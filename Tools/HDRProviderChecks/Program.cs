using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;

class Program
{
    static string bin = @"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
    static void Main(string[] args)
    {
        string root = Path.GetFullPath(args.Length == 0 ? "HDR_API" : args[0]);
        string source = Path.Combine(root, "OptionalMods/HDRInputBackend/Data/Scripts");
        string framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319");
        var references = new[] {"mscorlib.dll","System.dll","System.Core.dll","System.Xml.dll"}.Select(p=>Path.Combine(framework,p))
            .Concat(new[]{"netstandard.dll","System.Collections.Immutable.dll","Sandbox.Common.dll","Sandbox.Game.dll","SpaceEngineers.Game.dll","SpaceEngineers.ObjectBuilders.dll","VRage.dll","VRage.Game.dll","VRage.Library.dll","VRage.Math.dll","VRage.Render.dll","VRage.Input.dll","VRage.Scripting.dll","ProtoBuf.Net.Core.dll","ProtoBuf.Net.dll"}.Select(p=>Path.Combine(bin,p)))
            .Select(p=>(MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
        var trees = Directory.GetFiles(source,"*.cs",SearchOption.AllDirectories).Select(p=>CSharpSyntaxTree.ParseText(File.ReadAllText(p),new CSharpParseOptions(LanguageVersion.CSharp6),p));
        var compilation = CSharpCompilation.Create("HDRInputBackend",trees,references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        foreach (var error in result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)) Console.WriteLine(error);
        if (!result.Success) throw new Exception("Optional provider did not compile.");
        AssemblyLoadContext.Default.Resolving += (c,n)=>{string path=Path.Combine(bin,n.Name+".dll");return File.Exists(path)?c.LoadFromAssemblyPath(path):null;};
        var assembly = Assembly.Load(output.ToArray());
        var range = assembly.GetType("RichHudFramework.Server.BindRange",true);
        var manager = assembly.GetType("RichHudFramework.Server.BlacklistManager",true);
        var validate = manager.GetMethod("ValidateRanges",BindingFlags.Static|BindingFlags.NonPublic);
        object Range(int start,int count)=>Activator.CreateInstance(range,new object[]{start,count});
        bool Validate(params (int,int)[] values) {var array=Array.CreateInstance(range,values.Length);for(int i=0;i<values.Length;i++)array.SetValue(Range(values[i].Item1,values[i].Item2),i);return(bool)validate.Invoke(null,new[]{(object)array});}
        int count=((string[])assembly.GetType("RichHudFramework.UI.Server.BindManager").GetField("BuiltInBinds",BindingFlags.Static|BindingFlags.Public).GetValue(null)).Length;
        int passed = 0;
        void Require(bool value,string label){if(!value)throw new Exception(label);passed++;}
        Require(Validate((0,count)),"full range accepted");
        Require(Validate((count,0)),"empty endpoint accepted");
        Require(!Validate((-1,1)),"negative start rejected");
        Require(!Validate((0,-1)),"negative count rejected");
        Require(!Validate((count,1)),"endpoint overflow rejected");
        Require(!Validate((int.MaxValue,int.MaxValue)),"integer overflow rejected");
        Require(!Validate((0,count),(0,count)),"amplification rejected");
        Require(!Validate(Enumerable.Repeat((0,0),count+1).ToArray()),"range count bounded");
        var policy = assembly.GetType("RichHudFramework.Server.HdrNetworkBounds",true);
        var packet = policy.GetMethod("PacketAllowed",BindingFlags.Static|BindingFlags.NonPublic);
        bool Packet(int size,int queued,bool server,bool client,bool direction,ulong sender,ushort channel=50972) =>
            (bool)packet.Invoke(null,new object[]{channel,(ushort)50972,size,queued,server,client,direction,sender,(ulong)123});
        Require(Packet(8192,0,false,true,true,123),"authenticated server packet accepted");
        Require(Packet(1,0,false,true,true,0),"dedicated server sender zero accepted");
        Require(!Packet(8193,0,false,true,true,123),"oversize packet rejected before parsing");
        Require(!Packet(0,0,false,true,true,123),"empty packet rejected");
        Require(!Packet(1,128,false,true,true,123),"full input queue rejected");
        Require(!Packet(1,0,false,true,true,456),"wrong server sender rejected");
        Require(!Packet(1,0,false,true,false,123),"client peer cannot forge direction");
        Require(Packet(1,0,true,false,false,456),"server accepts actual client direction");
        Require(!Packet(1,0,true,false,true,123),"dedicated server cannot accept claimed replies");
        Require(!Packet(1,0,true,false,false,0),"zero client sender rejected");
        Require(!Packet(1,0,false,true,true,123,3),"wrong channel rejected");
        var batch = policy.GetMethod("BatchAllowed",BindingFlags.Static|BindingFlags.NonPublic);
        bool Batch(int size,int queued)=>(bool)batch.Invoke(null,new object[]{size,queued});
        Require(Batch(20,108),"bounded batch accepted");
        Require(!Batch(21,0),"oversize batch rejected");
        Require(!Batch(20,109),"parsed queue bounded");
        Require(!Batch(int.MaxValue,int.MaxValue),"batch integer overflow rejected");
        var budgetType=assembly.GetType("RichHudFramework.Server.HdrRequestBudget",true);
        var budget=Activator.CreateInstance(budgetType,true);
        var consume=budgetType.GetMethod("TryConsume",BindingFlags.Instance|BindingFlags.NonPublic);
        bool Consume(ulong peer,long tick,int requests)=>(bool)consume.Invoke(budget,new object[]{peer,tick,requests});
        Require(Consume(1,0,20),"20 requests accepted in window");
        Require(!Consume(1,59,1),"same peer requests throttled");
        Require(Consume(1,60,1),"window replenishes");
        Require(Consume(2,60,20),"peer budgets independent");
        Require(!Consume(3,60,int.MaxValue),"request amplification rejected");
        for(ulong i=3;i<=256;i++) Require(Consume(i,60,1),"bounded peer record "+i);
        Require(!Consume(257,60,1),"peer record capacity bounded");
        budgetType.GetMethod("Prune",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(budget,new object[]{(long)661});
        Require(Consume(257,661,1),"stale peer records reclaimed");
        var clientType=assembly.GetType("RichHudFramework.UI.Server.BindManager+Client",true);
        var uninitialized=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(clientType);
        var api=clientType.GetMethod("GetOwnedInputApi").Invoke(uninitialized,null);
        var apiType=api.GetType();
        Require((int)apiType.GetField("Item1").GetValue(api)==0,"capture capability unsupported");
        Require(!(bool)((Delegate)apiType.GetField("Item2").GetValue(api)).DynamicInvoke(7),"capture request fails closed");
        Require((int)((Delegate)apiType.GetField("Item3").GetValue(api)).DynamicInvoke()==0,"applied input mask zero");
        Require(!(bool)((Delegate)apiType.GetField("Item5").GetValue(api)).DynamicInvoke(),"capture ready remains false");
        Console.WriteLine("Optional RHF provider C#6 compilation and " + passed + " hostile boundary fixtures passed.");
    }
}
