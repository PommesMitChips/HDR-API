# HDR.HTML/Profile1

**ALPHA, frontend 0.1.1.** Profile1 is a bounded mod-native HTML/CSS authoring format for local UI. Acceptance is defined by the parser and layout shipped in [`Data/Scripts/HdrHtml`](../Data/Scripts/HdrHtml), followed by the selected painter's admission. Browser-valid HTML/CSS can still be rejected. No JavaScript, browser DOM, Canvas, WebGL, network fetch, iframe or event-handler execution is implemented.

## Markup

| Elements | Behavior |
| --- | --- |
| `html`, `body`, `div`, `section`, `main`, `header`, `footer` | Block containers by default |
| `p`, `h1`, `h2`, `h3`, `label` | Block text containers; headings have profile size/margin defaults |
| `span`, `br` | Inline text grouping and explicit line break |
| `button` | Rectangular pointer hit region; emits an owned `click` event |
| `input type="range"` | Rectangular track, fill and handle; emits owned `change` events |
| `head`, `style`, `title` | Accepted but not painted; `style` supplies CSS |

Fragments such as `<main>...</main>` are accepted. Optional `<!DOCTYPE html>` and HTML comments are accepted. Nesting must close explicitly and in order; there is no browser error recovery or automatic paragraph closing. Only `input` and `br` are void/self-closing elements, and they cannot have closing tags.

Global attributes are exactly `id`, `class`, `style`, `data-bind` and `data-action`. Additional attributes are:

| Element | Attributes |
| --- | --- |
| `label` | `for` (accepted metadata; no label activation behavior) |
| `button` | `type="button"`, `disabled` |
| `input` | Required `type="range"`; `min`, `max`, `step`, `value`, `disabled` |
| `style` | Optional `type="text/css"` |

`disabled` may be a bare Boolean attribute; its presence suppresses hit regions. Other attributes require values. Attribute names and tags are case-insensitive; IDs and class names match case-sensitively. Duplicate attributes and duplicate IDs fail. IDs have 1–128 characters without whitespace; binding/action/label-target values have the same length and whitespace rule. Each element admits at most 16 attributes, 4096 source characters per attribute value and 32 class names of at most 128 characters each.

Text accepts valid Unicode scalars, tabs and newlines, with control-character and surrogate validation. Supported named entities are `amp`, `lt`, `gt`, `quot`, `apos` and `nbsp`; decimal and hexadecimal numeric entities are accepted for valid scalars. Entities require a semicolon. Other named entities fail.

Elements outside the table fail, including `a`, `img`, inline `svg`, `strong`, `em`, lists, tables, forms, text inputs, `script`, `link`, `canvas`, `iframe`, audio and video. URL attributes, arbitrary `data-*`/ARIA attributes and `on*` handlers are not accepted. The grouped SVG **backend** does not add `<svg>` authoring support.

## CSS

CSS can come from the stylesheet argument, an embedded `style` element or inline `style`. Compound selectors such as `main.panel#status`, universal `*`, element, class and ID selectors are supported, as are comma-separated selector lists. A compound selector admits at most one ID; selector names use ASCII letters, digits, hyphens and underscores. Selectors have a 256-character limit. Descendant/child/sibling combinators, attribute selectors, pseudo-classes and pseudo-elements fail.

The cascade applies matching-rule specificity, then source order, then inline declarations. `color`, `font-size`, `font-family`, `line-height`, `text-align` and `white-space` inherit. Unsupported declarations are validated even in unused selectors and hidden branches.

| Accepted properties | Supported values and restrictions |
| --- | --- |
| `display` | `none`, `block`, `flex`, `inline`; authored inline layout is limited to `span`/`br` and text |
| `width`, `height`, `min-width`, `max-width`, `min-height`, `max-height` | Nonnegative `px`/`%`; `auto` only for width/height; percent heights require a definite parent height |
| `margin`, `padding`, their `-top`/`-right`/`-bottom`/`-left` longhands | One to four shorthand values, nonnegative `px`/`%`; no negative or `auto` margins |
| `gap`, `row-gap`, `column-gap` | Nonnegative `px`/`%` |
| `flex-direction`, `flex-grow` | `row`/`column`; finite grow weight 0–10000 |
| `align-items`, `align-self` | `stretch`, `flex-start`, `flex-end`, `center`; `align-self` also accepts `auto` |
| `justify-content` | `flex-start`, `flex-end`, `center`, `space-between`, `space-around`, `space-evenly` |
| `background`, `background-color`, `color`, `border-color` | Solid supported color; `background` aliases `background-color` |
| `border`, `border-width`, `border-radius` | `border: none` or `width solid color`; nonnegative `px` widths/radius, or unitless zero; radius is one scalar |
| `font-size`, `font-family` | Backend-specific measured size/family described below; size must be positive |
| `line-height` | Unitless finite multiplier 1–4, default 1.3 |
| `text-align`, `white-space` | `left`/`right`/`center`; `normal`/`nowrap`/`pre` |
| `overflow`, `opacity` | `visible`/`hidden`; finite opacity 0–1 only on leaves when not 1 |

All lengths accept unitless `0`; other unitless lengths fail. `px` lengths are bounded to 8192, percent numeric values to 10000. Dimensions resolve against the corresponding available parent dimension; percentage margins, padding and gaps use available width. `font-size: %` resolves against inherited measured font size. CSS `em`, `rem`, viewport units, `calc`, variables, escapes, URLs, `!important`, at-rules and nested rule blocks fail.

Flex layout is one row or column with growth and min/max constraints. It does not implement wrapping or flex shrinking. `flex-wrap`, `flex`, `flex-basis`, `flex-shrink`, `order`, grid, floats, position offsets, transforms, animations, gradients, shadows, font weight/style and individual `border-*-width`/`border-*-color` declarations are rejected by the CSS parser. Inline boxes cannot carry dimensions, margin, padding, borders, backgrounds or rounded corners. Blocks nested inside inline runs fail. Margins do not collapse. `overflow: hidden` clips to a rectangle; rounded backgrounds do not create a rounded clipping mask. Opacity on an element with children requires group compositing and is rejected when it differs from 1.

Widths/heights describe the content box; padding and solid border strips add to outer size. Flow stacks blocks vertically and groups adjacent inline text. `normal` collapses whitespace and wraps words; oversized words can wrap between Unicode scalars. `nowrap` collapses whitespace without wrapping. `pre` preserves newlines and expands tabs to four spaces without automatic wrapping. This is deterministic profile behavior, not full browser line breaking or grapheme layout.

## Fonts and coordinates

For vector/SVG, the exact metric schema is **`HDR.TextMetrics/1:Inter:cap-height`**. `font-size: 16px` scales the packaged Inter capital height to 16 logical pixels; it is not a browser 16px em box. The font bake does not retain an exact em-to-cap ratio. Measurement reads the same quantized outlines and advances as HDR text rendering and returns source-outline ink bounds, cap height and line advance. It does not tessellate glyph geometry. Vector/SVG accepts an omitted family, `Inter`, `HDR Inter` or `sans-serif`; the last selects the backend's measured font, not a fallback stack.

The packaged font renders scalar glyphs and substitutes `?` for unsupported code points. It has no shaping, kerning, ligatures, bidirectional reordering, grapheme clustering or system-font fallback. Accepting a Unicode string therefore does not establish faithful rendering of every language or emoji. Text runs are split to at most 64 UTF-16 units without splitting surrogate pairs; that is not a grapheme guarantee.

Native LCD uses **`Debug:Lcd`**, measured with the actual target's `MeasureStringInPixels(..., "Debug", scale)`. Its size normalizes the engine's measured `M` line height, and its ink box is a conservative proxy because the surface API does not expose exact glyph/shadow ink bounds. These are not Inter cap metrics. Use an omitted family, `Debug` or `sans-serif`; an explicit Inter family on native LCD fails. Rebuild layout with the selected backend's metrics instead of transferring Inter paint to Debug. Native glyph availability remains the game's font behavior.

Default font size is 16; `h1`/`h2`/`h3` default to 32/24/20. Default heading top/bottom margins are 16/12/10px; paragraphs have 8px top/bottom margins. Defaults have the selected metric meaning.

Viewport coordinates start at the top left, with +X right and +Y down. HUD units are pixels. A world pose locates that top left at its translation; local +X is right and local −Y is down. World scale is finite metres per logical pixel in 0.000001–1000. The logical viewport and each box are bounded to 8192 pixels per dimension; a viewport must be at least 1×1. Camera distance does not change authored layout or text size.

## Data and events

`SetText` replaces a node's content with plain text, without reparsing it as HTML. Text templates use `{{key}}` substitutions from `SetData`; missing keys substitute empty text. `data-bind` on a range selects its numeric data value; `data-action` supplies descriptive event data. Default range bounds/value/step are 0/100/minimum/1; `step="any"` is accepted. Values clamp to bounds and quantize relative to the minimum. No form submission, keyboard focus, tab navigation or browser accessibility tree is implemented.

Consumers supply cooperative logical pointer coordinates and poll `click`/`change` events from the last committed visible revision. The consumer must already own and block the corresponding game input. See the [local API](Local-API.md) for cancellation, generation changes and event tuples.

## Bounds and backend admission

Default limits are 131072 combined HTML/CSS characters, 512 nodes, depth 32, 256 CSS rules, 2048 expanded declarations, 16384 decoded/expanded text characters, 512 paint operations and 64 hit regions. The root layout node and generated work also consume bounded layout budgets. The local service admits eight owners, four documents per owner, 128 binding keys and 128 queued events per document.

The HDR painter additionally admits at most 64 retained items, 8192 points and 8192 primitives per document, subject to the core owner's shared geometry grant across its contexts. A single source must fit the core compiler's limits too. Unsupported features or budget exhaustion produce status errors instead of a hidden reduction in quality. Native LCD adds its [rectangle/text/scissor restrictions](Rendering.md). Parsing/layout success alone is not proof that a replacement is visible; inspect desired and visible revisions through the API.
