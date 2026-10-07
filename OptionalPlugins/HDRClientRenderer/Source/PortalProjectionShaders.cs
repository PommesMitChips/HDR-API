using System;
using VRageMath;
namespace HDRClientRenderer
{
    internal static class PortalProjectionShaders
    {
        // Private linear R32_FLOAT target, not the sRGB panorama framebuffer.
        // Invalid rays return -1: reversed-Z clip(-1-z) rejects every fragment.
        internal const string Threshold=@"
cbuffer HDRPortalThreshold : register(b1) {
 row_major float4x4 HDRProjection; row_major float4x4 HDRInverseProjection;
 float4 HDRExitCentreRadius; float4 HDRViewportGuard;
};
float __pixel_shader(float4 position:SV_Position,float2 uv:TEXCOORD0):SV_Target0 {
 float2 ndc=float2(2*position.x/HDRViewportGuard.x-1,1-2*position.y/HDRViewportGuard.y);
 float4 view=mul(float4(ndc,1,1),HDRInverseProjection);
 if(abs(view.w)<1e-12) return -1;
 float3 d=normalize(view.xyz/view.w), c=HDRExitCentreRadius.xyz;
 float3 perpendicular=cross(d,c);
 float b=dot(d,c), disc=HDRExitCentreRadius.w*HDRExitCentreRadius.w-dot(perpendicular,perpendicular);
 if(disc<0) return -1;
 float farRoot=b+sqrt(disc);
 if(farRoot<0) return -1;
 float4 clipPoint=mul(float4(d*(max(0,farRoot)+HDRViewportGuard.z),1),HDRProjection);
 if(clipPoint.w<=0) return -1;
 float depth=clipPoint.z/clipPoint.w;
 return depth>=0 ? min(1,depth) : -1;
}";
        // One perspective capture, resampled onto the selected entry branch.
        // Entry points are already expressed in captured viewer-relative axes;
        // no large world-coordinate subtraction is performed in this shader.
        internal const string Composite=@"
Texture2D<float4> HDRPortalImage:register(t0); SamplerState HDRPortalLinear:register(s2);
cbuffer HDRPortalComposite:register(b1) {
 row_major float4x4 HDRProjection;
 float4 HDREntryRight; float4 HDREntryUp; float4 HDREntryBackward;
 float4 HDREntryCentreRadius; float4 HDRPortalSettings;
};
float4 __pixel_shader(float4 position:SV_Position,float2 uv:TEXCOORD0):SV_Target0 {
 float longitude=(uv.x-.5)*6.28318530718, latitude=(.5-uv.y)*3.14159265359;
 float3 local=float3(-sin(longitude)*cos(latitude),sin(latitude),cos(longitude)*cos(latitude));
 float3 p=HDREntryCentreRadius.xyz+HDREntryCentreRadius.w*(local.x*HDREntryRight.xyz+local.y*HDREntryUp.xyz+local.z*HDREntryBackward.xyz);
 float distance=length(p); if(distance<1e-6) return 0;
 float3 d=p/distance,c=HDREntryCentreRadius.xyz;
 float3 perpendicular=cross(d,c);
 float b=dot(d,c),disc=HDREntryCentreRadius.w*HDREntryCentreRadius.w-dot(perpendicular,perpendicular);
 if(disc<0) return 0;
 float root=sqrt(disc),nearRoot=b-root,farRoot=b+root,entry=nearRoot>1e-6?nearRoot:farRoot;
 if(entry<0||abs(entry-distance)>max(1e-4,HDREntryCentreRadius.w*1e-5)) return 0;
 float4 clipPoint=mul(float4(p,1),HDRProjection); if(clipPoint.w<=0) return 0;
 float2 imageUv=float2(.5+.5*clipPoint.x/clipPoint.w,.5-.5*clipPoint.y/clipPoint.w);
 if(any(imageUv<0)||any(imageUv>1)) return 0;
 float3 color=HDRPortalImage.SampleLevel(HDRPortalLinear,imageUv,0).rgb;
 float luma=dot(color,float3(.2126,.7152,.0722));
 return float4(max(0,lerp(luma.xxx,color,HDRPortalSettings.x))*HDRPortalSettings.y,1);
}";
        // Viewer-dependent general surface compositor. Ray textures are private
        // immutable RGBA32_FLOAT packets, generated for this exact UV chart and
        // camera generation. Never interpolate validity or adjacent chart rays.
        // This produces colour/alpha only. Entry mesh rasterization supplies local
        // world depth; writing sampled remote depth would break external occlusion.
        internal const string GeneralComposite=@"
Texture2D<float4> HDRPortalImage:register(t0);
Texture2D<float4> HDRRayOrigins:register(t1);
Texture2D<float4> HDRRayDirections:register(t2);
SamplerState HDRPortalLinear:register(s2);
cbuffer HDRPortalGeneral : register(b1) {
 row_major float4x4 HDRProjection;
 float4 HDRShellCentre;
 float4 HDRShellRight; float4 HDRShellUp; float4 HDRShellBackward;
 float4 HDRGeneralSettings; float4 HDRRayDimensions;
};
float4 __pixel_shader(float4 position:SV_Position,float2 uv:TEXCOORD0):SV_Target0 {
 if(HDRShellRight.w>0&&HDRShellUp.w>0) uv=saturate((position.xy-HDRShellCentre.ww)/float2(HDRShellRight.w,HDRShellUp.w));
 if(any(uv<0)||any(uv>1)) return 0;
 int2 texel=min(int2(uv*HDRRayDimensions.xy),int2(HDRRayDimensions.xy)-1);
 float4 packet,ray;
 if(HDRRayDimensions.w>.5) {
  // Match the point packet's logical texel-centre ray exactly, even when its
  // low/nonsquare information grid is upsampled into a physical512 target.
  float2 sampleUv=(float2(texel)+.5)/HDRRayDimensions.xy;
  float4 o0=HDRRayOrigins.Load(int3(0,0,0)),ou=HDRRayOrigins.Load(int3(1,0,0)),ov=HDRRayOrigins.Load(int3(0,1,0));
  float3 d0=HDRRayDirections.Load(int3(0,0,0)).xyz,du=HDRRayDirections.Load(int3(1,0,0)).xyz,dv=HDRRayDirections.Load(int3(0,1,0)).xyz;
  packet=float4(o0.xyz+sampleUv.x*ou.xyz+sampleUv.y*ov.xyz,o0.w);
  ray=float4(d0+sampleUv.x*du+sampleUv.y*dv,0);
 } else {
  packet=HDRRayOrigins.Load(int3(texel,0));ray=HDRRayDirections.Load(int3(texel,0));
 }
 if(packet.w<.5||!all(isfinite(packet))||!all(isfinite(ray))) return 0;
 float3 direction=normalize(ray.xyz), delta=packet.xyz-HDRShellCentre.xyz;
 float3 o=float3(dot(delta,HDRShellRight.xyz),dot(delta,HDRShellUp.xyz),dot(delta,HDRShellBackward.xyz));
 float3 d=float3(dot(direction,HDRShellRight.xyz),dot(direction,HDRShellUp.xyz),dot(direction,HDRShellBackward.xyz));
 float3 perpendicular=cross(o,d);
 float a=dot(d,d), b=dot(o,d), c=dot(o,o)-1, disc=a-dot(perpendicular,perpendicular);
 if(!isfinite(disc)||disc<0||a<=0) return 0;
 float root=sqrt(disc), q=-b-(b>=0?root:-root);
 float farRoot=abs(q)<1e-20 ? -b/a : max(q/a,c/q);
 if(!isfinite(farRoot)||farRoot<=1e-6) return 0;
 float3 hit=packet.xyz+direction*farRoot;float distance=length(hit);
 if(!isfinite(distance)||distance<1e-6) return 0;
 float3 cameraDirection=hit/distance;
 float3 normalizedCameraDirection=float3(dot(cameraDirection,HDRShellRight.xyz),dot(cameraDirection,HDRShellUp.xyz),dot(cameraDirection,HDRShellBackward.xyz));
 if(dot(o+d*farRoot,normalizedCameraDirection)<-1e-7) return 0;
 if(HDRGeneralSettings.w<.5 && (length(cross(packet.xyz,direction))>HDRRayDimensions.z||dot(hit,direction)<=0)) return 0;
 float2 imageUv;
 if(HDRGeneralSettings.z>.5) {
  float3 unit=hit/distance;
  imageUv=float2(.5+atan2(-unit.x,unit.z)/6.28318530718,.5-asin(clamp(unit.y,-1,1))/3.14159265359);
 } else {
  float4 clipPoint=mul(float4(hit,1),HDRProjection);if(clipPoint.w<=0) return 0;
  imageUv=float2(.5+.5*clipPoint.x/clipPoint.w,.5-.5*clipPoint.y/clipPoint.w);
 }
 if(!all(isfinite(imageUv))||any(imageUv<0)||any(imageUv>1)) return 0;
 uint imageWidth,imageHeight;HDRPortalImage.GetDimensions(imageWidth,imageHeight);
 if(imageWidth==0||imageHeight==0) return 0;
 if(HDRGeneralSettings.z<.5) imageUv=clamp(imageUv,.5/float2(imageWidth,imageHeight),1-.5/float2(imageWidth,imageHeight));
 float3 color=HDRPortalImage.SampleLevel(HDRPortalLinear,imageUv,0).rgb;
 float luma=dot(color,float3(.2126,.7152,.0722));
 return float4(max(0,lerp(luma.xxx,color,HDRGeneralSettings.x))*HDRGeneralSettings.y,1);
}";
        // Same complementary-depth rejection convention as Threshold. Each
        // shell basis vector is expressed in capture axes and divided by its
        // own radius, supporting rotated spheroids and triaxial ellipsoids.
        internal const string EllipsoidThreshold=@"
cbuffer HDRPortalEllipsoidThreshold : register(b1) {
 row_major float4x4 HDRProjection; row_major float4x4 HDRInverseProjection;
 float4 HDRShellCentre;
 float4 HDRShellRight; float4 HDRShellUp; float4 HDRShellBackward;
 float4 HDRViewportGuard;
};
float __pixel_shader(float4 position:SV_Position,float2 uv:TEXCOORD0):SV_Target0 {
 float2 ndc=float2(2*position.x/HDRViewportGuard.x-1,1-2*position.y/HDRViewportGuard.y);
 float4 view=mul(float4(ndc,1,1),HDRInverseProjection);
 if(!all(isfinite(view))||abs(view.w)<1e-12) return -1;
 float3 direction=normalize(view.xyz/view.w),delta=-HDRShellCentre.xyz;
 float3 o=float3(dot(delta,HDRShellRight.xyz),dot(delta,HDRShellUp.xyz),dot(delta,HDRShellBackward.xyz));
 float3 d=float3(dot(direction,HDRShellRight.xyz),dot(direction,HDRShellUp.xyz),dot(direction,HDRShellBackward.xyz));
 float3 perpendicular=cross(o,d);
 float a=dot(d,d),b=dot(o,d),c=dot(o,o)-1,disc=a-dot(perpendicular,perpendicular);
 if(!isfinite(disc)||disc<0||a<=0) return -1;
 float root=sqrt(disc),q=-b-(b>=0?root:-root);
 float farRoot=abs(q)<1e-20?-b/a:max(q/a,c/q);
 if(!isfinite(farRoot)||farRoot<0) return -1;
 float4 clipPoint=mul(float4(direction*(farRoot+HDRViewportGuard.z),1),HDRProjection);
 if(!all(isfinite(clipPoint))||clipPoint.w<=0) return -1;
 float depth=clipPoint.z/clipPoint.w;
 return depth>=0?min(1,depth):-1;
}";
        internal static float[] EllipsoidThresholdConstants(PortalRayMapSpec spec,int width,int height)
        {
            if(spec==null||spec.ImageProjection!=PortalImageProjection.Perspective||width<64||height<64||width>2048||height>2048||(long)width*height>4194304)return null;
            var inverse=MatrixD.Transpose(spec.CapturePose.GetOrientation());var centre=Vector3D.TransformNormal(spec.Shell.Pose.Translation-spec.CapturePose.Translation,inverse);
            Vector3D safeRadii;double nativeGuard;if(!NativeShell(centre,spec.Shell.Extent,out safeRadii,out nativeGuard))return null;
            var result=new float[52];WriteMatrix(result,0,spec.Projection);WriteMatrix(result,16,MatrixD.Invert(spec.Projection));WriteVector(result,32,centre);
            WriteVector(result,36,Vector3D.TransformNormal(spec.Shell.Pose.Right,inverse)/safeRadii.X);
            WriteVector(result,40,Vector3D.TransformNormal(spec.Shell.Pose.Up,inverse)/safeRadii.Y);
            WriteVector(result,44,Vector3D.TransformNormal(spec.Shell.Pose.Backward,inverse)/safeRadii.Z);
            result[48]=width;result[49]=height;result[50]=(float)nativeGuard;
            return SafeConstants(result);
        }
        internal static float[] GeneralConstants(PortalRayPacket packet,float saturation,float brightness)
        {
            if(packet==null||!PortalProjection.Finite(saturation)||saturation<0||saturation>2||!PortalProjection.Finite(brightness)||brightness<0||brightness>4)return null;
            var spec=packet.Spec;var inverse=MatrixD.Transpose(spec.CapturePose.GetOrientation());
            var centre=Vector3D.TransformNormal(spec.Shell.Pose.Translation-spec.CapturePose.Translation,inverse);
            if(!PortalRayPacket.UploadVector(centre.X,centre.Y,centre.Z))return null;
            var result=new float[40];WriteMatrix(result,0,spec.Projection);WriteVector(result,16,centre);
            WriteVector(result,20,Vector3D.TransformNormal(spec.Shell.Pose.Right,inverse)/spec.Shell.Extent.X);
            WriteVector(result,24,Vector3D.TransformNormal(spec.Shell.Pose.Up,inverse)/spec.Shell.Extent.Y);
            WriteVector(result,28,Vector3D.TransformNormal(spec.Shell.Pose.Backward,inverse)/spec.Shell.Extent.Z);
            result[32]=saturation;result[33]=brightness;result[34]=spec.ImageProjection==PortalImageProjection.Angular?1:0;result[35]=spec.Accuracy==PortalRayAccuracy.ApproximateShell?1:0;
            result[36]=packet.Width;result[37]=packet.Height;result[38]=(float)Math.Max(1e-5,Math.Max(spec.Shell.Extent.X,Math.Max(spec.Shell.Extent.Y,spec.Shell.Extent.Z))*1e-6);result[39]=packet.IsAffine?1:0;
            if(packet.Gutter>0){result[19]=packet.Gutter;result[23]=packet.OutputWidth;result[27]=packet.OutputHeight;}
            return SafeConstants(result);
        }
        internal static float[] ThresholdConstants(PortalCaptureSpec spec)
        {
            if(spec==null)return null;var result=new float[40];WriteMatrix(result,0,spec.Projection);WriteMatrix(result,16,MatrixD.Invert(spec.Projection));
            Vector3D centre=Vector3D.TransformNormal(spec.ExitPose.Translation-spec.VirtualPose.Translation,MatrixD.Transpose(spec.VirtualPose.GetOrientation()));
            Vector3D radii;double guard;if(!NativeShell(centre,new Vector3D(spec.ExitRadius),out radii,out guard))return null;
            result[32]=(float)centre.X;result[33]=(float)centre.Y;result[34]=(float)centre.Z;result[35]=(float)radii.X;
            result[36]=spec.Width;result[37]=spec.Height;result[38]=(float)guard;return SafeConstants(result);
        }
        internal static float[] CompositeConstants(PortalCaptureSpec spec,float saturation,float brightness)
        {
            if(spec==null||float.IsNaN(saturation)||float.IsInfinity(saturation)||saturation<0||saturation>2||float.IsNaN(brightness)||float.IsInfinity(brightness)||brightness<0||brightness>4)return null;
            var result=new float[36];WriteMatrix(result,0,spec.Projection);MatrixD inverse=MatrixD.Transpose(spec.ViewerPose.GetOrientation());
            WriteVector(result,16,Vector3D.TransformNormal(spec.EntryPose.Right,inverse));WriteVector(result,20,Vector3D.TransformNormal(spec.EntryPose.Up,inverse));WriteVector(result,24,Vector3D.TransformNormal(spec.EntryPose.Backward,inverse));
            WriteVector(result,28,Vector3D.TransformNormal(spec.EntryPose.Translation-spec.ViewerPose.Translation,inverse));result[31]=(float)spec.EntryRadius;result[32]=saturation;result[33]=brightness;return SafeConstants(result);
        }
        static float[] SafeConstants(float[] values)
        {foreach(float value in values)if(float.IsNaN(value)||float.IsInfinity(value))return null;return values;}
        // Native float cutoff deliberately excludes a conservative superset of
        // the authored shell. Pixel-ray unprojection, basis casts, cross products
        // and grazing roots must not expose an interior strip after rounding.
        // Outside this bounded conditioning policy no capture is published.
        internal static bool NativeShell(Vector3D centre,Vector3D radii,out Vector3D inflated,out double guard)
        {
            inflated=Vector3D.Zero;guard=0;
            if(!PortalRayPacket.UploadVector(centre.X,centre.Y,centre.Z)||!PortalProjection.Positive(radii.X)||!PortalProjection.Positive(radii.Y)||!PortalProjection.Positive(radii.Z))return false;
            double min=Math.Min(radii.X,Math.Min(radii.Y,radii.Z)),max=Math.Max(radii.X,Math.Max(radii.Y,radii.Z)),distance=centre.Length();
            if(!PortalProjection.Finite(distance)||distance/min>8192||max/min>256)return false;
            double error=128d/8388608*Math.Max(1,distance+max);inflated=radii+new Vector3D(error);guard=Math.Max(1e-4,Math.Max(max*1e-6,error));return PortalProjection.Finite(inflated)&&PortalProjection.Finite(guard);
        }
        static void WriteVector(float[] a,int o,Vector3D v){a[o]=(float)v.X;a[o+1]=(float)v.Y;a[o+2]=(float)v.Z;}
        static void WriteMatrix(float[] a,int o,MatrixD m)
        {double[] v={m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};for(int i=0;i<16;i++)a[o+i]=(float)v[i];}
    }
}
