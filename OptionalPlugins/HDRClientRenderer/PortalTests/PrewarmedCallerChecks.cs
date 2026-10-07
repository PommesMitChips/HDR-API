using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HDRClientRenderer;
using HarmonyLib;
internal sealed class PrewarmedCallerChecks:IDisposable
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Sink(IntPtr c,int a,int b);
    readonly object context,native,shader;readonly FieldInfo pointer;readonly IntPtr table,instance;readonly Sink sink;
    readonly Harmony harness=new Harmony("HDR.Portal.PrewarmedCallerFixture");readonly MethodInfo outer,helper,stereo,draw;
    int calls;bool arguments=true;static int laterCalls;
    static bool SkipGpuSetup(){return false;}static void LaterPrefix(){laterCalls++;}
    internal unsafe PrewarmedCallerChecks(object contextValue,object nativeValue,object shaderValue,Action<bool,string> check)
    {
        context=contextValue;native=nativeValue;shader=shaderValue;var contextType=context.GetType();var stats=contextType.GetField("m_statistics",All);stats.SetValue(context,System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(stats.FieldType));
        for(Type t=native.GetType();t!=null&&pointer==null;t=t.BaseType)pointer=t.GetField("_nativePointer",All|BindingFlags.DeclaredOnly);
        table=Marshal.AllocHGlobal(160*IntPtr.Size);instance=Marshal.AllocHGlobal(IntPtr.Size);for(int i=0;i<160;i++)Marshal.WriteIntPtr(table,i*IntPtr.Size,IntPtr.Zero);Marshal.WriteIntPtr(instance,table);
        sink=(c,a,b)=>{calls++;arguments&=c==instance&&a==3&&b==5;};Marshal.WriteIntPtr(table,13*IntPtr.Size,Marshal.GetFunctionPointerForDelegate(sink));GC.SuppressFinalize(native);pointer.SetValue(native,Pointer.Box(instance.ToPointer(),pointer.FieldType));
        var stereoType=DirectCameraCaptureNative.FindType(null,"VRageRender.MyStereoRender");stereo=stereoType.GetMethod("DrawGBufferPass",All,null,new[]{contextType,typeof(int),typeof(int)},null);draw=contextType.GetMethod("Draw",All,null,new[]{typeof(int),typeof(int)},null);
        foreach(string name in new[]{"BeginDrawGBufferPass","SwitchDrawGBufferPass","EndDrawGBufferPass"})harness.Patch(stereoType.GetMethod(name,All),prefix:new HarmonyMethod(typeof(PrewarmedCallerChecks).GetMethod("SkipGpuSetup",All)));
        var asm=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("HDR.Portal.PrewarmedCaller"),AssemblyBuilderAccess.Run);var module=asm.DefineDynamicModule("Caller");
        var attribute=module.DefineType("System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute",TypeAttributes.Public|TypeAttributes.Sealed,typeof(Attribute));var ctor=attribute.DefineConstructor(MethodAttributes.Public,CallingConventions.Standard,new[]{typeof(string)});var cil=ctor.GetILGenerator();cil.Emit(OpCodes.Ldarg_0);cil.Emit(OpCodes.Call,typeof(Attribute).GetConstructor(All,null,Type.EmptyTypes,null));cil.Emit(OpCodes.Ret);var attrType=attribute.CreateType();asm.SetCustomAttribute(new CustomAttributeBuilder(attrType.GetConstructor(new[]{typeof(string)}),new object[]{contextType.Assembly.GetName().Name}));
        var fixture=module.DefineType("EnclosingRenderPass",TypeAttributes.Public|TypeAttributes.Abstract|TypeAttributes.Sealed);
        var inner=fixture.DefineMethod("Helper",MethodAttributes.Public|MethodAttributes.Static,typeof(void),new[]{typeof(object)});inner.SetImplementationFlags(MethodImplAttributes.IL|MethodImplAttributes.AggressiveInlining);var il=inner.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,contextType);il.Emit(OpCodes.Ldc_I4_3);il.Emit(OpCodes.Ldc_I4_5);il.Emit(OpCodes.Call,stereo);il.Emit(OpCodes.Ret);
        var enclosing=fixture.DefineMethod("Outer",MethodAttributes.Public|MethodAttributes.Static,typeof(void),new[]{typeof(object)});enclosing.SetImplementationFlags(MethodImplAttributes.IL|MethodImplAttributes.NoInlining);il=enclosing.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,inner);il.Emit(OpCodes.Ret);var type=fixture.CreateType();helper=type.GetMethod("Helper");outer=type.GetMethod("Outer");
        DirectCameraCapture.IsCapturing=false;for(int i=0;i<64;i++)outer.Invoke(null,new[]{context});check(calls==128&&arguments,"Actual stereo GBuffer helper and two enclosing caller levels execute/JIT before portal hook installation using CPU-only vtable calls");
    }
    internal void Verify(PortalCaptureIntegrationNative integration,Action<bool,string> check)
    {
        // The test assembly models already-JITted engine outer callers. Apply
        // the same identity re-JIT used by the renderer ancestry certificate.
        var identity=typeof(PortalCaptureCoverage).GetMethod("RecompileCaller",All);harness.Patch(helper,transpiler:new HarmonyMethod(identity));harness.Patch(outer,transpiler:new HarmonyMethod(identity));
        var field=typeof(PortalCaptureIntegrationNative).GetField("session",All);
        PortalCaptureIntegrationNative.Session Begin(){DirectCameraCapture.IsCapturing=true;var s=new PortalCaptureIntegrationNative.Session(new PortalCaptureGate(integration.Epoch,80,1,true,true),64,64,new Dictionary<object,PortalDrawRole>{{shader,PortalDrawRole.Auxiliary}});field.SetValue(integration,s);return s;}
        var scope=Begin();int before=calls;outer.Invoke(null,new[]{context});check(calls==before+2&&scope.AuxiliaryDraws==2&&integration.EndAfterJoin(scope,true),"Prewarmed two-level caller reaches actual patched MyStereoRender.DrawGBufferPass and both guarded renderer/native calls after closure re-JIT");
        var prefix=typeof(PrewarmedCallerChecks).GetMethod("LaterPrefix",All);laterCalls=0;harness.Patch(draw,prefix:new HarmonyMethod(prefix));scope=Begin();typeof(PortalCaptureDrawBridges).GetMethod("Draw",All).Invoke(null,new[]{context,(object)3,5});check(laterCalls==1&&integration.EndAfterJoin(scope,true),"Cached managed entry pointer preserves a later Harmony owner prefix");
        harness.Unpatch(draw,prefix);scope=Begin();typeof(PortalCaptureDrawBridges).GetMethod("Draw",All).Invoke(null,new[]{context,(object)3,5});check(laterCalls==1&&integration.EndAfterJoin(scope,true),"Cached managed entry pointer follows later unpatch and still reaches audited entry");DirectCameraCapture.IsCapturing=false;
    }
    public unsafe void Dispose(){DirectCameraCapture.IsCapturing=false;harness.UnpatchAll(harness.Id);pointer.SetValue(native,Pointer.Box(null,pointer.FieldType));Marshal.FreeHGlobal(instance);Marshal.FreeHGlobal(table);GC.KeepAlive(sink);}
}
