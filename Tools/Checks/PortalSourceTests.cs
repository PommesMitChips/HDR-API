using System.Collections;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

internal static class PortalSourceTests
{
 static int checks;
 static void Check(bool b,string why){if(!b)throw new Exception("Portal source: "+why);checks++;}
 static object Call(object target,string name,params object[] args)=>ClientReplicationTests.Call(target,name,args);
 static object Field(object o,string n)=>ClientReplicationTests.Field(o,n);
 static void Set(object o,string n,object v)=>ClientReplicationTests.SetField(o,n,v);
 static object Static(string name,params object[] args)
 {try{return typeof(HoloMapSession).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
 public static int Run()
 {
  checks=0;using var gateway=new ClientReplicationTests.GatewayScope{Server=true};var session=new HoloMapSession();
  var caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>m.Name switch{"get_EntityId"=>10L,"get_OwnerId"=>77L,"get_Closed"=>false,"IsSameConstructAs"=>true,_=>throw new Exception(m.Name)});
  MatrixD world=MatrixD.CreateTranslation(100000,0,0);bool working=true;
  var anchor=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>m.Name switch{"get_EntityId"=>20L,"get_Closed"=>false,"get_IsWorking"=>working,"get_WorldMatrix"=>world,"GetPosition"=>world.Translation,"HasPlayerAccess"=>true,"get_BlockDefinition"=>Activator.CreateInstance(m.ReturnType),_=>throw new Exception(m.Name)});
  gateway.Install("Entities",(m,a)=>m.Name=="GetEntityById"?((long)a[0]==10?caller:(long)a[0]==20?anchor:null):throw new Exception(m.Name));
  gateway.Install("Session",(m,a)=>m.Name=="get_Camera"?DrawTestProxy.Make(m.ReturnType,(cm,ca)=>cm.Name=="get_Position"?world.Translation+new Vector3D(0,0,2):throw new Exception(cm.Name)):throw new Exception(m.Name));
  var h=(Func<string,object[],object>)Call(session,"DrawEndpoint",caller);h("target",new[]{anchor});h("screen",new object[]{"portal",MatrixD.Identity,2d,1d,2d,1d});
  object Scene()=>((IDictionary)Field(session,"_scenes"))[20L];
  HoloProjectedScreenData Data()=>(HoloProjectedScreenData)Field(((IDictionary)Field(Scene(),"Screens"))["10:portal"],"Data");
  void Reject(string op,object[] args,string why){var before=Data();try{h(op,args);}catch(ArgumentException){Check(ReferenceEquals(before,Data()),"rejected declaration is atomic: "+why);return;}throw new Exception("Expected portal rejection: "+why);}
  Reject("screen-source",new object[]{"native-portal","portal"},"missing pair");
  h("screen-portal-plane",new object[]{MatrixD.CreateTranslation(0,0,-30),2d,1d});
  Check(Data().SourceProvider=="native-portal"&&Data().SourceId=="portal"&&Data().Portal.Accuracy==0,"plane pair is strict and resolves to its unique screen source");
  Reject("screen-source",new object[]{"native-portal","other"},"foreign source ID");
  Reject("screen-portal-plane",new object[]{MatrixD.CreateScale(2),2d,1d},"nonrigid exit");
  Reject("screen-portal-plane",new object[]{MatrixD.Identity,2d,1d,"teleport","strict"},"transport vocabulary");
  Reject("screen-portal-transport",new object[]{"stealth","vague"},"accuracy vocabulary");
  Reject("screen-portal-settings",new object[]{0d,1d,1d},"singular normal transport");
  Reject("screen-portal-settings",new object[]{double.NaN,1d,1d},"nonfinite transport");
  Reject("screen-portal-settings",new object[]{1d,2.1d,1d},"saturation bound");
  Reject("screen-portal-shell",new object[]{MatrixD.Identity,new Vector3D(0,1,1)},"degenerate capture shell");
  Reject("screen-portal-shell",new object[]{MatrixD.Identity,new Vector3D(251)},"capture shell bound");
  Reject("screen-resolution",new object[]{4097,512},"bounded GPU output");
  Reject("screen-resolution",new object[]{63,512},"minimum portal output size");
  Reject("screen-refresh",new object[]{121d},"capture rate bound");
  h("screen-resolution",new object[]{4096,4096});h("screen-refresh",new object[]{120d});
  Check(Data().UiRasterWidth==4096&&Data().UiRasterHeight==4096&&Data().RefreshHz==120,"native portal declaration accepts requested 4K per axis and 120 Hz");
  h("screen-resolution",new object[]{64,64});h("screen-refresh",new object[]{1d});
  Check(Data().UiRasterWidth==64&&Data().UiRasterHeight==64&&Data().RefreshHz==1,"native portal declaration accepts lower resolution and cadence boundaries");
  h("screen-resolution",new object[]{256,256});h("screen-refresh",new object[]{6d});
  Reject("screen-portal-charts",new object[]{1,0},"analytic chart");
  Reject("screen-portal-transport",new object[]{"stealth","strict"},"noncoincident stealth pair");
  h("screen-portal-settings",new object[]{-2d,0d,0d});h("screen-portal-transport",new object[]{"differential","approximate"});
  var frozen=(HoloProjectedScreenData)Static("CloneScreen",Data(),true);
  Data().Portal.ShellRadii[0]=7;Check(frozen.Portal.ShellRadii[0]==10,"publication clone freezes nested shell arrays");
  var round=MyAPIGateway.Utilities.SerializeFromBinary<HoloProjectedScreenData>(gateway.Serialize(Data()));
  Check(round.Portal.NormalScale==-2&&round.Portal.Saturation==0&&round.Portal.Brightness==0&&round.Portal.Accuracy==1&&round.Portal.ShellRadii[0]==7,"protobuf preserves explicit zero appearance, signed scale, shell and accuracy");
  var mesh=new[]{new Vector3D(-1,-1,0),new Vector3D(1,-1,0),new Vector3D(0,1,0)};var triangles=new[]{0,1,2};var uv=new[]{new Vector2(0,1),new Vector2(1,1),new Vector2(.5f,0)};
  Reject("screen-portal-mesh",new object[]{MatrixD.Identity,mesh,new[]{0,1,3},uv},"exit triangle bounds");
  Reject("screen-portal-mesh",new object[]{MatrixD.Identity,mesh,triangles,new Vector2[3]},"degenerate UV chart");
  h("screen-mesh",new object[]{mesh,triangles,uv,"outside"});h("screen-portal-mesh",new object[]{MatrixD.Identity,mesh,triangles,uv});
  mesh[0]=new Vector3D(200);triangles[0]=99;uv[0]=new Vector2(.3f,.3f);
  Check(Data().SurfaceMeshPoints[0]==-1&&Data().Portal.ExitPoints[0]==-1&&Data().Portal.ExitTriangles[0]==0&&Data().Portal.ExitUV[0]==0,"PB arrays are detached for both pair ends");
  var packet=ClientReplicationTests.Snapshot();packet.Scenes[0].Screens=new List<HoloProjectedScreenData>{(HoloProjectedScreenData)Static("CloneScreen",Data(),true)};
  HoloSnapshot Wire(HoloSnapshot s)=>MyAPIGateway.Utilities.SerializeFromBinary<HoloSnapshot>(gateway.Serialize(s));
  var receiver=new HoloMapSession();Call(receiver,"ApplySnapshot",Wire(packet));
  var before=Wire(packet);packet.Scenes[0].Screens[0].Portal.Brightness=.5;
  var delta=(HoloSnapshot)Static("BuildDelta",before,Wire(packet),1L);var joined=(HoloSnapshot)Static("MergeDelta",before,Wire(delta),1L);Call(receiver,"ApplySnapshot",joined);
  Check(joined.Scenes[0].Screens[0].Portal.Brightness==.5&&before.Scenes[0].Screens[0].Portal.Brightness==0,"portal metadata delta and late join retain frozen prior declaration");
  object previous=((IDictionary)Field(receiver,"_scenes"))[20L];var forged=Wire(packet);forged.Scenes[0].Screens[0].Portal.ShellRadii[0]=double.NaN;
  try{Call(receiver,"ApplySnapshot",forged);throw new Exception("Expected forged portal rejection.");}catch(ArgumentException){Check(ReferenceEquals(previous,((IDictionary)Field(receiver,"_scenes"))[20L]),"hostile network declaration preserves prior scene atomically");}
  Set(session,"_displaySourceRegistered",true);
  gateway.Install("Utilities",(m,a)=>m.Name switch{"get_IsDedicated"=>false,"SendModMessage"=>null,"SerializeToBinary"=>gateway.Serialize(a[0]),_=>throw new Exception(m.Name)});
  Func<string,object[],object> endpoint=(op,args)=>true;Call(session,"ReceiveDisplaySource",new MyTuple<string,int,Func<string,object[],object>>("native-portal",2,endpoint));
  object Descriptor(long owner=10,string source="portal")=>Call(session,"DisplaySourceService","source-portaldescriptor",new object[]{"native-portal",20L,owner,source});
  var descriptor=Descriptor() as object[];Check(descriptor!=null&&descriptor.Length==19&&(int)descriptor[0]==1&&(int)descriptor[3]==2,"service publishes fixed declarative mesh descriptor");
  Check(((double[])descriptor[4])[12]==100000&&((double[])descriptor[10])[12]==100000,"entry and exit follow current anchor world pose");
  ((double[])descriptor[6])[0]=999;((double[])descriptor[12])[0]=888;((double[])descriptor[16])[0]=555;
  var again=(object[])Descriptor();Check(((double[])again[6])[0]==-1&&((double[])again[12])[0]==-1&&((double[])again[16])[0]==7,"each consumer gets independent immutable declaration arrays");
  Check(Descriptor(11) is bool b&&!b&&Descriptor(10,"other") is bool c&&!c,"foreign caller and source identity cannot borrow descriptor");
  working=false;Check(Descriptor() is bool off&&!off,"unpowered anchor disables descriptor");working=true;
  h("screen-visible",new object[]{false});Check(Descriptor() is bool hidden&&!hidden,"hidden declaration stops local capture demand");h("screen-visible",new object[]{true});
  h("screen-portal-clear",Array.Empty<object>());h("screen-ellipsoid",new object[]{new Vector3D(2,1.5,2),Math.PI*2,Math.PI,"outside"});h("screen-two-sided",new object[]{false,1d,0d});h("screen-portal-ellipsoid",new object[]{MatrixD.Identity,new Vector3D(2,1.5,2)});h("screen-portal-exit-side",new object[]{"outside"});h("screen-portal-transport",new object[]{"stealth","strict"});
  var bubble=(object[])Descriptor();Check((int)bubble[1]==1&&(int)bubble[3]==3&&(int)bubble[9]==3&&((double[])bubble[4]).SequenceEqual((double[])bubble[10])&&((double[])bubble[5]).SequenceEqual((double[])bubble[11]),"stealth descriptor publishes coincident outward entry and exit skins");
  Reject("screen-portal-exit-side",new object[]{"inside"},"stealth chart side mismatch");
  h("screen-source-clear",Array.Empty<object>());Check(Data().Portal==null&&Descriptor() is bool clear&&!clear,"clear retires pair and local descriptor");
  return checks;
 }
}
