using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

public interface UiTestCharacter : IMyCharacter, VRage.Game.ModAPI.Interfaces.IMyControllableEntity { }

internal static class UiNetworkTests
{
 public static int Run()
 {
  // Metadata admission uses the same real protobuf serializer as publication.
  // This scope restores Utilities (and Multiplayer) after the narrower peer fixture.
  using var serialization=new ClientReplicationTests.GatewayScope();
  int checks=0;void Check(bool good,string label){if(!good)throw new Exception("UI network: "+label);checks++;}
  object Call(HoloMapSession s,string n,params object[] a)=>ClientReplicationTests.Call(s,n,a);
  var session=new HoloMapSession();
  var press=new UiPress{CallerId=10,TargetId=20,WidgetId="run",Revision=1,Sequence=1};
  // ValidUiPress is static; use reflection independently of instance test helper.
  bool Valid(UiPress p)=>(bool)typeof(HoloMapSession).GetMethod("ValidUiPress",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{p});
  Check(!Valid(null),"null press rejected");
  Check(Valid(press),"valid bounded request");press.Sequence=0;Check(!Valid(press),"zero sequence rejected");press.Sequence=1;
  press.WidgetId=new string('a',41);Check(!Valid(press),"oversized widget ID rejected");press.WidgetId="run";
  Check(!(bool)Call(session,"ReserveUiRequest",0UL),"zero sender rejected before decode");
  for(int i=0;i<5;i++)Check((bool)Call(session,"ReserveUiRequest",1UL),"five presses per peer permitted");
  Check(!(bool)Call(session,"ReserveUiRequest",1UL),"sixth press throttled");
  for(ulong peer=2;peer<=4;peer++)for(int i=0;i<5;i++)Check((bool)Call(session,"ReserveUiRequest",peer),"global budget slot permitted");
  Check(!(bool)Call(session,"ReserveUiRequest",5UL),"global twenty-per-second cap");
  ClientReplicationTests.SetField(session,"_ticks",60);
  Check((bool)Call(session,"ReserveUiRequest",1UL),"rate window resets");

  var gateway=typeof(MyAPIGateway);var names=new[]{"Multiplayer","Entities","Players","Physics"};
  var members=names.Select(n=>(MemberInfo)gateway.GetField(n)??gateway.GetProperty(n)).ToArray();
  object Read(MemberInfo m)=>m is FieldInfo f?f.GetValue(null):((PropertyInfo)m).GetValue(null);
  void Write(MemberInfo m,object v){if(m is FieldInfo f)f.SetValue(null,v);else ((PropertyInfo)m).SetValue(null,v);}
  Type Kind(MemberInfo m)=>m is FieldInfo f?f.FieldType:((PropertyInfo)m).PropertyType;
  var prior=members.Select(Read).ToArray();
  bool access=true,dead=false,registeredCharacter=true,connected=true,powered=true;int runs=0;var position=Vector3D.Zero;var lastArgument="";var headDirection=Vector3D.Forward;
  var registered=new Dictionary<long,object>();
  var caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>{
   if(m.Name=="get_EntityId")return 10L;if(m.Name=="get_OwnerId")return 77L;if(m.Name=="get_Closed")return false;
   if(m.Name=="IsSameConstructAs")return true;if(m.Name=="HasPlayerAccess")return access;
   if(m.Name=="TryRun"){runs++;lastArgument=(string)a[0];return true;}throw new Exception(m.Name);});
  var target=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>{
   if(m.Name=="get_EntityId")return 20L;if(m.Name=="get_Closed")return false;if(m.Name=="get_IsWorking")return powered;if(m.Name=="HasPlayerAccess")return access;
   if(m.Name=="GetPosition")return Vector3D.Zero;if(m.Name=="get_WorldMatrix")return MatrixD.Identity;
   if(m.Name=="get_BlockDefinition")return Activator.CreateInstance(m.ReturnType);throw new Exception(m.Name);});
  var character=DrawTestProxy.Make(typeof(UiTestCharacter),(m,a)=>{
   if(m.Name=="get_EntityId")return 30L;if(m.Name=="get_Closed")return false;if(m.Name=="get_IsDead")return dead;
   if(m.Name=="GetPosition")return position;if(m.Name=="GetHeadMatrix")return MatrixD.CreateWorld(new Vector3D(0,.8,1),headDirection,Vector3D.Up);throw new Exception(m.Name);});
  bool onFoot=true;
  var controller=DrawTestProxy.Make(typeof(IMyEntityController),(m,a)=>{if(m.Name=="get_ControlledEntity")return onFoot?character:null;throw new Exception(m.Name);});
  var player=DrawTestProxy.Make(typeof(IMyPlayer),(m,a)=>{
   if(m.Name=="get_SteamUserId")return 123UL;if(m.Name=="get_IdentityId")return 77L;if(m.Name=="get_Character")return character;
   if(m.Name=="get_Controller")return controller;
   throw new Exception(m.Name);});
  registered.Add(10,caller);registered.Add(20,target);registered.Add(30,character);
  try
  {
   Write(members[0],DrawTestProxy.Make(Kind(members[0]),(m,a)=>{if(m.Name=="get_IsServer")return true;throw new Exception(m.Name);}));
   Write(members[1],DrawTestProxy.Make(Kind(members[1]),(m,a)=>{if(m.Name=="GetEntityById"){long id=(long)a[0];return id==30&&!registeredCharacter?null:registered.GetValueOrDefault(id);}throw new Exception(m.Name);}));
   Write(members[2],DrawTestProxy.Make(Kind(members[2]),(m,a)=>{if(m.Name=="GetPlayers"){if(connected&&((Func<IMyPlayer,bool>)a[1])((IMyPlayer)player))((List<IMyPlayer>)a[0]).Add((IMyPlayer)player);return null;}throw new Exception(m.Name);}));
   Write(members[3],null);
   session=new HoloMapSession();Call(session,"ReserveUiRequest",123UL);
   int Queued()=>((System.Collections.ICollection)ClientReplicationTests.Field(session,"_uiRequests")).Count;
   Call(session,"ReceiveUiMessage",(ushort)49784,new byte[]{1},123UL,true);Check(Queued()==0,"server-origin action requests rejected");
   Call(session,"ReceiveUiMessage",(ushort)49784,new byte[1025],123UL,false);Check(Queued()==0,"oversized packet rejected before deserialize");
   Call(session,"ReceiveUiMessage",(ushort)49783,new byte[]{1},123UL,false);Check(Queued()==0,"wrong channel rejected");
   UiAck Process()=> (UiAck)Call(session,"ProcessUiPress",123UL,press);
   Check(!Process().Accepted,"unregistered canonical widget rejected");
   Check(!Process().Accepted,"replayed sequence rejected");
   press.Sequence++;connected=false;Check(!Process().Accepted,"disconnected sender rejected");connected=true;
   press.Sequence++;registeredCharacter=false;Check(!Process().Accepted,"unregistered character rejected");registeredCharacter=true;
   press.Sequence++;dead=true;Check(!Process().Accepted,"dead character rejected");dead=false;
   press.Sequence++;access=false;Check(!Process().Accepted,"changed ownership/access rejected");access=true;
   press.Sequence++;position=new Vector3D(6,0,0);Check(!Process().Accepted,"distant character rejected");position=Vector3D.Zero;
   press.Sequence++;registered.Remove(10);Check(!Process().Accepted,"deleted PB rejected");
   Check(runs==0,"hostile requests never execute PB");
   Check(!(bool)Call(session,"UiUnoccluded",30L,target,Vector3D.Zero,Vector3D.UnitZ),"missing physics fails closed");
   registered.Add(10,caller);
   Write(members[3],DrawTestProxy.Make(Kind(members[3]),(m,a)=>{if(m.Name=="CastRay")return null;throw new Exception(m.Name);}));
   Call(session,"GetScene",20L);
   var display=new UiDisplay{CallerId=10,TargetId=20,Revision=1};display.Bundles.Add(new UiBundle{Id="main"});
   var widget=new UiWidget{Id="run",Bundle="main",X=0,Y=0,Width=.5,Height=.5,ActionKind="pb",Argument="fixed-command"};display.Widgets.Add(widget);
   Call(session,"ApplyUiDisplays",new List<UiDisplay>{display});
   Check((bool)Call(session,"HasVisibleUiWidget",display,widget,false),"canonical visible widget available");
   var hitArgs=new object[]{display,widget,new Vector3D(0,.8,1),Vector3D.Forward,Vector3D.Zero,0L};
   var hitMethod=typeof(HoloMapSession).GetMethods(BindingFlags.NonPublic|BindingFlags.Instance).Single(m=>m.Name=="TryUiWidgetHit"&&m.GetParameters().Length==6);
   Check((bool)hitMethod.Invoke(session,hitArgs),"canonical head ray hits registered widget");
   press.Sequence++;var accepted=Process();Check(accepted.Accepted,"head-ray validated nearby button accepted");
   Check(runs==1&&lastArgument=="fixed-command","server runs only canonical PB argument");
   onFoot=false;press.Sequence++;Check(!Process().Accepted,"remote/cockpit-controlled requests rejected");onFoot=true;
   Check(!Process().Accepted&&runs==1,"accepted press cannot be replayed");
   press.Sequence++;press.Revision=2;Check(!Process().Accepted,"stale or invented revision rejected");press.Revision=1;
   widget.Visible=false;Call(session,"ApplyUiDisplays",new List<UiDisplay>{display});press.Sequence++;Check(!Process().Accepted,"disabled canonical widget rejected");widget.Visible=true;
   display.Bundles[0].Visible=false;Call(session,"ApplyUiDisplays",new List<UiDisplay>{display});press.Sequence++;Check(!Process().Accepted,"unopened hidden bundle rejected");
   Check(runs==1,"rejected widgets never invoke PB");
   display.Bundles[0].Visible=true;
   var obstruction=DrawTestProxy.Make(typeof(IHitInfo),(m,a)=>{if(m.Name=="get_HitEntity")return target;if(m.Name=="get_Position")return new Vector3D(0,.8,.5);throw new Exception(m.Name);});
   Write(members[3],DrawTestProxy.Make(Kind(members[3]),(m,a)=>{if(m.Name=="CastRay"){((List<IHitInfo>)a[2]).Add((IHitInfo)obstruction);return null;}throw new Exception(m.Name);}));
   Call(session,"ApplyUiDisplays",new List<UiDisplay>{display});press.Sequence++;Check(!Process().Accepted&&runs==1,"physical wall rejects otherwise valid button");
   Write(members[3],DrawTestProxy.Make(Kind(members[3]),(m,a)=>{if(m.Name=="CastRay")return null;throw new Exception(m.Name);}));
   display.Revision=3;display.Bundles.Add(new UiBundle{Id="options",Visible=false});
   widget.ActionKind="menu";widget.Argument="options";
   display.Widgets.Add(new UiWidget{Id="option",Bundle="options",Width=.5,Height=.5,ActionKind="pb",Argument="option-command"});
   Call(session,"ApplyUiDisplays",new List<UiDisplay>{display});press.Revision=3;press.Sequence++;
   Check(Process().Accepted,"canonical menu click grants private bundle");
   press.WidgetId="option";press.Sequence++;Check(Process().Accepted&&runs==2&&lastArgument=="option-command","approved hidden submenu action permitted");
   var close=new UiPress{Action=1,Sequence=++press.Sequence,WidgetId=""};Call(session,"ProcessUiPress",123UL,close);
   press.Sequence++;Check(!Process().Accepted,"close revokes submenu grant");
   press.WidgetId="run";press.Sequence++;Check(Process().Accepted,"menu can reopen after close");
   press.WidgetId="option";ClientReplicationTests.SetField(session,"_ticks",1801);press.Sequence++;Check(!Process().Accepted,"expired submenu grant rejected");
   Check(runs==2,"invalid submenu requests never execute PB");
   Check(!((UiAck)Call(session,"ProcessUiPress",999UL,press)).Accepted,"untracked secure sender cannot execute registered action");
   ClientReplicationTests.SetField(session,"_ticks",0);press.Action=2;press.Sequence++;
   Check(!Process().Accepted,"ordinary menu grant cannot authorize focused mouse selection");
   display.Revision=4;widget.ActionKind="focus";display.Widgets[1].X=4;
   Call(session,"ApplyUiDisplays",new List<UiDisplay>{display});press.Revision=4;press.Action=2;press.Sequence++;
   Check(!Process().Accepted,"focus selection requires an existing exact revision grant");
   press.Action=0;press.WidgetId="run";press.Sequence++;var focus=Process();
   Check(focus.Accepted&&focus.SourceTileId==20,"physical focus click grants actual tile lease");
   press.Action=2;press.WidgetId="option";press.Sequence++;
   Check(Process().Accepted&&runs==3,"focus mouse selection uses canonical widget without requiring crosshair on it");
   Check(!Process().Accepted&&runs==3,"focused mouse replay cannot execute twice");
   press.WidgetId="run";press.Sequence++;Check(!Process().Accepted,"focused selection cannot reach another bundle");press.WidgetId="option";
   powered=false;press.Sequence++;Check(!Process().Accepted,"unpowered focus anchor rejects actions");powered=true;
   position=new Vector3D(6,0,0);press.Sequence++;Check(!Process().Accepted,"focused mouse cannot operate from far away");position=Vector3D.Zero;
   headDirection=Vector3D.Backward;press.Sequence++;Check(!Process().Accepted,"looking away invalidates focus context");headDirection=Vector3D.Forward;
   access=false;press.Sequence++;Check(!Process().Accepted,"focus permission changes reject actions");access=true;
   Call(session,"SetLayerVisible",caller,target,"ui-main",false);press.Sequence++;Check(!Process().Accepted,"source UI layer off rejects focus actions");Call(session,"SetLayerVisible",caller,target,"ui-main",true);
   var scene=Call(session,"GetScene",20L);scene.GetType().GetField("Scale").SetValue(scene,2d);press.Sequence++;Check(!Process().Accepted,"source view changes invalidate focus grant");scene.GetType().GetField("Scale").SetValue(scene,1d);
   Write(members[3],DrawTestProxy.Make(Kind(members[3]),(m,a)=>{if(m.Name=="CastRay"){((List<IHitInfo>)a[2]).Add((IHitInfo)obstruction);return null;}throw new Exception(m.Name);}));
   press.Sequence++;Check(!Process().Accepted,"physical entry point occlusion rejects focused UI actions");
   Write(members[3],DrawTestProxy.Make(Kind(members[3]),(m,a)=>{if(m.Name=="CastRay")return null;throw new Exception(m.Name);}));
   close.Sequence=++press.Sequence;Call(session,"ProcessUiPress",123UL,close);press.Sequence++;Check(!Process().Accepted,"closing revokes focused mouse lease");
   Check(runs==3,"all hostile focus cases leave PB execution count unchanged");
  }
  finally{for(int i=0;i<members.Length;i++)Write(members[i],prior[i]);}
  return checks;
 }
}


