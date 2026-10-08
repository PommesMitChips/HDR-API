using System;
using Hdr.Mods;
using VRage;

namespace Hdr.Html
{
    /// <summary>Read-only metrics from the same packaged font used by HDR's vector and SVG paths.</summary>
    public sealed class HtmlHdrTextMetrics : IHtmlTextMetrics
    {
        public const string MetricSchema = "HDR.TextMetrics/1:Inter:cap-height";
        readonly IHtmlDrawApi api;
        public HtmlHdrTextMetrics(HdrModApi api) : this(new HtmlHdrDrawApi(api)) { }
        public HtmlHdrTextMetrics(IHtmlDrawApi api) { if (api == null) throw new ArgumentNullException("api"); this.api = api; }
        public string Profile { get { return MetricSchema; } }
        public HtmlTextMeasurement Measure(string text, double size, double lineHeight)
        {
            if (text == null || text.Length > 64) throw new ArgumentException("Metrics require at most 64 UTF16 units per call.");
            if (!api.Ready) throw new InvalidOperationException("Requires mod: HDR API with text metrics.");
            long generation=api.Generation;
            var result = (MyTuple<string,MyTuple<double,double,double>,MyTuple<double,double,double,double>,int,bool>)api.Call("measure-text",text,size,lineHeight);
            if (!api.Ready || generation != api.Generation) throw new InvalidOperationException("HDR metrics endpoint changed during measurement.");
            if (result.Item1 != MetricSchema) throw new ArgumentException("Unsupported HDR text metric profile.");
            return new HtmlTextMeasurement { Advance=result.Item2.Item1,CapHeight=result.Item2.Item2,LineAdvance=result.Item2.Item3,Ink=new HtmlRect(result.Item3.Item1, .5*size-result.Item3.Item4, result.Item3.Item3-result.Item3.Item1,result.Item3.Item4-result.Item3.Item2),Generation=result.Item4,HasInk=result.Item5 };
        }
    }
}
