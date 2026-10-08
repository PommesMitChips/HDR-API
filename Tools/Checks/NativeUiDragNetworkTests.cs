using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRageMath;

// Actual session receiver and host authority integration. Gateway replacements
// provide registered game entities/native rays and capture secure transport only.
// Control registration, hit testing, lease validation, value CAS, canonical poses,
// protobuf serialization, network queuing and TickUiNetwork are production paths.
internal static class NativeUiDragNetworkTests
{
    static int checks;
    static void Check(bool good, string label)
    {
        if (!good) throw new Exception("Native staged UI drag network: " + label);
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
    static bool Close(MatrixD a, MatrixD b)
    {
        var left = new[] { a.M11,a.M12,a.M13,a.M14,a.M21,a.M22,a.M23,a.M24,a.M31,a.M32,a.M33,a.M34,a.M41,a.M42,a.M43,a.M44 };
        var right = new[] { b.M11,b.M12,b.M13,b.M14,b.M21,b.M22,b.M23,b.M24,b.M31,b.M32,b.M33,b.M34,b.M41,b.M42,b.M43,b.M44 };
        return left.Zip(right).All(pair => Math.Abs(pair.First - pair.Second) < 1e-10);
    }
    sealed class Fixture : IDisposable
    {
        public readonly ClientReplicationTests.GatewayScope Gateway = new() { Server = true, Dedicated = true };
        public readonly HoloMapSession Session = new();
        public readonly Dictionary<long, object> Registry = new();
        public readonly List<(ushort Channel, byte[] Bytes, ulong Recipient, bool Reliable)> Sent = new();
        public readonly List<(ushort Channel, byte[] Bytes, bool Reliable)> ClientSent = new();
        public readonly Func<string, object[], object> Ui, Draw;
        public bool Access = true, SameConstruct = true, Working = true, Dead, OnFoot = true, Connected = true, Occluded, IsServer = true;
        public Vector3D Position = new(.2, 0, 1), Head = new(.2, 0, 1), Direction = Vector3D.Forward;
        public string Program = "native-drag-owner";
        public int Runs, RayCasts;
        public Fixture()
        {
            Registry[10] = DrawTestProxy.Make(typeof(IMyProgrammableBlock), (method, args) => method.Name switch
            {
                "get_EntityId" => 10L, "get_OwnerId" => 77L, "get_Closed" => false,
                "get_IsWorking" => Working, "get_ProgramData" => Program,
                "IsSameConstructAs" => SameConstruct, "HasPlayerAccess" => Access,
                "TryRun" => Run(), _ => throw new Exception("Native drag PB: " + method.Name)
            });
            // The shared helper handles ProgramData before dispatching Handler.
            // Supply its source hook so script-lifetime tests observe mutations.
            ((DrawTestProxy)Registry[10]).ProgramSource = () => Program;
            Registry[20] = DrawTestProxy.Make(typeof(IMyProjector), (method, args) => method.Name switch
            {
                "get_EntityId" => 20L, "get_Closed" => false, "get_IsWorking" => Working,
                "HasPlayerAccess" => Access, "get_WorldMatrix" => MatrixD.Identity,
                "GetPosition" => Vector3D.Zero, "get_BlockDefinition" => Activator.CreateInstance(method.ReturnType),
                _ => throw new Exception("Native drag target: " + method.Name)
            });
            object character = DrawTestProxy.Make(typeof(NativeUiDragCharacter), (method, args) => method.Name switch
            {
                "get_EntityId" => 30L, "get_Closed" => false, "get_IsDead" => Dead,
                "GetPosition" => Position, "GetHeadMatrix" => MatrixD.CreateWorld(Head, Direction, Vector3D.Up),
                _ => throw new Exception("Native drag character: " + method.Name)
            });
            object controller = DrawTestProxy.Make(typeof(IMyEntityController), (method, args) => method.Name switch
            {
                "get_ControlledEntity" => OnFoot ? character : null,
                _ => throw new Exception("Native drag controller: " + method.Name)
            });
            object player = DrawTestProxy.Make(typeof(IMyPlayer), (method, args) => method.Name switch
            {
                "get_SteamUserId" => 200UL, "get_IdentityId" => 77L, "get_Character" => character,
                "get_Controller" => controller, _ => throw new Exception("Native drag player: " + method.Name)
            });
            Registry[30] = character;
            Gateway.Install("Session", (method, args) => method.Name == "get_LocalHumanPlayer" ? null : throw new Exception("Native drag session: " + method.Name));
            Gateway.Install("Multiplayer", (method, args) =>
            {
                if (method.Name == "get_IsServer") return IsServer;
                if (method.Name == "get_ServerId") return 100UL;
                if (method.Name == "SendMessageTo")
                {
                    Sent.Add(((ushort)args[0], (byte[])args[1], (ulong)args[2], (bool)args[3]));
                    return true;
                }
                if (method.Name == "SendMessageToServer")
                {
                    ClientSent.Add(((ushort)args[0], (byte[])args[1], (bool)args[2]));
                    return true;
                }
                throw new Exception("Native drag multiplayer: " + method.Name);
            });
            Gateway.Install("Entities", (method, args) => method.Name == "GetEntityById" ? Registry.GetValueOrDefault((long)args[0]) : throw new Exception("Native drag entities: " + method.Name));
            Gateway.Install("Players", (method, args) =>
            {
                if (method.Name != "GetPlayers") throw new Exception("Native drag players: " + method.Name);
                var predicate = args.Length > 1 ? (Func<IMyPlayer, bool>)args[1] : null;
                if (Connected && (predicate == null || predicate((IMyPlayer)player))) ((List<IMyPlayer>)args[0]).Add((IMyPlayer)player);
                return null;
            });
            Gateway.Install("Physics", (method, args) =>
            {
                if (method.Name != "CastRay") throw new Exception("Native drag physics: " + method.Name);
                RayCasts++;
                if (Occluded)
                {
                    object obstruction = DrawTestProxy.Make(typeof(IHitInfo), (m, a) => m.Name switch
                    {
                        "get_HitEntity" => Registry[20], "get_Position" => Head + Direction * .5,
                        _ => throw new Exception("Native drag obstruction: " + m.Name)
                    });
                    ((List<IHitInfo>)args[2]).Add((IHitInfo)obstruction);
                }
                return null;
            });
            Draw = (Func<string, object[], object>)Call(Session, "DrawEndpoint", Registry[10]);
            Draw("target", new[] { Registry[20] });
            Draw("line", new object[] { "handle", Vector3D.Zero, Vector3D.UnitX, "cyan" });
            Ui = (Func<string, object[], object>)Call(Session, "UiEndpoint", Registry[10]);
            Ui("target", new[] { Registry[20] });
            Ui("bundle", new object[] { "main" });
            Ui("value", new object[] { "gain", 2d, 0d, 10d, 1d });
            Ui("control", new object[] { "slider", "main", "handle", .5, 0d, 1d, .25 });
            Ui("bind-value", new object[] { "slider", "gain" });
            Ui("constraint", new object[] { "slider", "line", Vector3D.Zero, Vector3D.UnitX });
            Ui("draggable", new object[] { "slider", true });
            Set(Scene, "Offset", Vector3D.Zero);
            Set(Scene, "Scale", 1d);
            Set(Scene, "Rotation", Vector3D.Zero);
            Set(Scene, "VolumeRange", 0d);
        }
        bool Run() { Runs++; return true; }
        public UiDisplay Display => (UiDisplay)Call(Session, "GetUiDisplay", 10L, 20L);
        public object Scene => ((IDictionary)Field(Session, "_scenes"))[20L];
        public object Artwork => ((IDictionary)Field(Scene, "Items"))["10:handle"];
        public MatrixD Pose => (MatrixD)Field(Artwork, "Transform");
        public MyTuple<double, long> Value => (MyTuple<double, long>)Ui("get-value", new object[] { "gain" });
        public UiDragTransactions Transactions => (UiDragTransactions)Field(Session, "_uiDrags");
        public UiDragRequest Begin(long requestId = 1, long sequence = 1) => new()
        {
            Kind = 1, CallerId = 10, TargetId = 20, ControlId = "slider", DefinitionRevision = Display.Revision,
            ValueRevision = Value.Item2, RequestId = requestId, Sequence = sequence, Mode = 1, Value = Value.Item1
        };
        public static UiDragRequest Request(UiDragAck grant, UiDragKind kind, long sequence, double value) => new()
        {
            Kind = (int)kind, CallerId = grant.CallerId, TargetId = grant.TargetId, ControlId = grant.ControlId,
            DefinitionRevision = grant.DefinitionRevision, ValueRevision = grant.ValueRevision, RequestId = grant.RequestId,
            Sequence = sequence, LeaseId = grant.LeaseId, Mode = 1, Value = value
        };
        public byte[] Bytes(UiDragRequest request) => UiDragWire.Wrap(MyAPIGateway.Utilities.SerializeToBinary(request));
        public void Receive(UiDragRequest request, ulong sender = 200, bool fromServer = false, ushort channel = 49784)
            => ReceiveBytes(Bytes(request), sender, fromServer, channel);
        public void ReceiveBytes(byte[] bytes, ulong sender = 200, bool fromServer = false, ushort channel = 49784)
            => Call(Session, "ReceiveUiMessage", channel, bytes, sender, fromServer);
        public void Tick(int tick)
        {
            Set(Session, "_ticks", tick);
            Call(Session, "TickUiNetwork");
        }
        public UiDragAck Ack => MyAPIGateway.Utilities.SerializeFromBinary<UiDragAck>(UiDragWire.Body(Sent.Last().Bytes));
        public MyTuple<string, string, string, MyTuple<double, long, long>>[] Events()
            => (MyTuple<string, string, string, MyTuple<double, long, long>>[])Ui("poll-value-events", Array.Empty<object>());
        public UiDragAck Grant()
        {
            Receive(Begin());
            Check(Sent.Count == 0 && Value.Item1 == 2, "actual secure receiver only queues Begin before simulation tick");
            Tick(0);
            Check(Sent.Count == 1, "actual host Begin produces secure serialized ACK");
            var ack = Ack;
            Check(UiDragWire.Valid(ack) && ack.Status == 0 && !ack.Terminal && ack.LeaseId > 0,
                "current registered character head ray grants valid authoritative lease");
            return ack;
        }
        public void Dispose() { Call(Session, "ClearUiDragNetwork"); Gateway.Dispose(); }
    }
    // Client allocator, send path and authenticated ACK queue are the actual
    // HoloMapSession methods. Native held samples drive the pure local lease;
    // the separate input fixture tests native-provider/client eligibility itself.
    sealed class ClientLoop
    {
        readonly Fixture fixture;
        public readonly HoloMapSession Session = new();
        public readonly UiDragLocalLease Local;
        public readonly List<UiDragRequest> Requests = new();
        public int Tick, Releases;
        public long Sequence => (long)Field(Session, "_uiSequence");
        public ClientLoop(Fixture fixture)
        {
            this.fixture = fixture;
            Set(Session, "_uiSequence", 0L); // deterministic seed; allocator itself is production.
            Local = new UiDragLocalLease(Send, () => Releases++, Next);
            Set(Session, "_uiDragLocal", Local);
            Set(Session, "_uiDragDisplay", fixture.Display);
            Set(Session, "_uiDragCharacter", 30L);
            Set(Session, "_uiDragTile", 20L);
            Set(Session, "_uiDragPhysicalGrantTile", 20L);
        }
        public long Next() => (long)Call(Session, "NextUiClientSequence");
        public bool Begin(int tick)
        {
            Tick = tick; Set(Session, "_ticks", tick);
            long sequence = Next();
            return Local.Begin(fixture.Begin(sequence, sequence), tick);
        }
        public void Send(UiDragRequest request)
        {
            Requests.Add(UiDragWire.Copy(request));
            int previous = fixture.ClientSent.Count;
            fixture.IsServer = false;
            try { Call(Session, "SendUiDragRequest", request); }
            finally { fixture.IsServer = true; }
            Check(fixture.ClientSent.Count == previous + 1 && fixture.ClientSent.Last().Reliable,
                "actual client drag sender emits one reliable serialized packet");
            fixture.ReceiveBytes(fixture.ClientSent.Last().Bytes);
        }
        public void Deliver(UiDragAck ack, int tick)
        {
            Tick = tick; Set(Session, "_ticks", tick);
            byte[] bytes = UiDragWire.Wrap(MyAPIGateway.Utilities.SerializeToBinary(ack));
            fixture.IsServer = false;
            try
            {
                Call(Session, "ReceiveUiMessage", (ushort)49784, bytes, 100UL, true);
                Call(Session, "TickUiDragNetwork");
            }
            finally { fixture.IsServer = true; }
        }
        public void Arm(long frame, int tick)
        {
            Check(Local.Pointer(new UiPointerSample { Frame = frame, Flags = 18, Pressed = 1, Held = 1, X = .5, Y = .5 }, tick),
                "accepted client lease activates from an applied physical pointer down");
        }
        public void Move(double value, int tick)
        {
            Tick = tick; Set(Session, "_ticks", tick);
            Check(Local.Predict(value, tick), "active local gesture accepts canonical-value prediction");
            fixture.Tick(tick); Deliver(fixture.Ack, tick);
            Check(Local.Confirmed == fixture.Value.Item1 && Local.ValueRevision == fixture.Value.Item2,
                "actual authenticated ACK queue reconciles canonical value and revision");
        }
        public void End(double value, int tick)
        {
            Tick = tick; Set(Session, "_ticks", tick);
            Check(Local.Predict(value, tick), "local End retains most recent absolute prediction");
            Local.End(tick); fixture.Tick(tick); Deliver(fixture.Ack, tick);
            Check(!Local.Pending && fixture.Transactions.ActiveLeases == 0 && fixture.Ack.Terminal,
                "client End and actual server terminal ACK close both gesture lifetimes");
        }
        public UiPress LegacyPress(UiWidget widget, int tick, bool close)
        {
            Tick = tick; Set(Session, "_ticks", tick); Set(Session, "_uiPending", null);
            int previous = fixture.ClientSent.Count;
            fixture.IsServer = false;
            try
            {
                if (close) Call(Session, "SendUiClose");
                else Call(Session, "SendUiWidgetPress", fixture.Display, widget, 3);
            }
            finally { fixture.IsServer = true; }
            Check(fixture.ClientSent.Count == previous + 1, "actual legacy client send emits packet beside drag sequence allocator");
            var sent = fixture.ClientSent.Last();
            var press = MyAPIGateway.Utilities.SerializeFromBinary<UiPress>(sent.Bytes);
            fixture.ReceiveBytes(sent.Bytes); fixture.Tick(tick);
            Check(press.Sequence == Sequence && sent.Channel == 49784 && sent.Reliable,
                "legacy click/close use actual shared client allocator and reliable channel");
            return press;
        }
    }
    public static int Run()
    {
        checks = 0;
        ActualReceiveCommitAndEnd();
        ActualReceiveRejectsBeforeDecode();
        SenderBindingAndReplay();
        ProgrammaticCasPreempts();
        CurrentContextInvalidation();
        ConsecutiveClientGesturesAndReconnect();
        LegacyScopeWireCompatibility();
        return checks;
    }
    static void ActualReceiveCommitAndEnd()
    {
        using var fixture = new Fixture();
        long definition = fixture.Display.Revision;
        object geometry = Field(fixture.Artwork, "Geometry");
        var grant = fixture.Grant();
        Check(grant.TileId == 20 && grant.CharacterId == 30 && grant.CallerId == 10 && grant.TargetId == 20,
            "actual ACK resolves physical tile and controlled character from registered game authority");
        Check(fixture.Sent[0].Channel == 49784 && fixture.Sent[0].Recipient == 200 && fixture.Sent[0].Reliable,
            "actual ACK uses secure UI channel, bound remote sender and reliable send");
        fixture.Receive(Fixture.Request(grant, UiDragKind.Move, 2, 3.1));
        fixture.Receive(Fixture.Request(grant, UiDragKind.Move, 3, 6.2));
        Check(fixture.Transactions.PendingMoves == 1 && fixture.Value.Item1 == 2 && fixture.Sent.Count == 1,
            "actual receiver coalesces pointer Move bytes without mutating PB value inline");
        fixture.Tick(5);
        Check(fixture.Value.Item1 == 2 && fixture.Sent.Count == 1, "actual network tick enforces six-tick move cadence");
        fixture.Tick(6);
        var moved = fixture.Ack;
        Check(moved.Kind == 2 && moved.Sequence == 3 && moved.Status == 0 && !moved.Terminal && moved.Value == 6,
            "actual server commit uses latest coalesced request and canonical PB snapping");
        Check(moved.ValueRevision > grant.ValueRevision && moved.DefinitionRevision == definition && fixture.Display.Revision == definition,
            "value commit advances its own revision without changing UI definition identity");
        Check(fixture.Value.Item1 == 6 && fixture.Value.Item2 == moved.ValueRevision && Close(fixture.Pose, MatrixD.CreateTranslation(.4, 0, 0)),
            "actual commit updates canonical numeric value and immutable-reference artwork pose");
        Check(ReferenceEquals(geometry, Field(fixture.Artwork, "Geometry")), "numeric commit preserves retained geometry object and compile identity");
        fixture.Receive(Fixture.Request(moved, UiDragKind.End, 4, 7.6));
        fixture.Tick(7);
        var ended = fixture.Ack;
        Check(ended.Kind == 3 && ended.Terminal && ended.Status == 0 && ended.Value == 8 && ended.ValueRevision > moved.ValueRevision,
            "actual End commits final snapped value and serializes terminal ACK before next move cadence");
        Check(fixture.Transactions.ActiveLeases == 0 && ((IDictionary)Field(fixture.Session, "_uiValueLeases")).Count == 0,
            "actual End releases both transport and canonical PB leases");
        Check(Close(fixture.Pose, MatrixD.CreateTranslation(.6, 0, 0)) && fixture.Display.Revision == definition,
            "final commit composes from immutable reference and preserves UI definition");
        var events = fixture.Events();
        Check(events.Any(e => e.Item1 == "change" && e.Item4.Item1 == 8 && e.Item4.Item3 == 77)
            && events.Any(e => e.Item1 == "commit" && e.Item4.Item1 == 8 && e.Item4.Item3 == 77),
            "actual PB poll reports final canonical value and authorized player identity");
        Check(fixture.Sent.All(sent => sent.Channel == 49784 && sent.Recipient == 200 && sent.Reliable)
            && fixture.RayCasts > 0 && fixture.Runs == 0, "complete accepted path retains secure routing and performs real authority rays without inline PB execution");
    }
    static void ActualReceiveRejectsBeforeDecode()
    {
        using var fixture = new Fixture();
        byte[] valid = fixture.Bytes(fixture.Begin());
        int decoded = fixture.Gateway.Deserializations;
        fixture.ReceiveBytes(valid, fromServer: true);
        fixture.ReceiveBytes(valid, sender: 0);
        fixture.ReceiveBytes(valid, channel: 49783);
        fixture.ReceiveBytes(null);
        fixture.ReceiveBytes(Array.Empty<byte>());
        var oversized = new byte[UiDragWire.PacketLimit + 1]; Array.Copy(valid, oversized, valid.Length);
        fixture.ReceiveBytes(oversized);
        fixture.ReceiveBytes(new byte[] { 72, 68, 82, 68, 49 });
        Check(fixture.Gateway.Deserializations == decoded && fixture.Transactions == null && fixture.Sent.Count == 0,
            "actual receiver rejects forged-server/zero-sender/wrong-channel/oversized/empty drag envelopes before decode or queue allocation");
        fixture.ReceiveBytes(UiDragWire.Wrap(new byte[] { 255 }));
        Check(fixture.Gateway.Deserializations == decoded + 1 && fixture.Transactions == null && fixture.Value.Item1 == 2,
            "actual malformed protobuf decode is contained without canonical or queue mutation");
        var invalid = fixture.Begin(); invalid.Protocol = 2;
        fixture.Receive(invalid);
        Check(fixture.Transactions == null && fixture.Sent.Count == 0 && fixture.Value.Item1 == 2,
            "actual receiver rejects invalid decoded protocol before transaction initialization");
        fixture.Receive(fixture.Begin()); fixture.Tick(0);
        Check(fixture.Ack.Status == 0 && fixture.Transactions.ActiveLeases == 1,
            "earlier hostile envelopes cannot consume valid drag request sequence or admission allowance");
    }
    static void SenderBindingAndReplay()
    {
        using var fixture = new Fixture(); var grant = fixture.Grant();
        var move = Fixture.Request(grant, UiDragKind.Move, 2, 9);
        fixture.Receive(move, sender: 201);
        Check(fixture.Transactions.PendingMoves == 0, "actual secure sender cannot borrow another player's granted lease");
        var forged = Fixture.Request(grant, UiDragKind.Move, 2, 9); forged.CallerId = 11;
        fixture.Receive(forged);
        Check(fixture.Transactions.PendingMoves == 0, "actual receiver rejects changed caller identity on bound lease");
        fixture.Receive(Fixture.Request(grant, UiDragKind.Move, 3, 4));
        fixture.Receive(Fixture.Request(grant, UiDragKind.Move, 3, 9));
        fixture.Tick(6);
        Check(fixture.Value.Item1 == 4 && fixture.Ack.Sequence == 3 && fixture.Sent.Count == 2,
            "actual packet replay cannot replace accepted canonical move or generate duplicate ACK");
    }
    static void ProgrammaticCasPreempts()
    {
        using var fixture = new Fixture(); var grant = fixture.Grant();
        long definition = fixture.Display.Revision;
        var failed = (MyTuple<bool, double, long>)fixture.Ui("set-value", new object[] { "gain", 8d, grant.ValueRevision - 1 });
        Check(!failed.Item1 && failed.Item2 == 2 && failed.Item3 == grant.ValueRevision,
            "stale programmatic CAS cannot mutate canonical value or its revision");
        fixture.Tick(1);
        Check(fixture.Transactions.ActiveLeases == 1 && fixture.Sent.Count == 1, "failed CAS leaves accepted drag authority valid");
        fixture.Receive(Fixture.Request(grant, UiDragKind.Move, 2, 9));
        var write = (MyTuple<bool, double, long>)fixture.Ui("set-value", new object[] { "gain", 8d, grant.ValueRevision });
        Check(write.Item1 && write.Item2 == 8 && write.Item3 > grant.ValueRevision,
            "successful exact CAS installs newer canonical state during pointer gesture");
        fixture.Tick(6);
        Check(fixture.Transactions.ActiveLeases == 0 && fixture.Transactions.PendingMoves == 0 && fixture.Value.Item1 == 8
            && fixture.Ack.Terminal && fixture.Ack.Status != 0, "source CAS preempts stale transport drag before queued pointer can overwrite programmatic state");
        Check(fixture.Display.Revision == definition && Close(fixture.Pose, MatrixD.CreateTranslation(.6, 0, 0)),
            "CAS preemption preserves definition identity and authoritative final pose");
        Check(((IDictionary)Field(fixture.Session, "_uiValueLeases")).Count == 0 && fixture.Events().Any(e => e.Item1 == "cancel"),
            "CAS preemption releases canonical lease and records cancellation");
    }
    static void CurrentContextInvalidation()
    {
        (string Name, Action<Fixture> Invalidate)[] invalidations = {
            ("access", fixture => fixture.Access = false),
            ("power", fixture => fixture.Working = false),
            ("dead character", fixture => fixture.Dead = true),
            ("controlled entity", fixture => fixture.OnFoot = false),
            ("disconnected", fixture => fixture.Connected = false),
            ("registered character", fixture => fixture.Registry.Remove(30)),
            ("distance", fixture => fixture.Position = new Vector3D(6, 0, 0)),
            ("looking away", fixture => fixture.Direction = Vector3D.Backward),
            ("occlusion", fixture => fixture.Occluded = true),
            ("source view", fixture => Set(fixture.Scene, "Scale", 2d)),
            ("PB program replacement", fixture => fixture.Program = "replaced-owner-program"),
            ("artwork source replacement", fixture => fixture.Draw("transform", new object[] { "handle", MatrixD.CreateTranslation(.2, .1, 0) }))
        };
        foreach (var invalidate in invalidations)
        {
            using var fixture = new Fixture(); var grant = fixture.Grant();
            var canonicalBeforeInvalidation = fixture.Display.Values.Single(value => value.Id == "gain");
            fixture.Receive(Fixture.Request(grant, UiDragKind.Move, 2, 9));
            invalidate.Invalidate(fixture); fixture.Tick(6);
            Check(fixture.Transactions.ActiveLeases == 0 && fixture.Transactions.PendingMoves == 0
                && fixture.Ack.Terminal && fixture.Ack.Status == (int)UiDragStatus.ContextLost,
                "actual " + invalidate.Name + " invalidation revokes queued drag (active=" + fixture.Transactions.ActiveLeases
                    + ", moves=" + fixture.Transactions.PendingMoves + ", status=" + fixture.Ack.Status + ", terminal=" + fixture.Ack.Terminal + ")");
            // Retain the declaration object for inspection when a changed PB
            // program correctly removes its old display lifetime entirely.
            Check(canonicalBeforeInvalidation.Value == 2 && fixture.Runs == 0,
                "invalid authority cannot mutate numeric value or execute PB from queued network input");
        }
    }
    static void ConsecutiveClientGesturesAndReconnect()
    {
        using var fixture = new Fixture();
        fixture.Draw("line", new object[] { "tap-art", Vector3D.Zero, Vector3D.UnitX, "cyan" });
        fixture.Ui("control", new object[] { "tap", "main", "tap-art", .5, 0d, 1d, .25 });
        var tap = fixture.Display.Widgets.Single(widget => widget.Id == "tap");
        var client = new ClientLoop(fixture);
        Check(client.Begin(0), "first actual client gesture allocates its Begin identity");
        fixture.Tick(0); var firstGrant = fixture.Ack; client.Deliver(firstGrant, 0);
        Check(firstGrant.Status == 0 && firstGrant.RequestId == 1 && firstGrant.Sequence == 1,
            "first client Begin succeeds against actual receiver with shared counter");
        client.Arm(1, 0); client.Move(4, 6); client.Move(6, 12); client.End(8, 13);
        long firstHigh = client.Sequence;
        Check(firstHigh == 4 && fixture.Value.Item1 == 8, "all first-gesture Moves and End advance HoloMapSession shared sequence");
        fixture.Head = fixture.Position = new Vector3D(.8, 0, 1);
        var legacyClick = client.LegacyPress(tap, 25, false);
        var clickAck = MyAPIGateway.Utilities.SerializeFromBinary<UiAck>(fixture.Sent.Last().Bytes);
        Check(clickAck.Accepted && clickAck.ActionKind == "control" && legacyClick.Sequence > firstHigh,
            "actual interleaved legacy non-draggable click succeeds with sequence beyond drag terminal");
        var legacyClose = client.LegacyPress(tap, 26, true);
        Check(legacyClose.Action == 1 && legacyClose.Sequence > legacyClick.Sequence,
            "actual legacy close advances same sequence after click");
        Check(client.Begin(27), "second actual client gesture starts after interleaved legacy traffic");
        fixture.Tick(27); var secondGrant = fixture.Ack; client.Deliver(secondGrant, 27);
        Check(secondGrant.Status == 0 && !secondGrant.Terminal && secondGrant.RequestId > legacyClose.Sequence
            && secondGrant.LeaseId != firstGrant.LeaseId, "second consecutive gesture grants new identity above every prior drag/legacy sequence");
        client.Arm(1, 27); client.Move(5, 33); client.Move(4, 39); client.End(3, 40);
        long oldHigh = client.Sequence;
        Check(oldHigh == 10 && client.Requests.Select(request => request.Sequence).SequenceEqual(new long[] { 1, 2, 3, 4, 7, 8, 9, 10 }),
            "two gesture packet families preserve one monotonic allocator across legacy sequence gaps");
        Check(fixture.Value.Item1 == 3 && ((IDictionary)Field(fixture.Session, "_uiValueLeases")).Count == 0,
            "two complete client gestures commit and release canonical PB leases");

        // The client process restarts; the sender and character remain the same.
        // A confirmed disconnected tick retires legacy focus but keeps the bounded
        // transport peer replay floor until its retention window naturally expires.
        fixture.Connected = false; fixture.Tick(60); fixture.Connected = true;
        var restarted = new ClientLoop(fixture); Set(restarted.Session, "_uiSequence", 1L);
        int priorAcks = fixture.Sent.Count;
        Check(restarted.Begin(61), "reinitialized client sends low-counter Begin as pending gesture");
        var staleBegin = restarted.Requests.Last();
        for (int i = 0; i < 32; i++) fixture.Receive(staleBegin);
        Check(fixture.Sent.Count == priorAcks && fixture.Transactions.ActiveLeases == 0,
            "stale reconnect Begin cannot grant inline or flood secure ACK callback");
        fixture.Tick(61);
        var resync = fixture.Ack;
        Check(fixture.Sent.Count == priorAcks + 1 && resync.Terminal && resync.Status == (int)UiDragStatus.Stale
            && resync.MinimumSequence >= oldHigh && resync.Sequence == staleBegin.Sequence,
            "bounded actual server denial supplies persisted sequence floor without resetting replay state");
        restarted.Deliver(resync, 61);
        Check(!restarted.Local.Pending && restarted.Sequence >= oldHigh && restarted.Releases > 0,
            "actual authenticated client ACK handling raises shared allocator and releases rejected pending capture");
        Check(restarted.Begin(62), "next native retry allocates above verified reconnect floor");
        fixture.Tick(62); var successor = fixture.Ack; restarted.Deliver(successor, 62);
        Check(successor.Status == 0 && !successor.Terminal && successor.RequestId > resync.MinimumSequence
            && successor.LeaseId != secondGrant.LeaseId, "same sender's successor gesture succeeds after client reinitialization");
        double canonical = fixture.Value.Item1;
        fixture.Receive(Fixture.Request(firstGrant, UiDragKind.Move, restarted.Next(), 9));
        fixture.Receive(Fixture.Request(firstGrant, UiDragKind.End, restarted.Next(), 9));
        var oldBlur = Fixture.Request(firstGrant, UiDragKind.Blur, restarted.Next(), canonical);
        oldBlur.ViewerId = oldBlur.LeaseId = firstGrant.LeaseId; fixture.Receive(oldBlur);
        fixture.Tick(63);
        Check(fixture.Transactions.ActiveLeases == 1 && fixture.Transactions.PendingMoves == 0
            && fixture.Transactions.PendingTerminals == 0 && fixture.Value.Item1 == canonical,
            "old Move/End/Blur identities cannot operate successor even with newer packet sequences");
        restarted.Arm(1, 62); restarted.Move(7, 68); restarted.End(6, 69);
        Check(fixture.Value.Item1 == 6 && !restarted.Local.Pending && fixture.Transactions.ActiveLeases == 0,
            "successor remains usable and commits normally after stale old-lifetime packets");
    }
    static void LegacyScopeWireCompatibility()
    {
        using var fixture = new Fixture();
        var validate = typeof(HoloMapSession).GetMethod("ValidUiPress", BindingFlags.NonPublic | BindingFlags.Static);
        bool Valid(UiPress press) => (bool)validate.Invoke(null, new object[] { press });
        foreach (int action in new[] { 0, 2, 3 })
        {
            var legacy = new UiPress { Action = action, CallerId = 10, TargetId = 20, WidgetId = "slider", Revision = fixture.Display.Revision, Sequence = 1 };
            Check(Valid(legacy), "legacy action " + action + " retains its zero-viewer wire shape");
            var copy = MyAPIGateway.Utilities.SerializeFromBinary<UiPress>(MyAPIGateway.Utilities.SerializeToBinary(legacy));
            Check(copy.ViewerId == 0 && Valid(copy), "omitted additive viewer tag preserves legacy protobuf compatibility");
            legacy.ViewerId = 700;
            Check(!Valid(legacy), "ordinary legacy action cannot carry a persistent viewer nonce");
        }
        var close = new UiPress { Action = 1, Sequence = 1, WidgetId = "" };
        Check(Valid(close), "legacy close keeps zero viewer identity");
        close.ViewerId = 700;
        Check(!Valid(close), "legacy close rejects a viewer nonce carried through an ordinary action");
        var scoped = new UiPress { Action = 4, CallerId = 10, TargetId = 20, WidgetId = "slider", Revision = fixture.Display.Revision, Sequence = 1 };
        Check(!Valid(scoped), "persistent focused click cannot omit its exact viewer nonce");
        scoped.ViewerId = 700;
        Check(Valid(scoped), "persistent focused click carries a positive viewer nonce");
        var scopedCopy = MyAPIGateway.Utilities.SerializeFromBinary<UiPress>(MyAPIGateway.Utilities.SerializeToBinary(scoped));
        Check(scopedCopy.ViewerId == 700 && Valid(scopedCopy), "focused click nonce survives actual game protobuf bytes");
        var ack = new UiAck { CallerId = 10, TargetId = 20, WidgetId = "slider", Revision = fixture.Display.Revision, Sequence = 1, ViewerId = 700 };
        var ackCopy = MyAPIGateway.Utilities.SerializeFromBinary<UiAck>(MyAPIGateway.Utilities.SerializeToBinary(ack));
        Check(ackCopy.ViewerId == 700, "legacy ACK additive nonce survives actual game protobuf bytes");
        Check(typeof(UiPress).GetField("ViewerId").GetCustomAttribute<ProtoBuf.ProtoMemberAttribute>().Tag == 7
            && typeof(UiAck).GetField("ViewerId").GetCustomAttribute<ProtoBuf.ProtoMemberAttribute>().Tag == 10,
            "focused click and ACK use their additive stable protobuf field numbers");
    }
}

public interface NativeUiDragCharacter : IMyCharacter, VRage.Game.ModAPI.Interfaces.IMyControllableEntity { }
