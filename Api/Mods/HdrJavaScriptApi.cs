using System;
using Sandbox.ModAPI;
using VRage;

namespace Hdr.Mods
{
    /// <summary>Copy into a consumer mod. Client-local source-vendored JavaScript interpreter; document bindings are explicit capabilities.</summary>
    public sealed class HdrJavaScriptApi : IDisposable
    {
        public const long DiscoveryChannel = 481770160, RequestChannel = 481770161;
        public const string Protocol = "HDR.JS/0.1";
        readonly string owner;
        Func<string, object[], object> service, endpoint;
        bool disposed;
        long acquisition;
        public long ConnectionGeneration { get; private set; }
        public HdrJavaScriptApi(string uniqueOwnerId)
        {
            if (string.IsNullOrEmpty(uniqueOwnerId) || uniqueOwnerId.Length > 40)
                throw new ArgumentException("JavaScript owner ID requires 1..40 characters.");
            foreach (char c in uniqueOwnerId)
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
                    throw new ArgumentException("JavaScript owner ID requires letters, digits, period, underscore or hyphen.");
            owner = uniqueOwnerId;
            MyAPIGateway.Utilities.RegisterMessageHandler(DiscoveryChannel, Receive); Request();
        }
        public bool Ready
        {
            get
            {
                if (disposed || endpoint == null) return false;
                var prior = endpoint; long generation = ConnectionGeneration;
                try
                {
                    bool valid = (bool)prior("valid", new object[0]);
                    if (disposed || !ReferenceEquals(prior, endpoint) || generation != ConnectionGeneration) return false;
                    if (valid) return true; endpoint = null;
                }
                catch { if (ReferenceEquals(prior, endpoint) && generation == ConnectionGeneration) endpoint = null; }
                return false;
            }
        }
        void Receive(object value)
        {
            var candidate = value as Func<string, object[], object>;
            if (disposed || candidate == null || ReferenceEquals(candidate, service) && Ready) return;
            long ticket = ++acquisition;
            try
            {
                if ((string)candidate("version", new object[0]) != Protocol) return;
                var acquired = candidate("open", new object[] { owner }) as Func<string, object[], object>;
                if (acquired == null) return;
                if (disposed || ticket != acquisition) { try { acquired("release", new object[0]); } catch { } return; }
                ReleaseEndpoint();
                if (disposed || ticket != acquisition) { try { acquired("release", new object[0]); } catch { } return; }
                service = candidate; endpoint = acquired; ConnectionGeneration++;
            }
            catch { }
        }
        public void Request()
        {
            if (!disposed) try { MyAPIGateway.Utilities.SendModMessage(RequestChannel,
                new Action<Func<string, object[], object>>(value => Receive(value))); } catch { }
        }
        public object Call(string command, params object[] args)
        {
            if (!Ready) throw new InvalidOperationException("Requires mod: HDR JavaScript Runtime (client service unavailable).");
            var prior = endpoint; long generation = ConnectionGeneration;
            object result = prior(command, args);
            if (disposed || !ReferenceEquals(prior, endpoint) || generation != ConnectionGeneration)
                throw new InvalidOperationException("HDR JavaScript endpoint changed during the command; rebuild owned realms.");
            if (command == "release") endpoint = null;
            return result;
        }
        public bool TryCall(string command, out object result, out string reason, params object[] args)
        { result = null; reason = null; try { result = Call(command, args); return true; } catch (Exception error) { reason = error.Message; return false; } }
        public string[] Capabilities()
        {
            if (!Ready) return new string[0]; var before = endpoint; long generation = ConnectionGeneration;
            var result = (string[])service("capabilities", new object[0]);
            if (disposed || !ReferenceEquals(before, endpoint) || generation != ConnectionGeneration)
                throw new InvalidOperationException("HDR JavaScript endpoint changed during the capability query.");
            return result;
        }
        /// <summary>Exact named limits affect subsequently created realms. Int32 counts, Int64 allocation units, Double milliseconds; zero disables the corresponding supported quotas.</summary>
        public void Configure(MyTuple<string, object[]>[] settings) { Call("configure", settings); }
        public MyTuple<string, object[]>[] Limits() { return (MyTuple<string, object[]>[])Call("limits"); }
        public MyTuple<string, object[]>[] RealmLimits(long realm) { return (MyTuple<string, object[]>[])Call("realm-limits", realm); }
        public long CreateRealm() { return (long)Call("create"); }
        /// <summary>Bind one already-owned HTML document. The endpoint and current-generation witness stay in C#; JS receives neither.</summary>
        public long BindHtml(Func<string, object[], object> ownedHtmlEndpoint, long ownedDocument, long htmlGeneration,
            Func<bool> actualOwnerCurrent, string[] allowedTextNodes, string[] allowedDataKeys = null)
        { return (long)Call("bind-html", ownedHtmlEndpoint, ownedDocument, htmlGeneration, actualOwnerCurrent,
            allowedTextNodes ?? new string[0], allowedDataKeys ?? new string[0]); }
        /// <summary>Optional HUD convenience. The runtime owns this HTML document and removes it when its realm is disposed.</summary>
        public long CreateHud(string markup, string css, double width, double height, string[] allowedTextNodes,
            string[] allowedDataKeys = null, string backend = "vector")
        { return (long)Call("create-hud", markup, css, width, height, allowedTextNodes ?? new string[0], allowedDataKeys ?? new string[0], backend); }
        /// <summary>Only for a runtime-owned HUD from CreateHud, using logical document pixels. Call from a consumer GUI that already owns/blocks the press; no native input is acquired.</summary>
        public void Pointer(long realm, double x, double y, bool pressed) { Call("pointer", realm, x, y, pressed); }
        /// <summary>Matching cleanup also works for a stopped realm's still-owned HUD. External BindHtml input remains with the caller's HTML API.</summary>
        public void CancelPointer(long realm) { Call("pointer-cancel", realm); }
        public bool Execute(long realm, string source) { return (bool)Call("execute", realm, source); }
        public object CallFunction(long realm, string name, params object[] scalarArguments)
        { return Call("call", realm, name, scalarArguments ?? new object[0]); }
        /// <summary>Current, last error, language profile, (owner generation, HTML generation, HTML bound), owned HTML handle.</summary>
        public MyTuple<bool, string, string, MyTuple<long, long, bool>, long> Status(long realm)
        { return (MyTuple<bool, string, string, MyTuple<long, long, bool>, long>)Call("status", realm); }
        /// <summary>Bounded host logs and capability reasons, separate from execution failures. Available after a realm stops; optionally drains the queue.</summary>
        public string[] Diagnostics(long realm, bool clear = true) { return (string[])Call("diagnostics", realm, clear); }
        /// <summary>Only the C# owner declares source choices, and must probe the actual provider's optional plugin and local-consumer capability.</summary>
        public void AddSourceChoice(long realm, string choice, string nodeId, string provider, string sourceId,
            Func<MyTuple<bool, string>> actualCapabilityProbe, MyTuple<string, object[]>[] settings = null)
        { Call("source-choice", realm, choice, nodeId, provider, sourceId, actualCapabilityProbe,
            settings ?? new MyTuple<string, object[]>[0]); }
        public void DisposeRealm(long realm) { Call("dispose", realm); }
        public void ClearOwned() { Call("clear-owned"); }
        void ReleaseEndpoint()
        { var old = endpoint; endpoint = null; if (old != null) try { old("release", new object[0]); } catch { } }
        public void Dispose()
        {
            if (disposed) return; disposed = true; acquisition++;
            if (MyAPIGateway.Utilities != null) try { MyAPIGateway.Utilities.UnregisterMessageHandler(DiscoveryChannel, Receive); } catch { }
            ReleaseEndpoint(); service = null;
        }
    }
}
