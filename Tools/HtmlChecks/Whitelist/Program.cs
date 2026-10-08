using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

// Offline host only. Uses the installed real default whitelist/analyzer, never game/native execution.
// No candidate syntax annotations, diagnostics suppressions, whitelist additions or analyzer replacement.
class Program
{
    static string bin;
    static bool ingame;
    static string modCompatibilityHeader;
    static object compiler, whitelist;
    static DiagnosticAnalyzer analyzer;
    static List<MetadataReference> references;
    static readonly Dictionary<string,object> receipt = new Dictionary<string,object>();
    static readonly BindingFlags InstanceAll=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static readonly BindingFlags StaticAll=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
    static readonly string[] GameReferenceNames={"netstandard","Sandbox.Game","Sandbox.Common","Sandbox.Graphics","VRage","VRage.Library","VRage.Math","VRage.Game","VRage.Render","VRage.Input","SpaceEngineers.ObjectBuilders","SpaceEngineers.Game","System.Collections.Immutable","ProtoBuf.Net.Core"};

    static void Main(string[] args)
    {
        string output=args.Length>1?Path.GetFullPath(args[1]):Path.Combine(Environment.CurrentDirectory,"setup-receipt.json");
        bin=args.Length>2?Path.GetFullPath(args[2]):@"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
        try
        {
            string target=args.Length>3?args[3]:"ModApi";
            if(target!="ModApi"&&target!="Ingame")throw new ArgumentException("Target requires ModApi or Ingame.");
            ingame=target=="Ingame";
            AppDomain.CurrentDomain.AssemblyResolve+=(sender,name)=>{
                string path=Path.Combine(bin,new AssemblyName(name.Name).Name+".dll");
                return File.Exists(path)?Assembly.LoadFrom(path):null;
            };
            receipt["Mode"]="Installed actual default "+target+" whitelist analyzer; CPU-only isolated host";
            receipt["HostClr"]=Environment.Version.ToString();
            receipt["HostFramework"]=typeof(object).Assembly.FullName;
            receipt["CandidateCodeExecuted"]=false;
            receipt["NativeOrGameContextInitialized"]=false;
            receipt["CustomWhitelistEntriesAdded"]=false;
            receipt["InjectedSyntaxAnnotations"]=false;
            receipt["OutputReport"]=output;
            Initialize();
            Controls();
            receipt["SetupSucceeded"]=true;
            if(args.Length==0||args[0]=="--setup-only")
            {receipt["CandidateValidated"]=false;receipt["Success"]=true;}
            else
            {
                string source=Path.GetFullPath(args[0]);
                var files=Directory.GetFiles(source,"*.cs",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
                if(files.Length==0)throw new InvalidOperationException("Candidate source set is empty.");
                var before=files.Select(FileRecord).ToList();
                var trees=files.Select(p=>CSharpSyntaxTree.ParseText(Preprocess(File.ReadAllText(p)),new CSharpParseOptions(LanguageVersion.CSharp6),p)).ToArray();
                receipt["PreprocessedSources"]=files.Select(p=>(object)new Dictionary<string,object>{{"File",p},{"SHA256",TextHash(Preprocess(File.ReadAllText(p)))}}).ToList();
                var diagnostics=new List<object>();
                var pbCompilations=new List<object>();
                if(ingame)
                {
                    for(int i=0;i<trees.Length;i++)
                    {
                        string candidateName="HDRHtmlPB_"+i;
                        var originalDiagnostics=Analyze(candidateName,new[]{trees[i]});
                        diagnostics.AddRange(originalDiagnostics);
                        if(originalDiagnostics.Count==0)pbCompilations.Add(RewriteAndEmit(candidateName,new[]{trees[i]}));
                    }
                    receipt["PbCompilations"]=pbCompilations;
                    receipt["MemorySafeRewriteApplied"]=pbCompilations.Count==files.Length;
                    receipt["RewrittenEmitSucceeded"]=pbCompilations.Count==files.Length&&pbCompilations.Cast<Dictionary<string,object>>().All(r=>(bool)r["RewrittenEmitSucceeded"]);
                }
                else diagnostics=Analyze("HDRHtmlCandidate",trees);
                var filesAfter=Directory.GetFiles(source,"*.cs",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
                var after=filesAfter.Select(FileRecord).ToList();
                bool stable=files.SequenceEqual(filesAfter,StringComparer.Ordinal)&&Json(before)==Json(after);
                receipt["SourceRoot"]=source;receipt["SourceFiles"]=before;receipt["SourceStableDuringGate"]=stable;
                receipt["CandidateSourceCount"]=files.Length;receipt["CandidateDiagnostics"]=diagnostics;
                receipt["CandidateValidated"]=true;receipt["Success"]=stable&&diagnostics.Count==0&&(!ingame||(bool)receipt["RewrittenEmitSucceeded"]);
                if(!stable)throw new InvalidOperationException("Candidate changed during gate; freeze and rerun.");
                if(!(bool)receipt["Success"])Environment.ExitCode=1;
            }
        }
        catch(Exception error)
        {
            receipt["Success"]=false;receipt["Error"]=error.ToString();Environment.ExitCode=2;
            Console.Error.WriteLine(error);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output,Json(receipt),new UTF8Encoding(false));
        Console.WriteLine("RECEIPT "+output+" SUCCESS "+(receipt.ContainsKey("Success")?receipt["Success"]:false));
        if(receipt.ContainsKey("CandidateDiagnostics"))foreach(var item in (List<object>)receipt["CandidateDiagnostics"])Console.WriteLine(Json(item));
    }

    static void Initialize()
    {
        var script=Assembly.LoadFrom(Path.Combine(bin,"VRage.Scripting.dll"));
        Type compilerType=script.GetType("VRage.Scripting.MyScriptCompiler",true);
        compiler=Activator.CreateInstance(compilerType,true);

        // Reproduce the inspected compiler's game reference set. Host BCL references are needed for
        // CLR-forwarded metadata under modern .NET; they never grant whitelist permission.
        var refPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trusted=AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if(!string.IsNullOrEmpty(trusted))
            foreach(string path in trusted.Split(Path.PathSeparator))
                if(Path.GetFileName(path).StartsWith("System.",StringComparison.Ordinal)||Path.GetFileName(path)=="mscorlib.dll"||Path.GetFileName(path)=="netstandard.dll")refPaths.Add(path);
        else
            foreach(Type type in new[]{typeof(object),typeof(Enumerable),typeof(System.Xml.XmlEntity),typeof(System.ComponentModel.INotifyPropertyChanged),typeof(Uri)})refPaths.Add(type.Assembly.Location);
        foreach(string name in GameReferenceNames)
        {string path=Path.Combine(bin,name+".dll");if(File.Exists(path))refPaths.Add(path);else throw new FileNotFoundException("Missing inspected game reference",path);}
        compilerType.GetMethod("AddReferencedAssemblies",InstanceAll).Invoke(compiler,new object[]{refPaths.ToArray()});
        whitelist=compilerType.GetProperty("Whitelist",InstanceAll).GetValue(compiler,null);
        object batch=whitelist.GetType().GetMethod("OpenBatch",InstanceAll).Invoke(whitelist,null);
        var registration=Assembly.LoadFrom(Path.Combine(bin,"SpaceEngineers.Game.dll")).GetType("SpaceEngineers.Game.MySpaceGameDefaultIlChecker",true);
        var routines=new List<object>();
        try
        {
            foreach(string name in new[]{"AllowDefaultNamespaces","AllowSandboxNamespaces","AllowSpaceEngineersNamespaces"})
            {registration.GetMethod(name,StaticAll).Invoke(null,new[]{batch});routines.Add(name);}
        }
        finally{((IDisposable)batch).Dispose();}
        if(ingame)ApplyDefaultIngameBlacklist();
        else ReadModCompatibilityHeader();
        receipt["SourcePreprocessing"]=ingame?"CallerProvidedWrappedPB":"InstalledOfficialModCompatibilityHeader";
        analyzer=(DiagnosticAnalyzer)compilerType.GetField(ingame?"m_inGameWhitelistDiagnosticAnalyzer":"m_modApiWhitelistDiagnosticAnalyzer",InstanceAll).GetValue(compiler);
        object selectedTarget=analyzer.GetType().GetField("m_target",InstanceAll).GetValue(analyzer);
        if(Convert.ToInt32(selectedTarget,CultureInfo.InvariantCulture)!=(ingame?2:1))throw new InvalidOperationException("Wrong whitelist target.");
        references=(List<MetadataReference>)compilerType.GetField("m_metadataReferences",InstanceAll).GetValue(compiler);
        receipt["DefaultRegistrationRoutines"]=routines;
        receipt["SelectedTarget"]=selectedTarget.ToString();receipt["SelectedTargetNumeric"]=ingame?2:1;
        receipt["AnalyzerType"]=analyzer.GetType().FullName;
        receipt["ParseLanguageVersion"]="CSharp6";
        receipt["WhitelistOwner"]=whitelist.GetType().FullName;
        receipt["AnalyzerAssembly"]=FileRecord(script.Location);
        receipt["RegistrationAssembly"]=FileRecord(registration.Assembly.Location);
        receipt["References"]=references.Select(r=>FileRecord(((PortableExecutableReference)r).FilePath)).ToList();
        object registered=whitelist.GetType().GetMethod("GetWhitelist",InstanceAll).Invoke(whitelist,null);
        receipt["WhitelistEntryCount"]=registered.GetType().GetProperty("Count").GetValue(registered,null);
    }

    static string Preprocess(string source){return ingame?source:modCompatibilityHeader+source;}
    static void ReadModCompatibilityHeader()
    {
        var game=Assembly.LoadFrom(Path.Combine(bin,"Sandbox.Game.dll"));
        var method=game.GetType("Sandbox.Game.World.MyScriptManager",true).GetMethod("UpdateCompatibility",StaticAll);
        byte[] il=method.GetMethodBody().GetILAsByteArray();
        var opcodes=new Dictionary<short,OpCode>();
        foreach(var field in typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static))if(field.FieldType==typeof(OpCode)){var op=(OpCode)field.GetValue(null);opcodes[op.Value]=op;}
        for(int index=0;index<il.Length;)
        {
            short key=il[index++];if(key==254)key=(short)(0xfe00|il[index++]);
            var op=opcodes[key];int length;
            switch(op.OperandType)
            {
                case OperandType.InlineNone:length=0;break;
                case OperandType.ShortInlineBrTarget:case OperandType.ShortInlineI:case OperandType.ShortInlineVar:length=1;break;
                case OperandType.InlineVar:length=2;break;
                case OperandType.InlineI8:case OperandType.InlineR:length=8;break;
                case OperandType.InlineSwitch:length=4+4*BitConverter.ToInt32(il,index);break;
                default:length=4;break;
            }
            if(op.OperandType==OperandType.InlineString)
            {
                string literal=method.Module.ResolveString(BitConverter.ToInt32(il,index));
                if(literal.StartsWith("using VRage;",StringComparison.Ordinal)&&literal.Contains("using Sandbox.ModAPI;")&&literal.Contains("#line 1"))
                {if(modCompatibilityHeader!=null)throw new InvalidOperationException("Multiple official compatibility header candidates.");modCompatibilityHeader=literal;}
            }
            index+=length;
        }
        if(string.IsNullOrEmpty(modCompatibilityHeader))throw new InvalidOperationException("Installed official mod compatibility header was not found; inspect new compiler profile.");
        receipt["ModCompatibilityHeaderApplied"]=true;
        receipt["ModCompatibilityHeader"]=modCompatibilityHeader;
        receipt["ModCompatibilityHeaderSHA256"]=TextHash(modCompatibilityHeader);
        receipt["ModCompatibilityEvidenceAssembly"]=FileRecord(game.Location);
        receipt["ModCompatibilityEvidenceMethod"]=method.DeclaringType.FullName+"."+method.Name;
        receipt["ModCompatibilityMethodILSHA256"]=BytesHash(il);
        receipt["ModCompatibilityHistoricalStringReplacementsApplied"]=false;
    }

    static void ApplyDefaultIngameBlacklist()
    {
        // Exact stock restrictions inspected in Sandbox.MySandboxGame.InitIlChecker.
        // These are additional denials through the actual engine blacklist, never whitelist grants.
        object batch=whitelist.GetType().GetMethod("OpenIngameBlacklistBatch",InstanceAll).Invoke(whitelist,null);
        var contract=Assembly.LoadFrom(Path.Combine(bin,"VRage.dll")).GetType("VRage.Scripting.IMyScriptBlacklistBatch",true);
        MethodInfo add=contract.GetMethod("AddMembers");
        var groups=new[]{
            Tuple.Create(typeof(System.IO.Path),new[]{"GetTempFileName"}),
            Tuple.Create(typeof(CultureInfo),new[]{"DefaultThreadCurrentCulture","DefaultThreadCurrentUICulture"}),
            Tuple.Create(typeof(Encoding),new[]{"RegisterProvider"}),
            Tuple.Create(typeof(System.Text.RegularExpressions.Regex),new[]{"CacheSize"})
        };
        var entries=new List<object>();
        try
        {
            foreach(var group in groups)
            {
                add.Invoke(batch,new object[]{group.Item1,group.Item2});
                entries.Add(new Dictionary<string,object>{{"Type",group.Item1.FullName},{"Members",group.Item2}});
            }
        }
        finally{((IDisposable)batch).Dispose();}
        receipt["DefaultIngameBlacklistApplied"]=true;
        receipt["DefaultIngameBlacklistRestrictions"]=entries;
        receipt["BlacklistEvidenceAssembly"]=FileRecord(Path.Combine(bin,"Sandbox.Game.dll"));
        receipt["BlacklistEvidenceMethod"]="Sandbox.MySandboxGame.InitIlChecker";
    }

    static void Controls()
    {
        string positiveSource=ingame?@"using System;using System.Collections.Generic;using System.Text;using Sandbox.ModAPI.Ingame;using Sandbox.ModAPI.Interfaces;using VRageMath;public class Program:MyGridProgram{public void Main(string argument,UpdateType updateSource){var items=new List<string>();items.Add(argument);var text=new StringBuilder();text.Append(Math.Abs(new Vector2(1,2).X));var block=GridTerminalSystem.GetBlockWithName(""HDR Display"");var property=Me.GetProperty(""HDR.Draw"");if(block!=null&&property!=null)Echo(text.ToString());}}":@"using System;using System.Collections.Generic;using System.Text;using Sandbox.ModAPI;using VRageMath;namespace Owned{public class Local{public double Abs(double value){return Math.Abs(value);}public string Name(){var items=new Dictionary<string,int>();items.Add(""owned"",1);var text=new StringBuilder();text.Append(items[""owned""]);var point=new Vector2(1,2);return text.ToString()+point.X+MyAPIGateway.Utilities.IsDedicated;}}}";
        var positive=AnalyzeSource("PositiveControl",positiveSource);
        var reflection=AnalyzeSource("UnusedForbiddenReflection",@"using System.Reflection;namespace Owned{public class Local{public void NeverCalled(){Assembly.Load(""not-executed"");}}}");
        var pragma=AnalyzeSource("ForbiddenPragma",@"#pragma warning disable CS0168
namespace Owned{public class Local{public int Add(int x){return x+1;}}}");
        receipt["PositiveControlDiagnostics"]=positive;receipt["UnusedReflectionControlDiagnostics"]=reflection;receipt["PragmaControlDiagnostics"]=pragma;
        if(positive.Count!=0)throw new InvalidOperationException("Known allowed control failed; registry/reference setup is not certified.");
        if(!HasWhitelistError(reflection)||!HasWhitelistError(pragma))throw new InvalidOperationException("Negative controls were not rejected by actual whitelist analyzer.");
        if(ingame)
        {
            var modOnly=AnalyzeSource("ForbiddenModOnlyApi",@"namespace Owned{public class Local{public bool NeverCalled(){return Sandbox.ModAPI.MyAPIGateway.Utilities.IsDedicated;}}}");
            var stockBlacklist=AnalyzeSource("ForbiddenDefaultBlacklist",@"namespace Owned{public class Local{public string NeverCalled(){return System.IO.Path.GetTempFileName();}}}");
            receipt["ModOnlyControlDiagnostics"]=modOnly;receipt["DefaultBlacklistControlDiagnostics"]=stockBlacklist;
            if(!HasWhitelistError(modOnly)||!HasWhitelistError(stockBlacklist))throw new InvalidOperationException("Ingame-specific/default blacklist controls were not rejected.");
            var controlRewrite=(Dictionary<string,object>)RewriteAndEmit("PositiveControl",new[]{CSharpSyntaxTree.ParseText(positiveSource,new CSharpParseOptions(LanguageVersion.CSharp6),"PositiveControl.cs")});
            receipt["PositiveControlMemorySafeRewrite"]=controlRewrite;
            if(!(bool)controlRewrite["RewrittenEmitSucceeded"])throw new InvalidOperationException("Positive PB control memory-safe rewrite did not emit.");
        }
        else
        {
            var collision=AnalyzeSource("CompatibilityAliasCollision",@"using Sandbox.ModAPI.Ingame;public class Local{public IMyTextSurface Surface;}");
            var alias=AnalyzeSource("CompatibilityAliasCorrect",@"using IMyTextSurface=Sandbox.ModAPI.Ingame.IMyTextSurface;public class Local{public IMyTextSurface Surface;}");
            receipt["CompatibilityAmbiguityControlDiagnostics"]=collision;receipt["CompatibilityAliasPositiveDiagnostics"]=alias;
            if(!collision.Cast<Dictionary<string,object>>().Any(e=>(string)e["Origin"]=="Compiler"&&(string)e["Id"]=="CS0104")||alias.Count!=0)throw new InvalidOperationException("Official compatibility header controls did not behave as expected.");
            receipt["ModCompatibilityControlsPassed"]=true;
        }
        receipt["ActualWhitelistControlsPassed"]=true;
    }
    static bool HasWhitelistError(List<object> entries)
    {return entries.Cast<Dictionary<string,object>>().Any(e=>(string)e["Origin"]=="Analyzer"&&(string)e["Severity"]=="Error");}
    static List<object> AnalyzeSource(string name,string source)
    {return Analyze(name,new[]{CSharpSyntaxTree.ParseText(Preprocess(source),new CSharpParseOptions(LanguageVersion.CSharp6),name+".cs")});}
    static List<object> Analyze(string name,SyntaxTree[] trees)
    {
        var compilation=CSharpCompilation.Create(name,trees,references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result=new List<object>();
        foreach(var error in compilation.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error))result.Add(DiagnosticRecord(compilation,error,"Compiler"));
        foreach(var error in compilation.WithAnalyzers(ImmutableArray.Create(analyzer)).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult().Where(d=>d.Severity==DiagnosticSeverity.Error||d.Id=="AD0001"))result.Add(DiagnosticRecord(compilation,error,"Analyzer"));
        return result;
    }
    static object RewriteAndEmit(string name,SyntaxTree[] trees)
    {
        var original=CSharpCompilation.Create(name,trees,references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var rewritten=original;
        Type rewriter=Assembly.LoadFrom(Path.Combine(bin,"VRage.Scripting.dll")).GetType("VRage.Scripting.Rewriters.TypeSafetyAndBlockRewriter",true);
        var outputs=new List<object>();bool changed=false;
        foreach(var tree in trees)
        {
            var visitor=(CSharpSyntaxRewriter)Activator.CreateInstance(rewriter,InstanceAll,null,new object[]{original.GetSemanticModel(tree),tree,true},null);
            var rewrittenTree=CSharpSyntaxTree.Create((CSharpSyntaxNode)visitor.Visit(tree.GetRoot()),new CSharpParseOptions(LanguageVersion.CSharp6),tree.FilePath);
            changed|=tree.GetRoot().ToFullString()!=rewrittenTree.GetRoot().ToFullString();
            rewritten=rewritten.ReplaceSyntaxTree(tree,rewrittenTree);
            outputs.Add(new Dictionary<string,object>{{"SourceFile",tree.FilePath},{"RewrittenSourceSHA256",TextHash(rewrittenTree.GetRoot().ToFullString())}});
        }
        var diagnostics=new List<object>();long emittedBytes;
        using(var stream=new MemoryStream())
        {
            var result=rewritten.Emit(stream);
            foreach(var error in result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error))diagnostics.Add(DiagnosticRecord(rewritten,error,"RewrittenCompiler"));
            emittedBytes=stream.Length;
        }
        receipt["RewriterType"]=rewriter.FullName;
        receipt["OriginalSourceWhitelistCheckedBeforeRewrite"]=true;
        receipt["CustomInjectedSyntaxAnnotations"]=false;
        receipt["OfficialEngineRewriterMayProduceAnnotations"]=true;
        return new Dictionary<string,object>{{"Compilation",name},{"MemorySafeRewriteApplied",true},{"RewriterType",rewriter.FullName},{"RewrittenTextChanged",changed},{"RewrittenEmitSucceeded",diagnostics.Count==0&&emittedBytes>0},{"EmittedBytes",emittedBytes},{"RewrittenDiagnostics",diagnostics},{"Sources",outputs}};
    }
    static string TextHash(string text)
    {return BytesHash(Encoding.UTF8.GetBytes(text));}
    static string BytesHash(byte[] bytes)
    {using(var algorithm=SHA256.Create())return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-","");}
    static object DiagnosticRecord(CSharpCompilation compilation,Diagnostic diagnostic,string origin)
    {
        var entry=new Dictionary<string,object>{{"Origin",origin},{"Id",diagnostic.Id},{"Severity",diagnostic.Severity.ToString()},{"Message",diagnostic.GetMessage(CultureInfo.InvariantCulture)}};
        if(diagnostic.Location.IsInSource)
        {
            var location=diagnostic.Location.GetMappedLineSpan();entry["File"]=location.Path;entry["Line"]=location.StartLinePosition.Line+1;entry["Column"]=location.StartLinePosition.Character+1;
            var tree=diagnostic.Location.SourceTree;var node=tree.GetRoot().FindNode(diagnostic.Location.SourceSpan);entry["SourceNode"]=node.ToString();
            var model=compilation.GetSemanticModel(tree);var symbol=model.GetSymbolInfo(node).Symbol;
            if(symbol!=null){entry["Symbol"]=symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);entry["ExternalAssembly"]=symbol.ContainingAssembly==null?null:symbol.ContainingAssembly.Identity.ToString();}
        }
        return entry;
    }
    static object FileRecord(string path)
    {
        using(var algorithm=SHA256.Create())return new Dictionary<string,object>{{"File",Path.GetFullPath(path)},{"SHA256",BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path))).Replace("-","")},{"Bytes",new FileInfo(path).Length}};
    }
    // Tiny report writer keeps the same host source usable in a .NET Framework CPU host.
    static string Json(object value)
    {
        if(value==null)return "null";
        if(value is string)return Quote((string)value);
        if(value is bool)return (bool)value?"true":"false";
        var dictionary=value as IDictionary;
        if(dictionary!=null){var entries=new List<string>();foreach(DictionaryEntry entry in dictionary)entries.Add(Quote(Convert.ToString(entry.Key,CultureInfo.InvariantCulture))+":"+Json(entry.Value));return "{"+string.Join(",",entries)+"}";}
        var enumerable=value as IEnumerable;
        if(enumerable!=null){var entries=new List<string>();foreach(object entry in enumerable)entries.Add(Json(entry));return "["+string.Join(",",entries)+"]";}
        return Convert.ToString(value,CultureInfo.InvariantCulture);
    }
    static string Quote(string text)
    {var output=new StringBuilder("\"");foreach(char ch in text){if(ch=='\\'||ch=='\"')output.Append('\\').Append(ch);else if(ch<' ')output.Append("\\u").Append(((int)ch).ToString("x4",CultureInfo.InvariantCulture));else output.Append(ch);}return output.Append('"').ToString();}
}
