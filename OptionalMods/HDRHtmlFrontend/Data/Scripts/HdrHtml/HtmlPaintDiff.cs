using System;
using System.Collections.Generic;

namespace Hdr.Html
{
    /// <summary>Exact paint comparison; revisions alone do not certify unchanged public mutable frames.</summary>
    public static class HtmlPaintDiff
    {
        public static bool Equal(HtmlPaintFrame a, HtmlPaintFrame b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Width != b.Width || a.Height != b.Height || a.FontProfile != b.FontProfile || a.Operations.Count != b.Operations.Count || a.SourceRegions.Count != b.SourceRegions.Count) return false;
            for (int i = 0; i < a.Operations.Count; i++) if (!Equal(a.Operations[i], b.Operations[i])) return false;
            for (int i = 0; i < a.SourceRegions.Count; i++) if (!Equal(a.SourceRegions[i], b.SourceRegions[i])) return false;
            return true;
        }
        static bool Equal(HtmlPaintOperation a, HtmlPaintOperation b)
        {
            return a != null && b != null && a.Key == b.Key && a.NodeId == b.NodeId && a.Kind == b.Kind && a.Text == b.Text && a.Asset == b.Asset && a.Order == b.Order && Rect(a.Bounds,b.Bounds) && Rect(a.Clip,b.Clip) && a.HasClip == b.HasClip && Color(a.Color,b.Color) && Color(a.Stroke,b.Stroke) && a.StrokeWidth == b.StrokeWidth && a.Radius == b.Radius && a.FontSize == b.FontSize && a.LineHeight == b.LineHeight;
        }
        static bool Rect(HtmlRect a, HtmlRect b) { return a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height; }
        static bool Equal(HtmlSourceRegion a, HtmlSourceRegion b)
        {
            return a != null && b != null && a.NodeId == b.NodeId && Rect(a.Bounds,b.Bounds) && Rect(a.Clip,b.Clip) && a.Opacity == b.Opacity && a.Order == b.Order && a.Visible == b.Visible;
        }
        static bool Color(HtmlColor a, HtmlColor b) { return a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A; }
        public static HtmlPaintFrame Snapshot(HtmlPaintFrame frame)
        {
            if (frame == null) return null;
            var copy = new HtmlPaintFrame { Revision = frame.Revision, Width = frame.Width, Height = frame.Height, FontProfile = frame.FontProfile };
            foreach (var p in frame.Operations) copy.Operations.Add(new HtmlPaintOperation { Key=p.Key,NodeId=p.NodeId,Kind=p.Kind,Text=p.Text,Asset=p.Asset,Order=p.Order,Bounds=p.Bounds,Clip=p.Clip,HasClip=p.HasClip,Color=p.Color,Stroke=p.Stroke,StrokeWidth=p.StrokeWidth,Radius=p.Radius,FontSize=p.FontSize,LineHeight=p.LineHeight });
            foreach (var h in frame.Hits) copy.Hits.Add(new HtmlHitRegion { NodeId=h.NodeId,Kind=h.Kind,Binding=h.Binding,Action=h.Action,Order=h.Order,Bounds=h.Bounds,Value=h.Value,Minimum=h.Minimum,Maximum=h.Maximum,Step=h.Step });
            foreach (var s in frame.SourceRegions) copy.SourceRegions.Add(new HtmlSourceRegion { NodeId=s.NodeId,Bounds=s.Bounds,Clip=s.Clip,Opacity=s.Opacity,Order=s.Order,Visible=s.Visible });
            return copy;
        }
    }
}
