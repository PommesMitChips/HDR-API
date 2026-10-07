using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  // Only Draw publishes geometry. The game-update service validates and copies
  // it; the native renderer must never traverse scenes or game entities.
  sealed class SourceOcclusionConsumer
  {
   public string Key,Source;public object Identity;public double[] Triangles;
   public long Anchor;public MatrixD World;
  }
  sealed class SourceOcclusionSnapshot
  {public int Tick;public Dictionary<string,SourceOcclusionConsumer> Consumers;public Dictionary<long,SourceOcclusionInventory> History;public Dictionary<long,SourceOcclusionProof> Proofs;}
  sealed class SourceOcclusionInventory
  {public object Identity;public SourceOcclusionConsumer[] Consumers;}
  sealed class SourceOcclusionProof
  {public object Identity;public SourceOcclusionInventory Inventory;public SourceOcclusionConsumer[] Current;}
  Dictionary<string,SourceOcclusionConsumer> _sourceOcclusionDrawing;
  volatile SourceOcclusionSnapshot _sourceOcclusionPublished;
  SourceOcclusionSnapshot _sourceOcclusionPrevious;
  readonly Dictionary<long,SourceOcclusionInventory> _sourceOcclusionInventories=new Dictionary<long,SourceOcclusionInventory>();
  bool _sourceOcclusionHistoryOverflow;
  void BeginDisplaySourceOcclusionDraw()
  {_sourceOcclusionDrawing=new Dictionary<string,SourceOcclusionConsumer>();_sourceOcclusionPublished=null;}
  void EndDisplaySourceOcclusionDraw()
  {
   if(_sourceOcclusionDrawing==null)return;
   foreach(var consumer in _sourceOcclusionDrawing.Values)
   {
    foreach(string part in consumer.Source.Split(','))
    {
     long camera;if(!long.TryParse(part,out camera)||camera<=0){_sourceOcclusionHistoryOverflow=true;continue;}
     SourceOcclusionInventory prior;_sourceOcclusionInventories.TryGetValue(camera,out prior);
     if(prior==null&&_sourceOcclusionInventories.Count>=32){_sourceOcclusionHistoryOverflow=true;continue;}
     var all=prior==null?new List<SourceOcclusionConsumer>():new List<SourceOcclusionConsumer>(prior.Consumers);
     int index=all.FindIndex(c=>c.Key==consumer.Key);SourceOcclusionConsumer old=index<0?null:all[index];
     var envelope=SourceOcclusionEnvelope(old==null?null:old.Triangles,consumer.Triangles);
     if(old!=null&&SameArray(old.Triangles,envelope))continue;
     var historical=new SourceOcclusionConsumer{Key=consumer.Key,Identity=new object(),Triangles=envelope};
     if(index<0){if(all.Count>=MaxProjectedScreens){_sourceOcclusionHistoryOverflow=true;continue;}all.Add(historical);}else all[index]=historical;
     all.Sort((a,b)=>string.CompareOrdinal(a.Key,b.Key));
     _sourceOcclusionInventories[camera]=new SourceOcclusionInventory{Identity=new object(),Consumers=all.ToArray()};
    }
   }
   var published=new SourceOcclusionSnapshot{Tick=_ticks,Consumers=_sourceOcclusionDrawing,History=new Dictionary<long,SourceOcclusionInventory>(_sourceOcclusionInventories),Proofs=new Dictionary<long,SourceOcclusionProof>()};
   if(!_sourceOcclusionHistoryOverflow)foreach(var pair in published.History)
   {
    SourceOcclusionConsumer[] current;if(!ValidateSourceOcclusionClosure(published,pair.Key,out current))continue;
    SourceOcclusionProof previous=null;if(_sourceOcclusionPrevious!=null)_sourceOcclusionPrevious.Proofs.TryGetValue(pair.Key,out previous);
    bool same=previous!=null&&ReferenceEquals(previous.Inventory,pair.Value)&&SameSourceOcclusionClosure(previous.Current,current);
    published.Proofs[pair.Key]=same?previous:new SourceOcclusionProof{Identity=new object(),Inventory=pair.Value,Current=current};
   }
   _sourceOcclusionDrawing=null;_sourceOcclusionPrevious=published;_sourceOcclusionPublished=published;
  }
  void ClearDisplaySourceOcclusion()
  {_sourceOcclusionDrawing=null;_sourceOcclusionPublished=null;_sourceOcclusionPrevious=null;_sourceOcclusionInventories.Clear();_sourceOcclusionHistoryOverflow=false;}
  void RecordDisplaySourceOcclusion(Scene scene,IMyTerminalBlock anchor,MatrixD world)
  {
   if(_sourceOcclusionDrawing==null)return;
   // A moving grid's interpolated render transform cannot be certified from the
   // game-update API. It deliberately remains an unknown consumer.
   bool needed=false;foreach(var screen in scene.Screens.Values)if(screen.Data.SourceProvider=="camera-panorama"){needed=true;break;}
   if(!needed||anchor.CubeGrid==null||!anchor.CubeGrid.IsStatic)return;
   foreach(var screen in scene.Screens.Values)
   {
    var d=screen.Data;if(d.SourceProvider!="camera-panorama"||string.IsNullOrEmpty(d.SourceId))continue;
    string key=PackedKey(d.CallerId,scene.ConsoleId,d.Id);if(key.Length>128)continue;
    double[] triangles;try{triangles=SourceOcclusionTriangles(d,world);}catch{continue;}
    if(triangles==null)continue;
    SourceOcclusionConsumer previous=null;
    if(_sourceOcclusionPrevious!=null)_sourceOcclusionPrevious.Consumers.TryGetValue(key,out previous);
    var identity=previous!=null&&previous.Source==d.SourceId&&previous.World==world&&SameArray(previous.Triangles,triangles)?previous.Identity:new object();
    _sourceOcclusionDrawing[key]=new SourceOcclusionConsumer{Key=key,Source=d.SourceId,Anchor=scene.ConsoleId,World=world,Identity=identity,Triangles=triangles};
   }
  }
  // A closed enclosure is intentionally larger than the rendered source. It
  // includes curvature between tessellation vertices and both .0005m layer
  // offsets. Crops, one-sidedness, LOD and exhausted work caps cannot shrink it.
  static double[] SourceOcclusionTriangles(HoloProjectedScreenData d,MatrixD anchorWorld)
  {
   if(d==null||d.SurfaceKind<0||d.SurfaceKind>=4)return null;
   Vector3D extent;
   if(d.SurfaceKind==0)extent=new Vector3D(d.Width*.5,d.Height*.5,0);
   else if(d.SurfaceKind==1)extent=new Vector3D(d.SurfaceRadius,d.Height*.5,d.SurfaceRadius);
   else if(d.SurfaceKind==2)extent=new Vector3D(d.SurfaceRadius);
   else extent=SurfaceRadii(d);
   // Also cover the native camera-relative float conversion (within its 10km
   // supported range). A rotated rigid box retains at least this axis margin.
   extent+=new Vector3D(.03);
   var transform=ReadMatrix(d.Pose)*anchorWorld;var corners=new Vector3D[8];
   for(int i=0;i<8;i++)
   {var p=Vector3D.Transform(new Vector3D((i&1)==0?-extent.X:extent.X,(i&2)==0?-extent.Y:extent.Y,(i&4)==0?-extent.Z:extent.Z),transform);if(!Geometry.Finite(p.X)||!Geometry.Finite(p.Y)||!Geometry.Finite(p.Z))return null;corners[i]=p;}
   var indices=new[]{0,1,3,0,3,2,4,6,7,4,7,5,0,4,5,0,5,1,2,3,7,2,7,6,0,2,6,0,6,4,1,5,7,1,7,3};
   var triangles=new double[indices.Length*3];for(int i=0;i<indices.Length;i++){var p=corners[indices[i]];triangles[i*3]=p.X;triangles[i*3+1]=p.Y;triangles[i*3+2]=p.Z;}return triangles;
  }
  static bool SourceOcclusionUsesCamera(string source,long camera)
  {
   if(string.IsNullOrEmpty(source))return false;
   foreach(string part in source.Split(',')){long parsed;if(long.TryParse(part,out parsed)&&parsed==camera)return true;}return false;
  }
  // Never shrink within a session: earlier submitted billboards can still be
  // queued in the renderer after an edit, removal or source reassignment. The
  // union covers that history without inventing a renderer acknowledgement.
  static double[] SourceOcclusionEnvelope(double[] previous,double[] current)
  {
   var min=new Vector3D(double.PositiveInfinity);var max=new Vector3D(double.NegativeInfinity);
   foreach(var triangles in new[]{previous,current})if(triangles!=null)for(int i=0;i<triangles.Length;i+=3)
   {var p=new Vector3D(triangles[i],triangles[i+1],triangles[i+2]);min=Vector3D.Min(min,p);max=Vector3D.Max(max,p);}
   var points=new Vector3D[8];for(int i=0;i<8;i++)points[i]=new Vector3D((i&1)==0?min.X:max.X,(i&2)==0?min.Y:max.Y,(i&4)==0?min.Z:max.Z);
   var indices=new[]{0,1,3,0,3,2,4,6,7,4,7,5,0,4,5,0,5,1,2,3,7,2,7,6,0,2,6,0,6,4,1,5,7,1,7,3};
   var result=new double[indices.Length*3];for(int i=0;i<indices.Length;i++){var p=points[indices[i]];result[i*3]=p.X;result[i*3+1]=p.Y;result[i*3+2]=p.Z;}return result;
  }
  static bool SameSourceOcclusionClosure(SourceOcclusionConsumer[] a,SourceOcclusionConsumer[] b)
  {
   if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i].Key!=b[i].Key||!ReferenceEquals(a[i].Identity,b[i].Identity))return false;return true;
  }
  bool ValidateSourceOcclusionClosure(SourceOcclusionSnapshot published,long camera,out SourceOcclusionConsumer[] current)
  {
   current=null;
   var consumers=new List<SourceOcclusionConsumer>();
   try
   {
    // Re-enumerate membership, including consumers skipped by the draw loop.
    // Missing, dynamic, LCD and arbitrary-mesh consumers invalidate ALL proof.
    foreach(var scene in _scenes.Values)foreach(var screen in scene.Screens.Values)
    {
     var d=screen.Data;if(d.SourceProvider!="camera-panorama"||!SourceOcclusionUsesCamera(d.SourceId,camera))continue;
     string key=PackedKey(d.CallerId,scene.ConsoleId,d.Id);SourceOcclusionConsumer saved;
     if(consumers.Count>=MaxProjectedScreens||!published.Consumers.TryGetValue(key,out saved)||saved.Source!=d.SourceId)return false;
     var anchor=MyAPIGateway.Entities.GetEntityById(scene.ConsoleId) as IMyProjector;
     if(anchor==null||anchor.Closed||!anchor.IsWorking||anchor.CubeGrid==null||!anchor.CubeGrid.IsStatic)return false;
     var world=anchor.WorldMatrix;if(world!=saved.World)return false;
     var triangles=SourceOcclusionTriangles(d,world);if(triangles==null||!SameArray(triangles,saved.Triangles))return false;
     consumers.Add(saved);
    }
   }
   catch{return false;}
   // No declarations does not establish ownership of an arbitrary camera.
   if(consumers.Count==0)return false;consumers.Sort((a,b)=>string.CompareOrdinal(a.Key,b.Key));current=consumers.ToArray();return true;
  }
  // The returned delegate is the only render-thread service. It reads one
  // immutable publication and never touches scenes, entities or the gateway.
  object DisplaySourceOcclusionFresh(object[] args)
  {
   if(args==null||args.Length!=1||!(args[0] is long)||(long)args[0]<=0)return false;
   long camera=(long)args[0];return new Func<object,bool>(identity=>
   {var published=_sourceOcclusionPublished;SourceOcclusionProof proof;return identity!=null&&published!=null&&published.Proofs.TryGetValue(camera,out proof)&&ReferenceEquals(identity,proof.Identity);});
  }
  object DisplaySourceOcclusion(object[] args)
  {
   var unknown=new MyTuple<bool,object,MyTuple<string,object,double[]>[]>(false,null,new MyTuple<string,object,double[]>[0]);
   if(args==null||args.Length!=1||!(args[0] is long)||(long)args[0]<=0)return unknown;
   long camera=(long)args[0];var published=_sourceOcclusionPublished;SourceOcclusionProof proof;
   if(published==null||_sourceOcclusionHistoryOverflow||_ticks<published.Tick||_ticks-published.Tick>1||!ClientRenderingEnabled||!published.Proofs.TryGetValue(camera,out proof))return unknown;
   SourceOcclusionConsumer[] current;if(!ValidateSourceOcclusionClosure(published,camera,out current)||!SameSourceOcclusionClosure(proof.Current,current))return unknown;
   var inventory=proof.Inventory;
   var result=new MyTuple<string,object,double[]>[inventory.Consumers.Length];
   for(int i=0;i<result.Length;i++){var c=inventory.Consumers[i];result[i]=new MyTuple<string,object,double[]>(c.Key,c.Identity,(double[])c.Triangles.Clone());}
   return new MyTuple<bool,object,MyTuple<string,object,double[]>[]>(true,proof.Identity,result);
  }
 }
}
