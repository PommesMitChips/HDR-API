using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  sealed class ModClientLocalProvider
  {public Func<string,object[],object> Endpoint;public Func<object,object[]> Resolver;public long Generation;public bool Native;}
  sealed class ModClientNativeBinding
  {
   public ModClientSourceContext State;public ModClientSourceSlot Slot;public HoloProjectedSourceSlotData Declaration;
   public ModClientLocalProvider Provider;public long ContextRevision,RegisteredGeneration;public int Protocol;public MatrixD ContextPose;public IMyTerminalBlock Anchor;
   public IMyTerminalBlock[] Sources;public object SourceIdentity;public HoloProjectedScreenData Portal;public long PortalCaller;
  }
  readonly List<ModClientLocalProvider> _modClientLocalProviders=new List<ModClientLocalProvider>();
  Func<string,object[],object> _modClientNativeEndpoint;long _modClientNativeGeneration;bool _modClientLocalStopping;
  static string ModClientNativeRequired(string provider)
  {return "RequiresPlugin: "+provider+" (HDR Client Renderer 0.9.14 local-mod sources)";}
  string ModClientSourceUnavailable(string provider)
  {return _displayProviders.ContainsKey(provider)?"Unsupported consumer capability: "+provider+" has not negotiated client-local sources.":ModClientNativeRequired(provider);}
  object ModClientNativeConsumerService(object[] args)
  {
   if(_modClientLocalStopping||args==null||args.Length!=2||!(args[0] is int)||(int)args[0]!=1||!(args[1] is Func<string,object[],object>))return false;
   var endpoint=(Func<string,object[],object>)args[1];bool native=false,registered=false;
   foreach(var provider in _displayProviders.Values)if(ReferenceEquals(provider.Endpoint,endpoint))registered=true;
   try{native=Equals(endpoint("version",new object[0]),"HDR.LocalModNative/1");}catch{}
   registered=false;foreach(var provider in _displayProviders.Values)if(ReferenceEquals(provider.Endpoint,endpoint))registered=true;
   if(_modClientLocalStopping||!native&&!registered)return false;
   ModClientPruneLocalProviders();
   foreach(var prior in _modClientLocalProviders)if(ReferenceEquals(prior.Endpoint,endpoint)&&prior.Native==native&&(!native||ReferenceEquals(endpoint,_modClientNativeEndpoint)))return prior.Resolver;
   var local=new ModClientLocalProvider{Endpoint=endpoint,Native=native,Generation=++_modClientNativeGeneration};
   local.Resolver=binding=>ModClientNativeDescriptor(local,binding);
   _modClientLocalProviders.Add(local);
   if(native){_modClientNativeEndpoint=endpoint;foreach(var state in new List<ModClientSourceContext>(_modClientSourceContexts.Values))foreach(var slot in new List<ModClientSourceSlot>(state.Slots.Values))if(slot.Binding!=null&&slot.Binding.Provider.Native)ModClientReleaseSourceSlot(slot);}
   ModClientPruneLocalProviders();
   return local.Resolver;
  }
  void ModClientPruneLocalProviders()
  {
   for(int i=_modClientLocalProviders.Count-1;i>=0;i--)
   {
    var local=_modClientLocalProviders[i];bool live=local.Native&&ReferenceEquals(local.Endpoint,_modClientNativeEndpoint);
    if(!local.Native)foreach(var provider in _displayProviders.Values)if(ReferenceEquals(local.Endpoint,provider.Endpoint)){live=true;break;}
    if(!live)_modClientLocalProviders.RemoveAt(i);
   }
  }
  bool ModClientNativeAvailable()
  {
   if(_modClientNativeEndpoint==null)return false;
   try{var caps=_modClientNativeEndpoint("capabilities",new object[0]) as string[];if(caps!=null)foreach(string name in caps)if(_displayProviders.ContainsKey(name))return true;}catch{}return false;
  }
  void ModClientStopSourceProviders()
  {_modClientLocalStopping=true;_modClientNativeEndpoint=null;_modClientNativeGeneration++;_modClientLocalProviders.Clear();foreach(var state in new List<ModClientSourceContext>(_modClientSourceContexts.Values))ModClientInvalidateSourceSlots(state.Context);}
  void ModClientStartSourceProviders(){_modClientLocalStopping=false;}
  bool ModClientLocalProviderCurrent(ModClientLocalProvider local,string name)
  {
   if(local==null||local.Endpoint==null)return false;
   if(local.Native)return ReferenceEquals(local.Endpoint,_modClientNativeEndpoint)&&_displayProviders.ContainsKey(name);
   DisplayProvider provider;return _displayProviders.TryGetValue(name,out provider)&&ReferenceEquals(provider.Endpoint,local.Endpoint);
  }
  ModClientLocalProvider ModClientLocalSourceProvider(string name)
  {
   foreach(var provider in _modClientLocalProviders)if(!provider.Native&&ModClientLocalProviderCurrent(provider,name))return provider;
   if(_modClientNativeEndpoint==null||!_displayProviders.ContainsKey(name))return null;
   foreach(var provider in _modClientLocalProviders)if(provider.Native&&ReferenceEquals(provider.Endpoint,_modClientNativeEndpoint))
   {
    try{var caps=provider.Endpoint("capabilities",new object[0]) as string[];if(caps!=null)foreach(string capability in caps)if(capability==name)return provider;}catch{}return null;
   }
   return null;
  }
  bool ModClientNativeProviderAvailable(string name)
  {return name!=null&&ModClientLocalSourceProvider(name)!=null;}
  MyTuple<bool,bool,string> ModClientSourceStatus(ModClientOwner owner,ModClientContext c,string provider=null)
  {
   bool anchored=ModClientCurrent(owner)&&ModClientAnchorCurrent(c.SourceAnchor),ready=provider==null?ModClientNativeAvailable():ModClientNativeProviderAvailable(provider);
   return new MyTuple<bool,bool,string>(ready,anchored,ready?(anchored?"Ready":"Client-local sources require a live accessible source anchor."):provider==null?ModClientNativeRequired("client-local sources"):ModClientSourceUnavailable(provider));
  }
  bool ModClientAnchorCurrent(IMyTerminalBlock anchor)
  {
   try
   {
    var viewer=MyAPIGateway.Session==null?null:MyAPIGateway.Session.Player;var camera=MyAPIGateway.Session==null?null:MyAPIGateway.Session.Camera;
    return ClientRenderingEnabled&&anchor!=null&&anchor.EntityId>0&&anchor.OwnerId!=0&&!anchor.Closed&&anchor.IsWorking&&viewer!=null&&viewer.Character!=null&&camera!=null&&MyAPIGateway.Entities!=null&&ReferenceEquals(MyAPIGateway.Entities.GetEntityById(anchor.EntityId),anchor)&&anchor.HasPlayerAccess(viewer.IdentityId)&&Vector3D.DistanceSquared(viewer.Character.GetPosition(),anchor.GetPosition())<=DrawDistance*DrawDistance&&Vector3D.DistanceSquared(camera.Position,anchor.GetPosition())<=DrawDistance*DrawDistance;
   }
   catch{return false;}
  }
  bool ModClientSourceBlockCurrent(IMyTerminalBlock anchor,IMyTerminalBlock source)
  {
   try{var viewer=MyAPIGateway.Session.Player;return source!=null&&!source.Closed&&source.IsWorking&&ReferenceEquals(MyAPIGateway.Entities.GetEntityById(source.EntityId),source)&&source.IsSameConstructAs(anchor)&&source.HasPlayerAccess(anchor.OwnerId)&&source.HasPlayerAccess(viewer.IdentityId);}catch{return false;}
  }
  ModClientNativeBinding ModClientCreateNativeBinding(ModClientSourceContext state,ModClientSourceSlot slot)
  {
   var declaration=slot.Declaration;var provider=ModClientLocalSourceProvider(declaration.Provider);
   if(provider==null){slot.Status=ModClientSourceUnavailable(declaration.Provider);return null;}
   if(!ModClientAnchorCurrent(state.Context.SourceAnchor)){slot.Status="Pending: a live accessible source anchor is required.";return null;}
   var binding=new ModClientNativeBinding{State=state,Slot=slot,Declaration=declaration,Provider=provider,ContextRevision=state.Context.SurfaceRevision,ContextPose=state.Context.Pose,Anchor=state.Context.SourceAnchor,SourceIdentity=declaration};
   DisplayProvider registered;if(_displayProviders.TryGetValue(declaration.Provider,out registered)){binding.RegisteredGeneration=registered.Generation;binding.Protocol=provider.Native?2:registered.Protocol;}else binding.Protocol=2;
   var blocks=new List<IMyTerminalBlock>();
   if(declaration.Provider=="lcd-texture")
   {
    long id=long.Parse(declaration.SourceId.Split(':')[0]);var source=MyAPIGateway.Entities.GetEntityById(id) as IMyTerminalBlock;
    if(!ModClientSourceBlockCurrent(binding.Anchor,source)){slot.Status="Pending: LCD source is unavailable or inaccessible.";return null;}blocks.Add(source);
   }
   else if(declaration.Provider=="camera-panorama")
   {
    foreach(string part in declaration.SourceId.Split(',')){var source=MyAPIGateway.Entities.GetEntityById(long.Parse(part)) as IMyTerminalBlock;if(!(source is IMyCameraBlock)||!ModClientSourceBlockCurrent(binding.Anchor,source)){slot.Status="Pending: camera source is unavailable or inaccessible.";return null;}blocks.Add(source);}
   }
   else if(declaration.Provider=="native-portal")
   {
    Scene scene;if(!(binding.Anchor is IMyProjector)||!_scenes.TryGetValue(binding.Anchor.EntityId,out scene)){slot.Status="Unsupported composition: declare the real portal source on this anchor first.";return null;}
    foreach(var screen in scene.Screens.Values)if(screen.Data.Id==declaration.SourceId&&screen.Data.SourceProvider=="native-portal"&&screen.Data.SourceId==declaration.SourceId&&screen.Data.Portal!=null)
    {
     // Caller authority comes from the original declaration, never from the mod owner.
     var caller=MyAPIGateway.Entities.GetEntityById(screen.Data.CallerId) as IMyProgrammableBlock;
     if(!ModClientPortalCallerCurrent(binding.Anchor,caller)||binding.Portal!=null){slot.Status="Unsupported composition: portal source declaration is missing, ambiguous or unauthorized.";return null;}
     binding.Portal=screen.Data;binding.PortalCaller=screen.Data.CallerId;
    }
    if(binding.Portal==null){slot.Status="Unsupported composition: declare the real portal source on this anchor first.";return null;}
    ValidatePortalSource(binding.Portal);binding.SourceIdentity=binding.Portal;blocks.Add(binding.Anchor);
   }
   else blocks.Add(binding.Anchor);
   binding.Sources=blocks.ToArray();slot.Status="Pending";return binding;
  }
  bool ModClientNativeBindingCurrent(ModClientNativeBinding binding,bool demand)
  {
   if(binding==null)return false;var state=binding.State;var c=state.Context;ModClientSourceContext currentState;ModClientContext context;ModClientSourceSlot slot;
   if(!ModClientCurrent(state.Owner)||!state.Owner.Contexts.TryGetValue(c.Handle,out context)||!ReferenceEquals(context,c)||!_modClientSourceContexts.TryGetValue(c,out currentState)||!ReferenceEquals(currentState,state)||!state.Slots.TryGetValue(binding.Declaration.Id,out slot)||!ReferenceEquals(slot,binding.Slot)||!ReferenceEquals(slot.Binding,binding)||!ReferenceEquals(slot.Declaration,binding.Declaration)||!c.Hud&&c.Surface==null||c.SurfaceRevision!=binding.ContextRevision||c.Pose!=binding.ContextPose||!ReferenceEquals(c.SourceAnchor,binding.Anchor)||!ModClientLocalProviderCurrent(binding.Provider,binding.Declaration.Provider)||!ModClientAnchorCurrent(binding.Anchor)||!c.Visible||!ModClientSourceIntersects(c,binding.Declaration))return false;
   DisplayProvider registered;if(!_displayProviders.TryGetValue(binding.Declaration.Provider,out registered)||registered.Generation!=binding.RegisteredGeneration||!binding.Provider.Native&&registered.Protocol!=binding.Protocol)return false;
   foreach(var source in binding.Sources)if(!ModClientSourceBlockCurrent(binding.Anchor,source))return false;
   if(binding.Portal!=null)
   {
    Scene scene;ProjectedScreen screen;var caller=MyAPIGateway.Entities.GetEntityById(binding.PortalCaller) as IMyProgrammableBlock;
    if(!ModClientPortalCallerCurrent(binding.Anchor,caller)||!_scenes.TryGetValue(binding.Anchor.EntityId,out scene)||!scene.Screens.TryGetValue(Key(binding.PortalCaller,binding.Declaration.SourceId),out screen)||!ReferenceEquals(screen.Data,binding.Portal))return false;
   }
   return !demand||ModClientNativeDemand(binding).Item4;
  }
  bool ModClientPortalCallerCurrent(IMyTerminalBlock anchor,IMyProgrammableBlock caller)
  {try{return caller!=null&&!caller.Closed&&caller.IsWorking&&caller.OwnerId!=0&&caller.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)&&caller.IsSameConstructAs(anchor)&&anchor.HasPlayerAccess(caller.OwnerId);}catch{return false;}}
  MyTuple<int,int,double,bool> ModClientNativeDemand(ModClientNativeBinding binding)
  {
   var empty=new MyTuple<int,int,double,bool>(0,0,0,false);if(!ModClientNativeBindingCurrent(binding,false))return empty;
   try
   {
    var c=binding.State.Context;var slot=binding.Declaration;var camera=MyAPIGateway.Session.Camera;
    var settings=SourceSlotSettings(ModClientSourceCanvas(c),slot);double hz=SourceRefreshLimit(settings);if(hz<=0)return empty;
    if(c.Hud)
    {
     var mesh=ModClientSourceCanvasMesh(RasterQuad(slot.Rect[2],slot.Rect[3]),slot,true,true);var size=camera.ViewportSize;if(size.X<1||size.Y<1)return empty;
     mesh=MeshEffects.Clip(mesh,CanvasRectPlanes(0,0,size.X,size.Y));if(mesh.Geometry.Triangles.Length==0)return empty;
     double minX=double.MaxValue,minY=double.MaxValue,maxX=double.MinValue,maxY=double.MinValue;foreach(var p in mesh.Geometry.Points){minX=Math.Min(minX,p.X);minY=Math.Min(minY,p.Y);maxX=Math.Max(maxX,p.X);maxY=Math.Max(maxY,p.Y);}
     if(maxX<=minX||maxY<=minY)return empty;
     var output=DisplayLod.MaximumVisible(new Vector2I(Math.Max(16,Math.Min(4096,(int)Math.Ceiling((maxX-minX)/slot.UV[2]))),Math.Max(16,Math.Min(4096,(int)Math.Ceiling((maxY-minY)/slot.UV[3])))),WideSourceRaster(slot.Provider)).Resolution;
     return new MyTuple<int,int,double,bool>(output.X,output.Y,hz,true);
    }
    var canvas=SourceSlotCanvasMesh(RasterQuad(slot.Rect[2],slot.Rect[3]),slot,true);
    var source=new ModClientItem{Geometry=canvas.Geometry,Color=Vector4.One,Colors=canvas.Colors,UV=canvas.UV};
    var mapped=ModClientMapSurface(source,MatrixD.Identity,c.Surface,c.SurfaceSourceAspect,0);
    if(mapped.Geometry.Triangles.Length==0)return empty;
    if(c.Surface.SurfaceKind<3&&ScreenSideOpacity(c.Surface,c.Pose,camera.Position)<=0)return empty;
    var viewport=new Vector2I((int)Math.Round(camera.ViewportSize.X),(int)Math.Round(camera.ViewportSize.Y));if(viewport.X<1||viewport.Y<1)return empty;
    var maximum=DisplayLod.MaximumVisible(new Vector2I(c.Surface.UiRasterWidth,c.Surface.UiRasterHeight),WideSourceRaster(slot.Provider)).Resolution;
    var lod=DisplayLod.Evaluate(mapped.Geometry.Points,mapped.Geometry.Triangles,mapped.UV,c.Pose*camera.ViewMatrix*camera.ProjectionMatrix,viewport,maximum,-1,WideSourceRaster(slot.Provider));
    if(!lod.Visible||lod.Resolution.X==0||lod.Resolution.Y==0)return empty;
    return new MyTuple<int,int,double,bool>(lod.Resolution.X,lod.Resolution.Y,hz,true);
   }
   catch{return empty;}
  }
  object[] ModClientNativeDescriptor(ModClientLocalProvider provider,object value)
  {
   var binding=value as ModClientNativeBinding;if(binding==null||!ReferenceEquals(binding.Provider,provider)||!ModClientNativeBindingCurrent(binding,false))return null;
   var demand=ModClientNativeDemand(binding);object data=null;var s=binding.Declaration;
   if(!provider.Native)data=new MyTuple<double,double>(s.Rect[2],s.Rect[3]);
   if(s.Provider=="camera-panorama"){var d=SourceSlotSettings(ModClientSourceCanvas(binding.State.Context),s);data=new MyTuple<MyTuple<double,double,double,int>,int>(new MyTuple<double,double,double,int>(d.SourceFov,d.SourceFeather,d.SourceSaturation,d.SourceCaptureResolution),d.SourceCaptureProfile);}
   else if(s.Provider=="native-portal")data=ModClientPortalDescriptor(binding);
   return new object[]{1,"local-mod",new MyTuple<string,long,long,long,long>(binding.State.Owner.Id,binding.State.Owner.Generation,binding.State.Context.Handle,binding.ContextRevision,s.Revision),binding.Anchor,s.Provider,s.SourceId,(IMyTerminalBlock[])binding.Sources.Clone(),demand,data,binding.SourceIdentity};
  }
  static object[] ModClientPortalDescriptor(ModClientNativeBinding binding)
  {
   var d=binding.Portal;var p=d.Portal;var world=binding.Anchor.WorldMatrix;
   return new object[]{1,p.Transport,p.Accuracy,PortalEntryKind(d),MatrixValues(ReadMatrix(d.Pose)*world),PortalEntryExtent(d),PortalArray(d.SurfaceMeshPoints,true)??new double[0],PortalArray(d.SurfaceMeshTriangles,true)??new int[0],PortalEntryUV(d)??new double[0],p.ExitKind,MatrixValues(PortalPose(p.ExitPose)*world),PortalArray(p.ExitExtent,true),PortalArray(p.ExitPoints,true)??new double[0],PortalArray(p.ExitTriangles,true)??new int[0],PortalArray(p.ExitUV,true)??new double[0],MatrixValues(PortalPose(p.ShellPose)*world),PortalArray(p.ShellRadii,true),new[]{p.NormalScale,d.RefreshHz,p.Saturation,p.Brightness},new[]{d.UiRasterWidth,d.UiRasterHeight,p.EntryChart,p.ExitChart}};
  }
  void ModClientAcquireNativeSource(ModClientSourceSlot slot,MyTuple<int,int,double,bool> demand)
  {
   var binding=slot.Binding;var provider=binding.Provider;var endpoint=provider.Endpoint;
   // A frame callback may retire a retained shared offer before returning it.
   // Such an offer was already owned, and must not be released a second time.
   var ownedOffers=new List<object>();foreach(var proof in _providerEvidenceOwnership)if(ReferenceEquals(proof.Endpoint,endpoint))ownedOffers.Add(proof.Evidence);
   object returned=endpoint("frame",new object[]{binding,demand.Item1,demand.Item2});
   if(returned==null){slot.Status="Pending";return;}
   object evidence=null;bool transferred=false;RasterImage uploaded=null;
   try
   {
    if(returned is MyTuple<int,object,object,bool,double>)evidence=((MyTuple<int,object,object,bool,double>)returned).Item3;
    else if(returned is MyTuple<Vector3D[],int[],Vector4[],object,bool,double>)evidence=((MyTuple<Vector3D[],int[],Vector4[],object,bool,double>)returned).Item4;
    if(!ModClientNativeBindingCurrent(binding,true))return;
    SurfaceMesh raw;string material=null;double hz;
    if(binding.Protocol==2&&returned is MyTuple<int,object,object,bool,double>)
    {
     var frame=(MyTuple<int,object,object,bool,double>)returned;evidence=frame.Item3;hz=frame.Item5;var uvRect=new Vector4(0,0,1,1);
     if(frame.Item1==1)
     {
      if(!(frame.Item2 is MyTuple<Vector2I,byte[],int>))throw new ArgumentException("RGBA source frame requires a size, bytes and format flags.");var bitmap=(MyTuple<Vector2I,byte[],int>)frame.Item2;if(bitmap.Item3!=0)throw new ArgumentException("Only straight-alpha sRGB RGBA8 is supported.");
      if(_rasterBackend==null){slot.Status="RequiresPlugin: raster-ui (HDR Client Renderer 0.9.14)";return;}
      uploaded=UploadRaster("local-mod:"+binding.State.Owner.Id+":"+binding.State.Owner.Generation+":"+binding.State.Context.Handle+":"+slot.Declaration.Id,bitmap.Item1,bitmap.Item2);if(uploaded==null)return;material=uploaded.Material;
     }
     else if(frame.Item1==2)
     {
      Vector2I size;long generation;
      if(frame.Item2 is MyTuple<string,Vector2I,long>){var texture=(MyTuple<string,Vector2I,long>)frame.Item2;material=texture.Item1;size=texture.Item2;generation=texture.Item3;}
      else if(frame.Item2 is MyTuple<string,Vector2I,long,Vector4>){var texture=(MyTuple<string,Vector2I,long,Vector4>)frame.Item2;material=texture.Item1;size=texture.Item2;generation=texture.Item3;uvRect=texture.Item4;}
      else throw new ArgumentException("Texture source frame requires material, size and generation.");
      if(generation<=0)throw new ArgumentException("Texture source requires a live generation.");material=ValidateRasterMaterial(material);ValidateTextureSize(size);ValidateRasterUVRect(uvRect);
      if((long)size.X*size.Y>(WideSourceRaster(slot.Declaration.Provider)?8388608:DisplayLod.MaxPixels))throw new ArgumentException("Texture source exceeds its source pixel allowance.");
     }
     else throw new ArgumentException("Unknown raster source frame kind.");
     raw=RasterQuad(slot.Declaration.Rect[2],slot.Declaration.Rect[3]);for(int i=0;i<raw.UV.Length;i++)raw.UV[i]=new Vector2(uvRect.X+raw.UV[i].X*uvRect.Z,uvRect.Y+raw.UV[i].Y*uvRect.W);
    }
    else if(binding.Protocol==1&&!provider.Native&&returned is MyTuple<Vector3D[],int[],Vector4[],object,bool,double>)
    {
     var frame=(MyTuple<Vector3D[],int[],Vector4[],object,bool,double>)returned;evidence=frame.Item4;hz=frame.Item6;
     if(frame.Item1==null||frame.Item2==null||frame.Item3==null||frame.Item1.Length>Geometry.MaxPoints||frame.Item2.Length%3!=0||frame.Item2.Length/3>Geometry.MaxPrimitives||frame.Item3.Length!=frame.Item2.Length/3)throw new ArgumentException("Vector source exceeds the retained geometry allowance.");
     var points=(Vector3D[])frame.Item1.Clone();var triangles=(int[])frame.Item2.Clone();var colors=(Vector4[])frame.Item3.Clone();
     foreach(var p in points)if(!ModClientRules.SafePoint(p)||Math.Abs(p.Z)>1e-6||Math.Abs(p.X)>slot.Declaration.Rect[2]/2+1e-6||Math.Abs(p.Y)>slot.Declaration.Rect[3]/2+1e-6)throw new ArgumentException("Vector source must lie in its finite canvas plane.");foreach(int index in triangles)if(index<0||index>=points.Length)throw new ArgumentException("Vector source triangle index is invalid.");foreach(var color in colors)Color(color);
     raw=new SurfaceMesh{Geometry=new Geometry(points,new int[0],triangles),Colors=colors,EdgeColors=new Vector4[0]};
    }
    else throw new ArgumentException("Unsupported local source frame.");
    if(evidence==null||!Geometry.Finite(hz)||hz<.1||hz>120||!ModClientNativeBindingCurrent(binding,true))return;
    var valid=endpoint("valid",new object[]{binding,evidence});if(!(valid is bool)||!(bool)valid||!ModClientNativeBindingCurrent(binding,true))return;
    var c=binding.State.Context;var canvas=ModClientSourceCanvasMesh(raw,slot.Declaration,material!=null,c.Hud);
    if(c.Hud){var viewport=MyAPIGateway.Session.Camera.ViewportSize;canvas=MeshEffects.Clip(canvas,CanvasRectPlanes(0,0,viewport.X,viewport.Y));}
    var item=new ModClientItem{Id=slot.Declaration.Id,Geometry=canvas.Geometry,Colors=canvas.Colors,UV=canvas.UV,Color=Vector4.One,Material=material,Order=slot.Declaration.Order,SourceSlotItem=true};
    for(int i=0;i<item.Colors.Length;i++)item.Colors[i].W*=slot.Declaration.Opacity;
    if(!c.Hud)item.SurfaceMesh=ModClientMapSurface(item,MatrixD.Identity,c.Surface,c.SurfaceSourceAspect,0);item.SurfaceRevision=c.SurfaceRevision;item.SurfaceTransform=MatrixD.Identity;
    if(!ModClientSourceGeometryFits(binding.State.Owner,c,slot,item)){slot.Status="Pending: shared local geometry allowance exhausted.";return;}
    var oldEndpoint=slot.Endpoint;var oldEvidence=slot.Evidence;var oldRaster=slot.Raster;
    if(!ReferenceEquals(oldEndpoint,endpoint)||!ReferenceEquals(oldEvidence,evidence)){RetainProviderEvidence(endpoint,evidence);slot.Endpoint=endpoint;slot.Evidence=evidence;ReleaseProviderEvidence(oldEndpoint,oldEvidence);}
    transferred=true;slot.EndpointGeneration=provider.Generation;slot.Raster=uploaded;slot.Item=item;slot.NextCapture=_ticks/60d+1/Math.Min(hz,demand.Item3);slot.Status="Ready";RetireRasterImage(oldRaster,uploaded);uploaded=null;
    if(!ModClientSourceItemCurrent(binding.State.Owner,c,item))slot.Status="Pending";
   }
   finally
   {
    ReleaseRasterImage(uploaded);
    bool wasOwned=false;foreach(var proof in ownedOffers)if(ReferenceEquals(proof,evidence)){wasOwned=true;break;}
    if(!transferred&&!wasOwned&&!ReferenceEquals(slot.Evidence,evidence))ReleaseUnownedProviderEvidence(endpoint,evidence);
   }
  }
 }
}
