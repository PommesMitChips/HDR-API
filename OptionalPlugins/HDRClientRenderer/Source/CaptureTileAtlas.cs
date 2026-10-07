using System;
using System.Collections.Generic;

namespace HDRClientRenderer
{
    // Density-gradient/benefit driven source-UV quadtree. Every leaf is an actual
    // native crop; extra scene passes have a configurable pixel-equivalent cost.
    // Each region is a native cropped view, not a warp of uniformly captured RGB.
    internal sealed class CaptureTileAtlas
    {
        internal const int MaxTiles = 64, MinTile = 64, MaxSide = 2048;
        internal struct Tile
        {
            internal readonly int Index, X, Y, Size;
            internal readonly double SourceMinU, SourceMinV, SourceMaxU, SourceMaxV;
            // Atlas bounds are pixel edges. The sampler clamps to the first/last
            // texel centres after mapping, without shrinking the source footprint.
            internal readonly double AtlasMinU, AtlasMinV, AtlasMaxU, AtlasMaxV;
            internal Tile(int index, int x, int y, int size, int width, int height,
                double minU, double minV, double maxU, double maxV)
            {
                Index = index; X = x; Y = y; Size = size;
                SourceMinU = minU; SourceMinV = minV; SourceMaxU = maxU; SourceMaxV = maxV;
                AtlasMinU = (double)x / width; AtlasMinV = (double)y / height;
                AtlasMaxU = (double)(x + size) / width; AtlasMaxV = (double)(y + size) / height;
            }
            internal int Pixels { get { return checked(Size * Size); } }
        }
        readonly Tile[] tiles;
        internal readonly int Width, Height, FullResolution, RasterPixels;
        internal readonly bool Adaptive;
        internal readonly long LayoutKey;
        internal int Count { get { return tiles.Length; } }
        internal Tile At(int index) { return tiles[index]; }
        internal Tile[] Tiles { get { return (Tile[])tiles.Clone(); } }
        internal CaptureTileAtlas OnCanvas(int width,int height)
        {
            if(width<Width||height<Height||width>MaxSide||height>MaxSide)throw new ArgumentOutOfRangeException("width");
            var result=new Tile[tiles.Length];
            for(int i=0;i<result.Length;i++){var t=tiles[i];result[i]=new Tile(t.Index,t.X,t.Y,t.Size,width,height,t.SourceMinU,t.SourceMinV,t.SourceMaxU,t.SourceMaxV);}
            return new CaptureTileAtlas(width,height,FullResolution,Adaptive,result);
        }
        CaptureTileAtlas(int width, int height, int fullResolution, bool adaptive, Tile[] tiles)
        {
            Width = width; Height = height; FullResolution = fullResolution; Adaptive = adaptive; this.tiles = tiles;
            long key = width + height * 4096L + (adaptive ? 16777216L : 0);
            int pixels = 0;
            foreach (var tile in tiles)
            {
                pixels=checked(pixels+tile.Pixels);
                foreach(long value in new[]{(long)tile.Index,tile.X,tile.Y,tile.Size,BitConverter.DoubleToInt64Bits(tile.SourceMinU),
                    BitConverter.DoubleToInt64Bits(tile.SourceMinV),BitConverter.DoubleToInt64Bits(tile.SourceMaxU),BitConverter.DoubleToInt64Bits(tile.SourceMaxV)})
                    key=unchecked(key*1099511628211L+value);
            }
            RasterPixels = pixels; LayoutKey = key;
        }
        internal static CaptureTileAtlas Uniform(int size)
        {
            if (!PowerOfTwo(size) || size < MinTile || size > MaxSide) throw new ArgumentOutOfRangeException("size");
            return new CaptureTileAtlas(size, size, size, false, new[] { new Tile(0, 0, 0, size, size, size, 0, 0, 1, 1) });
        }
        internal static CaptureTileAtlas Plan(PanoramaDensity density, int maximum)
        { return PlanAdaptive(density,maximum,16,16384); }
        sealed class Region
        {
            internal int X,Y,Side,Size;
            internal double Gradient;
            internal List<Region> Children;
            internal readonly List<Region>[] Alternatives=new List<Region>[5];
            internal int Pixels {get{return Size*Size;}}
        }
        static List<Region> Alternative(Region parent,int depth,PanoramaDensity density,int maximum,double passCost)
        {
            if(parent.Alternatives[depth]!=null)return parent.Alternatives[depth];
            var result=new List<Region>{parent};
            if(depth>0&&parent.Side>1&&parent.Gradient>0)
            {
                if(parent.Children==null)
                {
                    parent.Children=new List<Region>(4);int side=parent.Side/2;
                    for(int q=0;q<4;q++){var child=Measure(density,parent.X+(q%2)*side,parent.Y+(q/2)*side,side,maximum);
                        if(child!=null)parent.Children.Add(child);}
                }
                var refined=new List<Region>();int pixels=0;
                foreach(var child in parent.Children)refined.AddRange(Alternative(child,depth-1,density,maximum,passCost));
                foreach(var child in refined)pixels+=child.Pixels;
                if(refined.Count>0&&parent.Pixels>pixels+passCost*Math.Max(0,refined.Count-1))result=refined;
            }
            return parent.Alternatives[depth]=result;
        }
        static Region Measure(PanoramaDensity density,int x,int y,int side,int maximum)
        {
            bool covered=false;double high=0,low=double.PositiveInfinity;
            for(int row=y;row<y+side;row++)for(int col=x;col<x+side;col++)
            {
                var cell=density.At(col,row);double value=cell.Covered?Math.Max(cell.UpperMagnification,Math.Sqrt(cell.UpperArea)):0;
                if(cell.Covered)covered=true;
                high=Math.Max(high,value);low=Math.Min(low,value);
            }
            if(!covered)return null;
            int cap=Math.Max(MinTile,maximum*side/PanoramaDensity.Grid);
            return new Region{X=x,Y=y,Side=side,Size=CeilSide(Math.Min(maximum,high)*side/PanoramaDensity.Grid,cap),
                Gradient=high<=0?0:(high-low)/high};
        }
        internal static CaptureTileAtlas PlanAdaptive(PanoramaDensity density,int maximum,int limit,double passCost)
        {
            if (!PowerOfTwo(maximum) || maximum < MinTile || maximum > MaxSide) throw new ArgumentOutOfRangeException("maximum");
            if(limit<1||limit>MaxTiles||!PanoramaStore.Finite(passCost)||passCost<0)throw new ArgumentOutOfRangeException("limit");
            if (density == null || !density.Known || !density.Fresh || maximum < MinTile * 2) return Uniform(density==null?maximum:Math.Min(maximum,density.Ceiling));
            var root=Measure(density,0,0,PanoramaDensity.Grid,maximum);
            if(root==null)return Uniform(maximum);
            var regions=new List<Region>{root};
            while(true)
            {
                int selected=-1;List<Region> children=null;double best=0,bestGradient=0;
                for(int i=0;i<regions.Count;i++)
                {
                    var parent=regions[i];if(parent.Side<2)continue;
                    // Look ahead through the density grid: distributed hotspots
                    // can yield no first-level savings yet benefit deeper down.
                    for(int depth=1;depth<=4;depth++)
                    {
                        var split=Alternative(parent,depth,density,maximum,passCost);int cost=0;
                        if(split.Count==0||regions.Count-1+split.Count>limit)continue;
                        foreach(var child in split)cost+=child.Pixels;
                        double gain=parent.Pixels-cost-passCost*Math.Max(0,split.Count-1);
                        if(gain>best||gain>0&&gain==best&&parent.Gradient>bestGradient)
                        {selected=i;children=split;best=gain;bestGradient=parent.Gradient;}
                    }
                }
                if(selected<0)break;
                regions.RemoveAt(selected);regions.AddRange(children);
            }
            if(regions.Count==1&&regions[0].Side==PanoramaDensity.Grid)return Uniform(regions[0].Size);
            // Stable descriptor order and IDs include grid position and depth.
            regions.Sort((a,b)=>a.Y==b.Y?a.X.CompareTo(b.X):a.Y.CompareTo(b.Y));
            int count=regions.Count;
            // Try every bounded power-of-two atlas width. Descending-size shelf
            // packing avoids retaining a square atlas for a thin sparse footprint.
            int[] order=new int[count];for(int i=0;i<count;i++)order[i]=i;
            Array.Sort(order, (a, b) => regions[b].Size == regions[a].Size ? a.CompareTo(b) : regions[b].Size.CompareTo(regions[a].Size));
            int bestWidth = maximum, bestHeight = maximum, bestArea = int.MaxValue;
            int[] bestX = null, bestY = null;
            for (int width = MinTile; width <= maximum; width *= 2)
            {
                int x = 0, y = 0, row = 0; bool fits = true; int[] xs = new int[count], ys = new int[count];
                foreach (int index in order)
                {
                    int size = regions[index].Size;
                    if (size > width) { fits = false; break; }
                    if (x + size > width) { y += row; x = row = 0; }
                    xs[index] = x; ys[index] = y; x += size; row = Math.Max(row, size);
                }
                int height = NextPowerOfTwo(y + row);
                if (!fits || height > maximum || width * height >= bestArea) continue;
                bestWidth = width; bestHeight = height; bestArea = width * height; bestX = xs; bestY = ys;
            }
            if (bestX == null) return Uniform(maximum);
            var tiles = new List<Tile>(count);
            for (int q = 0; q < count; q++)
            {
                var r=regions[q];int id=r.X+r.Y*PanoramaDensity.Grid+(r.Side-1)*PanoramaDensity.CellCount;
                tiles.Add(new Tile(id, bestX[q], bestY[q], r.Size, bestWidth, bestHeight,
                    r.X/(double)PanoramaDensity.Grid,r.Y/(double)PanoramaDensity.Grid,(r.X+r.Side)/(double)PanoramaDensity.Grid,(r.Y+r.Side)/(double)PanoramaDensity.Grid));
            }
            return new CaptureTileAtlas(bestWidth, bestHeight, maximum, true, tiles.ToArray());
        }
        static int CeilSide(double demand, int maximum)
        { int side = MinTile; while (side < demand && side < maximum) side *= 2; return side; }
        // Do not saturate at MaxSide: an overflowing shelf must be rejected,
        // not described as a smaller atlas containing out-of-bounds tiles.
        static int NextPowerOfTwo(int value) { int result = MinTile; while (result < value) result *= 2; return result; }
        internal static int FitPixelLimit(int size, int pixels)
        { while (size >= MinTile && (long)size * size > pixels) size /= 2; return size < MinTile ? 0 : size; }
        static bool PowerOfTwo(int value) { return value > 0 && (value & (value - 1)) == 0; }
        internal static long MipPixels(int width, int height)
        {
            long result = 0;
            while (width > 0 && height > 0)
            { result += (long)width * height; if (width == 1 && height == 1) break; width = Math.Max(1, width / 2); height = Math.Max(1, height / 2); }
            return result;
        }
    }
}
