using System;

namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  static bool ExternalBudgetScreenVisible(HoloProjectedScreenData data)
  {return data!=null&&data.Visible&&data.Opacity>0;}
  bool ExternalBudgetItemVisible(Scene scene,CompiledDisplay cache)
  {
   Item item;if(cache.ConsoleId!=scene.ConsoleId||cache.Mesh==null||!scene.Items.TryGetValue(Key(cache.CallerId,cache.Id),out item)||!item.Visible||item.Opacity<=0||ClientLayerAlpha(scene,item.CallerId,item.Layer)<=0)return false;
   if(!IsProjectedId(item.Id))return true;
   int end=item.Id.IndexOf('!',3);if(end<0)return false;ProjectedScreen screen;
   return scene.Screens.TryGetValue(Key(item.CallerId,item.Id.Substring(3,end-3)),out screen)&&ExternalBudgetScreenVisible(screen.Data);
  }
  static void ExternalBudgetCount(SurfaceMesh mesh,ref int points,ref int primitives)
  {if(mesh==null)return;points+=mesh.Geometry.Points.Length;primitives+=mesh.Geometry.Triangles.Length/3+mesh.Geometry.Edges.Length/2;}
  static void ClearExternalBudgetView(ProjectedCache cache)
  {var image=cache.SourceRaster;var endpoint=cache.SourceProtocol==2?cache.SourceEndpoint:null;var evidence=cache.SourceEvidence;cache.SourceRaster=null;cache.SourceTexture=false;cache.SourceTextureMaterial=null;cache.SourceTextureSize=VRageMath.Vector2I.Zero;cache.View=null;cache.ViewVersion=null;cache.Raster=null;cache.SourceEvidence=null;cache.SourceEndpoint=null;cache.SourceProvider=null;cache.SourceId=null;cache.SourceGeneration=0;cache.SourceProtocol=0;cache.SourceReduced=false;cache.SourcePointShare=cache.SourcePrimitiveShare=0;cache.Mapped.Remove("source");ReleaseProviderEvidence(endpoint,evidence);ReleaseRasterImage(image);}
  static void ReleaseProviderEvidence(Func<string,object[],object> endpoint,object evidence)
  {if(endpoint==null||evidence==null)return;try{endpoint("release",new object[]{evidence});}catch{}}
  // One accounting rule for static admission, source allocation and final
  // projected admission. Cached invisible or orphaned payloads cost no draw room.
  void ActiveRenderCounts(Scene scene,CompiledDisplay replaceItem,ProjectedCache replaceProjected,bool replaceSprite,bool excludeExternalViews,out int points,out int primitives)
  {
   points=primitives=0;
   foreach(var screen in scene.Screens.Values)if(ExternalBudgetScreenVisible(screen.Data)){points+=4;primitives+=2;}
   foreach(var item in _clientGeometry.Values)if(item!=replaceItem&&ExternalBudgetItemVisible(scene,item))ExternalBudgetCount(item.Mesh,ref points,ref primitives);
   foreach(var cache in _projectedCaches.Values)
   {
    ProjectedScreen screen;if(cache.Anchor!=scene.ConsoleId||!scene.Screens.TryGetValue(Key(cache.Caller,cache.Id),out screen)||!ExternalBudgetScreenVisible(screen.Data))continue;
    if(cache!=replaceProjected||!replaceSprite)ExternalBudgetCount(cache.Sprites,ref points,ref primitives);
    if((cache!=replaceProjected||replaceSprite)&&(!excludeExternalViews||!HasExternalSource(screen.Data)))ExternalBudgetCount(cache.View,ref points,ref primitives);
    if(screen.Data.ContentRenderer==1)ExternalBudgetCount(cache.UiRasterMesh,ref points,ref primitives);
   }
  }
  // Static content owns its existing allocation. External sources share only what
  // remains; no previously cached external view can monopolize a sibling's share.
  void GetExternalSourceRenderBudget(Scene scene,ProjectedCache current,out int points,out int primitives)
  {
   points=primitives=0;if(scene==null)return;int usedPoints,usedPrimitives,sources=0;
   ActiveRenderCounts(scene,null,null,false,true,out usedPoints,out usedPrimitives);
   foreach(var screen in scene.Screens.Values)if(ExternalBudgetScreenVisible(screen.Data)&&HasExternalSource(screen.Data))sources++;
   foreach(var cache in _projectedCaches.Values)
   {
    if(cache.Anchor!=scene.ConsoleId)continue;ProjectedScreen screen;
    if(!scene.Screens.TryGetValue(Key(cache.Caller,cache.Id),out screen)||!ExternalBudgetScreenVisible(screen.Data))
    {ClearExternalBudgetView(cache);cache.Sprites=null;cache.SpriteVersion=null;cache.Items.Clear();continue;}
   }
   int pointShare=sources==0?0:Math.Min(Geometry.MaxPoints,Math.Max(0,scene.PointBudget-usedPoints)/sources);
   int primitiveShare=sources==0?0:Math.Min(Geometry.MaxPrimitives,Math.Max(0,scene.PrimitiveBudget-usedPrimitives)/sources);
   foreach(var cache in _projectedCaches.Values)
   {
    ProjectedScreen screen;if(cache.Anchor!=scene.ConsoleId||!scene.Screens.TryGetValue(Key(cache.Caller,cache.Id),out screen)||!ExternalBudgetScreenVisible(screen.Data)||!HasExternalSource(screen.Data)||cache.View==null)continue;
    var geometry=cache.View.Geometry;if(geometry.Points.Length>pointShare||geometry.Triangles.Length/3+geometry.Edges.Length/2>primitiveShare)ClearExternalBudgetView(cache);
   }
   ProjectedScreen selected;
   if(current!=null&&current.Anchor==scene.ConsoleId&&scene.Screens.TryGetValue(Key(current.Caller,current.Id),out selected)&&ExternalBudgetScreenVisible(selected.Data)&&HasExternalSource(selected.Data)){points=pointShare;primitives=primitiveShare;}
  }
  // Called when a pending static mesh needs room; capture data stays untouched.
  void ReclaimExternalViews(Scene scene)
  {
   if(scene==null)return;
   foreach(var cache in _projectedCaches.Values){ProjectedScreen screen;if(cache.Anchor==scene.ConsoleId&&scene.Screens.TryGetValue(Key(cache.Caller,cache.Id),out screen)&&HasExternalSource(screen.Data))ClearExternalBudgetView(cache);}
  }
 }
}
