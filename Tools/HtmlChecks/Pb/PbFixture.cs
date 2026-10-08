using System.Collections;
using System.Reflection;
using System.Text;
using Hdr.Html;
using HoloMap;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage;
using VRageMath;
using VRage.Game.GUI.TextPanel;

// Existing gateway doubles replace the game only; parsing, metrics, geometry,
// authorization, retained publication, control definitions and leases are real.
internal sealed class PbFixture : IHtmlPbBridge, IDisposable
{
    readonly ClientReplicationTests.GatewayScope gateway = new ClientReplicationTests.GatewayScope { Server = true };
    readonly Dictionary<long, object> entities = new Dictionary<long, object>();
    readonly Dictionary<string, object> properties = new Dictionary<string, object>();
    internal readonly HoloMapSession Core = new HoloMapSession();
    internal readonly IMyProgrammableBlock Caller;
    internal readonly IMyTerminalBlock Target;
    internal readonly IMyTextPanel NativeSource;
    internal bool SourceWorking = true, SourceAccess = true, SourceSame = true, FailNativeDraw;
    internal ContentType SourceContent = ContentType.SCRIPT;
    internal string SourceScript = "";
    internal Vector2 SourceSize = new Vector2(512, 256), SourceTexture = new Vector2(512, 512);
    internal readonly List<MySprite> NativeSprites = new List<MySprite>();
    internal readonly List<int> NativeFrameSizes = new List<int>();
    internal int NativeCommits, NativeDrawAttempts, NativeMeasures, SurfaceSettingWrites;
    internal readonly Func<string, object[], object> CoreDraw, CoreUi;
    internal bool Working = true, Enabled = true, TargetWorking = true, Access = true, SameConstruct = true, Closed;
    internal string Program = "html-pb-fixture";
    internal string TargetSubtype = "LargeProjector";
    internal VRage.Game.GUI.TextPanel.ContentType LcdContent = VRage.Game.GUI.TextPanel.ContentType.SCRIPT;
    internal string LcdScript = "HDR API";
    internal int DrawCalls, CostCalls, UiCalls, Mutations, TryRuns, AccessChecks;
    internal int FailSvgAt, SvgAttempts;
    internal bool FailAllSvg, FailCost;
    internal readonly List<string> Commands = new List<string>();
    internal bool Server { get { return gateway.Server; } set { gateway.Server = value; } }
    internal int AddedHtmlProperties, RemovedHtmlProperties;

    internal void CaptureHtmlProperty()
    {
        gateway.Install("TerminalControls", (method, args) =>
        {
            if (method.Name == "CreateProperty")
            {
                string id = (string)args[0]; Func<IMyTerminalBlock, Func<string, object[], object>> getter = null;
                var property = DrawTestProxy.Make(typeof(Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlProperty<Func<string, object[], object>>), (m, a) =>
                {
                    if (m.Name == "set_Getter") { getter = (Func<IMyTerminalBlock, Func<string, object[], object>>)a[0]; return null; }
                    if (m.Name == "get_Getter") return getter;
                    if (m.Name == "get_Id") return id;
                    if (m.Name == "GetValue") return getter((IMyTerminalBlock)a[0]);
                    throw new Exception("PB HTML terminal property: " + m.Name);
                });
                properties[id] = DrawTestProxy.Make(typeof(ITerminalProperty<Func<string, object[], object>>), (m, a) => m.Name == "GetValue" ? getter((IMyTerminalBlock)a[0]) : throw new Exception("PB registered property: " + m.Name));
                return property;
            }
            if (method.Name == "AddControl") { AddedHtmlProperties++; return null; }
            if (method.Name == "RemoveControl") { RemovedHtmlProperties++; properties.Remove("HDR.Html"); return null; }
            throw new Exception("PB HTML terminal controls: " + method.Name);
        });
    }
    internal Func<string, object[], object> HtmlEndpoint()
    {
        object property;
        if (!properties.TryGetValue("HDR.Html", out property)) return null;
        return ((ITerminalProperty<Func<string, object[], object>>)property).GetValue(Caller);
    }

    internal PbFixture(bool lcd = false)
    {
        Caller = (IMyProgrammableBlock)DrawTestProxy.Make(typeof(IMyProgrammableBlock), (m, a) =>
        {
            switch (m.Name)
            {
                case "get_EntityId": return 10L;
                case "get_OwnerId": return 77L;
                case "get_Closed": return Closed;
                case "get_IsWorking": return Working;
                case "get_Enabled": return Enabled;
                case "get_CubeGrid": return null;
                case "get_BlockDefinition": return Activator.CreateInstance(m.ReturnType);
                case "get_Model": return null;
                case "IsSameConstructAs": return ReferenceEquals(a[0], NativeSource) ? SourceSame : SameConstruct;
                case "HasPlayerAccess": return Access;
                case "GetPosition": return Vector3D.Zero;
                case "GetProperty": return properties.TryGetValue((string)a[0], out var p) ? p : null;
                case "TryRun": TryRuns++; return true;
                default: throw new Exception("PB fixture caller: " + m.Name);
            }
        });
        ((DrawTestProxy)(object)Caller).ProgramSource = () => Program;
        Target = (IMyTerminalBlock)DrawTestProxy.Make(lcd ? typeof(IMyTextPanel) : typeof(IMyProjector), (m, a) =>
        {
            switch (m.Name)
            {
                case "get_EntityId": return 20L;
                case "get_Closed": return false;
                case "get_IsWorking": return TargetWorking;
                case "get_Enabled": return TargetWorking;
                case "get_CustomName": return "HTML Display";
                case "get_CubeGrid": return null;
                case "get_BlockDefinition": return new VRage.ObjectBuilders.SerializableDefinitionId(typeof(Sandbox.Common.ObjectBuilders.MyObjectBuilder_Projector), TargetSubtype);
                case "get_Model": return null;
                case "HasPlayerAccess": AccessChecks++; return Access;
                case "IsSameConstructAs": return SameConstruct;
                case "GetPosition": return new Vector3D(1, 0, 0);
                case "get_ContentType": return LcdContent;
                case "get_Script": return LcdScript;
                case "get_SurfaceSize": case "get_TextureSize": return new Vector2(512, 512);
                default: throw new Exception("PB fixture display: " + m.Name);
            }
        });
        NativeSource = (IMyTextPanel)DrawTestProxy.Make(typeof(IMyTextPanel), (m, a) =>
        {
            switch(m.Name)
            {
                case "get_EntityId": return 30L;
                case "get_Closed": return false;
                case "get_IsWorking": return SourceWorking;
                case "get_Enabled": return SourceWorking;
                case "HasPlayerAccess": return SourceAccess;
                case "get_Model": return null;
                case "get_ContentType": return SourceContent;
                case "get_Script": return SourceScript;
                case "get_SurfaceSize": return SourceSize;
                case "get_TextureSize": return SourceTexture;
                case "get_ScriptBackgroundColor": case "get_BackgroundColor": return new Color(17, 34, 51, 255);
                case "MeasureStringInPixels": NativeMeasures++; EqualDebugFont((string)a[1]); return new Vector2(((StringBuilder)a[0]).Length * 14 * (float)a[2], 28 * (float)a[2]);
                case "DrawFrame":
                    NativeDrawAttempts++; if (FailNativeDraw) throw new InvalidOperationException("injected native frame uncertainty");
                    return new MySpriteDrawFrame(frame => { NativeCommits++; NativeSprites.Clear(); frame.AddToList(NativeSprites); NativeFrameSizes.Add(NativeSprites.Count); });
                default:
                    if(m.Name.StartsWith("set_", StringComparison.Ordinal))SurfaceSettingWrites++;
                    throw new Exception("Native source fixture: " + m.Name);
            }
        });
        entities[10] = Caller; entities[20] = Target; entities[30] = NativeSource;
        gateway.Install("Entities", (m, a) => m.Name == "GetEntityById" && entities.TryGetValue((long)a[0], out var block) ? block : null);
        gateway.Install("TerminalActionsHelper", (m, a) =>
        {
            if (m.Name != "GetTerminalSystemForGrid") throw new Exception("PB fixture terminal helper: " + m.Name);
            return DrawTestProxy.Make(m.ReturnType, (method, args) =>
            {
                if (method.Name != "GetBlocksOfType") throw new Exception("PB fixture terminal: " + method.Name);
                var output = (IList)args[0]; var filter = (Delegate)args[1];
                if ((bool)filter.DynamicInvoke(Target)) output.Add(Target);
                return null;
            });
        });
        CoreDraw = (Func<string, object[], object>)ClientReplicationTests.Call(Core, "DrawEndpoint", Caller);
        CoreUi = (Func<string, object[], object>)ClientReplicationTests.Call(Core, "UiEndpoint", Caller);
        Property("HDR.Draw", (op, args) => DispatchCoreDraw(op, args, false));
        Property("HDR.UI", (op, args) => Ui(op, args));
    }

    internal void Property(string id, Func<string, object[], object> endpoint)
    { properties[id] = DrawTestProxy.Make(typeof(ITerminalProperty<Func<string, object[], object>>), (m, a) => m.Name == "GetValue" ? endpoint : throw new Exception("PB fixture property: " + m.Name)); }
    public bool Ready { get { return !Closed && entities.TryGetValue(10, out var c) && ReferenceEquals(c, Caller) && entities.TryGetValue(20, out var t) && ReferenceEquals(t, Target); } }
    public object Draw(string operation, params object[] args)
    { return DispatchCoreDraw(operation, args, true); }
    object DispatchCoreDraw(string operation, object[] args, bool selectDefault)
    {
        DrawCalls++; Commands.Add("draw:" + operation);
        if (operation == "geometry-cost") { CostCalls++; if (FailCost) throw new ArgumentException("injected cost preflight failure"); }
        if (selectDefault && operation != "version" && operation != "measure-text" && operation != "geometry-cost" && operation != "capabilities" && operation != "finddisplay") CoreDraw("target", new object[] { Target });
        if (operation == "svg") { SvgAttempts++; if (FailAllSvg || SvgAttempts == FailSvgAt) throw new ArgumentException("injected SVG emit failure"); }
        if (operation != "version" && operation != "measure-text" && operation != "geometry-cost" && operation != "capabilities" && operation != "budget-settings" && operation != "layer-state" && operation != "finddisplay" && operation != "target") Mutations++;
        return CoreDraw(operation, args);
    }
    public object Ui(string operation, params object[] args)
    {
        UiCalls++; Commands.Add("ui:" + operation); CoreDraw("target", new object[] { Target });
        if (operation != "version" && operation != "capabilities" && operation != "get-value" && operation != "poll-value-events") Mutations++;
        return CoreUi(operation, args);
    }
    internal UiDisplay Display { get { return (UiDisplay)ClientReplicationTests.Call(Core, "GetUiDisplay", 10L, 20L); } }
    internal IDictionary Items { get { var s = (IDictionary)ClientReplicationTests.Field(Core, "_scenes"); return s.Contains(20L) ? (IDictionary)ClientReplicationTests.Field(s[20L], "Items") : new Hashtable(); } }
    internal IDictionary Layers { get { var s = (IDictionary)ClientReplicationTests.Field(Core, "_scenes"); return s.Contains(20L) ? (IDictionary)ClientReplicationTests.Field(s[20L], "Layers") : new Hashtable(); } }
    internal Dictionary<string, string> Sources()
    {
        var result = new Dictionary<string, string>();
        foreach (DictionaryEntry pair in Items) result[(string)pair.Key] = (string)ClientReplicationTests.Field(pair.Value, "SvgSource");
        return result;
    }
    internal void ReplaceCaller() { entities[10] = DrawTestProxy.Make(typeof(IMyProgrammableBlock), (m, a) => m.Name == "get_EntityId" ? 10L : throw new Exception("Retired caller unexpectedly queried: " + m.Name)); }
    internal void ReplaceTarget() { entities[20] = DrawTestProxy.Make(typeof(IMyProjector), (m, a) => m.Name == "get_EntityId" ? 20L : throw new Exception("Retired display unexpectedly queried: " + m.Name)); }
    internal void ReplaceSource() { entities[30] = DrawTestProxy.Make(typeof(IMyTextPanel), (m, a) => m.Name == "get_EntityId" ? 30L : throw new Exception("Replacement source unexpectedly queried: " + m.Name)); }
    internal void LocalHostUtilities()
    {
        gateway.Install("Utilities", (m,a) =>
        {
            if(m.Name=="get_IsDedicated")return false;
            if(m.Name=="SerializeToBinary")return gateway.Serialize(a[0]);
            if(m.Name=="RegisterMessageHandler"||m.Name=="UnregisterMessageHandler"||m.Name=="SendModMessage"||m.Name=="add_MessageEntered"||m.Name=="remove_MessageEntered"||m.Name=="ShowMessage")return null;
            throw new Exception("Native local-host utility: "+m.Name);
        });
    }
    internal IDictionary Screens { get { var s=(IDictionary)ClientReplicationTests.Field(Core,"_scenes"); return s.Contains(20L)?(IDictionary)ClientReplicationTests.Field(s[20L],"Screens"):new Hashtable(); } }
    static void EqualDebugFont(string font) { if(font!="Debug")throw new Exception("Native source measured unsupported font " + font); }
    internal bool Click(UiWidget w) { return (bool)InvokeUnique("UiClickControl", 4, new object[] { 10L, 20L, w.Id, 77L }); }
    internal long Begin(UiWidget w)
    {
        var d = Display; var value = d.Values.Find(v => v.Id == w.Control.ValueId);
        object[] args = { 10L, 20L, w.Id, 77L, d.Revision, value.Revision, w.Control.SourceRevision, 0L };
        if (!(bool)InvokeUnique("UiTryBeginValueLeaseScalar", 8, args)) throw new Exception("Actual core range lease refused.");
        return (long)args[7];
    }
    internal MyTuple<bool, double, long> Move(long lease, double requested)
    {
        object[] args = { lease, 77L, requested, new MyTuple<bool, double, long>() };
        if (!(bool)InvokeUnique("UiTryCommitValueLeaseScalar", 4, args)) throw new Exception("Actual core range lease commit refused.");
        return (MyTuple<bool, double, long>)args[3];
    }
    internal bool LeaseValid(long lease) { return (bool)ClientReplicationTests.Call(Core, "UiValidateValueLease", lease, 77L); }
    object InvokeUnique(string name, int arity, object[] args)
    {
        var method = typeof(HoloMapSession).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).Single(m => m.Name == name && m.GetParameters().Length == arity);
        try { return method.Invoke(Core, args); } catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    public void Dispose() { gateway.Dispose(); }
}
