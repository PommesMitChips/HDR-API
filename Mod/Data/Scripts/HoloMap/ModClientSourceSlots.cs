using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  sealed class ModClientSourceContext
  {public ModClientOwner Owner;public ModClientContext Context;public readonly Dictionary<string,ModClientSourceSlot> Slots=new Dictionary<string,ModClientSourceSlot>();}
  sealed class ModClientSourceSlot
  {
   public HoloProjectedSourceSlotData Declaration;public ModClientNativeBinding Binding;
   public Func<string,object[],object> Endpoint;public long EndpointGeneration;public object Evidence;
   public RasterImage Raster;public ModClientItem Item;public double NextCapture=-1;public string Status="Pending";
  }
  readonly Dictionary<ModClientContext,ModClientSourceContext> _modClientSourceContexts=new Dictionary<ModClientContext,ModClientSourceContext>();
  long _modClientSourceRevision;
  ModClientSourceContext ModClientSources(ModClientOwner owner,ModClientContext c)
  {
   ModClientSourceContext state;if(!_modClientSourceContexts.TryGetValue(c,out state)){state=new ModClientSourceContext{Owner=owner,Context=c};_modClientSourceContexts.Add(c,state);}return state;
  }
  static HoloProjectedSourceSlotData ModClientCloneSlot(HoloProjectedSourceSlotData slot)
  {return CloneSourceSlots(new List<HoloProjectedSourceSlotData>{slot})[0];}
  HoloProjectedScreenData ModClientSourceCanvas(ModClientContext c)
  {
   if(!c.Hud)return c.Surface;
   var camera=MyAPIGateway.Session==null?null:MyAPIGateway.Session.Camera;var size=camera==null?Vector2.Zero:camera.ViewportSize;
   return new HoloProjectedScreenData{Id="hud",CanvasWidth=Math.Max(1,size.X),CanvasHeight=Math.Max(1,size.Y),Width=Math.Max(1,size.X),Height=Math.Max(1,size.Y),Pose=MatrixValues(MatrixD.Identity),SurfaceSide=1,UiRasterWidth=Math.Max(16,Math.Min(4096,(int)size.X)),UiRasterHeight=Math.Max(16,Math.Min(4096,(int)size.Y))};
  }
  static SurfaceMesh ModClientSourceCanvasMesh(SurfaceMesh raw,HoloProjectedSourceSlotData slot,bool texture,bool hud)
  {
   if(!hud)return SourceSlotCanvasMesh(raw,slot,texture);
   // Source frames retain the centered +Y-up contract. HUD destinations use
   // top-left +Y-down pixels, including clipping and the final canvas pose.
   var reflected=ModClientCloneSlot(slot);reflected.Rect[1]=-slot.Rect[1]-slot.Rect[3];reflected.Clip[1]=-slot.Clip[1]-slot.Clip[3];reflected.Pose=null;
   return TransformCanvasMesh(SourceSlotCanvasMesh(raw,reflected,texture),MatrixD.CreateScale(1,-1,1)*(slot.Pose==null?MatrixD.Identity:ReadMatrix(slot.Pose)));
  }
  bool ModClientSourceIntersects(ModClientContext c,HoloProjectedSourceSlotData slot)
  {
   if(!c.Hud)return c.Surface!=null&&SourceSlotIntersects(c.Surface,slot);
   if(!slot.Visible||slot.Opacity<=0)return false;
   var mesh=ModClientSourceCanvasMesh(RasterQuad(slot.Rect[2],slot.Rect[3]),slot,true,true);var viewport=ModClientSourceCanvas(c);
   var clipped=MeshEffects.Clip(mesh,CanvasRectPlanes(0,0,viewport.CanvasWidth,viewport.CanvasHeight));return clipped.Geometry.Triangles.Length>0;
  }
  bool ModClientSourceSlotCommand(ModClientOwner owner,ModClientContext c,string op,DrawArgs a,out object result)
  {
   result=null;
   if(op=="context-source-status")
   {string provider=a.Has?a.Text():null;a.End();result=ModClientSourceStatus(owner,c,provider);return true;}
   if(op=="context-slot-validate")
   {string provider=a.Text(),source=a.Text();a.End();ValidateDisplaySource(provider,source);if(string.IsNullOrEmpty(provider))throw new ArgumentException("Source slots require an explicit provider.");result=true;return true;}
   if(op!="context-slot"&&!op.StartsWith("context-slot-",StringComparison.Ordinal))return false;
   if(!c.Hud&&c.Surface==null)throw new ArgumentException("Source slots require a declared world context surface.");
   string id=SourceSlotId(a.Text());var state=ModClientSources(owner,c);ModClientSourceSlot existing;state.Slots.TryGetValue(id,out existing);
   if(op=="context-slot-status")
   {a.End();if(existing==null)throw new ArgumentException("Source slot does not exist for this context.");result=ModClientSlotStatus(state,existing);return true;}
   if(op=="context-slot-settings")
   {
    a.End();if(existing==null)throw new ArgumentException("Source slot does not exist for this context.");var s=existing.Declaration;
    result=new MyTuple<string,string,Vector4,Vector4,Vector4,int>(s.Provider,s.SourceId,new Vector4((float)s.Rect[0],(float)s.Rect[1],(float)s.Rect[2],(float)s.Rect[3]),new Vector4((float)s.Clip[0],(float)s.Clip[1],(float)s.Clip[2],(float)s.Clip[3]),new Vector4(s.UV[0],s.UV[1],s.UV[2],s.UV[3]),s.Order);return true;
   }
   if(op=="context-slot-remove")
   {a.End();if(existing==null)throw new ArgumentException("Source slot does not exist for this context.");state.Slots.Remove(id);ModClientReleaseSourceSlot(existing);result=true;return true;}
   HoloProjectedSourceSlotData slot;
   if(op=="context-slot")
   {
    string provider=a.Text(),source=a.Text();var rect=a.Typed<Vector4>();var clip=a.Typed<Vector4>();var uv=a.Typed<Vector4>();int order=a.Integer(0);float opacity=(float)a.Number(1);a.End();
    if(existing==null&&state.Slots.Count>=MaxSourceSlotsPerScreen)throw new ArgumentException("Context source slot limit is 16.");
    slot=new HoloProjectedSourceSlotData{Id=id,Provider=provider,SourceId=source,Rect=SlotRect(rect),Clip=SlotRect(clip),UV=new[]{uv.X,uv.Y,uv.Z,uv.W},Order=order,Opacity=opacity};
   }
   else
   {
    if(existing==null)throw new ArgumentException("Source slot does not exist for this context.");slot=ModClientCloneSlot(existing.Declaration);
    switch(op)
    {
     case "context-slot-visible":slot.Visible=a.Flag();a.End();break;
     case "context-slot-opacity":slot.Opacity=(float)a.Number();a.End();break;
     case "context-slot-refresh":slot.RefreshHz=a.Number(0);a.End();break;
     case "context-slot-order":slot.Order=a.Integer(0);a.End();break;
     case "context-slot-pose":slot.Pose=MatrixValues(a.Typed<MatrixD>());a.End();break;
     case "context-slot-panorama":slot.CameraSettingsOverride=true;slot.CameraSettingsMask|=1;slot.SourceFov=a.Number(105);slot.SourceFeather=a.Number(8);slot.SourceSaturation=a.Number(1.15);slot.SourceCaptureResolution=a.Integer(1024);a.End();break;
     case "context-slot-camera-quality":slot.CameraSettingsOverride=true;slot.CameraSettingsMask|=2;string profile=a.Text();slot.SourceCaptureProfile=profile=="normal"?0:profile=="lite"?1:-1;a.End();break;
     case "context-slot-inherit":slot.CameraSettingsOverride=false;slot.CameraSettingsMask=0;slot.RefreshHz=0;slot.Layer=null;a.End();break;
     default:throw new ArgumentException("Unknown context source slot command.");
    }
   }
   // Validate a detached candidate before retiring the last accepted declaration.
   var validation=CloneScreen(ModClientSourceCanvas(c),true);var validated=slot;
   if(c.Hud)
   {
    ModClientRules.Bound(new Vector4((float)slot.Rect[0],(float)slot.Rect[1],(float)slot.Rect[2],(float)slot.Rect[3]));ModClientRules.Bound(new Vector4((float)slot.Clip[0],(float)slot.Clip[1],(float)slot.Clip[2],(float)slot.Clip[3]));
    if(slot.Pose!=null)ModClientSurfaceControlPose(ReadMatrix(slot.Pose));
    validated=ModClientCloneSlot(slot);validated.Rect=validated.Clip=new[]{0d,0,1,1};validated.Pose=null;
   }
   validation.SourceSlots=new List<HoloProjectedSourceSlotData>{validated};ValidateSourceSlots(validation);
   slot.Revision=checked(++_modClientSourceRevision);
   state.Slots[id]=new ModClientSourceSlot{Declaration=slot};if(existing!=null)ModClientReleaseSourceSlot(existing);result=true;return true;
  }
  MyTuple<bool,string> ModClientSlotStatus(ModClientSourceContext state,ModClientSourceSlot slot)
  {
   string reason;
   if(!state.Context.Visible||!slot.Declaration.Visible||slot.Declaration.Opacity<=0||!ClientRenderingEnabled)reason="Hidden";
   else if(!ModClientNativeProviderAvailable(slot.Declaration.Provider))reason=ModClientSourceUnavailable(slot.Declaration.Provider);
   else if(!ModClientAnchorCurrent(state.Context.SourceAnchor))reason="Pending: a live accessible source anchor is required.";
   else if(slot.Item!=null&&ModClientSourceItemCurrent(state.Owner,state.Context,slot.Item))reason="Ready";
   else reason=slot.Status??"Pending";
   return new MyTuple<bool,string>(reason=="Ready",reason);
  }
  static void ModClientReleaseSourceSlot(ModClientSourceSlot slot)
  {
   var endpoint=slot.Endpoint;var evidence=slot.Evidence;var raster=slot.Raster;
   // Detach before callbacks: provider release may synchronously reenter Core.
   slot.Binding=null;slot.Endpoint=null;slot.Evidence=null;slot.Raster=null;slot.Item=null;slot.EndpointGeneration=0;slot.NextCapture=-1;
   ReleaseProviderEvidence(endpoint,evidence);ReleaseRasterImage(raster);
  }
  void ModClientInvalidateSourceSlots(ModClientContext c)
  {ModClientSourceContext state;if(!_modClientSourceContexts.TryGetValue(c,out state))return;foreach(var slot in new List<ModClientSourceSlot>(state.Slots.Values)){ModClientReleaseSourceSlot(slot);slot.Status="Pending";}}
  void ModClientFlushSourceProvider(string provider)
  {foreach(var state in new List<ModClientSourceContext>(_modClientSourceContexts.Values))foreach(var slot in new List<ModClientSourceSlot>(state.Slots.Values))if(slot.Declaration.Provider==provider){ModClientReleaseSourceSlot(slot);slot.Status="Pending";}ModClientPruneLocalProviders();}
  void ModClientClearSourceSlots(ModClientContext c)
  {ModClientSourceContext state;if(!_modClientSourceContexts.TryGetValue(c,out state))return;_modClientSourceContexts.Remove(c);var slots=new List<ModClientSourceSlot>(state.Slots.Values);state.Slots.Clear();foreach(var slot in slots)ModClientReleaseSourceSlot(slot);}
  bool ModClientHasSourceSlots(ModClientContext c)
  {ModClientSourceContext state;if(!c.Visible||!c.Hud&&c.Surface==null||!_modClientSourceContexts.TryGetValue(c,out state))return false;foreach(var slot in state.Slots.Values)if(ModClientSourceIntersects(c,slot.Declaration))return true;return false;}
  void ModClientMaintainSourceSlots()
  {
   foreach(var state in new List<ModClientSourceContext>(_modClientSourceContexts.Values))
   {
    ModClientContext current;if(!ModClientCurrent(state.Owner)||!state.Owner.Contexts.TryGetValue(state.Context.Handle,out current)||!ReferenceEquals(current,state.Context)){ModClientClearSourceSlots(state.Context);continue;}
    foreach(var slot in new List<ModClientSourceSlot>(state.Slots.Values))if(slot.Item!=null&&!ModClientSourceItemCurrent(state.Owner,state.Context,slot.Item)){ModClientReleaseSourceSlot(slot);slot.Status="Pending";}
   }
  }
  bool ModClientSourceItemCurrent(ModClientOwner owner,ModClientContext c,ModClientItem item)
  {
   ModClientSourceContext state;if(!_modClientSourceContexts.TryGetValue(c,out state))return !item.SourceSlotItem;
   foreach(var slot in new List<ModClientSourceSlot>(state.Slots.Values))if(ReferenceEquals(slot.Item,item))
   {
    bool valid=slot.Binding!=null&&ModClientNativeBindingCurrent(slot.Binding,true)&&slot.EndpointGeneration==slot.Binding.Provider.Generation&&ReferenceEquals(slot.Endpoint,slot.Binding.Provider.Endpoint);
    if(valid)try{var answer=slot.Endpoint("valid",new object[]{slot.Binding,slot.Evidence});valid=answer is bool&&(bool)answer&&ModClientNativeBindingCurrent(slot.Binding,true)&&(slot.Raster==null||RasterImageCurrent(slot.Raster));}catch{valid=false;}
    // Artwork and poses can change after acquisition. A still-valid provider
    // lease cannot bypass the owner's current combined geometry allowance.
    if(valid)try{valid=ModClientSourceGeometryFits(state.Owner,c,slot,item);}catch{valid=false;}
    if(!valid){ModClientReleaseSourceSlot(slot);slot.Status="Pending";}return valid;
   }
   // Ordinary retained artwork has no provider lease.
   return !item.SourceSlotItem;
  }
  bool ModClientIsSourceItem(ModClientItem item)
  {return item.SourceSlotItem;}
  int ModClientCompositionCompare(ModClientItem a,ModClientItem b)
  {int order=a.Order.CompareTo(b.Order);if(order!=0)return order;bool sa=ModClientIsSourceItem(a),sb=ModClientIsSourceItem(b);if(sa!=sb)return sa?1:-1;return string.CompareOrdinal(a.Id,b.Id);}
  void ModClientCollectSourceItems(ModClientOwner owner,ModClientContext c,List<ModClientItem> items)
  {
   ModClientSourceContext state;if(!_modClientSourceContexts.TryGetValue(c,out state))return;
   var ordered=new List<ModClientSourceSlot>(state.Slots.Values);ordered.Sort((a,b)=>a.Declaration.Order!=b.Declaration.Order?a.Declaration.Order.CompareTo(b.Declaration.Order):string.CompareOrdinal(a.Declaration.Id,b.Declaration.Id));
   foreach(var slot in ordered)
   {
    if(!c.Visible||!ClientRenderingEnabled||!ModClientSourceIntersects(c,slot.Declaration)){ModClientReleaseSourceSlot(slot);slot.Status="Hidden";continue;}
    try
    {
     if(slot.Item!=null&&!ModClientSourceItemCurrent(owner,c,slot.Item))slot.Status="Pending";
     if(slot.Binding==null)slot.Binding=ModClientCreateNativeBinding(state,slot);
     if(slot.Binding==null)continue;
     var demand=ModClientNativeDemand(slot.Binding);if(!demand.Item4){ModClientReleaseSourceSlot(slot);slot.Status="Hidden";continue;}
     double now=_ticks/60d;
     if(slot.NextCapture<0||now+1e-8>=slot.NextCapture)
     {
      // Share the existing raster acquisition allowance with PB source slots.
      if(_rasterSourceTick!=_ticks){_rasterSourceTick=_ticks;_rasterSourceCalls=0;}
      if(_rasterSourceCalls<4){_rasterSourceCalls++;slot.NextCapture=now+1/demand.Item3;ModClientAcquireNativeSource(slot,demand);}
     }
     if(slot.Item!=null&&ModClientSourceItemCurrent(owner,c,slot.Item))items.Add(slot.Item);
    }
    catch(Exception e){ModClientReleaseSourceSlot(slot);slot.Status="Pending: "+e.Message;}
   }
  }
  bool ModClientSourceGeometryFits(ModClientOwner owner,ModClientContext c,ModClientSourceSlot replacing,ModClientItem item)
  {
   if(owner.PointLimit==0&&owner.PrimitiveLimit==0)return true;
   var proposed=c.Hud?item.Geometry:item.SurfaceMesh.Geometry;int points=proposed.Points.Length,primitives=proposed.Triangles.Length/3+proposed.Edges.Length/2;
   int retainedPoints=item.Geometry.Points.Length,retainedPrimitives=item.Geometry.Triangles.Length/3+item.Geometry.Edges.Length/2;
   foreach(var context in owner.Contexts.Values)foreach(var artwork in context.Items.Values){var g=context.Surface==null?artwork.Geometry:ModClientPreparedSurface(context,artwork).Geometry;points+=g.Points.Length;primitives+=g.Triangles.Length/3+g.Edges.Length/2;retainedPoints+=artwork.Geometry.Points.Length;retainedPrimitives+=artwork.Geometry.Triangles.Length/3+artwork.Geometry.Edges.Length/2;}
   foreach(var state in _modClientSourceContexts.Values)if(ReferenceEquals(state.Owner,owner))foreach(var other in state.Slots.Values)if(!ReferenceEquals(other,replacing)&&other.Item!=null){var g=state.Context.Hud?other.Item.Geometry:other.Item.SurfaceMesh.Geometry;points+=g.Points.Length;primitives+=g.Triangles.Length/3+g.Edges.Length/2;retainedPoints+=other.Item.Geometry.Points.Length;retainedPrimitives+=other.Item.Geometry.Triangles.Length/3+other.Item.Geometry.Edges.Length/2;}
   return ModClientRules.FitsAllowance(owner,points,primitives)&&ModClientRules.FitsAllowance(owner,retainedPoints,retainedPrimitives);
  }
 }
}
