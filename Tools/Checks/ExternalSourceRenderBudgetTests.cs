using System.Collections;
using System.Reflection;
using HoloMap;
using VRageMath;

internal static class ExternalSourceRenderBudgetTests
{
 static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception("External source render budget: "+why);checks++;}
 static object New(string type)=>Activator.CreateInstance(typeof(HoloMapSession).GetNestedType(type,BindingFlags.NonPublic),true);
 static object Field(object o,string n)=>ClientReplicationTests.Field(o,n);static void Set(object o,string n,object v)=>ClientReplicationTests.SetField(o,n,v);
 static SurfaceMesh Mesh(int points,int triangles=2,int edges=0)=>new SurfaceMesh{Geometry=new Geometry(new Vector3D[points],new int[edges*2],new int[triangles*3])};
 sealed class Fixture
 {
  public readonly HoloMapSession Session=new();public readonly object Scene=New("Scene");public readonly IDictionary Screens,Items,Caches,Compiled;
  public Fixture(bool unlimited=false){Set(Scene,"ConsoleId",20L);if(!unlimited){Set(Scene,"PointBudget",4096);Set(Scene,"PrimitiveBudget",8192);}((IDictionary)Field(Session,"_scenes"))[20L]=Scene;Screens=(IDictionary)Field(Scene,"Screens");Items=(IDictionary)Field(Scene,"Items");Caches=(IDictionary)Field(Session,"_projectedCaches");Compiled=(IDictionary)Field(Session,"_clientGeometry");}
  public object Screen(string id,int mode=1,long caller=10)
  {
   var d=new HoloProjectedScreenData{CallerId=caller,Id=id,SourceProvider=mode==0?"":"sample",SourceId=mode==0?"":"source"};var screen=New("ProjectedScreen");Set(screen,"Data",d);Screens[caller+":"+id]=screen;
   var cache=New("ProjectedCache");Set(cache,"Anchor",20L);Set(cache,"Caller",caller);Set(cache,"Id",id);Set(cache,"Data",new HoloProjectedScreenData{SourceProvider=mode==0?"sample":"",SourceId=mode==0?"source":""});Caches["20:"+caller+":"+id]=cache;return cache;
  }
  public HoloProjectedScreenData Data(string id,long caller=10)=>(HoloProjectedScreenData)Field(Screens[caller+":"+id],"Data");
  public object Item(string id,SurfaceMesh mesh,long caller=10)
  {
   var item=New("Item");Set(item,"Id",id);Set(item,"CallerId",caller);Items[caller+":"+id]=item;
   var cache=New("CompiledDisplay");Set(cache,"ConsoleId",20L);Set(cache,"CallerId",caller);Set(cache,"Id",id);Set(cache,"Mesh",mesh);Compiled["20:"+caller+":"+id]=cache;return item;
  }
  public (int Points,int Primitives) Budget(object current)
  {object[] args={Scene,current,0,0};typeof(HoloMapSession).GetMethod("GetExternalSourceRenderBudget",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(Session,args);return ((int)args[2],(int)args[3]);}
  public void Reclaim()=>ClientReplicationTests.Call(Session,"ReclaimExternalViews",Scene);
 }
 public static int Run(){checks=0;DemoAndCacheReplacement();StaticAndScopeAccounting();InactiveAndReclaim();PrimitivePressure();AdmissionConsistency();ActualStaticReclamation();ActualProjectedReclamation();UnlimitedAllocation();return checks;}
 static void UnlimitedAllocation()
 {
  var f=new Fixture(true);var a=f.Screen("a");var b=f.Screen("b");
  f.Item("static",Mesh(Geometry.MaxPoints,Geometry.MaxPrimitives));f.Item("static2",Mesh(Geometry.MaxPoints,Geometry.MaxPrimitives));f.Item("static3",Mesh(Geometry.MaxPoints,Geometry.MaxPrimitives));
  var retained=Mesh(Geometry.MaxPoints,Geometry.MaxPrimitives);Set(a,"View",retained);Set(b,"View",Mesh(Geometry.MaxPoints,Geometry.MaxPrimitives));
  Check(f.Budget(a)==(Geometry.MaxPoints,Geometry.MaxPrimitives),"unlimited aggregate allowance gives each source its structural per-frame maximum despite accumulated static geometry");
  Check(ReferenceEquals(Field(a,"View"),retained)&&Field(b,"View")!=null,"unlimited aggregate allocation retains valid sibling frames");
  Set(f.Scene,"PointBudget",8192);var mixed=f.Budget(a);Check(mixed.Points==(8192-8-3*Geometry.MaxPoints)/2&&mixed.Primitives==Geometry.MaxPrimitives,"finite points and unlimited primitives allocate independently");
  object Invoke(string name,params object[] args)=>typeof(HoloMapSession).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);
  Check((int)Invoke("AddExternalBudgetCount",int.MaxValue-2,4)==int.MaxValue,"active geometry totals saturate rather than overflow");
  Check((int)Invoke("ExternalSourceShare",int.MaxValue,int.MaxValue,16,Geometry.MaxPoints)==Geometry.MaxPoints,"unlimited source allocation remains valid when accumulated counts saturate");
  Check((int)Invoke("ExternalSourceShare",4096,int.MaxValue,2,Geometry.MaxPoints)==0,"finite exhausted source allocation cannot underflow");
  Check((int)Invoke("ExternalSourceShare",int.MaxValue,0,0,Geometry.MaxPoints)==0,"no source declarations allocate no geometry even with unlimited allowances");
 }
 static void DemoAndCacheReplacement()
 {
  var f=new Fixture();var feeds=f.Screen("feeds");var fusion=f.Screen("fusion");
  string[] text={"CAMERA ARRAY / COLLISION POV","Main grid / auto pages / /hdr status","RETAINED OBSERVATIONS","Deduplicated map / unknown stays empty"};int total=0,primitives=0;
  for(int i=0;i<text.Length;i++){var mesh=new SurfaceMesh{Geometry=MeshEffects.Text(text[i],.11)};total+=mesh.Geometry.Points.Length;primitives+=mesh.Geometry.Triangles.Length/3;f.Item("!s!"+(i<2?"feeds":"fusion")+"!"+i,mesh);}
  Check(total>2048&&total<4096&&primitives>2000,"actual four retained-map demo strings retain substantial full font geometry");
  Set(feeds,"View",Mesh(1600));Set(fusion,"View",Mesh(1200));var budget=f.Budget(feeds);
  Check(budget.Points==(4096-total-8)/2&&budget.Primitives==(8192-primitives-4)/2,"remaining presentation budget divides fairly after the actual demo font meshes");
  Check(Field(feeds,"View")==null&&Field(fusion,"View")==null,"both oversized old camera views are cleared together");
  var small=Mesh(400,200);Set(feeds,"View",small);Set(feeds,"ViewVersion",f.Data("feeds"));var second=f.Budget(fusion);
  Check(second==budget&&ReferenceEquals(Field(feeds,"View"),small),"below-share prior view neither steals allocation nor gets discarded");
  Check((int)Field(feeds,"Reported")==-10000&&Field(feeds,"Error")==null,"ordinary allocation pressure does not report an error");
  Set(f.Scene,"PointBudget",8192);Set(f.Scene,"PrimitiveBudget",16384);var raised=f.Budget(feeds);
  Check(raised.Points==Geometry.MaxPoints&&raised.Primitives==Geometry.MaxPrimitives,"configured anchor budget expands fair share while preserving per-object SDK limits");
 }
 static void StaticAndScopeAccounting()
 {
  var f=new Fixture();var a=f.Screen("a");var b=f.Screen("b",caller:11);var staticScreen=f.Screen("static",0);
  f.Item("root",Mesh(100));f.Item("!s!a!own",Mesh(40));f.Item("!s!a!foreign",Mesh(900),11);f.Item("!s!missing!old",Mesh(900));
  var removed=f.Item("removed",Mesh(900));f.Items.Remove("10:removed");
  Set(a,"Sprites",Mesh(80,40));Set(b,"Sprites",Mesh(60,30));Set(staticScreen,"Sprites",Mesh(24,12));Set(staticScreen,"View",Mesh(200,100));
  Set(a,"View",Mesh(2000));Set(b,"View",Mesh(2000));
  var budget=f.Budget(a);Check(budget.Points==(4096-12-100-40-80-60-24-200)/2,"all static families count, absent/foreign scoped items and every camera view do not");
  Check(Field(staticScreen,"View")!=null,"latest declaration wins over stale cached camera metadata");
  var outsider=New("ProjectedCache");Set(outsider,"Anchor",21L);Set(outsider,"Caller",10L);Set(outsider,"Id","a");Set(outsider,"View",Mesh(2048));f.Caches["21:10:a"]=outsider;
  Check(f.Budget(a)==budget&&Field(outsider,"View")!=null,"other anchor allocation and payload stay isolated");
  Check(f.Budget(outsider)==(0,0),"current cache must belong to requested anchor and declared camera family");
 }
 static void InactiveAndReclaim()
 {
  var f=new Fixture();var visible=f.Screen("same");var hidden=f.Screen("same",caller:11);var fixedView=f.Screen("static",0);
  f.Data("same",11).Visible=false;Set(hidden,"View",Mesh(2048));Set(hidden,"Sprites",Mesh(2048));Set(hidden,"SpriteVersion",f.Data("same",11));f.Item("!s!same!text",Mesh(2000),11);
  var invisible=f.Item("invisible",Mesh(1000));Set(invisible,"Visible",false);var transparent=f.Item("transparent",Mesh(1000));Set(transparent,"Opacity",0f);
  var layer=New("Layer");Set(layer,"Visible",false);((IDictionary)Field(f.Scene,"Layers"))["10:hidden"]=layer;var layered=f.Item("layered",Mesh(1000));Set(layered,"Layer","hidden");
  Set(fixedView,"View",Mesh(200));Set(visible,"Sprites",Mesh(100));Set(visible,"View",Mesh(300));
  var budget=f.Budget(visible);Check(budget.Points==2048&&budget.Primitives==4096,"hidden declaration families and hidden items do not consume active camera share; object caps apply");
  Check(Field(hidden,"View")==null&&Field(hidden,"Sprites")==null&&Field(hidden,"SpriteVersion")==null,"inactive screen render payloads and compile versions are released");Check(f.Budget(hidden)==(0,0),"hidden current screen has no allocation");
  var retainedStatic=Field(fixedView,"View");var retainedSprites=Field(visible,"Sprites");f.Reclaim();Check(Field(visible,"View")==null&&ReferenceEquals(Field(fixedView,"View"),retainedStatic)&&ReferenceEquals(Field(visible,"Sprites"),retainedSprites),"static admission reclaims only camera views, preserving static views and every sprite layer");
  Set(visible,"View",Mesh(300));f.Item("full",Mesh(2048));f.Item("full2",Mesh(2048));Check(f.Budget(visible).Points==0&&Field(visible,"View")==null,"exhausted static point budget yields empty camera allocation without exceptions");
 }
 static void PrimitivePressure()
 {
  var f=new Fixture();var a=f.Screen("a");var b=f.Screen("b");f.Item("triangles",Mesh(8,4000));f.Item("lines",Mesh(8,0,4000));Set(a,"View",Mesh(4,96));Set(b,"View",Mesh(4,95));
  var budget=f.Budget(a);Check(budget.Primitives==94&&budget.Points==2036,"edge and triangle primitives independently constrain equal shares");Check(Field(a,"View")==null&&Field(b,"View")==null,"primitive-over-budget camera caches are cleared even when point count is small");
 }
 static void AdmissionConsistency()
 {
  var f=new Fixture();var a=f.Screen("a");var b=f.Screen("b");f.Item("visible",Mesh(1000));
  var hidden=f.Item("hidden",Mesh(2048));Set(hidden,"Visible",false);var alpha=f.Item("alpha",Mesh(2048));Set(alpha,"Opacity",0f);
  var orphan=f.Screen("reused",caller:10);Set(orphan,"View",Mesh(2048));Set(orphan,"Sprites",Mesh(2048));f.Item("!s!reused!old",Mesh(2048));f.Screens.Remove("10:reused");var reused=f.Screen("reused",caller:11);f.Data("reused",11).Visible=false;
  var budget=f.Budget(a);var candidate=Mesh(budget.Points,2);Set(b,"View",Mesh(budget.Points,2));
  Check((bool)ClientReplicationTests.Call(f.Session,"ProjectedMeshFits",f.Scene,a,candidate,false),"final camera admission uses the same active accounting despite large hidden and orphan caches");
  Check(Field(orphan,"View")==null&&Field(orphan,"Sprites")==null,"same screen ID on another owner never revives deleted-owner cache payloads");
  f.Data("reused",11).Visible=true;var next=f.Budget(reused);Check(next.Points==(4096-12-1000)/3,"reused ID on new caller joins equal sharing without inheriting old child costs");
  Set(reused,"Sprites",Mesh(300));var sprite=Mesh(304);Check((bool)ClientReplicationTests.Call(f.Session,"ProjectedMeshFits",f.Scene,reused,sprite,true),"sprite replacement excludes only its old payload under the shared accounting rule");
 }
 static void ActualStaticReclamation()
 {
  var f=new Fixture();var a=f.Screen("a");var b=f.Screen("b");Set(a,"View",Mesh(2000));Set(b,"View",Mesh(2000));
  var hidden=f.Item("hidden",Mesh(2048));Set(hidden,"Visible",false);var pending=f.Item("pending",Mesh(4));Set(pending,"Geometry",Mesh(200).Geometry);Set(pending,"FillColor",Vector4.One);
  string key="20:10:pending";var cache=f.Compiled[key];Set(cache,"PendingItem",pending);Set(cache,"PendingScene",f.Scene);Set(cache,"LastSeen",0);
  ((Queue<string>)Field(f.Session,"_compileQueue")).Enqueue(key);((HashSet<string>)Field(f.Session,"_queuedCompile")).Add(key);
  ClientReplicationTests.Call(f.Session,"CompileQueuedDisplay");var compiled=(SurfaceMesh)Field(cache,"Mesh");
  Check(compiled.Geometry.Points.Length==200&&Field(cache,"Error")==null,"actual static compilation replaces old mesh without counting hidden caches");
  Check(Field(a,"View")==null&&Field(b,"View")==null,"actual compiler reclaims camera views before admitting static content");
  Check(f.Budget(a).Points==(4096-8-200)/2,"camera shares recompute from successfully admitted static mesh");
 }
 static void ActualProjectedReclamation()
 {
  foreach(bool sprite in new[]{true,false})foreach(bool staticOverflow in new[]{false,true})
  {
   var f=new Fixture();var a=f.Screen("a");var b=f.Screen("b");var target=f.Screen("static",0);Set(a,"View",Mesh(2000));Set(b,"View",Mesh(2000));f.Item("base",Mesh(staticOverflow?2048:1000));if(staticOverflow)f.Item("base2",Mesh(2048));
   var data=f.Data("static");var screen=f.Screens["10:static"];
   if(sprite)data.Sprites=new List<HoloProjectedSpriteData>{new HoloProjectedSpriteData{Type=0,Data="SquareSimple",Position=new[]{256f,256f},Size=new[]{100f,100f},Color=new[]{1f,1f,1f,1f},Alignment=2}};
   else{Set(screen,"Source",new Geometry(new[]{new Vector3D(-.5,-.5,0),new Vector3D(.5,-.5,0),new Vector3D(0,.5,0)},new int[0],new[]{0,1,2}));Set(screen,"SourceColors",new[]{Vector4.One});}
   ClientReplicationTests.Call(f.Session,"PrepareProjectedSources",f.Scene,screen,target,1);
   var output=Field(target,sprite?"Sprites":"View");
   Check(Field(a,"View")==null&&Field(b,"View")==null,(sprite?"sprite":"perspective")+" admission reclaims lower-priority camera views");
   Check(staticOverflow?output==null&&Field(target,"Error")!=null:output!=null&&Field(target,"Error")==null,(sprite?"sprite":"perspective")+" retries once and retains hard static-only overflow rejection");
  }
 }
}
