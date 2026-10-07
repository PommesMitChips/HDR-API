using HDRClientRenderer;
using System.Reflection;
using System.Text.RegularExpressions;
internal static class PortalBillboardProjectiveChecks
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static void Rejected(Action action,Action<bool,string> check,string message)
    {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}check(rejected,message);}
    internal static void Run(PortalClipShaders compiler,Action<bool,string> check)
    {
        Type macro=DirectCameraCaptureNative.FindType(null,"SharpDX.Direct3D.ShaderMacro");
        var certificate=PortalBillboardProjectiveNative.CreateBindingCertificate(DirectCameraCaptureNative.FindType(null,"VRageRender.MyRender11").Assembly);
        check(certificate!=null&&certificate.Verified,"Installed native Render/AllStages t30 binding proof provides an authenticated certificate");
        string[] defines={"LIT_PARTICLE","ALPHA_CUTOUT","SOFT_PARTICLE","DEBUG_UNIFORM_ACCUM","OIT","SINGLE_CHANNEL","LDR"};
        PortalClipShaders.PreparedSource baseline=null,debugBaseline=null;byte[] original=null,projective=null;
        for(int flags=0;flags<1<<defines.Length;flags++)
        {
            var names=defines.Where((name,index)=>(flags&(1<<index))!=0).ToArray();var macros=Array.CreateInstance(macro,names.Length);
            for(int index=0;index<names.Length;index++)macros.SetValue(Activator.CreateInstance(macro,new object[]{names[index],null}),index);
            var prepared=compiler.Prepare(new PortalClipDescriptor(null,"Transparent/Billboards.hlsl","headless-projective",macros));
            byte[] result;try{result=PortalBillboardProjectiveSource.CompileAndValidate(compiler,prepared,certificate);}catch(Exception ex){throw new InvalidOperationException("Native projective variant failed: "+string.Join(",",names),ex);}
            check(result.Length>100,"Native billboard projective output/resources/b7 row-major80 ABI retained: "+string.Join(",",names));
            if(flags==0){baseline=prepared;original=compiler.CompileFixed(prepared.Source);projective=result;}
            if(flags==8)debugBaseline=prepared;
        }
        check(PortalBillboardProjectiveSource.ConstantSlot==7&&PortalBillboardProjectiveSource.ConstantBytes==80,"Projective billboard uses the agreed b7/80-byte matrix/settings contract");
        string injected=PortalBillboardProjectiveSource.Inject(baseline);
        string compact=Regex.Replace(injected,@"\s+",string.Empty);
        check(compact.Contains("resultColor*=textureSample*billboardColor;")&&compact.Contains("float4color=CalculateColor(vertex,linearDepth,false,alphaCutout);")&&
            compact.Contains("resultColor=float4(reflectionColor,max(color.w,reflective));"),"Portal colour settings affect only the atlas sample; native material colour/alpha/reflection remain intact");
        check(injected.Contains("textureValue.rgb=max(0,lerp(")&&injected.Contains("return textureValue;")&&!injected.Contains("textureValue.a="),"Projective sample colour adjustment retains sampled alpha");
        check(injected.Contains("custom_projection_id>=0")&&injected.Contains("projected.w<=1e-6")&&injected.Contains("any(uv<0)")&&injected.Contains("any(uv>1)")&&injected.Contains("clamp(uv,halfTexel,1-halfTexel)"),"Private projective variant rejects custom/invalid projection and clamps single-mip sampling to half-texel bounds");
        Rejected(()=>PortalBillboardProjectiveSource.Inject(new PortalClipShaders.PreparedSource(baseline.Source,PortalClipFamily.Billboard,"Transparent/GPUParticles/Render.hlsl","test")),check,"Projective injector rejects another installed asset despite similar shader shape");
        Rejected(()=>PortalBillboardProjectiveSource.Inject(new PortalClipShaders.PreparedSource(baseline.Source.Replace("textureSample","renamedSample"),PortalClipFamily.Billboard,baseline.Asset,"test")),check,"Changed native texture-sample anchor fails closed");
        Rejected(()=>PortalBillboardProjectiveSource.Inject(new PortalClipShaders.PreparedSource(baseline.Source.Replace("wposition","changedPosition"),PortalClipFamily.Billboard,baseline.Asset,"test")),check,"Changed camera-relative pixel-position field fails closed");
        Rejected(()=>PortalBillboardProjectiveSource.Inject(new PortalClipShaders.PreparedSource(injected,PortalClipFamily.Billboard,baseline.Asset,"test")),check,"An already-private shader cannot be injected twice");
        byte[] collision=compiler.CompileFixed("cbuffer NativeOccupied:register(b7){float4 NativeValue;}; float4 __pixel_shader(float4 p:SV_Position):SV_Target0{return NativeValue;}");
        Rejected(()=>PortalBillboardProjectiveSource.RequireFreeSlot(collision),check,"Original reflected pixel b7 collision fails before creating a private replacement");
        byte[] wrongOutput=compiler.CompileFixed(injected.Replace("SV_TARGET0","SV_TARGET3"));
        Rejected(()=>PortalBillboardProjectiveSource.Validate(compiler,original,wrongOutput),check,"Changed native billboard output slot fails reflection validation");
        byte[] wrongResource=compiler.CompileFixed("SamplerState BogusPortalSampler:register(s15);\n"+injected.Replace("TextureAtlas.Sample(LinearSampler, HDRPortalProjectiveUv(input))","TextureAtlas.Sample(BogusPortalSampler, HDRPortalProjectiveUv(input))"));
        Rejected(()=>PortalBillboardProjectiveSource.Validate(compiler,original,wrongResource),check,"Native resource changes beyond the reserved b7 fail validation");
        byte[] wrongLayout=compiler.CompileFixed(injected.Replace("row_major float4x4 HDRPortalPrimaryRelativeToCaptureClip","column_major float4x4 HDRPortalPrimaryRelativeToCaptureClip"));
        Rejected(()=>PortalBillboardProjectiveSource.Validate(compiler,original,wrongLayout),check,"Column-major upload mismatch fails the reflected constant-layout proof");
        byte[] debugOriginal=compiler.CompileFixed(debugBaseline.Source);string debugInjected=PortalBillboardProjectiveSource.Inject(debugBaseline);byte[] debugPrivate=compiler.CompileFixed(debugInjected);
        Rejected(()=>PortalBillboardProjectiveSource.Validate(compiler,debugOriginal,debugPrivate),check,"DEBUG binding reactivation requires an explicit source-derived native binding exception");
        Rejected(()=>PortalBillboardProjectiveSource.Validate(compiler,debugOriginal,debugPrivate,certificate),check,"Native binding certificate alone cannot approve a source-unbound resource exception");
        PortalBillboardProjectiveSource.Validate(compiler,debugOriginal,debugPrivate,certificate,debugBaseline);check(true,"DEBUG reactivates only the existing native-bound BillboardBuffer t30/count1/stride48");
        byte[] wrongStride=compiler.CompileFixed(Regex.Replace(debugInjected,@"\bstruct\s+BillboardData\s*\{",match=>match.Value+" float4 HostilePadding;"));
        Rejected(()=>PortalBillboardProjectiveSource.Validate(compiler,debugOriginal,wrongStride,certificate,debugBaseline),check,"DEBUG native buffer reactivation rejects a changed structured stride");
        byte[] wrongBinding=compiler.CompileFixed(Regex.Replace(debugInjected,@"\bBillboardBuffer\s*:\s*register\s*\(\s*t30\s*\)","BillboardBuffer : register(t29)"));
        Rejected(()=>PortalBillboardProjectiveSource.Validate(compiler,debugOriginal,wrongBinding,certificate,debugBaseline),check,"DEBUG native buffer reactivation rejects another register");
        byte[] wrongName=compiler.CompileFixed(debugInjected.Replace("BillboardBuffer","HostileBillboardBuffer"));
        Rejected(()=>PortalBillboardProjectiveSource.Validate(compiler,debugOriginal,wrongName,certificate,debugBaseline),check,"DEBUG native buffer reactivation rejects another resource name");
        PortalBillboardProjectiveSource.Validate(compiler,original,projective);check(true,"Baseline private projective billboard remains accepted after hostile cases");
    }
}
