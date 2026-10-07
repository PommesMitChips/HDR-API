using System;
using VRageMath;

namespace HoloMap
{
    // Pure viewer-local work. No camera, network, raster, or renderer access belongs here.
    public struct DisplayLodResult
    {
        public readonly bool Visible;
        public readonly Vector2I Resolution;
        public readonly int Level;
        public DisplayLodResult(bool visible, Vector2I resolution, int level)
        { Visible = visible; Resolution = resolution; Level = level; }
    }

    public static class DisplayLod
    {
        public const int MaxPixels = 1048576;
        public const int MaxSide = 2048;
        public const int MinSide = 16;
        const double Epsilon = 1e-8;

        struct Vertex
        {
            public double X, Y, Z, W, U, V;
            public Vertex(double x, double y, double z, double w, double u, double v)
            { X=x; Y=y; Z=z; W=w; U=u; V=v; }
            public static Vertex Between(Vertex a, Vertex b, double t)
            { return new Vertex(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t,
                a.W+(b.W-a.W)*t,a.U+(b.U-a.U)*t,a.V+(b.V-a.V)*t); }
        }

        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        static double Plane(Vertex v, int plane)
        {
            switch(plane)
            {
                case 0: return v.W-Epsilon;
                case 1: return v.Z;
                case 2: return v.W-v.Z;
                case 3: return v.W+v.X;
                case 4: return v.W-v.X;
                case 5: return v.W+v.Y;
                default: return v.W-v.Y;
            }
        }
        static int Clip(Vertex[] source, int count, Vertex[] result, int plane)
        {
            int written=0;
            Vertex previous=source[count-1];double pd=Plane(previous,plane);
            for(int i=0;i<count;i++)
            {
                Vertex current=source[i];double cd=Plane(current,plane);
                bool pin=pd>=0,cin=cd>=0;
                if(pin!=cin)
                {
                    double fraction=pd/(pd-cd);
                    result[written++]=Vertex.Between(previous,current,fraction);
                }
                if(cin)result[written++]=current;
                previous=current;pd=cd;
            }
            return written;
        }
        static Vertex Transform(Vector3D p, Vector2 uv, MatrixD m)
        {
            return new Vertex(p.X*m.M11+p.Y*m.M21+p.Z*m.M31+m.M41,
                p.X*m.M12+p.Y*m.M22+p.Z*m.M32+m.M42,
                p.X*m.M13+p.Y*m.M23+p.Z*m.M33+m.M43,
                p.X*m.M14+p.Y*m.M24+p.Z*m.M34+m.M44,uv.X,uv.Y);
        }
        static bool IsFinite(Vertex v)
        { return Finite(v.X)&&Finite(v.Y)&&Finite(v.Z)&&Finite(v.W)&&Finite(v.U)&&Finite(v.V); }
        static Vector2I BaseResolution(Vector2I requested,bool panorama=false)
        { return BoundResolution(requested,panorama?4096:MaxSide,panorama?4096:MaxSide,panorama?8388608:MaxPixels,panorama); }

        public static Vector2I BoundResolution(Vector2I requested,int maxWidth,int maxHeight,int maxPixels,bool panorama=false)
        {
            if(requested.X<MinSide||requested.Y<MinSide||maxWidth<MinSide||maxHeight<MinSide||maxPixels<MinSide*MinSide)
                throw new ArgumentException("Resolution bounds must accommodate the minimum display size.");
            maxWidth=Math.Min(maxWidth,panorama?4096:MaxSide);maxHeight=Math.Min(maxHeight,panorama?4096:MaxSide);maxPixels=Math.Min(maxPixels,panorama?8388608:MaxPixels);
            double scale=Math.Min(1d,Math.Sqrt((double)maxPixels/((double)requested.X*requested.Y)));
            scale=Math.Min(scale,Math.Min((double)maxWidth/requested.X,(double)maxHeight/requested.Y));
            int width=(int)Math.Max(MinSide,Math.Min(requested.X,Math.Floor(requested.X*scale)));
            int height=(int)Math.Max(MinSide,Math.Min(requested.Y,Math.Floor(requested.Y*scale)));
            while((long)width*height>maxPixels)
            {
                if((double)width/requested.X>=(double)height/requested.Y && width>MinSide)width--;
                else if(height>MinSide)height--;
                else throw new ArgumentException("Requested aspect cannot fit the pixel budget.");
            }
            return new Vector2I(width,height);
        }
        static Vector2I AtLevel(Vector2I basis, int level)
        {
            int divisor=1<<level;
            return new Vector2I(Math.Max(MinSide,basis.X/divisor),Math.Max(MinSide,basis.Y/divisor));
        }
        static double Capacity(Vector2I basis, int level)
        {
            var size=AtLevel(basis,level);
            return Math.Min((double)size.X/basis.X,(double)size.Y/basis.Y);
        }

        public static DisplayLodResult MaximumVisible(Vector2I requested,bool panorama=false)
        {
            if(requested.X<MinSide||requested.Y<MinSide
                ||(double)requested.X/requested.Y>64||(double)requested.Y/requested.X>64)
                throw new ArgumentException("Invalid requested display resolution.");
            return new DisplayLodResult(true,BaseResolution(requested,panorama),0);
        }

        // Bounding-footprint overload for display surfaces without source UVs.
        public static DisplayLodResult Evaluate(Vector3D[] points, int[] triangles,
            MatrixD viewProjection, Vector2I viewport, Vector2I requested, int previousLevel,bool panorama=false)
        { return Evaluate(points,triangles,null,viewProjection,viewport,requested,previousLevel,panorama); }

        // points/triangles form a representative mesh. The matrix takes points from
        // their coordinate space to clip space: view * projection for world points,
        // or world * view * projection for local points. UVs are normalized source
        // coordinates; duplicate seam vertices when a curved surface wraps.
        // The previous level (-1 for first evaluation) supplies 20% LOD hysteresis.
        public static DisplayLodResult Evaluate(Vector3D[] points, int[] triangles, Vector2[] uv,
            MatrixD viewProjection, Vector2I viewport, Vector2I requested, int previousLevel,bool panorama=false)
        {
            if(points==null||triangles==null||points.Length<3||points.Length>2048
                ||uv!=null&&uv.Length!=points.Length||triangles.Length<3||triangles.Length%3!=0||triangles.Length>12288
                ||viewport.X<=0||viewport.Y<=0||requested.X<MinSide||requested.Y<MinSide
                ||(double)requested.X/requested.Y>64||(double)requested.Y/requested.X>64)
                throw new ArgumentException("Invalid bounded display footprint.");
            var basis=BaseResolution(requested,panorama);
            if(previousLevel < -1 || previousLevel>20)throw new ArgumentException("Invalid previous LOD level.");
            var transformed=new Vertex[points.Length];
            for(int i=0;i<points.Length;i++)
            {
                transformed[i]=Transform(points[i],uv==null?Vector2.Zero:uv[i],viewProjection);
                if(!IsFinite(transformed[i]))throw new ArgumentException("Nonfinite display projection.");
            }
            bool visible=false;
            double requiredWidth=0,requiredHeight=0;
            double minX=double.PositiveInfinity,maxX=double.NegativeInfinity;
            double minY=double.PositiveInfinity,maxY=double.NegativeInfinity;
            // Convex clipping adds at most one vertex per plane to a triangle.
            // Reuse the buffers across the whole mesh: no allocation per triangle.
            var clipA=new Vertex[16];var clipB=new Vertex[16];var front=new Vertex[16];
            for(int t=0;t<triangles.Length;t+=3)
            {
                int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
                if(a<0||a>=points.Length||b<0||b>=points.Length||c<0||c>=points.Length)
                    throw new ArgumentException("Display triangle index is outside the mesh.");
                Vertex va=transformed[a],vb=transformed[b],vc=transformed[c];
                bool outside=false,inside=true;
                for(int p=0;p<7;p++)
                {
                    double da=Plane(va,p),db=Plane(vb,p),dc=Plane(vc,p);
                    if(da<0&&db<0&&dc<0){outside=true;break;}
                    if(da<0||db<0||dc<0)inside=false;
                }
                if(outside)continue;
                clipA[0]=va;clipA[1]=vb;clipA[2]=vc;
                Vertex[] read=clipA,write=clipB;
                int count=3;
                // Clip near/far and positive W before division. This footprint retains
                // off-viewport portions, avoiding a severe LOD drop at a screen edge.
                if(!inside)
                    for(int p=0;p<3 && count>=3;p++)
                    {
                        count=Clip(read,count,write,p);
                        Vertex[] swap=read;read=write;write=swap;
                    }
                if(count<3)continue;
                int frontCount=count;Array.Copy(read,front,count);
                if(!inside)
                    for(int p=3;p<7 && count>=3;p++)
                    {
                        count=Clip(read,count,write,p);
                        Vertex[] swap=read;read=write;write=swap;
                    }
                if(count<3)continue;
                double twiceArea=0;
                for(int i=0;i<count;i++)
                {
                    Vertex first=read[i],second=read[(i+1)%count];
                    twiceArea+=(first.X/first.W)*(second.Y/second.W)
                        -(second.X/second.W)*(first.Y/first.W);
                }
                if(Math.Abs(twiceArea)<1e-14)continue;
                visible=true;
                if(uv==null)
                {
                    for(int i=0;i<frontCount;i++)
                    {
                        double sx=(front[i].X/front[i].W+1)*viewport.X*.5;
                        double sy=(front[i].Y/front[i].W+1)*viewport.Y*.5;
                        minX=Math.Min(minX,sx);maxX=Math.Max(maxX,sx);
                        minY=Math.Min(minY,sy);maxY=Math.Max(maxY,sy);
                    }
                    continue;
                }
                // The affine slope of projected UV is a secant, not an upper bound
                // for perspective magnification. The W ratio bounds its endpoint
                // growth across this clipped triangle.
                double minW=double.PositiveInfinity,maxW=0;
                for(int i=0;i<frontCount;i++)
                { minW=Math.Min(minW,front[i].W);maxW=Math.Max(maxW,front[i].W); }
                double perspectiveFactor=maxW/minW;
                for(int i=1;i+1<frontCount;i++)
                {
                    Vertex v0=front[0],v1=front[i],v2=front[i+1];
                    double x0=(v0.X/v0.W+1)*viewport.X*.5,y0=(v0.Y/v0.W+1)*viewport.Y*.5;
                    double x1=(v1.X/v1.W+1)*viewport.X*.5,y1=(v1.Y/v1.W+1)*viewport.Y*.5;
                    double x2=(v2.X/v2.W+1)*viewport.X*.5,y2=(v2.Y/v2.W+1)*viewport.Y*.5;
                    double du1=v1.U-v0.U,dv1=v1.V-v0.V,du2=v2.U-v0.U,dv2=v2.V-v0.V;
                    double det=du1*dv2-du2*dv1;
                    if(Math.Abs(det)<1e-12)
                    {
                        // A visible triangle with collapsed UVs cannot justify downsampling.
                        requiredWidth=basis.X;requiredHeight=basis.Y;continue;
                    }
                    double dx1=x1-x0,dy1=y1-y0,dx2=x2-x0,dy2=y2-y0;
                    double dudx=(dx1*dv2-dx2*dv1)/det,dudy=(dy1*dv2-dy2*dv1)/det;
                    double dvdx=(dx2*du1-dx1*du2)/det,dvdy=(dy2*du1-dy1*du2)/det;
                    requiredWidth=Math.Max(requiredWidth,Math.Sqrt(dudx*dudx+dudy*dudy)*perspectiveFactor);
                    requiredHeight=Math.Max(requiredHeight,Math.Sqrt(dvdx*dvdx+dvdy*dvdy)*perspectiveFactor);
                }
            }
            if(!visible)return new DisplayLodResult(false,Vector2I.Zero,-1);
            if(uv==null){requiredWidth=maxX-minX;requiredHeight=maxY-minY;}
            double demand=Math.Max(requiredWidth/basis.X,requiredHeight/basis.Y);
            if(!Finite(demand))demand=1;
            int maxLevel=0;
            while(maxLevel<20)
            {
                int nextDivisor=1<<(maxLevel+1);
                if(basis.X/nextDivisor<MinSide||basis.Y/nextDivisor<MinSide)break;
                var next=AtLevel(basis,maxLevel+1);
                var current=AtLevel(basis,maxLevel);
                if(next.X==current.X&&next.Y==current.Y)break;
                maxLevel++;
            }
            int level=0;
            while(level<maxLevel && demand<=Capacity(basis,level+1))level++;
            if(previousLevel>=0)
            {
                int prior=Math.Min(previousLevel,maxLevel);
                if(level>prior)
                {
                    level=prior;
                    while(level<maxLevel && demand<.8*Capacity(basis,level+1))level++;
                }
                else if(level<prior)
                {
                    level=prior;
                    while(level>0 && demand>1.2*Capacity(basis,level))level--;
                }
            }
            return new DisplayLodResult(true,AtLevel(basis,level),level);
        }
    }
}
