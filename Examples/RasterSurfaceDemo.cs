// Paste whole script. Console/Projector: HDR Display. Optional source LCD: HDR Video.
// Raster UI needs the optional HDR Client Renderer plugin on the viewing client.
Func<string,object[],object> _hdr;
string _surface="plane",_renderer="raster";
object H(string op,params object[] args)
{
 if(_hdr==null){var p=Me.GetProperty("HDR.Draw");if(p==null)throw new Exception("Enable HDR API 0.7 and reload.");_hdr=p.As<Func<string,object[],object>>().GetValue(Me);}
 return _hdr(op,args);
}
public void Main(string argument,UpdateType source)
{
 try
 {
  var display=H("findtarget","HDR Display") as IMyTerminalBlock;
  if(display==null)throw new Exception("Name a Console/Projector HDR Display.");
  if(argument=="clear"){H("screen","canvas");H("screen-remove");return;}
  if(argument=="two-sided"||argument=="one-sided"){H("screen","canvas");H("screen-two-sided",argument=="two-sided",1,.35);Echo("Updated screen sides. Front opacity 1, back opacity .35.");return;}
  if(argument.StartsWith("sides ")){var parts=argument.Substring(6).Split(' ');if(parts.Length!=2)throw new Exception("Use sides 1 0.35 (front/back opacity).");H("screen","canvas");H("screen-two-sided",true,double.Parse(parts[0]),double.Parse(parts[1]));Echo("Updated screen side opacity.");return;}
  if(argument=="plane"||argument=="cylinder"||argument=="sphere")_surface=argument;
  if(argument=="vector"||argument=="raster")_renderer=argument;
  if(argument.StartsWith("rate ")){H("screen","canvas");H("screen-refresh",double.Parse(argument.Substring(5)));return;}
  if(argument=="toggle"){H("screen","canvas");H("toggle","menu");return;}
  if((display.BlockDefinition.SubtypeName??"").IndexOf("Console",StringComparison.OrdinalIgnoreCase)>=0)H("volume",false);
  H("range",0);H("budget",0,0,30000);
  H("screen","canvas",MatrixD.CreateTranslation(0,1.5,0),4,2,4,2);
  H("clear");H("screen-mapping","angular");H("screen-surface",_surface,3,Math.PI*2,Math.PI,"inside");
  H("screen-renderer",_renderer,2048,1024,4);H("screen-refresh",6);
  H("screen-background",_surface=="plane"?"#05101a":"#00000000");
  H("screen-source-clear");var video=GridTerminalSystem.GetBlockWithName("HDR Video");
  if(video!=null)H("screen-source","lcd-texture",video.EntityId.ToString()+":0");
  string svg="<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 200'><rect x='10' y='10' width='380' height='180' rx='12' fill='#102b3c' fill-opacity='.65' stroke='#39e2f1' stroke-width='2'/><path d='M30 150 L120 150 L145 120 L175 165 L215 90 L250 150 L370 150' fill='none' stroke='#ffab32' stroke-width='3'/></svg>";
  H("svg","card",svg,MatrixD.CreateScale(.01),24);H("layer","card","menu");
  H("text","title","HDR / "+_renderer+" requested",0,.65,0,.16,"cyan");H("layer","title","menu");
  H("circle","dial",0,.05,0,.38,"cyan",192,.015,Vector3D.UnitZ);H("layer","dial","menu");
  H("animate","dial","scale",.9,1.1,2,true,true);H("layer-order","menu",10);
  Echo("Requested "+_surface+" / "+_renderer+". Run /hdr status for this client's actual backend and UI texture count.\nInside curves: stand within 3m of their centre.\nCommands: plane, cylinder, sphere, raster, vector, two-sided, one-sided, sides 1 0.35, rate 30, toggle, clear.\nVideo relays HDR Video's existing client texture; it needs a separate camera-video producer.");
 }
 catch(Exception e){Echo(e.Message);}
}
