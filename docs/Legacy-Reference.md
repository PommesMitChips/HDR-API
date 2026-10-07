# Legacy reference and migration notes

Preserved working notes from earlier repository layouts. The [current wiki](wiki/README.md) is the maintained API reference; historical release limits and defaults are in [CHANGELOG.md](../CHANGELOG.md).

# Holographic Display Rendering API (HDR API)

HDR API **0.9.6** and Client Renderer **0.9.12** provide portal capture before primary drawing and direct projective image sampling for paired planes. Cropped captures and stable texture regions preserve detail in the visible part of the pane. The portal demo defaults to `size native` and 60 Hz requested. The native capture ceiling remains 2048 per axis; local budgets can reduce detail. Curved portals retain the composed-image path. Protocol 17 remains compatible.

A separate Space Engineers 1 C# mod project, version 0.9.6 / protocol 17. Everything here is independent of the workspace's `Scripts` directory. It adds continuous-coordinate geometry anchored to Console or Projector Blocks (`IMyProjector`), including the vanilla Console (`LargeBlockConsole`). It can run alongside Projector+ 3.0; it neither copies its source nor depends on its API. The anchor's native blueprint hologram and HDR geometry can coexist.

## Start here

| Task | Guide |
| --- | --- |
| Build and generate PB demos | [Build and checks](#build-and-checks) |
| Use the recommended drawing interface | [Short drawing API](#short-drawing-api) |
| Direct camera panoramas, raster UI and native portals | [Client renderer](../OptionalPlugins/HDRClientRenderer/README.md) |
| General ellipsoids and authored surfaces | [General surfaces](../docs/GeneralSurfaces.md) |
| Configure display sources | [Display sources](../DisplaySources.md) |
| Review earlier behavior and fixes | [Release history](../CHANGELOG.md) |

For the current portal demo, use [PortalDemo.pb.cs](../Examples/PortalDemo.cs), name one Console/Projector **HDR Portal**, and start with `plane`. It requests native detail and 60 Hz; `rate 120` requests a higher cadence. Use [DirectCameraSphere_v14.pb.cs](../Examples/DirectCameraSphereDemo.cs) for direct camera panoramas and [GeneralSurfaceDemo.pb.cs](../Examples/GeneralSurfaceDemo.cs) for authored surfaces. Build-generated scripts live in `artifacts/`; their maintained sources live in `Examples/`.

Install or inspect client plugins through [InstallClientPlugins.ps1](../Tools/InstallClientPlugins.ps1). Close Space Engineers/Pulsar before installation; the installer backs up existing files and verifies profile hashes. Local rate/pixel/pass settings are documented in the client guide.

## Client rendering and replication

The server retains bounded display definitions and checks block ownership/access. Each viewer prepares SVGs, polygons, contour fills, fixed text meshes, clipping and gradients locally, with a fair compilation queue and a shared operation budget. Dedicated servers do not prepare or render these meshes. Live grid/model previews are also viewer-local.

Short-command animations send a timeline once and play locally between packets. State changes still publish at most twice per second; ordinary playback does not publish a frame update. Incremental publications send changed items and removal records; transform-only updates omit geometry. A versioned full baseline handles joining and recovery. All peers must use this build because the wire protocol is version 17.

Enter `/hdr off`, `/hdr on`, or `/hdr status` in chat to control your own rendering. The commands stay local. Preparation is asynchronous: unsupported or overly expensive artwork may be rejected locally while the last valid mesh remains displayed. Clients can reduce work this way without changing the shared display.

See [Security.md](../Security.md) for the trust and privacy boundaries, including the limits of PB caller identification. Follow [MultiplayerValidation.md](../MultiplayerValidation.md) before using this build on a public server. Offline compilation and regression checks do not certify real multiplayer delivery or GPU output.

## Geometry and retained scenes

- Wires defined by floating-point 3D points and indexed connections; duplicate undirected connections are drawn once.
- Filled planar polygons, including concave faces, disconnected faces, and faces that share vertices. Ear clipping triangulates faces. Outlines follow the original face boundaries, not generated triangulation diagonals. Shared boundary edges are drawn once.
- Parametric curves evaluated by the PB wrapper, sampled into continuous-coordinate line segments. Closed curves exclude the duplicated final endpoint. Functions stay in the PB; only geometry is uploaded.
- Camera-facing vector labels, rendered separately for each viewer with packaged proportional glyphs. Height stays constant in display meters when the map zooms; the anchor follows the map transform.
- RGBA outline and fill colors, display-space line thickness, optional two-sided flat face shading.
- Retained object IDs, updates, visibility, removal, PB-scoped clearing, object transforms, and shared Console view transforms. Updating a transform does not rebuild geometry or clear the native projector.
- Named layers for geometry, labels, and the live ship, with independent visibility and opacity. Hiding a layer retains its data; later updates keep their layer assignment.
- Server-authoritative scene definitions with versioned, bounded incremental replication. Transform/appearance updates reuse geometry; full baselines support late joining and recovery. Mod-managed animation timelines play locally on each viewer.
- Geometry budgets, ownership/access checks, cleanup when PBs/Consoles close or detach, and drawing only near powered Consoles.
- A live construct layer using native render-only grid previews, with the actual armor shapes and block models. Main grid and mechanical subgrids share the display transform with drawn geometry. Poses are submitted alongside drawn geometry in `Draw()`; cache maintenance and attached-grid discovery run during simulation. Fitted center/scale remain stable during articulation and only refit when grids/blocks change. Block additions/removals rebuild a cached preview, rate limited to once per 300 updates per grid; callbacks from other grids are ignored.
- Lines and polygon faces attach to the Console's render object using Console-local coordinates, so they follow its render-side motion. World-space drawing is a fallback when no Console render object exists.

## Folder layout

| Folder | Purpose |
| --- | --- |
| `Mod/` | Local mod package: definitions and runtime source |
| `Api/` | PB wrapper, supplied with this mod |
| `Examples/` | Mod-specific PB demonstration, kept here rather than in pure scripts |
| `Tools/Checks/` | Geometry, serialization, and installed-game compilation checks |
| `artifacts/` | Generated paste-ready PB demo; ignored build output |

## Build and checks

Requires the installed Space Engineers game and .NET 10 SDK for the check harness. The mod itself is distributed as source and compiled by the game; it does not require .NET 10 on a player's machine.

From this folder:

```powershell
.\Build.ps1
# For another Steam library:
.\Build.ps1 -GameBin 'D:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64'
```

The checks compile the mod and demo using C# 6 and your installed game assemblies, apply the installed PB type-safety/memory-safe rewrite, exercise polygon triangulation and curve sampling, round-trip the network DTOs, check packet size, and verify atomic client snapshot replacement.

Compilation is not an in-game sandbox-load test. The checks additionally reject the two sandbox violations observed in the game log: `ReadOnlyDictionary` and concrete `MyModel.AssetName` access (the public `IMyModel.AssetName` interface is used instead). These targeted regressions do not implement the full runtime mod whitelist. Material appearance, triangle transparency ordering, and multiplayer delivery still require testing in a running world. No automated result currently certifies visual correctness or dedicated-server behavior.

## Try the demo in a test world

1. Back up the existing local HDR mod, then replace `%APPDATA%\SpaceEngineers\Mods\HDR API` with the **contents** of `Mod/`. The destination should contain `metadata.mod` and `Data/` directly. Upgrading from 0.5.x requires removing the old camera source files; replacing the folder ensures those removed files disappear.
2. Enable the local HDR API mod in the world's mod list. Enable in-game scripts. Enable Projector+ separately if you also want its block projections; HDR works without it.
3. Place and power a Console or Projector Block and a Programmable Block on the same mechanical construct. The PB's owner must have access to the display anchor.
4. Name the Console `Holo Map`.
5. Run `Build.ps1`, paste `artifacts/ConsoleDemo.pb.cs` into the PB, and run with argument `demo`.
6. Expect the current PB's grid and mechanical subgrids, a range circle, sine graph, three wire axes, and a synthetic shaded asteroid-like mesh to the side. `spin` animates the synthetic mesh transform; `stop` stops that rotation while live tracking continues; `clear` removes this PB's objects and its live layer.

The demo mesh is synthetic. Scanning and surface reconstruction belong to the separate CamScan API project. No game/workshop files are modified by building this project.

## PB API

Include `Api/HoloMapApi.cs` inside the PB's `Program` class, like the generated demo. Call `api.Activate(Me)` after loading; retry if it returns false. The mod exposes a read-only delegate dictionary through the PB terminal property `HDR.Api`.

```csharp
var api = new HoloMapApi();
if (!api.Activate(Me)) return;
var console = GridTerminalSystem.GetBlockWithName("Holo Map");
var cyan = new Vector4(0.1f, 0.9f, 1f, 1f);

api.PutLine(console, "heading", Vector3D.Zero, new Vector3D(1, 0, 0), cyan);

api.PutCurve(console, "circle",
    t => new Vector3D(Math.Cos(t), 0, Math.Sin(t)),
    0, 2 * Math.PI, cyan, segments: 128, closed: true);

api.PutPolygons(console, "surface",
    new[] { new Vector3D(-0.5,0,-0.5), new Vector3D(0.5,0,-0.5),
            new Vector3D(0.5,0,0.5), new Vector3D(-0.5,0,0.5) },
    new[] { new[] { 0, 1, 2, 3 } },
    cyan, new Vector4(0, 0.7f, 1, 0.3f), shaded: true);
```

| Method | Purpose |
| --- | --- |
| `PutLine(console, id, from, to, rgba, thickness)` | One straight segment |
| `PutWires(console, id, points, connections, rgba, thickness)` | Indexed segments (`Vector2I` endpoint pairs) |
| `PutPolygons(console, id, points, faces, outlineRgba, fillRgba, thickness, shaded)` | Ordered index arrays, one array per face |
| `PutCurve(console, id, function, start, end, rgba, segments, thickness, closed)` | Fixed-resolution parametric curve |
| `SetView(console, offset, rotationRadians, scale)` | Shared display view; configure scale before uploading large model coordinates |
| `SetTransform(console, id, affineMatrix)` | Transform one retained object |
| `SetVisible(console, id, visible)` | Hide/show one object |
| `Remove(console, id)` | Remove one object owned by this PB |
| `Clear(console)` | Remove all objects owned by this PB on this Console |
| `TrackConstruct(console, enabled, displayRadius, offset)` | Display the PB's current grid plus mechanical subgrids, normalized to a radius in map units (default 0.55) |
| `TrackConstructWorld(console, metresPerUnit, rootLocalOrigin)` | Use a fixed real-distance scale and root-grid origin shared with terrain geometry |
| `PutLabel(console, id, position, text, rgba, height)` | Retained, camera-facing text at a model-space anchor; default character height 0.035m |
| `SetObjectLayer(console, id, layer)` | Group this PB's geometry/label into a named layer |
| `SetConstructLayer(console, layer)` | Group the live construct controlled by this PB |
| `SetLayerVisible(console, layer, visible)` | Hide/show a layer without removing data |
| `SetLayerOpacity(console, layer, opacity)` | Multiply layer opacity by a value in [0,1] |
| `GetLayerState(console, layer)` | Read `MyTuple<bool,float>` visibility and opacity |

Methods throw descriptive errors in the wrapper when the mod rejects input. Replacement is atomic: invalid geometry leaves the previous object visible. IDs are scoped to a PB, so different PBs can use the same ID. An object replacement preserves its transform and visibility. A PB cannot remove another PB's objects. All authorized PBs share the Console's view transform.

### Coordinates and appearance

- Points use model-space `Vector3D` coordinates. At scale 1, one model unit is one meter. Local axes follow the Console's `WorldMatrix`: X is right, Y is up, Z is backward. Default anchor is `(0, 0.8, 0)` relative to the block center; this needs visual calibration against the model.
- Transformation order: object transform, map scale, map rotation, map offset, Console world matrix. Rotations are radians: X pitch, Y yaw, Z roll. Line thickness is in display meters and stays fixed while zooming.
- Set alpha to zero to disable outlines or fills. Style applies per object for the original geometry API; SVGs carry per-triangle colors internally.
- Shading is an explicit ambient plus directional brightness calculation per triangle, using a light direction anchored to the Console. It is not a custom shader, smooth vertex lighting, or shadow casting.
- `PutPolygons` faces may be convex or concave but must be planar, simple, and without holes. Use `PutContours` for filled coplanar contours with holes and islands. Do not repeat closing indices/points. Send an irregular/nonplanar asteroid surface as triangles. Functions with discontinuities must be split into separate curves; there is no implicit discontinuity detection or adaptive sampling yet.

### Prototype limits

- 2,048 points per object; 4,096 connections or triangles; at most 128 vertices per polygon.
- 16 objects per anchor; default 4,096 total points / 8,192 combined edges and triangles, configurable with `H("budget",points,primitives,drawWork)` within fixed ceilings. 8 active anchors per world; per-object and replication limits remain in force.
- Points must be finite and within 1,000,000 model units of the origin. Map scale is `0.000001–1000`; offset stays within 10 meters of the block center; transformed geometry fits a 25 meter radius about the display anchor.
- Lines are `0.0001–0.25` display meters thick. Rendering stops beyond 60 meters from a Console; a global 20,000 primitive draw budget can omit later objects when exceeded.
- Scene replication is at most once per 30 simulation updates, uses packets below 4 KiB, and sends at most eight chunks per update. Snapshots contain the entire public scene, so large or frequently animated scenes can consume substantial bandwidth. Client recovery polls every 600 simulation updates.
- Scenes are **public shared displays**: all connected clients receive geometry. Per-player/private content is not implemented. Scenes and draft geometry are not saved; PBs must recreate them after world load or mod reload.
- The live layer has no fixed subgrid-count limit and supports 10,000 blocks in total. It excludes connector-docked ships and is controlled by one PB per Console. Each client builds previews from its locally replicated source grids; unavailable distant source grids cannot be displayed. Grid previews have no physics, are not saved or synchronized, and have mechanical/docking connection IDs and nested projector payloads removed. Functional blocks are disabled. Armor uses the native grid preview; fat-block models use independent plain `MyEntity` render entities, avoiding `MyRenderComponentCubeBlock` parenting to grid culling cells. Native fat-block copies are hidden. Each independent model root owns miniature scale; nested subparts inherit it once and follow live local poses. Independent models have no functional-block logic and are closed with the grid cache. Geometry refresh follows block addition/removal; repainting, deformation, skins, and changes to the subpart hierarchy are not continuously mirrored.
- Translucent intersecting surfaces may show sorting artifacts. Visual appearance and depth behavior must be confirmed in game. The obsolete-warning on `AddTriangleBillboard` is the game's "Only for modders" marker; the call is deliberately used by this mod.

## Short drawing API

**Recommended interface:** the mod exposes `HDR.Draw`, a PB-compatible `Func<string,object[],object>`. Binding, defaults, shapes, colors, clipping helpers, layers and animation state are handled by the mod. A PB only contains this forwarding function:

```csharp
Func<string,object[],object> _draw;
object H(string command, params object[] args)
{
    if (_draw == null)
        _draw = Me.GetProperty("HDR.Draw")
            .As<Func<string,object[],object>>().GetValue(Me);
    return _draw(command, args);
}
```

PBs cannot name custom classes defined in a mod assembly. The one built-in delegate therefore carries commands to the mod; no `HoloMapApi` class or per-operation `Func` declarations need to be pasted. Complete demos include missing-mod checks and normal exception reporting. Commands are case-insensitive, use a closed whitelist, and validate argument types/counts before dispatch. They do not use reflection, dynamic invocation or executable command strings. Existing registered-entity, same-construct and ownership/access checks run on each drawing call, before a curve callback can execute.

```csharp
H("target", "Holo Map");             // nearest same-name Console/Projector
H("view", 0, 0.85, 0);
H("line", "beam", 0, 0, 0, 1, 0, 0, "cyan");
H("circle", "ring", 0, 0.3, 0, 0.25, "orange");
H("text", "title", "HELLO", 0, 0.8, 0);
H("transparency", "ring", 0.25);
H("emission", "beam", 2);
H("animate", "ring", "rotation", 0, Math.PI * 2, 3, true);
```

**Reload the save after installing this build.** Paste **`artifacts/DrawDemo.pb.cs`** for a complete 1,114-character example, including animated geometry. No additional API class is appended. `artifacts/AppearanceDemo.Draw.pb.cs` reproduces the appearance demo with this interface at roughly 3.1k characters. `SvgDemo.Draw.pb.cs`, `ConsoleDemo.Draw.pb.cs`, `SvgFramesDemo.Draw.pb.cs` migrate the earlier demos. These generated migration scripts retain familiar data/geometry calls as needed, while the manually authored DrawDemo shows the short scalar forms. Exact sizes are in `artifacts/DrawApiSizes.json`.

| Command | Arguments after the command name |
| --- | --- |
| `target` | block reference or name; sets this PB's current anchor |
| `FindTarget` / `FindTargets` | name; return nearest block or `List<IMyProjector>` of same-construct matches |
| `line` | id, from XYZ, to XYZ, optional color/width |
| `circle` | id, center XYZ, radius, optional color/segments/width/normal |
| `curve` | id, `Func<double,Vector3D>`, start, end, optional color/segments/width/closed |
| `wires` | id, points, endpoint-index pairs, optional color/width |
| `polygons` | id, points, face arrays, outline/fill colors, optional width/shaded |
| `contours` | id, contour point arrays, outline/fill colors, optional width/shaded/evenOdd |
| `points` / `vertices` | id, positions, optional color/radius/width |
| `svg` / `svg-asset` | id, SVG text/asset name, optional position, scale and curve segments; a placement matrix may replace position/scale |
| `image` | id, asset, width/height or Vector2 size, optional tint |
| `text` / `label` | id, string, optional position, height and color; text also accepts alignment |
| `view` | offset XYZ, optional scale then pitch/yaw/roll; also accepts the earlier offset Vector3D, rotation Vector3D, scale |
| `move` | id, new XYZ; preserves current scale/orientation |
| `pose` | id, XYZ, optional scale/pitch/yaw/roll; without id returns a MatrixD utility value |
| `transform` | id, MatrixD |
| `opacity` / `transparency` / `emission` | id, value |
| `clip` | id, clip-plane array or null to clear |
| `clip-box` | id, min/max XYZ (or Vector3D corners) |
| `clip-circle` | id, center XY, radius, optional sides |
| `gradient` | id, start/end XYZ, start/end colors, optional resolution/radial |
| `cleargradient` | id |
| `live` | optional radius and offset; enables current construct preview |
| `track-world` | metres per model unit, root-local origin |
| `layer` | object id, layer name |
| `layer-visible` / `layer-opacity` | layer name, value |
| `layer-state` | layer name; returns `VRage.MyTuple<bool,float>` |
| `toggle` / `solo` | layer name; solo also takes the known layer-name array |
| `construct-layer` | layer name |
| `animate` | id, channel, from, to, seconds, optional loop/ping-pong |
| `play` | id, packed animation text, optional position/scale/curve segments; the mod owns the playback clock |
| `playsvgframes` | id, SVG string array, fps, position, optional scale/loop/curve segments |
| `pause` / `resume` | no arguments; pause/resume this PB's mod-managed animation across its targets |
| `stopanimation` | id; freeze/remove its mod-managed tracks |
| `remove` / `clear` / `visible` | id, none, or id + bool respectively |
| `rgb` / `rgba` | utility color channels; RGB is 0–255, RGBA 0–1, optional alpha |
| `version` | returns endpoint version string |

XYZ positions can be supplied as three numbers or one Vector3D. Colors can be Vector4, basic SVG names, `orange`, or `#RGB`/`#RRGGBB`; default drawing color is cyan, text/image tint defaults to white. Operations return `true` on success except typed utility/query results; invalid calls throw descriptive standard exceptions which the PB may catch. Existing long method names remain aliases for migration (e.g. `PutLine`, `SetGradient`). Optional arguments are positional, so provide intervening defaults to set later options. Clipping/gradient units and all existing object/scene/packet budgets still apply. The selected target is scoped to the caller's drawing context; the backend revalidates access, and a PB cannot use names/IDs to bypass construct/ownership rules.

**Animation no longer needs PB animation classes or an Update1/Update10 clock.** The server publishes bounded animation definitions and timing; each viewer evaluates tracks and SVG frames locally. Ordinary playback does not dirty shared scenes or resend geometry every frame. Pause/resume and stop replicate as state changes, with stop retaining the current pose/frame. `UpdateAnimations(elapsed)` remains a compatibility command and does not advance the clock twice. Animations and contexts are bounded to 128 entries each and discarded on unload. Ownership/access and retained objects are rechecked on the server. Completed tracks retain their final values.

**Custom Data players:** paste **`PackedPelicanDraw.pb.cs`** (939 characters) for one display or **`MultiConsoleAnimationDraw.pb.cs`** (about 2.9k) for several same-name displays. Both read animation from each Console/Projector's Custom Data. The multi-display PB polls for payload/name changes with Update100, while the mod independently drives animation. Empty payloads remove that player's scene; malformed replacement data reports an error and preserves the existing valid animation. `stop` pauses all caller animation, `start` resumes, `demo`/`reload` restart payloads, `clear` removes the caller's groups, and `status` reports discovery/errors. The entry points are `HDR.Draw` and `HDR.Api`; replace earlier PB scripts with the regenerated versions.

Offline tests exercise the real command endpoint with gateway doubles: scalar lines/circles/text, colors, appearance controls, layer queries, mod-side animation/pause, wrong/extra arguments, unknown operations, unauthorized curve callbacks, client execution and unregistered callers. The mod and 18 typed/command PB scripts compile against installed assemblies and the PB scripts pass memory-safe rewriting. Actual game whitelist loading, rendering and multiplayer are still runtime gates.

### Legacy wrapper size audit and authoring helpers

The full wrapper previously took **32,827 characters**. Its long delegate signatures were repeated in field declarations and activation calls. Binding now infers the type from the field, e.g. `Bind(ref _wires, methods, "PutWires")`, so the signature is written once. The API uses the standard PB imports (`System`, `System.Collections.Generic`, `Sandbox.ModAPI.Ingame`, `Sandbox.ModAPI.Interfaces`, `VRageMath`) and retains strongly typed `Func` calls. No dynamic invocation or new mod protocol is required. A differently named custom delegate type would not automatically cast to the mod's `Func` delegate even if its parameter list matched.

The full authoring API, including the new conveniences, is **28,027 characters**. The largest saving is **build-time API profiles**: Build determines which API methods an example references and includes those methods, fields and dependencies in its paste-ready script. Animation bookkeeping is omitted from profiles that do not use the animation helper. The generated code remains readable typed C#, with original method names and error checks. It is source selection, not opaque compressed runtime code.

| Script | Earlier full-wrapper PB, approximately | Current generated PB, characters |
| --- | ---: | ---: |
| Console demo | 36,300 | 11,265 |
| Appearance demo | 35,800 | 14,375 |
| SVG demo | 35,800 | 17,734 |
| SVG frame demo | 35,400 | 13,528 |
| Single packed-animation player | 3,077 | 3,077 |
| Multi-display packed-animation player | 6,156 | 6,156 |

Exact current counts are regenerated in **`artifacts/ApiSizeAudit.json`**. The packed players already use only three delegates, so they retain their small direct bindings. Each other example gets `<name>.api.cs` alongside `<name>.pb.cs`. Edit the example in `Examples`, then run Build to regenerate its profile after adding API calls. If you edit the generated PB by hand and add a method omitted from that profile, use the full authoring class or regenerate; a profile cannot expose omitted methods automatically. Direct use of the full wrapper remains supported.

Repeated patterns identified in the demos:

| Pattern | Helper / improvement |
| --- | --- |
| Repeating complete `Func<...>` types while binding | Inferred `Bind<T>(ref slot, methods, name)` |
| Including all drawing and animation methods in every PB | Generated per-example API dependency profile |
| Looking up a block by name, then repeating type/construct checks | `FindTarget(terminal, name)` / `FindTargets(terminal, name)` |
| Scale × rotation × translation matrix construction | `HoloMapApi.Pose(...)` |
| Converting byte RGB channels or validating RGBA inputs | `HoloMapApi.Rgb(...)` / `Rgba(...)` |
| Repeated circle trigonometry and closed-curve arguments | `PutCircle(...)` |
| Layer state read + invert + write, and solo loops | `ToggleLayer(...)` / `SoloLayer(...)` |
| Camera/terrain sampling, grid selection and scan scheduling | Moved to the independent CamScan API project |
| `Motion` / `AnimatedObject` bookkeeping and frame playback | Largest remaining wrapper component in animation profiles; moving playback into the mod would also support smoother remote animation |

```csharp
// After holo.Activate(Me): filters to Console/Projector blocks on Me's construct.
var display = holo.FindTarget(GridTerminalSystem, "Holo Map");
var displays = holo.FindTargets(GridTerminalSystem, "Holo Map");

// Position, uniform scale and radians; scalar overload uses pitch/yaw/roll.
holo.SetTransform(display, "ship", HoloMapApi.Pose(0.5, 0.2, 0, scale: 0.4, yaw: angle));
var cyan = HoloMapApi.Rgb(0, 220, 255, 0.8f);
holo.PutCircle(display, "ring", Vector3D.Zero, 0.5, cyan);
bool visible = holo.ToggleLayer(display, "terrain");
holo.SoloLayer(display, "terrain", new[] { "ship", "terrain", "points" });
```

`FindTarget` chooses the nearest matching projector to the PB, breaking equal-distance ties by entity ID. It ignores unrelated blocks sharing the name; single-display demos now use it. `FindTargets` returns all same-construct matching projectors. Mod ownership/access validation still applies. `Pose` preserves the existing scale → yaw/pitch/roll → translation order. `Rgb` accepts byte channels 0–255 and float alpha 0–1; `Rgba` accepts float channels 0–1. `PutCircle` defaults to the XY plane, with an optional normal to select another plane. `ToggleLayer` returns the resulting visibility; `SoloLayer` acts only on its supplied list.

Conveniences reduce authoring repetition but add their implementation when used. A rarely used shape helper can increase a small PB's total size; the profiles keep unused helpers out. The size audit therefore measures complete paste-ready scripts, not just how short one call looks. Existing direct matrix/geometry APIs remain available. All seven generated PBs compile against installed game assemblies and pass the game's memory-safe rewriting check; no mod reinstall or save reload is needed for these wrapper changes. In-game checks are still needed for the new target-selection/authoring helpers.

### SVGs, images and animation

Reload the world after installing this build, name a vanilla Console Block **Holo Map**, and paste `artifacts/SvgDemo.pb.cs` into an owned PB on its construct. Run `demo`. It shows a rotating embedded SVG reticle, a packaged SVG gauge, a fading image and the live construct together. `stop` freezes animations; `demo` restarts; `clear` removes this PB's scene content. The demo clears this PB's previous map, so use a separate PB/Console to retain a running terrain scanner.

```csharp
holo.PutSvg(console, "reticle", svgText, new Vector3D(0, 0.2, 0), 0.01);
holo.PutSvgAsset(console, "gauge", "gauge", Vector3D.Zero, 0.01);
holo.PutImage(console, "icon", "Demo", new Vector2(0.4f, 0.3f), Vector4.One);
holo.SetTransform(console, "icon", MatrixD.CreateTranslation(0.5, 0, 0));
holo.Animate(console, "reticle", "rotation", 0, Math.PI * 2, 3, loop: true);
holo.Animate(console, "icon", "opacity", 0.2, 1, 2, loop: true, pingPong: true);
// Main runs at Update1 or Update10; animation time is measured in seconds.
holo.UpdateAnimations(Runtime.TimeSinceLastRun.TotalSeconds);
```

| API | Behavior |
| --- | --- |
| `PutSvg(console, id, text, position, scale, curveSegments=12)` | Retain bounded SVG source on the server; each viewer compiles its own multicolor mesh; scale is model units per SVG user unit |
| `PutSvg(console, id, text, matrix, curveSegments=12)` | Same with full initial placement; this prevents large SVG coordinates exceeding display bounds before scaling |
| `PutSvgAsset(console, id, asset, position, scale, curveSegments=12)` | Load this mod's `Data/Svg/<asset>.svg`; pass a simple asset name, without extension |
| `PutImage(console, id, asset, size, tint)` | Centered XY quad with full texture UVs, sized in model units; asset must have a registered `HoloMap_Image_<asset>` material |
| `SetOpacity(console, id, opacity)` | Per-geometry-object opacity, multiplied with SVG/image color alpha and layer opacity |
| `Animate(console, id, channel, from, to, seconds, loop=false, pingPong=false)` | Linear timed track, replacing the same channel; duration 0.001–86400 seconds |
| `UpdateAnimations(elapsedSeconds)` | Advance all tracks and SVG frames in this PB wrapper instance |
| `StopAnimation(console, id)` | Freeze that object's tracks and frame playback at the current appearance |
| `PlaySvgFrames(console, id, frames, fps, position, scale=0.01, loop=true, curveSegments=12)` | Change geometry by replaying exported SVG strings, skipping overdue frames instead of uploading a backlog |

SVG support: `svg`, `g`, `path`, `rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`. Paths accept absolute/relative `M L H V C S Q T A Z`, repeated coordinates, exponents, cubic/quadratic curves, reflected controls and elliptical arcs. Transforms support `matrix`, `translate`, `scale`, `rotate` including a pivot, and `skewX/Y`. Presentation attributes or inline styles support fill, stroke, stroke width, fill/stroke opacity and opacity. Colors accept `none`, `#RGB`, `#RRGGBB`, `rgb(r,g,b)` with numeric 0–255 components, and black/white/red/green/blue/cyan/yellow/magenta.

Coordinates remain SVG user units, centered on the viewBox center (or numeric width/height center); SVG Y-down becomes map Y-up in the local XY plane. The viewBox provides a center, not viewport scaling or clipping. Rotate the object around X to lay it flat above the table. Bézier curves use `curveSegments` segments; circles/ellipses use four times that number; arc resolution scales with its angular span. No adaptive sampling or browser antialiasing. Fills reuse the existing simple concave polygon triangulator. Strokes are colored triangle strips with butt caps and bevel joins; their width follows object/view scaling. Layer and object opacity apply to the whole SVG. SVG and images are unlit by default. Submitting all artwork as one object makes visibility/removal/layer assignment atomic.

This is a bounded drawing subset rather than a browser engine. Rounded rectangles, local use/symbol, styled tspans, uniform vector masks and per-shape color-matrix filters are supported. General units, nested SVG viewports, CSS style sheets, embedded images, SMIL, scripts, blur and group filter compositing remain unsupported. Group opacity multiplies each primitive; overlapping primitives do not receive offscreen group compositing. Since preparation is client-side, parse failures are reported by /hdr status and preserve the last usable local mesh.

SVG limits: 65,536 characters, 256 tags, 16 nested elements, 128 sampled points per contour, 2–32 curve segments, plus existing per-object/per-scene geometry budgets. Complex strokes can use many vertices; simplify, reduce resolution or split art into objects. Asset names accept 1–48 ASCII letters/digits/underscores/hyphens. The parser has no XML entity resolver and accepts no resource URLs, event attributes or declarations other than an initial XML declaration. The PB cannot read arbitrary disk/network files through these calls.

Animation channels are `x`, `y`, `z` (map-unit translation offsets), `pitch`, `yaw`, `rotation` (radians; rotation is roll about local Z), `scale` (positive multiplier) and `opacity` (absolute 0–1). Transform channels compose before the base transform, preserving the SVG's placement, then apply XYZ offsets in map coordinates. They can run together. Set the base through `SetTransform` before animating; calling `SetTransform` or `PutSvg` again cancels that object's tracks and frame playback. `Remove`/`Clear` also clear helper state. `SetVisible`/layers hide art without pausing time. Animate once, then advance from Main; do not restart tracks every update. Helpers have a 128-object bound. Only geometry/SVG/image objects support these tracks; labels use their existing API.

The legacy typed wrapper's animation helpers still execute in the PB and send sampled updates. Use the short `HDR.Draw` commands `animate`, `playsvgframes`, and `play` for replicated timelines with client-side playback. SVG frame declarations are bounded to 300 frames and 60,000 total SVG characters, with 1–30 requested fps. Actual frame preparation is subject to the viewer's geometry work budget. World reload resets display state. Shared displays remain public.

#### Gradients, clipping, text, holes, transparency and emission

Reload the updated mod and paste **`artifacts/AppearanceDemo.pb.cs`** into a PB for a side-by-side SVG/native demonstration. The Console or Projector must be named **Holo Map**. Running the demo clears only that PB's existing content on the target and changes its shared view. It shows clipped gradient panels with holes, SVG/native text, a clipped gradient curve and vertex markers. Use a separate target to retain another live map.

| Native API | Behavior |
| --- | --- |
| `PutContours(target, id, contours, outline, fill, thickness=0.008, shaded=false, evenOdd=true)` | Fill one coplanar set of outer boundaries, holes, islands or disconnected contours; outline every input contour |
| `SetGradient(target, id, from, to, startRgba, endRgba, resolution=3, radial=false)` | Apply a linear or centered radial two-color gradient to fills and wires; endpoints/center-radius use object-local coordinates |
| `ClearGradient(target, id)` | Restore the original SVG/native colors |
| `SetClip(target, id, planes)` | Intersect up to 16 half-spaces in object-local coordinates, trimming filled triangles and line centerlines; `null` clears the clip |
| `SetClipBox(target, id, min, max)` | Six-plane axis-aligned box clip |
| `SetClipCircle(target, id, centerXY, radius, sides=12)` | Polygonal approximation to an XY circular aperture, extruded along Z; 3–16 sides |
| `PutText(target, id, text, rgba, height, matrix, anchor="middle")` | Fixed-plane 5x7 glyph mesh, transformed/zoomed like other geometry; start/middle/end alignment |
| `PutVertices(target, id, positions, rgba, radius=0.01, thickness=0.004)` | Render each point as a three-axis cross marker; then apply the same gradient/clip controls |
| `SetTransparency(target, id, value)` | 0 is fully opaque at the object level; 1 fully transparent; equivalent to opacity `1-value` |
| `SetOpacity(target, id, value)` | Existing 0–1 opacity control, now also usable on camera-facing labels and packed animation groups |
| `SetEmission(target, id, strength)` | 0–20 HDR RGB emission boost for geometry, SVGs, images, fixed/camera-facing text and packed groups |

```csharp
holo.SetGradient(target, "curve", new Vector3D(-1,0,0), new Vector3D(1,0,0),
    new Vector4(0,1,1,1), new Vector4(1,0.5f,0,0.3f), 4);
holo.SetClip(target, "curve", new[] { new Vector4(1,0,0,0), new Vector4(-1,0,0,0.8f) });
holo.SetTransparency(target, "curve", 0.25f);
holo.SetEmission(target, "curve", 2);
```

Each clip plane retains points satisfying **`normal.X*x + normal.Y*y + normal.Z*z + W >= 0`**. Clip and gradient operate before the object's transform, then the map view and block transform apply. Clipping interpolates image texture UVs at new boundaries. Wires are clipped by their centerlines, so billboard thickness may extend slightly beyond a boundary. New border outlines are not generated along cut faces. Box/circular helpers describe convex clips; arbitrary SVG clip shapes can be concave or contain holes.

Gradients use tessellated approximations because each billboard triangle has one color. Linear gradients use shared bands; radial gradients use shared concentric annuli rather than separate per-triangle samples. Resolution 1–8 gives 8–64 color bands. Radial ring boundaries use 32 angular segments and preserve arbitrary 3D face orientation. Banding can remain, and complex gradients must fit the geometry and compilation budgets. Gradient alpha multiplies base alpha. Clipping and gradients are compiled locally from retained source definitions; invalid replacements preserve the last valid mesh.

Emission is an additional **unlit HDR brightness term**, not a light source. Zero preserves the old appearance; positive values boost RGB without increasing alpha, independently of the optional face-shading term. Bloom/glow depends on the game's renderer/exposure settings and still needs visual verification. It does not illuminate nearby blocks, cast light/shadows or provide physical luminance units. Geometry is already drawn with unlit transparent materials. Alpha from paints/stops, object opacity and layer opacity multiply together. Opacity/emission for labels and edge colors/emission for geometry are validated and replicated for clients/late joiners. Other clients need the updated mod.

Native text has up to 64 characters, uses packaged proportional vector glyphs, and supports lowercase, multiline start/middle/end alignment, Latin accents, Greek, Cyrillic and selected symbols. Dense text remains subject to geometry budgets. Camera-facing labels use locally cached glyph meshes. Text supports geometry gradients/clipping, and labels support opacity/emission. Contours support 1–16 coplanar loops, 3–128 points each, with at most 256 input points; even-odd/nonzero fills support holes and islands.

SVG additions:

- **Gradients:** local `linearGradient`/`radialGradient` definitions, 1–16 `stop` children, offsets/colors/stop opacity, `objectBoundingBox` (default) or `userSpaceOnUse`, gradient transforms, and pad/repeat/reflect spread. Gradient coordinates support percentages. Only centered radial focal points are supported; no gradient inheritance via href, patterns or CSS paint servers. `fill="url(#id)"` and `stroke="url(#id)"` paint shapes. Set `data-gradient-resolution="1..8"` on the SVG/group/shape to control subdivision (default 3).
- **Clipping and masks:** local clipPath and userSpaceOnUse vector masks support transformed shapes and groups. Masks must use uniform alpha or luminance; mixed paints and gradient masks reject explicitly. Nested clips intersect; work and geometry limits apply. objectBoundingBox clipping/mask units and recursive definitions are unsupported.
- **Text:** text and styled nested tspan support scalar x/y/dx/dy, font-size, text-anchor, fills/gradients/clipping and numeric Unicode entities. Font families use the packaged vector font. textPath, stroked glyphs, external font files, complex shaping and arbitrary Unicode remain unsupported.
- **Compound fills:** `fill-rule="evenodd"` or `nonzero` on paths/groups, with `nonzero` as the SVG default. Multiple `M...Z` contours can form holes, islands or disconnected surfaces. `clip-rule` selects the clip fill rule.

Source references for SVG semantics: [gradients](https://www.w3.org/TR/SVG11/pservers.html), [clip paths](https://www.w3.org/TR/SVG11/masking.html), [text](https://www.w3.org/TR/SVG11/text.html). This implementation deliberately documents its approximations rather than claiming browser-level rendering fidelity. Mathematical/packet tests and compilation pass; actual game whitelist loading, appearance, bloom and multiplayer remain runtime gates.

#### Compressed animation in PB Custom Data

The pelican cycling animation from an optional local `physics/index.html` source has a compact player and compressed data export:

1. Reload the world with the updated HoloMap mod. Name an owned Console **Holo Map**.
2. Paste **`artifacts/PackedPelican.pb.cs`** into a PB on that Console's construct. This standalone player is 3,059 characters; it does not need the full API wrapper appended.
3. Paste the **entire contents** of **`artifacts/pelican.customdata.txt`** into that PB's **Custom Data** (replace existing Custom Data; don't append to INI settings).
4. Run `demo`. Commands: `stop` pauses, `start` resumes, `demo` restarts, `reload` rereads Custom Data, and `clear` removes only the pelican group. `Scale` and `CurveSegments` are at the top of the script; default scale is 0.003 and tessellation is 3.

Measured export: **72 frames, 18-second loop, 4 fps, three SVG parts per frame**. Decoded data is **429,156 bytes**, LZ-compressed data is **11,628 bytes**, and the final ASCII/base64 Custom Data payload is **15,534 characters**. Base64 is only the transport encoding; it increases binary size. Compression provides the savings. The original HTML is 26,134 bytes, but its JavaScript needs offline conversion into game-supported animation data rather than simply being stored as HTML.

This is a vector approximation, with intentionally coarse curve tessellation to fit the scene budget: all 72 frames have been parsed and validated, each part fits the 2,048-point object bound, and the largest complete frame uses 2,909 of the Console's 4,096 points. It retains the bicycle, wheel rotation, moving crank, inverse-kinematics pedalling, body bob, scarf flutter and a sampled looping blink. The scenery, webpage controls, bell sound/text and browser round-stroke rendering are omitted. Browser classes are flattened into presentation attributes, local `use` definitions are expanded, and coordinates are rounded offline. The source HTML is read without modification or execution.

Decode runs **in the mod**, not the PB: when the payload first arrives or changes, a bounded LZ decoder restores the SVG frame table once. The mod retains that decoded table and the last rendered meshes. Calls within the same frame reuse geometry; advancing frames parses only the selected three SVG parts. The PB supplies the playback clock and keeps its own update work small. The decoder accepts data, not executable bytecode/JavaScript, and checks the prefix/version, lengths, ASCII, frame count, fps, part count and every compressed back-reference. Invalid input cannot allocate beyond the decoded-size bound. Initial/current-frame SVG validation and scene-budget preflight occur before replacing any animation chunk.

Limits: 60,000 encoded characters, 2 MiB decoded bytes, 1–128 frames, 1–8 parts, 1–10 fps, at most eight decoded animation caches in the mod. Existing object/geometry/display-radius budgets also apply. Animation IDs are at most 48 characters. Parts use retained IDs `<animationId>~0`, `~1`, etc.; reserve those IDs for the player. The root ID works with `Remove` to remove the group; other object APIs address individual parts. `Clear`, caller/Console cleanup and world unload release caches. Data and scene are public under the existing multiplayer model; other players may see PB Custom Data according to normal game access. Remote playback still follows the existing approximately 2-Hz scene snapshot limit, so 4-fps host playback is not a promise of 4-fps client playback. In-game whitelist loading/rendering and multiplayer remain verification gates.

Rebuild the export with the source path adjusted to its local location. For example, if it is at `physics/index.html` relative to the repository root:

```powershell
.\Build.ps1 -PelicanHtml .\physics\index.html
```

`Tools/PackPelican.mjs` is an adapter for this particular SVG scene and its known animation equations; it is not a general HTML/JavaScript compiler. The generic mod call `PutPackedAnimationFrame(console, id, customData, timeSeconds, position, scale, curveSegments)` is also in `HoloMapApi`. Other offline exporters can use the same data format: `HMA1`, fps, frame count and part count on separate lines, then one normalized single-line SVG per part per frame, ending in a newline. The outer base64 blob has `HMC1` plus a little-endian decoded byte length and LZ token stream: each flag byte selects eight literal or match tokens; a match is a two-byte little-endian distance (1–65535) and one-byte length-minus-three (3–258). The text prefix is `HoloMapAnimation1:`. The exporter implementation and byte-exact decompression tests define the format.

#### One PB, several Consoles with the same name

Use **`artifacts/MultiConsoleAnimation.pb.cs`** instead of the single-Console player:

1. Name each owned Console or Projector Block exactly **Holo Map**. They may be on the PB's main grid or mechanical subgrids on the same construct.
2. Put a packed `HoloMapAnimation1:` payload in **each Console's Custom Data**. Each can contain a different animation; the PB's Custom Data is not used.
3. Paste the new standalone script into one PB and run `start` or `demo`.

Discovery uses all matching `IMyProjector` blocks on the same construct, including Consoles and ordinary projectors; entity IDs identify them even when names are identical. Each display has its own payload, playback time, cache and errors. One empty/invalid/inaccessible display does not stop the others. Running playback discovers additions/removals/name changes and checks Custom Data every two seconds. A newly discovered or edited animation starts at time zero. Empty Custom Data removes this player's animation from that display. Invalid replacement data leaves the last valid image frozen and reports an error until its data changes or `reload` retries it.

Commands apply to this player's matching displays: `start` resumes and discovers; `stop` pauses all; `demo`/`reload` reread data and restart all; `clear` removes the player's animation groups and pauses; `status` lists entity IDs, playback times and errors. Paused discovery can be triggered with `status` or `start`. Renamed/removed displays are dropped and their animation group is removed where the PB still has access. The retained group ID remains `pelican` for compatibility with the previous player; it does not restrict which animation is displayed. This PB can update/remove only its own groups.

The existing mod scopes caches by display entity ID, PB ID and animation ID. Projector support requires this updated mod plus the regenerated PB script. It still has a world-wide limit of **eight active displays and eight decoded animation caches**, shared with other scripts; excess or over-budget displays report errors independently. Each display has its own geometry budget. All displays use the script's `Scale`/`CurveSegments` defaults; each packed file can have different frame counts/fps/parts. Remote viewers retain the existing snapshot-rate limit. Multi-display in-game testing remains required.

All HoloMap calls accept any registered live `IMyProjector` implementation as their anchor, including large/small-grid vanilla projectors and compatible modded projectors. Other terminal-block types remain rejected. Ownership/access and same-mechanical-construct requirements apply. The anchor must be powered/working to draw, but no blueprint is required. Geometry uses the projector's own local axes and the same default 0.85m demo offset; adjust position/rotation for the projector's model as needed. The mod does not set its blueprint, projection settings or welding/build controls.

#### Three extra display assets

`artifacts/SvgSamples` includes two static displays and one animation:

| Name | Content | Custom Data characters |
| --- | --- | --- |
| `navigation-compass` | Static cyan/orange compass reticle | 890 |
| `ship-schematic` | Static top-down spacecraft diagram | 1,090 |
| `radar-sweep` | Cyan beam sweeping synthetic fixed contacts; 32 frames, 4 fps, 8-second loop | 1,350 |

Paste a sample's `.customdata.txt` into any **Holo Map** Console's Custom Data and run `start` in the multi-Console PB. All use the player's default scale 0.003. Static displays are single-frame packed animations, so the same player supports them without code changes. No mod update is needed. The radar artwork is decorative, not connected to a live scanner.

Each also has a standalone `.svg`: static SVGs can be imported directly with `PutSvg`; `radar-sweep.svg` uses standard SVG `animateTransform` for browser preview and must be baked to the supplied packed frames for HoloMap. Its browser animation is not accepted by `PutSvg`. The packed radar uses only supported geometry and transforms.

`Tools/GenerateSampleAssets.mjs` regenerates all three, and `Tools/PackAnimation.mjs` is a reusable packed-frame encoder for other authored SVGs. Build generates them and checks byte-exact decoding and every frame against the game geometry budget.

#### Pack an image

```powershell
.\HoloMap\Tools\PackImage.ps1 -InputPath C:\Art\icon.png -Asset MyIcon
.\HoloMap\Build.ps1
```

The offline packer accepts PNG/JPEG/BMP/GIF via Windows System.Drawing (GIF first frame), up to 2048×2048. It writes an uncompressed RGBA DDS under `Mod/Textures/HoloMap` and a matching transparent-material definition in `Mod/Data`. Install/package those files and reload the world; PB then uses asset name `MyIcon`. The texture bytes are packaged mod assets, not sent through PB/network calls. All clients need the same mod assets. Arbitrary image URLs or base64 image uploads are not implemented. `Demo` ships as a small transparent test image. Verify texture orientation/alpha edges and DDS loading in game; automated checks validate bytes and UVs, not GPU output.

#### Author animation in JS, React or Svelte

`Tools/ExportSvgFrames.mjs` is an offline Node exporter. An authoring module exports `render(timeSeconds)` returning an SVG string, plus `fps` and `duration`. Plain JS is demonstrated in `Examples/Authoring/pulse.mjs`. React's server SVG rendering or compiled Svelte server rendering can supply that string after the author has installed/bundled their framework in their own tooling. Arbitrary HTML/components do not automatically become SVG: output must contain supported SVG geometry. No JS or framework runtime is shipped into the game.

```powershell
node .\HoloMap\Tools\ExportSvgFrames.mjs .\HoloMap\Examples\Authoring\pulse.mjs pulse
.\HoloMap\Build.ps1
```

The exporter creates `artifacts/pulse.frames.cs`, containing PB-ready string constants. `artifacts/SvgFramesDemo.pb.cs` includes those frames and the API, ready to paste and run. For another animation, append its generated constants inside your PB and call `PlaySvgFrames`. The exporter validates size/type, and Build parses every demo frame with the actual mod importer. Authoring modules run locally with Node permissions; run code you trust. Runtime parsing, style/color/UV network validation, and rejected-input tests are included in the checks. Actual game whitelist loading, texture rendering and multiplayer behavior remain runtime verification gates.

Implementation references: [SVG path specification](https://www.w3.org/TR/SVG11/paths.html), [SVG transform specification](https://www.w3.org/TR/SVG11/coords.html), and [SE transparent geometry API](https://keensoftwarehouse.github.io/SpaceEngineersModAPI/api/VRage.Game.MyTransparentGeometry.html). Installed game assemblies are the compile-time authority for this build.

### Rendering verification

The initial shapes, armor preview, and piston hierarchy have been confirmed in game by the user. Independent block models explicitly create render objects on first visibility, after their miniature poses are assigned: setting `Render.Visible` alone does not create render objects for entities inserted into the scene while hidden. This initialization fix still needs in-game verification for rotors, hinges, PBs, and other functional blocks. Then test a host plus another client, late joining, object removal, ownership changes, and a dedicated server. Scanning examples and camera capture are now maintained in the separate CamScan API project.

LCD backgrounds are now explicit: `H("lcd-background", "transparent")` sets both native clear-color and background-alpha settings, and HDR no longer forces an opaque backdrop. Eligible rectangular fills use one quad to avoid internal triangle-mask seams. The native screen's resolution, update cadence and material still apply; [LCD.md](../LCD.md) records the investigation and proposed calibrated vector backend.
