using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HDRJavaScriptRuntime;
using Hdr.Html;
using Hdr.Mods;
using VRage;
using VRageMath;

internal static class JavaScriptHtmlSeamTests
{
    private static int assertions, cases, failures;
    private static void Check(bool value, string message)
    { assertions++; if (!value) throw new InvalidOperationException("Assertion failed: " + message); }
    private static void Case(string name, Action test)
    { cases++; try { test(); Console.WriteLine("PASS " + name); } catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error); } }
    public static int Main(string[] args)
    {
        HostTests.RunJavaScriptSeams(Check, Case);
        if (args.Length != 1) throw new ArgumentException("Seam receipt output path required.");
        File.WriteAllText(args[0], "{\"Passed\":" + (failures == 0 ? "true" : "false") + ",\"Assertions\":" + assertions + ",\"Cases\":" + cases + ",\"Failures\":" + failures + ",\"GameContextInitialized\":false,\"PluginRequired\":false}");
        Console.WriteLine("JavaScript HTML seam checks: " + assertions + " assertions, " + cases + " cases, " + failures + " failures.");
        return failures == 0 ? 0 : 1;
    }
}

// The existing HTML gate doubles only Core's external rendering boundary. All HTML
// parsing/layout/mutation/claim/input/service code and the JS runtime/service below
// are the real source, exercised through their actual public mod helpers.
static partial class HostTests
{
    private static Action<bool, string> javascriptAssert;
    public static void RunJavaScriptSeams(Action<bool, string> assert, Action<string, Action> run)
    {
        javascriptAssert = assert;
        run("actual HTML published counter driven by JS closure", JavaScriptActualCounter);
        run("actual HTML source and artwork batch rolls back on failure", JavaScriptActualRollback);
        run("runtime-owned actual HUD pointer and stopped-frame cleanup", JavaScriptOwnedHud);
    }
    private static void JsUnload(object session)
    { session.GetType().GetMethod("UnloadData", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(session, null); }
    private static MyTuple<string, object[]> JsSetting(string name, object value)
    { return new MyTuple<string, object[]>(name, new[] { value }); }

    private static void JavaScriptActualCounter()
    {
        using (var bus = new Bus())
        {
            var core = new Core(); bus.CoreService = core.Service;
            var htmlSession = new HtmlFrontendSession(); htmlSession.BeforeStart();
            var jsSession = new JavaScriptRuntimeSession(); jsSession.BeforeStart();
            using (var html = new HdrHtmlApi("js.actual.html"))
            using (var js = new HdrJavaScriptApi("js.actual.counter"))
            {
                js.Configure(new[] { JsSetting("timeout-ms", 1000.0) });
                long document = html.CreateHud("<button id='next' data-action='next'>Next</button><p id='status'>Count: 0</p>", "button {width:120px;height:32px;} p {height:24px;font-size:8px;}", 400, 200, "vector");
                javascriptAssert(Artwork(core).Contains("Count: 0"), "actual HTML publishes initial vector text");
                long generation = html.ConnectionGeneration;
                long realm = js.BindHtml((op, values) => html.Call(op, values), document, generation, () => html.Ready && html.ConnectionGeneration == generation, new[] { "status" });
                javascriptAssert(js.Execute(realm, "var count=0;hdr.on('click','next',function(e){count++;hdr.setText('status','Count: '+count);});"), "actual mod service registers closure on actual retained HTML");
                var before = html.Status(document);
                html.Pointer(document, 20, 16, true); html.Pointer(document, 20, 16, false);
                jsSession.UpdateAfterSimulation();
                var after = html.Status(document);
                javascriptAssert(Artwork(core).Contains("Count: 1") && !Artwork(core).Contains("Count: 0"), "actual hit/event/JS/mutate chain publishes updated rendered counter");
                javascriptAssert(after.Item3 == before.Item3 + 1 && after.Item5 == before.Item5 + 1, "one callback commits one layout/publication revision");
                html.Pointer(document, 20, 16, true); html.Pointer(document, 20, 16, false); jsSession.UpdateAfterSimulation();
                javascriptAssert(Artwork(core).Contains("Count: 2"), "retained JS closure survives actual document publication");
                string committed = Artwork(core);
                javascriptAssert(!js.Execute(realm, "hdr.setText('status','not committed');throw new Error('expected');"), "actual bound script error returns safe false");
                javascriptAssert(Artwork(core) == committed && html.Status(document).Item3 > 0, "script error retains real previously published artwork/hit revision");
                javascriptAssert(!js.Status(realm).Item1 && js.Status(realm).Item2.Contains("expected"), "actual realm stops and reports error");
                js.DisposeRealm(realm);
                javascriptAssert(html.Status(document).Item3 > 0 && Artwork(core) == committed, "JS disposal preserves C# consumer-owned HTML");
                html.Destroy(document);
            }
            JsUnload(jsSession); JsUnload(htmlSession);
            javascriptAssert(bus.ChatSubscriptions == 0, "both actual services unsubscribe cleanly");
        }
    }

    private static void JavaScriptActualRollback()
    {
        using (var bus = new Bus())
        {
            var core = new Core(); bus.CoreService = core.Service;
            var htmlSession = new HtmlFrontendSession(); htmlSession.BeforeStart();
            var jsSession = new JavaScriptRuntimeSession(); jsSession.BeforeStart();
            using (var html = new HdrHtmlApi("js.actual.source"))
            using (var js = new HdrJavaScriptApi("js.actual.rollback"))
            {
                js.Configure(new[] { JsSetting("timeout-ms", 1000.0) });
                long document = html.CreateSurface(MutationMarkup, MutationCss, 400, 240, MatrixD.Identity, .005, "ellipsoid", new object[] { new Vector3D(2, 1, 2), Math.PI * 2, Math.PI, "inside" }, AnchorSettings(SourceAnchor()));
                javascriptAssert(html.AttachSource(document, "camera", "camera-panorama", "101"), "actual source fixture publishes initial camera slot");
                string original = Artwork(core); var status = html.Status(document);
                long generation = html.ConnectionGeneration;
                long realm = js.BindHtml((op, values) => html.Call(op, values), document, generation, () => html.Ready && html.ConnectionGeneration == generation, new[] { "label" });
                js.AddSourceChoice(realm, "camera2", "camera", "camera-panorama", "102", () => new MyTuple<bool, string>(true, null));
                javascriptAssert(js.Execute(realm, "function update(){hdr.setText('label','candidate');hdr.selectSource('camera2');return true;}"), "actual JS named function prepares combined artwork/source transaction");
                core.FailSlotOnce = true;
                bool rejected = false;
                try { js.CallFunction(realm, "update"); } catch (InvalidOperationException) { rejected = true; }
                javascriptAssert(rejected && !js.Status(realm).Item1, "real source publication failure retires JS association safely");
                javascriptAssert(Artwork(core) == original && html.Status(document).Item3 == status.Item3, "real frontend rollback restores prior artwork and published revision");
                javascriptAssert(core.Surfaces.Values.Single().Slots.Single().Value.Source == "101", "real frontend rollback restores camera source");
                var token = new object();
                javascriptAssert(html.ClaimScript(document, token), "failed JS dispatch releases actual retained document claim");
                html.ReleaseScript(document, token);
                js.DisposeRealm(realm); html.Destroy(document);
            }
            JsUnload(jsSession); JsUnload(htmlSession);
        }
    }

    private static void JavaScriptOwnedHud()
    {
        using (var bus = new Bus())
        {
            var core = new Core(); bus.CoreService = core.Service;
            var htmlSession = new HtmlFrontendSession(); htmlSession.BeforeStart();
            var jsSession = new JavaScriptRuntimeSession(); jsSession.BeforeStart();
            using (var js = new HdrJavaScriptApi("js.owned.hud"))
            {
                js.Configure(new[] { JsSetting("timeout-ms", 1000.0) });
                long realm = js.CreateHud("<button id='next' data-action='next'>Next</button><p id='status'>Owned: 0</p>", "button {width:120px;height:32px;} p {height:24px;font-size:8px;}", 400, 200, new[] { "status" });
                javascriptAssert(js.Status(realm).Item5 > 0 && Artwork(core).Contains("Owned: 0"), "runtime convenience creates and owns actual retained HUD");
                javascriptAssert(js.Execute(realm, "var count=0;hdr.on('click','next',function(){count++;hdr.setText('status','Owned: '+count);});"), "owned HUD installs real closure");
                js.Pointer(realm, 20, 16, true); js.Pointer(realm, 20, 16, false); jsSession.UpdateAfterSimulation();
                javascriptAssert(Artwork(core).Contains("Owned: 1"), "owned cooperative pointer reaches actual HTML button and JS-rendered counter");
                bool nonfinite = false;
                try { js.Pointer(realm, double.NaN, 10, true); } catch (ArgumentException) { nonfinite = true; }
                javascriptAssert(nonfinite, "nonfinite owned pointer rejected before HTML input");
                string published = Artwork(core);
                javascriptAssert(!js.Execute(realm, "hdr.setText('status','bad');throw new Error('stop owned');"), "owned document script error stops safely");
                javascriptAssert(!js.Status(realm).Item1 && Artwork(core) == published && core.Surfaces.Count == 1, "automatic realm failure retains actual owned HUD's committed frame");
                js.CancelPointer(realm);
                javascriptAssert(Artwork(core) == published, "stopped HUD input cancellation cannot resume script or mutate artwork");
                js.DisposeRealm(realm);
                javascriptAssert(core.Surfaces.Count == 0, "later explicit disposal destroys previously stopped runtime-owned HUD");
            }
            JsUnload(jsSession); JsUnload(htmlSession);
        }
    }
}
