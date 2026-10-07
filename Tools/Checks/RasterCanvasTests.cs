using System;
using HoloMap;
using VRageMath;

internal static class RasterCanvasTests
{
    static int _checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Raster canvas: " + message);
        _checks++;
    }
    static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, message);
    }
    static SurfaceMesh Mesh(Vector3D[] points, int[] triangles, Vector4[] colors = null, int[] edges = null, Vector4[] edgeColors = null)
    {
        return new SurfaceMesh { Geometry = new Geometry(points, edges ?? new int[0], triangles), Colors = colors, EdgeColors = edgeColors };
    }
    static readonly Vector4 Transparent = Vector4.Zero;
    static readonly Vector4 Red = new Vector4(1, 0, 0, 1);
    static readonly Vector4 Blue = new Vector4(0, 0, 1, 1);
    static readonly Vector4 Green = new Vector4(0, 1, 0, 1);
    static RasterCanvas Canvas(int size = 32, long work = RasterCanvas.DefaultMaxWork, int samples = 4)
    {
        var canvas = new RasterCanvas(size, size, 2, 2, work, samples);
        canvas.Clear(Transparent);
        return canvas;
    }
    static SurfaceMesh Quad(double lowX, double lowY, double highX, double highY, Vector4 color)
    {
        return Mesh(new[] { new Vector3D(lowX, lowY, 0), new Vector3D(highX, lowY, 0),
            new Vector3D(highX, highY, 0), new Vector3D(lowX, highY, 0) },
            new[] { 0, 1, 2, 0, 2, 3 }, new[] { color, color });
    }
    static void Add(RasterCanvas canvas, SurfaceMesh mesh, Vector4 fill, Vector4 line, float thickness = 0, float opacity = 1,
        MatrixD? transform = null)
    {
        canvas.Add(mesh, transform ?? MatrixD.Identity, fill, line, thickness, opacity);
    }
    static byte[] Pixel(byte[] pixels, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return new[] { pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3] };
    }
    static void QuadSeamAndAlpha()
    {
        var c = Canvas(16);
        Add(c, Quad(-1, -1, 1, 1, new Vector4(1, 0, 0, .5f)), Transparent, Transparent);
        var image = c.Finish();
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
        {
            var p = Pixel(image, 16, x, y);
            Check(p[0] == 255 && p[1] == 0 && p[2] == 0 && p[3] == 128,
                "translucent quad has no shared-diagonal seam");
        }
        Check(object.ReferenceEquals(image, c.Finish()), "Finish caches its bounded result");
    }
    static void LayersAndColors()
    {
        var c = Canvas(16);
        var two = Mesh(new[] { new Vector3D(-1, -1, 0), new Vector3D(0, -1, 0), new Vector3D(0, 1, 0),
            new Vector3D(-1, 1, 0), new Vector3D(1, -1, 0), new Vector3D(1, 1, 0) },
            new[] { 0, 1, 2, 0, 2, 3, 1, 4, 5, 1, 5, 2 }, new[] { Red, Red, Blue, Blue });
        Add(c, two, Green, Transparent);
        var p = c.Finish();
        Check(Pixel(p, 16, 2, 8)[0] == 255, "first triangle color");
        Check(Pixel(p, 16, 13, 8)[2] == 255, "later triangle color");
        Add(c, Quad(-.5, -.5, .5, .5, new Vector4(0, 1, 0, .5f)), Transparent, Transparent);
        p = c.Finish(); var center = Pixel(p, 16, 5, 8);
        Check(center[0] == 127 || center[0] == 128, "ordered source-over red channel");
        Check(center[1] == 127 || center[1] == 128, "ordered source-over green channel");
        Check(center[3] == 255, "ordered source-over alpha");
        c.Clear(new Vector4(0, 0, 1, .25f));
        p = c.Finish(); var bg = Pixel(p, 16, 8, 8);
        Check(bg[2] == 255 && bg[3] == 64, "clear resets all samples to straight-alpha background");
        Check(c.WorkUsed == 0, "clear resets frame work");
    }
    static void HolesAndTransform()
    {
        var c = Canvas();
        Add(c, Quad(-.8, .2, .8, .8, Red), Transparent, Transparent);
        Add(c, Quad(-.8, -.8, .8, -.2, Red), Transparent, Transparent);
        Add(c, Quad(-.8, -.2, -.2, .2, Red), Transparent, Transparent);
        Add(c, Quad(.2, -.2, .8, .2, Red), Transparent, Transparent);
        var p = c.Finish();
        Check(Pixel(p, 32, 16, 16)[3] == 0, "unpainted center of ring stays transparent");
        Check(Pixel(p, 32, 16, 5)[3] == 255, "ring itself is painted");
        c.Clear(Transparent);
        Add(c, Quad(-.25, -.25, .25, .25, Red), Transparent, Transparent, transform: MatrixD.CreateTranslation(.5, 0, 0));
        p = c.Finish();
        Check(Pixel(p, 32, 24, 16)[0] == 255, "local affine translation moves mesh");
        Check(Pixel(p, 32, 8, 16)[3] == 0, "translation leaves old location clear");
        c.Clear(Transparent);
        Add(c, Quad(-2, -2, 0, 2, Blue), Transparent, Transparent);
        p = c.Finish();
        Check(Pixel(p, 32, 1, 16)[2] == 255 && Pixel(p, 32, 29, 16)[3] == 0,
            "off-canvas geometry clips without wrapping");
    }
    static void StrokesAndFallback()
    {
        var c = Canvas();
        var line = Mesh(new[] { new Vector3D(-.75, 0, 0), new Vector3D(.75, 0, 0) }, new int[0],
            edges: new[] { 0, 1 }, edgeColors: new[] { Blue });
        Add(c, line, Transparent, Red, .125f);
        var p = c.Finish();
        Check(Pixel(p, 32, 16, 16)[2] == 255, "edge color wins over default line color");
        Check(Pixel(p, 32, 16, 11)[3] == 0, "line does not infer a filled face");
        c.Clear(Transparent);
        Add(c, Mesh(new[] { new Vector3D(-.75, 0, 0), new Vector3D(.75, 0, 0) }, new int[0], edges: new[] { 0, 1 }),
            Transparent, Green, .125f);
        Check(Pixel(c.Finish(), 32, 16, 16)[1] == 255, "null edge colors use fallback");
        c.Clear(Transparent);
        Add(c, Quad(-.5, -.5, .5, .5, Red), Transparent, Transparent, opacity: .5f);
        Check(Pixel(c.Finish(), 32, 16, 16)[3] == 128, "opacity scales colored fill alpha");
        c.Clear(Transparent);
        var fallback = Mesh(new[] { new Vector3D(-.5, -.5, 0), new Vector3D(.5, -.5, 0),
            new Vector3D(0, .5, 0) }, new[] { 0, 1, 2 });
        Add(c, fallback, Green, Transparent);
        Check(Pixel(c.Finish(), 32, 16, 16)[1] == 255, "null triangle colors use fill fallback");
        c.Clear(Transparent);
        Add(c, Quad(-.5, -.5, .03125, .5, Red), Transparent, Transparent);
        var antialias = Pixel(c.Finish(), 32, 16, 16);
        Check(antialias[0] == 255 && antialias[3] > 0 && antialias[3] < 255,
            "four-sample edge resolves partial alpha without dimming straight RGB");
        c.Clear(Transparent);
        Add(c, Quad(-.5, -.5, .5, .5, Red), Transparent, Transparent, opacity: .5f,
            transform: MatrixD.CreateTranslation(.25, 0, 0));
        Check(Pixel(c.Finish(), 32, 10, 16)[3] == 0, "transformed translucent fill preserves clear area");
        var single = Canvas(16, samples: 1);
        Add(single, Quad(-1, -1, 1, 1, Red), Transparent, Transparent);
        Check(Pixel(single.Finish(), 16, 8, 8)[3] == 255, "single-sample mode");
    }
    static void BoundsAndInvalidInput()
    {
        Reject(() => new RasterCanvas(2048, 513, 2, 2), "pixel capacity bound");
        Reject(() => new RasterCanvas(2049, 16, 2, 2), "side capacity bound");
        Reject(() => new RasterCanvas(15, 16, 2, 2), "minimum dimension bound");
        Reject(() => new RasterCanvas(16, 16, 0, 2), "positive canvas dimensions");
        Reject(() => new RasterCanvas(16, 16, 2, 2, RasterCanvas.AbsoluteMaxWork + 1), "absolute work bound");
        Reject(() => new RasterCanvas(16, 16, 2, 2, samples: 2), "sample count bound");
        var c = Canvas(16, 32);
        var initial = c.Finish();
        Reject(() => Add(c, Quad(-1, -1, 1, 1, Red), Transparent, Transparent), "sample work bound");
        Check(c.WorkUsed == 0 && object.ReferenceEquals(initial, c.Finish()), "work failure is atomic");
        var bad = Quad(-.5, -.5, .5, .5, Red);
        bad.UV = new Vector2[4];
        Reject(() => Add(c, bad, Transparent, Transparent), "UV capability error");
        bad.UV = null; bad.Colors = new Vector4[1];
        Reject(() => Add(c, bad, Transparent, Transparent), "mismatched colors rejected");
        var invalidIndex = Mesh(new[] { Vector3D.Zero }, new[] { 0, 1, 0 });
        Reject(() => Add(c, invalidIndex, Red, Transparent), "out-of-range index rejected");
        Reject(() => c.Clear(new Vector4(float.NaN, 0, 0, 1)), "nonfinite clear rejected");
        Reject(() => Add(c, Quad(0, 0, .1, .1, Red), Transparent, Transparent, opacity: float.NaN), "nonfinite opacity rejected");
        var degenerate = Mesh(new[] { Vector3D.Zero, Vector3D.Zero, Vector3D.Zero }, new[] { 0, 1, 2 });
        Add(c, degenerate, Red, Transparent);
        Check(Pixel(c.Finish(), 16, 8, 8)[3] == 0, "degenerate triangle draws no pixels");
        var max = new RasterCanvas(1024, 1024, 2, 2, RasterCanvas.AbsoluteMaxWork);
        Check(max.RetainedBytes <= 1024L * 1024 * 4 * 4 * 4, "maximum frame allocation bounded");
        max.Clear(Transparent);
        Add(max,Quad(-1,-1,1,1,Red),Transparent,Transparent,opacity:.5f);
        var largePixels=max.Finish();
        Check(largePixels.Length==1024*1024*4&&Pixel(largePixels,1024,511,511)[3]==128&&
            Pixel(largePixels,1024,512,512)[3]==128,"one-megapixel fills preserve shared-edge alpha");
    }
    static void TriangleSpanCoverage()
    {
        var random=new Random(7102);
        for(int example=0;example<24;example++)
        {
            var points=new Vector3D[3];
            for(int i=0;i<3;i++)points[i]=new Vector3D(random.NextDouble()*6-3,random.NextDouble()*6-3,0);
            var canvas=Canvas();Add(canvas,Mesh(points,new[]{0,1,2}),Red,Transparent);
            var pixels=canvas.Finish();bool correct=true;
            double Cross(Vector3D a,Vector3D b,double x,double y)=>(b.X-a.X)*(y-a.Y)-(b.Y-a.Y)*(x-a.X);
            double orientation=Cross(points[0],points[1],points[2].X,points[2].Y);
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                int coverage=0;
                for(int sample=0;sample<4;sample++)
                {
                    double sx=(x+((sample&1)==0?.25:.75))/16-1;
                    double sy=1-(y+(sample<2?.25:.75))/16;
                    if(Cross(points[0],points[1],sx,sy)*orientation>0&&
                        Cross(points[1],points[2],sx,sy)*orientation>0&&
                        Cross(points[2],points[0],sx,sy)*orientation>0)coverage++;
                }
                correct&=pixels[(y*32+x)*4+3]==(byte)Math.Floor(coverage*255d/4+.5);
            }
            Check(correct,"row clipping matches brute-force coverage with arbitrary winding and off-canvas vertices "+example);
        }
    }
    static void HighResolutionDemo(string root)
    {
        string code=System.IO.File.ReadAllText(System.IO.Path.Combine(root,"Examples","RasterSurfaceDemo.cs"));
        var match=System.Text.RegularExpressions.Regex.Match(code,"string svg=\"([^\"]+)\";");
        Check(match.Success,"real raster demo SVG is available for its composition budget check");
        var size=DisplayLod.MaximumVisible(new Vector2I(2048,1024)).Resolution;
        var timer=System.Diagnostics.Stopwatch.StartNew();
        var canvas=new RasterCanvas(size.X,size.Y,4,2,RasterCanvas.AbsoluteMaxWork,4);
        canvas.Clear(Transparent);
        var svg=Svg.Parse(match.Groups[1].Value,24);
        canvas.Add(new SurfaceMesh{Geometry=svg.Geometry,Colors=svg.Colors},MatrixD.CreateScale(.01),Transparent,Transparent,0,1);
        foreach(var glyph in VectorFont.Layout("HDR / raster requested"))
            canvas.Add(new SurfaceMesh{Geometry=glyph.Mesh},MatrixD.CreateScale(.16)*
                MatrixD.CreateTranslation(new Vector3D(0,.65,0)+glyph.Offset*.16),new Vector4(0,1,1,1),Transparent,0,1);
        var points=new Vector3D[192];var edges=new int[384];
        for(int i=0;i<points.Length;i++)
        {
            double angle=2*Math.PI*i/points.Length;
            points[i]=new Vector3D(.38*Math.Cos(angle),.05+.38*Math.Sin(angle),0);
            edges[i*2]=i;edges[i*2+1]=(i+1)%points.Length;
        }
        canvas.Add(new SurfaceMesh{Geometry=new Geometry(points,edges,new int[0])},MatrixD.CreateScale(1.1),
            Transparent,new Vector4(0,1,1,1),.015f,1);
        Check(canvas.Finish().Length==size.X*size.Y*4&&canvas.WorkUsed<=RasterCanvas.AbsoluteMaxWork,
            "complete high-resolution demo including animation's largest circle fits its composition budget");
        Console.WriteLine("Raster demo: "+size.X+"x"+size.Y+", "+canvas.WorkUsed+" bounded samples, "+timer.ElapsedMilliseconds+" ms offline composition.");
    }
    public static int Run(string root)
    {
        _checks = 0;
        QuadSeamAndAlpha(); LayersAndColors(); HolesAndTransform(); StrokesAndFallback(); BoundsAndInvalidInput();
        TriangleSpanCoverage();HighResolutionDemo(root);
        return _checks;
    }
}
