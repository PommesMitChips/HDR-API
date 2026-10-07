// Whole PB: each Console/Projector named Holo Map holds its own Custom Data.
const string Name="Holo Map";
Func<string,object[],object> _draw;
readonly Dictionary<long,string> _data=new Dictionary<long,string>();
readonly Dictionary<long,IMyProjector> _blocks=new Dictionary<long,IMyProjector>();
readonly Dictionary<long,string> _errors=new Dictionary<long,string>();
bool _running;
object H(string command,params object[] args)
{
    if(_draw==null)
    {
        var p=Me.GetProperty("HDR.Draw");if(p==null)throw new Exception("Enable updated HDR API and reload save.");
        _draw=p.As<Func<string,object[],object>>().GetValue(Me);
    }
    return _draw(command,args);
}
public void Main(string argument,UpdateType source)
{
    try
    {
        string command=(argument??"").Trim().ToLowerInvariant();
        if(command=="stop"){H("pause");_running=false;Runtime.UpdateFrequency=UpdateFrequency.None;Report();return;}
        bool restart=command=="demo"||command=="reload";
        bool clearing=command=="clear";
        if(command!="status"&&!clearing){_running=true;H("resume");}
        var found=(List<IMyProjector>)H("FindTargets",Name);var present=new HashSet<long>();
        foreach(var block in found)
        {
            present.Add(block.EntityId);_blocks[block.EntityId]=block;
            string data=block.CustomData,old;
            if(!clearing&&!restart&&_data.TryGetValue(block.EntityId,out old)&&old==data)continue;
            _data[block.EntityId]=data;
            try
            {
                H("target",block);
                if(clearing||string.IsNullOrWhiteSpace(data))H("remove","pelican");
                else{H("view",0,0.85,0);H("play","pelican",data);}
                _errors.Remove(block.EntityId);
                if(string.IsNullOrWhiteSpace(data))_errors[block.EntityId]="Paste animation into this block's Custom Data.";
            }
            catch(Exception e){_errors[block.EntityId]=e.Message;}
        }
        var removed=new List<long>();foreach(long id in _blocks.Keys)if(!present.Contains(id))removed.Add(id);
        foreach(long id in removed)
        {
            try{H("target",_blocks[id]);H("remove","pelican");}catch(Exception){}
            _blocks.Remove(id);_data.Remove(id);_errors.Remove(id);
        }
        if(clearing){_data.Clear();_running=false;H("pause");}
        Runtime.UpdateFrequency=_running?UpdateFrequency.Update100:UpdateFrequency.None;
        Report();
    }
    catch(Exception e){Runtime.UpdateFrequency=UpdateFrequency.None;Echo(e.Message);}
}
void Report()
{
    Echo("Mod-side animation: "+(_running?"playing":"paused")+"\nMatching displays: "+_blocks.Count);
    foreach(var pair in _blocks){string error;Echo(pair.Key+": "+(_errors.TryGetValue(pair.Key,out error)?error:"OK"));}
    Echo("Commands: start, stop, demo, reload, clear, status.\nThe mod advances all playback clocks.");
}
