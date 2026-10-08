using System;
using Hdr.Mods;
using HDRJavaScriptRuntime;

namespace HdrJavaScriptExamples
{
    /// <summary>Call from the consumer's session and cooperative GUI. Pure Workshop-mod vector HUD; no renderer plugin required.</summary>
    public sealed class JavaScriptHudDemo : IDisposable
    {
        const string Markup = "<main><h2>Local JavaScript</h2><p id='status'>Count: 0</p><button id='increment' data-action='increment'>Increment</button></main>";
        const string Styles = "main {padding:14px;width:300px;height:160px;background-color:#123442;color:#00d0cd;font-size:16px;} h2 {font-size:22px;} button {width:130px;height:36px;background-color:#245a67;}";
        const string Script = @"
var count = 0;
hdr.on('click', 'increment', function(e) {
  count++;
  hdr.setText('status', 'Count: ' + count);
});
hdr.setTimeout(function() { hdr.setText('status', 'Count: ' + count + ' / JS ready'); }, 500);
";
        readonly HdrHtmlApi html = new HdrHtmlApi("example.javascript-counter");
        JavaScriptDocumentBridge bridge;
        long document, generation;
        int tick;
        public string LastError { get; private set; }

        public void Update()
        {
            tick++;
            if (tick % 60 == 0) html.Request();
            if (!html.Ready)
            { if (bridge != null) { bridge.Dispose(); bridge = null; } document = 0; LastError = "Requires mod: HDR HTML Frontend."; return; }
            try
            {
                if (generation != html.ConnectionGeneration)
                { if (bridge != null) bridge.Dispose(); bridge = null; document = 0; generation = html.ConnectionGeneration; }
                if (document == 0)
                {
                    document = html.CreateHud(Markup, Styles, 340, 200, "vector");
                    var host = new HdrHtmlDocumentHost(html, document, new[] { "status" });
                    bridge = new JavaScriptDocumentBridge(new JavaScriptRealm(new JavaScriptLimits()), host,
                        new JavaScriptHostLimits(), value => LastError = value);
                    bridge.Update(tick * (1000.0 / 60));
                    bridge.RegisterScript(Script);
                }
                if (bridge != null)
                { bridge.Update(tick * (1000.0 / 60)); LastError = bridge.LastError; }
            }
            catch (Exception error) { LastError = error.Message; }
        }

        /// <summary>The consumer's GUI must already own/block this press. JS does not acquire native game input.</summary>
        public void OnOwnedPointer(double x, double y, bool held, bool guiOwnsFocus)
        {
            if (!html.Ready || generation != html.ConnectionGeneration || document == 0) return;
            if (guiOwnsFocus) html.Pointer(document, x, y, held); else html.CancelPointer(document);
        }
        public void Dispose()
        { if (bridge != null) bridge.Dispose(); bridge = null; html.Dispose(); document = 0; }
    }
}
