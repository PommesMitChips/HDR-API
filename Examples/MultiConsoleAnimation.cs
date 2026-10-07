// Put a packed animation in EACH Console/Projector's Custom Data, not the PB's.
// All owned Console/Projector Blocks named exactly "Holo Map" on this construct are driven.
const string ConsoleName = "Holo Map";
const string AnimationId = "pelican"; // retained group ID, scoped separately to each Console
const double Scale = 0.003;
const int CurveSegments = 3;
const double DiscoverySeconds = 2;
Func<IMyTerminalBlock,IMyTerminalBlock,string,string,double,VRage.MyTuple<MatrixD,int>,VRage.MyTuple<bool,string>> _frame;
Func<IMyTerminalBlock,IMyTerminalBlock,Vector3D,Vector3D,double,VRage.MyTuple<bool,string>> _view;
Func<IMyTerminalBlock,IMyTerminalBlock,string,VRage.MyTuple<bool,string>> _remove;
sealed class Display
{
    public IMyProjector Console;
    public string Data, Error;
    public double Time;
    public bool Initialized;
}
readonly Dictionary<long,Display> _displays = new Dictionary<long,Display>();
readonly List<IMyProjector> _found = new List<IMyProjector>();
bool _playing;
double _discovery = DiscoverySeconds;
public Program() { Runtime.UpdateFrequency=UpdateFrequency.None; }
void Check(VRage.MyTuple<bool,string> r) { if(!r.Item1)throw new Exception(r.Item2); }
bool Bind()
{
    var property=Me.GetProperty("HDR.Api");if(property==null)return false;
    var typed=property.As<IReadOnlyDictionary<string,Delegate>>();if(typed==null)return false;
    var api=typed.GetValue(Me);Delegate method;
    if(!api.TryGetValue("PutPackedAnimationFrame",out method))return false;
    _frame=method as Func<IMyTerminalBlock,IMyTerminalBlock,string,string,double,VRage.MyTuple<MatrixD,int>,VRage.MyTuple<bool,string>>;
    _view=api["SetView"] as Func<IMyTerminalBlock,IMyTerminalBlock,Vector3D,Vector3D,double,VRage.MyTuple<bool,string>>;
    _remove=api["Remove"] as Func<IMyTerminalBlock,IMyTerminalBlock,string,VRage.MyTuple<bool,string>>;
    return _frame!=null&&_view!=null&&_remove!=null;
}
void Discover(bool restart)
{
    _found.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyProjector>(_found,b=>b.CustomName==ConsoleName && b.IsSameConstructAs(Me));
    var present=new HashSet<long>();
    foreach(var block in _found)
    {
        present.Add(block.EntityId);Display display;
        if(!_displays.TryGetValue(block.EntityId,out display))
        {display=new Display{Console=block};_displays.Add(block.EntityId,display);}
        display.Console=block;
        string data=block.CustomData;
        if(restart||data!=display.Data)
        {
            display.Data=data;display.Time=0;display.Error=null;display.Initialized=false;
            if(string.IsNullOrWhiteSpace(data))
            {
                try{Check(_remove(Me,block,AnimationId));}catch(Exception e){display.Error=e.Message;}
                if(display.Error==null)display.Error="Paste packed animation into this Console/Projector's Custom Data.";
            }
        }
    }
    var gone=new List<long>();foreach(var pair in _displays)if(!present.Contains(pair.Key))gone.Add(pair.Key);
    foreach(long id in gone)
    {
        try{Check(_remove(Me,_displays[id].Console,AnimationId));}catch(Exception){} // mod also cleans lost access/closed blocks
        _displays.Remove(id);
    }
    _discovery=0;
}
public void Main(string argument,UpdateType source)
{
    try
    {
        if(_frame==null&&!Bind()){Echo("Enable updated HDR API mod and reload the save.");return;}
        string command=(argument??"").Trim().ToLowerInvariant();
        if(command!=""&&command!="start"&&command!="stop"&&command!="demo"&&command!="reload"&&command!="clear"&&command!="status")
        {Echo("Commands: start, stop, demo, reload, clear, status");return;}
        if(command=="stop"){_playing=false;Runtime.UpdateFrequency=UpdateFrequency.None;Report();return;}
        if(command=="clear")
        {
            Discover(false);
            foreach(var display in _displays.Values)
            {try{Check(_remove(Me,display.Console,AnimationId));display.Initialized=false;}catch(Exception e){display.Error=e.Message;}}
            _playing=false;Runtime.UpdateFrequency=UpdateFrequency.None;Report();return;
        }
        bool restart=command=="demo"||command=="reload";
        bool scheduled=(source&UpdateType.Update10)!=0;
        double elapsed=scheduled&&_playing?Math.Max(0,Runtime.TimeSinceLastRun.TotalSeconds):0;
        // Each display has its own playback clock; newly found/edited displays start at zero.
        foreach(var display in _displays.Values)if(display.Initialized&&display.Error==null)display.Time+=elapsed;
        _discovery+=elapsed;
        if(restart||command=="start"||command=="status"||_discovery>=DiscoverySeconds)Discover(restart);
        if(command=="status"){Report();return;}
        if(!scheduled||command=="start"||restart)_playing=true;
        if(_playing)foreach(var display in _displays.Values)
        {
            if(display.Error!=null)continue;
            try
            {
                if(!display.Initialized)Check(_view(Me,display.Console,new Vector3D(0,0.85,0),Vector3D.Zero,1));
                Check(_frame(Me,display.Console,AnimationId,display.Data,display.Time,
                    new VRage.MyTuple<MatrixD,int>(MatrixD.CreateScale(Scale),CurveSegments)));
                display.Initialized=true;
            }
            catch(Exception e){display.Error=e.Message;} // one bad payload cannot stop healthy Consoles
        }
        Runtime.UpdateFrequency=_playing?UpdateFrequency.Update10:UpdateFrequency.None;
        Report();
    }
    catch(Exception e){_playing=false;Runtime.UpdateFrequency=UpdateFrequency.None;Echo(e.Message);}
}
void Report()
{
    Echo("HoloMap multi-Console animation: "+(_playing?"playing":"paused"));
    Echo("Matching Consoles/Projectors: "+_displays.Count+" (mod limit: 8 active displays / animation caches)");
    foreach(var display in _displays.Values)
    {
        Echo("Console "+display.Console.EntityId+": "+(display.Error??("time "+display.Time.ToString("0.0")+"s, "+(display.Data??"").Length+" chars")));
    }
    Echo("Each Console/Projector reads its OWN Custom Data.\nCommands: start, stop, demo, reload, clear, status");
}
