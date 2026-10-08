using System.Collections;
using System.Reflection;
using HoloMap;
using VRage;

internal static class PluginCapabilityTests
{
 public static int Run()
 {
  int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("Plugin capability: "+why);checks++;}
  object Call(object o,string method,params object[] args)=>ClientReplicationTests.Call(o,method,args);
  object Field(object o,string name)=>ClientReplicationTests.Field(o,name);
  object Nested(string name)=>Activator.CreateInstance(typeof(HoloMapSession).GetNestedType(name,BindingFlags.NonPublic),true);
  using var gateway=new ClientReplicationTests.GatewayScope();bool dedicated=false;var messages=new List<string>();int backendCalls=0;
  gateway.Install("Utilities",(m,a)=>m.Name switch{
   "get_IsDedicated"=>dedicated,"ShowMessage"=>Message(a),"SendModMessage"=>null,_=>throw new Exception("Unexpected plugin utility "+m.Name)});
  object Message(object[] a){messages.Add((string)a[1]);return null;}
  var session=new HoloMapSession();
  MyTuple<bool,bool,string> Status(string feature)=>(MyTuple<bool,bool,string>)Call(session,"LocalPluginStatus",feature);
  foreach(string feature in new[]{"raster-ui","camera-panorama","lcd-texture","native-portal"})
  {
   var status=Status(feature);Check(status.Item1&&!status.Item2&&status.Item3.StartsWith("Requires plugin: HDR Client Renderer ("),feature+" is queryable and safely absent");
   for(int i=0;i<100;i++)Check((bool)Call(session,"NotifyPluginUnavailable",feature),feature+" repeat use is nonthrowing");
  }
  Check(messages.Count==3,"notices bounded by three feature categories, including shared LCD/camera category");
  Check(((IDictionary)Field(session,"_scenes")).Count==0,"capability query never creates scenes or declarations");
  Check(!Status("third-party").Item1&&!(bool)Call(session,"NotifyPluginUnavailable","third-party")&&messages.Count==3,"generic providers are not falsely labelled plugin-dependent");
  Call(session,"ResetPluginNotices");int uploadBefore=messages.Count;
  Check(Call(session,"UploadRaster","third-party:rgba",new VRageMath.Vector2I(16,16),new byte[16*16*4])==null,"actual third-party RGBA upload safely returns no lease without an uploader");
  Check(messages.Count==uploadBefore+1&&messages.Last()=="Requires plugin: HDR Client Renderer (raster UI).","actual RGBA consumption reports the raster dependency rather than misclassifying its producer");
  Call(session,"UploadRaster","third-party:rgba",new VRageMath.Vector2I(16,16),new byte[16*16*4]);
  Check(messages.Count==uploadBefore+1,"repeated absent RGBA uploads do not repeat the notice");
  var scene=Nested("Scene");var cache=Nested("ProjectedCache");
  foreach(string feature in new[]{"native-portal","camera-panorama","lcd-texture","third-party"})
  {
   var data=new HoloProjectedScreenData{SourceProvider=feature,SourceId="sample"};
   Check(Call(session,"BuildExternalSource",scene,data,cache,32,32)==null,"absent "+feature+" cannot invoke an endpoint or native capture");
  }
  var raster=new HoloProjectedScreenData{ContentRenderer=1};
  Call(session,"PrepareScreenUiRaster",scene,raster,cache);
  Check(Field(cache,"UiRaster")==null&&(string)Field(cache,"UiRasterError")==Status("raster-ui").Item3,"missing raster does not construct a canvas or publish a lease");
  Check(!(bool)Call(session,"UseProjectedVectorFallback",raster),"explicit raster never silently draws vectors when plugin is missing");
  Check((bool)Call(session,"UseProjectedVectorFallback",new HoloProjectedScreenData{ContentRenderer=0}),"plugin-free vector rendering remains available");
  dedicated=true;Call(session,"ResetPluginNotices");int before=messages.Count;
  foreach(string feature in new[]{"raster-ui","camera-panorama","native-portal"})
  {var status=Status(feature);Check(status.Item1&&!status.Item2&&status.Item3.StartsWith("Viewer-dependent capability:"),"dedicated query does not infer remote viewer plugin state");Call(session,"NotifyPluginUnavailable",feature);}
  Check(messages.Count==before,"dedicated declarations do not produce client plugin warnings");
  dedicated=false;
  ClientReplicationTests.SetField(session,"_rasterRegistered",true);ClientReplicationTests.SetField(session,"_displaySourceRegistered",true);
  Func<string,object[],object> endpoint=(op,args)=>{backendCalls++;return null;};
  Call(session,"ReceiveRasterBackend",new MyTuple<int,Func<string,object[],object>>(1,endpoint));
  foreach(string feature in new[]{"camera-panorama","lcd-texture","native-portal"})
   Call(session,"ReceiveDisplaySource",new MyTuple<string,int,Func<string,object[],object>>(feature,2,endpoint));
  foreach(string feature in new[]{"raster-ui","camera-panorama","lcd-texture","native-portal"})
  {var status=Status(feature);Check(status.Item1&&status.Item2&&status.Item3.Contains("GPU/frame readiness is separate"),"registration and GPU readiness are distinct");Check(!(bool)Call(session,"NotifyPluginUnavailable",feature),"reconnected feature is no longer missing");}
  Check(backendCalls==0,"capability discovery/queries never call backend or instantiate native GPU work");
  Check((bool)Call(session,"UseProjectedVectorFallback",raster),"registered raster retains existing explicit busy/error fallback policy");
  Call(session,"RasterService","unregister",new object[]{endpoint});
  for(int i=0;i<100;i++)Call(session,"NotifyPluginUnavailable","raster-ui");
  Check(messages.Count==before+1,"new disconnection emits one notice after reconnect");
  Call(session,"DisplaySourceService","unregister",new object[]{"native-portal",endpoint});
  Call(session,"NotifyPluginUnavailable","native-portal");Check(messages.Count==before+2,"portal reconnect resets only its feature notice");
  Call(session,"ResetPluginNotices");before=messages.Count;
  var absentPointer=Status("interactive-pointer");
  Check(absentPointer.Item1&&!absentPointer.Item2&&absentPointer.Item3.Contains("0.9.13"),"native pointer has a safe version-specific missing-plugin explanation");
  Check(Field(session,"_uiPointer")==null,"pointer capability query does not initialize or acquire input");
  for(int i=0;i<100;i++)Check((bool)Call(session,"NotifyPluginUnavailable","interactive-pointer"),"missing pointer use is nonthrowing");
  Check(messages.Count==before+1,"pointer absence has its own bounded notice group");
  dedicated=true;before=messages.Count;
  Check(Status("interactive-pointer").Item1&&!Status("interactive-pointer").Item2&&Status("interactive-pointer").Item3.StartsWith("Viewer-dependent capability:"),"dedicated server accepts viewer-neutral pointer capability");
  Call(session,"NotifyPluginUnavailable","interactive-pointer");Check(messages.Count==before,"dedicated pointer capability cannot emit client warnings");
  dedicated=false;var pointerBridge=new PointerInputBridge();ClientReplicationTests.SetField(session,"_uiPointer",pointerBridge);
  int pointerCalls=0;Func<string,object[],object> pointerEndpoint=(op,args)=>{pointerCalls++;return null;};
  Call(session,"ReceiveUiPointerProvider",new MyTuple<string,int,Func<string,object[],object>>(PointerInputBridge.Version,1,pointerEndpoint));
  var connectedPointer=Status("interactive-pointer");
  Check(connectedPointer.Item1&&connectedPointer.Item2&&connectedPointer.Item3.Contains("confirmed input routing"),"local pointer registration does not claim routed-input readiness");
  Check(pointerCalls==0,"pointer discovery and capability queries never acquire or sample native input");
  Call(session,"ReceiveUiPointerProvider",new MyTuple<string,int,Func<string,object[],object>>(PointerInputBridge.Version,0,pointerEndpoint));
  before=messages.Count;Call(session,"NotifyPluginUnavailable","interactive-pointer");
  Check(!Status("interactive-pointer").Item2&&messages.Count==before+1,"provider reconnect resets pointer notice for a later withdrawal");
  return checks;
 }
}
