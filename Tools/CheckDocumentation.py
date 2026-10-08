"""Validate the maintained wiki, entry points and SVG source without network access."""
from pathlib import Path
import re
import sys
from urllib.parse import unquote
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PAGES = [ROOT / "README.md", ROOT / "Api/README.md", ROOT / "Examples/README.md",
         ROOT / "docs/README.md", ROOT / "docs/Contributing.md"]
PAGES += sorted((ROOT / "docs/wiki").glob("*.md"))
PAGES += [ROOT / "OptionalMods/HDRHtmlFrontend/README.md"]
PAGES += sorted((ROOT / "OptionalMods/HDRHtmlFrontend/Docs").glob("*.md"))
errors = []
links = 0


def anchors(path):
    found, counts = set(), {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if not re.match(r"^#{1,6}\s", line):
            continue
        value = re.sub(r"^#{1,6}\s+", "", line).strip().lower()
        value = re.sub(r"[^\w\- ]", "", value).replace(" ", "-")
        number = counts.get(value, 0)
        counts[value] = number + 1
        found.add(value if number == 0 else f"{value}-{number}")
    return found


for page in PAGES:
    source = page.read_text(encoding="utf-8-sig")
    if any(ord(char) < 32 and char not in "\t\r\n" for char in source):
        errors.append(f"{page.relative_to(ROOT)}: control character")
    if len(re.findall(r"^\s*```", source, re.M)) % 2:
        errors.append(f"{page.relative_to(ROOT)}: unbalanced code fence")
    for match in re.finditer(r"!?\[[^\]]*\]\(([^\n]+?)\)", source):
        target = match.group(1).strip().strip("<>")
        if re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", target):
            continue
        # Markdown links to code paths may contain ')' in a filename; currently
        # maintained links are ordinary repository files and fragments.
        path_part, _, fragment = target.partition("#")
        resolved = (page.parent / unquote(path_part)).resolve() if path_part else page
        links += 1
        if not resolved.exists():
            errors.append(f"{page.relative_to(ROOT)}: missing {target}")
        elif fragment and resolved.suffix == ".md" and unquote(fragment) not in anchors(resolved):
            errors.append(f"{page.relative_to(ROOT)}: missing anchor {target}")

svgs = sorted((ROOT / "docs/wiki/diagrams").glob("*.svg"))
ns = {"s": "http://www.w3.org/2000/svg"}
for svg in svgs:
    tree = ET.parse(svg)
    element = tree.getroot()
    if not element.get("viewBox") or element.find("s:title", ns) is None or element.find("s:desc", ns) is None:
        errors.append(f"{svg.relative_to(ROOT)}: missing accessible title/description/viewBox")
    for node in element.iter():
        if isinstance(node.tag, str) and not node.tag.startswith("{"):
            errors.append(f"{svg.relative_to(ROOT)}: element outside SVG namespace: {node.tag}")
        for name, value in node.attrib.items():
            if name.endswith("href") and value.startswith(("http:", "https:")):
                errors.append(f"{svg.relative_to(ROOT)}: external image/font dependency")

if errors:
    print("\n".join(errors), file=sys.stderr)
    sys.exit(1)
print(f"PASS: {len(PAGES)} maintained Markdown pages, {links} local links/anchors, "
      f"{len(svgs)} accessible standalone SVG diagrams.")
