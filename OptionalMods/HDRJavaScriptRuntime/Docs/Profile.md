# HDR JavaScript prototype profile

**ALPHA, runtime 0.1.0 / HTML 0.2.1.** This page distinguishes the language engine from APIs supplied by HDR. Runtime source has a preliminary actual installed-game ModApi/C# 6 pass; host behavior has offline fixtures. The complete maintained build gate checks current sources and records final compiler/behavior/package acceptance separately. No live game acceptance is implied.

## Language engine

The engine profile is **`HDR.JavaScript/ES5.1-Prototype1`**, based on the actual Jint 2.11.10 source at commit `340f0f4ffc2f2b389ebac0d9a4da318f248f20b6`. This is a source port, not a separately invented expression evaluator labeled Jint, and not the current upstream engine's complete feature set or a full ECMAScript conformance claim.

The initial port retains variables, functions/closures, object/array operations and upstream Math/JSON paths. It explicitly omits `Date`/local time, regular expressions and regex literals, dynamic `eval`/`Function` construction, CLR interoperability and debugger integration. Modern syntax such as modules, classes, arrow functions, `async`/`await`, promises and ES2015+ APIs is outside this ES5.1 source-line profile. Tests certify particular supported paths rather than arbitrary web frameworks.

The port removes or replaces dependencies that the actual game's ModApi/C# 6 gate rejects. It must retain the original license notices for all retained third-party code. [ORIGIN](../ORIGIN.md) identifies every source/dependency pin and records the changes. Any substituted built-in semantics must be listed there rather than described as full browser/ECMAScript conformance.

The initial explicit engine adapter is `IJavaScriptRealm`: register deliberate host functions, execute source, invoke a named global handler and dispose the realm. Document arguments remain scalar; JS functions are retained only as interpreter-owned callback handles. The adapter does not expose arbitrary CLR objects, property reflection, game entities or unrestricted .NET delegates. The evaluator runs within the mod's normal execution context; it does not load assemblies, emit C#, use a JIT, install hooks or escape the sandbox.

## Host environment

`HDR.JS/Host1` provides the [document-bound functions](Host-API.md). There is no `window`, browser `document`, browser event loop, URL loader, network service, filesystem API, iframe, audio/video decoder, Canvas, WebGL or DOM proxy. The HTML parser still rejects `<script>` and inline `on*` attributes. Consumers explicitly provide script source after creating their owned document.

Callbacks accept an ordinary JS function/closure or a global function selected by string name. A JS shim creates a plain JS event object from five scalar arguments; it does not proxy a game/CLR event object. Text/data IDs and optional source choices are declared by the C# owner. Markup, style/class/tree mutation, arbitrary source IDs and arbitrary game operations are not implicit privileges.

The initial mode is a client-local UI script bound to one caller-owned HTML document. The document adapter holds a private exclusive script claim on that actual document, separate from JavaScript globals, so two delegate aliases cannot bind competing runtimes. PB/server controller execution, client-to-PB actions and replicated JavaScript state are not provided. Per-viewer local timers can differ; they must not be used to create authoritative game state.

## Execution and lifetime

Interpreter work, recursion, parsing depth, object/property/string growth, queued events and timer count use configurable allowances. Allocation units are conservative accounting rather than measured CLR heap bytes; the prototype must not advertise a hard heap-memory measurement. These safeguards are separate from HDR's unlimited-by-default aggregate geometry setting. A timer or event callback must not run an unbounded loop on the game update thread.

One script error or exhausted allowance is reported for that realm/handler, without crashing the frontend or entering a missing renderer endpoint. Retire the realm with its owner/document, clear handler/timer state, and prevent stale callbacks from publishing into a new document generation. Host operations are bounded separately because a JavaScript statement counter cannot interrupt a single expensive C# callback.

Text/data writes coalesce within each successful callback, and all its text/data/source choices enter one `HdrHtmlApi.Mutate` batch. Input callbacks refer to the last committed visible document frame. An interpreter error before flush drops that callback's staged writes and retires its JS association. The frontend validates a detached candidate and commits its desired/published state after one successful layout/paint publication. A confirmed failed publication restores the previous accepted state; uncertain renderer retirement reports visible revision zero and disables input rather than presenting stale output as accepted. Native physical-LCD JS binding rejects at admission before a script claim because atomic publication is unavailable.

The batch admits at most 64 operations and 131,072 direct string characters. A larger configured host mutation limit does not bypass this structural HTML limit or split an oversized batch into partial writes. The normal document desired/visible revisions and provider status remain the source of truth.

## Optional renderer features

JavaScript does not change the renderer's capability boundaries. Mod-native geometry and ordinary native LCD sprites can remain plugin-free. Native camera capture, LCD texture relay, private pointer capture and future scene-texture/GPU effects remain optional providers.

Before such an operation, query the selected document backend and the actual plugin/provider status. An absent endpoint yields a specific `Requires plugin: ...` result. An installed endpoint without the requested feature yields its unsupported-capability reason. Neither should crash, silently rasterize into sprites, create virtual LCDs, or change the user's settings.
