using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using VRageMath;
namespace HDRClientRenderer
{
    // Main-primary batch adapter. Only a frozen, owner-proven target identity
    // selects a private PS. The two native billboard draw seams carry their
    // exact texture argument; ordinary draws do not inspect renderer state.
    internal sealed class PortalBillboardProjectiveNative : IDisposable
    {
        internal sealed class NativeBindingCertificate
        {
            static readonly object seal=new object();readonly object proof;readonly Guid module;
            NativeBindingCertificate(Guid module){this.module=module;proof=seal;}
            internal bool Verified{get{return ReferenceEquals(proof,seal)&&DirectCameraCaptureNative.FindType(null,"VRageRender.MyBillboardRenderer").Assembly.ManifestModule.ModuleVersionId==module;}}
            internal static NativeBindingCertificate Create(Assembly assembly)
            {
                PortalCaptureCoverage.ValidateRuntime();Func<string,Type> type=n=>DirectCameraCaptureNative.FindType(assembly,n);
                var billboards=type("VRageRender.MyBillboardRenderer");var render=billboards.GetMethods(All).Single(m=>m.Name=="Render"&&m.GetParameters().Length==5);
                var indexed=type("VRage.Render11.RenderContext.MyRenderContext").GetMethod("DrawIndexed",All,null,new[]{typeof(int),typeof(int),typeof(int)},null);
                var stereo=type("VRageRender.MyStereoRender").GetMethods(All).Single(m=>m.Name=="DrawIndexedBillboards");
                ValidateSeam(render,indexed,stereo,type("VRageRender.MyBillboardRendererBatch"));ValidateBillboardBuffer(billboards);
                var body=PatchProcessor.GetOriginalInstructions(render);var bind=billboards.GetMethod("BindResourcesCommon",All);int at=body.FindIndex(i=>Equals(i.operand,bind));
                if(at<0||body.Count(i=>Equals(i.operand,bind))!=1)throw new InvalidOperationException("Native billboard buffer binding lost dominance.");
                foreach(var draw in new[]{indexed,stereo})if(ReachableWithout(body,at,body.FindIndex(i=>Equals(i.operand,draw))))throw new InvalidOperationException("Billboard draw can bypass native buffer binding.");
                for(int i=at+1;i<body.Count;i++)if(body[i].operand is MethodInfo)
                {
                    var call=(MethodInfo)body[i].operand;
                    if(call.Name=="SetSrv"&&(i<2||(body[i-2].opcode!=OpCodes.Ldc_I4_0&&body[i-2].opcode!=OpCodes.Ldc_I4_1)))throw new InvalidOperationException("Billboard render can overwrite native t30.");
                    if(call.Name=="ClearState"||call.Name=="SetSrvs")throw new InvalidOperationException("Billboard render can invalidate native t30.");
                }
                var stages=type("VRage.Render11.RenderContext.MyAllShaderStages");var all=stages.GetMethods(All).Single(m=>m.Name=="SetSrv"&&m.GetParameters().Length==2);var il=PatchProcessor.GetOriginalInstructions(all);
                int pixel=il.FindIndex(i=>i.opcode==OpCodes.Ldfld&&Equals(i.operand,stages.GetField("m_pixelStage",All)));
                if(pixel<0||il.Count!=16||il.Any(i=>i.opcode.FlowControl==FlowControl.Branch||i.opcode.FlowControl==FlowControl.Cond_Branch)||il[pixel+1].opcode!=OpCodes.Ldarg_1||il[pixel+2].opcode!=OpCodes.Ldarg_2||!(il[pixel+3].operand is MethodInfo)||((MethodInfo)il[pixel+3].operand).Name!="SetSrv")throw new InvalidOperationException("Native all-stage pixel t30 propagation changed.");
                return new NativeBindingCertificate(billboards.Assembly.ManifestModule.ModuleVersionId);
            }
        }
        internal static NativeBindingCertificate CreateBindingCertificate(Assembly assembly){return NativeBindingCertificate.Create(assembly);}
        static bool ReachableWithout(List<CodeInstruction> body,int blocked,int target)
        {
            var pending=new Stack<int>();var seen=new HashSet<int>();pending.Push(0);
            while(pending.Count!=0)
            {
                int i=pending.Pop();if(i<0||i>=body.Count||i==blocked||!seen.Add(i))continue;if(i==target)return true;var instruction=body[i];
                if(instruction.operand is Label)pending.Push(body.FindIndex(item=>item.labels.Contains((Label)instruction.operand)));
                if(instruction.operand is Label[])foreach(var label in (Label[])instruction.operand)pending.Push(body.FindIndex(item=>item.labels.Contains(label)));
                if(instruction.opcode.FlowControl!=FlowControl.Branch&&instruction.opcode.FlowControl!=FlowControl.Return&&instruction.opcode.FlowControl!=FlowControl.Throw)pending.Push(i+1);
            }
            return false;
        }
        const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        const string PatchId="HDRClientRenderer.PortalBillboardProjective";
        sealed class Identity:IEqualityComparer<object>
        {internal static readonly Identity Instance=new Identity();public new bool Equals(object a,object b){return ReferenceEquals(a,b);}public int GetHashCode(object o){return RuntimeHelpers.GetHashCode(o);}}
        sealed class Variant{internal PortalClipDescriptor Descriptor;internal Task<byte[]> Job;internal object Shader;internal string Failure;internal volatile bool Demanded;}
        sealed class Binding{internal object Srv,Buffer;internal PortalProjectiveSampling Sampling;}
        sealed class Primary
        {internal readonly Dictionary<object,Binding> Targets;internal readonly HashSet<object> Owned;internal readonly long Frame,Epoch;internal readonly PortalPrimaryView View;internal readonly object Device,GBuffer;internal int Draws,Closing;internal Primary(Dictionary<object,Binding> targets,HashSet<object> owned,PortalPrimaryView view,object device,object gbuffer){Targets=targets;Owned=owned;Frame=view.Frame;Epoch=view.Epoch;View=view;Device=device;GBuffer=gbuffer;}}
        delegate object Read(object owner);
        delegate void Set(object owner,object value);
        delegate void SetBuffer(object owner,int slot,object value);
        delegate void NativeDraw(object context,int count,int start,int vertex,IntPtr pointer);
        sealed class DrawProof{internal PortalBillboardProjectiveNative Owner;internal Primary Primary;internal object Context,Stage,Shader,Buffer,Srv0,Srv30;internal int Calls;}
        [ThreadStatic] static DrawProof drawing;
        static PortalBillboardProjectiveNative active;
        static NativeDraw indexed,stereo;static IntPtr indexedPointer,stereoPointer;
        readonly Assembly assembly;readonly Func<IEnumerable<KeyValuePair<object,PortalProjectiveSampling>>> snapshot;
        readonly PortalClipShaders compiler;readonly MethodInfo render,drawIndexed,drawStereo;
        readonly Read pixel,shader,srvCache,constantCache,targetSrv;readonly Set setShader;readonly SetBuffer setBuffer,setSrv;
        readonly PropertyInfo device;readonly FieldInfo managers;readonly MethodInfo createBuffer,disposeBuffers,map,write,unmap;
        readonly ConstructorInfo createShader;readonly Type bufferType;
        readonly PropertyInfo immediate;readonly FieldInfo arrayData,billboardCount,vertexArray,dataArray,projectionId;readonly FieldInfo[] points;readonly FieldInfo position;
        readonly FieldInfo billboardBuffer;readonly PropertyInfo frameCounter;
        readonly FieldInfo mainGBuffer;readonly Func<long,PortalPrimaryView> readView;
        readonly NativeBindingCertificate nativeBinding;
        readonly Dictionary<object,Variant> variants=new Dictionary<object,Variant>(Identity.Instance);
        readonly List<Task<byte[]>> retiredJobs=new List<Task<byte[]>>();
        readonly Dictionary<object,object> buffers=new Dictionary<object,object>(Identity.Instance);
        readonly List<MethodBase> patched=new List<MethodBase>();
        object currentDevice;long resourceEpoch=long.MinValue;Primary primary;HashSet<object> ownedRaw=new HashSet<object>(Identity.Instance);Harmony harmony;bool disposed,installed,assetsVerified;int attachedCount;string reason="Projective billboard variants are warming.";
        internal bool Available{get{return installed&&!disposed&&Volatile.Read(ref attachedCount)>0;}}
        internal string Reason{get{return reason;}}
        internal PortalBillboardProjectiveNative(Assembly assembly,Func<IEnumerable<KeyValuePair<object,PortalProjectiveSampling>>> trustedSnapshot)
        {
            this.assembly=assembly; snapshot=trustedSnapshot??throw new ArgumentNullException("trustedSnapshot");
            PortalCaptureCoverage.ValidateRuntime();Func<string,Type> type=n=>DirectCameraCaptureNative.FindType(assembly,n);
            Type context=type("VRage.Render11.RenderContext.MyRenderContext"),ps=type("VRage.Render11.RenderContext.MyPixelStage"),common=type("VRage.Render11.RenderContext.MyCommonStage"),billboards=type("VRageRender.MyBillboardRenderer");
            render=billboards.GetMethods(All).Single(m=>m.Name=="Render"&&m.GetParameters().Length==5);
            drawIndexed=context.GetMethod("DrawIndexed",All,null,new[]{typeof(int),typeof(int),typeof(int)},null);
            drawStereo=type("VRageRender.MyStereoRender").GetMethod("DrawIndexedBillboards",All,null,new[]{context,typeof(int),typeof(int),typeof(int)},null);
            ValidateSeam(render,drawIndexed,drawStereo,type("VRageRender.MyBillboardRendererBatch"));
            pixel=Getter(context.GetField("m_pixelShaderStage",All));shader=Getter(ps.GetField("m_pixelShader",All));
            srvCache=Getter(common.GetField("m_srvs",All));constantCache=Getter(common.GetField("m_constantBuffers",All));
            targetSrv=Getter(type("VRage.Render11.Resources.ISrvBindable").GetProperty("Srv",All));
            var setter=ps.GetMethods(All).Single(m=>m.Name=="Set"&&m.GetParameters().Length==1);
            bufferType=type("VRage.Render11.Resources.IConstantBuffer");var cb=ps.GetMethod("SetConstantBuffer",All,null,new[]{typeof(int),bufferType},null);
            if(cb==null)throw new InvalidOperationException("Projective billboard pixel constant-buffer setter ABI changed.");
            setShader=Setter(setter);setBuffer=BufferSetter(cb);
            setSrv=BufferSetter(common.GetMethod("SetSrv",All,null,new[]{typeof(int),type("VRage.Render11.Resources.ISrvBindable")},null));
            device=type("VRageRender.MyRender11").GetProperty("DeviceInstance",All);
            managers=type("VRage.Render11.Common.MyManagers").GetField("Buffers",All);
            Type manager=type("VRage.Render11.Resources.MyBufferManager"),mapping=type("VRageRender.MyMapping");
            createBuffer=manager.GetMethods(All).Single(m=>m.Name=="CreateConstantBuffer"&&m.GetParameters().Length==5);
            disposeBuffers=manager.GetMethod("Dispose",All,null,new[]{bufferType.MakeArrayType()},null);
            map=mapping.GetMethod("MapDiscard",All,null,new[]{context,type("VRage.Render11.Resources.IBuffer")},null);
            write=mapping.GetMethods(All).Single(m=>m.Name=="WriteAndPosition"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==3).MakeGenericMethod(typeof(float));
            unmap=mapping.GetMethod("Unmap",All,null,Type.EmptyTypes,null);
            createShader=type("SharpDX.Direct3D11.PixelShader").GetConstructor(new[]{type("SharpDX.Direct3D11.Device"),typeof(byte[]),type("SharpDX.Direct3D11.ClassLinkage")});
            if(device==null||managers==null||disposeBuffers==null||map==null||unmap==null||createShader==null)throw new InvalidOperationException("Projective billboard GPU owner ABI changed.");
            compiler=new PortalClipShaders(assembly);
            immediate=type("VRageRender.MyRender11").GetProperty("RC",All);
            arrayData=billboards.GetField("m_arrayDataBillboards",All);billboardCount=billboards.GetField("m_billboardCountSafe",All);
            var arrayType=type("VRageRender.MyBillboardDataArray");vertexArray=arrayType.GetField("Vertex",All);dataArray=arrayType.GetField("Data",All);
            var vertexType=type("VRageRender.MyBillboardVertexData");points=new[]{"V0","V1","V2","V3"}.Select(n=>vertexType.GetField(n,All)).ToArray();
            position=type("VRageRender.Vertex.MyVertexFormatPositionTextureH").GetField("Position",All);
            projectionId=type("VRageRender.MyBillboardData").GetField("CustomProjectionID",All);
            billboardBuffer=billboards.GetField("m_SB",All);frameCounter=type("VRageRender.MyCommon").GetProperty("FrameCounter",All);
            if(immediate==null||arrayData==null||billboardCount==null||vertexArray==null||dataArray==null||projectionId==null||points.Any(p=>p==null)||position==null||position.FieldType!=typeof(Vector3))throw new InvalidOperationException("Projective billboard gathered vertex proof ABI changed.");
            if(billboardBuffer==null||frameCounter==null||frameCounter.PropertyType!=typeof(long)||System.Runtime.InteropServices.Marshal.SizeOf(type("VRageRender.MyBillboardData"))!=48)throw new InvalidOperationException("Projective billboard structured-buffer ABI changed.");
            ValidateBillboardBuffer(billboards);
            nativeBinding=CreateBindingCertificate(assembly);
            mainGBuffer=type("VRage.Render11.Resources.MyGBuffer").GetField("Main",All);
            if(mainGBuffer==null)throw new InvalidOperationException("Projective primary GBuffer ABI unavailable.");
            var nativeView=new PortalPrimaryViewNative(assembly);readView=epoch=>{PortalPrimaryView view;return nativeView.TrySnapshot(epoch,out view)?view:null;};
        }
        internal static void ValidateBillboardBuffer(Type billboards)
        {
            var bind=billboards.GetMethod("BindResourcesCommon",All);var body=PatchProcessor.GetOriginalInstructions(bind);
            int at=body.FindIndex(i=>i.opcode==OpCodes.Ldsfld&&Equals(i.operand,billboards.GetField("m_SB",All)));
            if(at<1||body[at-1].opcode!=OpCodes.Ldc_I4_S||Convert.ToInt32(body[at-1].operand)!=30||at+1>=body.Count||!(body[at+1].operand is MethodInfo)||((MethodInfo)body[at+1].operand).DeclaringType.FullName!="VRage.Render11.RenderContext.MyAllShaderStages"||((MethodInfo)body[at+1].operand).Name!="SetSrv"||
                body.Take(at+2).Any(i=>i.opcode.FlowControl==FlowControl.Branch||i.opcode.FlowControl==FlowControl.Cond_Branch))throw new InvalidOperationException("Native billboard all-stage t30 binding proof changed.");
        }
        internal static void ValidateSeam(MethodInfo render,MethodInfo indexed,MethodInfo stereo,Type batch)
        {
            if(render==null||indexed==null||stereo==null||!render.IsStatic||render.ReturnType!=typeof(void))throw new InvalidOperationException("Projective billboard draw ABI unavailable.");
            var body=PatchProcessor.GetOriginalInstructions(render);
            if(body.Count(i=>Equals(i.operand,indexed))!=1||body.Count(i=>Equals(i.operand,stereo))!=1||
                render.GetMethodBody().LocalVariables.Count<4||render.GetMethodBody().LocalVariables[3].LocalType.FullName!="VRage.Render11.Resources.ISrvBindable"||
                body.Count(i=>i.opcode==OpCodes.Ldfld&&Equals(i.operand,batch.GetField("Texture",All)))!=1||
                body.Any(i=>i.operand is MethodInfo&&((MethodInfo)i.operand).Name.StartsWith("Draw")&&!Equals(i.operand,indexed)&&!Equals(i.operand,stereo)))
                throw new InvalidOperationException("Installed billboard batch/draw coverage changed.");
        }
        internal bool TryInstall()
        {
            if(disposed)return false;if(installed)return true;
            if(active!=null&&!ReferenceEquals(active,this)){reason="Another projective billboard owner is installed.";return false;}
            try
            {
                indexed=DrawThunk(drawIndexed);stereo=DrawThunk(drawStereo);indexedPointer=drawIndexed.MethodHandle.GetFunctionPointer();stereoPointer=drawStereo.MethodHandle.GetFunctionPointer();
                harmony=new Harmony(PatchId);active=this;
                harmony.Patch(drawIndexed,transpiler:new HarmonyMethod(typeof(PortalBillboardProjectiveNative).GetMethod("AuditTerminal",All)){priority=Priority.Last});patched.Add(drawIndexed);
                harmony.Patch(render,transpiler:new HarmonyMethod(typeof(PortalBillboardProjectiveNative).GetMethod("Redirect",All)){priority=Priority.Last});patched.Add(render);
                // Re-JIT the managed ancestry as in the capture adapter; callers
                // prewarmed before installation must not retain old Render IL.
                var callers=PortalCaptureCoverage.TransitiveRenderCallers(assembly,new[]{render});
                foreach(var caller in callers){harmony.Patch(caller,transpiler:new HarmonyMethod(typeof(PortalCaptureCoverage).GetMethod("RecompileCaller",All)));patched.Add(caller);}
                installed=patched.All(m=>Harmony.GetPatchInfo(m).Owners.Contains(PatchId));if(!installed)throw new InvalidOperationException("Projective billboard hook was not retained.");return true;
            }
            catch(Exception ex){reason=ex.GetBaseException().Message;Unpatch();return false;}
        }
        static IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> instructions)
        {
            var owner=active;if(owner==null)throw new InvalidOperationException("Missing projective billboard adapter owner.");int count=0;
            foreach(var i in instructions)
            {
                var m=i.operand as MethodInfo;bool ordinary=m!=null&&(Equals(m,owner.drawIndexed)||m.DeclaringType==typeof(PortalCaptureDrawBridges)&&m.Name=="DrawIndexed");
                bool stereoCall=m!=null&&Equals(m,owner.drawStereo);
                if((i.opcode==OpCodes.Call||i.opcode==OpCodes.Callvirt)&&(ordinary||stereoCall))
                {
                    var texture=new CodeInstruction(OpCodes.Ldloc_3);texture.labels.AddRange(i.labels);texture.blocks.AddRange(i.blocks);yield return texture;
                    yield return new CodeInstruction(OpCodes.Call,typeof(PortalBillboardProjectiveNative).GetMethod(stereoCall?"StereoDraw":"IndexedDraw",All));count++;
                }
                else yield return i;
            }
            if(count!=2)throw new InvalidOperationException("Projective billboard draw seam lost exact two-branch coverage.");
        }
        static IEnumerable<CodeInstruction> AuditTerminal(IEnumerable<CodeInstruction> instructions,ILGenerator generator)
        {
            int found=0;
            foreach(var instruction in instructions)
            {
                var method=instruction.operand as MethodInfo;
                if(method==null||method.DeclaringType.FullName!="SharpDX.Direct3D11.DeviceContext"||method.Name!="DrawIndexed"||(instruction.opcode!=OpCodes.Call&&instruction.opcode!=OpCodes.Callvirt)){yield return instruction;continue;}
                found++;if(instruction.blocks.Count!=0||method.IsStatic||method.GetParameters().Length!=3)throw new InvalidOperationException("Projective billboard terminal draw ABI changed.");
                var args=method.GetParameters().Select(p=>generator.DeclareLocal(p.ParameterType)).ToArray();var context=generator.DeclareLocal(method.DeclaringType);var skip=generator.DefineLabel();
                var first=new CodeInstruction(OpCodes.Stloc,args[2]);first.labels.AddRange(instruction.labels);yield return first;yield return new CodeInstruction(OpCodes.Stloc,args[1]);yield return new CodeInstruction(OpCodes.Stloc,args[0]);yield return new CodeInstruction(OpCodes.Stloc,context);
                yield return new CodeInstruction(OpCodes.Ldarg_0);yield return new CodeInstruction(OpCodes.Call,typeof(PortalBillboardProjectiveNative).GetMethod("BeforeTerminal",All));yield return new CodeInstruction(OpCodes.Brfalse,skip);
                yield return new CodeInstruction(OpCodes.Ldloc,context);foreach(var argument in args)yield return new CodeInstruction(OpCodes.Ldloc,argument);yield return new CodeInstruction(instruction.opcode,method);
                var end=new CodeInstruction(OpCodes.Nop);end.labels.Add(skip);yield return end;
            }
            if(found!=1)throw new InvalidOperationException("Projective billboard needs exactly one audited terminal callsite.");
        }
        static bool BeforeTerminal(object context)
        {
            var proof=drawing;if(proof==null||DirectCameraCapture.IsCapturing)return true;var owner=proof.Owner;
            var srvs=owner.srvCache(proof.Stage) as Array;var cbs=owner.constantCache(proof.Stage) as Array;
            if(proof.Calls!=0||!ReferenceEquals(context,proof.Context)||!owner.MatchesPrimary(proof.Primary)||!ReferenceEquals(owner.shader(proof.Stage),proof.Shader)||srvs==null||srvs.Length!=32||cbs==null||cbs.Length<8||!ReferenceEquals(srvs.GetValue(0),proof.Srv0)||!ReferenceEquals(srvs.GetValue(30),proof.Srv30)||!ReferenceEquals(cbs.GetValue(7),proof.Buffer))
            {owner.reason="Owned portal billboard omitted at terminal draw: late hook changed the native shader/resource proof.";return false;}
            proof.Calls++;return true;
        }
        // One engine preprocessing job and at most two CPU workers per owner
        // update. GPU shader attachment remains on this serialized boundary.
        internal bool PrepareRender(){return PrepareRender(resourceEpoch==long.MinValue?0:resourceEpoch);}
        internal bool PrepareRender(long ownerEpoch)
        {
            if(disposed||Volatile.Read(ref primary)!=null||!TryInstall())return false;
            var raw=snapshot().ToArray();if(raw.Length>8)throw new InvalidOperationException("Projective billboard target quota exceeded.");
            Volatile.Write(ref ownedRaw,new HashSet<object>(raw.Where(i=>i.Key!=null).Select(i=>i.Key),Identity.Instance));
            retiredJobs.RemoveAll(j=>{if(!j.IsCompleted)return false;var ignored=j.Exception;return true;});
            if(resourceEpoch!=long.MinValue&&resourceEpoch!=ownerEpoch){ReleaseGpu();DropVariants();}resourceEpoch=ownerEpoch;
            if(!assetsVerified){PortalCaptureCoverage.ValidateAssets(compiler.InstalledShaderRoot);assetsVerified=true;}
            var live=compiler.ReadRegistry().Where(item=>item.File=="Transparent/Billboards.hlsl").ToArray();var present=new HashSet<object>(live.Select(v=>v.Original),Identity.Instance);
            foreach(var stale in variants.Keys.Where(k=>!present.Contains(k)).ToArray()){Release(variants[stale].Shader);RetireJob(variants[stale]);variants.Remove(stale);}
            Volatile.Write(ref attachedCount,variants.Values.Count(v=>v.Shader!=null));
            object d=device.GetValue(null);if(d==null){Volatile.Write(ref attachedCount,0);reason="Projective billboard render device unavailable.";return false;}
            if(currentDevice!=null&&!ReferenceEquals(currentDevice,d)){ReleaseGpu();DropVariants();}currentDevice=d;
            foreach(var descriptor in live)if(!variants.ContainsKey(descriptor.Original)&&variants.Count<128)variants.Add(descriptor.Original,new Variant{Descriptor=descriptor,Demanded=DefaultVariant(descriptor)});
            foreach(var v in variants.Values.Where(v=>v.Job!=null&&v.Job.IsCompleted&&v.Shader==null&&v.Failure==null).Take(2))
            {
                if(v.Job.IsFaulted||v.Job.IsCanceled){v.Failure=v.Job.Exception==null?"Compilation cancelled.":v.Job.Exception.GetBaseException().Message;continue;}
                v.Shader=createShader.Invoke(new[]{d,(object)v.Job.Result,null});
            }
            if(retiredJobs.Count+variants.Values.Count(v=>v.Job!=null&&!v.Job.IsCompleted)<2)
            {
                var next=variants.Values.FirstOrDefault(v=>v.Demanded&&v.Job==null&&v.Failure==null);
                if(next!=null)try{var prepared=compiler.Prepare(next.Descriptor);next.Job=Task.Run(()=>PortalBillboardProjectiveSource.CompileAndValidate(compiler,prepared,nativeBinding));}catch(Exception ex){next.Failure=ex.GetBaseException().Message;}
            }
            int attached=variants.Values.Count(v=>v.Shader!=null);Volatile.Write(ref attachedCount,attached);reason=attached+"/"+variants.Count+" private projective billboard variants attached.";return Available;
        }
        static bool DefaultVariant(PortalClipDescriptor descriptor)
        {
            if(descriptor.Macros==null)return true;
            foreach(object macro in descriptor.Macros)
            {
                string name=(string)macro.GetType().GetField("Name",All).GetValue(macro);
                if(name=="LIT_PARTICLE"||name=="ALPHA_CUTOUT"||name=="SINGLE_CHANNEL"||name=="DEBUG_UNIFORM_ACCUM")return false;
            }
            return true;
        }
        internal bool BeginPrimary(PortalPrimaryView view,object ownerContext)
        {
            if(disposed||!installed)return false;
            var items=snapshot().ToArray();if(items.Length>8)throw new InvalidOperationException("Projective billboard target quota exceeded.");
            var retained=new HashSet<object>(items.Where(i=>i.Key!=null).Select(i=>i.Key),Identity.Instance);Volatile.Write(ref ownedRaw,retained);
            if(view==null||!view.Valid||ownerContext==null||DirectCameraCapture.IsCapturing||currentDevice==null||!ReferenceEquals(currentDevice,device.GetValue(null)))return false;
            if(Volatile.Read(ref primary)!=null)throw new InvalidOperationException("Projective primary must join before its successor.");
            if(resourceEpoch!=long.MinValue&&resourceEpoch!=view.Epoch){ReleaseGpu();DropVariants();resourceEpoch=view.Epoch;return false;}resourceEpoch=view.Epoch;
            foreach(var stale in buffers.Keys.Where(k=>!retained.Contains(k)).ToArray())
            {var a=Array.CreateInstance(bufferType,1);a.SetValue(buffers[stale],0);disposeBuffers.Invoke(managers.GetValue(null),new object[]{a});buffers.Remove(stale);}
            var frozen=new Dictionary<object,Binding>(Identity.Instance);int count=0;
            foreach(var item in items)
            {
                if(++count>8)throw new InvalidOperationException("Projective billboard target quota exceeded.");
                if(item.Key==null||item.Value==null||frozen.ContainsKey(item.Key))continue;
                PortalProjectiveSampling sampling;if(!item.Value.TryRebase(view,out sampling))continue;
                object srv=targetSrv(item.Key);if(srv==null)continue;object buffer;
                if(!buffers.TryGetValue(item.Key,out buffer))
                {var p=createBuffer.GetParameters();buffer=createBuffer.Invoke(managers.GetValue(null),new object[]{"HDR.Portal.PrimaryBillboard",80,null,Enum.ToObject(p[3].ParameterType,2),false});buffers.Add(item.Key,buffer);}
                object mapped=map.Invoke(null,new[]{ownerContext,buffer});float[] data=sampling.CopyConstants();
                try{write.Invoke(mapped,new object[]{data,20,0});}finally{unmap.Invoke(mapped,null);}
                frozen.Add(item.Key,new Binding{Srv=srv,Buffer=buffer,Sampling=sampling});
            }
            Volatile.Write(ref ownedRaw,retained);Volatile.Write(ref primary,new Primary(frozen,retained,view,currentDevice,mainGBuffer.GetValue(null)));return true;
        }
        internal bool BeginPrimary(PortalPrimaryView view){return BeginPrimary(view,immediate.GetValue(null));}
        internal void EndPrimary()
        {
            var current=Volatile.Read(ref primary);if(current==null)return;Interlocked.Exchange(ref current.Closing,1);
            if(Volatile.Read(ref current.Draws)!=0)throw new InvalidOperationException("Projective billboard workers must join before primary cleanup.");
            Volatile.Write(ref primary,null);
        }
        [MethodImpl(MethodImplOptions.NoInlining)] static void IndexedDraw(object context,int count,int start,int vertex,object target){Draw(context,count,start,vertex,target,false);}
        [MethodImpl(MethodImplOptions.NoInlining)] static void StereoDraw(object context,int count,int start,int vertex,object target){Draw(context,count,start,vertex,target,true);}
        static void Draw(object context,int count,int start,int vertex,object target,bool stereoCall)
        {
            var owner=active;var current=owner==null?null:Volatile.Read(ref owner.primary);Binding binding;
            if(owner==null||target==null){Terminal(context,count,start,vertex,stereoCall);return;}
            if(DirectCameraCapture.IsCapturing){if(!Volatile.Read(ref owner.ownedRaw).Contains(target))Terminal(context,count,start,vertex,stereoCall);return;}
            if(current==null){if(!Volatile.Read(ref owner.ownedRaw).Contains(target))Terminal(context,count,start,vertex,stereoCall);return;}
            if(!current.Owned.Contains(target)){Terminal(context,count,start,vertex,stereoCall);return;}
            if(!current.Targets.TryGetValue(target,out binding)){owner.reason="Owned raw portal billboard omitted: matching published sampling proof unavailable.";return;}
            Interlocked.Increment(ref current.Draws);
            try
            {
                if(Volatile.Read(ref current.Closing)!=0||!owner.MatchesPrimary(current)){owner.reason="Owned portal billboard omitted: native primary view/device/GBuffer changed.";return;}
                if(stereoCall||!owner.TrustedVertices(binding.Sampling,current.View,count,start,vertex)){owner.reason="Owned portal billboard omitted: entry-plane, gathered-vertex, or mono-primary proof failed.";return;}
                object stage=owner.pixel(context),original=owner.shader(stage);var srvs=owner.srvCache(stage) as Array;var cbs=owner.constantCache(stage) as Array;Variant variant;
                object sb=owner.billboardBuffer.GetValue(null);
                if(srvs==null||srvs.Length!=32||!ReferenceEquals(srvs.GetValue(0),binding.Srv)||sb==null||!ReferenceEquals(srvs.GetValue(30),owner.targetSrv(sb))||cbs==null||cbs.Length<8||original==null||!owner.variants.TryGetValue(original,out variant))
                {owner.reason="Owned portal billboard omitted: current texture, shader, or private b7 proof unavailable.";return;}
                if(variant.Shader==null){variant.Demanded=true;owner.reason=variant.Failure==null?"Owned portal billboard omitted: demanded private shader is warming.":"Owned portal billboard omitted: "+variant.Failure;return;}
                object previous=cbs.GetValue(7);
                // Restore through native setters, keeping both engine caches
                // and the recorded deferred command stream synchronized.
                var previousProof=drawing;var proof=new DrawProof{Owner=owner,Primary=current,Context=context,Stage=stage,Shader=variant.Shader,Buffer=binding.Buffer,Srv0=binding.Srv,Srv30=srvs.GetValue(30)};
                try{owner.setBuffer(stage,7,binding.Buffer);owner.setShader(stage,variant.Shader);drawing=proof;Terminal(context,count,start,vertex,stereoCall);}
                finally
                {
                    drawing=previousProof;
                    try{owner.setShader(stage,original);}
                    finally
                    {
                        try{owner.setBuffer(stage,7,previous);}
                        finally
                        {
                            var after=owner.srvCache(stage) as Array;
                            if(after!=null&&after.Length==32)
                            {if(!ReferenceEquals(after.GetValue(0),binding.Srv))owner.setSrv(stage,0,target);if(!ReferenceEquals(after.GetValue(30),proof.Srv30))owner.setSrv(stage,30,sb);}
                        }
                    }
                }
            }
            finally{Interlocked.Decrement(ref current.Draws);}
        }
        [MethodImpl(MethodImplOptions.NoInlining)] static void Terminal(object context,int count,int start,int vertex,bool stereoCall)
        {if(stereoCall)stereo(context,count,start,vertex,stereoPointer);else indexed(context,count,start,vertex,indexedPointer);}
        bool MatchesPrimary(Primary current)
        {
            if(!ReferenceEquals(current.Device,device.GetValue(null))||!ReferenceEquals(current.GBuffer,mainGBuffer.GetValue(null))||current.GBuffer==null||(long)frameCounter.GetValue(null)!=current.Frame)return false;
            var now=readView(current.Epoch);var was=current.View;
            return now!=null&&now.Valid&&now.Frame==was.Frame&&now.Epoch==was.Epoch&&now.Viewer==was.Viewer&&now.Projection==was.Projection&&now.Width==was.Width&&now.Height==was.Height;
        }
        bool TrustedVertices(PortalProjectiveSampling sampling,PortalPrimaryView view,int count,int start,int baseVertex)
        {
            if(sampling==null||view==null||count<=0||count%6!=0||start<0||start%6!=0||baseVertex!=0)return false;
            int first=start/6,number=count/6,safe=(int)billboardCount.GetValue(null);if(first>safe-number)return false;
            object arrays=arrayData.GetValue(null);var vertices=vertexArray.GetValue(arrays) as Array;var data=dataArray.GetValue(arrays) as Array;
            if(vertices==null||data==null||first>vertices.Length-number||first>data.Length-number)return false;
            for(int i=first;i<first+number;i++)
            {
                if((int)projectionId.GetValue(data.GetValue(i))>=0)return false;object quad=vertices.GetValue(i);
                foreach(var point in points)if(!sampling.ContainsEntryPoint((Vector3D)(Vector3)position.GetValue(point.GetValue(quad)),view))return false;
            }
            return true;
        }
        static NativeDraw DrawThunk(MethodInfo method)
        {
            var d=new DynamicMethod("HDR.Projective.NativeDraw",typeof(void),new[]{typeof(object),typeof(int),typeof(int),typeof(int),typeof(IntPtr)},typeof(PortalBillboardProjectiveNative),true);var il=d.GetILGenerator();
            Type context=method.IsStatic?method.GetParameters()[0].ParameterType:method.DeclaringType;
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,context);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldarg_2);il.Emit(OpCodes.Ldarg_3);il.Emit(OpCodes.Ldarg_S,4);
            il.EmitCalli(OpCodes.Calli,method.IsStatic?CallingConventions.Standard:CallingConventions.Standard|CallingConventions.HasThis,typeof(void),method.GetParameters().Select(p=>p.ParameterType).ToArray(),null);il.Emit(OpCodes.Ret);return (NativeDraw)d.CreateDelegate(typeof(NativeDraw));
        }
        static Read Getter(MemberInfo member)
        {
            if(member==null)throw new InvalidOperationException("Projective billboard native cache ABI unavailable.");var d=new DynamicMethod("HDR.Projective.Read",typeof(object),new[]{typeof(object)},typeof(PortalBillboardProjectiveNative),true);var il=d.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,member.DeclaringType);
            if(member is FieldInfo)il.Emit(OpCodes.Ldfld,(FieldInfo)member);else il.Emit(OpCodes.Callvirt,((PropertyInfo)member).GetGetMethod(true));il.Emit(OpCodes.Ret);return (Read)d.CreateDelegate(typeof(Read));
        }
        static Set Setter(MethodInfo method)
        {var d=new DynamicMethod("HDR.Projective.Set",typeof(void),new[]{typeof(object),typeof(object)},typeof(PortalBillboardProjectiveNative),true);var il=d.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,method.DeclaringType);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Castclass,method.GetParameters()[0].ParameterType);il.Emit(OpCodes.Callvirt,method);il.Emit(OpCodes.Ret);return (Set)d.CreateDelegate(typeof(Set));}
        static SetBuffer BufferSetter(MethodInfo method)
        {var d=new DynamicMethod("HDR.Projective.SetBuffer",typeof(void),new[]{typeof(object),typeof(int),typeof(object)},typeof(PortalBillboardProjectiveNative),true);var il=d.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,method.DeclaringType);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldarg_2);il.Emit(OpCodes.Castclass,method.GetParameters()[1].ParameterType);il.Emit(OpCodes.Callvirt,method);il.Emit(OpCodes.Ret);return (SetBuffer)d.CreateDelegate(typeof(SetBuffer));}
        void ReleaseGpu()
        {
            if(Volatile.Read(ref primary)!=null)throw new InvalidOperationException("Projective primary must join before device retirement.");
            Volatile.Write(ref attachedCount,0);
            foreach(var v in variants.Values){if(v.Shader is IDisposable)((IDisposable)v.Shader).Dispose();v.Shader=null;}
            if(buffers.Count!=0){var a=Array.CreateInstance(bufferType,buffers.Count);int i=0;foreach(var b in buffers.Values)a.SetValue(b,i++);disposeBuffers.Invoke(managers.GetValue(null),new object[]{a});buffers.Clear();}currentDevice=null;
        }
        void Unpatch(){if(harmony!=null)foreach(var method in patched.AsEnumerable().Reverse())harmony.Unpatch(method,HarmonyPatchType.All,PatchId);patched.Clear();installed=false;if(ReferenceEquals(active,this))active=null;}
        void RetireJob(Variant variant){if(variant.Job!=null&&!variant.Job.IsCompleted)retiredJobs.Add(variant.Job);else if(variant.Job!=null){var ignored=variant.Job.Exception;}}
        void DropVariants(){foreach(var variant in variants.Values)RetireJob(variant);variants.Clear();}
        static void Release(object resource){if(resource is IDisposable)((IDisposable)resource).Dispose();}
        public void Dispose(){if(disposed)return;EndPrimary();ReleaseGpu();DropVariants();Unpatch();disposed=true;}
    }
}
