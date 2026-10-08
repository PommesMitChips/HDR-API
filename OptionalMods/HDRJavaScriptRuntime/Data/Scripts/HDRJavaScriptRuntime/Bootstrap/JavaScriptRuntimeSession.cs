using System;
using System.Collections.Generic;
using Hdr.Mods;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Components;

namespace HDRJavaScriptRuntime
{
    /// <summary>Opt-in client-local interpreter service. Loading this mod never executes a document or acquires game input.</summary>
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed partial class JavaScriptRuntimeSession : MySessionComponentBase
    {
        public const string Protocol = "HDR.JS/0.1";
        public const long DiscoveryChannel = 481770160, RequestChannel = 481770161;
        readonly Dictionary<string, Owner> owners = new Dictionary<string, Owner>(StringComparer.Ordinal);
        readonly Dictionary<long, Realm> documentBindings = new Dictionary<long, Realm>();
        Func<string, object[], object> service;
        bool active, busy;
        long nextGeneration, nextRealm;
        double now;

        sealed class Owner
        {
            public string Id;
            public long Generation;
            public bool Released;
            public JavaScriptLimits Limits = new JavaScriptLimits();
            public JavaScriptHostLimits HostLimits = new JavaScriptHostLimits();
            public int MaxRealms = 16;
            public HdrHtmlApi Html;
            public readonly Dictionary<long, Realm> Realms = new Dictionary<long, Realm>();
        }
        sealed class Realm
        {
            public long Handle, Document, HtmlGeneration;
            public Owner Owner;
            public JavaScriptRealm Interpreter;
            public JavaScriptHostLimits HostLimits;
            public HdrHtmlDocumentHost Host;
            public JavaScriptDocumentBridge Bridge;
            public bool Registered, OwnsDocument, Released, OwnedDocumentReleased;
            public string LastError;
            public readonly Queue<string> Diagnostics = new Queue<string>();
            public bool Current
            {
                get
                {
                    if (Released || Owner.Released || Interpreter == null || !Interpreter.Current) return false;
                    if (Host == null) return true;
                    return Host.Current && (!Registered || Bridge.Active);
                }
            }
        }

        public override void BeforeStart()
        {
            if (active || MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            active = true; service = Service;
            MyAPIGateway.Utilities.RegisterMessageHandler(RequestChannel, ReceiveRequest);
            MyAPIGateway.Utilities.MessageEntered += DemoChat;
            try { MyAPIGateway.Utilities.SendModMessage(DiscoveryChannel, service); } catch { }
        }
        void ReceiveRequest(object value)
        {
            var callback = value as Action<Func<string, object[], object>>;
            if (!active || callback == null) return;
            try { callback(service); } catch { }
        }
        public override void UpdateAfterSimulation()
        {
            if (!active || busy) return;
            now += 1000.0 / 60.0; busy = true;
            try
            {
                // Commands and callbacks cannot mutate these collections while the service updates.
                foreach (var owner in owners.Values)
                {
                    if (owner.Released) continue;
                    foreach (var realm in owner.Realms.Values)
                    {
                        if (realm.Released) continue;
                        try
                        {
                            if (!realm.Current)
                            {
                                if (realm.LastError == null)
                                {
                                    realm.LastError = realm.Bridge == null ? "JavaScript interpreter retired." :
                                        realm.Bridge.LastError ?? "HTML owner/document generation or script association retired; JavaScript callbacks and timers cancelled.";
                                    RecordDiagnostic(realm, realm.LastError);
                                }
                                RetireRealm(realm, false); continue;
                            }
                            if (realm.Bridge != null && realm.Registered)
                            {
                                realm.Bridge.Update(now); realm.LastError = realm.Bridge.LastError;
                                if (!realm.Current) RetireRealm(realm, false);
                            }
                        }
                        catch (Exception error) { realm.LastError = error.Message; RetireRealm(realm, false); }
                    }
                }
            }
            finally { busy = false; }
        }
        protected override void UnloadData()
        {
            active = false; service = null;
            if (MyAPIGateway.Utilities != null)
            {
                try { MyAPIGateway.Utilities.UnregisterMessageHandler(RequestChannel, ReceiveRequest); } catch { }
                try { MyAPIGateway.Utilities.MessageEntered -= DemoChat; } catch { }
            }
            foreach (var owner in owners.Values) ReleaseOwner(owner);
            owners.Clear(); documentBindings.Clear(); ClearDemo();
        }
        bool Current(Owner owner)
        {
            Owner current;
            return active && !owner.Released && owners.TryGetValue(owner.Id, out current) &&
                ReferenceEquals(current, owner) && current.Generation == owner.Generation;
        }
        void RequireIdle()
        {
            if (busy) throw new InvalidOperationException("Call HDR JavaScript on the client simulation thread outside its update or host callback; nested service calls are unsupported.");
        }
        void RetireRealm(Realm realm, bool destroyOwnedDocument = true)
        {
            if (!realm.Released)
            {
                realm.Released = true;
                if (realm.Document != 0)
                {
                    Realm binding;
                    if (documentBindings.TryGetValue(realm.Document, out binding) && ReferenceEquals(binding, realm))
                        documentBindings.Remove(realm.Document);
                }
                if (realm.Bridge != null)
                {
                    try { realm.Bridge.Dispose(); } catch { }
                    if (realm.LastError == null) realm.LastError = realm.Bridge.LastError;
                }
                else
                {
                    if (realm.Interpreter != null) try { realm.Interpreter.Dispose(); } catch { }
                    // A host constructor can fail after its document claim was admitted.
                    if (realm.Host != null) try { realm.Host.Dispose(); } catch { }
                }
            }
            // A script exception preserves the owned HUD's last committed frame for inspection.
            // Explicit realm/owner cleanup still removes it, even when the interpreter was already retired.
            if (destroyOwnedDocument && realm.OwnsDocument && !realm.OwnedDocumentReleased)
            {
                realm.OwnedDocumentReleased = true;
                if (realm.Owner.Html != null && realm.Owner.Html.Ready && realm.Owner.Html.ConnectionGeneration == realm.HtmlGeneration)
                    try { realm.Owner.Html.Destroy(realm.Document); } catch { }
            }
        }
        void ReleaseOwner(Owner owner)
        {
            if (owner.Released) return;
            owner.Released = true;
            foreach (var realm in owner.Realms.Values) RetireRealm(realm);
            owner.Realms.Clear();
            if (owner.Html != null) try { owner.Html.Dispose(); } catch { }
            owner.Html = null;
        }
        static void Notice(string message)
        { try { MyAPIGateway.Utilities.ShowMessage("HDR JavaScript", message); } catch { } }
        static void RecordDiagnostic(Realm realm, string message)
        {
            if (message == null) return;
            if (message.Length > realm.HostLimits.TextCharacters) message = message.Substring(0, realm.HostLimits.TextCharacters);
            while (realm.Diagnostics.Count >= realm.HostLimits.PendingEvents) realm.Diagnostics.Dequeue();
            realm.Diagnostics.Enqueue(message); Notice(message);
        }
    }
}
