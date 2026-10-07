using System;
using System.Collections.Generic;
using System.Reflection;
using VRageMath;

namespace HDRClientRenderer
{
    // A capture borrows model buffers/shaders, but owns its proxy objects and LOD
    // selection. No entity, physics block, render tree or asset is created here.
    internal sealed class CaptureModelState
    {
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly FieldInfo lods,proxies,keys,lodProxies,lodKeys,common,alpha,matrixIndex;
        readonly PropertyInfo cullProxy;
        readonly MethodInfo clone=typeof(object).GetMethod("MemberwiseClone",Instance);
        sealed class Saved { internal object Owner,Proxies,Keys;internal int MatrixIndex; }
        readonly Dictionary<object,Saved> saved=new Dictionary<object,Saved>();
        internal CaptureModelState(Assembly assembly)
        {
            var component=DirectCameraCaptureNative.FindType(assembly,"VRage.Render11.Scene.Components.MyRenderableComponent");
            var proxy=DirectCameraCaptureNative.FindType(assembly,"VRage.Render11.Culling.MyCullProxy");
            var lod=DirectCameraCaptureNative.FindType(assembly,"VRageRender.MyRenderLod");
            lods=component.GetField("m_lods",Instance);cullProxy=component.GetProperty("CullProxy",Instance);
            proxies=proxy.GetField("RenderableProxies",Instance);keys=proxy.GetField("SortingKeys",Instance);
            matrixIndex=proxy.GetField("m_worldMatrixIndex",Instance);
            lodProxies=lod.GetField("RenderableProxies",Instance);lodKeys=lod.GetField("SortingKeys",Instance);
            var renderProxy=DirectCameraCaptureNative.FindType(assembly,"VRageRender.MyRenderableProxy");
            common=renderProxy.GetField("CommonObjectData",Instance);alpha=common==null?null:common.FieldType.GetField("CustomAlpha",Instance);
            if(lods==null||cullProxy==null||proxies==null||keys==null||matrixIndex==null||matrixIndex.FieldType!=typeof(int)||lodProxies==null||lodKeys==null||common==null||!common.FieldType.IsValueType||alpha==null||alpha.FieldType!=typeof(Vector2)||clone==null)
                throw new InvalidOperationException("Capture individual-model proxy ABI changed.");
        }
        internal void Prepare(object component)
        {
            var owner=cullProxy.GetValue(component);if(owner==null||saved.ContainsKey(owner))return;
            var levels=lods.GetValue(component) as Array;
            if(levels==null||levels.Length==0||levels.GetValue(0)==null)return;
            var level=levels.GetValue(0);var source=lodProxies.GetValue(level) as Array;var sorting=lodKeys.GetValue(level) as Array;
            if(source==null||sorting==null||source.Length==0||source.Length!=sorting.Length)return;
            var view=Array.CreateInstance(source.GetType().GetElementType(),source.Length);
            for(int i=0;i<source.Length;i++)
            {
                var original=source.GetValue(i);if(original==null)return;
                var copy=clone.Invoke(original,null);var data=common.GetValue(copy);var fade=(Vector2)alpha.GetValue(data);
                // Disable only LOD cross-fade; preserve object transparency/fade.
                alpha.SetValue(data,new Vector2(0,fade.Y));common.SetValue(copy,data);view.SetValue(copy,i);
            }
            saved.Add(owner,new Saved{Owner=owner,Proxies=proxies.GetValue(owner),Keys=keys.GetValue(owner),MatrixIndex=(int)matrixIndex.GetValue(owner)});
            proxies.SetValue(owner,view);keys.SetValue(owner,sorting.Clone());
            // A previously unselected LOD may not have a populated world matrix.
            // Force the normal capture update to initialize rotation/scale too.
            matrixIndex.SetValue(owner,-1);
        }
        internal void Restore()
        {
            var errors=new List<Exception>();
            foreach(var entry in saved.Values)try{proxies.SetValue(entry.Owner,entry.Proxies);keys.SetValue(entry.Owner,entry.Keys);matrixIndex.SetValue(entry.Owner,entry.MatrixIndex);}catch(Exception error){errors.Add(error);}
            saved.Clear();if(errors.Count!=0)throw new AggregateException("Capture model proxy restoration failed.",errors);
        }
    }
}
