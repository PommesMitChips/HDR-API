# Compose sources, layouts and surfaces

**ALPHA — HDR API 0.9.11 / scene 20, HTML frontend 0.2.0.** PB camera/persistent pointer uses compatible Client Renderer 0.9.13+; the new client-local native source service requires 0.9.14. HDR separates what produces content, where content sits in a canvas, how that canvas maps onto a surface, how controls receive input, and which effects apply. Choose these layers independently, then check the selected backend's actual primitives, resource limits and input capabilities.

For example, a camera does not require a physical LCD, HTML does not require a plane, and a curved surface does not require a camera. A provider-backed window, ordinary SVG/text and controls can share one projected canvas with explicit paint order. This is bounded HDR composition, with the existing renderer and authority contracts; it adds no browser JavaScript or arbitrary virtual camera POV.

![HTML documents, source slots, surface mapping and input](diagrams/html-frontend.svg)

| Layer | Select independently | Actual boundary |
| --- | --- | --- |
| Content source | Retained geometry/text/SVG, packaged images, synthetic scene, native LCD texture, camera panorama or a registered external provider | Each producer retains its acquisition, ownership, generation and budget rules. A source ID selects data; it does not grant access or invent a new producer. |
| Layout | Manual canvas rectangles, Profile1 HTML layout or client-mod composition | Layout places and clips content. HTML source attachments use an existing explicit node ID, not an `img`/`video` element or browser resource fetch. |
| Surface | Plane, cylinder, sphere, ellipsoid or authored UV mesh | Content is prepared in the centred XY canvas, then mapped with the declared shape, fitting and clip. Surface quality is independent of acquisition detail. |
| Input | Server-owned Core UI, native persistent pointer, or cooperative client-mod/sprite input | Use the input route belonging to that document/backend. A render texture alone does not supply controls or an authenticated click. |
| Effects | Object/layer paint and opacity, supported screen effects, surface sides and visibility | Effects need a representable primitive. Native RGB cannot regain per-pixel alpha; a miniature model cannot be treated as a composited texture without a real producer. |

## Run the camera-in-HTML ellipsoid

Paste the entire [`HtmlCameraEllipsoidDemo.cs`](../../OptionalMods/HDRHtmlFrontend/Examples/HtmlCameraEllipsoidDemo.cs) into a programmable block. It uses raw `HDR.Draw` and `HDR.Html` delegates and needs no appended helper.

1. Add the core 0.9.11+ and HTML frontend 0.2.0 world mods explicitly.
2. Name a dedicated admitted Console or Projector **`HTML Display`**. Name two working physical camera blocks on the PB's construct **`HDR Camera 1`** and **`HDR Camera 2`**. Keep these names unique and accessible to the PB owner.
3. Each viewer needs **HDR Client Renderer 0.9.13 or later** for direct world camera capture. The interacting viewer also needs it for automatic persistent mouse controls. The current package is **0.9.14**, which adds the new client-local source bridge; this PB example remains compatible with 0.9.13.
4. Run `demo`. It creates the owned `htmlcamera` screen: a bounded ellipsoid patch with radii `(2.4, 1.5, 2.0)`, angular spans `(1.5, 1.1)` radians, and a 3.2×1.8 m canvas. A Console's table envelope is explicitly disabled for this dedicated demo; other range/view limits still apply.
5. Use the [HDR persistent control viewer](Interactive-Controls.md) to click the HTML **Previous** and **Next** buttons. The PB polls clicks in `Update10` and replaces only `#pov`'s physical camera source ID. Terminal `previous`/`next` provide the same explicit switching operation.
6. `status` prints source readiness, provider reason and server publication. `clear` removes only this document and its owned child screen. After PB recompilation, source/target retirement or endpoint invalidation, correct the cause and run `demo` deliberately.

The two cameras can occupy different positions and face different directions. Switching selects their actual physical viewpoints. `screen-camera` controls synthetic/pinhole projection; it does not move an arbitrary native capture camera. With a single camera, uncovered panorama directions remain provider-defined gaps. Source readiness and server publication do not establish that a viewer rendered a frame; live visual, GPU, input and multiplayer acceptance remain unverified.

## Bind a layout without changing its surface

Create the projected screen through `HDR.Draw`, then bind the document to its existing owned ID. The document pose is its **centre in screen-canvas metres**, with +X right and −Y down. `bind` and ordinary `bind-sprites` retain their older top-left pose convention.

```csharp
Draw("target", anchor);
Draw("screen", "htmlcamera", MatrixD.CreateTranslation(0, 2, 0), 3.2, 1.8, 3.2, 1.8);
Draw("screen-ellipsoid", new Vector3D(2.4, 1.5, 2.0), 1.5, 1.1, "outside");
Draw("screen-mapping", "angular");

long document = (long)Html("bind-screen", anchor, "htmlcamera",
    html, css, 640.0, 360.0, MatrixD.Identity, .005);
bool accepted = (bool)Html("attach-source", document, "pov",
    "camera-panorama", camera.EntityId.ToString(
        System.Globalization.CultureInfo.InvariantCulture));
var readiness = (VRage.MyTuple<bool, string>)Html("source-status", document, "pov");
```

The HTML contains a block `<div id='pov'></div>` with an explicit size. A visible identified block produces a `SourceRegion` even if it has no background or text. The region retains its full content bounds and its effective rectangular clip, including ancestor `overflow:hidden`. Painting inserts the source after that node's background/border and before its children. Partial clipping crops the provider's UVs against the original content box; it does not stretch the remaining video into the clipped rectangle.

`attach-source` commits a source declaration synchronously with the current accepted frame. A `true` result can still have `Pending`/`RequiresPlugin` readiness. `detach-source` removes that node's attachment. Missing, hidden or fully clipped nodes deactivate their published slot while preserving the desired attachment for a later visible layout. Query `source-capabilities` and `source-status`, and preserve their actual backend/provider reason in diagnostics.

Source attachment does not require replacing markup. Change `sourceId` on the same handle/node/provider to switch feeds while preserving the document, controls and their ownership. Ordinary camera frames and source changes preserve the surface coordinate generation; changing screen pose, shape, mapping, canvas/view, clip, visibility or sides cancels stale input contexts.

## Use the canvas directly

HTML is optional. A selected projected screen accepts bounded keyed source slots beside retained artwork:

```csharp
Draw("target", anchor);
Draw("screen", "dashboard");
Draw("screen-slot", "camera", "camera-panorama", cameraId,
    new Vector4(-1.2f, -.55f, 2.4f, 1.1f), // content: lower-left XY, width, height
    new Vector4(-1.2f, -.55f, 2.4f, 1.1f), // effective canvas clip
    new Vector4(0, 0, 1, 1),               // provider UV: top-left XY, width, height
    10, 1.0);                             // order and opacity
Draw("text", "caption", "EXTERNAL VIEW", 0, .7, 0, .1, "cyan");
Draw("layer", "caption", "overlay");
Draw("layer-order", "overlay", 20);
var state = (VRage.MyTuple<bool, string>)Draw("screen-slot-status", "camera");
```

Slots and retained artwork share one ordered canvas stream. Raster artwork is split into contiguous runs around source windows rather than moved wholesale above or below them. Replace the same `screen-slot` ID to change its source/rect/clip/UV; use `screen-slot-pose` for an affine source canvas pose and the layer/order/visibility/opacity/refresh commands for bounded updates. At most 16 slots fit one screen; IDs are at most 12 characters, orders are −10000–10000, and slot dimensions are bounded to 12 m. Shared anchor, provider, texture, lease and client budgets still apply. See [display surfaces](Display-Surfaces.md#source-slots-and-ordered-canvas) for the exact commands.

`screen-source` remains the legacy default whole-screen source. Independent slots use independent consumers and lifetimes; removing one slot must not retire another provider consumer. Detach and remove owned content when its consumer ends.

## Choose a backend that can represent the composition

The generic slot protocol preserves both existing provider formats. **Protocol 1** supplies a detached coloured triangle mesh and maps through the vector surface path; **protocol 2** supplies detached RGBA8 or a borrowed registered material with its payload-specific evidence/lease. RGBA needs a compatible raster upload backend, while a borrowed material needs its actual registered producer and valid lease. Each format can be placed on plane/cylinder/sphere/ellipsoid/mesh, beside ordinary vector or raster artwork, using the same supported surface/input/effect rules. A missing producer or upload primitive remains a concrete capability failure. The [provider reference](Api-Reference.md#display-source-protocols-1-and-2) describes the unchanged format boundaries.

| Combination | Supported path or concrete constraint |
| --- | --- |
| HTML SVG + camera window + curved controls | `bind-screen` plus `attach-source`, projected Core UI and the current camera/input plugin. All five screen shapes use the declared surface for input. Supported mapping and bounded artwork rules still apply. |
| Genuine native LCD sprites on a curved screen | `bind-sprites-screen` uses an explicitly owned physical SCRIPT/NONE LCD and maps its real `lcd-texture` into the existing owned screen. All five surface kinds are available; every viewer needs the current renderer. |
| Native sprite layout plus an external source window | The native backing is opaque RGB. Later visible HTML artwork/control overlapping the source window, or partial attachment opacity, is rejected with a backend reason. Use a non-overlapping layout or a backend with the needed primitive. |
| External engine camera texture inside a physical LCD | Unsupported by the physical LCD renderer. Use an owned projected screen. There is no silent 128×72 compatibility raster substitute. |
| `<img>`, `<video>`, arbitrary CSS group opacity or rounded source clipping | Outside Profile1. A node attachment adds a generic source slot; it does not add these browser features. Rectangular clip is supported. |
| Native block/grid miniature beside a projected composition | A native miniature remains 3D world model content and can coexist with the projection. To put a rendered grid view into a 2D node, use a real camera capture, a bounded provider, or `screen-scene` model triangles with the synthetic camera. Declaring a node does not automatically warp a native model shader onto a UV shell. |

Native LCD text uses actual Debug measurements and `SurfaceSize`; SVG/text uses packaged Inter cap-height metrics. A source LCD's padding, native update cadence, resolution and background remain relevant. Generic mapping preserves those facts rather than inventing a transparent off-screen LCD. Client-local mod input remains cooperative and belongs to the consumer's existing input session; see [mod integration](Mod-Integration.md) and the [local HTML API](../../OptionalMods/HDRHtmlFrontend/Docs/Local-API.md).

## Geometry allowance and drawing work

Aggregate geometry is **unlimited by default**, independently for points and primitives: PB `budget(0, 0, 20000)` preserves the separate 20,000 drawing-work default, and `budget-settings` returns `MyTuple<int,int,int>` with those geometry zeros. Explicit finite geometry allowances still validate declared content; a finite setting below existing geometry rejects atomically. Per-item, object, payload, texture and frame-work bounds remain separate.

Client-mod owners use `api.GeometryLimit()` for the default `0`/`0` aggregate allowance and `api.GeometrySettings()` → `MyTuple<int,int>`. Each nonnegative argument can be configured independently; accepted finite settings retire cached source frames exceeding the combined allowance while keeping desired slots. Hosted HTML provides `htmlApi.GeometryLimit` / `GeometrySettings` on the owner, without a document handle. HTML painter `MaxPoints` / `MaxPrimitives` likewise default to `0` and query the actual owner geometry settings instead of imposing a fixed 8,192 aggregate ceiling. The local owner's separate drawing default is 4,096, within the global 20,000-per-frame mod share. See [performance and budgets](Performance-and-Troubleshooting.md#scene-budgets).

## Compose in a client-local mod context

For a local HTML document, [`HdrHtmlApi`](../../Api/Mods/HdrHtmlApi.cs) exposes `CreateSurface` and the same `AttachSource`/`DetachSource`/`SourceStatus`/`SourceCapabilities` helpers. The world pose locates the surface; the document's centred canvas dimensions are automatically `width * metresPerPixel` and `height * metresPerPixel`.

```csharp
var surfaceSettings = new VRage.MyTuple<string, object[]>[] {
    new VRage.MyTuple<string, object[]>("anchor", new object[] { anchor }),
    new VRage.MyTuple<string, object[]>("mapping", new object[] { "angular" }),
    new VRage.MyTuple<string, object[]>("sided", new object[] { true, 1.0, .35 }),
    new VRage.MyTuple<string, object[]>("error", new object[] { .02 })
};
long document = htmlApi.CreateSurface(html, css, 640, 360, worldPose, .005,
    "ellipsoid", new object[] { new Vector3D(2.4, 1.5, 2.0), 1.5, 1.1, "outside" },
    surfaceSettings, "svg", 0);
bool attached = htmlApi.AttachSource(document, "pov", "camera-panorama", cameraId);
htmlApi.PointerRay(document, rayOrigin, rayDirection, held); // owned cooperative input
var events = htmlApi.PollEvents(document);
```

`CreateSurface` supports `vector`/`svg`, with `plane`, `cylinder`, `sphere`, `ellipsoid` or `mesh` parameters matching the core surface declarations. The optional `mapping` setting accepts angular/geodesic/pinhole plus optional vertical FOV and source aspect; `sided` and `error` configure surface sides and chord error. An actual accessible block `anchor` and a provider that explicitly negotiates local-consumer support are required for sources. Native sources need Client Renderer **0.9.14**; missing/unsupported capability rejects attachment before changing prior accepted sources and preserves its precise reason. This route creates no physical LCD or fake native sprite fallback. Local `PollEvents` retains `MyTuple<string,string,string,MyTuple<double,long>>[]` (kind, node ID, action, value/published document revision), with no authenticated PB player identity.

Existing HUD documents use the same attachment API after setting an actual source anchor:

```csharp
long hudDocument = htmlApi.CreateHud(html, css, 640, 360, "svg", 0);
htmlApi.SetSourceAnchor(hudDocument, anchor); // raw source-anchor(document, actualBlock)
bool attached = htmlApi.AttachSource(hudDocument, "pov", "camera-panorama", cameraId);
htmlApi.Pointer(hudDocument, ownedPixelX, ownedPixelY, held);
```

HUD source regions use the committed top-left Y-down pixel canvas and existing cooperative pixel input. HUDs remain flat; attaching a source does not introduce HUD curvature. `SetSourceAnchor` can also update a `CreateSurface` document's actual anchor. Existing `CreateWorld` documents support `SetSourceAnchor`/`AttachSource` through the shared plane mapper, preserving their original top-left pose across resize and their cooperative pixel input; `PointerRay` also resolves that committed plane. Only genuine physical `CreateNativeLcd` rejects external engine texture attachment with an explicit renderer reason. Source admission requires actual anchor validity and provider consumer capability before changing a prior declaration.

The [`HdrModApi`](../../Api/Mods/HdrModApi.cs) helpers expose the same source/canvas/surface separation for local world contexts. Configure `SurfacePlane`, `SurfaceCylinder`, `SurfaceSphere`, `SurfaceEllipsoid` or `SurfaceMesh`, choose `ContextMapping` and declare retained content and `SourceSlot` independently. This route is client-local; your mod owns lifecycle, input suppression, gameplay actions and any multiplayer messages.

```csharp
// api is a connected HdrModApi; anchor is an actual accessible block.
long context = api.CreateWorld(worldPose);
api.SurfaceEllipsoid(context, 3.2, 1.8, new Vector3D(2.4, 1.5, 2.0), 1.5, 1.1, false);
api.ContextMapping(context, "angular");
api.ContextAnchor(context, anchor);
api.SourceSlot(context, "pov", "camera-panorama", cameraId,
    new Vector4(-1.2f, -.55f, 2.4f, 1.1f),
    new Vector4(-1.2f, -.55f, 2.4f, 1.1f), new Vector4(0, 0, 1, 1), 10, 1.0);
var source = api.SourceSlotStatus(context, "pov");
// Consumer-owned input adapter supplies the actual world-space ray.
api.PointerRay(context, rayOrigin, rayDirection, held);
```

Context surface coordinates are centred Y-up metres; source UVs remain normalized top-left coordinates. Slots sort in the same stream as retained item `Order` values; ordinary artwork precedes a source at equal order. Native camera/LCD/portal sources require an explicit actual `ContextAnchor` and the negotiated local-consumer service from **Client Renderer 0.9.14**. The PB example above uses the already existing 0.9.13 camera/input service. `ContextSourceStatus(context, provider)` returns `MyTuple<bool,bool,string>`: consumer capability, actual anchor validity and reason. `SourceSlotStatus` reports slot readiness separately; neither a declaration nor capability admission proves a rendered viewer frame.

Cooperative `PointerRay` resolves the actual surface, then the current canvas and control source plane. `SurfaceRay(context, origin, direction)` is a read-only, event-free query returning `MyTuple<bool,Vector2,Vector3D>`: hit, centred canvas metres and actual world hit. It uses current visibility, shape, enabled sides and canvas crop; the HTML route converts that committed-frame hit into document pixels before cooperative document input. Numeric constraints still require affine XY artwork, XY paths and a local-Z rotation axis. Call `CancelPointer` on focus/ownership loss and `UpdateBindings` explicitly on the simulation thread outside drawing callbacks. Retire owned contexts and resource consumers on unload or connection-generation change. Native local portals refer to a real existing portal declaration on the actual anchor; this API adds no portal-creation shortcut or arbitrary capture POV.

Continue with [composition recipes](Composition-Recipes.md), [HTML API](../../OptionalMods/HDRHtmlFrontend/Docs/PB-API.md), [rendering constraints](../../OptionalMods/HDRHtmlFrontend/Docs/Rendering.md) and [camera providers](Cameras-and-Portals.md). Query readiness and capability reasons at the backend boundary; an accepted layout alone does not prove that every chosen primitive or viewer is available.
