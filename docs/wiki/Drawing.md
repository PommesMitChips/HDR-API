# Drawing, layers and animation

HDR scenes retain named geometry, source artwork and appearance. Send a definition once, change only what changed, and let each viewer compile/render locally. Use [PB integration](Programmable-Blocks.md) for the `H` helper and target selection.

For client-evaluated flicker, moving refresh bars, depth copies, cosmetic particles, projector rays and reveal transitions, see [Hologram effects](Special-Effects.md). Effects attach to authored drawing items and combine with their appearance and transforms.

![Content composed onto different display surfaces](diagrams/composition-surfaces.svg)

## Geometry command reference

Arguments below follow the command name. A point can be `Vector3D` or three numbers. `paint` means a supported color string or normalized RGBA `Vector4`. Square brackets denote optional arguments; defaults are shown.

| Command | Arguments | Purpose |
|---|---|---|
| `line` | `id, from, to, paint, [thickness=.008]` | One segment |
| `wires` | `id, Vector3D[] points, Vector2I[] edges, paint, [thickness=.008]` | Indexed segments; duplicate undirected edges drawn once |
| `circle` | `id, centre, radius, paint, [segments=64, thickness=.008, normal=UnitZ]` | Sampled circle |
| `curve` | `id, Func<double,Vector3D>, start, end, paint, [segments=128, thickness=.008, closed=false]` | Fixed sampling; anchor selection required |
| `polygons` | `id, Vector3D[] points, int[][] faces, outline, [fill="#00dfff", thickness=.008, shaded=true]` | Simple planar faces, including concave faces |
| `contours` | `id, Vector3D[][] loops, outline, fill, [thickness=.008, shaded=false, evenOdd=true]` | Coplanar holes, islands and disconnected fills |
| `points` | `id, Vector3D[] points, paint, [radius=.01, thickness=.004]` | Vertex markers |
| `text` | `id, text, [position=Zero, height=.1, paint="white", alignment="middle"]` | Fixed vector glyph geometry |
| `label` | `id, text, [position=Zero, height=.035, paint="white", alignment="middle"]` | Viewer-facing label; alignment is accepted by the short parser but label presentation uses its label API |
| `puttext` | `id, text, paint, height, MatrixD pose, [alignment="middle"]` | Explicit text pose |
| `putlabel` | `id, position, text, paint, [height=.035]` | Explicit label order |
| `svg` | `id, svgText, [position=Zero, scale=.01, curveSegments=12]` | Inline SVG; `MatrixD` may replace position/scale |
| `svg-asset` | `id, assetName, [position=Zero, scale=.01, curveSegments=12]` | Packaged `Data/Svg/<name>.svg` |
| `image` | `id, assetName, Vector2 dimensions, [paint="white"]` | Packaged image; two numbers may replace dimensions |

Polygons must be planar, simple and have no holes; use contours for holes. Do not repeat the closing vertex. Triangulation outlines follow authored boundaries, so internal triangulation diagonals do not become strokes. Functions with discontinuities need separate curves. Curves sample at a fixed segment count; they do not discover discontinuities or adapt to rendered density. The callable is evaluated to geometry during the command; no executable function is replicated to viewers.

```csharp
H("line", "axis", new Vector3D(-.6, 0, 0), new Vector3D(.6, 0, 0), "cyan");
H("polygons", "panel",
    new[] { new Vector3D(-.5,-.3,0), new Vector3D(.5,-.3,0),
            new Vector3D(.5,.3,0), new Vector3D(-.5,.3,0) },
    new[] { new[] { 0,1,2,3 } }, "cyan", "#123c5260", .008, false);
H("text", "title", "SYSTEM READY", 0, .15, 0, .08, "white");
```

Text uses packaged proportional vector glyphs, with Latin accents, Greek, Cyrillic and selected symbols. This is not an arbitrary installed-font or browser text engine. Fixed text is geometry and shares mesh budgets; labels have separate bounded label/text admission.

## Transforms and appearance

| Command | Arguments | Behavior |
|---|---|---|
| `view` | `offset, [scale=1, pitch=0, yaw=0, roll=0]` | Shared map view; alternatively `offset, rotationVector, [scale=1]` |
| `transform` | `id, MatrixD affineTransform` | Object base transform; resets its animation |
| `pose` | `id, position, [scale=1, pitch=0, yaw=0, roll=0]` | Object base transform |
| `pose` | `position, [scale=1, pitch=0, yaw=0, roll=0]` | Utility form returns a `MatrixD` |
| `move` | `id, position` | Replace translation; resets its animation |
| `visible` | `id, bool` | Retain but hide/show |
| `remove` | `id` | Remove caller's item and animation |
| `opacity` | `id, value` | Alpha multiplier, 0–1 |
| `transparency` | `id, value` | Equivalent opacity `1-value` |
| `emission` | `id, value` | Additional unlit RGB brightness, not a world light |
| `clip` | `id, Vector4[] planes`, or `id` | Set or clear object-local half-space clips |
| `clip-box` | `id, min, max` | Convex box clip |
| `clip-circle` | `id, Vector2 centre, radius, [sides=12]` | Convex XY circle approximation; scalar X/Y also accepted |
| `gradient` | `id, from, to, startPaint, endPaint, [resolution=3, radial=false]` | Locally tessellated gradient |
| `cleargradient` | `id` | Remove gradient |

Object clip planes keep `dot(normal, point) + W >= 0` **before** object/map transforms. This differs from `screen-clip`'s anchor-space convention; see [surfaces](Display-Surfaces.md#clipping-and-projection-envelopes). Clipping interpolates texture UVs. Wire centreline clipping can leave thickness slightly outside a boundary; cuts do not generate new border outlines.

Gradients approximate color with bounded geometry: resolution 1–8 gives 8–64 bands. Radial gradients use shared annuli with 32 angular segments. Alpha multiplies base paint alpha. Emission adds brightness without increasing alpha; bloom depends on the game's exposure/render settings. It does not cast light or shadows onto nearby blocks.

## Layers and composition

| Command | Arguments | Behavior |
|---|---|---|
| `layer` | `objectId, layerName` | Assign retained object/label |
| `construct-layer` | `layerName` | Assign the tracked live construct |
| `layer-order` | `layerName, [order=0]` | Presentation order |
| `layer-visible` | `layerName, bool` | Hide without deleting data |
| `layer-opacity` | `layerName, float` | Alpha multiplier 0–1 |
| `layer-state` | `layerName` | `MyTuple<bool,float>` visibility/opacity |
| `toggle` | `layerName` | Toggle and return visibility |
| `solo` | `selectedLayer, string[] layers` | Show only selected listed layer |

Object paint alpha × object opacity × layer opacity combine. Projected displays add screen and side multipliers. Raster UI composes source-over colors into one RGBA image. Direct vectors use the game's transparency/depth path; layer ordering is not a guarantee of browser-style offscreen group compositing.

## SVG scope

HDR parses a bounded SVG subset; scripts and browser runtime features do not execute. Geometry is centred using the SVG viewBox and Y is converted to display-up coordinates. `curveSegments` is 2–32 for importer curves.

Supported features include paths, standard basic shapes, transforms, inline supported presentation styles, rounded rectangles, local `use`/`symbol`, compound nonzero/even-odd fills, vector text/tspans, local linear/radial gradients, clip paths, uniform vector masks and supported per-shape color-matrix filters. Gradient stops are limited to 1–16. Supported gradient units include object bounding box and user space; spread can pad/repeat/reflect. Radial focal points must be centred. `data-gradient-resolution="1..8"` controls tessellation.

Unsupported features include CSS style sheets, general/nested SVG viewport units, scripts, SMIL playback, embedded image elements, external fonts, complex text shaping, `textPath`, blur, pattern paints, recursive definitions, general mixed/gradient masks and group filter compositing. Group opacity multiplies each primitive, so overlapping children can differ from a browser's isolated group opacity. Export complex text to paths and bake unsupported effects offline.

`svg-asset` reads an asset packaged in HDR's own mod. It does not accept an arbitrary filesystem path, URL or another mod's executable resource. Pack image assets with [PackImage.ps1](../../Tools/PackImage.ps1); register both vector transparent materials and native LCD textures when both paths are needed.

See [SvgDemo.cs](../../Examples/SvgDemo.cs), [SvgFramesDemo.cs](../../Examples/SvgFramesDemo.cs) and [AppearanceDemo.cs](../../Examples/AppearanceDemo.cs) for complete examples.

## Declarative animation

| Command | Arguments | Behavior |
|---|---|---|
| `animate` | `id, channel, from, to, seconds, [loop=false, pingPong=false]` | Declare timeline once |
| `stopanimation` | `id` | Stop that object's tracks/frame playback |
| `pause` / `resume` | None | Caller drawing-context timeline controls; anchor selection |
| `playsvgframes` | `id, string[] frames, fps, position, [scale=.01, loop=true, curveSegments=12]` | Bounded baked frame playback |
| `play` | `id, packedData, [position=Zero, scale=.003, curveSegments=3]` | Packed animation playback |
| `packedframe` | `id, packedData, time, [position=Zero, scale=.003, curveSegments=3]` | Explicit packed frame; `MatrixD` may replace position/scale |

Channels are `x`, `y`, `z`, `pitch`, `yaw`, `rotation` (Z roll), `scale` and `opacity`. Translation uses model units; rotations use radians; scale is positive. Transform channels compose against the base pose; opacity is absolute 0–1. Setting a new transform/SVG definition cancels tracks. Hidden layers continue their timeline; hiding is not pausing.

```csharp
H("circle", "pulse", 0, 0, 0, .25, "orange");
H("animate", "pulse", "scale", .8, 1.2, 2, true, true);
```

The short API's animation time is mod-managed. Send the declaration once; do not call `animate` every `Main` update. The older `HoloMapApi` helper also has PB-side animation helpers with their own update semantics; do not confuse them with the short endpoint's timelines.

For authoring, [ExportSvgFrames.mjs](../../Tools/ExportSvgFrames.mjs) runs offline with Node and accepts an authoring module producing SVG strings. React/server SVG or another framework can generate those strings in your tooling; no JS framework is shipped into the game.

## Live construct and interface actions

`H("live", [radius=.55, offset=Zero])` tracks the PB's mechanical construct. Console/Projector anchors render native model previews; the LCD path is a simplified flattened block-bounds view. `trackconstruct` additionally accepts an enable flag. `track-world(metresPerUnit, rootLocalOrigin)` chooses a real-distance scale. Connector-docked ships are not part of mechanical tracking.

Use `HDR.UI` to define fixed, authorized actions and numeric artwork controls. Fixed buttons support native **look and Use** on physical LCDs and anchor UI planes. Menus are bounded bundles; `pb` actions run the registering PB with a fixed server-held argument, `toggle` toggles a layer, and `menu`/`focus` grant viewer-local bundle visibility. Numeric controls add the persistent world viewer described below.

```csharp
Func<string, object[], object> _ui;
object U(string command, params object[] args)
{
    if (_ui == null)
    {
        var property = Me.GetProperty("HDR.UI");
        if (property == null) throw new Exception("Enable HDR API and reload the world.");
        _ui = property.As<Func<string, object[], object>>().GetValue(Me);
    }
    return _ui(command, args);
}
```

```csharp
U("target", "HDR UI");
U("bundle", "main", true);
U("button", "status", "main", 0, 0, 1.2, .2,
  "Status", "pb", "ui:status");
```

| UI command | Arguments / result |
|---|---|
| `version` | No arguments; `HDR.UI/1` |
| `capabilities` | No arguments; includes `values=1`, `constraints=line,path,rotation`, `mouse=client-provider`, `viewer=persistent-bundle` and `pointer=HDR.Pointer/1`; not proof of local capture readiness |
| `target` | Name/block reference; shares drawing target context |
| `bundle` / `menu` | Bundle ID, `[initialVisible=true]` |
| `button` | Widget ID, bundle ID, centre X/Y, width, height, caption, action kind, `[argument=""]` |
| `hit` | Same as button, omitting caption; use existing artwork |
| `bind` / `action` | Widget ID, action kind, `[argument=""]` |
| `visible` | Widget/bundle ID, bool |
| `remove` | Widget/bundle ID; referenced bundles must first be unreferenced |
| `clear` | Remove this caller's UI on selected target |

Boxes use drawing XY coordinates with Z zero and the scene view applied. IDs use 1–24 lowercase letters, digits, hyphens or underscores; bundle and widget IDs must differ. Bounds are 64 hit regions per UI display, 128 globally, 8 bundles per UI display and 16 UI declarations globally. Captioned buttons share the display's 16-label/256-character budget; custom artwork with `hit` can avoid caption limits but still uses ordinary geometry budgets.

`pb` runs only the registering PB using the server-held fixed argument, up to 256 characters. `toggle` names a shared drawing layer. `menu` and `focus` name an existing bundle and grant viewer-local visibility after validated interaction. Grants expire and follow character/declaration revision; local visibility is not confidentiality. Bundle layers are `ui-<bundle>`, so `H("layer-order", "ui-context", 20)` places context artwork above lower layers. Declare hidden context bundles once instead of continuously rebuilding them in `Main`.

The client sends registered IDs/revision/sequence, not arbitrary PB arguments. The server checks character, on-foot control, access, construct relationship, range, actual head-ray hit and physical occlusion before an action. Definitions and canonical arguments are public replicated data.

See [UiDemo.cs](../../Examples/UiDemo.cs) for fixed buttons and [UI.md](../../UI.md) for input-backend context. Projected curved-screen hit mapping helpers exist, but interactive widgets are not attached to those surfaces. A HUD/menu mod needs the separate [mod boundary](Mod-Integration.md); a PB does not gain a global HUD from these block controls.

### Numeric artwork controls

[Interactive controls](Interactive-Controls.md) bind one authored item to a finite, ranged and snapped value with a line, polyline or rotation constraint. The [PB demonstration](../../Examples/InteractiveControlsDemo.cs) moves retained artwork and explicitly synchronizes real script variables through queries, structured events and compare-and-set source writes. HDR does not discover variables by name or execute arbitrary callbacks from artwork.

In on-foot, first-person gameplay, **Use** on an authored hotzone enters that bundle's persistent viewer. Artwork stays on the world display without a camera-facing popup. An actual mouse-down starts a value gesture; mouse-up commits that gesture and releases its value lock while the cursor and viewer remain open. **Escape** or context loss closes the viewer and cancels remaining gestures. Hit rectangles are authored in the item's local XY plane and follow its admitted pose; padding and transparent holes can be included, so this is not pixel-perfect picking.

Console/Projector controls use their world display. Physical LCD picking admits XY paths and local-Z rotation with proof that the artwork pose remains planar. Curved/mesh display-surface picking is outside this scope. Native PB mouse capture requires the optional Client Renderer pointer provider; numeric math and a cooperating client mod's own input route work without it. Compilation and offline tests pass; live input acceptance remains unverified.

The general `HDR.ModClient/1` mod renderer provides retained vector/text/SVG and registered-material imagery in owner-scoped HUD/world contexts without the native client plugin. HUD artwork uses near-plane `PostPP` billboards and remains depth-tested; it is not an unconditional always-on-top overlay. World artwork uses the standard world depth path. A cooperating mod supplies context-space pointer/press state and polls bounded hit events; HDR does not capture global game input or acquire mouse focus on its behalf. See [Mod-Integration.md](Mod-Integration.md) and [composition recipes](Composition-Recipes.md) for the complete endpoint and lifecycle.
