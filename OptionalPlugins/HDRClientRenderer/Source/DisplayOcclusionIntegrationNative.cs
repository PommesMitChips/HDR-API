using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using VRage;
using VRageMath;

namespace HDRClientRenderer
{
    // Root capture owner supplies primary invocation tickets and consumes this
    // adapter BEFORE its post-primary capture dispatch. This class's sole hook
    // submits enclosure queries; it never dispatches source camera rendering.
    internal sealed class DisplayOcclusionIntegrationNative : IDisposable
    {
        const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        sealed class ConsumerProof
        { internal DisplayOcclusion.Stamp Stamp; internal DisplayOcclusion.Footprint Footprint; }
        sealed class CameraProof
        { internal DisplayOcclusionInventory Inventory; internal readonly Dictionary<string, ConsumerProof> Consumers = new Dictionary<string, ConsumerProof>(StringComparer.Ordinal); }
        sealed class MainTicket
        {
            internal long Frame, Epoch;
            internal object Target, Device, GBuffer, Depth, ViewToken = new object(), DepthToken = new object();
            internal Matrix Projection;
            internal Vector3D Camera;
            internal Vector2I Size;
            internal bool Submitted, Completed;
            internal readonly Dictionary<long, CameraProof> Cameras = new Dictionary<long, CameraProof>();
        }
        static DisplayOcclusionIntegrationNative active;
        Harmony harmony;
        MethodInfo opaqueBoundary, prefix, enqueue;
        PropertyInfo backbuffer, device, nativeFrame, stereo, depth, size;
        FieldInfo gbuffer, environment, matrices, projection, camera, complementary;
        Assembly assembly;
        Func<long[]> requestedCameras;
        Func<long, object> sourceInventory;
        Action<string> report;
        DisplayOcclusion policy;
        DisplayOcclusionRendererNative renderer;
        DisplayOcclusionQueryNative queries;
        MainTicket current;
        bool enabled, disposed;
        int failures;

        internal bool Ready { get { return !disposed && harmony != null; } }
        internal bool ProofAvailable
        { get { var ticket = current; var draw = renderer; return enabled && ticket != null && ticket.Completed && draw != null && draw.CurrentFrameOpaqueProof; } }
        internal string StatusText { get { return ProofAvailable ? "Current primary opaque proof available; only completed zero results suppress every known consumer." : "Display occlusion retains camera demand until complete current primary proof is available."; } }
        internal bool TryInstall(Assembly renderAssembly, Func<long[]> cameras, Func<long, object> inventory, Action<string> log)
        {
            if (disposed || renderAssembly == null || cameras == null || inventory == null) return false;
            if (Ready) return true;
            try
            {
                assembly = renderAssembly; requestedCameras = cameras; sourceInventory = inventory; report = log;
                if (!DisplayOcclusionNative.OrderShape(assembly)) throw new InvalidOperationException("Current primary opaque submission ordering changed.");
                var render = assembly.GetType("VRageRender.MyRender11", true);
                backbuffer = Need(render.GetProperty("Backbuffer", S)); device = Need(render.GetProperty("DeviceInstance", S));
                nativeFrame = Need(assembly.GetType("VRageRender.MyCommon", true).GetProperty("FrameCounter", S));
                stereo = Need(assembly.GetType("VRageRender.MyStereoRender", true).GetProperty("Enable", S));
                complementary = Need(render.GetField("UseComplementaryDepthBuffer", S));
                gbuffer = Need(assembly.GetType("VRage.Render11.Resources.MyGBuffer", true).GetField("Main", S));
                depth = Need(gbuffer.FieldType.GetProperty("DepthStencil", I));
                size = Need(assembly.GetType("VRage.Render11.Resources.IResource", true).GetProperty("Size", I));
                environment = Need(render.GetField("Environment", S)); matrices = Need(environment.FieldType.GetField("Matrices", I));
                projection = Need(matrices.FieldType.GetField("ViewProjectionAt0", I)); camera = Need(matrices.FieldType.GetField("CameraPosition", I));
                if (projection.FieldType != typeof(Matrix) || camera.FieldType != typeof(Vector3D) || nativeFrame.PropertyType != typeof(long)) throw new InvalidOperationException("Display opaque view ABI changed.");
                enqueue = Need(render.GetMethod("EnqueueUpdate", S, null, new[] { typeof(Action) }, null));
                opaqueBoundary = Need(assembly.GetType("VRage.Render11.GBufferResolve.MyGBufferResolver", true).GetMethod("ConsumeWork", S, null, Type.EmptyTypes, null));
                prefix = typeof(DisplayOcclusionIntegrationNative).GetMethod("OpaquePrefix", S);
                harmony = new Harmony("hdr.client.display-occlusion.current-opaque"); active = this;
                harmony.Patch(opaqueBoundary, prefix: new HarmonyMethod(prefix));
                return true;
            }
            catch (Exception ex) { Error("Display occlusion integration unavailable: " + ex.GetBaseException().Message); Dispose(); return false; }
        }
        internal void SetEnabled(bool value)
        { enabled = value; if (policy != null) policy.SetEnabled(value); if (!value) { current = null; if (renderer != null) renderer.Invalidate(); } }
        internal object BeginMain(object target)
        {
            current = null;
            if (!Ready || !enabled || DirectCameraCapture.IsCapturing || target == null) return null;
            try
            {
                if (!ReferenceEquals(target, backbuffer.GetValue(null)) || (bool)stereo.GetValue(null) || !(bool)complementary.GetValue(null)) return null;
                if (renderer == null)
                {
                    renderer = new DisplayOcclusionRendererNative(assembly);
                    queries = new DisplayOcclusionQueryNative(assembly, renderer);
                    policy = new DisplayOcclusion(queries); policy.SetEnabled(true);
                }
                renderer.Invalidate(); queries.DrainRetired();
                object buffer = gbuffer.GetValue(null), dev = device.GetValue(null);
                if (buffer == null || dev == null) return null;
                object raw = depth.GetValue(buffer), view = matrices.GetValue(environment.GetValue(null));
                if (raw == null || view == null) return null;
                var ticket = new MainTicket { Frame = (long)nativeFrame.GetValue(null), Epoch = policy.Epoch, Target = target, Device = dev, GBuffer = buffer, Depth = raw,
                    Size = (Vector2I)size.GetValue(raw), Projection = (Matrix)projection.GetValue(view), Camera = (Vector3D)camera.GetValue(view) };
                policy.ObserveMainStarting(ticket.Frame, ticket.Size.X, ticket.Size.Y, buffer);
                if (policy.CurrentPhase != DisplayOcclusion.Phase.BeforeMainScene) return null;
                current = ticket; return ticket;
            }
            catch (Exception ex) { Invalidate(); Error("Display occlusion primary observation failed: " + ex.GetBaseException().Message); return null; }
        }
        static void OpaquePrefix() { var owner = active; if (owner != null) owner.SubmitOpaque(); }
        void SubmitOpaque()
        {
            var ticket = current;
            if (ticket == null || ticket.Submitted || !Current(ticket)) return;
            ticket.Submitted = true;
            try
            {
                policy.ObservePrimaryOpaqueCompleted(ticket.Frame, ticket.GBuffer);
                var common = Stamp(ticket, new object());
                renderer.Bind(common, ticket.Depth, stamp => Current(ticket) && common.SameFrame(stamp));
                if (!policy.MarkOpaqueProofReady(common)) return;
                var cameras = requestedCameras();
                if (cameras == null || cameras.Length > DisplayOcclusion.MaxEntries) return;
                foreach (long id in cameras.Distinct())
                {
                    if (id == 0) continue;
                    var inventory = ReadInventory(sourceInventory(id)); if (inventory == null) continue;
                    var proof = new CameraProof { Inventory = inventory };
                    foreach (var consumer in inventory.Consumers)
                    {
                        DisplayOcclusion.Footprint footprint;
                        if (!Project(ticket, consumer.WorldTriangles, out footprint)) continue;
                        var stamp = Stamp(ticket, consumer.Geometry);
                        proof.Consumers.Add(consumer.Key, new ConsumerProof { Stamp = stamp, Footprint = footprint });
                        policy.Submit(QueryKey(id, consumer.Key), stamp, footprint);
                    }
                    ticket.Cameras.Add(id, proof);
                }
            }
            catch (Exception ex) { Invalidate(); Error("Display occlusion enclosure submission failed: " + ex.GetBaseException().Message); }
        }
        internal void CompleteMain(object invocationTicket, bool succeeded)
        {
            var ticket = invocationTicket as MainTicket;
            if (ticket == null || !ReferenceEquals(current, ticket)) return;
            if (!succeeded || !Current(ticket)) { Invalidate(); return; }
            policy.ObserveMainCompleted(ticket.Frame, ticket.GBuffer, true);
            ticket.Completed = ticket.Submitted;
        }
        internal bool CameraHidden(long cameraId)
        {
            var ticket = current; CameraProof proof;
            if (ticket == null || !ticket.Completed || !Current(ticket) || !ticket.Cameras.TryGetValue(cameraId, out proof)) return false;
            try
            {
                var inventory = ReadInventory(sourceInventory(cameraId));
                bool hidden = proof.Inventory.AllHidden(inventory, consumer =>
                {
                    ConsumerProof p;
                    if (!proof.Consumers.TryGetValue(consumer.Key, out p)) return new DisplayOcclusion.Decision(DisplayOcclusion.Status.InvalidEvidence);
                    return policy.Probe(QueryKey(cameraId, consumer.Key), p.Stamp, p.Footprint);
                });
                // A game Draw publication can arrive while nonblocking result
                // reads run. Revalidate the entire immutable inventory once more
                // before returning a camera-wide suppression decision.
                return hidden && Current(ticket) && proof.Inventory.Same(ReadInventory(sourceInventory(cameraId)));
            }
            catch (Exception ex) { Error("Display occlusion consumer validation failed: " + ex.GetBaseException().Message); return false; }
        }
        bool Current(MainTicket ticket)
        {
            if (disposed || !enabled || DirectCameraCapture.IsCapturing || !ReferenceEquals(ticket, current) || policy == null || ticket.Epoch != policy.Epoch ||
                ticket.Frame != (long)nativeFrame.GetValue(null) || !ReferenceEquals(ticket.Target, backbuffer.GetValue(null)) ||
                !ReferenceEquals(ticket.Device, device.GetValue(null)) || !ReferenceEquals(ticket.GBuffer, gbuffer.GetValue(null)) ||
                !ReferenceEquals(ticket.Depth, depth.GetValue(ticket.GBuffer)) || (bool)stereo.GetValue(null) || !(bool)complementary.GetValue(null) ||
                ticket.Size != (Vector2I)size.GetValue(ticket.Depth)) return false;
            var view = matrices.GetValue(environment.GetValue(null));
            return ticket.Projection == (Matrix)projection.GetValue(view) && ticket.Camera == (Vector3D)camera.GetValue(view);
        }
        static DisplayOcclusion.Stamp Stamp(MainTicket t, object geometry)
        { return new DisplayOcclusion.Stamp(t.Epoch, t.Frame, t.Size.X, t.Size.Y, t.ViewToken, geometry, t.DepthToken); }
        static string QueryKey(long cameraId, string key)
        { string result = cameraId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + key; return result.Length <= 128 ? result : null; }
        static bool Project(MainTicket ticket, double[] world, out DisplayOcclusion.Footprint footprint)
        {
            footprint = default(DisplayOcclusion.Footprint);
            var vertices = new DisplayOcclusionProjection.Vertex[world.Length / 3]; var m = ticket.Projection;
            for (int i = 0; i < vertices.Length; i++)
            {
                double x = world[i * 3] - ticket.Camera.X, y = world[i * 3 + 1] - ticket.Camera.Y, z = world[i * 3 + 2] - ticket.Camera.Z;
                if (Math.Abs(x) > 10000 || Math.Abs(y) > 10000 || Math.Abs(z) > 10000) return false;
                double w = x * m.M14 + y * m.M24 + z * m.M34 + m.M44;
                if (w < .5) return false;
                vertices[i] = new DisplayOcclusionProjection.Vertex(x * m.M11 + y * m.M21 + z * m.M31 + m.M41,
                    x * m.M12 + y * m.M22 + z * m.M32 + m.M42, x * m.M13 + y * m.M23 + z * m.M33 + m.M43, w);
            }
            return DisplayOcclusionProjection.TryEnclose(vertices, ticket.Size.X, ticket.Size.Y, out footprint);
        }
        static DisplayOcclusionInventory ReadInventory(object raw)
        {
            if (!(raw is MyTuple<bool, object, MyTuple<string, object, double[]>[]>)) return null;
            var tuple = (MyTuple<bool, object, MyTuple<string, object, double[]>[]>)raw;
            if (!tuple.Item1 || tuple.Item3 == null || tuple.Item3.Length > DisplayOcclusion.MaxEntries) return null;
            var consumers = new DisplayOcclusionInventory.Consumer[tuple.Item3.Length];
            for (int i = 0; i < consumers.Length; i++) consumers[i] = new DisplayOcclusionInventory.Consumer(tuple.Item3[i].Item1, tuple.Item3[i].Item2, tuple.Item3[i].Item3);
            return DisplayOcclusionInventory.Read(tuple.Item1, tuple.Item2, consumers);
        }
        void Invalidate() { current = null; if (renderer != null) renderer.Invalidate(); if (policy != null) policy.NewEpoch(); }
        internal void NewEpoch() { Invalidate(); }
        void Error(string message) { if (failures++ < 8 && report != null) report(message); }
        public void Dispose()
        {
            if (disposed) return; disposed = true; enabled = false; current = null;
            if (ReferenceEquals(active, this)) active = null;
            if (harmony != null && opaqueBoundary != null && prefix != null) try { harmony.Unpatch(opaqueBoundary, prefix); } catch { }
            harmony = null;
            if (policy != null) policy.Dispose();
            if (renderer != null) renderer.Invalidate();
            if (enqueue != null && (queries != null || renderer != null))
            {
                Action retire = () =>
                {
                    try { if (queries != null) queries.Dispose(); }
                    catch (Exception ex) { Error("Display occlusion private-query retirement failed: " + ex.GetBaseException().Message); }
                    finally
                    {
                        try { if (renderer != null) renderer.Dispose(); }
                        catch (Exception ex) { Error("Display occlusion enclosure retirement failed: " + ex.GetBaseException().Message); }
                        queries = null; renderer = null;
                    }
                };
                try { enqueue.Invoke(null, new object[] { retire }); } catch (Exception ex) { Error("Display occlusion render retirement could not be queued: " + ex.GetBaseException().Message); }
            }
        }
        static T Need<T>(T value) where T : class { return value ?? throw new InvalidOperationException("Display occlusion native seam ABI changed."); }
    }
}
