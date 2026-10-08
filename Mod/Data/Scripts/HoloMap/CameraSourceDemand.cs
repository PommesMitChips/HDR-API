using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  // Visibility follows the current view. The more expensive density field shares
  // a global two-per-second allowance, including unsuccessful evaluations.
  const int CameraDensityPeriod=30,CameraDemandCacheLimit=16;
  sealed class CameraDemandSurface
  {public HoloProjectedScreenData Settings;public SurfaceMesh Mesh;public MatrixD Clip;public Vector4D[] Crop;}
  sealed class CameraDemandInput
  {public double[] Bases;public CameraDensityLens[] Lenses;public CameraDemandSurface[] Surfaces;public Vector2I Viewport;public bool Known;}
  sealed class CameraDemandCache
  {
   public CameraDemandInput Input,DensityInput;public int Seen,Checked=-1,DensityTick=int.MinValue;
   public MyTuple<int,double[],bool> Packet,Density;public bool HasPacket,HasDensity;
  }
  readonly Dictionary<string,CameraDemandCache> _cameraDemandCaches=new Dictionary<string,CameraDemandCache>();
  int _cameraDensityNextTick;

  object CameraSourceDensity(object[] args)
  {
   if(args==null||args.Length!=5||!(args[0] is string)||(string)args[0]!="camera-panorama"||!(args[1] is long)||!(args[2] is long)||!(args[3] is string)||!(args[4] is double[]))return false;
   string source=(string)args[3];long anchorId=(long)args[1],caller=(long)args[2];
   try{ValidateDisplaySource("camera-panorama",source);}catch(ArgumentException){return false;}
   int count=source.Split(',').Length;var bases=(double[])args[4];if(bases.Length!=count*11)return false;
   foreach(double n in bases)if(!Geometry.Finite(n))return false;
   Scene scene;if(!_scenes.TryGetValue(anchorId,out scene))return false;
   HoloProjectedScreenData settings=null;
   foreach(var screen in scene.Screens.Values)
   {
    foreach(var d in ScreenSourceSettings(screen.Data)){if(d.CallerId!=caller||d.SourceProvider!="camera-panorama"||d.SourceId!=source)continue;
    if(settings!=null&&!SameCameraSourceSettings(settings,d))return false;settings=d;}
   }
   if(settings==null)return false;
   var lenses=new CameraDensityLens[count];
   for(int i=0;i<count;i++)
   {
    int p=i*11;lenses[i]=new CameraDensityLens{Right=new Vector3D(bases[p],bases[p+1],bases[p+2]),Up=new Vector3D(bases[p+3],bases[p+4],bases[p+5]),Forward=new Vector3D(bases[p+6],bases[p+7],bases[p+8]),TanHorizontal=bases[p+9],TanVertical=bases[p+10],FeatherRadians=settings.SourceFeather*Math.PI/180,CaptureMax=settings.SourceCaptureResolution};
   }
   // The cheap helper validates lens axes and limits even when geometry is absent.
   try{CameraDensityMap.EvaluateVisibility(null,null,null,MatrixD.Identity,new Vector2I(1,1),null,lenses);}
   catch(ArgumentException){return false;}
   var input=CameraDemandGeometry(scene,anchorId,caller,source,bases,lenses);
   string key=PackedKey(caller,anchorId,source);CameraDemandCache saved;
   if(!_cameraDemandCaches.TryGetValue(key,out saved))
   {
    if(_cameraDemandCaches.Count>=CameraDemandCacheLimit)
    {
     string oldest=null;int seen=int.MaxValue;
     foreach(var pair in _cameraDemandCaches)if(oldest==null||pair.Value.Seen<seen){oldest=pair.Key;seen=pair.Value.Seen;}
     _cameraDemandCaches.Remove(oldest);
    }
    saved=new CameraDemandCache();_cameraDemandCaches.Add(key,saved);
   }
   saved.Seen=_ticks;
   if(saved.HasPacket&&saved.Checked==_ticks&&SameCameraDemand(saved.Input,input))return CopyCameraDensity(saved.Packet);
   int mask=0;bool known=input.Known;int trianglesLeft=8192;
   if(known)
   {
    try
    {
     foreach(var surface in input.Surfaces)
     {
      if(trianglesLeft<=0){known=false;break;}
      var g=surface.Mesh.Geometry;var visible=CameraDensityMap.EvaluateVisibility(g.Points,g.Triangles,surface.Mesh.UV,surface.Clip,input.Viewport,surface.Crop,lenses,trianglesLeft,2,1d/512);
      if(visible.ConservativeFallback){known=false;break;}
      trianglesLeft-=visible.TriangleWork;mask|=visible.VisibleMask;
     }
    }
    catch{known=false;}
   }
   if(!known)mask=(1<<count)-1;
   var packet=ConservativeCameraDensity(lenses,mask,known);
   if(known&&mask!=0)
   {
    if(saved.HasDensity&&SameCameraDemand(saved.DensityInput,input))packet=saved.Density;
    else if(CameraDensityTurn(key,saved))
    {
     _cameraDensityNextTick=_ticks+CameraDensityPeriod;saved.DensityTick=_ticks;
     packet=EvaluateCameraSourceDensity(input,mask);
     saved.DensityInput=input;saved.Density=packet;saved.HasDensity=packet.Item3;
    }
   }
   saved.Input=input;saved.Checked=_ticks;saved.Packet=packet;saved.HasPacket=true;
   return CopyCameraDensity(packet);
  }
  bool CameraDensityTurn(string key,CameraDemandCache current)
  {
   if(_ticks<_cameraDensityNextTick)return false;
   // A continuously moving first caller must not monopolize the global allowance.
   foreach(var pair in _cameraDemandCaches)
   {
    var other=pair.Value;if(ReferenceEquals(other,current)||_ticks-other.Seen>60||!other.HasPacket||!other.Packet.Item3||other.Packet.Item1==0||other.HasDensity&&SameCameraDemand(other.DensityInput,other.Input))continue;
    if(other.DensityTick<current.DensityTick||other.DensityTick==current.DensityTick&&string.CompareOrdinal(pair.Key,key)<0)return false;
   }
   return true;
  }
  CameraDemandInput CameraDemandGeometry(Scene scene,long anchorId,long caller,string source,double[] bases,CameraDensityLens[] lenses)
  {
   var result=new CameraDemandInput{Bases=(double[])bases.Clone(),Lenses=lenses,Surfaces=new CameraDemandSurface[0]};
   try
   {
    var camera=MyAPIGateway.Session==null?null:MyAPIGateway.Session.Camera;var anchor=MyAPIGateway.Entities.GetEntityById(anchorId) as IMyTerminalBlock;
    if(camera==null||anchor==null||anchor.Closed)return result;
    result.Viewport=new Vector2I((int)Math.Round(camera.ViewportSize.X),(int)Math.Round(camera.ViewportSize.Y));
    if(result.Viewport.X<1||result.Viewport.Y<1)return result;
    var view=camera.ViewMatrix*camera.ProjectionMatrix;var surfaces=new List<CameraDemandSurface>();
    foreach(var screen in scene.Screens.Values)
    {
     if(screen.Data.CallerId==caller&&screen.Data.SourceSlots!=null)foreach(var slot in screen.Data.SourceSlots)
     {
      var parent=screen.Data;if(slot.Provider!="camera-panorama"||slot.SourceId!=source||!SourceSlotVisible(scene,parent,slot)||!ScreenSurfaceVisible(parent,anchor.WorldMatrix,camera.Position))continue;
      ProjectedCache slotParent;CompositionSlotCache slotCache;MappedScreenMesh slotMapped;
      if(!_projectedCaches.TryGetValue(PackedKey(caller,anchorId,parent.Id),out slotParent)||!slotParent.Slots.TryGetValue(slot.Id,out slotCache)||slotCache.Canvas==null)return result;
      SurfaceMesh mesh=slotCache.Canvas;MatrixD clip;var cropPose=ReadMatrix(parent.Pose);
      if(parent.SurfaceKind!=0){if(!slotParent.Mapped.TryGetValue("slot:"+slot.Id,out slotMapped)||slotMapped.Mesh==null||!SameSurface(slotMapped.Settings,parent)||_ticks-slotMapped.Seen>120)return result;mesh=slotMapped.Mesh;clip=ReadMatrix(parent.Pose)*anchor.WorldMatrix*view;}
      else{var content=ProjectedContentView(parent);if(slotCache.DemandCanvas==null||!ReferenceEquals(slotCache.DemandCanvasSource,mesh)||slotCache.DemandContent!=content){slotCache.DemandCanvas=TransformCanvasMesh(mesh,content);slotCache.DemandCanvasSource=mesh;slotCache.DemandContent=content;}mesh=slotCache.DemandCanvas;cropPose=ProjectedCanvas(parent,slotCache.Depth,parent.CanvasWidth,parent.CanvasHeight);clip=cropPose*anchor.WorldMatrix*view;}
      if(mesh.UV==null)return result;surfaces.Add(new CameraDemandSurface{Settings=parent,Mesh=mesh,Clip=clip,Crop=CameraDensityCrop(parent,cropPose)});
     }
     var d=screen.Data;if(d.CallerId!=caller||d.SourceProvider!="camera-panorama"||d.SourceId!=source||!ExternalBudgetScreenVisible(d)||!ScreenSurfaceVisible(d,anchor.WorldMatrix,camera.Position))continue;
     ProjectedCache cache;MappedScreenMesh mapped;
     if(!_projectedCaches.TryGetValue(PackedKey(caller,anchorId,d.Id),out cache)||!cache.Mapped.TryGetValue("source",out mapped)||mapped.Mesh==null||mapped.Mesh.UV==null||!SameSurface(mapped.Settings,d)||_ticks-mapped.Seen>120||mapped.Depth!=ScreenLayerDepth(d,anchor.WorldMatrix,camera.Position,.0005)||!SamePanorama(mapped.Panorama,PanoramaCameras(scene,d)))return result;
     var pose=ReadMatrix(d.Pose);var planes=CameraDensityCrop(d,pose);
     surfaces.Add(new CameraDemandSurface{Settings=d,Mesh=mapped.Mesh,Clip=pose*anchor.WorldMatrix*view,Crop=planes});
    }
    result.Surfaces=surfaces.ToArray();result.Known=true;
   }
   catch{result.Known=false;}
   return result;
  }
  static Vector4D[] CameraDensityCrop(HoloProjectedScreenData data,MatrixD pose)
  {
   if(data.SurfaceClip==null)return new Vector4D[0];var planes=new Vector4D[data.SurfaceClip.Length/4];
   // Preserve the rendered crop's double precision. A float-rounded tangent
   // plane can erase a thin visible patch and incorrectly stop its camera.
   for(int i=0;i<planes.Length;i++)
   {
    int p=i*4;var normal=new Vector3D(data.SurfaceClip[p],data.SurfaceClip[p+1],data.SurfaceClip[p+2]);
    planes[i]=new Vector4D(Vector3D.Dot(pose.Right,normal),Vector3D.Dot(pose.Up,normal),Vector3D.Dot(pose.Backward,normal),data.SurfaceClip[p+3]-Vector3D.Dot(pose.Translation,normal));
   }
   return planes;
  }
  static bool SameCameraDemand(CameraDemandInput a,CameraDemandInput b)
  {
   if(a==null||b==null||a.Known!=b.Known||a.Viewport!=b.Viewport||!SameArray(a.Bases,b.Bases)||a.Lenses.Length!=b.Lenses.Length||a.Surfaces.Length!=b.Surfaces.Length)return false;
   for(int i=0;i<a.Lenses.Length;i++)if(a.Lenses[i].CaptureMax!=b.Lenses[i].CaptureMax||a.Lenses[i].FeatherRadians!=b.Lenses[i].FeatherRadians)return false;
   for(int i=0;i<a.Surfaces.Length;i++)
   {
    var x=a.Surfaces[i];var y=b.Surfaces[i];if(!ReferenceEquals(x.Settings,y.Settings)||!ReferenceEquals(x.Mesh,y.Mesh)||x.Clip!=y.Clip||x.Crop.Length!=y.Crop.Length)return false;
    for(int j=0;j<x.Crop.Length;j++)if(x.Crop[j]!=y.Crop[j])return false;
   }
   return true;
  }
  static MyTuple<int,double[],bool> EvaluateCameraSourceDensity(CameraDemandInput input,int mask)
  {
   CameraDensityResult combined=null;int trianglesLeft=8192,cellsLeft=65536;
   try
   {
    foreach(var surface in input.Surfaces)
    {
     if(trianglesLeft<=0||cellsLeft<=0)return ConservativeCameraDensity(input.Lenses,mask,true);
     var g=surface.Mesh.Geometry;var result=CameraDensityMap.Evaluate(g.Points,g.Triangles,surface.Mesh.UV,surface.Clip,input.Viewport,surface.Crop,input.Lenses,16,trianglesLeft,cellsLeft,2,1d/512);
     if(result.ConservativeFallback)return ConservativeCameraDensity(input.Lenses,mask,true);
     trianglesLeft-=result.TriangleWork;cellsLeft-=result.CellWork;
     if(combined==null){combined=result;continue;}
     for(int i=0;i<input.Lenses.Length;i++)
     {
      var target=combined.Cameras[i];var incoming=result.Cameras[i];target.SuggestedResolution=Math.Max(target.SuggestedResolution,incoming.SuggestedResolution);
      for(int j=0;j<target.Cells.Length;j++)
      {
       var a=target.Cells[j];var b=incoming.Cells[j];a.Covered|=b.Covered;a.HasSample|=b.HasSample;
       a.UpperMagnification=Math.Max(a.UpperMagnification,b.UpperMagnification);a.UpperArea=Math.Max(a.UpperArea,b.UpperArea);
       a.Magnification=Math.Max(a.Magnification,b.Magnification);a.Area=Math.Max(a.Area,b.Area);a.BlendWeight=Math.Max(a.BlendWeight,b.BlendWeight);target.Cells[j]=a;
      }
     }
    }
   }
   catch{return ConservativeCameraDensity(input.Lenses,mask,true);}
   if(combined==null)return ConservativeCameraDensity(input.Lenses,mask,true);
   combined.VisibleMask=mask;return PackCameraDensity(combined,true);
  }
  static MyTuple<int,double[],bool> ConservativeCameraDensity(CameraDensityLens[] lenses,int mask,bool known)
  {
   int count=lenses.Length;var packed=new double[2+count+count*256*7];packed[0]=16;packed[1]=count;int offset=2+count;
   for(int i=0;i<count;i++)
   {
    bool visible=(mask&(1<<i))!=0;int cap=lenses[i].CaptureMax;packed[2+i]=visible?cap:0;
    for(int j=0;j<256;j++){packed[offset++]=visible?1:0;packed[offset++]=visible?cap:0;packed[offset++]=visible?(double)cap*cap:0;offset+=4;}
   }
   return new MyTuple<int,double[],bool>(mask,packed,known);
  }
  static MyTuple<int,double[],bool> CopyCameraDensity(MyTuple<int,double[],bool> packet)
  {return new MyTuple<int,double[],bool>(packet.Item1,(double[])packet.Item2.Clone(),packet.Item3);}
  static MyTuple<int,double[],bool> PackCameraDensity(CameraDensityResult result,bool known)
  {
   int count=result.Cameras.Length;var packed=new double[2+count+count*256*7];packed[0]=16;packed[1]=count;int offset=2+count;
   for(int i=0;i<count;i++)
   {
    var field=result.Cameras[i];
    if((result.VisibleMask&(1<<i))==0){offset+=256*7;continue;}
    // The cheap mask can conservatively retain a camera rejected by finer UV
    // tiles. Keep that current visibility proof and provide a valid size/field.
    if(field.SuggestedResolution==0)
    {
     packed[2+i]=field.CaptureMax;
     for(int j=0;j<256;j++){packed[offset++]=1;packed[offset++]=field.CaptureMax;packed[offset++]=(double)field.CaptureMax*field.CaptureMax;offset+=4;}
     continue;
    }
    packed[2+i]=field.SuggestedResolution;
    foreach(var cell in field.Cells){packed[offset++]=cell.Covered?1:0;packed[offset++]=cell.UpperMagnification;packed[offset++]=cell.UpperArea;packed[offset++]=cell.Magnification;packed[offset++]=cell.Area;packed[offset++]=cell.BlendWeight;packed[offset++]=cell.HasSample?1:0;}
   }
   return new MyTuple<int,double[],bool>(result.VisibleMask,packed,known);
  }
 }
}
