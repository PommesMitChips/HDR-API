using System.Collections;
using System.Reflection;
using HoloMap;
using VRageMath;
using ProtoBuf.Meta;
internal static class CompositionSourceSlotTests
{
 static int checks;
 static void Check(bool ok,string why){if(!ok)throw new Exception("Composition source slots: "+why);checks++;}
 static object Static(string name,params object[] args){try{return typeof(HoloMapSession).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
 static object Call(object o,string name,params object[] args)=>ClientReplicationTests.Call(o,name,args);
 static object Field(object o,string name)=>ClientReplicationTests.Field(o,name);
 static object New(string name)=>Activator.CreateInstance(typeof(HoloMapSession).GetNestedType(name,BindingFlags.NonPublic),true);
 static void Reject(Action action,string why){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Expected composition rejection: "+why);}
 static HoloProjectedScreenData Screen()=>new HoloProjectedScreenData{CallerId=10,Id="canvas",Pose=(double[])Static("MatrixValues",MatrixD.Identity),CanvasWidth=4,CanvasHeight=2,Width=4,Height=2};
 static HoloProjectedSourceSlotData Slot(string id="camera",string provider="camera-panorama",string source="1")=>new HoloProjectedSourceSlotData{Id=id,Provider=provider,SourceId=source,Rect=new[]{-1d,-.5,2,1},Clip=new[]{-.5d,-.25,1,.5},UV=new[]{.25f,.25f,.5f,.5f},Order=4,Opacity=.6f,Revision=12};
 public static int Run()
 {
  checks=0;BoundsAndWire();CanvasAndCurvature();CacheOwnership();ProjectedEvidenceOwners();return checks;
 }
 static void BoundsAndWire()
 {
  var d=Screen();d.SourceSlots=new List<HoloProjectedSourceSlotData>{Slot()};d.UiSurfaceGeneration=9;Static("ValidateScreenData",d);
  var copy=(HoloProjectedScreenData)Static("CloneScreen",d,true);d.SourceSlots[0].Rect[0]=-9;d.SourceSlots[0].Clip[0]=-8;d.SourceSlots[0].UV[0]=.1f;d.SourceSlots[0].SourceId="2";
  Check(copy.SourceSlots[0].Rect[0]==-1&&copy.SourceSlots[0].Clip[0]==-.5&&copy.SourceSlots[0].UV[0]==.25f&&copy.SourceSlots[0].SourceId=="1","freeze detaches every slot array and binding");Check(copy.UiSurfaceGeneration==9&&copy.SourceSlots[0].Revision==12,"UI coordinates and source binding revisions remain separate");
  var model=RuntimeTypeModel.Create();using var stream=new MemoryStream();model.Serialize(stream,copy);stream.Position=0;var received=(HoloProjectedScreenData)model.Deserialize(stream,null,typeof(HoloProjectedScreenData));Static("ValidateScreenData",received);
  Check(received.SourceSlots.Count==1&&received.SourceSlots[0].Id=="camera"&&received.SourceSlots[0].Opacity==.6f&&received.SourceSlots[0].Visible,"bounded new declarations round trip with defaults");Check(received.UiSurfaceGeneration==9,"UI generation survives source wire model");
  var legacy=Screen();using var oldStream=new MemoryStream();model.Serialize(oldStream,legacy);oldStream.Position=0;legacy=(HoloProjectedScreenData)model.Deserialize(oldStream,null,typeof(HoloProjectedScreenData));Static("ValidateScreenData",legacy);Check(legacy.SourceSlots==null&&legacy.UiSurfaceGeneration==0,"old scalar screen wire declarations remain valid");
  var slotTags=typeof(HoloProjectedScreenData).GetFields().Select(f=>(f.Name,f.GetCustomAttribute<ProtoBuf.ProtoMemberAttribute>()?.Tag)).ToArray();Check(slotTags.Any(t=>t.Name=="SourceSlots"&&t.Item2==64)&&slotTags.Any(t=>t.Name=="UiSurfaceGeneration"&&t.Item2==65)&&!slotTags.Any(t=>t.Item2>=25&&t.Item2<=35),"new tags preserve reserved camera capture space");
  received.SourceSlots.Add(Slot());Reject(()=>Static("ValidateScreenData",received),"duplicate slot identity");received.SourceSlots.RemoveAt(1);
  received.SourceSlots[0].UV[2]=1;Reject(()=>Static("ValidateScreenData",received),"UV beyond normalized bounds");received.SourceSlots[0].UV[2]=.5f;
  received.SourceSlots[0].Rect[2]=double.NaN;Reject(()=>Static("ValidateScreenData",received),"nonfinite rectangle");received.SourceSlots[0].Rect[2]=2;
  received.SourceSlots[0].Opacity=-.1f;Reject(()=>Static("ValidateScreenData",received),"opacity range");received.SourceSlots[0].Opacity=.6f;
  for(int i=1;i<17;i++)received.SourceSlots.Add(Slot("s"+i));Reject(()=>Static("ValidateScreenData",received),"bounded slot count");
  var effective=(HoloProjectedScreenData)Static("SourceSlotSettings",copy,copy.SourceSlots[0]);Check(effective.Id==copy.Id&&effective.CallerId==copy.CallerId&&effective.SourceId=="1","provider requests preserve real screen identity and owner");Check(effective.CanvasWidth==2&&effective.CanvasHeight==1&&effective.SourceFov==copy.SourceFov,"slot source canvas size and inherited camera settings");
  copy.SourceFov=110;copy.SourceSlots[0].CameraSettingsOverride=true;copy.SourceSlots[0].CameraSettingsMask=2;copy.SourceSlots[0].SourceCaptureProfile=1;effective=(HoloProjectedScreenData)Static("SourceSlotSettings",copy,copy.SourceSlots[0]);Check(effective.SourceFov==110&&effective.SourceCaptureProfile==1,"explicit quality preserves dynamic inherited lens settings");copy.SourceSlots[0].CameraSettingsMask=1;copy.SourceSlots[0].SourceFov=100;copy.SourceCaptureProfile=1;effective=(HoloProjectedScreenData)Static("SourceSlotSettings",copy,copy.SourceSlots[0]);Check(effective.SourceFov==100&&effective.SourceCaptureProfile==1,"explicit panorama preserves dynamic inherited quality");
  copy.SourceSlots[0].Pose=(double[])Static("MatrixValues",MatrixD.CreateRotationZ(.25)*MatrixD.CreateTranslation(.2,.1,0));var poseCopy=(HoloProjectedScreenData)Static("CloneScreen",copy,true);copy.SourceSlots[0].Pose[12]=9;Check(poseCopy.SourceSlots[0].Pose[12]==.2,"slot canvas pose freezes independently");Static("ValidateScreenData",poseCopy);
 }
 static void CanvasAndCurvature()
 {
  var d=Screen();var slot=Slot();var raw=(SurfaceMesh)Static("RasterQuad",2d,1d);var canvas=(SurfaceMesh)Static("SourceSlotCanvasMesh",raw,slot,true);
  Check(canvas.Geometry.Points.All(p=>p.X>=-.500001&&p.X<=.500001&&p.Y>=-.250001&&p.Y<=.250001),"local clip is applied before every surface map");Check(canvas.UV.All(u=>u.X>=.25&&u.X<=.75&&u.Y>=.25&&u.Y<=.75),"UV subrectangle preserves normalized texture source window");Check(raw.Geometry.Points[0].X==-1&&raw.UV[0].X==0,"preparation does not mutate provider arrays");
  var vector=(SurfaceMesh)Static("SourceSlotCanvasMesh",raw,slot,false);Check(vector.Geometry.Points.All(p=>p.X>=-.500001&&p.X<=.500001&&p.Y>=-.250001&&p.Y<=.250001),"vector and raster sources share local crop bounds");
  slot.Pose=(double[])Static("MatrixValues",MatrixD.CreateRotationZ(Math.PI/2)*MatrixD.CreateTranslation(.1,.2,0));var posed=(SurfaceMesh)Static("SourceSlotCanvasMesh",raw,slot,true);Check(posed.Geometry.Points.All(p=>p.X>=-.150001&&p.X<=.350001&&p.Y>=-.300001&&p.Y<=.700001),"arbitrary canvas pose transforms after local clip and before surface mapping");Check((bool)Static("SourceSlotIntersects",d,slot),"posed source visibility uses transformed canvas bounds");slot.Pose=null;
  foreach(int kind in new[]{1,2,3,4})
  {
   d.SurfaceKind=kind;d.SurfaceRadii=kind==3?new[]{2d,1.5,3}:null;
   d.SurfaceMeshPoints=kind==4?new[]{-2d,-1,0,2,-1,0,2,1,0,-2,1,0}:null;d.SurfaceMeshTriangles=kind==4?new[]{0,1,2,0,2,3}:null;d.SurfaceMeshUV=kind==4?new[]{0f,1,1,1,1,0,0,0}:null;
   var style=(SurfaceStyle)Static("ScreenSurfaceStyle",d,0d);var mapped=SurfaceMapping.Warp(canvas,d.CanvasWidth,d.CanvasHeight,style,.0015);
   Check(mapped.Geometry.Triangles.Length>0&&mapped.Geometry.Points.All(p=>Geometry.Finite(p.X)&&Geometry.Finite(p.Y)&&Geometry.Finite(p.Z)),"same clipped canvas stream maps onto surface kind "+kind);
  }
  var demand=(Vector2I)Static("SourceSlotDemandSize",Screen(),slot,new Vector2I(1024,512));Check(demand.X>0&&demand.Y>0&&demand.X<=1024&&demand.Y<=512,"slot crop and UV density bound requested pixels independently of whole canvas");
 }
 static void CacheOwnership()
 {
  var session=new HoloMapSession();var parent=New("ProjectedCache");ClientReplicationTests.SetField(parent,"Anchor",20L);ClientReplicationTests.SetField(parent,"Caller",10L);ClientReplicationTests.SetField(parent,"Id","canvas");var a=Call(session,"EnsureSourceSlotCache",parent,Slot("a"));var b=Call(session,"EnsureSourceSlotCache",parent,Slot("b"));Check(!ReferenceEquals(Field(a,"Source"),Field(b,"Source")),"slots have independent evidence, failure and refresh caches");Check((string)Field(Field(a,"Source"),"Id")=="canvas"&&(string)Field(Field(b,"Source"),"Id")=="canvas","cache independence never fabricates a screen identity");Check((string)Field(Field(a,"Source"),"ConsumerSuffix")!= (string)Field(Field(b,"Source"),"ConsumerSuffix"),"generated raster upload keys cannot alias sibling slots");
  object evidence=new object();int releases=0;Func<string,object[],object> endpoint=(op,args)=>{if(op=="release")releases++;return true;};Static("RetainProviderEvidence",endpoint,evidence);Static("RetainProviderEvidence",endpoint,evidence);Static("ReleaseUnownedProviderEvidence",endpoint,evidence);Check(releases==0,"failed acquisition cannot release a sibling's retained provider proof");Static("ReleaseProviderEvidence",endpoint,evidence);Check(releases==0,"removing one opaque shared lease consumer keeps the sibling resource live");Static("ReleaseProviderEvidence",endpoint,evidence);Check(releases==1,"final consumer releases its opaque provider proof exactly once");Static("ReleaseSourceSlots",parent);Check(((IDictionary)Field(parent,"Slots")).Count==0,"screen teardown removes every independent slot cache");
 }
 static void ProjectedEvidenceOwners()
 {
  int releases=0,otherReleases=0;object evidence=new object();Func<string,object[],object> endpoint=(op,args)=>{if(op=="release")releases++;return true;};Func<string,object[],object> other=(op,args)=>{if(op=="release")otherReleases++;return true;};
  object Cache(){var value=New("ProjectedCache");ClientReplicationTests.SetField(value,"SourceProtocol",2);ClientReplicationTests.SetField(value,"SourceEndpoint",endpoint);ClientReplicationTests.SetField(value,"SourceEvidence",evidence);Static("RetainProjectedProviderEvidence",endpoint,evidence,value);return value;}
  var a=Cache();var b=Cache();Static("ReleaseFailedSourceAcquisition",endpoint,evidence,a);Check(releases==0,"failed exact-owner reacquisition retains the current projected lease");
  ClientReplicationTests.SetField(a,"SourceEndpoint",other);Static("ReleaseFailedSourceAcquisition",endpoint,evidence,a);Check(releases==0&&otherReleases==0,"stale current-cache endpoint does not retire a live sibling's exact offering ownership");
  Static("ClearExternalBudgetView",b);Check(releases==1&&otherReleases==0,"last genuine projected sibling releases once despite a stale foreign-endpoint claim");Check(ReferenceEquals(Field(a,"SourceEndpoint"),other)&&ReferenceEquals(Field(a,"SourceEvidence"),evidence),"failure never overwrites or borrows a foreign cached endpoint's retained pair");Static("ClearExternalBudgetView",a);
  evidence=new object();releases=otherReleases=0;var wrong=Cache();ClientReplicationTests.SetField(wrong,"SourceEndpoint",other);Static("ReleaseFailedSourceAcquisition",endpoint,evidence,wrong);Check(releases==1&&otherReleases==0,"same evidence with only a stale cache owner releases the failed offering endpoint");Check(ReferenceEquals(Field(wrong,"SourceEndpoint"),other)&&ReferenceEquals(Field(wrong,"SourceEvidence"),evidence),"wrong-endpoint failed offer leaves the existing foreign owner untouched");Static("ClearExternalBudgetView",wrong);
  evidence=new object();releases=0;var projected=Cache();Static("RetainProviderEvidence",endpoint,evidence);Static("ClearExternalBudgetView",projected);Check(releases==0,"projected teardown does not consume a live local/generic retainer");Static("ReleaseProviderEvidence",endpoint,evidence);Check(releases==1,"generic final teardown releases a mixed projected/local proof once");
  evidence=new object();releases=0;projected=Cache();Static("RetainProviderEvidence",endpoint,evidence);Static("ReleaseProviderEvidence",endpoint,evidence);Check(releases==0,"generic teardown preserves a live projected retainer");Static("ClearExternalBudgetView",projected);Check(releases==1,"projected final teardown releases a mixed local/projected proof once");
 }
}
