using System;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  static void ValidateRasterRequest(Vector2I size)
  {if(size.X<16||size.Y<16||size.X>4096||size.Y>4096||(double)size.X/size.Y>64||(double)size.Y/size.X>64)throw new ArgumentException("Requested raster size must be 16–4096 per side, with aspect at most 64:1. Client output is capped separately.");}
  static SurfaceMesh ScreenFootprint(HoloProjectedScreenData d)
  {
   if(d.SurfaceKind==0)
   {
    var quad=RasterQuad(d.CanvasWidth,d.CanvasHeight);var mapping=ProjectedCanvas(d,0,d.CanvasWidth,d.CanvasHeight)*MatrixD.Invert(ReadMatrix(d.Pose));
    quad=TransformCanvasMesh(quad,mapping);
    return MeshEffects.Clip(quad,new[]{new Vector4(1,0,0,(float)(d.Width/2)),new Vector4(-1,0,0,(float)(d.Width/2)),new Vector4(0,1,0,(float)(d.Height/2)),new Vector4(0,-1,0,(float)(d.Height/2))});
   }
   if(d.SurfaceKind==4)return new SurfaceMesh{Geometry=new Geometry(SurfacePoints(d),new int[0],d.SurfaceMeshTriangles),UV=SurfaceUV(d)};
   double hspan=d.SurfaceHorizontal,vspan=d.SurfaceVertical;
   if(d.SurfaceMapping==2){vspan=d.Camera[9];hspan=2*Math.Atan(Math.Tan(vspan/2)*d.UiRasterWidth/d.UiRasterHeight);}
   if(d.SurfaceKind==1&&d.SurfaceMapping==1)hspan=d.CanvasWidth/d.SurfaceRadius;
   int nx=Math.Max(1,(int)Math.Ceiling(hspan/(Math.PI/8))),ny=(d.SurfaceKind==2||d.SurfaceKind==3)?Math.Max(1,(int)Math.Ceiling(vspan/(Math.PI/8))):1;
   var points=new Vector3D[(nx+1)*(ny+1)];var uv=new Vector2[points.Length];var triangles=new int[nx*ny*6];
   var style=ScreenSurfaceStyle(d,(double)d.UiRasterWidth/d.UiRasterHeight);
   for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)
   {
    int i=y*(nx+1)+x;var p=new Vector3D((x/(double)nx-.5)*d.CanvasWidth,(y/(double)ny-.5)*d.CanvasHeight,0);
    p=SurfaceMapping.MapPoint(p,d.CanvasWidth,d.CanvasHeight,style);if(d.SurfaceKind==1)p.Y*=d.Height/d.CanvasHeight;if(d.SurfaceSide==0)p.X=-p.X;
    points[i]=p;uv[i]=new Vector2(x/(float)nx,1-y/(float)ny);
   }
   for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
   {int i=(y*nx+x)*6,p=y*(nx+1)+x,q=p+nx+1;triangles[i]=p;triangles[i+1]=p+1;triangles[i+2]=q+1;triangles[i+3]=p;triangles[i+4]=q+1;triangles[i+5]=q;}
   return new SurfaceMesh{Geometry=new Geometry(points,new int[0],triangles),UV=uv};
  }
  static SurfaceMesh ScreenFootprintBounds(HoloProjectedScreenData d)
  {
   if(d.SurfaceKind==4)return ScreenFootprint(d);
   if(d.SurfaceKind==3)
   {
    var r=SurfaceRadii(d);double rmax=Math.Max(r.X,Math.Max(r.Y,r.Z)),rmin=Math.Min(r.X,Math.Min(r.Y,r.Z));double hh=d.SurfaceHorizontal/2,vv=d.SurfaceVertical/2,xx,yy,zz;
    if(d.SurfaceMapping==2){double ty=Math.Tan(d.Camera[9]/2),tx=ty*d.UiRasterWidth/d.UiRasterHeight;xx=Math.Min(r.X,rmax*tx/Math.Sqrt(1+tx*tx));yy=Math.Min(r.Y,rmax*ty/Math.Sqrt(1+ty*ty));zz=rmin/Math.Sqrt(1+tx*tx+ty*ty);}
    else{xx=Math.Min(r.X,rmax*Math.Sin(Math.Min(hh,Math.PI/2)));yy=Math.Min(r.Y,rmax*Math.Sin(vv));double minimum=Math.Cos(hh);if(minimum>0)minimum*=Math.Cos(vv);zz=minimum*(minimum<0?rmax:rmin);zz=Math.Max(-r.Z,zz);}
    var b=new Vector3D[8];for(int i=0;i<8;i++)b[i]=new Vector3D((i&1)==0?-xx-.01:xx+.01,(i&2)==0?-yy-.01:yy+.01,(i&4)==0?zz-.01:r.Z+.01);
    return new SurfaceMesh{Geometry=new Geometry(b,new int[0],new[]{0,1,3,0,3,2,4,6,7,4,7,5,0,4,5,0,5,1,2,3,7,2,7,6,0,2,6,0,6,4,1,5,7,1,7,3})};
   }
   double radius=d.SurfaceRadius,h=d.SurfaceHorizontal/2,v=d.SurfaceVertical/2,x,y,z;
   if(d.SurfaceKind==1)
   {if(d.SurfaceMapping==1)h=d.CanvasWidth/radius/2;x=radius*Math.Sin(Math.Min(h,Math.PI/2));y=d.Height/2;z=radius*Math.Cos(h);}
   else if(d.SurfaceMapping==2)
   {double ty=Math.Tan(d.Camera[9]/2),tx=ty*d.UiRasterWidth/d.UiRasterHeight;x=radius*tx/Math.Sqrt(1+tx*tx);y=radius*ty/Math.Sqrt(1+ty*ty);z=radius/Math.Sqrt(1+tx*tx+ty*ty);}
   else if(d.SurfaceMapping==1)
   {x=radius*Math.Sin(Math.Min(h,Math.PI/2));y=radius*Math.Sin(Math.Min(v,Math.PI/2));z=radius*Math.Cos(Math.Sqrt(h*h+v*v));}
   else
   {x=radius*Math.Sin(Math.Min(h,Math.PI/2));y=radius*Math.Sin(v);z=radius*Math.Cos(h);if(z>0)z*=Math.Cos(v);}
   // Analytic bounds contain the actual curved patch, including the bulge
   // between proxy vertices. A proxy rejection alone must not hide that patch.
   const double layerMargin=.01;var points=new Vector3D[8];for(int i=0;i<8;i++)points[i]=new Vector3D((i&1)==0?-x-layerMargin:x+layerMargin,(i&2)==0?-y-layerMargin:y+layerMargin,(i&4)==0?z-layerMargin:radius+layerMargin);
   return new SurfaceMesh{Geometry=new Geometry(points,new int[0],new[]{0,1,3,0,3,2,4,6,7,4,7,5,0,4,5,0,5,1,2,3,7,2,7,6,0,2,6,0,6,4,1,5,7,1,7,3})};
  }
  static Vector4[] ScreenCropPlanes(HoloProjectedScreenData d)
  {
   if(d.SurfaceClip==null||d.SurfaceClip.Length==0)return new Vector4[0];
   var pose=ReadMatrix(d.Pose);var result=new Vector4[d.SurfaceClip.Length/4];
   for(int i=0;i<result.Length;i++)
   {
    int p=i*4;var n=new Vector3D(d.SurfaceClip[p],d.SurfaceClip[p+1],d.SurfaceClip[p+2]);double length=n.Length();
    // MeshEffects keeps dot(n,p)+W >= 0; screen crops keep dot(n,p) <= W
    // in the anchor frame. Transform and negate the crop's normal.
    result[i]=new Vector4((float)(-Vector3D.Dot(pose.Right,n)/length),(float)(-Vector3D.Dot(pose.Up,n)/length),
     (float)(-Vector3D.Dot(pose.Backward,n)/length),(float)((d.SurfaceClip[p+3]-Vector3D.Dot(pose.Translation,n))/length));
   }
   return result;
  }
  static bool SameAdaptiveSurface(HoloProjectedScreenData a,HoloProjectedScreenData b)
  {
   if(!SameSurface(a,b))return false;int count=a.SurfaceClip==null?0:a.SurfaceClip.Length;
   if(count!=(b.SurfaceClip==null?0:b.SurfaceClip.Length))return false;
   for(int i=0;i<count;i++)if(a.SurfaceClip[i]!=b.SurfaceClip[i])return false;
   return count==0||ReadMatrix(a.Pose)==ReadMatrix(b.Pose);
  }
  static bool OutsideScreenCrop(SurfaceMesh mesh,Vector4[] planes)
  {
   foreach(var p in planes)
   {bool outside=true;foreach(var v in mesh.Geometry.Points)if(v.X*p.X+v.Y*p.Y+v.Z*p.Z+p.W>=-1e-6){outside=false;break;}if(outside)return true;}
   return false;
  }
  DisplayLodResult ScreenLod(HoloProjectedScreenData d,IMyTerminalBlock anchor,ProjectedCache cache)
  {
   var requested=new Vector2I(d.UiRasterWidth,d.UiRasterHeight);DisplayLodResult result;
   try
   {
    var camera=MyAPIGateway.Session.Camera;var view=camera.ViewMatrix*camera.ProjectionMatrix;var viewport=new Vector2I((int)Math.Round(camera.ViewportSize.X),(int)Math.Round(camera.ViewportSize.Y));var world=ReadMatrix(d.Pose)*anchor.WorldMatrix;
    if(cache.AdaptiveKnown&&SameAdaptiveSurface(cache.AdaptiveData,d)&&cache.AdaptiveWorld==world&&cache.AdaptiveView==view&&cache.AdaptiveViewport==viewport)return cache.Adaptive;
    if(cache.AdaptiveMesh==null||!SameAdaptiveSurface(cache.AdaptiveData,d))
    {
     var footprint=ScreenFootprint(d);var bounds=d.SurfaceKind==0?null:ScreenFootprintBounds(d);var crop=ScreenCropPlanes(d);
     bool outside=OutsideScreenCrop(bounds??footprint,crop);
     if(crop.Length>0&&footprint.Colors==null)footprint.Colors=new Vector4[footprint.Geometry.Triangles.Length/3];
     cache.AdaptiveMesh=MeshEffects.Clip(footprint,crop);cache.AdaptiveBounds=outside?null:bounds;
    }
    var g=cache.AdaptiveMesh.Geometry;var clip=world*view;
    result=g.Triangles.Length==0?new DisplayLodResult(false,Vector2I.Zero,-1):DisplayLod.Evaluate(g.Points,g.Triangles,cache.AdaptiveMesh.UV,clip,viewport,requested,cache.AdaptiveKnown?cache.Adaptive.Level:-1,WideSourceRaster(d.SourceProvider));
    if(!result.Visible&&cache.AdaptiveBounds!=null){var bounds=cache.AdaptiveBounds.Geometry;if(DisplayLod.Evaluate(bounds.Points,bounds.Triangles,clip,viewport,requested,-1,WideSourceRaster(d.SourceProvider)).Visible)result=DisplayLod.MaximumVisible(requested,WideSourceRaster(d.SourceProvider));}
    cache.AdaptiveWorld=world;cache.AdaptiveView=view;cache.AdaptiveViewport=viewport;
   }
   catch{result=DisplayLod.MaximumVisible(requested,WideSourceRaster(d.SourceProvider));}
   bool changed=cache.AdaptiveKnown&&cache.Adaptive.Resolution!=result.Resolution;
   bool retainPortal=d.SourceProvider=="native-portal"&&cache.AdaptiveKnown&&cache.Adaptive.Visible&&result.Visible;
   cache.Adaptive=result;cache.AdaptiveKnown=true;cache.AdaptiveData=CloneScreen(d);
   if(changed){ReleaseUiRaster(cache);if(!retainPortal)ClearExternalBudgetView(cache);cache.NextSample=cache.NextCapture=-1;}
   return result;
  }
  static bool WideSourceRaster(string provider){return provider=="camera-panorama"||provider=="native-portal";}
  static Vector2I ScreenRasterSize(HoloProjectedScreenData d,ProjectedCache cache)
  {return cache.AdaptiveKnown?cache.Adaptive.Resolution:DisplayLod.MaximumVisible(new Vector2I(d.UiRasterWidth,d.UiRasterHeight),WideSourceRaster(d.SourceProvider)).Resolution;}
  double SourceRefreshLimit(HoloProjectedScreenData d)
  {return WideSourceRaster(d.SourceProvider)?d.RefreshHz:Math.Min(d.RefreshHz,ClientLcdRefreshCap);}
  MyTuple<int,int,double,bool> DisplaySourceDemand(string name,long anchorId,long caller,string source)
  {
   var empty=new MyTuple<int,int,double,bool>(0,0,0,false);DisplayProvider provider;
   if(name==null||!_displayProviders.TryGetValue(name,out provider)||provider.Endpoint==null||!ClientRenderingEnabled||MyAPIGateway.Utilities.IsDedicated||MyAPIGateway.Session==null||MyAPIGateway.Session.Camera==null)return empty;
   Scene scene;if(!_scenes.TryGetValue(anchorId,out scene))return empty;var anchor=MyAPIGateway.Entities.GetEntityById(anchorId) as IMyTerminalBlock;
   if(anchor==null||anchor.Closed||!anchor.IsWorking||Vector3D.DistanceSquared(MyAPIGateway.Session.Camera.Position,anchor.GetPosition())>DrawDistance*DrawDistance)return empty;
   int width=0,height=0;double rate=0;
   foreach(var screen in scene.Screens.Values)
   {
    var d=screen.Data;if(d.CallerId!=caller||!ExternalBudgetScreenVisible(d)||!ScreenSurfaceVisible(d,anchor.WorldMatrix,MyAPIGateway.Session.Camera.Position))continue;
    string key=PackedKey(d.CallerId,anchorId,d.Id);ProjectedCache cache;if(!_projectedCaches.TryGetValue(key,out cache)){cache=new ProjectedCache{Anchor=anchorId,Caller=caller,Id=d.Id};_projectedCaches.Add(key,cache);}
    var lod=ScreenLod(d,anchor,cache);if(!lod.Visible)continue;
    if(d.SourceProvider==name&&d.SourceId==source){width=Math.Max(width,lod.Resolution.X);height=Math.Max(height,lod.Resolution.Y);rate=Math.Max(rate,SourceRefreshLimit(d));}
    if(d.SourceSlots!=null)foreach(var slot in d.SourceSlots)
    {if(slot.Provider!=name||slot.SourceId!=source||!SourceSlotVisible(scene,d,slot))continue;var requested=SourceSlotDemandSize(d,slot,lod.Resolution);width=Math.Max(width,requested.X);height=Math.Max(height,requested.Y);rate=Math.Max(rate,SourceRefreshLimit(SourceSlotSettings(d,slot)));}
   }
   if(width==0||height==0)return empty;var bounded=DisplayLod.MaximumVisible(new Vector2I(width,height),WideSourceRaster(name)).Resolution;return new MyTuple<int,int,double,bool>(bounded.X,bounded.Y,rate,true);
  }
  MyTuple<int,int,double,bool> DisplaySourceDemandAny(string name,string source)
  {
   int width=0,height=0;double rate=0;
   foreach(var scene in _scenes.Values)
   {
    var owners=new System.Collections.Generic.HashSet<long>();
    foreach(var screen in scene.Screens.Values)
    {var d=screen.Data;bool matches=d.SourceProvider==name&&d.SourceId==source;if(d.SourceSlots!=null)foreach(var slot in d.SourceSlots)if(slot.Provider==name&&slot.SourceId==source)matches=true;if(!matches||!owners.Add(d.CallerId))continue;var demand=DisplaySourceDemand(name,scene.ConsoleId,d.CallerId,source);if(!demand.Item4)continue;width=Math.Max(width,demand.Item1);height=Math.Max(height,demand.Item2);rate=Math.Max(rate,demand.Item3);}
   }
   return new MyTuple<int,int,double,bool>(width,height,rate,width>0&&height>0);
  }
 }
}
