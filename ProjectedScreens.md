# Projected HDR screens (0.7.1)

Current API and usage: [HDR API wiki](docs/wiki/Display-Surfaces.md). This page retains lower-level and historical design notes; the wiki states current capabilities and limits.


Name a Console or Projector **HDR Display**, paste `artifacts/ProjectedScreenDemo.pb.cs`, and run `demo`. Two floating screens follow the anchor with no physical LCD or cloned block. If you are behind the front face, run `flip`; `flat` places them horizontally, and `upright` restores vertical placement. `pause` stops the synthetic orbit, `orbit` starts it, `toggle` hides the view overlay, `rate 10` changes sampling, and `clear` removes the two screens.

This milestone demonstrates output behavior using a synthetic scene supplied by the PB. Optional data providers own camera acquisition and observation fusion; HDR only draws their prepared output. Depth is resolved on the viewing client from the synthetic source geometry. Uncovered pixels have no output geometry; an explicitly chosen screen background can still be visible behind them.

## Drawing interface

Use the existing `HDR.Draw` delegate and its short `H(command, args...)` helper. A screen ID uses 1–12 lowercase letters/digits/hyphens. Create or update a screen with a rigid anchor-local pose and physical dimensions, then draw into it:

```csharp
H("target", "HDR Display");
H("screen", "main", MatrixD.CreateTranslation(0, 1.5, 0), 2.4, 1.35);
H("text", "title", "HDR DECK", 0, 0.5, 0, 0.12, "cyan");
H("layer", "title", "menu");
H("layer-order", "menu", 10);
```

Optional fifth/sixth values on `screen` set the logical canvas width/height; defaults match physical dimensions. Direct drawing uses centered canvas coordinates with Y up. Z is flattened when presenting the screen. `screen-aspect` chooses `contain` (default), `stretch` or `cover`. All output is clipped to the physical rectangle and the anchor's existing range/table volume. The demo explicitly disables the table volume to place screens outside its normal truncated pyramid.

`H("screen", "main")` selects an existing screen. `H("screen-target", "")` returns drawing to the anchor. Changing `target` also returns to anchor drawing. Each screen and PB has its own object and layer namespace; local object IDs are at most 32 characters without `!` or `~`, and local layer names at most 12 characters. The internal `!s!` object prefix and `s_<screen>__` layer prefix are reserved.

Selected screens support lines, circles, points/wires, polygons/contours, text/labels, SVGs, registered images, effects, layers and declarative animations. `view` affects screen content. `clear` removes only that screen's content and animation state; `screen-remove` also deletes the selected screen. Callback curves and anchor-wide controls (construct tracking, range/volume, LCD settings and global pause/resume) require anchor selection. Existing `HDR.Api` methods continue addressing the anchor rather than virtual screen handles.

## Screen commands

| Command | Arguments after command | Behavior |
|---|---|---|
| `screen` | id, pose, width, height, optional canvas width/height | Create/update and select; id alone selects |
| `screen-pose` | rigid MatrixD | Change placement without changing content |
| `screen-background` | color | Default transparent; independent of source coverage |
| `screen-opacity`, `screen-visible` | 0–1 float, or bool | Presentation controls |
| `screen-two-sided` | optional bool (default true), front opacity, back opacity (both default 1) | Enable/disable reverse viewing and set independent side multipliers |
| `screen-side-opacity` | front opacity, back opacity, both 0–1 | Change side multipliers without changing sidedness |
| `screen-side-settings` | none | `(twoSided, frontOpacity, backOpacity)` tuple |
| `screen-refresh` | Hz 1–60 | Default 6; bounded by viewer cap and preparation budget |
| `screen-aspect` | contain/stretch/cover | Canvas sizing policy |
| `screen-settings` | none | `(id, width, height, requested Hz)` tuple |
| `screen-target` | id or empty string | Select screen or anchor |
| `screen-remove` | none | Remove selected screen and its content |
| `sprites` | MySprite[], optional source width/height | Replace bounded sprite-compatible frame |
| `screen-sprites-clear` | none | Remove sprite frame |
| `screen-scene` | Vector3D[], triangle int[], Vector4[] | Supply opaque synthetic scene with one color per triangle |
| `screen-camera` | eye, target, up, optional vertical FOV radians/near/far | Perspective view within synthetic scene |
| `screen-raster` | width 8–128, height 8–72 | Default 64×36 perspective working resolution |
| `screen-orbit` | radians/second, −2 to 2 | Client-side orbit around target using declared camera up; zero disables |
| `screen-scene-clear` | none | Remove synthetic source |
| `screen-source` | provider ID, source ID | Display a client-local external source; replaces the synthetic source |
| `screen-source-clear` | none | Detach external source |
| `screen-surface` | plane/cylinder/sphere, optional radius/horizontal radians/vertical radians/inside or outside | Select shape; curve pose origin is its centre |
| `screen-mapping` | angular/equirectangular, geodesic, pinhole | Curved canvas/ray mapping; pinhole requires sphere |
| `screen-quality` | chord error in meters, .00001–1 | Adaptive curved tessellation; excessively fine output fails within work/geometry caps |
| `screen-clip` | `Vector4[]` planes, or no args to clear | Up to eight planes in anchor coordinates, keeping `dot(normal, point) <= W`; clips video, overlays and background together, including curved surfaces |
| `screen-renderer` | vector/raster, optional requested width/height, 1 or 4 samples | Choose direct geometry or a client-composited RGBA texture |
| `screen-resolution` | requested width/height 16–4096, aspect at most 64:1 | Desired maximum for UI raster and v2 sources; clients select smaller effective frames |

The new raster/curved commands and their optional plugin are detailed in [RasterSurfaces.md](RasterSurfaces.md). Native/vector physical LCDs continue using their existing backends; these new settings apply to virtual screens attached to Console/Projector anchors.

Screens are one-sided by default. After selecting a screen, `H("screen-two-sided", true, 1, 0.35)` enables both views, with full front opacity and 35% back opacity. For a plane, front is local +Z; for a cylinder or sphere, front is the `inside`/`outside` side selected by `screen-surface`. The same artwork appears from the reverse, so lettering is mirrored. Side multipliers apply to backgrounds, video, sprites, raster UI and vector content, and multiply `screen-opacity`, object/layer opacity and paint alpha. Zero opacity suppresses that side's rendering and source demand. `H("screen-two-sided", false)` restores one-sided viewing and both multipliers to 1. Foreground layer offsets follow the viewer's side so backgrounds stay behind content. Both views share one content/texture resource; curved layer geometry is remapped when crossing sides.

## Compatibility and bounds

The sprite adapter supports `SquareSimple`, `SquareHollow`, `Circle`, `CircleHollow`, `Triangle`, `RightTriangle`, `Line`, Debug vector text, alignment, rotation, RGBA and nested clip rectangles. It computes painter-order alpha composition on the client at 128×72, then merges equal-color pixels into non-overlapping rectangles. This compatibility frame has finite raster resolution. Direct HDR overlays retain vector geometry. Unsupported textures/fonts/types fail with a capability error; this is not a virtual native `IMyTextSurface` or an engine offscreen texture.

Perspective clipping happens before projection and depth uses reciprocal-Z interpolation. Visibility is sampled at pixel centers; features that miss every sample can be lost. There is no claim of native camera fidelity or guaranteed thin-occluder detection. The synthetic view uses opaque flat triangle colors; translucent source triangles and native game textures are rejected. Background, sprite frame and direct overlays are composited presentation layers; their transparency does not change perspective-source validity.

Limits remain shared with the anchor: 16 objects (including screen slots), default 4,096 presented points and 8,192 primitives, plus per-object and global draw-work limits. `H("budget", points, primitives, drawWork)` configures the selected anchor; see [DisplaySources.md](DisplaySources.md#rendering-budgets) for ranges and the viewer-local work cap. At most eight screens per anchor and sixteen globally; each side is .05–12 meters, area at most 100 m², and all corners remain within 25 meters. Source meshes and replication declarations have additional shared admission checks. Expensive source preparation shares the one-job-per-tick compiler budget, queues no catch-up work, and may deliver fewer fresh frames than requested Hz. External-source presentation may reduce detail within a fair share of the remaining budget, retaining unknown holes. Other unsupported output/work limits fail explicitly. Preparation and drawing errors are contained per screen.

The anchor's transform follows every render frame. Sampled animation, sprite preparation and perspective acquisition have separate clocks; moving the anchor does not recapture the synthetic view. Power loss drops local caches, and removal/access revocation removes child declarations/content. `/hdr off` clears local caches; `/hdr on` permits rebuilding. `/hdr lcd-rate` also caps projected-screen sampling locally.

Screen declarations use public HDR scene replication, protocol **11**. All peers need **0.7.1**. External-source declarations carry provider and source IDs, not observed geometry, camera policies or evidence. Prepared provider frames remain local to the viewer.

Offline checks cover screen namespaces/lifecycle, actual Protobuf full/delta updates, malformed admission, moving transforms, perspective clipping/depth/unknown masks, sprite compositing and bounds. Compilation against the installed game assemblies is verified. In-game placement, visual layering, world occlusion and frame-time measurements remain acceptance checks for this prototype.
