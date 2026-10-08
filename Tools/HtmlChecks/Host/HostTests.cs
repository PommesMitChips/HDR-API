using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Hdr.Html;
using Hdr.Mods;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyUtilities = VRage.Game.ModAPI.IMyUtilities;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

// DispatchProxy reflects only the test doubles' public interfaces. Production state is never reflected.
public class HtmlHostProxy : DispatchProxy
{
    public Func<MethodInfo, object[], object> Handler;
    protected override object Invoke(MethodInfo method, object[] args) { return Handler(method, args); }
}

static partial class HostTests
{
    static int assertions, cases, failures;
    const string ButtonMarkup = "<button id='button' data-action='activate'>Go</button>";
    const string ButtonCss = "button {width:100px;height:30px;}";
    const string RangeMarkup = "<input id='range' type='range' min='0' max='100' step='1' value='0' data-bind='gain' data-action='gain-change'/><p id='label'>A</p>";
    const string RangeCss = "input {width:100px;height:24px;} p {height:24px;}";

    static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }
    static void Equal<T>(T expected, T actual, string message)
    { Check(EqualityComparer<T>.Default.Equals(expected, actual), message + "; expected " + expected + ", got " + actual); }
    static void Reject<T>(Action action, string contains) where T : Exception
    {
        try { action(); } catch (T error) { Check(error.Message.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0, "rejection should explain " + contains + ": " + error.Message); return; }
        throw new Exception("Expected " + typeof(T).Name + " containing " + contains);
    }
    static void Run(string name, Action test)
    {
        cases++;
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error); }
    }
    static int Main()
    {
        Run("failed paint keeps committed-frame picking and retry cadence", FailedPaintKeepsVisible);
        Run("failed restore retires visible hit regions", FailedRestoreRetiresHits);
        Run("native mutation failure retires hits without a Changed claim", NativeMutationFailureRetiresHits);
        Run("load and layout errors preserve prior visible output", ErrorsPreserveVisible);
        Run("unchanged and coalesced updates avoid relayout", NoopAndCoalescing);
        Run("prototype demo counter uses real text interpolation", DemoCounterBinding);
        Run("range changes coalesce and button event queue is bounded", BoundedEvents);
        Run("range grab survives unrelated committed text update", RangeSurvivesLabelCommit);
        Run("replacement source cancels old range grab", SourceReplacementCancelsGrab);
        Run("changed range geometry and action cancel old grab", ShapeAndBindingCancelGrab);
        Run("input suppression clears queued events and held press", SuppressionClearsInput);
        Run("pointer validation and painter lifetime fail closed", PointerAndDisposal);
        Run("actual session works for owned LCD without HDR core", ServiceWithoutCore);
        Run("actual service isolates owner generations and document handles", ServiceOwnership);
        Run("actual host suppresses queued input when global rendering is disabled", ServiceRenderingSuppression);
        Run("helper reentrant command and Ready fence replacement generations", HelperGenerationFences);
        Run("helper acquisitions survive release-callback reconnect", HelperAcquisitionFence);
        Run("helper version/open acquisitions preserve newest discovery", HelperNestedAcquisitions);
        Run("helper capabilities fence reentrant replacement", HelperCapabilitiesFence);
        Run("absent frontend and malformed discovery preserve helper lifecycle", HelperAbsentAndMalformedDiscovery);
        Run("host admission quotas preserve capacity after rejected documents", ServiceAdmissionLimits);
        Run("native surface leases isolate documents and free on retirement", NativeSurfaceLeases);
        Run("native input follows current owned surface mode and dimensions", NativeSurfaceAuthority);
        Run("actual native draw failure retires hits and keeps retry reason", NativeDrawFailureStatus);
        Run("renderer rebuild retains committed edits and rejects failed text edits", RebuildEditState);
        Run("failed queued data rebuild retains committed text and status error", FailedQueuedDataRebuild);
        Run("pending hosted resize survives renderer rebuild", PendingResizeRebuild);
        Run("actual service unload revokes owners and unregisters handlers", SessionUnload);
        Run("local mapped HTML surfaces preserve centered coordinates and ray input", SurfaceCreationAndRayInput);
        Run("local HTML source changes preserve layout, controls and rollback", LocalSourcesAndRollback);
        Run("HUD HTML source attachments preserve pixel canvas and input", HudSourcesAndPixels);
        Run("legacy world HTML preserves top-left pose through source attachment and resize", LegacyWorldSourcesAndPose);
        Run("local source capability and hidden-node fences precede declarations", LocalSourceCapabilityFences);
        Run("hosted geometry allowance forwards unlimited sentinel and finite preflight", HostedGeometryForwarding);
        Console.WriteLine("HTML host tests: " + cases + " cases, " + assertions + " assertions, " + failures + " failures; C#6 production glob and consumer helpers compiled against real SE assemblies.");
        return failures == 0 ? 0 : 1;
    }

    sealed class Metrics : IHtmlTextMetrics
    {
        internal int Calls;
        internal bool Fail;
        public string Profile { get { return "Test:cap-height"; } }
        public HtmlTextMeasurement Measure(string text, double size, double lineHeight)
        {
            Calls++; if (Fail) throw new InvalidOperationException("injected metric failure");
            return new HtmlTextMeasurement { Advance = text.Length * size * .5, CapHeight = size, LineAdvance = size * lineHeight, HasInk = text.Trim().Length > 0, Ink = new HtmlRect(0, 0, text.Length * size * .5, size), Generation = 1 };
        }
    }
    static void DemoCounterBinding()
    {
        var controller=new HtmlDocumentController(new Metrics(),new HtmlLimits());
        Check(controller.TryLoad(HtmlFrontendSession.DemoMarkup,HtmlFrontendSession.DemoStylesheet,480,280),"actual demo markup layouts: "+controller.LastError);
        Check(controller.SetData("clock","37"),"counter binding queues new text");
        Check(controller.Update(),"counter update relayouts the authored demo");
        Check(PaintedText(controller.Frame).IndexOf("37",StringComparison.Ordinal)>=0,"counter value reaches actual painted text rather than a literal zero");
    }
    sealed class Painter : IHtmlPainter
    {
        internal readonly Queue<HtmlPaintReport> Results = new Queue<HtmlPaintReport>();
        internal int Calls, Disposals;
        internal HtmlPaintFrame Output;
        public bool TryPaint(HtmlPaintFrame frame, out HtmlPaintReport report)
        {
            Calls++;
            report = Results.Count == 0 ? new HtmlPaintReport { Success = true, Changed = true } : Results.Dequeue();
            if (report.Success) Output = HtmlPaintDiff.Snapshot(frame);
            else if (report.Changed && !report.RestoredPrevious) Output = null;
            return report.Success;
        }
        public void Reset() { Output = null; }
        public void Dispose() { Disposals++; Output = null; }
        internal void Fail(bool changed, bool restored)
        { Results.Enqueue(new HtmlPaintReport { Success = false, Changed = changed, RestoredPrevious = restored, Error = "injected paint failure" }); }
    }
    sealed class Fixture : IDisposable
    {
        internal readonly Metrics Metrics = new Metrics();
        internal readonly Painter Painter = new Painter();
        internal readonly HtmlDocumentController Controller;
        internal readonly HtmlFrontendDocument Document;
        internal Fixture(string markup, string css)
        {
            Controller = new HtmlDocumentController(Metrics, new HtmlLimits());
            Document = new HtmlFrontendDocument(41, "test", Controller, Painter, false);
            Check(Document.Load(markup, css, 400, 200), "fixture load: " + Document.LastError);
            Document.Update(0, true);
            Check(Document.VisibleFrame != null, "fixture has committed visible output");
        }
        public void Dispose() { Document.Dispose(); }
    }
    static HtmlHitRegion Hit(HtmlPaintFrame frame, string id)
    {
        foreach (var hit in frame.Hits) if (hit.NodeId == id) return hit;
        throw new Exception("Missing hit " + id);
    }
    static double MiddleY(HtmlHitRegion hit) { return hit.Bounds.Y + hit.Bounds.Height / 2; }
    static void Click(HtmlFrontendDocument document, HtmlHitRegion hit)
    { ClickAt(document, hit.Bounds.X + hit.Bounds.Width / 2, MiddleY(hit)); }
    static void ClickAt(HtmlFrontendDocument document, double x, double y)
    { document.Pointer(x, y, true); document.Pointer(x, y, false); }
    static void Drag(HtmlFrontendDocument document, HtmlHitRegion hit, double proportion)
    { document.Pointer(hit.Bounds.X + hit.Bounds.Width * proportion, MiddleY(hit), true); }

    static void FailedPaintKeepsVisible()
    {
        using (var f = new Fixture(ButtonMarkup, ButtonCss))
        {
            var original = f.Document.VisibleFrame; long revision = original.Revision; var oldHit = Hit(original, "button");
            Check(!ReferenceEquals(original, f.Controller.Frame), "visible paint is a detached snapshot");
            Check(f.Document.Load(ButtonMarkup, ButtonCss + " button {margin-left:180px;}", 400, 200), "desired replacement admitted");
            f.Painter.Fail(true, true); f.Document.Update(10, true);
            Check(f.Controller.Revision > revision && f.Document.PendingPaint, "desired revision advances while paint stays pending");
            Equal(revision, f.Document.VisibleFrame.Revision, "failed restored paint retains visible revision");
            Click(f.Document, oldHit); var events = f.Document.PollEvents();
            Equal(1, events.Length, "old visible button remains interactive"); Equal(revision, events[0].Item4.Item2, "event carries committed revision");
            Click(f.Document, Hit(f.Controller.Frame, "button")); Equal(0, f.Document.PollEvents().Length, "unpainted candidate cannot receive a click");
            int calls = f.Painter.Calls; f.Document.Update(69, true); Equal(calls, f.Painter.Calls, "failed paint retry is deferred 60 ticks");
            f.Document.Update(70, true); Check(!f.Document.PendingPaint && f.Document.LastError == null, "successful retry clears pending error");
            Equal(f.Controller.Revision, f.Document.VisibleFrame.Revision, "retry commits desired frame");
            Click(f.Document, oldHit); Equal(0, f.Document.PollEvents().Length, "old location retires after successful commit");
            Click(f.Document, Hit(f.Document.VisibleFrame, "button")); Equal(1, f.Document.PollEvents().Length, "new committed location receives click");
        }
    }
    static void FailedRestoreRetiresHits()
    {
        using (var f = new Fixture(ButtonMarkup, ButtonCss))
        {
            var hit = Hit(f.Document.VisibleFrame, "button");
            f.Document.Pointer(hit.Bounds.X + 1, hit.Bounds.Y + 1, true);
            f.Controller.SetText("button", "Next"); f.Painter.Fail(true, false); f.Document.Update(1, true);
            Check(f.Document.VisibleFrame == null && f.Painter.Output == null && f.Document.PendingPaint, "unrestored output has no visible frame");
            f.Document.Pointer(hit.Bounds.X + 1, hit.Bounds.Y + 1, false); Click(f.Document, hit);
            Equal(0, f.Document.PollEvents().Length, "retired context and old held button cannot produce events");
            f.Document.Update(61, true); Check(f.Document.VisibleFrame != null, "a later full paint can recover input");
        }
        using (var f = new Fixture(ButtonMarkup, ButtonCss))
        {
            long revision = f.Document.VisibleFrame.Revision; f.Controller.SetText("button", "Next");
            f.Painter.Fail(false, false); f.Document.Update(1, true);
            Equal(revision, f.Document.VisibleFrame.Revision, "prepare rejection preserves prior frame without restore claim");
        }
    }
    static void ErrorsPreserveVisible()
    {
        using (var f = new Fixture(ButtonMarkup, ButtonCss))
        {
            var visible = f.Document.VisibleFrame; var desired = f.Controller.Frame; string source = f.Document.Markup; int calls = f.Painter.Calls;
            Check(!f.Document.Load("<script>bad()</script>", "", 400, 200), "unsupported source fails load");
            Check(ReferenceEquals(visible, f.Document.VisibleFrame) && ReferenceEquals(desired, f.Controller.Frame) && f.Document.Markup == source, "parse failure preserves old document and paint");
            Check(!string.IsNullOrEmpty(f.Document.LastError), "parse failure exposes reason");
            f.Metrics.Fail = true; Check(f.Controller.SetText("button", "Changed"), "text update staged"); f.Document.Update(1, true);
            Check(ReferenceEquals(visible, f.Document.VisibleFrame) && ReferenceEquals(desired, f.Controller.Frame), "layout failure preserves old desired and visible frames");
            Equal(calls, f.Painter.Calls, "failed layout does not invoke painter");
            Check(f.Document.LastError.IndexOf("metric failure", StringComparison.Ordinal) >= 0, "layout failure reaches host status");
            Click(f.Document, Hit(visible, "button")); Equal(1, f.Document.PollEvents().Length, "retained visible button works after source/layout errors");
        }
    }
    static void NativeMutationFailureRetiresHits()
    {
        using (var f = new Fixture(ButtonMarkup, ButtonCss))
        {
            var hit = Hit(f.Document.VisibleFrame, "button");
            f.Document.Pointer(hit.Bounds.X + 1, MiddleY(hit), true);
            f.Document.SetText("button", "Next");
            f.Painter.Results.Enqueue(new HtmlPaintReport { Success = false, Changed = false, MutatingCalls = 1, RestoredPrevious = false, Error = "injected native DrawFrame failure" });
            f.Document.Update(1, true);
            Check(f.Document.VisibleFrame == null && f.Document.PendingPaint, "attempted native mutation without restore makes prior hit publication uncertain even without Changed");
            Check(f.Document.LastError.IndexOf("DrawFrame", StringComparison.Ordinal) >= 0, "native failure reason reaches host status");
            f.Document.Pointer(hit.Bounds.X + 1, MiddleY(hit), false); Click(f.Document, hit);
            Equal(0, f.Document.PollEvents().Length, "uncertain native frame cannot emit old held-release or new click events");
            f.Document.Update(61, true); Check(f.Document.VisibleFrame != null, "full retry republishes native-style failed frame");
            Click(f.Document, Hit(f.Document.VisibleFrame, "button")); Equal(1, f.Document.PollEvents().Length, "new input works after successful repaint");
        }
        using (var f = new Fixture(ButtonMarkup, ButtonCss))
        {
            long revision = f.Document.VisibleFrame.Revision; f.Document.SetText("button", "Next");
            f.Painter.Results.Enqueue(new HtmlPaintReport { Success = false, Changed = false, MutatingCalls = 1, RestoredPrevious = true, Error = "injected mutation with successful restore" });
            f.Document.Update(1, true); Equal(revision, f.Document.VisibleFrame.Revision, "confirmed restore retains prior hits after attempted mutation");
        }
    }
    static void NoopAndCoalescing()
    {
        using (var f = new Fixture(ButtonMarkup, ButtonCss))
        {
            long builds = f.Controller.LayoutBuildCount, revision = f.Controller.Revision; int measures = f.Metrics.Calls, paints = f.Painter.Calls;
            for (int i = 1; i <= 200; i++) f.Document.Update(i, true);
            Check(f.Controller.LayoutBuildCount == builds && f.Metrics.Calls == measures && f.Painter.Calls == paints, "unchanged initial update ticks do no work");
            Check(!f.Controller.SetText("button", "Go") && !f.Controller.Resize(400, 200), "same text and viewport are no-ops");
            f.Controller.SetText("button", "Temporary"); f.Controller.SetText("button", "Go");
            f.Controller.Resize(350, 200); f.Controller.Resize(400, 200);
            f.Controller.SetData("x", "one"); f.Document.Update(201, true);
            long withData = f.Controller.LayoutBuildCount; Check(!f.Controller.SetData("x", "one"), "same binding data is a no-op");
            f.Controller.SetData("x", "temporary"); f.Controller.SetData("x", "one"); f.Document.Update(202, true);
            Equal(withData, f.Controller.LayoutBuildCount, "reverted data edit does not relayout");
            Equal(builds + 1, withData, "coalesced mutations build only one layout");
            Equal(revision + 1, f.Controller.Revision, "no-op edits do not consume revisions");
            Check(f.Metrics.Calls > measures && f.Painter.Calls == paints + 1, "only changed data batch measures and paints");
            builds = f.Controller.LayoutBuildCount; measures = f.Metrics.Calls; paints = f.Painter.Calls;
            for (int i = 203; i < 303; i++) f.Document.Update(i, true);
            Check(f.Controller.LayoutBuildCount == builds && f.Metrics.Calls == measures && f.Painter.Calls == paints, "idle ticks invoke no layout, measurements, or paint");
        }
    }
    static void BoundedEvents()
    {
        using (var f = new Fixture(ButtonMarkup, ButtonCss))
        {
            var hit = Hit(f.Document.VisibleFrame, "button");
            for (int i = 0; i < 200; i++) Click(f.Document, hit);
            var events = f.Document.PollEvents(); Equal(128, events.Length, "click queue retains bounded latest 128 events");
            foreach (var item in events) Check(item.Item1 == "click" && item.Item2 == "button" && item.Item3 == "activate" && item.Item4.Item2 == f.Document.VisibleFrame.Revision, "event ABI carries kind, node, action, and visible revision");
            Equal(0, f.Document.PollEvents().Length, "poll drains queued events");
        }
        using (var f = new Fixture(RangeMarkup, RangeCss))
        {
            var hit = Hit(f.Document.VisibleFrame, "range");
            for (int i = 0; i < 1000; i++) Drag(f.Document, hit, (i % 101) / 100.0);
            var events = f.Document.PollEvents(); Equal(1, events.Length, "unpolled range changes coalesce by node");
            Equal(90.0, events[0].Item4.Item1, "coalesced event contains latest quantized value");
            Drag(f.Document, hit, .904); Equal(0, f.Document.PollEvents().Length, "same quantized value produces no event");
            Drag(f.Document, hit, -2); Equal(0.0, f.Document.PollEvents()[0].Item4.Item1, "range drag clamps to minimum outside bounds");
            Drag(f.Document, hit, 2); Equal(100.0, f.Document.PollEvents()[0].Item4.Item1, "range drag clamps to maximum outside bounds");
        }
    }
    static void RangeSurvivesLabelCommit()
    {
        using (var f = new Fixture(RangeMarkup, RangeCss))
        {
            var hit = Hit(f.Document.VisibleFrame, "range"); Drag(f.Document, hit, .2); Equal(20.0, f.Document.PollEvents()[0].Item4.Item1, "first held range value");
            long oldRevision = f.Document.VisibleFrame.Revision; f.Controller.SetText("label", "Updated label"); f.Document.Update(1, true);
            Check(f.Document.VisibleFrame.Revision > oldRevision, "label update committed");
            Drag(f.Document, hit, .8); var events = f.Document.PollEvents(); Equal(1, events.Length, "held drag survives unrelated text commit without a new press");
            Equal(80.0, events[0].Item4.Item1, "continuous drag tracks new position"); Equal(f.Document.VisibleFrame.Revision, events[0].Item4.Item2, "continued event uses new committed revision");
        }
    }
    static void SourceReplacementCancelsGrab()
    {
        using (var f = new Fixture(RangeMarkup, RangeCss))
        {
            var hit = Hit(f.Document.VisibleFrame, "range"); Drag(f.Document, hit, .2); f.Document.PollEvents();
            Check(f.Document.Load(RangeMarkup, RangeCss, 400, 200), "same-shape source replacement admitted"); f.Document.Update(1, true);
            Drag(f.Document, hit, .8); Equal(0, f.Document.PollEvents().Length, "replacement source requires a new physical press even when hit metadata is equal");
            f.Document.Pointer(hit.Bounds.X + 80, MiddleY(hit), false); Drag(f.Document, hit, .8);
            Equal(1, f.Document.PollEvents().Length, "release and new press admits replacement range");
        }
    }
    static void ShapeAndBindingCancelGrab()
    {
        using (var f = new Fixture(RangeMarkup, RangeCss))
        {
            var hit = Hit(f.Document.VisibleFrame, "range"); Drag(f.Document, hit, .2); f.Document.PollEvents();
            Check(f.Controller.Resize(450, 200), "viewport resize is staged"); f.Document.Update(1, true);
            Equal(hit.Bounds.Width, Hit(f.Document.VisibleFrame, "range").Bounds.Width, "fixed-width range hotzone is unchanged by wider viewport");
            Drag(f.Document, hit, .8); Equal(0, f.Document.PollEvents().Length, "viewport identity change cancels held grab even with fixed hit bounds");
        }
        using (var f = new Fixture("<p id='label'>A</p>" + RangeMarkup.Replace("<p id='label'>A</p>", ""), "input {width:100px;height:24px;} p {width:100px;}"))
        {
            var hit = Hit(f.Document.VisibleFrame, "range"); Drag(f.Document, hit, .2); f.Document.PollEvents();
            f.Controller.SetText("label", "A much longer label that must wrap across several lines"); f.Document.Update(1, true);
            var moved = Hit(f.Document.VisibleFrame, "range"); Check(moved.Bounds.Y != hit.Bounds.Y, "same-source text edit moves range hotzone");
            Drag(f.Document, moved, .8); Equal(0, f.Document.PollEvents().Length, "changed hitshape from ordinary layout cancels held grab");
        }
        using (var f = new Fixture(RangeMarkup, RangeCss))
        {
            var hit = Hit(f.Document.VisibleFrame, "range"); Drag(f.Document, hit, .2); f.Document.PollEvents();
            f.Document.Load(RangeMarkup, "input {width:200px;height:24px;} p {height:24px;}", 400, 200); f.Document.Update(1, true);
            Drag(f.Document, Hit(f.Document.VisibleFrame, "range"), .8); Equal(0, f.Document.PollEvents().Length, "changed hit geometry cancels grab");
        }
        using (var f = new Fixture(RangeMarkup, RangeCss))
        {
            var hit = Hit(f.Document.VisibleFrame, "range"); Drag(f.Document, hit, .2); f.Document.PollEvents();
            f.Document.Load(RangeMarkup.Replace("gain-change", "replacement-action"), RangeCss, 400, 200); f.Document.Update(1, true);
            Drag(f.Document, hit, .8); Equal(0, f.Document.PollEvents().Length, "rebound action cancels grab");
        }
    }
    static void SuppressionClearsInput()
    {
        using (var f = new Fixture(RangeMarkup, RangeCss))
        {
            var hit = Hit(f.Document.VisibleFrame, "range"); Drag(f.Document, hit, .4); f.Document.SetInputEnabled(false);
            Equal(0, f.Document.PollEvents().Length, "input suppression discards previously queued events");
            Drag(f.Document, hit, .6); Equal(0, f.Document.PollEvents().Length, "suppressed pointer creates no events");
            f.Document.SetInputEnabled(true); Drag(f.Document, hit, .8);
            Equal(0, f.Document.PollEvents().Length, "re-enable while still held cannot reacquire range without release");
            f.Document.Pointer(hit.Bounds.X + 60, MiddleY(hit), false);
            Equal(0, f.Document.PollEvents().Length, "re-enable does not release a stale range press");
            Drag(f.Document, hit, .6); Equal(60.0, f.Document.PollEvents()[0].Item4.Item1, "fresh press works after re-enable");
        }
    }
    static void PointerAndDisposal()
    {
        var f = new Fixture(ButtonMarkup, ButtonCss); var hit = Hit(f.Document.VisibleFrame, "button");
        Reject<ArgumentException>(delegate { f.Document.Pointer(double.NaN, 0, false); }, "finite");
        Reject<ArgumentException>(delegate { f.Document.Pointer(0, double.PositiveInfinity, false); }, "finite");
        Reject<ArgumentException>(delegate { f.Document.Pointer(1000001, 0, false); }, "viewport");
        ClickAt(f.Document, hit.Bounds.X + hit.Bounds.Width, MiddleY(hit)); Equal(0, f.Document.PollEvents().Length, "right edge is exclusive");
        Click(f.Document, hit); f.Document.Dispose(); f.Document.Dispose(); Equal(1, f.Painter.Disposals, "document disposes painter once");
        Equal(0, f.Document.PollEvents().Length, "dispose clears queued events");
        Click(f.Document, hit); f.Document.Update(1, true); Check(f.Document.VisibleFrame == null && f.Document.PollEvents().Length == 0, "disposed document remains inert");
    }

    sealed class Bus : IDisposable
    {
        readonly IMyUtilities prior;
        readonly Dictionary<long, List<Action<object>>> handlers = new Dictionary<long, List<Action<object>>>();
        internal Func<string, object[], object> HtmlService;
        internal Func<string, object[], object> CoreService;
        internal bool Dedicated;
        internal int CoreRequests;
        internal int ChatSubscriptions;
        readonly List<string> unexpected = new List<string>();
        internal Bus()
        {
            prior = MyAPIGateway.Utilities;
            var utility = DispatchProxy.Create<IMyUtilities, HtmlHostProxy>();
            ((HtmlHostProxy)utility).Handler = Invoke;
            MyAPIGateway.Utilities = utility;
        }
        object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == "get_IsDedicated") return Dedicated;
            if (method.Name == "RegisterMessageHandler")
            {
                long channel = (long)args[0]; List<Action<object>> list;
                if (!handlers.TryGetValue(channel, out list)) handlers[channel] = list = new List<Action<object>>();
                list.Add((Action<object>)args[1]); return null;
            }
            if (method.Name == "UnregisterMessageHandler")
            {
                List<Action<object>> list; if (handlers.TryGetValue((long)args[0], out list)) list.Remove((Action<object>)args[1]); return null;
            }
            if (method.Name == "SendModMessage")
            {
                long channel = (long)args[0]; object message = args[1];
                if (channel == HdrHtmlApi.DiscoveryChannel) HtmlService = message as Func<string, object[], object>;
                if (channel == HdrModApi.RequestChannel)
                {
                    CoreRequests++; var callback = message as Action<Func<string, object[], object>>;
                    if (CoreService != null && callback != null) callback(CoreService);
                }
                Broadcast(channel, message); return null;
            }
            if (method.Name == "add_MessageEntered") { ChatSubscriptions++; return null; }
            if (method.Name == "remove_MessageEntered") { ChatSubscriptions--; return null; }
            if (method.Name == "ShowMessage") return null;
            unexpected.Add(method.Name);
            throw new Exception("Unexpected utilities call: " + method.Name);
        }
        internal void Broadcast(long channel, object value)
        {
            List<Action<object>> list;
            if (handlers.TryGetValue(channel, out list)) foreach (var handler in list.ToArray()) handler(value);
        }
        internal Func<string, object[], object> Open(string owner)
        { return (Func<string, object[], object>)HtmlService("open", new object[] { owner }); }
        internal int HandlerCount(long channel)
        { List<Action<object>> list; return handlers.TryGetValue(channel, out list) ? list.Count : 0; }
        public void Dispose() { MyAPIGateway.Utilities = prior; Check(unexpected.Count == 0, "utility proxy had no unexpected swallowed calls: " + string.Join(", ", unexpected.ToArray())); }
    }
    sealed class Surface
    {
        internal readonly IMyTextSurface Api;
        internal int Commits;
        internal bool FailDraw;
        internal ContentType Content = ContentType.SCRIPT;
        internal string Script = "";
        internal Vector2 Size = new Vector2(400, 200), Texture = new Vector2(400, 200);
        internal readonly List<MySprite> Sprites = new List<MySprite>();
        internal Surface()
        {
            Api = DispatchProxy.Create<IMyTextSurface, HtmlHostProxy>();
            ((HtmlHostProxy)Api).Handler = Invoke;
        }
        object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == "get_ContentType") return Content;
            if (method.Name == "get_Script") return Script;
            if (method.Name == "get_SurfaceSize") return Size;
            if (method.Name == "get_TextureSize") return Texture;
            if (method.Name == "MeasureStringInPixels") return new Vector2(((StringBuilder)args[0]).Length * 14 * (float)args[2], 28 * (float)args[2]);
            if (method.Name == "DrawFrame")
            {
                if (FailDraw) throw new InvalidOperationException("injected native DrawFrame failure");
                return new MySpriteDrawFrame(delegate(MySpriteDrawFrame frame) { Commits++; Sprites.Clear(); frame.AddToList(Sprites); });
            }
            throw new Exception("Unexpected LCD call: " + method.Name);
        }
    }
    static long Lcd(Func<string, object[], object> endpoint, Surface surface)
    { return (long)endpoint("create-lcd", new object[] { ButtonMarkup, ButtonCss, surface.Api, true }); }
    static void ServiceWithoutCore()
    {
        using (var bus = new Bus())
        {
            var session = new HtmlFrontendSession(); session.BeforeStart();
            Check(bus.HtmlService != null, "session announces actual service"); Equal("HDR.Html/0.1", (string)bus.HtmlService("version", new object[0]), "service version");
            using (var helper = new HdrHtmlApi("lcd-without-core"))
            {
                Check(helper.Ready, "HTML service can admit owner while optional HDR core is absent");
                Reject<ArgumentException>(delegate { helper.CreateHud(ButtonMarkup, ButtonCss, 400, 200); }, "Requires mod");
                Reject<ArgumentException>(delegate { helper.CreateWorld(ButtonMarkup, ButtonCss, 400, 200, MatrixD.Identity, .005); }, "Requires mod");
                var surface = new Surface(); long handle = helper.CreateNativeLcd(ButtonMarkup, ButtonCss, surface.Api, true);
                Check(surface.Commits == 1, "native document admission commits one frame");
                var state = helper.Status(handle); Check(state.Item1 == "native-lcd" && state.Item2 == state.Item3 && state.Item4.Item1, "native status reports committed visible frame without core dependency");
                helper.Pointer(handle, 20, 15, true); helper.Pointer(handle, 20, 15, false);
                Equal(1, helper.PollEvents(handle).Length, "owned native LCD events work without HDR core");
                for (int i = 0; i < 60; i++) session.UpdateAfterSimulation();
                Check(bus.CoreRequests >= 2, "actual host retries absent core during updates");
                Check(helper.Status(handle).Item3 > 0 && surface.Commits == 1, "native visible document stays committed without idle repaint during absent-core retry");
                Reject<ArgumentException>(delegate { helper.CreateNativeLcd(ButtonMarkup, ButtonCss, surface.Api, false); }, "own");
                helper.Destroy(handle); Reject<ArgumentException>(delegate { helper.Status(handle); }, "belong");
            }
        }
        using (var bus = new Bus())
        {
            bus.Dedicated = true; new HtmlFrontendSession().BeforeStart(); Check(bus.HtmlService == null, "dedicated server never advertises client frontend");
        }
    }
    static void ServiceOwnership()
    {
        using (var bus = new Bus())
        {
            var session = new HtmlFrontendSession(); session.BeforeStart();
            var a = bus.Open("owner-a"); var b = bus.Open("owner-b"); var surfaceA = new Surface(); var surfaceB = new Surface();
            long aHandle = Lcd(a, surfaceA), bHandle = Lcd(b, surfaceB);
            Check(aHandle != bHandle, "handles are globally distinct across live owners");
            Reject<ArgumentException>(delegate { b("status", new object[] { aHandle }); }, "belong");
            Reject<ArgumentException>(delegate { b("destroy", new object[] { aHandle }); }, "belong");
            Reject<ArgumentException>(delegate { b("data", new object[] { aHandle, "key", "value" }); }, "belong");
            Reject<ArgumentException>(delegate { b("text", new object[] { aHandle, "button", "foreign edit" }); }, "belong");
            Reject<ArgumentException>(delegate { b("resize", new object[] { aHandle, 300.0, 100.0 }); }, "belong");
            Reject<ArgumentException>(delegate { b("pointer", new object[] { aHandle, 20.0, 15.0, true }); }, "belong");
            Reject<ArgumentException>(delegate { b("poll-events", new object[] { aHandle }); }, "belong");
            Check(((MyTuple<string, long, long, MyTuple<bool, bool, string>, long>)a("status", new object[] { aHandle })).Item3 > 0, "foreign operation rejections leave original owner's document visible");
            var successor = bus.Open("owner-a"); Check(!(bool)a("valid", new object[0]), "replacement owner revokes old endpoint");
            Reject<ArgumentException>(delegate { a("status", new object[] { aHandle }); }, "revoked");
            long replacementHandle = Lcd(successor, new Surface()); Check(aHandle != replacementHandle, "replacement generation never reuses stale document handles");
            Reject<ArgumentException>(delegate { successor("status", new object[] { aHandle }); }, "belong");
            Check((bool)b("valid", new object[0]), "owner replacement preserves unrelated owner");
            b("clear-owned", new object[0]); Reject<ArgumentException>(delegate { b("status", new object[] { bHandle }); }, "belong");
            Check((bool)successor("valid", new object[0]), "clear-owned preserves other owner endpoint");
            successor("release", new object[0]); Check(!(bool)successor("valid", new object[0]), "release revokes endpoint");
            b("release", new object[0]);
            Reject<ArgumentException>(delegate { bus.Open("invalid owner"); }, "owner");
        }
    }
    static Func<string, object[], object> FakeHtmlService(Func<string, object[], object> endpoint)
    {
        return delegate(string command, object[] args)
        {
            if (command == "version") return "HDR.Html/0.1";
            if (command == "open") return endpoint;
            if (command == "capabilities") return new[] { "test" };
            throw new ArgumentException("Unexpected fake service command: " + command);
        };
    }
    sealed partial class Core
    {
        internal int GeometryPoints,GeometryPrimitives;
        internal bool Rendering = true;
        internal readonly Func<string, object[], object> Service;
        long nextContext;
        readonly HashSet<long> contexts = new HashSet<long>();
        internal Core()
        {
            Service = delegate(string command, object[] args)
            {
                if (command == "version") return "HDR.ModClient/1";
                if (command == "open") return new Func<string, object[], object>(Endpoint);
                throw new Exception("Unexpected fake core service command: " + command);
            };
        }
        object Endpoint(string command, object[] args)
        {
            object surfaceResult;if(TrySurfaceCommand(command,args,out surfaceResult))return surfaceResult;
            if (command == "valid") return true;
            if (command == "release") return true;
            if (command == "rendering-enabled") return Rendering;
            if (command == "measure-text")
            {
                string text = (string)args[0]; double size = (double)args[1], line = (double)args[2], advance = text.Length * size * .5;
                return new MyTuple<string, MyTuple<double, double, double>, MyTuple<double, double, double, double>, int, bool>(HtmlHdrTextMetrics.MetricSchema, new MyTuple<double, double, double>(advance, size, size * line), new MyTuple<double, double, double, double>(0, -.5 * size, advance, .5 * size), 1, text.Trim().Length > 0);
            }
            if (command == "geometry-cost") return new MyTuple<int, int>(4, 2);
            if (command == "geometry-usage") return new MyTuple<int, int>(0, 0);
            if (command == "geometry-limit-settings") return new MyTuple<int, int>(GeometryPoints, GeometryPrimitives);
            if (command == "geometry-limit") {int points=(int)args[0],primitives=(int)args[1];if(points<0||primitives<0)throw new ArgumentException("Geometry allowances cannot be negative.");GeometryPoints=points;GeometryPrimitives=primitives;return true;}
            if (command == "create-hud" || command == "create-world") { long context = ++nextContext; contexts.Add(context); return context; }
            if (command == "context-valid") return contexts.Contains((long)args[0]);
            if (command == "destroy") return contexts.Remove((long)args[0]);
            if (command == "mesh" || command == "text" || command == "svg" || command == "item-order" || command == "remove") {RecordArtwork(command,args);return true;}
            throw new Exception("Unexpected fake core endpoint command: " + command);
        }
    }
    static void ServiceRenderingSuppression()
    {
        using (var bus = new Bus())
        {
            var core = new Core(); bus.CoreService = core.Service;
            var session = new HtmlFrontendSession(); session.BeforeStart();
            using (var helper = new HdrHtmlApi("render-suppression"))
            {
                long handle = helper.CreateHud(ButtonMarkup, ButtonCss, 400, 200);
                helper.Pointer(handle, 20, 15, true); helper.Pointer(handle, 20, 15, false);
                core.Rendering = false; Equal(0, helper.PollEvents(handle).Length, "poll notices global hide immediately and clears queued click");
                helper.Pointer(handle, 20, 15, true); helper.Pointer(handle, 20, 15, false);
                session.UpdateAfterSimulation(); Equal(0, helper.PollEvents(handle).Length, "global hide suppresses both direct and update-driven input");
                core.Rendering = true; session.UpdateAfterSimulation();
                helper.Pointer(handle, 20, 15, true); helper.Pointer(handle, 20, 15, false);
                Equal(1, helper.PollEvents(handle).Length, "rendering re-enable restores fresh button input");
                helper.Pointer(handle, 20, 15, true); core.Rendering = false; session.UpdateAfterSimulation();
                core.Rendering = true; session.UpdateAfterSimulation(); helper.Pointer(handle, 20, 15, false);
                Equal(0, helper.PollEvents(handle).Length, "button held across global hide cannot click on release");
            }
        }
    }
    static void HelperAcquisitionFence()
    {
        using (var bus = new Bus())
        {
            int abandonedReleases = 0; bool reconnectDuringRelease = true;
            var newest = FakeHtmlService(delegate(string command, object[] args) { if (command == "valid" || command == "release") return true; if (command == "identity") return "newest"; throw new ArgumentException(command); });
            var outer = FakeHtmlService(delegate(string command, object[] args) { if (command == "valid") return true; if (command == "release") { abandonedReleases++; return true; } if (command == "identity") return "outer"; throw new ArgumentException(command); });
            var old = FakeHtmlService(delegate(string command, object[] args)
            {
                if (command == "valid") return true;
                if (command == "release") { if (reconnectDuringRelease) { reconnectDuringRelease = false; bus.Broadcast(HdrHtmlApi.DiscoveryChannel, newest); } return true; }
                if (command == "identity") return "old"; throw new ArgumentException(command);
            });
            using (var helper = new HdrHtmlApi("acquisition-fences"))
            {
                bus.Broadcast(HdrHtmlApi.DiscoveryChannel, old); bus.Broadcast(HdrHtmlApi.DiscoveryChannel, outer);
                Equal("newest", (string)helper.Call("identity"), "release-callback reconnect must win over superseded outer candidate");
                Equal(1, abandonedReleases, "superseded acquired endpoint released once");
                Equal(2L, helper.ConnectionGeneration, "abandoned outer candidate does not consume generation");
            }
        }
    }
    static void HelperCapabilitiesFence()
    {
        using (var bus = new Bus())
        {
            var next = FakeHtmlService(delegate(string command, object[] args) { if (command == "valid" || command == "release") return true; if (command == "identity") return "next"; throw new ArgumentException(command); });
            var endpoint = new Func<string, object[], object>(delegate(string command, object[] args) { if (command == "valid" || command == "release") return true; throw new ArgumentException(command); });
            var prior = new Func<string, object[], object>(delegate(string command, object[] args)
            {
                if (command == "version") return "HDR.Html/0.1"; if (command == "open") return endpoint;
                if (command == "capabilities") { bus.Broadcast(HdrHtmlApi.DiscoveryChannel, next); return new[] { "stale" }; }
                throw new ArgumentException(command);
            });
            using (var helper = new HdrHtmlApi("caps-fence"))
            {
                bus.Broadcast(HdrHtmlApi.DiscoveryChannel, prior);
                Reject<InvalidOperationException>(delegate { helper.Capabilities(); }, "changed during capabilities");
                Equal("next", (string)helper.Call("identity"), "successor remains live after rejected capabilities result");
            }
        }
    }
    static void HelperNestedAcquisitions()
    {
        foreach (string trigger in new[] { "version", "open" })
        {
            using (var bus = new Bus())
            {
                int staleReleases = 0; bool discover = true;
                var latest = FakeHtmlService(delegate(string command, object[] args) { if (command == "valid" || command == "release") return true; if (command == "identity") return "latest"; throw new ArgumentException(command); });
                var staleEndpoint = new Func<string, object[], object>(delegate(string command, object[] args) { if (command == "valid") return true; if (command == "release") { staleReleases++; return true; } if (command == "identity") return "stale"; throw new ArgumentException(command); });
                var candidate = new Func<string, object[], object>(delegate(string command, object[] args)
                {
                    if (command == trigger && discover) { discover = false; bus.Broadcast(HdrHtmlApi.DiscoveryChannel, latest); }
                    if (command == "version") return "HDR.Html/0.1"; if (command == "open") return staleEndpoint; throw new ArgumentException(command);
                });
                using (var helper = new HdrHtmlApi("nested-acquisition"))
                {
                    bus.Broadcast(HdrHtmlApi.DiscoveryChannel, candidate);
                    Equal("latest", (string)helper.Call("identity"), "nested " + trigger + " discovery wins");
                    Equal(1, staleReleases, "stale " + trigger + " acquisition releases its admitted endpoint"); Equal(1L, helper.ConnectionGeneration, "only newest " + trigger + " endpoint consumes generation");
                }
            }
        }
        using (var bus = new Bus())
        {
            int abandonedReleases = 0; HdrHtmlApi helper = null;
            var before = FakeHtmlService(delegate(string command, object[] args) { if (command == "valid") return true; if (command == "release") { helper.Dispose(); return true; } throw new ArgumentException(command); });
            var acquired = FakeHtmlService(delegate(string command, object[] args) { if (command == "valid") return true; if (command == "release") { abandonedReleases++; return true; } throw new ArgumentException(command); });
            using (helper = new HdrHtmlApi("dispose-acquisition"))
            {
                bus.Broadcast(HdrHtmlApi.DiscoveryChannel, before); bus.Broadcast(HdrHtmlApi.DiscoveryChannel, acquired);
                Check(!helper.Ready, "dispose during old release prevents acquired endpoint installation"); Equal(1, abandonedReleases, "endpoint admitted after disposal is immediately released"); Equal(1L, helper.ConnectionGeneration, "disposed acquisition does not consume generation");
            }
        }
    }
    static void HelperAbsentAndMalformedDiscovery()
    {
        using (var bus = new Bus())
        {
            using (var helper = new HdrHtmlApi("absent-helper"))
            {
                Check(!helper.Ready && helper.ConnectionGeneration == 0, "absent frontend has no connection"); Equal(0, helper.Capabilities().Length, "absent frontend has no capabilities");
                object result; string reason; Check(!helper.TryCall("anything", out result, out reason) && result == null && reason.IndexOf("Requires mod", StringComparison.Ordinal) >= 0, "absent frontend TryCall reports dependency failure");
                var good = FakeHtmlService(delegate(string command, object[] args) { if (command == "valid" || command == "release") return true; if (command == "identity") return "good"; throw new ArgumentException(command); });
                bus.Broadcast(HdrHtmlApi.DiscoveryChannel, good); Check(helper.Ready, "valid discovery connects helper");
                var malformed = new[]
                {
                    new Func<string, object[], object>(delegate(string command, object[] args) { return "wrong-version"; }),
                    new Func<string, object[], object>(delegate(string command, object[] args) { throw new InvalidOperationException("bad version"); }),
                    new Func<string, object[], object>(delegate(string command, object[] args) { if (command == "version") return "HDR.Html/0.1"; throw new InvalidOperationException("bad open"); }),
                    new Func<string, object[], object>(delegate(string command, object[] args) { if (command == "version") return "HDR.Html/0.1"; return null; })
                };
                foreach (var candidate in malformed) { bus.Broadcast(HdrHtmlApi.DiscoveryChannel, candidate); Equal("good", (string)helper.Call("identity"), "malformed discovery preserves current endpoint"); Equal(1L, helper.ConnectionGeneration, "malformed discovery consumes no generation"); }
                helper.Dispose(); helper.Dispose(); helper.Request(); Check(!helper.Ready && bus.HandlerCount(HdrHtmlApi.DiscoveryChannel) == 0, "helper disposal is idempotent and unregisters discovery");
            }
        }
    }
    static void ServiceAdmissionLimits()
    {
        using (var bus = new Bus())
        {
            var session = new HtmlFrontendSession(); session.BeforeStart(); var owner = bus.Open("quota-owner");
            var surface = new Surface();
            Reject<ArgumentException>(delegate { owner("create-lcd", new object[] { ButtonMarkup, ButtonCss, surface.Api, false }); }, "own");
            Reject<ArgumentException>(delegate { owner("create-lcd", new object[] { "<script>bad</script>", "", surface.Api, true }); }, "script");
            for (int i = 0; i < 4; i++) Lcd(owner, new Surface());
            Reject<ArgumentException>(delegate { Lcd(owner, new Surface()); }, "limit");
            owner("clear-owned", new object[0]); Check(Lcd(owner, new Surface()) > 0, "clear-owned returns document capacity");
            var endpoints = new List<Func<string, object[], object>>(); endpoints.Add(owner);
            for (int i = 1; i < 8; i++) endpoints.Add(bus.Open("quota-" + i));
            Reject<ArgumentException>(delegate { bus.Open("quota-overflow"); }, "limit");
            var replacement = bus.Open("quota-owner"); Check((bool)replacement("valid", new object[0]), "same owner replacement is admitted at full owner quota");
            Check(!(bool)owner("valid", new object[0]), "quota replacement revokes its predecessor");
            replacement("release", new object[0]); Check((bool)bus.Open("quota-after-release")("valid", new object[0]), "release returns owner capacity");
        }
    }
    static void SessionUnload()
    {
        using (var bus = new Bus())
        {
            var session = new HtmlFrontendSession(); session.AfterLoadData(); session.BeforeStart();
            using (var helper = new HdrHtmlApi("unload-owner"))
            {
                helper.CreateNativeLcd(ButtonMarkup, ButtonCss, new Surface().Api, true);
                Equal(1, bus.HandlerCount(HdrHtmlApi.RequestChannel), "frontend request handler registered"); Equal(1, bus.ChatSubscriptions, "demo chat handler registered");
                session.UnloadDataConditional();
                Check(!helper.Ready, "actual public unload revokes helper owner endpoint"); Equal(0, bus.HandlerCount(HdrHtmlApi.RequestChannel), "unload unregisters frontend request handler"); Equal(0, bus.ChatSubscriptions, "unload unregisters chat handler");
                Reject<ArgumentException>(delegate { bus.HtmlService("open", new object[] { "after-unload" }); }, "stopped");
            }
        }
    }
    static void NativeSurfaceLeases()
    {
        using (var bus = new Bus())
        {
            var session = new HtmlFrontendSession(); session.BeforeStart(); var owner = bus.Open("surface-owner"); var other = bus.Open("surface-other"); var surface = new Surface();
            long first = Lcd(owner, surface);
            Reject<ArgumentException>(delegate { Lcd(owner, surface); }, "already belongs");
            Reject<ArgumentException>(delegate { Lcd(other, surface); }, "already belongs");
            Check(surface.Sprites.Count > 0, "live native document publishes actual sprites");
            owner("destroy", new object[] { first }); Equal(0, surface.Sprites.Count, "destroy clears still-owned native sprite output");
            long next = Lcd(other, surface); Check(next != first, "destroy frees shared surface for another owner with a fresh handle");
            other("clear-owned", new object[0]); long reused = Lcd(owner, surface); Check(reused != next, "clear-owned frees native surface lease");
            var replacement = bus.Open("surface-owner"); Check(Lcd(replacement, surface) > reused, "same-owner generation replacement releases old surface lease before reusing");
            replacement("release", new object[0]); Check(Lcd(other, surface) > reused, "owner release frees native surface lease");
            var failedSurface = new Surface(); Reject<ArgumentException>(delegate { owner("create-lcd", new object[] { "<script>bad</script>", "", failedSurface.Api, true }); }, "revoked");
            Reject<ArgumentException>(delegate { other("create-lcd", new object[] { "<script>bad</script>", "", failedSurface.Api, true }); }, "script");
            Check(Lcd(other, failedSurface) > 0, "failed source admission leaves no orphan surface lease");
        }
    }
    static void NativeSurfaceAuthority()
    {
        using (var bus = new Bus())
        {
            var session = new HtmlFrontendSession(); session.BeforeStart();
            using (var helper = new HdrHtmlApi("native-authority"))
            {
                var surface = new Surface(); long handle = helper.CreateNativeLcd(ButtonMarkup, ButtonCss, surface.Api, true);
                helper.Pointer(handle, 20, 15, true); helper.Pointer(handle, 20, 15, false);
                surface.Content = ContentType.TEXT_AND_IMAGE; Equal(0, helper.PollEvents(handle).Length, "changed native mode discards queued events immediately");
                helper.Pointer(handle, 20, 15, true); helper.Pointer(handle, 20, 15, false); Equal(0, helper.PollEvents(handle).Length, "foreign native mode disables pointer input");
                surface.Content = ContentType.SCRIPT; surface.Script = "ForeignScript"; session.UpdateAfterSimulation();
                helper.Pointer(handle, 20, 15, true); helper.Pointer(handle, 20, 15, false); Equal(0, helper.PollEvents(handle).Length, "selected native surface script disables pointer input");
                surface.Script = ""; surface.Size = new Vector2(300, 200);
                helper.Pointer(handle, 20, 15, true); helper.Pointer(handle, 20, 15, false); Equal(0, helper.PollEvents(handle).Length, "native surface size mismatch disables old authored hit regions");
                surface.Size = new Vector2(400, 200); helper.Pointer(handle, 20, 15, true); helper.Pointer(handle, 20, 15, false);
                Equal(1, helper.PollEvents(handle).Length, "restored native mode and dimensions permit fresh input");
                helper.Pointer(handle, 20, 15, true); surface.Content = ContentType.TEXT_AND_IMAGE; session.UpdateAfterSimulation();
                surface.Content = ContentType.SCRIPT; session.UpdateAfterSimulation(); helper.Pointer(handle, 20, 15, false);
                Equal(0, helper.PollEvents(handle).Length, "native authority loss cancels previously held press");
                surface.Content = ContentType.TEXT_AND_IMAGE; int commits = surface.Commits;
                helper.Destroy(handle); Equal(commits, surface.Commits, "document retirement does not draw cleanup over foreign native content mode");
                Check(surface.Sprites.Count > 0, "foreign-mode output is preserved on document retirement");
            }
        }
    }
    static void NativeDrawFailureStatus()
    {
        using (var bus = new Bus())
        {
            var session = new HtmlFrontendSession(); session.BeforeStart();
            using (var helper = new HdrHtmlApi("native-draw-failure"))
            {
                var surface = new Surface(); long handle = helper.CreateNativeLcd(ButtonMarkup, ButtonCss, surface.Api, true);
                surface.FailDraw = true; helper.SetText(handle, "button", "Next"); session.UpdateAfterSimulation();
                var failed = helper.Status(handle); Check(failed.Item3 == 0 && failed.Item4.Item2, "actual native DrawFrame fault retires visible hits and remains pending");
                Check(failed.Item4.Item3.IndexOf("DrawFrame failure", StringComparison.Ordinal) >= 0, "actual native failure status exposes painter reason");
                helper.Pointer(handle, 20, 15, true); helper.Pointer(handle, 20, 15, false); Equal(0, helper.PollEvents(handle).Length, "retired actual native frame cannot produce pointer events");
                session.UpdateAfterSimulation();
                Check(helper.Status(handle).Item4.Item3.IndexOf("DrawFrame failure", StringComparison.Ordinal) >= 0, "retry delay preserves actual native paint error while content mode and dimensions remain valid");
                surface.FailDraw = false; for (int i = 0; i < 59; i++) session.UpdateAfterSimulation();
                Check(helper.Status(handle).Item3 > 0 && helper.Status(handle).Item4.Item3 == "", "successful actual native retry republishes and clears its error");
            }
        }
    }
    static string PaintedText(HtmlPaintFrame frame)
    {
        var text = new StringBuilder(); foreach (var operation in frame.Operations) if (operation.Kind == "text") text.Append(operation.Text); return text.ToString();
    }
    static void RebuildEditState()
    {
        using (var f = new Fixture(ButtonMarkup, ButtonCss))
        {
            Check(f.Document.SetText("button", "Saved"), "hosted saved text stages"); f.Document.Update(1, true);
            Equal("Saved", f.Document.TextEdits["button"], "successful layout persists text edit for endpoint rebuild");
            Check(f.Document.SetText("button", "Poison"), "candidate text stages"); f.Metrics.Fail = true; f.Document.Update(2, true);
            Equal("Saved", f.Document.TextEdits["button"], "failed layout cannot poison renderer rebuild text cache");
            f.Metrics.Fail = false; f.Document.Suspend("test renderer changed"); Check(f.Document.RebuildForRenderer(), "committed text state rebuilds"); f.Document.Update(3, true);
            Equal("Saved", PaintedText(f.Document.VisibleFrame), "endpoint replacement restores successful text rather than rejected candidate");
            f.Document.SetText("button", "Queued"); f.Document.Suspend("second renderer change"); Check(f.Document.RebuildForRenderer(), "pending hosted edit survives suspension before update"); f.Document.Update(4, true);
            Equal("Queued", PaintedText(f.Document.VisibleFrame), "pending text is included in renderer rebuild"); Equal("Queued", f.Document.TextEdits["button"], "successfully rebuilt queued text becomes committed edit state");
        }
        using (var f = new Fixture("<button id='button' data-action='activate'>{{caption}}</button>", ButtonCss))
        {
            Check(f.Document.SetData("caption", "Bound"), "hosted bound text stages"); f.Document.Suspend("renderer changed before data update");
            Check(f.Document.RebuildForRenderer(), "pending data is retained through endpoint rebuild"); f.Document.Update(1, true);
            Equal("Bound", PaintedText(f.Document.VisibleFrame), "pending binding value appears in rebuilt visible frame");
        }
    }
    static void FailedQueuedDataRebuild()
    {
        using (var f = new Fixture(RangeMarkup, RangeCss))
        {
            f.Document.SetText("label", "Saved"); f.Document.Update(1, true);
            Equal("Saved", f.Document.TextEdits["label"], "valid text is committed before renderer change");
            Check(f.Document.SetText("label", "Rejected candidate"), "candidate text stages before reconnect");
            Check(f.Document.SetData("gain", "NaN"), "plain-text bound data is staged for layout validation");
            f.Document.Suspend("renderer changed"); Check(!f.Document.RebuildForRenderer(), "invalid bound range candidate fails renderer rebuild");
            Equal("Saved", f.Document.TextEdits["label"], "rejected data batch cannot overwrite committed text edit cache");
            Equal("Saved", PaintedText(f.Controller.Frame), "rebuild first restores committed text before attempting bad pending data");
            Check(f.Document.LastError.IndexOf("bound range", StringComparison.OrdinalIgnoreCase) >= 0, "failed queued data exposes layout reason");
            f.Document.Update(2, true);
            Equal("Saved", PaintedText(f.Document.VisibleFrame), "painting the valid fallback preserves committed text");
            Check(f.Document.Status(true).Item4.Item3.IndexOf("bound range", StringComparison.OrdinalIgnoreCase) >= 0, "painting fallback does not hide rejected pending-data status error");
            f.Document.Update(3, true); Check(!string.IsNullOrEmpty(f.Document.Status(true).Item4.Item3), "idle update retains rebuild rejection reason");
            Check(f.Document.SetData("gain", "75"), "owner can repair rejected bound data"); f.Document.Update(4, true);
            Equal(75.0, Hit(f.Document.VisibleFrame, "range").Value, "valid corrected binding publishes desired range state");
            Check(f.Document.LastError == null, "successful repaired layout clears prior rebuild error");
            Equal("Saved", f.Document.TextEdits["label"], "repair retains committed text edit instead of rejected candidate");
        }
    }
    static void PendingResizeRebuild()
    {
        using (var f = new Fixture(ButtonMarkup, ButtonCss))
        {
            Check(f.Document.Resize(480, 240), "hosted viewport resize stages");
            Check(f.Document.SetText("button", "Resized"), "text can stage with hosted viewport change");
            f.Document.Suspend("renderer changed before resized layout"); Check(f.Document.RebuildForRenderer(), "pending viewport and text survive renderer suspension");
            Equal(480.0, f.Controller.Frame.Width, "rebuilt desired frame retains pending width"); Equal(240.0, f.Controller.Frame.Height, "rebuilt desired frame retains pending height");
            f.Document.Update(1, true); Equal(480.0, f.Document.VisibleFrame.Width, "successful repaint commits staged width"); Equal(240.0, f.Document.VisibleFrame.Height, "successful repaint commits staged height");
            Equal("Resized", PaintedText(f.Document.VisibleFrame), "co-staged text remains in resized renderer rebuild");
        }
    }
    static void HelperGenerationFences()
    {
        using (var bus = new Bus())
        {
            int oldReleases = 0; bool replaceOnValid = false, replaceOnCall = false;
            var successorEndpoint = new Func<string, object[], object>(delegate(string command, object[] args)
            { if (command == "valid") return true; if (command == "release") return true; if (command == "echo") return "successor"; throw new ArgumentException(command); });
            var successorService = FakeHtmlService(successorEndpoint);
            var oldEndpoint = new Func<string, object[], object>(delegate(string command, object[] args)
            {
                if (command == "valid") { if (replaceOnValid) { replaceOnValid = false; bus.Broadcast(HdrHtmlApi.DiscoveryChannel, successorService); } return true; }
                if (command == "release") { oldReleases++; return true; }
                if (command == "echo") { if (replaceOnCall) { replaceOnCall = false; bus.Broadcast(HdrHtmlApi.DiscoveryChannel, successorService); } return "old"; }
                throw new ArgumentException(command);
            });
            var oldService = FakeHtmlService(oldEndpoint);
            using (var helper = new HdrHtmlApi("helper-fences"))
            {
                bus.Broadcast(HdrHtmlApi.DiscoveryChannel, oldService); Check(helper.Ready, "initial helper endpoint ready"); Equal(1L, helper.ConnectionGeneration, "first connection generation");
                bus.Broadcast(HdrHtmlApi.DiscoveryChannel, oldService); Equal(1L, helper.ConnectionGeneration, "same valid service discovery is a no-op");
                replaceOnCall = true; Reject<InvalidOperationException>(delegate { helper.Call("echo"); }, "changed during");
                Equal(2L, helper.ConnectionGeneration, "reentrant command reconnect advances generation"); Check(helper.Ready, "new endpoint survives stale command result"); Equal("successor", (string)helper.Call("echo"), "new endpoint receives following commands");
                Equal(1, oldReleases, "reconnect releases prior endpoint once");
                bus.Broadcast(HdrHtmlApi.DiscoveryChannel, oldService); replaceOnValid = true;
                Check(!helper.Ready, "Ready result is fenced when its queried endpoint reconnects");
                Check(helper.Ready, "stale validity probe does not erase successor endpoint"); Equal(4L, helper.ConnectionGeneration, "both subsequent reconnects advance generation");
            }
        }
    }
}
