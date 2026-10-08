using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRageMath;

// Offline integration against the installed game ABI. Gateway doubles supply
// entity registration, player context, physics results and secure transport;
// PB registration, native receiver, simulation tick, authority and protobuf are
// production paths. This is not a live native GUI or physical-input proof.
internal static class PersistentUiDragTests
{
    static int checks;
    static void Check(bool good, string label)
    {
        if (!good) throw new Exception("Persistent native UI viewer: " + label);
        checks++;
    }
    static object Call(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == args.Length);
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    static object Field(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value);

    sealed class Fixture : IDisposable
    {
        public readonly ClientReplicationTests.GatewayScope Gateway = new() { Server = true, Dedicated = true };
        public readonly HoloMapSession Session = new();
        public readonly Dictionary<long, object> Registry = new();
        public readonly List<(ushort Channel, byte[] Bytes, ulong Recipient, bool Reliable)> Sent = new();
        public readonly List<byte[]> ClientPackets = new();
        public readonly Func<string, object[], object> Ui, Draw;
        public bool Access = true, SameConstruct = true, Working = true, Dead, OnFoot = true, Connected = true, Occluded;
        public bool AsClient, Escape;
        readonly object Player;
        public Vector3D Position = new(.2, 0, 1), Head = new(.2, 0, 1), Direction = Vector3D.Forward;
        public string Program = "persistent-viewer-owner";
        public int Runs, RayCasts;
        public Fixture()
        {
            Registry[10] = DrawTestProxy.Make(typeof(IMyProgrammableBlock), (method, args) => method.Name switch
            {
                "get_EntityId" => 10L, "get_OwnerId" => 77L, "get_Closed" => false,
                "get_IsWorking" => Working, "get_ProgramData" => Program,
                "IsSameConstructAs" => SameConstruct, "HasPlayerAccess" => Access,
                "TryRun" => Run(), _ => throw new Exception("Persistent viewer PB: " + method.Name)
            });
            ((DrawTestProxy)Registry[10]).ProgramSource = () => Program;
            Registry[20] = DrawTestProxy.Make(typeof(IMyProjector), (method, args) => method.Name switch
            {
                "get_EntityId" => 20L, "get_Closed" => false, "get_IsWorking" => Working,
                "HasPlayerAccess" => Access, "get_WorldMatrix" => MatrixD.Identity,
                "GetPosition" => Vector3D.Zero, "get_BlockDefinition" => Activator.CreateInstance(method.ReturnType),
                _ => throw new Exception("Persistent viewer target: " + method.Name)
            });
            object character = DrawTestProxy.Make(typeof(NativeUiDragCharacter), (method, args) => method.Name switch
            {
                "get_EntityId" => 30L, "get_Entity" => Registry.GetValueOrDefault(30L), "get_Closed" => false, "get_IsDead" => Dead,
                "GetPosition" => Position, "GetHeadMatrix" => MatrixD.CreateWorld(Head, Direction, Vector3D.Up),
                _ => throw new Exception("Persistent viewer character: " + method.Name)
            });
            object controller = DrawTestProxy.Make(typeof(IMyEntityController), (method, args) => method.Name switch
            {
                "get_ControlledEntity" => OnFoot ? character : null,
                _ => throw new Exception("Persistent viewer controller: " + method.Name)
            });
            object player = DrawTestProxy.Make(typeof(IMyPlayer), (method, args) => method.Name switch
            {
                "get_SteamUserId" => 200UL, "get_IdentityId" => 77L, "get_Character" => character,
                "get_Controller" => controller, _ => throw new Exception("Persistent viewer player: " + method.Name)
            });
            Player = player;
            Registry[30] = character;
            Gateway.Install("Session", (method, args) => method.Name == "get_LocalHumanPlayer" ? null : throw new Exception("Persistent viewer session: " + method.Name));
            Gateway.Install("Multiplayer", (method, args) =>
            {
                if (method.Name == "get_IsServer") return !AsClient;
                if (method.Name == "get_ServerId") return 100UL;
                if (method.Name == "SendMessageToServer") { ClientPackets.Add((byte[])args[1]); return true; }
                if (method.Name == "SendMessageTo")
                {
                    Sent.Add(((ushort)args[0], (byte[])args[1], (ulong)args[2], (bool)args[3]));
                    return true;
                }
                throw new Exception("Persistent viewer multiplayer: " + method.Name);
            });
            Gateway.Install("Entities", (method, args) => method.Name == "GetEntityById" ? Registry.GetValueOrDefault((long)args[0]) : throw new Exception("Persistent viewer entities: " + method.Name));
            Gateway.Install("Players", (method, args) =>
            {
                if (method.Name != "GetPlayers") throw new Exception("Persistent viewer players: " + method.Name);
                var predicate = args.Length > 1 ? (Func<IMyPlayer, bool>)args[1] : null;
                if (Connected && (predicate == null || predicate((IMyPlayer)player))) ((List<IMyPlayer>)args[0]).Add((IMyPlayer)player);
                return null;
            });
            Gateway.Install("Physics", (method, args) =>
            {
                if (method.Name != "CastRay") throw new Exception("Persistent viewer physics: " + method.Name);
                RayCasts++;
                if (Occluded)
                {
                    object obstruction = DrawTestProxy.Make(typeof(IHitInfo), (m, a) => m.Name switch
                    {
                        "get_HitEntity" => Registry[20], "get_Position" => Head + Direction * .5,
                        _ => throw new Exception("Persistent viewer obstruction: " + m.Name)
                    });
                    ((List<IHitInfo>)args[2]).Add((IHitInfo)obstruction);
                }
                return null;
            });
            Draw = (Func<string, object[], object>)Call(Session, "DrawEndpoint", Registry[10]);
            Draw("target", new[] { Registry[20] });
            foreach (string artwork in new[] { "entry-art", "first-art", "second-art", "click-art", "other-art" })
            {
                Draw("line", new object[] { artwork, Vector3D.Zero, Vector3D.UnitX, "cyan" });
                if (artwork != "entry-art") Draw("transform", new object[] { artwork,
                    MatrixD.CreateTranslation(artwork == "second-art" ? 1 : 2, artwork == "click-art" || artwork == "other-art" ? 1 : 0, 0) });
            }
            Ui = (Func<string, object[], object>)Call(Session, "UiEndpoint", Registry[10]);
            Ui("target", new[] { Registry[20] });
            Ui("bundle", new object[] { "main" });
            Ui("bundle", new object[] { "other" });
            Ui("value", new object[] { "gain", 2d, 0d, 10d, 1d });
            Ui("value", new object[] { "trim", 2d, 0d, 10d, 1d });
            Control("entry", "main", "entry-art");
            Control("first", "main", "first-art", "gain");
            Control("second", "main", "second-art", "trim");
            Control("click", "main", "click-art");
            Control("other-click", "other", "other-art");
            Set(Scene, "Offset", Vector3D.Zero);
            Set(Scene, "Scale", 1d);
            Set(Scene, "Rotation", Vector3D.Zero);
            Set(Scene, "VolumeRange", 0d);
        }
        void Control(string id, string bundle, string artwork, string value = null)
        {
            Ui("control", new object[] { id, bundle, artwork, .5, 0d, 1d, .25 });
            if (value == null) return;
            Ui("bind-value", new object[] { id, value });
            Ui("constraint", new object[] { id, "line", Vector3D.Zero, Vector3D.UnitX });
            Ui("draggable", new object[] { id, true });
        }
        bool Run() { Runs++; return true; }
        public void EnableClient()
        {
            object originalUtilities = MyAPIGateway.Utilities;
            Gateway.Install("Utilities", (method, args) =>
            {
                if (method.Name == "get_IsDedicated") return false;
                if (method.Name == "RegisterMessageHandler" || method.Name == "UnregisterMessageHandler" || method.Name == "SendModMessage") return null;
                return method.Invoke(originalUtilities, args);
            });
            object camera = DrawTestProxy.Make(typeof(IMySession).GetProperty("Camera").PropertyType, (method, args) => method.Name switch
            {
                "get_Position" => Head,
                "get_WorldMatrix" => MatrixD.CreateWorld(Head, Direction, Vector3D.Up),
                "get_ViewMatrix" => MatrixD.CreateLookAt(Head, Head + Direction, Vector3D.Up),
                "get_ProjectionMatrix" => MatrixD.CreatePerspectiveFieldOfView(2.4, 1920d / 1080, .1, 100),
                "get_ViewportSize" => new Vector2(1920, 1080), "get_ViewportOffset" => Vector2.Zero,
                _ => throw new Exception("Persistent viewer camera: " + method.Name)
            });
            object cameraController = DrawTestProxy.Make(typeof(IMySession).GetProperty("CameraController").PropertyType,
                (method, args) => method.Name == "get_IsInFirstPersonView" ? true : throw new Exception("Persistent viewer camera controller: " + method.Name));
            Gateway.Install("Session", (method, args) => method.Name switch
            {
                "get_LocalHumanPlayer" => null, "get_Player" => Player, "get_Camera" => camera, "get_CameraController" => cameraController,
                _ => throw new Exception("Persistent viewer local session: " + method.Name)
            });
            Gateway.Install("Input", (method, args) => method.Name switch
            {
                "GetMouseAreaSize" => new Vector2(1920, 1080),
                "IsNewKeyPressed" => Escape && (VRage.Input.MyKeys)args[0] == VRage.Input.MyKeys.Escape,
                _ => throw new Exception("Persistent viewer input: " + method.Name)
            });
            Gateway.Install("Gui", (method, args) => method.Name switch
            {
                "get_ChatEntryVisible" => false,
                "get_IsCursorVisible" => Field(Session, "_uiPointer") is PointerInputBridge currentPointer && currentPointer.Token > 0,
                "get_GetCurrentScreen" => Enum.Parse(method.ReturnType, "None"),
                _ => throw new Exception("Persistent viewer GUI: " + method.Name)
            });
            AsClient = true;
        }
        public UiDisplay Display => (UiDisplay)Call(Session, "GetUiDisplay", 10L, 20L);
        public object Scene => ((IDictionary)Field(Session, "_scenes"))[20L];
        public UiWidget Widget(string control) => Display.Widgets.Single(widget => widget.Id == control);
        public UiNumericValue Value(string id) => Display.Values.Single(value => value.Id == id);
        public UiDragTransactions Transactions => (UiDragTransactions)Field(Session, "_uiDrags");
        public int Viewers => ((IDictionary)Field(Session, "_uiDragViewers")).Count;
        public long CurrentViewerId => Viewers == 0 ? 0 : (long)Field(((IDictionary)Field(Session, "_uiDragViewers"))[200UL], "Id");
        public long LegacySequence => ((IDictionary)Field(Session, "_uiPeers"))[200UL] is object peer ? (long)Field(peer, "Sequence") : 0;
        public int PendingValueWakes => ((IEnumerable)Field(Session, "_uiValueWakePending")).Cast<object>().Count();
        public int ValueLeases => ((IDictionary)Field(Session, "_uiValueLeases")).Count;
        public int ValueLocks => ((IDictionary)Field(Session, "_uiValueLocks")).Count;
        public UiDragRequest Focus(string control = "entry", long requestId = 1, long sequence = 1) => new()
        {
            Kind = (int)UiDragKind.Focus, CallerId = 10, TargetId = 20, ControlId = control,
            DefinitionRevision = Display.Revision, ValueRevision = Widget(control).Control.SourceRevision,
            RequestId = requestId, Sequence = sequence, Mode = (int)UiDragMode.FocusedPointer
        };
        public UiDragRequest Begin(UiDragAck viewer, string control, long requestId, long sequence) => new()
        {
            Kind = (int)UiDragKind.Begin, CallerId = 10, TargetId = 20, ControlId = control,
            DefinitionRevision = Display.Revision, ValueRevision = Value(Widget(control).Control.ValueId).Revision,
            RequestId = requestId, Sequence = sequence, Mode = (int)UiDragMode.FocusedPointer,
            ViewerId = viewer.ViewerId, Value = Value(Widget(control).Control.ValueId).Value
        };
        public static UiDragRequest Request(UiDragAck grant, UiDragKind kind, long sequence, double value = 0) => new()
        {
            Kind = (int)kind, CallerId = grant.CallerId, TargetId = grant.TargetId, ControlId = grant.ControlId,
            DefinitionRevision = grant.DefinitionRevision, ValueRevision = grant.ValueRevision,
            RequestId = grant.RequestId, Sequence = sequence, LeaseId = grant.LeaseId,
            Mode = (int)UiDragMode.FocusedPointer, Value = value, ViewerId = grant.ViewerId
        };
        public UiPress Press(string widget, long sequence, int action = 4, long viewerId = 0) => new()
        { CallerId = 10, TargetId = 20, WidgetId = widget, Revision = Display.Revision, Sequence = sequence, Action = action, ViewerId = viewerId };
        public void Receive(UiDragRequest request, ulong sender = 200)
            => ReceiveBytes(UiDragWire.Wrap(MyAPIGateway.Utilities.SerializeToBinary(request)), sender);
        public void Receive(UiPress press, ulong sender = 200)
            => ReceiveBytes(MyAPIGateway.Utilities.SerializeToBinary(press), sender);
        public void ReceiveBytes(byte[] bytes, ulong sender = 200) => Call(Session, "ReceiveUiMessage", (ushort)49784, bytes, sender, false);
        public void Tick(int tick) { Set(Session, "_ticks", tick); Call(Session, "TickUiNetwork"); }
        public void ClientTick(int tick) { Set(Session, "_ticks", tick); Call(Session, "TickUiDragClient"); }
        public bool UseEntry()
        {
            Set(Session, "_uiHoveredDisplay", Display); Set(Session, "_uiHoveredWidget", Widget("entry"));
            return (bool)Call(Session, "TryConsumeUiUse", 20L);
        }
        int forwardedClientPackets, forwardedServerPackets;
        public void RoundTrip(int tick)
        {
            AsClient = false;
            while (forwardedClientPackets < ClientPackets.Count)
                Call(Session, "ReceiveUiMessage", (ushort)49784, ClientPackets[forwardedClientPackets++], 200UL, false);
            Tick(tick);
            AsClient = true;
            while (forwardedServerPackets < Sent.Count)
                Call(Session, "ReceiveUiMessage", (ushort)49784, Sent[forwardedServerPackets++].Bytes, 100UL, true);
            Tick(tick);
        }
        public UiDragAck Ack => MyAPIGateway.Utilities.SerializeFromBinary<UiDragAck>(UiDragWire.Body(Sent.Last(sent => UiDragWire.IsDrag(sent.Bytes)).Bytes));
        public UiAck PressAck => MyAPIGateway.Utilities.SerializeFromBinary<UiAck>(Sent.Last(sent => !UiDragWire.IsDrag(sent.Bytes)).Bytes);
        public UiDragAck[] DragAcks => Sent.Where(sent => UiDragWire.IsDrag(sent.Bytes))
            .Select(sent => MyAPIGateway.Utilities.SerializeFromBinary<UiDragAck>(UiDragWire.Body(sent.Bytes))).ToArray();
        public MyTuple<string, string, string, MyTuple<double, long, long>>[] Events()
            => (MyTuple<string, string, string, MyTuple<double, long, long>>[])Ui("poll-value-events", Array.Empty<object>());
        public bool HeadHits(string control)
        {
            object[] args = { Display, Widget(control), Head, Direction, Vector3D.Zero, 0L, false };
            return (bool)Call(Session, "TryUiControlHit", args);
        }
        public UiDragAck Grant(string control = "entry", int tick = 0, long requestId = 1, long sequence = 1)
        {
            int before = Sent.Count;
            Receive(Focus(control, requestId, sequence));
            Check(Sent.Count == before && ValueLeases == 0 && Viewers == 0, "Focus bytes queue without acquiring canonical value authority inline");
            Tick(tick);
            var ack = Ack;
            Check(UiDragWire.Valid(ack) && ack.Kind == (int)UiDragKind.Focus && ack.Status == 0 && !ack.Terminal
                && ack.ViewerId > 0 && ack.LeaseId == ack.ViewerId, "native Focus ACK grants a distinct persistent viewer identity");
            Check(ack.TileId == 20 && ack.CharacterId == 30 && ack.ValueRevision == Widget(control).Control.SourceRevision
                && Viewers == 1 && ValueLeases == 0 && ValueLocks == 0 && Transactions.ActiveLeases == 0,
                "Focus binds actual tile and character/source stamp while owning no numeric lock");
            return ack;
        }
        public UiDragAck Start(UiDragAck viewer, string control = "first", long requestId = 2, long sequence = 2, int tick = 1)
        {
            Receive(Begin(viewer, control, requestId, sequence)); Tick(tick);
            var ack = Ack;
            Check(ack.Kind == (int)UiDragKind.Begin && ack.Status == 0 && !ack.Terminal && ack.LeaseId > 0
                && ack.ViewerId == viewer.ViewerId && ack.ControlId == control && Viewers == 1
                && ValueLeases == 1 && ValueLocks == 1 && Transactions.ActiveLeases == 1,
                "same-bundle numeric gesture acquires its own value lease under current viewer");
            return ack;
        }
        public void Dispose() { Call(Session, "ClearUiDragNetwork"); Gateway.Dispose(); }
    }
    sealed class NativePointerProvider
    {
        public long Frame = 1;
        public int Flags = 18, Pressed, Held, Released, Acquires, Releases;
        public double X = .5, Y = .5;
        public object Endpoint(string operation, object[] arguments)
        {
            if (operation == "acquire") { Acquires++; return new MyTuple<long, int>(700, 1); }
            if (operation == "release") { Check((long)arguments[0] == 700, "provider release carries the acquired native token"); Releases++; return null; }
            if (operation == "sample")
            {
                Check((long)arguments[0] == 700, "provider samples retain their acquired native token");
                return new MyTuple<long, int, MyTuple<double, double>, MyTuple<int, int, int>>(
                    Frame, Flags, new MyTuple<double, double>(X, Y), new MyTuple<int, int, int>(Pressed, Held, Released));
            }
            throw new Exception("Persistent native pointer operation: " + operation);
        }
        public void Point(double worldX) { X = .5 + (worldX - .2) / (2 * Math.Tan(1.2) * (1920d / 1080)); Y = .5; }
        public void Install(Fixture fixture)
        {
            Call(fixture.Session, "InitializeUiPointer");
            Call(fixture.Session, "ReceiveUiPointerProvider",
                new MyTuple<string, int, Func<string, object[], object>>(PointerInputBridge.Version, 1, Endpoint));
        }
    }

    public static int Run()
    {
        checks = 0;
        FocusIsValueFree();
        MultipleGesturesRetainViewer();
        ViewerClicksAreBundleScoped();
        RetiredClickCannotOperateSuccessor();
        KeepAliveAndExpiry();
        CurrentContextInvalidation();
        ClientMouseUpRetainsPointerEscapeReleases();
        ClientContextLossReleasesPointer();
        CloseReleasesViewerAndValue();
        PendingFocusCloseCannotGrant();
        RetiredIdentitiesCannotOperateSuccessor();
        return checks;
    }
    static void FocusIsValueFree()
    {
        foreach (bool draggable in new[] { false, true })
        {
            using var fixture = new Fixture();
            string control = draggable ? "first" : "entry";
            if (draggable) fixture.Head = fixture.Position = new Vector3D(2.2, 0, 1);
            var value = fixture.Value("gain"); long revision = value.Revision;
            var viewer = fixture.Grant(control);
            Check(value.Value == 2 && value.Revision == revision && fixture.Events().Length == 0,
                "Focus on " + (draggable ? "draggable" : "click") + " artwork creates no begin/value event");
            Check(fixture.Sent.All(sent => sent.Channel == 49784 && sent.Recipient == 200 && sent.Reliable)
                && fixture.RayCasts > 0 && fixture.Runs == 0, "Focus traverses secure protobuf gateway and actual authority rays without inline PB execution");
            if (draggable)
            {
                var write = (MyTuple<bool, double, long>)fixture.Ui("set-value", new object[] { "gain", 4d, revision });
                Check(write.Item1 && write.Item2 == 4 && write.Item3 > revision && fixture.Viewers == 1 && fixture.ValueLeases == 0,
                    "programmatic CAS remains available while a draggable entry owns only viewer focus");
                fixture.Receive(Fixture.Request(viewer, UiDragKind.KeepAlive, 2)); fixture.Tick(1);
                Check(fixture.Ack.Kind == (int)UiDragKind.KeepAlive && fixture.Ack.Status == 0 && fixture.Viewers == 1,
                    "numeric revision changes do not replace the viewer's original control source stamp");
                fixture.Start(viewer, "first", 2, 3, 2);
            }
        }
        using (var fixture = new Fixture())
        {
            var request = fixture.Focus(); request.ValueRevision++;
            fixture.Receive(request); fixture.Tick(0);
            Check(fixture.Ack.Terminal && fixture.Ack.Status == (int)UiDragStatus.Stale && fixture.Viewers == 0 && fixture.ValueLeases == 0,
                "Focus rejects a numeric or stale stamp in place of the initial control source revision");
        }
    }
    static void MultipleGesturesRetainViewer()
    {
        using var fixture = new Fixture(); var viewer = fixture.Grant();
        Check(fixture.HeadHits("entry") && !fixture.HeadHits("first") && !fixture.HeadHits("second"),
            "native entry head ray misses both later numeric artwork controls");
        var first = fixture.Start(viewer);
        fixture.Receive(Fixture.Request(first, UiDragKind.Move, 3, 6.2)); fixture.Tick(7);
        var moved = fixture.Ack;
        Check(moved.Status == 0 && moved.Value == 6 && fixture.Value("gain").Value == 6 && fixture.Viewers == 1,
            "first control commits snapped pointer value under frozen entrypoint authority");
        fixture.Receive(Fixture.Request(moved, UiDragKind.End, 4, 7.6)); fixture.Tick(8);
        Check(fixture.Ack.Kind == (int)UiDragKind.End && fixture.Ack.Terminal && fixture.Ack.Status == 0
            && fixture.Value("gain").Value == 8 && fixture.ValueLeases == 0 && fixture.ValueLocks == 0
            && fixture.Transactions.ActiveLeases == 0 && fixture.Viewers == 1,
            "mouse-up End commits and releases only numeric authority while persistent viewer remains");
        var second = fixture.Start(viewer, "second", 3, 5, 9);
        Check(second.LeaseId != first.LeaseId && second.ViewerId == first.ViewerId,
            "a different control in the bundle starts without another Focus/Use and receives a fresh gesture lease");
        fixture.Receive(Fixture.Request(second, UiDragKind.End, 6, 3.2)); fixture.Tick(10);
        Check(fixture.Value("trim").Value == 3 && fixture.Value("gain").Value == 8 && fixture.Viewers == 1
            && fixture.ValueLeases == 0 && fixture.DragAcks.Count(ack => ack.Kind == (int)UiDragKind.Focus) == 1,
            "two distinct values commit in the original viewer using exactly one initial Focus packet");
        var events = fixture.Events();
        Check(events.Any(e => e.Item1 == "commit" && e.Item2 == "first" && e.Item3 == "gain" && e.Item4.Item1 == 8)
            && events.Any(e => e.Item1 == "commit" && e.Item2 == "second" && e.Item3 == "trim" && e.Item4.Item1 == 3),
            "PB polls canonical commit events for both gestures");
    }
    static void ViewerClicksAreBundleScoped()
    {
        using var fixture = new Fixture(); var viewer = fixture.Grant();
        Check(!fixture.HeadHits("click") && !fixture.HeadHits("other-click"), "click controls lie away from the head-ray entry region");
        fixture.Receive(fixture.Press("click", 10, viewerId: viewer.ViewerId)); fixture.Tick(1);
        Check(fixture.PressAck.Accepted && fixture.PressAck.ActionKind == "control" && fixture.PressAck.ViewerId == viewer.ViewerId && fixture.Viewers == 1
            && fixture.ValueLeases == 0 && fixture.Events().Any(e => e.Item1 == "click" && e.Item2 == "click" && e.Item4.Item3 == 77),
            "viewer Action 4 routes authorized same-bundle click into the PB event queue");
        fixture.Receive(fixture.Press("other-click", 11, viewerId: viewer.ViewerId)); fixture.Tick(2);
        Check(!fixture.PressAck.Accepted && fixture.Events().Length == 0 && fixture.Viewers == 1,
            "viewer Action 4 cannot click another bundle even with matching caller and target");
        fixture.Receive(fixture.Press("first", 12, viewerId: viewer.ViewerId)); fixture.Tick(3);
        Check(!fixture.PressAck.Accepted && fixture.Events().Length == 0 && fixture.ValueLeases == 0,
            "viewer click cannot bypass a draggable control's numeric Begin protocol");
    }
    static void RetiredClickCannotOperateSuccessor()
    {
        using var fixture = new Fixture();
        fixture.Ui("value-notify", new object[] { "on-viewer-click" });
        var viewerA = fixture.Grant();
        fixture.Receive(fixture.Press("click", 10, viewerId: viewerA.ViewerId)); fixture.Tick(1);
        Check(fixture.PressAck.Accepted && fixture.PressAck.ViewerId == viewerA.ViewerId && fixture.LegacySequence == 10
            && fixture.PendingValueWakes == 1 && fixture.Events().Single().Item1 == "click" && fixture.PendingValueWakes == 0,
            "original viewer establishes real legacy sequence floor and registered click wake path");
        var delayedClick = fixture.Press("click", 11, viewerId: viewerA.ViewerId);
        byte[] delayedBytes = MyAPIGateway.Utilities.SerializeToBinary(delayedClick);
        fixture.Receive(Fixture.Request(viewerA, UiDragKind.Blur, 12)); fixture.Tick(2);
        Check(fixture.Viewers == 0 && fixture.LegacySequence == 10,
            "reserved Blur closes original viewer without raising legacy click sequence floor");
        var viewerB = fixture.Grant(tick: 60, requestId: 13, sequence: 13);
        Check(viewerB.ViewerId != viewerA.ViewerId && viewerB.ControlId == viewerA.ControlId
            && viewerB.DefinitionRevision == viewerA.DefinitionRevision && fixture.LegacySequence == 10
            && delayedClick.Sequence > fixture.LegacySequence && fixture.CurrentViewerId == viewerB.ViewerId,
            "successor Focus retains bundle and declaration while old serialized click still beats the independent legacy floor");
        fixture.ReceiveBytes(delayedBytes); fixture.Tick(61);
        var rejected = fixture.PressAck;
        Check(!rejected.Accepted && rejected.ViewerId == viewerA.ViewerId && rejected.Sequence == delayedClick.Sequence
            && fixture.LegacySequence == delayedClick.Sequence && fixture.CurrentViewerId == viewerB.ViewerId && fixture.Viewers == 1
            && fixture.PendingValueWakes == 0 && fixture.Events().Length == 0 && fixture.Runs == 0 && fixture.ValueLeases == 0,
            "actual delayed original-viewer click is consumed and rejected without event, PB wake, value lease or successor mutation");
        fixture.Receive(fixture.Press("click", 14, viewerId: viewerB.ViewerId)); fixture.Tick(62);
        Check(fixture.PressAck.Accepted && fixture.PressAck.ViewerId == viewerB.ViewerId && fixture.PendingValueWakes == 1
            && fixture.CurrentViewerId == viewerB.ViewerId && fixture.Viewers == 1 && fixture.ValueLeases == 0,
            "fresh successor nonce authorizes a same-bundle click after retired click rejection");
        var events = fixture.Events();
        Check(events.Length == 1 && events[0].Item1 == "click" && events[0].Item2 == "click" && events[0].Item4.Item3 == 77
            && fixture.PendingValueWakes == 0 && fixture.Runs == 0,
            "only the fresh successor click publishes its registered structured event and queued notification");
    }
    static void KeepAliveAndExpiry()
    {
        using (var fixture = new Fixture())
        {
            var viewer = fixture.Grant();
            fixture.Tick(1700); int before = fixture.Sent.Count;
            fixture.Receive(Fixture.Request(viewer, UiDragKind.KeepAlive, 2));
            fixture.Receive(Fixture.Request(viewer, UiDragKind.KeepAlive, 3));
            fixture.Tick(1701);
            Check(fixture.Sent.Count == before + 1 && fixture.Ack.Kind == (int)UiDragKind.KeepAlive
                && fixture.Ack.Sequence == 3 && fixture.Ack.Status == 0 && fixture.Ack.ViewerId == viewer.ViewerId,
                "same-viewer heartbeat packets coalesce into one validated newest-sequence ACK");
            fixture.Tick(2000);
            Check(fixture.Viewers == 1 && fixture.ValueLeases == 0 && fixture.ValueLocks == 0,
                "validated heartbeat refreshes persistent viewer past initial idle deadline without taking a numeric lock");
            fixture.Tick(3502);
            Check(fixture.Viewers == 0 && fixture.Ack.Kind == (int)UiDragKind.Focus && fixture.Ack.Terminal
                && fixture.Ack.Status == (int)UiDragStatus.ContextLost && fixture.ValueLeases == 0,
                "unrefreshed viewer expires after its refreshed idle deadline");
        }
        using (var fixture = new Fixture())
        {
            var viewer = fixture.Grant(); var valueLease = fixture.Start(viewer);
            var forged = Fixture.Request(viewer, UiDragKind.KeepAlive, 3); forged.ControlId = "click";
            fixture.Receive(forged); fixture.Tick(2);
            Check(fixture.Viewers == 1 && fixture.ValueLeases == 1 && fixture.ValueLocks == 1 && fixture.Transactions.ActiveLeases == 1
                && fixture.Ack.Kind == (int)UiDragKind.KeepAlive && fixture.Ack.Terminal && fixture.Ack.Status == (int)UiDragStatus.Invalid
                && fixture.Ack.ViewerId == 0 && fixture.Ack.LeaseId == 0,
                "heartbeat identity mismatch rejects its own unbound scope without retiring authorized viewer or active value");
            fixture.Receive(Fixture.Request(valueLease, UiDragKind.Cancel, 4)); fixture.Tick(3); fixture.Tick(1801);
            Check(fixture.Viewers == 0 && fixture.Ack.Terminal && fixture.Ack.Kind == (int)UiDragKind.Focus,
                "rejected heartbeat does not refresh the initial viewer idle deadline");
        }
        using (var fixture = new Fixture())
        {
            var viewer = fixture.Grant(); fixture.Tick(1801);
            Check(fixture.Viewers == 0 && fixture.Ack.Terminal && fixture.Ack.ViewerId == viewer.ViewerId,
                "value-free idle viewer expires through the native simulation tick");
        }
        using (var fixture = new Fixture())
        {
            var viewer = fixture.Grant(); var gesture = fixture.Start(viewer);
            fixture.Receive(Fixture.Request(viewer, UiDragKind.KeepAlive, 3)); fixture.Tick(180);
            fixture.Tick(182);
            Check(fixture.Viewers == 1 && fixture.Transactions.ActiveLeases == 0 && fixture.ValueLeases == 0
                && fixture.ValueLocks == 0 && fixture.Value("gain").Value == 2
                && fixture.DragAcks.Any(ack => ack.RequestId == gesture.RequestId && ack.Terminal && ack.Status == (int)UiDragStatus.TimedOut),
                "viewer KeepAlive cannot keep an idle numeric gesture locked beyond its independent deadline");
            fixture.Start(viewer, "second", 3, 4, 183);
        }
        using (var fixture = new Fixture())
        {
            var viewer = fixture.Grant(); fixture.Receive(Fixture.Request(viewer, UiDragKind.KeepAlive, 2)); fixture.Tick(1800);
            Check(fixture.Viewers == 1 && fixture.Ack.Kind == (int)UiDragKind.KeepAlive && fixture.Ack.Status == 0,
                "heartbeat exactly at the viewer idle boundary refreshes still-current authority");
            int before = fixture.Sent.Count;
            fixture.Receive(Fixture.Request(viewer, UiDragKind.KeepAlive, 2)); fixture.Tick(1801);
            Check(fixture.Sent.Count == before && fixture.Viewers == 1,
                "replayed heartbeat silently drops without revoking current viewer or emitting duplicate ACK");
        }
        using (var fixture = new Fixture())
        {
            var viewer = fixture.Grant(); fixture.Receive(Fixture.Request(viewer, UiDragKind.KeepAlive, 2)); fixture.Tick(1801);
            Check(fixture.Viewers == 0 && fixture.Ack.Terminal && fixture.Ack.Status == (int)UiDragStatus.ContextLost
                && fixture.Ack.ViewerId == viewer.ViewerId
                && !fixture.DragAcks.Any(ack => ack.Kind == (int)UiDragKind.KeepAlive && ack.Status == 0),
                "heartbeat queued beyond the idle boundary cannot resurrect expired viewer authority");
        }
    }
    static void CurrentContextInvalidation()
    {
        (string Name, Action<Fixture> Invalidate)[] invalidations = {
            ("permission", fixture => fixture.Access = false),
            ("construct ownership", fixture => fixture.SameConstruct = false),
            ("power", fixture => fixture.Working = false),
            ("dead character", fixture => fixture.Dead = true),
            ("controlled entity", fixture => fixture.OnFoot = false),
            ("disconnect", fixture => fixture.Connected = false),
            ("registered character", fixture => fixture.Registry.Remove(30)),
            ("target instance replacement", fixture => fixture.Registry[20] = DrawTestProxy.Make(typeof(IMyProjector), ((DrawTestProxy)fixture.Registry[20]).Handler)),
            ("distance", fixture => fixture.Position = new Vector3D(6, 0, 0)),
            ("looking away", fixture => fixture.Direction = Vector3D.Backward),
            ("occlusion", fixture => fixture.Occluded = true),
            ("source view", fixture => Set(fixture.Scene, "Scale", 2d)),
            ("script replacement", fixture => fixture.Program = "replacement-owner"),
            ("initial source artwork", fixture => fixture.Draw("transform", new object[] { "entry-art", MatrixD.CreateTranslation(.1, 0, 0) })),
            ("other bundle-owned source artwork", fixture => fixture.Draw("transform", new object[] { "second-art", MatrixD.CreateTranslation(1.1, 0, 0) })),
            ("definition", fixture => fixture.Ui("visible", new object[] { "click", false }))
        };
        foreach (var invalidate in invalidations)
        {
            using var fixture = new Fixture(); var viewer = fixture.Grant(); var gesture = fixture.Start(viewer);
            var value = fixture.Value("gain"); long revision = value.Revision;
            fixture.Receive(Fixture.Request(gesture, UiDragKind.Move, 3, 9));
            invalidate.Invalidate(fixture); fixture.Tick(7);
            Check(fixture.Viewers == 0 && fixture.ValueLeases == 0 && fixture.ValueLocks == 0
                && fixture.Transactions.ActiveLeases == 0 && fixture.Transactions.PendingMoves == 0,
                invalidate.Name + " context loss revokes viewer, queued pointer work and canonical value authority");
            Check(value.Value == 2 && value.Revision == revision && fixture.Runs == 0
                && fixture.DragAcks.Any(ack => ack.Kind == (int)UiDragKind.Focus && ack.Terminal
                    && ack.ViewerId == viewer.ViewerId && ack.Status == (int)UiDragStatus.ContextLost),
                invalidate.Name + " emits terminal viewer revocation before invalid input can commit");
        }
    }
    static void CloseReleasesViewerAndValue()
    {
        using var fixture = new Fixture(); var viewer = fixture.Grant(); var gesture = fixture.Start(viewer);
        fixture.Receive(Fixture.Request(gesture, UiDragKind.Move, 3, 9));
        fixture.Receive(Fixture.Request(gesture, UiDragKind.Cancel, 4));
        // Spend the ordinary legacy allowance before the reserved Blur packet.
        // Viewer cleanup must not depend on an additional legacy UiPress slot.
        for (long sequence = 10; sequence < 15; sequence++) fixture.Receive(fixture.Press("other-click", sequence, viewerId: viewer.ViewerId));
        fixture.Receive(Fixture.Request(viewer, UiDragKind.Blur, 20));
        fixture.Tick(2); fixture.Tick(3);
        Check(fixture.Viewers == 0 && fixture.ValueLeases == 0 && fixture.ValueLocks == 0
            && fixture.Transactions.ActiveLeases == 0 && fixture.Transactions.PendingMoves == 0 && fixture.Value("gain").Value == 2,
            "reserved value Cancel plus viewer Blur clears numeric lock and focus even when ordinary press allowance is exhausted");
        fixture.Receive(fixture.Begin(viewer, "second", 3, 21)); fixture.Tick(4);
        Check(fixture.Ack.Kind == (int)UiDragKind.Begin && fixture.Ack.Terminal && fixture.Ack.Status == (int)UiDragStatus.Stale
            && fixture.Ack.RequestId == 3 && fixture.Ack.ViewerId == viewer.ViewerId && fixture.Viewers == 0 && fixture.ValueLeases == 0,
            "closed viewer cannot authorize a later gesture");
        using var closeOnly = new Fixture(); var closeViewer = closeOnly.Grant(); var active = closeOnly.Start(closeViewer);
        closeOnly.Receive(Fixture.Request(active, UiDragKind.Move, 3, 9));
        closeOnly.Receive(Fixture.Request(closeViewer, UiDragKind.Blur, 4)); closeOnly.Tick(2);
        Check(closeOnly.Viewers == 0 && closeOnly.Transactions.ActiveLeases == 0 && closeOnly.ValueLeases == 0
            && closeOnly.ValueLocks == 0 && closeOnly.Transactions.PendingMoves == 0 && closeOnly.Value("gain").Value == 2,
            "viewer Blur alone cancels an active numeric lease and its queued pointer work before commit");
    }
    static void PendingFocusCloseCannotGrant()
    {
        using (var fixture = new Fixture())
        {
            var focus = fixture.Focus(); fixture.Receive(focus);
            var blur = fixture.Focus(sequence: 2); blur.Kind = (int)UiDragKind.Blur;
            fixture.Receive(blur); fixture.Tick(0);
            Check(fixture.Viewers == 0 && fixture.ValueLeases == 0 && fixture.ValueLocks == 0 && fixture.Transactions.ActiveLeases == 0
                && fixture.DragAcks.Any(ack => ack.RequestId == focus.RequestId && ack.Terminal && ack.Status != 0)
                && !fixture.DragAcks.Any(ack => ack.Kind == (int)UiDragKind.Focus && ack.Status == 0 && !ack.Terminal),
                "exact pending-Focus Blur prevents a late viewer grant without needing an acknowledged viewer identity");
        }
        using (var fixture = new Fixture())
        {
            var focus = fixture.Focus(); var inFlightAck = fixture.Grant();
            var blur = fixture.Focus(sequence: 2); blur.Kind = (int)UiDragKind.Blur;
            fixture.Receive(blur); fixture.Tick(1);
            Check(fixture.Viewers == 0 && fixture.ValueLeases == 0 && fixture.Transactions.ActiveLeases == 0,
                "unacknowledged Focus Blur matches original request identity after the server has already assigned viewer authority");
            fixture.Receive(fixture.Begin(inFlightAck, "first", 2, 3)); fixture.Tick(2);
            Check(fixture.Ack.Kind == (int)UiDragKind.Begin && fixture.Ack.Terminal && fixture.Ack.Status == (int)UiDragStatus.Stale
                && fixture.Ack.RequestId == 2 && fixture.Ack.ViewerId == inFlightAck.ViewerId && fixture.Viewers == 0 && fixture.ValueLeases == 0,
                "delayed accepted Focus ACK cannot restore viewer authority after matching zero-ID Blur");
        }
        using (var fixture = new Fixture())
        {
            var focus = fixture.Focus(); fixture.Receive(focus);
            var wrongClose = fixture.Focus(sequence: 2); wrongClose.Kind = (int)UiDragKind.Blur; wrongClose.ControlId = "click";
            fixture.Receive(wrongClose); fixture.Tick(0);
            Check(fixture.Viewers == 1 && fixture.ValueLeases == 0
                && fixture.DragAcks.Any(ack => ack.Kind == (int)UiDragKind.Focus && ack.RequestId == focus.RequestId && ack.Status == 0 && !ack.Terminal),
                "pending zero-ID Blur with a different control cannot cancel the legitimate queued Focus");
        }
    }
    static void RetiredIdentitiesCannotOperateSuccessor()
    {
        using var fixture = new Fixture(); var oldViewer = fixture.Grant(); var first = fixture.Start(oldViewer);
        fixture.Receive(Fixture.Request(first, UiDragKind.End, 3, 4)); fixture.Tick(2);
        var successor = fixture.Start(oldViewer, "second", 3, 4, 3);
        int before = fixture.Sent.Count;
        fixture.Receive(Fixture.Request(first, UiDragKind.Move, 5, 9));
        fixture.Receive(Fixture.Request(first, UiDragKind.Cancel, 6)); fixture.Tick(10);
        Check(fixture.Sent.Count == before && fixture.Value("trim").Value == 2 && fixture.ValueLeases == 1
            && fixture.Transactions.ActiveLeases == 1 && fixture.Viewers == 1,
            "retired gesture Move and Cancel cannot mutate or terminate its successor value lease");
        fixture.Receive(Fixture.Request(successor, UiDragKind.End, 7, 3)); fixture.Tick(11);
        fixture.Receive(Fixture.Request(oldViewer, UiDragKind.Blur, 20)); fixture.Tick(12);
        Check(fixture.Viewers == 0, "old viewer closes before replacement Focus");
        var newViewer = fixture.Grant(tick: 60, requestId: 4, sequence: 21);
        Check(newViewer.ViewerId != oldViewer.ViewerId, "replacement Focus uses a fresh persistent viewer identity");
        var staleBegin = fixture.Begin(oldViewer, "first", 5, 22);
        fixture.Receive(staleBegin); fixture.Tick(61);
        Check(fixture.Ack.Kind == (int)UiDragKind.Begin && fixture.Ack.Terminal && fixture.Ack.Status == (int)UiDragStatus.Stale
            && fixture.Ack.RequestId == staleBegin.RequestId && fixture.Ack.ViewerId == oldViewer.ViewerId && fixture.Viewers == 1 && fixture.ValueLeases == 0,
            "stale viewer Begin cannot borrow or revoke replacement viewer");
        int afterRejectedBegin = fixture.Sent.Count;
        fixture.Receive(Fixture.Request(oldViewer, UiDragKind.KeepAlive, 23)); fixture.Tick(62);
        Check(fixture.Sent.Count == afterRejectedBegin && fixture.Viewers == 1 && fixture.ValueLeases == 0,
            "retired viewer heartbeat cannot refresh or remove the current viewer");
        fixture.Receive(Fixture.Request(oldViewer, UiDragKind.Blur, 24)); fixture.Tick(63);
        Check(fixture.Viewers == 1 && fixture.ValueLeases == 0,
            "retired viewer Blur cannot close a successor Focus even with a fresh global packet sequence");
        var stalePendingClose = fixture.Focus(requestId: oldViewer.RequestId, sequence: 25);
        stalePendingClose.Kind = (int)UiDragKind.Blur;
        fixture.Receive(stalePendingClose); fixture.Tick(64);
        Check(fixture.Viewers == 1 && fixture.ValueLeases == 0,
            "zero-ID Blur carrying retired Focus identity cannot close a successor viewer");
        fixture.Start(newViewer, "first", 6, 26, 65);
        Check(fixture.Viewers == 1 && fixture.ValueLeases == 1, "replacement viewer still authorizes fresh gestures after stale identities are rejected");
        fixture.Receive(Fixture.Request(newViewer, UiDragKind.KeepAlive, 27)); fixture.Tick(66);
        Check(fixture.Ack.Kind == (int)UiDragKind.KeepAlive && fixture.Ack.Status == 0 && !fixture.Ack.Terminal
            && fixture.Ack.ViewerId == newViewer.ViewerId && fixture.Viewers == 1 && fixture.ValueLeases == 1,
            "rejected retired close packets preserve current viewer's core heartbeat ownership");
    }
    static void ClientMouseUpRetainsPointerEscapeReleases()
    {
        using var fixture = new Fixture(); fixture.EnableClient();
        var provider = new NativePointerProvider(); provider.Install(fixture);
        Check(fixture.UseEntry(), "native USE consumption revalidates the entry hotzone and opens non-draggable artwork viewer");
        var bridge = (PointerInputBridge)Field(fixture.Session, "_uiPointer");
        var local = (UiDragLocalLease)Field(fixture.Session, "_uiDragLocal");
        Check(bridge.Token == 700 && provider.Acquires == 1 && provider.Releases == 0 && !local.Pending
            && fixture.ClientPackets.Count == 1 && MyAPIGateway.Utilities.SerializeFromBinary<UiDragRequest>(UiDragWire.Body(fixture.ClientPackets[0])).Kind == (int)UiDragKind.Focus,
            "USE acquires one native provider token and sends Focus without starting a numeric gesture");
        fixture.RoundTrip(0);
        Check((bool)Field(fixture.Session, "_uiViewerGranted") && fixture.Viewers == 1 && fixture.ValueLeases == 0,
            "secure serialized server Focus ACK opens client viewer without a numeric lease");
        provider.Point(2.5); provider.Frame = 2; provider.Pressed = provider.Held = 1;
        fixture.ClientTick(1); fixture.RoundTrip(1);
        Check(local.Active && local.Held && fixture.ValueLeases == 1 && bridge.Token == 700,
            "routed native pointer-down over the off-head-ray first control begins an acknowledged numeric gesture");
        provider.Point(2.7); provider.Frame = 3; provider.Pressed = 0;
        fixture.ClientTick(7); fixture.RoundTrip(7);
        Check(fixture.Value("gain").Value == 4 && fixture.Viewers == 1 && provider.Releases == 0,
            "client pointer prediction passes actual protobuf receiver and host commit at configured cadence");
        provider.Point(2.8); provider.Frame = 4; provider.Held = 0; provider.Released = 1;
        fixture.ClientTick(8);
        Check(bridge.Token == 700 && provider.Releases == 0 && Field(fixture.Session, "_uiViewerRequest") != null,
            "mouse-up emits final End while retaining persistent native cursor ownership before its ACK");
        fixture.RoundTrip(8);
        Check(!local.Pending && bridge.Token == 700 && provider.Releases == 0 && fixture.Viewers == 1
            && fixture.ValueLeases == 0 && fixture.Value("gain").Value == 5,
            "terminal numeric ACK clears gesture state while retaining viewer token and final release-frame value");
        provider.Point(1.5); provider.Frame = 5; provider.Pressed = provider.Held = 1; provider.Released = 0;
        fixture.ClientTick(9); fixture.RoundTrip(9);
        Check(local.Active && fixture.ValueLeases == 1 && provider.Acquires == 1 && provider.Releases == 0
            && fixture.ClientPackets.Count(bytes => UiDragWire.IsDrag(bytes)
                && MyAPIGateway.Utilities.SerializeFromBinary<UiDragRequest>(UiDragWire.Body(bytes)).Kind == (int)UiDragKind.Focus) == 1,
            "second numeric control works under the original viewer and provider token without another USE"
                + " (active=" + local.Active + ", pending=" + local.Pending + ", held=" + local.Held
                + ", valueLeases=" + fixture.ValueLeases + ", viewer=" + fixture.Viewers + ", token=" + bridge.Token
                + ", acquires=" + provider.Acquires + ", releases=" + provider.Releases + ", packets=" + fixture.ClientPackets.Count
                + ", granted=" + Field(fixture.Session, "_uiViewerGranted") + ", grab=" + Field(fixture.Session, "_uiDragGrabbed")
                + ", lastAck=" + fixture.Ack.Kind + "/" + fixture.Ack.Status + ", dragControl=" + Field(fixture.Session, "_uiDragControlId") + ")");
        provider.Point(1.6); provider.Frame = 6; provider.Pressed = 0;
        fixture.ClientTick(15); fixture.RoundTrip(15);
        Check(fixture.Value("trim").Value == 3 && fixture.Value("gain").Value == 5 && bridge.Token == 700 && provider.Releases == 0,
            "second control computes its drag from its own current value instead of the previous gesture's confirmed value");
        fixture.Escape = true; provider.Frame = 7;
        fixture.ClientTick(16);
        Check(bridge.Token == 0 && provider.Releases == 1 && Field(fixture.Session, "_uiViewerRequest") == null
            && fixture.ClientPackets.Any(bytes => UiDragWire.IsDrag(bytes)
                && MyAPIGateway.Utilities.SerializeFromBinary<UiDragRequest>(UiDragWire.Body(bytes)).Kind == (int)UiDragKind.Blur),
            "Escape releases native provider exactly once and sends reserved viewer Blur");
        fixture.RoundTrip(16);
        Check(fixture.Viewers == 0 && fixture.ValueLeases == 0 && fixture.ValueLocks == 0 && fixture.Transactions.ActiveLeases == 0
            && fixture.Transactions.PendingMoves == 0 && provider.Releases == 1 && fixture.Value("trim").Value == 3,
            "Escape's serialized close removes host viewer and active numeric authority without reacquiring native pointer");
    }
    static void ClientContextLossReleasesPointer()
    {
        using var fixture = new Fixture(); fixture.EnableClient();
        var provider = new NativePointerProvider(); provider.Install(fixture);
        Check(fixture.UseEntry(), "client context fixture enters through physically revalidated native USE consumption");
        fixture.RoundTrip(0);
        fixture.Access = false; fixture.ClientTick(1);
        var bridge = (PointerInputBridge)Field(fixture.Session, "_uiPointer");
        Check(bridge.Token == 0 && provider.Releases == 1 && Field(fixture.Session, "_uiViewerRequest") == null,
            "client permission context loss releases persistent cursor even with no active value gesture");
        fixture.RoundTrip(1);
        Check(fixture.Viewers == 0 && fixture.ValueLeases == 0 && fixture.Transactions.ActiveLeases == 0 && provider.Releases == 1,
            "client context-loss Blur and host authority validation converge on complete viewer cleanup");
    }
}
