using System;
using System.Collections.Generic;
using VRageMath;

namespace HoloMap
{
    // No game entities or rendering dependencies: also compiled by the geometry tests.
    public sealed class Geometry
    {
        public const int MaxPoints = 2048;
        public const int MaxPrimitives = 4096;
        public readonly Vector3D[] Points;
        public readonly int[] Edges;
        public readonly int[] Triangles;
        public readonly Vector3D[] WorldPoints;

        internal Geometry(Vector3D[] points, int[] edges, int[] triangles)
        {
            Points = (Vector3D[])points.Clone();
            Edges = edges;
            Triangles = triangles;
            WorldPoints = new Vector3D[points.Length];
        }

        public static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
        public static void ValidatePoints(Vector3D[] points)
        {
            if (points == null || points.Length == 0 || points.Length > MaxPoints)
                throw new ArgumentException("Supply 1 through " + MaxPoints + " points.");
            foreach (var p in points)
                if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z) || p.LengthSquared() > 1e12)
                    throw new ArgumentException("Points must be finite and within 1,000,000 model units of the origin.");
        }

        static void Index(int index, int count)
        {
            if (index < 0 || index >= count) throw new ArgumentException("Point index is outside the point array.");
        }

        static long EdgeKey(int a, int b) { return ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b); }
        static void Edge(List<int> edges, HashSet<long> seen, int a, int b)
        {
            if (seen.Add(EdgeKey(a, b))) { edges.Add(a); edges.Add(b); }
        }

        public static Geometry Wires(Vector3D[] points, Vector2I[] connections)
        {
            ValidatePoints(points);
            if (connections == null || connections.Length == 0 || connections.Length > MaxPrimitives)
                throw new ArgumentException("Supply 1 through " + MaxPrimitives + " connections.");
            var edges = new List<int>();
            var seen = new HashSet<long>();
            foreach (var c in connections)
            {
                Index(c.X, points.Length); Index(c.Y, points.Length);
                if ((points[c.X] - points[c.Y]).LengthSquared() < 1e-20)
                    throw new ArgumentException("Line endpoints must be distinct.");
                Edge(edges, seen, c.X, c.Y);
            }
            return new Geometry(points, edges.ToArray(), new int[0]);
        }

        public static Geometry Polygons(Vector3D[] points, int[][] faces)
        { using (GeometryWork.Begin()) return PolygonsCore(points,faces); }
        static Geometry PolygonsCore(Vector3D[] points, int[][] faces)
        {
            ValidatePoints(points);
            if (faces == null || faces.Length == 0 || faces.Length > MaxPrimitives)
                throw new ArgumentException("Supply a nonempty face array.");
            var edges = new List<int>();
            var triangles = new List<int>();
            var seen = new HashSet<long>();
            foreach (var face in faces)
            {
                // Bounding face size also bounds the quadratic triangulation work.
                if (face == null || face.Length < 3 || face.Length > 128)
                    throw new ArgumentException("A face requires 3 through 128 ordered indices; omit the repeated closing vertex.");
                if (triangles.Count / 3 + face.Length - 2 > MaxPrimitives)
                    throw new ArgumentException("Too many triangles.");
                var unique = new HashSet<int>();
                foreach (int i in face)
                {
                    Index(i, points.Length);
                    if (!unique.Add(i)) throw new ArgumentException("Face indices must not repeat.");
                }
                Triangulate(points, face, triangles);
                for (int i = 0; i < face.Length; i++) Edge(edges, seen, face[i], face[(i + 1) % face.Length]);
                if (edges.Count / 2 > MaxPrimitives) throw new ArgumentException("Too many boundary edges.");
            }
            return new Geometry(points, edges.ToArray(), triangles.ToArray());
        }

        static double Cross(Vector2D a, Vector2D b, Vector2D c)
        { return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X); }

        static bool OnSegment(Vector2D a, Vector2D b, Vector2D p, double epsilon, double coordinateEpsilon)
        {
            return Math.Abs(Cross(a, b, p)) <= epsilon
                && p.X >= Math.Min(a.X, b.X) - coordinateEpsilon && p.X <= Math.Max(a.X, b.X) + coordinateEpsilon
                && p.Y >= Math.Min(a.Y, b.Y) - coordinateEpsilon && p.Y <= Math.Max(a.Y, b.Y) + coordinateEpsilon;
        }

        static bool Intersects(Vector2D a, Vector2D b, Vector2D c, Vector2D d, double e, double ce)
        {
            double abC = Cross(a, b, c), abD = Cross(a, b, d), cdA = Cross(c, d, a), cdB = Cross(c, d, b);
            if (((abC > e && abD < -e) || (abC < -e && abD > e))
                && ((cdA > e && cdB < -e) || (cdA < -e && cdB > e))) return true;
            return OnSegment(a, b, c, e, ce) || OnSegment(a, b, d, e, ce)
                || OnSegment(c, d, a, e, ce) || OnSegment(c, d, b, e, ce);
        }

        static void Triangulate(Vector3D[] points, int[] face, List<int> triangles)
        {
            // Translate first to avoid cancellation in large model coordinates.
            var origin = points[face[0]];
            var normal = Vector3D.Zero;
            double extent = 0;
            for (int i = 0; i < face.Length; i++)
            {
                var a = points[face[i]] - origin;
                var b = points[face[(i + 1) % face.Length]] - origin;
                normal += Vector3D.Cross(a, b);
                extent = Math.Max(extent, a.Length());
            }
            double lengthTolerance = Math.Max(1e-9, extent * 1e-8);
            double areaTolerance = Math.Max(1e-18, extent * extent * 1e-10);
            if (normal.Length() <= areaTolerance) throw new ArgumentException("Face is degenerate or self-intersecting.");
            normal.Normalize();
            var axis = Math.Abs(normal.X) < 0.8 ? Vector3D.UnitX : Vector3D.UnitY;
            var u = Vector3D.Normalize(Vector3D.Cross(axis, normal));
            var v = Vector3D.Cross(normal, u);
            var flat = new Vector2D[face.Length];
            for (int i = 0; i < face.Length; i++)
            {
                var p = points[face[i]] - origin;
                if (Math.Abs(Vector3D.Dot(p, normal)) > lengthTolerance)
                    throw new ArgumentException("Faces must be planar; triangulate nonplanar surfaces before submitting.");
                flat[i] = new Vector2D(Vector3D.Dot(p, u), Vector3D.Dot(p, v));
                GeometryWork.Charge(i);for (int j = 0; j < i; j++)
                    if ((flat[i] - flat[j]).LengthSquared() <= lengthTolerance * lengthTolerance)
                        throw new ArgumentException("Face contains coincident vertices.");
            }
            for (int i = 0; i < face.Length; i++)
            {
                int nextI = (i + 1) % face.Length;
                for (int j = i + 1; j < face.Length; j++)
                {
                    GeometryWork.Charge();int nextJ = (j + 1) % face.Length;
                    if (nextI == j || nextJ == i) continue;
                    if (Intersects(flat[i], flat[nextI], flat[j], flat[nextJ], areaTolerance, lengthTolerance))
                        throw new ArgumentException("Face boundaries must not intersect or touch themselves.");
                }
            }
            double area = 0;
            for (int i = 0; i < flat.Length; i++)
            { var a = flat[i]; var b = flat[(i + 1) % flat.Length]; area += a.X * b.Y - b.X * a.Y; }
            if (Math.Abs(area) <= areaTolerance) throw new ArgumentException("Face has zero area.");
            double sign = area > 0 ? 1 : -1;
            var remaining = new List<int>();
            for (int i = 0; i < face.Length; i++) remaining.Add(i);
            // Remove straight boundary vertices from triangulation only; preserve outline edges.
            bool removed = true;
            while (removed && remaining.Count > 3)
            {
                removed = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    GeometryWork.Charge();var a = flat[remaining[(i + remaining.Count - 1) % remaining.Count]];
                    var b = flat[remaining[i]];
                    var c = flat[remaining[(i + 1) % remaining.Count]];
                    if (Math.Abs(Cross(a, b, c)) <= areaTolerance)
                    {
                        if (!OnSegment(a, c, b, areaTolerance, lengthTolerance))
                            throw new ArgumentException("Face boundary doubles back on itself.");
                        remaining.RemoveAt(i); removed = true; break;
                    }
                }
            }
            while (remaining.Count > 3)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    GeometryWork.Charge();int a = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                    if (sign * Cross(flat[a], flat[b], flat[c]) <= areaTolerance) continue;
                    bool occupied = false;
                    foreach (int p in remaining)
                    {
                        GeometryWork.Charge();if (p == a || p == b || p == c) continue;
                        if (sign * Cross(flat[a], flat[b], flat[p]) >= -areaTolerance
                            && sign * Cross(flat[b], flat[c], flat[p]) >= -areaTolerance
                            && sign * Cross(flat[c], flat[a], flat[p]) >= -areaTolerance)
                        { occupied = true; break; }
                    }
                    if (occupied) continue;
                    triangles.Add(face[a]); triangles.Add(face[b]); triangles.Add(face[c]);
                    remaining.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) throw new ArgumentException("Face could not be triangulated.");
            }
            if (sign * Cross(flat[remaining[0]], flat[remaining[1]], flat[remaining[2]]) <= areaTolerance)
                throw new ArgumentException("Face produces a degenerate triangle.");
            foreach (int index in remaining) triangles.Add(face[index]);
        }
    }
}
