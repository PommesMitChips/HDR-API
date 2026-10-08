# Third-party notices

These files are distribution contents of the optional JavaScript mod, not links that recipients must fetch. Preserve this directory and the retained vendor source headers when copying or packaging the mod. They cover third-party code; they do not replace the repository's own licensing decisions.

| Retained code | License and included notice | Source |
| --- | --- | --- |
| Jint 2.11.10 engine and C# source | [BSD 2-Clause](Jint-BSD-2-Clause.txt), exact original bytes; [original credits](Jint-CREDITS.txt), exact original bytes | Jint commit `340f0f4ffc2f2b389ebac0d9a4da318f248f20b6` |
| Bundled C# port of Esprima | Original Jint credits identify Ariya Hidayat and BSD. Included historical [BSD text](Esprima-BSD.txt) and [copyright/license header](Esprima-COPYRIGHT-NOTICE.txt) are exact supplements from Esprima 1.2.2. | Esprima commit `1114c32c4e0ffaf47864967a835fdc0a37909f14`; the Jint pin does not identify its bundled parser's original revision |
| V8 FastDtoa-derived files | [BSD 3-Clause notice](V8-Dtoa-NOTICE.txt), copied byte-for-byte from the original `DoubleHelper.cs` header; the original headers remain in the retained vendor files | Jint pin above; header cites Mozilla revision `67d1049b0bf9` and Hannes Wallnoefer's Java port |
| `FastDtoaBuilder.cs` | Original per-file MPL 2.0 notice plus the full [Mozilla Public License 2.0](MPL-2.0.txt) | Jint pin above; full license obtained from [Mozilla](https://www.mozilla.org/media/MPL/2.0/index.txt) |

Do not describe every bundled file as BSD 2-Clause: the pinned engine includes distinct BSD 3-Clause and MPL 2.0 notices. The covered source files and modifications ship as readable `.cs` in `Data/Scripts/HDRJavaScriptRuntime/Vendor/Jint`. The full source distribution and notice inventory are recorded in [ORIGIN](../ORIGIN.md).

`Esprima-BSD.txt` is the unmodified standalone file, which refers to an "above copyright" but contains no such line itself. The separate exact copyright header and Jint's original credit supply the corresponding attribution; neither file has been rewritten to invent a notice. That historical supplement is not a claim that the bundled parser equals Esprima 1.2.2.

The package gate must verify these files are present at their recorded SHA-256 hashes, retain the original V8/MPL source headers, and record the final source adaptation inventory. The upstream checkout and complete test corpus are development inputs, not additional mod runtime dependencies.
