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
        internal string Id,Source,Layer;
        internal int Order;
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
        internal readonly List<HtmlPbSourceSlot> Sources=new List<HtmlPbSourceSlot>();
    }
    internal sealed class HtmlPbSourceAttachment
    {
        internal string Node,Provider,Source,Slot;
        internal MyTuple<string,object[]>[] Settings;
    }
    internal sealed class HtmlPbSourceSlot
    {
        internal HtmlPbSourceAttachment Attachment;
        internal Vector4 Rect,Clip,UV;
        internal MatrixD Pose=MatrixD.Identity;
        internal int Order;
        internal double Opacity;
        internal bool Same(HtmlPbSourceSlot b)
        {return b!=null&&Attachment.Provider==b.Attachment.Provider&&Attachment.Source==b.Attachment.Source&&ReferenceEquals(Attachment.Settings,b.Attachment.Settings)&&Rect==b.Rect&&Clip==b.Clip&&UV==b.UV&&Pose==b.Pose&&Order==b.Order&&Opacity==b.Opacity;}
    }
    internal static class HtmlPbSourceOptions
    {
        internal static MyTuple<string,object[]>[] Copy(MyTuple<string,object[]>[] settings)
        {
            if(settings==null)return new MyTuple<string,object[]>[0];
            if(settings.Length>4)throw new ArgumentException("Source attachments allow at most four named settings.");
            var copy=new MyTuple<string,object[]>[settings.Length];var names=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<settings.Length;i++)
            {
                string name=settings[i].Item1;var values=settings[i].Item2;
                if(name=="camera-quality")name="quality";
                if(name==null||values==null||!names.Add(name))throw new ArgumentException("Source settings require unique names and argument arrays.");
                if(name=="refresh"){Count(values,1);Number(values[0],1,120);}
                else if(name=="opacity"){Count(values,1);Number(values[0],0,1);}
                else if(name=="quality"){Count(values,1);if(!(values[0] is string)||((string)values[0]!="normal"&&(string)values[0]!="lite"))throw new ArgumentException("Source quality requires normal or lite.");}
                else if(name=="panorama")
                {Count(values,4);Number(values[0],60,120);Number(values[1],0,25);Number(values[2],0,2);if(!(values[3] is int)||((int)values[3]!=256&&(int)values[3]!=512&&(int)values[3]!=1024&&(int)values[3]!=2048))throw new ArgumentException("Panorama resolution requires 256, 512, 1024 or 2048.");}
                else throw new ArgumentException("Unsupported source attachment setting: "+name);
                copy[i]=new MyTuple<string,object[]>(name,(object[])values.Clone());
            }
            return copy;
        }
        static void Count(object[] values,int count){if(values.Length!=count)throw new ArgumentException("Source setting has an invalid argument count.");}
        static double Number(object value,double min,double max)
        {double n;if(value is double)n=(double)value;else if(value is int)n=(int)value;else if(value is float)n=(float)value;else throw new ArgumentException("Source setting requires a number.");if(double.IsNaN(n)||double.IsInfinity(n)||n<min||n>max)throw new ArgumentException("Source setting is outside its allowed range.");return n;}
        internal static double Opacity(MyTuple<string,object[]>[] settings)
        {foreach(var option in settings)if(option.Item1=="opacity")return Number(option.Item2[0],0,1);return 1;}
        internal static void Apply(IHtmlPbBridge bridge,HtmlPbSourceAttachment source)
        {foreach(var option in source.Settings)if(option.Item1!="opacity"){var values=new object[option.Item2.Length+1];values[0]=source.Slot;Array.Copy(option.Item2,0,values,1,option.Item2.Length);bridge.Draw("screen-slot-"+(option.Item1=="quality"?"camera-quality":option.Item1),values);}}
    }
}
