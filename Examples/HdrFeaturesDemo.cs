// Whole PB script. Name a Console or Projector "HDR Display".
Func<string,object[],object> _draw;
object H(string command,params object[] args)
{
    if(_draw==null){var p=Me.GetProperty("HDR.Draw");if(p==null)throw new Exception("Enable HDR API and reload the save.");_draw=p.As<Func<string,object[],object>>().GetValue(Me);}
    return _draw(command,args);
}
public void Main(string argument,UpdateType source)
{
    try
    {
        H("target","HDR Display");
        if(argument=="clear"){H("clear");return;}
        H("clear");H("view",0,0.8,0);
        H("svg","gradient",@"<svg viewBox='0 0 480 480'><defs><radialGradient id='g'><stop offset='0' stop-color='#00eaff'/><stop offset='1' stop-color='#edba4a'/></radialGradient><symbol id='arrow' viewBox='0 0 20 20'><path d='M0 0 L20 10 L0 20 Z' fill='#ffffff'/></symbol></defs><circle cx='240' cy='240' r='175' fill='url(#g)'/><use href='#arrow' x='220' y='230' width='40' height='40'/></svg>",0,0,0,0.0025,8);
        H("text","heading","HDR API",0,0.6,0,0.12,"white");
        H("text","status","Ready: 20°C\nΩ  α  Привет",0,-0.6,0,0.075,"cyan");
        Echo("Radial gradient, reusable SVG symbol and vector text. Run clear to remove.");
    }
    catch(Exception e){Echo(e.Message);}
}
