using System;
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
        readonly MatrixD pose;
        readonly double units;
        bool claimed,relayDeclared,disposed,cleanup;
        Vector2 relayTexture,relayViewport;
        internal bool Relay{get{return !ReferenceEquals(source,anchor);}}
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
            this.port=port;this.caller=caller;this.anchor=anchor;this.source=source;surface=source;this.pose=pose;this.units=units;screen="h"+handle.ToString("x",System.Globalization.CultureInfo.InvariantCulture);
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
            if(!native.TryPaint(frame,out report))return false;
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
        void RelayCall(string command,params object[] args)
        {port.DrawSelectedScreen(screen,command,args);}
        void ValidateRelay(Vector2 viewport,Vector2 texture)
        {
            if(Math.Abs(pose.Right.LengthSquared()-1)>1e-8||Math.Abs(pose.Up.LengthSquared()-1)>1e-8||Math.Abs(pose.Backward.LengthSquared()-1)>1e-8||Math.Abs(Vector3D.Dot(pose.Right,pose.Up))>1e-8||Math.Abs(Vector3D.Dot(pose.Right,pose.Backward))>1e-8||Math.Abs(Vector3D.Dot(pose.Up,pose.Backward))>1e-8||pose.Determinant()<.999999)throw new ArgumentException("Native texture relay needs a rigid right-handed pose.");
            double width=texture.X*units,height=texture.Y*units;
            MatrixD center=MatrixD.CreateTranslation(viewport.X*.5*units,-viewport.Y*.5*units,0)*pose;
            if(texture.X<32||texture.Y<32||texture.X>4096||texture.Y>4096||texture.X!=(int)texture.X||texture.Y!=(int)texture.Y)throw new ArgumentException("Native texture relay dimensions require integer sizes 32..4096.");
            if(width<.05||height<.05||width>12||height>12||width*height>100||center.Translation.Length()+Math.Sqrt(width*width+height*height)/2>25)throw new ArgumentException("Native texture relay exceeds the core's plane size or anchor range.");
        }
        void RemoveRelay(){if(!Relay)return;try{port.DrawSelectedScreen(screen,"screen-remove",new object[0]);}catch{}relayDeclared=false;}
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
