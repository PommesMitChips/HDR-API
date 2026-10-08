// Whole PB script; paste directly into a programmable block. No appended helper.
// ALPHA: HDR API 0.9.10+ and HDR HTML frontend 0.1.1 world mods are required.
// Name one dedicated LCD / Console / Projector "HTML Display" on this construct.
// Physical LCD: manually select Content -> HDR API before running demo.
// Display is plugin-free. BOTH buttons and ranges require the viewer's existing
// HDR Client Renderer 0.9.13+; this script does not install or enable plugins.
// Run demo explicitly, then use the existing HDR persistent Use/pointer controls.
// Commands: demo, clear, status, set 60 (set60 also accepted).
// The level is a script variable only; no terminal actions or gameplay writes.

const string DisplayName = "HTML Display";
const double Width = 480, Height = 280, UnitsPerPixel = .005;
const string Markup = @"<main id='panel'>
 <h2>HDR HTML / PB</h2>
 <p>Server-owned UI</p>
 <p>Level: <span>{{level}}</span></p>
 <label>Level</label>
 <input id='level' type='range' min='0' max='100' step='1' value='60' data-bind='level' data-action='level'/>
 <button id='reset' data-action='reset'>Reset 60</button>
 <p id='note'>Events polled in Update10.</p>
</main>";
const string Stylesheet = @"main {width:440px;height:240px;padding:12px;background-color:#123442;color:#00d0cd;font-size:14px;line-height:1.3;}
h2 {font-size:20px;margin-bottom:8px;}
p {margin-bottom:8px;}
input {width:220px;height:24px;}
button {width:100px;height:28px;padding:4px;background-color:#245a67;margin-top:6px;}";

Func<string, object[], object> _html, _draw;
IMyTerminalBlock _target;
long _document, _lastPlayer, _lastValueRevision;
double _level = -1;

public Program() { Runtime.UpdateFrequency = UpdateFrequency.None; }

object Html(string operation, params object[] arguments)
{
    if (_html == null)
    {
        var property = Me.GetProperty("HDR.Html");
        if (property == null) throw new Exception("Requires the HDR HTML frontend world mod.");
        _html = property.As<Func<string, object[], object>>().GetValue(Me);
        if (_html == null || (string)_html("version", new object[0]) != "HDR.HtmlPB/0.1")
            throw new Exception("Requires compatible HDR.HtmlPB/0.1.");
    }
    return _html(operation, arguments);
}

object Draw(string operation, params object[] arguments)
{
    if (_draw == null)
    {
        var property = Me.GetProperty("HDR.Draw");
        if (property == null) throw new Exception("Requires HDR API 0.9.10+.");
        _draw = property.As<Func<string, object[], object>>().GetValue(Me);
        if (_draw == null) throw new Exception("HDR.Draw is unavailable.");
    }
    return _draw(operation, arguments);
}

void Setup()
{
    Clear();
    _html = null; _draw = null;
    _target = GridTerminalSystem.GetBlockWithName(DisplayName);
    if (_target == null) throw new Exception("Name a dedicated LCD / Console / Projector '" + DisplayName + "'.");
    var capability = (VRage.MyTuple<string, string, int>)Draw("capabilities", _target);
    if (capability.Item1 != "HDR.DisplayCapabilities/1" || (capability.Item3 & 1) == 0)
        throw new Exception("HTML Display is not an admitted HDR display.");

    if (capability.Item2 == "lcd")
    {
        // Explicit demo configuration of the core logical canvas only.
        // This does not change the panel's physical Content mode or selected script.
        var panel = _target as IMyTextPanel;
        if (panel == null) throw new Exception("LCD target must provide the physical panel surface.");
        Vector2 surface = panel.SurfaceSize;
        if (float.IsNaN(surface.X) || float.IsInfinity(surface.X) || surface.X <= 0 ||
            float.IsNaN(surface.Y) || float.IsInfinity(surface.Y) || surface.Y <= 0)
            throw new Exception("LCD SurfaceSize is invalid.");
        // Match the physical aspect and fit the authored UI inside it, leaving
        // letterbox space where needed instead of stretching 480x280 to a square.
        double logicalPerSurfacePixel = Math.Max(Width / surface.X, Height / surface.Y);
        Draw("target", _target);
        Draw("lcd", surface.X * logicalPerSurfacePixel * UnitsPerPixel,
            surface.Y * logicalPerSurfacePixel * UnitsPerPixel);
    }
    else if ((capability.Item3 & 8) == 0)
        throw new Exception("This demo needs an LCD / Console / Projector.");

    // Top-left model position chosen so the authored viewport is centred at origin.
    // Console/Projector view/range/table clipping remain the existing core settings.
    MatrixD topLeft = MatrixD.CreateTranslation(-Width * UnitsPerPixel / 2,
        Height * UnitsPerPixel / 2, 0);
    _document = (long)Html("bind", _target, Markup, Stylesheet,
        Width, Height, topLeft, UnitsPerPixel);
    _level = -1; _lastPlayer = 0; _lastValueRevision = 0;
    SetLevel(60);
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    PrintStatus();
}

void Clear()
{
    Runtime.UpdateFrequency = UpdateFrequency.None;
    if (_document != 0 && _html != null)
        try { Html("destroy", _document); } catch { }
    _document = 0; _target = null;
    // Never call HDR.Draw clear or HDR.UI clear: unrelated declarations may exist.
}

void SetLevel(double requested)
{
    if (double.IsNaN(requested) || double.IsInfinity(requested))
        throw new Exception("Level must be finite.");
    double value = Math.Max(0, Math.Min(100, Math.Floor(requested + .5)));
    if (_level == value) return;
    _level = value; // Explicit assignment to this PB's variable.
    Html("data", _document, "level", _level.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
}

void PollEvents()
{
    var events = (VRage.MyTuple<string, string, string, VRage.MyTuple<double, long, long>>[])Html("poll-events", _document);
    foreach (var item in events)
    {
        if (item.Item1 == "change" && item.Item2 == "level" && item.Item3 == "level")
            SetLevel(item.Item4.Item1); // Canonical value, not an inferred pointer position.
        else if (item.Item1 == "click" && item.Item2 == "reset" && item.Item3 == "reset")
            SetLevel(60);
        else continue;
        _lastValueRevision = item.Item4.Item2; // Core numeric revision; buttons may use zero.
        _lastPlayer = item.Item4.Item3;
    }
}

void PrintStatus()
{
    var state = (VRage.MyTuple<string, long, long, VRage.MyTuple<bool, bool, string>, long>)Html("status", _document);
    Echo("HTML PB / " + state.Item1 + " / ALPHA");
    Echo("Desired / server published: " + state.Item2 + " / " + state.Item3);
    Echo("Core ready: " + state.Item4.Item1 + "; pending: " + state.Item4.Item2);
    Echo("Layout builds: " + state.Item5 + "; level: " + _level.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
    Echo("Last core value revision/player: " + _lastValueRevision + " / " + _lastPlayer);
    if (!string.IsNullOrEmpty(state.Item4.Item3)) Echo(state.Item4.Item3);
    Echo("Publication is not a viewer/GPU acknowledgement.");
}

public void Main(string argument, UpdateType source)
{
    try
    {
        string command = (argument ?? "").Trim().ToLowerInvariant();
        if (command == "clear") { Clear(); Echo("This demo document cleared."); return; }
        if (command == "demo") { Setup(); return; }
        if (_document == 0) { Echo("Run demo explicitly. Commands: demo, clear, status, set 60."); return; }
        var current = GridTerminalSystem.GetBlockWithName(DisplayName);
        if (_target == null || current == null || current.EntityId != _target.EntityId)
            throw new Exception("Display changed. Run demo explicitly to bind again.");
        if (command.StartsWith("set", StringComparison.Ordinal))
        {
            double requested;
            if (!double.TryParse(command.Substring(3).Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out requested))
                throw new Exception("Use set 60 or another finite number.");
            SetLevel(requested);
        }
        else if (command != "" && command != "status")
        {
            Echo("Commands: demo, clear, status, set 60."); return;
        }
        PollEvents(); PrintStatus();
    }
    catch (Exception error)
    {
        // No automatic bind/redraw after source, caller, target or endpoint invalidation.
        Runtime.UpdateFrequency = UpdateFrequency.None;
        Echo(error.Message); Echo("Run demo explicitly after correcting the cause.");
    }
}
