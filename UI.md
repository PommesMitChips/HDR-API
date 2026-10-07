# HDR.UI

Current API and usage: [HDR API wiki](docs/wiki/Drawing.md). This page retains lower-level and historical design notes; the wiki states current capabilities and limits.


`HDR.UI` exposes a small `Func<string,object[],object>` PB endpoint alongside `HDR.Draw`. It defines buttons, bundles, custom hit regions and fixed actions. Clients render and pick controls; the server authorizes every action. No executable UI code or client-supplied PB argument is accepted.

## Try it

Enable the local **HDR API** mod. Name an LCD **HDR UI**, select **Content → HDR API**, paste `artifacts/UiDemo.pb.cs` and run `demo`. On foot in first-person view, aim at a button and press the game's **Use** control (F by default). The demo runs its own PB with `ui:status`, toggles a map layer, and opens a viewer-local context menu. Escape closes the menu. This is native Use interaction, not unsafe polling that also triggers the normal LCD editor.

The same controls can target a Console or Projector. Hologram picking installs a small temporary native detector at the currently authorized hover point. It does not spawn a networked entity per button or record custom detectors in the save. Native selection and physics still need in-game verification on a host and dedicated server.

## PB hookup

```csharp
Func<string,object[],object> _ui;
object U(string command,params object[] args) {
    if(_ui==null)
        _ui=Me.GetProperty("HDR.UI")
              .As<Func<string,object[],object>>().GetValue(Me);
    return _ui(command,args);
}
```

```csharp
U("target", "HDR UI");
U("bundle", "main", true);
U("bundle", "context", false);
U("button", "start", "main", 0, 0.3, 1.2, 0.2,
  "Start", "pb", "start-machine");
U("button", "more", "main", 0, 0, 1.2, 0.2,
  "More", "menu", "context");
```

The box coordinates are center X/Y, width and height in the normal drawing coordinate system. Z is zero; the scene view applies. LCD walls map each part to the corresponding physical tile. Detector-based physical screen calibration can differ from texture bounds on modded panels; test alignment before assigning consequential controls.

| Command | Arguments |
| --- | --- |
| `target` | display name or block reference; shared with the drawing endpoint |
| `bundle` / `menu` | bundle ID, initial visibility |
| `button` | widget ID, bundle ID, X, Y, width, height, caption, action kind, argument |
| `hit` | widget ID, bundle ID, X, Y, width, height, action kind, argument; uses existing artwork |
| `bind` / `action` | widget ID, action kind, argument |
| `visible` | widget or bundle ID, boolean |
| `remove` | widget or bundle ID |
| `clear` | remove this caller's UI on the target |
| `capabilities` | no arguments; reports available interaction modes |

IDs use 1–24 lowercase letters, digits, hyphens or underscores. Bundle and widget IDs must differ. There are at most 64 hit regions per UI display, 128 globally, 8 bundles per UI display and 16 UI declarations globally. Captioned buttons also share the normal 16-label / 256-character display budget. Dense controls can exceed geometry limits. Rendering updates are transactional; failures retain the previous definitions and artwork.

## Actions

- `pb`: run the registering PB with its fixed argument, up to 256 characters. Handle it in `Main` without rebuilding the whole UI unless needed.
- `toggle`: toggle the named shared drawing layer.
- `menu`: reveal the named bundle to the interacting viewer. The grant expires after inactivity, and is tied to the player character and declaration revision.
- `focus`: currently opens the bundle for **look-and-use navigation**, like a menu. It does not capture the mouse or move the camera.

Bundle layers are named `ui-<bundle>`. Use `H("layer-order", "ui-context", 20)` to place a menu above lower drawing layers. Hide menus initially with `U("bundle", "context", false)` rather than repeatedly changing shared visibility in the PB.

## Mouse focus

Mouse focus is not enabled by stock Rich HUD Framework API 13 or the current experimental HDR Input Backend fork. The optional RHF adapter and viewport renderer are present, but acquisition fails closed without an acknowledged applied-input contract. `focus` falls back to look-and-use navigation; it does not move the player camera.

The installed game exposes its immediate blacklist setter only as a private method. Its public setter is networked. Inspection also found that a later reliable mod-message acknowledgement cannot prove the earlier blacklist RPC was delivered when the transport reports a send failure. A timer, idle mouse or test click would not establish safe capture. No private-method or spectator workaround is used.

`OptionalMods/HDRInputBackend` is a separately named, MIT-licensed development fork with bounded and authenticated request handling. It is not required for native F buttons and should not be enabled alongside the original Rich HUD Framework provider. Its current module 6 intentionally reports capture unavailable. It remains an experimental artifact, not an installed or multiplayer-certified dependency. A future provider must confirm actual game-input application before enabling mouse interaction.

## Multiplayer boundary

The client sends only registered IDs, a revision and a sequence number. The server validates the active player and registered character, on-foot control, PB/display permissions, construct relationship, actual LCD tile, range, head-ray hit and physical occlusion. Requests are bounded and rate limited. Canonical arguments come from server-held definitions; the client cannot substitute arbitrary commands. Removing or changing definitions invalidates old requests and menu grants.

Viewer-local visibility is not confidentiality: definitions and arguments are replicated to clients. Do not store secrets in UI actions. See `Security.md` and `MultiplayerValidation.md` for remaining runtime and PB identity limits.
