// Whole PB script: no HoloMapApi class to append.
Func<string,object[],object> _draw;
object H(string command,params object[] args)
{
    if(_draw==null)
    {
        var p=Me.GetProperty("HDR.Draw");
        if(p==null)throw new Exception("Enable updated HDR API and reload the save.");
        _draw=p.As<Func<string,object[],object>>().GetValue(Me);
    }
    return _draw(command,args);
}
public void Main(string argument,UpdateType source)
{
    try
    {
        H("target","Holo Map");
        if(argument=="clear"){H("clear");return;}
        if(argument=="stop"){H("pause");return;}
        if(argument=="start"){H("resume");return;}
        H("clear");
        H("view",0,0.85,0);
        H("line","beam",-0.6,0,0,0.6,0,0,"cyan");
        H("circle","ring",0,0.3,0,0.25,"orange");
        H("text","title","HOLO DECK",0,0.8,0);
        H("opacity","ring",0.8);
        H("emission","beam",1);
        H("animate","ring","scale",0.8,1.2,2,true,true);
        Echo("Mod drawing API. Commands: demo, stop, start, clear.\nThe mod runs the animation.");
    }
    catch(Exception e){Echo(e.Message);}
}
