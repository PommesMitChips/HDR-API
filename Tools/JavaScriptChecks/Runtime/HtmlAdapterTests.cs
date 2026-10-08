using System;
using System.Collections.Generic;
using System.Reflection;
using HDRJavaScriptRuntime;
using Hdr.Mods;
using Sandbox.ModAPI;
using VRage;
using IMyUtilities = VRage.Game.ModAPI.IMyUtilities;

// Reflection is used only by the CPU interface double, never by the mod source.
public class JavaScriptUtilitiesProxy : DispatchProxy
{
    public Func<MethodInfo, object[], object> Handler;
    protected override object Invoke(MethodInfo method, object[] args) { return Handler(method, args); }
}

internal static class HtmlAdapterTests
{
    private static Action<bool, string> check;

    private sealed class Bus : IDisposable
    {
        private readonly IMyUtilities prior;
        private readonly List<Action<object>> receivers = new List<Action<object>>();
        public Func<string, object[], object> Service;
        public int Unexpected;
        public Bus()
        {
            prior = MyAPIGateway.Utilities;
            var utilities = DispatchProxy.Create<IMyUtilities, JavaScriptUtilitiesProxy>();
            ((JavaScriptUtilitiesProxy)utilities).Handler = Invoke;
            MyAPIGateway.Utilities = utilities;
        }
        object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == "RegisterMessageHandler") { receivers.Add((Action<object>)args[1]); return null; }
            if (method.Name == "UnregisterMessageHandler") { receivers.Remove((Action<object>)args[1]); return null; }
            if (method.Name == "SendModMessage" && (long)args[0] == HdrHtmlApi.RequestChannel)
            { var reply = args[1] as Action<Func<string, object[], object>>; if (reply != null && Service != null) reply(Service); return null; }
            Unexpected++; throw new InvalidOperationException("Unexpected offline utility operation: " + method.Name);
        }
        public void Replace(Func<string, object[], object> service)
        { Service = service; foreach (var receiver in receivers.ToArray()) receiver(service); }
        public void Dispose() { MyAPIGateway.Utilities = prior; check(Unexpected == 0, "SDK uses only declared message boundary in CPU fixture"); }
    }

    private sealed class Endpoint
    {
        public string[] SourceCapabilities = new[] { "composition-source-slots" };
        public int Mutations, Releases, DestroyCalls;
        private object claim;
        public MyTuple<string, object[]>[] LastBatch;
        public readonly Dictionary<string, string> Text = new Dictionary<string, string>();
        public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
        public object Call(string command, object[] args)
        {
            if (command == "valid") return true;
            if (command == "status") return new MyTuple<string, long, long, MyTuple<bool, bool, string>, long>("vector", 7, 7, new MyTuple<bool, bool, string>(true, false, null), 1);
            if (command == "poll-events") return new MyTuple<string, string, string, MyTuple<double, long>>[0];
            if (command == "source-capabilities") return SourceCapabilities;
            if (command == "source-check")
            {
                bool nativeUnavailable = (string)args[1] == "camera-panorama" && Array.Exists(SourceCapabilities, value => value.StartsWith("RequiresPlugin:"));
                return new MyTuple<bool, string>(!nativeUnavailable, nativeUnavailable ? "Requires plugin: HDR Client Renderer" : "Ready");
            }
            if (command == "script-claim") { if (claim != null) return false; claim = args[1]; return true; }
            if (command == "script-valid") return object.ReferenceEquals(claim, args[1]);
            if (command == "script-release") { if (!object.ReferenceEquals(claim, args[1])) return false; claim = null; return true; }
            if (command == "mutate")
            {
                check((long)args[0] == 5, "actual SDK retains owned document handle");
                LastBatch = (MyTuple<string, object[]>[])args[1]; Mutations++;
                foreach (var operation in LastBatch)
                {
                    if (operation.Item1 == "text") Text[(string)operation.Item2[0]] = (string)operation.Item2[1];
                    else if (operation.Item1 == "data") Data[(string)operation.Item2[0]] = (string)operation.Item2[1];
                    else check(operation.Item1 == "attach-source", "actual adapter resolves choice into generic source operation");
                }
                return true;
            }
            if (command == "release") { Releases++; return null; }
            if (command == "destroy") { DestroyCalls++; return null; }
            throw new InvalidOperationException("Unexpected fake HTML endpoint command: " + command);
        }
        public Func<string, object[], object> Service()
        { return (command, args) => command == "version" ? (object)"HDR.Html/0.1" : command == "open" ? (object)new Func<string, object[], object>(Call) : null; }
    }

    public static void Run(Action<bool, string> assert, Action<string, Action> runCase)
    {
        check = assert;
        runCase("real HTML SDK adapter atomic mutation and source guard", AtomicAdapter);
        runCase("real HTML SDK adapter generation and document preservation", Generation);
    }

    private static void AtomicAdapter()
    {
        using (var bus = new Bus())
        {
            var endpoint = new Endpoint(); bus.Service = endpoint.Service();
            using (var api = new HdrHtmlApi("js.cpu.fixture"))
            {
                check(api.Ready && api.ConnectionGeneration == 1, "actual public HTML helper acquires legitimate service endpoint");
                var host = new HdrHtmlDocumentHost(api, 5, new[] { "status" }, new[] { "count" });
                bool duplicateRejected = false;
                try { new HdrHtmlDocumentHost(api, 5, new[] { "status" }); }
                catch (ArgumentException error) { duplicateRejected = error.Message.Contains("another active"); }
                check(duplicateRejected && host.Current, "same real document cannot be bound by a second alias realm");
                host.AddSourceChoice("forward", "pov", "camera-panorama", "real-camera-123", () => new MyTuple<bool, string>(true, null));
                var realm = new JavaScriptRealm(new JavaScriptLimits { TimeoutMilliseconds = 1000 });
                var bridge = new JavaScriptDocumentBridge(realm, host);
                check(bridge.RegisterScript("hdr.setText('status','ready');hdr.setData('count',2);hdr.selectSource('forward');"), "real adapter publishes one complete JS batch");
                check(endpoint.Mutations == 1 && endpoint.LastBatch.Length == 3, "one atomic mutate call for text data and source");
                check(endpoint.Text["status"] == "ready" && endpoint.Data["count"] == "2", "actual adapter preserves primitive text/data");
                var source = endpoint.LastBatch[2];
                check(source.Item1 == "attach-source" && (string)source.Item2[0] == "pov" && (string)source.Item2[1] == "camera-panorama" && (string)source.Item2[2] == "real-camera-123", "source identity comes only from C# declared choice");
                bridge.Dispose();
                check(endpoint.DestroyCalls == 0 && api.Ready, "JS association disposal leaves consumer-owned HTML document and API intact");

                endpoint.SourceCapabilities = new[] { "RequiresPlugin: HDR Client Renderer" };
                host = new HdrHtmlDocumentHost(api, 5, new[] { "status" }, new[] { "count" });
                host.AddSourceChoice("forward", "pov", "camera-panorama", "real-camera-123", () => new MyTuple<bool, string>(true, null));
                var logs = new List<string>();
                bridge = new JavaScriptDocumentBridge(new JavaScriptRealm(new JavaScriptLimits { TimeoutMilliseconds = 1000 }), host, null, logs.Add);
                int before = endpoint.Mutations;
                check(bridge.RegisterScript("hdr.selectSource('forward');"), "absent plugin is a safe rejected optional operation");
                check(bridge.Active && endpoint.Mutations == before, "source capability guard happens before any native declaration/mutation");
                check(logs.Count == 1 && logs[0].StartsWith("Requires plugin:"), "actual adapter prints explicit plugin requirement");
                bridge.Dispose();

                host = new HdrHtmlDocumentHost(api, 5, new[] { "status" });
                host.AddSourceChoice("custom", "pov", "custom-frame", "owned-source", () => new MyTuple<bool, string>(true, null));
                bridge = new JavaScriptDocumentBridge(new JavaScriptRealm(new JavaScriptLimits { TimeoutMilliseconds = 1000 }), host);
                check(bridge.RegisterScript("hdr.selectSource('custom');"), "custom source remains usable without native plugin");
                check(bridge.Active && endpoint.Mutations == before + 1 && endpoint.LastBatch.Length == 1 && (string)endpoint.LastBatch[0].Item2[1] == "custom-frame", "exact-provider capability admits mod-native source despite unrelated native plugin absence");
                bridge.Dispose();
            }
            check(endpoint.Releases == 1 && endpoint.DestroyCalls == 0, "consumer helper release preserves document lifecycle ownership");
        }
    }

    private static void Generation()
    {
        using (var bus = new Bus())
        {
            var first = new Endpoint(); bus.Service = first.Service();
            using (var api = new HdrHtmlApi("js.cpu.generation"))
            {
                var host = new HdrHtmlDocumentHost(api, 5, new[] { "status" });
                var bridge = new JavaScriptDocumentBridge(new JavaScriptRealm(new JavaScriptLimits { TimeoutMilliseconds = 1000 }), host);
                check(bridge.RegisterScript("hdr.setText('status','published');hdr.setTimeout(function(){hdr.setText('status','late');},1);"), "real adapter initially publishes and schedules timer");
                var next = new Endpoint(); bus.Replace(next.Service());
                check(api.Ready && api.ConnectionGeneration == 2 && first.Releases == 1, "actual helper replacement fences its old generation");
                bridge.Update(1);
                check(!bridge.Active && next.Mutations == 0 && first.Text["status"] == "published", "old timer cannot write successor endpoint");
                check(first.DestroyCalls == 0 && next.DestroyCalls == 0, "retirement preserves prior and successor HTML documents");
            }
        }
    }
}
