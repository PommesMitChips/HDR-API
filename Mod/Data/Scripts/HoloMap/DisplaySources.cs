using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  const long DisplaySourceDiscovery=481770100,DisplaySourceRegistration=481770101;
  sealed class DisplayProvider {public string Name;public Func<string,object[],object> Endpoint;public long Generation;public int Protocol;public bool Began;}
  readonly Dictionary<string,DisplayProvider> _displayProviders=new Dictionary<string,DisplayProvider>();
  Func<string,object[],object> _displaySourceService;long _displayProviderGeneration;bool _displaySourceRegistered,_displaySourceDrawing,_displaySourceDiscovering;
  static bool HasExternalSource(HoloProjectedScreenData d){return d!=null&&!string.IsNullOrEmpty(d.SourceProvider);}
  static void ValidateDisplaySource(string provider,string source)
  {
   if(provider==null&&source==null)return;int limit=provider=="camera-panorama"?256:64;if(string.IsNullOrEmpty(provider)||provider.Length>32||string.IsNullOrWhiteSpace(source)||source.Length>limit)throw new ArgumentException("Display source requires a provider ID up to 32 characters and a bounded source ID.");
   foreach(char c in provider)if(!(c>='a'&&c<='z'||c>='0'&&c<='9'||c=='-'))throw new ArgumentException("Provider IDs use lowercase letters, digits and hyphens.");foreach(char c in source)if(char.IsControl(c))throw new ArgumentException("Invalid display source ID.");
   if(provider=="camera-panorama"){var ids=source.Split(',');if(ids.Length<1||ids.Length>6)throw new ArgumentException("Camera panoramas support one to six camera IDs.");var seen=new HashSet<long>();foreach(string value in ids){foreach(char c in value)if(c<'0'||c>'9')throw new ArgumentException("Camera source IDs are positive decimal entity IDs.");long id;if(value.Length==0||!long.TryParse(value,out id)||id<=0||!seen.Add(id))throw new ArgumentException("Camera source IDs must be positive, unique and bounded.");}}
  }
  void RegisterDisplaySources()
  {
   if(_displaySourceRegistered)return;_displaySourceService=DisplaySourceService;MyAPIGateway.Utilities.RegisterMessageHandler(DisplaySourceRegistration,ReceiveDisplaySource);_displaySourceRegistered=true;
   DiscoverDisplaySources();
  }
  void DiscoverDisplaySources()
  {if(!_displaySourceRegistered||_displaySourceDiscovering)return;_displaySourceDiscovering=true;try{MyAPIGateway.Utilities.SendModMessage(DisplaySourceDiscovery,_displaySourceService);}catch{ /* A failing optional consumer must not disable HDR's own display backend. */ }finally{_displaySourceDiscovering=false;}}
  void ReceiveDisplaySource(object message)
  {
   if(!_displaySourceRegistered||!(message is MyTuple<string,int,Func<string,object[],object>>))return;var m=(MyTuple<string,int,Func<string,object[],object>>)message;
   try{ValidateDisplaySource(m.Item1,"1");if(m.Item2!=1&&m.Item2!=2||m.Item3==null)return;DisplayProvider old;if(_displayProviders.TryGetValue(m.Item1,out old)&&ReferenceEquals(old.Endpoint,m.Item3)&&old.Protocol==m.Item2)return;if(old==null&&_displayProviders.Count>=8)return;
    _displayProviders[m.Item1]=new DisplayProvider{Name=m.Item1,Endpoint=m.Item3,Generation=++_displayProviderGeneration,Protocol=m.Item2};PluginComponentRegistered(m.Item1);FlushDisplayProvider(m.Item1);
    // Install first: a newer registration issued by the old provider's end callback must win.
    if(old!=null&&old.Began){old.Began=false;try{old.Endpoint("end",new object[0]);}catch{}}DiscoverDisplaySources();
   }catch(ArgumentException){}
  }
  object DisplaySourceService(string command,object[] args)
  {
   if(!_displaySourceRegistered)return false;if(command=="source-state"||command=="source-demand")
   {
    if(args==null||args.Length!=4||!(args[0] is string)||!(args[1] is long)||!(args[2] is long)||!(args[3] is string))return false;
    var demand=DisplaySourceDemand((string)args[0],(long)args[1],(long)args[2],(string)args[3]);return command=="source-state"?(object)demand.Item4:demand;
   }
   if(command=="source-demand-any")
   {if(args==null||args.Length!=2||!(args[0] is string)||!(args[1] is string))return false;return DisplaySourceDemandAny((string)args[0],(string)args[1]);}
   if(command=="source-panoramasettings"||command=="source-renderprofile")
   {
    if(args==null||args.Length!=4||!(args[0] is string)||(string)args[0]!="camera-panorama"||!(args[1] is long)||!(args[2] is long)||!(args[3] is string))return false;
    Scene scene;if(!_scenes.TryGetValue((long)args[1],out scene))return false;
    HoloProjectedScreenData found=null;
    foreach(var screen in scene.Screens.Values){var d=screen.Data;if(d.CallerId!=(long)args[2]||d.SourceProvider!=(string)args[0]||d.SourceId!=(string)args[3])continue;if(found!=null&&!SameCameraSourceSettings(found,d))return false;found=d;}
    if(found==null)return false;
    if(command=="source-renderprofile")return found.SourceCaptureProfile;
    return new MyTuple<double,double,double,int>(found.SourceFov,found.SourceFeather,found.SourceSaturation,found.SourceCaptureResolution);
   }
   if(command=="source-cameradensity")return CameraSourceDensity(args);
   if(command=="source-portaldescriptor")return PortalSourceDescriptor(args);
   if(command=="source-occlusion")return DisplaySourceOcclusion(args);
   if(command=="source-occlusion-fresh")return DisplaySourceOcclusionFresh(args);
   if(command=="unregister")
   {
    if(args==null||args.Length!=2||!(args[0] is string)||!(args[1] is Func<string,object[],object>))return false;DisplayProvider provider;
    if(!_displayProviders.TryGetValue((string)args[0],out provider)||!ReferenceEquals(provider.Endpoint,args[1]))return false;
    bool began=provider.Began;provider.Began=false;if(began)try{provider.Endpoint("end",new object[0]);}catch{}
    DisplayProvider active;if(_displayProviders.TryGetValue(provider.Name,out active)&&ReferenceEquals(active,provider)&&active.Generation==provider.Generation){_displayProviders.Remove(provider.Name);FlushDisplayProvider(provider.Name);}return true;
   }
   return false;
  }
  bool DisplaySourceVisible(string name,long anchorId,long caller,string source)
  {
   return DisplaySourceDemand(name,anchorId,caller,source).Item4;
  }
  void FlushDisplayProvider(string name)
  {foreach(var c in _projectedCaches.Values)if(c.SourceProvider==name||c.Data!=null&&c.Data.SourceProvider==name){ClearExternalBudgetView(c);c.NextCapture=-1;c.Error=null;}}
  void BeginDisplaySourceDraw()
  {
   BeginDisplaySourceOcclusionDraw();_displaySourceDrawing=true;var providers=new List<DisplayProvider>(_displayProviders.Values);
   foreach(var p in providers)
   {
    DisplayProvider current;if(!_displayProviders.TryGetValue(p.Name,out current)||!ReferenceEquals(current,p)||current.Generation!=p.Generation)continue;
    p.Began=true;
    try
    {
     p.Endpoint("begin",new object[0]);DisplayProvider active;
     if(!_displayProviders.TryGetValue(p.Name,out active)||!ReferenceEquals(active,p)||active.Generation!=p.Generation)
     {if(p.Began){p.Began=false;try{p.Endpoint("end",new object[0]);}catch{}}}
    }
    catch{if(p.Began){p.Began=false;try{p.Endpoint("end",new object[0]);}catch{}}FlushDisplayProvider(p.Name);}
   }
  }
  void EndDisplaySourceDraw()
  {
   EndDisplaySourceOcclusionDraw();
   var providers=new List<DisplayProvider>(_displayProviders.Values);foreach(var p in providers)if(p.Began){p.Began=false;try{p.Endpoint("end",new object[0]);}catch{FlushDisplayProvider(p.Name);}}_displaySourceDrawing=false;
  }
  bool ExternalEvidenceValid(Scene scene,HoloProjectedScreenData d,ProjectedCache cache)
  {
   DisplayProvider provider;if(!HasExternalSource(d)||cache.SourceProvider!=d.SourceProvider||cache.SourceId!=d.SourceId||!_displayProviders.TryGetValue(d.SourceProvider,out provider)||provider.Generation!=cache.SourceGeneration||!ReferenceEquals(provider.Endpoint,cache.SourceEndpoint)||_displaySourceDrawing&&!provider.Began)return false;
   try{var result=provider.Endpoint("valid",new object[]{scene.ConsoleId,d.CallerId,d.SourceId,cache.SourceEvidence});DisplayProvider active;return result is bool&&(bool)result&&_displayProviders.TryGetValue(d.SourceProvider,out active)&&active.Generation==cache.SourceGeneration&&ReferenceEquals(active.Endpoint,cache.SourceEndpoint);}catch{return false;}
  }
  void ValidateCachedExternalView(Scene scene,HoloProjectedScreenData d,ProjectedCache cache)
  {if(HasExternalSource(d)){if(cache.View!=null&&(!ExternalEvidenceValid(scene,d,cache)||cache.SourceRaster!=null&&!RasterImageCurrent(cache.SourceRaster)))ClearExternalBudgetView(cache);}else if(cache.SourceProvider!=null)ClearExternalBudgetView(cache);}
  static void ReleaseFailedSourceAcquisition(Func<string,object[],object> endpoint,object evidence,ProjectedCache cache)
  {
   // A provider can offer the already retained lease while a replacement is pending.
   // Failure to admit that offer does not retire the cache's current lease.
   if(ReferenceEquals(cache.SourceEndpoint,endpoint)&&ReferenceEquals(cache.SourceEvidence,evidence))return;
   ReleaseProviderEvidence(endpoint,evidence);
  }
  SurfaceMesh BuildExternalSource(Scene scene,HoloProjectedScreenData d,ProjectedCache cache,int pointBudget,int primitiveBudget)
  {
   DisplayProvider provider;if(!HasExternalSource(d))return null;
   if(!_displayProviders.TryGetValue(d.SourceProvider,out provider)){NotifyPluginUnavailable(d.SourceProvider);return null;}
   if(_displaySourceDrawing&&!provider.Began)return null;
   var endpoint=provider.Endpoint;long generation=provider.Generation;
   var request=new object[]{scene.ConsoleId,d.CallerId,d.SourceId,d.Id,pointBudget,primitiveBudget,d.CanvasWidth,d.CanvasHeight,(double[])d.Camera.Clone(),d.RasterWidth,d.RasterHeight,d.OrbitSpeed,(double)d.OrbitStartTick,(double)AnimationNow};
   if(provider.Protocol==2){var size=ScreenRasterSize(d,cache);if(size.X==0||size.Y==0)return null;var extended=new object[16];Array.Copy(request,extended,14);extended[14]=size.X;extended[15]=size.Y;request=extended;}
   object returned=endpoint("frame",request);
   if(returned==null)return null;
   if(provider.Protocol==2)
   {if(!(returned is MyTuple<int,object,object,bool,double>))throw new ArgumentException("Version 2 provider requires a raster frame.");var rasterFrame=(MyTuple<int,object,object,bool,double>)returned;DisplayProvider live;if(!_displayProviders.TryGetValue(d.SourceProvider,out live)||live.Generation!=generation||!ReferenceEquals(live.Endpoint,endpoint)){ReleaseFailedSourceAcquisition(endpoint,rasterFrame.Item3,cache);return null;}bool transferred=false;try{var mesh=BuildExternalRaster(scene,d,cache,returned,pointBudget,primitiveBudget,endpoint,generation,ref transferred);if(mesh==null&&!transferred)ReleaseFailedSourceAcquisition(endpoint,rasterFrame.Item3,cache);return mesh;}catch{if(!transferred)ReleaseFailedSourceAcquisition(endpoint,rasterFrame.Item3,cache);throw;}}
   if(!(returned is MyTuple<Vector3D[],int[],Vector4[],object,bool,double>))throw new ArgumentException("External display provider returned an unsupported frame type.");
   var frame=(MyTuple<Vector3D[],int[],Vector4[],object,bool,double>)returned;var p=frame.Item1;var t=frame.Item2;var colors=frame.Item3;
   if(p==null||t==null||colors==null||p.Length>Math.Min(Geometry.MaxPoints,pointBudget)||t.Length%3!=0||t.Length/3>Math.Min(Geometry.MaxPrimitives,primitiveBudget)||colors.Length!=t.Length/3||!Geometry.Finite(frame.Item6)||frame.Item6<.1||frame.Item6>6)throw new ArgumentException("External display frame exceeds its geometry or refresh allowance.");
   var detachedPoints=(Vector3D[])p.Clone();var detachedTriangles=(int[])t.Clone();var detachedColors=(Vector4[])colors.Clone();
   foreach(var point in detachedPoints)if(!Geometry.Finite(point.X)||!Geometry.Finite(point.Y)||!Geometry.Finite(point.Z)||Math.Abs(point.Z)>1e-6||Math.Abs(point.X)>d.CanvasWidth/2+1e-6||Math.Abs(point.Y)>d.CanvasHeight/2+1e-6)throw new ArgumentException("External display points must lie in the finite canvas plane.");foreach(int index in detachedTriangles)if(index<0||index>=detachedPoints.Length)throw new ArgumentException("External display triangle index is invalid.");foreach(var color in detachedColors)Color(color);
   DisplayProvider active;if(!_displayProviders.TryGetValue(d.SourceProvider,out active)||active.Generation!=generation||!ReferenceEquals(active.Endpoint,endpoint))return null;
   var priorRaster=cache.SourceRaster;cache.SourceRaster=null;cache.SourceTexture=false;cache.SourceTextureMaterial=null;cache.SourceTextureSize=Vector2I.Zero;cache.SourceProtocol=1;ReleaseRasterImage(priorRaster);
   cache.SourceProvider=d.SourceProvider;cache.SourceId=d.SourceId;cache.SourceEndpoint=endpoint;cache.SourceGeneration=generation;cache.SourceEvidence=frame.Item4;cache.SourceReduced=frame.Item5;
   cache.NextCapture=_ticks/60d+1/Math.Min(frame.Item6,Math.Min(d.RefreshHz,ClientLcdRefreshCap));if(!ExternalEvidenceValid(scene,d,cache)){ClearExternalBudgetView(cache);return null;}
   if(detachedPoints.Length==0){if(detachedTriangles.Length!=0)throw new ArgumentException("External frame has no points.");return null;}
   return new SurfaceMesh{Geometry=new Geometry(detachedPoints,new int[0],detachedTriangles),Colors=detachedColors};
  }
  void UnregisterDisplaySources()
  {
   ClearDisplaySourceOcclusion();
   _cameraDemandCaches.Clear();_cameraDensityNextTick=0;
   if(!_displaySourceRegistered)return;EndDisplaySourceDraw();if(MyAPIGateway.Utilities!=null)MyAPIGateway.Utilities.UnregisterMessageHandler(DisplaySourceRegistration,ReceiveDisplaySource);_displaySourceRegistered=false;_displayProviders.Clear();_displaySourceService=null;
  }
 }
}
