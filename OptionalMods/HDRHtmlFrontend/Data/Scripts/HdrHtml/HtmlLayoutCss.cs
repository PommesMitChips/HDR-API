using System;
using System.Collections.Generic;
using System.Globalization;

namespace Hdr.Html
{
    // Profile1 uses HDR Inter cap-height pixels, not browser CSS em sizing.
    internal sealed class HtmlBoxStyle
    {
        internal Dictionary<string, string> Values;
        internal string Display, Direction, Align, Justify, Self, WhiteSpace, TextAlign, Overflow, FontFamily;
        internal double Font, FontReference, Line, Opacity, Grow, Radius, Gap, RowGap, ColumnGap;
        internal double[] Margin = new double[4], Padding = new double[4], Border = new double[4];
        internal HtmlColor Color, Background;
        internal HtmlColor[] BorderColor = new HtmlColor[4];
    }

    internal static class HtmlLayoutCss
    {
        private static readonly HashSet<string> Properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "display", "width", "height", "min-width", "max-width", "min-height", "max-height",
            "margin-top", "margin-right", "margin-bottom", "margin-left", "padding-top", "padding-right", "padding-bottom", "padding-left",
            "border-width", "border-color", "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
            "border-top-color", "border-right-color", "border-bottom-color", "border-left-color", "border-radius",
            "color", "background-color", "font-size", "font-family", "line-height", "text-align", "white-space", "opacity", "overflow",
            "flex-direction", "flex-grow", "align-items", "align-self", "justify-content", "gap", "row-gap", "column-gap", "flex-wrap"
        };
        private static readonly string[] Sides = { "top", "right", "bottom", "left" };
        private static readonly Dictionary<string, string> NamedColors = new Dictionary<string, string> { { "white", "#ffffff" }, { "black", "#000000" }, { "red", "#ff0000" }, { "green", "#008000" }, { "blue", "#0000ff" }, { "gray", "#808080" }, { "grey", "#808080" }, { "yellow", "#ffff00" }, { "cyan", "#00ffff" }, { "magenta", "#ff00ff" } };
        internal static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
        internal static double Number(string value, string name, double min, double max)
        {
            double result;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) || !Finite(result) || result < min || result > max)
                throw new ArgumentException("Unsupported " + name + ": " + value);
            return result;
        }
        internal static double Length(string value, string name, double reference, bool allowPercent)
        {
            value = value.Trim().ToLowerInvariant();
            if (value == "0") return 0;
            if (value.EndsWith("px", StringComparison.Ordinal)) return Number(value.Substring(0, value.Length - 2), name, 0, 8192);
            if (allowPercent && value.EndsWith("%", StringComparison.Ordinal)) return Number(value.Substring(0, value.Length - 1), name, 0, 10000) * reference / 100;
            throw new ArgumentException("Unsupported " + name + " length/unit: " + value + ". Use nonnegative px or percent.");
        }
        internal static double Dimension(Dictionary<string, string> values, string property, double reference, double automatic)
        {
            string raw;
            if (!values.TryGetValue(property, out raw)) return automatic;
            raw = raw.Trim().ToLowerInvariant(); if (raw == "auto") return automatic;
            return Length(raw, property, reference, true);
        }
        internal static string Get(Dictionary<string, string> values, string name, string fallback)
        {
            string value; return values.TryGetValue(name, out value) ? value.Trim().ToLowerInvariant() : fallback;
        }
        private static string Choice(Dictionary<string, string> values, string property, string fallback, params string[] choices)
        {
            string value = Get(values, property, fallback);
            for (int i = 0; i < choices.Length; i++) if (value == choices[i]) return value;
            throw new ArgumentException("Unsupported " + property + ": " + value);
        }
        internal static HtmlBoxStyle Read(HtmlNode node, Dictionary<string, string> values, double width, double inheritedFont = 16)
        {
            foreach (KeyValuePair<string, string> property in values)
                if (!Properties.Contains(property.Key)) throw new ArgumentException("Unsupported CSS property: " + property.Key);
            var style = new HtmlBoxStyle(); style.Values = values;
            bool inline = node.Tag == "#text" || node.Tag == "span" || node.Tag == "strong" || node.Tag == "em" || node.Tag == "br";
            style.Display = Choice(values, "display", inline ? "inline" : "block", "none", "block", "flex", "inline");
            if (style.Display == "inline" && !inline) throw new ArgumentException("Inline layout is limited to text, span, strong and em.");
            style.Direction = Choice(values, "flex-direction", "row", "row", "column");
            style.Align = Choice(values, "align-items", "stretch", "stretch", "flex-start", "flex-end", "center");
            style.Justify = Choice(values, "justify-content", "flex-start", "flex-start", "flex-end", "center", "space-between", "space-around", "space-evenly");
            style.Self = Choice(values, "align-self", "auto", "auto", "stretch", "flex-start", "flex-end", "center");
            Choice(values, "flex-wrap", "nowrap", "nowrap");
            style.WhiteSpace = Choice(values, "white-space", "normal", "normal", "nowrap", "pre");
            style.TextAlign = Choice(values, "text-align", "left", "left", "right", "center");
            style.Overflow = Choice(values, "overflow", "visible", "visible", "hidden");
            string font = Get(values, "font-family", "inter").Trim('"', '\'');
            if (font != "inter" && font != "hdr inter" && font != "sans-serif" && font != "debug") throw new ArgumentException("Profile1 supports only the explicitly selected measured Inter or Debug font.");
            style.FontFamily = font;
            style.FontReference = inheritedFont;
            if (Get(values, "font-size", "") == "auto") throw new ArgumentException("font-size:auto is not supported.");
            style.Font = Dimension(values, "font-size", inheritedFont, node.Tag == "h1" ? 32 : node.Tag == "h2" ? 24 : node.Tag == "h3" ? 20 : 16);
            if (style.Font <= 0) throw new ArgumentException("font-size must be positive cap-height pixels.");
            style.Line = Number(Get(values, "line-height", "1.3"), "line-height", 1, 4);
            style.Opacity = Number(Get(values, "opacity", "1"), "opacity", 0, 1);
            style.Grow = Number(Get(values, "flex-grow", "0"), "flex-grow", 0, 10000);
            style.Gap = Length(Get(values, "gap", "0"), "gap", width, true);
            style.RowGap = Length(Get(values, "row-gap", Get(values, "gap", "0")), "row-gap", width, true);
            style.ColumnGap = Length(Get(values, "column-gap", Get(values, "gap", "0")), "column-gap", width, true);
            style.Radius = Length(Get(values, "border-radius", "0"), "border-radius", width, false);
            style.Color = Color(Get(values, "color", "#ffffff"));
            style.Background = Color(Get(values, "background-color", "transparent"));
            double border = Length(Get(values, "border-width", "0"), "border-width", width, false);
            HtmlColor borderColor = Color(Get(values, "border-color", Get(values, "color", "#ffffff")));
            for (int i = 0; i < 4; i++)
            {
                style.Margin[i] = Length(Get(values, "margin-" + Sides[i], DefaultMargin(node.Tag, i)), "margin-" + Sides[i], width, true);
                style.Padding[i] = Length(Get(values, "padding-" + Sides[i], "0"), "padding-" + Sides[i], width, true);
                style.Border[i] = Length(Get(values, "border-" + Sides[i] + "-width", border.ToString("R", CultureInfo.InvariantCulture) + "px"), "border-width", width, false);
                style.BorderColor[i] = values.ContainsKey("border-" + Sides[i] + "-color") ? Color(values["border-" + Sides[i] + "-color"]) : borderColor;
            }
            if (style.Opacity != 1 && node.Children.Count > 0)
                throw new ArgumentException("Non-leaf opacity needs group compositing, which Profile1 does not support.");
            if (style.Display == "inline")
            {
                for (int i = 0; i < 4; i++) if (style.Margin[i] != 0 || style.Padding[i] != 0 || style.Border[i] != 0) throw new ArgumentException("Inline box decorations are not supported.");
                if (style.Background.A != 0 || style.Radius != 0 || values.ContainsKey("width") || values.ContainsKey("height")) throw new ArgumentException("Inline box dimensions/backgrounds are not supported.");
            }
            // Validate authored dimensions even when another layout branch will not use them.
            string[] dimensions = { "width", "height", "min-width", "max-width", "min-height", "max-height" };
            for (int i = 0; i < dimensions.Length; i++)
            {
                if (i >= 2 && Get(values, dimensions[i], "") == "auto") throw new ArgumentException("Auto min/max dimensions are not supported.");
                Dimension(values, dimensions[i], width, 0);
            }
            return style;
        }
        private static string DefaultMargin(string tag, int side)
        {
            if (side != 0 && side != 2) return "0";
            if (tag == "h1") return "16px";
            if (tag == "h2") return "12px";
            if (tag == "h3") return "10px";
            if (tag == "p") return "8px";
            return "0";
        }
        internal static HtmlColor Color(string value)
        {
            value = value.Trim().ToLowerInvariant();
            if (value == "transparent") return new HtmlColor(0, 0, 0, 0);
            string mapped; if (NamedColors.TryGetValue(value, out mapped)) value = mapped;
            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                string hex = value.Substring(1);
                if (hex.Length == 3 || hex.Length == 4)
                {
                    string full = ""; for (int i = 0; i < hex.Length; i++) full += new string(hex[i], 2); hex = full;
                }
                uint packed;
                if ((hex.Length == 6 || hex.Length == 8) && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out packed))
                {
                    if (hex.Length == 6) packed = (packed << 8) | 255;
                    return new HtmlColor((packed >> 24) / 255.0, ((packed >> 16) & 255) / 255.0, ((packed >> 8) & 255) / 255.0, (packed & 255) / 255.0);
                }
            }
            bool rgba = value.StartsWith("rgba(", StringComparison.Ordinal), rgb = value.StartsWith("rgb(", StringComparison.Ordinal);
            if ((rgba || rgb) && value.EndsWith(")", StringComparison.Ordinal))
            {
                int start = rgba ? 5 : 4; string[] parts = value.Substring(start, value.Length - start - 1).Split(',');
                if (parts.Length == (rgba ? 4 : 3)) return new HtmlColor(Number(parts[0].Trim(), "color", 0, 255) / 255, Number(parts[1].Trim(), "color", 0, 255) / 255, Number(parts[2].Trim(), "color", 0, 255) / 255, rgba ? Number(parts[3].Trim(), "alpha", 0, 1) : 1);
            }
            throw new ArgumentException("Unsupported CSS color: " + value);
        }
    }
}
