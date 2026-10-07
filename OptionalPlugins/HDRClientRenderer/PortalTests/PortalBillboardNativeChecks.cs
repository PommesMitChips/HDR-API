using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using HDRClientRenderer;
using VRageMath;
internal static class PortalBillboardNativeChecks
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static readonly Dictionary<string,object> cpuReturns=new Dictionary<string,object>();
    static object CpuReturn(string key){return cpuReturns[key];}
    static IEnumerable<CodeInstruction> CpuGetter(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
    {var t=((MethodInfo)__originalMethod).ReturnType;return new[]{new CodeInstruction(OpCodes.Ldstr,__originalMethod.DeclaringType.FullName+"."+__originalMethod.Name),new CodeInstruction(OpCodes.Call,typeof(PortalBillboardNativeChecks).GetMethod("CpuReturn",All)),new CodeInstruction(t.IsValueType?OpCodes.Unbox_Any:OpCodes.Castclass,t),new CodeInstruction(OpCodes.Ret)};}
    static IEnumerable<CodeInstruction> Noop(IEnumerable<CodeInstruction> instructions){return new[]{new CodeInstruction(OpCodes.Ret)};}
    static IEnumerable<CodeInstruction> ManagerCpu(IEnumerable<CodeInstruction> instructions)
    {var field=DirectCameraCaptureNative.FindType(null,"VRage.Render11.Common.MyManagers").GetField("Buffers",All);return new[]{new CodeInstruction(OpCodes.Ldstr,"CPU.BufferManager"),new CodeInstruction(OpCodes.Call,typeof(PortalBillboardNativeChecks).GetMethod("CpuReturn",All)),new CodeInstruction(OpCodes.Castclass,field.FieldType),new CodeInstruction(OpCodes.Stsfld,field),new CodeInstruction(OpCodes.Ret)};}
    public class ResourceProxy:DispatchProxy
    {
        internal object Srv,Buffer,Render;
        protected override object Invoke(MethodInfo method,object[] args)
        {if(method.Name=="get_Srv")return Srv;if(method.Name=="get_Buffer")return Buffer;if(method.Name=="get_Render")return Render;return method.ReturnType==typeof(void)?null:method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;}
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DrawSink(IntPtr context,int count,int start,int vertex);
    static object lateStage,lateShader,lateSrv;static int lateMode;
    static void LaterHook()
    {
        if(lateMode==1)lateStage.GetType().GetField("m_pixelShader",All).SetValue(lateStage,lateShader);
        var common=DirectCameraCaptureNative.FindType(null,"VRage.Render11.RenderContext.MyCommonStage");
        if(lateMode==2)((Array)common.GetField("m_srvs",All).GetValue(lateStage)).SetValue(lateSrv,0);
        if(lateMode==3)((Array)common.GetField("m_constantBuffers",All).GetValue(lateStage)).SetValue(null,7);
        if(lateMode==4)throw new InvalidOperationException("CPU later-hook draw failure");
    }
    internal static unsafe void Run(Action<bool,string> check)
    {
        Func<string,Type> type=n=>DirectCameraCaptureNative.FindType(null,n);var assembly=type("VRageRender.MyRender11").Assembly;
        var certificate=PortalBillboardProjectiveNative.CreateBindingCertificate(assembly);
        check(certificate.Verified,"Actual installed Render CFG and unconditional all-stage t30 binding produce a sealed CPU certificate");
        var forged=RuntimeHelpers.GetUninitializedObject(typeof(PortalBillboardProjectiveNative.NativeBindingCertificate));check(!((PortalBillboardProjectiveNative.NativeBindingCertificate)forged).Verified,"Uninitialized certificate cannot authorize shader resource reactivation");
        Type contextType=type("VRage.Render11.RenderContext.MyRenderContext"),psType=type("VRage.Render11.RenderContext.MyPixelStage"),commonType=type("VRage.Render11.RenderContext.MyCommonStage");
        Type billboard=type("VRageRender.MyBillboardRenderer"),batchType=type("VRageRender.MyBillboardRendererBatch");
        object New(Type t){var value=RuntimeHelpers.GetUninitializedObject(t);GC.SuppressFinalize(value);return value;}
        object Proxy(string name){var p=DispatchProxy.Create(type(name),typeof(ResourceProxy));((ResourceProxy)p).Buffer=New(type("SharpDX.Direct3D11.Buffer"));((ResourceProxy)p).Srv=New(type("SharpDX.Direct3D11.ShaderResourceView"));return p;}
        object context=New(contextType),stage=New(psType),native=New(type("SharpDX.Direct3D11.DeviceContext1"));
        object original=New(type("SharpDX.Direct3D11.PixelShader")),replacement=New(type("SharpDX.Direct3D11.PixelShader")),gpuDevice=New(type("SharpDX.Direct3D11.Device1"));
        object target=Proxy("VRage.Render11.Resources.ISrvBindable"),unrelated=Proxy("VRage.Render11.Resources.ISrvBindable"),sb=Proxy("VRage.Render11.Resources.ISrvBuffer"),savedB7=Proxy("VRage.Render11.Resources.IConstantBuffer"),privateB7=Proxy("VRage.Render11.Resources.IConstantBuffer");
        contextType.GetField("m_pixelShaderStage",All).SetValue(context,stage);contextType.GetField("m_deviceContext",All).SetValue(context,native);
        contextType.GetField("m_vertexShaderStage",All).SetValue(context,New(type("VRage.Render11.RenderContext.MyVertexStage")));
        var stats=contextType.GetField("m_statistics",All);object statistics=New(stats.FieldType);stats.SetValue(context,statistics);
        commonType.GetField("m_statistics",All).SetValue(stage,statistics);commonType.GetField("m_deviceContext",All).SetValue(stage,native);commonType.GetField("m_shaderStage",All).SetValue(stage,New(type("SharpDX.Direct3D11.PixelShaderStage")));
        psType.GetField("m_pixelShader",All).SetValue(stage,original);var srvs=Array.CreateInstance(type("SharpDX.Direct3D11.ShaderResourceView"),32);srvs.SetValue(((ResourceProxy)sb).Srv,30);commonType.GetField("m_srvs",All).SetValue(stage,srvs);
        var cbs=Array.CreateInstance(type("VRage.Render11.Resources.IConstantBuffer"),8);cbs.SetValue(savedB7,7);commonType.GetField("m_constantBuffers",All).SetValue(stage,cbs);
        var p=MatrixD.Zero;p.M11=p.M22=1;p.M34=-1;p.M43=.1;var pose=MatrixD.CreateTranslation(0,0,5);var view=new PortalPrimaryView(pose,p,20,1920,1080,7);PortalPrimaryView observed=view;
        PortalSurfaceMap entry,exit,shell;PortalSurfaceMap.TryPlane(MatrixD.Identity,2,2,out entry);PortalSurfaceMap.TryPlane(MatrixD.CreateTranslation(0,0,-10),2,2,out exit);PortalSurfaceMap.TryEllipsoid(MatrixD.CreateTranslation(0,0,-10),new Vector3D(2),out shell);
        PortalRayMapSpec spec;check(PortalRayMapSpec.TryCreate(7,81,entry,0,exit,0,shell,pose,MatrixD.CreateTranslation(0,0,-5),p,PortalRayTransport.Differential,PortalImageProjection.Perspective,PortalRayAccuracy.ApproximateShell,1,out spec),"CPU projective batch fixture has an authorized plane mapping");
        PortalProjectiveSampling sampling;check(PortalProjectiveSampling.TryCreate(spec,view,512,512,1,1,out sampling),"CPU projective batch fixture has immutable matching sampling metadata");
        var publications=new List<KeyValuePair<object,PortalProjectiveSampling>>{new KeyValuePair<object,PortalProjectiveSampling>(target,sampling)};
        var harness=new Harmony("HDR.Portal.ProjectiveBatchFixture");PortalBillboardProjectiveNative adapter=null;PortalCaptureIntegrationNative captureGuard=null;var list=(IList)billboard.GetField("m_batches",All).GetValue(null);var oldList=list.Cast<object>().ToArray();
        var saved=new Dictionary<FieldInfo,object>();void SaveSet(FieldInfo field,object value){var previous=field.GetValue(null);field.SetValue(null,value);saved[field]=previous;}
        void Return(MethodInfo method,object value){cpuReturns[method.DeclaringType.FullName+"."+method.Name]=value;harness.Patch(method,transpiler:new HarmonyMethod(typeof(PortalBillboardNativeChecks).GetMethod("CpuGetter",All)));}
        void Skip(MethodInfo method){harness.Patch(method,transpiler:new HarmonyMethod(typeof(PortalBillboardNativeChecks).GetMethod("Noop",All)));}
        FieldInfo pointer=null;for(Type t=native.GetType();t!=null&&pointer==null;t=t.BaseType)pointer=t.GetField("_nativePointer",All|BindingFlags.DeclaredOnly);
        IntPtr table=Marshal.AllocHGlobal(160*IntPtr.Size),instance=Marshal.AllocHGlobal(IntPtr.Size);int draws=0;bool bound=true;DrawSink sink=(c,count,start,vertex)=>{draws++;bound&=c==instance&&count==6&&start==0&&vertex==0&&ReferenceEquals(psType.GetField("m_pixelShader",All).GetValue(stage),replacement)&&ReferenceEquals(cbs.GetValue(7),privateB7);};
        var render=billboard.GetMethods(All).Single(m=>m.Name=="Render"&&m.GetParameters().Length==5);var rcDraw=contextType.GetMethod("DrawIndexed",All,null,new[]{typeof(int),typeof(int),typeof(int)},null);
        var nativeDraw=type("SharpDX.Direct3D11.DeviceContext").GetMethod("DrawIndexed",All,null,new[]{typeof(int),typeof(int),typeof(int)},null);
        try
        {
            for(int i=0;i<160;i++)Marshal.WriteIntPtr(table,i*IntPtr.Size,IntPtr.Zero);Marshal.WriteIntPtr(instance,table);pointer.SetValue(native,Pointer.Box(instance.ToPointer(),pointer.FieldType));var nativeIl=PatchProcessor.GetOriginalInstructions(nativeDraw);int size=nativeIl.FindIndex(i=>i.opcode==OpCodes.Sizeof);int slot=Convert.ToInt32(nativeIl[size-2].operand);Marshal.WriteIntPtr(table,slot*IntPtr.Size,Marshal.GetFunctionPointerForDelegate(sink));
            var debugGetter=type("VRageRender.MyRender11").GetProperty("DebugOverrides",All).GetGetMethod(true);object debug=New(debugGetter.ReturnType);debug.GetType().GetField("BillboardsDynamic",All).SetValue(debug,true);debug.GetType().GetField("OIT",All).SetValue(debug,true);Return(debugGetter,debug);
            Return(type("VRageRender.MyRender11").GetProperty("DeviceInstance",All).GetGetMethod(true),gpuDevice);Return(type("VRageRender.MyCommon").GetProperty("FrameCounter",All).GetGetMethod(true),20L);
            Return(type("VRageRender.MyStereoRender").GetProperty("Enable",All).GetGetMethod(true),false);Return(type("VRageRender.MyTransparentRendering").GetMethod("DisplayTransparencyHeatMap",All),false);
            Skip(billboard.GetMethod("BindResourcesCommon",All));Skip(contextType.GetMethods(All).Single(m=>m.Name=="SetPrimitiveTopology"));Skip(contextType.GetMethods(All).Single(m=>m.Name=="SetRasterizerState"));Skip(type("VRage.Render11.Tools.MyRendererStats").GetMethod("AddBillboardRenderStats",All));
            var vsSet=type("VRage.Render11.RenderContext.MyVertexStage").GetMethods(All).Single(m=>m.Name=="Set"&&m.GetParameters().Length==1);Skip(vsSet);
            foreach(var m in PortalCaptureCoverage.Calls(render).Where(m=>m.Name=="op_Implicit").Cast<MethodInfo>())Return(m,m.ReturnType.FullName=="SharpDX.Direct3D11.PixelShader"?original:null);
            var psSet=psType.GetMethods(All).Single(m=>m.Name=="Set"&&m.GetParameters().Length==1);var terminalSet=(MethodInfo)PortalCaptureCoverage.Calls(psSet).Single(m=>m.Name=="Set");Skip(terminalSet);Return(type("SharpDX.Direct3D11.DeviceContext").GetProperty("PixelShader",All).GetGetMethod(true),New(type("SharpDX.Direct3D11.PixelShaderStage")));
            var setSrv=commonType.GetMethod("SetSrv",All,null,new[]{typeof(int),type("VRage.Render11.Resources.ISrvBindable")},null);Skip((MethodInfo)PortalCaptureCoverage.Calls(setSrv).Single(m=>m.Name=="SetShaderResource"));
            object platform=Proxy("VRage.IVRagePlatform"),vrageRender=Proxy("VRage.IVRageRender");((ResourceProxy)platform).Render=vrageRender;Return(type("VRage.MyVRage").GetProperty("Platform",All).GetGetMethod(true),platform);
            var bufferManager=type("VRage.Render11.Resources.MyBufferManager");cpuReturns["CPU.BufferManager"]=New(bufferManager);harness.Patch(type("VRage.Render11.Common.MyManagers").TypeInitializer,transpiler:new HarmonyMethod(typeof(PortalBillboardNativeChecks).GetMethod("ManagerCpu",All)));Return(bufferManager.GetMethods(All).Single(m=>m.Name=="CreateConstantBuffer"&&m.GetParameters().Length==5),privateB7);Skip(bufferManager.GetMethod("Dispose",All,null,new[]{type("VRage.Render11.Resources.IConstantBuffer").MakeArrayType()},null));
            var mapping=type("VRageRender.MyMapping");var mapped=mapping.GetMethod("MapDiscard",All,null,new[]{contextType,type("VRage.Render11.Resources.IBuffer")},null);Return(mapped,Activator.CreateInstance(mapping));Skip(mapping.GetMethod("Unmap",All,null,Type.EmptyTypes,null));Skip(mapping.GetMethods(All).Single(m=>m.Name=="WriteAndPosition"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==3).MakeGenericMethod(typeof(float)));
            SaveSet(type("VRage.Render11.Resources.MyGBuffer").GetField("Main",All),New(type("VRage.Render11.Resources.MyGBuffer")));SaveSet(billboard.GetField("m_SB",All),sb);SaveSet(billboard.GetField("m_billboardCountSafe",All),1);
            var arraysField=billboard.GetField("m_arrayDataBillboards",All);object arrays=Activator.CreateInstance(arraysField.FieldType);var verticesField=arraysField.FieldType.GetField("Vertex",All);var dataField=arraysField.FieldType.GetField("Data",All);var vertices=Array.CreateInstance(verticesField.FieldType.GetElementType(),1);var data=Array.CreateInstance(dataField.FieldType.GetElementType(),1);
            object quad=Activator.CreateInstance(vertices.GetType().GetElementType());var pointValues=new[]{new Vector3(-1,1,-5),new Vector3(1,1,-5),new Vector3(1,-1,-5),new Vector3(-1,-1,-5)};
            for(int i=0;i<4;i++){var corner=quad.GetType().GetField("V"+i,All);object vertex=Activator.CreateInstance(corner.FieldType);corner.FieldType.GetField("Position",All).SetValue(vertex,pointValues[i]);corner.SetValue(quad,vertex);}vertices.SetValue(quad,0);object record=Activator.CreateInstance(data.GetType().GetElementType());var custom=record.GetType().GetField("CustomProjectionID",All);custom.SetValue(record,-1);data.SetValue(record,0);verticesField.SetValue(arrays,vertices);dataField.SetValue(arrays,data);SaveSet(arraysField,arrays);
            object batch=Activator.CreateInstance(batchType);batchType.GetField("Texture",All).SetValue(batch,target);batchType.GetField("Num",All).SetValue(batch,1);batchType.GetField("Offset",All).SetValue(batch,0);list.Clear();list.Add(batch);
            adapter=new PortalBillboardProjectiveNative(assembly,()=>publications.ToArray());check(!adapter.Available&&adapter.TryInstall(),"Actual installed batch adapter installs reversibly without allocating a GPU object");
            typeof(PortalBillboardProjectiveNative).GetField("currentDevice",All).SetValue(adapter,gpuDevice);typeof(PortalBillboardProjectiveNative).GetField("readView",All).SetValue(adapter,(Func<long,PortalPrimaryView>)(epoch=>observed));
            var variantType=typeof(PortalBillboardProjectiveNative).GetNestedType("Variant",All);object variant=Activator.CreateInstance(variantType,true);variantType.GetField("Shader",All).SetValue(variant,replacement);((IDictionary)typeof(PortalBillboardProjectiveNative).GetField("variants",All).GetValue(adapter)).Add(original,variant);
            object bucket=Activator.CreateInstance(render.GetParameters()[2].ParameterType);bucket.GetType().GetField("Count",All).SetValue(bucket,1);bucket.GetType().GetField("StartIndex",All).SetValue(bucket,0);
            void Render(){render.Invoke(null,new[]{context,null,bucket,(object)true,false});}
            void Restored(string name){check(ReferenceEquals(psType.GetField("m_pixelShader",All).GetValue(stage),original)&&ReferenceEquals(cbs.GetValue(7),savedB7)&&ReferenceEquals(srvs.GetValue(0),((ResourceProxy)target).Srv)&&ReferenceEquals(srvs.GetValue(30),((ResourceProxy)sb).Srv),name);}
            check(adapter.BeginPrimary(view,context),"Primary owner freezes targets and uploads projective constants before native batches");Render();check(draws==1&&bound,"Actual installed Render batch and indexed native calli select private PS/b7 only at CPU fake-vtable terminal");Restored("Actual native setters restore shader, b7, texture and structured SRV caches after each projective draw");
            captureGuard=new PortalCaptureIntegrationNative(assembly);check(captureGuard.TryInstall(),"Native secondary capture draw guard can install alongside the private primary adapter: "+captureGuard.Reason);Render();check(draws==2&&bound,"Exact primary batch seam retains typed native draws when capture bridges and both terminal guards are installed");Restored("Primary/capture hook coexistence preserves native shader and binding caches");
            lateStage=stage;lateShader=New(type("SharpDX.Direct3D11.PixelShader"));lateSrv=New(type("SharpDX.Direct3D11.ShaderResourceView"));var later=typeof(PortalBillboardNativeChecks).GetMethod("LaterHook",All);harness.Patch(rcDraw,prefix:new HarmonyMethod(later));
            for(int mode=1;mode<=3;mode++){lateMode=mode;int before=draws;Render();check(draws==before,"Actual terminal guard rejects a later native hook mutating "+(mode==1?"PS":mode==2?"t0":"b7"));Restored("Late-hook mutation restores all projective batch caches "+mode);}
            lateMode=4;bool failure=false;try{Render();}catch(TargetInvocationException ex){failure=ex.GetBaseException().Message=="CPU later-hook draw failure";}check(failure,"Main renderer draw exception propagates unchanged through projective batch cleanup");Restored("Projective PS/b7 cleanup survives actual managed draw exceptions");lateMode=0;harness.Unpatch(rcDraw,later);
            observed=new PortalPrimaryView(MatrixD.CreateTranslation(.01,0,5),p,20,1920,1080,7);int baseline=draws;Render();check(draws==baseline,"Foreign auxiliary camera cannot reuse a primary proof merely by leaving IsCapturing false");observed=view;
            var otherProjection=p;otherProjection.M31=.05;observed=new PortalPrimaryView(pose,otherProjection,20,1920,1080,7);Render();check(draws==baseline,"Off-axis auxiliary projection cannot reuse the captured primary proof");observed=new PortalPrimaryView(pose,p,20,1600,900,7);Render();check(draws==baseline,"Auxiliary viewport cannot reuse the primary resolution proof");observed=view;
            var gbufferField=type("VRage.Render11.Resources.MyGBuffer").GetField("Main",All);object primaryBuffer=gbufferField.GetValue(null);gbufferField.SetValue(null,New(gbufferField.FieldType));Render();check(draws==baseline,"Swapped native GBuffer cannot consume a frozen primary proof");gbufferField.SetValue(null,primaryBuffer);
            string deviceKey="VRageRender.MyRender11.get_DeviceInstance";cpuReturns[deviceKey]=New(type("SharpDX.Direct3D11.Device1"));Render();check(draws==baseline,"Native device identity must match private shader and primary resources");cpuReturns[deviceKey]=gpuDevice;
            cpuReturns["VRageRender.MyCommon.get_FrameCounter"]=21L;Render();check(draws==baseline,"An old primary frame cannot consume the published private billboard proof");cpuReturns["VRageRender.MyCommon.get_FrameCounter"]=20L;
            srvs.SetValue(((ResourceProxy)unrelated).Srv,30);Render();check(draws==baseline,"Owned portal rejects an unproven native structured billboard t30 binding");srvs.SetValue(((ResourceProxy)sb).Srv,30);
            string pixelId="VRageRender.MyPixelShaders+Id.op_Implicit";var implicitPs=PortalCaptureCoverage.Calls(render).Cast<MethodInfo>().First(m=>m.Name=="op_Implicit"&&m.ReturnType.FullName=="SharpDX.Direct3D11.PixelShader");pixelId=implicitPs.DeclaringType.FullName+"."+implicitPs.Name;cpuReturns[pixelId]=lateShader;Render();check(draws==baseline,"Unknown native shader omits only the owned raw portal batch");cpuReturns[pixelId]=original;
            cpuReturns["VRageRender.MyStereoRender.get_Enable"]=true;Render();check(draws==baseline,"Owned projective batch fails closed on the exact installed stereo draw branch");cpuReturns["VRageRender.MyStereoRender.get_Enable"]=false;
            custom.SetValue(record,1);data.SetValue(record,0);Render();check(draws==baseline,"Owned batch with custom projection fails closed before GPU draw");custom.SetValue(record,-1);data.SetValue(record,0);
            var bad=quad.GetType().GetField("V3",All);var badPoint=bad.GetValue(quad);bad.FieldType.GetField("Position",All).SetValue(badPoint,new Vector3(0,0,-4));bad.SetValue(quad,badPoint);vertices.SetValue(quad,0);Render();check(draws==baseline,"Every gathered corner must lie inside the authorized entry plane/chart");
            var primaryField=typeof(PortalBillboardProjectiveNative).GetField("primary",All);object currentPrimary=primaryField.GetValue(adapter);var activeDraws=currentPrimary.GetType().GetField("Draws",All);activeDraws.SetValue(currentPrimary,1);bool refused=false;try{adapter.EndPrimary();}catch(InvalidOperationException){refused=true;}check(refused,"Primary resource retirement refuses unjoined deferred worker draws");activeDraws.SetValue(currentPrimary,0);
            adapter.EndPrimary();Render();check(draws==baseline,"Known raw front without an active primary never falls through to ordinary UV sampling");
            publications[0]=new KeyValuePair<object,PortalProjectiveSampling>(target,null);check(adapter.BeginPrimary(view,context),"Known raw target remains owned with missing immutable sampling metadata");Render();check(draws==baseline,"Nullable raw publication metadata omits only its owned batch");
            DirectCameraCapture.IsCapturing=true;Render();check(draws==baseline,"Known raw primary front is omitted from private secondary captures");DirectCameraCapture.IsCapturing=false;
            batchType.GetField("Texture",All).SetValue(batch,unrelated);list.Clear();list.Add(batch);Render();check(draws==baseline+1,"Unrelated actual native billboard batches keep the original render path");adapter.EndPrimary();
            check(Harmony.GetPatchInfo(render).Owners.Contains("HDRClientRenderer.PortalBillboardProjective"),"Private batch hook retains exact installed render coverage");
            // Original-shader identities are reconciled before the fixed quota;
            // old compile work remains CPU-only and cannot attach after an epoch.
            var registryReturn=typeof(PortalClipShaders).GetMethod("ReadRegistry",All);Return(registryReturn,new List<PortalClipDescriptor>());typeof(PortalBillboardProjectiveNative).GetField("assetsVerified",All).SetValue(adapter,true);
            var variants=(IDictionary)typeof(PortalBillboardProjectiveNative).GetField("variants",All).GetValue(adapter);for(int i=variants.Count;i<128;i++)variants.Add(new object(),Activator.CreateInstance(variantType,true));adapter.PrepareRender(7);check(variants.Count==0,"Live billboard registry reconciliation reclaims all stale identities before the 128-variant quota");
            var completion=new System.Threading.Tasks.TaskCompletionSource<byte[]>();object pending=Activator.CreateInstance(variantType,true);variantType.GetField("Job",All).SetValue(pending,completion.Task);variants.Add(new object(),pending);adapter.PrepareRender(8);var retired=(IList)typeof(PortalBillboardProjectiveNative).GetField("retiredJobs",All).GetValue(adapter);check(variants.Count==0&&retired.Count==1,"Device epoch drops stale attachment work while its CPU job still counts toward the worker bound");completion.SetResult(new byte[]{1});adapter.PrepareRender(8);check(retired.Count==0,"Finished retired CPU work is reclaimed without attaching its old bytecode");
            captureGuard.Dispose();captureGuard=null;check(Harmony.GetPatchInfo(render).Owners.Contains("HDRClientRenderer.PortalBillboardProjective"),"Unpatching the secondary capture owner preserves the private primary batch adapter");adapter.Dispose();adapter=null;check(!Harmony.GetPatchInfo(render).Owners.Contains("HDRClientRenderer.PortalBillboardProjective"),"Disposal removes only this adapter's reversible hooks");
        }
        finally
        {
            DirectCameraCapture.IsCapturing=false;lateMode=0;if(captureGuard!=null)captureGuard.Dispose();if(adapter!=null)adapter.Dispose();harness.UnpatchAll(harness.Id);foreach(var item in saved)item.Key.SetValue(null,item.Value);list.Clear();foreach(var item in oldList)list.Add(item);pointer.SetValue(native,Pointer.Box(null,pointer.FieldType));Marshal.FreeHGlobal(instance);Marshal.FreeHGlobal(table);cpuReturns.Clear();GC.KeepAlive(sink);
        }
    }
    internal static void Dump()
    {
        if(Environment.GetEnvironmentVariable("HDR_PORTAL_BILLBOARD_NATIVE_ONLY")=="1"){int count=0;Run((value,name)=>{count++;if(!value)throw new Exception(name);});Console.WriteLine("PASS: "+count+" projective billboard native CPU checks.");return;}
        foreach(string name in new[]{"VRageRender.MyBillboardRenderer","VRageRender.MyBillboardDataArray","VRageRender.MyBillboardVertexData","VRageRender.MyBillboardData","VRage.Render11.RenderContext.MyCommonStage","VRage.Render11.RenderContext.MyPixelStage","VRage.Render11.RenderContext.MyAllShaderStages"})
        {
            var t=DirectCameraCaptureNative.FindType(null,name);
            foreach(var f in t.GetFields(All))Console.WriteLine("FIELD "+name+" "+f);
            foreach(var nested in t.GetNestedTypes(All))foreach(var f in nested.GetFields(All))Console.WriteLine("NESTED FIELD "+nested.FullName+" "+f);
            foreach(var m in t.GetMethods(All).Where(m=>name.EndsWith("MyBillboardRenderer")?(m.Name.Contains("Render")||m.Name.Contains("Draw")||m.Name=="BindResourcesCommon"):(m.Name=="SetConstantBuffer"||m.Name=="Set"||m.Name=="SetSrv")))
            {Console.WriteLine("BODY "+m);foreach(var i in PatchProcessor.GetOriginalInstructions(m))Console.WriteLine(i);}
        }
    }
}
