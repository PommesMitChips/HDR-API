using System;

namespace HDRClientRenderer
{
    // Converts the complete rendered triangle list to a conservative screen
    // rectangle. Callers supply CURRENT primary-view clip coordinates, including
    // every tile after the display's crop and pose. No sparse sample is accepted.
    // Near-plane/eye crossings conservatively retain demand rather than trying
    // to infer an enclosure from a partially projected polygon.
    internal static class DisplayOcclusionProjection
    {
        internal const int MaxVertices = 384;
        const double EyeEpsilon = 1e-8, ReverseDepthPadding = 1e-5;
        internal struct Vertex
        {
            internal readonly double X, Y, Z, W;
            internal Vertex(double x, double y, double z, double w)
            { X = x; Y = y; Z = z; W = w; }
        }

        internal static bool TryEnclose(Vertex[] completeTriangles, int width, int height,
            out DisplayOcclusion.Footprint footprint)
        {
            footprint = default(DisplayOcclusion.Footprint);
            if (completeTriangles == null || completeTriangles.Length < 3 ||
                completeTriangles.Length > MaxVertices || completeTriangles.Length % 3 != 0 ||
                width < 1 || height < 1 || width > 16384 || height > 16384) return false;
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity, maxY = double.NegativeInfinity, closest = 0;
            foreach (var v in completeTriangles)
            {
                if (!Finite(v.X) || !Finite(v.Y) || !Finite(v.Z) || !Finite(v.W) ||
                    v.W <= EyeEpsilon || v.Z < 0 || v.Z > v.W) return false;
                double x = v.X / v.W, y = v.Y / v.W, depth = v.Z / v.W;
                if (!Finite(x) || !Finite(y) || !Finite(depth)) return false;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                closest = Math.Max(closest, depth);
            }
            // Positive W means projected edges and interiors lie in the convex
            // hull of projected vertices. Clipping a rectangle to the viewport
            // therefore still contains every surviving display sample.
            if (minX > 1 || maxX < -1 || minY > 1 || maxY < -1) return false;
            minX = Clamp(minX); maxX = Clamp(maxX); minY = Clamp(minY); maxY = Clamp(maxY);
            int left = Math.Max(0, (int)Math.Floor((minX + 1) * .5 * width) - 1);
            int right = Math.Min(width, (int)Math.Ceiling((maxX + 1) * .5 * width) + 1);
            int top = Math.Max(0, (int)Math.Floor((1 - maxY) * .5 * height) - 1);
            int bottom = Math.Min(height, (int)Math.Ceiling((1 - minY) * .5 * height) + 1);
            if (left >= right || top >= bottom) return false;
            // Reverse depth is largest nearest the viewer. Move the entire proxy
            // toward the viewer to avoid float conversion or self-depth turning
            // a visible display into zero samples. The backend must use >= and
            // rasterize every sample in the rectangle, with no stencil rejection.
            footprint = new DisplayOcclusion.Footprint(left, top, right, bottom,
                Math.Min(1, closest + ReverseDepthPadding), true);
            return true;
        }

        static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        static double Clamp(double x) { return Math.Max(-1, Math.Min(1, x)); }
    }
}
