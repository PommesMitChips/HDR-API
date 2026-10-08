using System;
using System.Collections.Generic;
using Hdr.Mods;
using VRage;

namespace HDRJavaScriptRuntime
{
    /// <summary>Wraps one handle on an HTML endpoint already owned by the C# consumer. Neither endpoint nor handle crosses into JS.</summary>
    public sealed class HdrHtmlDocumentHost : IJavaScriptDocumentHost
    {
        sealed class SourceChoice
        {
            public string Node, Provider, Source;
            public MyTuple<string, object[]>[] Settings;
            public Func<MyTuple<bool, string>> Capability;
        }
        readonly Func<string, object[], object> endpoint;
        readonly Func<bool> ownerCurrent;
        readonly object scriptToken = new object();
        readonly long handle, generation;
        readonly HashSet<string> textNodes = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> dataKeys = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, SourceChoice> sources = new Dictionary<string, SourceChoice>(StringComparer.Ordinal);
        bool disposed, claimed;

        public long Generation { get { return generation; } }
        public bool Current
        {
            get
            {
                if (disposed) return false;
                try
                {
                    if (!ownerCurrent() || !(bool)Call("valid")) return false;
                    EndpointStatus();
                    return !disposed && ownerCurrent() && (!claimed || (bool)Call("script-valid", handle, scriptToken));
                }
                catch { return false; }
            }
        }

        public HdrHtmlDocumentHost(HdrHtmlApi api, long ownedDocument, string[] allowedTextNodes, string[] allowedDataKeys = null)
            : this(OwnedEndpoint(api), ownedDocument, api.ConnectionGeneration, CaptureCurrent(api), allowedTextNodes, allowedDataKeys)
        { }

        /// <summary>The function is the exact caller-owned HTML endpoint capability. The generation witness must track that endpoint's actual retirement.</summary>
        public HdrHtmlDocumentHost(Func<string, object[], object> ownedHtmlEndpoint, long ownedDocument,
            long htmlGeneration, Func<bool> actualOwnerCurrent, string[] allowedTextNodes, string[] allowedDataKeys = null)
        {
            if (ownedHtmlEndpoint == null || actualOwnerCurrent == null) throw new ArgumentException("Owned HTML endpoint and actual generation witness are required.");
            if (ownedDocument <= 0 || htmlGeneration < 1) throw new ArgumentException("JavaScript requires a live caller-owned HTML document.");
            endpoint = ownedHtmlEndpoint; ownerCurrent = actualOwnerCurrent; handle = ownedDocument; generation = htmlGeneration;
            CopyIds(allowedTextNodes, textNodes); CopyIds(allowedDataKeys, dataKeys);
            if (!Current) throw new ArgumentException("HTML document is not owned by the supplied endpoint.");
            if (EndpointStatus().Item1 == "native-lcd")
                throw new ArgumentException("Unsupported: JavaScript document mutations require retained HUD/world/surface atomic publication; native LCD does not provide it.");
            if (!(bool)Call("script-claim", handle, scriptToken))
                throw new ArgumentException("HTML document already belongs to another active JavaScript realm.");
            claimed = true;
        }
        static Func<string, object[], object> OwnedEndpoint(HdrHtmlApi api)
        { if (api == null) throw new ArgumentNullException("api"); return (op, args) => api.Call(op, args); }
        static Func<bool> CaptureCurrent(HdrHtmlApi api)
        { long observed = api.ConnectionGeneration; return () => api.Ready && api.ConnectionGeneration == observed; }
        object Call(string command, params object[] arguments) { return endpoint(command, arguments); }
        MyTuple<string, long, long, MyTuple<bool, bool, string>, long> EndpointStatus()
        { return (MyTuple<string, long, long, MyTuple<bool, bool, string>, long>)Call("status", handle); }
        static void CopyIds(string[] ids, HashSet<string> destination)
        {
            if (ids == null) return;
            foreach (string id in ids)
            {
                if (string.IsNullOrEmpty(id) || id.Length > 128) throw new ArgumentException("Declared HTML identifier requires 1..128 characters.");
                destination.Add(id);
            }
        }
        void RequireCurrent()
        { if (!Current) throw new InvalidOperationException("HTML document owner/generation is retired."); }
        public JavaScriptDocumentStatus ReadStatus()
        {
            RequireCurrent(); var status = EndpointStatus();
            return new JavaScriptDocumentStatus { RendererReady = status.Item4.Item1,
                VisibleRevision = status.Item3, Error = status.Item4.Item3 };
        }
        public JavaScriptDocumentEvent[] PollEvents()
        {
            RequireCurrent(); var input = (MyTuple<string, string, string, MyTuple<double, long>>[])Call("poll-events", handle);
            var result = new JavaScriptDocumentEvent[input.Length];
            for (int i = 0; i < input.Length; i++)
                result[i] = new JavaScriptDocumentEvent { Kind = input[i].Item1, NodeId = input[i].Item2,
                    Action = input[i].Item3, Value = input[i].Item4.Item1, Revision = input[i].Item4.Item2 };
            return result;
        }
        public bool AllowsText(string nodeId) { return !disposed && textNodes.Contains(nodeId); }
        public bool AllowsData(string key) { return !disposed && dataKeys.Contains(key); }
        public void SetText(string nodeId, string value)
        { RequireCurrent(); if (!AllowsText(nodeId)) throw new ArgumentException("Undeclared JS text node."); Call("text", handle, nodeId, value); }
        public void SetData(string key, string value)
        { RequireCurrent(); if (!AllowsData(key)) throw new ArgumentException("Undeclared JS data key."); Call("data", handle, key, value); }

        public void ApplyMutations(JavaScriptDocumentMutation[] mutations)
        {
            RequireCurrent();
            if (mutations == null || mutations.Length > 64) throw new ArgumentException("HTML atomic mutation batches allow at most 64 entries.");
            var changes = new MyTuple<string, object[]>[mutations.Length];
            for (int i = 0; i < mutations.Length; i++)
            {
                var mutation = mutations[i];
                if (mutation == null) throw new ArgumentException("Missing JavaScript document mutation.");
                if (mutation.Kind == "text" || mutation.Kind == "data")
                {
                    if (mutation.Kind == "text" ? !AllowsText(mutation.Key) : !AllowsData(mutation.Key))
                        throw new ArgumentException("Undeclared JS HTML mutation target.");
                    changes[i] = new MyTuple<string, object[]>(mutation.Kind, new object[] { mutation.Key, mutation.Value });
                }
                else if (mutation.Kind == "source-choice")
                {
                    string reason;
                    if (!SourceReady(mutation.Key, out reason)) throw new InvalidOperationException(reason);
                    var source = sources[mutation.Key];
                    changes[i] = new MyTuple<string, object[]>("attach-source",
                        new object[] { source.Node, source.Provider, source.Source, source.Settings });
                }
                else throw new ArgumentException("Unsupported JS HTML mutation kind: " + mutation.Kind);
            }
            if (!(bool)Call("mutate", handle, changes))
                throw new InvalidOperationException(EndpointStatus().Item4.Item3 ?? "HTML atomic mutation batch failed.");
        }

        /// <summary>Only C# may register source choices. The probe checks required runtime/local consumer capability, not whether a first frame already exists. Mod-native providers need no plugin.</summary>
        public void AddSourceChoice(string choice, string nodeId, string provider, string sourceId,
            Func<MyTuple<bool, string>> actualCapabilityProbe, MyTuple<string, object[]>[] settings = null)
        {
            RequireCurrent();
            if (string.IsNullOrEmpty(choice) || choice.Length > 128 || string.IsNullOrEmpty(nodeId) ||
                string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(sourceId) || actualCapabilityProbe == null)
                throw new ArgumentException("Source choices require declared names/identities and an actual provider capability probe.");
            var copy = settings == null ? new MyTuple<string, object[]>[0] : new MyTuple<string, object[]>[settings.Length];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = new MyTuple<string, object[]>(settings[i].Item1,
                    settings[i].Item2 == null ? new object[0] : (object[])settings[i].Item2.Clone());
            sources[choice] = new SourceChoice { Node = nodeId, Provider = provider, Source = sourceId,
                Settings = copy, Capability = actualCapabilityProbe };
        }
        public bool SourceReady(string choice, out string reason)
        {
            reason = null;
            if (!Current) { reason = "HTML document owner/generation is retired."; return false; }
            SourceChoice source;
            if (!sources.TryGetValue(choice, out source)) { reason = "Unsupported: JavaScript source choice is not declared by its C# owner."; return false; }
            try
            {
                // Ask about this exact provider rather than applying the native plugin's status to custom mod-native sources.
                var capability = (MyTuple<bool, string>)Call("source-check", handle, source.Provider, source.Source);
                if (!capability.Item1)
                {
                    reason = capability.Item2 ?? "Unsupported: declared local source consumer is unavailable.";
                    if (reason.StartsWith("RequiresPlugin:", StringComparison.OrdinalIgnoreCase))
                        reason = "Requires plugin: " + reason.Substring("RequiresPlugin:".Length).Trim();
                    return false;
                }
                var available = source.Capability();
                if (!available.Item1) { reason = available.Item2 ?? "Unsupported: declared source provider is unavailable."; return false; }
                if (!Current) { reason = "HTML document retired during its source capability probe."; return false; }
                return true;
            }
            catch (Exception error) { reason = error.Message; return false; }
        }
        public void SelectSource(string choice)
        {
            string reason;
            if (!SourceReady(choice, out reason)) throw new InvalidOperationException(reason);
            var source = sources[choice];
            if (!(bool)Call("attach-source", handle, source.Node, source.Provider, source.Source, source.Settings))
                throw new InvalidOperationException(EndpointStatus().Item4.Item3 ?? "HTML source attachment failed.");
        }

        /// <summary>Releases only this JS association. The C# consumer retains its document and may dispose its HTML API separately.</summary>
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (claimed) try { Call("script-release", handle, scriptToken); } catch { }
            claimed = false; textNodes.Clear(); dataKeys.Clear(); sources.Clear();
        }
    }
}
