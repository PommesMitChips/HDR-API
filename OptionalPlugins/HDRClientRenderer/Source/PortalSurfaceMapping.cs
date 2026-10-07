using System;
using VRageMath;

namespace HDRClientRenderer
{
    internal enum PortalSurfaceKind { Plane, Ellipsoid, UvMesh }
    internal struct PortalSurfaceSample
    {
        internal readonly Vector3D Point, Du, Dv, Normal;
        internal PortalSurfaceSample(Vector3D point,Vector3D du,Vector3D dv)
        {Point=point;Du=du;Dv=dv;Normal=Vector3D.Normalize(Vector3D.Cross(du,dv));}
        internal PortalSurfaceSample(Vector3D point,Vector3D du,Vector3D dv,Vector3D normal)
        {Point=point;Du=du;Dv=dv;Normal=Vector3D.Normalize(normal);}
    }

    // Local immutable declaration proposal. A mesh chart is its triangle index:
    // overlapping UV islands retain independent mappings, rather than picking
    // whichever triangle happens to be visited first. No network/API is installed.
    internal sealed class PortalSurfaceMap
    {
        internal const int MaxVertices=2048,MaxTriangles=4096;
        internal readonly PortalSurfaceKind Kind;
        internal readonly MatrixD Pose;
        internal readonly Vector3D Extent;
        internal readonly bool Inward;
        readonly Vector3D[] points;
        readonly Vector2D[] uv;
        readonly int[] indices;
        internal int ChartCount {get{return Kind==PortalSurfaceKind.UvMesh?indices.Length/3:1;}}
        internal bool ChartCentre(int chart,out double u,out double v)
        {
            u=v=.5;if(chart<0||chart>=ChartCount)return false;if(Kind!=PortalSurfaceKind.UvMesh)return true;
            int at=chart*3;var centre=(uv[indices[at]]+uv[indices[at+1]]+uv[indices[at+2]])/3;u=centre.X;v=centre.Y;return true;
        }
        PortalSurfaceMap(PortalSurfaceKind kind,MatrixD pose,Vector3D extent,Vector3D[] p,Vector2D[] tex,int[] triangles,bool inward=true)
        {Kind=kind;Pose=pose;Extent=extent;points=p;uv=tex;indices=triangles;Inward=inward;}
        internal static bool TryPlane(MatrixD pose,double width,double height,out PortalSurfaceMap result)
        {
            result=null;if(!PortalProjection.Rigid(pose)||!Bound(width,.1,500)||!Bound(height,.1,500))return false;
            result=new PortalSurfaceMap(PortalSurfaceKind.Plane,pose,new Vector3D(width,height,0),null,null,null);return true;
        }
        internal static bool TryEllipsoid(MatrixD pose,Vector3D radii,out PortalSurfaceMap result)
        {return TryEllipsoid(pose,radii,true,out result);}
        internal static bool TryEllipsoid(MatrixD pose,Vector3D radii,bool inward,out PortalSurfaceMap result)
        {
            result=null;if(!PortalProjection.Rigid(pose)||!Bound(radii.X,.05,250)||!Bound(radii.Y,.05,250)||!Bound(radii.Z,.05,250))return false;
            result=new PortalSurfaceMap(PortalSurfaceKind.Ellipsoid,pose,radii,null,null,null,inward);return true;
        }
        internal static bool TryMesh(MatrixD pose,Vector3D[] p,Vector2D[] tex,int[] triangles,out PortalSurfaceMap result)
        {
            result=null;if(!PortalProjection.Rigid(pose)||p==null||tex==null||triangles==null||p.Length<3||p.Length>MaxVertices||tex.Length!=p.Length||triangles.Length<3||triangles.Length%3!=0||triangles.Length/3>MaxTriangles)return false;
            // Copy before validation; later caller mutation cannot change a frame.
            p=(Vector3D[])p.Clone();tex=(Vector2D[])tex.Clone();triangles=(int[])triangles.Clone();
            for(int i=0;i<p.Length;i++)if(!PortalProjection.Finite(p[i])||p[i].LengthSquared()>250*250||!UnitUv(tex[i].X,tex[i].Y))return false;
            for(int t=0;t<triangles.Length;t+=3)
            {
                int a=triangles[t],b=triangles[t+1],c=triangles[t+2];if(a<0||b<0||c<0||a>=p.Length||b>=p.Length||c>=p.Length)return false;
                var e1=p[b]-p[a];var e2=p[c]-p[a];var s=tex[b]-tex[a];var q=tex[c]-tex[a];
                double det=s.X*q.Y-s.Y*q.X;
                if(!PortalProjection.Finite(det)||Math.Abs(det)<1e-10||!Independent(e1,e2))return false;
            }
            result=new PortalSurfaceMap(PortalSurfaceKind.UvMesh,pose,Vector3D.Zero,p,tex,triangles);return true;
        }
        internal bool Evaluate(double u,double v,int chart,out PortalSurfaceSample sample)
        {
            sample=default(PortalSurfaceSample);if(!UnitUv(u,v)||chart<0||chart>=ChartCount)return false;
            Vector3D p,du,dv,normal=Vector3D.Zero;
            if(Kind==PortalSurfaceKind.Plane)
            {p=new Vector3D((u-.5)*Extent.X,(.5-v)*Extent.Y,0);du=Vector3D.UnitX*Extent.X;dv=-Vector3D.UnitY*Extent.Y;}
            else if(Kind==PortalSurfaceKind.Ellipsoid)
            {
                double lon=(u-.5)*2*Math.PI,lat=(.5-v)*Math.PI,cl=Math.Cos(lon),sl=Math.Sin(lon),ct=Math.Cos(lat),st=Math.Sin(lat);
                double mirror=Inward?-1:1;var ray=new Vector3D(mirror*sl*ct,st,cl*ct);
                var rayDu=new Vector3D(mirror*cl*ct,0,-sl*ct)*(2*Math.PI);
                var rayDv=new Vector3D(mirror*sl*st,-ct,cl*st)*Math.PI;
                var weighted=new Vector3D(ray.X/(Extent.X*Extent.X),ray.Y/(Extent.Y*Extent.Y),ray.Z/(Extent.Z*Extent.Z));
                double k=Math.Sqrt(Vector3D.Dot(ray,weighted));p=ray/k;
                du=rayDu/k-ray*(Vector3D.Dot(rayDu,weighted)/(k*k*k));dv=rayDv/k-ray*(Vector3D.Dot(rayDv,weighted)/(k*k*k));
                normal=new Vector3D(p.X/(Extent.X*Extent.X),p.Y/(Extent.Y*Extent.Y),p.Z/(Extent.Z*Extent.Z));
            }
            else
            {
                int t=chart*3,a=indices[t],b=indices[t+1],c=indices[t+2];var s=uv[b]-uv[a];var q=uv[c]-uv[a];var d=new Vector2D(u,v)-uv[a];double det=s.X*q.Y-s.Y*q.X;
                double wb=(d.X*q.Y-d.Y*q.X)/det,wc=(s.X*d.Y-s.Y*d.X)/det;
                if(wb< -1e-10||wc< -1e-10||wb+wc>1+1e-10)return false;
                var e1=points[b]-points[a];var e2=points[c]-points[a];p=points[a]+wb*e1+wc*e2;
                du=(e1*q.Y-e2*s.Y)/det;dv=(-e1*q.X+e2*s.X)/det;
                // Mirrored texture UVs do not reverse the authored physical side.
                normal=Vector3D.Cross(e1,e2);
            }
            if(!Independent(du,dv))return false; // Pole charts have no invertible differential.
            if(Kind==PortalSurfaceKind.Plane)normal=Vector3D.Cross(du,dv);
            var orientation=Pose.GetOrientation();sample=new PortalSurfaceSample(Pose.Translation+Vector3D.TransformNormal(p,orientation),Vector3D.TransformNormal(du,orientation),Vector3D.TransformNormal(dv,orientation),Vector3D.TransformNormal(normal,orientation));
            return PortalProjection.Finite(sample.Point)&&PortalProjection.Finite(sample.Normal);
        }
        internal bool FirstHit(Vector3D origin,Vector3D direction,out double distance)
        {
            distance=0;if(!PortalProjection.Finite(origin)||!PortalProjection.Finite(direction)||Math.Abs(direction.LengthSquared()-1)>1e-6)return false;
            var inverse=MatrixD.Transpose(Pose.GetOrientation());var o=Vector3D.TransformNormal(origin-Pose.Translation,inverse);var d=Vector3D.TransformNormal(direction,inverse);
            if(Kind==PortalSurfaceKind.Ellipsoid)
            {
                double far;return PortalRayMapping.EllipsoidRoots(o,d,Extent,out distance,out far)&&(distance>1e-8||(distance=far)>1e-8);
            }
            if(Kind==PortalSurfaceKind.Plane)
            {
                if(Math.Abs(d.Z)<1e-12)return false;distance=-o.Z/d.Z;var hit=o+d*distance;
                return distance>1e-8&&Math.Abs(hit.X)<=Extent.X*.5+1e-9&&Math.Abs(hit.Y)<=Extent.Y*.5+1e-9;
            }
            double closest=double.PositiveInfinity;
            for(int t=0;t<indices.Length;t+=3)
            {
                var a=points[indices[t]];var e1=points[indices[t+1]]-a;var e2=points[indices[t+2]]-a;var cross=Vector3D.Cross(d,e2);double det=Vector3D.Dot(e1,cross);
                if(Math.Abs(det)<1e-12)continue;var offset=o-a;double wb=Vector3D.Dot(offset,cross)/det;var q=Vector3D.Cross(offset,e1);double wc=Vector3D.Dot(d,q)/det;
                double hit=Vector3D.Dot(e2,q)/det;if(wb>=-1e-10&&wc>=-1e-10&&wb+wc<=1+1e-10&&hit>1e-8)closest=Math.Min(closest,hit);
            }
            if(double.IsInfinity(closest))return false;distance=closest;return true;
        }
        internal static bool UnitUv(double u,double v){return Bound(u,0,1)&&Bound(v,0,1);}
        static bool Bound(double n,double min,double max){return PortalProjection.Finite(n)&&n>=min&&n<=max;}
        internal static bool Independent(Vector3D a,Vector3D b)
        {double lengths=a.LengthSquared()*b.LengthSquared();return PortalProjection.Finite(a)&&PortalProjection.Finite(b)&&PortalProjection.Finite(lengths)&&lengths>1e-20&&Vector3D.Cross(a,b).LengthSquared()>lengths*1e-12;}
    }
}
