using System;
using System.Collections.Generic;

namespace Hdr.Html
{
    public static class HtmlPaintValidation
    {
        internal static void Limits(HtmlLimits limits)
        {
            if (limits == null || limits.MaxPaintOperations < 1 || limits.MaxPaintOperations > 512 || limits.MaxHitRegions < 0 || limits.MaxHitRegions > 64 || limits.MaxNodes < 1 || limits.MaxNodes > 512 || limits.MaxDepth < 1 || limits.MaxDepth > 32 || limits.MaxTextCharacters < 1 || limits.MaxTextCharacters > 16384) throw new ArgumentException("Layout profile ceilings are 512 nodes/paints, 64 hits, depth32 and 16384 expanded text characters.");
        }
        public static void Dimensions(double width, double height)
        {
            if (!HtmlLayoutCss.Finite(width) || !HtmlLayoutCss.Finite(height) || width < 1 || height < 1 || width > 8192 || height > 8192) throw new ArgumentException("Logical surface dimensions must be 1..8192 pixels.");
        }
        internal static void Rect(HtmlRect rect, bool positive)
        {
            if (!HtmlLayoutCss.Finite(rect.X) || !HtmlLayoutCss.Finite(rect.Y) || !HtmlLayoutCss.Finite(rect.Width) || !HtmlLayoutCss.Finite(rect.Height) || rect.Width < 0 || rect.Height < 0 || Math.Abs(rect.X) > 1e6 || Math.Abs(rect.Y) > 1e6 || rect.Width > 1e6 || rect.Height > 1e6 || (positive && rect.Empty)) throw new ArgumentException("Invalid paint rectangle.");
        }
        internal static void Text(string text, int maximum)
        {
            if (text == null || text.Length > maximum) throw new ArgumentException("Text exceeds its bounded admission limit.");
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch < 32 && ch != '\t' && ch != '\n' && ch != '\r') throw new ArgumentException("Text contains an unsupported control character.");
                if (char.IsHighSurrogate(ch)) { if (i + 1 >= text.Length || !char.IsLowSurrogate(text[++i])) throw new ArgumentException("Text contains an unpaired surrogate."); }
                else if (char.IsLowSurrogate(ch)) throw new ArgumentException("Text contains an unpaired surrogate.");
            }
        }
        private static void Color(HtmlColor color)
        {
            if (!HtmlLayoutCss.Finite(color.R) || !HtmlLayoutCss.Finite(color.G) || !HtmlLayoutCss.Finite(color.B) || !HtmlLayoutCss.Finite(color.A) || color.R < 0 || color.R > 1 || color.G < 0 || color.G > 1 || color.B < 0 || color.B > 1 || color.A < 0 || color.A > 1) throw new ArgumentException("Invalid paint color.");
        }
        public static void Validate(HtmlPaintFrame frame, HtmlLimits limits)
        {
            if (frame == null || limits == null) throw new ArgumentException("Frame and limits are required.");
            Limits(limits);
            Dimensions(frame.Width, frame.Height);
            if (frame.Operations.Count > limits.MaxPaintOperations || frame.Hits.Count > limits.MaxHitRegions || limits.MaxPaintOperations < 0 || limits.MaxHitRegions < 0) throw new ArgumentException("HTML paint/hit limit exceeded.");
            var keys = new HashSet<string>(StringComparer.Ordinal); var hitIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < frame.Operations.Count; i++)
            {
                HtmlPaintOperation operation = frame.Operations[i];
                if (operation == null || string.IsNullOrEmpty(operation.Key) || !keys.Add(operation.Key) || operation.Order != i) throw new ArgumentException("Paint keys and order must be unique and stable.");
                if (operation.Kind != "rect" && operation.Kind != "text" && operation.Kind != "image" && operation.Kind != "svg") throw new ArgumentException("Unsupported paint operation.");
                Rect(operation.Bounds, false); Color(operation.Color);
                if (operation.HasClip) Rect(operation.Clip, false);
                if (!HtmlLayoutCss.Finite(operation.Radius) || operation.Radius < 0 || !HtmlLayoutCss.Finite(operation.StrokeWidth) || operation.StrokeWidth < 0) throw new ArgumentException("Invalid paint decoration.");
                if (operation.Kind == "text")
                {
                    if (operation.Text == null || operation.Text.Length > 64 || operation.Text.IndexOf('\n') >= 0 || operation.Text.IndexOf('\r') >= 0 || !HtmlLayoutCss.Finite(operation.FontSize) || operation.FontSize <= 0 || !HtmlLayoutCss.Finite(operation.LineHeight) || operation.LineHeight < 1 || operation.LineHeight > 4) throw new ArgumentException("Invalid bounded text run.");
                    Text(operation.Text, 64);
                    if (operation.Text.Length > 0 && (char.IsHighSurrogate(operation.Text[operation.Text.Length - 1]) || char.IsLowSurrogate(operation.Text[0]))) throw new ArgumentException("Paint text cannot split a surrogate pair.");
                }
                else if ((operation.Kind == "image" || operation.Kind == "svg") && string.IsNullOrWhiteSpace(operation.Asset)) throw new ArgumentException("Registered asset key required.");
            }
            for (int i = 0; i < frame.Hits.Count; i++)
            {
                HtmlHitRegion hit = frame.Hits[i];
                if (hit == null || string.IsNullOrEmpty(hit.NodeId) || !hitIds.Add(hit.NodeId) || (hit.Kind != "button" && hit.Kind != "range") || hit.Order < 0 || hit.Order > frame.Operations.Count) throw new ArgumentException("Invalid hit region identity/order.");
                Rect(hit.Bounds, true);
                if (hit.Bounds.X < 0 || hit.Bounds.Y < 0 || hit.Bounds.X + hit.Bounds.Width > frame.Width + 1e-8 || hit.Bounds.Y + hit.Bounds.Height > frame.Height + 1e-8) throw new ArgumentException("Hit region escaped the visual surface clip.");
                if (!HtmlLayoutCss.Finite(hit.Value) || !HtmlLayoutCss.Finite(hit.Minimum) || !HtmlLayoutCss.Finite(hit.Maximum) || !HtmlLayoutCss.Finite(hit.Step) || hit.Step < 0 || hit.Value < hit.Minimum || hit.Value > hit.Maximum || (hit.Kind == "range" && hit.Minimum >= hit.Maximum)) throw new ArgumentException("Invalid canonical range value.");
            }
        }
    }
}
