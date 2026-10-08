# HTML frontend rendering

**ALPHA, frontend 0.1.1.** Profile1 builds a bounded paint frame, then the selected painter presents it. This frontend uses the existing HDR renderers; it does not embed a browser or add a HTML rasterizer. The authored viewport stays fixed when camera distance changes. See [Profile](Profile.md) for exact layout/font semantics, [Local API](Local-API.md) for local documents and [PB API](PB-API.md) for server-owned block documents.

## Implemented choices

| Backend | Output and dependency | Change cost and limitations |
| --- | --- | --- |
| `vector` | Plugin-free HDR retained meshes, Inter text and small SVG items; local HUD or flat world plane; requires core 0.9.10+ | Retains elements separately; changes affect dirty items. More retained items/calls than grouped SVG. Simple rectangles are clipped quads; rounded rectangles or partially clipped text use SVG. |
| `svg` | Plugin-free HDR contiguous grouped SVG chunks on the same HUD/world paths; requires core 0.9.10+ | Default chunk size is eight paint operations. A changed chunk recompiles all members. Fewer retained items can fit a larger UI, but grouping does not remove glyph geometry or guarantee less CPU work. |
| Local native LCD | Vanilla `MySprite` rectangles, Debug text and scissor sprites on an explicitly owned `IMyTextSurface` | Uses the actual surface's Debug metrics and native texture/update behavior. Requires caller-owned `SCRIPT` content with no selected text-surface script; viewport matches `SurfaceSize`. No rounded corners, stroke paint operations, images or SVG assets. |
| PB core SVG | Server-owned documents lowered to bounded core SVG artwork/UI controls on LCD/Console/Projector; existing `HDR.Draw` scene replication and viewer rendering | Display plugin-free; both button/range events require existing Client Renderer 0.9.13+ on the interacting viewer. |
| PB direct sprites | Actual native `MySprite` frames on an explicitly owned physical LCD; automatic viewport = real `SurfaceSize`; measured Debug font | Plugin-free; manual SCRIPT/NONE source. Rectangles/text/scissor subset; cooperative supplied pointer input. |
| PB sprite texture relay | Same real source LCD, relayed through existing core `lcd-texture` projected screen to Console/Projector | Existing Client Renderer 0.9.13+ on every viewer; native resolution/update/padding and opaque RGB limits; cooperative supplied pointer input. |

The HTML parser currently accepts no `<img>` or `<svg>` elements. Internal paint adapters contain some asset-oriented branches, but they do not establish an authoring feature or a registered SVG-asset path for Profile1. CSS solid borders lower to rectangle strips; they are distinct from unsupported native stroke paint operations.

Vector and grouped SVG preserve the same packaged glyph detail in the measured fixtures. Geometry remains finite: glyphs use the baked outlines and curves use bounded tessellation (default 12 segments). Native sprite text uses the game's Debug font instead. Tiny text, antialiasing, occlusion, clipping and surface projection still need live visual verification. No backend guarantees a GPU frame rate.

Client-local native LCD is an explicit alternative, not an automatic fallback for a failed HDR paint. The shared same-process native claim admits one live native frontend document per surface instance, including PB documents. That guard cannot detect another mod/script or remote writer; the consumer must maintain exclusive ownership. The native painter validates a complete sprite list before committing one `DrawFrame`. A native commit exception can leave the engine frame uncertain; it is not a cross-document transaction. Disposal clears a frame only while the painter's ownership/content-mode conditions still permit cleanup. Client-local native output is independent of `/hdr off`; a PB world relay still follows core projected-screen visibility.

## PB publication and composition

The PB adapter parses and measures on the server and rebuilds layout only for dirty sources, plain-text/data edits or viewport changes. Core SVG publishes bounded SVG items, one owned UI bundle and range values through the existing authenticated core drawing/UI endpoints. Clients receive ordinary core scene geometry and controls; they do not parse the HTML or CSS. Core SVG uses Inter cap-height metrics even when the core presents geometry through its physical native LCD renderer. The separate PB sprite path uses actual Debug `MySprite` output on a physical LCD source.

Physical LCDs require the user to select **Content → HDR API**. Binding does not change content mode, select a text-surface script, configure the LCD canvas or choose a renderer. The PB demo explicitly configures only its target's core logical canvas when `demo` is run, matching actual `SurfaceSize` aspect and fitting its authored UI with letterbox space instead of stretching it. Core native/calibrated vector LCD choices and their texture, update and model limits still apply. LCD paint uses one owned bundle layer and ordered item IDs. Console/Projector output uses public world billboards and existing range/view/table envelopes; this adapter does not establish browser-like CSS depth or compositing guarantees. Flat, front-facing artwork is the supported authoring convention.

Core SVG buttons and ranges lower to existing core controls. Range knobs need a visible, unclipped positive travel path; body/control overlap requiring interleaved paint order is rejected. Both button and range event delivery uses the core persistent pointer/input path, with **HDR Client Renderer 0.9.13+** already present on the interacting viewer. Core SVG display-only consumers need no plugin. No new renderer plugin update, full-screen HTML menu or frontend mouse capture is added.

PB publication status contains a desired revision and a **server-published revision**, not the local API's visible-frame acknowledgement. This also applies to native source-frame publication: it does not acknowledge a remote source texture or projected frame. Replication, client culling, render-off, occlusion or unavailable input/source provider can still prevent a viewer from seeing or operating a published document. PB source/generation changes and target/source retirement invalidate documents before automatic redraw; the PB must bind again deliberately. Core SVG cleanup removes only prefixed artwork, bundle, controls and values; filtered polling preserves unrelated core queues. Native cleanup follows source ownership and removes only its owned publication/relay. HTML `data-action` remains event data and never invokes `TryRun`.

## Actual PB sprites and native world relay

`bind-sprites` requires an explicitly owned physical LCD source, manually set to **SCRIPT/NONE**. With anchor = source, output is the actual native LCD and needs no plugin. With a Console/Projector anchor, the frontend creates an owned child projected screen using the existing **`lcd-texture`** source. **Every viewer needs current HDR Client Renderer 0.9.13+** for that relay; no new plugin/DLL build or update is introduced. A real physical source remains required—there is no virtual LCD creation or synthetic 128×72 fallback.

Native layout uses actual `SurfaceSize` and `Debug:Lcd` measurements; sprites are committed through the game's `DrawFrame`. The source's `TextureSize`, padding, resolution, background and native update behavior remain relevant. The relay plane uses the full real texture size/aspect, preserving padding where present. Existing provider/resource bounds still apply; an authored HTML viewport cannot create extra native pixels or bypass vanilla LCD consumption.

The relayed LCD source is **opaque RGB**, so transparent HTML canvas pixels and per-pixel alpha are lost. The source background remains game/native behavior, and the frontend does not automatically change it. Existing whole-projection opacity can affect the full plane; no new transparency shader or frontend opacity command is added. This route is a relay of actual native rendering, separate from a hypothetical off-screen HTML rasterizer.

Native sprite button/range events accept cooperative `pointer`/`pointer-cancel` calls from an already owned GUI/touch/eye provider or explicit synthetic coordinate tests. They do not automatically acquire global mouse state or attach the core persistent input viewer. Coordinates are source `SurfaceSize` pixels. Native events carry the published document revision and player ID **0**; core SVG events carry core value revisions and interacting player IDs. A shared same-process surface claim prevents PB and client-local native frontend documents from colliding, while external/remote writer exclusion remains the caller's responsibility.

The [native PB demo](../Examples/HtmlPbSpritesDemo.cs) exercises direct `lcd`/`demo` and relayed `world` modes, with separate source/anchor names and explicit ownership. Its buttons/range can receive manual `press`/`release` tests, and `_level` receives polled canonical values without gameplay writes. Live native LCD replication, projected texture readiness, input-provider mapping and GPU behavior still require in-game verification.

## Local retention and failure behavior

The controller parses on source load/replacement and lays out on source, plain-text, data or viewport changes. Pending edits coalesce. Repeating a value or leaving a document unchanged does not cause a new layout every frame. Text measurement is cached during a layout build; the core also has a bounded independent metric cache over immutable packaged glyph data.

Painters compare stable paint keys and prepared content. An unchanged retained frame performs no mutation or geometry compilation; it can still perform readiness/context checks when explicitly submitted. Dirty vector items replace only their changed owned sources. Dirty SVG chunks replace their entire changed source. Unchanged prepared item costs are reused.

New/changed text or SVG runs receive an exact CPU `geometry-cost` preflight, then successful source upserts compile again. This prevents guessing glyph complexity or silently reducing detail, but duplicate compilation is a real dirty-update cost. Grouped SVG chunks split deterministically when an exact compiler limit is exceeded; they still must fit per-item and shared owner grants. Fewer API calls are not the same as fewer triangles or faster preparation.

Preparation/admission occurs before renderer mutation. Parse or layout errors retain the prior valid controller frame. A renderer preparation error leaves prior output available; a failed mutation attempts to restore only this painter's owned previous items. If restoration fails, the context and input are retired. Pending paint failures retry after 60 simulation ticks. Endpoint-generation changes rebuild owned output and invalidate stale input. Check `desiredRevision`, `visibleRevision`, pending status and error text; a successfully accepted source replacement can still await a successful paint.

Input uses the last successfully committed visible frame. Source replacement, viewport changes, incompatible grabs, endpoint loss and `/hdr off` cancel HDR pointer interaction. Cooperative consumers are responsible for focus and game-input blocking; no backend acquires global mouse capture.

## Measured offline fixtures

The maintained [offline checks](../../../Tools/HtmlChecks/README.md), run with [`Check.ps1`](../../../Tools/HtmlChecks/Check.ps1), exercise the real packaged HDR glyph/SVG compilers against installed game references. The table below is a representative prototype CPU preparation/update snapshot reviewed for 0.1.0 on **2026-10-08**, with no game renderer, GPU, draw-thread timing or multiplayer acceptance. Initial preparation includes cost preflight and retained source updates; it is not steady-state frame time. Later runs can produce different timings.

| Fixture | Backend | Initial CPU ms | Retained items | Actual points | Actual triangles | Mutating API calls |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 5 rows | Retained elements | 2.2344 | 6 | 718 | 624 | 13 |
| 5 rows | Grouped SVG | 5.1493 | 1 | 718 | 624 | 3 |
| 20 rows | Retained elements | 6.3708 | 21 | 2834 | 2438 | 43 |
| 20 rows | Grouped SVG | 19.7828 | 3 | 2834 | 2438 | 7 |

For each fixture/backend, 1000 unchanged submissions performed zero mutations and zero geometry compiles. A single dirty update took three mutating calls in this fixture for either backend, while the grouped path rebuilt a larger source. The measured result illustrates a tradeoff: grouping reduced item/call count while retaining identical geometry, and initial CPU preparation was slower in this run. These values are not universal benchmarks or guaranteed game performance.

The gate writes `BENCHMARK.md`, `actual-hud.svg`, `actual-world.svg` and `actual-grouped.svg` under `artifacts/html-prototype/checks/retained-evidence` by default. These generated receipts are local outputs rather than repository assets. The prototype's earlier exports/previews reside locally under `artifacts/html-prototype/renderers/evidence`. They are offline projections of actual prepared/compiled geometry, not screenshots from Space Engineers.

## Future alternatives

The core [optional Client Renderer](../../../OptionalPlugins/HDRClientRenderer/README.md) already provides raster facilities elsewhere in HDR, including the real LCD-texture relay above. A dedicated off-screen HTML/browser rasterizer would need a separate pixel producer and explicit plugin integration, plus texture resolution, upload cadence, resource ownership and input-mapping work. That rasterizer is unimplemented; the native relay uses an actual owned source LCD and supplies no silent replacement for another failed backend.

Curved HTML surfaces are unimplemented. The PB property extends authoring through the existing HDR PB/display contracts; it does not add a new GPU renderer, browser compositor or curved-screen input route.
