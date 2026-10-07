#if MOD_CLIENT_NATIVE_PROOF
using System.Reflection;
using System.Runtime.Loader;
// Opt-in read-only installed-engine IL inspection; never part of mod/game code.
internal static class ModClientNativeProof
{
    static void Main()
    {
        string bin=Environment.GetEnvironmentVariable("SE_BIN")??@"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
        AssemblyLoadContext.Default.Resolving+=(context,name)=>{string path=Path.Combine(bin,name.Name+".dll");return File.Exists(path)?context.LoadFromAssemblyPath(path):null;};
        var dump=typeof(Program).GetMethod("DumpIl",BindingFlags.Static|BindingFlags.NonPublic);
        var script=Assembly.LoadFrom(Path.Combine(bin,"VRage.Scripting.dll"));var sandbox=Assembly.LoadFrom(Path.Combine(bin,"Sandbox.Game.dll"));
        foreach(var name in new[]{"VRage.Scripting.MyScriptCompiler","VRage.Scripting.MyScriptWhitelist"})
        {var t=script.GetType(name);foreach(var m in t.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public))dump.Invoke(null,new object[]{m});}
        var game=sandbox.GetType("Sandbox.MySandboxGame");dump.Invoke(null,new object[]{game.GetMethod("InitIlCompiler",BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)});
        var space=Assembly.LoadFrom(Path.Combine(bin,"SpaceEngineers.Game.dll"));
        var platform=Assembly.LoadFrom(Path.Combine(bin,"VRage.Platform.Windows.dll"));
        foreach(var m in script.GetType("VRage.Scripting.MyVRageScriptingInternal").GetMethods(BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public))if(m.Name=="Initialize"||m.Name=="Init"||m.Name.Contains("Whitelist"))dump.Invoke(null,new object[]{m});
        foreach(var assembly in new[]{script,platform})foreach(var t in Types(assembly))if(t.Name.IndexOf("Whitelist",StringComparison.OrdinalIgnoreCase)>=0||t.Name.IndexOf("Scripting",StringComparison.OrdinalIgnoreCase)>=0)
        {Console.WriteLine(t.FullName);foreach(var m in t.GetMethods(BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly))Console.WriteLine(m);}
        foreach(var t in space.GetTypes())if(t.Name.IndexOf("Compiler",StringComparison.OrdinalIgnoreCase)>=0||t.Name.IndexOf("Whitelist",StringComparison.OrdinalIgnoreCase)>=0)
        {Console.WriteLine(t.FullName);foreach(var m in t.GetMethods(BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly))if(m.Name.Contains("Init")||m.Name.Contains("Whitelist"))dump.Invoke(null,new object[]{m});}
        var utilities=typeof(Sandbox.ModAPI.MyAPIGateway).GetField("Utilities").FieldType;foreach(var m in utilities.GetMembers())if(m.Name.Contains("Thread"))Console.WriteLine(m);
        // The exact HUD pass is inspected separately from enum-name presence.
        var render=Assembly.LoadFrom(Path.Combine(bin,"VRage.Render11.dll"));
        foreach(var name in new[]{"VRageRender.MyBillboardRenderer","VRageRender.MyTransparentRendering"})
        {var t=render.GetType(name);if(t!=null)foreach(var m in t.GetMethods(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public))if(m.Name.Contains("PostPP"))dump.Invoke(null,new object[]{m});}
    }
    static IEnumerable<Type> Types(Assembly assembly){try{return assembly.GetTypes();}catch(ReflectionTypeLoadException error){return error.Types.Where(t=>t!=null);}}
}
#endif
