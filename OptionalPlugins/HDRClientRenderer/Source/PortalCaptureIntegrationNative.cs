using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
namespace HDRClientRenderer
{
    // Installed-renderer private shell capture. Every native draw is
    // guarded, including deferred contexts and foreign calls through SharpDX.
    // It is inert outside an explicitly begun private native capture lifetime.
    internal sealed class PortalCaptureIntegrationNative : IDisposable
    {
        sealed class ShaderIdentityComparer:IEqualityComparer<object>
        {internal static readonly ShaderIdentityComparer Instance=new ShaderIdentityComparer();public new bool Equals(object a,object b){return ReferenceEquals(a,b);}public int GetHashCode(object value){return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);}}
        const BindingFlags Static=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        const BindingFlags Instance=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        const string PatchId="HDRClientRenderer.PortalCaptureCoverage";
        static PortalCaptureIntegrationNative active;
        [ThreadStatic] static DrawState permitted;
        internal sealed class Session
        {
            internal readonly PortalCaptureGate Gate;internal readonly int Width,Height;
            internal readonly Dictionary<object,PortalDrawRole> Roles;internal readonly object MainTargetView;internal readonly Dictionary<object,PortalClipDescriptor> Descriptors;
            internal int ActiveDraws,Closing,GeometryDraws,AuxiliaryDraws,SuppressedDraws;
            internal Session(PortalCaptureGate gate,int width,int height,Dictionary<object,PortalDrawRole> roles,object mainTargetView=null,Dictionary<object,PortalClipDescriptor> descriptors=null){Gate=gate;Width=width;Height=height;Roles=roles;MainTargetView=mainTargetView;Descriptors=descriptors;}
        }
        sealed class DrawState
        {
            internal PortalCaptureIntegrationNative Owner;internal Session Session;
            internal object Context,Stage,Original,Replacement,NativeContext;internal bool Changed,Execute;
            internal int NativeCalls,AuditedCalls;internal bool InAuditedCall;internal DrawState Previous;
        }
        readonly PortalClipShaders compiler;readonly PortalCaptureResources resources;
        readonly PortalCaptureShaderNative shaders;readonly PortalCaptureThresholdNative threshold;
        readonly FieldInfo contextPixel,contextNative,contextState,currentShader,viewport,targetViews,targetCount;
        readonly PropertyInfo backbuffer,nativeRtv;
        readonly FieldInfo[] viewportFields;
        readonly MethodInfo[] renderDraws,nativeDraws;readonly MethodBase[] renderCallers,outerCallers;
        readonly List<MethodBase> patched=new List<MethodBase>();
        readonly string shaderRoot;
        Dictionary<object,PortalDrawRole> preparedRoles;
        Dictionary<object,PortalClipDescriptor> preparedDescriptors;
        Session session;Harmony harmony;bool disposed;
        internal bool Installed{get;private set;}
        internal bool HasActiveSession{get{return Volatile.Read(ref session)!=null;}}
        internal bool Ready{get{return Installed&&!disposed&&(Volatile.Read(ref session)==null||DirectCameraCapture.IsCapturing);}}
        internal string Reason{get{return Failure??"Native shell capture with clipped geometry/clouds and shell-bounded atmosphere; environment-probe atmosphere and debug primitives omitted; "+shaders.Status+".";}}
        internal int Epoch{get{return shaders.Epoch;}}
        internal string Failure{get;private set;}
        internal static bool Available{get{return active!=null&&active.Ready;}}
        internal PortalCaptureIntegrationNative(Assembly assembly,int initialEpoch=0)
        {
            PortalCaptureCoverage.ValidateRuntime();
            renderDraws=PortalCaptureCoverage.ValidateDrawBoundary(assembly);
            renderCallers=PortalCaptureCoverage.RenderDrawCallers(assembly,renderDraws);
            outerCallers=PortalCaptureCoverage.TransitiveRenderCallers(assembly,renderCallers);
            compiler=new PortalClipShaders(assembly);shaderRoot=ResolveInstalledShaderRoot(assembly);
            PortalCaptureCoverage.ValidateAssets(shaderRoot);resources=new PortalCaptureResources(assembly);
            shaders=new PortalCaptureShaderNative(assembly,resources,compiler,initialEpoch);threshold=new PortalCaptureThresholdNative(assembly,resources,compiler);
            Func<string,Type> type=name=>DirectCameraCaptureNative.FindType(assembly,name);
            var context=type("VRage.Render11.RenderContext.MyRenderContext");var pixel=type("VRage.Render11.RenderContext.MyPixelStage");
            contextPixel=F(context,"m_pixelShaderStage");contextNative=F(context,"m_deviceContext");contextState=F(context,"m_state");
            currentShader=F(pixel,"m_pixelShader");viewport=F(contextState.FieldType,"m_viewport");
            targetViews=F(contextState.FieldType,"m_rtvs");targetCount=F(contextState.FieldType,"m_rtvsCount");
            backbuffer=type("VRageRender.MyRender11").GetProperty("Backbuffer",Static);nativeRtv=type("VRage.Render11.Resources.IRtvBindable").GetProperty("Rtv",Instance);
            if(backbuffer==null||nativeRtv==null)throw new InvalidOperationException("Portal protected main target ABI unavailable.");
            viewportFields=new[]{"X","Y","Width","Height","MinDepth","MaxDepth"}.Select(n=>F(viewport.FieldType,n)).ToArray();
            nativeDraws=type("SharpDX.Direct3D11.DeviceContext").GetMethods(Instance|BindingFlags.DeclaredOnly).Where(m=>m.Name.StartsWith("Draw",StringComparison.Ordinal)&&!m.IsGenericMethod&&m.GetMethodBody()!=null).ToArray();
            if(nativeDraws.Length<6||renderDraws.SelectMany(PortalCaptureCoverage.Calls).Where(m=>m.DeclaringType.FullName=="SharpDX.Direct3D11.DeviceContext"&&m.Name.StartsWith("Draw",StringComparison.Ordinal)).Any(m=>!nativeDraws.Contains(m)))
                throw new InvalidOperationException("Portal native draw guard coverage is incomplete.");
        }
        // Hook installation is reversible and does not allocate a GPU resource.
        internal bool TryInstall()
        {
            if(disposed)return false;if(Installed)return true;if(active!=null&&!ReferenceEquals(active,this)){Failure="Another portal capture owner is still installed.";return false;}
            try
            {
                harmony=new Harmony(PatchId);
                var before=typeof(PortalCaptureIntegrationNative).GetMethod("BeforeRenderDraw",Static);var after=typeof(PortalCaptureIntegrationNative).GetMethod("AfterRenderDraw",Static);
                var guard=typeof(PortalCaptureIntegrationNative).GetMethod("BeforeNativeDraw",Static);
                var audit=typeof(PortalCaptureIntegrationNative).GetMethod("AuditNativeCallSite",Static);
                foreach(var method in nativeDraws){harmony.Patch(method,prefix:new HarmonyMethod(guard){priority=Priority.Last});patched.Add(method);}
                foreach(var method in renderDraws){harmony.Patch(method,prefix:new HarmonyMethod(before){priority=Priority.Last},transpiler:new HarmonyMethod(audit){priority=Priority.First},finalizer:new HarmonyMethod(after));patched.Add(method);}
                PortalCaptureDrawBridges.Initialize(renderDraws);
                var callers=typeof(PortalCaptureDrawBridges).GetMethod("RedirectCallers",Static);
                foreach(var method in renderCallers){harmony.Patch(method,transpiler:new HarmonyMethod(callers){priority=Priority.First});patched.Add(method);}
                // Existing JIT/R2R outer bodies may already contain inlined
                // direct helpers. Regenerate their complete managed ancestry
                // after the direct call sites have acquired NoInlining bridges.
                var recompile=typeof(PortalCaptureCoverage).GetMethod("RecompileCaller",Static);
                foreach(var method in outerCallers){harmony.Patch(method,transpiler:new HarmonyMethod(recompile));patched.Add(method);}
                if(patched.Any(m=>{var p=Harmony.GetPatchInfo(m);return p==null||!p.Owners.Contains(PatchId);}))throw new InvalidOperationException("Portal draw guard installation was not retained.");
                active=this;Installed=true;Failure=null;return true;
            }
            catch(Exception ex){Failure=ex.GetBaseException().Message;Unpatch();return false;}
        }
        // Call ONLY on the serialized render boundary before entering isolation.
        // Registry metadata and compilation inputs are captured together here.
        internal bool PrepareRenderResources()
        {
            if(!Installed||disposed||Volatile.Read(ref session)!=null)return false;
            try
            {
                if(!string.Equals(compiler.InstalledShaderRoot.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),shaderRoot.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Portal live compiler shader directory differs from the certified installed assets.");
                var snapshot=compiler.ReadRegistry(true);var roles=new Dictionary<object,PortalDrawRole>(ShaderIdentityComparer.Instance);var descriptors=new Dictionary<object,PortalClipDescriptor>(ShaderIdentityComparer.Instance);
                foreach(var descriptor in snapshot)
                {
                    PortalDrawRole role=PortalCaptureDrawPolicy.Role(descriptor.File),previous;
                    if(roles.TryGetValue(descriptor.Original,out previous)&&previous!=role)throw new InvalidOperationException("Portal shader identity aliases incompatible draw roles.");
                    roles[descriptor.Original]=role;
                    if(role==PortalDrawRole.Geometry)descriptors[descriptor.Original]=descriptor;
                }
                // Only identities requested by a previous private draw consume
                // the bounded registry. Resident auxiliary/unused material
                // variants never occupy its slots. Source freezing stays on the
                // renderer; sealed-source compilation runs on bounded workers.
                shaders.ScheduleCpu();shaders.PrepareGpu();preparedRoles=roles;preparedDescriptors=descriptors;Failure=null;return true;
            }
            catch(Exception ex){Failure=ex.GetBaseException().Message;preparedRoles=null;preparedDescriptors=null;return false;}
        }
        internal bool PrepareRender(){return PrepareRenderResources();}
        internal Session Begin(object isolatedContext,PortalRayMapSpec spec,int samples)
        {
            if(isolatedContext==null)return null;object value=viewport.GetValue(contextState.GetValue(isolatedContext));
            float width=Convert.ToSingle(viewportFields[2].GetValue(value)),height=Convert.ToSingle(viewportFields[3].GetValue(value));
            if(width<64||height<64||width>2048||height>2048||width!=(int)width||height!=(int)height||(long)width*height>4194304){Failure="Portal isolated viewport is outside its bounded capture size.";return null;}
            return Begin(isolatedContext,spec,(int)width,(int)height,samples);
        }
        internal Session Begin(object isolatedContext,PortalRayMapSpec spec,int width,int height,int samples)
        {
            if(!Installed||disposed||preparedRoles==null||Volatile.Read(ref session)!=null||spec==null)return null;
            var capture=new PortalCaptureGate(spec.Epoch,spec.Generation,samples,Installed,Installed);
            if(spec.Epoch!=Epoch){capture.Reject("Portal prepared device epoch does not match its spec.");Failure=capture.Failure;return null;}
            if(capture.Failure!=null){Failure=capture.Failure;return null;}
            // No session is visible during this trusted auxiliary threshold pass.
            if(!threshold.Generate(isolatedContext,spec,width,height,capture)){Failure=capture.Failure;return null;}
            object mainView=MainView(capture);if(mainView==null){Failure=capture.Failure;resources.Invalidate();return null;}
            var value=new Session(capture,width,height,preparedRoles,mainView,preparedDescriptors);Volatile.Write(ref session,value);return value;
        }
        internal Session Begin(object isolatedContext,PortalCaptureSpec spec,int samples)
        {
            if(!Installed||disposed||preparedRoles==null||Volatile.Read(ref session)!=null||spec==null)return null;
            var capture=new PortalCaptureGate(spec.Epoch,spec.Generation,samples,Installed,Installed);
            if(spec.Epoch!=Epoch){capture.Reject("Portal prepared device epoch does not match its spec.");Failure=capture.Failure;return null;}
            if(capture.Failure!=null){Failure=capture.Failure;return null;}
            if(!threshold.Generate(isolatedContext,spec,capture)){Failure=capture.Failure;return null;}
            object mainView=MainView(capture);if(mainView==null){Failure=capture.Failure;resources.Invalidate();return null;}
            var value=new Session(capture,spec.Width,spec.Height,preparedRoles,mainView,preparedDescriptors);Volatile.Write(ref session,value);return value;
        }
        // Before native camera/pipeline restoration, after Scheduler.Done consumed
        // joined command lists. Each worker restores its bindings before returning.
        internal bool EndAfterJoin(Session value,bool workersJoined)
        {
            if(value==null)return false;
            if(!ReferenceEquals(Volatile.Read(ref session),value)){value.Gate.Reject("Portal capture lifetime changed before completion.");return false;}
            Interlocked.Exchange(ref value.Closing,1);
            bool joined=workersJoined&&Volatile.Read(ref value.ActiveDraws)==0;
            if(!joined){value.Gate.Reject("Portal draw contexts have not joined; isolation must remain active.");Failure=value.Gate.Failure;return false;}
            shaders.UnbindAfterJoin(value.Gate);bool complete=value.Gate.Finish(Epoch,value.Gate.Generation,true);
            resources.Invalidate();Volatile.Write(ref session,null);Failure=value.Gate.Failure;return complete;
        }
        static bool BeforeRenderDraw(object __instance,out DrawState __state)
        {
            __state=null;var owner=active;var capture=owner==null?null:Volatile.Read(ref owner.session);if(capture==null)return true;
            if(!DirectCameraCapture.IsCapturing){capture.Gate.Reject("Portal native capture lifetime ended before joining its scope.");return true;}
            var state=new DrawState{Owner=owner,Session=capture,Previous=permitted,Context=__instance};__state=state;Interlocked.Increment(ref capture.ActiveDraws);
            if(Volatile.Read(ref capture.Closing)!=0){capture.Gate.Reject("Portal draw callback outlived its capture scope.");return false;}
            try
            {
                state.Stage=owner.contextPixel.GetValue(__instance);state.Original=owner.currentShader.GetValue(state.Stage);state.NativeContext=owner.contextNative.GetValue(__instance);
                PortalDrawRole role;if(state.Original==null)role=PortalDrawRole.Geometry;else if(!capture.Roles.TryGetValue(state.Original,out role))role=PortalDrawRole.Unknown;
                if(role==PortalDrawRole.Suppressed){Interlocked.Increment(ref capture.SuppressedDraws);return false;}
                if(role==PortalDrawRole.Unknown){capture.Gate.Reject("Portal draw shader is absent from the frozen installed registry.");return false;}
                if(role==PortalDrawRole.Geometry)
                {
                    if(!owner.MatchingViewport(__instance,capture)){capture.Gate.Reject("Portal world draw changed capture viewport/projection domain.");return false;}
                    PortalClipDescriptor demanded=null;if(state.Original!=null&&capture.Descriptors!=null)capture.Descriptors.TryGetValue(state.Original,out demanded);
                    object replacement;if(!owner.shaders.Select(state.Stage,state.Original,capture.Gate,out replacement,demanded))return false;
                    state.Changed=true;state.Replacement=replacement;owner.shaders.PixelShaderSet.Invoke(state.Stage,new[]{replacement});Interlocked.Increment(ref capture.GeometryDraws);
                }
                else Interlocked.Increment(ref capture.AuxiliaryDraws);
                state.Execute=true;permitted=state;return true;
            }
            catch(Exception ex){capture.Gate.Reject("Portal draw preparation failed: "+ex.GetBaseException().Message);return false;}
        }
        static Exception AfterRenderDraw(Exception __exception,DrawState __state)
        {
            if(__state==null)return __exception;var state=__state;var gate=state.Session.Gate;
            try
            {
                if(state.Execute&&state.AuditedCalls!=1)gate.Reject("Portal draw did not traverse exactly one audited native call site (audited="+state.AuditedCalls+", wrapper="+state.NativeCalls+").");
                if(__exception!=null)gate.Reject("Portal world draw failed: "+__exception.GetBaseException().Message);
                if(state.Changed)state.Owner.shaders.PixelShaderSet.Invoke(state.Stage,new[]{state.Original});
            }
            catch(Exception ex){gate.Reject("Portal shader restoration failed: "+ex.GetBaseException().Message);}
            finally
            {
                if(state.Stage!=null)state.Owner.shaders.UnbindAfterDraw(state.Stage,gate);
                permitted=state.Previous;Interlocked.Decrement(ref state.Session.ActiveDraws);
            }
            return __exception;
        }
        static bool BeforeNativeDraw(object __instance)
        {
            var owner=active;var capture=owner==null?null:Volatile.Read(ref owner.session);if(capture==null)return true;
            if(!DirectCameraCapture.IsCapturing){capture.Gate.Reject("Portal native capture lifetime ended before joining its scope.");return true;}
            var state=permitted;
            if(state==null||!state.InAuditedCall||!ReferenceEquals(state.Session,capture)||!ReferenceEquals(state.NativeContext,__instance)||Volatile.Read(ref capture.Closing)!=0||++state.NativeCalls!=1)
            {capture.Gate.Reject("Portal native draw bypassed the audited render-context boundary.");return false;}
            return ValidateNativeBindings(owner,capture,state);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool BeforeAuditedNativeDraw(object nativeContext)
        {
            var owner=active;var capture=owner==null?null:Volatile.Read(ref owner.session);if(capture==null||!DirectCameraCapture.IsCapturing)return true;
            var state=permitted;
            if(state==null||!state.Execute||!ReferenceEquals(state.Session,capture)||!ReferenceEquals(state.NativeContext,nativeContext)||Volatile.Read(ref capture.Closing)!=0||++state.AuditedCalls!=1)
            {capture.Gate.Reject("Portal audited native call site bypassed its private draw scope.");return false;}
            if(!ValidateNativeBindings(owner,capture,state))return false;state.InAuditedCall=true;return true;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void AfterAuditedNativeDraw(){var state=permitted;if(state!=null)state.InAuditedCall=false;}
        static bool ValidateNativeBindings(PortalCaptureIntegrationNative owner,Session capture,DrawState state)
        {
            if(state.Changed&&(!ReferenceEquals(owner.currentShader.GetValue(state.Stage),state.Replacement)||!owner.resources.IsBound(state.Stage,capture.Gate)))
            {capture.Gate.Reject("Portal private shader or threshold was replaced before native execution.");return false;}
            if(owner.TargetsMainView(state.Context,capture))
            {capture.Gate.Reject("Portal capture draw attempted to write the protected main backbuffer.");return false;}
            return true;
        }
        // Guard the exact renderer IL call rather than relying on a detour of
        // SharpDX's tiny calli wrapper, which the JIT can inline. The native
        // detour remains a defense for foreign/non-renderer managed calls.
        internal static IEnumerable<CodeInstruction> AuditNativeCallSite(IEnumerable<CodeInstruction> instructions,ILGenerator generator)
        {
            var result=new List<CodeInstruction>();int matched=0;
            foreach(var instruction in instructions)
            {
                var method=instruction.operand as MethodInfo;
                if((instruction.opcode!=OpCodes.Call&&instruction.opcode!=OpCodes.Callvirt)||method==null||method.DeclaringType.FullName!="SharpDX.Direct3D11.DeviceContext"||!method.Name.StartsWith("Draw",StringComparison.Ordinal))
                {result.Add(instruction);continue;}
                if(method.ReturnType!=typeof(void)||method.IsStatic||instruction.blocks.Count!=0)throw new InvalidOperationException("Portal audited draw call ABI/control flow changed.");
                matched++;var args=method.GetParameters().Select(p=>generator.DeclareLocal(p.ParameterType)).ToArray();var native=generator.DeclareLocal(method.DeclaringType);var skip=generator.DefineLabel();
                var first=new CodeInstruction(OpCodes.Stloc,args.Length==0?native:args[args.Length-1]);first.labels.AddRange(instruction.labels);result.Add(first);
                for(int i=args.Length-2;i>=0;i--)result.Add(new CodeInstruction(OpCodes.Stloc,args[i]));
                if(args.Length!=0)result.Add(new CodeInstruction(OpCodes.Stloc,native));
                result.Add(new CodeInstruction(OpCodes.Ldloc,native));result.Add(new CodeInstruction(OpCodes.Call,typeof(PortalCaptureIntegrationNative).GetMethod("BeforeAuditedNativeDraw",Static)));
                result.Add(new CodeInstruction(OpCodes.Brfalse,skip));result.Add(new CodeInstruction(OpCodes.Ldloc,native));foreach(var arg in args)result.Add(new CodeInstruction(OpCodes.Ldloc,arg));
                result.Add(new CodeInstruction(instruction.opcode,method));result.Add(new CodeInstruction(OpCodes.Call,typeof(PortalCaptureIntegrationNative).GetMethod("AfterAuditedNativeDraw",Static)));
                var end=new CodeInstruction(OpCodes.Nop);end.labels.Add(skip);result.Add(end);
            }
            if(matched!=1)throw new InvalidOperationException("Portal renderer requires exactly one audited native draw call site; found "+matched+".");return result;
        }
        bool MatchingViewport(object context,Session capture)
        {
            object value=viewport.GetValue(contextState.GetValue(context));float[] v=viewportFields.Select(f=>Convert.ToSingle(f.GetValue(value))).ToArray();
            return v[0]==0&&v[1]==0&&v[2]==capture.Width&&v[3]==capture.Height&&v[4]==0&&v[5]==1;
        }
        object MainView(PortalCaptureGate capture)
        {
            try{object target=backbuffer.GetValue(null),view=target==null?null:nativeRtv.GetValue(target);if(view==null)capture.Reject("Portal protected main backbuffer view is unavailable.");return view;}
            catch(Exception ex){capture.Reject("Portal protected target lookup failed: "+ex.GetBaseException().Message);return null;}
        }
        bool TargetsMainView(object context,Session capture)
        {
            if(capture.MainTargetView==null)return false;object state=contextState.GetValue(context);var views=targetViews.GetValue(state) as Array;int count=(int)targetCount.GetValue(state);
            if(views==null||count<0||count>views.Length)return true;
            for(int i=0;i<count;i++)if(ReferenceEquals(views.GetValue(i),capture.MainTargetView))return true;return false;
        }
        internal void ResetRenderResources()
        {
            if(Volatile.Read(ref session)!=null)throw new InvalidOperationException("Portal capture must join before a resource reset.");
            threshold.Dispose();shaders.ResetEpoch();preparedRoles=null;preparedDescriptors=null;
        }
        void Unpatch()
        {
            if(harmony!=null)foreach(var method in patched)harmony.Unpatch(method,HarmonyPatchType.All,PatchId);
            patched.Clear();harmony=null;Installed=false;if(ReferenceEquals(active,this))active=null;
        }
        // Must run on the serialized render boundary; the caller owns scheduling.
        public void Dispose(){if(disposed)return;ResetRenderResources();shaders.Dispose();Unpatch();disposed=true;}
        static FieldInfo F(Type type,string name){return type.GetField(name,Instance)??throw new InvalidOperationException("Portal draw coverage field "+type+"."+name);}
        static string ResolveInstalledShaderRoot(Assembly assembly)
        {
            Type filesystem=DirectCameraCaptureNative.FindType(assembly,"VRage.FileSystem.MyFileSystem");var content=filesystem.GetProperty("ContentPath",Static);
            string path=null;
            try{if(content!=null)path=content.GetValue(null) as string;}
            catch(TargetInvocationException ex){if(!(ex.InnerException is InvalidOperationException))throw;}
            if(!string.IsNullOrEmpty(path))return Path.GetFullPath(Path.Combine(path,"Shaders"));
            // Headless ABI fixtures have no initialized game filesystem. This
            // conservative fallback must still match the frozen asset manifest.
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(assembly.Location),"..","Content","Shaders"));
        }
    }
}
