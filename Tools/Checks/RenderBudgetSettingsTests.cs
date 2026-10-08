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
        Check(Values(Settings(draw),0,0,20000),"default aggregate geometry is unlimited while draw work stays finite");
        Check((int)Field(Scene(session),"PointBudget")==int.MaxValue&&(int)Field(Scene(session),"PrimitiveBudget")==int.MaxValue,"unlimited public allowances have safe internal admission values");
        draw("budget",new object[]{4096});Check(Values(Settings(draw),4096,0,20000),"omitted primitive allowance defaults to unlimited");
        draw("budget",new object[]{0,8192});Check(Values(Settings(draw),0,8192,20000),"each geometry allowance can independently be unlimited");
        Check((bool)draw("budget",new object[]{8192,16384,40000}),"budget command reports success");
        Check(Values(Settings(draw),8192,16384,40000),"configured values are queryable through SDK tuple");
        Check((int)Field(session,"ClientDrawWorkCap")==20000,"anchor budget cannot override a viewer's local draw cap");
        draw("render-budget",new object[]{65536,131072,200000});
        Check(Values(Settings(draw),65536,131072,200000),"previous finite ceilings and command alias accepted");
        draw("budget",new object[]{65537,131073,20000});
        Check(Values(Settings(draw),65537,131073,20000),"finite geometry allowances above former arbitrary ceilings are accepted");
        draw("budget",new object[]{int.MaxValue,int.MaxValue,20000});
        Check(Values(Settings(draw),0,0,20000),"largest integer geometry allowances canonicalize to unlimited");
        draw("budget",new object[]{1,1,1000});
        Check(Values(Settings(draw),1,1,1000),"small positive finite allowances are valid for an empty scene");
        draw("budget",new object[]{256,256,1000});
        Check(Values(Settings(draw),256,256,1000),"lower bounds accepted");
        void Invalid(object[] args,string name)
        {
            var prior=Settings(draw);bool dirty=(bool)Field(session,"_dirty");Reject(()=>draw("budget",args),name);
            Check(Values(Settings(draw),prior.Item1,prior.Item2,prior.Item3)&&(bool)Field(session,"_dirty")==dirty,name+" is atomic");
        }
        Set(session,"_dirty",false);
        Invalid(new object[]{(long)int.MaxValue+1,8192,20000},"point allowance outside integer representation");
        Invalid(new object[]{4096,(long)int.MaxValue+1,20000},"primitive allowance outside integer representation");
        Invalid(new object[]{-1,0,20000},"negative point allowance");Invalid(new object[]{0,-1,20000},"negative primitive allowance");
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
        draw("budget",new object[0]);Check(Values(Settings(draw),0,0,20000),"no-argument budget restores unlimited geometry defaults");
        var large=Points(2048);var line=new[]{new Vector2I(0,1)};
        draw("wires",new object[]{"first",large,line,"cyan"});draw("wires",new object[]{"second",large,line,"cyan"});
        var items=(IDictionary)Field(Scene(session),"Items");
        draw("wires",new object[]{"third",large,line,"cyan"});
        Check(items.Count==3,"default aggregate admission accepts geometry above the former 4096 point allowance");
        var dense=Edges(4096);for(int i=0;i<3;i++)draw("wires",new object[]{"dense"+i,Points(128),dense,"cyan"});
        Check(items.Count==6&&Values(Settings(draw),0,0,20000),"default aggregate admission accepts geometry above the former 8192 primitive allowance");
        draw("budget",new object[]{8192,16384,40000});
        Check(Values(Settings(draw),8192,16384,40000),"finite allowances can be configured for existing bounded geometry");
        Invalid(new object[]{4096,16384,40000},"cannot lower point budget below declared geometry");
        Reject(()=>draw("wires",new object[]{"fourth",large,line,"cyan"}),"configured aggregate point allowance enforced");
        Check(items.Count==6&&!items.Contains("10:fourth"),"finite point rejection preserves existing objects");
        Reject(()=>draw("wires",new object[]{"dense3",Points(128),dense,"cyan"}),"configured aggregate primitive allowance enforced");
        Check(items.Count==6&&!items.Contains("10:dense3"),"finite primitive rejection preserves existing objects");
        Reject(()=>draw("wires",new object[]{"too-many-points",Points(2049),line,"cyan"}),"anchor setting cannot raise hard per-object point cap");
        Reject(()=>draw("wires",new object[]{"too-many-edges",Points(128),Enumerable.Repeat(new Vector2I(0,1),4097).ToArray(),"cyan"}),"anchor setting cannot raise hard per-object primitive cap");
        Invalid(new object[]{8192,8192,40000},"cannot lower primitive budget below declared geometry");
        Check(Values(Settings(draw),8192,16384,40000),"hard object caps remain independent of pooled anchor settings");
        draw("clear",new object[0]);draw("budget",new object[0]);
        CacheInvalidation(session,draw);
        CompositionBudgetInvalidation(session,draw);
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
        Reject(()=>draw("budget",new object[]{-1,8192,20000}),"invalid settings do not reach cache invalidation");
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
    static void CompositionBudgetInvalidation(HoloMapSession session,Func<string,object[],object> draw)
    {
        draw("budget",new object[0]);
        draw("screen",new object[]{"comp",MatrixD.Identity,2d,1d,2d,1d});
        draw("screen-slot",new object[]{"video","sample","source",new Vector4(-1,-.5f,2,1),new Vector4(-1,-.5f,2,1),new Vector4(0,0,1,1),0});
        draw("screen-target",new object[]{""});
        var screen=((IDictionary)Field(Scene(session),"Screens"))["10:comp"];
        var data=(HoloProjectedScreenData)Field(screen,"Data");var declaration=data.SourceSlots[0];
        var providerReleased=new List<object>();var rasterReleased=new List<object>();
        Func<string,object[],object> provider=(op,args)=>{if(op=="release")providerReleased.Add(args[0]);return true;};
        Func<string,object[],object> backend=(op,args)=>{if(op=="release")rasterReleased.Add(args[0]);return true;};
        object Raster(object lease)
        {
            var image=NewNested("RasterImage");Set(image,"Lease",lease);Set(image,"Backend",backend);return image;
        }
        object Seed(long anchor,out object slot,out object chunk,out object proof,out object videoLease,out object uiLease)
        {
            var parent=NewNested("ProjectedCache");Set(parent,"Anchor",anchor);Set(parent,"Caller",10L);Set(parent,"Id","comp");Set(parent,"Data",data);
            slot=Call(session,"EnsureSourceSlotCache",parent,declaration);Set(slot,"Declaration",declaration);
            var source=Field(slot,"Source");var mesh=MeshEffects.Solid(Geometry.Wires(Points(512),new[]{new Vector2I(0,1)}),Vector4.One,Vector4.One);
            Set(slot,"Raw",mesh);Set(slot,"Canvas",mesh);Set(source,"View",mesh);Set(source,"NextCapture",1000d);
            proof=new object();Set(source,"SourceProtocol",2);Set(source,"SourceEndpoint",provider);Set(source,"SourceEvidence",proof);Static("RetainProjectedProviderEvidence",provider,proof,source);
            videoLease=new object();Set(source,"SourceRaster",Raster(videoLease));
            chunk=NewNested("ProjectedCache");Set(chunk,"Anchor",anchor);uiLease=new object();Set(chunk,"UiRaster",Raster(uiLease));Set(chunk,"UiRasterMesh",mesh);
            ((IDictionary)Field(parent,"UiChunks"))["chunk:0"]=chunk;
            ((IDictionary)Field(parent,"Mapped"))["slot:video"]=NewNested("MappedScreenMesh");
            ((IDictionary)Field(parent,"Mapped"))["chunk:0"]=NewNested("MappedScreenMesh");
            ((IDictionary)Field(session,"_projectedCaches"))[anchor+":10:comp"]=parent;return parent;
        }
        object slot,chunk,proof,videoLease,uiLease;var parent=Seed(20,out slot,out chunk,out proof,out videoLease,out uiLease);
        object otherSlot,otherChunk,otherProof,otherVideo,otherUi;var other=Seed(21,out otherSlot,out otherChunk,out otherProof,out otherVideo,out otherUi);
        Set(session,"_dirty",false);draw("budget",new object[0]);
        Check(ReferenceEquals(((IDictionary)Field(parent,"Slots"))["video"],slot)&&rasterReleased.Count==0&&providerReleased.Count==0&&!(bool)Field(session,"_dirty"),"unchanged unlimited budget preserves composition caches and provider/raster leases");
        Reject(()=>draw("budget",new object[]{1,1,20000}),"finite allowance below declared screen/slot geometry rejects before composition invalidation");
        Check(ReferenceEquals(((IDictionary)Field(parent,"Slots"))["video"],slot)&&((IDictionary)Field(parent,"UiChunks")).Count==1&&((IDictionary)Field(parent,"Mapped")).Count==2&&rasterReleased.Count==0&&providerReleased.Count==0&&!(bool)Field(session,"_dirty"),"rejected finite allowance preserves slots, artwork chunks, mapped payloads and all leases atomically");
        draw("budget",new object[]{16,16,40000});
        Check(Values(Settings(draw),16,16,40000),"finite allowance fitting declared geometry can replace unlimited presentation allowance");
        Check(((IDictionary)Field(parent,"Slots")).Count==0&&((IDictionary)Field(parent,"UiChunks")).Count==0&&((IDictionary)Field(parent,"Mapped")).Count==0,"accepted budget change removes oversized composition canvases, artwork chunks and mapped meshes before another draw");
        var sourceCache=Field(slot,"Source");
        Check(Field(sourceCache,"View")==null&&Field(sourceCache,"SourceEvidence")==null&&Field(sourceCache,"SourceRaster")==null&&Field(chunk,"UiRaster")==null,"accepted budget change detaches all owned source and UI raster resources");
        Check(providerReleased.Count==1&&ReferenceEquals(providerReleased[0],proof)&&rasterReleased.Count==2&&rasterReleased.Count(x=>ReferenceEquals(x,videoLease))==1&&rasterReleased.Count(x=>ReferenceEquals(x,uiLease))==1,"composition invalidation releases source proof and each distinct raster lease exactly once");
        Check(((IDictionary)Field(other,"Slots")).Count==1&&((IDictionary)Field(other,"UiChunks")).Count==1&&Field(otherSlot,"Canvas")!=null&&Field(otherChunk,"UiRaster")!=null&&!providerReleased.Contains(otherProof)&&!rasterReleased.Contains(otherVideo)&&!rasterReleased.Contains(otherUi),"another anchor retains its composition canvases, chunks and leases");
        var fresh=Call(session,"EnsureSourceSlotCache",parent,declaration);
        Check(!ReferenceEquals(fresh,slot)&&Field(fresh,"Canvas")==null&&(double)Field(Field(fresh,"Source"),"NextCapture")==-1,"next composition preparation creates a fresh source without the old delayed capture deadline");
        Static("ReleaseSourceSlots",other);Static("ReleaseSourceSlots",parent);
    }
    static void Replication(ClientReplicationTests.GatewayScope gateway)
    {
        var legacy=RoundTrip(ClientReplicationTests.Snapshot());var client=new HoloMapSession();Call(client,"ApplySnapshot",legacy);
        Check((int)Field(Scene(client),"PointBudget")==int.MaxValue&&(int)Field(Scene(client),"PrimitiveBudget")==int.MaxValue&&(int)Field(Scene(client),"DrawWorkBudget")==20000,"absent serialized budget imports unlimited geometry defaults");
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
        Invalid(s=>s.Budget.DrawWork=999,"malformed replicated draw-work minimum");Invalid(s=>s.Budget.DrawWork=200001,"malformed replicated draw-work maximum");
        Invalid(s=>s.Budget.Points=-1,"protobuf negative point allowance rejected");
        Invalid(s=>s.Budget.Primitives=-1,"protobuf negative primitive allowance rejected");
        Invalid(s=>s.Budget.DrawWork=0,"protobuf zero draw-work budget remains invalid");
        Invalid(s=>s.Budget=new HoloRenderBudgetData{Points=4096},"present incomplete protobuf budget cannot acquire constructor defaults");
        Invalid(s=>s.Budget=new HoloRenderBudgetData(),"present empty protobuf budget rejected");
        Invalid(s=>{s.Budget.Points=256;s.Items[0].Points=Points(512).SelectMany(p=>new[]{p.X,p.Y,p.Z}).ToArray();},"replicated geometry exceeds configured anchor budget");
        Invalid(s=>{s.Budget.Points=65536;s.Items[0].Points=Points(2049).SelectMany(p=>new[]{p.X,p.Y,p.Z}).ToArray();},"replicated hard per-object point cap retained");
        Invalid(s=>{s.Budget.Primitives=131072;s.Items[0].Edges=new int[4097*2];},"replicated hard per-object primitive cap retained");
        int protocol=(int)typeof(HoloMapSession).GetField("NetworkProtocol",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
        Check(protocol>=8,"budget scene schema uses an updated explicit protocol");
        UnlimitedReplication(client,merged);
    }
    static void UnlimitedReplication(HoloMapSession client,HoloSnapshot finite)
    {
        var unlimited=RoundTrip(finite);unlimited.Scenes[0].Budget=new HoloRenderBudgetData{Points=0,Primitives=0,DrawWork=20000};
        var original=RoundTrip(unlimited.Scenes[0].Items[0]);unlimited.Scenes[0].Items.Clear();
        for(int i=0;i<3;i++)
        {
            var item=RoundTrip(original);item.Id="large"+i;item.Points=Points(2048).SelectMany(p=>new[]{p.X,p.Y,p.Z}).ToArray();
            item.Edges=Enumerable.Repeat(new[]{0,1},4096).SelectMany(e=>e).ToArray();item.Triangles=new int[0];
            unlimited.Scenes[0].Items.Add(item);
        }
        unlimited=RoundTrip(unlimited);Call(client,"ApplySnapshot",unlimited);
        Check((int)Field(Scene(client),"PointBudget")==int.MaxValue&&(int)Field(Scene(client),"PrimitiveBudget")==int.MaxValue,"full protobuf zero allowances import as unlimited");
        Check(((IDictionary)Field(Scene(client),"Items")).Count==3,"full replication accepts aggregate geometry above both former defaults");
        var exported=(HoloRenderBudgetData)Static("ExportRenderBudget",Scene(client));
        Check(exported.Points==0&&exported.Primitives==0&&exported.DrawWork==20000,"unlimited internal allowances export the public zero sentinel");
        var tags=typeof(HoloRenderBudgetData).GetFields().Select(f=>f.GetCustomAttribute<ProtoBuf.ProtoMemberAttribute>().Tag).ToArray();
        Check(tags.SequenceEqual(new[]{1,2,3}),"budget protobuf tags retain their original field identities");
        var minimal=RoundTrip(new HoloRenderBudgetData{DrawWork=20000});
        Check(minimal.Points==0&&minimal.Primitives==0&&minimal.DrawWork==20000,"omitted protobuf geometry fields retain the zero unlimited defaults");
        var largest=RoundTrip(unlimited);largest.Scenes[0].Budget=new HoloRenderBudgetData{Points=int.MaxValue,Primitives=int.MaxValue,DrawWork=20000};Call(client,"ApplySnapshot",RoundTrip(largest));
        var canonical=RoundTrip((HoloRenderBudgetData)Static("ExportRenderBudget",Scene(client)));
        Check(canonical.Points==0&&canonical.Primitives==0&&canonical.DrawWork==20000,"largest integer geometry declarations import and export canonical zero unlimited fields");
        var largeFinite=RoundTrip(unlimited);largeFinite.Scenes[0].Budget=new HoloRenderBudgetData{Points=65537,Primitives=131073,DrawWork=20000};Call(client,"ApplySnapshot",largeFinite);
        Check((int)Field(Scene(client),"PointBudget")==65537&&(int)Field(Scene(client),"PrimitiveBudget")==131073,"full protobuf preserves positive finite allowances above former ceilings");
        var finiteAgain=RoundTrip(unlimited);finiteAgain.Scenes[0].Budget=new HoloRenderBudgetData{Points=8192,Primitives=16384,DrawWork=40000};
        var delta=(HoloSnapshot)Static("BuildDelta",unlimited,finiteAgain,1L);var merged=(HoloSnapshot)Static("MergeDelta",unlimited,RoundTrip(delta),1L);Call(client,"ApplySnapshot",merged);
        Check(delta.Scenes.Count==1&&delta.Scenes[0].Items.Count==0&&(int)Field(Scene(client),"PointBudget")==8192,"unlimited to finite metadata delta preserves geometry and finite settings");
        var toUnlimited=(HoloSnapshot)Static("BuildDelta",finiteAgain,unlimited,2L);var final=(HoloSnapshot)Static("MergeDelta",finiteAgain,RoundTrip(toUnlimited),2L);Call(client,"ApplySnapshot",final);
        Check(toUnlimited.Scenes.Count==1&&toUnlimited.Scenes[0].Items.Count==0&&(int)Field(Scene(client),"PrimitiveBudget")==int.MaxValue,"finite to unlimited metadata delta preserves zero allowance semantics");
        Check(((IDictionary)Field(Scene(client),"Items")).Count==3,"budget-only deltas preserve all admitted geometry");
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
