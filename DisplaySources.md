# External display sources (HDR API 0.7.0)

Current API and usage: [HDR API wiki](docs/wiki/Mod-Integration.md). This page retains lower-level and historical design notes; the wiki states current capabilities and limits.


HDR draws data supplied by PBs or optional client-local providers. Camera scanning, sensor permissions, retained observations and reconstruction belong to CamScan API. HDR works without it.

## PB commands

Create/select a projected screen with the normal drawing endpoint, then attach a provider's named source:

```csharp
H("target", "HDR Display");
H("screen", "feeds", MatrixD.CreateTranslation(0, 1.5, 0), 3.2, 1.8);
H("screen-source", "camscan", "feeds");
H("text", "title", "CAMERA ARRAY", 0, 0.7, 0, 0.12, "cyan");
```

The provider/source pair identifies data; it does not configure acquisition or grant access. Configure the source through that provider's own API. `screen-source-clear` detaches it. `screen-scene` replaces it with explicitly supplied synthetic geometry. Ordinary overlays, layers, backgrounds and screen controls remain HDR commands.

The former `screen-cameras`, `screen-camera-layout`, `screen-camera-page` and `screen-cameras-clear` commands are removed. The migrated camera demo lives in `CamScan API/Examples/CameraDeckDemo.cs`. Use both mods and regenerate the PB script for this version.

If a provider is missing, replaced, stopped or rejects validity, its image becomes empty; the screen's background and ordinary overlays can still draw. There is no stale-frame fallback across provider generations or failed validity checks.

## Rendering budgets

Select the anchor before `H("budget", points, primitives, drawWork)`. Geometry defaults are **0 points / 0 primitives = unlimited**, independently for each count; drawing work retains its separate **20,000** default and 1,000–200,000 range. Geometry settings accept nonnegative Int32 counts: positive values select finite allowances, and Int32.MaxValue normalizes to unlimited/query zero. Choosing a finite geometry allowance below declared geometry is rejected atomically. `budget-settings` returns `MyTuple<int,int,int>`, including zeros for unlimited geometry. An accepted change invalidates only that anchor's presentation caches.

Visible static content has priority. When configured, finite geometry allowances are shared fairly by visible external-source screens; zero removes that aggregate ceiling. Hidden or orphaned cached content does not consume that allocation. Each prepared frame still has the existing per-object ceiling of 2,048 points and 4,096 primitives, and provider payload/resource bounds remain independent. The provider can reduce detail within an offered finite allowance, preserving its own data semantics.

Each viewer separately controls its global draw-work cap with `/hdr work N`, initially 20,000 per frame. An anchor budget cannot raise another viewer's local cap. These are display allowances; they do not configure sensor rays, source memory or acquisition rates.

## Local mod protocol

Protocol 1 uses game/standard CLR types only. It never crosses multiplayer transport or enters a PB's object graph.

- Discovery channel `481770100`: HDR publishes its `Func<string, object[], object>` service. Providers retain the service and reply on registration. Providers should also register at startup, so either mod load order works.
- Registration channel `481770101`: `MyTuple<string, int, Func<string, object[], object>>` containing provider ID, protocol 1 and endpoint. Provider IDs use bounded lowercase identifiers; at most eight providers are retained.
- Unload: call the retained HDR service's `unregister` with `{ providerId, sameEndpointDelegate }`. Matching the registered endpoint prevents a departing old generation from removing a replacement.

HDR calls these provider endpoint commands:

| Command | Arguments | Result |
| --- | --- | --- |
| `begin`, `end` | none | Per-draw validity memoization scope |
| `frame` | See ordered fields below | Prepared triangular mesh and opaque evidence |
| `valid` | anchor ID, caller PB ID, source ID, evidence | Boolean; must describe the exact submitted payload |

`frame` receives an `object[]` in this order:

1. Anchor entity ID (`long`).
2. Caller PB entity ID (`long`).
3. Provider source ID (`string`).
4. HDR screen ID (`string`).
5. Available point budget (`int`).
6. Available triangle/primitive budget (`int`).
7. Canvas width (`double`).
8. Canvas height (`double`).
9. Detached twelve-number perspective view (`double[]`: eye, target, up, vertical FOV, near, far).
10. Raster width (`int`).
11. Raster height (`int`).
12. Orbit rate (`double`).
13. Orbit start tick (`double`).
14. HDR's synchronized animation time in ticks (`double`).

The protocol 1 result is `MyTuple<Vector3D[], int[], Vector4[], object, bool, double>`: points, triangle indices, one RGBA color per triangle, opaque validity evidence, reduced-detail flag and requested refresh Hz (.1–6). Coordinates are centered physical canvas units, Y up, on the Z=0 plane. Protocol 1 and its fourteen-field request remain compatible with CamScan. General HDR line/wire commands remain available independently.

HDR bounds array lengths before copying, then validates the detached indices, finite plane coordinates and colors against the offered budget and canvas. It retains the producing endpoint/generation and checks evidence after preparation and immediately before drawing. Callback exceptions or invalid evidence clear the source image. Neither evidence nor prepared frames are included in HDR scene serialization. Network declarations contain only provider/source IDs; removed camera-policy protobuf tags remain reserved.

HDR exposes `source-state` on its service with `{ providerId, anchorId, callerId, sourceId }`. It returns whether a corresponding source is currently useful to a visible, powered, nearby screen facing this client and intersecting its view frustum. A false result suspends expensive work; it does not mean the provider's authoritative source was deleted. The provider owns configuration lifetime, retained-data policy, permission leases and any immediate revocation.

`source-demand` accepts the same arguments and returns `MyTuple<int,int,double,bool>`: effective width, height, requested refresh cap, and useful. It combines visible screens' demands and caps the aggregate to 1,024 per side / 262,144 pixels. An inactive source returns `(0,0,0,false)`. This client-local hint lets a cooperating producer avoid captures and limit its upstream resolution. It does not automatically alter a separate plugin's settings, and is not an authorization proof.

## Raster protocol 2

Register the same provider tuple with version **2**. The request appends effective image width and height (`int`) as fields 15 and 16; no pixel buffers are included in server declarations. A v2 provider must return `MyTuple<int,object,object,bool,double>`: frame kind, payload, opaque evidence, reduced-detail flag, requested refresh cap (.1–60 Hz).

| Kind | Payload | Meaning |
| --- | --- | --- |
| 1 | `MyTuple<Vector2I,byte[],int>` | Image size, detached straight-alpha sRGB RGBA8 bytes, format flags **0** |
| 2 | `MyTuple<string,Vector2I,long>` or `MyTuple<string,Vector2I,long,Vector4>` | Bounded local material name, physical texture size, positive resource generation; optional normalized UV rectangle `(x,y,width,height)` |

Kind 1 requires the optional local raster upload backend; dimensions are 16–1,024, at most 262,144 pixels, exactly `width*height*4` bytes. Kind 2 borrows a provider-owned material rather than loading a file or URL; declared dimensions are bounded to 4,096. An optional UV rectangle must be finite, positive and contained in [0,1]; legacy frames use the full texture. Consumers keep the rectangle with its lease, so providers must retain its layout until ownership transfers. Both kinds use the usual `valid` check, including producing endpoint/generation. The positive texture generation supplements the opaque proof. A v2 provider handles optional `release` with `{ evidence }` when HDR retires a submitted frame. Repeated frames may reuse the same evidence; each independently owned screen has its own lease. Release must not destroy another screen's resource.

V2 preparation allows at most four ready source calls per draw tick with oldest-attempt scheduling. Actual upload/copy quotas may reduce the achieved rate further. Offscreen sources receive no `frame` call. Refresh settings are ceilings, and the client also retains its own rendering and sampling caps. Camera rendering must be controlled by the provider; HDR does no capture itself.

Installed mods are trusted local code. This interface bounds cooperative data and catches ordinary provider faults; it cannot sandbox a malicious installed mod or guarantee the execution time of an arbitrary delegate. Offline integration checks must be followed by an in-game two-mod load, provider loss/reload and multiplayer test.
