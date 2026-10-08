using System;
using System.Globalization;
using System.Text;

namespace Hdr.Html
{
    internal static class HtmlSvgEncoding
    {
        internal static string Number(double value){return value.ToString("R",CultureInfo.InvariantCulture);}
        internal static string Escape(string text){return text.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;").Replace("'","&apos;");}
        internal static string Color(HtmlColor color){return "rgb("+Number(color.R*255)+","+Number(color.G*255)+","+Number(color.B*255)+")";}
        internal static string Begin(HtmlPaintFrame frame){return "<svg viewBox=\"0 0 "+Number(frame.Width)+" "+Number(frame.Height)+"\" xmlns=\"http://www.w3.org/2000/svg\">";}
        internal static string Element(HtmlPaintOperation op,int clipId)
        {
            var b=new StringBuilder();string id="hc"+clipId.ToString(CultureInfo.InvariantCulture);
            if(op.HasClip)
            {
                b.Append("<defs><clipPath id=\"").Append(id).Append("\" clipPathUnits=\"userSpaceOnUse\"><rect x=\"").Append(Number(op.Clip.X)).Append("\" y=\"").Append(Number(op.Clip.Y)).Append("\" width=\"").Append(Number(op.Clip.Width)).Append("\" height=\"").Append(Number(op.Clip.Height)).Append("\"/></clipPath></defs><g clip-path=\"url(#").Append(id).Append(")\">");
            }
            var r=op.Bounds;
            if(op.Kind=="rect")
            {
                b.Append("<rect x=\"").Append(Number(r.X)).Append("\" y=\"").Append(Number(r.Y)).Append("\" width=\"").Append(Number(r.Width)).Append("\" height=\"").Append(Number(r.Height)).Append("\"");
                if(op.Radius>0)b.Append(" rx=\"").Append(Number(Math.Min(op.Radius,Math.Min(r.Width,r.Height)*.5))).Append("\"");
                b.Append(" fill=\"").Append(Color(op.Color)).Append("\" fill-opacity=\"").Append(Number(op.Color.A)).Append("\"");
                if(op.StrokeWidth>0)b.Append(" stroke=\"").Append(Color(op.Stroke)).Append("\" stroke-opacity=\"").Append(Number(op.Stroke.A)).Append("\" stroke-width=\"").Append(Number(op.StrokeWidth)).Append("\"");
                b.Append("/>");
            }
            else if(op.Kind=="text")
            {
                b.Append("<text x=\"").Append(Number(r.X)).Append("\" y=\"").Append(Number(r.Y+op.FontSize)).Append("\" font-family=\"Inter\" font-size=\"").Append(Number(op.FontSize)).Append("\" fill=\"").Append(Color(op.Color)).Append("\" fill-opacity=\"").Append(Number(op.Color.A)).Append("\">").Append(Escape(op.Text)).Append("</text>");
            }
            else throw new ArgumentException("Grouped SVG supports solid rectangles and text; registered image/SVG assets need an explicit asset adapter.");
            if(op.HasClip)b.Append("</g>");
            return b.ToString();
        }
    }
}
