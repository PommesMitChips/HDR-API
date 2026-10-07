using System;

namespace HDRClientRenderer
{
    internal static class PanoramaShader
    {
        internal const int MaxTiles=64,LookupOffset=16+MaxTiles*8,FaceFloats=LookupOffset+256,SettingsOffset=FaceFloats*6;
        internal const int ConstantFloats = SettingsOffset+4, ConstantBytes = ConstantFloats * 4;
        internal static bool ValidConstants(float[] values)
        {
            if(values==null||values.Length!=ConstantFloats)return false;
            foreach(float value in values)if(float.IsNaN(value)||float.IsInfinity(value))return false;
            return true;
        }
        // This embedded source is the only shader accepted by the compositor.
        // UV agrees with HDR's angular sphere: centre +Z, right +X, top +Y.
        internal const string Source = @"
Texture2D<float4> Source0 : register(t0);
Texture2D<float4> Source1 : register(t1);
Texture2D<float4> Source2 : register(t2);
Texture2D<float4> Source3 : register(t3);
Texture2D<float4> Source4 : register(t4);
Texture2D<float4> Source5 : register(t5);
SamplerState LinearClamp : register(s2);
struct Tile { float4 sourceRect; float4 atlasRect; };
struct Face { float4 rightTan; float4 upTan; float4 forwardValid; float4 atlasInfo; Tile tiles[64]; float4 tileLookup[64]; };
cbuffer PanoramaConstants : register(b1) { Face faces[6]; float4 settings; };
struct PixelInput { float4 position : SV_Position; float2 uv : TEXCOORD0; };
float3 ReadFace(int index, float2 uv)
{
    if (index == 0) return Source0.SampleLevel(LinearClamp, uv, 0).rgb;
    if (index == 1) return Source1.SampleLevel(LinearClamp, uv, 0).rgb;
    if (index == 2) return Source2.SampleLevel(LinearClamp, uv, 0).rgb;
    if (index == 3) return Source3.SampleLevel(LinearClamp, uv, 0).rgb;
    if (index == 4) return Source4.SampleLevel(LinearClamp, uv, 0).rgb;
    return Source5.SampleLevel(LinearClamp, uv, 0).rgb;
}
int CellTile(int faceIndex,int2 cell)
{
    int index=cell.y*16+cell.x;
    return (int)faces[faceIndex].tileLookup[index/4][index%4];
}
float4 main(PixelInput input) : SV_Target0
{
    float longitude = (input.uv.x - 0.5) * 6.28318530718;
    float latitude = (0.5 - input.uv.y) * 3.14159265359;
    float cp = cos(latitude);
    float3 ray = float3(cp * sin(longitude), sin(latitude), cp * cos(longitude));
    float3 sum = 0; float weights = 0;
    [unroll] for (int i = 0; i < 6; i++)
    {
        if (faces[i].forwardValid.w < 0.5) continue;
        float z = dot(ray, faces[i].forwardValid.xyz);
        if (z <= 0.000001) continue;
        float x = dot(ray, faces[i].rightTan.xyz) / z;
        float y = dot(ray, faces[i].upTan.xyz) / z;
        float2 uv = float2(0.5 + 0.5 * x / faces[i].rightTan.w,
                          0.5 - 0.5 * y / faces[i].upTan.w);
        if (any(uv < 0) || any(uv > 1)) continue;
        int2 cell=min((int2)floor(uv*16),int2(15,15));
        int selected=CellTile(i,cell);bool arbitrary=selected==-2;
        if(selected<0&&!arbitrary)
        {
            bool2 boundary=(uv*16)==floor(uv*16);int best=64;
            [unroll] for(int neighbour=0;neighbour<3;neighbour++)
            {
                if(neighbour==0&&!boundary.x||neighbour==1&&!boundary.y||neighbour==2&&!all(boundary))continue;
                int2 delta=neighbour==0?int2(1,0):neighbour==1?int2(0,1):int2(1,1);
                int candidate=CellTile(i,max(cell-delta,int2(0,0)));
                if(candidate==-2){arbitrary=true;continue;}
                if(candidate<0||candidate>=faces[i].atlasInfo.z)continue;
                float4 rect=faces[i].tiles[candidate].sourceRect;
                if(all(uv>=rect.xy)&&all(uv<=rect.zw))best=min(best,candidate);
            }
            if(!arbitrary&&best<64)selected=best;
        }
        // Only arbitrary non-grid boundaries, including adjacent sparse edges,
        // need a bounded scan. Native quadtree leaves use direct lookup.
        if(arbitrary)
        {
            selected=-1;
            [loop] for(int tileIndex=0;tileIndex<64;tileIndex++)
            {
                if(tileIndex>=faces[i].atlasInfo.z)break;
                float4 rect=faces[i].tiles[tileIndex].sourceRect;
                bool inside=all(uv>=rect.xy)&&all((uv<rect.zw)||((rect.zw==1)&&(uv<=1)));
                if(inside){selected=tileIndex;break;}
            }
            if(selected<0)
            {
                [loop] for(int edge=0;edge<64;edge++)
                {
                    if(edge>=faces[i].atlasInfo.z)break;
                    float4 rect=faces[i].tiles[edge].sourceRect;
                    if(all(uv>=rect.xy)&&all(uv<=rect.zw)){selected=edge;break;}
                }
            }
        }
        if(selected<0||selected>=faces[i].atlasInfo.z)continue;
        Tile tile=faces[i].tiles[selected];
        float2 atlasUv=lerp(tile.atlasRect.xy,tile.atlasRect.zw,(uv-tile.sourceRect.xy)/(tile.sourceRect.zw-tile.sourceRect.xy));
        atlasUv=clamp(atlasUv,tile.atlasRect.xy+faces[i].atlasInfo.xy*.5,tile.atlasRect.zw-faces[i].atlasInfo.xy*.5);
        float margin = min(atan(faces[i].rightTan.w) - atan(abs(x)),
                           atan(faces[i].upTan.w) - atan(abs(y)));
        float weight = settings.x <= 0 ? 1 : smoothstep(0, settings.x, margin);
        sum += ReadFace(i, atlasUv) * weight; weights += weight;
    }
    if (weights <= 0.0000001) return float4(0, 0, 0, 0);
    float3 colour = sum / weights;
    float luminance = dot(colour, float3(0.2126, 0.7152, 0.0722));
    colour = saturate(lerp(luminance.xxx, colour, settings.y));
    return float4(colour, 1);
}";
        internal static float[] Constants(PanoramaStore.Face[] faces, PanoramaStore.Settings settings)
        {
            if (faces == null || faces.Length > PanoramaStore.MaxFaces || settings == null || !settings.Valid) return null;
            var values = new float[ConstantFloats];
            for (int i = 0; i < faces.Length; i++)
            {
                var face = faces[i]; if (face == null || !face.Valid) return null;
                int start = i * FaceFloats;
                values[start] = (float)face.RightX; values[start + 1] = (float)face.RightY; values[start + 2] = (float)face.RightZ; values[start + 3] = (float)face.TanHorizontal;
                values[start + 4] = (float)face.UpX; values[start + 5] = (float)face.UpY; values[start + 6] = (float)face.UpZ; values[start + 7] = (float)face.TanVertical;
                values[start + 8] = (float)face.ForwardX; values[start + 9] = (float)face.ForwardY; values[start + 10] = (float)face.ForwardZ; values[start + 11] = 1;
                values[start+12]=1f/face.Width;values[start+13]=1f/face.Height;values[start+14]=face.Tiles==null?1:face.Tiles.Length;
                int count=face.Tiles==null?1:face.Tiles.Length;
                for(int t=0;t<count;t++)
                {
                    var tile=face.Tiles==null?PanoramaTile.Full:face.Tiles[t];int offset=start+16+t*8;
                    values[offset]=(float)tile.SourceMinU;values[offset+1]=(float)tile.SourceMinV;values[offset+2]=(float)tile.SourceMaxU;values[offset+3]=(float)tile.SourceMaxV;
                    values[offset+4]=(float)tile.AtlasMinU;values[offset+5]=(float)tile.AtlasMinV;values[offset+6]=(float)tile.AtlasMaxU;values[offset+7]=(float)tile.AtlasMaxV;
                    if(values[offset]>=values[offset+2]||values[offset+1]>=values[offset+3]||values[offset+4]>=values[offset+6]||values[offset+5]>=values[offset+7])return null;
                }
                Array.Copy(PanoramaTile.Lookup(face.Tiles),0,values,start+LookupOffset,256);
            }
            values[SettingsOffset] = (float)(settings.FeatherDegrees * Math.PI / 180); values[SettingsOffset+1] = (float)settings.Saturation;
            return values;
        }
        // Scalar policy oracle for mapping/weight tests. It never samples video or
        // participates in the GPU path, and its loop is bounded to six faces.
        internal static bool Project(double u, double v, PanoramaStore.Face face, double featherDegrees, out double sourceU, out double sourceV, out double weight)
        {
            sourceU = sourceV = weight = 0;
            if (face == null || !face.Valid || !PanoramaStore.Finite(u) || !PanoramaStore.Finite(v) || u < 0 || u > 1 || v < 0 || v > 1 ||
                !PanoramaStore.Finite(featherDegrees) || featherDegrees < 0 || featherDegrees > 25) return false;
            double longitude = (u - .5) * Math.PI * 2, latitude = (.5 - v) * Math.PI;
            double x = Math.Cos(latitude) * Math.Sin(longitude), y = Math.Sin(latitude), z = Math.Cos(latitude) * Math.Cos(longitude);
            double forward = x * face.ForwardX + y * face.ForwardY + z * face.ForwardZ;
            if (forward <= .000001) return false;
            double lensX = (x * face.RightX + y * face.RightY + z * face.RightZ) / forward;
            double lensY = (x * face.UpX + y * face.UpY + z * face.UpZ) / forward;
            sourceU = .5 + .5 * lensX / face.TanHorizontal; sourceV = .5 - .5 * lensY / face.TanVertical;
            if (sourceU < 0 || sourceU > 1 || sourceV < 0 || sourceV > 1) return false;
            double margin = Math.Min(Math.Atan(face.TanHorizontal) - Math.Atan(Math.Abs(lensX)), Math.Atan(face.TanVertical) - Math.Atan(Math.Abs(lensY)));
            double ratio = featherDegrees == 0 ? 1 : Math.Max(0, Math.Min(1, margin / (featherDegrees * Math.PI / 180)));
            weight = ratio * ratio * (3 - 2 * ratio); return weight > 0;
        }
    }
}
