using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HoloMap;
using Sandbox.ModAPI;

// Production chat commands, client cleanup, secure transport and authority run
// against the installed game ABI. The existing native-input fixture supplies
// gateway and exact-tuple provider doubles; this does not prove live GUI routing.
internal static class UiDragLifecycleIntegrationTests
{
    static int checks;
    static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static void Check(bool good, string label)
    { if (!good) throw new Exception("UI drag production lifecycle: " + label); checks++; }
    static object Call(object target, string name, params object[] arguments)
    {
        var method = target.GetType().GetMethods(Instance)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == arguments.Length);
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException error) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    static object Field(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    static T Property<T>(object target, string name) => (T)target.GetType().GetProperty(name, Instance).GetValue(target);
    static bool Command(HoloMapSession session, string text)
    {
        object[] arguments = { text, true };
        Call(session, "ClientMessageEntered", arguments);
        return (bool)arguments[1];
    }
    static void CaptureMessages(ClientReplicationTests.GatewayScope gateway, List<string> messages)
    {
        object previous = MyAPIGateway.Utilities;
        gateway.Install("Utilities", (method, arguments) =>
        {
            if (method.Name == "ShowMessage") { messages.Add((string)arguments[1]); return null; }
            if (method.Name == "get_IsDedicated") return false;
            if (method.Name == "RegisterMessageHandler" || method.Name == "UnregisterMessageHandler" || method.Name == "SendModMessage") return null;
            return method.Invoke(previous, arguments);
        });
    }

    // Reuse the established game-interface doubles without widening their
    // visibility or duplicating their GUI/camera/connected-player assumptions.
    sealed class Fixture : IDisposable
    {
        readonly object inner;
        readonly object provider;
        public readonly HoloMapSession Session;
        public readonly List<string> Messages = new();
        public Fixture(bool viewer)
        {
            var type = typeof(NativeUiDragInputTests).GetNestedType("Fixture", BindingFlags.NonPublic);
            inner = Activator.CreateInstance(type, Instance, null, new object[] { viewer, false, false }, CultureInfo.InvariantCulture);
            Session = (HoloMapSession)Field(inner, "Session");
            provider = Field(inner, "Provider");
            CaptureMessages((ClientReplicationTests.GatewayScope)Field(inner, "Gateway"), Messages);
        }
        public PointerInputBridge Bridge => Property<PointerInputBridge>(inner, "Bridge");
        public UiDragLocalLease Local => Property<UiDragLocalLease>(inner, "Local");
        public UiNumericValue Value => Property<UiNumericValue>(inner, "Value");
        public UiDragTransactions Transactions => (UiDragTransactions)Field(Session, "_uiDrags");
        public UiDragRequest[] Requests => Property<UiDragRequest[]>(inner, "Requests");
        public UiDragAck[] Acknowledgements => Property<UiDragAck[]>(inner, "Acknowledgements");
        public List<byte[]> ClientPackets => (List<byte[]>)Field(inner, "ClientPackets");
        public int Acquires => (int)Field(provider, "Acquires");
        public int Releases => (int)Field(provider, "Releases");
        public int Samples => ((ICollection)Field(provider, "Samples")).Count;
        public long[] ReleasedTokens => ((IEnumerable)Field(provider, "ReleasedTokens")).Cast<long>().ToArray();
        public bool Granted => (bool)Field(Session, "_uiViewerGranted");
        public int Viewers => ((IDictionary)Field(Session, "_uiDragViewers")).Count;
        public int ValueLeases => ((IDictionary)Field(Session, "_uiValueLeases")).Count;
        public int ValueLocks => ((IDictionary)Field(Session, "_uiValueLocks")).Count;
        public int QueuedAcks => ((ICollection)Field(Session, "_uiDragAcks")).Count;
        public int QueuedTerminals => ((IDictionary)Field(Session, "_uiDragTerminalAcks")).Count;
        public bool Use() => (bool)Call(inner, "Use", "slider");
        public void Down() => Call(provider, "Down");
        public void Up() => Call(provider, "Up");
        public void Move(double value) => Call(provider, "Move", value);
        public void Tick(int tick) => Call(inner, "Tick", tick);
        public void RoundTrip(int tick) => Call(inner, "RoundTrip", tick);
        public void Inject(UiDragAck acknowledgement) => Call(inner, "InjectServerAcknowledgement", acknowledgement);
        public void ForwardClient()
        {
            Set(inner, "AsClient", false);
            int forwarded = (int)Field(inner, "forwardedClient");
            while (forwarded < ClientPackets.Count)
                Call(Session, "ReceiveUiMessage", (ushort)49784, ClientPackets[forwarded++], 200UL, false);
            Set(inner, "forwardedClient", forwarded);
        }
        public void AdvanceServer(int tick)
        {
            ForwardClient(); Set(Session, "_ticks", tick); Call(Session, "TickUiNetwork");
            Set(inner, "AsClient", true);
        }
        public void Dispose() => ((IDisposable)inner).Dispose();
    }

    public static int Run()
    {
        checks = 0;
        StatusDoesNotAcquireOrApplyInput();
        RatesValidateAndSurviveLazyInitialization();
        HostAndRemoteConfigurationRemainIndependent();
        RenderingOffCancelsActiveViewerAndQueuedMove();
        RenderingOffBeforeGrantDelivery();
        return checks;
    }

    static void StatusDoesNotAcquireOrApplyInput()
    {
        using var fixture = new Fixture(viewer: true);
        Check(fixture.Bridge.Ready && fixture.Bridge.Token == 0 && !fixture.Local.Pending,
            "registered exact-tuple provider starts without a native or numeric lease");
        Check(!Command(fixture.Session, "/hdr status") && fixture.Messages.Any(message => message.Contains("10 Hz; pointer provider registered; viewer inactive")),
            "status is local and describes provider registration separately from viewer focus");
        Check(fixture.Acquires == 0 && fixture.Samples == 0 && fixture.Releases == 0 && fixture.ClientPackets.Count == 0
            && fixture.Bridge.Token == 0 && !fixture.Local.Pending,
            "status cannot acquire, sample or send an interaction for a merely registered provider");
        Check(fixture.Use(), "pending-input status fixture enters native persistent Focus");
        int samples = fixture.Samples, packets = fixture.ClientPackets.Count;
        fixture.Messages.Clear(); Command(fixture.Session, "/hdr status");
        Check(fixture.Messages.Any(message => message.Contains("pointer provider registered; viewer inactive"))
            && fixture.Acquires == 1 && fixture.Samples == samples && fixture.ClientPackets.Count == packets
            && !fixture.Local.Active && !fixture.Local.Held,
            "status preserves a pending native acquisition without treating registration as applied input");
        fixture.RoundTrip(0); fixture.Messages.Clear(); Command(fixture.Session, "/hdr status");
        Check(fixture.Granted && fixture.Messages.Any(message => message.Contains("pointer provider registered; viewer focused"))
            && fixture.Samples == samples && fixture.Acquires == 1 && !fixture.Local.Active && !fixture.Local.Predict(9, 1),
            "server Focus plus provider readiness still cannot apply or predict a numeric gesture through status");
    }

    static void RatesValidateAndSurviveLazyInitialization()
    {
        using var gateway = new ClientReplicationTests.GatewayScope();
        var messages = new List<string>(); CaptureMessages(gateway, messages);
        var session = new HoloMapSession();
        foreach (double rate in new[] { double.NaN, 0d, 31d })
        {
            bool rejected = false;
            try { Call(session, "SetUiDragRate", rate); } catch (ArgumentException) { rejected = true; }
            Check(rejected && (int)Field(session, "_uiDragInterval") == 6
                && Field(session, "_uiDrags") == null && Field(session, "_uiDragLocal") == null,
                "invalid direct rate " + rate + " is rejected before allocating or changing either lease policy");
        }
        foreach (string command in new[] { "/hdr drag-rate NaN", "/hdr drag-rate 0", "/hdr drag-rate 31" })
        {
            messages.Clear();
            Check(!Command(session, command) && messages.Single().Contains("Use /hdr drag-rate 1–30")
                && (int)Field(session, "_uiDragInterval") == 6 && Field(session, "_uiDrags") == null,
                "invalid chat rate is consumed locally and retains the previous policy");
        }
        double[] rates = { 1, 13, 30 }; int[] intervals = { 60, 5, 2 }; string[] effective = { "1", "12", "30" };
        for (int i = 0; i < rates.Length; i++)
        {
            messages.Clear();
            Check(!Command(session, "/hdr drag-rate " + rates[i].ToString(CultureInfo.InvariantCulture))
                && messages.Single().Contains("UI drag update cap: " + effective[i] + " Hz")
                && (int)Field(session, "_uiDragInterval") == intervals[i]
                && ((UiDragTransactions)Field(session, "_uiDrags")).MoveIntervalTicks == intervals[i]
                && Field(session, "_uiDragLocal") == null,
                "configured rate installs its conservative tick interval before local pointer initialization");
            Call(session, "InitializeUiPointer");
            Check(((UiDragLocalLease)Field(session, "_uiDragLocal")).MoveIntervalTicks == intervals[i],
                "lazy local lease inherits the configured " + rates[i] + " Hz interval");
            messages.Clear(); Command(session, "/hdr status");
            Check(messages.Any(message => message.Contains(effective[i] + " Hz; pointer provider unavailable; viewer inactive")),
                "status reports the effective cap without a provider or numeric lease");
            Call(session, "ClearUiDragNetwork"); Call(session, "InitializeUiDragNetwork");
            Check(((UiDragTransactions)Field(session, "_uiDrags")).MoveIntervalTicks == intervals[i],
                "recreated authority retains the configured interval");
            Call(session, "UnregisterUiPointer");
        }
        Check(gateway.Requests == 0 && gateway.Sent.Count == 0,
            "local rate commands and lazy initialization transmit no rate-setting packet");
        Call(session, "ClearUiDragNetwork");
    }

    static void HostAndRemoteConfigurationRemainIndependent()
    {
        using var gateway = new ClientReplicationTests.GatewayScope();
        var host = new HoloMapSession(); var remote = new HoloMapSession();
        gateway.Server = true; Call(host, "SetUiDragRate", 13d);
        gateway.Server = false; Call(remote, "SetUiDragRate", 30d);
        Check(((UiDragTransactions)Field(host, "_uiDrags")).MoveIntervalTicks == 5
            && ((UiDragTransactions)Field(remote, "_uiDrags")).MoveIntervalTicks == 2
            && gateway.Requests == 0 && gateway.Sent.Count == 0,
            "host and remote setters update their own authority policy without a client packet changing host limits");
        Call(host, "ClearUiDragNetwork"); Call(remote, "ClearUiDragNetwork");
        Call(host, "InitializeUiDragNetwork"); Call(remote, "InitializeUiDragNetwork");
        Check(((UiDragTransactions)Field(host, "_uiDrags")).MoveIntervalTicks == 5
            && ((UiDragTransactions)Field(remote, "_uiDrags")).MoveIntervalTicks == 2,
            "lazy authority constructors preserve each session's independent configured limit");
    }

    static void RenderingOffCancelsActiveViewerAndQueuedMove()
    {
        using var fixture = new Fixture(viewer: true);
        Check(fixture.Use(), "render-off fixture enters native persistent Focus"); fixture.RoundTrip(0);
        fixture.Down(); fixture.Tick(1); fixture.RoundTrip(1);
        long retiredRequest = fixture.Local.RequestId;
        fixture.Up(); fixture.Tick(2); fixture.RoundTrip(2);
        var retiredTerminal = fixture.Acknowledgements.Single(ack => ack.RequestId == retiredRequest && ack.Terminal);
        fixture.Down(); fixture.Tick(3); fixture.RoundTrip(3);
        var grant = fixture.Acknowledgements.Single(ack => ack.RequestId == fixture.Local.RequestId && ack.Kind == (int)UiDragKind.Begin);
        var focus = fixture.Acknowledgements.Single(ack => ack.Kind == (int)UiDragKind.Focus && !ack.Terminal);
        long token = fixture.Bridge.Token;
        Check(fixture.Granted && fixture.Local.Active && fixture.Local.Held && token > 0
            && fixture.Viewers == 1 && fixture.Transactions.ActiveLeases == 1 && fixture.ValueLeases == 1 && fixture.ValueLocks == 1,
            "active production gesture owns separate viewer, value and exact native scopes before rendering is disabled");
        fixture.Move(.4); fixture.Tick(10);
        Check(fixture.Local.Prediction == 4 && fixture.Value.Value == 2 && fixture.Requests.Last().Kind == (int)UiDragKind.Move,
            "a real applied pointer frame queues an uncommitted Move before render-off");
        fixture.Inject(grant); fixture.Inject(focus); fixture.Inject(retiredTerminal);
        Check(fixture.QueuedAcks == 2 && fixture.QueuedTerminals == 1,
            "authenticated delayed grants and an actual retired terminal occupy both ACK queues before cleanup");
        int before = fixture.ClientPackets.Count;
        Check(!Command(fixture.Session, "/hdr off"), "render-off command is consumed locally");
        var terminals = fixture.ClientPackets.Skip(before).Where(UiDragWire.IsDrag)
            .Select(bytes => MyAPIGateway.Utilities.SerializeFromBinary<UiDragRequest>(UiDragWire.Body(bytes))).ToArray();
        Check(terminals.Length == 2 && terminals.All(UiDragWire.Valid)
            && terminals.Single(request => request.Kind == (int)UiDragKind.Blur).ViewerId == focus.ViewerId
            && terminals.Single(request => request.Kind == (int)UiDragKind.Cancel).LeaseId == grant.LeaseId,
            "render-off emits one valid Blur and one Cancel for the original viewer/value identities");
        Check(!(bool)Field(fixture.Session, "ClientRenderingEnabled") && fixture.Bridge.Ready && fixture.Bridge.Token == 0
            && fixture.Releases == 1 && fixture.ReleasedTokens.SequenceEqual(new[] { token })
            && !fixture.Granted && Field(fixture.Session, "_uiViewerRequest") == null && !(bool)Field(fixture.Session, "_uiViewerDownHeld")
            && !fixture.Local.Pending && !fixture.Local.Active && !fixture.Local.Held && !fixture.Local.Finishing
            && fixture.QueuedAcks == 0 && fixture.QueuedTerminals == 0,
            "render-off releases the exact token and clears viewer, numeric identity and both delayed ACK queues immediately");
        fixture.ForwardClient();
        Check(fixture.Transactions.PendingMoves == 0 && fixture.Transactions.PendingTerminals == 1
            && ((IDictionary)Field(fixture.Transactions, "_viewerTerminals")).Count == 1,
            "secure receiver reserves both terminals and removes the pending Move before authority processing");
        fixture.RoundTrip(11);
        Check(fixture.Viewers == 0 && fixture.Transactions.ActiveLeases == 0 && fixture.ValueLeases == 0 && fixture.ValueLocks == 0
            && fixture.Value.Value == 2 && fixture.Bridge.Token == 0 && !fixture.Local.Pending,
            "server terminal drain frees PB viewer/value authority without committing the queued Move");
        int packets = fixture.ClientPackets.Count, samples = fixture.Samples;
        fixture.Inject(grant); fixture.Inject(focus); Set(fixture.Session, "_ticks", 12); Call(fixture.Session, "TickUiDragNetwork");
        fixture.Tick(12); Command(fixture.Session, "/hdr on"); Call(fixture.Session, "TickUiDragClient");
        Check(fixture.QueuedAcks == 0 && fixture.QueuedTerminals == 0 && !fixture.Local.Pending && !fixture.Local.Active
            && !fixture.Granted && Field(fixture.Session, "_uiViewerRequest") == null && fixture.Bridge.Token == 0
            && fixture.Acquires == 1 && fixture.Releases == 1 && fixture.Samples == samples && fixture.ClientPackets.Count == packets,
            "late accepted Begin/Focus packets and re-enabling rendering cannot resurrect input or reacquire a native token");
    }

    static void RenderingOffBeforeGrantDelivery()
    {
        foreach (bool viewer in new[] { false, true })
        {
            using var fixture = new Fixture(viewer);
            Check(fixture.Use(), "latency render-off fixture enters native " + (viewer ? "Focus" : "Begin"));
            if (!viewer) { fixture.Down(); fixture.Tick(1); }
            fixture.AdvanceServer(1);
            Check(fixture.Acknowledgements.Any(ack => ack.Status == (int)UiDragStatus.Accepted && !ack.Terminal)
                && !fixture.Granted && !fixture.Local.Active && fixture.Bridge.Token > 0,
                "server grants authority while the local scope still awaits secure ACK delivery");
            int before = fixture.ClientPackets.Count;
            Command(fixture.Session, "/hdr off");
            var terminal = fixture.ClientPackets.Skip(before).Where(UiDragWire.IsDrag)
                .Select(bytes => MyAPIGateway.Utilities.SerializeFromBinary<UiDragRequest>(UiDragWire.Body(bytes))).Single();
            Check(UiDragWire.Valid(terminal) && terminal.Kind == (int)(viewer ? UiDragKind.Blur : UiDragKind.Cancel)
                && terminal.LeaseId == 0 && terminal.ViewerId == 0 && fixture.Bridge.Token == 0
                && fixture.Releases == 1 && !fixture.Local.Pending && Field(fixture.Session, "_uiViewerRequest") == null,
                "render-off preserves the reserved identity-only terminal when its lease grant has not arrived");
            fixture.RoundTrip(2); fixture.Tick(3);
            Check(fixture.Viewers == 0 && fixture.Transactions.ActiveLeases == 0 && fixture.ValueLeases == 0 && fixture.ValueLocks == 0
                && !fixture.Granted && !fixture.Local.Pending && !fixture.Local.Active && fixture.Bridge.Token == 0
                && fixture.Value.Value == 2 && fixture.Acquires == 1 && fixture.Releases == 1,
                "delayed accepted and terminal ACK delivery closes authority without reviving either pending input scope");
        }
    }
}
