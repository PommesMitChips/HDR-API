# HDR HTML frontend 0.2.2

**ALPHA — initial prototype.** This optional, mod-native HTML/CSS frontend implements **`HDR.HTML/Profile1`**, a strict UI subset which lowers source markup to HDR's existing retained vector, SVG and text primitives. It is separate from the renderer. It does not embed a complete HTML5 browser, JavaScript engine, browser DOM, Canvas, WebGL, web-resource loading or arbitrary markup event handlers. The separately enabled [JavaScript Runtime 0.1.0](../HDRJavaScriptRuntime/README.md) can explicitly bind retained local documents through the batch/claim APIs introduced in **0.2.1**; frontend 0.2.2 remains compatible. The parser still rejects `<script>` and inline `on*` attributes.

Vector and grouped SVG backends draw through HDR's retained paths with **HDR API 0.9.11+ / scene 20** and need no client plugin for geometry. The **`HDR.Html` PB property** adds server-owned documents: `bind-screen` maps HTML onto an existing owned plane, cylinder, sphere, ellipsoid or authored mesh; `attach-source` puts a registered provider into an explicit HTML node. Core SVG controls use compatible **HDR Client Renderer 0.9.13+** for automatic persistent pointer input. Actual camera imagery needs that renderer on every viewer. Native sprites draw directly on a real owned LCD or relay its actual texture onto the same screen shapes, requiring the renderer **on every relay viewer**. Native sprite input stays cooperative. The PB route needs no new plugin/DLL update. Client-local native sources use the new negotiated service in **Client Renderer 0.9.14**; an off-screen HTML/browser rasterizer remains unimplemented.

![Local and PB frontend boundaries](../../docs/wiki/diagrams/html-frontend.svg)

## Try the prototype

1. Manually install the core [`Mod`](../../Mod) and this `OptionalMods/HDRHtmlFrontend` source directory as separate local world mods, then explicitly add both to the world's mod list. The core must be **0.9.11+**; the frontend is **0.2.2**.
2. Enter `/hdrhtml hud vector` or `/hdrhtml world svg` in chat. Both `hud` and `world` accept `vector` or `svg`. The world demo stays at the plane placed three metres in front of the camera when created.
3. Enter `/hdrhtml status` to inspect backend, desired/visible revision, layout count and any error. Enter `/hdrhtml clear` to release the demo owner.

Registration on startup draws no demo. Installation does not auto-enable a world mod, change game preferences, rename or reconfigure blocks, or fetch web resources. The chat demo creates only its owned local drawing contexts; clearing it leaves other consumers' contexts intact.

To build the game-loadable frontend ZIP from this repository, use [`Build.ps1`](Build.ps1) with the installed game's `Bin64` directory. It runs the [offline checks](../../Tools/HtmlChecks/README.md) and writes `artifacts/HDR-HTML-Frontend-0.2.2.zip`; it does not install the result.

## Use from a programmable block

For ordinary PB authoring, put plain HTML with embedded `<style>` in the **programmable block's Custom Data**, then paste the small [`HtmlPbDemo.cs`](Examples/HtmlPbDemo.cs) into its code editor. Name an authorized LCD, Console or Projector **`HTML Display`** and run with an empty argument or `demo`. The mod owns mounting, layout, source watching, range bindings and cleanup; the PB calls `run` and prints its result. Start with [`PlainHtmlPbDemo.html`](Examples/PlainHtmlPbDemo.html) and the [plain HTML tutorial](Docs/PB-Tutorial.md).

A physical LCD needs its **HDR API** text-surface script selected manually first. The frontend matches its logical canvas to the actual panel aspect, centring the authored 480×280 viewport. Console/Projector mounts use a centred plane at .005 metres per logical pixel and existing block view/range settings. The source is `HDR.HTML/Profile1`, a strict subset; full `html`/`head`/`body` plus embedded CSS is accepted, while browser features such as `<script>` remain unsupported.

`status` reports the selected target and publication/error state; `reload` forces a reattempt; `clear` removes only this automatic authoring mount. The mod checks edited Custom Data every 30 simulation ticks and keeps the previous published display when replacement source is invalid. PB program/power or target loss retires the mount; run again deliberately after recovery. No PB update loop or drawing API helper is needed. Display geometry needs no plugin; core SVG button/range input retains its existing per-viewer Client Renderer requirement. HTML does not infer gameplay actions from labels or `data-action`.

For camera-in-HTML composition, use the separate advanced [`HtmlCameraEllipsoidDemo.cs`](Examples/HtmlCameraEllipsoidDemo.cs). Name a dedicated Console/Projector **`HTML Display`** and two working physical cameras **`HDR Camera 1`** / **`HDR Camera 2`** on the PB's construct, then run `demo`. It binds a centred 640×360 document onto a bounded ellipsoid patch, attaches the selected physical camera to `<div id='pov'>`, and retains its **Previous** / **Next** HTML buttons. `Update10` click polling changes only that node's camera source ID. Terminal `previous`, `next`, `status` and `clear` operate the same owned demo. Each camera viewer needs Client Renderer 0.9.13+; automatic buttons use the existing persistent viewer. Read [Composition](../../docs/wiki/Composition.md) for setup, direct slots and backend constraints. Live viewer/GPU/input acceptance remains unverified.

`bind-screen` keeps the screen's shape and places the **document centre** in canvas metres (+X right, −Y down). It does not change ordinary `bind`/`bind-sprites`' top-left convention. Source regions preserve full node content bounds plus effective rectangular clip, so clipped provider UVs crop without stretching. `source-capabilities` / `source-status` report actual support and provider readiness reasons; source attachment is a generic provider binding, not a browser `img`/`video` feature.

The advanced [`HtmlPbBindingsDemo.cs`](Examples/HtmlPbBindingsDemo.cs) shows how a PB can interpret events and link them to its own script variables. Use it when gameplay/controller code needs values; it is separate from the plain-HTML bootstrap. Physical LCD setup and actual `SurfaceSize`/aspect handling remain the same. Console/Projector output follows the target's existing range/view/table settings and may be clipped by them.

That advanced script polls in `Update10`, assigns range events to its `_level` variable, and feeds changed values back through HTML data. `set 60` writes that variable and document binding; `clear` destroys only its demo document; `status` reports **server publication**, not a viewer-visible/GPU acknowledgement. The reset button assigns 60. These events do not execute `data-action` or call `TryRun`; the example performs no gameplay or terminal-action writes.

The [PB API](Docs/PB-API.md) is independent of the client-local mod API below. Server-owned documents are invalidated on PB source/generation or target/source retirement, so recreate deliberately after such changes. The core SVG demo's display needs no plugin; button/range interaction uses the existing [core persistent input path](../../docs/wiki/Interactive-Controls.md) and requires HDR Client Renderer **0.9.13+ on each interacting viewer**. Live input and GPU acceptance remain unverified.

For actual native sprites, paste the separate [`HtmlPbSpritesDemo.cs`](Examples/HtmlPbSpritesDemo.cs) script. Manually configure the physical **`HTML Sprite Source`** LCD as **SCRIPT with no selected script (NONE)** and explicitly reserve it for this PB. `demo`/`lcd` draws directly, plugin-free; `world` relays its real texture to Console/Projector **`HTML Display`**, requiring existing Client Renderer 0.9.13+ on **every viewer**. The source's actual dimensions/Debug font and texture background apply; relayed RGB is opaque, so transparent canvas pixels cannot be recovered. No virtual LCD or automatic content-mode/background change is made.

Native button/range events accept coordinates from a caller-owned GUI/touch/eye provider rather than automatic core persistent mouse input. The demo's `press X Y`/`release X Y` commands are explicit synthetic tests from the PB terminal. It polls events and assigns `_level` without gameplay writes. Native event revisions are published document revisions with player ID zero; core SVG events retain core value revisions and player IDs.

`bind-sprites-screen` maps the actual source LCD into an existing owned screen, preserving its shape and real texture padding. Its opaque RGB backing rejects partially transparent source attachments and source overlap with later visible HTML artwork/controls. An external camera texture cannot be embedded into the physical LCD itself. Missing capabilities fail with backend-specific reasons; there is no synthetic 128×72 fallback, virtual LCD creation or recovered native alpha.

## Use from a mod

Copy [`Api/Mods/HdrHtmlApi.cs`](../../Api/Mods/HdrHtmlApi.cs) into your consumer mod and adapt [`HtmlConsumerExample.cs`](Examples/HtmlConsumerExample.cs). Call discovery and document methods on the client simulation thread outside rendering callbacks. Use a unique owner ID, rebuild after connection-generation changes and dispose your endpoint on unload.

Use `CreateSurface(html, css, width, height, worldPose, metresPerPixel, surfaceKind, surfaceParameters, settings, backend, order)` for a centred local HTML canvas on any of the five surface kinds, using `vector` or `svg`. `AttachSource`/`DetachSource` bind explicit node IDs; `SourceCapabilities`/`SourceStatus` report negotiated support and readiness. Source settings include an actual accessible block anchor. Native camera/LCD/portal sources require the new local-consumer service in **Client Renderer 0.9.14**; existing PB composition works with 0.9.13+. `PointerRay` consumes an owned cooperative world ray and resolves the committed surface/document frame. See the [exact composition examples](../../docs/wiki/Composition.md#compose-in-a-client-local-mod-context) and [Local API](Docs/Local-API.md). No Core internal surface types cross the public delegate boundary.

For a HUD, keep `CreateHud` and call `SetSourceAnchor(document, actualBlock)` before `AttachSource`. HUD regions/input remain top-left Y-down pixels, with no curved HUD mapping. Existing `CreateWorld` uses the same anchored attachment route and shared plane mapper, preserving its top-left pose across resize and pixel input; `PointerRay` is also supported. `SetSourceAnchor` updates mapped documents too. Genuine physical `CreateNativeLcd` rejects external engine texture attachment with an explicit renderer reason.

The consumer must supply pointer coordinates from an input/GUI session it already owns. The frontend does not acquire global mouse input or prevent the same click from reaching weapons and game controls. Cancel pointer input on focus loss. Button/range events are data for your mod to interpret; `data-action` never executes code.

The local mod API retains the **0.2.1** atomic `mutate` batches for retained HUD/world/general-surface documents: validate one detached candidate, lay out/publish once, then commit text/data/source changes together. It also retains the private per-document `script-claim`/`script-valid`/matching `script-release` lifecycle and exact read-only `source-check(document,provider,sourceId)`. These commands allow the separate JS runtime to bind actual owned documents without executing scripts inside the frontend. They are not PB commands; the new plain-HTML `run` mount requires **0.2.2**. Physical native-LCD JS admission rejects before a claim because reversible atomic publication is unavailable. See [Local API](Docs/Local-API.md#atomic-mutations-and-document-script-claims) and the [JavaScript guide](../../docs/wiki/JavaScript-Prototype.md).

For a native LCD, the consumer must explicitly own the supplied `IMyTextSurface` and set it to `ContentType.SCRIPT` with no selected text-surface script before creating a document. The frontend never claims or configures an arbitrary LCD. Its one-document-per-surface guard coordinates frontend users only; the consumer remains responsible for excluding other sprite writers.

## Authoring and renderer choices

| Guide | Contents |
| --- | --- |
| [Profile](Docs/Profile.md) | Accepted markup/CSS, cap-height sizing, Unicode limits and bounded layout |
| [Rendering](Docs/Rendering.md) | Retained vector versus grouped SVG, native LCD restrictions, caching and measured costs |
| [Local API](Docs/Local-API.md) | Exact document calls, ownership, status, events and lifecycle |
| [PB API](Docs/PB-API.md) | Authenticated server documents, shared block displays, publication status and polled events |
| [Plain HTML tutorial](Docs/PB-Tutorial.md) | Tiny PB invocation, Custom Data HTML, editing, diagnostics and mount cleanup |
| [Wiki guide](../../docs/wiki/HTML-Frontend.md) | How this optional frontend fits the existing HDR APIs |
| [Composition guide](../../docs/wiki/Composition.md) | Runnable camera-container ellipsoid, independent source/layout/surface/input/effect layers and actual backend limits |

[`Panel.html`](Examples/Panel.html) and [`Panel.css`](Examples/Panel.css) show the HUD/world profile. [`NativeLcd.html`](Examples/NativeLcd.html) and [`NativeLcd.css`](Examples/NativeLcd.css) avoid features the native painter rejects. Unsupported features and exceeded renderer grants are errors; the frontend does not silently lower detail to fit.

Layout uses an authored logical viewport independent of camera distance. Unchanged documents retain their layout and paint; plain text, binding and resize updates coalesce into a later layout. Renderer preparation can still be costly when text or an SVG chunk changes. See the [rendering comparison](Docs/Rendering.md) for the measured tradeoff and the [offline check guide](../../Tools/HtmlChecks/README.md) to reproduce it. These fixtures do not establish live visual, GPU, input or multiplayer acceptance.

Geometry allowance is **unlimited by default**: painter `MaxPoints` / `MaxPrimitives` and core aggregate point/primitive settings use `0` independently. The frontend queries actual owner allowances and performs exact preflight; it adds no fixed 8,192 aggregate ceiling. Explicit positive allowances still apply before publication. Structural limits and per-frame drawing work remain separate; see [Rendering](Docs/Rendering.md) and [scene budgets](../../docs/wiki/Performance-and-Troubleshooting.md#scene-budgets).
