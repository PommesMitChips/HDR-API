using System;

namespace HDRClientRenderer
{
    internal sealed class PanoramaDemand
    {
        internal const int Grid = 16, CellFields = 7;
        internal int Mask;
        internal int[] Sizes;
        internal bool Known;
        internal bool[] Fresh;
        internal PanoramaDensity[] Density;
        internal static PanoramaDemand Full(int count, int maximum)
        {
            var value = new PanoramaDemand { Mask = (1 << count) - 1, Sizes = new int[count], Fresh = new bool[count], Density = new PanoramaDensity[count] };
            for (int i = 0; i < count; i++) { value.Sizes[i] = maximum; value.Density[i] = PanoramaDensity.Full(maximum); }
            return value;
        }
        internal static PanoramaDemand Read(int count, int maximum, int mask, double[] packed, bool known)
        {
            var fallback = Full(count, maximum);
            if (!known || count < 1 || count > PanoramaStore.MaxFaces || mask < 0 || (mask & ~fallback.Mask) != 0 || packed == null ||
                packed.Length != 2 + count + count * Grid * Grid * CellFields || packed[0] != Grid || packed[1] != count) return fallback;
            var sizes = new int[count];
            for (int i = 0; i < count; i++)
            {
                double requested = packed[2 + i]; bool wanted = (mask & (1 << i)) != 0;
                if (!PanoramaStore.Finite(requested) || requested != (int)requested || wanted &&
                    (requested != 256 && requested != 512 && requested != 1024 && requested != 2048 || requested > maximum) || !wanted && requested != 0) return fallback;
                sizes[i] = (int)requested;
            }
            int start = 2 + count; double maxArea = (double)maximum * maximum; var fresh = new bool[count];
            for (int i = start; i < packed.Length; i += CellFields)
            {
                for (int f = 0; f < CellFields; f++) if (!PanoramaStore.Finite(packed[i + f])) return fallback;
                if (packed[i] != 0 && packed[i] != 1 || packed[i + 1] < 0 || packed[i + 1] > maximum ||
                    packed[i + 2] < 0 || packed[i + 2] > maxArea || packed[i + 3] < 0 || packed[i + 3] > maximum ||
                    packed[i + 4] < 0 || packed[i + 4] > maxArea || packed[i + 5] < 0 || packed[i + 5] > 1 ||
                    packed[i + 6] != 0 && packed[i + 6] != 1) return fallback;
                if (packed[i] == 1 && packed[i + 6] == 1) fresh[(i - start) / (Grid * Grid * CellFields)] = true;
            }
            var density = new PanoramaDensity[count];
            for (int i = 0; i < count; i++)
            {
                var cells = new double[Grid * Grid * CellFields];
                Array.Copy(packed, start + i * cells.Length, cells, 0, cells.Length);
                density[i] = PanoramaDensity.FromPacked(cells, maximum, true);
                if (!density[i].Known) return fallback;
            }
            return new PanoramaDemand { Mask = mask, Sizes = sizes, Known = true, Fresh = fresh, Density = density };
        }
    }
    internal sealed class PanoramaResolution
    {
        internal int Value;
        int lower, priorLimit;
        double lowerSince;
        internal static int OutputLimit(int width,int height,int configured)
        {
            double side=Math.Max(width/2d,height);
            int requested=side<=256?256:side<=512?512:side<=1024?1024:2048;
            return Math.Min(configured,requested);
        }
        internal int Observe(int suggested,bool fresh,int limit,double now)
        {
            if(Value==0||limit>priorLimit){Value=limit;lower=0;}
            if(Value>limit){Value=limit;lower=0;}
            priorLimit=limit;
            if(!fresh)return Value;
            int desired=Math.Min(limit,suggested);
            if(desired>=Value){Value=desired;lower=0;}
            else if(lower!=desired){lower=desired;lowerSince=now;}
            else if(now-lowerSince>=2){Value=desired;lower=0;}
            return Value;
        }
    }
}
