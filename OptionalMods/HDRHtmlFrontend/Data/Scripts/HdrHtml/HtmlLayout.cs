using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hdr.Html
{
    // A deliberately bounded, static layout profile. Logical coordinates use top-left / Y down.
    public static class HtmlLayout
    {
        public static HtmlPaintFrame Build(HtmlDocument document, double width, double height, IHtmlTextMetrics metrics, IDictionary<string, string> data, HtmlLimits limits)
        {
            if (document == null || document.Root == null || metrics == null || limits == null) throw new ArgumentException("Document, metrics and limits are required.");
            HtmlPaintValidation.Limits(limits);
            HtmlPaintValidation.Dimensions(width, height);
            if (string.IsNullOrEmpty(metrics.Profile)) throw new ArgumentException("A measured font profile is required.");
            // Admission validates unused selectors as well as visible branches.
            for (int i = 0; i < document.Rules.Count; i++)
            {
                var declarations = document.Rules[i].Declarations;
                string display; declarations.TryGetValue("display", out display);
                HtmlLayoutCss.Read(new HtmlNode { Tag = string.Equals(display, "inline", StringComparison.OrdinalIgnoreCase) ? "span" : "div" }, new Dictionary<string, string>(declarations, StringComparer.OrdinalIgnoreCase), width);
                ValidateFont(declarations, metrics.Profile);
            }
            var context = new LayoutContext(document, metrics, data, limits, width, height);
            LayoutBox root = context.Create(document.Root, null, width, 0);
            context.Place(root, 0, 0, width, height, width, height);
            context.Paint(root, new HtmlRect(0, 0, width, height));
            HtmlPaintValidation.Validate(context.Frame, limits);
            return context.Frame;
        }
        private static void ValidateFont(Dictionary<string, string> values, string profile)
        {
            string family;
            if (!values.TryGetValue("font-family", out family)) return;
            family = family.Trim().ToLowerInvariant().Trim('"', '\'');
            bool native = profile == "Debug:Lcd";
            if (family == "sans-serif") return;
            if ((native && family != "debug") || (!native && family != "inter" && family != "hdr inter")) throw new ArgumentException("Authored font-family does not match the selected " + profile + " metrics backend.");
        }

        private sealed class LayoutBox
        {
            internal HtmlNode Node;
            internal HtmlBoxStyle Style;
            internal readonly List<LayoutBox> Children = new List<LayoutBox>();
            internal readonly List<TextSegment> Text = new List<TextSegment>();
            internal double X, Y, Width, Height, ContentX, ContentY, ContentWidth, ContentHeight;
            internal bool TextGroup, Hidden, Consumed, Generated;
            internal string RawText;
            internal bool Placed;
            internal double AvailableWidth, AvailableHeight, ForcedWidth, ForcedHeight;
        }
        private sealed class TextSegment
        {
            internal HtmlNode Node;
            internal HtmlBoxStyle Style;
            internal string Text;
            internal int Line, Chunk;
            internal double X, Y;
            internal HtmlTextMeasurement Metrics;
        }
        private sealed class InlineRun
        {
            internal HtmlNode Node; internal HtmlBoxStyle Style; internal string Text;
        }
        private sealed class LayoutContext
        {
            private readonly HtmlDocument _document;
            private readonly IHtmlTextMetrics _metrics;
            private readonly IDictionary<string, string> _data;
            private readonly HtmlLimits _limits;
            private int _nodes, _textCharacters, _fontGeneration = int.MinValue, _layoutWork, _metricWork;
            private readonly Dictionary<string, HtmlTextMeasurement> _metricCache = new Dictionary<string, HtmlTextMeasurement>(StringComparer.Ordinal);
            internal readonly HtmlPaintFrame Frame;
            internal LayoutContext(HtmlDocument document, IHtmlTextMetrics metrics, IDictionary<string, string> data, HtmlLimits limits, double width, double height)
            {
                _document = document; _metrics = metrics; _data = data; _limits = limits;
                Frame = new HtmlPaintFrame { Width = width, Height = height, FontProfile = metrics.Profile };
            }
            internal LayoutBox Create(HtmlNode node, Dictionary<string, string> inherited, double width, int depth)
            {
                if (++_nodes > _limits.MaxNodes || depth > _limits.MaxDepth) throw new ArgumentException("HTML layout node/depth limit exceeded.");
                HtmlLayoutCss.Read(new HtmlNode { Tag = node.Tag }, new Dictionary<string, string>(node.InlineStyle, StringComparer.OrdinalIgnoreCase), width);
                ValidateFont(node.InlineStyle, _metrics.Profile);
                var inheritedWithDefaults = inherited == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(inherited, StringComparer.OrdinalIgnoreCase);
                if (!inheritedWithDefaults.ContainsKey("font-family")) inheritedWithDefaults["font-family"] = _metrics.Profile == "Debug:Lcd" ? "Debug" : "Inter";
                if (node.Tag == "h1" || node.Tag == "h2" || node.Tag == "h3") inheritedWithDefaults["font-size"] = node.Tag == "h1" ? "32px" : node.Tag == "h2" ? "24px" : "20px";
                var values = HtmlCss.Resolve(_document, node, inheritedWithDefaults);
                string inheritedSize; double inheritedFont = inherited != null && inherited.TryGetValue("font-size", out inheritedSize) ? HtmlLayoutCss.Length(inheritedSize, "inherited font-size", 16, true) : 16;
                var box = new LayoutBox { Node = node, Style = HtmlLayoutCss.Read(node, values, width, inheritedFont) };
                ValidateFont(values, _metrics.Profile);
                // Propagate the actual profile defaults, rather than losing h1/h2 cap heights in #text children.
                var childInherited = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
                childInherited["font-size"] = box.Style.Font.ToString("R", CultureInfo.InvariantCulture) + "px";
                childInherited["line-height"] = box.Style.Line.ToString("R", CultureInfo.InvariantCulture);
                childInherited["color"] = ColorText(box.Style.Color);
                childInherited["font-family"] = _metrics.Profile == "Debug:Lcd" ? "Debug" : "Inter";
                childInherited["text-align"] = box.Style.TextAlign;
                childInherited["white-space"] = box.Style.WhiteSpace;
                box.Hidden = box.Style.Display == "none" || node.Tag == "head" || node.Tag == "style" || node.Tag == "title";
                if (node.Tag == "#text")
                {
                    HtmlPaintValidation.Text(node.Text ?? "", _limits.MaxTextCharacters);
                    string text = Substitute(node.Text ?? "");
                    _textCharacters += text.Length;
                    if (_textCharacters > _limits.MaxTextCharacters) throw new ArgumentException("Expanded text exceeds the text limit.");
                    box.TextGroup = true; box.RawText = text;
                    box.Text.Add(new TextSegment { Node = node, Style = box.Style, Text = text });
                }
                for (int i = 0; i < node.Children.Count; i++) box.Children.Add(Create(node.Children[i], childInherited, width, depth + 1));
                return box;
            }
            private string Substitute(string text)
            {
                var result = new StringBuilder(); int start = 0;
                while (start < text.Length)
                {
                    int open = text.IndexOf("{{", start, StringComparison.Ordinal);
                    if (open < 0) { result.Append(text, start, text.Length - start); break; }
                    result.Append(text, start, open - start);
                    int close = text.IndexOf("}}", open + 2, StringComparison.Ordinal);
                    if (close < 0) { result.Append(text, open, text.Length - open); break; }
                    string key = text.Substring(open + 2, close - open - 2).Trim(); string value;
                    if (key.Length == 0 || key.Length > 128) throw new ArgumentException("Invalid text binding key.");
                    if (_data != null && _data.TryGetValue(key, out value))
                    {
                        value = value ?? ""; HtmlPaintValidation.Text(value, _limits.MaxTextCharacters);
                        if ((long)result.Length + value.Length > _limits.MaxTextCharacters) throw new ArgumentException("Expanded text exceeds the text limit.");
                        result.Append(value);
                    }
                    if (result.Length > _limits.MaxTextCharacters) throw new ArgumentException("Expanded text exceeds the text limit.");
                    start = close + 2;
                }
                return result.ToString();
            }
            internal void Place(LayoutBox box, double x, double y, double availableWidth, double availableHeight, double forcedWidth, double forcedHeight)
            {
                if (box.Hidden) return;
                if (++_layoutWork > 4096) throw new ArgumentException("HTML layout exceeded the bounded 4096-pass work budget.");
                if (box.Placed && Same(box.AvailableWidth, availableWidth) && Same(box.AvailableHeight, availableHeight) && EquivalentForced(box.ForcedWidth, forcedWidth, box.Width) && EquivalentForced(box.ForcedHeight, forcedHeight, box.Height))
                {
                    Move(box, x + box.Style.Margin[3] - box.X, y + box.Style.Margin[0] - box.Y); return;
                }
                box.Style = HtmlLayoutCss.Read(box.Node, box.Style.Values, availableWidth, box.Style.FontReference);
                HtmlBoxStyle s = box.Style;
                for (int i = box.Children.Count - 1; i >= 0; i--)
                {
                    if (box.Children[i].Generated) box.Children.RemoveAt(i);
                    else box.Children[i].Consumed = false;
                }
                double edgeX = s.Padding[1] + s.Padding[3] + s.Border[1] + s.Border[3];
                double edgeY = s.Padding[0] + s.Padding[2] + s.Border[0] + s.Border[2];
                double authoredWidth = HtmlLayoutCss.Dimension(s.Values, "width", availableWidth, double.NaN);
                double contentWidth = !double.IsNaN(forcedWidth) ? forcedWidth - edgeX : !double.IsNaN(authoredWidth) ? authoredWidth : availableWidth - s.Margin[1] - s.Margin[3] - edgeX;
                contentWidth = ClampDimension(s.Values, "width", availableWidth, Math.Max(0, contentWidth));
                double authoredHeight = HtmlLayoutCss.Dimension(s.Values, "height", availableHeight, double.NaN);
                if (!HtmlLayoutCss.Finite(authoredHeight) && s.Values.ContainsKey("height") && HtmlLayoutCss.Get(s.Values, "height", "auto") != "auto") throw new ArgumentException("Percent heights require a definite parent height.");
                double contentHeight = !double.IsNaN(forcedHeight) ? forcedHeight - edgeY : authoredHeight;
                box.X = x + s.Margin[3]; box.Y = y + s.Margin[0];
                box.ContentX = box.X + s.Border[3] + s.Padding[3]; box.ContentY = box.Y + s.Border[0] + s.Padding[0];
                box.ContentWidth = contentWidth;
                double measuredHeight;
                if (box.TextGroup)
                {
                    var runs = new List<InlineRun>();
                    runs.Add(new InlineRun { Node = box.Node, Style = box.Style, Text = box.RawText ?? "" });
                    box.Text.Clear(); measuredHeight = Inline(box, runs, contentWidth);
                }
                else if (box.Node.Tag == "input") measuredHeight = RangeHeight(box, contentWidth);
                else if (box.Node.Tag == "img" || box.Node.Tag == "svg")
                {
                    if (double.IsNaN(contentHeight)) throw new ArgumentException("Registered image/SVG elements require explicit height.");
                    measuredHeight = contentHeight;
                }
                else if (s.Display == "flex") measuredHeight = Flex(box, contentWidth, contentHeight);
                else measuredHeight = Flow(box, contentWidth, contentHeight);
                if (double.IsNaN(contentHeight)) contentHeight = measuredHeight;
                contentHeight = ClampDimension(s.Values, "height", availableHeight, Math.Max(0, contentHeight));
                box.ContentHeight = contentHeight; box.Width = contentWidth + edgeX; box.Height = contentHeight + edgeY;
                CheckSize(box);
                box.Placed = true; box.AvailableWidth = availableWidth; box.AvailableHeight = availableHeight; box.ForcedWidth = forcedWidth; box.ForcedHeight = forcedHeight;
            }
            private static bool Same(double a, double b) { return a == b || (double.IsNaN(a) && double.IsNaN(b)); }
            private static bool EquivalentForced(double oldValue, double newValue, double actual)
            {
                return Same(oldValue, newValue) || ((double.IsNaN(oldValue) || oldValue == actual) && (double.IsNaN(newValue) || newValue == actual));
            }
            private static double ClampDimension(Dictionary<string, string> values, string axis, double reference, double size)
            {
                double min = HtmlLayoutCss.Dimension(values, "min-" + axis, reference, 0), max = HtmlLayoutCss.Dimension(values, "max-" + axis, reference, 8192);
                if (!HtmlLayoutCss.Finite(min) || !HtmlLayoutCss.Finite(max) || min > max) throw new ArgumentException("Invalid min/max " + axis + ".");
                return Math.Max(min, Math.Min(max, size));
            }
            private double Flow(LayoutBox box, double width, double height)
            {
                double cursor = 0;
                for (int i = 0; i < box.Children.Count;)
                {
                    LayoutBox child = box.Children[i];
                    if (child.Hidden) { i++; continue; }
                    if (child.Style.Display == "inline")
                    {
                        var runs = new List<InlineRun>(); int begin = i;
                        while (i < box.Children.Count && (box.Children[i].Hidden || box.Children[i].Style.Display == "inline")) { if (!box.Children[i].Hidden) GatherInline(box.Children[i], runs); i++; }
                        var group = new LayoutBox { Node = box.Children[begin].Node, Style = box.Style, TextGroup = true, Generated = true, ContentX = box.ContentX, ContentY = box.ContentY + cursor, ContentWidth = width };
                        double lineHeight = Inline(group, runs, width);
                        group.X = group.ContentX; group.Y = group.ContentY; group.Width = width; group.Height = lineHeight; group.ContentHeight = lineHeight;
                        // Replace only the local layout tree, never caller-owned DOM nodes.
                        for (int j = begin; j < i; j++) box.Children[j].Consumed = true;
                        box.Children.Insert(i, group); i++;
                        cursor += lineHeight;
                    }
                    else
                    {
                        Place(child, box.ContentX, box.ContentY + cursor, width, height, double.NaN, double.NaN);
                        cursor += child.Style.Margin[0] + child.Height + child.Style.Margin[2]; i++;
                    }
                }
                return cursor;
            }
            private void GatherInline(LayoutBox box, List<InlineRun> runs)
            {
                if (box.Hidden) return;
                if (box.Node.Tag == "br") { box.Style.WhiteSpace = "pre"; runs.Add(new InlineRun { Node = box.Node, Style = box.Style, Text = "\n" }); return; }
                if (box.Node.Tag == "#text") { runs.Add(new InlineRun { Node = box.Node, Style = box.Style, Text = box.RawText ?? "" }); return; }
                if (box.Style.Display != "inline") throw new ArgumentException("A block cannot be nested inside an inline run in Profile1.");
                for (int i = 0; i < box.Children.Count; i++) GatherInline(box.Children[i], runs);
            }
            private double IntrinsicWidth(LayoutBox box, double referenceWidth)
            {
                if (box.Hidden) return 0;
                box.Style = HtmlLayoutCss.Read(box.Node, box.Style.Values, referenceWidth, box.Style.FontReference);
                double width = HtmlLayoutCss.Dimension(box.Style.Values, "width", referenceWidth, double.NaN);
                if (!double.IsNaN(width)) return width + HorizontalEdges(box.Style);
                if (box.Node.Tag == "input") return 160 + HorizontalEdges(box.Style);
                double textWidth = 0, longest = 0;
                if (box.TextGroup) textWidth += Measure((box.RawText ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace('\n', ' ').Replace("\t", "    "), box.Style).Advance;
                for (int i = 0; i < box.Children.Count; i++)
                {
                    if (box.Children[i].Generated) continue;
                    double next = IntrinsicWidth(box.Children[i], referenceWidth);
                    if (box.Children[i].Style.Display == "inline") textWidth += next;
                    else longest = Math.Max(longest, next);
                }
                return Math.Max(textWidth, longest) + HorizontalEdges(box.Style);
            }
            private double Flex(LayoutBox box, double width, double height)
            {
                var children = new List<LayoutBox>();
                for (int i = 0; i < box.Children.Count; i++) if (!box.Children[i].Hidden)
                {
                    if (box.Children[i].Style.Display == "inline" && box.Children[i].Node.Tag == "#text" && string.IsNullOrWhiteSpace(box.Children[i].RawText)) { box.Children[i].Consumed = true; continue; }
                    box.Children[i].Style = HtmlLayoutCss.Read(box.Children[i].Node, box.Children[i].Style.Values, width, box.Children[i].Style.FontReference);
                    children.Add(box.Children[i]);
                }
                if (children.Count == 0) return 0;
                bool row = box.Style.Direction == "row";
                double gap = row ? box.Style.ColumnGap : box.Style.RowGap;
                var main = new double[children.Count]; double total = gap * (children.Count - 1), grow = 0, cross = 0;
                for (int i = 0; i < children.Count; i++)
                {
                    LayoutBox child = children[i]; HtmlBoxStyle s = child.Style;
                    if (row)
                    {
                        double authored = HtmlLayoutCss.Dimension(s.Values, "width", width, double.NaN);
                        main[i] = !double.IsNaN(authored) ? authored + HorizontalEdges(s) : Math.Min(width, IntrinsicWidth(child, width));
                        Place(child, 0, 0, width, height, main[i], double.NaN);
                        cross = Math.Max(cross, child.Height + s.Margin[0] + s.Margin[2]);
                        total += main[i] + s.Margin[1] + s.Margin[3];
                    }
                    else
                    {
                        double childWidth = WidthForAlign(child, width, s.Self == "auto" ? box.Style.Align : s.Self);
                        Place(child, 0, 0, width, height, childWidth, double.NaN);
                        double basis = HtmlLayoutCss.Dimension(s.Values, "height", height, double.NaN);
                        main[i] = double.IsNaN(basis) ? child.Height : basis + s.Border[0] + s.Border[2] + s.Padding[0] + s.Padding[2]; total += main[i] + s.Margin[0] + s.Margin[2];
                        cross = Math.Max(cross, child.Width + s.Margin[1] + s.Margin[3]);
                    }
                    grow += s.Grow;
                }
                double available = row ? width : double.IsNaN(height) ? total : height;
                double extra = Math.Max(0, available - total);
                double mainAvailable = available - gap * (children.Count - 1);
                for (int i = 0; i < children.Count; i++) mainAvailable -= row ? children[i].Style.Margin[1] + children[i].Style.Margin[3] : children[i].Style.Margin[0] + children[i].Style.Margin[2];
                AllocateGrow(children, main, Math.Max(0, mainAvailable), row, width, height);
                total = gap * (children.Count - 1);
                for (int i = 0; i < children.Count; i++) total += main[i] + (row ? children[i].Style.Margin[1] + children[i].Style.Margin[3] : children[i].Style.Margin[0] + children[i].Style.Margin[2]);
                extra = Math.Max(0, available - total);
                if (row)
                {
                    cross = 0;
                    for (int i = 0; i < children.Count; i++)
                    {
                        Place(children[i], 0, 0, width, height, main[i], double.NaN);
                        cross = Math.Max(cross, children[i].Height + children[i].Style.Margin[0] + children[i].Style.Margin[2]);
                    }
                }
                double start = 0, spacing = gap;
                if (box.Style.Justify == "flex-end") start = extra;
                else if (box.Style.Justify == "center") start = extra / 2;
                else if (box.Style.Justify == "space-between" && children.Count > 1) spacing += extra / (children.Count - 1);
                else if (box.Style.Justify == "space-around") { spacing += extra / children.Count; start = extra / children.Count / 2; }
                else if (box.Style.Justify == "space-evenly") { spacing += extra / (children.Count + 1); start = extra / (children.Count + 1); }
                double cursor = start;
                double crossAvailable = row ? double.IsNaN(height) ? cross : height : width;
                for (int i = 0; i < children.Count; i++)
                {
                    LayoutBox child = children[i]; HtmlBoxStyle s = child.Style; string align = s.Self == "auto" ? box.Style.Align : s.Self;
                    double forcedCross = double.NaN;
                    if (row && align == "stretch" && !HasSize(s, "height")) forcedCross = Math.Max(0, crossAvailable - s.Margin[0] - s.Margin[2]);
                    if (!row) forcedCross = WidthForAlign(child, width, align);
                    Place(child, 0, 0, width, height, row ? main[i] : forcedCross, row ? forcedCross : main[i]);
                    double actualCross = row ? child.Height + s.Margin[0] + s.Margin[2] : child.Width + s.Margin[1] + s.Margin[3];
                    double offset = align == "center" ? Math.Max(0, crossAvailable - actualCross) / 2 : align == "flex-end" ? Math.Max(0, crossAvailable - actualCross) : 0;
                    Move(child, box.ContentX + (row ? cursor : offset) - child.X + s.Margin[3], box.ContentY + (row ? offset : cursor) - child.Y + s.Margin[0]);
                    cursor += main[i] + (row ? s.Margin[1] + s.Margin[3] : s.Margin[0] + s.Margin[2]) + spacing;
                }
                return row ? crossAvailable : double.IsNaN(height) ? total : height;
            }
            private static void AllocateGrow(List<LayoutBox> children, double[] main, double available, bool row, double width, double height)
            {
                var frozen = new bool[children.Count]; var basis = (double[])main.Clone(); var lower = new double[children.Count]; var upper = new double[children.Count];
                for (int i = 0; i < children.Count; i++)
                {
                    HtmlBoxStyle s = children[i].Style;
                    double edge = row ? HorizontalEdges(s) : s.Border[0] + s.Border[2] + s.Padding[0] + s.Padding[2];
                    lower[i] = HtmlLayoutCss.Dimension(s.Values, row ? "min-width" : "min-height", row ? width : height, 0) + edge;
                    upper[i] = HtmlLayoutCss.Dimension(s.Values, row ? "max-width" : "max-height", row ? width : height, 8192) + edge;
                    if (!HtmlLayoutCss.Finite(lower[i]) || !HtmlLayoutCss.Finite(upper[i]) || lower[i] > upper[i]) throw new ArgumentException("Invalid flex min/max size.");
                    if (s.Grow <= 0) { main[i] = Math.Max(lower[i], Math.Min(upper[i], basis[i])); frozen[i] = true; }
                }
                // Keep authored/intrinsic bases; bound violations freeze, and their remainder is redistributed.
                for (int pass = 0; pass <= children.Count; pass++)
                {
                    double space = available, weight = 0;
                    for (int i = 0; i < children.Count; i++) { space -= frozen[i] ? main[i] : basis[i]; if (!frozen[i]) weight += children[i].Style.Grow; }
                    if (weight <= 0) break;
                    space = Math.Max(0, space); bool capped = false;
                    for (int i = 0; i < children.Count; i++)
                    {
                        if (frozen[i] || children[i].Style.Grow <= 0) continue;
                        double candidate = basis[i] + space * children[i].Style.Grow / weight;
                        main[i] = Math.Max(lower[i], Math.Min(upper[i], candidate));
                        if (Math.Abs(main[i] - candidate) > 1e-9) { frozen[i] = true; capped = true; }
                    }
                    if (!capped) break;
                }
            }
            private double WidthForAlign(LayoutBox box, double width, string align)
            {
                double authored = HtmlLayoutCss.Dimension(box.Style.Values, "width", width, double.NaN);
                if (!double.IsNaN(authored)) return authored + HorizontalEdges(box.Style);
                if (align == "stretch") return Math.Max(0, width - box.Style.Margin[1] - box.Style.Margin[3]);
                return Math.Min(width, IntrinsicWidth(box, width));
            }
            private static bool HasSize(HtmlBoxStyle s, string axis) { return s.Values.ContainsKey(axis) && HtmlLayoutCss.Get(s.Values, axis, "auto") != "auto"; }
            private static double HorizontalEdges(HtmlBoxStyle s) { return s.Border[1] + s.Border[3] + s.Padding[1] + s.Padding[3]; }
            private static void Move(LayoutBox box, double dx, double dy)
            {
                box.X += dx; box.Y += dy; box.ContentX += dx; box.ContentY += dy;
                for (int i = 0; i < box.Children.Count; i++) Move(box.Children[i], dx, dy);
            }
            private double Inline(LayoutBox box, List<InlineRun> runs, double width)
            {
                var lines = new List<List<TextSegment>>(); lines.Add(new List<TextSegment>());
                double currentWidth = 0; bool pendingSpace = false; InlineRun pendingSpaceRun = null;
                for (int r = 0; r < runs.Count; r++)
                {
                    InlineRun run = runs[r]; string raw = run.Text.Replace("\r\n", "\n").Replace('\r', '\n');
                    if (run.Style.WhiteSpace == "pre")
                    {
                        string[] explicitLines = raw.Replace("\t", "    ").Split('\n');
                        for (int i = 0; i < explicitLines.Length; i++)
                        {
                            AddText(lines[lines.Count - 1], run, explicitLines[i], ref currentWidth);
                            if (i < explicitLines.Length - 1) { lines.Add(new List<TextSegment>()); currentWidth = 0; }
                        }
                        pendingSpace = false;
                    }
                    else
                    {
                        int i = 0;
                        while (i < raw.Length)
                        {
                            if (Collapsible(raw[i])) { pendingSpace = true; pendingSpaceRun = run; i++; continue; }
                            int start = i; while (i < raw.Length && !Collapsible(raw[i])) i++;
                            string word = raw.Substring(start, i - start);
                            InlineRun spaceRun = pendingSpaceRun ?? run;
                            double space = pendingSpace && currentWidth > 0 ? Measure(" ", spaceRun.Style).Advance : 0;
                            double wordWidth = Measure(word, run.Style).Advance;
                            bool wrap = run.Style.WhiteSpace == "normal";
                            if (wrap && currentWidth > 0 && currentWidth + space + wordWidth > width) { lines.Add(new List<TextSegment>()); currentWidth = 0; space = 0; }
                            if (space > 0) AddText(lines[lines.Count - 1], spaceRun, " ", ref currentWidth);
                            if (!wrap || wordWidth <= width) AddText(lines[lines.Count - 1], run, word, ref currentWidth);
                            else
                            {
                                int offset = 0;
                                while (offset < word.Length)
                                {
                                    int units = ScalarUnits(word, offset); string scalar = word.Substring(offset, units); double scalarWidth = Measure(scalar, run.Style).Advance;
                                    if (currentWidth > 0 && currentWidth + scalarWidth > width) { lines.Add(new List<TextSegment>()); currentWidth = 0; }
                                    AddText(lines[lines.Count - 1], run, scalar, ref currentWidth); offset += units;
                                }
                            }
                            pendingSpace = false;
                        }
                    }
                }
                double y = 0;
                for (int l = 0; l < lines.Count; l++)
                {
                    double advance = 0, cap = box.Style.Font, line = box.Style.Font * box.Style.Line;
                    for (int j = 0; j < lines[l].Count; j++) { advance += lines[l][j].Metrics.Advance; cap = Math.Max(cap, lines[l][j].Metrics.CapHeight); line = Math.Max(line, lines[l][j].Metrics.LineAdvance); }
                    double x = box.Style.TextAlign == "center" ? Math.Max(0, width - advance) / 2 : box.Style.TextAlign == "right" ? Math.Max(0, width - advance) : 0;
                    for (int j = 0; j < lines[l].Count; j++)
                    {
                        TextSegment segment = lines[l][j]; segment.Line = l; segment.X = x; segment.Y = y + cap - segment.Metrics.CapHeight; segment.Chunk = j;
                        box.Text.Add(segment); x += segment.Metrics.Advance;
                    }
                    // Empty collapsed HTML whitespace does not make an extra visual line.
                    if (lines[l].Count > 0 || l < lines.Count - 1 || (runs.Count > 0 && runs[0].Style.WhiteSpace == "pre")) y += line;
                }
                return y;
            }
            private void AddText(List<TextSegment> line, InlineRun run, string value, ref double currentWidth)
            {
                for (int start = 0; start < value.Length;)
                {
                    int count = Math.Min(64, value.Length - start);
                    if (count < value.Length - start && char.IsHighSurrogate(value[start + count - 1]) && char.IsLowSurrogate(value[start + count])) count--;
                    string chunk = value.Substring(start, count); var measured = Measure(chunk, run.Style);
                    TextSegment last = line.Count > 0 ? line[line.Count - 1] : null;
                    if (last != null && last.Node == run.Node && last.Style == run.Style && last.Text.Length + chunk.Length <= 64)
                    {
                        double oldAdvance = last.Metrics.Advance; last.Text += chunk; last.Metrics = Measure(last.Text, run.Style); currentWidth += last.Metrics.Advance - oldAdvance;
                    }
                    else { line.Add(new TextSegment { Node = run.Node, Style = run.Style, Text = chunk, Metrics = measured }); currentWidth += measured.Advance; }
                    start += count;
                }
            }
            private HtmlTextMeasurement Measure(string text, HtmlBoxStyle style)
            {
                string cacheKey = style.Font.ToString("R", CultureInfo.InvariantCulture) + ":" + style.Line.ToString("R", CultureInfo.InvariantCulture) + ":" + text;
                HtmlTextMeasurement cached; if (_metricCache.TryGetValue(cacheKey, out cached)) return cached;
                var result = new HtmlTextMeasurement { CapHeight = style.Font, LineAdvance = style.Font * style.Line };
                for (int start = 0; start < text.Length;)
                {
                    int count = Math.Min(64, text.Length - start);
                    if (count < text.Length - start && char.IsHighSurrogate(text[start + count - 1]) && char.IsLowSurrogate(text[start + count])) count--;
                    if ((_metricWork += count) > 1048576) throw new ArgumentException("HTML font measurement exceeded its bounded work budget.");
                    HtmlTextMeasurement part = _metrics.Measure(text.Substring(start, count), style.Font, style.Line);
                    if (!HtmlLayoutCss.Finite(part.Advance) || part.Advance < 0 || !HtmlLayoutCss.Finite(part.CapHeight) || part.CapHeight <= 0 || !HtmlLayoutCss.Finite(part.LineAdvance) || part.LineAdvance < part.CapHeight) throw new ArgumentException("Invalid measured font metrics.");
                    if (result.Generation != 0 && part.Generation != result.Generation) throw new ArgumentException("Font generation changed during layout.");
                    if (_fontGeneration != int.MinValue && part.Generation != _fontGeneration) throw new ArgumentException("Font generation changed during layout.");
                    _fontGeneration = part.Generation;
                    result.Generation = part.Generation; result.CapHeight = Math.Max(result.CapHeight, part.CapHeight); result.LineAdvance = Math.Max(result.LineAdvance, part.LineAdvance);
                    if (part.HasInk)
                    {
                        HtmlRect ink = new HtmlRect(result.Advance + part.Ink.X, part.Ink.Y, part.Ink.Width, part.Ink.Height);
                        HtmlPaintValidation.Rect(ink, false);
                        result.Ink = result.HasInk ? Union(result.Ink, ink) : ink; result.HasInk = true;
                    }
                    result.Advance += part.Advance; start += count;
                }
                if (_metricCache.Count < 4096 && text.Length <= 256) _metricCache[cacheKey] = result;
                return result;
            }
            private static int ScalarUnits(string text, int index) { return char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1; }
            private static bool Collapsible(char ch) { return ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r' || ch == '\f'; }
            private double RangeHeight(LayoutBox box, double width)
            {
                string type; if (!box.Node.Attributes.TryGetValue("type", out type) || !string.Equals(type, "range", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only input type=range is supported.");
                return Math.Max(20, box.Style.Font * box.Style.Line);
            }
            internal void Paint(LayoutBox box, HtmlRect inheritedClip)
            {
                if (box.Hidden || box.Consumed || box.Style.Opacity == 0 || inheritedClip.Empty) return;
                HtmlBoxStyle s = box.Style; string identity = Identity(box.Node);
                var bounds = new HtmlRect(box.X, box.Y, box.Width, box.Height);
                if (!box.TextGroup)
                {
                    AddRect(box.Node, "background", bounds, s.Background, inheritedClip, s.Radius, s.Opacity);
                    double[] widths = s.Border;
                    HtmlRect[] borders = { new HtmlRect(box.X, box.Y, box.Width, widths[0]), new HtmlRect(box.X + box.Width - widths[1], box.Y + widths[0], widths[1], Math.Max(0, box.Height - widths[0] - widths[2])), new HtmlRect(box.X, box.Y + box.Height - widths[2], box.Width, widths[2]), new HtmlRect(box.X, box.Y + widths[0], widths[3], Math.Max(0, box.Height - widths[0] - widths[2])) };
                    for (int i = 0; i < 4; i++) AddRect(box.Node, "border" + i, borders[i], s.BorderColor[i], inheritedClip, 0, s.Opacity);
                }
                HtmlRect contentClip = inheritedClip;
                if (s.Overflow == "hidden") contentClip = Intersect(inheritedClip, new HtmlRect(box.X + s.Border[3], box.Y + s.Border[0], Math.Max(0, box.Width - s.Border[1] - s.Border[3]), Math.Max(0, box.Height - s.Border[0] - s.Border[2])));
                for (int i = 0; i < box.Text.Count; i++)
                {
                    TextSegment text = box.Text[i];
                    if (!text.Metrics.HasInk || text.Text.Length == 0) continue;
                    double x = box.ContentX + text.X, y = box.ContentY + text.Y;
                    HtmlRect ink = new HtmlRect(x + text.Metrics.Ink.X, y + text.Metrics.Ink.Y, text.Metrics.Ink.Width, text.Metrics.Ink.Height);
                    if (Intersect(ink, contentClip).Empty) continue;
                    var operation = new HtmlPaintOperation { Key = Key(text.Node, "text-" + text.Line + "-" + text.Chunk), NodeId = Identity(text.Node), Kind = "text", Text = text.Text, Bounds = new HtmlRect(x, y, text.Metrics.Advance, text.Metrics.CapHeight), Color = Alpha(text.Style.Color, text.Style.Opacity), FontSize = text.Style.Font, LineHeight = text.Style.Line, HasClip = true, Clip = contentClip };
                    Add(operation);
                }
                if (box.Node.Tag == "img" || box.Node.Tag == "svg")
                {
                    string asset;
                    if (!box.Node.Attributes.TryGetValue("src", out asset) && !box.Node.Attributes.TryGetValue("data-asset", out asset)) throw new ArgumentException("Images require a registered asset key.");
                    if (string.IsNullOrWhiteSpace(asset) || asset.IndexOf("://", StringComparison.Ordinal) >= 0 || asset.IndexOf('\\') >= 0 || asset.IndexOf("..", StringComparison.Ordinal) >= 0) throw new ArgumentException("Only registered asset keys are accepted.");
                    Add(new HtmlPaintOperation { Key = Key(box.Node, "asset"), NodeId = identity, Kind = box.Node.Tag == "svg" ? "svg" : "image", Asset = asset, Bounds = new HtmlRect(box.ContentX, box.ContentY, box.ContentWidth, box.ContentHeight), Color = Alpha(new HtmlColor(1, 1, 1, 1), s.Opacity), HasClip = true, Clip = contentClip });
                }
                if (box.Node.Tag == "input") PaintRange(box, contentClip);
                else if (box.Node.Tag == "button") Hit(box, "button", Intersect(bounds, contentClip), 0, 0, 0, 0);
                for (int i = 0; i < box.Children.Count; i++) Paint(box.Children[i], contentClip);
            }
            private void PaintRange(LayoutBox box, HtmlRect clip)
            {
                HtmlNode node = box.Node; string raw;
                double min = AttributeNumber(node, "min", 0), max = AttributeNumber(node, "max", 100), step = AttributeNumber(node, "step", 1);
                if (min >= max || step < 0 || min < -1e12 || max > 1e12) throw new ArgumentException("Invalid range min/max/step.");
                if (step > 0 && !HtmlLayoutCss.Finite((max - min) / step)) throw new ArgumentException("Range step is too small for finite canonical quantization.");
                double value = AttributeNumber(node, "value", min);
                string binding; if (node.Attributes.TryGetValue("data-bind", out binding) && _data != null && _data.TryGetValue(binding, out raw)) value = HtmlLayoutCss.Number(raw, "bound range value", -1e12, 1e12);
                value = Math.Max(min, Math.Min(max, value));
                if (step > 0 && value != max) value = Math.Max(min, Math.Min(max, min + Math.Floor((value - min) / step + 0.5) * step));
                double knob = Math.Min(Math.Max(12, box.Style.Font), Math.Min(box.ContentHeight, box.ContentWidth));
                double travel = Math.Max(0, box.ContentWidth - knob), ratio = (value - min) / (max - min);
                double y = box.ContentY + box.ContentHeight / 2;
                AddRect(node, "range-track", new HtmlRect(box.ContentX + knob / 2, y - 2, travel, 4), new HtmlColor(0.35, 0.35, 0.35, 1), clip, 0, box.Style.Opacity);
                AddRect(node, "range-fill", new HtmlRect(box.ContentX + knob / 2, y - 2, travel * ratio, 4), box.Style.Color, clip, 0, box.Style.Opacity);
                AddRect(node, "range-handle", new HtmlRect(box.ContentX + travel * ratio, y - knob / 2, knob, knob), box.Style.Color, clip, Math.Min(box.Style.Radius, knob / 2), box.Style.Opacity);
                Hit(box, "range", Intersect(new HtmlRect(box.ContentX, box.ContentY, box.ContentWidth, box.ContentHeight), clip), value, min, max, step);
            }
            private static double AttributeNumber(HtmlNode node, string name, double fallback)
            {
                string raw; if (!node.Attributes.TryGetValue(name, out raw)) return fallback;
                if (name == "step" && raw == "any") return 0;
                return HtmlLayoutCss.Number(raw, "range " + name, -1e12, 1e12);
            }
            private void Hit(LayoutBox box, string kind, HtmlRect bounds, double value, double min, double max, double step)
            {
                if (bounds.Empty || box.Style.Opacity == 0) return;
                string disabled; if (box.Node.Attributes.TryGetValue("disabled", out disabled)) return;
                string binding, action; box.Node.Attributes.TryGetValue("data-bind", out binding); box.Node.Attributes.TryGetValue("data-action", out action);
                if (Frame.Hits.Count >= _limits.MaxHitRegions) throw new ArgumentException("HTML hit region limit exceeded.");
                Frame.Hits.Add(new HtmlHitRegion { NodeId = Identity(box.Node), Kind = kind, Binding = binding, Action = action, Bounds = bounds, Order = Frame.Operations.Count, Value = value, Minimum = min, Maximum = max, Step = step });
            }
            private void AddRect(HtmlNode node, string purpose, HtmlRect bounds, HtmlColor color, HtmlRect clip, double radius, double opacity)
            {
                if (bounds.Empty || color.A == 0 || Intersect(bounds, clip).Empty) return;
                Add(new HtmlPaintOperation { Key = Key(node, purpose), NodeId = Identity(node), Kind = "rect", Bounds = bounds, Color = Alpha(color, opacity), Radius = radius, HasClip = true, Clip = clip });
            }
            private void Add(HtmlPaintOperation operation)
            {
                if (Frame.Operations.Count >= _limits.MaxPaintOperations) throw new ArgumentException("HTML paint operation limit exceeded.");
                operation.Order = Frame.Operations.Count; Frame.Operations.Add(operation);
            }
            private static string Key(HtmlNode node, string purpose) { return "html:" + node.Ordinal.ToString(CultureInfo.InvariantCulture) + ":" + purpose; }
            private static string Identity(HtmlNode node)
            {
                HtmlNode actual = node; while (actual != null) { if (!string.IsNullOrEmpty(actual.Id)) return actual.Id; if (node.Tag != "#text") break; actual = actual.Parent; }
                return "node-" + node.Ordinal.ToString(CultureInfo.InvariantCulture);
            }
            private static void CheckSize(LayoutBox box)
            {
                HtmlPaintValidation.Rect(new HtmlRect(box.X, box.Y, box.Width, box.Height), false);
                if (box.Width > 8192 || box.Height > 8192) throw new ArgumentException("HTML box exceeds 8192 logical pixels.");
            }
            private static HtmlColor Alpha(HtmlColor color, double opacity) { return new HtmlColor(color.R, color.G, color.B, color.A * opacity); }
            private static string ColorText(HtmlColor c) { return "rgba(" + (c.R * 255).ToString("R", CultureInfo.InvariantCulture) + "," + (c.G * 255).ToString("R", CultureInfo.InvariantCulture) + "," + (c.B * 255).ToString("R", CultureInfo.InvariantCulture) + "," + c.A.ToString("R", CultureInfo.InvariantCulture) + ")"; }
            private static HtmlRect Intersect(HtmlRect a, HtmlRect b) { double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y); return new HtmlRect(x, y, Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - x), Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - y)); }
            private static HtmlRect Union(HtmlRect a, HtmlRect b) { double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y); return new HtmlRect(x, y, Math.Max(a.X + a.Width, b.X + b.Width) - x, Math.Max(a.Y + a.Height, b.Y + b.Height) - y); }
        }
    }
}
