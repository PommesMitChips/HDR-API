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

// These use the staged session's actual eligibility, native USE, client tick,
// pointer bridge and secure ACK paths. The installed game interfaces are doubled;
// physical native input application and a live GUI still require in-game proof.
internal static class NativeUiDragInputTests
{
    static int checks;
    static void Check(bool good, string label)
    { if (!good) throw new Exception("Native UI input: " + label); checks++; }
    static object Call(object target, string name, params object[] arguments)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == arguments.Length);
        try { return method.Invoke(target, arguments); }
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
        public readonly List<byte[]> ClientPackets = new(), ServerPackets = new();
        public readonly Func<string, object[], object> Ui;
        public readonly NativePointerProvider Provider = new();
        public bool AsClient, Cursor, Chat, ForeignScreen;
        readonly Vector3D Head = new(.2, 0, 1);
        int forwardedClient, forwardedServer;
        public Fixture(bool viewer = false, bool sibling = false, bool siblingVisible = false)
        {
            Registry[10] = DrawTestProxy.Make(typeof(IMyProgrammableBlock), (method, args) => method.Name switch
            {
                "get_EntityId" => 10L, "get_OwnerId" => 77L, "get_Closed" => false, "get_IsWorking" => true,
                "IsSameConstructAs" => true, "HasPlayerAccess" => true,
                _ => throw new Exception("Native input PB: " + method.Name)
            });
            Registry[20] = DrawTestProxy.Make(typeof(IMyProjector), (method, args) => method.Name switch
            {
                "get_EntityId" => 20L, "get_Closed" => false, "get_IsWorking" => true, "HasPlayerAccess" => true,
                "get_WorldMatrix" => MatrixD.Identity, "GetPosition" => Vector3D.Zero,
                "get_BlockDefinition" => Activator.CreateInstance(method.ReturnType),
                _ => throw new Exception("Native input display: " + method.Name)
            });
            object character = DrawTestProxy.Make(typeof(NativeUiDragInputCharacter), (method, args) => method.Name switch
            {
                "get_EntityId" => 30L, "get_Entity" => Registry.GetValueOrDefault(30L), "get_Closed" => false,
                "get_IsDead" => false, "GetPosition" => Head,
                "GetHeadMatrix" => MatrixD.CreateWorld(Head, Vector3D.Forward, Vector3D.Up),
                _ => throw new Exception("Native input character: " + method.Name)
            });
            Registry[30] = character;
            object controller = DrawTestProxy.Make(typeof(IMyEntityController), (method, args) =>
                method.Name == "get_ControlledEntity" ? character : throw new Exception("Native input controller: " + method.Name));
            object player = DrawTestProxy.Make(typeof(IMyPlayer), (method, args) => method.Name switch
            {
                "get_SteamUserId" => 200UL, "get_IdentityId" => 77L, "get_Character" => character, "get_Controller" => controller,
                _ => throw new Exception("Native input player: " + method.Name)
            });
            object camera = DrawTestProxy.Make(typeof(IMySession).GetProperty("Camera").PropertyType, (method, args) => method.Name switch
            {
                "get_Position" => Head, "get_WorldMatrix" => MatrixD.CreateWorld(Head, Vector3D.Forward, Vector3D.Up),
                "get_ViewMatrix" => MatrixD.CreateLookAt(Head, Head + Vector3D.Forward, Vector3D.Up),
                "get_ProjectionMatrix" => MatrixD.CreatePerspectiveFieldOfView(2.4, 1920d / 1080, .1, 100),
                "get_ViewportSize" => new Vector2(1920, 1080), "get_ViewportOffset" => Vector2.Zero,
                _ => throw new Exception("Native input camera: " + method.Name)
            });
            object cameraController = DrawTestProxy.Make(typeof(IMySession).GetProperty("CameraController").PropertyType,
                (method, args) => method.Name == "get_IsInFirstPersonView" ? true : throw new Exception("Native input camera controller: " + method.Name));
            Gateway.Install("Session", (method, args) => method.Name switch
            {
                "get_LocalHumanPlayer" => null, "get_Player" => player, "get_Camera" => camera, "get_CameraController" => cameraController,
                _ => throw new Exception("Native input session: " + method.Name)
            });
            Gateway.Install("Multiplayer", (method, args) =>
            {
                if (method.Name == "get_IsServer") return !AsClient;
                if (method.Name == "get_ServerId") return 100UL;
                if (method.Name == "SendMessageToServer") { ClientPackets.Add((byte[])args[1]); return true; }
                if (method.Name == "SendMessageTo") { ServerPackets.Add((byte[])args[1]); return true; }
                throw new Exception("Native input multiplayer: " + method.Name);
            });
            Gateway.Install("Entities", (method, args) => method.Name == "GetEntityById"
                ? Registry.GetValueOrDefault((long)args[0]) : throw new Exception("Native input entities: " + method.Name));
            Gateway.Install("Players", (method, args) =>
            {
                if (method.Name != "GetPlayers") throw new Exception("Native input players: " + method.Name);
                var predicate = args.Length > 1 ? (Func<IMyPlayer, bool>)args[1] : null;
                if (predicate == null || predicate((IMyPlayer)player)) ((List<IMyPlayer>)args[0]).Add((IMyPlayer)player);
                return null;
            });
            Gateway.Install("Physics", (method, args) => method.Name == "CastRay" ? null : throw new Exception("Native input physics: " + method.Name));
            var draw = (Func<string, object[], object>)Call(Session, "DrawEndpoint", Registry[10]);
            draw("target", new[] { Registry[20] });
            draw("line", new object[] { "handle", Vector3D.Zero, Vector3D.UnitX, "cyan" });
            Ui = (Func<string, object[], object>)Call(Session, "UiEndpoint", Registry[10]);
            Ui("target", new[] { Registry[20] }); Ui("bundle", new object[] { "main" });
            Ui("value", new object[] { "gain", 2d, 0d, 10d, 1d });
            Ui("control", new object[] { "slider", "main", "handle", .5, 0d, 1d, .25 });
            Ui("bind-value", new object[] { "slider", "gain" });
            Ui("constraint", new object[] { "slider", "line", Vector3D.Zero, Vector3D.UnitX });
            Ui("draggable", new object[] { "slider", true });
            if (sibling)
            {
                draw("line", new object[] { "sibling-art", Vector3D.Zero, Vector3D.UnitX, "cyan" });
                draw("transform", new object[] { "sibling-art", MatrixD.CreateTranslation(2, 0, 0) });
                Ui("value", new object[] { "trim", 2d, 0d, 10d, 1d });
                Ui("control", new object[] { "sibling", "main", "sibling-art", .5, 0d, 1d, .25 });
                Ui("bind-value", new object[] { "sibling", "trim" });
                Ui("constraint", new object[] { "sibling", "line", Vector3D.Zero, Vector3D.UnitX });
                Ui("draggable", new object[] { "sibling", true });
                if (!siblingVisible) Ui("visible", new object[] { "sibling", false });
            }
            Set(Scene, "Offset", Vector3D.Zero); Set(Scene, "Scale", 1d); Set(Scene, "Rotation", Vector3D.Zero); Set(Scene, "VolumeRange", 0d);
            object originalUtilities = MyAPIGateway.Utilities;
            Gateway.Install("Utilities", (method, args) =>
            {
                if (method.Name == "get_IsDedicated") return false;
                if (method.Name == "RegisterMessageHandler" || method.Name == "UnregisterMessageHandler" || method.Name == "SendModMessage") return null;
                return method.Invoke(originalUtilities, args);
            });
            Gateway.Install("Input", (method, args) => method.Name switch
            {
                "GetMouseAreaSize" => new Vector2(1920, 1080), "IsNewKeyPressed" => false,
                _ => throw new Exception("Native input input: " + method.Name)
            });
            Gateway.Install("Gui", (method, args) => method.Name switch
            {
                "get_ChatEntryVisible" => Chat, "get_IsCursorVisible" => Cursor,
                "get_GetCurrentScreen" => ForeignScreen
                    ? Enum.GetValues(method.ReturnType).Cast<object>().First(value => value.ToString() != "None")
                    : Enum.Parse(method.ReturnType, "None"),
                _ => throw new Exception("Native input GUI: " + method.Name)
            });
            AsClient = true;
            Set(Session, "UiDragPersistentFocus", viewer);
            Call(Session, "InitializeUiPointer");
            Call(Session, "ReceiveUiPointerProvider", new MyTuple<string, int, Func<string, object[], object>>(
                PointerInputBridge.Version, 1, Provider.Endpoint));
        }
        public object Scene => ((IDictionary)Field(Session, "_scenes"))[20L];
        public UiDisplay Display => (UiDisplay)Call(Session, "GetUiDisplay", 10L, 20L);
        public UiWidget Widget => WidgetNamed("slider");
        public UiWidget WidgetNamed(string id) => Display.Widgets.Single(widget => widget.Id == id);
        public UiNumericValue Value => ValueNamed("gain");
        public UiNumericValue ValueNamed(string id) => Display.Values.Single(value => value.Id == id);
        public PointerInputBridge Bridge => (PointerInputBridge)Field(Session, "_uiPointer");
        public UiDragLocalLease Local => (UiDragLocalLease)Field(Session, "_uiDragLocal");
        public bool Eligible => (bool)Call(Session, "UiClientCanInteract");
        public UiDragRequest[] Requests => ClientPackets.Where(UiDragWire.IsDrag)
            .Select(bytes => MyAPIGateway.Utilities.SerializeFromBinary<UiDragRequest>(UiDragWire.Body(bytes))).ToArray();
        public UiDragAck[] Acknowledgements => ServerPackets.Where(UiDragWire.IsDrag)
            .Select(bytes => MyAPIGateway.Utilities.SerializeFromBinary<UiDragAck>(UiDragWire.Body(bytes))).ToArray();
        public bool Use(string control = "slider")
        {
            Set(Session, "_uiHoveredDisplay", Display); Set(Session, "_uiHoveredWidget", WidgetNamed(control));
            bool consumed = (bool)Call(Session, "TryConsumeUiUse", 20L);
            if (consumed) Cursor = true;
            return consumed;
        }
        public UiWidget Pick(out long tile)
        {
            if (!Bridge.Sample(out var sample)) throw new Exception("Native input fixture has no provider sample");
            object[] arguments = { sample, 0L };
            var result = (UiWidget)Call(Session, "UiViewerPick", arguments); tile = (long)arguments[1]; return result;
        }
        public object UiWrite(string command, params object[] arguments)
        { bool previous = AsClient; AsClient = false; try { return Ui(command, arguments); } finally { AsClient = previous; } }
        public void InjectServerAcknowledgement(UiDragAck acknowledgement)
        {
            AsClient = true;
            Call(Session, "ReceiveUiMessage", (ushort)49784,
                UiDragWire.Wrap(MyAPIGateway.Utilities.SerializeToBinary(acknowledgement)), 100UL, true);
        }
        public void Tick(int tick) { Set(Session, "_ticks", tick); Call(Session, "TickUiClient"); }
        public void RoundTrip(int tick)
        {
            Set(Session, "_ticks", tick); AsClient = false;
            while (forwardedClient < ClientPackets.Count)
                Call(Session, "ReceiveUiMessage", (ushort)49784, ClientPackets[forwardedClient++], 200UL, false);
            Call(Session, "TickUiNetwork"); AsClient = true;
            while (forwardedServer < ServerPackets.Count)
                Call(Session, "ReceiveUiMessage", (ushort)49784, ServerPackets[forwardedServer++], 100UL, true);
            Call(Session, "TickUiNetwork");
        }
        public void Dispose()
        {
            Call(Session, "UnregisterUiPointer"); Call(Session, "ClearUiDragNetwork"); Gateway.Dispose();
        }
    }
    sealed class NativePointerProvider
    {
        public const long Token = 700;
        public int Flags = 1, Pressed, Held, Released, Acquires, Releases;
        public long Frame = 1;
        public double X = .5, Y = .5;
        public readonly List<long> Samples = new(), ReleasedTokens = new();
        public object Endpoint(string operation, object[] arguments)
        {
            if (operation == "acquire")
            {
                Check((long)arguments[0] == 30 && ((string)arguments[1]).StartsWith("10/20/", StringComparison.Ordinal),
                    "native acquisition binds the current character and registered display key");
                Acquires++; return new MyTuple<long, int>(Token, 1);
            }
            if (operation == "release") { Releases++; ReleasedTokens.Add((long)arguments[0]); return null; }
            if (operation == "sample")
            {
                Samples.Add((long)arguments[0]);
                return new MyTuple<long, int, MyTuple<double, double>, MyTuple<int, int, int>>(Frame, Flags,
                    new MyTuple<double, double>(X, Y), new MyTuple<int, int, int>(Pressed, Held, Released));
            }
            throw new Exception("Native input provider: " + operation);
        }
        public void Down() { Frame++; Flags = 18; Pressed = Held = 1; Released = 0; }
        public void Up() { Frame++; Flags = 18; Pressed = Held = 0; Released = 1; }
        public void Move(double worldX)
        { Frame++; Pressed = Released = 0; Held = 1; X = .5 + (worldX - .2) / (2 * Math.Tan(1.2) * (1920d / 1080)); }
    }

    public static int Run()
    {
        checks = 0;
        PendingOwnTokenSurvivesServerFirst();
        AppliedDownWaitsForServerGrant();
        PendingPersistentViewerOwnsCursor();
        HiddenSiblingPreservesViewerScope();
        RetiredGrantCannotCancelSuccessorGesture();
        ForeignUiCancelsActualClient();
        CursorNeedsExactLocalOwnership();
        return checks;
    }
    static void PendingOwnTokenSurvivesServerFirst()
    {
        using var fixture = new Fixture();
        Check(fixture.Eligible && fixture.Use(), "actual native USE revalidates the draggable hotzone before acquiring input");
        Check(fixture.Bridge.Token == NativePointerProvider.Token && fixture.Provider.Acquires == 1 && fixture.Local.Pending
            && fixture.Requests.Length == 1 && fixture.Requests[0].Kind == (int)UiDragKind.Begin,
            "queued native acquisition retains its exact token and sends a pending Begin");
        UiPointerSample pending;
        Check(fixture.Bridge.Sample(out pending) && pending.Flags == 1 && !pending.Active && MyAPIGateway.Gui.IsCursorVisible
            && fixture.Eligible, "visible native cursor with own pending flag 1 remains eligible before application");
        fixture.Provider.Pressed = fixture.Provider.Held = 1;
        fixture.Tick(1);
        Check(fixture.Bridge.Token == NativePointerProvider.Token && fixture.Provider.Releases == 0 && fixture.Local.Pending
            && !fixture.Local.Active && !fixture.Local.Held && !fixture.Local.Predict(9, 1) && fixture.Requests.Length == 1,
            "actual client tick ignores pending button bits and keeps native input without prediction or Move");
        fixture.RoundTrip(2);
        Check(fixture.Local.LeaseId > 0 && fixture.Local.Pending && !fixture.Local.Active && !fixture.Local.Held,
            "authenticated serialized server grant alone cannot activate unapplied pointer input");
        fixture.Provider.Frame++; fixture.Tick(8);
        Check(fixture.Eligible && !fixture.Local.Predict(9, 8) && fixture.Local.Prediction == 2 && fixture.Value.Value == 2
            && fixture.Requests.Length == 1 && fixture.Provider.Releases == 0,
            "own pending sample survives after server grant without enabling value prediction or wire Move");
        fixture.Provider.Down(); fixture.Tick(9);
        Check(fixture.Local.Active && fixture.Local.Held && (bool)Field(fixture.Session, "_uiDragGrabbed")
            && fixture.Bridge.Token == NativePointerProvider.Token && fixture.Provider.Releases == 0,
            "Applied|PointerValid flag 18 plus physical down and server grant opens actual drag prediction");
        Check(fixture.Provider.Samples.Count > 0 && fixture.Provider.Samples.All(token => token == NativePointerProvider.Token),
            "eligibility and client sampling retain only their exact acquired provider token");
    }
    static void AppliedDownWaitsForServerGrant()
    {
        using var fixture = new Fixture(); Check(fixture.Use(), "latency fixture enters via actual native USE");
        fixture.Provider.Down(); fixture.Tick(1);
        Check(fixture.Eligible && fixture.Local.Pending && fixture.Local.Held && !fixture.Local.Active
            && (bool)Field(fixture.Session, "_uiDragGrabbed"), "applied physical down before server grant latches Held and grab state latently");
        fixture.Provider.Move(.4); fixture.Tick(7);
        Check(!fixture.Local.Predict(9, 7) && fixture.Local.Prediction == 2 && fixture.Value.Value == 2
            && fixture.Requests.Length == 1 && !fixture.Requests.Any(request => request.Kind == (int)UiDragKind.Move),
            "applied held motion still cannot predict or send Move before server grant");
        fixture.RoundTrip(8);
        Check(fixture.Local.Active && fixture.Local.Held && fixture.Local.LeaseId > 0 && fixture.Provider.Acquires == 1,
            "delayed secure Begin grant activates the already latched physical gesture without reacquisition");
        fixture.Provider.Move(.4); fixture.Tick(9);
        Check(fixture.Local.Prediction == 4 && fixture.Value.Value == 2
            && fixture.Requests.Count(request => request.Kind == (int)UiDragKind.Move) == 1,
            "first granted held frame predicts from original down position and sends one absolute Move");
        fixture.RoundTrip(14);
        Check(fixture.Value.Value == 4 && fixture.Local.Confirmed == 4 && fixture.Bridge.Token == NativePointerProvider.Token,
            "predicted Move passes actual server authority commit and authenticated client ACK");
    }
    static void PendingPersistentViewerOwnsCursor()
    {
        using var fixture = new Fixture(viewer: true); Check(fixture.Use(), "persistent viewer enters via actual native USE");
        fixture.Tick(1);
        Check(MyAPIGateway.Gui.IsCursorVisible && fixture.Eligible && fixture.Bridge.Token == NativePointerProvider.Token
            && fixture.Provider.Releases == 0 && Field(fixture.Session, "_uiViewerRequest") != null
            && !fixture.Local.Pending && !fixture.Local.Active && fixture.Requests.Single().Kind == (int)UiDragKind.Focus,
            "pending Focus owns native cursor without manufacturing a numeric Begin or prediction");
        fixture.Provider.Down(); fixture.Tick(2);
        Check((bool)Field(fixture.Session, "_uiViewerDownHeld") && (bool)Field(fixture.Session, "_uiDragGrabbed")
            && !fixture.Local.Pending && !fixture.Local.Predict(9, 2) && fixture.Requests.Length == 1,
            "applied viewer down before Focus ACK latches the physical gesture without numeric authority");
        fixture.RoundTrip(3); fixture.Provider.Move(.4); fixture.Tick(4);
        Check((bool)Field(fixture.Session, "_uiViewerGranted") && fixture.Local.Pending && fixture.Local.Held && !fixture.Local.Active
            && fixture.Requests.Count(request => request.Kind == (int)UiDragKind.Begin) == 1 && fixture.Local.Prediction == 2,
            "Focus grant permits one latent numeric Begin while its separate value grant remains pending");
        fixture.RoundTrip(5);
        Check(fixture.Local.Active && fixture.Local.Held && fixture.Bridge.Token == NativePointerProvider.Token
            && fixture.Provider.Acquires == 1 && fixture.Provider.Releases == 0,
            "persistent viewer and numeric grant share their original exact native token");
    }
    static void HiddenSiblingPreservesViewerScope()
    {
        using var fixture = new Fixture(viewer: true, sibling: true);
        Check(fixture.Widget.Visible && !fixture.WidgetNamed("sibling").Visible && !fixture.Use("sibling")
            && fixture.Provider.Acquires == 0 && fixture.Requests.Length == 0,
            "hidden sibling cannot enter Focus through actual native USE");
        Check(fixture.Use(), "visible entry control enters Focus with a hidden same-bundle sibling");
        var latched = (IDictionary)Field(fixture.Session, "_uiViewerSources");
        long oldDefinition = fixture.Display.Revision;
        Check(latched.Count == 2 && (long)latched["slider"] == fixture.Widget.Control.SourceRevision
            && (long)latched["sibling"] == fixture.WidgetNamed("sibling").Control.SourceRevision,
            "Focus latches both visible and hidden control membership and source stamps");
        fixture.RoundTrip(0); long oldViewer = (long)Field(fixture.Session, "_uiViewerId");
        fixture.Tick(1);
        Check(oldViewer > 0 && (bool)Field(fixture.Session, "_uiViewerGranted") && fixture.Eligible
            && Field(fixture.Session, "_uiViewerRequest") != null && fixture.Bridge.Token == NativePointerProvider.Token
            && fixture.Provider.Acquires == 1 && fixture.Provider.Releases == 0 && !fixture.Local.Pending,
            "authenticated Focus ACK and full client tick preserve exact token despite hidden sibling membership");
        fixture.Provider.Move(2.5); fixture.Provider.Down();
        Check(fixture.Pick(out long hiddenTile) == null && hiddenTile == 0,
            "actual viewer ray cannot pick the hidden sibling at its own physical hotzone");
        fixture.Tick(2);
        Check(Field(fixture.Session, "_uiViewerDownControl") == null && !(bool)Field(fixture.Session, "_uiViewerDownHeld")
            && !fixture.Local.Pending && fixture.Requests.Count(request => request.Kind == (int)UiDragKind.Focus) == 1
            && !fixture.Requests.Any(request => request.Kind == (int)UiDragKind.Begin)
            && fixture.Bridge.Token == NativePointerProvider.Token && fixture.Provider.Releases == 0,
            "applied down over hidden sibling creates no gesture while retaining the granted viewer");
        fixture.Provider.Move(.2);
        Check(fixture.Pick(out long visibleTile)?.Id == "slider" && visibleTile == 20,
            "visible entry control remains pickable under the same viewer");
        fixture.Provider.Up(); fixture.Tick(3);
        fixture.UiWrite("visible", "sibling", true);
        Check(fixture.Display.Revision > oldDefinition && fixture.WidgetNamed("sibling").Visible,
            "PB visibility update installs a new UI definition for the sibling");
        fixture.Provider.Frame++; fixture.Tick(4);
        Check(Field(fixture.Session, "_uiViewerRequest") == null && !(bool)Field(fixture.Session, "_uiViewerGranted")
            && fixture.Bridge.Token == 0 && fixture.Provider.Releases == 1
            && fixture.Requests.Count(request => request.Kind == (int)UiDragKind.Blur) == 1,
            "changed definition invalidates old client Focus and releases its exact token");
        fixture.RoundTrip(4);
        fixture.Cursor = false; fixture.Provider.Flags = 1; fixture.Provider.Pressed = fixture.Provider.Held = fixture.Provider.Released = 0;
        fixture.Provider.Frame++;
        Check(fixture.Use(), "changed definition requires another physically revalidated native USE Focus");
        var renewed = fixture.Requests.Last(request => request.Kind == (int)UiDragKind.Focus);
        fixture.RoundTrip(5); fixture.Tick(6);
        Check(renewed.DefinitionRevision == fixture.Display.Revision && renewed.DefinitionRevision != oldDefinition
            && (long)Field(fixture.Session, "_uiViewerId") > 0 && (long)Field(fixture.Session, "_uiViewerId") != oldViewer
            && (bool)Field(fixture.Session, "_uiViewerGranted") && fixture.Bridge.Token == NativePointerProvider.Token
            && fixture.Provider.Acquires == 2 && fixture.Provider.Releases == 1,
            "fresh Focus grants new definition and viewer identity after old scope is released");
        fixture.Provider.Flags = 18; fixture.Provider.Move(2.5);
        Check(fixture.Pick(out long renewedTile)?.Id == "sibling" && renewedTile == 20,
            "new Focus can pick the sibling only after its visibility definition changes");
    }
    static void RetiredGrantCannotCancelSuccessorGesture()
    {
        using var fixture = new Fixture(viewer: true, sibling: true, siblingVisible: true);
        Check(fixture.Use(), "retired ACK fixture enters native persistent Focus"); fixture.RoundTrip(0);
        fixture.Provider.Down(); fixture.Tick(1); fixture.RoundTrip(1);
        long oldRequest = fixture.Local.RequestId;
        var retired = fixture.Acknowledgements.Single(ack => ack.Kind == (int)UiDragKind.Begin && ack.RequestId == oldRequest
            && ack.Status == (int)UiDragStatus.Accepted && !ack.Terminal);
        Check(fixture.Local.Active && fixture.Local.Held && retired.LeaseId == fixture.Local.LeaseId
            && retired.ControlId == "slider", "gesture A obtains a real captured accepted Begin ACK");
        fixture.Provider.Up(); fixture.Tick(2); fixture.RoundTrip(2);
        Check(!fixture.Local.Pending && fixture.Bridge.Token == NativePointerProvider.Token && fixture.Provider.Releases == 0
            && (bool)Field(fixture.Session, "_uiViewerGranted"),
            "gesture A terminal ACK retires value authority while retaining exact native viewer token");
        fixture.Provider.Move(2.5); fixture.Provider.Down(); fixture.Tick(3); fixture.RoundTrip(3);
        long request = fixture.Local.RequestId, lease = fixture.Local.LeaseId, revision = fixture.Local.ValueRevision;
        long viewer = (long)Field(fixture.Session, "_uiViewerId");
        double prediction = fixture.Local.Prediction;
        int packets = fixture.ClientPackets.Count;
        Check(fixture.Local.Active && fixture.Local.Held && request != retired.RequestId && lease != retired.LeaseId
            && (string)Field(fixture.Session, "_uiDragControlId") == "sibling",
            "successor gesture B obtains its own active identity on another control under original viewer");
        retired.CharacterId = 999; retired.TileId = 998;
        retired.MinimumSequence = checked((long)Field(fixture.Session, "_uiSequence") + 10000);
        Check(UiDragWire.Valid(retired) && !fixture.Local.MatchesAcknowledgement(retired),
            "retired accepted Begin ACK remains wire-valid but cannot match successor gesture identity");
        fixture.InjectServerAcknowledgement(retired);
        Check(((ICollection)Field(fixture.Session, "_uiDragAcks")).Count == 1 && fixture.Local.Active
            && (long)Field(fixture.Session, "_uiSequence") < retired.MinimumSequence && fixture.ClientPackets.Count == packets,
            "authenticated secure receiver queues retired ACK without cancelling successor or advancing seed inline");
        Set(fixture.Session, "_ticks", 4); Call(fixture.Session, "TickUiDragNetwork");
        Check((long)Field(fixture.Session, "_uiSequence") == retired.MinimumSequence
            && ((ICollection)Field(fixture.Session, "_uiDragAcks")).Count == 0,
            "simulation-thread ACK drain independently advances authenticated sequence seed");
        Check(fixture.Local.Active && fixture.Local.Held && fixture.Local.RequestId == request && fixture.Local.LeaseId == lease
            && fixture.Local.ValueRevision == revision && fixture.Local.Prediction == prediction
            && (long)Field(fixture.Session, "_uiViewerId") == viewer && (bool)Field(fixture.Session, "_uiViewerGranted")
            && fixture.Bridge.Token == NativePointerProvider.Token && fixture.Provider.Acquires == 1
            && fixture.Provider.Releases == 0 && fixture.ClientPackets.Count == packets,
            "retired ACK with foreign character and tile cannot revoke successor value/viewer/native ownership");
        fixture.Provider.Move(2.6); fixture.Tick(10);
        var move = fixture.Requests.Last();
        Check(move.Kind == (int)UiDragKind.Move && move.ControlId == "sibling" && move.RequestId == request
            && move.LeaseId == lease && move.Sequence > retired.MinimumSequence,
            "surviving successor sends its next real Move above retired ACK's authenticated sequence seed");
        fixture.RoundTrip(10);
        Check(fixture.Local.Active && fixture.Local.Held && fixture.Local.Confirmed == 3
            && fixture.ValueNamed("trim").Value == 3 && fixture.Value.Value == 2
            && fixture.Bridge.Token == NativePointerProvider.Token && fixture.Provider.Releases == 0,
            "successor still commits its own numeric value through server authority after retired ACK rejection");
    }
    static void ForeignUiCancelsActualClient()
    {
        (string Label, Action<Fixture> Change)[] invalidations = {
            ("foreign terminal screen", fixture => fixture.ForeignScreen = true),
            ("chat entry", fixture => fixture.Chat = true),
            ("provider unavailable", fixture => fixture.Provider.Flags = 32),
            ("provider cancelled", fixture => fixture.Provider.Flags = 4),
            ("provider disposed", fixture => fixture.Provider.Flags = 64),
            ("applied sample without PointerValid", fixture => fixture.Provider.Flags = 2),
            ("PointerValid sample without Applied", fixture => fixture.Provider.Flags = 16),
            ("token without pending or applied sample", fixture => fixture.Provider.Flags = 0)
        };
        foreach (var invalidation in invalidations)
        {
            using var fixture = new Fixture(); Check(fixture.Use(), invalidation.Label + " fixture acquires pending native input");
            invalidation.Change(fixture);
            Check(!fixture.Eligible, invalidation.Label + " rejects eligibility despite own pending native token");
            fixture.Tick(1);
            Check(fixture.Bridge.Token == 0 && fixture.Provider.Releases == 1
                && fixture.Provider.ReleasedTokens.SequenceEqual(new[] { NativePointerProvider.Token })
                && !fixture.Local.Active && !fixture.Local.Held && fixture.Local.Finishing
                && fixture.Requests.Count(request => request.Kind == (int)UiDragKind.Cancel) == 1,
                invalidation.Label + " actual TickUiClient cancels exactly once and releases exact native token");
            fixture.RoundTrip(1);
            Check(!fixture.Local.Pending && fixture.Value.Value == 2 && !fixture.Requests.Any(request => request.Kind == (int)UiDragKind.Move),
                invalidation.Label + " pending cancellation closes through secure server ACK without a value Move");
        }
        using var viewer = new Fixture(viewer: true); Check(viewer.Use(), "foreign GUI viewer fixture opens pending Focus");
        viewer.ForeignScreen = true; viewer.Tick(1);
        Check(!viewer.Eligible && viewer.Bridge.Token == 0 && viewer.Provider.Releases == 1
            && Field(viewer.Session, "_uiViewerRequest") == null && !viewer.Local.Pending
            && viewer.Requests.Count(request => request.Kind == (int)UiDragKind.Blur) == 1,
            "foreign GUI actual client path sends pending viewer Blur and releases exact native token");
    }
    static void CursorNeedsExactLocalOwnership()
    {
        using (var fixture = new Fixture())
        {
            fixture.Cursor = true;
            Check(!fixture.Eligible && fixture.Bridge.Token == 0 && !fixture.Local.Pending,
                "visible cursor without a bridge token or local lease fails eligibility");
            fixture.Tick(1);
            Check(fixture.Provider.Acquires == 0 && fixture.Provider.Releases == 0 && fixture.ClientPackets.Count == 0,
                "foreign cursor cleanup cannot acquire a token or create a drag request");
        }
        using (var fixture = new Fixture())
        {
            Check(fixture.Bridge.Acquire(30, "10/20/orphan/1"), "orphan exact-token fixture acquires provider input");
            fixture.Cursor = true;
            Check(!fixture.Local.Pending && Field(fixture.Session, "_uiViewerRequest") == null && !fixture.Eligible,
                "an acquired exact bridge token without its pending drag or viewer lease cannot claim visible cursor");
            fixture.Tick(1);
            Check(fixture.Bridge.Token == 0 && fixture.Provider.Releases == 1 && fixture.ClientPackets.Count == 0,
                "actual client cleanup releases orphan provider token without manufacturing network authority");
        }
        using (var fixture = new Fixture())
        {
            Check(fixture.Use(), "lost-token fixture enters actual native drag"); fixture.Bridge.Release();
            Check(fixture.Local.Pending && !fixture.Eligible && fixture.Provider.Releases == 1,
                "pending local lease cannot borrow a visible cursor after its exact bridge token is gone");
            fixture.Tick(1);
            Check(fixture.Local.Finishing && fixture.Bridge.Token == 0 && fixture.Provider.Releases == 1
                && fixture.Requests.Count(request => request.Kind == (int)UiDragKind.Cancel) == 1,
                "actual client loss of exact token cancels pending lease without double native release");
        }
    }
}

public interface NativeUiDragInputCharacter : IMyCharacter, VRage.Game.ModAPI.Interfaces.IMyControllableEntity { }
