# General display surfaces

`screen-ellipsoid(radii, horizontalRadians = 2π, verticalRadians = π, side = "inside")` takes `Vector3D` radii, or three scalar radii. Each radius is at least 0.05 meters, and the entire shape must fit within 25 meters of its anchor. Equal X/Z radii give a spheroid; three independent radii give an ellipsoid. Angular and pinhole mappings preserve the original viewing ray by intersecting it with the ellipsoid. Layers move along the implicit surface normal, rather than along a scaled sphere radius. Geodesic mapping is rejected for ellipsoids; exact ellipsoid geodesics are not implemented.

`screen-mesh(points, triangleIndices, uv, side = "outside")` takes `Vector3D[]`, flattened indexed `int[]` triples, and one `Vector2` UV per point. Limits are 2048 points and 4096 triangles, inside the shared anchor budgets. Positions and UVs must be finite; UVs must be in 0..1; both physical and UV triangles must be nondegenerate. The authored triangle winding defines the outside. To create sharp normal seams or separate UV islands, duplicate vertices as needed. Disconnected and overlapping UV islands are supported intentionally: content maps to every authored triangle using that UV region.

Generic meshes use authored UVs instead of angular/pinhole/geodesic projection. Vector fills and lines are clipped to each UV triangle and mapped barycentrically; raster, image and camera sources retain their texture coordinates on the same surface. The client compilation budget also bounds unsuccessful clipping attempts. A large mesh or complicated vector overlay can exceed its output/work budget and should be split into smaller screens or use raster content.

`screen-two-sided(enabled, frontOpacity = 1, backOpacity = 1)` applies each triangle's primary/secondary opacity on the new shapes. Foreground layers move toward the viewer on either side. Footprints, crop bounds, density calculations and geometry caches include the authored positions/UVs or all three radii. A camera panorama on an ellipsoid remains ray-correct around the display origin; arbitrary meshes require deliberately authored UVs and do not automatically preserve a spherical viewing model.

`UiSurfaceHit.TryHit` provides bounded local-coordinate mesh/ellipsoid ray-to-UV evidence. Geodesic sphere hit mapping fails closed until its matching inverse is implemented. Existing interactive UI widgets currently attach to anchor/LCD planes, not projected screens; this helper does not add projected-screen widget controls. No GPU rendering is needed on the server: declarations are validated and replicated, while mapping and rendering run locally.

`screen-camera-quality("normal" | "lite")` declares an opt-in native capture quality profile. `screen-camera-quality-settings` returns the selected name. Screens sharing a camera source for the same PB must use matching capture settings, including the profile.

Paste `artifacts/GeneralSurfaceDemo.pb.cs` into a PB and run `ellipsoid` or `mesh` to compare both shapes. In-game visual and GPU validation remain necessary.

