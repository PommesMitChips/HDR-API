using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HDRClientRenderer;
using HarmonyLib;
internal static class DrawExecutionChecks
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void S0(IntPtr c);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void S2(IntPtr c,int a,int b);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void S3(IntPtr c,int a,int b,int d);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void S4(IntPtr c,int a,int b,int d,int e);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void S5(IntPtr c,int a,int b,int d,int e,int f);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void SI(IntPtr c,IntPtr b,int o);
    public class BufferProxy:DispatchProxy
    {internal object Buffer;protected override object Invoke(MethodInfo method,object[] args){return method.Name=="get_Buffer"?Buffer:method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;}}
    internal static unsafe void Run(PortalCaptureIntegrationNative integration,object context,object native,object shader,Action<bool,string> check)
    {
        var contextType=context.GetType();var nativeType=native.GetType();var drawMethods=PortalCaptureCoverage.ValidateDrawBoundary(contextType.Assembly);
        var stats=contextType.GetField("m_statistics",All);stats.SetValue(context,System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(stats.FieldType));
        FieldInfo pointer=null;for(Type t=nativeType;t!=null&&pointer==null;t=t.BaseType)pointer=t.GetField("_nativePointer",All|BindingFlags.DeclaredOnly);
        if(pointer==null)throw new Exception("Missing native pointer fixture ABI");
        IntPtr table=Marshal.AllocHGlobal(160*IntPtr.Size),instance=Marshal.AllocHGlobal(IntPtr.Size);var roots=new List<Delegate>();var calls=new Dictionary<string,int>();bool arguments=true;
        var sessionField=typeof(PortalCaptureIntegrationNative).GetField("session",All);
        var harness=new Harmony("HDR.Portal.CompiledDrawFixture");
        try
        {
            for(int i=0;i<160;i++)Marshal.WriteIntPtr(table,i*IntPtr.Size,IntPtr.Zero);Marshal.WriteIntPtr(instance,table);
            GC.SuppressFinalize(native);pointer.SetValue(native,Pointer.Box(instance.ToPointer(),pointer.FieldType));
            foreach(var draw in drawMethods)
            {
                string name=draw.Name;Action<IntPtr> entered=c=>{arguments&=c==instance;calls[name]=calls.ContainsKey(name)?calls[name]+1:1;};Delegate sink;
                switch(name)
                {
                    case "Draw":sink=new S2((c,a,b)=>{entered(c);arguments&=a==3&&b==5;});break;
                    case "DrawAuto":sink=new S0(c=>entered(c));break;
                    case "DrawIndexed":sink=new S3((c,a,b,d)=>{entered(c);arguments&=a==3&&b==5&&d==7;});break;
                    case "DrawInstanced":sink=new S4((c,a,b,d,e)=>{entered(c);arguments&=a==3&&b==5&&d==7&&e==11;});break;
                    case "DrawIndexedInstanced":sink=new S5((c,a,b,d,e,f)=>{entered(c);arguments&=a==3&&b==5&&d==7&&e==11&&f==13;});break;
                    default:sink=new SI((c,b,o)=>{entered(c);arguments&=b==IntPtr.Zero&&o==5;});break;
                }
                var nativeMethod=PortalCaptureCoverage.Calls(draw).Single(m=>m.DeclaringType.FullName=="SharpDX.Direct3D11.DeviceContext"&&m.Name.StartsWith("Draw"));
                var il=PatchProcessor.GetOriginalInstructions(nativeMethod);int size=il.FindIndex(i=>i.opcode==OpCodes.Sizeof);int slot=Convert.ToInt32(il[size-2].operand);
                roots.Add(sink);Marshal.WriteIntPtr(table,slot*IntPtr.Size,Marshal.GetFunctionPointerForDelegate(sink));
            }
            var iface=DirectCameraCaptureNative.FindType(null,"VRage.Render11.Resources.IBuffer");var buffer=DispatchProxy.Create(iface,typeof(BufferProxy));
            ((BufferProxy)buffer).Buffer=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(DirectCameraCaptureNative.FindType(null,"SharpDX.Direct3D11.Buffer"));
            foreach(var draw in drawMethods)
            {
                var scope=new PortalCaptureIntegrationNative.Session(new PortalCaptureGate(integration.Epoch,44,1,true,true),64,64,new Dictionary<object,PortalDrawRole>{{shader,PortalDrawRole.Auxiliary}});sessionField.SetValue(integration,scope);DirectCameraCapture.IsCapturing=true;
                var bridge=typeof(PortalCaptureDrawBridges).GetMethod(draw.Name,All);object[] args;
                if(draw.Name=="DrawIndexedInstancedIndirect")args=new[]{context,buffer,(object)5};
                else args=new[]{context}.Concat(new[]{3,5,7,11,13}.Take(draw.GetParameters().Length).Select(n=>(object)n)).ToArray();
                bridge.Invoke(null,args);
                check(calls.TryGetValue(draw.Name,out int count)&&count==1,"Actual installed "+draw.Name+" managed/native calli reaches only a CPU fake-vtable sentinel");
                check(integration.EndAfterJoin(scope,true),"Actual compiled "+draw.Name+" traverses one audited call site and preserves shader/lifetime: "+scope.Gate.Failure);
            }
            check(arguments,"Opaque managed bridges preserve every receiver and integer/buffer argument without per-draw reflection in production");
            // Remove only the native detour for one terminal wrapper: this
            // models the same observed counter state as a JIT-inlined SharpDX
            // body. The call-site guard remains mandatory and executes.
            var nativeDraw=DirectCameraCaptureNative.FindType(null,"SharpDX.Direct3D11.DeviceContext").GetMethod("Draw",All,null,new[]{typeof(int),typeof(int)},null);
            var guard=typeof(PortalCaptureIntegrationNative).GetMethod("BeforeNativeDraw",All);new Harmony("HDRClientRenderer.PortalCaptureCoverage").Unpatch(nativeDraw,guard);
            try
            {
                var scope=new PortalCaptureIntegrationNative.Session(new PortalCaptureGate(integration.Epoch,45,1,true,true),64,64,new Dictionary<object,PortalDrawRole>{{shader,PortalDrawRole.Auxiliary}});sessionField.SetValue(integration,scope);
                typeof(PortalCaptureDrawBridges).GetMethod("Draw",All).Invoke(null,new[]{context,(object)3,5});check(integration.EndAfterJoin(scope,true),"Missing/inlined native detour cannot break the independently executed terminal call-site proof");
            }
            finally{new Harmony("HDRClientRenderer.PortalCaptureCoverage").Patch(nativeDraw,prefix:new HarmonyMethod(guard){priority=Priority.Last});}
        }
        finally
        {
            DirectCameraCapture.IsCapturing=false;sessionField.SetValue(integration,null);harness.UnpatchAll(harness.Id);pointer.SetValue(native,Pointer.Box(null,pointer.FieldType));Marshal.FreeHGlobal(instance);Marshal.FreeHGlobal(table);GC.KeepAlive(roots);
        }
    }
}
