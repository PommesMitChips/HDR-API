// Paste this class inside a PB script. Uses standard PB imports plus Sandbox.ModAPI.Interfaces.
// All crossings use game/standard-library types; no mod assembly reference is required.
public sealed class HdrIngameApi
{
    public const int Supported = 1, NativeLcd = 2, CalibratedVectorLcd = 4,
        Floating3D = 8, TableVolume = 16, ProjectedSurfaces = 32, Ui = 64;
    Func<string, object[], object> _draw;
    public bool IsActive { get { return _draw != null; } }

    public bool Activate(IMyTerminalBlock programmableBlock)
    {
        _draw = null;
        if (programmableBlock == null) return false;
        var property = programmableBlock.GetProperty("HDR.Draw");
        if (property == null) return false;
        var typed = property.As<Func<string, object[], object>>();
        if (typed == null) return false;
        var endpoint = typed.GetValue(programmableBlock);
        if (endpoint == null || (string)endpoint("version", new object[0]) != "HDR.Draw/1") return false;
        _draw = endpoint;
        return true;
    }

    // Select is explicit. FindDisplay and other existing command aliases keep their semantics.
    public void Select(IMyTerminalBlock target) { Call("target", target); }
    public IMyTerminalBlock FindDisplay(string exactName) { return (IMyTerminalBlock)Call("finddisplay", exactName); }
    public object Call(string command, params object[] arguments)
    {
        if (_draw == null) throw new InvalidOperationException("Call HdrIngameApi.Activate(Me) first.");
        return _draw(command, arguments);
    }
    // Expected availability/validation failures become a result for the caller to Echo.
    // Call keeps its original throwing semantics. Unexpected provider faults propagate.
    public bool TryCall(string command, object[] arguments, out object result, out string reason)
    {
        result = null;
        reason = "";
        if (_draw == null) { reason = "Requires mod: HDR API."; return false; }
        try { result = _draw(command, arguments); return true; }
        catch (ArgumentException error) { reason = error.Message; return false; }
        catch (InvalidOperationException error) { reason = error.Message; return false; }
    }
    public VRage.MyTuple<string, string, int> Capabilities(IMyTerminalBlock target)
    {
        var result = (VRage.MyTuple<string, string, int>)Call("capabilities", target);
        if (result.Item1 != "HDR.DisplayCapabilities/1" || result.Item3 < 0 || (result.Item3 & ~127) != 0)
            throw new InvalidOperationException("Unsupported display capability protocol.");
        return result;
    }
    public bool TryCapabilities(IMyTerminalBlock target, out VRage.MyTuple<string, string, int> capabilities, out string reason)
    {
        capabilities = new VRage.MyTuple<string, string, int>();
        object result;
        if (!TryCall("capabilities", new object[] { target }, out result, out reason)) return false;
        if (!(result is VRage.MyTuple<string, string, int>)) { reason = "Unsupported display capability response."; return false; }
        var value = (VRage.MyTuple<string, string, int>)result;
        if (value.Item1 != "HDR.DisplayCapabilities/1" || value.Item3 < 0 || (value.Item3 & ~127) != 0)
        { reason = "Unsupported display capability protocol."; return false; }
        capabilities = value;
        return true;
    }
    // Item1: known feature; Item2: registered/available locally; Item3: explanation.
    // Registration does NOT prove GPU/capture readiness. Dedicated servers cannot know
    // remote viewers' plugins; never use this result to gate replicated declarations.
    public bool TryPluginStatus(string feature, out VRage.MyTuple<bool, bool, string> status, out string reason)
    {
        status = new VRage.MyTuple<bool, bool, string>();
        object result;
        if (!TryCall("plugin-status", new object[] { feature }, out result, out reason)) return false;
        if (!(result is VRage.MyTuple<bool, bool, string>)) { reason = "Unsupported local plugin status response."; return false; }
        status = (VRage.MyTuple<bool, bool, string>)result;
        return true;
    }
}
