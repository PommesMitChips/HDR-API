using System;
using VRageMath;

namespace HDRClientRenderer
{
    // Two private RGBA32_FLOAT textures, uploaded with point/Load sampling. Each
    // texel belongs to one evaluated UV/chart and one viewer/capture generation.
    // Origin.w is validity; Direction.w is entry distance, never remote depth.
    // Native integration must draw entry geometry with its ordinary local depth
    // and bind the same image generation. This packet is not a published source.
    internal sealed class PortalRayPacket
    {
        internal const int MaxPixels=262144,MaxAffinePixels=4194304,MaxIntersectionWork=4194304;
        internal readonly PortalRayMapSpec Spec;
        internal readonly int Width,Height,ValidPixels;
        internal readonly bool IsAffine,CoverageExact;
        internal readonly int OutputWidth,OutputHeight,Gutter;
        internal int TextureWidth{get{return IsAffine?2:Width;}}
        internal int TextureHeight{get{return IsAffine?2:Height;}}
        readonly float[] origins,directions;
        PortalRayPacket(PortalRayMapSpec spec,int width,int height,int valid,float[] o,float[] d,bool affine=false,bool coverageExact=true,int outputWidth=0,int outputHeight=0,int gutter=0)
        {Spec=spec;Width=width;Height=height;ValidPixels=valid;origins=o;directions=d;IsAffine=affine;CoverageExact=coverageExact;OutputWidth=outputWidth>0?outputWidth:width;OutputHeight=outputHeight>0?outputHeight:height;Gutter=gutter;}
        internal PortalRayPacket WithOutput(int width,int height,int gutter)
        {
            if(width<1||height<1||gutter<0||gutter>4||width+2*gutter>2048||height+2*gutter>2048)throw new ArgumentException("Invalid bounded portal active output.");
            return new PortalRayPacket(Spec,Width,Height,ValidPixels,origins,directions,IsAffine,CoverageExact,width,height,gutter);
        }
        internal float[] CopyOrigins(){return (float[])origins.Clone();}
        internal float[] CopyDirections(){return (float[])directions.Clone();}
        internal static bool TryBuild(PortalRayMapSpec spec,int width,int height,out PortalRayPacket result,out string error)
        {
            result=null;error=null;long pixels=(long)width*height;
            if(spec==null||width<1||height<1||width>2048||height>2048||pixels>MaxAffinePixels||pixels*spec.Entry.ChartCount>MaxIntersectionWork)
            {error="Portal ray packet exceeds its pixel or entry intersection budget.";return false;}
            if(TryAffine(spec,width,height,out result))return true;
            if(pixels>MaxPixels){error="Portal high resolution requires a valid affine plane packet; dense surfaces stay within262144 samples.";return false;}
            var o=new float[(int)pixels*4];var d=new float[o.Length];int valid=0;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                PortalMappedRay ray;PortalRaySample sample;
                if(!spec.Ray((x+.5)/width,(y+.5)/height,out ray)||!spec.Sample(ray,out sample)||
                    !UploadVector(ray.CaptureOrigin.X,ray.CaptureOrigin.Y,ray.CaptureOrigin.Z)||!UploadVector(ray.CaptureDirection.X,ray.CaptureDirection.Y,ray.CaptureDirection.Z)||ray.EntryDistance>1e6)continue;
                int at=4*(y*width+x);o[at]=(float)ray.CaptureOrigin.X;o[at+1]=(float)ray.CaptureOrigin.Y;o[at+2]=(float)ray.CaptureOrigin.Z;o[at+3]=1;
                d[at]=(float)ray.CaptureDirection.X;d[at+1]=(float)ray.CaptureDirection.Y;d[at+2]=(float)ray.CaptureDirection.Z;d[at+3]=(float)ray.EntryDistance;valid++;
            }
            if(valid==0){error="Portal mapping has no visible, valid capture rays for this viewer and accuracy policy.";return false;}
            result=new PortalRayPacket(spec,width,height,valid,o,d);return true;
        }
        static bool TryAffine(PortalRayMapSpec spec,int width,int height,out PortalRayPacket result)
        {
            result=null;if(spec.Entry.Kind!=PortalSurfaceKind.Plane||spec.Exit.Kind!=PortalSurfaceKind.Plane||spec.Transport!=PortalRayTransport.Differential)return false;
            PortalSurfaceSample a,b;if(!spec.Entry.Evaluate(0,0,0,out a)||!spec.Exit.Evaluate(0,0,0,out b))return false;
            // An eye lying on the entry plane has no positive first crossing.
            double eyeNormal=Math.Abs(Vector3D.Dot(a.Point-spec.ViewerPose.Translation,a.Normal));if(eyeNormal<=1e-8)return false;
            Vector3D d0,du,dv;
            if(!PortalRayMapping.Differential(a.Point-spec.ViewerPose.Translation,a,b,spec.NormalScale,out d0)||
                !PortalRayMapping.Differential(a.Du,a,b,spec.NormalScale,out du)||!PortalRayMapping.Differential(a.Dv,a,b,spec.NormalScale,out dv))return false;
            var inverse=MatrixD.Transpose(spec.CapturePose.GetOrientation());
            var o0=Vector3D.TransformNormal(b.Point-spec.CapturePose.Translation,inverse);var ou=Vector3D.TransformNormal(b.Du,inverse);var ov=Vector3D.TransformNormal(b.Dv,inverse);
            d0=Vector3D.TransformNormal(d0,inverse);du=Vector3D.TransformNormal(du,inverse);dv=Vector3D.TransformNormal(dv,inverse);
            for(int v=0;v<=1;v++)for(int u=0;u<=1;u++)
            {
                var point=a.Point+u*a.Du+v*a.Dv;var origin=o0+u*ou+v*ov;
                double eyeDistance=Vector3D.Distance(point,spec.ViewerPose.Translation);
                if(eyeDistance>1e6||eyeNormal/eyeDistance<=1e-12||!UploadVector(origin.X,origin.Y,origin.Z))return false;
            }
            var o=new float[16];var d=new float[16];if(!Coefficient(o,0,o0)||!Coefficient(o,4,ou)||!Coefficient(o,8,ov)||!Coefficient(d,0,d0)||!Coefficient(d,4,du)||!Coefficient(d,8,dv))return false;o[3]=1;
            d[3]=(float)Vector3D.Distance(a.Point,spec.ViewerPose.Translation); // Base entry distance; shader never writes remote depth.
            int valid=0;var tested=new System.Collections.Generic.HashSet<int>();
            int[] xs={0,width-1,0,width-1,width/2,Math.Min(width-1,(int)(spec.ProbeU*width))},ys={0,0,height-1,height-1,height/2,Math.Min(height-1,(int)(spec.ProbeV*height))};
            for(int i=0;i<xs.Length;i++)
            {
                int index=ys[i]*width+xs[i];if(!tested.Add(index))continue;PortalRaySample sample;
                if(spec.Sample((xs[i]+.5)/width,(ys[i]+.5)/height,out sample))valid++;
            }
            // Probes are a positive coverage witness, not an all-miss proof.
            // Fall back to bounded dense validation if no probe demonstrates a
            // sample, preserving narrow apertures and existing all-miss behavior.
            if(valid==0)return false;
            bool full=FullPlaneCoverage(spec,b,o0,ou,ov,d0,du,dv);
            result=new PortalRayPacket(spec,width,height,full?width*height:valid,o,d,true,full);return true;
        }
        static bool Coefficient(float[] values,int offset,Vector3D vector)
        {if(!UploadVector(vector.X,vector.Y,vector.Z))return false;values[offset]=(float)vector.X;values[offset+1]=(float)vector.Y;values[offset+2]=(float)vector.Z;return true;}
        static bool FullPlaneCoverage(PortalRayMapSpec spec,PortalSurfaceSample exit,Vector3D o0,Vector3D ou,Vector3D ov,Vector3D d0,Vector3D du,Vector3D dv)
        {
            // Exact source/capture-line identity, a convex entry patch strictly
            // inside the capture ellipsoid, and a convex pinhole frustum imply
            // every logical texel is visible/sampleable. Otherwise ValidPixels
            // is only the conservative lower bound witnessed by the probes.
            if(Vector3D.DistanceSquared(o0,d0)>1e-18||Vector3D.DistanceSquared(ou,du)>1e-18||Vector3D.DistanceSquared(ov,dv)>1e-18)return false;
            if(spec.ImageProjection==PortalImageProjection.Perspective&&(Math.Abs(spec.Projection.M41)>1e-14||Math.Abs(spec.Projection.M42)>1e-14))return false;
            var inverseShell=MatrixD.Transpose(spec.Shell.Pose.GetOrientation());
            for(int v=0;v<=1;v++)for(int u=0;u<=1;u++)
            {
                var world=exit.Point+u*exit.Du+v*exit.Dv;var local=Vector3D.TransformNormal(world-spec.Shell.Pose.Translation,inverseShell);var radii=spec.Shell.Extent;
                double inside=local.X*local.X/(radii.X*radii.X)+local.Y*local.Y/(radii.Y*radii.Y)+local.Z*local.Z/(radii.Z*radii.Z);
                if(!PortalProjection.Finite(inside)||inside>=1-1e-9)return false;
                if(spec.ImageProjection==PortalImageProjection.Perspective)
                {
                    var point=o0+u*ou+v*ov;var clip=Vector4D.Transform(new Vector4D(point,1),spec.Projection);
                    if(!PortalProjection.Finite(clip)||clip.W<=1e-8||Math.Abs(clip.X)>clip.W||Math.Abs(clip.Y)>clip.W)return false;
                }
            }
            return true;
        }
        internal static bool UploadVector(double x,double y,double z)
        {return PortalProjection.Finite(x)&&PortalProjection.Finite(y)&&PortalProjection.Finite(z)&&Math.Abs(x)<=1e6&&Math.Abs(y)<=1e6&&Math.Abs(z)<=1e6;}
    }
}
