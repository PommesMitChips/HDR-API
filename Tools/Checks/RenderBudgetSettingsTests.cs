using System.Collections;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

internal static class RenderBudgetSettingsTests
{
    static int _checks;
    static void Check(bool value,string name){if(!value)throw new Exception("Render budget settings: "+name);_checks++;}
    static object Call(object target,string name,params object[] args)=>ClientReplicationTests.Call(target,name,args);
    static object Field(object target,string name)=>ClientReplicationTests.Field(target,name);
    static void Set(object target,string name,object value)=>ClientReplicationTests.SetField(target,name,value);
    static object Static(string name,params object[] args)
    {
        try{return typeof(HoloMapSession).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
        catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    static void Reject(Action action,string name)
    {try{action();}catch(ArgumentException){_checks++;return;}throw new Exception("Render budget expected rejection: "+name);}
    static T RoundTrip<T>(T value)=>MyAPIGateway.Utilities.SerializeFromBinary<T>(MyAPIGateway.Utilities.SerializeToBinary(value));
    static object Scene(HoloMapSession session,long id=20)=>((IDictionary)Field(session,"_scenes"))[id];
    static MyTuple<int,int,int> Settings(Func<string,object[],object> draw)=>(MyTuple<int,int,int>)draw("budget-settings",new object[0]);
    static bool Values(MyTuple<int,int,int> value,int points,int primitives,int work)=>value.Item1==points&&value.Item2==primitives&&value.Item3==work;
    static Vector3D[] Points(int count)=>Enumerable.Range(0,count).Select(n=>new Vector3D((double)n/Math.Max(1,count-1),0,0)).ToArray();
    static Vector2I[] Edges(int count)
    {
        var result=new List<Vector2I>();for(int a=0;a<128&&result.Count<count;a++)for(int b=a+1;b<128&&result.Count<count;b++)result.Add(new Vector2I(a,b));return result.ToArray();
    }
    public static int Run()
    {
        _checks=0;using var gateway=new ClientReplicationTests.GatewayScope{Server=true};
        bool access=true,sameConstruct=true;long owner=77;var registered=new Dictionary<long,object>();
        object caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>{
            if(m.Name=="get_EntityId")return 10L;if(m.Name=="get_OwnerId")return owner;if(m.Name=="get_Closed")return false;
            if(m.Name=="get_IsWorking")return true;if(m.Name=="IsSameConstructAs")return sameConstruct;throw new Exception("Budget caller: "+m.Name);});
        object target=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>{
            if(m.Name=="get_EntityId")return 20L;if(m.Name=="get_Closed")return false;if(m.Name=="get_IsWorking")return true;
            if(m.Name=="HasPlayerAccess")return access;throw new Exception("Budget target: "+m.Name);});
        registered[10]=caller;registered[20]=target;
        gateway.Install("Entities",(m,a)=>{if(m.Name=="GetEntityById"){registered.TryGetValue((long)a[0],out var value);return value;}throw new Exception("Budget entities: "+m.Name);});
        var session=new HoloMapSession();var draw=(Func<string,object[],object>)Call(session,"DrawEndpoint",caller);
        Check((string)typeof(HoloMapSession).GetField("DrawPropertyId",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue()=="HDR.Draw","budget commands use the public short API");
        Reject(()=>draw("budget-settings",new object[0]),"query requires selected anchor");
        Reject(()=>draw("budget",new object[]{8192,16384,40000}),"configuration requires selected anchor");
        draw("target",new[]{target});
        Check(Values(Settings(draw),4096,8192,20000),"default shared anchor budgets");
        Check((bool)draw("budget",new object[]{8192,16384,40000}),"budget command reports success");
        Check(Values(Settings(draw),8192,16384,40000),"configured values are queryable through SDK tuple");
        Check((int)Field(session,"ClientDrawWorkCap")==20000,"anchor budget cannot override a viewer's local draw cap");
        draw("render-budget",new object[]{65536,131072,200000});
        Check(Values(Settings(draw),65536,131072,200000),"upper bounds and command alias accepted");
        draw("budget",new object[]{256,256,1000});
        Check(Values(Settings(draw),256,256,1000),"lower bounds accepted");
        void Invalid(object[] args,string name)
        {
            var prior=Settings(draw);bool dirty=(bool)Field(session,"_dirty");Reject(()=>draw("budget",args),name);
            Check(Values(Settings(draw),prior.Item1,prior.Item2,prior.Item3)&&(bool)Field(session,"_dirty")==dirty,name+" is atomic");
        }
        Set(session,"_dirty",false);
        Invalid(new object[]{255,8192,20000},"point lower bound");Invalid(new object[]{65537,8192,20000},"point upper bound");
        Invalid(new object[]{4096,255,20000},"primitive lower bound");Invalid(new object[]{4096,131073,20000},"primitive upper bound");
        Invalid(new object[]{4096,8192,999},"draw work lower bound");Invalid(new object[]{4096,8192,200001},"draw work upper bound");
        Invalid(new object[]{4096.5,8192,20000},"fractional budget rejected");Invalid(new object[]{double.NaN,8192,20000},"nonfinite budget rejected");
        Invalid(new object[]{4096,8192,20000,"extra"},"extra arguments rejected");
        Reject(()=>draw("budget-settings",new object[]{1}),"query rejects arguments");
        access=false;Reject(()=>draw("budget",new object[]{4096,8192,20000}),"lost anchor access blocks configuration");
        Reject(()=>draw("budget-settings",new object[0]),"lost anchor access blocks query");access=true;
        gateway.Server=false;Reject(()=>draw("budget",new object[]{4096,8192,20000}),"client cannot configure authoritative budgets");gateway.Server=true;
        owner=0;Reject(()=>draw("budget",new object[]{4096,8192,20000}),"unowned caller rejected");owner=77;
        sameConstruct=false;Reject(()=>draw("budget",new object[]{4096,8192,20000}),"unrelated construct rejected");sameConstruct=true;
        registered.Remove(10);Reject(()=>draw("budget",new object[]{4096,8192,20000}),"unregistered caller rejected");registered[10]=caller;
        Check(Values(Settings(draw),256,256,1000),"authorization failures preserve current configuration");
        draw("budget",new object[0]);Check(Values(Settings(draw),4096,8192,20000),"no-argument budget restores defaults");
        var large=Points(2048);var line=new[]{new Vector2I(0,1)};
        draw("wires",new object[]{"first",large,line,"cyan"});draw("wires",new object[]{"second",large,line,"cyan"});
        var items=(IDictionary)Field(Scene(session),"Items");
        Reject(()=>draw("wires",new object[]{"third",large,line,"cyan"}),"default pooled points still enforced");
        Check(items.Count==2&&!items.Contains("10:third"),"point rejection preserves existing objects");
        draw("budget",new object[]{8192,16384,40000});draw("wires",new object[]{"third",large,line,"cyan"});
        Check(items.Count==3,"raised anchor point budget allows more separately bounded objects");
        Invalid(new object[]{4096,16384,40000},"cannot lower point budget below declared geometry");
        Reject(()=>draw("wires",new object[]{"too-many-points",Points(2049),line,"cyan"}),"anchor setting cannot raise hard per-object point cap");
        Reject(()=>draw("wires",new object[]{"too-many-edges",Points(128),Enumerable.Repeat(new Vector2I(0,1),4097).ToArray(),"cyan"}),"anchor setting cannot raise hard per-object primitive cap");
        var dense=Edges(4096);for(int i=0;i<3;i++)draw("wires",new object[]{"dense"+i,Points(128),dense,"cyan"});
        Invalid(new object[]{8192,8192,40000},"cannot lower primitive budget below declared geometry");
        Check(Values(Settings(draw),8192,16384,40000),"hard object caps remain independent of pooled anchor settings");
        draw("clear",new object[0]);draw("budget",new object[0]);
        CacheInvalidation(session,draw);
        Replication(gateway);
        LocalViewerBudget();
        return _checks;
    }
    static object NewNested(string name)=>Activator.CreateInstance(typeof(HoloMapSession).GetNestedType(name,BindingFlags.NonPublic),true);
    static object SeedProjected(HoloMapSession session,long anchor)
    {
        object cache=NewNested("ProjectedCache");Set(cache,"Anchor",anchor);Set(cache,"NextSample",10d);Set(cache,"NextCapture",10d);Set(cache,"Error","stale");Set(cache,"SourceReduced",true);
        var geometry=Geometry.Wires(new[]{Vector3D.Zero,Vector3D.UnitX},new[]{new Vector2I(0,1)});
        var mesh=MeshEffects.Solid(geometry,Vector4.One,Vector4.One);Set(cache,"View",mesh);Set(cache,"Sprites",mesh);
        Set(cache,"ViewVersion",new HoloProjectedScreenData());Set(cache,"SpriteVersion",new HoloProjectedScreenData());
        var list=(IList)Field(cache,"Items");list.Add(Activator.CreateInstance(list.GetType().GetGenericArguments()[0],true));
        ((IDictionary)Field(session,"_projectedCaches"))[anchor+":cache"]=cache;return cache;
    }
    static void CacheInvalidation(HoloMapSession session,Func<string,object[],object> draw)
    {
        var cache=(IDictionary)Field(session,"_clientGeometry");object own=NewNested("CompiledDisplay"),other=NewNested("CompiledDisplay");
        Set(own,"ConsoleId",20L);Set(other,"ConsoleId",21L);cache["20:10:cached"]=own;cache["21:10:cached"]=other;
        var queued=(HashSet<string>)Field(session,"_queuedCompile");queued.Add("20:10:cached");queued.Add("21:10:cached");
        var pending=(Queue<string>)Field(session,"_compileQueue");pending.Enqueue("20:10:cached");pending.Enqueue("21:10:cached");
        object projected=SeedProjected(session,20),unrelated=SeedProjected(session,21);
        ((IDictionary)Field(session,"_lcdNativeSamples"))[20L]=NewNested("NativeLcdSample");
        Set(session,"_dirty",false);draw("budget",new object[0]);
        Check(ReferenceEquals(cache["20:10:cached"],own)&&!(bool)Field(session,"_dirty"),"unchanged budgets preserve compiled caches and dirty state");
        Reject(()=>draw("budget",new object[]{255,8192,20000}),"invalid settings do not reach cache invalidation");
        Check(ReferenceEquals(cache["20:10:cached"],own)&&Field(projected,"View")!=null,"invalid configuration preserves rendering caches");
        draw("budget",new object[]{8192,16384,40000});
        Check(!cache.Contains("20:10:cached")&&ReferenceEquals(cache["21:10:cached"],other),"budget change invalidates only matching anchor geometry");
        Check(!queued.Contains("20:10:cached")&&queued.Contains("21:10:cached"),"matching pending compilation is released");
        Check(pending.Count==1&&pending.Peek()=="21:10:cached","invalidated compile queue entries compacted while unrelated work retained");
        Check(Field(projected,"View")==null&&Field(projected,"Sprites")==null&&Field(projected,"ViewVersion")==null&&Field(projected,"SpriteVersion")==null,"projected presentation meshes and versions invalidated");
        Check(((IList)Field(projected,"Items")).Count==0&&(double)Field(projected,"NextSample")==-1,"projected budget change schedules fresh sampling");
        Check((double)Field(projected,"NextCapture")==-1&&Field(projected,"Error")==null&&!(bool)Field(projected,"SourceReduced"),"capture cadence and stale adaptive status reset");
        Check(((IDictionary)Field(session,"_lcdNativeSamples")).Count==0,"budget changes invalidate native LCD sampling baselines");
        Check(Field(unrelated,"View")!=null&&((IList)Field(unrelated,"Items")).Count==1,"other anchor presentation retained");
    }
    static void Replication(ClientReplicationTests.GatewayScope gateway)
    {
        var legacy=RoundTrip(ClientReplicationTests.Snapshot());var client=new HoloMapSession();Call(client,"ApplySnapshot",legacy);
        Check((int)Field(Scene(client),"PointBudget")==4096&&(int)Field(Scene(client),"PrimitiveBudget")==8192&&(int)Field(Scene(client),"DrawWorkBudget")==20000,"missing budget declaration preserves defaults");
        var full=ClientReplicationTests.Snapshot();full.Scenes[0].Budget=new HoloRenderBudgetData{Points=8192,Primitives=16384,DrawWork=50000};
        full=RoundTrip(full);Call(client,"ApplySnapshot",full);
        Check((int)Field(Scene(client),"PointBudget")==8192&&(int)Field(Scene(client),"PrimitiveBudget")==16384&&(int)Field(Scene(client),"DrawWorkBudget")==50000,"full protobuf transports configured budgets");
        var joined=new HoloMapSession();Call(joined,"ApplySnapshot",RoundTrip(full));Check((int)Field(Scene(joined),"PointBudget")==8192,"late join imports shared settings");
        var changed=RoundTrip(full);changed.Scenes[0].Budget.DrawWork=60000;
        var delta=(HoloSnapshot)Static("BuildDelta",full,changed,1L);
        Check(delta.Delta&&delta.Scenes.Count==1&&delta.Scenes[0].Partial&&delta.Scenes[0].Items.Count==0,"budget-only change is a scene metadata delta");
        object projected=SeedProjected(client,20);
        var merged=(HoloSnapshot)Static("MergeDelta",full,RoundTrip(delta),1L);Call(client,"ApplySnapshot",merged);
        Check((int)Field(Scene(client),"DrawWorkBudget")==60000&&((IDictionary)Field(Scene(client),"Items")).Count==1,"budget delta preserves geometry and applies settings");
        Check(Field(projected,"View")==null,"received budget change invalidates local presentation cache");
        void Invalid(Action<HoloSceneData> mutate,string name)
        {
            var bad=RoundTrip(merged);mutate(bad.Scenes[0]);object prior=Scene(client);object rendered=SeedProjected(client,20);
            Reject(()=>Call(client,"ApplySnapshot",RoundTrip(bad)),name);
            Check(ReferenceEquals(prior,Scene(client))&&Field(rendered,"View")!=null,name+" preserves scene and cache atomically");
        }
        Invalid(s=>s.Budget.Points=255,"malformed replicated point minimum");Invalid(s=>s.Budget.Points=65537,"malformed replicated point maximum");
        Invalid(s=>s.Budget.Primitives=255,"malformed replicated primitive minimum");Invalid(s=>s.Budget.Primitives=131073,"malformed replicated primitive maximum");
        Invalid(s=>s.Budget.DrawWork=999,"malformed replicated draw-work minimum");Invalid(s=>s.Budget.DrawWork=200001,"malformed replicated draw-work maximum");
        Invalid(s=>s.Budget.Points=0,"protobuf zero point budget remains invalid");
        Invalid(s=>s.Budget.Primitives=0,"protobuf zero primitive budget remains invalid");
        Invalid(s=>s.Budget.DrawWork=0,"protobuf zero draw-work budget remains invalid");
        Invalid(s=>s.Budget=new HoloRenderBudgetData{Points=4096},"present incomplete protobuf budget cannot acquire constructor defaults");
        Invalid(s=>s.Budget=new HoloRenderBudgetData(),"present empty protobuf budget rejected");
        Invalid(s=>{s.Budget.Points=256;s.Items[0].Points=Points(512).SelectMany(p=>new[]{p.X,p.Y,p.Z}).ToArray();},"replicated geometry exceeds configured anchor budget");
        Invalid(s=>{s.Budget.Points=65536;s.Items[0].Points=Points(2049).SelectMany(p=>new[]{p.X,p.Y,p.Z}).ToArray();},"replicated hard per-object point cap retained");
        Invalid(s=>{s.Budget.Primitives=131072;s.Items[0].Edges=new int[4097*2];},"replicated hard per-object primitive cap retained");
        int protocol=(int)typeof(HoloMapSession).GetField("NetworkProtocol",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
        Check(protocol>=8,"budget scene schema uses an updated explicit protocol");
    }
    static void LocalViewerBudget()
    {
        using var gateway=new ClientReplicationTests.GatewayScope();var session=new HoloMapSession();
        var snapshot=ClientReplicationTests.Snapshot();snapshot.Scenes[0].Budget=new HoloRenderBudgetData{Points=4096,Primitives=8192,DrawWork=70000};
        Call(session,"ApplySnapshot",snapshot);var other=new HoloMapSession();var messages=new List<string>();
        gateway.Install("Utilities",(m,a)=>{if(m.Name=="ShowMessage"){messages.Add((string)a[1]);return null;}throw new Exception("Local work utilities: "+m.Name);});
        void Command(string text){var args=new object[]{text,true};Call(session,"ClientMessageEntered",args);Check(!(bool)args[1],"local work command is not sent to other players");}
        Command("/hdr work 50000");Check((int)Field(session,"ClientDrawWorkCap")==50000,"viewer can raise local draw-work cap");
        Command("/hdr work 1000");Check((int)Field(session,"ClientDrawWorkCap")==1000,"viewer minimum accepted");
        Command("/hdr work 200000");Check((int)Field(session,"ClientDrawWorkCap")==200000,"viewer maximum accepted");
        foreach(string value in new[]{"999","200001","-1","nope","10.5","1 2"})
        {Command("/hdr work "+value);Check((int)Field(session,"ClientDrawWorkCap")==200000,"invalid local work cap preserves viewer setting");}
        Command(" /HDR WORK 120000 ");Check((int)Field(session,"ClientDrawWorkCap")==120000,"local work command handles whitespace and case");
        Check((int)Field(Scene(session),"DrawWorkBudget")==70000&&!(bool)Field(session,"_dirty"),"viewer cap never changes authoritative shared budget or dirties replication");
        Check((int)Field(other,"ClientDrawWorkCap")==20000,"another viewer session retains its independent default cap");
        Check(messages.Count==10,"local budget commands report accepted values or bounds");
    }
}
