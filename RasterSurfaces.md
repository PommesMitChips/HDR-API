# Raster and curved displays (HDR API 0.7.4)

Current API and usage: [HDR API wiki](docs/wiki/Display-Surfaces.md). This page retains lower-level and historical design notes; the wiki states current capabilities and limits.


HDR keeps direct triangles/lines for geometry and adds RGBA textures for virtual interfaces and video. These backends share virtual-screen transforms and layer namespaces. Raster UI provides source-over composition and 1×/4× sample coverage, including shared-edge handling without translucent diagonal seams. SVGs still use the existing bounded parser; their unsupported browser features remain unsupported.

## Components

The server runs the normal HDR mod and the PB. It stores bounded screen/layer/source declarations and replicates them. Every viewer prepares and draws its own content. The optional `OptionalPlugins/HDRClientRenderer` DLL runs on a trusted viewing client only. It uploads RGBA and uses the mod's fixed material definitions to copy existing LCD images into generated textures. It does not capture world camera scenes or require a server plugin.

The first real-video route is an LCD producer such as [CameraLCD Remastered](https://github.com/StarCpt/SE-CameraLCD-Remastered), which describes itself as a client plugin for camera views on LCD screens. Once the source LCD displays its camera image, HDR relays that local image. This interoperability and the GPU copy path have not been run in-game here. HDR contains no copied CameraLCD code or assets.

## Short commands

```csharp
if(H("findtarget", "HDR Display")==null)throw new Exception("Name a Console/Projector HDR Display.");
H("volume", false); H("range", 0);
H("screen", "hud", MatrixD.CreateTranslation(0,1.5,0), 4,2);
H("screen-surface", "sphere", 3, Math.PI*2, Math.PI, "inside");
H("screen-mapping", "angular");
H("screen-renderer", "raster", 512,256,4);
H("screen-refresh", 6);
H("screen-two-sided", true, 1,0.35); // primary / opposite opacity
H("text", "title", "HDR DECK", 0,0.5,0,0.15,"cyan");
H("layer", "title", "menu");
```

`screen-surface` selects `plane`, `cylinder` or `sphere`, with optional radius, horizontal/vertical angular span and `inside`/`outside`. Defaults are radius 2, full 2π horizontal / π vertical, inside. Curve pose origin is the centre; +Z is the patch centre. Inside presentation mirrors the geometric frame horizontally so text/images face the central viewer correctly. The viewer must be on the chosen side. Cylinder height is physical screen height; sphere coverage follows angular spans. Content Z is flattened before mapping.

`screen-mapping` selects angular/equirectangular, geodesic, or pinhole. Pinhole maps perspective rays onto a sphere using the declared `screen-camera` vertical FOV and requested raster aspect. All its image/UI/background layers use that same aspect, independent of LOD rounding. It suits camera patches. Angular maps a whole panorama; geodesic maps a bounded patch and rejects an antipodal sphere or a cylinder wrapping more than one turn. `screen-quality` sets chord error in meters; smaller values tessellate more and may exceed work/geometry budgets. Curved meshes cache locally and unchanged failures wait before retrying.

`screen-renderer` chooses `vector` or `raster`; optional width/height/sample count set the requested maximum. `screen-resolution` changes width/height alone. Requests are 16–4,096 per side, with aspect at most 64:1. UI bitmaps are capped at 2,048 per side and 1,048,576 pixels, then downscaled based on projected texel density, viewport resolution and distance. An older client DLL's smaller `caps` are negotiated. CPU composition retains its eight-million-sample work ceiling and uses row-clipped triangle spans; a four-sample maximum frame needs at most 64 MiB of temporary sample storage plus RGBA output. GPU UI targets retain a total 16 MiB resident budget, so four one-megapixel targets can fill it. LCD relay copies keep their earlier 1,024-side / 262,144-pixel cap. Quantized tiers have 20% hysteresis. Plane cover/contain UVs and perspective magnification are included. Curved visibility uses a proxy plus conservative analytical bounds. Off-frustum screens skip composition, copying and drawing. This is frustum culling, not a GPU occlusion query against walls.

UI raster composition is limited to one ready screen per draw tick; source calls are limited to four. Oldest-attempt scheduling prevents a high-rate screen from monopolizing service. `screen-refresh` (default 6, range 1–60) and `/hdr lcd-rate` are ceilings; local quotas can lower achieved rates. Moving the anchor keeps presentation aligned each render frame without recapturing unchanged content.

Layers, per-object opacity, SVG fills/strokes, text and animations compose into the UI bitmap. Layer visibility/opacity changes drop a stale bitmap immediately while it is rebuilt. **Per-object emission and packaged textured image layers currently require vector mode.** Choosing raster for a screen containing such packaged image layers reports a local error and falls back to vectors. Cameras/native source textures are separate and still work with either UI mode. Raster mode needs the optional client DLL; otherwise the screen's ordinary content uses vector fallback. That fallback produces a bounded client chat/log notice. Run `/hdr status` while the screen is visible: `Raster backend connected` plus a positive UI texture count confirms an uploaded bitmap; the demo's requested mode alone does not. Plugin activation transitions and the first failing gate are recorded with `[HDRClientRenderer]` in the game log.

Screens remain one-sided by default. `screen-two-sided` optionally enables reverse viewing, with independent front/back opacity multipliers. On curved screens, front means the selected inside/outside side. The same artwork and texture are shared; reverse lettering is mirrored. Zero opacity suppresses that side's rendering and source work. Foreground layer offsets reverse with the viewer's side. `screen-side-opacity` changes the two multipliers; `screen-side-settings` returns the flag and both values. The Raster Surface demo accepts `two-sided`, `one-sided`, and `sides 1 0.35`.

## Video and a spherical display

The v9 camera's default coverage is a **180-degree fisheye hemisphere**, not longitude 360 degrees by latitude 180 degrees. Azimuth turns 360 degrees around the physical optical axis; polar angle is 0–90 degrees from forward, giving a 180-degree opening across the dome. Five cube targets contribute, with only the front halves of side targets displayed. The rear target is skipped unless `sphere` is selected. Existing six HDR Video buffers are accepted and the rear buffer is unused in hemisphere mode. Side targets remain ordinary rectangular captures internally; their rear pixels are removed by HDR's source crop.


HDR 0.7.4 welds shared panorama vertices and applies the configured client scene budgets to the joined output. The live 0.7.3 join expanded indexed source geometry into triangle soup and hit the 2,048-point cap on every face, leaving only the grey backdrop. Regression checks now cover all six faces at the demo's actual 90°/.01m detail setting.

PB-owned displays are retained between calls, then retired when the PB powers off or its ProgramData changes. The server checks active owners every 30 ticks; a new program's first API write also retires its old generation immediately. Cleanup removes only that PB's screens, objects, labels, layers, animation, UI and tracking declarations, and releases its local source textures. Other PBs sharing the anchor keep their content. No per-frame clear/rebuild is needed. The v7 camera script uses `screen-exists` to republish after a power cycle; other one-shot scripts should be run again after powering up.

**Use `artifacts/SphericalCamera_v9.pb.cs` with HDR 0.7.5, HDR Client Renderer 0.7.8 and 360 Camera Capture 0.1.2.** The demo defaults to a camera-facing hemisphere. Its plane passes through the projection centre and faces along the reference camera's physical forward direction, transformed into the anchor frame. Normal arrays prefer the camera named `HDR Camera 1`; 360 mode uses the single 360 Camera. Run `sphere` to remove the crop or `hemisphere` to restore it. The video, overlays and grey backdrop share the crop; the missing half remains open. The capture addon verifies completed face-specific direction and FOV, so a missing or stopped addon cannot silently relay the same native camera image in six directions. Watch the game log for `[Camera360]` startup and verified face directions. The native crash from the latest session is not diagnosed by the offline checks.

**Camera binding and joining:** It automatically matches each `HDR Video [N]` LCD to the actual camera named in its Custom Data, including camera roll. A numeric LCD suffix does not select camera orientation. Relative direction changes are polled by the PB; unchanged poses produce no writes. The demo defaults to 512px maximum, bounded by the LCD's real texture size and each viewing client's LOD/relay limits. It presents a three-meter camera-facing hemisphere with guides off and grey unseen directions. Run `gaps transparent`, `gaps grey`, `guides`, `video`, `join`, or `patches` to change that presentation.

`H("screen-join", "cameras")` puts an external-image pinhole sphere in a panorama group. Screens from the same PB/group/centre/radius share angular ownership. A competing camera cuts pixels away only where its viewing cone actually covers them and its forward ray is closer to that direction; deterministic ID ordering resolves identical forward rays. The join rebuilds on group membership, camera pose, FOV or aspect changes, remains cached otherwise, and respects the existing geometry/work budgets. Images stay on the same spherical radius; no overlapping image planes or stretching into unseen directions are introduced. `H("screen-join")` disables joining. This directional stitching has no depth estimate, so physically separated normal cameras retain near-field parallax.

The **separate 360 Camera mod** supplies large/small-grid blocks and a client capture extension for CameraLCD. Run the v9 demo with **360**, with exactly six source LCDs and one such camera on the construct. It writes six `Camera360.Face` selectors into the LCDs' Custom Data and uses 90-degree cube views from the camera's one optical centre. Those six views cover the full 360°×180° sphere. Run `array` to stop auto-binding the LCDs to that camera. The capture extension lives outside HDR; HDR Client Renderer 0.7.8 copies the images after the separate addon verifies each capture. The local `source-demand-any` hint lets a cooperating producer know that a hidden physical buffer has a visible display consumer.

The earlier demo notes below describe v3–v5 setup and fixes. Native camera copies are confirmed working in-game with HDR Client Renderer 0.7.6. The v9 hemisphere and verified 360 capture path still need visual acceptance.

```csharp
H("screen-source", "lcd-texture", sourceLcd.EntityId.ToString()+":0");
H("screen-resolution", 4096,2160); // request ceiling; no 4K buffer is allocated
H("screen-refresh", 30);
```

The source block must be live, powered, on the PB's construct and accessible to its owner. `:0` selects its first text surface. The client provider checks the exact source/surface/texture identity and releases independent screen leases on changes. Its own copies use HDR's effective resolution within the unchanged relay quota. Native LCD images are copied over opaque black; CameraLCD's zero-alpha camera RGB must not become a transparent hologram. Screen and side opacity remain available. HDR's `source-demand` hint reports the aggregate bounded visible resolution/rate so a cooperating producer can reduce or stop upstream captures. The external CameraLCD plugin continues following its own capture policy until integrated with that hint.

Paste `artifacts/RasterSurfaceDemo.pb.cs` for a Console/Projector named **HDR Display**. Run `plane`, `cylinder`, `sphere`, `raster`, `vector`, `rate 30`, `toggle`, or `clear`. An optional LCD named **HDR Video** supplies the source. Stand within three meters of the curved centre 1.5 meters above the anchor for its inside view.

Paste `artifacts/SphericalCameraDemo.pb.cs` for up to six LCDs **HDR Video 1** through **6**. **HDR Video** without a number is also accepted for the first patch. Keep the Console/Projector anchor named **HDR Display**; LCDs named **HDR Display 2**, etc. are not source feeds. The demo uses HDR's Console/Projector-only lookup, so an LCD sharing **HDR Display** cannot become the sphere anchor.

For [CameraLCD Remastered](https://github.com/StarCpt/SE-CameraLCD-Remastered/blob/master/SE-CameraLCD-Remastered/CameraTSS.cs), enable the producer plugin on the viewing client. Use powered square source LCDs on the PB's construct. For each LCD select **Content → Script → Camera Display** and put the corresponding exact camera name, e.g. `HDR Camera 1`, in that LCD's Custom Data. (`0:HDR Camera 1` also selects its first surface.) Confirm the physical LCD displays video first. Name the corresponding camera blocks **HDR Camera 1** through **6**, facing the directions to show. Missing camera blocks use six orthogonal fallback orientations.

The demo defaults to a 60-degree square source image, matching fully zoomed-out standard cameras' approximately 1.047-radian maximum FOV. `fov 60` adjusts the mapping; set a different value only if the producer really captures that FOV. Six 60-degree feeds leave uncovered gaps; changing the mapping to 90 degrees does not supply missing image data. HDR maps those perspective patches onto the inside of a three-meter sphere, centred 1.5 meters above the anchor. `size 4096` changes the requested ceiling, `rate 30` changes requested refresh, and `clear` removes patches. Re-run after changing camera directions. Uncovered directions remain transparent. CameraLCD's own distance/frustum checks can pause physical LCD feeds outside the player's view; HDR's demand hints do not currently override that producer policy.

HDR Client Renderer 0.7.2 adds a client-only CameraLCD compatibility patch that filters HDR's transient billboard materials before camera-pass gathering. The main player view keeps all billboards. This also prevents cameras from filming the HDR output itself. The patch validates the installed renderer's two list-read sites, installs only while the CameraLCD type is present, and removes only its own patch on disposal. It does not suppress renderer exceptions or remove other mods' billboards. The October 4 crash log showed a `NullReferenceException` in camera-pass billboard gathering (`IsGPU: False`); isolation remains an in-game-verification step, not proof of the original null object's identity.

The subsequent live client log confirmed camera-pass isolation installed and active after the DLL was copied manually. Client renderer 0.7.3 separately fixes blank video copies: the GPU relay target now has the render-target view required by the offscreen sprite pass. The v3 Spherical Camera demo adds cyan patch outlines and camera numbers by default, so missing video is distinguishable from missing geometry. Run `video` to hide those guides or `guides` to restore them.

Client renderer 0.7.4 uses a direct render-thread RGB copy in place of the vanilla sprite relay after the latter produced black camera images despite valid sources and writable targets. The target remains opaque; presentation opacity remains configurable. The server still handles only display declarations. Spherical Camera v4 combines its frame and camera label into one SVG object per screen and clears old guide objects before rebuilding. This lowers six-patch setup from eighteen to twelve anchor objects and avoids its own sixteen-object-limit failure. Other demos sharing the anchor still consume their own object slots.

Client renderer 0.7.5 allocates the direct-copy destinations eagerly from initialized RGBA data. The v5 camera script explicitly removes legacy `guide-name` items before clearing and rebuilding the combined guides. Use the versioned `artifacts/SphericalCamera_v5.pb.cs` file and replace the complete PB source; its output begins `Spherical Camera v5`. A live PB with the v4 status text still contained the old `guide-name` drawing statement, filling sixteen slots and rendering two labels.

Client renderer 0.7.6 binds the fullscreen vertex buffer when copying camera RGB. The 0.7.5 log confirmed copy command execution, but the renderer's `updateVertexBuffer=false` argument suppressed binding immediately after `ClearState`, leaving the destination black. The new engine-call regression test rejects that argument and checks the viewport dimensions. This update needs a client restart; the normal mod and v5 PB script are unchanged. Pixel output remains pending live verification.

Known camera poses/viewing cones determine ray directions and align far-field imagery. They do not determine scene distance along each ray. Nearby objects therefore retain parallax at joins when cameras have different origins. Stereo matching from overlapping images could estimate depth, but reconstruction belongs in CamScan, not this display API.

## Build and validation

Build the normal mod with `Build.ps1`; it produces `artifacts/HDR-API-0.7.3.zip`. Build the optional DLL with `OptionalPlugins/HDRClientRenderer/Build.ps1 -Runtime Legacy` for .NET Framework, or `-Runtime Interim` for .NET 10. It is a separate `IPlugin` for a matching client loader such as [Pulsar](https://github.com/SpaceGT/Pulsar). Use the loader's local-plugin installation workflow, then fully restart the loader/game. Do not put the DLL inside the Workshop mod or on the server. No third-party producer/loader binaries are bundled or installed by the build.

All HDR peers need **version 0.7.3 / scene protocol 12**; clients without the optional plugin still use ordinary vectors. Offline checks cover clipping/LOD, bounded allocations, surface tessellation, RGBA composition, source proofs, resource retirement and demo compilation. Pixel colors/UVs, actual GPU copy, CameraLCD compatibility, device recreation and real multiplayer delivery require [in-game acceptance](MultiplayerValidation.md).
