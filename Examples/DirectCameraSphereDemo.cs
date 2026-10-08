// Whole PB script: HDR API + HDR Client Renderer 0.9.3. No LCD buffers needed.
// Console/Projector: HDR Display. Uses the 1..6 360 Camera blocks on this construct.
// If none exist, uses ordinary cameras with names starting HDR Camera.
Func<string,object[],object> _hdr;
IMyTerminalBlock _display;
List<IMyCameraBlock> _cameras=new List<IMyCameraBlock>();
double _rate=30,_fov=105,_feather=8,_saturation=1.15,_opacity=.85,_back=1;
int _size=1024;
bool _both=true,_running,_grey,_lite;
double _height;
object H(string op,params object[] args)
{
 if(_hdr==null){var p=Me.GetProperty("HDR.Draw");if(p==null)throw new Exception("Enable HDR API 0.9.3 and reload.");_hdr=p.As<Func<string,object[],object>>().GetValue(Me);}
 return _hdr(op,args);
}
void Retire()
{
 foreach(string id in new[]{"panorama","pan-gap","cam0","cam1","cam2","cam3","cam4","cam5","cam6"})
 {try{if((bool)H("screen-exists",id)){H("screen",id);H("screen-remove");}}catch{}}
}
void Setup()
{
 _display=H("findtarget","HDR Display") as IMyTerminalBlock;
 if(_display==null)throw new Exception("Name a Console or Projector HDR Display.");
 var cameras=new List<IMyCameraBlock>();
 GridTerminalSystem.GetBlocksOfType(cameras,c=>c.IsSameConstructAs(Me));
 _cameras=cameras.FindAll(c=>c.BlockDefinition.SubtypeName=="Camera360Large"||c.BlockDefinition.SubtypeName=="Camera360Small");
 if(_cameras.Count==0)_cameras=cameras.FindAll(c=>c.CustomName.StartsWith("HDR Camera",StringComparison.Ordinal));
 if(_cameras.Count<1||_cameras.Count>6)throw new Exception("Use one to six 360 Camera blocks, or ordinary cameras named HDR Camera [N].");
 _cameras.Sort((a,b)=>a.EntityId.CompareTo(b.EntityId));
 _height=1.5+_display.CubeGrid.GridSize;
 if((_display.BlockDefinition.SubtypeName??"").IndexOf("Console",StringComparison.OrdinalIgnoreCase)>=0)H("volume",false);
 H("range",0);H("budget",0,0,60000);Retire();
 H("screen","panorama",MatrixD.CreateTranslation(0,_height,0),6,3,6,3);
 H("screen-surface","sphere",3,Math.PI*2,Math.PI,"inside");
 H("screen-mapping","angular");H("screen-aspect","stretch");H("screen-quality",.015);
 H("screen-background",_grey?"#252b3030":"#00000000");
 H("screen-resolution",_size*2,_size);H("screen-refresh",_rate);
 H("screen-opacity",_opacity);H("screen-two-sided",_both,1,_back);
 H("screen-panorama",_fov,_feather,_saturation,_size);
 H("screen-camera-quality",_lite?"lite":"normal");
 var ids=new StringBuilder();foreach(var c in _cameras){if(ids.Length>0)ids.Append(',');ids.Append(c.EntityId);}
 H("screen-source","camera-panorama",ids.ToString());
 _running=true;Runtime.UpdateFrequency=UpdateFrequency.Update100;Status();
}
void Status()
{
 var text=new StringBuilder("Direct Camera Sphere v14 / "+_cameras.Count+" cameras / no LCD buffers\n");
 text.Append(_fov).Append(" degree square captures; ").Append(_feather).Append(" degree feather\n");
 text.Append(_size).Append("px capture ceiling; ").Append(_size*2).Append('x').Append(_size).Append(" panorama ceiling; ").Append(_rate).Append(" Hz requested\n");
 text.Append("Capture profile: ").Append(_lite?"lite":"normal").Append("; shared client pixel budget / density tiles\n");
 text.Append("Both sides: ").Append(_both).Append("; opacity ").Append(_opacity).Append("; outside multiplier ").Append(_back).Append("; saturation ").Append(_saturation).Append('\n');
 text.Append("Centre ").Append(_height).Append("m above anchor (one grid block higher); radius 3m.\n");
 foreach(var c in _cameras)text.Append(c.CustomName).Append(" [").Append(c.EntityId).Append("]\n");
 text.Append("Commands: rate 30, size 1024, fov 105, feather 8, saturation 1.15, opacity 0.85, transparency 0.15, back 1, both, inside, gaps transparent, gaps grey, quality normal, quality lite, clear.\n");
 text.Append("Local limits: /hdr camera status; pixels off; passes off; rate off. Actual detail/cadence follow local visibility, resolution and GPU speed. Nearby objects can retain parallax at camera joins.");Echo(text.ToString());
}
public void Main(string argument,UpdateType source)
{
 try
 {
  argument=(argument??"").Trim().ToLowerInvariant();
  if((source&UpdateType.Update100)!=0&&argument.Length==0&&_running)
  {
   H("target",_display);
   if(!(bool)H("screen-exists","panorama")){Setup();return;}
   foreach(var c in _cameras)if(c.Closed||!c.IsSameConstructAs(Me)){Setup();return;}
   return;
  }
  if(argument=="clear"){if(_display!=null){H("target",_display);Retire();}_running=false;Runtime.UpdateFrequency=UpdateFrequency.None;Echo("Camera sphere cleared.");return;}
  if(argument.StartsWith("rate "))_rate=double.Parse(argument.Substring(5));
  if(argument.StartsWith("size "))_size=int.Parse(argument.Substring(5));
  if(argument.StartsWith("fov "))_fov=double.Parse(argument.Substring(4));
  if(argument.StartsWith("feather "))_feather=double.Parse(argument.Substring(8));
  if(argument.StartsWith("saturation "))_saturation=double.Parse(argument.Substring(11));
  if(argument.StartsWith("opacity "))_opacity=double.Parse(argument.Substring(8));
  if(argument.StartsWith("transparency "))_opacity=1-double.Parse(argument.Substring(13));
  if(argument.StartsWith("back "))_back=double.Parse(argument.Substring(5));
  if(argument=="both")_both=true;if(argument=="inside")_both=false;
  if(argument=="quality lite")_lite=true;if(argument=="quality normal")_lite=false;
  if(argument=="gaps grey")_grey=true;if(argument=="gaps transparent")_grey=false;
  if(double.IsNaN(_rate)||double.IsInfinity(_rate)||_rate<1)throw new Exception("Use a finite rate of at least 1 Hz.");
  if(_size!=256&&_size!=512&&_size!=1024&&_size!=2048)throw new Exception("Use size 256, 512, 1024 or 2048.");
  if(double.IsNaN(_fov)||double.IsInfinity(_fov)||_fov<60||_fov>120)throw new Exception("Use fov 60..120 degrees.");
  if(double.IsNaN(_feather)||double.IsInfinity(_feather)||_feather<0||_feather>25)throw new Exception("Use feather 0..25 degrees.");
  if(double.IsNaN(_saturation)||double.IsInfinity(_saturation)||_saturation<0||_saturation>2)throw new Exception("Use saturation 0..2.");
  if(double.IsNaN(_opacity)||double.IsInfinity(_opacity)||_opacity<0||_opacity>1||double.IsNaN(_back)||double.IsInfinity(_back)||_back<0||_back>1)throw new Exception("Use opacity, transparency and back values between 0 and 1.");
  Setup();
 }
 catch(Exception e){_running=false;Runtime.UpdateFrequency=UpdateFrequency.None;Echo(e.Message);}
}
