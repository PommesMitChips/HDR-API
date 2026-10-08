using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HoloMap;
using Sandbox.ModAPI;
using VRageMath;

// Stage-only integration tests of the actual authored-hotzone picker. The native
// runtime probe includes this file; the pure wire/transaction probe excludes it.
internal static class UiDragHitTests
{
    static int checks;
    static void Check(bool condition, string label)
    { if (!condition) throw new Exception("Staged control hit: " + label); checks++; }
    static void Reject(Action action, string label)
    {
        try { action(); }
        catch (ArgumentException) { checks++; return; }
        throw new Exception("Staged control hit expected rejection: " + label);
    }
    static object Call(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    static object Field(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value);
    static bool Close(Vector3D a, Vector3D b) => Vector3D.DistanceSquared(a, b) < 1e-16;

    sealed class Fixture : IDisposable
    {
        readonly List<(MemberInfo Member, object Prior)> prior = new();
        readonly ProtoBuf.Meta.RuntimeTypeModel serializer = ProtoBuf.Meta.RuntimeTypeModel.Create();
        public readonly HoloMapSession Session = new();
        public readonly Dictionary<long, object> Registry = new();
        public readonly Dictionary<long, MatrixD> Worlds = new();
        public string Script = "HDRAPI";
        public bool Working = true;
        public readonly bool Lcd;
        public Fixture(bool lcd = false)
        {
            Lcd = lcd;
            Registry[10] = UiDragHitProxy.Make(typeof(IMyProgrammableBlock), (m, a) => m.Name switch
            {
                "get_EntityId" => 10L, "get_OwnerId" => 77L, "get_Closed" => false,
                "get_IsWorking" => true, "get_ProgramData" => "hotzone-owner",
                "IsSameConstructAs" => true, "HasPlayerAccess" => true,
                _ => throw new Exception("Hotzone PB gateway: " + m.Name)
            });
            AddDisplay(20, MatrixD.Identity);
            Install("Multiplayer", (m, a) => m.Name == "get_IsServer" ? true : throw new Exception("Hotzone multiplayer: " + m.Name));
            Install("Entities", (m, a) => m.Name == "GetEntityById" ? Registry.GetValueOrDefault((long)a[0]) : throw new Exception("Hotzone entities: " + m.Name));
            Install("Utilities", (m, a) =>
            {
                if (m.Name == "get_IsDedicated") return true;
                if (m.Name == "SerializeToBinary")
                { using var output = new MemoryStream(); serializer.Serialize(output, a[0]); return output.ToArray(); }
                if (m.Name == "SerializeFromBinary")
                { using var input = new MemoryStream((byte[])a[0]); return serializer.Deserialize(input, null, m.GetGenericArguments()[0]); }
                throw new Exception("Hotzone utilities: " + m.Name);
            });
        }
        public void AddDisplay(long id, MatrixD world)
        {
            Worlds[id] = world;
            Registry[id] = UiDragHitProxy.Make(Lcd ? typeof(IMyTextPanel) : typeof(IMyProjector), (m, a) => m.Name switch
            {
                "get_EntityId" => id, "get_Closed" => false, "get_IsWorking" => Working,
                "HasPlayerAccess" => true, "get_WorldMatrix" => Worlds[id], "GetPosition" => Worlds[id].Translation,
                "get_Script" => Script, "get_ContentType" => VRage.Game.GUI.TextPanel.ContentType.SCRIPT,
                "get_Model" => null, "get_LocalAABB" => m.ReturnType == typeof(BoundingBoxD)
                    ? (object)new BoundingBoxD(new Vector3D(-1, -1, -.1), new Vector3D(1, 1, .1))
                    : new BoundingBox(new Vector3(-1, -1, -.1f), new Vector3(1, 1, .1f)),
                "get_BlockDefinition" => Activator.CreateInstance(m.ReturnType),
                _ => throw new Exception("Hotzone display gateway: " + m.Name)
            });
        }
        void Install(string name, Func<MethodInfo, object[], object> handler)
        {
            MemberInfo member = (MemberInfo)typeof(MyAPIGateway).GetField(name) ?? typeof(MyAPIGateway).GetProperty(name);
            object old = member is FieldInfo field ? field.GetValue(null) : ((PropertyInfo)member).GetValue(null);
            Type type = member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;
            prior.Add((member, old)); Write(member, UiDragHitProxy.Make(type, handler));
        }
        static void Write(MemberInfo member, object value)
        { if (member is FieldInfo field) field.SetValue(null, value); else ((PropertyInfo)member).SetValue(null, value); }
        public object Scene(long id = 20) => ((IDictionary)Field(Session, "_scenes"))[id];
        public UiDisplay Display => (UiDisplay)Call(Session, "GetUiDisplay", 10L, 20L);
        public UiWidget Widget => Display.Widgets.Single();
        public object Artwork => ((IDictionary)Field(Scene(), "Items"))["10:handle"];
        public void Setup(MatrixD pose, double x = 0, double y = 0, double width = 1, double height = 1)
        {
            var draw = (Func<string, object[], object>)Call(Session, "DrawEndpoint", Registry[10]);
            draw("target", new[] { Registry[20] });
            draw("line", new object[] { "handle", new Vector3D(-.5, 0, 0), new Vector3D(.5, 0, 0), "cyan" });
            draw("transform", new object[] { "handle", pose });
            var ui = (Func<string, object[], object>)Call(Session, "UiEndpoint", Registry[10]);
            ui("target", new[] { Registry[20] }); ui("bundle", new object[] { "main" });
            ui("control", new object[] { "control", "main", "handle", x, y, width, height });
            Set(Scene(), "Offset", Vector3D.Zero);
            Set(Scene(), "VolumeRange", 0d);
        }
        public bool Hit(Vector3D origin, Vector3D direction, out Vector3D hit, out long tile)
        {
            var args = new object[] { Display, Widget, origin, direction, Vector3D.Zero, 0L };
            bool found = (bool)Call(Session, "TryUiControlHit", args); hit = (Vector3D)args[4]; tile = (long)args[5]; return found;
        }
        public MatrixD Mapping(long tile = 20)
        {
            var args = new object[] { Display, tile, MatrixD.Identity };
            Check((bool)Call(Session, "UiDragWorldMapping", args), "valid display exposes shared drag mapping");
            return (MatrixD)args[2];
        }
        public void Tile(long id, int column, MatrixD world)
        {
            AddDisplay(id, world);
            var tile = Call(Session, "GetScene", id);
            foreach (object scene in new[] { Scene(), tile })
            { Set(scene, "LcdWidth", 2d); Set(scene, "LcdHeight", 2d); Set(scene, "LcdColumns", 2); Set(scene, "LcdRows", 1); }
            Set(tile, "LcdSourceId", 20L); Set(tile, "LcdColumn", column);
        }
        public void Dispose() { for (int i = prior.Count - 1; i >= 0; i--) Write(prior[i].Member, prior[i].Prior); }
    }

    public static int Run()
    {
        checks = 0;
        AttachedAffineHotzone();
        LcdFlatteningAndTileCrop();
        LcdNearestPlane();
        CurrentSourceAndRange();
        return checks;
    }
    static void AttachedAffineHotzone()
    {
        using var f = new Fixture();
        var pose = MatrixD.Identity;
        pose.Right = new Vector3D(1.5, .25, 0); pose.Up = new Vector3D(.5, 1, 0);
        pose.Translation = new Vector3D(.8, -.4, .2);
        f.Setup(pose);
        Set(f.Scene(), "Rotation", new Vector3D(.1, .2, .3)); Set(f.Scene(), "Scale", .75);
        f.Worlds[20] = MatrixD.CreateRotationY(.4) * MatrixD.CreateTranslation(.2, .3, -.1);
        var mapping = pose * f.Mapping();
        var expected = Vector3D.Transform(new Vector3D(.3, -.2, 0), mapping);
        var normal = Vector3D.Normalize(Vector3D.Cross(Vector3D.TransformNormal(Vector3D.UnitX, mapping), Vector3D.TransformNormal(Vector3D.UnitY, mapping)));
        Check(f.Hit(expected + normal, -normal, out var hit, out long tile) && tile == 20 && Close(hit, expected),
            "hotzone follows item affine shear, source view, and block world transform");
        var outside = Vector3D.Transform(new Vector3D(.51, 0, 0), mapping);
        Check(!f.Hit(outside + normal, -normal, out _, out _), "authored hotzone boundary rejects an outside point after affine mapping");
        Check(f.Hit(expected - normal, normal, out _, out _), "world projector hotzone supports the renderer's two-sided plane");
    }
    static void LcdFlatteningAndTileCrop()
    {
        using var f = new Fixture(true);
        f.Setup(MatrixD.Identity, .75, 0, .4, .4);
        f.Tile(30, 1, MatrixD.CreateTranslation(2, 0, 0));
        var mapping = f.Mapping(30);
        Check(mapping.Backward.LengthSquared() == 0 && Math.Abs(mapping.Determinant()) < 1e-15,
            "LCD drag mapping explicitly discards canvas Z");
        var expected = Vector3D.Transform(new Vector3D(.75, 0, 0), mapping);
        Check(Close(Vector3D.Transform(new Vector3D(.75, 0, 5), mapping), expected),
            "physical LCD mapping flattens canvas depth without an inverse");
        Check(f.Hit(expected + Vector3D.UnitZ, -Vector3D.UnitZ, out var hit, out long tile) && tile == 30 && Close(hit, expected),
            "tiled LCD selects the physical panel showing the planar artwork hotzone");
        var offPanel = Vector3D.Transform(new Vector3D(.75, 0, 0), f.Mapping(20));
        f.Registry.Remove(30);
        Check(!f.Hit(offPanel + Vector3D.UnitZ, -Vector3D.UnitZ, out _, out _),
            "master tile cannot claim a hotzone outside its own physical screen");
        f.Script = "OtherScript";
        var args = new object[] { f.Display, 20L, MatrixD.Identity };
        Check(!(bool)Call(f.Session, "UiDragWorldMapping", args), "LCD without active HDR script cannot provide interaction mapping");
    }
    static void CurrentSourceAndRange()
    {
        using var f = new Fixture(); f.Setup(MatrixD.Identity);
        Check(f.Hit(new Vector3D(0, 0, 3), -Vector3D.UnitZ, out _, out _), "three meter interaction range is inclusive");
        Check(!f.Hit(new Vector3D(0, 0, 3.0001), -Vector3D.UnitZ, out _, out _), "ray beyond interaction range is rejected");
        Check(!f.Hit(new Vector3D(0, 0, 1), Vector3D.Zero, out _, out _), "zero ray direction is rejected");
        Check(!f.Hit(new Vector3D(double.NaN, 0, 1), -Vector3D.UnitZ, out _, out _), "nonfinite ray origin is rejected");
        Set(f.Scene(), "VolumeRange", .1d);
        Check(!f.Hit(new Vector3D(.4, 0, 1), -Vector3D.UnitZ, out _, out _), "projector volume crops an otherwise valid authored hotzone");
        Set(f.Scene(), "VolumeRange", 0d);
        f.Working = false; Check(!f.Hit(new Vector3D(0, 0, 1), -Vector3D.UnitZ, out _, out _), "unpowered display rejects the hotzone");
        f.Working = true; Set(f.Artwork, "Visible", false);
        Check(!f.Hit(new Vector3D(0, 0, 1), -Vector3D.UnitZ, out _, out _), "hidden owned artwork rejects the hotzone");
        Set(f.Artwork, "Visible", true); Set(f.Artwork, "Opacity", 0f);
        Check(!f.Hit(new Vector3D(0, 0, 1), -Vector3D.UnitZ, out _, out _), "transparent owned artwork rejects the hotzone");
        Set(f.Artwork, "Opacity", 1f);
        ((IDictionary)Field(f.Scene(), "Items")).Remove("10:handle");
        Check(!f.Hit(new Vector3D(0, 0, 1), -Vector3D.UnitZ, out _, out _), "removed owned artwork rejects the hotzone");
    }
    static void LcdNearestPlane()
    {
        using var f = new Fixture(true); f.Setup(MatrixD.Identity);
        f.Tile(30, 1, MatrixD.CreateTranslation(2, 0, .05));
        var origin = new Vector3D(1, 0, 1);
        Check(f.Hit(origin, -Vector3D.UnitZ, out _, out long tile) && tile == 30,
            "overlapping authored hotzones select the nearest physical LCD plane");
        f.Worlds[20] = MatrixD.CreateTranslation(0, 0, .1);
        Check(f.Hit(origin, -Vector3D.UnitZ, out _, out tile) && tile == 20,
            "nearest selection follows current panel world pose");
        f.Worlds[20] = MatrixD.CreateTranslation(0, 0, .05);
        Check(f.Hit(origin, -Vector3D.UnitZ, out _, out tile) && tile == 20,
            "equal-distance tile boundary selects the lower entity id deterministically");
        using var collapsed = new Fixture(true);
        Reject(() => collapsed.Setup(MatrixD.CreateRotationX(Math.PI / 2)),
            "LCD control pose collapsed to a line is rejected at admission");
        using var depth = new Fixture(true);
        Reject(() => depth.Setup(MatrixD.CreateTranslation(0, 0, 5)),
            "LCD artwork outside the canvas plane is rejected at admission");
    }
}

public class UiDragHitProxy : DispatchProxy
{
    public Func<MethodInfo, object[], object> Handler;
    protected override object Invoke(MethodInfo method, object[] args) => Handler(method, args);
    public static object Make(Type type, Func<MethodInfo, object[], object> handler)
    { var value = Create(type, typeof(UiDragHitProxy)); ((UiDragHitProxy)value).Handler = handler; return value; }
}
