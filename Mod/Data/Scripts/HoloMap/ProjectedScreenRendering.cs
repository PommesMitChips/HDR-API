using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;
namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  sealed class ProjectedCache
  {
   public long Anchor,Caller;public string Id,Error;public int Seen,Reported=-10000;public bool Front;
   public double NextSample=-1,NextCapture=-1;public HoloProjectedScreenData Data;
   public readonly List<LcdVectorEntry> Items=new List<LcdVectorEntry>();
   public SurfaceMesh Sprites,View;public PerspectiveView Raster;
   public bool SourceReduced;public int SourcePointShare,SourcePrimitiveShare;
   public object SourceEvidence;public string SourceProvider,SourceId;public long SourceGeneration;public Func<string,object[],object> SourceEndpoint;
   public bool SourceTexture;public string SourceTextureMaterial;public Vector2I SourceTextureSize;
   public RasterImage SourceRaster,UiRaster;public SurfaceMesh UiRasterMesh;public string UiRasterError;public int UiRasterReported=-10000;public double UiQuadWidth,UiQuadHeight;public float[] RasterLayerState;public int SourceProtocol;
   public SurfaceMesh SourceQuad;public double SourceQuadWidth,SourceQuadHeight;public Vector4 SourceQuadUV;
   public SurfaceMesh BackgroundQuad;public double BackgroundWidth,BackgroundHeight;
   public readonly Dictionary<string,MappedScreenMesh> Mapped=new Dictionary<string,MappedScreenMesh>();
   public int UiAttemptTick=-10000,SourceAttemptTick=-10000;
   public bool AdaptiveKnown;public DisplayLodResult Adaptive;public SurfaceMesh AdaptiveMesh,AdaptiveBounds;public HoloProjectedScreenData AdaptiveData;public MatrixD AdaptiveWorld,AdaptiveView;public Vector2I AdaptiveViewport;
   public HoloProjectedScreenData SpriteVersion,ViewVersion;
   public string ConsumerSuffix;
   public readonly Dictionary<string,CompositionSlotCache> Slots=new Dictionary<string,CompositionSlotCache>();
   public readonly Dictionary<string,ProjectedCache> UiChunks=new Dictionary<string,ProjectedCache>();
  }
  readonly Dictionary<string,ProjectedCache> _projectedCaches=new Dictionary<string,ProjectedCache>();
  string _lastProjectedJob,_lastUiRasterJob;
  int _rasterSourceTick=-1,_rasterSourceCalls,_uiRasterTick=-1;
  static MatrixD ProjectedCanvas(HoloProjectedScreenData d,double depth,double cw,double ch)
  {
   var pose=ReadMatrix(d.Pose);double x=d.Width/cw,y=d.Height/ch;
   if(d.Aspect=="contain")x=y=Math.Min(x,y);else if(d.Aspect=="cover")x=y=Math.Max(x,y);
   var m=MatrixD.Identity;m.Right=pose.Right*x;m.Up=pose.Up*y;m.Backward=Vector3D.Zero;m.Translation=pose.Translation+pose.Backward*depth;return m;
  }
  static MatrixD ProjectedContentView(HoloProjectedScreenData d)
  {var v=d.View;return MatrixD.CreateScale(v[6])*MatrixD.CreateFromYawPitchRoll(v[4],v[3],v[5])*MatrixD.CreateTranslation(v[0],v[1],v[2]);}
  DisplayVolume ProjectedVolume(Scene scene,HoloProjectedScreenData d,IMyTerminalBlock anchor)
  {
   var frame=ReadMatrix(d.Pose);frame.Right*=d.Width*.5;frame.Up*=d.Height*.5;frame*=anchor.WorldMatrix;
   var planes=new List<Vector4D>(LcdClipPlanes);var outer=GetDisplayVolume(scene,anchor);
   if(outer!=null){var mapping=frame*outer.Inverse;foreach(var p in outer.Planes){var n=new Vector3D(p.X,p.Y,p.Z);planes.Add(new Vector4D(Vector3D.Dot(mapping.Right,n),Vector3D.Dot(mapping.Up,n),Vector3D.Dot(mapping.Backward,n),p.W-Vector3D.Dot(mapping.Translation,n)));}}
   return new DisplayVolume(frame,planes.ToArray());
  }
  DisplayVolume SurfaceCrop(HoloProjectedScreenData d,IMyTerminalBlock anchor,DisplayVolume outer)
  {
   if(d.SurfaceClip==null||d.SurfaceClip.Length==0)return outer;
   var frame=anchor.WorldMatrix;var planes=new List<Vector4D>();
   for(int i=0;i<d.SurfaceClip.Length;i+=4)planes.Add(new Vector4D(d.SurfaceClip[i],d.SurfaceClip[i+1],d.SurfaceClip[i+2],d.SurfaceClip[i+3]));
   if(outer!=null){var mapping=frame*outer.Inverse;foreach(var p in outer.Planes){var n=new Vector3D(p.X,p.Y,p.Z);planes.Add(new Vector4D(Vector3D.Dot(mapping.Right,n),Vector3D.Dot(mapping.Up,n),Vector3D.Dot(mapping.Backward,n),p.W-Vector3D.Dot(mapping.Translation,n)));}}
   return new DisplayVolume(frame,planes.ToArray());
  }
  bool SampleProjectedItems(Scene scene,ProjectedScreen screen,ProjectedCache cache,ref int compileBudget)
  {
   var d=screen.Data;double now=_ticks/60d,rate=Math.Min(d.RefreshHz,ClientLcdRefreshCap);
   if(cache.NextSample>=0&&now+1e-8<cache.NextSample)return false;cache.NextSample=now+1/rate;cache.Items.Clear();string prefix=ScreenPrefix(d.Id);
   foreach(var canonical in scene.Items.Values)if(canonical.CallerId==d.CallerId&&canonical.Id.StartsWith(prefix,StringComparison.Ordinal)&&canonical.Visible)
   {var item=ClientDisplayItem(scene,GetAnimatedItem(scene.ConsoleId,canonical),ref compileBudget);if(item!=null)cache.Items.Add(new LcdVectorEntry{Item=CloneAnimationItem(item),Caller=item.CallerId,Id=item.Id,Order=LayerOrder(scene,item.CallerId,item.Layer)});}
   foreach(var label in scene.Labels.Values)if(label.CallerId==d.CallerId&&label.Id.StartsWith(prefix,StringComparison.Ordinal)&&label.Visible)
   {if(label.Glyphs==null)label.Glyphs=VectorFont.Layout(label.Text);cache.Items.Add(new LcdVectorEntry{Label=new Label{CallerId=label.CallerId,Id=label.Id,Text=label.Text,Position=label.Position,Color=label.Color,Height=label.Height,Opacity=label.Opacity,Emission=label.Emission,Visible=true,Layer=label.Layer,Glyphs=label.Glyphs},Caller=label.CallerId,Id=label.Id,Order=LayerOrder(scene,label.CallerId,label.Layer)});}
   cache.Items.Sort((a,b)=>{int order=a.Order.CompareTo(b.Order);return order!=0?order:string.CompareOrdinal(a.Id,b.Id);});
   return true;
  }
  bool ProjectedMeshFits(Scene scene,ProjectedCache cache,SurfaceMesh mesh,bool sprite)
  {
   int points,primitives;ActiveRenderCounts(scene,null,cache,sprite,false,out points,out primitives);ExternalBudgetCount(mesh,ref points,ref primitives);
   return points<=scene.PointBudget&&primitives<=scene.PrimitiveBudget;
  }
  void PrepareProjectedSources(Scene scene,ProjectedScreen screen,ProjectedCache cache,ref int compileBudget)
  {
   var d=screen.Data;if(d.Sprites==null){cache.Sprites=null;cache.SpriteVersion=null;cache.Mapped.Remove("sprites");}if(screen.Source==null&&!HasExternalSource(d))ClearExternalBudgetView(cache);
   bool sprite=d.Sprites!=null&&!SameProjectedSprites(cache.SpriteVersion,d);
   double now=_ticks/60d;bool view=HasExternalSource(d)&&now+1e-8>=cache.NextCapture||screen.Source!=null&&(!SameProjectedView(cache.ViewVersion,d)||d.OrbitSpeed!=0&&now+1e-8>=cache.NextCapture);
   if(cache.NextCapture>=0&&now+1e-8<cache.NextCapture)return;
   string job=PackedKey(cache.Caller,cache.Anchor,cache.Id);if(job==_lastProjectedJob&&OtherProjectedJobPending(cache,now))return;
   if(!sprite&&!view)return;
   DisplayProvider source;bool textureJob=!sprite&&HasExternalSource(d)&&_displayProviders.TryGetValue(d.SourceProvider,out source)&&source.Protocol==2;
   if(textureJob){if(_rasterSourceTick!=_ticks){_rasterSourceTick=_ticks;_rasterSourceCalls=0;}if(_rasterSourceCalls>=4||!OldestRasterJob(cache,now,false,4-_rasterSourceCalls))return;_rasterSourceCalls++;cache.SourceAttemptTick=_ticks;}
   else{if(_lastCompileTick==_ticks||compileBudget<=0)return;_lastCompileTick=_ticks;compileBudget--;}
   _lastProjectedJob=job;
   cache.NextCapture=now+1/Math.Min(d.RefreshHz,ClientLcdRefreshCap);try
   {
    SurfaceMesh prepared;
    if(sprite){using(GeometryWork.Begin(200000))prepared=ProjectedSprites.Composite(ScreenSprites(d.Sprites),d.SpriteWidth,d.SpriteHeight);if(!ProjectedMeshFits(scene,cache,prepared,true)){ReclaimExternalViews(scene);if(!ProjectedMeshFits(scene,cache,prepared,true))throw new ArgumentException("Projected sprite frame exceeds the shared anchor render budget.");}cache.Sprites=prepared;cache.SpriteVersion=d;}
    else if(HasExternalSource(d))
    {int points,primitives;GetExternalSourceRenderBudget(scene,cache,out points,out primitives);cache.SourcePointShare=points;cache.SourcePrimitiveShare=primitives;using(GeometryWork.Begin(200000))prepared=BuildExternalSource(scene,d,cache,points,primitives);if(prepared==null){ClearExternalBudgetView(cache);cache.ViewVersion=d;cache.Error=null;return;}if(!ProjectedMeshFits(scene,cache,prepared,false)||!ExternalEvidenceValid(scene,d,cache)){ClearExternalBudgetView(cache);cache.SourceReduced=true;cache.Error=null;return;}cache.View=prepared;cache.ViewVersion=d;}
    else
    {
     if(cache.Raster==null)cache.Raster=new PerspectiveView();var c=d.Camera;var eye=new Vector3D(c[0],c[1],c[2]);var look=new Vector3D(c[3],c[4],c[5]);var up=new Vector3D(c[6],c[7],c[8]);
     if(d.OrbitSpeed!=0){double angle=Math.Max(0,(AnimationNow-d.OrbitStartTick)/60d)*d.OrbitSpeed;eye=look+Vector3D.TransformNormal(eye-look,MatrixD.CreateFromAxisAngle(Vector3D.Normalize(up),angle));}
     using(GeometryWork.Begin(200000))prepared=cache.Raster.Render(screen.Source,screen.SourceColors,new PerspectiveCamera{Eye=eye,Target=look,Up=up,VerticalFovRadians=c[9],Near=c[10],Far=c[11],Width=d.RasterWidth,Height=d.RasterHeight},d.CanvasWidth,d.CanvasHeight,200000);
     if(!ProjectedMeshFits(scene,cache,prepared,false)){ReclaimExternalViews(scene);if(!ProjectedMeshFits(scene,cache,prepared,false))throw new ArgumentException("Perspective view exceeds the shared anchor render budget.");}cache.View=prepared;cache.ViewVersion=d;cache.SourceEvidence=null;
    }
    cache.Error=null;
   }
   catch(Exception e){if(sprite){cache.Sprites=null;cache.SpriteVersion=d;}else{ClearExternalBudgetView(cache);cache.ViewVersion=d;}ReportProjectedError(cache,e);}
  }
  static bool SameProjectedSprites(HoloProjectedScreenData a,HoloProjectedScreenData b)
  {
   if(a==null||a.SpriteWidth!=b.SpriteWidth||a.SpriteHeight!=b.SpriteHeight)return false;if(ReferenceEquals(a.Sprites,b.Sprites))return true;if(a.Sprites==null||b.Sprites==null||a.Sprites.Count!=b.Sprites.Count)return false;
   for(int i=0;i<a.Sprites.Count;i++){var x=a.Sprites[i];var y=b.Sprites[i];if(x.Type!=y.Type||x.Data!=y.Data||x.Font!=y.Font||x.Alignment!=y.Alignment||x.Rotation!=y.Rotation||!SameArray(x.Position,y.Position)||!SameArray(x.Size,y.Size)||!SameArray(x.Color,y.Color))return false;}return true;
  }
  static bool SameProjectedView(HoloProjectedScreenData a,HoloProjectedScreenData b){return a!=null&&a.RasterWidth==b.RasterWidth&&a.RasterHeight==b.RasterHeight&&a.CanvasWidth==b.CanvasWidth&&a.CanvasHeight==b.CanvasHeight&&a.OrbitSpeed==b.OrbitSpeed&&a.OrbitStartTick==b.OrbitStartTick&&SameArray(a.SourcePoints,b.SourcePoints)&&SameArray(a.SourceTriangles,b.SourceTriangles)&&SameArray(a.SourceColors,b.SourceColors)&&SameArray(a.Camera,b.Camera);}
  bool OtherProjectedJobPending(ProjectedCache current,double now)
  {
   foreach(var c in _projectedCaches.Values)
   {if(c==current||!c.Front||_ticks-c.Seen>2||c.NextCapture>=0&&now+1e-8<c.NextCapture)continue;Scene scene;ProjectedScreen s;if(!_scenes.TryGetValue(c.Anchor,out scene)||!scene.Screens.TryGetValue(Key(c.Caller,c.Id),out s))continue;var d=s.Data;if(!d.Visible||d.Opacity<=0)continue;if(HasExternalSource(d)||d.Sprites!=null&&!SameProjectedSprites(c.SpriteVersion,d)||s.Source!=null&&(!SameProjectedView(c.ViewVersion,d)||d.OrbitSpeed!=0))return true;}return false;
  }
  void ReportProjectedError(ProjectedCache cache,Exception error)
  {cache.Error=error.Message;if(_ticks-cache.Reported<300)return;cache.Reported=_ticks;try{VRage.Utils.MyLog.Default.WriteLineAndConsole("HDR API projected screen "+cache.Id+": "+error);}catch{}try{MyAPIGateway.Utilities.ShowMessage("HDR API","Screen "+cache.Id+": "+error.Message);}catch{}}
  void DrawProjectedScreens(Scene scene,IMyTerminalBlock anchor,ref int budget,ref int compileBudget)
  {
   // Capture full source enclosures before budgets, opacity, sidedness or LOD
   // can omit a consumer. Reuse exactly this draw's anchor transform below.
   MatrixD? drawWorld=null;
   if(_sourceOcclusionDrawing!=null)foreach(var candidate in scene.Screens.Values)if(ScreenHasProvider(candidate.Data,"camera-panorama"))
   {try{drawWorld=anchor.WorldMatrix;RecordDisplaySourceOcclusion(scene,anchor,drawWorld.Value);}catch{/* Unknown inventory retains capture demand. */}break;}
   foreach(var screen in scene.Screens.Values)
   {
    if(budget<=0)return;var d=screen.Data;string key=PackedKey(d.CallerId,scene.ConsoleId,d.Id);ProjectedCache cache;if(!d.Visible||d.Opacity<=0){if(_projectedCaches.TryGetValue(key,out cache)){ClearExternalBudgetView(cache);ReleaseSourceSlots(cache);}continue;}
    if(!_projectedCaches.TryGetValue(key,out cache)){cache=new ProjectedCache{Anchor=scene.ConsoleId,Caller=d.CallerId,Id=d.Id};_projectedCaches.Add(key,cache);}cache.Seen=_ticks;
    cache.Front=false;try
    {
     var world=drawWorld??anchor.WorldMatrix;var camera=MyAPIGateway.Session.Camera.Position;float screenOpacity=d.Opacity*ScreenSideOpacity(d,world,camera);cache.Front=screenOpacity>0&&ScreenLod(d,anchor,cache).Visible;if(!cache.Front){ReleaseUiRaster(cache);ClearExternalBudgetView(cache);ReleaseSourceSlots(cache);cache.NextSample=cache.NextCapture=-1;continue;}
     bool composition=PrepareCompositionMode(scene,d,cache);
     PruneScreenMeshes(cache,d);ValidateCachedExternalView(scene,d,cache);PrepareProjectedSources(scene,screen,cache,ref compileBudget);if(composition)PrepareSourceSlots(scene,d,cache,ref compileBudget);
     if(cache.Data==null||cache.Data.ContentRenderer!=d.ContentRenderer||cache.Data.UiRasterWidth!=d.UiRasterWidth||cache.Data.UiRasterHeight!=d.UiRasterHeight||cache.Data.UiRasterSamples!=d.UiRasterSamples||cache.Data.CanvasWidth!=d.CanvasWidth||cache.Data.CanvasHeight!=d.CanvasHeight||!SameArray(cache.Data.View,d.View))cache.NextSample=-1;
     if(!RasterLayersCurrent(scene,cache)){ReleaseUiRaster(cache);cache.NextSample=-1;}
     if(d.ContentRenderer==1&&(cache.NextSample<0||_ticks/60d+1e-8>=cache.NextSample)&&!OldestRasterJob(cache,_ticks/60d,true))cache.NextSample=_ticks/60d+1/60d;
     bool sampled=SampleProjectedItems(scene,screen,cache,ref compileBudget);
     if(composition)ReleaseUiRaster(cache);
     else if(sampled){if(d.ContentRenderer!=1||_uiRasterTick!=_ticks){if(d.ContentRenderer==1){_uiRasterTick=_ticks;cache.UiAttemptTick=_ticks;_lastUiRasterJob=key;}PrepareScreenUiRaster(scene,d,cache);}else cache.NextSample=-1;}
     cache.Data=d;
     var inverse=MatrixD.Invert(world);uint parent=anchor.Render.GetRenderObjectID();var volume=SurfaceCrop(d,anchor,d.SurfaceKind==0?ProjectedVolume(scene,d,anchor):GetDisplayVolume(scene,anchor));
     var background=ScreenColor(d.Background);
     if(background.W>0)
     {
      if(cache.BackgroundQuad==null||cache.BackgroundWidth!=d.CanvasWidth||cache.BackgroundHeight!=d.CanvasHeight){cache.BackgroundQuad=RasterQuad(d.CanvasWidth,d.CanvasHeight);cache.BackgroundQuad.Colors=null;cache.BackgroundQuad.UV=null;cache.BackgroundWidth=d.CanvasWidth;cache.BackgroundHeight=d.CanvasHeight;}
      DrawScreenMesh(scene,cache,"background",cache.BackgroundQuad,d,MatrixD.Identity,0,0,true,true,volume,screenOpacity,0,null,world,inverse,camera,parent,ref budget,ref compileBudget,background);
     }
     else cache.Mapped.Remove("background");
     if(HasExternalSource(d)&&cache.View!=null&&(!ExternalEvidenceValid(scene,d,cache)||cache.SourceRaster!=null&&!RasterImageCurrent(cache.SourceRaster)))ClearExternalBudgetView(cache);
     if(cache.View!=null)DrawScreenMesh(scene,cache,"source",cache.View,d,MatrixD.Identity,d.SourceProvider=="native-portal"?0:.0005,cache.SourceTexture?(double)d.UiRasterWidth/d.UiRasterHeight:0,true,cache.SourceTexture,volume,screenOpacity,0,cache.SourceTextureMaterial,world,inverse,camera,parent,ref budget,ref compileBudget);
     if(cache.Sprites!=null)DrawScreenMesh(scene,cache,"sprites",cache.Sprites,d,MatrixD.CreateScale(d.CanvasWidth,d.CanvasHeight,1),.001,0,true,false,volume,screenOpacity,0,null,world,inverse,camera,parent,ref budget,ref compileBudget);
     if(composition){DrawCompositionContent(scene,cache,d,sampled,volume,screenOpacity,world,inverse,camera,parent,ref budget,ref compileBudget);continue;}
     bool raster=d.ContentRenderer==1&&RasterImageCurrent(cache.UiRaster)&&cache.UiRasterMesh!=null;
     if(cache.UiRaster!=null&&!raster){ReleaseUiRaster(cache);cache.NextSample=-1;}
     if(raster)
     {
      var dead=new List<string>();foreach(var pair in cache.Mapped)if(pair.Key.StartsWith("item:",StringComparison.Ordinal)||pair.Key.StartsWith("label:",StringComparison.Ordinal))dead.Add(pair.Key);foreach(string id in dead)cache.Mapped.Remove(id);
      DrawScreenMesh(scene,cache,"ui",cache.UiRasterMesh,d,MatrixD.Identity,.0015,0,true,true,volume,screenOpacity,0,cache.UiRaster.Material,world,inverse,camera,parent,ref budget,ref compileBudget);
     }
     for(int i=0;!raster&&UseProjectedVectorFallback(d)&&i<cache.Items.Count&&budget>0;i++)
     {
      var entry=cache.Items[i];var map=ProjectedContentView(d);double depth=.0015+i*.00001;
      if(entry.Item!=null){var item=entry.Item;float opacity=screenOpacity*item.Opacity*ClientLayerAlpha(scene,item.CallerId,item.Layer);if(opacity<=0)continue;DrawScreenMesh(scene,cache,"item:"+item.Id,new SurfaceMesh{Geometry=item.Geometry,Colors=item.TriangleColors,EdgeColors=item.EdgeColors,UV=item.UV},d,item.Transform*map,depth,0,true,false,volume,opacity,item.Emission,item.Material,world,inverse,camera,parent,ref budget,ref compileBudget,item.FillColor,item.LineColor,item.Thickness*(d.SurfaceKind==0?d.Width/d.CanvasWidth:1),item);}
      else{var label=entry.Label;float alpha=screenOpacity*label.Opacity*ClientLayerAlpha(scene,label.CallerId,label.Layer);if(alpha<=0)continue;int glyphIndex=0;foreach(var glyph in label.Glyphs){var transform=MatrixD.CreateScale(label.Height)*MatrixD.CreateTranslation(label.Position+glyph.Offset*label.Height)*map;DrawScreenMesh(scene,cache,"label:"+label.Id+":"+(glyphIndex++),new SurfaceMesh{Geometry=glyph.Mesh},d,transform,depth,0,false,false,volume,alpha,label.Emission,null,world,inverse,camera,parent,ref budget,ref compileBudget,label.Color);}}
     }
    }
    catch(Exception error){ReportProjectedError(cache,error);}
   }
  }
  void DrawProjectedScreenEffects(Scene scene,IMyTerminalBlock anchor,ref int budget,ref int compileBudget)
  {
   foreach(var screen in scene.Screens.Values)
   {
    if(budget<=0)return;var d=screen.Data;ProjectedCache cache;if(!_projectedCaches.TryGetValue(PackedKey(d.CallerId,scene.ConsoleId,d.Id),out cache)||!cache.Front||cache.Seen!=_ticks||!d.Visible||d.Opacity<=0||!UseProjectedVectorFallback(d))continue;
    if(d.ContentRenderer==1&&RasterImageCurrent(cache.UiRaster)&&cache.UiRasterMesh!=null)continue;
    try
    {
     var world=anchor.WorldMatrix;var camera=MyAPIGateway.Session.Camera.Position;float screenOpacity=d.Opacity*ScreenSideOpacity(d,world,camera);if(screenOpacity<=0)continue;
     var inverse=MatrixD.Invert(world);uint parent=anchor.Render.GetRenderObjectID();var volume=SurfaceCrop(d,anchor,d.SurfaceKind==0?ProjectedVolume(scene,d,anchor):GetDisplayVolume(scene,anchor));
     for(int i=0;i<cache.Items.Count&&budget>0;i++)
     {
      var item=cache.Items[i].Item;if(item==null||item.Effects==null||CompositionEntryRasterized(cache,cache.Items[i]))continue;float opacity=screenOpacity*item.Opacity*ClientLayerAlpha(scene,item.CallerId,item.Layer);if(opacity<=0)continue;
      DrawScreenMesh(scene,cache,"item:"+item.Id,new SurfaceMesh{Geometry=item.Geometry,Colors=item.TriangleColors,EdgeColors=item.EdgeColors,UV=item.UV},d,item.Transform*ProjectedContentView(d),CompositionItemDepth(cache,i),0,true,false,volume,opacity,item.Emission,item.Material,world,inverse,camera,parent,ref budget,ref compileBudget,item.FillColor,item.LineColor,item.Thickness*(d.SurfaceKind==0?d.Width/d.CanvasWidth:1),item,true);
     }
    }
    catch(Exception error){ReportProjectedError(cache,error);}
   }
  }
  void DrawProjectedMesh(SurfaceMesh mesh,MatrixD matrix,DisplayVolume volume,float opacity,float emission,string material,MatrixD world,MatrixD inverse,Vector3D camera,uint parent,ref int budget,Vector4? fill=null,Vector4? line=null,double thickness=.008,int surfaceKind=0,HoloProjectedScreenData surface=null,double layerDepth=0,Item effectItem=null)
  {
   var localSurfaceEye=surface==null?Vector3D.Zero:Vector3D.Transform(camera,MatrixD.Invert(matrix));var g=mesh.Geometry;var effectState=HologramEffectsFor(effectItem,g);for(int i=0;i<g.Points.Length;i++)g.WorldPoints[i]=Vector3D.Transform(g.Points[i],matrix);
   for(int t=0;t<g.Triangles.Length&&budget>0;t+=3){int a=g.Triangles[t],b=g.Triangles[t+1],c=g.Triangles[t+2];var color=mesh.Colors==null?fill??Vector4.One:mesh.Colors[t/3];var wa=g.WorldPoints[a];var wb=g.WorldPoints[b];var wc=g.WorldPoints[c];float side=1;if(surface!=null){bool front;side=ScreenTriangleOpacity(surface,g.Points[a],g.Points[b],g.Points[c],localSurfaceEye,out front);if(!front&&layerDepth!=0){wa-=Vector3D.TransformNormal(ScreenPointNormal(surface,g.Points[a],g.Points[b],g.Points[c])*2*layerDepth,matrix);wb-=Vector3D.TransformNormal(ScreenPointNormal(surface,g.Points[b],g.Points[c],g.Points[a])*2*layerDepth,matrix);wc-=Vector3D.TransformNormal(ScreenPointNormal(surface,g.Points[c],g.Points[a],g.Points[b])*2*layerDepth,matrix);}}color=HologramEffectColor(effectState,color,(g.Points[a]+g.Points[b]+g.Points[c])/3,t/3);color.W*=opacity*side;if(color.W<=0)continue;DrawVolumeTriangle(volume,wa,wb,wc,mesh.UV==null?Vector2.Zero:mesh.UV[a],mesh.UV==null?Vector2.Zero:mesh.UV[b],mesh.UV==null?Vector2.Zero:mesh.UV[c],color,emission,false,material,world,inverse,camera,parent,ref budget);}
   var normal=Vector3D.Normalize(Vector3D.Cross(matrix.Right,matrix.Up));for(int e=0;e<g.Edges.Length&&budget>0;e+=2){var color=mesh.EdgeColors==null?line??Vector4.Zero:mesh.EdgeColors[e/2];color=HologramEffectColor(effectState,color,(g.Points[g.Edges[e]]+g.Points[g.Edges[e+1]])/2,g.Triangles.Length/3+e/2);color.W*=opacity;if(color.W>0){int ia=g.Edges[e],ib=g.Edges[e+1];var n=normal;if(surfaceKind>=3){var delta=camera-(g.WorldPoints[ia]+g.WorldPoints[ib])*.5;if(delta.LengthSquared()>1e-20)n=Vector3D.Normalize(delta);}else if(surfaceKind!=0){var mid=(g.Points[ia]+g.Points[ib])*.5;if(surfaceKind==1)mid.Y=0;if(mid.LengthSquared()>1e-20)n=Vector3D.Normalize(Vector3D.TransformNormal(mid,matrix));}DrawPinnedLine(volume,g.WorldPoints[ia],g.WorldPoints[ib],n,Math.Max(.0001,thickness),color,emission,world,inverse,camera,parent,ref budget);}}
  }
  static void ReleaseProjectedCache(ProjectedCache cache){var ui=cache.UiRaster;cache.UiRaster=null;cache.Mapped.Clear();ReleaseSourceSlots(cache);ClearExternalBudgetView(cache);ReleaseRasterImage(ui);}
  void ClearProjectedCaches(){var detached=new List<ProjectedCache>(_projectedCaches.Values);_projectedCaches.Clear();_lastProjectedJob=null;_lastUiRasterJob=null;foreach(var cache in detached)ReleaseProjectedCache(cache);}
  void PruneProjectedCaches()
   {var remove=new List<string>();foreach(var pair in _projectedCaches){var c=pair.Value;Scene scene;var anchor=MyAPIGateway.Entities.GetEntityById(c.Anchor) as IMyTerminalBlock;if(!_scenes.TryGetValue(c.Anchor,out scene)||!scene.Screens.ContainsKey(Key(c.Caller,c.Id))||anchor==null||anchor.Closed||!anchor.IsWorking)remove.Add(pair.Key);}foreach(string key in remove){ProjectedCache cache;if(_projectedCaches.TryGetValue(key,out cache)){_projectedCaches.Remove(key);ReleaseProjectedCache(cache);}}}
  void PruneProjectedDeclarations(Scene scene,IMyTerminalBlock anchor)
  {var remove=new List<string>();foreach(var pair in scene.Screens){var caller=MyAPIGateway.Entities.GetEntityById(pair.Value.Data.CallerId) as IMyProgrammableBlock;if(caller==null||caller.Closed||caller.OwnerId==0||!caller.IsSameConstructAs(anchor)||!anchor.HasPlayerAccess(caller.OwnerId))remove.Add(pair.Key);}foreach(string key in remove){ClearScreenContent(scene,scene.Screens[key]);scene.Screens.Remove(key);_dirty=true;}}
 }
}
