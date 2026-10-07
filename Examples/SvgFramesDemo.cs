// Build appends artifacts/pulse.frames.cs (exported by Tools/ExportSvgFrames.mjs) and the API.
readonly HoloMapApi _holo = new HoloMapApi();
IMyTerminalBlock _console;
bool _started;
public Program() { Runtime.UpdateFrequency=UpdateFrequency.None; }
public void Main(string argument, UpdateType source)
{
    try
    {
        if(!_holo.IsActive&&!_holo.Activate(Me)){Echo("Enable HDR API mod, then run this PB again.");return;}
        _console=_holo.FindTarget(GridTerminalSystem,"Holo Map");
        if(_console==null){Echo("Name a Console or Projector Block 'Holo Map'.");return;}
        if(argument=="stop"){_holo.StopAnimation(_console,"pulse");Runtime.UpdateFrequency=UpdateFrequency.None;return;}
        if(!_started||argument=="demo")
        {
            _holo.Clear(_console);_holo.SetView(_console,new Vector3D(0,0.85,0),Vector3D.Zero,1);
            _holo.PlaySvgFrames(_console,"pulse",SvgFrames,SvgFrameRate,Vector3D.Zero,0.01);
            Runtime.UpdateFrequency=UpdateFrequency.Update10;_started=true;
        }
        else _holo.UpdateAnimations(Runtime.TimeSinceLastRun.TotalSeconds);
        Echo("Playing exported JS SVG frames. Commands: demo, stop.");
    }
    catch(Exception ex){Runtime.UpdateFrequency=UpdateFrequency.None;_started=false;Echo(ex.Message);}
}
