using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Sandbox.Game.Entities;
using VRageMath;
using VRageRender;
using VRage.Utils;

namespace HDRClientRenderer
{
    // Client-owned rendering only. Declarative camera IDs become checked immutable
    // poses on the game thread; entities are never read from the render thread.
    internal sealed class DirectCameraCapture : IDisposable
    {
        const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        const string PatchId = "HDRClientRenderer.DirectCameraCapture";
        static DirectCameraCapture active;
        static int capturing;
        static int portalCapturing;
        [ThreadStatic] static bool copyingCapture;
        internal static bool IsCapturing { get { return Volatile.Read(ref capturing) != 0; } }
        internal static bool IsPortalCapturing { get { return Volatile.Read(ref portalCapturing) != 0; } }

        internal sealed class Snapshot
        {
            internal readonly long Camera, Generation;
            internal readonly string Texture;
            internal readonly int Width, Height;
            internal readonly MatrixD Pose;
            internal readonly double FovDegrees;
            internal readonly object EntityIdentity;
            internal readonly long Epoch;
            internal readonly int Profile, FullResolution;
            internal readonly long ContentRevision;
            readonly CaptureTileAtlas.Tile[] tiles;
            internal CaptureTileAtlas.Tile[] Tiles { get { return (CaptureTileAtlas.Tile[])tiles.Clone(); } }
            internal Snapshot(long camera, string texture, int size, MatrixD pose, long generation,
                double fov, object entityIdentity, long epoch)
            {
                Camera = camera; Texture = texture; Width = Height = size; Pose = pose;
                Generation = generation; FovDegrees = fov; EntityIdentity = entityIdentity; Epoch = epoch;
                FullResolution = size; tiles = CaptureTileAtlas.Uniform(size).Tiles;
            }
            internal Snapshot(long camera, string texture, CaptureTileAtlas atlas, MatrixD pose, long generation,
                double fov, object entityIdentity, long epoch, int profile, long contentRevision)
            {
                Camera = camera; Texture = texture; Width = atlas.Width; Height = atlas.Height; FullResolution = atlas.FullResolution;
                Pose = pose; Generation = generation; FovDegrees = fov; EntityIdentity = entityIdentity; Epoch = epoch;
                Profile = profile; ContentRevision = contentRevision; tiles = atlas.Tiles;
            }
        }
        sealed class PoseRequest
        {
            internal readonly MatrixD Pose;
            internal readonly object EntityIdentity;
            internal readonly int Profile;
            internal readonly PanoramaDensity Density;
            internal PoseRequest(MatrixD pose, object entityIdentity, int profile, PanoramaDensity density)
            { Pose = pose; EntityIdentity = entityIdentity; Profile = profile; Density = density; }
        }
        sealed class Work
        {
            internal DirectCameraCapturePolicy.Source Source;
            internal long Generation, Epoch;
            internal double Fov;
            internal int Resolution;
            internal PoseRequest Request;
        }
        sealed class Target
        {
            internal long Camera;
            internal string Name;
            internal object Texture;
            internal object Staging;
            internal string StagingName;
            internal int Slot, NextTile;
            internal CaptureTileAtlas Atlas;
            internal Work Batch;
            internal long ContentRevision;
        }

        readonly object gate = new object();
        readonly DirectCameraCapturePolicy policy = new DirectCameraCapturePolicy();
        // Only the renderer callback touches these resources.
        readonly Dictionary<long, Target> targets = new Dictionary<long, Target>();
        readonly List<MethodBase> patched = new List<MethodBase>();
        DirectCameraCaptureNative native;
        internal Func<object, object> MainSceneStarting { get; set; }
        internal Action<object, bool> MainSceneCompleted { get; set; }
        internal Action AfterPrimaryWork { get; set; }
        internal Action BeforePrimaryWork { get; set; }
        internal Action PrimaryPresentationCompleted { get; set; }
        object primaryPreparedDevice;long primaryPreparedEpoch=long.MinValue;bool primaryDeviceRetirementPending;
        internal Func<long, bool> CameraHidden { get; set; }
        internal System.Reflection.Assembly NativeAssembly { get { return native == null ? null : native.Assembly; } }
        internal long NativeEpoch { get { lock (gate) return epoch; } }
        internal bool PortalReady { get { return Ready && native.PortalReady; } }
        internal long PortalDeviceEpoch { get { return native == null ? 0 : native.PortalEpoch; } }
        internal string PortalIssue { get { return native == null ? "Native camera renderer unavailable." : native.PortalIssue; } }
        internal bool TryInstallPortals()
        { return Ready && native.TryInstallPortals(checked((int)NativeEpoch)); }
        internal bool PreparePortals() { return PortalReady && native.PreparePortals(); }
        internal long[] DemandedCameras()
        { lock (gate) return policy.All().Where(source => source.CaptureDemand).Select(source => source.Camera).ToArray(); }
        internal bool CapturePortal(object target, int resolution, PortalRayMapSpec spec, out string error)
        {
            error = null;
            if (!PortalReady || IsCapturing || !native.FrameIdle || spec == null || spec.Epoch != native.PortalEpoch)
            { error = PortalIssue ?? "Portal capture epoch is stale or renderer busy."; return false; }
            bool success;
            try
            {
                Interlocked.Increment(ref capturing);
                Interlocked.Increment(ref portalCapturing);
                double fov = 2 * Math.Atan(1 / Math.Max(1e-6, Math.Abs(spec.Projection.M22))) * 180 / Math.PI;
                success = native.Capture(target, resolution, spec.CapturePose, fov, 0, null, true, spec);
                if (!success) error = native.PortalIssue ?? "Portal coverage gate rejected the capture.";
            }
            catch (Exception failure) { error = failure.GetBaseException().Message; success = false; }
            finally { Interlocked.Decrement(ref portalCapturing); Interlocked.Decrement(ref capturing); }
            return success;
        }
        internal void WithIsolatedPass(Action<object> action)
        { if (!Ready || IsCapturing || !native.FrameIdle) throw new InvalidOperationException("Native isolated pass unavailable."); native.WithIsolatedPass(action); }
        internal void EnqueueCleanup(Action action)
        { var adapter = native; if (adapter != null) adapter.EnqueueCleanup(action); }
        ClientPixelBudget budget;
        ClientRenderSettings renderSettings=new ClientRenderSettings();
        internal void Configure(ClientRenderSettings settings)
        { if(settings==null||!settings.Valid)throw new ArgumentException("Invalid client settings.");
            lock(gate){renderSettings=settings;policy.RateCeiling=settings.CaptureRate;policy.RetainCompletedWhileLive=true;} }
        object scratch;
        int scratchSize;
        const string ScratchName = "HDR_DirectCamera_Scratch";
        Harmony harmony;
        Action<string> report;
        long epoch = 1, renderedEpoch;
        bool attempted, disposed;
        int reports;
        int traceSteps;
        bool tracedOwners;
        internal bool Ready { get { return native != null && native.Healthy && !disposed; } }
        internal string Describe()
        {
            lock(gate)
            {
                var text=new System.Text.StringBuilder("Camera detail:");
                foreach(var source in policy.All())
                {
                    var frame=source.Completed as Snapshot;
                    text.Append(" ").Append(source.Camera).Append(" requested ").Append(source.Resolution);
                    if(frame==null){text.Append(" pending;");continue;}
                    var tiles=frame.Tiles;int pixels=0;foreach(var tile in tiles)pixels+=tile.Pixels;
                    text.Append(" / atlas ").Append(frame.Width).Append('x').Append(frame.Height).Append(" / ").Append(tiles.Length).Append(" tiles / ").Append(pixels).Append(" sampled pixels;");
                }
                return text.ToString();
            }
        }
        internal void SetBudget(ClientPixelBudget shared) { budget = shared; }
        internal Func<double> Clock=null;
        double Now { get { return Clock==null?Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency:Clock(); } }

        internal void TryInstall(Action<string> log)
        {
            if (attempted || disposed) return;
            var renderer = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRageRender.MyRender11", false)).FirstOrDefault(t => t != null);
            if (renderer == null) return;
            // A hot reload must let the former owner's queued render cleanup finish
            // before reusing its fixed target names or replacing its global hooks.
            if (active != null && !ReferenceEquals(active, this)) return;
            attempted = true; report = log;
            try
            {
                var candidate = new DirectCameraCaptureNative(renderer.Assembly);
                candidate.Trace = Trace;
                harmony = new Harmony(PatchId); active = this;
                Patch(candidate.DrawScene, "BeforeScene", Priority.First);
                harmony.Patch(candidate.DrawScene, postfix: new HarmonyMethod(typeof(DirectCameraCapture).GetMethod("AfterScene", Static)) { priority = Priority.Last },
                    finalizer: new HarmonyMethod(typeof(DirectCameraCapture).GetMethod("FailedScene", Static)) { priority = Priority.Last });
                foreach (var method in candidate.SkipVoid)
                    Patch(method, "PreserveVoid", Priority.First);
                foreach (var method in candidate.SkipInt)
                    Patch(method, "PreserveInt", Priority.First);
                foreach (var method in candidate.BorrowMethods)
                { harmony.Patch(method, postfix: new HarmonyMethod(typeof(DirectCameraCapture).GetMethod("AfterBorrow", Static))); patched.Add(method); }
                foreach (var method in candidate.ReleaseMethods) Patch(method, "BeforeReleaseBorrowed", Priority.First);
                Patch(candidate.SupportsOcclusion, "PreserveOcclusion", Priority.First);
                Patch(candidate.ActorIsOccluded,"PreserveOcclusion",Priority.First);
                Patch(candidate.CheckDistanceCulling,"PreserveOcclusion",Priority.First);
                Patch(candidate.UpdateAfterCull, "BeforeComponentCull", Priority.First);
                Patch(candidate.UpdateWorldMatrix, "BeforeProxyMatrix", Priority.First);
                Patch(candidate.UpdateInstanceLods,"BeforeInstanceLods",Priority.First);
                Patch(candidate.UpdateCullProxies,"BeforeLegacyQuery",Priority.First);
                harmony.Patch(candidate.SchedulerDone, postfix: new HarmonyMethod(typeof(DirectCameraCapture).GetMethod("AfterSchedulerDone", Static)));
                patched.Add(candidate.SchedulerDone);
                Patch(candidate.SchedulerExecute, "BeforeSchedulerExecute", Priority.First);
                Patch(candidate.CullDone, "BeforeCullDone", Priority.First);
                harmony.Patch(candidate.CullDone, postfix: new HarmonyMethod(typeof(DirectCameraCapture).GetMethod("AfterCullDone", Static)));
                harmony.Patch(candidate.CullReset, postfix: new HarmonyMethod(typeof(DirectCameraCapture).GetMethod("AfterCullReset", Static)));
                patched.Add(candidate.CullReset);
                Patch(candidate.Particles.Run, "BeforeParticleRun", Priority.First);
                var vertexHook = typeof(DirectCameraCapture).GetMethod("ParticleVertexBridge", Static);
                harmony.Patch(candidate.Particles.VertexSet, prefix: new HarmonyMethod(vertexHook) { priority = Priority.First });
                patched.Add(candidate.Particles.VertexSet);
                harmony.Patch(candidate.CopyPass, prefix: new HarmonyMethod(typeof(DirectCameraCapture).GetMethod("BeforeCopy", Static)),
                    finalizer: new HarmonyMethod(typeof(DirectCameraCapture).GetMethod("AfterCopy", Static)));
                patched.Add(candidate.CopyPass);
                var blendHook = typeof(DirectCameraCapture).GetMethod("OpaqueBlendBridge", Static);
                harmony.Patch(candidate.SetBlend, prefix: new HarmonyMethod(blendHook) { priority = Priority.First });
                patched.Add(candidate.SetBlend);
                var queryPrefix = typeof(DirectCameraCapture).GetMethod("ShadowQueriesBridge", Static);
                harmony.Patch(candidate.ShadowQueries, prefix: new HarmonyMethod(queryPrefix) { priority = Priority.First });
                patched.Add(candidate.ShadowQueries);
                native = candidate;
                Log("Native camera capture installed; six camera sources, up to twelve sRGB atlases and one scratch target; configurable native passes per main frame and image cadence; complete cropped batches publish atomically. Use /hdr camera status.");
            }
            catch (Exception ex)
            {
                Unpatch(); native = null;
                Log("Native camera capture unavailable: " + ex.GetBaseException().Message);
            }
        }
        void Patch(MethodBase original, string prefixName, int priority)
        {
            harmony.Patch(original, prefix: new HarmonyMethod(typeof(DirectCameraCapture).GetMethod(prefixName, Static)) { priority = priority });
            patched.Add(original);
        }
        void Log(string message)
        { if (report != null) try { report(message); } catch { } }
        void Trace(string stage)
        {
            if (Interlocked.Increment(ref traceSteps) > 64) return;
            Log("Direct capture checkpoint: " + stage);
            // A native access violation can bypass managed finalizers and buffered
            // logging. Keep these first-attempt checkpoints durable and bounded.
            try { var log = MyLog.Default; if (log != null) log.Flush(); } catch { }
        }
        void TraceOwners()
        {
            if (tracedOwners) return; tracedOwners = true;
            foreach (var method in new[] { native.DrawScene, native.SetBlend, native.Particles.VertexSet })
            {
                var info = Harmony.GetPatchInfo(method);
                Trace(method.DeclaringType.Name + "." + method.Name + " prefixes=" + (info == null ? "none" :
                    string.Join(",", info.Prefixes.Select(p => p.owner + ":" + p.PatchMethod.Name))));
            }
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("CameraLCD.CameraViewRenderer", false); if (type == null) continue;
                try { var property = type.GetProperty("IsDrawing", Static); Trace("CameraLCD.IsDrawing=" + (property == null ? "unavailable" : property.GetValue(null).ToString())); }
                catch (Exception ex) { Trace("CameraLCD marker read failed: " + ex.GetType().Name); }
                break;
            }
        }

        // Called after the provider has rechecked local player access, power, entity
        // identity and visible source demand. Neither the PB nor a server calls it.
        internal Snapshot Request(long camera, MatrixD cameraWorld, double fovDegrees, int resolution,
            double rate = 30, object entityIdentity = null, int profile = 0, PanoramaDensity density = null)
        {
            if (!Ready || !ValidPose(cameraWorld) || profile < 0 || profile > 1) return null;
            lock (gate)
            {
                var previous = policy.Find(camera);
                long previousGeneration = previous == null ? 0 : previous.Generation;
                var previousRequest = previous == null ? null : previous.Payload as PoseRequest;
                if (previousRequest != null && (!ReferenceEquals(previousRequest.EntityIdentity, entityIdentity) || previousRequest.Profile != profile))
                    policy.Invalidate(previous);
                var source = policy.Request(camera, fovDegrees, resolution, rate, Now,
                    new PoseRequest(cameraWorld, entityIdentity, profile, density));
                if (source != null && source.Generation != previousGeneration)
                { CancelCameraBudget(camera); source.NativeTile = source.NativePixels = 0; }
                if (source != null && budget != null)
                {
                    if (source.NativePixels == 0)
                    { var proposed = PlanForBudget(density, resolution); if (proposed != null) source.NativePixels = proposed.At(0).Pixels; }
                    if (source.NativePixels > 0 && policy.Eligible(source, Now)) budget.Request("HDR.Camera." + camera + "." + source.NativeTile, ClientPixelBudget.Kind.Capture, source.NativePixels);
                    else CancelCameraBudget(camera);
                }
                return source == null || !policy.Valid(camera, source.Generation, Now) ? null : source.Completed as Snapshot;
            }
        }
        internal bool Valid(Snapshot snapshot)
        {
            if (!Ready || snapshot == null) return false;
            lock (gate)
            {
                var source = policy.Find(snapshot.Camera);
                var current = source == null ? null : source.Completed as Snapshot;
                return snapshot.Epoch == epoch && policy.Valid(snapshot.Camera, snapshot.Generation, Now) &&
                    current != null && current.Texture == snapshot.Texture && current.Width == snapshot.Width &&
                    current.Height == snapshot.Height && current.Profile == snapshot.Profile && current.FovDegrees == snapshot.FovDegrees &&
                    ReferenceEquals(current.EntityIdentity, snapshot.EntityIdentity);
            }
        }
        // The compositor resolves this on the render thread after this component's
        // capture prefix. Its pose therefore matches the pixels currently in the
        // reused target, including captures newer than the queued game-thread face.
        internal Snapshot Latest(long camera)
        {
            if (!Ready) return null;
            lock (gate)
            {
                var source = policy.Find(camera);
                return source != null && policy.Valid(camera, source.Generation, Now) ? source.Completed as Snapshot : null;
            }
        }
        internal void Revoke(long camera)
        {
            lock (gate)
            {
                var source = policy.Find(camera);
                if (source == null) return;
                source.DemandUntil = double.NegativeInfinity;
                policy.Invalidate(source); foreach (long removed in policy.Prune(Now)) CancelCameraBudget(removed);
                CancelCameraBudget(camera);
            }
        }
        internal void Warm(long camera)
        { lock (gate) { policy.Warm(camera, Now); CancelCameraBudget(camera); } }
        internal void Tick() { Prune(); }
        internal void Prune() { lock (gate) foreach (long camera in policy.Prune(Now)) CancelCameraBudget(camera); }
        internal void NewEpoch()
        {
            long retiredEpoch;
            lock (gate) { retiredEpoch = ++epoch; foreach (var source in policy.All()) CancelCameraBudget(source.Camera); policy.Clear(); }
            var renderer = native;
            if (renderer == null) return;
            try { renderer.EnqueueCleanup(() => RetireQueued(retiredEpoch)); }
            catch (Exception ex) { if (reports++ < 8) Log("Native camera cleanup queued for the next main frame: " + ex.GetBaseException().Message); }
        }
        void RetireQueued(long retiredEpoch)
        {
            try
            {
                lock (gate) if (retiredEpoch != epoch) return;
                try { ClearTargets(); } finally { if (disposed) native.DisposeNative(); else native.ReleaseDeviceResources(); }
                renderedEpoch = retiredEpoch;
            }
            catch (Exception ex) { if (reports++ < 8) Log("Native camera render-thread cleanup failed: " + ex.GetBaseException().Message); }
            finally { if (disposed) Unpatch(); }
        }

        // MAIN is proved by the engine's backbuffer argument, not by counting
        // arbitrary DrawGameScene calls. Nested captures cannot spend the budget.
        sealed class MainSceneTicket
        {
            internal DirectCameraCapture Owner;
            internal long Epoch, Frame;
            internal object Target;
            internal object Device;
            internal PortalPrimaryView View;
            internal object VisibilityTicket;
            internal bool Completed;
            internal bool PrimaryPresentation;
        }
        static void BeforeScene(object __0, out MainSceneTicket __state)
        {
            __state = null;
            var owner = active;
            if (owner == null || IsCapturing || owner.native == null || !owner.native.IsMainTarget(__0)) return;
            __state = new MainSceneTicket { Owner = owner, Epoch = owner.NativeEpoch, Frame = owner.native.Frame, Target = __0, Device = owner.native.DeviceIdentity };
            try { var callback = owner.MainSceneStarting; if (callback != null) __state.VisibilityTicket = callback(__0); }
            catch (Exception error) { if (owner.reports++ < 8) owner.Log("Display visibility preparation retained camera demand: " + error.GetBaseException().Message); }
            if (!owner.Ready || !owner.native.FrameIdle || owner.BeforePrimaryWork==null) return;
            if(owner.BeforePrimaryWork!=null)
            {
                try
                {
                    owner.primaryDeviceRetirementPending |= owner.native.DeviceChanged;
                    owner.native.PreparePrimaryDevice();
                    owner.primaryPreparedDevice=owner.native.DeviceIdentity;owner.primaryPreparedEpoch=owner.NativeEpoch;
                }
                catch(Exception error)
                {
                    if(!owner.native.Healthy||!owner.native.FrameIdle)throw;
                    if(owner.reports++<8)owner.Log("Optional portal device preparation held its previous image: "+error.GetBaseException().Message);
                    return;
                }
            }
            __state.View = owner.native.PrimaryView();
            if (__state.View == null) return;
            try
            {
                var callback = owner.BeforePrimaryWork;
                if (callback != null) { __state.PrimaryPresentation = true; callback(); }
            }
            catch (Exception error)
            {
                owner.EndPrimaryPresentation(__state);
                // Only a fully restored idle renderer may continue this primary
                // scene. Unsupported/warming capture paths simply hold pixels.
                if (!owner.native.Healthy || !owner.native.FrameIdle) throw;
                if (owner.reports++ < 8) owner.Log("Optional pre-scene provider held its previous image: " + error.GetBaseException().Message);
            }
            var restored = owner.native.PrimaryView();
            if (!owner.native.Healthy || !owner.native.FrameIdle || __state.Frame != owner.native.Frame || __state.Epoch != owner.NativeEpoch || !ReferenceEquals(__state.Device, owner.native.DeviceIdentity) || !owner.native.IsMainTarget(__state.Target) || restored == null || restored.Viewer != __state.View.Viewer || restored.Projection != __state.View.Projection || restored.Width != __state.View.Width || restored.Height != __state.View.Height || restored.PresentationWidth != __state.View.PresentationWidth || restored.PresentationHeight != __state.View.PresentationHeight || restored.Epoch != __state.View.Epoch)
            { owner.EndPrimaryPresentation(__state); throw new InvalidOperationException("Portal pre-scene work did not restore the primary renderer boundary."); }
        }
        static void AfterScene(MainSceneTicket __state)
        {
            // The postfix is reached only after the primary DrawGameScene has
            // consumed opaque, foliage and transparent command lists. A nested
            // auxiliary scene has no ticket and cannot start another capture.
            var ticket = __state; if (ticket == null) return;
            var owner = ticket.Owner;
            if (!ReferenceEquals(active, owner) || owner.native == null || owner.disposed || IsCapturing ||
                ticket.Epoch != owner.NativeEpoch || ticket.Frame != owner.native.Frame ||
                !owner.native.IsMainTarget(ticket.Target) || !owner.native.FrameIdle) return;
            ticket.Completed = true;
            try { var callback = owner.MainSceneCompleted; if (callback != null) callback(ticket.VisibilityTicket, true); }
            catch (Exception error) { if (owner.reports++ < 8) owner.Log("Display visibility result retained camera demand: " + error.GetBaseException().Message); }
            owner.EndPrimaryPresentation(ticket);
            owner.RenderMain();
            try { var callback = owner.AfterPrimaryWork; if (callback != null && owner.Ready && owner.native.FrameIdle) callback(); }
            catch (Exception error) { if (owner.reports++ < 8) owner.Log("Optional post-scene provider paused: " + error.GetBaseException().Message); }
        }
        static Exception FailedScene(Exception __exception, MainSceneTicket __state)
        {
            if (__exception != null && __state != null && !__state.Completed)
                try { var callback = __state.Owner.MainSceneCompleted; if (callback != null) callback(__state.VisibilityTicket, false); } catch { }
            if (__state != null) __state.Owner.EndPrimaryPresentation(__state);
            return __exception;
        }
        void EndPrimaryPresentation(MainSceneTicket ticket)
        {
            if (!ticket.PrimaryPresentation) return;
            if (native == null || !native.FrameIdle) throw new InvalidOperationException("Primary renderer workers or cull ownership did not retire before portal presentation cleanup.");
            ticket.PrimaryPresentation = false;
            var callback = PrimaryPresentationCompleted; if (callback != null) callback();
        }
        static bool PreserveVoid() { return !IsCapturing; }
        static MethodInfo ParticleVertexBridge(MethodBase original)
        { return DirectCameraCaptureBridge.Reference(original.GetParameters()[0].ParameterType,
            typeof(DirectCameraCaptureParticles).GetMethod("SelectVertex", Static), "HDRCaptureParticleVertex"); }
        static MethodInfo OpaqueBlendBridge(MethodBase original)
        { return DirectCameraCaptureBridge.Reference(original.GetParameters()[0].ParameterType,
            typeof(DirectCameraCapture).GetMethod("SelectCaptureBlend", Static), "HDRCaptureOpaqueBlend"); }
        static MethodInfo ShadowQueriesBridge(MethodBase original)
        { return DirectCameraCaptureBridge.Result(((MethodInfo)original).ReturnType,
            typeof(DirectCameraCapture).GetMethod("TryCapturedShadowQueries", Static), "HDRCaptureShadowQueries"); }
        static void BeforeCopy(object __0, out bool __state)
        {
            __state = copyingCapture;
            var owner = active;
            copyingCapture = IsCapturing && owner != null && ReferenceEquals(__0, owner.native.CaptureOutput);
        }
        static Exception AfterCopy(Exception __exception, bool __state)
        { copyingCapture = __state; return __exception; }
        static object SelectCaptureBlend(object original)
        { return copyingCapture && active != null ? active.native.OpaqueCopyBlend.GetValue(null) : original; }
        static void AfterSchedulerDone()
        { var owner = active; if (IsCapturing && owner != null) owner.native.SchedulerFinished = true; }
        static void BeforeSchedulerExecute()
        { var owner = active; if (IsCapturing && owner != null) owner.native.SchedulerEntered = true; }
        static void BeforeCullDone()
        { var owner = active; if (IsCapturing && owner != null) owner.native.CullingDoneAttempted = true; }
        static void AfterCullDone()
        { var owner = active; if (IsCapturing && owner != null) owner.native.CullingFinished = true; }
        static void AfterCullReset()
        { var owner = active; if (IsCapturing && owner != null) owner.native.CullingReset = true; }
        static bool BeforeParticleRun(object __0, object __1)
        {
            if (!IsCapturing) return true;
            var owner = active;
            if (owner == null || owner.native == null) return false;
            owner.native.Particles.DrawExisting(__0, __1); return false;
        }
        static void AfterBorrow(object __result)
        { var owner = active; if (IsCapturing && owner != null) owner.native.TrackBorrow(__result); }
        static void BeforeReleaseBorrowed(object __instance)
        { var owner = active; if (IsCapturing && owner != null) owner.native.TrackRelease(__instance); }
        static bool PreserveInt(ref int __result)
        { if (!IsCapturing) return true; __result = 0; return false; }
        static bool PreserveOcclusion(ref bool __result)
        { if (!IsCapturing) return true; __result = false; return false; }
        static void BeforeComponentCull(object __instance)
        { var owner = active; if (IsCapturing && owner != null) owner.native.RecordSceneMutation(__instance, false); }
        static void BeforeProxyMatrix(object __instance)
        { var owner = active; if (IsCapturing && owner != null) owner.native.RecordSceneMutation(__instance, true); }
        static bool BeforeInstanceLods(object __0)
        { if(!IsCapturing)return true;var owner=active;if(owner!=null)owner.native.PrepareInstanceLods(__0);return false; }
        static void BeforeLegacyQuery(object __0)
        { var owner=active;if(IsCapturing&&owner!=null)owner.native.PrepareLegacyQuery(__0); }
        static bool TryCapturedShadowQueries(out object result)
        {
            result = null;
            if (!IsCapturing) return false;
            var owner = active;
            if (owner == null || owner.native == null) return false;
            result = owner.native.EmptyShadowQueries; return true;
        }

        void RenderMain()
        {
            Work[] work;
            HashSet<long> demanded;
            long currentEpoch;
            lock (gate)
            {
                currentEpoch = epoch;
                foreach (long camera in policy.Prune(Now)) CancelCameraBudget(camera);
                // Existing native resources disappear as soon as demand is revoked.
                demanded = new HashSet<long>(targets.Keys.Where(id => policy.Find(id) != null));
                bool ready = !disposed && native.Healthy && native.FrameIdle;
                if (!ready) foreach (var source in policy.All()) CancelCameraBudget(source.Camera);
                var selected = policy.Candidates(Now, native.Frame, ready, false);
                work = selected.Select(source => new Work { Source = source, Generation = source.Generation,
                    Epoch = currentEpoch, Fov = source.FovDegrees, Resolution = source.Resolution,
                    Request = (PoseRequest)source.Payload }).ToArray();
            }
            try
            {
                if (renderedEpoch != currentEpoch || native.DeviceChanged || primaryDeviceRetirementPending)
                {
                    lock (gate)
                    {
                        foreach (var source in policy.All()) policy.Invalidate(source);
                        foreach (var item in work)
                            item.Generation = item.Source.Generation;
                    }
                    ClearTargets();
                    // Pre-primary device settlement already retired private
                    // state. Keep the newly published portal epoch/resources
                    // while ordinary camera atlas retirement stays post-scene.
                    if(primaryPreparedEpoch!=currentEpoch||!ReferenceEquals(primaryPreparedDevice,native.DeviceIdentity)||native.DeviceChanged)native.ReleaseDeviceResources();
                    primaryDeviceRetirementPending=false;renderedEpoch = currentEpoch;
                }
                foreach (long camera in targets.Keys.ToArray())
                    if (!demanded.Contains(camera)) DestroyTarget(camera);
                if (disposed) { ClearTargets(); native.ReleaseDeviceResources(); Unpatch(); return; }
                int passes = 0, passLimit=renderSettings.CapturePasses==0?DirectCameraCapturePolicy.MaxSources*CaptureTileAtlas.MaxTiles:renderSettings.CapturePasses;
                var finished=new HashSet<long>();
                for(int round=0;round<CaptureTileAtlas.MaxTiles&&passes<passLimit;round++)foreach (var item in work)
                {
                    if (!native.Healthy || passes >= passLimit) break;
                    if(finished.Contains(item.Source.Camera))continue;
                    bool hidden = false;
                    try { var visibility = CameraHidden; hidden = visibility != null && visibility(item.Source.Camera); } catch { }
                    if (hidden) { CancelCameraBudget(item.Source.Camera); finished.Add(item.Source.Camera); continue; }
                    if (Capture(item)) { passes++; if(item.Source.NativeTile==0)finished.Add(item.Source.Camera); }
                    else finished.Add(item.Source.Camera);
                }
            }
            catch (Exception ex)
            {
                if (reports++ < 8) Log("Native camera capture paused: " + ex.GetBaseException().Message);
            }
        }
        bool Capture(Work item)
        {
            if (!native.Healthy || !native.FrameIdle) return false;
            lock (gate) if (item.Epoch != epoch || !item.Source.CaptureDemand || !policy.Live(item.Source, item.Generation, Now) || item.Source.NativeTile==0&&!policy.Eligible(item.Source, Now)) return false;
            TraceOwners(); Trace("camera " + item.Source.Camera + " attempt; frame " + native.Frame + "; requested " + item.Resolution + "; FOV " + item.Fov);
            int size = native.BoundResolution(item.Resolution);
            if (size == 0) { CancelCameraBudget(item.Source.Camera); return false; }
            Target target;
            targets.TryGetValue(item.Source.Camera, out target);
            // A tighter runtime budget must not strand an oversized pending tile.
            // Discard staging only; the completed front survives replanning.
            if(target!=null&&target.Batch!=null&&!FitsBudget(target.Atlas))
            {CancelBudget(target);target.Batch=null;target.NextTile=0;lock(gate){item.Source.NativeTile=item.Source.NativePixels=0;}}
            // Continue one frozen-pose batch even if the latest game-thread demand
            // updates its pose. A new profile/configuration generation cancels it.
            var atlas = target != null && target.Batch != null && target.Batch.Generation == item.Generation && target.Atlas.Width==size && target.Atlas.Height==size && FitsBudget(target.Atlas) ? target.Atlas :
                PlanForBudget(renderSettings.Adaptive?item.Request.Density:null, size);
            if (atlas == null) { CancelCameraBudget(item.Source.Camera); return false; }
            // Keep one fixed-size front/staging canvas. Layout changes only affect
            // the staging descriptors, so the completed front survives refinement.
            atlas=atlas.OnCanvas(size,size);
            if (target != null && (target.Atlas.Width!=atlas.Width||target.Atlas.Height!=atlas.Height))
            {
                lock (gate) { policy.Invalidate(item.Source); item.Generation = item.Source.Generation; }
                DestroyTarget(target.Camera); target = null;
            }
            if (target == null)
            {
                if (targets.Count >= DirectCameraCapturePolicy.MaxSources) return false;
                int slot = 0;
                while (targets.Values.Any(t => t.Slot == slot)) slot++;
                string name = "HDR_DirectCamera_" + slot;
                object texture = null, staging = null;
                try { texture = native.CreateAtlasTarget(name, atlas.Width, atlas.Height);
                    staging = native.CreateAtlasTarget(name + "_Stage", atlas.Width, atlas.Height); }
                catch { if (texture != null) native.DestroyTarget(name); throw; }
                target = new Target { Camera = item.Source.Camera, Name = name, Texture = texture, Staging = staging,
                    StagingName = name + "_Stage", Atlas = atlas, Slot = slot };
                targets.Add(target.Camera, target);
            }
            if (target.Batch != null && target.Batch.Generation != item.Generation) { CancelBudget(target); target.Batch = null; }
            if (target.Batch == null) { target.Atlas=atlas;target.Batch = item; target.NextTile = 0; }
            var batch = target.Batch;
            var tile = target.Atlas.At(target.NextTile);
            string key = BudgetKey(target);
            lock (gate) { item.Source.NativeTile = target.NextTile; item.Source.NativePixels = tile.Pixels; }
            if (budget != null) { budget.Request(key, ClientPixelBudget.Kind.Capture, tile.Pixels);
                if (!budget.TrySpend(key, ClientPixelBudget.Kind.Capture, tile.Pixels)) return false; }
            lock (gate) if (target.NextTile==0&&!policy.RecordAttempt(item.Source, item.Generation, Now)) { CancelCameraBudget(item.Source.Camera); return false; }
            bool success = false, publishing = false;
            try
            {
                Interlocked.Increment(ref capturing);
                if (scratch == null || scratchSize < tile.Size)
                { if (scratch != null) native.DestroyTarget(ScratchName); scratch = null;
                    scratch = native.CreateTarget(ScratchName, tile.Size); scratchSize = tile.Size; }
                if (target.NextTile == 0)
                { native.ClearAtlas(target.Staging); if (budget != null) budget.RecordBandwidth((long)atlas.Width * atlas.Height); }
                success = native.Capture(scratch, tile.Size, batch.Request.Pose, batch.Fov, batch.Request.Profile, tile, false);
                if (budget != null) budget.RecordBandwidth((long)scratchSize * scratchSize);
                if (success) { native.CopyTile(scratch, target.Staging, tile);
                    if (budget != null) budget.RecordBandwidth(tile.Pixels * 2L);
                    target.NextTile++;
                    if (target.NextTile == target.Atlas.Count)
                    { publishing=true;native.PublishAtlas(target.Staging, target.Texture);
                        if (budget != null) { long mipPixels = CaptureTileAtlas.MipPixels(atlas.Width, atlas.Height);
                            budget.RecordBandwidth(mipPixels * 2L + (mipPixels - (long)atlas.Width * atlas.Height) * 5L); }
                    } }
            }
            catch (Exception ex)
            { success = false; if (reports++ < 8) Log("Native camera " + item.Source.Camera + " capture failed: " + ex.GetBaseException().Message); }
            finally { Interlocked.Decrement(ref capturing); if (budget != null) budget.Complete(key); }
            if (!success)
            {
                // Partial work touched only staging. The completed front and its
                // matching pose/layout remain usable while the next batch retries.
                // A failed commit itself cannot guarantee the old front contents.
                if(publishing)lock(gate)policy.Invalidate(item.Source);
                target.Batch = null;
                lock (gate) { item.Source.NativeTile = item.Source.NativePixels = 0; }
                return true;
            }
            if (target.NextTile < target.Atlas.Count)
            {
                var next = target.Atlas.At(target.NextTile);
                lock (gate) { item.Source.NativeTile = target.NextTile; item.Source.NativePixels = next.Pixels; }
                // Continuation work registers its pixels at the next main-frame
                // capture attempt. Image cadence only limits new batch starts.
                return true;
            }
            var snapshot = new Snapshot(item.Source.Camera, target.Name, target.Atlas, batch.Request.Pose,
                batch.Generation, batch.Fov, batch.Request.EntityIdentity, batch.Epoch, batch.Request.Profile, ++target.ContentRevision);
            target.Batch = null;
            lock (gate) { item.Source.NativeTile = 0; item.Source.NativePixels = target.Atlas.At(0).Pixels; }
            lock (gate)
            {
                if (batch.Epoch == epoch) policy.Complete(item.Source, batch.Generation, snapshot, Now);
            }
            Trace("camera " + item.Source.Camera + " completed; pose/pixels snapshot published");
            return true;
        }
        void DestroyTarget(long camera)
        {
            Target target;
            if (!targets.TryGetValue(camera, out target)) return;
            targets.Remove(camera); CancelBudget(target);
            try { native.DestroyTarget(target.Name); } finally { native.DestroyTarget(target.StagingName); }
        }
        static string BudgetKey(Target target) { return "HDR.Camera." + target.Camera + "." + target.NextTile; }
        void CancelCameraBudget(long camera) { if (budget != null) for (int i = 0; i < CaptureTileAtlas.MaxTiles; i++) budget.Cancel("HDR.Camera." + camera + "." + i); }
        void CancelBudget(Target target) { CancelCameraBudget(target.Camera); }
        bool FitsBudget(CaptureTileAtlas atlas)
        { if (budget == null) return true; int limit = budget.PixelLimit; for (int i = 0; i < atlas.Count; i++) if (atlas.At(i).Pixels > limit) return false; return true; }
        CaptureTileAtlas PlanForBudget(PanoramaDensity density, int maximum)
        {
            while (maximum >= CaptureTileAtlas.MinTile)
            { var atlas = CaptureTileAtlas.PlanAdaptive(density, maximum,renderSettings.TileLimit,renderSettings.TilePassCost); if (FitsBudget(atlas)) return atlas; maximum /= 2; }
            return null;
        }
        void ClearTargets()
        {
            var failures = new List<Exception>();
            foreach (long id in targets.Keys.ToArray()) try { DestroyTarget(id); } catch (Exception ex) { failures.Add(ex.GetBaseException()); }
            if (scratch != null) { scratch = null; scratchSize = 0; try { native.DestroyTarget(ScratchName); } catch (Exception ex) { failures.Add(ex.GetBaseException()); } }
            if (failures.Count != 0) throw new AggregateException("Native camera target cleanup failed.", failures);
        }
        void Unpatch()
        {
            if (harmony != null)
                foreach (var method in patched) try { harmony.Unpatch(method, HarmonyPatchType.All, PatchId); } catch { }
            patched.Clear(); harmony = null;
            if (ReferenceEquals(active, this)) active = null;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; NewEpoch();
            // EnqueueUpdate releases resources on the render thread, including hot
            // reloads without a subsequent main view. No game-thread COM call.
            if (native == null) Unpatch();
        }
        internal static bool ValidPose(MatrixD pose)
        {
            return DirectCameraCapturePolicy.Finite(pose.Translation.X) && DirectCameraCapturePolicy.Finite(pose.Translation.Y) &&
                DirectCameraCapturePolicy.Finite(pose.Translation.Z) && DirectCameraCapturePolicy.Finite(pose.Forward.X) &&
                DirectCameraCapturePolicy.Finite(pose.Forward.Y) && DirectCameraCapturePolicy.Finite(pose.Forward.Z) &&
                DirectCameraCapturePolicy.Finite(pose.Up.X) && DirectCameraCapturePolicy.Finite(pose.Up.Y) &&
                DirectCameraCapturePolicy.Finite(pose.Up.Z) && pose.Forward.LengthSquared() > .99 &&
                pose.Forward.LengthSquared() < 1.01 && pose.Up.LengthSquared() > .99 && pose.Up.LengthSquared() < 1.01 &&
                Math.Abs(Vector3D.Dot(pose.Up, pose.Forward)) < .001;
        }
        internal static MatrixD OpticalPose(MyCameraBlock camera)
        {
            if (camera == null || camera.Closed) throw new ArgumentException("Camera is unavailable.");
            return DirectCameraCaptureNative.OpticalPose(camera);
        }
    }
}
