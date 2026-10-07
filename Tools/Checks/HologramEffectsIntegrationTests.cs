using System.Collections;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

// Exercise the real PB endpoint, scene ownership and wire receiver. Gateway
// doubles substitute game entities/transport; no effects implementation is mocked.
internal static class HologramEffectsIntegrationTests
{
    static int checks;
    static void Check(bool condition, string why)
    { if (!condition) throw new Exception("Hologram effects integration: " + why); checks++; }
    static void Reject(Action action, string why)
    { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("Expected effects rejection: " + why); }
    static object Call(object target, string name, params object[] args) => ClientReplicationTests.Call(target, name, args);
    static object Field(object target, string name) => ClientReplicationTests.Field(target, name);
    static void Set(object target, string name, object value) => ClientReplicationTests.SetField(target, name, value);
    static object Static(string name, params object[] args)
    {
        try { return typeof(HoloMapSession).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    static MyTuple<double[], int[]> Descriptor(Action<double[]> values = null, Action<int[]> flags = null)
    {
        var v = HologramEffectSettings.DefaultValues(); var f = HologramEffectSettings.DefaultFlags();
        values?.Invoke(v); flags?.Invoke(f); return new MyTuple<double[], int[]>(v, f);
    }
    static HologramEffectSettings Effects(object item) => (HologramEffectSettings)Field(item, "Effects");
    static void SameEffects(object item, HologramEffectSettings expected, string why)
    { Check(Effects(item) != null && Effects(item).Equivalent(expected), why); }

    sealed class Fixture : IDisposable
    {
        public readonly ClientReplicationTests.GatewayScope Gateway = new ClientReplicationTests.GatewayScope { Server = true };
        public readonly HoloMapSession Session = new HoloMapSession();
        public readonly Dictionary<long, object> Registry = new Dictionary<long, object>();
        public bool Access = true, SameConstruct = true;
        public Fixture()
        {
            foreach (long id in new[] { 10L, 11L })
                Registry[id] = DrawTestProxy.Make(typeof(IMyProgrammableBlock), (m, a) => m.Name switch
                {
                    "get_EntityId" => id, "get_OwnerId" => 77L, "get_Closed" => false,
                    "IsSameConstructAs" => SameConstruct, _ => throw new Exception("Effects caller " + m.Name)
                });
            Registry[20] = DrawTestProxy.Make(typeof(IMyProjector), (m, a) => m.Name switch
            {
                "get_EntityId" => 20L, "get_Closed" => false, "HasPlayerAccess" => Access,
                _ => throw new Exception("Effects anchor " + m.Name)
            });
            Gateway.Install("Entities", (m, a) => m.Name == "GetEntityById" && Registry.TryGetValue((long)a[0], out var value) ? value : null);
        }
        public Func<string, object[], object> Draw(long owner = 10)
        {
            var draw = (Func<string, object[], object>)Call(Session, "DrawEndpoint", Registry[owner]);
            draw("target", new[] { Registry[20] }); return draw;
        }
        public void AddLcd(long id)
        {
            Registry[id] = DrawTestProxy.Make(typeof(IMyTextPanel), (m, a) => m.Name switch
            {
                "get_EntityId" => id, "get_Closed" => false, "HasPlayerAccess" => Access,
                _ => throw new Exception("Effects LCD " + m.Name)
            });
        }
        public object Scene => ((IDictionary)Field(Session, "_scenes"))[20L];
        public IDictionary Items => (IDictionary)Field(Scene, "Items");
        public object Item(string id = "shape", long owner = 10) => Items[owner + ":" + id];
        public MyTuple<bool, string> Typed(string method, string id, params object[] args)
        {
            var input = new List<object> { Registry[10], Registry[20], id }; input.AddRange(args);
            return (MyTuple<bool, string>)Call(Session, method, input.ToArray());
        }
        public MyTuple<double[], int[]> Query(string id = "shape") =>
            (MyTuple<double[], int[]>)Call(Session, "GetHologramEffects", Registry[10], Registry[20], id);
        public void Dispose() => Gateway.Dispose();
    }
    static void Line(Func<string, object[], object> draw, string id = "shape") =>
        draw("line", new object[] { id, Vector3D.Zero, Vector3D.UnitX, "cyan" });
    static void Screen(Func<string, object[], object> draw, string id) =>
        draw("screen", new object[] { id, MatrixD.Identity, 2d, 1d, 2d, 1d });

    public static int Run()
    {
        checks = 0; Commands(); AuthorizationAndIsolation(); LcdBoundary(); RasterBoundary(); ProjectedCapabilities(); Replication(); ClonesAndCompilation(); PackedAnimationRetention(); RenderPlanning(); AcceptedBoundsClosure(); PrimitiveSafety(); return checks;
    }

    static void Commands()
    {
        using var f = new Fixture(); var draw = f.Draw(); Line(draw);
        var originalGeometry = Field(f.Item(), "Geometry");
        draw("effect", new object[] { "shape", "flicker", .08, 12d });
        Check(Effects(f.Item()).FlickerStrength == .08 && Effects(f.Item()).FlickerHz == 12, "flicker command reaches retained item settings");
        draw("effect", new object[] { "shape", "scan", .35, .3, .04, .5 });
        Check(Effects(f.Item()).ScanStrength == .35 && Effects(f.Item()).ScanHz == .3 && Effects(f.Item()).ScanWidth == .04 && Effects(f.Item()).ScanBoost == .5, "refresh bar command sets independent scan fields");
        draw("effect", new object[] { "shape", "volume", .04, 4, .5 });
        Check(Effects(f.Item()).Depth == .04 && Effects(f.Item()).DepthLayers == 4 && Effects(f.Item()).DepthFalloff == .5, "flat volume command sets ghost depth and falloff");
        draw("effect", new object[] { "shape", "particles", 32, .008, .15, 2d, .05, .5, 41 });
        Check(Effects(f.Item()).Particles == 32 && Effects(f.Item()).ParticleSize == .008 && Effects(f.Item()).ParticleSpeed == .15 && Effects(f.Item()).ParticleLifetime == 2 && Effects(f.Item()).ParticleRadius == .05 && Effects(f.Item()).ParticleOpacity == .5 && Effects(f.Item()).Seed == 41, "particle declaration stores all explicit values");
        draw("effect", new object[] { "shape", "beams", 8, .003, .3, new Vector3D(0, -.5, 0), .02 });
        Check(Effects(f.Item()).Beams == 8 && Effects(f.Item()).BeamWidth == .003 && Effects(f.Item()).BeamOpacity == .3 && Effects(f.Item()).Emitter == new Vector3D(0, -.5, 0) && Effects(f.Item()).BeamJitter == .02, "projector beams retain the local emitter");
        Check(Effects(f.Item()).FlickerStrength == .08 && Effects(f.Item()).Particles == 32 && ReferenceEquals(originalGeometry, Field(f.Item(), "Geometry")), "effect composition preserves prior channels and source mesh");
        draw("effect", new object[] { "shape", "rays", 6, .06, .15, Vector3D.Zero, .01 });
        Check(Effects(f.Item()).Beams == 6 && Effects(f.Item()).BeamWidth == .06 && Effects(f.Item()).BeamOpacity == .15, "light rays use the beam channel with explicit width/opacity");
        draw("effect", new object[] { "shape", "budget", 64 });
        Check(Effects(f.Item()).PrimitiveLimit == 64, "per-object effect work ceiling is configurable");
        var composed = Effects(f.Item());
        foreach (var args in new[]
        {
            new object[] { "shape", "flicker", double.NaN, 12d },
            new object[] { "shape", "scan", .35, .3, 0d },
            new object[] { "shape", "volume", .04, 33 },
            new object[] { "shape", "particles", 4097 },
            new object[] { "shape", "beams", 2049 },
            new object[] { "shape", "budget", 65537 },
            new object[] { "shape", "unknown" },
            new object[] { "shape", "flicker", .08, 12d, "extra" }
        })
        {
            Reject(() => draw("effect", args), "invalid or surplus effect arguments");
            SameEffects(f.Item(), composed, "rejected command is atomic");
        }
        draw("effect-clear", new object[] { "shape", "scan" });
        Check(Effects(f.Item()).ScanStrength == 0 && Effects(f.Item()).FlickerStrength == .08 && Effects(f.Item()).Particles == 32, "clearing one channel preserves siblings");
        draw("effect-clear", new object[] { "shape" });
        Check(Effects(f.Item()) == null || Effects(f.Item()).Equivalent(HologramEffectSettings.Create(HologramEffectSettings.DefaultValues(), HologramEffectSettings.DefaultFlags())), "clearing effects restores neutral content");

        var raw = Descriptor(v => { v[0] = .4; v[6] = .1; }, q => { q[2] = 3; q[7] = 80; });
        var expected = HologramEffectSettings.Create(raw.Item1, raw.Item2);
        draw("effect", new object[] { "shape", "raw", raw });
        raw.Item1[0] = .9; raw.Item2[2] = 10;
        SameEffects(f.Item(), expected, "raw tuple admission detaches caller-owned arrays");
        var queried = f.Query(); queried.Item1[0] = 1; queried.Item2[2] = 32;
        SameEffects(f.Item(), expected, "typed query cannot mutate retained effects");
        var replacement = Descriptor(v => v[0] = .2);
        Check(f.Typed("SetHologramEffects", "shape", replacement).Item1, "typed setter accepts sandbox-compatible tuple");
        replacement.Item1[0] = .7;
        Check(Effects(f.Item()).FlickerStrength == .2, "typed admission is detached");
        var rejected = f.Typed("SetHologramEffects", "shape", new MyTuple<double[], int[]>(new double[1], new int[1]));
        Check(!rejected.Item1 && !string.IsNullOrWhiteSpace(rejected.Item2) && Effects(f.Item()).FlickerStrength == .2, "typed invalid descriptor returns a safe failure and preserves state");

        Set(f.Session, "_ticks", 120);
        draw("transition", new object[] { "shape", "in", "fade", .5 });
        Check(Effects(f.Item()).Transition == 1 && Effects(f.Item()).TransitionSeconds == .5 && (bool)Field(f.Item(), "EffectEntering") && (double)Field(f.Item(), "EffectStart") == 2, "enter transition latches the server time once");
        Set(f.Session, "_ticks", 180);
        Check((double)Field(f.Item(), "EffectStart") == 2, "ordinary clock advance cannot restart a transition");
        draw("transition", new object[] { "shape", "out", "wipe", .75 });
        Check(Effects(f.Item()).Transition == 2 && !(bool)Field(f.Item(), "EffectEntering") && (double)Field(f.Item(), "EffectStart") == 3, "exit transition explicitly restarts its local retained clock");
        Check(f.Typed("SetHologramTransition", "shape", true, "dissolve", 1d).Item1 && Effects(f.Item()).Transition == 3, "typed transition supports dissolve");
        foreach (var args in new[] { new object[] { "shape", "sideways", "fade", .5 }, new object[] { "shape", "in", "unknown", .5 }, new object[] { "shape", "in", "fade", double.NaN } })
            Reject(() => draw("transition", args), "invalid transition mode/duration");
        Check(f.Typed("ClearHologramEffects", "shape").Item1, "typed clear succeeds");
        var defaults = f.Query();
        Check(defaults.Item1.SequenceEqual(HologramEffectSettings.DefaultValues()) && defaults.Item2.SequenceEqual(HologramEffectSettings.DefaultFlags()), "cleared query returns detached neutral defaults");
    }

    static void AuthorizationAndIsolation()
    {
        using var f = new Fixture(); var draw = f.Draw(); Line(draw); var other = f.Draw(11); Line(other);
        draw("effect", new object[] { "shape", "flicker", .2, 12d });
        Check(Effects(f.Item("shape", 11)) == null, "same ID owned by another PB is untouched");
        var retained = Effects(f.Item()); f.Access = false;
        Reject(() => draw("effect", new object[] { "shape", "particles", 32 }), "each effects update rechecks current display access");
        Check(!f.Typed("ClearHologramEffects", "shape").Item1, "typed clear also rechecks access");
        SameEffects(f.Item(), retained, "lost access cannot mutate the scene");
        f.Access = true; f.SameConstruct = false;
        Check(!f.Typed("SetHologramEffects", "shape", Descriptor()).Item1, "typed effects require the same construct");
        f.SameConstruct = true; f.Gateway.Server = false;
        Reject(() => draw("effect-clear", new object[] { "shape" }), "client endpoint cannot change server declarations");
        f.Gateway.Server = true; f.Registry.Remove(10);
        Reject(() => draw("transition", new object[] { "shape", "in", "fade", .5 }), "unregistered PB cannot publish transitions");

        // Each projected surface has an independent child-ID namespace. Effects
        // must use that namespace without exposing reserved IDs to typed callers.
        using var scoped = new Fixture(); var h = scoped.Draw(); Line(h);
        Screen(h, "a"); Line(h); h("effect", new object[] { "shape", "flicker", .4, 7d });
        Screen(h, "b"); Line(h); h("effect", new object[] { "shape", "flicker", .6, 9d });
        Check(Effects(scoped.Item("!s!a!shape")).FlickerStrength == .4 && Effects(scoped.Item("!s!b!shape")).FlickerStrength == .6 && Effects(scoped.Item()) == null, "screen-local commands preserve sibling and root IDs");
        h("screen", new object[] { "a" }); h("effect-clear", new object[] { "shape" });
        Check(Effects(scoped.Item("!s!a!shape")) == null && Effects(scoped.Item("!s!b!shape")).FlickerStrength == .6, "selected child clear does not touch another surface");
        Check(!scoped.Typed("SetHologramEffects", "!s!b!shape", Descriptor(v => v[0] = .9)).Item1, "typed caller cannot forge a reserved child ID");
        Reject(() => h("effect", new object[] { "!s!b!shape", "flicker", .9 }), "friendly caller cannot forge scope delimiters");
        SameEffects(scoped.Item("!s!b!shape"), HologramEffectSettings.Create(Descriptor(v => { v[0] = .6; v[1] = 9; }).Item1, HologramEffectSettings.DefaultFlags()), "reserved-ID rejection preserves sibling content");
        h("clear", Array.Empty<object>());
        Check(!scoped.Items.Contains("10:!s!a!shape") && scoped.Items.Contains("10:!s!b!shape"), "clearing selected content releases its attached effects");
    }

    static void Replication()
    {
        using var f = new Fixture(); var draw = f.Draw(); Line(draw);
        draw("effect", new object[] { "shape", "flicker", .3, 7d });
        Set(f.Session, "_ticks", 150); draw("transition", new object[] { "shape", "in", "dissolve", 2d });
        var before = (HoloSnapshot)Call(f.Session, "CaptureSnapshot");
        var wire = before.Scenes[0].Items[0].Effects;
        Check(wire != null && wire.Values.Length == 24 && wire.Flags.Length == 8 && wire.Start == 2.5 && wire.Entering, "full snapshot exports detached effects and transition origin");
        var model = ProtoBuf.Meta.RuntimeTypeModel.Create();
        using var stream = new MemoryStream(); model.Serialize(stream, before); stream.Position = 0;
        var copy = (HoloSnapshot)model.Deserialize(stream, null, typeof(HoloSnapshot));
        var restored = copy.Scenes[0].Items[0].Effects;
        Check(restored.Values.SequenceEqual(wire.Values) && restored.Flags.SequenceEqual(wire.Flags) && restored.Start == wire.Start && restored.Entering == wire.Entering, "protobuf round trip preserves the fixed numeric declaration");
        var receiver = new HoloMapSession(); Call(receiver, "ApplySnapshot", copy);
        var receivedScene = ((IDictionary)Field(receiver, "_scenes"))[20L]; var receivedItem = ((IDictionary)Field(receivedScene, "Items"))["10:shape"];
        SameEffects(receivedItem, Effects(f.Item()), "client imports identical immutable settings");
        Check((double)Field(receivedItem, "EffectStart") == 2.5 && (bool)Field(receivedItem, "EffectEntering"), "client retains the shared transition epoch instead of restarting at receipt");
        restored.Values[0] = .9; restored.Flags[1] = 456;
        SameEffects(receivedItem, Effects(f.Item()), "receiver detaches arrays from mutable wire declarations");
        f.Gateway.Server = false; Call(receiver, "SetAnimationClock", 210L);
        var state = Call(receiver, "HologramEffectsFor", receivedItem, Field(receivedItem, "Geometry"));
        var frame = (HologramEffectFrame)Field(state, "Frame");
        Check(frame.Time == 3.5 && frame.Progress == .5, "late join evaluates halfway through the existing transition instead of beginning again");
        f.Gateway.Server = true;

        draw("effect", new object[] { "shape", "scan", .45, .4, .03 });
        var after = (HoloSnapshot)Call(f.Session, "CaptureSnapshot");
        var delta = (HoloSnapshot)Static("BuildDelta", before, after, 8L); var update = delta.Scenes[0].Items[0];
        Check(update.ReuseGeometry && update.Points == null && update.Effects.Values[2] == .45, "effects changes replicate as metadata-only deltas");
        var merged = (HoloSnapshot)Static("MergeDelta", before, delta, 8L);
        Check(ReferenceEquals(merged.Scenes[0].Items[0].Points, before.Scenes[0].Items[0].Points) && merged.Scenes[0].Items[0].Effects.Values[2] == .45, "delta reconstruction reuses geometry and updates effects");
        Check(before.Scenes[0].Items[0].Effects.Values[2] == 0, "effects metadata cannot mutate the prior publication baseline");
        Call(receiver, "ApplySnapshot", merged);
        var current = ((IDictionary)Field(receiver, "_scenes"))[20L];
        foreach (Action<HoloEffectDeclaration> mutate in new Action<HoloEffectDeclaration>[]
        {
            d => d.Values = null, d => d.Values = new double[23], d => d.Flags = null,
            d => d.Flags = new int[7], d => d.Values[0] = double.NaN,
            d => d.Flags[0] = 2, d => d.Flags[3] = 4097, d => d.Start = double.NaN,
            d => d.Start = double.PositiveInfinity, d => d.Start = -1, d => d.Start = 1e9 + 1
        })
        {
            var invalid = ClientReplicationTests.Snapshot();
            invalid.Scenes[0].Items[0].Effects = new HoloEffectDeclaration
            { Values = HologramEffectSettings.DefaultValues(), Flags = HologramEffectSettings.DefaultFlags(), Start = 0, Entering = true };
            mutate(invalid.Scenes[0].Items[0].Effects);
            Reject(() => Call(receiver, "ApplySnapshot", invalid), "hostile effect declaration rejected before scene commit");
            Check(ReferenceEquals(((IDictionary)Field(receiver, "_scenes"))[20L], current), "invalid effects preserve last valid scene atomically");
        }
        draw("effect-clear", new object[] { "shape" });
        var cleared = (HoloSnapshot)Call(f.Session, "CaptureSnapshot");
        var clearDelta = (HoloSnapshot)Static("BuildDelta", after, cleared, 9L);
        Check(clearDelta.Scenes[0].Items[0].ReuseGeometry && clearDelta.Scenes[0].Items[0].Effects == null, "clear produces an explicit neutral metadata replacement");
        var clearMerge = (HoloSnapshot)Static("MergeDelta", after, clearDelta, 9L);
        Check(clearMerge.Scenes[0].Items[0].Effects == null && after.Scenes[0].Items[0].Effects != null, "cleared merge does not preserve stale effects or corrupt its baseline");
    }

    static void LcdBoundary()
    {
        using var f = new Fixture(); f.AddLcd(21); var draw = f.Draw();
        draw("target", new[] { f.Registry[21] }); Line(draw);
        object LcdItem()
        {
            var scene = ((IDictionary)Field(f.Session, "_scenes"))[21L];
            return ((IDictionary)Field(scene, "Items"))["10:shape"];
        }
        draw("effect", new object[] { "shape", "flicker", .15, 9d });
        draw("effect", new object[] { "shape", "particles", 12 });
        draw("transition", new object[] { "shape", "in", "wipe", .4 });
        Check(Effects(LcdItem()).FlickerStrength == .15 && Effects(LcdItem()).Particles == 12 && Effects(LcdItem()).Transition == 2,
            "LCD accepts its supported flat-image effects without a renderer plugin");
        var retained = Effects(LcdItem());
        Reject(() => draw("effect", new object[] { "shape", "volume", .1, 4 }), "LCD rejects world-volume request safely");
        Reject(() => draw("effect", new object[] { "shape", "beams", 8 }), "LCD rejects world-projection beams safely");
        SameEffects(LcdItem(), retained, "unsupported world effects cannot erase existing LCD effects");
        var volume = Descriptor(v => v[6] = .1, q => q[2] = 4);
        var result = (MyTuple<bool, string>)Call(f.Session, "SetHologramEffects", f.Registry[10], f.Registry[21], "shape", volume);
        Check(!result.Item1 && !string.IsNullOrWhiteSpace(result.Item2), "typed LCD capability rejection returns diagnostic instead of throwing into the PB");
    }

    static void RasterBoundary()
    {
        using var f = new Fixture(); var draw = f.Draw(); Screen(draw, "panel"); Line(draw);
        HoloProjectedScreenData ScreenData()
        {
            var screen = ((IDictionary)Field(f.Scene, "Screens"))["10:panel"];
            return (HoloProjectedScreenData)Field(screen, "Data");
        }
        object child() => f.Item("!s!panel!shape");
        draw("effect", new object[] { "shape", "flicker", .2, 8d });
        draw("effect", new object[] { "shape", "volume", .04, 4 });
        draw("effect", new object[] { "shape", "beams", 8 });
        var previous = ScreenData(); var effects = Effects(child());
        Reject(() => draw("screen-renderer", new object[] { "raster", 256, 144, 4 }), "raster switch rejects retained world-only effects");
        Check(ReferenceEquals(ScreenData(), previous) && ScreenData().ContentRenderer == 0, "failed raster switch preserves vector screen declaration");
        SameEffects(child(), effects, "failed raster switch preserves retained source effects");
        draw("effect-clear", new object[] { "shape", "volume" });
        draw("effect-clear", new object[] { "shape", "beams" });
        draw("screen-renderer", new object[] { "raster", 256, 144, 4 });
        Check(ScreenData().ContentRenderer == 1 && Effects(child()).FlickerStrength == .2, "cleared world channels permit raster switch without erasing flat effects");
        var flat = Effects(child());
        Reject(() => draw("effect", new object[] { "shape", "volume", .04, 4 }), "raster child refuses volume admission");
        Reject(() => draw("effect", new object[] { "shape", "beams", 8 }), "raster child refuses projection rays admission");
        Check(ScreenData().ContentRenderer == 1, "unsupported child effects cannot change current renderer selection");
        SameEffects(child(), flat, "unsupported child effects cannot erase prior raster appearance");
        draw("effect", new object[] { "shape", "particles", 12 });
        draw("transition", new object[] { "shape", "out", "dissolve", .4 });
        Check(Effects(child()).Particles == 12 && Effects(child()).Transition == 3 && !(bool)Field(child(), "EffectEntering"), "raster child still supports particles and transitions");
    }

    static void ProjectedCapabilities()
    {
        var descriptors = new[]
        {
            Descriptor(v => v[0] = .2), Descriptor(flags: q => q[5] = 1), Descriptor(flags: q => q[5] = 3),
            Descriptor(v => v[2] = .3), Descriptor(v => v[5] = .3), Descriptor(flags: q => q[5] = 2),
            Descriptor(flags: q => q[3] = 12), Descriptor(flags: q => q[2] = 4), Descriptor(flags: q => q[4] = 8)
        };
        for (int surface = 0; surface <= 4; surface++) for (int renderer = 0; renderer <= 1; renderer++)
            for (int profile = 0; profile < descriptors.Length; profile++)
            {
                var descriptor = descriptors[profile]; var settings = HologramEffectSettings.Create(descriptor.Item1, descriptor.Item2);
                var screen = new HoloProjectedScreenData { SurfaceKind = surface, ContentRenderer = renderer };
                bool unsupported = renderer == 1 ? profile >= 7 : surface != 0 && profile >= 3;
                if (unsupported) Reject(() => Static("ValidateProjectedHologramEffects", settings, screen), "projected source-coordinate capability matrix");
                else { Static("ValidateProjectedHologramEffects", settings, screen); Check(true, "supported projected source-coordinate profile is accepted"); }
            }

        using var f = new Fixture(); var draw = f.Draw(); Screen(draw, "panel"); Line(draw);
        HoloProjectedScreenData Data()
        { return (HoloProjectedScreenData)Field(((IDictionary)Field(f.Scene, "Screens"))["10:panel"], "Data"); }
        object Child() => f.Item("!s!panel!shape");
        draw("effect", new object[] { "shape", "scan", .3, .4, .04 });
        var previous = Data(); var before = Effects(Child());
        Reject(() => draw("screen-surface", new object[] { "sphere", 2d, Math.PI * 2, Math.PI, "inside" }), "curved surface change refuses retained flat-vector scan");
        Check(ReferenceEquals(Data(), previous) && Data().SurfaceKind == 0, "failed surface change preserves the old screen declaration");
        SameEffects(Child(), before, "failed surface change preserves its source effects");
        draw("effect-clear", new object[] { "shape", "scan" });
        draw("screen-surface", new object[] { "sphere", 2d, Math.PI * 2, Math.PI, "inside" });
        draw("effect", new object[] { "shape", "flicker", .2, 8d });
        draw("transition", new object[] { "shape", "in", "fade", .4 });
        before = Effects(Child()); double start = (double)Field(Child(), "EffectStart");
        Reject(() => draw("transition", new object[] { "shape", "out", "wipe", .6 }), "curved vector transition refuses source-coordinate wipe");
        SameEffects(Child(), before, "failed curved transition preserves prior supported effect configuration");
        Check((double)Field(Child(), "EffectStart") == start && (bool)Field(Child(), "EffectEntering"), "failed curved transition preserves its time and direction");
        draw("transition", new object[] { "shape", "out", "dissolve", .4 });
        Check(Effects(Child()).Transition == 3 && Data().SurfaceKind == 2, "curved vector permits primitive dissolve");

        var valid = (HoloSnapshot)Call(f.Session, "CaptureSnapshot"); var receiver = new HoloMapSession(); Call(receiver, "ApplySnapshot", valid);
        var retained = ((IDictionary)Field(receiver, "_scenes"))[20L];
        foreach (Action<HoloEffectDeclaration> mutation in new Action<HoloEffectDeclaration>[]
        { d => d.Values[2] = .2, d => d.Values[5] = .3, d => d.Flags[5] = 2, d => d.Flags[3] = 12, d => d.Flags[2] = 4, d => d.Flags[4] = 8 })
        {
            var hostile = (HoloSnapshot)Call(f.Session, "CaptureSnapshot"); mutation(hostile.Scenes[0].Items[0].Effects);
            Reject(() => Call(receiver, "ApplySnapshot", hostile), "network cannot attach unsupported effects to a curved vector screen");
            Check(ReferenceEquals(((IDictionary)Field(receiver, "_scenes"))[20L], retained), "network capability failure preserves previous scene atomically");
        }
        draw("screen-renderer", new object[] { "raster", 256, 144, 4 });
        draw("effect", new object[] { "shape", "scan", .3, .4, .04 });
        draw("effect", new object[] { "shape", "particles", 12 });
        draw("transition", new object[] { "shape", "in", "wipe", .4 });
        Check(Data().ContentRenderer == 1 && Effects(Child()).Particles == 12 && Effects(Child()).Transition == 2, "curved raster accepts scan, particle and wipe source effects");
        previous = Data(); before = Effects(Child());
        Reject(() => draw("screen-renderer", new object[] { "vector" }), "curved raster cannot switch to unsupported vector effect coordinates");
        Check(ReferenceEquals(Data(), previous) && Data().ContentRenderer == 1, "failed renderer change preserves current curved raster screen");
        SameEffects(Child(), before, "failed renderer change preserves all supported raster effects");

        var flat = new HoloProjectedScreenData { Width = 4, Height = 2, CanvasWidth = 2, CanvasHeight = 2, Aspect = "stretch", Pose = (double[])Static("MatrixValues", MatrixD.Identity) };
        var extrusion = (MatrixD)Static("HologramProjectedExtrusion", MatrixD.Identity, flat, MatrixD.Identity, .003d);
        var collapsed = (MatrixD)Static("ProjectedCanvas", flat, .003d, 2d, 2d);
        Check(collapsed.Backward == Vector3D.Zero && Math.Abs(extrusion.Backward.Length() - Math.Sqrt(2)) < 1e-9, "flat effects restore source Z at canvas scale while ordinary 2D projection collapses it");
        Check(Vector3D.Distance(Vector3D.Transform(new Vector3D(0, 0, .1), extrusion), new Vector3D(0, 0, .003 + .1 * Math.Sqrt(2))) < 1e-9, "identity flat extrusion has explicit scaled depth units");
        var pose = MatrixD.CreateFromYawPitchRoll(.6, .2, .3) * MatrixD.CreateTranslation(1, 2, 3);
        flat.Pose = (double[])Static("MatrixValues", pose);
        var world = MatrixD.CreateRotationX(.7) * MatrixD.CreateTranslation(100, 200, 300);
        var content = MatrixD.CreateScale(3) * MatrixD.CreateRotationZ(.2) * MatrixD.CreateTranslation(.1, .2, 0);
        extrusion = (MatrixD)Static("HologramProjectedExtrusion", content, flat, world, .003d);
        collapsed = content * (MatrixD)Static("ProjectedCanvas", flat, .003d, 2d, 2d) * world;
        var delta = Vector3D.Transform(new Vector3D(0, 0, .1), extrusion) - Vector3D.Transform(Vector3D.Zero, extrusion);
        // VRage's yaw/pitch/roll constructor passes through a float quaternion.
        // Its authored basis is almost unit length; preserve that actual metric
        // instead of replacing it with an idealized rotation in the reference.
        double authoredMetric = Math.Sqrt((pose.Right * (flat.Width / flat.CanvasWidth)).Length() * (pose.Up * (flat.Height / flat.CanvasHeight)).Length());
        var authoredNormal = Vector3D.Normalize(Vector3D.Cross(pose.Right, pose.Up));
        if (Vector3D.Dot(authoredNormal, pose.Backward) < 0) authoredNormal = -authoredNormal;
        Check(Vector3D.Distance(delta, Vector3D.TransformNormal(authoredNormal, world) * (.3 * authoredMetric)) < 1e-9, "authored plane normal and world rotation transport ghost depth exactly once");
        var planar = new Vector3D(.3, .2, 0);
        Check(Vector3D.Distance(Vector3D.Transform(planar, extrusion), Vector3D.Transform(planar, collapsed)) < 1e-9, "restoring volume does not shift the original flat artwork");

        var affine = new HoloProjectedScreenData { Width = 2, Height = 2, CanvasWidth = 2, CanvasHeight = 2, Aspect = "stretch", Pose = (double[])Static("MatrixValues", MatrixD.CreateScale(2)) };
        var scaled = (MatrixD)Static("HologramProjectedExtrusion", MatrixD.Identity, affine, MatrixD.Identity, 0d);
        Check(Math.Abs(scaled.Backward.Length() - 2) < 1e-9 && Math.Abs(scaled.Backward.Z - 2) < 1e-9, "uniformly scaled screen carries depth scale once rather than squaring pose scale");
        var shearedPose = MatrixD.Identity; shearedPose.Up = new Vector3D(.5, 1, 0); shearedPose.Backward = new Vector3D(.7, .8, 2);
        affine.Pose = (double[])Static("MatrixValues", shearedPose);
        var sheared = (MatrixD)Static("HologramProjectedExtrusion", MatrixD.Identity, affine, MatrixD.Identity, 0d);
        double shearMetric = Math.Sqrt(shearedPose.Right.Length() * shearedPose.Up.Length());
        Check(Math.Abs(sheared.Backward.Length() - shearMetric) < 1e-9 && Math.Abs(Vector3D.Dot(sheared.Backward, sheared.Right)) < 1e-9 && Math.Abs(Vector3D.Dot(sheared.Backward, sheared.Up)) < 1e-9,
            "sheared authored plane extrudes orthogonally at its in-plane metric rather than following oblique backward basis");
        Check(sheared.Backward.Z > 0, "extrusion normal chooses the authored backward orientation");
        shearedPose.Backward = -shearedPose.Backward; affine.Pose = (double[])Static("MatrixValues", shearedPose);
        var reversed = (MatrixD)Static("HologramProjectedExtrusion", MatrixD.Identity, affine, MatrixD.Identity, 0d);
        Check(Vector3D.Distance(reversed.Backward, -sheared.Backward) < 1e-9, "reversing authored backside reverses extrusion without altering depth magnitude");
        foreach (var badPose in new[]
        {
            new MatrixD(0,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1),
            new MatrixD(1,0,0,0, 2,0,0,0, 0,0,1,0, 0,0,0,1),
            new MatrixD(double.NaN,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1)
        })
        {
            affine.Pose = (double[])Static("MatrixValues", badPose);
            Reject(() => Static("HologramProjectedExtrusion", MatrixD.Identity, affine, MatrixD.Identity, 0d), "invalid screen-plane normal proof fails before geometry can reach terminal renderer");
        }
    }

    static void ClonesAndCompilation()
    {
        using var f = new Fixture(); var draw = f.Draw();
        draw("svg", new object[] { "shape", "<svg viewBox='0 0 1 1'><rect width='1' height='1' fill='cyan'/></svg>" });
        draw("effect", new object[] { "shape", "scan", .35, .3, .04 });
        draw("transition", new object[] { "shape", "in", "fade", .5 });
        var item = f.Item(); var identity = Field(item, "ContentIdentity"); var geometry = Field(item, "Geometry");
        var declaration = Field(item, "Declaration"); var effect = Effects(item);
        var animationClone = Call(f.Session, "CloneAnimationItem", item);
        Check(!ReferenceEquals(animationClone, item) && ReferenceEquals(Field(animationClone, "Geometry"), geometry), "animation clones are detached while retaining immutable source geometry");
        SameEffects(animationClone, effect, "animation clone preserves effects");
        Check((double)Field(animationClone, "EffectStart") == (double)Field(item, "EffectStart") && (bool)Field(animationClone, "EffectEntering") == (bool)Field(item, "EffectEntering"), "animation clone preserves transition state");

        var compile = typeof(HoloMapSession).GetMethod("ClientDisplayItem", BindingFlags.Instance | BindingFlags.NonPublic);
        object Render(int budget)
        {
            var args = new[] { f.Scene, f.Item(), (object)budget }; var result = compile.Invoke(f.Session, args);
            if (result == null && budget > 0) { Call(f.Session, "CompileQueuedDisplay"); result = compile.Invoke(f.Session, args); }
            return result;
        }
        var rendered = Render(1); Check(rendered != null, "declarative artwork compiles with attached effects");
        SameEffects(rendered, effect, "compiled render clone retains attached effects");
        Check((double)Field(rendered, "EffectStart") == (double)Field(item, "EffectStart"), "compiled render clone preserves transition origin");
        var compiledGeometry = Field(rendered, "Geometry"); var lastCompile = Field(f.Session, "_lastCompileTick");
        Set(f.Session, "_ticks", 1); draw("effect", new object[] { "shape", "flicker", .2, 8d });
        var updated = Render(0);
        Check(updated != null && ReferenceEquals(Field(updated, "Geometry"), compiledGeometry), "effects-only change reuses compiled geometry without a work slot");
        Check(ReferenceEquals(Field(f.Item(), "ContentIdentity"), identity) && ReferenceEquals(Field(f.Item(), "Geometry"), geometry) && ReferenceEquals(Field(f.Item(), "Declaration"), declaration), "effect setters preserve all source compilation identities");
        Check(Equals(Field(f.Session, "_lastCompileTick"), lastCompile) && Effects(updated).FlickerStrength == .2, "effect appearance changes bypass geometry recompilation while reaching render clones");
        Set(f.Session, "_dirty", false); Set(f.Session, "_ticks", 20);
        Call(f.Session, "TickDrawAnimations"); Call(f.Session, "GetAnimatedItem", 20L, f.Item());
        Check(!(bool)Field(f.Session, "_dirty") && (double)Field(f.Item(), "EffectStart") == 0, "client animation evaluation does not replicate every frame or restart retained transition");
        Check(f.Typed("ClearHologramEffects", "shape").Item1, "compiled artwork effect clear succeeds");
        var plain = Render(0);
        Check(plain != null && Effects(plain) == null && ReferenceEquals(Field(plain, "Geometry"), compiledGeometry), "clear immediately removes effects from reused compiled geometry");
    }

    static void RenderPlanning()
    {
        using var f = new Fixture(); var draw = f.Draw(); Line(draw);
        var descriptor = Descriptor(v => { v[6] = .1; v[7] = .5; }, q => { q[2] = 4; q[3] = 32; q[4] = 8; q[7] = 64; });
        Check(f.Typed("SetHologramEffects", "shape", descriptor).Item1, "render work receives the actual admitted effect declaration");
        for (int remaining = 0; remaining <= 100; remaining++)
        {
            var plan = (HologramEffectPlan)Static("HologramEffectDrawPlan", f.Item(), remaining);
            // One retained line costs one primitive. This helper is called after
            // canonical content has been drawn, so only extras consume remaining.
            int available = Math.Min(remaining + 1, 64), layers = Math.Min(4, available - 1);
            int particles = Math.Min(32, (available - 1 - layers) / 4);
            int beams = Math.Min(8, (available - 1 - layers - particles * 4) / 4);
            int total = 1 + layers + particles * 4 + beams * 4;
            Check(plan.BaseAccepted && plan.DepthLayers == layers && plan.Particles == particles && plan.Beams == beams && plan.Primitives == total,
                "actual item geometry determines deterministic effect prefixes");
            Check(plan.Primitives - 1 <= remaining && plan.Primitives <= 64, "actual renderer planning obeys both per-view remainder and declared ceiling");
        }
        var maximum = (HologramEffectPlan)Static("HologramEffectDrawPlan", f.Item(), int.MaxValue);
        Check(maximum.Primitives <= 64, "large client budgets do not overflow or bypass effect ceiling");
        var retainedGeometry = Field(f.Item(), "Geometry");
        descriptor.Item2[7] = 0; Check(f.Typed("SetHologramEffects", "shape", descriptor).Item1, "zero effect ceiling is accepted");
        var zero = (HologramEffectPlan)Static("HologramEffectDrawPlan", f.Item(), 100);
        Check(!zero.BaseAccepted && zero.DepthLayers == 0 && zero.Particles == 0 && zero.Beams == 0 && ReferenceEquals(Field(f.Item(), "Geometry"), retainedGeometry),
            "zero effect ceiling drops embellishments without replacing canonical geometry");

        var clock = typeof(HoloMapSession).GetProperty("HologramEffectNow", BindingFlags.Instance | BindingFlags.NonPublic);
        Set(f.Session, "_ticks", 60);
        Check((double)clock.GetValue(f.Session) == 1, "effect clock follows synchronized server animation seconds");
        Set(f.Session, "_effectDrawNow", 1.125d); Set(f.Session, "_effectDrawActive", true);
        Set(f.Session, "_ticks", 120);
        Check((double)clock.GetValue(f.Session) == 1.125, "all effects inside one draw retain the same frozen time");
        Set(f.Session, "_effectDrawActive", false);
        Check((double)clock.GetValue(f.Session) == 2, "ending a draw resumes synchronized time instead of retaining the previous frame");

        draw("polygons", new object[] { "quad", new[] { Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitX + Vector3D.UnitY, Vector3D.UnitY }, new[] { new[] { 0, 1, 2, 3 } }, "cyan", "cyan" });
        descriptor.Item2[7] = 128; descriptor.Item2[3] = 0;
        Check(f.Typed("SetHologramEffects", "quad", descriptor).Item1, "filled source receives the admitted effect declaration");
        var compile = typeof(HoloMapSession).GetMethod("ClientDisplayItem", BindingFlags.Instance | BindingFlags.NonPublic);
        var compileArgs = new[] { f.Scene, f.Item("quad"), (object)1 };
        compile.Invoke(f.Session, compileArgs); Call(f.Session, "CompileQueuedDisplay");
        var quad = compile.Invoke(f.Session, compileArgs); Check(quad != null, "filled source compiles before effects draw planning");
        var source = (Geometry)Field(quad, "Geometry");
        Check(source.Triangles.Length == 6 && source.Edges.Length == 8, "quad witness contains two triangles and four outline lines");
        for (int remaining = 0; remaining <= 100; remaining++)
        {
            var plan = (HologramEffectPlan)Static("HologramEffectDrawPlan", quad, remaining);
            // Actual terminal cost: each triangle reserves admission plus a
            // billboard submission, and each outline costs one submission.
            int available = Math.Min(remaining + 8, 128), layers = Math.Min(4, (available - 8) / 8);
            int beams = Math.Min(8, (available - 8 - layers * 8) / 4);
            Check(plan.BaseAccepted && plan.DepthLayers == layers && plan.Particles == 0 && plan.Beams == beams && plan.Primitives == 8 + layers * 8 + beams * 4,
                "mixed filled/outline geometry is billed at terminal draw cost");
        }
        for (int slot = 0; slot < 8; slot++)
        {
            var expected = source.Points[slot * source.Points.Length / 8];
            Check((Vector3D)Static("HologramBeamTarget", quad, slot) == expected, "beam target index uses configured count");
            foreach (int budget in new[] { 0, 10, 48, 100, int.MaxValue })
            {
                Static("HologramEffectDrawPlan", quad, budget);
                Check((Vector3D)Static("HologramBeamTarget", quad, slot) == expected, "reduced draw budget cannot redistribute retained beam slots");
            }
        }
    }

    static void PackedAnimationRetention()
    {
        using var f = new Fixture();
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes("HMA1\n1\n2\n1\n<svg viewBox='0 0 1 1'><rect width='1' height='1'/></svg>\n<svg viewBox='0 0 1 1'><circle cx='.5' cy='.5' r='.4'/></svg>\n");
        var encoded = new List<byte> { 72, 77, 67, 49 }; encoded.AddRange(BitConverter.GetBytes(bytes.Length));
        for (int i = 0; i < bytes.Length; i += 8)
        {
            int count = Math.Min(8, bytes.Length - i); encoded.Add((byte)((1 << count) - 1)); encoded.AddRange(bytes.Skip(i).Take(count));
        }
        string packed = "HoloMapAnimation1:" + Convert.ToBase64String(encoded.ToArray());
        var options = new MyTuple<MatrixD, int>(MatrixD.Identity, 3);
        Check(f.Typed("PutPackedAnimationFrame", "packed", packed, 0d, options).Item1, "packed initial frame is admitted");
        var descriptor = Descriptor(v => { v[0] = .3; v[2] = .4; }, flags => flags[1] = 31);
        Check(f.Typed("SetHologramEffects", "packed~0", descriptor).Item1, "packed part receives attached effects");
        Set(f.Session, "_ticks", 120);
        Check(f.Typed("SetHologramTransition", "packed~0", false, "fade", .75d).Item1, "packed part receives retained exit transition");
        var prior = f.Item("packed~0"); var effects = Effects(prior); var svg = (string)Field(prior, "SvgSource");
        double start = (double)Field(prior, "EffectStart");
        Set(f.Session, "_ticks", 150);
        Check(f.Typed("PutPackedAnimationFrame", "packed", packed, 1d, options).Item1, "packed frame update is admitted");
        var updated = f.Item("packed~0");
        Check(!ReferenceEquals(updated, prior) && (string)Field(updated, "SvgSource") != svg, "packed witness actually replaces the part with different artwork");
        SameEffects(updated, effects, "packed part replacement preserves effect configuration");
        Check((double)Field(updated, "EffectStart") == start && !(bool)Field(updated, "EffectEntering"), "packed frame refresh cannot restart or reverse an existing transition");
    }

    static void AcceptedBoundsClosure()
    {
        using var f = new Fixture(); var draw = f.Draw(); Line(draw, "large-model");
        var transform = MatrixD.CreateScale(1e-5);
        Check(f.Typed("SetTransform", "large-model", transform).Item1, "valid small projector pose is admitted before large model points");
        var points = new[] { new Vector3D(700000, 700000, 0), new Vector3D(-700000, 0, 700000), new Vector3D(0, -700000, -700000) };
        var edges = new[] { new Vector2I(0, 1), new Vector2I(1, 2), new Vector2I(2, 0) };
        draw("wires", new object[] { "large-model", points, edges, "cyan" });
        draw("effect", new object[] { "large-model", "flicker", .2, 8d });
        var item = Call(f.Session, "GetItem", f.Registry[10], f.Registry[20], "large-model");
        var geometry = (Geometry)Field(item, "Geometry");
        Check(geometry.Points.SequenceEqual(points) && (MatrixD)Field(item, "Transform") == transform, "actual PB wire replacement retains scaled pose and accepted model coordinates");
        Check(points.All(HologramEffectKernel.PointValid), "each accepted geometry point satisfies the kernel's radial point contract");
        var state = Call(f.Session, "HologramEffectsFor", item, geometry);
        var min = (Vector3D)Field(state, "Min"); var max = (Vector3D)Field(state, "Max");
        Check(min == new Vector3D(-700000) && max == new Vector3D(700000), "closure witness has corners assembled from different accepted vertices");
        Check(!HologramEffectKernel.PointValid(min) && !HologramEffectKernel.PointValid(max), "derived AABB corners exceed radial point bound while their components remain valid");
        for (int i = 0; i < edges.Length; i++)
        {
            var point = (geometry.Points[edges[i].X] + geometry.Points[edges[i].Y]) * .5;
            var color = (Vector4)Static("HologramEffectColor", state, Vector4.One, point, i);
            Check(float.IsFinite(color.X) && float.IsFinite(color.Y) && float.IsFinite(color.Z) && float.IsFinite(color.W) && color.W >= 0 && color.W <= 1,
                "every valid primitive center evaluates effects with derived AABB bounds without exception or nonfinite color");
        }
    }

    static void PrimitiveSafety()
    {
        static bool Finite(Vector3D p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
        var quadMethod = typeof(HoloMapSession).GetMethod("HologramParticleQuad", BindingFlags.Static | BindingFlags.NonPublic);
        bool Quad(Vector3D camera, Vector3D center, double size, out Vector3D[] vertices)
        {
            object[] args = { camera, center, size, Vector3D.Zero, Vector3D.Zero, Vector3D.Zero, Vector3D.Zero };
            bool result = (bool)quadMethod.Invoke(null, args); vertices = args.Skip(3).Cast<Vector3D>().ToArray(); return result;
        }
        foreach (var camera in new[] { Vector3D.Zero, new Vector3D(0, 10, 4), new Vector3D(10, 0, 4) })
        {
            var center = new Vector3D(0, 0, 4);
            Check(Quad(camera, center, 2, out var vertices) && vertices.All(Finite), "ordinary particles generate four finite vertices");
            for (int i = 0; i < 4; i++) Check(Math.Abs(Vector3D.Distance(vertices[i], vertices[(i + 1) % 4]) - 2) < 1e-9, "particle quad retains requested square dimensions");
            Check(Vector3D.Distance(vertices.Aggregate(Vector3D.Zero, (sum, p) => sum + p) / 4, center) < 1e-9, "particle quad is centered on retained particle position");
        }
        Check(!Quad(Vector3D.One, Vector3D.One, 1, out _), "coincident eye/particle is rejected before normalization");
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            for (int axis = 0; axis < 3; axis++)
            {
                var invalid = Vector3D.Zero; if (axis == 0) invalid.X = bad; else if (axis == 1) invalid.Y = bad; else invalid.Z = bad;
                Check(!Quad(invalid, new Vector3D(0, 0, 4), 1, out _) && !Quad(Vector3D.Zero, invalid, 1, out _), "nonfinite eye and particle coordinates fail safely");
            }
        foreach (double size in new[] { -1d, 0, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e9 + 1 })
            Check(!Quad(Vector3D.Zero, Vector3D.UnitZ, size, out _), "invalid particle dimensions cannot reach billboard vertices");
        Check(!Quad(new Vector3D(-double.MaxValue), new Vector3D(double.MaxValue), 1, out _), "overflowing finite separation fails before normalization");

        var session = new HoloMapSession();
        var sink = typeof(HoloMapSession).GetMethod("DrawVolumeTriangle", BindingFlags.Instance | BindingFlags.NonPublic);
        int Sink(int budget, Vector3D a, Vector3D b, Vector3D c)
        {
            object[] args = { null, a, b, c, Vector2.Zero, Vector2.UnitX, Vector2.UnitY, Vector4.One, 0f, false, null, MatrixD.Identity, MatrixD.Identity, Vector3D.UnitZ, uint.MaxValue, budget };
            sink.Invoke(session, args); return (int)args[15];
        }
        Check(Sink(0, Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitY) == 0, "terminal triangle sink cannot underflow exhausted budget");
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            for (int point = 0; point < 3; point++)
            {
                var vertices = new[] { Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitY }; vertices[point].X = bad;
                Check(Sink(8, vertices[0], vertices[1], vertices[2]) == 8, "invalid terminal vertex consumes no work and performs no GPU submission");
            }
        Check(Sink(1, Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitY) == 0, "admission at one remaining work unit cannot submit a triangle or underflow");
        Check(Sink(2, Vector3D.Zero, Vector3D.Zero, Vector3D.Zero) == 2, "degenerate finite triangle is rejected before admission and cannot submit a billboard");

        var pathological = MatrixD.Identity; pathological.M33 = 1e308;
        var ghost = Vector3D.Transform(new Vector3D(0, 0, .01), pathological);
        var ghostB = Vector3D.Transform(new Vector3D(1, 0, .01), pathological);
        var ghostC = Vector3D.Transform(new Vector3D(0, 1, .01), pathological);
        Check(Finite(ghost) && ghost.Z > 1e305, "pathological finite planar transform creates the large-coordinate regression witness");
        Check(!ModClientRules.Normal(ghost, ghostB, ghostC, Vector3D.UnitZ, out var normal) && Finite(normal), "normal proof rejects huge ghost coordinates without producing an unsafe normal");
        Check(Sink(8, ghost, ghostB, ghostC) == 8, "huge transformed ghost geometry reaches no GPU call and consumes no admission budget");

        var lineMethod = typeof(HoloMapSession).GetMethod("HologramSafeLine", BindingFlags.Static | BindingFlags.NonPublic);
        bool SafeLine(Vector3D a, Vector3D b, Vector3D camera, out Vector3 direction, out float length)
        {
            object[] args = { a, b, camera, Vector3.Zero, 0f };
            bool result = (bool)lineMethod.Invoke(null, args); direction = (Vector3)args[3]; length = (float)args[4]; return result;
        }
        Check(SafeLine(Vector3D.Zero, Vector3D.UnitZ * 2, new Vector3D(0, 0, 4), out var ordinaryDirection, out var ordinaryLength) && ordinaryDirection == Vector3.UnitZ && ordinaryLength == 2,
            "ordinary lines parallel to the view axis remain visible with finite terminal direction");
        Check(!SafeLine(ghost, ghostB, Vector3D.UnitZ, out var rejectedDirection, out var rejectedLength) && float.IsFinite(rejectedDirection.X) && float.IsFinite(rejectedDirection.Y) && float.IsFinite(rejectedDirection.Z) && float.IsFinite(rejectedLength),
            "huge ghost line is rejected before converting coordinates into a terminal float direction");
        foreach (var bad in new[] { new Vector3D(double.NaN, 0, 0), new Vector3D(double.PositiveInfinity, 0, 0) })
            Check(!SafeLine(bad, Vector3D.UnitX, Vector3D.UnitZ, out _, out _) && !SafeLine(Vector3D.Zero, bad, Vector3D.UnitZ, out _, out _) && !SafeLine(Vector3D.Zero, Vector3D.UnitX, bad, out _, out _),
                "nonfinite line endpoints and camera cannot reach line submission");
    }
}
