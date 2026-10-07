using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

static class ParticleBindingFixture
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static object Texture, Shadows, Samplers;
    static object previousBuffer, previousOwner, captureBuffer, observedBuffer, selectedShader, originalShader, privateShader, vertex;
    static object mapping;
    static Assembly plugin;
    static bool throwDraw;
    static int sets, renders, outsideRuns, privateSelections;
    static Type Engine(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null);
    static object Fake(Type type) => RuntimeHelpers.GetUninitializedObject(type);
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static bool SkipGpu() => false;
    static bool StopOutsideRun() { outsideRuns++; return false; }
    static void FastConstant(MethodInfo method, object[] args)
    { Assert((int)args[1] == 6, "Private binding did not use compiled-verified free b6."); sets++; observedBuffer = args[2]; }
    static bool CheckCb(int __0)
    { Assert(__0 >= 0 && __0 < 8, "DrawExisting routed an out-of-range constant slot through the engine cache."); return false; }
    static bool CheckSampler(int __0)
    { Assert(__0 >= 0 && __0 < 16, "DrawExisting sampler exceeds the engine's 16-slot cache."); return false; }
    static bool CheckSrv(int __0)
    { Assert(__0 >= 0 && __0 < 32, "DrawExisting SRV exceeds the engine's 32-slot cache."); return false; }
    static bool Shader(object __0) { selectedShader = __0; privateSelections++; return false; }
    static bool Render()
    {
        renders++;
        Assert(ReferenceEquals(observedBuffer, captureBuffer), "Particle render did not have its private origin constant bound.");
        vertex.GetType().GetMethod("Set", Instance).Invoke(vertex, new[] { originalShader });
        Assert(ReferenceEquals(selectedShader, privateShader), "Actual vertex detour did not select the private capture shader.");
        if (throwDraw) throw new InvalidOperationException("Injected particle draw failure.");
        return false;
    }
    static bool MapResult(out object result) { result = mapping; return true; }
    static MethodInfo MappingBridge(MethodBase original)
    {
        var bridge = plugin.GetType("HDRClientRenderer.DirectCameraCaptureBridge", true).GetMethod("Result", Static);
        return (MethodInfo)bridge.Invoke(null, new object[] { ((MethodInfo)original).ReturnType,
            typeof(ParticleBindingFixture).GetMethod(nameof(MapResult), Static), "HDRFixtureMapResult" });
    }
    static void Patch(Harmony harmony, MethodInfo method, string prefix, int priority = Priority.Last)
    { harmony.Patch(method, prefix: new HarmonyMethod(typeof(ParticleBindingFixture).GetMethod(prefix, Static)) { priority = priority }); }
    static MethodInfo Method(object particles, string name) => (MethodInfo)particles.GetType().GetField(name, Instance).GetValue(particles);
    static void Donor(object particles, string field, string donor)
    { particles.GetType().GetField(field, Instance).SetValue(particles, typeof(ParticleBindingFixture).GetField(donor, Static)); }
    static void Set(object particles, string field, object value) => particles.GetType().GetField(field, Instance).SetValue(particles, value);

    internal static void Run(Assembly loadedPlugin)
    {
        plugin = loadedPlugin;
        var serviceType = plugin.GetType("HDRClientRenderer.DirectCameraCapture", true);
        object service = Activator.CreateInstance(serviceType, true);
        var terminal = new Harmony("HDR.Tests.ActualParticleRunBinding");
        try
        {
            serviceType.GetMethod("TryInstall", Instance).Invoke(service, new object[] { (Action<string>)Console.WriteLine });
            Assert((bool)serviceType.GetProperty("Ready", Instance).GetValue(service), "Actual capture hooks failed to install.");
            object native = serviceType.GetField("native", Instance).GetValue(service);
            object particles = native.GetType().GetField("Particles", Instance).GetValue(native);
            var run = Method(particles, "Run");
            var contextType = run.GetParameters()[0].ParameterType;
            object context = Fake(contextType);
            vertex = Activator.CreateInstance(Engine("VRage.Render11.RenderContext.MyVertexStage"), true);
            object pixel = Activator.CreateInstance(Engine("VRage.Render11.RenderContext.MyPixelStage"), true);
            object all = Fake(Engine("VRage.Render11.RenderContext.MyAllShaderStages"));
            foreach (var pair in new[] { ("m_vertexShaderStage", vertex), ("m_pixelShaderStage", pixel) })
                contextType.GetField(pair.Item1, Instance).SetValue(context, pair.Item2);
            var allProperty = (PropertyInfo)particles.GetType().GetField("all", Instance).GetValue(particles);
            // An all-stage wrapper owns no GPU resources. Its fixed binding calls
            // are intercepted below, while the real VS cache constructor remains.
            var allField = contextType.GetFields(Instance).Single(f => f.FieldType == allProperty.PropertyType);
            allField.SetValue(context, all);
            object dx = Fake(Engine("SharpDX.Direct3D11.DeviceContext1"));
            contextType.GetField("m_deviceContext", Instance).SetValue(context, dx);
            var cacheField = Engine("VRage.Render11.RenderContext.MyCommonStage").GetField("m_constantBuffers", Instance);
            var statistics = Engine("VRage.Render11.RenderContext.MyCommonStage").GetField("m_statistics", Instance);
            statistics.SetValue(vertex, Fake(statistics.FieldType));
            Assert(((Array)cacheField.GetValue(vertex)).Length == 8, "Installed vertex constant-buffer cache changed.");
            var cachedSet = vertex.GetType().GetMethod("SetConstantBuffer", Instance);
            try { cachedSet.Invoke(vertex, new object[] { 13, null }); throw new Exception("Engine b13 cache failure did not reproduce."); }
            catch (TargetInvocationException ex) { Assert(ex.GetBaseException() is IndexOutOfRangeException, "Unexpected b13 negative-control failure."); }
            Console.WriteLine("REPRODUCED: actual constructed MyVertexStage.SetConstantBuffer(13,null) faults on its eight-entry cache before GPU work.");

            foreach (string name in new[] { "setDepth", "setRasterizer", "setLayout", "unmap", "write" }) Patch(terminal, Method(particles, name), nameof(SkipGpu));
            Patch(terminal, Method(particles, "map"), nameof(MappingBridge));
            Patch(terminal, Method(particles, "setFrame"), nameof(CheckCb));
            Patch(terminal, Method(particles, "setSampler"), nameof(CheckSampler));
            Patch(terminal, Method(particles, "setSamplers"), nameof(CheckSampler));
            Patch(terminal, Method(particles, "setSrv"), nameof(CheckSrv));
            Patch(terminal, Method(particles, "setAllSrv"), nameof(CheckSrv));
            Patch(terminal, Method(particles, "render"), nameof(Render));
            Patch(terminal, Method(particles, "VertexSet"), nameof(Shader));
            Patch(terminal, run, nameof(StopOutsideRun));
            mapping = Activator.CreateInstance(Method(particles, "map").ReturnType);
            originalShader = Fake(Engine("SharpDX.Direct3D11.VertexShader"));
            privateShader = Fake(Engine("SharpDX.Direct3D11.VertexShader"));
            Set(particles, "ordinaryOriginal", originalShader); Set(particles, "ordinaryShader", privateShader);
            captureBuffer = Fake(Engine("SharpDX.Direct3D11.Buffer"));
            object translation = DispatchProxy.Create(Engine("VRage.Render11.Resources.IConstantBuffer"), typeof(ParticleFixtureResource));
            ((ParticleFixtureResource)translation).Buffer = captureBuffer;
            Set(particles, "translation", translation);
            Texture = DispatchProxy.Create(run.GetParameters()[1].ParameterType, typeof(ParticleFixtureResource));
            Shadows = Fake(Engine("VRageRender.MyShadows"));
            var cascades = Fake(Engine("VRageRender.MyShadowCascades"));
            Shadows.GetType().GetFields(Instance).Single(f => f.FieldType == cascades.GetType()).SetValue(Shadows, cascades);
            Samplers = Array.CreateInstance(Engine("VRage.Render11.Resources.ISamplerState"), 0);
            Donor(particles, "texture", nameof(Texture)); Donor(particles, "shadows", nameof(Shadows)); Donor(particles, "samplers", nameof(Samplers));
            serviceType.GetField("capturing", Static).SetValue(null, 1);
            OfflineEngineServices.RenderCall = FastConstant;
            foreach (var scenario in new[] { (false, false), (true, false), (false, true), (true, true) })
            {
                bool fail = scenario.Item1, empty = scenario.Item2;
                previousBuffer = empty ? null : Fake(Engine("SharpDX.Direct3D11.Buffer"));
                previousOwner = empty ? null : DispatchProxy.Create(Engine("VRage.Render11.Resources.IConstantBuffer"), typeof(ParticleFixtureResource));
                if (previousOwner != null) ((ParticleFixtureResource)previousOwner).Buffer = previousBuffer;
                ((Array)cacheField.GetValue(vertex)).SetValue(previousOwner, 6);
                var originalCache = (Array)cacheField.GetValue(vertex);
                var originalEntries = (Array)originalCache.Clone();
                observedBuffer = null; sets = renders = privateSelections = outsideRuns = 0; throwDraw = fail;
                try { run.Invoke(null, new[] { context, null, null, (object)false }); Assert(!fail, "Injected particle failure did not escape the actual Run wrapper."); }
                catch (TargetInvocationException ex) { Assert(fail && ex.GetBaseException().Message.Contains("Injected particle draw failure"), "Actual Run wrapper hit an unexpected failure: " + ex.GetBaseException()); }
                Assert(sets == 2 && renders == 1 && privateSelections == 1 && outsideRuns == 0,
                    "Actual particle Run capture path did not use the scoped cache-compatible binding/shader detours.");
                Assert(ReferenceEquals(observedBuffer, previousBuffer) && ReferenceEquals(((Array)cacheField.GetValue(vertex)).GetValue(6), previousOwner),
                    "Cached/native b6 owners were not restored after the particle draw.");
                Assert(ReferenceEquals(originalCache, cacheField.GetValue(vertex)), "Particle scope replaced the engine cache array.");
                for (int i = 0; i < originalCache.Length; i++) Assert(ReferenceEquals(originalCache.GetValue(i), originalEntries.GetValue(i)), "Particle scope changed another cached constant-buffer owner at " + i + ".");
                if (previousBuffer != null) Assert(!(bool)previousBuffer.GetType().GetProperty("IsDisposed", Instance).GetValue(previousBuffer), "Capture disposed the retained original buffer owner.");
                Console.WriteLine("PASS: actual patched particle Run uses the real eight-slot vertex cache for private b6, selects its shader and restores cache identity/all entries and " + (empty ? "null" : "retained") + " native binding after " + (fail ? "injected failure" : "success") + "; b4/s15/t16 fit real engine caches; only native GPU calls intercepted.");
            }
            serviceType.GetField("capturing", Static).SetValue(null, 0); outsideRuns = 0;
            run.Invoke(null, new[] { context, null, null, (object)false });
            Assert(outsideRuns == 1, "HDR capture replacement blocked the normal main-view particle Run.");
            Console.WriteLine("PASS: normal main particle Run passes through HDR's guard; terminal fixture prevents simulation/GPU execution.");
        }
        finally
        {
            serviceType.GetField("capturing", Static).SetValue(null, 0);
            serviceType.GetField("native", Instance).SetValue(service, null); ((IDisposable)service).Dispose();
            terminal.UnpatchAll(terminal.Id); OfflineEngineServices.RenderCall = null; plugin = null;
        }
    }
}

public class ParticleFixtureResource : DispatchProxy
{
    public object Buffer;
    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (method.Name == "get_Buffer") return Buffer;
        throw new Exception("Particle binding fixture unexpectedly accessed GPU resource method: " + method.Name);
    }
}
