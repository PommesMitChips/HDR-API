using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace HDRClientRenderer
{
    // CPU-only installed-assembly call audit. Call-site coverage is checked
    // against the draw boundary before any global hook can be installed.
    internal static class PortalCaptureCoverage
    {
        internal static bool CertifiedRuntime(PlatformID platform,int pointerBytes,int clrMajor){return platform==PlatformID.Win32NT&&pointerBytes==8&&clrMajor==10;}
        internal static void ValidateRuntime()
        {if(!CertifiedRuntime(Environment.OSVersion.Platform,IntPtr.Size,Environment.Version.Major))throw new InvalidOperationException("Portal enclosing draw/JIT coverage is certified only for Windows x64 .NET 10; ordinary camera/raster rendering remains available.");}
        static readonly Dictionary<short,OpCode> opcodes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(o=>o.Value);
        internal static MethodBase[] Calls(MethodBase method)
        {
            var body=method.GetMethodBody();if(body==null)return new MethodBase[0];var bytes=body.GetILAsByteArray();var result=new List<MethodBase>();int i=0;
            while(i<bytes.Length)
            {
                short code=bytes[i++];if(code==254)code=(short)(0xfe00|bytes[i++]);OpCode op=opcodes[code];int length;
                switch(op.OperandType)
                {
                    case OperandType.InlineNone:length=0;break;
                    case OperandType.ShortInlineBrTarget:case OperandType.ShortInlineI:case OperandType.ShortInlineVar:length=1;break;
                    case OperandType.InlineVar:length=2;break;
                    case OperandType.InlineI8:case OperandType.InlineR:length=8;break;
                    case OperandType.InlineSwitch:length=checked(4+4*BitConverter.ToInt32(bytes,i));break;
                    default:length=4;break;
                }
                if(op.OperandType==OperandType.InlineMethod)
                {
                    var value=method.Module.ResolveMethod(BitConverter.ToInt32(bytes,i),method.DeclaringType.IsGenericType?method.DeclaringType.GetGenericArguments():null,method.IsGenericMethod?method.GetGenericArguments():null);
                    if(value!=null)result.Add(value);
                }
                i+=length;
            }
            return result.ToArray();
        }
        internal static KeyValuePair<MethodBase,MethodBase[]>[] DrawSites(Assembly assembly)
        {
            var result=new List<KeyValuePair<MethodBase,MethodBase[]>>();
            foreach(var type in assembly.GetTypes())foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)))
            {
                if(method.IsAbstract)continue;
                var calls=Calls(method).Where(m=>m.DeclaringType.FullName=="SharpDX.Direct3D11.DeviceContext"&&m.Name.StartsWith("Draw",StringComparison.Ordinal)).ToArray();
                if(calls.Length!=0)result.Add(new KeyValuePair<MethodBase,MethodBase[]>(method,calls));
            }
            return result.ToArray();
        }
        internal static MethodInfo[] ValidateDrawBoundary(Assembly assembly)
        {
            var sites=DrawSites(assembly);string[] required={"Draw","DrawAuto","DrawIndexed","DrawInstanced","DrawIndexedInstanced","DrawIndexedInstancedIndirect"};
            if(sites.Length!=required.Length||sites.Any(s=>s.Key.DeclaringType.FullName!="VRage.Render11.RenderContext.MyRenderContext"||!(s.Key is MethodInfo)||s.Value.Length!=1)||
                !sites.Select(s=>s.Key.Name).OrderBy(n=>n).SequenceEqual(required.OrderBy(n=>n)))throw new InvalidOperationException("Portal draw-context coverage differs from the audited installed renderer.");
            return sites.Select(s=>(MethodInfo)s.Key).ToArray();
        }
        internal static MethodBase[] RenderDrawCallers(Assembly assembly,IEnumerable<MethodInfo> renderDraws)
        {
            var targets=new HashSet<MethodBase>(renderDraws);var result=new List<MethodBase>();
            foreach(var type in assembly.GetTypes())foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)))
                if(!method.IsAbstract&&Calls(method).Any(targets.Contains))result.Add(method);
            if(result.Count==0)throw new InvalidOperationException("Portal renderer has no auditable enclosing draw callers.");return result.ToArray();
        }
        internal static MethodBase[] TransitiveRenderCallers(Assembly assembly,IEnumerable<MethodBase> directCallers)
        {
            var direct=new HashSet<int>(directCallers.Select(m=>m.MetadataToken));var affected=new HashSet<int>(direct);
            var methods=assembly.GetTypes().SelectMany(t=>t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Cast<MethodBase>().Concat(t.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static))).Where(m=>!m.IsAbstract&&m.GetMethodBody()!=null).ToArray();
            var edges=methods.ToDictionary(m=>m.MetadataToken,m=>Calls(m).Where(c=>c.Module==assembly.ManifestModule).Select(c=>c.MetadataToken).ToArray());bool changed;
            do{changed=false;foreach(var method in methods)if(!affected.Contains(method.MetadataToken)&&edges[method.MetadataToken].Any(affected.Contains)){affected.Add(method.MetadataToken);changed=true;}}while(changed);
            return methods.Where(m=>affected.Contains(m.MetadataToken)&&!direct.Contains(m.MetadataToken)).ToArray();
        }
        internal static IEnumerable<HarmonyLib.CodeInstruction> RecompileCaller(IEnumerable<HarmonyLib.CodeInstruction> instructions){return instructions;}
        internal const string AuditedShaderFingerprint=PortalShaderBytecodeCache.AuditedShaderFingerprint;
        internal static string ShaderFingerprint(string shaderRoot)
        {
            string root=Path.GetFullPath(shaderRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);var lines=new List<string>();
            using(var sha=SHA256.Create())
            {
                foreach(string file in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories))
                {
                    string extension=Path.GetExtension(file);if(!extension.Equals(".hlsl",StringComparison.OrdinalIgnoreCase)&&!extension.Equals(".hlsli",StringComparison.OrdinalIgnoreCase))continue;
                    using(var stream=File.OpenRead(file))lines.Add(file.Substring(root.Length+1).Replace('\\','/')+":"+Hex(sha.ComputeHash(stream)));
                }
                lines.Sort(StringComparer.Ordinal);return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n",lines))));
            }
        }
        internal static void ValidateAssets(string root)
        {if(ShaderFingerprint(root)!=AuditedShaderFingerprint)throw new InvalidOperationException("Portal auxiliary shader assets changed from the audited installed build; geometry-only capture is unavailable.");}
        static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
    }
}
