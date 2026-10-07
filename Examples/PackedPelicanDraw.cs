// Whole PB script. Paste animation into the Console/Projector's Custom Data.
Func<string,object[],object> _draw;
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
        H("target","Holo Map");
        if(argument=="stop"){H("pause");return;}
        if(argument=="start"){H("resume");return;}
        if(argument=="clear"){H("remove","pelican");return;}
        var block=(IMyTerminalBlock)H("FindTarget","Holo Map");
        H("view",0,0.85,0);
        H("play","pelican",block.CustomData);
        Echo("Mod runs this animation. Commands: demo, stop, start, clear.");
    }
    catch(Exception e){Echo(e.Message);}
}
