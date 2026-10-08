# Local HTML frontend API

**ALPHA.** `HDR.Html/0.1` is a client-local source frontend for `HDR.HTML/Profile1`. It is not a browser engine: it has no JavaScript, browser DOM, network loading, iframe, canvas, WebGL or arbitrary HTML event handlers. Unsupported markup, CSS and paint features are reported.

Copy [`HdrHtmlApi.cs`](../../../Api/Mods/HdrHtmlApi.cs) into the consumer mod. The optional frontend itself needs the HDR API world mod for its vector/SVG backends. No client plugin is needed for those paths. Native LCD sprites are an explicitly selected alternative with their own measured font and feature limits.

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
| `create-lcd` | HTML string, CSS string, `Sandbox.ModAPI.Ingame.IMyTextSurface`, `bool callerOwnsSurface` | `long` handle |
| `replace` | handle, HTML string, CSS string, width, height | `bool` accepted layout |
| `data` | handle, binding key, plain string | `bool` queued change |
| `text` | handle, node ID, plain string | `bool` queued change |
| `resize` | handle, width, height | `bool` queued change |
| `status` | handle | status tuple below |
| `destroy` | handle | `true` |
| `pointer` | handle, logical X, logical Y, held Boolean | `true` |
| `pointer-cancel` | handle | `true` |
| `poll-events` | handle | event tuple array below |

Logical coordinates have a top-left origin and Y points down. A world pose places that top-left at its translation: local +X is right, local −Y is down. HUD units are pixels. World metres per pixel must be finite and between 0.000001 and 1000. Native LCD layout dimensions match the surface's actual `SurfaceSize`.

`replace` succeeds only after parsing and layout succeed. Its painter may still reject the new frame; inspect status to confirm visible output. Plain text and binding updates coalesce into one layout in the next update. An unchanged document does not parse or lay out every frame. Per-document input always refers to the last successfully committed visible frame, even when a newer candidate fails. Failed renderer mutations restore owned prior output when possible; if restoration fails, the context and its input are retired.

Parser/layout defaults allow 131072 combined HTML/CSS characters, 512 nodes, depth 32, 256 CSS rules, 2048 declarations, 16384 text characters, 512 paint operations and 64 hit regions. A document admits at most 128 binding keys and 128 queued events. The selected HDR painter must also fit the renderer's actual owner/item/point/primitive limits; these are explicit errors, not silent quality reduction.

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

The native painter currently supports solid rectangles and measured Debug text, including the profile's rectangular range control. Authored rounded corners, strokes, images and SVG assets are explicit unsupported errors. A minimal native demo is supplied separately. This route still uses vanilla LCD texture size and update behavior. The separate [PB adapter](PB-API.md) runs on the host/server; curved HTML surfaces and plugin raster output remain future frontend work.

## Manual prototype demo

After explicitly adding both world mods, run `/hdrhtml hud vector` or `/hdrhtml world svg` in chat. The world demo is placed three metres in front of the current camera and stays there. `/hdrhtml status` reports backend, desired/visible revision and layout count. `/hdrhtml clear` releases only the demo owner.

Startup merely registers the service; it never draws a demo automatically. These commands do not capture game input, modify a programmable block, change world layout or change Pulsar preferences. For interactive use, connect a consumer-owned GUI through the example adapter rather than polling game mouse state.
