using System;
using System.Collections.Generic;
using VRageMath;
namespace HoloMap
{
 public static partial class SurfaceMapping
 {
  public static Vector3D IntersectEllipsoid(Vector3D ray,Vector3D radii)
  {
   if(!Finite(ray)||!Finite(radii)||radii.X<=0||radii.Y<=0||radii.Z<=0)throw new ArgumentException("Invalid ellipsoid ray/radii.");
   double length=Math.Sqrt(ray.X*ray.X/(radii.X*radii.X)+ray.Y*ray.Y/(radii.Y*radii.Y)+ray.Z*ray.Z/(radii.Z*radii.Z));
   if(!Geometry.Finite(length)||length<1e-12)throw new ArgumentException("Ellipsoid ray requires a nonzero direction.");return ray/length;
  }
  public static Vector3D EllipsoidNormal(Vector3D point,Vector3D radii)
  {return Vector3D.Normalize(new Vector3D(point.X/(radii.X*radii.X),point.Y/(radii.Y*radii.Y),point.Z/(radii.Z*radii.Z)));}
  public static void ValidateAuthoredMesh(Vector3D[] points,int[] triangles,Vector2[] uv)
  {
   if(points==null||points.Length<3||points.Length>Geometry.MaxPoints||triangles==null||triangles.Length<3||triangles.Length%3!=0||triangles.Length>Geometry.MaxPrimitives*3||uv==null||uv.Length!=points.Length)throw new ArgumentException("Display mesh requires 3–2048 points, 1–4096 indexed triangles and one UV per point.");
   foreach(var p in points)if(!Finite(p)||p.LengthSquared()>1e12)throw new ArgumentException("Invalid display mesh point.");
   foreach(var p in uv)if(!Finite(p)||p.X<0||p.X>1||p.Y<0||p.Y>1)throw new ArgumentException("Display UV coordinates must be finite and inside 0..1.");
   foreach(int index in triangles)if(index<0||index>=points.Length)throw new ArgumentException("Invalid display mesh triangle index.");
   for(int i=0;i<triangles.Length;i+=3)
   {
    int a=triangles[i],b=triangles[i+1],c=triangles[i+2];var u=uv[b]-uv[a];var v=uv[c]-uv[a];
    if(Vector3D.Cross(points[b]-points[a],points[c]-points[a]).LengthSquared()<1e-20||Math.Abs(u.X*v.Y-u.Y*v.X)<1e-10)throw new ArgumentException("Display mesh contains a degenerate position or UV triangle.");
   }
  }
  static bool Barycentric(Vector2 point,Vector2 a,Vector2 b,Vector2 c,out double w0,out double w1,out double w2)
  {
   double det=(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);w0=w1=w2=0;if(Math.Abs(det)<1e-10)return false;
   w1=((point.X-a.X)*(c.Y-a.Y)-(point.Y-a.Y)*(c.X-a.X))/det;w2=((b.X-a.X)*(point.Y-a.Y)-(b.Y-a.Y)*(point.X-a.X))/det;w0=1-w1-w2;return w0>=-1e-7&&w1>=-1e-7&&w2>=-1e-7;
  }
  static int AuthoredFace(Vector3D source,double width,double height,SurfaceStyle style,out double a,out double b,out double c)
  {
   var uv=new Vector2((float)(source.X/width+.5),(float)(.5-source.Y/height));
   for(int i=0;i<style.MeshTriangles.Length;i+=3){GeometryWork.Charge();if(Barycentric(uv,style.MeshUV[style.MeshTriangles[i]],style.MeshUV[style.MeshTriangles[i+1]],style.MeshUV[style.MeshTriangles[i+2]],out a,out b,out c))return i;}
   a=b=c=0;throw new ArgumentException("Canvas point lies outside authored display UV triangles.");
  }
  static Vector3D AuthoredNormal(Vector3D source,double width,double height,SurfaceStyle style)
  {double a,b,c;int f=AuthoredFace(source,width,height,style,out a,out b,out c);var n=Vector3D.Normalize(Vector3D.Cross(style.MeshPoints[style.MeshTriangles[f+1]]-style.MeshPoints[style.MeshTriangles[f]],style.MeshPoints[style.MeshTriangles[f+2]]-style.MeshPoints[style.MeshTriangles[f]]));return style.Inward?-n:n;}
  static Vector3D AuthoredPoint(Vector3D source,double width,double height,SurfaceStyle style,double offset)
  {double a,b,c;int f=AuthoredFace(source,width,height,style,out a,out b,out c);var p=style.MeshPoints[style.MeshTriangles[f]]*a+style.MeshPoints[style.MeshTriangles[f+1]]*b+style.MeshPoints[style.MeshTriangles[f+2]]*c;var n=Vector3D.Normalize(Vector3D.Cross(style.MeshPoints[style.MeshTriangles[f+1]]-style.MeshPoints[style.MeshTriangles[f]],style.MeshPoints[style.MeshTriangles[f+2]]-style.MeshPoints[style.MeshTriangles[f]]));return p+n*(source.Z+(style.Inward?-offset:offset));}
  static MeshEffects.Vertex MappedVertex(MeshEffects.Vertex vertex,Vector3D p0,Vector3D p1,Vector3D p2,Vector2 u0,Vector2 u1,Vector2 u2,Vector3D normal,double width,double height,double offset)
  {
   double a,b,c;Barycentric(new Vector2((float)(vertex.P.X/width+.5),(float)(.5-vertex.P.Y/height)),u0,u1,u2,out a,out b,out c);
   // Float clip planes can leave tiny barycentric overshoot on small UV islands.
   // Keep the mapped position in the authored triangle instead of extrapolating.
   a=Math.Max(0,Math.Min(1,a));b=Math.Max(0,Math.Min(1,b));c=Math.Max(0,Math.Min(1,c));double total=a+b+c;if(total<1e-12||!Geometry.Finite(total))throw new ArgumentException("Invalid clipped display UV.");
   return new MeshEffects.Vertex{P=(p0*a+p1*b+p2*c)/total+normal*offset,UV=vertex.UV};
  }
  static Vector4 UvClip(Vector2 a,Vector2 b,Vector2 third,double width,double height)
  {
   var p=new Vector3D((a.X-.5)*width,(.5-a.Y)*height,0);var q=new Vector3D((b.X-.5)*width,(.5-b.Y)*height,0);var r=new Vector3D((third.X-.5)*width,(.5-third.Y)*height,0);
   double nx=-(q.Y-p.Y),ny=q.X-p.X;if(nx*(r.X-p.X)+ny*(r.Y-p.Y)<0){nx=-nx;ny=-ny;}return new Vector4((float)nx,(float)ny,0,(float)(-nx*p.X-ny*p.Y));
  }
  static SurfaceMesh BuildAuthored(SurfaceStyle style,Vector4 color,double depth)
  {
   using(GeometryWork.Begin(style.MaxWork))
   {
    if(style.MeshPoints.Length>style.MaxPoints||style.MeshTriangles.Length/3>style.MaxPrimitives)throw new ArgumentException("Authored display exceeds local geometry budget.");
    var b=new MeshEffects.Builder{Textured=true};var p=style.MeshPoints;var t=style.MeshTriangles;var uv=style.MeshUV;
    for(int i=0;i<t.Length;i+=3)
    {
     int a=t[i],c=t[i+1],d=t[i+2];var n=Vector3D.Normalize(Vector3D.Cross(p[c]-p[a],p[d]-p[a]))*(style.Inward?-1:1);
     var x=new MeshEffects.Vertex{P=p[a]+n*depth,UV=uv[a]};var y=new MeshEffects.Vertex{P=p[c]+n*depth,UV=uv[c]};var z=new MeshEffects.Vertex{P=p[d]+n*depth,UV=uv[d]};
     if(style.Inward)b.Triangle(x,z,y,color);else b.Triangle(x,y,z,color);
    }
    var result=b.Finish();if(result.Geometry.Points.Length>style.MaxPoints)throw new ArgumentException("Authored display layers exceed local point budget.");return result;
   }
  }
  static SurfaceMesh WarpAuthored(SurfaceMesh source,double width,double height,SurfaceStyle style,double offset)
  {
   using(GeometryWork.Begin(style.MaxWork))
   {
    var b=new MeshEffects.Builder{Textured=source.UV!=null};var p=style.MeshPoints;var t=style.MeshTriangles;var uv=style.MeshUV;
    for(int i=0;i<t.Length;i+=3)
    {
     GeometryWork.Charge();int i0=t[i],i1=t[i+1],i2=t[i+2];var n=Vector3D.Normalize(Vector3D.Cross(p[i1]-p[i0],p[i2]-p[i0]));var desired=style.Inward?-n:n;
     var piece=MeshEffects.Clip(source,new[]{UvClip(uv[i0],uv[i1],uv[i2],width,height),UvClip(uv[i1],uv[i2],uv[i0],width,height),UvClip(uv[i2],uv[i0],uv[i1],width,height)});
     var g=piece.Geometry;var mapped=new MeshEffects.Vertex[g.Points.Length];
     for(int j=0;j<mapped.Length;j++)mapped[j]=MappedVertex(new MeshEffects.Vertex{P=g.Points[j],UV=piece.UV==null?Vector2.Zero:piece.UV[j]},p[i0],p[i1],p[i2],uv[i0],uv[i1],uv[i2],n,width,height,g.Points[j].Z+(style.Inward?-offset:offset));
     for(int j=0;j<g.Triangles.Length;j+=3){var a=mapped[g.Triangles[j]];var c=mapped[g.Triangles[j+1]];var d=mapped[g.Triangles[j+2]];if(Vector3D.Dot(Vector3D.Cross(c.P-a.P,d.P-a.P),desired)<0)b.Triangle(a,d,c,piece.Colors[j/3]);else b.Triangle(a,c,d,piece.Colors[j/3]);}
     for(int j=0;j<g.Edges.Length;j+=2)b.Line(mapped[g.Edges[j]],mapped[g.Edges[j+1]],piece.EdgeColors[j/2]);
    }
    var result=b.Finish();if(result.Geometry.Points.Length>style.MaxPoints||result.Geometry.Triangles.Length/3+result.Geometry.Edges.Length/2>style.MaxPrimitives)throw new ArgumentException("Mapped display mesh exceeds geometry budget.");return result;
   }
  }
  public static bool HitMesh(Vector3D origin,Vector3D direction,Vector3D[] points,int[] triangles,Vector2[] uv,double range,out Vector3D hit,out Vector2 texture,bool twoSided=true,bool inward=false)
  {
   hit=Vector3D.Zero;texture=Vector2.Zero;ValidateAuthoredMesh(points,triangles,uv);
   if(!Finite(origin)||!Finite(direction)||!Geometry.Finite(range)||range<=0||direction.LengthSquared()<1e-16)return false;direction=Vector3D.Normalize(direction);double nearest=range;bool found=false;
   for(int i=0;i<triangles.Length;i+=3)
   {
    var a=points[triangles[i]];var e1=points[triangles[i+1]]-a;var e2=points[triangles[i+2]]-a;if(!twoSided&&Vector3D.Dot(Vector3D.Cross(e1,e2),direction)*(inward?-1:1)>=0)continue;var h=Vector3D.Cross(direction,e2);double determinant=Vector3D.Dot(e1,h);if(Math.Abs(determinant)<1e-12)continue;
    var s=origin-a;double u=Vector3D.Dot(s,h)/determinant;if(u<0||u>1)continue;var q=Vector3D.Cross(s,e1);double v=Vector3D.Dot(direction,q)/determinant;if(v<0||u+v>1)continue;double distance=Vector3D.Dot(e2,q)/determinant;if(distance<0||distance>nearest)continue;
    nearest=distance;found=true;hit=origin+direction*distance;texture=uv[triangles[i]]*(float)(1-u-v)+uv[triangles[i+1]]*(float)u+uv[triangles[i+2]]*(float)v;
   }
   return found;
  }
 }
}



