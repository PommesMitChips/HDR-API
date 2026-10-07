using System;
using System.Collections.Generic;
using VRageMath;

namespace HoloMap
{
    public sealed class SurfaceMesh
    {
        public Geometry Geometry;
        public Vector4[] Colors, EdgeColors;
        public Vector2[] UV;
    }
    public static partial class MeshEffects
    {
        public struct Vertex : IEquatable<Vertex>
        {
            public Vector3D P; public Vector2 UV;
            public bool Equals(Vertex other){return P==other.P&&UV==other.UV;}
            public override bool Equals(object other){return other is Vertex&&Equals((Vertex)other);}
            public override int GetHashCode(){return unchecked(P.GetHashCode()*397^UV.GetHashCode());}
        }
        public sealed class Builder
        {
            readonly List<Vector3D> points=new List<Vector3D>();readonly List<Vector2> uv=new List<Vector2>();
            readonly List<int> triangles=new List<int>(),edges=new List<int>();readonly List<Vector4> colors=new List<Vector4>(),edgeColors=new List<Vector4>();
            readonly Dictionary<Vertex,int> indices=new Dictionary<Vertex,int>();
            public bool Textured;
            int Point(Vertex v){int index;if(indices.TryGetValue(v,out index))return index;if(points.Count>=Geometry.MaxPoints)throw new ArgumentException("Styled mesh exceeds 2048 points; lower gradient resolution or split the object.");index=points.Count;points.Add(v.P);uv.Add(v.UV);indices.Add(v,index);return index;}
            public void Triangle(Vertex a,Vertex b,Vertex c,Vector4 color)
            {GeometryWork.Charge();if(Vector3D.Cross(b.P-a.P,c.P-a.P).LengthSquared()<1e-22)return;if(triangles.Count/3+edges.Count/2>=Geometry.MaxPrimitives)throw new ArgumentException("Styled primitive budget exceeded.");triangles.Add(Point(a));triangles.Add(Point(b));triangles.Add(Point(c));colors.Add(color);}
            public void Line(Vertex a,Vertex b,Vector4 color)
            {GeometryWork.Charge();if((a.P-b.P).LengthSquared()<1e-20)return;if(triangles.Count/3+edges.Count/2>=Geometry.MaxPrimitives)throw new ArgumentException("Styled primitive budget exceeded.");edges.Add(Point(a));edges.Add(Point(b));edgeColors.Add(color);}
            public SurfaceMesh Finish()
            {if(points.Count==0){points.Add(Vector3D.Zero);uv.Add(Vector2.Zero);}var p=points.ToArray();Geometry.ValidatePoints(p);return new SurfaceMesh{Geometry=new Geometry(p,edges.ToArray(),triangles.ToArray()),Colors=colors.ToArray(),EdgeColors=edgeColors.ToArray(),UV=Textured?uv.ToArray():null};}
        }
        public static Vertex V(Vector3D p){return new Vertex{P=p};}
        public static Vertex Lerp(Vertex a,Vertex b,double t){return new Vertex{P=a.P+(b.P-a.P)*t,UV=a.UV+(b.UV-a.UV)*(float)t};}
        static double Distance(Vertex v,Vector4 p){return v.P.X*p.X+v.P.Y*p.Y+v.P.Z*p.Z+p.W;}
        public static void ValidatePlanes(Vector4[] planes)
        {
            if(planes==null)return;if(planes.Length>16)throw new ArgumentException("Clip supports at most 16 inward-facing planes.");
            foreach(var p in planes)if(!Geometry.Finite(p.X)||!Geometry.Finite(p.Y)||!Geometry.Finite(p.Z)||!Geometry.Finite(p.W)||new Vector3(p.X,p.Y,p.Z).LengthSquared()<1e-16)throw new ArgumentException("Clip plane requires finite nonzero normal and finite distance.");
        }
        public static List<Vertex> ClipPolygon(List<Vertex> polygon,Vector4 plane)
        {
            GeometryWork.Charge(polygon.Count+1);var output=new List<Vertex>();if(polygon.Count==0)return output;var a=polygon[polygon.Count-1];double da=Distance(a,plane);
            foreach(var b in polygon){double db=Distance(b,plane);if((da>=0)!=(db>=0))output.Add(Lerp(a,b,da/(da-db)));if(db>=0)output.Add(b);a=b;da=db;}return output;
        }
        public static SurfaceMesh Clip(SurfaceMesh mesh,Vector4[] planes)
        { using (GeometryWork.Begin()) return ClipCore(mesh,planes); }
        static SurfaceMesh ClipCore(SurfaceMesh mesh,Vector4[] planes)
        {
            ValidatePlanes(planes);if(planes==null||planes.Length==0)return mesh;var g=mesh.Geometry;var builder=new Builder{Textured=mesh.UV!=null};
            for(int i=0;i<g.Triangles.Length;i+=3)
            {
                var poly=new List<Vertex>{At(mesh,g.Triangles[i]),At(mesh,g.Triangles[i+1]),At(mesh,g.Triangles[i+2])};
                foreach(var plane in planes)poly=ClipPolygon(poly,plane);
                for(int j=1;j+1<poly.Count;j++)builder.Triangle(poly[0],poly[j],poly[j+1],mesh.Colors[i/3]);
            }
            for(int i=0;i<g.Edges.Length;i+=2)
            {
                var a=At(mesh,g.Edges[i]);var b=At(mesh,g.Edges[i+1]);bool keep=true;
                foreach(var p in planes){GeometryWork.Charge();double da=Distance(a,p),db=Distance(b,p);if(da<0&&db<0){keep=false;break;}if(da<0)a=Lerp(a,b,da/(da-db));else if(db<0)b=Lerp(a,b,da/(da-db));}
                if(keep)builder.Line(a,b,mesh.EdgeColors[i/2]);
            }
            return builder.Finish();
        }
        static Vertex At(SurfaceMesh m,int i){return new Vertex{P=m.Geometry.Points[i],UV=m.UV==null?Vector2.Zero:m.UV[i]};}
        static List<Vertex> ClipScalar(List<Vertex> polygon,Func<Vector3D,double> coordinate,double boundary,bool above)
        {
            GeometryWork.Charge(polygon.Count+1);var result=new List<Vertex>();if(polygon.Count==0)return result;var a=polygon[polygon.Count-1];double da=(coordinate(a.P)-boundary)*(above?1:-1);
            foreach(var b in polygon){double db=(coordinate(b.P)-boundary)*(above?1:-1);if((da>=0)!=(db>=0))result.Add(Lerp(a,b,da/(da-db)));if(db>=0)result.Add(b);a=b;da=db;}return result;
        }
        public static SurfaceMesh LinearGradient(SurfaceMesh mesh,Func<Vector3D,double> coordinate,Func<double,Vector4> palette,int bands,bool repeat=false)
        { using (GeometryWork.Begin()) return LinearGradientCore(mesh,coordinate,palette,bands,repeat); }
        static SurfaceMesh LinearGradientCore(SurfaceMesh mesh,Func<Vector3D,double> coordinate,Func<double,Vector4> palette,int bands,bool repeat)
        {
            if(coordinate==null||palette==null||bands<1||bands>64)throw new ArgumentException("Linear gradient requires a coordinate, palette and 1–64 bands.");
            var builder=new Builder{Textured=mesh.UV!=null};var g=mesh.Geometry;
            for(int i=0;i<g.Triangles.Length;i+=3)
            {
                var polygon=new List<Vertex>{At(mesh,g.Triangles[i]),At(mesh,g.Triangles[i+1]),At(mesh,g.Triangles[i+2])};
                double min=double.MaxValue,max=double.MinValue;foreach(var p in polygon){double t=coordinate(p.P);if(!Geometry.Finite(t))throw new ArgumentException("Nonfinite gradient coordinate.");min=Math.Min(min,t);max=Math.Max(max,t);}
                float alpha=mesh.Colors[i/3].W;
                if(max-min<1e-12){PaintPolygon(builder,polygon,palette((min+max)/2),alpha);continue;}
                if(!repeat)
                {
                    if(min<0)PaintPolygon(builder,ClipScalar(polygon,coordinate,0,false),palette(0),alpha);
                    if(max>1)PaintPolygon(builder,ClipScalar(polygon,coordinate,1,true),palette(1),alpha);
                    if(max<=0||min>=1)continue;min=Math.Max(0,min);max=Math.Min(1,max);
                }
                int first,last;BandRange(min,max,bands,repeat,out first,out last);
                for(int band=first;band<=last;band++)
                {
                    var piece=ClipScalar(polygon,coordinate,(double)band/bands,true);piece=ClipScalar(piece,coordinate,(double)(band+1)/bands,false);
                    PaintPolygon(builder,piece,palette((band+0.5)/bands),alpha);
                }
            }
            for(int i=0;i<g.Edges.Length;i+=2)
            {
                var a=At(mesh,g.Edges[i]);var b=At(mesh,g.Edges[i+1]);double ta=coordinate(a.P),tb=coordinate(b.P);if(!Geometry.Finite(ta)||!Geometry.Finite(tb))throw new ArgumentException("Nonfinite gradient coordinate.");
                if(Math.Abs(ta-tb)<1e-12){var color=palette((ta+tb)/2);color.W*=mesh.EdgeColors[i/2].W;builder.Line(a,b,color);continue;}
                double min=Math.Min(ta,tb),max=Math.Max(ta,tb);
                if(!repeat)
                {
                    if(min<0)PaintLine(builder,a,b,ta,tb,double.NegativeInfinity,0,palette(0),mesh.EdgeColors[i/2].W);
                    if(max>1)PaintLine(builder,a,b,ta,tb,1,double.PositiveInfinity,palette(1),mesh.EdgeColors[i/2].W);
                    if(max<=0||min>=1)continue;min=Math.Max(0,min);max=Math.Min(1,max);
                }
                int first,last;BandRange(min,max,bands,repeat,out first,out last);
                for(int band=first;band<=last;band++)PaintLine(builder,a,b,ta,tb,(double)band/bands,(double)(band+1)/bands,palette((band+0.5)/bands),mesh.EdgeColors[i/2].W);
            }
            return builder.Finish();
        }
        static void BandRange(double min,double max,int bands,bool repeat,out int first,out int last)
        {
            if(repeat&&(Math.Abs(min*bands)>1e9||Math.Abs(max*bands)>1e9||(max-min)*bands>128))throw new ArgumentException("Repeated gradient exceeds the band work budget.");
            first=(int)Math.Floor(min*bands);last=(int)Math.Floor(max*bands);
            if(!repeat){first=Math.Max(0,Math.Min(bands-1,first));last=Math.Max(0,Math.Min(bands-1,last));}
        }
        static void PaintPolygon(Builder b,List<Vertex> polygon,Vector4 color,float alpha)
        {color.W*=alpha;for(int i=1;i+1<polygon.Count;i++)b.Triangle(polygon[0],polygon[i],polygon[i+1],color);}
        static void PaintLine(Builder builder,Vertex a,Vertex b,double ta,double tb,double low,double high,Vector4 color,float alpha)
        {
            GeometryWork.Charge();double start=0,end=1,delta=tb-ta;
            if(delta>0){start=Math.Max(start,(low-ta)/delta);end=Math.Min(end,(high-ta)/delta);}
            else{start=Math.Max(start,(high-ta)/delta);end=Math.Min(end,(low-ta)/delta);}
            if(end<=start)return;color.W*=alpha;builder.Line(Lerp(a,b,start),Lerp(a,b,end),color);
        }
        public static SurfaceMesh Solid(Geometry geometry,Vector4 fill,Vector4 line,Vector4[] colors=null,Vector4[] edgeColors=null,Vector2[] uv=null)
        {var c=colors??new Vector4[geometry.Triangles.Length/3];if(colors==null)for(int i=0;i<c.Length;i++)c[i]=fill;var e=edgeColors??new Vector4[geometry.Edges.Length/2];if(edgeColors==null)for(int i=0;i<e.Length;i++)e[i]=line;var mesh=new SurfaceMesh{Geometry=geometry,Colors=c,EdgeColors=e,UV=uv};ValidateAttributes(mesh);return mesh;}
        // Validate once when admitting a mesh, rather than in the per-frame renderer.
        public static void ValidateAttributes(SurfaceMesh mesh)
        {
            if(mesh==null||mesh.Geometry==null)throw new ArgumentException("Missing display geometry.");
            var g=mesh.Geometry;
            if(g.Triangles.Length%3!=0||g.Edges.Length%2!=0||mesh.Colors==null||mesh.Colors.Length!=g.Triangles.Length/3||mesh.EdgeColors==null||mesh.EdgeColors.Length!=g.Edges.Length/2||mesh.UV!=null&&mesh.UV.Length!=g.Points.Length)
                throw new ArgumentException("Display mesh attributes must match its topology.");
            foreach(int i in g.Triangles)if(i<0||i>=g.Points.Length)throw new ArgumentException("Invalid display triangle index.");
            foreach(int i in g.Edges)if(i<0||i>=g.Points.Length)throw new ArgumentException("Invalid display edge index.");
        }
        public static SurfaceMesh Gradient(SurfaceMesh mesh,Func<Vector3D,Vector4> sample,int resolution)
        { using (GeometryWork.Begin()) return GradientCore(mesh,sample,resolution); }
        static SurfaceMesh GradientCore(SurfaceMesh mesh,Func<Vector3D,Vector4> sample,int resolution)
        {
            if(sample==null||resolution<1||resolution>8)throw new ArgumentException("Gradient resolution must be 1–8.");
            var g=mesh.Geometry;var builder=new Builder{Textured=mesh.UV!=null};
            for(int i=0;i<g.Triangles.Length;i+=3)
            {
                var a=At(mesh,g.Triangles[i]);var b=At(mesh,g.Triangles[i+1]);var c=At(mesh,g.Triangles[i+2]);
                for(int row=0;row<resolution;row++)for(int column=0;column<resolution-row;column++)
                {
                    var p=Bary(a,b,c,(double)row/resolution,(double)column/resolution);
                    var q=Bary(a,b,c,(double)(row+1)/resolution,(double)column/resolution);
                    var r=Bary(a,b,c,(double)row/resolution,(double)(column+1)/resolution);
                    AddGradient(builder,p,q,r,sample,mesh.Colors[i/3].W);
                    if(row+column+1<resolution)AddGradient(builder,q,Bary(a,b,c,(double)(row+1)/resolution,(double)(column+1)/resolution),r,sample,mesh.Colors[i/3].W);
                }
            }
            for(int i=0;i<g.Edges.Length;i+=2)for(int j=0;j<resolution;j++)
            {var a=Lerp(At(mesh,g.Edges[i]),At(mesh,g.Edges[i+1]),(double)j/resolution);var b=Lerp(At(mesh,g.Edges[i]),At(mesh,g.Edges[i+1]),(double)(j+1)/resolution);var color=sample((a.P+b.P)/2);color.W*=mesh.EdgeColors[i/2].W;builder.Line(a,b,color);}
            return builder.Finish();
        }
        static Vertex Bary(Vertex a,Vertex b,Vertex c,double u,double v){return new Vertex{P=a.P+(b.P-a.P)*u+(c.P-a.P)*v,UV=a.UV+(b.UV-a.UV)*(float)u+(c.UV-a.UV)*(float)v};}
        static void AddGradient(Builder b,Vertex a,Vertex c,Vertex d,Func<Vector3D,Vector4> sample,float alpha){var color=sample((a.P+c.P+d.P)/3);color.W*=alpha;b.Triangle(a,c,d,color);}
        public static Vector4 Mix(Vector4 a,Vector4 b,double t){t=Math.Max(0,Math.Min(1,t));return a+(b-a)*(float)t;}
        public static Geometry Text(string text,double height,string anchor="middle")
        { return VectorFont.Text(text,height,anchor); }
    }
}
