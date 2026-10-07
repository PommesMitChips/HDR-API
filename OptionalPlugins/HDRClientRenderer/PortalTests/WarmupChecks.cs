using System.Threading;
using HDRClientRenderer;
internal static class WarmupChecks
{
    internal static void Run(Action<bool,string> check)
    {
        ShaderCacheChecks.Run(check);
        var registry=new PortalClipRegistry();var first=new object();var second=new object();registry.Queue(new PortalClipDescriptor(first,"Geometry/Materials/Standard/Pixel.hlsl","test",null));registry.Queue(new PortalClipDescriptor(second,"Geometry/Materials/Standard/Pixel.hlsl","test",null));
        int main=Environment.CurrentManagedThreadId,freezeThread=0,workerThread=0,live=7,freezes=0;
        using(var started=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var worker=new PortalClipWarmup(registry,d=>
        {freezeThread=Environment.CurrentManagedThreadId;freezes++;int sealedValue=live;return ()=>{workerThread=Environment.CurrentManagedThreadId;started.Set();if(!release.Wait(5000))throw new Exception("CPU fixture timed out");return new byte[]{(byte)sealedValue,0,0,0};};}))
        {
            worker.Schedule();check(started.Wait(5000)&&freezeThread==main&&workerThread!=main,"Shader input freezes on renderer while expensive compilation executes only on CPU worker");
            worker.Schedule();live=99;check(freezes==1&&worker.Busy&&registry.Prepared(first)==null&&registry.Prepared(second)==null,"One in-flight sealed-source job cannot block render or create an unbounded worker backlog");
            release.Set();check(SpinWait.SpinUntil(()=>!worker.Busy,5000)&&registry.Prepared(first).Bytecode[0]==7&&registry.Prepared(second)==null,"Worker uses immutable frozen input and publishes only one completed bounded CPU item");
            worker.Schedule();check(SpinWait.SpinUntil(()=>!worker.Busy,5000)&&registry.Prepared(second).Bytecode[0]==99,"Next renderer schedule sees fresh inputs after previous worker retires");
        }
        registry=new PortalClipRegistry();first=new object();registry.Queue(new PortalClipDescriptor(first,"Geometry/Materials/Standard/Pixel.hlsl","test",null));
        using(var started=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var worker=new PortalClipWarmup(registry,d=>()=>{started.Set();release.Wait(5000);return new byte[4];}))
        {
            worker.Schedule();check(started.Wait(5000),"Epoch retirement fixture has a real in-flight CPU job");registry.NewEpoch();release.Set();
            check(SpinWait.SpinUntil(()=>!worker.Busy,5000)&&registry.Prepared(first)==null&&registry.ResidentBytes==0,"Old device epoch completion cannot publish or attach stale CPU bytecode");
        }
        registry=new PortalClipRegistry();first=new object();registry.Queue(new PortalClipDescriptor(first,"Geometry/Materials/Standard/Pixel.hlsl","test",null));
        using(var started=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
        {
            var worker=new PortalClipWarmup(registry,d=>()=>{started.Set();release.Wait(5000);return new byte[4];});worker.Schedule();check(started.Wait(5000),"Disposal fixture has a real in-flight CPU job");worker.Dispose();release.Set();
            check(SpinWait.SpinUntil(()=>!worker.Busy,5000)&&registry.Prepared(first)==null,"Owner disposal drops worker completion without GPU work or lifecycle callbacks");worker.Schedule();check(!worker.Busy,"Disposed CPU worker never restarts or retains a new task");
        }
        registry=new PortalClipRegistry();first=new object();registry.Queue(new PortalClipDescriptor(first,"Geometry/Materials/Standard/Pixel.hlsl","test",null));
        using(var worker=new PortalClipWarmup(registry,d=>{throw new Exception("sealed source failure");})){worker.Schedule();check(!worker.Busy&&registry.Issue(first).Contains("sealed source failure"),"Render-side source freeze failure retires job and remains fail closed");}
        registry=new PortalClipRegistry();var identities=new[]{new object(),new object(),new object()};foreach(var identity in identities)registry.Queue(new PortalClipDescriptor(identity,"Geometry/Materials/Standard/Pixel.hlsl","parallel",null));
        int owner=Environment.CurrentManagedThreadId,parallelFreezes=0,active=0,peak=0;bool ownerOnly=true,workerOnly=true;
        using(var started=new CountdownEvent(2))using(var release=new ManualResetEventSlim())using(var worker=new PortalClipWarmup(registry,d=>
        {
            ownerOnly&=Environment.CurrentManagedThreadId==owner;parallelFreezes++;
            return ()=>{workerOnly&=Environment.CurrentManagedThreadId!=owner;int count=Interlocked.Increment(ref active);int previous;do{previous=Volatile.Read(ref peak);if(previous>=count)break;}while(Interlocked.CompareExchange(ref peak,count,previous)!=previous);started.Signal();try{if(!release.Wait(5000))throw new Exception("Parallel CPU fixture timeout");return new byte[4];}finally{Interlocked.Decrement(ref active);}};
        },2))
        {
            worker.Schedule();check(parallelFreezes==1,"Each Schedule freezes only one source on the render owner");worker.Schedule();check(started.Wait(5000),"Two bounded CPU lanes run concurrently");worker.Schedule();
            check(parallelFreezes==2&&peak==2&&ownerOnly&&workerOnly&&registry.Prepared(identities[2])==null,"Two lanes preserve owner freeze affinity, worker-only compilation and zero third-job backlog");
            registry.NewEpoch();release.Set();check(SpinWait.SpinUntil(()=>!worker.Busy,5000)&&registry.ResidentBytes==0,"Epoch retirement drops both concurrent completions");
        }
    }
}
