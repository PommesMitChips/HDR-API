using System;
using System.Linq;
using HoloMap;
using VRageMath;
internal static class CameraPanoramaTests
{
 static int checks;
 static void Check(bool ok,string message){if(!ok)throw new Exception("Camera panorama: "+message);checks++;}
 static SurfaceMesh Mesh(PanoramaLens lens)
 {
  var mesh=SurfaceMapping.BuildQuad(2,2,Vector4.One,new SurfaceStyle{Kind=SurfaceKind.Sphere,Mapping=SurfaceMappingMode.Pinhole,Radius=3,
   VerticalFovRadians=lens.VerticalFov,SourceAspect=lens.Aspect,Inward=lens.Inward,MaxError=.04});
  if(lens.Inward)for(int i=0;i<mesh.Geometry.Points.Length;i++)mesh.Geometry.Points[i].X=-mesh.Geometry.Points[i].X;
  return mesh;
 }
 static bool Covered(PanoramaLens lens,Vector3D ray)
 {var p=Vector3D.TransformNormal(ray,MatrixD.Invert(lens.Frame));double ty=Math.Tan(lens.VerticalFov/2);return p.Z>0&&Math.Abs(p.X)<p.Z*ty*lens.Aspect&&Math.Abs(p.Y)<p.Z*ty;}
 static bool Hit(SurfaceMesh mesh,PanoramaLens lens,Vector3D ray)
 {
  ray=Vector3D.TransformNormal(ray,MatrixD.Invert(lens.Frame));var g=mesh.Geometry;
  for(int i=0;i<g.Triangles.Length;i+=3)
  {
   var a=g.Points[g.Triangles[i]];var b=g.Points[g.Triangles[i+1]];var c=g.Points[g.Triangles[i+2]];var normal=Vector3D.Cross(b-a,c-a);
   double denom=Vector3D.Dot(ray,normal);if(Math.Abs(denom)<1e-12)continue;double t=Vector3D.Dot(a,normal)/denom;if(t<=0)continue;var p=ray*t;
   if(Vector3D.Dot(Vector3D.Cross(b-a,p-a),normal)>=-1e-9&&Vector3D.Dot(Vector3D.Cross(c-b,p-b),normal)>=-1e-9&&Vector3D.Dot(Vector3D.Cross(a-c,p-c),normal)>=-1e-9)return true;
  }
  return false;
 }
 public static int Run()
 {
  checks=0;
  // Use the exact quality/radius/FOV requested by the live camera demo, not only
  // the coarser fixtures below. Previously this expanded beyond 2048 points.
  var cube=new PanoramaLens{Id="cube",VerticalFov=Math.PI/2};
  var exact=SurfaceMapping.BuildQuad(2,2,Vector4.One,new SurfaceStyle{Kind=SurfaceKind.Sphere,Mapping=SurfaceMappingMode.Pinhole,Radius=3,VerticalFovRadians=Math.PI/2,SourceAspect=1,Inward=true,MaxError=.01});
  foreach(int i in Enumerable.Range(0,exact.Geometry.Points.Length))exact.Geometry.Points[i].X=-exact.Geometry.Points[i].X;
  var dense=CameraPanorama.Join(exact,cube,new[]{cube},32768,65536);
  Check(dense.Geometry.Triangles.Length==exact.Geometry.Triangles.Length&&dense.Geometry.Points.Length<=exact.Geometry.Points.Length+8,"live demo detail retains triangles and welds shared vertices");
  Check(dense.Geometry.Points.Length<dense.Geometry.Triangles.Length,"output remains indexed instead of triangle soup");
  var axes=new[]{Vector3D.UnitZ,Vector3D.UnitX,-Vector3D.UnitZ,-Vector3D.UnitX,Vector3D.UnitY,-Vector3D.UnitY};
  var cubeViews=axes.Select((axis,i)=>new PanoramaLens{Id="f"+i,VerticalFov=Math.PI/2,Frame=MatrixD.CreateWorld(Vector3D.Zero,-axis,i>=4?Vector3D.UnitZ:Vector3D.UnitY)}).ToArray();
  var cubeMeshes=cubeViews.Select(lens=>CameraPanorama.Join(exact,lens,cubeViews,32768,65536)).ToArray();
  Check(cubeMeshes.Sum(mesh=>mesh.Geometry.Points.Length)<32768&&cubeMeshes.Sum(mesh=>mesh.Geometry.Triangles.Length/3)<65536,"six joined full-detail camera faces fit the live scene budget");
  for(int i=0;i<6;i++)Check(Hit(cubeMeshes[i],cubeViews[i],axes[i]),"each joined cube face retains its central ray");
  try{CameraPanorama.Join(exact,cube,new[]{cube},16,16);throw new Exception("Ignored panorama budget");}catch(ArgumentException){checks++;}
  var views=new[]{new PanoramaLens{Id="a",VerticalFov=Math.PI/2},new PanoramaLens{Id="b",Frame=MatrixD.CreateRotationY(Math.PI/3),VerticalFov=Math.PI/2}};
  var joined=views.Select(v=>CameraPanorama.Join(Mesh(v),v,views)).ToArray();
  for(int y=-28;y<=28;y+=7)for(double angle=-41.3;angle<105;angle+=7.1)
  {
   var ray=Vector3D.Normalize(new Vector3D(Math.Sin(angle*Math.PI/180),Math.Tan(y*Math.PI/180),Math.Cos(angle*Math.PI/180)));
   int winner=-1;double best=-2;
   for(int i=0;i<views.Length;i++)if(Covered(views[i],ray)){double score=Vector3D.Dot(ray,views[i].Frame.Backward);if(score>best){best=score;winner=i;}}
   for(int i=0;i<views.Length;i++)Check(Hit(joined[i],views[i],ray)==(winner==i),"overlaps have one owner; uncovered directions have no image");
  }
  foreach(var mesh in joined)
  {
   Check(mesh.Geometry.Points.All(p=>Math.Abs(p.Length()-3)<1e-9),"all clipped vertices return to the common sphere");
   Check(mesh.UV.All(p=>p.X>=0&&p.X<=1&&p.Y>=0&&p.Y<=1),"joined UVs remain inside camera images");
  }
  var wide=new PanoramaLens{Id="a",VerticalFov=2*Math.PI/3};var narrow=new PanoramaLens{Id="b",Frame=MatrixD.CreateRotationY(70*Math.PI/180),VerticalFov=10*Math.PI/180};
  var retained=CameraPanorama.Join(Mesh(wide),wide,new[]{wide,narrow});
  Check(Hit(retained,wide,new Vector3D(Math.Sin(50*Math.PI/180),0,Math.Cos(50*Math.PI/180))),"closer but uncovered camera does not cut a valid image away");
  var duplicate=new PanoramaLens{Id="b",VerticalFov=views[0].VerticalFov};
  Check(CameraPanorama.Join(Mesh(duplicate),duplicate,new[]{views[0],duplicate}).Geometry.Triangles.Length==0,"identical camera views choose one deterministic owner");
  var rolled=new PanoramaLens{Id="r",Frame=MatrixD.CreateRotationZ(Math.PI/2),Aspect=.5,VerticalFov=Math.PI/2};
  Check(Covered(rolled,new Vector3D(.6,0,1))&&!Covered(rolled,new Vector3D(0,.6,1)),"roll changes a portrait camera's world coverage correctly");
  var rolledMesh=CameraPanorama.Join(Mesh(rolled),rolled,new[]{rolled});
  foreach(int i in Enumerable.Range(0,rolledMesh.Geometry.Points.Length))
  {var p=rolledMesh.Geometry.Points[i];Check(Math.Abs(rolledMesh.UV[i].X-(.5-p.X/(p.Z)))<1e-6&&Math.Abs(rolledMesh.UV[i].Y-(.5-p.Y/(2*p.Z)))<1e-6,"inside UVs preserve image handedness and roll");}
  try{using(GeometryWork.Begin(1))CameraPanorama.Join(Mesh(views[0]),views[0],views);throw new Exception("Unbounded panorama work");}catch(ArgumentException){checks++;}
  return checks;
 }
}
