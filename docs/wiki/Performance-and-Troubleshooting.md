# Performance, budgets and troubleshooting

Requested quality, admitted scene size, client resource ceilings and actual frame rate are different quantities. A PB can configure its display allowance and requested rate; it cannot raise another player's local scheduling preferences or remove descriptor/resource safety checks.

## Scene budgets

Select the anchor rather than a virtual screen before configuring:

```csharp
H("screen-target", "");
H("budget", 32768, 65536, 60000);
var budget = (VRage.MyTuple<int,int,int>)H("budget-settings");
```

| Quantity | Default | Configurable range / retained bound |
|---|---|---|
| Anchor geometry points | 4096 | 256–65536 |
| Anchor primitives | 8192 | 256–131072 |
| Anchor draw-work units | 20000 | 1000–200000 |
| Viewer total draw-work | 20000 per frame | `/hdr work 1000..200000` |
| Objects per anchor, including screen slots | 16 | Fixed admission bound |
| Active display anchors | 8 | Fixed admission bound |
| Screens | 8 per anchor, 16 globally | Fixed admission bound |
| Points/primitives per ordinary object | 2048 points / 4096 primitives | Fixed per-object bound |
| Ordinary display distance | 60 m | Retained renderer range |
| Transformed scene/surface extent | 25 m about anchor | Retained validation bound |

Lowering an anchor budget below already declared raw geometry is rejected. Raising it does not raise every other bound. Source preparation shares the remaining allowance fairly after visible static content. Hidden/orphaned caches do not reserve visible-source geometry. Invalid preparation leaves previous usable geometry where authorized; revoked source proofs cannot be bypassed by stale caching.

Six screens plus their overlay objects consume the same 16-slot admission bound. Combine related artwork into one SVG object where useful, remove unused declarations, and use a single direct panorama instead of six separate screens when the presentation permits it. “Console object limit reached” is not a GPU out-of-memory message; it is bounded retained scene admission.

## Client-native scheduling

Use local chat; these preferences are stored by the plugin and never set by a remote PB/server:

| Command | Meaning |
|---|---|
| `/hdr camera status` | Current preferences and capture diagnostics |
| `/hdr camera pixels N` | Base-pixel work cap per native main-frame accounting period |
| `/hdr camera pixels viewport S` | Cap scaled by current viewport pixels |
| `/hdr camera pixels off` | Remove configurable pixel scheduling ceiling |
| `/hdr camera passes N` | Native capture-pass ceiling |
| `/hdr camera passes off` | Remove configurable pass ceiling within bounded source/tile resources |
| `/hdr camera rate N` | Viewer capture-rate ceiling |
| `/hdr camera rate off` | Follow source requested cadence |
| `/hdr camera adaptive on/off` | Density-tiled vs uniform forward capture |
| `/hdr camera tiles 1..64` | Maximum density-plan leaves |
| `/hdr camera tile-cost N` | Estimated extra-pass cost in pixel equivalents; ≥0 |
| `/hdr camera occlusion on/off` | Conservative display-occlusion suppression |

Current defaults are pixels off, passes 6, requested rate, adaptive on, occlusion off, 16 leaves and 16384 pixel-equivalent extra-pass cost. `off` removes that scheduling preference; validation, source counts, texture/memory bounds and device/world lifetime still apply. A large user-set cap does not improve imagery already limited by the source request, physical source texture, native maximum or current density plan.

The shared ledger includes native capture rasters, panorama output, LCD destination copies and UI uploads; portal accounting includes its capture/composition work as appropriate to the path. It counts base pixels, not every shaded fragment or actual GPU milliseconds. Mips, overlap, postprocessing, world submission, transparent passes and publication copies have extra cost. A 4K-sized base-pixel budget does not make six extra world renders free.

Density planning uses conservative source-UV coverage/magnification. More distortion does not inherently imply more edge samples: the appropriate criterion is the mapping derivative from source UV into currently visible display pixels. Hidden consumers avoid work, and conservative unknown evidence may retain work rather than incorrectly erase an image.

## Occlusion and geometry visibility

Ordinary camera depth hides surfaces behind foreground objects **inside camera images**. Display occlusion instead asks whether the projected screen itself is hidden behind opaque world geometry; it can stop acquisition entirely. Native conservative GPU queries are optional and off by default.

Queries require matching primary depth/publication identity and complete conservative static consumer enclosures. Pending/unknown results retain demand. Moving anchors, unsupported/unknown consumers, near-plane crossings and geometry outside the certified query domain fail open. A supported occlusion query does not promise all hidden portals/screens will stop work immediately.

Camera capture intentionally isolates player-view cached model visibility/LOD and occlusion decisions. The source-camera frustum and its own depth still apply. A block that disappears as the player approaches a projection may indicate stale native visibility state, not a scan result or display crop. Record plugin version, `/hdr camera status`, adaptive on/off outcome and viewpoint reproduction; do not infer that increased pixels fixes culling state.

## Refresh and movement

Physical native LCDs keep their engine consumption schedule even with high requested sampling. Vectors submit static geometry each render frame. Raster UI prepares bounded ready screens; providers schedule their own captures. Native camera/portal rates also depend on primary render cadence and available passes.

Portal 0.9.12 moves capture before main drawing and pairs capture pixels with their own projection coefficients. It does not manufacture intermediate motion frames. A request of 30 Hz on a 60/120 Hz primary view intentionally holds older eye-dependent images. Use `rate 120` in the portal demo to request a higher rate, then read `/hdr portal status` to check actual requests/age. Live motion tests remain necessary; offline matrix/lifecycle tests cannot certify absence of presentation jitter.

## Diagnosis by symptom

| Symptom | Check |
|---|---|
| HDR property is absent | World mod enabled, correct package folder, save reloaded after update |
| Wrong target or table-volume error | Use `findtarget` for Console/Projector; LCDs sharing the name can otherwise win nearest selection; `volume` is Console-family only |
| No floating sphere | Powered authorized anchor, declaration exists, visible side/opacity, actual centre height, range/table envelope, `/hdr status` errors |
| PB prints setup but plugin image is empty | Setup output only proves declarations; read any `Requires plugin: HDR Client Renderer (...)` notice, then check local camera/portal readiness and source power/access |
| Raster looks like vectors | Check selected renderer and `/hdr status`; a connected backend can report an explicit composition fallback. Plugin absence in explicit raster mode now leaves it empty with a requirement message |
| Physical LCD video works, projected video black | Confirm `lcd-texture` provider identity/surface and loaded plugin; a direct panorama source needs actual camera IDs, not LCD names |
| Camera image blurry despite high local budget | Check PB capture/output ceiling, relay source size, effective density/bucket and provider bound |
| Portal status still says requested 512/30 | Running PB still declares 512×512/30 Hz; replace the full maintained demo or run supported size/rate commands |
| Transparent or grey camera gaps | Coverage cones do not fill those directions; grey is background choice; larger mapping FOV does not invent missing source imagery |
| Portal strict mode partly empty | Rays cannot share the current native perspective capture; approximate is an explicit quality tradeoff |
| Projection survives PB script replacement | Current lifecycle retires changed `ProgramData` every 30 ticks and on next write; verify the loaded mod version and exact PB owning the declaration |
| Plugin missing from Pulsar list | Correct runtime/package, DLL+XML pair in actual active data folder, then restart; a GitHub cache copy alone is not an install |

Run `/hdr off` to release local display caches/previews and `/hdr on` to permit rebuilding. It changes no shared scene or other player's rendering. `/hdr lcd-rate 1..60` caps ordinary LCD/projected metadata sampling locally; direct camera/native portal sources also use their own requested/native scheduling path.

## Reproducible bug reports

Include mod/plugin versions actually **loaded**, runtime (Interim/Legacy), PB source/example name and its current status, client commands/status, requested/effective sizes and capture age, whether the problem occurs with a simple static vector display, and a short movement reproduction/video. Logs report assembly path/version/MVID so a DLL copied on disk can be distinguished from an old DLL still loaded in a running process.

GPU-driver crashes and native render freezes require live logs and controlled reproduction. Do not claim a passed CPU/HLSL/ABI test proves driver safety. Before updating plugins close the loader/game; use the backed-up installer and verify profile hashes/physical paths. Multiplayer acceptance must include host, dedicated server, joining, source/provider loss, permission revocation and world/device lifecycle; see [MultiplayerValidation.md](../../MultiplayerValidation.md).
