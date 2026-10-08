using System;
using System.Collections.Generic;
using System.Reflection;
using HDRJavaScriptRuntime;
using Hdr.Mods;
using Sandbox.ModAPI;
using VRage;
using IMyUtilities = VRage.Game.ModAPI.IMyUtilities;

internal static class ServiceTests
{
    private static Action<bool, string> check;

    private sealed class Bus : IDisposable
    {
        private readonly IMyUtilities prior;
        private readonly Dictionary<long, List<Action<object>>> handlers = new Dictionary<long, List<Action<object>>>();
        public Func<string, object[], object> Service;
        public bool Dedicated;
        public int HtmlRequests, ChatHandlers, Unexpected;
        public readonly List<string> Notices = new List<string>();
        public Bus()
        {
            prior = MyAPIGateway.Utilities;
            var utility = DispatchProxy.Create<IMyUtilities, JavaScriptUtilitiesProxy>();
            ((JavaScriptUtilitiesProxy)utility).Handler = Invoke;
            MyAPIGateway.Utilities = utility;
        }
        object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == "get_IsDedicated") return Dedicated;
            if (method.Name == "RegisterMessageHandler")
            {
                long channel = (long)args[0]; List<Action<object>> items;
                if (!handlers.TryGetValue(channel, out items)) handlers[channel] = items = new List<Action<object>>();
                items.Add((Action<object>)args[1]); return null;
            }
            if (method.Name == "UnregisterMessageHandler")
            { List<Action<object>> items; if (handlers.TryGetValue((long)args[0], out items)) items.Remove((Action<object>)args[1]); return null; }
            if (method.Name == "SendModMessage")
            {
                long channel = (long)args[0];
                if (channel == HdrJavaScriptApi.DiscoveryChannel) Service = args[1] as Func<string, object[], object>;
                if (channel == HdrHtmlApi.RequestChannel) HtmlRequests++;
                Broadcast(channel, args[1]); return null;
            }
            if (method.Name == "add_MessageEntered") { ChatHandlers++; return null; }
            if (method.Name == "remove_MessageEntered") { ChatHandlers--; return null; }
            if (method.Name == "ShowMessage") { Notices.Add((string)args[1]); return null; }
            Unexpected++; throw new InvalidOperationException("Unexpected service fixture utility method: " + method.Name);
        }
        public int Count(long channel) { List<Action<object>> items; return handlers.TryGetValue(channel, out items) ? items.Count : 0; }
        public void Broadcast(long channel, object value)
        { List<Action<object>> items; if (handlers.TryGetValue(channel, out items)) foreach (var handler in items.ToArray()) handler(value); }
        public void Dispose() { MyAPIGateway.Utilities = prior; check(Unexpected == 0, "service fixture did not initialize native or world utilities"); }
    }

    private sealed class Document
    {
        public object Token;
        public bool Current = true;
        public long Revision = 1;
        public int Writes, Destroyed;
        public readonly Dictionary<string, string> Text = new Dictionary<string, string>();
        public readonly List<MyTuple<string, string, string, MyTuple<double, long>>> Events = new List<MyTuple<string, string, string, MyTuple<double, long>>>();
        public object Call(string op, object[] args)
        {
            if (op == "valid") return Current;
            if (!Current) throw new InvalidOperationException("actual test HTML endpoint retired");
            if (op == "status") return new MyTuple<string, long, long, MyTuple<bool, bool, string>, long>("vector", Revision, Revision, new MyTuple<bool, bool, string>(true, false, null), 1);
            if (op == "script-claim") { if (Token != null) return false; Token = args[1]; return true; }
            if (op == "script-valid") return object.ReferenceEquals(Token, args[1]);
            if (op == "script-release") { if (!object.ReferenceEquals(Token, args[1])) return false; Token = null; return true; }
            if (op == "poll-events") { var result = Events.ToArray(); Events.Clear(); return result; }
            if (op == "source-capabilities") return new[] { "source-slots" };
            if (op == "source-check") return (string)args[1] == "custom-frame" ? new MyTuple<bool, string>(true, "Ready") : new MyTuple<bool, string>(false, "Requires plugin: HDR Client Renderer");
            if (op == "mutate")
            {
                var operations = (MyTuple<string, object[]>[])args[1];
                foreach (var operation in operations) if (operation.Item1 == "text") Text[(string)operation.Item2[0]] = (string)operation.Item2[1];
                if (operations.Length > 0) { Writes++; Revision++; }
                return true;
            }
            if (op == "destroy") { Destroyed++; return null; }
            throw new InvalidOperationException("Unexpected document fixture operation: " + op);
        }
        public Func<string, object[], object> Alias() { return (op, args) => Call(op, args); }
        public void Click()
        { Events.Add(new MyTuple<string, string, string, MyTuple<double, long>>("click", "next", "next", new MyTuple<double, long>(1, Revision))); }
    }

    private static void Unload(JavaScriptRuntimeSession session)
    { typeof(JavaScriptRuntimeSession).GetMethod("UnloadData", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(session, null); }
    private static MyTuple<string, object[]> Setting(string name, object value)
    { return new MyTuple<string, object[]>(name, new[] { value }); }
    private static object Limit(MyTuple<string, object[]>[] values, string name)
    { foreach (var item in values) if (item.Item1 == name) return item.Item2[0]; throw new InvalidOperationException("Missing quota field: " + name); }
    private static void Reject(Action operation, string reason)
    {
        bool rejected = false; string actual = "no exception";
        try { operation(); } catch (Exception error) { actual = error.Message; rejected = actual.IndexOf(reason, StringComparison.OrdinalIgnoreCase) >= 0; }
        check(rejected, "expected service boundary rejection " + reason + "; actual: " + actual);
    }

    public static void Run(Action<bool, string> assert, Action<string, Action> runCase)
    {
        check = assert;
        runCase("actual mod service discovery and opt-in standalone execution", Discovery);
        runCase("actual mod service configurable limits and scalar boundaries", Configuration);
        runCase("actual mod service HTML claims timers errors and cleanup", BoundDocuments);
        runCase("actual mod service generations dedicated guard and absence", Generations);
    }

    private static void Discovery()
    {
        using (var bus = new Bus())
        {
            var session = new JavaScriptRuntimeSession(); session.BeforeStart(); session.BeforeStart();
            check(bus.Service != null && (string)bus.Service("version", new object[0]) == HdrJavaScriptApi.Protocol, "actual mod announces exact scalar/delegate ABI");
            check(bus.Count(HdrJavaScriptApi.RequestChannel) == 1 && bus.ChatHandlers == 1, "startup registers exactly one request and chat handler");
            check(bus.HtmlRequests == 0 && bus.Notices.Count == 0, "loading runtime never creates UI or executes demo");
            using (var api = new HdrJavaScriptApi("js.service.cpu"))
            {
                check(api.Ready && api.ConnectionGeneration == 1, "actual public JS SDK acquires opt-in endpoint");
                api.Configure(new[] { Setting("timeout-ms", 1000.0) });
                long realm = api.CreateRealm(); check(api.Status(realm).Item1, "standalone realm needs no HTML or plugin");
                check(api.Execute(realm, "var state=2;function add(x){state+=x;return state;}"), "explicit execute evaluates real JavaScript");
                check((double)api.CallFunction(realm, "add", 40.0) == 42, "named scalar call retains real realm state");
                check(bus.HtmlRequests == 0, "standalone service never requests renderer/frontend");
                api.DisposeRealm(realm); Reject(() => api.Status(realm), "does not belong");
                Reject(() => api.CreateHud("<p id='status'>A</p>", "", 100, 100, new[] { "status" }), "Requires mod: HDR HTML Frontend");
                check(api.Ready, "missing optional HTML does not retire JS owner");
            }
            Unload(session);
            check(bus.Count(HdrJavaScriptApi.RequestChannel) == 0 && bus.ChatHandlers == 0, "unload revokes discovery request and chat handlers");
        }
    }

    private static void Configuration()
    {
        using (var bus = new Bus())
        {
            var session = new JavaScriptRuntimeSession(); session.BeforeStart();
            using (var api = new HdrJavaScriptApi("js.limits.cpu"))
            {
                api.Configure(new[] { Setting("timeout-ms", 1000.0), Setting("statements", 200), Setting("max-realms", 1) });
                long realm = api.CreateRealm();
                check((int)Limit(api.RealmLimits(realm), "statements") == 200, "realm snapshots selected limits");
                Reject(() => api.Configure(new[] { Setting("statements", 999), Setting("recursion", -1) }), "Invalid JavaScript limits");
                check((int)Limit(api.Limits(), "statements") == 200, "failed configuration rolls back all earlier entries");
                Reject(() => api.Configure(new[] { Setting("statements", 200L) }), "Int32");
                Reject(() => api.Configure(new[] { Setting("unknown", 1) }), "Unknown JavaScript limit");
                Reject(() => api.Configure(new[] { Setting("statements", 1), Setting("statements", 2) }), "distinct");
                Reject(() => api.CreateRealm(), "realm limit");
                api.Configure(new[] { Setting("statements", 500), Setting("max-realms", 0) });
                check((int)Limit(api.RealmLimits(realm), "statements") == 200 && (int)Limit(api.Limits(), "statements") == 500, "new defaults do not rewrite existing realm limits");
                long second = api.CreateRealm();
                check((int)Limit(api.RealmLimits(second), "statements") == 500, "subsequent realm uses updated default");
                api.Execute(second, "function value(x){return x;} function structured(){return {x:1};}");
                Reject(() => api.CallFunction(second, "value", new object()), "values must");
                Reject(() => api.CallFunction(second, "value", double.NaN), "finite");
                Reject(() => api.CallFunction(second, "structured"), "objects and arrays");
                api.ClearOwned(); Reject(() => api.Status(realm), "does not belong"); Reject(() => api.Status(second), "does not belong");
                check(api.CreateRealm() > second, "clear-owned frees capacity without reusing stale handles");
            }
            Unload(session);
        }
    }

    private static void BoundDocuments()
    {
        using (var bus = new Bus())
        {
            var session = new JavaScriptRuntimeSession(); session.BeforeStart();
            using (var api = new HdrJavaScriptApi("js.document.cpu"))
            {
                api.Configure(new[] { Setting("timeout-ms", 1000.0) });
                var doc = new Document();
                long realm = api.BindHtml(doc.Alias(), 5, 1, () => doc.Current, new[] { "status" });
                check(doc.Token != null && api.Status(realm).Item4.Item3, "binding claims actual caller-owned HTML capability");
                Reject(() => api.BindHtml(doc.Alias(), 5, 1, () => doc.Current, new[] { "status" }), "another active JavaScript");
                check(api.Execute(realm, "var n=0;hdr.setText('status','ready');hdr.on('click','next',function(){n++;hdr.setText('status','Count '+n);});hdr.setTimeout(function(){hdr.setText('status','delayed');},100);"), "service installs natural JS closures on declared document");
                check(doc.Text["status"] == "ready", "initial script uses atomic HTML mutate");
                doc.Click(); session.UpdateAfterSimulation();
                check(doc.Text["status"] == "Count 1", "published-frame event invokes JS closure through actual mod service");
                for (int i = 0; i < 6; i++) session.UpdateAfterSimulation();
                check(doc.Text["status"] == "delayed", "client update clock drives registered JS timeout");
                api.AddSourceChoice(realm, "camera", "pov", "camera-panorama", "camera-id", () => new MyTuple<bool, string>(false, "Requires plugin: HDR Client Renderer"));
                check(api.Execute(realm, "hdr.log('hello');hdr.selectSource('camera');"), "successful optional feature rejection does not fault realm");
                var status = api.Status(realm); var messages = api.Diagnostics(realm);
                check(status.Item1 && status.Item2 == null, "host logs stay separate from execution failure status");
                check(Array.Exists(messages, x => x == "hello") && Array.Exists(messages, x => x.StartsWith("Requires plugin:")), "bounded diagnostic queue preserves log and precise optional plugin reason");
                check(api.Diagnostics(realm).Length == 0, "diagnostic drain is explicit and exact");
                api.AddSourceChoice(realm, "nested", "pov", "custom-frame", "owned-id", () => { api.ClearOwned(); return new MyTuple<bool, string>(true, null); });
                check(api.Execute(realm, "hdr.selectSource('nested');"), "reentrant source probe becomes safe rejected optional operation");
                check(api.Status(realm).Item1 && doc.Text["status"] == "delayed", "nested service mutation cannot alter owners or published state");
                check(Array.Exists(api.Diagnostics(realm), value => value.Contains("nested service calls")), "busy guard exposes explicit reentrancy reason");
                check(!api.Execute(realm, "hdr.setText('status','bad');throw new Error('fault');"), "bound script error returns safe false");
                check(doc.Text["status"] == "delayed" && !api.Status(realm).Item1, "script error preserves prior content and retires callbacks");
                check(doc.Token == null && doc.Destroyed == 0, "script failure releases only JS claim on externally owned HTML");
                api.DisposeRealm(realm); check(doc.Destroyed == 0, "explicit JS cleanup never destroys external HTML");

                realm = api.BindHtml(doc.Alias(), 5, 1, () => doc.Current, new[] { "status" });
                check(api.Execute(realm, "hdr.setTimeout(function(){hdr.setText('status','late');},1);"), "retirement fixture schedules timer");
                doc.Current = false; session.UpdateAfterSimulation();
                check(!api.Status(realm).Item1 && doc.Text["status"] == "delayed", "actual owner witness retirement cancels timers before writes");

                var admission = new Document();
                api.Configure(new[] { Setting("object-properties", 1) });
                Reject(() => api.BindHtml(admission.Alias(), 6, 1, () => admission.Current, new[] { "status" }), "property");
                check(admission.Token == null && admission.Writes == 0, "failed bridge construction releases already-acquired actual HTML claim");
                api.Configure(new[] { Setting("object-properties", 2048) });
                long rebound = api.BindHtml(admission.Alias(), 6, 1, () => admission.Current, new[] { "status" });
                check(api.Status(rebound).Item1 && admission.Token != null, "same actual document can bind after failed admission cleanup");
                Reject(() => api.Pointer(rebound, 10, 10, true), "externally bound");
                api.DisposeRealm(rebound); check(admission.Token == null, "explicit external binding disposal releases claim");
            }
            Unload(session);
        }
    }

    private static void Generations()
    {
        using (var bus = new Bus())
        {
            var session = new JavaScriptRuntimeSession(); session.BeforeStart();
            using (var first = new HdrJavaScriptApi("js.generation.cpu"))
            {
                long stale = first.CreateRealm();
                using (var successor = new HdrJavaScriptApi("js.generation.cpu"))
                {
                    check(!first.Ready && successor.Ready, "reopening same owner retires previous endpoint generation");
                    Reject(() => successor.Status(stale), "does not belong");
                    Unload(session); check(!successor.Ready, "session unload revokes all owner endpoints");
                }
            }
            var replacement = new JavaScriptRuntimeSession(); replacement.BeforeStart();
            using (var api = new HdrJavaScriptApi("js.after-unload.cpu")) check(api.Ready && api.CreateRealm() > 0, "replacement session admits fresh realms after clean unload");
            Unload(replacement);
            bus.Service = null; bus.Dedicated = true; new JavaScriptRuntimeSession().BeforeStart();
            check(bus.Service == null && bus.ChatHandlers == 0, "dedicated server never advertises client UI JS service");
            using (var absent = new HdrJavaScriptApi("js.absent.cpu"))
            { check(!absent.Ready, "missing local runtime remains unavailable"); Reject(() => absent.CreateRealm(), "Requires mod: HDR JavaScript Runtime"); }
            Reject(() => new HdrJavaScriptApi("invalid owner"), "owner");
        }
    }
}
