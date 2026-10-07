using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HDRClientRenderer;
using HarmonyLib;
using VRageMath;
internal static class PortalCaptureBillboardIsolationChecks
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static object readList;
    static object[] persistentItems;
    static object environment,debugOverrides,spriteManager;
    static MethodInfo forbiddenSpriteRender;
    static bool ReadList(ref object __result){__result=readList;return false;}
    static bool Persistent(object __0){foreach(var value in persistentItems)((Delegate)__0).DynamicInvoke(value);return false;}
    static IEnumerable<CodeInstruction> BreakSelection(IEnumerable<CodeInstruction> code)
    {foreach(var i in code){if(i.operand is MethodInfo m&&m.Name=="GetBillboardBucket")yield return new CodeInstruction(OpCodes.Nop);yield return i;}}
    // Replace only static game entry reads. The installed flare math,
    // native billboard helpers, once allocator and Gather execute unchanged.
    static IEnumerable<CodeInstruction> NativeEntryReads(IEnumerable<CodeInstruction> code)
    {
        foreach(var i in code)
        {
            if(i.opcode==OpCodes.Ldsfld&&i.operand is FieldInfo f&&f.DeclaringType.FullName=="VRageRender.MyRender11"&&f.Name=="Environment")
            {yield return new CodeInstruction(OpCodes.Ldsfld,typeof(PortalCaptureBillboardIsolationChecks).GetField("environment",All)).WithLabels(i.labels);yield return new CodeInstruction(OpCodes.Castclass,f.FieldType);}
            else if(i.opcode==OpCodes.Ldsfld&&i.operand is FieldInfo sprites&&sprites.DeclaringType.FullName=="VRage.Render11.Common.MyManagers"&&sprites.Name=="SpritesManager")
            {yield return new CodeInstruction(OpCodes.Ldsfld,typeof(PortalCaptureBillboardIsolationChecks).GetField("spriteManager",All)).WithLabels(i.labels);yield return new CodeInstruction(OpCodes.Castclass,sprites.FieldType);}
            else if(i.operand is MethodInfo m&&m.DeclaringType.FullName=="VRageRender.MyRender11"&&m.Name=="get_DebugOverrides")
            {yield return new CodeInstruction(OpCodes.Ldsfld,typeof(PortalCaptureBillboardIsolationChecks).GetField("debugOverrides",All)).WithLabels(i.labels);yield return new CodeInstruction(OpCodes.Castclass,m.ReturnType);}
            else if(i.operand is MethodInfo deferred&&deferred.DeclaringType.FullName=="VRageRender.MyRender11"&&deferred.Name=="get_DeferredTransferData")yield return new CodeInstruction(OpCodes.Ldc_I4_0).WithLabels(i.labels);
            else if(i.operand is MethodInfo offscreen&&offscreen.DeclaringType.FullName=="VRageRender.MyRender11"&&offscreen.Name=="DrawSpritesOffscreen"&&offscreen.GetParameters().Length==7)yield return new CodeInstruction(OpCodes.Call,forbiddenSpriteRender).WithLabels(i.labels);
            else yield return i;
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        Type T(string n)=>DirectCameraCaptureNative.FindType(null,n);
        var renderer=T("VRageRender.MyBillboardRenderer");var assembly=renderer.Assembly;var billboard=T("VRageRender.MyBillboard");
        var prepare=renderer.GetMethod("PrepareList",All);var gather=renderer.GetMethod("Gather",All);
        var callbacks=renderer.GetNestedType("<>c",All);var flare=T("VRageRender.MyFlareRenderer");
        var mainMethods=new[]{renderer.GetMethod("OnFrameStart",All),renderer.GetMethod("OnDeviceReset",All),renderer.GetMethod("OnDeviceEnd",All),renderer.GetMethod("ClearBillboardsOnce",All)};
        var mainOwners=mainMethods.Select(m=>Harmony.GetPatchInfo(m)?.Owners.ToArray()??Array.Empty<string>()).ToArray();
        var saved=new List<Action>();
        void Swap(object target,FieldInfo field,object value){var old=field.GetValue(target);field.SetValue(target,value);saved.Add(()=>field.SetValue(target,old));}
        void Static(Type t,string n,object value)=>Swap(null,t.GetField(n,All),value);
        void Set(object target,string n,object value)=>target.GetType().GetField(n,All).SetValue(target,value);
        void SaveArray(Array values){var old=(Array)values.Clone();saved.Add(()=>Array.Copy(old,values,old.Length));}
        void SaveList(IList values){var old=values.Cast<object>().ToArray();values.Clear();saved.Add(()=>{values.Clear();foreach(var v in old)values.Add(v);});}
        void SaveDictionary(IDictionary values){var old=new List<DictionaryEntry>();var e=values.GetEnumerator();while(e.MoveNext())old.Add(e.Entry);values.Clear();saved.Add(()=>{values.Clear();foreach(var v in old)values.Add(v.Key,v.Value);});}
        var fixture=new Harmony("HDR.Portal.BillboardIsolationFixture");PortalCaptureBillboardIsolationNative adapter=null;
        int portalScope=0;
        try
        {
            var material=T("VRageRender.MyTransparentMaterial");var materials=(IDictionary)T("VRageRender.MyTransparentMaterials").GetField("m_materialsByName",All).GetValue(null);SaveDictionary(materials);
            var id=T("VRage.Utils.MyStringId");var intern=id.GetMethod("GetOrCompute",All,null,new[]{typeof(string)},null);
            var spriteId=intern.Invoke(null,new object[]{"HDR_PortalFixture_Sprite"});var ordinaryId=intern.Invoke(null,new object[]{"HDR_PortalFixture_File"});var otherId=intern.Invoke(null,new object[]{"HDR_PortalFixture_Other"});
            object Material(object name,int kind){var value=RuntimeHelpers.GetUninitializedObject(material);Set(value,"Id",name);Set(value,"TextureType",Enum.ToObject(material.GetField("TextureType",All).FieldType,kind));Set(value,"Color",Vector4.One);Set(value,"TargetSize",new Vector2I(64));materials.Add(name,value);return value;}
            var spriteMaterial=Material(spriteId,1);Material(ordinaryId,0);Material(otherId,2);
            object Billboard(object name,int blend){var value=Activator.CreateInstance(billboard);Set(value,"Material",name);Set(value,"BlendType",Enum.ToObject(billboard.GetField("BlendType",All).FieldType,blend));Set(value,"CustomViewProjection",-1);return value;}
            var listType=typeof(List<>).MakeGenericType(billboard);var read=(IList)Activator.CreateInstance(listType);readList=read;
            var readOrdinary=Billboard(ordinaryId,0);var readSprite=Billboard(spriteId,0);read.Add(readSprite);read.Add(readOrdinary);
            var pool=renderer.GetField("m_billboardsOncePool",All).GetValue(null);var poolItems=pool.GetType().GetField("m_items",All);var poolNext=pool.GetType().GetField("m_nextAllocateIndex",All);
            var items=Array.CreateInstance(billboard,16);for(int i=0;i<items.Length;i++)items.SetValue(Billboard(ordinaryId,0),i);
            Swap(pool,poolItems,items);Swap(pool,poolNext,2);var onceSprite=Billboard(spriteId,1);var onceOrdinary=Billboard(ordinaryId,1);items.SetValue(onceSprite,0);items.SetValue(onceOrdinary,1);
            var persistentSprite=Billboard(spriteId,2);var persistentOrdinary=Billboard(ordinaryId,2);persistentItems=new[]{persistentSprite,persistentOrdinary};
            var counts=(int[])renderer.GetField("m_bucketCounts",All).GetValue(null);var indices=(int[])renderer.GetField("m_bucketIndices",All).GetValue(null);SaveArray(counts);SaveArray(indices);SaveArray((Array)renderer.GetField("m_bucketBatches",All).GetValue(null));
            var batches=(IList)renderer.GetField("m_batches",All).GetValue(null);SaveList(batches);
            Static(renderer,"m_tempBuffer",Array.CreateInstance(billboard,32));Static(renderer,"m_arrayDataBillboards",Activator.CreateInstance(T("VRageRender.MyBillboardDataArray"),new object[]{32}));
            Static(renderer,"m_lastBatchOffset",0);Static(renderer,"m_billboardCountSafe",0);Static(renderer,"m_stats",Activator.CreateInstance(renderer.GetField("m_stats",All).FieldType));
            var proxy=T("VRageRender.MyRenderProxy");fixture.Patch(proxy.GetProperty("BillboardsRead",All).GetGetMethod(true),prefix:new HarmonyMethod(typeof(PortalCaptureBillboardIsolationChecks).GetMethod("ReadList",All)));
            var apply=proxy.GetMethods(All).Single(m=>m.Name=="ApplyActionOnPersistentBillboards"&&m.GetParameters()[0].ParameterType.IsGenericType);
            fixture.Patch(apply,prefix:new HarmonyMethod(typeof(PortalCaptureBillboardIsolationChecks).GetMethod("Persistent",All)));
            int Prepare()=>Convert.ToInt32(prepare.Invoke(null,null));
            object[] Selected(int n)=>((Array)renderer.GetField("m_tempBuffer",All).GetValue(null)).Cast<object>().Take(n).ToArray();
            bool Includes(object[] values,params object[] expected)=>values.Length==expected.Length&&expected.All(e=>values.Any(v=>ReferenceEquals(v,e)));
            check(Prepare()==6&&counts.Take(3).SequenceEqual(new[]{2,2,2}),"Actual installed PrepareList prewarms both count/write loops and cached persistent callbacks with all six main-view billboards");
            var unsupported=new PortalCaptureBillboardIsolationNative(typeof(PortalCaptureBillboardIsolationChecks).Assembly,()=>false);
            check(!unsupported.TryInstall()&&!unsupported.Ready&&unsupported.Reason.Contains("installed renderer assembly"),"Unrecognized native billboard assembly fails closed before installing hooks");unsupported.Dispose();
            adapter=new PortalCaptureBillboardIsolationNative(assembly,()=>Volatile.Read(ref portalScope)!=0);
            check(!adapter.Ready&&adapter.TryInstall()&&adapter.Ready,"Actual installed capture-only billboard/flare hooks install without GPU resources: "+adapter.Reason);
            check(Prepare()==6&&Includes(Selected(6),readSprite,readOrdinary,onceSprite,onceOrdinary,persistentSprite,persistentOrdinary),"Main view and ordinary captures retain every billboard after hook installation and cached delegate warmup");
            Volatile.Write(ref portalScope,1);
            int selected=Prepare();
            check(selected==3&&counts.SequenceEqual(new[]{1,1,1,0,0,0})&&indices.SequenceEqual(new[]{0,1,2,3,3,3}),"Actual installed portal PrepareList excludes sprite materials from all sources before counting and fixes all bucket offsets");
            check(Includes(Selected(selected),readOrdinary,onceOrdinary,persistentOrdinary),"Actual native vertex input list is compact and contains no sprite, holes or stale primary-view entries");
            check(read.Count==2&&ReferenceEquals(read[0],readSprite)&&Convert.ToInt32(poolNext.GetValue(pool))==2&&ReferenceEquals(items.GetValue(0),onceSprite)&&persistentItems.Length==2&&Convert.ToInt32(material.GetField("TextureType",All).GetValue(spriteMaterial))==1,"Portal filters preserve source lists, shared once-pool allocation count and native material definitions");
            read.Add(Billboard(otherId,3));check(Prepare()==4&&counts[3]==1,"Capture filtering excludes exactly TextureType 1; other native material types and blend buckets remain selected");read.RemoveAt(read.Count-1);
            Volatile.Write(ref portalScope,0);check(Prepare()==6,"Primary list recomputation after capture restores the original count and indices without rebuilding shared sources");
            Volatile.Write(ref portalScope,1);var worker=new Thread(()=>{selected=Prepare();});worker.Start();worker.Join();check(selected==3,"Capture-only predicate and guards remain visible on native worker threads");
            // Actual Gather must observe zero accepted sprites and never reach
            // its destructive AcquireDrawMessages / DisposeDrawMessages branch.
            read.Clear();read.Add(readSprite);poolNext.SetValue(pool,1);items.SetValue(onceSprite,0);persistentItems=new[]{persistentSprite};
            var sprites=T("VRage.Render11.Sprites.MySpritesManager");var manager=RuntimeHelpers.GetUninitializedObject(sprites);var queueField=sprites.GetField("m_drawQueue",All);var queue=(IDictionary)Activator.CreateInstance(queueField.FieldType);queueField.SetValue(manager,queue);
            var messages=RuntimeHelpers.GetUninitializedObject(T("VRage.Render11.Sprites.MySpriteMessageData"));const string spriteName="HDR_PortalFixture_Sprite";queue.Add(spriteName,messages);
            spriteManager=manager;var offscreen=T("VRageRender.MyRender11").GetMethods(All).Single(m=>m.Name=="DrawSpritesOffscreen"&&m.GetParameters().Length==7);
            var noGpu=new DynamicMethod("HDR_ForbiddenSpriteOffscreen",offscreen.ReturnType,offscreen.GetParameters().Select(p=>p.ParameterType).ToArray(),typeof(PortalCaptureBillboardIsolationChecks),true);var noGpuIl=noGpu.GetILGenerator();noGpuIl.Emit(OpCodes.Ldstr,"Sprite-backed billboard escaped portal preparation");noGpuIl.Emit(OpCodes.Newobj,typeof(InvalidOperationException).GetConstructor(new[]{typeof(string)}));noGpuIl.Emit(OpCodes.Throw);forbiddenSpriteRender=noGpu;
            var entry=new HarmonyMethod(typeof(PortalCaptureBillboardIsolationChecks).GetMethod("NativeEntryReads",All));fixture.Patch(gather,transpiler:entry);fixture.Patch(renderer.GetMethod("GatherInternal",All),transpiler:entry);
            gather.Invoke(null,new object[]{null,false});
            check(Convert.ToInt32(renderer.GetField("m_billboardCountSafe",All).GetValue(null))==0&&counts.All(c=>c==0)&&batches.Count==0&&queue.Count==1&&ReferenceEquals(queue[spriteName],messages),"Actual installed portal Gather excludes sprite-only input before batch/vertex generation and preserves its queued draw messages");
            Volatile.Write(ref portalScope,0);check(Prepare()==3,"Main PrepareList still selects all sprite-only sources after portal Gather");
            var acquired=sprites.GetMethod("AcquireDrawMessages",All).Invoke(manager,new object[]{spriteName});check(ReferenceEquals(acquired,messages)&&queue.Count==0,"Actual main sprite acquisition remains unchanged and consumes exactly its preserved native queue entry");
            // Run the actual large native flare leaf, its billboard helpers and
            // once allocator. Only CPU camera/material/occlusion inputs exist.
            var native=T("VRageRender.MyRender11");var env=RuntimeHelpers.GetUninitializedObject(native.GetField("Environment",All).FieldType);var matrices=env.GetType().GetField("Matrices",All);var matrix=RuntimeHelpers.GetUninitializedObject(matrices.FieldType);Set(matrix,"CameraPosition",Vector3D.Zero);Set(matrix,"InvViewAt0",Matrix.Identity);Set(matrix,"ViewProjectionAt0",Matrix.Identity);matrices.SetValue(env,matrix);environment=env;
            var overrides=Activator.CreateInstance(T("VRageRender.Messages.MyRenderDebugOverrides"));Set(overrides,"BillboardsDynamic",true);debugOverrides=overrides;
            var dataType=flare.GetNestedType("Data",All);var data=Activator.CreateInstance(dataType);var query=RuntimeHelpers.GetUninitializedObject(T("VRage.Render11.Culling.Occlusion.MyFlareOcclusionData"));Set(query,"OcclusionFactor",0f);Set(data,"Query",query);
            var descType=T("VRageRender.Messages.MyFlareDesc");var desc=Activator.CreateInstance(descType);Set(desc,"Intensity",1f);Set(desc,"MaxDistance",100f);Set(desc,"SizeMultiplier",Vector2.One);
            var glareType=T("VRageRender.Messages.MySubGlare");var glare=Activator.CreateInstance(glareType);Set(glare,"Material",ordinaryId);Set(glare,"Color",Vector4.One);Set(glare,"Size",Vector2.One);Set(glare,"ScreenIntensityMultiplierCenter",1f);Set(glare,"ScreenIntensityMultiplierEdge",1f);
            var glares=Array.CreateInstance(glareType,1);glares.SetValue(glare,0);Set(desc,"Glares",glares);Set(data,"Desc",desc);
            var free=flare.GetField("m_flares",All).GetValue(null);var dataArray=Array.CreateInstance(dataType,1);dataArray.SetValue(data,0);Swap(free,free.GetType().GetField("m_entities",All),dataArray);
            var leaf=flare.GetMethods(All).Single(m=>m.Name=="Draw"&&m.GetParameters().Length==5);var flareId=Activator.CreateInstance(T("VRageRender.FlareId"));Set(flareId,"Index",0);var arguments=new object[]{flareId,new Vector3D(0,0,-1),Vector3D.Forward,Vector4.One,1f};
            foreach(var method in T("VRageRender.MyBillboardsHelper11").GetMethods(All).Where(m=>m.Name=="CreateBillboard"||m.Name=="AddBillboardOriented"||m.Name=="AddBillboardRotated").OrderBy(m=>m.Name=="CreateBillboard"?0:1).ThenByDescending(m=>m.GetParameters().Length))fixture.Patch(method,transpiler:entry);
            fixture.Patch(leaf,transpiler:entry);
            poolNext.SetValue(pool,0);leaf.Invoke(null,arguments);check(Convert.ToInt32(poolNext.GetValue(pool))==1,"Actual installed primary flare Draw and native helpers allocate one CPU billboard into the shared once-pool");
            var first=items.GetValue(0);Set(query,"Visible",false);Volatile.Write(ref portalScope,1);leaf.Invoke(null,arguments);
            check(Convert.ToInt32(poolNext.GetValue(pool))==1&&ReferenceEquals(items.GetValue(0),first)&&!(bool)query.GetType().GetField("Visible",All).GetValue(query),"Actual installed portal flare guard leaves once-pool count/items and flare visibility state untouched");
            foreach(var method in flare.GetMethods(All).Where(m=>m.Name=="Draw"&&m.GetParameters().Length!=5))
            {var arguments2=method.GetParameters().Select(p=>p.ParameterType.IsValueType?Activator.CreateInstance(p.ParameterType):null).ToArray();method.Invoke(null,arguments2);}
            check(Convert.ToInt32(poolNext.GetValue(pool))==1,"Both higher flare producer overloads are capture-only guarded before reading missing scene/Ansel inputs");
            Volatile.Write(ref portalScope,0);leaf.Invoke(null,arguments);check(Convert.ToInt32(poolNext.GetValue(pool))==2,"Main flare producer resumes immediately after portal scope ends");
            adapter.Dispose();check(!adapter.Ready&&adapter.Reason.Contains("disposed"),"Adapter disposal removes only its capture guards");
            Volatile.Write(ref portalScope,1);leaf.Invoke(null,arguments);check(Convert.ToInt32(poolNext.GetValue(pool))==3,"Actual native flare body runs unchanged after capture-hook disposal");
            check(mainMethods.Select((m,i)=>(Harmony.GetPatchInfo(m)?.Owners.ToArray()??Array.Empty<string>()).SequenceEqual(mainOwners[i])).All(v=>v),"Frame-start clear and device reset/end hooks are never patched by portal isolation");
            renderer.GetMethod("OnFrameStart",All).Invoke(null,null);check(Convert.ToInt32(poolNext.GetValue(pool))==0,"Actual native primary OnFrameStart still clears the once-pool normally");
            // A foreign transform that changes the audited per-item count
            // shape cannot partially activate portal isolation.
            fixture.Patch(prepare,transpiler:new HarmonyMethod(typeof(PortalCaptureBillboardIsolationChecks).GetMethod("BreakSelection",All)));
            var altered=new PortalCaptureBillboardIsolationNative(assembly,()=>true);
            check(!altered.TryInstall()&&!altered.Ready&&altered.Reason.Contains("changed"),"Unsupported live billboard selection IL fails closed during installation");altered.Dispose();
            var targets=flare.GetMethods(All).Where(m=>m.Name=="Draw").Concat(callbacks.GetMethods(All).Where(m=>m.Name.StartsWith("<PrepareList>",StringComparison.Ordinal))).Concat(new[]{prepare});
            check(targets.All(m=>!(Harmony.GetPatchInfo(m)?.Owners.Contains("HDRClientRenderer.PortalCaptureBillboardIsolation")??false)),"Failed guard installation rolls back flare, persistent and preparation hooks instead of leaving main renderer partially patched");
        }
        finally
        {
            Volatile.Write(ref portalScope,0);adapter?.Dispose();fixture.UnpatchAll(fixture.Id);
            for(int i=saved.Count-1;i>=0;i--)saved[i]();readList=null;persistentItems=null;environment=null;debugOverrides=null;spriteManager=null;forbiddenSpriteRender=null;
        }
    }
}
