# Put plain HTML on a block display

**ALPHA — frontend 0.2.2 / core 0.9.11 / scene 20.** Your UI lives in the programmable block's Custom Data as plain HTML/CSS. The PB code is a tiny invocation; the frontend mod owns mounting, layout, watching and cleanup. This uses `HDR.HTML/Profile1`, a strict UI subset, rather than a complete HTML5 browser.

## First display

1. Add both **HDR API 0.9.11** and **HDR HTML Frontend 0.2.2** to the world. The separate JS runtime/plugin is unnecessary for display geometry.
2. Name an authorized LCD, Console or Projector **`HTML Display`** on the PB's construct. Give the name to exactly one matching block. For a physical LCD, manually select its **HDR API** text-surface script first.
3. Paste [`PlainHtmlPbDemo.html`](../Examples/PlainHtmlPbDemo.html) into the **programmable block's Custom Data**. This is the HTML document, not the PB's C# code or the target display's Custom Data.
4. Paste the whole small [`HtmlPbDemo.cs`](../Examples/HtmlPbDemo.cs) into the PB code editor and save it. Change only `HTML Display` in that loader if you chose a different exact name.
5. Run the PB with no argument, or `demo`. Its output reports the selected target and mount status.

The example document is a complete `html`/`head`/`body` document with `<style>` in the head. You can also use a fragment such as:

```html
<main style="padding:12px;background-color:#123442;color:#00d0cd">
  <h2>Ship status</h2>
  <p>Ready</p>
</main>
```

The default authored canvas is 480×280 logical pixels. The mod centres it; on an LCD it fits the actual panel aspect without stretching. Console/Projector output uses .005 metres per logical pixel and the block's existing view/range settings.

## Edit and retry

Edit HTML/CSS in the PB's Custom Data and save it. The mod checks for changes every 30 simulation ticks, so the PB does not need `Update10` or a draw loop. Unchanged source is not reparsed continuously.

Run `status` to inspect the mount. If an edit is invalid, the previous published display stays available and the output retains the source error. Correct the HTML and save; run `reload` to force a reattempt if needed. Invalid source never becomes an instruction to call a terminal action.

| PB argument | Result |
| --- | --- |
| Empty, `demo` or `mount` | Deliberately mount or refresh this PB's one automatic authoring document |
| `reload` | Force source reattempt |
| `status` | Print selected target/type, publication/pending state and errors |
| `clear` | Remove only this automatic mount |

After changing the PB's program, turning it off, or losing the target, the mod retires the mount. Run again deliberately after recovery; editing Custom Data does not resurrect a retired mount or choose a replacement block automatically.

## Controls and values

The plain demo contains a range with `data-bind="level"`. The mod synchronizes its canonical range value back into that document's binding, so `{{level}}` text can update without an event loop in the PB. That is UI state, not engine power or a game variable. `data-action` is descriptive event data and never runs gameplay code automatically.

Core SVG button/range input retains the existing per-viewer **HDR Client Renderer 0.9.13+** requirement; the server and display geometry do not require that plugin. Publication status is server state, not a remote viewer/GPU acknowledgement. Input/source providers keep their actual capability checks.

If you want values to control gameplay or a PB variable, use the separate [bindings/controller example](../Examples/HtmlPbBindingsDemo.cs) and [advanced PB API](PB-API.md). Camera composition, shaped screens and native sprite alternatives also remain advanced explicit choices rather than new browser features.

## Profile limits

Read the [authoring profile](Profile.md) before copying browser markup. Full document wrappers and embedded CSS are supported, but the parser rejects `<script>`, inline `on*` handlers, unsupported elements/styles and network resources. Declarative CSS animation, browser DOM, Canvas and PB JavaScript are not added by the loader. The optional [JS runtime](../../../docs/wiki/JavaScript-Prototype.md) has its separate client-local mod API.

If no projection appears, inspect the PB output, confirm the exact target name/access and manual LCD script selection, then check block range/view settings and viewer rendering state. The loader prints errors and does not change content modes, rename targets, create virtual LCDs or silently substitute a different renderer.
