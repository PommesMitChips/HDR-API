using System;
using System.Collections.Generic;
using Hdr.Mods;
using VRageMath;

namespace Hdr.Html
{
    /// <summary>Per-element retained HDR artwork. Logical cap-top Y-down is converted to native HUD or world coordinates.</summary>
    public sealed class HtmlVectorPainter : HtmlRetainedPainter
    {
        public HtmlVectorPainter(HdrModApi api,bool hud,MatrixD worldPose,double unitsPerPixel=1,int contextOrder=0,HtmlPainterLimits limits=null) : this(new HtmlHdrDrawApi(api),hud,worldPose,unitsPerPixel,contextOrder,limits) { }
        public HtmlVectorPainter(IHtmlDrawApi api,bool hud,MatrixD worldPose,double unitsPerPixel=1,int contextOrder=0,HtmlPainterLimits limits=null) : base(api,hud,worldPose,unitsPerPixel,contextOrder,limits) { }
        public override string Backend { get { return IsHud?"HDR retained vector HUD":"HDR retained vector world plane"; } }
        protected override List<HtmlPreparedItem> Prepare(HtmlPaintFrame frame)
        {
            var result=new List<HtmlPreparedItem>();
            foreach(var op in frame.Operations)
            {
                if(!VisiblePaint(op)||op.Bounds.Empty||op.HasClip&&op.Clip.Empty)continue;
                if(op.Kind=="rect"&&op.Radius==0&&op.StrokeWidth==0)
                {var clipped=op.HasClip?Intersection(op.Bounds,op.Clip):op.Bounds;if(!clipped.Empty)result.Add(Quad(op,clipped));}
                else if(op.Kind=="image")
                {
                    var clipped=op.HasClip?Intersection(op.Bounds,op.Clip):op.Bounds;if(!clipped.Empty)result.Add(Quad(op,clipped,op.Asset));
                }
                else if(op.Kind=="text")
                {
                    var measured=Metrics.Measure(op.Text,op.FontSize,op.LineHeight);if(!measured.HasInk)continue;
                    var ink=new HtmlRect(op.Bounds.X+measured.Ink.X,op.Bounds.Y+measured.Ink.Y,measured.Ink.Width,measured.Ink.Height);
                    if(op.HasClip&&Intersection(ink,op.Clip).Empty)continue;
                    if(!op.HasClip||Contains(op.Clip,ink))
                    {
                        double s=UnitsPerPixel;var item=new HtmlPreparedItem {Id=ItemId(op.Key),Kind="text",Order=op.Order,Text=op.Text,Color=Paint(op.Color),Height=op.FontSize*s,Position=new Vector3D(op.Bounds.X*s,(IsHud?1:-1)*(op.Bounds.Y+op.FontSize*.5)*s,0)};
                        ExactCost(item);if(item.Points>0)result.Add(item);
                    }
                    else result.Add(SvgItem(frame,op.Key,HtmlSvgEncoding.Begin(frame)+HtmlSvgEncoding.Element(op,op.Order)+"</svg>",op.Order));
                }
                else if(op.Kind=="rect")result.Add(SvgItem(frame,op.Key,HtmlSvgEncoding.Begin(frame)+HtmlSvgEncoding.Element(op,op.Order)+"</svg>",op.Order));
                else throw new ArgumentException("Registered SVG asset rendering is not implemented in the first HTML painter profile.");
            }
            return result;
        }
    }
}
