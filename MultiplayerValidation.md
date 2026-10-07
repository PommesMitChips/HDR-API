# Multiplayer acceptance checks

These are runtime checks for the client-rendering build. The offline harness cannot verify game rendering, game sandbox loading, or real network delivery.

Use the same HDR API build on every peer. Test a listen host plus a remote viewer, and repeat with a dedicated server plus two viewers. Keep a copy of the save before testing.

1. Run `artifacts/DrawDemo.pb.cs`. The circle should pulse continuously on both viewers without PB Update1. Pause, resume and stop it; both viewers should converge to the same state. Check that a client joining halfway through a pulse starts at the current phase.
2. Run `artifacts/MultiConsoleAnimationDraw.pb.cs` with two same-name Console/Projector blocks and different animation payloads. Both should animate independently. Pause/resume, replace a payload, clear a display, and rename/remove an anchor. Check that stale frames do not reappear.
3. Run the appearance demo. Check gradients, clipping, holes, labels and textured quads on host and remote viewer. Rotate and zoom while it runs. Client render meshes should never change another player's canonical display definition.
4. Run a PB that updates drawn triangles and layers. Incremental updates should preserve old triangles and layers; deletion/clear should remove them. Join while a large display is transferring, and check that the display appears atomically rather than as a partial scene.
5. Change PB ownership or remove its access to the display. Existing animation should stop being authorized and its display content should be removed. Attempt commands from another construct and an unowned PB; they should fail.
6. Enter `/hdr off` on one viewer. Its holograms and previews should disappear, and its expensive display preparation should stop. Other viewers should be unaffected. Enter `/hdr on` to restore the current display; the command should not appear in multiplayer chat.
7. Observe server and client logs during malformed SVG replacement, excessive geometry, and invalid compressed payloads. A rejected display must not crash the session, monopolize repeated compilation, or repeatedly flood the log.
8. With simulated latency/loss or interrupted delivery, check late joining and recovery from missing revisions. An invalid or incomplete update should retain the last valid display until a valid baseline arrives. Repeat rapidly changing view/opacity while geometry is unchanged.

Record the game version, mod build, host type, peer count, latency and any errors. Passing the offline checks alone does not constitute multiplayer certification.

## Render budgets (0.5.1)

With multiple external-source screens on one anchor, set `H("budget",4096,8192)`, then `H("budget",8192,16384)`. Both viewers should keep the text overlays and share external-source presentation fairly, show unknown holes, report reduced detail when needed, and avoid repeated budget-error messages. Hide/show a large layer; hidden cached geometry must not prevent a new visible image or label from being prepared. A late join must receive the current budget. Reject out-of-range settings and attempts to lower below declared raw geometry without changing the prior scene. Enter `/hdr work 1000` on only one viewer; its drawing should stop at its local work allowance while the other viewer remains unaffected. Raising a PB's draw allowance must not raise that local viewer cap.

## LCD and UI acceptance

Select Content -> HDR API on each LCD before running LcdDemo. Verify native orientation and a 2x2 wall, tile removal/replacement, layer order, transparency and the edge of radial fills. Selecting another Content mode must immediately stop HDR from drawing there.

Run UiDemo on a panel named HDR UI. Press Use (F) on every button; it should invoke the fixed PB command once, toggle the shared layer or open the viewer-local menu. A second viewer must not inherit that menu. Check Console and Projector picking too. Native LCD editing must still work away from buttons and after clearing the UI. Test Escape, death/respawn, cockpit entry, power/access loss, block removal, stale revisions and mod unload. Verify no custom detector remains saved or interferes with another mod.

Treat mouse focus as unavailable unless the installed backend exposes an acknowledged capture contract. On a future compatible backend, test remote latency, firing and block placement, held buttons on entry/exit, Alt look, connected controllers, GUI/chat transitions and another RHF UI owning the cursor before enabling gameplay controls through HDR.

## Native/vector LCD acceptance (0.3.0)

Use LcdDemo's demo, vector/native and rate 6/15/30/60 arguments. Verify square, wide and transparent large/small panels at all four screen rotations. Switching to vector must blank the selected native component; rotate away/back and check old native artwork clears. A short overlap until the next native texture refresh is expected. Unknown/modded model paths must continue using native frames. Confirm /hdr status reports fallback and /hdr lcd-rate affects only that viewer.

Compare radar motion at 6, 15, 30 and 60 Hz with two viewers and a dedicated server. Move/rotate a ship while sampling at 6 Hz: the pinned plane and map view should follow each render while animation metadata steps at the selected rate. Test 24 and 59 Hz phase scheduling and a simulation/FPS slowdown without catch-up bursts. Repeated identical settings from a PB must not resample faster than the viewer cap.

On a 2x2 wall verify exact crop edges, texture UVs, masks/holes, transparent layers, vector text, live-grid lines and bezels. Verify F buttons share the displayed screen coordinates at every rotation; transparent-panel rear views must neither render vectors nor activate their controls. Test power/content change, panel removal, group/master loss, /hdr off/on and save reload. GPU appearance, OIT layer ordering and depth offsets are not certified by offline assertions.

## Raster and curved display acceptance (0.7.0)

Use the optional client renderer on one viewing client and keep the dedicated server plugin-free. Without it, a raster UI should show vector fallback and `/hdr status` should report the unavailable backend. Verify static fixed material definitions load, then compare translucent overlapping fills/text with native vectors. Check pixel color, premultiplied alpha, UV direction and image orientation on planes and on inside/outside cylinder/sphere patches. Packaged texture layers and per-object emission still need vector mode.

Use at least three 60 Hz raster UI screens and five or more 60 Hz texture sources; every eligible screen must receive service. Mix 1 Hz and 60 Hz UI screens and check the slow deadline is not shortened. Lower the anchor budget after curved meshes are prepared and verify they are rejected/rebuilt within the new allowance. A repeatedly failing over-fine curved map must not stop a sibling from compiling.

At 1080p and 4K viewer resolutions move close/far, rotate the view away/back, and cross frustum/near-plane edges. Check reduced copy/upload dimensions, stable LOD tiers, no offscreen HDR preparation, and conservative curved culling when standing just inside the shell. Test portrait raster settings on a square pinhole canvas, cover/contain clipping, and moving/rotating anchors. A camera producer that honors `source-demand` should reduce/stop its own capture; an independent CameraLCD installation does not gain that integration automatically.

Relay a source LCD that visibly contains a real camera image. Switch sources, resize two screens using the same LCD, remove one while keeping the other, revoke PB/source access, power off, leave/reload the world, and recreate graphics devices. Verify no stale feed or destroyed peer resource is shown. Run the six-face spherical demo with matched 90-degree square source images. RGB-only joins retain depth-dependent parallax between different camera origins; this prototype does not reconstruct depth.
