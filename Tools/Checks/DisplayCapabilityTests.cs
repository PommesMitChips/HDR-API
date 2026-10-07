using System.Collections;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRageMath;

internal static class DisplayCapabilityTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string clause) { if (!value) throw new Exception("Display capabilities: " + clause); count++; }
        void Reject(Action action, string clause) { try { action(); } catch (ArgumentException) { count++; return; } throw new Exception("Expected capability rejection: " + clause); }
        using var gateway = new ClientReplicationTests.GatewayScope { Server = true, Dedicated = true };
        bool same = true, access = true, closed = false; long owner = 77;
        var registered = new Dictionary<long, object>();
        var caller = DrawTestProxy.Make(typeof(IMyProgrammableBlock), (m, a) => m.Name switch
        {
            "get_EntityId" => 10L, "get_OwnerId" => owner, "get_Closed" => false,
            "IsSameConstructAs" => same, _ => throw new Exception("Capability query observed caller state: " + m.Name)
        });
        registered[10] = caller;
        object Block(Type type, long id, string subtype)
        {
            var block = DrawTestProxy.Make(type, (m, a) => m.Name switch
            {
                "get_EntityId" => id, "get_Closed" => closed, "HasPlayerAccess" => access,
                "get_BlockDefinition" => (VRage.ObjectBuilders.SerializableDefinitionId)new MyDefinitionId(typeof(VRage.Game.MyObjectBuilder_CubeBlock), subtype),
                "get_Model" => null, _ => throw new Exception("Capability query mutated target state: " + m.Name)
            });
            registered[id] = block; return block;
        }
        var projector = Block(typeof(IMyProjector), 20, "LargeProjector");
        var console = Block(typeof(IMyProjector), 21, "LargeBlockConsole");
        var lcd = Block(typeof(IMyTextPanel), 22, "LargeLCDPanel");
        var unsupported = Block(typeof(IMyProgrammableBlock), 23, "LargeProgrammableBlock");
        gateway.Install("Entities", (m, a) => m.Name == "GetEntityById" && registered.TryGetValue((long)a[0], out var block) ? block : null);
        var session = new HoloMapSession();
        var draw = (Func<string, object[], object>)ClientReplicationTests.Call(session, "DrawEndpoint", caller);
        MyTuple<string, string, int> Query(object target) => (MyTuple<string, string, int>)draw("capabilities", new[] { target });
        void Pure(string clause)
        {
            Check(((IDictionary)ClientReplicationTests.Field(session, "_scenes")).Count == 0, clause + " allocates no scenes");
            Check(((IDictionary)ClientReplicationTests.Field(session, "_callerPrograms")).Count == 0, clause + " observes no PB program");
            Check(!(bool)ClientReplicationTests.Field(session, "_dirty"), clause + " does not dirty replication");
        }
        var result = Query(projector);
        Check(result.Item1 == "HDR.DisplayCapabilities/1" && result.Item2 == "projector" && result.Item3 == (1 | 8 | 32 | 64), "projector supports floating surfaces without claiming table limits");
        result = Query(console); Check(result.Item2 == "console" && result.Item3 == (1 | 8 | 16 | 32 | 64), "console adds table volume");
        result = Query(lcd); Check(result.Item2 == "lcd" && result.Item3 == (1 | 2 | 64), "LCD native drawing has no floating projection and unknown calibration stays native");
        result = Query(unsupported); Check(result.Item2 == "unsupported" && result.Item3 == 0, "other text-surface providers are honestly unsupported");
        Pure("Detached queries");
        Reject(() => draw("line", new object[] { "proof", 0, 0, 0, 1, 0, 0 }), "explicit capability query never selects a target");
        Reject(() => draw("capabilities", new object[0]), "no selected target");
        Reject(() => draw("capabilities", new object[] { projector, console }), "extra arguments");
        Reject(() => draw("capabilities", new object[] { "HDR Display" }), "wrong target type");
        access = false; Reject(() => Query(lcd), "lost ownership"); access = true;
        same = false; Reject(() => Query(projector), "different construct"); same = true;
        owner = 0; Reject(() => Query(console), "unowned PB"); owner = 77;
        closed = true; Reject(() => Query(console), "closed target"); closed = false;
        registered.Remove(10); Reject(() => Query(projector), "unregistered caller"); registered[10] = caller;
        registered.Remove(20); Reject(() => Query(projector), "unregistered target"); registered[20] = projector;
        gateway.Server = false; Reject(() => Query(projector), "client execution"); gateway.Server = true;
        Pure("Rejected queries");

        // Known calibration is checked against the same immutable model lookup as rendering.
        var modelType = typeof(VRage.ModAPI.IMyEntity).GetProperty("Model").PropertyType;
        var model = DrawTestProxy.Make(modelType, (m, a) => m.Name == "get_AssetName" ? "Models/Cubes/Large/LCDPanel.mwm" : throw new Exception(m.Name));
        var rotation = DrawTestProxy.Make(typeof(IMyLcdSurfaceComponent), (m, a) => m.Name == "get_SelectedRotationIndex" ? 0 : throw new Exception(m.Name));
        var components = DrawTestProxy.Make(typeof(VRage.Game.Components.Interfaces.IMyEntityComponentContainer), (m, a) => { if (m.Name == "TryGet") { a[0] = rotation; return true; } throw new Exception(m.Name); });
        var calibrated = DrawTestProxy.Make(typeof(IMyTextPanel), (m, a) => m.Name switch
        {
            "get_EntityId" => 24L, "get_Closed" => false, "HasPlayerAccess" => true,
            "get_Model" => model, "get_Components" => components, "get_Name" => "ScreenArea", _ => throw new Exception(m.Name)
        });
        registered[24] = calibrated;
        Check(Query(calibrated).Item3 == (1 | 2 | 4 | 64), "calibrated vector LCD flag uses real model/surface/rotation");
        Pure("Calibrated query");

        draw("target", new[] { projector });
        var context = ((IDictionary)ClientReplicationTests.Field(session, "_drawContexts"))[10L];
        object selected = ClientReplicationTests.Field(context, "Target");
        ClientReplicationTests.SetField(context, "ScreenId", "not-created");
        var beforeDirty = ClientReplicationTests.Field(session, "_dirty");
        Query(lcd);
        Check(ReferenceEquals(selected, ClientReplicationTests.Field(context, "Target")), "explicit query leaves selected projector unchanged");
        result = (MyTuple<string, string, int>)draw("capabilities", new object[0]);
        Check(result.Item2 == "projector", "omitted target queries the existing selection");
        Check(((IDictionary)ClientReplicationTests.Field(session, "_scenes")).Count == 0 && Equals(beforeDirty, ClientReplicationTests.Field(session, "_dirty")), "selected queries still do not allocate or dirty scenes");
        result.Item2 = "tampered"; result.Item3 = 0;
        Check(Query(projector).Item2 == "projector" && Query(projector).Item3 == (1 | 8 | 32 | 64), "tuple mutation cannot alter capability state");
        return count;
    }
}
