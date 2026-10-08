# Getting started

HDR renders retained artwork on physical LCDs and in world space from Console/Projector anchors. A programmable block declares the display; each viewer renders it locally. The optional HDR Client Renderer adds raster UI, direct camera imagery and native visual portals. Scene scanning and reconstruction belong to CamScan API.

![Server declarations and local renderers](diagrams/architecture-boundaries.svg)

## Choose a display

| Goal | Start with | Client plugin |
|---|---|---|
| Physical LCD, text or vector instrumentation | [LCD setup](Display-Surfaces.md#physical-lcds) | Optional |
| Floating geometry or current-grid miniature | [Drawing](Drawing.md) on a Console/Projector | Optional |
| Plane, sphere, ellipsoid or authored mesh with interface artwork | [Projected surfaces](Display-Surfaces.md#projected-screens) | Required for raster mode; vectors work without it |
| Direct camera panorama | [Camera sphere](Cameras-and-Portals.md#direct-camera-panorama) | Required on each viewer |
| Paired portal or capture-shell bubble | [Native portals](Cameras-and-Portals.md#native-portals-and-capture-shells) | Required; supported native renderer runtime |
| A client vector HUD/menu supplied by another mod | [Mod integration](Mod-Integration.md) | Not required; cooperative input is supplied by that mod |
| Retained artwork bound to numeric sliders or rotors | [Interactive controls](Interactive-Controls.md) on a Console/Projector, or the admitted planar LCD subset | Required for native PB mouse capture; numeric math and cooperative mod input work without it |

The [PB capability matrix](Programmable-Blocks.md#block-capability-matrix) explains which block can own each kind of output. An LCD named “HDR Display” does not become a world-space projector.

The general mod HUD uses near-plane, depth-tested `PostPP` artwork. It is a separate owner-scoped client service with cooperative pointer/event handling, not PB access to global input or an always-on-top compositor.

## Install the normal mod

1. Build from the repository root with `./Build.ps1`. The build validates sources against installed game assemblies and generates paste-ready scripts in `artifacts/`.
2. Put the **contents** of `Mod/` in `%APPDATA%/SpaceEngineers/Mods/HDR API`. That folder must contain `metadata.mod` and `Data/` directly. Replace an old package as a unit so removed source files do not survive an upgrade.
3. Enable HDR API in the world's mod list and enable in-game scripts. Dedicated servers need the world mod, not the client plugin.
4. Put a powered PB and display on the same mechanical construct. The PB owner needs access to the display. Use an exact, distinctive block name.
5. Paste a generated example, compile, and run it. Recreate declarations after world load; retained scenes are not save-file storage.

For native client features, build `OptionalPlugins/HDRClientRenderer/Build.ps1 -Runtime Interim` or `-Runtime Legacy` to match the loader runtime. Close Space Engineers/Pulsar before replacing DLLs. Install the DLL and XML sidecar using [InstallClientPlugins.ps1](../../Tools/InstallClientPlugins.ps1), then enable the local plugin and restart. The installer backs up replacements, checks physical paths and verifies that Pulsar profiles are unchanged. Building alone does not install anything.

Native portal integration currently targets the certified Interim/.NET 10 renderer path. A successfully loaded plugin on another runtime does not establish native portal capability. `/hdr portal status` reports the actual local gate.

## First physical LCD

Name a panel **HDR LCD**, select **Content → HDR API**, then build and paste the generated `LcdDemo.pb.cs`. Its maintained source is [LcdDemo.cs](../../Examples/LcdDemo.cs). Run `demo`; `vector` requests calibrated screen-pinned geometry and `native` restores native LCD frames. `rate 30` requests faster metadata/animation sampling, subject to the backend's own schedule.

## First floating display

Name a Console or Projector **HDR Display**. Paste the generated `GeneralSurfaceDemo.pb.cs`, whose source is [GeneralSurfaceDemo.cs](../../Examples/GeneralSurfaceDemo.cs), and run `ellipsoid` or `mesh`. It needs no camera feed. This is a useful first test of world-space placement, sidedness and surface mapping. Console anchors have a default table envelope; large surfaces outside it need that envelope disabled as described in [projection envelopes](Display-Surfaces.md#clipping-and-projection-envelopes).

The short drawing example [DrawDemo.cs](../../Examples/DrawDemo.cs) instead targets **Holo Map** and demonstrates retained lines, text and mod-managed animation. [ConsoleDemo.cs](../../Examples/ConsoleDemo.cs) demonstrates a live construct miniature.

## First interactive control display

Name a Console/Projector **HDR Controls**, paste the built [InteractiveControlsDemo.cs](../../Examples/InteractiveControlsDemo.cs) example, and run `demo`. It declares throttle, trim and angle controls once, then explicitly synchronizes real PB variables through numeric queries/events and revision-checked source writes. A fixed wake argument tells the PB to poll; HDR does not reflect arbitrary fields or callbacks. The [interaction guide](Interactive-Controls.md) covers the exact declarations, event tuple and source commands.

For native mouse operation, stand on foot in first-person gameplay and press **Use** on an authored hotzone. The bundle viewer keeps the artwork in the world without a popup. Actual mouse-down starts a gesture; mouse-up commits/releases only that value gesture and keeps the cursor/viewer open for another control. **Escape** or context loss closes it. Hotzones are author rectangles, not pixel-perfect artwork tests. LCD picking supports planar XY paths and local-Z rotation with admitted pose proof; curved/mesh surface picking is unavailable.

Native PB mouse capture requires the optional HDR Client Renderer 0.9.13 or later pointer provider. Query `H("plugin-status", "interactive-pointer")` for `MyTuple<bool,bool,string>`: known feature, local registration, explanation. Registration does not prove actual routed input or an admitted viewer/gesture; a dedicated server reports viewer dependency with local registration false. The numeric core and a cooperating client mod's controls can run without it. Compilation and offline tests pass, while live click/drag acceptance remains unverified.

## Verify the actual backend

Run `/hdr status` locally. **“Raster backend connected” and a positive UI texture count** confirm raster UI upload. An explicit raster request with no plugin stays empty and reports `Requires plugin: HDR Client Renderer (raster UI).` It does not silently replace that request with vectors. Ordinary vector content remains plugin-free; a connected backend's unsupported/busy/failing composition can still use its explicit logged vector fallback. Direct camera or portal failures need their own `/hdr camera status` or `/hdr portal status` diagnostics.

Offline checks establish compilation, bounded data handling, geometry/matrix behavior and selected installed-engine ABI contracts. They do not certify final GPU pixels, frame times, driver stability or multiplayer delivery. Use [MultiplayerValidation.md](../../MultiplayerValidation.md) for live acceptance.

Continue with [PB integration](Programmable-Blocks.md), [drawing reference](Drawing.md), [surfaces](Display-Surfaces.md), or [performance troubleshooting](Performance-and-Troubleshooting.md).
