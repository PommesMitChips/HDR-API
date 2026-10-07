using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace HDRClientRenderer
{
    // Private shaders only. The owner freezes CPU jobs at the render boundary, creates GPU
    // objects on its serialized boundary, and selects only while world draws are
    // covered by its active gate. No engine shader registry entry is replaced.
    internal sealed class PortalCaptureShaderNative : IDisposable
    {
        const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly PortalClipRegistry registry;
        readonly PortalClipShaders compiler;
        readonly PortalClipWarmup warmup;
        readonly PortalCaptureResources resources;
        readonly PropertyInfo device;
        readonly ConstructorInfo create;
        readonly byte[] depthBytecode,discardBytecode;
        readonly HashSet<object> boundStages=new HashSet<object>();readonly object bindingGate=new object();
        object currentDevice,depthOnly,discard;
        internal readonly MethodInfo PixelShaderSet;
        internal int Epoch{get{return registry.Epoch;}}
        internal string Status{get{return registry.Status;}}
        internal PortalCaptureShaderNative(Assembly assembly,PortalCaptureResources targets,PortalClipShaders trustedCompiler,int initialEpoch=0)
        {
            if(targets==null||trustedCompiler==null)throw new ArgumentNullException("targets");resources=targets;compiler=trustedCompiler;
            registry=new PortalClipRegistry(initialEpoch);
            warmup=new PortalClipWarmup(registry,d=>{var prepared=compiler.Prepare(d);return ()=>compiler.CompilePrepared(prepared);},2);
            Func<string,Type> type=name=>DirectCameraCaptureNative.FindType(assembly,name);
            device=type("VRageRender.MyRender11").GetProperty("DeviceInstance",Static);
            create=type("SharpDX.Direct3D11.PixelShader").GetConstructor(new[]{type("SharpDX.Direct3D11.Device"),typeof(byte[]),type("SharpDX.Direct3D11.ClassLinkage")});
            PixelShaderSet=type("VRage.Render11.RenderContext.MyPixelStage").GetMethods(Instance).Single(m=>m.Name=="Set"&&m.GetParameters().Length==1);
            if(device==null||create==null||PixelShaderSet.GetParameters()[0].ParameterType!=create.DeclaringType)throw new InvalidOperationException("Portal pixel selector ABI changed.");
            depthBytecode=compiler.CompileFixed(PortalClipSource.DepthOnly);discardBytecode=compiler.CompileFixed(PortalClipSource.DiscardAll);
            if(compiler.Inspect(depthBytecode,true).Length!=0||compiler.Inspect(discardBytecode,false).Length!=0)throw new InvalidOperationException("Portal no-colour fallback ABI changed.");
        }
        internal int WarmCpu()
        {
            foreach(var descriptor in compiler.ReadRegistry())registry.Queue(descriptor);
            return registry.Warm(compiler.Compile);
        }
        internal int WarmCpu(IEnumerable<PortalClipDescriptor> snapshot)
        {foreach(var descriptor in snapshot)registry.Queue(descriptor);return registry.Warm(compiler.Compile);}
        internal void ScheduleCpu(){warmup.Schedule();}
        internal void UnbindAfterDraw(object stage,PortalCaptureGate capture)
        {
            try{resources.Unbind(stage);}catch(Exception ex){capture.Reject("Portal threshold per-draw unbind failed: "+ex.GetBaseException().Message);}
            finally{lock(bindingGate)boundStages.Remove(stage);}
        }
        // Once per capture after WarmCpu, before workers can see any shader.
        internal void PrepareGpu()
        {
            object active=device.GetValue(null);if(active==null)throw new InvalidOperationException("Portal render device unavailable.");
            if(!ReferenceEquals(active,currentDevice))
            {
                if(currentDevice!=null)ResetEpoch();
                try
                {
                    depthOnly=create.Invoke(new[]{active,(object)depthBytecode,null});discard=create.Invoke(new[]{active,(object)discardBytecode,null});currentDevice=active;
                }
                catch{ResetEpoch();throw;}
            }
            int attached=0;
            foreach(var item in registry.AwaitingAttachment())
            {
                if(attached++>=PortalClipRegistry.MaxCompilationsPerUpdate)break;
                object privateShader=create.Invoke(new[]{active,(object)item.Bytecode,null});
                if(!registry.Attach(item,privateShader))Release(privateShader);
            }
            if(depthOnly==null||discard==null)throw new InvalidOperationException("Portal fail-closed shaders unavailable.");
        }
        // Suitable for a MyPixelStage.Set prefix with ref object __0. Unknown
        // geometry is discarded and also rejects the frame; it cannot publish
        // a partially clipped image. Auxiliary/postprocessing passes must be
        // distinguished by the owner, never inferred from an unknown shader.
        internal bool Select(object pixelStage,object original,PortalCaptureGate capture,out object replacement,PortalClipDescriptor demanded=null)
        {
            replacement=discard;
            if(capture==null)return false;
            if(pixelStage==null||discard==null){capture.Reject("Portal native shader owner has not prepared its fail-closed resources.");return false;}
            if(capture.Epoch!=Epoch){capture.Reject("Portal shader owner epoch changed.");return false;}
            bool cutoff=resources.Bind(pixelStage,capture);
            if(cutoff)lock(bindingGate)boundStages.Add(pixelStage);
            if(original==null)
            {
                if(!capture.Observe(PortalClipFamily.Depth,depthOnly!=null,cutoff))return false;
                replacement=depthOnly;return true;
            }
            if(demanded!=null&&ReferenceEquals(demanded.Original,original))registry.Queue(demanded);
            object selected;if(!registry.Select(original,capture,cutoff,out selected))return false;
            replacement=selected;return true;
        }
        // All deferred workers MUST have joined first. Call even after failures,
        // before the native owner restores or returns any borrowed contexts.
        internal void UnbindAfterJoin(PortalCaptureGate capture)
        {
            lock(bindingGate)
            {
                foreach(object stage in boundStages)
                    try{resources.Unbind(stage);}catch(Exception ex){if(capture!=null)capture.Reject("Portal threshold unbind failed: "+ex.GetBaseException().Message);}
                boundStages.Clear();
            }
        }
        internal void ResetEpoch()
        {
            lock(bindingGate)if(boundStages.Count!=0)throw new InvalidOperationException("Portal contexts must be joined and unbound before resetting shaders.");
            foreach(var value in registry.NewEpoch())Release(value);
            Release(depthOnly);Release(discard);depthOnly=discard=currentDevice=null;resources.Invalidate();
        }
        public void Dispose(){warmup.Dispose();ResetEpoch();}
        static void Release(object value){if(value is IDisposable)((IDisposable)value).Dispose();}
    }
}
