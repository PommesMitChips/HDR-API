using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
namespace HoloMap
{
 public static class LcdRefreshSchedule
 {
  public static void Validate(double hz){if(!Geometry.Finite(hz)||hz<1||hz>60)throw new ArgumentException("LCD refresh must be 1–60 Hz.");}
  public static bool Due(double now,double last,double hz){Validate(hz);return last<0||now<last||now-last+1e-8>=1/hz;}
 }
 public static class LcdPinnedProjection
 {
  public static bool FrontFacing(LcdScreenBasis basis,MatrixD world,Vector3D viewer)
  {return Vector3D.Dot(viewer-Vector3D.Transform(basis.Center,world),Vector3D.TransformNormal(basis.Normal,world))>0;}
  public static MatrixD CanvasToPanel(LcdScreenBasis basis,double width,double height,int columns,int rows,int column,int row,double depth)
  {
   LcdProjection.Validate(width,height,columns,rows,column,row);
   if(!Geometry.Finite(depth)||depth<0||depth>.003)throw new ArgumentException("Invalid screen depth offset.");
   var result=MatrixD.Identity;result.Right=basis.HalfRight*(2*columns/width);result.Up=basis.HalfUp*(2*rows/height);result.Backward=Vector3D.Zero;
   result.Translation=basis.Center+basis.HalfRight*(columns-2*column-1)+basis.HalfUp*(1-rows+2*row)+basis.Normal*depth;
   return result;
  }
  public static MatrixD ScreenFrame(LcdScreenBasis basis,MatrixD world,double depth)
  {var frame=MatrixD.Identity;frame.Right=basis.HalfRight;frame.Up=basis.HalfUp;frame.Backward=basis.Normal;frame.Translation=basis.Center+basis.Normal*depth;return frame*world;}
 }
 public sealed partial class HoloMapSession
 {
  double ClientLcdRefreshCap=60;
  sealed class NativeLcdSample{public double Time=-1;public double Rate;public int Mode;}
  readonly Dictionary<long,NativeLcdSample> _lcdNativeSamples=new Dictionary<long,NativeLcdSample>();
  sealed class LcdSampleClock{public double Last=-1,Next,Rate;}
  readonly Dictionary<long,LcdSampleClock> _lcdSampleClock=new Dictionary<long,LcdSampleClock>();
  readonly Dictionary<long,string> _lcdVectorBlanked=new Dictionary<long,string>();
  sealed class LcdBackendFault{public bool NativeOnly,Disabled;public int ReportedStage;}
  readonly Dictionary<long,LcdBackendFault> _lcdBackendFaults=new Dictionary<long,LcdBackendFault>();
  List<Scene> LcdSettingTargets(IMyProgrammableBlock caller,IMyTerminalBlock target)
  {
   Authorize(caller,target);if(!(target is IMyTextPanel))throw new ArgumentException("LCD renderer settings require an LCD panel.");
   var selected=GetScene(target.EntityId);long master=selected.LcdSourceId==0?selected.ConsoleId:selected.LcdSourceId;var result=new List<Scene>();
   foreach(var scene in _scenes.Values)if(scene.ConsoleId==master||scene.LcdSourceId==master)
   {var block=MyAPIGateway.Entities.GetEntityById(scene.ConsoleId) as IMyTerminalBlock;if(block==null||block.Closed)throw new ArgumentException("LCD group contains an unavailable panel.");Authorize(caller,block);result.Add(scene);}
   return result;
  }
  void SetLcdRenderer(IMyProgrammableBlock caller,IMyTerminalBlock target,string renderer)
  {
   int mode;if(renderer=="native")mode=0;else if(renderer=="vector")mode=1;else throw new ArgumentException("LCD renderer must be native or vector.");
   var scenes=LcdSettingTargets(caller,target);bool changed=false;foreach(var scene in scenes){changed|=scene.LcdRenderer!=mode||scene.LcdCallerId!=caller.EntityId;scene.LcdRenderer=mode;scene.LcdCallerId=caller.EntityId;}if(changed){InvalidateLcdSamples();_dirty=true;}
  }
  void SetLcdRefresh(IMyProgrammableBlock caller,IMyTerminalBlock target,double hz)
  {LcdRefreshSchedule.Validate(hz);var scenes=LcdSettingTargets(caller,target);bool changed=false;foreach(var scene in scenes){changed|=scene.LcdRefreshHz!=hz||scene.LcdCallerId!=caller.EntityId;scene.LcdRefreshHz=hz;scene.LcdCallerId=caller.EntityId;}if(changed){InvalidateLcdSamples();_dirty=true;}}
  // Settings changes keep the last sampled picture until the new interval is due.
  // This also prevents PB settings churn from bypassing a viewer's sampling cap.
  void InvalidateLcdSamples(){_lcdNativeSamples.Clear();}
  bool LcdSampleDue(long id,double hz)
  {
   LcdSampleClock clock;if(!_lcdSampleClock.TryGetValue(id,out clock)){clock=new LcdSampleClock();_lcdSampleClock[id]=clock;}
   double now=_ticks/60.0,period=1/hz;
   if(clock.Rate!=hz){clock.Rate=hz;clock.Next=clock.Last+period;}
   if(clock.Last<0||now<clock.Last){clock.Last=now;clock.Next=now+period;return true;}
   if(now<=clock.Last||now+1e-8<clock.Next)return false;
   clock.Last=now;clock.Next+=period;if(clock.Next<=now+1e-8)clock.Next=now+period;return true;
  }
  bool NativeLcdDue(Scene tile)
  {
   NativeLcdSample state;if(!_lcdNativeSamples.TryGetValue(tile.ConsoleId,out state)){state=new NativeLcdSample();_lcdNativeSamples[tile.ConsoleId]=state;}
   double rate=Math.Min(6,Math.Min(tile.LcdRefreshHz,ClientLcdRefreshCap)),now=_ticks/60.0;
   state.Rate=rate;state.Mode=tile.LcdRenderer;
   if(!LcdSampleDue(tile.ConsoleId,rate))return false;state.Time=now;return true;
  }
  void DrawLcdDisplay(Scene tile,IMyTextPanel panel,ref int budget,ref int compileBudget)
  {
   if(tile==null||panel==null)return;
   LcdBackendFault fault;
   if(_lcdBackendFaults.TryGetValue(tile.ConsoleId,out fault)&&fault.Disabled)return;
   bool vectorAttempt=tile.LcdRenderer==1&&(fault==null||!fault.NativeOnly);
   try{DrawLcdDisplayCore(tile,panel,ref vectorAttempt,ref budget,ref compileBudget);}
   catch(Exception error)
   {
    if(fault==null){fault=new LcdBackendFault();_lcdBackendFaults[tile.ConsoleId]=fault;}
    if(vectorAttempt)
    {
     fault.NativeOnly=true;_lcdVectorBlanked.Remove(tile.ConsoleId);_lcdVectorSamples.Remove(tile.ConsoleId);
     string detail=error.ToString();bool nativeAttempt=false;
     try{DrawLcdDisplayCore(tile,panel,ref nativeAttempt,ref budget,ref compileBudget);}
     catch(Exception nativeError){fault.Disabled=true;detail+="\nNative fallback failed: "+nativeError;}
     ReportLcdBackendFault(tile.ConsoleId,fault,detail);
    }
    else{fault.Disabled=true;ReportLcdBackendFault(tile.ConsoleId,fault,error.ToString());}
   }
  }
  void ReportLcdBackendFault(long id,LcdBackendFault fault,string detail)
  {
   int stage=fault.Disabled?2:1;if(fault.ReportedStage>=stage)return;fault.ReportedStage=stage;
   string action=fault.Disabled?"rendering disabled for this LCD":"vector rendering failed; this LCD will use native rendering";
   // Diagnostics must never turn a contained display failure into a game crash.
   try{VRage.Utils.MyLog.Default.WriteLineAndConsole("HDR API LCD "+id+": "+action+". "+detail);}catch(Exception){}
   try{if(MyAPIGateway.Utilities!=null)MyAPIGateway.Utilities.ShowMessage("HDR API","LCD "+id+": "+action+". See the game log.");}catch(Exception){}
  }
  void DrawLcdDisplayCore(Scene tile,IMyTextPanel panel,ref bool vectorAttempt,ref int budget,ref int compileBudget)
  {
   var surface=(Sandbox.ModAPI.Ingame.IMyTextSurface)panel;
   if(surface.ContentType!=VRage.Game.GUI.TextPanel.ContentType.SCRIPT||surface.Script!="HDRAPI")
   {_lcdVectorBlanked.Remove(tile.ConsoleId);_lcdNativeSamples.Remove(tile.ConsoleId);_lcdVectorSamples.Remove(tile.ConsoleId);return;}
   LcdScreenBasis basis;
   if(vectorAttempt&&LcdScreenCalibration.TryGet(panel,out basis))
   {
    string blanked;
    if(!_lcdVectorBlanked.TryGetValue(tile.ConsoleId,out blanked)||blanked!=surface.Name)
    {using(var frame=surface.DrawFrame()){} _lcdVectorBlanked[tile.ConsoleId]=surface.Name;_lcdDrawn.Add(tile.ConsoleId);}
    DrawVectorLcd(tile,panel,basis,ref budget,ref compileBudget);return;
   }
   vectorAttempt=false;
   if(_lcdVectorBlanked.Remove(tile.ConsoleId))_lcdNativeSamples.Remove(tile.ConsoleId);
   _lcdVectorSamples.Remove(tile.ConsoleId);
   if(NativeLcdDue(tile))DrawLcd(tile,panel,ref budget,ref compileBudget);
  }
  void ClearLcdBackends(){InvalidateLcdSamples();_lcdVectorSamples.Clear();_lcdSampleClock.Clear();_lcdVectorBlanked.Clear();_lcdBackendFaults.Clear();}
  void PruneLcdBackends()
  {
   var remove=new List<long>();foreach(var id in _lcdNativeSamples.Keys)if(!_scenes.ContainsKey(id))remove.Add(id);foreach(var id in remove)_lcdNativeSamples.Remove(id);
   remove.Clear();foreach(var id in _lcdVectorSamples.Keys)if(!_scenes.ContainsKey(id))remove.Add(id);foreach(var id in remove)_lcdVectorSamples.Remove(id);
   remove.Clear();foreach(var id in _lcdVectorBlanked.Keys)if(!_scenes.ContainsKey(id))remove.Add(id);foreach(var id in remove)_lcdVectorBlanked.Remove(id);
   remove.Clear();foreach(var id in _lcdSampleClock.Keys)if(!_scenes.ContainsKey(id))remove.Add(id);foreach(var id in remove)_lcdSampleClock.Remove(id);
   remove.Clear();foreach(var id in _lcdBackendFaults.Keys)if(!_scenes.ContainsKey(id))remove.Add(id);foreach(var id in remove)_lcdBackendFaults.Remove(id);
  }
 }
}
