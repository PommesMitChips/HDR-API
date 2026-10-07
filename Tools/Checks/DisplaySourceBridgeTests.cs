using System.Collections;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

internal static class DisplaySourceBridgeTests
{
 static int checks;
 static void Check(bool value,string why){if(!value)throw new Exception("Display source bridge: "+why);checks++;}
 static void Reject(Action action,string why){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Expected source rejection: "+why);}
 static object Call(object o,string method,params object[] args)=>ClientReplicationTests.Call(o,method,args);
 static object Field(object o,string name)=>ClientReplicationTests.Field(o,name);
 static void Set(object o,string name,object value)=>ClientReplicationTests.SetField(o,name,value);
 static object Nested(string name)=>Activator.CreateInstance(typeof(HoloMapSession).GetNestedType(name,BindingFlags.NonPublic),true);
 sealed class Fixture:IDisposable
 {
  public readonly ClientReplicationTests.GatewayScope Gateway=new();
  public readonly HoloMapSession Session=new();public readonly object Scene=Nested("Scene"),Cache=Nested("ProjectedCache");
  public readonly HoloProjectedScreenData Data=new(){CallerId=10,Id="view",Pose=new[]{1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1},SourceProvider="test",SourceId="feed",CanvasWidth=2,CanvasHeight=2};
  public readonly Vector3D[] Points={new Vector3D(-.4,-.3,0),new Vector3D(.4,-.3,0),new Vector3D(0,.3,0)};
  public readonly int[] Triangles={0,1,2};public readonly Vector4[] Colors={Vector4.One};public readonly object Proof=new();
  public bool Valid=true,ThrowFrame,ThrowBegin,ThrowValid;public object Override;
  public int Begins,Ends,Frames,Validations;public object[] LastArgs;
  public readonly Func<string,object[],object> Endpoint;
  public readonly Dictionary<long,Action<object>> Handlers=new();public readonly List<(long Channel,object Payload)> Sent=new();
  public Fixture()
  {
   Gateway.Install("Utilities",(m,a)=>{if(m.Name=="get_IsDedicated")return false;if(m.Name=="ShowMessage")return null;if(m.Name=="RegisterMessageHandler"){Handlers[(long)a[0]]=(Action<object>)a[1];return null;}if(m.Name=="UnregisterMessageHandler"){Handlers.Remove((long)a[0]);return null;}if(m.Name=="SendModMessage"){Sent.Add(((long)a[0],a[1]));return null;}throw new Exception("Unexpected source utility "+m.Name);});
   Endpoint=(op,args)=>{if(op=="begin"){Begins++;if(ThrowBegin)throw new ArgumentException("begin");return true;}if(op=="end"){Ends++;return true;}if(op=="valid"){Validations++;if(ThrowValid)throw new ArgumentException("valid");return Valid&&ReferenceEquals(args[3],Proof);}if(op=="frame"){Frames++;LastArgs=args;if(ThrowFrame)throw new ArgumentException("frame");return Override??new MyTuple<Vector3D[],int[],Vector4[],object,bool,double>(Points,Triangles,Colors,Proof,false,1);}return false;};
   Set(Scene,"ConsoleId",20L);((IDictionary)Field(Session,"_scenes"))[20L]=Scene;var screen=Nested("ProjectedScreen");Set(screen,"Data",Data);((IDictionary)Field(Scene,"Screens"))["10:view"]=screen;
   Set(Cache,"Anchor",20L);Set(Cache,"Caller",10L);Set(Cache,"Id","view");Set(Cache,"Data",Data);((IDictionary)Field(Session,"_projectedCaches"))["20:10:view"]=Cache;
   Call(Session,"RegisterDisplaySources");
  }
  public void Register(Func<string,object[],object> endpoint=null)=>Handlers[481770101](new MyTuple<string,int,Func<string,object[],object>>("test",1,endpoint??Endpoint));
  public SurfaceMesh Build()=> (SurfaceMesh)Call(Session,"BuildExternalSource",Scene,Data,Cache,32,32);
  public void VisibleGateway()
  {
   var anchor=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>m.Name switch{"get_Closed"=>false,"get_IsWorking"=>true,"get_WorldMatrix"=>MatrixD.Identity,"GetPosition"=>Vector3D.Zero,_=>throw new Exception("Unexpected visible anchor "+m.Name)});
   Gateway.Install("Entities",(m,a)=>m.Name=="GetEntityById"?anchor:throw new Exception("Unexpected visible entities "+m.Name));
   Gateway.Install("Session",(m,a)=>m.Name=="get_Camera"?DrawTestProxy.Make(m.ReturnType,(cm,ca)=>cm.Name=="get_Position"?new Vector3D(0,0,2):throw new Exception("Unexpected visible camera "+cm.Name)):throw new Exception("Unexpected visible session "+m.Name));
  }
  public void Dispose(){Call(Session,"UnregisterDisplaySources");Gateway.Dispose();}
 }
 public static int Run()
 {
  checks=0;
  using(var f=new Fixture())
  {
   Check(f.Handlers.ContainsKey(481770101)&&f.Sent.Single().Channel==481770100&&f.Sent.Single().Payload is Func<string,object[],object>,"local discovery publishes a service and installs the exact registration channel");
   Check(f.Build()==null&&f.Frames==0,"HDR alone does not attempt capture or invent an absent provider frame");
   f.Register();var mesh=f.Build();Set(f.Cache,"View",mesh);
   Check(mesh!=null&&f.LastArgs.Length==14&&f.LastArgs[13] is double&&f.LastArgs[8] is double[],"late provider supplies a bounded frame through the documented SDK-only arguments");
   Check(!ReferenceEquals(f.LastArgs[8],f.Data.Camera),"provider receives detached camera arguments");
   Check(f.Sent.Count==2,"a late provider receives the discovery service again after successful registration");
   f.Points[0]=new Vector3D(999);f.Triangles[0]=99;f.Colors[0]=Vector4.Zero;
   Check(mesh.Geometry.Points[0].X==-.4&&mesh.Geometry.Triangles[0]==0&&mesh.Colors[0]==Vector4.One,"provider cannot mutate admitted geometry arrays after returning them");
   long generation=(long)Field(f.Cache,"SourceGeneration");f.Register();Check((long)Field(f.Cache,"SourceGeneration")==generation&&Field(f.Cache,"View")!=null,"identical endpoint registration preserves a valid cache");
   f.Valid=false;Call(f.Session,"ValidateCachedExternalView",f.Scene,f.Data,f.Cache);Check(Field(f.Cache,"View")==null,"stale proof hides a cached frame immediately before draw regardless of capture cadence");
  }
  using(var f=new Fixture())
  {
   f.Register();Set(f.Cache,"View",f.Build());Func<string,object[],object> replacement=(op,args)=>op=="valid"?true:null;f.Register(replacement);
   Check(Field(f.Cache,"View")==null,"provider replacement immediately removes old geometry and evidence");
   Check(!(bool)Call(f.Session,"DisplaySourceService","unregister",new object[]{"test",f.Endpoint}),"an old endpoint cannot unregister its replacement");
   Check((bool)Call(f.Session,"DisplaySourceService","unregister",new object[]{"test",replacement}),"only the exact active endpoint may unregister");
   Check(f.Build()==null,"removed provider yields no camera or display fallback");
  }
  using(var f=new Fixture())
  {
   f.VisibleGateway();
   Check(!(bool)Call(f.Session,"DisplaySourceService","source-state",new object[]{"test",20L,10L,"feed"}),"a declared screen alone cannot activate an absent or denied provider");
   f.Register();Set(f.Cache,"View",f.Build());
   Check((bool)Call(f.Session,"DisplaySourceService","source-state",new object[]{"test",20L,10L,"feed"}),"provider receives visible-source hints from the registered HDR service");
   Call(f.Session,"DisplaySourceService","unregister",new object[]{"test",f.Endpoint});
   Check(!(bool)Call(f.Session,"DisplaySourceService","source-state",new object[]{"test",20L,10L,"feed"}),"matching provider unregistration immediately disables visible-source hints even with a live declared screen");
   f.Register();Set(f.Cache,"View",f.Build());
   object[] off={"/hdr off",true};Call(f.Session,"ClientMessageEntered",off);
   Check(!(bool)Call(f.Session,"DisplaySourceService","source-state",new object[]{"test",20L,10L,"feed"})&&((IDictionary)Field(f.Session,"_projectedCaches")).Count==0,"client off immediately clears HDR source views and stops visible-source hints");
   Call(f.Session,"UnregisterDisplaySources");Check(!(bool)Call(f.Session,"DisplaySourceService","source-state",new object[]{"test",20L,10L,"feed"}),"unloaded service refuses old consumer calls");
   Call(f.Session,"RegisterDisplaySources");f.Register();Check(f.Build()!=null,"provider discovery and frames recover after HDR service reinitialization");
  }
  using(var f=new Fixture())
  {
   f.Register();Call(f.Session,"BeginDisplaySourceDraw");Call(f.Session,"EndDisplaySourceDraw");Check(f.Begins==1&&f.Ends==1,"provider bookkeeping is scoped once per HDR draw");
   f.ThrowBegin=true;Set(f.Cache,"View",f.Build());Call(f.Session,"BeginDisplaySourceDraw");Check(Field(f.Cache,"View")==null&&f.Build()==null,"failed begin hides prior output and rejects frame callbacks for that draw");Call(f.Session,"EndDisplaySourceDraw");
  }
  using(var f=new Fixture())
  {
   f.Register();f.Override=new MyTuple<Vector3D[],int[],Vector4[],object,bool,double>(new[]{new Vector3D(0,0,.1)},new int[0],new Vector4[0],f.Proof,false,1);Reject(()=>f.Build(),"off-plane frame");
   f.Override=new MyTuple<Vector3D[],int[],Vector4[],object,bool,double>(f.Points,new[]{0,1,99},f.Colors,f.Proof,false,1);Reject(()=>f.Build(),"invalid triangle index");
   f.Override=new MyTuple<Vector3D[],int[],Vector4[],object,bool,double>(new Vector3D[33],new int[0],new Vector4[0],f.Proof,false,1);Reject(()=>f.Build(),"point allowance overflow");
   f.Override=new MyTuple<Vector3D[],int[],Vector4[],object,bool,double>(f.Points,f.Triangles,f.Colors,f.Proof,false,double.NaN);Reject(()=>f.Build(),"invalid refresh rate");
   f.Override="not a frame";Reject(()=>f.Build(),"unsupported return type");
   f.Override=null;Set(f.Cache,"View",f.Build());f.ThrowValid=true;Call(f.Session,"ValidateCachedExternalView",f.Scene,f.Data,f.Cache);Check(Field(f.Cache,"View")==null,"proof callback exceptions fail closed");
   f.ThrowFrame=true;Reject(()=>f.Build(),"provider frame callback exception");
  }
  using(var f=new Fixture())
  {
   int ended=0;Func<string,object[],object> endpoint=null;
   endpoint=(op,args)=>{if(op=="end"){ended++;Call(f.Session,"DisplaySourceService","unregister",new object[]{"test",endpoint});}return true;};
   f.Register(endpoint);Call(f.Session,"BeginDisplaySourceDraw");Call(f.Session,"DisplaySourceService","unregister",new object[]{"test",endpoint});
   Check(ended==1&&((IDictionary)Field(f.Session,"_displayProviders")).Count==0,"self-unregistering end callback runs once without recursion");
   Call(f.Session,"EndDisplaySourceDraw");Check(ended==1,"removed provider receives no extra end callback");
  }
  using(var f=new Fixture())
  {
   int ended=0;Func<string,object[],object> replacement=(op,args)=>true;
   Func<string,object[],object> endpoint=(op,args)=>{if(op=="end"){ended++;f.Register(replacement);}return true;};
   f.Register(endpoint);Call(f.Session,"BeginDisplaySourceDraw");Call(f.Session,"DisplaySourceService","unregister",new object[]{"test",endpoint});
   Check(ended==1&&((IDictionary)Field(f.Session,"_displayProviders")).Count==1,"replacement installed during end survives old endpoint unregistration");
   Call(f.Session,"EndDisplaySourceDraw");Check((bool)Call(f.Session,"DisplaySourceService","unregister",new object[]{"test",replacement}),"replacement retains independent endpoint identity");
  }
  using(var f=new Fixture())
  {
   int ended=0;Func<string,object[],object> replacement=(op,args)=>op=="frame"?new MyTuple<Vector3D[],int[],Vector4[],object,bool,double>(f.Points,f.Triangles,f.Colors,f.Proof,false,1):true;
   Func<string,object[],object> endpoint=(op,args)=>{if(op=="begin")f.Register(replacement);if(op=="end")ended++;return true;};
   f.Register(endpoint);Call(f.Session,"BeginDisplaySourceDraw");Check(f.Build()==null&&ended==1,"begin-time replacement waits for its own new draw scope");
   Call(f.Session,"EndDisplaySourceDraw");Call(f.Session,"BeginDisplaySourceDraw");Check(f.Build()!=null,"replacement starts successfully on the next draw generation");Call(f.Session,"EndDisplaySourceDraw");
  }
  Check(typeof(HoloMapSession).GetMethod("TickCameraCapture",BindingFlags.NonPublic|BindingFlags.Instance)==null&&typeof(HoloMapSession).GetField("_cameraObservationMap",BindingFlags.NonPublic|BindingFlags.Instance)==null,"HDR contains no camera scanner or retained observation store");
  return checks;
 }
}
