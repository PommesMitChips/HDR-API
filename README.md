# HDR API

> # ⚠️ ALPHA
> **Experimental. Expect bugs, breaking changes and possible game or GPU-driver crashes with native rendering features. Back up your saves before testing.**

HDR API is a display and hologram rendering library for **Space Engineers**. Programmable blocks and other mods can use it to draw custom interfaces on LCDs, project artwork into the world, or build their own HUDs and menus.

## What it can do

- Draw text, lines, shapes, SVG artwork, images and 3D geometry.
- Display floating panels and wrap content onto cylinders, spheres, ellipsoids or custom meshes.
- Animate holograms with flicker, moving scan bars, depth layers, particles, projection rays and transitions.
- Show a live grid preview and combine artwork with data from other mods.
- Create buttons and menus; numeric sliders, constrained handles and rotation controls are being integrated.
- With the optional **HDR Client Renderer**, display camera feeds, raster interfaces and visual portals.

Ordinary vector displays and hologram effects work **without a client plugin**. Features that need one remain inactive and explain the requirement when it is missing. Camera scanning and reconstruction belong to separate data-provider mods.

## Simple setup

1. Download or clone this repository.
2. Copy the contents of [`Mod`](Mod) into `%APPDATA%\SpaceEngineers\Mods\HDR API`.
3. Add **HDR API** to your world's mod list and enable **In-game scripts**.
4. Place a powered Console, Projector or LCD and a Programmable Block on the same construct. Name the display **Holo Map**. For an LCD, select **HDR API** as its Content.
5. Paste the complete [`DrawDemo.cs`](Examples/DrawDemo.cs) into the Programmable Block and run it with the argument `demo`.

You should see text, a line and an animated ring. Run `clear` to remove the demo.

For the effects demonstration, name a Console or Projector **HDR Effects** and use [`HologramEffectsDemo.cs`](Examples/HologramEffectsDemo.cs). More examples and their required block names are in the [examples guide](Examples/README.md).

### Optional client renderer

Camera feeds, raster displays and portals use the optional client renderer. Each viewer who uses these features installs it through a compatible client plugin loader such as Pulsar. A dedicated server does not need the client plugin. Native mouse dragging is still being integrated; the current client renderer source includes its experimental input provider.

See the [client renderer setup guide](OptionalPlugins/HDRClientRenderer/README.md). Close the game before replacing plugin DLLs, then restart it through the loader.

## Documentation

- [Getting started](docs/wiki/Getting-Started.md)
- [GitHub wiki](https://github.com/PommesMitChips/HDR-API/wiki), including examples and SVG diagrams
- [Documentation sources](docs/wiki/README.md)
- [Programmable-block API](docs/wiki/Programmable-Blocks.md)
- [Hologram effects](docs/wiki/Special-Effects.md)
- [HUDs, menus and integration with other mods](docs/wiki/Mod-Integration.md)
- [API boundaries](docs/wiki/API-Boundaries.md) and [security](Security.md)
- [Release history](CHANGELOG.md)

## Build from source

With Space Engineers installed and the .NET 10 SDK available, run:

```powershell
.\Build.ps1
```

This validates the mod and examples, generates paste-ready scripts in `artifacts`, and creates a mod package. The optional renderer has a separate build described in its setup guide. See [contributing and build instructions](docs/Contributing.md) for custom game paths and checks.

Native rendering and mouse interaction are experimental; offline tests do not replace in-game testing. Third-party notices are retained alongside their assets and integrations.
