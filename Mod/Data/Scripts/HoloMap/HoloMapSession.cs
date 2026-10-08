using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using VRageMath;
using VRageRender;
using PbBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace HoloMap
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed partial class HoloMapSession : MySessionComponentBase
    {
        const string PropertyId = "HDR.Api";
        const int MaxConsoles = 8, MaxObjects = 16;
        const double MaxDisplayRadius = 25, DrawDistance = 60;
        readonly Dictionary<long, Scene> _scenes = new Dictionary<long, Scene>();
        readonly Dictionary<string, Delegate> _api = new Dictionary<string, Delegate>();
        readonly List<long> _closed = new List<long>();
        bool _registered, _dirty;
        int _ticks;
        static readonly MyStringId LineMaterial = MyStringId.GetOrCompute("HoloMap_Line");
        static readonly MyStringId FillMaterial = MyStringId.GetOrCompute("HoloMap_Fill");

        sealed class Scene
        {
            public long ConsoleId;
            public int PointBudget=int.MaxValue,PrimitiveBudget=int.MaxValue,DrawWorkBudget=20000;
            public readonly Dictionary<string,ProjectedScreen> Screens=new Dictionary<string,ProjectedScreen>();
            public double VolumeRange = 25;
            public bool TableVolumeEnabled = true;
            public double[] TableVolume;
            public Vector3D Offset = new Vector3D(0, 0.8, 0);
            public Vector3D Rotation;
            public double Scale = 1;
            public long TrackedRootId, TrackingCallerId;
            public double ShipRadius = 0.55;
            public Vector3D ShipOffset;
            public double ShipMetresPerUnit;
            public Vector3D ShipOrigin;
            public readonly Dictionary<string, Item> Items = new Dictionary<string, Item>();
            public readonly Dictionary<string, Label> Labels = new Dictionary<string, Label>();
            public readonly Dictionary<string, Layer> Layers = new Dictionary<string, Layer>();
            public string ConstructLayer = "ship"; public int LcdColumns=1,LcdRows=1,LcdColumn,LcdRow;public double LcdWidth=2,LcdHeight=2;public long LcdSourceId,LcdCallerId;public int LcdRenderer;public double LcdRefreshHz=6;
        }

        sealed class Item
        {
            public long CallerId;
            public string Id;
            public Geometry Geometry;
            public Vector4[] TriangleColors;
            public Vector4[] EdgeColors;
            public SurfaceMesh Source;
            public HoloGeometryDeclaration Declaration;public object ContentIdentity;
            public HologramEffectSettings Effects;public double EffectStart;public bool EffectEntering=true;
            public string SvgSource, TextSource, TextAnchor;
            public int SvgSegments; public double TextHeight;
            public int[][] Faces; public Vector3D[][] Contours; public bool EvenOdd;
            public Vector4[] ClipPlanes;
            public GradientStyle Gradient;
            public float Emission;
            public Vector2[] UV;
            public string Material;
            public float Opacity = 1;
            public Vector4 LineColor, FillColor;
            public float Thickness;
            public bool Shaded, Visible = true;
            public string Layer = "";
            public MatrixD Transform = MatrixD.Identity;
        }

        static string Key(long callerId, string id) { return callerId + ":" + id; }

        public override void BeforeStart()
        {
            InitializeClientControls();
            RegisterApi();
            RepairLegacyLcdContent();
            RegisterLcdContentControl();
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NetworkChannel, Receive);
            InitializeUiPointer();
            RegisterUiNetwork();
            RegisterDisplaySources();
            RegisterRasterBackend();
            InitializeModClientApi();
            InitializeRHF();
            if (!MyAPIGateway.Multiplayer.IsServer) RequestSnapshot();
        }

        void RegisterApi()
        {
            if (_registered) return;
            RegisterHologramEffectsApi();
            _api.Add("Version", new Func<string>(() => "0.9.11"));
            _api.Add("ApiVersion", new Func<string>(() => "HDR.Api/1"));
            _api.Add("GetDisplayCapabilities", new Func<PbBlock, PbBlock, MyTuple<string, string, int>>(GetDisplayCapabilities));
            _api.Add("PutWires", new Func<PbBlock, PbBlock, string, Vector3D[], Vector2I[], Vector4, float, MyTuple<bool, string>>(PutWires));
            _api.Add("PutPolygons", new Func<PbBlock, PbBlock, string, Vector3D[], int[][], MyTuple<Vector4, Vector4, float, bool>, MyTuple<bool, string>>(PutPolygons));
            _api.Add("SetView", new Func<PbBlock, PbBlock, Vector3D, Vector3D, double, MyTuple<bool, string>>(SetView));
            _api.Add("SetTransform", new Func<PbBlock, PbBlock, string, MatrixD, MyTuple<bool, string>>(SetTransform));
            _api.Add("SetVisible", new Func<PbBlock, PbBlock, string, bool, MyTuple<bool, string>>(SetVisible));
            _api.Add("Remove", new Func<PbBlock, PbBlock, string, MyTuple<bool, string>>(Remove));
            _api.Add("Clear", new Func<PbBlock, PbBlock, MyTuple<bool, string>>(Clear));
            _api.Add("TrackConstruct", new Func<PbBlock, PbBlock, bool, double, Vector3D, MyTuple<bool, string>>(TrackConstruct));
            _api.Add("TrackConstructWorld", new Func<PbBlock, PbBlock, bool, double, Vector3D, MyTuple<bool, string>>(TrackConstructWorld));
            _api.Add("PutLabel", new Func<PbBlock, PbBlock, string, Vector3D, string, Vector4, float, MyTuple<bool, string>>(PutLabel));
            _api.Add("SetObjectLayer", new Func<PbBlock, PbBlock, string, string, MyTuple<bool, string>>(SetObjectLayer));
            _api.Add("SetConstructLayer", new Func<PbBlock, PbBlock, string, MyTuple<bool, string>>(SetConstructLayer));
            _api.Add("SetLayerVisible", new Func<PbBlock, PbBlock, string, bool, MyTuple<bool, string>>(SetLayerVisible));
            _api.Add("SetLayerOpacity", new Func<PbBlock, PbBlock, string, float, MyTuple<bool, string>>(SetLayerOpacity));
            _api.Add("GetLayerState", new Func<PbBlock, PbBlock, string, MyTuple<bool, string, bool, float>>(GetLayerState));
            RegisterArtworkApi();
            RegisterAppearanceApi();
            RegisterDrawApi();
            RegisterUiApi();
            var property = MyAPIGateway.TerminalControls.CreateProperty<IReadOnlyDictionary<string, Delegate>, IMyProgrammableBlock>(PropertyId);
            // Use the same sandbox-compatible immutable collection as Projector+.
            // System.Collections.ObjectModel.ReadOnlyDictionary is prohibited by SE.
            var apiBuilder = System.Collections.Immutable.ImmutableDictionary.CreateBuilder<string, Delegate>();
            foreach (var method in _api) apiBuilder.Add(method.Key, method.Value);
            var apiView = apiBuilder.ToImmutable();
            property.Getter = block => apiView;
            MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(property);
            _registered = true;
        }

        IMyTerminalBlock Authorize(PbBlock callerBlock, PbBlock consoleBlock)
        {
            var target=AuthorizeAccess(callerBlock,consoleBlock);
            ObserveCaller((IMyProgrammableBlock)callerBlock);
            return target;
        }
        IMyTerminalBlock AuthorizeAccess(PbBlock callerBlock, PbBlock consoleBlock, bool requireDisplay = true)
        {
            if (!MyAPIGateway.Multiplayer.IsServer) throw new ArgumentException("Scene changes execute on the host/server through a PB.");
            var caller = callerBlock as IMyProgrammableBlock;
            var console = consoleBlock as IMyTerminalBlock;
            if (caller == null || caller.Closed || console == null || (requireDisplay && !(console is IMyProjector || console is IMyTextPanel)) || console.Closed)
                throw new ArgumentException("Supply a live programmable block and a Console, Projector or LCD Block.");
            if (!ReferenceEquals(MyAPIGateway.Entities.GetEntityById(caller.EntityId), caller)
                || !ReferenceEquals(MyAPIGateway.Entities.GetEntityById(console.EntityId), console))
                throw new ArgumentException("Blocks must be registered game entities.");
            if (!caller.IsSameConstructAs(console) || caller.OwnerId == 0 || !console.HasPlayerAccess(caller.OwnerId))
                throw new ArgumentException("The PB must own/access the display anchor and be on the same mechanical construct.");
            Scene ownedLcd;
            if(console is IMyTextPanel&&_scenes.TryGetValue(console.EntityId,out ownedLcd)&&ownedLcd.LcdCallerId!=0&&ownedLcd.LcdCallerId!=caller.EntityId)
                throw new ArgumentException("Another PB controls this LCD display.");
            return console;
        }

        Scene GetScene(long id)
        {
            Scene scene;
            if (_scenes.TryGetValue(id, out scene)) return scene;
            if (_scenes.Count >= MaxConsoles) throw new ArgumentException("HDR API supports at most " + MaxConsoles + " active display anchors.");
            scene = new Scene { ConsoleId = id }; var lcd=MyAPIGateway.Entities.GetEntityById(id) as IMyTextPanel;if(lcd!=null){scene.Offset=Vector3D.Zero;}
            _scenes.Add(id, scene);
            return scene;
        }

        static void ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64) throw new ArgumentException("Object ID requires 1 through 64 characters.");
        }
        static void Color(Vector4 color)
        {
            if (!Geometry.Finite(color.X) || !Geometry.Finite(color.Y) || !Geometry.Finite(color.Z) || !Geometry.Finite(color.W)
                || color.X < 0 || color.Y < 0 || color.Z < 0 || color.W < 0
                || color.X > 1 || color.Y > 1 || color.Z > 1 || color.W > 1)
                throw new ArgumentException("RGBA color components must be finite and in [0,1].");
        }
        static void Width(float width)
        {
            if (!Geometry.Finite(width) || width < 0.0001f || width > 0.25f)
                throw new ArgumentException("Line thickness must be 0.0001 through 0.25 display meters.");
        }

        static MatrixD LocalView(Scene scene)
        {
            var rotation = MatrixD.CreateFromYawPitchRoll(scene.Rotation.Y, scene.Rotation.X, scene.Rotation.Z);
            return MatrixD.CreateScale(scene.Scale) * rotation * MatrixD.CreateTranslation(scene.Offset);
        }

        static void FitsDisplay(Geometry geometry, MatrixD transform, Scene scene)
        {
            var view = transform * LocalView(scene);
            foreach (var p in geometry.Points)
            {
                var mapped = Vector3D.Transform(p, view) - scene.Offset;
                if (!Geometry.Finite(mapped.X) || !Geometry.Finite(mapped.Y) || !Geometry.Finite(mapped.Z)
                    || mapped.LengthSquared() > MaxDisplayRadius * MaxDisplayRadius)
                    throw new ArgumentException("Geometry exceeds the 25 meter display radius; reduce the map scale.");
            }
        }

        MyTuple<bool, string> Guard(Action action)
        {
            try { action(); _dirty = true; return new MyTuple<bool, string>(true, ""); }
            catch (ArgumentException ex) { return new MyTuple<bool, string>(false, ex.Message); }
            catch (Exception ex) { MyLog.Default.WriteLineAndConsole("HDR API: " + ex); return new MyTuple<bool, string>(false, "HoloMap error; see the game log."); }
        }

        void Put(PbBlock caller, IMyTerminalBlock console, string id, Geometry geometry, Vector4 line, Vector4 fill, float thickness, bool shaded, MatrixD? initialTransform = null, Vector4[] triangleColors = null, Vector2[] uv = null, string material = null)
        {
            Color(line); Color(fill); Width(thickness);
            var scene = GetScene(console.EntityId);
            string key = Key(caller.EntityId, id);
            Item previous;
            scene.Items.TryGetValue(key, out previous);
            var source = MeshEffects.Solid(geometry,fill,line,triangleColors,null,uv);
            var styled = source; // Appearance is compiled only by each viewing client.
            geometry = styled.Geometry;
            if (previous == null && scene.Items.Count+scene.Screens.Count >= MaxObjects) throw new ArgumentException("Console object limit reached.");
            int points = geometry.Points.Length, primitives = geometry.Edges.Length / 2 + geometry.Triangles.Length / 3;
            int screenPoints,screenPrimitives;ProjectedSourceCounts(scene,out screenPoints,out screenPrimitives);points+=screenPoints;primitives+=screenPrimitives;
            foreach (var entry in scene.Items)
                if (entry.Key != key)
                { points += entry.Value.Geometry.Points.Length; primitives += entry.Value.Geometry.Edges.Length / 2 + entry.Value.Geometry.Triangles.Length / 3; }
            if (points > scene.PointBudget || primitives > scene.PrimitiveBudget) throw new ArgumentException("Console geometry budget exceeded.");
            ValidateSceneSources(scene,new[]{new Item{Geometry=geometry,TriangleColors=triangleColors,UV=uv}},new HashSet<string>{key});
            var transform = initialTransform ?? (previous == null ? MatrixD.Identity : previous.Transform);
            ValidateTransform(transform);
            FitsDisplay(geometry, transform, scene);
            scene.Items[key] = new Item { CallerId = caller.EntityId, Id = id, Geometry = geometry, LineColor = line,
                FillColor = fill, Thickness = thickness, Shaded = shaded, Transform = transform,
                Visible = previous == null || previous.Visible, Layer = previous == null ? (_screenWriteLayerPrefix==null?"":_screenWriteLayerPrefix+"default") : previous.Layer };
            scene.Items[key].Opacity = previous == null ? 1 : previous.Opacity;
            var item=scene.Items[key];item.Source=source;item.ClipPlanes=previous==null?null:previous.ClipPlanes;item.Gradient=previous==null?null:previous.Gradient;
            item.Emission=previous==null?0:previous.Emission;item.Material=material;item.UV=styled.UV;
            item.Effects=previous==null?null:previous.Effects;item.EffectStart=previous==null?0:previous.EffectStart;item.EffectEntering=previous==null||previous.EffectEntering;
            item.TriangleColors=styled.Colors;item.EdgeColors=styled.EdgeColors;
            NotifyUiArtworkChanged(caller.EntityId,console.EntityId,id,false);
            if(console is IMyTextPanel)scene.LcdCallerId=caller.EntityId;
        }

        MyTuple<bool, string> PutWires(PbBlock caller, PbBlock console, string id, Vector3D[] points, Vector2I[] connections, Vector4 color, float width)
        {
            return Guard(() => { var target = Authorize(caller, console); ValidateWriteId(caller,console,id); Put(caller, target, id, Geometry.Wires(points, connections), color, Vector4.Zero, width, false); });
        }
        MyTuple<bool, string> PutPolygons(PbBlock caller, PbBlock console, string id, Vector3D[] points, int[][] faces, MyTuple<Vector4, Vector4, float, bool> style)
        {
            return Guard(() => { var target = Authorize(caller, console); ValidateWriteId(caller,console,id); var copied=ValidateFaces(points,faces);ValidateSceneSources(GetScene(target.EntityId),new[]{new Item{Geometry=new Geometry(points,new int[0],new int[0]),Faces=copied}},new HashSet<string>{Key(caller.EntityId,id)}); Put(caller, target, id, new Geometry(points,new int[0],new int[0]), style.Item1, style.Item2, style.Item3, style.Item4); _scenes[target.EntityId].Items[Key(caller.EntityId,id)].Faces=copied; });
        }
        MyTuple<bool, string> SetView(PbBlock caller, PbBlock console, Vector3D offset, Vector3D rotation, double scale)
        {
            return Guard(() =>
            {
                var target = Authorize(caller, console);
                if (!Geometry.Finite(scale) || scale < 1e-6 || scale > 1000
                    || !Geometry.Finite(offset.X) || !Geometry.Finite(offset.Y) || !Geometry.Finite(offset.Z) || offset.LengthSquared() > 100
                    || !Geometry.Finite(rotation.X) || !Geometry.Finite(rotation.Y) || !Geometry.Finite(rotation.Z))
                    throw new ArgumentException("View requires a finite offset within 10m, radians, and scale in [0.000001,1000].");
                var scene = GetScene(target.EntityId);
                var candidate = new Scene { Offset = offset, Rotation = rotation, Scale = scale };
                UiValidateDisplayViewChange(target,candidate);
                foreach (var item in scene.Items.Values) FitsDisplay(item.Geometry, item.Transform, candidate);
                foreach (var label in scene.Labels.Values)
                    if ((Vector3D.Transform(label.Position, LocalView(candidate)) - candidate.Offset).LengthSquared() > MaxDisplayRadius * MaxDisplayRadius)
                        throw new ArgumentException("Labels exceed the display radius at this zoom.");
                scene.Offset = offset; scene.Rotation = rotation; scene.Scale = scale;
                NotifyUiDisplayViewChanged(target.EntityId);
            });
        }

        Item GetItem(PbBlock caller, PbBlock console, string id)
        {
            var target = Authorize(caller, console); ValidateWriteId(caller,console,id);
            Scene scene; Item item;
            if (!_scenes.TryGetValue(target.EntityId, out scene) || !scene.Items.TryGetValue(Key(caller.EntityId, id), out item))
                throw new ArgumentException("Object ID does not exist for this PB.");
            return item;
        }
        MyTuple<bool, string> SetTransform(PbBlock caller, PbBlock console, string id, MatrixD transform)
        {
            return Guard(() =>
            {
                var item = GetItem(caller, console, id);
                ValidateTransform(transform);
                UiValidateArtworkPoseMutation(caller.EntityId,console.EntityId,id,transform);
                FitsDisplay(item.Geometry, transform, _scenes[console.EntityId]);
                item.Transform = transform;
                NotifyUiArtworkChanged(caller.EntityId,console.EntityId,id,false);
            });
        }
        static void ValidateTransform(MatrixD m)
        {
            foreach (var n in MatrixValues(m)) if (!Geometry.Finite(n)) throw new ArgumentException("Transform must be finite.");
            if (Math.Abs(m.M14) > 1e-12 || Math.Abs(m.M24) > 1e-12 || Math.Abs(m.M34) > 1e-12 || Math.Abs(m.M44 - 1) > 1e-12
                || Math.Abs(m.Determinant()) < 1e-15)
                throw new ArgumentException("Transform must be a nonsingular affine matrix.");
        }
        MyTuple<bool, string> SetVisible(PbBlock caller, PbBlock console, string id, bool visible)
        {
            return Guard(() =>
            {
                var target = Authorize(caller, console); ValidateWriteId(caller,console,id); Scene scene; Item item; Label label;
                if (!_scenes.TryGetValue(target.EntityId, out scene)) throw new ArgumentException("Object ID does not exist.");
                bool found = false;
                if (scene.Items.TryGetValue(Key(caller.EntityId, id), out item)) { item.Visible = visible;NotifyUiArtworkChanged(caller.EntityId,console.EntityId,id,false); found = true; }
                if (scene.Labels.TryGetValue(Key(caller.EntityId, id), out label)) { label.Visible = visible; found = true; }
                if (!found) throw new ArgumentException("Object ID does not exist for this PB.");
            });
        }
        MyTuple<bool, string> Remove(PbBlock caller, PbBlock console, string id)
        {
            return Guard(() => { var target = Authorize(caller, console); ValidateWriteId(caller,console,id); Scene scene;
                RemovePacked(caller.EntityId,target.EntityId,id);
                RemoveDrawAnimation(caller.EntityId,target.EntityId,id);
                if (_scenes.TryGetValue(target.EntityId, out scene)) { scene.Items.Remove(Key(caller.EntityId, id)); scene.Labels.Remove(Key(caller.EntityId, id));NotifyUiArtworkChanged(caller.EntityId,target.EntityId,id,true); } });
        }
        MyTuple<bool, string> Clear(PbBlock caller, PbBlock console)
        {
            return Guard(() =>
            {
                var target = Authorize(caller, console); Scene scene;
                ClearUiDisplay(caller.EntityId,target.EntityId);
                ClearPacked(caller.EntityId,target.EntityId);
                ClearDrawAnimations(caller.EntityId,target.EntityId);
                if (!_scenes.TryGetValue(target.EntityId, out scene)) return;
                var ownedScreens=new List<string>();foreach(var s in scene.Screens)if(s.Value.Data.CallerId==caller.EntityId)ownedScreens.Add(s.Key);foreach(string screenKey in ownedScreens)scene.Screens.Remove(screenKey);
                if (scene.TrackingCallerId == caller.EntityId) { scene.TrackedRootId = 0; scene.TrackingCallerId = 0; }
                var keys = new List<string>();
                foreach (var entry in scene.Items) if (entry.Value.CallerId == caller.EntityId) keys.Add(entry.Key);
                foreach (var key in keys) scene.Items.Remove(key);
                keys.Clear();
                foreach (var entry in scene.Labels) if (entry.Value.CallerId == caller.EntityId) keys.Add(entry.Key);
                foreach (var key in keys) scene.Labels.Remove(key);
                keys.Clear();
                foreach (var entry in scene.Layers) if (entry.Value.CallerId == caller.EntityId) keys.Add(entry.Key);
                foreach (var key in keys) scene.Layers.Remove(key);
            });
        }

        public override void UpdateAfterSimulation()
        {
            _ticks++;
            TickCallerLifetimes();
            if (_ticks % 60 == 0 && MyAPIGateway.Multiplayer.IsServer)
            {
                _closed.Clear();
                foreach (var scene in _scenes.Values)
                {
                    var console = MyAPIGateway.Entities.GetEntityById(scene.ConsoleId) as IMyTerminalBlock;
                    if (console == null || !(console is IMyProjector || console is IMyTextPanel) || console.Closed) { _closed.Add(scene.ConsoleId); continue; }
                    PruneProjectedDeclarations(scene,console);
                    var keys = new List<string>();
                    foreach (var entry in scene.Items)
                    {
                        var caller = MyAPIGateway.Entities.GetEntityById(entry.Value.CallerId) as IMyProgrammableBlock;
                        if (caller == null || caller.Closed || !caller.IsSameConstructAs(console)
                            || caller.OwnerId == 0 || !console.HasPlayerAccess(caller.OwnerId)) keys.Add(entry.Key);
                    }
                    foreach (var key in keys) { scene.Items.Remove(key); _dirty = true; }
                    var trackingCaller = MyAPIGateway.Entities.GetEntityById(scene.TrackingCallerId) as IMyProgrammableBlock;
                    if (scene.TrackedRootId != 0 && (trackingCaller == null || trackingCaller.Closed
                        || !trackingCaller.IsSameConstructAs(console) || !console.HasPlayerAccess(trackingCaller.OwnerId)))
                    { scene.TrackedRootId = 0; scene.TrackingCallerId = 0; _dirty = true; }
                    keys.Clear();
                    foreach (var entry in scene.Labels)
                    {
                        var caller = MyAPIGateway.Entities.GetEntityById(entry.Value.CallerId) as IMyProgrammableBlock;
                        if (caller == null || caller.Closed || !caller.IsSameConstructAs(console) || caller.OwnerId == 0 || !console.HasPlayerAccess(caller.OwnerId)) keys.Add(entry.Key);
                    }
                    foreach (var key in keys) { scene.Labels.Remove(key); _dirty = true; }
                    keys.Clear();
                    foreach (var entry in scene.Layers)
                    {
                        var caller = MyAPIGateway.Entities.GetEntityById(entry.Value.CallerId) as IMyProgrammableBlock;
                        if (caller == null || caller.Closed || !caller.IsSameConstructAs(console) || caller.OwnerId == 0 || !console.HasPlayerAccess(caller.OwnerId)) keys.Add(entry.Key);
                    }
                    foreach (var key in keys) { scene.Layers.Remove(key); _dirty = true; }
                    if(scene.LcdCallerId!=0)
                    {
                        var lcdCaller=MyAPIGateway.Entities.GetEntityById(scene.LcdCallerId) as IMyProgrammableBlock;
                        if(lcdCaller==null||lcdCaller.Closed||lcdCaller.OwnerId==0||!lcdCaller.IsSameConstructAs(console)||!console.HasPlayerAccess(lcdCaller.OwnerId))
                        {_closed.Add(scene.ConsoleId);continue;}
                    }
                    bool uiDefinition=false;foreach(var ui in _uiDisplays.Values)if(ui.TargetId==scene.ConsoleId){uiDefinition=true;break;}
                    if (scene.Items.Count == 0 && scene.Labels.Count == 0 && scene.Layers.Count == 0 && scene.Screens.Count==0 && scene.TrackedRootId == 0 && scene.LcdCallerId==0 && !uiDefinition) _closed.Add(scene.ConsoleId);
                }
                foreach (long id in _closed) { _scenes.Remove(id); _dirty = true; }
                CleanupPacked();
            }
            TickUiState();
            TickUiValueState();
            TickUiNetwork();


            TickNetwork();
            TickDrawAnimations();
            TickRHF();
            TickUiClient();
            TickUiUseObjects();
            TickUiValueWakes();
            UpdateConstructPreviews(true);
        }

        public override void Draw()
        {
            if (!ClientRenderingEnabled) ModClientMaintainSourceSlots();
            if (!ClientRenderingEnabled || MyAPIGateway.Utilities.IsDedicated || MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;
            _effectDrawNow=AnimationNow/60d;_effectDrawActive=true;
            try
            {
            _volumeClipWork = 120000;
            UpdateConstructPreviews(false);
            var camera = MyAPIGateway.Session.Camera.Position;
            int budget = ClientDrawWorkCap, compileBudget = 1;
            int modGrant = ClientBudgetPartitions.ModGrant(budget, ClientBlockDrawDemand(camera));
            int modRemaining = modGrant;
            DrawModClientApi(ref modRemaining);
            budget -= modGrant - modRemaining;
            BeginDisplaySourceDraw();try
            {
            PruneClientGeometry();
            PruneLocalLcdFrames();
            PruneProjectedCaches();
            foreach (var scene in _scenes.Values)
            {
                var console = MyAPIGateway.Entities.GetEntityById(scene.ConsoleId) as IMyTerminalBlock;
                if (console == null || !(console is IMyProjector || console is IMyTextPanel) || console.Closed || !console.IsWorking || Vector3D.DistanceSquared(camera, console.GetPosition()) > DrawDistance * DrawDistance) continue;
                int reservedDrawWork=Math.Max(0,budget-scene.DrawWorkBudget);budget-=reservedDrawWork;try
                {
                if(console is IMyTextPanel){DrawLcdDisplay(scene,(IMyTextPanel)console,ref budget,ref compileBudget);continue;}
                DrawProjectedScreens(scene,console,ref budget,ref compileBudget);var view = LocalView(scene) * console.WorldMatrix;
                uint anchorId = console.Render.GetRenderObjectID();
                var anchorInverse = MatrixD.Invert(console.WorldMatrix);
                var volume = GetDisplayVolume(scene, console);
                DrawLabels(scene, console.WorldMatrix, MyAPIGateway.Session.Camera.WorldMatrix, anchorId, ref budget);
                foreach (var canonical in scene.Items.Values)
                {
                    if(IsProjectedId(canonical.Id)||!canonical.Visible||ClientLayerAlpha(scene,canonical.CallerId,canonical.Layer)<=0)continue;
                    var animated=GetAnimatedItem(scene.ConsoleId,canonical);
                    var item=ClientDisplayItem(scene,animated,ref compileBudget);
                    if(item==null)continue;
                    float opacity = ClientLayerAlpha(scene, item.CallerId, item.Layer) * item.Opacity;
                    if (!item.Visible || opacity <= 0) continue;
                    DrawHologramItem(item,item.Transform*view,volume,opacity,console.WorldMatrix,anchorInverse,camera,anchorId,ref budget);
                }
                // Optional effects consume only the remaining allowance, after all canonical items.
                DrawProjectedScreenEffects(scene,console,ref budget,ref compileBudget);
                foreach(var canonical in scene.Items.Values)
                {
                    if(budget<=0)break;if(IsProjectedId(canonical.Id)||!canonical.Visible||canonical.Effects==null||ClientLayerAlpha(scene,canonical.CallerId,canonical.Layer)<=0)continue;
                    var item=ClientDisplayItem(scene,GetAnimatedItem(scene.ConsoleId,canonical),ref compileBudget);if(item==null)continue;
                    float opacity=ClientLayerAlpha(scene,item.CallerId,item.Layer)*item.Opacity;
                    DrawHologramExtras(item,item.Transform*view,volume,opacity,console.WorldMatrix,anchorInverse,camera,anchorId,ref budget);
                }
                }finally{budget+=reservedDrawWork;}
            }
            DrawUiHover(ref budget);
            DrawUiFocus(ref budget, ref compileBudget);
            }finally{EndDisplaySourceDraw();}
            }finally{_effectDrawActive=false;}
        }
        bool ClientBlockDrawDemand(Vector3D camera)
        {
            if (UiFocusActive) return true;
            foreach (var scene in _scenes.Values)
            {
                if (scene.Items.Count == 0 && scene.Labels.Count == 0 && scene.Screens.Count == 0 && scene.TrackedRootId == 0) continue;
                var target = MyAPIGateway.Entities.GetEntityById(scene.ConsoleId) as IMyTerminalBlock;
                if (target != null && !target.Closed && target.IsWorking && Vector3D.DistanceSquared(camera, target.GetPosition()) <= DrawDistance * DrawDistance) return true;
            }
            return false;
        }
        static Vector4 Premultiply(Vector4 color, float brightness)
        { return new Vector4(color.X * color.W * brightness, color.Y * color.W * brightness, color.Z * color.W * brightness, color.W); }

        protected override void UnloadData()
        {
            UnloadModClientApi();
            ResetPluginNotices();
            UnregisterRasterBackend();
            UnregisterDisplaySources();

            ClearUiUseObjects();
            ClearUiClientLocal();
            UnregisterUiPointer();
            UnregisterRHF();
            UnregisterUiNetwork();
            ClearUiState();
            UnregisterLcdContentControl();
            ClearLocalLcdFrames();
            UnloadClientControls();
            CloseConstructPreviews();
            if (MyAPIGateway.Multiplayer != null) MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(NetworkChannel, Receive);
            if (_registered)
            {
                List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls;
                MyAPIGateway.TerminalControls.GetControls<IMyProgrammableBlock>(out controls);
                foreach (var control in controls) if (control.Id == PropertyId || control.Id == DrawPropertyId || control.Id == UiPropertyId) MyAPIGateway.TerminalControls.RemoveControl<IMyProgrammableBlock>(control);
            }
            _scenes.Clear(); _api.Clear(); _assemblies.Clear(); _requestTicks.Clear(); _outgoing.Clear(); _registered = false;
            _packed.Clear();
            ClearServerPackedBudget();
            ClearClientAnimations();
            ResetNetworkState();
            ClearClientGeometry();
            _drawAnimations.Clear();_drawContexts.Clear();_callerPrograms.Clear();
        }
    }
}
