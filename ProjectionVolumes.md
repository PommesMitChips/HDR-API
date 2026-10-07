# Projection envelopes

Current API and usage: [HDR API wiki](docs/wiki/Display-Surfaces.md). This page retains lower-level and historical design notes; the wiki states current capabilities and limits.


Projection envelopes are evaluated by viewers. Scene points stay unchanged; lines and filled triangles are clipped as they are submitted to the renderer. Clipped texture coordinates are interpolated so images remain aligned. Billboard labels use the same triangle clipping path. LCDs use their own 2D screen bounds instead.

Ordinary projectors have a configurable range. Zero disables the additional range envelope; the existing scene size and draw-distance safety limits still apply. The radius is measured from the map view offset in the projector's local axes. The envelope is an inscribed 20-face icosahedron, so it stays entirely inside the requested spherical range. Its boundary is an approximation rather than a perfect sphere.

Console projector blocks default to an inverted truncated pyramid. Its bottom is 0.1 metres above the block origin, its height is 2 metres, and its lower half widths use 45 percent of the local block bounding-box width and depth. The upper half widths are 1.8 times the lower half widths. Configuration can replace all six dimensions: bottom height, total height, lower X/Z half widths, and upper X/Z half widths. The envelope follows the console orientation, independent of the camera or the map's rotation.

Native miniature grid and block models have no exposed clipping shader in the supported render API. Their world bounding boxes are checked conservatively: a model or grid is hidden when any part of its bounding box lies outside the envelope. This guarantees containment but does not slice a model at the boundary. Armor-grid previews may disappear as a group while independent block models inside the envelope remain visible. Wire and polygon geometry is sliced at the boundary.

Display clipping has a shared per-frame vertex/plane work budget in addition to the draw budget. Exhausted work is skipped for that frame. Disabling the envelope does not disable ownership checks, geometry budgets, or the maximum 25-metre scene radius.
