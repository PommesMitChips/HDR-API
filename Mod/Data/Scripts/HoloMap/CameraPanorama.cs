using System;
using System.Collections.Generic;
using VRageMath;
namespace HoloMap
{
 // Directional stitching only: image rays share a sphere, but no scene depth is inferred.
 public sealed class PanoramaLens
 {
  public string Id;
  public MatrixD Frame=MatrixD.Identity;
  public double VerticalFov=Math.PI/3,Aspect=1;
  public bool Inward=true;
 }
 public static class CameraPanorama
 {
  const double Epsilon=1e-11;
  static List<Vector3D> Clip(List<Vector3D> polygon,Vector3D plane)
  {
   var output=new List<Vector3D>();if(polygon.Count==0)return output;
   GeometryWork.Charge(polygon.Count);
   var a=polygon[polygon.Count-1];double da=Vector3D.Dot(a,plane);bool insideA=da>=-Epsilon;
   foreach(var b in polygon)
   {
    double db=Vector3D.Dot(b,plane);bool insideB=db>=-Epsilon;
    if(insideA!=insideB)output.Add(a+(b-a)*(da/(da-db)));
    if(insideB)output.Add(b);a=b;da=db;insideA=insideB;
   }
   return output;
  }
  static void Subtract(List<Vector3D> polygon,Vector3D[] planes,List<List<Vector3D>> output)
  {
   // A convex cone's complement is partitioned into disjoint convex pieces.
   var remaining=polygon;
   foreach(var plane in planes)
   {
    var outside=Clip(remaining,-plane);if(outside.Count>=3)output.Add(outside);
    remaining=Clip(remaining,plane);if(remaining.Count<3)return;
   }
  }
  static void Validate(PanoramaLens lens)
  {
   if(lens==null||string.IsNullOrEmpty(lens.Id)||!Geometry.Finite(lens.VerticalFov)||lens.VerticalFov<.1||lens.VerticalFov>2.8||
      !Geometry.Finite(lens.Aspect)||lens.Aspect<1d/64||lens.Aspect>64)
    throw new ArgumentException("Invalid spherical camera lens.");
   var m=lens.Frame;
   if(!Geometry.Finite(m.Right.X)||!Geometry.Finite(m.Right.Y)||!Geometry.Finite(m.Right.Z)||
      !Geometry.Finite(m.Up.X)||!Geometry.Finite(m.Up.Y)||!Geometry.Finite(m.Up.Z)||
      !Geometry.Finite(m.Backward.X)||!Geometry.Finite(m.Backward.Y)||!Geometry.Finite(m.Backward.Z)||
      Math.Abs(m.Right.LengthSquared()-1)>1e-8||Math.Abs(m.Up.LengthSquared()-1)>1e-8||Math.Abs(m.Backward.LengthSquared()-1)>1e-8||
      Math.Abs(Vector3D.Dot(m.Right,m.Up))>1e-8||Math.Abs(Vector3D.Dot(m.Right,m.Backward))>1e-8||Math.Abs(Vector3D.Dot(m.Up,m.Backward))>1e-8)
    throw new ArgumentException("Spherical camera lens requires an orthonormal basis.");
  }
  public static SurfaceMesh Join(SurfaceMesh source,PanoramaLens own,PanoramaLens[] peers,int maxPoints=Geometry.MaxPoints,int maxPrimitives=Geometry.MaxPrimitives)
  {
   if(maxPoints<1||maxPoints>65536||maxPrimitives<1||maxPrimitives>131072)throw new ArgumentException("Invalid panorama output budget.");
   Validate(own);if(source==null||source.Geometry==null||peers==null||peers.Length>8)throw new ArgumentException("Invalid panorama mesh or camera count.");
   foreach(var peer in peers)Validate(peer);
   var g=source.Geometry;var points=new List<Vector3D>();var uv=new List<Vector2>();var triangles=new List<int>();var colors=new List<Vector4>();
   var vertices=new Dictionary<Vector3D,int>();
   var inverse=MatrixD.Invert(own.Frame);double ty=Math.Tan(own.VerticalFov/2),tx=ty*own.Aspect;
   var competitors=new List<Vector3D[]>();
   foreach(var peer in peers)
   {
    if(peer.Id==own.Id)continue;
    var frame=peer.Frame*inverse;var f=frame.Backward;var better=f-Vector3D.UnitZ;
    bool tied=better.LengthSquared()<1e-20;
    if(tied&&string.CompareOrdinal(peer.Id,own.Id)>0)continue;
    double py=Math.Tan(peer.VerticalFov/2),px=py*peer.Aspect;
    var planes=new List<Vector3D>{f*px+frame.Right,f*px-frame.Right,f*py+frame.Up,f*py-frame.Up};
    if(!tied)planes.Add(Vector3D.Normalize(better));
    competitors.Add(planes.ToArray());
   }
   for(int t=0;t<g.Triangles.Length;t+=3)
   {
    var a=g.Points[g.Triangles[t]];var b=g.Points[g.Triangles[t+1]];var c=g.Points[g.Triangles[t+2]];
    double radius=a.Length();if(radius<1e-8)throw new ArgumentException("Panorama vertices must lie on a sphere.");
    var pieces=new List<List<Vector3D>>{new List<Vector3D>{a,b,c}};
    foreach(var planes in competitors)
    {
     var next=new List<List<Vector3D>>();foreach(var piece in pieces)Subtract(piece,planes,next);pieces=next;
     if(pieces.Count>128)throw new ArgumentException("Panorama partition limit exceeded.");if(pieces.Count==0)break;
    }
    foreach(var piece in pieces)
    {
     var indices=new int[piece.Count];int vertex=0;
     foreach(var p in piece)
     {
      GeometryWork.Charge();var q=Vector3D.Normalize(p)*radius;if(q.Z<=1e-10)throw new ArgumentException("Panorama vertex lies behind its camera.");
      double x=own.Inward?-q.X:q.X;
      // Adjacent clipped triangles share positions and the same camera UV mapping.
      // Weld numerical intersection noise instead of expanding to triangle soup.
      var key=new Vector3D(Math.Round(q.X,8),Math.Round(q.Y,8),Math.Round(q.Z,8));int index;
      if(!vertices.TryGetValue(key,out index))
      {
       if(points.Count>=maxPoints)throw new ArgumentException("Panorama point budget exceeded.");
       index=points.Count;vertices.Add(key,index);points.Add(q);
       uv.Add(new Vector2((float)Math.Max(0,Math.Min(1,.5+x/(2*tx*q.Z))),
        (float)Math.Max(0,Math.Min(1,.5-q.Y/(2*ty*q.Z)))));
      }
      indices[vertex++]=index;
     }
     for(int k=1;k+1<piece.Count;k++)
     {
      int aIndex=indices[0],bIndex=indices[k],cIndex=indices[k+1];
      if(Vector3D.Cross(points[bIndex]-points[aIndex],points[cIndex]-points[aIndex]).LengthSquared()<1e-20)continue;
      if(triangles.Count/3>=maxPrimitives)throw new ArgumentException("Panorama triangle budget exceeded.");
      triangles.Add(aIndex);triangles.Add(bIndex);triangles.Add(cIndex);colors.Add(source.Colors==null?Vector4.One:source.Colors[t/3]);
     }
    }
   }
   return new SurfaceMesh{Geometry=new Geometry(points.ToArray(),new int[0],triangles.ToArray()),UV=uv.ToArray(),Colors=colors.ToArray()};
  }
 }
}
