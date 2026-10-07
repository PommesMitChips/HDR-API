using System;
using System.Collections.Generic;
using System.Threading;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using GameBlock = Sandbox.ModAPI.IMyTerminalBlock;
using PbBlock = Sandbox.ModAPI.IMyProgrammableBlock;

namespace HDRClientRenderer
{
    public sealed partial class HDRClientRendererPlugin
    {
        const string PortalProviderId = "native-portal";
        Func<string, object[], object> portalEndpoint;
        PortalProvider portals;
        PortalNativeGpu portalGpu;
        readonly DisplayOcclusionIntegrationNative displayOcclusion = new DisplayOcclusionIntegrationNative();
        readonly object inventoryGate = new object();
        Dictionary<long, object> occlusionInventories = new Dictionary<long, object>();
        bool providersAttempted;
        int providerErrors;

        void UpdateNativeProviders()
        {
            if (!directCapture.Ready) return;
            if (!providersAttempted)
            {
                providersAttempted = true;
                displayOcclusion.TryInstall(directCapture.NativeAssembly, directCapture.DemandedCameras, ReadOcclusionInventory, Log);
                directCapture.MainSceneStarting = displayOcclusion.BeginMain;
                directCapture.MainSceneCompleted = displayOcclusion.CompleteMain;
                directCapture.CameraHidden = displayOcclusion.CameraHidden;
                displayOcclusion.SetEnabled(clientSettings.DisplayOcclusion);
                try
                {
                    if (directCapture.TryInstallPortals())
                    {
                        portalGpu = new PortalNativeGpu(directCapture.NativeAssembly, directCapture.CapturePortal,
                            directCapture.WithIsolatedPass, directCapture.EnqueueCleanup,
                            () => directCapture.PortalDeviceEpoch, () => directCapture.PortalReady, () => directCapture.PortalIssue);
                        portals = new PortalProvider(new PortalWorld(this), portalGpu);
                        portals.SetBudget(pixelBudget);
                        Log("Native portal declaration and private GPU compositor installed; individual captures require complete shader coverage.");
                    }
                    else Log("Native portals unavailable: " + directCapture.PortalIssue);
                }
                catch (Exception error) { Log("Native portal binding unavailable: " + error.GetBaseException().Message); }
                if(portals!=null&&portalGpu!=null)
                {
                    directCapture.BeforePrimaryWork = RenderNativeProviders;
                    directCapture.PrimaryPresentationCompleted = () => { if (portalGpu != null) portalGpu.EndPrimary(); };
                }
            }
            var next = new Dictionary<long, object>();
            if (registered && hdrService != null && clientSettings.DisplayOcclusion)
                foreach (long camera in directCapture.DemandedCameras())
                    try {
                        var value = hdrService("source-occlusion", new object[] { camera });
                        var fresh = hdrService("source-occlusion-fresh", new object[] { camera }) as Func<object, bool>;
                        if (value != null && fresh != null) next[camera] = new MyTuple<object, Func<object, bool>>(value, fresh);
                    } catch { }
            lock (inventoryGate) occlusionInventories = next;
            if (portals != null) portals.Prune();
        }
        object ReadOcclusionInventory(long camera)
        {
            object value; lock (inventoryGate) if (!occlusionInventories.TryGetValue(camera, out value)) return null;
            if (!(value is MyTuple<object, Func<object, bool>>)) return null;
            var cached = (MyTuple<object, Func<object, bool>>)value;
            if (!(cached.Item1 is MyTuple<bool, object, MyTuple<string, object, double[]>[]>)) return null;
            var inventory = (MyTuple<bool, object, MyTuple<string, object, double[]>[]>)cached.Item1;
            try { return inventory.Item1 && cached.Item2(inventory.Item2) ? cached.Item1 : null; } catch { return null; }
        }
        void RenderNativeProviders()
        {
            if (!registered || !Ready() || portals == null || portalGpu == null) return;
            portalGpu.PreparePrimary();
            if (portals.HasDemand && directCapture.PreparePortals()) portals.RenderPending();
            // Freeze mappings only after all bounded capture/copy work has
            // restored the primary view, before its transparent jobs are built.
            portalGpu.BeginPrimary();
        }
        void LeaveNativeProviders()
        {
            lock (inventoryGate) occlusionInventories.Clear();
            displayOcclusion.NewEpoch();
            if (portals != null) portals.ClearWorld();
        }
        string NativeProviderStatus()
        {
            return "Display occlusion " + (clientSettings.DisplayOcclusion ? "on: " + displayOcclusion.StatusText : "off") +
                "; portals " + (portals == null ? "unavailable: " + directCapture.PortalIssue : portals.Reason);
        }
        internal static bool PortalProjectionFromCamera(MatrixD cameraProjection, out MatrixD projection)
        {
            projection = cameraProjection;
            if (!PortalProjection.Finite(projection) || Math.Abs(projection.Determinant()) < 1e-18) return false;
            if (PortalProjection.ComplementaryProjection(projection)) return true;
            if (Math.Abs(projection.M34 + 1) > 1e-6 || Math.Abs(projection.M44) > 1e-8 || projection.M43 >= 0 || projection.M33 > -1) return false;
            // The gameplay camera uses ordinary RH depth. Native HDR captures
            // require reverse Z: change z to w-z, preserving x/y/w exactly.
            projection.M13 = cameraProjection.M14 - cameraProjection.M13;
            projection.M23 = cameraProjection.M24 - cameraProjection.M23;
            projection.M33 = cameraProjection.M34 - cameraProjection.M33;
            projection.M43 = cameraProjection.M44 - cameraProjection.M43;
            return PortalProjection.ComplementaryProjection(projection);
        }
        sealed class PortalWorld : PortalProvider.IWorld
        {
            readonly HDRClientRendererPlugin plugin;
            internal PortalWorld(HDRClientRendererPlugin plugin) { this.plugin = plugin; }
            public double Now { get { return System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency; } }
            public bool Active(long anchor, long caller, string source, string screen)
            {
                if (source != screen || plugin.hdrService == null) return false;
                try { var state = plugin.hdrService("source-state", new object[] { PortalProviderId, anchor, caller, source }); return state is bool && (bool)state; }
                catch { return false; }
            }
            public bool Authorized(long anchor, long caller, string source)
            {
                if (!Ready() || !plugin.registered || MyAPIGateway.Session.Player == null || MyAPIGateway.Session.Player.Character == null) return false;
                var block = MyAPIGateway.Entities.GetEntityById(anchor) as GameBlock;
                var pb = MyAPIGateway.Entities.GetEntityById(caller) as PbBlock;
                var player = MyAPIGateway.Session.Player;
                return block != null && pb != null && !block.Closed && !pb.Closed && block.IsWorking && pb.IsWorking && pb.OwnerId != 0 &&
                    pb.IsSameConstructAs(block) && block.HasPlayerAccess(pb.OwnerId) && pb.HasPlayerAccess(player.IdentityId) &&
                    block.HasPlayerAccess(player.IdentityId) && Vector3D.DistanceSquared(player.Character.GetPosition(), block.GetPosition()) <= 3600;
            }
            public bool TryDescriptor(long anchor, long caller, string source, out object[] descriptor)
            {
                descriptor = null; if (plugin.hdrService == null) return false;
                try { descriptor = plugin.hdrService("source-portaldescriptor", new object[] { PortalProviderId, anchor, caller, source }) as object[]; return descriptor != null; }
                catch { return false; }
            }
            public bool TryLocalView(out MatrixD viewer, out MatrixD projection)
            {
                viewer = projection = MatrixD.Identity;
                try { var camera = MyAPIGateway.Session.Camera; if (camera == null) return false;
                    viewer = camera.WorldMatrix;
                    return PortalProjection.Rigid(viewer) && PortalProjectionFromCamera(camera.ProjectionMatrix, out projection); }
                catch { return false; }
            }
        }
        object PortalEndpoint(string command, object[] args)
        {
            if (!registered || hdrService == null || portals == null || gameThreadId != Thread.CurrentThread.ManagedThreadId || !Ready()) return null;
            if (command == "begin" || command == "end") return true;
            if (command == "release") { if (args == null || args.Length != 1 || !(args[0] is PortalProvider.Frame)) return false; portals.Release((PortalProvider.Frame)args[0]); return true; }
            if (command == "valid") return args != null && args.Length == 4 && args[0] is long && args[1] is long && args[2] is string &&
                portals.Valid(args[3] as PortalProvider.Frame, (long)args[0], (long)args[1], (string)args[2]);
            if (command != "frame" || args == null || (args.Length != 14 && args.Length != 16) || !(args[0] is long) || !(args[1] is long) || !(args[2] is string) || !(args[3] is string)) return null;
            var frame = portals.Request((long)args[0], (long)args[1], (string)args[2], (string)args[3]);
            if (frame == null)
            {
                if (providerErrors++ < 8) Log("Portal frame pending: " + portals.Reason);
                return null;
            }
            return new MyTuple<int, object, object, bool, double>(2,
                new MyTuple<string, Vector2I, long, Vector4>(frame.Texture, new Vector2I(frame.Bucket, frame.Bucket), frame.Generation, frame.TextureUv), frame, frame.Reduced, frame.Declaration.Rate);
        }
    }
}
