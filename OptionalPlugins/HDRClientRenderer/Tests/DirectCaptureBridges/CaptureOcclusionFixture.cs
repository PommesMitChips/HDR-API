using System.Reflection;
using System.Runtime.CompilerServices;
using VRageMath;

static class CaptureOcclusionFixture
{
    const BindingFlags I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static Type Engine(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).First(t=>t!=null);
    static object Fake(Type type)=>RuntimeHelpers.GetUninitializedObject(type);
    static void Check(bool value,string text){if(!value)throw new Exception("Capture occlusion: "+text);}
    internal static void Run(Assembly plugin)
    {
        var actorType=Engine("VRage.Render.Scene.MyActor");var actor=Fake(actorType);
        var scene=Engine("VRage.Render.Scene.MyScene");var counter=scene.GetField("FrameCounter",S);var oldCounter=counter.GetValue(null);
        actorType.GetField("FrameInView",I).SetValue(actor,new long[]{40});
        actorType.GetField("<OccludedState>k__BackingField",I).SetValue(actor,new bool[]{true});
        counter.SetValue(null,41L);
        var groupType=Engine("VRage.Render.Scene.MyManualCullTreeData");var group=Activator.CreateInstance(groupType,true);
        groupType.GetField("Actor",I).SetValue(group,actor);
        var resultType=Engine("VRage.Render11.Culling.MyCullResults");var all=Activator.CreateInstance(resultType,true);
        groupType.GetField("All",I).SetValue(group,all);
        var list=resultType.GetField("CullProxies",I).GetValue(all);list.GetType().GetMethod("Add",I).Invoke(list,new[]{Fake(Engine("VRage.Render11.Culling.MyCullProxy"))});
        var tree=new MyDynamicAABBTreeD(Vector3D.Zero,1);var box=new BoundingBoxD(new Vector3D(-.1,-.1,.4),new Vector3D(.1,.1,.6));tree.AddProxy(ref box,group,0);
        var workType=Engine("VRage.Render11.Culling.Frustum.MyFrustumCullingWork");var work=Fake(workType);
        workType.GetField("m_groupContainedList",I).SetValue(work,new List<bool>());
        var queryType=Engine("VRage.Render11.Culling.MyCullQuery");
        var operation=workType.GetNestedType("CullDeepTree",BindingFlags.NonPublic|BindingFlags.Public);
        var method=workType.GetMethods(I).Single(m=>m.Name=="Cull"&&m.IsGenericMethodDefinition).MakeGenericMethod(operation);
        var renderer=Engine("VRageRender.MyRender11");var settingsField=renderer.GetField("Settings",S);var oldSettings=settingsField.GetValue(null);
        var settings=settingsField.GetValue(null);settings.GetType().GetField("DrawGroups",I).SetValue(settings,true);settingsField.SetValue(null,settings);
        var serviceType=plugin.GetType("HDRClientRenderer.DirectCameraCapture",true);var service=Activator.CreateInstance(serviceType,true);var capturing=serviceType.GetField("capturing",S);
        Func<int> cull=()=>
        {
            var query=Activator.CreateInstance(queryType,true);queryType.GetField("Frustum",I).SetValue(query,new BoundingFrustumD(MatrixD.Identity));queryType.GetField("FrustumFar",I).SetValue(query,new BoundingFrustumD(MatrixD.Identity));
            workType.GetField("m_query",I).SetValue(work,query);
            method.Invoke(work,new object[]{new MyDynamicAABBTreeD(),new MyDynamicAABBTreeD(),tree,(Action<BoundingFrustumD,MyDynamicAABBTreeD>)((f,t)=>{}),Activator.CreateInstance(operation,true)});
            var results=queryType.GetField("Results",I).GetValue(query);var proxies=resultType.GetField("CullProxies",I).GetValue(results);
            return (int)proxies.GetType().GetProperty("Count",I).GetValue(proxies);
        };
        try
        {
            for(int i=0;i<32;i++)Check(cull()==0,"warm real frustum caller skips the group occluded in the player's view");
            serviceType.GetMethod("TryInstall",I).Invoke(service,new object[]{(Action<string>)Console.WriteLine});
            Check((bool)serviceType.GetProperty("Ready",I).GetValue(service),"capture hooks bind");
            capturing.SetValue(null,1);
            Check(cull()==1,"capture does not inherit the player's cached occlusion, including a warmed native generic caller");
            Check(((bool[])actorType.GetProperty("OccludedState",I).GetValue(actor))[0],"capture does not mutate the player's cached occlusion array");
            capturing.SetValue(null,0);Check(cull()==0,"main-view cached occlusion is unchanged after capture");
            Console.WriteLine("PASS: actual warmed frustum group traversal isolates cached main-view occlusion; no GPU frame.");
        }
        finally{capturing.SetValue(null,0);((IDisposable)service).Dispose();counter.SetValue(null,oldCounter);settingsField.SetValue(null,oldSettings);}
    }
}
