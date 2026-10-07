using System;
using System.Collections.Generic;
using System.Reflection;

namespace HDRClientRenderer
{
    // Stage2 uses a value-type LOD strategy, separately from legacy block proxies.
    // Borrow its geometry, but do not borrow the player's selected/fading LOD.
    internal sealed class CaptureInstanceState
    {
        const BindingFlags I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly FieldInfo results,instances,groups;
        readonly FieldInfo[] owners;
        readonly FieldInfo current,transitionLod,transition;
        readonly Dictionary<object,Tuple<FieldInfo,object>> saved=new Dictionary<object,Tuple<FieldInfo,object>>();
        internal CaptureInstanceState(Assembly assembly)
        {
            Func<string,Type> type=n=>DirectCameraCaptureNative.FindType(assembly,n);
            results=type("VRage.Render11.Culling.MyCullQuery").GetField("Results",I);
            instances=results.FieldType.GetField("Instances",I);groups=results.FieldType.GetField("StaticGroups",I);
            owners=new[]{type("VRage.Render11.GeometryStage2.Instancing.MyInstance").GetField("LodStrategy",I),
                type("VRage.Render11.GeometryStage2.StaticGroup.MyStaticGroup").GetField("LodStrategy",I)};
            var strategy=owners[0].FieldType;
            current=strategy.GetField("m_currentLod",I);transitionLod=strategy.GetField("m_transitionLod",I);transition=strategy.GetField("m_transition",I);
            if(instances==null||groups==null||!strategy.IsValueType||owners[1].FieldType!=strategy||current==null||transitionLod==null||transition==null)
                throw new InvalidOperationException("Capture instanced-model LOD ABI changed.");
        }
        internal void Prepare(object query)
        {
            var value=results.GetValue(query);
            PrepareList(instances.GetValue(value),owners[0]);PrepareList(groups.GetValue(value),owners[1]);
        }
        void PrepareList(object list,FieldInfo owner)
        {
            int count=(int)list.GetType().GetProperty("Count",I).GetValue(list);
            var array=(Array)list.GetType().GetMethod("GetInternalArray",I).Invoke(list,null);
            for(int i=0;i<count;i++)
            {
                object item=array.GetValue(i);if(item==null||saved.ContainsKey(item))continue;
                object original=owner.GetValue(item),capture=owner.GetValue(item);
                saved.Add(item,Tuple.Create(owner,original));
                current.SetValue(capture,0);transitionLod.SetValue(capture,0);transition.SetValue(capture,0f);
                // Explicit object visibility/fade and transition callbacks stay intact.
                owner.SetValue(item,capture);
            }
        }
        internal void Restore()
        {
            var failures=new List<Exception>();
            foreach(var entry in saved)try{entry.Value.Item1.SetValue(entry.Key,entry.Value.Item2);}catch(Exception e){failures.Add(e);}
            saved.Clear();if(failures.Count>0)throw new AggregateException("Capture instance LOD restoration failed.",failures);
        }
    }
}
