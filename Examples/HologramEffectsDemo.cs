// Whole PB script. HDR API 0.9.8. No optional client plugin is required.
// Name a Console / Projector "HDR Effects", or use an LCD with HDR API Content.
// Run demo once: viewing clients animate retained content without PB frame writes.
const string DisplayName = "HDR Effects";
Func<string, object[], object> _hdr;
bool _ready, _world;
readonly string[] _objects = {
 "title", "ship", "ship-name", "refresh", "refresh-name",
 "signal", "signal-name", "plinth", "plinth-ring", "footer"
};

// This SVG is authored in model units: at scale 1 its viewBox is one metre wide.
const string Ship = @"<svg viewBox='0 0 1 1' xmlns='http://www.w3.org/2000/svg'>
 <g stroke='#18dce6' stroke-width='.012' fill='#0b708a50'>
  <path d='M.5 .06 L.6 .25 L.58 .48 L.79 .69 L.78 .8 L.58 .67 L.58 .9 L.42 .9 L.42 .67 L.22 .8 L.21 .69 L.42 .48 L.4 .25 Z'/>
  <path d='M.5 .28 L.55 .39 L.55 .56 L.45 .56 L.45 .39 Z' fill='#f5bd5560' stroke='#f5bd55'/>
  <path d='M.28 .7 L.4 .59 M.72 .7 L.6 .59 M.44 .78 H.56' fill='none'/>
 </g>
</svg>";
const string Readout = @"<svg viewBox='0 0 .85 .4' xmlns='http://www.w3.org/2000/svg'>
 <rect x='.02' y='.02' width='.81' height='.36' rx='.018' fill='#103e5150' stroke='#18dce6' stroke-width='.008'/>
 <g stroke='#18dce6' stroke-width='.012' fill='none'>
  <path d='M.06 .21 H.14 L.19 .12 L.24 .29 L.3 .17 L.36 .21 H.48 L.54 .1 L.6 .3 L.66 .21 H.78'/>
  <path d='M.07 .07 H.27 M.65 .33 H.78' stroke='#f5bd55'/>
 </g>
</svg>";
const string Signal = @"<svg viewBox='0 0 .85 .36' xmlns='http://www.w3.org/2000/svg'>
 <rect x='.02' y='.02' width='.81' height='.32' rx='.018' fill='#103e5140' stroke='#18dce6' stroke-width='.008'/>
 <g fill='#18dce6'>
  <rect x='.1' y='.23' width='.08' height='.05'/><rect x='.24' y='.19' width='.08' height='.09'/>
  <rect x='.38' y='.14' width='.08' height='.14'/><rect x='.52' y='.09' width='.08' height='.19'/>
  <rect x='.66' y='.06' width='.08' height='.22' fill='#f5bd55'/>
 </g>
</svg>";

object H(string command, params object[] args)
{
 if (_hdr == null)
 {
  var property = Me.GetProperty("HDR.Draw");
  if (property == null) throw new Exception("Requires mod: HDR API 0.9.8 or later.");
  _hdr = property.As<Func<string, object[], object>>().GetValue(Me);
 }
 return _hdr(command, args);
}
public Program() { Runtime.UpdateFrequency = UpdateFrequency.None; }
void SelectDisplay()
{
 var target = H("findtarget", DisplayName) as IMyTerminalBlock;
 if (target == null) target = H("finddisplay", DisplayName) as IMyTerminalBlock;
 if (target == null) throw new Exception("Name a Console, Projector or LCD '" + DisplayName + "'.");
 var capabilities = (VRage.MyTuple<string, string, int>)H("capabilities", target);
 _world = (capabilities.Item3 & 8) != 0;
 if (!_world && capabilities.Item2 != "lcd") throw new Exception("This block cannot render HDR artwork.");
}
void Setup()
{
 H("clear");
 H("budget", 0, 0, 20000);
 if (_world)
 {
  // Dedicated demo anchor: open its envelope so projector rays reach the source.
  H("volume", false);
  H("range", 4);
  H("view", new Vector3D(0, 1.7, 0));
 }
 else
 {
  H("lcd", 2.8, 2.0);
  H("view", Vector3D.Zero);
 }
 H("text", "title", "HOLOGRAPHIC / EFFECTS", 0, .78, 0, .07, "#c4faff");
 H("svg", "ship", Ship, new Vector3D(-.6, .03, .08), 1, 4);
 H("svg", "refresh", Readout, new Vector3D(.61, .28, 0), 1, 4);
 H("svg", "signal", Signal, new Vector3D(.61, -.28, 0), 1, 4);
 H("emission", "ship", .65);
 H("emission", "refresh", .25);
 H("emission", "signal", .25);
 H("text", "ship-name", _world ? "DEPTH / RAYS / PARTICLES" : "SVG / PARTICLES", -.6, -.57, 0, .045, "#92d6de");
 H("text", "refresh-name", "MOVING REFRESH BAR", .61, .57, 0, .04, "#92d6de");
 H("text", "signal-name", "SEEDED SIGNAL FLICKER", .61, -.55, 0, .04, "#92d6de");
 H("circle", "plinth", 0, -.67, 0, .31, "#167c91", 48, .008, _world ? Vector3D.UnitY : Vector3D.UnitZ);
 H("circle", "plinth-ring", 0, -.67, 0, .21, "#f5bd55", 40, .006, _world ? Vector3D.UnitY : Vector3D.UnitZ);
 H("text", "footer", "RETAINED ART / CLIENT ANIMATION", 0, -.88, 0, .04, "#92d6de");

 H("effect", "ship", "flicker", .10, 9);
 H("effect", "ship", "particles", 40, .009, .08, 2.6, .07, .42, 73);
 H("effect", "ship", "budget", 4096);
 if (_world)
 {
  H("effect", "ship", "volume", .14, 6, .56);
  // Emitter is ship-local before its pose; this maps to the anchor's origin.
  H("effect", "ship", "rays", 12, .022, .10, new Vector3D(.6, -1.73, -.08), .006);
 }
 H("effect", "refresh", "scan", .10, .22, .15, 1.4);
 H("effect", "signal", "flicker", .55, 8);
 H("animate", "plinth-ring", "opacity", .35, .85, 2, true, true);
 _ready = true;
 Transition(true);
}
void Transition(bool entering)
{
 // Different reveal styles remain independent of the source SVGs.
 foreach (string id in _objects)
 {
  H("visible", id, true);
  H("transition", id, entering ? "in" : "out",
    id == "ship" ? "dissolve" : id == "refresh" ? "wipe" : "fade", .9);
 }
}
public void Main(string argument, UpdateType source)
{
 try
 {
  Runtime.UpdateFrequency = UpdateFrequency.None;
  SelectDisplay();
  string command = (argument ?? "").Trim().ToLowerInvariant();
  if (command == "clear")
  {
   H("clear"); _ready = false; Echo("Owned artwork and effects cleared."); return;
  }
  if (!_ready || command == "demo") Setup();
  else if (command == "off" || command == "on")
   foreach (string id in _objects) H("visible", id, command == "on");
  else if (command == "transitionin" || command == "transitionout")
   Transition(command == "transitionin");
  else if (command.Length != 0) throw new Exception("Use demo, on, off, clear, transitionin or transitionout.");
  Echo("Hologram Effects / " + (_world ? "Console or Projector / 3D" : "LCD / 2D") +
    "\nNo optional plugin. No PB animation loop.\nCommands: demo, on, off, clear, transitionin, transitionout." +
    (!_world ? "\nLCD Content must be HDR API; volume and emitter rays are omitted." : "\nEmitter rays and particles are cosmetic geometry."));
 }
 catch (Exception error) { _ready = false; Echo(error.Message); }
}
