# Source provenance

**ALPHA source prototype.** The upstream engine is Jint 2.11.10, from [sebastienros/jint](https://github.com/sebastienros/jint), pinned to commit [`340f0f4ffc2f2b389ebac0d9a4da318f248f20b6`](https://github.com/sebastienros/jint/tree/340f0f4ffc2f2b389ebac0d9a4da318f248f20b6). The verified sparse Git checkout supplies the original 157 C# library files. The shipped [source manifest](ORIGIN/Jint-source-manifest.json) maps all originals: 140 selected files, 17 omitted with reasons and two explicitly HDR-authored vendor helpers. It records upstream/shipped SHA-256 values and normalized-text modification flags. No upstream DLL, NuGet dependency bundle, native runtime or plugin is treated as Workshop mod source.

The complete archive URL is [`https://codeload.github.com/sebastienros/jint/zip/340f0f4ffc2f2b389ebac0d9a4da318f248f20b6`](https://codeload.github.com/sebastienros/jint/zip/340f0f4ffc2f2b389ebac0d9a4da318f248f20b6), verified at SHA-256 `38229A27DBB18905F73203EB60E707454209F54837CF4E0FCD3198242A9940B8`. The shipped manifest remains the authority for the current port hashes and must be regenerated after any vendor repair.

## Licenses carried in the mod

The original `LICENSE.txt` and `CREDITS.txt` from this exact Jint commit are preserved byte-for-byte in [LICENSES](LICENSES/README.md):

| Mod file | SHA-256 | Origin |
| --- | --- | --- |
| `LICENSES/Jint-BSD-2-Clause.txt` | `8626F0B8F39A9AE2BFD4ACA1DA667076A781E4AE07312BC2F880ACCCB57ABDF4` | Exact pinned `LICENSE.txt` |
| `LICENSES/Jint-CREDITS.txt` | `054F2E7C8B325B6E8461E1F0908139A0A56E610AEE05A2DB9228F87EE9A8B3E0` | Exact pinned `CREDITS.txt` |
| `LICENSES/V8-Dtoa-NOTICE.txt` | `3418ED12299F9049FE5441A2807A4694BB69DEFE9AB640A05567CF3138341048` | Exact first 29 lines of pinned `Jint/Native/Number/Dtoa/DoubleHelper.cs` |
| `LICENSES/MPL-2.0.txt` | `3F3D9E0024B1921B067D6F7F88DEB4A60CBE7A78E76C64E3F1D7FC3B779B9D04` | Full license, [official Mozilla plaintext](https://www.mozilla.org/media/MPL/2.0/index.txt) |
| `LICENSES/Esprima-BSD.txt` | `0E74697A68CEBDCD61502C30FE80AB7F9E341D995DCD452023654D57133534B1` | Exact `LICENSE.BSD`, Esprima commit `1114c32c4e0ffaf47864967a835fdc0a37909f14` / historical tag 1.2.2 |
| `LICENSES/Esprima-COPYRIGHT-NOTICE.txt` | `9F5A4DA6ACE561BB7A3C38F16E8777DD51E84B95D893D4C72D7C57027C951563` | Exact initial comment of `esprima.js` at that same Esprima commit |

The bundled C# parser is included in `Jint/Parser`; it is not a separate Acornima/NuGet dependency in this source line. Original Jint credits identify it as a C# port of Esprima, copyright Ariya Hidayat, BSD, and point to an upstream license. They do not pin the original Esprima revision or include a complete standalone parser license. The historical supplement above preserves an authentic Ariya-era license/header; it does not invent a parser version or replace Jint's exact credits.

The Dtoa inventory at the Jint pin is:

| Original file | Retained notice |
| --- | --- |
| `Jint/Native/Number/Dtoa/CachePowers.cs` | V8 BSD 3-Clause |
| `Jint/Native/Number/Dtoa/DiyFp.cs` | V8 BSD 3-Clause |
| `Jint/Native/Number/Dtoa/DoubleHelper.cs` | V8 BSD 3-Clause |
| `Jint/Native/Number/Dtoa/FastDtoa.cs` | V8 BSD 3-Clause |
| `Jint/Native/Number/Dtoa/FastDtoaBuilder.cs` | MPL 2.0 |

Original per-file notices must remain in retained/modified vendor code. Covered source ships in readable form inside this mod's `Data/Scripts` tree; package notices must remain available with that source. If an algorithm is omitted or replaced, the final port manifest must say so without relabeling retained MPL/V8 source as Jint-only BSD.

## Adaptation and capability scope

The changes are source-level compatibility and explicit host binding: remove arbitrary CLR/reflection integration; add a deliberate realm adapter and interpreter-owned callback handles; add configurable execution safeguards and parser-depth guards; and integrate owned document events/timers through one validated mutation batch. The port omits `Date`/local time, regular expressions/literals, dynamic `eval`/`Function` construction and debugger integration. V8/Mozilla number conversion code is retained with its original headers; removal of diagnostic `Debug.Assert` calls does not change those notices. The manifest lists current adaptations. Final compiler/test/package receipts are separate evidence; they do not change this source/license provenance, and live game acceptance remains unverified.

Jint's engine does not supply a browser DOM, renderer, web-resource loader or sandbox bypass. The [profile](Docs/Profile.md) lists the delivered language/host scope. The JavaScript runtime requires no renderer plugin; optional native capture/texture/input features retain their existing provider checks and specific missing-capability diagnostics.
