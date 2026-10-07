using System;
using ProtoBuf;
using Sandbox.ModAPI;
using VRageMath;

namespace HoloMap
{
 [ProtoContract] public sealed class HoloPortalData
 {
  [ProtoMember(1)] public int ExitKind;
  [ProtoMember(2)] public double[] ExitPose;
  [ProtoMember(3,OverwriteList=true)] public double[] ExitExtent=new[]{2d,1.125,0};
  [ProtoMember(4)] public double[] ExitPoints;
  [ProtoMember(5)] public int[] ExitTriangles;
  [ProtoMember(6)] public double[] ExitUV;
  [ProtoMember(7)] public double[] ShellPose;
  [ProtoMember(8,OverwriteList=true)] public double[] ShellRadii=new[]{10d,10,10};
  [ProtoMember(9)] public int Transport;
  [ProtoMember(10)] public int Accuracy;
  [ProtoMember(11,IsRequired=true)] public double NormalScale=1;
  [ProtoMember(12,IsRequired=true)] public double Saturation=1;
  [ProtoMember(13,IsRequired=true)] public double Brightness=1;
  [ProtoMember(14)] public int EntryChart;
  [ProtoMember(15)] public int ExitChart;
 }
 public sealed partial class HoloMapSession
 {
  static HoloPortalData NewPortal()
  {return new HoloPortalData{ExitPose=MatrixValues(MatrixD.Identity),ShellPose=MatrixValues(MatrixD.Identity)};}
  static HoloPortalData ClonePortal(HoloPortalData p,bool deep)
  {
   if(p==null)return null;
   return new HoloPortalData{ExitKind=p.ExitKind,ExitPose=PortalArray(p.ExitPose,deep),ExitExtent=PortalArray(p.ExitExtent,deep),ExitPoints=PortalArray(p.ExitPoints,deep),ExitTriangles=PortalArray(p.ExitTriangles,deep),ExitUV=PortalArray(p.ExitUV,deep),ShellPose=PortalArray(p.ShellPose,deep),ShellRadii=PortalArray(p.ShellRadii,deep),Transport=p.Transport,Accuracy=p.Accuracy,NormalScale=p.NormalScale,Saturation=p.Saturation,Brightness=p.Brightness,EntryChart=p.EntryChart,ExitChart=p.ExitChart};
  }
  static T[] PortalArray<T>(T[] p,bool deep){return p==null?null:deep?(T[])p.Clone():p;}
  static void PortalBound(double x,double lo,double hi)
  {if(!Geometry.Finite(x)||x<lo||x>hi)throw new ArgumentException("Portal setting is nonfinite or outside its documented bounds.");}
  static MatrixD PortalPose(double[] values)
  {
   var p=ReadMatrix(values);foreach(double x in values)PortalBound(x,-1000000,1000000);
   if(Math.Abs(p.Right.LengthSquared()-1)>1e-8||Math.Abs(p.Up.LengthSquared()-1)>1e-8||Math.Abs(p.Backward.LengthSquared()-1)>1e-8||Math.Abs(Vector3D.Dot(p.Right,p.Up))>1e-8||Math.Abs(Vector3D.Dot(p.Right,p.Backward))>1e-8||Math.Abs(Vector3D.Dot(p.Up,p.Backward))>1e-8||Math.Abs(p.Determinant()-1)>1e-8||p.M14!=0||p.M24!=0||p.M34!=0||p.M44!=1)throw new ArgumentException("Portal poses must be rigid right-handed affine transforms.");return p;
  }
  static void ValidatePortalShape(int kind,double[] extent,double[] points,int[] indices,double[] uv,int chart)
  {
   if(kind<0||kind>3||extent==null||extent.Length!=3)throw new ArgumentException("Portal shape requires a plane, ellipsoid or UV mesh.");
   if(kind!=2)
   {
    if(points!=null&&points.Length!=0||indices!=null&&indices.Length!=0||uv!=null&&uv.Length!=0||chart!=0)throw new ArgumentException("Analytic portal shapes have no mesh payload or chart.");
    if(kind==0){PortalBound(extent[0],.1,500);PortalBound(extent[1],.1,500);if(extent[2]!=0)throw new ArgumentException("Portal plane extent Z is zero.");}
    else foreach(double r in extent)PortalBound(r,.05,250);return;
   }
   if(extent[0]!=0||extent[1]!=0||extent[2]!=0||points==null||points.Length<9||points.Length%3!=0||points.Length>2048*3||indices==null||indices.Length<3||indices.Length%3!=0||indices.Length>4096*3||uv==null||uv.Length!=points.Length/3*2||chart<0||chart>=indices.Length/3)throw new ArgumentException("Invalid bounded portal mesh or chart.");
   var p=new Vector3D[points.Length/3];for(int i=0;i<p.Length;i++){p[i]=new Vector3D(points[i*3],points[i*3+1],points[i*3+2]);if(!Geometry.Finite(p[i].X)||!Geometry.Finite(p[i].Y)||!Geometry.Finite(p[i].Z)||p[i].LengthSquared()>250*250)throw new ArgumentException("Portal mesh point exceeds its 250 meter bound.");PortalBound(uv[i*2],0,1);PortalBound(uv[i*2+1],0,1);}
   foreach(int index in indices)if(index<0||index>=p.Length)throw new ArgumentException("Invalid portal triangle index.");
   for(int t=0;t<indices.Length;t+=3){int a=indices[t],b=indices[t+1],c=indices[t+2];var e=p[b]-p[a];var f=p[c]-p[a];double det=(uv[b*2]-uv[a*2])*(uv[c*2+1]-uv[a*2+1])-(uv[b*2+1]-uv[a*2+1])*(uv[c*2]-uv[a*2]);double lengths=e.LengthSquared()*f.LengthSquared();if(Math.Abs(det)<1e-10||lengths<=1e-20||Vector3D.Cross(e,f).LengthSquared()<=1e-12*lengths)throw new ArgumentException("Portal mesh has a degenerate physical or UV triangle.");}
  }
  static int PortalEntryKind(HoloProjectedScreenData d)
  {return d.SurfaceKind==0?0:d.SurfaceKind==2||d.SurfaceKind==3?(d.SurfaceSide==0?1:3):d.SurfaceKind==4?2:-1;}
  static double[] PortalEntryExtent(HoloProjectedScreenData d)
  {var r=SurfaceRadii(d);return d.SurfaceKind==0?new[]{d.Width,d.Height,0d}:d.SurfaceKind==4?new double[3]:new[]{r.X,r.Y,r.Z};}
  static double[] PortalEntryUV(HoloProjectedScreenData d)
  {if(d.SurfaceMeshUV==null)return null;var uv=new double[d.SurfaceMeshUV.Length];for(int i=0;i<uv.Length;i++)uv[i]=d.SurfaceMeshUV[i];return uv;}
  static void ValidatePortalSource(HoloProjectedScreenData d)
  {
   var p=d.Portal;if(p==null){if(d.SourceProvider=="native-portal")throw new ArgumentException("Declare a paired portal before selecting native-portal.");return;}
   PortalPose(p.ExitPose);PortalPose(p.ShellPose);ValidatePortalShape(p.ExitKind,p.ExitExtent,p.ExitPoints,p.ExitTriangles,p.ExitUV,p.ExitChart);
   if(p.ShellRadii==null||p.ShellRadii.Length!=3)throw new ArgumentException("Portal capture shell requires three radii.");foreach(double r in p.ShellRadii)PortalBound(r,.05,250);
   if(p.Transport<0||p.Transport>1||p.Accuracy<0||p.Accuracy>1)throw new ArgumentException("Portal transport is differential or stealth; accuracy is strict or approximate.");PortalBound(Math.Abs(p.NormalScale),1d/64,64);PortalBound(p.Saturation,0,2);PortalBound(p.Brightness,0,4);
   if(d.SourceProvider!="native-portal")return;
   if(d.SourceId!=d.Id)throw new ArgumentException("Native portal source ID must equal its consumer screen ID.");
   if(d.SurfaceKind==0&&d.Aspect!="stretch"&&Math.Abs(d.Width/d.CanvasWidth-d.Height/d.CanvasHeight)>1e-8)throw new ArgumentException("Portal plane aspect must preserve the complete physical entry; use stretch.");
   ValidatePortalShape(PortalEntryKind(d),PortalEntryExtent(d),d.SurfaceMeshPoints,d.SurfaceMeshTriangles,PortalEntryUV(d),p.EntryChart);
   if(d.SurfaceKind==4&&d.SurfaceMeshTriangles.Length!=3)throw new ArgumentException("Native portal entry currently supports one authored UV triangle per screen; use separate screens for separate charts.");
   if(p.Transport==1&&(PortalEntryKind(d)!=p.ExitKind||!SameSurfaceArray(d.Pose,p.ExitPose)||!SameSurfaceArray(PortalEntryExtent(d),p.ExitExtent)||!SameSurfaceArray(d.SurfaceMeshPoints,p.ExitPoints)||!SameSurfaceArray(d.SurfaceMeshTriangles,p.ExitTriangles)||!SameSurfaceArray(PortalEntryUV(d),p.ExitUV)||p.EntryChart!=p.ExitChart))throw new ArgumentException("Stealth transport requires identical coincident entry and exit charts. Configure the pair first, then select stealth.");
   if((d.SurfaceKind==2||d.SurfaceKind==3)&&(d.SurfaceMapping!=0||Math.Abs(d.SurfaceHorizontal-2*Math.PI)>1e-8||Math.Abs(d.SurfaceVertical-Math.PI)>1e-8)||d.SurfaceKind==0&&d.SurfaceMapping!=0||d.PanoramaGroup!=null)throw new ArgumentException("Portal entry requires a plane, full angular ellipsoid, or authored UV mesh.");
   PortalBound(d.RefreshHz,1,120);if(d.UiRasterWidth<64||d.UiRasterHeight<64||d.UiRasterWidth>4096||d.UiRasterHeight>4096)throw new ArgumentException("Portal output dimensions require 64–4096 pixels per axis.");
  }
  object PortalSourceDescriptor(object[] args)
  {
   if(args==null||args.Length!=4||!(args[0] is string)||(string)args[0]!="native-portal"||!(args[1] is long)||!(args[2] is long)||!(args[3] is string))return false;
   long anchorId=(long)args[1],caller=(long)args[2];string source=(string)args[3];Scene scene;ProjectedScreen screen;
   if(!_scenes.TryGetValue(anchorId,out scene)||!scene.Screens.TryGetValue(Key(caller,source),out screen))return false;var d=screen.Data;
   if(d.SourceProvider!="native-portal"||d.SourceId!=source||!DisplaySourceVisible("native-portal",anchorId,caller,source))return false;
   var anchor=MyAPIGateway.Entities.GetEntityById(anchorId) as IMyProjector;if(anchor==null||anchor.Closed||!anchor.IsWorking)return false;
   try
   {
    ValidatePortalSource(d);var p=d.Portal;var world=anchor.WorldMatrix;
    return new object[]{1,p.Transport,p.Accuracy,PortalEntryKind(d),MatrixValues(ReadMatrix(d.Pose)*world),PortalEntryExtent(d),PortalArray(d.SurfaceMeshPoints,true)??new double[0],PortalArray(d.SurfaceMeshTriangles,true)??new int[0],PortalEntryUV(d)??new double[0],p.ExitKind,MatrixValues(PortalPose(p.ExitPose)*world),PortalArray(p.ExitExtent,true),PortalArray(p.ExitPoints,true)??new double[0],PortalArray(p.ExitTriangles,true)??new int[0],PortalArray(p.ExitUV,true)??new double[0],MatrixValues(PortalPose(p.ShellPose)*world),PortalArray(p.ShellRadii,true),new[]{p.NormalScale,d.RefreshHz,p.Saturation,p.Brightness},new[]{d.UiRasterWidth,d.UiRasterHeight,p.EntryChart,p.ExitChart}};
   }
   catch(ArgumentException){return false;}
  }
 }
}
