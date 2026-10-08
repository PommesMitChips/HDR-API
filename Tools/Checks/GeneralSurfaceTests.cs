using System;
using System.Collections;
using System.Reflection;
using HoloMap;
using VRageMath;
using Sandbox.ModAPI;
internal static class GeneralSurfaceTests
{
 static int checks;
 static void Check(bool ok,string why){if(!ok)throw new Exception("General surfaces: "+why);checks++;}
 static void Reject(Action action,string why){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Expected surface rejection: "+why);}
 static object Static(string name,params object[] args){try{return typeof(HoloMapSession).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
 static readonly double[] Identity={1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
 static HoloProjectedScreenData Data()=>new HoloProjectedScreenData{CallerId=10,Id="view",Pose=(double[])Identity.Clone(),CanvasWidth=2,CanvasHeight=2,Width=2,Height=2};
 static SurfaceStyle Ellipse()=>new SurfaceStyle{Kind=SurfaceKind.Ellipsoid,Radii=new Vector3D(2,1,3),Inward=false,HorizontalRadians=Math.PI*2,VerticalRadians=Math.PI};
 static SurfaceStyle Mesh()=>new SurfaceStyle{Kind=SurfaceKind.Mesh,Inward=false,MeshPoints=new[]{new Vector3D(-1,-1,0),new Vector3D(1,-1,0),new Vector3D(1,1,.5),new Vector3D(-1,1,.5)},MeshTriangles=new[]{0,1,2,0,2,3},MeshUV=new[]{new Vector2(0,1),new Vector2(1,1),new Vector2(1,0),new Vector2(0,0)}};
 public static int Run()
 {
  checks=0;var ellipse=Ellipse();
  for(int y= -4;y<=4;y++)for(int x= -7;x<=7;x++)
  {
   var source=new Vector3D(x/8.0,y/8.0,0);var point=SurfaceMapping.MapPoint(source,2,2,ellipse);
   double equation=point.X*point.X/4+point.Y*point.Y+point.Z*point.Z/9;
   Check(Math.Abs(equation-1)<1e-9,"angular samples lie on independent-radii ellipsoid");
   double longitude=source.X/2*ellipse.HorizontalRadians,latitude=source.Y/2*ellipse.VerticalRadians;var ray=new Vector3D(Math.Cos(latitude)*Math.Sin(longitude),Math.Sin(latitude),Math.Cos(latitude)*Math.Cos(longitude));
   Check(Vector3D.Cross(Vector3D.Normalize(point),ray).Length()<1e-9,"angular panorama preserves viewing rays instead of scaled latitude distortion");
  }
  ellipse.Mapping=SurfaceMappingMode.Pinhole;ellipse.SourceAspect=1.7;ellipse.VerticalFovRadians=1.2;
  var basePoint=SurfaceMapping.MapPoint(new Vector3D(.6,.2,0),2,2,ellipse);var pinhole=new Vector3D(.6*Math.Tan(.6)*1.7,.2*Math.Tan(.6),1);
  Check(Vector3D.Cross(basePoint,pinhole).Length()<1e-9,"pinhole ellipsoid uses camera ray intersection");
  var normal=SurfaceMapping.Normal(new Vector3D(.6,.2,0),2,2,ellipse);var shifted=SurfaceMapping.MapPoint(new Vector3D(.6,.2,0),2,2,ellipse,.01);
  Check(Vector3D.Distance(shifted-basePoint,normal*.01)<1e-9,"ellipsoid layer offset follows implicit normal, not radial direction");
  Check(Vector3D.Cross(normal,Vector3D.Normalize(basePoint)).Length()>.05,"unequal radii expose the distinction between surface normal and radius");
  ellipse.Inward=true;var inner=SurfaceMapping.MapPoint(new Vector3D(.6,.2,0),2,2,ellipse,.01);Check(Vector3D.Distance(inner-basePoint,-normal*.01)<1e-9,"inside layers offset toward interior viewer");
  Reject(()=>SurfaceMapping.MapPoint(Vector3D.Zero,2,2,ellipse,2),"layer cannot cross ellipsoid shell");
  Vector3D hit;Vector2 uv;Check(UiSurfaceHit.TryHit(Vector3D.Zero,basePoint,ellipse,10,out hit,out uv)&&Vector3D.Distance(hit,basePoint)<1e-8&&Vector2.Distance(uv,new Vector2(.8f,.4f))<1e-6,"ellipsoid UV hit projection round-trips ray-preserving pinhole mapping");
  Check(!UiSurfaceHit.TryHit(Vector3D.Zero,basePoint,ellipse,.1,out hit,out uv),"UV hit obeys interaction range");
  foreach(var bad in new[]{new Vector3D(0,1,1),new Vector3D(1,double.NaN,1),new Vector3D(-1,1,1)}){var style=Ellipse();style.Radii=bad;Reject(()=>SurfaceMapping.ValidateStyle(style),"invalid radii rejected");}
  var geodesic=new SurfaceStyle{Kind=SurfaceKind.Sphere,Mapping=SurfaceMappingMode.Geodesic,Radius=2,HorizontalRadians=Math.PI,VerticalRadians=Math.PI/2,Inward=true};Check(UiSurfaceHit.TryHit(Vector3D.Zero,Vector3D.UnitZ,geodesic,10,out hit,out uv)&&Vector2.Distance(uv,new Vector2(.5f))<1e-6,"geodesic sphere hit maps its centre to the same UV as rendering");
  var mesh=Mesh();var mapped=SurfaceMapping.BuildQuad(2,2,Vector4.One,mesh);
  Check(mapped.Geometry.Triangles.Length==6&&mapped.Geometry.Points.Length==4,"authored background reuses indexed triangles and UV vertices");
  Check(mapped.UV[0]==mesh.MeshUV[0],"author UVs preserved on display background");
  var pointOnMesh=SurfaceMapping.MapPoint(Vector3D.Zero,2,2,mesh);Check(Vector3D.Distance(pointOnMesh,new Vector3D(0,0,.25))<1e-9,"canvas centre maps barycentrically onto tilted authored surface");
  var meshNormal=SurfaceMapping.Normal(Vector3D.Zero,2,2,mesh);var offset=SurfaceMapping.MapPoint(Vector3D.Zero,2,2,mesh,.01);Check(Vector3D.Distance(offset-pointOnMesh,meshNormal*.01)<1e-9,"authored layer uses face normal");
  Check(UiSurfaceHit.TryHit(new Vector3D(0,0,3),-Vector3D.UnitZ,mesh,10,out hit,out uv)&&Vector2.Distance(uv,new Vector2(.5f))<1e-6,"mesh hit returns authored interpolated UV");
  Check(!UiSurfaceHit.TryHit(new Vector3D(3,0,3),-Vector3D.UnitZ,mesh,10,out hit,out uv),"ray outside physical mesh misses instead of plane approximation");
  Check(!UiSurfaceHit.TryHit(new Vector3D(0,0,-3),Vector3D.UnitZ,mesh,10,out hit,out uv)&&UiSurfaceHit.TryHit(new Vector3D(0,0,-3),Vector3D.UnitZ,mesh,10,out hit,out uv,true),"authored hit respects optional back side");
  var canvas=new SurfaceMesh{Geometry=new Geometry(new[]{new Vector3D(-.5,-.5,0),new Vector3D(.5,-.5,0),new Vector3D(.5,.5,0),new Vector3D(-.5,.5,0)},new[]{0,1},new[]{0,1,2,0,2,3}),Colors=new[]{Vector4.One,Vector4.One},EdgeColors=new[]{Vector4.One},UV=new[]{new Vector2(.2f,.8f),new Vector2(.8f,.8f),new Vector2(.8f,.2f),new Vector2(.2f,.2f)}};
  var warped=SurfaceMapping.Warp(canvas,2,2,mesh);Check(warped.Geometry.Triangles.Length>0&&warped.Geometry.Edges.Length>0&&warped.UV!=null,"vector lines/fills and texture UV all map through authored UV triangles");
  foreach(var p in warped.Geometry.Points)Check(Math.Abs(p.Z-(p.Y+1)*.25)<1e-6,"warped content follows actual tilted mesh");
  var invalid=Mesh();invalid.MeshUV[2]=invalid.MeshUV[1];Reject(()=>SurfaceMapping.ValidateStyle(invalid),"degenerate UV triangles rejected");invalid=Mesh();invalid.MeshTriangles[0]=99;Reject(()=>SurfaceMapping.ValidateStyle(invalid),"invalid indices rejected");invalid=Mesh();invalid.MeshUV[0]=new Vector2(-1,0);Reject(()=>SurfaceMapping.ValidateStyle(invalid),"outside UV range rejected");
  var quadBudget=Mesh();quadBudget.MaxWork=1;Reject(()=>SurfaceMapping.BuildQuad(2,2,Vector4.One,quadBudget),"authored background honors work budget outside an existing compilation scope");quadBudget=Mesh();quadBudget.MaxPoints=3;Reject(()=>SurfaceMapping.BuildQuad(2,2,Vector4.One,quadBudget),"authored background honors reduced point budget");
  var tiny=Mesh();tiny.MaxWork=1;Reject(()=>SurfaceMapping.Warp(canvas,2,2,tiny),"piecewise clipping respects bounded work allowance");
  var d=Data();d.SurfaceKind=3;d.SurfaceRadii=new[]{2d,1d,3d};Static("ValidateScreenData",d);
  var clone=(HoloProjectedScreenData)Static("CloneScreen",d,true);d.SurfaceRadii[0]=4;Check(clone.SurfaceRadii[0]==2,"deep wire clone isolates radii");
  d=Data();d.SurfaceKind=4;d.SurfaceSide=1;d.SurfaceMeshPoints=mesh.MeshPoints.SelectMany(p=>new[]{p.X,p.Y,p.Z}).ToArray();d.SurfaceMeshTriangles=mesh.MeshTriangles;d.SurfaceMeshUV=mesh.MeshUV.SelectMany(p=>new[]{p.X,p.Y}).ToArray();Static("ValidateScreenData",d);
  clone=(HoloProjectedScreenData)Static("CloneScreen",d,true);d.SurfaceMeshPoints[0]= -.9;d.SurfaceMeshTriangles[0]=1;d.SurfaceMeshUV[0]=.1f;
  Check(clone.SurfaceMeshPoints[0]== -1&&clone.SurfaceMeshTriangles[0]==0&&clone.SurfaceMeshUV[0]==0,"deep wire clone isolates authored geometry and UV");
  var footprint=(SurfaceMesh)Static("ScreenFootprint",clone);Check(footprint.Geometry.Triangles.Length==6&&footprint.Geometry.Points.Any(p=>p.Z==.5),"density footprint uses actual custom triangle geometry");
  var changed=(HoloProjectedScreenData)Static("CloneScreen",clone,true);changed.SurfaceMeshPoints[2]=.1;Check(!(bool)Static("SameSurface",clone,changed),"mapped cache invalidates on custom geometry change");
  var ellipseData=Data();ellipseData.SurfaceKind=3;ellipseData.SurfaceRadii=new[]{2d,1d,3d};var changedEllipse=(HoloProjectedScreenData)Static("CloneScreen",ellipseData,true);changedEllipse.SurfaceRadii[0]=2.1;Check(!(bool)Static("SameSurface",ellipseData,changedEllipse),"mapped cache invalidates on radius change");
  var bounds=(SurfaceMesh)Static("ScreenFootprintBounds",ellipseData);Check(bounds.Geometry.Points.Any(p=>p.X>2)&&bounds.Geometry.Points.Any(p=>p.Y< -1)&&bounds.Geometry.Points.Any(p=>p.Z>3),"adaptive conservative bounds contain real ellipsoid extents");
  ellipseData.TwoSided=true;ellipseData.FrontOpacity=.7f;ellipseData.BackOpacity=.2f;ellipseData.SurfaceSide=1;
  object[] front={ellipseData,new Vector3D(0,0,3),new Vector3D(.1,0,3),new Vector3D(0,.1,3),new Vector3D(0,0,4),false};Check((float)Static("ScreenTriangleOpacity",front)==.7f&&(bool)front[5],"ellipsoid face uses front opacity from implicit normal");
  object[] back={ellipseData,new Vector3D(0,0,-3),new Vector3D(.1,0,-3),new Vector3D(0,.1,-3),new Vector3D(0,0,4),false};Check((float)Static("ScreenTriangleOpacity",back)==.2f&&!(bool)back[5],"opposite ellipsoid triangle uses independent back opacity");
  using(var wire=new ClientReplicationTests.GatewayScope())
  {
   clone.SourceCaptureProfile=1;var bytes=wire.Serialize(clone);var received=MyAPIGateway.Utilities.SerializeFromBinary<HoloProjectedScreenData>(bytes);Static("ValidateScreenData",received);
   Check(received.SurfaceKind==4&&received.SurfaceMeshPoints.SequenceEqual(clone.SurfaceMeshPoints)&&received.SurfaceMeshTriangles.SequenceEqual(clone.SurfaceMeshTriangles)&&received.SurfaceMeshUV.SequenceEqual(clone.SurfaceMeshUV)&&received.SourceCaptureProfile==1,"new mesh and capture profile fields round-trip through actual protobuf wire model");
   bytes=wire.Serialize(ellipseData);received=MyAPIGateway.Utilities.SerializeFromBinary<HoloProjectedScreenData>(bytes);Static("ValidateScreenData",received);Check(received.SurfaceRadii.SequenceEqual(ellipseData.SurfaceRadii),"independent ellipsoid radii round-trip through wire model");
  }
  var mismatch=(HoloProjectedScreenData)Static("CloneScreen",clone,true);mismatch.SurfaceKind=0;Reject(()=>Static("ValidateScreenData",mismatch),"shape-mismatched mesh payload rejected at receiver boundary");
  mismatch=(HoloProjectedScreenData)Static("CloneScreen",clone,true);mismatch.SurfaceMeshUV=null;Reject(()=>Static("ValidateScreenData",mismatch),"incomplete custom mesh payload rejected before installation");
  mismatch=(HoloProjectedScreenData)Static("CloneScreen",clone,true);mismatch.SurfaceMeshTriangles[0]=2000;Reject(()=>Static("ValidateScreenData",mismatch),"receiver rejects out-of-range custom mesh indices");
  Commands();return checks;
 }
 static void Commands()
 {
  using var gateway=new ClientReplicationTests.GatewayScope{Server=true};var session=new HoloMapSession();bool access=true;
  var caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>m.Name switch{"get_EntityId"=>10L,"get_OwnerId"=>77L,"get_Closed"=>false,"IsSameConstructAs"=>true,_=>throw new Exception(m.Name)});
  var anchor=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>m.Name switch{"get_EntityId"=>20L,"get_Closed"=>false,"get_IsWorking"=>true,"HasPlayerAccess"=>access,"get_BlockDefinition"=>Activator.CreateInstance(m.ReturnType),_=>throw new Exception(m.Name)});
  gateway.Install("Entities",(m,a)=>m.Name=="GetEntityById"?(long)a[0]==10?caller:anchor:null);
  var draw=(Func<string,object[],object>)ClientReplicationTests.Call(session,"DrawEndpoint",caller);draw("target",new[]{anchor});draw("screen",new object[]{"shape",MatrixD.Identity,2d,2d,2d,2d});
  HoloProjectedScreenData Current(){var scene=((IDictionary)ClientReplicationTests.Field(session,"_scenes"))[20L];var screen=((IDictionary)ClientReplicationTests.Field(scene,"Screens"))["10:shape"];return (HoloProjectedScreenData)ClientReplicationTests.Field(screen,"Data");}
  draw("screen-camera-quality",new object[]{"lite"});Check(Current().SourceCaptureProfile==1&&(string)draw("screen-camera-quality-settings",Array.Empty<object>())=="lite","short camera quality profile command and getter publish lite");Reject(()=>draw("screen-camera-quality",new object[]{"ultra"}),"unknown camera quality profile rejected");Check(Current().SourceCaptureProfile==1,"invalid camera quality command retains existing profile");
  draw("screen-ellipsoid",new object[]{new Vector3D(2,1,3)});Check(Current().SurfaceKind==3&&Current().SurfaceRadii.SequenceEqual(new[]{2d,1d,3d}),"short ellipsoid command publishes bounded shape descriptor");
  Reject(()=>draw("screen-ellipsoid",new object[]{new Vector3D(2,0,3)}),"command rejects zero axis before retained state changes");Check(Current().SurfaceRadii[1]==1,"invalid ellipsoid command leaves prior display intact");
  var mesh=Mesh();draw("screen-mesh",new object[]{mesh.MeshPoints,mesh.MeshTriangles,mesh.MeshUV});Check(Current().SurfaceKind==4&&Current().SurfaceSide==1&&Current().SurfaceRadii==null,"mesh command replaces prior shape and defaults to authored outside side");
  mesh.MeshPoints[0]=new Vector3D(9);mesh.MeshTriangles[0]=99;mesh.MeshUV[0]=new Vector2(.1f);Check(Current().SurfaceMeshPoints[0]== -1&&Current().SurfaceMeshTriangles[0]==0&&Current().SurfaceMeshUV[0]==0,"PB array mutation cannot alter retained screen geometry");
  access=false;Reject(()=>draw("screen-mesh",new object[]{Mesh().MeshPoints,Mesh().MeshTriangles,Mesh().MeshUV}),"shape changes recheck current display permissions");access=true;
  draw("screen-surface",new object[]{"plane"});Check(Current().SurfaceKind==0&&Current().SurfaceMeshPoints==null&&Current().SurfaceRadii==null,"legacy surface command clears new payload and stays compatible");
 }
}




