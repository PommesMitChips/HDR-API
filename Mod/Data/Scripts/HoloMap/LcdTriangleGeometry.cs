using System;
using System.Collections.Generic;
using VRageMath;
namespace HoloMap
{
 public struct LcdTrianglePiece
 {
  public Vector2 Center,Size;
  public float Rotation;
  public bool Mirrored;
 }
 public struct LcdRectanglePiece
 {
  public Vector2 Center,Size;
  public float Rotation;
 }
 public static class LcdTriangleGeometry
 {
  static bool Near(Vector2 a,Vector2 b){return (a-b).LengthSquared()<=0.000001f;}
  static bool Finite(Vector2 p){return Geometry.Finite(p.X)&&Geometry.Finite(p.Y);}
  static bool InTriangle(Vector2 p,Vector2 a,Vector2 b,Vector2 c){return Near(p,a)||Near(p,b)||Near(p,c);}
  public static bool TryRectangle(Vector2 a,Vector2 b,Vector2 c,Vector2 d,Vector2 e,Vector2 f,out LcdRectanglePiece rectangle)
  {
   rectangle=new LcdRectanglePiece();
   if(!Finite(a)||!Finite(b)||!Finite(c)||!Finite(d)||!Finite(e)||!Finite(f)
      ||Near(a,b)||Near(a,c)||Near(b,c)||Near(d,e)||Near(d,f)||Near(e,f))return false;
   bool sa=InTriangle(a,d,e,f),sb=InTriangle(b,d,e,f),sc=InTriangle(c,d,e,f);
   if((sa?1:0)+(sb?1:0)+(sc?1:0)!=2)return false;
   Vector2 p,q,outer;
   if(sa&&sb){p=a;q=b;outer=c;}else if(sa&&sc){p=a;q=c;outer=b;}else{p=b;q=c;outer=a;}
   Vector2 other=!Near(d,p)&&!Near(d,q)?d:!Near(e,p)&&!Near(e,q)?e:f;
   if(Near(outer,other))return false;
   var x=outer-p;var y=other-p;float width=x.Length(),height=y.Length();
   if(!Geometry.Finite(width)||!Geometry.Finite(height)||width<0.01f||height<0.01f)return false;
   // The shared edge must be the rectangle's diagonal, not an overlapping side.
   if((p+x+y-q).LengthSquared()>Math.Max(0.000001,(width+height)*(double)(width+height)*1e-12)
      ||Math.Abs(Vector2.Dot(x,y))>width*(double)height*1e-5)return false;
   rectangle=new LcdRectanglePiece{Center=(p+q)*0.5f,Size=new Vector2(width,height),Rotation=(float)Math.Atan2(x.Y,x.X)};
   return true;
  }
  // Left template is x >= y; Right is x + y <= 1 in normalized texture coordinates.
  public static LcdTrianglePiece[] Decompose(Vector2 a,Vector2 b,Vector2 c)
  {
   if(!Geometry.Finite(a.X)||!Geometry.Finite(a.Y)||!Geometry.Finite(b.X)||!Geometry.Finite(b.Y)||!Geometry.Finite(c.X)||!Geometry.Finite(c.Y))throw new ArgumentException("LCD triangle vertices must be finite.");
   double ab=(b-a).LengthSquared(),bc=(c-b).LengthSquared(),ca=(a-c).LengthSquared();
   double dotA=Vector2.Dot(b-a,c-a),dotB=Vector2.Dot(a-b,c-b),dotC=Vector2.Dot(a-c,b-c);
   if(ab>1e-8&&ca>1e-8&&dotA*dotA<=ab*ca*1e-12){}
   else if(ab>1e-8&&bc>1e-8&&dotB*dotB<=ab*bc*1e-12){var saved=a;a=b;b=saved;}
   else if(ca>1e-8&&bc>1e-8&&dotC*dotC<=ca*bc*1e-12){var saved=a;a=c;c=b;b=saved;}
   else if(bc>=ab&&bc>=ca){var saved=a;a=b;b=c;c=saved;}
   else if(ca>=ab&&ca>=bc){var saved=b;b=a;a=c;c=saved;}
   var baseVector=b-a;double length=baseVector.Length();if(length<0.0001)return new LcdTrianglePiece[0];
   var unit=baseVector/(float)length;var normal=new Vector2(-unit.Y,unit.X);double height=Vector2.Dot(c-a,normal);
   if(height<0){var saved=a;a=b;b=saved;unit=-unit;normal=-normal;height=-height;}
   if(height<0.0001)return new LcdTrianglePiece[0];
   double distance=Math.Max(0,Math.Min(length,Vector2.Dot(c-a,unit)));var foot=a+unit*(float)distance;float angle=(float)Math.Atan2(unit.Y,unit.X);var result=new List<LcdTrianglePiece>(2);
   if(distance>0.0001)result.Add(new LcdTrianglePiece{Center=foot-unit*(float)(distance*.5)+normal*(float)(height*.5),Size=new Vector2((float)distance,(float)height),Rotation=angle,Mirrored=true});
   double remaining=length-distance;
   if(remaining>0.0001)result.Add(new LcdTrianglePiece{Center=foot+unit*(float)(remaining*.5)+normal*(float)(height*.5),Size=new Vector2((float)remaining,(float)height),Rotation=angle,Mirrored=false});
   return result.ToArray();
  }
 }
}
