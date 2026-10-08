using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage;
namespace HoloMap
{
 [ProtoContract] public sealed class HoloRenderBudgetData
 {
  // Geometry zero means unlimited; absent budgets use the same scene defaults.
  // DrawWork is required for a present budget and keeps its separate finite bound.
  [ProtoMember(1)] public int Points;
  [ProtoMember(2)] public int Primitives;
  [ProtoMember(3)] public int DrawWork;
 }
 public sealed partial class HoloMapSession
 {
  const int MaxConfiguredDrawWork=200000;
  int ClientDrawWorkCap=20000;
  static int GeometryAllowance(int value){return value==0?int.MaxValue:value;}
  static int PublicGeometryAllowance(int value){return value==int.MaxValue?0:value;}
  static void ValidateRenderBudget(HoloRenderBudgetData d)
  {if(d==null||d.Points<0||d.Primitives<0||d.DrawWork<1000||d.DrawWork>MaxConfiguredDrawWork)throw new ArgumentException("Geometry allowances require 0 (unlimited) or a positive integer; draw work requires 1000–200000 units.");}
  void SetRenderBudget(IMyProgrammableBlock caller,IMyTerminalBlock target,int points,int primitives,int drawWork)
  {
   Authorize(caller,target);var proposed=new HoloRenderBudgetData{Points=points,Primitives=primitives,DrawWork=drawWork};ValidateRenderBudget(proposed);var scene=GetScene(target.EntityId);int usedPoints,usedPrimitives;ProjectedSourceCounts(scene,out usedPoints,out usedPrimitives);foreach(var i in scene.Items.Values){usedPoints+=i.Geometry.Points.Length;usedPrimitives+=i.Geometry.Triangles.Length/3+i.Geometry.Edges.Length/2;}
   points=GeometryAllowance(points);primitives=GeometryAllowance(primitives);
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
   {ReleaseSourceSlots(c);ClearExternalBudgetView(c);ReleaseUiRaster(c);c.Mapped.Clear();c.Sprites=null;c.SpriteVersion=null;c.Items.Clear();c.NextSample=-1;c.NextCapture=-1;c.SourceReduced=false;c.Error=null;}
   InvalidateLcdSamples();
  }
  static HoloRenderBudgetData ExportRenderBudget(Scene scene){return new HoloRenderBudgetData{Points=PublicGeometryAllowance(scene.PointBudget),Primitives=PublicGeometryAllowance(scene.PrimitiveBudget),DrawWork=scene.DrawWorkBudget};}
  static void ImportRenderBudget(Scene scene,HoloRenderBudgetData d){if(d==null)return;ValidateRenderBudget(d);scene.PointBudget=GeometryAllowance(d.Points);scene.PrimitiveBudget=GeometryAllowance(d.Primitives);scene.DrawWorkBudget=d.DrawWork;}
 }
}
