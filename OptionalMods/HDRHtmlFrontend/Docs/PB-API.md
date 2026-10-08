# Programmable-block HTML API

**ALPHA, frontend 0.1.1; requires HDR API 0.9.10+.** The **`HDR.Html`** terminal property provides an authenticated, server-owned HTML document adapter for LCD, Console and Projector targets. Its command version is **`HDR.HtmlPB/0.1`**. It is independent of the client-local [`HdrHtmlApi`](Local-API.md) service and implements the same bounded [Profile1 authoring subset](Profile.md), with additional block-renderer limits. It is not a browser engine and has no JavaScript, Canvas, web-resource fetch or executable HTML event handlers.

The server parses/measures/layouts source and dirty edits. `bind` publishes bounded Inter SVG artwork and controls through core `HDR.Draw`/`HDR.UI`; existing scene replication and client renderers present it. `bind-sprites` draws real Debug `MySprite` frames on an explicitly owned physical source LCD, either directly or through the existing LCD-texture world relay. HTML/CSS parsing does not run on each viewer or every frame.

| PB backend | Display dependency | Input contract |
| --- | --- | --- |
| `core-svg` (`bind`) | Plugin-free LCD/Console/Projector geometry | Both buttons and ranges require existing Client Renderer 0.9.13+ on the interacting viewer |
| `sprites-lcd` (`bind-sprites`, anchor = source LCD) | Plugin-free actual native LCD | Cooperative supplied pointer coordinates; no automatic core persistent input |
| `sprites-relay` (`bind-sprites`, Console/Projector anchor) | Existing Client Renderer 0.9.13+ on **every viewer**, plus an owned physical source LCD | Cooperative supplied pointer coordinates; no automatic core persistent input |

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
| `bind` | Target block, HTML string, CSS string, logical width, logical height, `MatrixD` model pose, optional metres-per-pixel (default `.005`) | Create an owned document; returns `long` handle |
| `bind-sprites` | Anchor block, source LCD block, HTML string, CSS string, `MatrixD` model pose, optional metres-per-pixel (default `.005`), surface index (default `0`), caller-owns-surface Boolean (default `false`) | Create actual native LCD sprites, directly or relayed; returns `long` handle; explicit ownership `true` required |
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

Current capability strings are `HDR.HTML/Profile1`, `server-layout`, `core-replicated-svg`, `lcd-console-projector`, `native-lcd-sprites`, `native-sprites-texture-relay`, `native-cooperative-pointer`, `owned-prefix-cleanup`, `filtered-ui-events`, `input-requires-client-renderer-0.9.13`, `relay-requires-client-renderer-0.9.13`, `no-javascript`, `no-hud` and `no-curved-pb-html`. Input-plugin requirements describe the core SVG route; cooperative native input has its separate contract above. Capabilities do not prove a remote viewer's plugin readiness. `valid` checks the endpoint, not every document or viewer. Update calls can return `false` for no change or rejection; inspect status error text. Malformed delegate arguments throw.

Coordinates start at the authored viewport's top left, with +X right and +Y down. The model pose locates that top left relative to the selected display; local +X is right and local −Y is down. Metres per logical pixel must be finite in 0.000001–1000. Core SVG uses packaged Inter cap-height metrics; a 16px font is a capital-height scale, not a browser em box. Sprite layout instead uses the actual source's `SurfaceSize` and measured `Debug:Lcd` font. Leave `font-family` unspecified or select Debug for sprite documents; Inter metrics cannot be substituted.

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

World output creates only an owned core projected-screen relay using the existing **`lcd-texture`** source. **Every viewer needs existing HDR Client Renderer 0.9.13+**, with its normal source resolution/update/resource limits. The relay preserves the real texture's dimensions/aspect, including padding, and remains bounded by vanilla LCD rendering and the existing provider. Native texture RGB is **opaque**: transparent HTML canvas pixels and per-pixel alpha cannot be recovered from that source. Source background behavior remains the game's actual LCD behavior. Existing whole-projection opacity can affect the full plane; no new alpha shader or opacity API is added by this frontend.

## Target, lifetime and ownership

A physical **core SVG** LCD must already have **Content → HDR API** selected manually. `bind` does not change physical content mode, select a text-surface script, configure the core LCD canvas, select a renderer or change global core view/budget settings. Configure the logical LCD canvas explicitly when needed, as the [core SVG demo](../Examples/HtmlPbDemo.cs) does on `demo`: it reads actual `SurfaceSize`, matches the core canvas aspect and fits its 480×280 authored UI with letterbox space. A square surface therefore does not stretch a wide authoring viewport. Core SVG LCD output follows the core native/calibrated-vector backend's texture, cadence and model limits and uses Inter triangles in either renderer. The separate sprite source uses the SCRIPT/NONE/Debug contract above.

PB source (`SourceProgramData`), caller generation/lifetime, loss of power/access/construct membership and target retirement invalidate owned documents before automatic redraw. Stale endpoints/handles fail closed. Reacquire the current property and bind deliberately after recompilation, world reload or target retirement; the example starts with no automatic update and requires `demo`. It does not persist a handle as proof of ownership across these changes.

Cleanup removes only this adapter's prefixed artwork, controls, range values and UI bundle, or its owned native publication and child relay. Native cleanup follows its source-ownership/content-mode guard. It does not call whole-scene `HDR.Draw clear` or `HDR.UI clear`. Other PB declarations and non-HTML UI events remain owned by their existing callers. Keep target names unique and choose dedicated displays for the examples.

## Rendering limits and example

Profile1's bounded parser/layout limits apply; core SVG additionally fits selected core display budgets. The PB service admits at most 16 owners, four documents per PB and 16 documents overall. Each PB document admits 32 hit regions and 128 binding keys. The core SVG publisher accepts rectangle/text paint, groups body and interactive artwork into items of at most 65536 SVG characters each, and creates one owned UI bundle layer. Core SVG ranges require a visible knob with an unclipped positive travel path. Nested buttons, a range inside a button and body/control overlap needing interleaved paint are rejected on that route. Sprite output instead fits bounded native operation/sprite and source-texture limits. Unsupported features or exceeded grants are errors; detail is not silently reduced.

For core SVG LCDs, ordered item IDs on the one bundle layer determine declaration ordering. Core SVG Console/Projector output uses existing world billboards and range/view/table envelopes. Flat artwork and the authored viewing side are the supported convention; browser CSS compositing/depth is not guaranteed. The sprite relay uses the existing projected texture plane with the opacity/resolution limits above. Curved HTML, an off-screen HTML/browser rasterizer and arbitrary HTML assets remain unimplemented. See [Rendering](Rendering.md) for backend tradeoffs and honest offline CPU evidence.

[`HtmlPbDemo.cs`](../Examples/HtmlPbDemo.cs) is a whole PB script with raw delegate forwarding. It authors a 480×280 panel, level range and reset button. `demo` creates it; `set 60` updates `_level` and the data binding; `status` prints server publication; `clear` destroys that demo handle. The `Update10` loop polls events and sends new data only when the level changes. The demo performs no terminal actions, block renames, content-mode changes or gameplay writes.

[`HtmlPbSpritesDemo.cs`](../Examples/HtmlPbSpritesDemo.cs) is a separate whole PB script for actual Debug sprites. Name its source **`HTML Sprite Source`** and configure SCRIPT/NONE manually. `demo` or `lcd` draws directly; `world` relays to Console/Projector **`HTML Display`**; `status`/`clear` inspect/release its owned document. Its demo layout needs at least a 192×128 source surface. `set 60` changes `_level`; `press X Y`, `release X Y` and `cancel` are explicit synthetic coordinate tests entered through the PB terminal. A real provider must own its input before forwarding those coordinates. No game mouse polling, automatic mode/background writes or gameplay actions occur.
