# Native portal API

See the [current camera and portal wiki](../docs/wiki/Cameras-and-Portals.md) and [API reference](../docs/wiki/Api-Reference.md).


Paste `artifacts/PortalDemo.pb.cs` into a programmable block. On the same construct, name exactly one Console or Projector **HDR Portal**, compile, then Run with an empty argument. The entry plane appears 4 meters above the anchor. Its paired exit is 30 meters ahead in the anchor's local forward direction. A 5 meter ellipsoid shell at the exit excludes geometry inside the shell. Each viewer needs the optional HDR Client Renderer. Run `/hdr portal status` locally for availability and capture diagnostics.

The demo accepts `plane`, `ellipsoid`, `mesh`, `differential`, `stealth`, `strict`, `approximate`, `status`, and `clear`. `stealth` creates an opaque outward ellipsoid bubble centred 4 meters above the anchor, with radii 2, 1.5 and 2 meters and a 5 cm capture-shell margin. Look from outside the bubble. Its entry and exit skins coincide so the exterior replaces the interior with straight-through background beyond the shell. `differential` restores the selected paired shape and its exit 30 meters ahead. The demo removes only its own `portal` screen. A slow liveness poll repairs a lost screen; native capture and scheduling remain local to the viewing client.

`Api/PortalApi.cs` is an optional small PB wrapper around the `HDR.Draw` commands below. Entry geometry is the selected projected screen. Exit and shell poses use **anchor-local meters**, follow the anchor, and must be rigid right-handed transforms. No executable callback is replicated.

| Command | Arguments |
| --- | --- |
| `screen-portal-plane` | `MatrixD exitPose, double width, double height, string transport="differential", string accuracy="strict"` |
| `screen-portal-ellipsoid` | `MatrixD exitPose, Vector3D radii, string transport="differential", string accuracy="strict"` |
| `screen-portal-mesh` | `MatrixD exitPose, Vector3D[] points, int[] triangles, Vector2[] uv, string transport="differential", string accuracy="strict"` |
| `screen-portal-shell` | `MatrixD shellPose, Vector3D radii` |
| `screen-portal-settings` | `double normalScale=1, double saturation=1, double brightness=1` |
| `screen-portal-transport` | `string transport="differential", string accuracy="strict"` |
| `screen-portal-exit-side` | `string side="inside"` or `"outside"`; ellipsoid exits only |
| `screen-portal-charts` | `int entryChart=0, int exitChart=0` |
| `screen-portal-clear` | none; removes the paired declaration and its source binding |

The three shape commands bind the selected screen to provider `native-portal`, using the screen ID as its source ID. The first pair defaults its capture shell to a sphere of radius 10 meters at the exit pose; use `screen-portal-shell` to choose another exclusion volume. Use existing `screen-resolution` and `screen-refresh` commands for output and cadence. Portal declarations accept 64–4096 pixels per axis and 1–120 Hz. The current native capture ceiling is 2048 per axis; curved/nonlinear projection retains a bounded 512-square information grid. Actual detail and cadence follow visible physical presentation pixels, local budgets and GPU speed. The demo defaults to native detail and 60 Hz. These limits are independent of ordinary camera panorama settings.

Planes use width/height .1–500 meters. Ellipsoid and capture-shell radii use .05–250 meters. Mesh exits support 2048 vertices and 4096 triangles, finite coordinates within a 250 meter local radius, nondegenerate physical and UV triangles, and UV coordinates from 0 to 1. Entry geometry also obeys the existing 25 meter HDR screen range. Entry ellipsoids require full angular coverage. A mesh entry currently uses one authored triangle per screen; use separate screens for separate entry charts. An exit mesh may select any valid triangle chart. Analytic shapes require chart zero. Normal scale has signed magnitude 1/64–64; saturation 0–2; brightness 0–4.

`differential` transports rays through the paired surface derivatives. `stealth` requests straight-through transport and requires identical coincident entry and exit charts, including ellipsoid side. Configure the pair and exit side before selecting stealth. `strict` publishes only rays representable by the current native perspective capture; other pixels remain transparent. `approximate` explicitly permits sampling at the capture shell and can distort near geometry. The optional renderer reports missing native coverage and refuses output when it cannot guarantee the capture-shell exclusion.

The local provider endpoint `source-portaldescriptor` takes `{ "native-portal", long anchorId, long callerId, string sourceId }`. It returns `false` when demand or identity is invalid, otherwise a fresh `object[19]` v1 declaration: version, transport, accuracy, entry kind, entry world pose, entry extent, entry XYZ, entry triangles, entry UV, exit kind, exit world pose, exit extent, exit XYZ, exit triangles, exit UV, shell world pose, shell radii, `{normalScale,rate,saturation,brightness}`, and `{width,height,entryChart,exitChart}`. Kinds are plane 0, inward angular ellipsoid 1, UV mesh 2, and outward angular ellipsoid 3. Analytic mesh arrays are empty. Every returned array is detached from the replicated DTO and other consumers.
