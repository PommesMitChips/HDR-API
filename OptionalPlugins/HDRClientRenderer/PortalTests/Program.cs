using System.Reflection;
using System.Runtime.Loader;
using HDRClientRenderer;
using VRageMath;
using HarmonyLib;

namespace HDRClientRenderer
{
    internal static class DirectCameraCapture{internal static bool IsCapturing{get;set;}}
    // Type lookup only; no renderer adapter, game or GPU is initialized in these tests.
    internal static class DirectCameraCaptureNative
    {internal static Type FindType(Assembly assembly,string name){return AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null)??throw new Exception("Missing installed type "+name);}}
}
internal static class Program
{
    static int assertions;const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static void Check(bool value,string name){assertions++;if(!value)throw new Exception(name);}
    static bool Exists(string __0,ref bool __result){__result=File.Exists(__0);return false;}
    static void Main()
    {
        string bin=@"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
        AssemblyLoadContext.Default.Resolving+=(c,n)=>{var p=Path.Combine(bin,n.Name+".dll");return File.Exists(p)?c.LoadFromAssemblyPath(p):null;};
        foreach(string dll in new[]{"VRage.Math","VRage.Render","VRage.Library","VRage","SharpDX","SharpDX.DXGI","SharpDX.Direct3D11","SharpDX.D3DCompiler","VRage.Render11"})AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin,dll+".dll"));
        if(Environment.GetEnvironmentVariable("HDR_PORTAL_BILLBOARD_NATIVE_ONLY")=="1"){PortalBillboardNativeChecks.Run(Check);Console.WriteLine("PASS: "+assertions+" projective native billboard assertions; no GPU/game.");return;}
        if(Environment.GetEnvironmentVariable("HDR_PORTAL_BILLBOARD_NATIVE_DUMP")=="1"){PortalBillboardNativeChecks.Dump();return;}
        if(Environment.GetEnvironmentVariable("HDR_PORTAL_BILLBOARD_DUMP")=="1")
        {
            foreach(string name in new[]{"VRageRender.MyBillboardRenderer","VRage.Render11.Resources.MyFileTextureManager","VRage.Render11.Resources.MyGeneratedTextureManager","VRage.Render11.Resources.Internal.MyGeneratedTexture","VRage.Render11.Resources.Internal.MyUserGeneratedTexture","VRageRender.MyTransparentMaterials"})
            {var t=DirectCameraCaptureNative.FindType(null,name);foreach(var m in t.GetMethods(All).Where(m=>m.Name=="AddBatch"||m.Name=="GatherInternal"||m.Name=="GetTexture"||m.Name=="DestroyGeneratedTexture"||m.Name=="CreateGeneratedTexture"||m.Name=="RemoveMaterial"||m.Name=="GetMaterial"||m.Name=="Dispose"||m.Name=="Reset"||m.Name=="ResetUserTexture"))
                {Console.WriteLine("TARGET "+m+" PARAMS "+string.Join(",",m.GetParameters().Select(p=>p.Name)));foreach(var instruction in PatchProcessor.GetOriginalInstructions(m))Console.WriteLine(instruction);}}
            return;
        }
        if(Environment.GetEnvironmentVariable("HDR_PORTAL_COVERAGE_DUMP")=="1")
        {
            var native=DirectCameraCaptureNative.FindType(null,"VRageRender.MyRender11").Assembly;
            foreach(var item in PortalCaptureCoverage.DrawSites(native))Console.WriteLine("DRAW "+item.Key.DeclaringType.FullName+"."+item.Key.Name+" -> "+string.Join(",",item.Value.Select(m=>m.ToString())));
            foreach(var m in PortalCaptureCoverage.ValidateDrawBoundary(native))
            {Console.WriteLine("BODY "+m+" FLAGS "+m.GetMethodImplementationFlags());foreach(var instruction in PatchProcessor.GetOriginalInstructions(m))Console.WriteLine(instruction);}
            foreach(var m in DirectCameraCaptureNative.FindType(null,"SharpDX.Direct3D11.DeviceContext").GetMethods(All).Where(m=>m.Name=="Draw"||m.Name=="DrawIndexed"))
            {Console.WriteLine("NATIVE BODY "+m+" FLAGS "+m.GetMethodImplementationFlags());foreach(var instruction in PatchProcessor.GetOriginalInstructions(m))Console.WriteLine(instruction);}
            foreach(var m in DirectCameraCaptureNative.FindType(null,"VRageRender.MyShaderCompiler").GetMethods(All).Where(m=>m.Name=="PreprocessShader"))
            {Console.WriteLine("PREPROCESS "+m);foreach(var instruction in PatchProcessor.GetOriginalInstructions(m))Console.WriteLine(instruction);}
            foreach(var m in DirectCameraCaptureNative.FindType(null,"VRageRender.MyStereoRender").GetMethods(All).Where(m=>m.Name.Contains("Draw")&&m.Name.Contains("GBuffer")))
            {Console.WriteLine("STEREO "+m);foreach(var instruction in PatchProcessor.GetOriginalInstructions(m))Console.WriteLine(instruction);}
            foreach(string name in new[]{"VRageRender.MyEnvironmentMatrices","VRage.Render11.RenderContext.MyRenderContext","VRage.Render11.RenderContext.MyPixelStage","VRage.Render11.RenderContext.MyCommonStage","VRage.Render11.RenderContext.Internal.MyRenderContextState","VRageRender.MyBillboardRenderer"})
            {var t=DirectCameraCaptureNative.FindType(null,name);foreach(var f in t.GetFields(All))Console.WriteLine("FIELD "+name+" "+f);foreach(var m in t.GetMethods(All).Where(m=>m.Name.StartsWith("Draw")||m.Name.Contains("SetSrv")))Console.WriteLine("METHOD "+name+" "+m);}
            return;
        }
        try{if(Environment.GetEnvironmentVariable("HDR_PORTAL_INTEGRATION_ONLY")!="1"){MathChecks();ShaderChecks(bin);}else DirectCameraCaptureNative.FindType(null,"VRageRender.MyShaderCompiler").GetField("m_shadersPath",All).SetValue(null,Path.GetFullPath(Path.Combine(bin,"..","Content","Shaders")));IntegrationChecks(bin);WarmupChecks.Run(Check);FrontLifetimeChecks.Run(Check);NativeResolutionChecks.Run(Check);PortalCaptureBillboardIsolationChecks.Run(Check);PortalBillboardNativeChecks.Run(Check);Console.WriteLine("PASS: "+assertions+" portal math, fail-closed, installed compiler/reflection and native draw/lifecycle assertions; no GPU objects or game frames.");}
        catch(Exception ex){Console.WriteLine(ex.GetBaseException());Environment.ExitCode=1;}
    }
    static MatrixD ReverseProjection(){var p=MatrixD.Identity;p.M11=p.M22=1;p.M33=0;p.M34=-1;p.M43=.1;p.M44=0;return p;}
    static void MathChecks()
    {
        double distance;Check(PortalProjection.FarIntersection(Vector3D.Zero,-Vector3D.UnitZ,2,out distance)&&Math.Abs(distance-2)<1e-9,"Inside eye far root");
        Check(PortalProjection.FarIntersection(new Vector3D(0,0,-5),-Vector3D.UnitZ,2,out distance)&&Math.Abs(distance-7)<1e-9,"Outside eye skips near branch/prefix");
        Check(!PortalProjection.FarIntersection(new Vector3D(0,0,5),-Vector3D.UnitZ,2,out distance),"Backward sphere misses aperture");
        Check(!PortalProjection.FarIntersection(new Vector3D(5,0,-5),-Vector3D.UnitZ,2,out distance),"Miss ray");
        Check(PortalProjection.FarIntersection(new Vector3D(2,0,-5),-Vector3D.UnitZ,2,out distance)&&Math.Abs(distance-5)<1e-9,"Tangent sphere");
        Check(PortalProjection.EntryIntersection(new Vector3D(0,0,-5),-Vector3D.UnitZ,2,out distance)&&Math.Abs(distance-3)<1e-9,"Exterior entry selects near shell");
        Check(PortalProjection.EntryIntersection(Vector3D.Zero,-Vector3D.UnitZ,2,out distance)&&Math.Abs(distance-2)<1e-9,"Interior entry sole exit shell");
        var entry=MatrixD.CreateTranslation(1e12,0,0);var exit=MatrixD.CreateWorld(new Vector3D(0,1e12,0),Vector3D.UnitX,Vector3D.UnitY);var viewer=entry;viewer.Translation+=new Vector3D(0,0,-.5);
        PortalCaptureSpec spec;string error;
        Check(PortalCaptureSpec.TryCreate(1,2,entry,2,exit,4,viewer,ReverseProjection(),512,512,out spec,out error),"Rotated/scaled/large-coordinate snapshot");
        Check(Vector3D.Distance(spec.VirtualPose.Translation,exit.Translation+Vector3D.UnitX)<1e-6,"Similarity maps viewer eye");
        Check(Vector3D.Distance(spec.MapPoint(entry.Translation),exit.Translation)<1e-6,"Similarity maps centres");
        Check(spec.Cutoff(.5,.5,out distance)&&distance>0&&distance<1,"Reverse-Z cutoff valid");
        var reverse=ReverseProjection();PortalCaptureSpec simple;
        Check(PortalCaptureSpec.TryCreate(0,0,MatrixD.Identity,2,MatrixD.Identity,2,MatrixD.Identity,reverse,512,512,out simple,out error),"Inside simple spec");
        Check(simple.Cutoff(.5,.5,out distance)&&Math.Abs(distance-.1/(2+simple.BoundaryGuard))<1e-10,"Boundary guard moves beyond sphere in reversed Z");
        Check(.1/1>distance&&.1/3<distance,"Near geometry rejected; farther geometry retained before depth write");
        var outside=MatrixD.CreateTranslation(0,0,5);
        Check(PortalCaptureSpec.TryCreate(0,0,MatrixD.Identity,2,MatrixD.Identity,2,outside,reverse,512,512,out simple,out error),"Outside simple spec");
        Check(simple.Cutoff(.5,.5,out distance)&&Math.Abs(distance-.1/(7+simple.BoundaryGuard))<1e-10,"Exterior cutoff uses FAR exit crossing");
        Vector2D uv;Check(simple.EntrySample(new Vector3D(0,0,2),out uv)&&Vector2D.Distance(uv,new Vector2D(.5))<1e-9,"Near entry shell projective sample");
        Check(!simple.EntrySample(new Vector3D(0,0,-2),out uv),"Far entry shell is transparent (no double opacity)");
        Check(!PortalCaptureSpec.TryCreate(0,0,MatrixD.Identity,2,MatrixD.Identity,2,MatrixD.CreateScale(2),reverse,512,512,out simple,out error),"Reject nonrigid viewer");
        Check(!PortalCaptureSpec.TryCreate(0,0,MatrixD.Identity,2,MatrixD.Identity,2,MatrixD.Identity,MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100),512,512,out simple,out error),"Reject forward-Z projection rather than applying reversed-Z clip inequality");
        var scope=new PortalCaptureGate(0,0,1,true,true);Check(scope.Observe(PortalClipFamily.Opaque,true,true)&&scope.Finish(0,0,true),"Known clipped frame passes");
        scope=new PortalCaptureGate(0,0,1,true,true);Check(!scope.Observe(PortalClipFamily.Unknown,false,true)&&!scope.Finish(0,0,true),"Unknown world shader cannot publish");
        scope=new PortalCaptureGate(0,0,4,true,true);Check(!scope.Finish(0,0,true),"MSAA unsupported frame cannot publish");
        scope=new PortalCaptureGate(0,0,1,false,true);Check(!scope.Finish(0,0,true),"Missing pass coverage cannot publish");
        scope=new PortalCaptureGate(0,0,1,true,false);Check(!scope.Finish(0,0,true),"Unknown custom projection billboards cannot publish");
        scope=new PortalCaptureGate(0,0,1,true,true);Check(!scope.Finish(0,0,false),"Unjoined deferred worker cannot publish");
        scope=new PortalCaptureGate(0,0,1,true,true);Check(!scope.Observe(PortalClipFamily.Voxel,true,false)&&!scope.Finish(0,0,true),"Unbound cutoff cannot publish");
        scope=new PortalCaptureGate(0,0,1,true,true);Check(scope.Finish(0,0,true)&&!scope.Observe(PortalClipFamily.Opaque,true,true)&&!scope.Complete,"Late callback invalidates publication instead of slipping through Finish");
        scope=new PortalCaptureGate(0,0,1,true,true);Check(scope.Finish(0,0,true)&&!scope.Finish(0,0,true)&&!scope.Complete,"Double finish invalidates capture");
        scope=new PortalCaptureGate(0,0,1,true,true);Check(!scope.Finish(1,0,true),"Device epoch mismatch cannot publish");
        scope=new PortalCaptureGate(0,0,1,true,true);Check(!scope.Finish(0,1,true),"Source generation mismatch cannot publish");
        Check(PortalClipSource.Family("Geometry/Materials/TriplanarMulti/Pixel.hlsl")==PortalClipFamily.Voxel,"Recognize old voxel registry asset");
        Check(!PortalCaptureCapability.Available&&PortalCaptureCapability.Reason.Contains("disabled"),"Foundation cannot publish a fake working provider");
        var registry=new PortalClipRegistry();var a=new object();var b=new object();var c=new object();
        Check(registry.Queue(new PortalClipDescriptor(a,"Geometry/Materials/Standard/Pixel.hlsl","test",null))&&registry.Queue(new PortalClipDescriptor(b,"Geometry/Materials/Standard/Pixel.hlsl","test",null))&&registry.Queue(new PortalClipDescriptor(c,"Geometry/Materials/Standard/Pixel.hlsl","test",null)),"Bounded warmup queue accepts known assets");
        int calls=0;Check(registry.Warm(d=>{calls++;return new byte[4];})==2&&calls==2&&registry.Prepared(c)==null,"At most two CPU compilations per update");
        var prepared=registry.Prepared(a);var shader=new object();Check(registry.Attach(prepared,shader),"Private shader attachment");
        Check(registry.AwaitingAttachment().Length==1&&ReferenceEquals(registry.AwaitingAttachment()[0].Descriptor.Original,b),"Native attachment snapshot includes only warmed unattached entries");
        scope=new PortalCaptureGate(registry.Epoch,0,1,true,true);object replacement;
        Check(registry.Select(a,scope,true,out replacement)&&ReferenceEquals(shader,replacement),"Known private shader selected only with threshold binding");
        var retired=registry.NewEpoch();Check(retired.Length==1&&ReferenceEquals(retired[0],shader)&&!registry.Attach(prepared,new object()),"Device epoch retires private resources and rejects stale attachments");
        scope=new PortalCaptureGate(registry.Epoch,0,1,true,true);Check(!registry.Select(a,scope,true,out replacement)&&!scope.Finish(registry.Epoch,0,true),"Stale shader identity cannot publish");
        Check(PortalProjectionShaders.ThresholdConstants(spec).Length*4==160&&PortalProjectionShaders.CompositeConstants(spec,1,1).Length*4==144,"Fixed GPU constant buffers have exact bounded size");
        registry=new PortalClipRegistry();bool acceptedAll=true;for(int i=0;i<PortalClipRegistry.MaxVariants;i++)acceptedAll&=registry.Queue(new PortalClipDescriptor(new object(),"Geometry/Materials/Standard/Pixel.hlsl","test",null));Check(acceptedAll,"Bounded variant capacity accepts its declared maximum");
        Check(!registry.Queue(new PortalClipDescriptor(new object(),"Geometry/Materials/Standard/Pixel.hlsl","test",null)),"Variant cap rejects excess identities");
        registry=new PortalClipRegistry();var ownedKey=new object();var returnedBytes=new byte[]{1,2,3,4};
        registry.Queue(new PortalClipDescriptor(ownedKey,"Geometry/Materials/Standard/Pixel.hlsl","test",null));registry.Warm(d=>returnedBytes);returnedBytes[0]=99;
        Check(registry.Prepared(ownedKey).Bytecode[0]==1,"Compiler scratch bytecode cannot mutate private shader registry");
        registry=new PortalClipRegistry();bool ignored=true;
        for(int i=0;i<PortalClipRegistry.MaxVariants+32;i++)ignored&=!registry.Queue(new PortalClipDescriptor(new object(),"Lighting/LightDir.hlsl","auxiliary",null));
        var neededWorld=new object();Check(ignored&&registry.Queue(new PortalClipDescriptor(neededWorld,"Geometry/Materials/Standard/Pixel.hlsl","needed",null)),"More than 128 auxiliary identities never exhaust demand-warmed geometry slots");
        Check(registry.Issue(neededWorld).Contains("CPU warmup"),"A demanded geometry shader reports bounded CPU warmup distinctly");registry.Warm(d=>new byte[4]);
        Check(registry.Issue(neededWorld).Contains("GPU attachment")&&registry.Status.StartsWith("1/128 variants"),"Warmed geometry reports GPU attachment and quota occupancy distinctly");
    }
    static void ShaderChecks(string bin)
    {
        var assembly=DirectCameraCaptureNative.FindType(null,"VRageRender.MyRender11").Assembly;var compiler=new PortalClipShaders(assembly,new PortalShaderBytecodeCache(Path.Combine(Environment.CurrentDirectory,"artifacts","portal-tests","shader-cache")));
        var resources=new PortalCaptureResources(assembly);Check(resources.Threshold==null,"Actual native R32 texture/binding ABI verified without GPU allocation");
        var thresholdNative=new PortalCaptureThresholdNative(assembly,resources,compiler);
        var shaderNative=new PortalCaptureShaderNative(assembly,resources,compiler);
        Check(resources.Threshold==null&&shaderNative.Epoch==0&&shaderNative.PixelShaderSet!=null,"Native threshold generator and private world shader selector validate ABI/CPU bytecode without GPU allocation");
        shaderNative.Dispose();thresholdNative.Dispose();
        var engineCompiler=DirectCameraCaptureNative.FindType(null,"VRageRender.MyShaderCompiler");engineCompiler.GetField("m_shadersPath",All).SetValue(null,Path.GetFullPath(Path.Combine(bin,"..","Content","Shaders")));
        var harmony=new Harmony("HDR.Portal.HeadlessCompiler");var fileSystem=DirectCameraCaptureNative.FindType(null,"VRage.FileSystem.MyFileSystem");
        harmony.Patch(fileSystem.GetMethod("FileExists",All,null,new[]{typeof(string)},null),prefix:new HarmonyMethod(typeof(Program).GetMethod("Exists",All)));
        try
        {
            Check(compiler.Inspect(compiler.CompileFixed(PortalClipSource.DepthOnly),true).Length==0,"Depth-only clip preserves null PS no-colour ABI");
            compiler.Inspect(compiler.CompileFixed(PortalClipSource.DiscardAll),false);assertions++;
            Check(compiler.Inspect(compiler.CompileFixed(PortalProjectionShaders.Threshold),false).Length==1,"Private R32 threshold shader compiles with one target");
            Check(compiler.Inspect(compiler.CompileFixed(PortalProjectionShaders.Composite),false).Length==1,"Projective-to-angular compositor compiles with one image/target");
            var macroType=DirectCameraCaptureNative.FindType(null,"SharpDX.Direct3D.ShaderMacro");
            AtmosphereChecks.Run(compiler,macroType,Check);
            var variants=new[]{
                new[]{"Transparent/GPUParticles/Render.hlsl","STREAKS","LIT_PARTICLE"},
                new[]{"Transparent/GPUParticles/Render.hlsl","STREAKS","LIT_PARTICLE","OIT","DEBUG_SHADOWS"},
                new[]{"Transparent/Billboards.hlsl","OIT"},new[]{"Transparent/Billboards.hlsl","LDR"},
                new[]{"Foliage/Foliage.hlsl"},
                new[]{"Geometry/Materials/Standard/Pixel.hlsl","RENDERING_PASS_GBUFFER"},
                new[]{"Geometry/Materials/Standard/Pixel.hlsl","RENDERING_PASS_DEPTH"},
                new[]{"Geometry/Materials/AlphaMasked/Pixel.hlsl","RENDERING_PASS_GBUFFER","ALPHA_MASKED"},
                new[]{"Geometry/Materials/AlphaMaskedArray/Pixel.hlsl","RENDERING_PASS_GBUFFER","ALPHA_MASKED"},
                new[]{"Geometry/Materials/AlphaMaskedArray/Pixel.hlsl","RENDERING_PASS_GBUFFER","ALPHA_MASKED","ALPHA_MASK_ARRAY","ALTER_DEPTH"},
                new[]{"Geometry/Materials/AlphaMaskedArray/Pixel.hlsl","RENDERING_PASS_GBUFFER","ALPHA_MASKED","STATIC_DECAL"},
                new[]{"Geometry/Materials/TriplanarSingle/Pixel.hlsl","RENDERING_PASS_GBUFFER"},
                new[]{"Geometry/Materials/TriplanarMulti/Pixel.hlsl","RENDERING_PASS_GBUFFER"},
                new[]{"Geometry/Materials/TriplanarDebris/Pixel.hlsl","RENDERING_PASS_GBUFFER"},
                new[]{"Geometry/Materials/Glass/Pixel.hlsl","RENDERING_PASS_TRANSPARENT"},
                new[]{"Geometry/Materials/Holo/Pixel.hlsl","RENDERING_PASS_TRANSPARENT"},
                new[]{"Geometry/Materials/Shield/Pixel.hlsl","RENDERING_PASS_TRANSPARENT"},
                new[]{"Geometry/Materials/ShieldLit/Pixel.hlsl","RENDERING_PASS_TRANSPARENT"}
            };
            foreach(string[] names in variants)
            {
                var macros=Array.CreateInstance(macroType,names.Length-1);
                for(int i=1;i<names.Length;i++)
                {
                    object value;
                    if(names[i].StartsWith("RENDERING_PASS_"))
                    {
                        string pass=names[i]=="RENDERING_PASS_DEPTH"?"Depth":names[i]=="RENDERING_PASS_TRANSPARENT"?"Transparent":"GBuffer";
                        value=DirectCameraCaptureNative.FindType(null,"VRageRender.MyMaterialShaders").GetMethod("GetRenderingPassMacro",All).Invoke(null,new object[]{pass});
                    }
                    else value=Activator.CreateInstance(macroType,new object[]{names[i],null});
                    macros.SetValue(value,i-1);
                }
                byte[] bytes=compiler.Compile(new PortalClipDescriptor(null,names[0],"headless",macros));
                Check(bytes.Length>100&&System.Text.Encoding.ASCII.GetString(bytes,0,4)=="DXBC","Installed private PS variant "+string.Join(",",names));
                Console.WriteLine("Compiled installed portal variant "+string.Join(",",names)+": "+bytes.Length+" bytes, t31-free proof, native outputs retained.");
            }
            ShaderCacheChecks.Installed(compiler,Check);
            PortalBillboardProjectiveChecks.Run(compiler,Check);
            bool rejected=false;try{compiler.Inspect(compiler.CompileFixed("Texture2D<float> Collision:register(t31); float4 __pixel_shader(float4 p:SV_Position):SV_Target{return Collision.Load(int3(p.xy,0));}"),false);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Native t31 collision rejected");
            rejected=false;try{compiler.Inspect(compiler.CompileFixed("[earlydepthstencil] float4 __pixel_shader(float4 p:SV_Position):SV_Target{return 1;}"),false);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Force earlydepth bytecode rejected");
            rejected=false;try{compiler.Inspect(compiler.CompileFixed("RWTexture2D<float4> Output:register(u1); float4 __pixel_shader(float4 p:SV_Position):SV_Target{Output[(int2)p.xy]=1;return 1;}"),false);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Writable UAV side effects rejected");
        }
        finally{harmony.UnpatchAll(harmony.Id);}
    }
    static void IntegrationChecks(string bin)
    {
        Check(PortalCaptureCoverage.CertifiedRuntime(PlatformID.Win32NT,8,10)&&!PortalCaptureCoverage.CertifiedRuntime(PlatformID.Win32NT,8,4)&&!PortalCaptureCoverage.CertifiedRuntime(PlatformID.Win32NT,4,10)&&!PortalCaptureCoverage.CertifiedRuntime(PlatformID.Unix,8,10),"Caller/JIT certificate rejects untested Legacy/x86/non-Windows runtimes without changing ordinary renderer availability");
        var assembly=DirectCameraCaptureNative.FindType(null,"VRageRender.MyRender11").Assembly;
        Check(PortalCaptureCoverage.ValidateDrawBoundary(assembly).Length==6,"Installed IL has exactly six audited immediate/deferred native draw boundaries");
        string root=Path.GetFullPath(Path.Combine(bin,"..","Content","Shaders"));
        Check(PortalCaptureCoverage.ShaderFingerprint(root)==PortalCaptureCoverage.AuditedShaderFingerprint,"Every installed HLSL/include asset matches audited auxiliary semantics");
        Check(PortalCaptureDrawPolicy.Role("Lighting/LightDir.hlsl")==PortalDrawRole.Auxiliary&&PortalCaptureDrawPolicy.Role("Decals/Decals.hlsl")==PortalDrawRole.Auxiliary,"Lighting/decal reconstruction uses clipped native depth");
        Check(PortalCaptureDrawPolicy.Role("Transparent/Clouds/Clouds.hlsl")==PortalDrawRole.Geometry&&PortalCaptureDrawPolicy.Role("Transparent/Atmosphere/AtmosphereGBuffer.hlsl")==PortalDrawRole.Geometry&&PortalCaptureDrawPolicy.Role("Transparent/Atmosphere/AtmosphereEnv.hlsl")==PortalDrawRole.Suppressed,"Certified atmosphere interval and cloud mesh warm private variants while probe projection stays suppressed");
        Check(PortalCaptureDrawPolicy.Role("Postprocess/NewWorldGenerator.hlsl")==PortalDrawRole.Unknown&&PortalCaptureDrawPolicy.Role("../Lighting/LightDir.hlsl")==PortalDrawRole.Unknown,"Filename prefix/traversal cannot bypass world draw classification");
        var integration=new PortalCaptureIntegrationNative(assembly,7);
        Check(integration.Epoch==7&&!integration.Ready,"Native device epoch seed survives CPU-only construction");
        var contextType=DirectCameraCaptureNative.FindType(null,"VRage.Render11.RenderContext.MyRenderContext");
        var stageType=DirectCameraCaptureNative.FindType(null,"VRage.Render11.RenderContext.MyPixelStage");
        object context=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(contextType),stage=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(stageType);
        object native=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(DirectCameraCaptureNative.FindType(null,"SharpDX.Direct3D11.DeviceContext1"));
        object fakeShader=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(DirectCameraCaptureNative.FindType(null,"SharpDX.Direct3D11.PixelShader"));
        contextType.GetField("m_pixelShaderStage",All).SetValue(context,stage);contextType.GetField("m_deviceContext",All).SetValue(context,native);stageType.GetField("m_pixelShader",All).SetValue(stage,fakeShader);
        using(var prewarmed=new PrewarmedCallerChecks(context,native,fakeShader,Check))
        {
            Check(integration.TryInstall()&&integration.Ready&&PortalCaptureCapability.Available,"Actual installed engine/native/enclosing Harmony hooks certify geometry-only availability without GPU allocation: "+integration.Failure);
            prewarmed.Verify(integration,Check);
        }
        var adapterType=typeof(PortalCaptureIntegrationNative);var sessionField=adapterType.GetField("session",All);
        var before=adapterType.GetMethod("BeforeRenderDraw",All);var after=adapterType.GetMethod("AfterRenderDraw",All);var guard=adapterType.GetMethod("BeforeNativeDraw",All);
        var audited=adapterType.GetMethod("BeforeAuditedNativeDraw",All);var auditedAfter=adapterType.GetMethod("AfterAuditedNativeDraw",All);
        Check((bool)guard.Invoke(null,new[]{native}),"All native main-view draws remain untouched outside capture scope");
        PortalCaptureIntegrationNative.Session NewSession(PortalDrawRole role)
        {DirectCameraCapture.IsCapturing=true;var s=new PortalCaptureIntegrationNative.Session(new PortalCaptureGate(7,8,1,true,true),64,64,new Dictionary<object,PortalDrawRole>{{fakeShader,role}});sessionField.SetValue(integration,s);return s;}
        try
        {
            var s=NewSession(PortalDrawRole.Auxiliary);var args=new object[]{context,null};
            Check((bool)before.Invoke(null,args)&&(bool)audited.Invoke(null,new[]{native})&&(bool)guard.Invoke(null,new[]{native}),"Known auxiliary draw traverses the guarded native context without setting GPU shaders");auditedAfter.Invoke(null,null);after.Invoke(null,new[]{null,args[1]});
            Check(s.ActiveDraws==0&&integration.EndAfterJoin(s,true)&&ReferenceEquals(stageType.GetField("m_pixelShader",All).GetValue(stage),fakeShader),"Per-draw finalizer restores lifetime and preserves native cached shader");
            s=NewSession(PortalDrawRole.Unknown);args=new object[]{context,null};Check(!(bool)before.Invoke(null,args),"Unknown shader draw is suppressed before native execution");after.Invoke(null,new[]{null,args[1]});Check(!integration.EndAfterJoin(s,true),"Unknown geometry cannot publish even if every worker joined");
            s=NewSession(PortalDrawRole.Suppressed);args=new object[]{context,null};Check(!(bool)before.Invoke(null,args),"Known unsupported volume draw is intentionally omitted");after.Invoke(null,new[]{null,args[1]});Check(integration.EndAfterJoin(s,true)&&s.SuppressedDraws==1,"Geometry-only omission is explicit and bounded");
            s=NewSession(PortalDrawRole.Auxiliary);Check(!(bool)guard.Invoke(null,new[]{native})&&!integration.EndAfterJoin(s,true),"Direct SharpDX draw bypass is suppressed and rejects capture");
            s=NewSession(PortalDrawRole.Auxiliary);args=new object[]{context,null};before.Invoke(null,args);audited.Invoke(null,new[]{native});Check((bool)guard.Invoke(null,new[]{native})&&!(bool)guard.Invoke(null,new[]{native}),"Exactly one native draw is authorized per context boundary");auditedAfter.Invoke(null,null);after.Invoke(null,new[]{null,args[1]});Check(!integration.EndAfterJoin(s,true),"Duplicate native execution cannot publish");
            var contextStateField=contextType.GetField("m_state",All);object fakeState=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(contextStateField.FieldType);contextStateField.SetValue(context,fakeState);
            var targetsField=contextStateField.FieldType.GetField("m_rtvs",All);var targetCountField=contextStateField.FieldType.GetField("m_rtvsCount",All);var targets=Array.CreateInstance(targetsField.FieldType.GetElementType(),1);
            object mainView=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(targetsField.FieldType.GetElementType());targets.SetValue(mainView,0);targetsField.SetValue(fakeState,targets);targetCountField.SetValue(fakeState,1);
            s=new PortalCaptureIntegrationNative.Session(new PortalCaptureGate(7,8,1,true,true),64,64,new Dictionary<object,PortalDrawRole>{{fakeShader,PortalDrawRole.Auxiliary}},mainView);sessionField.SetValue(integration,s);args=new object[]{context,null};before.Invoke(null,args);
            Check(!(bool)audited.Invoke(null,new[]{native}),"Even a certified auxiliary shader cannot draw into the main backbuffer during capture");after.Invoke(null,new[]{null,args[1]});Check(!integration.EndAfterJoin(s,true),"Protected main-target violation rejects the captured frame");
            s=NewSession(PortalDrawRole.Geometry);args=new object[]{context,null};Check(!(bool)before.Invoke(null,args),"Geometry viewport differing from cutoff domain is suppressed before shader/GPU access");after.Invoke(null,new[]{null,args[1]});Check(!integration.EndAfterJoin(s,true),"Viewport mismatch cannot publish");
            s=NewSession(PortalDrawRole.Auxiliary);Check(!integration.EndAfterJoin(s,false)&&ReferenceEquals(sessionField.GetValue(integration),s),"Unjoined capture retains isolation guard and cannot restore/publish");Check(integration.EndAfterJoin(s,true)==false,"Later worker join closes the failed capture safely");
            s=NewSession(PortalDrawRole.Auxiliary);DirectCameraCapture.IsCapturing=false;args=new object[]{context,null};Check((bool)before.Invoke(null,args)&&args[1]==null&&(bool)guard.Invoke(null,new[]{native}),"Unexpected ended capture never suppresses or substitutes main-view draws");Check(!integration.EndAfterJoin(s,true),"Orphaned guard closes failed after proven worker join");
            DrawExecutionChecks.Run(integration,context,native,fakeShader,Check);
            integration.ResetRenderResources();Check(integration.Epoch==8,"Native resource reset advances device epoch before admitting a new generation");
        }
        finally{DirectCameraCapture.IsCapturing=false;sessionField.SetValue(integration,null);integration.Dispose();}
        Check(!PortalCaptureCapability.Available&&(bool)guard.Invoke(null,new[]{native}),"Owner disposal removes hooks and restores inert main-view state");
    }
}
