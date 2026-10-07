// Paste-ready build: artifacts/SvgDemo.pb.cs. Name a Console or Projector Block "Holo Map".
const string ConsoleName = "Holo Map";
readonly HoloMapApi _holo = new HoloMapApi();
IMyTerminalBlock _console;
bool _started;
const string Reticle = @"<svg viewBox='0 0 100 100' xmlns='http://www.w3.org/2000/svg'>
 <g fill='none' stroke='#00dfff' stroke-width='2'>
  <circle cx='50' cy='50' r='34'/>
  <path d='M50 6 V24 M50 76 V94 M6 50 H24 M76 50 H94'/>
  <path stroke='#ffa020' d='M30 50 Q40 30 50 50 T70 50'/>
 </g>
 <polygon points='50,44 56,50 50,56 44,50' fill='#00dfff' fill-opacity='0.45'/>
</svg>";

public Program() { Runtime.UpdateFrequency = UpdateFrequency.None; }
public void Main(string argument, UpdateType source)
{
    try
    {
        if (!_holo.IsActive && !_holo.Activate(Me)) { Echo("Enable HDR API mod, then run this PB again."); return; }
        _console = _holo.FindTarget(GridTerminalSystem,ConsoleName);
        if (_console == null) { Echo("Name a Console or Projector Block '" + ConsoleName + "'."); return; }
        string command = (argument ?? "").Trim().ToLowerInvariant();
        if (command == "clear") { _holo.Clear(_console); _started = false; Runtime.UpdateFrequency = UpdateFrequency.None; return; }
        if (command == "stop")
        { _holo.StopAnimation(_console,"reticle"); _holo.StopAnimation(_console,"image"); Runtime.UpdateFrequency=UpdateFrequency.None; Echo("Paused. Run demo to restart."); return; }
        if (!_started || command == "demo")
        {
            _holo.Clear(_console);
            _holo.SetView(_console, new Vector3D(0,0.85,0), Vector3D.Zero, 1);
            _holo.TrackConstruct(_console, true, 0.3, new Vector3D(0,-0.15,0.35));
            _holo.PutSvg(_console,"reticle",Reticle,new Vector3D(-0.4,0.2,0),0.009);
            _holo.SetObjectLayer(_console,"reticle","hud");
            _holo.PutSvgAsset(_console,"gauge","gauge",new Vector3D(0.4,0.2,0),0.008);
            _holo.SetObjectLayer(_console,"gauge","hud");
            _holo.PutImage(_console,"image","Demo",new Vector2(0.45f,0.3f),Vector4.One);
            _holo.SetTransform(_console,"image",HoloMapApi.Pose(0.4,-0.3,0));
            _holo.SetObjectLayer(_console,"image","hud");
            _holo.Animate(_console,"reticle","rotation",0,Math.PI*2,6,true);
            _holo.Animate(_console,"image","opacity",0.25,1,2,true,true);
            _holo.PutLabel(_console,"legend",new Vector3D(0,0.8,0),"SVG + IMAGE + LIVE GRID",new Vector4(0.2f,0.9f,1,1),0.025f);
            Runtime.UpdateFrequency=UpdateFrequency.Update10; _started=true;
        }
        else if ((source & UpdateType.Update10) != 0) _holo.UpdateAnimations(Runtime.TimeSinceLastRun.TotalSeconds);
        Echo("HoloMap SVG demo: rotating SVG, packaged SVG gauge, fading image, live ship.\nCommands: demo, stop, clear");
    }
    catch(Exception ex) { Runtime.UpdateFrequency=UpdateFrequency.None; _started=false; Echo(ex.Message); }
}
