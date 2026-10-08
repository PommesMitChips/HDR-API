using System;
namespace Hdr.Html
{
    internal sealed class HtmlPbNativeMetrics : IHtmlTextMetrics
    {
        readonly HtmlPbNativePainter owner;
        readonly HtmlLcdTextMetrics metrics;
        internal HtmlPbNativeMetrics(HtmlPbNativePainter owner){this.owner=owner;metrics=new HtmlLcdTextMetrics(owner.Surface);}
        public string Profile{get{return metrics.Profile;}}
        public HtmlTextMeasurement Measure(string text,double size,double lineHeight)
        {if(!owner.Ready)throw new ArgumentException("Native sprite metrics require current PB/source authorization.");return metrics.Measure(text,size,lineHeight);}
    }
}
