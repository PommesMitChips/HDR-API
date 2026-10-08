# HDR HTML frontend 0.1.1

**ALPHA — initial prototype.** This optional, mod-native HTML/CSS frontend implements **`HDR.HTML/Profile1`**, a strict UI subset which lowers source markup to HDR's existing retained vector, SVG and text primitives. It is separate from the renderer. It does not implement a complete HTML5 browser, JavaScript, a browser DOM, Canvas, WebGL, web-resource loading or arbitrary event handlers.

Vector and grouped SVG backends draw on local HUDs or flat world planes with **HDR API 0.9.10+** and need no client plugin. An explicitly owned native LCD surface is an alternative using vanilla sprites and measured Debug text. The **`HDR.Html` PB property** adds server-owned documents: core SVG display is plugin-free, while **both core SVG button and range events require existing HDR Client Renderer 0.9.13+**. PB sprites draw directly on a real owned LCD without a plugin, or relay that actual LCD texture to a Console/Projector using the same renderer **on every viewer**. Native sprite input is cooperative. No additional plugin/DLL update is needed. An off-screen HTML/browser rasterizer and curved HTML remain future work.

![Local and PB frontend boundaries](../../docs/wiki/diagrams/html-frontend.svg)

## Try the prototype

1. Manually install the core [`Mod`](../../Mod) and this `OptionalMods/HDRHtmlFrontend` source directory as separate local world mods, then explicitly add both to the world's mod list. The core must be **0.9.10+**; the frontend is **0.1.1**.
2. Enter `/hdrhtml hud vector` or `/hdrhtml world svg` in chat. Both `hud` and `world` accept `vector` or `svg`. The world demo stays at the plane placed three metres in front of the camera when created.
3. Enter `/hdrhtml status` to inspect backend, desired/visible revision, layout count and any error. Enter `/hdrhtml clear` to release the demo owner.

Registration on startup draws no demo. Installation does not auto-enable a world mod, change game preferences, rename or reconfigure blocks, or fetch web resources. The chat demo creates only its owned local drawing contexts; clearing it leaves other consumers' contexts intact.

To build the game-loadable frontend ZIP from this repository, use [`Build.ps1`](Build.ps1) with the installed game's `Bin64` directory. It runs the [offline checks](../../Tools/HtmlChecks/README.md) and writes `artifacts/HDR-HTML-Frontend-0.1.1.zip`; it does not install the result.

## Use from a programmable block

Paste the complete [`HtmlPbDemo.cs`](Examples/HtmlPbDemo.cs) script into a PB; it needs no appended helper. Name a dedicated LCD, Console or Projector **`HTML Display`**, then run `demo` explicitly. For a physical LCD, manually choose **Content → HDR API** first. The demo reads the panel's actual `SurfaceSize` and configures a core canvas of matching aspect ratio, fitting its 480×280 authored viewport with letterbox space when needed. It leaves the physical content mode unchanged. Console/Projector output uses the target's existing range/view/table settings and may be clipped by them.

The script polls in `Update10`, assigns range events to its `_level` variable, and feeds changed values back through HTML data. `set 60` writes that variable and document binding; `clear` destroys only this demo document; `status` reports **server publication**, not a viewer-visible/GPU acknowledgement. The reset button assigns 60. These events do not execute `data-action` or call `TryRun`; the example performs no gameplay or terminal-action writes.

The [PB API](Docs/PB-API.md) is independent of the client-local mod API below. Server-owned documents are invalidated on PB source/generation or target/source retirement, so recreate deliberately after such changes. The core SVG demo's display needs no plugin; button/range interaction uses the existing [core persistent input path](../../docs/wiki/Interactive-Controls.md) and requires HDR Client Renderer **0.9.13+ on each interacting viewer**. Live input and GPU acceptance remain unverified.

For actual native sprites, paste the separate [`HtmlPbSpritesDemo.cs`](Examples/HtmlPbSpritesDemo.cs) script. Manually configure the physical **`HTML Sprite Source`** LCD as **SCRIPT with no selected script (NONE)** and explicitly reserve it for this PB. `demo`/`lcd` draws directly, plugin-free; `world` relays its real texture to Console/Projector **`HTML Display`**, requiring existing Client Renderer 0.9.13+ on **every viewer**. The source's actual dimensions/Debug font and texture background apply; relayed RGB is opaque, so transparent canvas pixels cannot be recovered. No virtual LCD or automatic content-mode/background change is made.

Native button/range events accept coordinates from a caller-owned GUI/touch/eye provider rather than automatic core persistent mouse input. The demo's `press X Y`/`release X Y` commands are explicit synthetic tests from the PB terminal. It polls events and assigns `_level` without gameplay writes. Native event revisions are published document revisions with player ID zero; core SVG events retain core value revisions and player IDs.

## Use from a mod

Copy [`Api/Mods/HdrHtmlApi.cs`](../../Api/Mods/HdrHtmlApi.cs) into your consumer mod and adapt [`HtmlConsumerExample.cs`](Examples/HtmlConsumerExample.cs). Call discovery and document methods on the client simulation thread outside rendering callbacks. Use a unique owner ID, rebuild after connection-generation changes and dispose your endpoint on unload.

The consumer must supply pointer coordinates from an input/GUI session it already owns. The frontend does not acquire global mouse input or prevent the same click from reaching weapons and game controls. Cancel pointer input on focus loss. Button/range events are data for your mod to interpret; `data-action` never executes code.

For a native LCD, the consumer must explicitly own the supplied `IMyTextSurface` and set it to `ContentType.SCRIPT` with no selected text-surface script before creating a document. The frontend never claims or configures an arbitrary LCD. Its one-document-per-surface guard coordinates frontend users only; the consumer remains responsible for excluding other sprite writers.

## Authoring and renderer choices

| Guide | Contents |
| --- | --- |
| [Profile](Docs/Profile.md) | Accepted markup/CSS, cap-height sizing, Unicode limits and bounded layout |
| [Rendering](Docs/Rendering.md) | Retained vector versus grouped SVG, native LCD restrictions, caching and measured costs |
| [Local API](Docs/Local-API.md) | Exact document calls, ownership, status, events and lifecycle |
| [PB API](Docs/PB-API.md) | Authenticated server documents, shared block displays, publication status and polled events |
| [Wiki guide](../../docs/wiki/HTML-Frontend.md) | How this optional frontend fits the existing HDR APIs |

[`Panel.html`](Examples/Panel.html) and [`Panel.css`](Examples/Panel.css) show the HUD/world profile. [`NativeLcd.html`](Examples/NativeLcd.html) and [`NativeLcd.css`](Examples/NativeLcd.css) avoid features the native painter rejects. Unsupported features and exceeded renderer grants are errors; the frontend does not silently lower detail to fit.

Layout uses an authored logical viewport independent of camera distance. Unchanged documents retain their layout and paint; plain text, binding and resize updates coalesce into a later layout. Renderer preparation can still be costly when text or an SVG chunk changes. See the [rendering comparison](Docs/Rendering.md) for the measured tradeoff and the [offline check guide](../../Tools/HtmlChecks/README.md) to reproduce it. These fixtures do not establish live visual, GPU, input or multiplayer acceptance.
