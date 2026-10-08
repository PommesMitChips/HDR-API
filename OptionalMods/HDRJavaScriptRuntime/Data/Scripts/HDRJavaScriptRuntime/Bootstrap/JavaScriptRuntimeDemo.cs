using System;

namespace HDRJavaScriptRuntime
{
    public sealed partial class JavaScriptRuntimeSession
    {
        const string DemoOwner = "hdrjs.demo";
        const string DemoMarkup = "<main><button id='increment' data-action='increment'>Increment</button><p id='status'>Count: 0</p><p>Use /hdrjs click for an explicit synthetic click.</p></main>";
        const string DemoStyles = "main {padding:14px;width:360px;height:180px;background-color:#123442;color:#00d0cd;font-size:16px;} button {width:130px;height:36px;background-color:#245a67;} p {margin:8px 0;}";
        const string DemoScript = "var count=0; hdr.on('click','increment',function(e){count++;hdr.setText('status','Count: '+count);}); hdr.log('Local JavaScript counter ready.');";
        Owner demoOwner;
        long demoRealm;

        void DemoChat(string message, ref bool send)
        {
            if (message == null || !(message.Equals("/hdrjs", StringComparison.OrdinalIgnoreCase) || message.StartsWith("/hdrjs ", StringComparison.OrdinalIgnoreCase))) return;
            send = false;
            try
            {
                string command = message.Length <= 7 ? "status" : message.Substring(7).Trim().ToLowerInvariant();
                if (command == "demo") StartDemo();
                else if (command == "clear") { ClearDemo(); Notice("Owned JavaScript demo cleared."); }
                else if (command == "click") ClickDemo();
                else if (command == "status") DemoStatus();
                else Notice("Commands: /hdrjs demo, status, click, clear. Loading the mod never starts the demo automatically.");
            }
            catch (Exception error) { Notice(error.Message); }
        }
        void StartDemo()
        {
            ClearDemo();
            var endpoint = Service("open", new object[] { DemoOwner }) as Func<string, object[], object>;
            demoOwner = owners[DemoOwner];
            try
            {
                demoRealm = (long)endpoint("create-hud", new object[] { DemoMarkup, DemoStyles, 400.0, 220.0,
                    new[] { "status" }, new string[0], "vector" });
                if (!(bool)endpoint("execute", new object[] { demoRealm, DemoScript }))
                    throw new InvalidOperationException(demoOwner.Realms[demoRealm].LastError ?? "JavaScript demo stopped.");
                Notice("Plugin-free HUD counter active. /hdrjs click delivers a cooperative synthetic HTML pointer click; /hdrjs clear removes this demo.");
            }
            catch { ClearDemo(); throw; }
        }
        void ClickDemo()
        {
            if (demoOwner == null || !Current(demoOwner) || demoRealm == 0) throw new InvalidOperationException("Run /hdrjs demo first.");
            Realm realm;
            if (!demoOwner.Realms.TryGetValue(demoRealm, out realm) || !realm.Current || demoOwner.Html == null)
                throw new InvalidOperationException("JavaScript demo/document is retired; run /hdrjs demo again.");
            // The first button has an explicit 130x36 box inside 14px padding. This command explicitly owns the synthetic press.
            Command(demoOwner, "pointer", new object[] { demoRealm, 30.0, 30.0, false });
            Command(demoOwner, "pointer", new object[] { demoRealm, 30.0, 30.0, true });
            Command(demoOwner, "pointer", new object[] { demoRealm, 30.0, 30.0, false });
            Notice("Synthetic HTML click queued; the registered JavaScript closure handles it on the next update.");
        }
        void DemoStatus()
        {
            if (demoOwner == null || !Current(demoOwner) || demoRealm == 0)
            { Notice(Protocol + " / " + JavaScriptRealm.Profile + " / no demo active. Requires mod: HDR HTML Frontend and HDR API only for the optional HUD demo; the interpreter needs no plugin."); return; }
            Realm realm;
            if (!demoOwner.Realms.TryGetValue(demoRealm, out realm)) { Notice("No live demo realm."); return; }
            Notice("Demo realm " + realm.Handle + " / HTML document " + realm.Document + " / active " + realm.Current +
                " / " + (realm.LastError ?? (realm.Bridge == null ? null : realm.Bridge.LastError) ?? "ready") + "; native game input is not captured.");
        }
        void ClearDemo()
        {
            if (demoOwner != null)
            {
                ReleaseOwner(demoOwner);
                Owner current;
                if (owners.TryGetValue(DemoOwner, out current) && ReferenceEquals(current, demoOwner)) owners.Remove(DemoOwner);
            }
            demoOwner = null; demoRealm = 0;
        }
    }
}
