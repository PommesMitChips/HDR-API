using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Hdr.Html;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;

// The accepted PB gateway is linked unchanged. This fixture supplies registered
// cameras and bounded native texture/evidence responses; it never draws pixels,
// uploads to a GPU, or starts a game. Core/frontend declarations remain real.
internal sealed class CompositionFixture : IDisposable
{
    internal readonly PbFixture Pb = new();
    internal readonly HtmlPbSession Html = new();
    internal readonly Dictionary<long, Action<object>> Handlers = new();
    internal readonly List<object[]> Requests = new();
    internal readonly List<object> Released = new();
    internal readonly Dictionary<string, object> Proofs = new();
    internal readonly Func<string, object[], object> TextureProvider;
    internal readonly Func<string, object[], object> VectorProvider;
    internal readonly Func<string, object[], object> Endpoint;
    internal Vector3D Eye = new(0, 0, 6);
    internal bool SourceLive = true, ThrowFrame, InvalidFrame;
    internal int SlotCalls, FailSlotAt, Tick;
    internal readonly List<(string Key, Vector2I Size, byte[] Rgba)> RasterUploads = new();
    internal readonly List<object> RasterReleased = new();
    readonly HashSet<object> rasterLeases = new();
    internal const double Units = .005;
    internal const double Width = 480, Height = 280;
    internal static readonly MatrixD CanvasPose = MatrixD.Identity;
    internal static object Field(object target, string name) => ClientReplicationTests.Field(target, name);
    internal static void Set(object target, string name, object value) => ClientReplicationTests.SetField(target, name, value);
    internal static object New(string name) => Activator.CreateInstance(typeof(HoloMapSession).GetNestedType(name, BindingFlags.NonPublic), true);
    internal static object Call(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    internal static object Static(string name, params object[] args)
    {
        var method = typeof(HoloMapSession).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException error) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    internal CompositionFixture(bool console = false)
    {
        if (console) Pb.TargetSubtype = "LargeConsole";
        var proxy = (DrawTestProxy)(object)Pb.Target; var oldHandler = proxy.Handler;
        proxy.Handler = (m, a) => m.Name == "get_WorldMatrix" ? MatrixD.Identity : oldHandler(m, a);
        var gateway = (ClientReplicationTests.GatewayScope)Field(Pb, "gateway");
        gateway.Install("Utilities", (m, a) =>
        {
            if (m.Name == "get_IsDedicated") return false;
            if (m.Name == "RegisterMessageHandler") { Handlers[(long)a[0]] = (Action<object>)a[1]; return null; }
            if (m.Name == "UnregisterMessageHandler") { Handlers.Remove((long)a[0]); return null; }
            if (m.Name == "SendModMessage" || m.Name == "ShowMessage") return null;
            if (m.Name == "SerializeToBinary") return gateway.Serialize(a[0]);
            throw new Exception("Composition utility: " + m.Name);
        });
        gateway.Install("Session", (m, a) => m.Name == "get_Camera" ? DrawTestProxy.Make(m.ReturnType, (cm, ca) => cm.Name switch
        {
            "get_Position" => Eye,
            "get_ViewMatrix" => MatrixD.CreateLookAt(Eye, Vector3D.Zero, Vector3D.Up),
            "get_ProjectionMatrix" => MatrixD.CreatePerspectiveFieldOfView(Math.PI / 3, 16d / 9, .05, 100),
            "get_ViewportSize" => new Vector2(1280, 720),
            _ => throw new Exception("Composition viewer: " + cm.Name)
        }) : throw new Exception("Composition session: " + m.Name));
        AddCamera(101); AddCamera(102);
        Pb.Property("HDR.Draw", (op, args) =>
        {
            if (op == "screen-slot" && ++SlotCalls == FailSlotAt) throw new ArgumentException("injected composition slot failure");
            return Call(Pb, "DispatchCoreDraw", op, args, false);
        });
        Pb.Property("HDR.UI", (op, args) => { Pb.UiCalls++; Pb.Commands.Add("ui:" + op); return Pb.CoreUi(op, args); });
        TextureProvider = (op, args) =>
        {
            if (op == "release") { Released.Add(args[0]); return true; }
            if (op == "valid") return SourceLive && Proofs.TryGetValue((string)args[2], out var proof) && ReferenceEquals(proof, args[3]);
            if (op != "frame") return true;
            Requests.Add((object[])args.Clone());
            if (ThrowFrame) throw new ArgumentException("injected camera frame failure");
            if (!SourceLive) return null;
            string source = (string)args[2];
            if ((source == "101" || source == "102") && !((IMyCameraBlock)Entities[long.Parse(source)]).IsWorking) return null;
            if (!Proofs.TryGetValue(source, out var evidence)) Proofs[source] = evidence = new object();
            return new MyTuple<int, object, object, bool, double>(2,
                new MyTuple<string, Vector2I, long, Vector4>("fixture_material_" + source.Replace(':', '_'), new Vector2I(256, 128), InvalidFrame ? 0 : 1, new Vector4(.125f, .25f, .75f, .5f)), evidence, false, 30);
        };
        VectorProvider = (op, args) =>
        {
            if (op == "valid") return SourceLive && Proofs.TryGetValue((string)args[2], out var proof) && ReferenceEquals(proof, args[3]);
            if (op != "frame") return true;
            Requests.Add((object[])args.Clone());
            string source = (string)args[2]; if (!Proofs.TryGetValue(source, out var evidence)) Proofs[source] = evidence = new object();
            double w = (double)args[6] / 2, h = (double)args[7] / 2;
            return new MyTuple<Vector3D[], int[], Vector4[], object, bool, double>(
                new[] { new Vector3D(-w, -h, 0), new Vector3D(w, -h, 0), new Vector3D(0, h, 0) },
                new[] { 0, 1, 2 }, new[] { new Vector4(0, 1, 0, .8f) }, evidence, false, 6);
        };
        Call(Pb.Core, "RegisterDisplaySources");
        Register("camera-panorama", TextureProvider); Register("fixture-texture", TextureProvider); Register("lcd-texture", TextureProvider);
        Handlers[481770101](new MyTuple<string, int, Func<string, object[], object>>("fixture-vector", 1, VectorProvider));
        Pb.CaptureHtmlProperty(); Html.AfterLoadData(); Html.BeforeStart(); Endpoint = Pb.HtmlEndpoint();
        // A floating ellipsoid explicitly uses the Console's public free-volume setting.
        if (console) { Pb.CoreDraw("target", new object[] { Pb.Target }); Pb.CoreDraw("table-volume", new object[] { false }); }
    }
    internal Dictionary<long, object> Entities => (Dictionary<long, object>)Field(Pb, "entities");
    internal void AddCamera(long id)
    {
        Entities[id] = DrawTestProxy.Make(typeof(IMyCameraBlock), (m, a) => m.Name switch
        {
            "get_EntityId" => id, "get_Closed" => false, "get_IsWorking" => SourceLive,
            "HasPlayerAccess" => true, "get_WorldMatrix" => MatrixD.CreateTranslation(id == 101 ? Vector3D.Zero : Vector3D.UnitX),
            _ => throw new Exception("Composition physical camera: " + m.Name)
        });
    }
    internal void Register(string name, Func<string, object[], object> provider) =>
        Handlers[481770101](new MyTuple<string, int, Func<string, object[], object>>(name, 2, provider));
    internal object Invoke(string op, params object[] args) => Endpoint(op, args);
    internal void Draw(string op, params object[] args) { Pb.CoreDraw("target", new object[] { Pb.Target }); Pb.CoreDraw("screen", new object[] { "surface" }); Pb.CoreDraw(op, args); }
    internal void CreateShape(string shape)
    {
        Pb.CoreDraw("target", new object[] { Pb.Target });
        Pb.CoreDraw("screen", new object[] { "surface", MatrixD.Identity, Width * Units, Height * Units, Width * Units, Height * Units });
        if (shape == "ellipsoid") Draw("screen-ellipsoid", new Vector3D(1.6, 1.2, 2), 1.2d, .8d, "outside");
        else if (shape == "mesh") Draw("screen-mesh", new[] { new Vector3D(-1.2, -.7, 0), new Vector3D(1.2, -.7, .2), new Vector3D(1.2, .7, .3), new Vector3D(-1.2, .7, 0) }, new[] { 0, 1, 2, 0, 2, 3 }, new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) }, "outside");
        else Draw("screen-surface", shape, 2d, 1.2d, .8d, "outside");
        Draw("screen-aspect", "stretch"); Draw("screen-quality", .08d);
    }
    internal long Bind(string markup, string css, MatrixD? pose = null) => (long)Invoke("bind-screen", Pb.Target, "surface", markup, css, Width, Height, pose ?? CanvasPose, Units);
    internal HtmlPbDocument Document(long handle) => (HtmlPbDocument)((IDictionary)Field(((IDictionary)Field(Html, "owners"))[10L], "Documents"))[handle];
    internal HoloProjectedScreenData Data => (HoloProjectedScreenData)Field(Pb.Screens["10:surface"], "Data");
    internal object Scene => ((IDictionary)Field(Pb.Core, "_scenes"))[20L];
    internal object Cache
    {
        get
        {
            var caches = (IDictionary)Field(Pb.Core, "_projectedCaches");
            if (!caches.Contains("20:10:surface")) { var cache = New("ProjectedCache"); Set(cache, "Anchor", 20L); Set(cache, "Caller", 10L); Set(cache, "Id", "surface"); Set(cache, "Front", true); caches["20:10:surface"] = cache; }
            return caches["20:10:surface"];
        }
    }
    internal void Advance(int ticks = 10) { Tick += ticks; Set(Pb.Core, "_ticks", Tick); }
    internal void Prepare()
    {
        Advance(); Set(Cache, "Data", Data); Set(Cache, "Seen", Tick);
        object[] args = { Scene, Data, Cache, 100 }; Call(Pb.Core, "PrepareSourceSlots", args);
    }
    internal object SlotCache(string id) => ((IDictionary)Field(Cache, "Slots"))[id];
    internal SurfaceMesh Map(string id, SurfaceMesh mesh, MatrixD? transform = null, Vector4? fill = null, Vector4? line = null)
    {
        Advance(); object[] args = { Scene, Cache, id, mesh, Data, transform ?? MatrixD.Identity, .0015d, 0d, true, 100, fill, line, false };
        return (SurfaceMesh)Call(Pb.Core, "MappedScreenGeometry", args);
    }
    internal bool RayHit(UiWidget widget)
    {
        object item = Pb.Items["10:" + widget.Control.Artwork];
        var canvas = Vector3D.Transform(new Vector3D(widget.X, widget.Y, 0), (MatrixD)Field(item, "Transform"));
        var style = (SurfaceStyle)Static("ScreenSurfaceStyle", Data, 0d);
        Vector3D point, normal;
        if (Data.SurfaceKind == 0) { point = canvas; normal = Vector3D.UnitZ; }
        else { point = SurfaceMapping.MapPoint(canvas, Data.CanvasWidth, Data.CanvasHeight, style); normal = SurfaceMapping.Normal(canvas, Data.CanvasWidth, Data.CanvasHeight, style); }
        object[] args = { Pb.Display, widget, point + normal * .5, -normal, Vector3D.Zero, 0L };
        return (bool)Call(Pb.Core, "TryUiControlHit", args);
    }
    internal void Update(int count = 1) { for (int i = 0; i < count; i++) Html.UpdateAfterSimulation(); }
    internal void EnableRasterBackend()
    {
        Call(Pb.Core, "RegisterRasterBackend");
        Func<string, object[], object> backend = (op, args) =>
        {
            if (op == "caps") return new MyTuple<int, int, int, int>(512, 512, 262144, 16);
            if (op == "valid") return rasterLeases.Contains(args[0]);
            if (op == "release") { RasterReleased.Add(args[0]); rasterLeases.Remove(args[0]); return true; }
            if (op == "upload")
            {
                var lease = new object(); rasterLeases.Add(lease);
                RasterUploads.Add(((string)args[0], new Vector2I((int)args[2], (int)args[3]), (byte[])args[4]));
                return new MyTuple<string, object>("fixture_ui_" + RasterUploads.Count, lease);
            }
            return false;
        };
        Handlers[481770111](new MyTuple<int, Func<string, object[], object>>(1, backend));
    }
    internal void DrawLegacyClipped()
    {
        // Game render metadata supplies only an ID. The public 1cm display volume
        // clips this ellipsoid at Z~2m before every native billboard submission.
        var render = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Sandbox.Game.Components.MyRenderComponentCubeBlock));
        typeof(VRage.Game.Components.MyRenderComponentBase).GetField("m_renderObjectIDs", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(render, new[] { uint.MaxValue });
        var proxy = (DrawTestProxy)(object)Pb.Target; var previous = proxy.Handler;
        proxy.Handler = (m, a) => m.Name == "get_Render" ? render : previous(m, a);
        Pb.CoreDraw("target", new object[] { Pb.Target }); Pb.CoreDraw("projection-range", new object[] { .01d });
        Advance(); Set(Pb.Core, "_volumeClipWork", 100000); Set(Cache, "NextSample", -1d);
        object[] draw = { Scene, Pb.Target, 10000, 100 };
        try { Call(Pb.Core, "DrawProjectedScreens", draw); }
        finally { proxy.Handler = previous; }
    }
    internal sealed class ImageDefinitionScope : IDisposable
    {
        readonly FieldInfo singleton = typeof(Sandbox.Definitions.MyDefinitionManager).BaseType.GetField("Static", BindingFlags.Static | BindingFlags.Public);
        readonly object previous;
        internal ImageDefinitionScope()
        {
            previous = singleton.GetValue(null);
            var managerType = typeof(Sandbox.Definitions.MyDefinitionManager);
            var manager = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(managerType);
            var setType = managerType.GetNestedType("DefinitionSet", BindingFlags.NonPublic);
            var set = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(setType);
            var definitions = setType.GetField("m_definitionsById", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var dictionary = (IDictionary)Activator.CreateInstance(definitions.FieldType, new object[] { 1 });
            var material = (Sandbox.Definitions.MyTransparentMaterialDefinition)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Sandbox.Definitions.MyTransparentMaterialDefinition));
            material.Id = new VRage.Game.MyDefinitionId(typeof(VRage.Game.MyObjectBuilder_TransparentMaterialDefinition), "HoloMap_Image_Fixture");
            dictionary.Add(material.Id, material); definitions.SetValue(set, dictionary);
            managerType.BaseType.GetField("m_definitions", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, set);
            singleton.SetValue(null, manager);
        }
        public void Dispose() { singleton.SetValue(null, previous); }
    }
    public void Dispose() { Html.UnloadDataConditional(); Call(Pb.Core, "UnregisterDisplaySources"); Call(Pb.Core, "UnregisterRasterBackend"); Pb.Dispose(); }
}
