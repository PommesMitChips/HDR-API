using System.Collections;
using System.Reflection;
using System.Text;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using VRage.Game.GUI.TextPanel;

internal static class ProjectedScreenIntegrationTests
{
 static int checks;
 static void Check(bool ok,string why){if(!ok)throw new Exception("Projected screen integration: "+why);checks++;}
 static void Reject(Action work,string why){try{work();}catch(ArgumentException){checks++;return;}throw new Exception("Expected screen rejection: "+why);}
 static object Call(object o,string n,params object[] a)=>ClientReplicationTests.Call(o,n,a);
 static object Field(object o,string n)=>ClientReplicationTests.Field(o,n);
 static void Set(object o,string n,object v)=>ClientReplicationTests.SetField(o,n,v);
 static object Static(string name,params object[] args)
 {try{return typeof(HoloMapSession).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
 sealed class Fixture:IDisposable
 {
  public readonly ClientReplicationTests.GatewayScope Gateway=new ClientReplicationTests.GatewayScope{Server=true,Dedicated=false};
  public readonly HoloMapSession Session=new HoloMapSession();
  public readonly Dictionary<long,object> Registry=new Dictionary<long,object>();
  public bool Access=true,FailWorld,FailRender;public int WorldReads,RenderReads;public MatrixD World=MatrixD.Identity;
  public Fixture(){AddCaller(10);AddCaller(11);AddAnchor(20);AddAnchor(21);Gateway.Install("Entities",(m,a)=>m.Name=="GetEntityById"&&Registry.TryGetValue((long)a[0],out var value)?value:null);}
  void AddCaller(long id){Registry[id]=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>m.Name switch{"get_EntityId"=>id,"get_OwnerId"=>77L,"get_Closed"=>false,"IsSameConstructAs"=>true,_=>throw new Exception("Caller "+m.Name)});}
  public void AddAnchor(long id){Registry[id]=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>{
   if(m.Name=="get_EntityId")return id;if(m.Name=="get_Closed")return false;if(m.Name=="get_IsWorking")return true;if(m.Name=="HasPlayerAccess")return Access;
   if(m.Name=="get_BlockDefinition")return Activator.CreateInstance(m.ReturnType);
   if(m.Name=="get_WorldMatrix"){WorldReads++;if(FailWorld)throw new IndexOutOfRangeException("Injected screen transform fault");return World;}
   if(m.Name=="get_Render"){RenderReads++;if(FailRender)throw new InvalidOperationException("Injected screen render fault");throw new Exception("GPU render access forbidden in this test");}
   throw new Exception("Anchor "+m.Name);
  });}
  public Func<string,object[],object> Draw(long caller=10,long anchor=20){var h=(Func<string,object[],object>)Call(Session,"DrawEndpoint",Registry[caller]);h("target",new[]{Registry[anchor]});return h;}
  public object Scene(long anchor=20)=>((IDictionary)Field(Session,"_scenes"))[anchor];
  public IDictionary Bag(string name,long anchor=20)=>(IDictionary)Field(Scene(anchor),name);
  public object Screen(string id,long caller=10,long anchor=20)=>Bag("Screens",anchor)[caller+":"+id];
  public HoloProjectedScreenData Data(string id,long caller=10,long anchor=20)=>(HoloProjectedScreenData)Field(Screen(id,caller,anchor),"Data");
  public void Dispose()=>Gateway.Dispose();
 }
 static void Make(Func<string,object[],object> h,string id)=>h("screen",new object[]{id,MatrixD.Identity,2d,1d,2d,1d});
 static void Line(Func<string,object[],object> h,string id)=>h("line",new object[]{id,Vector3D.Zero,Vector3D.UnitX,"cyan"});
 static string Packed(){byte[] bytes=Encoding.ASCII.GetBytes("HMA1\n1\n1\n1\n<svg viewBox='0 0 1 1'><rect width='1' height='1'/></svg>\n");var encoded=new List<byte>{72,77,67,49};encoded.AddRange(BitConverter.GetBytes(bytes.Length));for(int i=0;i<bytes.Length;i+=8){int n=Math.Min(8,bytes.Length-i);encoded.Add((byte)((1<<n)-1));encoded.AddRange(bytes.Skip(i).Take(n));}return "HoloMapAnimation1:"+Convert.ToBase64String(encoded.ToArray());}
 public static int Run(){checks=0;AnchorVolumeCompatibility();Sides();Isolation();ValidationAndQuotas();ProjectionAndFaultBoundary();DemoRasterBudget();CacheIdentityAndClocks();CaptureFairness();return checks;}
 static void Sides()
 {
  using var f=new Fixture();var h=f.Draw();Make(h,"both");
  var d=f.Data("both");var front=new Vector3D(0,0,2);var back=-front;
  float Alpha(Vector3D eye)=>(float)Static("ScreenSideOpacity",f.Data("both"),MatrixD.Identity,eye);
  bool Visible(Vector3D eye)=>(bool)Static("ScreenSurfaceVisible",f.Data("both"),MatrixD.Identity,eye);
  double Depth(Vector3D eye)=>(double)Static("ScreenLayerDepth",f.Data("both"),MatrixD.Identity,eye,.0015d);
  Check(!d.TwoSided&&d.FrontOpacity==1&&d.BackOpacity==1&&Visible(front)&&!Visible(back),"screens retain single-sided presentation by default");
  h("screen-two-sided",new object[]{true,.8,.2});
  Check(Alpha(front)==.8f&&Alpha(back)==.2f&&Visible(back),"two-sided command enables the reverse with independent opacity");
  Check(Depth(front)==.0015&&Depth(back)==-.0015,"foreground depth moves toward the current side so a solid background cannot cover reverse content");
  var settings=(MyTuple<bool,float,float>)h("screen-side-settings",Array.Empty<object>());
  Check(settings.Item1&&settings.Item2==.8f&&settings.Item3==.2f,"short query returns sidedness and both opacity values");
  h("screen-side-opacity",new object[]{0d,.3});
  Check(!Visible(front)&&Visible(back)&&Alpha(back)==.3f,"fully transparent front culls front work while retaining the back");
  h("screen-side-opacity",new object[]{1d,0d});Check(Visible(front)&&!Visible(back),"zero back opacity culls reverse work");
  h("screen-two-sided",new object[]{false});Check(!Visible(back)&&Alpha(front)==1,"one-sided command restores single-side defaults");
  h("screen-two-sided",new object[]{true,.6,.15});
  foreach(var kind in new[]{"cylinder","sphere"})
  {
   h("screen-surface",new object[]{kind,3d,Math.PI,Math.PI/2,"inside"});
   Check(Alpha(Vector3D.Zero)==.6f&&Alpha(new Vector3D(0,0,4))==.15f,kind+" applies primary inside and opposite outside opacity");
   Check(Depth(Vector3D.Zero)>0&&Depth(new Vector3D(0,0,4))<0,kind+" reverses layer offsets for the opposite side");
   h("screen-surface",new object[]{kind,3d,Math.PI,Math.PI/2,"outside"});
   Check(Alpha(new Vector3D(0,0,4))==.6f&&Alpha(Vector3D.Zero)==.15f,kind+" respects outside as the selected primary side");
  }
  h("screen-surface",new object[]{"plane"});
  var pose=MatrixD.CreateRotationY(.8)*MatrixD.CreateTranslation(1,2,3);
  var world=MatrixD.CreateRotationX(.4)*MatrixD.CreateTranslation(1000000,-2000000,3000000);
  h("screen-pose",new object[]{pose});d=f.Data("both");
  Check((float)Static("ScreenSideOpacity",d,world,Vector3D.Transform(front,pose*world))==.6f&&(float)Static("ScreenSideOpacity",d,world,Vector3D.Transform(back,pose*world))==.15f,"facing follows screen pose and moving anchor in large world coordinates");
  Reject(()=>h("screen-two-sided",new object[]{true,double.NaN,.5}),"nonfinite side opacity");
  Reject(()=>h("screen-side-opacity",new object[]{1d,1.01}),"back opacity beyond one");
  Reject(()=>h("screen-side-opacity",new object[]{-.1,1d}),"negative front opacity");
  Reject(()=>h("screen-two-sided",new object[]{"both"}),"wrong flag type");
  Check(f.Data("both").FrontOpacity==.6f&&f.Data("both").BackOpacity==.15f,"rejected side commands preserve the previous settings atomically");
  f.Access=false;Reject(()=>h("screen-two-sided",new object[]{true}),"sidedness changes still require current display access");
 }
 static void AnchorVolumeCompatibility()
 {
  using var f=new Fixture();var h=f.Draw();
  h("volume",new object[]{false});h("range",new object[]{0d});Make(h,"demo");
  Check(!(bool)Field(f.Scene(),"TableVolumeEnabled")&&f.Bag("Screens").Contains("10:demo"),"shared demo setup disables nonexistent table volume on an ordinary projector and creates its screen");
  h("screen-target",new object[]{""});Reject(()=>h("volume",new object[]{true}),"ordinary projector still cannot enable a table-specific volume");
  f.Access=false;Reject(()=>h("volume",new object[]{false}),"volume disabling still rechecks block access");
 }
 static void Isolation()
 {
  using var f=new Fixture();var h=f.Draw();Line(h,"world");h("layer",new object[]{"world","common"});h("animate",new object[]{"world","rotation",0d,1d,2d,true,false});h("play",new object[]{"packed",Packed(),Vector3D.Zero,.1,3});h("pause",Array.Empty<object>());
  Make(h,"a");Line(h,"same");h("label",new object[]{"label","A",Vector3D.Zero,.05,"white"});
  Check((string)Field(f.Bag("Items")["10:!s!a!same"],"Layer")=="s_a__default"&&(string)Field(f.Bag("Labels")["10:!s!a!label"],"Layer")=="s_a__default","default item and label layers are screen-local");
  h("layer",new object[]{"same","common"});h("layer-opacity",new object[]{"common",.25});h("animate",new object[]{"same","rotation",0d,1d,2d,true,false});h("play",new object[]{"packed",Packed(),Vector3D.Zero,.1,3});
  Check(((HoloAnimationData)Field(((IDictionary)Field(f.Session,"_drawAnimations"))["20:10:world"],"Data")).Paused,"starting child animations does not resume paused root timelines");
  h("sprites",new object[]{new[]{MySprite.CreateSprite("SquareSimple",new Vector2(256),new Vector2(64))}});
  h("screen-scene",new object[]{new[]{Vector3D.Zero,Vector3D.UnitX,Vector3D.UnitY},new[]{0,1,2},new[]{Vector4.One}});
  var rootLayer=f.Bag("Layers")["10:common"];Check((float)Field(rootLayer,"Opacity")==1&&(float)Field(f.Bag("Layers")["10:s_a__common"],"Opacity")==.25f,"screen layer updates preserve root layer");
  Make(h,"b");Line(h,"same");h("layer",new object[]{"same","common"});h("animate",new object[]{"same","rotation",0d,1d,2d,true,false});h("play",new object[]{"packed",Packed(),Vector3D.Zero,.1,3});
  var other=f.Draw(11);Reject(()=>other("screen",new object[]{"b"}),"another caller cannot select an owned screen");Make(other,"a");Line(other,"same");
  Check(f.Bag("Items").Contains("10:!s!a!same")&&f.Bag("Items").Contains("10:!s!b!same")&&f.Bag("Items").Contains("11:!s!a!same"),"two screens and another caller retain separate same-ID children");
  h("screen",new object[]{"a"});h("clear",Array.Empty<object>());
  Check(!f.Bag("Items").Contains("10:!s!a!same")&&!f.Bag("Labels").Contains("10:!s!a!label")&&!f.Bag("Layers").Contains("10:s_a__common"),"selected clear removes only its items labels and layers");
  Check(f.Bag("Items").Contains("10:world")&&f.Bag("Items").Contains("10:!s!b!same")&&f.Bag("Items").Contains("11:!s!a!same")&&f.Bag("Layers").Contains("10:common"),"selected clear preserves root sibling and other-owner content");
  Check(f.Data("a").Sprites==null&&f.Data("a").SourcePoints==null&&f.Bag("Screens").Contains("10:a"),"clear empties screen sources while retaining the screen");
  var animations=(IDictionary)Field(f.Session,"_drawAnimations");var packed=(IDictionary)Field(f.Session,"_packed");
  Check(!animations.Contains("20:10:!s!a!same")&&!animations.Contains("20:10:!s!a!packed")&&animations.Contains("20:10:world")&&animations.Contains("20:10:!s!b!same"),"clear scopes animation removal");
  Check(!packed.Contains("20:10:!s!a!packed")&&packed.Contains("20:10:packed")&&packed.Contains("20:10:!s!b!packed"),"clear scopes packed-cache removal");
  h("screen-remove",Array.Empty<object>());Line(h,"root-again");Check(!f.Bag("Screens").Contains("10:a")&&f.Bag("Items").Contains("10:root-again"),"removing selected screen returns to root content");
  var rejected=(MyTuple<bool,string>)Call(f.Session,"PutWires",f.Registry[10],f.Registry[20],"!s!b!forged",new[]{Vector3D.Zero,Vector3D.UnitX},new[]{new Vector2I(0,1)},Vector4.One,.008f);
  Check(!rejected.Item1&&!f.Bag("Items").Contains("10:!s!b!forged"),"legacy typed write cannot forge reserved child IDs");
  rejected=(MyTuple<bool,string>)Call(f.Session,"SetLayerVisible",f.Registry[10],f.Registry[20],"s_b__common",false);Check(!rejected.Item1,"legacy typed layer write cannot forge screen layer scope");
  Reject(()=>Line(h,"!s!b!forged"),"root HDR.Draw rejects reserved IDs");h("screen",new object[]{"b"});Reject(()=>Line(h,"bad~id"),"child IDs cannot collide with packed suffixes");Reject(()=>Line(h,"bad!id"),"child IDs cannot forge scope delimiters");
  int callbackCalls=0;Reject(()=>h("curve",new object[]{"bad",(Func<double,Vector3D>)(t=>{callbackCalls++;return Vector3D.UnitX*t;}),0d,1d,"cyan"}),"child scope rejects callback-bearing commands");Check(callbackCalls==0,"rejected callback is never executed");
  Reject(()=>h("line",new object[]{"broken",Vector3D.Zero,Vector3D.Zero,"cyan"}),"failed child mutation");rejected=(MyTuple<bool,string>)Call(f.Session,"SetVisible",f.Registry[10],f.Registry[20],"!s!b!same",false);Check(!rejected.Item1,"failed nested command restores reserved write scope");
  h("target",new[]{f.Registry[21]});Line(h,"same");Check(f.Bag("Items",21).Contains("10:same")&&!f.Bag("Items",21).Contains("10:!s!b!same"),"target changes reset selected screen");
 }
 static void ValidationAndQuotas()
 {
  using(var f=new Fixture())
  {
   var h=f.Draw();Make(h,"a");Line(h,"keep");var before=f.Data("a");
   foreach(Action bad in new Action[]{()=>h("screen-opacity",new object[]{double.NaN}),()=>h("screen-pose",new object[]{MatrixD.CreateScale(2)}),()=>h("screen",new object[]{"bad!id",MatrixD.Identity}),()=>h("screen-camera",new object[]{Vector3D.Zero,Vector3D.Zero,Vector3D.UnitY}),()=>h("screen-scene",new object[]{new[]{Vector3D.Zero,Vector3D.UnitX,Vector3D.UnitY},new[]{0,1,99},new[]{Vector4.One}}),()=>h("sprites",new object[]{new[]{MySprite.CreateSprite("Unknown",Vector2.Zero,Vector2.One)}})})
   {Reject(bad,"malformed screen mutation");Check(ReferenceEquals(before,f.Data("a"))&&f.Bag("Items").Contains("10:!s!a!keep"),"failed mutations preserve previous screen and content");}
   f.Access=false;Reject(()=>h("screen-visible",new object[]{false}),"ownership/access rechecked on mutation");Check(ReferenceEquals(before,f.Data("a")),"access rejection is atomic");f.Access=true;
   var p=new[]{Vector3D.Zero,Vector3D.UnitX,Vector3D.UnitY};var t=new[]{0,1,2};var colors=new[]{Vector4.One};h("screen-scene",new object[]{p,t,colors});p[0]=new Vector3D(999);t[0]=2;colors[0]=Vector4.Zero;
   Check(f.Data("a").SourcePoints[0]==0&&f.Data("a").SourceTriangles[0]==0&&f.Data("a").SourceColors[0]==1,"source admission copies mutable caller arrays");
   var sprites=new[]{MySprite.CreateSprite("SquareSimple",new Vector2(100),new Vector2(50))};h("sprites",new object[]{sprites});sprites[0].Data="Unknown";Check(f.Data("a").Sprites[0].Data=="SquareSimple","sprite admission copies caller frame");
   foreach(string id in new[]{"b","c","d","e","f","g","h"})Make(h,id);Reject(()=>Make(h,"i"),"eight screens per anchor");Check(f.Bag("Screens").Count==8&&((MyTuple<string,double,double,double>)h("screen-settings",Array.Empty<object>())).Item1=="h","screen count rejection preserves selection and declarations");
   for(int i=0;i<7;i++)Line(h,"n"+i);Reject(()=>Line(h,"overflow"),"children share the existing anchor object quota");Check(f.Bag("Items").Count==8,"rejected object admission leaves quota unchanged");
  }
  using(var f=new Fixture())
  {
   for(long anchor=20;anchor<=23;anchor++){if(!f.Registry.ContainsKey(anchor))f.AddAnchor(anchor);var h=f.Draw(10,anchor);for(int i=0;i<4;i++)Make(h,"s"+i);}
   f.AddAnchor(24);var last=f.Draw(10,24);Reject(()=>Make(last,"extra"),"sixteen screens globally");int total=0;foreach(var scene in ((IDictionary)Field(f.Session,"_scenes")).Values)total+=((IDictionary)Field(scene,"Screens")).Count;Check(total==16,"global screen rejection rolls back declaration");
  }
 }
 static void ProjectionAndFaultBoundary()
 {
  using var f=new Fixture();var h=f.Draw();Make(h,"a");var d=f.Data("a");d.Width=4;d.Height=2;d.CanvasWidth=d.CanvasHeight=2;d.Pose=(double[])Static("MatrixValues",MatrixD.CreateRotationY(.4)*MatrixD.CreateTranslation(1,2,3));
  var pose=(MatrixD)Static("ReadMatrix",d.Pose);var canvas=(MatrixD)Static("ProjectedCanvas",d,0d,2d,2d);
  Check(Vector3D.Distance(Vector3D.Transform(new Vector3D(1,1,900),canvas),pose.Translation+pose.Right+pose.Up)<1e-9,"contain policy letterboxes and discards canvas Z");
  d.Aspect="stretch";canvas=(MatrixD)Static("ProjectedCanvas",d,0d,2d,2d);Check(Vector3D.Distance(Vector3D.Transform(new Vector3D(1,1,0),canvas),pose.Translation+pose.Right*2+pose.Up)<1e-9,"stretch policy uses physical dimensions");
  d.Aspect="cover";canvas=(MatrixD)Static("ProjectedCanvas",d,0d,2d,2d);Check(Vector3D.Distance(Vector3D.Transform(new Vector3D(1,1,0),canvas),pose.Translation+pose.Right*2+pose.Up*2)<1e-9,"cover policy enlarges uniformly for clipping");
  f.World=MatrixD.CreateRotationX(.7)*MatrixD.CreateTranslation(1000,2000,3000);var point=new Vector3D(.2,.1,0);Check(Vector3D.Distance(Vector3D.Transform(point,canvas*f.World),Vector3D.Transform(Vector3D.Transform(point,canvas),f.World))<1e-8,"screen content follows current anchor pose exactly once");
  Set(f.Scene(),"VolumeRange",0d);var volume=(DisplayVolume)Call(f.Session,"ProjectedVolume",f.Scene(),d,f.Registry[20]);var center=Vector3D.Transform(pose.Translation,f.World);var right=Vector3D.TransformNormal(pose.Right,f.World);Check(volume.Contains(center)&&!volume.Contains(center+right*2.01),"screen volume clips outside its physical rectangle on moving anchor");
  d.Pose=(double[])Static("MatrixValues",MatrixD.Identity);d.Width=d.Height=2;f.World=MatrixD.Identity;Set(f.Scene(),"Offset",Vector3D.Zero);Set(f.Scene(),"VolumeRange",.5d);volume=(DisplayVolume)Call(f.Session,"ProjectedVolume",f.Scene(),d,f.Registry[20]);Check(volume.Contains(Vector3D.Zero)&&!volume.Contains(Vector3D.UnitX*.8),"parent display volume clips projected screen too");
  Make(h,"b");f.WorldReads=0;Call(f.Session,"DrawProjectedScreens",f.Scene(),f.Registry[20],0,1);Check(f.WorldReads==0&&((IDictionary)Field(f.Session,"_projectedCaches")).Count==0,"exhausted draw budget performs no screen work or GPU access");
  int notices=0;f.Gateway.Install("Utilities",(m,a)=>{if(m.Name=="ShowMessage"){notices++;throw new Exception("diagnostics unavailable");}throw new Exception(m.Name);});f.FailWorld=true;
  Call(f.Session,"DrawProjectedScreens",f.Scene(),f.Registry[20],20,1);Check(f.WorldReads==2&&((IDictionary)Field(f.Session,"_projectedCaches")).Count==2&&notices==2,"one screen fault does not prevent visiting sibling screen and diagnostic errors stay contained");
  Call(f.Session,"DrawProjectedScreens",f.Scene(),f.Registry[20],20,1);Check(notices==2,"repeated screen failures do not spam notifications each draw");
  f.FailWorld=false;f.FailRender=true;Vector3D viewer=new Vector3D(0,0,-2);
  var cameraType=typeof(MyAPIGateway).GetProperty("Session").PropertyType.GetProperty("Camera").PropertyType;
  var camera=DrawTestProxy.Make(cameraType,(m,a)=>m.Name=="get_Position"?viewer:throw new Exception(m.Name));
  f.Gateway.Install("Session",(m,a)=>m.Name=="get_Camera"?camera:throw new Exception(m.Name));
  Call(f.Session,"DrawProjectedScreens",f.Scene(),f.Registry[20],20,1);Check(f.RenderReads==0,"back-facing screens cull before any render/GPU access");
  viewer=new Vector3D(0,0,2);Call(f.Session,"DrawProjectedScreens",f.Scene(),f.Registry[20],20,1);Check(f.RenderReads==2,"front-facing screens proceed and independent render faults remain contained");
 }
 static void DemoRasterBudget()
 {
  // The actual example's four boxes, including its broad floor, exercise the
  // integration's 200,000-work allowance rather than the rasterizer's default.
  var points=new List<Vector3D>();var triangles=new List<int>();var colors=new List<Vector4>();
  void Box(Vector3D center,Vector3D size,Vector4 color){int start=points.Count;for(int i=0;i<8;i++)points.Add(center+new Vector3D((i&1)==0?-size.X/2:size.X/2,(i&2)==0?-size.Y/2:size.Y/2,(i&4)==0?-size.Z/2:size.Z/2));int[] faces={0,2,3,0,3,1,4,5,7,4,7,6,0,4,6,0,6,2,1,3,7,1,7,5,0,1,5,0,5,4,2,6,7,2,7,3};for(int i=0;i<faces.Length;i+=3){triangles.Add(start+faces[i]);triangles.Add(start+faces[i+1]);triangles.Add(start+faces[i+2]);float shade=.6f+.08f*(i/6);colors.Add(new Vector4(color.X*shade,color.Y*shade,color.Z*shade,1));}}
  Box(new Vector3D(-.65,-.25,0),new Vector3D(.8,1.3,.9),new Vector4(.05f,.7f,.8f,1));Box(new Vector3D(.65,-.45,.7),new Vector3D(.9),new Vector4(.55f,.18f,.7f,1));Box(new Vector3D(0,0,-.85),new Vector3D(.14,2,.1),new Vector4(.95f,.4f,.05f,1));Box(new Vector3D(0,-1.05,0),new Vector3D(5,.1,5),new Vector4(.08f,.14f,.19f,1));
  var source=new Geometry(points.ToArray(),Array.Empty<int>(),triangles.ToArray());var paint=colors.ToArray();var renderer=new PerspectiveView();
  foreach(int resolution in new[]{64,128})for(int frame=0;frame<24;frame++)
  {
   var camera=new PerspectiveCamera{Eye=Vector3D.TransformNormal(new Vector3D(3,2,-5),MatrixD.CreateRotationY(frame*Math.PI/12)),Target=Vector3D.Zero,Up=Vector3D.Up,VerticalFovRadians=Math.PI/3,Near=.05,Far=30,Width=resolution,Height=resolution*9/16};
   var mesh=renderer.Render(source,paint,camera,2.4,1.35,200000);MeshEffects.ValidateAttributes(mesh);
   Check(mesh.Geometry.Points.Length<=Geometry.MaxPoints&&mesh.Geometry.Triangles.Length>0,"actual demo floor/source fits admitted work and output budget at "+resolution+" orbit sample "+frame);
  }
 }
 static object CaptureCache(Fixture f,string id)
 {
  var cache=Activator.CreateInstance(typeof(HoloMapSession).GetNestedType("ProjectedCache",BindingFlags.NonPublic),true);
  Set(cache,"Anchor",20L);Set(cache,"Caller",10L);Set(cache,"Id",id);Set(cache,"Front",true);Set(cache,"Seen",0);
  ((IDictionary)Field(f.Session,"_projectedCaches")).Add("20:10:"+id,cache);return cache;
 }
 static void Source(Func<string,object[],object> h)
 {h("screen-scene",new object[]{new[]{new Vector3D(-1,-1,0),new Vector3D(1,-1,0),new Vector3D(0,1,0)},new[]{0,1,2},new[]{Vector4.One}});}
 static void CacheIdentityAndClocks()
 {
  using var f=new Fixture();var h=f.Draw();Make(h,"a");Source(h);h("sprites",new object[]{new[]{MySprite.CreateSprite("SquareSimple",new Vector2(100),new Vector2(40))}});var original=f.Data("a");
  HoloProjectedScreenData Clone()=>(HoloProjectedScreenData)Static("CloneScreen",original,true);
  bool SameView(HoloProjectedScreenData d)=>(bool)Static("SameProjectedView",original,d);
  bool SameSprites(HoloProjectedScreenData d)=>(bool)Static("SameProjectedSprites",original,d);
  var clone=Clone();Check(!ReferenceEquals(original.SourcePoints,clone.SourcePoints)&&!ReferenceEquals(original.Camera,clone.Camera)&&!ReferenceEquals(original.Sprites,clone.Sprites)&&!ReferenceEquals(original.Sprites[0].Position,clone.Sprites[0].Position),"network-style deep clone owns all source buffers");
  Check(SameView(clone)&&SameSprites(clone),"equal deep-cloned source buffers preserve cached capture identity");
  clone.Pose[12]+=.2;clone.Width+=.1;clone.Height+=.1;clone.Background[0]=.3f;clone.Opacity=.4f;clone.RefreshHz=30;clone.Aspect="cover";clone.View[0]+=.5;
  Check(SameView(clone)&&SameSprites(clone),"layout-only replicated changes do not force raster or sprite recapture");
  clone=Clone();clone.Camera[0]+=.1;Check(!SameView(clone),"camera change invalidates perspective capture");
  clone=Clone();clone.SourcePoints[0]+=.1;Check(!SameView(clone),"source vertex change invalidates perspective capture");
  clone=Clone();clone.SourceTriangles[0]=1;clone.SourceTriangles[1]=0;Check(!SameView(clone),"source topology change invalidates perspective capture");
  clone=Clone();clone.SourceColors[0]=.25f;Check(!SameView(clone),"source paint change invalidates perspective capture");
  clone=Clone();clone.Sprites[0].Position[0]+=1;Check(!SameSprites(clone),"sprite position change invalidates composition");
  clone=Clone();clone.Sprites[0].Data="Circle";Check(!SameSprites(clone),"sprite primitive change invalidates composition");
  h("screen-sprites-clear",Array.Empty<object>());h("screen-refresh",new object[]{60d});h("screen-orbit",new object[]{.2});Set(f.Session,"ClientLcdRefreshCap",1d);var cache=CaptureCache(f,"a");
  Call(f.Session,"PrepareProjectedSources",f.Scene(),f.Screen("a"),cache,1);var first=Field(cache,"View");Check(first!=null&&(int)Field(f.Session,"_lastCompileTick")==0,"first source capture initializes once");
  Set(f.Session,"_ticks",1);h("screen-camera",new object[]{new Vector3D(1,0,-4),Vector3D.Zero,Vector3D.Up});h("screen-opacity",new object[]{.9});Call(f.Session,"PrepareProjectedSources",f.Scene(),f.Screen("a"),cache,1);
  Check(ReferenceEquals(first,Field(cache,"View"))&&(int)Field(f.Session,"_lastCompileTick")==0,"source and metadata churn cannot bypass viewer-local capture cap");
  Set(f.Session,"_ticks",60);Call(f.Session,"PrepareProjectedSources",f.Scene(),f.Screen("a"),cache,1);Check(Field(cache,"View")!=null&&!ReferenceEquals(first,Field(cache,"View")),"pending source change captures after the shared deadline");
 }
 static void CaptureFairness()
 {
  using var f=new Fixture();var h=f.Draw();Make(h,"orbit");Source(h);h("screen-refresh",new object[]{60d});h("screen-orbit",new object[]{.2});
  Make(h,"menu");h("sprites",new object[]{new[]{MySprite.CreateSprite("SquareSimple",new Vector2(256),new Vector2(100))}});
  var orbit=CaptureCache(f,"orbit");var menu=CaptureCache(f,"menu");
  Call(f.Session,"PrepareProjectedSources",f.Scene(),f.Screen("orbit"),orbit,1);var first=Field(orbit,"View");Check(first!=null,"first orbital job compiles");
  Call(f.Session,"PrepareProjectedSources",f.Scene(),f.Screen("menu"),menu,1);Check(Field(menu,"Sprites")==null,"only one heavy projected job runs in a simulation tick");
  Set(f.Session,"_ticks",1);Set(orbit,"Seen",1);Set(menu,"Seen",1);Call(f.Session,"PrepareProjectedSources",f.Scene(),f.Screen("orbit"),orbit,1);Check(ReferenceEquals(first,Field(orbit,"View")),"60Hz first screen yields to a pending visible sibling");
  Call(f.Session,"PrepareProjectedSources",f.Scene(),f.Screen("menu"),menu,1);Check(Field(menu,"Sprites")!=null&&(string)Field(f.Session,"_lastProjectedJob")=="20:10:menu","new sibling sprite frame makes progress despite continuous orbit");
  Set(f.Session,"_ticks",2);Set(orbit,"Seen",2);Set(menu,"Seen",2);Call(f.Session,"PrepareProjectedSources",f.Scene(),f.Screen("orbit"),orbit,1);Check(!ReferenceEquals(first,Field(orbit,"View")),"orbital capture resumes after sibling service");
  h("sprites",new object[]{new[]{MySprite.CreateSprite("Circle",new Vector2(256),new Vector2(100))}});Set(menu,"NextCapture",-1d);Set(menu,"Front",false);
  Check(!(bool)Call(f.Session,"OtherProjectedJobPending",orbit,2d/60),"back-facing pending screen cannot block a visible job");
  Set(menu,"Front",true);Set(menu,"Seen",-10);Check(!(bool)Call(f.Session,"OtherProjectedJobPending",orbit,2d/60),"stale unseen pending screen cannot block a visible job");
 }
}
