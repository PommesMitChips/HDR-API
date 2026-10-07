// Paste the whole script. Name a Console or Projector HDR Display.
Func<string,object[],object> _hdr;
bool _flipped,_flat;
object H(string command,params object[] args)
{
 if(_hdr==null){var p=Me.GetProperty("HDR.Draw");if(p==null)throw new Exception("Enable HDR API 0.4 and reload the world.");_hdr=p.As<Func<string,object[],object>>().GetValue(Me);}
 return _hdr(command,args);
}
MatrixD ScreenPose(Vector3D position)
{return MatrixD.CreateRotationX(_flat?-Math.PI/2:0)*MatrixD.CreateRotationY(_flipped?Math.PI:0)*MatrixD.CreateTranslation(position);}
public void Main(string argument,UpdateType source)
{
 try
 {
  H("target","HDR Display");
  if(argument=="two-sided"||argument=="one-sided"){foreach(var id in new[]{"scene","menu"}){H("screen",id);H("screen-two-sided",argument=="two-sided",1,.35);}Echo("Updated screen sides (front 1, back .35).");return;}
  if(argument=="flip"||argument=="flat"||argument=="upright")
  {if(argument=="flip")_flipped=!_flipped;else _flat=argument=="flat";H("screen","scene");H("screen-pose",ScreenPose(new Vector3D(0,1.5,0)));H("screen","menu");H("screen-pose",ScreenPose(new Vector3D(-2,1.2,0)));Echo("Updated screen placement.");return;}
  if(argument=="clear"){H("screen","scene");H("screen-remove");H("screen","menu");H("screen-remove");Echo("Removed both projected screens.");return;}
  if(argument=="pause"||argument=="orbit"){H("screen","scene");H("screen-orbit",argument=="pause"?0:0.2);return;}
  if(argument=="toggle"){H("screen","scene");H("toggle","overlay");return;}
  if(argument.StartsWith("rate ")){double rate;if(!double.TryParse(argument.Substring(5),out rate))throw new Exception("Use rate 6, rate 10 or rate 15.");H("screen","scene");H("screen-refresh",rate);return;}
  H("volume",false); // The screen is outside the table's usual truncated pyramid.
  H("screen","scene",ScreenPose(new Vector3D(0,1.5,0)),2.4,1.35);
  H("clear");H("screen-background","#030a10");H("screen-refresh",10);
  var points=new List<Vector3D>();var triangles=new List<int>();var colors=new List<Vector4>();
  Box(points,triangles,colors,new Vector3D(-0.65,-0.25,0),new Vector3D(0.8,1.3,0.9),new Vector4(0.05f,0.7f,0.8f,1));
  Box(points,triangles,colors,new Vector3D(0.65,-0.45,0.7),new Vector3D(0.9,0.9,0.9),new Vector4(0.55f,0.18f,0.7f,1));
  Box(points,triangles,colors,new Vector3D(0,0,-0.85),new Vector3D(0.14,2.0,0.1),new Vector4(0.95f,0.4f,0.05f,1));
  Box(points,triangles,colors,new Vector3D(0,-1.05,0),new Vector3D(5,0.1,5),new Vector4(0.08f,0.14f,0.19f,1));
  H("screen-scene",points.ToArray(),triangles.ToArray(),colors.ToArray());
  H("screen-camera",new Vector3D(3,2,-5),Vector3D.Zero,Vector3D.Up,Math.PI/3,0.05,30);
  H("screen-raster",64,36);H("screen-orbit",0.2);
  H("text","title","SYNTHETIC VIEW",0,0.58,0,0.11,"cyan");H("layer","title","overlay");
  H("text","status","Perspective + depth / unknown stays empty",0,-0.58,0,0.045,"white");H("layer","status","overlay");
  H("circle","reticle",0,0,0,0.075,"#56e9ed",40,0.002,Vector3D.UnitZ);H("layer","reticle","overlay");H("layer-order","overlay",10);
  H("screen","menu",ScreenPose(new Vector3D(-2,1.2,0)),1.2,0.8);
  H("clear");H("screen-background","#05101c");
  var sprites=new VRage.Game.GUI.TextPanel.MySprite[] {
   new VRage.Game.GUI.TextPanel.MySprite {Data="SquareSimple",Position=new Vector2(256,256),Size=new Vector2(480,400),Color=new Color(14,40,58)},
   new VRage.Game.GUI.TextPanel.MySprite {Data="SquareSimple",Position=new Vector2(256,320),Size=new Vector2(380,120),Color=new Color(20,160,175,180)},
   new VRage.Game.GUI.TextPanel.MySprite {Data="Circle",Position=new Vector2(420,100),Size=new Vector2(35,35),Color=Color.Cyan}
  };
  H("sprites",sprites,512,512);
  H("text","title","HDR DECK",0,0.23,0,0.12,"cyan");H("layer","title","menu");
  H("text","status","Direct vectors + sprites\nNo physical LCD required",0,-0.05,0,0.055,"white");H("layer","status","menu");
  Echo("Projected screens created. Commands: demo, flip, flat, upright, two-sided, one-sided, pause, orbit, toggle, rate 10, clear. Camera imagery here is synthetic.");
 }
 catch(Exception e){Echo(e.Message);}
}
void Box(List<Vector3D> points,List<int> triangles,List<Vector4> colors,Vector3D center,Vector3D size,Vector4 color)
{
 int start=points.Count;for(int i=0;i<8;i++)points.Add(center+new Vector3D((i&1)==0?-size.X/2:size.X/2,(i&2)==0?-size.Y/2:size.Y/2,(i&4)==0?-size.Z/2:size.Z/2));
 int[] faces={0,2,3,0,3,1,4,5,7,4,7,6,0,4,6,0,6,2,1,3,7,1,7,5,0,1,5,0,5,4,2,6,7,2,7,3};
 for(int i=0;i<faces.Length;i+=3){triangles.Add(start+faces[i]);triangles.Add(start+faces[i+1]);triangles.Add(start+faces[i+2]);float shade=0.6f+0.08f*(i/6);colors.Add(new Vector4(color.X*shade,color.Y*shade,color.Z*shade,1));}
}
