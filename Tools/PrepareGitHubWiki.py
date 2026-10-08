"""Stage the public GitHub wiki from maintained documentation; never push or alter Git.

Run with Python 3.10+ from any directory.  Generated files are intentionally
separate from a cloned wiki, so existing pages (including _new) remain untouched.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import quote, unquote, urlsplit
import xml.etree.ElementTree as ET


REPOSITORY = "https://github.com/PommesMitChips/HDR-API"
WIKI = REPOSITORY + "/wiki"
WIKI_RAW = "https://raw.githubusercontent.com/wiki/PommesMitChips/HDR-API/"
LINK = re.compile(r"(!?\[[^\]\n]*\])\(([^\n]+?)\)")
GUIDES = [
    ("Getting-Started", "Getting started"),
    ("API-Boundaries", "API boundaries"),
    ("Programmable-Blocks", "Programmable blocks"),
    ("Drawing", "Drawing and animation"),
    ("Interactive-Controls", "Interactive controls"),
    ("HTML-Frontend", "HTML/CSS frontend prototype"),
    ("JavaScript-Prototype", "JavaScript runtime prototype"),
    ("Special-Effects", "Hologram effects"),
    ("Display-Surfaces", "Display surfaces"),
    ("Cameras-and-Portals", "Cameras and visual portals"),
    ("Mod-Integration", "Integrating other mods"),
    ("Composition-Recipes", "Composition recipes"),
    ("Composition", "Combining content, surfaces and input"),
    ("Api-Reference", "API reference"),
    ("Performance-and-Troubleshooting", "Performance and troubleshooting"),
]


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def anchors(text: str) -> set[str]:
    result: set[str] = set()
    seen: dict[str, int] = {}
    in_fence = False
    for line in text.splitlines():
        if re.match(r"^\s*(```|~~~)", line):
            in_fence = not in_fence
        if in_fence or not re.match(r"^#{1,6}\s", line):
            continue
        name = re.sub(r"^#{1,6}\s+", "", line).strip().lower()
        name = re.sub(r"[^\w\- ]", "", name).replace(" ", "-")
        suffix = seen.get(name, 0)
        seen[name] = suffix + 1
        result.add(name if suffix == 0 else f"{name}-{suffix}")
    return result


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig").replace("\r\n", "\n")


def default_root() -> Path:
    # Works in Tools/ after promotion and in ignored publication staging before it.
    for candidate in Path(__file__).resolve().parents:
        if (candidate / "docs/wiki").is_dir() and (candidate / "Mod/Data").is_dir():
            return candidate
    raise RuntimeError("Cannot locate HDR API; pass an explicit --repository-root.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", type=Path,
                        default=default_root())
    parser.add_argument("--output", type=Path,
                        help="Wiki checkout contents only; defaults to artifacts/github-wiki/generated.")
    parser.add_argument("--release", help="Defaults to the maintained wiki index release.")
    parser.add_argument("--scene-protocol", help="Defaults to the maintained wiki index scene protocol.")
    parser.add_argument("--client-renderer", help="Defaults to the maintained wiki index optional renderer.")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    root = args.repository_root.resolve()
    output = (args.output if args.output else root / "artifacts/github-wiki/generated").resolve()
    wiki_source = root / "docs/wiki"
    sources = sorted(wiki_source.glob("*.md"))
    source_to_page = {p.resolve(): ("Documentation-Index" if p.stem == "README"
                                  else p.stem) for p in sources}
    pages: dict[str, str] = {}
    assets: dict[str, Path] = {}
    dependencies: dict[str, dict[str, str]] = {}
    errors: list[str] = []
    published_index = read_text(wiki_source / "README.md")
    declared_versions = re.search(
        r"HDR API \*\*([^*]+)\*\*, scene protocol \*\*([^*]+)\*\*, optional Client Renderer \*\*([^*]+)\*\*",
        published_index)
    if declared_versions:
        for name, value in zip(("release", "scene_protocol", "client_renderer"), declared_versions.groups()):
            if getattr(args, name) is None:
                setattr(args, name, value)
    if not declared_versions or declared_versions.groups() != (
            args.release, args.scene_protocol, args.client_renderer):
        errors.append("Release arguments must match the maintained docs/wiki/README.md version tuple.")

    def convert(source: Path, target: str) -> str:
        target = target.strip().strip("<>")
        if re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", target):
            return target
        path_part, _, fragment = target.partition("#")
        resolved = ((source.parent / unquote(path_part)).resolve()
                    if path_part else source.resolve())
        if not resolved.exists():
            errors.append(f"{source.relative_to(root)}: missing target {target}")
            return target
        if resolved in source_to_page:
            result = WIKI + "/" + quote(source_to_page[resolved], safe="-")
        elif resolved.suffix.lower() == ".svg" and resolved.parent == wiki_source / "diagrams":
            relative_asset = "assets/diagrams/" + resolved.name
            assets[relative_asset] = resolved
            result = WIKI_RAW + quote(relative_asset, safe="/")
        else:
            try:
                relative = resolved.relative_to(root).as_posix()
            except ValueError:
                errors.append(f"{source.relative_to(root)}: target leaves repository {target}")
                return target
            if relative.startswith(("artifacts/", ".git/")):
                errors.append(f"{source.relative_to(root)}: private/generated target {target}")
            result = REPOSITORY + "/blob/main/" + quote(relative, safe="/")
            dependencies[relative] = {
                "url": result,
                "sha256": digest(resolved) if resolved.is_file() else "directory",
            }
        return result + ("#" + fragment if fragment else "")

    # Preserve source prose and fenced examples byte-for-byte except line endings.
    for source in sources:
        lines: list[str] = []
        in_fence = False
        for line in read_text(source).splitlines(keepends=True):
            if re.match(r"^\s*(```|~~~)", line):
                in_fence = not in_fence
                lines.append(line)
            elif in_fence:
                lines.append(line)
            else:
                lines.append(LINK.sub(lambda m: m.group(1) + "(" +
                                     convert(source, m.group(2)) + ")", line))
        pages[source_to_page[source.resolve()]] = "".join(lines)

    page_links = "\n".join(f"- [{title}]({WIKI}/{name})" for name, title in GUIDES)
    pages["Home"] = f"""# HDR API

## ⚠️ ALPHA — experimental software

> HDR API is in active development. APIs and behavior can change. Native camera,
> raster and portal features have caused game freezes and GPU-driver crashes
> during development. Start with a small test world and keep backups.

HDR API is a **display and hologram rendering library for Space Engineers**.
Programmable blocks can draw on LCDs and project artwork from Console/Projector
anchors. Other mods can use HDR to build local HUDs, menus and world-space interfaces.

The documented release is **HDR API {args.release}**, scene protocol
**{args.scene_protocol}**, with optional **HDR Client Renderer {args.client_renderer}**.

## What it can do

- Draw text, lines, shapes, SVG, images and 3D geometry.
- Wrap artwork onto planes, cylinders, spheres, ellipsoids or authored meshes.
- Add flicker, moving refresh bars, depth layers, cosmetic particles, projection
  rays and fade/wipe/dissolve transitions.
- Show a live grid preview and display content supplied by other mods.
- Create buttons, menus, sliders and constrained path/rotation handles with
  numeric values and script/mod value bindings.
- With the optional client renderer, show camera panoramas, raster interfaces
  and visual portals/capture shells.

Optional [HTML Frontend 0.2.2]({WIKI}/HTML-Frontend) provides a bounded HTML/CSS
authoring profile. Put plain HTML and inline CSS in PB Custom Data; a short
invocation mounts it while the mod owns display setup, bindings, refresh and
cleanup. Larger C# helpers are optional. The separate [JavaScript Runtime 0.1.0]({WIKI}/JavaScript-Prototype)
adds explicitly registered client-local ES5.1 scripts, closures, events and timers
without a plugin. It is a UI prototype, not a complete browser or PB JavaScript
endpoint. The source mod includes Jint's BSD 2-Clause license and dependency notices.

Ordinary vectors and hologram effects work without a client plugin. Plugin-only
features remain inactive and explain the requirement when the component is absent.
Native block UI mouse interaction requires **HDR API 0.9.9 or later** and
**HDR Client Renderer 0.9.13 or later** on each participating viewer. Look at a
control and press **Use** to enter its UI, then click or drag controls on the
original display. Releasing the mouse commits the current gesture while keeping
the UI focused; **Escape** or context loss exits. Other mods can provide their own
cooperative pointer input without the plugin. See
[interactive controls]({WIKI}/Interactive-Controls) for setup, values and limits.
The feature is experimental; [development status]({WIKI}/Development-Status)
separates offline checks from live acceptance.

## Simple setup

1. Download or clone [the repository]({REPOSITORY}).
2. Copy the contents of `Mod/` into `%APPDATA%\\SpaceEngineers\\Mods\\HDR API`.
   The destination must contain `metadata.mod` and `Data/` directly.
3. Add **HDR API** to your world's mod list and enable **In-game scripts**.
4. Put a powered display and PB on the same construct. Name the display
   **Holo Map**. On an LCD select **HDR API** as its Content.
5. Paste the complete [DrawDemo.cs]({REPOSITORY}/blob/main/Examples/DrawDemo.cs)
   into the PB and run `demo`. Run `clear` to remove it.

For hologram effects, name a Console or Projector **HDR Effects** and use
[HologramEffectsDemo.cs]({REPOSITORY}/blob/main/Examples/HologramEffectsDemo.cs).
For interactive controls, follow the
[interactive controls guide]({WIKI}/Interactive-Controls) and its complete examples.
Each viewer using native features installs the optional client renderer. A
dedicated server does not need that plugin. See [getting started]({WIKI}/Getting-Started)
for builds, installation and backend checks.

## Guides and reference

{page_links}

![API execution and ownership boundaries]({WIKI_RAW}assets/diagrams/architecture-boundaries.svg)

The [complete documentation index]({WIKI}/Documentation-Index) includes the
maintained examples. [Source code]({REPOSITORY}), [release history]({REPOSITORY}/blob/main/CHANGELOG.md)
and [security notes]({REPOSITORY}/blob/main/Security.md) remain in the repository.
"""
    pages["Development-Status"] = f"""# Development status

**ALPHA.** This wiki documents HDR API {args.release} / Client Renderer
{args.client_renderer}. Native rendering requires live testing and is not a
promise of driver stability or final visual quality.

## Available in the documented release

Retained drawing, SVG/text/geometry, LCD and world-space surfaces, effects,
native look-and-Use buttons, numeric interactive controls, consumer-owned mod
HUD/world contexts, optional raster UI, direct camera panoramas and native visual
portals are documented
in their [feature guides]({WIKI}/Documentation-Index). Each guide states bounds,
ownership, dependencies and validation limits.

The optional HTML Frontend **0.2.2** and JavaScript Runtime **0.1.0** provide
mod-native parsing/layout and scoped client-local ES5.1 events/timers. Callback
errors drop unflushed changes. Confirmed restoration preserves the prior frame;
uncertain publication retires visible output and input. Native source features
retain their provider checks. Broader CSS timelines, DOM features and Canvas remain
planned; the [JavaScript guide]({WIKI}/JavaScript-Prototype) records that expansion
policy separately from implemented capabilities.

## Supported, experimental: numeric interactive controls

The interaction API includes bounded numeric values, sliders, constrained
line/polyline paths and rotation handles, event polling, revision-checked writes
and local mod value bindings. Programmable-block controls use server-validated
declarations and canonical values. Mod consumers retain local contexts and supply
their own cooperative input.

For native block mouse interaction, use HDR API **0.9.9+** with optional
HDR Client Renderer **0.9.13+**. Press **Use** on an authored control to focus its
bundle, then click or drag controls on the original display. Mouse-up ends the
current gesture and releases its value lock; it keeps the viewer session open.
**Escape** or context loss releases the viewer session and input ownership.
Missing native pointer support remains inactive and explains the plugin
requirement. Cooperative mod pointer handling does not require that plugin.

Offline checks cover interaction math, declaration/value admission, ownership,
revision and gesture lifetimes, transport, and the pointer-provider state machine.
These checks do not certify actual in-game GUI behavior or real multiplayer
delivery. See [interactive controls]({WIKI}/Interactive-Controls) for the exact
commands, examples and supported display/input scopes.

## Live acceptance still pending

Native mouse acquisition/release, cursor placement on moving artwork, multiplayer
gestures and final GPU visuals/performance need live-game acceptance. ALPHA APIs
can change. Supported controls should not be read as a promise of browser-style
text editing, arbitrary global input access or general curved-surface picking.

## Rendering and multiplayer limits

Offline compilation, geometry, lifetime and ABI checks do not replace live GPU,
input or multiplayer acceptance. Camera images are viewer-local; the server
replicates declarations, not video. Portals are visual ray mappings and do not
transport players or physics. Source mods own scanning and reconstruction.

Consult [performance and troubleshooting]({WIKI}/Performance-and-Troubleshooting)
and [API boundaries]({WIKI}/API-Boundaries) when integrating a feature.
"""
    pages["_Sidebar"] = f"""**[HDR API]({WIKI}/Home)** · **ALPHA**

**Start here**

- [Getting started]({WIKI}/Getting-Started)
- [Development status]({WIKI}/Development-Status)

**Build displays**

- [Drawing and animation]({WIKI}/Drawing)
- [Interactive controls]({WIKI}/Interactive-Controls)
- [HTML/CSS frontend]({WIKI}/HTML-Frontend)
- [JavaScript runtime prototype]({WIKI}/JavaScript-Prototype)
- [Hologram effects]({WIKI}/Special-Effects)
- [Display surfaces]({WIKI}/Display-Surfaces)
- [Cameras and visual portals]({WIKI}/Cameras-and-Portals)
- [Composition recipes]({WIKI}/Composition-Recipes)
- [Combining content, surfaces and input]({WIKI}/Composition)

**Integrate**

- [Programmable blocks]({WIKI}/Programmable-Blocks)
- [Integrating other mods]({WIKI}/Mod-Integration)
- [API boundaries]({WIKI}/API-Boundaries)

**Reference**

- [API reference]({WIKI}/Api-Reference)
- [Performance and troubleshooting]({WIKI}/Performance-and-Troubleshooting)
- [Complete documentation index]({WIKI}/Documentation-Index)

---

[Source]({REPOSITORY}) · [Examples]({REPOSITORY}/blob/main/Examples/README.md)
"""
    pages["_Footer"] = f"""**ALPHA** · Native features are experimental; live testing is required.
[HDR API source]({REPOSITORY}) · [API boundaries]({WIKI}/API-Boundaries) ·
[Development status]({WIKI}/Development-Status)
"""

    # Always include all maintained diagrams so no guide asset depends on main.
    for source in sorted((wiki_source / "diagrams").glob("*.svg")):
        assets["assets/diagrams/" + source.name] = source
    expected_files = {name + ".md" for name in pages} | set(assets)
    if output.exists():
        unexpected_files = sorted(p.relative_to(output).as_posix()
                                  for p in output.rglob("*") if p.is_file()
                                  and p.relative_to(output).as_posix() not in expected_files)
        if unexpected_files:
            errors.append("Unexpected files in wiki output; use a fresh output directory or remove only reviewed stale files: " +
                          ", ".join(unexpected_files))
    if not args.validate_only:
        output.mkdir(parents=True, exist_ok=True)
        for name, text in pages.items():
            (output / (name + ".md")).write_text(text, encoding="utf-8", newline="\n")
        for relative, source in assets.items():
            destination = output / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(source.read_bytes())

    checked_links = 0
    repo_links = 0
    raw_assets = 0
    for name, expected in pages.items():
        file = output / (name + ".md")
        if not file.exists():
            errors.append(f"Missing generated page {file.name}")
            continue
        text = read_text(file)
        if text != expected:
            errors.append(f"Generated page differs from current sources: {file.name}")
        if len(re.findall(r"^\s*```", text, re.M)) % 2:
            errors.append(f"Unbalanced backtick code fence: {file.name}")
        if len(re.findall(r"^\s*~~~", text, re.M)) % 2:
            errors.append(f"Unbalanced tilde code fence: {file.name}")
        if re.search(r"[A-Za-z]:[/\\](?:Users|Temp|Program Files)", text):
            errors.append(f"Private absolute path: {file.name}")
        for match in LINK.finditer(text):
            target = match.group(2).strip().strip("<>")
            checked_links += 1
            if target.startswith(WIKI + "/"):
                page_target = unquote(urlsplit(target).path.split("/wiki/", 1)[1])
                if page_target not in pages:
                    errors.append(f"{file.name}: missing wiki page {target}")
                fragment = unquote(urlsplit(target).fragment)
                if fragment and page_target in pages and fragment not in anchors(pages[page_target]):
                    errors.append(f"{file.name}: missing wiki anchor {target}")
            elif target.startswith(WIKI_RAW):
                raw_assets += 1
                relative = unquote(target[len(WIKI_RAW):])
                if relative not in assets or not (output / relative).exists():
                    errors.append(f"{file.name}: missing wiki image {target}")
            elif target.startswith(REPOSITORY + "/blob/main/"):
                repo_links += 1
                relative = unquote(urlsplit(target).path.split("/blob/main/", 1)[1])
                path = (root / relative).resolve()
                if not path.is_relative_to(root) or not path.exists():
                    errors.append(f"{file.name}: missing repository target {target}")
                elif path.is_file():
                    dependencies[relative] = {"url": target.split("#", 1)[0], "sha256": digest(path)}
            elif not re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", target):
                errors.append(f"{file.name}: unconverted relative link {target}")

    svg_ns = {"s": "http://www.w3.org/2000/svg"}
    for relative, source in assets.items():
        destination = output / relative
        if not destination.exists() or destination.read_bytes() != source.read_bytes():
            errors.append(f"SVG bytes differ from maintained source: {relative}")
            continue
        element = ET.parse(destination).getroot()
        if not element.get("viewBox") or element.find("s:title", svg_ns) is None or element.find("s:desc", svg_ns) is None:
            errors.append(f"SVG lacks title, description or viewBox: {relative}")
        for node in element.iter():
            local_name = node.tag.rsplit("}", 1)[-1]
            if local_name == "script":
                errors.append(f"SVG contains script: {relative}")
            if local_name in {"rect", "text", "tspan", "path", "circle", "ellipse",
                              "line", "polyline", "polygon", "g", "use", "image"} and not node.tag.startswith("{http://www.w3.org/2000/svg}"):
                errors.append(f"SVG drawing node has a foreign/empty namespace: {relative} ({local_name})")
            for name, value in node.attrib.items():
                if name.lower().startswith("on"):
                    errors.append(f"SVG contains event handler: {relative}")
                if name.endswith("href") and re.match(r"^(https?:|file:)", value):
                    errors.append(f"SVG external image/font dependency: {relative}")
    report = {
        "success": not errors,
        "page_count": len(pages),
        "svg_count": len(assets),
        "checked_links": checked_links,
        "repository_link_count": repo_links,
        "wiki_image_link_count": raw_assets,
        "release": args.release,
        "scene_protocol": args.scene_protocol,
        "client_renderer": args.client_renderer,
        "asset_url_scheme": WIKI_RAW + "assets/diagrams/<name>.svg",
        "network_performed": False,
        "existing_remote_pages_modified": False,
        "source_upload_dependency": "All repository blob/main links require the maintained tree on GitHub main. Local existence and hashes are verified; remote availability is not asserted.",
        "repository_dependencies": dict(sorted(dependencies.items())),
        "source_pages": {p.relative_to(root).as_posix(): digest(p) for p in sources},
        "generated_files": {relative: digest(output / relative)
                            for relative in sorted(expected_files) if (output / relative).is_file()},
        "errors": errors,
    }
    report_file = output.parent / "validation.json"
    report_file.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k not in
                      ("repository_dependencies", "source_pages", "generated_files")}, indent=2))
    return 0 if report["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
