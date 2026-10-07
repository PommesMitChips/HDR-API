using System;
using System.Text.RegularExpressions;
namespace HDRClientRenderer
{
    internal enum PortalClipFamily { Unknown, Opaque, AlphaTest, Voxel, Transparent, Billboard, Particle, Foliage, Depth, Decal, Atmosphere, Cloud }
    internal static class PortalClipSource
    {
        internal const int ThresholdSlot=31;
        internal const string ThresholdName="HDRPortalExitThreshold";
        internal const string DepthOnly="Texture2D<float> HDRPortalExitThreshold : register(t31); void __pixel_shader(float4 position : SV_Position) { clip(HDRPortalExitThreshold.Load(int3((int2)position.xy,0))-position.z); }";
        internal const string DiscardAll="void __pixel_shader(float4 position : SV_Position) { clip(-1); }";
        internal static PortalClipFamily Family(string file)
        {
            if(file==null)return PortalClipFamily.Unknown;file=file.Replace('\\','/');
            if(file=="Transparent/Billboards.hlsl")return PortalClipFamily.Billboard;
            if(file=="Transparent/GPUParticles/Render.hlsl")return PortalClipFamily.Particle;
            if(file=="Foliage/Foliage.hlsl")return PortalClipFamily.Foliage;
            if(file=="Transparent/Atmosphere/AtmosphereGBuffer.hlsl")return PortalClipFamily.Atmosphere;
            if(file=="Transparent/Clouds/Clouds.hlsl")return PortalClipFamily.Cloud;
            if(!file.StartsWith("Geometry/Materials/",StringComparison.Ordinal)||!file.EndsWith("/Pixel.hlsl",StringComparison.Ordinal))return PortalClipFamily.Unknown;
            string material=file.Substring(19,file.Length-19-11);
            switch(material){case "Standard":return PortalClipFamily.Opaque;case "AlphaMasked":case "AlphaMaskedArray":return PortalClipFamily.AlphaTest;
                case "TriplanarSingle":case "TriplanarMulti":case "TriplanarDebris":return PortalClipFamily.Voxel;
                case "Glass":case "Holo":case "Shield":case "ShieldLit":return PortalClipFamily.Transparent;default:return PortalClipFamily.Unknown;}
        }
        internal static string Inject(string expanded,PortalClipFamily family)
        {
            if(expanded==null||expanded.Length>4*1024*1024||family==PortalClipFamily.Unknown||expanded.Contains(ThresholdName))throw new InvalidOperationException("Unverified portal shader source.");
            // The installed preprocessor inserts #line directives between the
            // conditional entry signature and its opening brace. They carry
            // diagnostics only; removing them does not alter shader semantics.
            expanded=Regex.Replace(expanded,@"^\s*#\s*line[^\r\n]*",string.Empty,RegexOptions.Multiline);
            if(Regex.IsMatch(expanded,@"\[\s*earlydepthstencil\s*\]",RegexOptions.IgnoreCase))
                throw new InvalidOperationException("Portal shader requires unsupported early-depth execution.");
            if(family==PortalClipFamily.Atmosphere)return PortalAtmosphereClipSource.Inject(expanded);
            var matches=Regex.Matches(expanded,@"\b(?:void|float4)\s+__pixel_shader\s*\([^)]*\)\s*(?::\s*\w+\s*)?\{");
            if(matches.Count!=1)throw new InvalidOperationException("Portal shader has no unique active pixel entry.");
            string signature=matches[0].Value,position;
            if(Regex.IsMatch(signature,@"\bPixelStageInput\s+input\b"))position="input.position";
            else if(Regex.IsMatch(signature,@"\bVertexStageOutput\s+vertex\b")||Regex.IsMatch(signature,@"\bVsOut\s+vertex\b"))position="vertex.position";
            else if(Regex.IsMatch(signature,@"\bPS_INPUT\s+In\b"))position="In.Position";
            else if(Regex.IsMatch(signature,@"\bRenderingPixelInput\s+input\b"))position="input.position";
            else if(family==PortalClipFamily.Cloud&&Regex.IsMatch(signature,@"\bPsInput\s+input\b"))position="input.positionScreen";
            else throw new InvalidOperationException("Portal pixel position signature changed.");
            // Custom-projection billboards require a separate producer capability:
            // do not silently interpret their depth in the ordinary capture projection.
            string check=family==PortalClipFamily.Billboard?"if (BillboardBuffer[vertex.index].custom_projection_id >= 0) clip(-1);\n":"";
            check+="clip(HDRPortalExitThreshold.Load(int3((int2)"+position+".xy,0))-"+position+".z);\n";
            int start=matches[0].Index+matches[0].Length;
            string result=expanded.Insert(start,"\n"+check);
            // AlphaMaskedArray changes SV_Depth. Keep the geometric test above,
            // then also reject its final depth before it can be committed.
            if(Regex.IsMatch(expanded,@"\bSV_Depth\b",RegexOptions.IgnoreCase))
            {
                var depth=Regex.Matches(result,@"float\s+depth\s*=\s*material_output\s*\.\s*depth\s*>\s*0\s*\?\s*material_output\s*\.\s*depth\s*:\s*input\s*\.\s*position\s*\.\s*z\s*;");
                if(depth.Count!=1)throw new InvalidOperationException("Unverified portal custom-depth output.");
                int end=depth[0].Index+depth[0].Length;
                result=result.Insert(end,"\nclip(HDRPortalExitThreshold.Load(int3((int2)input.position.xy,0))-depth);\n");
            }
            return "Texture2D<float> "+ThresholdName+" : register(t31);\n"+result;
        }
    }
}
