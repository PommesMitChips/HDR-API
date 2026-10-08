using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using Hdr.Html;
using HoloMap;
using VRage;
using VRageMath;

internal static partial class PbTests
{
    const string Markup = "<p id='caption' data-bind='caption'>Start</p><button id='go' data-action='launch'>Go</button><input id='gain' type='range' min='0' max='100' step='1' value='0' data-bind='gain' data-action='gain-change'/>";
    const string Css = "p {width:160px;height:24px;} button {width:100px;height:30px;} input {width:100px;height:24px;}";
    static int assertions, cases, failures;
    static readonly List<string> results = new List<string>();
    static void Check(bool ok, string why) { assertions++; if (!ok) throw new Exception(why); }
    static void Equal<T>(T wanted, T actual, string why) { Check(EqualityComparer<T>.Default.Equals(wanted, actual), why + "; expected " + wanted + ", got " + actual); }
    static void Near(double wanted, double actual, string why) { Check(Math.Abs(wanted - actual) < 1e-8, why + "; expected " + wanted.ToString("R", CultureInfo.InvariantCulture) + ", got " + actual.ToString("R", CultureInfo.InvariantCulture)); }
    static void Reject(Action action, string why) { try { action(); } catch (ArgumentException) { assertions++; return; } throw new Exception("Expected argument rejection: " + why); }
    static void Run(string name, Action test)
    {
        cases++;
        try { test(); results.Add("PASS " + name); Console.WriteLine(results.Last()); }
        catch (Exception error) { failures++; results.Add("FAIL " + name + ": " + error); Console.WriteLine(results.Last()); }
    }
    static HtmlDocumentController Layout(PbFixture bridge, string markup = Markup, string css = Css, double w = 240, double h = 160)
    {
        var controller = new HtmlDocumentController(new HtmlPbMetrics(bridge), new HtmlLimits { MaxHitRegions = 32 });
        Check(controller.TryLoad(markup, css, w, h), "Actual parser/layout admission: " + controller.LastError);
        return controller;
    }
    static void SourcesEqual(Dictionary<string, string> wanted, Dictionary<string, string> actual, string why)
    { Equal(wanted.Count, actual.Count, why + " count"); foreach (var pair in wanted) Check(actual.TryGetValue(pair.Key, out var source) && source == pair.Value, why + " " + pair.Key); }
    static UiWidget Range(PbFixture f) { return f.Display.Widgets.Single(w => w.Control.ValueId != null); }
    static UiWidget Button(PbFixture f) { return f.Display.Widgets.Single(w => w.Control.ValueId == null); }

    static void PublicationAndCoordinates()
    {
        using var f = new PbFixture();
        var controller = Layout(f, "<div id='box'></div><button id='go' data-action='launch'>Go</button>", "div {width:40px;height:20px;background:#ff0000;margin-left:10px;} button {width:80px;height:30px;margin-left:15px;}", 200, 100);
        var pose = MatrixD.CreateRotationZ(.3) * MatrixD.CreateTranslation(3, 4, 5); const double units = .005;
        using var painter = new HtmlPbPainter(1, f, pose, units);
        Check(painter.Paint(controller.Frame, controller.Document), "Actual PB draw publication: " + painter.LastError);
        Check(!ReferenceEquals(painter.Accepted.Frame, controller.Frame), "Published frame is a detached server snapshot");
        var red = controller.Frame.Operations.Single(op => op.Kind == "rect" && op.Color.R == 1 && op.Color.G == 0 && op.Color.B == 0);
        var body = painter.Accepted.Items.First(); var compiled = Svg.Parse(body.Source, 12).Geometry;
        var inverse = MatrixD.Invert(pose);
        var local = compiled.Points.Select(p => Vector3D.Transform(Vector3D.Transform(p, body.Pose), inverse)).ToArray();
        Near(red.Bounds.X * units, local.Min(p => p.X), "SVG viewBox recentering is compensated in model X");
        Near((red.Bounds.X + red.Bounds.Width) * units, local.Max(p => p.X), "Pixel width becomes model width");
        Near(-(red.Bounds.Y + red.Bounds.Height) * units, local.Min(p => p.Y), "SVG Y inversion preserves downward authored pixels");
        Near(-red.Bounds.Y * units, local.Max(p => p.Y), "Top edge follows model negative up axis");
        foreach (var point in compiled.Points)
        {
            var model = Vector3D.Transform(point, body.Pose); var pixels = Vector3D.Transform(model, inverse);
            var expected = pose.Translation + pose.Right * pixels.X + pose.Up * pixels.Y;
            Near(expected.X, model.X, "Model pose right/up axes X"); Near(expected.Y, model.Y, "Model pose right/up axes Y"); Near(expected.Z, model.Z, "Model pose translation Z");
        }
        var control = painter.Accepted.Controls.Single(); var widget = Button(f); var hit = controller.Frame.Hits.Single();
        var artwork = painter.Accepted.Items.Single(i => i.Id == control.Artwork);
        var center = Vector3D.Transform(new Vector3D(widget.X, widget.Y, 0), artwork.Pose);
        var expectedCenter = Vector3D.Transform(new Vector3D((hit.Bounds.X + hit.Bounds.Width / 2) * units, -(hit.Bounds.Y + hit.Bounds.Height / 2) * units, 0), pose);
        Near(expectedCenter.X, center.X, "Core artwork-local hotzone agrees with authored button X"); Near(expectedCenter.Y, center.Y, "Hotzone agrees with authored button Y"); Near(expectedCenter.Z, center.Z, "Hotzone agrees with model plane");
        Check(painter.Accepted.Items.Select(i => i.Id).SequenceEqual(painter.Accepted.Items.Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal)), "Zero-padded retained IDs preserve body/control LCD lexical order");
        Check(f.AccessChecks > 0, "Real public Core drawing checks target access");
    }
    static void CoreEventsAndGesture()
    {
        using var f = new PbFixture(); var c = Layout(f); using var painter = new HtmlPbPainter(2, f, MatrixD.Identity, .005);
        Check(painter.Paint(c.Frame, c.Document), "Interactive publication: " + painter.LastError);
        int costCalls = f.CostCalls, mutations = f.Mutations;
        Check(painter.Paint(c.Frame, c.Document), "Repeated paint accepts an unchanged real frame");
        Equal(costCalls, f.CostCalls, "Unchanged paint reuses actual compiled source costs"); Equal(mutations, f.Mutations, "Unchanged paint emits zero retained/UI mutations");
        var button = Button(f); Check(f.Click(button), "Actual core button click accepted");
        var click = painter.Poll(); Equal(1, click.Length, "Button produces one pollable event");
        Equal("click", click[0].Item1, "Button event kind"); Equal("go", click[0].Item2, "Opaque core ID maps to authored node"); Equal("launch", click[0].Item3, "Authored action remains event metadata"); Equal(77L, click[0].Item4.Item3, "Core player identity survives adapter");
        var range = Range(f); long definition = f.Display.Revision, source = range.Control.SourceRevision; string artwork = range.Control.Artwork;
        var rangeSource = f.Sources()["10:" + artwork]; long lease = f.Begin(range);
        var moved = f.Move(lease, 49.6); Near(50, moved.Item2, "Actual core snaps slider to canonical value");
        painter.SyncRanges(c); Check(c.Update(), "Core range value drives dirty authored fill/text");
        Check(painter.Paint(c.Frame, c.Document), "Value-only paint publishes: " + painter.LastError);
        Equal(definition, f.Display.Revision, "Value-only paint leaves gesture definition stable"); Equal(source, Range(f).Control.SourceRevision, "Value-only paint leaves range source metadata stable");
        Equal(rangeSource, f.Sources()["10:" + artwork], "Value-only fill repaint retains original constrained knob source"); Check(f.LeaseValid(lease), "Active core range gesture survives fill repaint");
        c.SetText("caption", "Unrelated"); Check(c.Update() && painter.Paint(c.Frame, c.Document), "Unrelated dirty label publishes");
        Equal(definition, f.Display.Revision, "Unrelated text paint does not rebuild UI declarations"); Check(f.LeaseValid(lease), "Gesture survives unrelated dirty text");
        var next = f.Move(lease, 64.4); var events = painter.Poll();
        Check(events.Any(e => e.Item1 == "change" && e.Item2 == "gain" && e.Item3 == "gain-change" && e.Item4.Item1 == 64 && e.Item4.Item2 == next.Item3 && e.Item4.Item3 == 77), "Range poll carries canonical value, core value revision, player and authored action");
        Equal(0, f.TryRuns, "Cloned HTML buttons and ranges never invoke PB TryRun directly");
        Equal(0, painter.Poll().Length, "Polling drains only owned events once");
    }
    static void FilteredQueuesAndCleanup()
    {
        using var f = new PbFixture(); var c = Layout(f);
        var a = new HtmlPbPainter(3, f, MatrixD.Identity, .005); var b = new HtmlPbPainter(4, f, MatrixD.Identity, .005);
        Check(a.Paint(c.Frame, c.Document) && b.Paint(c.Frame, c.Document), "Two documents share one PB/display safely");
        var aButton = f.Display.Widgets.Single(w => w.Id == a.Accepted.Controls.Single(v => v.Kind == "button").Id);
        var bButton = f.Display.Widgets.Single(w => w.Id == b.Accepted.Controls.Single(v => v.Kind == "button").Id);
        Check(f.Click(aButton) && f.Click(bButton), "Two real core click events queued");
        Equal(1, a.Poll().Length, "First document drains its prefix"); Equal(1, b.Poll().Length, "Other document event stays queued");
        f.Draw("line", "foreign", Vector3D.Zero, Vector3D.UnitX, "white"); f.Ui("value", "foreign-value", 1d, 0d, 10d, 1d);
        var before = f.Sources(); var bItems = b.Accepted.Items.Select(i => "10:" + i.Id).ToArray(); var bControlIds = b.Accepted.Controls.Select(v => v.Id).ToArray();
        a.Dispose();
        Check(f.Items.Contains("10:foreign"), "Destroy preserves foreign same-PB artwork");
        foreach (string id in bItems) Check(f.Items.Contains(id) && f.Sources()[id] == before[id], "Destroy preserves other document source " + id);
        Check(f.Display.Values.Any(v => v.Id == "foreign-value") && bControlIds.All(id => f.Display.Widgets.Any(w => w.Id == id)), "Destroy removes only owned controls and values");
        Check(a.Accepted == null && !f.Layers.Contains("10:ui-hp3_b"), "Destroy retires publication and removes owned UI layer");
        b.Dispose(); Check(f.Items.Count == 1 && f.Items.Contains("10:foreign"), "All document cleanup leaves only foreign artwork");
        Check(f.Display.Widgets.Count == 0 && f.Display.Values.Count == 1 && f.Display.Values[0].Id == "foreign-value", "All document cleanup leaves only foreign numeric state");
    }
    static void PreflightAndRollback()
    {
        using var f = new PbFixture(); var c = Layout(f); using var painter = new HtmlPbPainter(5, f, MatrixD.Identity, .005);
        Check(painter.Paint(c.Frame, c.Document), "Initial owned publication"); f.Draw("line", "foreign", Vector3D.Zero, Vector3D.UnitX, "white");
        var before = f.Sources(); var accepted = painter.Accepted; int mutations = f.Mutations;
        c.SetText("caption", "Next"); Check(c.Update(), "Changed frame prepared"); f.FailCost = true;
        Check(!painter.Paint(c.Frame, c.Document), "Cost preflight failure reports rejection"); Equal(mutations, f.Mutations, "Failed preflight emits zero declarations"); Check(ReferenceEquals(accepted, painter.Accepted), "Preflight retains old publication"); SourcesEqual(before, f.Sources(), "Preflight preserves retained sources");
        f.FailCost = false; f.FailSvgAt = f.SvgAttempts + 2; c.SetText("go", "Later"); Check(c.Update(), "Second changed source produces multi-call publication");
        Check(!painter.Paint(c.Frame, c.Document), "Injected second source emit fails");
        Check(!painter.Retired && ReferenceEquals(accepted, painter.Accepted), "Confirmed owned rollback retains previous publication"); SourcesEqual(before, f.Sources(), "Partial emit rollback restores owned sources and preserves foreign content");
        Check(painter.Paint(c.Frame, c.Document), "Retry publishes desired source after restored rollback");
        f.FailAllSvg = true; c.SetText("caption", "Uncertain"); Check(c.Update() && !painter.Paint(c.Frame, c.Document), "Unrestorable source failure reported");
        Check(painter.Retired && painter.Accepted == null, "Uncertain owned restoration retires publication");
        Equal(0, painter.Poll().Length, "Uncertain publication cannot return input events"); Check(f.Items.Contains("10:foreign"), "Failed cleanup preserves unrelated retained artwork");
    }
    static void RealDiscoveryAndBridgeLifetime()
    {
        foreach (string condition in new[] { "PB off", "PB disabled", "program changed", "target off", "caller replaced", "target replaced", "access lost", "construct changed" })
        {
            using var f = new PbFixture();
            Check(ReferenceEquals(f.CoreDraw("finddisplay", new object[] { "HTML Display" }), f.Target), "Target discovery runs real public HDR.Draw");
            var bridge = new HtmlPbBridge(f.Caller, f.Target); Check(bridge.Ready, "Actual bridge binds live caller/target");
            switch (condition)
            {
                case "PB off": f.Working = false; break;
                case "PB disabled": f.Enabled = false; break;
                case "program changed": f.Program = "replacement-script"; break;
                case "target off": f.TargetWorking = false; break;
                case "caller replaced": f.ReplaceCaller(); break;
                case "target replaced": f.ReplaceTarget(); break;
                case "access lost": f.Access = false; break;
                case "construct changed": f.SameConstruct = false; break;
            }
            int calls = f.DrawCalls + f.UiCalls;
            Check(!bridge.Ready, condition + " revokes HTML before a repaint");
            Reject(() => bridge.Draw("measure-text", "Changed", 12d, 1.3d), condition + " forbids preparation");
            Reject(() => bridge.Draw("svg", "dead", "<svg/>", MatrixD.Identity, 12), condition + " forbids mutation");
            Equal(calls, f.DrawCalls + f.UiCalls, condition + " reaches no core endpoint after retirement");
        }
    }

    static long Bind(Func<string, object[], object> endpoint, PbFixture f, string html = Markup, string css = Css)
    { return (long)endpoint("bind", new object[] { f.Target, html, css, 240d, 160d, MatrixD.Identity, .005d }); }
    static MyTuple<string, long, long, MyTuple<bool, bool, string>, long> Status(Func<string, object[], object> endpoint, long handle)
    { return (MyTuple<string, long, long, MyTuple<bool, bool, string>, long>)endpoint("status", new object[] { handle }); }
    static void ActualSessionPublicationAndCoalescing()
    {
        using var f = new PbFixture(); f.CaptureHtmlProperty(); var session = new HtmlPbSession(); session.AfterLoadData(); session.BeforeStart(); session.BeforeStart();
        try
        {
            Equal(1, f.AddedHtmlProperties, "Server registers one actual HDR.Html property"); var endpoint = f.HtmlEndpoint();
            Equal("HDR.HtmlPB/0.1", (string)endpoint("version", new object[0]), "Discovered session protocol");
            Check((bool)endpoint("valid", new object[0]), "Discovered owner endpoint is live");
            var capabilities = (string[])endpoint("capabilities", new object[0]);
            Check(capabilities.Contains("server-layout") && capabilities.Contains("input-requires-client-renderer-0.9.13"), "Capabilities state server authority and existing pointer plugin prerequisite");
            string markup = Markup.Replace("<p id='caption' data-bind='caption'>Start</p>", "<p id='caption'>{{caption}}</p>");
            long handle = Bind(endpoint, f, markup); var initial = Status(endpoint, handle);
            Equal("projector", initial.Item1, "Status reflects real display capability kind");
            Check(initial.Item3 > 0 && initial.Item2 == initial.Item3 && initial.Item4.Item1 && !initial.Item4.Item2, "Published SERVER revision is available before any viewer acknowledgement");
            session.UpdateAfterSimulation(); session.UpdateAfterSimulation();
            var before = Status(endpoint, handle); var sources = f.Sources(); int costs = f.CostCalls, mutations = f.Mutations;
            Check(!(bool)endpoint("replace", new object[] { handle, "<script>bad()</script>", "", 240d, 160d }), "Unsupported source fails through actual HDR.Html command");
            var rejected = Status(endpoint, handle);
            Equal(before.Item2, rejected.Item2, "Malformed source preserves desired revision"); Equal(before.Item3, rejected.Item3, "Malformed source preserves published revision");
            Check(rejected.Item4.Item3.Contains("script", StringComparison.OrdinalIgnoreCase), "Source rejection reaches actual status error"); SourcesEqual(sources, f.Sources(), "Malformed source preserves retained Core publication");
            Equal(mutations, f.Mutations, "Malformed source performs no retained/UI write"); Equal(costs, f.CostCalls, "Malformed source never reaches geometry preflight");
            for (int i = 0; i < 20; i++) session.UpdateAfterSimulation();
            Equal(before.Item5, Status(endpoint, handle).Item5, "Idle server ticks do no relayout"); Equal(costs, f.CostCalls, "Idle server ticks compile no geometry"); Equal(mutations, f.Mutations, "Idle server ticks perform no paint");
            Check(!(bool)endpoint("text", new object[] { handle, "go", "Go" }) && !(bool)endpoint("resize", new object[] { handle, 240d, 160d }), "Unchanged text and dimensions are public no-ops");
            Check((bool)endpoint("data", new object[] { handle, "caption", "Intermediate" }) && (bool)endpoint("data", new object[] { handle, "caption", "Final" }), "Binding edits queue a coalesced final value");
            endpoint("resize", new object[] { handle, 230d, 150d }); endpoint("resize", new object[] { handle, 240d, 160d });
            Equal(before.Item3, Status(endpoint, handle).Item3, "Pending edits do not claim a published revision");
            session.UpdateAfterSimulation(); var committed = Status(endpoint, handle);
            Equal(before.Item5 + 1, committed.Item5, "Data and resize batch builds one layout"); Equal(before.Item2 + 1, committed.Item2, "Batch consumes one desired revision"); Equal(committed.Item2, committed.Item3, "Committed server publication catches desired revision");
            Check(f.Sources().Values.Any(v => v != null && v.Contains("Final", StringComparison.Ordinal)), "Final binding reaches actual Core SVG declaration");
            costs = f.CostCalls; mutations = f.Mutations;
            Check(!(bool)endpoint("data", new object[] { handle, "caption", "Final" }), "Repeated final data is a public no-op");
            session.UpdateAfterSimulation(); Equal(committed.Item5, Status(endpoint, handle).Item5, "Repeated final data does not build"); Equal(costs, f.CostCalls, "Repeated final data does not cost compile"); Equal(mutations, f.Mutations, "Repeated final data does not paint");
            Reject(() => endpoint("status", new object[] { (int)handle }), "Handle ABI requires Int64");
            Reject(() => endpoint("bind", new object[] { "HTML Display", Markup, Css, 240d, 160d, MatrixD.Identity }), "Adapter bind takes a real discovered block");
        }
        finally { session.UnloadDataConditional(); }
    }
    static void ActualSessionUnloadAndOwnership()
    {
        using var f = new PbFixture(); f.CaptureHtmlProperty(); var session = new HtmlPbSession(); session.AfterLoadData(); session.BeforeStart(); var endpoint = f.HtmlEndpoint();
        long handle = Bind(endpoint, f);
        f.Draw("line", "foreign", Vector3D.Zero, Vector3D.UnitX, "white"); f.Ui("value", "foreign-value", 1d, 0d, 10d, 1d);
        var outside = new HtmlPbPainter(99, f, MatrixD.Identity, .005); var c = Layout(f); Check(outside.Paint(c.Frame, c.Document), "Independent adapter namespace on same PB/display");
        var outsideItems = outside.Accepted.Items.Select(i => "10:" + i.Id).ToArray();
        session.UnloadDataConditional();
        Check(!(bool)endpoint("valid", new object[0]), "Public session unload revokes captured handler");
        Reject(() => endpoint("version", new object[0]), "Stale handler version fails after unload"); Reject(() => endpoint("status", new object[] { handle }), "Stale document handler fails after unload");
        Equal(1, f.RemovedHtmlProperties, "Unload unregisters actual terminal property");
        Check(f.Items.Contains("10:foreign") && outsideItems.All(id => f.Items.Contains(id)), "Actual session unload removes only its owned retained sources");
        Check(f.Display.Values.Any(v => v.Id == "foreign-value") && outside.Accepted.Controls.All(control => f.Display.Widgets.Any(w => w.Id == control.Id)), "Unload preserves other namespaces' numeric values and UI");
        Check(!f.Layers.Contains("10:ui-hp" + handle.ToString("x", CultureInfo.InvariantCulture) + "_b"), "Unload removes its owned UI layer"); outside.Dispose();
        Check(f.Items.Count == 1 && f.Display.Widgets.Count == 0 && f.Display.Values.Count == 1, "Complete teardown leaves precisely foreign retained state");
        using var client = new PbFixture(); client.Server = false; client.CaptureHtmlProperty(); var inactive = new HtmlPbSession(); inactive.AfterLoadData(); inactive.BeforeStart();
        Equal(0, client.AddedHtmlProperties, "Viewing client does not register server HTML property"); Check(client.HtmlEndpoint() == null, "Viewing client cannot discover an authoritative adapter"); inactive.UnloadDataConditional();
    }
    static void ActualSessionRetiresBeforeRepaint()
    {
        foreach (string condition in new[] { "PB off", "program changed", "target off", "caller replaced", "target replaced" })
        {
            using var f = new PbFixture(); f.CaptureHtmlProperty(); var session = new HtmlPbSession(); session.AfterLoadData(); session.BeforeStart(); var endpoint = f.HtmlEndpoint(); long handle = Bind(endpoint, f);
            endpoint("text", new object[] { handle, "caption", "Pending repaint" }); int costs = f.CostCalls, svgAttempts = f.SvgAttempts;
            switch (condition)
            {
                case "PB off": f.Working = false; break;
                case "program changed": f.Program = "new-script"; break;
                case "target off": f.TargetWorking = false; break;
                case "caller replaced": f.ReplaceCaller(); break;
                case "target replaced": f.ReplaceTarget(); break;
            }
            session.UpdateAfterSimulation(); Equal(costs, f.CostCalls, condition + " retires before geometry preflight"); Equal(svgAttempts, f.SvgAttempts, condition + " retires before SVG repaint");
            Reject(() => endpoint("status", new object[] { handle }), condition + " invalidates old handle immediately");
            session.UnloadDataConditional();
        }
    }
    static void LcdModeAdmission()
    {
        using var f = new PbFixture(true); f.CaptureHtmlProperty(); var session = new HtmlPbSession(); session.AfterLoadData(); session.BeforeStart(); var endpoint = f.HtmlEndpoint();
        try
        {
            f.LcdContent = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            Reject(() => Bind(endpoint, f), "LCD content mode must already select HDR API"); Equal(0, f.Items.Count, "Rejected LCD bind never claims retained scene content");
            f.LcdContent = VRage.Game.GUI.TextPanel.ContentType.SCRIPT; f.LcdScript = "OtherScript";
            Reject(() => Bind(endpoint, f), "Foreign LCD script must not be overwritten"); Equal("OtherScript", f.LcdScript, "Binding cannot autoselect HDR API");
            f.LcdScript = "HDR API"; long handle = Bind(endpoint, f); Equal("lcd", Status(endpoint, handle).Item1, "Already configured HDR LCD is accepted");
            f.LcdScript = "OtherScript"; int costs = f.CostCalls, emits = f.SvgAttempts; session.UpdateAfterSimulation();
            Equal(costs, f.CostCalls, "Foreign LCD script disables preparation"); Equal(emits, f.SvgAttempts, "Foreign LCD script disables repaint"); Reject(() => endpoint("status", new object[] { handle }), "LCD ownership change retires document");
            Equal("OtherScript", f.LcdScript, "Retirement preserves changed LCD script");
        }
        finally { session.UnloadDataConditional(); }
    }
    static void CanonicalDemoAdmission()
    {
        string path = typeof(PbTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "HtmlPbDemoPath").Value;
        var source = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("class Program {" + File.ReadAllText(path) + "}").GetRoot();
        string Literal(string name)
        {
            var variable = source.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>().Single(v => v.Identifier.ValueText == name);
            return ((Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax)variable.Initializer.Value).Token.ValueText;
        }
        string markup = Literal("Markup"), css = Literal("Stylesheet");
        foreach (string kind in new[] { "projector", "console", "lcd" })
        {
            using var f = new PbFixture(kind == "lcd"); if (kind == "console") f.TargetSubtype = "LargeConsole";
            f.CaptureHtmlProperty(); var session = new HtmlPbSession(); session.AfterLoadData(); session.BeforeStart(); var endpoint = f.HtmlEndpoint();
            try
            {
                long handle = (long)endpoint("bind", new object[] { f.Target, markup, css, 480d, 280d, MatrixD.Identity, .005d });
                var status = Status(endpoint, handle); Equal(kind, status.Item1, "Canonical demo target capability " + kind);
                Check(status.Item2 == status.Item3 && status.Item3 > 0 && !status.Item4.Item2, "Canonical authored demo admits under real geometry and overlap grants on " + kind);
                Check(f.Display.Widgets.Count == 2 && f.Display.Widgets.Count(w => w.Control.Draggable) == 1, "Canonical demo publishes button and range on " + kind);
                var range = Range(f); Near(60, f.Display.Values.Single(v => v.Id == range.Control.ValueId).Value, "Canonical demo range initial value on " + kind);
            }
            finally { session.UnloadDataConditional(); }
        }
    }

    static int Main(string[] args)
    {
        Run("real SVG compiler pixel/model/hotzone coordinates", PublicationAndCoordinates);
        Run("real core button/range events and retained active gesture", CoreEventsAndGesture);
        Run("prefix event isolation and owned teardown", FilteredQueuesAndCleanup);
        Run("preflight, multi-call rollback and uncertain retirement", PreflightAndRollback);
        Run("public target discovery and immediate bridge lifetime fences", RealDiscoveryAndBridgeLifetime);
        Run("actual HDR.Html server publication, source rejection and update coalescing", ActualSessionPublicationAndCoalescing);
        Run("actual session unload isolates ownership and revokes captured handlers", ActualSessionUnloadAndOwnership);
        Run("actual session lifetime ends before queued repaint", ActualSessionRetiresBeforeRepaint);
        Run("LCD admission requires existing HDR content mode", LcdModeAdmission);
        Run("canonical whole PB demo admits on LCD, Console and Projector", CanonicalDemoAdmission);
        Run("PB HTML unlimited geometry sentinel and explicit finite budget", UnlimitedPbGeometry);
        RunAuthoring();
        RunNative();
        string summary = "PB HTML tests: " + assertions + " assertions; " + cases + " cases, " + failures + " failures; actual frontend/Core compilers and retained/UI delegates against installed SE assemblies.";
        Console.WriteLine(summary);
        if (args.Length > 0)
        {
            Directory.CreateDirectory(args[0]);
            File.WriteAllText(Path.Combine(args[0], "RESULTS.txt"), string.Join(Environment.NewLine, results) + Environment.NewLine + summary + Environment.NewLine);
        }
        return failures == 0 ? 0 : 1;
    }
    static void UnlimitedPbGeometry()
    {
        var html=new StringBuilder();for(int i=0;i<16;i++)html.Append("<button id='dense-").Append(i).Append("'>").Append(new string('A',64)).Append("</button>");
        const string css="button {width:600px;height:24px;padding:0;font-size:8px;}";
        using(var f=new PbFixture())
        {
            var budget=(MyTuple<int,int,int>)f.Draw("budget-settings");Equal(0,budget.Item1,"Default PB point allowance is unlimited");Equal(0,budget.Item2,"Default PB primitive allowance is unlimited");
            var controller=Layout(f,html.ToString(),css,800,1000);using(var painter=new HtmlPbPainter(91,f,MatrixD.Identity,.005))
            {
                Check(painter.Paint(controller.Frame,controller.Document),"Actual PB compiler accepts dense controls under unlimited scene geometry: "+painter.LastError);
                Check(painter.Accepted.Items.Sum(i=>i.Points)>8192&&painter.Accepted.Items.Sum(i=>i.Primitives)>8192,"Actual compiled PB artwork exceeds both earlier aggregate grants");Equal(16,painter.Accepted.Controls.Count,"Unlimited geometry preserves the independent retained-object structural cap");
            }
        }
        using(var f=new PbFixture())
        {
            f.Draw("budget",8192,8192,20000);var controller=Layout(f,html.ToString(),css,800,1000);using(var painter=new HtmlPbPainter(92,f,MatrixD.Identity,.005))
            {int before=f.Mutations;Check(!painter.Paint(controller.Frame,controller.Document),"Explicit finite PB allowance still rejects excessive aggregate geometry");Equal(before,f.Mutations,"Finite PB preflight rejection mutates no artwork/control declaration");Check(painter.Accepted==null,"Finite PB preflight allocates no published state");}
        }
    }
}
