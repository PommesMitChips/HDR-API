using System;
using VRageMath;

namespace HDRClientRenderer
{
    internal enum PortalRayTransport { Differential, StealthWorldDirection }
    internal enum PortalImageProjection { Perspective, Angular }
    internal enum PortalRayAccuracy { ExactCaptureLine, ApproximateShell }
    internal struct PortalMappedRay
    {
        internal readonly Vector3D Origin, Direction, CaptureOrigin, CaptureDirection;
        internal readonly double EntryDistance;
        internal PortalMappedRay(Vector3D origin,Vector3D direction,Vector3D captureOrigin,Vector3D captureDirection,double entryDistance)
        {Origin=origin;Direction=direction;CaptureOrigin=captureOrigin;CaptureDirection=captureDirection;EntryDistance=entryDistance;}
    }
    internal struct PortalRaySample
    {
        internal readonly Vector2D ImageUv;
        internal readonly double ShellDistance, EntryDistance;
        internal readonly bool Exact;
        internal PortalRaySample(Vector2D uv,double shell,double entry,bool exact)
        {ImageUv=uv;ShellDistance=shell;EntryDistance=entry;Exact=exact;}
    }
    // A local, viewer-dependent snapshot. Capture identity/projection must match
    // its image; authorization/epoch publication remain the native owner's job.
    internal sealed class PortalRayMapSpec
    {
        internal readonly int Epoch, EntryChart, ExitChart;
        internal readonly long Generation;
        internal readonly PortalSurfaceMap Entry, Exit, Shell;
        internal readonly MatrixD ViewerPose, CapturePose, Projection;
        internal readonly PortalRayTransport Transport;
        internal readonly PortalImageProjection ImageProjection;
        internal readonly PortalRayAccuracy Accuracy;
        internal readonly double NormalScale;
        internal readonly double ProbeU,ProbeV;
        PortalRayMapSpec(int epoch,long generation,PortalSurfaceMap entry,int entryChart,PortalSurfaceMap exit,int exitChart,
            PortalSurfaceMap shell,MatrixD viewer,MatrixD capture,MatrixD projection,PortalRayTransport transport,PortalImageProjection imageProjection,PortalRayAccuracy accuracy,double normalScale,double probeU=.5,double probeV=.5)
        {Epoch=epoch;Generation=generation;Entry=entry;EntryChart=entryChart;Exit=exit;ExitChart=exitChart;Shell=shell;ViewerPose=viewer;CapturePose=capture;Projection=projection;Transport=transport;ImageProjection=imageProjection;Accuracy=accuracy;NormalScale=normalScale;ProbeU=probeU;ProbeV=probeV;}
        internal bool WithProjection(MatrixD projection,double probeU,double probeV,out PortalRayMapSpec result)
        {
            result=null;if(!PortalProjection.ComplementaryProjection(projection)||Math.Abs(projection.Determinant())<1e-18||!PortalSurfaceMap.UnitUv(probeU,probeV))return false;
            result=new PortalRayMapSpec(Epoch,Generation,Entry,EntryChart,Exit,ExitChart,Shell,ViewerPose,CapturePose,projection,Transport,ImageProjection,Accuracy,NormalScale,probeU,probeV);return true;
        }
        internal static bool TryCreate(int epoch,long generation,PortalSurfaceMap entry,int entryChart,PortalSurfaceMap exit,int exitChart,
            PortalSurfaceMap shell,MatrixD viewer,MatrixD capture,MatrixD projection,PortalRayTransport transport,PortalImageProjection imageProjection,PortalRayAccuracy accuracy,double normalScale,out PortalRayMapSpec result)
        {
            result=null;
            if(epoch<0||generation<0||entry==null||exit==null||shell==null||shell.Kind!=PortalSurfaceKind.Ellipsoid||entryChart<0||entryChart>=entry.ChartCount||exitChart<0||exitChart>=exit.ChartCount||
                !PortalProjection.Rigid(viewer)||!PortalProjection.Rigid(capture)||!PortalProjection.Finite(normalScale)||Math.Abs(normalScale)<1d/64||Math.Abs(normalScale)>64||
                !Enum.IsDefined(typeof(PortalRayTransport),transport)||!Enum.IsDefined(typeof(PortalImageProjection),imageProjection)||!Enum.IsDefined(typeof(PortalRayAccuracy),accuracy)||
                imageProjection==PortalImageProjection.Perspective&&(!PortalProjection.ComplementaryProjection(projection)||Math.Abs(projection.Determinant())<1e-18))return false;
            // Angular captures have no perspective matrix, but still reject an
            // accidentally nonfinite declaration instead of publishing poison.
            if(!PortalProjection.Finite(projection))return false;
            result=new PortalRayMapSpec(epoch,generation,entry,entryChart,exit,exitChart,shell,viewer,capture,projection,transport,imageProjection,accuracy,normalScale);return true;
        }
        internal bool Ray(double u,double v,out PortalMappedRay mapped)
        {
            mapped=default(PortalMappedRay);PortalSurfaceSample a,b;if(!Entry.Evaluate(u,v,EntryChart,out a)||!Exit.Evaluate(u,v,ExitChart,out b))return false;
            var delta=a.Point-ViewerPose.Translation;double entryDistance=delta.Length();if(!PortalProjection.Finite(entryDistance)||entryDistance<1e-8)return false;
            var incoming=delta/entryDistance;double visible;
            if(!Entry.FirstHit(ViewerPose.Translation,incoming,out visible)||Math.Abs(visible-entryDistance)>Math.Max(1e-5,entryDistance*1e-6))return false;
            Vector3D outgoing;
            if(Transport==PortalRayTransport.StealthWorldDirection)
            {
                // A stealth skin preserves the physical sight line. Moving its
                // outgoing origin would instead be a direction-preserving portal.
                if(Vector3D.DistanceSquared(a.Point,b.Point)>1e-10)return false;outgoing=incoming;
            }
            else
            {
                if(!PortalRayMapping.Differential(incoming,a,b,NormalScale,out outgoing)||outgoing.LengthSquared()<1e-20)return false;outgoing=Vector3D.Normalize(outgoing);
            }
            var inverse=MatrixD.Transpose(CapturePose.GetOrientation());var origin=Vector3D.TransformNormal(b.Point-CapturePose.Translation,inverse);var direction=Vector3D.TransformNormal(outgoing,inverse);
            if(!PortalProjection.Finite(origin)||!PortalProjection.Finite(direction))return false;
            mapped=new PortalMappedRay(b.Point,outgoing,origin,direction,entryDistance);return true;
        }
        internal bool Sample(double u,double v,out PortalRaySample sample)
        {sample=default(PortalRaySample);PortalMappedRay ray;return Ray(u,v,out ray)&&Sample(ray,out sample);}
        internal bool Cutoff(double u,double v,out double nativeDepth)
        {
            nativeDepth=-1;Vector3D viewRay;
            // Angular images must supply a separate pinhole spec per cubemap
            // face. Angular UV has no single native perspective depth value.
            if(ImageProjection!=PortalImageProjection.Perspective||!PortalProjection.ViewRay(Projection,u,v,out viewRay))return false;
            var direction=Vector3D.TransformNormal(viewRay,CapturePose.GetOrientation());var inverse=MatrixD.Transpose(Shell.Pose.GetOrientation());
            var origin=Vector3D.TransformNormal(CapturePose.Translation-Shell.Pose.Translation,inverse);var localDirection=Vector3D.TransformNormal(direction,inverse);double near,far;
            if(!PortalRayMapping.EllipsoidRoots(origin,localDirection,Shell.Extent,out near,out far)||far<0)return false;
            double guard=Math.Max(1e-4,Math.Max(Shell.Extent.X,Math.Max(Shell.Extent.Y,Shell.Extent.Z))*1e-6);
            var clip=Vector4D.Transform(new Vector4D(viewRay*(far+guard),1),Projection);
            if(!PortalProjection.Finite(clip)||clip.W<=0)return false;double depth=clip.Z/clip.W;
            if(!PortalProjection.Finite(depth)||depth<0)return false;nativeDepth=Math.Min(1,depth);return true;
        }
        internal bool Sample(PortalMappedRay ray,out PortalRaySample sample)
        {
            sample=default(PortalRaySample);var inverse=MatrixD.Transpose(Shell.Pose.GetOrientation());
            var o=Vector3D.TransformNormal(ray.Origin-Shell.Pose.Translation,inverse);var d=Vector3D.TransformNormal(ray.Direction,inverse);double near,far;
            if(!PortalRayMapping.EllipsoidRoots(o,d,Shell.Extent,out near,out far)||far<=1e-8)return false;
            var hit=ray.CaptureOrigin+ray.CaptureDirection*far;double length=hit.Length();if(!PortalProjection.Finite(length)||length<1e-8)return false;
            // Native capture clips through its FAR shell crossing. A target on
            // that camera's entering shell branch samples the wrong exterior,
            // even when finite-shell approximation was explicitly requested.
            var boundary=o+d*far;
            var cameraRay=Vector3D.TransformNormal(Vector3D.TransformNormal(hit/length,CapturePose.GetOrientation()),inverse);
            var normalizedCameraRay=new Vector3D(cameraRay.X/Shell.Extent.X,cameraRay.Y/Shell.Extent.Y,cameraRay.Z/Shell.Extent.Z);
            var normalizedBoundary=new Vector3D(boundary.X/Shell.Extent.X,boundary.Y/Shell.Extent.Y,boundary.Z/Shell.Extent.Z);
            if(Vector3D.Dot(normalizedBoundary,normalizedCameraRay)<-1e-10)return false;
            double lineError=Vector3D.Cross(ray.CaptureOrigin,ray.CaptureDirection).Length();
            double tolerance=Math.Max(1e-5,Math.Max(Shell.Extent.X,Math.Max(Shell.Extent.Y,Shell.Extent.Z))*1e-6);
            bool exact=lineError<=tolerance&&Vector3D.Dot(hit,ray.CaptureDirection)>0;
            if(Accuracy==PortalRayAccuracy.ExactCaptureLine&&!exact)return false;
            Vector2D uv;
            if(ImageProjection==PortalImageProjection.Angular)
            {
                var unit=hit/length;uv=new Vector2D(.5+Math.Atan2(-unit.X,unit.Z)/(2*Math.PI),.5-Math.Asin(Math.Max(-1,Math.Min(1,unit.Y)))/Math.PI);
            }
            else
            {
                var clip=Vector4D.Transform(new Vector4D(hit,1),Projection);if(!PortalProjection.Finite(clip)||clip.W<=1e-8)return false;
                uv=new Vector2D(.5+.5*clip.X/clip.W,.5-.5*clip.Y/clip.W);
            }
            if(!PortalSurfaceMap.UnitUv(uv.X,uv.Y))return false;sample=new PortalRaySample(uv,far,ray.EntryDistance,exact);return true;
        }
    }
    internal static class PortalRayMapping
    {
        // Linear differential extension. Keeping the unnormalized result lets
        // an affine plane packet interpolate raw directions exactly; normalized
        // corner directions would instead bend off-centre viewing rays.
        internal static bool Differential(Vector3D vector,PortalSurfaceSample a,PortalSurfaceSample b,double normalScale,out Vector3D mapped)
        {
            mapped=Vector3D.Zero;double aa=a.Du.LengthSquared(),ab=Vector3D.Dot(a.Du,a.Dv),bb=a.Dv.LengthSquared(),det=aa*bb-ab*ab;
            if(!PortalProjection.Finite(vector)||!PortalProjection.Finite(normalScale)||!PortalProjection.Finite(det)||det<=aa*bb*1e-12)return false;
            double da=Vector3D.Dot(vector,a.Du),db=Vector3D.Dot(vector,a.Dv);
            mapped=((da*bb-db*ab)/det)*b.Du+((db*aa-da*ab)/det)*b.Dv+Vector3D.Dot(vector,a.Normal)*normalScale*b.Normal;return PortalProjection.Finite(mapped);
        }
        // Stable quadratic roots for a rigid-frame, axis-aligned ellipsoid.
        // Origin is relative to its centre; direction is a world-unit vector
        // transformed into that frame, so roots remain distances in metres.
        internal static bool EllipsoidRoots(Vector3D origin,Vector3D direction,Vector3D radii,out double near,out double far)
        {
            near=far=0;if(!PortalProjection.Finite(origin)||!PortalProjection.Finite(direction)||!PortalProjection.Positive(radii.X)||!PortalProjection.Positive(radii.Y)||!PortalProjection.Positive(radii.Z)||Math.Abs(direction.LengthSquared()-1)>1e-6)return false;
            var o=new Vector3D(origin.X/radii.X,origin.Y/radii.Y,origin.Z/radii.Z);var d=new Vector3D(direction.X/radii.X,direction.Y/radii.Y,direction.Z/radii.Z);
            double a=d.LengthSquared(),b=Vector3D.Dot(o,d),c=o.LengthSquared()-1,disc=a-Vector3D.Cross(o,d).LengthSquared();
            if(!PortalProjection.Finite(a)||!PortalProjection.Finite(b)||!PortalProjection.Finite(c)||!PortalProjection.Finite(disc)||a<=0||disc<0)return false;
            double root=Math.Sqrt(disc),q=-b-(b>=0?root:-root);
            if(Math.Abs(q)<1e-30)near=far=-b/a;
            else{near=q/a;far=c/q;if(near>far){double swap=near;near=far;far=swap;}}
            return PortalProjection.Finite(near)&&PortalProjection.Finite(far);
        }
    }
}
