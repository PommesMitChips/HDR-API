using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VRageMath;

// Uses the real initialized engine owners, but never creates a device, texture,
// scene or renderer frame. Only logging and the offline content-path dependency
// of MyRender11's class initializer are intercepted in this isolated process.
static class InitializedStateFixture
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type Engine(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(name, false)).First(t => t != null);
    static bool ContentPath(ref string __result)
    { __result = @"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Content"; return false; }
    static bool SuppressLog() => false;
    static object platform;
    static bool Platform(ref object __result) { __result = platform; return false; }
    static object New(Type type, params object[] args) => Activator.CreateInstance(type, Instance, null, args, null);
    static void Call(object owner, string name) => owner.GetType().GetMethod(name, Instance).Invoke(owner, null);
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void AssertContents(Array array, Array expected, string message)
    {
        Assert(array.Length == expected.Length, message + " length");
        for (int i = 0; i < array.Length; i++)
            Assert(Equals(array.GetValue(i), expected.GetValue(i)), message + " element " + i);
    }
    static void AssertFields(object owner, FieldInfo[] fields, object[] values, string message)
    {
        for (int i = 0; i < fields.Length; i++)
            Assert(fields[i].FieldType.IsValueType ? Equals(fields[i].GetValue(owner), values[i]) :
                ReferenceEquals(fields[i].GetValue(owner), values[i]), message + " " + fields[i].Name);
    }
    static void RejectReadonlyWrite(FieldInfo field)
    {
        try { field.SetValue(null, field.GetValue(null)); }
        catch (FieldAccessException) { return; }
        throw new Exception("Initialized runtime failed to reject the original readonly-static replacement: " + field.Name);
    }

    internal static void Run(Assembly plugin, Action next = null)
    {
        var isolation = new Harmony("HDR.Tests.InitializedCaptureState");
        try
        {
            // MyRenderSettings' real initializer only asks the platform for two
            // CPU memory-budget values. An interface fixture supplies those;
            // creating/starting the game's platform is unnecessary and forbidden.
            platform = DispatchProxy.Create(Engine("VRage.IVRagePlatform"), typeof(OfflineEngineServices));
            isolation.Patch(Engine("VRage.MyVRage").GetMethod("get_Platform", Static),
                prefix: new HarmonyMethod(typeof(InitializedStateFixture).GetMethod(nameof(Platform), Static)));
            var content = Engine("VRage.FileSystem.MyFileSystem").GetMethod("get_ContentPath", Static);
            isolation.Patch(content, prefix: new HarmonyMethod(typeof(InitializedStateFixture).GetMethod(nameof(ContentPath), Static)));
            foreach (string name in new[] { "VRage.Utils.MyLog", "VRage.Utils.Keen.MyLogKeen" })
                foreach (var method in Engine(name).GetMethods(Instance | BindingFlags.DeclaredOnly)
                    .Where(m => m.ReturnType == typeof(void) && m.GetMethodBody() != null &&
                        (m.Name == "InitWithDate" || m.Name == "InitWithDateNoCheck" || m.Name == "WriteLine")))
                    isolation.Patch(method, prefix: new HarmonyMethod(typeof(InitializedStateFixture).GetMethod(nameof(SuppressLog), Static)));

            var renderer = Engine("VRageRender.MyRender11");
            var bloom = Engine("VRageRender.MyModernBloom");
            RuntimeHelpers.RunClassConstructor(renderer.TypeHandle);
            RuntimeHelpers.RunClassConstructor(bloom.TypeHandle);
            Console.WriteLine("INITIALIZED: real MyRender11 and MyModernBloom class constructors completed; no graphics device/frame.");
            Console.Out.Flush();

            var environmentField = renderer.GetField("Environment", Static);
            var matricesField = environmentField.FieldType.GetField("Matrices", Instance);
            object environment = environmentField.GetValue(null), matrices = matricesField.GetValue(environment);
            Assert(environmentField.IsInitOnly && matricesField.IsInitOnly, "Real camera-owner readonly ABI changed.");
            RejectReadonlyWrite(environmentField);
            var bloomFields = new[] { "m_bloomCascadeDown", "m_bloomCascadeUp" }.Select(n => bloom.GetField(n, Static)).ToArray();
            foreach (var field in bloomFields)
            { Assert(field.IsInitOnly, "Real bloom-array readonly ABI changed."); RejectReadonlyWrite(field); }

            var native = plugin.GetType("HDRClientRenderer.DirectCameraCaptureNative", true);
            var cameraSnapshot = native.GetNestedType("CameraMatricesSnapshot", BindingFlags.NonPublic);
            var staticSnapshot = native.GetNestedType("StaticSnapshot", BindingFlags.NonPublic);
            // Seed two real nonempty frusta so stale nested planes/corners cannot
            // hide behind the engine initializer's initially-null references.
            var near = new BoundingFrustumD(MatrixD.CreatePerspectiveFieldOfView(1.1, 1.3, .2, 400));
            var far = new BoundingFrustumD(MatrixD.CreatePerspectiveFieldOfView(.9, 1.1, .4, 1200));
            var nearField = matrices.GetType().GetField("ViewFrustumClippedD", Instance);
            var farField = matrices.GetType().GetField("ViewFrustumClippedFarD", Instance);
            nearField.SetValue(matrices, near); farField.SetValue(matrices, far);
            var matrixFields = matrices.GetType().GetFields(Instance);
            var matrixValues = matrixFields.Select(f => f.GetValue(matrices)).ToArray();
            var nearCorners = near.GetCorners(); var farCorners = far.GetCorners();
            var nearPlanes = new[] { near.Near, near.Far, near.Left, near.Right, near.Top, near.Bottom };
            var farPlanes = new[] { far.Near, far.Far, far.Left, far.Right, far.Top, far.Bottom };
            var bloomArrays = bloomFields.Select(f => (Array)f.GetValue(null)).ToArray();
            var sentinels = new object[2];
            for (int i = 0; i < bloomArrays.Length; i++)
            {
                // Lease sentinels are identity tokens only. Their methods are
                // never called and no underlying GPU resource is constructed.
                var concrete = Engine("VRage.Render11.Resources.Textures.MyBorrowedRtvTexture");
                sentinels[i] = RuntimeHelpers.GetUninitializedObject(concrete);
                bloomArrays[i].SetValue(sentinels[i], i);
            }
            var bloomContents = bloomArrays.Select(a => (Array)a.Clone()).ToArray();

            // Fail at three different points, then repeat a completed pass.
            // Every path uses the production snapshot helpers' actual Restore.
            foreach (int failAfter in new[] { 0, 1, 2, -1 })
            {
                object camera = New(cameraSnapshot, environmentField, matricesField);
                object scratch = New(staticSnapshot, (object)bloomFields);
                try
                {
                    Call(scratch, "ClearArrays");
                    foreach (var array in bloomArrays) foreach (object value in array)
                        Assert(value == null, "Capture inherited a main-view bloom lease.");
                    if (failAfter == 0) throw new InjectedStateFailure();
                    matrices.GetType().GetField("CameraPosition", Instance).SetValue(matrices, new Vector3D(10, 20, 30));
                    matrices.GetType().GetField("NearClipping", Instance).SetValue(matrices, 3f);
                    near.Matrix = MatrixD.CreatePerspectiveFieldOfView(.6, .8, .8, 80);
                    far.Matrix = MatrixD.CreatePerspectiveFieldOfView(1.4, 1.8, .7, 3000);
                    Assert(!near.GetCorners().SequenceEqual(nearCorners), "Nested frustum mutation fixture did not change corners.");
                    if (failAfter == 1) throw new InjectedStateFailure();
                    bloomArrays[0].SetValue(sentinels[1], 0); bloomArrays[1].SetValue(sentinels[0], 0);
                    // Also replace a mutable matrices child during the pass;
                    // restore must recover both its original identity and caches.
                    nearField.SetValue(matrices, new BoundingFrustumD(MatrixD.Identity));
                    if (failAfter == 2) throw new InjectedStateFailure();
                }
                catch (InjectedStateFailure) { }
                finally { Call(camera, "Restore"); Call(scratch, "Restore"); }
                Assert(ReferenceEquals(environmentField.GetValue(null), environment) && ReferenceEquals(matricesField.GetValue(environment), matrices),
                    "Capture replaced a readonly camera-owner reference.");
                AssertFields(matrices, matrixFields, matrixValues, "Camera matrices state not restored:");
                Assert(ReferenceEquals(nearField.GetValue(matrices), near) && ReferenceEquals(farField.GetValue(matrices), far), "Frustum identity was not restored.");
                Assert(near.GetCorners().SequenceEqual(nearCorners) && far.GetCorners().SequenceEqual(farCorners), "Frustum corner cache was not restored.");
                Assert(new[] { near.Near, near.Far, near.Left, near.Right, near.Top, near.Bottom }.SequenceEqual(nearPlanes) &&
                    new[] { far.Near, far.Far, far.Left, far.Right, far.Top, far.Bottom }.SequenceEqual(farPlanes), "Frustum plane cache was not restored.");
                for (int i = 0; i < bloomFields.Length; i++)
                { Assert(ReferenceEquals(bloomFields[i].GetValue(null), bloomArrays[i]), "Readonly bloom-array identity changed."); AssertContents(bloomArrays[i], bloomContents[i], "Bloom scratch contents not restored"); }
                Console.WriteLine("PASS: initialized camera owners, matrix fields, both nested frusta/planes/corners and both readonly bloom arrays restore after stage " + failAfter + ".");
            }
            TestMutableStaticWrites(native, renderer);
            TestUnhealthyScheduling(plugin, native, renderer);
            if (next != null) next();
            Console.WriteLine("PASS: initialized-runtime state regression; no GPU constructor/frame or game process.");
        }
        finally { isolation.UnpatchAll(isolation.Id); platform = null; }
    }
    static void TestMutableStaticWrites(Type native, Type renderer)
    {
        object adapter = New(native, renderer.Assembly);
        foreach (string name in new[] { "fullViewport", "resolution", "settings", "postprocess", "overrides", "gbufferMain", "gbufferHdr", "lockImmediate" })
        {
            var field = (FieldInfo)native.GetField(name, Instance).GetValue(adapter);
            RuntimeHelpers.RunClassConstructor(field.DeclaringType.TypeHandle);
            Assert(!field.IsInitOnly && !field.IsLiteral, "Capture static assignment lacks mutable ABI: " + field.Name);
            object value = field.GetValue(null); field.SetValue(null, value);
        }
        var hbao = (FieldInfo[])native.GetField("hbaoTextures", Instance).GetValue(adapter);
        foreach (var field in hbao)
        {
            RuntimeHelpers.RunClassConstructor(field.DeclaringType.TypeHandle);
            Assert(!field.IsInitOnly && !field.IsLiteral, "HBAO substitution is not mutable: " + field.Name);
            object value = field.GetValue(null); field.SetValue(null, value);
        }
        foreach (string name in new[] { "commonState", "debugTextureState" })
            foreach (var field in (FieldInfo[])native.GetField(name, Instance).GetValue(adapter))
            {
                RuntimeHelpers.RunClassConstructor(field.DeclaringType.TypeHandle);
                Assert(!field.IsInitOnly && !field.IsLiteral, "Static snapshot assignment is not mutable: " + field.Name);
                object value = field.GetValue(null); field.SetValue(null, value);
            }
        foreach (string name in new[] { "viewport", "lodding", "debugAmbient" })
        {
            var property = (PropertyInfo)native.GetField(name, Instance).GetValue(adapter);
            RuntimeHelpers.RunClassConstructor(property.DeclaringType.TypeHandle);
            Assert(property.CanWrite, "Restored renderer property has no setter: " + property.Name);
            object value = property.GetValue(null); property.SetValue(null, value);
        }
        Console.WriteLine("PASS: all explicit static field assignments accept restoration after their real engine class initialization.");
    }
    static object failingAdapter;
    static int allocations, captureAttempts;
    static long mainFrame;
    static bool Bound(ref int __result) { __result = 256; return false; }
    static bool Allocate(ref object __result) { allocations++; __result = new object(); return false; }
    static bool FailedCapture(ref bool __result)
    {
        captureAttempts++;
        failingAdapter.GetType().GetField("<Healthy>k__BackingField", Instance).SetValue(failingAdapter, false);
        __result = false; return false;
    }
    static bool Frame(ref long __result) { __result = mainFrame; return false; }
    static bool SameDevice(ref bool __result) { __result = false; return false; }
    static bool NoGpu() { return false; }
    static bool Idle(ref bool __result) { __result = true; return false; }
    static void TestUnhealthyScheduling(Assembly plugin, Type native, Type renderer)
    {
        var interception = new Harmony("HDR.Tests.CaptureFailureStopsAllocation");
        var serviceType = plugin.GetType("HDRClientRenderer.DirectCameraCapture", true);
        object service = New(serviceType);
        failingAdapter = New(native, renderer.Assembly);
        try
        {
            foreach (var pair in new[] {
                (native.GetMethod("BoundResolution", Instance), nameof(Bound)),
                (native.GetMethod("CreateTarget", Instance), nameof(Allocate)),
                (native.GetMethod("CreateAtlasTarget", Instance), nameof(Allocate)),
                (native.GetMethod("ClearAtlas", Instance), nameof(NoGpu)),
                (native.GetMethod("CopyTile", Instance), nameof(NoGpu)),
                (native.GetMethod("PublishAtlas", Instance), nameof(NoGpu)),
                (native.GetMethod("Capture", Instance), nameof(FailedCapture)),
                (native.GetProperty("Frame", Instance).GetGetMethod(true), nameof(Frame)),
                (native.GetProperty("FrameIdle", Instance).GetGetMethod(true), nameof(Idle)),
                (native.GetProperty("DeviceChanged", Instance).GetGetMethod(true), nameof(SameDevice)) })
                interception.Patch(pair.Item1, prefix: new HarmonyMethod(typeof(InitializedStateFixture).GetMethod(pair.Item2, Static)));
            serviceType.GetField("native", Instance).SetValue(service, failingAdapter);
            // Match resource epoch without executing cleanup/device methods.
            serviceType.GetField("renderedEpoch", Instance).SetValue(service, serviceType.GetField("epoch", Instance).GetValue(service));
            allocations = captureAttempts = 0; mainFrame = 1;
            for (long id = 1; id <= 6; id++)
                serviceType.GetMethod("Request", Instance).Invoke(service, new object[] { id, MatrixD.Identity, 105d, 256, 30d, new object(), 0, null });
            Call(service, "RenderMain");
            Assert(allocations == 3 && captureAttempts == 1, "An unhealthy capture continued the same-frame target allocation/work loop.");
            mainFrame = 2; Call(service, "RenderMain");
            Assert(allocations == 3 && captureAttempts == 1, "An unhealthy capture allocated/retried targets on the next main frame.");
            Assert(!(bool)serviceType.GetProperty("Ready", Instance).GetValue(service), "Unhealthy renderer continued to advertise readiness.");
            Console.WriteLine("PASS: six accepted demands stop at the first injected capture failure; allocations stay at one camera's two atlases plus shared scratch on same/next frame; native GPU bodies intercepted.");
        }
        finally
        {
            // The fixture owns no engine targets/cleanup job. Detach its fake
            // adapter before disposing the service's ordinary managed shell.
            serviceType.GetField("native", Instance).SetValue(service, null);
            ((IDisposable)service).Dispose(); failingAdapter = null;
            interception.UnpatchAll(interception.Id);
        }
    }
    sealed class InjectedStateFailure : Exception { }
}

public class OfflineEngineServices : DispatchProxy
{
    public static Action<MethodInfo, object[]> RenderCall;
    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (method.Name == "get_Render") return Create(method.ReturnType, typeof(OfflineEngineServices));
        if (method.Name == "GetMemoryBudgetForStreamedResources" || method.Name == "GetMemoryBudgetForVoxelTextureArrays") return 0UL;
        if (method.Name == "FastVSSetConstantBuffer" && RenderCall != null) { RenderCall(method, args); return null; }
        throw new Exception("Initialized state fixture unexpectedly requested an engine service: " + method.Name);
    }
}
