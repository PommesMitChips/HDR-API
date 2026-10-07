using System;
using System.Collections.Generic;
namespace HDRClientRenderer
{
    internal enum PortalDrawRole { Unknown, Geometry, Auxiliary, Suppressed }
    // Deferred passes consume clipped surface inputs. The certified atmosphere
    // family moves its integration interval past the shell; clouds use mesh depth.
    internal static class PortalCaptureDrawPolicy
    {
        static readonly HashSet<string> auxiliary=new HashSet<string>(StringComparer.Ordinal){
            "Lighting/LightDir.hlsl","Lighting/LightPoint.hlsl","Lighting/LightSpot.hlsl",
            "Postprocess/Blur.hlsl","Postprocess/DepthResolve.hlsl","Postprocess/EdgeDetection.hlsl","Postprocess/Fxaa.hlsl",
            "Postprocess/PostprocessClearAlpha.hlsl","Postprocess/PostprocessCopy.hlsl","Postprocess/PostprocessCopyFilter.hlsl",
            "Postprocess/PostprocessCopyInverseStencil.hlsl","Postprocess/PostprocessCopyStencil.hlsl","Postprocess/PostprocessStretch.hlsl",
            "Postprocess/Bloom/Blur.hlsl","Postprocess/Bloom/DownsampleBlur.hlsl","Postprocess/Bloom/Downscale2.hlsl","Postprocess/Bloom/Downscale4.hlsl",
            "Postprocess/Bloom/Init.hlsl","Postprocess/Bloom/PreFilter.hlsl","Postprocess/Bloom/UpsampleBlur.hlsl",
            "Postprocess/ChromaticAberration/ChromaticAberration.hlsl","Postprocess/EyeAdaptation/ConstantExposure.hlsl",
            "Postprocess/EyeAdaptation/DownSample.hlsl","Postprocess/EyeAdaptation/EyeAdaptation.hlsl","Postprocess/EyeAdaptation/UpdateHistogram.hlsl",
            "Postprocess/HBAO/BlurX.hlsl","Postprocess/HBAO/BlurY.hlsl","Postprocess/HBAO/CoarseAO.hlsl","Postprocess/HBAO/Copy.hlsl",
            "Postprocess/HBAO/DeinterleaveDepth.hlsl","Postprocess/HBAO/DrawNormals.hlsl","Postprocess/HBAO/LinearizeDepth.hlsl",
            "Postprocess/HBAO/ReconstructNormal.hlsl","Postprocess/HBAO/ReinterleaveAO.hlsl",
            "Postprocess/LuminanceReduction/Init.hlsl","Postprocess/LuminanceReduction/Skip.hlsl","Postprocess/LuminanceReduction/Sum.hlsl",
            "Postprocess/SSAO/Ssao.hlsl","Postprocess/Tonemapping/Main.hlsl","Transparent/OIT/Resolve.hlsl",
            // Decals reconstruct their surface from the clipped native depth;
            // clipping decal-volume SV_Position instead would remove valid decals.
            "Decals/Decals.hlsl","Shadows/Shape.hlsl","EnvProbe/ForwardPostprocess.hlsl"};
        static readonly HashSet<string> suppressed=new HashSet<string>(StringComparer.Ordinal){
            "Transparent/Atmosphere/AtmosphereEnv.hlsl",
            "Transparent/ResolveAccumIntoHeatMap.hlsl","Stereo/StereoStencilMask.hlsl",
            "Primitives/Lines.hlsl","Primitives/Primitives.hlsl","Primitives/Sprites.hlsl","Primitives/GroupOcclusionQuery.hlsl","Primitives/OcclusionQuery.hlsl"};
        internal static PortalDrawRole Role(string file)
        {
            if(file==null)return PortalDrawRole.Unknown;file=file.Replace('\\','/');
            if(PortalClipSource.Family(file)!=PortalClipFamily.Unknown)return PortalDrawRole.Geometry;
            if(auxiliary.Contains(file))return PortalDrawRole.Auxiliary;
            if(suppressed.Contains(file))return PortalDrawRole.Suppressed;
            return PortalDrawRole.Unknown;
        }
    }
}
