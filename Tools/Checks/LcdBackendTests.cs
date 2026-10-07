using System.Collections;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using VRage.Game.GUI.TextPanel;
internal static class LcdBackendTests
{
 static int checks;
 static void Check(bool value,string name){if(!value)throw new Exception("LCD backend: "+name);checks++;}
 static void Reject(Action a,string name){try{a();}catch(ArgumentException){checks++;return;}throw new Exception("Expected LCD rejection: "+name);}
 static object Call(object o,string n,params object[] a)=>ClientReplicationTests.Call(o,n,a);
 static object Field(object o,string n)=>ClientReplicationTests.Field(o,n);
 static void Set(object o,string n,object v)=>ClientReplicationTests.SetField(o,n,v);
 public static int Run(){checks=0;Calibration();Refresh();Settings();Sampling();Dispatcher();AnimationSampling();return checks;}
 static void Calibration()
 {
  foreach(string model in new[]{"large/lcdpanel","large/lcdpanelwide","small/lcdpanel","small/lcdpanelwide","large/transparentlcd","small/transparentlcd"})
  for(int r=0;r<4;r++)
  {
   string surface=(model.Contains("transparent")?"TransparentScreenArea":"ScreenArea")+(r==0?"":(r*90).ToString());LcdScreenBasis basis;
   Check(LcdScreenCalibration.TryGet("Models/Cubes/"+model+".mwm",surface,r,out basis),"installed model and rotation calibration "+model+" "+r);
   Check(basis.HalfRight.Length()>0&&basis.HalfUp.Length()>0&&Vector3D.Dot(basis.Normal,Vector3D.UnitZ)>.99999,"calibration front normal and nonzero extents");
   var mapping=LcdPinnedProjection.CanvasToPanel(basis,2,2,1,1,0,0,0);
   Check(Vector3D.Distance(Vector3D.Transform(new Vector3D(-1,1,700),mapping),basis.Center-basis.HalfRight+basis.HalfUp)<1e-8,"canvas top-left follows actual rotated UVs and drops Z");
   var world=MatrixD.CreateRotationY(.7)*MatrixD.CreateTranslation(100,200,300);var target=Vector3D.Transform(Vector3D.Zero,mapping*world);
   Check(Vector3D.Distance(target,Vector3D.Transform(basis.Center,world))<1e-8,"physical screen follows moving/rotating block transforms");
  }
  LcdScreenBasis b;Check(!LcdScreenCalibration.TryGet("Mods/Other/Models/Cubes/large/lcdpanel.mwm","ScreenArea",0,out b),"rooted mod model never inherits vanilla calibration");
  Check(!LcdScreenCalibration.TryGet("Models/Cubes/large/lcdpanel.mwm","WrongScreen",0,out b),"unknown screen stays native");
  Check(!LcdScreenCalibration.TryGet("Models/Cubes/large/lcdpanel.mwm","ScreenArea90",0,out b),"mismatched rotation fails closed");
  LcdScreenCalibration.TryGet("Models/Cubes/large/lcdpanel.mwm","ScreenArea",0,out b);
  var left=LcdPinnedProjection.CanvasToPanel(b,4,2,2,1,0,0,0);var right=LcdPinnedProjection.CanvasToPanel(b,4,2,2,1,1,0,0);
  Check(Vector3D.Distance(Vector3D.Transform(Vector3D.Zero,left),b.Center+b.HalfRight)<1e-9&&Vector3D.Distance(Vector3D.Transform(Vector3D.Zero,right),b.Center-b.HalfRight)<1e-9,"wall tiles share their adjoining canvas boundary");
  var volume=new DisplayVolume(LcdPinnedProjection.ScreenFrame(b,MatrixD.Identity,.001),new[]{new Vector4D(1,0,0,1),new Vector4D(-1,0,0,1),new Vector4D(0,1,0,1),new Vector4D(0,-1,0,1)});
  var p=b.Center-b.HalfRight*2+b.Normal*.001;var q=b.Center+b.HalfRight*2+b.Normal*.001;
  Check(volume.ClipLine(ref p,ref q)&&Vector3D.Distance(p,b.Center-b.HalfRight+b.Normal*.001)<1e-8&&Vector3D.Distance(q,b.Center+b.HalfRight+b.Normal*.001)<1e-8,"vector lines clip to exact calibrated screen bounds");
  var clipped=volume.ClipTriangle(new DisplayVolume.Vertex(b.Center-b.HalfRight*2+b.Normal*.001,new Vector2(0,0)),new DisplayVolume.Vertex(b.Center+b.HalfRight*2+b.Normal*.001,new Vector2(1,0)),new DisplayVolume.Vertex(b.Center+b.HalfUp*.8+b.Normal*.001,new Vector2(.5f,1)));
  Check(clipped.Count>=3&&clipped.All(v=>Math.Abs(v.UV.X-(.5+Vector3D.Dot(v.Position-b.Center,b.HalfRight)/b.HalfRight.LengthSquared()*.25))<1e-6),"textured vector triangles interpolate UVs when clipped at tile edges");
  Check(LcdPinnedProjection.FrontFacing(b,MatrixD.Identity,b.Center+b.Normal)&&!LcdPinnedProjection.FrontFacing(b,MatrixD.Identity,b.Center-b.Normal),"rendering and picking share front-only visibility for transparent screens");
  Reject(()=>LcdPinnedProjection.CanvasToPanel(b,2,2,1,1,0,0,.1),"unbounded hover offset");
 }
 static void Refresh()
 {
  Check(LcdRefreshSchedule.Due(0,-1,6)&&!LcdRefreshSchedule.Due(.1,0,6)&&LcdRefreshSchedule.Due(1d/6,0,6),"six Hertz default sampling interval");
  Check(LcdRefreshSchedule.Due(1d/60,0,60)&&!LcdRefreshSchedule.Due(.001,0,60),"60Hz is bounded by the sampling interval");
  foreach(double rate in new[]{0d,61,double.NaN,double.PositiveInfinity})Reject(()=>LcdRefreshSchedule.Validate(rate),"invalid rate "+rate);
  var session=new HoloMapSession();Set(session,"_ticks",0);Check((bool)Call(session,"LcdSampleDue",20L,6d),"first sample admitted");
  Call(session,"InvalidateLcdSamples");Set(session,"_ticks",1);Check(!(bool)Call(session,"LcdSampleDue",20L,6d),"repeated settings invalidation cannot bypass viewer cadence");
  Set(session,"_ticks",10);Check((bool)Call(session,"LcdSampleDue",20L,6d),"sample admitted after ten ticks");
  foreach(double rate in new[]{24d,59d})
  {var phased=new HoloMapSession();int total=0;for(int tick=0;tick<60;tick++){Set(phased,"_ticks",tick);if((bool)Call(phased,"LcdSampleDue",20L,rate))total++;}Check(total==(int)rate,"fractional tick cadence preserves requested average rate "+rate);}
 }
 static void Settings()
 {
  using var gateway=new ClientReplicationTests.GatewayScope{Server=true,Dedicated=true};bool access=true;var registry=new Dictionary<long,object>();
  var caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>{if(m.Name=="get_EntityId")return 10L;if(m.Name=="get_OwnerId")return 77L;if(m.Name=="get_Closed")return false;if(m.Name=="IsSameConstructAs")return true;throw new Exception(m.Name);});registry[10]=caller;
  for(long id=20;id<=21;id++){long capture=id;registry[id]=DrawTestProxy.Make(typeof(IMyTextPanel),(m,a)=>{if(m.Name=="get_EntityId")return capture;if(m.Name=="get_Closed")return false;if(m.Name=="HasPlayerAccess")return capture==20||access;throw new Exception(m.Name);});}
  gateway.Install("Entities",(m,a)=>m.Name=="GetEntityById"&&registry.TryGetValue((long)a[0],out var value)?value:null);
  var packet=ClientReplicationTests.Snapshot();packet.Scenes.Add(ClientReplicationTests.Scene(21));for(int i=0;i<2;i++)packet.Scenes[i].Lcd=new HoloLcdData{Columns=2,Rows=1,Column=i,Width=4,Height=2,SourceId=20,CallerId=10};
  var session=new HoloMapSession();Call(session,"ApplySnapshot",packet);var draw=(Func<string,object[],object>)Call(session,"DrawEndpoint",caller);draw("target",new[]{registry[20]});
  draw("lcd-renderer",new object[]{"vector"});draw("lcd-refresh",new object[]{30});
  foreach(object scene in ((IDictionary)Field(session,"_scenes")).Values)Check((int)Field(scene,"LcdRenderer")==1&&(double)Field(scene,"LcdRefreshHz")==30,"group renderer/rate configured coherently");
  var settings=(MyTuple<string,double>)draw("lcd-settings",new object[0]);Check(settings.Item1=="vector"&&settings.Item2==30,"PB settings query reports requested mode/rate");
  access=false;Reject(()=>draw("lcd-renderer",new object[]{"native"}),"inaccessible group member preflight");
  foreach(object scene in ((IDictionary)Field(session,"_scenes")).Values)Check((int)Field(scene,"LcdRenderer")==1,"failed group change is atomic");access=true;
  Reject(()=>draw("lcd-renderer",new object[]{"anything"}),"unknown renderer");Reject(()=>draw("lcd-refresh",new object[]{120}),"unsafe requested rate");
  var capturePacket=(HoloSnapshot)Call(session,"CaptureSnapshot");Check(capturePacket.Scenes.All(s=>s.Lcd.Renderer==1&&s.Lcd.RefreshHz==30),"renderer settings survive full publication");
  var round=MyAPIGateway.Utilities.SerializeFromBinary<HoloSnapshot>(gateway.Serialize(capturePacket));Check(round.Scenes.All(s=>s.Lcd.Renderer==1&&s.Lcd.RefreshHz==30),"renderer settings survive actual protobuf");
  var before=ClientReplicationTests.Snapshot();before.Scenes[0].Lcd=new HoloLcdData();var after=ClientReplicationTests.Snapshot();after.Scenes[0].Lcd=new HoloLcdData{Renderer=1,RefreshHz=60};
  var build=typeof(HoloMapSession).GetMethod("BuildDelta",BindingFlags.NonPublic|BindingFlags.Static);var merge=typeof(HoloMapSession).GetMethod("MergeDelta",BindingFlags.NonPublic|BindingFlags.Static);
  var delta=(HoloSnapshot)build.Invoke(null,new object[]{before,after,1L});var merged=(HoloSnapshot)merge.Invoke(null,new object[]{before,delta,1L});Check(merged.Scenes[0].Lcd.Renderer==1&&merged.Scenes[0].Lcd.RefreshHz==60,"incremental scene metadata retains new renderer/rate settings");
  round.Scenes[1].Lcd.RefreshHz=double.NaN;Reject(()=>Call(session,"ApplySnapshot",round),"invalid replicated refresh");Check(((IDictionary)Field(session,"_scenes")).Count==2,"invalid configuration preserves prior scenes");
 }
 static void Sampling()
 {
  using var gateway=new ClientReplicationTests.GatewayScope();var session=new HoloMapSession();var packet=ClientReplicationTests.Snapshot();packet.Scenes[0].Lcd=new HoloLcdData{Renderer=1,RefreshHz=6};Call(session,"ApplySnapshot",packet);
  var scene=((IDictionary)Field(session,"_scenes"))[20L];var item=((IDictionary)Field(scene,"Items"))["10:line"];Set(session,"_ticks",0);
  Call(session,"ClientDisplayItem",scene,item,1);Call(session,"CompileQueuedDisplay");
  var sample=Call(session,"SampleVectorLcd",scene,scene,1);var entries=(IList)Field(sample,"Entries");Check(entries.Count==1,"vector sampling prepares immutable render metadata");
  var frozen=Field(entries[0],"Item");var old=(MatrixD)Field(frozen,"Transform");Set(item,"Transform",MatrixD.CreateTranslation(2,0,0));Set(session,"_ticks",1);sample=Call(session,"SampleVectorLcd",scene,scene,1);
  Check(((MatrixD)Field(Field(((IList)Field(sample,"Entries"))[0],"Item"),"Transform")).Equals(old),"animation metadata stays sampled between refreshes");
  Set(session,"_ticks",10);sample=Call(session,"SampleVectorLcd",scene,scene,1);Check(((MatrixD)Field(Field(((IList)Field(sample,"Entries"))[0],"Item"),"Transform")).Translation.X==2,"animation/content metadata refreshes at requested cadence");
  Set(session,"ClientLcdRefreshCap",1d);Set(scene,"LcdRefreshHz",60d);Set(item,"Transform",MatrixD.CreateTranslation(3,0,0));Set(session,"_ticks",11);Call(session,"InvalidateLcdSamples");sample=Call(session,"SampleVectorLcd",scene,scene,1);Check(((MatrixD)Field(Field(((IList)Field(sample,"Entries"))[0],"Item"),"Transform")).Translation.X==2,"viewer-local cap preserves the last picture and cannot be bypassed by settings churn");
 }
 static void Dispatcher()
 {
  using var gateway=new ClientReplicationTests.GatewayScope();int rotation=0,blankFrames=0;bool known=true;
  var modelType=typeof(VRage.ModAPI.IMyEntity).GetProperty("Model").PropertyType;
  var model=DrawTestProxy.Make(modelType,(m,a)=>m.Name=="get_AssetName"?(known?"Models/Cubes/Large/LCDPanel.mwm":"Mods/Unknown.mwm"):throw new Exception(m.Name));
  var rotationState=DrawTestProxy.Make(typeof(IMyLcdSurfaceComponent),(m,a)=>m.Name=="get_SelectedRotationIndex"?rotation:throw new Exception(m.Name));
  var components=DrawTestProxy.Make(typeof(VRage.Game.Components.Interfaces.IMyEntityComponentContainer),(m,a)=>{if(m.Name=="TryGet"){a[0]=rotationState;return true;}throw new Exception(m.Name);});
  string Name()=>"ScreenArea"+(rotation==0?"":(rotation*90).ToString());
  var panel=DrawTestProxy.Make(typeof(IMyTextPanel),(m,a)=>{
   if(m.Name=="get_Closed")return false;if(m.Name=="get_Model")return model;if(m.Name=="get_Components")return components;
   if(m.Name=="get_Name")return Name();if(m.Name=="get_ContentType")return ContentType.SCRIPT;if(m.Name=="get_Script")return "HDRAPI";
   if(m.Name=="DrawFrame")return new MySpriteDrawFrame(f=>{var sprites=new List<MySprite>();f.AddToList(sprites);Check(sprites.Count==0,"vector blank frame contains no native artwork");blankFrames++;});
   if(m.Name=="get_SurfaceSize"||m.Name=="get_TextureSize")return new Vector2(512);
   throw new Exception("Unexpected dispatcher panel call: "+m.Name);});
  var session=new HoloMapSession();var packet=ClientReplicationTests.Snapshot();packet.Scenes[0].Lcd=new HoloLcdData{Renderer=1};Call(session,"ApplySnapshot",packet);var scene=((IDictionary)Field(session,"_scenes"))[20L];
  Call(session,"DrawLcdDisplay",scene,panel,0,1);Call(session,"DrawLcdDisplay",scene,panel,0,1);Check(blankFrames==1,"unchanged selected surface is blanked only once");
  rotation=1;Call(session,"DrawLcdDisplay",scene,panel,0,1);rotation=0;Call(session,"DrawLcdDisplay",scene,panel,0,1);Check(blankFrames==3,"rotating away and revisiting an old native surface clears its prior texture binding");
  known=false;Call(session,"DrawLcdDisplay",scene,panel,0,1);Check(((IDictionary)Field(session,"_lcdVectorBlanked")).Count==0,"unsupported model automatically falls back and releases vector binding");
  Set(scene,"LcdRenderer",0);Set(scene,"LcdRefreshHz",60d);Set(session,"ClientLcdRefreshCap",60d);Call(session,"ClearLcdBackends");Set(session,"_ticks",0);Check((bool)Call(session,"NativeLcdDue",scene),"native sampling initializes normally");Set(session,"_ticks",1);Check(!(bool)Call(session,"NativeLcdDue",scene),"native requests above six remain bounded by the engine refresh schedule");
 }
 static void AnimationSampling()
 {
  using var gateway=new ClientReplicationTests.GatewayScope();var session=new HoloMapSession();var packet=ClientReplicationTests.Snapshot();var data=packet.Scenes[0];data.Lcd=new HoloLcdData{Renderer=1,RefreshHz=6};
  data.Animations.Add(new HoloAnimationData{CallerId=10,ConsoleId=20,Id="line",Base=data.Items[0].Transform,StartTick=0,Tracks=new List<HoloAnimationTrack>{new HoloAnimationTrack{Channel="rotation",From=0,To=Math.PI*2,Duration=1,Loop=true}}});
  Call(session,"ApplySnapshot",packet);Call(session,"ImportDrawAnimations",data.Animations,20L);Call(session,"SetAnimationClock",0L);var scene=((IDictionary)Field(session,"_scenes"))[20L];var item=((IDictionary)Field(scene,"Items"))["10:line"];Call(session,"ClientDisplayItem",scene,item,1);Call(session,"CompileQueuedDisplay");
  MatrixD Sample(int tick){Set(session,"_ticks",tick);var sample=Call(session,"SampleVectorLcd",scene,scene,1);return (MatrixD)Field(Field(((IList)Field(sample,"Entries"))[0],"Item"),"Transform");}
  var initial=Sample(0);Check(Sample(5).Equals(initial),"real replicated timeline stays frozen between six Hertz samples");
  var advanced=Sample(10);Check(!advanced.Equals(initial)&&Math.Abs(advanced.M11-.5)<1e-6,"real timeline advances by one sixth of a turn at the next sample");
 }
}
