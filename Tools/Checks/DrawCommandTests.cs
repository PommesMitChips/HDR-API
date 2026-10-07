using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRageMath;

// Offline gateway doubles exercise the actual mod endpoint, including authorization.
public class DrawTestProxy:DispatchProxy
{
    public Func<MethodInfo,object[],object> Handler;
    public Func<string> ProgramSource;
    protected override object Invoke(MethodInfo method,object[] args){if(method.Name=="get_ProgramData")return ProgramSource==null?"test-program":ProgramSource();return Handler(method,args);}
    public static object Make(Type type,Func<MethodInfo,object[],object> handler)
    {var proxy=Create(type,typeof(DrawTestProxy));((DrawTestProxy)proxy).Handler=handler;return proxy;}
}
internal static class DrawCommandTests
{
    public static int Run()
    {
        int count=0;void Check(bool condition,string name){if(!condition)throw new Exception(name);count++;}
        void Reject(Action action,string name){try{action();}catch(ArgumentException){count++;return;}throw new Exception("Expected command rejection: "+name);}
        var gateway=typeof(MyAPIGateway);MemberInfo multi=(MemberInfo)gateway.GetField("Multiplayer")??gateway.GetProperty("Multiplayer");MemberInfo entities=(MemberInfo)gateway.GetField("Entities")??gateway.GetProperty("Entities");
        object Read(MemberInfo member)=>member is FieldInfo f?f.GetValue(null):((PropertyInfo)member).GetValue(null);
        void Write(MemberInfo member,object value){if(member is FieldInfo f)f.SetValue(null,value);else ((PropertyInfo)member).SetValue(null,value);}
        Type Kind(MemberInfo member)=>member is FieldInfo f?f.FieldType:((PropertyInfo)member).PropertyType;
        object oldMulti=Read(multi),oldEntities=Read(entities);
        bool access=true,server=true;var registered=new Dictionary<long,object>();
        object caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>
        {
            if(m.Name=="get_EntityId")return 10L;if(m.Name=="get_OwnerId")return 77L;
            if(m.Name=="get_Closed")return false;if(m.Name=="IsSameConstructAs")return true;
            throw new Exception("Unexpected caller method: "+m.Name);
        });
        object target=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>
        {
            if(m.Name=="get_EntityId")return 20L;if(m.Name=="get_Closed")return false;
            if(m.Name=="HasPlayerAccess")return access;
            throw new Exception("Unexpected target method: "+m.Name);
        });
        registered.Add(10,caller);registered.Add(20,target);
        try
        {
            Write(multi,DrawTestProxy.Make(Kind(multi),(m,a)=>{if(m.Name=="get_IsServer")return server;throw new Exception("Unexpected multiplayer method: "+m.Name);}));
            Write(entities,DrawTestProxy.Make(Kind(entities),(m,a)=>{if(m.Name=="GetEntityById"){object value;return registered.TryGetValue((long)a[0],out value)?value:null;}throw new Exception("Unexpected entities method: "+m.Name);}));
            var session=new HoloMapSession();var endpoint=typeof(HoloMapSession).GetMethod("DrawEndpoint",BindingFlags.NonPublic|BindingFlags.Instance);
            var draw=(Func<string,object[],object>)endpoint.Invoke(session,new[]{caller});
            Check((string)typeof(HoloMapSession).GetField("DrawPropertyId",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue()=="HDR.Draw","short drawing property uses the HDR API identity");
            Check((string)typeof(HoloMapSession).GetField("PropertyId",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue()=="HDR.Api","typed drawing property uses the HDR API identity");
            Check((string)draw("version",new object[0])=="HDR.Draw/1","drawing endpoint version");
            Reject(()=>draw("line",new object[]{"line",0,0,0,1,0,0}),"draw without target");
            draw("target",new[]{target});
            draw("line",new object[]{"line",0,0,0,1,0,0,"cyan"});
            var scenes=(System.Collections.IDictionary)typeof(HoloMapSession).GetField("_scenes",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(session);
            var scene=scenes[20L];var items=(System.Collections.IDictionary)scene.GetType().GetField("Items").GetValue(scene);
            var item=items["10:line"];var geometry=(Geometry)item.GetType().GetField("Geometry").GetValue(item);
            Check(geometry.Points[0]==Vector3D.Zero&&geometry.Points[1]==Vector3D.UnitX,"short scalar line command builds actual geometry");
            Check((Vector4)item.GetType().GetField("LineColor").GetValue(item)==new Vector4(0,1,1,1),"named color applied by mod");
            draw("circle",new object[]{"circle",0,0,0,0.5,"orange"});
            Check(items.Contains("10:circle"),"short circle command");
            draw("text",new object[]{"text","HI",0,0.5,0});Check(items.Contains("10:text"),"short text command");
            draw("transparency",new object[]{"line",0.25});Check((float)item.GetType().GetField("Opacity").GetValue(item)==0.75f,"transparency command");
            draw("emission",new object[]{"line",2});Check((float)item.GetType().GetField("Emission").GetValue(item)==2,"emission command");
            draw("layer",new object[]{"line","hud"});Check((bool)draw("toggle",new object[]{"hud"})==false,"layer toggle command returns state");
            var state=(VRage.MyTuple<bool,float>)draw("layer-state",new object[]{"hud"});Check(!state.Item1&&state.Item2==1,"layer query returns sandbox-compatible tuple");
            var matrix=(MatrixD)draw("pose",new object[]{1,2,3,0.5});Check(matrix.Translation==new Vector3D(1,2,3)&&matrix.Right.Length()==0.5,"pose command returns SDK matrix");
            Reject(()=>draw("line",new object[]{"bad",0,0,0,1,0,0,"cyan",0.01,123}),"extra drawing arguments");
            Reject(()=>draw("line",new object[]{"bad",double.NaN,0,0,1,0,0}),"nonfinite geometry argument");
            Reject(()=>draw("execute",new object[]{"untrusted code"}),"unknown command rejected");
            Reject(()=>draw("svg",new object[]{"bad",new object()}),"wrong argument type");
            int callbacks=0;access=false;
            Reject(()=>draw("curve",new object[]{"bad",new Func<double,Vector3D>(t=>{callbacks++;return Vector3D.Zero;}),0,1}),"lost ownership rejected");
            Check(callbacks==0,"unauthorized calls never execute curve callbacks");
            access=true;server=false;Reject(()=>draw("line",new object[]{"bad",0,0,0,1,0,0}),"client commands rejected");server=true;
            registered.Remove(10);Reject(()=>draw("line",new object[]{"bad",0,0,0,1,0,0}),"unregistered caller rejected");registered.Add(10,caller);
            Check(!items.Contains("10:bad"),"rejected commands preserve scene");
            draw("animate",new object[]{"line","opacity",0,1,1,true,true});
            ClientReplicationTests.SetField(session,"_ticks",1);
            ClientReplicationTests.SetField(session,"_dirty",false);
            typeof(HoloMapSession).GetMethod("TickDrawAnimations",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(session,null);
            object rendered=ClientReplicationTests.Call(session,"GetAnimatedItem",20L,item);
            float renderedOpacity=(float)ClientReplicationTests.Field(rendered,"Opacity");
            Check(renderedOpacity>0&&renderedOpacity<0.02,"client render evaluates animation without PB callback");
            Check((float)ClientReplicationTests.Field(item,"Opacity")==0.75f,"animation preserves canonical server appearance");
            Check(!(bool)ClientReplicationTests.Field(session,"_dirty"),"animation tick does not dirty replication every frame");
            draw("pause",new object[0]);float before=renderedOpacity;
            ClientReplicationTests.SetField(session,"_ticks",61);
            typeof(HoloMapSession).GetMethod("TickDrawAnimations",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(session,null);
            rendered=ClientReplicationTests.Call(session,"GetAnimatedItem",20L,item);
            Check((float)ClientReplicationTests.Field(rendered,"Opacity")==before,"pause preserves client animation time");
            draw("clear",new object[0]);Check(items.Count==0,"clear removes caller content");
        }
        finally{Write(multi,oldMulti);Write(entities,oldEntities);}
        return count+TargetDiscovery();
    }
    static int TargetDiscovery()
    {
        int count=0;void Check(bool ok,string clause){if(!ok)throw new Exception("Display discovery: "+clause);count++;}
        using var gateway=new ClientReplicationTests.GatewayScope{Server=true};
        var entities=new Dictionary<long,object>();var candidates=new List<object>();
        object caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>m.Name switch
        {
            "get_EntityId"=>10L,"get_OwnerId"=>77L,"get_Closed"=>false,"get_CubeGrid"=>null,
            "GetPosition"=>Vector3D.Zero,"IsSameConstructAs"=>true,_=>throw new Exception("Discovery caller "+m.Name)
        });
        object Block(Type type,long id,double distance,bool same=true)
        {
            var block=DrawTestProxy.Make(type,(m,a)=>m.Name switch
            {
                "get_EntityId"=>id,"get_CustomName"=>"HDR Display","get_Closed"=>false,
                "GetPosition"=>new Vector3D(distance,0,0),"IsSameConstructAs"=>same,"HasPlayerAccess"=>true,
                _=>throw new Exception("Discovery block "+m.Name)
            });
            entities[id]=block;candidates.Add(block);return block;
        }
        entities[10]=caller;
        var lcd=Block(typeof(IMyTextPanel),21,.1);
        var unrelated=Block(typeof(IMyTerminalBlock),23,.2);
        var foreign=Block(typeof(IMyProjector),24,.3,false);
        var farther=Block(typeof(IMyProjector),22,3);
        var projector=Block(typeof(IMyProjector),20,2);
        gateway.Install("Entities",(m,a)=>m.Name=="GetEntityById"&&entities.TryGetValue((long)a[0],out var block)?block:null);
        gateway.Install("TerminalActionsHelper",(m,a)=>
        {
            if(m.Name!="GetTerminalSystemForGrid")throw new Exception("Discovery helper "+m.Name);
            return DrawTestProxy.Make(m.ReturnType,(method,args)=>
            {
                if(method.Name!="GetBlocksOfType")throw new Exception("Discovery terminal "+method.Name);
                var result=(System.Collections.IList)args[0];var filter=(Delegate)args[1];
                foreach(var item in candidates)if((bool)filter.DynamicInvoke(item))result.Add(item);
                return null;
            });
        });
        var session=new HoloMapSession();var draw=(Func<string,object[],object>)ClientReplicationTests.Call(session,"DrawEndpoint",caller);
        Check(ReferenceEquals(draw("findtarget",new object[]{"HDR Display"}),projector),"projector lookup skips closer LCD and other-construct blocks sharing its name");
        draw("line",new object[]{"probe",0,0,0,1,0,0});
        var scenes=(System.Collections.IDictionary)ClientReplicationTests.Field(session,"_scenes");
        Check(scenes.Contains(20L)&&!scenes.Contains(21L),"findtarget also selects the drawing context for the projector");
        Check(ReferenceEquals(draw("finddisplay",new object[]{"HDR Display"}),lcd),"general display lookup still accepts the closest LCD");
        candidates.Remove(projector);candidates.Remove(farther);
        Check(draw("findtarget",new object[]{"HDR Display"})==null,"no eligible projector returns null instead of an LCD");
        try{draw("line",new object[]{"probe",0,0,0,1,0,0});throw new Exception("Missing anchor retained the previous target.");}
        catch(ArgumentException){count++;}
        return count;
    }
}

