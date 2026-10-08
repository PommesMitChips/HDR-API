using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;
using HDR.Interactions;
namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  long _uiProjectedSurfaceSequence;
  // Core's existing reserved-ID guard remains active for UI writes as for Draw.
  object WithProjectedUiWrite(DrawContext context,Func<object> operation)
  {
   string prefix=_screenWritePrefix,layer=_screenWriteLayerPrefix;long caller=_screenWriteCaller,anchor=_screenWriteAnchor;
   try
   {
    _screenWritePrefix=_screenWriteLayerPrefix=null;_screenWriteCaller=_screenWriteAnchor=0;
    if(context.ScreenId!=null){SelectedScreen(context);_screenWritePrefix=ScreenPrefix(context.ScreenId);string sample=ScreenLayer(context.ScreenId,"x");_screenWriteLayerPrefix=sample.Substring(0,sample.Length-1);_screenWriteCaller=context.Caller.EntityId;_screenWriteAnchor=context.Target.EntityId;}
    return operation();
   }
   finally{_screenWritePrefix=prefix;_screenWriteLayerPrefix=layer;_screenWriteCaller=caller;_screenWriteAnchor=anchor;}
  }
  static string UiRenderId(string screen,string local){return screen==null?local:ScreenPrefix(screen)+local;}
  static string UiRenderLayer(string screen,string bundle)
  {
   if(screen==null)return "ui-"+bundle;uint hash=2166136261;foreach(char c in bundle)hash=unchecked((hash^c)*16777619);return ScreenLayer(screen,"ui-"+hash.ToString("x8"));
  }
  static string UiSelectedArtwork(DrawContext context,string local)
  {
   if(context.ScreenId==null)return local;ValidateId(local);if(local.Length>32||local.IndexOf('!')>=0||local.IndexOf('~')>=0)throw new ArgumentException("Screen control artwork uses a local Draw object ID.");return UiRenderId(context.ScreenId,local);
  }
  Item UiOwnedControlItem(IMyProgrammableBlock caller,IMyTerminalBlock target,UiWidget widget)
  {
   if(widget==null||widget.Control==null||widget.ScreenId==null&&IsProjectedId(widget.Control.Artwork)||widget.ScreenId!=null&&!widget.Control.Artwork.StartsWith(ScreenPrefix(widget.ScreenId),StringComparison.Ordinal))throw new ArgumentException("Control artwork must belong to its declared screen.");
   var context=new DrawContext{Caller=caller,Target=target,ScreenId=widget.ScreenId};return (Item)WithProjectedUiWrite(context,()=>GetItem(caller,target,widget.Control.Artwork));
  }
  bool UiProjectedScreen(UiDisplay display,UiWidget widget,out Scene scene,out HoloProjectedScreenData data)
  {
   scene=null;data=null;ProjectedScreen screen;
   if(display==null||widget==null||widget.ScreenId==null||!_scenes.TryGetValue(display.TargetId,out scene)||!scene.Screens.TryGetValue(Key(display.CallerId,widget.ScreenId),out screen))return false;
   data=screen.Data;return data.CallerId==display.CallerId&&data.Id==widget.ScreenId&&data.Visible&&data.Opacity>0&&(data.FrontOpacity>0||data.TwoSided&&data.BackOpacity>0);
  }
  long UiProjectedGeneration(UiDisplay display,UiWidget widget)
  {Scene scene;HoloProjectedScreenData data;return UiProjectedScreen(display,widget,out scene,out data)?data.UiSurfaceGeneration:-1;}
  bool UiProjectedOpenContext(UiPeer peer,UiDisplay display)
  {return display!=null&&display.Widgets.Exists(w=>w.ScreenId==peer.OpenScreenId&&w.Bundle==peer.OpenBundle&&UiProjectedGeneration(display,w)==peer.OpenSurfaceGeneration);}
  static bool UiSameArray<T>(T[] a,T[] b){if(a==null||b==null)return a==b;if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(!EqualityComparer<T>.Default.Equals(a[i],b[i]))return false;return true;}
  void UiStampProjectedSurfaceMutation(Scene scene,HoloProjectedScreenData previous,HoloProjectedScreenData candidate)
  {
   bool pinhole=candidate.SurfaceMapping==2&&(candidate.SurfaceKind==2||candidate.SurfaceKind==3);
   bool same=previous!=null&&UiSameArray(previous.Pose,candidate.Pose)&&UiSameArray(previous.View,candidate.View)&&previous.Width==candidate.Width&&previous.Height==candidate.Height&&previous.CanvasWidth==candidate.CanvasWidth&&previous.CanvasHeight==candidate.CanvasHeight&&previous.Aspect==candidate.Aspect&&previous.Visible==candidate.Visible&&previous.Opacity==candidate.Opacity&&previous.TwoSided==candidate.TwoSided&&previous.FrontOpacity==candidate.FrontOpacity&&previous.BackOpacity==candidate.BackOpacity&&previous.SurfaceKind==candidate.SurfaceKind&&previous.SurfaceMapping==candidate.SurfaceMapping&&previous.SurfaceSide==candidate.SurfaceSide&&previous.SurfaceRadius==candidate.SurfaceRadius&&previous.SurfaceHorizontal==candidate.SurfaceHorizontal&&previous.SurfaceVertical==candidate.SurfaceVertical&&UiSameArray(previous.SurfaceRadii,candidate.SurfaceRadii)&&UiSameArray(previous.SurfaceMeshPoints,candidate.SurfaceMeshPoints)&&UiSameArray(previous.SurfaceMeshTriangles,candidate.SurfaceMeshTriangles)&&UiSameArray(previous.SurfaceMeshUV,candidate.SurfaceMeshUV)&&UiSameArray(previous.SurfaceClip,candidate.SurfaceClip)&&(!pinhole||previous.Camera[9]==candidate.Camera[9]&&previous.UiRasterWidth==candidate.UiRasterWidth&&previous.UiRasterHeight==candidate.UiRasterHeight);
   if(same){candidate.UiSurfaceGeneration=previous.UiSurfaceGeneration;return;}
   if(previous!=null&&(!UiSameArray(previous.View,candidate.View)||previous.CanvasWidth!=candidate.CanvasWidth||previous.CanvasHeight!=candidate.CanvasHeight))
   {var display=GetUiDisplay(candidate.CallerId,scene.ConsoleId);UiValueOwner owner;if(display!=null&&_uiValueOwners.TryGetValue(UiKey(candidate.CallerId,scene.ConsoleId),out owner))foreach(var widget in display.Widgets)if(widget.ScreenId==candidate.Id&&widget.Control!=null){Item item;if(scene.Items.TryGetValue(Key(candidate.CallerId,widget.Control.Artwork),out item))UiValidateControlPose(MyAPIGateway.Entities.GetEntityById(scene.ConsoleId) as IMyTerminalBlock,item,UiSourceProof(owner,item),item.Transform,scene);}}
   foreach(var s in _scenes.Values)foreach(var screen in s.Screens.Values)_uiProjectedSurfaceSequence=Math.Max(_uiProjectedSurfaceSequence,screen.Data.UiSurfaceGeneration);
   if(_uiProjectedSurfaceSequence==long.MaxValue)throw new ArgumentException("Projected UI surface generation exhausted.");candidate.UiSurfaceGeneration=++_uiProjectedSurfaceSequence;
  }
  void UiNotifyProjectedScreenRemoved(long caller,long target,string screenId)
  {
   var display=GetUiDisplay(caller,target);if(display!=null)foreach(var widget in display.Widgets)if(widget.ScreenId==screenId&&widget.Control!=null)UiCancelLeases(caller,target,widget.Control.ValueId,"cancel");
   foreach(var grant in new List<UiDragViewerGrant>(_uiDragViewers.Values))if(grant.CallerId==caller&&grant.TargetId==target&&grant.ScreenId==screenId)RevokeUiDragViewer(grant);
  }
  bool TryUiProjectedCanvas(UiDisplay display,UiWidget widget,Vector3D origin,Vector3D direction,out Vector3D hit,out Vector3D canvas)
  {
   hit=canvas=Vector3D.Zero;Scene scene;HoloProjectedScreenData data;if(!UiProjectedScreen(display,widget,out scene,out data))return false;
   var block=MyAPIGateway.Entities.GetEntityById(display.TargetId) as IMyTerminalBlock;if(block==null||block.Closed||!block.IsWorking||!(block is IMyProjector)||!FiniteDisplayPoint(origin)||!FiniteDisplayPoint(direction)||direction.LengthSquared()<1e-16)return false;
   var volume=SurfaceCrop(data,block,data.SurfaceKind==0?ProjectedVolume(scene,data,block):GetDisplayVolume(scene,block));
   var frame=ReadMatrix(data.Pose)*block.WorldMatrix;Vector2 uv;Vector3D local;
   if(data.SurfaceKind==0)
   {
    var map=ProjectedCanvas(data,0,data.CanvasWidth,data.CanvasHeight)*block.WorldMatrix;var right=map.Right*data.CanvasWidth*.5;var up=map.Up*data.CanvasHeight*.5;
    if(!UiHitGeometry.Quad(origin,direction,map.Translation,right,up,UiInteractionRange,out hit))return false;
    bool front=Vector3D.Dot(frame.Backward,origin-hit)>=0;if((front?data.FrontOpacity:data.TwoSided?data.BackOpacity:0)<=0)return false;
    var delta=hit-map.Translation;double xx=right.LengthSquared(),yy=up.LengthSquared(),xy=Vector3D.Dot(right,up),det=xx*yy-xy*xy;if(det<1e-16)return false;
    canvas=new Vector3D((Vector3D.Dot(delta,right)*yy-Vector3D.Dot(delta,up)*xy)/det*data.CanvasWidth*.5,(Vector3D.Dot(delta,up)*xx-Vector3D.Dot(delta,right)*xy)/det*data.CanvasHeight*.5,0);
   }
   else
   {
    var inverse=MatrixD.Invert(frame);var o=Vector3D.Transform(origin,inverse);var d=Vector3D.TransformNormal(direction,inverse);
    if(data.SurfaceSide==0&&data.SurfaceKind!=4){o.X=-o.X;d.X=-d.X;}
    if(data.SurfaceKind==1){o.Y*=data.CanvasHeight/data.Height;d.Y*=data.CanvasHeight/data.Height;}
    if(!UiSurfaceHit.TryHit(o,d,ScreenSurfaceStyle(data),data.CanvasWidth,data.CanvasHeight,1000000,data.FrontOpacity,data.TwoSided?data.BackOpacity:0,out local,out uv,p=>{if(data.SurfaceKind==1)p.Y*=data.Height/data.CanvasHeight;if(data.SurfaceSide==0&&data.SurfaceKind!=4)p.X=-p.X;var world=Vector3D.Transform(p,frame);return Vector3D.DistanceSquared(origin,world)<=UiInteractionRange*UiInteractionRange+1e-8&&(volume==null||volume.Contains(world));}))return false;
    canvas=new Vector3D((uv.X-.5)*data.CanvasWidth,(.5-uv.Y)*data.CanvasHeight,0);
    if(data.SurfaceKind==1)local.Y*=data.Height/data.CanvasHeight;if(data.SurfaceSide==0&&data.SurfaceKind!=4)local.X=-local.X;hit=Vector3D.Transform(local,frame);
    if(Vector3D.DistanceSquared(origin,hit)>UiInteractionRange*UiInteractionRange+1e-8)return false;
   }
   return (volume==null||volume.Contains(hit))&&FiniteDisplayPoint(canvas);
  }
  bool TryUiProjectedHit(UiDisplay display,UiWidget widget,Vector3D origin,Vector3D direction,out Vector3D hit,out long tileId)
  {
   tileId=0;Vector3D canvas;if(!TryUiProjectedCanvas(display,widget,origin,direction,out hit,out canvas))return false;Scene scene;HoloProjectedScreenData data;if(!UiProjectedScreen(display,widget,out scene,out data))return false;
   var mapping=ProjectedContentView(data);if(widget.Control!=null){Item item;if(!scene.Items.TryGetValue(Key(display.CallerId,widget.Control.Artwork),out item)||item.CallerId!=display.CallerId||!item.Id.StartsWith(ScreenPrefix(widget.ScreenId),StringComparison.Ordinal))return false;mapping=item.Transform*mapping;}
   Vector3D local;return UiProjectedPlanePoint(mapping,canvas,out local)&&UiRules.Contains(widget,local.X,local.Y)&&(tileId=display.TargetId)!=0;
  }
  static bool UiProjectedPlanePoint(MatrixD mapping,Vector3D canvas,out Vector3D local)
  {
   local=Vector3D.Zero;double det=mapping.M11*mapping.M22-mapping.M12*mapping.M21;if(!UiDragMappingFinite(mapping)||Math.Abs(det)<1e-15)return false;double x=canvas.X-mapping.M41,y=canvas.Y-mapping.M42;local=new Vector3D((x*mapping.M22-y*mapping.M21)/det,(y*mapping.M11-x*mapping.M12)/det,0);return FiniteDisplayPoint(local);
  }
  bool UiProjectedLocalRay(UiDisplay display,UiControlData control,long tileId,Vector3D origin,Vector3D direction,out LocalRay ray)
  {
   ray=default(LocalRay);UiWidget widget=display==null?null:display.Widgets.Find(w=>w.Control!=null&&w.Control.Artwork==control.Artwork);if(widget==null||widget.ScreenId==null||tileId!=display.TargetId)return false;
   Vector3D hit,canvas;Scene scene;HoloProjectedScreenData data;if(!UiProjectedScreen(display,widget,out scene,out data)||!TryUiProjectedCanvas(display,widget,origin,direction,out hit,out canvas))return false;
   var mapping=UiValueRules.Matrix(control.ReferencePose)*ProjectedContentView(data);var inverse=MatrixD.Invert(mapping);return LocalRay.TryFromDirection(Vector3D.Transform(new Vector3D(canvas.X,canvas.Y,1000),inverse),Vector3D.TransformNormal(Vector3D.Forward,inverse),out ray);
  }
  bool UiProjectedRequestRay(VRage.Game.ModAPI.IMyPlayer player,UiDisplay display,UiWidget widget,UiDragRequest request,out LocalRay ray)
  {
   ray=default(LocalRay);if(player==null||player.Character==null||widget==null||widget.ScreenId==null||request.ScreenId!=widget.ScreenId||request.SurfaceGeneration!=UiProjectedGeneration(display,widget))return false;
   var head=player.Character.GetHeadMatrix(true,true,true,true);var origin=new Vector3D(request.OriginX,request.OriginY,request.OriginZ);var direction=new Vector3D(request.DirectionX,request.DirectionY,request.DirectionZ);double length=direction.Length();
   if(!FiniteDisplayPoint(origin)||!FiniteDisplayPoint(direction)||!FiniteDisplayPoint(head.Translation)||!FiniteDisplayPoint(head.Forward)||head.Forward.LengthSquared()<1e-16||length<.999||length>1.001||Vector3D.DistanceSquared(origin,head.Translation)>.5625||Vector3D.Dot(direction/length,Vector3D.Normalize(head.Forward))<.25)return false;
   Vector3D hit,canvas;if(!TryUiProjectedCanvas(display,widget,origin,direction,out hit,out canvas))return false;var tile=MyAPIGateway.Entities.GetEntityById(display.TargetId) as IMyTerminalBlock;
   return tile!=null&&UiUnoccluded(player.Character.EntityId,tile,origin,hit)&&UiProjectedLocalRay(display,widget.Control,display.TargetId,origin,direction,out ray);
  }
  double? UiDragProjectedValue(UiDragBinding binding,UiDragRequest request)
  {
   var context=binding.Context as UiDragServerContext;if(context==null||context.ScreenId==null||request.ScreenId!=context.ScreenId||request.SurfaceGeneration!=context.SurfaceGeneration)return null;
   var display=GetUiDisplay(binding.CallerId,binding.TargetId);var widget=GetUiWidget(binding.CallerId,binding.TargetId,binding.ControlId,false);var player=UiDragPlayerForIdentity(context.Identity);LocalRay ray;
   if(!UiProjectedRequestRay(player,display,widget,request,out ray))return null;
   if(context.Path!=null){PathDragState next;if(!InteractionMath.TryUpdatePathDrag(context.Path,ray,context.PathState,out next))return null;context.PathState=next;return next.Value;}
   RotationDragState rotation;if(context.Rotation==null||!InteractionMath.TryUpdateRotationDrag(context.Rotation,ray,context.RotationState,out rotation))return null;context.RotationState=rotation;double canonical;return UiValueRules.Range(context.Numeric).TryNormalize(UiValueRules.FromAngle(context.Control,context.Numeric,rotation.Value),out canonical)?(double?)canonical:null;
  }
  VRage.Game.ModAPI.IMyPlayer UiDragPlayerForIdentity(long identity)
  {var players=new List<VRage.Game.ModAPI.IMyPlayer>();MyAPIGateway.Players.GetPlayers(players,p=>p.IdentityId==identity);return players.Count==1?UiDragPlayer(players[0].SteamUserId):null;}
 }
}
