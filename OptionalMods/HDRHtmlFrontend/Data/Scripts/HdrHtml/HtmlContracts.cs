using System;
using System.Collections.Generic;

namespace Hdr.Html
{
    // Internal source contracts for the prototype, not a cross-mod delegate ABI.
    public sealed class HtmlLimits
    {
        public int MaxCharacters = 131072, MaxNodes = 512, MaxDepth = 32;
        public int MaxRules = 256, MaxDeclarations = 2048, MaxTextCharacters = 16384;
        public int MaxPaintOperations = 512, MaxHitRegions = 64;
    }

    public sealed class HtmlNode
    {
        public int Ordinal;
        public string Tag, Id, Text;
        public HtmlNode Parent;
        public readonly Dictionary<string, string> Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> InlineStyle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Classes = new List<string>();
        public readonly List<HtmlNode> Children = new List<HtmlNode>();
    }

    public sealed class HtmlCssRule
    {
        public string Selector;
        public int Specificity, Order;
        public readonly Dictionary<string, string> Declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class HtmlDocument
    {
        public const string Profile = "HDR.HTML/Profile1";
        public HtmlNode Root;
        public readonly List<HtmlCssRule> Rules = new List<HtmlCssRule>();
        public readonly Dictionary<string, HtmlNode> ById = new Dictionary<string, HtmlNode>(StringComparer.Ordinal);
    }

    public struct HtmlColor
    {
        public double R, G, B, A;
        public HtmlColor(double r, double g, double b, double a) { R = r; G = g; B = b; A = a; }
    }

    public struct HtmlRect
    {
        public double X, Y, Width, Height;
        public HtmlRect(double x, double y, double width, double height) { X = x; Y = y; Width = width; Height = height; }
        public bool Empty { get { return Width <= 0 || Height <= 0; } }
    }

    public struct HtmlTextMeasurement
    {
        public double Advance, CapHeight, LineAdvance;
        public HtmlRect Ink;
        public bool HasInk;
        public int Generation;
    }

    public interface IHtmlTextMetrics
    {
        string Profile { get; }
        HtmlTextMeasurement Measure(string text, double size, double lineHeight);
    }

    public sealed class HtmlPaintOperation
    {
        public string Key, NodeId, Kind, Text, Asset;
        public int Order;
        public HtmlRect Bounds, Clip;
        public bool HasClip;
        public HtmlColor Color, Stroke;
        public double StrokeWidth, Radius, FontSize, LineHeight;
    }

    public sealed class HtmlHitRegion
    {
        public string NodeId, Kind, Binding, Action;
        public int Order;
        public HtmlRect Bounds;
        public double Value, Minimum, Maximum, Step;
    }

    public sealed class HtmlPaintFrame
    {
        public long Revision;
        public double Width, Height;
        public string FontProfile;
        public readonly List<HtmlPaintOperation> Operations = new List<HtmlPaintOperation>();
        public readonly List<HtmlHitRegion> Hits = new List<HtmlHitRegion>();
    }
}
