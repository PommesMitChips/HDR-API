using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

static class LcdFaultIsolationTests
{
 static int _checks;
 const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Field(object target,string name)=>target.GetType().GetField(name,Private|BindingFlags.Public).GetValue(target);
 static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private|BindingFlags.Public).SetValue(target,value);
 static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
 static void Check(bool condition,string text){_checks++;if(!condition)throw new Exception("LCD fault isolation: "+text);}
 public static int Run()
 {
  NativeFault();VectorFallback();return _checks;
 }
 static object Scene(HoloMapSession session,int renderer)
 {
  var packet=ClientReplicationTests.Snapshot();packet.Scenes[0].Items.Clear();packet.Scenes[0].Labels.Clear();packet.Scenes[0].Lcd=new HoloLcdData{Renderer=renderer};
  Call(session,"ApplySnapshot",packet);return ((IDictionary)Field(session,"_scenes"))[20L];
 }
 static object Panel(Func<MySpriteDrawFrame> draw,bool known=true)
 {
  var model=DrawTestProxy.Make(typeof(VRage.ModAPI.IMyEntity).GetProperty("Model").PropertyType,(m,a)=>m.Name=="get_AssetName"?(known?"Models/Cubes/Large/LCDPanel.mwm":"Mods/Unknown.mwm"):throw new Exception(m.Name));
  var rotation=DrawTestProxy.Make(typeof(IMyLcdSurfaceComponent),(m,a)=>m.Name=="get_SelectedRotationIndex"?0:throw new Exception(m.Name));
  var components=DrawTestProxy.Make(typeof(VRage.Game.Components.Interfaces.IMyEntityComponentContainer),(m,a)=>{if(m.Name=="TryGet"){a[0]=rotation;return true;}throw new Exception(m.Name);});
  return DrawTestProxy.Make(typeof(IMyTextPanel),(m,a)=>{
   if(m.Name=="get_Closed")return false;if(m.Name=="get_Model")return model;if(m.Name=="get_Components")return components;if(m.Name=="get_Name")return "ScreenArea";
   if(m.Name=="get_ContentType")return ContentType.SCRIPT;if(m.Name=="get_Script")return "HDRAPI";
   if(m.Name=="get_SurfaceSize"||m.Name=="get_TextureSize")return new Vector2(512);
   if(m.Name=="DrawFrame")return draw();throw new Exception("Unexpected isolated panel call "+m.Name);
  });
 }
 static void NativeFault()
 {
  using var gateway=new ClientReplicationTests.GatewayScope();var session=new HoloMapSession();var scene=Scene(session,0);int attempts=0,notices=0;
  var panel=Panel(()=>{attempts++;throw new IndexOutOfRangeException("deliberate native failure");});
  gateway.Install("Utilities",(m,a)=>{if(m.Name=="ShowMessage"){notices++;throw new InvalidOperationException("notification also failed");}throw new Exception(m.Name);});
  Call(session,"DrawLcdDisplay",scene,panel,20,1);
  var faults=(IDictionary)Field(session,"_lcdBackendFaults");Check(faults.Count==1&&(bool)Field(faults[20L],"Disabled"),"native failure disables only this display");
  Set(session,"_ticks",60);Call(session,"DrawLcdDisplay",scene,panel,20,1);Check(attempts==1&&notices==1,"disabled display and failing diagnostics are not retried each frame");
  int healthyFrames=0;var healthy=Panel(()=>new MySpriteDrawFrame(f=>healthyFrames++));Set(scene,"ConsoleId",21L);Call(session,"DrawLcdDisplay",scene,healthy,20,1);Check(healthyFrames==1,"a different LCD continues rendering");Set(scene,"ConsoleId",20L);
  Call(session,"ClearLcdBackends");Check(faults.Count==0,"explicit backend reset clears contained faults");Call(session,"DrawLcdDisplay",scene,panel,20,1);Check(attempts==2&&notices==2,"reset permits a fresh attempt and one fresh diagnostic");
 }
 static void VectorFallback()
 {
  using var gateway=new ClientReplicationTests.GatewayScope();var session=new HoloMapSession();var scene=Scene(session,1);int attempts=0,notices=0,nativeFrames=0;bool nativeFails=false;
  var panel=Panel(()=>{attempts++;if(attempts==1||nativeFails)throw new IndexOutOfRangeException("deliberate backend failure");return new MySpriteDrawFrame(f=>{var sprites=new List<MySprite>();f.AddToList(sprites);if(sprites.Count>0)nativeFrames++;});});
  gateway.Install("Utilities",(m,a)=>{if(m.Name=="ShowMessage"){notices++;return null;}throw new Exception(m.Name);});
  Call(session,"DrawLcdDisplay",scene,panel,20,1);
  var fault=((IDictionary)Field(session,"_lcdBackendFaults"))[20L];
  Check((bool)Field(fault,"NativeOnly")&&!(bool)Field(fault,"Disabled")&&nativeFrames==1,"failed vector frame falls back to native for the same display");
  Check(((IDictionary)Field(session,"_lcdVectorBlanked")).Count==0&&notices==1,"fallback clears vector binding and reports once");
  Set(session,"_ticks",10);Call(session,"InvalidateLcdSamples");Call(session,"DrawLcdDisplay",scene,panel,20,1);Check(nativeFrames==2&&notices==1,"settings invalidation cannot retry the faulted vector backend");
  nativeFails=true;Set(session,"_ticks",20);Call(session,"DrawLcdDisplay",scene,panel,20,1);Check((bool)Field(fault,"Disabled")&&notices==2,"later native failure disables LCD and reports the new transition once");
  Set(session,"_ticks",60);Call(session,"DrawLcdDisplay",scene,panel,20,1);Check(attempts==4&&notices==2,"both failures remain isolated on subsequent frames");
 }
}
