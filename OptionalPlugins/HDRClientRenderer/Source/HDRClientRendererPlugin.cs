using System;
using System.Globalization;
using System.Threading;
using Sandbox.Game.Components;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Plugins;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
using GameBlock = Sandbox.ModAPI.IMyTerminalBlock;
using PbBlock = Sandbox.ModAPI.IMyProgrammableBlock;
using SurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
using Surface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace HDRClientRenderer
{
    // Optional trusted client installation. Never belongs in a Workshop mod or a dedicated server.
    public sealed partial class HDRClientRendererPlugin : IPlugin, IDisposable
    {
        private const long DisplayDiscovery = 481770100, DisplayRegistration = 481770101;
        private const long RasterDiscovery = 481770110, RasterRegistration = 481770111;
        private const long CubeCaptureDiscovery = 481770120, CubeCaptureRegistration = 481770121;
        private const string ProviderId = "lcd-texture";
        private const string PanoramaProviderId = "camera-panorama";
        private readonly Func<string, object[], object> displayEndpoint;
        private readonly Func<string, object[], object> panoramaEndpoint;
        private readonly Func<string, object[], object> rasterEndpoint;
        private readonly RasterStore raster;
        private readonly LcdStore lcd;
        private readonly CameraLcdCompatibility cameraCompatibility = new CameraLcdCompatibility();
        private readonly NativeTextureCopy textureCopy = new NativeTextureCopy();
        private readonly NativePanoramaGpu panoramaGpu = new NativePanoramaGpu();
        private readonly NativeRasterUpload rasterUpload = new NativeRasterUpload();
        private readonly ClientPixelBudget pixelBudget;
        private ClientPixelBudgetNative budgetNative;
        private readonly DirectCameraCapture directCapture = new DirectCameraCapture();
        private readonly PanoramaStore panorama;
        private readonly CubeCaptureGuard cubeCapture = new CubeCaptureGuard();
        private readonly Input.PointerServices pointerServices = new Input.PointerServices(Log);
        private string lastCubeIssue;
        private int cubeReports;
        private object world, renderThread;
        private object renderSettings;
        private Func<string, object[], object> hdrService, rasterService;
        private bool initialized, registered, registering, disposed;
        private int gameThreadId, pendingDeviceReset;
        private string lastGate;
        private int diagnosticCount;
        private bool firstUpdate = true;

        private static void Log(string message)
        {
            try { var log = MyLog.Default;
                if (log != null) { log.WriteLine("[HDRClientRenderer] " + message); log.Flush(); } }
            catch { }
        }
        private void Gate(string state)
        {
            if (lastGate == state) return;
            lastGate = state;
            if (diagnosticCount++ < 32) Log(state);
        }

        private sealed class RenderBackend : RasterStore.IBackend, RasterStore.IReadiness
        {
            readonly HDRClientRendererPlugin plugin;
            internal RenderBackend(HDRClientRendererPlugin plugin){this.plugin=plugin;}
            public void Create(string texture, int width, int height, byte[] rgba,long serial)
            { if(!plugin.rasterUpload.Enqueue(texture,width,height,rgba,serial))throw new InvalidOperationException("UI render upload unavailable."); }
            public void Reset(string texture,int width,int height,byte[] rgba,long serial){Create(texture,width,height,rgba,serial);}
            public bool Ready(string texture,long serial){return plugin.rasterUpload.TargetReady(texture,serial);}
            public void Destroy(string texture) { plugin.rasterUpload.Cancel(texture);MyRenderProxy.DestroyGeneratedTexture(texture); }
        }
        private sealed class LcdWorld : LcdStore.IWorld
        {
            private readonly HDRClientRendererPlugin plugin;
            internal LcdWorld(HDRClientRendererPlugin plugin) { this.plugin = plugin; }
            public bool Active(long anchor, long caller, string sourceId)
            {
                if(plugin.IsModSourceKey(caller))return plugin.ModSourceCurrent(anchor,caller,ProviderId,sourceId)!=null;
                try { if (plugin.hdrService == null) return false;
                    object result = plugin.hdrService("source-state", new object[] { ProviderId, anchor, caller, sourceId });
                    return result is bool && (bool)result; }
                catch { return false; }
            }
            public bool Authorized(long anchor, long caller, long source)
            { return plugin.IsModSourceKey(caller)?plugin.ModSourceEntityAuthorized(anchor,caller,source,ProviderId):HDRClientRendererPlugin.Authorized(anchor, caller, source); }
            public bool TryTexture(long source, int index, out string texture, out int width, out int height)
            {
                Vector2I size;
                bool found = plugin.TryLcdTexture(source, index, out texture, out size);
                width = size.X; height = size.Y;
                return found;
            }
            public void CreateTarget(string target, int width, int height)
            {
                CreateReadyGpuTarget(target,width,height);
            }
            public bool TargetReady(string target) { return plugin.textureCopy.TargetReady(target); }
            public void CopyTexture(string source, string target, int width, int height)
            {
                if(!plugin.textureCopy.Enqueue(source,target,width,height))
                    throw new InvalidOperationException("Direct LCD GPU copy is unavailable or request is invalid.");
            }
            public void DestroyTarget(string target) { plugin.textureCopy.Cancel(target); MyRenderProxy.DestroyGeneratedTexture(target); }
        }
        private sealed class PanoramaWorld : PanoramaStore.IWorld
        {
            readonly HDRClientRendererPlugin plugin;
            internal PanoramaWorld(HDRClientRendererPlugin plugin) { this.plugin = plugin; }
            public bool Active(long anchor, long caller, string sourceId)
            {
                if(plugin.IsModSourceKey(caller))return plugin.ModSourceCurrent(anchor,caller,PanoramaProviderId,sourceId)!=null;
                try { if (plugin.hdrService == null || !plugin.directCapture.Ready || !plugin.panoramaGpu.Ready || !plugin.cameraCompatibility.Ready) return false;
                    object state = plugin.hdrService("source-state", new object[] { PanoramaProviderId, anchor, caller, sourceId });
                    return state is bool && (bool)state; }
                catch { return false; }
            }
            public bool TrySettings(long anchor, long caller, string sourceId, out PanoramaStore.Settings settings)
            {
                settings = null;
                if(plugin.IsModSourceKey(caller))
                {
                    var consumer=plugin.ModSourceCurrent(anchor,caller,PanoramaProviderId,sourceId);if(consumer==null)return false;
                    var local=(MyTuple<MyTuple<double,double,double,int>,int>)consumer.State.Data;var values=local.Item1;
                    settings=new PanoramaStore.Settings{FovDegrees=values.Item1,FeatherDegrees=values.Item2,Saturation=values.Item3,CaptureResolution=values.Item4,Profile=local.Item2,Rate=consumer.State.Demand.Item3};return settings.Valid;
                }
                if (plugin.hdrService == null) return false;
                object value = plugin.hdrService("source-panoramasettings", new object[] { PanoramaProviderId, anchor, caller, sourceId });
                if (!(value is MyTuple<double, double, double, int>)) return false;
                var tuple = (MyTuple<double, double, double, int>)value;
                settings = new PanoramaStore.Settings { FovDegrees = tuple.Item1, FeatherDegrees = tuple.Item2,
                    Saturation = tuple.Item3, CaptureResolution = tuple.Item4 };
                object profile=plugin.hdrService("source-renderprofile",new object[]{PanoramaProviderId,anchor,caller,sourceId});
                if(profile!=null){if(!(profile is int))return false;settings.Profile=(int)profile;}
                return settings.Valid;
            }
            public double Now { get { return System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency; } }
            public bool DisplayDemand(long anchor,long caller,string sourceId,out int width,out int height,out double rate)
            {
                width=height=0;rate=0;
                if(plugin.IsModSourceKey(caller))
                {var consumer=plugin.ModSourceCurrent(anchor,caller,PanoramaProviderId,sourceId);if(consumer==null)return false;var demand=consumer.State.Demand;width=demand.Item1;height=demand.Item2;rate=demand.Item3;return demand.Item4;}
                try
                {
                    if(plugin.hdrService==null)return false;
                    object value=plugin.hdrService("source-demand",new object[]{PanoramaProviderId,anchor,caller,sourceId});
                    if(!(value is MyTuple<int,int,double,bool>))return false;
                    var demand=(MyTuple<int,int,double,bool>)value;
                    width=demand.Item1;height=demand.Item2;rate=demand.Item3;
                    return demand.Item4&&width>=16&&height>=16&&width<=4096&&height<=4096&&PanoramaStore.Finite(rate)&&rate>0;
                }
                catch{return false;}
            }
            public bool TryFace(long anchor, long caller, long cameraId, PanoramaStore.Settings settings, PanoramaDensity density, out PanoramaStore.Face face)
            {
                face = null;
                if (!Authorized(anchor, caller, cameraId)) { plugin.directCapture.Revoke(cameraId); return false; }
                var camera = MyAPIGateway.Entities.GetEntityById(cameraId) as Sandbox.Game.Entities.MyCameraBlock;
                var anchorBlock = MyAPIGateway.Entities.GetEntityById(anchor) as GameBlock;
                if (camera == null || anchorBlock == null) return false;
                var snapshot = plugin.directCapture.Request(cameraId, DirectCameraCapture.OpticalPose(camera), settings.FovDegrees, settings.CaptureResolution, settings.Rate, camera,settings.Profile,density);
                if (snapshot == null || !plugin.directCapture.Valid(snapshot)) return false;
                var inverse = MatrixD.Invert(anchorBlock.WorldMatrix);
                var right = Vector3D.Normalize(Vector3D.TransformNormal(snapshot.Pose.Right, inverse));
                var up = Vector3D.Normalize(Vector3D.TransformNormal(snapshot.Pose.Up, inverse));
                var forward = Vector3D.Normalize(Vector3D.TransformNormal(snapshot.Pose.Forward, inverse));
                double tangent = Math.Tan(snapshot.FovDegrees * Math.PI / 360);
                face = new PanoramaStore.Face { Camera = snapshot.Camera, Generation = snapshot.Generation,
                    Texture = snapshot.Texture, Width = snapshot.Width, Height = snapshot.Height,
                    RightX = right.X, RightY = right.Y, RightZ = right.Z, UpX = up.X, UpY = up.Y, UpZ = up.Z,
                    ForwardX = forward.X, ForwardY = forward.Y, ForwardZ = forward.Z,
                    TanHorizontal = tangent, TanVertical = tangent, Evidence = snapshot,Profile=snapshot.Profile,ContentRevision=snapshot.ContentRevision,
                    Tiles=CopyTiles(snapshot.Tiles) };
                face.AnchorInverse = new[] { inverse.M11, inverse.M12, inverse.M13, inverse.M21, inverse.M22, inverse.M23, inverse.M31, inverse.M32, inverse.M33 };
                return face.Valid;
            }
            public bool Authorized(long anchor, long caller, long camera)
            {
                bool allowed = plugin.IsModSourceKey(caller)?plugin.ModSourceEntityAuthorized(anchor,caller,camera,PanoramaProviderId):CameraAuthorized(anchor, caller, camera);
                if (!allowed) plugin.directCapture.Revoke(camera);
                return allowed;
            }
            public PanoramaDemand Demand(long anchor, long caller, string sourceId, long[] cameras, PanoramaStore.Settings settings)
            {
                var fallback = PanoramaDemand.Full(cameras.Length, settings.CaptureResolution);
                if(plugin.IsModSourceKey(caller))return fallback;
                try
                {
                    var block = MyAPIGateway.Entities.GetEntityById(anchor) as GameBlock;
                    if (block == null || plugin.hdrService == null) return fallback;
                    var inverse = MatrixD.Invert(block.WorldMatrix); var bases = new double[cameras.Length * 11];
                    double tangent = Math.Tan(settings.FovDegrees * Math.PI / 360);
                    for (int i = 0; i < cameras.Length; i++)
                    {
                        var camera = MyAPIGateway.Entities.GetEntityById(cameras[i]) as Sandbox.Game.Entities.MyCameraBlock;
                        if (camera == null) return fallback;
                        var pose = DirectCameraCapture.OpticalPose(camera);
                        var right = Vector3D.Normalize(Vector3D.TransformNormal(pose.Right, inverse));
                        var up = Vector3D.Normalize(Vector3D.TransformNormal(pose.Up, inverse));
                        var forward = Vector3D.Normalize(Vector3D.TransformNormal(pose.Forward, inverse));
                        int start = i * 11;
                        bases[start] = right.X; bases[start + 1] = right.Y; bases[start + 2] = right.Z;
                        bases[start + 3] = up.X; bases[start + 4] = up.Y; bases[start + 5] = up.Z;
                        bases[start + 6] = forward.X; bases[start + 7] = forward.Y; bases[start + 8] = forward.Z;
                        bases[start + 9] = tangent; bases[start + 10] = tangent;
                    }
                    object value = plugin.hdrService("source-cameradensity", new object[] { PanoramaProviderId, anchor, caller, sourceId, bases });
                    if (!(value is MyTuple<int, double[], bool>)) return fallback;
                    var tuple = (MyTuple<int, double[], bool>)value;
                    return PanoramaDemand.Read(cameras.Length, settings.CaptureResolution, tuple.Item1, tuple.Item2, tuple.Item3);
                }
                catch { return fallback; }
            }
            public void Warm(long camera) { plugin.directCapture.Warm(camera); }
            public bool ValidFace(long anchor, long caller, PanoramaStore.Face face, PanoramaStore.Settings settings)
            {
                if (!Authorized(anchor, caller, face.Camera)) { plugin.directCapture.Revoke(face.Camera); return false; }
                var snapshot = face.Evidence as DirectCameraCapture.Snapshot;
                return snapshot != null && snapshot.Camera == face.Camera && snapshot.FovDegrees == settings.FovDegrees &&
                    snapshot.Profile<=settings.Profile&&
                    ReferenceEquals(snapshot.EntityIdentity, MyAPIGateway.Entities.GetEntityById(face.Camera)) &&
                    plugin.directCapture.Valid(snapshot);
            }
            public void CreateTarget(string target, int width, int height)
            {
                CreateReadyGpuTarget(target,width,height);
            }
            public bool TargetReady(string target) { return plugin.panoramaGpu.TargetReady(target); }
            public bool Compose(string target, int width, int height, PanoramaStore.Face[] faces, PanoramaStore.Settings settings)
            { return plugin.panoramaGpu.Enqueue(target, width, height, faces, settings); }
            public bool TargetHealthy(string target) { return plugin.panoramaGpu.Ready && plugin.panoramaGpu.Healthy(target); }
            public void DestroyTarget(string target) { plugin.panoramaGpu.Cancel(target); MyRenderProxy.DestroyGeneratedTexture(target); }
            public void RetireFaces(PanoramaStore.Face[] faces)
            { foreach (var face in faces) if (!plugin.panorama.Demands(face.Camera)) plugin.directCapture.Revoke(face.Camera); }
        }
        public bool CanCapture { get { return registered && directCapture.Ready && cameraCompatibility.Ready && Ready(); } }
        static void CreateReadyGpuTarget(string target,int width,int height)
        {
            // A base-only byte[] is unsafe for this engine's full-mip Reset path.
            // The render-thread adapter corrects mip count, activates with null
            // subresource data and clears/completes it before publication.
            MyRenderProxy.CreateGeneratedTexture(target,width,height,MyGeneratedTextureType.RGBA,1,null,true,true);
        }
        public HDRClientRendererPlugin()
        {
            displayEndpoint = DisplayEndpoint;
            panoramaEndpoint = PanoramaEndpoint;
            portalEndpoint = PortalEndpoint;
            rasterEndpoint = RasterEndpoint;
            pixelBudget=new ClientPixelBudget(()=>budgetNative==null?default(ClientPixelBudget.Frame):budgetNative.Read());
            ApplyClientSettings();
            rasterUpload.SetBudget(pixelBudget);textureCopy.SetBudget(pixelBudget);panoramaGpu.SetBudget(pixelBudget);directCapture.SetBudget(pixelBudget);
            raster = new RasterStore(new RenderBackend(this));
            lcd = new LcdStore(new LcdWorld(this));
            panorama = new PanoramaStore(new PanoramaWorld(this));
            panoramaGpu.SetResolver(ResolveRenderFace);
        }
        public void Init(object gameInstance)
        {
            if (initialized || disposed) throw new InvalidOperationException("Plugin lifecycle cannot restart.");
            initialized = true;
            Gate("Init entered; client renderer " + typeof(HDRClientRendererPlugin).Assembly.GetName().Version + "; post-primary capture, optional display occlusion and private portal providers");
            var module=typeof(HDRClientRendererPlugin).Assembly.ManifestModule;
            Log("Loaded client assembly "+module.Assembly.GetName().Version+"; MVID "+module.ModuleVersionId+"; path "+module.Assembly.Location+"; PID "+System.Diagnostics.Process.GetCurrentProcess().Id);
        }
        public void Update()
        {
            if (!initialized || disposed) return;
            if (firstUpdate) { firstUpdate = false; Gate("Update entered"); }
            gameThreadId = Thread.CurrentThread.ManagedThreadId;
            pointerServices.Update();
            UpdateRenderControls();
            if(budgetNative==null)try{budgetNative=ClientPixelBudgetNative.TryBind();}catch(Exception error){Gate("shared pixel budget unavailable: "+error.GetBaseException().Message);}
            cameraCompatibility.TryInstall(Log);
            textureCopy.TryInstall(Log);
            rasterUpload.TryInstall(Log);
            directCapture.TryInstall(Log);
            UpdateNativeProviders();
            panoramaGpu.TryInstall(Log);
            raster.Tick();
            lcd.Tick();
            panorama.Tick();
            directCapture.Tick();
            if (Interlocked.Exchange(ref pendingDeviceReset, 0) != 0) LeaveWorld();
            string issue = ReadinessIssue();
            if (issue != null) { Gate("waiting: " + issue); if (registered || world != null) LeaveWorld(); return; }
            object session, thread, settings;
            try { session = MyAPIGateway.Session; thread = MyRenderProxy.RenderThread;
                settings = MyRenderProxy.RenderThread.CurrentSettings; }
            catch (Exception ex) { Gate("renderer state error: " + ex.GetType().Name);
                if (registered || world != null) LeaveWorld(); return; }
            if (!ReferenceEquals(world, session) || !ReferenceEquals(renderThread, thread) ||
                renderSettings == null || !renderSettings.Equals(settings))
            {
                LeaveWorld();
                world = session; renderThread = thread; renderSettings = settings;
            }
            if (!registered) Register();
            if(modSourceConsumers!=null)modSourceConsumers.Prune();
            lcd.Prune();
            panorama.Prune();
            panorama.RefreshDemands();
            directCapture.Prune();
        }
        // A client host with a device-recreated notification can invalidate leases
        // without calling the renderer from its notification thread.
        public void SignalDeviceReset() { Interlocked.Exchange(ref pendingDeviceReset, 1); }
        private static string ReadinessIssue()
        {
            try
            {
                if (MyAPIGateway.Utilities == null) return "utilities absent";
                if (MyAPIGateway.Utilities.IsDedicated) return "dedicated server";
                if (MyAPIGateway.Session == null) return "session absent";
                if (MyAPIGateway.Entities == null) return "entities absent";
                if (MyRenderProxy.RenderThread == null) return "render thread absent";
                return null;
            }
            catch (Exception ex) { return "gateway error " + ex.GetType().Name; }
        }
        private static bool Ready() { return ReadinessIssue() == null; }
        private void Register()
        {
            if (registering) return;
            string materialIssue;
            try { materialIssue = StaticMaterialIssue(); }
            catch (Exception ex) { Gate("material inspection error: " + ex.GetType().Name); return; }
            if (materialIssue != null) { Gate("waiting: " + materialIssue); return; }
            Gate("materials ready; registering services");
            registering = true;
            try
            {
                registered = true;
                MyAPIGateway.Utilities.RegisterMessageHandler(DisplayDiscovery, ReceiveDisplayDiscovery);
                MyAPIGateway.Utilities.RegisterMessageHandler(RasterDiscovery, ReceiveRasterDiscovery);
                MyAPIGateway.Utilities.RegisterMessageHandler(CubeCaptureRegistration, ReceiveCubeCapture);
                SendRegistrations();
                Gate("registration messages sent");
            }
            catch (Exception ex) { Gate("registration error: " + ex.GetType().Name); LeaveWorld(); }
            finally { registering = false; }
        }
        private void SendRegistrations()
        {
            if (!registered || !Ready()) return;
            MyAPIGateway.Utilities.SendModMessage(DisplayRegistration,
                new MyTuple<string, int, Func<string, object[], object>>(ProviderId, 2, displayEndpoint));
            MyAPIGateway.Utilities.SendModMessage(DisplayRegistration,
                new MyTuple<string, int, Func<string, object[], object>>(PanoramaProviderId, 2, panoramaEndpoint));
            if (portals != null && portals.Ready) MyAPIGateway.Utilities.SendModMessage(DisplayRegistration,
                new MyTuple<string, int, Func<string, object[], object>>(PortalProviderId, 2, portalEndpoint));
            MyAPIGateway.Utilities.SendModMessage(RasterRegistration,
                new MyTuple<int, Func<string, object[], object>>(1, rasterEndpoint));
            MyAPIGateway.Utilities.SendModMessage(CubeCaptureDiscovery, null);
            NegotiateModSourceConsumers();
        }
        private void ReceiveCubeCapture(object message)
        {
            if(!registered||!(message is MyTuple<int,Func<long,int,bool>>))return;
            var registration=(MyTuple<int,Func<long,int,bool>>)message;if(registration.Item2==null)return;
            if(registration.Item1==1){cubeCapture.Register(registration.Item2);Log("360 Camera Capture verification service registered.");lastCubeIssue=null;}
            else if(registration.Item1==0)cubeCapture.Unregister(registration.Item2);
        }
        private void ReceiveDisplayDiscovery(object message)
        {
            if (!registered || !(message is Func<string, object[], object>)) return;
            hdrService = (Func<string, object[], object>)message;
            Gate("HDR display discovery received");
            if (!registering) SendRegistrations();
        }
        private void ReceiveRasterDiscovery(object message)
        {
            if (!registered || !(message is Func<string, object[], object>)) return;
            rasterService = (Func<string, object[], object>)message;
            Gate("HDR raster discovery received");
            if (!registering) SendRegistrations();
        }
        private void LeaveWorld()
        {
            LeaveModSourceConsumers();
            if (registered && MyAPIGateway.Utilities != null)
            {
                try { if (hdrService != null) hdrService("unregister", new object[] { ProviderId, displayEndpoint }); } catch { }
                try { if (hdrService != null) hdrService("unregister", new object[] { PanoramaProviderId, panoramaEndpoint }); } catch { }
                try { if (hdrService != null) hdrService("unregister", new object[] { PortalProviderId, portalEndpoint }); } catch { }
                try { if (rasterService != null) rasterService("unregister", new object[] { rasterEndpoint }); } catch { }
                try { MyAPIGateway.Utilities.UnregisterMessageHandler(DisplayDiscovery, ReceiveDisplayDiscovery); } catch { }
                try { MyAPIGateway.Utilities.UnregisterMessageHandler(RasterDiscovery, ReceiveRasterDiscovery); } catch { }
                try { MyAPIGateway.Utilities.UnregisterMessageHandler(CubeCaptureRegistration, ReceiveCubeCapture); } catch { }
            }
            registered = false; hdrService = null; rasterService = null;
            LeaveNativeProviders();
            cubeCapture.Clear();lastCubeIssue=null;cubeReports=0;
            lcd.NewEpoch();
            panorama.NewEpoch();
            panoramaGpu.Clear();
            directCapture.NewEpoch();
            rasterUpload.Clear();pixelBudget.Clear();
            raster.NewEpoch(); world = null; renderThread = null; renderSettings = null;
            Gate("world or renderer left; services inactive");
        }
        public void Dispose() { if (disposed) return; pointerServices.Dispose(); UnregisterRenderControls();LeaveWorld(); displayOcclusion.Dispose();
            if (portals != null) portals.Dispose(); if (portalGpu != null) portalGpu.Dispose();
            panoramaGpu.Dispose(); directCapture.Dispose(); textureCopy.Dispose();rasterUpload.Dispose(); cameraCompatibility.Dispose(); disposed = true; Gate("disposed"); }

        private static string StaticMaterialIssue()
        {
            for (int i = 0; i < RasterStore.MaxSlots; i++)
                foreach (string prefix in new[] { "HDR_ClientRaster_", "HDR_ClientLcd_" })
                {
                    string name = prefix + i;
                    var id = MyStringId.GetOrCompute(name);
                    MyTransparentMaterial material;
                    if (!MyTransparentMaterials.TryGetMaterial(id, out material) || material == null)
                        return "material missing " + name;
                    if (material.Id != id) return "material identity mismatch " + name;
                    if (material.TextureType != MyTransparentMaterialTextureType.FileTexture)
                        return "material type mismatch " + name;
                    if (material.Texture != name) return "material texture mismatch " + name;
                    if (material.UseAtlas) return "material atlas mismatch " + name;
                }
            for (int i = 0; i < PanoramaStore.MaxSlots; i++)
            {
                string name = "HDR_ClientPanorama_" + i;
                var id = MyStringId.GetOrCompute(name); MyTransparentMaterial material;
                if (!MyTransparentMaterials.TryGetMaterial(id, out material) || material == null || material.Id != id ||
                    material.TextureType != MyTransparentMaterialTextureType.FileTexture || material.Texture != name || material.UseAtlas)
                    return "panorama material missing or mismatched " + name;
            }
            for (int i = 0; i < PortalProvider.MaxSources; i++)
            {
                string name = "HDR_ClientPortal_" + i; var id = MyStringId.GetOrCompute(name); MyTransparentMaterial material;
                if (!MyTransparentMaterials.TryGetMaterial(id, out material) || material == null || material.Id != id ||
                    material.TextureType != MyTransparentMaterialTextureType.FileTexture || material.Texture != name || material.UseAtlas)
                    return "portal material missing or mismatched " + name;
            }
            return null;
        }
        private object RasterEndpoint(string command, object[] args)
        {
            if (!registered || rasterService == null || gameThreadId != Thread.CurrentThread.ManagedThreadId || !Ready()) return null;
            if (command == "caps") return pixelBudget.PixelLimit<=0?null:(object)new MyTuple<int, int, int, int>(RasterStore.MaxWidth,
                RasterStore.MaxHeight, Math.Min(RasterStore.MaxPixels,pixelBudget.PixelLimit), RasterStore.MaxSlots);
            if (command == "valid") return args != null && args.Length == 1 && raster.Valid(args[0]);
            if (command == "release") return args != null && args.Length == 1 && raster.Release(args[0]);
            if (command == "upload")
            {
                if (args == null || args.Length != 5 || !(args[0] is string) || !(args[1] is long) ||
                    !(args[2] is int) || !(args[3] is int) || !(args[4] is byte[])) return null;
                if((long)(int)args[2]*(int)args[3]>pixelBudget.PixelLimit)return null;
                var entry = raster.Upload((string)args[0], (long)args[1], (int)args[2], (int)args[3], (byte[])args[4]);
                return entry == null || !raster.Valid(entry.Lease) ? null : (object)new MyTuple<string, object>(entry.Material, entry.Lease);
            }
            return null;
        }
        private object DisplayEndpoint(string command, object[] args)
        {
            if (!registered || hdrService == null || gameThreadId != Thread.CurrentThread.ManagedThreadId || !Ready()) return null;
            if (command == "begin" || command == "end") return true;
            if (command == "release") return args != null && args.Length == 1 && lcd.Release(args[0]);
            if (command == "valid")
            {
                if (args == null || args.Length != 4 || !(args[0] is long) || !(args[1] is long) || !(args[2] is string)) return false;
                return lcd.Valid(args[3], (long)args[0], (long)args[1], (string)args[2]);
            }
            if (command != "frame" || args == null || (args.Length != 14 && args.Length != 16) || !(args[0] is long) ||
                !(args[1] is long) || !(args[2] is string) || !(args[3] is string)) return null;
            long anchor = (long)args[0], caller = (long)args[1];
            string sourceId = (string)args[2], screenId = (string)args[3];
            int width = 256, height = 144;
            if (args.Length == 16)
            {
                if (!(args[14] is int) || !(args[15] is int)) return null;
                width = (int)args[14]; height = (int)args[15];
            }
            long sourceEntity; int index;
            if (!ParseSource(sourceId, out sourceEntity, out index)) return null;
            int requestedWidth=width,requestedHeight=height;
            if(!LcdStore.BoundSize(width,height,out width,out height))return null;
            if(!ClientPixelBudget.BoundSize(width,height,16,pixelBudget.PixelLimit,out width,out height))return null;
            var lease = lcd.Acquire(anchor, caller, sourceId, screenId, sourceEntity, index, width, height);
            if (lease == null) return null;
            return new MyTuple<int, object, object, bool, double>(2,
                new MyTuple<string, Vector2I, long>(lease.Material,
                    new Vector2I(lease.Width, lease.Height), lease.Version), lease,
                    width!=requestedWidth||height!=requestedHeight, 60d);
        }
        private static bool ParseSource(string id, out long entityId, out int index)
        {
            entityId = 0; index = -1;
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            int colon = id.IndexOf(':');
            return colon > 0 && colon == id.LastIndexOf(':') &&
                long.TryParse(id.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out entityId) &&
                int.TryParse(id.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out index) &&
                entityId > 0 && index >= 0 && index < 32;
        }
        private object PanoramaEndpoint(string command, object[] args)
        {
            if (!registered || hdrService == null || gameThreadId != Thread.CurrentThread.ManagedThreadId || !Ready()) return null;
            if (command == "begin" || command == "end") return true;
            if (command == "release") return args != null && args.Length == 1 && panorama.Release(args[0]);
            if (command == "valid")
            {
                if (args == null || args.Length != 4 || !(args[0] is long) || !(args[1] is long) || !(args[2] is string)) return false;
                return panorama.Valid(args[3], (long)args[0], (long)args[1], (string)args[2]);
            }
            if (command != "frame" || args == null || (args.Length != 14 && args.Length != 16) || !(args[0] is long) ||
                !(args[1] is long) || !(args[2] is string) || !(args[3] is string)) return null;
            int width = 1024, height = 512;
            if (args.Length == 16)
            {
                if (!(args[14] is int) || !(args[15] is int)) return null;
                width = (int)args[14]; height = (int)args[15];
            }
            int requestedWidth = width, requestedHeight = height;
            if (!PanoramaStore.BoundSize(width, height, out width, out height)) return null;
            if(!ClientPixelBudget.BoundSize(width,height,16,pixelBudget.PixelLimit,out width,out height))return null;
            var lease = panorama.Acquire((long)args[0], (long)args[1], (string)args[2], (string)args[3], width, height);
            if (lease == null) return null;
            return new MyTuple<int, object, object, bool, double>(2,
                new MyTuple<string, Vector2I, long>(lease.Material, new Vector2I(lease.Width, lease.Height), lease.Version), lease,
                width != requestedWidth || height != requestedHeight, lease.RefreshRate);
        }
        private static bool Authorized(long anchorId, long callerId, long sourceId)
        {
            if (!Ready() || MyAPIGateway.Session.Player == null ||
                MyAPIGateway.Session.Player.Character == null) return false;
            var anchor = MyAPIGateway.Entities.GetEntityById(anchorId) as GameBlock;
            var caller = MyAPIGateway.Entities.GetEntityById(callerId) as PbBlock;
            var source = MyAPIGateway.Entities.GetEntityById(sourceId) as GameBlock;
            if (anchor == null || caller == null || source == null || anchor.Closed || caller.Closed || source.Closed ||
                !anchor.IsWorking || !source.IsWorking || caller.OwnerId == 0 || !caller.IsSameConstructAs(anchor) ||
                !caller.IsSameConstructAs(source) || !anchor.HasPlayerAccess(caller.OwnerId) ||
                !source.HasPlayerAccess(caller.OwnerId)) return false;
            var viewer = MyAPIGateway.Session.Player.Character;
            return Vector3D.DistanceSquared(viewer.GetPosition(), anchor.GetPosition()) <= 3600;
        }
        private static bool CameraAuthorized(long anchorId, long callerId, long sourceId)
        {
            if (!Authorized(anchorId, callerId, sourceId)) return false;
            var player = MyAPIGateway.Session.Player;
            var anchor = MyAPIGateway.Entities.GetEntityById(anchorId) as GameBlock;
            var caller = MyAPIGateway.Entities.GetEntityById(callerId) as PbBlock;
            var camera = MyAPIGateway.Entities.GetEntityById(sourceId) as Sandbox.ModAPI.IMyCameraBlock;
            return player != null && player.IdentityId != 0 && anchor != null && caller != null && camera != null && caller.IsWorking &&
                caller.HasPlayerAccess(player.IdentityId) && anchor.HasPlayerAccess(player.IdentityId) && camera.HasPlayerAccess(player.IdentityId);
        }
        private PanoramaStore.Face ResolveRenderFace(PanoramaStore.Face face)
        {
            // Resolve the most recent complete post-primary capture, preserving
            // its matching pose even when a physical camera has since moved.
            var latest = directCapture.Latest(face.Camera);
            var prior = face.Evidence as DirectCameraCapture.Snapshot;
            if (latest == null || prior == null || !directCapture.Valid(latest) || latest.Generation != face.Generation ||
                latest.Texture != face.Texture || latest.Width != face.Width || latest.Height != face.Height ||
                latest.FovDegrees != prior.FovDegrees || latest.Profile!=prior.Profile || !ReferenceEquals(latest.EntityIdentity, prior.EntityIdentity) ||
                face.AnchorInverse == null || face.AnchorInverse.Length != 9) return null;
            var inverse = MatrixD.Identity; var a = face.AnchorInverse;
            for (int i = 0; i < a.Length; i++) if (!PanoramaStore.Finite(a[i])) return null;
            inverse.M11 = a[0]; inverse.M12 = a[1]; inverse.M13 = a[2]; inverse.M21 = a[3]; inverse.M22 = a[4];
            inverse.M23 = a[5]; inverse.M31 = a[6]; inverse.M32 = a[7]; inverse.M33 = a[8];
            var right = Vector3D.Normalize(Vector3D.TransformNormal(latest.Pose.Right, inverse));
            var up = Vector3D.Normalize(Vector3D.TransformNormal(latest.Pose.Up, inverse));
            var forward = Vector3D.Normalize(Vector3D.TransformNormal(latest.Pose.Forward, inverse));
            var current = face.Copy();
            current.RightX = right.X; current.RightY = right.Y; current.RightZ = right.Z;
            current.UpX = up.X; current.UpY = up.Y; current.UpZ = up.Z;
            current.ForwardX = forward.X; current.ForwardY = forward.Y; current.ForwardZ = forward.Z; current.Evidence = latest;
            current.Tiles=CopyTiles(latest.Tiles);current.Profile=latest.Profile;current.ContentRevision=latest.ContentRevision;
            return current.Valid ? current : null;
        }
        static PanoramaTile[] CopyTiles(CaptureTileAtlas.Tile[] tiles)
        {
            if(tiles==null||tiles.Length<1||tiles.Length>CaptureTileAtlas.MaxTiles)return new PanoramaTile[0];
            var result=new PanoramaTile[tiles.Length];
            for(int i=0;i<tiles.Length;i++){var tile=tiles[i];result[i]=new PanoramaTile(tile.SourceMinU,tile.SourceMinV,tile.SourceMaxU,tile.SourceMaxV,tile.AtlasMinU,tile.AtlasMinV,tile.AtlasMaxU,tile.AtlasMaxV);}
            return result;
        }
        private bool TryLcdTexture(long entityId,int index,out string texture,out Vector2I size)
        {
            texture=null;size=default(Vector2I);
            var source=MyAPIGateway.Entities.GetEntityById(entityId) as GameBlock;if(source==null)return false;
            string issue;
            if(!cubeCapture.CanRelay(entityId,index,source.CustomData,out issue))
            {
                if(issue!=lastCubeIssue&&cubeReports++<8)
                {lastCubeIssue=issue;Log("360 camera relay waiting: "+issue);MyAPIGateway.Utilities.ShowMessage("HDR Client Renderer",issue);}
                return false;
            }
            return GetLcdTexture(entityId,index,out texture,out size);
        }
        private static bool GetLcdTexture(long entityId, int index, out string texture, out Vector2I size)
        {
            texture = null; size = default(Vector2I);
            var source = MyAPIGateway.Entities.GetEntityById(entityId) as GameBlock;
            var entity = source as MyEntity;
            var render = entity == null ? null : entity.Render as MyRenderComponentScreenAreas;
            if (source == null || render == null || source.Closed) return false;
            Surface surface = null;
            var provider = source as SurfaceProvider;
            if (provider != null && index < provider.SurfaceCount) surface = provider.GetSurface(index);
            else if (provider == null && index == 0) surface = source as Surface;
            if (surface == null) return false;
            var pixels = surface.TextureSize;
            if (pixels.X < 1 || pixels.Y < 1 || pixels.X > 8192 || pixels.Y > 8192) return false;
            size = new Vector2I((int)pixels.X, (int)pixels.Y);
            texture = render.GenerateOffscreenTextureName(entityId, index);
            return !string.IsNullOrEmpty(texture);
        }
    }
}
