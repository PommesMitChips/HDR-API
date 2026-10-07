# HDR Input Backend

Optional, modified Rich HUD Framework provider for HDR.UI. It replaces the original provider in a save: **do not enable both providers**. HDR API remains usable without either provider; native look-and-use controls remain available.

Source: installed Steam Workshop package [Rich HUD Framework, 1965654081](https://steamcommunity.com/sharedfiles/filedetails/?id=1965654081), API 13 / upstream client version 1.0.3.0, copied on 2026-10-02. The original Workshop directory was not modified. Rich HUD Framework source copyright Zach Hembree, MIT; original notices remain in place and are reproduced in LICENSE. Font licenses and texture assets remain as supplied by upstream. This is a separately named local fork, not an upstream release.

Changes: authenticated network direction, 8 KiB packet limits, bounded queues/batches/ranges, exact action IDs and sender-bound blacklist requests. Existing API 13 modules remain compatible. Module 6 reserves an HDR acknowledgment extension but deliberately advertises version zero and never grants capture.

Module 6 returns `MyTuple<int, Func<int,bool>, Func<int>, Action, Func<bool>>`: version zero, request always false, applied mask always zero, no-op release, ready always false. HDR.UI therefore falls back to native look-and-use menus. No mouse-focus capability is claimed by this optional fork.

Installed-game IL shows `MyVisualScriptLogicProvider.SetPlayerInputBlacklistStateSync` would apply local blocks immediately, but the method is private and cannot be called by a normal mod. Compilation confirms it is inaccessible. This fork does not use reflection, private APIs or client plugins to reach it. The public setter invokes an asynchronous network event, and idle input getters cannot distinguish an applied blacklist from ordinary idle input. A server reply alone would not prove local input application. Legacy network actions validate transport sender and can only affect that sender's identity.

The inherited RHF broker recomputes the union of registered RHF consumers, and client unload now removes clients by identity rather than stale indices. It does not own or promise compatibility with independent mods writing directly to the game's blacklist. The original RHF input API remains asynchronous and is not an HDR security boundary. This extension does not capture joystick input.

Runtime acceptance requires a dedicated server and remote client smoke test, held-button release, respawn, disconnect, GUI transitions, permission loss and another cooperating RHF consumer. Offline compilation and fixtures do not prove engine input ordering. Do not call this a security-reviewed upstream RHF release.
