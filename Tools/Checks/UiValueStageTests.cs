using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HoloMap;
using HDR.Interactions;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

// Stage-only integration tests: the actual PB UI endpoint, canonical scene items,
// and protobuf publication code execute against bounded game gateway doubles.
// This class owns no production implementation and no live Checks output.
internal static class UiValueStageTests
{
    static int checks;
    static void Check(bool condition, string label)
    { if (!condition) throw new Exception("Staged UI value integration: " + label); checks++; }
    static void Reject(Action action, string label)
    {
        try { action(); }
        catch (ArgumentException) { checks++; return; }
        throw new Exception("Expected staged UI value rejection: " + label);
    }
    static object Call(object target, string name, params object[] args)
    {
        var candidates = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.Name == name && m.GetParameters().Length == args.Length).ToArray();
        if (candidates.Length != 1) throw new Exception("Ambiguous or missing stage seam: " + name + "/" + args.Length);
        try { return candidates[0].Invoke(target, args); }
        catch (TargetInvocationException error) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    static object Field(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value);

    sealed class Fixture : IDisposable
    {
        readonly List<(MemberInfo Member, object Prior)> prior = new List<(MemberInfo, object)>();
        readonly ProtoBuf.Meta.RuntimeTypeModel serializer = ProtoBuf.Meta.RuntimeTypeModel.Create();
        public readonly HoloMapSession Session = new HoloMapSession();
        public readonly Dictionary<long, object> Registry = new Dictionary<long, object>();
        public bool Server = true, Access = true, SameConstruct = true, CallerWorking = true, TargetWorking = true;
        public bool CallerClosed, TargetClosed;
        public string Program = "stage-value-owner";
        public int Runs;
        public Func<string, bool> OnRun;
        public readonly List<string> Arguments = new List<string>();

        public Fixture(bool lcd = false)
        {
            foreach (long owner in new[] { 10L, 11L })
            {
                long id = owner;
                Registry[id] = UiValueStageProxy.Make(typeof(IMyProgrammableBlock), (m, a) => m.Name switch
                {
                    "get_EntityId" => id, "get_OwnerId" => 77L, "get_Closed" => CallerClosed,
                    "get_IsWorking" => CallerWorking, "get_ProgramData" => Program,
                    "IsSameConstructAs" => SameConstruct, "HasPlayerAccess" => Access,
                    "TryRun" => Run((string)a[0]), _ => throw new Exception("Stage PB gateway: " + m.Name)
                });
            }
            Registry[20] = UiValueStageProxy.Make(lcd ? typeof(IMyTextPanel) : typeof(IMyProjector), (m, a) => m.Name switch
            {
                "get_EntityId" => 20L, "get_Closed" => TargetClosed, "get_IsWorking" => TargetWorking,
                "HasPlayerAccess" => Access, "get_WorldMatrix" => MatrixD.Identity,
                "GetPosition" => Vector3D.Zero, _ => throw new Exception("Stage display gateway: " + m.Name)
            });
            Install("Multiplayer", (m, a) => m.Name == "get_IsServer" ? Server : throw new Exception("Stage multiplayer: " + m.Name));
            Install("Entities", (m, a) => m.Name == "GetEntityById" ? Registry.GetValueOrDefault((long)a[0]) : throw new Exception("Stage entities: " + m.Name));
            Install("Utilities", (m, a) =>
            {
                if (m.Name == "get_IsDedicated") return true;
                if (m.Name == "SerializeToBinary") return Serialize(a[0]);
                if (m.Name == "SerializeFromBinary")
                { using var input = new MemoryStream((byte[])a[0]); return serializer.Deserialize(input, null, m.GetGenericArguments()[0]); }
                throw new Exception("Stage utilities: " + m.Name);
            });
        }
        bool Run(string argument) { Runs++; Arguments.Add(argument); return OnRun == null || OnRun(argument); }
        void Install(string name, Func<MethodInfo, object[], object> handler)
        {
            MemberInfo member = (MemberInfo)typeof(MyAPIGateway).GetField(name) ?? typeof(MyAPIGateway).GetProperty(name);
            object old = member is FieldInfo field ? field.GetValue(null) : ((PropertyInfo)member).GetValue(null);
            Type type = member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;
            prior.Add((member, old)); Write(member, UiValueStageProxy.Make(type, handler));
        }
        static void Write(MemberInfo member, object value)
        { if (member is FieldInfo field) field.SetValue(null, value); else ((PropertyInfo)member).SetValue(null, value); }
        public byte[] Serialize(object value)
        { using var output = new MemoryStream(); serializer.Serialize(output, value); return output.ToArray(); }
        public T RoundTrip<T>(T value)
        { using var input = new MemoryStream(Serialize(value)); return (T)serializer.Deserialize(input, null, typeof(T)); }
        public void DisableUtilities()
        {
            MemberInfo member = (MemberInfo)typeof(MyAPIGateway).GetField("Utilities") ?? typeof(MyAPIGateway).GetProperty("Utilities");
            object old = member is FieldInfo field ? field.GetValue(null) : ((PropertyInfo)member).GetValue(null);
            prior.Add((member, old)); Write(member, null);
        }
        public Func<string, object[], object> Ui(long owner = 10)
        {
            var ui = (Func<string, object[], object>)Call(Session, "UiEndpoint", Registry[owner]);
            ui("target", new[] { Registry[20] }); return ui;
        }
        public Func<string, object[], object> Draw(long owner = 10)
        {
            var draw = (Func<string, object[], object>)Call(Session, "DrawEndpoint", Registry[owner]);
            draw("target", new[] { Registry[20] }); return draw;
        }
        public UiDisplay Display(long owner = 10) => (UiDisplay)Call(Session, "GetUiDisplay", owner, 20L);
        public List<UiDisplay> Captured() => (List<UiDisplay>)Call(Session, "CaptureUiDisplays");
        public object Scene => ((IDictionary)Field(Session, "_scenes"))[20L];
        public IDictionary Items => (IDictionary)Field(Scene, "Items");
        public object Item(string id, long owner = 10) => Items[owner + ":" + id];
        public void Tick(int tick) { Set(Session, "_ticks", tick); Call(Session, "TickUiValueState"); }
        public void Dispose() { for (int i = prior.Count - 1; i >= 0; i--) Write(prior[i].Member, prior[i].Prior); }
    }

    public static int Run()
    {
        checks = 0;
        LegacyBoundary();
        NumericCommands();
        DescriptorAndPublicationIsolation();
        HostilePublicationAtomicity();
        BoundPathPoses();
        BoundPolylinePoses();
        BoundRotationPoses();
        LeasePreemptionAndEvents();
        ScalarLeaseAndNoOpPreemption();
        ValueIdentityReuse();
        BoundedEventQueue();
        LifetimeAndDeferredWakes();
        DeclarationProofCache();
        CoupledDeclarationBoundsAtomicity();
        LcdPlanarAdmissionAndMutation();
        NonfiniteDeterminantPublications();
        ViewAndSharedSourcePreemption();
        ReadOnlyNumericQueries();
        SourceProofRetirement();
        NumericOnlyPublicationAndControlHeaders();
        MetadataReserveAndCapabilities();
        WakeExceptionBusyAndSuccessorGuards();
        GlobalImportAdmission();
        LegacyMetadataAdmissionAtomicity();
        PassiveMetadataGrowthPublication();
        UiImportWithoutUtilities();
        return checks;
    }
    static MyTuple<double, long> Value(Func<string, object[], object> ui, string id = "gain") =>
        (MyTuple<double, long>)ui("get-value", new object[] { id });
    static MyTuple<bool, double, long> Write(Func<string, object[], object> ui, double value, long? expected = null, string id = "gain") =>
        (MyTuple<bool, double, long>)ui("set-value", expected.HasValue ? new object[] { id, value, expected.Value } : new object[] { id, value });
    static MyTuple<string, string, string, MyTuple<double, long, long>>[] Events(Func<string, object[], object> ui) =>
        (MyTuple<string, string, string, MyTuple<double, long, long>>[])ui("poll-value-events", Array.Empty<object>());
    static Func<string, object[], object> Setup(Fixture f, double initial = 2, double step = 1, MatrixD? pose = null)
    {
        var draw = f.Draw();
        draw("line", new object[] { "handle", Vector3D.Zero, Vector3D.UnitX, "cyan" });
        if (pose.HasValue) draw("transform", new object[] { "handle", pose.Value });
        var ui = f.Ui();
        ui("bundle", new object[] { "main" });
        ui("value", new object[] { "gain", initial, 0d, 10d, step });
        ui("control", new object[] { "slider", "main", "handle", .5, 0d, 1d, .25 });
        ui("bind-value", new object[] { "slider", "gain" });
        ui("constraint", new object[] { "slider", "line", Vector3D.Zero, Vector3D.UnitX });
        ui("draggable", new object[] { "slider", true });
        return ui;
    }
    static UiControlData Control(Fixture f) => f.Display().Widgets.Single(w => w.Id == "slider").Control;
    static UiControlData Control(Fixture f, string id) => f.Display().Widgets.Single(w => w.Id == id).Control;
    static object Proof(Fixture f, string artwork = "handle") =>
        ((IDictionary)Field(((IDictionary)Field(f.Session, "_uiValueOwners"))["10:20"], "Proofs"))[artwork];
    static int ProofCompiles(Fixture f, string artwork = "handle") => (int)Field(Proof(f, artwork), "Compiles");
    static int ProofCount(Fixture f)
    {
        var owners = (IDictionary)Field(f.Session, "_uiValueOwners");
        return owners.Contains("10:20") ? ((IDictionary)Field(owners["10:20"], "Proofs")).Count : 0;
    }
    static void AttachControl(Func<string, object[], object> ui, string control, string artwork, Vector3D from, Vector3D to)
    {
        ui("control", new object[] { control, "main", artwork, .5, 0d, 1d, .25 });
        ui("bind-value", new object[] { control, "gain" });
        ui("constraint", new object[] { control, "line", from, to });
        ui("draggable", new object[] { control, true });
    }
    static MatrixD Pose(Fixture f) => (MatrixD)Field(f.Item("handle"), "Transform");
    static bool Close(double a, double b, double tolerance = 1e-10) => Math.Abs(a - b) <= tolerance;
    static bool Close(MatrixD a, MatrixD b, double tolerance = 1e-10)
    {
        var x = new[] { a.M11,a.M12,a.M13,a.M14,a.M21,a.M22,a.M23,a.M24,a.M31,a.M32,a.M33,a.M34,a.M41,a.M42,a.M43,a.M44 };
        var y = new[] { b.M11,b.M12,b.M13,b.M14,b.M21,b.M22,b.M23,b.M24,b.M31,b.M32,b.M33,b.M34,b.M41,b.M42,b.M43,b.M44 };
        return x.Zip(y).All(p => Close(p.First, p.Second, tolerance));
    }
    static LocalRay Ray(double x, double y = 0)
    {
        if (!LocalRay.TryFromDirection(new Vector3D(x, y, 1), new Vector3D(0, 0, -1), out var ray))
            throw new Exception("Test ray is invalid.");
        return ray;
    }
    static bool Begin(Fixture f, long player, LocalRay ray, out long lease, long? expectedDefinition = null, long? expectedValue = null, long? expectedSource = null)
    {
        var current = Value(f.Ui());
        var args = new object[] { 10L, 20L, "slider", player, ray, expectedDefinition ?? f.Display().Revision,
            expectedValue ?? current.Item2, expectedSource ?? Control(f).SourceRevision, 0L };
        bool accepted = (bool)Call(f.Session, "UiTryBeginValueLease", args);
        lease = (long)args[8]; return accepted;
    }
    static bool Update(Fixture f, long lease, long player, LocalRay ray, out MyTuple<bool, double, long> result)
    {
        var args = new object[] { lease, player, ray, default(MyTuple<bool, double, long>) };
        bool accepted = (bool)Call(f.Session, "UiTryUpdateValueLease", args);
        result = (MyTuple<bool, double, long>)args[3]; return accepted;
    }
    static void End(Fixture f, long lease, long player, bool commit) => Call(f.Session, "UiEndValueLease", lease, player, commit);

    static void NumericCommands()
    {
        using var f = new Fixture(); var ui = f.Ui();
        ui("bundle", new object[] { "main" });
        ui("value", new object[] { "gain", 3.5, 2d, 9d, 3d });
        var initial = Value(ui);
        Check(initial.Item1 == 5 && initial.Item2 > 0, "value declaration snaps midpoint ties upward on the min-anchored grid");
        long definition = f.Display().Revision, data = f.Display().DataRevision;
        var stale = Write(ui, 8, initial.Item2 + 1);
        Check(!stale.Item1 && stale.Item2 == 5 && stale.Item3 == initial.Item2, "stale CAS returns the current authoritative value and revision");
        Check(f.Display().Revision == definition && f.Display().DataRevision == data, "rejected CAS changes neither definition nor data revision");
        Check(Events(ui).Length == 0, "rejected CAS emits no event");
        var max = Write(ui, 100, initial.Item2);
        Check(max.Item1 && max.Item2 == 9 && max.Item3 > initial.Item2, "bounded out-of-range write clamps to off-grid maximum and advances revision");
        var down = Write(ui, -100);
        Check(down.Item1 && down.Item2 == 2 && down.Item3 > max.Item3, "unconditional programmatic write clamps to minimum");
        var tie = Write(ui, 6.5);
        Check(tie.Item1 && tie.Item2 == 8, "midpoint snap selects the larger represented grid value");
        ui("value", new object[] { "continuous", .12345, 0d, 1d });
        Check(Value(ui, "continuous").Item1 == .12345, "omitted zero step preserves continuous values");
        var before = f.RoundTrip(f.Display());
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e13 })
            Reject(() => Write(ui, invalid), "nonfinite or unbounded scalar request rejected before mutation");
        Reject(() => ui("value", new object[] { "bad", 0d, 1d, 1d, 0d }), "empty numeric range rejected");
        Reject(() => ui("value", new object[] { "bad", 0d, 0d, 1d, 2d }), "step larger than range rejected");
        Reject(() => ui("get-value", new object[] { "absent" }), "absent numeric value query rejected");
        Check(f.Display().Revision == before.Revision && f.Display().DataRevision == before.DataRevision,
            "hostile numeric definitions and writes preserve canonical revisions");
        f.Access = false;
        Reject(() => Write(ui, 2), "retained value delegate reauthorizes target access on writes");
        Reject(() => Value(ui), "retained value delegate reauthorizes target access on reads");
        Check(f.Runs == 0, "programmatic value writes never execute PB callbacks inline");
    }
    static void DescriptorAndPublicationIsolation()
    {
        using var f = new Fixture(); var ui = Setup(f);
        var points = new[] { Vector3D.Zero, new Vector3D(.25, .5, 0), Vector3D.UnitX };
        ui("constraint", new object[] { "slider", "path", points });
        var stored = Control(f); var authored = (double[])stored.Points.Clone();
        points[1] = new Vector3D(99, 99, 99);
        Check(stored.Points.SequenceEqual(authored), "path descriptor defensively copies caller-owned point arrays");
        var copy = UiRules.Copy(f.Display());
        copy.Values[0].Value = 99;
        copy.Widgets.Single(w => w.Id == "slider").Control.Points[0] = 99;
        copy.Widgets.Single(w => w.Id == "slider").Control.ReferencePose[0] = 99;
        Check(Value(ui).Item1 == 2 && stored.Points[0] == authored[0] && stored.ReferencePose[0] != 99,
            "UI copy deeply detaches values, descriptor points and immutable reference poses");
        var published = (HoloSnapshot)Call(f.Session, "CaptureSnapshot");
        var wire = f.RoundTrip(published);
        var wireDisplay = wire.Ui.Single(d => d.CallerId == 10 && d.TargetId == 20);
        Check(wireDisplay.Values[0].Value == 2 && wireDisplay.Widgets.Single(w => w.Id == "slider").Control.Points.SequenceEqual(authored),
            "new value and descriptor metadata survive actual snapshot protobuf serialization");
        wireDisplay.Values[0].Value = 98;
        wireDisplay.Widgets.Single(w => w.Id == "slider").Control.Points[0] = 98;
        Check(Value(ui).Item1 == 2 && Control(f).Points[0] == authored[0], "serialized snapshot mutation cannot modify the live numeric state");
        long definition = f.Display().Revision; var pose = Pose(f);
        Reject(() => ui("constraint", new object[] { "slider", "path", new[] { Vector3D.Zero, Vector3D.Zero } }), "degenerate path rejected atomically");
        Reject(() => ui("constraint", new object[] { "slider", "rotation", Vector3D.Zero, Vector3D.Zero, 0d, 1d }), "zero rotation axis rejected atomically");
        Reject(() => ui("bind-value", new object[] { "slider", "absent" }), "missing numeric binding rejected atomically");
        Reject(() => ui("control", new object[] { "foreign", "main", "missing-artwork", 0d, 0d, 1d, 1d }), "control requires registered owner artwork");
        f.Draw(11)("line", new object[] { "foreign-artwork", Vector3D.Zero, Vector3D.UnitX, "cyan" });
        Reject(() => ui("control", new object[] { "foreign", "main", "foreign-artwork", 0d, 0d, 1d, 1d }),
            "another PB's artwork cannot be adopted as this caller's control");
        Check(f.Display().Revision == definition && Control(f).Points.SequenceEqual(authored) && Close(Pose(f), pose),
            "failed binding admission retains descriptor, pose and definition revision");
    }
    static void HostilePublicationAtomicity()
    {
        using var f = new Fixture(); Setup(f);
        var baseline = (HoloSnapshot)Call(f.Session, "CaptureSnapshot");
        var receiver = new HoloMapSession(); Call(receiver, "ApplySnapshot", f.RoundTrip(baseline));
        var scene = ((IDictionary)Field(receiver, "_scenes"))[20L];
        var display = (UiDisplay)Call(receiver, "GetUiDisplay", 10L, 20L);
        void Malformed(Action<UiDisplay> change, string label)
        {
            var candidate = f.RoundTrip(baseline); change(candidate.Ui.Single(d => d.CallerId == 10 && d.TargetId == 20));
            Reject(() => Call(receiver, "ApplySnapshot", candidate), label);
            Check(ReferenceEquals(((IDictionary)Field(receiver, "_scenes"))[20L], scene)
                && ReferenceEquals(Call(receiver, "GetUiDisplay", 10L, 20L), display), label + " preserves prior scene and numeric UI atomically");
        }
        Malformed(d => d.Values[0].Value = 2.5, "untrusted unsnapped scalar publication");
        Malformed(d => d.Values[0].Revision = d.DataRevision + 1, "numeric revision beyond publication data revision");
        Malformed(d => d.Widgets.Single(w => w.Id == "slider").Control.ReferencePose[0] = double.NaN, "nonfinite immutable reference pose");
        Malformed(d => d.Widgets.Single(w => w.Id == "slider").Control.Points = new[] { 0d, 0, 0 }, "incomplete path descriptor");
        Malformed(d => d.Widgets.Single(w => w.Id == "slider").Control.ValueId = "absent", "dangling control numeric binding");
        Malformed(d => d.Widgets.Single(w => w.Id == "slider").Control.SourceRevision = d.DataRevision + 1, "invented control source revision");
    }
    static void BoundPathPoses()
    {
        using var f = new Fixture();
        var basis = MatrixD.CreateScale(1.3) * MatrixD.CreateFromYawPitchRoll(.3, -.2, .4) * MatrixD.CreateTranslation(.3, -.4, .2);
        var ui = Setup(f, 2, 1, basis);
        Check(Close(Pose(f), basis), "initial path binding preserves authored artwork pose without a jump");
        var changed = Write(ui, 6);
        var expected = MatrixD.CreateTranslation(.4, 0, 0) * basis;
        Check(changed.Item1 && Close(Pose(f), expected), "path value composes object-local delta before immutable rotated/scaled reference pose");
        Write(ui, 2);
        Check(Close(Pose(f), basis), "returning to initial path value exactly restores reference pose");
        for (int i = 0; i < 100; i++) { Write(ui, 8); Write(ui, 2); }
        Check(Close(Pose(f), basis), "repeated programmatic path changes do not accumulate incremental pose drift");
        var source = Control(f).SourceRevision;
        var rebased = MatrixD.CreateFromYawPitchRoll(-.2, .1, -.3) * MatrixD.CreateTranslation(.6, .2, .3);
        f.Draw()("transform", new object[] { "handle", rebased });
        Check(Control(f).SourceRevision > source && Close(Pose(f), rebased), "authored transform preempts and rebases source identity without moving numeric value");
        Write(ui, 6);
        Check(Close(Pose(f), MatrixD.CreateTranslation(.4, 0, 0) * rebased), "subsequent value edits use the refreshed immutable authored reference pose");
    }
    static void BoundRotationPoses()
    {
        using var f = new Fixture(); var ui = Setup(f, 5, 0);
        var basis = MatrixD.CreateScale(1.2) * MatrixD.CreateFromYawPitchRoll(.2, .1, -.3) * MatrixD.CreateTranslation(.4, .1, .2);
        f.Draw()("transform", new object[] { "handle", basis });
        var pivot = new Vector3D(.1, .2, 0);
        ui("constraint", new object[] { "slider", "rotation", pivot, new Vector3D(0, 0, 7), 0d, Math.PI });
        Check(Close(Pose(f), basis), "initial rotation binding preserves authored pose at the current numeric reference value");
        Write(ui, 7.5);
        var expected = MatrixD.CreateTranslation(-pivot) * MatrixD.CreateRotationZ(Math.PI / 4) * MatrixD.CreateTranslation(pivot) * basis;
        Check(Close(Pose(f), expected), "rotation maps scalar range to radians and composes around object-local pivot");
        Write(ui, 5);
        Check(Close(Pose(f), basis), "rotation returns to the authored reference pose without angular drift");
        var detached = UiRules.Copy(f.Display()).Widgets.Single(w => w.Id == "slider").Control;
        detached.Pivot[0] = 999; detached.Axis[2] = 999;
        Check(Control(f).Pivot[0] == pivot.X && Control(f).Axis[2] != 999, "rotation descriptor axes and pivots are deeply detached");
    }
    static void BoundPolylinePoses()
    {
        using var f = new Fixture(); var ui = Setup(f, 2, 0);
        var basis = MatrixD.CreateFromYawPitchRoll(.2, .3, -.1) * MatrixD.CreateTranslation(.2, .4, -.1);
        basis.M12 += .2; basis.M23 -= .15;
        f.Draw()("transform", new object[] { "handle", basis });
        ui("constraint", new object[] { "slider", "path", new[] { Vector3D.Zero, new Vector3D(1, 0, 0), new Vector3D(1, 3, 0) } });
        Write(ui, 5);
        // Total authored length is four. Reference value two lies at (.8,0,0),
        // whereas value five lies at (1,1,0); this is not a vertex-index mapping.
        Check(Close(Pose(f), MatrixD.CreateTranslation(.2, 1, 0) * basis),
            "polyline binding uses physical arc length and preserves a sheared reference pose");
        Write(ui, 10);
        Check(Close(Pose(f), MatrixD.CreateTranslation(.2, 3, 0) * basis), "polyline endpoint reaches the last authored vertex");
        Write(ui, 2);
        Check(Close(Pose(f), basis), "polyline return to initial scalar restores immutable sheared artwork pose");
    }
    static void LeasePreemptionAndEvents()
    {
        using var f = new Fixture(); var ui = Setup(f);
        long initialValueRevision = Value(ui).Item2;
        Check(Begin(f, 77, Ray(.2), out long lease) && lease > 0, "canonical control grants a value lease with exact definition/value/source evidence");
        Check(!Begin(f, 88, Ray(.2), out _), "another player cannot preempt an active value lease");
        Check(!Update(f, lease, 88, Ray(.4), out _), "wrong player cannot update the exact numeric lease");
        Check(Update(f, lease, 77, Ray(.4), out var first) && first.Item1 && first.Item2 == 4, "local path drag updates canonical snapped value through real binding");
        Check(Update(f, lease, 77, Ray(.6), out var second) && second.Item1 && second.Item2 == 6, "same lease can advance without definition revision invalidation");
        End(f, lease, 77, true);
        var events = Events(ui);
        Check(events.Length == 3 && events[0].Item1 == "begin" && events[1].Item1 == "change" && events[2].Item1 == "commit", "gesture begin precedes coalesced final change and commit");
        Check(events[0].Item4.Item1 == 2 && events[0].Item4.Item2 == initialValueRevision,
            "begin event records the canonical value and revision before pointer movement");
        Check(events.All(e => e.Item2 == "slider" && e.Item3 == "gain" && e.Item4.Item3 == 77)
            && events.Skip(1).All(e => e.Item4.Item1 == 6 && e.Item4.Item2 == second.Item3),
            "coalesced events retain canonical control/value/revision/player identity");
        Check(Events(ui).Length == 0, "poll drains only this caller's value events");
        Check(!Update(f, lease, 77, Ray(.8), out _), "ended lease cannot be replayed after commit");
        Check(Begin(f, 77, Ray(.6), out long next), "new pointer lease can begin after prior commit");
        var program = Write(ui, 8);
        Check(program.Item1 && !Update(f, next, 77, Ray(.9), out _), "programmatic value write atomically preempts an old pointer lease");
        Check(Value(ui).Item1 == 8, "preempted pointer update cannot overwrite newer programmatic state");
        Check(Begin(f, 77, Ray(.8), out long sourceLease), "new lease begins against latest numeric revision");
        f.Draw()("transform", new object[] { "handle", MatrixD.CreateTranslation(.2, .1, 0) });
        Check(!Update(f, sourceLease, 77, Ray(.9), out _), "authored artwork transform preempts its active pointer lease");
        Check(!Begin(f, 77, Ray(.8), out _, expectedValue: 1), "stale value revision rejects beginning a lease");
        Check(!Begin(f, 77, Ray(.8), out _, expectedDefinition: f.Display().Revision + 1), "invented UI definition revision rejects beginning a lease");
        Check(!Begin(f, 77, Ray(.8), out _, expectedSource: Control(f).SourceRevision + 1), "invented source revision rejects beginning a lease");
    }
    static void ScalarLeaseAndNoOpPreemption()
    {
        using var f = new Fixture(); var ui = Setup(f);
        var value = Value(ui); long data = f.Display().DataRevision, source = Control(f).SourceRevision;
        var unchanged = Write(ui, value.Item1, value.Item2);
        Check(unchanged.Item1 && unchanged.Item3 == value.Item2 && f.Display().DataRevision == data,
            "same-canonical programmatic write succeeds without numeric or data revision churn when idle");
        Check(Begin(f, 77, Ray(.2), out long pointer), "no-op preemption case starts a pointer lease");
        var same = Write(ui, value.Item1, value.Item2);
        Check(same.Item1 && same.Item3 == value.Item2 && f.Display().DataRevision > data && Control(f).SourceRevision > source,
            "explicit same-value write preempts active gesture and rebases source without inventing a numeric change");
        Check(!Update(f, pointer, 77, Ray(.4), out _), "explicit no-op write prevents retained pointer from changing the value later");
        var cancelled = Events(ui);
        Check(cancelled.Any(e => e.Item1 == "cancel" && e.Item2 == "slider" && e.Item4.Item3 == 77),
            "no-op preemption emits a cancellation event for the replaced player gesture");
        value = Value(ui);
        var begin = new object[] { 10L, 20L, "slider", 77L, f.Display().Revision, value.Item2, Control(f).SourceRevision, 0L };
        Check((bool)Call(f.Session, "UiTryBeginValueLeaseScalar", begin) && (long)begin[7] > 0,
            "scalar editor can acquire an exact-revision canonical control lease");
        long scalar = (long)begin[7];
        var invalid = new object[] { scalar, 88L, 7d, default(MyTuple<bool, double, long>) };
        Check(!(bool)Call(f.Session, "UiTryCommitValueLeaseScalar", invalid) && Value(ui).Item1 == 2,
            "wrong-player scalar commit cannot change the bound artwork or value");
        var commit = new object[] { scalar, 77L, 7.5, default(MyTuple<bool, double, long>) };
        Check((bool)Call(f.Session, "UiTryCommitValueLeaseScalar", commit) && ((MyTuple<bool, double, long>)commit[3]).Item2 == 8,
            "scalar lease commit uses the same canonical numeric snapping as pointer and PB writes");
        Check(Close(Pose(f), MatrixD.CreateTranslation(.6, 0, 0)), "scalar editor applies the same immutable-reference artwork delta");
        End(f, scalar, 77, true);
        Check(!(bool)Call(f.Session, "UiTryCommitValueLeaseScalar", commit), "ended scalar lease cannot be replayed after the transport's terminal release");
    }
    static void ValueIdentityReuse()
    {
        using var f = new Fixture(); var ui = Setup(f); long old = Value(ui).Item2;
        Check(Begin(f, 77, Ray(.2), out long lease), "reuse test begins an old-lifetime lease");
        ui("clear", Array.Empty<object>());
        Check(!Update(f, lease, 77, Ray(.4), out _), "UI clear revokes old-lifetime pointer lease");
        ui("bundle", new object[] { "main" }); ui("value", new object[] { "gain", 2d, 0d, 10d, 1d });
        var reused = Value(ui);
        Check(reused.Item2 > old, "removed and reused numeric ID receives a fresh monotonic revision");
        var rejected = Write(ui, 8, old);
        Check(!rejected.Item1 && rejected.Item2 == 2 && rejected.Item3 == reused.Item2,
            "old lifetime CAS cannot mutate a newly declared value with the same ID");
    }
    static void LifetimeAndDeferredWakes()
    {
        using var f = new Fixture(); var ui = Setup(f);
        ui("value-notify", new object[] { "canonical-values-changed" });
        Check(Begin(f, 77, Ray(.2), out long lease), "notification control obtains a lease");
        Update(f, lease, 77, Ray(.4), out _); Update(f, lease, 77, Ray(.6), out _);
        End(f, lease, 77, true);
        Check(f.Runs == 0, "mutation, pointer update and commit only queue wake requests");
        Call(f.Session, "TickUiValueWakes");
        Check(f.Runs == 1 && f.Arguments.Single() == "canonical-values-changed", "deferred wake executes only the registered fixed argument and coalesces changes");
        Call(f.Session, "TickUiValueWakes");
        Check(f.Runs == 1, "flushed wake cannot execute twice without a new value event");
        Check(Begin(f, 77, Ray(.6), out long oldProgram), "lease begins before script replacement");
        f.Program = "replacement-program"; f.Tick(60);
        Check(!Update(f, oldProgram, 77, Ray(.8), out _), "changing PB source invalidates the old interaction lifetime");

        using var hidden = new Fixture(); var hiddenUi = Setup(hidden);
        Check(Begin(hidden, 77, Ray(.2), out long hiddenLease), "visible artwork starts a lease");
        hidden.Draw()("visible", new object[] { "handle", false }); hidden.Tick(60);
        Check(!Update(hidden, hiddenLease, 77, Ray(.4), out _), "hidden artwork invalidates a retained pointer lease");

        using var power = new Fixture(); Setup(power);
        Check(Begin(power, 77, Ray(.2), out long powerLease), "powered caller starts a lease");
        power.CallerWorking = false; power.Tick(60);
        Check(!Update(power, powerLease, 77, Ray(.4), out _), "turning off the PB invalidates its pointer lease");
    }
    static void BoundedEventQueue()
    {
        using var f = new Fixture(); var ui = Setup(f);
        int accepted = 0;
        for (int i = 0; i < 80; i++)
        {
            double current = Value(ui).Item1;
            long player = 1000 + i;
            if (!Begin(f, player, Ray(current / 10), out long lease))
                throw new Exception("Bounded event probe could not begin a serial authorized gesture.");
            double next = current == 6 ? 4 : 6;
            if (!Update(f, lease, player, Ray(next / 10), out var result) || result.Item2 != next)
                throw new Exception("Bounded event probe could not update a serial authorized gesture.");
            End(f, lease, player, true); accepted++;
        }
        var events = Events(ui);
        Check(accepted == 80 && events.Length > 0 && events.Length <= 64, "distinct serial player gestures remain bounded by the 64-event queue limit");
        Check(events.All(e => e.Item4.Item2 > 0 && e.Item4.Item1 >= 0 && e.Item4.Item1 <= 10),
            "queue saturation never fabricates noncanonical values or revisions");
        Check(Events(ui).Length == 0 && f.Runs == 0, "saturated queue drains once and never executes a PB inline");
    }
    static void DeclarationProofCache()
    {
        using var f = new Fixture(); var draw = f.Draw();
        const string svg = "<svg viewBox='0 0 2 1'><rect width='2' height='1'/></svg>";
        const string changedSvg = "<svg viewBox='0 0 3 1'><rect width='3' height='1'/></svg>";
        draw("svg", new object[] { "handle", svg, MatrixD.Identity, 4 });
        var raw = (Geometry)Field(f.Item("handle"), "Geometry");
        Check(raw.Points.Length == 1 && raw.Points[0] == Vector3D.Zero,
            "SVG artwork reaches real retained declaration path with its uncompiled geometry placeholder");
        var ui = f.Ui(); ui("bundle", new object[] { "main" }); ui("value", new object[] { "gain", 0d, 0d, 10d, 1d });
        AttachControl(ui, "slider", "handle", Vector3D.Zero, Vector3D.UnitX);
        Check(ProofCompiles(f) == 1, "SVG enclosure compiles once when the real declaration is attached to a control");
        var enclosure = (Geometry)Field(Proof(f), "Enclosure");
        Check(enclosure.Points.Length == 8 && enclosure.Points.Max(p => p.X) - enclosure.Points.Min(p => p.X) > 1.5,
            "cached eight-corner enclosure represents authored SVG rather than the origin placeholder");
        Check(Begin(f, 77, Ray(0), out long lease), "declaration-backed artwork acquires a gesture using its cached enclosure");
        for (int i = 1; i <= 9; i++)
            if (!Update(f, lease, 77, Ray(i / 10d), out _)) throw new Exception("SVG cached-proof gesture update failed.");
        End(f, lease, 77, true);
        for (int i = 0; i < 40; i++) Write(ui, i % 11);
        Check(ProofCompiles(f) == 1, "pointer updates and programmatic scalar writes do not recompile unchanged SVG bounds");
        draw("svg", new object[] { "handle", changedSvg, MatrixD.Identity, 4 });
        Check(ProofCompiles(f) == 2, "replacing SVG content compiles exactly one fresh enclosure at source-change notification");
        draw("svg", new object[] { "handle", changedSvg, MatrixD.Identity, 4 });
        Check(ProofCompiles(f) == 2, "resubmitting unchanged SVG content reuses its immutable enclosure");
        draw("text", new object[] { "handle", "HI", Vector3D.Zero, .5, "white", "start" });
        Check(ProofCompiles(f) == 3 && ((Geometry)Field(f.Item("handle"), "Geometry")).Points.Length == 1,
            "text declaration replacement compiles once after final text metadata replaces the placeholder");
        for (int i = 0; i < 20; i++) Write(ui, i % 11);
        Check(ProofCompiles(f) == 3, "text-backed scalar updates reuse the cached source enclosure");
        double textValue = Value(ui).Item1;
        Check(Begin(f, 77, Ray(textValue / 10), out long textLease)
            && Update(f, textLease, 77, Ray((textValue - 1) / 10), out _),
            "text-backed artwork acquires and updates a gesture with its cached declaration enclosure");
        End(f, textLease, 77, true);
        Check(ProofCompiles(f) == 3, "text-backed pointer updates do not recompile unchanged declaration bounds");
        draw("text", new object[] { "handle", "HO", Vector3D.Zero, .5, "white", "start" });
        Check(ProofCompiles(f) == 4, "changed text content compiles one new enclosure");
    }
    static void CoupledDeclarationBoundsAtomicity()
    {
        foreach (bool text in new[] { false, true })
        {
            using var f = new Fixture(); var draw = f.Draw();
            // First candidate remains within 25 metres at +25; the second source
            // extends beyond its origin, which an origin-only placeholder misses.
            draw("line", new object[] { "safe", new Vector3D(-1, 0, 0), new Vector3D(-.5, 0, 0), "cyan" });
            if (text) draw("text", new object[] { "edge", "HI", Vector3D.Zero, .5, "white", "start" });
            else draw("svg", new object[] { "edge", "<svg viewBox='0 0 1 1'><rect width='1' height='1'/></svg>", MatrixD.Identity, 4 });
            var ui = f.Ui(); ui("bundle", new object[] { "main" }); ui("value", new object[] { "gain", 0d, 0d, 1d, 0d });
            AttachControl(ui, "slider", "safe", Vector3D.Zero, new Vector3D(25, 0, 0));
            AttachControl(ui, "edge-control", "edge", Vector3D.Zero, new Vector3D(25, 0, 0));
            var numeric = Value(ui); long definition = f.Display().Revision, data = f.Display().DataRevision;
            var safePose = (MatrixD)Field(f.Item("safe"), "Transform"); var edgePose = (MatrixD)Field(f.Item("edge"), "Transform");
            int safeCompiles = ProofCompiles(f, "safe"), edgeCompiles = ProofCompiles(f, "edge");
            Check(Begin(f, 77, Ray(0), out long lease), "coupled " + (text ? "text" : "SVG") + " case grants the valid initial gesture");
            Reject(() => Write(ui, 1, numeric.Item2), "real " + (text ? "text" : "SVG") + " enclosure rejects a candidate beyond the display radius");
            var after = Value(ui);
            Check(after.Item1 == numeric.Item1 && after.Item2 == numeric.Item2 && f.Display().Revision == definition && f.Display().DataRevision == data,
                "failed declaration bounds preparation preserves numeric value and every publication revision");
            Check(Close((MatrixD)Field(f.Item("safe"), "Transform"), safePose) && Close((MatrixD)Field(f.Item("edge"), "Transform"), edgePose),
                "later coupled candidate failure cannot commit any earlier prepared artwork pose");
            Check((bool)Call(f.Session, "UiValidateValueLease", lease, 77L), "failed programmatic preparation does not preempt the valid active gesture");
            Check(ProofCompiles(f, "safe") == safeCompiles && ProofCompiles(f, "edge") == edgeCompiles,
                "rejected pose candidate still reuses cached source enclosures");
            var events = Events(ui);
            Check(events.Length == 1 && events[0].Item1 == "begin", "failed coupled pose preparation emits no change or cancellation event");
            End(f, lease, 77, false);
            Reject(() => draw("transform", new object[] { "edge", MatrixD.CreateTranslation(25, 0, 0) }),
                "explicit script transform checks actual declaration bounds before replacing its bound source pose");
            Check(Close((MatrixD)Field(f.Item("edge"), "Transform"), edgePose), "failed script transform retains previous declaration pose");
        }
    }
    static void LcdPlanarAdmissionAndMutation()
    {
        using var f = new Fixture(lcd: true); var ui = Setup(f);
        var draw = f.Draw();
        draw("svg", new object[] { "handle", "<svg viewBox='0 0 1 1'><rect width='1' height='1'/></svg>", MatrixD.Identity, 4 });
        long revision = f.Display().Revision, data = f.Display().DataRevision;
        var points = (double[])Control(f).Points.Clone();
        Reject(() => ui("constraint", new object[] { "slider", "line", Vector3D.Zero, Vector3D.UnitZ }), "LCD cannot admit a Z-only path");
        Reject(() => ui("constraint", new object[] { "slider", "path", new[] { Vector3D.Zero, Vector3D.UnitX, new Vector3D(1, 1, .01) } }),
            "LCD cannot admit a nonplanar polyline");
        Reject(() => ui("constraint", new object[] { "slider", "rotation", Vector3D.Zero, Vector3D.UnitX, 0d, Math.PI }),
            "LCD cannot admit a rotation axis outside local Z");
        Reject(() => ui("constraint", new object[] { "slider", "rotation", Vector3D.UnitZ, Vector3D.UnitZ, 0d, Math.PI }),
            "LCD cannot admit a rotation pivot outside local XY");
        Check(f.Display().Revision == revision && f.Display().DataRevision == data && Control(f).Points.SequenceEqual(points),
            "failed LCD constraints preserve prior admitted descriptor and revisions");
        ui("constraint", new object[] { "slider", "rotation", Vector3D.Zero, new Vector3D(0, 0, -3), 0d, Math.PI });
        Check(Write(ui, 8).Item1 && Close(Pose(f), MatrixD.CreateRotationZ(-.6 * Math.PI)),
            "LCD permits normalized negative Z-axis rotation with an XY pivot");
        var pose = Pose(f); var offset = (Vector3D)Field(f.Scene, "Offset"); var rotation = (Vector3D)Field(f.Scene, "Rotation"); double scale = (double)Field(f.Scene, "Scale");
        data = f.Display().DataRevision;
        Reject(() => draw("transform", new object[] { "handle", MatrixD.CreateTranslation(0, 0, .01) }),
            "LCD final source pose cannot move declared artwork outside the canvas plane");
        Reject(() => draw("transform", new object[] { "handle", MatrixD.CreateRotationX(.2) }),
            "LCD final source pose cannot tilt declared two-dimensional artwork out of plane");
        Reject(() => draw("view", new object[] { Vector3D.Zero, new Vector3D(.2, 0, 0), 1d }),
            "LCD view candidate proves the final declared artwork remains planar");
        Reject(() => draw("view", new object[] { new Vector3D(0, 0, .01), Vector3D.Zero, 1d }),
            "LCD view translation cannot shift controls out of the physical canvas plane");
        Check(Close(Pose(f), pose) && f.Display().DataRevision == data && (Vector3D)Field(f.Scene, "Offset") == offset
            && (Vector3D)Field(f.Scene, "Rotation") == rotation && (double)Field(f.Scene, "Scale") == scale,
            "failed LCD final-pose or view proof retains artwork, scene view and numeric metadata atomically");
    }
    static void NonfiniteDeterminantPublications()
    {
        using var f = new Fixture(); Setup(f);
        var baseline = (HoloSnapshot)Call(f.Session, "CaptureSnapshot");
        var receiver = new HoloMapSession(); Call(receiver, "ApplySnapshot", f.RoundTrip(baseline));
        var scene = ((IDictionary)Field(receiver, "_scenes"))[20L]; var display = Call(receiver, "GetUiDisplay", 10L, 20L);
        var infinity = MatrixD.Identity; infinity.M11 = infinity.M22 = infinity.M33 = 1e110;
        var nan = MatrixD.Identity; nan.M11 = nan.M12 = nan.M13 = nan.M21 = nan.M22 = nan.M23 = nan.M31 = nan.M32 = nan.M33 = 1e200;
        Check(double.IsInfinity(infinity.Determinant()), "finite diagonal coefficients produce the intended overflowing determinant fixture");
        Check(double.IsNaN(nan.Determinant()), "finite dense coefficients produce the intended NaN determinant fixture");
        foreach (var matrix in new[] { infinity, nan })
        {
            var values = new[] { matrix.M11,matrix.M12,matrix.M13,matrix.M14,matrix.M21,matrix.M22,matrix.M23,matrix.M24,
                matrix.M31,matrix.M32,matrix.M33,matrix.M34,matrix.M41,matrix.M42,matrix.M43,matrix.M44 };
            Check(values.All(v => !double.IsNaN(v) && !double.IsInfinity(v)), "hostile determinant case contains only finite authored coefficients");
            Reject(() => UiValueRules.Matrix(values), "finite coefficients with nonfinite determinant cannot define a control reference pose");
            var candidate = f.RoundTrip(baseline); candidate.Ui[0].Widgets.Single(w => w.Id == "slider").Control.ReferencePose = values;
            Reject(() => Call(receiver, "ApplySnapshot", candidate), "actual snapshot rejects nonfinite determinant before canonical replacement");
            Check(ReferenceEquals(((IDictionary)Field(receiver, "_scenes"))[20L], scene) && ReferenceEquals(Call(receiver, "GetUiDisplay", 10L, 20L), display),
                "nonfinite determinant snapshot retains the old scene and UI atomically");
        }
    }
    static void ViewAndSharedSourcePreemption()
    {
        using var f = new Fixture(); var ui = Setup(f); var draw = f.Draw();
        Check(Begin(f, 77, Ray(.2), out long lease) && Update(f, lease, 77, Ray(.4), out _), "view-change fixture begins a live pointer gesture");
        var numeric = Value(ui); long source = Control(f).SourceRevision; var reference = Pose(f);
        draw("view", new object[] { new Vector3D(0, .8, 0), new Vector3D(0, 0, .2), 1.2 });
        Check(!Update(f, lease, 77, Ray(.6), out _), "accepted display view change cancels an old source-view gesture");
        Check(Control(f).SourceRevision > source && Control(f).ReferenceValue == numeric.Item1 && Close(Pose(f), reference)
            && Value(ui).Item2 == numeric.Item2, "view-change notification rebases source evidence against the current pose and scalar without inventing a scalar change");
        Write(ui, 6);
        Check(Close(Pose(f), MatrixD.CreateTranslation(.2, 0, 0) * reference), "value write after a view change uses the rebased current reference pose");
        long definition = f.Display().Revision, data = f.Display().DataRevision; var pose = Pose(f);
        int animationCount = ((IDictionary)Field(f.Session, "_drawAnimations")).Count;
        Reject(() => draw("animate", new object[] { "handle", "x", 0d, 1d, 1d, true }), "later transform animation cannot compete with retained numeric artwork binding");
        Reject(() => draw("animate", new object[] { "handle", "opacity", 0d, 1d, 1d, true }), "later appearance animation also requires explicit numeric unbinding");
        Check(((IDictionary)Field(f.Session, "_drawAnimations")).Count == animationCount && Close(Pose(f), pose)
            && f.Display().Revision == definition && f.Display().DataRevision == data, "rejected animation admission preserves numeric source ownership and pose");

        draw("line", new object[] { "other", Vector3D.Zero, Vector3D.UnitX, "cyan" });
        AttachControl(ui, "other-control", "other", Vector3D.Zero, Vector3D.UnitX);
        double before = Value(ui).Item1;
        Check(Begin(f, 77, Ray(before / 10), out long shared), "control A begins a lease on the value also bound to source B");
        var poseA = Pose(f); long revision = Value(ui).Item2;
        draw("transform", new object[] { "other", MatrixD.CreateTranslation(.5, .1, 0) });
        Check(!Update(f, shared, 77, Ray(.8), out _), "mutation of coupled source B cancels the active gesture through source A");
        Check(Value(ui).Item1 == before && Value(ui).Item2 == revision && Close(Pose(f), poseA)
            && Close((MatrixD)Field(f.Item("other"), "Transform"), MatrixD.CreateTranslation(.5, .1, 0)),
            "shared-source preemption retains scalar and source A while committing only B's authored pose");
        Check(Events(ui).Any(e => e.Item1 == "cancel" && e.Item2 == "slider" && e.Item4.Item3 == 77),
            "shared-source mutation records cancellation for the preempted control A gesture");
    }
    static void ReadOnlyNumericQueries()
    {
        using var empty = new Fixture(); var emptyUi = empty.Ui();
        Set(empty.Session, "_dirty", false);
        long emptyDefinition = (long)Field(empty.Session, "_uiRevision"), emptyData = (long)Field(empty.Session, "_uiDataRevision");
        Reject(() => Value(emptyUi, "absent"), "missing numeric query rejects without declaring an owner or value");
        Check(((IDictionary)Field(empty.Session, "_uiValueOwners")).Count == 0
            && ((IDictionary)Field(empty.Session, "_scenes")).Count == 0
            && ((IDictionary)Field(empty.Session, "_uiDisplays")).Count == 0,
            "missing query allocates no target scene, UI display or numeric owner");
        Check(!(bool)Field(empty.Session, "_dirty") && (long)Field(empty.Session, "_uiRevision") == emptyDefinition
            && (long)Field(empty.Session, "_uiDataRevision") == emptyData,
            "missing query leaves dirty state and global revision counters untouched");

        using var f = new Fixture(); var ui = Setup(f); var display = f.Display(); var value = Value(ui);
        Set(f.Session, "_dirty", false);
        long definition = display.Revision, data = display.DataRevision;
        long definitionCounter = (long)Field(f.Session, "_uiRevision"), dataCounter = (long)Field(f.Session, "_uiDataRevision");
        int compiles = ProofCompiles(f), owners = ((IDictionary)Field(f.Session, "_uiValueOwners")).Count;
        for (int i = 0; i < 20; i++)
        {
            var current = Value(ui);
            if (current.Item1 != value.Item1 || current.Item2 != value.Item2) throw new Exception("Read-only numeric query returned inconsistent state.");
        }
        Check(ReferenceEquals(f.Display(), display) && display.Revision == definition && display.DataRevision == data
            && (long)Field(f.Session, "_uiRevision") == definitionCounter && (long)Field(f.Session, "_uiDataRevision") == dataCounter
            && ProofCompiles(f) == compiles && ((IDictionary)Field(f.Session, "_uiValueOwners")).Count == owners && !(bool)Field(f.Session, "_dirty"),
            "successful repeated get-value reads preserve canonical object, source cache, dirty bit and all definition/data counters");
        f.Program = "new-owner-program";
        Reject(() => Value(ui), "read of an expired PB lifetime fails without mutating server metadata");
        Check(ReferenceEquals(f.Display(), display) && display.Revision == definition && display.DataRevision == data
            && (long)Field(f.Session, "_uiDataRevision") == dataCounter && !(bool)Field(f.Session, "_dirty")
            && ((IDictionary)Field(f.Session, "_uiValueOwners")).Count == owners,
            "read-only lifetime rejection does not retire owners or advance publication state before the tick lifecycle");
    }
    static void SourceProofRetirement()
    {
        using (var removedControl = new Fixture())
        {
            var ui = Setup(removedControl); Check(ProofCount(removedControl) == 1, "control attaches one owned source proof");
            ui("remove", new object[] { "slider" });
            Check(ProofCount(removedControl) == 0 && removedControl.Items.Contains("10:handle"),
                "removing a UI control prunes its source proof while preserving caller artwork");
        }
        using (var removedArtwork = new Fixture())
        {
            Setup(removedArtwork); removedArtwork.Draw()("remove", new object[] { "handle" });
            Check(ProofCount(removedArtwork) == 0 && removedArtwork.Display().Widgets.All(w => w.Control == null),
                "removing bound artwork drops both its cached proof and dependent control");
        }
        using (var cleared = new Fixture())
        {
            var ui = Setup(cleared); ui("clear", Array.Empty<object>());
            Check(ProofCount(cleared) == 0 && ((IDictionary)Field(cleared.Session, "_uiValueOwners")).Count == 0,
                "UI clear makes no source-proof owner reachable from the session");
        }
        using (var retired = new Fixture())
        {
            Setup(retired); retired.Program = "retired-source"; retired.Tick(60);
            Check(ProofCount(retired) == 0 && ((IDictionary)Field(retired.Session, "_uiValueOwners")).Count == 0,
                "PB lifetime retirement releases its complete source-proof owner");
        }
        using (var failed = new Fixture())
        {
            var ui = Setup(failed); var draw = failed.Draw();
            draw("svg", new object[] { "orphan", "<svg viewBox='0 0 10 10'><rect width='10' height='10'/></svg>", MatrixD.CreateScale(4), 4 });
            Reject(() => ui("control", new object[] { "orphan-control", "main", "orphan", 0d, 0d, 1d, 1d }),
                "out-of-bounds declaration candidate cannot become a bound control");
            Check(ProofCount(failed) == 2 && failed.Display().Widgets.All(w => w.Id != "orphan-control"),
                "failed admission retains only bounded memoized source evidence, not canonical control metadata");
            draw("remove", new object[] { "orphan" });
            Check(ProofCount(failed) == 1 && ProofCompiles(failed) == 1,
                "artwork removal also prunes a failed-candidate proof with no bound control and preserves the live source proof");
        }
    }
    static void NumericOnlyPublicationAndControlHeaders()
    {
        using (var only = new Fixture())
        {
            var ui = only.Ui();
            Check(((IDictionary)Field(only.Session, "_scenes")).Count == 0, "numeric-only fixture starts without a drawing scene");
            ui("value", new object[] { "gain", 2d, 0d, 10d, 1d });
            var published = (HoloSnapshot)Call(only.Session, "CaptureSnapshot");
            Check(published.Scenes.Any(s => s.ConsoleId == 20) && published.Ui.Single().Values.Single().Value == 2,
                "numeric-only declaration creates and publishes the target scene required by UI references");
            var receiver = new HoloMapSession(); Call(receiver, "ApplySnapshot", only.RoundTrip(published));
            Check(((UiDisplay)Call(receiver, "GetUiDisplay", 10L, 20L)).Values.Single().Value == 2,
                "numeric-only display survives actual full snapshot serialization and scene/UI validation");
        }
        using var f = new Fixture(); var endpoint = Setup(f);
        var baseline = (HoloSnapshot)Call(f.Session, "CaptureSnapshot"); var receiver2 = new HoloMapSession();
        Call(receiver2, "ApplySnapshot", f.RoundTrip(baseline)); var scene = ((IDictionary)Field(receiver2, "_scenes"))[20L];
        var display = Call(receiver2, "GetUiDisplay", 10L, 20L);
        var malformed = f.RoundTrip(baseline); malformed.Ui[0].Widgets.Single(w => w.Id == "slider").Control = null;
        Reject(() => Call(receiver2, "ApplySnapshot", malformed), "control action header without its descriptor is not a valid replicated widget");
        Check(ReferenceEquals(((IDictionary)Field(receiver2, "_scenes"))[20L], scene)
            && ReferenceEquals(Call(receiver2, "GetUiDisplay", 10L, 20L), display), "descriptor-less control snapshot preserves the old scene and UI atomically");
        endpoint("hit", new object[] { "run", "main", 0d, 0d, 1d, 1d, "pb", "fixed" });
        long definition = f.Display().Revision, data = f.Display().DataRevision;
        Reject(() => endpoint("bind", new object[] { "run", "control", "" }), "legacy action rebinding cannot invent an unregistered artwork control");
        Reject(() => endpoint("hit", new object[] { "anonymous", "main", 0d, 0d, 1d, 1d, "control", "" }),
            "legacy hit declaration cannot create a control action without structured artwork metadata");
        Check(f.Display().Revision == definition && f.Display().DataRevision == data
            && f.Display().Widgets.Single(w => w.Id == "run").ActionKind == "pb" && f.Display().Widgets.All(w => w.Id != "anonymous"),
            "failed legacy control actions preserve prior widget action and all canonical revisions");
    }
    static UiDisplay DenseMetadata(long caller, long target)
    {
        var d = new UiDisplay { CallerId = caller, TargetId = target, Revision = long.MaxValue, DataRevision = long.MaxValue,
            ValueNotify = new string('界', 128) };
        d.Bundles.Add(new UiBundle { Id = "main".PadRight(24, 'm') });
        d.Values.Add(new UiNumericValue { Id = "gain".PadRight(24, 'g'), Min = -5e11, Max = 5e11, Step = 0, Value = 0, Revision = long.MaxValue });
        var pose = new[] { 1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1 };
        for (int widget = 0; widget < 16; widget++)
        {
            var points = new double[256 * 3];
            for (int i = 0; i < 256; i++) { points[3 * i] = i + .123456789; points[3 * i + 1] = Math.Sin(i) + .123456789; points[3 * i + 2] = Math.Cos(i) + .123456789; }
            d.Widgets.Add(new UiWidget { Id = ("widget" + widget).PadRight(24, 'w'), Bundle = d.Bundles[0].Id,
                X = 0, Y = 0, Width = 1, Height = 1, Label = new string('界', 64), ActionKind = "control", Argument = "",
                Control = new UiControlData { Artwork = ("artwork" + widget).PadRight(64, 'a'), Draggable = true,
                    ValueId = d.Values[0].Id, ConstraintKind = "path", Points = points, ReferencePose = (double[])pose.Clone(),
                    ReferenceValue = 0, SourceRevision = long.MaxValue } });
        }
        return d;
    }
    static void MetadataReserveAndCapabilities()
    {
        using var f = new Fixture(); var ui = Setup(f);
        string capability = (string)ui("capabilities", Array.Empty<object>());
        Check(capability.Split(';').Contains("values=1") && capability.Split(';').Contains("mouse=client-provider"),
            "UI capability advertises numeric protocol and provider-dependent mouse input without claiming native readiness");
        var dense = DenseMetadata(1001, 2001); UiRules.Display(dense);
        int bytes = f.Serialize(new List<UiDisplay> { dense }).Length;
        Check(bytes > 90000 && bytes <= UiRules.ReplicationReserve - 16384,
            "sixteen maximal-path artwork control descriptors serialize within the network reserve while keeping its fixed margin");
        var oversized = Enumerable.Range(0, 4).Select(i => DenseMetadata(1001 + i, 2001 + i)).ToList();
        foreach (var declaration in oversized) UiRules.Display(declaration);
        Check(oversized.Sum(d => d.Widgets.Count) < UiRules.MaxTotalWidgets && oversized.Sum(d => d.Values.Count) < UiValueRules.MaxTotalValues
            && oversized.Count + 1 < UiRules.MaxDisplays && f.Serialize(oversized).Length > UiRules.ReplicationReserve - 16384,
            "reserve-limit fixture exceeds bytes while remaining below every count admission limit");
        // Seed already validated DTO declarations only to stress the real byte
        // admission gate independently of the per-scene artwork quota.
        var declarations = (IDictionary)Field(f.Session, "_uiDisplays");
        foreach (var declaration in oversized) declarations[declaration.CallerId + ":" + declaration.TargetId] = declaration;
        long definition = f.Display().Revision, data = f.Display().DataRevision;
        long definitionCounter = (long)Field(f.Session, "_uiRevision"), dataCounter = (long)Field(f.Session, "_uiDataRevision");
        Set(f.Session, "_dirty", false);
        Reject(() => ui("value-notify", new object[] { "oversized-metadata" }), "real numeric definition admission rejects aggregate metadata beyond reserve margin");
        Check(f.Display().Revision == definition && f.Display().DataRevision == data && f.Display().ValueNotify == null
            && (long)Field(f.Session, "_uiRevision") == definitionCounter && (long)Field(f.Session, "_uiDataRevision") == dataCounter
            && !(bool)Field(f.Session, "_dirty"), "byte-reserve rejection retains caller publication, notification, counters and dirty state atomically");
    }
    static int PendingWakes(Fixture f) => ((IEnumerable)Field(f.Session, "_uiValueWakePending")).Cast<object>().Count();
    static void FlushWake(Fixture f, int tick) { Set(f.Session, "_ticks", tick); Call(f.Session, "TickUiValueWakes"); }
    static void WakeExceptionBusyAndSuccessorGuards()
    {
        foreach (bool throws in new[] { false, true })
        {
            using var f = new Fixture(); var ui = Setup(f);
            ui("value-notify", new object[] { "deferred-values" }); Write(ui, 4);
            f.OnRun = _ => throws ? throw new InvalidOperationException("Fixture PB callback failure") : false;
            FlushWake(f, 0);
            Check(f.Runs == 1 && PendingWakes(f) == 1,
                (throws ? "throwing" : "busy") + " PB callback is contained and requeues the same still-valid pending owner");
            FlushWake(f, 1);
            Check(f.Runs == 1, "failed wake retries remain subject to the normal coalescing cadence");
            f.OnRun = _ => true; FlushWake(f, 6);
            Check(f.Runs == 2 && PendingWakes(f) == 0 && f.Arguments.All(a => a == "deferred-values"),
                "recovered PB callback consumes exactly its fixed-argument wake after the retry cadence");
        }
        foreach (bool successorEvent in new[] { false, true })
        {
            using var f = new Fixture(); var ui = Setup(f);
            ui("value-notify", new object[] { "old-owner" }); Write(ui, 4);
            f.OnRun = _ =>
            {
                ui("clear", Array.Empty<object>());
                var next = Setup(f); next("value-notify", new object[] { "new-owner" });
                if (successorEvent) Write(next, 5);
                f.OnRun = nextArgument => true;
                return false;
            };
            FlushWake(f, 0);
            Check(f.Runs == 1 && f.Arguments[0] == "old-owner" && PendingWakes(f) == (successorEvent ? 1 : 0),
                "failed old callback cannot requeue stale ownership or erase a successor's independently scheduled wake");
            FlushWake(f, 6);
            Check(f.Runs == (successorEvent ? 2 : 1) && PendingWakes(f) == 0
                && (!successorEvent || f.Arguments[1] == "new-owner"),
                "callback clear/recreate preserves only the successor's own event and fixed notification argument");
        }
    }
    static void GlobalImportAdmission()
    {
        using var f = new Fixture(); Setup(f);
        var baseline = (HoloSnapshot)Call(f.Session, "CaptureSnapshot");
        var receiver = new HoloMapSession(); Call(receiver, "ApplySnapshot", f.RoundTrip(baseline));
        var oldScene = ((IDictionary)Field(receiver, "_scenes"))[20L]; var oldUi = Call(receiver, "GetUiDisplay", 10L, 20L);
        var values = new List<UiDisplay>();
        for (int display = 0; display < 5; display++)
        {
            var declaration = new UiDisplay { CallerId = 3000 + display, TargetId = 20, Revision = 1, DataRevision = 1 };
            for (int value = 0; value < UiValueRules.MaxValues; value++)
                declaration.Values.Add(new UiNumericValue { Id = "v" + value, Value = 0, Min = 0, Max = 1, Step = 0, Revision = 1 });
            UiRules.Display(declaration); values.Add(declaration);
        }
        Check(values.Count <= UiRules.MaxDisplays && values.Sum(d => d.Values.Count) > UiValueRules.MaxTotalValues
            && f.Serialize(values).Length < UiRules.ReplicationReserve,
            "global-value import fixture is locally valid, below byte reserve, and exceeds only aggregate numeric value count");
        var tooMany = f.RoundTrip(baseline); tooMany.Ui = f.RoundTrip(values);
        Reject(() => Call(receiver, "ApplySnapshot", tooMany), "full snapshot rejects aggregate numeric value overflow before replacement");
        Check(ReferenceEquals(((IDictionary)Field(receiver, "_scenes"))[20L], oldScene)
            && ReferenceEquals(Call(receiver, "GetUiDisplay", 10L, 20L), oldUi), "global-value import failure preserves prior scene and UI atomically");

        var dense = Enumerable.Range(0, 4).Select(i => DenseMetadata(4000 + i, 20)).ToList();
        foreach (var declaration in dense) UiRules.Display(declaration);
        Check(dense.Sum(d => d.Widgets.Count) <= UiRules.MaxTotalWidgets && dense.Sum(d => d.Values.Count) <= UiValueRules.MaxTotalValues
            && dense.Count <= UiRules.MaxDisplays && f.Serialize(dense).Length > UiRules.ReplicationReserve,
            "UI reserve import fixture is locally valid and below aggregate count limits but exceeds serialized metadata bytes");
        var oversized = f.RoundTrip(baseline); oversized.Ui = f.RoundTrip(dense);
        Reject(() => Call(receiver, "ApplySnapshot", oversized), "full snapshot rejects UI byte reserve overflow before replacement");
        Check(ReferenceEquals(((IDictionary)Field(receiver, "_scenes"))[20L], oldScene)
            && ReferenceEquals(Call(receiver, "GetUiDisplay", 10L, 20L), oldUi), "UI byte reserve import failure preserves prior scene and UI atomically");
    }
    static void ResetPassiveFields(UiDisplay display)
    {
        display.Revision = display.DataRevision = 1;
        foreach (var value in display.Values) { value.Value = 0; value.Revision = 1; }
        foreach (var widget in display.Widgets) if (widget.Control != null)
        { widget.Control.ReferenceValue = 0; widget.Control.SourceRevision = 1; widget.Label = null; }
    }
    static void TunePathsNear(Fixture f, List<UiDisplay> templates, Func<List<UiDisplay>> publications, int targetBytes)
    {
        // Tune only declaration geometry before admission. Once frozen, later
        // scalar/source edits keep every repeated-array cardinality unchanged.
        var widgets = templates.SelectMany(d => d.Widgets).Where(w => w.Control != null && w.Control.Points != null).ToArray();
        var original = widgets.Select(w => (double[])w.Control.Points.Clone()).ToArray();
        foreach (var widget in widgets) { widget.Control.Points = widget.Control.Points.Take(6).ToArray(); widget.Label = null; }
        int Bytes() => f.Serialize(publications()).Length;
        if (Bytes() > targetBytes) throw new Exception("Near-limit fixture has too much non-path metadata.");
        for (int i = 0; i < widgets.Length; i++)
        {
            int low = 2, high = original[i].Length / 3;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                widgets[i].Control.Points = original[i].Take(middle * 3).ToArray();
                if (Bytes() <= targetBytes) low = middle; else high = middle - 1;
            }
            widgets[i].Control.Points = original[i].Take(low * 3).ToArray();
        }
        var pad = widgets.Last();
        int best = 0;
        for (int length = 1; length <= 64; length++)
        { pad.Label = new string('x', length); if (Bytes() <= targetBytes) best = length; else break; }
        pad.Label = best == 0 ? null : new string('x', best);
        foreach (var display in templates) UiRules.Display(display);
        if (Bytes() > targetBytes || targetBytes - Bytes() > 32) throw new Exception("Near-limit fixture could not approach the byte boundary.");
    }
    static Dictionary<object, object> DictionaryState(object dictionary)
    {
        var result = new Dictionary<object, object>();
        foreach (DictionaryEntry entry in (IDictionary)dictionary) result.Add(entry.Key, entry.Value);
        return result;
    }
    static bool SameDictionary(object dictionary, Dictionary<object, object> prior)
    {
        var current = (IDictionary)dictionary;
        return current.Count == prior.Count && prior.All(p => current.Contains(p.Key) && ReferenceEquals(current[p.Key], p.Value));
    }
    static List<UiDisplay> SeedNearAdmission(Fixture f, int headroom = 16)
    {
        var templates = Enumerable.Range(0, 3).Select(i => DenseMetadata(6000 + i, 20)).ToList();
        foreach (var display in templates) ResetPassiveFields(display);
        var declarations = (IDictionary)Field(f.Session, "_uiDisplays");
        foreach (var display in templates) declarations[display.CallerId + ":" + display.TargetId] = display;
        List<UiDisplay> All() => declarations.Values.Cast<UiDisplay>().ToList();
        TunePathsNear(f, templates, All, UiMetadataRules.AdmissionBytes - headroom);
        return templates;
    }
    static void LegacyMetadataAdmissionAtomicity()
    {
        using var f = new Fixture(); var ui = Setup(f);
        ui("hit", new object[] { "run", "main", 0d, 0d, 1d, 1d, "pb", "fixed" });
        ui("button", new object[] { "caption", "main", 0d, .5, 2d, .3, "A", "pb", "fixed" });
        SeedNearAdmission(f);
        int bytes = f.Serialize(f.Captured()).Length;
        Check(bytes <= UiMetadataRules.AdmissionBytes && UiMetadataRules.AdmissionBytes - bytes <= 48,
            "legacy mutation fixture is valid numeric metadata immediately below the declaration admission margin");
        Check(Begin(f, 77, Ray(.2), out long lease), "near-limit legacy fixture retains an active numeric gesture");
        var oldDisplay = f.Display(); long definition = oldDisplay.Revision, data = oldDisplay.DataRevision;
        long definitionCounter = (long)Field(f.Session, "_uiRevision"), dataCounter = (long)Field(f.Session, "_uiDataRevision");
        byte[] oldMetadata = f.Serialize(f.Captured());
        var items = Field(f.Scene, "Items"); var labels = Field(f.Scene, "Labels"); var layers = Field(f.Scene, "Layers");
        var priorItems = DictionaryState(items); var priorLabels = DictionaryState(labels); var priorLayers = DictionaryState(layers);
        Set(f.Session, "_dirty", false);
        void Atomic(Action mutation, string label)
        {
            Reject(mutation, label + " exceeds shared admission bytes");
            Check(ReferenceEquals(f.Display(), oldDisplay) && oldDisplay.Revision == definition && oldDisplay.DataRevision == data
                && f.Serialize(f.Captured()).SequenceEqual(oldMetadata) && (long)Field(f.Session, "_uiRevision") == definitionCounter
                && (long)Field(f.Session, "_uiDataRevision") == dataCounter && !(bool)Field(f.Session, "_dirty"),
                label + " preserves all numeric/legacy metadata, counters and dirty state");
            Check(ReferenceEquals(Field(f.Scene, "Items"), items) && ReferenceEquals(Field(f.Scene, "Labels"), labels)
                && ReferenceEquals(Field(f.Scene, "Layers"), layers) && SameDictionary(items, priorItems)
                && SameDictionary(labels, priorLabels) && SameDictionary(layers, priorLayers),
                label + " rejects before render items, captions or layers can be replaced");
            Check((bool)Call(f.Session, "UiValidateValueLease", lease, 77L), label + " cannot cancel the valid numeric gesture before admission");
        }
        Atomic(() => ui("button", new object[] { "extra", "main", 0d, -.5, 2d, .3, new string('界', 64), "pb", "" }), "new legacy button");
        Atomic(() => ui("hit", new object[] { "extra-hit", "main", 0d, -.5, 2d, .3, "pb", new string('界', 256) }), "new legacy hit area");
        Atomic(() => ui("bind", new object[] { "run", "pb", new string('界', 256) }), "legacy action argument expansion");
        Atomic(() => ui("button", new object[] { "caption", "main", 0d, .5, 2d, .3, new string('界', 64), "pb", "fixed" }), "existing legacy caption expansion");
        End(f, lease, 77, false);
    }
    static List<UiDisplay> MaxPassiveShape()
    {
        var displays = new List<UiDisplay>();
        for (int display = 0; display < UiRules.MaxDisplays; display++)
        {
            var d = DenseMetadata(7000 + display, 20);
            d.Widgets.RemoveRange(8, d.Widgets.Count - 8); d.ValueNotify = null;
            d.Values.Clear();
            for (int value = 0; value < 16; value++)
                d.Values.Add(new UiNumericValue { Id = "v" + value, Min = 0, Max = 1, Step = 0, Value = 0, Revision = 1 });
            ResetPassiveFields(d);
            for (int widget = 0; widget < d.Widgets.Count; widget++) d.Widgets[widget].Control.ValueId = d.Values[widget].Id;
            displays.Add(d);
        }
        return displays;
    }
    static void PassiveMetadataGrowthPublication()
    {
        using (var f = new Fixture())
        {
            var declarations = MaxPassiveShape();
            TunePathsNear(f, declarations, () => declarations, UiMetadataRules.AdmissionBytes - 16);
            int before = f.Serialize(declarations).Length;
            var grown = declarations.Select(UiRules.Copy).ToList();
            var basis = MatrixD.CreateFromYawPitchRoll(.3, .2, .1) * MatrixD.CreateTranslation(1, 2, 3);
            var pose = new[] { basis.M11,basis.M12,basis.M13,basis.M14,basis.M21,basis.M22,basis.M23,basis.M24,
                basis.M31,basis.M32,basis.M33,basis.M34,basis.M41,basis.M42,basis.M43,basis.M44 };
            foreach (var d in grown)
            {
                d.Revision = d.DataRevision = long.MaxValue;
                foreach (var value in d.Values) { value.Value = .5; value.Revision = long.MaxValue; }
                foreach (var widget in d.Widgets)
                { widget.Control.ReferenceValue = .5; widget.Control.SourceRevision = long.MaxValue; Array.Copy(pose, widget.Control.ReferencePose, pose.Length); }
                UiRules.Display(d);
            }
            int after = f.Serialize(grown).Length, growth = after - before;
            Console.WriteLine("UI metadata passive growth: " + before + " -> " + after + " bytes; delta " + growth
                + ", analytical bound " + UiMetadataRules.PassiveGrowthBound + ", reserved headroom " + UiMetadataRules.GrowthMargin + ".");
            Check(declarations.Count == UiRules.MaxDisplays && declarations.Sum(d => d.Values.Count) == UiValueRules.MaxTotalValues
                && declarations.Sum(d => d.Widgets.Count) == UiRules.MaxTotalWidgets,
                "passive growth fixture saturates all permitted display/value/control cardinalities");
            Check(before <= UiMetadataRules.AdmissionBytes && UiMetadataRules.AdmissionBytes - before <= 48
                && growth > 0 && growth <= UiMetadataRules.PassiveGrowthBound && UiMetadataRules.PassiveGrowthBound < UiMetadataRules.GrowthMargin,
                "genuine protobuf default-zero and maximum revision growth stays below the analytical reserved headroom");
            Check(after > UiMetadataRules.AdmissionBytes && after <= UiRules.ReplicationReserve,
                "passive scalar/source changes may use growth headroom without exceeding the full publication reserve");
            Check(declarations.Zip(grown).All(pair => pair.First.Widgets.Zip(pair.Second.Widgets).All(w =>
                w.First.Control.Points.Length == w.Second.Control.Points.Length && w.First.Control.ReferencePose.Length == w.Second.Control.ReferencePose.Length
                && w.First.Control.Points.SequenceEqual(w.Second.Control.Points) && w.First.Control.ConstraintKind == w.Second.Control.ConstraintKind)),
                "passive growth measurement changes scalar/reference values and revisions while leaving declaration arrays and constraints fixed");
            Call(f.Session, "GetScene", 20L);
            var registry = (IDictionary)Field(f.Session, "_uiDisplays");
            foreach (var display in grown) registry[display.CallerId + ":" + display.TargetId] = display;
            var published = (HoloSnapshot)Call(f.Session, "CaptureSnapshot"); var receiver = new HoloMapSession();
            Call(receiver, "ApplySnapshot", f.RoundTrip(published));
            Check(((List<UiDisplay>)Call(receiver, "CaptureUiDisplays")).Count == UiRules.MaxDisplays,
                "grown near-limit metadata publishes and imports a genuine full snapshot using the full reserve");
        }
        using (var f = new Fixture())
        {
            var ui = Setup(f, 0, 0); SeedNearAdmission(f);
            int before = f.Serialize(f.Captured()).Length;
            Set(f.Session, "_uiDataRevision", long.MaxValue - 4);
            Check(Write(ui, .5).Item1, "actual scalar edit can consume reserved default/revision growth beyond declaration admission size");
            f.Draw()("transform", new object[] { "handle", MatrixD.CreateTranslation(.2, .1, 0) });
            int after = f.Serialize(f.Captured()).Length;
            Console.WriteLine("UI metadata actual scalar/source growth: " + before + " -> " + after + " bytes.");
            Check(before <= UiMetadataRules.AdmissionBytes && after > UiMetadataRules.AdmissionBytes && after <= UiRules.ReplicationReserve,
                "actual source rebase and scalar edit consume only reserved publication headroom");
            var published = (HoloSnapshot)Call(f.Session, "CaptureSnapshot"); var receiver = new HoloMapSession();
            Call(receiver, "ApplySnapshot", f.RoundTrip(published));
            var imported = (UiDisplay)Call(receiver, "GetUiDisplay", 10L, 20L);
            Check(imported.Values.Single().Value == .5 && imported.Widgets.Single(w => w.Id == "slider").Control.ReferenceValue == .5,
                "actual near-limit scalar and source-reference updates survive full snapshot import without a false margin rejection");
        }
    }
    static void UiImportWithoutUtilities()
    {
        foreach (bool omitted in new[] { false, true })
        {
            using var f = new Fixture(); Setup(f);
            var baseline = (HoloSnapshot)Call(f.Session, "CaptureSnapshot"); var receiver = new HoloMapSession();
            Call(receiver, "ApplySnapshot", f.RoundTrip(baseline));
            var candidate = f.RoundTrip(baseline); candidate.Ui = omitted ? null : new List<UiDisplay>();
            f.DisableUtilities();
            Call(receiver, "ApplySnapshot", candidate);
            Check(((IDictionary)Field(receiver, "_scenes")).Contains(20L) && ((IDictionary)Field(receiver, "_uiDisplays")).Count == 0,
                (omitted ? "omitted" : "empty") + " UI publication trivially fits and imports without a Utilities serializer");
        }
        using var nonempty = new Fixture(); Setup(nonempty);
        var full = (HoloSnapshot)Call(nonempty.Session, "CaptureSnapshot"); var existing = new HoloMapSession();
        Call(existing, "ApplySnapshot", nonempty.RoundTrip(full));
        var oldScene = ((IDictionary)Field(existing, "_scenes"))[20L]; var oldUi = Call(existing, "GetUiDisplay", 10L, 20L);
        var next = nonempty.RoundTrip(full); nonempty.DisableUtilities();
        Reject(() => Call(existing, "ApplySnapshot", next), "nonempty UI import without a serializer fails closed on metadata budget validation");
        Check(ReferenceEquals(((IDictionary)Field(existing, "_scenes"))[20L], oldScene)
            && ReferenceEquals(Call(existing, "GetUiDisplay", 10L, 20L), oldUi), "missing-serializer rejection preserves prior scene and numeric UI atomically");
    }
    static void LegacyBoundary()
    {
        using var f = new Fixture();
        var ui = f.Ui();
        ui("bundle", new object[] { "main" });
        ui("hit", new object[] { "run", "main", 0d, 0d, 1d, 1d, "pb", "fixed-command" });
        var published = f.Captured();
        Check(published.Count == 1 && published[0].Widgets.Single().Argument == "fixed-command", "legacy canonical widget is admitted through the real endpoint");
        var roundtrip = f.RoundTrip(published);
        Check(roundtrip[0].Widgets.Single().Argument == "fixed-command", "legacy widget survives actual protobuf publication");
        long revision = f.Display().Revision;
        roundtrip[0].Widgets[0].Argument = "detached";
        published[0].Bundles[0].Visible = false;
        Check(f.Display().Widgets[0].Argument == "fixed-command" && f.Display().Bundles[0].Visible, "publication and protobuf copies are detached from canonical metadata");
        f.Access = false;
        Reject(() => ui("bind", new object[] { "run", "pb", "hostile" }), "changed display ownership rejects a retained delegate");
        f.Access = true; f.SameConstruct = false;
        Reject(() => ui("bind", new object[] { "run", "pb", "hostile" }), "changed construct membership rejects a retained delegate");
        f.SameConstruct = true; f.Server = false;
        Reject(() => ui("clear", Array.Empty<object>()), "client-side endpoint cannot mutate canonical values");
        f.Server = true; f.Registry.Remove(10);
        Reject(() => ui("clear", Array.Empty<object>()), "unregistered caller cannot use a retained endpoint");
        Check(f.Display().Revision == revision && f.Display().Widgets[0].Argument == "fixed-command", "hostile mutation attempts preserve canonical metadata and revision");
        Check(f.Runs == 0, "value declarations and rejected endpoint calls never execute the PB inline");
    }
}

public class UiValueStageProxy : DispatchProxy
{
    public Func<MethodInfo, object[], object> Handler;
    protected override object Invoke(MethodInfo method, object[] args) => Handler(method, args);
    public static object Make(Type type, Func<MethodInfo, object[], object> handler)
    { var value = Create(type, typeof(UiValueStageProxy)); ((UiValueStageProxy)value).Handler = handler; return value; }
}
