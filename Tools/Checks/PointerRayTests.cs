using System;
using HoloMap;
using VRageMath;

internal static class PointerRayTests
{
    static readonly Vector2 Native = new Vector2(1920, 1080);
    static readonly Vector2 Full = new Vector2(1920, 1080);
    static readonly Vector3D Forward = new Vector3D(0, 0, -1);

    public static void Run(Action<bool, string> check)
    {
        CameraRays(check);
        DynamicResolution(check);
        BoundsAndFiniteInput(check);
        InvalidMatricesAndHomogeneousPoints(check);
    }

    // A right-handed, row-vector perspective with depth in [0,1]. Expectations
    // below are independently derived from the near-plane rectangle, not an
    // inverse projection or a FOV approximation.
    static MatrixD Projection(double left = -1.6, double right = 1.6,
        double bottom = -.9, double top = .9, double near = 1, double far = 1000)
    {
        var m = new MatrixD();
        m.M11 = 2 * near / (right - left);
        m.M22 = 2 * near / (top - bottom);
        m.M31 = (left + right) / (right - left);
        m.M32 = (top + bottom) / (top - bottom);
        m.M33 = far / (near - far);
        m.M34 = -1;
        m.M43 = near * far / (near - far);
        return m;
    }
    static MatrixD View(MatrixD world)
    {
        var m = MatrixD.Identity;
        m.M11 = world.M11; m.M12 = world.M21; m.M13 = world.M31;
        m.M21 = world.M12; m.M22 = world.M22; m.M23 = world.M32;
        m.M31 = world.M13; m.M32 = world.M23; m.M33 = world.M33;
        var p = world.Translation;
        m.M41 = -(p.X * m.M11 + p.Y * m.M21 + p.Z * m.M31);
        m.M42 = -(p.X * m.M12 + p.Y * m.M22 + p.Z * m.M32);
        m.M43 = -(p.X * m.M13 + p.Y * m.M23 + p.Z * m.M33);
        return m;
    }
    static Vector3D Expected(double u, double v, MatrixD world,
        double left = -1.6, double right = 1.6, double bottom = -.9, double top = .9)
    {
        var local = new Vector3D(left + (right - left) * u, top + (bottom - top) * v, -1);
        return Vector3D.Normalize(Vector3D.TransformNormal(local, world));
    }
    static bool Close(Vector3D actual, Vector3D expected, double tolerance = 1e-10)
    { return Vector3D.DistanceSquared(actual, expected) <= tolerance * tolerance; }
    static bool UnitFinite(Vector3D v)
    {
        return Finite(v.X) && Finite(v.Y) && Finite(v.Z)
            && Math.Abs(v.LengthSquared() - 1) < 1e-12;
    }
    static bool Finite(double value)
    { return !double.IsNaN(value) && !double.IsInfinity(value); }
    static Vector3D Ray(Action<bool, string> check, string label, double u, double v,
        MatrixD world, MatrixD projection, Vector3D expected, Vector2? viewport = null,
        double tolerance = 1e-10, Vector3D? forward = null)
    {
        Vector3D origin, direction;
        bool ok = UiPointerRay.Try(u, v, Native, viewport ?? Full, Vector2.Zero,
            View(world), projection, world.Translation, forward ?? world.Forward, out origin, out direction);
        check(ok, label + " accepted");
        check(origin == world.Translation && UnitFinite(direction) && Close(direction, expected, tolerance),
            label + " preserves camera origin and analytical unit direction");
        return direction;
    }
    static void Reject(Action<bool, string> check, string label, double u = .5, double v = .5,
        Vector2? native = null, Vector2? viewport = null, Vector2? offset = null,
        MatrixD? view = null, MatrixD? projection = null,
        Vector3D? position = null, Vector3D? forward = null)
    {
        Vector3D origin, direction;
        bool ok = UiPointerRay.Try(u, v, native ?? Native, viewport ?? Full, offset ?? Vector2.Zero,
            view ?? MatrixD.Identity, projection ?? Projection(), position ?? Vector3D.Zero,
            forward ?? Forward, out origin, out direction);
        check(!ok && origin == Vector3D.Zero && direction == Vector3D.Zero, label + " fails closed");
    }
    static void CameraRays(Action<bool, string> check)
    {
        var points = new[] { new Vector2D(.5, .5), new Vector2D(0, 0), new Vector2D(1, 0),
            new Vector2D(0, 1), new Vector2D(1, 1), new Vector2D(.17, .81) };
        foreach (var p in points)
            Ray(check, "identity camera " + p, p.X, p.Y, MatrixD.Identity, Projection(), Expected(p.X, p.Y, MatrixD.Identity));

        var world = MatrixD.CreateRotationY(.61) * MatrixD.CreateRotationX(-.24) * MatrixD.CreateRotationZ(.19);
        world.Translation = new Vector3D(71234.25, -4231.5, 8756.75);
        foreach (var p in points)
            Ray(check, "translated rotated camera " + p, p.X, p.Y, world, Projection(), Expected(p.X, p.Y, world), tolerance: 2e-10);

        // This projection's optical center is deliberately away from (.5,.5).
        var offAxis = Projection(-.7, 2.5, -1.3, .5);
        foreach (var p in points)
            Ray(check, "off-axis perspective " + p, p.X, p.Y, world, offAxis,
                Expected(p.X, p.Y, world, -.7, 2.5, -1.3, .5), tolerance: 2e-10);
        var shifted = Ray(check, "off-axis center", .5, .5, MatrixD.Identity, offAxis,
            Expected(.5, .5, MatrixD.Identity, -.7, 2.5, -1.3, .5));
        check(!Close(shifted, Forward, .1), "off-axis center does not collapse to a manual FOV forward ray");

        world.Translation = new Vector3D(1e8, -2e8, 3e8);
        Ray(check, "galaxy-scale translated camera", .17, .81, world, Projection(), Expected(.17, .81, world), tolerance: 1e-7);
        var distant = MatrixD.Identity; distant.Translation = new Vector3D(1e100, 0, 0);
        Ray(check, "finite extreme camera translation", .5, .5, distant, Projection(), Forward);
        Ray(check, "non-unit finite forward", .5, .5, MatrixD.Identity, Projection(), Forward,
            forward: new Vector3D(0, 0, -1e300));
        Ray(check, "tiny finite forward", .5, .5, MatrixD.Identity, Projection(), Forward,
            forward: new Vector3D(0, 0, -1e-300));
    }
    static void DynamicResolution(Action<bool, string> check)
    {
        var expected = Expected(.12, .76, MatrixD.Identity);
        Vector3D full = Ray(check, "native full render", .12, .76, MatrixD.Identity, Projection(), expected);
        foreach (var dimensions in new[] { new Vector2(960, 540), new Vector2(1280, 720),
            new Vector2(3840, 2160), new Vector2(320, 180) })
        {
            Vector3D scaled = Ray(check, "DRS " + dimensions, .12, .76, MatrixD.Identity, Projection(), expected, dimensions);
            check(Close(full, scaled, 1e-14), "DRS normalized fractions retain identical direction " + dimensions);
        }
        Ray(check, "independently rounded DRS pixels", .12, .76, MatrixD.Identity, Projection(), expected, new Vector2(1001, 563));
        Reject(check, "wrong render aspect", viewport: new Vector2(1280, 800));
        Reject(check, "render aspect beyond nearest-pixel error", viewport: new Vector2(1004, 563));
        Reject(check, "wrong native aspect", native: new Vector2(1920, 1200));
    }
    static void BoundsAndFiniteInput(Action<bool, string> check)
    {
        foreach (double invalid in new[] { -.001, 1.001, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Reject(check, "invalid normalized X " + invalid, u: invalid);
            Reject(check, "invalid normalized Y " + invalid, v: invalid);
        }
        foreach (Vector2 invalid in new[] { new Vector2(0, 1080), new Vector2(1920, 0),
            new Vector2(-1, 1080), new Vector2(1920, .99f), new Vector2(float.NaN, 1080),
            new Vector2(1920, float.PositiveInfinity) })
        {
            Reject(check, "invalid native dimensions " + invalid, native: invalid);
            Reject(check, "invalid render dimensions " + invalid, viewport: invalid);
        }
        foreach (Vector2 invalid in new[] { new Vector2(1, 0), new Vector2(0, -1),
            new Vector2(float.NaN, 0), new Vector2(0, float.PositiveInfinity) })
            Reject(check, "unsupported viewport offset " + invalid, offset: invalid);
        foreach (Vector3D invalid in new[] { new Vector3D(double.NaN, 0, 0),
            new Vector3D(0, double.PositiveInfinity, 0), new Vector3D(0, 0, double.NegativeInfinity) })
        {
            Reject(check, "nonfinite camera position " + invalid, position: invalid);
            Reject(check, "nonfinite camera forward " + invalid, forward: invalid);
        }
        Reject(check, "zero camera forward", forward: Vector3D.Zero);
        Reject(check, "backward camera forward", forward: -Forward);
        Reject(check, "perpendicular camera forward", forward: Vector3D.UnitX);
    }
    static void InvalidMatricesAndHomogeneousPoints(Action<bool, string> check)
    {
        for (int field = 0; field < 16; field++)
        {
            MatrixD badView = WithField(MatrixD.Identity, field, double.NaN);
            MatrixD badProjection = WithField(Projection(), field, double.PositiveInfinity);
            Vector3D origin, direction;
            bool viewAccepted = UiPointerRay.Try(.5, .5, Native, Full, Vector2.Zero,
                badView, Projection(), Vector3D.Zero, Forward, out origin, out direction);
            bool projectionAccepted = UiPointerRay.Try(.5, .5, Native, Full, Vector2.Zero,
                MatrixD.Identity, badProjection, Vector3D.Zero, Forward, out origin, out direction);
            check(!viewAccepted && !projectionAccepted && origin == Vector3D.Zero && direction == Vector3D.Zero,
                "nonfinite view/projection matrix field " + field + " fails closed");
        }
        Reject(check, "zero projection matrix", projection: new MatrixD());
        Reject(check, "zero view matrix", view: new MatrixD());
        var singular = Projection(); singular.M11 = 0;
        Reject(check, "finite singular projection", projection: singular);
        var singularView = MatrixD.Identity; singularView.M22 = 0;
        Reject(check, "finite singular view", view: singularView);
        var dependentRows = new MatrixD();
        dependentRows.M11 = 1; dependentRows.M12 = 3;
        dependentRows.M21 = 3; dependentRows.M22 = 9;
        dependentRows.M33 = 10; dependentRows.M44 = 10;
        Reject(check, "exact proportional projection rows", projection: dependentRows, forward: Vector3D.UnitZ);
        Reject(check, "exact proportional view rows", view: dependentRows, projection: MatrixD.Identity, forward: Vector3D.UnitZ);
        dependentRows.M12 = 11; dependentRows.M21 = 11; dependentRows.M22 = 121;
        dependentRows.M33 = 1000000000000011; dependentRows.M44 = 1000000000000011;
        Reject(check, "singular cofactor cancellation", projection: dependentRows, forward: Vector3D.UnitZ);
        var overflow = MatrixD.Identity; overflow.M11 = double.MaxValue;
        var two = MatrixD.Identity; two.M11 = 2;
        Reject(check, "view-projection multiplication overflow", view: overflow, projection: two);
        var inverseOverflow = MatrixD.Identity; inverseOverflow.M33 = double.Epsilon;
        Reject(check, "inverse matrix overflow", projection: inverseOverflow);
        var homogeneousOverflow = MatrixD.Identity;
        homogeneousOverflow.M11 = 1e-308; homogeneousOverflow.M21 = -1;
        Reject(check, "homogeneous coordinate addition overflow", u: 1, v: 0, projection: homogeneousOverflow);
        var deltaOverflow = MatrixD.Identity; deltaOverflow.M11 = 1e-308;
        Reject(check, "finite world point camera subtraction overflow", u: 1, projection: deltaOverflow,
            position: new Vector3D(-double.MaxValue, 0, 0), forward: Vector3D.UnitX);

        // Inverse matrices chosen so the center's homogeneous result has W=0
        // or a world point exactly equal to the supplied camera position.
        var inverse = MatrixD.Identity; inverse.M34 = 2; inverse.M44 = -1;
        Reject(check, "zero homogeneous W", projection: MatrixD.Invert(inverse));
        inverse = MatrixD.Identity; inverse.M43 = -.5;
        Reject(check, "zero unprojected camera delta", projection: MatrixD.Invert(inverse));
        inverse = MatrixD.Identity; inverse.M43 = -1;
        Reject(check, "behind-camera unprojection", projection: MatrixD.Invert(inverse), forward: Vector3D.UnitZ);
        inverse = MatrixD.Identity; inverse.M44 = -1;
        Ray(check, "finite negative homogeneous W", .5, .5, MatrixD.Identity, MatrixD.Invert(inverse), Forward);

        var tiny = Multiply(Projection(), 1e-100);
        var huge = Multiply(Projection(), 1e100);
        var expected = Expected(.2, .7, MatrixD.Identity);
        Ray(check, "tiny homogeneous projection scale", .2, .7, MatrixD.Identity, tiny, expected);
        Ray(check, "large homogeneous projection scale", .2, .7, MatrixD.Identity, huge, expected);
        Vector3D suppliedOrigin, suppliedDirection;
        var suppliedCamera = new Vector3D(1e308, 0, 0);
        bool suppliedValid = UiPointerRay.Try(.5, .5, Native, Full, Vector2.Zero, MatrixD.Identity,
            Multiply(MatrixD.Identity, 2), suppliedCamera, -Vector3D.UnitX, out suppliedOrigin, out suppliedDirection);
        check(suppliedValid && suppliedOrigin == suppliedCamera && UnitFinite(suppliedDirection)
            && Close(suppliedDirection, -Vector3D.UnitX),
            "independently supplied finite camera position does not overflow inverse proof");
    }
    static MatrixD Multiply(MatrixD matrix, double factor)
    {
        for (int field = 0; field < 16; field++) matrix = WithField(matrix, field, Field(matrix, field) * factor);
        return matrix;
    }
    static double Field(MatrixD matrix, int field)
    {
        switch (field)
        {
            case 0: return matrix.M11; case 1: return matrix.M12; case 2: return matrix.M13; case 3: return matrix.M14;
            case 4: return matrix.M21; case 5: return matrix.M22; case 6: return matrix.M23; case 7: return matrix.M24;
            case 8: return matrix.M31; case 9: return matrix.M32; case 10: return matrix.M33; case 11: return matrix.M34;
            case 12: return matrix.M41; case 13: return matrix.M42; case 14: return matrix.M43; default: return matrix.M44;
        }
    }
    static MatrixD WithField(MatrixD matrix, int field, double value)
    {
        switch (field)
        {
            case 0: matrix.M11 = value; break; case 1: matrix.M12 = value; break;
            case 2: matrix.M13 = value; break; case 3: matrix.M14 = value; break;
            case 4: matrix.M21 = value; break; case 5: matrix.M22 = value; break;
            case 6: matrix.M23 = value; break; case 7: matrix.M24 = value; break;
            case 8: matrix.M31 = value; break; case 9: matrix.M32 = value; break;
            case 10: matrix.M33 = value; break; case 11: matrix.M34 = value; break;
            case 12: matrix.M41 = value; break; case 13: matrix.M42 = value; break;
            case 14: matrix.M43 = value; break; case 15: matrix.M44 = value; break;
        }
        return matrix;
    }
}
