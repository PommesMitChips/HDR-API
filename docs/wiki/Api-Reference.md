# API reference

This page indexes public contracts and specifies the mod-facing envelopes. Drawing command tables live alongside their concepts so examples and constraints stay together. All types below are standard/game CLR types; local mod protocols use `VRage.MyTuple`, `VRageMath` vectors/matrices, arrays and delegates.

## Contract identifiers

| Contract | Discovery | Version / shape | Implementation or helper |
|---|---|---|---|
| PB command endpoint | `Me.GetProperty("HDR.Draw")` | `Func<string,object[],object>`; `version` → `HDR.Draw/1` | [DrawCommands.cs](../../Mod/Data/Scripts/HoloMap/DrawCommands.cs), [HdrIngameApi.cs](../../Api/Ingame/HdrIngameApi.cs) |
| PB typed endpoint | `Me.GetProperty("HDR.Api")` | `IReadOnlyDictionary<string,Delegate>`; `ApiVersion()` → `HDR.Api/1` | [HoloMapApi.cs](../../Api/HoloMapApi.cs), [registration](../../Mod/Data/Scripts/HoloMap/HoloMapSession.cs) |
| Block capabilities | Short `capabilities`, typed `GetDisplayCapabilities` | `MyTuple<string,string,int>`; schema `HDR.DisplayCapabilities/1` | [DisplayCapabilities.cs](../../Mod/Data/Scripts/HoloMap/DisplayCapabilities.cs) |
| PB block UI | `Me.GetProperty("HDR.UI")` | `Func<string,object[],object>`; `version` → `HDR.UI/1` | [UiCommands.cs](../../Mod/Data/Scripts/HoloMap/UiCommands.cs) |
| General client-mod rendering | Local message `481770130` / request `481770131` | `Func<string,object[],object>`; `version` → `HDR.ModClient/1` | [HdrModApi.cs](../../Api/Mods/HdrModApi.cs), [ModClientApi.cs](../../Mod/Data/Scripts/HoloMap/ModClientApi.cs) |
| Optional HTML/CSS frontend | Local message `481770150` / request `481770151` | `Func<string,object[],object>`; `version` → `HDR.Html/0.1` | [HdrHtmlApi.cs](../../Api/Mods/HdrHtmlApi.cs), [frontend reference](../../OptionalMods/HDRHtmlFrontend/Docs/Local-API.md) |
| Optional PB HTML/CSS adapter | `Me.GetProperty("HDR.Html")` | `Func<string,object[],object>`; `version` → `HDR.HtmlPB/0.1` | [HdrHtmlIngameApi.cs](../../Api/Ingame/HdrHtmlIngameApi.cs), [PB HTML reference](../../OptionalMods/HDRHtmlFrontend/Docs/PB-API.md) |
| Optional client JavaScript runtime | Local message `481770160` / request `481770161` | `Func<string,object[],object>`; `version` → `HDR.JS/0.1` | [HdrJavaScriptApi.cs](../../Api/Mods/HdrJavaScriptApi.cs), [runtime API](../../OptionalMods/HDRJavaScriptRuntime/Docs/Mod-API.md) |
| Display source | Local `481770100` / registration `481770101` | Registration tuple carries protocol **1** or **2** | [DisplaySources.cs](../../Mod/Data/Scripts/HoloMap/DisplaySources.cs) |
| Raster upload backend | Local `481770110` / registration `481770111` | Registration tuple carries protocol **1** | [RasterBackends.cs](../../Mod/Data/Scripts/HoloMap/RasterBackends.cs) |

Interface versions, release versions, multiplayer protocol numbers and opaque resource generations are separate. The multiplayer scene protocol is an internal replication contract; do not use it as the public API version or send provider delegates through it. Existing protobuf tags and compatibility envelopes remain preserved.

Current packages are HDR API **0.9.11 / scene protocol 20**, HTML Frontend **0.2.2**, JavaScript Runtime **0.1.0** and optional Client Renderer **0.9.14**. Existing PB camera capture and native mouse input retain their **0.9.13** minimum. Client-local native source consumers require **0.9.14**. See the [composition contract](Composition.md) for ordered source slots, generic surface mapping and HTML attachments.

The PB convenience command `run(targetName, [command=""])` returns printable status and reads the authenticated caller's **Custom Data** as a plain HTML document with embedded CSS. Empty, `demo` or `mount` starts one managed document; `reload`, `status` and `clear` operate only that document. The mod owns target discovery, fitting, bindings, refresh and retirement. Existing handle-based APIs remain available. See the [authoring tutorial](../../OptionalMods/HDRHtmlFrontend/Docs/PB-Tutorial.md) for the small invocation and [advanced bindings example](../../OptionalMods/HDRHtmlFrontend/Examples/HtmlPbBindingsDemo.cs) for optional gameplay integration.

## Client-local JavaScript prototype

The optional `HDR.JS/0.1` service interprets explicitly registered scripts through the `HDR.JavaScript/ES5.1-Prototype1` profile. Copy the public SDK into a consuming mod; use `open(ownerId)` to acquire its owner endpoint. Only standard scalars, arrays, tuples and delegates cross this boundary. No Jint types or game objects are exposed to JavaScript.

| SDK operation | Purpose |
|---|---|
| `CreateRealm`, `Execute`, `CallFunction` | Own an isolated interpreter and execute ES5.1 source or named functions |
| `BindHtml` | Bind an actual caller-owned HTML endpoint/document with its generation witness and declared editable IDs |
| `CreateHud`, `Pointer`, `CancelPointer` | Own a vector HUD and feed cooperative logical-pixel input; no native input acquisition |
| `AddSourceChoice` | Grant a named source choice with an actual provider capability probe; JS never receives raw provider handles |
| `Configure`, `Limits`, `RealmLimits` | Configure allowances for subsequently created realms and inspect the active policy |
| `Status`, `Diagnostics` | Inspect execution failures separately from bounded host logs and capability reasons |
| `DisposeRealm`, `ClearOwned`, `Dispose` | Retire scripts/timers/claims and explicitly clean up only runtime-owned displays |

The HTML host stages text/data/source edits during a successful callback and publishes one validated mutation batch. Errors discard staged edits, stop that realm and preserve its last committed display. Externally owned documents remain the C# owner's responsibility. Native LCD binding is rejected where atomic publication is unavailable. Ordinary vector updates and custom mod-native sources need no plugin; native providers report their exact `Requires plugin` reason when absent. The [host reference](../../OptionalMods/HDRJavaScriptRuntime/Docs/Host-API.md) specifies events, timers and source selection.

HTML Frontend 0.2.1 adds `mutate`, `script-claim`, `script-valid`, `script-release` and read-only provider-specific `source-check`; see its [local reference](../../OptionalMods/HDRHtmlFrontend/Docs/Local-API.md). Consumers must rebuild realm/document handles after connection-generation changes and must not separately drain a bound document's event queue. PB JavaScript execution, CSS keyframes, browser DOM, Canvas and modern ECMAScript are not implemented by this prototype.

## PB command index

| Area | Canonical reference |
|---|---|
| Binding, exact target discovery, structural capabilities, ownership, budgets | [Programmable blocks](Programmable-Blocks.md) |
| Geometry, SVG subset, transforms, appearance, layers, animations and native UI | [Drawing](Drawing.md) |
| Numeric values, source compare-and-set, artwork controls, constraints, event polling and persistent viewer | [Interactive artwork controls](Interactive-Controls.md) |
| Effect declarations, raw descriptor, transitions and limits | [Hologram effects](Special-Effects.md) |
| LCD backends/groups, virtual screens, analytic/mesh surfaces, raster UI, clipping/envelopes | [Display surfaces](Display-Surfaces.md) |
| Direct camera panorama, relay sources, synthetic perspective, portal declarations | [Cameras and portals](Cameras-and-Portals.md) |
| Client work/pixel/capture settings, diagnostics and fallback | [Performance and troubleshooting](Performance-and-Troubleshooting.md) |
| Combinations and runnable snippets | [Composition recipes](Composition-Recipes.md) |
| Ordered source slots, shared surface input and HTML composition | [Composition API](Composition.md) |

`HdrIngameApi` exposes constants for `Supported=1`, `NativeLcd=2`, `CalibratedVectorLcd=4`, `Floating3D=8`, `TableVolume=16`, `ProjectedSurfaces=32`, `Ui=64`. Capability tuples are `(schema, kind, flags)` with kind `lcd`, `console`, `projector` or `unsupported`. Flags describe the **block**, not the optional renderer installed on this client. Querying capabilities does not create content or select the target.

PB arguments accept finite numeric CLR values, not numeric strings. Public short commands validate argument count/type, authorization and retained geometry; an invalid replacement preserves earlier valid content where appropriate. Permissions/provider validity are checked separately and can require removing output. The legacy typed helper supplies exact delegate signatures for callers that prefer compile-time wrappers; it does not implicitly address a virtual screen selected through another endpoint.

The PB short endpoint also exposes `plugin-status(feature)` with `MyTuple<bool,bool,string>` known/registered/reason. A PB runs on the host/server: this result describes that execution context, not every remote viewer's plugin installation. Dedicated servers return viewer-dependent requirements rather than claiming remote plugin absence. `HdrIngameApi.TryCapabilities` and `TryPluginStatus` return whether the query succeeded, independently of the capabilities or registration reported in their output tuple. Use the local mod service below when querying a viewing client's registration.

The **0.9.9 / scene 19** interaction implementation adds server-owned PB values and client-local mod controls. Native mouse focus requires optional HDR Client Renderer **0.9.13** on the viewer; live native input remains unverified. PB `HDR.UI/1` uses `value`, `control`, `bind-value`, `draggable` and line/path/rotation `constraint` declarations. `get-value` returns `MyTuple<double,long>`; `set-value` returns `MyTuple<bool,double,long>` and accepts an optional expected revision. Fixed `value-notify` wakes prompt structured `poll-value-events`; they carry no dynamic command/value payload. Scripts explicitly assign variables from canonical results. A value ID is not a reflected field, and no arbitrary remote callback is exposed. See [exact PB commands and tuple/event meanings](Interactive-Controls.md#pb-declaration-order-and-exact-commands).

`U("version")` remains `HDR.UI/1`. Its static `capabilities` string preserves `values=1`, `constraints=line,path,rotation` and `mouse=client-provider`, with additive `viewer=persistent-bundle;pointer=HDR.Pointer/1`. This advertises implemented semantics rather than current input ownership. `H("plugin-status", "interactive-pointer")` returns `MyTuple<bool,bool,string>` for known feature/local provider registration/explanation. It requires HDR Client Renderer 0.9.13 or later. Dedicated-server results are known=true, registered=false and viewer-dependent; registration does not prove an actual routing ACK or server viewer/value grant.

HDR API 0.9.10 adds `U("poll-value-events", prefix)` to drain only records whose control or value ID starts with an owned prefix; omitted/empty prefix retains the original drain-all behavior. Prefixes use the same 1–24 lowercase letter/digit/underscore/hyphen grammar as UI IDs. Unmatched events and their wake state remain queued. `U("remove-value", id)` removes an unbound value and its events, returns false if missing, and rejects values still bound to controls. It preserves unrelated metadata; normal definition-revision changes can cancel existing gestures.

The optional frontend's `HDR.HtmlPB/0.1` adapter is separate from both core drawing and the client-local HTML service. It parses/layouts bounded sources on the host when dirty and publishes ordinary retained HDR drawing/UI declarations. Its published revision is a server commit, not a viewer rendering acknowledgement. See [PB HTML/CSS](../../OptionalMods/HDRHtmlFrontend/Docs/PB-API.md) for the exact commands, lifecycle and event mapping.

## General client-mod protocol 1

The service broadcasts its delegate on `481770130`. Send `Action<Func<string,object[],object>>` on `481770131` to receive it on demand. This is client-local; dedicated servers do not activate it. See [mod integration](Mod-Integration.md#connect-a-client-mod) for load-order-safe registration/disposal.

### Service commands

| Command | Arguments | Result |
|---|---|---|
| `version` | None | `"HDR.ModClient/1"` |
| `capabilities` | None | Detached `string[]` capability tokens |
| `plugin-status` | Feature name (`string`) | `MyTuple<bool,bool,string>`: known feature, registered locally, reason |
| `open` | Owner ID (`string`) | Consumer-scoped `Func<string,object[],object>` |
| `draw-budget` | Primitive budget (`int`, 16–131,072) | `true`; shared client budget, default 20,000 |

Current capability tokens are `client-local`, `hud-vector-postpp`, `world-vector-depth`, `text`, `svg`, `registered-material-uv`, `cooperative-pointer`, `event-poll`. Do not treat these as raster-upload or native-capture support. Opening an existing owner ID revokes its predecessor generation and clears its retained contexts.

### Text metrics and compiler admission

HDR API 0.9.10 adds `measure-text` on the PB short endpoint, general mod service and current mod-owner endpoint. It returns `MyTuple<string,MyTuple<double,double,double>,MyTuple<double,double,double,double>,int,bool>`: profile, `(maximum line advance, cap height, line advance)`, `(minX,minY,maxX,maxY)`, metric generation and ink-present flag. The profile is `HDR.TextMetrics/1:Inter:cap-height`; bounds describe source outlines in Y-up coordinates with the first baseline at `-height/2`, before clipping or rasterization. Text is limited to 64 original UTF-16 code units, height is positive up to 1,000,000 and line-height is 1–4. CR/LF normalization, tabs and fallback glyphs match rendering; shaping, kerning, bidi and exact CSS em metrics are unavailable. This query builds no glyph mesh.

`geometry-cost` intentionally compiles bounded text/SVG once to report exact retained cost. The subsequent artwork upsert compiles again. It is a preflight, not an atomic document transaction or a guarantee that all geometry fits the shared per-frame draw grant. `geometry-usage` counts hidden content too. `context-valid` reports ownership/lifetime, while `rendering-enabled` lets a consumer suspend its own input when local rendering is off. Explicit `item-order` must be reapplied after dirty artwork replacement.

### Consumer commands

Arguments are ordered after the command name. `[x=default]` denotes an optional trailing argument. `paint` is a color string or normalized `Vector4` RGBA.

| Command | Arguments | Result / meaning |
|---|---|---|
| `create-hud` | `[order=0]` | New `long` context handle; native viewport pixels |
| `create-world` | `order, MatrixD pose` | New `long` context handle; world-space pose |
| `viewport` | None | Current `Vector2` viewport size; zero if camera absent |
| `valid` | None | Boolean; **false** for stale/revoked owner endpoint without throwing |
| `rendering-enabled` | None | Local `/hdr on/off` rendering switch, independent of context lifetime |
| `context-valid` | `context` | Boolean; exact membership in the current owner's contexts |
| `measure-text` | `text, [height=1, lineHeight=1.3]` | Mesh-free packaged Inter cap-height metrics; tuple below |
| `geometry-cost` | `"text"` or `"svg", source, [height=1, segments=12]` | `MyTuple<int,int>`: compiled points, triangles + wire edges |
| `geometry-usage` | None | `MyTuple<int,int>`: retained point/primitive counts across this owner |
| `geometry-limit` | `[points=0, primitives=0]` | `true`; aggregate owner allowances, independently `0` for unlimited; finite limits validate existing artwork and retire overflowing cached sources |
| `geometry-limit-settings` | None | `MyTuple<int,int>`: configured point/primitive allowances; defaults `(0,0)` |
| `plugin-status` | Feature name (`string`) | Local registration tuple, not GPU/frame readiness |
| `draw-limit` | `[primitives=4096]`, 1–8,192 | `true`; owner cap within fair shared budget |
| `release` | None | `true`; revoke owner and all its handles |
| `destroy` | `context` | `true`; remove one context |
| `clear` | `context` | `true`; reset drawing, bounds, values, controls and events |
| `context-visible` | `context, bool` | `true`; hidden context stops drawing/hits and clears input state |
| `context-pose` | `context, MatrixD` | `true`; world contexts only |
| `context-surface` | `context, kind, canvasWidth, canvasHeight, ...shapeParameters` | `true`; plane/cylinder/sphere/ellipsoid/mesh on a world context; exact typed shape helpers in `HdrModApi` |
| `context-mapping` | `context, angular/geodesic/pinhole, [verticalFovRadians=π/2, sourceAspect=0]` | `true`; zero aspect uses canvas aspect |
| `context-surface-sided` | `context, twoSided, [frontOpacity=1, backOpacity=1]` | `true`; independent side multipliers |
| `context-surface-error` | `context, metres` | `true`; bounded surface chord error |
| `context-surface-clear` | `context` | `true`; clear mapping and source slots |
| `context-surface-ray` | `context, worldOrigin, worldDirection` | `MyTuple<bool,Vector2,Vector3D>` hit, centred Y-up canvas metres, world hit; read-only, no input grant |
| `context-anchor` | `context, actual IMyTerminalBlock or null` | `true`; physical source authority for HUD/world contexts |
| `context-source-status` | `context, [provider]` | `MyTuple<bool,bool,string>` negotiated provider availability, current anchor validity, reason |
| `context-slot` | `context, id, provider, sourceId, Vector4 rect, Vector4 clip, Vector4 UV, [order=0, opacity=1]` | `true`; ordered local source region; HUD top-left pixels or world centred Y-up canvas metres |
| `context-slot-status` | `context, id` | `MyTuple<bool,string>` frame readiness and reason |
| `context-slot-remove` | `context, id` | `true`; retire only this source slot |
| `mesh` | `context, id, Vector3D[] points, int[] triangles, [paint="white", Vector2[] UV=null, string material=null]` | `true`; indexed triangle mesh, optional registered material |
| `wires` | `context, id, Vector3D[] points, Vector2I[] edges, [paint="white", width]` | `true`; indexed segments; width defaults 1 HUD pixel or .01 world metre |
| `text` | `context, id, string text, Vector3D position, height, [paint="white", alignment="start"]` | `true`; retained vector glyphs |
| `svg` | `context, id, string svg, MatrixD transform, [segments=12]` | `true`; bounded SVG subset |
| `transform` | `context, id, MatrixD` | `true`; update existing item's transform |
| `visible` | `context, id, bool` | `true`; update drawing visibility |
| `item-order` | `context, id, int order` | `true`; explicit retained order without replacing geometry or controls |
| `effect` | `context, id, type, ...values` | `true`; merge a retained effect, or replace the validated raw descriptor |
| `effect-clear` | `context, id, [type]` | `true`; clear one effect or all effect settings |
| `transition` | `context, id, in/out, [style="fade", seconds=.5]` | `true`; start a retained fade, wipe or dissolve |
| `remove` | `context, id` | Boolean: item existed |
| `bounds` | `context, regionId, Vector4(x,y,width,height)` | `true`; positive hit extent; last declaration wins overlap |
| `remove-bounds` | `context, regionId` | `true`; remove hit region |
| `pointer` | `context, x, y, pressed` | `true`; cooperative local hit evaluation |
| `poll-events` | `context` | Drained `MyTuple<string,string>[]`: event kind and region ID |
| `value` | `context, id, initial, min, max, [step=0]` | `true`; client-local finite numeric record; duplicate ID rejects |
| `get-value` | `context, valueId` | `MyTuple<double,long>`: canonical value, revision |
| `set-value` | `context, valueId, requested, [expectedRevision=-1]` | `MyTuple<bool,double,long>`: accepted, current value, revision; nonnegative revision requests CAS |
| `remove-value` | `context, valueId` | `true`; unbind controls and revoke the shared value gesture |
| `control` | `context, controlId, artworkId, Vector4(left,top,width,height)` | `true`; existing owned artwork; one control per artwork |
| `bind-value` | `context, controlId, valueId` | `true`; no-jump reference pose/value baseline |
| `draggable` | `context, controlId, [enabled=true]` | `true`; admit drag participation |
| `constraint` | `context, controlId, "line", Vector3D from, Vector3D to` | `true`; local line motion |
| `constraint` | `context, controlId, "path", Vector3D[] points` | `true`; local polyline arc-length motion |
| `constraint` | `context, controlId, "rotation", Vector3D pivot, Vector3D axis, angleMin, angleMax` | `true`; angles in radians map from the numeric range |
| `remove-control` | `context, controlId` | `true`; artwork and legacy bounds remain |
| `pointer-ray` | `context, Vector3D worldOrigin, Vector3D worldDirection, pressed` | `true`; cooperative world-context picking |
| `pointer-cancel` | `context` | `true`; revoke gesture on input/focus ownership loss |
| `poll-value-events` | `context` | Drained `MyTuple<string,string,string,MyTuple<double,long,long>>[]` |

`Rect` and `Image` in the typed helper compose mesh quads; raw `rect`/`image` command names are not defined. UV arrays and material must both be supplied or both absent. Material names refer to consumer-owned already registered transparent materials, not URLs/files or newly uploaded GPU resources. The `HDR_Client` prefix is reserved and rejected; renderer-private material names do not grant a source lease. One UV per point, each component 0–1; one uniform paint per raw mesh. SVG geometry carries imported per-triangle colors.

HUD Z must remain zero after transformation. HUD origin is top-left, +Y down; text is adapted automatically, SVG needs a consumer-supplied Y flip. World geometry uses metres, transformed item → context pose. Submission order is context order, then retained item declaration order; native billboard blending/sorting remains in control. Each wire draws two triangles and consumes two draw-work primitives. Legacy event kinds remain `enter`, `leave`, `click`; click fires on the pressed rising edge while a region is hit. Numeric gestures add `down`, `move`, `up`, `cancel` without changing that envelope. Legacy bounds are independent of artwork/transforms.

Numeric event fields are kind/control ID/value ID/(canonical value, revision, player ID). Mod kinds are `begin`, `change`, `set`, `end`, `cancel`; local player ID is zero. PB completion instead uses `commit`. Progress coalesces and queues are bounded. Values clamp/snap through the shared kernel, and all coupled artwork poses commit atomically. Rejected CAS preserves current state; an accepted source write preempts the active value gesture. HUD control/path/pivot geometry stays planar, with rotations about local Z. Author control rectangles follow their artwork pose; they do not prove pixel/triangle coverage or occlusion against unrelated world geometry.

The [effects reference](Special-Effects.md) gives the shared PB/mod effect grammar, numeric descriptor, defaults and limits. World contexts support depth layers and projection rays; HUD contexts accept the planar subset and reject world-only settings. Canonical items submit before their optional embellishments. Effects capabilities are `hologram-effects`, `procedural-particles` and `hologram-transitions`; numeric capabilities are `constrained-controls`, `values-cas` and `cooperative-drag`.

Aggregate geometry allowances default to **unlimited**. Use `geometry-limit(points, primitives)` or `HdrModApi.GeometryLimit` to select finite allowances, and `geometry-limit-settings` / `GeometrySettings` to read them. Zero disables each allowance independently. Finite fitting includes mapped artwork and cached source geometry; lowering below existing artwork is rejected atomically, while a permitted change retires overflowing source caches.

Structural bounds from [ModClientDefinitions.cs](../../Mod/Data/Scripts/HoloMap/ModClientDefinitions.cs) and [ModClientInteractions.cs](../../Mod/Data/Scripts/HoloMap/ModClientInteractions.cs) remain separate: 16 owners, 16 contexts/owner, 64 items, regions, numeric values and controls/context, and bounded 64-entry event queues. A mesh respects the common 2,048-point/4,096-primitive item format. IDs are 1–64 characters without control characters. Numeric progress uses at most 63 slots, reserving the 64th for a terminal event; excess terminal accumulation can drop the oldest. Per-frame draw work has its own configurable allowance.

The shared mod primitive budget and each owner's limit are also bounded by the viewer's global `/hdr work` allowance. Eligible powered nearby PB content or active block UI focus reserves a block share: mod contexts receive at most half the global grant, rounded up. Otherwise the mod category can use the full grant. Only submitted work is charged; unused mod grant flows to block drawing. Active owners/contexts share the mod allocation. `/hdr off` disables local HDR output. No consumer may increase the viewer's global allowance through this service.

Transforms must be finite nonsingular affine matrices, with linear coefficients bounded to 1,000,000 and translations/placed points bounded to 10¹². HUD transformed Z is zero within tolerance. Native submission also rejects nonfinite/degenerate normals and points beyond the supported 1,000,000-metre camera-relative envelope. HUD geometry is placed just beyond the near plane (`NearPlaneDistance * 1.001` with a positive guard), uses premultiplied color and the engine's **depth-tested** `PostPP` billboard path. Very near world geometry can occlude it; this is not an always-on-top overlay contract.

Calls require the client simulation thread outside HDR drawing. Wrong types, arguments or bounds throw `ArgumentException`; revoked endpoints reject all commands except the zero-argument `valid` query, which returns false. No renderer-thread callbacks, mouse capture, keyboard focus, arbitrary PB actions or automatic networking are exposed by this contract.

The typed numeric methods are `Value`, `Control`, `BindControlValue`, `Draggable`, `ConstraintLine`, `ConstraintPath`, `ConstraintRotation`, `GetValue`, `SetValue` and `PollValueEvents`. `BindValue(context,id,Func<double>,Action<double>)` registers a consumer-owned local adapter and returns `IDisposable`; it runs no callback at registration. Explicitly call `UpdateBindings` on the client simulation thread outside Draw. The first pump makes the getter source authority; later source changes use fresh-revision CAS, while new HDR revisions and canonical corrections reach the setter. Failed callbacks detach that adapter and set `LastBindingError`. `UnbindValue`, disposal, value removal, context clear/destroy, endpoint release/replacement and reconnect revoke registrations. Rebuild them for the new connection generation. See [InteractiveControlsModExample.cs](../../Examples/Mods/InteractiveControlsModExample.cs) and [callback/input ownership](Interactive-Controls.md#client-mod-bindings-and-cooperative-input).

Cooperative `Pointer`/`PointerRay` need no optional renderer plugin and obtain no automatic native capture. The consumer supplies owned input and calls `CancelPointer` on focus/route loss; it also owns gameplay replication. The optional `HDR.Pointer/1` native provider is a separate local input lease contract. For PBs, on-foot first-person **Use** enters a persistent world-artwork bundle viewer; mouse-down leases a value, mouse-up releases only that gesture, and **Escape/context loss** closes the viewer and cancels its gestures. Provider readiness does not replace the server grant or a fresh **Applied + PointerValid** routed sample. See [viewer admission and retirement](Interactive-Controls.md#input-leases-multiplayer-and-retirement).

The typed mod helper checks `valid` in `Ready`; a successful reconnect increments `ConnectionGeneration`. Consumers must discard/rebuild all old context handles. `Request` supports bounded retry; [HudMenuModExample.cs](../../Examples/Mods/HudMenuModExample.cs) retries every 60 simulation ticks. `TryCall(command,out result,out reason,params args)` distinguishes command success from its returned value. Convenience methods are `Capabilities()` and `PluginStatus(feature)`. `TryCapabilities`/`TryPluginStatus` belong to the separate **PB** helper, not `HdrModApi`.

```csharp
object result;
string reason;
if (_api.TryCall("plugin-status", out result, out reason, "native-portal")) {
    var status = (MyTuple<bool,bool,string>)result;
    // Query success is distinct from status.Item1 (known) and Item2 (registered).
    if (!status.Item2) MyAPIGateway.Utilities.ShowMessage("My mod", status.Item3);
}
```

Plugin features are `raster-ui`, `camera-panorama`, `lcd-texture`, `native-portal`, `interactive-pointer`. `plugin-status` returns **known**, **registered locally**, **reason**; recognized-but-absent and registered-but-not-ready are distinct cases. A successful helper query does not imply tuple `Item2=true`, and `Item2=true` proves neither a completed usable frame nor native input routing. `interactive-pointer` requires HDR Client Renderer 0.9.13 or later. Missing-feature use reports a bounded `Requires plugin: HDR Client Renderer (feature)` notice once per raster/camera/native-portal/interactive-pointer category until registration/reconnect or session reset. Dedicated servers describe viewer-dependent requirements and accept declarations without asserting remote plugin absence. Exact source: [PluginCapabilities.cs](../../Mod/Data/Scripts/HoloMap/PluginCapabilities.cs).

## Display source protocols 1 and 2

HDR broadcasts `Func<string,object[],object>` on discovery **481770100**. Provider registration on **481770101** is:

```csharp
MyTuple<string, int, Func<string, object[], object>>
// provider ID, protocol (1 or 2), endpoint
```

Provider IDs are up to 32 lowercase letters/digits/hyphens. General source IDs are nonempty, at most 64 characters, without control characters. `camera-panorama` has its own comma-separated positive-camera-ID schema up to six cameras and 256 characters. Native provider IDs are specialized implementations, not generic sample IDs. A new endpoint/protocol for an existing provider replaces its generation and invalidates old output. At most eight providers are retained.

### HDR → provider commands

| Command | Arguments | Result |
|---|---|---|
| `begin`, `end` | None | Return ignored; validity memoization scope |
| `frame` | Ordered request below | Protocol-specific frame or `null` |
| `valid` | `long anchorId, long callerPbId, string sourceId, object evidence` | Boolean proof for exact payload |
| `release` | `object evidence` | Protocol 2 retirement; return ignored |

Frame request fields, in order:

| Position | Type | Meaning |
|---|---|---|
| 1–4 | `long, long, string, string` | Anchor entity ID, caller PB ID, source ID, screen ID |
| 5–6 | `int, int` | Available points and triangle/primitive allowance |
| 7–8 | `double, double` | Physical canvas width/height |
| 9 | `double[12]` | Eye XYZ, target XYZ, up XYZ, vertical FOV radians, near, far |
| 10–11 | `int, int` | Screen synthetic raster width/height |
| 12–14 | `double, double, double` | Orbit rate, orbit start tick, synchronized animation time ticks |
| 15–16 | `int, int` | **Protocol 2 only:** effective image width/height |

Protocol 1 result:

```csharp
MyTuple<Vector3D[], int[], Vector4[], object, bool, double>
// points, indexed triangles, RGBA per triangle, evidence, reduced, refresh Hz
```

Points lie on Z=0, within ±half canvas width/height, Y up. HDR bounds lengths before copying; indices and colors must be finite/valid. Points and primitives fit the supplied allowance and per-item geometry bounds. Refresh range is .1–6 Hz.

Protocol 2 result:

```csharp
MyTuple<int, object, object, bool, double>
// frame kind, payload, evidence, reduced, requested refresh Hz
```

| Kind | Payload | Contract |
|---|---|---|
| 1 | `MyTuple<Vector2I,byte[],int>` | Size, exact detached `width*height*4` bytes, flags **0**: straight-alpha sRGB RGBA8 |
| 2 | `MyTuple<string,Vector2I,long>` | Registered local material, physical texture size, positive resource generation; full UV |
| 2 | `MyTuple<string,Vector2I,long,Vector4>` | Same plus normalized `(x,y,width,height)` UV rectangle |

Kind 1 uploads through the optional backend; current mod admission is 16–2,048 per axis and at most 1,048,576 pixels, further limited by effective demand/backend caps. Kind 2 physical dimensions are 1–4,096 per axis, material name at most 128 characters from letters/digits/underscore/hyphen/dot, positive resource generation, and optional finite positive UV rectangle fully inside 0–1. Preserve its pixels/layout until all acquired leases retire.

General protocol 2 refresh accepts .1–60 Hz, then applies declaration/client limits. The built-in `native-portal` provider allows up to 120 Hz; `camera-panorama` uses its specialized cadence limits. This exception is not a promise that any generic provider can bypass scheduling. Offscreen sources do not receive expensive frame preparation; current client scheduling and upload/capture quotas remain separate. See [performance](Performance-and-Troubleshooting.md).

### Provider → HDR service commands

| Command | Arguments | Result |
|---|---|---|
| `unregister` | `providerId, sameEndpointDelegate` | Boolean; compare exact registered endpoint |
| `source-state` | `providerId, anchorId, callerPbId, sourceId` | Boolean useful visible demand |
| `source-demand` | Same four fields | `MyTuple<int,int,double,bool>` width, height, refresh ceiling, useful |
| `source-demand-any` | `providerId, sourceId` | Same tuple aggregated across matching anchors/callers |

Missing/hidden/offscreen demand returns `(0,0,0,false)`. Demand combines visible consumers and bounds effective dimensions; it is **not** permission evidence, acquisition configuration or a promise of GPU completion. Current ordinary image demand is bounded to 2,048 per side/1,048,576 pixels; wide native source requests use a 4,096-side/8,388,608-pixel path, before further provider/GPU allocation caps. See [AdaptiveScreens.cs](../../Mod/Data/Scripts/HoloMap/AdaptiveScreens.cs) and [DisplayLod.cs](../../Mod/Data/Scripts/HoloMap/DisplayLod.cs).

### Opt-in client-local source consumers

Core 0.9.11 adds `local-mod-native-consumers(1, endpoint)` to the display-source service. A currently registered protocol-1/2 provider passes its **exact registered delegate** to opt into local mod contexts. HDR Client Renderer 0.9.14 negotiates the same extension for its native providers. Success returns `Func<object,object[]>`, a resolver for current opaque Core-owned bindings; failure returns `false`.

After negotiation, local frame calls use `frame(binding, width, height)`, `valid(binding, evidence)` and `release(evidence)`. Frame payloads retain the existing protocol-1 mesh and protocol-2 image/material formats. Legacy PB calls retain their original caller/screen arguments. A local binding is not a PB entity ID and never enters scene replication.

The resolver returns a detached ten-field descriptor, or `null` when the owner/context/slot, endpoint generation, visibility, anchor or source access is stale:

| Index | Value |
| --- | --- |
| 0–1 | Schema `1`, caller kind `"local-mod"` |
| 2 | `MyTuple<string,long,long,long,long>` owner ID, owner generation, context handle, context revision, source revision |
| 3–5 | Actual `IMyTerminalBlock` anchor, provider ID, source ID |
| 6 | Actual `IMyTerminalBlock[]` source identities; custom providers receive the anchor and validate their own source |
| 7 | `MyTuple<int,int,double,bool>` requested width, height, refresh ceiling, active demand |
| 8 | Provider data: camera panorama/settings profile tuple, existing portal descriptor, or `null` |
| 9 | Opaque current source-declaration identity |

Provider replacement/unregistration, source retirement, owner replacement and context cleanup revoke these bindings and release owned frames. Custom providers still own their data permissions. A registered provider that has not opted in reports `Unsupported consumer capability`; absent native registration reports the required plugin. A local portal window consumes an existing declared portal's texture; it does not relocate the transported entry. Exact implementation: [Core bridge](../../Mod/Data/Scripts/HoloMap/ModClientNativeSources.cs), [native consumer registry](../../OptionalPlugins/HDRClientRenderer/Source/ModSourceConsumers.cs).

### Specialized native-provider service extensions

These commands are intended for HDR's optional native integration, not required by an ordinary display provider. Return `false`/unknown conservatively if a schema or proof cannot be established. The base source protocol remains independent of these extensions.

| Command | Arguments | Result / source |
|---|---|---|
| `source-panoramasettings` | `"camera-panorama", anchorId, callerPbId, sourceId` | `MyTuple<double,double,double,int>` FOV degrees, feather degrees, saturation, capture ceiling |
| `source-renderprofile` | Same | `"normal"` or `"lite"` |
| `source-cameradensity` | Same plus detached lens-basis `double[]` | `MyTuple<int,double[],bool>` visible camera bitmask, packed field, known |
| `source-portaldescriptor` | `"native-portal", anchorId, callerPbId, sourceId` | Detached 19-field `object[]` or `false` |
| `source-occlusion` | Positive `long cameraId` | `MyTuple<bool,object,MyTuple<string,object,double[]>[]>` known, inventory proof, consumer enclosures |
| `source-occlusion-fresh` | Positive `long cameraId` | `Func<object,bool>` checking current immutable inventory identity |

Density lens bases have 11 doubles/camera: right XYZ, up XYZ, forward XYZ, horizontal/vertical half-angle tangents. The detached field starts with tile side **16**, camera count, then one suggested resolution/camera, then 256 cells/camera with seven doubles/cell: covered, upper magnification, upper area, measured magnification, measured area, blend weight, has sample. `known=false` must retain conservative camera visibility; an absent fine sample is not proof that a ray is invisible. Exact code: [CameraSourceDemand.cs](../../Mod/Data/Scripts/HoloMap/CameraSourceDemand.cs).

Portal descriptor schema **1** positions: schema, transport, accuracy, entry kind, entry world matrix, entry extents, entry XYZ points, entry indices, entry UVs, exit kind, exit world matrix, exit extents, exit points, exit indices, exit UVs, shell world matrix, shell radii, settings `{normalScale,refresh,saturation,brightness}`, request `{width,height,entryChart,exitChart}`. Kind 0 is plane, 1 inward ellipsoid, 2 authored UV mesh, 3 outward ellipsoid; transport 0 differential/1 stealth; accuracy 0 strict/1 approximate. Matrices are detached row-ordered 16-number arrays; meshes use flattened XYZ/UV arrays. Exact validation/lifetime: [PortalSources.cs](../../Mod/Data/Scripts/HoloMap/PortalSources.cs), [PortalProvider.cs](../../OptionalPlugins/HDRClientRenderer/Source/PortalProvider.cs).

Occlusion returns a complete conservative static-consumer inventory, not camera-image depth. Consumer tuples carry a key, immutable identity and detached world triangle enclosure. Any missing/dynamic/unsupported consumer or stale proof prevents culling the camera. Only the returned freshness delegate is render-thread safe; the ordinary service traverses game declarations on the simulation side. See [DisplaySourceOcclusion.cs](../../Mod/Data/Scripts/HoloMap/DisplaySourceOcclusion.cs).

## Raster upload backend protocol 1

HDR discovery **481770110** supplies its service. Registration **481770111** carries `MyTuple<int,Func<string,object[],object>>(1, endpoint)`. Replacing a backend invalidates old upload wrappers/generation and retires their resources.

| Direction / command | Arguments | Result |
|---|---|---|
| HDR → `caps` | None | Optional `MyTuple<int,int,int,int>` max width, max height, max pixels, positive capability/format marker |
| HDR → `upload` | `string key, long serial, int width, int height, byte[] rgba` | `MyTuple<string,object>` registered material, lease; or `null` |
| HDR → `valid` | `object lease` | Boolean |
| HDR → `release` | `object lease` | Return ignored |
| Backend → `unregister` | `sameEndpointDelegate` | Boolean |

Caps are clamped to the mod's current 2,048-side/1,048,576-pixel bounds. An absent/malformed caps result uses conservative 1,024-side/262,144-pixel defaults. Upload receives a copied exact RGBA buffer, straight-alpha sRGB RGBA8. A deferred upload may return its existing published lease until a new serial commits; retaining that same lease must not destroy it. `valid` is checked against both the lease and registered backend generation; exceptions invalidate output.

Use unique ownership/publication stamps for native resources and retire captured leases rather than whichever resource later occupies the same slot. Source evidence and upload leases are distinct: the provider owns source authorization/publication, while the backend owns the upload it created. Neither may destroy a successor or another screen's acquired resource.

## Compatibility and acceptance checklist

- Check the interface identifier, required capability tokens and exact return shapes.
- Register before discovery/request; test both mod load orders and reconnect after unload/replacement.
- Keep named content retained, and update only changed definitions.
- Fail empty on revoked permission/provider evidence; keep unrelated artwork usable.
- Release the exact context, endpoint, source evidence and upload lease you acquired.
- Test dedicated-server absence, per-client plugin absence, two consumers and multiplayer declarations.
- Treat offline type/geometry checks and live GPU/interaction testing as separate evidence.
