using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using VRageRender;

namespace HDRClientRenderer
{
    // One fixed fullscreen pixel shader. Only trusted capture texture identities
    // and bounded numeric bases reach this hook; no caller shader or file is read.
    internal sealed class NativePanoramaGpu : IDisposable
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static NativePanoramaGpu active;
        readonly PanoramaQueue queue = new PanoramaQueue();
        readonly int[] failures = new int[PanoramaStore.MaxSlots];
        readonly int[] initializedTargets=new int[PanoramaStore.MaxSlots],readyTargets=new int[PanoramaStore.MaxSlots];
        Harmony harmony;
        MethodInfo original, prefix;
        GpuPass pass;
        bool attempted, reported;
        int errors;
        int resetResources;
        int resolveAttempts,gpuAttempts;
        Action<string> report;
        Func<PanoramaStore.Face, PanoramaStore.Face> resolveFace;
        ClientPixelBudget budget;
        long lastFrame = long.MinValue;
        internal bool Ready { get { return pass != null && resolveFace != null; } }
        internal void SetResolver(Func<PanoramaStore.Face, PanoramaStore.Face> resolver) { resolveFace = resolver; }
        internal void SetBudget(ClientPixelBudget value){budget=value;}
        static string BudgetKey(string target){return "panorama:"+target;}
        void Trace(string stage,PanoramaQueue.Request request)
        {
            var log=report;if(log==null)return;
            string identities=string.Join(",",request.Faces.Select(face=>face.Camera+"/"+face.Generation));
            log("Panorama first-attempt stage="+stage+" frame="+lastFrame+" target="+request.Target+" size="+request.Width+"x"+request.Height+" sources="+identities);
        }
        internal bool Enqueue(string target, int width, int height, PanoramaStore.Face[] faces, PanoramaStore.Settings settings)
        { return Ready && Healthy(target) && queue.Enqueue(target, width, height, faces, settings,
            ()=>budget==null||budget.Request(BudgetKey(target),ClientPixelBudget.Kind.Panorama,width*height)); }
        internal bool Healthy(string target)
        { int slot = target == "HDR_ClientPanorama_0" ? 0 : target == "HDR_ClientPanorama_1" ? 1 : -1; return slot >= 0 && Interlocked.CompareExchange(ref failures[slot], 0, 0) == 0; }
        internal bool TargetReady(string target)
        {int slot=target=="HDR_ClientPanorama_0"?0:target=="HDR_ClientPanorama_1"?1:-1;return slot>=0&&Interlocked.CompareExchange(ref readyTargets[slot],0,0)==1&&Healthy(target);}
        internal void Cancel(string target)
        { queue.Cancel(target,()=>{if(budget!=null)budget.Cancel(BudgetKey(target));int slot = target == "HDR_ClientPanorama_0" ? 0 : target == "HDR_ClientPanorama_1" ? 1 : -1; if (slot >= 0){Interlocked.Exchange(ref failures[slot], 0);Interlocked.Exchange(ref initializedTargets[slot],0);Interlocked.Exchange(ref readyTargets[slot],0);}}); }
        internal void Clear() { queue.Clear(() => { for (int i = 0; i < failures.Length; i++){if(budget!=null)budget.Cancel(BudgetKey("HDR_ClientPanorama_"+i));Interlocked.Exchange(ref failures[i], 0);Interlocked.Exchange(ref initializedTargets[i],0);Interlocked.Exchange(ref readyTargets[i],0);} Interlocked.Exchange(ref resetResources, 1); }); }
        internal void TryInstall(Action<string> log)
        {
            if (attempted) return;
            var renderer = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRageRender.MyRender11", false)).FirstOrDefault(t => t != null);
            if (renderer == null) return;
            attempted = true; report = log;
            try
            {
                pass = new GpuPass(renderer.Assembly);
                original = renderer.GetMethods(Static).Single(m => m.Name == "DrawGameScene" && m.GetParameters().Length == 2);
                prefix = typeof(NativePanoramaGpu).GetMethod("BeforeScene", Static);
                harmony = new Harmony("HDRClientRenderer.NativePanoramaGpu"); active = this;
                harmony.Patch(original, prefix: new HarmonyMethod(prefix) { priority = Priority.Last });
                log("Direct camera panorama compositor installed; fixed ps_5_0, six sources, normalized overlaps and transparent gaps.");
            }
            catch (Exception error) { Dispose(); log("Camera panorama GPU composition unavailable: " + error.GetBaseException().Message); }
        }
        static void BeforeScene(object __0)
        {
            var compositor = active;
            if (compositor == null || !compositor.Ready || DirectCameraCapture.IsCapturing || !compositor.pass.IsMainTarget(__0)) return;
            long frame = compositor.pass.Frame;
            if (compositor.lastFrame == frame) return; compositor.lastFrame = frame;
            compositor.queue.Drain(request =>
            {
                int slot=request.Target=="HDR_ClientPanorama_0"?0:1;
                if(compositor.resolveAttempts++<4)compositor.Trace("resolve-faces",request);
                // A host-signalled reset may reuse its device wrapper. Recreate
                // shader/CB resources on this serialized render boundary too.
                if (Interlocked.Exchange(ref compositor.resetResources, 0) != 0) compositor.pass.Dispose();
                if(Interlocked.CompareExchange(ref compositor.initializedTargets[slot],0,0)==0)
                {
                    if(!compositor.pass.InitializeTarget(request.Target,request.Width,request.Height))return false;
                    Interlocked.Exchange(ref compositor.initializedTargets[slot],1);
                    if(compositor.budget!=null)compositor.budget.RecordBandwidth((long)request.Width*request.Height+ClientPixelBudget.MipPixels(request.Width,request.Height));
                }
                var resolved = new PanoramaStore.Face[request.Faces.Length];
                for (int i = 0; i < resolved.Length; i++)
                {
                    resolved[i] = compositor.resolveFace(request.Faces[i]);
                    if (resolved[i] == null || !resolved[i].Valid) return false;
                }
                request.Faces = resolved; request.Constants = PanoramaShader.Constants(resolved, request.Settings);
                if (request.Constants == null) return false;
                Action<string> trace=compositor.gpuAttempts++<2?(Action<string>)(stage=>compositor.Trace(stage,request)):null;
                bool submitted = compositor.pass.Compose(request,trace);
                if(submitted){Interlocked.Exchange(ref compositor.readyTargets[slot],1);if(compositor.budget!=null){compositor.budget.RecordBandwidth((long)request.Width*request.Height*(request.Faces.Length+2)+ClientPixelBudget.MipPixels(request.Width,request.Height));compositor.budget.Complete(BudgetKey(request.Target));}}
                if (submitted && !compositor.reported)
                {
                    compositor.reported = true;
                    compositor.report("Camera panorama GPU composition submitted: " + request.Target + " " + request.Width + "x" + request.Height + "; " + request.Faces.Length + " sources; command submission is not visual verification.");
                }
                return submitted;
            }, message => { if (compositor.errors++ < 8) compositor.report(message); }, request =>
            { if(compositor.budget!=null)compositor.budget.Cancel(BudgetKey(request.Target));int slot = request.Target == "HDR_ClientPanorama_0" ? 0 : 1; Interlocked.Exchange(ref compositor.failures[slot], 1); },
            request=>compositor.budget==null||compositor.budget.TrySpend(BudgetKey(request.Target),ClientPixelBudget.Kind.Panorama,request.Width*request.Height));
        }
        public void Dispose()
        {
            if (ReferenceEquals(active, this)) active = null;
            if (harmony != null && original != null && prefix != null) try { harmony.Unpatch(original, prefix); } catch { }
            harmony = null;
            queue.Clear(() => { if (pass != null) pass.Dispose(); pass = null; });
        }
        internal sealed class GpuPass : IDisposable
        {
            readonly PropertyInfo rc, pixel, rtv, srv, device, backbuffer, frameCounter;
            readonly FieldInfo fileTextures, buffers, blend, depth, linear;
            readonly MethodInfo tryTexture, clear, clearRtv, setBlend, setTarget, setDepth, setShader, setSrv, setSampler, draw,
                createBuffer, disposeBuffers, mapDiscard, writeFloats, unmap, setConstantBuffer, generateMips;
            readonly ConstructorInfo colour, pixelShaderConstructor;
            readonly Type constantBufferType;
            readonly byte[] bytecode;
            readonly PanoramaGeneratedTarget generatedTarget;
            object currentDevice, shader, constantBuffer;
            internal int ShaderBytes { get { return bytecode.Length; } }
            internal GpuPass(Assembly assembly)
            {
                Func<string, Type> type = name => assembly.GetType(name, true);
                generatedTarget=new PanoramaGeneratedTarget(assembly);
                var renderer = type("VRageRender.MyRender11");
                var context = type("VRage.Render11.RenderContext.MyRenderContext");
                var ps = type("VRage.Render11.RenderContext.MyPixelStage");
                var common = type("VRage.Render11.RenderContext.MyCommonStage");
                var texture = type("VRage.Render11.Resources.IUserGeneratedTexture");
                var rtvType = type("VRage.Render11.Resources.IRtvBindable");
                var srvType = type("VRage.Render11.Resources.ISrvBindable");
                var bufferType = type("VRage.Render11.Resources.IBuffer");
                constantBufferType = type("VRage.Render11.Resources.IConstantBuffer");
                var mapping = type("VRageRender.MyMapping");
                var bufferManager = type("VRage.Render11.Resources.MyBufferManager");
                rc = renderer.GetProperty("RC", Static); device = renderer.GetProperty("DeviceInstance", Static);
                backbuffer = renderer.GetProperty("Backbuffer", Static); frameCounter = type("VRageRender.MyCommon").GetProperty("FrameCounter", Static);
                pixel = context.GetProperty("PixelShader", Instance);
                rtv = rtvType.GetProperty("Rtv"); srv = srvType.GetProperty("Srv");
                fileTextures = type("VRage.Render11.Common.MyManagers").GetField("FileTextures", Static);
                buffers = type("VRage.Render11.Common.MyManagers").GetField("Buffers", Static);
                blend = type("VRage.Render11.Resources.MyBlendStateManager").GetField("BlendReplace", Static);
                depth = type("VRage.Render11.Resources.MyDepthStencilStateManager").GetField("IgnoreDepthStencil", Static);
                linear = type("VRage.Render11.Resources.MySamplerStateManager").GetField("Linear", Static);
                tryTexture = type("VRage.Render11.Resources.MyFileTextureManager").GetMethod("TryGetTexture", Instance, null, new[] { typeof(string), texture.MakeByRefType() }, null);
                clear = context.GetMethod("ClearState", Instance, null, Type.EmptyTypes, null);
                generateMips = context.GetMethod("GenerateMips", Instance, null, new[] { srvType }, null);
                clearRtv = context.GetMethods(Instance).Single(m => m.Name == "ClearRtv" && m.GetParameters().Length == 2);
                colour = clearRtv.GetParameters()[1].ParameterType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
                setBlend = context.GetMethods(Instance).Single(m => m.Name == "SetBlendState" && m.GetParameters().Length == 2);
                setTarget = context.GetMethod("SetRtv", Instance, null, new[] { rtvType }, null);
                setDepth = context.GetMethods(Instance).Single(m => m.Name == "SetDepthStencilState" && m.GetParameters().Length == 2);
                setShader = ps.GetMethods(Instance).Single(m => m.Name == "Set" && m.GetParameters().Length == 1);
                setSrv = common.GetMethod("SetSrv", Instance, null, new[] { typeof(int), srvType }, null);
                setSampler = common.GetMethods(Instance).Single(m => m.Name == "SetSampler" && m.GetParameters().Length == 2);
                setConstantBuffer = ps.GetMethod("SetConstantBuffer", Instance, null, new[] { typeof(int), constantBufferType }, null);
                draw = type("VRageRender.MyScreenPass").GetMethods(Static).Single(m => m.Name == "DrawFullscreenQuad" && m.GetParameters().Length == 3);
                createBuffer = bufferManager.GetMethods(Instance).Single(m => m.Name == "CreateConstantBuffer" && m.GetParameters().Length == 5);
                disposeBuffers = bufferManager.GetMethod("Dispose", Instance, null, new[] { constantBufferType.MakeArrayType() }, null);
                mapDiscard = mapping.GetMethod("MapDiscard", Static, null, new[] { context, bufferType }, null);
                writeFloats = mapping.GetMethods(Instance).Single(m => m.Name == "WriteAndPosition" && m.IsGenericMethodDefinition && m.GetParameters().Length == 3).MakeGenericMethod(typeof(float));
                unmap = mapping.GetMethod("Unmap", Instance, null, Type.EmptyTypes, null);
                var d3d = Assembly.Load("SharpDX.Direct3D11");
                var nativeDevice = d3d.GetType("SharpDX.Direct3D11.Device", true);
                pixelShaderConstructor = d3d.GetType("SharpDX.Direct3D11.PixelShader", true).GetConstructor(new[] { nativeDevice, typeof(byte[]), d3d.GetType("SharpDX.Direct3D11.ClassLinkage", true) });
                if (new object[] { rc, device, backbuffer, frameCounter, pixel, rtv, srv, fileTextures, buffers, blend, depth, linear, tryTexture, clear, generateMips, clearRtv,
                    colour, setBlend, setTarget, setDepth, setShader, setSrv, setSampler, setConstantBuffer, draw, createBuffer,
                    disposeBuffers, mapDiscard, writeFloats, unmap, pixelShaderConstructor }.Any(item => item == null))
                    throw new InvalidOperationException("Panorama GPU renderer ABI is incomplete: " + string.Join(", ",
                        GetType().GetFields(Instance).Where(field => typeof(MemberInfo).IsAssignableFrom(field.FieldType) && field.GetValue(this) == null).Select(field => field.Name)));
                if (draw.GetParameters()[2].ParameterType != typeof(bool) || draw.GetParameters()[2].Name != "updateVertexBuffer")
                    throw new InvalidOperationException("Panorama fullscreen vertex binding ABI changed.");
                bytecode = CompileFixedShader();
            }
            internal static bool SameMainTarget(object target, object main) { return target != null && main != null && ReferenceEquals(target, main); }
            internal bool IsMainTarget(object target) { return target != null && SameMainTarget(target, backbuffer.GetValue(null)); }
            internal long Frame { get { return (long)frameCounter.GetValue(null); } }
            internal static byte[] CompileFixedShader()
            {
                var compiler = Assembly.Load("SharpDX.D3DCompiler");
                var shaderType = compiler.GetType("SharpDX.D3DCompiler.ShaderBytecode", true);
                var compile = shaderType.GetMethods(Static).Single(m => m.Name == "Compile" && m.GetParameters().Length == 8
                    && m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters()[6].ParameterType.Name == "SecondaryDataFlags");
                var parameters = compile.GetParameters();
                object result = compile.Invoke(null, new object[] { PanoramaShader.Source, "main", "ps_5_0", Enum.ToObject(parameters[3].ParameterType, 0),
                    Enum.ToObject(parameters[4].ParameterType, 0), "HDR_ClientPanorama_Fixed", Enum.ToObject(parameters[6].ParameterType, 0), null });
                try
                {
                    object code = result.GetType().GetProperty("Bytecode", Instance).GetValue(result);
                    var bytes = (byte[])shaderType.GetProperty("Data", Instance).GetValue(code);
                    if (bytes == null || bytes.Length == 0 || bytes.Length > 256 * 1024) throw new InvalidOperationException("Fixed panorama shader compilation produced invalid bytecode.");
                    return (byte[])bytes.Clone();
                }
                finally { var disposable = result as IDisposable; if (disposable != null) disposable.Dispose(); }
            }
            void EnsureResources(Action<string> trace)
            {
                object activeDevice = device.GetValue(null);
                if (activeDevice == null) throw new InvalidOperationException("Panorama device unavailable.");
                if (ReferenceEquals(currentDevice, activeDevice) && shader != null && constantBuffer != null) return;
                Dispose(); currentDevice = activeDevice;
                if(trace!=null)trace("create-pixel-shader");
                shader = pixelShaderConstructor.Invoke(new[] { activeDevice, (object)bytecode, null });
                var parameters = createBuffer.GetParameters();
                if(trace!=null)trace("create-constant-buffer bytes="+PanoramaShader.ConstantBytes);
                constantBuffer = createBuffer.Invoke(buffers.GetValue(null), new object[] { "HDR_CameraPanoramaConstants", PanoramaShader.ConstantBytes,
                    null, Enum.ToObject(parameters[3].ParameterType, 2), false });
                if (shader == null || constantBuffer == null) throw new InvalidOperationException("Panorama shader/buffer allocation failed.");
            }
            internal void DrawQuad(object context, int width, int height) { draw.Invoke(null, new[] { context, (object)new MyViewport(width, height), true }); }
            internal void RegenerateTargetMips(object context, object target) { clear.Invoke(context, null); generateMips.Invoke(context, new[] { target }); }
            internal bool InitializeTarget(string name,int width,int height)
            {
                var target=new object[]{name,null};
                if(!(bool)tryTexture.Invoke(fileTextures.GetValue(null),target)||target[1]==null)return false;
                if(!generatedTarget.Matches(target[1],width,height))return false;
                if(rtv.GetValue(target[1])==null||srv.GetValue(target[1])==null)if(!generatedTarget.Activate(target[1],width,height))return false;
                if(rtv.GetValue(target[1])==null||srv.GetValue(target[1])==null)return false;
                object context=rc.GetValue(null);
                try{clear.Invoke(context,null);clearRtv.Invoke(context,new[]{target[1],colour.Invoke(new object[]{0f,0f,0f,0f})});RegenerateTargetMips(context,target[1]);return true;}
                finally{clear.Invoke(context,null);}
            }
            internal void UploadConstants(object context,float[] constants,Action<string> trace=null)
            {
                if(!PanoramaShader.ValidConstants(constants))throw new ArgumentException("Panorama constants must be exactly 304 finite bytes.");
                if(constantBuffer==null)throw new InvalidOperationException("Panorama constant buffer is unavailable.");
                if(trace!=null)trace("map-discard");
                object mapped=mapDiscard.Invoke(null,new[]{context,constantBuffer});
                try
                {
                    if(trace!=null)trace("write-constants floats="+constants.Length+" bytes="+PanoramaShader.ConstantBytes);
                    writeFloats.Invoke(mapped,new object[]{constants,constants.Length,0});
                }
                finally { if(trace!=null)trace("unmap");unmap.Invoke(mapped,null); }
            }
            internal bool Compose(PanoramaQueue.Request request,Action<string> trace=null)
            {
                if(request==null||!PanoramaShader.ValidConstants(request.Constants))throw new ArgumentException("Invalid panorama GPU request constants.");
                object manager = fileTextures.GetValue(null);
                var target = new object[] { request.Target, null };
                if (!(bool)tryTexture.Invoke(manager, target) || target[1] == null || rtv.GetValue(target[1]) == null) return false;
                if(!generatedTarget.Matches(target[1],request.Width,request.Height))return false;
                var sources = new object[request.Faces.Length];
                for (int i = 0; i < sources.Length; i++)
                {
                    var source = new object[] { request.Faces[i].Texture, null };
                    if (!(bool)tryTexture.Invoke(manager, source) || source[1] == null || ReferenceEquals(source[1], target[1]) || srv.GetValue(source[1]) == null) return false;
                    sources[i] = source[1];
                }
                object replace = blend.GetValue(null), depthState = depth.GetValue(null), sampler = linear.GetValue(null);
                if (replace == null || depthState == null || sampler == null) return false;
                if(trace!=null)trace("ensure-resources");
                EnsureResources(trace);
                if(trace!=null)trace("resources-ready");
                object context = rc.GetValue(null), stage = pixel.GetValue(context);
                try
                {
                    clear.Invoke(context, null);
                    if(trace!=null)trace("clear-target");
                    clearRtv.Invoke(context, new[] { target[1], colour.Invoke(new object[] { 0f, 0f, 0f, 0f }) });
                    UploadConstants(context,request.Constants,trace);
                    setBlend.Invoke(context, new[] { replace, null });
                    setDepth.Invoke(context, new[] { depthState, (object)0 });
                    setShader.Invoke(stage, new[] { shader });
                    setConstantBuffer.Invoke(stage, new[] { (object)1, constantBuffer });
                    setTarget.Invoke(context, new[] { target[1] });
                    for (int i = 0; i < PanoramaStore.MaxFaces; i++) setSrv.Invoke(stage, new[] { (object)i, i < sources.Length ? sources[i] : null });
                    setSampler.Invoke(stage, new[] { (object)2, sampler });
                    if(trace!=null)trace("draw-quad");
                    DrawQuad(context, request.Width, request.Height);
                    // The installed generated-texture API creates a full mipchain
                    // when it creates an RTV. Unbind the target before regeneration.
                    if(trace!=null)trace("generate-mips");
                    RegenerateTargetMips(context, target[1]);
                    if(trace!=null)trace("compose-complete");
                    return true;
                }
                finally { clear.Invoke(context, null); }
            }
            public void Dispose()
            {
                if (constantBuffer != null)
                {
                    try { var array = Array.CreateInstance(constantBufferType, 1); array.SetValue(constantBuffer, 0); disposeBuffers.Invoke(buffers.GetValue(null), new object[] { array }); } catch { }
                    constantBuffer = null;
                }
                var disposable = shader as IDisposable; if (disposable != null) disposable.Dispose();
                shader = currentDevice = null;
            }
        }
    }
}
