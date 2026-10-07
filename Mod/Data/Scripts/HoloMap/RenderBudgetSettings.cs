using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage;
namespace HoloMap
{
 [ProtoContract] public sealed class HoloRenderBudgetData
 {
  // An absent budget means scene defaults. A present incomplete budget is invalid.
  [ProtoMember(1)] public int Points;
  [ProtoMember(2)] public int Primitives;
  [ProtoMember(3)] public int DrawWork;
 }
 public sealed partial class HoloMapSession
 {
  const int MaxConfiguredPoints=65536,MaxConfiguredPrimitives=131072,MaxConfiguredDrawWork=200000;
  int ClientDrawWorkCap=20000;
  static void ValidateRenderBudget(HoloRenderBudgetData d)
  {if(d==null||d.Points<256||d.Points>MaxConfiguredPoints||d.Primitives<256||d.Primitives>MaxConfiguredPrimitives||d.DrawWork<1000||d.DrawWork>MaxConfiguredDrawWork)throw new ArgumentException("Budget requires 256–65536 points, 256–131072 primitives and 1000–200000 draw work units.");}
  void SetRenderBudget(IMyProgrammableBlock caller,IMyTerminalBlock target,int points,int primitives,int drawWork)
  {
   Authorize(caller,target);var proposed=new HoloRenderBudgetData{Points=points,Primitives=primitives,DrawWork=drawWork};ValidateRenderBudget(proposed);var scene=GetScene(target.EntityId);int usedPoints,usedPrimitives;ProjectedSourceCounts(scene,out usedPoints,out usedPrimitives);foreach(var i in scene.Items.Values){usedPoints+=i.Geometry.Points.Length;usedPrimitives+=i.Geometry.Triangles.Length/3+i.Geometry.Edges.Length/2;}
   if(usedPoints>points||usedPrimitives>primitives)throw new ArgumentException("New budget is smaller than the anchor's declared geometry.");
   if(scene.PointBudget==points&&scene.PrimitiveBudget==primitives&&scene.DrawWorkBudget==drawWork)return;scene.PointBudget=points;scene.PrimitiveBudget=primitives;scene.DrawWorkBudget=drawWork;InvalidateSceneRenderBudget(scene.ConsoleId);_dirty=true;
  }
  void InvalidateSceneRenderBudget(long id)
  {
   var remove=new List<string>();foreach(var pair in _clientGeometry)if(pair.Value.ConsoleId==id)remove.Add(pair.Key);
   foreach(string key in remove){_clientGeometry.Remove(key);_queuedCompile.Remove(key);}
   // Do not leave invalidated entries queued when a script repeatedly changes the allowance.
   var pending=new List<string>();foreach(string key in _compileQueue)if(_clientGeometry.ContainsKey(key))pending.Add(key);
   _compileQueue.Clear();_queuedCompile.Clear();foreach(string key in pending)if(_queuedCompile.Add(key))_compileQueue.Enqueue(key);
   foreach(var c in _projectedCaches.Values)if(c.Anchor==id)
   {ClearExternalBudgetView(c);ReleaseUiRaster(c);c.Mapped.Clear();c.Sprites=null;c.SpriteVersion=null;c.Items.Clear();c.NextSample=-1;c.NextCapture=-1;c.SourceReduced=false;c.Error=null;}
   InvalidateLcdSamples();
  }
  static HoloRenderBudgetData ExportRenderBudget(Scene scene){return new HoloRenderBudgetData{Points=scene.PointBudget,Primitives=scene.PrimitiveBudget,DrawWork=scene.DrawWorkBudget};}
  static void ImportRenderBudget(Scene scene,HoloRenderBudgetData d){if(d==null)return;ValidateRenderBudget(d);scene.PointBudget=d.Points;scene.PrimitiveBudget=d.Primitives;scene.DrawWorkBudget=d.DrawWork;}
 }
}
