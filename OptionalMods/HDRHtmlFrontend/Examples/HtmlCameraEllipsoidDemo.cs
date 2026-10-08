// Whole PB script; paste directly into a programmable block. No appended helper.
// ALPHA: HDR API 0.9.11+ / scene 20 and HDR HTML frontend 0.2.0 are required.
// Name a dedicated Console or Projector "HTML Display" on this construct.
// Name two working physical camera blocks "HDR Camera 1" and "HDR Camera 2".
// Each viewer needs HDR Client Renderer 0.9.13+ (current package 0.9.14) for capture;
// the interacting viewer also needs it for automatic persistent pointer controls.
// Run demo explicitly. Commands: demo, previous, next, status, clear.
// Previous/Next HTML buttons change only #pov's source ID. No browser JavaScript,
// arbitrary virtual POV, native LCD buffer, gameplay action or plugin install.
// Server publication/source status is not live viewer/GPU acceptance.

const string DisplayName = "HTML Display", ScreenId = "htmlcamera";
const double Width = 640, Height = 360, UnitsPerPixel = .005;
const string Markup = @"<main id='panel'>
 <h2>External camera</h2>
 <div id='pov'></div>
 <div id='controls'>
  <button id='previous' data-action='previous'>Previous</button>
  <button id='next' data-action='next'>Next</button>
 </div>
</main>";
const string Stylesheet = @"main {width:608px;height:328px;padding:16px;background-color:#123442;color:#ffffff;font-size:14px;}
h2 {font-size:20px;margin:0;margin-bottom:12px;}
#pov {width:608px;height:228px;overflow:hidden;margin-bottom:12px;}
#controls {display:flex;flex-direction:row;gap:12px;height:40px;}
button {width:144px;height:32px;padding:4px;background-color:#245a67;}";

Func<string, object[], object> _html, _draw;
IMyTerminalBlock _target;
IMyCameraBlock[] _cameras;
long _document;
int _cameraIndex;
bool _ownsScreen;

public Program() { Runtime.UpdateFrequency = UpdateFrequency.None; }

object Html(string operation, params object[] arguments)
{
    if (_html == null)
    {
        var property = Me.GetProperty("HDR.Html");
        if (property == null) throw new Exception("Requires HDR HTML frontend 0.2.0.");
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
        if (property == null) throw new Exception("Requires HDR API 0.9.11+.");
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
    if (_target == null) throw new Exception("Name a dedicated Console/Projector '" + DisplayName + "'.");
    var capability = (VRage.MyTuple<string, string, int>)Draw("capabilities", _target);
    if (capability.Item1 != "HDR.DisplayCapabilities/1" || (capability.Item3 & 8) == 0)
        throw new Exception("HTML Display must be an admitted Console/Projector, not a physical LCD.");
    _cameras = new IMyCameraBlock[2];
    for (int i = 0; i < _cameras.Length; i++)
    {
        string name = "HDR Camera " + (i + 1);
        _cameras[i] = GridTerminalSystem.GetBlockWithName(name) as IMyCameraBlock;
        if (_cameras[i] == null || !_cameras[i].IsSameConstructAs(Me) || !_cameras[i].IsWorking)
            throw new Exception("Requires a working physical camera named '" + name + "' on this construct.");
    }

    Draw("target", _target);
    // Dedicated demo anchor: disable only a Console's table envelope so the
    // bounded shell fits. Existing range/view and the 25 m safety limit apply.
    if ((capability.Item3 & 16) != 0) Draw("volume", false);
    Draw("screen", ScreenId, MatrixD.CreateTranslation(0, 2, 0),
        Width * UnitsPerPixel, Height * UnitsPerPixel,
        Width * UnitsPerPixel, Height * UnitsPerPixel);
    _ownsScreen = true;
    // An ellipsoid PATCH: unequal radii and finite angular spans, viewed outside.
    Draw("screen-ellipsoid", new Vector3D(2.4, 1.5, 2.0), 1.5, 1.1, "outside");
    Draw("screen-mapping", "angular");
    Draw("screen-aspect", "stretch");
    Draw("screen-two-sided", true, 1, .35);
    Draw("screen-background", "transparent");
    Draw("screen-renderer", "vector");
    Draw("screen-quality", .02);
    // BindScreen's pose locates the document CENTER in the screen canvas.
    _document = (long)Html("bind-screen", _target, ScreenId, Markup, Stylesheet,
        Width, Height, MatrixD.Identity, UnitsPerPixel);
    _cameraIndex = -1;
    SelectCamera(0);
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    PrintStatus();
}

void SelectCamera(int requested)
{
    int index = (requested % _cameras.Length + _cameras.Length) % _cameras.Length;
    if (index == _cameraIndex) return;
    string sourceId = _cameras[index].EntityId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    var settings = new VRage.MyTuple<string, object[]>[] {
        new VRage.MyTuple<string, object[]>("refresh", new object[] { 6.0 }),
        new VRage.MyTuple<string, object[]>("panorama", new object[] { 105.0, 8.0, 1.0, 512 }),
        new VRage.MyTuple<string, object[]>("quality", new object[] { "normal" })
    };
    // Same document, node, provider and settings; only the physical camera ID changes.
    // AttachSource commits the declaration synchronously; readiness may be Pending.
    if (!(bool)Html("attach-source", _document, "pov", "camera-panorama", sourceId, settings))
        throw new Exception("Camera attachment was rejected. Inspect status; run demo after fixing the cause.");
    _cameraIndex = index;
}

void PollEvents()
{
    var events = (VRage.MyTuple<string, string, string, VRage.MyTuple<double, long, long>>[])Html("poll-events", _document);
    foreach (var item in events)
    {
        if (item.Item1 != "click") continue;
        if (item.Item2 == "previous" && item.Item3 == "previous") SelectCamera(_cameraIndex - 1);
        else if (item.Item2 == "next" && item.Item3 == "next") SelectCamera(_cameraIndex + 1);
    }
}

void PrintStatus()
{
    var state = (VRage.MyTuple<string, long, long, VRage.MyTuple<bool, bool, string>, long>)Html("status", _document);
    var source = (VRage.MyTuple<bool, string>)Html("source-status", _document, "pov");
    Echo("HTML camera ellipsoid / ALPHA");
    Echo("Selected: " + _cameras[_cameraIndex].CustomName + " [" + _cameras[_cameraIndex].EntityId + "]");
    Echo("Source ready: " + source.Item1 + "; " + source.Item2);
    Echo("Desired / server published: " + state.Item2 + " / " + state.Item3);
    Echo("Pending: " + state.Item4.Item2 + "; layout builds: " + state.Item5);
    if (!string.IsNullOrEmpty(state.Item4.Item3)) Echo(state.Item4.Item3);
    Echo("Use the HDR persistent viewer for the HTML buttons.");
    Echo("Renderer 0.9.13+ required (current 0.9.14); status is not GPU acceptance.");
}

void Clear()
{
    Runtime.UpdateFrequency = UpdateFrequency.None;
    if (_document != 0 && _html != null)
        try { Html("destroy", _document); } catch { }
    _document = 0;
    if (_ownsScreen && _draw != null && _target != null)
    {
        try {
            Draw("target", _target);
            if ((bool)Draw("screen-exists", ScreenId)) {
                Draw("screen", ScreenId); Draw("screen-remove");
            }
        } catch { }
    }
    _ownsScreen = false; _target = null; _cameras = null;
    // Cleanup only this owned child screen/document; never clear the whole scene/UI.
}

public void Main(string argument, UpdateType source)
{
    try
    {
        string command = (argument ?? "").Trim().ToLowerInvariant();
        if (command == "clear") { Clear(); Echo("This demo document/screen cleared."); return; }
        if (command == "demo") { Setup(); return; }
        if (_document == 0) { Echo("Run demo explicitly. Commands: demo, previous, next, status, clear."); return; }
        var current = GridTerminalSystem.GetBlockWithName(DisplayName);
        if (_target == null || _target.Closed || current == null || current.EntityId != _target.EntityId)
            throw new Exception("Display changed. Explicitly clear/demo to bind again.");
        foreach (var camera in _cameras)
            if (camera.Closed || !camera.IsSameConstructAs(Me) || !camera.IsWorking)
                throw new Exception("A camera retired or stopped working. Explicitly clear/demo after correcting it.");
        if (command == "previous") SelectCamera(_cameraIndex - 1);
        else if (command == "next") SelectCamera(_cameraIndex + 1);
        else if (command != "" && command != "status") {
            Echo("Commands: demo, previous, next, status, clear."); return;
        }
        PollEvents(); PrintStatus();
    }
    catch (Exception error)
    {
        Runtime.UpdateFrequency = UpdateFrequency.None;
        Echo(error.Message); Echo("Run demo explicitly after correcting the cause.");
    }
}
