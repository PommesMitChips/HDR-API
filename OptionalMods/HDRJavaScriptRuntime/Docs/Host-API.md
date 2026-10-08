# HDR.JS/Host1

**ALPHA, runtime 0.1.0 / HTML 0.2.1.** One bridge binds a JavaScript realm to a retained local HTML document the caller already owns. Runtime source has a preliminary actual ModApi/C# 6 pass; host behavior is covered by offline fixtures, with complete build receipts maintained separately. The caller registers script source explicitly through its C# integration; the existing HTML parser does not execute script markup or event-handler attributes. The direct classes described here are in-package integration types. Foreign mods use the separately negotiated [`HDR.JS/0.1` delegate API](Mod-API.md).

## JavaScript functions

| Function | Operation |
| --- | --- |
| `hdr.setText(nodeId, value)` | Queue plain-text replacement for a C#-declared node. `String(value)` supplies text; it never parses as HTML. |
| `hdr.setData(key, value)` | Queue an existing C#-declared binding value after `String(value)`. It does not assign arbitrary CSS properties or game variables. |
| `hdr.on(kind, nodeId, handler)` | Subscribe a JS function/closure or global function name to the document's existing `click` or `change` event. One subscription is retained per kind/node pair. |
| `hdr.off(kind, nodeId)` | Remove that document event subscription and release its retained callback. |
| `hdr.setTimeout(handler, delayMs)` | Schedule one JS function or global function name on the bridge's client update timeline and return its timer ID. |
| `hdr.setInterval(handler, delayMs)` | Schedule repeated handler delivery and return its timer ID. Missed update intervals coalesce instead of replaying an unbounded backlog. |
| `hdr.clearTimer(id)` | Remove the bridge-owned timer. |
| `hdr.selectSource(choiceName)` | Queue only a C#-declared source choice after actual document/provider capability checks. Unavailable capability returns `false` and prints its reason without entering `AttachSource`. |
| `hdr.log(value)` | Send `String(value)` to the C# consumer's supplied diagnostic callback; the service also records its bounded `Diagnostics` queue. |

Document values crossing the host boundary are scalar. JS function handles belong to the interpreter; they are released with the subscription/timer/realm. The bridge does not expose arbitrary .NET objects, C# callbacks, reflection, a browser DOM or unrestricted game APIs.

Function handlers receive a plain JavaScript object created by the shim:

```javascript
hdr.on("click", "increment", function(e) {
    // e.type, e.targetId, e.action, e.value, e.revision
});
```

Named global handlers receive five scalar arguments:

```javascript
function handler(kind, nodeId, action, numericValue, committedRevision) {
    // action is descriptive data from the authored document.
    // It does not execute a terminal action or another script.
}
```

Callbacks are admitted only for the current document/backend generation and a committed visible frame. The adapter acquires the actual owned HTML document's `script-claim` with a private C# token; it checks the matching claim and owner generation before dispatch and releases that exact claim on disposal. Distinct delegate aliases cannot give the same document two active JS runtimes. The token, endpoint and document handle never cross into JS or network payloads.

The bridge consumes its event queue once, so it must be the designated event consumer rather than competing with another poller. An unchanged source frame does not recreate a document or grant new script authority.

## Example

The caller creates this document with the current HTML frontend:

```html
<main>
  <p id="count">Clicked 0 times</p>
  <button id="increment">Increment</button>
</main>
```

It then explicitly registers this script for that document:

```javascript
var count = 0;
hdr.on("click", "increment", function(e) {
    count += 1;
    hdr.setText("count", "Clicked " + count + " times");
});
```

The C# consumer must grant text-node `count` explicitly when constructing `HdrHtmlDocumentHost`; unspecified nodes/data keys are denied. [JavaScriptHudDemo.cs](../Examples/JavaScriptHudDemo.cs) provides a complete adapter example using real `HdrHtmlApi.CreateHud`, `JavaScriptRealm`, `HdrHtmlDocumentHost` and `JavaScriptDocumentBridge`.

Rendering this document through ordinary mod geometry requires no plugin. Pointer input remains the document's existing cooperative input route: the calling mod must own the input it supplies. A renderer/pointer plugin, when explicitly selected and present, is a separate capability.

## Update, errors and cleanup

The C# consumer calls `Update(monotonicMilliseconds)`. Due timers and visible-frame events share its callback allowance. Text/data writes coalesce within a callback; the bridge does not reparse HTML for each timer tick. A callback error drops unflushed writes and retires the JS association.

Each successful callback produces one `IJavaScriptDocumentHost.ApplyMutations` batch containing `text`, `data` or `source-choice` operations. The HTML adapter resolves every declared source choice and its capability before invoking `HdrHtmlApi.Mutate(document, MyTuple<string,object[]>[])` once. The frontend validates the entire detached candidate, performs one layout/paint publication and commits the candidate only on success. Confirmed restoration after a rejected publication preserves the previous desired/published frame. If renderer state cannot be safely restored, visible revision becomes zero, input is disabled and status gives the retirement reason.

The batch is available for retained local HUD, world-plane and general-surface documents. Physical native-LCD JS binding rejects with `Unsupported` at admission before taking a script claim, because atomic publication is unavailable. The bridge does not pretend several `DrawFrame` or setter calls form an atomic update. The batch limit is 64 operations and 131,072 direct string characters. Larger host policy limits still receive a whole-batch structural rejection, with no splitting or partial application.

`RegisterScript(source)` installs the host shim exactly once on a new bridge. The C# owner can deliberately execute additional source through `Execute(source)` in that initialized realm, or call a named function through `InvokeNamed(name, scalarArguments)`. Both use the same deferred atomic mutation route. A failed named call throws the bridge's error; an inactive bridge rejects entry with its retirement reason. These operations do not replace the realm or grant JavaScript a script-loading/browser API. To replace all code and globals, retire the old bridge and create a new realm.

Destroying the owned document, owner/backend generation loss or explicit bridge disposal cancels subscriptions/timers. `HdrHtmlDocumentHost.Dispose` releases only the JS association; it does not dispose the C# consumer's HTML endpoint/document. Script errors preserve the accepted frame for external and service-created documents; explicit service cleanup removes only its own created HUDs. Late events cannot revive a retired bridge. Execution limits apply across script and callback work; renderer geometry allowance is not an interpreter budget.

Source choices are registered through `HdrHtmlDocumentHost.AddSourceChoice(choiceName, nodeId, provider, sourceId, actualCapabilityProbe, settings)`. The C# probe must check the actual provider's required capabilities; native providers include plugin/local-source-consumer checks. The bridge also invokes the exact read-only HTML `source-check(document,provider,sourceId)`, so a negotiated mod-native provider can work without a plugin. JavaScript selects only the declared choice name. This is not unrestricted `AttachSource`, game entity lookup, PB callback or terminal-action access.

## Host scheduling defaults

`JavaScriptHostLimits` is copied at construction; all settings require positive finite values. Current defaults are 16 callbacks/update, 128 pending events, 64 timers, 128 handlers, 64 distinct mutations/dispatch, 16,384 text characters/value and 86,400,000 ms maximum delay. Interpreter statement/depth/source limits are separate in `JavaScriptLimits`. Event overflow drops the oldest queued event; delayed intervals coalesce into one callback and schedule their next tick from the current clock.
