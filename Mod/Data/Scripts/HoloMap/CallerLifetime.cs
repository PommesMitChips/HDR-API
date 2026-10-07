using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  sealed class CallerProgram{public IMyProgrammableBlock Block;public string Source;public bool Active=true;}
  readonly Dictionary<long,CallerProgram> _callerPrograms=new Dictionary<long,CallerProgram>();
  void ObserveCaller(IMyProgrammableBlock caller)
  {
   string source=caller.ProgramData??"";CallerProgram previous;
   if(_callerPrograms.TryGetValue(caller.EntityId,out previous))
   {if(!ReferenceEquals(previous.Block,caller)||!string.Equals(previous.Source,source,StringComparison.Ordinal))RetireCaller(caller.EntityId);previous.Block=caller;previous.Source=source;previous.Active=true;}
   else{if(_callerPrograms.Count>=128)throw new ArgumentException("PB lifetime registry limit reached.");_callerPrograms.Add(caller.EntityId,new CallerProgram{Block=caller,Source=source});}
  }
  void RetireCaller(long caller)
  {
   foreach(var scene in _scenes.Values)
   {
    ClearUiDisplay(caller,scene.ConsoleId);ClearPacked(caller,scene.ConsoleId);ClearDrawAnimations(caller,scene.ConsoleId);
    var keys=new List<string>();foreach(var pair in scene.Screens)if(pair.Value.Data.CallerId==caller)keys.Add(pair.Key);
    foreach(string key in keys){ClearScreenContent(scene,scene.Screens[key]);scene.Screens.Remove(key);}
    keys.Clear();foreach(var pair in scene.Items)if(pair.Value.CallerId==caller)keys.Add(pair.Key);foreach(string key in keys)scene.Items.Remove(key);
    keys.Clear();foreach(var pair in scene.Labels)if(pair.Value.CallerId==caller)keys.Add(pair.Key);foreach(string key in keys)scene.Labels.Remove(key);
    keys.Clear();foreach(var pair in scene.Layers)if(pair.Value.CallerId==caller)keys.Add(pair.Key);foreach(string key in keys)scene.Layers.Remove(key);
    if(scene.TrackingCallerId==caller){scene.TrackingCallerId=scene.TrackedRootId=0;}
    if(scene.LcdCallerId==caller){scene.LcdCallerId=0;scene.LcdSourceId=0;}
   }
   DrawContext context;if(_drawContexts.TryGetValue(caller,out context)){context.ScreenId=null;context.Paused=false;}
   // Canonical removals replicate normally; cached textures are released locally now.
   var remove=new List<string>();foreach(var pair in _projectedCaches)if(pair.Value.Caller==caller)remove.Add(pair.Key);
   foreach(string key in remove){var cache=_projectedCaches[key];_projectedCaches.Remove(key);ReleaseProjectedCache(cache);}
   _dirty=true;
  }
  void TickCallerLifetimes()
  {
   if(!MyAPIGateway.Multiplayer.IsServer||_ticks%30!=0)return;
   var remove=new List<long>();
   foreach(var pair in _callerPrograms)
   {
    var state=pair.Value;var caller=MyAPIGateway.Entities.GetEntityById(pair.Key) as IMyProgrammableBlock;
    if(caller==null||caller.Closed||!ReferenceEquals(caller,state.Block)){RetireCaller(pair.Key);remove.Add(pair.Key);continue;}
    string source=caller.ProgramData??"";bool active=caller.Enabled&&caller.IsWorking;
    if(!string.Equals(source,state.Source,StringComparison.Ordinal)||state.Active&&!active)RetireCaller(pair.Key);
    state.Source=source;state.Active=active;
   }
   foreach(long id in remove){_callerPrograms.Remove(id);_drawContexts.Remove(id);}
  }
 }
}
