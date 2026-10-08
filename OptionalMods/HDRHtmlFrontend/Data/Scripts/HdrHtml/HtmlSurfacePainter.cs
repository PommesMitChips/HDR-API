using System;
using System.Collections.Generic;
using Hdr.Mods;
using VRage;
using VRageMath;
using Block=Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace Hdr.Html
{
    // Uses the same legitimate local Core owner/context as every retained frontend.
    internal sealed class HtmlSurfacePainter : IHtmlPainter
    {
        readonly IHtmlDrawApi api;
        readonly HtmlRetainedPainter artwork;
        readonly Adapter adapter;
        readonly string kind;
        readonly object[] parameters;
        readonly MyTuple<string,object[]>[] settings;
        readonly double units;
        readonly bool hud;
        readonly MatrixD worldPose;
        readonly bool topLeftWorld;
        Block sourceAnchor;
        HtmlPaintFrame committed;
        List<HtmlPbSourceSlot> committedSources=new List<HtmlPbSourceSlot>();
        long generation=-1;
        bool disposed;
        internal ICollection<HtmlPbSourceAttachment> Sources;
        internal long Context{get{return artwork.Context;}}
        internal double Units{get{return units;}}

        internal HtmlSurfacePainter(HdrModApi core,MatrixD pose,double units,string kind,object[] parameters,MyTuple<string,object[]>[] settings,string backend,int order,bool topLeftWorld=false)
            :this(new HtmlHdrDrawApi(core),pose,units,kind,parameters,settings,backend,order,topLeftWorld){}
        internal HtmlSurfacePainter(IHtmlDrawApi api,MatrixD pose,double units,string kind,object[] parameters,MyTuple<string,object[]>[] settings,string backend,int order,bool topLeftWorld=false)
        {
            if(api==null)throw new ArgumentNullException("api");
            if(kind!="hud"&&kind!="plane"&&kind!="cylinder"&&kind!="sphere"&&kind!="ellipsoid"&&kind!="mesh")throw new ArgumentException("Surface kind requires plane, cylinder, sphere, ellipsoid or mesh.");
            if(backend!="vector"&&backend!="svg")throw new ArgumentException("Surface HTML backend requires vector or svg.");
            if(!HtmlFrontendDocument.Finite(units)||units<1e-6||units>1000)throw new ArgumentException("Surface HTML metres per pixel require 0.000001..1000.");
            this.api=api;this.units=units;this.kind=kind;worldPose=pose;this.topLeftWorld=topLeftWorld;hud=kind=="hud";if(hud&&units!=1)throw new ArgumentException("HUD source canvas uses pixels.");this.parameters=Copy(parameters);this.settings=CopySettings(settings);
            adapter=new Adapter(this);
            if(backend=="vector")artwork=new HtmlVectorPainter(adapter,hud,pose,units,order);
            else{var svg=new HtmlSvgPainter(adapter,hud,pose,units,order);svg.BreakAtSourceRegions=true;artwork=svg;}
        }
        static object[] Copy(object[] values)
        {
            if(values==null)return new object[0];if(values.Length>8)throw new ArgumentException("Surface parameter limit exceeded.");
            var copy=new object[values.Length];
            for(int i=0;i<values.Length;i++)
            {var value=values[i];if(value is Vector3D[])copy[i]=((Vector3D[])value).Clone();else if(value is Vector2[])copy[i]=((Vector2[])value).Clone();else if(value is int[])copy[i]=((int[])value).Clone();else if(value is string||value is int||value is double||value is float||value is bool||value is Vector3D||value is Block)copy[i]=value;else throw new ArgumentException("Unsupported surface parameter type.");}
            return copy;
        }
        static MyTuple<string,object[]>[] CopySettings(MyTuple<string,object[]>[] values)
        {
            if(values==null)return new MyTuple<string,object[]>[0];if(values.Length>4)throw new ArgumentException("Surface settings allow at most four named entries.");
            var result=new MyTuple<string,object[]>[values.Length];var names=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<values.Length;i++)
            {
                string name=values[i].Item1;var args=Copy(values[i].Item2);
                if(name==null||!names.Add(name))throw new ArgumentException("Surface settings require unique names.");
                if(name=="anchor"){if(args.Length!=1||!(args[0] is Block))throw new ArgumentException("Surface anchor requires an actual terminal block.");}
                else if(name=="mapping"){if(args.Length<1||args.Length>3||!(args[0] is string))throw new ArgumentException("Surface mapping requires a mode and optional FOV/aspect.");}
                else if(name=="sided"){if(args.Length!=3||!(args[0] is bool))throw new ArgumentException("Surface sided requires a Boolean and front/back opacity.");}
                else if(name=="error"){if(args.Length!=1)throw new ArgumentException("Surface error requires one metre tolerance.");}
                else throw new ArgumentException("Unsupported surface setting: "+name);
                result[i]=new MyTuple<string,object[]>(name,args);
            }
            return result;
        }
        object Call(string command,params object[] args)
        {
            long observed=api.Generation;if(!api.Ready||observed!=generation)throw new InvalidOperationException("HDR endpoint changed during surface composition.");
            object result=api.Call(command,args);if(!api.Ready||api.Generation!=observed)throw new InvalidOperationException("HDR endpoint changed during surface composition.");return result;
        }
        void Configure(long context,HtmlPaintFrame frame)
        {
            if(!hud){var values=new object[4+parameters.Length];values[0]=context;values[1]=kind;values[2]=frame.Width*units;values[3]=frame.Height*units;Array.Copy(parameters,0,values,4,parameters.Length);Call("context-surface",values);}
            if(topLeftWorld)Call("context-pose",context,ContextPose(frame));
            foreach(var setting in settings)
            {var args=new object[setting.Item2.Length+1];args[0]=context;Array.Copy(setting.Item2,0,args,1,setting.Item2.Length);Call("context-"+(setting.Item1=="sided"?"surface-sided":setting.Item1=="error"?"surface-error":setting.Item1),args);}
            if(sourceAnchor!=null)Call("context-anchor",context,sourceAnchor);
        }
        MatrixD ContextPose(HtmlPaintFrame frame)
        {return topLeftWorld?MatrixD.CreateTranslation(frame.Width*.5*units,-frame.Height*.5*units,0)*worldPose:worldPose;}
        internal void SetSourceAnchor(Block anchor)
        {if(anchor==null)throw new ArgumentException("Source anchor requires an actual terminal block.");if(Context==0||!api.Ready||generation!=api.Generation)throw new ArgumentException("No live local source context.");Call("context-anchor",Context,anchor);sourceAnchor=anchor;}
        internal void ValidateSource(string provider,string source)
        {
            if(disposed||Context==0||!api.Ready||api.Generation!=generation)throw new ArgumentException("Surface document has no live committed Core context.");
            Call("context-slot-validate",Context,provider,source);
            var capability=(MyTuple<bool,bool,string>)Call("context-source-status",Context,provider);
            if(!capability.Item1||!capability.Item2)throw new ArgumentException(capability.Item3??"Unsupported: local source consumer or actual source anchor is unavailable.");
        }
        internal MyTuple<bool,string> SourceStatus(HtmlPbSourceAttachment source)
        {
            if(disposed||Context==0||!api.Ready||api.Generation!=generation)return new MyTuple<bool,string>(false,"Inactive: local Core context is unavailable.");
            return committedSources.Exists(s=>s.Attachment.Slot==source.Slot)?(MyTuple<bool,string>)Call("context-slot-status",Context,source.Slot):new MyTuple<bool,string>(false,"Hidden: source node has no visible content region.");
        }
        internal string[] SourceCapabilities()
        {
            if(Context==0||!api.Ready||api.Generation!=generation)return new[]{"Inactive: local Core context is unavailable."};
            var capability=(MyTuple<bool,bool,string>)Call("context-source-status",Context);
            return new[]{"client-local-source-slots","ordered-artwork",hud?"hud-pixel-source-rectangles":"all-core-surface-kinds",hud?"cooperative-pointer":"cooperative-pointer-ray",capability.Item3??""};
        }
        internal MyTuple<bool,Vector2,Vector3D> Ray(Vector3D origin,Vector3D direction)
        {if(hud)throw new ArgumentException("HUD documents use cooperative Pointer pixels.");return (MyTuple<bool,Vector2,Vector3D>)Call("context-surface-ray",Context,origin,direction);}
        HtmlPaintFrame Prepare(HtmlPaintFrame frame,out List<HtmlPbSourceSlot> slots)
        {
            var ranked=HtmlPaintDiff.Snapshot(frame);ranked.SourceRegions.Clear();slots=new List<HtmlPbSourceSlot>();int order=0;
            for(int position=0;position<=frame.Operations.Count;position++)
            {
                foreach(var region in frame.SourceRegions)
                {
                    if(region.Order!=position||!region.Visible||region.Bounds.Empty||region.Clip.Empty||region.Opacity<=0||Sources==null)continue;
                    HtmlPbSourceAttachment attachment=null;foreach(var source in Sources)if(source.Node==region.NodeId){attachment=source;break;}if(attachment==null)continue;
                    slots.Add(new HtmlPbSourceSlot{Attachment=attachment,Rect=Rect(region.Bounds,frame),Clip=Rect(region.Clip,frame),UV=new Vector4(0,0,1,1),Order=order++,Opacity=region.Opacity*HtmlPbSourceOptions.Opacity(attachment.Settings)});
                    ranked.SourceRegions.Add(new HtmlSourceRegion{NodeId=region.NodeId,Bounds=region.Bounds,Clip=region.Clip,Opacity=region.Opacity,Order=region.Order,Visible=region.Visible});
                }
                if(position<ranked.Operations.Count)order++;
            }
            return ranked;
        }
        Vector4 Rect(HtmlRect r,HtmlPaintFrame frame)
        {return hud?new Vector4((float)r.X,(float)r.Y,(float)r.Width,(float)r.Height):new Vector4((float)((r.X-frame.Width*.5)*units),(float)((frame.Height*.5-r.Y-r.Height)*units),(float)(r.Width*units),(float)(r.Height*units));}
        void Publish(List<HtmlPbSourceSlot> next,List<HtmlPbSourceSlot> previous)
        {
            foreach(var slot in next)
            {
                var old=previous.Find(s=>s.Attachment.Slot==slot.Attachment.Slot);if(slot.Same(old))continue;
                var source=slot.Attachment;Call("context-slot",Context,source.Slot,source.Provider,source.Source,slot.Rect,slot.Clip,slot.UV,slot.Order,slot.Opacity);
                foreach(var setting in source.Settings)if(setting.Item1!="opacity")
                {var args=new object[2+setting.Item2.Length];args[0]=Context;args[1]=source.Slot;Array.Copy(setting.Item2,0,args,2,setting.Item2.Length);Call("context-slot-"+(setting.Item1=="quality"?"camera-quality":setting.Item1),args);}
            }
            foreach(var old in previous)if(!next.Exists(s=>s.Attachment.Slot==old.Attachment.Slot))Call("context-slot-remove",Context,old.Attachment.Slot);
        }
        public bool TryPaint(HtmlPaintFrame frame,out HtmlPaintReport report)
        {
            report=new HtmlPaintReport{Backend="local-surface",Revision=frame==null?0:frame.Revision};
            if(disposed){report.Error="Surface painter is disposed.";return false;}
            bool reconfigured=false;HtmlPaintFrame next=null;List<HtmlPbSourceSlot> slots=null;
            try
            {
                HtmlPaintValidation.Validate(frame,new HtmlLimits());
                if(!api.Ready)throw new InvalidOperationException("Requires mod: HDR API local surface context.");
                if(generation!=api.Generation){generation=api.Generation;committed=null;committedSources.Clear();}
                if(Context!=0&&!(bool)Call("context-valid",Context)){artwork.Reset();committed=null;committedSources.Clear();}
                next=Prepare(frame,out slots);adapter.SetFrame(next);
                if(Context!=0&&committed!=null&&(committed.Width!=next.Width||committed.Height!=next.Height)){Configure(Context,next);reconfigured=true;report.MutatingCalls++;}
                if(!artwork.TryPaint(next,out report))
                {
                    if(reconfigured&&committed!=null){Configure(Context,committed);report.RestoredPrevious=report.RestoredPrevious||!report.Changed;}
                    if(committed!=null&&Context!=0){adapter.SetFrame(committed);adapter.ApplyOrders();}
                    return false;
                }
                adapter.ApplyOrders();
                bool changed=slots.Count!=committedSources.Count;foreach(var slot in slots)if(!slot.Same(committedSources.Find(s=>s.Attachment.Slot==slot.Attachment.Slot)))changed=true;
                if(changed){Publish(slots,committedSources);report.MutatingCalls++;report.Changed=true;}
                if(committed==null){Call("context-visible",Context,true);report.MutatingCalls++;report.Changed=true;}
                committed=next;committedSources=slots;report.Success=true;return true;
            }
            catch(Exception error)
            {
                report.Success=false;report.Error=error.Message;
                if(Context==0)return false;
                try
                {
                    if(committed==null)throw new InvalidOperationException("Initial surface publication failed.");
                    if(reconfigured)Configure(Context,committed);
                    adapter.SetFrame(committed);HtmlPaintReport restored;
                    if(!artwork.TryPaint(committed,out restored))throw new InvalidOperationException("Prior artwork could not be restored.");
                    adapter.ApplyOrders();
                    Publish(committedSources,slots??new List<HtmlPbSourceSlot>());report.RestoredPrevious=true;report.Changed=true;
                }
                catch{Reset();report.RestoredPrevious=false;report.Changed=true;}
                return false;
            }
        }
        public void Reset(){artwork.Reset();committed=null;committedSources.Clear();}
        public void Dispose(){if(disposed)return;artwork.Dispose();committed=null;committedSources.Clear();Sources=null;disposed=true;}
        sealed class Adapter : IHtmlDrawApi
        {
            readonly HtmlSurfacePainter owner;
            internal HtmlPaintFrame Frame;
            readonly Dictionary<string,int> originalOrders=new Dictionary<string,int>(StringComparer.Ordinal);
            readonly Dictionary<string,int> actualOrders=new Dictionary<string,int>(StringComparer.Ordinal);
            readonly Dictionary<int,int> operationOrders=new Dictionary<int,int>();
            internal Adapter(HtmlSurfacePainter owner){this.owner=owner;}
            public bool Ready{get{return owner.api.Ready;}}
            public long Generation{get{return owner.api.Generation;}}
            internal void SetFrame(HtmlPaintFrame frame)
            {
                Frame=frame;operationOrders.Clear();int rank=0;
                for(int i=0;i<frame.Operations.Count;i++){foreach(var region in frame.SourceRegions)if(region.Order==i)rank++;operationOrders[i]=rank++;}
            }
            internal void ApplyOrders()
            {foreach(var pair in originalOrders){int rank=operationOrders[pair.Value],actual;if(!actualOrders.TryGetValue(pair.Key,out actual)||actual!=rank){owner.Call("item-order",owner.Context,pair.Key,rank);actualOrders[pair.Key]=rank;}}}
            public object Call(string command,params object[] args)
            {
                if(command=="create-world"||command=="create-hud")
                {
                    originalOrders.Clear();actualOrders.Clear();var values=(object[])args.Clone();if(command=="create-world")values[1]=owner.ContextPose(Frame);long context=(long)owner.Call(command,values);
                    try{owner.Call("context-visible",context,false);owner.Configure(context,Frame);return context;}
                    catch{try{owner.Call("destroy",context);}catch{}throw;}
                }
                var copy=(object[])args.Clone();var shift=new Vector3D(Frame.Width*.5*owner.units,-Frame.Height*.5*owner.units,0);
                if(command=="item-order"){string id=(string)copy[1];int original=(int)copy[2],rank;originalOrders[id]=original;copy[2]=operationOrders.TryGetValue(original,out rank)?rank:original;actualOrders[id]=(int)copy[2];}
                if(command=="remove"){originalOrders.Remove((string)copy[1]);actualOrders.Remove((string)copy[1]);}
                if(!owner.hud&&command=="svg"){var pose=(MatrixD)copy[3];pose.Translation-=shift;copy[3]=pose;}
                else if(!owner.hud&&command=="text")copy[3]=(Vector3D)copy[3]-shift;
                else if(!owner.hud&&command=="mesh"){var points=(Vector3D[])copy[2];var centered=new Vector3D[points.Length];for(int i=0;i<points.Length;i++)centered[i]=points[i]-shift;copy[2]=centered;}
                return owner.Call(command,copy);
            }
        }
    }
}
