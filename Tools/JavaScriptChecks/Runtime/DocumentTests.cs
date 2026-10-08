using System;
using System.Collections.Generic;
using HDRJavaScriptRuntime;

internal static class DocumentTests
{
    private sealed class Document : IJavaScriptDocumentHost
    {
        public long GenerationValue = 1;
        public bool CurrentValue = true;
        public bool Ready = true;
        public long Revision = 7;
        public bool SourceAvailable = true;
        public bool StatusThrows;
        public int RejectMutationIndex = -1;
        public int DisposeCount;
        public string Selected;
        public readonly Dictionary<string, string> Text = new Dictionary<string, string>();
        public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
        public readonly List<JavaScriptDocumentEvent> Events = new List<JavaScriptDocumentEvent>();
        public readonly List<string> Writes = new List<string>();
        public long Generation { get { return GenerationValue; } }
        public bool Current { get { return CurrentValue; } }
        public JavaScriptDocumentStatus ReadStatus()
        {
            if (StatusThrows) throw new InvalidOperationException("expected status failure");
            return new JavaScriptDocumentStatus { RendererReady = Ready, VisibleRevision = Revision, Error = Ready ? null : "Requires plugin: optional test renderer" };
        }
        public JavaScriptDocumentEvent[] PollEvents() { var result = Events.ToArray(); Events.Clear(); return result; }
        public bool AllowsText(string nodeId) { return nodeId == "status" || nodeId == "detail"; }
        public bool AllowsData(string key) { return key == "count"; }
        public void SetText(string nodeId, string value) { Text[nodeId] = value; Writes.Add("text:" + nodeId + ":" + value); }
        public void SetData(string key, string value) { Data[key] = value; Writes.Add("data:" + key + ":" + value); }
        public bool SourceReady(string choice, out string reason)
        {
            if (choice != "forward" && choice != "aft") { reason = "Unknown owned source choice."; return false; }
            reason = SourceAvailable ? null : "Requires plugin: HDR Client Renderer (camera capture).";
            return SourceAvailable;
        }
        public void SelectSource(string choice) { Selected = choice; Writes.Add("source:" + choice); }
        public void ApplyMutations(JavaScriptDocumentMutation[] mutations)
        {
            var candidateText = new Dictionary<string, string>(Text);
            var candidateData = new Dictionary<string, string>(Data);
            var candidateWrites = new List<string>();
            string candidateSource = Selected;
            for (int i = 0; i < mutations.Length; i++)
            {
                var mutation = mutations[i];
                if (i == RejectMutationIndex) throw new InvalidOperationException("expected atomic mutation failure");
                if (mutation.Kind == "text")
                {
                    if (!AllowsText(mutation.Key)) throw new InvalidOperationException("foreign text node");
                    candidateText[mutation.Key] = mutation.Value;
                }
                else if (mutation.Kind == "data")
                {
                    if (!AllowsData(mutation.Key)) throw new InvalidOperationException("foreign data key");
                    candidateData[mutation.Key] = mutation.Value;
                }
                else if (mutation.Kind == "source-choice")
                {
                    string reason;
                    if (!SourceReady(mutation.Key, out reason)) throw new InvalidOperationException(reason);
                    candidateSource = mutation.Key;
                }
                else throw new InvalidOperationException("unexpected mutation kind");
                candidateWrites.Add(mutation.Kind + ":" + mutation.Key + ":" + mutation.Value);
            }
            Text.Clear(); foreach (var pair in candidateText) Text[pair.Key] = pair.Value;
            Data.Clear(); foreach (var pair in candidateData) Data[pair.Key] = pair.Value;
            Selected = candidateSource; Writes.AddRange(candidateWrites);
        }
        public void Dispose() { DisposeCount++; }
        public void Click(string node, long revision, double value = 0)
        { Events.Add(new JavaScriptDocumentEvent { Kind = "click", NodeId = node, Action = "select", Value = value, Revision = revision }); }
    }

    private static Action<bool, string> check;

    public static void Run(Action<bool, string> assert, Action<string, Action> runCase)
    {
        check = assert;
        runCase("HTML closure events and camera choices", EventsAndSources);
        runCase("HTML closure timers and cadence coalescing", Timers);
        runCase("HTML transactional script errors", Errors);
        runCase("HTML ownership plugin guards and retirement", OwnershipAndCapabilities);
        runCase("HTML bounded event backlog", EventBacklog);
        runCase("HTML reentrant disposal during host callback", ReentrantDisposal);
    }

    private static JavaScriptDocumentBridge Bridge(Document document, out JavaScriptRealm realm, JavaScriptHostLimits limits = null, Action<string> log = null)
    {
        realm = new JavaScriptRealm(new JavaScriptLimits());
        return new JavaScriptDocumentBridge(realm, document, limits, log);
    }

    private static void EventsAndSources()
    {
        var doc = new Document(); JavaScriptRealm realm;
        var bridge = Bridge(doc, out realm);
        check(bridge.RegisterScript(@"
var selected = 0;
hdr.setText('status','ready');
hdr.on('click','next',function(e) {
 selected = 1 - selected;
 hdr.selectSource(selected ? 'aft' : 'forward');
 hdr.setText('status', JSON.stringify([e.type,e.targetId,e.action,e.value,e.revision,selected]));
});"), "register real JS closure against document adapter");
        check(bridge.Active && doc.Text["status"] == "ready", "initial successful JS publication");
        doc.Click("next", 6, 99); doc.Click("next", 7, 3);
        bridge.Update(10);
        check(doc.Selected == "aft", "fresh published-frame event selects source");
        check(doc.Text["status"] == "[\"click\",\"next\",\"select\",3,7,1]", "event object has actual typed fields and stale event is ignored");
        doc.Click("next", 7, 4); bridge.Update(20);
        check(doc.Selected == "forward", "closure state persists across callbacks");
        check(doc.Text["status"] == "[\"click\",\"next\",\"select\",4,7,0]", "second fresh event reverses choice");
        bridge.Dispose(); bridge.Dispose();
        check(!bridge.Active && doc.DisposeCount == 1, "bridge disposal is idempotent");
        var visible = doc.Text["status"]; doc.Click("next", 7); bridge.Update(30);
        check(doc.Text["status"] == visible, "retired bridge cannot alter last published content");
    }

    private static void Timers()
    {
        var doc = new Document(); JavaScriptRealm realm;
        var bridge = Bridge(doc, out realm);
        check(bridge.RegisterScript(@"
var ticks=0;
var cancelled=hdr.setTimeout(function(){hdr.setText('detail','bad');},5);
hdr.clearTimer(cancelled);
hdr.setTimeout(function(){hdr.setText('detail','once');},10);
hdr.setInterval(function(){ticks++;hdr.setData('count',ticks);},10);
"), "closure timers register");
        bridge.Update(9);
        check(doc.Writes.Count == 0, "timer does not run early");
        bridge.Update(10);
        check(doc.Text["detail"] == "once" && doc.Data["count"] == "1", "one-shot and interval closures run at due time");
        bridge.Update(10000);
        check(doc.Data["count"] == "2", "missed interval ticks coalesce");
        bridge.Update(10000);
        check(doc.Data["count"] == "2", "same clock cannot redispatch interval");
        doc.Ready = false; bridge.Update(10010);
        check(doc.Data["count"] == "2" && bridge.Active, "unready optional renderer pauses host dispatch safely");
        doc.Ready = true; bridge.Update(10020);
        check(doc.Data["count"] == "3", "renderer readiness resumes without backlog explosion");
        bridge.Dispose(); bridge.Update(20000);
        check(doc.Data["count"] == "3", "disposal cancels timers");
    }

    private static void Errors()
    {
        var doc = new Document(); doc.Text["status"] = "previous"; JavaScriptRealm realm;
        var bridge = Bridge(doc, out realm);
        check(!bridge.RegisterScript("hdr.setText('status','uncommitted'); hdr.setTimeout(function(){hdr.setText('status','late');},1); throw new Error('stop');"), "initial script fault returns a safe result");
        check(!bridge.Active && bridge.LastError.Contains("stop"), "fault retires realm and reports error");
        check(doc.Text["status"] == "previous" && doc.Writes.Count == 0, "script fault discards queued mutations");
        bridge.Update(100); check(doc.Text["status"] == "previous" && doc.DisposeCount == 1, "fault cancels timers and preserves published content");

        doc = new Document(); bridge = Bridge(doc, out realm);
        check(bridge.RegisterScript("hdr.setText('status','good'); hdr.on('click','next',function(){hdr.setText('status','uncommitted');throw new Error('callback');});"), "faulting event fixture initially publishes");
        doc.Click("next", 7); bridge.Update(1);
        check(!bridge.Active && doc.Text["status"] == "good", "callback fault preserves previous frame");
        check(doc.DisposeCount == 1 && bridge.LastError.Contains("callback"), "callback realm teardown reports reason once");

        doc = new Document(); bridge = Bridge(doc, out realm);
        check(bridge.RegisterScript("hdr.setText('status','good'); hdr.setTimeout(function(){hdr.setText('status','uncommitted'); throw new Error('timer');},1);"), "faulting timer fixture initially publishes");
        bridge.Update(1);
        check(!bridge.Active && doc.Text["status"] == "good" && doc.DisposeCount == 1, "timer fault preserves previous frame and retires once");

        doc = new Document(); bridge = Bridge(doc, out realm);
        check(!bridge.RegisterScript("hdr.setText('foreign-node','bad');"), "foreign node host mutation is rejected");
        check(!bridge.Active && doc.Writes.Count == 0, "host authorization error produces no writes");

        doc = new Document { RejectMutationIndex = 1 }; doc.Text["status"] = "published"; bridge = Bridge(doc, out realm);
        check(!bridge.RegisterScript("hdr.setText('status','uncommitted'); hdr.setData('count',2);"), "host rejection of second batch entry stops dispatch");
        check(!bridge.Active && doc.Text["status"] == "published" && doc.Data.Count == 0, "atomic host flush prevents partial earlier mutations");
        check(doc.Writes.Count == 0 && doc.DisposeCount == 1, "failed atomic flush leaves published state and retires once");
    }

    private static void OwnershipAndCapabilities()
    {
        var doc = new Document { SourceAvailable = false }; JavaScriptRealm realm;
        var logs = new List<string>(); var bridge = Bridge(doc, out realm, null, logs.Add);
        check(bridge.RegisterScript("hdr.setData('count',hdr.selectSource('forward'));"), "unavailable optional camera source does not crash script");
        check(bridge.Active && doc.Selected == null && doc.Data["count"] == "false", "plugin guard returns false without publishing fake source");
        check(logs.Count == 1 && logs[0].StartsWith("Requires plugin:"), "plugin guard prints explicit requirement");
        doc.GenerationValue++;
        bridge.Update(1);
        check(!bridge.Active && doc.DisposeCount == 1, "document generation retirement cancels realm");
        check(doc.Data["count"] == "false", "generation retirement preserves published state");

        doc = new Document(); bridge = Bridge(doc, out realm);
        check(bridge.RegisterScript("hdr.setText('status','valid');"), "status-fault fixture publishes first");
        doc.StatusThrows = true; bridge.Update(1);
        check(!bridge.Active && bridge.LastError == "expected status failure", "host status exception is isolated");
        check(doc.Text["status"] == "valid" && doc.DisposeCount == 1, "host status exception preserves content");
    }

    private static void EventBacklog()
    {
        var doc = new Document(); JavaScriptRealm realm;
        var limits = new JavaScriptHostLimits { CallbacksPerUpdate = 2, PendingEvents = 3 };
        var bridge = Bridge(doc, out realm, limits);
        check(bridge.RegisterScript("var n=0;hdr.on('click','next',function(e){n++;hdr.setText('status',n+':'+e.value);});"), "bounded backlog fixture registers");
        for (int i = 1; i <= 5; i++) doc.Click("next", 7, i);
        bridge.Update(1);
        check(doc.Text["status"] == "2:4", "queue retains bounded newest events and dispatches configured count");
        bridge.Update(2);
        check(doc.Text["status"] == "3:5", "next update drains remaining event exactly once");
        int writes = doc.Writes.Count; bridge.Update(3);
        check(doc.Writes.Count == writes, "drained queue does not replay callbacks");
        bridge.Dispose();
    }

    private static void ReentrantDisposal()
    {
        var doc = new Document(); doc.Text["status"] = "published";
        var realm = new JavaScriptRealm(new JavaScriptLimits { TimeoutMilliseconds = 1000 });
        JavaScriptDocumentBridge bridge = null;
        bridge = new JavaScriptDocumentBridge(realm, doc, null, value => bridge.Dispose());
        check(!bridge.RegisterScript("hdr.setText('status','uncommitted');hdr.log('retire');"), "host callback can request retirement during executing script");
        check(!bridge.Active && !realm.Current, "deferred retirement disposes realm after interpreter unwinds");
        check(doc.Text["status"] == "published" && doc.Writes.Count == 0, "reentrant retirement drops queued writes");
        check(doc.DisposeCount == 1, "reentrant retirement releases host exactly once");
        bridge.Dispose();
        check(doc.DisposeCount == 1, "later duplicate disposal stays inert");
    }
}
