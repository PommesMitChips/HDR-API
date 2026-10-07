using System.Reflection;
using System.Runtime.CompilerServices;
using VRageMath;


static class CaptureModelFixture
{
    const BindingFlags I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static Type Engine(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).First(t=>t!=null);
    static object Fake(Type t)=>RuntimeHelpers.GetUninitializedObject(t);
    static void Check(bool ok,string text){if(!ok)throw new Exception("Capture models: "+text);}

    internal static void Run(Assembly plugin)
    {
        var serviceType=plugin.GetType("HDRClientRenderer.DirectCameraCapture",true);var service=Activator.CreateInstance(serviceType,true);
        var capturing=serviceType.GetField("capturing",S);
        var renderer=Engine("VRageRender.MyRender11");var env=renderer.GetField("Environment",S).GetValue(null);
        var matrices=env.GetType().GetField("Matrices",I).GetValue(env);var projection=matrices.GetType().GetField("Projection",I);
        var viewport=renderer.GetProperty("ViewportResolution",S);var oldProjection=projection.GetValue(matrices);var oldViewport=viewport.GetValue(null);
        object native=null;

        try
        {
            serviceType.GetMethod("TryInstall",I).Invoke(service,new object[]{(Action<string>)Console.WriteLine});
            Check((bool)serviceType.GetProperty("Ready",I).GetValue(service),"real hooks bind");
            native=serviceType.GetField("native",I).GetValue(service);
            var type=Engine("VRage.Render11.Scene.Components.MyRenderableComponent");var component=Fake(type);
            var actorType=Engine("VRage.Render.Scene.MyActor");var actor=Fake(actorType);
            actorType.GetField("<VolumeExtent>k__BackingField",I).SetValue(actor,.1f);
            actorType.GetField("<Scene>k__BackingField",I).SetValue(actor,Fake(Engine("VRage.Render11.Scene.MyScene11")));
            actorType.GetField("m_aabb",I).SetValue(actor,new BoundingBoxD(new Vector3D(-.1,-.1,-20.1),new Vector3D(.1,.1,-19.9)));
            Engine("VRage.Render.Scene.Components.MyActorComponent").GetField("<Owner>k__BackingField",I).SetValue(component,actor);
            var proxyType=Engine("VRage.Render11.Culling.MyCullProxy");var proxy=Fake(proxyType);
            proxyType.GetField("m_worldMatrixIndex",I).SetValue(proxy,17);
            type.GetProperty("CullProxy",I).SetValue(component,proxy);proxyType.GetField("Parent",I).SetValue(proxy,component);
            var drawType=Engine("VRageRender.MyRenderableProxy");var high=Fake(drawType);
            var common=drawType.GetField("CommonObjectData",I);var data=common.GetValue(high);var alpha=common.FieldType.GetField("CustomAlpha",I);
            alpha.SetValue(data,new Vector2(.8f,-.35f));common.SetValue(high,data);
            var highArray=Array.CreateInstance(drawType,1);highArray.SetValue(high,0);
            var oldArray=Array.CreateInstance(drawType,0);var oldKeys=new ulong[0];
            proxyType.GetField("RenderableProxies",I).SetValue(proxy,oldArray);proxyType.GetField("SortingKeys",I).SetValue(proxy,oldKeys);
            var lodType=Engine("VRageRender.MyRenderLod");var lod=Fake(lodType);
            lodType.GetField("RenderableProxies",I).SetValue(lod,highArray);var highKeys=new ulong[]{42};lodType.GetField("SortingKeys",I).SetValue(lod,highKeys);
            var lods=Array.CreateInstance(lodType,2);lods.SetValue(lod,0);lods.SetValue(Fake(lodType),1);type.GetField("m_lods",I).SetValue(component,lods);
            type.GetField("<CurrentLod>k__BackingField",I).SetValue(component,1);type.GetField("m_lastLodDistanceSqr",I).SetValue(component,0f);
            var culled=type.GetField("<IsCulled>k__BackingField",I);culled.SetValue(component,true);
            projection.SetValue(matrices,Matrix.CreatePerspectiveFieldOfView(1.1f,1,.1f,100));viewport.SetValue(null,new Vector2I(64,64));
            var filter=type.GetMethod("CheckDistanceCulling",I);var update=type.GetMethod("UpdateAfterCull",I);
            Check((bool)filter.Invoke(component,new object[]{400f}),"real main-view filter discards a tiny offscreen-camera model below native20pixel-area cutoff");
            capturing.SetValue(null,1);
            Check(!(bool)filter.Invoke(component,new object[]{400f}),"capture retains tiny model without changing the main filter");
            update.Invoke(component,null);
            var view=(Array)proxyType.GetField("RenderableProxies",I).GetValue(proxy);var viewProxy=view.GetValue(0);
            Check(!(bool)culled.GetValue(component)&&view.Length==1&&!ReferenceEquals(view,highArray)&&!ReferenceEquals(viewProxy,high),"actual hooked update prepares privateLOD0 geometry despite an empty current main-view proxy");
            Check((int)proxyType.GetField("m_worldMatrixIndex",I).GetValue(proxy)==-1,"newly selected geometry requests a full capture-world transform");
            var viewAlpha=(Vector2)alpha.GetValue(common.GetValue(viewProxy));
            Check(viewAlpha.X==0&&viewAlpha.Y==-.35f&&((Vector2)alpha.GetValue(common.GetValue(high))).X==.8f,"capture removes only LOD fade, retains transparency, never mutates shared model proxy");
            Check((int)type.GetProperty("CurrentLod",I).GetValue(component)==1&&!ReferenceEquals(proxyType.GetField("SortingKeys",I).GetValue(proxy),highKeys),"main LOD selection stays intact and sorting is detached");
            native.GetType().GetMethod("RestoreSceneMutations",I).Invoke(native,null);
            Check(ReferenceEquals(proxyType.GetField("RenderableProxies",I).GetValue(proxy),oldArray)&&ReferenceEquals(proxyType.GetField("SortingKeys",I).GetValue(proxy),oldKeys)&&(bool)culled.GetValue(component),"main proxy identities and visibility restore after joined capture");
            Check((int)proxyType.GetField("m_worldMatrixIndex",I).GetValue(proxy)==17,"main matrix cache restores after capture");
            CachedLegacyQuery(native,proxy,component,oldArray,highArray);
            Instances(plugin,native);
            update.Invoke(component,null);common.SetValue(((Array)proxyType.GetField("RenderableProxies",I).GetValue(proxy)).GetValue(0),data);
            native.GetType().GetMethod("RestoreSceneMutations",I).Invoke(native,null);
            Check(ReferenceEquals(proxyType.GetField("RenderableProxies",I).GetValue(proxy),oldArray),"failure-style cleanup also restores original proxy ownership");
            capturing.SetValue(null,0);
            Check((bool)filter.Invoke(component,new object[]{400f}),"ordinary player-view culling is unchanged after capture");
            Console.WriteLine("PASS: native small-model culling and empty-main-LOD regression, private geometry/alpha and success/failure restoration; no GPU frame.");
        }
        finally
        {
            if(native!=null)native.GetType().GetMethod("RestoreSceneMutations",I).Invoke(native,null);
            capturing.SetValue(null,0);((IDisposable)service).Dispose();projection.SetValue(matrices,oldProjection);viewport.SetValue(null,oldViewport);

        }
    }
    static void CachedLegacyQuery(object native,object proxy,object component,Array oldArray,Array highArray)
    {
        var queryType=Engine("VRage.Render11.Culling.MyCullQuery");var query=Fake(queryType);
        var resultsField=queryType.GetField("Results",I);var results=Activator.CreateInstance(resultsField.FieldType,true);resultsField.SetValue(query,results);
        var list=results.GetType().GetField("CullProxies",I).GetValue(results);
        list.GetType().GetMethod("Add",I).Invoke(list,new[]{proxy});
        var pending=queryType.GetField("TmpCullProxies",I);pending.SetValue(query,Activator.CreateInstance(pending.FieldType));
        var marker=proxy.GetType().GetField("Updated",I);marker.SetValue(proxy,2);
        var renderer=Fake(Engine("VRageRender.MyGeometryRendererOld"));
        renderer.GetType().GetMethod("UpdateCullProxies",I).Invoke(renderer,new[]{query});
        var visible=(bool)component.GetType().GetProperty("IsCulled",I).GetValue(component);
        var view=(Array)proxy.GetType().GetField("RenderableProxies",I).GetValue(proxy);
        Check(!visible&&!ReferenceEquals(view,oldArray)&&!ReferenceEquals(view,highArray),
            "real legacy query refreshes a proxy marked already-updated by the player view before trimming capture geometry");
        native.GetType().GetMethod("PrepareLegacyQuery",I).Invoke(native,new[]{query});
        Check((int)marker.GetValue(proxy)==2,"a second capture query does not reset a proxy already claimed by this capture");
        native.GetType().GetMethod("RestoreSceneMutations",I).Invoke(native,null);
        Check((int)marker.GetValue(proxy)==2&&(bool)component.GetType().GetProperty("IsCulled",I).GetValue(component)&&ReferenceEquals(proxy.GetType().GetField("RenderableProxies",I).GetValue(proxy),oldArray),
            "capture restores the player view's marker, visibility and proxy ownership");
        var captureType=native.GetType().Assembly.GetType("HDRClientRenderer.DirectCameraCapture",true);var capturing=captureType.GetField("capturing",S);
        try
        {
            capturing.SetValue(null,0);renderer.GetType().GetMethod("UpdateCullProxies",I).Invoke(renderer,new[]{query});
            Check((int)marker.GetValue(proxy)==2&&(bool)component.GetType().GetProperty("IsCulled",I).GetValue(component),
                "ordinary main-view query retains the native already-updated semantics");
        }
        finally{capturing.SetValue(null,1);}
    }
    static void Instances(Assembly plugin,object native)
    {
        var queryType=Engine("VRage.Render11.Culling.MyCullQuery");var query=Fake(queryType);var resultField=queryType.GetField("Results",I);
        var results=Activator.CreateInstance(resultField.FieldType,true);resultField.SetValue(query,results);
        var instanceType=Engine("VRage.Render11.GeometryStage2.Instancing.MyInstance");var groupType=Engine("VRage.Render11.GeometryStage2.StaticGroup.MyStaticGroup");
        var instance=Fake(instanceType);var group=Fake(groupType);
        foreach(var pair in new[]{(instance,"Instances"),(group,"StaticGroups")})
        {
            var field=pair.Item1.GetType().GetField("LodStrategy",I);var strategy=field.GetValue(pair.Item1);var type=field.FieldType;
            type.GetField("m_currentLod",I).SetValue(strategy,2);type.GetField("m_transitionLod",I).SetValue(strategy,1);type.GetField("m_transition",I).SetValue(strategy,.75f);
            type.GetField("m_explicitStateData",I).SetValue(strategy,.4f);field.SetValue(pair.Item1,strategy);
            var listField=results.GetType().GetField(pair.Item2,I);var list=listField.GetValue(results);
            if(list==null){list=Activator.CreateInstance(listField.FieldType,true);listField.SetValue(results,list);}
            list.GetType().GetMethod("Add",I).Invoke(list,new[]{pair.Item1});
        }
        var geometry=Fake(Engine("VRage.Render11.GeometryStage2.Rendering.MyGeometryRenderer"));
        // Its LOD updates are disabled as in captures. Our prefix must still
        // detach the retained main-view strategies, before the engine returns.
        geometry.GetType().GetMethod("UpdateLods",I).Invoke(geometry,new[]{query});
        foreach(var owner in new[]{instance,group})
        {
            var field=owner.GetType().GetField("LodStrategy",I);var state=field.GetValue(owner);
            Check((int)field.FieldType.GetField("m_currentLod",I).GetValue(state)==0&&(float)field.FieldType.GetField("m_transition",I).GetValue(state)==0,
                "instanced and static models use complete capture LOD independently of the player strategy");
            Check((float)field.FieldType.GetField("m_explicitStateData",I).GetValue(state)==.4f,"capture preserves explicit object fade");
        }
        native.GetType().GetMethod("RestoreSceneMutations",I).Invoke(native,null);
        foreach(var owner in new[]{instance,group})
        {
            var field=owner.GetType().GetField("LodStrategy",I);var state=field.GetValue(owner);
            Check((int)field.FieldType.GetField("m_currentLod",I).GetValue(state)==2&&(float)field.FieldType.GetField("m_transition",I).GetValue(state)==.75f,
                "instanced and static main-view LOD/fade restore after capture");
        }
    }
}
