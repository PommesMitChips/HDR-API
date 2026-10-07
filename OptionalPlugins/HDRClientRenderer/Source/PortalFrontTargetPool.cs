using System;
namespace HDRClientRenderer
{
    // Transparent-material names outlive provider leases and queued billboards.
    // These bounded manager-owned wrappers therefore survive cancellation, resize,
    // world/provider disposal and backend replacement. Retirement changes pixels
    // only; the renderer's own texture/device shutdown owns final destruction.
    internal sealed class PortalFrontTargetPool
    {
        internal const int Size=512,Count=2,NativeBucketCount=4,TotalTargets=10;
        sealed class Entry{internal object Target;internal string Stamp,Name;}
        readonly Entry[] entries=new Entry[TotalTargets];readonly Func<string,object> lookup,create;
        readonly Func<object,int,bool> compatible;readonly Func<object,bool> live;readonly Action<object,int> activate;readonly Action<object> clear;
        readonly Func<object,string> getStamp;readonly Action<object,string> setStamp;
        readonly string owner=Guid.NewGuid().ToString("N");long serial;
        internal PortalFrontTargetPool(Func<string,object> lookup,Func<string,object> create,Func<object,bool> compatible,Func<object,bool> live,Action<object> activate,
            Func<object,string> getStamp,Action<object,string> setStamp,Action<object> clear)
            :this(lookup,create,(t,s)=>compatible(t),live,(t,s)=>activate(t),getStamp,setStamp,clear){}
        internal PortalFrontTargetPool(Func<string,object> lookup,Func<string,object> create,Func<object,int,bool> compatible,Func<object,bool> live,Action<object,int> activate,
            Func<object,string> getStamp,Action<object,string> setStamp,Action<object> clear)
        {this.lookup=lookup;this.create=create;this.compatible=compatible;this.live=live;this.activate=activate;this.getStamp=getStamp;this.setStamp=setStamp;this.clear=clear;}
        internal static int Slot(string name)
        {
            if(name=="HDR_ClientPortal_0")return 0;if(name=="HDR_ClientPortal_1")return 1;
            for(int slot=0;slot<Count;slot++)for(int bucket=0;bucket<NativeBucketCount;bucket++)if(name=="HDR_ClientPortal_"+slot+"_N"+(256<<bucket))return Count+slot*NativeBucketCount+bucket;return -1;
        }
        internal static bool IsNative(string name){return Slot(name)>=Count;}
        internal static int PhysicalSize(string name){int slot=Slot(name);return slot<0?0:slot<Count?Size:256<<((slot-Count)%NativeBucketCount);}
        internal object Acquire(string name)
        {
            int index=Slot(name);if(index<0)throw new ArgumentException("Unknown public portal front target.");
            int size=PhysicalSize(name);object target=lookup(name)??create(name);if(target==null||!compatible(target,size))throw new InvalidOperationException("Portal public front target must remain a fixed compatible size bucket.");
            // No Activate/Reset touches an already live SRV: GatherInternal may
            // retain this wrapper through paused or delayed billboard batches.
            bool newView=!live(target);if(newView){activate(target,size);if(!live(target))throw new InvalidOperationException("Portal front device reactivation produced no live views.");}
            var previous=entries[index];bool adopted=previous==null||!ReferenceEquals(previous.Target,target)||getStamp(target)!=previous.Stamp;
            string stamp="HDR.Portal.Front."+owner+"."+(++serial);setStamp(target,stamp);entries[index]=new Entry{Target=target,Stamp=stamp,Name=name};
            if(adopted||newView)clear(target);return target;
        }
        internal void Retire(string name)
        {
            CaptureRetirement(name)();
        }
        internal Action CaptureRetirement(string name)
        {
            int index=Slot(name);var entry=index<0?null:entries[index];
            // The ownership stamp is on the live SRV, not an assembly-static
            // dictionary: a new backend can adopt the same registered wrapper
            // even across DLL reloads. Delayed older cleanup then cannot clear
            // its successor's published frame.
            return ()=>{if(entry==null||!ReferenceEquals(lookup(name),entry.Target)||!live(entry.Target)||getStamp(entry.Target)!=entry.Stamp)return;clear(entry.Target);};
        }
        internal string OwnershipToken(string name){int index=Slot(name);return index<0||entries[index]==null?null:entries[index].Stamp;}
        internal bool OwnsAcquisition(string name,string stamp)
        {
            int index=Slot(name);var entry=index<0?null:entries[index];return entry!=null&&entry.Stamp==stamp&&ReferenceEquals(lookup(name),entry.Target)&&live(entry.Target)&&getStamp(entry.Target)==stamp;
        }
        internal bool OwnsTarget(object target){foreach(var entry in entries)if(entry!=null&&ReferenceEquals(entry.Target,target)&&OwnsAcquisition(entry.Name,entry.Stamp))return true;return false;}
        internal void RetireAll(){foreach(var entry in entries)if(entry!=null)Retire(entry.Name);}
    }
}
