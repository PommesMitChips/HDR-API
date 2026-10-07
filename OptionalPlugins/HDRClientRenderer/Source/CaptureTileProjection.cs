using System;
using VRageMath;

namespace HDRClientRenderer
{
    internal static class CaptureTileProjection
    {
        internal struct View
        {
            internal readonly float Fov, OffsetX, OffsetY;
            internal readonly Matrix Near, Far;
            internal View(float fov, float x, float y, Matrix near, Matrix far)
            { Fov = fov; OffsetX = x; OffsetY = y; Near = near; Far = far; }
        }
        internal static View Create(double fovDegrees, Matrix originalNear, Matrix originalFar, CaptureTileAtlas.Tile tile)
        {
            double width = tile.SourceMaxU - tile.SourceMinU, height = tile.SourceMaxV - tile.SourceMinV;
            if (fovDegrees < 60 || fovDegrees > 120 || width <= 0 || Math.Abs(width - height) > 1e-10)
                throw new ArgumentException("Invalid bounded square camera crop.");
            float fov = (float)(2 * Math.Atan(Math.Tan(fovDegrees * Math.PI / 360) * height));
            float x = (float)((tile.SourceMinU + tile.SourceMaxU - 1) / width);
            float y = (float)((1 - tile.SourceMinV - tile.SourceMaxV) / height);
            // Keep the renderer's existing clipped near/far depth conventions.
            // The distant projection can use a different near plane for precision.
            var clipped = originalNear; var distant = originalFar;
            float scale = (float)(1 / Math.Tan(fov * .5));
            clipped.M11 = clipped.M22 = distant.M11 = distant.M22 = scale;
            clipped.M31 = distant.M31 = x; clipped.M32 = distant.M32 = y;
            return new View(fov, x, y, clipped, distant);
        }
    }
}
