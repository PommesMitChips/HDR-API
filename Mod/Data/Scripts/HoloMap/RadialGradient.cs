using System;
using System.Collections.Generic;
using VRageMath;

namespace HoloMap
{
    public static partial class MeshEffects
    {
        // Shared annuli in gradient space, independent of the source triangulation.
        // Colors remain quantized because SE billboards expose one color per triangle.
        public static SurfaceMesh RadialGradient(SurfaceMesh mesh,Func<Vector3D,Vector3D> metric,Func<double,Vector4> palette,int resolution,bool repeat=false)
        {
            using(GeometryWork.Begin())
            {
                if(metric==null||palette==null||resolution<1||resolution>8)throw new ArgumentException("Radial gradient requires a metric, palette and resolution 1–8.");
                int bands=resolution*8,angles=32;var output=new Builder{Textured=mesh.UV!=null};var g=mesh.Geometry;var boundsMin=g.Points[0];var boundsMax=boundsMin;foreach(var point in g.Points){boundsMin=Vector3D.Min(boundsMin,point);boundsMax=Vector3D.Max(boundsMax,point);}double mergeStep=Math.Max(1e-12,(boundsMax-boundsMin).Length()*1e-10);
                var merged=ConvexBoundary(mesh);int faceCount=merged==null?g.Triangles.Length/3:1;
                for(int face=0;face<faceCount;face++)
                {
                    int triangle=face*3;var va=At(mesh,g.Triangles[triangle]);var vb=At(mesh,g.Triangles[triangle+1]);var vc=At(mesh,g.Triangles[triangle+2]);var a=metric(va.P);var b=metric(vb.P);var c=metric(vc.P);var border=merged==null?new[]{a,b,c}:Array.ConvertAll(merged,p=>metric(p));
                    Geometry.ValidatePoints(new[]{a,b,c});var normal=Vector3D.Cross(b-a,c-a);double area=normal.Length();if(area<1e-15)continue;normal/=area;
                    // Canonical orientation makes coplanar triangles use the same ring vertices.
                    if(normal.X<-1e-12||(Math.Abs(normal.X)<=1e-12&&(normal.Y<-1e-12||(Math.Abs(normal.Y)<=1e-12&&normal.Z<0))))normal=-normal;
                    double distance=Vector3D.Dot(normal,a);var center=normal*distance;var axis=Math.Abs(normal.Z)<.9?Vector3D.UnitZ:Vector3D.UnitY;var u=Vector3D.Normalize(Vector3D.Cross(normal,axis));var v=Vector3D.Cross(normal,u);
                    double max=0;foreach(var point in border)max=Math.Max(max,point.Length());double min=PolygonRadius(border,center,normal);
                    if(!repeat&&min>=1/Math.Cos(Math.PI/angles)){var color=MultiplyAlpha(palette(1),mesh.Colors[triangle/3].W);if(merged==null)output.Triangle(va,vb,vc,color);else for(int i=0;i<g.Triangles.Length;i+=3)output.Triangle(At(mesh,g.Triangles[i]),At(mesh,g.Triangles[i+1]),At(mesh,g.Triangles[i+2]),color);continue;}
                    if(repeat&&max*bands>128)throw new ArgumentException("Repeated radial gradient exceeds the ring work budget.");
                    double cosine=Math.Cos(Math.PI/angles),conservativeMin=Math.Sqrt(distance*distance+Math.Max(0,min*min-distance*distance)*cosine*cosine);
                    int first=Math.Max(0,(int)Math.Floor(conservativeMin*bands)),last=repeat?(int)Math.Floor(max*bands):Math.Min(bands-1,(int)Math.Floor(max*bands));
                    for(int band=first;band<=last+(!repeat&&max>1?1:0);band++)
                    {
                        GeometryWork.Charge();bool outer=band>=bands&&!repeat;double low=outer?1:(double)band/bands,high=outer?max+1e-8:(double)(band+1)/bands;
                        // Circumscribed shared rings cover the source boundary without triangle-specific inflation.
                        double inner=Math.Sqrt(Math.Max(0,low*low-distance*distance))/cosine,radius=Math.Sqrt(Math.Max(0,high*high-distance*distance))/cosine;
                        if(radius<=inner)continue;var color=MultiplyAlpha(palette(outer?1:(band+.5)/bands),mesh.Colors[triangle/3].W);
                        for(int angle=0;angle<angles;angle++)
                        {
                            GeometryWork.Charge();double t=angle*2*Math.PI/angles,next=(angle+1)*2*Math.PI/angles;var p=u*Math.Cos(t)+v*Math.Sin(t);var q=u*Math.Cos(next)+v*Math.Sin(next);
                            var polygon=new List<Vertex>{V(center+p*inner),V(center+p*radius),V(center+q*radius),V(center+q*inner)};
                            double sign=PolygonSign(border,normal);
                            for(int edge=0;edge<border.Length;edge++){GeometryWork.Charge();var x=border[edge];var inward=Vector3D.Cross(normal,border[(edge+1)%border.Length]-x)*sign;bool allOutside=true,allInside=true;foreach(var point in polygon){double value=Vector3D.Dot(inward,point.P-x);if(value>=0)allOutside=false;else allInside=false;}if(allOutside){polygon.Clear();break;}if(allInside)continue;polygon=ClipPolygon(polygon,new Vector4((float)inward.X,(float)inward.Y,(float)inward.Z,(float)-Vector3D.Dot(inward,x)));if(polygon.Count==0)break;}
                            if(polygon.Count<3)continue;
                            for(int i=0;i<polygon.Count;i++){var point=FromMetric(polygon[i].P,a,b,c,va,vb,vc);point.P=new Vector3D(Math.Round(point.P.X/mergeStep)*mergeStep,Math.Round(point.P.Y/mergeStep)*mergeStep,Math.Round(point.P.Z/mergeStep)*mergeStep);point.UV=new Vector2((float)(Math.Round(point.UV.X*1e8)/1e8),(float)(Math.Round(point.UV.Y*1e8)/1e8));polygon[i]=point;}
                            for(int i=1;i+1<polygon.Count;i++)output.Triangle(polygon[0],polygon[i],polygon[i+1],color);
                        }
                    }
                }
                // Wires retain their shape and get small, uniformly sized color segments.
                for(int i=0;i<g.Edges.Length;i+=2){var a=At(mesh,g.Edges[i]);var b=At(mesh,g.Edges[i+1]);for(int j=0;j<bands;j++){var p=Lerp(a,b,(double)j/bands);var q=Lerp(a,b,(double)(j+1)/bands);var color=palette(metric((p.P+q.P)/2).Length());output.Line(p,q,MultiplyAlpha(color,mesh.EdgeColors[i/2].W));}}
                return output.Finish();
            }
        }
        static Vector4 MultiplyAlpha(Vector4 color,float alpha){color.W*=alpha;return color;}
        static double PolygonRadius(Vector3D[] points,Vector3D center,Vector3D normal)
        {
            double sign=PolygonSign(points,normal);bool inside=true;double result=double.MaxValue;for(int i=0;i<points.Length;i++){var p=points[i];var d=points[(i+1)%points.Length]-p;if(Vector3D.Dot(Vector3D.Cross(d,center-p),normal)*sign<0)inside=false;double t=Math.Max(0,Math.Min(1,-Vector3D.Dot(p,d)/d.LengthSquared()));result=Math.Min(result,(p+d*t).Length());}return inside?center.Length():result;
        }
        static double PolygonSign(Vector3D[] points,Vector3D normal)
        {double area=0;for(int i=1;i+1<points.Length;i++)area+=Vector3D.Dot(Vector3D.Cross(points[i]-points[0],points[i+1]-points[0]),normal);return area>=0?1:-1;}
        static Vector3D[] ConvexBoundary(SurfaceMesh mesh)
        {
            var g=mesh.Geometry;if(mesh.UV!=null||g.Triangles.Length<6)return null;GeometryWork.Charge(g.Points.Length+mesh.Colors.Length);var color=mesh.Colors[0];foreach(var c in mesh.Colors)if(c!=color)return null;
            var normal=Vector3D.Cross(g.Points[g.Triangles[1]]-g.Points[g.Triangles[0]],g.Points[g.Triangles[2]]-g.Points[g.Triangles[0]]);if(normal.LengthSquared()<1e-20)return null;normal.Normalize();var origin=g.Points[g.Triangles[0]];
            foreach(var p in g.Points)if(Math.Abs(Vector3D.Dot(p-origin,normal))>1e-8)return null;
            var counts=new Dictionary<long,int>();for(int i=0;i<g.Triangles.Length;i+=3)for(int e=0;e<3;e++){GeometryWork.Charge();int x=g.Triangles[i+e],y=g.Triangles[i+(e+1)%3];long key=((long)Math.Min(x,y)<<32)|(uint)Math.Max(x,y);int count;counts.TryGetValue(key,out count);counts[key]=count+1;}
            var neighbors=new Dictionary<int,List<int>>();foreach(var pair in counts){if(pair.Value==2)continue;if(pair.Value!=1)return null;int x=(int)(pair.Key>>32),y=(int)pair.Key;List<int> list;if(!neighbors.TryGetValue(x,out list)){list=new List<int>();neighbors.Add(x,list);}list.Add(y);if(!neighbors.TryGetValue(y,out list)){list=new List<int>();neighbors.Add(y,list);}list.Add(x);}
            if(neighbors.Count<3||neighbors.Count>128)return null;int start=int.MaxValue;foreach(var pair in neighbors){if(pair.Value.Count!=2)return null;start=Math.Min(start,pair.Key);}var loop=new List<Vector3D>();int previous=-1,current=start;
            do{if(loop.Count>=neighbors.Count)return null;loop.Add(g.Points[current]);var choices=neighbors[current];int next=choices[0]==previous?choices[1]:choices[0];previous=current;current=next;}while(current!=start);
            if(loop.Count!=neighbors.Count)return null;double sign=0;for(int i=0;i<loop.Count;i++){double turn=Vector3D.Dot(Vector3D.Cross(loop[(i+1)%loop.Count]-loop[i],loop[(i+2)%loop.Count]-loop[(i+1)%loop.Count]),normal);if(Math.Abs(turn)<1e-12)continue;if(sign==0)sign=Math.Sign(turn);else if(turn*sign<0)return null;}return sign==0?null:loop.ToArray();
        }
        static Vertex FromMetric(Vector3D point,Vector3D a,Vector3D b,Vector3D c,Vertex va,Vertex vb,Vertex vc)
        {
            var ab=b-a;var ac=c-a;var ap=point-a;double bb=Vector3D.Dot(ab,ab),bc=Vector3D.Dot(ab,ac),cc=Vector3D.Dot(ac,ac),pb=Vector3D.Dot(ap,ab),pc=Vector3D.Dot(ap,ac),den=bb*cc-bc*bc;if(!Geometry.Finite(den)||den<=0)throw new ArgumentException("Radial gradient metric triangle is numerically degenerate.");double x=(cc*pb-bc*pc)/den,y=(bb*pc-bc*pb)/den;
            return Bary(va,vb,vc,x,y);
        }
    }
}
