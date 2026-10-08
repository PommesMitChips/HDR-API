using System;
using System.Collections.Generic;
using ProtoBuf;

namespace HoloMap
{
    [ProtoContract] public sealed class UiDisplay
    {
        [ProtoMember(1)] public long CallerId;
        [ProtoMember(2)] public long TargetId;
        [ProtoMember(3)] public long Revision;
        [ProtoMember(4)] public List<UiBundle> Bundles=new List<UiBundle>();
        [ProtoMember(5)] public List<UiWidget> Widgets=new List<UiWidget>();
        [ProtoMember(6)] public List<UiNumericValue> Values=new List<UiNumericValue>();
        [ProtoMember(7)] public long DataRevision;
        [ProtoMember(8)] public string ValueNotify;
    }
    [ProtoContract] public sealed class UiBundle
    {
        [ProtoMember(1)] public string Id;
        [ProtoMember(2,IsRequired=true)] public bool Visible=true;
    }
    [ProtoContract] public sealed class UiWidget
    {
        [ProtoMember(1)] public string Id;
        [ProtoMember(2)] public string Bundle;
        [ProtoMember(3)] public double X;
        [ProtoMember(4)] public double Y;
        [ProtoMember(5)] public double Width;
        [ProtoMember(6)] public double Height;
        [ProtoMember(7,IsRequired=true)] public bool Visible=true;
        [ProtoMember(8)] public string ActionKind;
        [ProtoMember(9)] public string Argument;
        [ProtoMember(10)] public string Label;
        [ProtoMember(11)] public UiControlData Control;
        [ProtoMember(12)] public string ScreenId;
    }
    public static class UiRules
    {
        public const int MaxWidgets=64,MaxTotalWidgets=128,MaxBundles=8,MaxDisplays=16;
        public const int ReplicationReserve=262144;
        public static string Id(string id)
        {
            if(id==null||id.Length<1||id.Length>24)throw new ArgumentException("UI ids require 1–24 characters.");
            foreach(char c in id)if(!(c>='a'&&c<='z')&&!(c>='0'&&c<='9')&&c!='-'&&c!='_')throw new ArgumentException("UI ids use lowercase letters, digits, hyphens and underscores.");
            return id;
        }
        public static void Action(string kind,string argument)
        {
            if(kind!="pb"&&kind!="toggle"&&kind!="menu"&&kind!="focus"&&kind!="control")throw new ArgumentException("UI actions are pb, toggle, menu or focus.");
            if(argument==null||argument.Length>256)throw new ArgumentException("UI action arguments accept at most 256 characters.");
            if(kind=="control"&&argument!="")throw new ArgumentException("Artwork controls use structured events, not remote action arguments.");
            if(kind=="toggle"&&argument!=LayerRules.Name(argument))throw new ArgumentException("UI layer actions use canonical lowercase layer names.");
            if(kind=="menu"||kind=="focus")Id(argument);
        }
        public static void Widget(UiWidget w)
        {
            if(w==null)throw new ArgumentException("Missing UI widget.");Id(w.Id);Id(w.Bundle);Action(w.ActionKind,w.Argument);
            if(w.ScreenId!=null){if(w.ScreenId.Length<1||w.ScreenId.Length>12)throw new ArgumentException("Invalid UI screen ID.");foreach(char c in w.ScreenId)if(!(c>='a'&&c<='z'||c>='0'&&c<='9'||c=='-'))throw new ArgumentException("Invalid UI screen ID.");}
            double extentX=Math.Abs(w.X)+w.Width/2,extentY=Math.Abs(w.Y)+w.Height/2;
            if(!Geometry.Finite(w.X)||!Geometry.Finite(w.Y)||!Geometry.Finite(w.Width)||!Geometry.Finite(w.Height)||w.Width<=0||w.Height<=0||(w.Control==null?extentX*extentX+extentY*extentY>625:extentX>1000000||extentY>1000000))throw new ArgumentException("UI bounds must be finite and positive; control rectangles use bounded artwork-local units.");
            if(w.Label!=null&&w.Label.Length>64)throw new ArgumentException("UI labels accept at most 64 characters.");
        }
        public static void Display(UiDisplay d)
        {
            if(d==null||d.CallerId==0||d.TargetId==0||d.Revision<1||d.Bundles==null||d.Widgets==null||d.Bundles.Count>MaxBundles||d.Widgets.Count>MaxWidgets)throw new ArgumentException("Invalid UI display.");
            var names=new HashSet<string>();foreach(var b in d.Bundles){if(b==null)throw new ArgumentException("Missing UI bundle.");Id(b.Id);if(!names.Add(b.Id))throw new ArgumentException("Duplicate UI bundle.");}
            var ids=new HashSet<string>();foreach(var w in d.Widgets){Widget(w);if(!names.Contains(w.Bundle)||names.Contains(w.Id)||!ids.Add(w.Id))throw new ArgumentException("Missing UI bundle or duplicate widget/bundle id.");if((w.ActionKind=="menu"||w.ActionKind=="focus")&&!names.Contains(w.Argument))throw new ArgumentException("UI action references a missing bundle.");}UiValueRules.Display(d);
        }
        public static bool Contains(UiWidget w,double x,double y)
        {return w!=null&&Geometry.Finite(x)&&Geometry.Finite(y)&&Math.Abs(x-w.X)<=w.Width/2&&Math.Abs(y-w.Y)<=w.Height/2;}
        public static UiWidget Hit(UiDisplay d,double x,double y)
        {
            if(d==null)return null;for(int i=d.Widgets.Count-1;i>=0;i--){var w=d.Widgets[i];if(!w.Visible||!Contains(w,x,y))continue;foreach(var b in d.Bundles)if(b.Id==w.Bundle&&b.Visible)return w;}return null;
        }
        public static UiDisplay Copy(UiDisplay d)
        {
            var n=new UiDisplay{CallerId=d.CallerId,TargetId=d.TargetId,Revision=d.Revision,DataRevision=d.DataRevision,ValueNotify=d.ValueNotify};
            foreach(var b in d.Bundles)n.Bundles.Add(new UiBundle{Id=b.Id,Visible=b.Visible});
            foreach(var w in d.Widgets)n.Widgets.Add(new UiWidget{Id=w.Id,Bundle=w.Bundle,X=w.X,Y=w.Y,Width=w.Width,Height=w.Height,Visible=w.Visible,ActionKind=w.ActionKind,Argument=w.Argument,Label=w.Label,Control=UiValueRules.Copy(w.Control),ScreenId=w.ScreenId});foreach(var v in d.Values)n.Values.Add(UiValueRules.Copy(v));return n;
        }
    }
}
