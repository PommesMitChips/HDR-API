using System;
using System.Collections.Generic;
using HoloMap;
using VRage;

internal static class LocalLeaseTests
{
    sealed class Fixture
    {
        public readonly List<UiDragRequest> Sent = new List<UiDragRequest>();
        public readonly UiDragLocalLease Lease;
        public int Releases;
        public Fixture()
        {
            Lease = new UiDragLocalLease(p => Sent.Add(Copy(p)), () => Releases++);
        }
        public bool Begin(int tick = 0) { return Lease.Begin(Request(), tick); }
        public void Grant(int tick = 0)
        {
            Lease.Receive(Ack(Sent[0]), tick);
        }
        public bool Arm(long frame = 1, int tick = 0)
        {
            return Lease.Pointer(Sample(frame, 18, 1, 1), tick);
        }
    }

    static UiDragRequest Request()
    {
        return new UiDragRequest { Kind = (int)UiDragKind.Begin, CallerId = 11, TargetId = 22,
            ControlId = "slider", DefinitionRevision = 3, ValueRevision = 5, RequestId = 7,
            Sequence = 1, Mode = (int)UiDragMode.FocusedPointer, Value = 0.25 };
    }
    static UiDragRequest Copy(UiDragRequest p)
    {
        return new UiDragRequest { Protocol = p.Protocol, Kind = p.Kind, CallerId = p.CallerId,
            TargetId = p.TargetId, ControlId = p.ControlId, DefinitionRevision = p.DefinitionRevision,
            ValueRevision = p.ValueRevision, RequestId = p.RequestId, Sequence = p.Sequence,
            LeaseId = p.LeaseId, Mode = p.Mode, Value = p.Value };
    }
    static UiDragAck Ack(UiDragRequest p, bool terminal = false, UiDragStatus status = UiDragStatus.Accepted)
    {
        return new UiDragAck { Kind = p.Kind, CallerId = p.CallerId, TargetId = p.TargetId,
            ControlId = p.ControlId, DefinitionRevision = p.DefinitionRevision,
            ValueRevision = p.ValueRevision, RequestId = p.RequestId, Sequence = p.Sequence,
            LeaseId = p.LeaseId == 0 ? 91 : p.LeaseId, Status = (int)status, Value = p.Value,
            TileId = 33, CharacterId = 44, Terminal = terminal };
    }
    static UiPointerSample Sample(long frame, int flags, int pressed = 0, int held = 0, int released = 0)
    {
        return new UiPointerSample { Frame = frame, Flags = flags, Pressed = pressed, Held = held,
            Released = released, X = 0.4, Y = 0.6 };
    }
    static MyTuple<string, int, Func<string, object[], object>> Announcement(
        Func<string, object[], object> endpoint, int status = 1, string version = PointerInputBridge.Version)
    {
        return new MyTuple<string, int, Func<string, object[], object>>(version, status, endpoint);
    }
    static object NativeSample(long frame = 10, int flags = 18, double x = 0.4, double y = 0.6,
        int pressed = 1, int held = 3, int released = 4)
    {
        return new MyTuple<long, int, MyTuple<double, double>, MyTuple<int, int, int>>(
            frame, flags, new MyTuple<double, double>(x, y), new MyTuple<int, int, int>(pressed, held, released));
    }

    public static void Run(Action<bool, string> check)
    {
        Barriers(check);
        Acknowledgements(check);
        Prediction(check);
        Cancellation(check);
        LatentPress(check);
        TimingAndCallbacks(check);
        SequenceAllocator(check);
        BridgeAbi(check);
        BridgeLifecycle(check);
    }

    static void Barriers(Action<bool, string> check)
    {
        var f = new Fixture();
        check(!f.Lease.Begin(null, 0), "local rejects null begin");
        var invalid = Request(); invalid.Kind = (int)UiDragKind.Move;
        check(!f.Lease.Begin(invalid, 0), "local rejects non-begin identity");
        var begin = Request();
        check(f.Lease.Begin(begin, 0), "local begins valid request");
        check(f.Lease.Pending && !f.Lease.Active && f.Lease.LeaseId == 0,
            "begin is pending until both ownership barriers hold");
        check(f.Lease.RequestId == 7 && f.Lease.Prediction == 0.25 && f.Lease.Confirmed == 0.25,
            "begin captures identity and initial canonical value");
        check(!f.Lease.Begin(Request(), 0) && f.Sent.Count == 1, "pending gesture rejects a second begin");
        begin.RequestId = 99; begin.ControlId = "changed"; begin.TargetId = 88;
        check(!f.Lease.Pointer(Sample(1, 1, 1, 1), 0) && !f.Lease.Held,
            "native acquired status is only pending and cannot latch physical down");
        check(!f.Lease.Pointer(Sample(2, 2, 1, 1), 0) && !f.Lease.Held,
            "native applied without pointer-valid cannot latch physical down");
        check(!f.Lease.Pointer(Sample(3, 16, 1, 1), 0) && !f.Lease.Held,
            "native pointer-valid without applied cannot latch physical down");
        check(!f.Lease.Pointer(Sample(4, 18, 1, 1), 0) && f.Lease.Held && !f.Lease.Active
            && !f.Lease.Predict(0.7, 6) && f.Sent.Count == 1,
            "applied physical down before grant is latent without prediction or move");
        f.Grant();
        check(f.Lease.Active && f.Lease.Held && f.Lease.RequestId == 7,
            "grant uses immutable begin identity and enables latent applied gesture");
        check(f.Lease.Pointer(Sample(5, 18, 0, 1), 1) && f.Lease.Predict(0.7, 1) && f.Sent.Count == 1,
            "held-only sample after grant continues latent physical down");
        check(f.Lease.Pointer(Sample(6, 18, 1, 1), 2), "fresh applied edge remains accepted during granted drag");
        check(!f.Lease.Pointer(Sample(6, 18, 0, 0, 1), 2) && f.Sent.Count == 1,
            "duplicate pointer frame cannot end the gesture");
        check(f.Lease.Predict(0.7, 2) && f.Sent.Count == 1,
            "prediction updates immediately before move send interval");
        check(!f.Lease.Pointer(Sample(7, 1), 3) && !f.Lease.Active,
            "inactive native sample closes pointer barrier");
        check(!f.Lease.Pointer(Sample(6, 18, 0, 1), 3) && !f.Lease.Active,
            "stale active frame cannot reopen pointer barrier");

        var g = new Fixture(); g.Begin(); g.Grant();
        check(!g.Lease.Active, "server grant alone does not activate pointer prediction");
        check(g.Arm(), "grant then applied pointer with fresh edge activates prediction");
        g.Lease.Pointer(Sample(2, 18, 0, 0), 1);
        check(!g.Lease.Active && g.Sent.Count == 2 && g.Sent[1].Kind == (int)UiDragKind.End,
            "loss of held button sends terminal End");

        foreach (int flag in new[] { 4, 32, 64 })
        {
            var revoked = new Fixture(); revoked.Begin(); revoked.Grant(); revoked.Arm();
            check(!revoked.Lease.Pointer(Sample(2, 18 | flag, 1, 1), 1) && !revoked.Lease.Active
                && revoked.Sent.Count == 2 && revoked.Sent[1].Kind == (int)UiDragKind.Cancel
                && revoked.Sent[1].LeaseId == 91 && revoked.Releases > 0,
                "native revocation flag " + flag + " cancels granted lease");
        }
    }

    static void Acknowledgements(Action<bool, string> check)
    {
        var f = new Fixture(); f.Begin();
        Action<UiDragAck>[] corrupt = {
            a => a.Protocol = 2, a => a.CallerId++, a => a.TargetId++, a => a.ControlId = "other",
            a => a.DefinitionRevision++, a => a.RequestId++, a => a.Sequence = 2,
            a => a.LeaseId = 0, a => a.ValueRevision = 0, a => a.Value = double.NaN
        };
        f.Lease.Receive(null, 0);
        foreach (var change in corrupt)
        {
            var wrong = Ack(f.Sent[0]); change(wrong); f.Lease.Receive(wrong, 0);
            check(f.Lease.Pending && f.Lease.LeaseId == 0 && !f.Lease.Active,
                "wrong or malformed acknowledgement cannot grant ownership");
        }
        f.Grant(); f.Arm();
        var duplicate = Ack(f.Sent[0]); duplicate.Value = 0.9;
        f.Lease.Receive(duplicate, 1);
        check(f.Lease.Confirmed == 0.25 && f.Lease.Prediction == 0.25,
            "duplicate accepted acknowledgement cannot rewrite canonical value");
        f.Lease.Predict(0.4, 6);
        var committed = Ack(f.Sent[1]); committed.ValueRevision = 6;
        f.Lease.Receive(committed, 6);
        var oldTerminal = Ack(f.Sent[0], true, UiDragStatus.Preempted);
        f.Lease.Receive(oldTerminal, 7);
        check(f.Lease.Active && f.Lease.Confirmed == 0.4 && f.Lease.ValueRevision == 6,
            "lower sequence terminal acknowledgement cannot revoke newer state");
        f.Lease.Predict(0.6, 12);
        var regressed = Ack(f.Sent[2]); regressed.ValueRevision = 5;
        f.Lease.Receive(regressed, 12);
        check(f.Lease.Active && f.Lease.Confirmed == 0.4 && f.Lease.ValueRevision == 6,
            "regressed value revision is ignored");
        var foreignLease = Ack(f.Sent[2], true, UiDragStatus.Cancelled); foreignLease.LeaseId = 92;
        f.Lease.Receive(foreignLease, 12);
        check(f.Lease.Active, "terminal acknowledgement for another lease cannot release this pointer");
        var equalSequenceRevoke = Ack(f.Sent[1], true, UiDragStatus.Preempted);
        equalSequenceRevoke.ValueRevision = 6;
        f.Lease.Receive(equalSequenceRevoke, 13);
        check(!f.Lease.Pending && !f.Lease.Active && f.Lease.LeaseId == 0 && f.Releases > 0,
            "equal acknowledged sequence server revocation still clears ownership");
        int sent = f.Sent.Count; f.Lease.Receive(Ack(f.Sent[0]), 14);
        check(!f.Lease.Pending && f.Sent.Count == sent, "acknowledgement after clear cannot resurrect gesture");
    }

    static void Prediction(Action<bool, string> check)
    {
        var f = new Fixture(); f.Begin(); f.Grant(); f.Arm();
        check(!f.Lease.Predict(double.NaN, 6) && !f.Lease.Predict(double.PositiveInfinity, 6)
            && !f.Lease.Predict(1e12 + 1, 6) && f.Sent.Count == 1,
            "nonfinite or unbounded prediction cannot send move");
        check(f.Lease.Predict(0.4, 6) && f.Sent.Count == 2 && f.Sent[1].Kind == (int)UiDragKind.Move
            && f.Sent[1].Sequence == 2 && f.Sent[1].LeaseId == 91 && f.Sent[1].ValueRevision == 5,
            "move interval sends absolute prediction with granted identity");
        f.Lease.Predict(0.8, 7);
        var ack = Ack(f.Sent[1]); ack.Value = 0.35; ack.ValueRevision = 6;
        f.Lease.Receive(ack, 7);
        check(f.Lease.Confirmed == 0.35 && f.Lease.ValueRevision == 6 && f.Lease.Prediction == 0.8,
            "acknowledgement of newest sent move preserves newer unsent prediction");
        f.Lease.Predict(0.9, 12);
        var current = Ack(f.Sent[2]); current.Value = 0.85; current.ValueRevision = 7;
        f.Lease.Receive(current, 12);
        check(f.Lease.Confirmed == 0.85 && f.Lease.Prediction == 0.85 && f.Lease.ValueRevision == 7,
            "acknowledgement covering current prediction reconciles canonical result");
        f.Lease.Predict(0.95, 13);
        f.Lease.End(13);
        check(f.Sent.Count == 4 && f.Sent[3].Kind == (int)UiDragKind.End && f.Sent[3].Value == 0.95
            && f.Sent[3].Sequence == 4 && f.Sent[3].ValueRevision == 7 && !f.Lease.Active,
            "End sends latest unsent prediction immediately");
        f.Lease.Receive(Ack(f.Sent[3], true), 14);
        check(!f.Lease.Pending && !f.Lease.Active, "terminal End acknowledgement clears prediction ownership");
    }

    static void Cancellation(Action<bool, string> check)
    {
        var f = new Fixture(); f.Begin();
        f.Lease.Cancel(1);
        check(f.Lease.Pending && !f.Lease.Active && f.Sent.Count == 2
            && f.Sent[1].Kind == (int)UiDragKind.Cancel && f.Sent[1].LeaseId == 0,
            "cancel before grant sends pending request cancellation");
        int sent = f.Sent.Count; f.Lease.Cancel(2); f.Lease.End(2);
        check(f.Sent.Count == sent, "repeated cancellation or End sends no duplicate terminal request");
        f.Lease.Receive(Ack(f.Sent[0]), 3);
        check(f.Sent.Count == 3 && f.Sent[2].Kind == (int)UiDragKind.Cancel
            && f.Sent[2].LeaseId == 91 && f.Sent[2].Sequence == 3 && !f.Lease.Active,
            "delayed begin grant after Cancel resends cancellation with granted lease");
        f.Lease.Receive(Ack(f.Sent[2], true, UiDragStatus.Cancelled), 4);
        check(!f.Lease.Pending && !f.Lease.Active, "cancel confirmation clears pending gesture");

        var releaseBeforeGrant = new Fixture(); releaseBeforeGrant.Begin();
        releaseBeforeGrant.Lease.End(1);
        check(releaseBeforeGrant.Sent.Count == 2 && releaseBeforeGrant.Sent[1].Kind == (int)UiDragKind.Cancel
            && releaseBeforeGrant.Sent[1].LeaseId == 0,
            "End without both barriers cancels pending begin");

        var synchronous = new List<UiDragRequest>();
        UiDragLocalLease lease = null;
        lease = new UiDragLocalLease(p => {
            synchronous.Add(Copy(p));
            if (p.Kind == (int)UiDragKind.Cancel && p.LeaseId == 0)
                lease.Receive(Ack(synchronous[0]), 1);
        }, () => { });
        lease.Begin(Request(), 0); lease.Cancel(1);
        check(synchronous.Count == 3 && synchronous[2].Kind == (int)UiDragKind.Cancel
            && synchronous[2].LeaseId == 91 && lease.Pending && !lease.Active,
            "synchronous delayed grant during Cancel cannot leak server lease");
    }

    static void LatentPress(Action<bool, string> check)
    {
        var notApplied = new Fixture(); notApplied.Begin();
        check(!notApplied.Lease.Pointer(Sample(1, 1, 1, 1), 0) && !notApplied.Lease.Held
            && !notApplied.Lease.Predict(0.8, 6) && notApplied.Sent.Count == 1,
            "physical edge in non-applied pending sample cannot latch or send");
        notApplied.Grant(1);
        check(!notApplied.Lease.Pointer(Sample(2, 18, 0, 1), 1) && !notApplied.Lease.Held
            && !notApplied.Lease.Predict(0.8, 6) && notApplied.Sent.Count == 1,
            "grant cannot reuse physical edge from non-applied sample");
        check(notApplied.Lease.Pointer(Sample(3, 18, 1, 1), 2) && notApplied.Lease.Held,
            "non-applied pending edge requires a later routed physical down");

        var released = new Fixture(); released.Begin();
        check(!released.Lease.Pointer(Sample(1, 18, 1, 1), 0) && released.Lease.Held
            && !released.Lease.Active && !released.Lease.Predict(0.9, 6) && released.Sent.Count == 1,
            "pregrant applied down latches only latent held state");
        check(!released.Lease.Pointer(Sample(2, 18, 0, 0, 1), 1) && !released.Lease.Held
            && released.Lease.Pending && !released.Lease.Active && released.Sent.Count == 2
            && released.Sent[1].Kind == (int)UiDragKind.Cancel && released.Sent[1].LeaseId == 0
            && released.Releases > 0,
            "physical release before grant cancels pending begin and releases native pointer");
        released.Grant(2);
        check(released.Sent.Count == 3 && released.Sent[2].Kind == (int)UiDragKind.Cancel
            && released.Sent[2].LeaseId == 91 && released.Sent[2].Sequence == 3
            && !released.Lease.Active && !released.Lease.Held,
            "grant after latent gesture release cancels granted lease without revival");
        check(!released.Lease.Pointer(Sample(3, 18, 1, 1), 3) && !released.Lease.Held
            && !released.Lease.Predict(0.9, 6) && released.Sent.Count == 3,
            "late physical down cannot revive released pregrant gesture");
        released.Lease.Receive(Ack(released.Sent[2], true, UiDragStatus.Cancelled), 4);
        check(!released.Lease.Pending && !released.Lease.Active && !released.Lease.Held,
            "latent gesture cancellation confirmation clears all local ownership");
    }

    static void TimingAndCallbacks(Action<bool, string> check)
    {
        var pending = new Fixture(); pending.Begin();
        pending.Lease.Tick(180, true);
        check(pending.Lease.Pending && pending.Sent.Count == 1, "pending timeout is inclusive at configured boundary");
        pending.Lease.Tick(181, true);
        check(!pending.Lease.Active && pending.Sent.Count == 2
            && pending.Sent[1].Kind == (int)UiDragKind.Cancel,
            "missing begin acknowledgement cancels at timeout boundary plus one");
        pending.Lease.Tick(182, true);
        check(!pending.Lease.Pending, "unacknowledged terminal eventually clears local state");

        var active = new Fixture(); active.Begin(); active.Grant(2); active.Arm(1, 2);
        active.Lease.Tick(182, true);
        check(active.Lease.Active && active.Sent.Count == 1, "active acknowledgement timeout uses last ack tick");
        active.Lease.Tick(183, true);
        check(!active.Lease.Active && active.Sent.Count == 2 && active.Sent[1].LeaseId == 91,
            "active acknowledgement timeout releases native ownership and cancels server lease");
        var lost = new Fixture(); lost.Begin(); lost.Grant(); lost.Arm(); lost.Lease.Tick(1, false);
        check(!lost.Lease.Active && lost.Sent.Count == 2 && lost.Sent[1].Kind == (int)UiDragKind.Cancel,
            "lost interaction context immediately cancels granted lease");

        int releases = 0;
        var sendFailure = new UiDragLocalLease(p => { throw new InvalidOperationException("send failure"); }, () => releases++);
        sendFailure.Begin(Request(), 0);
        check(!sendFailure.Pending && !sendFailure.Active && releases > 0,
            "throwing begin send callback clears local ownership without escaping");
        var releaseFailure = new UiDragLocalLease(p => { }, () => { throw new InvalidOperationException("release failure"); });
        releaseFailure.Begin(Request(), 0); releaseFailure.Cancel(1); releaseFailure.Clear();
        check(!releaseFailure.Pending && !releaseFailure.Active,
            "throwing release callback does not prevent clear");
        var noCallbacks = new UiDragLocalLease(null, null);
        noCallbacks.Begin(Request(), 0); noCallbacks.Cancel(1); noCallbacks.Clear();
        check(!noCallbacks.Pending && !noCallbacks.Active, "missing callbacks leave clear usable");
    }

    static void SequenceAllocator(Action<bool, string> check)
    {
        var sent = new List<UiDragRequest>();
        var allocated = new Queue<long>(new[] { 41L, 57L });
        int calls = 0, releases = 0;
        var lease = new UiDragLocalLease(p => sent.Add(Copy(p)), () => releases++, () => {
            calls++; return allocated.Dequeue();
        });
        check(lease.Begin(Request(), 0) && calls == 0,
            "begin preserves supplied shared sequence without allocating again");
        lease.Receive(Ack(sent[0]), 0); lease.Pointer(Sample(1, 18, 1, 1), 0);
        lease.Predict(0.5, 6); lease.End(7);
        check(sent.Count == 3 && calls == 2 && sent[1].Sequence == 41 && sent[2].Sequence == 57
            && sent[1].Kind == (int)UiDragKind.Move && sent[2].Kind == (int)UiDragKind.End,
            "move and terminal use exact monotonic shared allocator values including gaps");
        check(sent[1].RequestId == 7 && sent[2].RequestId == 7 && sent[1].CallerId == 11
            && sent[2].TargetId == 22 && sent[1].ControlId == "slider" && sent[2].DefinitionRevision == 3
            && sent[1].LeaseId == 91 && sent[2].LeaseId == 91 && sent[2].ValueRevision == 5,
            "shared sequence allocation preserves granted request identity");

        Func<long>[] invalid = {
            () => -1, () => 0, () => 1,
            () => { throw new InvalidOperationException("allocator failure"); }
        };
        foreach (var allocate in invalid)
        {
            var packets = new List<UiDragRequest>();
            int nativeReleases = 0;
            var failed = new UiDragLocalLease(p => packets.Add(Copy(p)), () => nativeReleases++, allocate);
            failed.Begin(Request(), 0); failed.Receive(Ack(packets[0]), 0);
            failed.Pointer(Sample(1, 18, 1, 1), 0); failed.Predict(0.5, 6);
            check(packets.Count == 1 && !failed.Pending && !failed.Active && !failed.Held
                && failed.LeaseId == 0 && nativeReleases > 0,
                "failed or nonmonotonic shared sequence clears ownership without emitting packet");
        }

        var regressionPackets = new List<UiDragRequest>();
        var regressionSequence = new Queue<long>(new[] { 41L, 40L });
        int regressionReleases = 0;
        var regressed = new UiDragLocalLease(p => regressionPackets.Add(Copy(p)),
            () => regressionReleases++, () => regressionSequence.Dequeue());
        regressed.Begin(Request(), 0); regressed.Receive(Ack(regressionPackets[0]), 0);
        regressed.Pointer(Sample(1, 18, 1, 1), 0); regressed.Predict(0.5, 6); regressed.Cancel(7);
        check(regressionPackets.Count == 2 && regressionPackets[1].Sequence == 41
            && !regressed.Pending && !regressed.Active && !regressed.Held && regressionReleases > 0,
            "allocator regression after valid move cannot send replayed terminal sequence");

        var overflowPackets = new List<UiDragRequest>();
        int overflowReleases = 0;
        var overflow = new UiDragLocalLease(p => overflowPackets.Add(Copy(p)), () => overflowReleases++);
        var lastSequence = Request(); lastSequence.Sequence = long.MaxValue;
        overflow.Begin(lastSequence, 0); overflow.Receive(Ack(overflowPackets[0]), 0);
        overflow.Pointer(Sample(1, 18, 1, 1), 0); overflow.Predict(0.5, 6);
        check(overflowPackets.Count == 1 && !overflow.Pending && !overflow.Active && !overflow.Held
            && overflowReleases > 0,
            "default sequence overflow releases pointer without wrapping or sending");

        var releaseFailurePackets = new List<UiDragRequest>();
        int releaseCalls = 0;
        var releaseFailure = new UiDragLocalLease(p => releaseFailurePackets.Add(Copy(p)), () => {
            releaseCalls++; throw new InvalidOperationException("release failure");
        }, () => 0);
        releaseFailure.Begin(Request(), 0); releaseFailure.Receive(Ack(releaseFailurePackets[0]), 0);
        releaseFailure.Pointer(Sample(1, 18, 1, 1), 0); releaseFailure.Predict(0.5, 6);
        check(releaseFailurePackets.Count == 1 && !releaseFailure.Pending && !releaseFailure.Active
            && !releaseFailure.Held && releaseCalls > 0,
            "throwing native release cannot retain ownership after allocator failure");
    }

    static void BridgeAbi(Action<bool, string> check)
    {
        var bridge = new PointerInputBridge();
        object result = new MyTuple<long, int>(71, 1);
        int acquires = 0;
        Func<string, object[], object> endpoint = (operation, args) => {
            if (operation == "acquire") { acquires++; return result; }
            if (operation == "sample") return result;
            return null;
        };
        bridge.Receive(null); bridge.Receive(new object());
        bridge.Receive(Announcement(endpoint, 1, "HDR.Pointer/2"));
        bridge.Receive(Announcement(endpoint, 2));
        check(!bridge.Ready && !bridge.Acquire(11, "lease"), "bridge ignores wrong registration shape, version, and status");
        bridge.Receive(Announcement(endpoint));
        check(bridge.Ready && !bridge.Acquire(0, "lease") && !bridge.Acquire(11, null)
            && !bridge.Acquire(11, "") && !bridge.Acquire(11, new string('x', 193)) && acquires == 0,
            "bridge validates owner and key before native acquisition");
        check(bridge.Acquire(11, "lease") && bridge.Token == 71,
            "bridge acquisition accepts native pending token");
        check(!bridge.Acquire(11, "another") && acquires == 1, "bridge cannot acquire second token while owned");
        UiPointerSample sample;
        result = NativeSample(flags: 1);
        check(bridge.Sample(out sample) && !sample.Active, "acquired sample stays pending until native routing applies");
        result = NativeSample();
        check(bridge.Sample(out sample) && sample.Active && sample.Frame == 10 && sample.Flags == 18
            && sample.X == 0.4 && sample.Y == 0.6 && sample.Pressed == 1 && sample.Held == 3 && sample.Released == 4,
            "bridge decodes exact nested four-item MyTuple sample ABI");
        result = Tuple.Create(10L, 18, Tuple.Create(0.4, 0.6), Tuple.Create(1, 3, 4));
        check(!bridge.Sample(out sample), "bridge rejects System.Tuple lookalike sample ABI");
        result = new MyTuple<long, int, double, double>(10, 18, 0.4, 0.6);
        check(!bridge.Sample(out sample), "bridge rejects flattened four-item tuple ABI");
        foreach (var bad in new[] {
            NativeSample(frame: -1), NativeSample(x: double.NaN), NativeSample(y: double.PositiveInfinity),
            NativeSample(x: -0.01), NativeSample(y: 1.01), NativeSample(pressed: 32),
            NativeSample(held: -1), NativeSample(released: 32)
        })
        {
            result = bad;
            check(!bridge.Sample(out sample), "bridge rejects invalid frame, coordinate, or button mask");
        }
        result = NativeSample(frame: 0, x: 0, y: 1, pressed: 31, held: 31, released: 31);
        check(bridge.Sample(out sample), "bridge accepts frame zero and inclusive coordinate/button bounds");
        foreach (int flag in new[] { 4, 32, 64 })
        {
            result = NativeSample(flags: 18 | flag);
            check(bridge.Sample(out sample) && !sample.Active,
                "bridge preserves native revocation flag " + flag + " for local cancellation");
        }
        bridge.Release();
        foreach (var badAcquire in new object[] {
            null, Tuple.Create(71L, 1), new MyTuple<long, int>(0, 1), new MyTuple<long, int>(-1, 1),
            new MyTuple<long, int>(71, 4), new MyTuple<long, int>(71, 32), new MyTuple<long, int>(71, 64)
        })
        {
            result = badAcquire;
            check(!bridge.Acquire(11, "lease") && bridge.Token == 0,
                "bridge rejects invalid acquisition shape, token, or revocation");
        }
        bridge.Close();
        check(!bridge.Ready && !bridge.Sample(out sample), "closed bridge cannot sample native pointer");
    }

    static void BridgeLifecycle(Action<bool, string> check)
    {
        var bridge = new PointerInputBridge();
        int oldReleases = 0, newReleases = 0;
        bool releaseSawZero = false;
        Func<string, object[], object> oldEndpoint = (operation, args) => {
            if (operation == "acquire") return new MyTuple<long, int>(71, 1);
            if (operation == "sample") return NativeSample();
            if (operation == "release") { oldReleases++; releaseSawZero = bridge.Token == 0; }
            return null;
        };
        Func<string, object[], object> newEndpoint = (operation, args) => {
            if (operation == "acquire") return new MyTuple<long, int>(72, 1);
            if (operation == "release") newReleases++;
            return null;
        };
        bridge.Receive(Announcement(oldEndpoint)); bridge.Acquire(11, "lease");
        bridge.Receive(Announcement(newEndpoint, 0));
        check(bridge.Ready && bridge.Token == 71 && oldReleases == 0,
            "withdrawal from another provider cannot revoke current native token");
        bridge.Receive(Announcement(oldEndpoint));
        check(bridge.Token == 71 && oldReleases == 0, "repeated current provider registration preserves token");
        bridge.Receive(Announcement(newEndpoint));
        check(bridge.Ready && bridge.Token == 0 && oldReleases == 1 && releaseSawZero,
            "provider replacement releases old token after clearing local token");
        bridge.Acquire(11, "lease"); bridge.Receive(Announcement(newEndpoint, 0));
        check(!bridge.Ready && bridge.Token == 0 && newReleases == 1,
            "matching provider withdrawal releases token and discovery state");
        bridge.Release(); bridge.Close();
        check(newReleases == 1 && oldReleases == 1, "release and close are idempotent after token removal");

        var failures = new PointerInputBridge();
        Func<string, object[], object> throwing = (operation, args) => {
            throw new InvalidOperationException("native failure");
        };
        failures.Receive(Announcement(throwing));
        check(!failures.Acquire(11, "lease") && failures.Token == 0,
            "throwing acquisition callback cannot retain native token");
        Func<string, object[], object> throwingSampleAndRelease = (operation, args) => {
            if (operation == "acquire") return new MyTuple<long, int>(73, 1);
            throw new InvalidOperationException("native failure");
        };
        failures.Receive(Announcement(throwingSampleAndRelease)); failures.Acquire(11, "lease");
        UiPointerSample sample;
        check(!failures.Sample(out sample), "throwing sample callback is isolated");
        failures.Close();
        check(!failures.Ready && failures.Token == 0, "throwing native release cannot prevent bridge close");
    }
}
