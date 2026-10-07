using System.Collections;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRageMath;

// These exercise the real receiver and scene validator. The gateway doubles only
// replace transport/serialization; no production validation is mocked out.
internal static class ClientReplicationTests
{
    static int _checks;
    static void Check(bool condition,string name)
    {if(!condition)throw new Exception("Client replication: "+name);_checks++;}
    internal static object Call(object target,string method,params object[] args)
    {
        try{return target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    internal static object Field(object target,string name)=>target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Public).GetValue(target);
    internal static void SetField(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Public).SetValue(target,value);
    internal static HoloItemData Item(string id="line")=>new HoloItemData{
        CallerId=10,Id=id,Points=new[]{0d,0,0,1,0,0},Edges=new[]{0,1},Triangles=new int[0],
        Style=new[]{0f,1,1,1,0,1,1,0.3f,0.008f},
        Transform=new[]{1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1},Visible=true};
    internal static HoloSceneData Scene(long id=20)=>new HoloSceneData{
        ConsoleId=id,View=new[]{0d,0.8,0,0,0,0,1},Items=new List<HoloItemData>{Item()}};
    internal static HoloSnapshot Snapshot(long id=20)=>new HoloSnapshot{Scenes=new List<HoloSceneData>{Scene(id)}};
    static void Reject(Action action,string name)
    {try{action();}catch(ArgumentException){_checks++;return;}throw new Exception("Client replication expected rejection: "+name);}
    public static int Run()
    {
        _checks=0;
        AtomicBounds();
        ReceiverAuthentication();
        AnimationDeclarations();
        GeometryWorkLimits();
        DeferredGeometry();
        DeltaUpdates();
        PublishedAnimationPause();
        RemoteSvgFrames();
        CompilationFairness();
        RecoveryRequestFairness();
        ServerPackedValidationBudget();
        return _checks;
    }
    static void ServerPackedValidationBudget()
    {
        string Encode(string svg)
        {
            byte[] bytes=System.Text.Encoding.ASCII.GetBytes("HMA1\n1\n1\n1\n"+svg+"\n");
            var encoded=new List<byte>{72,77,67,49};encoded.AddRange(BitConverter.GetBytes(bytes.Length));
            for(int i=0;i<bytes.Length;i+=8){int count=Math.Min(8,bytes.Length-i);encoded.Add((byte)((1<<count)-1));encoded.AddRange(bytes.Skip(i).Take(count));}
            return "HoloMapAnimation1:"+Convert.ToBase64String(encoded.ToArray());
        }
        var session=new HoloMapSession();string valid=Encode("<svg/>");
        object first=Call(session,"DecodeServerPacked",valid),second=Call(session,"DecodeServerPacked",valid);
        Check(ReferenceEquals(first,second),"packed admission and commit reuse the same decoded animation object");
        Check((int)Field(session,"_serverDecodeTickCount")==1&&(int)Field(session,"_serverDecodeWindowCount")==1,"repeated valid source consumes only one decode allowance");
        string failed="HoloMapAnimation1:!0";Reject(()=>Call(session,"DecodeServerPacked",failed),"malformed packed source consumes initial validation attempt");
        int used=(int)Field(session,"_serverDecodeWindowCount");Reject(()=>Call(session,"DecodeServerPacked",failed),"repeated malformed source rejected from negative memo");
        Check((int)Field(session,"_serverDecodeWindowCount")==used,"negative memo avoids repeated decompression attempts");
        Reject(()=>Call(session,"DecodeServerPacked","HoloMapAnimation1:!extra"),"third changed source in same tick exceeds validation allowance");
        Check((int)Field(session,"_serverDecodeTickCount")==2,"per-tick decode attempts remain bounded including failures");
        for(int tick=1;tick<=3;tick++)
        {
            SetField(session,"_ticks",tick);
            for(int attempt=0;attempt<2;attempt++)Reject(()=>Call(session,"DecodeServerPacked","HoloMapAnimation1:!"+tick+":"+attempt),"changed malformed source charged against rolling validation window");
            Check(((IDictionary)Field(session,"_serverPackedMemo")).Count<=2,"packed validation memo has at most two entries");
        }
        SetField(session,"_ticks",4);Reject(()=>Call(session,"DecodeServerPacked","HoloMapAnimation1:!window"),"new tick cannot bypass eight-attempt window limit");
        Check((int)Field(session,"_serverDecodeWindowCount")==8,"failed-source flood cannot increase work beyond window allowance");
        SetField(session,"_ticks",60);var recovered=(PackedAnimation)Call(session,"DecodeServerPacked",Encode("<svg><rect/></svg>"));
        Check(recovered.Frames==1&&(int)Field(session,"_serverDecodeWindowCount")==1,"packed validation recovers after sixty simulation ticks");
        Call(session,"ClearServerPackedBudget");
        Check(((IDictionary)Field(session,"_serverPackedMemo")).Count==0&&(int)Field(session,"_serverDecodeTickCount")==0&&(int)Field(session,"_serverDecodeWindowCount")==0,"unload cleanup clears memo and all decode counters");
        Check((int)Field(session,"_serverDecodeTick")==-1&&(int)Field(session,"_serverDecodeWindow")==-60,"unload cleanup restores admission clock sentinels");
        Call(session,"DecodeServerPacked",valid);Check((int)Field(session,"_serverDecodeWindowCount")==1,"validation operates normally after unload reset");
    }
    static void RecoveryRequestFairness()
    {
        using var gateway=new GatewayScope{Server=true,Dedicated=true};
        var player=(VRage.Game.ModAPI.IMyPlayer)DrawTestProxy.Make(typeof(VRage.Game.ModAPI.IMyPlayer),(m,a)=>{
            if(m.Name=="get_SteamUserId")return 200UL;throw new Exception("Unexpected player method: "+m.Name);});
        gateway.Install("Players",(m,a)=>{
            if(m.Name!="GetPlayers")throw new Exception("Unexpected players method: "+m.Name);
            var list=(List<VRage.Game.ModAPI.IMyPlayer>)a[0];var filter=a.Length>1?(Func<VRage.Game.ModAPI.IMyPlayer,bool>)a[1]:null;
            if(filter==null||filter(player))list.Add(player);return null;});
        var session=new HoloMapSession();Call(session,"ApplySnapshot",Snapshot());
        SetField(session,"_sentSnapshot",Call(session,"CaptureSnapshot"));SetField(session,"_revision",1L);SetField(session,"_dirty",true);
        int protocol=(int)typeof(HoloMapSession).GetField("NetworkProtocol",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
        Call(session,"Receive",(ushort)49783,gateway.Serialize(new HoloFrame{Protocol=protocol,Request=true}),200UL,false);
        Check((ulong)Field(session,"_pendingRecovery")==200,"recovery request queues while an authoritative publication is dirty");
        for(int tick=30;tick<=32;tick++){SetField(session,"_ticks",tick);SetField(session,"_dirty",true);Call(session,"TickNetwork");}
        Check(gateway.Sent.Any(p=>p.Recipient==200),"pending recovery receives targeted full snapshot despite continuous changes");
        int decodes=gateway.Deserializations;Call(session,"Receive",(ushort)49783,new byte[]{0xff},200UL,false);
        Check(gateway.Deserializations==decodes,"request flood cooldown applies before malformed payload deserialization");
    }
    static void CompilationFairness()
    {
        using var gateway=new GatewayScope();var session=new HoloMapSession();var packet=Snapshot();var sceneData=packet.Scenes[0];
        sceneData.Items.Clear();for(int i=0;i<3;i++){var item=Item("animated"+i);item.Declaration=new HoloGeometryDeclaration{SvgSource="<svg viewBox='0 0 10 10'><rect width='4' height='10'/></svg>",SvgSegments=3};sceneData.Items.Add(item);}
        Call(session,"ApplySnapshot",packet);object scene=((IDictionary)Field(session,"_scenes"))[20L];var items=(IDictionary)Field(scene,"Items");
        var compiler=typeof(HoloMapSession).GetMethod("ClientDisplayItem",BindingFlags.NonPublic|BindingFlags.Instance);
        for(int tick=0;tick<6;tick++)
        {
            SetField(session,"_ticks",tick);
            foreach(DictionaryEntry entry in items)
            {object item=entry.Value;SetField(item,"SvgSource","<svg viewBox='0 0 10 10'><rect width='"+(4+tick%2)+"' height='10'/></svg>");compiler.Invoke(session,new[]{scene,item,(object)1});}
            Call(session,"CompileQueuedDisplay");int workTick=(int)Field(session,"_lastCompileTick");
            Call(session,"CompileQueuedDisplay");Check((int)Field(session,"_lastCompileTick")==workTick,"repeated compilation calls share the same frame allowance");
        }
        var caches=(IDictionary)Field(session,"_clientGeometry");
        Check(caches.Count==3&&caches.Values.Cast<object>().All(c=>Field(c,"Mesh")!=null),"continuously changing displays all receive compilation slots");
        Call(session,"ApplySnapshot",new HoloSnapshot());Call(session,"PruneClientGeometry");
        Check(caches.Count==0,"removed displays promptly release compiled geometry caches");
    }
    static void RemoteSvgFrames()
    {
        using var gateway=new GatewayScope();var session=new HoloMapSession();
        string first="<svg viewBox='0 0 10 10'><rect width='4' height='10' fill='cyan'/></svg>";
        string second="<svg viewBox='0 0 10 10'><rect width='8' height='10' fill='#ff9a30'/></svg>";
        var packet=Snapshot();packet.Scenes[0].Items[0].Declaration=new HoloGeometryDeclaration{SvgSource=first,SvgSegments=3};
        var animation=Animation();animation.StartTick=0;animation.Tracks.Clear();animation.Frames=new[]{first,second};animation.Fps=1;animation.Segments=3;animation.Loop=true;
        Call(session,"ApplySnapshot",packet);Call(session,"ImportDrawAnimations",new List<HoloAnimationData>{animation},20L);Call(session,"SetAnimationClock",0L);
        object scene=((IDictionary)Field(session,"_scenes"))[20L];object canonical=((IDictionary)Field(scene,"Items"))["10:line"];
        var compiler=typeof(HoloMapSession).GetMethod("ClientDisplayItem",BindingFlags.NonPublic|BindingFlags.Instance);
        object Render(){object animated=Call(session,"GetAnimatedItem",20L,canonical);compiler.Invoke(session,new[]{scene,animated,(object)1});Call(session,"CompileQueuedDisplay");return compiler.Invoke(session,new[]{scene,animated,(object)1});}
        SetField(session,"_ticks",0);object renderedFirst=Render();SetField(session,"_ticks",60);object renderedSecond=Render();
        Check(renderedFirst!=null&&renderedSecond!=null&&!ReferenceEquals(Field(renderedFirst,"Geometry"),Field(renderedSecond,"Geometry")),"remote SVG frame advances despite unchanged imported declaration");
        var a=(Geometry)Field(renderedFirst,"Geometry");var b=(Geometry)Field(renderedSecond,"Geometry");
        Check(!a.Points.SequenceEqual(b.Points),"remote SVG frame geometry reflects changed path width");
        Check((string)Field(canonical,"SvgSource")==first,"remote frame playback leaves canonical SVG declaration unchanged");
    }
    static void PublishedAnimationPause()
    {
        using var gateway=new GatewayScope{Server=true,Dedicated=true};
        object caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>{
            if(m.Name=="get_EntityId")return 10L;if(m.Name=="get_OwnerId")return 77L;
            if(m.Name=="get_Closed")return false;if(m.Name=="IsSameConstructAs")return true;
            throw new Exception("Unexpected caller method: "+m.Name);});
        object target=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>{
            if(m.Name=="get_EntityId")return 20L;if(m.Name=="get_Closed")return false;
            if(m.Name=="HasPlayerAccess")return true;throw new Exception("Unexpected projector method: "+m.Name);});
        gateway.Install("Entities",(m,a)=>{
            if(m.Name=="GetEntityById")return (long)a[0]==10?caller:(long)a[0]==20?target:null;
            throw new Exception("Unexpected entities method: "+m.Name);});
        var session=new HoloMapSession();var draw=(Func<string,object[],object>)Call(session,"DrawEndpoint",caller);
        draw("target",new[]{target});draw("line",new object[]{"line",0,0,0,1,0,0});
        draw("animate",new object[]{"line","opacity",0,1,2,true,true});
        var published=(HoloSnapshot)Call(session,"CaptureSnapshot");
        SetField(session,"_ticks",30);draw("pause",new object[0]);
        var paused=(HoloSnapshot)Call(session,"CaptureSnapshot");
        Check(!published.Scenes[0].Animations[0].Paused,"published descriptor remains immutable after pause command");
        var delta=(HoloSnapshot)Static("BuildDelta",published,paused,1L);
        Check(delta.Scenes.Count==1&&delta.Scenes[0].Animations[0].Paused,"pause produces animation metadata delta");
        Check(delta.Scenes[0].Items.Count==0,"pause does not resend geometry");
        draw("svg",new object[]{"deferred","not valid SVG",0,0,0});
        var scene=((IDictionary)Field(session,"_scenes"))[20L];var items=(IDictionary)Field(scene,"Items");
        Check(((Geometry)Field(items["10:deferred"],"Geometry")).Triangles.Length==0,"dedicated PB SVG command stores source without invoking parser");
        byte[] packedBytes=System.Text.Encoding.ASCII.GetBytes("HMA1\n1\n1\n1\n<svg/>\n");
        var literalPacked=new List<byte>{72,77,67,49};literalPacked.AddRange(BitConverter.GetBytes(packedBytes.Length));
        for(int i=0;i<packedBytes.Length;i+=8){int count=Math.Min(8,packedBytes.Length-i);literalPacked.Add((byte)((1<<count)-1));literalPacked.AddRange(packedBytes.Skip(i).Take(count));}
        string canonicalPacked="HoloMapAnimation1:"+Convert.ToBase64String(literalPacked.ToArray());
        draw("play",new object[]{"whitespace"," \n"+canonicalPacked+"\r\n "});
        var packedSnapshot=(HoloSnapshot)Call(session,"CaptureSnapshot");
        var stored=packedSnapshot.Scenes[0].Animations.Single(a=>a.Id=="whitespace");
        Check(stored.Packed==canonicalPacked,"packed playback canonicalizes surrounding paste whitespace before replication");
        Call(session,"ValidateDrawAnimations",packedSnapshot.Scenes[0].Animations,20L);
        Check(true,"canonicalized packed playback descriptor passes client validation");
        int beforeOversized=items.Count;
        Reject(()=>draw("play",new object[]{"oversized-whitespace",new string(' ',60001)+canonicalPacked}),"raw packed character limit enforced before trimming whitespace");
        Check(items.Count==beforeOversized&&!items.Contains("10:oversized-whitespace~0"),"oversized whitespace payload leaves active content unchanged");
        var frames=new[]{"<svg/>","<svg/>"+new string(' ',54990)};
        int admitted=0,beforeCount=0;bool refused=false;
        for(int i=0;i<9;i++){beforeCount=items.Count;try{draw("playsvgframes",new object[]{"large"+i,frames,1,0,0,0,0.01,true,3});admitted++;}catch(ArgumentException){refused=true;break;}}
        Check(refused&&admitted>=6&&admitted<=8,"combined animation and reserved UI publication budgets bound large animation admission");
        Check(items.Count==beforeCount&&!items.Contains("10:large"+admitted),"rejected large animation preserves existing display and timelines");
    }
    static object Static(string method,params object[] args)
    {
        try{return typeof(HoloMapSession).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static void DeltaUpdates()
    {
        using var gateway=new GatewayScope();var before=Snapshot();var after=Snapshot();
        var points=new double[512*3];for(int i=0;i<512;i++)points[i*3]=i/512.0;
        before.Scenes[0].Items[0].Points=points;after.Scenes[0].Items[0].Points=(double[])points.Clone();
        after.Scenes[0].Items[0].Opacity=0.5f;after.ServerTick=150;
        var delta=(HoloSnapshot)Static("BuildDelta",before,after,5L);
        Check(delta.Delta&&delta.BaseRevision==5&&delta.Scenes.Count==1,"metadata change creates a revisioned scene delta");
        var update=delta.Scenes[0].Items[0];Check(update.ReuseGeometry&&update.Points==null,"appearance change references existing geometry");
        Check(gateway.Serialize(delta).Length<gateway.Serialize(after).Length/4,"metadata delta avoids transmitting large point arrays");
        var merged=(HoloSnapshot)Static("MergeDelta",before,delta,5L);
        Check(!merged.Delta&&merged.ServerTick==150&&merged.Scenes[0].Items[0].Opacity==0.5f,"delta reconstruction includes latest appearance and clock");
        Check(ReferenceEquals(merged.Scenes[0].Items[0].Points,before.Scenes[0].Items[0].Points),"unchanged geometry reused during reconstruction");
        Check(before.Scenes[0].Items[0].Opacity==null,"merging does not mutate publication baseline");
        Reject(()=>Static("MergeDelta",before,delta,4L),"wrong delta base revision");
        update.Points=new[]{0d,0,0};Reject(()=>Static("MergeDelta",before,delta,5L),"geometry smuggled into metadata-only update");update.Points=null;
        delta.Scenes[0].Items.Add(update);Reject(()=>Static("MergeDelta",before,delta,5L),"duplicate object update");delta.Scenes[0].Items.RemoveAt(1);
        delta.Scenes[0].RemovedItems.Add(new HoloItemKey{CallerId=10,Id="line"});Reject(()=>Static("MergeDelta",before,delta,5L),"object removal conflicts with modification");delta.Scenes[0].RemovedItems.Clear();
        var removed=(HoloSnapshot)Static("BuildDelta",before,new HoloSnapshot(),5L);
        Check(removed.RemovedScenes.SequenceEqual(new[]{20L}),"display deletion has explicit tombstone");
        Check(((HoloSnapshot)Static("MergeDelta",before,removed,5L)).Scenes.Count==0,"display tombstone removes previous scene");
        var replacement=Snapshot(21);var replaced=(HoloSnapshot)Static("MergeDelta",before,Static("BuildDelta",before,replacement,5L),5L);
        Check(replaced.Scenes.Count==1&&replaced.Scenes[0].ConsoleId==21,"new display and old display removal merge together");
        var unchanged=(HoloSnapshot)Static("BuildDelta",before,before,5L);Check(unchanged.Scenes.Count==0,"unchanged displays produce no changed scene payload");
        var session=new HoloMapSession();Call(session,"ApplySnapshot",before);var scenes=(IDictionary)Field(session,"_scenes");object retained=scenes[20L];
        update.Transform[0]=double.NaN;var invalidMerged=(HoloSnapshot)Static("MergeDelta",before,delta,5L);
        Reject(()=>Call(session,"ApplySnapshot",invalidMerged),"invalid metadata in merged delta");
        Check(ReferenceEquals(retained,scenes[20L]),"invalid merged delta preserves live scene atomically");
    }
    static void DeferredGeometry()
    {
        using var gateway=new GatewayScope();var session=new HoloMapSession();var packet=Snapshot();
        packet.Scenes[0].Items[0].Declaration=new HoloGeometryDeclaration{
            SvgSource="<svg viewBox='0 0 10 10'><rect width='10' height='10' fill='cyan'/></svg>",SvgSegments=3};
        Call(session,"ApplySnapshot",packet);
        object scene=((IDictionary)Field(session,"_scenes"))[20L];object item=((IDictionary)Field(scene,"Items"))["10:line"];
        Check(((Geometry)Field(item,"Geometry")).Triangles.Length==0,"snapshot retains declarative SVG before client compilation");
        var compile=typeof(HoloMapSession).GetMethod("ClientDisplayItem",BindingFlags.NonPublic|BindingFlags.Instance);
        object Compile(int allowance,out int left){var args=new[]{scene,item,(object)allowance};compile.Invoke(session,args);if(allowance>0)Call(session,"CompileQueuedDisplay");object result=compile.Invoke(session,args);left=(int)args[2];return result;}
        gateway.Server=true;gateway.Dedicated=true;session.Draw();
        Check(((IDictionary)Field(session,"_clientGeometry")).Count==0,"dedicated server Draw never compiles display geometry");
        gateway.Server=false;gateway.Dedicated=false;
        object rendered=Compile(1,out int remaining);
        Check(rendered!=null&&((Geometry)Field(rendered,"Geometry")).Triangles.Length>0,"viewing client compiles declarative SVG");
        Check((int)Field(session,"_lastCompileTick")==0,"compilation consumes this frame's scheduled work slot");
        Check(((Geometry)Field(item,"Geometry")).Triangles.Length==0,"client compilation does not overwrite canonical mesh");
        object reused=Compile(0,out remaining);
        Check(reused!=null&&ReferenceEquals(Field(reused,"Geometry"),Field(rendered,"Geometry")),"cached display renders without another compilation slot");
        SetField(item,"SvgSource","<svg><path d='M '/></svg>");
        SetField(session,"_ticks",1);
        object fallback=Compile(1,out remaining);
        Check(fallback!=null&&ReferenceEquals(Field(fallback,"Geometry"),Field(rendered,"Geometry")),"malformed artwork retains last valid client mesh");
        SetField(session,"_ticks",2);Compile(1,out remaining);Check((int)Field(session,"_lastCompileTick")==1,"failed unchanged artwork is not recompiled every frame");
        var invalid=Snapshot();invalid.Scenes[0].Items[0].Declaration=new HoloGeometryDeclaration{SvgSource=new string('x',Svg.MaxCharacters+1),SvgSegments=3};
        Reject(()=>Call(session,"ApplySnapshot",invalid),"oversized declarative SVG source");
        invalid=Snapshot();invalid.Scenes[0].Items[0].Declaration=new HoloGeometryDeclaration{SvgSource="<svg/>",SvgSegments=3,Text="bad",TextHeight=1,TextAnchor="start"};
        Reject(()=>Call(session,"ApplySnapshot",invalid),"conflicting declaration source types");
    }
    static void GeometryWorkLimits()
    {
        using(GeometryWork.Begin(2))
        {
            GeometryWork.Charge();
            using(GeometryWork.Begin())GeometryWork.Charge();
            Reject(()=>GeometryWork.Charge(),"nested geometry scopes share the outer work budget");
        }
        var degenerate=MeshEffects.Solid(new Geometry(new[]{Vector3D.Zero,Vector3D.Zero,Vector3D.Zero},new int[0],new[]{0,1,2}),Vector4.One,Vector4.One);
        using(GeometryWork.Begin(5))
            Reject(()=>MeshEffects.Gradient(degenerate,p=>Vector4.One,8),"degenerate zero-output triangles still consume compilation work");
        string disjoint="<svg viewBox='0 0 100 100'><defs><clipPath id='c'><rect x='80' y='80' width='10' height='10'/><rect x='70' y='70' width='5' height='5'/></clipPath></defs><g clip-path='url(#c)'><rect width='10' height='10'/><rect x='15' width='10' height='10'/><rect x='30' width='10' height='10'/></g></svg>";
        using(GeometryWork.Begin(10))Reject(()=>Svg.Parse(disjoint,3),"disjoint clipping work bounded even without visible output");
        var normal=Svg.Parse("<svg viewBox='0 0 10 10'><rect width='10' height='10' fill='cyan'/></svg>",3);
        Check(normal.Geometry.Triangles.Length>0,"work exhaustion disposal allows later ordinary SVG compilation");
    }
    static HoloAnimationData Animation()=>new HoloAnimationData{
        CallerId=10,ConsoleId=20,Id="line",Base=Item().Transform,StartTick=60,
        Tracks=new List<HoloAnimationTrack>{new HoloAnimationTrack{Channel="opacity",From=0,To=1,Duration=2,Loop=true,Ping=true}}};
    static void AnimationDeclarations()
    {
        using var gateway=new GatewayScope();var session=new HoloMapSession();Call(session,"ApplySnapshot",Snapshot());
        var scene=((IDictionary)Field(session,"_scenes"))[20L];var item=((IDictionary)Field(scene,"Items"))["10:line"];
        var declaration=Animation();var list=new List<HoloAnimationData>{declaration};
        Call(session,"ValidateDrawAnimations",list,20L);Call(session,"ImportDrawAnimations",list,20L);
        SetField(session,"_ticks",5);Call(session,"SetAnimationClock",120L);
        object rendered=Call(session,"GetAnimatedItem",20L,item);
        Check((float)Field(rendered,"Opacity")==0.5f,"late join evaluates current phase using server clock");
        SetField(session,"_ticks",6);rendered=Call(session,"GetAnimatedItem",20L,item);
        Check((float)Field(rendered,"Opacity")>0.5f,"remote animation advances between network packets");
        Check((float)Field(item,"Opacity")==1,"remote playback leaves canonical item unchanged");
        declaration.Paused=true;declaration.PauseTick=120;
        SetField(session,"_ticks",600);rendered=Call(session,"GetAnimatedItem",20L,item);
        Check((float)Field(rendered,"Opacity")==0.5f,"replicated pause freezes phase independently of client clock");
        void Invalid(Action<HoloAnimationData> mutate,string name){var bad=Animation();mutate(bad);Reject(()=>Call(session,"ValidateDrawAnimations",new List<HoloAnimationData>{bad},20L),name);}
        Invalid(d=>d.ConsoleId=21,"animation target mismatch");
        Invalid(d=>d.CallerId=0,"animation caller missing");
        Invalid(d=>d.StartTick=-1,"negative animation clock");
        Invalid(d=>d.Tracks[0].Channel="execute","arbitrary animation operation");
        Invalid(d=>d.Tracks[0].Duration=double.NaN,"nonfinite animation duration");
        Invalid(d=>d.Tracks.Add(d.Tracks[0]),"duplicate animation channel");
        Invalid(d=>{d.Frames=new[]{"<svg/>"};d.Packed="HoloMapAnimation1:AAAA";d.Fps=1;d.Segments=3;},"multiple animation source formats");
        Invalid(d=>d.Packed="untrusted executable payload","packed animation header");
        Call(session,"ImportDrawAnimations",new List<HoloAnimationData>(),20L);
        Check(ReferenceEquals(Call(session,"GetAnimatedItem",20L,item),item),"removed animation stops local evaluation");
        Check(((IDictionary)Field(session,"_remoteAnimations")).Count==0,"animation removal releases declarations");
    }
    internal sealed class GatewayScope:IDisposable
    {
        readonly List<(MemberInfo Member,object Old)> _old=new List<(MemberInfo,object)>();
        public bool Server,Dedicated;
        public ulong ServerId=100;
        public int Requests,Deserializations;
        public List<(string Method,byte[] Data,ulong Recipient)> Sent=new List<(string,byte[],ulong)>();
        readonly ProtoBuf.Meta.RuntimeTypeModel _model=ProtoBuf.Meta.RuntimeTypeModel.Create();
        public GatewayScope()
        {
            Install("Multiplayer",(m,a)=>{
                if(m.Name=="get_IsServer")return Server;
                if(m.Name=="get_ServerId")return ServerId;
                if(m.Name=="SendMessageToServer"){Requests++;return true;}
                if(m.Name.StartsWith("SendMessageTo")){Sent.Add((m.Name,(byte[])a[1],m.Name=="SendMessageTo"?(ulong)a[2]:0));return true;}
                throw new Exception("Unexpected multiplayer method: "+m.Name);
            });
            Install("Utilities",(m,a)=>{
                if(m.Name=="get_IsDedicated")return Dedicated;
                if(m.Name=="SerializeToBinary")return Serialize(a[0]);
                if(m.Name=="SerializeFromBinary"){
                    Deserializations++;using var input=new MemoryStream((byte[])a[0]);
                    return _model.Deserialize(input,null,m.GetGenericArguments()[0]);
                }
                throw new Exception("Unexpected utility method: "+m.Name);
            });
        }
        public byte[] Serialize(object value){using var output=new MemoryStream();_model.Serialize(output,value);return output.ToArray();}
        public void Install(string name,Func<MethodInfo,object[],object> handler)
        {
            MemberInfo member=(MemberInfo)typeof(MyAPIGateway).GetField(name)??typeof(MyAPIGateway).GetProperty(name);
            object old=member is FieldInfo f?f.GetValue(null):((PropertyInfo)member).GetValue(null);
            Type type=member is FieldInfo field?field.FieldType:((PropertyInfo)member).PropertyType;
            _old.Add((member,old));Write(member,DrawTestProxy.Make(type,handler));
        }
        static void Write(MemberInfo member,object value){if(member is FieldInfo field)field.SetValue(null,value);else ((PropertyInfo)member).SetValue(null,value);}
        public void Dispose(){for(int i=_old.Count-1;i>=0;i--)Write(_old[i].Member,_old[i].Old);}
    }
    static void ReceiverAuthentication()
    {
        using var gateway=new GatewayScope();var session=new HoloMapSession();
        byte[] body=gateway.Serialize(Snapshot());
        int protocol=(int)typeof(HoloMapSession).GetField("NetworkProtocol",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
        var frame=new HoloFrame{Protocol=protocol,Revision=1,Index=0,Count=1,Length=body.Length,Payload=body};
        byte[] bytes=gateway.Serialize(frame);
        void Receive(byte[] payload,ulong sender,bool fromServer)=>Call(session,"Receive",(ushort)49783,payload,sender,fromServer);
        var scenes=(IDictionary)Field(session,"_scenes");
        Receive(bytes,200,false);Check(scenes.Count==0,"peer cannot provide geometry");
        Check(gateway.Deserializations==0,"peer content rejected before deserialization");
        Receive(bytes,200,true);Check(scenes.Count==0&&gateway.Deserializations==0,"forged server flag cannot bypass sender identity");
        frame.Protocol=protocol+1;Receive(gateway.Serialize(frame),100,true);
        Check(scenes.Count==0,"incompatible protocol rejected");frame.Protocol=protocol;
        Receive(new byte[4097],100,true);Check(scenes.Count==0,"oversized packet ignored");
        Receive(bytes,100,true);Check(scenes.Count==1,"authenticated server snapshot accepted");
        object first=scenes[20L];Receive(bytes,100,true);
        Check(ReferenceEquals(first,scenes[20L]),"replayed revision cannot replace scene");
        frame.Revision=2;frame.Index=1;Receive(gateway.Serialize(frame),100,true);
        Check(ReferenceEquals(first,scenes[20L]),"invalid chunk index cannot alter scene");
        frame.Index=0;frame.Length=2*1024*1024+1;Receive(gateway.Serialize(frame),100,true);
        Check(ReferenceEquals(first,scenes[20L]),"snapshot size cap prevents assembly allocation");
        Check(((IDictionary)Field(session,"_assemblies")).Count==0,"invalid packets leave no partial assemblies");
        var missing=new HoloSnapshot{Delta=true,BaseRevision=77,ServerTick=600};body=gateway.Serialize(missing);
        frame=new HoloFrame{Protocol=protocol,Revision=78,BaseRevision=77,Delta=true,Index=0,Count=1,Length=body.Length,Payload=body};
        Receive(gateway.Serialize(frame),100,true);
        Check(gateway.Requests==1&&(bool)Field(session,"_needsSnapshot"),"missing delta baseline requests full resynchronization");
        Check((long)Field(session,"_receivedRevision")==1&&ReferenceEquals(first,scenes[20L]),"missing baseline does not advance revision or alter scene");
        Receive(gateway.Serialize(frame),100,true);Check(gateway.Requests==1,"repeated missing baselines cannot flood snapshot requests");
        SetField(session,"_ticks",120);Receive(gateway.Serialize(frame),100,true);Check(gateway.Requests==2,"resynchronization retries after bounded interval");
        void Full(HoloSnapshot snapshot,long revision,ulong sender)
        {
            byte[] content=gateway.Serialize(snapshot);
            Receive(gateway.Serialize(new HoloFrame{Protocol=protocol,Revision=revision,Index=0,Count=1,Length=content.Length,Payload=content}),sender,true);
        }
        var animated=Snapshot();animated.ServerTick=200;animated.Scenes[0].Animations.Add(Animation());
        Full(animated,2,0);Check((long)Field(session,"_receivedRevision")==2,"dedicated server zero sender accepted with authenticated server flag");
        Check(((IDictionary)Field(session,"_remoteAnimations")).Count==1,"receiver imports animation declarations");
        var adjusted=Snapshot();adjusted.ServerTick=220;adjusted.Scenes[0].Animations.Add(Animation());adjusted.Scenes[0].Items[0].Opacity=0.4f;
        var adjustment=(HoloSnapshot)Static("BuildDelta",animated,adjusted,2L);body=gateway.Serialize(adjustment);
        Receive(gateway.Serialize(new HoloFrame{Protocol=protocol,Revision=3,BaseRevision=2,Delta=true,Index=0,Count=1,Length=body.Length,Payload=body}),100,true);
        var adjustedItem=((IDictionary)Field(scenes[20L],"Items"))["10:line"];
        Check((long)Field(session,"_receivedRevision")==3&&(float)Field(adjustedItem,"Opacity")==0.4f,"authenticated metadata delta reaches live scene through actual receiver");
        object second=scenes[20L];var badClock=Snapshot();badClock.ServerTick=(long)int.MaxValue+1;
        Full(badClock,4,100);
        Check((long)Field(session,"_receivedRevision")==3&&ReferenceEquals(second,scenes[20L]),"invalid server clock cannot partially commit scene or revision");
        Check(((IDictionary)Field(session,"_remoteAnimations")).Count==1,"invalid publication preserves prior animation declarations");
        Full(new HoloSnapshot(),5,100);
        Check(scenes.Count==0&&((IDictionary)Field(session,"_remoteAnimations")).Count==0,"removed display purges both scene and remote animation state");
    }
    static void AtomicBounds()
    {
        var session=new HoloMapSession();Call(session,"ApplySnapshot",Snapshot());
        var scenes=(IDictionary)Field(session,"_scenes");object retained=scenes[20L];
        void Invalid(Action<HoloSnapshot> mutate,string name)
        {
            var packet=Snapshot();mutate(packet);Reject(()=>Call(session,"ApplySnapshot",packet),name);
            Check(scenes.Count==1&&ReferenceEquals(retained,scenes[20L]),name+" preserves last valid scene atomically");
        }
        Invalid(s=>s.Scenes.Add(Scene(20)),"duplicate scene id");
        Invalid(s=>s.Scenes[0].Items.Add(Item()),"duplicate object id");
        Invalid(s=>s.Scenes[0].Items[0].Points[0]=double.PositiveInfinity,"infinite point");
        Invalid(s=>s.Scenes[0].Items[0].Edges=new[]{-1,0},"negative vertex index");
        Invalid(s=>s.Scenes[0].Items[0].Edges=new[]{0,1,0},"incomplete edge");
        Invalid(s=>s.Scenes[0].Items[0].Transform[0]=double.NaN,"nonfinite transform");
        Invalid(s=>s.Scenes[0].Items[0].Opacity=1.01f,"opacity outside range");
        Invalid(s=>s.Scenes[0].Items[0].Emission=21,"emission outside range");
        Invalid(s=>s.Scenes[0].Items[0].TriangleColors=new[]{1f},"color cardinality mismatch");
        Invalid(s=>s.Scenes[0].Items[0].Points=new double[(Geometry.MaxPoints+1)*3],"per-object point budget");
        Invalid(s=>{for(int i=0;i<17;i++)s.Scenes[0].Items.Add(Item("extra"+i));},"scene object budget");
        Invalid(s=>{for(int i=0;i<9;i++)s.Scenes.Add(Scene(100+i));},"display budget");
        Invalid(s=>{var i=s.Scenes[0].Items[0];i.Material="Other_Mod_Material";i.UV=new[]{0f,0,1,1};},"unregistered material namespace");
        Invalid(s=>{var i=s.Scenes[0].Items[0];i.Material="HoloMap_Image_Demo";i.UV=new[]{0f,0,2,1};},"texture coordinate bounds");
        Call(session,"ApplySnapshot",new HoloSnapshot());Check(scenes.Count==0,"empty snapshot clears removed displays");
    }
}

