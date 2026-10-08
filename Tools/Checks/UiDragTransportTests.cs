using System.Reflection;
using HoloMap;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage;

internal static class UiDragTransportTests
{
    static int _checks;
    internal static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Transport: " + name);
        _checks++;
    }
    public static int Run()
    {
        _checks = 0;
        WireContract();
        SerializedDragFlow();
        CoalescingAndTerminalPriority();
        LocksSenderAndReplay();
        CapacityAndRateLimits();
        CurrentAuthorityAndPreemption();
        WorldAndCommitValidation();
        CancellationAndLifecycle();
        CallbackFailures();
        HostReleaseLifecycle();
        LocalLeaseTests.Run(Check);
        PointerRayTests.Run(Check);
        CombinedTransportFlow();
        return _checks;
    }
    static UiDragRequest Begin(string control = "slider", long request = 1, long sequence = 1, int mode = 1)
        => new() { Kind = (int)UiDragKind.Begin, CallerId = 10, TargetId = 20, ControlId = control,
            DefinitionRevision = 1, ValueRevision = 1, RequestId = request, Sequence = sequence, Mode = mode, Value = 2 };
    static UiDragRequest Packet(UiDragAck grant, UiDragKind kind, long sequence, double value = 3)
        => new() { Kind = (int)kind, CallerId = grant.CallerId, TargetId = grant.TargetId, ControlId = grant.ControlId,
            DefinitionRevision = grant.DefinitionRevision, ValueRevision = grant.ValueRevision,
            RequestId = grant.RequestId, Sequence = sequence, LeaseId = grant.LeaseId, Mode = 1, Value = value };
    static UiDragAck Grant(Fixture fixture, ulong sender = 200, string control = "slider", int tick = 0, int mode = 1)
    {
        Check(fixture.Receive(sender, Begin(control, mode: mode), tick), "valid Begin enters queue");
        fixture.Service.Tick(tick);
        var ack = fixture.Last(sender);
        Check(ack.Status == (int)UiDragStatus.Accepted && !ack.Terminal && ack.LeaseId > 0, "Begin grants authoritative lease");
        return ack;
    }
    static void WireContract()
    {
        Check(!UiDragWire.IsDrag(null) && !UiDragWire.IsDrag(new byte[4]), "null/truncated prefix rejected");
        Check(UiDragWire.Wrap(null) == null && UiDragWire.Wrap(Array.Empty<byte>()) == null, "empty protobuf body rejected");
        var body = new byte[507]; body[0] = 19; body[506] = 255;
        byte[] wrapped = UiDragWire.Wrap(body);
        Check(wrapped.Length == 512 && UiDragWire.IsDrag(wrapped) && UiDragWire.Body(wrapped).SequenceEqual(body), "exact packet boundary roundtrips");
        body[0] = 7;
        Check(wrapped[5] == 19, "wrap copies caller-owned bytes");
        byte[] unpacked = UiDragWire.Body(wrapped); unpacked[0] = 8;
        Check(wrapped[5] == 19, "body returns detached bytes");
        Check(UiDragWire.Wrap(new byte[508]) == null, "oversized body rejected");
        Check(UiDragWire.Body(new byte[] { 72, 68, 82, 68, 49 }) == null, "prefix-only packet rejected");
        var oversize = new byte[513]; Array.Copy(wrapped, oversize, wrapped.Length);
        Check(UiDragWire.Body(oversize) == null, "oversized wrapped packet rejected before decode");
        wrapped[1] ^= 1;
        Check(!UiDragWire.IsDrag(wrapped) && UiDragWire.Body(wrapped) == null, "prefix mismatch rejected");
        foreach (string id in new[] { "a", "slider_01", new string('a', 24) }) Check(UiDragWire.Id(id), "valid identifier " + id);
        foreach (string id in new[] { null, "", "A", "a b", "a/b", "é", new string('a', 25) }) Check(!UiDragWire.Id(id), "hostile identifier rejected");
        Check(UiDragWire.Finite(1e12) && UiDragWire.Finite(-1e12), "inclusive finite value bounds");
        foreach (double value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, 1e12 + 1 })
            Check(!UiDragWire.Finite(value), "nonfinite/out-of-bound value rejected");
        Action<UiDragRequest>[] invalid = {
            p=>p.Protocol=2, p=>p.Kind=0, p=>p.Kind=8, p=>p.CallerId=0, p=>p.TargetId=0,
            p=>p.ControlId="bad id", p=>p.DefinitionRevision=0, p=>p.ValueRevision=0,
            p=>p.RequestId=0, p=>p.Sequence=0, p=>p.LeaseId=1, p=>p.Mode=2, p=>p.Value=double.NaN, p=>p.ViewerId=-1
        };
        foreach (var mutate in invalid) { var request = Begin(); mutate(request); Check(!UiDragWire.Valid(request), "invalid request field rejected"); }
        var pendingCancel = Begin(); pendingCancel.Kind = (int)UiDragKind.Cancel;
        Check(UiDragWire.Valid(pendingCancel), "pending cancellation permits zero lease");
        pendingCancel.Kind = (int)UiDragKind.Move;
        Check(!UiDragWire.Valid(pendingCancel), "move cannot use zero lease");
        var focus = Begin(); focus.Kind = (int)UiDragKind.Focus;
        Check(UiDragWire.Valid(focus), "Focus begins a pending viewer with no granted lease");
        focus.LeaseId = 41;
        Check(!UiDragWire.Valid(focus), "Focus cannot pretend an existing lease grant");
        var heartbeat = Begin(); heartbeat.Kind = (int)UiDragKind.KeepAlive; heartbeat.ViewerId = heartbeat.LeaseId = 41;
        Check(UiDragWire.Valid(heartbeat), "KeepAlive addresses one exact positive viewer/lease identity");
        heartbeat.ViewerId = 0;
        Check(!UiDragWire.Valid(heartbeat), "KeepAlive cannot use a missing viewer identity");
        heartbeat.ViewerId = 42;
        Check(!UiDragWire.Valid(heartbeat), "KeepAlive cannot mismatch viewer and lease identity");
        var blur = Begin(); blur.Kind = (int)UiDragKind.Blur;
        Check(UiDragWire.Valid(blur), "pending Blur permits zero viewer and lease");
        blur.ViewerId = blur.LeaseId = 41;
        Check(UiDragWire.Valid(blur), "granted Blur binds matching positive viewer and lease");
        blur.LeaseId = 42;
        Check(!UiDragWire.Valid(blur), "Blur rejects a mismatched granted viewer identity");
        var scalarTypes = new[] { typeof(int), typeof(long), typeof(double), typeof(string), typeof(bool) };
        foreach (Type type in new[] { typeof(UiDragRequest), typeof(UiDragAck) })
        {
            var fields = type.GetFields();
            Check(fields.All(field => scalarTypes.Contains(field.FieldType)), type.Name + " uses primitive wire fields only");
            var tags = fields.Select(field => field.GetCustomAttribute<ProtoMemberAttribute>()?.Tag).ToArray();
            Check(tags.All(tag => tag.HasValue) && tags.Distinct().Count() == fields.Length, type.Name + " has explicit unique protobuf tags");
        }
        using var fixture = new Fixture();
        var full = Begin(new string('a', 24), long.MaxValue - 1, long.MaxValue - 2);
        full.CallerId = long.MinValue; full.TargetId = long.MaxValue; full.DefinitionRevision = long.MaxValue;
        full.ValueRevision = long.MaxValue; full.Value = -1e12; full.ViewerId = long.MaxValue;
        var roundtrip = MyAPIGateway.Utilities.SerializeFromBinary<UiDragRequest>(MyAPIGateway.Utilities.SerializeToBinary(full));
        Check(typeof(UiDragRequest).GetFields().All(field => Equals(field.GetValue(full), field.GetValue(roundtrip))), "all request primitives survive real protobuf roundtrip");
        Check(UiDragWire.Wrap(MyAPIGateway.Utilities.SerializeToBinary(full)).Length <= 512, "largest supported request remains below packet cap");
        int decoded = fixture.Gateway.Deserializations;
        Check(!fixture.ReceiveBytes(200, new byte[] { 72, 68, 82, 68, 49 }, 0) && fixture.Gateway.Deserializations == decoded, "invalid envelope avoids protobuf work");
        Check(!fixture.ReceiveBytes(200, UiDragWire.Wrap(new byte[] { 255 }), 0), "malformed protobuf rejected without mutation");
        Check(fixture.Service.ActiveLeases == 0 && fixture.CommitCount == 0, "malformed datagrams leave service unchanged");
        var badAck = new UiDragAck { Kind = 1, CallerId = 10, TargetId = 20, ControlId = "slider", DefinitionRevision = 1, ValueRevision = 1, RequestId = 1, Sequence = 1 };
        Check(!UiDragWire.Valid(badAck), "accepted ACK needs lease and authority IDs");
        badAck.Status = (int)UiDragStatus.Stale; badAck.Terminal = true;
        Check(UiDragWire.Valid(badAck), "rejected Begin ACK may have no lease");
        badAck.Status = 9;
        Check(!UiDragWire.Valid(badAck), "unknown ACK status rejected");
        var fullAck = new UiDragAck { Kind = (int)UiDragKind.KeepAlive, CallerId = long.MinValue, TargetId = long.MaxValue,
            ControlId = new string('a', 24), DefinitionRevision = long.MaxValue, ValueRevision = long.MaxValue,
            RequestId = long.MaxValue, Sequence = long.MaxValue, LeaseId = long.MaxValue, Status = 0,
            Value = 1e12, TileId = long.MinValue, CharacterId = long.MaxValue, Terminal = true,
            MinimumSequence = long.MaxValue, ViewerId = long.MaxValue };
        Check(UiDragWire.Valid(fullAck), "ACK viewer and reconnect floor support maximum primitive identity values");
        var ackRoundtrip = MyAPIGateway.Utilities.SerializeFromBinary<UiDragAck>(MyAPIGateway.Utilities.SerializeToBinary(fullAck));
        Check(typeof(UiDragAck).GetFields().All(field => Equals(field.GetValue(fullAck), field.GetValue(ackRoundtrip))),
            "all ACK primitives including viewer and reconnect floor survive game protobuf");
        Check(UiDragWire.Wrap(MyAPIGateway.Utilities.SerializeToBinary(fullAck)).Length <= 512, "largest supported viewer ACK stays below packet cap");
        fullAck.MinimumSequence = -1;
        Check(!UiDragWire.Valid(fullAck), "negative reconnect ACK floor rejected");
        fullAck.MinimumSequence = 0; fullAck.ViewerId = -1;
        Check(!UiDragWire.Valid(fullAck), "negative ACK viewer identity rejected");
    }
    static void SerializedDragFlow()
    {
        using var fixture = new Fixture();
        var begin = Begin();
        Check(fixture.Receive(200, begin, 0), "bytes receiver accepts Begin");
        Check(fixture.Gateway.Sent.Count == 0 && fixture.Service.ActiveLeases == 0, "network callback queues without game mutation");
        begin.ControlId = "mutated"; begin.Value = 99;
        fixture.Service.Tick(0);
        var grant = fixture.Last();
        Check(grant.Value == 2 && grant.ControlId == "slider" && grant.TileId == 30 && grant.CharacterId == 40, "grant carries canonical value and server-resolved authority");
        Check(grant.DefinitionRevision == 1 && grant.ValueRevision == 1 && grant.RequestId == 1 && grant.Sequence == 1, "grant preserves request/revision identities");
        var move = Packet(grant, UiDragKind.Move, 2, 5.25);
        Check(fixture.Receive(200, move, 1), "serialized Move accepted");
        fixture.Service.Tick(6);
        var changed = fixture.Last();
        Check(changed.Kind == 2 && !changed.Terminal && changed.Value == 5.25 && changed.ValueRevision == 2, "serialized ACK reports canonical commit and new revision");
        Check(changed.LeaseId == grant.LeaseId && fixture.Gateway.Sent.All(sent => sent.Recipient == 200), "ACKs target bound sender with stable lease");
        Check(fixture.Receive(200, Packet(changed, UiDragKind.End, 3, 8), 7), "serialized End accepted");
        fixture.Service.Tick(7);
        var ended = fixture.Last();
        Check(ended.Terminal && ended.Kind == 3 && ended.Status == 0 && ended.Value == 8 && ended.ValueRevision == 3, "End commits final value and produces serialized terminal ACK");
        Check(fixture.Service.ActiveLeases == 0 && fixture.Service.PendingMoves == 0 && fixture.Service.PendingTerminals == 0, "End releases all lease work");
        Check(fixture.AllAcks().All(UiDragWire.Valid), "every emitted ACK passes real wire validation");
    }
    static void CoalescingAndTerminalPriority()
    {
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture);
            for (int sequence = 2; sequence <= 5; sequence++) Check(fixture.Receive(200, Packet(grant, UiDragKind.Move, sequence, sequence), 1), "Move flood admitted into coalesced slot");
            Check(fixture.Service.PendingMoves == 1, "one latest move per lease retained");
            fixture.Service.Tick(5);
            Check(fixture.CommitCount == 0, "10 Hz cadence blocks move before six simulation ticks");
            fixture.Service.Tick(6);
            Check(fixture.CommitCount == 1 && fixture.Last().Sequence == 5 && fixture.Last().Value == 5, "sixth tick commits latest coalesced move");
            var next = Packet(fixture.Last(), UiDragKind.Move, 6, 6);
            Check(fixture.Service.Submit(200, next, 7), "direct queue accepts immutable move copy"); next.Value = 99;
            fixture.Service.Tick(11); Check(fixture.CommitCount == 1, "next cadence shares previous commit clock");
            fixture.Service.Tick(12); Check(fixture.CommitCount == 2 && fixture.Last().Value == 6, "queued move copied and next sixth tick commits");
            Check(fixture.Receive(200, Packet(fixture.Last(), UiDragKind.Move, 7, 7), 13), "pending move accepted before terminal");
            var end = Packet(fixture.Last(), UiDragKind.End, 8, 8);
            Check(fixture.Service.Submit(200, end, 13), "End accepted before move cadence"); end.Value = 99;
            Check(fixture.Service.PendingMoves == 0 && fixture.Service.PendingTerminals == 1, "terminal removes pending move and owns reserved slot");
            Check(!fixture.Receive(200, Packet(fixture.Last(), UiDragKind.Cancel, 9), 13), "first terminal wins");
            fixture.Service.Tick(13);
            Check(fixture.CommitCount == 3 && fixture.Last().Value == 8 && fixture.Last().Terminal, "End copy commits immediately despite cadence");
        }
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture);
            fixture.Receive(200, Packet(grant, UiDragKind.Move, 2, 9), 1);
            fixture.Receive(200, Packet(grant, UiDragKind.Cancel, 3), 1);
            fixture.Service.Tick(1);
            Check(fixture.CommitCount == 0 && fixture.Service.ActiveLeases == 0 && fixture.Last().Status == 5, "Cancel releases without committing queued prediction");
        }
    }
    static void LocksSenderAndReplay()
    {
        using var fixture = new Fixture();
        fixture.Add("alias", "value"); fixture.Add("other", "other");
        var grant = Grant(fixture);
        Check(fixture.Receive(201, Begin("alias"), 0), "second peer can request alias"); fixture.Service.Tick(0);
        Check(fixture.Last(201).Status == (int)UiDragStatus.Busy && fixture.Service.ActiveLeases == 1, "same canonical value locks across control aliases");
        Check(fixture.Receive(202, Begin("other"), 0), "independent value begins"); fixture.Service.Tick(0);
        Check(fixture.Service.ActiveLeases == 2 && fixture.Last(202).Status == 0, "independent value permits another peer");
        Check(fixture.Receive(200, Begin("other", 2, 2), 0), "same sender second Begin queued"); fixture.Service.Tick(0);
        Check(fixture.Last(200).Status == 2 && fixture.Service.ActiveLeases == 2, "sender owns at most one lease");
        Check(!fixture.Receive(203, Packet(grant, UiDragKind.Move, 1), 1), "different sender cannot borrow lease");
        Action<UiDragRequest>[] wrongIdentity = { p=>p.RequestId++, p=>p.CallerId++, p=>p.TargetId++, p=>p.ControlId="alias", p=>p.DefinitionRevision++, p=>p.Mode=0, p=>p.ValueRevision=2 };
        long sequence = 3;
        foreach (var mutate in wrongIdentity) { var p = Packet(grant, UiDragKind.Move, sequence++); mutate(p); Check(!fixture.Receive(200, p, 1), "changed binding field rejected"); }
        var accepted = Packet(grant, UiDragKind.Move, sequence, 9);
        Check(fixture.Receive(200, accepted, 1), "fresh matching sequence accepted after hostile packets");
        Check(!fixture.Receive(200, accepted, 1), "duplicate packet sequence rejected");
        Check(!fixture.Receive(200, Packet(grant, UiDragKind.Move, sequence - 1), 1), "out-of-order packet rejected");
        fixture.Service.Tick(6);
        Check(fixture.CommitCount == 1 && fixture.Last(200).Sequence == sequence, "replayed/forged packets cannot mutate canonical value");
        Check(fixture.Receive(200, Packet(grant, UiDragKind.Move, sequence + 1, 10), 7), "older acknowledged revision remains usable within same lease");
        fixture.Service.Tick(12);
        Check(fixture.Last(200).ValueRevision == 3, "lease advances its own revision through delayed request ACKs");
    }
    static void CapacityAndRateLimits()
    {
        using (var fixture = new Fixture())
        {
            for (int i = 0; i < 48; i++) fixture.Add("c" + i, "v" + i);
            for (int i = 0; i < 16; i++) Check(fixture.Receive((ulong)(200 + i), Begin("c" + i), 0), "Begin queue accepts fixed capacity");
            Check(!fixture.Receive(300, Begin("c16"), 0), "Begin queue overflow rejected");
            fixture.Service.Tick(0); Check(fixture.Service.ActiveLeases == 4, "simulation tick bounds Begin work to four");
            fixture.Service.Tick(1); fixture.Service.Tick(2); fixture.Service.Tick(3);
            Check(fixture.Service.ActiveLeases == 16, "queued Begins eventually receive fair simulation work");
            for (int i = 16; i < 32; i++) Check(fixture.Receive((ulong)(200 + i), Begin("c" + i), 60), "next admission window accepts more peers");
            for (int tick = 60; tick < 64; tick++) fixture.Service.Tick(tick);
            Check(fixture.Service.ActiveLeases == 32, "active leases bounded at maximum");
            Check(fixture.Receive(1000, Begin("c32"), 64), "excess active lease request may enter validation queue");
            fixture.Service.Tick(64);
            Check(fixture.Last(1000).Status == 2 && fixture.Service.ActiveLeases == 32, "thirty-third active lease receives Busy");
            for (int i = 32; i < 48; i++) Check(fixture.Receive((ulong)(200 + i), Begin("c" + i), 120), "Begin capacity may fill beside active leases");
            for (int i = 0; i < 32; i++)
            {
                var ack = fixture.Last((ulong)(200 + i));
                Check(fixture.Receive((ulong)(200 + i), Packet(ack, UiDragKind.Move, 2, 4), 120), "all active leases reserve move slots");
                Check(fixture.Receive((ulong)(200 + i), Packet(ack, i % 2 == 0 ? UiDragKind.End : UiDragKind.Cancel, 3, 5), 120), "all terminals survive full Begin queue and move flood");
            }
            Check(fixture.Service.PendingTerminals == 32 && fixture.Service.PendingMoves == 0, "terminal capacity reserved per lease");
            fixture.Service.Tick(120);
            Check(fixture.Service.PendingTerminals == 0 && fixture.Service.ActiveLeases == 4 && fixture.CommitCount == 16, "all terminals process before new Begin budget");
            Check(fixture.AllAcks().Count(ack => ack.Terminal && ack.LeaseId > 0) == 32, "every active sender receives terminal ACK under saturation");
        }
        using (var fixture = new Fixture())
        {
            for (int i = 1; i <= 5; i++) Check(fixture.Receive(200, Begin(request: i, sequence: i), 0), "five per-peer Begin attempts admitted");
            Check(!fixture.Receive(200, Begin(request: 6, sequence: 6), 0), "sixth per-peer Begin throttled");
            fixture.Service.Tick(0); fixture.Service.Tick(1);
            Check(!fixture.Receive(200, Begin(request: 6, sequence: 6), 60), "throttled sequence consumed permanently before queue admission");
            Check(fixture.Receive(200, Begin(request: 7, sequence: 7), 60), "per-peer Begin limit resets after sixty ticks");
        }
        using (var fixture = new Fixture())
        {
            for (int i = 0; i < 16; i++) fixture.Receive((ulong)(200 + i), Begin(), 0);
            fixture.Service.Tick(0);
            for (int i = 16; i < 20; i++) Check(fixture.Receive((ulong)(200 + i), Begin(), 0), "global Begin allowance admits twenty across queue drains");
            Check(!fixture.Receive(220, Begin(), 0), "twenty-first global Begin attempt throttled");
        }
        using (var fixture = new Fixture())
        {
            for (int i = 0; i < UiDragTransactions.MaxPeers; i++) fixture.Receive((ulong)(200 + i), Begin(), 0);
            Check(!fixture.Receive(1000, Begin(), 0), "peer table bounded even for rejected queue attempts");
            fixture.Service.Tick(3660);
            Check(fixture.Receive(1000, Begin(), 3660), "idle peer records pruned to recover bounded admission capacity");
        }
    }
    static void CurrentAuthorityAndPreemption()
    {
        Action<Fixture>[] invalidations = {
            f=>f.AuthorityValid=false, f=>f.CharacterId++, f=>f.TileId++,
            f=>f.Records["slider"].DefinitionRevision++, f=>f.Records["slider"].ValueRevision++
        };
        foreach (var invalidate in invalidations)
        {
            using var fixture = new Fixture(); var grant = Grant(fixture);
            fixture.Receive(200, Packet(grant, UiDragKind.Move, 2, 9), 1); invalidate(fixture); fixture.Service.Tick(6);
            Check(fixture.CommitCount == 0 && fixture.Service.ActiveLeases == 0 && fixture.Last().Status == 4 && fixture.Last().Terminal, "current authority/source-revision change revokes before commit");
        }
        using (var fixture = new Fixture())
        {
            fixture.Records["slider"].ValueRevision = 2;
            Check(fixture.Receive(200, Begin(), 0), "stale Begin admitted only as queued validation work"); fixture.Service.Tick(0);
            Check(fixture.Last().Status == 3 && fixture.Last().Terminal && fixture.Service.ActiveLeases == 0, "stale source value cannot grant lease");
        }
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture);
            fixture.Receive(200, Packet(grant, UiDragKind.Move, 2, 9), 1);
            fixture.Service.Preempt(10, 20, "value");
            Check(fixture.Last().Status == 7 && fixture.Last().Terminal && fixture.Service.PendingMoves == 0 && fixture.Service.ActiveLeases == 0, "programmatic preemption drops prediction and releases lock");
            fixture.Service.Tick(6); Check(fixture.CommitCount == 0, "preempted queued move never commits");
            Check(fixture.Receive(201, Begin(), 6), "new sender can begin after preemption"); fixture.Service.Tick(6);
            Check(fixture.Last(201).LeaseId > grant.LeaseId && fixture.Last(201).Status == 0, "released value admits new monotonically distinct lease");
        }
    }
    static void WorldAndCommitValidation()
    {
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture, mode: 0); fixture.Derived = 42;
            var move = Packet(grant, UiDragKind.Move, 2, 999); move.Mode = 0;
            fixture.Receive(200, move, 1); fixture.Service.Tick(6);
            Check(fixture.Last().Value == 42 && fixture.WorldCalls == 1 && fixture.CommitCount == 1, "world-look derives value server-side and ignores submitted pointer value");
        }
        foreach (double? derived in new double?[] { null, double.NaN, double.PositiveInfinity, 1e12 + 1 })
        {
            using var fixture = new Fixture(); var grant = Grant(fixture, mode: 0); fixture.Derived = derived;
            var move = Packet(grant, UiDragKind.Move, 2); move.Mode = 0; fixture.Receive(200, move, 1); fixture.Service.Tick(6);
            Check(fixture.CommitCount == 0 && fixture.Last().Status == 8 && fixture.Last().Terminal, "missing/nonfinite world derivation rejects without commit");
        }
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture); fixture.Receive(200, Packet(grant, UiDragKind.Move, 2, 4), 1); fixture.Service.Tick(6);
            Check(fixture.WorldCalls == 0 && fixture.Last().Value == 4, "focused mode uses pointer value without world resolver");
        }
        var invalidResults = new (Func<UiDragBinding,double,UiDragCommit> Commit, UiDragStatus Status)[] {
            ((b,v)=>null, UiDragStatus.Stale),
            ((b,v)=>new(){Accepted=false,Status=UiDragStatus.Accepted}, UiDragStatus.Stale),
            ((b,v)=>new(){Accepted=true,Value=b.Value+1,ValueRevision=b.ValueRevision,Status=UiDragStatus.Accepted}, UiDragStatus.Stale),
            ((b,v)=>new(){Accepted=true,Value=b.Value,ValueRevision=b.ValueRevision-1,Status=UiDragStatus.Accepted}, UiDragStatus.Stale),
            ((b,v)=>new(){Accepted=true,Value=double.NaN,ValueRevision=b.ValueRevision+1,Status=UiDragStatus.Accepted}, UiDragStatus.Bounds)
        };
        foreach (var result in invalidResults)
        {
            using var fixture = new Fixture(); var grant = Grant(fixture); fixture.CommitOverride = result.Commit;
            fixture.Receive(200, Packet(grant, UiDragKind.Move, 2, 9), 1); fixture.Service.Tick(6);
            Check(fixture.Last().Status == (int)result.Status && fixture.Last().Terminal && fixture.Last().Value == 2 && fixture.Last().ValueRevision == 1,
                "hostile host result cannot acknowledge success or regress canonical state");
            Check(fixture.Service.ActiveLeases == 0, "invalid host result releases lease");
        }
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture); fixture.CommitOverride = (b,v)=>new(){Accepted=true,Value=b.Value,ValueRevision=b.ValueRevision};
            fixture.Receive(200, Packet(grant, UiDragKind.Move, 2, 2), 1); fixture.Service.Tick(6);
            Check(fixture.Last().Status == 0 && !fixture.Last().Terminal && fixture.Last().ValueRevision == 1, "canonical no-op may retain value revision");
        }
    }
    static void CancellationAndLifecycle()
    {
        using (var fixture = new Fixture())
        {
            var begin = Begin(); fixture.Receive(200, begin, 0);
            var cancel = Begin(sequence: 2); cancel.Kind = 4;
            Check(fixture.Receive(200, cancel, 0), "pending zero-lease cancellation admitted"); fixture.Service.Tick(0);
            Check(fixture.BeginCount == 0 && fixture.Service.ActiveLeases == 0 && fixture.Last().Kind == 4 && fixture.Last().Status == 5 && fixture.Last().Sequence == 2,
                "cancel before grant prevents begin callback and emits terminal identity");
        }
        using (var fixture = new Fixture())
        {
            fixture.Add("other", "other");
            fixture.Receive(200, Begin(), 0);
            Check(!fixture.Receive(200, Begin("other", sequence: 2), 0), "reused Begin RequestId rejected across controls");
            var cancel = Begin("other", sequence: 3); cancel.Kind = 4;
            Check(!fixture.Receive(200, cancel, 0), "wrong-control pending cancellation rejected");
            fixture.Service.Tick(0);
            var acks = fixture.AllAcks().ToArray();
            Check(acks.Count(ack => ack.Status == 0 && !ack.Terminal) == 1
                && acks.Any(ack => ack.ControlId == "slider" && ack.Status == 0 && !ack.Terminal)
                && acks.All(ack => ack.ControlId != "other" || ack.Status != 0 && ack.Terminal),
                "wrong-control cancellation cannot apply to queued control sharing RequestId");
        }
        using (var fixture = new Fixture())
        {
            fixture.Receive(200, Begin(), 0);
            Check(!fixture.Receive(200, Begin(sequence: 2), 0), "duplicate queued Begin RequestId rejected despite newer sequence");
            var cancel = Begin(sequence: 3); cancel.Kind = 4;
            Check(fixture.Receive(200, cancel, 0), "duplicate queued Begin identity remains cancellable");
            fixture.Service.Tick(0);
            Check(fixture.BeginCount == 0 && fixture.Service.ActiveLeases == 0, "all duplicate queued Begins cancelled without lease resurrection");
        }
        using (var fixture = new Fixture())
        {
            Grant(fixture); var cancel = Begin(sequence: 2); cancel.Kind = 4;
            Check(fixture.Receive(200, cancel, 1), "zero-lease cancellation binds server lease while grant in flight"); fixture.Service.Tick(1);
            Check(fixture.Service.ActiveLeases == 0 && fixture.Last().Terminal && fixture.Last().LeaseId > 0 && fixture.CommitCount == 0, "grant-in-flight cancellation terminates without commit");
        }
        foreach (Action<UiDragRequest> mutate in new Action<UiDragRequest>[] { p=>p.DefinitionRevision++, p=>p.ValueRevision++, p=>p.Mode=0 })
        {
            using var fixture = new Fixture(); fixture.Receive(200, Begin(), 0);
            var cancel = Begin(sequence: 2); cancel.Kind = 4; mutate(cancel);
            Check(!fixture.Receive(200, cancel, 0), "pending cancellation rejects changed definition/value/mode identity");
            fixture.Service.Tick(0); Check(fixture.Service.ActiveLeases == 1, "invalid pending cancellation cannot discard valid Begin");
        }
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture); fixture.Receive(200, Packet(grant, UiDragKind.End, 2, 8), 1);
            var cancel = Begin(sequence: 3); cancel.Kind = 4;
            Check(!fixture.Receive(200, cancel, 1), "pending-style cancellation cannot overwrite active reserved End");
            fixture.Service.Tick(1); Check(fixture.Last().Kind == 3 && fixture.Last().Value == 8, "first terminal stays authoritative against zero-lease cancellation");
        }
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture); fixture.Receive(200, Packet(grant, UiDragKind.Move, 2), 1);
            fixture.Service.CancelPeer(200);
            Check(fixture.Service.ActiveLeases == 0 && fixture.Service.PendingMoves == 0 && fixture.Last().Status == 5, "peer cancellation releases active work");
            fixture.Service.Tick(6); Check(fixture.CommitCount == 0, "cancelled peer prediction cannot commit later");
        }
        using (var fixture = new Fixture())
        {
            Grant(fixture); fixture.Service.Tick(180); Check(fixture.Service.ActiveLeases == 1, "idle timeout inclusive boundary remains live");
            fixture.Service.Tick(181); Check(fixture.Service.ActiveLeases == 0 && fixture.Last().Status == 6, "idle timeout revokes on next tick");
        }
        using (var fixture = new Fixture())
        {
            fixture.Service.IdleTicks = 100; fixture.Service.LifetimeTicks = 12;
            var grant = Grant(fixture); fixture.Receive(200, Packet(grant, UiDragKind.Move, 2), 6); fixture.Service.Tick(6);
            fixture.Service.Tick(12); Check(fixture.Service.ActiveLeases == 1, "lifetime boundary remains live despite active movement");
            fixture.Service.Tick(13); Check(fixture.Service.ActiveLeases == 0 && fixture.Last().Status == 6, "lifetime timeout unaffected by recent commit");
        }
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture); fixture.Receive(200, Packet(grant, UiDragKind.End, 2, 9), 181);
            fixture.Service.Tick(181);
            Check(fixture.CommitCount == 0 && fixture.Last().Kind == 3 && fixture.Last().Status == 6 && fixture.Last().Terminal
                && fixture.Service.ActiveLeases == 0, "late End releases reserved terminal without committing expired lease");
        }
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture); fixture.Receive(200, Packet(grant, UiDragKind.Move, 2), 1);
            fixture.Service.Clear(); fixture.Service.Tick(6);
            Check(fixture.Service.ActiveLeases == 0 && fixture.Service.PendingMoves == 0 && fixture.Service.PendingTerminals == 0 && fixture.CommitCount == 0, "clear drops all queued and active lifecycle state");
        }
    }
    static void CallbackFailures()
    {
        using (var fixture = new Fixture())
        {
            fixture.ThrowBegin = true; fixture.Receive(200, Begin(), 0); fixture.Service.Tick(0);
            Check(fixture.Service.ActiveLeases == 0 && fixture.Last().Status == 3, "begin callback failure rejects Stale without escape");
        }
        using (var fixture = new Fixture())
        {
            fixture.ThrowValidate = true; fixture.Receive(200, Begin(), 0); fixture.Service.Tick(0);
            Check(fixture.Service.ActiveLeases == 0 && fixture.Last().Status == 4, "validation callback failure rejects ContextLost without escape");
        }
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture); fixture.ThrowCommit = true;
            fixture.Receive(200, Packet(grant, UiDragKind.End, 2), 1); fixture.Service.Tick(1);
            Check(fixture.Service.ActiveLeases == 0 && fixture.Last().Status == 4 && fixture.Last().Terminal, "terminal callback failure always releases lease");
        }
        var beginCount = 0;
        var service = new UiDragTransactions((sender,request)=> { beginCount++; return new UiDragBinding { CallerId=10,TargetId=20,ControlId="slider",ValueId="value",Mode=1,CharacterId=40,TileId=30,DefinitionRevision=1,ValueRevision=1,Value=2 }; },
            (sender,binding)=>true, (binding,value)=>new UiDragCommit{Accepted=true,Value=value,ValueRevision=2}, binding=>null,
            (sender,ack)=>throw new InvalidOperationException("transport down"));
        Check(service.Submit(200, Begin(), 0), "Begin remains queueable with throwing ACK callback"); service.Tick(0);
        Check(beginCount == 1 && service.ActiveLeases == 1, "ACK failure does not escape or corrupt lease authority");
        service.CancelPeer(200); Check(service.ActiveLeases == 0, "ACK failure cannot stop peer release");
    }
    static void HostReleaseLifecycle()
    {
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture); fixture.Receive(200, Packet(grant, UiDragKind.End, 2, 9), 1); fixture.Service.Tick(1);
            Check(fixture.Releases.Count == 1 && fixture.Releases[0].Commit, "successful End releases host session exactly once with commit flag");
            fixture.Service.Clear(); Check(fixture.Releases.Count == 1, "clear cannot release already completed host session again");
        }
        using (var fixture = new Fixture())
        {
            var grant = Grant(fixture); fixture.Receive(200, Packet(grant, UiDragKind.Cancel, 2), 1); fixture.Service.Tick(1);
            Check(fixture.Releases.Count == 1 && !fixture.Releases[0].Commit, "Cancel releases host session without commit");
        }
        using (var fixture = new Fixture())
        {
            Grant(fixture); fixture.Service.Clear();
            Check(fixture.Releases.Count == 1 && !fixture.Releases[0].Commit, "clear releases abandoned host session");
        }
        using (var fixture = new Fixture())
        {
            fixture.ThrowValidate = true; fixture.Receive(200, Begin(), 0); fixture.Service.Tick(0);
            Check(fixture.Releases.Count == 1 && !fixture.Releases[0].Commit, "rejected resolved binding cleans host session");
        }
        using (var fixture = new Fixture())
        {
            Grant(fixture); fixture.ThrowRelease = true; fixture.Service.CancelPeer(200);
            Check(fixture.Service.ActiveLeases == 0 && fixture.Releases.Count == 1, "throwing host release cannot retain transport locks");
        }
    }
    static void CombinedTransportFlow()
    {
        using var fixture = new Fixture();
        var bridge = new PointerInputBridge();
        int providerFlags = 1, pressed = 1, held = 1, released = 0, providerReleases = 0, tick = 0;
        long frame = 1;
        Func<string, object[], object> endpoint = (verb, args) =>
        {
            if (verb == "acquire") return new MyTuple<long, int>(77, 1);
            if (verb == "sample") return new MyTuple<long, int, MyTuple<double, double>, MyTuple<int, int, int>>
                (frame, providerFlags, new MyTuple<double, double>(0.4, 0.6), new MyTuple<int, int, int>(pressed, held, released));
            if (verb == "release") { providerReleases++; return null; }
            throw new Exception("Unexpected pointer verb: " + verb);
        };
        bridge.Receive(new MyTuple<string, int, Func<string, object[], object>>(PointerInputBridge.Version, 1, endpoint));
        Check(bridge.Acquire(10, "combined"), "combined flow acquires pending provider lease");
        var local = new UiDragLocalLease(request => Check(fixture.Receive(200, request, tick), "local request passes real protobuf receiver"), bridge.Release);
        Check(local.Begin(Begin(), tick), "combined local drag begins and serializes queued request");
        Check(bridge.Sample(out var pending) && !local.Pointer(pending, tick) && !local.Active, "native pending state blocks local drag before provider apply");
        fixture.Service.Tick(tick); local.Receive(fixture.Last(), tick);
        Check(!local.Active && local.LeaseId > 0, "server grant alone cannot activate pointer ownership");
        providerFlags = 18; frame++; pressed = 0;
        Check(bridge.Sample(out var earlyHeld) && !local.Pointer(earlyHeld, tick), "pregrant press edge cannot turn later held state into a drag");
        frame++; pressed = 1;
        Check(bridge.Sample(out var active) && local.Pointer(active, tick) && local.Active, "fresh edge after both ownership barriers activates combined flow");
        tick = 6;
        Check(local.Predict(7, tick) && fixture.Service.PendingMoves == 1 && fixture.Records["slider"].Value == 2,
            "local prediction serializes move while canonical host waits for Tick");
        fixture.Service.Tick(tick); local.Receive(fixture.Last(), tick);
        Check(local.Confirmed == 7 && local.Prediction == 7 && local.ValueRevision == 2, "serialized authoritative ACK reconciles local prediction");
        tick = 7;
        Check(local.Predict(8, tick) && fixture.Service.PendingMoves == 0, "new unsent prediction obeys shared 10 Hz cadence");
        local.End(tick);
        Check(!local.Active && bridge.Token == 0 && providerReleases == 1, "End releases native ownership before waiting for server terminal ACK");
        fixture.Service.Tick(tick); local.Receive(fixture.Last(), tick);
        Check(fixture.Last().Terminal && fixture.Last().Value == 8 && fixture.Last().ValueRevision == 3
            && fixture.Service.ActiveLeases == 0 && !local.Pending, "final prediction commits through serialized End and ACK closes both leases");
        bridge.Close();
    }

    internal sealed class Record
    {
        public string ValueId;
        public long DefinitionRevision = 1, ValueRevision = 1;
        public double Value = 2;
    }
    internal sealed class Fixture : IDisposable
    {
        public readonly UiDragTransportGatewayScope Gateway = new();
        public readonly Dictionary<string, Record> Records = new();
        public readonly UiDragTransactions Service;
        public bool AuthorityValid = true, ThrowBegin, ThrowValidate, ThrowCommit, ThrowRelease;
        public readonly List<(UiDragBinding Binding, bool Commit)> Releases = new();
        public long CharacterId = 40, TileId = 30;
        public int BeginCount, CommitCount, WorldCalls;
        public double? Derived = 42;
        public Func<UiDragBinding,double,UiDragCommit> CommitOverride;
        public Fixture()
        {
            Add("slider", "value");
            Service = new UiDragTransactions(Resolve, Validate, Commit, binding => { WorldCalls++; return Derived; }, Send,
                (binding, commit) => { Releases.Add((binding, commit)); if (ThrowRelease) throw new InvalidOperationException("host release failed"); });
        }
        public void Add(string control, string value)
        {
            Record shared = Records.Values.FirstOrDefault(record => record.ValueId == value);
            Records[control] = shared ?? new Record { ValueId = value };
        }
        UiDragBinding Resolve(ulong sender, UiDragRequest request)
        {
            BeginCount++;
            if (ThrowBegin) throw new InvalidOperationException("host failed");
            if (!Records.TryGetValue(request.ControlId, out var record)) return null;
            return new UiDragBinding { CallerId = request.CallerId, TargetId = request.TargetId, ControlId = request.ControlId,
                ValueId = record.ValueId, DefinitionRevision = record.DefinitionRevision, ValueRevision = record.ValueRevision,
                Value = record.Value, Mode = request.Mode, CharacterId = CharacterId, TileId = TileId, Context = record };
        }
        bool Validate(ulong sender, UiDragBinding binding)
        {
            if (ThrowValidate) throw new InvalidOperationException("host failed");
            var record = (Record)binding.Context;
            return AuthorityValid && sender != 0 && binding.CharacterId == CharacterId && binding.TileId == TileId
                && binding.DefinitionRevision == record.DefinitionRevision && binding.ValueRevision == record.ValueRevision;
        }
        UiDragCommit Commit(UiDragBinding binding, double value)
        {
            CommitCount++;
            if (ThrowCommit) throw new InvalidOperationException("host failed");
            if (CommitOverride != null) return CommitOverride(binding, value);
            var record = (Record)binding.Context;
            if (!Validate(200, binding)) return new UiDragCommit { Status = UiDragStatus.Stale };
            if (Math.Abs(value) > 100) return new UiDragCommit { Status = UiDragStatus.Bounds };
            if (value != record.Value) { record.Value = value; record.ValueRevision++; }
            return new UiDragCommit { Accepted = true, Value = record.Value, ValueRevision = record.ValueRevision };
        }
        static void Send(ulong sender, UiDragAck ack)
        {
            byte[] bytes = UiDragWire.Wrap(MyAPIGateway.Utilities.SerializeToBinary(ack));
            if (bytes == null) throw new InvalidOperationException("ACK overflow");
            MyAPIGateway.Multiplayer.SendMessageTo(49784, bytes, sender);
        }
        public bool Receive(ulong sender, UiDragRequest request, int tick)
            => ReceiveBytes(sender, UiDragWire.Wrap(MyAPIGateway.Utilities.SerializeToBinary(request)), tick);
        public bool ReceiveBytes(ulong sender, byte[] bytes, int tick)
        {
            byte[] body = UiDragWire.Body(bytes);
            if (body == null) return false;
            try { return Service.Submit(sender, MyAPIGateway.Utilities.SerializeFromBinary<UiDragRequest>(body), tick); }
            catch { return false; }
        }
        public UiDragAck Last(ulong sender = 200)
        {
            byte[] body = UiDragWire.Body(Gateway.Sent.Last(sent => sent.Recipient == sender).Data);
            return MyAPIGateway.Utilities.SerializeFromBinary<UiDragAck>(body);
        }
        public IEnumerable<UiDragAck> AllAcks() => Gateway.Sent.Select(sent => MyAPIGateway.Utilities.SerializeFromBinary<UiDragAck>(UiDragWire.Body(sent.Data))).ToArray();
        public void Dispose() => Gateway.Dispose();
    }
}

// Adapted from Tools/Checks/ClientReplicationTests.cs. The real game protobuf
// serializer is retained; gateway replacements capture only transport edges.
public class UiDragTransportProxy : DispatchProxy
{
    public Func<MethodInfo, object[], object> Handler;
    protected override object Invoke(MethodInfo method, object[] args) => Handler(method, args);
    internal static object Make(Type type, Func<MethodInfo, object[], object> handler)
    {
        var proxy = (UiDragTransportProxy)Create(type, typeof(UiDragTransportProxy));
        proxy.Handler = handler;
        return proxy;
    }
}

internal sealed class UiDragTransportGatewayScope : IDisposable
{
    readonly List<(MemberInfo Member, object Old)> _old = new();
    readonly ProtoBuf.Meta.RuntimeTypeModel _model = ProtoBuf.Meta.RuntimeTypeModel.Create();
    public readonly List<(byte[] Data, ulong Recipient)> Sent = new();
    public int Deserializations;
    public UiDragTransportGatewayScope()
    {
        Install("Utilities", (method, args) =>
        {
            if (method.Name == "SerializeToBinary") return Serialize(args[0]);
            if (method.Name == "SerializeFromBinary")
            {
                Deserializations++;
                using var input = new MemoryStream((byte[])args[0]);
                return _model.Deserialize(input, null, method.GetGenericArguments()[0]);
            }
            throw new Exception("Unexpected utilities method: " + method.Name);
        });
        Install("Multiplayer", (method, args) =>
        {
            if (method.Name == "get_IsServer") return true;
            if (method.Name == "get_ServerId") return 100UL;
            if (method.Name == "SendMessageTo")
            {
                Sent.Add(((byte[])args[1], (ulong)args[2]));
                return true;
            }
            throw new Exception("Unexpected multiplayer method: " + method.Name);
        });
    }
    public byte[] Serialize(object value)
    {
        using var output = new MemoryStream();
        _model.Serialize(output, value);
        return output.ToArray();
    }
    void Install(string name, Func<MethodInfo, object[], object> handler)
    {
        MemberInfo member = (MemberInfo)typeof(MyAPIGateway).GetField(name) ?? typeof(MyAPIGateway).GetProperty(name);
        object old = member is FieldInfo field ? field.GetValue(null) : ((PropertyInfo)member).GetValue(null);
        Type type = member is FieldInfo fieldInfo ? fieldInfo.FieldType : ((PropertyInfo)member).PropertyType;
        _old.Add((member, old));
        Write(member, UiDragTransportProxy.Make(type, handler));
    }
    static void Write(MemberInfo member, object value)
    {
        if (member is FieldInfo field) field.SetValue(null, value);
        else ((PropertyInfo)member).SetValue(null, value);
    }
    public void Dispose()
    {
        for (int i = _old.Count - 1; i >= 0; i--) Write(_old[i].Member, _old[i].Old);
    }
}
