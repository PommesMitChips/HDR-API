# API boundaries and capabilities

HDR separates **who declares content**, **where it appears**, **who produces its data**, and **who owns rendering resources**. Product versions do not replace interface versions.

![API execution boundaries](diagrams/architecture-boundaries.svg)

## Public entry points

| Caller | Entry point | Authority and output |
| --- | --- | --- |
| Programmable block | `HDR.Draw/1`, `HDR.UI/1`, typed `HDR.Api/1` | Server-validated declarations on accessible LCD/Projector/Console blocks on the PB's construct |
| Client mod consumer | `HDR.ModClient/1`, channels `481770130` / `481770131` | Consumer-owned local HUD/world contexts; no PB identity or automatic network replication |
| Content producer mod | Source protocol 1/2, channels `481770100` / `481770101` | Supplies content/evidence to display consumers; owns acquisition and validity |
| Raster backend | Backend protocol 1, channels `481770110` / `481770111` | Uploads raster content through owned opaque resource leases |
| Optional native renderer | HDR Client Renderer | Client-only camera capture, native portals, runtime raster uploads and LCD texture relay |

The Camera360 compatibility channels `481770120` / `481770121` are reserved separately. A source-provider endpoint is not a general drawing service. Native renderer resource names are not public consumer texture handles.

## Programmable blocks: block capabilities

![Block capability matrix](diagrams/block-capabilities.svg)

Use `H("capabilities", target)` or the small [HdrIngameApi helper](../../Api/Ingame/HdrIngameApi.cs). The result is `MyTuple<string,string,int>`:

- `Item1`: schema `HDR.DisplayCapabilities/1`.
- `Item2`: `lcd`, `projector`, `console`, or `unsupported`.
- `Item3`: structural flags below.

| Flag | Value | Meaning |
| --- | ---: | --- |
| `Supported` | 1 | Recognized block display |
| `NativeLcd` | 2 | Native LCD canvas |
| `CalibratedVectorLcd` | 4 | Known physical LCD screen-plane calibration |
| `Floating3D` | 8 | Retained floating geometry |
| `TableVolume` | 16 | Console/Holo Table volume |
| `ProjectedSurfaces` | 32 | Floating planar/curved/authored surfaces |
| `Ui` | 64 | Supported block UI attachment |

A Console/Holo Table is recognized through `IMyProjector` and its Console subtype. Floating geometry/surfaces require a projector; table volumes additionally require a Console. LCD support currently targets `IMyTextPanel`, not every `IMyTextSurfaceProvider` such as cockpit embedded screens.

Queries authenticate the live PB and target, construct relationship and access. They do not create a scene, claim an LCD, observe a new PB program generation or alter the selected target/screen. An authorized non-display block returns `unsupported` rather than gaining drawing authority.

`HDR.Api` retains its typed delegate signatures and product `Version`. The new `ApiVersion` reports `HDR.Api/1`; helpers negotiate this stable contract independently of product patch versions. The [typed helper](../../Api/HoloMapApi.cs) remains available for compatibility; new scripts can use the short endpoint.

## Mods: general local rendering

Mods connect through [HdrModApi](../../Api/Mods/HdrModApi.cs), use a unique consumer ID and create local HUD/world contexts. The bound endpoint owns all context handles. Replacement revokes the predecessor; stale handles cannot clear a successor. Dispose the helper during consumer unload.

HUD contexts use pixel coordinates and public `PostPP` billboard rendering just beyond the camera's near clip plane. **This path remains depth-tested**; it is not a promise of an always-on-top native GUI. World contexts use arbitrary validated affine poses and normal world depth. Vectors, text, SVG and consumer-owned registered texture materials work without the optional client renderer.

Menus use caller-supplied pointer coordinates and bounded event polling. HDR does not capture the OS cursor, pause player controls or execute remote callbacks. Consumers own input cooperation and their gameplay actions. Calls must run on the client main thread outside HDR drawing; this is a caller precondition, not a cross-thread marshalling service.

The mod and block categories share the viewer's `/hdr work` cap. When nearby powered block content or focused block UI demands work, mod consumers receive at most half of the cap; otherwise they can use the full available cap. Only actual submissions are charged. Owner/context budgets further divide the mod grant. `/hdr off` disables both.

See [mod integration](Mod-Integration.md) for command signatures, reconnect generations and the complete HUD/menu example.

## Optional plugins: explicit capability failure

| Feature key | Optional component |
| --- | --- |
| `raster-ui` | Runtime raster uploader in HDR Client Renderer |
| `camera-panorama` | Direct camera image renderer |
| `lcd-texture` | LCD texture relay bridge |
| `native-portal` | Native capture-shell/portal renderer |

`plugin-status` returns `MyTuple<bool,bool,string>`: **known feature**, **component registered locally**, **detail**. Registration does not certify GPU readiness, a completed image or every viewer's setup.

Missing components leave their native paths inactive and produce a bounded local message:

```text
Requires plugin: HDR Client Renderer (raster-ui).
```

Explicit raster requests do not silently become vectors when their uploader is absent. Ordinary vector paths remain available. Packaged texture materials and triangular sources do not require the uploader; external RGBA uploads do. Notices reset when the component reconnects and on world unload.

PBs execute on the server, while optional renderers belong to individual viewers. Dedicated servers report `Viewer-dependent capability` and retain valid declarations. Do not reject a replicated display because the server cannot see a viewer plugin. Each viewer safely handles its own absent capability.

The PB helper offers nonthrowing `TryCall`, `TryCapabilities` and `TryPluginStatus`; callers can `Echo(reason)`. Query success and plugin registration are different booleans. Invalid arguments still retain ordinary throwing `Call` behavior; the missing-plugin renderer path itself does not throw.

## Data, resource and trust ownership

- HDR stores bounded display content and prepares its rendering. Scanning, reconstruction, spatial caches and sensor permission policies belong to source mods such as CamScan.
- Camera image capture is an optional renderer feature, not a server-side ray-scanning system.
- Producer validity, user access, power, source generation and resource epochs are checked independently.
- Provider delegates, local menu callbacks and native resource objects do not enter PB payloads or scene replication.
- The server replicates PB display declarations; general mod contexts are local unless the consumer implements its own synchronization.
- Installed client mods are trusted code. Consumer IDs provide lifecycle ownership and collision avoidance, not authentication against another malicious installed mod.

See [API reference](Api-Reference.md), [security](../../Security.md) and [composition recipes](Composition-Recipes.md).
