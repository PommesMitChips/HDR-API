using System;
using VRageMath;
namespace HoloMap
{
 // Inverse of SurfaceMapping.MapPoint. UVs are derived from the world ray;
 // authored client UVs never participate in interaction authority.
 public static class UiSurfaceHit
 {
  public static bool TryHit(Vector3D origin,Vector3D direction,SurfaceStyle style,double range,out Vector3D hit,out Vector2 uv,bool twoSided=false)
  {return TryHit(origin,direction,style,1,1,range,1,twoSided?1:0,out hit,out uv);}
  public static bool TryHit(Vector3D origin,Vector3D direction,SurfaceStyle style,double width,double height,double range,float frontOpacity,float backOpacity,out Vector3D hit,out Vector2 uv,Func<Vector3D,bool> accept=null)
  {
   hit=Vector3D.Zero;uv=Vector2.Zero;
   try{SurfaceMapping.ValidateStyle(style);}catch(ArgumentException){return false;}
   if(!Finite(origin)||!Finite(direction)||!Geometry.Finite(range)||range<=0||range>1000000||!Geometry.Finite(width)||!Geometry.Finite(height)||width<=0||height<=0||direction.LengthSquared()<1e-16)return false;
   direction=Vector3D.Normalize(direction);
   if(style.Kind==SurfaceKind.Mesh)return Mesh(origin,direction,style,range,frontOpacity,backOpacity,out hit,out uv,accept);
   if(style.Kind==SurfaceKind.Plane)
   {
    if(Math.Abs(direction.Z)<1e-12||!Enabled(Vector3D.UnitZ,direction,frontOpacity,backOpacity))return false;
    double t=-origin.Z/direction.Z;if(t<0||t>range)return false;var p=origin+direction*t;
    if(!Uv(.5+p.X/width,.5-p.Y/height,out uv)||accept!=null&&!accept(p))return false;hit=p;return true;
   }
   var r=style.Kind==SurfaceKind.Ellipsoid?style.Radii:new Vector3D(style.Radius);
   var o=new Vector3D(origin.X/r.X,style.Kind==SurfaceKind.Cylinder?0:origin.Y/r.Y,origin.Z/r.Z);
   var d=new Vector3D(direction.X/r.X,style.Kind==SurfaceKind.Cylinder?0:direction.Y/r.Y,direction.Z/r.Z);
   double a=d.LengthSquared(),b=Vector3D.Dot(o,d),c=o.LengthSquared()-1,disc=b*b-a*c;if(a<1e-20||disc< -1e-12)return false;double root=Math.Sqrt(Math.Max(0,disc));
   var candidates=new[]{(-b-root)/a,(-b+root)/a};
   foreach(double distance in candidates)
   {
    if(distance<0||distance>range)continue;var point=origin+direction*distance;
    var normal=style.Kind==SurfaceKind.Cylinder?new Vector3D(point.X,0,point.Z):SurfaceMapping.EllipsoidNormal(point,r);
    if(!Enabled(normal*(style.Inward?-1:1),direction,frontOpacity,backOpacity))continue;
    double x,y;
    if(style.Kind==SurfaceKind.Cylinder){double angle=Math.Atan2(point.X,point.Z);x=style.Mapping==SurfaceMappingMode.Geodesic?angle*style.Radius/width:angle/style.HorizontalRadians;y=point.Y/height;}
    else if(style.Mapping==SurfaceMappingMode.Pinhole){if(point.Z<=0)continue;double tangent=Math.Tan(style.VerticalFovRadians/2),aspect=style.SourceAspect>0?style.SourceAspect:width/height;x=point.X/point.Z/(2*tangent*aspect);y=point.Y/point.Z/(2*tangent);}
    else if(style.Mapping==SurfaceMappingMode.Geodesic)
    {double length=point.Length(),theta=Math.Acos(Math.Max(-1,Math.Min(1,point.Z/length))),radial=Math.Sqrt(point.X*point.X+point.Y*point.Y);if(theta>=Math.PI-.01)continue;double scale=radial<1e-12?0:theta/radial;x=point.X*scale/style.HorizontalRadians;y=point.Y*scale/style.VerticalRadians;}
    else{double length=point.Length();x=Math.Atan2(point.X,point.Z)/style.HorizontalRadians;y=Math.Asin(Math.Max(-1,Math.Min(1,point.Y/length)))/style.VerticalRadians;}
    if(!Uv(.5+x,.5-y,out uv)||accept!=null&&!accept(point))continue;hit=point;return true;
   }
   return false;
  }
  static bool Mesh(Vector3D origin,Vector3D direction,SurfaceStyle style,double range,float front,float back,out Vector3D hit,out Vector2 uv,Func<Vector3D,bool> accept)
  {
   hit=Vector3D.Zero;uv=Vector2.Zero;double nearest=range;bool found=false;var p=style.MeshPoints;var t=style.MeshTriangles;var texture=style.MeshUV;
   for(int i=0;i<t.Length;i+=3)
   {
    var a=p[t[i]];var e1=p[t[i+1]]-a;var e2=p[t[i+2]]-a;if(!Enabled(Vector3D.Cross(e1,e2)*(style.Inward?-1:1),direction,front,back))continue;
    var cross=Vector3D.Cross(direction,e2);double determinant=Vector3D.Dot(e1,cross);if(Math.Abs(determinant)<1e-12)continue;var delta=origin-a;double u=Vector3D.Dot(delta,cross)/determinant;if(u< -1e-10||u>1+1e-10)continue;
    var q=Vector3D.Cross(delta,e1);double v=Vector3D.Dot(direction,q)/determinant;if(v< -1e-10||u+v>1+1e-10)continue;double distance=Vector3D.Dot(e2,q)/determinant;if(distance<0||distance>nearest)continue;
    var mapped=texture[t[i]]*(float)(1-u-v)+texture[t[i+1]]*(float)u+texture[t[i+2]]*(float)v;Vector2 bounded;if(!Uv(mapped.X,mapped.Y,out bounded))continue;
    var point=origin+direction*distance;if(accept!=null&&!accept(point))continue;nearest=distance;hit=point;uv=bounded;found=true;
   }
   return found;
  }
  static bool Enabled(Vector3D normal,Vector3D direction,float front,float back){return Vector3D.Dot(normal,direction)<=0?front>0:back>0;}
  static bool Uv(double u,double v,out Vector2 uv){uv=Vector2.Zero;if(!Geometry.Finite(u)||!Geometry.Finite(v)||u< -1e-7||u>1+1e-7||v< -1e-7||v>1+1e-7)return false;uv=new Vector2((float)Math.Max(0,Math.Min(1,u)),(float)Math.Max(0,Math.Min(1,v)));return true;}
  static bool Finite(Vector3D p){return Geometry.Finite(p.X)&&Geometry.Finite(p.Y)&&Geometry.Finite(p.Z);}
 }
}
