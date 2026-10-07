HoloMap vector lettering

VectorFontData.cs contains offline triangulated outlines from Inter, Copyright
The Inter Project Authors. Its SIL Open Font License is in Inter-LICENSE.txt.
The generated outlines and license are included; the source font is external.
To regenerate on Windows, supply an Inter TTF and the accompanying SIL Open Font
License file that you obtained separately. Run from the HDR_API repository root:

  powershell -NoProfile -File .\Tools\BuildVectorFont.ps1 -FontPath 'C:\Fonts\Inter.ttf' -FontLicensePath 'C:\Fonts\OFL.txt'

Use the original Inter family with its license; the baker rejects other families.
The tool runs offline, writes Mod/Data/Scripts/HoloMap/VectorFontData.cs and copies
the supplied license to Mod/Data/Fonts/Inter-LICENSE.txt. It does not download a
font or depend on a sibling repository. Normal builds use the packaged outlines
and do not need the source TTF or this regeneration step.
The mod never loads fonts from the operating system, a network URL or player data.

Supported packaged repertoire: printable ASCII, Latin-1 and Latin Extended-A,
Greek, Cyrillic, selected typographic punctuation, currency, math and arrows.
Other characters use '?'. One supplementary Unicode code point produces one
fallback glyph. This is not a universal Unicode font or a complex-text shaper:
Arabic shaping, right-to-left layout, emoji and CJK are not implemented.

Fixed-plane/SVG text uses proportional vector meshes and existing mesh budgets;
split very long or complex captions into separate objects when needed. Newlines,
CRLF, tabs and per-line start/middle/end anchors are supported, with 1.3-height
line spacing. Hologram labels are camera-facing vector meshes. LCD labels use
the game's native Debug font; LCD fixed text and SVG use vector geometry.

The old GlyphFont.Layout bitmap helper is retained for compatibility only.
