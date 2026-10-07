# Client rendering security boundary

HDR API is a visual display API. Its client-generated meshes, animation poses and previews do not authorize game actions. Camera scans, PB execution and other game state remain controlled by Space Engineers and the server.

## Network trust

Clients accept display publications only through the game's secure messaging handler with the server-origin flag. Server-origin messages may use sender zero on dedicated servers. Client messages can request a baseline; they cannot upload scene mutations through this channel. Requests are limited by active player identity, per-sender and global pacing, and queued transfer backpressure.

The protocol sends bounded declarations and indexed geometry. It sends no delegates, executable scripts, JavaScript, arbitrary runtime types, file paths or remote asset URLs. Packaged image names are restricted to the registered mod material namespace. The compressed animation format contains ASCII SVG and numeric metadata, with validated output length and back-references; it is not executable bytecode.

Publications carry a protocol version and revision. Incremental updates require the exact baseline revision and use explicit removal records. Clients validate a complete prospective scene before replacing the last valid scene. Missing baselines request recovery; malformed or incomplete updates do not grant authority or partly replace a display.

## Resource controls

The server bounds retained definitions and replication size before admitting new display content. Each client independently bounds received content, decoded animation size, parsed SVG structure, geometry output, retained caches and rendering. Geometry compilation has a shared operation allowance across nested parser, triangulation, clipping and gradient operations, including operations that produce no visible output. Compilation is scheduled rather than allowing every object to compile each frame.

An authorized PB can configure an anchor's shared presentation budget within 65,536 points / 131,072 primitives / 200,000 draw-work units. Settings are validated before commit and on receipt; omitted budget metadata uses conservative defaults, while a present incomplete budget is rejected. Per-object, source-data and replication limits remain independent. Every viewer retains its own global draw-work cap, initially 20,000 per frame, which only its local `/hdr work N` command changes. External-source images yield space to visible static content. Rendering quotas do not change a data provider's acquisition policy.

External display sources run in installed mods on the local client. HDR bounds and clones their triangle meshes, validates plane coordinates, indices, colors and refresh policy, and calls the provider's validity callback before every use. Missing providers, replacement generations, false proofs and callback errors suppress source geometry while ordinary overlays remain available. Opaque evidence stays in client memory and is never serialized or exposed through the PB display API. Local mod delegates are a cooperation interface, not a sandbox against malicious installed mods.

HDR contains no acquisition, camera permissions or retained-observation service. A provider such as CamScan owns and rechecks its own source authorization. See [DisplaySources.md](DisplaySources.md) for the generic interface and the separate CamScan project's security documentation for scanning.

The optional HDR client renderer is explicitly installed trusted `IPlugin` code, separate from the Workshop mod. It accepts local bounded RGBA buffers or copies an authorized LCD texture; it executes no server-provided code, URLs, file paths, shaders or renderer commands. The core uses a fixed material pool, avoiding runtime material-registry mutation. Raster buffers/material handles/evidence stay local and never enter scene publications or PB results. Power/access/source loss, world leave and observed renderer reset retire resource leases. Identical-settings device recreation still needs the host's reset signal and an in-game test.

Requested raster sizes are bounded metadata, not allocation sizes. Each viewer picks effective sizes from its viewport, projection footprint and requested cap; actual uploaded/copied buffers stay within 1,024 per side and 262,144 pixels. Frustum-culling suppresses display work and publishes no-demand hints to local providers. It does not enforce a trusted third-party camera plugin's capture policy or prevent a modified client from observing already replicated world data. Use provider-owned permission checks for any private source.

Initial packed-animation validation still decodes bounded data on the server. New decode attempts, including failures, are limited globally to two per simulation tick and eight per 60-tick window. Admission and commit reuse their decoded result; already retained animation tables need no new decode. A PB initializing many different packed payloads may receive a temporary busy error and should retry on a later update. The multi-display demo already polls and retries.

Display compilation failures retain the last usable mesh where possible. Invalid compressed sources are cached as failures. Viewers can use `/hdr off` to stop their own display rendering and preparation, and `/hdr on` to resume. These controls do not change other viewers' scenes.

These limits reduce content-driven denial of service; they are not a mathematical CPU-time guarantee. Limits may reject unusually complex but legitimate artwork. A listen host also runs a local renderer, so its machine still pays that client's rendering cost.

## PB and privacy limits

The API verifies that supplied PB and projector references are live registered entities, on the same construct, and that the supplied PB owner has access to the projector. Authorization is rechecked during cleanup and animation maintenance.

The public terminal-property API does **not** identify the currently executing PB. Legacy calls supply a PB reference; the short command endpoint captures the PB requested through the property getter. Neither mechanism is proof against a host-executed PB deliberately requesting another PB's endpoint. This follows the available mod API boundary; do not use display commands as authorization for weapons, inventory changes, private data access or other trusted gameplay operations. World operators retain control over whether player PB scripts are enabled.

Scenes are public to connected viewers. Distance culling and hidden layers reduce rendering work, not disclosure: a client can inspect the data it receives. Do not publish telemetry that a viewer must not know.

## Verification

The offline harness checks installed-game compilation, PB memory-safe rewriting, parser/work limits, replication validation and client/server behavior through gateway doubles. It does not certify actual game sandbox loading, GPU output, real transport behavior or public-server security. Follow [MultiplayerValidation.md](MultiplayerValidation.md) on a listen host and dedicated server before publishing this build for multiplayer use.
## UI action boundary

HDR.UI actions are fixed declarations. Remote requests carry only caller/display/widget IDs, revision and sequence; the server supplies the registered PB argument or layer/menu action. It rechecks the transport sender, live controlled character, on-foot state, PB/display access, same construct, physical LCD tile, distance, head ray and line of sight. Requests, peers and grants are bounded and rate limited. Viewer-local menus are presentation state, not private data.

Mouse focus requires a provider-owned acknowledgement that gameplay input blocking has actually been applied. Stock Rich HUD Framework API 13 has no such acknowledgement, and a timed delay or an idle mouse is not proof. The optional adapter fails closed when this contract is missing. No private game methods, reflection, spectator permission bypass or executable client UI payloads are used.
