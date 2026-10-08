using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  sealed class CompositionSlotCache
  {public HoloProjectedSourceSlotData Declaration;public HoloProjectedScreenData Settings;public ProjectedCache Source;public SurfaceMesh Raw,Canvas,DemandCanvas,DemandCanvasSource;public MatrixD DemandContent;public long Revision;public string Status;public int Seen;public double Depth;}
  static string ProjectedConsumerKey(ProjectedCache c){return PackedKey(c.Caller,c.Anchor,c.Id)+(c.ConsumerSuffix??"");}
  static Vector4[] CanvasRectPlanes(double x,double y,double w,double h)
  {return new[]{new Vector4(1,0,0,(float)-x),new Vector4(-1,0,0,(float)(x+w)),new Vector4(0,1,0,(float)-y),new Vector4(0,-1,0,(float)(y+h))};}
  static bool SourceSlotIntersects(HoloProjectedScreenData d,HoloProjectedSourceSlotData s)
  {var bounds=SourceSlotCanvasBounds(d,s);return s.Visible&&s.Opacity>0&&bounds[2]>0&&bounds[3]>0&&bounds[0]<d.CanvasWidth/2&&bounds[0]+bounds[2]>-d.CanvasWidth/2&&bounds[1]<d.CanvasHeight/2&&bounds[1]+bounds[3]>-d.CanvasHeight/2;}
  static double[] SourceSlotCanvasBounds(HoloProjectedScreenData d,HoloProjectedSourceSlotData s)
  {
   double x=Math.Max(s.Rect[0],s.Clip[0]),y=Math.Max(s.Rect[1],s.Clip[1]),right=Math.Min(s.Rect[0]+s.Rect[2],s.Clip[0]+s.Clip[2]),top=Math.Min(s.Rect[1]+s.Rect[3],s.Clip[1]+s.Clip[3]);if(right<=x||top<=y)return new double[4];var matrix=(s.Pose==null?MatrixD.Identity:ReadMatrix(s.Pose))*ProjectedContentView(d);var min=new Vector3D(double.PositiveInfinity);var max=new Vector3D(double.NegativeInfinity);foreach(var p in new[]{new Vector3D(x,y,0),new Vector3D(right,y,0),new Vector3D(right,top,0),new Vector3D(x,top,0)}){var point=Vector3D.Transform(p,matrix);min=Vector3D.Min(min,point);max=Vector3D.Max(max,point);}return new[]{min.X,min.Y,max.X-min.X,max.Y-min.Y};
  }
  static Vector2I SourceSlotDemandSize(HoloProjectedScreenData d,HoloProjectedSourceSlotData s,Vector2I parent)
  {var bounds=SourceSlotCanvasBounds(d,s);double width=Math.Max(0,Math.Min(bounds[0]+bounds[2],d.CanvasWidth/2)-Math.Max(bounds[0],-d.CanvasWidth/2)),height=Math.Max(0,Math.Min(bounds[1]+bounds[3],d.CanvasHeight/2)-Math.Max(bounds[1],-d.CanvasHeight/2));return DisplayLod.MaximumVisible(new Vector2I(Math.Max(16,Math.Min(4096,(int)Math.Ceiling(parent.X*width/d.CanvasWidth/s.UV[2]))),Math.Max(16,Math.Min(4096,(int)Math.Ceiling(parent.Y*height/d.CanvasHeight/s.UV[3])))),WideSourceRaster(s.Provider)).Resolution;}
  bool SourceSlotVisible(Scene scene,HoloProjectedScreenData d,HoloProjectedSourceSlotData s)
  {return ExternalBudgetScreenVisible(d)&&SourceSlotIntersects(d,s)&&(s.Layer==null||ClientLayerAlpha(scene,d.CallerId,ScreenLayer(d.Id,s.Layer))>0);}
  static void ReleaseSourceSlots(ProjectedCache cache)
  {var slots=new List<CompositionSlotCache>(cache.Slots.Values);cache.Slots.Clear();var chunks=new List<ProjectedCache>(cache.UiChunks.Values);cache.UiChunks.Clear();var dead=new List<string>();foreach(var pair in cache.Mapped)if(pair.Key.StartsWith("slot:",StringComparison.Ordinal)||pair.Key.StartsWith("chunk:",StringComparison.Ordinal))dead.Add(pair.Key);foreach(string id in dead)cache.Mapped.Remove(id);foreach(var s in slots)ReleaseProjectedCache(s.Source);foreach(var chunk in chunks)ReleaseProjectedCache(chunk);}
  bool PrepareCompositionMode(Scene scene,HoloProjectedScreenData d,ProjectedCache cache)
  {if(d.SourceSlots!=null)foreach(var slot in d.SourceSlots)if(SourceSlotVisible(scene,d,slot))return true;ReleaseSourceSlots(cache);return false;}
  object SourceSlotStatus(DrawContext context,HoloProjectedScreenData d,HoloProjectedSourceSlotData slot)
  {
   ProjectedCache cache;CompositionSlotCache current;string state="Pending";
   if(!slot.Visible||slot.Opacity<=0)state="Hidden";
   else if(!_displayProviders.ContainsKey(slot.Provider))state="RequiresPlugin: "+slot.Provider;
   else if(_projectedCaches.TryGetValue(PackedKey(d.CallerId,context.Target.EntityId,d.Id),out cache)&&cache.Slots.TryGetValue(slot.Id,out current)){state=current.Revision!=slot.Revision?"Pending":current.Status??(current.Canvas==null?"Pending":"Ready");MappedScreenMesh mapped;if(state=="Ready"&&d.SurfaceKind!=0&&cache.Mapped.TryGetValue("slot:"+slot.Id,out mapped)&&mapped.Error!=null)state="Renderer: "+mapped.Error;}
   return new MyTuple<bool,string>(state=="Ready",state);
  }
  CompositionSlotCache EnsureSourceSlotCache(ProjectedCache parent,HoloProjectedSourceSlotData slot)
  {
   CompositionSlotCache cache;if(!parent.Slots.TryGetValue(slot.Id,out cache))
   {cache=new CompositionSlotCache{Source=new ProjectedCache{Anchor=parent.Anchor,Caller=parent.Caller,Id=parent.Id,ConsumerSuffix=":slot:"+slot.Id}};parent.Slots.Add(slot.Id,cache);}return cache;
  }
  // Keep native entry semantics on their real declaration. Reusing this texture
  // is a video window; it does not relocate the transported portal entry.
  HoloProjectedScreenData SourceSlotRequestSettings(Scene scene,HoloProjectedScreenData parent,HoloProjectedSourceSlotData slot)
  {
   var d=SourceSlotSettings(parent,slot);
   if(slot.Provider=="native-portal")
   {ProjectedScreen portal;if(!scene.Screens.TryGetValue(Key(parent.CallerId,slot.SourceId),out portal)||portal.Data.SourceProvider!="native-portal"||portal.Data.Portal==null)return null;d=CloneScreen(portal.Data);d.SourceSlots=null;d.CanvasWidth=slot.Rect[2];d.CanvasHeight=slot.Rect[3];if(slot.RefreshHz>0)d.RefreshHz=slot.RefreshHz;}
   return d;
  }
  void PrepareSourceSlots(Scene scene,HoloProjectedScreenData d,ProjectedCache parent,ref int compileBudget)
  {
   var retained=new HashSet<string>();
   if(d.SourceSlots!=null)foreach(var slot in d.SourceSlots)
   {
    retained.Add(slot.Id);var cache=EnsureSourceSlotCache(parent,slot);cache.Seen=_ticks;cache.Declaration=slot;
    if(!SourceSlotVisible(scene,d,slot)){ClearExternalBudgetView(cache.Source);cache.Raw=cache.Canvas=null;cache.Status="Hidden";continue;}
    var settings=SourceSlotRequestSettings(scene,d,slot);
    if(settings==null){ClearExternalBudgetView(cache.Source);cache.Raw=cache.Canvas=null;cache.Status="Unsupported composition: portal-window-entry requires an existing declared portal source.";continue;}
    if(cache.Revision!=slot.Revision||cache.Settings==null||!SameCameraSourceSettings(cache.Settings,settings)||cache.Settings.RefreshHz!=settings.RefreshHz||!SameArray(cache.Settings.Camera,settings.Camera)||cache.Settings.CanvasWidth!=settings.CanvasWidth||cache.Settings.CanvasHeight!=settings.CanvasHeight||cache.Settings.SourceProvider!=settings.SourceProvider||cache.Settings.SourceId!=settings.SourceId)
    {ClearExternalBudgetView(cache.Source);cache.Raw=cache.Canvas=null;cache.Source.NextCapture=-1;cache.Revision=slot.Revision;}
    cache.Declaration=slot;cache.Settings=settings;cache.Source.Data=settings;cache.Source.Seen=_ticks;cache.Source.Front=true;
    ValidateCachedExternalView(scene,settings,cache.Source);if(cache.Source.View==null){cache.Raw=cache.Canvas=null;}
    double now=_ticks/60d;if(cache.Source.NextCapture>=0&&now+1e-8<cache.Source.NextCapture)continue;
    DisplayProvider provider;if(!_displayProviders.TryGetValue(slot.Provider,out provider)){cache.Status="RequiresPlugin: "+slot.Provider;continue;}
    if(provider.Protocol==2){if(_rasterSourceTick!=_ticks){_rasterSourceTick=_ticks;_rasterSourceCalls=0;}if(_rasterSourceCalls>=4)continue;_rasterSourceCalls++;cache.Source.SourceAttemptTick=_ticks;}
    else{if(compileBudget<=0||_lastCompileTick==_ticks)continue;compileBudget--;_lastCompileTick=_ticks;}
    cache.Source.NextCapture=now+1/SourceRefreshLimit(settings);
    try
    {
     int points,primitives;GetExternalSourceRenderBudget(scene,cache.Source,out points,out primitives);cache.Source.SourcePointShare=points;cache.Source.SourcePrimitiveShare=primitives;
     if(points<=0||primitives<=0||provider.Protocol==2&&(points<4||primitives<2)){ClearExternalBudgetView(cache.Source);cache.Raw=cache.Canvas=null;cache.Status="Pending: shared render budget exhausted.";continue;}
     SurfaceMesh raw;using(GeometryWork.Begin(200000))raw=BuildExternalSource(scene,settings,cache.Source,points,primitives);
     if(raw==null){ClearExternalBudgetView(cache.Source);cache.Raw=cache.Canvas=null;cache.Status="Pending";continue;}
     if(!ExternalEvidenceValid(scene,settings,cache.Source)){ClearExternalBudgetView(cache.Source);cache.Raw=cache.Canvas=null;cache.Status="Pending";continue;}
     cache.Source.View=raw;cache.Raw=raw;
     using(GeometryWork.Begin(200000))cache.Canvas=SourceSlotCanvasMesh(raw,slot,cache.Source.SourceTexture);
     int usedPoints,usedPrimitives;ActiveRenderCounts(scene,null,null,false,false,out usedPoints,out usedPrimitives);if(usedPoints>scene.PointBudget||usedPrimitives>scene.PrimitiveBudget){ClearExternalBudgetView(cache.Source);cache.Raw=cache.Canvas=null;cache.Status="Pending: clipped source exceeds the shared render budget.";continue;}
     cache.Status="Ready";
    }
    catch(Exception e){ClearExternalBudgetView(cache.Source);cache.Raw=cache.Canvas=null;cache.Status=e.Message;ReportProjectedError(parent,e);}
   }
   var dead=new List<string>();foreach(var pair in parent.Slots)if(!retained.Contains(pair.Key))dead.Add(pair.Key);foreach(string id in dead){var cache=parent.Slots[id];parent.Slots.Remove(id);ReleaseProjectedCache(cache.Source);parent.Mapped.Remove("slot:"+id);}
  }
  static SurfaceMesh SourceSlotCanvasMesh(SurfaceMesh raw,HoloProjectedSourceSlotData slot,bool texture)
  {
   var rect=slot.Rect;SurfaceMesh mesh;
   if(texture)
   {
    mesh=TransformCanvasMesh(raw,MatrixD.CreateTranslation(rect[0]+rect[2]/2,rect[1]+rect[3]/2,0));
    if(raw.UV!=null){mesh.UV=new Vector2[raw.UV.Length];var min=raw.UV[3];var extent=raw.UV[1]-min;for(int i=0;i<mesh.UV.Length;i++){var u=new Vector2((float)((raw.Geometry.Points[i].X+rect[2]/2)/rect[2]),(float)((rect[3]/2-raw.Geometry.Points[i].Y)/rect[3]));mesh.UV[i]=new Vector2(min.X+(slot.UV[0]+u.X*slot.UV[2])*extent.X,min.Y+(slot.UV[1]+u.Y*slot.UV[3])*extent.Y);}}
   }
   else
   {
    // The UV window has the same meaning for vector providers: crop the source
    // canvas first, then fit that source window into the local rectangle.
    double x=-rect[2]/2+slot.UV[0]*rect[2],y=rect[3]/2-(slot.UV[1]+slot.UV[3])*rect[3],w=slot.UV[2]*rect[2],h=slot.UV[3]*rect[3];
    var cropped=MeshEffects.Clip(raw,CanvasRectPlanes(x,y,w,h));var transform=MatrixD.CreateTranslation(-x,-y,0)*MatrixD.CreateScale(rect[2]/w,rect[3]/h,1)*MatrixD.CreateTranslation(rect[0],rect[1],0);mesh=TransformCanvasMesh(cropped,transform);
   }
   mesh=MeshEffects.Clip(mesh,CanvasRectPlanes(slot.Clip[0],slot.Clip[1],slot.Clip[2],slot.Clip[3]));
   mesh=MeshEffects.Clip(mesh,CanvasRectPlanes(rect[0],rect[1],rect[2],rect[3]));return slot.Pose==null?mesh:TransformCanvasMesh(mesh,ReadMatrix(slot.Pose));
  }
  List<CompositionSlotCache> OrderedSourceSlots(ProjectedCache cache)
  {var slots=new List<CompositionSlotCache>();foreach(var c in cache.Slots.Values)if(c.Declaration!=null)slots.Add(c);slots.Sort((a,b)=>{int order=a.Declaration.Order.CompareTo(b.Declaration.Order);return order!=0?order:string.CompareOrdinal(a.Declaration.Id,b.Declaration.Id);});return slots;}
  void DrawSourceSlot(Scene scene,ProjectedCache parent,CompositionSlotCache cache,HoloProjectedScreenData d,double depth,DisplayVolume volume,float screenOpacity,MatrixD world,MatrixD inverse,Vector3D camera,uint renderParent,ref int budget,ref int compileBudget)
  {
   var slot=cache.Declaration;if(cache.Canvas==null||!SourceSlotVisible(scene,d,slot))return;
   if(!ExternalEvidenceValid(scene,cache.Settings,cache.Source)||cache.Source.SourceRaster!=null&&!RasterImageCurrent(cache.Source.SourceRaster)){ClearExternalBudgetView(cache.Source);cache.Raw=cache.Canvas=null;cache.Status="Pending";return;}
   float opacity=screenOpacity*slot.Opacity*(slot.Layer==null?1:ClientLayerAlpha(scene,d.CallerId,ScreenLayer(d.Id,slot.Layer)));cache.Depth=depth;
   DrawScreenMesh(scene,parent,"slot:"+slot.Id,cache.Canvas,d,ProjectedContentView(d),depth,0,true,false,volume,opacity,0,cache.Source.SourceTextureMaterial,world,inverse,camera,renderParent,ref budget,ref compileBudget);
  }
  void FlushSourceSlotProvider(ProjectedCache parent,string provider)
  {foreach(var s in parent.Slots.Values)if(s.Source.SourceProvider==provider||s.Declaration!=null&&s.Declaration.Provider==provider){ClearExternalBudgetView(s.Source);s.Source.NextCapture=-1;s.Raw=s.Canvas=null;s.Status="Pending";parent.Mapped.Remove("slot:"+s.Declaration.Id);}}
  void DrawCompositionContent(Scene scene,ProjectedCache cache,HoloProjectedScreenData d,bool sampled,DisplayVolume volume,float opacity,MatrixD world,MatrixD inverse,Vector3D camera,uint renderParent,ref int budget,ref int compileBudget)
  {
   var slots=OrderedSourceSlots(cache);int item=0,sequence=0;var chunks=new HashSet<string>();
   for(int s=0;s<=slots.Count&&budget>0;s++)
   {
    int start=item;while(item<cache.Items.Count&&(s==slots.Count||cache.Items[item].Order<=slots[s].Declaration.Order))item++;
    if(item>start)
    {
     double depth=.0015+sequence*.00001;string chunkId="chunk:"+s;bool raster=false;
     if(d.ContentRenderer==1)
     {
      chunks.Add(chunkId);ProjectedCache chunk;if(!cache.UiChunks.TryGetValue(chunkId,out chunk)){chunk=new ProjectedCache{Anchor=cache.Anchor,Caller=cache.Caller,Id=cache.Id,ConsumerSuffix=":"+chunkId};cache.UiChunks.Add(chunkId,chunk);}
      if(sampled||chunk.Data==null||!SameSurface(chunk.Data,d)||!SameArray(chunk.Data.View,d.View)||!RasterLayersCurrent(scene,chunk)){ReleaseUiRaster(chunk);chunk.Items.Clear();for(int j=start;j<item;j++)chunk.Items.Add(cache.Items[j]);chunk.Data=d;chunk.NextSample=-1;}
      if(chunk.UiRaster==null&&_uiRasterTick!=_ticks){_uiRasterTick=_ticks;chunk.UiAttemptTick=_ticks;PrepareScreenUiRaster(scene,d,chunk);}
      raster=RasterImageCurrent(chunk.UiRaster)&&chunk.UiRasterMesh!=null;
      if(raster)DrawScreenMesh(scene,cache,chunkId,chunk.UiRasterMesh,d,MatrixD.Identity,depth,0,true,true,volume,opacity,0,chunk.UiRaster.Material,world,inverse,camera,renderParent,ref budget,ref compileBudget);
     }
     if(!raster&&UseProjectedVectorFallback(d))for(int j=start;j<item&&budget>0;j++)DrawCompositionEntry(scene,cache,d,cache.Items[j],.0015+(sequence+j-start)*.00001,volume,opacity,world,inverse,camera,renderParent,ref budget,ref compileBudget);
     sequence+=item-start;
    }
    if(s<slots.Count){DrawSourceSlot(scene,cache,slots[s],d,.0015+(sequence++)*.00001,volume,opacity,world,inverse,camera,renderParent,ref budget,ref compileBudget);}
   }
   var dead=new List<string>();foreach(var pair in cache.UiChunks)if(!chunks.Contains(pair.Key))dead.Add(pair.Key);foreach(string id in dead){ReleaseProjectedCache(cache.UiChunks[id]);cache.UiChunks.Remove(id);cache.Mapped.Remove(id);}
  }
  void DrawCompositionEntry(Scene scene,ProjectedCache cache,HoloProjectedScreenData d,LcdVectorEntry entry,double depth,DisplayVolume volume,float screenOpacity,MatrixD world,MatrixD inverse,Vector3D camera,uint renderParent,ref int budget,ref int compileBudget)
  {
   var map=ProjectedContentView(d);
   if(entry.Item!=null){var item=entry.Item;float opacity=screenOpacity*item.Opacity*ClientLayerAlpha(scene,item.CallerId,item.Layer);if(opacity<=0)return;DrawScreenMesh(scene,cache,"item:"+item.Id,new SurfaceMesh{Geometry=item.Geometry,Colors=item.TriangleColors,EdgeColors=item.EdgeColors,UV=item.UV},d,item.Transform*map,depth,0,true,false,volume,opacity,item.Emission,item.Material,world,inverse,camera,renderParent,ref budget,ref compileBudget,item.FillColor,item.LineColor,item.Thickness*(d.SurfaceKind==0?d.Width/d.CanvasWidth:1),item);}
   else{var label=entry.Label;float alpha=screenOpacity*label.Opacity*ClientLayerAlpha(scene,label.CallerId,label.Layer);if(alpha<=0)return;int glyphIndex=0;foreach(var glyph in label.Glyphs){var transform=MatrixD.CreateScale(label.Height)*MatrixD.CreateTranslation(label.Position+glyph.Offset*label.Height)*map;DrawScreenMesh(scene,cache,"label:"+label.Id+":"+(glyphIndex++),new SurfaceMesh{Geometry=glyph.Mesh},d,transform,depth,0,false,false,volume,alpha,label.Emission,null,world,inverse,camera,renderParent,ref budget,ref compileBudget,label.Color);}}
  }
  double CompositionItemDepth(ProjectedCache cache,int index)
  {int preceding=0;foreach(var slot in cache.Slots.Values)if(slot.Declaration!=null&&slot.Declaration.Order<cache.Items[index].Order)preceding++;return .0015+(index+preceding)*.00001;}
  bool CompositionEntryRasterized(ProjectedCache cache,LcdVectorEntry entry)
  {foreach(var chunk in cache.UiChunks.Values)if(chunk.UiRasterMesh!=null&&RasterImageCurrent(chunk.UiRaster))foreach(var saved in chunk.Items)if(saved.Caller==entry.Caller&&saved.Id==entry.Id)return true;return false;}
 }
}
