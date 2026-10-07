using System;
using VRageMath;

namespace HDRClientRenderer
{
    // Numeric publication proof only; no renderer/resource ownership lives here.
    // A primary billboard's world-cameraPosition input is mapped through the
    // full entry->exit affine transport, then the matching capture projection.
    // Its ordinary primary geometry continues to supply local depth/occlusion.
    internal sealed class PortalProjectiveSampling
    {
        internal const int ConstantBytes=80;
        internal readonly MatrixD PrimaryRelativeToCaptureClip;
        internal readonly long CaptureGeneration,Epoch,ViewFrame;
        internal readonly int ImageWidth,ImageHeight;
        internal readonly float Saturation,Brightness;
        readonly PortalRayMapSpec capturedSpec;
        PortalProjectiveSampling(PortalRayMapSpec spec,MatrixD matrix,long viewFrame,int width,int height,float saturation,float brightness)
        {capturedSpec=spec;PrimaryRelativeToCaptureClip=matrix;CaptureGeneration=spec.Generation;Epoch=spec.Epoch;ViewFrame=viewFrame;ImageWidth=width;ImageHeight=height;Saturation=saturation;Brightness=brightness;}
        internal static bool TryCreate(PortalRayMapSpec spec,PortalPrimaryView primary,int width,int height,float saturation,float brightness,out PortalProjectiveSampling result)
        {
            result=null;MatrixD matrix;
            if(!Eligible(spec)||primary==null||!primary.Valid||primary.Epoch!=spec.Epoch||primary.Viewer!=spec.ViewerPose||
                !PortalResolution.IsBucket(width)||height!=width||!PortalProjection.Finite(saturation)||!PortalProjection.Finite(brightness)||saturation<0||saturation>2||brightness<0||brightness>4||
                !TryMatrix(spec,primary.Viewer.Translation,out matrix))return false;
            result=new PortalProjectiveSampling(spec,matrix,primary.Frame,width,height,saturation,brightness);return true;
        }
        internal static bool Eligible(PortalRayMapSpec spec)
        {
            if(spec==null||spec.Generation<=0||spec.Entry.Kind!=PortalSurfaceKind.Plane||spec.Exit.Kind!=PortalSurfaceKind.Plane||spec.Transport!=PortalRayTransport.Differential||spec.ImageProjection!=PortalImageProjection.Perspective)return false;
            // The raw exterior image is sufficient only when each exit point
            // starts within the convex capture shell. Otherwise a ray can need
            // an additional start-distance rejection beyond its shell cutoff.
            var inverse=MatrixD.Transpose(spec.Shell.Pose.GetOrientation());var radii=spec.Shell.Extent;
            PortalSurfaceSample a,b;if(!spec.Entry.Evaluate(.5,.5,0,out a)||!spec.Exit.Evaluate(.5,.5,0,out b))return false;
            var captureInverse=MatrixD.Transpose(spec.CapturePose.GetOrientation());
            for(int v=0;v<=1;v++)for(int u=0;u<=1;u++)
            {
                PortalSurfaceSample point;if(!spec.Exit.Evaluate(u,v,0,out point))return false;
                var local=Vector3D.TransformNormal(point.Point-spec.Shell.Pose.Translation,inverse);
                double scaled=local.X*local.X/(radii.X*radii.X)+local.Y*local.Y/(radii.Y*radii.Y)+local.Z*local.Z/(radii.Z*radii.Z);
                if(!PortalProjection.Finite(scaled)||scaled>=1-1e-8)return false;
                PortalSurfaceSample entry;Vector3D direction;if(!spec.Entry.Evaluate(u,v,0,out entry)||!PortalRayMapping.Differential(entry.Point-spec.ViewerPose.Translation,a,b,spec.NormalScale,out direction)||direction.LengthSquared()<1e-20)return false;
                var origin=Vector3D.TransformNormal(point.Point-spec.CapturePose.Translation,captureInverse);direction=Vector3D.TransformNormal(Vector3D.Normalize(direction),captureInverse);
                if(Vector3D.Cross(origin,direction).Length()>Math.Max(1e-5,Math.Max(radii.X,Math.Max(radii.Y,radii.Z))*1e-6)||Vector3D.Dot(origin,direction)<=0)return false;
            }
            return true;
        }
        internal bool Matches(long captureGeneration,long epoch,long capturedViewFrame)
        {return CaptureGeneration==captureGeneration&&Epoch==epoch&&ViewFrame==capturedViewFrame;}
        internal bool ContainsEntryPoint(Vector3D primaryRelativePoint,PortalPrimaryView current)
        {
            if(current==null||!current.Valid||current.Epoch!=Epoch||current.Frame<ViewFrame||!PortalProjection.Finite(primaryRelativePoint))return false;
            var entry=capturedSpec.Entry;var offset=current.Viewer.Translation-entry.Pose.Translation;
            var local=Vector3D.TransformNormal(primaryRelativePoint+offset,MatrixD.Transpose(entry.Pose.GetOrientation()));
            // Native vertices are camera-relative floats. Bound their arithmetic
            // error, and fail closed when that bound is too large to identify a
            // small entry patch. Never admit a broad world-space plane slab.
            double scale=Math.Max(primaryRelativePoint.Length(),offset.Length())+Math.Max(entry.Extent.X,entry.Extent.Y);
            double tolerance=Math.Max(1e-4,8*1.1920928955078125e-7*scale);
            if(!PortalProjection.Finite(local)||tolerance>Math.Min(entry.Extent.X,entry.Extent.Y)*.001)return false;
            return Math.Abs(local.Z)<=tolerance&&Math.Abs(local.X)<=entry.Extent.X*.5+tolerance&&Math.Abs(local.Y)<=entry.Extent.Y*.5+tolerance;
        }
        // Reusing a completed image does not create a new captured view. Rebase
        // its coordinates when the primary camera moves, retaining all image
        // identity fields. This cannot correct prior-eye translation parallax.
        internal bool TryRebase(PortalPrimaryView primary,out PortalProjectiveSampling result)
        {
            result=null;MatrixD matrix;if(primary==null||!primary.Valid||primary.Epoch!=Epoch||primary.Frame<ViewFrame||!TryMatrix(capturedSpec,primary.Viewer.Translation,out matrix))return false;
            result=new PortalProjectiveSampling(capturedSpec,matrix,ViewFrame,ImageWidth,ImageHeight,Saturation,Brightness);return true;
        }
        internal static bool TryMatrix(PortalRayMapSpec spec,Vector3D primaryEye,out MatrixD matrix)
        {
            matrix=default(MatrixD);if(spec==null||!PortalProjection.Finite(primaryEye)||spec.Entry.Kind!=PortalSurfaceKind.Plane||spec.Exit.Kind!=PortalSurfaceKind.Plane||
                spec.Transport!=PortalRayTransport.Differential||spec.ImageProjection!=PortalImageProjection.Perspective)return false;
            PortalSurfaceSample entry,exit;if(!spec.Entry.Evaluate(0,0,spec.EntryChart,out entry)||!spec.Exit.Evaluate(0,0,spec.ExitChart,out exit))return false;
            Vector3D right,up,backward;
            if(!PortalRayMapping.Differential(Vector3D.UnitX,entry,exit,spec.NormalScale,out right)||!PortalRayMapping.Differential(Vector3D.UnitY,entry,exit,spec.NormalScale,out up)||
                !PortalRayMapping.Differential(Vector3D.UnitZ,entry,exit,spec.NormalScale,out backward))return false;
            var transport=MatrixD.Identity;transport.Right=right;transport.Up=up;transport.Backward=backward;
            // Subtract double world positions before uploading float matrices.
            // Omitting transport incorrectly samples translated/rotated exits.
            var sourceOffset=primaryEye-entry.Point;var targetOffset=exit.Point-spec.CapturePose.Translation;
            matrix=MatrixD.CreateTranslation(sourceOffset)*transport*MatrixD.CreateTranslation(targetOffset)*MatrixD.Transpose(spec.CapturePose.GetOrientation())*spec.Projection;
            if(!PortalProjection.Finite(matrix)||Math.Abs(matrix.Determinant())<1e-18)return false;
            foreach(double value in Values(matrix))if(Math.Abs(value)>1e6)return false;
            return true;
        }
        internal bool TryImageUv(Vector3D primaryRelativePoint,long generation,long epoch,long capturedFrame,out Vector2D uv)
        {
            uv=default(Vector2D);if(!Matches(generation,epoch,capturedFrame)||!PortalProjection.Finite(primaryRelativePoint))return false;
            var clip=Vector4D.Transform(new Vector4D(primaryRelativePoint,1),PrimaryRelativeToCaptureClip);
            if(!PortalProjection.Finite(clip)||clip.W<=1e-8)return false;
            uv=new Vector2D(.5+.5*clip.X/clip.W,.5-.5*clip.Y/clip.W);return PortalSurfaceMap.UnitUv(uv.X,uv.Y);
        }
        internal float[] CopyConstants()
        {
            var values=Values(PrimaryRelativeToCaptureClip);var result=new float[20];for(int i=0;i<16;i++)result[i]=(float)values[i];
            result[16]=Saturation;result[17]=Brightness;result[18]=ImageWidth;result[19]=ImageHeight;return result;
        }
        static double[] Values(MatrixD m)
        {return new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};}
    }
}
