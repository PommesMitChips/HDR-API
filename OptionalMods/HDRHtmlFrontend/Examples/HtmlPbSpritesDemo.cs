// Whole PB script; paste directly into a programmable block. No appended helper.
// ALPHA: HDR API 0.9.10+ and HDR HTML frontend 0.1.1 world mods are required.
// Name a dedicated physical LCD "HTML Sprite Source" on this PB's construct.
// Manually set that source to ContentType.SCRIPT, with no selected surface script
// (NONE), and keep it exclusively owned by this script. No automatic mode/BG writes.
// demo / lcd: draw directly on the source LCD, plugin-free.
// world: relay its real native texture to Console/Projector "HTML Display".
// World relay requires the EXISTING HDR Client Renderer 0.9.13+ on EVERY viewer.
// No new plugin/DLL update, virtual LCD, browser rasterizer or low-resolution fallback.
// Other commands: status, clear, set 60, press X Y, release X Y, cancel.
// press/release are manual synthetic coordinate tests entered through the PB terminal.
// A real GUI/touch/eye adapter must already own its input before forwarding coordinates.
// This script reads no global mouse/weapon controls and performs no gameplay writes.

const string SourceName = "HTML Sprite Source", AnchorName = "HTML Display";
const double UnitsPerPixel = .005;
const string Markup = @"<main>
 <h2>Native HTML</h2>
 <p>Level: <span>{{level}}</span></p>
 <div class='controls'>
  <input id='level' type='range' min='0' max='100' step='1' value='60' data-bind='level' data-action='level'/>
  <button id='reset' data-action='reset'>Reset 60</button>
 </div>
 <p>Owned pointer input</p>
</main>";
// Omit font-family: bind-sprites selects the source LCD's measured Debug profile.
// No rounded corners, stroke paint or assets. Actual SurfaceSize is the viewport.
const string Stylesheet = @"main {height:100%;padding:12px;background-color:#123442;color:#00d0cd;font-size:14px;line-height:1.2;}
h2 {font-size:20px;margin:0;}
p {margin:4px 0;}
.controls {display:flex;gap:8px;align-items:center;height:28px;}
input {height:24px;flex-grow:1;min-width:40px;}
button {width:90px;height:24px;background-color:#245a67;}";

Func<string, object[], object> _html;
IMyTextPanel _source;
IMyTerminalBlock _anchor;
long _document, _lastPointerRevision, _lastPlayer;
double _level = -1;
bool _world;

public Program() { Runtime.UpdateFrequency = UpdateFrequency.None; }

object Html(string operation, params object[] arguments)
{
    if (_html == null)
    {
        var property = Me.GetProperty("HDR.Html");
        if (property == null) throw new Exception("Requires HDR HTML frontend.");
        _html = property.As<Func<string, object[], object>>().GetValue(Me);
        if (_html == null || (string)_html("version", new object[0]) != "HDR.HtmlPB/0.1")
            throw new Exception("Requires compatible HDR.HtmlPB/0.1.");
    }
    return _html(operation, arguments);
}

void Setup(bool world)
{
    Clear(); _html = null;
    _source = GridTerminalSystem.GetBlockWithName(SourceName) as IMyTextPanel;
    if (_source == null) throw new Exception("Name a dedicated physical LCD '" + SourceName + "'.");
    if (_source.ContentType != VRage.Game.GUI.TextPanel.ContentType.SCRIPT || !string.IsNullOrEmpty(_source.Script))
        throw new Exception("Manually set source Content to SCRIPT with no selected script (NONE).");
    Vector2 size = _source.SurfaceSize;
    if (float.IsNaN(size.X) || float.IsInfinity(size.X) || size.X < 192 ||
        float.IsNaN(size.Y) || float.IsInfinity(size.Y) || size.Y < 128)
        throw new Exception("This demo needs a source SurfaceSize of at least 192x128.");
    _anchor = world ? GridTerminalSystem.GetBlockWithName(AnchorName) : _source;
    if (_anchor == null || world && !(_anchor is IMyProjector))
        throw new Exception("World relay needs a Console / Projector named '" + AnchorName + "'.");
    MatrixD topLeft = MatrixD.CreateTranslation(-size.X * UnitsPerPixel / 2,
        size.Y * UnitsPerPixel / 2, 0);
    // Explicit true is the caller's promise of exclusive source-surface ownership.
    // The frontend checks its own claims; it cannot detect arbitrary outside writers.
    _document = (long)Html("bind-sprites", _anchor, _source, Markup, Stylesheet,
        topLeft, UnitsPerPixel, 0, true);
    _world = world; _level = -1; _lastPointerRevision = 0; _lastPlayer = 0;
    SetLevel(60); Runtime.UpdateFrequency = UpdateFrequency.Update10;
    PrintStatus();
}

void Clear()
{
    Runtime.UpdateFrequency = UpdateFrequency.None;
    if (_document != 0 && _html != null)
    {
        try { Html("pointer-cancel", _document); } catch { }
        try { Html("destroy", _document); } catch { }
    }
    _document = 0; _source = null; _anchor = null;
    // No HDR.Draw/UI global clear and no source mode, background or block-name changes.
}

void SetLevel(double requested)
{
    if (double.IsNaN(requested) || double.IsInfinity(requested)) throw new Exception("Level must be finite.");
    double value = Math.Max(0, Math.Min(100, Math.Floor(requested + .5)));
    if (_level == value) return;
    _level = value;
    Html("data", _document, "level", value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
}

void PollEvents()
{
    var events = (VRage.MyTuple<string, string, string, VRage.MyTuple<double, long, long>>[])Html("poll-events", _document);
    foreach (var item in events)
    {
        if (item.Item1 == "change" && item.Item2 == "level" && item.Item3 == "level") SetLevel(item.Item4.Item1);
        else if (item.Item1 == "click" && item.Item2 == "reset" && item.Item3 == "reset") SetLevel(60);
        else continue;
        _lastPointerRevision = item.Item4.Item2; // Native published document revision, not a core value revision.
        _lastPlayer = item.Item4.Item3; // Cooperative native PB events use player identity 0.
    }
}

// Only a caller-owned GUI/touch/eye provider, or the explicit synthetic terminal
// tests below, should call this. Coordinates refer to source SurfaceSize pixels.
void OnOwnedPointer(double x, double y, bool held)
{ Html("pointer", _document, x, y, held); }

void PrintStatus()
{
    var state = (VRage.MyTuple<string, long, long, VRage.MyTuple<bool, bool, string>, long>)Html("status", _document);
    Echo("Native HTML / " + (_world ? "world texture relay" : "source LCD") + " / ALPHA");
    Echo("Desired / server published: " + state.Item2 + " / " + state.Item3);
    Echo("Ready: " + state.Item4.Item1 + "; pending: " + state.Item4.Item2 + "; layouts: " + state.Item5);
    Echo("Source pixels: " + _source.SurfaceSize + "; Debug font; level: " + _level.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
    Echo("Native event revision/player: " + _lastPointerRevision + " / " + _lastPlayer);
    if (!string.IsNullOrEmpty(state.Item4.Item3)) Echo(state.Item4.Item3);
    if (_world) Echo("Every viewer needs existing Client Renderer 0.9.13+. Relay RGB is opaque.");
    Echo("Server publication is not a viewer/GPU acknowledgement.");
}

public void Main(string argument, UpdateType source)
{
    try
    {
        string command = (argument ?? "").Trim().ToLowerInvariant();
        if (command == "clear") { Clear(); Echo("This native document and owned relay cleared."); return; }
        if (command == "demo" || command == "lcd") { Setup(false); return; }
        if (command == "world") { Setup(true); return; }
        if (_document == 0) { Echo("Run lcd/demo or world explicitly. Commands: status, clear, set 60, press/release X Y, cancel."); return; }
        var currentSource = GridTerminalSystem.GetBlockWithName(SourceName);
        var currentAnchor = _world ? GridTerminalSystem.GetBlockWithName(AnchorName) : currentSource;
        if (_source == null || currentSource == null || currentSource.EntityId != _source.EntityId ||
            _anchor == null || currentAnchor == null || currentAnchor.EntityId != _anchor.EntityId)
            throw new Exception("Source or anchor changed; bind again explicitly.");
        if (command == "cancel") Html("pointer-cancel", _document);
        else if (command.StartsWith("set", StringComparison.Ordinal))
        {
            double requested;
            if (!double.TryParse(command.Substring(3).Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out requested)) throw new Exception("Use set 60.");
            SetLevel(requested);
        }
        else if (command.StartsWith("press ", StringComparison.Ordinal) || command.StartsWith("release ", StringComparison.Ordinal))
        {
            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            double x, y;
            if (parts.Length != 3 || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out x) || !double.TryParse(parts[2],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y))
                throw new Exception("Use press X Y or release X Y in source pixels.");
            OnOwnedPointer(x, y, parts[0] == "press");
        }
        else if (command != "" && command != "status")
        { Echo("Commands: demo/lcd, world, status, clear, set 60, press/release X Y, cancel."); return; }
        PollEvents(); PrintStatus();
    }
    catch (Exception error)
    {
        Runtime.UpdateFrequency = UpdateFrequency.None;
        Echo(error.Message); Echo("Correct the cause, then run lcd/demo or world explicitly.");
    }
}
