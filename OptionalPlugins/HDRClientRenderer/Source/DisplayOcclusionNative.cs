using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using VRageMath;

namespace HDRClientRenderer
{
    // Dormant ABI/phase adapter only: no Harmony hooks or GPU queries are installed.
    // An existing main-scene seam may observe phase, but that cannot certify depth.
    // The next implementation needs capture scheduled after current primary opaque
    // depth and a nonblocking, same-frame completed query. Query a padded enclosure
    // of every clipped tile at its closest reverse depth, with color/depth writes
    // off. Pending results keep demand. Previous-frame zeros are unusable even
    // with a stationary viewer: no global opaque-world revision is validated here.
    internal sealed class DisplayOcclusionNative
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        readonly DisplayOcclusion policy;
        readonly PropertyInfo backbuffer, size, frame, device;
        readonly FieldInfo gbufferMain;
        // Metadata only. The resolver prefix follows all opaque command-list
        // consumption on the installed renderer; the scene postfix follows
        // transparent consumption, so no pending main command list is clobbered
        // by recursive source capture. No hook/capture rescheduling occurs here.
        internal readonly MethodInfo QuerySubmitBoundary, QueryConsumeBoundary;
        internal readonly PropertyInfo PrimaryOpaqueDepth;
        object lastDevice;
        internal sealed class MainObservation
        {
            internal readonly object Target, GBuffer, Device;
            internal readonly long Frame, Epoch;
            internal MainObservation(object target, object gbuffer, object device, long frame, long epoch)
            { Target = target; GBuffer = gbuffer; Device = device; Frame = frame; Epoch = epoch; }
        }
        internal bool QueryApiPresent { get; private set; }
        internal bool ProofAvailable { get { return false; } }
        internal string StatusText { get { return policy.StatusText; } }
        internal DisplayOcclusionNative(Assembly assembly, DisplayOcclusion policy)
        {
            if (assembly == null) throw new ArgumentNullException("assembly");
            this.policy = policy ?? throw new ArgumentNullException("policy");
            var renderer = assembly.GetType("VRageRender.MyRender11", true);
            var rtv = assembly.GetType("VRage.Render11.Resources.IRtvBindable", true);
            var borrowed = assembly.GetType("VRage.Render11.Resources.IBorrowedRtvTexture", true);
            var scene = renderer.GetMethod("DrawGameScene", Static, null, new[] { rtv, borrowed.MakeByRefType() }, null);
            backbuffer = renderer.GetProperty("Backbuffer", Static); device = renderer.GetProperty("DeviceInstance", Static);
            frame = assembly.GetType("VRageRender.MyCommon", true).GetProperty("FrameCounter", Static);
            size = backbuffer == null ? null : backbuffer.PropertyType.GetProperty("Size", Instance);
            gbufferMain = assembly.GetType("VRage.Render11.Resources.MyGBuffer", true).GetField("Main", Static);
            QuerySubmitBoundary = assembly.GetType("VRage.Render11.GBufferResolve.MyGBufferResolver", false)?.GetMethod("ConsumeWork", Static, null, Type.EmptyTypes, null);
            QueryConsumeBoundary = scene;
            PrimaryOpaqueDepth = gbufferMain?.FieldType.GetProperty("DepthStencil", Instance);
            if (scene == null || scene.ReturnType != typeof(void) || backbuffer == null || device == null || frame == null || frame.PropertyType != typeof(long) || size == null || size.PropertyType != typeof(Vector2I) || gbufferMain == null)
                throw new InvalidOperationException("Display occlusion phase ABI changed.");
            QueryApiPresent = QueryShape(assembly);
        }
        internal bool ObserveBeforeMain(object target)
        {
            MainObservation observation;
            return ObserveBeforeMain(target, out observation);
        }
        // A scene hook can carry this invocation-local ticket through its
        // postfix/finalizer. It must not replace it with a cached last-frame one.
        internal bool ObserveBeforeMain(object target, out MainObservation observation)
        {
            observation = null;
            // No consumer geometry or validated opaque-depth image is available;
            // this only reports BeforeMainScene, never completed zero evidence.
            if (DirectCameraCapture.IsCapturing) return false;
            try
            {
                var main = backbuffer.GetValue(null);
                if (main == null || !ReferenceEquals(target, main)) return false;
                var gbuffer = gbufferMain.GetValue(null); var currentDevice = device.GetValue(null);
                if (gbuffer == null || currentDevice == null) return false;
                if (lastDevice != null && !ReferenceEquals(lastDevice, currentDevice)) policy.NewEpoch();
                lastDevice = currentDevice;
                var viewport = (Vector2I)size.GetValue(main);
                long number = (long)frame.GetValue(null);
                policy.ObserveMainStarting(number, viewport.X, viewport.Y, gbuffer);
                if (policy.CurrentPhase != DisplayOcclusion.Phase.BeforeMainScene) return false;
                observation = new MainObservation(main, gbuffer, currentDevice, number, policy.Epoch);
                return true;
            }
            catch { policy.NewEpoch(); return false; }
        }
        internal bool ObserveAfterMain(MainObservation observation, bool succeeded)
        {
            if (observation == null) return false;
            try
            {
                // Recursive direct-camera rendering may swap the main resources.
                // A changed device/world epoch, frame, target or G-buffer cannot
                // certify completion of the original primary invocation.
                bool same = !DirectCameraCapture.IsCapturing && observation.Epoch == policy.Epoch &&
                    observation.Frame == (long)frame.GetValue(null) &&
                    ReferenceEquals(observation.Target, backbuffer.GetValue(null)) &&
                    ReferenceEquals(observation.GBuffer, gbufferMain.GetValue(null)) &&
                    ReferenceEquals(observation.Device, device.GetValue(null));
                policy.ObserveMainCompleted(observation.Frame, observation.GBuffer, succeeded && same);
                return succeeded && same;
            }
            catch { policy.NewEpoch(); return false; }
        }
        internal static bool QueryShape(Assembly assembly)
        {
            // Query construction allocates GPU resources. Signature checking
            // calls none of Begin/End/GetResult and has no renderer side effect.
            var query = assembly.GetType("VRage.Render11.Culling.Occlusion.MyOcclusionQuery", false);
            var context = assembly.GetType("VRage.Render11.RenderContext.MyRenderContext", false);
            if (query == null || context == null) return false;
            var begin = query.GetMethod("Begin", Instance, null, new[] { context }, null);
            var end = query.GetMethod("End", Instance, null, new[] { context }, null);
            var result = query.GetMethod("GetResult", Instance, null, new[] { typeof(bool) }, null);
            return begin != null && begin.ReturnType == typeof(void) && end != null && end.ReturnType == typeof(void) && result != null && result.ReturnType == typeof(long);
        }
        internal static bool OrderShape(Assembly assembly)
        {
            try
            {
                var scheduler = assembly.GetType("VRage.Render11.Render.MyRenderScheduler", true);
                var calls = Calls(scheduler.GetMethod("Done", Instance));
                int oldGeometry = CallIndex(calls, "VRageRender.MyGeometryRendererOld", "DoneFrame");
                int geometry = CallIndex(calls, "VRage.Render11.GeometryStage2.Rendering.MyGeometryRenderer", "DoneFrame");
                int foliage = CallIndex(calls, "VRageRender.MyFoliageRenderingPass", "Consume");
                int resolve = CallIndex(calls, "VRage.Render11.GBufferResolve.MyGBufferResolver", "ConsumeWork");
                int transparent = CallIndex(calls, "VRageRender.MyTransparentRendering", "ConsumeWork");
                return oldGeometry >= 0 && geometry >= 0 && foliage >= 0 && resolve > oldGeometry && resolve > geometry && resolve > foliage && transparent > resolve;
            }
            catch { return false; }
        }
        static int CallIndex(List<MethodBase> calls, string type, string method)
        { return calls.FindIndex(m => m.DeclaringType.FullName == type && m.Name == method); }
        static List<MethodBase> Calls(MethodInfo method)
        {
            var ops = new Dictionary<short, OpCode>();
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.FieldType == typeof(OpCode)) { var op = (OpCode)field.GetValue(null); ops[op.Value] = op; }
            var result = new List<MethodBase>(); var bytes = method.GetMethodBody().GetILAsByteArray(); int at = 0;
            while (at < bytes.Length)
            {
                short key = bytes[at++]; if (key == 254) key = (short)(0xfe00 | bytes[at++]); var op = ops[key]; int length;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: length = 0; break;
                    case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: length = 1; break;
                    case OperandType.InlineVar: length = 2; break;
                    case OperandType.InlineI8: case OperandType.InlineR: length = 8; break;
                    case OperandType.InlineSwitch: length = 4 + 4 * BitConverter.ToInt32(bytes, at); break;
                    default: length = 4; break;
                }
                if (op == OpCodes.Call || op == OpCodes.Callvirt) result.Add(method.Module.ResolveMethod(BitConverter.ToInt32(bytes, at)));
                at += length;
            }
            return result;
        }
        internal void NewEpoch() { policy.NewEpoch(); lastDevice = null; }
    }
}
