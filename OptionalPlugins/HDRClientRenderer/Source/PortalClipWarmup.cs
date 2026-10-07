using System;
using System.Threading;
namespace HDRClientRenderer
{
    // Bounded CPU jobs. One freeze per Schedule runs on the renderer; only sealed
    // source compilation closure runs on ThreadPool. Epoch/disposal changes
    // discard completion and never create or attach a GPU shader off-thread.
    internal sealed class PortalClipWarmup : IDisposable
    {
        readonly PortalClipRegistry registry;readonly Func<PortalClipDescriptor,Func<byte[]>> freeze;
        readonly int maximum;int busy,disposed,scheduling;
        internal bool Busy{get{return Volatile.Read(ref busy)!=0;}}
        internal PortalClipWarmup(PortalClipRegistry bank,Func<PortalClipDescriptor,Func<byte[]>> freezer,int workers=1){registry=bank??throw new ArgumentNullException("bank");freeze=freezer??throw new ArgumentNullException("freezer");if(workers<1||workers>2)throw new ArgumentOutOfRangeException("workers");maximum=workers;}
        internal void Schedule()
        {
            if(Volatile.Read(ref disposed)!=0||Interlocked.CompareExchange(ref scheduling,1,0)!=0)return;
            try{ScheduleOne();}finally{Volatile.Write(ref scheduling,0);}
        }
        void ScheduleOne()
        {
            if(Volatile.Read(ref disposed)!=0||Volatile.Read(ref busy)>=maximum)return;
            Interlocked.Increment(ref busy);
            var entries=registry.ClaimPending(1);if(entries.Length==0){Interlocked.Decrement(ref busy);return;}var entry=entries[0];Func<byte[]> compile;
            try{compile=freeze(entry.Descriptor);if(compile==null)throw new InvalidOperationException("Portal CPU source freeze returned no compiler.");}
            catch(Exception ex){registry.CompleteCompilation(entry,null,ex.GetBaseException().Message);Interlocked.Decrement(ref busy);return;}
            try
            {
                if(!ThreadPool.QueueUserWorkItem(_=>
                {
                    try
                    {
                        if(Volatile.Read(ref disposed)!=0||entry.Epoch!=registry.Epoch)return;
                        byte[] bytes=null;string failure=null;try{bytes=compile();}catch(Exception ex){failure=ex.GetBaseException().Message;}
                        if(Volatile.Read(ref disposed)==0)registry.CompleteCompilation(entry,bytes,failure);
                    }
                    finally{Interlocked.Decrement(ref busy);}
                }))throw new InvalidOperationException("Portal CPU shader worker could not be scheduled.");
            }
            catch(Exception ex){registry.CompleteCompilation(entry,null,ex.GetBaseException().Message);Interlocked.Decrement(ref busy);}
        }
        public void Dispose(){Interlocked.Exchange(ref disposed,1);}
    }
}
