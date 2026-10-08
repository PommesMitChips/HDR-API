// Whole PB script. HDR API 0.9.9 / scene 19, ALPHA.
// Name a dedicated Console / Projector "HDR Controls" on this PB's construct.
// Run demo once. HDR retains the controls; this PB has no animation/update loop.
// Native mouse capture requires HDR Client Renderer 0.9.13+ / HDR.Pointer/1.
// Install the renderer on this viewer. Native live input acceptance is unverified.
// Source commands and numeric bindings are separate from optional input capture.
// On foot in first person: Use enters the world control bundle; drag controls.
// Mouse-up ends only the value gesture. Escape/context loss closes the viewer.
const string DisplayName = "HDR Controls";
const string ValueWake = "controls:values";
Func<string, object[], object> _draw, _ui;
IMyTerminalBlock _target;
bool _ready;
double _throttle, _curve, _angle; // Actual script variables, explicitly assigned below.

const string HandleSvg = @"<svg viewBox='0 0 .16 .16' xmlns='http://www.w3.org/2000/svg'>
 <circle cx='.08' cy='.08' r='.064' fill='#164753' stroke='#75edec' stroke-width='.009'/>
 <path d='M.055 .055 L.08 .035 L.105 .055 M.055 .105 L.08 .125 L.105 .105' fill='none' stroke='#f5bd55' stroke-width='.008'/>
</svg>";
const string RotorSvg = @"<svg viewBox='0 0 .5 .5' xmlns='http://www.w3.org/2000/svg'>
 <path d='M.19 .22 H.37 V.17 L.45 .25 L.37 .33 V.28 H.19 Z' fill='#f5bd55' stroke='#ffe0a0' stroke-width='.008'/>
 <circle cx='.25' cy='.25' r='.035' fill='#123e4b' stroke='#75edec' stroke-width='.009'/>
</svg>";
readonly Vector3D[] _curvePath = {
 Vector3D.Zero, new Vector3D(.18,.14,0), new Vector3D(.43,.20,0),
 new Vector3D(.69,.13,0), new Vector3D(.94,-.02,0), new Vector3D(1.15,-.15,0)
};

object H(string command, params object[] args)
{
 if (_draw == null)
 {
  var property = Me.GetProperty("HDR.Draw");
  if (property == null) throw new Exception("Requires mod: HDR API with numeric controls.");
  _draw = property.As<Func<string, object[], object>>().GetValue(Me);
 }
 return _draw(command, args);
}
object U(string command, params object[] args)
{
 if (_ui == null)
 {
  var property = Me.GetProperty("HDR.UI");
  if (property == null) throw new Exception("Requires HDR.UI with numeric controls.");
  _ui = property.As<Func<string, object[], object>>().GetValue(Me);
 }
 return _ui(command, args);
}
public Program() { Runtime.UpdateFrequency = UpdateFrequency.None; }
void SelectDisplay()
{
 _target = H("findtarget", DisplayName) as IMyTerminalBlock;
 if (_target == null) throw new Exception("Name a Console / Projector '" + DisplayName + "'.");
 var capability = (VRage.MyTuple<string, string, int>)H("capabilities", _target);
 if ((capability.Item3 & 8) == 0) throw new Exception("This demo requires a Console / Projector world display.");
 H("target", _target); U("target", _target);
}
void Bind(string control, string artwork, string value, double initial, double min, double max, double step)
{
 U("value", value, initial, min, max, step);
 // PB rectangle uses centre X/Y, width/height in artwork-local coordinates.
 U("control", control, "controls", artwork, 0d, 0d, .18, .18);
 U("bind-value", control, value);
}
void Setup()
{
 U("clear"); H("clear");
 H("budget", 0, 0, 20000);
 H("volume", false); H("range", 4); H("view", new Vector3D(0, 1.7, 0));
 H("text", "title", "HOLOGRAPHIC / CONTROLS", 0, .96, 0, .075, "#c4faff");
 H("text", "line-label", "THROTTLE / LINE / 5% STEPS", -.75, .72, 0, .045, "#92d6de");
 H("line", "line-track", new Vector3D(-.75,.45,0), new Vector3D(.45,.45,0), "#167c91", .012);
 H("svg", "line-handle", HandleSvg, new Vector3D(-.75,.45,0), 1, 8);
 H("text", "curve-label", "TRIM / POLYLINE / ARC LENGTH", -.75, .15, 0, .045, "#92d6de");
 var curveOrigin = new Vector3D(-.75,-.12,0);
 for (int i = 0; i < _curvePath.Length - 1; i++)
  H("line", "curve-track-" + i, curveOrigin + _curvePath[i], curveOrigin + _curvePath[i+1], "#167c91", .012);
 H("svg", "curve-handle", HandleSvg, curveOrigin, 1, 8);
 H("text", "angle-label", "ROTOR / PIVOT + AXIS", -.75, -.62, 0, .045, "#92d6de");
 H("circle", "dial-ring", .55, -.79, 0, .25, "#167c91", 48, .012, Vector3D.UnitZ);
 H("line", "dial-zero", new Vector3D(.77,-.79,0), new Vector3D(.84,-.79,0), "#92d6de", .008);
 H("svg", "rotor", RotorSvg, new Vector3D(.55,-.79,0), 1, 8);
 H("text", "footer", "SOURCE WRITES + POINTER EDITS SHARE ONE VALUE", 0, -1.22, 0, .039, "#92d6de");

 U("bundle", "controls", true);
 Bind("throttle-control", "line-handle", "throttle", 0, 0, 100, 5);
 U("constraint", "throttle-control", "line", Vector3D.Zero, new Vector3D(1.2,0,0));
 U("draggable", "throttle-control", true);
 Bind("curve-control", "curve-handle", "trim", 0, 0, 1, .05);
 U("constraint", "curve-control", "path", _curvePath);
 U("draggable", "curve-control", true);
 U("value", "angle", 0d, -Math.PI/2, Math.PI/2, Math.PI/12);
 U("control", "rotor-control", "controls", "rotor", 0d, 0d, .5, .5);
 U("bind-value", "rotor-control", "angle");
 U("constraint", "rotor-control", "rotation", Vector3D.Zero, Vector3D.UnitZ, -Math.PI/2, Math.PI/2);
 U("draggable", "rotor-control", true);
 U("value-notify", ValueWake); // Fixed callback argument; no numeric payload in Run.
 // Depth/flicker are cosmetic. Control geometry and numeric ranges stay independent.
 H("effect", "line-handle", "volume", .025, 2, .6);
 H("effect", "curve-handle", "flicker", .05, 8);
 H("effect", "rotor", "volume", .025, 2, .6);
 _ready = true; PullValues(); RefreshReadouts();
}
void Assign(string id, double value)
{
 if (id == "throttle") _throttle = value;
 else if (id == "trim") _curve = value;
 else if (id == "angle") _angle = value;
}
void PullValues()
{
 foreach (string id in new[] { "throttle", "trim", "angle" })
  Assign(id, ((VRage.MyTuple<double,long>)U("get-value", id)).Item1);
}
void PollChanges()
{
 var events = (VRage.MyTuple<string,string,string,VRage.MyTuple<double,long,long>>[])U("poll-value-events");
 foreach (var e in events) Assign(e.Item3, e.Item4.Item1);
 // Events are bounded/coalesced. The canonical query repairs a missed intermediate event.
 PullValues(); RefreshReadouts();
}
void SourceWrite(string id, double requested)
{
 var before = (VRage.MyTuple<double,long>)U("get-value", id);
 var result = (VRage.MyTuple<bool,double,long>)U("set-value", id, requested, before.Item2);
 Assign(id, result.Item2); // Canonical clamp/snap, or newer value on CAS conflict.
 Echo(result.Item1 ? "Source write accepted." : "Concurrent edit won; current value retained.");
 // An accepted source write preempts a pointer gesture, even at the same value.
 RefreshReadouts();
}
void RefreshReadouts()
{
 H("text", "throttle-readout", _throttle.ToString("0") + " %", .78, .45, 0, .075, "#f5bd55");
 H("text", "curve-readout", (_curve*100).ToString("0") + " %", .78, -.12, 0, .075, "#f5bd55");
 H("text", "angle-readout", (_angle*180/Math.PI).ToString("0") + " deg", -.75, -.89, 0, .075, "#f5bd55");
}
bool Request(string command, string prefix, out double value)
{
 value = 0;
 return command.StartsWith(prefix) && double.TryParse(command.Substring(prefix.Length),
  System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
}
public void Main(string argument, UpdateType source)
{
 try
 {
  Runtime.UpdateFrequency = UpdateFrequency.None; SelectDisplay();
  string command = (argument ?? "").Trim().ToLowerInvariant();
  if (command == "clear") { U("clear"); H("clear"); _ready = false; Echo("Owned controls and artwork cleared."); return; }
  if (!_ready || command == "demo") Setup();
  if (command == ValueWake) { PollChanges(); return; }
  double requested;
  if (command == "reset") { SourceWrite("throttle", 0); SourceWrite("trim", 0); SourceWrite("angle", 0); }
  else if (Request(command, "throttle:", out requested)) SourceWrite("throttle", requested);
  else if (Request(command, "trim:", out requested)) SourceWrite("trim", requested/100);
  else if (Request(command, "angle:", out requested)) SourceWrite("angle", requested*Math.PI/180);
  else if (command.Length != 0 && command != "demo") throw new Exception("Use demo, reset, clear, throttle:65, trim:75 or angle:45.");
  Echo("Controls retained. No PB update loop.\nSources: throttle:65, trim:75, angle:45, reset.\nOn foot / first person: Use enters; drag controls; Escape exits.\nMouse-up ends the gesture and retains the viewer.\nNative mouse requires HDR Client Renderer 0.9.13 (ALPHA).\n" + (string)U("capabilities"));
 }
 catch (Exception error) { Echo(error.Message); }
}
