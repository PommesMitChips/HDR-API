using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
namespace HDRClientRenderer
{
    internal sealed class PortalClipDescriptor
    {
        internal readonly object Original;internal readonly string File,Pass;internal readonly Array Macros;internal readonly PortalClipFamily Family;
        internal PortalClipDescriptor(object original,string file,string pass,Array macros)
        {Original=original;File=file.Replace('\\','/');Pass=pass;Macros=macros==null?null:(Array)macros.Clone();Family=PortalClipSource.Family(File);}
    }
    // CPU-only trusted compilation and reflection; no device or shared engine PS
    // is changed. New native PS objects are created separately by the capture owner.
    internal sealed class PortalClipShaders
    {
        internal sealed class PreparedSource
        {internal readonly string Source,Asset,Pass;internal readonly PortalClipFamily Family;internal PreparedSource(string source,PortalClipFamily family,string asset,string pass){Source=source;Family=family;Asset=asset;Pass=pass;}}
        const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly Func<string,Type> type;readonly Type macro;
        readonly PropertyInfo root,globals;readonly MethodInfo fill,preprocess,compile;
        readonly PortalShaderBytecodeCache cache;readonly Lazy<string> fingerprint;
        internal PortalClipShaders(Assembly assembly,PortalShaderBytecodeCache bytecodeCache=null)
        {
            type=name=>DirectCameraCaptureNative.FindType(assembly,name);
            Type compiler=type("VRageRender.MyShaderCompiler");macro=type("SharpDX.Direct3D.ShaderMacro");
            root=P(compiler,"ShadersPath",Static);globals=P(compiler,"GlobalShaderMacros",Static);
            fill=M(compiler,"FillGlobalMacros",Static,typeof(List<>).MakeGenericType(macro),typeof(bool));
            preprocess=M(compiler,"PreprocessShader",Static,typeof(string),macro.MakeArrayType(),typeof(string).MakeByRefType());
            compile=type("SharpDX.D3DCompiler.ShaderBytecode").GetMethods(Static).Single(m=>m.Name=="Compile"&&m.GetParameters().Length==8&&m.GetParameters().Take(3).All(p=>p.ParameterType==typeof(string)));
            cache=bytecodeCache??new PortalShaderBytecodeCache(PortalShaderBytecodeCache.DefaultDirectory);
            fingerprint=new Lazy<string>(()=>CompilerFingerprint(new[]{compiler.Assembly,macro.Assembly,compile.DeclaringType.Assembly,type("SharpDX.D3DCompiler.ShaderReflection").Assembly}));
            // Validate reflection ABI now, without constructing any GPU object.
            P(type("SharpDX.D3DCompiler.ShaderReflection"),"RequiresFlags",Instance);
            M(type("SharpDX.D3DCompiler.ShaderReflection"),"GetOutputParameterDescription",Instance,typeof(int));
            // Both registries are distinct; verify metadata paths even in a
            // headless process where no native shader has been created yet.
            Type pixelShaders=type("VRageRender.MyPixelShaders");F(pixelShaders,"m_shaders",Static);M(pixelShaders,"GetInfoId",Static,typeof(int));
            Type material=type("VRageRender.MyMaterialShaders");F(material,"Bundles",Static);F(material,"BundleInfo",Static);
            F(type("VRageRender.MyMaterialShadersInfo"),"Material",Instance);F(type("VRageRender.MyMaterialShadersInfo"),"Pass",Instance);
            F(type("VRageRender.MyMaterialShaderInfo"),"PixelShaderFilepath",Instance);
        }
        internal byte[] Compile(PortalClipDescriptor descriptor)
        {return CompilePrepared(Prepare(descriptor));}
        // Engine macros/preprocessor are read only on the serialized renderer.
        // Worker jobs receive an immutable expanded source and no engine state.
        internal PreparedSource Prepare(PortalClipDescriptor descriptor)
        {
            if(descriptor==null||descriptor.Family==PortalClipFamily.Unknown)throw new InvalidOperationException("Unrecognized portal shader asset.");
            Array macros=EffectiveMacros(descriptor.Macros);ValidateSamples(macros);
            string shaderRoot=Path.GetFullPath((string)root.GetValue(null));
            string path=Path.GetFullPath(Path.Combine(shaderRoot,descriptor.File.Replace('/',Path.DirectorySeparatorChar)));
            if(!path.StartsWith(shaderRoot.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!System.IO.File.Exists(path))
                throw new InvalidOperationException("Portal shader asset is outside the installed renderer directory.");
            var args=new object[]{path,macros,null};string source=(string)preprocess.Invoke(null,args);
            if(source==null)throw new InvalidOperationException("Portal installed shader preprocessing failed: "+args[2]);
            if(source.Length>4*1024*1024)throw new InvalidOperationException("Portal expanded shader exceeds the private source quota.");
            return new PreparedSource(source,descriptor.Family,descriptor.File,descriptor.Pass);
        }
        internal byte[] CompilePrepared(PreparedSource prepared)
        {
            if(prepared==null)throw new ArgumentNullException("prepared");string source=prepared.Source,clipped=PortalClipSource.Inject(source,prepared.Family);
            Func<PortalShaderBytecodeCache.Pair> build=()=>new PortalShaderBytecodeCache.Pair(CompileFixed(source),CompileFixed(clipped));
            string identity=fingerprint.Value;
            if(identity==null){var uncached=build();ValidatePair(uncached);return (byte[])uncached.Clipped.Clone();}
            string key=PortalShaderBytecodeCache.Key("portal-clip-cache-v1","__pixel_shader/ps_5_0/32768/0",identity,PortalShaderBytecodeCache.AuditedShaderFingerprint,
                prepared.Asset,prepared.Pass,prepared.Family.ToString(),source,clipped);
            return cache.GetOrCompile(key,build,ValidatePair).Clipped;
        }
        void ValidatePair(PortalShaderBytecodeCache.Pair pair)
        {if(!Inspect(pair.Original,false).SequenceEqual(Inspect(pair.Clipped,true)))throw new InvalidOperationException("Portal shader output ABI changed.");}
        static string CompilerFingerprint(Assembly[] assemblies)
        {
            try
            {
                if(Environment.OSVersion.Platform!=PlatformID.Win32NT||IntPtr.Size!=8||Environment.Version.Major!=10)return null;
                var parts=new List<string>{"Windows/x64/CLR",Environment.Version.ToString()};
                foreach(var assembly in assemblies.Distinct().OrderBy(a=>a.FullName,StringComparer.Ordinal))
                {parts.Add(assembly.FullName);parts.Add(assembly.ManifestModule.ModuleVersionId.ToString());parts.Add(PortalShaderBytecodeCache.Hash(File.ReadAllBytes(assembly.Location)));}
                using(var process=Process.GetCurrentProcess())
                {
                    var native=process.Modules.Cast<ProcessModule>().Where(m=>string.Equals(m.ModuleName,"d3dcompiler_47.dll",StringComparison.OrdinalIgnoreCase)).ToArray();
                    if(native.Length!=1)return null;parts.Add(PortalShaderBytecodeCache.Hash(File.ReadAllBytes(native[0].FileName)));
                }
                return PortalShaderBytecodeCache.Key(parts.ToArray());
            }
            catch{return null;}
        }
        internal string InstalledShaderRoot{get{return Path.GetFullPath((string)root.GetValue(null));}}
        internal byte[] CompileFixed(string source)
        {
            object result=null;var p=compile.GetParameters();
            try
            {
                result=compile.Invoke(null,new object[]{source,"__pixel_shader","ps_5_0",Enum.ToObject(p[3].ParameterType,1<<15),Enum.ToObject(p[4].ParameterType,0),"HDR.TrustedPortalClip",Enum.ToObject(p[6].ParameterType,0),null});
                if((bool)P(result.GetType(),"HasErrors",Instance).GetValue(result))throw new InvalidOperationException("Private portal shader compilation failed: "+P(result.GetType(),"Message",Instance).GetValue(result));
                object bytes=P(result.GetType(),"Bytecode",Instance).GetValue(result);return (byte[])P(bytes.GetType(),"Data",Instance).GetValue(bytes);
            }
            finally{Dispose(result);}
        }
        internal string[] Inspect(byte[] bytes,bool thresholdExpected)
        {
            Type reflectionType=type("SharpDX.D3DCompiler.ShaderReflection");object reflection=reflectionType.GetConstructor(new[]{typeof(byte[])}).Invoke(new object[]{bytes});
            try
            {
                object flags=P(reflectionType,"RequiresFlags",Instance).GetValue(reflection);
                long early=Convert.ToInt64(Enum.Parse(flags.GetType(),"ShaderRequiresEarlyDepthStencil"));
                if((Convert.ToInt64(flags)&early)!=0)throw new InvalidOperationException("Portal bytecode forces early depth/stencil writes.");
                object desc=P(reflectionType,"Description",Instance).GetValue(reflection);bool found=false;
                if((int)F(desc.GetType(),"InterlockedInstructions",Instance).GetValue(desc)!=0||(int)F(desc.GetType(),"TextureStoreInstructions",Instance).GetValue(desc)!=0)
                    throw new InvalidOperationException("Portal shader has unverified writable side effects.");
                var binding=M(reflectionType,"GetResourceBindingDescription",Instance,typeof(int));
                for(int i=0;i<(int)F(desc.GetType(),"BoundResources",Instance).GetValue(desc);i++)
                {
                    object b=binding.Invoke(reflection,new object[]{i});int slot=(int)F(b.GetType(),"BindPoint",Instance).GetValue(b),count=(int)F(b.GetType(),"BindCount",Instance).GetValue(b);
                    string kind=F(b.GetType(),"Type",Instance).GetValue(b).ToString(),name=(string)F(b.GetType(),"Name",Instance).GetValue(b);
                    bool reserved=thresholdExpected&&kind=="Texture"&&name==PortalClipSource.ThresholdName&&slot==31&&count==1;
                    if(reserved){found=true;continue;}
                    if(kind.IndexOf("Unordered",StringComparison.OrdinalIgnoreCase)>=0||kind.IndexOf("Uav",StringComparison.OrdinalIgnoreCase)>=0||count<1||slot<0||
                        kind=="ConstantBuffer"&&slot+count>8||kind=="Sampler"&&slot+count>16||kind!="ConstantBuffer"&&kind!="Sampler"&&(slot+count>32||slot<=31&&slot+count>31))
                        throw new InvalidOperationException("Portal shader resource binding collision: "+name);
                }
                if(thresholdExpected&&!found)throw new InvalidOperationException("Private portal threshold texture was optimized away.");
                var output=M(reflectionType,"GetOutputParameterDescription",Instance,typeof(int));
                var signatures=new List<string>();
                for(int i=0;i<(int)F(desc.GetType(),"OutputParameters",Instance).GetValue(desc);i++)
                {
                    object o=output.Invoke(reflection,new object[]{i});signatures.Add(string.Join("/",new[]{"SemanticName","SemanticIndex","Register","SystemValueType","ComponentType","UsageMask"}.Select(n=>F(o.GetType(),n,Instance).GetValue(o).ToString())));
                }
                return signatures.ToArray();
            }
            finally{Dispose(reflection);}
        }
        Array EffectiveMacros(Array own)
        {
            var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(macro));var global=globals.GetValue(null) as Array;
            if(global!=null)foreach(var m in global)list.Add(m);if(own!=null)foreach(var m in own)list.Add(m);
            fill.Invoke(null,new object[]{list,true});var result=Array.CreateInstance(macro,list.Count);list.CopyTo(result,0);return result;
        }
        static void ValidateSamples(Array macros)
        {
            foreach(object item in macros)
            {
                string name=(string)F(item.GetType(),"Name",Instance).GetValue(item);if(name!="MS_SAMPLE_COUNT")continue;
                // In this engine single-sample shaders OMIT the macro; defining
                // it as 1 does not define COVERAGE_MASK_ALL and is not a native ABI.
                throw new InvalidOperationException("Portal shaders require the native single-sample variant (MS_SAMPLE_COUNT absent).");
            }
        }
        internal List<PortalClipDescriptor> ReadRegistry(bool includeUnknown=false)
        {
            var result=new List<PortalClipDescriptor>();Type shaders=type("VRageRender.MyPixelShaders");
            object list=F(shaders,"m_shaders",Static).GetValue(null);Array entries=(Array)P(list.GetType(),"Data",Instance).GetValue(list);
            MethodInfo info=M(shaders,"GetInfoId",Static,typeof(int));var compilation=M(type("VRageRender.MyShaders"),"GetCompilationInfo",Static,info.ReturnType);
            for(int i=0;i<entries.Length&&result.Count<8192;i++)
            {
                object value=entries.GetValue(i);if(value==null)continue;object shader=F(value.GetType(),"Shader",Instance).GetValue(value);if(shader==null)continue;
                object spec=compilation.Invoke(null,new[]{info.Invoke(null,new object[]{i})});string file=F(spec.GetType(),"File",Instance).GetValue(spec).ToString().Replace('\\','/');
                if(F(spec.GetType(),"Profile",Instance).GetValue(spec).ToString()!="ps_5_0")continue;
                if(!includeUnknown&&PortalClipSource.Family(file)==PortalClipFamily.Unknown)continue;
                result.Add(new PortalClipDescriptor(shader,file,"native",F(spec.GetType(),"Macros",Instance).GetValue(spec) as Array));
            }
            Type material=type("VRageRender.MyMaterialShaders");Array bundles=(Array)F(material,"Bundles",Static).GetValue(null);
            object materialInfo=F(material,"BundleInfo",Static).GetValue(null);Array infos=(Array)P(materialInfo.GetType(),"Data",Instance).GetValue(materialInfo);
            var getSource=material.GetMethods(Static).Single(m=>m.Name=="GetMaterialSources"&&m.GetParameters().Length==2);
            var passMacro=M(material,"GetRenderingPassMacro",Static,typeof(string));
            var flags=material.GetMethods(Static).Single(m=>m.Name=="AddMaterialShaderFlagMacrosTo"&&m.GetParameters().Length==3);
            for(int i=0;i<bundles.Length&&i<infos.Length&&result.Count<8192;i++)
            {
                object bundle=bundles.GetValue(i);if(bundle==null)continue;object shader=F(bundle.GetType(),"PS",Instance).GetValue(bundle);if(shader==null)continue;
                object spec=infos.GetValue(i);object[] args={F(spec.GetType(),"Material",Instance).GetValue(spec),null};getSource.Invoke(null,args);
                string file=((string)F(args[1].GetType(),"PixelShaderFilepath",Instance).GetValue(args[1])).Replace('\\','/');
                string shaderRoot=Path.GetFullPath((string)root.GetValue(null)).TrimEnd(Path.DirectorySeparatorChar);if(Path.IsPathRooted(file)){string full=Path.GetFullPath(file);if(!full.StartsWith(shaderRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;file=full.Substring(shaderRoot.Length+1).Replace('\\','/');}
                if(!includeUnknown&&PortalClipSource.Family(file)==PortalClipFamily.Unknown)continue;
                string pass=F(spec.GetType(),"Pass",Instance).GetValue(spec).ToString();var macros=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(macro));macros.Add(passMacro.Invoke(null,new object[]{pass}));
                flags.Invoke(null,new[]{(object)macros,F(spec.GetType(),"Flags",Instance).GetValue(spec),F(spec.GetType(),"TextureTypes",Instance).GetValue(spec)});
                object layout=F(spec.GetType(),"Layout",Instance).GetValue(spec),layoutInfo=P(layout.GetType(),"Info",Instance).GetValue(layout);
                foreach(object item in (Array)F(layoutInfo.GetType(),"Macros",Instance).GetValue(layoutInfo))macros.Add(item);
                var array=Array.CreateInstance(macro,macros.Count);macros.CopyTo(array,0);result.Add(new PortalClipDescriptor(shader,file,pass,array));
            }
            return result;
        }
        static PropertyInfo P(Type t,string n,BindingFlags f){return t.GetProperty(n,f)??throw new InvalidOperationException("Portal shader ABI property "+t+"."+n);}
        static FieldInfo F(Type t,string n,BindingFlags f){return t.GetField(n,f)??throw new InvalidOperationException("Portal shader ABI field "+t+"."+n);}
        static MethodInfo M(Type t,string n,BindingFlags f,params Type[] args){return t.GetMethod(n,f,null,args,null)??throw new InvalidOperationException("Portal shader ABI method "+t+"."+n);}
        static void Dispose(object value){if(value is IDisposable)((IDisposable)value).Dispose();}
    }
}
