using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

internal static class ModClientSourceCompositionTests
{
 static int checks;
 static void Check(bool ok,string why){if(!ok)throw new Exception("Local mod composition: "+why);checks++;}
 static object Call(object o,string name,params object[] values)=>ClientReplicationTests.Call(o,name,values);
 static object Field(object o,string name)=>ClientReplicationTests.Field(o,name);
 static void Set(object o,string name,object value)=>ClientReplicationTests.SetField(o,name,value);
 static object New(string name)=>Activator.CreateInstance(typeof(HoloMapSession).GetNestedType(name,BindingFlags.NonPublic),true);
 static void Reject(Action action,string why){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Expected local source rejection: "+why);}
 sealed class Fixture:IDisposable
 {
  public readonly ClientReplicationTests.GatewayScope Gateway=new();
  public readonly HoloMapSession Session=new();
  public readonly Dictionary<long,object> Registry=new();
  public readonly List<ModClientTriangleSubmission> Submitted=new();
  public Func<string,object[],object> Service,OwnerEndpoint,Plugin;
  public Func<object,object[]> Resolve;
  public ModClientOwner Owner;public ModClientContext Context;public long Handle;
  public object Anchor;public object Evidence=new();public object LastBinding;
  public int Frames,Releases,Tick;public bool AnchorWorking=true,AnchorAccess=true,SourceWorking=true,SourceAccess=true,Construct=true,Proof=true,RevokeInFrame;
  public string[] Caps={"lcd-texture","camera-panorama","native-portal"};public Vector3D Eye=new(0,0,5);public Vector2 Viewport=new(1920,1080);
  public Action OnRelease,OnFrame;
  public Fixture()
  {
   Anchor=Block(typeof(IMyProjector),20,true);Registry.Add(20,Anchor);Registry.Add(55,Block(typeof(IMyTextPanel),55,false));Registry.Add(56,Block(typeof(IMyCameraBlock),56,false));Registry.Add(10,Block(typeof(IMyProgrammableBlock),10,false));
   Gateway.Install("Utilities",(m,a)=>m.Name=="get_IsDedicated"?false:m.ReturnType==typeof(void)?null:throw new Exception("Source fixture utility: "+m.Name));
   Gateway.Install("Entities",(m,a)=>m.Name=="GetEntityById"&&Registry.TryGetValue((long)a[0],out var entity)?entity:null);
   Gateway.Install("Session",(m,a)=>m.Name switch
   {
    "get_Player"=>DrawTestProxy.Make(m.ReturnType,(pm,pa)=>pm.Name switch
    {"get_IdentityId"=>77L,"get_Character"=>DrawTestProxy.Make(pm.ReturnType,(cm,ca)=>cm.Name=="GetPosition"?Eye:throw new Exception(cm.Name)),_=>throw new Exception(pm.Name)}),
    "get_Camera"=>DrawTestProxy.Make(m.ReturnType,(cm,ca)=>cm.Name switch
    {"get_Position"=>Eye,"get_ViewMatrix"=>MatrixD.CreateLookAt(Eye,Vector3D.Zero,Vector3D.Up),"get_ProjectionMatrix"=>MatrixD.CreatePerspectiveFieldOfView(1.1,Viewport.X/Viewport.Y,.1,1000),"get_ViewportSize"=>cm.ReturnType==typeof(Vector2I)?new Vector2I((int)Viewport.X,(int)Viewport.Y):Viewport,"get_NearPlaneDistance"=>.1f,_=>throw new Exception(cm.Name)}),
    "get_ElapsedPlayTime"=>TimeSpan.FromSeconds(Tick/60d),_=>throw new Exception(m.Name)
   });
   Set(Session,"_modClientActive",true);Set(Session,"_displaySourceRegistered",true);Set(Session,"_modClientOfflineSubmit",new Action<ModClientTriangleSubmission>(s=>Submitted.Add(s)));
   Service=(Func<string,object[],object>)Delegate.CreateDelegate(typeof(Func<string,object[],object>),Session,typeof(HoloMapSession).GetMethod("ModClientService",BindingFlags.Instance|BindingFlags.NonPublic));
   OwnerEndpoint=(Func<string,object[],object>)Service("open",new object[]{"local-source-tests"});Owner=((Dictionary<string,ModClientOwner>)Field(Session,"_modClientOwners"))["local-source-tests"];
   Handle=(long)OwnerEndpoint("create-world",new object[]{0,MatrixD.Identity});Context=Owner.Contexts[Handle];Shape("plane");OwnerEndpoint("context-anchor",new[]{(object)Handle,Anchor});
   Plugin=(op,a)=>
   {
    if(op=="version")return "HDR.LocalModNative/1";if(op=="capabilities")return Caps;
    if(op=="frame")
    {
     Frames++;LastBinding=a[0];var descriptor=Resolve(a[0]);Check(descriptor!=null&&descriptor.Length==10,"frame resolves genuine local authority");Check(a.Length==3&&(int)a[1]>0&&(int)a[2]>0,"local frame has binding plus viewport dimensions");
     OnFrame?.Invoke();if(RevokeInFrame)OwnerEndpoint("context-visible",new object[]{Handle,false});return new MyTuple<int,object,object,bool,double>(2,new MyTuple<string,Vector2I,long>("local_test_texture",new Vector2I(512,256),1),Evidence,false,6);
    }
    if(op=="valid")return Proof&&a.Length==2&&Resolve(a[0]) is object[] descriptor&&((MyTuple<int,int,double,bool>)descriptor[7]).Item4&&ReferenceEquals(a[1],Evidence);
    if(op=="release"){Releases++;OnRelease?.Invoke();return true;}
    return false;
   };
   Register("lcd-texture",2,(op,a)=>false);Register("camera-panorama",2,(op,a)=>false);Register("native-portal",2,(op,a)=>false);
   Resolve=(Func<object,object[]>)Call(Session,"DisplaySourceService","local-mod-native-consumers",new object[]{1,Plugin});
  }
  object Block(Type type,long id,bool anchor)=>DrawTestProxy.Make(type,(m,a)=>m.Name switch
  {"get_EntityId"=>id,"get_OwnerId"=>77L,"get_Closed"=>false,"get_IsWorking"=>anchor?AnchorWorking:SourceWorking,"HasPlayerAccess"=>anchor?AnchorAccess:SourceAccess,"IsSameConstructAs"=>Construct,"GetPosition"=>Vector3D.Zero,"get_WorldMatrix"=>MatrixD.Identity,_=>throw new Exception("Source fixture block: "+m.Name)});
  public Func<string,object[],object> Register(string name,int protocol,Func<string,object[],object> endpoint)
  {
   var provider=New("DisplayProvider");Set(provider,"Name",name);Set(provider,"Protocol",protocol);Set(provider,"Endpoint",endpoint);Set(provider,"Generation",100L+name.Length+Tick);
   ((IDictionary)Field(Session,"_displayProviders"))[name]=provider;return endpoint;
  }
  public object Op(string op,params object[] values)=>OwnerEndpoint(op,new object[]{Handle}.Concat(values).ToArray());
  public void Slot(string id="video",string provider="lcd-texture",string source="55:0",int order=2)
  {Op("context-slot",id,provider,source,new Vector4(-1,-.5f,2,1),new Vector4(-.5f,-.25f,1,.5f),new Vector4(.25f,.25f,.5f,.5f),order,.75d);}
  public List<ModClientItem> Collect()
  {Set(Session,"_ticks",++Tick*20);var items=new List<ModClientItem>(Context.Items.Values);Call(Session,"ModClientCollectSourceItems",Owner,Context,items);items.Sort((a,b)=>(int)Call(Session,"ModClientCompositionCompare",a,b));return items;}
  public MyTuple<bool,string> Status(string id="video")=>(MyTuple<bool,string>)Op("context-slot-status",id);
  public void Shape(string kind)
  {
   if(kind=="plane")Op("context-surface",kind,4d,2d);
   else if(kind=="mesh")Op("context-surface",kind,4d,2d,new[]{new Vector3D(-2,-1,0),new Vector3D(2,-1,0),new Vector3D(2,1,0),new Vector3D(-2,1,0)},new[]{0,1,2,0,2,3},new[]{Vector2.UnitY,Vector2.One,Vector2.UnitX,Vector2.Zero},"outside");
   else if(kind=="ellipsoid")Op("context-surface",kind,4d,2d,new Vector3D(2,1.5,2),1.5d,1.1d,"outside");
   else Op("context-surface",kind,4d,2d,2d,1.5d,1.1d,"outside");
  }
  public void Draw(ModClientItem item)
  {object[] args={Context,item,Viewport,MatrixD.Identity,MatrixD.Identity,Eye,8192};Call(Session,"DrawModClientItem",args);Check((int)args[6]==8192-Submitted.Count,"real source submission bills mapped primitives");}
  public void Dispose(){OnRelease=null;Call(Session,"UnloadModClientApi");Gateway.Dispose();}
 }
 public static int Run()
 {
  checks=0;DeclarationsAndMapping();LeaseAndAuthority();GenericProviders();PortalReuse();Reentrancy();HudComposition();ArtworkGrowthBudget();ConfigurableGeometryAllowance();return checks;
 }
 static void DeclarationsAndMapping()
 {
  using var f=new Fixture();Check(ReferenceEquals(f.Resolve,Call(f.Session,"DisplaySourceService","local-mod-native-consumers",new object[]{1,f.Plugin})),"repeated handshake preserves descriptor callback identity");Check(Call(f.Session,"DisplaySourceService","local-mod-native-consumers",new object[]{2,f.Plugin}) is bool bad&&!bad,"wrong local protocol fails closed");
  f.Slot();var settings=(MyTuple<string,string,Vector4,Vector4,Vector4,int>)f.Op("context-slot-settings","video");Check(settings.Item1=="lcd-texture"&&settings.Item2=="55:0"&&settings.Item6==2,"public declarations preserve provider/source/order");
  Reject(()=>f.Op("context-slot","video","lcd-texture","55:0",new Vector4(0,0,-1,2),new Vector4(0,0,2,1),new Vector4(0,0,1,1),1,1d),"invalid replacement rectangle");Check(((MyTuple<string,string,Vector4,Vector4,Vector4,int>)f.Op("context-slot-settings","video")).Item3==settings.Item3,"failed mutation preserves prior source declaration");
  foreach(string shape in new[]{"plane","cylinder","sphere","ellipsoid","mesh"})
  {
   f.Shape(shape);var items=f.Collect();Check(items.Count==1&&f.Status().Item1,shape+" source becomes ready");var item=items.Single();Check(item.Geometry.Points.All(p=>p.X>=-.500001&&p.X<=.500001&&p.Y>=-.250001&&p.Y<=.250001),shape+" local source clip precedes mapper");Check(item.UV.All(u=>u.X>=.25&&u.X<=.75&&u.Y>=.25&&u.Y<=.75),shape+" UV crop remains exact");
   f.Submitted.Clear();f.Draw(item);Check(f.Submitted.Count>0&&f.Submitted.All(t=>t.Material=="local_test_texture"&&!t.Hud),shape+" actual surface draw emits source material");
   var descriptor=f.Resolve(f.LastBinding);var stamp=(MyTuple<string,long,long,long,long>)descriptor[2];Check(stamp.Item1==f.Owner.Id&&stamp.Item2==f.Owner.Generation&&stamp.Item3==f.Handle&&stamp.Item4==f.Context.SurfaceRevision,"descriptor stamps current real local owner/context");Check(ReferenceEquals(descriptor[3],f.Anchor)&&ReferenceEquals(((IMyTerminalBlock[])descriptor[6])[0],f.Registry[55]),"descriptor carries actual anchor and source identities");Check(((IDictionary)Field(f.Session,"_scenes")).Count==0,"local source creates no authoritative PB scene");
  }
  f.Shape("plane");f.Op("mesh","under",new[]{Vector3D.Zero,Vector3D.UnitX,Vector3D.UnitY},new[]{0,1,2},Vector4.One);f.Op("item-order","under",2);f.Op("mesh","over",new[]{Vector3D.Zero,Vector3D.UnitX,Vector3D.UnitY},new[]{0,1,2},Vector4.One);f.Op("item-order","over",3);var mixed=f.Collect();Check(mixed.Select(i=>i.Id).SequenceEqual(new[]{"under","video","over"}),"source paint order interleaves with artwork and follows equal-order artwork");
  var old=f.LastBinding;f.Op("context-slot-pose","video",MatrixD.CreateRotationZ(.3)*MatrixD.CreateTranslation(.1,0,0));f.Collect();Check(f.Resolve(old)==null,"slot pose replacement revokes old binding");
  f.Slot("unknown","custom-missing","camera");Check(f.Status("unknown").Item2.Contains("RequiresPlugin"),"unknown provider has explicit requirement");
 }
 static void LeaseAndAuthority()
 {
  using var f=new Fixture();f.Slot("a");f.Slot("b");Check(f.Collect().Count==2,"siblings retain independent source items");Check(f.Releases==0,"shared evidence remains live after acquisition");f.Op("context-slot-remove","a");Check(f.Releases==0,"removing one shared consumer keeps sibling resource");var last=f.LastBinding;f.Op("context-slot-remove","b");Check(f.Releases==1&&f.Resolve(last)==null,"final shared consumer releases exactly once and revokes authority");
  f.Evidence=new object();f.Slot();f.Collect();int frames=f.Frames,releases=f.Releases;var stale=f.LastBinding;f.Op("context-visible",false);Check(f.Resolve(stale)==null&&f.Releases==releases+1,"hide retires lease and callback authority synchronously");Check(f.Collect().Count==0&&f.Frames==frames,"hidden slot acquires no frame");f.Op("context-visible",true);
  foreach(string invalid in new[]{"anchor-power","anchor-access","source-power","source-access","source-removed","source-identity","construct","renderer","offscreen","no-viewport"})
  {
   f.Evidence=new object();f.Collect();var item=f.Collect().Single();var binding=f.LastBinding;int before=f.Releases;object source=f.Registry[55];
   switch(invalid){case "anchor-power":f.AnchorWorking=false;break;case "anchor-access":f.AnchorAccess=false;break;case "source-power":f.SourceWorking=false;break;case "source-access":f.SourceAccess=false;break;case "source-removed":f.Registry.Remove(55);break;case "source-identity":f.Registry[55]=f.Registry[56];break;case "construct":f.Construct=false;break;case "renderer":Set(f.Session,"ClientRenderingEnabled",false);break;case "offscreen":f.Eye=new Vector3D(0,0,100);break;case "no-viewport":f.Viewport=Vector2.Zero;break;}
   Check(f.Resolve(binding)==null||!((MyTuple<int,int,double,bool>)f.Resolve(binding)[7]).Item4,invalid+" removes demand");Check(!(bool)Call(f.Session,"ModClientSourceItemCurrent",f.Owner,f.Context,item)&&f.Releases==before+1,invalid+" retires actual cached lease once");Check(!(bool)Call(f.Session,"ModClientSourceItemCurrent",f.Owner,f.Context,item)&&f.Releases==before+1,invalid+" retired draw item cannot use freed material");
   f.AnchorWorking=f.AnchorAccess=f.SourceWorking=f.SourceAccess=f.Construct=true;Set(f.Session,"ClientRenderingEnabled",true);f.Registry[55]=source;f.Eye=new Vector3D(0,0,5);f.Viewport=new Vector2(1920,1080);
  }
  f.Evidence=new object();f.Collect();var owned=f.LastBinding;f.Service("open",new object[]{f.Owner.Id});Check(f.Resolve(owned)==null,"owner replacement invalidates opaque binding");
  using var revoked=new Fixture();revoked.Slot();revoked.RevokeInFrame=true;Check(revoked.Collect().Count==0&&revoked.Releases==1,"reentrant hidden frame is released before admission exactly once");
  using var sharedRevoke=new Fixture();sharedRevoke.Slot();sharedRevoke.Collect();sharedRevoke.OnFrame=()=>sharedRevoke.Op("context-visible",false);Check(sharedRevoke.Collect().Count==0&&sharedRevoke.Releases==1,"reentrant retirement of an already owned shared frame cannot release it twice");
  using var disabled=new Fixture();disabled.Slot();disabled.Collect();int beforeDisabled=disabled.Releases;var disabledBinding=disabled.LastBinding;Set(disabled.Session,"ClientRenderingEnabled",false);disabled.Session.Draw();Check(disabled.Releases==beforeDisabled+1&&disabled.Resolve(disabledBinding)==null,"disabled Draw retires retained Core source lease before platform or GPU body");disabled.Session.Draw();Check(disabled.Releases==beforeDisabled+1,"repeated disabled Draw cannot release source lease twice");
 }
 static void GenericProviders()
 {
  using var f=new Fixture();int genericFrames=0,genericReleases=0;var evidence=new object();Func<object,object[]> resolver=null;
  Func<string,object[],object> endpoint=(op,a)=>op switch
  {"frame"=>Frame(a),"valid"=>resolver(a[0])!=null&&ReferenceEquals(a[1],evidence),"release"=>Release(),_=>false};
  object Frame(object[] a){genericFrames++;Check(a.Length==3&&resolver(a[0])[4].Equals("custom-vector"),"opted-in vector provider receives genuine local binding");return new MyTuple<Vector3D[],int[],Vector4[],object,bool,double>(new[]{new Vector3D(-1,-.5,0),new Vector3D(1,-.5,0),new Vector3D(1,.5,0),new Vector3D(-1,.5,0)},new[]{0,1,2,0,2,3},new[]{Vector4.One,Vector4.One},evidence,false,6);}
  object Release(){genericReleases++;return true;}
  f.Register("custom-vector",1,endpoint);f.Slot("custom","custom-vector","opaque-custom");Check(f.Status("custom").Item2.Contains("Unsupported consumer capability"),"installed provider without local opt-in has precise capability reason");Check(((MyTuple<bool,bool,string>)f.Op("context-source-status","custom-vector")).Item3.Contains("Unsupported consumer capability"),"context capability status distinguishes provider without opt-in from missing plugin");
  resolver=(Func<object,object[]>)Call(f.Session,"DisplaySourceService","local-mod-native-consumers",new object[]{1,endpoint});var items=f.Collect();Check(items.Count==1&&items[0].Material==null&&genericFrames==1,"opted-in generic v1 source composes vector geometry");f.Submitted.Clear();f.Draw(items[0]);Check(f.Submitted.Count>0,"actual generic vector source uses common mapper");
  Check((bool)Call(f.Session,"DisplaySourceService","unregister",new object[]{"custom-vector",endpoint}),"actual generic unregister succeeds");Check(genericReleases==1&&f.Collect().Count==0,"generic unregister retires local lease and content");
  Func<object,object[]> rasterResolver=null;object rasterEvidence=new object();int rasterReleases=0;Func<string,object[],object> raster=(op,a)=>op switch
  {"frame"=>new MyTuple<int,object,object,bool,double>(2,new MyTuple<string,Vector2I,long>("generic_local_texture",new Vector2I(512,256),7),rasterEvidence,false,6),"valid"=>rasterResolver(a[0])!=null&&ReferenceEquals(a[1],rasterEvidence),"release"=>++rasterReleases,_=>false};
  f.Register("custom-raster",2,raster);rasterResolver=(Func<object,object[]>)Call(f.Session,"DisplaySourceService","local-mod-native-consumers",new object[]{1,raster});f.Slot("raster","custom-raster","raster-source");var rasterItems=f.Collect();Check(rasterItems.Count==1&&rasterItems[0].Material=="generic_local_texture","generic opted-in v2 provider retains independent texture source");f.Op("context-slot-remove","raster");Check(rasterReleases==1,"generic v2 evidence retires exactly once");
  Func<object,object[]> oldResolver=null;object oldBinding=null;f.Op("context-slot-remove","custom");
  for(int i=0;i<12;i++)
  {
   object offer=new object();Func<object,object[]> currentResolver=null;object currentBinding=null;Func<string,object[],object> replacement=(op,a)=>op switch
   {"frame"=>ReplacementFrame(a),"valid"=>currentResolver(a[0])!=null&&ReferenceEquals(a[1],offer),"release"=>true,_=>false};
   object ReplacementFrame(object[] a){currentBinding=a[0];return new MyTuple<int,object,object,bool,double>(2,new MyTuple<string,Vector2I,long>("replacement_local_texture",new Vector2I(512,256),1),offer,false,6);}
   f.Register("custom-raster",2,replacement);currentResolver=(Func<object,object[]>)Call(f.Session,"DisplaySourceService","local-mod-native-consumers",new object[]{1,replacement});f.Slot("replacement","custom-raster","replacement");Check(f.Collect().Count==1,"replacement provider retains current local consumer");if(oldResolver!=null)Check(oldResolver(oldBinding)==null,"retired provider resolver cannot attest old binding");Check(((IList)Field(f.Session,"_modClientLocalProviders")).Count<=2,"obsolete provider resolver registrations remain bounded");oldResolver=currentResolver;oldBinding=currentBinding;
  }
 }
 static void PortalReuse()
 {
  using var f=new Fixture();f.Slot("portal","native-portal","paired");Check(f.Collect().Count==0&&f.Status("portal").Item2.Contains("declare the real portal"),"portal source requires a real declaration");
  var scene=New("Scene");Set(scene,"ConsoleId",20L);((IDictionary)Field(f.Session,"_scenes"))[20L]=scene;
  var data=new HoloProjectedScreenData{CallerId=10,Id="paired",SourceProvider="native-portal",SourceId="paired",Pose=new[]{1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1},CanvasWidth=2,CanvasHeight=2,Width=2,Height=2,Aspect="stretch",UiRasterWidth=512,UiRasterHeight=512,Portal=new HoloPortalData{ExitPose=new[]{1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1},ShellPose=new[]{1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1}}};
  var screen=New("ProjectedScreen");Set(screen,"Data",data);((IDictionary)Field(scene,"Screens"))["10:paired"]=screen;Check(f.Collect().Count==1,"local portal window reuses actual paired declaration");var binding=f.LastBinding;var descriptor=f.Resolve(binding);Check(ReferenceEquals(descriptor[9],data)&&((object[])descriptor[8]).Length==19&&ReferenceEquals(((IMyTerminalBlock[])descriptor[6])[0],f.Anchor),"portal descriptor preserves original declaration identity and native world payload");
  f.SourceAccess=false;Check(f.Resolve(binding)==null,"original portal caller viewer permission remains authoritative");f.SourceAccess=true;Set(screen,"Data",new HoloProjectedScreenData());Check(f.Resolve(binding)==null,"original portal declaration replacement invalidates local window");
 }
 static void Reentrancy()
 {
  using var f=new Fixture();f.Slot();f.Collect();var old=f.LastBinding;f.OnRelease=()=>{f.OnRelease=null;f.Service("open",new object[]{f.Owner.Id});};f.OwnerEndpoint("release",new object[0]);Check(f.Resolve(old)==null&&((Dictionary<string,ModClientOwner>)Field(f.Session,"_modClientOwners"))[f.Owner.Id].Generation>f.Owner.Generation,"release callback reopening owner preserves replacement authority");
  using var clear=new Fixture();clear.Slot();clear.Collect();clear.OnRelease=()=>{clear.OnRelease=null;clear.Slot("new");};clear.Op("clear");Check(clear.Collect().Single().Id=="new","clear release callback's newer declaration survives");
  using var replace=new Fixture();replace.Slot();replace.Collect();replace.OnRelease=()=>{replace.OnRelease=null;replace.Op("context-slot-order","video",7);};replace.Op("context-slot-order","video",5);Check(((MyTuple<string,string,Vector4,Vector4,Vector4,int>)replace.Op("context-slot-settings","video")).Item6==7,"source mutation publishes before release callback and preserves newer declaration");
  using var stopped=new Fixture();stopped.Slot();stopped.Collect();object renegotiated=null;stopped.OnRelease=()=>renegotiated=Call(stopped.Session,"DisplaySourceService","local-mod-native-consumers",new object[]{1,stopped.Plugin});Call(stopped.Session,"ModClientStopSourceProviders");Check(renegotiated is bool no&&!no&&!(bool)Call(stopped.Session,"ModClientNativeAvailable"),"teardown release callback cannot renegotiate stopped source authority");
 }
 static void HudComposition()
 {
  using var f=new Fixture();f.Handle=(long)f.OwnerEndpoint("create-hud",new object[]{0});f.Context=f.Owner.Contexts[f.Handle];f.Op("context-anchor",f.Anchor);
  f.Op("context-slot","hudvideo","lcd-texture","55:0",new Vector4(-100,80,800,400),new Vector4(0,100,400,200),new Vector4(.1f,.2f,.5f,.6f),2,.75d);
  f.Op("mesh","under",new[]{new Vector3D(0,0,0),new Vector3D(300,0,0),new Vector3D(0,200,0)},new[]{0,1,2},Vector4.One);f.Op("item-order","under",2);
  f.Op("mesh","over",new[]{new Vector3D(0,0,0),new Vector3D(300,0,0),new Vector3D(0,200,0)},new[]{0,1,2},Vector4.One);f.Op("item-order","over",3);
  var items=f.Collect();Check(items.Select(i=>i.Id).SequenceEqual(new[]{"under","hudvideo","over"}),"HUD source interleaves with ordinary pixel artwork");var source=items.Single(i=>i.Id=="hudvideo");
  Check(f.Context.Surface==null&&source.Geometry.Points.All(p=>p.X>=0&&p.X<=400&&p.Y>=100&&p.Y<=300),"HUD source uses clipped top-left pixel geometry with no world surface");Check(source.UV.Where((u,i)=>source.Geometry.Points[i].Y<101).All(u=>u.Y<.5),"HUD texture top edge preserves source UV orientation");
  var descriptor=f.Resolve(f.LastBinding);var demand=(MyTuple<int,int,double,bool>)descriptor[7];Check(demand.Item4&&demand.Item1>=400&&demand.Item2>=200,"HUD demand uses actual visible pixels and UV density");
  var projection=MatrixD.CreatePerspectiveFieldOfView(1.1,f.Viewport.X/f.Viewport.Y,.1,1000);object[] args={f.Context,source,f.Viewport,MatrixD.Invert(projection),MatrixD.Identity,Vector3D.Zero,8192};Call(f.Session,"DrawModClientItem",args);
  Check(f.Submitted.Count>0&&f.Submitted.All(t=>t.Hud&&t.Material=="local_test_texture")&&(int)args[6]==8192-f.Submitted.Count,"HUD source uses existing pixel unprojection and shared triangle billing");
  int releases=f.Releases,frames=f.Frames;var binding=f.LastBinding;f.Op("context-visible",false);Check(f.Releases==releases+1&&f.Resolve(binding)==null&&f.Collect().Count==2&&f.Frames==frames,"HUD hide retires source and acquires no new frame");f.Op("context-visible",true);f.Evidence=new object();f.Collect();binding=f.LastBinding;f.OwnerEndpoint("release",new object[0]);Check(f.Resolve(binding)==null,"HUD owner retirement revokes physical source authority");
 }
 static void ArtworkGrowthBudget()
 {
  foreach(bool hud in new[]{false,true})
  {
   using var f=new Fixture();string kind=hud?"HUD":"world";
   f.OwnerEndpoint("geometry-limit",new object[]{8192,8192});
   if(hud){f.Handle=(long)f.OwnerEndpoint("create-hud",new object[]{0});f.Context=f.Owner.Contexts[f.Handle];f.Op("context-anchor",f.Anchor);f.Op("context-slot","video","lcd-texture","55:0",new Vector4(100,100,200,100),new Vector4(100,100,200,100),new Vector4(0,0,1,1),2,1d);}else f.Slot();
   var source=f.Collect().Single();Check((bool)Call(f.Session,"ModClientSourceItemCurrent",f.Owner,f.Context,source),kind+" source is valid before artwork growth");
   var points=hud?new[]{new Vector3D(10,10,0),new Vector3D(30,10,0),new Vector3D(10,30,0)}:new[]{new Vector3D(-.2,-.2,0),new Vector3D(.2,-.2,0),new Vector3D(-.2,.2,0)};
   var triangles=new int[Geometry.MaxPrimitives*3];for(int i=0;i<triangles.Length;i++)triangles[i]=i%3;
   f.Op("mesh","first",points,triangles,Vector4.One);Check((bool)Call(f.Session,"ModClientSourceItemCurrent",f.Owner,f.Context,source),kind+" cached source remains live while combined primitives fit");
   f.Op("mesh","second",points,triangles,Vector4.One);int releases=f.Releases;Call(f.Session,"ModClientMaintainSourceSlots");
   Check(f.Releases==releases+1&&!(bool)Call(f.Session,"ModClientSourceItemCurrent",f.Owner,f.Context,source),kind+" later artwork filling owner grant retires cached source before use");
   Check(!(bool)Call(f.Session,"ModClientSourceItemCurrent",f.Owner,f.Context,source)&&f.Releases==releases+1,kind+" retired over-budget source lease cannot be released twice");f.Evidence=new object();
   Check(f.Collect().All(i=>!i.SourceSlotItem)&&f.Releases==releases+2,kind+" replacement source offer cannot revive an over-budget cached frame");
   f.Op("remove","second");f.Evidence=new object();Check(f.Collect().Any(i=>i.SourceSlotItem),kind+" source can reacquire after ordinary artwork releases its grant");
  }
 }
 static void ConfigurableGeometryAllowance()
 {
  foreach(bool hud in new[]{false,true})
  {
   using var f=new Fixture();string kind=hud?"HUD":"world";var initial=(MyTuple<int,int>)f.OwnerEndpoint("geometry-limit-settings",new object[0]);Check(initial.Item1==0&&initial.Item2==0,kind+" aggregate geometry is unlimited by default");
   if(hud){f.Handle=(long)f.OwnerEndpoint("create-hud",new object[]{0});f.Context=f.Owner.Contexts[f.Handle];f.Op("context-anchor",f.Anchor);f.Op("context-slot","video","lcd-texture","55:0",new Vector4(100,100,200,100),new Vector4(100,100,200,100),new Vector4(0,0,1,1),2,1d);}else f.Slot();
   var source=f.Collect().Single();var points=hud?new[]{new Vector3D(10,10,0),new Vector3D(30,10,0),new Vector3D(10,30,0)}:new[]{new Vector3D(-.2,-.2,0),new Vector3D(.2,-.2,0),new Vector3D(-.2,.2,0)};
   var triangles=new int[Geometry.MaxPrimitives*3];for(int i=0;i<triangles.Length;i++)triangles[i]=i%3;for(int i=0;i<3;i++)f.Op("mesh","large"+i,points,triangles,Vector4.One);
   Check((bool)Call(f.Session,"ModClientSourceItemCurrent",f.Owner,f.Context,source)&&f.Context.Items.Values.Sum(i=>i.Geometry.Triangles.Length/3)>8192,kind+" default accepts aggregate artwork and source beyond the former8192 cap");
   Reject(()=>f.OwnerEndpoint("geometry-limit",new object[]{8192,8192}),kind+" lowering below retained ordinary artwork rejects atomically");var unchanged=(MyTuple<int,int>)f.OwnerEndpoint("geometry-limit-settings",new object[0]);Check(unchanged.Item1==0&&unchanged.Item2==0&&(bool)Call(f.Session,"ModClientSourceItemCurrent",f.Owner,f.Context,source)&&f.Releases==0,kind+" rejected finite change preserves allowance and current source lease");
   f.Op("remove","large2");f.Op("remove","large1");int releases=f.Releases;f.OwnerEndpoint("geometry-limit",new object[]{8192,4096});var finite=(MyTuple<int,int>)f.OwnerEndpoint("geometry-limit-settings",new object[0]);
   Check(finite.Item1==8192&&finite.Item2==4096&&f.Releases==releases+1&&!(bool)Call(f.Session,"ModClientSourceItemCurrent",f.Owner,f.Context,source),kind+" finite limit fitting ordinary artwork retires only the excess cached source");
   f.OwnerEndpoint("geometry-limit",new object[]{0,0});f.Evidence=new object();Check(f.Collect().Any(i=>i.SourceSlotItem),kind+" clearing finite aggregate limits restores source composition");
   f.OwnerEndpoint("geometry-limit",new object[]{3,0});f.Evidence=new object();var pointsOnly=(MyTuple<int,int>)f.OwnerEndpoint("geometry-limit-settings",new object[0]);Check(pointsOnly.Item1==3&&pointsOnly.Item2==0&&!f.Collect().Any(i=>i.SourceSlotItem),kind+" zero primitive sentinel stays unlimited while finite point grant retires excess source");
  }
  using var retained=new Fixture();retained.Slot();var retainedSource=retained.Collect().Single();var retainedPoints=new Vector3D[Geometry.MaxPoints];for(int i=0;i<retainedPoints.Length;i++)retainedPoints[i]=new Vector3D(i*.00001,0,0);retainedPoints[1]=Vector3D.UnitX;retainedPoints[2]=Vector3D.UnitY;retained.Op("mesh","retained",retainedPoints,new[]{0,1,2},Vector4.One);retained.OwnerEndpoint("geometry-limit",new object[]{Geometry.MaxPoints,0});Check(!(bool)Call(retained.Session,"ModClientSourceItemCurrent",retained.Owner,retained.Context,retainedSource)&&retained.Releases==1,"finite retained-point allowance includes source geometry even when most ordinary points clip out of mapped artwork");
 }
}
