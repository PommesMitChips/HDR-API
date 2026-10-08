using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using IMyTextSurface=Sandbox.ModAPI.Ingame.IMyTextSurface;
using PbBlock=Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace Hdr.Html
{
    internal sealed class HtmlPbNativePainter : IHtmlPainter
    {
        readonly HtmlPbBridge port;
        readonly IMyProgrammableBlock caller;
        readonly IMyTextPanel source;
        readonly IMyTextSurface surface;
        readonly IMyTerminalBlock anchor;
        readonly object token=new object();
        readonly HtmlNativeLcdPainter native;
        readonly string screen;
        readonly string artworkSlot;
        readonly MatrixD pose;
        readonly double units;
        bool claimed,relayDeclared,disposed,cleanup;
        Vector2 relayTexture,relayViewport;
        List<HtmlPbSourceSlot> committedSources=new List<HtmlPbSourceSlot>();
        HtmlPaintFrame committedFrame;
        internal ICollection<HtmlPbSourceAttachment> Sources;
        bool Mapped{get{return port.ScreenId!=null;}}
        internal bool Relay{get{return Mapped||!ReferenceEquals(source,anchor);}}
        internal IMyTextSurface Surface{get{return surface;}}
        internal bool NeedsRefresh{get{return Relay&&(!relayDeclared||surface.TextureSize!=relayTexture||surface.SurfaceSize!=relayViewport);}}
        internal HtmlPbNativePainter(long handle,HtmlPbBridge port,IMyProgrammableBlock caller,IMyTerminalBlock anchor,IMyTextPanel source,MatrixD pose,double units,bool owns)
        {
            if(!owns)throw new ArgumentException("PB sprites require explicit caller ownership of the source LCD surface.");
            if(source==null||!(anchor is IMyProjector||ReferenceEquals(source,anchor)))throw new ArgumentException("Native sprites need a physical LCD or Console/Projector with an owned physical source LCD.");
            if(double.IsNaN(units)||double.IsInfinity(units)||units<1e-6||units>1000)throw new ArgumentException("Native relay metres per pixel require 0.000001..1000.");
            double determinant=pose.Determinant();if(double.IsNaN(determinant)||double.IsInfinity(determinant)||Math.Abs(determinant)<1e-12||Math.Abs(pose.M14)>1e-9||Math.Abs(pose.M24)>1e-9||Math.Abs(pose.M34)>1e-9||Math.Abs(pose.M44-1)>1e-9)throw new ArgumentException("Native sprite model pose must be nonsingular affine.");
            double[] poseValues={pose.M11,pose.M12,pose.M13,pose.M21,pose.M22,pose.M23,pose.M31,pose.M32,pose.M33,pose.M41,pose.M42,pose.M43};foreach(double n in poseValues)if(double.IsNaN(n)||double.IsInfinity(n)||Math.Abs(n)>1e12)throw new ArgumentException("Native sprite model pose must be finite and bounded.");
            if(handle<1||handle>0xFFFFFFFFFFFL)throw new ArgumentException("Native sprite screen handle exceeds the collision-free twelve-character screen ID domain.");
            if(port.ScreenId!=null&&handle>uint.MaxValue)throw new ArgumentException("Mapped native HTML handle exceeds its collision-free short source slot identity domain.");
            this.port=port;this.caller=caller;this.anchor=anchor;this.source=source;surface=source;this.pose=pose;this.units=units;screen=port.ScreenId??"h"+handle.ToString("x",System.Globalization.CultureInfo.InvariantCulture);artworkSlot="h"+handle.ToString("x",System.Globalization.CultureInfo.InvariantCulture)+"n";
            if(!Authority())throw new ArgumentException("Source LCD must be live, authorized, working and manually SCRIPT/NONE before sprite binding.");
            if(Relay)ValidateRelay(surface.SurfaceSize,surface.TextureSize);
            if(!HtmlPbNativeSurfaceClaims.TryClaim(surface,token))throw new ArgumentException("The source LCD surface is already claimed by another HTML frontend document.");
            claimed=true;native=new HtmlNativeLcdPainter(surface,true,null,Authority);
        }
        internal bool Ready{get{return !disposed&&Authority();}}
        bool Authority()
        {
            try
            {
                if(disposed||!port.Ready||source.Closed||!ReferenceEquals(MyAPIGateway.Entities.GetEntityById(source.EntityId),source)||caller.OwnerId==0||!caller.IsSameConstructAs(source)||!source.HasPlayerAccess(caller.OwnerId))return false;
                if(!cleanup&&!source.IsWorking)return false;
                if(surface.ContentType!=VRage.Game.GUI.TextPanel.ContentType.SCRIPT||!string.IsNullOrEmpty(surface.Script))return false;
                if(claimed&&!HtmlPbNativeSurfaceClaims.Owns(surface,token))return false;
                var caps=(MyTuple<string,string,int>)port.Draw("capabilities",(PbBlock)source);return caps.Item1=="HDR.DisplayCapabilities/1"&&caps.Item2=="lcd"&&(caps.Item3&1)!=0;
            }
            catch{return false;}
        }
        public bool TryPaint(HtmlPaintFrame frame,out HtmlPaintReport report)
        {
            if(!Authority()){report=new HtmlPaintReport{Backend="native-sprites",Error="Native sprite source ownership/lifecycle is inactive."};return false;}
            Vector2 texture=surface.TextureSize,viewport=surface.SurfaceSize;
            if(Relay)try{ValidateRelay(viewport,texture);}catch(Exception error){report=new HtmlPaintReport{Backend="native-sprites-relay",Error=error.Message};return false;}
            List<HtmlPbSourceSlot> next;
            try{next=PrepareSources(frame);}catch(Exception error){report=new HtmlPaintReport{Backend="native-sprites-relay",Error=error.Message};return false;}
            if(!native.TryPaint(frame,out report))return false;
            if(Mapped)
            {
                try
                {
                    if(!relayDeclared||texture!=relayTexture||viewport!=relayViewport)
                    {
                        PublishArtwork(frame,texture,viewport);
                    }
                    PublishSources(next,committedSources);
                    relayDeclared=true;relayTexture=texture;relayViewport=viewport;committedSources=next;committedFrame=HtmlPaintDiff.Snapshot(frame);return true;
                }
                catch(Exception error)
                {
                    report.Success=false;report.Changed=true;report.Error="Native composition publication failed: "+error.Message;
                    try
                    {
                        RemoveSourceList(next);
                        if(committedFrame!=null)PublishArtwork(committedFrame,relayTexture,relayViewport);else try{RelayCall("screen-slot-remove",artworkSlot);}catch{}
                        PublishSources(committedSources,null);
                        HtmlPaintReport restored;
                        if(committedFrame!=null&&native.TryPaint(committedFrame,out restored)){report.RestoredPrevious=true;return false;}
                    }
                    catch{}
                    RemoveRelay();return false;
                }
            }
            if(Relay&&(!relayDeclared||texture!=relayTexture||viewport!=relayViewport))
            {
                try
                {
                    // Preserve texture pixels/aspect; native surface content is centered inside any texture padding.
                    MatrixD center=MatrixD.CreateTranslation(frame.Width*.5*units,-frame.Height*.5*units,0)*pose;
                    port.Draw("screen",screen,center,texture.X*units,texture.Y*units,texture.X*units,texture.Y*units);
                    RelayCall("screen-source","lcd-texture",source.EntityId.ToString(System.Globalization.CultureInfo.InvariantCulture)+":0");
                    RelayCall("screen-resolution",(int)texture.X,(int)texture.Y);
                    RelayCall("screen-refresh",60.0);
                    RelayCall("screen-aspect","stretch");relayDeclared=true;relayTexture=texture;relayViewport=viewport;
                }
                catch(Exception error){report.Success=false;report.Changed=true;report.Error="Native LCD painted, but owned projection relay failed: "+error.Message;RemoveRelay();return false;}
            }
            return true;
        }
        List<HtmlPbSourceSlot> PrepareSources(HtmlPaintFrame frame)
        {
            var slots=new List<HtmlPbSourceSlot>();if(Mapped)CanvasRect(new HtmlRect(0,0,frame.Width,frame.Height),frame);if(Sources==null||Sources.Count==0)return slots;
            if(!Mapped)throw new ArgumentException("Unsupported: external textures require a mapped native relay screen.");
            foreach(var region in frame.SourceRegions)
            {
                HtmlPbSourceAttachment attachment=null;foreach(var candidate in Sources)if(candidate.Node==region.NodeId){attachment=candidate;break;}
                if(attachment==null||!region.Visible||region.Bounds.Empty||region.Clip.Empty||region.Opacity<=0||HtmlPbSourceOptions.Opacity(attachment.Settings)<=0)continue;
                if(region.Opacity<.999999||HtmlPbSourceOptions.Opacity(attachment.Settings)<.999999)throw new ArgumentException("Unsupported: the native LCD texture is RGB; partial source opacity cannot reveal the LCD's original HTML background at authored paint order.");
                for(int i=region.Order;i<frame.Operations.Count;i++)
                {var op=frame.Operations[i];if(op.Color.A>0&&Overlap(op.HasClip?Intersection(op.Bounds,op.Clip):op.Bounds,region.Clip))throw new ArgumentException("Unsupported: native LCD RGB texture cannot place this source before overlapping later HTML artwork or controls. Use the core SVG renderer for this composition.");}
                slots.Add(new HtmlPbSourceSlot{Attachment=attachment,Rect=CanvasRect(region.Bounds,frame),Clip=CanvasRect(region.Clip,frame),UV=new Vector4(0,0,1,1),Pose=pose,Opacity=1,Order=frame.SourceRegions.IndexOf(region)});
            }
            if(slots.Count>15)throw new ArgumentException("Mapped native HTML reserves one of the 16 source slots for its real LCD artwork.");return slots;
        }
        void PublishArtwork(HtmlPaintFrame frame,Vector2 texture,Vector2 viewport)
        {
            Vector4 rect=CanvasRect(new HtmlRect(0,0,frame.Width,frame.Height),frame);
            Vector4 uv=new Vector4((texture.X-viewport.X)*.5f/texture.X,(texture.Y-viewport.Y)*.5f/texture.Y,viewport.X/texture.X,viewport.Y/texture.Y);
            RelayCall("screen-slot",artworkSlot,"lcd-texture",source.EntityId.ToString(System.Globalization.CultureInfo.InvariantCulture)+":0",rect,rect,uv,-1,1.0);
            RelayCall("screen-slot-pose",artworkSlot,pose);
            RelayCall("screen-slot-refresh",artworkSlot,60.0);
        }
        Vector4 CanvasRect(HtmlRect r,HtmlPaintFrame frame)
        {
            return new Vector4((float)((r.X-frame.Width*.5)*units),(float)((frame.Height*.5-r.Y-r.Height)*units),(float)(r.Width*units),(float)(r.Height*units));
        }
        void PublishSources(List<HtmlPbSourceSlot> next,List<HtmlPbSourceSlot> previous)
        {
            if(previous!=null)foreach(var old in previous)if(!next.Exists(s=>s.Attachment.Slot==old.Attachment.Slot))RelayCall("screen-slot-remove",old.Attachment.Slot);
            foreach(var slot in next){var old=previous==null?null:previous.Find(s=>s.Attachment.Slot==slot.Attachment.Slot);if(slot.Same(old))continue;RelayCall("screen-slot",slot.Attachment.Slot,slot.Attachment.Provider,slot.Attachment.Source,slot.Rect,slot.Clip,slot.UV,slot.Order,slot.Opacity);RelayCall("screen-slot-pose",slot.Attachment.Slot,slot.Pose);HtmlPbSourceOptions.Apply(port,slot.Attachment);}
        }
        void RemoveSourceList(List<HtmlPbSourceSlot> slots){foreach(var slot in slots)try{RelayCall("screen-slot-remove",slot.Attachment.Slot);}catch{}}
        internal MyTuple<bool,string> SourceStatus(HtmlPbSourceAttachment attachment)
        {return committedSources.Exists(s=>s.Attachment.Slot==attachment.Slot)?(MyTuple<bool,string>)port.Draw("screen-slot-status",attachment.Slot):new MyTuple<bool,string>(false,"Hidden: source node has no visible content region.");}
        static bool Overlap(HtmlRect a,HtmlRect b){return !a.Empty&&!b.Empty&&a.X<b.X+b.Width&&b.X<a.X+a.Width&&a.Y<b.Y+b.Height&&b.Y<a.Y+a.Height;}
        static HtmlRect Intersection(HtmlRect a,HtmlRect b){double x=Math.Max(a.X,b.X),y=Math.Max(a.Y,b.Y);return new HtmlRect(x,y,Math.Max(0,Math.Min(a.X+a.Width,b.X+b.Width)-x),Math.Max(0,Math.Min(a.Y+a.Height,b.Y+b.Height)-y));}
        void RelayCall(string command,params object[] args)
        {port.DrawSelectedScreen(screen,command,args);}
        void ValidateRelay(Vector2 viewport,Vector2 texture)
        {
            if(!Mapped&&(Math.Abs(pose.Right.LengthSquared()-1)>1e-8||Math.Abs(pose.Up.LengthSquared()-1)>1e-8||Math.Abs(pose.Backward.LengthSquared()-1)>1e-8||Math.Abs(Vector3D.Dot(pose.Right,pose.Up))>1e-8||Math.Abs(Vector3D.Dot(pose.Right,pose.Backward))>1e-8||Math.Abs(Vector3D.Dot(pose.Up,pose.Backward))>1e-8||pose.Determinant()<.999999))throw new ArgumentException("Native texture relay needs a rigid right-handed pose.");
            double width=texture.X*units,height=texture.Y*units;
            MatrixD center=Mapped?pose:MatrixD.CreateTranslation(viewport.X*.5*units,-viewport.Y*.5*units,0)*pose;
            if(texture.X<32||texture.Y<32||texture.X>4096||texture.Y>4096||texture.X!=(int)texture.X||texture.Y!=(int)texture.Y)throw new ArgumentException("Native texture relay dimensions require integer sizes 32..4096.");
            if(width<.05||height<.05||width>12||height>12||width*height>100||center.Translation.Length()+Math.Sqrt(width*width+height*height)/2>25)throw new ArgumentException("Native texture relay exceeds the core's plane size or anchor range.");
        }
        void RemoveRelay(){if(!Relay)return;if(Mapped){RemoveSourceList(committedSources);committedSources.Clear();try{RelayCall("screen-slot-remove",artworkSlot);}catch{}}else try{port.DrawSelectedScreen(screen,"screen-remove",new object[0]);}catch{}relayDeclared=false;committedFrame=null;}
        public void Reset(){native.Reset();RemoveRelay();}
        public void Dispose()
        {
            if(disposed)return;
            cleanup=true;
            try{port.RetireOwned(()=>{native.Dispose();RemoveRelay();});}
            finally{HtmlPbNativeSurfaceClaims.Release(surface,token);claimed=false;disposed=true;cleanup=false;}
        }
    }
}
