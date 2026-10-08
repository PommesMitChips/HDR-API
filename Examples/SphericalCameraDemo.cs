// Whole PB script: Console/Projector "HDR Display", live camera LCDs "HDR Video [N]".
// Each LCD's Camera Display Custom Data names its actual camera (or 0:Camera Name).
// HDR API 0.7.5+; HDR Client Renderer 0.7.8 and CameraLCD relay the image.
// Auto-detects one 360 Camera bound to five/six dedicated buffers; "360" forces that mode.
// Cube capture uses the separate 360 Camera mod and Camera360Client plug-in.
// Default: 360-degree azimuth around the camera's physical optical axis,
// 180-degree full dome opening (polar angle 0..90 degrees from forward).
// Five cube targets contribute; side images are cropped at the forward plane.
// Up to seven feeds: the eighth screen is the optional background for unseen directions.
Func<string,object[],object> _hdr;
IMyTerminalBlock _display;
double _rate=6,_fov=Math.PI/3;
int _resolution=512;
bool _guides=false,_grey=true,_joined=true,_running,_use360,_hemisphere=true;
int _cameraMode; // 0 automatic, 1 one 360 Camera, 2 manual array, 3 one forward view per 360 Camera.
IMyCameraBlock _reference;
Vector3D _cropDirection;
Vector4[] Crop()
{
 if(!_hemisphere)return new Vector4[0];
 var forward=Pose(_reference).Backward;_cropDirection=forward;var normal=-forward;
 return new[]{new Vector4((float)normal.X,(float)normal.Y,(float)normal.Z,(float)Vector3D.Dot(normal,new Vector3D(0,1.5,0)))};
}
sealed class Feed
{
 public IMyTextPanel Lcd;
 public IMyCameraBlock Camera;
 public MatrixD Pose;
 public string Data,Id;
 public int Face=-1;
 public bool Hemisphere;
}
List<Feed> _feeds=new List<Feed>();
object H(string op,params object[] args)
{
 if(_hdr==null){var p=Me.GetProperty("HDR.Draw");if(p==null)throw new Exception("Enable HDR API 0.7.5 and reload.");_hdr=p.As<Func<string,object[],object>>().GetValue(Me);}
 return _hdr(op,args);
}
MatrixD FaceBasis(int face)
{
 var direction=new[]{-Vector3D.UnitZ,Vector3D.UnitX,Vector3D.UnitZ,-Vector3D.UnitX,Vector3D.UnitY,-Vector3D.UnitY}[face];
 return MatrixD.CreateWorld(Vector3D.Zero,direction,face==4?Vector3D.UnitZ:face==5?-Vector3D.UnitZ:Vector3D.UnitY);
}
bool Is360(IMyCameraBlock camera){string type=camera.BlockDefinition.SubtypeName;return type=="Camera360Large"||type=="Camera360Small";}
int SourceFace(IMyTextPanel lcd,IMyCameraBlock camera)
{
 if(!Is360(camera))return -1;
 foreach(string raw in lcd.CustomData.Split('\n'))
 {string line=raw.Trim();if(!line.StartsWith("Camera360.Face=",StringComparison.OrdinalIgnoreCase))continue;string value=line.Substring(15).Trim().ToLowerInvariant();int face;if(int.TryParse(value,out face)&&face>=0&&face<6)return face;var names=new[]{"front","right","back","left","up","down"};for(int i=0;i<6;i++)if(value==names[i])return i;throw new Exception("Use Camera360.Face=0..5 in "+lcd.CustomName);}
 return 0;
}
bool SourceHemisphere(IMyTextPanel lcd,IMyCameraBlock camera)
{
 if(!Is360(camera))return false;
 foreach(string raw in lcd.CustomData.Split('\n'))
 {
  string line=raw.Trim();const string key="Camera360.Coverage=";
  if(!line.StartsWith(key,StringComparison.OrdinalIgnoreCase))continue;
  string value=line.Substring(key.Length).Trim();
  if(value.Equals("hemisphere",StringComparison.OrdinalIgnoreCase))return true;
  if(value.Equals("sphere",StringComparison.OrdinalIgnoreCase))return false;
  throw new Exception("Use Camera360.Coverage=hemisphere or sphere in "+lcd.CustomName);
 }
 return true;
}
MatrixD Pose(IMyCameraBlock camera,int face=-1)
{
 var inverse=MatrixD.Invert(_display.WorldMatrix);var pose=MatrixD.Identity;
 var frame=face<0?camera.WorldMatrix:FaceBasis(face)*camera.WorldMatrix;
 pose.Right=Vector3D.TransformNormal(-frame.Right,inverse);
 pose.Up=Vector3D.TransformNormal(frame.Up,inverse);
 pose.Backward=Vector3D.TransformNormal(frame.Forward,inverse);
 pose.Translation=new Vector3D(0,1.5,0);return pose;
}
Vector4[] FeedCrop(Feed feed,Vector4[] global)
{
 if(!feed.Hemisphere)return global;
 // Capture coverage follows this source's physical axis, never its cube face.
 var normal=-Pose(feed.Camera).Backward;
 var planes=new Vector4[global.Length+1];for(int i=0;i<global.Length;i++)planes[i]=global[i];
 planes[global.Length]=new Vector4((float)normal.X,(float)normal.Y,(float)normal.Z,(float)Vector3D.Dot(normal,new Vector3D(0,1.5,0)));
 return planes;
}
IMyCameraBlock NamedCamera(string name,IMyTextPanel lcd,List<IMyCameraBlock> cameras)
{
 IMyCameraBlock result=null;
 // CameraLCD searches the LCD's own grid first, then its mechanical construct.
 foreach(var camera in cameras)if(camera.CubeGrid==lcd.CubeGrid&&camera.CustomName==name)
 {if(result!=null)throw new Exception("Duplicate camera name: "+name);result=camera;}
 if(result!=null)return result;
 foreach(var camera in cameras)if(camera.IsSameConstructAs(lcd)&&camera.CustomName==name)
 {if(result!=null)throw new Exception("Duplicate camera name: "+name);result=camera;}
 return result;
}
IMyCameraBlock SourceCamera(IMyTextPanel lcd,List<IMyCameraBlock> cameras)
{
 var lines=(lcd.CustomData??"").Replace("\r","").Split('\n');
 foreach(string line in lines)if(line.StartsWith("0:")&&line.Length>2)return NamedCamera(line.Substring(2),lcd,cameras);
 foreach(string line in lines){var camera=NamedCamera(line,lcd,cameras);if(camera!=null)return camera;}
 return null;
}
bool VideoName(string name)
{
 if(name=="HDR Video")return true;
 if(!name.StartsWith("HDR Video "))return false;
 string suffix=name.Substring(10);if(suffix.Length==0)return false;
 foreach(char character in suffix)if(character<'0'||character>'9')return false;
 int number;return int.TryParse(suffix,out number)&&number>0;
}
string CameraData(IMyTextPanel lcd,IMyCameraBlock camera,int face,List<IMyCameraBlock> cameras)
{
 var text=new StringBuilder("0:"+camera.CustomName+"\nCamera360.Face="+face+"\nCamera360.Coverage="+(_hemisphere?"hemisphere":"sphere"));
 if(!string.IsNullOrEmpty(lcd.CustomData))foreach(string raw in lcd.CustomData.Replace("\r","").Split('\n'))
 {
  string line=raw.Trim();
  if(line.StartsWith("0:")||line.StartsWith("Camera360.Face=",StringComparison.OrdinalIgnoreCase)||line.StartsWith("Camera360.Coverage=",StringComparison.OrdinalIgnoreCase))continue;
  // Replace only Camera Display's surface-0 binding; preserve other surfaces,
  // notes and unrelated plug-in keys on the dedicated HDR Video buffers.
  if(cameras.Exists(candidate=>candidate.CustomName==raw))continue;
  text.Append('\n').Append(raw);
 }
 return text.ToString();
}
IMyCameraBlock SelectPanorama(List<IMyTextPanel> lcds,List<IMyCameraBlock> cameras)
{
 if(_cameraMode==2)return null;
 var panoramas=cameras.FindAll(camera=>Is360(camera));
 if(_cameraMode==1)
 {
  if(panoramas.Count!=1)throw new Exception("360 mode needs exactly one 360 Camera block on this construct (from the separate 360 Camera mod).");
  return panoramas[0];
 }
 if(_hemisphere?lcds.Count!=5&&lcds.Count!=6:lcds.Count!=6)return null;
 IMyCameraBlock selected=null;
 foreach(var lcd in lcds)
 {
  var camera=SourceCamera(lcd,cameras);if(camera==null)continue;
  if(!Is360(camera)||selected!=null&&selected.EntityId!=camera.EntityId)return null;
  selected=camera;
 }
 // Existing directional cameras do not prevent choosing the one panorama
 // actually bound to these buffers. Mixed arrays keep their bindings.
 return selected??(panoramas.Count==1?panoramas[0]:null);
}
void ConfigurePanorama(List<IMyTextPanel> lcds,IMyCameraBlock camera,List<IMyCameraBlock> cameras)
{
 if(_hemisphere?lcds.Count!=5&&lcds.Count!=6:lcds.Count!=6)throw new Exception(_hemisphere?"360 hemisphere needs five or six dedicated source LCDs named HDR Video [N].":"360 sphere needs six dedicated source LCDs named HDR Video [N].");
 foreach(var lcd in lcds)if(Math.Abs(lcd.TextureSize.X-lcd.TextureSize.Y)>1)throw new Exception("360 cube capture needs square source LCD textures: "+lcd.CustomName);
 lcds.Sort((a,b)=>string.CompareOrdinal(a.CustomName,b.CustomName));
 var faces=lcds.Count==6?new[]{0,1,2,3,4,5}:new[]{0,1,3,4,5};
 for(int i=0;i<lcds.Count;i++){string data=CameraData(lcds[i],camera,faces[i],cameras);if(lcds[i].CustomData!=data)lcds[i].CustomData=data;}
}
void ConfigureCameraArray(List<IMyTextPanel> lcds,List<IMyCameraBlock> cameras)
{
 var panoramas=cameras.FindAll(camera=>Is360(camera));
 if(panoramas.Count<1||panoramas.Count>7||lcds.Count!=panoramas.Count)throw new Exception("cameras mode needs one dedicated HDR Video LCD per 360 Camera, up to seven cameras.");
 foreach(var lcd in lcds)if(Math.Abs(lcd.TextureSize.X-lcd.TextureSize.Y)>1)throw new Exception("360 cube capture needs square source LCD textures: "+lcd.CustomName);
 // CameraLCD resolves camera names, so new blocks with duplicate default
 // names need distinct names before assigning the per-camera bindings.
 var duplicates=panoramas.FindAll(camera=>cameras.FindAll(other=>other.CustomName==camera.CustomName).Count>1);
 duplicates.Sort((a,b)=>a.EntityId.CompareTo(b.EntityId));int suffix=1;
 foreach(var camera in duplicates)
 {
  string name;do{name="HDR Dome "+suffix++;}while(cameras.Exists(other=>other.CustomName==name));
  camera.CustomName=name;
 }
 panoramas.Sort((a,b)=>{int order=string.CompareOrdinal(a.CustomName,b.CustomName);return order!=0?order:a.EntityId.CompareTo(b.EntityId);});
 lcds.Sort((a,b)=>string.CompareOrdinal(a.CustomName,b.CustomName));
 for(int i=0;i<lcds.Count;i++){string data=CameraData(lcds[i],panoramas[i],0,cameras);if(lcds[i].CustomData!=data)lcds[i].CustomData=data;}
}
void Retire()
{
 for(int i=0;i<7;i++){try{H("screen","cam"+i);H("screen-remove");}catch{}}
 try{H("screen","pan-gap");H("screen-remove");}catch{}
}
void Status()
{
 var text=new StringBuilder("Spherical Camera v10 / "+(_hemisphere?"180-degree forward dome":"full sphere")+" / "+(_cameraMode==3?"one forward view per camera":_use360?"360 capture":"camera array")+" / "+(_joined?"joined":"patches")+" / gaps "+(_grey?"grey":"transparent")+"\n");
 text.Append(_feeds.Count).Append(" auto-oriented feeds, ").Append(_resolution).Append("px request ceiling / ").Append(_rate).Append(" Hz\n");
 foreach(var feed in _feeds){text.Append(feed.Lcd.CustomName).Append(" -> ").Append(feed.Camera.CustomName);if(feed.Face>=0)text.Append(" / face ").Append(feed.Face);text.Append('\n');}
 text.Append("Reference camera: ").Append(_reference.CustomName).Append("\n360-degree azimuth around physical forward; polar angle 0..90 degrees in hemisphere mode.\nCentre: 1.5m above anchor; radius 3m.\nCommands: hemisphere, sphere, auto, 360, array, cameras, gaps grey, gaps transparent, join, patches, guides, video, size 512, rate 30, fov 60, clear.\n360 uses five square 90-degree targets from one camera for its dome. cameras uses one forward 90-degree view per camera and one LCD each; it does not capture each camera's full dome. Normal feeds must match camera zoom. Unknown views stay empty; nearby array seams can retain parallax.");Echo(text.ToString());
}
void Setup()
{
 _display=H("findtarget","HDR Display") as IMyTerminalBlock;
 if(_display==null)throw new Exception("Name a Console/Projector HDR Display.");
 var lcds=new List<IMyTextPanel>();var cameras=new List<IMyCameraBlock>();
 GridTerminalSystem.GetBlocksOfType(lcds,lcd=>lcd.IsSameConstructAs(Me)&&VideoName(lcd.CustomName));
 GridTerminalSystem.GetBlocksOfType(cameras,camera=>camera.IsSameConstructAs(Me));
 if(_cameraMode==3){ConfigureCameraArray(lcds,cameras);_use360=true;}
 else{var panorama=SelectPanorama(lcds,cameras);_use360=panorama!=null;if(_use360)ConfigurePanorama(lcds,panorama,cameras);}
 var feeds=new List<Feed>();var seen=new HashSet<string>();var missing=new List<string>();
 foreach(var lcd in lcds)
 {
  var camera=SourceCamera(lcd,cameras);if(camera==null){missing.Add(lcd.CustomName);continue;}
  int face=SourceFace(lcd,camera);
  bool sourceHemisphere=SourceHemisphere(lcd,camera);
  if(face==2&&sourceHemisphere)continue;
  if(seen.Add(camera.EntityId.ToString()+":"+face))feeds.Add(new Feed{Lcd=lcd,Camera=camera,Face=face,Hemisphere=sourceHemisphere,Pose=Pose(camera,face),Data=lcd.CustomData});
 }
 if(missing.Count>0)throw new Exception("Cannot identify source camera for "+string.Join(", ",missing)+". Its Custom Data must contain the exact camera name.");
 if(feeds.Count==0||feeds.Count>7)throw new Exception("Use 1..7 unique camera feeds on LCDs named HDR Video [N].");
 _reference=feeds[0].Camera;
 if(!_use360)foreach(var feed in feeds)if(feed.Camera.CustomName=="HDR Camera 1"){_reference=feed.Camera;break;}
 var crop=Crop();
 feeds.Sort((a,b)=>{double pitchA=Math.Asin(Math.Max(-1,Math.Min(1,a.Pose.Backward.Y))),pitchB=Math.Asin(Math.Max(-1,Math.Min(1,b.Pose.Backward.Y)));int n=pitchB.CompareTo(pitchA);if(n!=0)return n;n=Math.Atan2(a.Pose.Backward.X,a.Pose.Backward.Z).CompareTo(Math.Atan2(b.Pose.Backward.X,b.Pose.Backward.Z));return n!=0?n:a.Camera.EntityId.CompareTo(b.Camera.EntityId);});
 if((_display.BlockDefinition.SubtypeName??"").IndexOf("Console",StringComparison.OrdinalIgnoreCase)>=0)H("volume",false);
 H("range",0);H("budget",0,0,60000);Retire();_feeds=feeds;
 for(int i=0;i<feeds.Count;i++)
 {
  var feed=feeds[i];feed.Id="cam"+i;
  H("screen",feed.Id,feed.Pose,2,2,2,2);
  H("screen-surface","sphere",3,Math.PI/2,Math.PI/2,"inside");H("screen-mapping","pinhole");
  H("screen-camera",new Vector3D(0,0,-1),Vector3D.Zero,Vector3D.UnitY,feed.Face>=0?Math.PI/2:_fov);H("screen-quality",.01);
  var size=feed.Lcd.TextureSize;double scale=Math.Min(1,_resolution/Math.Max(size.X,size.Y));
  H("screen-resolution",Math.Max(16,(int)(size.X*scale)),Math.Max(16,(int)(size.Y*scale)));
  H("screen-refresh",_rate);H("screen-background","#00000000");
  H("screen-source","lcd-texture",feed.Lcd.EntityId.ToString()+":0");
  if(_joined)H("screen-join","cameras");
  H("screen-clip",FeedCrop(feed,crop));
  if(_guides)
  {
   string label=feed.Camera.CustomName.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;");
   H("svg","guide","<svg viewBox='0 0 200 200'><rect x='1' y='1' width='198' height='198' fill='none' stroke='#00ffff' stroke-width='1'/><text x='100' y='20' font-size='8' text-anchor='middle' fill='white'>"+label+"</text></svg>",MatrixD.CreateScale(.01),12);
  }
 }
 if(_grey)
 {
  H("screen","pan-gap",MatrixD.CreateTranslation(0,1.5,0),2,2,2,2);
  H("screen-surface","sphere",3.015,Math.PI*2,Math.PI,"inside");H("screen-quality",.025);H("screen-background","#252b30");
  H("screen-clip",crop);
 }
 _running=true;Runtime.UpdateFrequency=UpdateFrequency.Update10;Status();
}
public void Main(string argument,UpdateType source)
{
 try
 {
  argument=(argument??"").Trim().ToLowerInvariant();
  if((source&UpdateType.Update10)!=0&&argument.Length==0&&_running)
  {
   if(!(bool)H("screen-exists","cam0")){Setup();return;}
   var direction=Pose(_reference).Backward;bool cropChanged=_hemisphere&&Vector3D.DistanceSquared(direction,_cropDirection)>1e-8;
   Vector4[] crop=cropChanged?Crop():null;
   for(int i=0;i<_feeds.Count;i++)
   {
    var feed=_feeds[i];if(feed.Lcd.CustomData!=feed.Data||!feed.Camera.IsFunctional){Setup();return;}
    var pose=Pose(feed.Camera,feed.Face);
    bool poseChanged=Vector3D.DistanceSquared(pose.Right,feed.Pose.Right)>1e-8||Vector3D.DistanceSquared(pose.Up,feed.Pose.Up)>1e-8||Vector3D.DistanceSquared(pose.Backward,feed.Pose.Backward)>1e-8;
    if(poseChanged)
    {H("screen",feed.Id);H("screen-pose",pose);feed.Pose=pose;}
    if(cropChanged||poseChanged&&feed.Hemisphere){H("screen",feed.Id);H("screen-clip",FeedCrop(feed,crop??Crop()));}
   }
   if(cropChanged&&_grey){H("screen","pan-gap");H("screen-clip",crop);}
   return;
  }
  if(argument.StartsWith("rate "))_rate=double.Parse(argument.Substring(5));
  if(argument.StartsWith("size "))_resolution=int.Parse(argument.Substring(5));
  if(argument.StartsWith("fov "))_fov=double.Parse(argument.Substring(4))*Math.PI/180;
  if(argument=="guides")_guides=true;if(argument=="video")_guides=false;
  if(argument=="gaps grey")_grey=true;if(argument=="gaps transparent")_grey=false;
  if(argument=="join")_joined=true;if(argument=="patches")_joined=false;
  if(argument=="auto")_cameraMode=0;if(argument=="360")_cameraMode=1;if(argument=="array")_cameraMode=2;
  if(argument=="cameras"){_cameraMode=3;_hemisphere=false;}
  if(argument=="hemisphere")_hemisphere=true;if(argument=="sphere")_hemisphere=false;
  if(argument=="clear"){if(_display!=null){H("target",_display);Retire();}_running=false;Runtime.UpdateFrequency=UpdateFrequency.None;Echo("Camera sphere cleared.");return;}
  if(_resolution<16||_resolution>4096)throw new Exception("Use size 16..4096; the source LCD and local relay limits still apply.");
  if(double.IsNaN(_rate)||double.IsInfinity(_rate)||_rate<1||_rate>60)throw new Exception("Use rate 1..60.");
  if(double.IsNaN(_fov)||double.IsInfinity(_fov)||_fov<.1||_fov>2.8)throw new Exception("Use a camera FOV between 6 and 160 degrees, matching the source image.");
  Setup();
 }
 catch(Exception e){_running=false;Runtime.UpdateFrequency=UpdateFrequency.None;Echo(e.Message);}
}
