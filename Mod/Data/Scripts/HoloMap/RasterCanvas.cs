using System;
using VRageMath;

namespace HoloMap
{
    /// <summary>Bounded, ordered, two-dimensional RGBA rasterizer for untextured display meshes.</summary>
    public sealed class RasterCanvas
    {
        public const int MaxPixels = 1048576;
        public const long DefaultMaxWork = 2000000;
        public const long AbsoluteMaxWork = 8000000;
        readonly float[] _premultiplied;
        readonly double _scaleX, _scaleY;
        byte[] _finished;
        long _workUsed;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Samples { get; private set; }
        public long MaxWork { get; private set; }
        public long WorkUsed { get { return _workUsed; } }
        public long RetainedBytes { get { return (long)_premultiplied.Length * sizeof(float) + (_finished == null ? 0 : _finished.Length); } }

        public RasterCanvas(int width, int height, double canvasWidth, double canvasHeight, long maxWork = DefaultMaxWork, int samples = 4)
        {
            if (width < 16 || width > 2048 || height < 16 || height > 2048 || (long)width * height > MaxPixels)
                throw new ArgumentException("Raster dimensions must be 16–2048 and at most 1048576 pixels.");
            if (!Finite(canvasWidth) || !Finite(canvasHeight) || canvasWidth <= 0 || canvasHeight <= 0)
                throw new ArgumentException("Canvas dimensions must be finite and positive.");
            if (maxWork < 1 || maxWork > AbsoluteMaxWork) throw new ArgumentException("Raster work limit must be 1–8000000 samples.");
            if (samples != 1 && samples != 4) throw new ArgumentException("Raster sampling must be 1 or 4.");
            Width = width; Height = height; Samples = samples; MaxWork = maxWork;
            _scaleX = width / canvasWidth; _scaleY = height / canvasHeight;
            _premultiplied = new float[checked(width * height * samples * 4)];
        }

        static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        static void CheckColor(Vector4 c)
        {
            if (!Finite(c.X) || !Finite(c.Y) || !Finite(c.Z) || !Finite(c.W) ||
                c.X < 0 || c.X > 1 || c.Y < 0 || c.Y > 1 || c.Z < 0 || c.Z > 1 || c.W < 0 || c.W > 1)
                throw new ArgumentException("Raster colors require finite RGBA components in [0,1].");
        }
        static void CheckMatrix(MatrixD m)
        {
            if (!Finite(m.M11) || !Finite(m.M12) || !Finite(m.M13) || !Finite(m.M14) ||
                !Finite(m.M21) || !Finite(m.M22) || !Finite(m.M23) || !Finite(m.M24) ||
                !Finite(m.M31) || !Finite(m.M32) || !Finite(m.M33) || !Finite(m.M34) ||
                !Finite(m.M41) || !Finite(m.M42) || !Finite(m.M43) || !Finite(m.M44))
                throw new ArgumentException("Raster transform must be finite.");
        }
        static int Byte(float x)
        {
            return (int)Math.Max(0, Math.Min(255, Math.Floor(x * 255 + .5)));
        }
        public void Clear(Vector4 background)
        {
            CheckColor(background);
            float a = background.W;
            for (int i = 0; i < _premultiplied.Length; i += 4)
            {
                _premultiplied[i] = background.X * a;
                _premultiplied[i + 1] = background.Y * a;
                _premultiplied[i + 2] = background.Z * a;
                _premultiplied[i + 3] = a;
            }
            _workUsed = 0;
            _finished = null;
        }

        public void Add(SurfaceMesh mesh, MatrixD localTransform, Vector4 defaultFill, Vector4 defaultLine, float thickness, float opacity)
        { Add(mesh, localTransform, defaultFill, defaultLine, thickness, opacity, null, default(HologramEffectFrame)); }

        public void Add(SurfaceMesh mesh, MatrixD localTransform, Vector4 defaultFill, Vector4 defaultLine,
            float thickness, float opacity, HologramEffectSettings effects, HologramEffectFrame effectFrame)
        {
            if (mesh == null || mesh.Geometry == null) throw new ArgumentException("Raster mesh requires geometry.");
            if (mesh.UV != null) throw new ArgumentException("Textured UV meshes are unsupported by the CPU raster compositor.");
            CheckColor(defaultFill); CheckColor(defaultLine); CheckMatrix(localTransform);
            if (!Finite(thickness) || thickness < 0 || !Finite(opacity) || opacity < 0 || opacity > 1)
                throw new ArgumentException("Raster thickness and opacity must be finite and nonnegative; opacity is at most one.");
            var g = mesh.Geometry;
            if (g.Points == null || g.Edges == null || g.Triangles == null || g.Points.Length > Geometry.MaxPoints ||
                g.Triangles.Length % 3 != 0 || g.Edges.Length % 2 != 0 ||
                (long)g.Triangles.Length / 3 + g.Edges.Length / 2 > Geometry.MaxPrimitives ||
                mesh.Colors != null && mesh.Colors.Length != g.Triangles.Length / 3 ||
                mesh.EdgeColors != null && mesh.EdgeColors.Length != g.Edges.Length / 2)
                throw new ArgumentException("Raster mesh topology or color array is invalid.");
            if (mesh.Colors != null) for (int i = 0; i < mesh.Colors.Length; i++) CheckColor(mesh.Colors[i]);
            if (mesh.EdgeColors != null) for (int i = 0; i < mesh.EdgeColors.Length; i++) CheckColor(mesh.EdgeColors[i]);
            for (int i = 0; i < g.Triangles.Length; i++) CheckIndex(g.Triangles[i], g.Points.Length);
            for (int i = 0; i < g.Edges.Length; i++) CheckIndex(g.Edges[i], g.Points.Length);
            double[] coordinates = null;
            var effectSampler = default(HologramEffectSampler);
            if (effects != null)
            {
                // Validate the complete temporal sample before any work/pixel mutation.
                effectSampler = HologramEffectKernel.Prepare(effects, effectFrame);
                HologramRasterEffects.Color(effectSampler, Vector4.One, .5, 0);
                coordinates = HologramRasterEffects.Coordinates(g, effects);
            }

            // Transform into pixel coordinates once; never mutate Geometry.WorldPoints shared with renderers.
            var x = new double[g.Points.Length]; var y = new double[g.Points.Length];
            for (int i = 0; i < g.Points.Length; i++)
            {
                var p = g.Points[i];
                if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z)) throw new ArgumentException("Raster points must be finite.");
                var q = Vector3D.Transform(p, localTransform);
                if (!Finite(q.X) || !Finite(q.Y) || !Finite(q.Z)) throw new ArgumentException("Raster transformed points must be finite.");
                x[i] = q.X * _scaleX + Width * .5;
                y[i] = Height * .5 - q.Y * _scaleY;
                if (!Finite(x[i]) || !Finite(y[i])) throw new ArgumentException("Raster pixel positions must be finite.");
            }
            double radius = thickness * Math.Min(_scaleX, _scaleY) * .5;
            if (!Finite(radius)) throw new ArgumentException("Raster thickness is too large.");

            // Precharge the entire Add. A failure leaves both pixels and the work counter untouched.
            long charge = 0;
            for (int t = 0; t < g.Triangles.Length; t += 3)
            {
                int a = g.Triangles[t], b = g.Triangles[t + 1], c = g.Triangles[t + 2];
                var col = mesh.Colors == null ? defaultFill : mesh.Colors[t / 3];
                if (col.W == 0 || opacity == 0) continue;
                charge += TriangleWork(x[a],y[a],x[b],y[b],x[c],y[c],MaxWork-_workUsed-charge);
                if(charge>MaxWork-_workUsed)throw new ArgumentException("Raster frame exceeds its sample work limit.");
            }
            if (radius > 0) for (int e = 0; e < g.Edges.Length; e += 2)
            {
                int a = g.Edges[e], b = g.Edges[e + 1];
                var col = mesh.EdgeColors == null ? defaultLine : mesh.EdgeColors[e / 2];
                if (col.W == 0 || opacity == 0) continue;
                charge += BoxWork(Math.Min(x[a], x[b]) - radius, Math.Max(x[a], x[b]) + radius,
                    Math.Min(y[a], y[b]) - radius, Math.Max(y[a], y[b]) + radius);
                if(charge>MaxWork-_workUsed)throw new ArgumentException("Raster frame exceeds its sample work limit.");
            }
            if (charge > MaxWork - _workUsed) throw new ArgumentException("Raster frame exceeds its sample work limit.");
            _workUsed += charge;
            _finished = null;
            for (int t = 0; t < g.Triangles.Length; t += 3)
            {
                var color = mesh.Colors == null ? defaultFill : mesh.Colors[t / 3];
                if (color.W * opacity <= 0) continue;
                int a = g.Triangles[t], b = g.Triangles[t + 1], c = g.Triangles[t + 2];
                Triangle(x[a], y[a], x[b], y[b], x[c], y[c], color, opacity,
                    effects != null, effectSampler, coordinates == null ? 0 : coordinates[a],
                    coordinates == null ? 0 : coordinates[b], coordinates == null ? 0 : coordinates[c], t / 3);
            }
            if (radius > 0) for (int e = 0; e < g.Edges.Length; e += 2)
            {
                var color = mesh.EdgeColors == null ? defaultLine : mesh.EdgeColors[e / 2];
                if (color.W * opacity <= 0) continue;
                int a = g.Edges[e], b = g.Edges[e + 1];
                Line(x[a], y[a], x[b], y[b], radius, color, opacity,
                    effects != null, effectSampler, coordinates == null ? 0 : coordinates[a],
                    coordinates == null ? 0 : coordinates[b], g.Triangles.Length / 3 + e / 2);
            }
        }

        static void CheckIndex(int index, int count)
        {
            if (index < 0 || index >= count) throw new ArgumentException("Raster mesh index is out of range.");
        }
        long BoxWork(double lowX, double highX, double lowY, double highY)
        {
            int left, right, top, bottom;
            Box(lowX, highX, lowY, highY, out left, out right, out top, out bottom);
            return (long)(right - left) * (bottom - top) * Samples;
        }
        void Box(double lowX, double highX, double lowY, double highY, out int left, out int right, out int top, out int bottom)
        {
            left = (int)Math.Max(0, Math.Min(Width, Math.Floor(lowX)));
            right = (int)Math.Max(0, Math.Min(Width, Math.Ceiling(highX)));
            top = (int)Math.Max(0, Math.Min(Height, Math.Floor(lowY)));
            bottom = (int)Math.Max(0, Math.Min(Height, Math.Ceiling(highY)));
        }
        static void RowEdge(double ax,double ay,double bx,double by,double low,double high,ref double min,ref double max)
        {
            if(ay>=low&&ay<=high){min=Math.Min(min,ax);max=Math.Max(max,ax);}
            if(by>=low&&by<=high){min=Math.Min(min,bx);max=Math.Max(max,bx);}
            if(ay==by)return;
            double bottom=Math.Min(ay,by),top=Math.Max(ay,by);
            if(low>=bottom&&low<=top)
            {double x=ax+(bx-ax)*((low-ay)/(by-ay));min=Math.Min(min,x);max=Math.Max(max,x);}
            if(high>=bottom&&high<=top)
            {double x=ax+(bx-ax)*((high-ay)/(by-ay));min=Math.Min(min,x);max=Math.Max(max,x);}
        }
        void TriangleRow(double ax,double ay,double bx,double by,double cx,double cy,int row,out int left,out int right)
        {
            double low=row+(Samples==1?.5:.25)-1e-7,high=row+(Samples==1?.5:.75)+1e-7;
            double min=double.PositiveInfinity,max=double.NegativeInfinity;
            RowEdge(ax,ay,bx,by,low,high,ref min,ref max);
            RowEdge(bx,by,cx,cy,low,high,ref min,ref max);
            RowEdge(cx,cy,ax,ay,low,high,ref min,ref max);
            if(min>max){left=right=0;return;}
            if(!Finite(min)||!Finite(max)){left=0;right=Width;return;}
            // One pixel of margin covers edge-test epsilon and intersection rounding.
            left=(int)Math.Max(0,Math.Min(Width,Math.Floor(min)-1));
            right=(int)Math.Max(0,Math.Min(Width,Math.Ceiling(max)+1));
        }
        long TriangleWork(double ax,double ay,double bx,double by,double cx,double cy,long remaining)
        {
            double area=Cross(ax,ay,bx,by,cx,cy);
            if(!Finite(area))throw new ArgumentException("Raster triangle area is nonfinite.");
            if(Math.Abs(area)<1e-12)return 0;
            int left,right,top,bottom;
            Box(0,0,Math.Min(ay,Math.Min(by,cy)),Math.Max(ay,Math.Max(by,cy)),out left,out right,out top,out bottom);
            long work=0;
            for(int row=top;row<bottom;row++)
            {
                TriangleRow(ax,ay,bx,by,cx,cy,row,out left,out right);
                // Charge both row-bound calculations as well as covered sample candidates.
                work+=8+(long)(right-left)*Samples;
                if(work>remaining)return work;
            }
            return work;
        }
        static double Cross(double ax, double ay, double bx, double by, double px, double py)
        {
            return (bx - ax) * (py - ay) - (by - ay) * (px - ax);
        }
        static bool TopLeft(double ax, double ay, double bx, double by)
        {
            return by < ay || by == ay && bx > ax;
        }
        static bool Inside(double cross, bool topLeft, double epsilon)
        {
            return cross > epsilon || Math.Abs(cross) <= epsilon && topLeft;
        }
        void Triangle(double ax, double ay, double bx, double by, double cx, double cy, Vector4 color, float opacity,
            bool effects, HologramEffectSampler effectSampler, double coordinateA, double coordinateB, double coordinateC, int primitive)
        {
            double area = Cross(ax, ay, bx, by, cx, cy);
            if (Math.Abs(area) < 1e-12) return;
            if (area < 0)
            {
                double swap = bx; bx = cx; cx = swap; swap = by; by = cy; cy = swap;
                swap = coordinateB; coordinateB = coordinateC; coordinateC = swap;
                area = -area;
            }
            int left, right, top, bottom;
            Box(Math.Min(ax, Math.Min(bx, cx)), Math.Max(ax, Math.Max(bx, cx)),
                Math.Min(ay, Math.Min(by, cy)), Math.Max(ay, Math.Max(by, cy)), out left, out right, out top, out bottom);
            bool ab = TopLeft(ax, ay, bx, by), bc = TopLeft(bx, by, cx, cy), ca = TopLeft(cx, cy, ax, ay);
            double eps = 1e-10 * Math.Max(1, Math.Max(Math.Abs(area),
                Math.Max(Math.Abs(bx - ax) + Math.Abs(by - ay), Math.Abs(cx - bx) + Math.Abs(cy - by))));
            double abX=bx-ax,abY=by-ay,bcX=cx-bx,bcY=cy-by,caX=ax-cx,caY=ay-cy;
            double offset=Samples==1?.5:.25;
            for (int py = top; py < bottom; py++)
            {
                TriangleRow(ax,ay,bx,by,cx,cy,py,out left,out right);
                double eAB=Cross(ax,ay,bx,by,left+offset,py+offset);
                double eBC=Cross(bx,by,cx,cy,left+offset,py+offset);
                double eCA=Cross(cx,cy,ax,ay,left+offset,py+offset);
                for (int px = left; px < right; px++)
                {
                    for(int s=0;s<Samples;s++)
                    {
                        double sx=(s&1)*.5,sy=(s>>1)*.5;
                        double crossAB = eAB-abY*sx+abX*sy, crossBC = eBC-bcY*sx+bcX*sy, crossCA = eCA-caY*sx+caX*sy;
                        if(Inside(crossAB,ab,eps)&&Inside(crossBC,bc,eps)&&Inside(crossCA,ca,eps))
                        {
                            var sampled = !effects ? color : HologramRasterEffects.Color(effectSampler, color,
                                (crossBC*coordinateA+crossCA*coordinateB+crossAB*coordinateC)/area, primitive);
                            Over(px,py,s,sampled,opacity);
                        }
                    }
                    eAB-=abY;eBC-=bcY;eCA-=caY;
                }
            }
        }
        void Line(double ax, double ay, double bx, double by, double radius, Vector4 color, float opacity,
            bool effects, HologramEffectSampler effectSampler, double coordinateA, double coordinateB, int primitive)
        {
            int left, right, top, bottom;
            Box(Math.Min(ax, bx) - radius, Math.Max(ax, bx) + radius, Math.Min(ay, by) - radius,
                Math.Max(ay, by) + radius, out left, out right, out top, out bottom);
            double dx = bx - ax, dy = by - ay, length2 = dx * dx + dy * dy, radius2 = radius * radius;
            for (int py = top; py < bottom; py++) for (int px = left; px < right; px++) for (int s = 0; s < Samples; s++)
            {
                double sx = px + SampleX(s), sy = py + SampleY(s);
                double t = length2 < 1e-20 ? 0 : Math.Max(0, Math.Min(1, ((sx - ax) * dx + (sy - ay) * dy) / length2));
                double ex = sx - (ax + t * dx), ey = sy - (ay + t * dy);
                if (ex * ex + ey * ey <= radius2)
                {
                    var sampled = !effects ? color : HologramRasterEffects.Color(effectSampler, color,
                        coordinateA+(coordinateB-coordinateA)*t, primitive);
                    Over(px, py, s, sampled, opacity);
                }
            }
        }
        double SampleX(int s) { return Samples == 1 ? .5 : (s % 2 == 0 ? .25 : .75); }
        double SampleY(int s) { return Samples == 1 ? .5 : (s < 2 ? .25 : .75); }
        void Over(int px, int py, int sample, Vector4 color, float opacity)
        {
            int i = ((py * Width + px) * Samples + sample) * 4;
            float a = color.W * opacity, remainder = 1 - a;
            _premultiplied[i] = color.X * a + _premultiplied[i] * remainder;
            _premultiplied[i + 1] = color.Y * a + _premultiplied[i + 1] * remainder;
            _premultiplied[i + 2] = color.Z * a + _premultiplied[i + 2] * remainder;
            _premultiplied[i + 3] = a + _premultiplied[i + 3] * remainder;
        }
        public byte[] Finish()
        {
            if (_finished != null) return _finished;
            var result = new byte[Width * Height * 4];
            for (int p = 0; p < Width * Height; p++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int s = 0; s < Samples; s++)
                {
                    int src = (p * Samples + s) * 4;
                    r += _premultiplied[src]; g += _premultiplied[src + 1];
                    b += _premultiplied[src + 2]; a += _premultiplied[src + 3];
                }
                int dst = p * 4;
                if (a > 0)
                {
                    result[dst] = (byte)Byte(r / a); result[dst + 1] = (byte)Byte(g / a);
                    result[dst + 2] = (byte)Byte(b / a);
                }
                result[dst + 3] = (byte)Byte(a / Samples);
            }
            return _finished = result;
        }
    }
}
