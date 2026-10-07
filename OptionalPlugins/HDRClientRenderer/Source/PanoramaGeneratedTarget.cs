using System;
using System.Reflection;

namespace HDRClientRenderer
{
    // Fixed trusted engine adapter shared by panorama and LCD targets. Descriptor
    // creation does not allocate; null-data activation avoids the engine's unsafe
    // base-only CPU buffer walk across a complete mip chain.
    internal sealed class PanoramaGeneratedTarget
    {
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly FieldInfo description,mipLevels;
        readonly FieldInfo widthField,heightField,format,arraySize,usage,bindFlags,options,samples,sampleCount,sampleQuality;
        readonly MethodInfo reset;
        internal PanoramaGeneratedTarget(Assembly assembly)
        {
            var generated=assembly.GetType("VRage.Render11.Resources.Internal.MyGeneratedTexture",true);
            var user=assembly.GetType("VRage.Render11.Resources.Internal.MyUserGeneratedTexture",true);
            description=generated.GetField("m_desc",Instance);
            mipLevels=description==null?null:description.FieldType.GetField("MipLevels",Instance);
            if(description!=null)
            {
                var type=description.FieldType;widthField=type.GetField("Width");heightField=type.GetField("Height");format=type.GetField("Format");
                arraySize=type.GetField("ArraySize");usage=type.GetField("Usage");bindFlags=type.GetField("BindFlags");options=type.GetField("OptionFlags");samples=type.GetField("SampleDescription");
                if(samples!=null){sampleCount=samples.FieldType.GetField("Count");sampleQuality=samples.FieldType.GetField("Quality");}
            }
            reset=user.GetMethod("Reset",Instance,null,new[]{typeof(byte[])},null);
            if(description==null||mipLevels==null||reset==null||widthField==null||heightField==null||format==null||arraySize==null||usage==null||bindFlags==null||options==null||samples==null||sampleCount==null||sampleQuality==null)
                throw new InvalidOperationException("Generated target null-data activation ABI changed.");
        }
        internal static int LegalMipLevels(int width,int height)
        {
            if(width<=0||height<=0||width>8192||height>8192)throw new ArgumentException("Invalid generated target size.");
            int levels=1;for(int size=Math.Max(width,height);size>1;size>>=1)levels++;
            return levels;
        }
        internal bool Matches(object target,int width,int height)
        {
            if(target==null)return false;
            object descriptor=description.GetValue(target),sample=samples.GetValue(descriptor);
            return (int)widthField.GetValue(descriptor)==width&&(int)heightField.GetValue(descriptor)==height&&
                Convert.ToInt32(format.GetValue(descriptor))==29&&(int)arraySize.GetValue(descriptor)==1&&Convert.ToInt32(usage.GetValue(descriptor))==0&&
                Convert.ToInt32(bindFlags.GetValue(descriptor))==40&&(Convert.ToInt32(options.GetValue(descriptor))&1)!=0&&
                (int)sampleCount.GetValue(sample)==1&&(int)sampleQuality.GetValue(sample)==0;
        }
        internal bool Activate(object target,int width,int height)
        {
            if(!Matches(target,width,height))return false;
            object descriptor=description.GetValue(target);
            // Installed GetMipLevels rounds upward for non-power-of-two sizes.
            // D3D11 permits only floor(log2(max dimension))+1.
            mipLevels.SetValue(descriptor,LegalMipLevels(width,height));
            description.SetValue(target,descriptor);
            reset.Invoke(target,new object[]{null});
            return true;
        }
    }
}
