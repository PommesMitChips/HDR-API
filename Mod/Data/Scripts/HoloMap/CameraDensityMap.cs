using System;
using VRageMath;

namespace HoloMap
{
    // These axes use the same anchor-relative frame as the panorama shader.
    // Camera translation affects capture content, not its angular UV footprint.
    public sealed class CameraDensityLens
    {
        public Vector3D Right = Vector3D.UnitX, Up = Vector3D.UnitY, Forward = Vector3D.UnitZ;
        public double TanHorizontal = 1, TanVertical = 1, FeatherRadians;
        public int CaptureMax = 1024;
    }

    public struct CameraDensityCell
    {
        public bool Covered, HasSample;
        // Pixels / normalized source-camera UV, and pixels squared / UV area.
        // All density channels saturate at CaptureMax or CaptureMax squared.
        // Upper channels bound actionable demand, not unbounded geometric density.
        public double Magnification, Area, UpperMagnification, UpperArea;
        // Normalized contribution of this lens to the shader's blended pixel.
        // Samples are measurements; only BlendWeightUpper is conservative.
        public double BlendWeight, BlendWeightUpper;
    }

    public sealed class CameraDensityField
    {
        public readonly int GridSize, CaptureMax;
        public readonly CameraDensityCell[] Cells;
        public int SuggestedResolution;
        internal CameraDensityField(int gridSize, int captureMax)
        { GridSize = gridSize; CaptureMax = captureMax; Cells = new CameraDensityCell[gridSize * gridSize]; }
    }

    public sealed class CameraDensityResult
    {
        public readonly CameraDensityField[] Cameras;
        public int VisibleMask, TriangleWork, CellWork;
        public bool ConservativeFallback;
        internal CameraDensityResult(CameraDensityLens[] lenses, int gridSize)
        {
            Cameras = new CameraDensityField[lenses.Length];
            for (int i = 0; i < lenses.Length; i++) Cameras[i] = new CameraDensityField(gridSize, lenses[i].CaptureMax);
        }
    }

    public struct CameraVisibilityResult
    {
        public int VisibleMask, TriangleWork;
        public bool ConservativeFallback;
    }

    // Pure geometry/differential measurements only. No engine state, pixels,
    // raycasts, GPU calls, or assumptions about opaque/self-occluding surfaces.
    public static class CameraDensityMap
    {
        public const int MaxCameras = 6, MaxTriangleWork = 8192, MaxCellWork = 262144;
        const double Epsilon = 1e-9, TwoPi = Math.PI * 2;
        const int BufferSize = 64;

        struct Interval
        {
            internal double Low, High;
            internal Interval(double low, double high) { Low = low; High = high; }
            internal static Interval Point(double value) { return new Interval(value, value); }
            internal double AbsMax { get { return Math.Max(Math.Abs(Low), Math.Abs(High)); } }
            public static Interval operator +(Interval a, Interval b) { return new Interval(a.Low + b.Low, a.High + b.High); }
            public static Interval operator -(Interval a, Interval b) { return new Interval(a.Low - b.High, a.High - b.Low); }
            public static Interval operator *(Interval a, Interval b)
            {
                double aa = a.Low * b.Low, ab = a.Low * b.High, ba = a.High * b.Low, bb = a.High * b.High;
                return new Interval(Math.Min(Math.Min(aa, ab), Math.Min(ba, bb)), Math.Max(Math.Max(aa, ab), Math.Max(ba, bb)));
            }
            public static Interval operator *(Interval a, double b) { return a * Point(b); }
            internal static Interval Divide(Interval a, Interval b)
            { return a * new Interval(1 / b.High, 1 / b.Low); }
        }

        struct VectorInterval
        {
            internal Interval X, Y, Z;
            internal VectorInterval(Interval x, Interval y, Interval z) { X = x; Y = y; Z = z; }
            internal Interval Dot(Vector3D n) { return X * n.X + Y * n.Y + Z * n.Z; }
        }

        struct Vertex
        {
            internal Vector3D Local;
            internal double X, Y, Z, W, U, V;
            internal static Vertex Between(Vertex a, Vertex b, double t)
            {
                return new Vertex { Local = a.Local + (b.Local - a.Local) * t,
                    X = a.X + (b.X - a.X) * t, Y = a.Y + (b.Y - a.Y) * t,
                    Z = a.Z + (b.Z - a.Z) * t, W = a.W + (b.W - a.W) * t,
                    U = a.U + (b.U - a.U) * t, V = a.V + (b.V - a.V) * t };
            }
        }

        // Clip coordinates are affine in panorama UV on a rendered triangle.
        struct Affine
        {
            internal double U, V, C;
            internal double At(double u, double v) { return U * u + V * v + C; }
            internal Interval At(Interval u, Interval v) { return u * U + v * V + Interval.Point(C); }
            internal static Affine Fit(Vertex a, Vertex b, Vertex c, double av, double bv, double cv, double det)
            {
                double du = ((bv - av) * (c.V - a.V) - (cv - av) * (b.V - a.V)) / det;
                double dv = ((cv - av) * (b.U - a.U) - (bv - av) * (c.U - a.U)) / det;
                return new Affine { U = du, V = dv, C = av - du * a.U - dv * a.V };
            }
        }

        struct Projection
        {
            internal Affine X, Y, W;
            internal bool Valid;
            internal static Projection Fit(Vertex a, Vertex b, Vertex c)
            {
                double det = (b.U - a.U) * (c.V - a.V) - (c.U - a.U) * (b.V - a.V);
                if (!Finite(det) || Math.Abs(det) < 1e-16) return new Projection();
                return new Projection { Valid = true, X = Affine.Fit(a, b, c, a.X, b.X, c.X, det),
                    Y = Affine.Fit(a, b, c, a.Y, b.Y, c.Y, det), W = Affine.Fit(a, b, c, a.W, b.W, c.W, det) };
            }
        }

        static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
        static bool Finite(Vector3D p) { return Finite(p.X) && Finite(p.Y) && Finite(p.Z); }
        static bool ValidLens(CameraDensityLens lens)
        {
            return lens != null && Finite(lens.Right) && Finite(lens.Up) && Finite(lens.Forward) &&
                Math.Abs(lens.Right.LengthSquared() - 1) < .0001 && Math.Abs(lens.Up.LengthSquared() - 1) < .0001 &&
                Math.Abs(lens.Forward.LengthSquared() - 1) < .0001 && Math.Abs(Vector3D.Dot(lens.Right, lens.Up)) < .0001 &&
                Math.Abs(Vector3D.Dot(lens.Right, lens.Forward)) < .0001 && Math.Abs(Vector3D.Dot(lens.Up, lens.Forward)) < .0001 &&
                Finite(lens.TanHorizontal) && Finite(lens.TanVertical) && lens.TanHorizontal > 0 && lens.TanHorizontal < 16 &&
                lens.TanVertical > 0 && lens.TanVertical < 16 && Finite(lens.FeatherRadians) && lens.FeatherRadians >= 0 &&
                lens.FeatherRadians <= Math.PI * 25 / 180 && (lens.CaptureMax == 256 || lens.CaptureMax == 512 || lens.CaptureMax == 1024 || lens.CaptureMax == 2048);
        }

        static Vertex Transform(Vector3D p, Vector2 uv, MatrixD m)
        {
            return new Vertex { Local = p, U = uv.X, V = uv.Y,
                X = p.X * m.M11 + p.Y * m.M21 + p.Z * m.M31 + m.M41,
                Y = p.X * m.M12 + p.Y * m.M22 + p.Z * m.M32 + m.M42,
                Z = p.X * m.M13 + p.Y * m.M23 + p.Z * m.M33 + m.M43,
                W = p.X * m.M14 + p.Y * m.M24 + p.Z * m.M34 + m.M44 };
        }
        static bool Finite(Vertex p)
        { return Finite(p.Local) && Finite(p.X) && Finite(p.Y) && Finite(p.Z) && Finite(p.W) && Finite(p.U) && Finite(p.V) &&
            Math.Abs(p.Local.X) <= 1e100 && Math.Abs(p.Local.Y) <= 1e100 && Math.Abs(p.Local.Z) <= 1e100 &&
            Math.Abs(p.X) <= 1e100 && Math.Abs(p.Y) <= 1e100 && Math.Abs(p.Z) <= 1e100 && Math.Abs(p.W) <= 1e100; }

        static double Plane(Vertex p, int plane, Vector4D crop, double horizontalGuard, double verticalGuard)
        {
            switch (plane)
            {
                case 0: return p.W - Epsilon;
                case 1: return p.Z;
                case 2: return p.W - p.Z;
                case 3: return p.W * horizontalGuard + p.X;
                case 4: return p.W * horizontalGuard - p.X;
                case 5: return p.W * verticalGuard + p.Y;
                case 6: return p.W * verticalGuard - p.Y;
                default: return crop.W - crop.X * p.Local.X - crop.Y * p.Local.Y - crop.Z * p.Local.Z;
            }
        }
        static int Clip(Vertex[] source, int count, Vertex[] result, int plane, Vector4D crop, double gx, double gy)
        {
            if (count < 3) return 0;
            int written = 0; Vertex previous = source[count - 1]; double pd = Plane(previous, plane, crop, gx, gy);
            for (int i = 0; i < count; i++)
            {
                Vertex current = source[i]; double cd = Plane(current, plane, crop, gx, gy);
                if ((pd >= 0) != (cd >= 0)) result[written++] = Vertex.Between(previous, current, pd / (pd - cd));
                if (cd >= 0) result[written++] = current;
                previous = current; pd = cd;
            }
            return written;
        }
        static double UvPlane(Vertex p, int plane, double u0, double u1, double v0, double v1)
        { return plane == 0 ? p.U - u0 : plane == 1 ? u1 - p.U : plane == 2 ? p.V - v0 : v1 - p.V; }
        static int ClipUv(Vertex[] source, int count, Vertex[] result, int plane, double u0, double u1, double v0, double v1)
        {
            if (count < 3) return 0;
            int written = 0; Vertex previous = source[count - 1]; double pd = UvPlane(previous, plane, u0, u1, v0, v1);
            for (int i = 0; i < count; i++)
            {
                Vertex current = source[i]; double cd = UvPlane(current, plane, u0, u1, v0, v1);
                if ((pd >= 0) != (cd >= 0)) result[written++] = Vertex.Between(previous, current, pd / (pd - cd));
                if (cd >= 0) result[written++] = current;
                previous = current; pd = cd;
            }
            return written;
        }

        static Interval Sin(Interval a)
        {
            double low = Math.Min(Math.Sin(a.Low), Math.Sin(a.High)), high = Math.Max(Math.Sin(a.Low), Math.Sin(a.High));
            if (a.High - a.Low >= TwoPi) return new Interval(-1, 1);
            if (Math.Ceiling((a.Low - Math.PI / 2) / TwoPi) <= Math.Floor((a.High - Math.PI / 2) / TwoPi)) high = 1;
            if (Math.Ceiling((a.Low + Math.PI / 2) / TwoPi) <= Math.Floor((a.High + Math.PI / 2) / TwoPi)) low = -1;
            return new Interval(low - 1e-14, high + 1e-14);
        }
        static Interval Cos(Interval a) { return Sin(a + Interval.Point(Math.PI / 2)); }
        static void Rays(Interval u, Interval v, out VectorInterval ray, out VectorInterval du, out VectorInterval dv)
        {
            Interval longitude = (u - Interval.Point(.5)) * TwoPi, latitude = (Interval.Point(.5) - v) * Math.PI;
            Interval sl = Sin(longitude), cl = Cos(longitude), sp = Sin(latitude), cp = Cos(latitude), zero = Interval.Point(0);
            ray = new VectorInterval(cp * sl, sp, cp * cl);
            du = new VectorInterval(cp * cl * TwoPi, zero, cp * sl * -TwoPi);
            dv = new VectorInterval(sp * sl * Math.PI, cp * -Math.PI, sp * cl * Math.PI);
        }
        static VectorInterval RayBounds(Interval u, Interval v)
        {
            Interval longitude = (u - Interval.Point(.5)) * TwoPi, latitude = (Interval.Point(.5) - v) * Math.PI;
            Interval cosine = Cos(latitude);
            return new VectorInterval(cosine * Sin(longitude), Sin(latitude), cosine * Cos(longitude));
        }
        static Vector3D Ray(double u, double v)
        {
            double longitude = (u - .5) * TwoPi, latitude = (.5 - v) * Math.PI, cp = Math.Cos(latitude);
            return new Vector3D(cp * Math.Sin(longitude), Math.Sin(latitude), cp * Math.Cos(longitude));
        }
        static bool CameraBounds(VectorInterval ray, CameraDensityLens camera, out Interval sourceU, out Interval sourceV)
        {
            sourceU = sourceV = new Interval(0, 1);
            if (!CameraMayContribute(ray, camera)) return false;
            Interval z = ray.Dot(camera.Forward);
            if (z.Low <= Epsilon) return true;
            sourceU = Interval.Point(.5) + Interval.Divide(ray.Dot(camera.Right), z) * (.5 / camera.TanHorizontal);
            sourceV = Interval.Point(.5) - Interval.Divide(ray.Dot(camera.Up), z) * (.5 / camera.TanVertical);
            sourceU = new Interval(Math.Max(0, sourceU.Low - Epsilon), Math.Min(1, sourceU.High + Epsilon));
            sourceV = new Interval(Math.Max(0, sourceV.Low - Epsilon), Math.Min(1, sourceV.High + Epsilon));
            return sourceU.Low <= sourceU.High && sourceV.Low <= sourceV.High;
        }
        static bool CameraMayContribute(VectorInterval ray, CameraDensityLens camera)
        {
            if (ray.Dot(camera.Forward).High < -Epsilon ||
                ray.Dot(camera.Forward * camera.TanHorizontal + camera.Right).High < -Epsilon ||
                ray.Dot(camera.Forward * camera.TanHorizontal - camera.Right).High < -Epsilon ||
                ray.Dot(camera.Forward * camera.TanVertical + camera.Up).High < -Epsilon ||
                ray.Dot(camera.Forward * camera.TanVertical - camera.Up).High < -Epsilon) return false;
            return true;
        }

        static Interval LensDerivative(Interval numerator, Interval forward, Interval numeratorDerivative,
            Interval forwardDerivative, double scale)
        { return Interval.Divide(numeratorDerivative * forward - numerator * forwardDerivative, forward * forward) * scale; }

        static double Saturate(double value, double maximum)
        { return !Finite(value) || value > maximum ? maximum : Math.Max(0, value); }

        static void DensityBounds(Projection projection, Interval u, Interval v, VectorInterval ray, VectorInterval du,
            VectorInterval dv, CameraDensityLens camera, Vector2I viewport, out double magnification, out double area)
        {
            double cap = camera.CaptureMax; magnification = cap; area = cap * cap;
            if (!projection.Valid) return;
            Interval w = projection.W.At(u, v), z = ray.Dot(camera.Forward);
            if (w.Low <= Epsilon || z.Low <= Epsilon) return;
            Interval x = ray.Dot(camera.Right), y = ray.Dot(camera.Up), zu = du.Dot(camera.Forward), zv = dv.Dot(camera.Forward);
            Interval a = LensDerivative(x, z, du.Dot(camera.Right), zu, .5 / camera.TanHorizontal);
            Interval b = LensDerivative(x, z, dv.Dot(camera.Right), zv, .5 / camera.TanHorizontal);
            Interval c = LensDerivative(y, z, du.Dot(camera.Up), zu, -.5 / camera.TanVertical);
            Interval d = LensDerivative(y, z, dv.Dot(camera.Up), zv, -.5 / camera.TanVertical);
            Interval det = a * d - b * c;
            if (det.Low <= Epsilon && det.High >= -Epsilon) return;
            Interval sx = projection.X.At(u, v), sy = projection.Y.At(u, v), ww = w * w;
            Interval xu = Interval.Divide(w * projection.X.U - sx * projection.W.U, ww) * (viewport.X * .5);
            Interval xv = Interval.Divide(w * projection.X.V - sx * projection.W.V, ww) * (viewport.X * .5);
            Interval yu = Interval.Divide(w * projection.Y.U - sy * projection.W.U, ww) * (-viewport.Y * .5);
            Interval yv = Interval.Divide(w * projection.Y.V - sy * projection.W.V, ww) * (-viewport.Y * .5);
            Interval ja = Interval.Divide(xu * d - xv * c, det), jb = Interval.Divide(xv * a - xu * b, det);
            Interval jc = Interval.Divide(yu * d - yv * c, det), jd = Interval.Divide(yv * a - yu * b, det);
            // Frobenius norm is a conservative upper bound on the largest singular value.
            magnification = Saturate(Math.Sqrt(ja.AbsMax * ja.AbsMax + jb.AbsMax * jb.AbsMax + jc.AbsMax * jc.AbsMax + jd.AbsMax * jd.AbsMax) * (1 + 1e-9) + 1e-6, cap);
            area = Saturate((ja * jd - jb * jc).AbsMax * (1 + 1e-9) + 1e-6, cap * cap);
        }

        static double Weight(Vector3D ray, CameraDensityLens camera, out double sourceU, out double sourceV)
        {
            sourceU = sourceV = 0; double z = Vector3D.Dot(ray, camera.Forward);
            if (z <= .000001) return 0;
            double x = Vector3D.Dot(ray, camera.Right) / z, y = Vector3D.Dot(ray, camera.Up) / z;
            sourceU = .5 + .5 * x / camera.TanHorizontal; sourceV = .5 - .5 * y / camera.TanVertical;
            if (sourceU < -Epsilon || sourceU > 1 + Epsilon || sourceV < -Epsilon || sourceV > 1 + Epsilon) return 0;
            if (camera.FeatherRadians == 0) return 1;
            double margin = Math.Min(Math.Atan(camera.TanHorizontal) - Math.Atan(Math.Abs(x)), Math.Atan(camera.TanVertical) - Math.Atan(Math.Abs(y)));
            double t = Math.Max(0, Math.Min(1, margin / camera.FeatherRadians));
            return t * t * (3 - 2 * t);
        }

        static bool Inside(Vertex[] polygon, int count, double u, double v)
        {
            bool positive = false, negative = false;
            for (int i = 0; i < count; i++)
            {
                Vertex a = polygon[i], b = polygon[(i + 1) % count];
                double cross = (b.U - a.U) * (v - a.V) - (b.V - a.V) * (u - a.U);
                if (cross > 1e-11) positive = true; if (cross < -1e-11) negative = true;
                if (positive && negative) return false;
            }
            return true;
        }

        static void Sample(CameraDensityResult result, CameraDensityLens[] lenses, int cameraIndex, Projection projection,
            Vertex[] polygon, int count, Vector2I viewport, double u, double v)
        {
            if (!Inside(polygon, count, u, v)) return;
            CameraDensityLens camera = lenses[cameraIndex]; CameraDensityField field = result.Cameras[cameraIndex];
            Vector3D ray = Ray(u, v); double sourceU, sourceV, weight = Weight(ray, camera, out sourceU, out sourceV);
            if (weight <= 0) return;
            int ix = Math.Min(field.GridSize - 1, Math.Max(0, (int)(sourceU * field.GridSize)));
            int iy = Math.Min(field.GridSize - 1, Math.Max(0, (int)(sourceV * field.GridSize)));
            int index = iy * field.GridSize + ix; CameraDensityCell cell = field.Cells[index];
            // Sampling cannot create an unbounded extra region: interval bounds have already marked coverage.
            cell.Covered = true; cell.HasSample = true;
            double total = 0, unusedU, unusedV;
            for (int i = 0; i < lenses.Length; i++) total += Weight(ray, lenses[i], out unusedU, out unusedV);
            cell.BlendWeight = Math.Max(cell.BlendWeight, total > 0 ? weight / total : 1);
            cell.BlendWeightUpper = 1;
            double cap = camera.CaptureMax, magnification = cap, area = cap * cap;
            if (projection.Valid)
            {
                double w = projection.W.At(u, v), z = Vector3D.Dot(ray, camera.Forward);
                double longitude = (u - .5) * TwoPi, latitude = (.5 - v) * Math.PI;
                Vector3D ru = new Vector3D(Math.Cos(latitude) * Math.Cos(longitude), 0, -Math.Cos(latitude) * Math.Sin(longitude)) * TwoPi;
                Vector3D rv = new Vector3D(Math.Sin(latitude) * Math.Sin(longitude), -Math.Cos(latitude), Math.Sin(latitude) * Math.Cos(longitude)) * Math.PI;
                double x = Vector3D.Dot(ray, camera.Right), y = Vector3D.Dot(ray, camera.Up);
                double zu = Vector3D.Dot(ru, camera.Forward), zv = Vector3D.Dot(rv, camera.Forward);
                double a = (Vector3D.Dot(ru, camera.Right) * z - x * zu) / (2 * camera.TanHorizontal * z * z);
                double b = (Vector3D.Dot(rv, camera.Right) * z - x * zv) / (2 * camera.TanHorizontal * z * z);
                double c = -(Vector3D.Dot(ru, camera.Up) * z - y * zu) / (2 * camera.TanVertical * z * z);
                double d = -(Vector3D.Dot(rv, camera.Up) * z - y * zv) / (2 * camera.TanVertical * z * z);
                double determinant = a * d - b * c;
                if (w > Epsilon && Math.Abs(determinant) > 1e-14)
                {
                    double sx = projection.X.At(u, v), sy = projection.Y.At(u, v);
                    double xu = (projection.X.U * w - sx * projection.W.U) / (w * w) * viewport.X * .5;
                    double xv = (projection.X.V * w - sx * projection.W.V) / (w * w) * viewport.X * .5;
                    double yu = -(projection.Y.U * w - sy * projection.W.U) / (w * w) * viewport.Y * .5;
                    double yv = -(projection.Y.V * w - sy * projection.W.V) / (w * w) * viewport.Y * .5;
                    double ja = (xu * d - xv * c) / determinant, jb = (xv * a - xu * b) / determinant;
                    double jc = (yu * d - yv * c) / determinant, jd = (yv * a - yu * b) / determinant;
                    double trace = ja * ja + jb * jb + jc * jc + jd * jd, product = ja * jd - jb * jc;
                    magnification = Math.Sqrt(.5 * (trace + Math.Sqrt(Math.Max(0, trace * trace - 4 * product * product))));
                    area = Math.Abs(product);
                }
            }
            cell.Magnification = Math.Max(cell.Magnification, Saturate(magnification, cap));
            cell.Area = Math.Max(cell.Area, Saturate(area, cap * cap));
            cell.UpperMagnification = Math.Max(cell.UpperMagnification, cell.Magnification);
            cell.UpperArea = Math.Max(cell.UpperArea, cell.Area);
            field.Cells[index] = cell;
        }

        static bool Charge(CameraDensityResult result, int budget)
        { if (result.CellWork >= budget) return false; result.CellWork++; return true; }

        static CameraDensityResult Fallback(CameraDensityResult result)
        {
            result.ConservativeFallback = true; result.VisibleMask = (1 << result.Cameras.Length) - 1;
            foreach (CameraDensityField field in result.Cameras)
            {
                double cap = field.CaptureMax; field.SuggestedResolution = field.CaptureMax;
                for (int i = 0; i < field.Cells.Length; i++)
                    field.Cells[i] = new CameraDensityCell { Covered = true, Magnification = cap, Area = cap * cap,
                        UpperMagnification = cap, UpperArea = cap * cap, BlendWeightUpper = 1 };
            }
            return result;
        }

        static bool Accumulate(CameraDensityResult result, CameraDensityLens[] lenses, Vertex[] polygon, int count,
            Projection projection, Vector2I viewport, int cellBudget, double filterUv)
        {
            double u0 = 1, u1 = 0, v0 = 1, v1 = 0;
            for (int i = 0; i < count; i++) { u0 = Math.Min(u0, polygon[i].U); u1 = Math.Max(u1, polygon[i].U); v0 = Math.Min(v0, polygon[i].V); v1 = Math.Max(v1, polygon[i].V); }
            // Expand in panorama UV for the composed texture's filtering footprint.
            Interval u = new Interval(Math.Max(0, u0 - filterUv), Math.Min(1, u1 + filterUv));
            Interval v = new Interval(Math.Max(0, v0 - filterUv), Math.Min(1, v1 + filterUv));
            VectorInterval ray, du, dv; Rays(u, v, out ray, out du, out dv);
            for (int cameraIndex = 0; cameraIndex < lenses.Length; cameraIndex++)
            {
                if (!Charge(result, cellBudget)) return false;
                CameraDensityLens camera = lenses[cameraIndex]; CameraDensityField field = result.Cameras[cameraIndex]; Interval su, sv;
                if (!CameraBounds(ray, camera, out su, out sv)) continue;
                int x0 = Math.Max(0, Math.Min(field.GridSize - 1, (int)Math.Floor(su.Low * field.GridSize)));
                int x1 = Math.Max(0, Math.Min(field.GridSize - 1, (int)Math.Floor(su.High * field.GridSize)));
                int y0 = Math.Max(0, Math.Min(field.GridSize - 1, (int)Math.Floor(sv.Low * field.GridSize)));
                int y1 = Math.Max(0, Math.Min(field.GridSize - 1, (int)Math.Floor(sv.High * field.GridSize)));
                double magnification, area; DensityBounds(projection, u, v, ray, du, dv, camera, viewport, out magnification, out area);
                result.VisibleMask |= 1 << cameraIndex;
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    if (!Charge(result, cellBudget)) return false;
                    int index = y * field.GridSize + x; CameraDensityCell cell = field.Cells[index]; cell.Covered = true;
                    cell.UpperMagnification = Math.Max(cell.UpperMagnification, magnification); cell.UpperArea = Math.Max(cell.UpperArea, area); cell.BlendWeightUpper = 1;
                    field.Cells[index] = cell;
                    // Inverse-map the camera cell centre. This samples a field in
                    // SOURCE-camera coordinates, not a panorama or viewport grid.
                    double sourceU = (x + .5) / field.GridSize, sourceV = (y + .5) / field.GridSize;
                    Vector3D direction = Vector3D.Normalize(camera.Forward + camera.Right * ((sourceU * 2 - 1) * camera.TanHorizontal) + camera.Up * ((1 - sourceV * 2) * camera.TanVertical));
                    double pu = .5 + Math.Atan2(direction.X, direction.Z) / TwoPi;
                    double pv = .5 - Math.Asin(Math.Max(-1, Math.Min(1, direction.Y))) / Math.PI;
                    Sample(result, lenses, cameraIndex, projection, polygon, count, viewport, pu, pv);
                    // The longitude seam has two distinct UV endpoints.
                    if (pu < Epsilon) Sample(result, lenses, cameraIndex, projection, polygon, count, viewport, 1, pv);
                    else if (pu > 1 - Epsilon) Sample(result, lenses, cameraIndex, projection, polygon, count, viewport, 0, pv);
                }
                // Thin/sliver coverage can miss every cell centre. Vertex witnesses
                // retain its measured demand; the interval upper field covers it all.
                for (int i = 0; i < count; i++) Sample(result, lenses, cameraIndex, projection, polygon, count, viewport, polygon[i].U, polygon[i].V);
            }
            return true;
        }

        static bool ValidGeometry(Vector3D[] points, int[] triangles, Vector2[] panoramaUv, Vector2I viewport, Vector4D[] crop)
        {
            if (points == null || triangles == null || panoramaUv == null || panoramaUv.Length != points.Length || points.Length > 65536 ||
                triangles.Length % 3 != 0 || viewport.X <= 0 || viewport.Y <= 0 || crop != null && crop.Length > 32) return false;
            if (crop != null) foreach (Vector4D p in crop)
                if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z) || !Finite(p.W) || Math.Abs(p.X) > 1e100 ||
                    Math.Abs(p.Y) > 1e100 || Math.Abs(p.Z) > 1e100 || Math.Abs(p.W) > 1e100) return false;
            return true;
        }
        static CameraVisibilityResult VisibilityFallback(int cameraCount, int triangleWork)
        { return new CameraVisibilityResult { VisibleMask = (1 << cameraCount) - 1, ConservativeFallback = true, TriangleWork = triangleWork }; }

        // Cheap per-frame path. A single UV interval rectangle per clipped
        // triangle conservatively encloses every contributing camera. It does
        // not allocate density fields, subdivide UV tiles, or evaluate Jacobians.
        // The more expensive Evaluate() is intended for infrequent density refresh.
        public static CameraVisibilityResult EvaluateVisibility(Vector3D[] points, int[] triangles, Vector2[] panoramaUv,
            MatrixD localToClip, Vector2I viewport, Vector4D[] localCropPlanes, CameraDensityLens[] cameras,
            int triangleBudget = MaxTriangleWork, double pixelGuard = 2, double panoramaFilterUv = 1d / 512)
        {
            if (cameras == null || cameras.Length < 1 || cameras.Length > MaxCameras || triangleBudget < 0 || triangleBudget > MaxTriangleWork ||
                !Finite(pixelGuard) || pixelGuard < 0 || pixelGuard > 64 || !Finite(panoramaFilterUv) || panoramaFilterUv < 0 || panoramaFilterUv > .0625)
                throw new ArgumentException("Invalid bounded camera visibility request.");
            foreach (CameraDensityLens camera in cameras) if (!ValidLens(camera)) throw new ArgumentException("Invalid camera visibility lens.");
            if (!ValidGeometry(points, triangles, panoramaUv, viewport, localCropPlanes)) return VisibilityFallback(cameras.Length, 0);
            int cropCount = localCropPlanes == null ? 0 : localCropPlanes.Length, fullMask = (1 << cameras.Length) - 1;
            var result = new CameraVisibilityResult();
            var clipA = new Vertex[BufferSize]; var clipB = new Vertex[BufferSize];
            double gx = 1 + pixelGuard * 2 / viewport.X, gy = 1 + pixelGuard * 2 / viewport.Y;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                if (result.TriangleWork >= triangleBudget) return VisibilityFallback(cameras.Length, result.TriangleWork); result.TriangleWork++;
                for (int i = 0; i < 3; i++)
                {
                    int index = triangles[t + i]; if (index < 0 || index >= points.Length) return VisibilityFallback(cameras.Length, result.TriangleWork);
                    clipA[i] = Transform(points[index], panoramaUv[index], localToClip);
                    if (!Finite(clipA[i]) || clipA[i].U < -Epsilon || clipA[i].U > 1 + Epsilon || clipA[i].V < -Epsilon || clipA[i].V > 1 + Epsilon)
                        return VisibilityFallback(cameras.Length, result.TriangleWork);
                }
                Vertex[] read = clipA, write = clipB; int count = 3;
                for (int plane = 0; plane < 7 + cropCount && count >= 3; plane++)
                {
                    count = Clip(read, count, write, plane, plane < 7 ? new Vector4D() : localCropPlanes[plane - 7], gx, gy);
                    Vertex[] swap = read; read = write; write = swap;
                }
                if (count < 3) continue;
                double u0 = 1, u1 = 0, v0 = 1, v1 = 0;
                for (int i = 0; i < count; i++) { u0 = Math.Min(u0, read[i].U); u1 = Math.Max(u1, read[i].U); v0 = Math.Min(v0, read[i].V); v1 = Math.Max(v1, read[i].V); }
                VectorInterval ray = RayBounds(new Interval(Math.Max(0, u0 - panoramaFilterUv), Math.Min(1, u1 + panoramaFilterUv)),
                    new Interval(Math.Max(0, v0 - panoramaFilterUv), Math.Min(1, v1 + panoramaFilterUv)));
                for (int camera = 0; camera < cameras.Length; camera++)
                {
                    if ((result.VisibleMask & (1 << camera)) != 0) continue;
                    if (CameraMayContribute(ray, cameras[camera])) result.VisibleMask |= 1 << camera;
                }
                // Once every camera is demanded no later triangle can add work.
                if (result.VisibleMask == fullMask) return result;
            }
            return result;
        }

        // localCropPlanes use dot(normal, localPoint) <= W. Supply the EXACT
        // rendered source mesh, already mirrored/warped/joined, with its UVs.
        // A caller with unknown/stale geometry supplies null and gets full demand.
        // Both shell surfaces remain relevant; this method does no backface/depth culling.
        public static CameraDensityResult Evaluate(Vector3D[] points, int[] triangles, Vector2[] panoramaUv,
            MatrixD localToClip, Vector2I viewport, Vector4D[] localCropPlanes, CameraDensityLens[] cameras,
            int gridSize = 16, int triangleBudget = MaxTriangleWork, int cellWorkBudget = 65536,
            double pixelGuard = 2, double panoramaFilterUv = 1d / 512)
        {
            if (cameras == null || cameras.Length < 1 || cameras.Length > MaxCameras || gridSize != 16 && gridSize != 32 ||
                triangleBudget < 0 || triangleBudget > MaxTriangleWork || cellWorkBudget < 0 || cellWorkBudget > MaxCellWork ||
                !Finite(pixelGuard) || pixelGuard < 0 || pixelGuard > 64 || !Finite(panoramaFilterUv) || panoramaFilterUv < 0 || panoramaFilterUv > .0625)
                throw new ArgumentException("Invalid bounded camera density request.");
            foreach (CameraDensityLens camera in cameras) if (!ValidLens(camera)) throw new ArgumentException("Invalid camera density lens.");
            var result = new CameraDensityResult(cameras, gridSize);
            if (!ValidGeometry(points, triangles, panoramaUv, viewport, localCropPlanes)) return Fallback(result);
            int cropCount = localCropPlanes == null ? 0 : localCropPlanes.Length;
            var clipA = new Vertex[BufferSize]; var clipB = new Vertex[BufferSize];
            var tileA = new Vertex[BufferSize]; var tileB = new Vertex[BufferSize];
            double gx = 1 + pixelGuard * 2 / viewport.X, gy = 1 + pixelGuard * 2 / viewport.Y;
            int tilesX = gridSize * 2, tilesY = gridSize;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                if (result.TriangleWork >= triangleBudget) return Fallback(result); result.TriangleWork++;
                for (int i = 0; i < 3; i++)
                {
                    int index = triangles[t + i]; if (index < 0 || index >= points.Length) return Fallback(result);
                    clipA[i] = Transform(points[index], panoramaUv[index], localToClip);
                    if (!Finite(clipA[i]) || clipA[i].U < -Epsilon || clipA[i].U > 1 + Epsilon || clipA[i].V < -Epsilon || clipA[i].V > 1 + Epsilon) return Fallback(result);
                }
                Projection projection = Projection.Fit(clipA[0], clipA[1], clipA[2]);
                Vertex[] read = clipA, write = clipB; int count = 3;
                for (int plane = 0; plane < 7 + cropCount && count >= 3; plane++)
                {
                    count = Clip(read, count, write, plane, plane < 7 ? new Vector4D() : localCropPlanes[plane - 7], gx, gy);
                    Vertex[] swap = read; read = write; write = swap;
                }
                if (count < 3) continue;
                if (!projection.Valid) return Fallback(result);
                double u0 = 1, u1 = 0, v0 = 1, v1 = 0;
                for (int i = 0; i < count; i++) { u0 = Math.Min(u0, read[i].U); u1 = Math.Max(u1, read[i].U); v0 = Math.Min(v0, read[i].V); v1 = Math.Max(v1, read[i].V); }
                int tx0 = Math.Max(0, Math.Min(tilesX - 1, (int)Math.Floor(u0 * tilesX)));
                int tx1 = Math.Max(0, Math.Min(tilesX - 1, (int)Math.Floor(u1 * tilesX)));
                int ty0 = Math.Max(0, Math.Min(tilesY - 1, (int)Math.Floor(v0 * tilesY)));
                int ty1 = Math.Max(0, Math.Min(tilesY - 1, (int)Math.Floor(v1 * tilesY)));
                for (int y = ty0; y <= ty1; y++) for (int x = tx0; x <= tx1; x++)
                {
                    if (!Charge(result, cellWorkBudget)) return Fallback(result);
                    Array.Copy(read, tileA, count); Vertex[] tr = tileA, tw = tileB; int tc = count;
                    for (int plane = 0; plane < 4 && tc >= 3; plane++)
                    {
                        tc = ClipUv(tr, tc, tw, plane, (double)x / tilesX, (double)(x + 1) / tilesX, (double)y / tilesY, (double)(y + 1) / tilesY);
                        Vertex[] swap = tr; tr = tw; tw = swap;
                    }
                    if (tc >= 3 && !Accumulate(result, cameras, tr, tc, projection, viewport, cellWorkBudget, panoramaFilterUv)) return Fallback(result);
                }
            }
            foreach (CameraDensityField field in result.Cameras)
            {
                double required = 0; bool covered = false;
                foreach (CameraDensityCell cell in field.Cells) if (cell.Covered) { covered = true; required = Math.Max(required, cell.UpperMagnification); }
                field.SuggestedResolution = !covered ? 0 : Math.Min(field.CaptureMax, required <= 256 ? 256 : required <= 512 ? 512 : required <= 1024 ? 1024 : 2048);
            }
            return result;
        }
    }
}
