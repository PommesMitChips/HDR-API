using System;
using System.Collections.Generic;
using VRageMath;

namespace HoloMap
{
    public sealed class PerspectiveCamera
    {
        public Vector3D Eye, Target, Up = Vector3D.UnitY;
        // Zero selects the legacy +Z-forward convention. Physical SE cameras can
        // supply WorldMatrix.Right explicitly to retain their native handedness.
        public Vector3D Right;
        public double VerticalFovRadians = Math.PI / 3, Near = 0.1, Far = 10000;
        public int Width = 64, Height = 36;
    }

    // Viewer-local opaque rasterizer. Unknown pixels never acquire a background surface.
    // The instance owns reusable bounded scratch buffers; call it only on the simulation thread.
    public sealed class PerspectiveView
    {
        public const int MaxWidth = 128, MaxHeight = 72, MaxWork = 4000000, MaxJobPoints = 32768;
        readonly double[] inverseDepth = new double[MaxWidth * MaxHeight];
        readonly Vector4[] colors = new Vector4[MaxWidth * MaxHeight];
        readonly Vector4[] cameraDisplayColors = new Vector4[MaxWidth * MaxHeight];
        readonly bool[] known = new bool[MaxWidth * MaxHeight];
        readonly Vector3D[] normals = new Vector3D[MaxWidth * MaxHeight];
        readonly long[] sources = new long[MaxWidth * MaxHeight];
        readonly Vector3D[] cameraPoints = new Vector3D[Geometry.MaxPoints];
        readonly Vector3D[] clipA = new Vector3D[12], clipB = new Vector3D[12];
        int width, height, remaining, inputPoints, seedCount;
        bool active;
        double tangentX, tangentY, near, far, canvasWidth, canvasHeight;
        Vector3D eye, right, up, forward;
        public int Width { get { return width; } }
        public int Height { get { return height; } }
        void Charge(int count = 1)
        { remaining -= count; if (remaining < 0) throw new ArgumentException("Perspective view work budget exceeded."); }
        int Pixel(int x, int y)
        { if (x < 0 || y < 0 || x >= width || y >= height) throw new ArgumentException("Pixel outside perspective raster."); return y * width + x; }
        public bool IsKnown(int x, int y) { return known[Pixel(x, y)]; }
        public bool HasDepth(int x, int y) { return inverseDepth[Pixel(x, y)] > 0; }
        public double Depth(int x, int y) { int i = Pixel(x, y); return inverseDepth[i] > 0 ? 1 / inverseDepth[i] : double.PositiveInfinity; }
        public Vector3D NormalAt(int x, int y) { int i = Pixel(x, y); return known[i] ? normals[i] : Vector3D.Zero; }
        public long SourceAt(int x, int y) { int i = Pixel(x, y); return known[i] ? sources[i] : 0; }
        public Vector4 ColorAt(int x, int y) { int i = Pixel(x, y); return known[i] ? colors[i] : Vector4.Zero; }
        static bool Finite(Vector3D v) { return Geometry.Finite(v.X) && Geometry.Finite(v.Y) && Geometry.Finite(v.Z); }
        static void ValidateColor(Vector4 c)
        {
            if (!Geometry.Finite(c.X) || !Geometry.Finite(c.Y) || !Geometry.Finite(c.Z) || !Geometry.Finite(c.W)
                || c.X < 0 || c.X > 1 || c.Y < 0 || c.Y > 1 || c.Z < 0 || c.Z > 1 || c.W != 1)
                throw new ArgumentException("Perspective surfaces require finite opaque RGBA colors in [0,1].");
        }
        public SurfaceMesh Render(Geometry geometry, Vector4[] triangleColors, PerspectiveCamera camera,
            double canvasWidth, double canvasHeight, int workLimit = 1000000)
        {
            Begin(camera, canvasWidth, canvasHeight, workLimit);
            AddGeometry(geometry, triangleColors, MatrixD.Identity);
            return End();
        }
        // A job freezes the camera. Matrix inputs to AddGeometry map mesh coordinates into
        // this same Eye/Target coordinate frame, not into already-projected camera XYZ.
        public void Begin(PerspectiveCamera camera, double canvasWidth, double canvasHeight, int workLimit = 1000000)
        {
            Abort();
            try
            {
                if (camera == null || !Finite(camera.Eye) || !Finite(camera.Target) || !Finite(camera.Up) || !Finite(camera.Right)
                    || !Geometry.Finite(camera.VerticalFovRadians) || camera.VerticalFovRadians <= 0.01 || camera.VerticalFovRadians >= Math.PI - 0.01
                    || !Geometry.Finite(camera.Near) || !Geometry.Finite(camera.Far) || camera.Near < 0.0001 || camera.Far <= camera.Near || camera.Far > 1000000
                    || camera.Width < 1 || camera.Width > MaxWidth || camera.Height < 1 || camera.Height > MaxHeight
                    || !Geometry.Finite(canvasWidth) || !Geometry.Finite(canvasHeight) || canvasWidth <= 0 || canvasHeight <= 0 || canvasWidth > 50 || canvasHeight > 50
                    || workLimit < 1 || workLimit > MaxWork)
                    throw new ArgumentException("Invalid perspective camera, canvas or work budget.");
                forward = camera.Target - camera.Eye;
                if (!Finite(forward) || !Geometry.Finite(forward.LengthSquared()) || forward.LengthSquared() < 1e-20) throw new ArgumentException("Perspective eye and target must be distinct.");
                forward.Normalize(); right = Vector3D.Cross(camera.Up, forward);
                if (!Finite(right) || !Geometry.Finite(right.LengthSquared()) || right.LengthSquared() < 1e-20) throw new ArgumentException("Perspective up must not be parallel to the view.");
                right.Normalize(); up = Vector3D.Cross(forward, right);
                if (camera.Right != Vector3D.Zero)
                {
                    right = camera.Right;
                    if (!Geometry.Finite(right.LengthSquared()) || right.LengthSquared() < 1e-20) throw new ArgumentException("Invalid perspective explicit right basis.");
                    right.Normalize();
                    if (Math.Abs(Vector3D.Dot(right, forward)) > 1e-6 || Math.Abs(Vector3D.Dot(right, up)) > 1e-6)
                        throw new ArgumentException("Perspective explicit right must be perpendicular to forward and up.");
                }
                eye = camera.Eye;
                width = camera.Width; height = camera.Height; remaining = workLimit;
                near = camera.Near; far = camera.Far; tangentY = Math.Tan(camera.VerticalFovRadians / 2); tangentX = tangentY * width / height;
                this.canvasWidth = canvasWidth; this.canvasHeight = canvasHeight;
                inputPoints = 0; seedCount = 0; active = true;
            }
            catch { Abort(); throw; }
        }
        public void Abort()
        {
            active = false; Array.Clear(known, 0, known.Length); Array.Clear(inverseDepth, 0, inverseDepth.Length);
        }
        static void ValidateMatrix(MatrixD m)
        {
            double[] a = { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
            foreach (double value in a) if (!Geometry.Finite(value) || Math.Abs(value) > 1e12) throw new ArgumentException("Invalid perspective source transform.");
            if (m.M14 != 0 || m.M24 != 0 || m.M34 != 0 || m.M44 != 1) throw new ArgumentException("Perspective source transforms must be affine.");
        }
        public void AddGeometry(Geometry geometry, Vector4[] triangleColors, MatrixD geometryToCameraLocal, bool occluderOnly = false, long sourceId = 0)
        {
            try
            {
                if (!active) throw new ArgumentException("Begin a perspective job before adding geometry.");
                Charge();
                if (geometry == null || geometry.Points == null || geometry.Triangles == null
                    || geometry.Points.Length > Geometry.MaxPoints || geometry.Triangles.Length % 3 != 0
                    || geometry.Triangles.Length / 3 > Geometry.MaxPrimitives
                    || !occluderOnly && (triangleColors == null || triangleColors.Length != geometry.Triangles.Length / 3))
                    throw new ArgumentException("Invalid bounded perspective geometry/colors.");
                ValidateMatrix(geometryToCameraLocal);
                if (geometry.Points.Length > MaxJobPoints - inputPoints) throw new ArgumentException("Perspective job input point budget exceeded.");
                inputPoints += geometry.Points.Length;
                for (int i = 0; i < geometry.Points.Length; i++)
                {
                    Charge(); var p = geometry.Points[i]; if (!Finite(p) || p.LengthSquared() > 1e12) throw new ArgumentException("Invalid perspective point.");
                    p = Vector3D.Transform(p, geometryToCameraLocal) - eye;
                    var transformed = new Vector3D(Vector3D.Dot(p, right), Vector3D.Dot(p, up), Vector3D.Dot(p, forward));
                    if (!Finite(transformed)) throw new ArgumentException("Nonfinite perspective camera coordinates."); cameraPoints[i] = transformed;
                }
                for (int t = 0; t < geometry.Triangles.Length; t += 3)
                {
                    Charge(); var color = occluderOnly ? Vector4.Zero : triangleColors[t / 3]; if (!occluderOnly) ValidateColor(color);
                    for (int j = 0; j < 3; j++)
                    { int index = geometry.Triangles[t + j]; if (index < 0 || index >= geometry.Points.Length) throw new ArgumentException("Invalid perspective triangle index."); clipA[j] = cameraPoints[index]; }
                    var normal = Vector3D.Cross(clipA[1] - clipA[0], clipA[2] - clipA[0]);
                    if (normal.LengthSquared() > 1e-20) { normal.Normalize(); if (Vector3D.Dot(normal, clipA[0]) > 0) normal = -normal; normal = right * normal.X + up * normal.Y + forward * normal.Z; } else normal = Vector3D.Zero;
                    Vector3D[] input = clipA, output = clipB; int count = 3;
                    for (int plane = 0; plane < 6 && count > 0; plane++)
                    { count = Clip(input, count, output, plane); var swap = input; input = output; output = swap; }
                    for (int j = 1; j + 1 < count; j++) Raster(input[0], input[j], input[j + 1], color, !occluderOnly, normal, sourceId);
                }
            }
            catch { Abort(); throw; }
        }
        // Collision/raycast samples use positive camera-forward Z, not Euclidean ray distance.
        // Unknown nearest hits still block farther known surfaces without inventing a fill.
        public void SeedDepthPixel(int x, int y, double forwardZ, Vector4 color, bool isKnown, Vector3D normal = default(Vector3D), long sourceId = 0)
        {
            try
            {
                if (!active) throw new ArgumentException("Begin a perspective job before seeding depth.");
                Charge(); int index = Pixel(x, y);
                if (++seedCount > MaxWidth * MaxHeight * 4 || !Geometry.Finite(forwardZ) || forwardZ < near || forwardZ > far)
                    throw new ArgumentException("Invalid or excessive perspective depth seeds.");
                if (isKnown) { ValidateColor(color); if (!Finite(normal) || normal.LengthSquared() > 1e12) throw new ArgumentException("Invalid perspective seed normal."); if (normal.LengthSquared() > 1e-20) normal.Normalize(); else normal = Vector3D.Zero; }
                WriteHit(index, 1 / forwardZ, color, isKnown, normal, sourceId);
            }
            catch { Abort(); throw; }
        }
        public SurfaceMesh End()
        {
            try
            {
                if (!active) throw new ArgumentException("Begin a perspective job before producing a frame.");
                var result = Merge(canvasWidth, canvasHeight, false, Geometry.MaxPoints, Geometry.MaxPrimitives); active = false; return result;
            }
            catch { Abort(); throw; }
        }
        // Visual reduction drops whole original known rectangles. It never bridges an
        // unknown ray, changes a color, or removes valid capture/provenance samples.
        public SurfaceMesh EndBounded(int maxPoints, int maxPrimitives)
        {
            try
            {
                if (!active) throw new ArgumentException("Begin a perspective job before producing a frame.");
                if (maxPoints < 0 || maxPoints > Geometry.MaxPoints || maxPrimitives < 0 || maxPrimitives > Geometry.MaxPrimitives)
                    throw new ArgumentException("Invalid bounded perspective output quota.");
                var result = Merge(canvasWidth, canvasHeight, true, maxPoints, maxPrimitives); active = false; return result;
            }
            catch { Abort(); throw; }
        }
        // The camera-only presentation palette is separate from observed colors/depth/provenance.
        // Five merge scans, four palette scans, and worst final rectangle selection are reserved.
        public static int AdaptiveOutputWorkReserve(int width,int height)
        {
            if(width<1||width>MaxWidth||height<1||height>MaxHeight)throw new ArgumentException("Invalid adaptive output dimensions.");
            return 10*width*height+16*512+512;
        }
        public SurfaceMesh EndAdaptiveBounded(int maxPoints,int maxPrimitives,out bool reduced)
        {
            reduced=false;
            try
            {
                if(!active)throw new ArgumentException("Begin a perspective job before producing a camera frame.");
                if(maxPoints<0||maxPoints>Geometry.MaxPoints||maxPrimitives<0||maxPrimitives>Geometry.MaxPrimitives)throw new ArgumentException("Invalid adaptive output geometry quota.");
                if(Math.Min(maxPoints/4,maxPrimitives/2)==0)
                {
                    for(int i=0;i<width*height;i++){Charge();if(known[i])reduced=true;}
                    var empty=Merge(canvasWidth,canvasHeight,true,maxPoints,maxPrimitives);active=false;return empty;
                }
                var exact=Merge(canvasWidth,canvasHeight,true,maxPoints,maxPrimitives,null,false);
                if(exact!=null){active=false;return exact;}
                reduced=true;int[] levels={16,8,4,2};
                for(int pass=0;pass<levels.Length;pass++)
                {
                    int steps=levels[pass]-1;
                    for(int i=0;i<width*height;i++)
                    {
                        Charge();if(!known[i]){cameraDisplayColors[i]=Vector4.Zero;continue;}
                        var c=colors[i];cameraDisplayColors[i]=new Vector4((float)(Math.Round(c.X*steps)/steps),(float)(Math.Round(c.Y*steps)/steps),(float)(Math.Round(c.Z*steps)/steps),1);
                    }
                    // The final palette may thin whole known rectangles if it still cannot fit.
                    var mesh=Merge(canvasWidth,canvasHeight,true,maxPoints,maxPrimitives,cameraDisplayColors,pass==levels.Length-1);
                    if(mesh!=null){active=false;return mesh;}
                }
                throw new ArgumentException("Adaptive output did not complete its bounded presentation pass.");
            }
            catch {Abort();throw;}
        }
        void WriteHit(int index, double reciprocalZ, Vector4 color, bool isKnown, Vector3D normal, long sourceId)
        {
            if (reciprocalZ > 0 && Geometry.Finite(reciprocalZ)
                && (reciprocalZ > inverseDepth[index] || reciprocalZ == inverseDepth[index] && !isKnown))
            { inverseDepth[index] = reciprocalZ; known[index] = isKnown; colors[index] = isKnown ? color : Vector4.Zero; normals[index] = isKnown ? normal : Vector3D.Zero; sources[index] = isKnown ? sourceId : 0; }
        }
        double Plane(Vector3D p, int plane)
        {
            switch (plane)
            { case 0: return p.Z - near; case 1: return far - p.Z; case 2: return p.X + p.Z * tangentX; case 3: return p.Z * tangentX - p.X; case 4: return p.Y + p.Z * tangentY; default: return p.Z * tangentY - p.Y; }
        }
        int Clip(Vector3D[] input, int count, Vector3D[] output, int plane)
        {
            int n = 0; var a = input[count - 1]; double da = Plane(a, plane);
            for (int i = 0; i < count; i++)
            {
                Charge(); var b = input[i]; double db = Plane(b, plane);
                if ((da >= 0) != (db >= 0))
                    AppendClip(output, ref n, a + (b - a) * (da / (da - db)));
                if (db >= 0) AppendClip(output, ref n, b);
                a = b; da = db;
            }
            if (n > 1 && output[n - 1] == output[0]) n--;
            return n;
        }
        static void AppendClip(Vector3D[] output, ref int count, Vector3D point)
        {
            // Exact plane-endpoint intersections must not multiply coincident vertices.
            if (count > 0 && output[count - 1] == point) return;
            if (count == output.Length) throw new ArgumentException("Perspective clipping budget exceeded.");
            output[count++] = point;
        }
        Vector3D Project(Vector3D p)
        { return new Vector3D((p.X / (p.Z * tangentX) + 1) * width / 2, (1 - p.Y / (p.Z * tangentY)) * height / 2, 1 / p.Z); }
        static double Edge(Vector3D a, Vector3D b, double x, double y)
        { return (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X); }
        void Raster(Vector3D worldA, Vector3D worldB, Vector3D worldC, Vector4 color, bool isKnown, Vector3D normal, long sourceId)
        {
            Charge(); var a = Project(worldA); var b = Project(worldB); var c = Project(worldC); double area = Edge(a, b, c.X, c.Y);
            if (!Geometry.Finite(area) || Math.Abs(area) < 1e-12) return;
            if (area < 0) { var swap = b; b = c; c = swap; area = -area; }
            int x0 = Math.Max(0, (int)Math.Ceiling(Math.Min(a.X, Math.Min(b.X, c.X)) - 0.5));
            int x1 = Math.Min(width - 1, (int)Math.Floor(Math.Max(a.X, Math.Max(b.X, c.X)) - 0.5));
            int y0 = Math.Max(0, (int)Math.Ceiling(Math.Min(a.Y, Math.Min(b.Y, c.Y)) - 0.5));
            int y1 = Math.Min(height - 1, (int)Math.Floor(Math.Max(a.Y, Math.Max(b.Y, c.Y)) - 0.5));
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                Charge(); double wa = Edge(b, c, x + 0.5, y + 0.5), wb = Edge(c, a, x + 0.5, y + 0.5), wc = Edge(a, b, x + 0.5, y + 0.5);
                if (wa < -1e-10 || wb < -1e-10 || wc < -1e-10) continue;
                // Screen-space barycentrics interpolate reciprocal camera Z, never linear Z.
                double z = (wa * a.Z + wb * b.Z + wc * c.Z) / area; int i = y * width + x;
                WriteHit(i, z, color, isKnown, normal, sourceId);
            }
        }
        struct Run : IEquatable<Run>
        {
            public int Start, End; public Vector4 Color;
            public bool Equals(Run r) { return Start == r.Start && End == r.End && Color == r.Color; }
            public override bool Equals(object o) { return o is Run && Equals((Run)o); }
            public override int GetHashCode() { return unchecked((Start * 397 ^ End) * 397 ^ Color.GetHashCode()); }
        }
        sealed class Rectangle { public Run Run; public int Top, Bottom; }
        SurfaceMesh Merge(double canvasWidth, double canvasHeight, bool bounded, int maxPoints, int maxPrimitives,Vector4[] displayColors=null,bool allowThinning=true)
        {
            int rectangleQuota = Math.Min(maxPoints / 4, maxPrimitives / 2);
            if (bounded && rectangleQuota == 0)
                return new SurfaceMesh { Geometry = new Geometry(new Vector3D[0], new int[0], new int[0]), Colors = new Vector4[0], EdgeColors = new Vector4[0] };
            var active = new Dictionary<Run, Rectangle>(); var rectangles = new List<Rectangle>();
            for (int y = 0; y < height; y++)
            {
                var next = new Dictionary<Run, Rectangle>(); int x = 0;
                while (x < width)
                {
                    Charge(); int i = y * width + x; if (!known[i]) { x++; continue; }
                    var palette=displayColors??colors;
                    var run = new Run { Start = x, Color = palette[i] }; x++;
                    while (x < width && known[y * width + x] && palette[y * width + x] == run.Color) { Charge(); x++; }
                    run.End = x; Rectangle r;
                    if (active.TryGetValue(run, out r)) r.Bottom = y + 1;
                    else
                    {
                        r = new Rectangle { Run = run, Top = y, Bottom = y + 1 }; rectangles.Add(r);
                        if (rectangles.Count > (bounded ? width * height : rectangleQuota)) throw new ArgumentException("Perspective output exceeds geometry budget; reduce resolution or colors.");
                    }
                    next.Add(run, r);
                }
                active = next;
            }
            if(bounded&&!allowThinning&&rectangles.Count>rectangleQuota)return null;
            if (bounded && rectangles.Count > rectangleQuota) rectangles = SelectRectangles(rectangles, rectangleQuota);
            var points = new Vector3D[bounded ? rectangles.Count * 4 : Math.Max(1, rectangles.Count * 4)]; var triangles = new int[rectangles.Count * 6]; var paint = new Vector4[rectangles.Count * 2];
            if (paint.Length > maxPrimitives || points.Length > maxPoints) throw new ArgumentException("Perspective output primitive/point budget exceeded.");
            for (int i = 0; i < rectangles.Count; i++)
            {
                Charge(); var r = rectangles[i]; double left = (r.Run.Start / (double)width - 0.5) * canvasWidth, right = (r.Run.End / (double)width - 0.5) * canvasWidth;
                double top = (0.5 - r.Top / (double)height) * canvasHeight, bottom = (0.5 - r.Bottom / (double)height) * canvasHeight; int p = i * 4, t = i * 6;
                points[p] = new Vector3D(left, top, 0); points[p + 1] = new Vector3D(right, top, 0); points[p + 2] = new Vector3D(right, bottom, 0); points[p + 3] = new Vector3D(left, bottom, 0);
                triangles[t] = p; triangles[t + 1] = p + 1; triangles[t + 2] = p + 2; triangles[t + 3] = p; triangles[t + 4] = p + 2; triangles[t + 5] = p + 3; paint[i * 2] = paint[i * 2 + 1] = r.Run.Color;
            }
            return new SurfaceMesh { Geometry = new Geometry(points, new int[0], triangles), Colors = paint, EdgeColors = new Vector4[0] };
        }
        List<Rectangle> SelectRectangles(List<Rectangle> input, int quota)
        {
            var bins = new List<Rectangle>[16]; var consumed = new int[16];
            for (int i = 0; i < bins.Length; i++) bins[i] = new List<Rectangle>();
            foreach (var rectangle in input)
            {
                Charge(); int bx = Math.Min(3, (rectangle.Run.Start + rectangle.Run.End) * 2 / width);
                int by = Math.Min(3, (rectangle.Top + rectangle.Bottom) * 2 / height);
                bins[by * 4 + bx].Add(rectangle);
            }
            // Corners first gives even a four-rectangle budget broad spatial coverage;
            // subsequent rounds distribute the remaining quota across all occupied bins.
            int[] order = { 0, 15, 3, 12, 5, 10, 6, 9, 1, 14, 2, 13, 4, 11, 7, 8 };
            var selected = new List<Rectangle>(quota);
            while (selected.Count < quota)
                for (int i = 0; i < order.Length && selected.Count < quota; i++)
                {
                    Charge(); int bin = order[i];
                    if (consumed[bin] < bins[bin].Count) selected.Add(bins[bin][consumed[bin]++]);
                }
            return selected;
        }
    }
}
