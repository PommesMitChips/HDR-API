# Public API helpers

| Helper | Caller | Contract |
| --- | --- | --- |
| [Ingame/HdrIngameApi.cs](Ingame/HdrIngameApi.cs) | Programmable block | Short `HDR.Draw/1`; structural capabilities and nonthrowing status helpers |
| [HoloMapApi.cs](HoloMapApi.cs) | Programmable block | Compatibility typed `HDR.Api/1` delegate dictionary |
| [PortalApi.cs](PortalApi.cs) | Programmable block | Numeric portal/capture-shell declaration helpers |
| [Mods/HdrModApi.cs](Mods/HdrModApi.cs) | Client mod | Local `HDR.ModClient/1` consumers, HUD/world contexts and lifecycle |

PB helpers are pasted inside `Program` using the standard PB imports; consumer mod helpers compile as mod source. They are not interchangeable endpoints. See [API boundaries](../docs/wiki/API-Boundaries.md) and the [complete reference](../docs/wiki/Api-Reference.md).

The stable API contract is distinct from product version, scene replication protocol and native resource generations. External source/backend protocols are described in [mod integration](../docs/wiki/Mod-Integration.md).

The PB short endpoint and client-mod wrapper expose retained [hologram effects](../docs/wiki/Special-Effects.md); parameters use standard numeric arrays/tuples when a complete descriptor is needed. Effects remain owned by the drawing item and evaluated on viewing clients.
