using System;
using VRageMath;

namespace HDRClientRenderer
{
    // Immutable local snapshot. Network declarations never supply this render pose.
    internal sealed class PortalCaptureSpec
    {
        internal readonly int Epoch, Width, Height;
        internal readonly long Generation;
        internal readonly MatrixD EntryPose, ExitPose, ViewerPose, VirtualPose, Projection;
        internal readonly double EntryRadius, ExitRadius, Scale, BoundaryGuard;
        PortalCaptureSpec(int epoch,long generation,MatrixD entry,double entryRadius,MatrixD exit,double exitRadius,
            MatrixD viewer,MatrixD projection,int width,int height)
        {
            Epoch=epoch;Generation=generation;EntryPose=entry;EntryRadius=entryRadius;ExitPose=exit;ExitRadius=exitRadius;
            ViewerPose=viewer;Projection=projection;Width=width;Height=height;Scale=exitRadius/entryRadius;
            BoundaryGuard=Math.Max(1e-4,1e-6*exitRadius);
            // Subtract the shared world origin in double precision before any
            // rotation; affine inversion multiplies huge translations together.
            var inverse=MatrixD.Transpose(entry.GetOrientation());
            var localPosition=Vector3D.TransformNormal(viewer.Translation-entry.Translation,inverse);
            var position=exit.Translation+Vector3D.TransformNormal(localPosition*Scale,exit.GetOrientation());
            VirtualPose=MatrixD.CreateWorld(position,Vector3D.TransformNormal(Vector3D.TransformNormal(viewer.Forward,inverse),exit.GetOrientation()),Vector3D.TransformNormal(Vector3D.TransformNormal(viewer.Up,inverse),exit.GetOrientation()));
        }
        internal static bool TryCreate(int epoch,long generation,MatrixD entry,double entryRadius,MatrixD exit,double exitRadius,
            MatrixD viewer,MatrixD projection,int width,int height,out PortalCaptureSpec result,out string error)
        {
            result=null;error=null;
            if(epoch<0||generation<0||!PortalProjection.Rigid(entry)||!PortalProjection.Rigid(exit)||!PortalProjection.Rigid(viewer)||
                !PortalProjection.Finite(projection)||!PortalProjection.Positive(entryRadius)||!PortalProjection.Positive(exitRadius)||
                exitRadius<.1||exitRadius>250||exitRadius/entryRadius<1d/64||exitRadius/entryRadius>64||
                width<64||height<64||width>2048||height>2048||(long)width*height>4194304||
                Math.Abs(projection.Determinant())<1e-18||!PortalProjection.ComplementaryProjection(projection))
            {error="Portal requires rigid local frames, bounded positive sphere radii/scale and a finite invertible capture projection.";return false;}
            result=new PortalCaptureSpec(epoch,generation,entry,entryRadius,exit,exitRadius,viewer,projection,width,height);
            if(!PortalProjection.Rigid(result.VirtualPose)){result=null;error="Portal similarity transform exceeded finite camera-relative coordinates.";return false;}return true;
        }
        internal Vector3D MapPoint(Vector3D point)
        {return ExitPose.Translation+Vector3D.TransformNormal(Vector3D.TransformNormal(point-EntryPose.Translation,MatrixD.Transpose(EntryPose.GetOrientation()))*Scale,ExitPose.GetOrientation());}
        internal bool Cutoff(double u,double v,out double nativeDepth)
        {
            nativeDepth=-1;Vector3D ray;
            if(!PortalProjection.ViewRay(Projection,u,v,out ray))return false;
            var direction=Vector3D.Normalize(Vector3D.TransformNormal(ray,VirtualPose));double distance;
            if(!PortalProjection.FarIntersection(ExitPose.Translation-VirtualPose.Translation,direction,ExitRadius,out distance))return false;
            var local=ray*(distance+BoundaryGuard);
            var clip=Vector4D.Transform(new Vector4D(local,1),Projection);
            if(!PortalProjection.Finite(clip)||clip.W<=0)return false;
            nativeDepth=clip.Z/clip.W;if(!PortalProjection.Finite(nativeDepth)||nativeDepth<0)return false;
            // If the sphere crossing precedes the hardware near plane, every
            // rasterized fragment already lies beyond it. Preserve that aperture.
            nativeDepth=Math.Min(1,nativeDepth);return true;
        }
        internal bool EntrySample(Vector3D point,out Vector2D uv)
        {
            uv=Vector2D.Zero;
            if(!PortalProjection.Finite(point))return false;
            var delta=point-ViewerPose.Translation;double distance=delta.Length();if(distance<1e-9)return false;
            var direction=delta/distance;double visibleDistance;
            if(!PortalProjection.EntryIntersection(EntryPose.Translation-ViewerPose.Translation,direction,EntryRadius,out visibleDistance)||
                Math.Abs(distance-visibleDistance)>Math.Max(1e-4,EntryRadius*1e-5))return false;
            var view=Vector3D.TransformNormal(delta,MatrixD.Transpose(ViewerPose.GetOrientation()));
            var clip=Vector4D.Transform(new Vector4D(view,1),Projection);
            if(!PortalProjection.Finite(clip)||clip.W<=0)return false;
            uv=new Vector2D(.5+.5*clip.X/clip.W,.5-.5*clip.Y/clip.W);
            return uv.X>=0&&uv.X<=1&&uv.Y>=0&&uv.Y<=1;
        }
    }
    internal static class PortalProjection
    {
        internal static bool Finite(double n){return !double.IsNaN(n)&&!double.IsInfinity(n);}
        internal static bool Positive(double n){return Finite(n)&&n>0;}
        internal static bool Finite(Vector3D p){return Finite(p.X)&&Finite(p.Y)&&Finite(p.Z);}
        internal static bool Finite(Vector4D p){return Finite(p.X)&&Finite(p.Y)&&Finite(p.Z)&&Finite(p.W);}
        internal static bool Finite(MatrixD m)
        {return Finite(m.M11)&&Finite(m.M12)&&Finite(m.M13)&&Finite(m.M14)&&Finite(m.M21)&&Finite(m.M22)&&Finite(m.M23)&&Finite(m.M24)&&Finite(m.M31)&&Finite(m.M32)&&Finite(m.M33)&&Finite(m.M34)&&Finite(m.M41)&&Finite(m.M42)&&Finite(m.M43)&&Finite(m.M44);}
        internal static bool Rigid(MatrixD m)
        {
            return Finite(m)&&Math.Abs(m.M14)+Math.Abs(m.M24)+Math.Abs(m.M34)<1e-8&&Math.Abs(m.M44-1)<1e-8&&
                Math.Abs(m.Right.LengthSquared()-1)<1e-6&&Math.Abs(m.Up.LengthSquared()-1)<1e-6&&Math.Abs(m.Backward.LengthSquared()-1)<1e-6&&
                Math.Abs(Vector3D.Dot(m.Right,m.Up))+Math.Abs(Vector3D.Dot(m.Right,m.Backward))+Math.Abs(Vector3D.Dot(m.Up,m.Backward))<1e-6&&m.Determinant()>0;
        }
        internal static bool ComplementaryProjection(MatrixD m)
        {return Finite(m)&&Math.Abs(m.M34+1)<1e-6&&Math.Abs(m.M44)<1e-8&&m.M43>0&&m.M33>=0;}
        internal static bool ViewRay(MatrixD projection,double u,double v,out Vector3D ray)
        {
            ray=Vector3D.Zero;if(!Finite(projection)||!Finite(u)||!Finite(v)||u<0||v<0||u>1||v>1)return false;
            var point=Vector4D.Transform(new Vector4D(2*u-1,1-2*v,1,1),MatrixD.Invert(projection));
            if(!Finite(point)||Math.Abs(point.W)<1e-18)return false;
            ray=new Vector3D(point.X,point.Y,point.Z)/point.W;if(!Finite(ray)||ray.LengthSquared()<1e-20)return false;
            ray=Vector3D.Normalize(ray);return ray.Z<0;
        }
        internal static bool FarIntersection(Vector3D centre,Vector3D unitRay,double radius,out double distance)
        {
            double near;return Roots(centre,unitRay,radius,out near,out distance)&&distance>=0;
        }
        internal static bool EntryIntersection(Vector3D centre,Vector3D unitRay,double radius,out double distance)
        {
            double near,far;distance=0;if(!Roots(centre,unitRay,radius,out near,out far)||far<0)return false;
            distance=near>1e-9?near:far;return distance>=0;
        }
        static bool Roots(Vector3D centre,Vector3D unitRay,double radius,out double near,out double far)
        {
            near=far=0;if(!Finite(centre)||!Finite(unitRay)||!Positive(radius)||Math.Abs(unitRay.LengthSquared()-1)>1e-6)return false;
            // Cross-product distance avoids subtracting two huge squared eye
            // distances when a small shell is far along the viewing ray.
            double b=Vector3D.Dot(unitRay,centre),d=radius*radius-Vector3D.Cross(unitRay,centre).LengthSquared();
            if(!Finite(d)||d<0)return false;double root=Math.Sqrt(d);near=b-root;far=b+root;return Finite(near)&&Finite(far);
        }
        internal static bool RigSphere(MatrixD[] opticalPoses,int orientationIndex,double explicitRadius,double margin,double radiusScale,
            out MatrixD frame,out double radius)
        {
            frame=MatrixD.Identity;radius=0;
            if(opticalPoses==null||opticalPoses.Length<1||opticalPoses.Length>6||orientationIndex<0||orientationIndex>=opticalPoses.Length||
                !Finite(explicitRadius)||explicitRadius<0||!Finite(margin)||margin<0||margin>25||!Positive(radiusScale))return false;
            // Sum offsets from one local origin, rather than large world positions.
            Vector3D origin=opticalPoses[0].Translation,offset=Vector3D.Zero;
            foreach(var pose in opticalPoses){if(!Rigid(pose))return false;offset+=pose.Translation-origin;}
            Vector3D centre=origin+offset/opticalPoses.Length;
            foreach(var pose in opticalPoses)radius=Math.Max(radius,Vector3D.Distance(centre,pose.Translation));
            radius=(explicitRadius>0?explicitRadius:Math.Max(.1,radius+margin))*radiusScale;
            if(!Finite(radius)||radius<.1||radius>250)return false;
            frame=opticalPoses[orientationIndex];frame.Translation=centre;return true;
        }
    }
}
