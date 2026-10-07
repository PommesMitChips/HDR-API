using System;

namespace HDRClientRenderer
{
    internal readonly struct PanoramaTile
    {
        internal readonly double SourceMinU,SourceMinV,SourceMaxU,SourceMaxV,AtlasMinU,AtlasMinV,AtlasMaxU,AtlasMaxV;
        internal PanoramaTile(double minU,double minV,double maxU,double maxV,double atlasMinU,double atlasMinV,double atlasMaxU,double atlasMaxV)
        {SourceMinU=minU;SourceMinV=minV;SourceMaxU=maxU;SourceMaxV=maxV;AtlasMinU=atlasMinU;AtlasMinV=atlasMinV;AtlasMaxU=atlasMaxU;AtlasMaxV=atlasMaxV;}
        internal static PanoramaTile Full {get{return new PanoramaTile(0,0,1,1,0,0,1,1);}}
        static bool Interval(double min,double max){return PanoramaStore.Finite(min)&&PanoramaStore.Finite(max)&&min>=0&&max<=1&&max>min;}
        internal bool Valid(int width,int height)
        {return Interval(SourceMinU,SourceMaxU)&&Interval(SourceMinV,SourceMaxV)&&Interval(AtlasMinU,AtlasMaxU)&&Interval(AtlasMinV,AtlasMaxV)&&
            (AtlasMaxU-AtlasMinU)*width>=1-.000001&&(AtlasMaxV-AtlasMinV)*height>=1-.000001;}
        internal bool Same(PanoramaTile other)
        {return SourceMinU==other.SourceMinU&&SourceMinV==other.SourceMinV&&SourceMaxU==other.SourceMaxU&&SourceMaxV==other.SourceMaxV&&
            AtlasMinU==other.AtlasMinU&&AtlasMinV==other.AtlasMinV&&AtlasMaxU==other.AtlasMaxU&&AtlasMaxV==other.AtlasMaxV;}
        static bool Overlap(double a0,double a1,double b0,double b1){return Math.Min(a1,b1)>Math.Max(a0,b0)+.000000001;}
        internal static bool Valid(PanoramaTile[] tiles,int width,int height)
        {
            if(tiles==null)return true;
            if(tiles.Length<1||tiles.Length>64)return false;
            for(int i=0;i<tiles.Length;i++)
            {
                if(!tiles[i].Valid(width,height))return false;
                for(int j=0;j<i;j++)
                {
                    var a=tiles[i];var b=tiles[j];
                    if(Overlap(a.SourceMinU,a.SourceMaxU,b.SourceMinU,b.SourceMaxU)&&Overlap(a.SourceMinV,a.SourceMaxV,b.SourceMinV,b.SourceMaxV)||
                        Overlap(a.AtlasMinU,a.AtlasMaxU,b.AtlasMinU,b.AtlasMaxU)&&Overlap(a.AtlasMinV,a.AtlasMaxV,b.AtlasMinV,b.AtlasMaxV))return false;
                }
            }
            return true;
        }
        internal bool Contains(double u,double v)
        {return u>=SourceMinU&&v>=SourceMinV&&(u<SourceMaxU||SourceMaxU==1&&u<=1)&&(v<SourceMaxV||SourceMaxV==1&&v<=1);}
        // The native leaves are aligned to density cells. Arbitrary rectangles
        // retain a scan sentinel only in cells crossed by a non-grid boundary.
        internal static float[] Lookup(PanoramaTile[] tiles)
        {
            var result=new float[256];int count=tiles==null?1:tiles.Length;
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)
            {
                double minU=x/16d,minV=y/16d,maxU=(x+1)/16d,maxV=(y+1)/16d;int owner=-1;bool ambiguous=false;
                for(int i=0;i<count;i++)
                {
                    var tile=tiles==null?Full:tiles[i];
                    if(tile.Contains((minU+maxU)*.5,(minV+maxV)*.5))owner=i;
                    if(Math.Min(maxU,tile.SourceMaxU)<=Math.Max(minU,tile.SourceMinU)||Math.Min(maxV,tile.SourceMaxV)<=Math.Max(minV,tile.SourceMinV))continue;
                    if(tile.SourceMinU>minU&&tile.SourceMinU<maxU||tile.SourceMaxU>minU&&tile.SourceMaxU<maxU||
                        tile.SourceMinV>minV&&tile.SourceMinV<maxV||tile.SourceMaxV>minV&&tile.SourceMaxV<maxV)ambiguous=true;
                }
                result[y*16+x]=ambiguous?-2:owner;
            }
            return result;
        }
        internal static bool Map(PanoramaTile[] tiles,int width,int height,double sourceU,double sourceV,out double u,out double v)
        {
            u=v=0;
            if(width<1||height<1||!Valid(tiles,width,height)||!PanoramaStore.Finite(sourceU)||!PanoramaStore.Finite(sourceV)||sourceU<0||sourceU>1||sourceV<0||sourceV>1)return false;
            int count=tiles==null?1:tiles.Length;
            int selected=-1;
            for(int i=0;i<count;i++)
            {var tile=tiles==null?Full:tiles[i];if(tile.Contains(sourceU,sourceV)){selected=i;break;}}
            // A sparse footprint can end on an internal grid edge. Preserve
            // its last captured texel if no half-open neighbour owns that ray.
            if(selected<0)for(int i=0;i<count;i++)
            {var tile=tiles==null?Full:tiles[i];if(sourceU>=tile.SourceMinU&&sourceU<=tile.SourceMaxU&&sourceV>=tile.SourceMinV&&sourceV<=tile.SourceMaxV){selected=i;break;}}
            if(selected<0)return false;
            {
                var tile=tiles==null?Full:tiles[selected];
                u=tile.AtlasMinU+(sourceU-tile.SourceMinU)/(tile.SourceMaxU-tile.SourceMinU)*(tile.AtlasMaxU-tile.AtlasMinU);
                v=tile.AtlasMinV+(sourceV-tile.SourceMinV)/(tile.SourceMaxV-tile.SourceMinV)*(tile.AtlasMaxV-tile.AtlasMinV);
                u=Math.Max(tile.AtlasMinU+.5/width,Math.Min(tile.AtlasMaxU-.5/width,u));
                v=Math.Max(tile.AtlasMinV+.5/height,Math.Min(tile.AtlasMaxV-.5/height,v));
                return true;
            }
        }
    }
}
