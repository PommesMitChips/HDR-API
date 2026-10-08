using System;
using VRageMath;

namespace HoloMap
{
    // The native pointer is normalized over the full Form.ClientSize. Render
    // resolution may differ (DRS), but it must cover that same full output.
    public static class UiPointerRay
    {
        public static bool Try(double normX, double normY, Vector2 nativeArea,
            Vector2 viewportSize, Vector2 viewportOffset, MatrixD view,
            MatrixD projection, Vector3D cameraPosition, Vector3D cameraForward,
            out Vector3D origin, out Vector3D direction)
        {
            origin = Vector3D.Zero;
            direction = Vector3D.Zero;
            if (!Finite(normX) || !Finite(normY) || normX < 0 || normX > 1
                || normY < 0 || normY > 1
                || !Area(nativeArea) || !Area(viewportSize)
                || !Finite(viewportOffset.X) || !Finite(viewportOffset.Y)
                || viewportOffset.X != 0 || viewportOffset.Y != 0
                || !Finite(cameraPosition) || !Finite(view) || !Finite(projection)) return false;

            double nativeAspect = (double)nativeArea.X / nativeArea.Y;
            // Rounding each render axis to the nearest pixel contributes at
            // most half a pixel: |renderW - aspect * renderH| <= .5*(1+aspect).
            // Actual dimensions need not match: normalized fractions survive DRS.
            double aspectError = Math.Abs((double)viewportSize.X - nativeAspect * viewportSize.Y);
            if (!Finite(aspectError) || aspectError > .5 * (1 + nativeAspect)) return false;

            Vector3D forward;
            if (!Normalize(cameraForward, out forward)) return false;
            MatrixD viewProjection = view * projection;
            if (!Finite(viewProjection)) return false;

            // Prefer the actual matrix: normalizing a view with a very large
            // translation can otherwise underflow an invertible determinant.
            double determinant = viewProjection.Determinant();
            MatrixD inverse, inverseBasis = viewProjection;
            if (Finite(determinant) && determinant != 0)
                inverse = MatrixD.Invert(viewProjection);
            else
            {
                // Uniform binary scaling preserves stored coefficients and
                // proportional rows. Dividing by an arbitrary maximum can round
                // an exactly singular matrix into an apparently invertible one.
                double maximum = Maximum(viewProjection);
                if (maximum == 0) return false;
                double exponent = Math.Floor(Math.Log(maximum) / Math.Log(2));
                exponent = Math.Max(-1022, Math.Min(1023, exponent));
                MatrixD scaled = Scale(viewProjection, Math.Pow(2, exponent));
                determinant = scaled.Determinant();
                if (!Finite(determinant) || determinant == 0) return false;
                inverse = MatrixD.Invert(scaled);
                inverseBasis = scaled;
            }
            if (!Finite(inverse)) return false;
            // A singular matrix can acquire a nonzero cofactor determinant from
            // cancellation. Require both products to reproduce identity within
            // 1e-7 per entry. Large translations may amplify cancellation; retry
            // the same inverse in the camera-relative basis in that case.
            if (!Identity(inverseBasis * inverse) || !Identity(inverse * inverseBasis))
            {
                MatrixD relativeBasis = MatrixD.CreateTranslation(cameraPosition) * inverseBasis;
                MatrixD relativeInverse = inverse * MatrixD.CreateTranslation(-cameraPosition);
                if (!Finite(relativeBasis) || !Finite(relativeInverse)
                    || !Identity(relativeBasis * relativeInverse)
                    || !Identity(relativeInverse * relativeBasis)) return false;
            }

            // VRageMath uses row vectors. NDC is viewport-local and Y points up.
            double x = 2 * normX - 1, y = 1 - 2 * normY;
            double worldX = x * inverse.M11 + y * inverse.M21 + .5 * inverse.M31 + inverse.M41;
            double worldY = x * inverse.M12 + y * inverse.M22 + .5 * inverse.M32 + inverse.M42;
            double worldZ = x * inverse.M13 + y * inverse.M23 + .5 * inverse.M33 + inverse.M43;
            double worldW = x * inverse.M14 + y * inverse.M24 + .5 * inverse.M34 + inverse.M44;
            if (!Finite(worldX) || !Finite(worldY) || !Finite(worldZ)
                || !Finite(worldW) || worldW == 0) return false;
            var worldPoint = new Vector3D(worldX / worldW, worldY / worldW, worldZ / worldW);
            if (!Finite(worldPoint)) return false;
            Vector3D ray;
            if (!Normalize(worldPoint - cameraPosition, out ray)) return false;
            double facing = Vector3D.Dot(ray, forward);
            if (!Finite(facing) || facing <= 0) return false;
            origin = cameraPosition;
            direction = ray;
            return true;
        }

        static bool Area(Vector2 area)
        { return Finite(area.X) && Finite(area.Y) && area.X >= 1 && area.Y >= 1; }
        static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
        static bool Finite(Vector3D value)
        { return Finite(value.X) && Finite(value.Y) && Finite(value.Z); }
        static bool Finite(MatrixD m)
        {
            return Finite(m.M11) && Finite(m.M12) && Finite(m.M13) && Finite(m.M14)
                && Finite(m.M21) && Finite(m.M22) && Finite(m.M23) && Finite(m.M24)
                && Finite(m.M31) && Finite(m.M32) && Finite(m.M33) && Finite(m.M34)
                && Finite(m.M41) && Finite(m.M42) && Finite(m.M43) && Finite(m.M44);
        }
        static bool Identity(MatrixD m)
        {
            const double tolerance = 1e-7;
            return Finite(m) && Math.Abs(m.M11 - 1) <= tolerance && Math.Abs(m.M22 - 1) <= tolerance
                && Math.Abs(m.M33 - 1) <= tolerance && Math.Abs(m.M44 - 1) <= tolerance
                && Math.Abs(m.M12) <= tolerance && Math.Abs(m.M13) <= tolerance && Math.Abs(m.M14) <= tolerance
                && Math.Abs(m.M21) <= tolerance && Math.Abs(m.M23) <= tolerance && Math.Abs(m.M24) <= tolerance
                && Math.Abs(m.M31) <= tolerance && Math.Abs(m.M32) <= tolerance && Math.Abs(m.M34) <= tolerance
                && Math.Abs(m.M41) <= tolerance && Math.Abs(m.M42) <= tolerance && Math.Abs(m.M43) <= tolerance;
        }
        static bool Normalize(Vector3D value, out Vector3D normalized)
        {
            normalized = Vector3D.Zero;
            if (!Finite(value)) return false;
            double maximum = Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
            if (maximum == 0) return false;
            var scaled = value / maximum;
            // Each component is bounded by one before squaring, avoiding both
            // overflow and underflow for finite nonzero vectors.
            double length = Math.Sqrt(scaled.X * scaled.X + scaled.Y * scaled.Y + scaled.Z * scaled.Z);
            if (!Finite(length) || length == 0) return false;
            normalized = scaled / length;
            return Finite(normalized);
        }
        static double Maximum(MatrixD m)
        {
            double a = Math.Max(Math.Abs(m.M11), Math.Max(Math.Abs(m.M12), Math.Max(Math.Abs(m.M13), Math.Abs(m.M14))));
            double b = Math.Max(Math.Abs(m.M21), Math.Max(Math.Abs(m.M22), Math.Max(Math.Abs(m.M23), Math.Abs(m.M24))));
            double c = Math.Max(Math.Abs(m.M31), Math.Max(Math.Abs(m.M32), Math.Max(Math.Abs(m.M33), Math.Abs(m.M34))));
            double d = Math.Max(Math.Abs(m.M41), Math.Max(Math.Abs(m.M42), Math.Max(Math.Abs(m.M43), Math.Abs(m.M44))));
            return Math.Max(Math.Max(a, b), Math.Max(c, d));
        }
        static MatrixD Scale(MatrixD m, double divisor)
        {
            m.M11 /= divisor; m.M12 /= divisor; m.M13 /= divisor; m.M14 /= divisor;
            m.M21 /= divisor; m.M22 /= divisor; m.M23 /= divisor; m.M24 /= divisor;
            m.M31 /= divisor; m.M32 /= divisor; m.M33 /= divisor; m.M34 /= divisor;
            m.M41 /= divisor; m.M42 /= divisor; m.M43 /= divisor; m.M44 /= divisor;
            return m;
        }
    }
}
