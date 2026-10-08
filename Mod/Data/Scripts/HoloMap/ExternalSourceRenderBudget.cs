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
  static int AddExternalBudgetCount(int count,int additional){return (int)Math.Min(int.MaxValue,(long)count+additional);}
  static void ExternalBudgetCount(SurfaceMesh mesh,ref int points,ref int primitives)
  {if(mesh==null)return;points=AddExternalBudgetCount(points,mesh.Geometry.Points.Length);primitives=AddExternalBudgetCount(primitives,mesh.Geometry.Triangles.Length/3);primitives=AddExternalBudgetCount(primitives,mesh.Geometry.Edges.Length/2);}
  static int ExternalSourceShare(int allowance,int used,int sources,int maximum)
  {return sources==0?0:allowance==int.MaxValue?maximum:(int)Math.Min(maximum,Math.Max(0L,(long)allowance-used)/sources);}
  static void ClearExternalBudgetView(ProjectedCache cache)
  {var image=cache.SourceRaster;var endpoint=cache.SourceProtocol==2?cache.SourceEndpoint:null;var evidence=cache.SourceEvidence;cache.SourceRaster=null;cache.SourceTexture=false;cache.SourceTextureMaterial=null;cache.SourceTextureSize=VRageMath.Vector2I.Zero;cache.View=null;cache.ViewVersion=null;cache.Raster=null;cache.SourceEvidence=null;cache.SourceEndpoint=null;cache.SourceProvider=null;cache.SourceId=null;cache.SourceGeneration=0;cache.SourceProtocol=0;cache.SourceReduced=false;cache.SourcePointShare=cache.SourcePrimitiveShare=0;cache.Mapped.Remove("source");ReleaseProjectedProviderEvidence(endpoint,evidence,cache);ReleaseRasterImage(image);}
  static void ReleaseProviderEvidence(Func<string,object[],object> endpoint,object evidence)
  {if(endpoint==null||evidence==null)return;ProviderEvidenceOwnership saved=FindProviderEvidence(endpoint,evidence);if(saved!=null){if(saved.Count>0)saved.Count--;if(saved.Count>0||saved.ProjectedOwners.Count>0)return;_providerEvidenceOwnership.Remove(saved);}try{endpoint("release",new object[]{evidence});}catch{}}
  static void ReleaseProjectedProviderEvidence(Func<string,object[],object> endpoint,object evidence,ProjectedCache cache)
  {if(endpoint==null||evidence==null)return;var saved=FindProviderEvidence(endpoint,evidence);if(saved!=null){saved.ProjectedOwners.Remove(cache);if(saved.Count>0||saved.ProjectedOwners.Count>0)return;_providerEvidenceOwnership.Remove(saved);}try{endpoint("release",new object[]{evidence});}catch{}}
  sealed class ProviderEvidenceOwnership
  {public Func<string,object[],object> Endpoint;public object Evidence;public int Count;public readonly System.Collections.Generic.List<ProjectedCache> ProjectedOwners=new System.Collections.Generic.List<ProjectedCache>();}
  static readonly System.Collections.Generic.List<ProviderEvidenceOwnership> _providerEvidenceOwnership=new System.Collections.Generic.List<ProviderEvidenceOwnership>();
  static ProviderEvidenceOwnership FindProviderEvidence(Func<string,object[],object> endpoint,object evidence)
  {
   for(int i=0;i<_providerEvidenceOwnership.Count;i++)
   {
    var value=_providerEvidenceOwnership[i];if(!ReferenceEquals(value.Endpoint,endpoint)||!ReferenceEquals(value.Evidence,evidence))continue;
    // A stale cache claim cannot protect an offering from another endpoint.
    // Cache teardown detaches its fields before callbacks; surviving sibling
    // caches still carry this exact protocol/endpoint/evidence ownership.
    for(int j=value.ProjectedOwners.Count-1;j>=0;j--){var owner=value.ProjectedOwners[j];if(owner.SourceProtocol!=2||!ReferenceEquals(owner.SourceEndpoint,endpoint)||!ReferenceEquals(owner.SourceEvidence,evidence))value.ProjectedOwners.RemoveAt(j);}
    if(value.Count==0&&value.ProjectedOwners.Count==0){_providerEvidenceOwnership.RemoveAt(i);return null;}return value;
   }
   return null;
  }
  static void RetainProviderEvidence(Func<string,object[],object> endpoint,object evidence)
  {if(endpoint==null||evidence==null)return;var owned=FindProviderEvidence(endpoint,evidence);if(owned!=null){owned.Count++;return;}_providerEvidenceOwnership.Add(new ProviderEvidenceOwnership{Endpoint=endpoint,Evidence=evidence,Count=1});}
  static void RetainProjectedProviderEvidence(Func<string,object[],object> endpoint,object evidence,ProjectedCache cache)
  {if(endpoint==null||evidence==null)return;var owned=FindProviderEvidence(endpoint,evidence);if(owned==null){owned=new ProviderEvidenceOwnership{Endpoint=endpoint,Evidence=evidence};_providerEvidenceOwnership.Add(owned);}if(!owned.ProjectedOwners.Contains(cache))owned.ProjectedOwners.Add(cache);}
  static void ReleaseUnownedProviderEvidence(Func<string,object[],object> endpoint,object evidence)
  {if(FindProviderEvidence(endpoint,evidence)==null)ReleaseProviderEvidence(endpoint,evidence);}
  // One accounting rule for static admission, source allocation and final
  // projected admission. Cached invisible or orphaned payloads cost no draw room.
  void ActiveRenderCounts(Scene scene,CompiledDisplay replaceItem,ProjectedCache replaceProjected,bool replaceSprite,bool excludeExternalViews,out int points,out int primitives)
  {
   points=primitives=0;
   foreach(var screen in scene.Screens.Values)if(ExternalBudgetScreenVisible(screen.Data)){points=AddExternalBudgetCount(points,4);primitives=AddExternalBudgetCount(primitives,2);}
   foreach(var item in _clientGeometry.Values)if(item!=replaceItem&&ExternalBudgetItemVisible(scene,item))ExternalBudgetCount(item.Mesh,ref points,ref primitives);
   foreach(var cache in _projectedCaches.Values)
   {
    ProjectedScreen screen;if(cache.Anchor!=scene.ConsoleId||!scene.Screens.TryGetValue(Key(cache.Caller,cache.Id),out screen)||!ExternalBudgetScreenVisible(screen.Data))continue;
    if(cache!=replaceProjected||!replaceSprite)ExternalBudgetCount(cache.Sprites,ref points,ref primitives);
    if((cache!=replaceProjected||replaceSprite)&&(!excludeExternalViews||!HasExternalSource(screen.Data)))ExternalBudgetCount(cache.View,ref points,ref primitives);
    if(screen.Data.ContentRenderer==1)ExternalBudgetCount(cache.UiRasterMesh,ref points,ref primitives);
    foreach(var slot in cache.Slots.Values)if(slot.Declaration!=null&&SourceSlotVisible(scene,screen.Data,slot.Declaration)&&slot.Source!=replaceProjected&&(!excludeExternalViews))ExternalBudgetCount(slot.Canvas,ref points,ref primitives);
    foreach(var chunk in cache.UiChunks.Values)ExternalBudgetCount(chunk.UiRasterMesh,ref points,ref primitives);
   }
  }
  // Static content owns its existing allocation. External sources share only what
  // remains; no previously cached external view can monopolize a sibling's share.
  void GetExternalSourceRenderBudget(Scene scene,ProjectedCache current,out int points,out int primitives)
  {
   points=primitives=0;if(scene==null)return;int usedPoints,usedPrimitives,sources=0;
   ActiveRenderCounts(scene,null,null,false,true,out usedPoints,out usedPrimitives);
   foreach(var screen in scene.Screens.Values)if(ExternalBudgetScreenVisible(screen.Data)){if(HasExternalSource(screen.Data))sources++;if(screen.Data.SourceSlots!=null)foreach(var slot in screen.Data.SourceSlots)if(SourceSlotVisible(scene,screen.Data,slot))sources++;}
   foreach(var cache in _projectedCaches.Values)
   {
    if(cache.Anchor!=scene.ConsoleId)continue;ProjectedScreen screen;
    if(!scene.Screens.TryGetValue(Key(cache.Caller,cache.Id),out screen)||!ExternalBudgetScreenVisible(screen.Data))
    {ClearExternalBudgetView(cache);ReleaseSourceSlots(cache);cache.Sprites=null;cache.SpriteVersion=null;cache.Items.Clear();continue;}
   }
   int pointShare=ExternalSourceShare(scene.PointBudget,usedPoints,sources,Geometry.MaxPoints);
   int primitiveShare=ExternalSourceShare(scene.PrimitiveBudget,usedPrimitives,sources,Geometry.MaxPrimitives);
   foreach(var cache in _projectedCaches.Values)
   {
    ProjectedScreen screen;if(cache.Anchor!=scene.ConsoleId||!scene.Screens.TryGetValue(Key(cache.Caller,cache.Id),out screen)||!ExternalBudgetScreenVisible(screen.Data)||!HasExternalSource(screen.Data)||cache.View==null)continue;
    var geometry=cache.View.Geometry;if(geometry.Points.Length>pointShare||geometry.Triangles.Length/3+geometry.Edges.Length/2>primitiveShare)ClearExternalBudgetView(cache);
   }
   ProjectedScreen selected;
   if(current!=null&&current.Anchor==scene.ConsoleId&&scene.Screens.TryGetValue(Key(current.Caller,current.Id),out selected)&&ExternalBudgetScreenVisible(selected.Data)&&(HasExternalSource(selected.Data)||current.ConsumerSuffix!=null&&current.ConsumerSuffix.StartsWith(":slot:",StringComparison.Ordinal))){points=pointShare;primitives=primitiveShare;}
  }
  // Called when a pending static mesh needs room; capture data stays untouched.
  void ReclaimExternalViews(Scene scene)
  {
   if(scene==null)return;
   foreach(var cache in _projectedCaches.Values){ProjectedScreen screen;if(cache.Anchor==scene.ConsoleId&&scene.Screens.TryGetValue(Key(cache.Caller,cache.Id),out screen)){if(HasExternalSource(screen.Data))ClearExternalBudgetView(cache);foreach(var slot in cache.Slots.Values){ClearExternalBudgetView(slot.Source);slot.Raw=slot.Canvas=null;}}}
  }
 }
}
