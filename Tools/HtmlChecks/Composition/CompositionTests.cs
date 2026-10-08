using System.Collections;
using System.Globalization;
using System.Text.Json;
using Hdr.Html;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using F = CompositionFixture;

internal static class CompositionTests
{
    const string Markup = "<div id='viewport'></div><button id='camera-a' data-action='camera-a'>Camera A</button><button id='camera-b' data-action='camera-b'>Camera B</button><input id='gain' type='range' min='0' max='100' step='1' value='25' data-bind='gain' data-action='gain-change'/>";
    const string Css = "#viewport {width:320px;height:120px;padding:10px;border:2px solid #223344;margin-left:24px;margin-top:20px;background:#112233;} button {width:110px;height:28px;background:#334455;} input {width:110px;height:24px;}";
    static int assertions, cases, failures;
    static readonly List<string> results = new();
    static readonly MyTuple<string, object[]>[] CameraSettings = {
        new("refresh", new object[] { 30d }), new("panorama", new object[] { 110d, 5d, 1.2d, 512 }), new("quality", new object[] { "lite" }) };
    static void Check(bool good, string why) { assertions++; if (!good) throw new Exception(why); }
    static void Equal<T>(T wanted, T actual, string why) => Check(EqualityComparer<T>.Default.Equals(wanted, actual), why + "; wanted " + wanted + ", got " + actual);
    static void Near(double wanted, double actual, string why) => Check(Math.Abs(wanted - actual) < 2e-6, why + "; wanted " + wanted.ToString("R", CultureInfo.InvariantCulture) + ", got " + actual.ToString("R", CultureInfo.InvariantCulture));
    static void Reject(Action action, string why) { try { action(); } catch (ArgumentException) { assertions++; return; } throw new Exception("Expected rejection: " + why); }
    static void Run(string name, Action test)
    {
        cases++; try { test(); results.Add("PASS " + name); }
        catch (Exception error) { failures++; results.Add("FAIL " + name + ": " + error); }
        Console.WriteLine(results.Last());
    }
    static MyTuple<string, long, long, MyTuple<bool, bool, string>, long> Status(F f, long handle) => (MyTuple<string, long, long, MyTuple<bool, bool, string>, long>)f.Invoke("status", handle);
    static MyTuple<bool, string> SourceStatus(F f, long handle, string node = "viewport") => (MyTuple<bool, string>)f.Invoke("source-status", handle, node);
    static MyTuple<string, string, string, MyTuple<double, long, long>>[] Poll(F f, long handle) => (MyTuple<string, string, string, MyTuple<double, long, long>>[])f.Invoke("poll-events", handle);
    static HtmlPbPlan Publication(F f, long handle) => ((HtmlPbPainter)F.Field(f.Document(handle), "painter")).Accepted;
    static UiWidget Widget(F f, long handle, string node)
    {
        var control = Publication(f, handle).Controls.Single(c => c.Node == node);
        return f.Pb.Display.Widgets.Single(w => w.Id == control.Id);
    }
    static SurfaceMesh SourceMesh(F f, string slot) => (SurfaceMesh)F.Field(f.SlotCache(slot), "Canvas");
    static void SourceRegion(F f, long handle, string node)
    {
        var region = f.Document(handle).Controller.Frame.SourceRegions.Single(r => r.NodeId == node);
        var slot = f.Data.SourceSlots.Single(s => s.Id == Publication(f, handle).Sources.Single(s => s.Attachment.Node == node).Attachment.Slot);
        Near(region.Bounds.X * F.Units - F.Width * F.Units / 2, slot.Rect[0], "source rectangle uses content X");
        Near(F.Height * F.Units / 2 - (region.Bounds.Y + region.Bounds.Height) * F.Units, slot.Rect[1], "source rectangle uses content Y and downward HTML axis");
        Near(region.Bounds.Width * F.Units, slot.Rect[2], "source width excludes border and padding");
        Near(region.Bounds.Height * F.Units, slot.Rect[3], "source height excludes border and padding");
        Near(region.Clip.X * F.Units - F.Width * F.Units / 2, slot.Clip[0], "clip stays in same canvas X");
        Near(F.Height * F.Units / 2 - (region.Clip.Y + region.Clip.Height) * F.Units, slot.Clip[1], "clip stays in same canvas Y");
        Near(region.Opacity, slot.Opacity, "source opacity follows HTML");
    }
    static void Acceptance()
    {
        foreach (bool console in new[] { false, true })
        {
            using var f = new F(console); f.CreateShape("ellipsoid"); long handle = f.Bind(Markup, Css);
            var document = f.Document(handle); var tree = document.Controller.Document; var initial = Status(f, handle);
            Equal(console ? "console" : "projector", initial.Item1, "actual target capability");
            Check((bool)f.Invoke("attach-source", handle, "viewport", "camera-panorama", "101", CameraSettings), "physical camera A attaches to HTML viewport");
            SourceRegion(f, handle, "viewport"); var slot = f.Data.SourceSlots.Single(); string slotId = slot.Id;
            Near(36, document.Controller.Frame.SourceRegions.Single(r => r.NodeId == "viewport").Bounds.X, "content origin includes declared margin, border and padding");
            Near(320 * F.Units, slot.Rect[2], "requested 320 pixel camera width retained");
            Equal(512, slot.SourceCaptureResolution, "source capture maximum"); Equal(1, slot.SourceCaptureProfile, "source lite capture profile"); Near(30, slot.RefreshHz, "requested refresh");
            var a = Widget(f, handle, "camera-a"); var b = Widget(f, handle, "camera-b"); var range = Widget(f, handle, "gain");
            Check(a.ScreenId == "surface" && b.ScreenId == "surface" && range.ScreenId == "surface", "actual HTML controls belong to mapped surface");
            foreach (var control in new[] { a, b })
            {
                var artwork = f.Pb.Items["10:" + control.Control.Artwork];
                int order = (int)F.Static("LayerOrder", f.Scene, 10L, F.Field(artwork, "Layer"));
                Check(order > slot.Order, "authored HTML buttons are ordered above camera source window");
            }
            Check(Publication(f, handle).Items.Any(i => i.Order < slot.Order && Svg.Parse(i.Source, 12).Colors.Any(color => Math.Abs(color.X - 17f / 255) < 1e-5 && Math.Abs(color.Y - 34f / 255) < 1e-5 && Math.Abs(color.Z - 51f / 255) < 1e-5)), "camera slot is ordered after viewport's own HTML background");
            long generation = f.Data.UiSurfaceGeneration, uiRevision = f.Pb.Display.Revision, lease = f.Pb.Begin(range);
            Poll(f, handle); // Beginning the retained range lease also queues its own gesture event.
            f.Prepare(); var first = SourceMesh(f, slotId); Check(first != null, "bounded fake native camera frame is admitted by real source renderer");
            var mapped = f.Map("slot:" + slotId, first); Check(mapped != null && mapped.UV.Length == mapped.Geometry.Points.Length, "camera texture UVs map onto ellipsoid");
            Check(mapped.UV.All(uv => uv.X >= .125 - 1e-6 && uv.X <= .875 + 1e-6 && uv.Y >= .25 - 1e-6 && uv.Y <= .75 + 1e-6), "native subtexture window survives mapping");
            var proofA = F.Field(F.Field(f.SlotCache(slotId), "Source"), "SourceEvidence");
            var sourcesBefore = f.Pb.Sources();
            Check(f.RayHit(b), "authoritative world ray hits mapped Camera B HTML button"); Check(f.Pb.Click(b), "real core queues Camera B click");
            var events = Poll(f, handle); Equal(1, events.Length, "PB receives one button event");
            Equal("camera-b", events[0].Item2, "authored node preserved"); Equal("camera-b", events[0].Item3, "authored action preserved"); Equal(77L, events[0].Item4.Item3, "actor identity preserved");
            Check((bool)f.Invoke("attach-source", handle, "viewport", "camera-panorama", events[0].Item3 == "camera-b" ? "102" : "101", CameraSettings), "PB action switches physical source without HTML replacement");
            Equal(slotId, f.Data.SourceSlots.Single().Id, "camera replacement preserves node attachment identity"); Equal("102", f.Data.SourceSlots.Single().SourceId, "selected Camera B source ID");
            Check(ReferenceEquals(tree, document.Controller.Document), "camera switch never reparses document");
            Equal(initial.Item5, Status(f, handle).Item5, "camera switch causes zero relayouts"); Equal(initial.Item2, Status(f, handle).Item2, "camera switch preserves HTML desired revision");
            Check(ReferenceEquals(a, Widget(f, handle, "camera-a")) && ReferenceEquals(b, Widget(f, handle, "camera-b")) && ReferenceEquals(range, Widget(f, handle, "gain")), "camera switch recreates no HTML controls");
            Equal(uiRevision, f.Pb.Display.Revision, "camera switch preserves input definition revision"); Equal(generation, f.Data.UiSurfaceGeneration, "camera frames and source declarations preserve coordinate generation");
            Check(f.Pb.LeaseValid(lease), "active range lease survives camera switch");
            Check(sourcesBefore.OrderBy(p => p.Key).SequenceEqual(f.Pb.Sources().OrderBy(p => p.Key)), "camera switch leaves retained HTML artwork unchanged");
            f.Prepare(); Check(SourceMesh(f, slotId) != null, "Camera B publishes after switch");
            var proofB = F.Field(F.Field(f.SlotCache(slotId), "Source"), "SourceEvidence");
            Check(!ReferenceEquals(proofA, proofB), "different camera sources have independent evidence"); Check(f.Released.Any(p => ReferenceEquals(p, proofA)), "old camera evidence releases after source replacement");
            Equal("surface", (string)f.Requests.Last()[3], "provider receives real declared surface identity"); Equal(20L, (long)f.Requests.Last()[0], "provider request is bounded to anchor"); Equal(10L, (long)f.Requests.Last()[1], "provider request is bounded to owning PB");
            Check((int)f.Requests.Last()[14] <= 4096 && (int)f.Requests.Last()[15] <= 4096, "native request size remains bounded");
            Check(f.RayHit(a) && f.Pb.Click(a), "Camera A button remains functional after replacement");
            var back = Poll(f, handle); Check((bool)f.Invoke("attach-source", handle, "viewport", "camera-panorama", back.Single().Item3 == "camera-a" ? "101" : "102", CameraSettings), "PB switches back through ordinary authored action");
            Equal("101", f.Data.SourceSlots.Single().SourceId, "camera selection switches back to A"); Equal(0, Poll(f, handle).Length, "owned poll queue drains once"); Equal(0, f.Pb.TryRuns, "HTML actions remain PB-polled metadata");
        }
    }
    static void ShapeAndContentMatrix()
    {
        using var registeredMaterial = new F.ImageDefinitionScope();
        foreach (string shape in new[] { "plane", "cylinder", "sphere", "ellipsoid", "mesh" })
        {
            using var f = new F(); f.CreateShape(shape); long h = f.Bind(Markup, Css);
            Check((bool)f.Invoke("attach-source", h, "viewport", "fixture-texture", "registered"), shape + " admits generic native texture source");
            f.Prepare(); string slotId = f.Data.SourceSlots.Single().Id; var canvas = SourceMesh(f, slotId);
            Check(canvas != null && canvas.Geometry.Points.All(p => Math.Abs(p.Z) < 1e-10), shape + " source is clipped in canvas before warp");
            var mapped = f.Map("slot:" + slotId, canvas);
            Check(mapped != null && mapped.Geometry.Triangles.Length >= 6, shape + " source maps through production surface cache");
            Check(mapped.UV.Length == mapped.Geometry.Points.Length && mapped.UV.All(uv => uv.X >= .125 - 1e-6 && uv.X <= .875 + 1e-6 && uv.Y >= .25 - 1e-6 && uv.Y <= .75 + 1e-6), shape + " mapped source retains finite normalized texture UVs");
            Check(f.RayHit(Widget(f, h, "camera-a")) && f.RayHit(Widget(f, h, "camera-b")), shape + " real HTML buttons are ray-pickable");
            // The same selected screen accepts ordinary vector, SVG, text, image,
            // sprite and effects declarations next to HTML and provider content.
            f.Draw("line", "vector", new Vector3D(-.8, -.4, 0), new Vector3D(.8, -.4, 0), "orange");
            f.Draw("svg", "svg", "<svg viewBox='0 0 20 20'><rect width='20' height='20' fill='cyan'/></svg>", MatrixD.CreateScale(.004) * MatrixD.CreateTranslation(.5, .3, 0), 6);
            f.Draw("text", "label", "LIVE", new Vector3D(.4, .35, 0), .04d, "white");
            f.Draw("image", "image", "Fixture", new Vector2(.12f, .08f), "white");
            f.Draw("transform", "image", MatrixD.CreateTranslation(.7, .2, 0));
            f.Draw("effect", "vector", "flicker", .1d, 12d);
            f.Draw("sprites", new[] { new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(384, 200), new Vector2(18, 18), Color.Yellow) }, 480d, 280d);
            for (int i = 0; i < 16; i++)
            {
                f.Advance(); object[] sample = { f.Scene, f.Pb.Screens["10:surface"], f.Cache, 100 }; F.Call(f.Pb.Core, "SampleProjectedItems", sample);
                F.Call(f.Pb.Core, "CompileQueuedDisplay");
            }
            var entries = (IList)F.Field(f.Cache, "Items"); Check(entries.Count >= Publication(f, h).Items.Count + 4, shape + " projected sampler joins HTML and existing vector/SVG/text/image content");
            var item = f.Pb.Items["10:!s!surface!vector"]; var solid = new SurfaceMesh { Geometry = (Geometry)F.Field(item, "Geometry") };
            Check(f.Map("item:vector", solid, (MatrixD)F.Field(item, "Transform"), Vector4.Zero, new Vector4(1, .5f, 0, 1)) != null, shape + " ordinary vector shares source surface mapping");
            var imageItem = f.Pb.Items["10:!s!surface!image"];
            var imageMesh = f.Map("item:image", new SurfaceMesh { Geometry = (Geometry)F.Field(imageItem, "Geometry"), UV = (Vector2[])F.Field(imageItem, "UV") }, (MatrixD)F.Field(imageItem, "Transform"));
            Check(imageMesh != null && imageMesh.UV.Length == imageMesh.Geometry.Points.Length && imageMesh.UV.All(uv => uv.X >= 0 && uv.X <= 1 && uv.Y >= 0 && uv.Y <= 1), shape + " registered image material retains texture coordinates through same surface");
            f.Advance(); object[] prepare = { f.Scene, f.Pb.Screens["10:surface"], f.Cache, 100 }; F.Call(f.Pb.Core, "PrepareProjectedSources", prepare);
            var sprites = (SurfaceMesh)F.Field(f.Cache, "Sprites"); Check(sprites != null, shape + " actual sprite command compiles while provider slots remain attached");
            Check(f.Map("sprites", sprites, MatrixD.CreateScale(f.Data.CanvasWidth, f.Data.CanvasHeight, 1)) != null, shape + " sprites share surface mapping");
            Check((bool)f.Invoke("attach-source", h, "viewport", "fixture-vector", "vector-feed"), shape + " generic vector provider can replace native texture in same node");
            f.Prepare(); var vector = SourceMesh(f, slotId); Check(vector != null && f.Map("slot:" + slotId, vector) != null, shape + " generic vector provider maps on same surface");
            Check(f.Pb.Items.Contains("10:!s!surface!vector") && f.Data.Sprites != null, shape + " provider replacement preserves other composed content");
        }
    }
    static void RegionLifecycleAndInputFence()
    {
        using var f = new F(); f.CreateShape("ellipsoid");
        string markup = "<div id='clip'><div id='viewport'></div></div><button id='camera-a' data-action='camera-a'>Camera A</button>";
        string css = "#clip {width:140px;height:70px;overflow:hidden;margin-left:30px;} #viewport {width:240px;height:100px;padding:10px;background:#112233;opacity:.8;} button {width:110px;height:28px;}";
        long h = f.Bind(markup, css); Check((bool)f.Invoke("attach-source", h, "viewport", "camera-panorama", "101"), "clipped source attaches"); SourceRegion(f, h, "viewport");
        var slot = f.Data.SourceSlots.Single(); Check(slot.Clip[2] < slot.Rect[2] && slot.Clip[3] < slot.Rect[3], "ancestor overflow clips source without rescaling original node"); Near(.8, slot.Opacity, "permitted leaf HTML opacity reaches source");
        f.Prepare(); var canvas = SourceMesh(f, slot.Id); Check(canvas != null, "clipped source produces bounded canvas");
        Check(canvas.Geometry.Points.All(p => p.X >= slot.Clip[0] - 1e-6 && p.X <= slot.Clip[0] + slot.Clip[2] + 1e-6 && p.Y >= slot.Clip[1] - 1e-6 && p.Y <= slot.Clip[1] + slot.Clip[3] + 1e-6), "source mesh obeys exact ancestor clipping before warp");
        int requested = f.Requests.Count;
        Check((bool)f.Invoke("replace", h, markup, css + " #viewport {display:none;}", F.Width, F.Height), "hidden node source replacement parses"); f.Update();
        Equal(0, f.Data.SourceSlots.Count, "hidden node releases declared active source slot"); Check(SourceStatus(f, h).Item2.StartsWith("Hidden"), "desired hidden attachment reports hidden"); f.Prepare(); Equal(requested, f.Requests.Count, "hidden node issues no native frame demand");
        Check((bool)f.Invoke("replace", h, markup, css, F.Width, F.Height), "shown node restores desired source"); f.Update();
        Equal(slot.Id, f.Data.SourceSlots.Single().Id, "showing restores stable source attachment identity"); SourceRegion(f, h, "viewport");
        Check((bool)f.Invoke("resize", h, 360d, 180d), "resize queues layout"); f.Update();
        var reflow = f.Document(h).Controller.Frame.SourceRegions.Single(r => r.NodeId == "viewport"); Check(reflow.Clip.Width <= 140 && reflow.Clip.Height <= 70, "reflow recomputes bounded visible source region");
        Check((bool)f.Invoke("replace", h, "<button id='camera-a' data-action='camera-a'>A</button>", "button {width:110px;height:28px;}", F.Width, F.Height), "source node removal accepted"); f.Update();
        Equal(0, f.Data.SourceSlots.Count, "removed source node removes active slot"); f.Prepare(); Equal(requested, f.Requests.Count, "removed node leaves no native frame request");
        Reject(() => f.Invoke("attach-source", h, "missing", "camera-panorama", "101"), "attachment requires explicit existing node");
        var old = f.Document(h).PublishedFrame;
        foreach (string unsafeMarkup in new[] { "<script>alert(1)</script>", "<button id='bad' onclick='run()'>Bad</button>", "<img src='https://example.com/a.png'/>" })
        { Check(!(bool)f.Invoke("replace", h, unsafeMarkup, "", F.Width, F.Height), "parser continues rejecting executable or remote content"); Check(ReferenceEquals(old, f.Document(h).PublishedFrame), "parser rejection preserves last committed frame"); }
        Check((bool)f.Invoke("detach-source", h, "viewport"), "removed node desired attachment can be detached"); Equal("Detached", SourceStatus(f, h).Item2, "detach clears desired source state");
    }
    static void RollbackSourceLossAndOwnership()
    {
        using var f = new F(); f.CreateShape("ellipsoid"); long h = f.Bind(Markup, Css);
        Check((bool)f.Invoke("attach-source", h, "viewport", "camera-panorama", "101"), "initial camera declaration");
        var tree = f.Document(h).Controller.Document; var committed = f.Document(h).PublishedFrame; var source = f.Data.SourceSlots.Single(); var button = Widget(f, h, "camera-a");
        var range = Widget(f, h, "gain"); long lease = f.Pb.Begin(range), uiRevision = f.Pb.Display.Revision;
        f.FailSlotAt = f.SlotCalls + 1;
        Check(!(bool)f.Invoke("attach-source", h, "viewport", "camera-panorama", "102"), "source mutation failure surfaces through real public command");
        Equal("101", f.Data.SourceSlots.Single().SourceId, "failed source switch restores previous declaration"); Check(ReferenceEquals(committed, f.Document(h).PublishedFrame), "failed source commit preserves published frame");
        Check(ReferenceEquals(button, Widget(f, h, "camera-a")) && ReferenceEquals(range, Widget(f, h, "gain")), "source-only rollback preserves actual controls");
        Equal(uiRevision, f.Pb.Display.Revision, "source-only rollback leaves UI definitions unchanged"); Check(f.Pb.LeaseValid(lease), "failed camera switch preserves active range lease");
        f.FailSlotAt = 0; Check((bool)f.Invoke("attach-source", h, "viewport", "camera-panorama", "102"), "safe retry commits requested source"); button = Widget(f, h, "camera-a");
        f.Prepare(); Check(SourceMesh(f, source.Id) != null, "source frame admitted before loss");
        f.SourceLive = false; f.Prepare(); Check(SourceMesh(f, source.Id) == null && !SourceStatus(f, h).Item1, "lost native evidence cannot keep stale camera frame");
        Check(f.Data.SourceSlots.Single().SourceId == "102" && ReferenceEquals(tree, f.Document(h).Controller.Document) && ReferenceEquals(button, Widget(f, h, "camera-a")), "source loss retains committed HTML and desired attachment");
        f.SourceLive = true; f.ThrowFrame = true; f.Prepare(); Check(SourceMesh(f, source.Id) == null, "frame exception admits no geometry");
        f.ThrowFrame = false; f.InvalidFrame = true; f.Prepare(); Check(SourceMesh(f, source.Id) == null, "invalid native generation admits no geometry");
        f.InvalidFrame = false; f.Prepare(); Check(SourceMesh(f, source.Id) != null, "source recovers without document/control recreation");
        var other = (IMyProgrammableBlock)DrawTestProxy.Make(typeof(IMyProgrammableBlock), (m, a) => m.Name switch
        { "get_EntityId" => 11L, "get_OwnerId" => 77L, "get_Closed" => false, "get_Enabled" => true, "get_IsWorking" => true, "IsSameConstructAs" => true, "HasPlayerAccess" => true, _ => throw new Exception("foreign PB " + m.Name) });
        f.Entities[11] = other;
        var foreignEndpoint = (Func<string, object[], object>)F.Call(f.Html, "Endpoint", other);
        Reject(() => foreignEndpoint("attach-source", new object[] { h, "viewport", "camera-panorama", "101" }), "foreign PB cannot attach to owner's document");
        var foreignDemand = (MyTuple<int, int, double, bool>)F.Call(f.Pb.Core, "DisplaySourceService", "source-demand", new object[] { "camera-panorama", 20L, 11L, "102" });
        Check(!foreignDemand.Item4, "foreign PB cannot obtain demand for owner's source attachment");
        Check(F.Call(f.Pb.Core, "DisplaySourceService", "source-panoramasettings", new object[] { "camera-panorama", 20L, 11L, "102" }) is false, "foreign PB cannot read owner's camera settings declaration");
        var foreignDraw = (Func<string, object[], object>)F.Call(f.Pb.Core, "DrawEndpoint", other);
        foreignDraw("target", new object[] { f.Pb.Target }); Reject(() => foreignDraw("screen", new object[] { "surface" }), "foreign PB cannot select owner's declared source surface");
        Reject(() => foreignDraw("line", new object[] { "!s!surface!steal", Vector3D.Zero, Vector3D.UnitX, "white" }), "raw reserved IDs cannot escape ownership guard");
        foreignDraw("line", new object[] { "foreign", Vector3D.Zero, Vector3D.UnitX, "white" });
        var foreignUi = (Func<string, object[], object>)F.Call(f.Pb.Core, "UiEndpoint", other); foreignUi("target", new object[] { f.Pb.Target }); Reject(() => foreignUi("screen", new object[] { "surface" }), "foreign PB cannot map actions onto owner's screen");
        f.Invoke("destroy", h); Check(f.Pb.Items.Contains("11:foreign"), "owned cleanup preserves foreign PB artwork"); Equal(0, f.Data.SourceSlots.Count, "owned cleanup detaches only document sources"); Check(f.Pb.Screens.Contains("10:surface"), "document destruction preserves parent's authored ellipsoid surface");
        Equal(0, f.Pb.Display.Widgets.Count, "owned cleanup removes document controls");
    }
    static void NativeSpriteRelay()
    {
        foreach (string shape in new[] { "plane", "cylinder", "sphere", "ellipsoid", "mesh" })
        {
            using var f = new F(); f.CreateShape(shape);
            string markup = "<div id='viewport'></div><button id='camera-a' data-action='camera-a'>Camera A</button>";
            string css = "#viewport {width:180px;height:90px;margin-left:20px;margin-top:12px;background:#112233;} button {width:110px;height:28px;}";
            long h = (long)f.Invoke("bind-sprites-screen", f.Pb.Target, "surface", f.Pb.NativeSource, markup, css, F.CanvasPose, F.Units, 0, true);
            Equal(shape == "plane" ? 0 : shape == "cylinder" ? 1 : shape == "sphere" ? 2 : shape == "ellipsoid" ? 3 : 4, f.Data.SurfaceKind, shape + " native sprite relay preserves parent shape");
            Check((bool)f.Invoke("attach-source", h, "viewport", "camera-panorama", "101"), shape + " opaque native relay permits nonoverlapping camera window");
            Check(f.Data.SourceSlots.Count == 2 && f.Data.SourceSlots.Any(s => s.Provider == "lcd-texture") && f.Data.SourceSlots.Any(s => s.Provider == "camera-panorama"), shape + " native RGB artwork and camera are separate generic source slots");
            Check(((string[])f.Invoke("source-capabilities", h)).Contains("source-overlap-with-later-artwork-unsupported"), shape + " native RGB path reports exact ordering limitation");
            f.Register("lcd-texture", f.TextureProvider);
            f.Prepare(); Check(f.Data.SourceSlots.All(s => SourceMesh(f, s.Id) != null), shape + " bounded native relay and camera evidence admitted independently: " + string.Join("; ", f.Data.SourceSlots.Select(s => F.Field(f.SlotCache(s.Id), "Status"))));
            var before = f.Data.SourceSlots.Select(s => (s.Id, s.SourceId)).ToArray();
            Check((bool)f.Invoke("replace", h, "<div id='viewport'><button id='camera-a' data-action='camera-a'>Camera A</button></div>", css, f.Pb.SourceSize.X, f.Pb.SourceSize.Y), shape + " native overlapping source layout parses"); f.Update();
            Check(Status(f, h).Item4.Item2 && Status(f, h).Item4.Item3.Contains("Unsupported", StringComparison.OrdinalIgnoreCase), shape + " native opaque texture rejects later artwork overlap explicitly");
            Check(before.SequenceEqual(f.Data.SourceSlots.Select(s => (s.Id, s.SourceId))), shape + " unsupported overlap preserves last committed slot declaration");
            f.Invoke("destroy", h); Check(f.Pb.Screens.Contains("10:surface"), shape + " native document cleanup preserves parent surface");
        }
    }
    static void DemandPoseAndGeneration()
    {
        using var f = new F(); f.CreateShape("ellipsoid");
        var pose = MatrixD.CreateScale(.8, .8, 1) * MatrixD.CreateRotationZ(.18) * MatrixD.CreateTranslation(.05, -.02, 0);
        long h = f.Bind(Markup, Css, pose);
        Check((bool)f.Invoke("attach-source", h, "viewport", "camera-panorama", "101", CameraSettings), "rotated HTML pose accepts actual source rectangle");
        var slot = f.Data.SourceSlots.Single(); f.Prepare(); var canvas = SourceMesh(f, slot.Id);
        Check(canvas != null && canvas.Geometry.Points.All(p => Math.Abs(p.Z) < 1e-8), "source pose projects only in same finite canvas plane");
        var corners = new[] { new Vector3D(slot.Rect[0], slot.Rect[1], 0), new Vector3D(slot.Rect[0] + slot.Rect[2], slot.Rect[1], 0), new Vector3D(slot.Rect[0] + slot.Rect[2], slot.Rect[1] + slot.Rect[3], 0), new Vector3D(slot.Rect[0], slot.Rect[1] + slot.Rect[3], 0) }.Select(p => Vector3D.Transform(p, pose)).ToArray();
        Check(corners.All(p => canvas.Geometry.Points.Any(actual => Vector3D.Distance(actual, p) < 2e-6)), "renderer source rectangle receives exact authored affine canvas pose");
        Check(f.RayHit(Widget(f, h, "camera-a")), "rotated HTML button shares authoritative canvas pose");
        Check(f.Map("slot:" + slot.Id, canvas) != null, "rotated camera window uses parent ellipsoid mapping");
        MyTuple<int, int, double, bool> Demand(string source) => (MyTuple<int, int, double, bool>)F.Call(f.Pb.Core, "DisplaySourceService", "source-demand", new object[] { "camera-panorama", 20L, 10L, source });
        MyTuple<int, double[], bool> Density(string source) => (MyTuple<int, double[], bool>)F.Call(f.Pb.Core, "DisplaySourceService", "source-cameradensity", new object[] { "camera-panorama", 20L, 10L, source, new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1 } });
        var demand = Demand("101"); Check(demand.Item4 && demand.Item1 > 0 && demand.Item2 > 0 && demand.Item1 <= 4096 && demand.Item2 <= 4096, "visible node obtains bounded native source demand"); Near(30, demand.Item3, "source demand uses requested native cadence");
        var density = Density("101"); Check(density.Item3 && density.Item2.Length == 3 + 256 * 7, "density service evaluates real mapped source window with bounded per-camera field");
        Check(density.Item2.All(Geometry.Finite) && density.Item2[2] <= 512, "camera source field stays finite and respects configured capture maximum");
        Check(!Demand("102").Item4, "undeclared physical camera receives no demand");
        int calls = f.Requests.Count; long generation = f.Data.UiSurfaceGeneration;
        f.Draw("screen-slot", "outside", "fixture-texture", "outside", new Vector4(5, 5, .5f, .5f), new Vector4(5, 5, .5f, .5f), new Vector4(0, 0, 1, 1), 0, 1d);
        f.Prepare(); Check(f.Requests.Count == calls + 1 && f.Requests.All(r => (string)r[2] != "outside"), "out-of-canvas source performs no native acquisition while visible node continues refreshing");
        Equal(generation, f.Data.UiSurfaceGeneration, "source-slot additions preserve surface coordinate generation");
        f.Draw("screen-visible", false); f.Prepare(); calls = f.Requests.Count;
        Check(!Demand("101").Item4, "hidden parent receives no native demand");
        var hiddenDensity = Density("101"); Check(hiddenDensity.Item3 && hiddenDensity.Item1 == 0 && hiddenDensity.Item2[2] == 0, "hidden parent receives a proved zero camera-density request");
        f.Prepare(); Equal(calls, f.Requests.Count, "hidden parent performs no further native acquisition");
        Check(!f.RayHit(Widget(f, h, "camera-a")), "hidden mapped surface refuses prior HTML hotzone");
    }
    static void OrderedRasterComposition()
    {
        using var f = new F(); f.CreateShape("ellipsoid"); long h = f.Bind(Markup, Css);
        Check((bool)f.Invoke("attach-source", h, "viewport", "camera-panorama", "101"), "ordered raster composition retains generic camera slot");
        f.EnableRasterBackend(); f.Draw("screen-renderer", "raster", 128, 80, 1);
        f.Prepare(); var slot = f.Data.SourceSlots.Single();
        for (int i = 0; i < 16; i++)
        {
            f.Advance(); object[] sample = { f.Scene, f.Pb.Screens["10:surface"], f.Cache, 100 };
            F.Call(f.Pb.Core, "SampleProjectedItems", sample); F.Call(f.Pb.Core, "CompileQueuedDisplay");
        }
        // Zero output opacity keeps the real ordering/mapping/raster paths active
        // while production alpha rejection prevents native billboard submission.
        for (int i = 0; i < 4; i++)
        {
            f.Advance(); object[] draw = { f.Scene, f.Cache, f.Data, false, null, 0f, MatrixD.Identity, MatrixD.Identity, f.Eye, 0u, 1000, 100 };
            F.Call(f.Pb.Core, "DrawCompositionContent", draw);
        }
        var chunks = (IDictionary)F.Field(f.Cache, "UiChunks"); Equal(2, chunks.Count, "real ordered renderer splits opaque HTML background and foreground around camera source");
        Equal(2, f.RasterUploads.Count, "real raster compositor uploads each stable contiguous artwork run once: " + string.Join("; ", chunks.Values.Cast<object>().Select(c => F.Field(c, "UiRasterError"))));
        Check(f.RasterUploads.Select(u => u.Key).Distinct().Count() == 2, "before/after artwork runs have independent bounded backend leases");
        Check(f.RasterUploads.All(u => u.Size == new Vector2I(128, 80) && u.Rgba.Length == 128 * 80 * 4), "actual CPU raster images retain configured viewport dimensions");
        Check(f.RasterUploads.All(u => Enumerable.Range(0, u.Rgba.Length / 4).Any(p => u.Rgba[p * 4 + 3] == 0) && Enumerable.Range(0, u.Rgba.Length / 4).Any(p => u.Rgba[p * 4 + 3] > 0)), "each composed UI texture retains background transparency and painted coverage");
        var mapped = (IDictionary)F.Field(f.Cache, "Mapped");
        Check(mapped.Contains("chunk:0") && mapped.Contains("slot:" + slot.Id) && mapped.Contains("chunk:1"), "actual ordered renderer maps both UI raster runs and native camera on ellipsoid");
        double before = (double)F.Field(mapped["chunk:0"], "Depth"), camera = (double)F.Field(mapped["slot:" + slot.Id], "Depth"), after = (double)F.Field(mapped["chunk:1"], "Depth");
        Check(before < camera && camera < after, "actual renderer depth places camera above viewport background and below HTML controls");
        Check(f.RayHit(Widget(f, h, "camera-a")) && f.RayHit(Widget(f, h, "camera-b")), "raster artwork preserves independent authoritative HTML ray picking");
        int uploads = f.RasterUploads.Count; long generation = f.Data.UiSurfaceGeneration;
        Check((bool)f.Invoke("attach-source", h, "viewport", "camera-panorama", "102"), "raster composition switches camera through public HTML API");
        f.Prepare(); f.Advance(); object[] redraw = { f.Scene, f.Cache, f.Data, false, null, 0f, MatrixD.Identity, MatrixD.Identity, f.Eye, 0u, 1000, 100 }; F.Call(f.Pb.Core, "DrawCompositionContent", redraw);
        Equal(uploads, f.RasterUploads.Count, "camera frame replacement reuses unchanged before/after HTML raster images");
        Equal(generation, f.Data.UiSurfaceGeneration, "camera replacement retains raster interaction generation");
    }
    static void LegacyCompositionRetirement()
    {
        foreach (string transition in new[] { "detach", "slot-hidden", "node-hidden" })
        {
            using var f = new F(); f.CreateShape("ellipsoid"); long h = f.Bind(Markup, Css);
            f.Invoke("attach-source", h, "viewport", "camera-panorama", "101");
            f.EnableRasterBackend(); f.Draw("screen-renderer", "raster", 128, 80, 1); f.Prepare();
            var slot = f.Data.SourceSlots.Single();
            for (int i = 0; i < 16; i++)
            {
                f.Advance(); object[] sample = { f.Scene, f.Pb.Screens["10:surface"], f.Cache, 100 }; F.Call(f.Pb.Core, "SampleProjectedItems", sample); F.Call(f.Pb.Core, "CompileQueuedDisplay");
            }
            for (int i = 0; i < 4; i++)
            {
                f.Advance(); object[] draw = { f.Scene, f.Cache, f.Data, false, null, 0f, MatrixD.Identity, MatrixD.Identity, f.Eye, 0u, 1000, 100 }; F.Call(f.Pb.Core, "DrawCompositionContent", draw);
            }
            var chunks = (IDictionary)F.Field(f.Cache, "UiChunks");
            Check(chunks.Count == 2 && chunks.Values.Cast<object>().All(c => F.Field(c, "UiRaster") != null), transition + " begins with admitted before/after raster chunks");
            var oldLeases = chunks.Values.Cast<object>().Select(c => F.Field(F.Field(c, "UiRaster"), "Lease")).ToArray();
            var oldMeshes = chunks.Values.Cast<object>().Select(c => (SurfaceMesh)F.Field(c, "UiRasterMesh")).ToArray();
            var sourceEvidence = F.Field(F.Field(f.SlotCache(slot.Id), "Source"), "SourceEvidence");
            int uploads = f.RasterUploads.Count;
            string effectArtwork = Publication(f, h).Controls.Single(c => c.Node == "camera-a").Artwork;
            f.Draw("effect", effectArtwork, "flicker", .1d, 12d);
            if (transition == "detach") Check((bool)f.Invoke("detach-source", h, "viewport"), "last active source detaches through public HTML API");
            else if (transition == "slot-hidden") { f.Draw("screen-slot-visible", slot.Id, false); Check(!f.Data.SourceSlots.Single().Visible, "last declared source can become hidden without removal"); }
            else { Check((bool)f.Invoke("replace", h, Markup, Css + " #viewport {display:none;}", F.Width, F.Height), "hidden-node transition parses through public HTML API"); f.Update(); }
            f.DrawLegacyClipped();
            Equal(0, chunks.Count, transition + " actual legacy branch retires obsolete raster chunk caches");
            Check(oldLeases.All(lease => f.RasterReleased.Count(released => ReferenceEquals(released, lease)) == 1), transition + " old contiguous-raster leases release exactly once");
            Check(f.Released.Any(proof => ReferenceEquals(proof, sourceEvidence)), transition + " no-active-source transition releases camera evidence");
            var mapped = (IDictionary)F.Field(f.Cache, "Mapped");
            Check(mapped.Keys.Cast<string>().All(id => !id.StartsWith("chunk:", StringComparison.Ordinal) && !id.StartsWith("slot:", StringComparison.Ordinal)), transition + " parent mapped cache has no obsolete slot/chunk geometry");
            Check(F.Field(f.Cache, "UiRaster") != null && F.Field(f.Cache, "UiRasterMesh") != null && f.RasterUploads.Count == uploads + 1 && !f.RasterUploads.Last().Key.Contains(":chunk:"), transition + " actual legacy branch admits one whole-document raster image");
            var entries = (IList)F.Field(f.Cache, "Items");
            var effected = entries.Cast<object>().Single(e => F.Field(e, "Id").Equals("!s!surface!" + effectArtwork));
            Check(F.Field(F.Field(effected, "Item"), "Effects") != null && !(bool)F.Call(f.Pb.Core, "CompositionEntryRasterized", f.Cache, effected), transition + " obsolete composition chunks no longer suppress effect processing");
            // Canonical item accounting remains unchanged while the independent UI
            // reservation is exactly the current whole-frame quad, not old chunks.
            var legacyMesh = (SurfaceMesh)F.Field(f.Cache, "UiRasterMesh");
            object[] active = { f.Scene, null, null, false, false, 0, 0 }; F.Call(f.Pb.Core, "ActiveRenderCounts", active);
            F.Set(f.Cache, "UiRasterMesh", null); object[] baseline = { f.Scene, null, null, false, false, 0, 0 };
            try { F.Call(f.Pb.Core, "ActiveRenderCounts", baseline); }
            finally { F.Set(f.Cache, "UiRasterMesh", legacyMesh); }
            Equal(legacyMesh.Geometry.Points.Length, (int)active[5] - (int)baseline[5], transition + " active point admission reserves only current legacy UI mesh");
            Equal(legacyMesh.Geometry.Triangles.Length / 3 + legacyMesh.Geometry.Edges.Length / 2, (int)active[6] - (int)baseline[6], transition + " active primitive admission reserves only current legacy UI mesh");
            Check(oldMeshes.All(mesh => !ReferenceEquals(mesh, legacyMesh)), transition + " legacy admission replaces old chunk geometry ownership");
        }
    }
    static int Main(string[] args)
    {
        Run("Console/Projector ellipsoid HTML camera buttons and retained source switch", Acceptance);
        Run("all projected shapes compose HTML/vector/SVG/text/registered images/sprites/effects/generic providers", ShapeAndContentMatrix);
        Run("actual node content/clip/opacity, hide/reflow/removal and parser preservation", RegionLifecycleAndInputFence);
        Run("publication rollback, camera loss/recovery and parent/foreign ownership", RollbackSourceLossAndOwnership);
        Run("all shapes native sprite relay with explicit opaque overlap rejection", NativeSpriteRelay);
        Run("affine canvas pose, bounded node camera demand/density and hidden/outside acquisition", DemandPoseAndGeneration);
        Run("actual ordered raster renderer with fake native leases and no billboard submits", OrderedRasterComposition);
        Run("final detach/slot-hide/node-hide retires raster chunks before actual legacy admission", LegacyCompositionRetirement);
        if (args.Length > 0)
        {
            Directory.CreateDirectory(args[0]);
            File.WriteAllText(Path.Combine(args[0], "composition-results.json"), JsonSerializer.Serialize(new
            { Cases = cases, Assertions = assertions, Failures = failures, LiveGameValidation = false, LivePixels = false, GpuPerformanceMeasured = false, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"Composition integration checks: {assertions} assertions, {cases} cases, {failures} failures.");
        return failures == 0 ? 0 : 1;
    }
}
