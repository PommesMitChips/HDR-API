using System;
using System.Linq;
using System.Reflection;
namespace HDRClientRenderer
{
    // Native owner must call allocation/disposal on its serialized render boundary.
    // Construction validates ABI only and never allocates or draws GPU resources.
    internal sealed class PortalCaptureResources
    {
        const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly FieldInfo manager,srvs;readonly MethodInfo create,dispose,setSrv;
        readonly PropertyInfo srv,rtv;readonly Type format;
        internal object Threshold{get;private set;}internal int Width{get;private set;}internal int Height{get;private set;}
        int thresholdEpoch=-1;long thresholdGeneration=-1;
        internal PortalCaptureResources(Assembly assembly)
        {
            Func<string,Type> type=name=>DirectCameraCaptureNative.FindType(assembly,name);
            Type managers=type("VRage.Render11.Common.MyManagers"),rw=type("VRage.Render11.Resources.MyRwTextureManager");format=type("SharpDX.DXGI.Format");
            manager=managers.GetField("RwTextures",Static);
            create=rw.GetMethods(Instance).Single(m=>m.Name=="CreateRtv"&&m.GetParameters().Length==10);
            var p=create.GetParameters();if(p[0].ParameterType!=typeof(string)||p[1].ParameterType!=typeof(int)||p[2].ParameterType!=typeof(int)||p[3].ParameterType!=format||p[8].ParameterType!=typeof(int))throw new InvalidOperationException("Portal threshold texture allocation ABI changed.");
            Type texture=type("VRage.Render11.Resources.IRtvTexture"),srvType=type("VRage.Render11.Resources.ISrvBindable"),rtvType=type("VRage.Render11.Resources.IRtvBindable");
            if(create.ReturnType!=texture||!srvType.IsAssignableFrom(texture)||!rtvType.IsAssignableFrom(texture))throw new InvalidOperationException("Portal threshold requires private R32 RTV and SRV interfaces.");
            dispose=rw.GetMethod("DisposeTex",Instance,null,new[]{texture.MakeByRefType()},null);
            srv=srvType.GetProperty("Srv",Instance);rtv=rtvType.GetProperty("Rtv",Instance);
            var common=type("VRage.Render11.RenderContext.MyCommonStage");srvs=common.GetField("m_srvs",Instance);setSrv=common.GetMethod("SetSrv",Instance,null,new[]{typeof(int),srvType},null);
            if(manager==null||dispose==null||srv==null||rtv==null||srvs==null||setSrv==null)throw new InvalidOperationException("Portal threshold binding ABI incomplete.");
        }
        internal void AllocateThreshold(int width,int height)
        {
            if(width<64||height<64||width>2048||height>2048||(long)width*height>4194304)throw new ArgumentOutOfRangeException("width");
            if(Threshold!=null&&Width==width&&Height==height)return;DestroyThreshold();var p=create.GetParameters();
            Threshold=create.Invoke(manager.GetValue(null),new object[]{"HDR.Portal.ExitThreshold",width,height,Enum.Parse(format,"R32_Float"),1,0,
                Enum.ToObject(p[6].ParameterType,0),Enum.ToObject(p[7].ParameterType,0),1,Enum.ToObject(p[9].ParameterType,0)});
            Width=width;Height=height;
            if(Threshold==null||srv.GetValue(Threshold)==null||rtv.GetValue(Threshold)==null){DestroyThreshold();throw new InvalidOperationException("Portal private linear threshold target is unavailable.");}
        }
        internal void Generated(PortalCaptureSpec spec)
        {
            if(spec==null||Threshold==null||spec.Width!=Width||spec.Height!=Height)throw new InvalidOperationException("Portal threshold generation does not match its capture viewport.");
            Generated(spec.Epoch,spec.Generation,spec.Width,spec.Height);
        }
        internal void Generated(int epoch,long generation,int width,int height)
        {if(epoch<0||generation<0||Threshold==null||width!=Width||height!=Height)throw new InvalidOperationException("Portal threshold generation does not match its capture viewport.");thresholdEpoch=epoch;thresholdGeneration=generation;}
        internal void Invalidate(){thresholdEpoch=-1;thresholdGeneration=-1;}
        internal bool Bind(object pixelStage,PortalCaptureGate capture)
        {
            try
            {
                var cache=srvs.GetValue(pixelStage) as Array;
                if(Threshold==null||thresholdEpoch!=capture.Epoch||thresholdGeneration!=capture.Generation||cache==null||cache.Length!=32){capture.Reject("Portal threshold target, generation or native SRV cache is unavailable.");return false;}
                object current=cache.GetValue(31),view=srv.GetValue(Threshold);
                // No owner-preserving raw SRV setter exists in this renderer.
                // Refuse an occupied slot instead of replacing an unknown view.
                if(current!=null&&!ReferenceEquals(current,view)){capture.Reject("Portal threshold slot is occupied by another resource.");return false;}
                setSrv.Invoke(pixelStage,new[]{(object)31,Threshold});return true;
            }
            catch(Exception ex){capture.Reject("Portal threshold binding failed: "+ex.GetBaseException().Message);return false;}
        }
        internal void Unbind(object pixelStage)
        {
            var cache=srvs.GetValue(pixelStage) as Array;
            if(Threshold!=null&&cache!=null&&cache.Length==32&&ReferenceEquals(cache.GetValue(31),srv.GetValue(Threshold)))setSrv.Invoke(pixelStage,new object[]{31,null});
        }
        internal bool IsBound(object pixelStage,PortalCaptureGate capture)
        {
            try{var cache=srvs.GetValue(pixelStage) as Array;return Threshold!=null&&thresholdEpoch==capture.Epoch&&thresholdGeneration==capture.Generation&&cache!=null&&cache.Length==32&&ReferenceEquals(cache.GetValue(31),srv.GetValue(Threshold));}
            catch{return false;}
        }
        internal void DestroyThreshold()
        {
            thresholdEpoch=-1;thresholdGeneration=-1;if(Threshold==null)return;var args=new[]{Threshold};dispose.Invoke(manager.GetValue(null),args);Threshold=null;Width=Height=0;
        }
    }
}
