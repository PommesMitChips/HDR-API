using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using ValueEvents = VRage.MyTuple<string,string,string,VRage.MyTuple<double,long,long>>[];

// Generic PB utilities and selective numeric cleanup; no HTML runtime or renderer is mocked.
internal static class HtmlPbCoreSeamTests
{
    static int checks;
    static void Check(bool condition,string label)
    {if(!condition)throw new Exception("PB HTML core seam: "+label);checks++;}
    static void Reject(Action action,string label)
    {try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Expected PB core rejection: "+label);}
    static object Invoke(object instance,string name,params object[] arguments)
    {
        var method=instance.GetType().GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)
            .Single(m=>m.Name==name&&m.GetParameters().Length==arguments.Length);
        try{return method.Invoke(instance,arguments);}
        catch(TargetInvocationException error){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static object Field(object instance,string name)
    {return instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(instance);}
    static void Set(object instance,string name,object value)
    {instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(instance,value);}
    sealed class Fixture:IDisposable
    {
        readonly ClientReplicationTests.GatewayScope gateway=new ClientReplicationTests.GatewayScope{Server=true,Dedicated=true};
        public readonly HoloMapSession Session=new HoloMapSession();
        public readonly Dictionary<long,object> Registry=new Dictionary<long,object>();
        public bool Access=true,Same=true,Closed;
        public int Runs;
        public bool AcceptWake;
        public readonly List<string> WakeArguments=new List<string>();
        public Fixture()
        {
            foreach(long caller in new[]{10L,11L})
            {
                long id=caller;
                Registry[id]=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>m.Name switch
                {"get_EntityId"=>id,"get_OwnerId"=>77L,"get_Closed"=>Closed,"get_IsWorking"=>true,"IsSameConstructAs"=>Same,"HasPlayerAccess"=>Access,
                    "TryRun"=>Wake((string)a[0]),_=>throw new Exception("PB seam caller: "+m.Name)});
            }
            Registry[20]=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>m.Name switch
            {"get_EntityId"=>20L,"get_Closed"=>false,"get_IsWorking"=>true,"HasPlayerAccess"=>Access,"get_WorldMatrix"=>MatrixD.Identity,"GetPosition"=>Vector3D.Zero,_=>throw new Exception("PB seam target: "+m.Name)});
            gateway.Install("Entities",(m,a)=>m.Name=="GetEntityById"?Registry.GetValueOrDefault((long)a[0]):throw new Exception("PB seam registry: "+m.Name));
        }
        bool Wake(string argument){Runs++;WakeArguments.Add(argument);return AcceptWake;}
        public Func<string,object[],object> Draw(bool select=true,long caller=10)
        {var endpoint=(Func<string,object[],object>)Invoke(Session,"DrawEndpoint",Registry[caller]);if(select)endpoint("target",new[]{Registry[20]});return endpoint;}
        public Func<string,object[],object> Ui(long caller=10)
        {var endpoint=(Func<string,object[],object>)Invoke(Session,"UiEndpoint",Registry[caller]);endpoint("target",new[]{Registry[20]});return endpoint;}
        public UiDisplay Display(long caller=10){return (UiDisplay)Invoke(Session,"GetUiDisplay",caller,20L);}
        public object Owner(long caller=10){return ((IDictionary)Field(Session,"_uiValueOwners"))[caller+":20"];}
        public IList Events(long caller=10){return (IList)Field(Owner(caller),"Events");}
        public HashSet<string> Pending{get{return (HashSet<string>)Field(Session,"_uiValueWakePending");}}
        public IDictionary Scenes{get{return (IDictionary)Field(Session,"_scenes");}}
        public IDictionary Items{get{return (IDictionary)Field(Scenes[20L],"Items");}}
        public byte[] Serialize(object value){return gateway.Serialize(value);}
        public void TickWakes(int tick){Set(Session,"_ticks",tick);Invoke(Session,"TickUiValueWakes");}
        public void Dispose(){gateway.Dispose();}
    }
    public static int Run()
    {checks=0;UtilityQueries();SelectivePolling();SelectiveRemoval();return checks;}
    static ValueEvents Poll(Func<string,object[],object> ui,params object[] filter)
    {return (ValueEvents)ui("poll-value-events",filter);}
    static MyTuple<double,long> Value(Func<string,object[],object> ui,string id)
    {return (MyTuple<double,long>)ui("get-value",new object[]{id});}
    static void Write(Func<string,object[],object> ui,string id,double value)
    {var result=(MyTuple<bool,double,long>)ui("set-value",new object[]{id,value});Check(result.Item1,"fixture source write admitted "+id);}
    static long Begin(Fixture f,string control)
    {
        var display=f.Display();var widget=display.Widgets.Single(w=>w.Id==control);var value=display.Values.Single(v=>v.Id==widget.Control.ValueId);
        var arguments=new object[]{10L,20L,control,77L,display.Revision,value.Revision,widget.Control.SourceRevision,0L};
        Check((bool)Invoke(f.Session,"UiTryBeginValueLeaseScalar",arguments),"actual scalar lease admitted "+control);return (long)arguments[7];
    }
    static void Attach(Fixture f,Func<string,object[],object> ui,string control,string value,string artwork)
    {
        f.Draw()("line",new object[]{artwork,Vector3D.Zero,Vector3D.UnitX,"cyan"});
        ui("control",new object[]{control,"shared",artwork,.5,0d,1d,.25});ui("bind-value",new object[]{control,value});
        ui("constraint",new object[]{control,"line",Vector3D.Zero,Vector3D.UnitX});ui("draggable",new object[]{control,true});
    }
    static void UtilityQueries()
    {
        using var f=new Fixture();var draw=f.Draw(false);Set(f.Session,"_dirty",false);
        var context=((IDictionary)Field(f.Session,"_drawContexts"))[10L];
        var cost=(MyTuple<int,int>)draw("geometry-cost",new object[]{"text","Hello",2d});var geometry=VectorFont.Text("Hello",2,"start");
        Check(cost.Item1==geometry.Points.Length&&cost.Item2==geometry.Triangles.Length/3,"PB cost compiles exact text geometry");
        Check(f.Scenes.Count==0&&Field(context,"Target")==null&&!((bool)Field(f.Session,"_dirty")),"PB cost before selection creates no scene or target claim");
        const string svg="<svg viewBox='0 0 2 1'><rect width='2' height='1' fill='#fff'/></svg>";
        cost=(MyTuple<int,int>)draw("geometry-cost",new object[]{"svg",svg,1d,8});geometry=Svg.Parse(svg,8).Geometry;
        Check(cost.Item1==geometry.Points.Length&&cost.Item2==geometry.Triangles.Length/3,"PB cost shares bounded SVG compiler");
        Set(context,"ScreenId","retired-child");var second=(MyTuple<int,int>)draw("geometry-cost",new object[]{"svg",svg,1d,8});
        Check(second.Equals(cost)&&Field(context,"ScreenId").Equals("retired-child")&&Field(context,"Target")==null&&f.Scenes.Count==0,"retired selected child query bypasses lookup without changing selection");
        foreach(object[] invalid in new[]{new object[]{"html",svg},new object[]{"text",new string('H',65)},new object[]{"svg",svg,1d,33},new object[]{"text","H",double.NaN},new object[]{"text","H",1d,12,1},new object[]{1,"H"}})
            Reject(()=>draw("geometry-cost",invalid),"strict cost validation before mutation");
        Check(!((bool)Field(f.Session,"_dirty"))&&f.Scenes.Count==0,"rejected cost queries do not publish");
        using(GeometryWork.Begin(1))Reject(()=>draw("geometry-cost",new object[]{"text","H"}),"PB cost obeys existing compiler allowance");
        f.Registry.Remove(10);Reject(()=>draw("geometry-cost",new object[]{"text","H"}),"utility rejects retired caller");
        // A real selected child screen remains selected, with retained artwork/UI unchanged.
        using var live=new Fixture();var h=live.Draw();h("screen",new object[]{"body",MatrixD.Identity,2d,1d,2d,1d});
        h("svg",new object[]{"one",svg,MatrixD.Identity,8});var liveContext=((IDictionary)Field(live.Session,"_drawContexts"))[10L];var selected=Field(liveContext,"ScreenId");
        var count=live.Items.Count;Set(live.Session,"_dirty",false);h("geometry-cost",new object[]{"text","A"});
        Check(Field(liveContext,"ScreenId").Equals(selected)&&live.Items.Count==count&&!((bool)Field(live.Session,"_dirty")),"live selected child cost preserves geometry and dirty state");
        live.Closed=true;Reject(()=>h("geometry-cost",new object[]{"text","H"}),"selected-child utility retains registered caller check");
    }
    static void SelectivePolling()
    {
        using var f=new Fixture();var ui=f.Ui();ui("bundle",new object[]{"shared"});
        foreach(string id in new[]{"html-gain","other-gain","plain"})ui("value",new object[]{id,0d,0d,10d,1d});
        Attach(f,ui,"html-handle","plain","handle");ui("value-notify",new object[]{"fixed-ui-wake"});
        Write(ui,"other-gain",1);Write(ui,"html-gain",2);long lease=Begin(f,"html-handle");
        var before=f.Serialize(f.Display());var eventReferences=f.Events().Cast<object>().ToArray();
        Check(eventReferences.Length==3&&f.Pending.Contains("10:20"),"mixed source/control events and wake fixture");
        foreach(object[] invalid in new[]{new object[]{"UPPER"},new object[]{"with space"},new object[]{new string('a',25)},new object[]{null},new object[]{1},new object[]{"html-",1}})
        {Reject(()=>Poll(ui,invalid),"invalid prefix/arity");Check(f.Events().Cast<object>().SequenceEqual(eventReferences)&&f.Pending.Contains("10:20")&&f.Serialize(f.Display()).SequenceEqual(before),"invalid filter preserves exact queue, wake and declarations");}
        Set(f.Session,"_dirty",false);var selected=Poll(ui,"html-");
        Check(selected.Length==2&&selected[0].Item3=="html-gain"&&selected[0].Item2==""&&selected[1].Item2=="html-handle"&&selected[1].Item3=="plain","prefix matches source ValueId or Control id");
        Check(f.Events().Count==1&&ReferenceEquals(f.Events()[0],eventReferences[0])&&f.Pending.Contains("10:20"),"matching drain retains unmatched object order and pending wake");
        Check(!((bool)Field(f.Session,"_dirty"))&&f.Serialize(f.Display()).SequenceEqual(before)&&((IDictionary)Field(f.Session,"_uiValueLeases")).Contains(lease),"polling does not retire controls/leases, increment revisions or dirty state");
        Check(Poll(ui,"missing-").Length==0&&f.Events().Count==1&&f.Pending.Contains("10:20"),"nonmatching prefix preserves entire queue and pending wake");
        f.TickWakes(10);Check(f.Runs==1&&f.WakeArguments[0]=="fixed-ui-wake"&&f.Pending.Contains("10:20"),"unmatched events still deliver same fixed wake and retry if busy");
        f.AcceptWake=true;f.TickWakes(16);Check(f.Runs==2&&!f.Pending.Contains("10:20")&&f.Events().Count==1,"accepted fixed wake leaves queued unmatched events without a pending retry");
        Check(Poll(ui,"missing-").Length==0&&!f.Pending.Contains("10:20")&&f.Events().Count==1,"nonmatching filter does not rearm an already delivered wake");
        var remainder=Poll(ui);Check(remainder.Length==1&&remainder[0].Item3=="other-gain"&&!f.Pending.Contains("10:20"),"zero-arg polling preserves old complete-drain behavior");
        Invoke(f.Session,"UiEndValueLease",lease,77L,true);Check(f.Events().Count==1&&f.Pending.Contains("10:20"),"terminal event still enters queue after selective polling");
        var all=Poll(ui,"");Check(all.Length==1&&all[0].Item1=="commit"&&f.Events().Count==0&&!f.Pending.Contains("10:20"),"explicit empty prefix drains all and clears wake");
        Write(ui,"html-gain",3);f.Access=false;Reject(()=>Poll(ui,"html-"),"poll reauthorizes target");Check(f.Events().Count==1&&f.Pending.Contains("10:20"),"failed authorization does not drain source events");f.Access=true;
        var other=f.Ui(11);Reject(()=>Poll(other,"html-"),"another PB cannot poll value owner");Check(f.Events().Count==1,"cross-caller poll leaves original queue");
        Check(Poll(ui,"html-").Length==1,"original caller retains its own event");
    }
    static void SelectiveRemoval()
    {
        using var f=new Fixture();var ui=f.Ui();ui("bundle",new object[]{"shared"});
        ui("value",new object[]{"html-gain",0d,0d,10d,1d});ui("value",new object[]{"other-gain",0d,0d,10d,1d});
        Attach(f,ui,"html-handle","html-gain","handle");Attach(f,ui,"other-handle","other-gain","other");
        ui("value-notify",new object[]{"fixed-ui-wake"});Write(ui,"html-gain",2);Write(ui,"other-gain",4);
        long lease=Begin(f,"html-handle");var old=f.Serialize(f.Display());var events=f.Events().Cast<object>().ToArray();var sourceGeometry=Field(f.Items["10:handle"],"Geometry");
        Set(f.Session,"_dirty",false);Reject(()=>ui("remove-value",new object[]{"html-gain"}),"bound numeric value rejects removal");
        Check(f.Serialize(f.Display()).SequenceEqual(old)&&f.Events().Cast<object>().SequenceEqual(events)&&f.Pending.Contains("10:20")&&!((bool)Field(f.Session,"_dirty"))&&((IDictionary)Field(f.Session,"_uiValueLeases")).Contains(lease),"bound rejection preserves values/events/wake/revisions and active lease");
        f.Draw()("geometry-cost",new object[]{"text","source only",1d});
        Check(f.Serialize(f.Display()).SequenceEqual(old)&&f.Events().Cast<object>().SequenceEqual(events)&&((IDictionary)Field(f.Session,"_uiValueLeases")).Contains(lease)&&ReferenceEquals(sourceGeometry,Field(f.Items["10:handle"],"Geometry")),"PB cost query cannot retire or rebuild a live UI control");
        foreach(object[] invalid in new[]{new object[]{"UPPER"},new object[]{new string('a',25)},new object[]{1},new object[]{"html-gain",true}})
        {Reject(()=>ui("remove-value",invalid),"invalid remove id/arity");Check(f.Serialize(f.Display()).SequenceEqual(old)&&f.Events().Cast<object>().SequenceEqual(events),"invalid removal is atomic");}
        var valueBefore=Value(ui,"other-gain");ui("bind-value",new object[]{"html-handle",""});
        Check(!((IDictionary)Field(f.Session,"_uiValueLeases")).Contains(lease),"normal definition mutation already cancels gesture on explicit unbind");
        long unrelatedLease=Begin(f,"other-handle");
        var display=f.Display();long definition=display.Revision,data=display.DataRevision;var otherControl=display.Widgets.Single(w=>w.Id=="other-handle").Control;var otherPose=Field(f.Items["10:other"],"Transform");var otherGeometry=Field(f.Items["10:other"],"Geometry");
        Check((bool)ui("remove-value",new object[]{"html-gain"}),"unbound value removal returns true");
        Check(!((IDictionary)Field(f.Session,"_uiValueLeases")).Contains(unrelatedLease),"successful numeric declaration cleanup follows existing global definition gesture-cancellation policy");
        display=f.Display();Check(display.Values.Count==1&&display.Values[0].Id=="other-gain"&&Value(ui,"other-gain").Equals(valueBefore),"selective removal keeps unrelated numeric record and revision");
        Check(display.Revision>definition&&display.DataRevision>data&&display.ValueNotify=="fixed-ui-wake"&&display.Bundles.Count==1&&display.Widgets.Count==2,"removal advances normal metadata revisions without clearing UI or fixed notify");
        Check(display.Widgets.Single(w=>w.Id=="other-handle").Control.SourceRevision==otherControl.SourceRevision&&Field(f.Items["10:other"],"Transform").Equals(otherPose)&&ReferenceEquals(Field(f.Items["10:other"],"Geometry"),otherGeometry)&&ReferenceEquals(Field(f.Items["10:handle"],"Geometry"),sourceGeometry),"removal preserves unrelated control binding/pose and all retained geometry");
        Check(f.Pending.Contains("10:20"),"removal preserves pending wake for unrelated source/terminal events");
        var retained=Poll(ui,"other-");Check(retained.Length==3&&retained[0].Item3=="other-gain"&&retained[1].Item1=="begin"&&retained[2].Item1=="cancel"&&Poll(ui).Length==0,"removed value events are pruned while unrelated source/begin/cancel events survive in order");
        Set(f.Session,"_dirty",false);definition=display.Revision;data=display.DataRevision;
        Check(!(bool)ui("remove-value",new object[]{"html-gain"})&&f.Display().Revision==definition&&f.Display().DataRevision==data&&!((bool)Field(f.Session,"_dirty")),"missing already-removed value is an idempotent false with no revision change");
        Write(ui,"other-gain",6);Check(f.Pending.Contains("10:20"),"unrelated notification remains operational");
        f.Access=false;Reject(()=>ui("remove-value",new object[]{"other-gain"}),"remove reauthorizes target");Check(f.Display().Values.Count==1&&f.Events().Count==1,"failed authorization preserves unrelated values/events");f.Access=true;
        var another=f.Ui(11);another("value",new object[]{"other-gain",1d,0d,2d});Check(!(bool)another("remove-value",new object[]{"html-gain"})&&Value(ui,"other-gain").Item1==6,"caller-scoped cleanup cannot delete same-target other owner state");
        Check((bool)another("remove-value",new object[]{"other-gain"})&&Value(ui,"other-gain").Item1==6,"second PB removes only its own same-ID value");
        ui("bind-value",new object[]{"other-handle",""});Check((bool)ui("remove-value",new object[]{"other-gain"}),"last unbound record cleanup");
        Check(f.Display().Values.Count==0&&f.Display().Widgets.Count==2&&f.Events().Count==0&&!f.Pending.Contains("10:20"),"empty numeric cleanup preserves UI/artwork and clears only empty wake queue");
    }
}
