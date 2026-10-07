using System;
using System.Reflection;
namespace HDRClientRenderer
{
    // Single-mip RTV/SRV targets for active-rectangle publication. The engine
    // factory's generateMipmaps=true reserves RTV bindings; before its first
    // null-data activation we set one mip and remove GenerateMipMaps options.
    // A live SRV is never reset or resized.
    internal sealed class PortalSingleMipTargetNative
    {
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly FieldInfo description,width,height,mips,format,array,usage,bind,options,samples,count,quality,rtvEnabled;
        readonly PropertyInfo srv,rtv,resource;readonly MethodInfo reset;
        internal PortalSingleMipTargetNative(Assembly assembly)
        {
            Func<string,Type> type=n=>DirectCameraCaptureNative.FindType(assembly,n);var generated=type("VRage.Render11.Resources.Internal.MyGeneratedTexture");var user=type("VRage.Render11.Resources.Internal.MyUserGeneratedTexture");
            description=generated.GetField("m_desc",Instance);rtvEnabled=generated.GetField("m_isGeneratingMipmaps",Instance);if(description==null||rtvEnabled==null)throw new InvalidOperationException("Portal single-mip descriptor ABI changed.");
            var d=description.FieldType;width=d.GetField("Width");height=d.GetField("Height");mips=d.GetField("MipLevels");format=d.GetField("Format");array=d.GetField("ArraySize");usage=d.GetField("Usage");bind=d.GetField("BindFlags");options=d.GetField("OptionFlags");samples=d.GetField("SampleDescription");
            count=samples==null?null:samples.FieldType.GetField("Count");quality=samples==null?null:samples.FieldType.GetField("Quality");
            srv=type("VRage.Render11.Resources.ISrvBindable").GetProperty("Srv",Instance);rtv=type("VRage.Render11.Resources.IRtvBindable").GetProperty("Rtv",Instance);resource=type("VRage.Render11.Resources.IResource").GetProperty("Resource",Instance);reset=user.GetMethod("Reset",Instance,null,new[]{typeof(byte[])},null);
            if(width==null||height==null||mips==null||format==null||array==null||usage==null||bind==null||options==null||count==null||quality==null||srv==null||rtv==null||resource==null||reset==null)throw new InvalidOperationException("Portal single-mip allocation ABI incomplete.");
        }
        internal bool Compatible(object target,int w,int h)
        {
            if(target==null||w<64||h<64||w>2048||h>2048)return false;object d=description.GetValue(target),sample=samples.GetValue(d);
            bool shape=(int)width.GetValue(d)==w&&(int)height.GetValue(d)==h&&Convert.ToInt32(format.GetValue(d))==29&&(int)array.GetValue(d)==1&&Convert.ToInt32(usage.GetValue(d))==0&&Convert.ToInt32(bind.GetValue(d))==40&&(int)count.GetValue(sample)==1&&(int)quality.GetValue(sample)==0&&(bool)rtvEnabled.GetValue(target);
            return shape&&(srv.GetValue(target)==null||Matches(target,w,h));
        }
        internal bool Matches(object target,int w,int h)
        {
            if(target==null)return false;object d=description.GetValue(target);
            return (int)width.GetValue(d)==w&&(int)height.GetValue(d)==h&&(int)mips.GetValue(d)==1&&Convert.ToInt32(options.GetValue(d))==0;
        }
        internal bool Activate(object target,int w,int h)
        {
            if(!Prepare(target,w,h)||srv.GetValue(target)!=null)return false;reset.Invoke(target,new object[]{null});
            return Compatible(target,w,h)&&Matches(target,w,h)&&srv.GetValue(target)!=null&&rtv.GetValue(target)!=null&&resource.GetValue(target)!=null;
        }
        internal bool Prepare(object target,int w,int h)
        {
            if(!Compatible(target,w,h))return false;if(srv.GetValue(target)!=null)return Matches(target,w,h);
            object d=description.GetValue(target);mips.SetValue(d,1);options.SetValue(d,Enum.ToObject(options.FieldType,0));description.SetValue(target,d);return Matches(target,w,h);
        }
    }
}
