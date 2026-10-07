using System;
using System.Collections.Generic;
using VRageMath;

namespace HoloMap
{
    public enum SurfaceKind { Plane, Cylinder, Sphere, Ellipsoid, Mesh }
    public enum SurfaceMappingMode { Angular, Geodesic, Pinhole }

    // The pose supplied by the caller places this local frame in the world. For curved
    // surfaces its origin is the sphere/cylinder centre; +Z is the centre of the patch.
    // Source XY is a centred canvas. Source Z displaces along the outward radial
    // normal; positive depthOffset moves toward the intended viewer (+Z for a plane,
    // toward the centre for an inward curved surface). Angular spans are separate
    // from pinhole VerticalFovRadians and SourceAspect (actual image width/height).
    public sealed class SurfaceStyle
    {
        public SurfaceKind Kind = SurfaceKind.Plane;
        public SurfaceMappingMode Mapping = SurfaceMappingMode.Angular;
        public double Radius = 2;
        public Vector3D Radii = new Vector3D(2,2,2);
        public Vector3D[] MeshPoints; public int[] MeshTriangles; public Vector2[] MeshUV;
        public double HorizontalRadians = Math.PI * 2;
        public double VerticalRadians = Math.PI;
        public double VerticalFovRadians = Math.PI / 2;
        public double SourceAspect = 0; // 0 uses logical canvas aspect.
        public bool Inward = true;
        public double MaxError = 0.01;
        public int MaxPoints = Geometry.MaxPoints;
        public int MaxPrimitives = Geometry.MaxPrimitives;
        public int MaxWork = GeometryWork.DefaultLimit;
    }

    // Pure local-coordinate tessellation. All output is built before returning; quota or
    // malformed-input failures leave the caller's mesh untouched.
    public static partial class SurfaceMapping
    {
        struct Face
        {
            public int A, B, C; public Vector4 Color;
            public Face(int a, int b, int c, Vector4 color) { A = a; B = b; C = c; Color = color; }
        }
        struct Line
        {
            public int A, B; public Vector4 Color;
            public Line(int a, int b, Vector4 color) { A = a; B = b; Color = color; }
        }
        sealed class Work
        {
            public int Remaining;
            public void Charge(int amount = 1)
            {
                if (amount > Remaining) throw new ArgumentException("Surface mapping work budget exceeded.");
                Remaining -= amount;
            }
        }
        static long Key(int a, int b) { return ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b); }
        static bool Finite(Vector3D p) { return Geometry.Finite(p.X) && Geometry.Finite(p.Y) && Geometry.Finite(p.Z); }
        static bool Finite(Vector2 p) { return Geometry.Finite(p.X) && Geometry.Finite(p.Y); }
        static bool Finite(Vector4 p) { return Geometry.Finite(p.X) && Geometry.Finite(p.Y) && Geometry.Finite(p.Z) && Geometry.Finite(p.W); }
        static void Canvas(double width, double height)
        {
            if (!Geometry.Finite(width) || !Geometry.Finite(height) || width <= 0 || height <= 0 || width > 50 || height > 50)
                throw new ArgumentException("Surface canvas dimensions must be finite and in (0, 50] meters.");
        }
        public static void ValidateStyle(SurfaceStyle style)
        {
            if (style == null || !Enum.IsDefined(typeof(SurfaceKind), style.Kind)
                || !Enum.IsDefined(typeof(SurfaceMappingMode), style.Mapping)
                || !Geometry.Finite(style.Radius) || style.Radius <= 0 || style.Radius > 100000
                || !Geometry.Finite(style.HorizontalRadians) || style.HorizontalRadians <= 0 || style.HorizontalRadians > 2 * Math.PI
                || !Geometry.Finite(style.VerticalRadians) || style.VerticalRadians <= 0 || style.VerticalRadians > Math.PI
                || !Geometry.Finite(style.VerticalFovRadians) || style.VerticalFovRadians <= 0.01 || style.VerticalFovRadians >= Math.PI - 0.01
                || !Geometry.Finite(style.SourceAspect) || style.SourceAspect < 0 || style.SourceAspect > 100
                || !Geometry.Finite(style.MaxError) || style.MaxError < 0.00001 || style.MaxError > 1
                || style.MaxPoints < 1 || style.MaxPoints > Geometry.MaxPoints
                || style.MaxPrimitives < 1 || style.MaxPrimitives > Geometry.MaxPrimitives
                || style.MaxWork < 1 || style.MaxWork > GeometryWork.DefaultLimit)
                throw new ArgumentException("Invalid bounded surface style.");
            if(style.Kind==SurfaceKind.Ellipsoid){if(!Finite(style.Radii)||style.Radii.X<=0||style.Radii.Y<=0||style.Radii.Z<=0||Math.Max(style.Radii.X,Math.Max(style.Radii.Y,style.Radii.Z))>100000||style.Mapping==SurfaceMappingMode.Geodesic)throw new ArgumentException("Ellipsoid requires finite positive radii and angular or pinhole mapping.");}
            if(style.Kind==SurfaceKind.Mesh){if(style.Mapping!=SurfaceMappingMode.Angular)throw new ArgumentException("Authored display meshes use their authored UV mapping.");ValidateAuthoredMesh(style.MeshPoints,style.MeshTriangles,style.MeshUV);}
            if (style.Kind == SurfaceKind.Cylinder && style.Mapping == SurfaceMappingMode.Pinhole)
                throw new ArgumentException("Pinhole mapping requires a sphere.");
            if (style.Kind == SurfaceKind.Sphere && style.Mapping == SurfaceMappingMode.Geodesic)
            {
                double reach = Math.Sqrt(style.HorizontalRadians * style.HorizontalRadians + style.VerticalRadians * style.VerticalRadians) / 2;
                if (reach >= Math.PI - 0.01) throw new ArgumentException("Geodesic patch reaches the antipode.");
            }
        }
        public static Vector3D MapPoint(Vector3D source, double canvasWidth, double canvasHeight, SurfaceStyle style, double depthOffset = 0)
        {
            Canvas(canvasWidth, canvasHeight); ValidateStyle(style);
            if (!Finite(source) || !Geometry.Finite(depthOffset) || Math.Abs(depthOffset) > 100000)
                throw new ArgumentException("Invalid surface point or depth offset.");
            return Project(source, canvasWidth, canvasHeight, style, depthOffset);
        }
        static Vector3D Project(Vector3D source, double width, double height, SurfaceStyle style, double offset)
        {
            if(style.Kind==SurfaceKind.Mesh)return AuthoredPoint(source,width,height,style,offset);
            if(style.Kind==SurfaceKind.Ellipsoid)
            {
                double ex=source.X/width,ey=source.Y/height;Vector3D ray;
                if(style.Mapping==SurfaceMappingMode.Pinhole){double etangent=Math.Tan(style.VerticalFovRadians/2),easpect=style.SourceAspect>0?style.SourceAspect:width/height;ray=new Vector3D(2*ex*etangent*easpect,2*ey*etangent,1);}
                else{double elambda=ex*style.HorizontalRadians,ephi=ey*style.VerticalRadians,ec=Math.Cos(ephi);ray=new Vector3D(ec*Math.Sin(elambda),Math.Sin(ephi),ec*Math.Cos(elambda));}
                var p=IntersectEllipsoid(ray,style.Radii);var n=EllipsoidNormal(p,style.Radii);double displacement=source.Z+(style.Inward?-offset:offset);if(Math.Abs(displacement)>=Math.Min(style.Radii.X,Math.Min(style.Radii.Y,style.Radii.Z))-.00001)throw new ArgumentException("Ellipsoid layer depth crosses its bounded shell.");return p+n*displacement;
            }
            if (style.Kind == SurfaceKind.Plane) return new Vector3D(source.X, source.Y, source.Z + offset);
            double radius = style.Radius + source.Z + (style.Inward ? -offset : offset);
            if (radius <= 0.00001 || radius > 1000000) throw new ArgumentException("Surface depth crosses its centre or exceeds model range.");
            double x = source.X / width, y = source.Y / height;
            if (style.Kind == SurfaceKind.Cylinder)
            {
                double angle = style.Mapping == SurfaceMappingMode.Geodesic ? source.X / style.Radius : x * style.HorizontalRadians;
                return new Vector3D(radius * Math.Sin(angle), source.Y, radius * Math.Cos(angle));
            }
            if (style.Mapping == SurfaceMappingMode.Pinhole)
            {
                double tangent = Math.Tan(style.VerticalFovRadians / 2);
                double aspect = style.SourceAspect > 0 ? style.SourceAspect : width / height;
                var ray = new Vector3D(2 * x * tangent * aspect, 2 * y * tangent, 1);
                return ray * (radius / ray.Length());
            }
            if (style.Mapping == SurfaceMappingMode.Geodesic)
            {
                double tx = x * style.HorizontalRadians, ty = y * style.VerticalRadians;
                double theta = Math.Sqrt(tx * tx + ty * ty);
                double sinc = theta < 1e-8 ? 1 - theta * theta / 6 : Math.Sin(theta) / theta;
                return new Vector3D(radius * tx * sinc, radius * ty * sinc, radius * Math.Cos(theta));
            }
            double lambda = x * style.HorizontalRadians, phi = y * style.VerticalRadians;
            double cosine = Math.Cos(phi);
            return new Vector3D(radius * cosine * Math.Sin(lambda), radius * Math.Sin(phi), radius * cosine * Math.Cos(lambda));
        }
        public static Vector3D Normal(Vector3D source, double canvasWidth, double canvasHeight, SurfaceStyle style)
        {
            var point=MapPoint(new Vector3D(source.X,source.Y,0),canvasWidth,canvasHeight,style);
            if(style.Kind==SurfaceKind.Plane)return Vector3D.UnitZ;
            if(style.Kind==SurfaceKind.Mesh)return AuthoredNormal(source,canvasWidth,canvasHeight,style);
            Vector3D normal=style.Kind==SurfaceKind.Ellipsoid?EllipsoidNormal(point,style.Radii):style.Kind==SurfaceKind.Cylinder?Vector3D.Normalize(new Vector3D(point.X,0,point.Z)):Vector3D.Normalize(point);
            return style.Inward?-normal:normal;
        }
        static void ValidateMesh(SurfaceMesh mesh)
        {
            if (mesh == null || mesh.Geometry == null || mesh.Geometry.Points == null || mesh.Geometry.Edges == null
                || mesh.Geometry.Triangles == null || mesh.Colors == null || mesh.EdgeColors == null)
                throw new ArgumentException("Invalid surface mesh.");
            var g = mesh.Geometry;
            if (g.Points.Length > Geometry.MaxPoints || g.Edges.Length % 2 != 0 || g.Triangles.Length % 3 != 0
                || g.Triangles.Length / 3 + g.Edges.Length / 2 > Geometry.MaxPrimitives
                || mesh.Colors.Length != g.Triangles.Length / 3 || mesh.EdgeColors.Length != g.Edges.Length / 2
                || mesh.UV != null && mesh.UV.Length != g.Points.Length)
                throw new ArgumentException("Inconsistent or excessive surface mesh arrays.");
            foreach (var point in g.Points) if (!Finite(point) || point.LengthSquared() > 1e12) throw new ArgumentException("Invalid surface source point.");
            if (mesh.UV != null) foreach (var uv in mesh.UV) if (!Finite(uv)) throw new ArgumentException("Invalid surface UV.");
            foreach (var color in mesh.Colors) if (!Finite(color)) throw new ArgumentException("Invalid surface face color.");
            foreach (var color in mesh.EdgeColors) if (!Finite(color)) throw new ArgumentException("Invalid surface edge color.");
            foreach (int index in g.Triangles) if (index < 0 || index >= g.Points.Length) throw new ArgumentException("Invalid surface triangle index.");
            foreach (int index in g.Edges) if (index < 0 || index >= g.Points.Length) throw new ArgumentException("Invalid surface edge index.");
        }
        static int Midpoint(int a, int b, List<Vector3D> source, List<Vector3D> projected, List<Vector2> uv,
            Dictionary<long, int> mids, double width, double height, SurfaceStyle style, double offset, Work work)
        {
            int index; long key = Key(a, b);
            if (mids.TryGetValue(key, out index)) return index;
            work.Charge();
            if (source.Count >= style.MaxPoints) throw new ArgumentException("Surface point budget exceeded.");
            var point = (source[a] + source[b]) * 0.5;
            index = source.Count; source.Add(point); projected.Add(Project(point, width, height, style, offset));
            if (uv != null) uv.Add((uv[a] + uv[b]) * 0.5f);
            mids.Add(key, index); return index;
        }
        static bool SplitEdge(int a, int b, List<Vector3D> source, List<Vector3D> projected,
            double width, double height, SurfaceStyle style, double offset, Work work)
        {
            work.Charge();
            var middle = Project((source[a] + source[b]) * 0.5, width, height, style, offset);
            return (middle - (projected[a] + projected[b]) * 0.5).LengthSquared() > style.MaxError * style.MaxError;
        }
        static void Add(List<Face> faces, int a, int b, int c, Vector4 color, SurfaceStyle style)
        {
            if (faces.Count >= style.MaxPrimitives) throw new ArgumentException("Surface primitive budget exceeded.");
            faces.Add(new Face(a, b, c, color));
        }
        static void Refine(Face f, bool ab, bool bc, bool ca, List<Face> next, List<Vector3D> source,
            List<Vector3D> projected, List<Vector2> uv, Dictionary<long, int> mids,
            double width, double height, SurfaceStyle style, double offset, Work work)
        {
            int a = f.A, b = f.B, c = f.C;
            // Rotate the two-edge case so the uncut edge is CA; orientation is preserved.
            if (!ab && bc && ca) { a = f.B; b = f.C; c = f.A; ab = true; bc = true; ca = false; }
            else if (ab && !bc && ca) { a = f.C; b = f.A; c = f.B; ab = true; bc = true; ca = false; }
            if (ab && bc && !ca)
            {
                int m = Midpoint(a, b, source, projected, uv, mids, width, height, style, offset, work);
                int n = Midpoint(b, c, source, projected, uv, mids, width, height, style, offset, work);
                Add(next, a, m, c, f.Color, style); Add(next, m, n, c, f.Color, style); Add(next, m, b, n, f.Color, style); return;
            }
            if (ab && bc && ca)
            {
                int m = Midpoint(a, b, source, projected, uv, mids, width, height, style, offset, work);
                int n = Midpoint(b, c, source, projected, uv, mids, width, height, style, offset, work);
                int o = Midpoint(c, a, source, projected, uv, mids, width, height, style, offset, work);
                Add(next, a, m, o, f.Color, style); Add(next, m, b, n, f.Color, style);
                Add(next, o, n, c, f.Color, style); Add(next, m, n, o, f.Color, style); return;
            }
            if (bc) { int t = a; a = b; b = c; c = t; }
            else if (ca) { int t = c; c = b; b = a; a = t; }
            int middle = Midpoint(a, b, source, projected, uv, mids, width, height, style, offset, work);
            Add(next, a, middle, c, f.Color, style); Add(next, middle, b, c, f.Color, style);
        }
        public static SurfaceMesh Warp(SurfaceMesh mesh, double canvasWidth, double canvasHeight, SurfaceStyle style, double depthOffset = 0)
        {
            Canvas(canvasWidth, canvasHeight); ValidateStyle(style); ValidateMesh(mesh);
            if (!Geometry.Finite(depthOffset) || Math.Abs(depthOffset) > 100000) throw new ArgumentException("Invalid surface depth offset.");
            if(style.Kind==SurfaceKind.Mesh)return WarpAuthored(mesh,canvasWidth,canvasHeight,style,depthOffset);
            var work = new Work { Remaining = style.MaxWork };
            var original = mesh.Geometry; var source = new List<Vector3D>(original.Points);
            var projected = new List<Vector3D>(source.Count);
            var uv = mesh.UV == null ? null : new List<Vector2>(mesh.UV);
            foreach (var point in source) { work.Charge(); projected.Add(Project(point, canvasWidth, canvasHeight, style, depthOffset)); }
            if (source.Count > style.MaxPoints) throw new ArgumentException("Surface point budget exceeded.");
            var faces = new List<Face>(original.Triangles.Length / 3);
            for (int i = 0; i < original.Triangles.Length; i += 3)
                faces.Add(new Face(original.Triangles[i], original.Triangles[i + 1], original.Triangles[i + 2], mesh.Colors[i / 3]));
            var lines = new List<Line>(original.Edges.Length / 2);
            for (int i = 0; i < original.Edges.Length; i += 2)
                lines.Add(new Line(original.Edges[i], original.Edges[i + 1], mesh.EdgeColors[i / 2]));
            if (style.Kind != SurfaceKind.Plane)
            {
                var mids = new Dictionary<long, int>();
                // Edge decisions depend only on endpoints, so both sides of a shared edge
                // split in the same pass. Midpoints are indexed once for UV and position.
                for (int pass = 0; pass < 12; pass++)
                {
                    bool changed = false; var nextFaces = new List<Face>(); var nextLines = new List<Line>();
                    foreach (var face in faces)
                    {
                        work.Charge();
                        bool ab = SplitEdge(face.A, face.B, source, projected, canvasWidth, canvasHeight, style, depthOffset, work);
                        bool bc = SplitEdge(face.B, face.C, source, projected, canvasWidth, canvasHeight, style, depthOffset, work);
                        bool ca = SplitEdge(face.C, face.A, source, projected, canvasWidth, canvasHeight, style, depthOffset, work);
                        if (ab || bc || ca)
                        {
                            changed = true; Refine(face, ab, bc, ca, nextFaces, source, projected, uv, mids,
                                canvasWidth, canvasHeight, style, depthOffset, work);
                        }
                        else
                        {
                            // A bowed interior can be missed by three straight edges.
                            var centre = (source[face.A] + source[face.B] + source[face.C]) / 3;
                            var actual = Project(centre, canvasWidth, canvasHeight, style, depthOffset);
                            var linear = (projected[face.A] + projected[face.B] + projected[face.C]) / 3;
                            if ((actual - linear).LengthSquared() > style.MaxError * style.MaxError)
                            {
                                changed = true;
                                if (source.Count >= style.MaxPoints) throw new ArgumentException("Surface point budget exceeded.");
                                int i = source.Count; source.Add(centre); projected.Add(actual);
                                if (uv != null) uv.Add((uv[face.A] + uv[face.B] + uv[face.C]) / 3f);
                                Add(nextFaces, face.A, face.B, i, face.Color, style);
                                Add(nextFaces, face.B, face.C, i, face.Color, style);
                                Add(nextFaces, face.C, face.A, i, face.Color, style);
                            }
                            else Add(nextFaces, face.A, face.B, face.C, face.Color, style);
                        }
                    }
                    foreach (var line in lines)
                    {
                        work.Charge();
                        if (SplitEdge(line.A, line.B, source, projected, canvasWidth, canvasHeight, style, depthOffset, work))
                        {
                            changed = true; int i = Midpoint(line.A, line.B, source, projected, uv, mids,
                                canvasWidth, canvasHeight, style, depthOffset, work);
                            if (nextFaces.Count + nextLines.Count + 2 > style.MaxPrimitives) throw new ArgumentException("Surface primitive budget exceeded.");
                            nextLines.Add(new Line(line.A, i, line.Color)); nextLines.Add(new Line(i, line.B, line.Color));
                        }
                        else { if (nextFaces.Count + nextLines.Count + 1 > style.MaxPrimitives) throw new ArgumentException("Surface primitive budget exceeded."); nextLines.Add(line); }
                    }
                    faces = nextFaces; lines = nextLines;
                    if (!changed) break;
                    if (pass == 11) throw new ArgumentException("Surface curvature requires more than 12 subdivision passes.");
                }
            }
            if (faces.Count + lines.Count > style.MaxPrimitives) throw new ArgumentException("Surface primitive budget exceeded.");
            if (projected.Count != 0) Geometry.ValidatePoints(projected.ToArray());
            var triangles = new int[faces.Count * 3]; var colors = new Vector4[faces.Count];
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i]; triangles[i * 3] = f.A;
                triangles[i * 3 + 1] = style.Kind != SurfaceKind.Plane && style.Inward ? f.C : f.B;
                triangles[i * 3 + 2] = style.Kind != SurfaceKind.Plane && style.Inward ? f.B : f.C;
                colors[i] = f.Color;
            }
            var edges = new int[lines.Count * 2]; var edgeColors = new Vector4[lines.Count];
            for (int i = 0; i < lines.Count; i++) { edges[i * 2] = lines[i].A; edges[i * 2 + 1] = lines[i].B; edgeColors[i] = lines[i].Color; }
            return new SurfaceMesh { Geometry = new Geometry(projected.ToArray(), edges, triangles),
                Colors = colors, EdgeColors = edgeColors, UV = uv == null ? null : uv.ToArray() };
        }
        public static SurfaceMesh BuildQuad(double width, double height, Vector4 color, SurfaceStyle style, double depthOffset = 0)
        {
            Canvas(width, height); ValidateStyle(style);
            if(!Geometry.Finite(depthOffset)||Math.Abs(depthOffset)>100000)throw new ArgumentException("Invalid quad depth.");
            if (!Finite(color)) throw new ArgumentException("Invalid quad color.");
            // Grid seeding avoids a long diagonal through a sphere's seam and poles.
            // The adaptive pass still checks actual edge and interior chord error.
            if(style.Kind==SurfaceKind.Mesh)return BuildAuthored(style,color,depthOffset);
            int nx = 1, ny = 1;
            if (style.Kind != SurfaceKind.Plane)
            {
                double scale = Math.Sqrt((style.Kind==SurfaceKind.Ellipsoid?Math.Max(style.Radii.X,Math.Max(style.Radii.Y,style.Radii.Z)):style.Radius) / (4 * style.MaxError));
                double hSpan = style.Kind == SurfaceKind.Cylinder && style.Mapping == SurfaceMappingMode.Geodesic
                    ? width / style.Radius : style.Mapping == SurfaceMappingMode.Pinhole
                    ? 2 * Math.Atan(Math.Tan(style.VerticalFovRadians / 2) * (style.SourceAspect > 0 ? style.SourceAspect : width / height))
                    : style.HorizontalRadians;
                double vSpan = style.Mapping == SurfaceMappingMode.Pinhole ? style.VerticalFovRadians : style.VerticalRadians;
                nx = Math.Max(1, (int)Math.Ceiling(hSpan * scale));
                if (style.Kind == SurfaceKind.Sphere||style.Kind==SurfaceKind.Ellipsoid) ny = Math.Max(1, (int)Math.Ceiling(vSpan * scale));
                if (nx > 64 || ny > 64 || (long)(nx + 1) * (ny + 1) > style.MaxPoints || (long)nx * ny * 2 > style.MaxPrimitives)
                    throw new ArgumentException("Curved quad exceeds the surface geometry budget.");
            }
            var points = new Vector3D[(nx + 1) * (ny + 1)];
            var uv = new Vector2[points.Length];
            var triangles = new int[nx * ny * 6];
            var colors = new Vector4[triangles.Length / 3];
            for (int y = 0; y <= ny; y++) for (int x = 0; x <= nx; x++)
            {
                int i = y * (nx + 1) + x;
                points[i] = new Vector3D((x / (double)nx - .5) * width, (y / (double)ny - .5) * height, 0);
                uv[i] = new Vector2(x / (float)nx, 1 - y / (float)ny);
            }
            for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
            {
                int i = y * nx + x, p = y * (nx + 1) + x, q = p + nx + 1;
                triangles[i * 6] = p; triangles[i * 6 + 1] = p + 1; triangles[i * 6 + 2] = q + 1;
                triangles[i * 6 + 3] = p; triangles[i * 6 + 4] = q + 1; triangles[i * 6 + 5] = q;
                colors[i * 2] = color; colors[i * 2 + 1] = color;
            }
            var mesh = new SurfaceMesh { Geometry = new Geometry(points, new int[0], triangles),
                Colors = colors, EdgeColors = new Vector4[0], UV = uv };
            return Warp(mesh, width, height, style, depthOffset);
        }
    }
}
