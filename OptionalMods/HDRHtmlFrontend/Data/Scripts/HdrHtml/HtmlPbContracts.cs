using System;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace Hdr.Html
{
    // Source-only boundary. PB crossings use game types and standard tuples, never these classes.
    internal interface IHtmlPbBridge
    {
        bool Ready { get; }
        object Draw(string operation,params object[] arguments);
        object Ui(string operation,params object[] arguments);
    }
    internal sealed class HtmlPbMetrics : IHtmlTextMetrics
    {
        readonly IHtmlPbBridge bridge;
        internal HtmlPbMetrics(IHtmlPbBridge bridge){this.bridge=bridge;}
        public string Profile{get{return HtmlHdrTextMetrics.MetricSchema;}}
        public HtmlTextMeasurement Measure(string text,double size,double lineHeight)
        {
            var m=(MyTuple<string,MyTuple<double,double,double>,MyTuple<double,double,double,double>,int,bool>)bridge.Draw("measure-text",text,size,lineHeight);
            if(m.Item1!=Profile||m.Item4!=1)throw new ArgumentException("Unsupported HDR PB text metrics.");
            return new HtmlTextMeasurement{Advance=m.Item2.Item1,CapHeight=m.Item2.Item2,LineAdvance=m.Item2.Item3,Ink=new HtmlRect(m.Item3.Item1,.5*size-m.Item3.Item4,m.Item3.Item3-m.Item3.Item1,m.Item3.Item4-m.Item3.Item2),Generation=m.Item4,HasInk=m.Item5};
        }
    }
    internal sealed class HtmlPbItem
    {
        internal string Id,Source;
        internal MatrixD Pose;
        internal int Points,Primitives;
    }
    internal sealed class HtmlPbControl
    {
        internal string Id,ValueId,Artwork,Node,Action,Binding,Kind;
        internal HtmlRect Hit,Knob;
        internal double Value,Min,Max,Step;
        internal Vector3D Start,End;
        internal bool Same(HtmlPbControl b)
        {return b!=null&&Id==b.Id&&ValueId==b.ValueId&&Artwork==b.Artwork&&Node==b.Node&&Action==b.Action&&Binding==b.Binding&&Kind==b.Kind&&Hit.X==b.Hit.X&&Hit.Y==b.Hit.Y&&Hit.Width==b.Hit.Width&&Hit.Height==b.Hit.Height&&Min==b.Min&&Max==b.Max&&Step==b.Step&&Near(Start,b.Start)&&Near(End,b.End);}
        static bool Near(Vector3D a,Vector3D b){return Vector3D.DistanceSquared(a,b)<=1e-18;}
    }
    internal sealed class HtmlPbPlan
    {
        internal HtmlPaintFrame Frame;
        internal readonly List<HtmlPbItem> Items=new List<HtmlPbItem>();
        internal readonly List<HtmlPbControl> Controls=new List<HtmlPbControl>();
    }
}
