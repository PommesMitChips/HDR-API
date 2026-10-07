// Paste artifacts/pelican.customdata.txt into this PB's Custom Data.
// Name an owned Console or Projector Block "Holo Map" on the same construct.
const string ConsoleName = "Holo Map";
const double Scale = 0.003;
const int CurveSegments = 3;
Func<IMyTerminalBlock,IMyTerminalBlock,string,string,double,VRage.MyTuple<MatrixD,int>,VRage.MyTuple<bool,string>> _frame;
Func<IMyTerminalBlock,IMyTerminalBlock,Vector3D,Vector3D,double,VRage.MyTuple<bool,string>> _view;
Func<IMyTerminalBlock,IMyTerminalBlock,string,VRage.MyTuple<bool,string>> _remove;
IMyTerminalBlock _console;
string _data;
double _time;
bool _playing;
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
public void Main(string argument,UpdateType source)
{
    try
    {
        if(_frame==null&&!Bind()){Echo("Update HoloMap mod to the packed animation build, reload save, then run again.");return;}
        _console=GridTerminalSystem.GetBlockWithName(ConsoleName);
        if(_console==null){Echo("Name a Console or Projector Block '"+ConsoleName+"'.");return;}
        string command=(argument??"").Trim().ToLowerInvariant();
        if(command=="stop"){_playing=false;Runtime.UpdateFrequency=UpdateFrequency.None;Echo("Paused. Run start to resume.");return;}
        if(command=="clear"){Check(_remove(Me,_console,"pelican"));_playing=false;Runtime.UpdateFrequency=UpdateFrequency.None;_data=null;return;}
        if(command==""&&(source&UpdateType.Update10)!=0&&!_playing)return;
        if(_data==null||command=="demo"||command=="reload")
        {
            _data=Me.CustomData;_time=0;
            Check(_view(Me,_console,new Vector3D(0,0.85,0),Vector3D.Zero,1));
        }
        else if((source&UpdateType.Update10)!=0&&_playing)_time+=Runtime.TimeSinceLastRun.TotalSeconds;
        Check(_frame(Me,_console,"pelican",_data,_time,new VRage.MyTuple<MatrixD,int>(MatrixD.CreateScale(Scale),CurveSegments)));
        _playing=true;Runtime.UpdateFrequency=UpdateFrequency.Update10;
        Echo("Compressed pelican animation\nCustom Data: "+_data.Length+" characters\nTime: "+_time.ToString("0.0")+"s\nCommands: demo, start, stop, clear, reload");
    }
    catch(Exception e){_playing=false;_data=null;Runtime.UpdateFrequency=UpdateFrequency.None;Echo(e.Message);}
}
