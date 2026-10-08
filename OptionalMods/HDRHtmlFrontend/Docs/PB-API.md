# Programmable-block HTML API

**ALPHA, frontend 0.2.2; requires HDR API 0.9.11+ / scene 20.** The **`HDR.Html`** terminal property provides an authenticated, server-owned HTML document adapter for LCD, Console and Projector targets. Its command version stays **`HDR.HtmlPB/0.1`**. It is independent of the client-local [`HdrHtmlApi`](Local-API.md) service and implements the same bounded [Profile1 authoring subset](Profile.md), with additional block-renderer limits. It is not a browser engine and has no JavaScript, Canvas, web-resource fetch or executable HTML event handlers.

Frontend 0.2.2's atomic `mutate`, private script-claim lifecycle and exact `source-check` are **client-local mod API** commands. They support the separately enabled [JavaScript Runtime](../../../docs/wiki/JavaScript-Prototype.md); they are not new `HDR.HtmlPB/0.1` commands and do not execute or replicate JS for a PB. The parser still rejects `<script>`/inline handlers, and `data-action` remains event data. Core 0.9.11, scene 20 and existing native renderer requirements remain unchanged.

## Plain HTML authoring

Use the small [`HtmlPbDemo.cs`](../Examples/HtmlPbDemo.cs) and place [`PlainHtmlPbDemo.html`](../Examples/PlainHtmlPbDemo.html) in the **PB's Custom Data**. Name an authorized target `HTML Display` and run with no argument or `demo`. The mod owns mounting, logical aspect/centering, range-data synchronization, watching and cleanup. The [tutorial](PB-Tutorial.md) is the normal user path; low-level APIs and the [bindings/controller example](../Examples/HtmlPbBindingsDemo.cs) below are optional advanced integration.

The additive command is:

```csharp
string result = (string)Html("run", "HTML Display", argument ?? "");
Echo(result);
```

`run` takes an exact target-name string and optional command string (default empty), returning a **printable string** rather than a document handle. Commands are empty/`demo`/`mount`, `reload`, `status` and `clear`. Empty/`demo`/`mount` deliberately mount or idempotently refresh one authoring document per PB. `reload` forces a reattempt of the current source. `status` reports selected target name/kind, published revision, pending state and current errors; `clear` affects only this automatic document, preserving unrelated low-level documents.

Source comes from the actual caller's `CustomData`, with full HTML and embedded CSS accepted only within Profile1. Default authored viewport is **480×280** logical pixels at **.005 m/px**, centred. Physical LCD canvas matches actual panel aspect with letterbox space; selecting its HDR API text-surface script remains manual. Target resolution requires exactly one registered, working accessible matching block on the PB's construct; the adapter does not rename blocks or choose among ambiguous duplicate names.

The mod watches source every **30 simulation ticks** and caches unchanged/failed attempts. Invalid edits preserve the last published frame and retain a separate source error until corrected; `reload` can force a retry. PB program/power change or target loss retires the automatic mount; it does not attach a replacement automatically. Run again explicitly after recovery. Automatic range synchronization updates HTML `data-bind` values locally in the document; no game action or PB variable is inferred from labels/`data-action`.

This command does not add JS execution for PBs, new CSS/browser syntax or new renderer capabilities. Existing display/input/plugin admission remains in force. Server publication is not proof that a viewer sees the output.

The server parses/measures/layouts source and dirty edits. `bind` publishes bounded Inter SVG artwork and controls through core `HDR.Draw`/`HDR.UI`; existing scene replication and client renderers present it. `bind-screen` publishes the same document into an existing owned projected screen of any of the five surface kinds. `bind-sprites` draws real Debug `MySprite` frames on an explicitly owned physical source LCD, either directly or through the existing LCD-texture world relay; `bind-sprites-screen` maps that actual source into an existing owned screen. HTML/CSS parsing does not run on each viewer or every frame.

| PB backend | Display dependency | Input contract |
| --- | --- | --- |
| `core-svg` (`bind`) | Plugin-free LCD/Console/Projector geometry | Both buttons and ranges require existing Client Renderer 0.9.13+ on the interacting viewer |
| `core-svg` (`bind-screen`) | Existing owned plane/cylinder/sphere/ellipsoid/mesh; ordinary SVG geometry plugin-free, camera/provider display follows provider dependency | Automatic persistent mouse controls require Client Renderer 0.9.13+ on the interacting viewer; Core UI resolves the actual declared surface |
| `sprites-lcd` (`bind-sprites`, anchor = source LCD) | Plugin-free actual native LCD | Cooperative supplied pointer coordinates; no automatic core persistent input |
| `sprites-relay` (`bind-sprites`, Console/Projector anchor) | Existing Client Renderer 0.9.13+ on **every viewer**, plus an owned physical source LCD | Cooperative supplied pointer coordinates; no automatic core persistent input |
| `sprites-relay` (`bind-sprites-screen`) | Existing owned screen of any of the five shapes; Client Renderer 0.9.13+ on **every viewer**, plus an owned physical source LCD | Cooperative supplied pointer coordinates; opaque LCD backing constrains source overlap |

No new renderer plugin or DLL update is required. The texture relay uses the actual native LCD source and its bounded resolution/update behavior; it is not an off-screen browser rasterizer.

## Bind the property

Use this small forwarding method inside the PB's `Program` class:

```csharp
Func<string, object[], object> _html;
object Html(string operation, params object[] arguments)
{
    if (_html == null)
    {
        var property = Me.GetProperty("HDR.Html");
        if (property == null) throw new Exception("Requires HDR HTML frontend.");
        _html = property.As<Func<string, object[], object>>().GetValue(Me);
    }
    return _html(operation, arguments);
}
```

`Html("version")` returns `HDR.HtmlPB/0.1`, a command schema string rather than a package or scene-protocol version. All crossings use standard CLR values, game types and `VRage.MyTuple` values. Client-local mod handles/endpoints and internal frontend classes are not accepted by the PB route. The optional typed [`HdrHtmlIngameApi.cs`](../../../Api/Ingame/HdrHtmlIngameApi.cs) helper can instead be pasted inside `Program` with standard PB imports plus `Sandbox.ModAPI.Interfaces`; the complete demo uses raw forwarding and needs no helper append.

Resolve an actual block, not a display-name string:

```csharp
var target = GridTerminalSystem.GetBlockWithName("HTML Display");
if (target == null) throw new Exception("Name a dedicated HDR display HTML Display.");
double width = 480, height = 280, units = .005;
MatrixD topLeft = MatrixD.CreateTranslation(-width * units / 2, height * units / 2, 0);
long document = (long)Html("bind", target, html, css, width, height, topLeft, units);
```

Core admission enforces caller/target identity and access. The PB must be registered, enabled, working and owned; the target must be working, on the same construct and accessible to that owner. A physical drawing target must be an admitted LCD panel; embedded text surfaces are not interchangeable with the client-local `IMyTextSurface` sprite route. See [core PB capabilities](../../../docs/wiki/Programmable-Blocks.md) for LCD/Console/Projector structural differences.

## Document commands

| Command | Arguments | Meaning |
| --- | --- | --- |
| `version` | None | `HDR.HtmlPB/0.1` |
| `valid` | None | Whether this authenticated endpoint is current |
| `capabilities` | None | Detached string array of actual supported profile/adapter capabilities |
| `run` | Exact target-name string, optional command string (default empty) | Printable string for this PB's mod-owned Custom Data authoring mount; commands above |
| `bind` | Target block, HTML string, CSS string, logical width, logical height, `MatrixD` model pose, optional metres-per-pixel (default `.005`) | Create an owned document; returns `long` handle |
| `bind-screen` | Target block, existing owned screen ID, HTML string, CSS string, logical width, logical height, `MatrixD` canvas pose, optional metres-per-pixel (default `.005`) | Bind core SVG/control content to that screen; pose is document centre in canvas metres; returns `long` handle |
| `bind-sprites` | Anchor block, source LCD block, HTML string, CSS string, `MatrixD` model pose, optional metres-per-pixel (default `.005`), surface index (default `0`), caller-owns-surface Boolean (default `false`) | Create actual native LCD sprites, directly or relayed; returns `long` handle; explicit ownership `true` required |
| `bind-sprites-screen` | Anchor block, existing owned screen ID, source LCD block, HTML string, CSS string, `MatrixD` canvas pose, metres-per-pixel, surface index, caller-owns-surface Boolean | Map actual native sprites into that screen; centred pose, physical surface 0, explicit ownership `true`; returns `long` handle |
| `attach-source` | Handle, explicit block node ID, provider ID, source ID, optional `MyTuple<string,object[]>[]` settings | Boolean synchronous accepted publication of generic source attachment; acquisition can remain pending |
| `detach-source` | Handle, node ID | Boolean accepted removal of that attachment |
| `source-status` | Handle, node ID | `MyTuple<bool,string>` ready and actual state/reason |
| `source-capabilities` | Handle | Detached string array of this document/backend's actual source capabilities/reasons |
| `replace` | Handle, HTML string, CSS string, logical width, logical height | Boolean source/layout acceptance for a bounded candidate |
| `data` | Handle, binding key, plain string | Boolean changed/queued result for plain binding data |
| `text` | Handle, node ID, plain string | Boolean changed/queued result for plain text, without parsing HTML |
| `resize` | Handle, logical width, logical height | Boolean changed/queued result for viewport dimensions |
| `status` | Handle | Server publication tuple below |
| `backend` | Handle | Target kind, selected backend, font profile and relay-plugin requirement tuple below |
| `pointer` | Handle, source-logical X, source-logical Y, held Boolean | Cooperative input for sprite documents only; returns `true` |
| `pointer-cancel` | Handle | Cancel supplied sprite input on focus/provider loss; returns `true` |
| `poll-events` | Handle | Owned click/change event tuples below |
| `destroy` | Handle | Remove only this document's declarations; endpoint returns `true` |
| `clear-owned` | None | Remove only this PB endpoint's HTML documents; endpoint returns `true` |

Current capability strings are `HDR.HTML/Profile1`, `server-layout`, `core-replicated-svg`, `owned-projected-screen-binding`, `generic-node-source-slots`, `ordered-source-artwork`, `lcd-console-projector`, `native-lcd-sprites`, `native-sprites-texture-relay`, `native-cooperative-pointer`, `owned-prefix-cleanup`, `filtered-ui-events`, `custom-data-authoring`, `mod-owned-authoring-refresh`, `input-requires-client-renderer-0.9.13`, `relay-requires-client-renderer-0.9.13`, `no-javascript` and `no-hud`. Input-plugin requirements describe the core SVG route; cooperative native input has its separate contract above. Capabilities do not prove a remote viewer's plugin readiness. `valid` checks the endpoint, not every document or viewer. Update calls can return `false` for no change or rejection; inspect status error text. Malformed delegate arguments throw.

Coordinates start at the authored viewport's top left, with +X right and +Y down. The model pose locates that top left relative to the selected display; local +X is right and local −Y is down. Metres per logical pixel must be finite in 0.000001–1000. Core SVG uses packaged Inter cap-height metrics; a 16px font is a capital-height scale, not a browser em box. Sprite layout instead uses the actual source's `SurfaceSize` and measured `Debug:Lcd` font. Leave `font-family` unspecified or select Debug for sprite documents; Inter metrics cannot be substituted.

For **`bind-screen` and `bind-sprites-screen`**, `canvasPose` instead locates the **document centre** in selected-screen canvas metres, with +X right and −Y down. The existing screen keeps its surface, mapping, clip and other owned content. This explicit screen binding survives the adapter's target reselection; selecting a screen through a separate `HDR.Draw` endpoint alone is insufficient.

## Attach a source to a layout node

Use a sized identified block such as `<div id='pov'></div>`. A visible explicit block ID retains a source region even if it emits no background/text paint. Regions carry full content bounds, effective ancestor/own rectangular clip and insertion order. The source paints after that node's background/border and before its children. Clipping crops provider UVs against the original content box without stretching the remaining content.

```csharp
long document = (long)Html("bind-screen", anchor, "htmlcamera",
    html, css, 640.0, 360.0, MatrixD.Identity, .005);
var settings = new VRage.MyTuple<string, object[]>[] {
    new VRage.MyTuple<string, object[]>("refresh", new object[] { 6.0 }),
    new VRage.MyTuple<string, object[]>("panorama", new object[] { 105.0, 8.0, 1.0, 512 }),
    new VRage.MyTuple<string, object[]>("quality", new object[] { "normal" })
};
bool accepted = (bool)Html("attach-source", document, "pov", "camera-panorama", cameraId, settings);
var source = (VRage.MyTuple<bool, string>)Html("source-status", document, "pov");
// Switch only the physical camera ID; keep the same document and controls.
Html("attach-source", document, "pov", "camera-panorama", anotherCameraId, settings);
```

Settings are named tuples: `refresh` takes finite 1–120 Hz; `panorama` takes FOV 60–120 degrees, feather 0–25 degrees, saturation 0–2 and integer capture size 256/512/1024/2048; `quality` takes `normal`/`lite`; `opacity` takes 0–1. Omitted overrides inherit the parent screen's source settings. Each document has at most 16 attachments, subject to the selected screen's shared slot and resource budgets. Camera IDs are positive decimal physical entity IDs, with one to six IDs allowed by `camera-panorama`; they are not camera labels or arbitrary native camera poses.

Attachment is a synchronous declaration commit, with acquisition readiness reported separately: `Detached`, `Hidden`, `Pending`, `Ready`, `RequiresPlugin` or the actual provider reason. Missing/hidden/fully clipped nodes deactivate their slot while retaining desired attachments. `detach-source` releases only that attachment. Query `source-capabilities` for the selected backend; generic core SVG returns `generic-source-slots`, `ordered-artwork`, `any-projected-surface`, `affine-canvas-source-pose`. Native returns `generic-source-slots`, `any-projected-surface`, `native-lcd-rgb-texture`, `source-overlap-with-later-artwork-unsupported`.

Physical LCD output cannot embed external engine textures. Native projected output retains its opaque LCD backing: it rejects source overlap with later visible HTML artwork/controls and partial source alpha. These errors have backend-specific reasons; no silent 128×72 raster fallback is used. The attachment API adds no `<img>`/`<video>`, browser resource loading or CSS group compositing. See [Composition](../../../docs/wiki/Composition.md) and the complete [ellipsoid camera demo](../Examples/HtmlCameraEllipsoidDemo.cs).

Text templates use `{{key}}`, range `data-bind` reads numeric data, and `data-action` supplies event metadata. The PB assigns returned canonical values to its own variables and decides what they mean. Repeating unchanged data does not rebuild layout. Updates coalesce into the server's later simulation update; a successful queue operation does not acknowledge client presentation.

## Server publication status

```csharp
MyTuple<string, long, long, MyTuple<bool, bool, string>, long>
// targetKind, desiredRevision, publishedServerRevision,
// (adapter/sourceReady, pendingPublication, lastError), layoutBuildCount
```

`publishedServerRevision` records the last committed server declarations or native source-frame publication. **It is not the client-local API's visible revision and is not a viewer/GPU acknowledgement.** Clients can receive output later, cull or occlude it, switch rendering off or lack the input/texture provider. A server publication therefore does not prove that any particular player saw or operated the UI. Pending/failed candidates can coexist with prior published output; use the status error and revision fields when diagnosing changes.

Backend inspection uses:

```csharp
MyTuple<string, string, string, bool>
// targetKind, "core-svg" | "sprites-lcd" | "sprites-relay",
// measuredFontProfile, requiresPluginForRelay
```

The final Boolean identifies the sprite **relay display** dependency. It does not report remote plugin registration or remove the core SVG input-plugin requirement.

## Events and script variables

```csharp
MyTuple<string, string, string, MyTuple<double, long, long>>[]
// kind, HTML nodeId, data-action,
// (canonicalValue, backendDependentRevision, playerIdentityId)
```

Buttons emit `click`; ranges emit `change`. Core SVG numeric events carry the core's canonical clamped/quantized value and **core value revision**, with the interacting player's identity ID. Core button events may have zero numeric value/revision. Sprite events instead carry the **published document revision** and player ID **`0`**, because coordinates come from the caller's cooperative provider. These revision meanings are not interchangeable. Neither tuple carries an executable callback.

Poll in a bounded update cadence such as `Update10`, assign the returned value to an actual PB variable, and send back accepted data when it changes. The adapter filters its owned events instead of consuming unrelated UI queues. It never interprets `data-action` as a command, calls `TryRun` or writes gameplay state. There is no implicit reflection-based binding between HTML names and PB fields.

Core SVG mouse interaction uses the existing [HDR persistent control viewer](../../../docs/wiki/Interactive-Controls.md), including its Use/focus/input-cancellation rules. Both button and range interaction require **Client Renderer 0.9.13+ on that viewer**. Sprite documents accept `pointer` only from a caller-owned GUI/touch/eye provider, or explicit synthetic test coordinates; the PB must already own/block corresponding game input. Supply source `SurfaceSize` pixels and call `pointer-cancel` on focus/provider loss. Sprite documents do not automatically use the core persistent viewer or acquire global mouse buttons. HTML adds no full-screen menu or plugin-manager changes. Offline compilation does not prove live input or multiplayer acceptance.

## Native source LCD and world relay

Resolve a real physical LCD source and explicitly promise ownership:

```csharp
var source = GridTerminalSystem.GetBlockWithName("HTML Sprite Source") as IMyTextPanel;
if (source == null) throw new Exception("Requires the dedicated source LCD.");
Vector2 size = source.SurfaceSize;
MatrixD topLeft = MatrixD.CreateTranslation(-size.X * .005 / 2, size.Y * .005 / 2, 0);
long document = (long)Html("bind-sprites", anchor, source, html, css, topLeft, .005, 0, true);
```

Set `anchor = source` for direct physical LCD output. For world output, use an actual admitted Console/Projector anchor such as `HTML Display`. The source remains a real owned LCD: there is no virtual LCD creation or synthetic 128×72 fallback. Only physical surface index **0** is supported. The typed helper's `BindSprites` arguments explicitly include metres-per-pixel, index and ownership; callers must pass `true` themselves.

Manually configure the source as **`ContentType.SCRIPT` with no selected text-surface script (NONE)** before binding. This differs from core SVG's **Content → HDR API** requirement. Binding changes neither the source mode nor its background. Both source and anchor must remain live, working, on the caller's construct and accessible to its owner. A shared same-process claim prevents competing PB and client-local native HTML documents from owning the same surface. It cannot detect arbitrary external writers or claims in another client process; exclusive ownership remains the caller's responsibility.

Sprite viewport dimensions match actual `SurfaceSize`; native text uses the target's Debug measurements and conservative ink proxy. Source texture padding is retained. Replacement/resizing must continue to match the actual surface; it is not a free-sized off-screen canvas. Solid rectangles, measured text and rectangular scissor clipping are supported, including square button/range artwork. Rounded corners, stroke paint operations, images and SVG assets fail.

World output creates an owned core projected-screen relay using the existing **`lcd-texture`** source. `bind-sprites-screen` instead places its owned relay slot into a preexisting owned screen without changing that screen's shape, supporting plane/cylinder/sphere/ellipsoid/mesh. **Every viewer needs HDR Client Renderer 0.9.13+**, with its normal source resolution/update/resource limits. The relay preserves the real texture's dimensions/aspect, including padding and provider UV crop, and remains bounded by vanilla LCD rendering and the existing provider. Native texture RGB is **opaque**: transparent HTML canvas pixels and per-pixel alpha cannot be recovered from that source. Source background behavior remains the game's actual LCD behavior. Whole-screen/slot opacity can affect the complete mapped backing; it cannot provide missing per-pixel alpha.

## Target, lifetime and ownership

A physical **core SVG** LCD must already have **Content → HDR API** selected manually. Low-level `bind` does not change physical content mode, select a text-surface script, configure the core LCD canvas, select a renderer or change global core view/budget settings. Configure the logical LCD canvas explicitly when using that advanced path, as the [bindings/controller demo](../Examples/HtmlPbBindingsDemo.cs) does on `demo`. The plain `run` mount handles logical aspect/centering in the mod while retaining the same manual physical script fence. A square surface therefore does not stretch a wide authoring viewport. Core SVG LCD output follows the core native/calibrated-vector backend's texture, cadence and model limits and uses Inter triangles in either renderer. The separate sprite source uses the SCRIPT/NONE/Debug contract above.

PB source (`SourceProgramData`), caller generation/lifetime, loss of power/access/construct membership and target retirement invalidate owned documents before automatic redraw. Stale endpoints/handles fail closed. Reacquire the current property and bind deliberately after recompilation, world reload or target retirement; the example starts with no automatic update and requires `demo`. It does not persist a handle as proof of ownership across these changes.

Cleanup removes only this adapter's prefixed artwork, controls, range values and UI bundle, or its owned native publication and child relay. Native cleanup follows its source-ownership/content-mode guard. It does not call whole-scene `HDR.Draw clear` or `HDR.UI clear`. Other PB declarations and non-HTML UI events remain owned by their existing callers. Keep target names unique and choose dedicated displays for the examples.

## Rendering limits and example

Profile1's bounded parser/layout limits apply; core SVG additionally fits selected core display budgets. The PB service admits at most 16 owners, four documents per PB and 16 documents overall. Each PB document admits 32 hit regions and 128 binding keys. The core SVG publisher accepts rectangle/text paint, groups contiguous artwork into items of at most 65536 SVG characters each, and creates one owned UI bundle with at most 32 artwork layers. Ordered publication supports body/control interleaving on both `bind` and `bind-screen`; source attachment still requires explicit projected binding. Core SVG ranges require a visible knob with an unclipped positive travel path. Nested buttons and a range inside a button remain rejected. Sprite output instead fits bounded native operation/sprite and source-texture limits, including the native overlap restriction above. Unsupported features or exceeded grants are errors; detail is not silently reduced.

Core SVG artwork layers preserve contiguous paint order, including interleaved body/control artwork. Core SVG Console/Projector output uses existing world billboards and range/view/table envelopes. `bind-screen` uses an ordered canvas with source slots and the actual declared surface for controls; supported source/artwork paint order does not establish browser CSS group compositing. Mapped native sprites retain the opaque backing and overlap restrictions above. An off-screen HTML/browser rasterizer and arbitrary HTML assets remain unimplemented. See [Rendering](Rendering.md) for backend tradeoffs and honest offline CPU evidence.

The primary [`HtmlPbDemo.cs`](../Examples/HtmlPbDemo.cs) only invokes `run`; UI markup comes from [`PlainHtmlPbDemo.html`](../Examples/PlainHtmlPbDemo.html) in its Custom Data. The separate [`HtmlPbBindingsDemo.cs`](../Examples/HtmlPbBindingsDemo.cs) is the advanced controller example with raw delegate forwarding. It authors a 480×280 panel, level range and reset button. `demo` creates it; `set 60` updates `_level` and the data binding; `status` prints server publication; `clear` destroys that demo handle. Its `Update10` loop polls events and sends new data only when the level changes. It performs no terminal actions, block renames, content-mode changes or gameplay writes.

[`HtmlPbSpritesDemo.cs`](../Examples/HtmlPbSpritesDemo.cs) is a separate whole PB script for actual Debug sprites. Name its source **`HTML Sprite Source`** and configure SCRIPT/NONE manually. `demo` or `lcd` draws directly; `world` relays to Console/Projector **`HTML Display`**; `status`/`clear` inspect/release its owned document. Its demo layout needs at least a 192×128 source surface. `set 60` changes `_level`; `press X Y`, `release X Y` and `cancel` are explicit synthetic coordinate tests entered through the PB terminal. A real provider must own its input before forwarding those coordinates. No game mouse polling, automatic mode/background writes or gameplay actions occur.

[`HtmlCameraEllipsoidDemo.cs`](../Examples/HtmlCameraEllipsoidDemo.cs) is the complete `bind-screen`/`attach-source` example. It requires Console/Projector **`HTML Display`** and physical **`HDR Camera 1`** / **`HDR Camera 2`**. `demo` creates a bounded ellipsoid patch, centres the HTML canvas and attaches a single camera ID to `pov`. HTML Previous/Next clicks polled in `Update10` switch only that source ID; `previous`/`next` terminal commands exercise the same switch. `status` preserves actual source reasons; `clear` removes only its owned document/screen. The current renderer is required for camera capture and automatic persistent pointer controls. Server/offline evidence does not prove live GPU, input or multiplayer acceptance.
