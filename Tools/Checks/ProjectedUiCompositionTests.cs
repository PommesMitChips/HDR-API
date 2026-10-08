using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HoloMap;
using HDR.Interactions;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

// Focused composition authority checks. The existing native fixture supplies
// registered entities, head matrices, protobuf transport and bounded physics;
// production UI/Draw endpoints, hit inverses and transaction ticks run unchanged.
internal static class ProjectedUiCompositionTests
{
 static int checks;
 static void Check(bool ok,string label){if(!ok)throw new Exception("Projected UI composition: "+label);checks++;}
 static void Reject(Action action,string label){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Projected UI expected rejection: "+label);}
 static object Call(object target,string name,params object[] args)
 {var method=target.GetType().GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length);try{return method.Invoke(target,args);}catch(TargetInvocationException e){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
 static object Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(o);
 static void Set(object o,string name,object value)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(o,value);
 sealed class Fixture:IDisposable
 {
  readonly object native;
  public readonly HoloMapSession Session;
  public readonly Func<string,object[],object> Draw,Ui;
  public readonly int Shape;
  public Fixture(int shape=0)
  {
   Shape=shape;native=Activator.CreateInstance(typeof(NativeUiDragNetworkTests).GetNestedType("Fixture",BindingFlags.NonPublic),true);Session=(HoloMapSession)Field(native,"Session");Draw=(Func<string,object[],object>)Field(native,"Draw");Ui=(Func<string,object[],object>)Field(native,"Ui");
   Ui("remove",new object[]{"slider"});Draw("screen",new object[]{"surface",MatrixD.Identity,2d,2d,2d,2d});
   if(shape==1||shape==2)Draw("screen-surface",new object[]{shape==1?"cylinder":"sphere",2d,2d,1d,"inside"});
   if(shape==3)Draw("screen-ellipsoid",new object[]{new Vector3D(2,1.5,2),2d,1d,"inside"});
   if(shape==4)Draw("screen-mesh",new object[]{new[]{new Vector3D(-1,-1,2),new Vector3D(1,-1,2),new Vector3D(1,1,2),new Vector3D(-1,1,2)},new[]{0,1,2,0,2,3},new[]{new Vector2(0,1),new Vector2(1,1),new Vector2(1,0),new Vector2(0,0)},"outside"});
   Draw("line",new object[]{"handle",Vector3D.Zero,new Vector3D(.25,0,0),"cyan"});Ui("screen",new object[]{"surface"});Ui("control",new object[]{"slider","main","handle",.125,0d,.25,.25});Ui("bind-value",new object[]{"slider","gain"});Ui("constraint",new object[]{"slider","line",Vector3D.Zero,Vector3D.UnitX});Ui("draggable",new object[]{"slider",true});Aim(.125);
  }
  public object Scene=>Call(native,"get_Scene");
  public UiDisplay Display=>(UiDisplay)Call(native,"get_Display");
  public UiWidget Widget=>Display.Widgets.Single(w=>w.Id=="slider");
  public HoloProjectedScreenData Screen=>(HoloProjectedScreenData)Field(((IDictionary)Field(Scene,"Screens"))["10:surface"],"Data");
  public MyTuple<double,long> Value=>(MyTuple<double,long>)Ui("get-value",new object[]{"gain"});
  public UiDragAck Ack=>(UiDragAck)Call(native,"get_Ack");
  public UiDragTransactions Transactions=>(UiDragTransactions)Call(native,"get_Transactions");
  public Vector3D Point(double x,double y=0)
  {
   var s=Screen;if(Shape==0)return new Vector3D(x,y,0);if(Shape==4)return new Vector3D(x,y,2);
   var style=new SurfaceStyle{Kind=(SurfaceKind)s.SurfaceKind,Mapping=(SurfaceMappingMode)s.SurfaceMapping,Radius=s.SurfaceRadius,Radii=s.SurfaceRadii==null?new Vector3D(s.SurfaceRadius):new Vector3D(s.SurfaceRadii[0],s.SurfaceRadii[1],s.SurfaceRadii[2]),HorizontalRadians=s.SurfaceHorizontal,VerticalRadians=s.SurfaceVertical,Inward=s.SurfaceSide==0,VerticalFovRadians=s.Camera[9],SourceAspect=(double)s.UiRasterWidth/s.UiRasterHeight};
   var p=SurfaceMapping.MapPoint(new Vector3D(x,y,0),s.CanvasWidth,s.CanvasHeight,style);if(s.SurfaceKind==1)p.Y*=s.Height/s.CanvasHeight;if(s.SurfaceSide==0)p.X=-p.X;return p;
  }
  public Vector3D Origin=>Shape==0?new Vector3D(.125,0,1):Shape==4?new Vector3D(.125,0,3):Vector3D.Zero;
  public void Aim(double x,double y=0){var origin=Origin;Set(native,"Head",origin);Set(native,"Position",origin);Set(native,"Direction",Vector3D.Normalize(Point(x,y)-origin));}
  public UiDragRequest Request(UiDragKind kind,long sequence,UiDragAck grant=null,double x=.125,double value=2)
  {
   var origin=Origin;var direction=Vector3D.Normalize(Point(x)-origin);return new UiDragRequest{Kind=(int)kind,CallerId=10,TargetId=20,ControlId="slider",DefinitionRevision=Display.Revision,ValueRevision=kind==UiDragKind.Focus?Widget.Control.SourceRevision:grant==null?Value.Item2:grant.ValueRevision,RequestId=grant==null?sequence:grant.RequestId,Sequence=sequence,Mode=1,Value=value,LeaseId=grant==null?0:grant.LeaseId,ViewerId=grant==null?0:grant.ViewerId,ScreenId="surface",SurfaceGeneration=Screen.UiSurfaceGeneration,OriginX=origin.X,OriginY=origin.Y,OriginZ=origin.Z,DirectionX=direction.X,DirectionY=direction.Y,DirectionZ=direction.Z};
  }
  public UiDragAck Send(UiDragRequest request,int tick){Call(native,"Receive",request,200UL,false,(ushort)49784);Call(native,"Tick",tick);return Ack;}
  public bool Hit(Vector3D origin,Vector3D direction,out Vector3D hit)
  {var args=new object[]{Display,Widget,origin,direction,Vector3D.Zero,0L};bool found=(bool)Call(Session,"TryUiControlHit",args);hit=(Vector3D)args[4];return found&&(long)args[5]==20;}
  public void Change(string name,object value)=>Set(native,name,value);
  public void Dispose()=>((IDisposable)native).Dispose();
 }
 public static int Run()
 {
  checks=0;PureInverses();foreach(int shape in Enumerable.Range(0,5)){AuthoritativeDrag(shape);ViewerStamp(shape);}PermissionAndReplay();ArtworkAndCrop();NativeInputPipeline();LegacyUnchanged();return checks;
 }
 static void PureInverses()
 {
  foreach(var kind in new[]{SurfaceKind.Plane,SurfaceKind.Cylinder,SurfaceKind.Sphere,SurfaceKind.Ellipsoid})foreach(var mapping in new[]{SurfaceMappingMode.Angular,SurfaceMappingMode.Geodesic,SurfaceMappingMode.Pinhole})
  {
   if(kind==SurfaceKind.Ellipsoid&&mapping==SurfaceMappingMode.Geodesic)continue;
   var style=new SurfaceStyle{Kind=kind,Mapping=mapping,Radius=2,Radii=new Vector3D(2,1.5,2),HorizontalRadians=1.3,VerticalRadians=1,Inward=true,VerticalFovRadians=1.1,SourceAspect=1.8};Vector3D point;
   try{point=SurfaceMapping.MapPoint(new Vector3D(.21,-.13,0),2,1.2,style);}catch(ArgumentException){continue;}
   var origin=kind==SurfaceKind.Plane?point+Vector3D.UnitZ:Vector3D.Zero;var direction=kind==SurfaceKind.Plane?-Vector3D.UnitZ:Vector3D.Normalize(point);
   Check(UiSurfaceHit.TryHit(origin,direction,style,2,1.2,5,1,0,out var hit,out var uv)&&Vector3D.DistanceSquared(point,hit)<1e-14&&Vector2.Distance(uv,new Vector2(.605f,.60833335f))<1e-6,"inverse round trip "+kind+"/"+mapping);
  }
  var mesh=new SurfaceStyle{Kind=SurfaceKind.Mesh,Inward=false,MeshPoints=new[]{new Vector3D(-1,-1,0),new Vector3D(1,-1,0),new Vector3D(0,1,0),new Vector3D(-1,-1,1),new Vector3D(0,1,1),new Vector3D(1,-1,1)},MeshTriangles=new[]{0,1,2,3,4,5},MeshUV=new[]{Vector2.Zero,Vector2.UnitX,Vector2.UnitY,Vector2.Zero,Vector2.UnitY,Vector2.UnitX}};
  Check(UiSurfaceHit.TryHit(new Vector3D(0,0,2),-Vector3D.UnitZ,mesh,2,2,4,1,0,out var selected,out _)&&Math.Abs(selected.Z)<1e-12,"mesh chooses nearest enabled side rather than opaque disabled back face");
  Check(UiSurfaceHit.TryHit(new Vector3D(0,0,2),-Vector3D.UnitZ,mesh,2,2,4,1,1,out selected,out _,p=>p.Z<.5)&&Math.Abs(selected.Z)<1e-12,"mesh chooses nearest surviving clip candidate when a closer enabled face is clipped");
  Check(!UiSurfaceHit.TryHit(Vector3D.Zero,Vector3D.Zero,new SurfaceStyle(),4,out _,out _),"zero rays fail closed");
 }
 static void AuthoritativeDrag(int shape)
 {
  using var f=new Fixture(shape);Check(f.Widget.ScreenId=="surface"&&f.Widget.Control.Artwork=="!s!surface!handle","exact Draw artwork parent is retained for shape "+shape);
  var point=f.Point(.125);Check(f.Hit(f.Origin,Vector3D.Normalize(point-f.Origin),out var hit)&&Vector3D.Distance(point,hit)<1e-5,"actual shape hotzone hit "+shape);
  var args=new object[]{f.Display,f.Widget.Control,20L,f.Origin,Vector3D.Normalize(point-f.Origin),default(LocalRay)};Check((bool)Call(f.Session,"UiDragLocalRay",args),"shape ray becomes artwork orthographic drag ray "+shape);
  var grant=f.Send(f.Request(UiDragKind.Begin,1),0);Check(grant.Status==0&&grant.ScreenId=="surface"&&grant.SurfaceGeneration==f.Screen.UiSurfaceGeneration,"secure receiver grants projected lease "+shape);
  var move=f.Request(UiDragKind.Move,2,grant,.625,9);var changed=f.Send(move,6);Check(changed.Status==0&&f.Value.Item1==7,"server derives projected value from ray and ignores claimed scalar "+shape);
  var before=f.Value;f.Send(move,12);Check(f.Value.Item1==before.Item1&&f.Value.Item2==before.Item2,"replayed projected move cannot recommit "+shape);
  var generation=f.Screen.UiSurfaceGeneration;f.Draw("screen-camera",new object[]{new Vector3D(0,0,-5),Vector3D.Zero,Vector3D.Up});Check(f.Screen.UiSurfaceGeneration==generation&&f.Transactions.ActiveLeases==1,"source camera frame preserves projected lease "+shape);
  var end=f.Request(UiDragKind.End,3,changed,.625,0);var terminal=f.Send(end,18);Check(terminal.Status==0&&terminal.Terminal&&f.Value.Item1==7,"end recomputes projected ray value "+shape);
 }
 static void ViewerStamp(int shape)
 {
  using var f=new Fixture(shape);var focus=f.Send(f.Request(UiDragKind.Focus,1),0);Check(focus.Status==0&&focus.ViewerId>0,"physical native head proof grants persistent projected viewer "+shape);
  var generation=f.Screen.UiSurfaceGeneration;f.Draw("screen-source",new object[]{"lcd","55"});Check(f.Screen.UiSurfaceGeneration==generation,"live source switch does not alter projected coordinates "+shape);
  var heartbeat=f.Request(UiDragKind.KeepAlive,2,focus);heartbeat.LeaseId=heartbeat.ViewerId;heartbeat.ValueRevision=focus.ValueRevision;Check(f.Send(heartbeat,60).Status==0,"viewer survives source switch "+shape);
  f.Draw("screen-two-sided",new object[]{true,1d,0d});Check(f.Screen.UiSurfaceGeneration>generation,"visible side mutation advances monotonic generation "+shape);Call(f.Session,"TickUiDragViewers");Check(((IDictionary)Field(f.Session,"_uiDragViewers")).Count==0,"surface coordinate/visibility mutation revokes viewer "+shape);
 }
 static void PermissionAndReplay()
 {
  foreach(string field in new[]{"Access","Working","SameConstruct","OnFoot","Connected"})
  {using var f=new Fixture(3);var grant=f.Send(f.Request(UiDragKind.Begin,1),0);Check(grant.Status==0,"fixture grants before lifecycle loss "+field);f.Change(field,false);Call(f.Session,"TickUiNetwork");Check(f.Transactions.ActiveLeases==0,"permission/life invalidity cancels projected gesture "+field);}
  using(var f=new Fixture(3)){var bad=f.Request(UiDragKind.Begin,1);bad.OriginX=10;Check(f.Send(bad,0).Status!=0&&f.Value.Item1==2,"ray origin away from current head is rejected");}
  using(var f=new Fixture(3)){var bad=f.Request(UiDragKind.Begin,1);bad.SurfaceGeneration--;Check(f.Send(bad,0).Status!=0,"stale projected generation is rejected before acquiring value lock");}
  using(var f=new Fixture(3)){var focus=f.Send(f.Request(UiDragKind.Focus,1),0);long generation=f.Screen.UiSurfaceGeneration;f.Draw("screen-remove",Array.Empty<object>());Check(((IDictionary)Field(f.Session,"_uiDragViewers")).Count==0,"removal closes viewer immediately");f.Draw("screen",new object[]{"surface",MatrixD.Identity,2d,2d,2d,2d});Check(f.Screen.UiSurfaceGeneration>generation,"same screen ID recreation cannot replay retired generation");}
  using(var f=new Fixture(3)){f.Change("Occluded",true);Check(f.Send(f.Request(UiDragKind.Begin,1),0).Status!=0,"current occlusion blocks a projected pointer grant");}
 }
 static void ArtworkAndCrop()
 {
  using var f=new Fixture(1);var point=f.Point(.125);f.Draw("screen-clip",new object[]{new[]{new Vector4(1,0,0,-1)}});Check(!f.Hit(f.Origin,Vector3D.Normalize(point-f.Origin),out _),"screen crop clips control hit using display-anchor planes");f.Draw("screen-clip",Array.Empty<object>());
  f.Draw("visible",new object[]{"handle",false});Check(!f.Hit(f.Origin,Vector3D.Normalize(point-f.Origin),out _),"hidden control artwork cannot acquire a projected input");f.Draw("visible",new object[]{"handle",true});
  f.Ui("screen",new object[]{""});Reject(()=>f.Ui("control",new object[]{"wrong","main","!s!surface!handle",0d,0d,.1,.1}),"unselected projected write cannot bypass Core reserved-ID authorization");
  f.Ui("screen",new object[]{"surface"});f.Draw("line",new object[]{"raised",Vector3D.Zero,new Vector3D(.2,0,.2),"cyan"});Reject(()=>f.Ui("control",new object[]{"raised","main","raised",0d,0d,.2,.2}),"nonplanar source artwork cannot bind a projected control");
  f.Ui("button",new object[]{"button","main",-.5,0d,.3,.3,"Run","pb","fixed"});var button=f.Display.Widgets.Single(w=>w.Id=="button");Check(button.ScreenId=="surface"&&((IDictionary)Field(f.Scene,"Items")).Contains("10:!s!surface!ui_main"),"generated buttons occupy their exact projected Draw namespace");
 }
 static void LegacyUnchanged()
 {using var native=(IDisposable)Activator.CreateInstance(typeof(NativeUiDragNetworkTests).GetNestedType("Fixture",BindingFlags.NonPublic),true);var grant=(UiDragAck)Call(native,"Grant");Check(grant.Status==0&&grant.ScreenId==null&&grant.SurfaceGeneration==0,"legacy anchor drag grant and existing wire semantics remain unchanged");}
 static void NativeInputPipeline()
 {
  using var fixture=(IDisposable)Activator.CreateInstance(typeof(NativeUiDragInputTests).GetNestedType("Fixture",BindingFlags.NonPublic),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{true,false,false},null);
  var session=(HoloMapSession)Field(fixture,"Session");var registry=(Dictionary<long,object>)Field(fixture,"Registry");var ui=(Func<string,object[],object>)Field(fixture,"Ui");var provider=Field(fixture,"Provider");
  Set(fixture,"AsClient",false);var draw=(Func<string,object[],object>)Call(session,"DrawEndpoint",registry[10]);ui("remove",new object[]{"slider"});draw("screen",new object[]{"native",MatrixD.CreateTranslation(0,0,-2),2d,2d,2d,2d});draw("screen-ellipsoid",new object[]{new Vector3D(2,1.5,2),2d,1d,"outside"});draw("line",new object[]{"handle",Vector3D.Zero,new Vector3D(.5,0,0),"cyan"});ui("screen",new object[]{"native"});ui("control",new object[]{"slider","main","handle",.125,0d,.5,.25});ui("bind-value",new object[]{"slider","gain"});ui("constraint",new object[]{"slider","line",Vector3D.Zero,Vector3D.UnitX});ui("draggable",new object[]{"slider",true});Set(fixture,"AsClient",true);
  Check((bool)Call(fixture,"Use","slider"),"actual native Use enters projected persistent viewer with existing pointer ABI");Call(fixture,"RoundTrip",0);
  Check((bool)Field(session,"_uiViewerGranted")&&(long)Field(session,"_uiViewerId")>0,"projected native viewer receives authenticated serialized focus grant");
  Call(provider,"Down");Call(fixture,"Tick",1);Call(fixture,"RoundTrip",1);var local=(UiDragLocalLease)Call(fixture,"get_Local");
  Check(local.Active&&local.Held&&(bool)Field(session,"_uiDragGrabbed"),"applied native down plus projected authority opens drag state");
  var packets=(UiDragRequest[])Call(fixture,"get_Requests");var begin=packets.Last(p=>p.Kind==(int)UiDragKind.Begin);Check(begin.ScreenId=="native"&&new Vector3D(begin.DirectionX,begin.DirectionY,begin.DirectionZ).Length()>.999,"normalized native cursor generates finite projected world ray in appended wire evidence");
  Call(provider,"Move",.6);Call(fixture,"Tick",8);Call(fixture,"RoundTrip",8);var numeric=(UiNumericValue)Call(fixture,"get_Value");Check(numeric.Value>2&&local.Confirmed==numeric.Value,"native held cursor motion commits the server-derived curved-surface value");
  Call(provider,"Up");Call(fixture,"Tick",9);Call(fixture,"RoundTrip",9);Check(!local.Pending&&(bool)Field(session,"_uiViewerGranted")&&(long)Call((PointerInputBridge)Call(fixture,"get_Bridge"),"get_Token")==700,"projected gesture release preserves the exact native viewer token");
 }
}
