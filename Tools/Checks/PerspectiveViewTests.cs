using System;
using System.Collections.Generic;
using HoloMap;
using VRageMath;

internal static class PerspectiveViewTests
{
    static int checks;
    static readonly Vector4 Red = new Vector4(1, 0, 0, 1), Blue = new Vector4(0, 0, 1, 1), Green = new Vector4(0, 1, 0, 1);
    static void Check(bool value, string name) { if (!value) throw new Exception("Perspective view: " + name); checks++; }
    static void Reject(Action action, string name)
    { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("Perspective expected rejection: " + name); }
    static PerspectiveCamera Camera(int width = 64, int height = 36)
    { return new PerspectiveCamera { Eye = Vector3D.Zero, Target = Vector3D.UnitZ, Up = Vector3D.UnitY, VerticalFovRadians = Math.PI / 2, Near = 1, Far = 100, Width = width, Height = height }; }
    static Vector3D P(double x, double y, double z, PerspectiveCamera c)
    { double tangent = Math.Tan(c.VerticalFovRadians / 2); return new Vector3D(x * z * tangent * c.Width / c.Height, y * z * tangent, z); }
    static Geometry Triangle(PerspectiveCamera c, double a, double b, double d)
    { return new Geometry(new[] { P(-0.8, -0.8, a, c), P(0.8, -0.8, b, c), P(0, 0.8, d, c) }, new int[0], new[] { 0, 1, 2 }); }
    static Geometry Combine(params Geometry[] all)
    {
        var p = new List<Vector3D>(); var t = new List<int>();
        foreach (var g in all) { int offset = p.Count; p.AddRange(g.Points); foreach (int i in g.Triangles) t.Add(i + offset); }
        return new Geometry(p.ToArray(), new int[0], t.ToArray());
    }
    static Geometry Quad(PerspectiveCamera c, double left, double right, double bottom, double top, double z)
    { return new Geometry(new[] { P(left, bottom, z, c), P(right, bottom, z, c), P(right, top, z, c), P(left, top, z, c) }, new int[0], new[] { 0, 1, 2, 0, 2, 3 }); }
    public static int Run()
    {
        checks = 0; CrossingDepth(); ThinOccluder(); UnknownAndClipping(); CameraPoses(); LimitsAndRecovery(); IncrementalBatches(); SeedsAndOccluders(); SharedQuotas(); BoundedOutput(); AdaptiveOutput(); return checks;
    }
    static void CrossingDepth()
    {
        var c = Camera(); var view = new PerspectiveView(); var near = Triangle(c, 2, 10, 10); var middle = Triangle(c, 6, 6, 6);
        view.Render(Combine(near, middle), new[] { Red, Blue }, c, 2, 1.125);
        Check(view.ColorAt(32, 18) == Red, "reciprocal depth beats constant Z6 at center where linear Z would lose");
        double nx = (32.5 / c.Width * 2 - 1), ny = 1 - 18.5 / c.Height * 2;
        double wd = (ny + 0.8) / 1.6, wb = (1 - wd + nx / 0.8) / 2, wa = 1 - wd - wb;
        double expected = 1 / (wa / 2 + wb / 10 + wd / 10);
        Check(Math.Abs(view.Depth(32, 18) - expected) < 1e-10, "first-fragment depth agrees with analytic reciprocal-Z interpolation");
        Check(view.ColorAt(32, 8) == Blue, "constant Z6 occludes far portion of slanted triangle");
        view.Render(Combine(middle, near), new[] { Blue, Red }, c, 2, 1.125);
        Check(view.ColorAt(32, 18) == Red && view.ColorAt(32, 8) == Blue, "unequal-depth visibility is independent of triangle submission order");
    }
    static void ThinOccluder()
    {
        var c = Camera(); var view = new PerspectiveView();
        view.Render(Combine(Quad(c, -0.8, 0.8, -0.8, 0.8, 8), Quad(c, 0.008, 0.025, -0.7, 0.7, 2)), new[] { Blue, Blue, Red, Red }, c, 2, 1.125);
        Check(view.ColorAt(32, 18) == Red && Math.Abs(view.Depth(32, 18) - 2) < 1e-10, "subpixel-width occluder covering the pixel center remains visible");
        Check(view.ColorAt(31, 18) == Blue && view.ColorAt(33, 18) == Blue, "thin occluder does not flood neighboring rays");
    }
    static void UnknownAndClipping()
    {
        var c = Camera(); var view = new PerspectiveView();
        var mesh = view.Render(Triangle(c, 4, 4, 4), new[] { Green }, c, 2, 1.125);
        Check(!view.IsKnown(0, 0) && double.IsPositiveInfinity(view.Depth(0, 0)) && view.ColorAt(0, 0) == Vector4.Zero, "unobserved rays remain explicitly unknown");
        Check(mesh.Geometry.Triangles.Length > 0 && mesh.Geometry.Points.Length <= Geometry.MaxPoints, "known pixels become bounded canvas rectangles");
        var ring = Combine(Quad(c, -0.8, 0.8, 0.2, 0.8, 3), Quad(c, -0.8, 0.8, -0.8, -0.2, 3),
            Quad(c, -0.8, -0.2, -0.2, 0.2, 3), Quad(c, 0.2, 0.8, -0.2, 0.2, 3));
        var hole = view.Render(ring, new[] { Green, Green, Green, Green, Green, Green, Green, Green }, c, 2, 1);
        Check(!view.IsKnown(32, 18) && view.IsKnown(32, 5), "interior unsampled hole stays unknown inside otherwise known terrain");
        bool coversCenter = false;
        for (int t = 0; t < hole.Geometry.Triangles.Length; t += 3)
        {
            var a = hole.Geometry.Points[hole.Geometry.Triangles[t]]; var b = hole.Geometry.Points[hole.Geometry.Triangles[t + 1]]; var d = hole.Geometry.Points[hole.Geometry.Triangles[t + 2]];
            double ab = a.X * b.Y - a.Y * b.X, bd = b.X * d.Y - b.Y * d.X, da = d.X * a.Y - d.Y * a.X;
            if (ab >= 0 && bd >= 0 && da >= 0 || ab <= 0 && bd <= 0 && da <= 0) coversCenter = true;
        }
        Check(!coversCenter, "rectangle merging never bridges the unknown interior hole");
        var empty = view.Render(new Geometry(new Vector3D[0], new int[0], new int[0]), new Vector4[0], c, 2, 1);
        Check(empty.Geometry.Triangles.Length == 0 && !view.IsKnown(32, 18), "empty geometry creates no background or retained hits");
        view.Render(Triangle(c, 0.5, 4, 4), new[] { Green }, c, 2, 1);
        int hits = 0; for (int y = 0; y < view.Height; y++) for (int x = 0; x < view.Width; x++) if (view.IsKnown(x, y))
        { hits++; Check(view.Depth(x, y) >= c.Near - 1e-9 && view.Depth(x, y) <= c.Far + 1e-9, "near-clipped fragments lie inside the depth interval"); }
        Check(hits > 0, "triangle crossing the near plane is clipped rather than discarded");
        view.Render(Triangle(c, 0.5, 0.5, 0.5), new[] { Red }, c, 2, 1); Check(!view.IsKnown(32, 18), "geometry entirely before near plane is excluded");
        view.Render(Triangle(c, 101, 101, 101), new[] { Red }, c, 2, 1); Check(!view.IsKnown(32, 18), "geometry entirely beyond far plane is excluded");
        view.Render(Triangle(c, -4, -4, -4), new[] { Red }, c, 2, 1); Check(!view.IsKnown(32, 18), "geometry behind the eye is excluded");
        view.Render(Quad(c, -10, 10, -10, 10, 3), new[] { Green, Green }, c, 2, 1);
        Check(view.IsKnown(0, 0) && view.IsKnown(63, 35), "frustum-clipped large surfaces cover the bounded raster");
        var merged = view.Render(Quad(c, -1, 1, -1, 1, 3), new[] { Green, Green }, c, 2, 1);
        Check(merged.Geometry.Points.Length == 4 && merged.Geometry.Triangles.Length == 6, "same-color full canvas merges into one rectangle");
    }
    static void CameraPoses()
    {
        var c = Camera(); var view = new PerspectiveView(); var g = Triangle(c, 4, 4, 4);
        var shifted = new Vector3D[g.Points.Length]; var displacement = new Vector3D(300, -20, 70); for (int i = 0; i < shifted.Length; i++) shifted[i] = g.Points[i] + displacement;
        c.Eye += displacement; c.Target += displacement;
        view.Render(new Geometry(shifted, new int[0], g.Triangles), new[] { Red }, c, 2, 1);
        Check(view.ColorAt(32, 18) == Red && Math.Abs(view.Depth(32, 18) - 4) < 1e-10, "translated camera and world preserve view/depth");
        c = Camera(); c.Target = Vector3D.UnitX; var rotated = new Vector3D[g.Points.Length];
        for (int i = 0; i < rotated.Length; i++) rotated[i] = new Vector3D(g.Points[i].Z, g.Points[i].Y, -g.Points[i].X);
        view.Render(new Geometry(rotated, new int[0], g.Triangles), new[] { Blue }, c, 2, 1);
        Check(view.ColorAt(32, 18) == Blue && Math.Abs(view.Depth(32, 18) - 4) < 1e-10, "rotated camera uses its basis rather than fixed world axes");
        c.Up = Vector3D.UnitX; Reject(() => view.Render(g, new[] { Red }, c, 2, 1), "parallel camera up");
    }
    static void LimitsAndRecovery()
    {
        var c = Camera(); var view = new PerspectiveView(); var g = Triangle(c, 4, 4, 4);
        Reject(() => view.Render(g, new[] { Red }, c, 2, 1, 1), "work exhaustion");
        Check(!view.IsKnown(32, 18), "work failure clears partial hit validity");
        view.Render(g, new[] { Red }, c, 2, 1); Check(view.IsKnown(32, 18), "instance recovers after work failure");
        Reject(() => view.Render(g, new[] { new Vector4(1, 0, 0, 0.5f) }, c, 2, 1), "translucent surfaces are not silently treated as opaque");
        c.Width = 129; Reject(() => view.Render(g, new[] { Red }, c, 2, 1), "raster width bound");
        c = Camera(32, 32); var points = new Vector3D[33 * 33]; var indices = new List<int>(); var colors = new List<Vector4>();
        for (int y = 0; y <= 32; y++) for (int x = 0; x <= 32; x++) points[y * 33 + x] = P(x / 16.0 - 1, 1 - y / 16.0, 2, c);
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        { int p = y * 33 + x; indices.AddRange(new[] { p, p + 1, p + 34, p, p + 34, p + 33 }); var color = (x + y) % 2 == 0 ? Red : Blue; colors.Add(color); colors.Add(color); }
        var checker = new Geometry(points, new int[0], indices.ToArray());
        Reject(() => view.Render(checker, colors.ToArray(), c, 2, 2), "checkerboard output exceeds point cap");
        Check(!view.IsKnown(0, 0), "output overflow returns no partial known surface");
    }
    static void IncrementalBatches()
    {
        var c = Camera(); var near = Triangle(c, 2, 10, 10); var back = Triangle(c, 6, 6, 6);
        var single = new PerspectiveView(); single.Render(Combine(near, back), new[] { Red, Blue }, c, 2, 1);
        var batched = new PerspectiveView(); batched.Begin(c, 2, 1);
        batched.AddGeometry(back, new[] { Blue }, MatrixD.Identity, false, 20);
        batched.AddGeometry(near, new[] { Red }, MatrixD.Identity, false, 10); var result = batched.End();
        bool equal = true; for (int y = 0; y < c.Height; y++) for (int x = 0; x < c.Width; x++)
        { if (single.IsKnown(x, y) != batched.IsKnown(x, y) || single.Depth(x, y) != batched.Depth(x, y) || single.ColorAt(x, y) != batched.ColorAt(x, y)) equal = false; }
        Check(equal && result.Geometry.Triangles.Length > 0, "incremental independently submitted meshes exactly match combined raster");
        Check(batched.SourceAt(32, 18) == 10 && batched.SourceAt(32, 8) == 20, "winning surface provenance follows depth rather than submission order");
        Check(Math.Abs(batched.NormalAt(32, 18).Length() - 1) < 1e-12 && batched.NormalAt(32, 18).Z < 0, "winning normals are normalized and face the frozen camera");
        var translated = new PerspectiveView(); translated.Begin(c, 2, 1); var displacement = new Vector3D(9, 8, 7); var local = new Vector3D[near.Points.Length];
        for (int i = 0; i < local.Length; i++) local[i] = near.Points[i] - displacement;
        translated.AddGeometry(new Geometry(local, new int[0], near.Triangles), new[] { Red }, MatrixD.CreateTranslation(displacement)); translated.End();
        Check(Math.Abs(translated.Depth(32, 18) - batched.Depth(32, 18)) < 1e-10, "mesh matrix maps into the Eye/Target frame before camera projection");
        var frozen = new PerspectiveView(); frozen.Begin(c, 2, 1); c.Eye = new Vector3D(900, 900, 900); c.Width = 1;
        frozen.AddGeometry(near, new[] { Red }, MatrixD.Identity); frozen.End();
        Check(frozen.Width == 64 && frozen.ColorAt(32, 18) == Red, "camera mutations after Begin cannot change a pending frame");
        Reject(() => frozen.AddGeometry(near, new[] { Red }, MatrixD.Identity), "geometry after End requires a new job");
        var physical = Camera(); physical.Target = -Vector3D.UnitZ; physical.Right = Vector3D.UnitX;
        var physicalMesh = new Geometry(new[] { new Vector3D(1, -1, -4), new Vector3D(3, -1, -4), new Vector3D(2, 1, -4) }, new int[0], new[] { 0, 1, 2 });
        frozen.Begin(physical, 2, 1); frozen.AddGeometry(physicalMesh, new[] { Red }, MatrixD.Identity); frozen.End();
        Check(frozen.IsKnown(41, 18) && !frozen.IsKnown(22, 18), "explicit physical camera right prevents a horizontally mirrored feed");
    }
    static void SeedsAndOccluders()
    {
        var c = Camera(); var view = new PerspectiveView(); var back = Quad(c, -0.8, 0.8, -0.8, 0.8, 8); var mask = Quad(c, -0.2, 0.2, -0.2, 0.2, 2);
        view.Begin(c, 2, 1); view.AddGeometry(back, new[] { Blue, Blue }, MatrixD.Identity, false, 10);
        view.AddGeometry(mask, null, MatrixD.Identity, true, 99); view.End();
        Check(!view.IsKnown(32, 18) && view.HasDepth(32, 18) && Math.Abs(view.Depth(32, 18) - 2) < 1e-10, "unknown occluder removes a farther known fill while retaining first depth");
        Check(view.SourceAt(32, 18) == 0 && view.NormalAt(32, 18) == Vector3D.Zero && view.ColorAt(32, 18) == Vector4.Zero, "unknown occluder publishes no invented provenance, normal or color");
        view.Begin(c, 2, 1); view.AddGeometry(mask, null, MatrixD.Identity, true); view.AddGeometry(back, new[] { Blue, Blue }, MatrixD.Identity); view.End();
        Check(!view.IsKnown(32, 18), "a farther known fragment cannot overwrite an earlier unknown nearest occluder");
        view.Begin(c, 2, 1); view.AddGeometry(mask, null, MatrixD.Identity, true);
        view.AddGeometry(Quad(c, -0.2, 0.2, -0.2, 0.2, 1.5), new[] { Red, Red }, MatrixD.Identity, false, 30); view.End();
        Check(view.IsKnown(32, 18) && view.SourceAt(32, 18) == 30 && view.ColorAt(32, 18) == Red, "a genuinely nearer known surface replaces an unknown occluder");
        view.Begin(c, 2, 1); view.SeedDepthPixel(32, 18, 3, Green, true, new Vector3D(0, 0, -4), 40);
        view.AddGeometry(back, new[] { Blue, Blue }, MatrixD.Identity, false, 10); view.End();
        Check(view.ColorAt(32, 18) == Green && view.Depth(32, 18) == 3 && view.SourceAt(32, 18) == 40 && view.NormalAt(32, 18) == -Vector3D.UnitZ, "collision depth seeds retain their nearer color, normalized normal and provenance");
        view.Begin(c, 2, 1); view.SeedDepthPixel(32, 18, 3, Vector4.Zero, false, Vector3D.UnitY, 42);
        view.AddGeometry(back, new[] { Blue, Blue }, MatrixD.Identity); view.End();
        Check(!view.IsKnown(32, 18) && view.Depth(32, 18) == 3 && view.SourceAt(32, 18) == 0, "unknown collision seed validates occlusion without drawing terrain");
        view.Begin(c, 2, 1); Reject(() => view.SeedDepthPixel(32, 18, double.NaN, Green, true), "nonfinite seed depth");
        Check(!view.HasDepth(32, 18), "invalid seed aborts and clears partial depth state");
    }
    static void SharedQuotas()
    {
        var c = Camera(); var view = new PerspectiveView(); var cloud = new Vector3D[Geometry.MaxPoints];
        var geometry = new Geometry(cloud, new int[0], new int[0]); view.Begin(c, 2, 1, 1000);
        var small = new Geometry(new Vector3D[600], new int[0], new int[0]); view.AddGeometry(small, new Vector4[0], MatrixD.Identity);
        Reject(() => view.AddGeometry(small, new Vector4[0], MatrixD.Identity), "independent batches share one work quota");
        view.Begin(c, 2, 1); for (int i = 0; i < PerspectiveView.MaxJobPoints / Geometry.MaxPoints; i++) view.AddGeometry(geometry, new Vector4[0], MatrixD.Identity);
        Reject(() => view.AddGeometry(new Geometry(new[] { Vector3D.Zero }, new int[0], new int[0]), new Vector4[0], MatrixD.Identity), "job point count is bounded across batches");
        view.Begin(c, 2, 1); var invalid = MatrixD.Identity; invalid.M14 = 1;
        Reject(() => view.AddGeometry(Triangle(c, 4, 4, 4), new[] { Red }, invalid), "nonaffine mesh transform");
        view.Begin(c, 2, 1); view.SeedDepthPixel(32, 18, 2, Red, true); view.Abort();
        Check(!view.IsKnown(32, 18) && !view.HasDepth(32, 18), "explicit abort clears seeded validity and depth");
    }
    static void BoundedOutput()
    {
        var c = Camera(32, 32); var view = new PerspectiveView();
        Action seed = () =>
        {
            view.Begin(c, 2, 2);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                if (x < 12 || x >= 20 || y < 12 || y >= 20)
                    view.SeedDepthPixel(x, y, 3, (x + y) % 2 == 0 ? Red : Blue, true, -Vector3D.UnitZ, 42);
        };
        seed(); var mesh = view.EndBounded(256, 128);
        Check(mesh.Geometry.Points.Length <= 256 && mesh.Geometry.Triangles.Length / 3 <= 128, "bounded End fits both quotas before constructing output arrays");
        bool[] bins = new bool[16]; bool correct = true;
        for (int i = 0; i < mesh.Geometry.Points.Length; i += 4)
        {
            var a = mesh.Geometry.Points[i]; var b = mesh.Geometry.Points[i + 2];
            int x0 = (int)Math.Round((a.X / 2 + 0.5) * 32), x1 = (int)Math.Round((b.X / 2 + 0.5) * 32);
            int y0 = (int)Math.Round((0.5 - a.Y / 2) * 32), y1 = (int)Math.Round((0.5 - b.Y / 2) * 32);
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
                if (!view.IsKnown(x, y) || view.ColorAt(x, y) != mesh.Colors[i / 2]) correct = false;
            bins[Math.Min(3, (x0 + x1) * 2 / 32) + Math.Min(3, (y0 + y1) * 2 / 32) * 4] = true;
        }
        Check(correct && !view.IsKnown(16, 16), "bounded mesh only retains original exact-color rectangles and never bridges unknown holes");
        bool allBins = true; foreach (bool covered in bins) if (!covered) allBins = false;
        Check(allBins, "bounded visual selection distributes geometry across occupied spatial regions");
        Check(view.IsKnown(10, 10) && view.SourceAt(10, 10) == 42 && view.Depth(10, 10) == 3, "visual omission preserves full captured depth and provenance mask");
        seed(); var four = view.EndBounded(16, 8);
        bool[] quadrants = new bool[4];
        for (int i = 0; i < four.Geometry.Points.Length; i += 4)
        { var p = four.Geometry.Points[i]; quadrants[(p.X < 0 ? 0 : 1) + (p.Y >= 0 ? 0 : 2)] = true; }
        Check(quadrants[0] && quadrants[1] && quadrants[2] && quadrants[3], "tiny four-rectangle visual quota still covers all four image quadrants");
        seed(); var empty = view.EndBounded(0, 0);
        Check(empty.Geometry.Points.Length == 0 && empty.Geometry.Triangles.Length == 0 && view.IsKnown(0, 0), "zero visual quota returns empty geometry without invalidating capture");
        seed(); var primitiveLimited = view.EndBounded(256, 3);
        Check(primitiveLimited.Geometry.Points.Length == 4 && primitiveLimited.Geometry.Triangles.Length == 6, "primitive quota is rounded down to complete original rectangles");
        seed(); Reject(() => view.End(), "strict End still rejects excess output instead of reducing it");
        seed(); Reject(() => view.EndBounded(Geometry.MaxPoints + 1, 2), "bounded End cannot exceed hard geometry caps");
    }
    static double MeshArea(SurfaceMesh mesh)
    {double result=0;var g=mesh.Geometry;for(int i=0;i<g.Triangles.Length;i+=3)result+=Vector3D.Cross(g.Points[g.Triangles[i+1]]-g.Points[g.Triangles[i]],g.Points[g.Triangles[i+2]]-g.Points[g.Triangles[i]]).Length()/2;return result;}
    static void AdaptiveOutput()
    {
        var camera=Camera();var view=new PerspectiveView();int count=camera.Width*camera.Height;bool reduced;
        Action<bool> seed=hole=>
        {
            view.Begin(camera,4,2,200000);
            for(int y=0;y<camera.Height;y++)for(int x=0;x<camera.Width;x++)
            {
                if(hole&&x>=24&&x<40&&y>=12&&y<20)continue;
                float shade=.3f+x*.35f/64+y*.05f/36;
                view.SeedDepthPixel(x,y,3,new Vector4(shade,shade,shade,1),true,-Vector3D.UnitZ,42);
            }
        };
        seed(false);var old=view.EndBounded(2048,4096);
        Check(Math.Abs(MeshArea(old)-8*512.0/count)<1e-9,"exact-color bounded output drops most uniquely shaded camera pixels");
        seed(false);var adaptive=view.EndAdaptiveBounded(2048,4096,out reduced);
        Check(reduced&&adaptive.Geometry.Points.Length<=2048&&Math.Abs(MeshArea(adaptive)-8)<1e-9,"display-only color quantization restores every known grey-plane pixel before thinning");
        float original=.3f+10*.35f/64+10*.05f/36;
        Check(view.ColorAt(10,10)==new Vector4(original,original,original,1)&&view.IsKnown(10,10)&&view.Depth(10,10)==3&&view.NormalAt(10,10)==-Vector3D.UnitZ&&view.SourceAt(10,10)==42,"display presentation palette leaves original color, depth, mask, normal and source untouched");
        seed(true);var perforated=view.EndAdaptiveBounded(2048,4096,out reduced);
        Check(Math.Abs(MeshArea(perforated)-8*(count-128.0)/count)<1e-9&&!view.IsKnown(32,16),"adaptive camera rectangles never fill unknown interior holes");
        view.Begin(camera,4,2,200000);
        for(int y=0;y<camera.Height;y++)for(int x=0;x<camera.Width;x++)view.SeedDepthPixel(x,y,3,(x+y)%2==0?Red:Blue,true);
        var thinned=view.EndAdaptiveBounded(128,64,out reduced);
        Check(reduced&&thinned.Geometry.Points.Length<=128&&thinned.Geometry.Triangles.Length/3<=64&&MeshArea(thinned)>0,"irreducible camera color patterns thin only after all bounded palette passes");
        seed(false);var empty=view.EndAdaptiveBounded(0,0,out reduced);
        Check(reduced&&empty.Geometry.Points.Length==0&&view.IsKnown(10,10),"zero adaptive output quota preserves captured observations");
        view.Begin(camera,4,2,200000);view.SeedDepthPixel(10,10,3,Red,true);
        var exact=view.EndAdaptiveBounded(2048,4096,out reduced);
        Check(!reduced&&exact.Colors[0]==Red,"fitting adaptive output retains exact colors without invoking palette reduction");
        Check(PerspectiveView.AdaptiveOutputWorkReserve(64,36)==10*64*36+8704,"shared camera reserve includes all palette, merge and selection passes");
        Reject(()=>PerspectiveView.AdaptiveOutputWorkReserve(129,36),"invalid camera reserve dimensions");
        seed(false);Reject(()=>view.EndAdaptiveBounded(2049,4096,out reduced),"adaptive output still enforces hard geometry limits");
    }
}
