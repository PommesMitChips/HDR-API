using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
namespace HDRClientRenderer
{
    // Only the trusted private HDRportal billboard batch selects this variant.
    // Native material multiplication, reflection, depth and OIT stay untouched.
    internal static class PortalBillboardProjectiveSource
    {
        internal const int ConstantSlot=7,ConstantBytes=80;
        internal const string ConstantName="HDRPortalBillboardProjection";
        internal const string MatrixName="HDRPortalPrimaryRelativeToCaptureClip",ParametersName="HDRPortalImageParameters";
        // DEBUG_UNIFORM_ACCUM removes this declared native binding from its
        // original PS. The projection guard reactivates it. The native owner
        // separately certifies Render's unconditional t30 binding and exact SRV.
        internal const string DebugUniformReactivatedResource="BillboardBuffer/Structured/30/1/Userpacked/Mixed/Buffer/48";
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        const string Constants=@"
cbuffer HDRPortalBillboardProjection : register(b7) {
 row_major float4x4 HDRPortalPrimaryRelativeToCaptureClip;
 float4 HDRPortalImageParameters;
};
";
        const string Helpers=@"
float2 HDRPortalProjectiveUv(VsOut input) {
 float4 projected=mul(float4(input.wposition,1),HDRPortalPrimaryRelativeToCaptureClip);
 if(!all(isfinite(projected)) || projected.w<=1e-6) clip(-1);
 if(!all(isfinite(HDRPortalImageParameters)) || any(HDRPortalImageParameters.zw<1) ||
    HDRPortalImageParameters.x<0 || HDRPortalImageParameters.x>2 ||
    HDRPortalImageParameters.y<0 || HDRPortalImageParameters.y>4) clip(-1);
 float2 uv=float2(.5+.5*projected.x/projected.w,.5-.5*projected.y/projected.w);
 if(!all(isfinite(uv)) || any(uv<0) || any(uv>1)) clip(-1);
 float2 halfTexel=.5/HDRPortalImageParameters.zw;
 return clamp(uv,halfTexel,1-halfTexel);
}
float4 HDRPortalAdjustSample(float4 textureValue) {
 float luma=dot(textureValue.rgb,float3(.2126,.7152,.0722));
 textureValue.rgb=max(0,lerp(luma.xxx,textureValue.rgb,HDRPortalImageParameters.x))*HDRPortalImageParameters.y;
 return textureValue;
}
";
        internal static string Inject(PortalClipShaders.PreparedSource prepared)
        {
            if(prepared==null||prepared.Family!=PortalClipFamily.Billboard||prepared.Asset!="Transparent/Billboards.hlsl"||prepared.Source==null||prepared.Source.Length>4*1024*1024)
                throw new InvalidOperationException("Projective sampling requires the sealed installed billboard shader.");
            string source=Regex.Replace(prepared.Source,@"^\s*#\s*line[^\r\n]*",string.Empty,RegexOptions.Multiline);
            if(source.Contains("HDRPortal")||Regex.IsMatch(source,@"register\s*\(\s*b7\s*\)",RegexOptions.IgnoreCase)||
                !Regex.IsMatch(source,@"\bfloat3\s+wposition\s*:\s*TEXCOORD2\s*;",RegexOptions.IgnoreCase))
                throw new InvalidOperationException("Installed billboard projective source ABI changed.");
            var sample=Regex.Matches(source,@"\bfloat4\s+textureSample\s*=\s*TextureAtlas\s*\.\s*Sample\s*\(\s*LinearSampler\s*,\s*input\s*\.\s*texcoord\s*\.\s*xy\s*\)\s*(?<channel>\.\s*xxxx)?\s*;");
            var calculate=Regex.Matches(source,@"\bfloat4\s+CalculateColor\s*\(\s*VsOut\s+input\s*,[^)]*\)\s*\{");
            if(sample.Count!=1||calculate.Count!=1||sample[0].Index<calculate[0].Index)
                throw new InvalidOperationException("Installed billboard texture-sample anchor changed.");
            source=source.Remove(sample[0].Index,sample[0].Length).Insert(sample[0].Index,
                "float4 textureSample = HDRPortalAdjustSample(TextureAtlas.Sample(LinearSampler, HDRPortalProjectiveUv(input))"+sample[0].Groups["channel"].Value+");");
            var entry=Regex.Matches(source,@"\bvoid\s+__pixel_shader\s*\(\s*VsOut\s+vertex\s*,[^)]*\)\s*\{");
            if(entry.Count!=1)throw new InvalidOperationException("Installed billboard pixel-entry ABI changed.");
            source=source.Insert(entry[0].Index+entry[0].Length,"\nif(BillboardBuffer[vertex.index].custom_projection_id>=0) clip(-1);\nHDRPortalProjectiveUv(vertex);\n");
            calculate=Regex.Matches(source,@"\bfloat4\s+CalculateColor\s*\(\s*VsOut\s+input\s*,[^)]*\)\s*\{");
            return Constants+source.Insert(calculate[0].Index,Helpers);
        }
        // All preprocessing has already finished on the renderer. These methods
        // use immutable source and CPU compiler/reflection only, including b7 proof.
        internal static byte[] CompileAndValidate(PortalClipShaders compiler,PortalClipShaders.PreparedSource prepared,PortalBillboardProjectiveNative.NativeBindingCertificate certificate=null)
        {
            if(compiler==null)throw new ArgumentNullException("compiler");string injected=Inject(prepared);
            byte[] original=compiler.CompileFixed(prepared.Source);RequireFreeSlot(original);
            byte[] projective=compiler.CompileFixed(injected);
            Validate(compiler,original,projective,certificate,prepared);return (byte[])projective.Clone();
        }
        internal static void Validate(PortalClipShaders compiler,byte[] original,byte[] projective,PortalBillboardProjectiveNative.NativeBindingCertificate certificate=null,PortalClipShaders.PreparedSource prepared=null)
        {
            if(!compiler.Inspect(original,false).SequenceEqual(compiler.Inspect(projective,false)))throw new InvalidOperationException("Projective billboard native output ABI changed.");
            string[] expected=Resources(original,false),actual=Resources(projective,true);
            bool debugUniformNativeBinding=certificate!=null&&certificate.Verified&&prepared!=null&&prepared.Asset=="Transparent/Billboards.hlsl"&&prepared.Family==PortalClipFamily.Billboard&&
                Regex.IsMatch(prepared.Source,@"\bDebugUniformAccum\s*\(\s*resultColor\s*,");
            if(debugUniformNativeBinding&&!expected.Contains(DebugUniformReactivatedResource)&&actual.Contains(DebugUniformReactivatedResource))
                actual=actual.Where(resource=>resource!=DebugUniformReactivatedResource).ToArray();
            if(!expected.SequenceEqual(actual))throw new InvalidOperationException("Projective billboard native resource ABI changed. Removed: "+string.Join(", ",expected.Except(actual))+"; added: "+string.Join(", ",actual.Except(expected)));
        }
        internal static void RequireFreeSlot(byte[] original){Resources(original,false);}
        static string[] Resources(byte[] bytes,bool projective)
        {
            Type type=DirectCameraCaptureNative.FindType(null,"SharpDX.D3DCompiler.ShaderReflection");object reflection=type.GetConstructor(new[]{typeof(byte[])}).Invoke(new object[]{bytes});
            try
            {
                object description=Property(reflection,"Description");var result=new List<string>();bool found=false;
                for(int index=0;index<(int)Field(description,"BoundResources");index++)
                {
                    object binding=Method(reflection,"GetResourceBindingDescription",typeof(int)).Invoke(reflection,new object[]{index});
                    int slot=(int)Field(binding,"BindPoint"),count=(int)Field(binding,"BindCount");string kind=Field(binding,"Type").ToString(),name=(string)Field(binding,"Name");
                    if(kind=="ConstantBuffer"&&slot<=ConstantSlot&&slot+count>ConstantSlot)
                    {
                        if(!projective||slot!=ConstantSlot||count!=1||name!=ConstantName||found)throw new InvalidOperationException("Native billboard pixel b7 is occupied.");
                        ValidateConstants(reflection);found=true;continue;
                    }
                    result.Add(string.Join("/",new[]{"Name","Type","BindPoint","BindCount","Flags","ReturnType","Dimension","NumSamples"}.Select(n=>Field(binding,n).ToString())));
                }
                if(projective&&!found)throw new InvalidOperationException("Projective billboard b7 was optimized away.");
                return result.OrderBy(s=>s,StringComparer.Ordinal).ToArray();
            }
            finally{if(reflection is IDisposable)((IDisposable)reflection).Dispose();}
        }
        static void ValidateConstants(object reflection)
        {
            object buffer=Method(reflection,"GetConstantBuffer",typeof(string)).Invoke(reflection,new object[]{ConstantName});object description=Property(buffer,"Description");
            if((int)Field(description,"Size")!=ConstantBytes||(int)Field(description,"VariableCount")!=2)throw new InvalidOperationException("Projective billboard constant-buffer size changed.");
            Variable(buffer,MatrixName,0,64,"MatrixRows",4,4);Variable(buffer,ParametersName,64,16,"Vector",1,4);
        }
        static void Variable(object buffer,string name,int offset,int size,string kind,int rows,int columns)
        {
            object variable=Method(buffer,"GetVariable",typeof(string)).Invoke(buffer,new object[]{name}),description=Property(variable,"Description");
            object variableType=Method(variable,"GetVariableType").Invoke(variable,null),shape=Property(variableType,"Description");
            if((int)Field(description,"StartOffset")!=offset||(int)Field(description,"Size")!=size||Field(shape,"Class").ToString()!=kind||Field(shape,"Type").ToString()!="Float"||
                (int)Field(shape,"RowCount")!=rows||(int)Field(shape,"ColumnCount")!=columns||(int)Field(shape,"ElementCount")!=0)
                throw new InvalidOperationException("Projective billboard constant layout changed: "+name);
        }
        static object Property(object value,string name){return value.GetType().GetProperty(name,Instance).GetValue(value);}
        static object Field(object value,string name){return value.GetType().GetField(name,Instance).GetValue(value);}
        static MethodInfo Method(object value,string name,params Type[] arguments){return value.GetType().GetMethod(name,Instance,null,arguments,null);}
    }
}
