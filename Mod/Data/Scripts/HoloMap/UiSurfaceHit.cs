using System;
using VRageMath;
namespace HoloMap
{
 // Pure UV evidence for authored/ellipsoid surfaces. Projected-screen widgets are
 // not yet part of the anchor/LCD UI runtime; consumers can use this bounded seam.
 public static class UiSurfaceHit
 {
  public static bool TryHit(Vector3D origin,Vector3D direction,SurfaceStyle style,double range,out Vector3D hit,out Vector2 uv,bool twoSided=false)
  {
   hit=Vector3D.Zero;uv=Vector2.Zero;SurfaceMapping.ValidateStyle(style);
   if(!Finite(origin)||!Finite(direction)||!Geometry.Finite(range)||range<=0||range>1000000||direction.LengthSquared()<1e-16)return false;
   direction=Vector3D.Normalize(direction);
   if(style.Kind==SurfaceKind.Mesh)return SurfaceMapping.HitMesh(origin,direction,style.MeshPoints,style.MeshTriangles,style.MeshUV,range,out hit,out uv,twoSided,style.Inward);
   if(style.Kind!=SurfaceKind.Ellipsoid&&style.Kind!=SurfaceKind.Sphere)return false;
   // Geodesic sphere coordinates need a different inverse than angular UVs.
   // Fail closed until that inverse is implemented.
   if(style.Mapping==SurfaceMappingMode.Geodesic)return false;
   var r=style.Kind==SurfaceKind.Ellipsoid?style.Radii:new Vector3D(style.Radius);
   var o=new Vector3D(origin.X/r.X,origin.Y/r.Y,origin.Z/r.Z);var d=new Vector3D(direction.X/r.X,direction.Y/r.Y,direction.Z/r.Z);
   double a=d.LengthSquared(),b=Vector3D.Dot(o,d),c=o.LengthSquared()-1,disc=b*b-a*c;if(disc<0)return false;double root=Math.Sqrt(disc);
   var candidates=new[]{(-b-root)/a,(-b+root)/a};
   foreach(double distance in candidates)
   {
    if(distance<0||distance>range)continue;var point=origin+direction*distance;var n=SurfaceMapping.EllipsoidNormal(point,r)*(style.Inward?-1:1);if(!twoSided&&Vector3D.Dot(n,direction)>=0)continue;
    double u,v;
    if(style.Mapping==SurfaceMappingMode.Pinhole){if(point.Z<=0)continue;double tangent=Math.Tan(style.VerticalFovRadians/2),aspect=style.SourceAspect>0?style.SourceAspect:1;u=.5+point.X/point.Z/(2*tangent*aspect);v=.5-point.Y/point.Z/(2*tangent);}
    else{double length=point.Length();u=.5+Math.Atan2(point.X,point.Z)/style.HorizontalRadians;v=.5-Math.Asin(Math.Max(-1,Math.Min(1,point.Y/length)))/style.VerticalRadians;}
    if(!Geometry.Finite(u)||!Geometry.Finite(v)||u< -1e-7||u>1+1e-7||v< -1e-7||v>1+1e-7)continue;hit=point;uv=new Vector2((float)Math.Max(0,Math.Min(1,u)),(float)Math.Max(0,Math.Min(1,v)));return true;
   }
   return false;
  }
  static bool Finite(Vector3D p){return Geometry.Finite(p.X)&&Geometry.Finite(p.Y)&&Geometry.Finite(p.Z);}
 }
}

