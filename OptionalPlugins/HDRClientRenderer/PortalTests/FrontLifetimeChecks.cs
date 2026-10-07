using System.Reflection;
using System.Reflection.Emit;
using System.Collections;
using HDRClientRenderer;
using HarmonyLib;
internal static class FrontLifetimeChecks
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    // CPU replacement for the terminal COM descriptor query only. Actual engine
    // AddBatch and generated-texture Dispose execute unchanged in this fixture.
    static IEnumerable<CodeInstruction> DescriptionCpu(IEnumerable<CodeInstruction> instructions,ILGenerator generator,MethodBase __originalMethod)
    {
        var type=((MethodInfo)__originalMethod).ReturnType;var local=generator.DeclareLocal(type);
        return new[]{new CodeInstruction(OpCodes.Ldloca,local),new CodeInstruction(OpCodes.Initobj,type),new CodeInstruction(OpCodes.Ldloc,local),new CodeInstruction(OpCodes.Ret)};
    }
    internal static void Run(Action<bool,string> check)
    {
        var context=DirectCameraCaptureNative.FindType(null,"VRage.Render11.RenderContext.MyRenderContext");
        foreach(string name in new[]{"ClearRtv","GenerateMips"})
        {
            var method=context.GetMethods(All).Single(m=>m.Name==name&&m.GetParameters().Length==(name=="ClearRtv"?2:1));var il=PatchProcessor.GetOriginalInstructions(method);
            check(!il.Any(i=>i.opcode==OpCodes.Stfld&&i.operand is FieldInfo f&&f.DeclaringType.FullName.Contains("MyRenderContextState"))&&!PortalCaptureCoverage.Calls(method).Any(m=>m.Name=="ClearState"||m.Name.StartsWith("Set",StringComparison.Ordinal)),"Installed "+name+" targets an explicit resource without changing main-view binding caches");
        }
        var generated=DirectCameraCaptureNative.FindType(null,"VRage.Render11.Resources.Internal.MyGeneratedTexture");var srvField=generated.GetField("m_srv",All);
        var viewType=srvField.FieldType;object NewView(){var v=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(viewType);GC.SuppressFinalize(v);return v;}
        object texture=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(generated),material=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(DirectCameraCaptureNative.FindType(null,"VRageRender.MyTransparentMaterial"));
        srvField.SetValue(texture,NewView());var billboard=DirectCameraCaptureNative.FindType(null,"VRageRender.MyBillboardRenderer");var batch=billboard.GetMethod("AddBatch",All);
        var batches=billboard.GetField("m_batches",All);var list=(IList)batches.GetValue(null);var oldBatches=list.Cast<object>().ToArray();list.Clear();
        var harmony=new Harmony("HDR.Portal.FrontLifetimeFixture");var descriptor=viewType.GetProperty("Description",All).GetGetMethod(true);
        harmony.Patch(descriptor,transpiler:new HarmonyMethod(typeof(FrontLifetimeChecks).GetMethod("DescriptionCpu",All)));
        try
        {
            // counter0 equals offset0 and ends immediately at bucket boundary;
            // initialized native bucket arrays remain ordinary engine CPU data.
            batch.Invoke(null,new[]{(object)0,0,texture,material});check(true,"Actual installed AddBatch accepts a retained generated wrapper and its live SRV");
            generated.GetMethod("Dispose",All,null,Type.EmptyTypes,null).Invoke(texture,null);
            bool exact=false;try{batch.Invoke(null,new[]{(object)0,0,texture,material});}catch(TargetInvocationException ex){exact=ex.InnerException is NullReferenceException;}
            check(exact&&srvField.GetValue(texture)==null,"Actual installed generated Dispose nulls SRV and reproduces reported AddBatch NullReference with non-null material/texture inputs");
            srvField.SetValue(texture,NewView());object originalView=srvField.GetValue(texture);var registry=new Dictionary<string,object>{{"HDR_ClientPortal_0",texture}};var stamps=new Dictionary<object,string>();int resets=0,clears=0;bool transparent=false;
            Func<PortalFrontTargetPool> Pool=()=>new PortalFrontTargetPool(n=>registry.TryGetValue(n,out var t)?t:null,n=>{var t=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(generated);registry[n]=t;return t;},t=>true,t=>srvField.GetValue(t)!=null,
                t=>{resets++;srvField.SetValue(t,NewView());},t=>stamps.TryGetValue(t,out var s)?s:null,(t,s)=>stamps[t]=s,t=>{clears++;transparent=true;});
            var first=Pool();check(ReferenceEquals(first.Acquire("HDR_ClientPortal_0"),texture)&&resets==0&&ReferenceEquals(originalView,srvField.GetValue(texture)),"Adopting a live front never Activate/Reset/disposes its retained SRV");
            object cachedBillboard=texture;first.Retire("HDR_ClientPortal_0");batch.Invoke(null,new[]{(object)0,0,cachedBillboard,material});check(transparent&&ReferenceEquals(originalView,srvField.GetValue(cachedBillboard)),"Offscreen/LOD lease release writes transparent tombstone while actual delayed AddBatch remains valid indefinitely");
            first.Acquire("HDR_ClientPortal_0");check(resets==0&&registry.Count==1,"Logical resolution/pose changes reuse the fixed front wrapper without a streaming-registration gap");
            var oldRetirement=first.CaptureRetirement("HDR_ClientPortal_0");first.Acquire("HDR_ClientPortal_0");int beforeReacquired=clears;oldRetirement();check(clears==beforeReacquired,"Delayed A-to-B-to-A lease retirement cannot tombstone a reacquired front owned by the same backend");
            var successor=Pool();successor.Acquire("HDR_ClientPortal_0");int before=clears;first.RetireAll();check(clears==before,"Delayed older backend cleanup cannot clear a front adopted by a successor owner");
            successor.RetireAll();check(clears==before+1&&registry.Count==1&&srvField.GetValue(texture)!=null,"Backend/world disposal tombstones and preserves manager registration for cached material references");
            srvField.SetValue(texture,null);successor.Acquire("HDR_ClientPortal_0");check(resets==1&&srvField.GetValue(texture)!=null,"Only genuinely missing device views reactivate before a front is reused");
            successor.Acquire("HDR_ClientPortal_1");check(registry.Count==2&&PortalFrontTargetPool.Slot("HDR_ClientPortal_2")==-1,"Public front ownership has exactly two trusted fixed names and bounded registrations");
        }
        finally{harmony.UnpatchAll(harmony.Id);list.Clear();foreach(var value in oldBatches)list.Add(value);}
    }
}
