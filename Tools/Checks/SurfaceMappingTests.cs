using System;
using System.Collections.Generic;
using HoloMap;
using VRageMath;

internal static class SurfaceMappingTests
{
    static int checks;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Surface mapping: " + name);
        checks++;
    }
    static void Near(Vector3D actual, Vector3D expected, double tolerance, string name)
    { Check((actual - expected).Length() <= tolerance, name); }
    static void Reject(Action action, string name)
    {
        try { action(); }
        catch (ArgumentException) { checks++; return; }
        throw new Exception("Surface mapping accepted: " + name);
    }
    static SurfaceMesh Quad()
    {
        return new SurfaceMesh
        {
            Geometry = new Geometry(new[] { new Vector3D(-1, -1, 0), new Vector3D(1, -1, 0),
                new Vector3D(1, 1, 0), new Vector3D(-1, 1, 0) }, new[] { 0, 1, 1, 2 },
                new[] { 0, 1, 2, 0, 2, 3 }),
            Colors = new[] { new Vector4(1, 0, 0, 0.5f), new Vector4(0, 1, 0, 0.75f) },
            EdgeColors = new[] { new Vector4(0, 0, 1, 0.4f), new Vector4(1, 1, 0, 0.9f) },
            UV = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) }
        };
    }
    static SurfaceStyle Sphere(double error = 0.04)
    {
        return new SurfaceStyle { Kind = SurfaceKind.Sphere, Mapping = SurfaceMappingMode.Angular,
            Radius = 2, HorizontalRadians = Math.PI, VerticalRadians = Math.PI / 2,
            MaxError = error, Inward = false };
    }
    public static int Run()
    {
        checks = 0;
        var plane = new SurfaceStyle();
        var source = Quad();
        var flat = SurfaceMapping.Warp(source, 2, 2, plane, 0.25);
        Check(flat.Geometry.Points.Length == 4 && flat.Geometry.Triangles.Length == 6 && flat.Geometry.Edges.Length == 4, "plane topology");
        Near(flat.Geometry.Points[0], new Vector3D(-1, -1, 0.25), 1e-12, "plane coordinate and offset");
        Check(flat.UV[3] == source.UV[3] && flat.Colors[1] == source.Colors[1]
            && flat.EdgeColors[0] == source.EdgeColors[0], "plane attributes");
        Check(!Object.ReferenceEquals(flat.Geometry.Points, source.Geometry.Points), "plane output detached");

        var style = Sphere();
        Near(SurfaceMapping.MapPoint(Vector3D.Zero, 2, 2, style), new Vector3D(0, 0, 2), 1e-12, "sphere centre");
        Near(SurfaceMapping.MapPoint(new Vector3D(1, 0, 0), 2, 2, style), new Vector3D(2, 0, 0), 1e-12, "sphere right bound");
        Near(SurfaceMapping.MapPoint(new Vector3D(0, 1, 0), 2, 2, style), new Vector3D(0, Math.Sqrt(2), Math.Sqrt(2)), 1e-12, "sphere upper bound");
        Near(SurfaceMapping.MapPoint(new Vector3D(0, 0, 0.2), 2, 2, style, 0.1), new Vector3D(0, 0, 2.3), 1e-12, "sphere radial offset");
        Near(SurfaceMapping.Normal(Vector3D.Zero, 2, 2, style), Vector3D.UnitZ, 1e-12, "outward normal");
        style.Inward = true;
        Near(SurfaceMapping.Normal(Vector3D.Zero, 2, 2, style), -Vector3D.UnitZ, 1e-12, "inward normal");
        Near(SurfaceMapping.MapPoint(Vector3D.Zero, 2, 2, style, 0.1), new Vector3D(0, 0, 1.9), 1e-12, "positive layer depth moves inward");
        style.Inward = false;
        var curved = SurfaceMapping.Warp(source, 2, 2, style);
        Check(curved.Geometry.Triangles.Length > 6 && curved.Geometry.Edges.Length > 4, "filled and line tessellation");
        Check(curved.Geometry.Points.Length <= Geometry.MaxPoints && curved.Geometry.Triangles.Length / 3 + curved.Geometry.Edges.Length / 2 <= Geometry.MaxPrimitives, "bounded output");
        Check(curved.UV.Length == curved.Geometry.Points.Length, "UV count preserved");
        for (int i = 0; i < curved.Geometry.Points.Length; i++)
        {
            var uv = curved.UV[i];
            Near(curved.Geometry.Points[i], SurfaceMapping.MapPoint(new Vector3D((uv.X - .5) * 2, (uv.Y - .5) * 2, 0), 2, 2, style), 2e-6, "UV follows mapped source");
        }
        foreach (var color in curved.Colors) Check(color == source.Colors[0] || color == source.Colors[1], "face color preserved");
        foreach (var color in curved.EdgeColors) Check(color == source.EdgeColors[0] || color == source.EdgeColors[1], "line color preserved");
        // Every original diagonal edge is shared by both triangles after refinement.
        var diagonal = new Dictionary<long, int>();
        for (int t = 0; t < curved.Geometry.Triangles.Length; t += 3)
            for (int j = 0; j < 3; j++)
            {
                int a = curved.Geometry.Triangles[t + j], b = curved.Geometry.Triangles[t + (j + 1) % 3];
                var ua = curved.UV[a]; var ub = curved.UV[b];
                if (Math.Abs(ua.X - ua.Y) > 1e-6 || Math.Abs(ub.X - ub.Y) > 1e-6) continue;
                if (Math.Abs(ua.X - ub.X) < 1e-7) continue;
                long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                int count; diagonal.TryGetValue(key, out count); diagonal[key] = count + 1;
            }
        Check(diagonal.Count > 1, "shared diagonal refined");
        foreach (int count in diagonal.Values) Check(count == 2, "shared diagonal has no crack");
        var inside = Sphere(); inside.Inward = true;
        var insideMesh = SurfaceMapping.Warp(source, 2, 2, inside);
        Check(insideMesh.Geometry.Triangles[1] == curved.Geometry.Triangles[2]
            && insideMesh.Geometry.Triangles[2] == curved.Geometry.Triangles[1], "inward winding reverses");

        var cylinder = new SurfaceStyle { Kind = SurfaceKind.Cylinder, Radius = 3,
            HorizontalRadians = Math.PI, MaxError = .04, Inward = false };
        Near(SurfaceMapping.MapPoint(new Vector3D(0, .7, 0), 2, 2, cylinder), new Vector3D(0, .7, 3), 1e-12, "cylinder physical height");
        Near(SurfaceMapping.MapPoint(new Vector3D(1, 0, 0), 2, 2, cylinder), new Vector3D(3, 0, 0), 1e-12, "cylinder angular bound");
        cylinder.Mapping = SurfaceMappingMode.Geodesic;
        Near(SurfaceMapping.MapPoint(new Vector3D(Math.PI * 3 / 2, 0, 0), 10, 2, cylinder), new Vector3D(3, 0, 0), 1e-12, "cylinder arc length");

        var geo = Sphere(); geo.Mapping = SurfaceMappingMode.Geodesic;
        geo.HorizontalRadians = Math.PI; geo.VerticalRadians = Math.PI / 2;
        Near(SurfaceMapping.MapPoint(Vector3D.Zero, 2, 2, geo), new Vector3D(0, 0, 2), 1e-12, "geodesic centre");
        Near(SurfaceMapping.MapPoint(new Vector3D(1, 0, 0), 2, 2, geo), new Vector3D(2, 0, 0), 1e-12, "geodesic horizontal distance");
        var pin = Sphere(); pin.Mapping = SurfaceMappingMode.Pinhole;
        pin.VerticalFovRadians = Math.PI / 2; pin.SourceAspect = 2;
        var corner = SurfaceMapping.MapPoint(new Vector3D(1, 1, 0), 2, 2, pin);
        Check(Math.Abs(corner.X / corner.Z - 2) < 1e-12 && Math.Abs(corner.Y / corner.Z - 1) < 1e-12, "pinhole field of view and canvas aspect");
        Check(Math.Abs(corner.Length() - 2) < 1e-12, "pinhole remains on sphere");

        var globe = Sphere(.03); globe.HorizontalRadians = 2 * Math.PI; globe.VerticalRadians = Math.PI;
        Near(SurfaceMapping.MapPoint(new Vector3D(-1, 0, 0), 2, 2, globe),
            SurfaceMapping.MapPoint(new Vector3D(1, 0, 0), 2, 2, globe), 1e-12, "full sphere seam closes");
        Near(SurfaceMapping.MapPoint(new Vector3D(-.5, 1, 0), 2, 2, globe),
            SurfaceMapping.MapPoint(new Vector3D(.5, 1, 0), 2, 2, globe), 1e-12, "angular north pole converges");
        var globeQuad = SurfaceMapping.BuildQuad(2, 2, Vector4.One,
            new SurfaceStyle { Kind = SurfaceKind.Sphere });
        Check(globeQuad.Geometry.Points.Length <= Geometry.MaxPoints
            && globeQuad.Geometry.Triangles.Length / 3 <= Geometry.MaxPrimitives
            && globeQuad.Geometry.Triangles.Length > 1000, "full sphere image quad fits with curved interior");
        Reject(() => SurfaceMapping.Warp(source, 2, 2, new SurfaceStyle { Kind = SurfaceKind.Sphere,
            Radius = 2, HorizontalRadians = Math.PI, VerticalRadians = Math.PI / 2,
            MaxError = .001, MaxPoints = 8 }), "point quota rejects atomically");
        Check(source.Geometry.Points.Length == 4 && source.Geometry.Triangles.Length == 6, "quota did not mutate input");
        Reject(() => SurfaceMapping.Warp(source, 2, 2, new SurfaceStyle { Kind = SurfaceKind.Sphere,
            Radius = 2, HorizontalRadians = Math.PI, VerticalRadians = Math.PI / 2,
            MaxError = .001, MaxWork = 1 }), "work quota");
        Reject(() => SurfaceMapping.MapPoint(Vector3D.Zero, 2, 2, new SurfaceStyle { Kind = SurfaceKind.Sphere,
            Mapping = SurfaceMappingMode.Geodesic }), "geodesic antipode");
        Reject(() => SurfaceMapping.MapPoint(Vector3D.Zero, 2, 2, new SurfaceStyle { Kind = SurfaceKind.Sphere,
            Mapping = SurfaceMappingMode.Pinhole, VerticalFovRadians = Math.PI }), "pinhole 180 degree field of view");
        return checks;
    }
}
