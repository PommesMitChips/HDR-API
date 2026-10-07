using System.Collections;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

internal static class RasterSurfaceIntegrationTests
{
    static int checks;
    static void Check(bool ok, string clause) { if (!ok) throw new Exception("Raster surface integration: " + clause); checks++; }
    static void Reject(Action action, string clause)
    { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("Expected raster/surface rejection: " + clause); }
    static object Call(object target, string name, params object[] args) => ClientReplicationTests.Call(target, name, args);
    static object Field(object target, string name) => ClientReplicationTests.Field(target, name);
    static void Set(object target, string name, object value) => ClientReplicationTests.SetField(target, name, value);
    static object Nested(string name) => Activator.CreateInstance(typeof(HoloMapSession).GetNestedType(name, BindingFlags.NonPublic), true);
    static object Static(string name, params object[] args)
    {
        try { return typeof(HoloMapSession).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args); }
        catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    static readonly double[] Identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
    static HoloProjectedScreenData Screen() => new HoloProjectedScreenData { CallerId = 10, Id = "view", Pose = (double[])Identity.Clone(), SourceProvider = "test", SourceId = "feed", CanvasWidth = 2, CanvasHeight = 2 };

    sealed class DrawFixture : IDisposable
    {
        public readonly ClientReplicationTests.GatewayScope Gateway = new() { Server = true };
        public readonly HoloMapSession Session = new();
        readonly Dictionary<long, object> entities = new();
        public DrawFixture()
        {
            entities[10] = DrawTestProxy.Make(typeof(IMyProgrammableBlock), (m, a) => m.Name switch
            { "get_EntityId" => 10L, "get_OwnerId" => 77L, "get_Closed" => false, "IsSameConstructAs" => true, _ => throw new Exception("Raster caller " + m.Name) });
            entities[20] = DrawTestProxy.Make(typeof(IMyProjector), (m, a) => m.Name switch
            { "get_EntityId" => 20L, "get_Closed" => false, "get_IsWorking" => true, "HasPlayerAccess" => true,
              "get_BlockDefinition" => Activator.CreateInstance(m.ReturnType), _ => throw new Exception("Raster anchor " + m.Name) });
            Gateway.Install("Entities", (m, a) => m.Name == "GetEntityById" && entities.TryGetValue((long)a[0], out var entity) ? entity : null);
        }
        public Func<string, object[], object> Draw()
        {
            var endpoint = (Func<string, object[], object>)Call(Session, "DrawEndpoint", entities[10]);
            endpoint("target", new[] { entities[20] });
            endpoint("screen", new object[] { "view", MatrixD.Identity, 2d, 1d, 2d, 1d });
            return endpoint;
        }
        public HoloProjectedScreenData Data => (HoloProjectedScreenData)Field(((IDictionary)Field(((IDictionary)Field(Session, "_scenes"))[20L], "Screens"))["10:view"], "Data");
        public void Dispose() => Gateway.Dispose();
    }

    sealed class ProviderFixture : IDisposable
    {
        public readonly ClientReplicationTests.GatewayScope Gateway = new();
        public readonly HoloMapSession Session = new();
        public readonly object Scene = Nested("Scene"), Cache = Nested("ProjectedCache");
        public readonly HoloProjectedScreenData Data = Screen();
        public readonly object Proof = new(), Lease = new();
        public readonly Dictionary<long, Action<object>> Handlers = new();
        public readonly List<(long Channel, object Payload)> Sent = new();
        public readonly List<string> Messages = new();
        public readonly List<object> Released = new();
        public readonly List<object> ReleasedProofs = new();
        public readonly Func<string, object[], object> Provider, Backend;
        public object Frame, UploadResult;
        public bool ProofValid = true, LeaseValid = true;
        public int Frames, Uploads, ProofChecks, LeaseChecks;
        public int RenderReads;
        public object ViewerAnchor;
        public Vector3D Eye = new Vector3D(0, 0, 2);
        public MatrixD View = MatrixD.CreateTranslation(new Vector3D(0, 0, 2));
        public Vector2 Viewport = new Vector2(1920, 1080);
        public object[] UploadArgs, FrameArgs;
        public Action DuringUpload, DuringLeaseCheck, DuringProofCheck, DuringRelease;
        public Action<object> DuringProofRelease;
        public int ReleaseDepth, MaxReleaseDepth;
        public ProviderFixture()
        {
            Gateway.Install("Utilities", (m, a) =>
            {
                if (m.Name == "get_IsDedicated") return false;
                if (m.Name == "RegisterMessageHandler") { Handlers[(long)a[0]] = (Action<object>)a[1]; return null; }
                if (m.Name == "UnregisterMessageHandler") { Handlers.Remove((long)a[0]); return null; }
                if (m.Name == "SendModMessage") { Sent.Add(((long)a[0], a[1])); return null; }
                if (m.Name == "ShowMessage") { Messages.Add((string)a[1]); return null; }
                throw new Exception("Raster utility " + m.Name);
            });
            Provider = (op, args) =>
            {
                if (op == "frame") { Frames++; FrameArgs = args; return Frame; }
                if (op == "valid") { ProofChecks++; DuringProofCheck?.Invoke(); return ProofValid && ReferenceEquals(args[3], Proof); }
                if (op == "release") { ReleasedProofs.Add(args[0]); DuringProofRelease?.Invoke(args[0]); return true; }
                return true;
            };
            Backend = (op, args) =>
            {
                if(op=="caps")return new MyTuple<int,int,int,int>(2048,2048,1048576,16);
                if (op == "upload") { Uploads++; UploadArgs = args; DuringUpload?.Invoke(); return UploadResult ?? new MyTuple<string, object>("local_material", Lease); }
                if (op == "valid") { LeaseChecks++; DuringLeaseCheck?.Invoke(); return LeaseValid && ReferenceEquals(args[0], Lease); }
                if (op == "release")
                {
                    ReleaseDepth++; MaxReleaseDepth = Math.Max(MaxReleaseDepth, ReleaseDepth);
                    try { Released.Add(args[0]); DuringRelease?.Invoke(); return true; }
                    finally { ReleaseDepth--; }
                }
                return null;
            };
            Set(Scene, "ConsoleId", 20L);
            ((IDictionary)Field(Session, "_scenes"))[20L] = Scene;
            var screen = Nested("ProjectedScreen"); Set(screen, "Data", Data);
            ((IDictionary)Field(Scene, "Screens"))["10:view"] = screen;
            Set(Cache, "Anchor", 20L); Set(Cache, "Caller", 10L); Set(Cache, "Id", "view"); Set(Cache, "Data", Data);
            ((IDictionary)Field(Session, "_projectedCaches"))["20:10:view"] = Cache;
            Call(Session, "RegisterDisplaySources"); Call(Session, "RegisterRasterBackend");
        }
        public void RegisterProvider(int protocol = 2, Func<string, object[], object> endpoint = null) =>
            Handlers[481770101](new MyTuple<string, int, Func<string, object[], object>>("test", protocol, endpoint ?? Provider));
        public void RegisterBackend(Func<string, object[], object> endpoint = null) =>
            Handlers[481770111](new MyTuple<int, Func<string, object[], object>>(1, endpoint ?? Backend));
        public void InstallViewer()
        {
            var projection = MatrixD.Identity; projection.M34 = 1; projection.M43 = -1; projection.M44 = 0;
            ViewerAnchor = DrawTestProxy.Make(typeof(IMyProjector), (m, a) => m.Name switch
            {
                "get_Closed" => false, "get_IsWorking" => true, "get_WorldMatrix" => MatrixD.Identity,
                "GetPosition" => Vector3D.Zero,
                "get_Render" => ThrowRender(),
                _ => throw new Exception("Unexpected adaptive anchor " + m.Name)
            });
            object ThrowRender() { RenderReads++; throw new Exception("Render must not be read for offscreen screen"); }
            Gateway.Install("Entities", (m, a) => m.Name == "GetEntityById" && (long)a[0] == 20 ? ViewerAnchor : null);
            Gateway.Install("Session", (m, a) =>
            {
                if (m.Name != "get_Camera") throw new Exception("Unexpected adaptive session " + m.Name);
                return DrawTestProxy.Make(m.ReturnType, (cameraMethod, cameraArgs) => cameraMethod.Name switch
                {
                    "get_Position" => Eye,
                    "get_ViewMatrix" => View,
                    "get_ProjectionMatrix" => projection,
                    "get_ViewportSize" => cameraMethod.ReturnType == typeof(Vector2I) ? new Vector2I((int)Viewport.X, (int)Viewport.Y) : Viewport,
                    _ => throw new Exception("Unexpected adaptive camera " + cameraMethod.Name)
                });
            });
        }
        public object AddScreen(string id)
        {
            var data = Screen(); data.Id = id; data.ContentRenderer = 1;
            var screen = Nested("ProjectedScreen"); Set(screen, "Data", data);
            ((IDictionary)Field(Scene, "Screens"))["10:" + id] = screen;
            var cache = Nested("ProjectedCache"); Set(cache, "Anchor", 20L); Set(cache, "Caller", 10L); Set(cache, "Id", id); Set(cache, "Data", data);
            ((IDictionary)Field(Session, "_projectedCaches"))["20:10:" + id] = cache;
            return cache;
        }
        public SurfaceMesh Build(int points = 32, int primitives = 32) => (SurfaceMesh)Call(Session, "BuildExternalSource", Scene, Data, Cache, points, primitives);
        public void Dispose() { Call(Session, "UnregisterDisplaySources"); Call(Session, "UnregisterRasterBackend"); Gateway.Dispose(); }
    }
    static MyTuple<int, object, object, bool, double> Rgba(object proof, Vector2I size, byte[] pixels, int flags = 0, double rate = 6) =>
        new(1, new MyTuple<Vector2I, byte[], int>(size, pixels, flags), proof, false, rate);
    static MyTuple<int, object, object, bool, double> Texture(object proof, string material = "native_tex", long generation = 1) =>
        new(2, new MyTuple<string, Vector2I, long>(material, new Vector2I(32, 16), generation), proof, false, 6);
    static MyTuple<int, object, object, bool, double> TextureRect(object proof,Vector4 rect,long generation=1,double rate=6) =>
        new(2,new MyTuple<string,Vector2I,long,Vector4>("native_tex",new Vector2I(2048,2048),generation,rect),proof,false,rate);
    static byte[] Pixels(int width = 16, int height = 16) => Enumerable.Repeat((byte)0xA5, width * height * 4).ToArray();
    public static int Run()
    {
        checks = 0;
        CommandsAndSerialization();
        PanoramaCommands();
        ScreenCrops();
        ScreenResolutionAndUiRaster();
        SixCameraGuideQuota();
        AdaptiveRasterSelection();
        AdaptiveVisibilityAndDemand();
        FootprintGeometryAndBounds();
        FramesAndLifecycle();
        DeferredLeaseReplacement();
        FailedPublishedLeaseReacquisition();
        NativeTextureUVRect();
        NativePortalAdaptiveLease();
        ReentrantReleaseRegistration();
        SchedulerFairness();
        CurvedFailureAndBounds();
        RasterBudgetInvalidation();
        CurvedMappingAndCleanup();
        return checks;
    }
    static void ScreenCrops()
    {
        using(var f=new DrawFixture())
        {
            var draw=f.Draw();var planes=new[]{new Vector4(0,0,-1,-1.5f)};
            draw("screen-clip",new object[]{planes});planes[0].W=-9;
            Check(f.Data.SurfaceClip.SequenceEqual(new[]{0d,0,-1,-1.5}),"crop command owns its input and preserves anchor-space convention");
            var prior=f.Data;
            Reject(()=>draw("screen-clip",new object[]{new[]{new Vector4(0,0,0,1)}}),"zero crop normal rejected");
            Check(ReferenceEquals(prior,f.Data),"invalid crop preserves the entire screen atomically");
            Reject(()=>draw("screen-clip",new object[]{new Vector4[9]}),"crop count is bounded before allocating wire data");
            draw("screen-clip",Array.Empty<object>());Check(f.Data.SurfaceClip.Length==0,"no-argument crop removes the restriction");
        }
        var session=new HoloMapSession();
        var frame=MatrixD.CreateFromYawPitchRoll(.63,-.21,.38)*MatrixD.CreateTranslation(200,-50,300);
        var anchor=(IMyTerminalBlock)DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>m.Name=="get_WorldMatrix"?frame:throw new Exception("Crop anchor "+m.Name));
        var center=new Vector3D(0,1.5,0);var forward=Vector3D.Normalize(new Vector3D(.4,.3,-.8));var normal=-forward;
        var data=Screen();data.SurfaceClip=new[]{normal.X,normal.Y,normal.Z,Vector3D.Dot(normal,center)};
        var volume=(DisplayVolume)Call(session,"SurfaceCrop",data,anchor,null);
        for(int i=-5;i<=5;i++)
        {
            var local=center+forward*i;
            Check(volume.Contains(Vector3D.Transform(local,frame))==(i>=0),"hemisphere follows camera orientation through a rotated and translated projection anchor");
        }
        var outer=new DisplayVolume(MatrixD.CreateTranslation(center)*frame,DisplayVolume.Range(2));
        var combined=(DisplayVolume)Call(session,"SurfaceCrop",data,anchor,outer);
        for(int x=-3;x<=3;x++)for(int y=-3;y<=3;y++)for(int z=-3;z<=3;z++)
        {
            var world=Vector3D.Transform(center+new Vector3D(x,y,z),frame);
            Check(combined.Contains(world)==(volume.Contains(world)&&outer.Contains(world)),"surface crop intersects the existing display envelope in its own frame");
        }
        data.SurfaceClip=new[]{-1d,0,0,0};
        anchor=(IMyTerminalBlock)DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>m.Name=="get_WorldMatrix"?MatrixD.Identity:throw new Exception("Crop anchor "+m.Name));
        volume=(DisplayVolume)Call(session,"SurfaceCrop",data,anchor,null);
        var trimmed=volume.ClipTriangle(new DisplayVolume.Vertex(new Vector3D(-1,0,0),Vector2.Zero),
            new DisplayVolume.Vertex(new Vector3D(1,0,0),Vector2.UnitX),new DisplayVolume.Vertex(new Vector3D(0,1,0),new Vector2(.5f,1)));
        Check(trimmed.Count>=3&&trimmed.All(v=>v.Position.X>=-1e-9&&Math.Abs(v.UV.X-(v.Position.X+1)/2)<1e-6),"camera crop interpolates texture UVs at the hemisphere rim");
        using var viewer=new ProviderFixture();viewer.RegisterProvider();viewer.InstallViewer();
        viewer.Data.SurfaceKind=2;viewer.Data.SurfaceMapping=2;viewer.Data.SurfaceSide=0;viewer.Data.SurfaceRadius=3;
        viewer.Data.Camera[9]=Math.PI/2;
        var demand=(MyTuple<int,int,double,bool>)Call(viewer.Session,"DisplaySourceService","source-demand",new object[]{"test",20L,10L,"feed"});
        Check(demand.Item4,"visible uncut camera patch requests source pixels");
        viewer.Data.SurfaceClip=new[]{0d,0,1,0};
        demand=(MyTuple<int,int,double,bool>)Call(viewer.Session,"DisplaySourceService","source-demand",new object[]{"test",20L,10L,"feed"});
        Check(!demand.Item4&&demand.Item1==0&&Field(viewer.Cache,"AdaptiveBounds")==null,"wholly cropped camera patch requests no capture and cannot fall back to its uncut bounds");
        viewer.Data.SurfaceClip=new[]{0d,0,-1,0};
        demand=(MyTuple<int,int,double,bool>)Call(viewer.Session,"DisplaySourceService","source-demand",new object[]{"test",20L,10L,"feed"});
        Check(demand.Item4,"changing the hemisphere invalidates a previously hidden patch's adaptive cache");
    }
    static void PanoramaCommands()
    {
        using var f=new DrawFixture();var draw=f.Draw();
        Reject(()=>draw("screen-join",new object[]{"cameras"}),"plane cannot join a sphere");
        draw("screen-surface",new object[]{"sphere",3d,Math.PI/2,Math.PI/2,"inside"});
        draw("screen-mapping",new object[]{"pinhole"});draw("screen-join",new object[]{"cameras"});
        Check(f.Data.PanoramaGroup=="cameras","short join command stores bounded group");
        var copy=(HoloProjectedScreenData)Static("CloneScreen",f.Data,true);Check(copy.PanoramaGroup=="cameras","join group survives network freezing");
        var model=ProtoBuf.Meta.RuntimeTypeModel.Create();using var stream=new MemoryStream();model.Serialize(stream,copy);stream.Position=0;
        var wire=(HoloProjectedScreenData)model.Deserialize(stream,null,typeof(HoloProjectedScreenData));Check(wire.PanoramaGroup=="cameras","join group survives protobuf");
        Reject(()=>draw("screen-join",new object[]{"not a group"}),"unbounded group vocabulary rejected");
        draw("screen-join",Array.Empty<object>());Check(f.Data.PanoramaGroup==null,"join can be removed without rebuilding declarations");
    }
    static void CommandsAndSerialization()
    {
        using var f = new DrawFixture(); var draw = f.Draw();
        Check(f.Data.SurfaceKind == 0 && f.Data.SurfaceMapping == 0 && f.Data.SurfaceSide == 0 && f.Data.ContentRenderer == 0 && f.Data.UiRasterWidth == 256 && f.Data.UiRasterHeight == 144 && f.Data.UiRasterSamples == 4, "new screens default to plane/angular/inside/vector and bounded raster quality");
        draw("screen-surface", new object[] { "cylinder", 3d, Math.PI, Math.PI / 2, "outside" });
        draw("screen-mapping", new object[] { "geodesic" }); draw("screen-quality", new object[] { .04d });
        draw("screen-renderer", new object[] { "raster", 512, 256, 1 });
        Check(f.Data.SurfaceKind == 1 && f.Data.SurfaceRadius == 3 && f.Data.SurfaceSide == 1 && f.Data.SurfaceMapping == 1 && f.Data.SurfaceError == .04 && f.Data.ContentRenderer == 1 && f.Data.UiRasterWidth == 512 && f.Data.UiRasterHeight == 256 && f.Data.UiRasterSamples == 1, "draw commands commit cylinder, mapping, tessellation tolerance, and renderer quality");
        draw("screen-surface", new object[] { "sphere", 2d, Math.PI, Math.PI / 2, "inside" });
        draw("screen-mapping", new object[] { "pinhole" });
        Check(f.Data.SurfaceKind == 2 && f.Data.SurfaceMapping == 2 && f.Data.SurfaceSide == 0, "sphere and pinhole commands remain independent");
        var copy = (HoloProjectedScreenData)Static("CloneScreen", f.Data, true);
        Check(copy.SurfaceKind == 2 && copy.SurfaceMapping == 2 && copy.ContentRenderer == 1 && copy.UiRasterWidth == 512 && copy.UiRasterSamples == 1 && !ReferenceEquals(copy.Pose, f.Data.Pose), "deep clone retains new fields and detaches arrays");
        var model = ProtoBuf.Meta.RuntimeTypeModel.Create(); using var stream = new MemoryStream(); model.Serialize(stream, copy); stream.Position = 0;
        var roundtrip = (HoloProjectedScreenData)model.Deserialize(stream, null, typeof(HoloProjectedScreenData));
        Check(roundtrip.SurfaceKind == copy.SurfaceKind && roundtrip.SurfaceMapping == copy.SurfaceMapping && roundtrip.SurfaceRadius == copy.SurfaceRadius && roundtrip.SurfaceError == copy.SurfaceError && roundtrip.ContentRenderer == copy.ContentRenderer && roundtrip.UiRasterHeight == copy.UiRasterHeight && roundtrip.UiRasterSamples == copy.UiRasterSamples, "protobuf retains surface and raster declarations");
        void Invalid(string op, params object[] values) => Reject(() => draw(op, values), op + " rejects malformed or unbounded settings");
        Invalid("screen-surface", "torus"); Invalid("screen-surface", "sphere", 26d); Invalid("screen-surface", "cylinder", 2d, Math.PI, Math.PI, "back");
        Invalid("screen-mapping", "fisheye"); Invalid("screen-quality", double.NaN); Invalid("screen-quality", 0d);
        Invalid("screen-renderer", "bitmap"); Invalid("screen-renderer", "raster", 15, 16, 4);
        Invalid("screen-renderer", "raster", 4097, 1024, 4); Invalid("screen-renderer", "raster", 16, 1025, 4);
        Invalid("screen-renderer", "raster", 16, 16, 2);
        Invalid("screen-resolution", 4096, 63); Invalid("screen-resolution", 4097, 1024);
        Check(f.Data.SurfaceKind == 2 && f.Data.ContentRenderer == 1 && f.Data.UiRasterWidth == 512, "rejected commands leave prior declaration intact");
        draw("screen-resolution", new object[] { 4096, 4096 });
        Check(f.Data.UiRasterWidth == 4096 && f.Data.UiRasterHeight == 4096 && f.Data.ContentRenderer == 1 &&
              DisplayLod.MaximumVisible(new Vector2I(f.Data.UiRasterWidth, f.Data.UiRasterHeight)).Resolution == new Vector2I(1024, 1024),
            "4K request remains metadata while client fallback is capped to 1024 square pixels");
        draw("screen-resolution", new object[] { 4096, 64 });
        Check(f.Data.UiRasterWidth == 4096 && f.Data.UiRasterHeight == 64,
            "screen-resolution admits the supported 64:1 requested aspect");
        draw("screen-renderer", new object[] { "vector" });
        Check(f.Data.ContentRenderer == 0 && f.Data.UiRasterWidth == 256 && f.Data.UiRasterSamples == 4, "vector command restores documented optional quality defaults");
    }
    static void ScreenResolutionAndUiRaster()
    {
        using var f = new ProviderFixture();
        f.Data.ContentRenderer = 1; f.Data.UiRasterWidth = 32; f.Data.UiRasterHeight = 16; f.Data.UiRasterSamples = 1;
        Call(f.Session, "PrepareScreenUiRaster", f.Scene, f.Data, f.Cache);
        Check(f.Uploads == 0 && Field(f.Cache, "UiRaster") == null && (string)Field(f.Cache, "UiRasterError") != null,
            "raster UI reports a missing backend without publishing a texture");
        Check(f.Messages.Count == 1 && f.Messages[0] == "Requires plugin: HDR Client Renderer (raster UI)." && !(bool)Call(f.Session,"UseProjectedVectorFallback",f.Data),
            "explicit raster reports its plugin requirement without silently drawing vector fallback");
        Call(f.Session, "PrepareScreenUiRaster", f.Scene, f.Data, f.Cache);
        Check(f.Messages.Count == 1, "unchanged raster failure does not repeat its chat notification");
        f.RegisterBackend(); Call(f.Session, "PrepareScreenUiRaster", f.Scene, f.Data, f.Cache);
        Check(f.Uploads == 1 && (int)f.UploadArgs[2] == 32 && (int)f.UploadArgs[3] == 16 && ((byte[])f.UploadArgs[4]).Length == 32 * 16 * 4 &&
              Field(f.Cache, "UiRaster") != null && Field(f.Cache, "UiRasterMesh") != null && Field(f.Cache, "UiRasterError") == null,
            "UI compositor uploads exact declared resolution and RGBA byte count");
        f.Data.UiRasterWidth = 64; f.Data.UiRasterHeight = 32; f.Data.UiRasterSamples = 4;
        Call(f.Session, "PrepareScreenUiRaster", f.Scene, f.Data, f.Cache);
        Check(f.Uploads == 2 && (int)f.UploadArgs[2] == 64 && (int)f.UploadArgs[3] == 32 && ((byte[])f.UploadArgs[4]).Length == 64 * 32 * 4 &&
              f.Released.Count(lease => ReferenceEquals(lease, f.Lease)) == 0,
            "UI resolution change keeps its published lease until replacement commits");
        var priorQuad = Field(f.Cache, "UiRasterMesh");
        f.Data.CanvasWidth = 3; f.Data.CanvasHeight = 1.5;
        Call(f.Session, "PrepareScreenUiRaster", f.Scene, f.Data, f.Cache);
        var resizedQuad = (SurfaceMesh)Field(f.Cache, "UiRasterMesh");
        Check(f.Uploads == 3 && !ReferenceEquals(priorQuad, resizedQuad) && Math.Abs(resizedQuad.Geometry.Points[0].X + 1.5) < 1e-9 &&
              Math.Abs(resizedQuad.Geometry.Points[0].Y + .75) < 1e-9,
            "canvas resize rebuilds UI quad even when cached screen data was updated in place");
        f.Data.UiRasterWidth = 4096; f.Data.UiRasterHeight = 4096;
        Call(f.Session, "PrepareScreenUiRaster", f.Scene, f.Data, f.Cache);
        Check(f.Uploads == 4 && (int)f.UploadArgs[2] == 1024 && (int)f.UploadArgs[3] == 1024 &&
              ((byte[])f.UploadArgs[4]).Length == 1024 * 1024 * 4 && f.Data.UiRasterWidth == 4096,
            "4K screen request uploads at the bounded client resolution without rewriting declaration metadata");
        var label = Nested("Label"); Set(label, "CallerId", 10L); Set(label, "Layer", "s_view__ink");
        var entry = Nested("LcdVectorEntry"); Set(entry, "Caller", 10L); Set(entry, "Label", label);
        ((IList)Field(f.Cache, "Items")).Add(entry);
        var layer = Nested("Layer"); Set(layer, "CallerId", 10L); Set(layer, "Name", "s_view__ink");
        ((IDictionary)Field(f.Scene, "Layers"))["10:s_view__ink"] = layer;
        Set(f.Cache, "RasterLayerState", new[] { 1f });
        Check((bool)Call(f.Session, "RasterLayersCurrent", f.Scene, f.Cache), "cached raster records current layer alpha");
        Set(layer, "Opacity", 0f);
        Check(!(bool)Call(f.Session, "RasterLayersCurrent", f.Scene, f.Cache), "layer opacity change immediately marks cached raster stale");
        f.Data.ContentRenderer = 0; Call(f.Session, "PrepareScreenUiRaster", f.Scene, f.Data, f.Cache);
        Check(Field(f.Cache, "UiRaster") == null && Field(f.Cache, "UiRasterMesh") == null && Field(f.Cache, "UiRasterError") == null &&
              f.Released.Count(lease => ReferenceEquals(lease, f.Lease)) == 1,
            "switching back to vector content clears raster UI and releases the retained lease once");
        ((IList)Field(f.Cache,"Items")).Clear();f.Data.ContentRenderer=1;
        f.RegisterBackend((op,args)=>op=="caps"?new MyTuple<int,int,int,int>(1024,1024,262144,16):f.Backend(op,args));
        Call(f.Session,"PrepareScreenUiRaster",f.Scene,f.Data,f.Cache);
        Check((int)f.UploadArgs[2]==512&&(int)f.UploadArgs[3]==512&&Field(f.Cache,"UiRasterError")==null,
            "older client plugins negotiate their smaller texture limit without vector fallback");
    }
    static void SixCameraGuideQuota()
    {
        using var f=new DrawFixture();var draw=f.Draw();
        for(int i=0;i<3;i++)draw("line",new object[]{"other"+i,Vector3D.Zero,Vector3D.UnitX,"cyan"});
        for(int cycle=0;cycle<2;cycle++)
        {
            if(cycle>0)for(int i=0;i<6;i++){draw("screen",new object[]{"cam"+i});draw("clear",Array.Empty<object>());}
            for(int i=0;i<6;i++)
            {
                draw("screen",new object[]{"cam"+i,MatrixD.Identity,2d,2d,2d,2d});
                string guide="<svg viewBox='0 0 200 200'><rect x='1' y='1' width='198' height='198' fill='none' stroke='#00ffff' stroke-width='1'/><text x='100' y='20' font-size='10' text-anchor='middle' fill='white'>CAM "+(i+1)+"</text></svg>";
                draw("svg",new object[]{"guide",guide,MatrixD.CreateScale(.01),12});
            }
            var scene=((IDictionary)Field(f.Session,"_scenes"))[20L];
            Check(((IDictionary)Field(scene,"Screens")).Count==7&&((IDictionary)Field(scene,"Items")).Count==9,
                "six camera guides plus four existing display objects fit the sixteen-object quota on setup/rebuild "+cycle);
        }
    }
    static void AdaptiveVisibilityAndDemand()
    {
        using var f = new ProviderFixture(); f.RegisterProvider(); f.InstallViewer();
        f.Data.ContentRenderer = 1; f.Data.UiRasterWidth = 4096; f.Data.UiRasterHeight = 4096;
        f.Data.Pose[12] = 20;
        object[] request = { "test", 20L, 10L, "feed" };
        MyTuple<int, int, double, bool> Demand() =>
            (MyTuple<int, int, double, bool>)Call(f.Session, "DisplaySourceService", "source-demand", request);
        var offscreen = Demand();
        var anyOff=(MyTuple<int,int,double,bool>)Call(f.Session,"DisplaySourceService","source-demand-any",new object[]{"test","feed"});
        Check(!anyOff.Item4&&anyOff.Item1==0,"aggregate capture hint does not force offscreen sources");
        Check(!offscreen.Item4 && offscreen.Item1 == 0 && offscreen.Item2 == 0 && offscreen.Item3 == 0 &&
              !(bool)Call(f.Session, "DisplaySourceService", "source-state", request),
            "off-frustum source has no dimensions, rate, or active state");
        object[] drawArgs = { f.Scene, f.ViewerAnchor, 10, 1 };
        Call(f.Session, "DrawProjectedScreens", drawArgs);
        Check(f.RenderReads == 0 && f.Frames == 0 && Field(f.Cache, "UiRaster") == null && !(bool)Field(f.Cache, "Front"),
            "offscreen projected screen is culled before render access, source callback, or UI upload");
        f.Data.Pose[12] = 0;
        var near = Demand();
        var anyNear=(MyTuple<int,int,double,bool>)Call(f.Session,"DisplaySourceService","source-demand-any",new object[]{"test","feed"});
        Check(anyNear.Equals(near),"aggregate source demand matches visible screen demand");
        Check(!(bool)Call(f.Session,"DisplaySourceService","source-demand-any",new object[]{"test",20L}),"capture hint rejects malformed source identity");
        Check(near.Item4 && near.Item1 > 0 && near.Item2 > 0 && near.Item1 <= DisplayLod.MaxSide && near.Item2 <= DisplayLod.MaxSide &&
              (long)near.Item1 * near.Item2 <= DisplayLod.MaxPixels && near.Item3 == f.Data.RefreshHz &&
              (bool)Call(f.Session, "DisplaySourceService", "source-state", request),
            "visible 4K declaration reports bounded client demand and true legacy source state");
        f.Eye = new Vector3D(0, 0, 40); f.View = MatrixD.CreateTranslation(new Vector3D(0, 0, 40));
        var far = Demand();
        Check(far.Item4 && far.Item1 < near.Item1 && far.Item2 < near.Item2 && far.Item3 == near.Item3,
            "farther camera lowers requested source pixels while preserving refresh demand");
        f.Frame = null; f.Build();
        Check(f.FrameArgs.Length == 16 && (int)f.FrameArgs[14] == far.Item1 && (int)f.FrameArgs[15] == far.Item2,
            "v2 frame request uses current viewer-selected source resolution");
        f.Eye = new Vector3D(0, 0, 2); f.View = MatrixD.CreateTranslation(new Vector3D(0, 0, 2));
        var secondCache = f.AddScreen("aux");
        var second = (HoloProjectedScreenData)Field(secondCache, "Data");
        second.UiRasterWidth = 1024; second.UiRasterHeight = 512; second.RefreshHz = 12; second.Aspect = "stretch";
        var aggregate = Demand();
        var firstLod = (DisplayLodResult)Field(f.Cache, "Adaptive");
        var secondLod = (DisplayLodResult)Field(secondCache, "Adaptive");
        var requestedAggregate = new Vector2I(Math.Max(firstLod.Resolution.X, secondLod.Resolution.X),
            Math.Max(firstLod.Resolution.Y, secondLod.Resolution.Y));
        var boundedAggregate = DisplayLod.MaximumVisible(requestedAggregate).Resolution;
        Check(aggregate.Item4 && aggregate.Item1 == boundedAggregate.X && aggregate.Item2 == boundedAggregate.Y &&
              aggregate.Item3 == 12 && (long)aggregate.Item1 * aggregate.Item2 <= DisplayLod.MaxPixels,
            "shared provider/source demand bounds aggregated visible dimensions and highest refresh rate");
        f.Data.UiRasterWidth = 1024; f.Data.UiRasterHeight = 256; f.Data.Aspect = "stretch";
        second.UiRasterWidth = 256; second.UiRasterHeight = 1024;
        aggregate = Demand();
        firstLod = (DisplayLodResult)Field(f.Cache, "Adaptive"); secondLod = (DisplayLodResult)Field(secondCache, "Adaptive");
        requestedAggregate = new Vector2I(Math.Max(firstLod.Resolution.X, secondLod.Resolution.X),
            Math.Max(firstLod.Resolution.Y, secondLod.Resolution.Y));
        boundedAggregate = DisplayLod.MaximumVisible(requestedAggregate).Resolution;
        Check(firstLod.Resolution.X > firstLod.Resolution.Y && secondLod.Resolution.Y > secondLod.Resolution.X &&
              aggregate.Item1 == boundedAggregate.X && aggregate.Item2 == boundedAggregate.Y &&
              aggregate.Item1 <= DisplayLod.MaxSide && aggregate.Item2 <= DisplayLod.MaxSide && (long)aggregate.Item1 * aggregate.Item2 <= DisplayLod.MaxPixels,
            "wide and tall consumers sharing a source cannot combine into an oversized bitmap");
    }
    static void AdaptiveRasterSelection()
    {
        using var f = new ProviderFixture(); f.RegisterBackend();
        f.Data.ContentRenderer = 1; f.Data.UiRasterWidth = 4096; f.Data.UiRasterHeight = 4096;
        Set(f.Cache, "AdaptiveKnown", true);
        Set(f.Cache, "Adaptive", new DisplayLodResult(true, new Vector2I(64, 32), 3));
        Call(f.Session, "PrepareScreenUiRaster", f.Scene, f.Data, f.Cache);
        Check(f.Uploads == 1 && (int)f.UploadArgs[2] == 64 && (int)f.UploadArgs[3] == 32 &&
              ((byte[])f.UploadArgs[4]).Length == 64 * 32 * 4,
            "far viewer's adaptive result controls actual UI raster upload size");
        Set(f.Cache, "Adaptive", new DisplayLodResult(false, Vector2I.Zero, -1));
        Call(f.Session, "PrepareScreenUiRaster", f.Scene, f.Data, f.Cache);
        Check(f.Uploads == 1 && Field(f.Cache, "UiRaster") == null && f.Released.Count(lease => ReferenceEquals(lease, f.Lease)) == 1,
            "offscreen adaptive result performs no upload and releases prior UI image");
        f.RegisterProvider(); Set(f.Cache, "Adaptive", new DisplayLodResult(true, new Vector2I(512, 512), 0));
        f.Frame = Rgba(f.Proof, new Vector2I(512, 512), Pixels(512, 512));
        Check(f.Build() != null && f.FrameArgs.Length == 16 && (int)f.FrameArgs[14] == 512 && (int)f.FrameArgs[15] == 512 &&
              f.Uploads == 2 && (int)f.UploadArgs[2] == 512 && (int)f.UploadArgs[3] == 512 &&
              ((byte[])f.UploadArgs[4]).Length == 512 * 512 * 4,
            "4K source declaration requests and uploads only a bounded 512-square RGBA frame");
    }
    static void FootprintGeometryAndBounds()
    {
        var contain = Screen(); contain.SourceProvider = null; contain.SourceId = null;
        contain.Width = 2; contain.Height = 1; contain.CanvasWidth = 2; contain.CanvasHeight = 2; contain.Aspect = "contain";
        var letterbox = (SurfaceMesh)Static("ScreenFootprint", contain);
        Check(Math.Abs(letterbox.Geometry.Points.Min(p => p.X) + .5) < 1e-6 &&
              Math.Abs(letterbox.Geometry.Points.Max(p => p.X) - .5) < 1e-6 &&
              Math.Abs(letterbox.Geometry.Points.Min(p => p.Y) + .5) < 1e-6 &&
              Math.Abs(letterbox.Geometry.Points.Max(p => p.Y) - .5) < 1e-6 &&
              letterbox.UV.Min(uv => uv.X) == 0 && letterbox.UV.Max(uv => uv.X) == 1,
            "contain footprint leaves physical side bars while retaining the complete source UV span");
        var cover = Screen(); cover.SourceProvider = null; cover.SourceId = null;
        cover.Width = 2; cover.Height = 1; cover.CanvasWidth = 4; cover.CanvasHeight = 1; cover.Aspect = "cover";
        var cropped = (SurfaceMesh)Static("ScreenFootprint", cover);
        Check(Math.Abs(cropped.Geometry.Points.Min(p => p.X) + 1) < 1e-6 &&
              Math.Abs(cropped.Geometry.Points.Max(p => p.X) - 1) < 1e-6 &&
              Math.Abs(cropped.UV.Min(uv => uv.X) - .25) < 1e-6 &&
              Math.Abs(cropped.UV.Max(uv => uv.X) - .75) < 1e-6,
            "cover footprint clips to the physical rectangle and preserves only the visible middle half of U");
        var outward = Screen(); outward.SurfaceKind = 2; outward.SurfaceMapping = 0;
        outward.SurfaceRadius = 2; outward.SurfaceHorizontal = Math.PI; outward.SurfaceVertical = Math.PI / 2;
        outward.SurfaceSide = 1;
        var outsideMesh = (SurfaceMesh)Static("ScreenFootprint", outward);
        outward.SurfaceSide = 0;
        var insideMesh = (SurfaceMesh)Static("ScreenFootprint", outward);
        Check(outsideMesh.Geometry.Points.Length == insideMesh.Geometry.Points.Length &&
              Enumerable.Range(0, outsideMesh.Geometry.Points.Length).All(i =>
                  Math.Abs(outsideMesh.Geometry.Points[i].X + insideMesh.Geometry.Points[i].X) < 1e-9 &&
                  Math.Abs(outsideMesh.Geometry.Points[i].Y - insideMesh.Geometry.Points[i].Y) < 1e-9 &&
                  Math.Abs(outsideMesh.Geometry.Points[i].Z - insideMesh.Geometry.Points[i].Z) < 1e-9 &&
                  outsideMesh.UV[i] == insideMesh.UV[i]),
            "inside sphere footprint mirrors X while keeping the texture coordinates attached to the same patch");
        var pinhole = Screen(); pinhole.SurfaceKind = 2; pinhole.SurfaceMapping = 2; pinhole.SurfaceSide = 1;
        pinhole.SurfaceHorizontal = .25; pinhole.SurfaceVertical = .25;
        pinhole.Camera[9] = 1.2; pinhole.UiRasterWidth = 1024; pinhole.UiRasterHeight = 256;
        var pinholeMesh = (SurfaceMesh)Static("ScreenFootprint", pinhole);
        double horizontal = 2 * Math.Atan(Math.Tan(pinhole.Camera[9] / 2) * pinhole.UiRasterWidth / pinhole.UiRasterHeight);
        int nx = (int)Math.Ceiling(horizontal / (Math.PI / 8)), ny = (int)Math.Ceiling(pinhole.Camera[9] / (Math.PI / 8));
        Check(pinholeMesh.Geometry.Points.Length == (nx + 1) * (ny + 1) && nx > 1 && ny > 1,
            "pinhole footprint subdivides using effective camera FOV and source aspect, not unused angular settings");
        var portrait = Screen(); portrait.SurfaceKind = 2; portrait.SurfaceMapping = 2; portrait.SurfaceSide = 1;
        portrait.CanvasWidth = 2; portrait.CanvasHeight = 2; portrait.UiRasterWidth = 256; portrait.UiRasterHeight = 1024;
        portrait.Camera[9] = 1.2;
        var defaultAspect = (SurfaceStyle)Static("ScreenSurfaceStyle", portrait, 0d);
        var videoAspect = (SurfaceStyle)Static("ScreenSurfaceStyle", portrait, 2d);
        var rightRay = SurfaceMapping.MapPoint(new Vector3D(1, 0, 0), 2, 2, videoAspect);
        Check(Math.Abs(defaultAspect.SourceAspect - .25) < 1e-12 && Math.Abs(videoAspect.SourceAspect - .25) < 1e-12 &&
              Math.Abs(rightRay.X / rightRay.Z - Math.Tan(.6) * .25) < 1e-10,
            "pinhole UI and video layers use requested portrait aspect on a square canvas regardless of caller aspect");
        var portraitCopy = (HoloProjectedScreenData)Static("CloneScreen", portrait, true);
        portraitCopy.UiRasterWidth = 512;
        Check(!(bool)Static("SameSurface", portrait, portraitCopy),
            "changing requested raster aspect invalidates cached curved surface geometry");
        void BoundsContain(HoloProjectedScreenData d, string name)
        {
            var bound = (SurfaceMesh)Static("ScreenFootprintBounds", d);
            var vertices = bound.Geometry.Points;
            double minX = vertices.Min(p => p.X), maxX = vertices.Max(p => p.X);
            double minY = vertices.Min(p => p.Y), maxY = vertices.Max(p => p.Y);
            double minZ = vertices.Min(p => p.Z), maxZ = vertices.Max(p => p.Z);
            var style = (SurfaceStyle)Static("ScreenSurfaceStyle", d, (double)d.UiRasterWidth / d.UiRasterHeight);
            bool contained = true;
            for (int y = 0; y <= 20; y++) for (int x = 0; x <= 20; x++)
            {
                var source = new Vector3D((x / 20d - .5) * d.CanvasWidth, (y / 20d - .5) * d.CanvasHeight, 0);
                var point = SurfaceMapping.MapPoint(source, d.CanvasWidth, d.CanvasHeight, style);
                if (d.SurfaceKind == 1) point.Y *= d.Height / d.CanvasHeight;
                if (d.SurfaceSide == 0) point.X = -point.X;
                contained &= point.X >= minX - 1e-8 && point.X <= maxX + 1e-8 &&
                             point.Y >= minY - 1e-8 && point.Y <= maxY + 1e-8 &&
                             point.Z >= minZ - 1e-8 && point.Z <= maxZ + 1e-8;
            }
            Check(contained, name + " analytic bounds contain sampled curved patch between proxy vertices");
        }
        BoundsContain(outward, "angular inside sphere");
        BoundsContain(pinhole, "pinhole sphere");
        BoundsContain(portrait, "portrait pinhole sphere");
        var cylinder = Screen(); cylinder.SurfaceKind = 1; cylinder.SurfaceSide = 1;
        cylinder.SurfaceRadius = 2; cylinder.SurfaceHorizontal = 2 * Math.PI; cylinder.Height = 1.5;
        BoundsContain(cylinder, "full cylinder");
        var geodesic = Screen(); geodesic.SurfaceKind = 2; geodesic.SurfaceMapping = 1; geodesic.SurfaceSide = 1;
        geodesic.SurfaceRadius = 2; geodesic.SurfaceHorizontal = Math.PI; geodesic.SurfaceVertical = Math.PI / 2;
        BoundsContain(geodesic, "geodesic sphere");
        using var fixture = new ProviderFixture(); fixture.RegisterProvider(); fixture.InstallViewer();
        fixture.Data.SurfaceKind = 2; fixture.Data.SurfaceSide = 0; fixture.Data.SurfaceRadius = 2;
        fixture.Data.SurfaceHorizontal = 2 * Math.PI; fixture.Data.SurfaceVertical = Math.PI;
        fixture.Eye = new Vector3D(0, 0, 1.99);
        var demand = (MyTuple<int, int, double, bool>)Call(fixture.Session, "DisplaySourceService", "source-demand",
            new object[] { "test", 20L, 10L, "feed" });
        Check(demand.Item4 && demand.Item1 > 0 && Field(fixture.Cache, "AdaptiveBounds") != null,
            "viewer near the inside sphere shell retains visible source demand");
        fixture.Data.SurfaceKind=0;fixture.Data.TwoSided=true;fixture.Data.BackOpacity=.35f;
        // This gateway uses a +Z clip-space projection instead of the engine's
        // conventional look-at projection. Rotate its matching view toward +Z.
        fixture.Eye=new Vector3D(0,0,-2);fixture.View=MatrixD.CreateRotationY(Math.PI)*MatrixD.CreateTranslation(0,0,2);
        demand=(MyTuple<int,int,double,bool>)Call(fixture.Session,"DisplaySourceService","source-demand",new object[]{"test",20L,10L,"feed"});
        Check(demand.Item4&&demand.Item1>0,"two-sided reverse viewer participates in raster/video source demand");
        fixture.Data.BackOpacity=0;
        demand=(MyTuple<int,int,double,bool>)Call(fixture.Session,"DisplaySourceService","source-demand",new object[]{"test",20L,10L,"feed"});
        Check(!demand.Item4&&demand.Item1==0,"transparent reverse side requests no capture or texture work");
    }
    static void DeferredLeaseReplacement()
    {
        using(var f = new ProviderFixture())
        {
            f.RegisterProvider(); f.RegisterBackend();
            f.Frame = Rgba(f.Proof, new Vector2I(16,16), Pixels());
            Check(f.Build()!=null, "first published source image is admitted");
            var first=Field(f.Cache,"SourceRaster"); int retired=f.Released.Count;
            Check(f.Build()!=null&&!ReferenceEquals(first,Field(f.Cache,"SourceRaster"))&&f.Released.Count==retired,
                "RGBA provider returning its published lease during deferral does not destroy current/pending texture");
            Call(f.Session,"ClearProjectedCaches");
            Check(f.Released.Count==retired+1,"retained source lease still retires exactly once on cache removal");
        }
        using(var f = new ProviderFixture())
        {
            f.RegisterBackend(); f.Data.ContentRenderer=1; f.Data.UiRasterWidth=32; f.Data.UiRasterHeight=16; f.Data.UiRasterSamples=1;
            Call(f.Session,"PrepareScreenUiRaster",f.Scene,f.Data,f.Cache);
            Check(Field(f.Cache,"UiRaster")!=null,"first published UI image is admitted");
            var first=Field(f.Cache,"UiRaster"); int retired=f.Released.Count;
            Call(f.Session,"PrepareScreenUiRaster",f.Scene,f.Data,f.Cache);
            Check(!ReferenceEquals(first,Field(f.Cache,"UiRaster"))&&f.Released.Count==retired,
                "UI pending update can reuse published lease without releasing its pending/current texture");
            Call(f.Session,"ClearProjectedCaches");
            Check(f.Released.Count==retired+1,"retained UI lease retires once on cache removal");
        }
    }
    static void NativeTextureUVRect()
    {
        using var f=new ProviderFixture();f.RegisterProvider();f.Frame=Texture(f.Proof);
        var legacy=f.Build();Set(f.Cache,"View",legacy);var oldUv=(Vector2[])legacy.UV.Clone();
        Check(legacy.UV[0]==new Vector2(0,1)&&legacy.UV[2]==new Vector2(1,0),"legacy three-field texture payload keeps full texture UVs");
        var rect=new Vector4(.125f,.25f,.5f,.25f);f.Frame=TextureRect(f.Proof,rect);var cropped=f.Build();
        Check(!ReferenceEquals(legacy.Geometry,cropped.Geometry)&&legacy.UV.SequenceEqual(oldUv)&&cropped.UV[0]==new Vector2(.125f,.5f)&&cropped.UV[1]==new Vector2(.625f,.5f)&&cropped.UV[2]==new Vector2(.625f,.25f)&&cropped.UV[3]==new Vector2(.125f,.25f),"subrect maps all physical plane corners with fresh geometry and immutable prior UVs");
        Check(ReferenceEquals(cropped,f.Build())&&f.ReleasedProofs.Count==0,"stable subrect preserves mesh identity and same retained provider lease");
        object[] Map(SurfaceMesh source,HoloProjectedScreenData d)=>new object[]{f.Scene,f.Cache,"uv",source,d,MatrixD.Identity,0d,0d,true,1,null,null,true};
        f.Data.SurfaceKind=2;f.Data.SurfaceSide=1;f.Data.SurfaceHorizontal=Math.PI;f.Data.SurfaceVertical=Math.PI/2;Set(f.Session,"_ticks",100);
        var curved=(SurfaceMesh)Call(f.Session,"MappedScreenGeometry",Map(cropped,f.Data));
        Check(curved!=null&&curved.UV.All(uv=>uv.X>=rect.X&&uv.X<=rect.X+rect.Z&&uv.Y>=rect.Y&&uv.Y<=rect.Y+rect.W)&&curved.UV.Any(uv=>uv==new Vector2(.125f,.5f))&&curved.UV.Any(uv=>uv==new Vector2(.625f,.25f)),"curved fast quad preserves atlas rectangle instead of regenerating full texture UVs");
        var savedCurved=(Vector2[])curved.UV.Clone();var nextRect=new Vector4(.25f,.125f,.25f,.5f);f.Frame=TextureRect(f.Proof,nextRect);var next=f.Build();Set(f.Session,"_ticks",101);
        var nextCurved=(SurfaceMesh)Call(f.Session,"MappedScreenGeometry",Map(next,f.Data));
        Check(!ReferenceEquals(curved.Geometry,nextCurved.Geometry)&&curved.UV.SequenceEqual(savedCurved)&&nextCurved.UV.All(uv=>uv.X>=.25f&&uv.X<=.5f&&uv.Y>=.125f&&uv.Y<=.625f),"changed atlas rectangle rebuilds curved geometry without mutating retained geometry");
        f.Data.SurfaceKind=4;f.Data.SurfaceMeshPoints=new[]{-1d,-1,0,1,-1,0,0,1,0};f.Data.SurfaceMeshTriangles=new[]{0,1,2};f.Data.SurfaceMeshUV=new[]{0f,1,1,1,.5f,0};Set(f.Session,"_ticks",102);
        var authored=(SurfaceMesh)Call(f.Session,"MappedScreenGeometry",Map(next,f.Data));
        Check(authored.UV.Length==3&&authored.UV[0]==new Vector2(.25f,.625f)&&authored.UV[2]==new Vector2(.375f,.125f),"authored physical mesh UVs map into the extended texture rectangle");
        Set(f.Cache,"View",next);int releases=f.ReleasedProofs.Count;
        foreach(var bad in new[]{new Vector4(float.NaN,0,1,1),new Vector4(0,float.PositiveInfinity,1,1),new Vector4(-.01f,0,.5f,.5f),new Vector4(0,0,0,1),new Vector4(0,0,1,-1),new Vector4(.75f,0,.5f,1),new Vector4(0,.75f,1,.5f)})
        {f.Frame=TextureRect(f.Proof,bad);Reject(()=>f.Build(),"nonfinite, negative or overflowing UV rectangle");Check(ReferenceEquals(Field(f.Cache,"View"),next)&&f.ReleasedProofs.Count==releases,"rejected UV rectangle retains exact owner evidence until outer cleanup");}
        var fresh=new object();f.Frame=TextureRect(fresh,new Vector4(0,0,2,1));Reject(()=>f.Build(),"invalid newly acquired UV rectangle");
        Check(f.ReleasedProofs.Count(e=>ReferenceEquals(e,fresh))==1&&f.ReleasedProofs.Count(e=>ReferenceEquals(e,f.Proof))==0,"invalid new rectangle releases only the new evidence");
        f.Frame=Texture(f.Proof);var full=f.Build();Check(full.UV.SequenceEqual(oldUv)&&!ReferenceEquals(full.Geometry,next.Geometry),"legacy frame after subrect restores full UVs with fresh geometry");
    }
    static void NativePortalAdaptiveLease()
    {
        using var f=new ProviderFixture();f.Data.SourceProvider="native-portal";f.Data.SourceId=f.Data.Id;f.Data.UiRasterWidth=4096;f.Data.UiRasterHeight=2048;f.Data.RefreshHz=120;
        f.Handlers[481770101](new MyTuple<string,int,Func<string,object[],object>>("native-portal",2,f.Provider));
        f.Frame=TextureRect(f.Proof,new Vector4(0,0,1,1),rate:120);var source=f.Build();Set(f.Cache,"View",source);
        Check((int)f.FrameArgs[14]==4096&&(int)f.FrameArgs[15]==2048&&source!=null,"native portal request uses the wide 4096 by 2048 raster allowance and 120 Hz texture frame");
        Check(Math.Abs((double)Field(f.Cache,"NextCapture")-1d/120)<1e-12,"native portal frame cadence is not clamped to the ordinary LCD limit");
        f.Frame=TextureRect(f.Proof,new Vector4(0,0,1,1),rate:121);Reject(()=>f.Build(),"native portal texture frame rate exceeds 120 Hz");
        Check(ReferenceEquals(Field(f.Cache,"View"),source)&&f.ReleasedProofs.Count==0,"invalid rate does not prematurely release retained native evidence");
        f.Frame=TextureRect(f.Proof,new Vector4(0,0,1,1),rate:120);
        f.InstallViewer();f.Viewport=new Vector2(4096,4096);
        var near=(DisplayLodResult)Call(f.Session,"ScreenLod",f.Data,f.ViewerAnchor,f.Cache);
        f.Eye=new Vector3D(0,0,40);f.View=MatrixD.CreateTranslation(0,0,40);
        var far=(DisplayLodResult)Call(f.Session,"ScreenLod",f.Data,f.ViewerAnchor,f.Cache);
        Check(near.Visible&&far.Visible&&far.Resolution.X<near.Resolution.X,"moving viewer changes native portal raster LOD");
        Check(ReferenceEquals(Field(f.Cache,"View"),source)&&ReferenceEquals(Field(f.Cache,"SourceEvidence"),f.Proof)&&f.ReleasedProofs.Count==0,"sole visible LOD change retains displayed portal lease while adaptation is pending");
        Check((double)Field(f.Cache,"NextCapture")==-1,"LOD adaptation remains eligible for a replacement without deleting output");
        f.Eye=new Vector3D(0,0,2);f.View=MatrixD.CreateTranslation(0,0,2);f.Data.Pose[12]=20;var invisible=(DisplayLodResult)Call(f.Session,"ScreenLod",f.Data,f.ViewerAnchor,f.Cache);
        Check(!invisible.Visible&&Field(f.Cache,"View")==null&&f.ReleasedProofs.Count(e=>ReferenceEquals(e,f.Proof))==1,"off-frustum portal LOD still clears the lease rather than retaining invisible work");
    }
    static void FailedPublishedLeaseReacquisition()
    {
        using (var f = new ProviderFixture())
        {
            f.RegisterProvider(); f.Frame = Texture(f.Proof);
            var published = f.Build(); Set(f.Cache, "View", published);
            f.DuringProofRelease = evidence => { if (ReferenceEquals(evidence, f.Proof)) f.ProofValid = false; };
            void Retained(string why)
            {
                Call(f.Session, "ValidateCachedExternalView", f.Scene, f.Data, f.Cache);
                Check(f.ProofValid && ReferenceEquals(Field(f.Cache, "View"), published) &&
                      ReferenceEquals(Field(f.Cache, "SourceEvidence"), f.Proof) &&
                      f.ReleasedProofs.All(evidence => !ReferenceEquals(evidence, f.Proof)), why);
            }
            Check(f.Build(3, 2) == null, "published texture reacquisition can be point-starved");
            Retained("point-starved reacquisition retains its exact current lease and drawable cache");
            Check(f.Build(4, 1) == null, "published texture reacquisition can be primitive-starved");
            Retained("primitive-starved reacquisition does not cancel the displayed source");
            var freshBudget = new object(); f.Frame = Texture(freshBudget);
            Check(f.Build(3, 2) == null && f.ReleasedProofs.Count(e => ReferenceEquals(e, freshBudget)) == 1,
                "point-starved genuinely new evidence is released exactly once");
            var freshPrimitive = new object(); f.Frame = Texture(freshPrimitive);
            Check(f.Build(4, 1) == null && f.ReleasedProofs.Count(e => ReferenceEquals(e, freshPrimitive)) == 1,
                "primitive-starved genuinely new evidence is released exactly once");
            Retained("releasing rejected new evidence preserves the prior published lease");
            f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels());
            Check(f.Build() == null && f.Uploads == 0, "RGBA reacquisition can defer without a raster backend");
            Retained("null upload before transfer retains evidence already owned by the cache");
            var freshUpload = new object(); f.Frame = Rgba(freshUpload, new Vector2I(16, 16), Pixels());
            Check(f.Build() == null && f.ReleasedProofs.Count(e => ReferenceEquals(e, freshUpload)) == 1,
                "null upload releases a genuinely new provider lease once");
            f.Frame = Texture(f.Proof, generation: 0);
            Reject(() => f.Build(), "malformed retained reacquisition throws before transfer");
            Retained("pre-transfer validation failure retains its exact current provider lease");
            var freshMalformed = new object(); f.Frame = Texture(freshMalformed, generation: 0);
            Reject(() => f.Build(), "malformed new reacquisition throws before transfer");
            Check(f.ReleasedProofs.Count(e => ReferenceEquals(e, freshMalformed)) == 1,
                "pre-transfer exception releases a genuinely new provider lease once");
            Retained("rejected new malformed frame does not invalidate prior displayed evidence");
            f.Frame = null; Check(f.Build() == null, "provider can skip pending acquisition");
            Retained("absent replacement frame does not release the retained lease");
            Static("ClearExternalBudgetView", f.Cache);
            Check(!f.ProofValid && Field(f.Cache, "View") == null && Field(f.Cache, "SourceEvidence") == null &&
                  f.ReleasedProofs.Count(e => ReferenceEquals(e, f.Proof)) == 1,
                "outer budget/failure cleanup still clears output and cancels the published lease exactly once");
            Static("ClearExternalBudgetView", f.Cache);
            Check(f.ReleasedProofs.Count(e => ReferenceEquals(e, f.Proof)) == 1,
                "repeated outer cleanup does not cancel the retired lease twice");
        }
        using (var f = new ProviderFixture())
        {
            f.RegisterProvider(); f.Frame = Texture(f.Proof); Set(f.Cache, "View", f.Build());
            int otherReleases = 0;
            Func<string, object[], object> otherOwner = (op, args) => { if (op == "release") otherReleases++; return true; };
            Set(f.Cache, "SourceEndpoint", otherOwner);
            Check(f.Build(3, 2) == null && f.ReleasedProofs.Count(e => ReferenceEquals(e, f.Proof)) == 1 && otherReleases == 0,
                "same evidence object with a different cached endpoint releases the failed offering owner's lease");
            Check(ReferenceEquals(Field(f.Cache, "SourceEndpoint"), otherOwner) && ReferenceEquals(Field(f.Cache, "SourceEvidence"), f.Proof),
                "failed offering never borrows or overwrites another endpoint's retained ownership");
        }
    }
    static void FramesAndLifecycle()
    {
        using var f = new ProviderFixture();
        Check(f.Handlers.ContainsKey(481770101) && f.Handlers.ContainsKey(481770111) && f.Sent.Any(s => s.Channel == 481770110 && s.Payload is Func<string, object[], object>), "raster backend publishes discovery and registers exact channel");
        f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels()); f.RegisterProvider();
        Check(f.Build() == null && f.Uploads == 0, "RGBA source needs an available upload backend");
        f.RegisterBackend(); var mesh = f.Build(); Set(f.Cache, "View", mesh);
        Check(mesh != null && mesh.UV.Length == 4 && mesh.Geometry.Triangles.Length == 6 && (bool)Field(f.Cache, "SourceTexture") && (string)Field(f.Cache, "SourceTextureMaterial") == "local_material", "v2 RGBA frame produces a textured quad");
        Check(f.UploadArgs.Length == 5 && (string)f.UploadArgs[0] == "20:10:view:source" && (long)f.UploadArgs[1] > 0 && (int)f.UploadArgs[2] == 16 && (int)f.UploadArgs[3] == 16 && f.FrameArgs.Length == 16 && (int)f.FrameArgs[14] == 256 && (int)f.FrameArgs[15] == 144, "v2 source request includes screen raster resolution after the legacy capture arguments");
        f.Data.UiRasterWidth = 512; f.Data.UiRasterHeight = 288;
        f.Frame = null; Check(f.Build() == null, "provider may skip a v2 frame without invalidating its request");
        Check(f.FrameArgs.Length == 16 && (int)f.FrameArgs[14] == 512 && (int)f.FrameArgs[15] == 288 && f.FrameArgs[8] is double[] && !ReferenceEquals(f.FrameArgs[8], f.Data.Camera), "v2 source receives updated declared screen resolution and a detached camera");
        f.Data.UiRasterWidth = 4096; f.Data.UiRasterHeight = 4096; f.Build();
        Check(f.FrameArgs.Length == 16 && (int)f.FrameArgs[14] == 1024 && (int)f.FrameArgs[15] == 1024 && f.Data.UiRasterWidth == 4096,
            "4K source request is advertised at bounded client demand while declaration retains requested dimensions");
        f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels());
        var framePixels = ((MyTuple<Vector2I, byte[], int>)((MyTuple<int, object, object, bool, double>)f.Frame).Item2).Item2;
        Check(!ReferenceEquals(f.UploadArgs[4], framePixels), "upload detaches provider-owned RGBA bytes");
        int priorProofReleases = f.ReleasedProofs.Count;
        f.ProofValid = false; Call(f.Session, "ValidateCachedExternalView", f.Scene, f.Data, f.Cache);
        Check(Field(f.Cache, "View") == null && f.Released.Contains(f.Lease), "proof invalidation hides source and releases backend lease before draw");
        Check(f.ReleasedProofs.Count(proof => ReferenceEquals(proof, f.Proof)) == priorProofReleases + 1,
            "proof invalidation releases provider evidence once");
        f.ProofValid = true; f.Frame = Texture(f.Proof); var texture = f.Build(); Set(f.Cache, "View", texture);
        Check(texture != null && (string)Field(f.Cache, "SourceTextureMaterial") == "native_tex" && f.Uploads == 1 && Field(f.Cache, "SourceRaster") == null, "v2 native texture frame uses provider material without RGBA upload");
        priorProofReleases = f.ReleasedProofs.Count;
        f.ProofValid = false; Call(f.Session, "ValidateCachedExternalView", f.Scene, f.Data, f.Cache);
        Check(Field(f.Cache, "View") == null && Field(f.Cache, "SourceTextureMaterial") == null &&
              f.ReleasedProofs.Count(proof => ReferenceEquals(proof, f.Proof)) == priorProofReleases + 1,
            "native texture proof is revalidated and released before drawing");
        f.ProofValid = true;
        f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels());
        priorProofReleases = f.ReleasedProofs.Count;
        Check(f.Build(3, 2) == null && f.Uploads == 1 && (bool)Field(f.Cache, "SourceReduced"), "too-small point allowance reduces raster source without uploading");
        Check(f.ReleasedProofs.Count == priorProofReleases + 1, "budget-rejected v2 frame releases provider evidence exactly once");
        Check(f.Build(4, 1) == null && f.Uploads == 1, "too-small primitive allowance reduces raster source without uploading");
        priorProofReleases = f.ReleasedProofs.Count;
        Reject(() => { f.Frame = Rgba(f.Proof, new Vector2I(16, 16), new byte[3]); f.Build(); }, "wrong RGBA byte count");
        Check(f.ReleasedProofs.Count == priorProofReleases + 1, "malformed v2 frame releases provider evidence exactly once");
        Reject(() => { f.Frame = Rgba(f.Proof, new Vector2I(15, 16), Pixels(15, 16)); f.Build(); }, "undersized RGBA dimensions");
        Reject(() => { f.Frame = Rgba(f.Proof, new Vector2I(2048, 513), Pixels(16, 16)); f.Build(); }, "RGBA pixel cap");
        Reject(() => { f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels(), 1); f.Build(); }, "unsupported RGBA format flags");
        Reject(() => { f.Frame = Texture(f.Proof, "../unsafe"); f.Build(); }, "unsafe local material");
        Reject(() => { f.Frame = Texture(f.Proof, "native_tex", 0); f.Build(); }, "nonpositive native texture generation");
        Reject(() => { f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels(), 0, double.NaN); f.Build(); }, "nonfinite refresh");
        Reject(() => { f.Frame = new MyTuple<int, object, object, bool, double>(3, null, f.Proof, false, 6); f.Build(); }, "unknown frame kind");
        f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels());
        f.UploadResult = new MyTuple<string, object>("bad/name", f.Lease);
        Reject(() => f.Build(), "backend cannot return unsafe material name"); f.UploadResult = null;
        f.LeaseValid = false; Check(f.Build() == null && f.Released.Contains(f.Lease), "invalid backend lease is released before use");
        f.LeaseValid = true; mesh = f.Build(); Set(f.Cache, "View", mesh);
        var replacement = new Func<string, object[], object>((op, args) => op == "valid" ? true : null);
        f.RegisterBackend(replacement);
        Check(Field(f.Cache, "View") == null && Field(f.Cache, "SourceRaster") == null && f.Released.Contains(f.Lease), "backend replacement invalidates cached frame and releases old lease");
        Check(!(bool)Call(f.Session, "RasterService", "unregister", new object[] { f.Backend }), "stale backend cannot unregister its replacement");
        Check((bool)Call(f.Session, "RasterService", "unregister", new object[] { replacement }), "active backend can unregister itself");
        f.RegisterBackend(); f.DuringUpload = () => f.RegisterBackend(replacement); f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels());
        Check(f.Build() == null && f.Released.Contains(f.Lease), "reentrant backend replacement during upload discards and releases stale lease");
        f.DuringUpload = null; f.RegisterBackend(); f.DuringLeaseCheck = () => f.RegisterBackend(replacement);
        Check(f.Build() == null && f.Released.Contains(f.Lease), "reentrant backend replacement during lease validation discards stale upload");
        f.DuringLeaseCheck = null; f.RegisterBackend();
        Func<string, object[], object> nextProvider = (op, args) => op == "valid" ? true : null;
        f.DuringProofCheck = () => f.RegisterProvider(2, nextProvider);
        Check(f.Build() == null && f.Released.Contains(f.Lease), "reentrant provider endpoint replacement during proof validation discards uploaded frame");
        f.DuringProofCheck = null; f.RegisterProvider();
        f.Frame = new MyTuple<Vector3D[], int[], Vector4[], object, bool, double>(new[] { Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitY }, new[] { 0, 1, 2 }, new[] { Vector4.One }, f.Proof, false, 1);
        Reject(() => f.Build(), "protocol 2 cannot return a legacy triangle tuple");
        f.RegisterProvider(1); f.Frame = new MyTuple<Vector3D[], int[], Vector4[], object, bool, double>(new[] { Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitY }, new[] { 0, 1, 2 }, new[] { Vector4.One }, f.Proof, false, 1);
        Check(f.Build() != null && f.FrameArgs.Length == 14 && f.FrameArgs[13] is double, "protocol 1 triangle frames retain the fourteen-argument request");
        Reject(() => { f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels()); f.Build(); }, "protocol 1 cannot bypass version negotiation with RGBA frame");
    }
    static void ReentrantReleaseRegistration()
    {
        using (var f = new ProviderFixture())
        {
            f.RegisterProvider(); f.RegisterBackend(); f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels());
            Set(f.Cache, "View", f.Build());
            Func<string, object[], object> intermediate = (op, args) => op == "valid" ? true : null;
            Func<string, object[], object> newest = (op, args) => op == "valid" ? true : null;
            int callbacks = 0;
            f.DuringRelease = () => { callbacks++; f.RegisterBackend(newest); };
            f.RegisterBackend(intermediate);
            Check(callbacks == 1 && f.Released.Count(lease => ReferenceEquals(lease, f.Lease)) == 1 && f.MaxReleaseDepth == 1,
                "backend replacement detaches old lease before release callback registers another backend");
            Check(ReferenceEquals(Field(f.Session, "_rasterBackend"), newest) && Field(f.Cache, "SourceRaster") == null && Field(f.Cache, "View") == null,
                "newest backend survives reentrant registration and stale source is hidden");
            Check(!(bool)Call(f.Session, "RasterService", "unregister", new object[] { intermediate }) &&
                  (bool)Call(f.Session, "RasterService", "unregister", new object[] { newest }),
                "intermediate backend cannot unregister reentrant winner");
        }
        using (var f = new ProviderFixture())
        {
            f.RegisterProvider(); f.RegisterBackend(); f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels());
            Set(f.Cache, "View", f.Build());
            int callbacks = 0;
            f.DuringRelease = () => { callbacks++; Call(f.Session, "ClearProjectedCaches"); };
            Call(f.Session, "ClearProjectedCaches");
            Check(callbacks == 1 && f.Released.Count(lease => ReferenceEquals(lease, f.Lease)) == 1 && f.MaxReleaseDepth == 1 &&
                  ((IDictionary)Field(f.Session, "_projectedCaches")).Count == 0,
                "projected cache clear detaches entries before reentrant release callback clears again");
        }
    }
    static void SchedulerFairness()
    {
        using var f = new ProviderFixture(); f.RegisterProvider();
        Set(f.Session, "_ticks", 100);
        var names = new[] { "a", "b", "c", "d", "e" };
        var caches = names.Select(f.AddScreen).ToArray();
        foreach (var cache in caches)
        {
            Set(cache, "Front", true); Set(cache, "Seen", 100);
            Set(cache, "NextSample", -1d); Set(cache, "NextCapture", -1d);
        }
        double now = 100 / 60d;
        bool Due(object cache, bool ui, int allowance = 1) => (bool)Call(f.Session, "OldestRasterJob", cache, now, ui, allowance);
        Check(Due(caches[0], true) && !Due(caches[1], true) && !Due(caches[2], true),
            "one UI raster slot selects oldest deterministic screen among three due screens");
        Set(caches[0], "UiAttemptTick", 100);
        Check(Due(caches[1], true) && !Due(caches[2], true), "UI scheduler advances to second screen after first attempt");
        Set(caches[1], "UiAttemptTick", 100);
        Check(Due(caches[2], true), "third 60 Hz UI screen is eventually eligible");
        Set(f.Session, "_ticks", 101); now = 101 / 60d;
        Set(caches[0], "UiAttemptTick", 101); Set(caches[1], "NextSample", now + 1);
        ((HoloProjectedScreenData)Field(caches[1], "Data")).RefreshHz = 5;
        ((HoloProjectedScreenData)Field(caches[2], "Data")).RefreshHz = 60;
        Check(Due(caches[2], true), "busy UI screen is skipped even when its earlier attempt would win ordering");
        foreach (var cache in caches) { Set(cache, "SourceAttemptTick", -10000); Set(cache, "NextCapture", -1d); }
        Check(Due(caches[0], false, 4) && Due(caches[3], false, 4) && !Due(caches[4], false, 4),
            "four source slots admit oldest four of five due protocol 2 texture screens");
        Set(caches[0], "SourceAttemptTick", 101);
        Check(Due(caches[3], false, 3) && !Due(caches[4], false, 3), "remaining three source slots rotate past attempted screen");
        Set(caches[1], "SourceAttemptTick", 101); Set(caches[2], "SourceAttemptTick", 101);
        Check(Due(caches[3], false, 1) && !Due(caches[4], false, 1), "last source slot favors older pending screen");
        Set(caches[3], "SourceAttemptTick", 101);
        Set(f.Session, "_ticks", 102); now = 102 / 60d;
        Check(Due(caches[4], false, 4), "fifth source receives a slot on the next tick");
        Set(caches[4], "SourceAttemptTick", 102);
        foreach (var cache in caches.Take(3)) Set(cache, "NextCapture", now + 1);
        ((HoloProjectedScreenData)Field(caches[0], "Data")).RefreshHz = 1;
        ((HoloProjectedScreenData)Field(caches[3], "Data")).RefreshHz = 60;
        Check(Due(caches[3], false, 4), "recently attempted source and slower busy sources do not starve due 60 Hz source");
    }
    static void CurvedFailureAndBounds()
    {
        using var f = new ProviderFixture();
        var source = (SurfaceMesh)Static("RasterQuad", 2d, 2d);
        source.Geometry.Points[0].Z = .25;
        var flat = (SurfaceMesh)Static("TransformCanvasMesh", source, MatrixD.CreateTranslation(new Vector3D(0, 0, .5)));
        Check(flat.Geometry.Points.All(point => Math.Abs(point.Z) < 1e-12) && source.Geometry.Points[0].Z == .25 &&
              ReferenceEquals(flat.UV, source.UV), "canvas transform flattens depth without changing source points or UVs");
        var cylinder = Screen(); cylinder.SurfaceKind = 1; cylinder.SurfaceRadius = 24.8; cylinder.Height = 12;
        Reject(() => Static("ValidateScreenData", cylinder), "cylinder physical half-height contributes to 25 meter range");
        cylinder.Height = 1; Static("ValidateScreenData", cylinder);
        cylinder.SurfaceRadius = 2; cylinder.Height = 1.5; cylinder.SurfaceHorizontal = Math.PI; cylinder.SurfaceError = .04;
        Set(f.Session, "_ticks", 50);
        object[] CylinderArgs() => new object[] { f.Scene, f.Cache, "cylinder", source, cylinder, MatrixD.Identity, 0d, 0d, true, 1, null, null, true };
        var cylinderArgs = CylinderArgs(); var mappedCylinder = (SurfaceMesh)Call(f.Session, "MappedScreenGeometry", cylinderArgs);
        Check(mappedCylinder != null && mappedCylinder.Geometry.Points.Max(point => Math.Abs(point.Y)) <= .75 + 1e-8 &&
              mappedCylinder.Geometry.Points.Max(point => Math.Abs(point.Y)) >= .7,
            "cylinder mapped quad uses physical screen height rather than canvas height");
        var bad = Screen(); bad.SurfaceKind = 2; bad.SurfaceRadius = 2; bad.SurfaceHorizontal = 2 * Math.PI;
        bad.SurfaceVertical = Math.PI; bad.SurfaceError = .00001;
        Set(f.Session, "_ticks", 100);
        object[] MapArgs(string id, HoloProjectedScreenData data) => new object[] { f.Scene, f.Cache, id, source, data, MatrixD.Identity, 0d, 0d, true, 1, null, null, true };
        var first = MapArgs("too-fine", bad);
        Reject(() => Call(f.Session, "MappedScreenGeometry", first), "full sphere finer than shared mapping work limit");
        var failed = ((IDictionary)Field(f.Cache, "Mapped"))["too-fine"];
        Check(failed != null && (int)Field(failed, "RetryTick") == 400 && Field(failed, "Mesh") == null &&
              !string.IsNullOrEmpty((string)Field(failed, "Error")), "curved compile failure records same-input retry deadline");
        Set(f.Session, "_ticks", 101);
        var second = MapArgs("too-fine", bad);
        Check(Call(f.Session, "MappedScreenGeometry", second) == null && (int)second[9] == 1 &&
              (int)Field(f.Session, "_lastCompileTick") == 100,
            "same failed map returns cached error next tick without spending compile budget");
        var good = Screen(); good.SurfaceKind = 2; good.SurfaceRadius = 2; good.SurfaceHorizontal = Math.PI;
        good.SurfaceVertical = Math.PI / 2; good.SurfaceError = .04;
        var third = MapArgs("good", good);
        Check(Call(f.Session, "MappedScreenGeometry", third) is SurfaceMesh && (int)third[9] == 0,
            "different valid curved map may compile while failed map waits for retry");
    }
    static void RasterBudgetInvalidation()
    {
        using var f = new ProviderFixture(); f.RegisterProvider(); f.RegisterBackend();
        f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels());
        Set(f.Cache, "View", f.Build());
        f.Data.ContentRenderer = 1; f.Data.UiRasterWidth = 32; f.Data.UiRasterHeight = 16;
        Call(f.Session, "PrepareScreenUiRaster", f.Scene, f.Data, f.Cache);
        ((IDictionary)Field(f.Cache, "Mapped"))["ui"] = Nested("MappedScreenMesh");
        ((IDictionary)Field(f.Cache, "Mapped"))["source"] = Nested("MappedScreenMesh");
        Call(f.Session, "InvalidateSceneRenderBudget", 20L);
        Check(Field(f.Cache, "UiRaster") == null && Field(f.Cache, "SourceRaster") == null && Field(f.Cache, "View") == null &&
              ((IDictionary)Field(f.Cache, "Mapped")).Count == 0 && f.Released.Count(lease => ReferenceEquals(lease, f.Lease)) == 2,
            "render budget change clears mapped meshes, UI raster, source frame, and both leases");
        Check((double)Field(f.Cache, "NextSample") == -1 && (double)Field(f.Cache, "NextCapture") == -1,
            "budget change schedules fresh UI and source samples");
    }
    static void CurvedMappingAndCleanup()
    {
        var d = Screen(); d.SurfaceKind = 2; d.SurfaceRadius = 2; d.SurfaceHorizontal = Math.PI; d.SurfaceVertical = Math.PI / 2; d.SurfaceSide = 1;
        d.SurfaceMapping = 2; d.ContentRenderer = 1; d.UiRasterWidth = 32; d.UiRasterHeight = 16;
        Static("ValidateScreenData", d);
        var style = (SurfaceStyle)Static("ScreenSurfaceStyle", d, 2d);
        var centre = SurfaceMapping.MapPoint(Vector3D.Zero, 2, 2, style);
        Check(Math.Abs(centre.Z - 2) < 1e-10 && style.Mapping == SurfaceMappingMode.Pinhole && !style.Inward, "screen fields flow to outward pinhole sphere mapper");
        using var f = new ProviderFixture(); f.RegisterProvider(); f.RegisterBackend(); f.Frame = Rgba(f.Proof, new Vector2I(16, 16), Pixels());
        var mesh = f.Build(); Set(f.Cache, "View", mesh);
        Call(f.Session, "ClearProjectedCaches");
        Check(((IDictionary)Field(f.Session, "_projectedCaches")).Count == 0 && f.Released.Contains(f.Lease), "projected cache cleanup releases live raster lease");
        using var gateway = new ClientReplicationTests.GatewayScope();
        var snapshot = ClientReplicationTests.Snapshot(); snapshot.Scenes[0].Screens = new List<HoloProjectedScreenData> { d };
        var model = ProtoBuf.Meta.RuntimeTypeModel.Create(); using var output = new MemoryStream(); model.Serialize(output, snapshot);
        var wire = output.ToArray();
        Check(output.Length < 20000 && !Enumerable.Range(0, Math.Max(0, wire.Length - 15)).Any(i => wire.Skip(i).Take(16).All(b => b == 0xA5)), "scene snapshot carries raster metadata without source RGBA pixel payload");
        var quad = (SurfaceMesh)Static("RasterQuad", 2d, 2d);
        Check(quad.EdgeColors != null && quad.EdgeColors.Length == 0, "raster quad supplies an empty edge-color array accepted by curved surface mapper");
        var mapped = SurfaceMapping.Warp(quad, 2, 2, style);
        Check(mapped.UV.Length == mapped.Geometry.Points.Length && mapped.Geometry.Triangles.Length > quad.Geometry.Triangles.Length, "textured raster quad carries UVs through curved tessellation");
    }
}
