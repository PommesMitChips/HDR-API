using System;
using System.Globalization;

namespace HDRClientRenderer
{
    // Local performance preferences, never accepted from a server or PB.
    // Zero disables that scheduling ceiling; resource/descriptor validation remains.
    public sealed class ClientRenderSettings
    {
        public int PixelBudget;
        public double ViewportMultiplier;
        public int CapturePasses = 6;
        public double CaptureRate;
        public bool Adaptive = true;
        public bool DisplayOcclusion;
        public int TileLimit=16;
        public double TilePassCost=16384;
        internal bool Valid
        {
            get { return PixelBudget >= 0 && CapturePasses >= 0 && Finite(ViewportMultiplier) && ViewportMultiplier >= 0 &&
                Finite(CaptureRate) && CaptureRate >= 0 && TileLimit>=1&&TileLimit<=64&&Finite(TilePassCost)&&TilePassCost>=0; }
        }
        static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        internal string Describe()
        {
            return "HDR camera: pixels " + (PixelBudget > 0 ? PixelBudget.ToString() : ViewportMultiplier > 0 ?
                "viewport x" + ViewportMultiplier.ToString(CultureInfo.InvariantCulture) : "off") +
                "; passes " + (CapturePasses == 0 ? "off" : CapturePasses.ToString()) +
                "; rate " + (CaptureRate == 0 ? "requested" : CaptureRate.ToString(CultureInfo.InvariantCulture) + " Hz") +
                "; adaptive " + (Adaptive ? "on" : "off") + "; occlusion " + (DisplayOcclusion ? "on" : "off") + "; tiles "+TileLimit+"; extra-pass cost "+TilePassCost.ToString(CultureInfo.InvariantCulture)+" pixel equivalents.";
        }
        internal bool Apply(string command)
        {
            var p=(command??"").Trim().ToLowerInvariant().Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
            if(p.Length==2&&p[0]=="adaptive"&&(p[1]=="on"||p[1]=="off")) { Adaptive=p[1]=="on"; return true; }
            if(p.Length==2&&p[0]=="occlusion"&&(p[1]=="on"||p[1]=="off")) { DisplayOcclusion=p[1]=="on"; return true; }
            if(p.Length==3&&p[0]=="pixels"&&p[1]=="viewport")
            { double scale; if(!double.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out scale)||!Finite(scale)||scale<=0)return false;
                ViewportMultiplier=scale;PixelBudget=0;return true; }
            if(p.Length!=2)return false;
            if(p[0]=="tiles")
            {int tiles;if(!int.TryParse(p[1],NumberStyles.None,CultureInfo.InvariantCulture,out tiles)||tiles<1||tiles>64)return false;TileLimit=tiles;return true;}
            if(p[0]=="tile-cost")
            {double cost;if(!double.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out cost)||!Finite(cost)||cost<0)return false;TilePassCost=cost;return true;}
            string value=p[1]=="off"||p[1]=="unlimited"?"0":p[1];
            if(p[0]=="pixels"||p[0]=="passes")
            { int n;if(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out n)||n<0)return false;
                if(p[0]=="pixels"){PixelBudget=n;ViewportMultiplier=0;}else CapturePasses=n;return true; }
            if(p[0]=="rate")
            { double hz;if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out hz)||!Finite(hz)||hz<0)return false;
                CaptureRate=hz;return true; }
            return false;
        }
    }
}
