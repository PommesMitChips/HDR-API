using System;
using System.Linq;
using System.Reflection;
using VRageRender;
namespace HDRClientRenderer
{
    // This pass is called ONLY inside the native capture owner's isolated,
    // serialized render boundary. It installs no global hook and never changes
    // capability on its own. Construction validates/compiles on the CPU only.
    internal sealed class PortalCaptureThresholdNative : IDisposable
    {
        const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly PortalCaptureResources resources;
        readonly PropertyInfo device,pixel;
        readonly FieldInfo buffers,blend,depth;
        readonly MethodInfo clear,setBlend,setDepth,setTarget,setShader,setConstants,draw,createBuffer,disposeBuffers,map,write,unmap;
        readonly ConstructorInfo shaderConstructor;
        readonly Type bufferType;
        readonly byte[] bytecode,ellipsoidBytecode;
        object currentDevice,shader,constants;byte[] selectedBytecode;int constantBytes;
        internal PortalCaptureThresholdNative(Assembly assembly,PortalCaptureResources targets,PortalClipShaders compiler)
        {
            if(targets==null||compiler==null)throw new ArgumentNullException("targets");resources=targets;
            Func<string,Type> type=name=>DirectCameraCaptureNative.FindType(assembly,name);
            var context=type("VRage.Render11.RenderContext.MyRenderContext");var ps=type("VRage.Render11.RenderContext.MyPixelStage");
            var manager=type("VRage.Render11.Resources.MyBufferManager");var mapping=type("VRageRender.MyMapping");
            bufferType=type("VRage.Render11.Resources.IConstantBuffer");
            device=P(type("VRageRender.MyRender11"),"DeviceInstance",Static);pixel=P(context,"PixelShader",Instance);
            buffers=F(type("VRage.Render11.Common.MyManagers"),"Buffers",Static);
            blend=F(type("VRage.Render11.Resources.MyBlendStateManager"),"BlendReplace",Static);
            depth=F(type("VRage.Render11.Resources.MyDepthStencilStateManager"),"IgnoreDepthStencil",Static);
            clear=M(context,"ClearState",Instance,Type.EmptyTypes);
            setBlend=context.GetMethods(Instance).Single(m=>m.Name=="SetBlendState"&&m.GetParameters().Length==2);
            setDepth=context.GetMethods(Instance).Single(m=>m.Name=="SetDepthStencilState"&&m.GetParameters().Length==2);
            setTarget=M(context,"SetRtv",Instance,type("VRage.Render11.Resources.IRtvBindable"));
            setShader=ps.GetMethods(Instance).Single(m=>m.Name=="Set"&&m.GetParameters().Length==1);
            setConstants=M(ps,"SetConstantBuffer",Instance,typeof(int),bufferType);
            draw=type("VRageRender.MyScreenPass").GetMethods(Static).Single(m=>m.Name=="DrawFullscreenQuad"&&m.GetParameters().Length==3);
            if(draw.GetParameters()[2].ParameterType!=typeof(bool)||draw.GetParameters()[2].Name!="updateVertexBuffer")throw new InvalidOperationException("Portal fullscreen vertex ABI changed.");
            createBuffer=manager.GetMethods(Instance).Single(m=>m.Name=="CreateConstantBuffer"&&m.GetParameters().Length==5);
            disposeBuffers=M(manager,"Dispose",Instance,bufferType.MakeArrayType());
            map=M(mapping,"MapDiscard",Static,context,type("VRage.Render11.Resources.IBuffer"));
            write=mapping.GetMethods(Instance).Single(m=>m.Name=="WriteAndPosition"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==3).MakeGenericMethod(typeof(float));
            unmap=M(mapping,"Unmap",Instance,Type.EmptyTypes);
            shaderConstructor=type("SharpDX.Direct3D11.PixelShader").GetConstructor(new[]{type("SharpDX.Direct3D11.Device"),typeof(byte[]),type("SharpDX.Direct3D11.ClassLinkage")});
            if(shaderConstructor==null)throw new InvalidOperationException("Portal private shader constructor ABI changed.");
            bytecode=compiler.CompileFixed(PortalProjectionShaders.Threshold);
            if(compiler.Inspect(bytecode,false).Length!=1)throw new InvalidOperationException("Portal threshold shader requires one R32 output.");
            ellipsoidBytecode=compiler.CompileFixed(PortalProjectionShaders.EllipsoidThreshold);
            if(compiler.Inspect(ellipsoidBytecode,false).Length!=1)throw new InvalidOperationException("Portal ellipsoid threshold shader requires one R32 output.");
        }
        internal bool Generate(object isolatedContext,PortalCaptureSpec spec,PortalCaptureGate capture)
        {
            if(isolatedContext==null||spec==null||capture==null)throw new ArgumentNullException("spec");
            resources.Invalidate();
            if(spec.Epoch!=capture.Epoch||spec.Generation!=capture.Generation){capture.Reject("Portal threshold capture generation mismatch.");return false;}
            float[] values=PortalProjectionShaders.ThresholdConstants(spec);
            return GenerateCore(isolatedContext,spec.Width,spec.Height,spec.Epoch,spec.Generation,values,bytecode,capture);
        }
        internal bool Generate(object isolatedContext,PortalRayMapSpec spec,int width,int height,PortalCaptureGate capture)
        {
            if(isolatedContext==null||spec==null||capture==null)throw new ArgumentNullException("spec");resources.Invalidate();
            if(spec.ImageProjection!=PortalImageProjection.Perspective){capture.Reject("Portal native threshold requires a perspective capture.");return false;}
            return GenerateCore(isolatedContext,width,height,spec.Epoch,spec.Generation,PortalProjectionShaders.EllipsoidThresholdConstants(spec,width,height),ellipsoidBytecode,capture);
        }
        bool GenerateCore(object isolatedContext,int width,int height,int epoch,long generation,float[] values,byte[] code,PortalCaptureGate capture)
        {
            if(epoch!=capture.Epoch||generation!=capture.Generation){capture.Reject("Portal threshold capture generation mismatch.");return false;}
            int expected=ReferenceEquals(code,bytecode)?40:52;
            if(values==null||values.Length!=expected||values.Any(v=>float.IsNaN(v)||float.IsInfinity(v))){capture.Reject("Portal native shell precision/axis ratios or threshold constants are outside the supported finite domain.");return false;}
            try
            {
                EnsureResources(code,values.Length*4);resources.AllocateThreshold(width,height);
                object replace=blend.GetValue(null),ignore=depth.GetValue(null);
                if(replace==null||ignore==null)throw new InvalidOperationException("Portal threshold render states unavailable.");
                // Native owner must suppress shader-selection callbacks for this
                // trusted auxiliary pass; they apply to world draws only.
                clear.Invoke(isolatedContext,null);
                object stage=pixel.GetValue(isolatedContext),mapped=map.Invoke(null,new[]{isolatedContext,constants});
                try{write.Invoke(mapped,new object[]{values,values.Length,0});}finally{unmap.Invoke(mapped,null);}
                setBlend.Invoke(isolatedContext,new[]{replace,null});setDepth.Invoke(isolatedContext,new[]{ignore,(object)0});
                setShader.Invoke(stage,new[]{shader});setConstants.Invoke(stage,new[]{(object)1,constants});
                setTarget.Invoke(isolatedContext,new[]{resources.Threshold});
                draw.Invoke(null,new[]{isolatedContext,(object)new MyViewport(width,height),true});
                // Remove RTV before any worker can sample this target as t31.
                clear.Invoke(isolatedContext,null);resources.Generated(epoch,generation,width,height);
            }
            catch(Exception ex){resources.Invalidate();capture.Reject("Portal threshold generation failed: "+ex.GetBaseException().Message);}
            finally{try{clear.Invoke(isolatedContext,null);}catch(Exception ex){resources.Invalidate();capture.Reject("Portal threshold context cleanup failed: "+ex.GetBaseException().Message);}}
            return capture.Failure==null;
        }
        void EnsureResources(byte[] code,int bytes)
        {
            object active=device.GetValue(null);if(active==null)throw new InvalidOperationException("Portal render device is unavailable.");
            if(ReferenceEquals(currentDevice,active)&&ReferenceEquals(selectedBytecode,code)&&constantBytes==bytes&&shader!=null&&constants!=null)return;
            Dispose();currentDevice=active;selectedBytecode=code;constantBytes=bytes;shader=shaderConstructor.Invoke(new[]{active,(object)code,null});
            var p=createBuffer.GetParameters();constants=createBuffer.Invoke(buffers.GetValue(null),new object[]{"HDR.Portal.ThresholdConstants",bytes,null,Enum.ToObject(p[3].ParameterType,2),false});
            if(shader==null||constants==null)throw new InvalidOperationException("Portal threshold GPU allocation failed.");
        }
        public void Dispose()
        {
            resources.DestroyThreshold();
            if(constants!=null){var a=Array.CreateInstance(bufferType,1);a.SetValue(constants,0);disposeBuffers.Invoke(buffers.GetValue(null),new object[]{a});constants=null;}
            if(shader is IDisposable)((IDisposable)shader).Dispose();shader=currentDevice=null;selectedBytecode=null;constantBytes=0;
        }
        static PropertyInfo P(Type t,string n,BindingFlags f){return t.GetProperty(n,f)??throw new InvalidOperationException("Portal threshold property "+t+"."+n);}
        static FieldInfo F(Type t,string n,BindingFlags f){return t.GetField(n,f)??throw new InvalidOperationException("Portal threshold field "+t+"."+n);}
        static MethodInfo M(Type t,string n,BindingFlags f,params Type[] args){return t.GetMethod(n,f,null,args,null)??throw new InvalidOperationException("Portal threshold method "+t+"."+n);}
    }
}
