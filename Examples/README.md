# Examples

PB source files stay in this directory because the build compiles and generates paste-ready versions from their canonical names. Copy generated `artifacts/*.pb.cs` into a PB; do not combine generated wrappers from unrelated demos.

| Goal | Maintained source | Notes |
| --- | --- | --- |
| First short-command script | [DrawDemo.cs](DrawDemo.cs) | Small `HDR.Draw` forwarding function |
| Plain HTML/CSS interface | [Tiny HTML PB invocation](../OptionalMods/HDRHtmlFrontend/Examples/HtmlPbDemo.cs) + [HTML document](../OptionalMods/HDRHtmlFrontend/Examples/PlainHtmlPbDemo.html) | Put HTML in PB Custom Data; frontend 0.2.2 owns setup, fitting, bindings, refresh and cleanup |
| Retained Console/Projector geometry | [ConsoleDemo.cs](ConsoleDemo.cs) | Includes live construct preview |
| Native/vector LCD | [LcdDemo.cs](LcdDemo.cs) | Physical screen rendering |
| SVG/images | [SvgDemo.cs](SvgDemo.cs) | Artwork and packaged materials |
| Exported SVG animation | [SvgFramesDemo.cs](SvgFramesDemo.cs) | Authoring input in [Authoring](Authoring/pulse.mjs) |
| Appearance and layers | [AppearanceDemo.cs](AppearanceDemo.cs) | Colors, transparency and transforms |
| Hologram effects | [HologramEffectsDemo.cs](HologramEffectsDemo.cs) | Plugin-free depth, flicker, refresh bars, particles, rays and transitions; target **HDR Effects** |
| Interactive artwork controls | [InteractiveControlsDemo.cs](InteractiveControlsDemo.cs) | Line/polyline/rotation values; target **HDR Controls**; native PB mouse requires Client Renderer 0.9.13+ |
| Block UI | [UiDemo.cs](UiDemo.cs) | Fixed authorized actions and look-and-use |
| Floating screen | [ProjectedScreenDemo.cs](ProjectedScreenDemo.cs) | Projector/Console surface |
| Raster/vector surface | [RasterSurfaceDemo.cs](RasterSurfaceDemo.cs) | Raster mode needs Client Renderer |
| General ellipsoid/mesh | [GeneralSurfaceDemo.cs](GeneralSurfaceDemo.cs) | Authored projection geometry |
| Direct camera sphere | [DirectCameraSphereDemo.cs](DirectCameraSphereDemo.cs) | Client capture, no LCD buffers |
| Legacy camera LCD relay | [SphericalCameraDemo.cs](SphericalCameraDemo.cs) | Relay path, distinct from direct capture |
| Native portal/capture shell | [PortalDemo.cs](PortalDemo.cs) | Optional native renderer |
| Multi-display retained animation | [MultiConsoleAnimationDraw.cs](MultiConsoleAnimationDraw.cs) | Short API player |
| Packed Custom Data playback | [PackedPelicanDraw.cs](PackedPelicanDraw.cs) | Mod-managed playback |
| Broader feature demonstration | [HdrFeaturesDemo.cs](HdrFeaturesDemo.cs) | Combined rendering features |
| Client HUD/menu consumer mod | [Mods/HudMenuModExample.cs](Mods/HudMenuModExample.cs) | Copy [HdrModApi.cs](../Api/Mods/HdrModApi.cs) into the consumer mod |
| Client mod effects | [Mods/HologramEffectsModExample.cs](Mods/HologramEffectsModExample.cs) | World effects and planar HUD particles/transitions |
| Cooperative client-mod controls | [Mods/InteractiveControlsModExample.cs](Mods/InteractiveControlsModExample.cs) | Client-local variables and consumer-owned input; no client plugin |

`Api/HoloMapApi.cs` is the compatibility typed PB helper. [Api/Ingame/HdrIngameApi.cs](../Api/Ingame/HdrIngameApi.cs) is the smaller current PB helper; [Api/Mods](../Api/Mods/HdrModApi.cs) contains the separate client-mod wrapper. The mod example is deliberately outside the flat PB source directory so it is compiled as mod code rather than pasted into a programmable block.

See the [composition recipes](../docs/wiki/Composition-Recipes.md) and [API reference](../docs/wiki/Api-Reference.md).

The [HTML authoring tutorial](../OptionalMods/HDRHtmlFrontend/Docs/PB-Tutorial.md) is the normal HTML/CSS path. The separate [advanced bindings demo](../OptionalMods/HDRHtmlFrontend/Examples/HtmlPbBindingsDemo.cs) shows optional C# gameplay values and event polling; its code is not required to render an HTML interface.

The [interactive controls guide](../docs/wiki/Interactive-Controls.md) describes ranges/snapping, explicit value bindings and source-write preemption. Native PB mouse input is ALPHA: Use enters the world bundle, mouse-up ends only the gesture, and Escape/context loss closes the viewer. The renderer build and offline checks do not establish installation or live input acceptance.
