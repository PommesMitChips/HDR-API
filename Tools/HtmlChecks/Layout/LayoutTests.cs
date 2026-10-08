using System;
using System.Collections.Generic;
using Hdr.Html;

// Pure layout oracle. Deliberately uses an invented proportional font: these tests
// prove injected-metric behavior, not fidelity to Inter or any game renderer font.
internal sealed class ExactFixtureMetrics : IHtmlTextMetrics
{
    public string Profile { get { return "FixtureMetrics/1:proportional:cap-height"; } }
    public int Calls, MaximumRun;
    public int Generation = 7;
    public readonly List<string> Runs = new List<string>();

    public HtmlTextMeasurement Measure(string text, double size, double lineHeight)
    {
        if (text == null) throw new ArgumentNullException("text");
        if (text.Length > 64) throw new InvalidOperationException("Backend run exceeds 64 UTF-16 units.");
        if (text.Length > 0 && char.IsLowSurrogate(text[0]))
            throw new InvalidOperationException("Run begins inside a surrogate pair.");
        if (text.Length > 0 && char.IsHighSurrogate(text[text.Length - 1]))
            throw new InvalidOperationException("Run ends inside a surrogate pair.");
        Calls++; MaximumRun = Math.Max(MaximumRun, text.Length); Runs.Add(text);
        double advance = 0, minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 == text.Length || !char.IsLowSurrogate(text[i + 1]))
                    throw new InvalidOperationException("Invalid paired text.");
                cp = char.ConvertToUtf32(text[i], text[++i]);
            }
            double width = cp == 'i' ? .25 : cp == 'W' ? 1.0 : cp == ' ' || cp == 160 ? .3 : cp == '\t' ? 1.2 : cp > 65535 ? 1.1 : .6;
            if (cp != ' ' && cp != 160 && cp != '\t')
            {
                minX = Math.Min(minX, advance + .04 * size);
                maxX = Math.Max(maxX, advance + (width - .03) * size);
            }
            advance += width * size;
        }
        bool hasInk = minX != double.PositiveInfinity;
        return new HtmlTextMeasurement {
            Advance = advance, CapHeight = size, LineAdvance = size * lineHeight,
            Ink = hasInk ? new HtmlRect(minX, -.08 * size, maxX - minX, 1.12 * size) : new HtmlRect(0, 0, 0, 0),
            HasInk = hasInk, Generation = Generation
        };
    }
}

internal sealed class ChangingGenerationMetrics : IHtmlTextMetrics
{
    private readonly ExactFixtureMetrics _inner = new ExactFixtureMetrics();
    public string Profile { get { return _inner.Profile; } }
    public HtmlTextMeasurement Measure(string text, double size, double lineHeight)
    {
        var result = _inner.Measure(text, size, lineHeight);
        result.Generation = _inner.Calls;
        return result;
    }
}

internal sealed class TemporarilyUnavailableMetrics : IHtmlTextMetrics
{
    private readonly ExactFixtureMetrics _inner = new ExactFixtureMetrics();
    public bool Unavailable;
    public string Profile { get { return _inner.Profile; } }
    public HtmlTextMeasurement Measure(string text, double size, double lineHeight)
    {
        if (Unavailable) throw new InvalidOperationException("Fixture font backend unavailable.");
        return _inner.Measure(text, size, lineHeight);
    }
}

// Relabels the deterministic fixture to exercise backend/font admission only.
// Neither profile string turns these invented metrics into real renderer data.
internal sealed class ProfiledFixtureMetrics : IHtmlTextMetrics
{
    private readonly ExactFixtureMetrics _inner = new ExactFixtureMetrics();
    private readonly string _profile;
    public ProfiledFixtureMetrics(string profile) { _profile = profile; }
    public string Profile { get { return _profile; } }
    public int Calls { get { return _inner.Calls; } }
    public HtmlTextMeasurement Measure(string text, double size, double lineHeight) { return _inner.Measure(text, size, lineHeight); }
}

internal static class LayoutTests
{
    private static int _assertions;
    private static int _failures;

    private static void Main()
    {
        Run("metric oracle", ExactMetricOracle);
        Run("dimensions and spacing", DimensionsAndSpacing);
        Run("flex", FlexLayout);
        Run("text and wrapping", ProportionalTextAndWrapping);
        Run("white-space", WhiteSpaceModes);
        Run("long-run safety", LongRunSafety);
        Run("clips and input", ClipsAndInput);
        Run("admission", AdmissionFailures);
        Run("controller", ControllerAtomicUpdates);
        Run("forced lines and repeated flex layout", ForcedLinesAndFlexRelayout);
        Run("percent spacing and flex min/max", PercentSpacingAndFlexLimits);
        Run("unmatched and hidden invalid CSS", InvalidUnusedCss);
        Run("metric generation consistency", MetricGenerationConsistency);
        Run("metric provider failure recovery", MetricProviderFailureRecovery);
        Run("flex maximum freeze", FlexMaximumFreeze);
        Run("deep flex measurement work", DeepFlexMeasurementWork);
        Run("heading and font inheritance", HeadingAndFontInheritance);
        Run("percentage flex edges", PercentageFlexEdges);
        Run("nonbreaking spaces", NonbreakingSpaces);
        Run("malformed text admission", MalformedTextAdmission);
        Run("data storage ceilings", DataStorageCeilings);
        Run("reverted pending no-op", RevertedPendingNoOp);
        Run("nested flex padding placement", NestedFlexPaddingPlacement);
        Run("height auto casing and subnormal range step", HeightAutoAndSubnormalStep);
        Run("selected font backend admission", SelectedFontBackendAdmission);
        Run("range handle authored radius", RangeHandleAuthoredRadius);
        Console.WriteLine("HTML pure layout: " + _assertions + " assertions; " + _failures + " failed groups.");
        if (_failures > 0) Environment.ExitCode = 1;
    }

    private static void Run(string name, Action fixture)
    {
        try { fixture(); }
        catch (Exception error) { _failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }

    private static HtmlPaintFrame Build(string markup, double width, double height, ExactFixtureMetrics metrics = null, HtmlLimits limits = null)
    {
        limits = limits ?? new HtmlLimits();
        HtmlDocument document = HtmlParser.Parse(markup, "", limits);
        return HtmlLayout.Build(document, width, height, metrics ?? new ExactFixtureMetrics(), new Dictionary<string, string>(), limits);
    }

    private static List<HtmlPaintOperation> Of(HtmlPaintFrame frame, string nodeId, string kind)
    {
        var found = new List<HtmlPaintOperation>();
        foreach (var operation in frame.Operations)
            if (operation.NodeId == nodeId && (kind == null || operation.Kind == kind)) found.Add(operation);
        return found;
    }

    private static HtmlPaintOperation First(HtmlPaintFrame frame, string nodeId, string kind)
    {
        List<HtmlPaintOperation> found = Of(frame, nodeId, kind);
        True(found.Count > 0, "Paint operation for " + nodeId + "/" + kind);
        return found[0];
    }

    private static void True(bool value, string message)
    {
        _assertions++;
        if (!value) throw new Exception("Assertion failed: " + message);
    }

    private static void Near(double expected, double actual, string message)
    {
        _assertions++;
        if (Math.Abs(expected - actual) > 1e-7) throw new Exception(message + ": expected " + expected + ", actual " + actual);
    }

    private static void Rect(HtmlRect actual, double x, double y, double width, double height, string message)
    {
        Near(x, actual.X, message + " x"); Near(y, actual.Y, message + " y");
        Near(width, actual.Width, message + " width"); Near(height, actual.Height, message + " height");
    }

    private static void Reject(Action action, string message)
    {
        bool threw = false;
        try { action(); } catch (ArgumentException) { threw = true; } catch (InvalidOperationException) { threw = true; }
        True(threw, message);
    }

    private static void ExactMetricOracle()
    {
        var m = new ExactFixtureMetrics();
        Near(5, m.Measure("ii", 10, 1.3).Advance, "Narrow fixture glyphs");
        Near(20, m.Measure("WW", 10, 1.3).Advance, "Wide fixture glyphs");
        Near(13, m.Measure("W", 10, 1.3).LineAdvance, "Injected line advance");
        True(!m.Measure("   ", 10, 1.3).HasInk, "Whitespace advance does not invent ink");
        Near(11, m.Measure("\uD83D\uDE00", 10, 1.3).Advance, "Single surrogate glyph advance");
        Reject(delegate { m.Measure(new string('W', 65), 10, 1.3); }, "Fixture enforces backend maximum run");
        Reject(delegate { m.Measure("\uD83D", 10, 1.3); }, "Fixture rejects split surrogate tail");
    }

    private static void DimensionsAndSpacing()
    {
        var f = Build("<div id='outer' style='width:200px;padding:10px;border:2px solid #ffffff;background:#222222'><div id='inner' style='width:50%;height:20px;margin:3px;background:#ff0000'></div></div>", 400, 200);
        Rect(First(f, "outer", "rect").Bounds, 0, 0, 224, 50, "Content-box auto parent with padding and border");
        Rect(First(f, "inner", "rect").Bounds, 15, 15, 100, 20, "Percent width uses parent content width, margin offsets child");
        f = Build("<div id='auto' style='padding:10px;border:2px solid #ffffff;margin:3px;background:#ff0000'></div>", 100, 200);
        Rect(First(f, "auto", "rect").Bounds, 3, 3, 94, 24, "Auto block width fills available border box after margins");
    }

    private static void FlexLayout()
    {
        var f = Build("<div style='display:flex;width:200px;height:60px;gap:10px;align-items:center'><div id='a' style='width:20px;height:20px;flex-grow:1;background:#ff0000'></div><div id='b' style='width:20px;height:40px;flex-grow:3;background:#00ff00'></div></div>", 300, 200);
        Rect(First(f, "a", "rect").Bounds, 0, 20, 57.5, 20, "Row grow1 + centered cross axis");
        Rect(First(f, "b", "rect").Bounds, 67.5, 10, 132.5, 40, "Row grow3 + gap");
        f = Build("<div style='display:flex;flex-direction:column;width:80px;height:100px;gap:10px;justify-content:center;align-items:flex-end'><div id='a' style='width:20px;height:20px;background:#ff0000'></div><div id='b' style='width:30px;height:30px;background:#00ff00'></div></div>", 100, 200);
        Rect(First(f, "a", "rect").Bounds, 60, 20, 20, 20, "Column centered total stack/end cross axis A");
        Rect(First(f, "b", "rect").Bounds, 50, 50, 30, 30, "Column centered total stack/end cross axis B");
        f = Build("<div style='display:flex;width:100px;justify-content:space-between'><div id='a' style='width:20px;height:10px;background:#ff0000'></div><div id='b' style='width:20px;height:10px;background:#00ff00'></div><div id='c' style='width:20px;height:10px;background:#0000ff'></div></div>", 100, 100);
        Near(0, First(f, "a", "rect").Bounds.X, "Space-between first");
        Near(40, First(f, "b", "rect").Bounds.X, "Space-between middle");
        Near(80, First(f, "c", "rect").Bounds.X, "Space-between last");
    }

    private static void ProportionalTextAndWrapping()
    {
        var m = new ExactFixtureMetrics();
        var f = Build("<div id='text' style='width:24px;font-size:10px'>WW ii WW</div>", 100, 100, m);
        List<HtmlPaintOperation> lines = Of(f, "text", "text");
        True(lines.Count == 3, "Exact wide/narrow glyph metrics produce three lines");
        True(lines[0].Text == "WW" && lines[1].Text == "ii" && lines[2].Text == "WW", "Word wrapping preserves ordered words");
        Near(13, lines[1].Bounds.Y - lines[0].Bounds.Y, "Injected metric line advance used between rows");
        Near(20, lines[0].Bounds.Width, "Text paint advance uses exact wide metrics");
        Near(5, lines[1].Bounds.Width, "Text paint advance uses exact narrow metrics");
        True(f.FontProfile == m.Profile, "Frame reports injected font profile");
        f = Build("<div id='center' style='width:50px;font-size:10px;text-align:center'>Wi</div>", 100, 100);
        Near(18.75, First(f, "center", "text").Bounds.X, "Center aligns exact proportional run rather than glyph count");
        f = Build("<div id='right' style='width:50px;font-size:10px;text-align:right'>Wi</div>", 100, 100);
        Near(37.5, First(f, "right", "text").Bounds.X, "Right aligns exact proportional run");
        f = Build("<div id='broken' style='width:15px;font-size:10px'>WWW</div>", 100, 100);
        lines = Of(f, "broken", "text");
        True(lines.Count == 3, "Unbreakable oversized word is split into bounded lines");
        foreach (var line in lines) True(line.Text == "W" && line.Bounds.Width <= 15, "Oversized word split preserves glyphs and usable width");
        f = Build("<div id='mixed' style='width:30px;font-size:10px'>i <span id='wide' style='color:#ff0000'>W</span></div>", 100, 100);
        var narrow = First(f, "mixed", "text");
        var wide = First(f, "wide", "text");
        Near(0, narrow.Bounds.X, "Mixed inline first run origin");
        Near(5.5, wide.Bounds.X, "Mixed inline next style run continues from exact whitespace advance");
        Near(1, wide.Color.R, "Inline style color is preserved");
        Near(0, wide.Color.G, "Inline style excludes inherited green");
    }

    private static void WhiteSpaceModes()
    {
        var f = Build("<div id='pre' style='white-space:pre;font-size:10px'> i  W\nWW</div>", 100, 100);
        var runs = Of(f, "pre", "text");
        True(runs.Count == 2, "Pre preserves explicit line separator");
        True(runs[0].Text == " i  W" && runs[1].Text == "WW", "Pre preserves repeated/leading spaces");
        f = Build("<div id='nowrap' style='width:10px;white-space:nowrap;font-size:10px'>WW   ii</div>", 100, 100);
        runs = Of(f, "nowrap", "text");
        True(runs.Count == 1 && runs[0].Text == "WW ii", "Nowrap collapses spaces while retaining overflow");
        Near(28, runs[0].Bounds.Width, "Nowrap preserves exact overflow advance");
    }

    private static void LongRunSafety()
    {
        var metrics = new ExactFixtureMetrics();
        string text = new string('i', 63) + "\uD83D\uDE00" + new string('W', 69);
        var f = Build("<div id='long' style='white-space:nowrap;font-size:10px'>" + text + "</div>", 2000, 100, metrics);
        var runs = Of(f, "long", "text");
        string rejoined = "";
        double expectedX = 0;
        foreach (var run in runs)
        {
            True(run.Text.Length <= 64, "Every paint text run fits real backend limit");
            True(!char.IsLowSurrogate(run.Text[0]) && !char.IsHighSurrogate(run.Text[run.Text.Length - 1]), "Paint run boundary preserves surrogate pairs");
            Near(expectedX, run.Bounds.X, "Chunk origins use accumulated exact advance");
            expectedX += metrics.Measure(run.Text, 10, 1.3).Advance;
            rejoined += run.Text;
        }
        True(rejoined == text, "Long paint runs rejoin exactly");
        Near(858.5, expectedX, "Full long text advance includes narrow, astral and wide glyphs");
        True(metrics.MaximumRun <= 64, "All layout measurement calls respect backend bound");
    }

    private static void ClipsAndInput()
    {
        var f = Build("<div style='width:40px;height:20px;overflow:hidden'><button id='button' data-action='go' style='width:60px;height:30px;background:#ff0000'>Go</button></div>", 100, 100);
        var box = First(f, "button", "rect");
        True(box.HasClip, "Overflow clip reaches visual paint");
        Rect(box.Clip, 0, 0, 40, 20, "Parent content clip");
        True(f.Hits.Count == 1 && f.Hits[0].NodeId == "button" && f.Hits[0].Action == "go", "Button action uses explicit declared binding");
        Rect(f.Hits[0].Bounds, 0, 0, 40, 20, "Input hit is intersected with actual visible clip");
        f = Build("<input id='range' type='range' min='-1' max='3' step='0.25' value='1' data-bind='power' style='width:100px;height:20px'/>", 200, 100);
        True(f.Hits.Count == 1 && f.Hits[0].Binding == "power", "Range records canonical data binding");
        Near(-1, f.Hits[0].Minimum, "Range minimum"); Near(3, f.Hits[0].Maximum, "Range maximum");
        Near(.25, f.Hits[0].Step, "Range snap"); Near(1, f.Hits[0].Value, "Range initial value");
        for (int i = 1; i < f.Operations.Count; i++) True(f.Operations[i].Order > f.Operations[i - 1].Order, "Paint order strictly increasing");
        f = Build("<div style='width:40px;height:40px;overflow:hidden;padding:5px'><div style='width:60px;height:20px;overflow:hidden;margin-left:10px'><button id='nested' style='width:100px;height:40px;background:#ff0000'>Nested</button></div></div>", 200, 200);
        box = First(f, "nested", "rect");
        Rect(box.Clip, 15, 5, 35, 20, "Nested padding-box overflow intersected in logical coordinates");
        Rect(f.Hits[0].Bounds, 15, 5, 35, 20, "Nested padding-box clip also bounds hit regions");
        f = Build("<div style='width:10px;height:0;overflow:hidden'><button id='hidden'>Hidden</button></div>", 100, 100);
        True(f.Hits.Count == 0, "Zero-area clipping cannot leave interactive invisible hits");
    }

    private static void AdmissionFailures()
    {
        Reject(delegate { Build("<div style='position:absolute'></div>", 100, 100); }, "Unimplemented CSS rejected explicitly");
        Reject(delegate { Build("<div style='opacity:.5'><div style='background:#ff0000'></div></div>", 100, 100); }, "Group opacity rejected rather than incorrectly multiplying children");
        Reject(delegate { Build("<div></div>", double.NaN, 100); }, "Nonfinite viewport rejected");
        Reject(delegate { Build("<div></div>", 100, -1); }, "Negative viewport rejected");
        Reject(delegate { Build("<input id='bad' type='range' min='3' max='-1'/>", 100, 100); }, "Inverted range rejected");
        Reject(delegate { Build("<input id='bad' type='range' min='0' max='1' step='-1'/>", 100, 100); }, "Negative range snap rejected");
        Reject(delegate { Build("<div id='a' style='height:10px;background:#ff0000'></div><div id='b' style='height:10px;background:#00ff00'></div>", 100, 100, null, new HtmlLimits { MaxPaintOperations = 1 }); }, "Paint output bound enforced");
        Reject(delegate { Build("<button id='a'>A</button><button id='b'>B</button>", 100, 100, null, new HtmlLimits { MaxHitRegions = 1 }); }, "Input output bound enforced");
    }

    private static void ControllerAtomicUpdates()
    {
        var controller = new HtmlDocumentController(new ExactFixtureMetrics(), new HtmlLimits());
        True(controller.TryLoad("<div id='status' style='font-size:10px'>{{message}}</div>", "", 100, 100), "Controller accepts bounded document");
        HtmlPaintFrame original = controller.Frame;
        long revision = controller.Revision;
        long builds = controller.LayoutBuildCount;
        True(!controller.Update(), "No-op update does not re-layout");
        True(ReferenceEquals(original, controller.Frame), "No-op retains exact committed frame");
        True(controller.LayoutBuildCount == builds && controller.Revision == revision, "No-op leaves builds/revision unchanged");
        True(!controller.Resize(100, 100), "Unchanged viewport does not mark dirty");
        True(controller.SetData("message", "WW"), "Changed bound text queues update");
        True(controller.IsDirty, "Data update marks document dirty");
        True(ReferenceEquals(original, controller.Frame), "Queued update cannot mutate visible frame");
        True(controller.Update(), "Queued data commits");
        True(controller.LayoutBuildCount == builds + 1 && controller.Revision == revision + 1, "Single dirty update builds once and increments revision once");
        Near(20, First(controller.Frame, "status", "text").Bounds.Width, "Committed data uses exact injected metrics");
        True(!controller.SetData("message", "WW") && !controller.IsDirty, "Same canonical data is a no-op");
        var copy = controller.GetData();
        copy["message"] = "Mutated outside controller";
        True((string)controller.GetData()["message"] == "WW", "Data getter copy preserves controller authority");
        HtmlDocument committedDocument = controller.Document;
        HtmlPaintFrame committedFrame = controller.Frame;
        revision = controller.Revision;
        True(!controller.TryLoad("<script>alert(1)</script>", "", 100, 100), "Failed parse reports false");
        True(!string.IsNullOrEmpty(controller.LastError), "Failed parse has explicit error");
        True(ReferenceEquals(committedDocument, controller.Document) && ReferenceEquals(committedFrame, controller.Frame), "Failed replacement retains committed document and frame");
        True(controller.Revision == revision && (string)controller.GetData()["message"] == "WW", "Failed replacement cannot mutate data/revision");
        True(controller.Resize(80, 100), "Valid viewport change queued");
        True(!controller.Resize(double.NaN, 100) && controller.IsDirty, "Rejected resize cannot discard previously valid pending resize");
        True(!controller.SetText("missing", "hello") && controller.IsDirty, "Rejected text target cannot discard previously valid pending resize");
        True(controller.Update(), "Resize commits");
        Near(80, controller.Frame.Width, "Resize uses new logical viewport");
        committedFrame = controller.Frame;
        True(!controller.Resize(double.NaN, 100) && !string.IsNullOrEmpty(controller.LastError), "Invalid viewport reports failure before mutation");
        True(ReferenceEquals(committedFrame, controller.Frame) && !controller.IsDirty, "Invalid resize retains complete output");
        True(!controller.SetText("missing", "hello") && !string.IsNullOrEmpty(controller.LastError), "Unknown text target cannot mutate document");
        True(controller.SetText("status", "iii"), "Direct text replacement queues change");
        True(controller.Update(), "Direct text update commits atomically");
        True(First(controller.Frame, "status", "text").Text == "iii", "Direct text replacement uses the new literal node content");
        True((string)controller.GetData()["message"] == "WW", "Direct text replacement does not overwrite independent canonical data");

        var callerLimits = new HtmlLimits { MaxPaintOperations = 2 };
        var limited = new HtmlDocumentController(new ExactFixtureMetrics(), callerLimits);
        callerLimits.MaxPaintOperations = 200;
        True(limited.TryLoad("<div id='short' style='font-size:10px;white-space:nowrap'>W</div>", "", 2000, 100), "Limited document starts usable");
        committedFrame = limited.Frame;
        revision = limited.Revision;
        True(limited.SetText("short", new string('W', 193)), "Oversized queued text remains detached");
        True(!limited.Update(), "Output budget failure rejects entire pending update even after caller mutates original limits");
        True(ReferenceEquals(committedFrame, limited.Frame) && limited.Revision == revision && !limited.IsDirty, "Failed dirty build retains old frame/revision and retires pending transaction");
        True(limited.SetText("short", "ii") && limited.Update(), "Controller remains usable after admission failure");
        Near(5, First(limited.Frame, "short", "text").Bounds.Width, "Subsequent complete output succeeds");
        True(limited.SetText("short", "<script>") && limited.Update(), "Text insertion is literal data, never reparsed as markup");
        True(First(limited.Frame, "short", "text").Text == "<script>", "Literal text insertion preserves authored characters");
    }

    private static void ForcedLinesAndFlexRelayout()
    {
        var f = Build("<div id='lines' style='font-size:10px'>i<br/>W<br/><br/>ii</div>", 100, 100);
        var lines = Of(f, "lines", "text");
        True(lines.Count == 3, "br forces line breaks without emitting visible text for blank line");
        Near(0, lines[0].Bounds.Y, "br first line cap top");
        Near(13, lines[1].Bounds.Y, "br second line cap top");
        Near(39, lines[2].Bounds.Y, "Repeated br preserves intervening blank line");
        string markup = "<div style='display:flex;width:90px;gap:10px'><div id='a' style='font-size:10px;flex-grow:1'>WW ii WW</div><div id='b' style='width:20px;height:20px;background:#ff0000'></div></div>";
        HtmlDocument document = HtmlParser.Parse(markup, "", new HtmlLimits());
        var metrics = new ExactFixtureMetrics();
        f = HtmlLayout.Build(document, 100, 100, metrics, new Dictionary<string, string>(), new HtmlLimits());
        var again = HtmlLayout.Build(document, 100, 100, metrics, new Dictionary<string, string>(), new HtmlLimits());
        True(f.Operations.Count == again.Operations.Count, "Repeated flex text measure/place does not accumulate generated groups");
        for (int i = 0; i < f.Operations.Count; i++)
        {
            True(f.Operations[i].Key == again.Operations[i].Key && f.Operations[i].Text == again.Operations[i].Text, "Repeated flex layout preserves ordered stable operation identities");
            Rect(again.Operations[i].Bounds, f.Operations[i].Bounds.X, f.Operations[i].Bounds.Y, f.Operations[i].Bounds.Width, f.Operations[i].Bounds.Height, "Repeated flex layout preserves geometry");
        }
    }

    private static void PercentSpacingAndFlexLimits()
    {
        var f = Build("<div style='width:200px;height:100px'><div id='percent' style='width:50%;height:50%;padding:10%;background:#ff0000'></div></div>", 400, 200);
        Rect(First(f, "percent", "rect").Bounds, 0, 0, 140, 90, "Percent padding uses parent content width on both axes; percent height uses definite parent height");
        f = Build("<div style='display:flex;width:100px'><div id='a' style='width:10px;min-width:60px;flex-grow:1;height:10px;background:#ff0000'></div><div id='b' style='width:10px;flex-grow:1;height:10px;background:#00ff00'></div></div>", 100, 100);
        Rect(First(f, "a", "rect").Bounds, 0, 0, 60, 10, "Flex min-width freezes constrained grow item");
        Rect(First(f, "b", "rect").Bounds, 60, 0, 40, 10, "Flex remaining grow redistributed after minimum freeze");
    }

    private static void InvalidUnusedCss()
    {
        Reject(delegate {
            var limits = new HtmlLimits();
            var doc = HtmlParser.Parse("<div id='ok'>W</div>", "#missing { width: 1em; }", limits);
            HtmlLayout.Build(doc, 100, 100, new ExactFixtureMetrics(), new Dictionary<string, string>(), limits);
        }, "Unmatched unsupported CSS units cannot survive the static profile validation");
        Reject(delegate { Build("<div style='display:none;width:1em'>Hidden</div>", 100, 100); }, "Hidden authored invalid CSS still rejected");
    }

    private static void MetricGenerationConsistency()
    {
        Reject(delegate {
            var limits = new HtmlLimits();
            var doc = HtmlParser.Parse("<div style='font-size:10px;white-space:nowrap'>" + new string('W', 129) + "</div>", "", limits);
            HtmlLayout.Build(doc, 2000, 100, new ChangingGenerationMetrics(), new Dictionary<string, string>(), limits);
        }, "Font metrics generation cannot change halfway through one committed frame");
    }

    private static void MetricProviderFailureRecovery()
    {
        var metrics = new TemporarilyUnavailableMetrics();
        var controller = new HtmlDocumentController(metrics, new HtmlLimits());
        True(controller.TryLoad("<div id='text'>W</div>", "", 100, 100), "Available metric provider loads document");
        HtmlPaintFrame committed = controller.Frame;
        long revision = controller.Revision;
        True(controller.SetText("text", "ii"), "Metric failure test queues detached text");
        metrics.Unavailable = true;
        True(!controller.Update(), "Unavailable metric provider produces contained failure");
        True(ReferenceEquals(committed, controller.Frame) && controller.Revision == revision && !controller.IsDirty, "Unavailable metric provider leaves last complete visual and resets pending changes");
        True(!string.IsNullOrEmpty(controller.LastError), "Metric backend failure has an explicit error");
        metrics.Unavailable = false;
        True(controller.SetText("text", "ii") && controller.Update(), "Document remains usable after metrics return");
    }

    private static void FlexMaximumFreeze()
    {
        var f = Build("<div style='display:flex;width:100px'><div id='a' style='width:10px;max-width:30px;flex-grow:1;height:10px;background:#ff0000'></div><div id='b' style='width:10px;flex-grow:1;height:10px;background:#00ff00'></div></div>", 100, 100);
        Rect(First(f, "a", "rect").Bounds, 0, 0, 30, 10, "Flex max-width freezes constrained grow item");
        Rect(First(f, "b", "rect").Bounds, 30, 0, 70, 10, "Flex remaining grow redistributed after maximum freeze");
    }

    private static void DeepFlexMeasurementWork()
    {
        int[] depths = { 10, 20 };
        int firstCalls = 0;
        foreach (int depth in depths)
        {
            string markup = "<div id='glyph' style='font-size:10px'>W</div>";
            for (int i = 0; i < depth; i++) markup = "<div style='display:flex'>" + markup + "</div>";
            var metrics = new ExactFixtureMetrics();
            HtmlPaintFrame frame;
            try { frame = Build(markup, 100, 100, metrics); }
            catch (ArgumentException error) { throw new Exception("Deep flex depth " + depth + ": " + error.Message + " Metric calls=" + metrics.Calls); }
            Near(10, First(frame, "glyph", "text").Bounds.Width, "Deep flex preserves exact glyph width");
            True(metrics.Calls <= 16, "Deep flex caches repeated exact measurements instead of repeating them per placement");
            if (firstCalls == 0) firstCalls = metrics.Calls;
            else True(metrics.Calls <= firstCalls + 4, "Doubling nested flex depth keeps font measurement work bounded");
        }
    }

    private static void HeadingAndFontInheritance()
    {
        var frame = Build("<h1 id='one'>W</h1><h2 id='two'>W</h2>", 300, 200);
        Near(32, First(frame, "one", "text").FontSize, "h1 default cap-height preserved into text node");
        Near(24, First(frame, "two", "text").FontSize, "h2 default cap-height preserved into text node");
        frame = Build("<div style='font-size:32px'><span id='larger' style='font-size:150%'>W</span></div>", 300, 200);
        Near(48, First(frame, "larger", "text").FontSize, "Percent font size uses inherited parent cap-height");
        Near(48, First(frame, "larger", "text").Bounds.Width, "Percent font size drives exact matching width");
    }

    private static void PercentageFlexEdges()
    {
        var frame = Build("<div style='display:flex;width:200px'><div id='padded' style='width:20px;height:10px;padding:10%;background:#ff0000'></div><div id='sibling' style='width:20px;height:10px;background:#00ff00'></div></div>", 400, 200);
        Rect(First(frame, "padded", "rect").Bounds, 0, 0, 60, 50, "Flex child's 10 percent padding uses 200px parent, not 400px frame");
        Near(60, First(frame, "sibling", "rect").Bounds.X, "Flex placement includes correctly resolved percentage padding edges");
    }

    private static void NonbreakingSpaces()
    {
        var frame = Build("<div id='nbsp' style='font-size:10px'>W&nbsp;&nbsp;W</div>", 100, 100);
        var text = First(frame, "nbsp", "text");
        True(text.Text == "W\u00a0\u00a0W", "Two nonbreaking spaces retain their characters and are not collapsed");
        Near(26, text.Bounds.Width, "Nonbreaking spaces use exact preserved fixture advances");
        frame = Build("<div id='nbsp' style='font-size:10px;width:23px'>W&nbsp;W</div>", 100, 100);
        True(Of(frame, "nbsp", "text").Count == 1 && First(frame, "nbsp", "text").Text == "W\u00a0W", "Nonbreaking space does not introduce a word-break opportunity");
    }

    private static void MalformedTextAdmission()
    {
        var controller = new HtmlDocumentController(new ExactFixtureMetrics(), new HtmlLimits());
        True(controller.TryLoad("<div id='text'>W</div>", "", 100, 100), "Malformed-data test starts with valid output");
        var old = controller.Frame;
        True(!controller.SetData("bad", "\uD83D") && !controller.IsDirty, "Unpaired high surrogate rejected before pending data mutation");
        True(!controller.SetData("bad", "\uDE00") && !controller.IsDirty, "Unpaired low surrogate rejected before pending data mutation");
        True(!controller.SetData("\uD83D", "W"), "Malformed key rejected before pending data mutation");
        True(!controller.SetText("text", "\uD83D") && !controller.IsDirty, "Unpaired text surrogate rejected before pending text mutation");
        True(ReferenceEquals(old, controller.Frame) && controller.GetData().Count == 0, "Rejected Unicode leaves committed output and data untouched");
        var metrics = new ExactFixtureMetrics();
        var limits = new HtmlLimits();
        var document = HtmlParser.Parse("<div>{{value}}</div>", "", limits);
        Reject(delegate { HtmlLayout.Build(document, 100, 100, metrics, new Dictionary<string, string> { { "value", "\uD83D" } }, limits); }, "Direct layout rejects malformed substituted text before painting");
        True(metrics.Calls == 0, "Malformed substitution rejected before any metric-provider call");
        Reject(delegate { HtmlLayout.Build(document, 100, 100, metrics, new Dictionary<string, string> { { "value", new string('W', 16385) } }, limits); }, "Direct layout rejects oversized substitution before painting");
        True(metrics.Calls == 0, "Oversized substitution rejected before any metric-provider call");
    }

    private static void DataStorageCeilings()
    {
        var controller = new HtmlDocumentController(new ExactFixtureMetrics(), new HtmlLimits());
        True(controller.TryLoad("<div>W</div>", "", 100, 100), "Key budget test starts valid");
        for (int i = 0; i < 512; i++) True(controller.SetData("key-" + i, "W"), "Binding key admitted within 512-key limit");
        True(!controller.SetData("over-limit", "W"), "513th key rejected");
        True(controller.Update() && controller.GetData().Count == 512, "Key limit failure preserves all earlier valid pending keys");
        True(controller.SetData("key-0", "ii") && controller.Update(), "Existing key update remains possible at key ceiling");
        var total = new HtmlDocumentController(new ExactFixtureMetrics(), new HtmlLimits());
        True(total.TryLoad("<div>W</div>", "", 100, 100), "Character budget test starts valid");
        for (int i = 0; i < 8; i++) True(total.SetData("big-" + i, new string('W', 16384)), "Binding admitted through aggregate exact boundary");
        True(!total.SetData("over-char-limit", "i"), "Aggregate 131073rd character rejected");
        True(total.Update() && total.GetData().Count == 8, "Character limit failure preserves exact 131072-character pending transaction");
        True(total.SetData("big-0", "W") && total.SetData("freed", "ii") && total.Update(), "Shrinking existing value frees aggregate storage for new values");
    }

    private static void RevertedPendingNoOp()
    {
        var controller = new HtmlDocumentController(new ExactFixtureMetrics(), new HtmlLimits());
        True(controller.TryLoad("<div id='text'>W</div>", "", 100, 100), "Reversion test starts valid");
        True(controller.SetData("message", "W") && controller.Update(), "Reversion test commits initial canonical value");
        long builds = controller.LayoutBuildCount, revision = controller.Revision;
        var frame = controller.Frame;
        True(controller.SetData("message", "ii") && controller.SetData("message", "W"), "Data change then revert accepted");
        True(!controller.IsDirty && !controller.Update(), "Reverted data does not request layout");
        True(controller.SetText("text", "ii") && controller.SetText("text", "W"), "Text change then revert accepted");
        True(!controller.IsDirty && !controller.Update(), "Reverted text does not request layout");
        True(controller.Resize(80, 80) && controller.Resize(100, 100), "Viewport change then revert accepted");
        True(!controller.IsDirty && !controller.Update(), "Reverted viewport does not request layout");
        True(controller.LayoutBuildCount == builds && controller.Revision == revision && ReferenceEquals(frame, controller.Frame), "Reverted pending changes retain frame/revision with zero extra layout builds");
    }

    private static void NestedFlexPaddingPlacement()
    {
        var frame = Build("<div id='outer' style='display:flex;width:100px;padding:5px;background:#ff0000'><div id='inner' style='display:flex;padding:7px;background:#00ff00'><div id='leaf' style='padding:3px;font-size:10px;background:#0000ff'>W</div></div><div id='sibling' style='width:10px;height:10px;background:#ffffff'></div></div>", 200, 200);
        Rect(First(frame, "outer", "rect").Bounds, 0, 0, 110, 43, "Nested flex parent padding contributes once");
        Rect(First(frame, "inner", "rect").Bounds, 5, 5, 30, 33, "Nested flex middle padding contributes once");
        Rect(First(frame, "leaf", "rect").Bounds, 12, 12, 16, 19, "Nested flex leaf padding contributes once");
        var text = First(frame, "leaf", "text");
        Near(15, text.Bounds.X, "Nested flex text X has one contribution per ancestor padding");
        Near(15, text.Bounds.Y, "Nested flex text Y has one contribution per ancestor padding");
        Near(35, First(frame, "sibling", "rect").Bounds.X, "Nested decorated flex basis places next sibling after full outer width");
    }

    private static void HeightAutoAndSubnormalStep()
    {
        var frame = Build("<div id='auto' style='height:AUTO;font-size:10px;background:#ff0000'>W</div>", 100, 100);
        Near(13, First(frame, "auto", "rect").Bounds.Height, "CSS height auto is case-insensitive and uses measured line height");
        Reject(delegate { Build("<input type='range' min='0' max='1' value='.5' step='5e-324'/>", 100, 100); }, "Subnormal range step that overflows finite quantization is rejected");
    }

    private static HtmlPaintFrame BuildWithProfile(string markup, string css, IHtmlTextMetrics metrics)
    {
        var limits = new HtmlLimits();
        var document = HtmlParser.Parse(markup, css, limits);
        return HtmlLayout.Build(document, 200, 100, metrics, new Dictionary<string, string>(), limits);
    }

    private static void SelectedFontBackendAdmission()
    {
        var inter = new ProfiledFixtureMetrics("FixtureMetrics/1:Inter:cap-height");
        var frame = BuildWithProfile("<div id='text' style='font-size:10px'>WW</div>", "", inter);
        True(frame.FontProfile == inter.Profile, "Default vector profile reports the selected injected backend");
        Near(20, First(frame, "text", "text").Bounds.Width, "Default vector profile uses its exact selected metrics");
        frame = BuildWithProfile("<div id='text' style='font-family:Inter;font-size:10px'>WW</div>", "", inter);
        True(frame.FontProfile == inter.Profile, "Explicit Inter accepted by selected vector profile");
        var debug = new ProfiledFixtureMetrics("Debug:Lcd");
        frame = BuildWithProfile("<div id='text' style='font-size:10px'>WW</div>", "", debug);
        True(frame.FontProfile == "Debug:Lcd", "Default native LCD family follows the selected Debug metrics backend");
        frame = BuildWithProfile("<div id='text' style='font-family:Debug;font-size:10px'>WW</div>", "", debug);
        True(frame.FontProfile == "Debug:Lcd", "Explicit Debug accepted by selected LCD profile");
        Near(20, First(frame, "text", "text").Bounds.Width, "Explicit Debug uses injected matching metrics without substitution");
        Reject(delegate { BuildWithProfile("<div style='font-family:Inter'>W</div>", "", debug); }, "Inter family rejected by Debug metrics backend");
        Reject(delegate { BuildWithProfile("<div>W</div>", "#unused { font-family: Inter; }", debug); }, "Unused Inter font rule rejected by Debug backend");
        Reject(delegate { BuildWithProfile("<div style='font-family:Debug'>W</div>", "", inter); }, "Debug family rejected by vector Inter profile");
        Reject(delegate { BuildWithProfile("<div>W</div>", "#unused { font-family: Debug; }", inter); }, "Unused Debug font rule rejected by vector profile");
        foreach (var backend in new IHtmlTextMetrics[] { inter, debug })
        {
            frame = BuildWithProfile("<div id='generic' style='font-family:sans-serif;font-size:10px'>WW</div>", "", backend);
            True(frame.FontProfile == backend.Profile, "Generic sans-serif retains actual selected metrics backend");
            Near(20, First(frame, "generic", "text").Bounds.Width, "Generic sans-serif does not silently substitute another metric system");
        }
        var untouched = new ProfiledFixtureMetrics("Debug:Lcd");
        Reject(delegate { BuildWithProfile("<div>W</div>", "#unused { font-family: Inter; }", untouched); }, "Font mismatch rejected at complete document admission");
        True(untouched.Calls == 0, "Unused font mismatch rejected before font measurement or partial paint output");
    }

    private static HtmlPaintOperation RangeHandle(HtmlPaintFrame frame)
    {
        foreach (var operation in frame.Operations)
            if (operation.NodeId == "range" && operation.Key.EndsWith(":range-handle", StringComparison.Ordinal)) return operation;
        throw new Exception("Range handle paint operation is missing.");
    }

    private static void RangeHandleAuthoredRadius()
    {
        var frame = Build("<input id='range' type='range' style='width:100px;height:20px'/>", 200, 100);
        Near(0, RangeHandle(frame).Radius, "Default range handle stays square for native LCD compatibility");
        frame = Build("<input id='range' type='range' style='width:100px;height:20px;border-radius:6px'/>", 200, 100);
        Near(6, RangeHandle(frame).Radius, "Explicit six-pixel range handle radius is preserved");
    }
}
