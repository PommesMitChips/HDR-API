using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using HoloMap;
using Sandbox.ModAPI;

internal static class UiApiTests
{
    public static int Run()
    {
        // UI declaration byte admission is part of the real endpoint contract.
        using var serialization=new ClientReplicationTests.GatewayScope();
        int count=0;
        void Check(bool value,string name){if(!value)throw new Exception("UI: "+name);count++;}
        void Reject(Action action,string name){try{action();}catch(ArgumentException){count++;return;}throw new Exception("Expected UI rejection: "+name);}
        var d=new UiDisplay{CallerId=1,TargetId=2,Revision=1};
        d.Bundles.Add(new UiBundle{Id="main"});d.Bundles.Add(new UiBundle{Id="context",Visible=false});
        var w=new UiWidget{Id="start",Bundle="main",X=0,Y=0,Width=2,Height=1,ActionKind="pb",Argument="start",Label="Start"};d.Widgets.Add(w);
        UiRules.Display(d);count++;
        Check(UiRules.Contains(w,1,.5),"rectangle boundary is included");Check(!UiRules.Contains(w,1.001,0),"outside rejected");Check(!UiRules.Contains(w,double.NaN,0),"NaN hit rejected");
        Check(ReferenceEquals(UiRules.Hit(d,0,0),w),"visible hit");w.Visible=false;Check(UiRules.Hit(d,0,0)==null,"hidden widget excluded");w.Visible=true;
        d.Bundles[0].Visible=false;Check(UiRules.Hit(d,0,0)==null,"hidden bundle excluded");d.Bundles[0].Visible=true;
        var over=new UiWidget{Id="over",Bundle="main",Width=2,Height=1,ActionKind="menu",Argument="context"};d.Widgets.Add(over);Check(ReferenceEquals(UiRules.Hit(d,0,0),over),"last declaration wins overlap");
        var copy=UiRules.Copy(d);copy.Widgets[0].Argument="changed";copy.Bundles[0].Visible=false;Check(d.Widgets[0].Argument=="start"&&d.Bundles[0].Visible,"copies are detached");
        var hiddenCopy=UiRules.Copy(d);hiddenCopy.Bundles[0].Visible=false;hiddenCopy.Widgets[0].Visible=false;var model=ProtoBuf.Meta.RuntimeTypeModel.Create();using(var stream=new MemoryStream()){model.Serialize(stream,hiddenCopy);stream.Position=0;var roundtrip=(UiDisplay)model.Deserialize(stream,null,typeof(UiDisplay));Check(!roundtrip.Bundles[0].Visible&&!roundtrip.Widgets[0].Visible,"false visibility survives actual protobuf roundtrip");Check(roundtrip.Bundles[1].Visible==false,"hidden menu remains hidden");}
        foreach(string kind in new[]{"pb","toggle","menu","focus"}){UiRules.Action(kind,kind=="pb"?new string('a',256):"context");count++;}
        Reject(()=>UiRules.Action("script","alert(1)"),"executable action");Reject(()=>UiRules.Action("PB","run"),"noncanonical action");Reject(()=>UiRules.Action("pb",new string('a',257)),"oversize PB argument");Reject(()=>UiRules.Action("pb",null),"null argument");
        Reject(()=>UiRules.Id("Main"),"uppercase identifier");Reject(()=>UiRules.Id("a/b"),"path identifier");Reject(()=>UiRules.Id(new string('a',25)),"long identifier");
        var bad=UiRules.Copy(d);bad.Widgets[0].Width=double.PositiveInfinity;Reject(()=>UiRules.Display(bad),"infinite bounds");bad.Widgets[0].Width=0;Reject(()=>UiRules.Display(bad),"zero bounds");bad.Widgets[0].Width=2;bad.Widgets[0].X=25;Reject(()=>UiRules.Display(bad),"out of plane bounds");
        bad=UiRules.Copy(d);bad.Widgets[0].Label=new string('a',65);Reject(()=>UiRules.Display(bad),"long label");
        bad=UiRules.Copy(d);bad.Widgets[0].Bundle="absent";Reject(()=>UiRules.Display(bad),"missing bundle");
        bad=UiRules.Copy(d);bad.Widgets[0].Id="main";Reject(()=>UiRules.Display(bad),"crosscategory ID collision");
        bad=UiRules.Copy(d);bad.Widgets.Add(UiRules.Copy(d).Widgets[0]);Reject(()=>UiRules.Display(bad),"duplicate widget");
        bad=UiRules.Copy(d);bad.Widgets[1].Argument="absent";Reject(()=>UiRules.Display(bad),"missing menu target");
        bad=UiRules.Copy(d);bad.Revision=0;Reject(()=>UiRules.Display(bad),"zero revision");
        bad=UiRules.Copy(d);bad.CallerId=0;Reject(()=>UiRules.Display(bad),"missing caller");
        bad=UiRules.Copy(d);for(int i=0;i<7;i++)bad.Bundles.Add(new UiBundle{Id="b"+i});Reject(()=>UiRules.Display(bad),"bundle count bound");
        bad=UiRules.Copy(d);for(int i=0;i<63;i++)bad.Widgets.Add(new UiWidget{Id="w"+i,Bundle="main",Width=1,Height=1,ActionKind="pb",Argument=""});Reject(()=>UiRules.Display(bad),"widget count bound");
        count+=Endpoints();return count;
    }
    static int Endpoints()
    {
        int count=0;void Check(bool condition,string name){if(!condition)throw new Exception("UI endpoint: "+name);count++;}
        void Reject(Action action,string name){try{action();}catch(ArgumentException){count++;return;}throw new Exception("Expected UI endpoint rejection: "+name);}
        var gateway=typeof(MyAPIGateway);MemberInfo multi=(MemberInfo)gateway.GetField("Multiplayer")??gateway.GetProperty("Multiplayer"),entities=(MemberInfo)gateway.GetField("Entities")??gateway.GetProperty("Entities");
        object Read(MemberInfo member)=>member is FieldInfo f?f.GetValue(null):((PropertyInfo)member).GetValue(null);
        void Write(MemberInfo member,object value){if(member is FieldInfo f)f.SetValue(null,value);else ((PropertyInfo)member).SetValue(null,value);}
        Type Kind(MemberInfo member)=>member is FieldInfo f?f.FieldType:((PropertyInfo)member).PropertyType;
        object oldMulti=Read(multi),oldEntities=Read(entities);bool access=true,server=true;var registered=new Dictionary<long,object>();
        object caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>{if(m.Name=="get_EntityId")return 710L;if(m.Name=="get_OwnerId")return 77L;if(m.Name=="get_Closed")return false;if(m.Name=="IsSameConstructAs")return true;throw new Exception(m.Name);});
        object target=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>{if(m.Name=="get_EntityId")return 720L;if(m.Name=="get_Closed")return false;if(m.Name=="HasPlayerAccess")return access;throw new Exception(m.Name);});registered.Add(710,caller);registered.Add(720,target);
        try
        {
            Write(multi,DrawTestProxy.Make(Kind(multi),(m,a)=>{if(m.Name=="get_IsServer")return server;throw new Exception(m.Name);}));
            Write(entities,DrawTestProxy.Make(Kind(entities),(m,a)=>{if(m.Name=="GetEntityById"){object value;return registered.TryGetValue((long)a[0],out value)?value:null;}throw new Exception(m.Name);}));
            var session=new HoloMapSession();var endpoint=typeof(HoloMapSession).GetMethod("UiEndpoint",BindingFlags.Instance|BindingFlags.NonPublic);var ui=(Func<string,object[],object>)endpoint.Invoke(session,new[]{caller});
            Check((string)ui("version",new object[0])=="HDR.UI/1","version");Reject(()=>ui("bundle",new object[]{"main"}),"target required");ui("target",new[]{target});ui("bundle",new object[]{"main"});ui("bundle",new object[]{"menu",false});
            ui("button",new object[]{"go","main",0,0,1,.5,"Go","pb","start"});
            var capture=typeof(HoloMapSession).GetMethod("CaptureUiDisplays",BindingFlags.Instance|BindingFlags.NonPublic);Func<List<UiDisplay>> state=()=>((List<UiDisplay>)capture.Invoke(session,null));
            var d=state()[0];Check(d.Widgets.Count==1&&d.Widgets[0].Argument=="start","registered action");long revision=d.Revision;
            ui("bind",new object[]{"go","menu","menu"});Check(state()[0].Revision>revision,"mutation advances revision");
            var get=typeof(HoloMapSession).GetMethod("GetUiWidget",BindingFlags.Instance|BindingFlags.NonPublic);Check(get.Invoke(session,new object[]{710L,720L,"go",true})!=null,"active lookup");
            ui("visible",new object[]{"main",false});Check(get.Invoke(session,new object[]{710L,720L,"go",true})==null,"hidden bundle lookup");
            var hidden=(UiWidget)get.Invoke(session,new object[]{710L,720L,"go",false});var visible=typeof(HoloMapSession).GetMethod("HasVisibleUiWidget",BindingFlags.Instance|BindingFlags.NonPublic);Check((bool)visible.Invoke(session,new object[]{state()[0],hidden,true}),"authorized local menu override");
            Reject(()=>ui("bind",new object[]{"go","script","bad"}),"unknown action");Reject(()=>ui("bind",new object[]{"go","pb",new string('x',257)}),"long PB action");Reject(()=>ui("button",new object[]{"bad","main",double.NaN,0,1,1,"Bad","pb",""}),"nonfinite input");
            access=false;Reject(()=>ui("bind",new object[]{"go","pb","bad"}),"lost target ownership");access=true;server=false;Reject(()=>ui("clear",new object[0]),"client mutation");server=true;registered.Remove(710);Reject(()=>ui("clear",new object[0]),"unregistered caller");registered.Add(710,caller);
            ui("clear",new object[0]);Check(state().Count==0,"clear removes metadata");
            ui("bundle",new object[]{"main"});for(int i=0;i<8;i++)ui("button",new object[]{"setting"+i,"main",0,-.7+i*.2,1.8,.18,"Open settings","pb","settings "+i});
            var scenes=(System.Collections.IDictionary)typeof(HoloMapSession).GetField("_scenes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);var scene=scenes[720L];var items=(System.Collections.IDictionary)scene.GetType().GetField("Items").GetValue(scene);var labels=(System.Collections.IDictionary)scene.GetType().GetField("Labels").GetValue(scene);
            Check(labels.Count==8,"eight realistic captions use bounded independent labels");string svg=null;foreach(var item in items.Values){var field=item.GetType().GetField("SvgSource");if(field.GetValue(item) is string text)svg=text;}
            var mesh=Svg.Parse(svg,4);Check(mesh.Geometry.Points.Length<=2048&&mesh.Geometry.Triangles.Length>0,"eight-button backgrounds compile within object budget");
            var snapshotBefore=state()[0];Reject(()=>ui("button",new object[]{"tiny","main",0,0,.01,.01,"Unreadable","pb",""}),"unreadable caption rejected");Check(state()[0].Revision==snapshotBefore.Revision&&labels.Count==8,"failed caption admission leaves canonical UI and visuals unchanged");
            ui("clear",new object[0]);Check(labels.Count==0,"clear removes owned captions");var layers=(System.Collections.IDictionary)scene.GetType().GetField("Layers").GetValue(scene);Check(layers.Count==0,"clear reclaims unused UI layers");
            ui("bundle",new object[]{"main"});ui("button",new object[]{"first","main",0,0,1,.2,"First","pb","first"});var beforePartial=state()[0];object originalItem=null;foreach(var item in items.Values)originalItem=item;
            scene.GetType().GetField("Scale").SetValue(scene,100d);
            Reject(()=>ui("button",new object[]{"second","main",1,0,1,.2,"Second","pb","second"}),"caption view radius failure after background replacement");object afterItem=null;foreach(var item in items.Values)afterItem=item;
            Check(ReferenceEquals(originalItem,afterItem)&&labels.Count==1&&state()[0].Revision==beforePartial.Revision&&state()[0].Widgets.Count==1,"mid-update failure restores background, captions and metadata");scene.GetType().GetField("Scale").SetValue(scene,1d);ui("clear",new object[0]);
        }
        finally{Write(multi,oldMulti);Write(entities,oldEntities);}return count;
    }
}
