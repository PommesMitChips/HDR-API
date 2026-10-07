using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using VRageMath;
using VRageRender;

namespace HDRClientRenderer
{
    // GPU ownership lives on the existing capture owner's serialized render
    // boundary. The callback swaps native context state and restores engine
    // caches; this class never installs an independent scene hook.
    internal sealed class PortalNativeGpu : PortalProvider.INative, IDisposable
    {
        internal delegate bool CaptureCall(object target,int resolution,PortalRayMapSpec spec,out string error);
        const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly CaptureCall capture;
        readonly Action<Action<object>> isolated;
        readonly Action<Action> cleanup;
        readonly Func<long> epoch;
        readonly Func<bool> ready;
        readonly Func<string> nativeStatus;
        readonly PropertyInfo device,pixel,resource,rtv,srv,size,rc,viewDebugName,resourceDebugName;
        readonly FieldInfo fileTextures,rwTextures,buffers,blend,depth,linear,dxContext;
        readonly MethodInfo createTarget,destroyTarget,tryTarget,clearRtv,createRtv,disposeRtv,update,clear,setBlend,setDepth,setTarget,setShader,setSrv,setSampler,setConstants,draw,generateMips,
            createBuffer,disposeBuffers,map,write,unmap,copyRegion;
        readonly ConstructorInfo shaderConstructor,dataBox,transparent,resourceRegion;
        readonly Type bufferType,format;
        readonly PanoramaGeneratedTarget generated;
        readonly PortalSingleMipTargetNative single;
        readonly PortalPrimaryViewNative primaryView;
        readonly PortalBillboardProjectiveNative projective;
        readonly Dictionary<object,PortalProjectiveSampling> projectiveFrames=new Dictionary<object,PortalProjectiveSampling>();
        readonly HashSet<object> rawFronts=new HashSet<object>();
        readonly PortalFrontTargetPool fronts;readonly Dictionary<string,string> privateNames=new Dictionary<string,string>();readonly string privateOwner=Guid.NewGuid().ToString("N");
        readonly byte[] shaderBytes;
        object activeDevice,shader,constants,origins,directions;
        int rayWidth,rayHeight;long resourceEpoch=long.MinValue;bool disposed;
        public bool Ready{get{return !disposed&&ready();}}public string Reason{get{return nativeStatus!=null?nativeStatus():(Ready?"Portal capture/compositor native ABI installed; visual validation pending.":"Portal native capture is unavailable or its coverage gate is warming.");}}
        public long DeviceEpoch{get{return epoch();}}
        internal int ShaderBytes{get{return shaderBytes.Length;}}
        internal PortalNativeGpu(Assembly assembly,CaptureCall capture,Action<Action<object>> isolated,Action<Action> cleanup,Func<long> epoch,Func<bool> ready,Func<string> nativeStatus=null)
        {
            this.capture=capture??throw new ArgumentNullException("capture");this.isolated=isolated??throw new ArgumentNullException("isolated");this.cleanup=cleanup??throw new ArgumentNullException("cleanup");this.epoch=epoch??throw new ArgumentNullException("epoch");this.ready=ready??throw new ArgumentNullException("ready");
            this.nativeStatus=nativeStatus;
            Func<string,Type> type=name=>DirectCameraCaptureNative.FindType(assembly,name);
            var context=type("VRage.Render11.RenderContext.MyRenderContext");var ps=type("VRage.Render11.RenderContext.MyPixelStage");var common=type("VRage.Render11.RenderContext.MyCommonStage");
            var texture=type("VRage.Render11.Resources.IUserGeneratedTexture");var rtvType=type("VRage.Render11.Resources.IRtvBindable");var srvType=type("VRage.Render11.Resources.ISrvBindable");var manager=type("VRage.Render11.Resources.MyBufferManager");var mapping=type("VRageRender.MyMapping");
            bufferType=type("VRage.Render11.Resources.IConstantBuffer");format=type("SharpDX.DXGI.Format");
            device=P(type("VRageRender.MyRender11"),"DeviceInstance",Static);pixel=P(context,"PixelShader",Instance);rtv=P(rtvType,"Rtv",Instance);srv=P(srvType,"Srv",Instance);resource=P(type("VRage.Render11.Resources.IResource"),"Resource",Instance);
            rc=P(type("VRageRender.MyRender11"),"RC",Static);viewDebugName=P(type("SharpDX.Direct3D11.ShaderResourceView"),"DebugName",Instance);
            resourceDebugName=P(type("SharpDX.Direct3D11.Resource"),"DebugName",Instance);
            tryTarget=M(type("VRage.Render11.Resources.MyFileTextureManager"),"TryGetTexture",Instance,typeof(string),texture.MakeByRefType());
            size=P(type("VRage.Render11.Resources.IResource"),"Size",Instance);var region=type("SharpDX.Direct3D11.ResourceRegion");resourceRegion=region.GetConstructor(new[]{typeof(int),typeof(int),typeof(int),typeof(int),typeof(int),typeof(int)});
            copyRegion=M(type("SharpDX.Direct3D11.DeviceContext"),"CopySubresourceRegion",Instance,type("SharpDX.Direct3D11.Resource"),typeof(int),typeof(Nullable<>).MakeGenericType(region),type("SharpDX.Direct3D11.Resource"),typeof(int),typeof(int),typeof(int),typeof(int));
            if(resourceRegion==null)throw new InvalidOperationException("Portal active rectangle copy ABI changed.");
            var managers=type("VRage.Render11.Common.MyManagers");fileTextures=F(managers,"FileTextures",Static);rwTextures=F(managers,"RwTextures",Static);buffers=F(managers,"Buffers",Static);dxContext=F(context,"m_deviceContext",Instance);
            blend=F(type("VRage.Render11.Resources.MyBlendStateManager"),"BlendReplace",Static);depth=F(type("VRage.Render11.Resources.MyDepthStencilStateManager"),"IgnoreDepthStencil",Static);linear=F(type("VRage.Render11.Resources.MySamplerStateManager"),"Linear",Static);
            createTarget=M(fileTextures.FieldType,"CreateGeneratedTexture",Instance,typeof(string),typeof(int),typeof(int),typeof(VRageRender.Messages.MyGeneratedTextureType),typeof(bool),typeof(byte[]),typeof(bool));
            destroyTarget=M(fileTextures.FieldType,"DestroyGeneratedTexture",Instance,typeof(string));
            createRtv=rwTextures.FieldType.GetMethods(Instance).Single(m=>m.Name=="CreateRtv"&&m.GetParameters().Length==10);
            disposeRtv=M(rwTextures.FieldType,"DisposeTex",Instance,type("VRage.Render11.Resources.IRtvTexture").MakeByRefType());
            dataBox=type("SharpDX.DataBox").GetConstructor(new[]{typeof(IntPtr),typeof(int),typeof(int)});
            update=M(type("SharpDX.Direct3D11.DeviceContext"),"UpdateSubresource",Instance,type("SharpDX.DataBox"),type("SharpDX.Direct3D11.Resource"),typeof(int));
            clear=M(context,"ClearState",Instance,Type.EmptyTypes);setBlend=context.GetMethods(Instance).Single(m=>m.Name=="SetBlendState"&&m.GetParameters().Length==2);setDepth=context.GetMethods(Instance).Single(m=>m.Name=="SetDepthStencilState"&&m.GetParameters().Length==2);
            clearRtv=context.GetMethods(Instance).Single(m=>m.Name=="ClearRtv"&&m.GetParameters().Length==2);transparent=clearRtv.GetParameters()[1].ParameterType.GetConstructor(new[]{typeof(float),typeof(float),typeof(float),typeof(float)});
            if(transparent==null||!viewDebugName.CanRead||!viewDebugName.CanWrite)throw new InvalidOperationException("Portal stable front clear/ownership ABI changed.");
            setTarget=M(context,"SetRtv",Instance,rtvType);setShader=ps.GetMethods(Instance).Single(m=>m.Name=="Set"&&m.GetParameters().Length==1);setSrv=M(common,"SetSrv",Instance,typeof(int),srvType);setSampler=common.GetMethods(Instance).Single(m=>m.Name=="SetSampler"&&m.GetParameters().Length==2);setConstants=M(ps,"SetConstantBuffer",Instance,typeof(int),bufferType);
            generateMips=M(context,"GenerateMips",Instance,srvType);draw=type("VRageRender.MyScreenPass").GetMethods(Static).Single(m=>m.Name=="DrawFullscreenQuad"&&m.GetParameters().Length==3);
            if(draw.GetParameters()[2].ParameterType!=typeof(bool)||draw.GetParameters()[2].Name!="updateVertexBuffer")throw new InvalidOperationException("Portal compositor fullscreen ABI changed.");
            createBuffer=manager.GetMethods(Instance).Single(m=>m.Name=="CreateConstantBuffer"&&m.GetParameters().Length==5);disposeBuffers=M(manager,"Dispose",Instance,bufferType.MakeArrayType());map=M(mapping,"MapDiscard",Static,context,type("VRage.Render11.Resources.IBuffer"));
            write=mapping.GetMethods(Instance).Single(m=>m.Name=="WriteAndPosition"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==3).MakeGenericMethod(typeof(float));unmap=M(mapping,"Unmap",Instance,Type.EmptyTypes);
            shaderConstructor=type("SharpDX.Direct3D11.PixelShader").GetConstructor(new[]{type("SharpDX.Direct3D11.Device"),typeof(byte[]),type("SharpDX.Direct3D11.ClassLinkage")});
            if(shaderConstructor==null||dataBox==null||createRtv.GetParameters()[3].ParameterType!=format)throw new InvalidOperationException("Portal compositor texture/shader ABI changed.");
            generated=new PanoramaGeneratedTarget(assembly);single=new PortalSingleMipTargetNative(assembly);var compiler=new PortalClipShaders(assembly);shaderBytes=compiler.CompileFixed(PortalProjectionShaders.GeneralComposite);
            primaryView=new PortalPrimaryViewNative(assembly);
            projective=new PortalBillboardProjectiveNative(assembly,()=>rawFronts.Select(t=>new KeyValuePair<object,PortalProjectiveSampling>(t,projectiveFrames.ContainsKey(t)?projectiveFrames[t]:null)).ToArray());
            if(compiler.Inspect(shaderBytes,false).Length!=1)throw new InvalidOperationException("Portal compositor must output colour only; entry geometry owns world depth.");
            fronts=new PortalFrontTargetPool(LookupTarget,n=>CreateRegisteredTarget(n,PortalFrontTargetPool.PhysicalSize(n),PortalFrontTargetPool.PhysicalSize(n),true,PortalFrontTargetPool.IsNative(n)),(t,s)=>generated.Matches(t,s,s)||single.Compatible(t,s,s),
                t=>srv.GetValue(t)!=null,(t,s)=>{if(srv.GetValue(t)!=null)throw new InvalidOperationException("Portal live front SRV must never reset.");bool activated=single.Matches(t,s,s)?single.Activate(t,s,s):generated.Activate(t,s,s);if(!activated||rtv.GetValue(t)==null||srv.GetValue(t)==null||resource.GetValue(t)==null)throw new InvalidOperationException("Portal front reactivation failed.");},
                t=>(string)viewDebugName.GetValue(srv.GetValue(t)),(t,stamp)=>viewDebugName.SetValue(srv.GetValue(t),stamp),ClearFront);
        }
        public bool TryView(out PortalPrimaryView view){view=null;return Ready&&primaryView.TrySnapshot(DeviceEpoch,out view);}
        public bool ProjectiveReady{get{return Ready&&projective.Available;}}
        internal void PreparePrimary()
        {
            if(disposed||!Ready)return;
            var registered=new HashSet<object>();
            for(int slot=0;slot<2;slot++)for(int bucket=256;bucket<=2048;bucket*=2)
            {
                string name="HDR_ClientPortal_"+slot+"_N"+bucket;object target=LookupTarget(name);if(target==null)continue;registered.Add(target);
                object value=resource.GetValue(target);if(value!=null&&((string)resourceDebugName.GetValue(value)??"").StartsWith("HDR.Portal.RawFront.",StringComparison.Ordinal))rawFronts.Add(target);
            }
            rawFronts.RemoveWhere(t=>!registered.Contains(t));foreach(var target in projectiveFrames.Keys.Where(t=>!registered.Contains(t)).ToArray())projectiveFrames.Remove(target);
            projective.PrepareRender(DeviceEpoch);
        }
        internal bool BeginPrimary(){PortalPrimaryView view;return TryView(out view)&&projective.BeginPrimary(view,rc.GetValue(null));}
        internal void EndPrimary(){projective.EndPrimary();}
        public object CreateTarget(string name,int width,int height)
        {
            int maximum=2048;
            if(disposed||string.IsNullOrEmpty(name)||!name.StartsWith("HDR_",StringComparison.Ordinal)||name.Length>128||width<64||height<64||width>maximum||height>maximum)throw new ArgumentException("Invalid portal owned target.");
            if(PortalFrontTargetPool.Slot(name)>=0){int physical=PortalFrontTargetPool.PhysicalSize(name);if(width!=physical||height!=physical)throw new ArgumentException("Portal public target must match its stable size bucket.");return fronts.Acquire(name);}
            if(privateNames.ContainsKey(name))throw new InvalidOperationException("Portal private target lease must retire before replacement.");
            bool staging=name.StartsWith("HDR_PortalStaging_",StringComparison.Ordinal);bool captured=name=="HDR_PortalCapture_0"||name=="HDR_PortalCapture_1";
            if(!staging&&!captured)throw new ArgumentException("Unknown trusted portal private target.");
            string owned=name+"_"+privateOwner;object created=CreateRegisteredTarget(owned,width,height,false,staging);privateNames.Add(name,owned);return created;
        }
        object LookupTarget(string name){var args=new object[]{name,null};return (bool)tryTarget.Invoke(fileTextures.GetValue(null),args)?args[1]:null;}
        object CreateRegisteredTarget(string name,int width,int height,bool preserveRegistration=false,bool singleMip=false)
        {
            object target=null;
            try
            {
                target=createTarget.Invoke(fileTextures.GetValue(null),new object[]{name,width,height,VRageRender.Messages.MyGeneratedTextureType.RGBA,true,null,true});
                if(target==null||!(singleMip?single.Prepare(target,width,height):generated.Matches(target,width,height)))throw new InvalidOperationException("Portal target allocation descriptor differs from its fixed ownership size.");
                if(!preserveRegistration&&(!(singleMip?single.Activate(target,width,height):generated.Activate(target,width,height))||rtv.GetValue(target)==null||srv.GetValue(target)==null||resource.GetValue(target)==null))throw new InvalidOperationException("Portal target allocation failed.");return target;
            }
            catch{if(!preserveRegistration)try{destroyTarget.Invoke(fileTextures.GetValue(null),new object[]{name});}catch{}throw;}
        }
        public void DestroyTarget(string name)
        {
            if(PortalFrontTargetPool.Slot(name)>=0){var target=LookupTarget(name);if(target!=null)projectiveFrames.Remove(target);fronts.Retire(name);return;}string owned;
            if(name!=null&&privateNames.TryGetValue(name,out owned)){destroyTarget.Invoke(fileTextures.GetValue(null),new object[]{owned});privateNames.Remove(name);}
        }
        public Action FrontRetirement(string name)
        {
            string stamp=fronts.OwnershipToken(name);object target=LookupTarget(name);var clear=fronts.CaptureRetirement(name);
            return ()=>{if(fronts.OwnsAcquisition(name,stamp)&&target!=null)projectiveFrames.Remove(target);clear();};
        }
        void ClearFront(object target)
        {
            // ClearRtv and GenerateMips address an explicit resource and do not
            // mutate render-context binding caches. No IsolatedPass/Ready lease
            // is needed during deferred provider/backend retirement.
            object context=rc.GetValue(null);if(context==null||rtv.GetValue(target)==null||srv.GetValue(target)==null||resource.GetValue(target)==null)throw new InvalidOperationException("Portal front tombstone requires a live render context and owned RTV/SRV/resource.");
            clearRtv.Invoke(context,new[]{target,transparent.Invoke(new object[]{0f,0f,0f,0f})});var dimensions=(Vector2I)size.GetValue(target);if(!single.Matches(target,dimensions.X,dimensions.Y))generateMips.Invoke(context,new[]{target});
        }
        public bool Capture(object target,int resolution,PortalRayMapSpec spec,out string error)
        {if(!Ready){error=Reason;return false;}return capture(target,resolution,spec,out error);}
        public void EnqueueCleanup(Action action){cleanup(action);}
        public bool Compose(object captured,object output,PortalRayPacket packet,float saturation,float brightness,out string error)
        {
            error=null;if(!Ready||captured==null||output==null||packet==null){error="Portal composition inputs unavailable.";return false;}
            long currentEpoch=DeviceEpoch;var values=PortalProjectionShaders.GeneralConstants(packet,saturation,brightness);if(values==null){error="Invalid portal compositor constants.";return false;}
            try
            {
                isolated(context=>
                {
                    Ensure(packet.TextureWidth,packet.TextureHeight);clear.Invoke(context,null);
                    try
                    {
                        Upload(context,origins,packet.CopyOrigins(),packet.TextureWidth,packet.TextureHeight);Upload(context,directions,packet.CopyDirections(),packet.TextureWidth,packet.TextureHeight);
                        var mapped=map.Invoke(null,new[]{context,constants});try{write.Invoke(mapped,new object[]{values,values.Length,0});}finally{unmap.Invoke(mapped,null);}
                        object stage=pixel.GetValue(context);setBlend.Invoke(context,new[]{blend.GetValue(null),null});setDepth.Invoke(context,new[]{depth.GetValue(null),(object)0});
                        setShader.Invoke(stage,new[]{shader});setConstants.Invoke(stage,new[]{(object)1,constants});setSampler.Invoke(stage,new[]{(object)2,linear.GetValue(null)});
                        setSrv.Invoke(stage,new[]{(object)0,captured});setSrv.Invoke(stage,new[]{(object)1,origins});setSrv.Invoke(stage,new[]{(object)2,directions});setTarget.Invoke(context,new[]{output});
                        var outputSize=(Vector2I)size.GetValue(output);int drawWidth,drawHeight;
                        if(!ActiveRectangle(outputSize,packet.OutputWidth,packet.OutputHeight,packet.Gutter,out drawWidth,out drawHeight)||!single.Compatible(output,outputSize.X,outputSize.Y)||!single.Matches(output,outputSize.X,outputSize.Y))throw new InvalidOperationException("Portal staging active rectangle or single-mip descriptor differs from its stable bucket.");
                        draw.Invoke(null,new[]{context,(object)new MyViewport(drawWidth,drawHeight),true});
                    }
                    finally{clear.Invoke(context,null);}
                });
                if(currentEpoch!=DeviceEpoch){error="Portal device epoch changed during composition.";return false;}return true;
            }
            catch(Exception ex){error="Portal GPU composition failed: "+ex.GetBaseException().Message;return false;}
        }
        public bool Publish(object staging,object front,int activeWidth,int activeHeight,int gutter,out string error)
        {
            error=null;if(!Ready||staging==null||front==null||ReferenceEquals(staging,front)){error="Portal publication targets unavailable or aliased.";return false;}
            long currentEpoch=DeviceEpoch;
            try
            {
                var dimensions=(Vector2I)size.GetValue(staging);
                int drawWidth,drawHeight;
                if(!ActiveRectangle(dimensions,activeWidth,activeHeight,gutter,out drawWidth,out drawHeight)||!single.Compatible(staging,dimensions.X,dimensions.Y)||!single.Matches(staging,dimensions.X,dimensions.Y)||!single.Compatible(front,dimensions.X,dimensions.Y)||!single.Matches(front,dimensions.X,dimensions.Y)||resource.GetValue(staging)==null||resource.GetValue(front)==null)
                {error="Portal publication requires matching owned single-mip sRGB targets and a bounded active rectangle.";return false;}
                projectiveFrames.Remove(front);
                isolated(context=>
                {
                    clear.Invoke(context,null);
                    try{copyRegion.Invoke(dxContext.GetValue(context),new[]{resource.GetValue(staging),(object)0,resourceRegion.Invoke(new object[]{0,0,0,drawWidth,drawHeight,1}),resource.GetValue(front),(object)0,0,0,0});}
                    finally{clear.Invoke(context,null);}
                });
                if(currentEpoch!=DeviceEpoch){error="Portal device epoch changed during publication.";return false;}
                resourceDebugName.SetValue(resource.GetValue(front),"HDR.Portal.ComposedFront");rawFronts.Remove(front);return true;
            }
            catch(Exception ex){error="Portal front target publication failed: "+ex.GetBaseException().Message;return false;}
        }
        public bool PublishProjective(object captured,object front,PortalProjectiveSampling sampling,out string error)
        {
            error=null;if(!ProjectiveReady||captured==null||front==null||sampling==null||ReferenceEquals(captured,front)||sampling.Epoch!=DeviceEpoch||!fronts.OwnsTarget(front))
            {error="Portal projective publication owner, epoch, target, or shader proof unavailable.";return false;}
            PortalPrimaryView current;if(!TryView(out current)||current.Frame!=sampling.ViewFrame||current.Epoch!=sampling.Epoch)
            {error="Portal projective publication no longer matches the current primary capture frame.";return false;}
            try
            {
                int w=sampling.ImageWidth,h=sampling.ImageHeight;
                if(!privateNames.Values.Any(n=>ReferenceEquals(LookupTarget(n),captured))||!generated.Matches(captured,w,h)||!single.Compatible(front,w,h)||!single.Matches(front,w,h)||resource.GetValue(captured)==null||resource.GetValue(front)==null)
                {error="Portal projective copy requires the owned raw capture and matching single-mip front.";return false;}
                PortalProjectiveSampling old;
                if(projectiveFrames.TryGetValue(front,out old)&&(sampling.Epoch<old.Epoch||sampling.Epoch==old.Epoch&&(sampling.CaptureGeneration<=old.CaptureGeneration||sampling.ViewFrame<old.ViewFrame)))
                {error="Portal projective publication would regress capture identity.";return false;}
                // No primary command list exists yet. Pixel commands and their
                // immutable mapping commit on the one serialized owner boundary.
                rawFronts.Add(front);resourceDebugName.SetValue(resource.GetValue(front),"HDR.Portal.RawFront."+sampling.Epoch);
                projectiveFrames.Remove(front);
                isolated(context=>{clear.Invoke(context,null);try{copyRegion.Invoke(dxContext.GetValue(context),new[]{resource.GetValue(captured),(object)0,resourceRegion.Invoke(new object[]{0,0,0,w,h,1}),resource.GetValue(front),(object)0,0,0,0});}finally{clear.Invoke(context,null);}});
                if(DeviceEpoch!=sampling.Epoch){error="Portal device epoch changed during raw publication.";return false;}
                projectiveFrames[front]=sampling;return true;
            }
            catch(Exception ex){projectiveFrames.Remove(front);error="Portal projective publication failed: "+ex.GetBaseException().Message;return false;}
        }
        internal static bool ActiveRectangle(Vector2I bucket,int width,int height,int gutter,out int drawWidth,out int drawHeight)
        {return PortalActiveRectangle.TryCreate(bucket,width,height,gutter,out drawWidth,out drawHeight);}
        void Ensure(int width,int height)
        {
            object active=device.GetValue(null);if(active==null)throw new InvalidOperationException("Portal device unavailable.");
            if(!ReferenceEquals(activeDevice,active)||resourceEpoch!=DeviceEpoch){Release();activeDevice=active;resourceEpoch=DeviceEpoch;}
            if(shader==null)shader=shaderConstructor.Invoke(new[]{active,(object)shaderBytes,null});
            if(constants==null){var p=createBuffer.GetParameters();constants=createBuffer.Invoke(buffers.GetValue(null),new object[]{"HDR.Portal.CompositeConstants",160,null,Enum.ToObject(p[3].ParameterType,2),false});}
            if(origins!=null&&rayWidth==width&&rayHeight==height)return;ReleaseRays();
            origins=CreateRayTexture("HDR.Portal.RayOrigins",width,height);directions=CreateRayTexture("HDR.Portal.RayDirections",width,height);rayWidth=width;rayHeight=height;
        }
        object CreateRayTexture(string name,int width,int height)
        {
            var p=createRtv.GetParameters();object target=createRtv.Invoke(rwTextures.GetValue(null),new object[]{name,width,height,Enum.Parse(format,"R32G32B32A32_Float"),1,0,Enum.ToObject(p[6].ParameterType,0),Enum.ToObject(p[7].ParameterType,0),1,Enum.ToObject(p[9].ParameterType,0)});
            if(target==null||srv.GetValue(target)==null||resource.GetValue(target)==null)
            {if(target!=null)disposeRtv.Invoke(rwTextures.GetValue(null),new[]{target});throw new InvalidOperationException("Portal RGBA32 ray texture allocation failed.");}return target;
        }
        void Upload(object context,object texture,float[] values,int width,int height)
        {
            if(values==null||values.Length!=(long)width*height*4)throw new InvalidOperationException("Portal ray texture upload size mismatch.");
            var pinned=GCHandle.Alloc(values,GCHandleType.Pinned);try{update.Invoke(dxContext.GetValue(context),new[]{dataBox.Invoke(new object[]{pinned.AddrOfPinnedObject(),width*16,width*height*16}),resource.GetValue(texture),(object)0});}finally{pinned.Free();}
        }
        void ReleaseRays()
        {if(origins!=null){var a=new[]{origins};disposeRtv.Invoke(rwTextures.GetValue(null),a);origins=null;}if(directions!=null){var a=new[]{directions};disposeRtv.Invoke(rwTextures.GetValue(null),a);directions=null;}rayWidth=rayHeight=0;}
        void Release()
        {ReleaseRays();if(constants!=null){var a=Array.CreateInstance(bufferType,1);a.SetValue(constants,0);disposeBuffers.Invoke(buffers.GetValue(null),new object[]{a});constants=null;}if(shader is IDisposable)((IDisposable)shader).Dispose();shader=activeDevice=null;resourceEpoch=long.MinValue;}
        public void Dispose(){if(disposed)return;disposed=true;cleanup(()=>{projective.Dispose();projectiveFrames.Clear();rawFronts.Clear();fronts.RetireAll();foreach(var name in new List<string>(privateNames.Keys))DestroyTarget(name);Release();});}
        static PropertyInfo P(Type t,string n,BindingFlags f){return t.GetProperty(n,f)??throw new InvalidOperationException("Portal GPU property "+t+"."+n);}
        static FieldInfo F(Type t,string n,BindingFlags f){return t.GetField(n,f)??throw new InvalidOperationException("Portal GPU field "+t+"."+n);}
        static MethodInfo M(Type t,string n,BindingFlags f,params Type[] p){return t.GetMethod(n,f,null,p,null)??throw new InvalidOperationException("Portal GPU method "+t+"."+n);}
    }
}
