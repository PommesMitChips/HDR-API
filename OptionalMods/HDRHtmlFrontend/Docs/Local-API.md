# Local HTML frontend API

**ALPHA — frontend 0.2.2, core 0.9.11 / scene 20.** `HDR.Html/0.1` is a client-local source frontend for `HDR.HTML/Profile1`. It is not a browser engine: it embeds no JavaScript engine, browser DOM, network loading, iframe, canvas, WebGL or arbitrary HTML event handlers. The separate [JavaScript Runtime 0.1.0](../../HDRJavaScriptRuntime/README.md) can bind these local retained documents explicitly. The parser still rejects `<script>` and `on*` attributes. Unsupported markup, CSS and paint features are reported.

Copy [`HdrHtmlApi.cs`](../../../Api/Mods/HdrHtmlApi.cs) into the consumer mod. Vector/SVG geometry needs the core world mod and no client plugin. Native source attachments require actual block authority and explicit local-consumer provider negotiation; camera/LCD/portal sources use **Client Renderer 0.9.14**. The separate PB camera/persistent pointer route remains compatible with **0.9.13+**. Native LCD sprites are an explicitly selected alternative with their own measured font and feature limits.

Call discovery and document methods on the client simulation thread outside rendering callbacks. The service uses local message channels **481770150** (discovery) and **481770151** (request), with `Func<string, object[], object>` endpoints. Only standard CLR values, game types and `VRage.MyTuple` values cross the delegate boundary; consumers do not exchange frontend-specific CLR classes.

## Ownership and lifecycle

`version()` returns `HDR.Html/0.1`. `capabilities()` returns actual profile and backend strings. `open(ownerId)` returns a generation-bound owner endpoint. IDs contain 1–48 letters, digits, periods, underscores or hyphens. Opening the same ID replaces and releases that owner's prior documents. Use a unique stable mod-specific ID.

The endpoint supports `valid()`, `release()` and `clear-owned()`. Both release methods affect only that frontend owner's documents. The frontend owns a separate HDR endpoint; it never clears the core scene or another mod's contexts. There are at most eight owners and four documents per owner. Document handles are session-wide monotonic integers, so a handle cannot address a replacement owner's different document.

Consumers must rebuild their documents after `ConnectionGeneration` changes. Stale endpoints fail closed. HDR endpoint replacement invalidates vector/SVG input until owned output is rebuilt. On world unload the service unregisters and releases its contexts. Dedicated servers do not register the client service.

Only one live frontend document may own the same native LCD surface instance, including across different owners. Destroy or release it before reusing the target. This prevents conflicts between frontend documents; it cannot detect arbitrary external sprite writers. Explicit surface ownership remains the consumer's responsibility.

## Document methods

The SDK wraps these exact endpoint calls:

| Method | Arguments | Result |
| --- | --- | --- |
| `create-hud` | HTML string, CSS string, width, height, `vector` or `svg`, optional integer order | `long` handle |
| `create-world` | HTML string, CSS string, width, height, `MatrixD` pose, metres per pixel, `vector` or `svg`, optional integer order | `long` handle |
| `create-surface` | HTML string, CSS string, width, height, `MatrixD` world pose, metres per pixel, surface kind, `object[]` surface parameters, `MyTuple<string,object[]>[]` settings, `vector` or `svg`, integer order | `long` handle |
| `create-lcd` | HTML string, CSS string, `Sandbox.ModAPI.Ingame.IMyTextSurface`, `bool callerOwnsSurface` | `long` handle |
| `geometry-limit` | Optional point and primitive Int32 counts, each default `0`; no document handle | `true`; SDK `GeometryLimit` returns void |
| `geometry-limit-settings` | None; no document handle | `MyTuple<int,int>` owner point/primitive allowances |
| `replace` | handle, HTML string, CSS string, width, height | `bool` accepted layout |
| `data` | handle, binding key, plain string | `bool` queued change |
| `text` | handle, node ID, plain string | `bool` queued change |
| `resize` | handle, width, height | `bool` queued change |
| `status` | handle | status tuple below |
| `destroy` | handle | `true` |
| `pointer` | handle, logical X, logical Y, held Boolean | `true` |
| `pointer-ray` | handle, world-space `Vector3D` origin, direction, held Boolean | `true`; cooperative input on the committed world plane/surface |
| `pointer-cancel` | handle | `true` |
| `source-anchor` | handle, actual `Sandbox.ModAPI.Ingame.IMyTerminalBlock` anchor | `true`; SDK `SetSourceAnchor` returns void |
| `attach-source` | handle, explicit block node ID, provider, source ID, optional `MyTuple<string,object[]>[]` settings | Boolean accepted source publication |
| `detach-source` | handle, node ID | Boolean accepted removal |
| `source-status` | handle, node ID | `MyTuple<bool,string>` readiness and actual reason |
| `source-capabilities` | handle | Detached `string[]` of actual supported capabilities/reasons |
| `source-check` | handle, exact provider ID, exact source ID | `MyTuple<bool,string>` read-only consumer/provider admission and actual reason; no frame acquisition/attachment |
| `mutate` | handle, `MyTuple<string,object[]>[]` operations | Boolean accepted atomic retained publication; schema below |
| `script-claim` | handle, private non-null C# token object | Boolean exclusive script association for this actual owned document |
| `script-valid` | handle, same token | Boolean current matching claim |
| `script-release` | handle, same token | Boolean matching cleanup; a foreign token cannot release it |
| `poll-events` | handle | event tuple array below |

Logical coordinates have a top-left origin and Y points down. A world pose places that top-left at its translation: local +X is right, local −Y is down. HUD units are pixels. World metres per pixel must be finite and between 0.000001 and 1000. Native LCD layout dimensions match the surface's actual `SurfaceSize`.

`CreateWorld` retains this top-left convention across resize, using the shared plane mapper for provider sources and `PointerRay`. `CreateSurface` instead uses a centred world surface pose and a centred canvas of `width * metresPerPixel` by `height * metresPerPixel`. It accepts plane/cylinder/sphere/ellipsoid/authored mesh parameters matching the core mapper. Named settings are `anchor` with an actual block; `mapping` with angular/geodesic/pinhole and optional vertical FOV/source aspect; `sided` with two-sided/front/back opacity; and `error` with chord error in metres. Both `vector` and `svg` are supported. See the [exact composition calls](../../../docs/wiki/Composition.md#compose-in-a-client-local-mod-context).

`replace` succeeds only after parsing and layout succeed. Its painter may still reject the new frame; inspect status to confirm visible output. Plain text and binding updates coalesce into one layout in the next update. An unchanged document does not parse or lay out every frame. Per-document input always refers to the last successfully committed visible frame, even when a newer candidate fails. Failed renderer mutations restore owned prior output when possible; if restoration fails, the context and its input are retired.

Parser/layout defaults allow 131072 combined HTML/CSS characters, 512 nodes, depth 32, 256 CSS rules, 2048 declarations, 16384 text characters, 512 paint operations and 64 hit regions. A document admits at most 128 binding keys and 128 queued events. The selected HDR painter must also fit the renderer's actual owner/item/point/primitive limits; these are explicit errors, not silent quality reduction.

Aggregate point/primitive allowance defaults to **0 = unlimited**, independently in `HtmlPainterLimits.MaxPoints` / `MaxPrimitives` and the underlying core owner. Positive configured painter allowances still apply; there is no fixed 8,192 aggregate grant. Exact preflight queries `geometry-limit-settings` → `MyTuple<int,int>` and checks actual owner usage before renderer mutation. `HdrHtmlApi.GeometryLimit(points=0, primitives=0)` / `GeometrySettings()` forward the owner-level core calls, with no document handle, across this hosted owner's HUD/world/surface contexts. A live core owner is required. Negative counts fail; local Int32.MaxValue remains a positive finite value and is returned unchanged. Accepted finite settings retire cached source frames exceeding the allowance and preserve desired slots; a finite configuration below existing ordinary/mapped artwork rejects atomically. Genuine physical native LCD output retains its actual sprite/payload limits. Per-item/operation limits and the separate local-owner 4,096 / global-mod 20,000 frame-work defaults remain in force.

## Provider source attachments

Existing HUD and world documents keep their original constructors:

```csharp
long document = html.CreateWorld(markup, css, 640, 360, topLeftPose, .005, "svg", 0);
html.SetSourceAnchor(document, actualBlock);
bool attached = html.AttachSource(document, "pov", "camera-panorama", cameraId);
html.PointerRay(document, ownedRayOrigin, ownedRayDirection, held);
var source = html.SourceStatus(document, "pov");
```

For a HUD, use `CreateHud`, the same `SetSourceAnchor`/`AttachSource` calls and existing cooperative `Pointer` pixels. HUD sources retain top-left Y-down pixel bounds/clip and flat HUD mapping. `CreateSurface` can set its initial anchor through settings or later `SetSourceAnchor`. Only genuine physical `CreateNativeLcd` rejects external engine texture embedding; use an HDR HUD/world/surface renderer for provider images.

Attachments use explicit visible block IDs, retain full node content bounds and effective rectangular clip, and insert after node background/border before children. Clipped provider UVs crop without stretching. The source hint schema matches the [PB attachment API](PB-API.md#attach-a-source-to-a-layout-node): refresh, panorama, quality and opacity. Admission checks actual anchor validity and explicitly negotiated local-consumer provider capability before changing accepted declarations. Native providers need Client Renderer 0.9.14; missing/unsupported service returns its actual reason. Readiness is separate from declaration acceptance, visible document revision and viewer/GPU acceptance. Detach/replacement retires only the appropriate owned source consumer.

`source-check(handle, provider, sourceId)` tests that exact selected consumer/provider identity without acquiring a frame or changing the document. A negotiated mod-native provider does not inherit a universal renderer-plugin requirement. An absent native provider returns its specific `Requires plugin: ...` reason; an installed provider lacking the consumer capability returns its actual unsupported reason. Declaration support is not frame/GPU readiness.

## Atomic mutations and document script claims

`HdrHtmlApi.Mutate(document, operations)` supports one retained local HUD/world/general-surface candidate. Each standard tuple names one operation:

| Operation | `object[]` values |
| --- | --- |
| `text` | Node ID string, plain text string |
| `data` | Binding key string, plain value string |
| `attach-source` | Node ID string, provider string, source ID string, optional `MyTuple<string,object[]>[]` hints |
| `detach-source` | Node ID string |

```csharp
bool published = html.Mutate(document, new[] {
    new MyTuple<string,object[]>("text", new object[] { "label", "Forward camera" }),
    new MyTuple<string,object[]>("attach-source", new object[] { "pov", "camera-panorama", cameraId })
});
```

The whole payload is copied/validated before publication: at most **64** operations, at most four values per operation and **131,072** direct string characters. Node/provider/hint validation, queued standalone edits and candidate layout all precede one painter publication. Success commits candidate desired state and the visible frame together. Rejection never splits an oversized batch. A confirmed restore leaves the prior accepted state; uncertain restoration retires visible output/input and gives a precise status error. Physical native-LCD batches reject before writes because they lack a reversible context transaction.

`ClaimScript(document, token)`, `ScriptValid` and `ReleaseScript` reserve an exclusive script association on the actual document, independently of wrapper identity. Use one private non-null C# token and the actual owned endpoint; the token is not serialized or passed into JS. Claim ownership does not replace the frontend endpoint's document authority, and normal C# ownership remains intact. Matching release/valid checks can run during publication; release before/during a guarded mutation prevents its candidate commit and restores or retires prior output safely. Destroy/owner retirement releases the association.

The [separate JS runtime](../../../docs/wiki/JavaScript-Prototype.md) acquires that claim and consumes the document's event queue. Do not also drain `PollEvents` while that bridge owns event dispatch. This frontend itself still executes no JavaScript or event-handler attributes, and the PB adapter does not expose these local batch/claim commands.

## Status and events

Status uses:

```csharp
MyTuple<string, long, long, MyTuple<bool, bool, string>, long>
// backend, desiredRevision, visibleRevision,
// (rendererReady, pendingPaint, lastError), layoutBuildCount
```

An error or pending candidate does not mean the prior visible UI has changed. A visible revision of zero means no committed output is available for input.

Events use:

```csharp
MyTuple<string, string, string, MyTuple<double, long>>[]
// kind, nodeId, data-action string, (numericValue, visibleRevision)
```

`click` is emitted after a button press/release in the same visible button. `change` is emitted for a range input as its value changes; range events coalesce by node. A `data-action` value is descriptive event data. HDR HTML never executes it as code, a game command or a script argument. The owner polls the event and decides what it means. Feed an accepted value back through `SetData` using the authored `data-bind` key.

Input is cooperative. The consumer must already own a GUI/input session which prevents the press from also firing weapons or activating game controls. This service reads no global mouse buttons and captures no input by itself. Supply authored logical pixel coordinates, and cancel on focus loss. Source replacement, viewport changes, incompatible hit-region changes, endpoint loss and `/hdr off` cancel vector/SVG input. The native LCD path is independent of `/hdr off` and remains the explicitly owning consumer's responsibility.

## Native LCD restrictions

Before calling `CreateNativeLcd`, the consumer must explicitly own the supplied target and configure `ContentType.SCRIPT` with no selected text-surface script. The frontend does not claim an arbitrary LCD, rename blocks, change content mode or select a script. It uses the actual surface's `Debug:Lcd` font measurements; the Inter vector profile is not substituted. Use an unspecified font family or the native Debug profile as documented by the layout profile.

The native painter currently supports solid rectangles and measured Debug text, including the profile's rectangular range control. Authored rounded corners, strokes, images and SVG assets are explicit unsupported errors. A minimal native demo is supplied separately. This route still uses vanilla LCD texture size and update behavior and cannot embed external engine source textures. Curved local HTML uses `CreateSurface`; the separate [PB adapter](PB-API.md) runs on the host/server and can map actual native LCD textures through its explicit projected relay.

## Manual prototype demo

After explicitly adding both world mods, run `/hdrhtml hud vector` or `/hdrhtml world svg` in chat. The world demo is placed three metres in front of the current camera and stays there. `/hdrhtml status` reports backend, desired/visible revision and layout count. `/hdrhtml clear` releases only the demo owner.

Startup merely registers the service; it never draws a demo automatically. These commands do not capture game input, modify a programmable block, change world layout or change Pulsar preferences. For interactive use, connect a consumer-owned GUI through the example adapter rather than polling game mouse state.
