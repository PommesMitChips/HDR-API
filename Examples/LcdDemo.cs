// Whole PB script. Set LCD Content to HDR API, and name it HDR LCD.
// For a 2 × 2 wall, give all four panels that name and run group.
Func<string,object[],object> _draw;
object H(string command,params object[] args)
{
 if(_draw==null){var p=Me.GetProperty("HDR.Draw");if(p==null)throw new Exception("Enable updated HDR API and reload the save.");_draw=p.As<Func<string,object[],object>>().GetValue(Me);}
 return _draw(command,args);
}
public void Main(string argument,UpdateType source)
{
 try
 {
  H("target","HDR LCD");
  if(argument=="clear"){H("clear");return;}
  if(argument=="toggle"){H("toggle","menu");return;}
  if(argument=="vector"||argument=="native"){H("lcd-renderer",argument);Echo("Renderer requested: "+argument+". Use /hdr status in chat to check the active backend.");return;}
  if(argument.StartsWith("rate ")){double rate;if(!double.TryParse(argument.Substring(5),out rate))throw new Exception("Use rate 6, rate 15, rate 30 or rate 60.");H("lcd-refresh",rate);Echo("Requested sampling: "+rate+" Hz.");return;}
  if(argument=="transparent"||argument=="black"){H("lcd-background",argument);return;}
  if(argument=="status"){var settings=(VRage.MyTuple<string,double>)H("lcd-settings");Echo(settings.Item1+", requested "+settings.Item2+" Hz. Use /hdr status for local fallback information.");return;}
  if(argument=="group")H("lcdgroup","HDR LCD",2,2,4.0,4.0);else H("lcd",2.0,2.0);
  H("clear");H("view",0,0,0);
  H("circle","radar",0,0,0,0.75,"#006b80",96);
  H("layer","radar","map");H("layer-order","map",0);
  H("line","scan",0,0,0,0.75,0,0,"cyan");H("layer","scan","map");
  H("animate","scan","rotation",0,Math.PI*2,4,true,false);
  H("text","heading","HDR API",0,0.8,0,0.16,"white");H("layer","heading","menu");H("layer-order","menu",10);
  H("text","status","Station ready\nPress Toggle",0,-0.8,0,0.09,"cyan");H("layer","status","menu");
  Echo("LCD Content must be HDR API. Commands: demo, group, toggle, clear, vector, native, rate 6/15/30/60, transparent, black, status.");
 }
 catch(Exception e){Echo(e.Message);}
}
