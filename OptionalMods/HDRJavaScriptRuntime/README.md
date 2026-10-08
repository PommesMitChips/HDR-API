# HDR JavaScript Runtime 0.1.0

**ALPHA — mod-native prototype.** This optional mod executes UI JavaScript through a managed interpreter compiled from mod source. It requires no client plugin to interpret JavaScript or update ordinary retained HTML text/data. Runtime source has passed a preliminary actual installed-game ModApi/C# 6 check, and owned host behavior has offline fixtures. The maintained build reruns the complete current-source compiler/behavior/package gates; final acceptance receipts are separate from this guide. Live game/input/GPU/multiplayer behavior remains unverified.

The adapted engine is **Jint 2.11.10**, pinned to commit [`340f0f4ffc2f2b389ebac0d9a4da318f248f20b6`](https://github.com/sebastienros/jint/tree/340f0f4ffc2f2b389ebac0d9a4da318f248f20b6). This older source line targets ECMAScript 5.1 and avoids treating current Jint's modern language/toolchain support as support in a Space Engineers mod. The port profile/provenance are recorded in [ORIGIN](ORIGIN.md) and [Profile](Docs/Profile.md); final build acceptance receipts remain separate.

The initial host is deliberately small: update a declared node's text or binding, subscribe a JavaScript handler to a document event, and schedule a handler with a timer. Each runtime is bound to one document the calling mod already owns. JavaScript is data interpreted by the mod; it is not compiled into a PB or given arbitrary .NET/game objects.

```javascript
var count = 0;
hdr.on("click", "increment", function(e) {
    count += 1;
    hdr.setText("count", "Clicked " + count + " times");
});
```

This example describes the initial `HDR.JS/Host1` bridge. It requires an explicitly registered script, an already created document containing `count` and `increment`, and a C# owner that grants text updates to `count`. `<script>` markup and inline `onclick` attributes are not accepted by the existing HTML parser. [JavaScriptHudDemo.cs](Examples/JavaScriptHudDemo.cs) demonstrates the real owned HTML adapter; its consumer supplies cooperative pointer input.

| Guide | Contents |
| --- | --- |
| [Profile](Docs/Profile.md) | Engine/language scope, explicit omissions and execution safeguards |
| [Mod API](Docs/Mod-API.md) | Public delegate SDK, realm ownership, limits and shaped-document binding |
| [Host API](Docs/Host-API.md) | Document-bound JavaScript functions, events, timers and ownership |
| [Provenance](ORIGIN.md) | Exact source revision, archive hashes, dependency notices and port changes |
| [Third-party notices](LICENSES/README.md) | License copies included inside the mod distribution |

JS support does not automatically add CSS animations, a browser DOM, Canvas, WebGL, resource fetching or web-framework compatibility. Existing HDR surface mapping and provider composition are independent. An operation that requires a native camera/texture/input renderer must pass its actual capability check and report `Requires plugin: ...` when absent; JavaScript must not turn it into an unsafe native entry or a silent low-quality fallback.

## Try the optional mod

Use matched **JavaScript Runtime 0.1.0 / HTML Frontend 0.2.1 / HDR API 0.9.11** source/packages as separate world mods. The interpreter itself is independent of HDR rendering; the HUD demo needs both rendering/frontend mods and their new `mutate`/`script-claim` capabilities. A previously released frontend without those commands cannot host this prototype's document scripts.

Explicitly enter `/hdrjs demo` to create the plugin-free vector HUD counter, `/hdrjs click` to deliver a synthetic owned click, `/hdrjs status` to inspect it and `/hdrjs clear` to release the demo. Loading the mod does not start a display or capture mouse input. A real consumer supplies cooperative input through its own bound HTML API, or `HdrJavaScriptApi.Pointer` for a runtime-owned HUD; the chat click command is an explicit test.

Build from this repository with the [repository build script](https://github.com/PommesMitChips/HDR-API/blob/main/OptionalMods/HDRJavaScriptRuntime/Build.ps1), using the installed game's `Bin64` directory. It runs the complete checks by default and writes `artifacts/js-prototype/HDR-JavaScript-Runtime-0.1.0.zip`. Reusing a verified receipt still requires all sealed inputs to match. The ZIP contains readable mod source, licenses and provenance; repository-only build tools are excluded. The build does not install it or alter game/Pulsar settings.

Foreign mods copy the public [`HdrJavaScriptApi.cs`](../../Api/Mods/HdrJavaScriptApi.cs) and use the negotiated [mod API](Docs/Mod-API.md). SDK links under `Api/` refer to the [source repository](https://github.com/PommesMitChips/HDR-API); the ZIP also retains an identical helper in `Data/Scripts/HDRJavaScriptRuntime/Bootstrap`. The in-package [`JavaScriptHudDemo.cs`](Examples/JavaScriptHudDemo.cs) illustrates the direct adapter seam; do not use Jint/runtime types as cross-mod assembly ABI. Both routes execute locally, without a renderer plugin. Source-camera choices retain their native provider requirements.

PB execution and replication are separate from the client-local bridge. A client script can display UI state; it cannot independently assign an authoritative PB variable or run terminal actions. PB-supplied script/controller modes, if added, need their own authenticated ownership and event contract. No such mode is claimed by this prototype.

Runtime errors and exhausted execution allowances remain local to the script. Document/owner retirement cancels event handlers and timers. A failed callback drops its unflushed changes and retires JS/timers/the script claim while preserving the last committed HTML frame, including a runtime-created HUD. Explicit realm/owner cleanup removes a runtime-created HUD; externally bound documents remain their C# consumer's responsibility. Successful callbacks submit one validated HTML mutation batch; its detached candidate commits only after layout and renderer publication succeed. Physical native-LCD JS binding is rejected before acquiring a script claim because atomic publication is unavailable; there is no sequential fallback. Offline CPU fixtures and real analyzer acceptance do not establish live rendering/input/multiplayer behavior.
