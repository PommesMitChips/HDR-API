// Whole PB script. Requires HDR API + optional HDR Client Renderer with native portals.
// Name exactly one Console/Projector on this construct "HDR Portal"; paste, compile, Run.
// The plane is 4m above its anchor; paired exit is 30m ahead, with a 5m capture shell.
Func<string,object[],object> _hdr;
IMyProjector _anchor;
bool _running;
int _size=4096,_rate=60;
bool _native=true;
string _shape="plane",_transport="differential",_accuracy="strict";
object H(string op,params object[] args)
{if(_hdr==null){var p=Me.GetProperty("HDR.Draw");if(p==null)throw new Exception("Enable HDR API and reload.");_hdr=p.As<Func<string,object[],object>>().GetValue(Me);}return _hdr(op,args);}
void Retire()
{if(_anchor==null)return;H("target",_anchor);if((bool)H("screen-exists","portal")){H("screen","portal");H("screen-remove");}}
void Setup()
{
 var anchors=new List<IMyProjector>();GridTerminalSystem.GetBlocksOfType(anchors,p=>!p.Closed&&p.IsSameConstructAs(Me)&&p.CustomName=="HDR Portal");
 if(anchors.Count!=1)throw new Exception("Name exactly one Console or Projector HDR Portal on this construct.");
 if(_anchor!=null&&_anchor.EntityId!=anchors[0].EntityId)Retire();_anchor=anchors[0];Retire();
 H("target",_anchor);
 if((_anchor.BlockDefinition.SubtypeName??"").IndexOf("Console",StringComparison.OrdinalIgnoreCase)>=0)H("volume",false);
 H("screen","portal",MatrixD.CreateTranslation(0,4,0),3,2,3,2);
 H("screen-resolution",_size,_native?_size*2/3:_size);H("screen-refresh",_rate);H("screen-background","#00000000");H("screen-aspect","stretch");
 bool stealth=_transport=="stealth";var entry=MatrixD.CreateTranslation(0,4,0);var exit=stealth?entry:MatrixD.CreateTranslation(0,4,-30);
 if(stealth)
 {
  var radii=new Vector3D(2,1.5,2);H("screen-ellipsoid",radii,Math.PI*2,Math.PI,"outside");H("screen-two-sided",false,1,0);H("screen-opacity",1);
  H("screen-portal-ellipsoid",exit,radii,"differential",_accuracy);H("screen-portal-exit-side","outside");H("screen-portal-transport","stealth",_accuracy);
 }
 else if(_shape=="ellipsoid")
 {H("screen-ellipsoid",new Vector3D(2,1.5,2),Math.PI*2,Math.PI,"inside");H("screen-two-sided",true,1,1);H("screen-portal-ellipsoid",exit,new Vector3D(2,1.5,2),_transport,_accuracy);}
 else if(_shape=="mesh")
 {
  var points=new[]{new Vector3D(-1.5,-1,0),new Vector3D(1.5,-1,0),new Vector3D(0,1,.4)};
  var uv=new[]{new Vector2(0,1),new Vector2(1,1),new Vector2(.5f,0)};var triangles=new[]{0,1,2};
  H("screen-mesh",points,triangles,uv,"outside");H("screen-two-sided",true,1,1);H("screen-portal-mesh",exit,points,triangles,uv,_transport,_accuracy);
 }
 else H("screen-portal-plane",exit,3,2,_transport,_accuracy);
 H("screen-portal-shell",exit,stealth?new Vector3D(2.05,1.55,2.05):new Vector3D(5));H("screen-portal-settings",1,1,1);
 _running=true;Runtime.UpdateFrequency=UpdateFrequency.Update100;Status();
}
void Status()
{Echo("Native portal: "+(_transport=="stealth"?"outward ellipsoid bubble":_shape)+" / "+_transport+" / "+_accuracy+"\n"+(_transport=="stealth"?"Stealth bubble centre 4m above HDR Portal; radii 2m, 1.5m, 2m. Look from outside. Entry and exit coincide; an enclosing shell has a 5cm margin. The opaque exterior samples straight-through background beyond the shell, hiding its interior.":"Entry 4m above HDR Portal; exit 30m ahead; capture starts beyond 5m shell.")+"\n"+(_native?"Native pixel density (validated backend ceiling 2048 per axis)":_size+"-pixel resolution ceiling")+"; "+_rate+" Hz requested. Actual size/cadence follows local visibility, budget and GPU speed.\nObjects inside the shell are excluded.\nCommands: plane, ellipsoid, mesh, differential, stealth, strict, approximate, size native, size 1024, rate 60, status, clear. Stealth always uses the outward bubble; differential restores the selected paired shape.\nStrict can leave transparent gaps when transported rays cannot share one native perspective capture. Approximate explicitly samples the shell and may distort nearby objects.\nEach viewer needs the optional renderer. No output means absent/unsupported native capability or strict rays outside the perspective capture. Check /hdr portal status locally.");}
public void Main(string argument,UpdateType source)
{
 try
 {
  argument=(argument??"").Trim().ToLowerInvariant();
  if(argument=="clear"){Retire();_running=false;Runtime.UpdateFrequency=UpdateFrequency.None;Echo("Portal screen removed.");return;}
  if(argument=="status"){Status();return;}
  if((source&UpdateType.Update100)!=0&&argument.Length==0&&_running)
  {if(_anchor==null||_anchor.Closed||!_anchor.IsSameConstructAs(Me)){_running=false;Runtime.UpdateFrequency=UpdateFrequency.None;Echo("Portal anchor removed; run again after naming its replacement.");return;}H("target",_anchor);if(!(bool)H("screen-exists","portal"))Setup();return;}
  if(argument=="size native"){_native=true;_size=4096;}
  else if(argument.StartsWith("size ")){int size;if(!int.TryParse(argument.Substring(5),out size)||size<64||size>4096)throw new Exception("Use size native or size 64..4096.");_native=false;_size=size;}
  else if(argument.StartsWith("rate ")){int rate;if(!int.TryParse(argument.Substring(5),out rate)||rate<1||rate>120)throw new Exception("Use rate 1..120.");_rate=rate;}
  else if(argument=="plane"||argument=="ellipsoid"||argument=="mesh")_shape=argument;
  else if(argument=="differential"||argument=="stealth")_transport=argument;
  else if(argument=="strict"||argument=="approximate")_accuracy=argument;
  else if(argument.Length!=0)throw new Exception("Use plane, ellipsoid, mesh, differential, stealth, strict, approximate, size native, rate 60, status or clear.");
  Setup();
 }
 catch(Exception e){try{Retire();}catch{} _running=false;Runtime.UpdateFrequency=UpdateFrequency.None;Echo(e.Message);}
}
