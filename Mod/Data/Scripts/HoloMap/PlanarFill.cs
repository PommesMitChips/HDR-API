using System;
using System.Collections.Generic;
using VRageMath;

namespace HoloMap
{
    public static class PlanarFill
    {
        sealed class Edge { public Vector2D A,B; public int Sign; public double X(double y){return A.X+(B.X-A.X)*(y-A.Y)/(B.Y-A.Y);} }
        public static Geometry Tessellate(Vector3D[][] contours,bool evenOdd=true,bool outlines=true,int contourLimit=16,int pointLimit=256)
        { using (GeometryWork.Begin()) return TessellateCore(contours,evenOdd,outlines,contourLimit,pointLimit); }
        static Geometry TessellateCore(Vector3D[][] contours,bool evenOdd,bool outlines,int contourLimit,int pointLimit)
        {
            if(contours==null||contours.Length<1||contours.Length>contourLimit)throw new ArgumentException("Coplanar contour count exceeds the budget.");
            int total=0;foreach(var c in contours){if(c==null||c.Length<3||c.Length>128)throw new ArgumentException("Contours require 3–128 points without repeating the closing point.");Geometry.ValidatePoints(c);total+=c.Length;}
            if(total>pointLimit)throw new ArgumentException("Contour input point budget exceeded.");
            var origin=contours[0][0];var normal=Vector3D.Zero;
            for(int i=1;i+1<contours[0].Length;i++){normal=Vector3D.Cross(contours[0][i]-origin,contours[0][i+1]-origin);if(normal.LengthSquared()>1e-20)break;}
            if(normal.LengthSquared()<1e-20)throw new ArgumentException("First contour is degenerate.");normal.Normalize();
            var axis=Math.Abs(normal.X)<0.8?Vector3D.UnitX:Vector3D.UnitY;var u=Vector3D.Normalize(Vector3D.Cross(axis,normal));var v=Vector3D.Cross(normal,u);
            var edges=new List<Edge>();var levels=new List<double>();var builder=new MeshEffects.Builder();
            foreach(var contour in contours)
            {
                var flat=new Vector2D[contour.Length];double extent=1;foreach(var p in contour)extent=Math.Max(extent,(p-origin).Length());
                for(int i=0;i<contour.Length;i++){var p=contour[i]-origin;if(Math.Abs(Vector3D.Dot(p,normal))>extent*1e-8)throw new ArgumentException("All contours must share one plane.");flat[i]=new Vector2D(Vector3D.Dot(p,u),Vector3D.Dot(p,v));levels.Add(flat[i].Y);}
                for(int i=0;i<flat.Length;i++)
                {var a=flat[i];var b=flat[(i+1)%flat.Length];if((a-b).LengthSquared()<1e-20)throw new ArgumentException("Repeated contour point.");if(outlines)builder.Line(MeshEffects.V(contour[i]),MeshEffects.V(contour[(i+1)%contour.Length]),Vector4.One);if(Math.Abs(a.Y-b.Y)>1e-12)edges.Add(new Edge{A=a,B=b,Sign=b.Y>a.Y?1:-1});}
            }
            // Split at crossings, making the edge order fixed inside each slab.
            for(int i=0;i<edges.Count;i++)for(int j=i+1;j<edges.Count;j++)
            {
                GeometryWork.Charge();
                var a=edges[i].A;var d=edges[i].B-a;var b=edges[j].A;var e=edges[j].B-b;double cross=d.X*e.Y-d.Y*e.X;if(Math.Abs(cross)<1e-12)continue;
                var delta=b-a;double t=(delta.X*e.Y-delta.Y*e.X)/cross,s=(delta.X*d.Y-delta.Y*d.X)/cross;
                if(t>0&&t<1&&s>0&&s<1)levels.Add(a.Y+t*d.Y);
            }
            levels.Sort();var unique=new List<double>();foreach(double y in levels)if(unique.Count==0||Math.Abs(y-unique[unique.Count-1])>1e-9)unique.Add(y);
            if(unique.Count>1024)throw new ArgumentException("Filled contour intersection budget exceeded.");
            for(int slab=0;slab+1<unique.Count;slab++)
            {
                GeometryWork.Charge(edges.Count);
                double lo=unique[slab],hi=unique[slab+1],mid=(lo+hi)/2;var active=new List<Edge>();
                foreach(var e in edges)if(mid>Math.Min(e.A.Y,e.B.Y)&&mid<Math.Max(e.A.Y,e.B.Y))active.Add(e);
                active.Sort((a,b)=>a.X(mid).CompareTo(b.X(mid)));int winding=0;
                for(int i=0;i+1<active.Count;i++)
                {
                    GeometryWork.Charge();
                    winding+=evenOdd?1:active[i].Sign;if((evenOdd?winding%2!=0:winding!=0)&&active[i+1].X(mid)-active[i].X(mid)>1e-10)
                    {
                        var a=MeshEffects.V(origin+u*active[i].X(lo)+v*lo);var b=MeshEffects.V(origin+u*active[i+1].X(lo)+v*lo);
                        var c=MeshEffects.V(origin+u*active[i+1].X(hi)+v*hi);var d=MeshEffects.V(origin+u*active[i].X(hi)+v*hi);
                        builder.Triangle(a,b,c,Vector4.One);builder.Triangle(a,c,d,Vector4.One);
                    }
                }
            }
            return builder.Finish().Geometry;
        }
    }
}
