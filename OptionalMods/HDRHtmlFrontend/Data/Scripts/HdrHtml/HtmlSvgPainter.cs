using System;
using System.Collections.Generic;
using System.Text;
using Hdr.Mods;
using VRageMath;

namespace Hdr.Html
{
    /// <summary>Bounded contiguous SVG chunks reduce retained item count; a changed chunk recompiles all its members.</summary>
    public sealed class HtmlSvgPainter : HtmlRetainedPainter
    {
        internal bool BreakAtSourceRegions;
        public HtmlSvgPainter(HdrModApi api,bool hud,MatrixD worldPose,double unitsPerPixel=1,int contextOrder=0,HtmlPainterLimits limits=null) : this(new HtmlHdrDrawApi(api),hud,worldPose,unitsPerPixel,contextOrder,limits) { }
        public HtmlSvgPainter(IHtmlDrawApi api,bool hud,MatrixD worldPose,double unitsPerPixel=1,int contextOrder=0,HtmlPainterLimits limits=null) : base(api,hud,worldPose,unitsPerPixel,contextOrder,limits) { }
        public override string Backend { get { return IsHud?"HDR grouped SVG HUD":"HDR grouped SVG world plane"; } }
        protected override List<HtmlPreparedItem> Prepare(HtmlPaintFrame frame)
        {
            var result=new List<HtmlPreparedItem>();var chunk=new List<HtmlPaintOperation>();
            foreach(var op in frame.Operations)
            {
                if(BreakAtSourceRegions&&chunk.Count>0&&frame.SourceRegions.Exists(s=>s.Order==op.Order))Flush(frame,chunk,result);
                if(!VisiblePaint(op)||op.Bounds.Empty||op.HasClip&&op.Clip.Empty)continue;
                if(op.Kind=="image")
                {
                    Flush(frame,chunk,result);var clipped=op.HasClip?Intersection(op.Bounds,op.Clip):op.Bounds;if(!clipped.Empty)result.Add(Quad(op,clipped,op.Asset));continue;
                }
                if(op.Kind!="rect"&&op.Kind!="text")throw new ArgumentException("Grouped SVG registered SVG assets are not implemented in this prototype.");
                if(op.Kind=="text")
                {
                    var m=Metrics.Measure(op.Text,op.FontSize,op.LineHeight);if(!m.HasInk)continue;
                    var ink=new HtmlRect(op.Bounds.X+m.Ink.X,op.Bounds.Y+m.Ink.Y,m.Ink.Width,m.Ink.Height);if(op.HasClip&&Intersection(ink,op.Clip).Empty)continue;
                }
                else if(op.HasClip&&Intersection(PaintedExtent(op),op.Clip).Empty)continue;
                chunk.Add(op);if(chunk.Count>=Limits.SvgOperationsPerChunk)Flush(frame,chunk,result);
            }
            Flush(frame,chunk,result);return result;
        }
        void Flush(HtmlPaintFrame frame,List<HtmlPaintOperation> chunk,List<HtmlPreparedItem> result)
        {
            if(chunk.Count==0)return;
            try{result.Add(Compile(frame,chunk));}
            catch(ArgumentException)
            {
                // Exact compiler rejects oversized chunks. Split deterministically rather than lower quality.
                if(chunk.Count==1)throw;
                int count=chunk.Count/2;var left=chunk.GetRange(0,count);var right=chunk.GetRange(count,chunk.Count-count);
                Flush(frame,left,result);Flush(frame,right,result);
            }
            chunk.Clear();
        }
        HtmlPreparedItem Compile(HtmlPaintFrame frame,List<HtmlPaintOperation> chunk)
        {
            var source=new StringBuilder(HtmlSvgEncoding.Begin(frame));
            foreach(var op in chunk)source.Append(HtmlSvgEncoding.Element(op,op.Order));source.Append("</svg>");
            return SvgItem(frame,"chunk:"+chunk[0].Key,source.ToString(),chunk[0].Order);
        }
    }
}
