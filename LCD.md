# LCD displays: native and screen-pinned vectors

Current API and usage: [HDR API wiki](docs/wiki/Display-Surfaces.md). This page retains lower-level and historical design notes; the wiki states current capabilities and limits.


Select **Content → HDR API** (or Script → HDR API). HDR drawing never takes over another selected content mode. Name a panel **HDR LCD**, paste `artifacts/LcdDemo.pb.cs`, run `demo`, then `vector` and `rate 30`. Run `native` to switch back. A group uses the same drawing scene across its tiles.

## Backend and refresh commands

```csharp
H("target", "HDR LCD");
H("lcd", 2, 2);
H("lcd-renderer", "vector"); // "native" is the default
H("lcd-refresh", 30);        // default 6; valid range 1–60
H("lcd-background", "transparent");
```

`H("lcd-settings")` returns `VRage.MyTuple<string,double>` with the requested renderer and rate. Renderer/rate changes apply coherently to the selected LCD's whole group, after permission checks on every member. All peers need HDR API 0.3.0 / protocol 5.

Native rendering uses MySpriteDrawFrame on the actual game surface. Its normal texture consumption is every ten simulation ticks, about six refresh opportunities per second. Requests above six do not bypass that engine schedule. Eligible rectangular fills use one quad, removing internal alpha-mask diagonals without expanding or overlapping geometry. Arbitrary triangle masks and bitmap fonts retain native raster limitations. Images use packaged native LCD texture definitions.

Vector rendering uses HDR's normal client-side triangles, vector fonts and geometric effects, flattened onto the calibrated physical screen. A bounded 1–1.02 mm outward offset avoids coplanar depth fighting; individual sorted entries have very small additional depth separation. Cached geometry is submitted each render frame and follows the current panel WorldMatrix and map view. Animation/item metadata is sampled at the requested rate, bounded by simulation/client timing. Fractional tick rates use a phase accumulator rather than rounding to divisors of 60. No catch-up bursts are emitted after a stall.

The viewer can set a local cap with `/hdr lcd-rate 15`, up to 60. `/hdr status` reports calibrated vector/native/fallback counts. Settings changes do not reset the sampling clock or bypass the local cap. Static vector geometry still has a per-frame GPU cost; the refresh setting is not a promise of FPS. Live construct poses follow the grid/subgrids each draw, with topology refreshed once per second; this is deliberately live independently of animation sampling.

## Calibrated panels and fallback

Supported models are the large/small vanilla square LCD, wide LCD and transparent LCD, each with four screen rotations: 24 exact model/material/rotation records. `Tools/ScreenCalibration` reads installed MWM screen meshes and UVs offline and verifies the generated table during the build. Runtime uses only public model, surface and rotation state; it does not import models, reflect private fields or add entities.

Unknown model paths, screen names or rotation states automatically retain native rendering. Interaction detectors and AABBs are not guessed as screen UV bounds. The same exact calibration drives vector rendering and F-button hit testing. Transparent models are calibrated for their front face; vectors and their buttons are not active from behind. Overridden/modded model geometry requires its own verified calibration.

LCD rendering exceptions are contained per panel. A vector failure switches that panel to native rendering; if native rendering also fails, HDR stops drawing that panel and reports the error once in chat and the game log. Other panels continue drawing. Reload the world or use `/hdr off` followed by `/hdr on` to retry after fixing the cause. Generated text, polygons and contours rebuild their attribute buffers when tessellated, with topology checks before admission to the render cache.

Entering vector mode submits an empty native frame once for each selected surface binding. Rotating to another native component reblanks that component. The native engine still consumes that empty frame on its own schedule, so switching modes can briefly overlap old native artwork. The native block stays enabled, powered and working. Unsupported models switch back to native frames.

## Canvas, layers and groups

`H("lcd", width, height)` sets a centered XY canvas (default 2×2). Z is discarded after the normal object/map transforms. Layers retain draw order, visibility, opacity and animation. `H("layer-order", "menu", 10)` places menus after lower layers. Vector transparent rendering uses the game's depth/OIT path; exact browser-style painter compositing is not guaranteed.

For a 2×2 wall, place four evenly spaced coplanar panels with identical facing/up orientation. Give them the name HDR LCD, select HDR API Content on each, and run `group`. `H("lcdgroup", "HDR LCD", 2, 2, 4, 4)` selects the upper-left panel as master. Each tile clips to its own calibrated screen while preserving the shared canvas. Bezels and gaps remain physical. At most eight displays/tiles are supported. One PB owns a group; `H("lcd-release")` releases the selected panel or group. Missing master data leaves remaining tiles blank; reconstruct groups after save reload.

Native frames retain a 4,096-sprite panel budget within the global draw budget. Vectors share HDR's bounded geometry compilation, clipping and global primitive budgets; complex content can be rejected or truncated rather than allocating/rendering unbounded work. Use `/hdr off` as the local rendering escape hatch.

## Background and transparency

HDR does not add a forced opaque backdrop. `H("lcd-background", "transparent")` explicitly sets both ScriptBackgroundColor and BackgroundAlpha once. `H("lcd-background", "black")` selects opaque black. RGBA vectors and supported color strings work. An optional zero-based surface index addresses embedded LCD settings, such as `H("lcd-background", "transparent", 1)`. These are ordinary synchronized terminal settings and are not automatically undone by geometry clear/release. The draw loop never writes them.

Transparent pixels do not turn an opaque physical LCD into glass. Native transparent panels use their separate transparent material and can retain lighting/reflections. The vector backend retains the physical screen/backing, uses normal depth testing and never forces visibility through walls.

## Images, construct and validation

Vector images use packaged transparent materials, interpolated UV clipping and existing effects. Native images use packaged LCD textures; sheared/warped image quads and image clipping/gradients remain native limitations. Repack old assets with Tools/PackImage.ps1 to include both paths.

`H("live")` uses up to 256 block bounds across the mechanical construct, flattened onto the canvas. It is a simplified wire map, not the full holographic block-model preview.

Dedicated servers retain definitions and authorize actions but never compile or render display meshes. Native HDR Script submissions use the installed engine's local-only DispatchSprites path. The vector backend sends no render-frame network traffic. Offline tests verify calibration, projection, clip/UV math, settings replication, sampling, fallback and rotation blanking; in-game GPU output, moving-grid alignment, transparency, grouped screens and F picking still need runtime acceptance. See MultiplayerValidation.md.
