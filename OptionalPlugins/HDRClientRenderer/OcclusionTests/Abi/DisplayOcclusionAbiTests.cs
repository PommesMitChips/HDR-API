using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using HDRClientRenderer;

namespace HDRClientRenderer { internal static class DirectCameraCapture { internal static bool IsCapturing { get { return false; } } } }
internal static class DisplayOcclusionAbiTests
{
    sealed class UnavailableEnclosure : DisplayOcclusionQueryNative.IEnclosureRenderer
    {
        public bool CurrentFrameOpaqueProof { get { return false; } }
        public bool Matches(DisplayOcclusion.Stamp stamp) { return false; }
        public void Draw(object context, DisplayOcclusion.Stamp stamp, DisplayOcclusion.Footprint footprint)
        { throw new Exception("Offline ABI tests must never draw."); }
    }
    static void Main()
    {
        const string game = @"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        { var path = Path.Combine(game, new AssemblyName(args.Name).Name + ".dll"); return File.Exists(path) ? Assembly.LoadFrom(path) : null; };
        var renderer = Assembly.LoadFrom(Path.Combine(game, "VRage.Render11.dll"));
        var policy = new DisplayOcclusion(); policy.SetEnabled(true);
        var adapter = new DisplayOcclusionNative(renderer, policy);
        if (!DisplayOcclusionNative.OrderShape(renderer)) throw new Exception("Runtime primary opaque ordering gate rejected the installed renderer.");
        using (var queries = new DisplayOcclusionQueryNative(renderer, new UnavailableEnclosure()))
            if (queries.CurrentFrameOpaqueProof) throw new Exception("Private-query ABI inspection cannot certify proof.");
        using (var enclosure = new DisplayOcclusionRendererNative(renderer))
        {
            if (enclosure.CurrentFrameOpaqueProof) throw new Exception("Enclosure shader compilation cannot certify a current frame.");
            var ds = enclosure.CreateDepthDescription(); var rd = enclosure.CreateRasterDescription();
            if (Field(ds, "DepthComparison") != "GreaterEqual" || Field(ds, "DepthWriteMask") != "Zero" ||
                Field(ds, "IsStencilEnabled") != "False" || Field(rd, "CullMode") != "None" ||
                Field(rd, "IsScissorEnabled") != "False" || Field(rd, "IsMultisampleEnabled") != "True")
                throw new Exception("Conservative enclosure raster/depth state changed.");
            VerifyEnclosureShader();
        }
        if (!adapter.QueryApiPresent || adapter.ProofAvailable || policy.Supported || !adapter.StatusText.Contains("unavailable")) throw new Exception("Native signature availability was incorrectly promoted to occlusion capability.");
        VerifyRenderOrdering(renderer, adapter);
        // No native getter, scene method, query constructor or query result method
        // is invoked: this verifies ABI and dormant status without a GPU/device.
        Console.WriteLine("Display occlusion native ABI and opaque/transparent submission order validated; proof unavailable; no GPU calls or hooks.");
    }
    static string Field(object value, string name) { return value.GetType().GetField(name).GetValue(value).ToString(); }
    static void VerifyEnclosureShader()
    {
        var bytes = DisplayOcclusionRendererNative.Compile(DisplayOcclusionRendererNative.PixelSource, "ps_5_0");
        var type = Assembly.Load("SharpDX.D3DCompiler").GetType("SharpDX.D3DCompiler.ShaderReflection", true);
        var reflection = type.GetConstructor(new[] { typeof(byte[]) }).Invoke(new object[] { bytes });
        try
        {
            var description = type.GetProperty("Description").GetValue(reflection);
            int outputs = (int)description.GetType().GetField("OutputParameters").GetValue(description);
            int inputs = (int)description.GetType().GetField("InputParameters").GetValue(description);
            if (outputs != 1) throw new Exception("Enclosure PS must output depth only.");
            var output = type.GetMethod("GetOutputParameterDescription").Invoke(reflection, new object[] { 0 });
            if ((string)output.GetType().GetField("SemanticName").GetValue(output) != "SV_Depth") throw new Exception("Enclosure writes a color target.");
            bool sampleFrequency = false;
            for (int i = 0; i < inputs; i++)
            {
                var input = type.GetMethod("GetInputParameterDescription").Invoke(reflection, new object[] { i });
                if ((string)input.GetType().GetField("SemanticName").GetValue(input) == "SV_SampleIndex") sampleFrequency = true;
            }
            if (!sampleFrequency) throw new Exception("Compiler removed sample-frequency enclosure input.");
        }
        finally { ((IDisposable)reflection).Dispose(); }
    }
    static void VerifyRenderOrdering(Assembly renderer, DisplayOcclusionNative adapter)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        if (adapter.QuerySubmitBoundary == null || adapter.QuerySubmitBoundary.ReturnType != typeof(void) ||
            adapter.QueryConsumeBoundary == null || adapter.PrimaryOpaqueDepth == null)
            throw new Exception("Current-frame submission/consumption seam metadata is missing.");
        var scheduler = renderer.GetType("VRage.Render11.Render.MyRenderScheduler", true);
        var calls = Calls(scheduler.GetMethod("Done", flags));
        int opaqueOld = Index(calls, "VRageRender.MyGeometryRendererOld", "DoneFrame");
        int opaqueNew = Index(calls, "VRage.Render11.GeometryStage2.Rendering.MyGeometryRenderer", "DoneFrame");
        int foliage = Index(calls, "VRageRender.MyFoliageRenderingPass", "Consume");
        int resolver = Index(calls, adapter.QuerySubmitBoundary.DeclaringType.FullName, "ConsumeWork");
        int transparent = Index(calls, "VRageRender.MyTransparentRendering", "ConsumeWork");
        if (opaqueOld < 0 || opaqueNew < 0 || foliage < 0 || resolver <= opaqueOld || resolver <= opaqueNew ||
            resolver <= foliage || transparent <= resolver)
            throw new Exception("Opaque submission prefix no longer precedes the resolver and transparent stage.");
        calls = Calls(adapter.QueryConsumeBoundary);
        int execute = Index(calls, scheduler.FullName, "Execute"), done = Index(calls, scheduler.FullName, "Done");
        if (execute < 0 || done <= execute) throw new Exception("Late consumption seam no longer follows scheduler completion.");
        var resolverCalls = Calls(adapter.QuerySubmitBoundary);
        if (Index(resolverCalls, "VRage.Render11.RenderContext.MyRenderContext", "ExecuteContext") < 0)
            throw new Exception("Resolver no longer consumes its command list at the known seam.");
    }
    static int Index(List<MethodBase> calls, string type, string name)
    { return calls.FindIndex(m => m.DeclaringType.FullName == type && m.Name == name); }
    // Decode IL without executing any game method or static initializer. Avoid
    // scanning raw bytes for call opcodes: operands can contain the same bytes.
    static List<MethodBase> Calls(MethodInfo method)
    {
        var ops = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.FieldType == typeof(OpCode)) { var op = (OpCode)field.GetValue(null); ops[op.Value] = op; }
        var result = new List<MethodBase>(); var bytes = method.GetMethodBody().GetILAsByteArray(); int at = 0;
        while (at < bytes.Length)
        {
            short key = bytes[at++]; if (key == 254) key = (short)(0xfe00 | bytes[at++]); var op = ops[key];
            int length;
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
}
