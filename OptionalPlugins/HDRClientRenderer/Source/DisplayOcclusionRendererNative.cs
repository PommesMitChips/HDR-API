using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using VRageMath;

namespace HDRClientRenderer
{
    // A private deferred context records only an enclosure draw. Executing it
    // with restoreContextState=true preserves the primary immediate pipeline and
    // its engine caches; no scheduler, pooled render context or world draw runs.
    internal sealed class DisplayOcclusionRendererNative : DisplayOcclusionQueryNative.IEnclosureRenderer, IDisposable
    {
        const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        internal const string VertexSource = "float4 main(uint id:SV_VertexID):SV_Position { float2 p=float2((id<<1)&2,id&2); return float4(p*float2(2,-2)+float2(-1,1),0,1); }";
        internal const string PixelSource = "cbuffer Enclosure:register(b0) { float4 closest; }; float main(float4 p:SV_Position,uint sample:SV_SampleIndex):SV_Depth { return min(1.0,closest.x+sample*1e-8); }";
        readonly PropertyInfo device, nativeContext, vertexStage, pixelStage, rasterStage, mergerStage, inputStage, topology, rasterState;
        readonly PropertyInfo depthSize, readonlyView, nativeView;
        readonly ConstructorInfo contextConstructor, vertexConstructor, pixelConstructor, bufferConstructor, depthConstructor, rasterConstructor;
        readonly Type bufferDescription, depthDescription, rasterDescription, targetView;
        readonly MethodInfo clear, map, unmap, setVertex, setPixel, setConstants, setTargets, setDepth, setBlend, setViewport, draw, finish, execute;
        readonly FieldInfo pointer;
        readonly byte[] vertexBytes, pixelBytes;
        readonly object triangleList;
        object resourceDevice, context, vertex, pixel, constants, depthState, rasterizer;
        object primaryDepth, boundDevice;
        DisplayOcclusion.Stamp frame;
        Func<DisplayOcclusion.Stamp, bool> validator;
        bool healthy = true, disposed;

        internal DisplayOcclusionRendererNative(Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException("assembly");
            Func<string, Type> t = name => Find(assembly, name);
            var dx = t("SharpDX.Direct3D11.DeviceContext"); var dev = t("SharpDX.Direct3D11.Device");
            var buffer = t("SharpDX.Direct3D11.Buffer"); var linkage = t("SharpDX.Direct3D11.ClassLinkage");
            device = P(t("VRageRender.MyRender11"), "DeviceInstance", S);
            nativeContext = P(t("VRage.Render11.RenderContext.MyRenderContext"), "DeviceContext", I);
            depthSize = P(t("VRage.Render11.Resources.IResource"), "Size", I);
            readonlyView = P(t("VRage.Render11.Resources.IDepthStencil"), "DsvRo", I);
            nativeView = P(t("VRage.Render11.Resources.IDsvBindable"), "Dsv", I);
            contextConstructor = C(dx, dev);
            vertexConstructor = C(t("SharpDX.Direct3D11.VertexShader"), dev, typeof(byte[]), linkage);
            pixelConstructor = C(t("SharpDX.Direct3D11.PixelShader"), dev, typeof(byte[]), linkage);
            bufferDescription = t("SharpDX.Direct3D11.BufferDescription");
            depthDescription = t("SharpDX.Direct3D11.DepthStencilStateDescription");
            rasterDescription = t("SharpDX.Direct3D11.RasterizerStateDescription");
            bufferConstructor = C(buffer, dev, bufferDescription);
            depthConstructor = C(t("SharpDX.Direct3D11.DepthStencilState"), dev, depthDescription);
            rasterConstructor = C(t("SharpDX.Direct3D11.RasterizerState"), dev, rasterDescription);
            vertexStage = P(dx, "VertexShader", I); pixelStage = P(dx, "PixelShader", I);
            rasterStage = P(dx, "Rasterizer", I); mergerStage = P(dx, "OutputMerger", I); inputStage = P(dx, "InputAssembler", I);
            topology = P(inputStage.PropertyType, "PrimitiveTopology", I); triangleList = Enum.Parse(topology.PropertyType, "TriangleList");
            rasterState = P(rasterStage.PropertyType, "State", I);
            clear = M(dx, "ClearState");
            map = M(dx, "MapSubresource", t("SharpDX.Direct3D11.Resource"), typeof(int), t("SharpDX.Direct3D11.MapMode"), t("SharpDX.Direct3D11.MapFlags"));
            pointer = map.ReturnType.GetField("DataPointer", I) ?? throw Changed("map data pointer");
            unmap = M(dx, "UnmapSubresource", t("SharpDX.Direct3D11.Resource"), typeof(int));
            setVertex = M(vertexStage.PropertyType, "Set", vertexConstructor.DeclaringType);
            setPixel = M(pixelStage.PropertyType, "Set", pixelConstructor.DeclaringType);
            setConstants = M(pixelStage.PropertyType, "SetConstantBuffer", typeof(int), buffer);
            targetView = t("SharpDX.Direct3D11.RenderTargetView");
            setTargets = M(mergerStage.PropertyType, "SetTargets", t("SharpDX.Direct3D11.DepthStencilView"), targetView.MakeArrayType());
            setDepth = M(mergerStage.PropertyType, "SetDepthStencilState", depthConstructor.DeclaringType, typeof(int));
            setBlend = M(mergerStage.PropertyType, "SetBlendState", t("SharpDX.Direct3D11.BlendState"), typeof(Nullable<>).MakeGenericType(t("SharpDX.Mathematics.Interop.RawColor4")), typeof(int));
            setViewport = M(rasterStage.PropertyType, "SetViewport", typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float));
            draw = M(dx, "Draw", typeof(int), typeof(int)); finish = M(dx, "FinishCommandList", typeof(bool));
            execute = M(dx, "ExecuteCommandList", finish.ReturnType, t("SharpDX.Mathematics.Interop.RawBool"));
            vertexBytes = Compile(VertexSource, "vs_5_0"); pixelBytes = Compile(PixelSource, "ps_5_0");
        }
        internal void Bind(DisplayOcclusion.Stamp snapshot, object rawOpaqueDepth, Func<DisplayOcclusion.Stamp, bool> current)
        {
            validator = null; primaryDepth = null;
            if (disposed || !healthy || !snapshot.Valid || rawOpaqueDepth == null || current == null) return;
            var size = (Vector2I)depthSize.GetValue(rawOpaqueDepth);
            if (size.X != snapshot.Width || size.Y != snapshot.Height || readonlyView.GetValue(rawOpaqueDepth) == null ||
                nativeView.GetValue(readonlyView.GetValue(rawOpaqueDepth)) == null) return;
            frame = snapshot; primaryDepth = rawOpaqueDepth; boundDevice = device.GetValue(null); validator = current;
        }
        internal void Invalidate() { validator = null; primaryDepth = null; }
        public bool CurrentFrameOpaqueProof { get { return !disposed && healthy && validator != null && primaryDepth != null; } }
        public bool Matches(DisplayOcclusion.Stamp stamp)
        { return CurrentFrameOpaqueProof && frame.SameFrame(stamp) && ReferenceEquals(boundDevice, device.GetValue(null)) && validator(stamp); }
        public void Draw(object immediateContext, DisplayOcclusion.Stamp stamp, DisplayOcclusion.Footprint footprint)
        {
            if (!Matches(stamp) || !footprint.Valid(stamp)) throw new InvalidOperationException("Current primary opaque proof changed.");
            object list = null;
            try
            {
                EnsureResources(); clear.Invoke(context, null);
                object mapped = map.Invoke(context, new[] { constants, (object)0, Enum.Parse(map.GetParameters()[2].ParameterType, "WriteDiscard"), Enum.ToObject(map.GetParameters()[3].ParameterType, 0) });
                try
                {
                    var address = (IntPtr)pointer.GetValue(mapped);
                    if (address == IntPtr.Zero) throw new InvalidOperationException("Enclosure constant buffer mapping returned no storage.");
                    Marshal.Copy(new[] { (float)footprint.ClosestReverseDepth, 0f, 0f, 0f }, 0, address, 4);
                }
                finally { unmap.Invoke(context, new[] { constants, (object)0 }); }
                var ps = pixelStage.GetValue(context); var rs = rasterStage.GetValue(context); var om = mergerStage.GetValue(context);
                topology.SetValue(inputStage.GetValue(context), triangleList);
                rasterState.SetValue(rs, rasterizer);
                setViewport.Invoke(rs, new object[] { (float)footprint.Left, (float)footprint.Top, (float)(footprint.Right - footprint.Left), (float)(footprint.Bottom - footprint.Top), 0f, 1f });
                setVertex.Invoke(vertexStage.GetValue(context), new[] { vertex }); setPixel.Invoke(ps, new[] { pixel });
                setConstants.Invoke(ps, new[] { (object)0, constants });
                setTargets.Invoke(om, new[] { nativeView.GetValue(readonlyView.GetValue(primaryDepth)), Array.CreateInstance(targetView, 0) });
                setDepth.Invoke(om, new[] { depthState, (object)0 });
                setBlend.Invoke(om, new object[] { null, null, -1 }); // Explicit full sample mask; default blend disables alpha-to-coverage.
                draw.Invoke(context, new object[] { 3, 0 });
                list = finish.Invoke(context, new object[] { false });
                // Restore=true preserves ALL primary GPU state, including the
                // immediate native query interval owned by the backend.
                execute.Invoke(nativeContext.GetValue(immediateContext), new[] { list, RawBool(execute.GetParameters()[1].ParameterType, true) });
            }
            catch { healthy = false; Invalidate(); throw; }
            finally { DisposeObject(list); }
        }
        void EnsureResources()
        {
            var active = device.GetValue(null);
            if (active == null) throw new InvalidOperationException("Primary device unavailable.");
            if (ReferenceEquals(resourceDevice, active) && context != null) return;
            Release(); resourceDevice = active;
            context = contextConstructor.Invoke(new[] { active });
            vertex = vertexConstructor.Invoke(new[] { active, (object)vertexBytes, null });
            pixel = pixelConstructor.Invoke(new[] { active, (object)pixelBytes, null });
            var cb = Activator.CreateInstance(bufferDescription);
            Set(cb, "SizeInBytes", 16); SetEnum(cb, "Usage", "Dynamic"); SetEnum(cb, "BindFlags", "ConstantBuffer"); SetEnum(cb, "CpuAccessFlags", "Write");
            constants = bufferConstructor.Invoke(new[] { active, cb });
            var ds = CreateDepthDescription();
            depthState = depthConstructor.Invoke(new[] { active, ds });
            var rd = CreateRasterDescription();
            rasterizer = rasterConstructor.Invoke(new[] { active, rd });
        }
        internal object CreateDepthDescription()
        {
            var ds = Activator.CreateInstance(depthDescription);
            SetBool(ds, "IsDepthEnabled", true); SetEnum(ds, "DepthWriteMask", "Zero"); SetEnum(ds, "DepthComparison", "GreaterEqual"); SetBool(ds, "IsStencilEnabled", false);
            return ds;
        }
        internal object CreateRasterDescription()
        {
            var rd = Activator.CreateInstance(rasterDescription);
            SetEnum(rd, "FillMode", "Solid"); SetEnum(rd, "CullMode", "None"); SetBool(rd, "IsDepthClipEnabled", true);
            SetBool(rd, "IsScissorEnabled", false); SetBool(rd, "IsMultisampleEnabled", true);
            return rd;
        }
        internal static byte[] Compile(string source, string profile)
        {
            var compiler = Assembly.Load("SharpDX.D3DCompiler"); var shader = compiler.GetType("SharpDX.D3DCompiler.ShaderBytecode", true);
            var method = shader.GetMethods(S).Single(m => m.Name == "Compile" && m.GetParameters().Length == 8 && m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters()[6].ParameterType.Name == "SecondaryDataFlags");
            var p = method.GetParameters(); var compiled = method.Invoke(null, new object[] { source, "main", profile, Enum.ToObject(p[3].ParameterType, 0), Enum.ToObject(p[4].ParameterType, 0), "HDR.Display.Enclosure", Enum.ToObject(p[6].ParameterType, 0), null });
            try { var code = compiled.GetType().GetProperty("Bytecode", I).GetValue(compiled); var bytes = (byte[])shader.GetProperty("Data", I).GetValue(code); if (bytes == null || bytes.Length == 0 || bytes.Length > 65536) throw Changed("enclosure bytecode"); return (byte[])bytes.Clone(); }
            finally { DisposeObject(compiled); }
        }
        void Release()
        {
            DisposeObject(context); DisposeObject(vertex); DisposeObject(pixel); DisposeObject(constants); DisposeObject(depthState); DisposeObject(rasterizer);
            context = vertex = pixel = constants = depthState = rasterizer = resourceDevice = null;
        }
        public void Dispose() { disposed = true; Invalidate(); Release(); }
        static Type Find(Assembly assembly, string name)
        { var t = assembly.GetType(name, false) ?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(x => x != null); if (t != null) return t; var root = name.StartsWith("SharpDX.Mathematics.", StringComparison.Ordinal) ? "SharpDX" : "SharpDX.Direct3D11"; return Assembly.Load(root).GetType(name, true); }
        static Exception Changed(string name) { return new InvalidOperationException("Display enclosure ABI changed: " + name); }
        static PropertyInfo P(Type t, string n, BindingFlags f) { return t.GetProperty(n, f) ?? throw Changed(t + "." + n); }
        static ConstructorInfo C(Type t, params Type[] args) { return t.GetConstructor(args) ?? throw Changed(t + " constructor"); }
        static MethodInfo M(Type t, string n, params Type[] args) { return t.GetMethod(n, I, null, args, null) ?? throw Changed(t + "." + n); }
        static void Set(object o, string n, object value) { o.GetType().GetField(n, I).SetValue(o, value); }
        static void SetEnum(object o, string n, string value) { var f = o.GetType().GetField(n, I); f.SetValue(o, Enum.Parse(f.FieldType, value)); }
        static void SetBool(object o, string n, bool value) { var f = o.GetType().GetField(n, I); f.SetValue(o, f.FieldType == typeof(bool) ? (object)value : RawBool(f.FieldType, value)); }
        static object RawBool(Type t, bool value) { return t.GetMethod("op_Implicit", S, null, new[] { typeof(bool) }, null).Invoke(null, new object[] { value }); }
        static void DisposeObject(object o) { if (o is IDisposable) ((IDisposable)o).Dispose(); }
    }
}
