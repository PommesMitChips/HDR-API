using System;
using System.Collections.Generic;
using System.Globalization;
using VRage;
using VRageMath;

namespace Hdr.Html
{
    /// <summary>Owns one isolated context. Validation precedes mutation; a failed batch restores owned prior items when possible.</summary>
    public abstract class HtmlRetainedPainter : IHtmlPainter
    {
        protected readonly IHtmlDrawApi Api;
        protected readonly HtmlPainterLimits Limits;
        protected readonly HtmlHdrTextMetrics Metrics;
        readonly MatrixD worldPose;
        readonly int contextOrder;
        readonly Dictionary<string,HtmlPreparedItem> retained = new Dictionary<string,HtmlPreparedItem>(StringComparer.Ordinal);
        HtmlPaintFrame accepted;
        long generation=-1;
        bool disposed;
        public long Context { get; private set; }
        public bool IsHud { get; private set; }
        public double UnitsPerPixel { get; private set; }
        public bool IsRenderingEnabled
        {
            get
            {
                if(disposed||!Api.Ready)return false;long observed=Api.Generation;
                try{bool enabled=(bool)Api.Call("rendering-enabled");return enabled&&Api.Ready&&Api.Generation==observed;}catch{return false;}
            }
        }
        public abstract string Backend { get; }
        protected HtmlRetainedPainter(IHtmlDrawApi api, bool hud, MatrixD pose, double unitsPerPixel, int contextOrder, HtmlPainterLimits limits)
        {
            if (api==null) throw new ArgumentNullException("api");
            if (!Finite(unitsPerPixel)||unitsPerPixel<1e-6||unitsPerPixel>1000||hud&&unitsPerPixel!=1) throw new ArgumentException("HUD units are pixels; world metres per pixel require 0.000001..1000.");
            Api=api;IsHud=hud;worldPose=pose;UnitsPerPixel=unitsPerPixel;this.contextOrder=contextOrder;Limits=(limits??new HtmlPainterLimits()).Snapshot();Limits.Validate();Metrics=new HtmlHdrTextMetrics(api);
        }
        protected abstract List<HtmlPreparedItem> Prepare(HtmlPaintFrame frame);
        public bool TryPaint(HtmlPaintFrame frame,out HtmlPaintReport report)
        {
            report=new HtmlPaintReport { Backend=Backend,Revision=frame==null?0:frame.Revision };
            try
            {
                if(disposed) throw new InvalidOperationException(Backend+" has been disposed.");
                Limits.Validate();HtmlPaintValidation.Validate(frame,new HtmlLimits { MaxPaintOperations=Limits.MaxOperations });
                if(frame.FontProfile!=HtmlHdrTextMetrics.MetricSchema) throw new ArgumentException("HDR painters require the matching Inter cap-height metric profile.");
                if(!Api.Ready) { LoseContext();throw new InvalidOperationException("Requires mod: HDR API (renderer unavailable)."); }
                long current=Api.Generation;report.EndpointGeneration=current;
                if(generation!=current) LoseContext();
                generation=current;
                if(Context!=0&&!(bool)CheckedCall("context-valid",Context)) { LoseContext();generation=current; }
                if(Context!=0&&HtmlPaintDiff.Equal(accepted,frame)) { report.Success=true;return true; }
                var prepared=Prepare(frame);report.PreparedOperations=prepared.Count;
                if(prepared.Count>Limits.MaxItems) throw new ArgumentException("HTML frame exceeds the painter's item grant; use bounded SVG chunks or simplify the document.");
                int points=0,primitives=0;var ids=new HashSet<string>(StringComparer.Ordinal);
                foreach(var item in prepared)
                {
                    if(!ids.Add(item.Id)) throw new ArgumentException("Stable HTML item ID collision.");
                    points=checked(points+item.Points);primitives=checked(primitives+item.Primitives);
                }
                report.EstimatedPoints=points;report.EstimatedTriangles=primitives;
                if(points>Limits.MaxPoints||primitives>Limits.MaxPrimitives) throw new ArgumentException("HTML compiled geometry exceeds the painter's retained point/primitive grant.");
                if(!Api.Ready||current!=Api.Generation) { LoseContext();throw new InvalidOperationException("HDR endpoint changed during paint preparation."); }
                // Counts include other contexts belonging to this caller's endpoint, not just this document.
                var usage=(MyTuple<int,int>)CheckedCall("geometry-usage");int oldPoints=0,oldPrimitives=0;
                foreach(var item in retained.Values){oldPoints+=item.Points;oldPrimitives+=item.Primitives;}
                if(usage.Item1-oldPoints+points>8192||usage.Item2-oldPrimitives+primitives>8192) throw new ArgumentException("HTML frame plus the caller's other contexts exceeds HDR's owner geometry grant.");
                if(Context==0){Context=(long)CheckedCall(IsHud?"create-hud":"create-world",IsHud?new object[]{contextOrder}:new object[]{contextOrder,worldPose});generation=current;report.MutatingCalls++;}
                var previous=new Dictionary<string,HtmlPreparedItem>(retained,StringComparer.Ordinal);
                try
                {
                    // Remove only obsolete owned IDs first; source upserts can otherwise exceed admission temporarily.
                    foreach(var old in previous.Values)if(!ids.Contains(old.Id)){CheckedCall("remove",Context,old.Id);report.MutatingCalls++;}
                    var changed=new List<HtmlPreparedItem>();
                    foreach(var item in prepared){HtmlPreparedItem old;if(!previous.TryGetValue(item.Id,out old)||!item.SameContent(old)){changed.Add(item);}}
                    // Dirty existing sources are retired before replacement to bound both point and primitive peaks.
                    foreach(var item in changed)if(previous.ContainsKey(item.Id)){CheckedCall("remove",Context,item.Id);report.MutatingCalls++;}
                    foreach(var item in changed){Send(item);report.MutatingCalls++;CheckedCall("item-order",Context,item.Id,item.Order);report.MutatingCalls++;}
                    foreach(var item in prepared){HtmlPreparedItem old;if(previous.TryGetValue(item.Id,out old)&&item.SameContent(old)&&old.Order!=item.Order){CheckedCall("item-order",Context,item.Id,item.Order);report.MutatingCalls++;}}
                    retained.Clear();foreach(var item in prepared)retained.Add(item.Id,item);
                    accepted=HtmlPaintDiff.Snapshot(frame);report.Success=true;report.Changed=report.MutatingCalls>0;return true;
                }
                catch(Exception error)
                {
                    report.Error=error.Message;report.Changed=report.MutatingCalls>0;
                    report.RestoredPrevious=Restore(previous,prepared,report,current);
                    if(!report.RestoredPrevious) report.Error+=" Previous owned paint could not be restored; the document context was retired.";
                    return false;
                }
            }
            catch(Exception error){report.Error=error.Message;report.Success=false;return false;}
        }
        object CheckedCall(string command, params object[] arguments)
        {
            long before=Api.Generation;if(!Api.Ready||before!=generation)throw new InvalidOperationException("HDR endpoint generation changed before renderer call.");
            var result=Api.Call(command,arguments);
            if(!Api.Ready||Api.Generation!=before)throw new InvalidOperationException("HDR endpoint generation changed during renderer call.");
            return result;
        }
        bool Restore(Dictionary<string,HtmlPreparedItem> previous,List<HtmlPreparedItem> candidates,HtmlPaintReport report,long expected)
        {
            if(!Api.Ready||Api.Generation!=expected){LoseContext();return false;}
            try
            {
                // Retire only IDs owned by this painter, then rebuild prior sources in their original order.
                var all=new HashSet<string>(previous.Keys,StringComparer.Ordinal);foreach(var item in candidates)all.Add(item.Id);
                foreach(var id in all){CheckedCall("remove",Context,id);report.MutatingCalls++;}
                var ordered=new List<HtmlPreparedItem>(previous.Values);ordered.Sort((a,b)=>a.Order.CompareTo(b.Order));
                foreach(var item in ordered){Send(item);report.MutatingCalls++;CheckedCall("item-order",Context,item.Id,item.Order);report.MutatingCalls++;}
                retained.Clear();foreach(var item in ordered)retained.Add(item.Id,item);return true;
            }
            catch{try{CheckedCall("destroy",Context);report.MutatingCalls++;}catch{}LoseContext();return false;}
        }
        void Send(HtmlPreparedItem item)
        {
            if(item.Kind=="mesh")CheckedCall("mesh",Context,item.Id,item.Vertices,item.Triangles,item.Color,item.UV,item.Material);
            else if(item.Kind=="text")CheckedCall("text",Context,item.Id,item.Text,item.Position,item.Height,item.Color,"start");
            else if(item.Kind=="svg")CheckedCall("svg",Context,item.Id,item.Svg,item.Pose,item.Segments);
            else throw new ArgumentException("Unknown prepared paint item.");
        }
        protected void ExactCost(HtmlPreparedItem item)
        {
            if(item.Kind=="mesh"){item.Points=item.Vertices.Length;item.Primitives=item.Triangles.Length/3;return;}
            HtmlPreparedItem unchanged;
            if(retained.TryGetValue(item.Id,out unchanged)&&item.SameContent(unchanged)){item.Points=unchanged.Points;item.Primitives=unchanged.Primitives;return;}
            var cost=(MyTuple<int,int>)CheckedCall("geometry-cost",item.Kind,item.Kind=="text"?item.Text:item.Svg,item.Kind=="text"?item.Height:1.0,item.Segments==0?Limits.CurveSegments:item.Segments);
            if(cost.Item1<0||cost.Item2<0||cost.Item1>2048||cost.Item2>4096)throw new ArgumentException("HDR returned an invalid compiled geometry cost.");
            item.Points=cost.Item1;item.Primitives=cost.Item2;
        }
        protected MatrixD SvgPose(HtmlPaintFrame frame)
        {return MatrixD.CreateScale(UnitsPerPixel,IsHud?-UnitsPerPixel:UnitsPerPixel,1)*MatrixD.CreateTranslation(frame.Width*.5*UnitsPerPixel,(IsHud?1:-1)*frame.Height*.5*UnitsPerPixel,0);}
        protected HtmlPreparedItem SvgItem(HtmlPaintFrame frame,string key,string source,int order)
        {if(source.Length>Limits.MaxSvgCharacters)throw new ArgumentException("HTML SVG source exceeds the configured chunk character grant.");var item=new HtmlPreparedItem {Id=ItemId(key),Kind="svg",Svg=source,Order=order,Pose=SvgPose(frame),Segments=Limits.CurveSegments};ExactCost(item);return item;}
        protected HtmlPreparedItem Quad(HtmlPaintOperation op,HtmlRect r,string material=null)
        {
            double s=UnitsPerPixel,y=IsHud?1:-1;
            var item=new HtmlPreparedItem {Id=ItemId(op.Key),Order=op.Order,Kind="mesh",Color=Paint(op.Color),Material=material,Vertices=new[]{new Vector3D(r.X*s,y*r.Y*s,0),new Vector3D((r.X+r.Width)*s,y*r.Y*s,0),new Vector3D((r.X+r.Width)*s,y*(r.Y+r.Height)*s,0),new Vector3D(r.X*s,y*(r.Y+r.Height)*s,0)},Triangles=new[]{0,1,2,0,2,3}};
            if(material!=null){double x=(r.X-op.Bounds.X)/op.Bounds.Width,yy=(r.Y-op.Bounds.Y)/op.Bounds.Height,w=r.Width/op.Bounds.Width,h=r.Height/op.Bounds.Height;item.UV=new[]{new Vector2((float)x,(float)yy),new Vector2((float)(x+w),(float)yy),new Vector2((float)(x+w),(float)(yy+h)),new Vector2((float)x,(float)(yy+h))};}
            ExactCost(item);return item;
        }
        protected static Vector4 Paint(HtmlColor c){return new Vector4((float)c.R,(float)c.G,(float)c.B,(float)c.A);}
        protected static bool VisiblePaint(HtmlPaintOperation op)
        {
            if(op.StrokeWidth>0)
            {
                var c=op.Stroke;
                if(!Finite(c.R)||!Finite(c.G)||!Finite(c.B)||!Finite(c.A)||c.R<0||c.R>1||c.G<0||c.G>1||c.B<0||c.B>1||c.A<0||c.A>1)throw new ArgumentException("HTML stroke RGBA must be finite and within 0..1.");
                if(op.Kind!="rect")throw new ArgumentException("HTML stroke decoration currently requires a rectangle paint operation.");
            }
            if(op.Kind!="rect"&&op.Radius>0)throw new ArgumentException("Rounded decoration currently requires a rectangle paint operation.");
            return op.Color.A>0||op.Kind=="rect"&&op.StrokeWidth>0&&op.Stroke.A>0;
        }
        protected static HtmlRect PaintedExtent(HtmlPaintOperation op)
        {double pad=op.StrokeWidth>0&&op.Stroke.A>0?op.StrokeWidth*.5:0;var r=op.Bounds;return new HtmlRect(r.X-pad,r.Y-pad,r.Width+pad*2,r.Height+pad*2);}
        protected static HtmlRect Intersection(HtmlRect a,HtmlRect b){double x=Math.Max(a.X,b.X),y=Math.Max(a.Y,b.Y);return new HtmlRect(x,y,Math.Max(0,Math.Min(a.X+a.Width,b.X+b.Width)-x),Math.Max(0,Math.Min(a.Y+a.Height,b.Y+b.Height)-y));}
        protected static bool Contains(HtmlRect outside,HtmlRect inside){return inside.Empty||inside.X>=outside.X-1e-8&&inside.Y>=outside.Y-1e-8&&inside.X+inside.Width<=outside.X+outside.Width+1e-8&&inside.Y+inside.Height<=outside.Y+outside.Height+1e-8;}
        internal static string ItemId(string key)
        {ulong hash=14695981039346656037UL;unchecked{foreach(char c in key){hash^=c;hash*=1099511628211UL;}}return "html-"+hash.ToString("x16",CultureInfo.InvariantCulture);}
        protected static bool Finite(double x){return !double.IsNaN(x)&&!double.IsInfinity(x);}
        void LoseContext(){Context=0;generation=-1;accepted=null;retained.Clear();}
        public void Reset(){if(Context!=0&&Api.Ready&&Api.Generation==generation)try{CheckedCall("destroy",Context);}catch{}LoseContext();}
        public void Dispose(){if(disposed)return;Reset();disposed=true;}
    }
}
