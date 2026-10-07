using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using VRageRender;

namespace HDRClientRenderer
{
    // A trusted client-only hook. No caller-supplied shaders, pointers, paths or GPU
    // code enter it. LCD source authorization and resolution quotas run on the game
    // thread; this pass binds existing textures and preserves RGB independently of A.
    internal sealed class NativeTextureCopy : IDisposable
    {
        const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static NativeTextureCopy active;
        readonly TextureCopyQueue queue=new TextureCopyQueue();
        readonly int[] initializedTargets=new int[16],readyTargets=new int[16];
        Harmony harmony;MethodInfo original,prefix;
        Action<string> report;int errors;
        GpuPass pass;bool attempted,reportedCopy;
        long lastFrame=long.MinValue;
        ClientPixelBudget budget;
        internal void SetBudget(ClientPixelBudget value){budget=value;}
        static string BudgetKey(string target){return "lcd:"+target;}
        internal bool Ready {get{return pass!=null;}}
        internal bool Enqueue(string source,string target,int width,int height)
        {return Ready&&queue.Enqueue(source,target,width,height,()=>budget==null||budget.Request(BudgetKey(target),ClientPixelBudget.Kind.Lcd,width*height));}
        static int Slot(string target){const string prefix="HDR_ClientLcd_";int slot;return target!=null&&target.StartsWith(prefix,StringComparison.Ordinal)&&int.TryParse(target.Substring(prefix.Length),out slot)&&slot>=0&&slot<16?slot:-1;}
        internal bool TargetReady(string target){int slot=Slot(target);return slot>=0&&Interlocked.CompareExchange(ref readyTargets[slot],0,0)==1;}
        internal void Cancel(string target){queue.Cancel(target,()=>{if(budget!=null)budget.Cancel(BudgetKey(target));int slot=Slot(target);if(slot>=0){Interlocked.Exchange(ref initializedTargets[slot],0);Interlocked.Exchange(ref readyTargets[slot],0);}});}
        internal void Clear(){queue.Clear(()=>{for(int i=0;i<16;i++){if(budget!=null)budget.Cancel(BudgetKey("HDR_ClientLcd_"+i));Interlocked.Exchange(ref initializedTargets[i],0);Interlocked.Exchange(ref readyTargets[i],0);}});}
        internal void TryInstall(Action<string> log)
        {
            if(attempted)return;
            var renderer=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("VRageRender.MyRender11",false)).FirstOrDefault(t=>t!=null);
            if(renderer==null)return;
            attempted=true;report=log;
            try
            {
                pass=new GpuPass(renderer.Assembly);
                original=renderer.GetMethods(Static).Single(m=>m.Name=="DrawGameScene"&&m.GetParameters().Length==2);
                prefix=typeof(NativeTextureCopy).GetMethod("BeforeScene",Static);
                harmony=new Harmony("HDRClientRenderer.NativeTextureCopy");active=this;
                harmony.Patch(original,prefix:new HarmonyMethod(prefix){priority=Priority.Last});
                log("Direct LCD GPU copy installed; RGB-only replacement with opaque destination alpha.");
            }
            catch(Exception ex){Dispose();log("Direct LCD GPU copy unavailable: "+ex.GetBaseException().Message);}
        }
        static void BeforeScene(object __0)
        {
            var copier=active;if(copier==null||copier.pass==null||DirectCameraCapture.IsCapturing||!copier.pass.IsMainTarget(__0))return;
            long frame=copier.pass.Frame;if(copier.lastFrame==frame)return;copier.lastFrame=frame;
            copier.queue.Drain(request=>
            {
                int slot=Slot(request.Target);
                if(Interlocked.CompareExchange(ref copier.initializedTargets[slot],0,0)==0)
                {if(!copier.pass.InitializeTarget(request.Target,request.Width,request.Height))return false;Interlocked.Exchange(ref copier.initializedTargets[slot],1);if(copier.budget!=null)copier.budget.RecordBandwidth((long)request.Width*request.Height+ClientPixelBudget.MipPixels(request.Width,request.Height));}
                bool copied=copier.pass.Copy(request);
                if(copied){Interlocked.Exchange(ref copier.readyTargets[slot],1);if(copier.budget!=null){copier.budget.RecordBandwidth((long)request.Width*request.Height*3+ClientPixelBudget.MipPixels(request.Width,request.Height));copier.budget.Complete(BudgetKey(request.Target));}}
                if(copied&&!copier.reportedCopy)
                {copier.reportedCopy=true;copier.report("Direct LCD GPU copy submitted: "+request.Source+" -> "+request.Target+" "+request.Width+"x"+request.Height+" RGB, opaque alpha, fullscreen vertices bound.");}
                return copied;
            },message=>{if(copier.errors++<8)copier.report(message);},
            request=>copier.budget==null||copier.budget.TrySpend(BudgetKey(request.Target),ClientPixelBudget.Kind.Lcd,request.Width*request.Height),
            request=>{if(copier.budget!=null)copier.budget.Cancel(BudgetKey(request.Target));});
        }
        public void Dispose()
        {
            Clear();if(ReferenceEquals(active,this))active=null;
            if(harmony!=null&&original!=null&&prefix!=null)try{harmony.Unpatch(original,prefix);}catch{}
            harmony=null;pass=null;
        }
        // Bound reflection avoids a compile-time dependency on private/publicized
        // renderer types. Validate every required ABI before enabling the provider.
        internal sealed class GpuPass
        {
            readonly PropertyInfo rc,pixel,rtv,srv,backbuffer,frameCounter;
            readonly FieldInfo fileTextures,blend,depth,linear,shader;
            readonly MethodInfo tryTexture,clear,clearRtv,generateMips,setBlend,setLayout,setTarget,setDepth,
                setShader,setSrv,setSampler,shaderValue,draw;
            readonly ConstructorInfo color;
            readonly PanoramaGeneratedTarget generatedTarget;
            internal GpuPass(Assembly assembly)
            {
                Func<string,Type> type=name=>assembly.GetType(name,true);
                generatedTarget=new PanoramaGeneratedTarget(assembly);
                var renderer=type("VRageRender.MyRender11");
                var context=type("VRage.Render11.RenderContext.MyRenderContext");
                var ps=type("VRage.Render11.RenderContext.MyPixelStage");
                var common=type("VRage.Render11.RenderContext.MyCommonStage");
                var texture=type("VRage.Render11.Resources.IUserGeneratedTexture");
                var rtvType=type("VRage.Render11.Resources.IRtvBindable");
                var srvType=type("VRage.Render11.Resources.ISrvBindable");
                var file=type("VRage.Render11.Resources.MyFileTextureManager");
                rc=renderer.GetProperty("RC",Static);pixel=context.GetProperty("PixelShader",Instance);
                backbuffer=renderer.GetProperty("Backbuffer",Static);frameCounter=type("VRageRender.MyCommon").GetProperty("FrameCounter",Static);
                rtv=rtvType.GetProperty("Rtv");srv=srvType.GetProperty("Srv");
                fileTextures=type("VRage.Render11.Common.MyManagers").GetField("FileTextures",Static);
                blend=type("VRage.Render11.Resources.MyBlendStateManager").GetField("BlendReplaceNoAlphaChannel",Static);
                depth=type("VRage.Render11.Resources.MyDepthStencilStateManager").GetField("IgnoreDepthStencil",Static);
                linear=type("VRage.Render11.Resources.MySamplerStateManager").GetField("Linear",Static);
                shader=type("VRageRender.MyCopyToRT").GetField("m_copyFilterPs",Static);
                tryTexture=file.GetMethod("TryGetTexture",Instance,null,new[]{typeof(string),texture.MakeByRefType()},null);
                clear=context.GetMethod("ClearState",Instance,null,Type.EmptyTypes,null);
                generateMips=context.GetMethod("GenerateMips",Instance,null,new[]{srvType},null);
                clearRtv=context.GetMethods(Instance).Single(m=>m.Name=="ClearRtv"&&m.GetParameters().Length==2);
                var raw=clearRtv.GetParameters()[1].ParameterType;
                color=raw.GetConstructor(new[]{typeof(float),typeof(float),typeof(float),typeof(float)});
                setBlend=context.GetMethods(Instance).Single(m=>m.Name=="SetBlendState"&&m.GetParameters().Length==2);
                setLayout=context.GetMethod("SetInputLayout",Instance);
                setTarget=context.GetMethod("SetRtv",Instance,null,new[]{rtvType},null);
                setDepth=context.GetMethods(Instance).Single(m=>m.Name=="SetDepthStencilState"&&m.GetParameters().Length==2);
                setShader=ps.GetMethods(Instance).Single(m=>m.Name=="Set"&&m.GetParameters().Length==1);
                var pixelType=setShader.GetParameters()[0].ParameterType;
                shaderValue=shader.FieldType.GetMethods(Static).Single(m=>m.Name=="op_Implicit"&&m.ReturnType==pixelType);
                setSrv=common.GetMethod("SetSrv",Instance,null,new[]{typeof(int),srvType},null);
                setSampler=common.GetMethods(Instance).Single(m=>m.Name=="SetSampler"&&m.GetParameters().Length==2);
                draw=type("VRageRender.MyScreenPass").GetMethods(Static).Single(m=>m.Name=="DrawFullscreenQuad"&&m.GetParameters().Length==3);
                if(new object[]{rc,pixel,rtv,srv,backbuffer,frameCounter,fileTextures,blend,depth,linear,shader,tryTexture,clear,
                    clearRtv,generateMips,color,setBlend,setLayout,setTarget,setDepth,setShader,shaderValue,setSrv,setSampler,draw}.Any(x=>x==null))
                    throw new InvalidOperationException("GPU copy renderer ABI is incomplete.");
                if(draw.GetParameters()[2].ParameterType!=typeof(bool)||draw.GetParameters()[2].Name!="updateVertexBuffer")
                    throw new InvalidOperationException("Fullscreen copy vertex binding ABI changed.");
            }
            internal void DrawCopyQuad(object context,int width,int height)
            {
                // ClearState unbinds every vertex buffer. The third argument requests
                // rebinding the fullscreen vertices; it does not control stereo mode.
                var viewport=new MyViewport(width,height);
                draw.Invoke(null,new[]{context,(object)viewport,true});
            }
            internal bool IsMainTarget(object target){if(target==null)return false;object main=backbuffer.GetValue(null);return main!=null&&ReferenceEquals(target,main);}
            internal long Frame{get{return (long)frameCounter.GetValue(null);}}
            internal void RegenerateTargetMips(object context,object target){clear.Invoke(context,null);generateMips.Invoke(context,new[]{target});}
            internal bool InitializeTarget(string name,int width,int height)
            {
                var target=new object[]{name,null};
                if(!(bool)tryTexture.Invoke(fileTextures.GetValue(null),target)||target[1]==null)return false;
                if(!generatedTarget.Matches(target[1],width,height))return false;
                if(rtv.GetValue(target[1])==null||srv.GetValue(target[1])==null)if(!generatedTarget.Activate(target[1],width,height))return false;
                if(rtv.GetValue(target[1])==null||srv.GetValue(target[1])==null)return false;
                object context=rc.GetValue(null);
                try{clear.Invoke(context,null);clearRtv.Invoke(context,new[]{target[1],color.Invoke(new object[]{0f,0f,0f,1f})});RegenerateTargetMips(context,target[1]);return true;}
                finally{clear.Invoke(context,null);}
            }
            internal bool Copy(TextureCopyQueue.Request request)
            {
                object manager=fileTextures.GetValue(null);
                var from=new object[]{request.Source,null};var to=new object[]{request.Target,null};
                if(!(bool)tryTexture.Invoke(manager,from)||!(bool)tryTexture.Invoke(manager,to)||from[1]==null||to[1]==null||
                    ReferenceEquals(from[1],to[1])||srv.GetValue(from[1])==null||rtv.GetValue(to[1])==null)return false;
                if(!generatedTarget.Matches(to[1],request.Width,request.Height))return false;
                object copyShader=shaderValue.Invoke(null,new[]{shader.GetValue(null)});
                object copyBlend=blend.GetValue(null),copyDepth=depth.GetValue(null),copySampler=linear.GetValue(null);
                if(copyShader==null||copyBlend==null||copyDepth==null||copySampler==null)return false;
                object context=rc.GetValue(null),stage=pixel.GetValue(context);
                try
                {
                    clear.Invoke(context,null);
                    clearRtv.Invoke(context,new[]{to[1],color.Invoke(new object[]{0f,0f,0f,1f})});
                    setBlend.Invoke(context,new[]{copyBlend,null});
                    setLayout.Invoke(context,new object[]{null});
                    setShader.Invoke(stage,new[]{copyShader});
                    setTarget.Invoke(context,new[]{to[1]});
                    setDepth.Invoke(context,new[]{copyDepth,(object)0});
                    setSrv.Invoke(stage,new[]{(object)0,from[1]});
                    setSampler.Invoke(stage,new[]{(object)2,copySampler});
                    DrawCopyQuad(context,request.Width,request.Height);
                    RegenerateTargetMips(context,to[1]);
                    return true;
                }
                finally{clear.Invoke(context,null);}
            }
        }
    }
}
