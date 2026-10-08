# Release history

These notes preserve release-time behavior and validation limits. Later releases supersede earlier defaults and limits. Current setup and commands are in [README.md](README.md) and the [client renderer guide](OptionalPlugins/HDRClientRenderer/README.md).

## HDR API 0.9.9 / Client Renderer 0.9.13 — alpha

- Retained numeric controls: stepped ranges, line/polyline movement and constrained rotation, with explicit PB event polling and mod getter/setter bindings.
- Native block UI focus remains on the original hologram. Use enters a bundle; controls can be clicked or dragged; mouse-up ends the value gesture; Escape or context loss exits. Native focus requires Client Renderer 0.9.13 on the viewing client. Cooperative mod HUD/world input remains plugin-free.
- Server-authoritative values use revision checks and per-value interaction leases. Coupled controls update atomically; source changes retire stale gestures. A fixed registered PB argument wakes the script, which reads structured value events.
- Startup, disable, source/context loss and unload release owned input and queued work. An absent input provider reports `Requires plugin: HDR Client Renderer (interactive pointer; 0.9.13 or later).` without entering native input.
- Scene protocol 19 carries the additive numeric-control definitions. Existing drawing, effects, camera/portal renderer paths and API protocol names remain intact.
- Added PB and mod interaction demos, API documentation and SVG diagrams. Offline checks cover input ownership, timing races, bounds and serialization; live native GUI, GPU and multiplayer acceptance is still required.
- Public setup paths are configurable. The repository and GitHub wiki are published with prominent ALPHA notices.

## HDR API 0.9.8 / Client Renderer 0.9.12

- Retained per-item effects for authored geometry: seeded flicker, moving refresh bars, translucent depth copies, cosmetic particles, projection rays and fade/wipe/dissolve transitions.
- PB Console/Projector and mod world rendering support geometric depth and rays. LCD and mod HUD paths support the planar subset, with explicit rejection of world-only effects.
- Effects are evaluated locally from a frozen clock sample without per-frame script uploads or native camera/portal lease changes; canonical content has drawing priority over optional detail.
- Added checked PB/mod examples, a complete effects reference and an accessible SVG diagram. Geometric light shafts do not implement physical volumetric scattering.
- Scene protocol 18 carries validated numeric effect declarations and metadata-only updates. The optional Client Renderer remains 0.9.12; live visual/GPU acceptance is pending.

## HDR API 0.9.7 / Client Renderer 0.9.12

- Separate `HDR.ModClient/1` consumer API for local retained HUD/world contexts, SVG/textured geometry and cooperative menus.
- Stable `HDR.Api/1` typed-helper negotiation and read-only PB block-capability queries; LCD, Projector and Console capabilities remain distinct.
- Local plugin-status queries and nonthrowing helpers; absent camera/portal/raster components report `Requires plugin…` without entering their native paths.
- General mod and existing block displays share the viewer's work cap with protected category shares.
- Reorganized public documentation into a complete wiki with examples and accessible SVG diagrams; preserved compatibility paths and release notes.
- Scene protocol 17 and optional Client Renderer 0.9.12 remain compatible. Live HUD/GPU acceptance is still required.

## HDR API 0.9.6 / Client Renderer 0.9.12

- Portal capture runs before the primary scene; ordinary camera panoramas retain their existing timing.
- Eligible paired planes sample cropped captures directly with matching projective metadata.
- Portal detail uses physical presentation dimensions; native viewport dimensions remain part of rendering validation.
- Retained textures preserve stamped ownership across refreshes, resizes and failures.
- Offline checks and independent review pass. Live GPU behavior still requires in-game validation.

## HDR API 0.9.6 / Client Renderer 0.9.10–0.9.11

Client **0.9.10** introduced retained transparent portal front bindings when demand disappears so queued billboards cannot dereference disposed textures. Its 512×512 / 30 Hz demo defaults are superseded by the current native-size / 60 Hz demo.

## Changed in 0.9.5 (portal frame retention)

Client **0.9.9** retains portal image evidence across successful captures, preventing cached displays from blanking between source refreshes. The world mod avoids prematurely releasing cached evidence when a failed acquisition returns the same lease. The outer renderer still clears the view and releases it on a hard budget failure; new or revoked leases follow normal cleanup. Existing portal scripts and protocol 17 remain compatible. Native rendering needs a game restart after installing the client update; live flicker acceptance is pending.

## Changed in 0.9.4 (native portal and occlusion providers)

Client **0.9.8** fixes the native draw guard's JIT-inlining failure observed in 0.9.7 and performs private shader compilation in bounded background work. The world mod and portal script remain unchanged.

Optional client renderer **0.9.7** integrates current-depth display occlusion and native portal raster sources. Captures run after the primary scene has consumed its command lists. `/hdr camera occlusion on` enables conservative suppression for complete static display enclosures; unresolved GPU queries retain capture demand. `/hdr camera occlusion off` restores the default. This never removes occluded geometry inside ordinary camera images.

Try `artifacts/PortalDemo.pb.cs` on one Console/Projector named **HDR Portal**. Start with `plane`; `ellipsoid` and `mesh` use differential mappings, while `stealth` sets up a coincident outward ellipsoid and excludes its interior capture shell. `/hdr portal status` reports local native readiness and failures. Strict mode leaves rays not representable by the perspective capture transparent; `approximate` explicitly permits shell sampling with finite-view and parallax limitations. See `Api/PortalApi.md` and the optional client README for limits. Each viewer needs the plugin; the server replicates bounded declarations only. These modes have offline tests and installed-engine ABI checks; in-game GPU/visual acceptance remains pending.

## Changed in 0.9.3 (adaptive camera stability and detail)

Client **0.9.6** added automatic finer capture subdivisions driven by source-UV density variation, with a default 16-leaf limit, a 64-leaf ABI and configurable scene-pass cost. Use `/hdr camera adaptive on`, `/hdr camera tiles 16`, and `/hdr camera tile-cost 16384`; Camera Sphere v14 remains supported. The camera compositor uses a direct cell-to-leaf lookup. Client 0.9.7 now integrates the previously dormant portal and occlusion components as described above.

Client 0.9.5 additionally bypasses previous-frame player-camera actor occlusion during auxiliary capture. That cache could omit entire grid groups before the model hooks ran. The camera still uses its own frustum and depth testing, and the main-view occlusion cache is untouched. World mod 0.9.3 and Camera Sphere v14 remain compatible.

Client 0.9.4 additionally isolates the legacy proxy's already-updated marker, which previously let captures skip their own visibility/transform refresh. It remains compatible with world mod 0.9.3 and Camera Sphere v14. See the client README for the reproduced native-query regression.

Adaptive atlas packing now rejects shelves whose required height exceeds the texture, rather than clamping the height and copying crops out of bounds. Failed staging-only captures keep the completed front image; failed publication, device loss and access revocation still invalidate it. Instanced/static model LOD strategies are isolated from the player view and restored after capture, in addition to the individual-model proxy fix.

Use `artifacts/DirectCameraSphere_v14.pb.cs`. Run its PB with `size 2048` for a 2048px camera ceiling and 4096×2048 panorama ceiling. The default stays 1024. Pixel/pass controls schedule work; they do not request more image detail. `/hdr camera status` now reports the actual panorama size, camera atlas dimensions, tile count and sampled pixels. `/hdr camera adaptive off` requests full uniform capture resolution for comparison; adaptive mode otherwise reduces sampling to the visible UV density. Camera rendering uses private buffers and is no longer capped by the main window's smaller dimension. Raster UI retains its existing bounds. These changes are verified offline; live image quality and GPU behaviour still require in-game acceptance.

## Changed in 0.9.1 (retained images and local performance controls)

Camera atlases keep a stable front/staging canvas while source-UV density changes. New layouts publish only after their full staged batch completes; refinement no longer destroys the front texture or changes its authorization generation. A completed image remains valid while its camera is authorized, working and demanded, even if a refresh waits for budget. Power/access loss, source retirement and render/device errors still invalidate it.

The fixed one-viewport/4K pixel ceiling is removed from the default. Viewer-local preferences now persist in `HDRClientRenderer.settings.xml` in the game's plugin local storage:

| Local chat command | Effect |
| --- | --- |
| `/hdr camera status` | Show active preferences |
| `/hdr camera pixels off` | Disable the shared per-frame output-pixel ceiling (default) |
| `/hdr camera pixels 8294400` | Set an absolute output-pixel budget |
| `/hdr camera pixels viewport 2` | Set a budget relative to the current viewport |
| `/hdr camera passes 12` | Allow twelve native capture passes per main frame (default: six) |
| `/hdr camera passes off` | Remove the configurable pass ceiling; one complete batch per source per main frame remains the natural work bound |
| `/hdr camera rate 60` | Apply an optional local ceiling to the requested image rate |
| `/hdr camera rate off` | Follow the display's requested rate (default); no hidden 30 Hz clamp |
| `/hdr camera adaptive on` / `off` | Select cropped density-driven or uniform capture |

Rate pacing applies to complete camera-image batches rather than separately delaying every tile. Tile continuation can run in the same or a later main frame, subject to the selected pass and pixel budgets. Denied work keeps its eligibility and published image. Native texture-size, resource-lifetime and allocation validation remain in place.

Paste `artifacts/DirectCameraSphere_v13.pb.cs` for the camera demo, which accepts `rate 60` as well as the existing commands. Changes are compiled and tested offline; in-game flicker and throughput still require visual acceptance.

## Added in 0.9.0 (adaptive capture and general surfaces)

Use HDR API **0.9.0 / scene protocol 15** with HDR Client Renderer **0.9.0**. Both retain the allocation, readonly-state, particle-buffer and failed-capture cleanup fixes verified in 0.8.3. Servers retain only validated display declarations; camera images and preparation stay local to each viewer.

The client shares a frame pixel ledger between native camera capture, panorama output, LCD relay copies and UI uploads. Its default ceiling is one main-viewport pixel count, capped at 8,294,400 pixels. Pending producers rotate fairly; deferred work retains its previous valid image. Clears, mip generation and read/copy bandwidth are tracked separately. This bounds base output pixels, not overdraw, MSAA samples, repeated geometry traversal, lighting, shadows or total GPU time.

Fresh 16×16 source-camera density fields now drive up to four native off-axis quadrant captures, with independent 64–512 pixel tile sizes. Unneeded quadrants can be omitted. Unknown/stale fields fall back to a whole forward view. A bounded atlas publishes only after every selected tile completes from one frozen camera pose; tile work shares the three-secondary-pass limit per main frame. Each camera is limited to 30 native passes per second, so four-tile batches can publish at most 7.5 complete images per second before shared-budget limits. Cross-frame world motion can cause seams despite the frozen camera pose. Requests are ceilings, not guarantees.

`H("screen-camera-quality", "normal")` preserves ordinary effects and is the default. `"lite"` opts out of secondary-view shadows, fog, SSAO, foliage, bloom and flares. If visible consumers share one physical camera, normal quality wins whenever any consumer requests it. Lite changes only the auxiliary pass and restores main-view settings afterward.

`H("screen-ellipsoid", new Vector3D(3, 2, 4))` adds spheroids/ellipsoids. `H("screen-mesh", points, indices, uv)` maps vector or raster content to an authored UV mesh. Two-sided opacity, clipping, layers and density measurements use those surfaces. See [general surface commands and limits](docs/GeneralSurfaces.md) and paste [GeneralSurfaceDemo.pb.cs](Examples/GeneralSurfaceDemo.cs) for the example.

[DirectCameraSphereDemo.pb.cs](Examples/DirectCameraSphereDemo.cs) is now v12, with `quality normal` and `quality lite`. The existing names, sphere placement and transparency controls remain available. Offline tests and installed-engine ABI/shader checks do not establish visual quality or frame rate; live GPU acceptance remains necessary.

World-occlusion capture suppression and the curved portal/capture-shell mode remain disabled. The new conservative proof and shader/math foundations do not constitute a working portal or stealth bubble. Occlusion results need the correct main opaque-depth phase and current-frame evidence; portal exclusion must happen before all relevant depth writes.

## Added in 0.8.0 (direct camera panoramas)

The 0.8.x series used **HDR Client Renderer 0.8.3** for native camera panoramas and LCD relays. It includes the allocation, hook and readonly-state repairs, and addresses the particle binding and failed-capture culling-query cleanup reached by 0.8.2. Its world mod was **0.8.0 / protocol 14** with **Direct Camera Sphere v11**. Details are in [the client renderer README](OptionalPlugins/HDRClientRenderer/README.md).

`screen-source`, `camera-panorama`, `"cameraId,cameraId,..."` declares one to six physical cameras. The optional HDR Client Renderer captures their forward views into client-owned textures and composes an equirectangular panorama on the GPU. This path requires no LCD buffers or CameraLCD dependency. The ordinary world mod and server exchange only declarative camera IDs and display settings; they perform no capture, image transfer, raycasts or map scanning.

`screen-panorama` accepts vertical FOV in degrees, edge feather in degrees, saturation and per-camera capture resolution. Defaults are **105°, 8°, 1.15 and 1024²**. FOV is bounded to 60–120°, feather to 0–25°, saturation to 0–2, and capture size to 256, 512 or 1024. Settings sharing the same source declaration for one PB must match. The final panorama has a 2048×1024 request ceiling, with local visibility/distance LOD and GPU limits lowering actual work. Requested camera cadence is capped locally; the demo requests 30 Hz. Independent camera positions still create near-object parallax: feathering softens joins but does not invent depth.

Paste `artifacts/DirectCameraSphere_v11.pb.cs` (generated from `Examples/DirectCameraSphereDemo.cs`) into a PB. Name a Console/Projector **HDR Display**. The demo uses all 360 Camera blocks on the PB's construct, or ordinary blocks named **HDR Camera [N]** when there are no 360 cameras. Camera names need not be unique because the direct source uses entity IDs. Orient the cameras outward to cover the desired directions; uncovered areas are transparent by default. The sphere is viewable inside and outside at 85% opacity, has radius 3 m, and its centre is **1.5 m + one grid-block size** above the display anchor.

Demo commands include `rate 30`, `size 1024`, `fov 105`, `feather 8`, `saturation 1.15`, `opacity 0.85`, `transparency 0.15`, `back 1`, `both`, `inside`, `gaps transparent`, `gaps grey` and `clear`. The older v10 LCD-buffer demo remains available for the CameraLCD-based path.

The viewing client measures a **16 × 16 density field in each source camera's UV space**, using the actual mapped display mesh, viewport clipping, display crop and camera overlap. Fields include local magnification, area density and blend contribution; they do not assume that a sphere's edges always need more detail than its centre. The inexpensive visibility mask follows the current view. Full density measurements share a global limit of one calculation per 30 simulation ticks, with a bounded cache and fair scheduling across source contexts. When the view changes before another measurement is due, currently visible sources receive conservative maximum bounds and no claimed fresh samples. All matching screens contribute to demand; hiding one screen does not hide another screen's cameras.

In 0.8.x these measurements supported visibility culling and uniform capture budgeting. Native captures then did not vary sampling density within an image. Version 0.9.0 adds cropped off-axis adaptive tiles; a postprocess warp cannot recover detail absent from the source capture. Pixel limits also do not bound repeated scene culling, geometry and lighting costs. Normal-quality rendering remains the default; a stripped visual-effects preset is a planned opt-in option, separate from the shared-state protections required for every secondary view. Offline compilation, ABI checks and policy tests do not establish in-game image quality or frame rate.

### Planned curved portal / shell mode

A separate future mode could use the camera rig to define a destination sphere and render the client scene through it. If the two spheres are related by rotation, translation and uniform scale, one transformed virtual viewpoint can provide the correct viewer-dependent rays; the curved boundary does not itself require six camera images. A genuinely noncentral ray mapping needs more extensive geometry projection, depth and material integration. The planned mode must also handle curved exit clipping and foreground occlusion. It is **not implemented in 0.8.0**. [Multi-perspective rasterization research](https://diglib.eg.org/items/1f19fcbd-703d-4d1c-b556-69efa066767f) demonstrates a geometry-based approach, while [Microsoft's stencil documentation](https://learn.microsoft.com/en-us/windows/win32/direct3d11/d3d10-graphics-programming-guide-depth-stencil) describes masking a second scene into the main view.

## Added in 0.7.5 (camera-facing surface crops)

`screen-clip` defines up to eight convex crop planes in the display anchor's coordinate frame. Video, raster/vector overlays and background are clipped together, including on curved screens. Texture coordinates interpolate at the boundary. Wholly cropped camera patches request no HDR source capture work.

The v10 spherical camera demo automatically configures distinct cube views when five or six dedicated video buffers are unbound or all reference the same 360 Camera. A normal or mixed camera array retains its existing bindings. Run `360` to force the one-camera setup, `array` to retain manual bindings, or `auto` to restore detection. The display defaults to the hemisphere facing the reference camera's physical forward direction, transformed into the Console/Projector frame. Camera rotation and projection-anchor rotation update that boundary. Run `sphere` for the complete panorama or `hemisphere` to restore the camera-facing half; normal camera arrays use `HDR Camera 1` as their reference when present.

For several 360 Camera blocks, run `cameras` with one square `HDR Video N` LCD per camera (up to seven). The script binds one forward 90-degree capture per camera, joins those images according to their physical orientations on a full spherical display, and leaves uncovered directions empty or grey. Duplicate camera names become distinct `HDR Dome N` names so CameraLCD can resolve them. Six outward-facing cameras can cover the sphere with six captures instead of thirty. This mode does not capture every camera's full 180-degree dome; producing one complete fisheye image per camera would require a new capture compositor.

HDR Client Renderer 0.7.8 requires a completed, direction/FOV-verified capture from 360 Camera Capture 0.1.2 before relaying an LCD tagged with `Camera360.Face`. An absent, stopped or incompatible addon produces a diagnostic instead of silently repeating a native camera's single view in six directions. Capture stays in the separate camera project; the HDR world mod remains a display API. These changes are compiled and tested offline; the latest native runtime crash still requires an in-game retest and crash stack.
## Added in 0.7.4 (panorama budgets and PB lifetime)

Joined image meshes keep shared vertices and use the configured scene geometry budgets. PB displays retire when their owner powers off or its script changes; unchanged scripts retain their output between calls. The v7 spherical camera demo republishes after a PB power cycle using screen-exists. Client renderer and 360 Camera capture binaries are unchanged.

## Added in 0.7.3 (joined spherical images)

Spherical camera images can share a panorama group with H("screen-join", "cameras"). Their observed viewing cones are partitioned on one sphere, choosing the most central valid camera in overlaps, preserving image orientation and leaving missing directions empty. The v6 camera script resolves each LCD's actual camera from Custom Data, follows orientation changes, requests up to 512px from the source texture, and offers grey or transparent gaps.

The separate 360 Camera project supplies one camera block with six client-side cube captures. The demo's 360 command configures its six LCD buffers and presents a complete sphere. HDR retains only display declarations, stitching and local source-demand hints; capture code and camera blocks live in that independent project.

## Added in 0.7.2 (raster quality and camera-pass isolation)

UI raster textures allow up to 1,048,576 pixels and 2,048 per side. The Raster Surface demo requests 2,048 × 1,024; nearby clients can use 1,448 × 724, with smaller tiers at distance. Its circle uses 192 segments. Row-clipped triangle rasterization avoids scanning empty bounding-box areas while retaining the existing eight-million-sample work cap. Older client DLLs negotiate their smaller bitmap ceiling. Camera LCD copies retain their previous quotas.

The optional client renderer excludes HDR billboards from CameraLCD's secondary capture pass, leaving the player's main view intact and avoiding display feedback. The installed-game crash log implicated billboard gathering inside that camera pass; the isolation patch is validated offline, with in-game crash verification still pending. HDR's surface presentation still uses billboard triangles; a dedicated textured-mesh backend is not implemented. The camera demo now documents CameraLCD setup and offers `fov 60` to match standard zoomed-out cameras.

## Added in 0.7.1 (two-sided screens)

Projected screens optionally display from both sides, with independent opacity. Use `H("screen-two-sided", true, 1, 0.35)` after selecting a screen. Side settings follow each viewer locally; zero opacity suppresses rendering and source demand on that side. Layer offsets reverse so backgrounds stay behind their content. Plane, cylinder and sphere screens share the same artwork on either side.

## Added in 0.7.0 (raster and curved displays)

Projected screens now support plane, cylinder and sphere canvases, including inward-facing surfaces. SVGs, text and geometry can remain vectors, or a client RGBA compositor can flatten their layers into one texture. Meshes and native grid previews remain available for 3D objects. The optional trusted [HDR Client Renderer](OptionalPlugins/HDRClientRenderer/README.md) uploads those textures and relays existing LCD images through owned GPU targets. It contains no native world-camera capture. A separate video producer such as [CameraLCD Remastered](https://github.com/StarCpt/SE-CameraLCD-Remastered) must first paint the source LCD. No plugin or pixel processing is required on the server.

Client viewport size, projected texel density and distance select the effective resolution with hysteresis. Offscreen screens skip preparation/drawing and report no provider demand. Requested resolution up to 4,096 per side is metadata; UI textures are bounded to 2,048 per side / 1,048,576 pixels, and LCD relay copies retain 1,024 per side / 262,144 pixels. Provider requests carry the effective size, and `source-demand` aggregates visible needs. The relay downscales its own GPU copies; controlling the original camera producer's capture needs that producer to honor demand.

Paste [RasterSurfaceDemo.pb.cs](Examples/RasterSurfaceDemo.cs) to compare the surface/render modes, or [SphericalCameraDemo.pb.cs](Examples/SphericalCameraDemo.cs) for six camera-image patches. See [RasterSurfaces.md](RasterSurfaces.md) for setup, commands and limits. All HDR peers need **0.7.5 / scene protocol 13**. The core mod, client DLL and demos compile against the installed game; visual GPU output and CameraLCD interoperability still require in-game acceptance.

## Added in 0.6.0 (display-only API)

Camera acquisition, permission leases, progressive scanning, retained observations, reconstruction and scanner examples now live in the independent **CamScan API** project. HDR works on its own and draws supplied data. Its generic external-source interface accepts bounded prepared geometry from optional providers; it contains no sensor IDs, camera scan policies, camera-capture raycasts or observation maps. See [DisplaySources.md](DisplaySources.md).

`HDR.Draw` retains drawing, SVGs, images, animation, layers, text, LCDs, projected screens and synthetic perspective rendering. `screen-source` replaces the former camera-specific screen commands. Camera PB scripts must use `CamScan.Draw` for scanning and `HDR.Draw` for display. Rebuild and re-paste the camera demo from the CamScan project, and enable both mods to use it. All HDR peers need **0.7.5 / scene protocol 13**.

Anchor rendering budgets remain configurable with `H("budget", points, primitives, drawWork)` after selecting the anchor. Defaults are 4,096 points / 8,192 primitives / 20,000 work units. Valid ranges are 256–65,536 points, 256–131,072 primitives and 1,000–200,000 work units. External images share the room remaining after visible static geometry. `/hdr work N` separately controls each viewer's global work cap.

## Added in 0.4.0 (projected-screen prototype)

Console/Projector anchors can now own floating rectangular HDR screens, with separate object/layer namespaces, direct vector overlays, bounded sprite-compatible frames and a client-rendered synthetic perspective view. Paste [ProjectedScreenDemo.pb.cs](Examples/ProjectedScreenDemo.cs) for an anchor named **HDR Display**. See [ProjectedScreens.md](ProjectedScreens.md) for commands, quotas and remaining in-game checks. Camera acquisition and fusion are supplied by the separate CamScan API. All peers need version 0.7.5 / protocol 13.

## Added in 0.3.0

LCDs now offer native or calibrated screen-pinned vector rendering. Set `H("lcd-renderer", "vector")` and `H("lcd-refresh", 30)`; defaults remain native / 6 Hz. Six vanilla panel models and their four rotations are supported; unknown models retain native rendering. A viewer-local sampling cap uses `/hdr lcd-rate 15`. Vector geometry follows the panel and map view every render frame, with independently sampled animation metadata. See [LCD.md](LCD.md) for calibration, backgrounds and remaining runtime checks.

## Added in 0.2.0

- `HDR.Draw` and `HDR.Api` replace the previous terminal-property names. Regenerate/re-paste PB scripts; no old entry-point aliases are registered.
- Radial gradients use shared concentric bands, removing triangle-shaped color patches. Resolution 1–8 selects 8–64 bands, with 32 angular segments. This improves smoothness but is not continuous shader interpolation.
- Packaged proportional vector text supports lowercase, multiline alignment, Latin accents, Greek, Cyrillic and selected symbols. Glyphs are triangulated offline; unknown characters use one fallback glyph. Complex shaping, RTL, emoji and CJK are not supported.
- SVG gains rounded rectangles, local `use`/`symbol`, styled nested `tspan`, numeric Unicode entities, uniform vector masks, and per-shape color-matrix filters. External references, browser scripts, general blur and group filter compositing remain unsupported.
- `HDR.UI` adds native F/Use buttons, fixed PB actions, layer toggles and viewer-local menus. Mouse focus remains gated until a backend can confirm actual input capture. See [UI.md](UI.md).
- LCDs are explicit 2D targets: select **Content → HDR API**. Layers have draw order; several coplanar panels can share one canvas. See [LCD.md](LCD.md).
- Projector range limits and an oriented truncated-pyramid Console envelope restrict HDR geometry. Native model previews use conservative whole-model culling. See [ProjectionVolumes.md](ProjectionVolumes.md).

Paste [the feature demo](Examples/HdrFeaturesDemo.cs) for a Console/Projector named **HDR Display**, or [the LCD demo](Examples/LcdDemo.cs) for a panel named **HDR LCD**. The installed local mod folder is **HDR API**; replace the previous local-mod entry in the save's mod list before loading. All peers need version 0.7.5 / protocol 13.
