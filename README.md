# HDR API

> # ⚠️ ALPHA
> **Experimental. Expect bugs, breaking changes and possible game or GPU-driver crashes with native rendering features. Back up your saves before testing.**

HDR API is a display and hologram rendering library for **Space Engineers**. Programmable blocks and other mods can use it to draw custom interfaces on LCDs, project artwork into the world, or build their own HUDs and menus.

## What it can do

- Draw text, lines, shapes, SVG artwork, images and 3D geometry.
- Display floating panels and wrap content onto cylinders, spheres, ellipsoids or custom meshes.
- Animate holograms with flicker, moving scan bars, depth layers, particles, projection rays and transitions.
- Show a live grid preview and combine artwork with data from other mods.
- Create buttons, menus and [numeric sliders, constrained handles and rotation controls](docs/wiki/Interactive-Controls.md), with two-way script/mod values and retained artwork poses.
- With the optional **HDR Client Renderer**, display camera feeds, raster interfaces and visual portals.

An optional [HTML/CSS frontend prototype](OptionalMods/HDRHtmlFrontend/README.md) lets programmable blocks author interfaces on LCDs, Projectors and Consoles, and lets client mods build HUDs and flat world panels. Its bounded profile uses existing vector/SVG renderers without a plugin; client mods can also select explicitly owned native LCD sprites. PB mouse controls use the existing optional input provider. It is not a complete browser and does not run JavaScript.

PBs can also select native sprites on a caller-owned LCD, or project that LCD's native texture onto a Console/Projector with the existing client renderer. See the [PB HTML/CSS guide](OptionalMods/HDRHtmlFrontend/Docs/PB-API.md) for backend selection, input and source-surface setup.

Ordinary vector displays, hologram effects, numeric source writes and cooperative mod controls work **without a client plugin**. Native PB mouse controls require **HDR Client Renderer 0.9.13+**; an absent provider leaves them inactive with a printable `Requires plugin` reason. Camera scanning and reconstruction belong to separate data-provider mods.

## Simple setup

1. Download or clone this repository.
2. Copy the contents of [`Mod`](Mod) into `%APPDATA%\SpaceEngineers\Mods\HDR API`.
3. Add **HDR API** to your world's mod list and enable **In-game scripts**.
4. Place a powered Console, Projector or LCD and a Programmable Block on the same construct. Name the display **Holo Map**. For an LCD, select **HDR API** as its Content.
5. Paste the complete [`DrawDemo.cs`](Examples/DrawDemo.cs) into the Programmable Block and run it with the argument `demo`.

You should see text, a line and an animated ring. Run `clear` to remove the demo.

For the effects demonstration, name a Console or Projector **HDR Effects** and use [`HologramEffectsDemo.cs`](Examples/HologramEffectsDemo.cs). More examples and their required block names are in the [examples guide](Examples/README.md).

### Optional client renderer

Camera feeds, raster displays, portals and native PB mouse controls use the optional client renderer. Each viewer who uses these features installs it through a compatible client plugin loader such as Pulsar. A dedicated server does not need the client plugin. On foot in first-person view, **Use** enters an authored control bundle; mouse-up ends a value gesture while the viewer remains open, and **Escape** closes it. Install HDR Client Renderer 0.9.13 or later for native PB mouse controls. Live input acceptance remains unverified.

See the [client renderer setup guide](OptionalPlugins/HDRClientRenderer/README.md). Close the game before replacing plugin DLLs, then restart it through the loader.

## Documentation

- [Getting started](docs/wiki/Getting-Started.md)
- [GitHub wiki](https://github.com/PommesMitChips/HDR-API/wiki), including examples and SVG diagrams
- [Documentation sources](docs/wiki/README.md)
- [Programmable-block API](docs/wiki/Programmable-Blocks.md)
- [Hologram effects](docs/wiki/Special-Effects.md)
- [Interactive artwork controls](docs/wiki/Interactive-Controls.md)
- [HUDs, menus and integration with other mods](docs/wiki/Mod-Integration.md)
- [HTML/CSS frontend prototype](docs/wiki/HTML-Frontend.md)
- [API boundaries](docs/wiki/API-Boundaries.md) and [security](Security.md)
- [Release history](CHANGELOG.md)

## Build from source

With Space Engineers installed and the .NET 10 SDK available, run:

```powershell
.\Build.ps1
```

This validates the mod and examples, generates paste-ready scripts in `artifacts`, and creates a mod package. The optional renderer has a separate build described in its setup guide. See [contributing and build instructions](docs/Contributing.md) for custom game paths and checks.

Native rendering and mouse interaction are experimental; offline tests do not replace in-game testing. Third-party notices are retained alongside their assets and integrations.
