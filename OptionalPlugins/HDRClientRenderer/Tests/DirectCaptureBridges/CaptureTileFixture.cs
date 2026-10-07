using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VRageMath;

static class CaptureTileFixture
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static Type Engine(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null);
    static void Check(bool ok, string message) { if (!ok) throw new Exception("Capture tiles: " + message); checks++; }
    static object New(Type type, params object[] args) => Activator.CreateInstance(type, Instance, null, args, null);
    static object Read(object owner, string field) => owner.GetType().GetField(field, Instance).GetValue(owner);
    static T Read<T>(object owner, string field) => (T)Read(owner, field);
    static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Instance).SetValue(owner, value);
    static object Call(Type type, object owner, string method, params object[] args) => type.GetMethod(method, owner == null ? Static : Instance).Invoke(owner, args);
    static double[] Density(params double[] quadrants)
    {
        var packed = new double[256 * 7];
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
        {
            int at = (y * 16 + x) * 7; double value = quadrants[(y / 8) * 2 + x / 8];
            if (value < 0) continue;
            packed[at] = packed[at + 6] = 1; packed[at + 1] = packed[at + 3] = value;
            packed[at + 2] = packed[at + 4] = value * value; packed[at + 5] = 1;
        }
        return packed;
    }
    static object TypedDensity(Assembly plugin, double[] cells, bool known = true)
    { return Call(plugin.GetType("HDRClientRenderer.PanoramaDensity", true), null, "FromPacked", cells, 1024, known); }
    static object Plan(Assembly plugin, object density, int maximum = 1024)
    { return Call(plugin.GetType("HDRClientRenderer.CaptureTileAtlas", true), null, "Plan", density, maximum); }
    static Array Tiles(object atlas) => (Array)atlas.GetType().GetProperty("Tiles", Instance).GetValue(atlas);

    internal static void Run(Assembly plugin)
    {
        var atlasType = plugin.GetType("HDRClientRenderer.CaptureTileAtlas", true);
        var adaptive = Plan(plugin, TypedDensity(plugin, Density(1024, 200, 100, 64)));
        var tiles = Tiles(adaptive);
        // Three large quadrants plus one smaller quadrant cannot fit in a
        // single narrow 1024-high shelf. The former saturated height admitted it.
        var hostile=Plan(plugin,TypedDensity(plugin,Density(1024,1024,1024,512)));
        foreach(object tile in Tiles(hostile))
            Check(Read<int>(tile,"X")+Read<int>(tile,"Size")<=Read<int>(hostile,"Width")&&Read<int>(tile,"Y")+Read<int>(tile,"Size")<=Read<int>(hostile,"Height"),
                "Unequal near-maximum quadrants reject overflowing shelves rather than clamping their height.");
        Check(tiles.Length == 4 && Read<bool>(adaptive, "Adaptive"), "Fresh local density produces four real unequal native crop regions.");
        Check(tiles.Cast<object>().Select(t => Read<int>(t, "Size")).SequenceEqual(new[] { 512, 128, 64, 64 }), "Conservative quadrant maxima control physical raster sizes.");
        int pixels = 0;
        foreach (object tile in tiles)
        {
            int size = Read<int>(tile, "Size"), x = Read<int>(tile, "X"), y = Read<int>(tile, "Y");
            pixels += size * size;
            Check(x >= 0 && y >= 0 && x + size <= Read<int>(adaptive, "Width") && y + size <= Read<int>(adaptive, "Height"), "Packed tile stays inside its generated atlas.");
            Check(Read<double>(tile, "AtlasMaxU") - Read<double>(tile, "AtlasMinU") == (double)size / Read<int>(adaptive, "Width"), "Atlas coordinates use exact pixel edges.");
        }
        Check(pixels == Read<int>(adaptive, "RasterPixels") && pixels < 1024 * 1024, "Primary occupancy is the sum of actual nonuniform crop rasters.");
        for (int i = 0; i < tiles.Length; i++) for (int j = i + 1; j < tiles.Length; j++)
        {
            object a = tiles.GetValue(i), b = tiles.GetValue(j);
            Check(Read<int>(a, "X") + Read<int>(a, "Size") <= Read<int>(b, "X") || Read<int>(b, "X") + Read<int>(b, "Size") <= Read<int>(a, "X") ||
                Read<int>(a, "Y") + Read<int>(a, "Size") <= Read<int>(b, "Y") || Read<int>(b, "Y") + Read<int>(b, "Size") <= Read<int>(a, "Y"), "Adaptive physical tiles never overlap.");
        }
        var sparse = Plan(plugin, TypedDensity(plugin, Density(1024, -1, -1, -1)));
        Check(Tiles(sparse).Length == 1 && Read<bool>(sparse, "Adaptive"), "Proved unneeded quadrants are omitted, not rendered black.");
        var uniform = Plan(plugin, TypedDensity(plugin, Density(200, 200, 200, 200)));
        Check(!Read<bool>(uniform, "Adaptive") && Tiles(uniform).Length == 1 && Read<int>(uniform, "Width") == 256, "Uniform density uses one physically reduced full-view raster.");
        foreach (object unknown in new[] { TypedDensity(plugin, Density(1024, 0, 0, 0), false), Call(plugin.GetType("HDRClientRenderer.PanoramaDensity", true), null, "Full", 1024) })
            Check(!Read<bool>(Plan(plugin, unknown), "Adaptive") && Read<int>(Plan(plugin, unknown), "Width") == 1024, "Unknown/stale fields conservatively use one full-view capture.");
        var malformed = Density(1024, 200, 100, 64); malformed[1] = double.NaN;
        Check(!Read<bool>(Plan(plugin, TypedDensity(plugin, malformed)), "Adaptive"), "Malformed field fails to uniform fallback.");
        Check((int)Call(atlasType, null, "FitPixelLimit", 1024, 640 * 480) == 512, "Oversized raster physically fits the strict primary viewport budget.");
        NativeProjection(plugin, adaptive);
        Refinement(plugin);
        Profiles(plugin);
        CopyRegions(plugin, adaptive);
        AtomicService(plugin, adaptive);
        Console.WriteLine("PASS: " + checks + " adaptive native capture assertions; local density/raster occupancy, cropped native projection/culling, scoped profiles, same-pose atomic atlas generations and resource reuse; no GPU frame.");
    }
    static void Refinement(Assembly plugin)
    {
        var cells=Density(64,64,64,64);
        // Four distributed hotspots have no immediate root split benefit.
        // Lookahead must discover savings deeper in each quadrant.
        foreach(var point in new[]{(1,1),(14,1),(1,14),(14,14)})
        {int at=(point.Item2*16+point.Item1)*7;cells[at+1]=cells[at+3]=1024;cells[at+2]=cells[at+4]=1024*1024;}
        object density=TypedDensity(plugin,cells);
        var type=plugin.GetType("HDRClientRenderer.CaptureTileAtlas",true);
        var fine=Call(type,null,"PlanAdaptive",density,1024,64,0d);var tiles=Tiles(fine);
        Check(tiles.Length>4&&tiles.Length<=64,"distributed density peaks automatically refine beyond quadrants despite zero immediate split savings");
        Check(Read<int>(fine,"RasterPixels")<1024*1024,"fine density map physically rasterizes fewer pixels than a full image");
        var expensive=Call(type,null,"PlanAdaptive",density,1024,64,1e9);
        Check(Tiles(expensive).Length==1,"high scene-pass penalty coalesces the tree to a uniform view");
        var limited=Call(type,null,"PlanAdaptive",density,1024,4,0d);
        Check(Tiles(limited).Length<=4,"local tile limit constrains refinement");
        foreach(object tile in tiles)
        {
            int size=Read<int>(tile,"Size"),x=Read<int>(tile,"X"),y=Read<int>(tile,"Y");
            Check(x>=0&&y>=0&&x+size<=Read<int>(fine,"Width")&&y+size<=Read<int>(fine,"Height"),"mixed-depth leaves pack inside the allocated atlas");
        }
        for(int y=0;y<16;y++)for(int x=0;x<16;x++)
        {
            double u=(x+.5)/16,v=(y+.5)/16;int owners=0;
            foreach(object tile in tiles)if(u>=Read<double>(tile,"SourceMinU")&&u<Read<double>(tile,"SourceMaxU")&&v>=Read<double>(tile,"SourceMinV")&&v<Read<double>(tile,"SourceMaxV"))
            {owners++;Check(Read<int>(tile,"Size")>=(cells[(y*16+x)*7+1])*(Read<double>(tile,"SourceMaxU")-Read<double>(tile,"SourceMinU")),"every covered cell retains its conservative density bound");}
            Check(owners==1,"mixed-depth leaves cover each source cell exactly once");
        }
        NativeProjection(plugin,fine);
        var timer=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<200;i++)Call(type,null,"PlanAdaptive",density,1024,64,0d);
        timer.Stop();Console.WriteLine("BENCH: 200 adaptive tree plans (reflection included) took "+timer.Elapsed.TotalMilliseconds.ToString("F1")+"ms; "+tiles.Length+" tiles / "+Read<int>(fine,"RasterPixels")+" raster pixels. GPU performance not measured.");
    }
    static void NativeProjection(Assembly plugin, object atlas)
    {
        var renderer = Engine("VRageRender.MyRender11");
        var nativeType = plugin.GetType("HDRClientRenderer.DirectCameraCaptureNative", true);
        object adapter = New(nativeType, renderer.Assembly);
        object environment = renderer.GetField("Environment", Static).GetValue(null);
        object matrices = environment.GetType().GetField("Matrices", Instance).GetValue(environment);
        Set(matrices, "OriginalProjection", Matrix.CreatePerspectiveFieldOfView(.8f, 1.7f, .1f, 3000));
        Set(matrices, "OriginalProjectionFar", Matrix.CreatePerspectiveFieldOfView(.8f, 1.7f, 100, 10000000));
        Set(matrices, "NearClipping", .1f); Set(matrices, "FarClipping", 3000f); Set(matrices, "LargeDistanceFarClipping", 1500000f);
        var viewport = renderer.GetProperty("ViewportResolution", Static); object originalViewport = viewport.GetValue(null);
        var resolution = renderer.GetField("m_resolution", Static); object originalResolution = resolution.GetValue(null);
        var snapshotType = nativeType.GetNestedType("CameraMatricesSnapshot", BindingFlags.NonPublic);
        object snapshot = New(snapshotType, renderer.GetField("Environment", Static), environment.GetType().GetField("Matrices", Instance));
        var setup = (MethodInfo)nativeType.GetField("setup", Instance).GetValue(adapter);
        // The actual native setup only creates CPU scene owners, never a device.
        RuntimeHelpers.RunClassConstructor(Engine("VRage.Render11.Scene.MyScene11").TypeHandle);
        try
        {
            viewport.SetValue(null, new Vector2I(512, 512)); resolution.SetValue(null, new Vector2I(512, 512));
            foreach (object tile in Tiles(atlas))
            {
                object message = Call(nativeType, adapter, "CameraMessage", MatrixD.Identity, 105d, matrices, tile, null);
                setup.Invoke(null, new[] { message, matrices, Enum.ToObject(setup.GetParameters()[2].ParameterType, 0) });
                Check(Read<float>(matrices, "FovH") > 0 && Read<float>(matrices, "FovV") > 0, "Actual native off-axis setup retains valid FOV/LOD metadata.");
                var draw = Read<Matrix>(matrices, "Projection"); var sky = Read<Matrix>(matrices, "ProjectionForSkybox");
                var clipped = Read<Matrix>(matrices, "OriginalProjection"); var distant = Read<Matrix>(matrices, "OriginalProjectionFar");
                double minU = Read<double>(tile, "SourceMinU"), minV = Read<double>(tile, "SourceMinV"), maxU = Read<double>(tile, "SourceMaxU"), maxV = Read<double>(tile, "SourceMaxV");
                foreach (var uv in new[] { (minU, minV), (maxU, minV), (minU, maxV), (maxU, maxV), ((minU + maxU) * .5, (minV + maxV) * .5) })
                {
                    double tangent = Math.Tan(105 * Math.PI / 360); var ray = new Vector4((float)((uv.Item1 * 2 - 1) * tangent), (float)((1 - uv.Item2 * 2) * tangent), -1, 0);
                    double expectedX = 2 * (uv.Item1 - minU) / (maxU - minU) - 1, expectedY = 1 - 2 * (uv.Item2 - minV) / (maxV - minV);
                    foreach (var projection in new[] { draw, sky, clipped, distant })
                    { var point = Vector4.Transform(ray, projection); Check(Math.Abs(point.X / point.W - expectedX) < 2e-6 && Math.Abs(point.Y / point.W - expectedY) < 2e-6, "Native draw/skybox/near/far culling projection applies each physical crop offset exactly once."); }
                }
            }
        }
        finally { snapshotType.GetMethod("Restore", Instance).Invoke(snapshot, null); viewport.SetValue(null, originalViewport); resolution.SetValue(null, originalResolution); }
    }
    static void Profiles(Assembly plugin)
    {
        var native = plugin.GetType("HDRClientRenderer.DirectCameraCaptureNative", true);
        object defaults = Activator.CreateInstance(Engine("VRageRender.Messages.MyRenderDebugOverrides"), true);
        var fields = defaults.GetType().GetFields(Instance).Where(f => f.FieldType == typeof(bool)).ToArray();
        foreach (var field in fields) field.SetValue(defaults, true);
        object normal = Call(native, null, "CaptureOverrides", defaults, 0), lite = Call(native, null, "CaptureOverrides", defaults, 1);
        var reduced = new HashSet<string>(new[] { "Fog", "SSAO", "Shadows", "Foliage", "Bloom", "Flares" });
        foreach (var field in fields)
        { Check((bool)field.GetValue(defaults) && (bool)field.GetValue(normal), "Normal and main renderer flags retain their defaults.");
            Check((bool)field.GetValue(lite) == !reduced.Contains(field.Name), "Lite disables only the six explicitly opted-in effects; particles/billboards remain."); }
    }
    static void AtomicService(Assembly plugin, object atlas) { CaptureTileServiceFixture.Run(plugin, atlas, Check); }
    static object copyContext;
    static int expectedSize, expectedX, expectedY, copied;
    static bool CopyScope(Action<object, object> __0) { __0(null, copyContext); return false; }
    static bool CopyIntercept(object __0, int __1, object __2, object __3, int __4, int __5, int __6, int __7)
    {
        Check(__0 != null && __3 != null && __1 == 0 && __4 == 0 && __5 == expectedX && __6 == expectedY && __7 == 0,
            "Actual native copy call uses trusted owned source/destination, mip0 and bounded destination offset.");
        var type = __2.GetType();
        Check((int)type.GetField("Left").GetValue(__2) == 0 && (int)type.GetField("Top").GetValue(__2) == 0 &&
            (int)type.GetField("Right").GetValue(__2) == expectedSize && (int)type.GetField("Bottom").GetValue(__2) == expectedSize &&
            (int)type.GetField("Front").GetValue(__2) == 0 && (int)type.GetField("Back").GetValue(__2) == 1,
            "Actual CopySubresourceRegion nullable ABI carries the top-left logical crop, never full oversized scratch.");
        copied++; return false;
    }
    static void CopyRegions(Assembly plugin, object atlas)
    {
        var nativeType = plugin.GetType("HDRClientRenderer.DirectCameraCaptureNative", true);
        object native = New(nativeType, Engine("VRageRender.MyRender11").Assembly);
        var h = new Harmony("HDR.Tests.CroppedNativeCopyABI");
        try
        {
            h.Patch(nativeType.GetMethod("WithCopyState", Instance), prefix: new HarmonyMethod(typeof(CaptureTileFixture).GetMethod(nameof(CopyScope), Static)));
            h.Patch((MethodInfo)nativeType.GetField("copyRegion", Instance).GetValue(native), prefix: new HarmonyMethod(typeof(CaptureTileFixture).GetMethod(nameof(CopyIntercept), Static)));
            nativeType.GetField("targetSize", Instance).SetValue(native, typeof(CpuTexture).GetProperty(nameof(CpuTexture.Size)));
            nativeType.GetField("targetResource", Instance).SetValue(native, typeof(CpuTexture).GetProperty(nameof(CpuTexture.Resource)));
            copyContext = RuntimeHelpers.GetUninitializedObject(Engine("SharpDX.Direct3D11.DeviceContext1"));
            object texture = RuntimeHelpers.GetUninitializedObject(Engine("SharpDX.Direct3D11.Texture2D"));
            var source = new CpuTexture(new Vector2I(512, 512), texture);
            var destination = new CpuTexture(new Vector2I(Read<int>(atlas, "Width"), Read<int>(atlas, "Height")), texture);
            copied = 0;
            var tiles = Tiles(atlas);
            foreach (int index in new[] { 0, 2, 0 })
            {
                object tile = tiles.GetValue(index); expectedSize = Read<int>(tile, "Size"); expectedX = Read<int>(tile, "X"); expectedY = Read<int>(tile, "Y");
                Check((bool)Call(nativeType, null, "ScratchFits", expectedSize, 512, 512), "Physical scratch remains valid for alternating512→64→512 logical captures.");
                Call(nativeType, native, "CopyTile", source, destination, tile);
            }
            Check(copied == 3 && !(bool)Call(nativeType, null, "ScratchFits", 512, 64, 64), "Oversized logical captures fail validation while smaller logical crops reuse the physical target.");
        }
        finally { copyContext = null; h.UnpatchAll(h.Id); }
    }
    sealed class CpuTexture
    {
        public Vector2I Size { get; }
        public object Resource { get; }
        internal CpuTexture(Vector2I size, object resource) { Size = size; Resource = resource; }
    }
}
