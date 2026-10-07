using System;
using HDRClientRenderer;

internal static class DisplayOcclusionTests
{
    sealed class Query : DisplayOcclusion.IQuery
    {
        internal long Samples;
        internal bool Completed = true, Fail;
        internal int Polls, Releases;
        public bool TryGetSamples(out long samples) { Polls++; if (Fail) throw new InvalidOperationException(); samples = Samples; return Completed; }
        public void Dispose() { Releases++; }
    }
    sealed class Backend : DisplayOcclusion.IBackend
    {
        internal bool Supported = true, Fail;
        internal Query Next = new Query();
        internal int Begins;
        public bool CurrentFrameOpaqueProof { get { return Supported; } }
        public DisplayOcclusion.IQuery Begin(string key, DisplayOcclusion.Stamp stamp, DisplayOcclusion.Footprint footprint)
        { Begins++; if (Fail) throw new InvalidOperationException(); return Next; }
    }
    sealed class Fixture
    {
        internal readonly Backend Backend = new Backend();
        internal readonly DisplayOcclusion Policy;
        internal object Main = new object(), View = new object(), Geometry = new object(), Depth = new object();
        internal long Frame = 10;
        internal Fixture() { Policy = new DisplayOcclusion(Backend); Policy.SetEnabled(true); }
        internal DisplayOcclusion.Stamp Stamp { get { return new DisplayOcclusion.Stamp(Policy.Epoch, Frame, 100, 100, View, Geometry, Depth); } }
        internal void Ready()
        { Policy.ObserveMainStarting(Frame, 100, 100, Main); Policy.ObserveMainCompleted(Frame, Main, true); Check(Policy.MarkOpaqueProofReady(Stamp), "trusted test backend can certify the current main frame"); }
        internal DisplayOcclusion.Decision Probe(string key = "screen") { return Policy.Probe(key, Stamp, Full); }
    }
    static int checks;
    static DisplayOcclusion.Footprint Full { get { return new DisplayOcclusion.Footprint(0, 0, 100, 100, .5, true); } }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    static void Main()
    {
        DefaultRetainsDemand(); CorrectPhase(); Freshness(); CompleteCoverage(); QueryResults(); DeferredSubmission(); Projection(); NativeLifecycle(); ConsumerInventory(); Bounds(); Lifetime();
        Console.WriteLine("Display occlusion: " + checks + " conservative proof checks passed.");
    }
    static void DefaultRetainsDemand()
    {
        var policy = new DisplayOcclusion(); policy.SetEnabled(true);
        var stamp = new DisplayOcclusion.Stamp(1, 1, 100, 100, new object(), new object(), new object());
        policy.ObserveMainStarting(1, 100, 100, new object());
        Check(!policy.Supported && policy.StatusText.Contains("unavailable"), "requesting the unavailable feature cannot advertise enabled support");
        Check(policy.Probe("screen", stamp, Full).KeepDemand && policy.PendingCount == 0, "unavailable backend never culls or creates query work");
        Check(!policy.MarkOpaqueProofReady(stamp), "phase observation cannot forge native opaque proof");
        Check(default(DisplayOcclusion.Decision).KeepDemand, "default result is conservative");
        policy.SetEnabled(false); Check(policy.StatusText.Contains("disabled") && policy.Probe("screen", stamp, Full).KeepDemand, "disabled feature preserves demand");
    }
    static void CorrectPhase()
    {
        var f = new Fixture(); f.Policy.ObserveMainStarting(f.Frame, 100, 100, f.Main);
        Check(f.Probe().State == DisplayOcclusion.Status.BeforeOpaqueDepth && f.Backend.Begins == 0, "capture prefix cannot consume missing current depth");
        Check(!f.Policy.MarkOpaqueProofReady(f.Stamp), "starting the main scene is not completed depth");
        f.Policy.ObserveMainCompleted(f.Frame, f.Main, true);
        Check(f.Probe().KeepDemand && f.Backend.Begins == 0, "scene completion alone cannot issue a proof query");
        f.Policy.MarkOpaqueProofReady(f.Stamp);
        Check(!f.Probe().KeepDemand, "only fresh trusted zero evidence can suppress demand in the certified phase");
        f.Policy.ObserveMainCompleted(f.Frame, new object(), true);
        Check(f.Probe().KeepDemand, "an auxiliary or replaced G-buffer invalidates phase evidence");
        var opaque = new Fixture(); opaque.Policy.ObserveMainStarting(opaque.Frame, 100, 100, opaque.Main);
        opaque.Policy.ObservePrimaryOpaqueCompleted(opaque.Frame, opaque.Main);
        Check(opaque.Policy.MarkOpaqueProofReady(opaque.Stamp), "exact primary opaque seam can certify proof before transparent scene completion");
        opaque.Policy.Submit("screen", opaque.Stamp, Full);
        opaque.Policy.ObserveMainCompleted(opaque.Frame, opaque.Main, true);
        Check(!opaque.Probe().KeepDemand, "successful main postfix preserves submitted current-frame evidence for late capture scheduling");
        opaque.Policy.ObserveMainCompleted(opaque.Frame, opaque.Main, false);
        Check(opaque.Probe().KeepDemand, "failed main completion invalidates previously submitted opaque evidence");
    }
    static void Freshness()
    {
        var f = new Fixture(); f.Ready(); var old = f.Stamp; Check(!f.Probe().KeepDemand, "fresh zero proof established");
        f.Frame++; f.Ready();
        Check(f.Policy.Probe("screen", old, Full).State == DisplayOcclusion.Status.Stale, "prior-frame zero cannot cull current moving occluders even with the same view");
        Check(f.Backend.Next.Releases == 1, "old GPU evidence retires at the frame boundary");
        var changedView = new DisplayOcclusion.Stamp(f.Policy.Epoch, f.Frame, 100, 100, new object(), f.Geometry, f.Depth);
        Check(f.Policy.Probe("screen", changedView, Full).KeepDemand, "viewer change invalidates same-frame evidence");
        var changedDepth = new DisplayOcclusion.Stamp(f.Policy.Epoch, f.Frame, 100, 100, f.View, f.Geometry, new object());
        Check(f.Policy.Probe("screen", changedDepth, Full).KeepDemand, "changed opaque image invalidates proof independently of the resource name");
        var resized = new DisplayOcclusion.Stamp(f.Policy.Epoch, f.Frame, 101, 100, f.View, f.Geometry, f.Depth);
        Check(f.Policy.Probe("screen", resized, Full).KeepDemand, "viewport resize invalidates projection evidence");
        var wrongEpoch = new DisplayOcclusion.Stamp(f.Policy.Epoch + 1, f.Frame, 100, 100, f.View, f.Geometry, f.Depth);
        Check(f.Policy.Probe("screen", wrongEpoch, Full).KeepDemand, "world or device epoch mismatch never suppresses demand");
        f.Backend.Next = new Query { Completed = false }; f.Geometry = new object();
        Check(f.Probe().KeepDemand, "changed display mesh, world pose or crop cannot inherit a former zero result");
    }
    static void CompleteCoverage()
    {
        var f = new Fixture(); f.Ready();
        var partial = new DisplayOcclusion.Footprint(0, 0, 50, 50, .5, false);
        Check(f.Policy.Probe("screen", f.Stamp, partial).KeepDemand && f.Backend.Begins == 0, "sparse rays or partial-tile samples cannot certify complete occlusion");
        var outside = new DisplayOcclusion.Footprint(-1, 0, 100, 100, .5, true);
        Check(f.Policy.Probe("screen", f.Stamp, outside).KeepDemand, "unclipped or malformed footprint is rejected");
        var nonfinite = new DisplayOcclusion.Footprint(0, 0, 100, 100, double.NaN, true);
        Check(f.Policy.Probe("screen", f.Stamp, nonfinite).KeepDemand, "invalid closest reverse depth cannot become zero evidence");
        Check(f.Policy.Probe(new string('x', 129), f.Stamp, Full).KeepDemand, "unbounded display key cannot allocate work");
    }
    static void QueryResults()
    {
        var f = new Fixture(); f.Backend.Next.Completed = false; f.Ready();
        Check(f.Probe().State == DisplayOcclusion.Status.Pending && f.Probe().KeepDemand, "pending GetResult(false) is conservative");
        Check(f.Backend.Next.Polls == 1, "repeated demand probes do not busy-poll the GPU");
        var visible = new Fixture(); visible.Backend.Next.Samples = 1; visible.Ready(); Check(visible.Probe().KeepDemand, "one surviving sample retains capture demand");
        var negative = new Fixture(); negative.Backend.Next.Samples = -1; negative.Ready(); Check(negative.Probe().KeepDemand, "negative completion sentinel never aliases zero");
        var error = new Fixture(); error.Backend.Next.Fail = true; error.Ready(); Check(error.Probe().KeepDemand, "query errors retain demand");
        var beginError = new Fixture(); beginError.Backend.Fail = true; beginError.Ready(); Check(beginError.Probe().KeepDemand, "submission errors retain demand");
        var retired = new Fixture(); retired.Ready(); retired.Probe(); retired.Backend.Supported = false; Check(retired.Probe().KeepDemand, "capability retirement invalidates the decision path");
        Check(retired.Policy.PendingCount == 0 && retired.Backend.Next.Releases == 1, "capability retirement releases its stale query ownership");
    }
    static void Bounds()
    {
        var f = new Fixture(); f.Ready(); var tiny = new DisplayOcclusion.Footprint(0, 0, 1, 1, .5, true);
        for (int i = 0; i < DisplayOcclusion.MaxQueriesPerFrame; i++) Check(!f.Policy.Probe("tile" + i, f.Stamp, tiny).KeepDemand, "bounded query admission " + i);
        Check(f.Policy.Probe("overflow", f.Stamp, tiny).State == DisplayOcclusion.Status.BudgetExceeded, "query overflow retains demand");
        f.Policy.ObserveMainStarting(f.Frame, 100, 100, f.Main); f.Policy.ObserveMainCompleted(f.Frame, f.Main, true); f.Policy.MarkOpaqueProofReady(f.Stamp);
        Check(f.Policy.Probe("repeat", f.Stamp, tiny).KeepDemand, "repeated main draw does not refill query allowance in one frame");
        var pixels = new Fixture(); pixels.Ready(); pixels.Probe("full");
        Check(pixels.Policy.Probe("extra", pixels.Stamp, tiny).KeepDemand, "total query enclosure pixels are bounded to one viewport");
    }
    static void DeferredSubmission()
    {
        var f = new Fixture(); f.Backend.Next.Completed = false; f.Ready();
        Check(f.Policy.Submit("screen", f.Stamp, Full).KeepDemand && f.Backend.Next.Polls == 0,
            "submission does not consume the one result poll before the GPU can complete");
        f.Policy.Submit("screen", f.Stamp, Full);
        Check(f.Backend.Begins == 1 && f.Backend.Next.Polls == 0, "duplicate same-frame submission reuses its query without polling");
        f.Backend.Next.Completed = true;
        Check(!f.Probe().KeepDemand && f.Backend.Next.Polls == 1, "later same-frame consumption can suppress after completed zero proof");
        Check(!f.Probe().KeepDemand && f.Backend.Next.Polls == 1, "completed evidence can be reused only within the immutable same-frame stamp");
        var pending = new Fixture(); pending.Backend.Next.Completed = false; pending.Ready();
        pending.Policy.Submit("screen", pending.Stamp, Full); pending.Probe(); pending.Backend.Next.Completed = true;
        Check(pending.Probe().KeepDemand && pending.Backend.Next.Polls == 1, "a pending consumption never turns into busy polling");
        var stale = new Fixture(); stale.Ready(); var old = stale.Stamp;
        stale.Policy.Submit("screen", old, Full); stale.Frame++; stale.Ready();
        Check(stale.Policy.Probe("screen", old, Full).KeepDemand && stale.Backend.Next.Polls == 0,
            "deferred query cannot survive a frame boundary even if it has completed");
    }
    static DisplayOcclusionProjection.Vertex V(double x, double y, double depth = .5, double w = 1)
    { return new DisplayOcclusionProjection.Vertex(x * w, y * w, depth * w, w); }
    static void Projection()
    {
        DisplayOcclusion.Footprint box;
        var quad = new[] { V(-.5, -.5), V(.5, -.5), V(-.5, .5), V(-.5, .5), V(.5, -.5), V(.5, .5) };
        Check(DisplayOcclusionProjection.TryEnclose(quad, 100, 80, out box), "complete projected triangle mesh has an enclosure");
        Check(box.Left == 24 && box.Right == 76 && box.Top == 19 && box.Bottom == 61 && box.ClosestReverseDepth > .5,
            "rectangle pads raster edges and depth toward the viewer");
        Check(!DisplayOcclusionProjection.TryEnclose(new[] { V(0, 0), V(0, 0) }, 100, 100, out box), "partial triangle cannot certify complete coverage");
        Check(!DisplayOcclusionProjection.TryEnclose(new[] { V(0, 0), V(0, 0), V(0, 0, .5, -1) }, 100, 100, out box), "eye-plane crossing retains demand");
        Check(!DisplayOcclusionProjection.TryEnclose(new[] { V(0, 0), V(0, 0), V(0, 0, 1.01) }, 100, 100, out box), "near-plane crossing retains demand");
        Check(!DisplayOcclusionProjection.TryEnclose(new[] { V(0, 0), V(0, 0), V(double.NaN, 0) }, 100, 100, out box), "nonfinite pose cannot produce proof");
        Check(!DisplayOcclusionProjection.TryEnclose(new DisplayOcclusionProjection.Vertex[DisplayOcclusionProjection.MaxVertices + 3], 100, 100, out box), "mesh projection work has a strict vertex bound");
        Check(DisplayOcclusionProjection.TryEnclose(new[] { V(-2, -2), V(2, -2), V(0, 2) }, 100, 100, out box) &&
            box.Left == 0 && box.Top == 0 && box.Right == 100 && box.Bottom == 100, "viewport clipping preserves all surviving pixels");
        Check(!DisplayOcclusionProjection.TryEnclose(new[] { V(2, 2), V(3, 2), V(2, 3) }, 100, 100, out box), "offscreen geometry supplies no occlusion proof");
        var random = new Random(728);
        for (int i = 0; i < 400; i++)
        {
            var triangle = new DisplayOcclusionProjection.Vertex[3];
            for (int j = 0; j < 3; j++) triangle[j] = V(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1,
                random.NextDouble(), .01 + random.NextDouble() * 10);
            Check(DisplayOcclusionProjection.TryEnclose(triangle, 1920, 1080, out box), "bounded positive-W triangle projects " + i);
            // Homogeneous interpolation models a point inside the original
            // triangle, including strong perspective variation between vertices.
            double a = random.NextDouble(), b = random.NextDouble(), c = random.NextDouble(), total = a + b + c;
            a /= total; b /= total; c /= total;
            double w = a * triangle[0].W + b * triangle[1].W + c * triangle[2].W;
            double x = (a * triangle[0].X + b * triangle[1].X + c * triangle[2].X) / w;
            double y = (a * triangle[0].Y + b * triangle[1].Y + c * triangle[2].Y) / w;
            double z = (a * triangle[0].Z + b * triangle[1].Z + c * triangle[2].Z) / w;
            double px = (x + 1) * 960, py = (1 - y) * 540;
            Check(px >= box.Left && px < box.Right && py >= box.Top && py < box.Bottom && z <= box.ClosestReverseDepth,
                "enclosure contains interior pixels and never moves behind a triangle sample " + i);
        }
    }
    static void Lifetime()
    {
        var f = new Fixture(); f.Ready(); f.Probe(); var ticket = f.Backend.Next;
        f.Policy.Forget("screen"); Check(ticket.Releases == 1 && f.Policy.PendingCount == 0, "consumer removal retires its query");
        var before = f.Policy.Epoch; f.Policy.NewEpoch(); Check(f.Policy.Epoch > before && f.Probe().KeepDemand, "world reset cannot retain proof");
        f.Policy.Dispose(); Check(f.Probe().KeepDemand && f.Policy.StatusText.Contains("unavailable"), "disposal is visible and conservative");
        f.Policy.Dispose(); Check(ticket.Releases == 1, "resource retirement remains idempotent");
    }
    sealed class Enclosure : DisplayOcclusionQueryNative.IEnclosureRenderer
    {
        internal bool Proof, Match = true, Fail, RetireDuringDraw;
        public bool CurrentFrameOpaqueProof { get { return Proof; } }
        public bool Matches(DisplayOcclusion.Stamp stamp) { return Match; }
        public void Draw(object context, DisplayOcclusion.Stamp stamp, DisplayOcclusion.Footprint footprint)
        {
            VRage.Render11.Culling.Occlusion.MyOcclusionQuery.Events.Add("draw");
            if (RetireDuringDraw) Match = false;
            if (Fail) throw new InvalidOperationException("Draw failed.");
        }
    }
    static void ConsumerInventory()
    {
        var identity = new object(); var geometry = new object();
        var triangles = new[] { 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d, 0d };
        var consumers = new[] { new DisplayOcclusionInventory.Consumer("front", geometry, triangles), new DisplayOcclusionInventory.Consumer("rear", new object(), triangles) };
        var inventory = DisplayOcclusionInventory.Read(true, identity, consumers);
        Check(inventory != null && inventory.Same(DisplayOcclusionInventory.Read(true, identity, consumers)), "matching complete inventory preserves immutable membership and mesh provenance");
        int polls = 0;
        Check(inventory.AllHidden(DisplayOcclusionInventory.Read(true, identity, consumers), c => { polls++; return new DisplayOcclusion.Decision(DisplayOcclusion.Status.Occluded); }) && polls == 2,
            "camera suppression requires completed occlusion of every consumer");
        Check(!inventory.AllHidden(DisplayOcclusionInventory.Read(true, identity, consumers), c => new DisplayOcclusion.Decision(c.Key == "rear" ? DisplayOcclusion.Status.Pending : DisplayOcclusion.Status.Occluded)),
            "one pending display keeps shared camera demand");
        polls = 0;
        Check(!inventory.AllHidden(DisplayOcclusionInventory.Read(true, new object(), consumers), c => { polls++; return new DisplayOcclusion.Decision(DisplayOcclusion.Status.Occluded); }) && polls == 0,
            "changed inventory identity rejects stale results before reading queries");
        triangles[0] = 2;
        Check(!inventory.Same(DisplayOcclusionInventory.Read(true, identity, consumers)), "mutated callback arrays cannot rewrite frozen geometry evidence");
        Check(DisplayOcclusionInventory.Read(false, identity, consumers) == null, "unknown LCD or other consumers force complete-inventory failure");
        Check(DisplayOcclusionInventory.Read(true, identity, new DisplayOcclusionInventory.Consumer[0]) == null, "empty inventory never vacuously suppresses a camera");
        Check(DisplayOcclusionInventory.Read(true, identity, new[] { consumers[0], consumers[0] }) == null, "duplicate consumer keys cannot hide uncovered mesh");
        Check(DisplayOcclusionInventory.Read(true, identity, new[] { new DisplayOcclusionInventory.Consumer("invalid", geometry, new[] { double.NaN, 0d, 0d, 1d, 0d, 0d, 0d, 1d, 0d }) }) == null,
            "nonfinite geometry is not a complete inventory");
    }
    static void NativeLifecycle()
    {
        var events = VRage.Render11.Culling.Occlusion.MyOcclusionQuery.Events; events.Clear();
        var draw = new Enclosure();
        var backend = new DisplayOcclusionQueryNative(typeof(DisplayOcclusionTests).Assembly, draw);
        Check(events.Count == 0 && VRageRender.MyRender11.ContextReads == 0, "native metadata inspection creates no query and reads no renderer context");
        var stamp = new DisplayOcclusion.Stamp(1, 10, 100, 100, new object(), new object(), new object());
        Check(!backend.CurrentFrameOpaqueProof && backend.Begin("display", stamp, Full) == null && events.Count == 0,
            "native API presence without explicit current opaque proof does not allocate work");
        draw.Proof = true;
        var ticket = backend.Begin("display", stamp, Full);
        Check(string.Join(",", events) == "create,begin,draw,end", "private query surrounds the complete enclosure draw");
        long samples;
        Check(ticket.TryGetSamples(out samples) && samples == 0 && events[4] == "poll:false", "native result uses only nonblocking false argument");
        bool refused = false; int beforeEvents = events.Count;
        var wrongThread = new System.Threading.Thread(() =>
        { try { long ignored; ticket.TryGetSamples(out ignored); } catch (InvalidOperationException) { refused = true; } });
        wrongThread.Start(); wrongThread.Join();
        Check(refused && events.Count == beforeEvents, "foreign thread cannot read any native query result");
        draw.Match = false;
        Check(!ticket.TryGetSamples(out samples) && events.Count == 5, "changed frame/depth/view identity prevents reading or using native zero");
        var native = VRage.Render11.Culling.Occlusion.MyOcclusionQuery.Last;
        var thread = new System.Threading.Thread(() => ticket.Dispose()); thread.Start(); thread.Join();
        Check(native.m_query.Releases == 0, "game-thread invalidation defers GPU retirement to owner thread");
        backend.DrainRetired(); ticket.Dispose();
        Check(native.m_query.Releases == 1, "owner retirement disposes private resource once without engine pool return");
        draw.Match = true; draw.Fail = true; events.Clear();
        try { backend.Begin("failed", stamp, Full); Check(false, "draw exception must remain conservative"); } catch (InvalidOperationException) { }
        Check(string.Join(",", events) == "create,begin,draw,end,release", "failed proxy closes and retires query before returning failure");
        draw.Fail = false; draw.RetireDuringDraw = true;
        Check(backend.Begin("changed", stamp, Full) == null, "frame changed during draw cannot issue usable evidence");
        draw.Match = true; draw.RetireDuringDraw = false; VRage.Render11.Culling.Occlusion.MyOcclusionQuery.NextResult = -1;
        ticket = backend.Begin("pending", stamp, Full);
        Check(!ticket.TryGetSamples(out samples) && samples == -1, "native pending sentinel retains demand");
        for (int i = 1; i < DisplayOcclusion.MaxEntries; i++) backend.Begin("bounded" + i, stamp, Full);
        beforeEvents = events.Count;
        Check(backend.Begin("overflow", stamp, Full) == null && events.Count == beforeEvents,
            "unretired private queries have a fixed resource ownership cap");
        backend.Dispose(); backend.Dispose();
        Check(!backend.CurrentFrameOpaqueProof && !ticket.TryGetSamples(out samples), "native backend disposal retires every remaining query and capability");
    }
}
