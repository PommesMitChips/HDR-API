using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.ModAPI.Ingame;
using VRageMath;

internal static class DirectCameraSphereDemoTests
{
 public static int Run(CSharpCompilation compilation)
 {
  using var image=new MemoryStream();var emitted=compilation.Emit(image);
  if(!emitted.Success)throw new Exception(string.Join("\n",emitted.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
  image.Position=0;var type=AssemblyLoadContext.Default.LoadFromStream(image).GetType("Program",true);
  int checks=0;
  void Check(bool value,string why){if(!value)throw new Exception("Direct sphere PB: "+why);checks++;}
  var normal=Camera(900,"Ignored Camera","LargeCameraBlock");
  for(int count=1;count<=6;count++)
  {
   var fixture=new Fixture(type,2.5f,"LargeConsole");
   fixture.Cameras.Add(normal);for(int i=count;i>=1;i--)fixture.Cameras.Add(Camera(100+i,"Same default name","Camera360Large",()=>fixture.Working));
   fixture.Owned.Add("cam1");fixture.Owned.Add("pan-gap");fixture.Foreign.Add("other-player-hud");fixture.Main("");
   Check(fixture.Echoes.Last().Contains(count+" cameras"),"one through six new cameras are accepted");
   var source=fixture.Commands.Last(c=>c.Op=="screen-source");var ids=((string)source.Args[1]).Split(',').Select(long.Parse).ToArray();
   Check((string)source.Args[0]=="camera-panorama"&&ids.Length==count&&ids.All(id=>id>0)&&ids.SequenceEqual(Enumerable.Range(1,count).Select(i=>101L+i-1)),"direct source uses positive sorted distinct physical camera IDs without LCD bindings");
   Check(fixture.LcdQueries==0,"setup neither discovers LCDs nor renames or rewrites cameras");
   var pose=(MatrixD)fixture.Commands.Last(c=>c.Op=="screen"&&c.Args.Length>1).Args[1];
   Check(Math.Abs(pose.Translation.Y-4)<1e-9,"large grid sphere moves one grid block plus1.5m above anchor");
   Check(fixture.Commands.Any(c=>c.Op=="screen-panorama"&&(double)c.Args[0]==105&&(double)c.Args[1]==8&&(double)c.Args[2]==1.15&&(int)c.Args[3]==1024),"default direct capture requests105degrees,8degree feather,1.15saturation,1024ceiling");
   Check(fixture.Commands.Any(c=>c.Op=="screen-resolution"&&(int)c.Args[0]==2048&&(int)c.Args[1]==1024)&&fixture.Commands.Any(c=>c.Op=="screen-refresh"&&(double)c.Args[0]==30),"default spherical panorama requests2048x1024 at30Hz");
   Check(fixture.Commands.Any(c=>c.Op=="screen-two-sided"&&(bool)c.Args[0]&&(int)c.Args[1]==1&&(double)c.Args[2]==1)&&fixture.Commands.Any(c=>c.Op=="screen-opacity"&&(double)c.Args[0]==.85),"both-side presentation starts with requested opacity and outside multiplier");
   Check(fixture.Commands.Any(c=>c.Op=="volume"&&!(bool)c.Args[0]),"console grid volume is disabled for camera sphere");
   Check(!fixture.Owned.Contains("cam1")&&!fixture.Owned.Contains("pan-gap")&&fixture.Foreign.Contains("other-player-hud"),"migration retires only known PB screen IDs and preserves unrelated player's HUD");
   Check(fixture.Frequency==UpdateFrequency.Update100,"script leaves capture scheduling to client and uses slow liveness polling");
   int before=fixture.Mutations;fixture.Main("",UpdateType.Update100);
   Check(fixture.Mutations==before,"ordinary PB heartbeat does not republish geometry or source frames");
   fixture.Working=false;fixture.Main("",UpdateType.Update100);
   Check(fixture.Mutations==before,"camera power state is left to native client policy without PB republish");
   fixture.Working=true;fixture.Owned.Remove("panorama");fixture.Main("",UpdateType.Update100);
   Check(fixture.Owned.Contains("panorama")&&fixture.Mutations>before,"lost scene declaration is recreated on next liveness poll");
  }
  var small=new Fixture(type,.5f,"SmallProjector");small.Cameras.Add(Camera(77,"Dome","Camera360Small"));small.Main("");
  Check(Math.Abs(((MatrixD)small.Commands.Last(c=>c.Op=="screen"&&c.Args.Length>1).Args[1]).Translation.Y-2)<1e-9,"small grid center rises exactly2m");
  Check(!small.Commands.Any(c=>c.Op=="volume"),"ordinary projectors retain their volume behavior");
  var fallback=new Fixture(type,2.5f,"LargeProjector");fallback.Cameras.Add(Camera(10,"HDR Camera Left","LargeCameraBlock"));fallback.Cameras.Add(Camera(20,"Other Camera","LargeCameraBlock"));fallback.Main("");
  Check((string)fallback.Commands.Last(c=>c.Op=="screen-source").Args[1]=="10","named ordinary-camera fallback remains available when no360blocks exist");
  bool removed=false;var lifetime=new Fixture(type,2.5f,"LargeConsole");lifetime.Cameras.Add(Camera(11,"Old Dome","Camera360Large",null,()=>removed));lifetime.Main("");int lifetimeWrites=lifetime.Mutations;
  removed=true;lifetime.Cameras.Clear();lifetime.Cameras.Add(Camera(22,"Replacement Dome","Camera360Large"));lifetime.Main("",UpdateType.Update100);
  Check((string)lifetime.Commands.Last(c=>c.Op=="screen-source").Args[1]=="22"&&lifetime.Mutations>lifetimeWrites,"deleted camera binding is replaced on liveness poll using current construct cameras");
  var invalids=new[]{"rate -1","rate NaN","rate 0","size 4096","fov Infinity","fov 121","feather -1","feather 26","saturation 2.1","opacity -1","transparency 1.1","back NaN","size nope"};
  foreach(string argument in invalids)
  {var invalid=new Fixture(type,2.5f,"LargeConsole");invalid.Cameras.Add(Camera(10,"Dome","Camera360Large"));invalid.Main(argument);Check(invalid.Mutations==0&&invalid.Commands.Count==0&&invalid.Frequency==UpdateFrequency.None,"invalid setting fails before API writes: "+argument);}
  foreach(int count in new[]{0,7})
  {var invalid=new Fixture(type,2.5f,"LargeConsole");for(int i=0;i<count;i++)invalid.Cameras.Add(Camera(10+i,"Dome","Camera360Large"));invalid.Main("");Check(invalid.Mutations==0&&!invalid.Owned.Contains("panorama")&&invalid.Frequency==UpdateFrequency.None,"invalid number of cameras never creates partially configured screen");}
  var controls=new Fixture(type,2.5f,"LargeConsole");controls.Cameras.Add(Camera(10,"Dome","Camera360Large"));controls.Main("");
  controls.Main("inside");Check(controls.Commands.Last(c=>c.Op=="screen-two-sided").Args[0] is false,"inside command removes exterior rendering");
  controls.Main("both");Check(controls.Commands.Last(c=>c.Op=="screen-two-sided").Args[0] is true,"both command restores exterior rendering");
  controls.Main("transparency 0.25");Check((double)controls.Commands.Last(c=>c.Op=="screen-opacity").Args[0]==.75,"transparency maps to opacity");
  controls.Main("size 512");Check(controls.Commands.Last(c=>c.Op=="screen-resolution").Args.SequenceEqual(new object[]{1024,512})&&(int)controls.Commands.Last(c=>c.Op=="screen-panorama").Args[3]==512,"size changes capture and panorama ceilings together");
  controls.Main("size 2048");Check(controls.Commands.Last(c=>c.Op=="screen-resolution").Args.SequenceEqual(new object[]{4096,2048})&&(int)controls.Commands.Last(c=>c.Op=="screen-panorama").Args[3]==2048,"higher detail changes capture and panorama ceilings together");
  controls.Main("rate 144");Check((double)controls.Commands.Last(c=>c.Op=="screen-refresh").Args[0]==144,"rates above old ceiling remain declarative requests");
  controls.Main("rate 15");Check((double)controls.Commands.Last(c=>c.Op=="screen-refresh").Args[0]==15,"rate control changes declarative cadence request");
  controls.Main("gaps grey");Check((string)controls.Commands.Last(c=>c.Op=="screen-background").Args[0]=="#252b3030","grey gaps control is applied");
  controls.Main("gaps transparent");Check((string)controls.Commands.Last(c=>c.Op=="screen-background").Args[0]=="#00000000","transparent gaps control is applied");
  controls.Main("fov 110");Check((double)controls.Commands.Last(c=>c.Op=="screen-panorama").Args[0]==110,"FOV command changes native capture declaration");
  controls.Main("feather 12");Check((double)controls.Commands.Last(c=>c.Op=="screen-panorama").Args[1]==12,"feather command changes seam blend declaration");
  controls.Main("saturation 1.3");Check((double)controls.Commands.Last(c=>c.Op=="screen-panorama").Args[2]==1.3,"saturation command changes panorama declaration");
  controls.Main("back 0.5");Check((double)controls.Commands.Last(c=>c.Op=="screen-two-sided").Args[2]==.5,"back command changes exterior alpha multiplier");
  controls.Main("clear");Check(!controls.Owned.Contains("panorama")&&controls.Frequency==UpdateFrequency.None,"clear retires own screen and stops polling");
  return checks;
 }
 static IMyCameraBlock Camera(long id,string name,string subtype,Func<bool> working=null,Func<bool> closed=null)
 {
  return (IMyCameraBlock)DrawTestProxy.Make(typeof(IMyCameraBlock),(method,args)=>method.Name switch{
   "get_EntityId"=>id,"get_CustomName"=>name,"get_Closed"=>closed==null?false:closed(),"get_IsWorking"=>working==null?true:working(),"IsSameConstructAs"=>true,
   "get_BlockDefinition"=>new VRage.ObjectBuilders.SerializableDefinitionId(typeof(Sandbox.Common.ObjectBuilders.MyObjectBuilder_CameraBlock),subtype),
   _=>throw new Exception("Unexpected direct camera operation: "+method.Name)});
 }
 sealed class Fixture
 {
  public List<IMyCameraBlock> Cameras=new List<IMyCameraBlock>();
  public List<(string Op,object[] Args)> Commands=new List<(string,object[])>();
  public List<string> Echoes=new List<string>();public HashSet<string> Owned=new HashSet<string>(),Foreign=new HashSet<string>();
  public int LcdQueries,Mutations;public UpdateFrequency Frequency;public bool Working=true;
  readonly Type Type;readonly object Program;string Selected;
  public Fixture(Type type,float gridSize,string subtype)
  {
   Type=type;Program=RuntimeHelpers.GetUninitializedObject(type);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
   void Set(string name,object value)=>type.GetField(name,flags).SetValue(Program,value);
   var grid=(VRage.Game.ModAPI.Ingame.IMyCubeGrid)DrawTestProxy.Make(typeof(VRage.Game.ModAPI.Ingame.IMyCubeGrid),(m,a)=>m.Name=="get_GridSize"?gridSize:throw new Exception(m.Name));
   var display=(IMyTerminalBlock)DrawTestProxy.Make(typeof(IMyTerminalBlock),(m,a)=>m.Name switch{
    "get_CubeGrid"=>grid,"get_BlockDefinition"=>new VRage.ObjectBuilders.SerializableDefinitionId(typeof(Sandbox.Common.ObjectBuilders.MyObjectBuilder_Projector),subtype),_=>throw new Exception(m.Name)});
   var me=(IMyProgrammableBlock)DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>m.Name=="get_EntityId"?500L:throw new Exception(m.Name));
   var terminals=(IMyGridTerminalSystem)DrawTestProxy.Make(typeof(IMyGridTerminalSystem),(m,a)=>{
    if(m.Name!="GetBlocksOfType"||m.GetGenericArguments()[0]!=typeof(IMyCameraBlock)){LcdQueries++;throw new Exception("Unexpected terminal/LCD discovery: "+m.Name);}
    var list=(List<IMyCameraBlock>)a[0];var predicate=a.Length>1?a[1] as Func<IMyCameraBlock,bool>:null;foreach(var camera in Cameras)if(predicate==null||predicate(camera))list.Add(camera);return null;});
   var runtime=(IMyGridProgramRuntimeInfo)DrawTestProxy.Make(typeof(IMyGridProgramRuntimeInfo),(m,a)=>{
    if(m.Name=="set_UpdateFrequency"){Frequency=(UpdateFrequency)a[0];return null;}if(m.Name=="get_UpdateFrequency")return Frequency;throw new Exception(m.Name);});
   void Base(string name,object value)=>typeof(MyGridProgram).GetField("<"+name+">k__BackingField",flags).SetValue(Program,value);
   Base("Me",me);Base("GridTerminalSystem",terminals);Base("Runtime",runtime);Base("Echo",new Action<string>(text=>Echoes.Add(text)));
   Set("_hdr",new Func<string,object[],object>((op,args)=>{
    Commands.Add((op,(object[])args.Clone()));if(op=="findtarget")return display;if(op=="target")return true;if(op=="screen-exists")return Owned.Contains((string)args[0]);
    if(op=="screen"){Selected=(string)args[0];if(args.Length>1){Owned.Add(Selected);Mutations++;}return true;}
    if(op=="screen-remove"){Owned.Remove(Selected);Mutations++;return true;}Mutations++;return true;
   }));
   Set("_cameras",new List<IMyCameraBlock>());Set("_rate",30d);Set("_fov",105d);Set("_feather",8d);Set("_saturation",1.15d);Set("_opacity",.85d);Set("_back",1d);Set("_size",1024);Set("_both",true);
  }
  public void Main(string argument,UpdateType source=UpdateType.Terminal)
  {Type.GetMethod("Main",BindingFlags.Instance|BindingFlags.Public).Invoke(Program,new object[]{argument,source});}
 }
}
