using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.ModAPI.Interfaces;
using PbBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;
using VRage;

internal static class TypedApiBoundaryTests
{
    public static int Run(string root, IEnumerable<MetadataReference> references, CSharpParseOptions options)
    {
        int count = 0;
        void Check(bool value, string clause) { if (!value) throw new Exception("Typed PB API boundary: " + clause); count++; }
        const string imports = "using System;using System.Collections.Generic;using Sandbox.ModAPI.Ingame;using Sandbox.ModAPI.Interfaces;using VRageMath;";
        Type Compile(string name, string source, string typeName)
        {
            var compilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(imports + source, options) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream(); var result = compilation.Emit(stream);
            if (!result.Success) throw new Exception(string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            return Assembly.Load(stream.ToArray()).GetType(typeName, true);
        }
        string full = File.ReadAllText(Path.Combine(root, "Api/HoloMapApi.cs"));
        var type = Compile("TypedBoundary" + Guid.NewGuid().ToString("N"), full, "HoloMapApi");
        var methods = new Dictionary<string, Delegate>();
        string[] required = { "Version", "PutWires", "PutPolygons", "SetView", "SetTransform", "SetVisible", "Remove", "Clear" };
        string[] fields = { "_version", "_wires", "_polygons", "_view", "_transform", "_visible", "_remove", "_clear" };
        for (int i = 0; i < required.Length; i++)
        {
            var delegateType = type.GetField(fields[i], BindingFlags.NonPublic | BindingFlags.Instance).FieldType;
            var invoke = delegateType.GetMethod("Invoke");
            methods[required[i]] = Expression.Lambda(delegateType, Expression.Default(invoke.ReturnType), invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType))).Compile();
        }
        IReadOnlyDictionary<string, Delegate> current = methods;
        var property = DrawTestProxy.Make(typeof(ITerminalProperty<IReadOnlyDictionary<string, Delegate>>), (m, a) => m.Name == "GetValue" ? current : throw new Exception("Typed property " + m.Name));
        var caller = (PbBlock)DrawTestProxy.Make(typeof(Sandbox.ModAPI.Ingame.IMyProgrammableBlock), (m, a) => m.Name == "GetProperty" && (string)a[0] == "HDR.Api" ? property : throw new Exception("Typed caller " + m.Name));
        var api = Activator.CreateInstance(type);
        bool Activate() => (bool)type.GetMethod("Activate").Invoke(api, new object[] { caller });
        bool Active() => (bool)type.GetProperty("IsActive").GetValue(api);
        methods["Version"] = new Func<string>(() => "0.9.6");
        Check(Activate(), "historical installed product version activates through delegate-compatible fallback");
        foreach (string version in new[] { "0.1.0", "0.8.12", "0.9.7" }) { methods["Version"] = new Func<string>(() => version); Check(Activate(), "historical product " + version); }
        foreach (string version in new[] { "1.0.0", "0.10.0", "0.0.1", "0.9", "0.9.-1", "unknown", null }) { methods["Version"] = new Func<string>(() => version); Check(!Activate() && !Active(), "unadvertised incompatible product rejected " + version); }
        methods["Version"] = new Func<string>(() => "99.0.0");
        methods["ApiVersion"] = new Func<string>(() => "HDR.Api/1");
        Check(Activate() && (string)type.GetProperty("ApiVersion").GetValue(api) == "HDR.Api/1", "stable API protocol is independent of product release");
        foreach (string protocol in new[] { "HDR.Api/2", "HDR.Draw/1", "", null }) { methods["ApiVersion"] = new Func<string>(() => protocol); Check(!Activate() && !Active(), "unknown advertised protocol rejected " + protocol); }
        methods["ApiVersion"] = new Func<int>(() => 1); Check(!Activate(), "wrong advertised protocol delegate cannot use legacy fallback");
        methods["ApiVersion"] = new Func<string>(() => { throw new InvalidOperationException("provider fault"); }); Check(!Activate(), "faulting protocol handshake does not activate");
        methods["ApiVersion"] = new Func<string>(() => "HDR.Api/1");
        foreach (string key in required)
        {
            var saved = methods[key]; methods.Remove(key); Check(!Activate(), "missing required delegate " + key);
            methods[key] = new Action(() => { }); Check(!Activate(), "wrong required delegate shape " + key); methods[key] = saved;
        }
        Check(Activate(), "optional capability delegate may be absent on older providers");
        try { type.GetMethod("Capabilities").Invoke(api, new object[] { caller }); throw new Exception("Absent optional capability delegate accepted."); }
        catch (TargetInvocationException e) { Check(e.InnerException is InvalidOperationException, "missing optional operation reports availability at use"); }
        PbBlock seenCaller = null, seenTarget = null;
        methods["GetDisplayCapabilities"] = new Func<PbBlock, PbBlock, MyTuple<string, string, int>>((c, t) => { seenCaller = c; seenTarget = t; return new MyTuple<string, string, int>("HDR.DisplayCapabilities/1", "lcd", 67); });
        Check(Activate(), "optional capability delegate binds");
        var caps = (MyTuple<string, string, int>)type.GetMethod("Capabilities").Invoke(api, new object[] { caller });
        Check(caps.Item3 == 67 && ReferenceEquals(seenCaller, caller) && ReferenceEquals(seenTarget, caller), "capability helper forwards caller and target without selecting or drawing");
        current = null; Check(!Activate() && !Active(), "null dictionary resets an active helper"); current = methods;

        // Profiles retain the stable handshake but do not require unused drawing families.
        const string header = "using System;using System.Collections.Generic;using Sandbox.ModAPI.Ingame;using Sandbox.ModAPI.Interfaces;using VRageMath;public class Program:MyGridProgram{";
        string body = "HoloMapApi api=new HoloMapApi();public void Main(){api.Activate(Me);api.Capabilities(Me);}";
        string profile = ApiProfiles.Generate(full, body, header, references, options);
        var profileType = Compile("ProfileBoundary" + Guid.NewGuid().ToString("N"), profile, "HoloMapApi");
        Check(profile.Contains("ProtocolSupported") && profile.Contains("\"ApiVersion\"") && !profile.Contains("ref _wires"), "profile retains handshake and strips unused mandatory drawing binds");
        var reduced = Activator.CreateInstance(profileType);
        var oldMethods = methods; methods = new Dictionary<string, Delegate> { ["Version"] = new Func<string>(() => "0.9.6"), ["ApiVersion"] = new Func<string>(() => "HDR.Api/1") }; current = methods;
        Check((bool)profileType.GetMethod("Activate").Invoke(reduced, new object[] { caller }), "capability-only generated profile activates without unused drawing delegates");
        methods = oldMethods; current = methods;

        var shortType = Compile("ShortBoundary" + Guid.NewGuid().ToString("N"), File.ReadAllText(Path.Combine(root, "Api/Ingame/HdrIngameApi.cs")), "HdrIngameApi");
        string shortProtocol = "HDR.Draw/1"; string last = null; object[] lastArgs = null;
        string failure = null; bool unexpectedFailure = false; object response = new MyTuple<string, string, int>("HDR.DisplayCapabilities/1", "projector", 105);
        var shortProperty = DrawTestProxy.Make(typeof(ITerminalProperty<Func<string, object[], object>>), (m, a) => m.Name == "GetValue" ? new Func<string, object[], object>((op, args) =>
        {
            last = op; lastArgs = args;
            if (op == "version") return shortProtocol;
            if (unexpectedFailure) throw new NotSupportedException("unexpected provider fault");
            if (failure != null) throw new ArgumentException(failure);
            return response;
        }) : throw new Exception(m.Name));
        var shortCaller = DrawTestProxy.Make(typeof(Sandbox.ModAPI.Ingame.IMyProgrammableBlock), (m, a) => m.Name == "GetProperty" ? shortProperty : throw new Exception(m.Name));
        var shortApi = Activator.CreateInstance(shortType);
        Check((bool)shortType.GetMethod("Activate").Invoke(shortApi, new[] { shortCaller }), "small HDR.Draw helper compiles and activates");
        caps = (MyTuple<string, string, int>)shortType.GetMethod("Capabilities").Invoke(shortApi, new[] { shortCaller });
        Check(caps.Item3 == 105 && last == "capabilities" && lastArgs.Length == 1 && ReferenceEquals(lastArgs[0], shortCaller), "small helper capability query stays explicit and detached");
        var tryCall = shortType.GetMethod("TryCall");
        object[] tryArguments = { "line", new object[] { "axis" }, null, null };
        Check((bool)tryCall.Invoke(shortApi, tryArguments) && Equals(tryArguments[2], response) && (string)tryArguments[3] == "", "TryCall preserves successful results");
        failure = "Invalid geometry: finite points required.";
        Check(!(bool)tryCall.Invoke(shortApi, tryArguments) && tryArguments[2] == null && (string)tryArguments[3] == failure, "TryCall reports expected validation failure without a PB exception");
        try { shortType.GetMethod("Call").Invoke(shortApi, new object[] { "line", new object[] { "axis" } }); throw new Exception("Call no longer throws validation errors."); }
        catch (TargetInvocationException e) { Check(e.InnerException is ArgumentException && e.InnerException.Message == failure, "Call retains existing validation exception behavior"); }
        failure = "Requires plugin: HDR Client Renderer (raster UI).";
        Check(!(bool)tryCall.Invoke(shortApi, tryArguments) && (string)tryArguments[3] == failure, "TryCall preserves dependency explanation for caller Echo");
        failure = null;
        var tryCaps = shortType.GetMethod("TryCapabilities");
        object[] capArguments = { shortCaller, null, null };
        Check((bool)tryCaps.Invoke(shortApi, capArguments) && ((MyTuple<string, string, int>)capArguments[1]).Item3 == 105, "TryCapabilities returns structural flags");
        response = new MyTuple<string, string, int>("HDR.DisplayCapabilities/2", "projector", 105);
        Check(!(bool)tryCaps.Invoke(shortApi, capArguments) && ((MyTuple<string, string, int>)capArguments[1]).Item3 == 0, "TryCapabilities fails closed on unknown schema");
        response = "invalid response";
        Check(!(bool)tryCaps.Invoke(shortApi, capArguments), "TryCapabilities rejects wrong response shape without throwing");
        var tryStatus = shortType.GetMethod("TryPluginStatus");
        object[] statusArguments = { "native-portal", null, null };
        response = new MyTuple<bool, bool, string>(true, false, "Requires plugin: HDR Client Renderer (native portals).");
        Check((bool)tryStatus.Invoke(shortApi, statusArguments) && !((MyTuple<bool, bool, string>)statusArguments[1]).Item2, "successful local status query does not imply plugin availability");
        Check(last == "plugin-status" && (string)lastArgs[0] == "native-portal", "status helper calls only the detached optional query");
        response = new MyTuple<bool, bool, string>(true, false, "Viewer-dependent capability: native portals.");
        Check((bool)tryStatus.Invoke(shortApi, statusArguments) && ((MyTuple<bool, bool, string>)statusArguments[1]).Item3.StartsWith("Viewer-dependent"), "dedicated-server uncertainty survives the helper");
        response = new MyTuple<bool, bool, string>(false, false, "Unknown plugin feature.");
        Check((bool)tryStatus.Invoke(shortApi, statusArguments) && !((MyTuple<bool, bool, string>)statusArguments[1]).Item1, "unsupported feature is a status result rather than a failed query");
        response = "invalid response"; Check(!(bool)tryStatus.Invoke(shortApi, statusArguments), "optional status rejects wrong response shape without throwing");
        failure = "Unknown drawing command: plugin-status."; Check(!(bool)tryStatus.Invoke(shortApi, statusArguments), "older mod lacking optional status query remains nonthrowing"); failure = null;
        unexpectedFailure = true;
        try { tryCall.Invoke(shortApi, tryArguments); throw new Exception("TryCall swallowed unexpected provider faults."); }
        catch (TargetInvocationException e) { Check(e.InnerException is NotSupportedException, "unexpected faults are not disguised as dependency failures"); }
        unexpectedFailure = false;
        var absentApi = Activator.CreateInstance(shortType);
        Check(!(bool)tryCall.Invoke(absentApi, tryArguments) && (string)tryArguments[3] == "Requires mod: HDR API.", "inactive helper reports missing mod without invoking a provider");
        shortProtocol = "HDR.Draw/2"; Check(!(bool)shortType.GetMethod("Activate").Invoke(shortApi, new[] { shortCaller }) && !(bool)shortType.GetProperty("IsActive").GetValue(shortApi), "small helper rejects unsupported drawing protocol");
        return count;
    }
}
