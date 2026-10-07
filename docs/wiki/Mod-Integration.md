# Integrating another mod

HDR offers three different mod boundaries: **consume rendering**, **supply display data**, or **implement a raster backend**. Use the first for a HUD/menu or custom world-space drawing; use the second for data that a PB can attach to a block-bound screen. Neither interface grants a PB access to arbitrary client delegates or native GPU resources.

Retained HUD/world items can use [hologram effects](Special-Effects.md), including flicker, refresh bars, procedural particles and reveal transitions. World contexts also support layered depth and projection rays. The effects use the consumer's existing local context and do not require a renderer plugin.

![API ownership and execution boundaries](diagrams/architecture-boundaries.svg)

## Choose the boundary

| Interface | Consumer | Purpose | Discovery / reply channels |
|---|---|---|---|
| `HDR.ModClient/1` | Client mod | Retained HUD and general world-space content; cooperative local input | `481770130` / `481770131` |
| Display source protocol 1 or 2 | Data/provider mod or optional plugin | Mesh, RGBA image, or borrowed local material attached to a declared screen | `481770100` / `481770101` |
| Raster upload backend protocol 1 | Optional renderer implementation | Upload HDR's raster pixels to a leased registered material | `481770110` / `481770111` |
| `HDR.Draw/1`, `HDR.UI/1`, `HDR.Api/1` | Programmable block | Authorized LCD/Console/Projector content and block interaction | Terminal properties on the caller PB |

These channels use **local `SendModMessage`**, not multiplayer transport. Delegate endpoints, opaque evidence and GPU leases never enter PB scene serialization. `481770120`/`481770121` belong to the separate Camera360 integration and are not general drawing discovery.

## Runtime and replication matrix

| Work | Viewing client | Dedicated server | Shared across players |
|---|---|---|---|
| PB drawing declarations and canonical block UI actions | Draws authorized declarations | Validates/replicates declarations and actions | Yes, through HDR scene protocol |
| `HDR.ModClient/1` HUD/world contexts | Retains and draws this mod's local contexts | Service absent; skip activation | No automatic replication |
| Provider frame and source evidence | Prepared/validated locally | No local GPU image capture required | Only provider/source identifiers in HDR declarations |
| Direct camera/native portal rendering | Optional client renderer captures/composes | Plugin not required for capture | Declaration shared; pixels remain local |
| General menu hit events | Cooperating consumer supplies pointer and polls | No client pointer or local menu | Consumer mod owns any authorized network action |
| CamScan observations/reconstruction | Provider-specific | Provider-specific | Provider's own policy and protocol |

If all players should see a mod's custom world context, send your own validated data to each client and build equivalent contexts there. Calling a local endpoint once on the server does not broadcast it. Keep menu commands and sensor access checks in the owning mod; local visibility and hit events are not authorization.

## Connect a client mod

All calls below use standard/game CLR types. Include normal mod imports for `System`, `Sandbox.ModAPI`, `VRage`, `VRageMath` and session components. Register discovery before requesting the service so either load order works.

```csharp
const long Discovery = 481770130, Request = 481770131;
Func<string, object[], object> _client;
long _hud;
bool _registered;

void ConnectHdr()
{
    if (MyAPIGateway.Utilities.IsDedicated || _registered) return;
    MyAPIGateway.Utilities.RegisterMessageHandler(Discovery, ReceiveHdr);
    _registered = true;
    MyAPIGateway.Utilities.SendModMessage(Request,
        new Action<Func<string, object[], object>>(ActivateHdr));
}

void ReceiveHdr(object message)
{
    var service = message as Func<string, object[], object>;
    if (service != null) ActivateHdr(service);
}

void ActivateHdr(Func<string, object[], object> service)
{
    // Discovery can be repeated. Do not reopen a live context every time.
    if (_client != null) {
        if ((bool)_client("valid", new object[0])) return;
        _client = null;
        _hud = 0;
    }
    if ((string)service("version", new object[0]) != "HDR.ModClient/1") return;
    _client = (Func<string, object[], object>)service("open",
        new object[] { "example.systems-hud" });
    _hud = (long)_client("create-hud", new object[] { 20 });
    _client("text", new object[] {
        _hud, "title", "SYSTEMS", new Vector3D(32, 48, 0),
        24d, new Vector4(0, .8f, .9f, 1), "start"
    });
    _client("bounds", new object[] {
        _hud, "open-menu", new Vector4(24, 24, 240, 64)
    });
}

// Call from your input adapter on the client simulation thread.
// This API does not capture a cursor or read mouse state for you.
void UpdateHdrPointer(double pixelX, double pixelY, bool pressed)
{
    if (_client == null) return;
    try {
        _client("pointer", new object[] { _hud, pixelX, pixelY, pressed });
        var events = (MyTuple<string, string>[])_client("poll-events",
            new object[] { _hud });
        foreach (var e in events)
            if (e.Item1 == "click" && e.Item2 == "open-menu")
                OpenMyLocalMenu(); // your mod's action, not an HDR command
    }
    catch (ArgumentException) {
        // Endpoint revoked/replaced: drop local handles and reconnect later.
        _client = null;
        _hud = 0;
    }
}

void DisconnectHdr()
{
    var endpoint = _client;
    _client = null;
    _hud = 0;
    if (endpoint != null)
        try { endpoint("release", new object[0]); } catch { }
    if (_registered && MyAPIGateway.Utilities != null)
        MyAPIGateway.Utilities.UnregisterMessageHandler(Discovery, ReceiveHdr);
    _registered = false;
}
```

Call `ConnectHdr` from client session initialization and `DisconnectHdr` from `UnloadData`. Define `OpenMyLocalMenu` in your own mod. If HDR loads later it broadcasts discovery; after a revoked endpoint, request again on a bounded retry cadence. Reacquire all context handles after reconnecting. The [typed mod helper](../../Api/Mods/HdrModApi.cs) and [complete HUD example](../../Examples/Mods/HudMenuModExample.cs) provide the same lifecycle with convenience methods.

The typed helper's `Ready` checks the consumer endpoint's nonthrowing `valid` query. A successful reconnection increments `ConnectionGeneration`; handles from earlier generations must be discarded. A minimal simulation-update pattern is:

```csharp
Hdr.Mods.HdrModApi _api;
long _connection, _menu;
int _retryTicks;

void UpdateMenu()
{
    if (_api == null) return;
    if (!_api.Ready) {
        if (++_retryTicks >= 60) { _retryTicks = 0; _api.Request(); }
        return;
    }
    if (_connection != _api.ConnectionGeneration) {
        _connection = _api.ConnectionGeneration;
        _menu = 0; // no old-generation handles survive a reconnect
    }
    if (_menu == 0) {
        _menu = _api.CreateHud(20);
        _api.Text(_menu, "title", "SYSTEMS", new Vector3D(32,48,0),
            24, new Vector4(0,.8f,.9f,1));
        // Rebuild the rest of this menu, including hit bounds, here.
    }
}
```

Create `_api` in client startup with a unique owner ID and dispose it on unload. This typed pattern is an alternative to the raw delegate sample. The mod helper's `TryCall` reports whether a command/query succeeded without throwing; for `plugin-status`, inspect the returned tuple separately to determine known/local registration. `Capabilities()` and `PluginStatus(feature)` are the convenience query methods. The complete sample retries every 60 simulation ticks and rebuilds its HUD on each new connection generation. The separate **PB** helper exposes `TryCapabilities` and `TryPluginStatus`; those names are not methods of `HdrModApi`.

### Coordinates, order and ownership

HUD geometry uses native viewport pixel coordinates: origin at top left, X right, Y down, Z zero. `viewport` returns the current `Vector2` viewport size. Layout belongs to the consumer: update placement when the viewport changes. Text handles the HUD Y flip automatically. SVG parsing produces display-up geometry; use a transform with a negative Y scale when mapping it into a HUD pixel plane. Geometry must remain in the HUD XY plane after its transform.

World contexts use a valid affine `MatrixD` pose and local geometry in metres. World content participates in ordinary depth/transparency. HUD vectors use the engine's **depth-tested** `PostPP` billboard path with premultiplied color, placed just beyond the camera near plane at `NearPlaneDistance * 1.001` (with a small positive guard). This is a camera-plane overlay, not a guaranteed always-on-top layer: very nearby world geometry can occlude it. HUD drawing does not require a native plugin. Higher context order submits later; items use declaration/replacement order within their context. Native billboard blending/sorting still applies, so this is not browser-style isolated group compositing. Rectangle/image helper methods build mesh primitives; they are not separate wire commands.

Opening the same owner ID replaces its generation and revokes the old endpoint. Each handle belongs to one endpoint; another consumer cannot use it. `destroy` removes one context, `clear` resets its items/bounds/events, and `release` drops the whole owner. Clear/dispose explicitly rather than abandoning retained handles.

Calls run on the client simulation thread outside HDR's `Draw` scope. Do not call endpoints from a GPU thread or mutate them inside drawing. Invalid commands/types/bounds throw `ArgumentException`; endpoint replacement/unload also revokes further calls. Geometry/UV arrays are validated and copied before retention. Installed mods are trusted local code; this boundary catches cooperative errors, not malicious mod code.

Drawing shares the viewer's global `/hdr work` allowance. When eligible powered nearby PB content or block UI focus is active, the mod category receives at most half the global grant (rounded up), preserving a block-display share; otherwise it can receive the full grant. Only work actually submitted is charged, so unused mod allocation remains available to PB content. The mod service also has a configurable shared primitive budget, with an independent per-owner `draw-limit`; active owners/contexts share available work. `/hdr off` disables local HDR output, including these contexts. Increasing a consumer's declaration does not override the viewer's controls. Offline API/SDK checks establish the contract and submission path; live GPU and input acceptance must still be tested in game.

Consumer images use their own registered transparent materials. Names beginning `HDR_Client` are reserved for renderer-owned resources and rejected by the general consumer endpoint; obtaining such a name does not acquire its GPU lease. Use the source/backend protocols for provider-owned images.

### Interaction semantics

`bounds` registers `(x,y,width,height)` hit regions independently from artwork. The last declared matching region wins. `pointer` generates `enter`, `leave` and rising-edge `click` events; `poll-events` drains the queue. The bounded queue discards the oldest event on overflow. There is no keyboard focus, text editing, mouse capture or automatic multiplayer action in this protocol. A consumer can combine it with its own input handling or an optional input framework.

Bounds use context coordinates and are not attached automatically to transformed items. When moving a menu, update its artwork and bounds together. Hiding/removing a drawing item alone does not delete its separate hit region; use `remove-bounds` or hide/clear the context.

## Supply an external display source

Use this interface when your mod owns observations, video, simulation data or a derived image that PBs should display. A PB binds it using:

```csharp
H("screen-source", "example-feed", "navigation");
```

This attaches a named source. Acquisition settings and access remain in your provider. Register a stable endpoint delegate on channel `481770101` using `MyTuple<string,int,Func<string,object[],object>>`: provider ID, protocol version 1 or 2, endpoint. Listen on discovery `481770100` to retain HDR's service, and also register proactively at startup. Up to eight providers are retained.

```csharp
// Inside startup; _provider is a retained field of this exact delegate type.
_provider = MyProviderCommand;
MyAPIGateway.Utilities.SendModMessage(481770101,
    new MyTuple<string, int, Func<string, object[], object>>(
        "example-feed", 2, _provider));
```

Keep the exact delegate instance. On unload, call the retained HDR service's `unregister` with `{ "example-feed", _provider }`; matching endpoint identity prevents an old generation from unregistering its replacement. A provider also unregisters its own discovery handler.

HDR calls `begin`/`end` around each draw validity scope, `frame` with the available canvas/demand/budgets, and `valid` with the anchor, PB, source and opaque evidence. Protocol 1 returns a detached coloured triangle mesh; protocol 2 returns either detached RGBA8 bytes or a **borrowed registered material**. See [the exact protocol reference](Api-Reference.md#display-source-protocols-1-and-2).

### Minimal synthetic provider endpoint

This protocol 1 example returns a small coloured rectangle for source `navigation`. It owns no private sensor data; real observations require the provider's own permission/lifetime checks. Register it with protocol **1**, retain the same delegate for unregister, and set `_running=false` before unloading.

```csharp
bool _running;
long _epoch; // increment at each provider activation/replacement

object MyProviderCommand(string command, object[] args)
{
    if (command == "begin" || command == "end") return null;
    if (command == "valid") {
        if (!_running || args == null || args.Length != 4 ||
            !(args[3] is MyTuple<long, long, string, long>)) return false;
        var proof = (MyTuple<long, long, string, long>)args[3];
        return args[0] is long && args[1] is long && args[2] is string &&
            proof.Item1 == (long)args[0] && proof.Item2 == (long)args[1] &&
            proof.Item3 == (string)args[2] && proof.Item3 == "navigation" &&
            proof.Item4 == _epoch;
    }
    if (command != "frame" || !_running || args == null || args.Length != 14)
        return null;
    if ((string)args[2] != "navigation" || (int)args[4] < 4 || (int)args[5] < 2)
        return null;
    double w = (double)args[6] * .6, h = (double)args[7] * .6;
    var points = new[] {
        new Vector3D(-w/2, -h/2, 0), new Vector3D(w/2, -h/2, 0),
        new Vector3D(w/2, h/2, 0), new Vector3D(-w/2, h/2, 0)
    };
    var color = new Vector4(0, .7f, .8f, .8f);
    var evidence = new MyTuple<long, long, string, long>(
        (long)args[0], (long)args[1], (string)args[2], _epoch);
    return new MyTuple<Vector3D[], int[], Vector4[], object, bool, double>(
        points, new[] { 0,1,2, 0,2,3 }, new[] { color, color },
        evidence, false, 1d);
}
```

At startup set `_running=true` and increment `_epoch` before registration. The proof binds this immutable synthetic payload to the supplied anchor/PB/source and provider epoch. If you replace contents under a resource-backed provider, use a distinct publication proof rather than validating every old payload against the latest source name. HDR independently verifies the registered provider generation and bounds the submitted mesh.

### Evidence is a payload-specific lease

`valid` must prove that the exact submitted payload is still usable by this anchor/caller/source. Include your permission, data epoch and resource lifetime in the evidence policy; a source name alone is insufficient. HDR checks the producing endpoint generation, validates/copies bounded payloads, and rechecks evidence after preparation and before drawing. Failed validity, provider exceptions, unload or replacement clear source output.

For protocol 2, handle `release(evidence)` when HDR retires an acquired frame. Reusing the same evidence while a replacement is pending does not transfer a second independent resource; preserve the existing lease. Independently owned screens need independently valid resource ownership. Do not destroy a texture while another acquired screen can still draw it. Borrowed material generations and UV rectangles remain attached to their publication; changing a texture layout in place under an old lease can produce tearing.

`source-demand` supplies useful width/height/rate hints for an anchor/PB/source. A false/empty demand should suspend expensive local work; it does not delete your authoritative configuration. Demand is not authorization. Known-good permissions and lease checks must still gate acquisition. Specialized camera-density, occlusion and portal-descriptor services belong to the native provider integration; use them only with their documented schema and conservative fallback.

CamScan remains the owner of raycasts, observations, persistence, deduplication and reconstruction. HDR's direct camera provider renders client-local images; it does not scan or retain a 3D sensor map. A server need not install HDR Client Renderer to host declarations, but each viewing client needs it for native captures.

## Implement a raster upload backend

This is the renderer boundary, not the general HUD consumer API. A backend listens on `481770110` and registers `MyTuple<int,Func<string,object[],object>>(1, endpoint)` on `481770111`. HDR supplies detached RGBA bytes through `upload`; the backend returns a registered material name and opaque lease. HDR calls `valid` before use and `release` when retiring that upload.

Keep GPU allocation, render-thread dispatch, frame publication and resource cleanup inside the backend. Do not read PB/game entities from a native render thread. Optional `caps` describes maximum upload dimensions/pixels/format capability; older backends use conservative defaults. Unregister using the exact endpoint instance. See [raster backend reference](Api-Reference.md#raster-upload-backend-protocol-1) and [HDR Client Renderer](../../OptionalPlugins/HDRClientRenderer/README.md).

## Dependency choices

Query `plugin-status` on the service or consumer endpoint with `raster-ui`, `camera-panorama`, `lcd-texture`, or `native-portal`. It returns `MyTuple<bool,bool,string>`: **known feature**, **component registered on this client**, **reason**. Registration is not successful GPU initialization, native capability support, completed capture, or frame readiness. Unknown names return `(false,false,reason)`.

When a client tries to draw a missing plugin feature, HDR reports `Requires plugin: HDR Client Renderer (feature)`. Notices are bounded to three categories: raster UI, camera images (direct and LCD relay share one category), and native portals. Each category is reported once until a matching component registers/reconnects or the session resets. Server declarations remain accepted and dedicated-server diagnostics explain that availability is viewer-dependent; the server cannot claim that a remote viewer has no plugin. See [PluginCapabilities.cs](../../Mod/Data/Scripts/HoloMap/PluginCapabilities.cs).

| Feature | HDR Workshop mod | HDR Client Renderer plugin | Other integration |
|---|---|---|---|
| PB vectors, SVG, physical LCD sprite output | Required | Not required | None |
| General mod HUD/world vectors and registered materials | Required | Not required | Consumer packages its own registered materials |
| Native block look-and-use PB buttons | Required | Not required | Server validation built into HDR |
| Cooperative mod menu | Required | Not required | Consumer input/action handling |
| Raster UI or provider RGBA upload | Required | Required unless another compatible raster backend supplies it | Upload lease protocol |
| Direct camera panorama or native portals | Required | Required per viewing client | Engine/runtime support must be available |
| Existing LCD camera relay | Required | Required for the relay projection path | CameraLCD supplies the physical LCD image |
| CamScan reconstructed display | Required | Depends on supplied frame kind | Separate CamScan mod/API |

Protocol schemas are independent of product versions. Check `version` and required capabilities rather than parsing the mod release number. Test both load orders, absent HDR, context/provider reload, permission loss, resource retirement, dedicated servers and multiplayer before distributing your integration.
