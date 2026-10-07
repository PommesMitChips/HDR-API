using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VRageMath;

namespace HDRClientRenderer
{
    // The installed game owns its particle shader source. Read only its fixed
    // asset at runtime, validate the two injection points and compile a private
    // variant. No game source is written, bundled or supplied by a caller.
    internal sealed class DirectCameraCaptureParticles
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const int TranslationSlot = 6;
        [ThreadStatic] static DirectCameraCaptureParticles drawing;
        internal readonly MethodInfo Run, VertexSet;
        readonly Func<string, Type> type;
        readonly FieldInfo texture, emissive, reset, ordinaryId, debugId;
        readonly MethodInfo render, getShader, getCompilationInfo, fillMacros, entryPoint, profileName;
        readonly PropertyInfo shaderInfo, shaderRoot, globalMacros;
        readonly MethodInfo compile, map, write, unmap, createBuffer, disposeBuffer;
        readonly ConstructorInfo include, vertexConstructor;
        readonly PropertyInfo vertex, pixel, all;
        readonly MethodInfo setDepth, setRasterizer, setLayout, setFrame, setVertexBuffer, setSrv, setAllSrv,
            setSamplers, setSampler;
        readonly FieldInfo vertexConstants;
        readonly FieldInfo buffers, rasterizer, samplers, shadowSampler, shadows;
        readonly PropertyInfo frame, depth;
        readonly MethodInfo cascadeBuffer, cascadeMap;
        readonly Type macro, constantBuffer;
        object translation, ordinaryShader, debugShader, ordinaryOriginal, debugOriginal, device;
        Vector3 delta;

        internal DirectCameraCaptureParticles(Assembly assembly)
        {
            type = name => DirectCameraCaptureNative.FindType(assembly, name);
            var particles = type("VRageRender.MyGPUParticleRenderer");
            var context = type("VRage.Render11.RenderContext.MyRenderContext");
            var srv = type("VRage.Render11.Resources.ISrvBindable");
            var vertexType = type("VRage.Render11.RenderContext.MyVertexStage");
            var common = type("VRage.Render11.RenderContext.MyCommonStage");
            var shaderType = type("SharpDX.Direct3D11.VertexShader");
            var managers = type("VRage.Render11.Common.MyManagers");
            texture = F(particles, "m_textureArraySrv", Static); emissive = F(particles, "m_emissiveArraySrv", Static);
            reset = F(particles, "m_resetSystem", Static);
            ordinaryId = F(particles, "m_vs", Static); debugId = F(particles, "m_vsShadowDebug", Static);
            Run = M(particles, "Run", Static, context, srv, srv, typeof(bool));
            render = M(particles, "Render", Static, context, srv, srv);
            VertexSet = M(vertexType, "Set", Instance, shaderType);
            var shaders = type("VRageRender.MyVertexShaders");
            getShader = M(shaders, "GetShader", Static, ordinaryId.FieldType);
            shaderInfo = P(ordinaryId.FieldType, "InfoId", Instance);
            getCompilationInfo = M(type("VRageRender.MyShaders"), "GetCompilationInfo", Static, shaderInfo.PropertyType);
            var compiler = type("VRageRender.MyShaderCompiler");
            macro = type("SharpDX.Direct3D.ShaderMacro");
            globalMacros = P(compiler, "GlobalShaderMacros", Static); shaderRoot = P(compiler, "ShadersPath", Static);
            fillMacros = M(compiler, "FillGlobalMacros", Static, typeof(List<>).MakeGenericType(macro), typeof(bool));
            var profile = type("VRageRender.MyShaderProfile");
            entryPoint = M(compiler, "ProfileEntryPoint", Static, profile); profileName = M(compiler, "ProfileToString", Static, profile);
            include = Need(type("VRageRender.MyShaderCompiler+MyIncludeProcessor").GetConstructor(Instance, null, new[] { typeof(string) }, null));
            compile = Need(type("SharpDX.D3DCompiler.ShaderBytecode").GetMethods(Static).SingleOrDefault(m => m.Name == "Compile" &&
                m.GetParameters().Length == 10 && m.GetParameters().Take(3).All(p => p.ParameterType == typeof(string)) &&
                m.GetParameters()[6].ParameterType.FullName == "SharpDX.D3DCompiler.Include"));
            vertexConstructor = Need(shaderType.GetConstructor(new[] { type("SharpDX.Direct3D11.Device"), typeof(byte[]), type("SharpDX.Direct3D11.ClassLinkage") }));
            constantBuffer = type("VRage.Render11.Resources.IConstantBuffer");
            var bufferManager = type("VRage.Render11.Resources.MyBufferManager");
            buffers = F(managers, "Buffers", Static);
            createBuffer = M(bufferManager, "CreateConstantBuffer", Instance, typeof(string), typeof(int), typeof(IntPtr?),
                type("SharpDX.Direct3D11.ResourceUsage"), typeof(bool));
            disposeBuffer = M(bufferManager, "Dispose", Instance, constantBuffer.MakeArrayType());
            var mapping = type("VRageRender.MyMapping");
            map = M(mapping, "MapDiscard", Static, context, type("VRage.Render11.Resources.IBuffer"));
            write = Need(mapping.GetMethods(Instance).SingleOrDefault(m => m.Name == "WriteAndPosition" && m.IsGenericMethodDefinition &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsByRef)).MakeGenericMethod(typeof(Vector4));
            unmap = M(mapping, "Unmap", Instance);
            vertex = P(context, "VertexShader", Instance); pixel = P(context, "PixelShader", Instance); all = P(context, "AllShaderStages", Instance);
            setDepth = M(context, "SetDepthStencilState", Instance, type("VRage.Render11.Resources.IDepthStencilState"), typeof(int));
            setRasterizer = M(context, "SetRasterizerState", Instance, type("VRage.Render11.Resources.IRasterizerState"));
            setLayout = M(context, "SetInputLayout", Instance, type("SharpDX.Direct3D11.InputLayout"));
            setFrame = M(all.PropertyType, "SetConstantBuffer", Instance, typeof(int), constantBuffer);
            // The engine's common shader cache has only eight CB slots. b6 is
            // validated free in the compiled native particle variants below;
            // keep both engine cache and native state aligned through its setter.
            setVertexBuffer = M(vertexType, "SetConstantBuffer", Instance, typeof(int), constantBuffer);
            vertexConstants = F(common, "m_constantBuffers", Instance);
            setSrv = M(common, "SetSrv", Instance, typeof(int), srv);
            setAllSrv = M(all.PropertyType, "SetSrv", Instance, typeof(int), srv);
            setSamplers = M(common, "SetSamplers", Instance, typeof(int), type("VRage.Render11.Resources.ISamplerState").MakeArrayType());
            setSampler = M(common, "SetSampler", Instance, typeof(int), type("VRage.Render11.Resources.ISamplerState"));
            depth = P(type("VRage.Render11.Resources.MyDepthStencilStateManager"), "DefaultDepthState", Static);
            rasterizer = F(type("VRage.Render11.Resources.MyRasterizerStateManager"), "NocullRasterizerState", Static);
            samplers = F(type("VRage.Render11.Resources.MySamplerStateManager"), "StandardSamplers", Static);
            shadowSampler = F(type("VRage.Render11.Resources.MySamplerStateManager"), "Shadowmap", Static);
            shadows = F(managers, "Shadows", Static); frame = P(type("VRageRender.MyCommon"), "FrameConstants", Static);
            cascadeBuffer = M(shadows.FieldType, "get_ShadowCascades", Instance);
            cascadeMap = M(cascadeBuffer.ReturnType, "get_CascadeShadowmapArray", Instance);
        }

        internal void Prepare(object currentDevice, Vector3D simulationOrigin, Vector3D captureOrigin)
        {
            delta = (Vector3)(simulationOrigin - captureOrigin);
            if (texture.GetValue(null) == null || (bool)reset.GetValue(null)) return;
            if (ReferenceEquals(device, currentDevice) && ordinaryShader != null) return;
            Dispose(); device = currentDevice;
            ordinaryOriginal = getShader.Invoke(null, new[] { ordinaryId.GetValue(null) });
            debugOriginal = getShader.Invoke(null, new[] { debugId.GetValue(null) });
            ordinaryShader = vertexConstructor.Invoke(new object[] { device, CompileVariant(ordinaryId.GetValue(null)), null });
            debugShader = vertexConstructor.Invoke(new object[] { device, CompileVariant(debugId.GetValue(null)), null });
            var usage = createBuffer.GetParameters()[3].ParameterType;
            translation = createBuffer.Invoke(buffers.GetValue(null), new object[] { "HDR.DirectCamera.ParticleOrigin", 16, null, Enum.Parse(usage, "Dynamic"), false });
        }
        byte[] CompileVariant(object shaderId)
        {
            object spec = getCompilationInfo.Invoke(null, new[] { shaderInfo.GetValue(shaderId) });
            string nativeFile = F(spec.GetType(), "File", Instance).GetValue(spec).ToString().Replace('\\', '/');
            if (nativeFile != "Transparent/GPUParticles/Render.hlsl")
                throw new InvalidOperationException("Particle shader asset ABI changed.");
            string root = Path.GetFullPath((string)shaderRoot.GetValue(null));
            string path = Path.GetFullPath(Path.Combine(root, "Transparent", "GPUParticles", "Render.hlsl"));
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Particle shader path is outside the installed shader directory.");
            string source = File.ReadAllText(path);
            const string particle = "Particle pa = g_ParticleBuffer[index];", distance = "float distance = length(pa.Position);";
            if (source.Length > 262144 || Count(source, particle) != 1 || Count(source, distance) != 1 ||
                source.IndexOf("HDRCameraParticle", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Particle shader source ABI changed; capture variant was not compiled.");
            var macroList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(macro));
            foreach (var item in (Array)globalMacros.GetValue(null)) macroList.Add(item);
            var ownMacros = F(spec.GetType(), "Macros", Instance).GetValue(spec) as Array;
            if (ownMacros != null) foreach (var item in ownMacros) macroList.Add(item);
            fillMacros.Invoke(null, new object[] { macroList, true });
            var macros = Array.CreateInstance(macro, macroList.Count); macroList.CopyTo(macros, 0);
            object profile = F(spec.GetType(), "Profile", Instance).GetValue(spec);
            string entry = (string)entryPoint.Invoke(null, new[] { profile }), target = (string)profileName.Invoke(null, new[] { profile });
            if (entry != "__vertex_shader" || target != "vs_5_0") throw new InvalidOperationException("Particle vertex profile ABI changed.");
            // Prove the translation slot is free in the compiled installed shader,
            // including all engine includes and this variant's actual defines.
            ValidateBindings(CompileSource(source, entry, target, macros, path), false);
            source = "cbuffer HDRCameraParticleTranslation : register(b6) { float4 HDRCameraParticleOffset; };\n" +
                source.Replace(particle, particle + "\n    pa.Position += HDRCameraParticleOffset.xyz;\n    pa.Origin += HDRCameraParticleOffset.xyz;");
            byte[] variant = CompileSource(source, entry, target, macros, path);
            ValidateBindings(variant, true);
            return variant;
        }
        byte[] CompileSource(string source, string entry, string target, Array macros, string path)
        {
            object includes = include.Invoke(new object[] { path }), result = null;
            try
            {
                var parameters = compile.GetParameters();
                result = compile.Invoke(null, new object[] { source, entry, target, Enum.ToObject(parameters[3].ParameterType, 1 << 15),
                    Enum.ToObject(parameters[4].ParameterType, 0), macros, includes, path,
                    Enum.ToObject(parameters[8].ParameterType, 0), null });
                if ((bool)P(result.GetType(), "HasErrors", Instance).GetValue(result))
                    throw new InvalidOperationException("Private particle vertex shader compilation failed: " + P(result.GetType(), "Message", Instance).GetValue(result));
                object bytecode = P(result.GetType(), "Bytecode", Instance).GetValue(result);
                return (byte[])P(bytecode.GetType(), "Data", Instance).GetValue(bytecode);
            }
            finally { DisposeObject(result); DisposeObject(includes); }
        }
        void ValidateBindings(byte[] bytecode, bool capture)
        {
            var reflectionType = type("SharpDX.D3DCompiler.ShaderReflection");
            object reflection = Need(reflectionType.GetConstructor(new[] { typeof(byte[]) })).Invoke(new object[] { bytecode });
            bool translationFound = false;
            try
            {
                object description = P(reflectionType, "Description", Instance).GetValue(reflection);
                int count = (int)F(description.GetType(), "BoundResources", Instance).GetValue(description);
                var binding = M(reflectionType, "GetResourceBindingDescription", Instance, typeof(int));
                for (int i = 0; i < count; i++)
                {
                    object resource = binding.Invoke(reflection, new object[] { i });
                    int slot = (int)F(resource.GetType(), "BindPoint", Instance).GetValue(resource);
                    int length = (int)F(resource.GetType(), "BindCount", Instance).GetValue(resource);
                    string kind = F(resource.GetType(), "Type", Instance).GetValue(resource).ToString();
                    string name = (string)F(resource.GetType(), "Name", Instance).GetValue(resource);
                    bool translationResource = capture && kind == "ConstantBuffer" && name == "HDRCameraParticleTranslation" && slot == TranslationSlot && length == 1;
                    if (translationResource) { translationFound = true; continue; }
                    // These limits are the installed engine's own cache sizes.
                    // A native binding covering b6 rejects the private variant.
                    if (length < 1 || slot < 0 || kind == "ConstantBuffer" && (slot + length > 8 || slot <= TranslationSlot && slot + length > TranslationSlot) ||
                        kind == "Sampler" && slot + length > 16 || kind != "ConstantBuffer" && kind != "Sampler" && slot + length > 32)
                        throw new InvalidOperationException("Particle shader resource binding ABI changed: " + name + ".");
                }
                if (capture && !translationFound) throw new InvalidOperationException("Private particle translation constant was not bound by the compiled shader.");
            }
            finally { DisposeObject(reflection); }
        }
        internal void DrawExisting(object context, object depthRead)
        {
            if (texture.GetValue(null) == null || (bool)reset.GetValue(null)) return;
            if (ordinaryShader == null || translation == null) throw new InvalidOperationException("Private particle rendering is unavailable.");
            object mapped = map.Invoke(null, new[] { context, translation });
            try { write.Invoke(mapped, new object[] { new Vector4(delta, 0) }); }
            finally { unmap.Invoke(mapped, null); }
            object vs = vertex.GetValue(context), ps = pixel.GetValue(context), allStages = all.GetValue(context);
            object cascades = cascadeBuffer.Invoke(shadows.GetValue(null), null);
            object cascadeConstants = M(cascades.GetType(), "get_CascadeConstantBuffer", Instance).Invoke(cascades, null);
            setDepth.Invoke(context, new[] { depth.GetValue(null), (object)0 });
            setRasterizer.Invoke(context, new[] { rasterizer.GetValue(null) }); setLayout.Invoke(context, new object[] { null });
            setFrame.Invoke(allStages, new[] { (object)0, frame.GetValue(null) });
            setFrame.Invoke(allStages, new[] { (object)4, cascadeConstants });
            setSamplers.Invoke(vs, new[] { (object)0, samplers.GetValue(null) });
            setSamplers.Invoke(ps, new[] { (object)0, samplers.GetValue(null) });
            setSampler.Invoke(vs, new[] { (object)15, shadowSampler.GetValue(null) });
            setSrv.Invoke(vs, new[] { (object)16, cascadeMap.Invoke(cascades, null) });
            setAllSrv.Invoke(allStages, new[] { (object)0, depthRead });
            var previous = drawing; drawing = this;
            try { WithTranslation(context, () => render.Invoke(null, new[] { context, texture.GetValue(null), emissive.GetValue(null) })); }
            finally { drawing = previous; }
        }
        internal void WithTranslation(object context, Action draw)
        {
            object stage = vertex.GetValue(context);
            var bindings = vertexConstants.GetValue(stage) as Array;
            if (bindings == null || bindings.Length != 8) throw new InvalidOperationException("Particle vertex constant-buffer cache ABI changed.");
            object previous = bindings.GetValue(TranslationSlot);
            try
            {
                setVertexBuffer.Invoke(stage, new[] { (object)TranslationSlot, translation });
                draw();
            }
            finally
            {
                // The cache owns the retained buffer reference. Restoring that
                // same owner works for both immediate and deferred contexts.
                setVertexBuffer.Invoke(stage, new[] { (object)TranslationSlot, previous });
            }
        }
        internal static object SelectVertex(object original)
        {
            var capture = drawing; if (capture == null) return original;
            if (ReferenceEquals(original, capture.ordinaryOriginal)) return capture.ordinaryShader;
            else if (ReferenceEquals(original, capture.debugOriginal)) return capture.debugShader;
            else throw new InvalidOperationException("Unexpected particle vertex shader; capture was rejected.");
        }
        internal void Dispose()
        {
            DisposeObject(ordinaryShader); DisposeObject(debugShader);
            ordinaryShader = debugShader = ordinaryOriginal = debugOriginal = device = null;
            if (translation != null)
            { var handles = Array.CreateInstance(constantBuffer, 1); handles.SetValue(translation, 0); disposeBuffer.Invoke(buffers.GetValue(null), new object[] { handles }); translation = null; }
        }
        static int Count(string source, string marker)
        { int count = 0, start = 0; while ((start = source.IndexOf(marker, start, StringComparison.Ordinal)) >= 0) { count++; start += marker.Length; } return count; }
        static void DisposeObject(object value) { var disposable = value as IDisposable; if (disposable != null) disposable.Dispose(); }
        static T Need<T>(T value) where T : class
        { if (value == null) throw new InvalidOperationException("Private particle renderer ABI is unavailable."); return value; }
        static FieldInfo F(Type t, string name, BindingFlags flags) { return Need(t.GetField(name, flags)); }
        static PropertyInfo P(Type t, string name, BindingFlags flags) { return Need(t.GetProperty(name, flags)); }
        static MethodInfo M(Type t, string name, BindingFlags flags, params Type[] parameters)
        { return Need(t.GetMethod(name, flags, null, parameters, null)); }
    }
}
