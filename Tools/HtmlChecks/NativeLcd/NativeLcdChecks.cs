using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Hdr.Html;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

public class NativeLcdProxy : DispatchProxy
{
    public Func<MethodInfo, object[], object> Handler;
    protected override object Invoke(MethodInfo method, object[] args) { return Handler(method, args); }
}

static class NativeLcdChecks
{
    static int checks;
    static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    static void Reject(Action action, string message)
    {
        bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, message);
    }
    sealed class Fixture
    {
        public readonly IMyTextSurface Surface;
        public Vector2 Size = new Vector2(512, 256), Texture = new Vector2(512, 512);
        public ContentType Content = ContentType.SCRIPT;
        public string Script = "";
        public int Frames, Measurements, Mutations;
        public bool FailDraw, FailCommit, InvalidMeasure;
        public Action OnCommit;
        public readonly List<MySprite> Sprites = new List<MySprite>();
        public Fixture()
        {
            Surface = DispatchProxy.Create<IMyTextSurface, NativeLcdProxy>();
            ((NativeLcdProxy)Surface).Handler = Handle;
        }
        object Handle(MethodInfo method, object[] args)
        {
            switch (method.Name)
            {
                case "get_ContentType": return Content;
                case "get_Script": return Script;
                case "get_SurfaceSize": return Size;
                case "get_TextureSize": return Texture;
                case "MeasureStringInPixels":
                    Measurements++;
                    Check((string)args[1] == "Debug", "native font must be Debug");
                    string text = ((StringBuilder)args[0]).ToString();
                    Check(text.Length <= 64, "each measurement is bounded to 64 UTF-16 units");
                    if (InvalidMeasure) return new Vector2(float.NaN, 28);
                    return new Vector2(text.Length * 14 * (float)args[2], 28 * (float)args[2]);
                case "DrawFrame":
                    Mutations++;
                    if (FailDraw) throw new InvalidOperationException("injected DrawFrame fault");
                    return new MySpriteDrawFrame(delegate(MySpriteDrawFrame frame)
                    {
                        if (OnCommit != null) OnCommit();
                        if (FailCommit) throw new InvalidOperationException("injected native commit fault");
                        Frames++; Sprites.Clear(); frame.AddToList(Sprites);
                    });
                default: throw new Exception("Unexpected surface access: " + method.Name);
            }
        }
    }
    static HtmlPaintFrame Frame()
    {
        return new HtmlPaintFrame { Width = 512, Height = 256, FontProfile = "Debug:Lcd", Revision = 1 };
    }
    static HtmlPaintOperation Rect(double x, double y, double width, double height)
    {
        return new HtmlPaintOperation { Kind = "rect", Bounds = new HtmlRect(x, y, width, height), Color = new HtmlColor(1, .2, .3, .8) };
    }
    static HtmlPaintOperation Text(double x, double y)
    {
        return new HtmlPaintOperation { Kind = "text", Bounds = new HtmlRect(x, y, 42, 28), Text = "ABC", FontSize = 28, LineHeight = 1.2, Color = new HtmlColor(0, 1, 1, 1) };
    }
    static void ExpectRejected(HtmlNativeLcdPainter painter, Fixture fixture, HtmlPaintFrame frame, string error)
    {
        int previous = fixture.Mutations; HtmlPaintReport report;
        Check(!painter.TryPaint(frame, out report), "must reject " + error);
        Check(!report.Success && report.MutatingCalls == 0 && !string.IsNullOrEmpty(report.Error), "explicit preflight report for " + error);
        Check(fixture.Mutations == previous, "must not draw a partial frame for " + error);
    }
    static void Main()
    {
        OwnershipAndRects(); TextAndClip(); FailureAndLimits(); CleanupLifecycle(); ReentrantLifecycle(); MetricsAndNoopPerformance();
        Console.WriteLine("Native LCD checks passed: " + checks);
    }
    static void ReentrantLifecycle()
    {
        var a = Frame(); a.Operations.Add(Rect(0, 0, 20, 20)); var b = Frame(); b.Operations.Add(Rect(30, 0, 20, 20));
        var f = new Fixture(); HtmlNativeLcdPainter painter = null; HtmlPaintReport report;
        bool enter = false, nested = true; HtmlPaintReport nestedReport = null;
        int guardCalls = 0;
        painter = new HtmlNativeLcdPainter(f.Surface, true, null, delegate
        {
            guardCalls++;
            if (enter) { enter = false; nested = painter.TryPaint(b, out nestedReport); painter.Reset(); }
            return true;
        });
        enter = true; Check(painter.TryPaint(a, out report), "initial paint remains coherent when guard attempts nested paint/reset");
        Check(!nested && nestedReport.MutatingCalls == 0 && guardCalls == 1 && f.Mutations == 1, "reentrant paint fails before recursively invoking ownership guard");
        Check(!string.IsNullOrEmpty(painter.LastCleanupError), "reentrant reset rejection is explicit");
        enter = true; painter.Reset();
        Check(!nested && nestedReport.MutatingCalls == 0 && f.Mutations == 2 && f.Sprites.Count == 0, "cleanup guard cannot publish a replacement frame behind outer cleanup");
        Check(painter.LastCleanupSucceeded && painter.LastCleanupError == null, "successful eligible cleanup replaces rejected nested reset diagnostic");
        Check(painter.TryPaint(b, out report) && report.Changed && f.Mutations == 3 && f.Sprites[0].Position == new Vector2(40, 138), "next replacement frame actually paints instead of returning stale blank cache");

        bool disposeInsideGuard = false; f = new Fixture(); painter = new HtmlNativeLcdPainter(f.Surface, true, null, delegate
        {
            if (disposeInsideGuard) { disposeInsideGuard = false; painter.Dispose(); }
            return true;
        });
        Check(painter.TryPaint(a, out report), "dispose guard fixture published first frame");
        disposeInsideGuard = true; Check(!painter.TryPaint(b, out report), "terminal disposal requested in paint guard rejects in-flight publication");
        Check(report.Error.IndexOf("Disposal was requested during the native LCD operation.", StringComparison.Ordinal) >= 0, "pending disposal retains explicit whitelisted diagnostic");
        Check(f.Mutations == 2 && f.Sprites.Count == 0 && painter.LastCleanupSucceeded, "deferred disposal clears only prior own publication once");
        Check(report.MutatingCalls == 1 && !painter.TryPaint(a, out nestedReport) && nestedReport.MutatingCalls == 0, "deferred cleanup is counted and disposed painter rejects later work");

        f = new Fixture(); painter = new HtmlNativeLcdPainter(f.Surface, true); var callbackPainter = painter;
        f.OnCommit = delegate { f.OnCommit = null; callbackPainter.Dispose(); };
        Check(!painter.TryPaint(a, out report) && !report.Success, "native callback terminal disposal does not claim retained successful output");
        Check(f.Mutations == 2 && f.Frames == 2 && f.Sprites.Count == 0 && report.MutatingCalls == 2, "commit callback disposal cleans freshly committed owned frame once");
        Check(typeof(HtmlNativeLcdPainter).GetField("_last", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(painter) == null && typeof(HtmlNativeLcdPainter).GetField("_lastFrame", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(painter) == null, "terminal disposal retains no callback-created cache");

        bool switchMode = false; f = new Fixture(); painter = new HtmlNativeLcdPainter(f.Surface, true, null, delegate
        {
            if (switchMode) f.Script = "Clock";
            return true;
        });
        Check(painter.TryPaint(a, out report), "mode-switch cleanup fixture owns initial publication"); switchMode = true; painter.Reset();
        Check(f.Mutations == 1 && !painter.LastCleanupAttempted, "mode changed by guard is rechecked before any empty native frame");
        painter.Dispose(); Check(f.Mutations == 1, "guard-switched mode consumes old cleanup grant without later blank");
    }
    static void OwnershipAndRects()
    {
        var f = new Fixture(); Reject(delegate { new HtmlNativeLcdPainter(f.Surface, false); }, "ownership confirmation required");
        var painter = new HtmlNativeLcdPainter(f.Surface, true); var frame = Frame();
        var clipped = Rect(-10, 5, 100, 40); clipped.HasClip = true; clipped.Clip = new HtmlRect(20, 0, 60, 100); frame.Operations.Add(clipped);
        frame.Operations.Add(Rect(600, 0, 100, 20));
        HtmlPaintReport report; Check(painter.TryPaint(frame, out report), "clipped solid rectangle supported");
        Check(report.Success && report.Changed && report.MutatingCalls == 1 && report.PreparedOperations == 1 && report.EstimatedTriangles == 2, "native admission accounting");
        Check(f.Frames == 1 && f.Sprites.Count == 1, "one complete native frame");
        Check(f.Sprites[0].Data == "SquareSimple" && f.Sprites[0].Position == new Vector2(50, 153) && f.Sprites[0].Size == new Vector2(60, 40), "rect clipped before sprite with actual texture centering");
        Check(painter.TryPaint(frame, out report) && !report.Changed && report.MutatingCalls == 0 && f.Frames == 1, "identical frame no-op");
        frame.Operations[0].Color = new HtmlColor(0, 0, 1, 1);
        Check(painter.TryPaint(frame, out report) && report.Changed && f.Frames == 2, "public mutable frame content changes detected");
        painter.Reset(); Check(f.Frames == 3 && f.Sprites.Count == 0 && painter.LastCleanupAttempted && painter.LastCleanupSucceeded && painter.LastCleanupError == null, "reset publishes one empty native frame after own successful paint");
        painter.Reset(); Check(f.Frames == 3, "repeated reset does not blank twice");
        Check(painter.TryPaint(frame, out report) && report.Changed && f.Frames == 4, "fresh paint after reset regrants publication ownership");
        frame.Operations.Clear(); Check(painter.TryPaint(frame, out report) && f.Sprites.Count == 0, "explicit empty frame clears owned native output");
        int before = f.Mutations; painter.Dispose(); Check(f.Mutations == before + 1 && f.Sprites.Count == 0, "dispose cleans successful own output once");
        painter.Dispose(); painter.Reset(); Check(f.Mutations == before + 1, "dispose and reset remain idempotent after disposal");
        Check(typeof(HtmlNativeLcdPainter).GetField("_metrics", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(painter) == null, "dispose releases metrics and their surface reference");
        Check(!painter.TryPaint(frame, out report) && report.MutatingCalls == 0, "disposed painter fails closed");
        Check(report.Error == "The native LCD painter has been disposed.", "disposed native diagnostic remains explicit with whitelisted exception");
    }
    static void CleanupLifecycle()
    {
        var frame = Frame(); frame.Operations.Add(Rect(0, 0, 20, 20)); HtmlPaintReport report;
        var untouched = new Fixture(); var fresh = new HtmlNativeLcdPainter(untouched.Surface, true);
        fresh.Reset(); fresh.Reset(); fresh.Dispose(); fresh.Dispose();
        Check(untouched.Mutations == 0 && !fresh.LastCleanupAttempted && !fresh.LastCleanupSucceeded, "never blank before first own successful publication");

        var rendered = new Fixture(); var disposable = new HtmlNativeLcdPainter(rendered.Surface, true);
        Check(disposable.TryPaint(frame, out report) && rendered.Sprites.Count == 1, "dispose lifecycle starts with visible owned output");
        disposable.Dispose(); Check(rendered.Mutations == 2 && rendered.Sprites.Count == 0 && disposable.LastCleanupSucceeded, "dispose removes visible own content through one empty commit");
        disposable.Dispose(); Check(rendered.Mutations == 2, "repeated dispose does not issue another cleanup");

        var foreign = new Fixture(); var painter = new HtmlNativeLcdPainter(foreign.Surface, true);
        Check(painter.TryPaint(frame, out report), "foreign content guard fixture painted"); foreign.Content = ContentType.TEXT_AND_IMAGE;
        painter.Reset(); Check(foreign.Mutations == 1 && !painter.LastCleanupAttempted && !painter.LastCleanupSucceeded, "reset never blanks a different content mode");
        foreign.Content = ContentType.SCRIPT; painter.Dispose(); Check(foreign.Mutations == 1, "consumed foreign-mode cleanup cannot erase later native content");

        foreign = new Fixture(); painter = new HtmlNativeLcdPainter(foreign.Surface, true); Check(painter.TryPaint(frame, out report), "foreign script guard fixture painted");
        foreign.Script = "HDRAPI"; painter.Dispose(); Check(foreign.Mutations == 1 && !painter.LastCleanupAttempted, "dispose never blanks another selected text-surface script");

        bool owned = true; var guarded = new Fixture(); painter = new HtmlNativeLcdPainter(guarded.Surface, true, null, delegate { return owned; });
        Check(painter.TryPaint(frame, out report), "guarded owned frame publishes"); owned = false;
        painter.Reset(); Check(guarded.Mutations == 1 && !painter.LastCleanupAttempted, "revoked caller ownership skips cleanup");
        Check(!painter.TryPaint(frame, out report) && report.MutatingCalls == 0, "revoked guard prevents future painting");
        owned = true; Check(painter.TryPaint(frame, out report) && report.Changed && guarded.Mutations == 2, "restored caller guard cannot reuse stale publication cache");
        painter.Dispose(); Check(guarded.Mutations == 3 && painter.LastCleanupSucceeded, "fresh guarded publication grants one cleanup");
        Check(typeof(HtmlNativeLcdPainter).GetField("_ownershipGuard", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(painter) == null, "dispose releases caller ownership delegate");

        var observed = new Fixture(); painter = new HtmlNativeLcdPainter(observed.Surface, true); Check(painter.TryPaint(frame, out report), "observed mode loss fixture painted");
        observed.Script = "Clock"; Check(!painter.TryPaint(frame, out report), "foreign script observed during paint fails closed");
        observed.Script = ""; painter.Dispose(); Check(observed.Mutations == 1, "previously observed ownership loss prevents later cleanup");

        var fault = new Fixture(); painter = new HtmlNativeLcdPainter(fault.Surface, true); Check(painter.TryPaint(frame, out report), "cleanup draw fault fixture painted");
        fault.FailDraw = true; painter.Reset();
        Check(fault.Mutations == 2 && painter.LastCleanupAttempted && !painter.LastCleanupSucceeded && !string.IsNullOrEmpty(painter.LastCleanupError), "cleanup DrawFrame failure is contained and truthfully recorded");
        painter.Reset(); Check(fault.Mutations == 2, "failed cleanup consumes one attempt rather than blanking repeatedly");
        fault.FailDraw = false; Check(painter.TryPaint(frame, out report) && report.Changed && fault.Mutations == 3, "cleanup failure invalidates cached output and allows fresh repair");
        painter.Reset(); Check(fault.Mutations == 4 && painter.LastCleanupSucceeded && painter.LastCleanupError == null, "fresh repair can clean successfully and replace old error");

        fault = new Fixture(); painter = new HtmlNativeLcdPainter(fault.Surface, true); Check(painter.TryPaint(frame, out report), "cleanup commit fault fixture painted");
        fault.FailCommit = true; painter.Dispose();
        Check(fault.Mutations == 2 && painter.LastCleanupAttempted && !painter.LastCleanupSucceeded && !string.IsNullOrEmpty(painter.LastCleanupError), "native cleanup commit fault leaves uncertainty explicit");
        painter.Dispose(); Check(fault.Mutations == 2, "disposed failed cleanup cannot retry");

        bool throws = false; guarded = new Fixture(); painter = new HtmlNativeLcdPainter(guarded.Surface, true, null, delegate { if (throws) throw new InvalidOperationException("owner token unavailable"); return true; });
        Check(painter.TryPaint(frame, out report), "throwing ownership guard fixture publishes before failure"); throws = true; painter.Reset();
        Check(guarded.Mutations == 1 && !painter.LastCleanupAttempted && !painter.LastCleanupSucceeded && painter.LastCleanupError == "owner token unavailable", "failed ownership proof never opens cleanup frame");
        throws = false; painter.Reset(); Check(guarded.Mutations == 1, "failed ownership proof also consumes cleanup opportunity");
    }
    static void TextAndClip()
    {
        var f = new Fixture(); var painter = new HtmlNativeLcdPainter(f.Surface, true); var frame = Frame(); var text = Text(10, 20);
        text.HasClip = true; text.Clip = new HtmlRect(0, 0, 512, 256); frame.Operations.Add(text);
        HtmlPaintReport report; Check(painter.TryPaint(frame, out report), "wholly contained Debug text supported");
        Check(f.Sprites.Count == 3 && f.Sprites[1].Type == SpriteType.TEXT && f.Sprites[1].Data == "ABC" && f.Sprites[1].FontId == "Debug" && f.Sprites[1].Position == new Vector2(10, 148) && f.Sprites[1].RotationOrScale == 1, "native text uses measured scale and top-left origin");
        Check((int)f.Sprites[0].Type == 4 && f.Sprites[0].Position == new Vector2(0, 128) && f.Sprites[0].Size == new Vector2(512, 256) && (int)f.Sprites[2].Type == 4 && f.Sprites[2].Position == null && f.Sprites[2].Size == null, "actual public clip/text/clear sprite sequence");
        Check(report.PreparedOperations == 3 && report.EstimatedTriangles == 6, "all native text scissor entries budgeted");
        text.Clip = new HtmlRect(20, 0, 512, 256); Check(painter.TryPaint(frame, out report) && f.Sprites[0].Position == new Vector2(20, 128) && f.Sprites[0].Size == new Vector2(492, 256), "partial horizontal text clipping uses viewport-intersected native scissor");
        text.Clip = new HtmlRect(0, 0, 512, 35); Check(painter.TryPaint(frame, out report) && f.Sprites[0].Size == new Vector2(512, 35), "partial vertical native text clipping");
        text.Clip = new HtmlRect(20.2, 20.2, 40.7, 20.7); Check(painter.TryPaint(frame, out report) && f.Sprites[0].Position == new Vector2(21, 149) && f.Sprites[0].Size == new Vector2(39, 19), "fractional text clip rounds inward in actual texture pixels");
        text.Clip = new HtmlRect(200, 200, 10, 10); Check(painter.TryPaint(frame, out report) && f.Sprites.Count == 3, "do not cull native shadow overhang from an approximate measured line box");
        text.Clip = new HtmlRect(200, 200, 0, 0); Check(painter.TryPaint(frame, out report) && f.Sprites.Count == 0, "empty clip omits native text");
        text.HasClip = false; text.Bounds.Width = 44; ExpectRejected(painter, f, frame, "mismatched Debug layout measurements");
        text.Bounds.Width = 42; text.FontSize = double.NaN; ExpectRejected(painter, f, frame, "invalid text size");
        text.FontSize = 28; text.Text = new string('x', 65); ExpectRejected(painter, f, frame, "oversize native text run");
        text.Text = "one\ntwo"; ExpectRejected(painter, f, frame, "native multiline operation");
        text.Text = "\ud800"; ExpectRejected(painter, f, frame, "unpaired UTF16 surrogate");
    }
    static void FailureAndLimits()
    {
        var f = new Fixture(); var painter = new HtmlNativeLcdPainter(f.Surface, true); var frame = Frame();
        frame.Operations.Add(Rect(0, 0, 20, 20)); frame.Operations.Add(new HtmlPaintOperation { Kind = "svg", Bounds = new HtmlRect(0, 0, 100, 100), Color = new HtmlColor(1, 1, 1, 1) });
        ExpectRejected(painter, f, frame, "late unsupported asset");
        frame.Operations.RemoveAt(1); frame.Operations[0].Radius = 10; ExpectRejected(painter, f, frame, "rounded rectangle");
        frame.Operations[0].Radius = 0; frame.Operations[0].StrokeWidth = 1; ExpectRejected(painter, f, frame, "native stroke");
        frame.Operations[0].StrokeWidth = 0; frame.FontProfile = "Inter"; ExpectRejected(painter, f, frame, "font substitution");
        frame.FontProfile = "Debug:Lcd"; frame.Width = 500; ExpectRejected(painter, f, frame, "mismatched authored viewport");
        frame.Width = 512; f.Content = ContentType.TEXT_AND_IMAGE; ExpectRejected(painter, f, frame, "foreign content type");
        f.Content = ContentType.SCRIPT; f.Script = "HDRAPI"; ExpectRejected(painter, f, frame, "foreign text-surface script");
        f.Script = ""; f.Texture = new Vector2(100); ExpectRejected(painter, f, frame, "surface exceeds texture"); f.Texture = new Vector2(512);
        frame.Operations[0].Color = new HtmlColor(0, 0, 0, double.NaN); ExpectRejected(painter, f, frame, "NaN color");
        frame.Operations[0].Color = new HtmlColor(1, 1, 1, 1); frame.Operations[0].Bounds.Width = double.PositiveInfinity; ExpectRejected(painter, f, frame, "infinite rectangle");
        frame.Operations[0] = Rect(0, 0, 20, 20); frame.Operations.Add(Rect(20, 0, 20, 20));
        var limited = new HtmlNativeLcdPainter(f.Surface, true, new HtmlPainterLimits { MaxOperations = 1 }); ExpectRejected(limited, f, frame, "configurable operation limit");
        limited = new HtmlNativeLcdPainter(f.Surface, true, new HtmlPainterLimits { MaxPrimitives = 1 }); ExpectRejected(limited, f, frame, "configurable sprite limit");
        var textFrame = Frame(); textFrame.Operations.Add(Text(10, 10));
        limited = new HtmlNativeLcdPainter(f.Surface, true, new HtmlPainterLimits { MaxPrimitives = 2 }); ExpectRejected(limited, f, textFrame, "text plus clip plus clear must fit atomically");
        frame.Operations.RemoveAt(1); f.FailDraw = true; HtmlPaintReport report;
        Check(!painter.TryPaint(frame, out report) && report.MutatingCalls == 1 && !report.Changed, "native DrawFrame fault contained");
        f.FailDraw = false; f.FailCommit = true; Check(!painter.TryPaint(frame, out report) && report.MutatingCalls == 1, "native commit fault contained without atomicity claim");
        f.FailCommit = false; Check(painter.TryPaint(frame, out report) && report.Changed, "uncertain frame invalidates cache and retries");
        frame.Hits.Add(null); frame.Operations[0].Bounds.X = 1;
        Check(painter.TryPaint(frame, out report) && report.Changed, "paint-only cache ignores unrelated controller hit metadata");
    }
    static void MetricsAndNoopPerformance()
    {
        var f = new Fixture(); var metrics = new HtmlLcdTextMetrics(f.Surface); var result = metrics.Measure("AB", 14, 1.5);
        Check(metrics.Profile == "Debug:Lcd" && result.Advance == 14 && result.CapHeight == 14 && result.LineAdvance == 21 && result.Ink.X == -14 && result.Ink.Y == -14 && result.Ink.Width == 42 && result.Ink.Height == 42 && result.HasInk, "native measured metric mapping with conservative shadow envelope");
        Check(!metrics.Measure("", 14, 1.5).HasInk, "empty measured text has no ink");
        f.InvalidMeasure = true; Reject(delegate { metrics.Measure("AB", 14, 1.5); }, "invalid actual native measurements rejected"); f.InvalidMeasure = false;
        var painter = new HtmlNativeLcdPainter(f.Surface, true); var frame = Frame(); frame.Operations.Add(Text(10, 10)); HtmlPaintReport report;
        Check(painter.TryPaint(frame, out report), "performance fixture first commit");
        int measurements = f.Measurements, mutations = f.Mutations;
        for (int i = 0; i < 100; i++) painter.TryPaint(frame, out report);
        var watch = Stopwatch.StartNew(); long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) if (!painter.TryPaint(frame, out report)) throw new Exception(report.Error);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before; watch.Stop();
        Check(f.Measurements == measurements && f.Mutations == mutations, "unchanged frames do not remeasure or redraw");
        Console.WriteLine("Cached native LCD: 10000 calls, " + watch.Elapsed.TotalMilliseconds.ToString("F2") + " ms, " + allocated + " bytes (diagnostic reports plus DispatchProxy surface getter overhead); zero MeasureStringInPixels/DrawFrame calls.");
    }
}
