# Client mod API: HDR.JS/0.1

**ALPHA, runtime 0.1.0 / HTML 0.2.1.** Runtime source has a preliminary actual installed-game ModApi/C# 6 pass; offline behavior and final current-source/package receipts are recorded separately. Copy [`HdrJavaScriptApi.cs`](../../../Api/Mods/HdrJavaScriptApi.cs) into your consumer mod. Runtime implementation classes and Jint objects do not cross this public boundary. The service passes ordinary delegates, scalars and existing `MyTuple` types, so the consumer does not need to compile against an optional DLL or reproduce the engine source.

The client-only service uses discovery channel **481770160**, request channel **481770161**, and `Func<string,object[],object>` endpoints. `version` returns exactly `HDR.JS/0.1`; `capabilities` returns detached capability names. `open(ownerId)` returns an owner endpoint. IDs require 1–40 letters/digits/`-`/`_`/`.` characters. Use a unique ID: opening it again retires that ID's prior owner/realms.

Create the SDK on the client simulation thread, check `Ready`, retry discovery with `Request`, and rebuild your realm handles after `ConnectionGeneration` changes. Dispose it on consumer unload. Missing service produces `Requires mod: HDR JavaScript Runtime (client service unavailable)`; no renderer plugin is needed for interpretation.

## Owner and realm methods

| Helper | Operation |
| --- | --- |
| `Capabilities()` | Query the connected service's actual profile/capability strings. |
| `Configure(settings)` / `Limits()` | Set/query named owner policies; configuration is validated as one candidate and affects subsequently created realms. |
| `CreateRealm()` | Create a plain owned interpreter realm without HTML. |
| `BindHtml(endpoint, document, htmlGeneration, ownerCurrent, textNodes, dataKeys)` | Bind one existing caller-owned HTML document through its real endpoint and current-generation witness. IDs grant exactly which text/data locations JS may change. |
| `CreateHud(markup, css, width, height, textNodes, dataKeys, backend)` | Convenience creation of a runtime-owned HTML HUD; default backend `vector`. Its document is removed when the realm is disposed. |
| `Pointer(realm, x, y, pressed)` / `CancelPointer(realm)` | Supply/clean cooperative logical-pixel input only for a current runtime-owned HUD. External `BindHtml` documents reject these helpers and keep their caller's HTML input route. |
| `Execute(realm, source)` | Execute explicit source in that owned realm. Initial HTML-bound execution installs the host shim; later calls retain globals and use the same mutation batch. Returns success. |
| `CallFunction(realm, name, scalarArguments)` | Invoke an owned named function; return a scalar. Arguments admit null/string/bool/Int32/finite Double, not arbitrary objects or arrays passed into JS. Serialize structured data explicitly. |
| `Status(realm)` | Return current/error/profile/generations/document identity as the tuple below. |
| `Diagnostics(realm, clear=true)` | Return the bounded log/capability-reason queue as `string[]`; consume it by default. This is separate from execution-failure `LastError`. |
| `RealmLimits(realm)` | Query the realm's copied interpreter/host policies. |
| `AddSourceChoice(realm, choice, nodeId, provider, sourceId, probe, settings)` | C# declares a finite source choice and mandatory actual provider/plugin capability probe. JS can select its choice name, not arbitrary provider/game identities. |
| `DisposeRealm(realm)` / `ClearOwned()` | Release one/all realms belonging to this endpoint. |
| `TryCall(...)` / `Call(...)` | Access exact endpoint commands while preserving failure reasons and generation checks. |
| `Dispose()` | Unregister discovery and release this endpoint and its owned realms. |

Status is:

```csharp
MyTuple<bool, string, string, MyTuple<long,long,bool>, long>
// current, lastError, languageProfile,
// (ownerGeneration, htmlGeneration, htmlBound), htmlDocumentHandle
```

It reports realm/association validity, not a GPU acknowledgement. `CreateHud` returns a realm handle; status `Item5` identifies its underlying runtime-owned HTML document. That identity does not grant a separate raw HTML endpoint to your mod: use `Pointer`/`CancelPointer` for its cooperative input. Alternatively create the document through your own HTML SDK and bind it explicitly. For an externally owned document, query that HTML endpoint's normal desired/visible status as well. The profile is `HDR.JavaScript/ES5.1-Prototype1`; see [language omissions](Profile.md).

## Minimal interpreter

```csharp
var js = new HdrJavaScriptApi("example.counter");
// Continue only after js.Ready; retain this SDK until unload.
long realm = js.CreateRealm();
js.Execute(realm, "var count = 0; function add(n) { count += n; return count; }");
double count = (double)js.CallFunction(realm, "add", 2.0);
// count is 2; subsequent calls use this realm's existing globals.
js.DisposeRealm(realm);
js.Dispose();
```

Use `Configure` before creating realms when you want a policy other than the defaults:

```csharp
js.Configure(new[] {
    new MyTuple<string,object[]>("statements", new object[] { 25000 }),
    new MyTuple<string,object[]>("timeout-ms", new object[] { 10.0 }),
    new MyTuple<string,object[]>("allocation-realm", new object[] { 32L * 1024 * 1024 })
});
```

## Bind an existing shaped document

First create the document through your own `HdrHtmlApi`—HUD, plane or general surface—and retain that endpoint's ownership. Then pass a C# wrapper around its real `Call` method and a witness for that exact current generation:

```csharp
long htmlGeneration = html.ConnectionGeneration;
Func<string,object[],object> ownedEndpoint = (op, args) => html.Call(op, args);
Func<bool> ownerCurrent = () => html.Ready && html.ConnectionGeneration == htmlGeneration;
long realm = js.BindHtml(ownedEndpoint, document, htmlGeneration, ownerCurrent,
    new[] { "count" }, new string[0]);
js.Execute(realm, "var n=0; hdr.on('click','increment',function(e){" +
    "n++; hdr.setText('count','Count: '+n); });");
```

The real HTML document accepts one private script claim. Another wrapper around the same endpoint does not grant a second runtime. Only C# holds the token, endpoint, handle and owner witness. Destroying the document, generation loss or releasing the JS realm cancels callbacks and releases its claim. Disposing an externally bound realm leaves your HTML document alive; the consumer still owns its cleanup.

The runtime update loop dispatches timers/events. Supply pointer or ray input through your own bound HTML API, or `Pointer` for a runtime-owned HUD, only from an input session your consumer owns. Call `CancelPointer` on focus loss; it also cleans a stopped realm's still-owned HUD without restarting JS. JS does not acquire native game input, mutate PB values or execute `data-action`. Keep event polling assigned to this bridge rather than a second competing consumer.

Script exceptions retire the interpreter, callbacks, timers and private claim but preserve the last committed HTML frame. This also preserves a runtime-created HUD after a script failure; it is not automatically erased by the failure. Explicit `DisposeRealm`, `ClearOwned`, owner replacement or unload removes runtime-owned HUDs while leaving foreign HTML documents alive. A stopped realm cannot be reused; deliberately create a new one if needed.

## Configurable policies

Settings use distinct known names, at most 32 entries, and exactly one value per entry. Count settings require Int32; allocation accounting requires Int64; time requires finite Double. Defaults are copied into new realms, so configure before creation and inspect `RealmLimits` for an existing realm.

| Setting | Default | Validation |
| --- | --- | --- |
| `statements`, `operations` | 50,000 / 100,000 | Nonnegative; `0` disables that quota |
| `recursion`, `parse-depth` | 64 / 128 | Positive, at most 256 |
| `source-characters`, `string-characters` | 65,536 each | Positive |
| `array-length`, `object-properties`, `created-objects` | 8,192 / 2,048 / 16,384 | Positive |
| `allocation-operation`, `allocation-realm` | 8,388,608 / 67,108,864 units | Nonnegative Int64; `0` disables that quota |
| `host-calls`, `timeout-ms` | 256 / 25 ms | Nonnegative; `0` disables that quota |
| `callbacks-per-update`, `pending-events` | 16 / 128 | Positive |
| `timers`, `event-handlers` | 64 / 128 | Positive |
| `mutations-per-dispatch`, `text-characters` | 64 / 16,384 | Positive; HTML still rejects a batch above its structural limits |
| `maximum-timer-delay-ms` | 86,400,000 ms | Positive finite Double |
| `max-realms` | 16 per owner | Nonnegative; `0` means unlimited |

Allocation units are conservative work/allocation accounting, not a measured CLR heap byte budget. They do not reset retained script globals into a memory-free state between calls. Timers/events are client-local, and quota configuration is separate from unlimited-by-default renderer geometry allowances.

## Optional renderer calls

`AddSourceChoice` requires a C# capability probe for the actual provider, including its optional plugin and local-consumer support. The HTML adapter separately calls exact read-only `source-check(document, provider, sourceId)` on its real document/backend before `AttachSource`. Negotiated mod-native providers are accepted without imposing a universal plugin requirement. Missing native plugin returns `false` from `hdr.selectSource` and prints the specific reason without invoking the attachment. The reason also enters `Diagnostics`, alongside `hdr.log` output; a later successful status query does not erase it. Queue count/text bounds use the realm's copied `pending-events`/`text-characters` host policies. Presence is not proof of ready imagery; normal source status still applies. There is no silent sprite/pixel fallback.

Native physical-LCD JS binding rejects before a script claim because atomic publication is unavailable; it does not enter a sequential mutation fallback. Retained local HUD/world/general-surface batches validate the whole detached candidate, including every declared source choice, and publish once. See [Host API](Host-API.md) for atomic publication, stale events and error retirement.
