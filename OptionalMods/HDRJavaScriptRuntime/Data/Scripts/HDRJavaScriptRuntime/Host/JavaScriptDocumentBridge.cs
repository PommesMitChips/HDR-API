using System;
using System.Collections.Generic;

namespace HDRJavaScriptRuntime
{
    /// <summary>One explicitly registered client-local script for one owned HTML document. This is an HDR host profile, not a browser DOM.</summary>
    public sealed class JavaScriptDocumentBridge : IDisposable
    {
        public const string Profile = "HDR.JS/Host1";
        const string Shim = @"
var hdr = {
 setText: function(id, value) { return __hdrText(id, String(value)); },
 setData: function(key, value) { return __hdrData(key, String(value)); },
 on: function(kind, id, handler) {
   if (typeof handler === 'function') {
     return __hdrOn(kind, id, function(type, target, action, value, revision) {
       return handler({type:type, targetId:target, action:action, value:value, revision:revision});
     });
   }
   return __hdrOn(kind, id, handler);
 },
 off: function(kind, id) { return __hdrOff(kind, id); },
 setTimeout: function(handler, delay) { return __hdrTimer(handler, delay, false); },
 setInterval: function(handler, delay) { return __hdrTimer(handler, delay, true); },
 clearTimer: function(id) { return __hdrClearTimer(id); },
 selectSource: function(choice) { return __hdrSource(choice); },
 log: function(value) { return __hdrLog(String(value)); }
};";

        sealed class Handler : IDisposable
        {
            public string Name;
            public JavaScriptCallback Callback;
            public object Invoke(IJavaScriptRealm realm, object[] args)
            { return Callback == null ? realm.InvokeNamed(Name, args) : Callback.Invoke(args); }
            public void Dispose() { if (Callback != null) Callback.Dispose(); Callback = null; }
        }
        sealed class Timer
        { public int Id; public Handler Handler; public double Due, Interval; }

        readonly IJavaScriptRealm realm;
        readonly IJavaScriptDocumentHost document;
        readonly JavaScriptHostLimits limits;
        readonly Action<string> print;
        readonly long generation;
        readonly Dictionary<string, Handler> handlers = new Dictionary<string, Handler>(StringComparer.Ordinal);
        readonly Dictionary<int, Timer> timers = new Dictionary<int, Timer>();
        readonly Queue<JavaScriptDocumentEvent> events = new Queue<JavaScriptDocumentEvent>();
        readonly Dictionary<string, string> text = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> data = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<string> sourceChoices = new List<string>();
        bool registered, disposed, dispatching, realmDisposePending;
        int nextTimer;
        double clock;

        public bool Active { get { return registered && !disposed; } }
        public string LastError { get; private set; }

        public JavaScriptDocumentBridge(IJavaScriptRealm realm, IJavaScriptDocumentHost document,
            JavaScriptHostLimits limits = null, Action<string> print = null)
        {
            if (realm == null) throw new ArgumentNullException("realm");
            if (document == null) throw new ArgumentNullException("document");
            this.realm = realm; this.document = document;
            this.limits = (limits ?? new JavaScriptHostLimits()).Copy(); this.print = print;
            generation = document.Generation;
            if (!Current()) throw new ArgumentException("JavaScript requires a current caller-owned HTML document.");
            realm.RegisterHostFunction("__hdrText", a => QueueText(a, false));
            realm.RegisterHostFunction("__hdrData", a => QueueText(a, true));
            realm.RegisterHostFunction("__hdrOn", RegisterHandler);
            realm.RegisterHostFunction("__hdrOff", RemoveHandler);
            realm.RegisterHostFunction("__hdrTimer", RegisterTimer);
            realm.RegisterHostFunction("__hdrClearTimer", ClearTimer);
            realm.RegisterHostFunction("__hdrSource", QueueSource);
            realm.RegisterHostFunction("__hdrLog", Log);
        }

        /// <summary>Register exactly once. To replace code, retire this bridge and create a new realm; no shared globals survive.</summary>
        public bool RegisterScript(string source)
        {
            if (registered || disposed) throw new InvalidOperationException("JavaScript script registration requires a new live document realm.");
            if (source == null) throw new ArgumentNullException("source");
            registered = true;
            return Dispatch(() => { realm.Execute(Shim); realm.Execute(source); });
        }

        /// <summary>Explicit C# owner execution in the already-registered realm. Host writes retain the same atomic publication boundary.</summary>
        public bool Execute(string source)
        {
            if (!Active) throw new InvalidOperationException(LastError ?? "JavaScript document realm is not active.");
            if (source == null) throw new ArgumentNullException("source");
            return Dispatch(() => realm.Execute(source));
        }

        /// <summary>Explicit C# owner call of a named JS function. Only scalar results leave the interpreter.</summary>
        public object InvokeNamed(string name, params object[] arguments)
        {
            if (!Active) throw new InvalidOperationException(LastError ?? "JavaScript document realm is not active.");
            object result = null;
            if (!Dispatch(() => result = realm.InvokeNamed(name, arguments)))
                throw new InvalidOperationException(LastError ?? "JavaScript document call failed.");
            return result;
        }

        /// <summary>Time is monotonic client simulation milliseconds. Missed interval ticks collapse into one dispatch.</summary>
        public void Update(double nowMilliseconds)
        {
            if (disposed) { ReleasePendingRealm(); return; }
            if (!JavaScriptHostLimits.Finite(nowMilliseconds) || nowMilliseconds < clock || nowMilliseconds > 1e15)
                throw new ArgumentException("JavaScript host clock requires finite monotonic milliseconds.");
            clock = nowMilliseconds;
            if (!registered || dispatching) return;
            if (!Current()) { Retire("HTML owner/document generation retired; JavaScript callbacks and timers cancelled."); return; }
            JavaScriptDocumentStatus status;
            try { status = document.ReadStatus(); }
            catch (Exception error) { Retire(error.Message); return; }
            if (status == null || !status.RendererReady || status.VisibleRevision <= 0)
            { events.Clear(); LastError = status == null ? "HTML status unavailable." : status.Error; return; }
            try
            {
                var incoming = document.PollEvents() ?? new JavaScriptDocumentEvent[0];
                if (!Current()) { Retire("HTML document retired while polling events."); return; }
                // The frontend creates input from its successfully published frame. Reject queued events from a replaced frame.
                foreach (var item in incoming)
                {
                    if (item == null || item.Revision != status.VisibleRevision || !ValidEvent(item)) continue;
                    if (events.Count >= limits.PendingEvents) events.Dequeue();
                    events.Enqueue(item);
                }
                int remaining = limits.CallbacksPerUpdate;
                while (!disposed && remaining > 0 && events.Count > 0)
                {
                    var item = events.Dequeue();
                    // A preceding callback may publish a reflow. Its successor must use that latest visible frame.
                    status = document.ReadStatus();
                    if (status == null || !status.RendererReady || status.VisibleRevision <= 0)
                    { events.Clear(); return; }
                    if (item.Revision != status.VisibleRevision) continue;
                    Handler handler;
                    if (!handlers.TryGetValue(EventKey(item.Kind, item.NodeId), out handler)) continue;
                    remaining--;
                    if (!Dispatch(() => handler.Invoke(realm, new object[] { item.Kind, item.NodeId,
                        item.Action ?? "", item.Value, (double)item.Revision }))) return;
                }
                // Snapshot IDs: a callback may remove itself, register another timer, or remove a sibling.
                var due = new List<int>();
                foreach (var pair in timers) if (pair.Value.Due <= clock) due.Add(pair.Key);
                due.Sort();
                foreach (int id in due)
                {
                    if (disposed || remaining <= 0) break;
                    Timer timer;
                    if (!timers.TryGetValue(id, out timer) || timer.Due > clock) continue;
                    remaining--;
                    bool repeats = timer.Interval > 0;
                    if (repeats) timer.Due = clock + timer.Interval;
                    else timers.Remove(id);
                    try { if (!Dispatch(() => timer.Handler.Invoke(realm, new object[0]))) return; }
                    finally { if (!repeats) timer.Handler.Dispose(); }
                }
            }
            catch (Exception error) { Retire(error.Message); }
        }

        bool Dispatch(Action callback)
        {
            if (disposed || !Current()) { Retire("HTML document retired before JavaScript dispatch."); return false; }
            if (dispatching) throw new InvalidOperationException("Reentrant JavaScript document dispatch is unsupported.");
            dispatching = true; ClearMutations();
            try
            {
                callback();
                if (disposed || !Current()) { Retire("HTML document retired during JavaScript dispatch."); return false; }
                var mutations = new List<JavaScriptDocumentMutation>();
                foreach (var pair in text) mutations.Add(new JavaScriptDocumentMutation { Kind = "text", Key = pair.Key, Value = pair.Value });
                foreach (var pair in data) mutations.Add(new JavaScriptDocumentMutation { Kind = "data", Key = pair.Key, Value = pair.Value });
                foreach (string choice in sourceChoices) mutations.Add(new JavaScriptDocumentMutation { Kind = "source-choice", Key = choice });
                if (mutations.Count > 0) document.ApplyMutations(mutations.ToArray());
                LastError = null; return true;
            }
            catch (Exception error) { Retire("JavaScript document stopped: " + error.Message); return false; }
            finally { ClearMutations(); dispatching = false; ReleasePendingRealm(); }
        }

        object QueueText(object[] args, bool binding)
        {
            RequireDispatch(); Count(args, 2); string id = Text(args[0]), value = Text(args[1]);
            if (id.Length == 0 || id.Length > 128 || value.Length > limits.TextCharacters)
                throw new ArgumentException("JavaScript HTML identifier/text exceeds the configured host limits.");
            if (binding ? !document.AllowsData(id) : !document.AllowsText(id))
                throw new ArgumentException("JavaScript does not own this declared HTML " + (binding ? "data key: " : "text node: ") + id);
            var destination = binding ? data : text;
            if (!destination.ContainsKey(id) && MutationCount() >= limits.MutationsPerDispatch)
                throw new ArgumentException("JavaScript mutation limit exceeded.");
            destination[id] = value; return true;
        }
        object QueueSource(object[] args)
        {
            RequireDispatch(); Count(args, 1); string choice = Text(args[0]), reason;
            if (!document.SourceReady(choice, out reason)) { Report(reason ?? "Unsupported: source choice is unavailable."); return false; }
            if (MutationCount() >= limits.MutationsPerDispatch) throw new ArgumentException("JavaScript mutation limit exceeded.");
            sourceChoices.Add(choice); return true;
        }
        int MutationCount() { return text.Count + data.Count + sourceChoices.Count; }
        object RegisterHandler(object[] args)
        {
            RequireDispatch(); Count(args, 3); string kind = Text(args[0]), node = Text(args[1]);
            if ((kind != "click" && kind != "change") || node.Length < 1 || node.Length > 128)
                throw new ArgumentException("JavaScript event registration requires click/change and an explicit node ID.");
            string key = EventKey(kind, node);
            if (!handlers.ContainsKey(key) && handlers.Count >= limits.EventHandlers)
                throw new ArgumentException("JavaScript handler limit exceeded.");
            var candidate = HandlerFor(args[2]); Handler prior;
            if (handlers.TryGetValue(key, out prior)) prior.Dispose();
            handlers[key] = candidate; return true;
        }
        object RemoveHandler(object[] args)
        {
            RequireDispatch(); Count(args, 2); Handler prior; string key = EventKey(Text(args[0]), Text(args[1]));
            if (!handlers.TryGetValue(key, out prior)) return false;
            handlers.Remove(key); prior.Dispose(); return true;
        }
        object RegisterTimer(object[] args)
        {
            RequireDispatch(); Count(args, 3); double delay = Number(args[1]);
            if (!(args[2] is bool) || delay < 0 || delay > limits.MaximumTimerDelayMs)
                throw new ArgumentException("JavaScript timer delay exceeds the configured host limit.");
            if (timers.Count >= limits.Timers || nextTimer == int.MaxValue)
                throw new ArgumentException("JavaScript timer limit exceeded.");
            var handler = HandlerFor(args[0]); int id = ++nextTimer;
            timers[id] = new Timer { Id = id, Handler = handler, Due = clock + Math.Max(1, delay),
                Interval = (bool)args[2] ? Math.Max(1, delay) : 0 };
            return (double)id;
        }
        object ClearTimer(object[] args)
        {
            RequireDispatch(); Count(args, 1); double value = Number(args[0]);
            if (value < 1 || value > int.MaxValue || value != Math.Floor(value)) return false;
            Timer timer; int id = (int)value;
            if (!timers.TryGetValue(id, out timer)) return false;
            timers.Remove(id); timer.Handler.Dispose(); return true;
        }
        Handler HandlerFor(object value)
        {
            var callback = value as JavaScriptCallback;
            if (callback != null)
            {
                if (!callback.Current || !callback.IsOwnedBy(realm)) throw new ArgumentException("JavaScript callback belongs to a retired or foreign realm.");
                return new Handler { Callback = callback };
            }
            string name = value as string;
            if (string.IsNullOrEmpty(name) || name.Length > 128 || !Identifier(name))
                throw new ArgumentException("JavaScript callback requires a realm-owned function or a simple global function name.");
            return new Handler { Name = name };
        }
        object Log(object[] args)
        { RequireDispatch(); Count(args, 1); string value = Text(args[0]); if (value.Length > limits.TextCharacters) throw new ArgumentException("JavaScript log exceeds the configured host text limit."); Report(value); return null; }
        void RequireDispatch()
        { if (!dispatching || disposed || !Current()) throw new InvalidOperationException("JavaScript host function requires the current active document dispatch."); }
        bool Current()
        { try { return !disposed && document.Current && document.Generation == generation; } catch { return false; } }
        static bool ValidEvent(JavaScriptDocumentEvent item)
        { return (item.Kind == "click" || item.Kind == "change") && !string.IsNullOrEmpty(item.NodeId) && item.NodeId.Length <= 128 && JavaScriptHostLimits.Finite(item.Value); }
        static string EventKey(string kind, string node) { return kind + ":" + node; }
        static bool Identifier(string value)
        {
            for (int i = 0; i < value.Length; i++)
            { char c = value[i]; if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c == '_' || c == '$' || i > 0 && c >= '0' && c <= '9')) return false; }
            return true;
        }
        static void Count(object[] args, int expected)
        { if (args == null || args.Length != expected) throw new ArgumentException("JavaScript host argument count mismatch."); }
        static string Text(object value)
        { var result = value as string; if (result == null) throw new ArgumentException("JavaScript host argument requires a string."); return result; }
        static double Number(object value)
        { if (!(value is double) || !JavaScriptHostLimits.Finite((double)value)) throw new ArgumentException("JavaScript host argument requires a finite number."); return (double)value; }
        void ClearMutations() { text.Clear(); data.Clear(); sourceChoices.Clear(); }
        void Report(string message)
        { if (print != null) try { print(message); } catch { } }
        void Retire(string reason)
        { if (disposed) return; LastError = reason; Report(reason); Dispose(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true; registered = false;
            foreach (var handler in handlers.Values) handler.Dispose(); handlers.Clear();
            foreach (var timer in timers.Values) timer.Handler.Dispose(); timers.Clear();
            events.Clear(); ClearMutations();
            try { realm.Dispose(); } catch { realmDisposePending = true; }
            try { document.Dispose(); } catch { }
        }
        void ReleasePendingRealm()
        {
            if (!realmDisposePending || dispatching) return;
            try { realm.Dispose(); realmDisposePending = false; } catch { }
        }
    }
}
