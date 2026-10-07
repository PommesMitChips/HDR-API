using System;
using System.Text.RegularExpressions;
namespace HDRClientRenderer
{
    // Rewrites only the installed GBuffer atmosphere integrator after trusted
    // preprocessing. Its native proxy, blend, constants and LUT remain intact.
    internal static class PortalAtmosphereClipSource
    {
        internal static string Inject(string source)
        {
            // The native preprocessor spaces member-access tokens as `a . b`.
            // Canonicalize only that spelling; decimal literals remain untouched.
            source=Regex.Replace(source,@"(?<=[A-Za-z_0-9\]])\s*\.\s*(?=[A-Za-z_])",".");
            Require(source,@"struct\s+EnvironmentSettings\s*\{\s*matrix\s+view_matrix\s*;\s*matrix\s+projection_matrix\s*;\s*matrix\s+projection_matrix_for_skybox\s*;\s*matrix\s+view_projection_matrix\s*;\s*matrix\s+inv_view_matrix\s*;\s*matrix\s+inv_proj_matrix\s*;\s*matrix\s+inv_view_proj_matrix\s*;");
            Require(source,@"struct\s+FrameConstants\s*\{\s*EnvironmentSettings\s+Environment\s*;");
            Require(source,@"struct\s+ScreenSettings\s*\{\s*uint2\s+offset\s*;\s*float2\s+resolution\s*;");
            Require(source,@"cbuffer\s+Frame\s*:\s*register\s*\(\s*b0\s*\)\s*\{\s*FrameConstants\s+frame_\s*;\s*\}");
            source=Replace(source,@"\bfloat4\s+ComputeAtmosphere\s*\(\s*float3\s+inputV\s*,\s*float3\s+position\s*,\s*float3\s+lightVec\s*,\s*float\s+depth\s*,\s*float\s+native_depth\s*,\s*int\s+steps\s*\)",
                "float4 ComputeAtmosphere(float3 inputV, float3 position, float3 lightVec, float depth, float native_depth, int steps, float hdrCutoffAxial, float hdrRayScale, float hdrCutoffDistance, float hdrForegroundDistance)");
            source=Replace(source,@"\bclip\s*\(\s*depth\s*-\s*1000\s*\)\s*;",
                "if (!isfinite(native_depth) || native_depth < 0 || native_depth > 1) discard; if (IsDepthForeground(native_depth)) { if (!isfinite(depth) || depth <= 0) discard; clip(depth - 1000); }");
            source=Replace(source,@"float3\s+rayEnd\s*=\s*position\s*/\s*PlanetScaleFactor\s*;\s*float\s+rayLength\s*=\s*length\s*\(\s*rayEnd\s*\)\s*;\s*float3\s+ray\s*=\s*rayEnd\s*/\s*rayLength\s*;\s*if\s*\(\s*viewAtmosphereInt\.y\s*>\s*0\s*\)\s*rayEnd\s*=\s*V\s*\*\s*min\s*\(\s*viewAtmosphereInt\.y\s*,\s*rayLength\s*\)\s*;\s*else\s*discard\s*;\s*ray\s*=\s*normalize\s*\(\s*rayEnd\s*\)\s*;\s*rayLength\s*=\s*length\s*\(\s*rayEnd\s*\)\s*;",@"
    if (!all(isfinite(V)) || !all(isfinite(viewAtmosphereInt)) || viewAtmosphereInt.y <= 0) discard;
    float hdrCutoffScaled = hdrCutoffDistance / PlanetScaleFactor;
    float rayLength = viewAtmosphereInt.y;
    // Sky depth is zero: bound it by the atmosphere, never length(position).
    if (IsDepthForeground(native_depth)) {
        float hdrForegroundEnd = hdrForegroundDistance / PlanetScaleFactor;
        if (!isfinite(hdrForegroundEnd) || hdrForegroundEnd <= 0) discard;
        rayLength = min(rayLength, hdrForegroundEnd);
    }
    if (!isfinite(hdrCutoffScaled) || hdrCutoffScaled < 0 || !isfinite(rayLength)) discard;
    float3 ray = V;
    float3 rayEnd = ray * rayLength;
    // Native fog uses axial metres, while atmosphere integration uses scaled ray distance.
    float hdrFogEndAxial = IsDepthForeground(native_depth) ? depth : rayLength * PlanetScaleFactor / hdrRayScale;
    depth = max(0, hdrFogEndAxial - hdrCutoffAxial);
    if (!isfinite(depth)) discard;
");
            source=Replace(source,@"float3\s+Pa\s*=\s*0\s*;\s*if\s*\(\s*viewAtmosphereInt\.x\s*>\s*0\s*\)\s*\{\s*Pa\s*=\s*ray\s*\*\s*viewAtmosphereInt\.x\s*\*\s*1\.001f\s*;\s*\}\s*float3\s+Pb\s*=\s*rayEnd\s*\*\s*0\.999f\s*;\s*if\s*\(\s*dot\s*\(\s*Pa\s*,\s*Pa\s*\)\s*>\s*dot\s*\(\s*Pb\s*,\s*Pb\s*\)\s*\)\s*discard\s*;",@"
    float hdrStart = max(max(0, viewAtmosphereInt.x) * 1.001f, hdrCutoffScaled);
    float hdrEnd = rayLength * 0.999f;
    if (!isfinite(hdrStart) || !isfinite(hdrEnd) || hdrStart >= hdrEnd) discard;
    float3 Pa = ray * hdrStart;
    float3 Pb = ray * hdrEnd;
    if (!all(isfinite(Pa)) || !all(isfinite(Pb))) discard;
");
            // This anchor certifies that observer attenuation starts at the new Pa.
            Require(source,@"float3\s+PaPTransmittance\s*=\s*0\s*;\s*float2\s+prevOpticalDepth\s*=\s*0\s*;");
            Require(source,@"float3\s+P\s*=\s*Pa\s*\+\s*ray\s*\*\s*stepLength\s*\*\s*i\s*;");
            Require(source,@"float3\s+foggedColor\s*=\s*Fog\s*\(\s*color\s*,\s*0\s*,\s*depth\s*,\s*frame_\.Fog\.atmo\s*\)\s*;");
            source=Replace(source,@"\boutput\s*=\s*ComputeAtmosphere\s*\(\s*input\.V\s*,\s*input\.position\s*,\s*frame_\.Light\.directionalLightVec\s*,\s*input\.depth\s*,\s*input\.native_depth\s*,\s*([124])\s*\)\s*;",
                "output = ComputeAtmosphere(-hdrWorldRay, input.position, frame_.Light.directionalLightVec, hdrForegroundAxial, input.native_depth, $1, hdrCutoffAxial, hdrRayScale, hdrCutoffDistance, hdrForegroundDistance);");
            source=Replace(source,@"(void\s+__pixel_shader\s*\(\s*float4\s+svPos\s*:\s*SV_Position\s*,\s*out\s+float4\s+output\s*:\s*SV_Target0(?:\s*,\s*uint\s+sample_index\s*:\s*SV_SampleIndex)?\s*\)\s*\{)",@"$1
    float hdrThreshold = HDRPortalExitThreshold.Load(int3((int2)svPos.xy, 0));
    // A zero threshold may be a negative shell depth clamped at a finite far
    // plane. Its true boundary is lost, so it cannot certify a surviving interval.
    if (!isfinite(hdrThreshold) || hdrThreshold <= 0 || hdrThreshold > 1 || !isfinite(PlanetScaleFactor) || PlanetScaleFactor <= 0) discard;
    float2 hdrUv = screen_to_uv((uint2)svPos.xy);
    float2 hdrNdc = float2(2 * hdrUv.x - 1, 1 - 2 * hdrUv.y);
    // Use the complete native inverse: paired-plane transport can shear both XY
    // and homogeneous W. The native diagonal-only depth/ray helpers lose that shear.
    float4 hdrNearH = mul(float4(hdrNdc, 1, 1), frame_.Environment.inv_proj_matrix);
    float4 hdrCutoffH = mul(float4(hdrNdc, hdrThreshold, 1), frame_.Environment.inv_proj_matrix);
    if (!all(isfinite(hdrNearH)) || !all(isfinite(hdrCutoffH)) || abs(hdrNearH.w) < 1e-12 || abs(hdrCutoffH.w) < 1e-12) discard;
    float3 hdrNearView = hdrNearH.xyz / hdrNearH.w;
    float3 hdrCutoffView = hdrCutoffH.xyz / hdrCutoffH.w;
    float hdrNearLength = length(hdrNearView);
    if (!all(isfinite(hdrNearView)) || !all(isfinite(hdrCutoffView)) || !isfinite(hdrNearLength) || hdrNearLength <= 0 || hdrNearView.z >= 0) discard;
    float3 hdrViewRay = hdrNearView / hdrNearLength;
    float3 hdrWorldRay = view_to_world(hdrViewRay);
    float hdrCutoffDistance = dot(hdrCutoffView, hdrViewRay);
    float hdrCutoffAxial = -hdrCutoffView.z;
    float hdrRayScale = hdrNearLength / -hdrNearView.z;
    if (!isfinite(hdrCutoffDistance) || hdrCutoffDistance <= 0 || !isfinite(hdrCutoffAxial) || hdrCutoffAxial <= 0 || !isfinite(hdrRayScale) || hdrRayScale <= 0) discard;
");
            source=Replace(source,@"(SurfaceInterface\s+input\s*=\s*read_gbuffer\s*\(\s*svPos\.xy\s*\)\s*;)",@"$1
    float hdrForegroundAxial = 0;
    float hdrForegroundDistance = 0;
    if (IsDepthForeground(input.native_depth)) {
        float4 hdrForegroundH = mul(float4(hdrNdc, input.native_depth, 1), frame_.Environment.inv_proj_matrix);
        if (!all(isfinite(hdrForegroundH)) || abs(hdrForegroundH.w) < 1e-12) discard;
        float3 hdrForegroundView = hdrForegroundH.xyz / hdrForegroundH.w;
        hdrForegroundAxial = -hdrForegroundView.z;
        hdrForegroundDistance = dot(hdrForegroundView, hdrViewRay);
        if (!all(isfinite(hdrForegroundView)) || !isfinite(hdrForegroundAxial) || hdrForegroundAxial <= 0 || !isfinite(hdrForegroundDistance) || hdrForegroundDistance <= 0) discard;
    }
");
            return "Texture2D<float> "+PortalClipSource.ThresholdName+" : register(t31);\n"+source;
        }
        static void Require(string source,string pattern)
        {if(Regex.Matches(source,pattern).Count!=1)throw new InvalidOperationException("Installed portal atmosphere anchor changed.");}
        static string Replace(string source,string pattern,string replacement)
        {Require(source,pattern);return Regex.Replace(source,pattern,replacement);}
    }
}
