using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;
using VRage;

public class LocalSourceProxy:DispatchProxy
{
    public Func<MethodInfo,object[],object> Handler;
    protected override object Invoke(MethodInfo method,object[] args){return Handler(method,args);}
    public static object Make(Type type,Func<MethodInfo,object[],object> handler)
    {var proxy=Create(type,typeof(LocalSourceProxy));((LocalSourceProxy)proxy).Handler=handler;return proxy;}
}

// Executes the built plugin endpoint and its actual physical authorization
// adapter. The existing backend's acquire/valid/release calls are intercepted;
// renderer readiness and pixel allowance are supplied without a device.
static class ActualEndpointChecks
{
    const BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
    static Type leaseType;
    static int acquisitions,retirements;
    static bool Ready(ref bool __result){__result=true;return false;}
    static bool Pixels(ref int __result){__result=262144;return false;}
    static bool Acquire(object[] __args,ref object __result)
    {
        acquisitions++;object lease=Activator.CreateInstance(leaseType,true);
        leaseType.GetField("Material",Instance).SetValue(lease,"HDR_ClientLcd_0");
        leaseType.GetField("Width",Instance).SetValue(lease,(int)__args[6]);
        leaseType.GetField("Height",Instance).SetValue(lease,(int)__args[7]);
        leaseType.GetField("Version",Instance).SetValue(lease,acquisitions);
        __result=lease;return false;
    }
    static bool Retire(ref bool __result){retirements++;__result=true;return false;}
    static MemberInfo Member(Type type,string name){return (MemberInfo)type.GetField(name,Static)??type.GetProperty(name,Static);}
    static Type Kind(MemberInfo member){return member is FieldInfo f?f.FieldType:((PropertyInfo)member).PropertyType;}
    static object Read(MemberInfo member){return member is FieldInfo f?f.GetValue(null):((PropertyInfo)member).GetValue(null);}
    static void Write(MemberInfo member,object value){if(member is FieldInfo f)f.SetValue(null,value);else ((PropertyInfo)member).SetValue(null,value);}
    internal static void Run()
    {
        string bin=Environment.GetEnvironmentVariable("SE_BIN")??@"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
        AssemblyLoadContext.Default.Resolving+=(context,name)=>{string path=Path.Combine(bin,name.Name+".dll");return File.Exists(path)?context.LoadFromAssemblyPath(path):null;};
        string pluginPath=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../bin/Release/net10.0/HDRClientRenderer.dll"));
        var plugin=AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);var type=plugin.GetType("HDRClientRenderer.HDRClientRendererPlugin",true);
        var lcd=plugin.GetType("HDRClientRenderer.LcdStore",true);leaseType=lcd.GetNestedType("Lease",BindingFlags.NonPublic);
        var gateway=Assembly.Load("Sandbox.Common").GetType("Sandbox.ModAPI.MyAPIGateway",true);
        var entities=Member(gateway,"Entities");var session=Member(gateway,"Session");object oldEntities=Read(entities),oldSession=Read(session);
        var harmony=new Harmony("HDR.Tests.LocalModSourceEndpoint");
        int checks=0;void Check(bool condition,string name){checks++;if(!condition)throw new Exception("Actual local source endpoint: "+name);}
        bool access=true,ownerAccess=true,sourceWorking=true,sameConstruct=true,anchorWorking=true,character=true;double distance=0;
        var table=new Dictionary<long,object>();
        var blockType=Assembly.Load("Sandbox.Common").GetType("Sandbox.ModAPI.IMyTerminalBlock",true);
        object anchor=LocalSourceProxy.Make(blockType,(method,args)=>
        {
            if(method.Name=="get_EntityId")return 20L;if(method.Name=="get_OwnerId")return 77L;
            if(method.Name=="get_Closed")return false;if(method.Name=="get_IsWorking")return anchorWorking;
            if(method.Name=="HasPlayerAccess")return access;if(method.Name=="IsSameConstructAs")return sameConstruct;
            if(method.Name=="GetPosition")return Activator.CreateInstance(Assembly.Load("VRage.Math").GetType("VRageMath.Vector3D"));
            throw new Exception("Unexpected anchor call: "+method.Name);
        });
        object source=LocalSourceProxy.Make(blockType,(method,args)=>
        {
            if(method.Name=="get_EntityId")return 42L;if(method.Name=="get_Closed")return false;if(method.Name=="get_IsWorking")return sourceWorking;
            if(method.Name=="HasPlayerAccess")return (long)args[0]==77?ownerAccess:access;
            throw new Exception("Unexpected source call: "+method.Name);
        });
        table.Add(20,anchor);table.Add(42,source);
        var playerType=Kind(session).GetProperty("Player").PropertyType;var characterType=playerType.GetProperty("Character").PropertyType;
        object body=LocalSourceProxy.Make(characterType,(method,args)=>
        {if(method.Name=="GetPosition")return Activator.CreateInstance(Assembly.Load("VRage.Math").GetType("VRageMath.Vector3D"),new object[]{distance,0d,0d});throw new Exception("Unexpected character call: "+method.Name);});
        object player=LocalSourceProxy.Make(playerType,(method,args)=>
        {if(method.Name=="get_IdentityId")return 88L;if(method.Name=="get_Character")return character?body:null;throw new Exception("Unexpected player call: "+method.Name);});
        object pluginInstance=null;
        try
        {
            Write(entities,LocalSourceProxy.Make(Kind(entities),(method,args)=>{if(method.Name=="GetEntityById")return table.TryGetValue((long)args[0],out var entity)?entity:null;throw new Exception("Unexpected entity call: "+method.Name);}));
            Write(session,LocalSourceProxy.Make(Kind(session),(method,args)=>{if(method.Name=="get_Player")return player;throw new Exception("Unexpected session call: "+method.Name);}));
            var prefix=new HarmonyMethod(typeof(ActualEndpointChecks).GetMethod(nameof(Ready),Static));
            harmony.Patch(type.GetMethod("Ready",Static),prefix:prefix);
            harmony.Patch(plugin.GetType("HDRClientRenderer.NativeTextureCopy").GetProperty("Ready",Instance).GetMethod,prefix:prefix);
            harmony.Patch(plugin.GetType("HDRClientRenderer.ClientPixelBudget").GetProperty("PixelLimit",Instance).GetMethod,prefix:new HarmonyMethod(typeof(ActualEndpointChecks).GetMethod(nameof(Pixels),Static)));
            harmony.Patch(lcd.GetMethods(Instance).Single(method=>method.Name=="Acquire"&&method.GetParameters().Length==8),prefix:new HarmonyMethod(typeof(ActualEndpointChecks).GetMethod(nameof(Acquire),Static)));
            harmony.Patch(lcd.GetMethod("Valid",Instance),prefix:prefix);
            harmony.Patch(lcd.GetMethod("Release",Instance),prefix:new HarmonyMethod(typeof(ActualEndpointChecks).GetMethod(nameof(Retire),Static)));
            pluginInstance=Activator.CreateInstance(type);
            type.GetField("registered",Instance).SetValue(pluginInstance,true);type.GetField("gameThreadId",Instance).SetValue(pluginInstance,Environment.CurrentManagedThreadId);
            object binding=new object(),declaration=new object();long generation=1;
            Func<object,object[]> describe=token=>!ReferenceEquals(token,binding)?null:new object[]{1,"local-mod",new MyTuple<string,long,long,long,long>("actual",generation,1,1,1),anchor,"lcd-texture","42:0",new object[]{source},new MyTuple<int,int,double,bool>(256,144,30,true),null,declaration};
            Func<string,object[],object> service=(command,args)=>command=="local-mod-native-consumers"?describe:null;
            type.GetField("hdrService",Instance).SetValue(pluginInstance,service);type.GetMethod("NegotiateModSourceConsumers",Instance).Invoke(pluginInstance,null);
            var endpoint=(Func<string,object[],object>)Delegate.CreateDelegate(typeof(Func<string,object[],object>),pluginInstance,type.GetMethod("ModSourceEndpoint",Instance));
            object Frame()=>endpoint("frame",new object[]{binding,256,144});
            Check(Frame() is MyTuple<int,object,object,bool,double> && acquisitions==1,"authorized real anchor/LCD reaches existing backend without a PB entity");
            var frame=(MyTuple<int,object,object,bool,double>)Frame();Check((bool)endpoint("valid",new[]{binding,frame.Item3}),"actual endpoint retains current native evidence");
            int prior=acquisitions;access=false;Check(Frame()==null&&acquisitions==prior,"viewer access revocation blocks backend work");access=true;
            ownerAccess=false;Check(Frame()==null&&acquisitions==prior,"real anchor ownership cannot grant an inaccessible source");ownerAccess=true;
            sameConstruct=false;Check(Frame()==null&&acquisitions==prior,"source construct mismatch blocks backend work");sameConstruct=true;
            sourceWorking=false;Check(Frame()==null&&acquisitions==prior,"disabled physical source blocks backend work");sourceWorking=true;
            anchorWorking=false;Check(Frame()==null&&acquisitions==prior,"disabled real anchor blocks backend work");anchorWorking=true;
            character=false;Check(Frame()==null&&acquisitions==prior,"missing viewer character blocks backend work");character=true;
            distance=61;Check(Frame()==null&&acquisitions==prior,"existing60m access bound applies to local consumers");distance=0;
            table.Remove(42);Check(Frame()==null&&acquisitions==prior,"source removal blocks backend work");table.Add(42,source);
            frame=(MyTuple<int,object,object,bool,double>)Frame();generation++;
            Check(!(bool)endpoint("valid",new[]{binding,frame.Item3})&&retirements>=3,"current Core owner replacement retires actual backend leases");
            Frame();type.GetMethod("LeaveModSourceConsumers",Instance).Invoke(pluginInstance,null);
            Check(Frame()==null,"world leave drops negotiated authority before more backend work");
            Console.WriteLine($"Actual built plugin local consumer checks passed: {checks} assertions; LCD acquire/valid/release intercepted, no GPU work.");
        }
        finally{harmony.UnpatchAll(harmony.Id);Write(entities,oldEntities);Write(session,oldSession);}
    }
}
