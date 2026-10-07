using System.Collections;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

internal static class DisplaySourceOcclusionTests
{
 static int checks;
 static void Check(bool ok,string why){if(!ok)throw new Exception("Source occlusion inventory: "+why);checks++;}
 static object New(string name)=>Activator.CreateInstance(typeof(HoloMapSession).GetNestedType(name,BindingFlags.NonPublic),true);
 static object Field(object o,string n)=>ClientReplicationTests.Field(o,n);
 static void Set(object o,string n,object v)=>ClientReplicationTests.SetField(o,n,v);
 static object Call(object o,string n,params object[] a)=>ClientReplicationTests.Call(o,n,a);
 static readonly double[] Identity={1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
 sealed class Fixture:IDisposable
 {
  public readonly ClientReplicationTests.GatewayScope Gateway=new();public readonly HoloMapSession Session=new();
  public readonly object Scene=New("Scene");public readonly IMyTerminalBlock Anchor;
  public MatrixD World=MatrixD.Identity;public bool Static=true,Fault,Lcd;
  public Fixture()
  {
   Set(Scene,"ConsoleId",20L);((IDictionary)Field(Session,"_scenes"))[20L]=Scene;
   var grid=DrawTestProxy.Make(typeof(VRage.Game.ModAPI.IMyCubeGrid),(m,a)=>m.Name=="get_IsStatic"?Static:throw new Exception(m.Name));
   object MakeAnchor(Type t)=>DrawTestProxy.Make(t,(m,a)=>m.Name switch{"get_Closed"=>false,"get_IsWorking"=>true,"get_CubeGrid"=>grid,"get_WorldMatrix"=>Fault?throw new Exception("matrix unavailable"):World,_=>throw new Exception(m.Name)});
   Anchor=(IMyTerminalBlock)MakeAnchor(typeof(IMyProjector));var lcd=MakeAnchor(typeof(IMyTextPanel));
   Gateway.Install("Entities",(m,a)=>m.Name=="GetEntityById"&&(long)a[0]==20?(Lcd?lcd:Anchor):null);
  }
  public HoloProjectedScreenData Add(string id="a",string source="1,2",int kind=0)
  {var d=new HoloProjectedScreenData{CallerId=10,Id=id,Pose=(double[])Identity.Clone(),SourceProvider="camera-panorama",SourceId=source,SurfaceKind=kind};var screen=New("ProjectedScreen");Set(screen,"Data",d);((IDictionary)Field(Scene,"Screens"))["10:"+id]=screen;return d;}
  public void Publish(bool record=true)
  {Call(Session,"BeginDisplaySourceOcclusionDraw");if(record)Call(Session,"RecordDisplaySourceOcclusion",Scene,Anchor,World);Call(Session,"EndDisplaySourceOcclusionDraw");}
  public MyTuple<bool,object,MyTuple<string,object,double[]>[]> Query(long camera=1)=>(MyTuple<bool,object,MyTuple<string,object,double[]>[]>)Call(Session,"DisplaySourceOcclusion",(object)new object[]{camera});
  public void Dispose()=>Gateway.Dispose();
 }
 static double Min(double[] xyz,int axis)=>Enumerable.Range(0,xyz.Length/3).Min(i=>xyz[i*3+axis]);
 static double Max(double[] xyz,int axis)=>Enumerable.Range(0,xyz.Length/3).Max(i=>xyz[i*3+axis]);
 public static int Run()
 {
  checks=0;
  using(var f=new Fixture())
  {
   var d=f.Add();Check(!f.Query().Item1,"no Draw publication retains demand");f.Publish();var first=f.Query();
   Check(first.Item1&&first.Item3.Length==1&&first.Item3[0].Item1=="20:10:a","complete exact camera consumer closure");
   Check(first.Item3[0].Item3.Length==108&&first.Item3[0].Item3.All(double.IsFinite),"bounded complete closed world enclosure");
   Check(f.Query(2).Item1&&!f.Query(3).Item1,"each comma-delimited camera is matched exactly");
   var original=first.Item3[0].Item3[0];first.Item3[0].Item3[0]=999;var copied=f.Query();Check(copied.Item3[0].Item3[0]==original,"callback cannot mutate immutable publication");
   f.Publish();var same=f.Query();Check(ReferenceEquals(first.Item2,same.Item2)&&ReferenceEquals(first.Item3[0].Item2,same.Item3[0].Item2),"unchanged numeric geometry retains exact immutable identities");
   d.Width=6;Check(!f.Query().Item1,"unpublished geometry edit invalidates current proof");f.Publish();var bigger=f.Query();Check(bigger.Item1&&!ReferenceEquals(same.Item2,bigger.Item2),"expanded footprint publishes new proof identity");
   d.Width=1;f.Publish();var smaller=f.Query();Check(smaller.Item1&&!ReferenceEquals(bigger.Item2,smaller.Item2)&&ReferenceEquals(bigger.Item3[0].Item2,smaller.Item3[0].Item2)&&Max(smaller.Item3[0].Item3,0)>=3.03,"history never shrinks but changed current closure receives fresh validation identity");
   d.Pose[12]=10;Check(!f.Query().Item1,"pose edit invalidates before Draw");f.Publish();var moved=f.Query();Check(Min(moved.Item3[0].Item3,0)<=-3.03&&Max(moved.Item3[0].Item3,0)>=10.53,"new pose unions old and current submitted geometry");
   f.Add("b");Check(!f.Query().Item1,"new unpublished consumer invalidates ALL inventory");f.Publish();var two=f.Query();Check(two.Item1&&two.Item3.Length==2,"all consumers are returned together");
   ((IDictionary)Field(f.Scene,"Screens")).Remove("10:b");f.Publish();Check(f.Query().Item3.Length==2,"removed consumer remains in historical renderer envelope");
   d.Visible=false;d.Opacity=0;f.Publish();Check(f.Query().Item1,"visibility and opacity do not truncate geometry");
   f.World=MatrixD.CreateTranslation(1,0,0);Check(!f.Query().Item1,"current anchor matrix mismatch fails open");f.Publish();Check(f.Query().Item1,"static anchor reread agrees with actual Draw world");
   f.Static=false;Check(!f.Query().Item1,"dynamic interpolation cannot be certified");f.Static=true;f.Fault=true;Check(!f.Query().Item1,"gateway faults retain capture demand");f.Fault=false;
   f.Lcd=true;Check(!f.Query().Item1,"LCD consumers cannot masquerade as projected consumers");f.Lcd=false;
   Set(f.Session,"_ticks",2);Check(!f.Query().Item1,"stale Draw publication fails open");f.Publish();Check(f.Query().Item1,"fresh Draw restores proof");
   Call(f.Session,"ClearDisplaySourceOcclusion");Check(!f.Query().Item1&&((IDictionary)Field(f.Session,"_sourceOcclusionInventories")).Count==0,"world unload releases historical inventory and identities");
  }
  using(var f=new Fixture())
  {
   f.Add(kind:4);f.Publish();Check(!f.Query().Item1,"arbitrary mesh cannot certify bounded source enclosure");
   var d=f.Add(kind:2);f.Publish(false);Check(!f.Query().Item1,"unvisited consumer remains unknown even when declared");
   d.SurfaceRadius=4;f.Publish();var g=f.Query().Item3[0].Item3;Check(Min(g,0)<=-4.03&&Max(g,0)>=4.03&&Min(g,2)<=-4.03&&Max(g,2)>=4.03,"curved enclosure covers bulge beyond coarse tessellation vertices");
   d.SurfaceKind=3;d.SurfaceRadii=new[]{2d,3d,4d};f.Publish();g=f.Query().Item3[0].Item3;Check(Min(g,1)<=-3.03&&Max(g,2)>=4.03,"ellipsoid full analytic extent is enclosed");
  }
  using(var f=new Fixture())
  {
   f.Add();Call(f.Session,"BeginDisplaySourceOcclusionDraw");
   Call(f.Session,"DrawProjectedScreens",f.Scene,f.Anchor,0,0);
   Call(f.Session,"EndDisplaySourceOcclusionDraw");
   Check(f.Query().Item1&&f.Query().Item3[0].Item3.Length==108,"actual Draw hook preserves full inventory when work and compile budgets are exhausted");
  }
  using(var f=new Fixture())
  {
   var d=f.Add();f.Publish();var cached=f.Query();
   Set(f.Session,"_displaySourceRegistered",true);
   var fresh=(Func<object,bool>)Call(f.Session,"DisplaySourceService","source-occlusion-fresh",new object[]{1L});
   Check(fresh(cached.Item2)&&!fresh(new object())&&!fresh(null),"render-safe freshness accepts only validated publication identity");
   f.Publish();Check(fresh(cached.Item2),"unchanged Draw retains cached game-update proof validity");
   d.Width=8;Call(f.Session,"BeginDisplaySourceOcclusionDraw");Check(!fresh(cached.Item2),"beginning an intervening Draw invalidates pending GPU submission immediately");
   Call(f.Session,"RecordDisplaySourceOcclusion",f.Scene,f.Anchor,f.World);Call(f.Session,"EndDisplaySourceOcclusionDraw");
   Check(!fresh(cached.Item2),"larger surface between game update and GPU submission rejects cached undersized enclosure");
   cached=f.Query();Check(cached.Item1&&fresh(cached.Item2),"game-update revalidation admits enlarged immutable envelope");
   var hidden=f.Add("hidden");hidden.Visible=false;f.Publish(false);Check(!fresh(cached.Item2),"unvisited hidden new consumer invalidates freshness even if historical envelope did not grow");
   ((IDictionary)Field(f.Scene,"Screens")).Remove("10:hidden");f.Publish();cached=f.Query();
   f.Static=false;f.Add("moving").Visible=false;f.Publish();Check(!fresh(cached.Item2),"new hidden dynamic consumer cannot reuse static history proof");
   f.Static=true;((IDictionary)Field(f.Scene,"Screens")).Remove("10:moving");f.Publish();cached=f.Query();
   f.Gateway.Install("Entities",(m,a)=>throw new Exception("render thread must not query gateway"));
   Check(fresh(cached.Item2),"render-safe freshness reads immutable publication without entities or scenes");
   Call(f.Session,"ClearDisplaySourceOcclusion");Check(!fresh(cached.Item2),"captured freshness delegate invalidates on unload");
  }
  using(var f=new Fixture())
  {
   for(int i=0;i<=16;i++){f.Add("x"+i);f.Publish();((IDictionary)Field(f.Scene,"Screens")).Clear();}
   f.Add();f.Publish();Check(!f.Query().Item1,"historical inventory overflow refuses truncated proof");
  }
  return checks;
 }
}
