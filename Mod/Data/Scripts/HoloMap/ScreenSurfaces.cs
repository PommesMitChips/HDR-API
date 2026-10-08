using System;
using System.Collections.Generic;
using VRageMath;

namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  sealed class MappedScreenMesh
  {
   public SurfaceMesh Source,Mesh;public MatrixD Transform;public HoloProjectedScreenData Settings;
   public double Depth,Aspect;public Vector4 Fill,Line;public bool BaselineCounted;public int Seen,RetryTick;public string Error;
   public PanoramaLens[] Panorama;
  }
  static bool SameSurface(HoloProjectedScreenData a,HoloProjectedScreenData b)
  {return a!=null&&a.SurfaceKind==b.SurfaceKind&&a.SurfaceMapping==b.SurfaceMapping&&a.SurfaceRadius==b.SurfaceRadius&&a.SurfaceHorizontal==b.SurfaceHorizontal&&a.SurfaceVertical==b.SurfaceVertical&&a.SurfaceSide==b.SurfaceSide&&a.SurfaceError==b.SurfaceError&&a.CanvasWidth==b.CanvasWidth&&a.CanvasHeight==b.CanvasHeight&&a.Width==b.Width&&a.Height==b.Height&&a.Aspect==b.Aspect&&a.UiRasterWidth==b.UiRasterWidth&&a.UiRasterHeight==b.UiRasterHeight&&a.Camera[9]==b.Camera[9]&&SameSurfaceArray(a.SurfaceRadii,b.SurfaceRadii)&&SameSurfaceArray(a.SurfaceMeshPoints,b.SurfaceMeshPoints)&&SameSurfaceArray(a.SurfaceMeshTriangles,b.SurfaceMeshTriangles)&&SameSurfaceArray(a.SurfaceMeshUV,b.SurfaceMeshUV);}
  static bool SameSurfaceArray<T>(T[] a,T[] b)
  {if(ReferenceEquals(a,b))return true;if(a==null||b==null||a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(!EqualityComparer<T>.Default.Equals(a[i],b[i]))return false;return true;}
  static Vector3D SurfaceRadii(HoloProjectedScreenData d)
  {if(d.SurfaceRadii==null)return new Vector3D(d.SurfaceRadius);if(d.SurfaceRadii.Length!=3)throw new ArgumentException("Ellipsoid requires three radii.");return new Vector3D(d.SurfaceRadii[0],d.SurfaceRadii[1],d.SurfaceRadii[2]);}
  static Vector3D[] SurfacePoints(HoloProjectedScreenData d)
  {
   var data=d.SurfaceMeshPoints;if(data==null)return null;if(data.Length<9||data.Length%3!=0||data.Length>Geometry.MaxPoints*3)throw new ArgumentException("Invalid authored display points.");
   if(ReferenceEquals(data,d.SurfacePointCacheSource))return d.SurfacePointCache;var result=new Vector3D[data.Length/3];for(int i=0;i<result.Length;i++)result[i]=new Vector3D(data[i*3],data[i*3+1],data[i*3+2]);d.SurfacePointCacheSource=data;return d.SurfacePointCache=result;
  }
  static Vector2[] SurfaceUV(HoloProjectedScreenData d)
  {
   var data=d.SurfaceMeshUV;if(data==null)return null;if(data.Length%2!=0||data.Length>Geometry.MaxPoints*2)throw new ArgumentException("Invalid authored display UVs.");
   if(ReferenceEquals(data,d.SurfaceUvCacheSource))return d.SurfaceUvCache;var result=new Vector2[data.Length/2];for(int i=0;i<result.Length;i++)result[i]=new Vector2(data[i*2],data[i*2+1]);d.SurfaceUvCacheSource=data;return d.SurfaceUvCache=result;
  }
  static SurfaceStyle ScreenSurfaceStyle(HoloProjectedScreenData d,double aspect=0)
  {
   return new SurfaceStyle{Kind=(SurfaceKind)d.SurfaceKind,Mapping=(SurfaceMappingMode)d.SurfaceMapping,Radius=d.SurfaceRadius,Radii=SurfaceRadii(d),MeshPoints=SurfacePoints(d),MeshTriangles=d.SurfaceMeshTriangles,MeshUV=SurfaceUV(d),HorizontalRadians=d.SurfaceHorizontal,VerticalRadians=d.SurfaceVertical,Inward=d.SurfaceSide==0,MaxError=d.SurfaceError,MaxWork=200000,SourceAspect=d.SurfaceMapping==2?(double)d.UiRasterWidth/d.UiRasterHeight:aspect,VerticalFovRadians=d.Camera[9]};
  }
  static PanoramaLens PanoramaCamera(HoloProjectedScreenData d)
  {return new PanoramaLens{Id=d.Id,Frame=ReadMatrix(d.Pose),VerticalFov=d.Camera[9],Aspect=(double)d.UiRasterWidth/d.UiRasterHeight,Inward=d.SurfaceSide==0};}
  static PanoramaLens[] PanoramaCameras(Scene scene,HoloProjectedScreenData d)
  {
   if(d.PanoramaGroup==null)return null;var result=new List<PanoramaLens>();var centre=ReadMatrix(d.Pose).Translation;
   foreach(var screen in scene.Screens.Values)
   {
    var p=screen.Data;
    if(p.CallerId!=d.CallerId||p.PanoramaGroup!=d.PanoramaGroup||!p.Visible||p.Opacity<=0||!HasExternalSource(p)||
       (p.SurfaceKind!=2&&p.SurfaceKind!=3)||p.SurfaceMapping!=2||p.SurfaceSide!=d.SurfaceSide||!SameSurfaceArray(p.SurfaceRadii,d.SurfaceRadii)||Math.Abs(p.SurfaceRadius-d.SurfaceRadius)>1e-8||
       Vector3D.DistanceSquared(ReadMatrix(p.Pose).Translation,centre)>1e-12)continue;
    result.Add(PanoramaCamera(p));
   }
   result.Sort((a,b)=>string.CompareOrdinal(a.Id,b.Id));return result.ToArray();
  }
  static bool SamePanorama(PanoramaLens[] a,PanoramaLens[] b)
  {
   if(a==null||b==null)return a==b;if(a.Length!=b.Length)return false;
   for(int i=0;i<a.Length;i++)if(a[i].Id!=b[i].Id||a[i].Frame!=b[i].Frame||a[i].VerticalFov!=b[i].VerticalFov||a[i].Aspect!=b[i].Aspect||a[i].Inward!=b[i].Inward)return false;return true;
  }
  static void ValidateScreenSurface(HoloProjectedScreenData d,MatrixD pose)
  {
   if(d.SurfaceSide<0||d.SurfaceSide>1||d.ContentRenderer<0||d.ContentRenderer>1)throw new ArgumentException("Invalid surface side or content renderer.");
   if(d.SurfaceKind!=3&&d.SurfaceRadii!=null||d.SurfaceKind!=4&&(d.SurfaceMeshPoints!=null||d.SurfaceMeshTriangles!=null||d.SurfaceMeshUV!=null))throw new ArgumentException("Surface payload does not match its selected shape.");
   if(d.SurfaceKind==3&&d.SurfaceRadii==null)throw new ArgumentException("Ellipsoid requires three explicit radii.");
   SurfaceMapping.ValidateStyle(ScreenSurfaceStyle(d));
   if(d.SurfaceKind==1&&d.SurfaceMapping==1&&d.CanvasWidth/d.SurfaceRadius>Math.PI*2)throw new ArgumentException("Geodesic cylinder canvas must cover at most one full turn.");
   double extent=d.SurfaceKind==1?Math.Sqrt(d.SurfaceRadius*d.SurfaceRadius+d.Height*d.Height/4):d.SurfaceRadius;
   if(d.SurfaceKind==3){var r=SurfaceRadii(d);if(Math.Min(r.X,Math.Min(r.Y,r.Z))<.05)throw new ArgumentException("Ellipsoid radii must be .05–25 meters.");extent=Math.Max(r.X,Math.Max(r.Y,r.Z));}
   if(d.SurfaceKind==4){extent=0;foreach(var p in SurfacePoints(d))extent=Math.Max(extent,p.Length());}
   if(d.SurfaceKind!=0&&(d.SurfaceRadius<.05||d.SurfaceRadius>25||pose.Translation.Length()+extent>25||extent>25))throw new ArgumentException("Curved surface must stay within the anchor's 25 meter display range.");
   ValidateRasterRequest(new Vector2I(d.UiRasterWidth,d.UiRasterHeight));if(d.UiRasterSamples!=1&&d.UiRasterSamples!=4)throw new ArgumentException("UI raster sampling is 1 or 4.");
  }
  static bool ScreenSurfaceFront(HoloProjectedScreenData d,MatrixD world,Vector3D eye)
  {
   var pose=ReadMatrix(d.Pose);var local=Vector3D.Transform(eye,MatrixD.Invert(pose*world));
   if(d.SurfaceKind==0)return local.Z>0;
   if(d.SurfaceKind==3){var r=SurfaceRadii(d);double value=local.X*local.X/(r.X*r.X)+local.Y*local.Y/(r.Y*r.Y)+local.Z*local.Z/(r.Z*r.Z);return d.SurfaceSide==0?value<1:value>1;}
   if(d.SurfaceKind==4){var p=SurfacePoints(d);var t=d.SurfaceMeshTriangles;for(int i=0;i<t.Length;i+=3){var n=Vector3D.Cross(p[t[i+1]]-p[t[i]],p[t[i+2]]-p[t[i]]);if(Vector3D.Dot(n,local-p[t[i]])*(d.SurfaceSide==0?-1:1)>0)return true;}return false;}
   double radial=d.SurfaceKind==1?local.X*local.X+local.Z*local.Z:local.LengthSquared();
   return d.SurfaceSide==0?radial<d.SurfaceRadius*d.SurfaceRadius:radial>d.SurfaceRadius*d.SurfaceRadius;
  }
  static float ScreenSideOpacity(HoloProjectedScreenData d,MatrixD world,Vector3D eye)
  {if(d.SurfaceKind>=3)return d.FrontOpacity>0||d.TwoSided&&d.BackOpacity>0?1:0;bool front=ScreenSurfaceFront(d,world,eye);return front?d.FrontOpacity:d.TwoSided?d.BackOpacity:0;}
  static bool ScreenSurfaceVisible(HoloProjectedScreenData d,MatrixD world,Vector3D eye)
  {return ScreenSideOpacity(d,world,eye)>0;}
  static double ScreenLayerDepth(HoloProjectedScreenData d,MatrixD world,Vector3D eye,double depth)
  {return d.SurfaceKind>=3?depth:ScreenSurfaceFront(d,world,eye)?depth:-depth;}
  static Vector3D ScreenPointNormal(HoloProjectedScreenData d,Vector3D point,Vector3D b,Vector3D c)
  {
   if(d.SurfaceKind==3){var n=SurfaceMapping.EllipsoidNormal(point,SurfaceRadii(d));return d.SurfaceSide==0?-n:n;}
   var cross=Vector3D.Cross(b-point,c-point);return cross.LengthSquared()>1e-20?Vector3D.Normalize(cross):Vector3D.UnitZ;
  }
  static float ScreenTriangleOpacity(HoloProjectedScreenData d,Vector3D a,Vector3D b,Vector3D c,Vector3D eye,out bool front)
  {var centre=(a+b+c)/3;var normal=d.SurfaceKind==3?ScreenPointNormal(d,centre,b,c):ScreenPointNormal(d,a,b,c);front=Vector3D.Dot(normal,eye-centre)>=0;return front?d.FrontOpacity:d.TwoSided?d.BackOpacity:0;}
  static SurfaceMesh TransformCanvasMesh(SurfaceMesh source,MatrixD transform)
  {
   var g=source.Geometry;var points=new Vector3D[g.Points.Length];for(int i=0;i<points.Length;i++){points[i]=Vector3D.Transform(g.Points[i],transform);points[i].Z=0;}
   return new SurfaceMesh{Geometry=new Geometry(points,g.Edges,g.Triangles),Colors=source.Colors,EdgeColors=source.EdgeColors,UV=source.UV};
  }
  bool MappedScreenFits(Scene scene,ProjectedCache cache,string replacement,MappedScreenMesh candidate)
  {
   int points,primitives;ActiveRenderCounts(scene,null,null,false,false,out points,out primitives);
   foreach(var c in _projectedCaches.Values)if(c.Anchor==scene.ConsoleId)
   {
    foreach(var pair in c.Mapped)
    {
     if(c==cache&&pair.Key==replacement)continue;var entry=pair.Value;if(_ticks-entry.Seen>120||entry.Mesh==null)continue;
     points+=Math.Max(0,entry.Mesh.Geometry.Points.Length-(entry.BaselineCounted?entry.Source.Geometry.Points.Length:0));
     primitives+=Math.Max(0,entry.Mesh.Geometry.Triangles.Length/3+entry.Mesh.Geometry.Edges.Length/2-(entry.BaselineCounted?entry.Source.Geometry.Triangles.Length/3+entry.Source.Geometry.Edges.Length/2:0));
    }
   }
   points+=Math.Max(0,candidate.Mesh.Geometry.Points.Length-(candidate.BaselineCounted?candidate.Source.Geometry.Points.Length:0));
   primitives+=Math.Max(0,candidate.Mesh.Geometry.Triangles.Length/3+candidate.Mesh.Geometry.Edges.Length/2-(candidate.BaselineCounted?candidate.Source.Geometry.Triangles.Length/3+candidate.Source.Geometry.Edges.Length/2:0));
   return points<=scene.PointBudget&&primitives<=scene.PrimitiveBudget;
  }
  SurfaceMesh MappedScreenGeometry(Scene scene,ProjectedCache cache,string id,SurfaceMesh source,HoloProjectedScreenData d,MatrixD transform,double depth,double aspect,bool baseline,ref int compileBudget,Vector4? fill=null,Vector4? line=null,bool quad=false)
  {
   var fillColor=fill??Vector4.One;var lineColor=line??Vector4.Zero;
   var panorama=id=="source"?PanoramaCameras(scene,d):null;
   MappedScreenMesh old;if(cache.Mapped.TryGetValue(id,out old))
   {
    old.Seen=_ticks;
    if(ReferenceEquals(old.Source.Geometry,source.Geometry)&&ReferenceEquals(old.Source.Colors,source.Colors)&&ReferenceEquals(old.Source.EdgeColors,source.EdgeColors)&&ReferenceEquals(old.Source.UV,source.UV)&&old.Transform==transform&&old.Depth==depth&&old.Aspect==aspect&&old.Fill==fillColor&&old.Line==lineColor&&SameSurface(old.Settings,d))
    {if(SamePanorama(old.Panorama,panorama)){if(old.Error==null)return old.Mesh;if(_ticks<old.RetryTick){cache.Error=old.Error;return null;}}}
   }
   if(compileBudget<=0||_lastCompileTick==_ticks)return null;
   if(old==null&&cache.Mapped.Count>=128)throw new ArgumentException("Curved surface mesh cache limit reached.");
   _lastCompileTick=_ticks;compileBudget--;
   var next=new MappedScreenMesh{Source=source,Settings=CloneScreen(d),Transform=transform,Depth=depth,Aspect=aspect,Fill=fillColor,Line=lineColor,BaselineCounted=baseline,Seen=_ticks,Panorama=panorama};
   try
   {
   SurfaceMesh prepared;var style=ScreenSurfaceStyle(d,aspect);
   using(GeometryWork.Begin(200000))
   {
    if(quad&&transform==MatrixD.Identity)
    {prepared=SurfaceMapping.BuildQuad(d.CanvasWidth,d.CanvasHeight,fillColor,style,depth);if(prepared.UV!=null&&source.UV!=null&&source.UV.Length==4){var offset=source.UV[3];var size=source.UV[1]-offset;for(int i=0;i<prepared.UV.Length;i++)prepared.UV[i]=new Vector2(offset.X+prepared.UV[i].X*size.X,offset.Y+prepared.UV[i].Y*size.Y);}}
    else
    {
     var solid=MeshEffects.Solid(source.Geometry,fillColor,lineColor,source.Colors,source.EdgeColors,source.UV);
     var transformed=TransformCanvasMesh(solid,transform);
     var clipped=MeshEffects.Clip(transformed,new[]{new Vector4(1,0,0,(float)(d.CanvasWidth/2)),new Vector4(-1,0,0,(float)(d.CanvasWidth/2)),new Vector4(0,1,0,(float)(d.CanvasHeight/2)),new Vector4(0,-1,0,(float)(d.CanvasHeight/2))});
     prepared=SurfaceMapping.Warp(clipped,d.CanvasWidth,d.CanvasHeight,style,depth);
    }
   }
   if(d.SurfaceKind==1)for(int i=0;i<prepared.Geometry.Points.Length;i++)prepared.Geometry.Points[i].Y*=d.Height/d.CanvasHeight;
   // A viewer at the centre looks toward local +Z. Their right is -X;
   // mirror the curved frame so canvas text and camera images read correctly inside.
   if(d.SurfaceSide==0&&d.SurfaceKind!=4)for(int i=0;i<prepared.Geometry.Points.Length;i++)prepared.Geometry.Points[i].X=-prepared.Geometry.Points[i].X;
   if(panorama!=null)using(GeometryWork.Begin(200000))prepared=CameraPanorama.Join(prepared,PanoramaCamera(d),panorama,scene.PointBudget,scene.PrimitiveBudget);
   next.Mesh=prepared;
   if(!MappedScreenFits(scene,cache,id,next))throw new ArgumentException("Curved surface exceeds the shared anchor geometry budget; raise budget or surface error.");
   cache.Mapped[id]=next;return prepared;
   }
   catch(Exception error){next.Mesh=null;next.Error=error.Message;next.RetryTick=_ticks+300;cache.Mapped[id]=next;throw;}
  }
  void PrepareScreenUiRaster(Scene scene,HoloProjectedScreenData d,ProjectedCache cache)
  {
   if(d.ContentRenderer!=1){ReleaseUiRaster(cache);SetUiRasterError(cache,null);return;}
   if(_rasterBackend==null){ReleaseUiRaster(cache);cache.UiRasterError=LocalPluginStatus("raster-ui").Item3;NotifyPluginUnavailable("raster-ui");return;}
   try
   {
    var size=ScreenRasterSize(d,cache);if(size.X==0||size.Y==0){ReleaseUiRaster(cache);return;}size=RasterBackendSize(size);
    var compositor=new RasterCanvas(size.X,size.Y,d.CanvasWidth,d.CanvasHeight,RasterCanvas.AbsoluteMaxWork,d.UiRasterSamples);
    compositor.Clear(Vector4.Zero);var view=ProjectedContentView(d);var layers=new float[cache.Items.Count];int layerIndex=0;
    foreach(var entry in cache.Items)
    {
     float layer=ClientLayerAlpha(scene,entry.Caller,entry.Item!=null?entry.Item.Layer:entry.Label.Layer);layers[layerIndex++]=layer;
     if(entry.Item!=null)
     {
      var item=entry.Item;float opacity=item.Opacity*layer;if(opacity<=0)continue;
      if(item.Emission!=0)throw new ArgumentException("Per-object emission needs the vector content renderer.");
      if(item.Material!=null)throw new ArgumentException("Packaged image layers currently need the vector content renderer; video textures remain separate raster sources.");
      compositor.Add(new SurfaceMesh{Geometry=item.Geometry,Colors=item.TriangleColors,EdgeColors=item.EdgeColors,UV=item.UV},item.Transform*view,item.FillColor,item.LineColor,item.Thickness,opacity,item.Effects,item.Effects==null?default(HologramEffectFrame):HologramEffectKernel.Evaluate(item.Effects,HologramEffectNow,item.EffectStart,item.EffectEntering));
     }
     else if(entry.Label!=null)
     {
      var label=entry.Label;float opacity=label.Opacity*layer;if(opacity<=0)continue;
      if(label.Emission!=0)throw new ArgumentException("Per-label emission needs the vector content renderer.");
      foreach(var glyph in label.Glyphs)compositor.Add(new SurfaceMesh{Geometry=glyph.Mesh},MatrixD.CreateScale(label.Height)*MatrixD.CreateTranslation(label.Position+glyph.Offset*label.Height)*view,label.Color,Vector4.Zero,0,opacity);
     }
    }
    // All source items have priority over optional particle work.
    foreach(var entry in cache.Items)if(entry.Item!=null)RasterEffectParticles(compositor,entry.Item,view,entry.Item.Opacity*ClientLayerAlpha(scene,entry.Caller,entry.Item.Layer));
    var image=UploadRaster(ProjectedConsumerKey(cache)+":ui",size,compositor.Finish());
    if(image==null){SetUiRasterError(cache,"Raster upload is unavailable or busy.");ReleaseUiRaster(cache);return;}
    var old=cache.UiRaster;cache.UiRaster=image;RetireRasterImage(old,image);
    if(cache.UiRasterMesh==null||cache.UiQuadWidth!=d.CanvasWidth||cache.UiQuadHeight!=d.CanvasHeight){cache.UiRasterMesh=RasterQuad(d.CanvasWidth,d.CanvasHeight);cache.UiQuadWidth=d.CanvasWidth;cache.UiQuadHeight=d.CanvasHeight;}
    cache.RasterLayerState=layers;
    SetUiRasterError(cache,null);
   }
   catch(Exception error){ReleaseUiRaster(cache);SetUiRasterError(cache,error.Message);}
  }
  void SetUiRasterError(ProjectedCache cache,string error)
  {
   bool changed=cache.UiRasterError!=error;cache.UiRasterError=error;
   if(error==null||!changed||_ticks-cache.UiRasterReported<300)return;cache.UiRasterReported=_ticks;
   string message="Screen "+cache.Id+" uses vector fallback: "+error.Substring(0,Math.Min(error.Length,160));
   try{VRage.Utils.MyLog.Default.WriteLine("HDR API: "+message);}catch{}
   try{Sandbox.ModAPI.MyAPIGateway.Utilities.ShowMessage("HDR API",message);}catch{}
  }
  static void ReleaseUiRaster(ProjectedCache cache)
  {var image=cache.UiRaster;cache.UiRaster=null;cache.UiRasterMesh=null;cache.RasterLayerState=null;cache.Mapped.Remove("ui");ReleaseRasterImage(image);}
  bool RasterLayersCurrent(Scene scene,ProjectedCache cache)
  {
   if(cache.UiRaster==null)return true;var state=cache.RasterLayerState;if(state==null||state.Length!=cache.Items.Count)return false;
   for(int i=0;i<state.Length;i++){var entry=cache.Items[i];if(state[i]!=ClientLayerAlpha(scene,entry.Caller,entry.Item!=null?entry.Item.Layer:entry.Label.Layer))return false;}return true;
  }
  bool OldestRasterJob(ProjectedCache current,double now,bool ui,int allowance=1)
  {
   int tick=ui?current.UiAttemptTick:current.SourceAttemptTick,older=0;string key=PackedKey(current.Caller,current.Anchor,current.Id);
   foreach(var c in _projectedCaches.Values)
   {
    double due=ui?c.NextSample:c.NextCapture;
    if(c==current||!c.Front||_ticks-c.Seen>2||due>=0&&now+1e-8<due)continue;
    Scene scene;ProjectedScreen screen;if(!_scenes.TryGetValue(c.Anchor,out scene)||!scene.Screens.TryGetValue(Key(c.Caller,c.Id),out screen)||!ExternalBudgetScreenVisible(screen.Data))continue;
    if(ui){if(screen.Data.ContentRenderer!=1)continue;}
    else{DisplayProvider provider;if(!HasExternalSource(screen.Data)||!_displayProviders.TryGetValue(screen.Data.SourceProvider,out provider)||provider.Protocol!=2)continue;}
    int other=ui?c.UiAttemptTick:c.SourceAttemptTick;if(other<tick||other==tick&&string.CompareOrdinal(PackedKey(c.Caller,c.Anchor,c.Id),key)<0){older++;if(older>=allowance)return false;}
   }
   return true;
  }
  void DrawScreenMesh(Scene scene,ProjectedCache cache,string id,SurfaceMesh mesh,HoloProjectedScreenData d,MatrixD content,double depth,double aspect,bool baseline,bool quad,DisplayVolume volume,float opacity,float emission,string material,MatrixD world,MatrixD inverse,Vector3D camera,uint parent,ref int budget,ref int compileBudget,Vector4? fill=null,Vector4? line=null,double thickness=.008,Item effectItem=null,bool effectExtrasOnly=false)
  {
   // Move foreground layers toward the current viewer on either side so an
   // opaque background never hides the content when viewed from the reverse.
   depth=ScreenLayerDepth(d,world,camera,depth);
   var matrix=content*ProjectedCanvas(d,depth,d.CanvasWidth,d.CanvasHeight)*world;
   if(d.SurfaceKind!=0)
   {
    mesh=MappedScreenGeometry(scene,cache,id,mesh,d,content,depth,aspect,baseline,ref compileBudget,fill,line,quad);if(mesh==null)return;
    matrix=ReadMatrix(d.Pose)*world;
   }
   if(effectExtrasOnly)
   {
    if(effectItem==null||effectItem.Effects==null)return;
    if(d.SurfaceKind==0)matrix=HologramProjectedExtrusion(content,d,world,depth);
    var mappedItem=CloneAnimationItem(effectItem);mappedItem.Geometry=mesh.Geometry;mappedItem.TriangleColors=mesh.Colors;mappedItem.EdgeColors=mesh.EdgeColors;mappedItem.UV=mesh.UV;mappedItem.Transform=MatrixD.Identity;
    DrawHologramExtras(mappedItem,matrix,volume,opacity,world,inverse,camera,parent,ref budget);return;
   }
   DrawProjectedMesh(mesh,matrix,volume,opacity,emission,material,world,inverse,camera,parent,ref budget,fill,line,thickness,d.SurfaceKind,d.SurfaceKind>=3?d:null,depth,effectItem);
  }
  void PruneScreenMeshes(ProjectedCache cache,HoloProjectedScreenData d)
  {
   var dead=new List<string>();foreach(var pair in cache.Mapped)if(d.SurfaceKind==0||_ticks-pair.Value.Seen>120)dead.Add(pair.Key);
   foreach(var id in dead)cache.Mapped.Remove(id);
  }
 }
}
