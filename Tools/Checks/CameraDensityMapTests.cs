using System;
using System.Linq;
using HoloMap;
using VRageMath;

internal static class CameraDensityMapTests
{
    static int checks;
    static readonly int[] Quad = { 0, 1, 2, 0, 2, 3 };
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception("Camera density: " + name); checks++; }
    static Vector3D[] Rectangle(double x = .4, double y = .4, double z = .5)
    { return new[] { new Vector3D(-x, -y, z), new Vector3D(x, -y, z), new Vector3D(x, y, z), new Vector3D(-x, y, z) }; }
    static Vector2[] Uv(double u0 = .45, double u1 = .55, double v0 = .45, double v1 = .55)
    { return new[] { new Vector2((float)u0, (float)v1), new Vector2((float)u1, (float)v1), new Vector2((float)u1, (float)v0), new Vector2((float)u0, (float)v0) }; }
    static CameraDensityLens Lens(Vector3D forward, Vector3D up, int cap = 1024)
    { return new CameraDensityLens { Forward = forward, Up = up, Right = Vector3D.Cross(up, forward), CaptureMax = cap }; }
    static CameraDensityLens[] Cube()
    {
        return new[] { Lens(Vector3D.UnitZ, Vector3D.UnitY), Lens(Vector3D.UnitX, Vector3D.UnitY),
            Lens(-Vector3D.UnitZ, Vector3D.UnitY), Lens(-Vector3D.UnitX, Vector3D.UnitY),
            Lens(Vector3D.UnitY, -Vector3D.UnitZ), Lens(-Vector3D.UnitY, Vector3D.UnitZ) };
    }
    static CameraDensityResult Evaluate(Vector3D[] points, int[] triangles, Vector2[] uv, CameraDensityLens[] lenses,
        MatrixD? matrix = null, Vector4D[] crop = null, int triangleBudget = 8192, int cellBudget = 65536, int grid = 16)
    { return CameraDensityMap.Evaluate(points, triangles, uv, matrix ?? MatrixD.Identity, new Vector2I(200, 200), crop, lenses, grid, triangleBudget, cellBudget, 0, 0); }
    static Vector3D Ray(double u, double v)
    {
        double a = (u - .5) * 2 * Math.PI, b = (.5 - v) * Math.PI;
        return new Vector3D(Math.Cos(b) * Math.Sin(a), Math.Sin(b), Math.Cos(b) * Math.Cos(a));
    }
    // Independent shader-equivalent witness. This never reads the helper's mask
    // or intervals to decide whether a camera should contribute.
    static bool Projects(CameraDensityLens lens, double u, double v, out double s, out double t)
    {
        Vector3D ray = Ray(u, v); double z = Vector3D.Dot(ray, lens.Forward); s = t = 0;
        if (z <= .000001) return false;
        double x = Vector3D.Dot(ray, lens.Right) / z, y = Vector3D.Dot(ray, lens.Up) / z;
        s = .5 + .5 * x / lens.TanHorizontal; t = .5 - .5 * y / lens.TanVertical;
        if (s < 0 || s > 1 || t < 0 || t > 1) return false;
        return lens.FeatherRadians == 0 || Math.Min(Math.Atan(lens.TanHorizontal) - Math.Atan(Math.Abs(x)),
            Math.Atan(lens.TanVertical) - Math.Atan(Math.Abs(y))) > 0;
    }
    static bool Visible(Vector3D point, MatrixD m, Vector4D[] crop)
    {
        double x = point.X * m.M11 + point.Y * m.M21 + point.Z * m.M31 + m.M41;
        double y = point.X * m.M12 + point.Y * m.M22 + point.Z * m.M32 + m.M42;
        double z = point.X * m.M13 + point.Y * m.M23 + point.Z * m.M33 + m.M43;
        double w = point.X * m.M14 + point.Y * m.M24 + point.Z * m.M34 + m.M44;
        if (w <= 1e-9 || z < 0 || z > w || Math.Abs(x) > w || Math.Abs(y) > w) return false;
        if (crop != null) foreach (Vector4D p in crop) if (point.X * p.X + point.Y * p.Y + point.Z * p.Z > p.W) return false;
        return true;
    }
    static void Witnesses(CameraDensityResult result, Vector3D[] points, int[] triangles, Vector2[] uv,
        CameraDensityLens[] lenses, MatrixD matrix, Vector4D[] crop = null, int divisions = 16)
    {
        CameraVisibilityResult cheap = CameraDensityMap.EvaluateVisibility(points, triangles, uv, matrix, new Vector2I(200, 200), crop, lenses, 8192, 0, 0);
        Check(!cheap.ConservativeFallback, "valid cheap visibility uses bounded geometry instead of fallback");
        for (int face = 0; face < triangles.Length; face += 3)
        {
            int ia = triangles[face], ib = triangles[face + 1], ic = triangles[face + 2];
            for (int y = 0; y <= divisions; y++) for (int x = 0; x + y <= divisions; x++)
            {
                double b = (double)x / divisions, c = (double)y / divisions, a = 1 - b - c;
                Vector3D point = points[ia] * a + points[ib] * b + points[ic] * c;
                if (!Visible(point, matrix, crop)) continue;
                double u = uv[ia].X * a + uv[ib].X * b + uv[ic].X * c, v = uv[ia].Y * a + uv[ib].Y * b + uv[ic].Y * c;
                for (int camera = 0; camera < lenses.Length; camera++)
                {
                    double s, t; if (!Projects(lenses[camera], u, v, out s, out t)) continue;
                    CameraDensityField field = result.Cameras[camera]; int ix = Math.Min(field.GridSize - 1, (int)(s * field.GridSize)), iy = Math.Min(field.GridSize - 1, (int)(t * field.GridSize));
                    Check((result.VisibleMask & (1 << camera)) != 0 && field.Cells[iy * field.GridSize + ix].Covered,
                        "every visible positive-contribution witness retains its source camera cell");
                    Check((cheap.VisibleMask & (1 << camera)) != 0, "cheap per-frame visibility retains every positive-contribution witness");
                }
            }
        }
    }
    static void Bounded(CameraDensityResult result)
    {
        foreach (CameraDensityField field in result.Cameras) foreach (CameraDensityCell cell in field.Cells)
        {
            Check(!double.IsNaN(cell.Magnification) && !double.IsInfinity(cell.Magnification) && cell.Magnification >= 0 && cell.Magnification <= field.CaptureMax,
                "finite capped magnification");
            Check(cell.UpperMagnification >= cell.Magnification && cell.UpperMagnification <= field.CaptureMax &&
                cell.UpperArea >= cell.Area && cell.UpperArea <= (double)field.CaptureMax * field.CaptureMax, "conservative bounded density channels");
            Check(cell.BlendWeight >= 0 && cell.BlendWeight <= cell.BlendWeightUpper && cell.BlendWeightUpper <= 1, "bounded blend weight");
        }
    }
    static void FullFallback(CameraDensityResult result)
    {
        Check(result.ConservativeFallback && result.VisibleMask == (1 << result.Cameras.Length) - 1, "unknown/exhausted state demands every camera");
        foreach (CameraDensityField field in result.Cameras)
            Check(field.SuggestedResolution == field.CaptureMax && field.Cells.All(c => c.Covered && c.UpperMagnification == field.CaptureMax && c.UpperArea == (double)field.CaptureMax * field.CaptureMax),
                "unknown field fills all source-image cells at configured maximum");
    }
    static Vector2D AnisotropicScreen(double s, double t)
    {
        Vector3D direction = Vector3D.Normalize(new Vector3D(2 * s - 1, 1 - 2 * t, 1));
        double u = .5 + Math.Atan2(direction.X, direction.Z) / (2 * Math.PI);
        double v = .5 - Math.Asin(direction.Y) / Math.PI;
        return new Vector2D(100 + 800 * (u - .5), 100 + 80 * (v - .5));
    }
    public static int Run()
    {
        checks = 0; CameraDensityLens[] cube = Cube(); Vector3D[] quad = Rectangle(); Vector2[] uv = Uv();
        var basic = Evaluate(quad, Quad, uv, cube);
        Check(!basic.ConservativeFallback && basic.VisibleMask == 1, "front image region excludes other camera frustums");
        Check(basic.Cameras[0].Cells.Any(c => c.HasSample) && basic.Cameras[0].Cells.Any(c => !c.Covered), "map is a localized source-image field");
        Witnesses(basic, quad, Quad, uv, cube, MatrixD.Identity); Bounded(basic);
        Check(CameraDensityMap.EvaluateVisibility(quad, Quad, uv, MatrixD.Identity, new Vector2I(200, 200), null, cube).VisibleMask == 1,
            "cheap path excludes unrelated source frustums");

        // Density is local magnification, not the count of visible pixels.
        var anisotropic = Evaluate(Rectangle(.4, .04), Quad, uv, new[] { cube[0] });
        CameraDensityCell centre = anisotropic.Cameras[0].Cells[8 * 16 + 8];
        Check(centre.HasSample && centre.Magnification > 240 && centre.Magnification < 270, "composed Jacobian has expected largest singular value");
        Check(centre.Area > 11000 && centre.Area < 15000 && centre.Magnification > 2 * Math.Sqrt(centre.Area), "anisotropy retains major-axis demand separately from area");
        // A separate finite-difference oracle checks interval upper bounds at
        // many locations BETWEEN the helper's sampled camera-cell centres.
        for (double t = .425; t < .575; t += .0073) for (double s = .35; s < .65; s += .0071)
        {
            const double delta = 1e-6;
            Vector2D ds = (AnisotropicScreen(s + delta, t) - AnisotropicScreen(s - delta, t)) / (2 * delta);
            Vector2D dt = (AnisotropicScreen(s, t + delta) - AnisotropicScreen(s, t - delta)) / (2 * delta);
            double det = ds.X * dt.Y - dt.X * ds.Y, trace = ds.LengthSquared() + dt.LengthSquared();
            double sigma = Math.Sqrt(.5 * (trace + Math.Sqrt(Math.Max(0, trace * trace - 4 * det * det))));
            CameraDensityCell cell = anisotropic.Cameras[0].Cells[(int)(t * 16) * 16 + (int)(s * 16)];
            Check(cell.Covered && cell.UpperMagnification + .001 >= sigma && cell.UpperArea + .1 >= Math.Abs(det),
                "interval envelopes bound independently differentiated local source UV demand");
        }
        var clipped = Evaluate(Rectangle(.4, .04), Quad, uv, new[] { cube[0] }, crop: new[] { new Vector4D(1, 0, 0, .06) });
        CameraDensityCell clippedCentre = clipped.Cameras[0].Cells[8 * 16 + 8];
        Check(clippedCentre.HasSample && Math.Abs(clippedCentre.Magnification - centre.Magnification) < .01,
            "clipping changes coverage without reducing shared-cell local magnification");
        Check(clipped.Cameras[0].Cells.Count(c => c.Covered) < anisotropic.Cameras[0].Cells.Count(c => c.Covered), "crop reduces source UV coverage");

        // No original vertex is inside, but the clipped triangle covers the viewer.
        var crossingPoints = new[] { new Vector3D(-4, -3, .5), new Vector3D(4, -3, .5), new Vector3D(0, 4, .5) };
        var crossingUv = new[] { new Vector2(.3f, .7f), new Vector2(.7f, .7f), new Vector2(.5f, .3f) };
        var crossing = Evaluate(crossingPoints, new[] { 0, 1, 2 }, crossingUv, cube);
        Check(!crossing.ConservativeFallback && crossing.VisibleMask != 0 && crossingPoints.All(p => !Visible(p, MatrixD.Identity, null)), "frustum-edge intersections retain a no-vertex-inside triangle");
        Witnesses(crossing, crossingPoints, new[] { 0, 1, 2 }, crossingUv, cube, MatrixD.Identity, divisions: 32);

        // Perspective clipping near W=0 and crop intersections preserve UVs.
        MatrixD perspective = MatrixD.Identity; perspective.M34 = .7;
        var tilted = new[] { new Vector3D(-2, -.8, -.2), new Vector3D(2, -.8, 1.2), new Vector3D(2, .8, 1.2), new Vector3D(-2, .8, -.2) };
        var crops = new[] { new Vector4D(1, .2, 0, .7), new Vector4D(-1, 0, 0, .9) };
        var near = Evaluate(tilted, Quad, Uv(.25, .75, .25, .75), cube, perspective, crops);
        Check(!near.ConservativeFallback, "valid perspective/crop path remains measured");
        Witnesses(near, tilted, Quad, Uv(.25, .75, .25, .75), cube, perspective, crops, 32); Bounded(near);

        // Actual angular sphere with mirrored inward geometry, independent display
        // rotation, anchor-frame cameras, and both shell intersections retained.
        var sphere = SurfaceMapping.BuildQuad(2, 2, Vector4.One, new SurfaceStyle { Kind = SurfaceKind.Sphere,
            Mapping = SurfaceMappingMode.Angular, Radius = .45, Inward = true, MaxError = .04 });
        foreach (int i in Enumerable.Range(0, sphere.Geometry.Points.Length)) sphere.Geometry.Points[i].X = -sphere.Geometry.Points[i].X;
        MatrixD sphereMatrix = MatrixD.CreateRotationY(.37) * MatrixD.CreateRotationZ(.23) * MatrixD.CreateTranslation(.3, 0, .5);
        MatrixD cameraRotation = MatrixD.CreateRotationY(-.29);
        foreach (CameraDensityLens camera in cube) { camera.Right = Vector3D.TransformNormal(camera.Right, cameraRotation); camera.Up = Vector3D.TransformNormal(camera.Up, cameraRotation); camera.Forward = Vector3D.TransformNormal(camera.Forward, cameraRotation); }
        var globe = Evaluate(sphere.Geometry.Points, sphere.Geometry.Triangles, sphere.UV, cube, sphereMatrix, cellBudget: CameraDensityMap.MaxCellWork);
        Check(!globe.ConservativeFallback && globe.VisibleMask == 63, "two-sided transparent globe retains front and far shell camera support");
        Witnesses(globe, sphere.Geometry.Points, sphere.Geometry.Triangles, sphere.UV, cube, sphereMatrix, divisions: 5);
        var halfCrop = new[] { new Vector4D(1, 0, 0, -.1) };
        var cutGlobe = Evaluate(sphere.Geometry.Points, sphere.Geometry.Triangles, sphere.UV, cube, sphereMatrix, halfCrop, cellBudget: CameraDensityMap.MaxCellWork);
        Witnesses(cutGlobe, sphere.Geometry.Points, sphere.Geometry.Triangles, sphere.UV, cube, sphereMatrix, halfCrop, 5);

        // Both sides of the atlas longitude seam and the polar singularity.
        cube = Cube();
        foreach (Vector2[] edgeUv in new[] { Uv(0, .04, .4, .6), Uv(.96, 1, .4, .6), Uv(.1, .9, 0, .04) })
        {
            var edge = Evaluate(quad, Quad, edgeUv, cube, cellBudget: CameraDensityMap.MaxCellWork);
            Check(!edge.ConservativeFallback, "seam and pole remain bounded without global fallback");
            Witnesses(edge, quad, Quad, edgeUv, cube, MatrixD.Identity); Bounded(edge);
        }

        // Overlapping lenses contribute together; no best-camera winner is used.
        var twinA = Lens(Vector3D.UnitZ, Vector3D.UnitY); var twinB = Lens(Vector3D.UnitZ, Vector3D.UnitY);
        twinA.FeatherRadians = twinB.FeatherRadians = Math.PI * 8 / 180;
        var twins = Evaluate(quad, Quad, uv, new[] { twinA, twinB });
        Check(twins.VisibleMask == 3 && twins.Cameras.All(f => f.Cells.Any(c => c.HasSample && Math.Abs(c.BlendWeight - .5) < 1e-12)), "identical overlapping cameras carry normalized half blend weights");
        var overlapB = Lens(Vector3D.Normalize(new Vector3D(.4, 0, 1)), Vector3D.UnitY);
        var overlap = Evaluate(quad, Quad, Uv(.48, .62, .4, .6), new[] { twinA, overlapB });
        Witnesses(overlap, quad, Quad, Uv(.48, .62, .4, .6), new[] { twinA, overlapB }, MatrixD.Identity); Bounded(overlap);
        CameraDensityLens[] wide = Cube();
        foreach (CameraDensityLens lens in wide) { lens.TanHorizontal = lens.TanVertical = Math.Tan(105 * Math.PI / 360); lens.FeatherRadians = 8 * Math.PI / 180; }
        Vector2[] wideUv = Uv(.35, .65, .35, .65); var actualFov = Evaluate(quad, Quad, wideUv, wide);
        Check((actualFov.VisibleMask & 11) == 11, "105-degree capture overlap retains front and adjacent camera footprints");
        Witnesses(actualFov, quad, Quad, wideUv, wide, MatrixD.Identity, divisions: 24);

        var highGrid = Evaluate(quad, Quad, uv, new[] { twinA }, grid: 32);
        Check(highGrid.Cameras[0].Cells.Length == 1024 && highGrid.Cameras[0].Cells.Any(c => !c.Covered), "32-square source UV field supported");
        FullFallback(Evaluate(null, null, null, cube));
        FullFallback(Evaluate(quad, Quad, uv, cube, triangleBudget: 0));
        FullFallback(Evaluate(quad, Quad, uv, cube, cellBudget: 0));
        FullFallback(Evaluate(quad, Quad, uv, cube, triangleBudget: 1));
        FullFallback(Evaluate(quad, Quad, uv, cube, cellBudget: 7));
        FullFallback(Evaluate(quad, new[] { 0, 1, 99 }, uv, cube));
        FullFallback(Evaluate(quad, Quad, new[] { Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero }, cube));
        var badPoints = Rectangle(); badPoints[0].X = double.NaN; FullFallback(Evaluate(badPoints, Quad, uv, cube));
        var offscreen = Evaluate(Rectangle(), Quad, uv, cube, MatrixD.CreateTranslation(10, 0, 0));
        Check(offscreen.VisibleMask == 0 && offscreen.Cameras.All(f => f.SuggestedResolution == 0), "provably offscreen geometry requires no captures");
        var croppedAway = Evaluate(quad, Quad, uv, cube, crop: new[] { new Vector4D(1, 0, 0, -10) });
        Check(croppedAway.VisibleMask == 0, "fully cropped geometry requires no captures");
        var empty = Evaluate(new Vector3D[0], new int[0], new Vector2[0], cube);
        Check(empty.VisibleMask == 0 && !empty.ConservativeFallback, "known empty geometry differs from unknown geometry");
        var limited = Lens(Vector3D.UnitZ, Vector3D.UnitY, 256); FullFallback(Evaluate(null, null, null, new[] { limited }));
        Check(Evaluate(Rectangle(), Quad, uv, new[] { limited }).Cameras[0].SuggestedResolution <= 256, "per-camera configured cap is respected");
        var cheapUnknown = CameraDensityMap.EvaluateVisibility(null, null, null, MatrixD.Identity, new Vector2I(200, 200), null, cube);
        Check(cheapUnknown.ConservativeFallback && cheapUnknown.VisibleMask == 63, "cheap missing mesh keeps every source");
        var cheapExhausted = CameraDensityMap.EvaluateVisibility(quad, Quad, uv, MatrixD.Identity, new Vector2I(200, 200), null, cube, 1);
        Check(cheapExhausted.ConservativeFallback && cheapExhausted.VisibleMask == 63 && cheapExhausted.TriangleWork == 1,
            "cheap exhausted geometry keeps every remaining source without exceeding work cap");
        Check(CameraDensityMap.EvaluateVisibility(quad, Quad, uv, MatrixD.CreateTranslation(10, 0, 0), new Vector2I(200, 200), null, cube).VisibleMask == 0,
            "cheap path rejects wholly offscreen geometry");
        Check(CameraDensityMap.EvaluateVisibility(quad, Quad, uv, MatrixD.Identity, new Vector2I(200, 200), new[] { new Vector4D(1, 0, 0, -10) }, cube).VisibleMask == 0,
            "cheap path rejects wholly cropped geometry");
        try { Evaluate(quad, Quad, uv, new CameraDensityLens[7]); throw new Exception("Unbounded camera count"); } catch (ArgumentException) { checks++; }
        try { Evaluate(quad, Quad, uv, new[] { new CameraDensityLens { Forward = Vector3D.Zero } }); throw new Exception("Malformed camera basis"); } catch (ArgumentException) { checks++; }
        return checks;
    }
}
