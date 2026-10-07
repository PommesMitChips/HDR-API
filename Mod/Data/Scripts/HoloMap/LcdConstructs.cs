using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  sealed class LcdGrid {public VRage.Game.ModAPI.IMyCubeGrid Grid;public readonly List<Vector3D[]> Blocks=new List<Vector3D[]>();}
  sealed class LcdConstruct {public long Root;public int Tick;public Vector3D Center;public double Radius;public readonly List<LcdGrid> Grids=new List<LcdGrid>();}
  readonly Dictionary<long,LcdConstruct> _lcdConstructs=new Dictionary<long,LcdConstruct>();
  void PruneLcdConstructs(){var remove=new List<long>();foreach(var pair in _lcdConstructs){Scene scene;if(!_scenes.TryGetValue(pair.Key,out scene)||scene.TrackedRootId==0||scene.TrackedRootId!=pair.Value.Root)remove.Add(pair.Key);}foreach(var id in remove)_lcdConstructs.Remove(id);}
  void DrawLcdConstruct(Scene scene,Scene tile,MySpriteDrawFrame frame,Vector2 origin,Vector2 size,ref int sprites)
  {
   if(scene.TrackedRootId==0)return;float opacity=ClientLayerAlpha(scene,scene.TrackingCallerId,scene.ConstructLayer);if(opacity<=0)return;var root=MyAPIGateway.Entities.GetEntityById(scene.TrackedRootId) as VRage.Game.ModAPI.IMyCubeGrid;if(root==null||root.Closed)return;LcdConstruct cache;
   var inverse=MatrixD.Invert(root.WorldMatrix);cache=GetLcdConstructCache(scene,root,inverse);if(cache==null)return;
   double fit=scene.ShipMetresPerUnit>0?1/scene.ShipMetresPerUnit:scene.ShipRadius/cache.Radius;var center=scene.ShipMetresPerUnit>0?scene.ShipOrigin:cache.Center;var map=MatrixD.CreateTranslation(-center)*MatrixD.CreateScale(fit)*MatrixD.CreateTranslation(scene.ShipOffset)*LocalView(scene);var color=new Color(.1f,.8f,1f,opacity);
   foreach(var grid in cache.Grids){if(grid.Grid.Closed)continue;var matrix=grid.Grid.WorldMatrix*inverse*map;foreach(var block in grid.Blocks){if(sprites<=0)return;var p=new Vector2[8];for(int i=0;i<8;i++)p[i]=origin+LcdProjection.Point(Vector3D.Transform(block[i],matrix),tile.LcdWidth,tile.LcdHeight,size,tile.LcdColumns,tile.LcdRows,tile.LcdColumn,tile.LcdRow);for(int i=0;i<8&&sprites>0;i++)for(int j=i+1;j<8&&sprites>0;j++){var d=block[i]-block[j];int changed=(Math.Abs(d.X)>1e-8?1:0)+(Math.Abs(d.Y)>1e-8?1:0)+(Math.Abs(d.Z)>1e-8?1:0);if(changed==1)LcdLine(frame,p[i],p[j],1,color,origin,size,ref sprites);}}}
  }
  LcdConstruct GetLcdConstructCache(Scene scene,VRage.Game.ModAPI.IMyCubeGrid root,MatrixD inverse)
  {
   LcdConstruct cache;
   if(!_lcdConstructs.TryGetValue(scene.ConsoleId,out cache)||cache.Root!=root.EntityId||_ticks-cache.Tick>=60)
   {
    cache=new LcdConstruct{Root=root.EntityId,Tick=_ticks};var grids=new List<VRage.Game.ModAPI.IMyCubeGrid>();MyAPIGateway.GridGroups.GetGroup(root,GridLinkTypeEnum.Mechanical,grids);var bounds=BoundingBoxD.CreateInvalid();int remaining=256;
    foreach(var grid in grids){if(remaining<=0)break;var entry=new LcdGrid{Grid=grid};var blocks=new List<VRage.Game.ModAPI.IMySlimBlock>();int accepted=0;grid.GetBlocks(blocks,b=>accepted++<remaining);foreach(var block in blocks){if(remaining--<=0)break;var box=new BoundingBoxD(((Vector3D)block.Min-new Vector3D(.5))*grid.GridSize,((Vector3D)block.Max+new Vector3D(.5))*grid.GridSize);var corners=box.GetCorners();entry.Blocks.Add(corners);foreach(var p in corners)bounds.Include(Vector3D.Transform(p,grid.WorldMatrix*inverse));}cache.Grids.Add(entry);}
    if(bounds.Min.X>bounds.Max.X)return null;cache.Center=bounds.Center;cache.Radius=Math.Max(.001,bounds.HalfExtents.Length());_lcdConstructs[scene.ConsoleId]=cache;
   }
   return cache;
  }
  void DrawVectorLcdConstruct(Scene scene,MatrixD viewMapping,DisplayVolume volume,Vector3D normal,MatrixD world,MatrixD panelInverse,Vector3D camera,uint parent,ref int budget)
  {
   float opacity=ClientLayerAlpha(scene,scene.TrackingCallerId,scene.ConstructLayer);if(opacity<=0)return;
   var root=MyAPIGateway.Entities.GetEntityById(scene.TrackedRootId) as VRage.Game.ModAPI.IMyCubeGrid;if(root==null||root.Closed)return;
   var inverse=MatrixD.Invert(root.WorldMatrix);var cache=GetLcdConstructCache(scene,root,inverse);if(cache==null)return;
   double fit=scene.ShipMetresPerUnit>0?1/scene.ShipMetresPerUnit:scene.ShipRadius/cache.Radius;var center=scene.ShipMetresPerUnit>0?scene.ShipOrigin:cache.Center;
   var map=MatrixD.CreateTranslation(-center)*MatrixD.CreateScale(fit)*MatrixD.CreateTranslation(scene.ShipOffset)*viewMapping;
   var color=new Vector4(.1f,.8f,1f,opacity);
   foreach(var grid in cache.Grids)
   {
    if(grid.Grid.Closed)continue;var matrix=grid.Grid.WorldMatrix*inverse*map;
    foreach(var block in grid.Blocks)
    {
     if(budget<=0)return;var points=new Vector3D[8];for(int i=0;i<8;i++)points[i]=Vector3D.Transform(block[i],matrix);
     for(int i=0;i<8&&budget>0;i++)for(int j=i+1;j<8&&budget>0;j++)
     {var delta=block[i]-block[j];int changed=(Math.Abs(delta.X)>1e-8?1:0)+(Math.Abs(delta.Y)>1e-8?1:0)+(Math.Abs(delta.Z)>1e-8?1:0);if(changed==1)DrawPinnedLine(volume,points[i],points[j],normal,.0015,color,0,world,panelInverse,camera,parent,ref budget);}
    }
   }
  }
 }
}
