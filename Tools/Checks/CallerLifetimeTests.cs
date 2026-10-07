using System.Collections;
using HoloMap;
using Sandbox.ModAPI;
using VRageMath;
internal static class CallerLifetimeTests
{
 static int checks;
 static void Check(bool ok,string message){if(!ok)throw new Exception("PB lifetime: "+message);checks++;}
 static object Call(object o,string m,params object[] a)=>ClientReplicationTests.Call(o,m,a);
 static object Field(object o,string m)=>ClientReplicationTests.Field(o,m);
 public static int Run()
 {
  checks=0;using var gateway=new ClientReplicationTests.GatewayScope(){Server=true};
  bool active=true;string program="first script";var entities=new Dictionary<long,object>();
  object caller(long id)=>DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>m.Name switch{
   "get_EntityId"=>id,"get_Closed"=>false,"get_OwnerId"=>77L,"IsSameConstructAs"=>true,"get_Enabled"=>active,"get_IsWorking"=>active,
   _=>throw new Exception("Lifetime caller "+m.Name)});
  var a=caller(10);var b=caller(11);((DrawTestProxy)a).ProgramSource=()=>program;((DrawTestProxy)b).ProgramSource=()=>"other script";
  entities[10]=a;entities[11]=b;
  entities[20]=DrawTestProxy.Make(typeof(IMyProjector),(m,x)=>m.Name switch{
   "get_EntityId"=>20L,"get_Closed"=>false,"HasPlayerAccess"=>true,_=>throw new Exception("Lifetime anchor "+m.Name)});
  gateway.Install("Entities",(m,x)=>m.Name=="GetEntityById"&&entities.TryGetValue((long)x[0],out var value)?value:null);
  var session=new HoloMapSession();
  Func<string,object[],object> endpoint(object block){var h=(Func<string,object[],object>)Call(session,"DrawEndpoint",block);h("target",new[]{entities[20]});return h;}
  var ha=endpoint(a);var hb=endpoint(b);
  void Screen(Func<string,object[],object> h,string name){h("screen",new object[]{name,MatrixD.Identity,2d,2d});h("line",new object[]{"marker",0d,0d,0d,1d,0d,0d});}
  Screen(ha,"a");Screen(hb,"b");
  var scene=((IDictionary)Field(session,"_scenes"))[20L];
  void Tick(int tick){ClientReplicationTests.SetField(session,"_ticks",tick);Call(session,"TickCallerLifetimes");}
  Tick(30);Check(((IDictionary)Field(scene,"Screens")).Count==2,"ordinary time passage preserves retained displays without per-frame redraw");
  program="second script";Tick(60);
  Check(!((IDictionary)Field(scene,"Screens")).Contains("10:a")&&((IDictionary)Field(scene,"Screens")).Contains("11:b"),"script replacement retires only its own screens");
  Check(!((IDictionary)Field(scene,"Items")).Contains("10:!s!a!marker")&&((IDictionary)Field(scene,"Items")).Contains("11:!s!b!marker"),"replacement clears only its own screen content");
  Check(!(bool)ha("screen-exists",new object[]{"a"}),"script can detect retirement and republish after restart");
  Screen(ha,"new");Tick(90);Check(((IDictionary)Field(scene,"Screens")).Contains("10:new"),"replacement's new content survives the following poll");
  program="third script";ha("screen",new object[]{"fresh",MatrixD.Identity,2d,2d});
  Check(!((IDictionary)Field(scene,"Screens")).Contains("10:new")&&((IDictionary)Field(scene,"Screens")).Contains("10:fresh"),"first write of new code retires prior generation before publishing");
  active=false;Tick(120);Check(((IDictionary)Field(scene,"Screens")).Count==0&&((IDictionary)Field(scene,"Items")).Count==0,"powering PBs off retires displays and their content");
  active=true;ha("screen",new object[]{"again",MatrixD.Identity,2d,2d});Tick(150);
  Check(((IDictionary)Field(scene,"Screens")).Contains("10:again"),"powered-up PB can republish its display");
  entities.Remove(10);Tick(180);Check(((IDictionary)Field(scene,"Screens")).Count==0&&!((IDictionary)Field(session,"_drawContexts")).Contains(10L),"closing or unregistering PB retires content and context");
  return checks;
 }
}
