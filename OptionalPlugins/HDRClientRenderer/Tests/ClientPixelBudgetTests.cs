using System;
using System.Threading;
using System.Threading.Tasks;
using HDRClientRenderer;

internal static class ClientPixelBudgetTests
{
    internal static void Run(Action<bool,string> check)
    {
        long frame=0;var budget=new ClientPixelBudget(()=>new ClientPixelBudget.Frame(frame,10,10));
        for(int kind=0;kind<4;kind++)check(budget.Request("kind"+kind,(ClientPixelBudget.Kind)kind,70),"shared family registration "+kind);
        var grants=new int[4];
        for(frame=0;frame<16;frame++)
        {
            for(int kind=0;kind<4;kind++)if(budget.TrySpend("kind"+kind,(ClientPixelBudget.Kind)kind,70))grants[kind]++;
            check(budget.Spent==70,"all producer families share exactly one viewport occupancy ledger");
        }
        frame--;
        for(int kind=0;kind<4;kind++)check(grants[kind]==4,"round-robin grants prevent warm-up starvation "+kind);
        budget.RecordBandwidth(1000000);check(budget.Spent==70&&budget.BandwidthPixels==1000000,"bandwidth never multiplies primary occupancy allowance");
        check(!budget.TrySpend("kind3",ClientPixelBudget.Kind.Ui,70)&&budget.Spent==70,"same job cannot be rasterized twice in a frame");
        budget.Clear();frame=20;
        check(budget.Request("reserved",ClientPixelBudget.Kind.Panorama,100),"reserve full-frame compositor");
        check(!budget.TrySpend("new",ClientPixelBudget.Kind.Capture,10),"later producer cannot steal reserved compositor pixels");
        check(budget.TrySpend("reserved",ClientPixelBudget.Kind.Panorama,100)&&budget.Spent==100,"reservation remains usable by late compositor hook");
        frame++;check(budget.TrySpend("new",ClientPixelBudget.Kind.Capture,10),"deferred capture progresses on next render frame");
        budget.Cancel("reserved");budget.Cancel("new");check(budget.Pending==0,"retirement drops pending grants");
        budget.Clear();frame=100;
        for(int kind=0;kind<4;kind++)budget.Request("starvation"+kind,(ClientPixelBudget.Kind)kind,kind==1?100:10);
        int largeGrants=0;
        for(;frame<116;frame++)
        {
            for(int kind=0;kind<4;kind++)if(budget.TrySpend("starvation"+kind,(ClientPixelBudget.Kind)kind,kind==1?100:10)&&kind==1)largeGrants++;
            check(budget.Spent<=100,"large compositor and small recurring producers share one strict cap");
        }
        check(largeGrants==4,"full-viewport compositor receives a full turn despite recurring small jobs");
        long largeFrame=1;var large=new ClientPixelBudget(()=>new ClientPixelBudget.Frame(largeFrame,7680,4320));
        check(large.PixelLimit==8294400,"default pixel occupancy never exceeds 4K");
        check(!large.Request("bad",ClientPixelBudget.Kind.Ui,8388609)&&!large.Request("bad",(ClientPixelBudget.Kind)99,1),"invalid and requests beyond the validated 2K capture plus composition bound are rejected");
        check(large.Request("native-2k",ClientPixelBudget.Kind.Capture,8388608)&&!large.TrySpend("native-2k",ClientPixelBudget.Kind.Capture,8388608),"validated native work can register while an unconfigured viewport limit still defers it");
        large.Configure(new ClientRenderSettings());
        largeFrame++; // The changed preference takes effect in the next renderer frame.
        check(large.TrySpend("native-2k",ClientPixelBudget.Kind.Capture,8388608),"explicit unlimited client preference permits bounded native capture and composition work");
        int width,height;
        check(ClientPixelBudget.BoundSize(2048,1024,16,1920*1080,out width,out height)&&width*height<=1920*1080&&width<2048,"oversized panorama is physically reduced before allocation");
        check(!ClientPixelBudget.BoundSize(16,16,16,255,out width,out height),"unrenderable minimum cannot exceed viewport allowance");
        check(ClientPixelBudget.MipPixels(1024,1024)==349525&&ClientPixelBudget.MipPixels(724,362)>0,"mip diagnostics count legal levels separately");
        budget.Clear();frame=0;
        for(int i=0;i<ClientPixelBudget.MaxJobs;i++)check(budget.Request("bounded"+i,ClientPixelBudget.Kind.Capture,1),"bounded work registration "+i);
        check(!budget.Request("overflow",ClientPixelBudget.Kind.Capture,1),"bounded reservation table cannot grow indefinitely");
        budget.Clear();
        var queue=new PanoramaQueue();queue.Enqueue("HDR_ClientPanorama_0",16,16,new[]{PanoramaTests.Forward()},new PanoramaStore.Settings());
        int draws=0,failures=0;
        for(int i=0;i<240;i++)queue.Drain(request=>{draws++;return false;},message=>failures++,null,request=>false);
        check(queue.Count==1&&draws==0&&failures==0,"budget deferral does not consume GPU resource retry lifetime");
        queue.Drain(request=>{draws++;return true;},message=>failures++,null,request=>true);
        check(queue.Count==0&&draws==1&&failures==0,"deferred composition publishes when a grant arrives");
        var copy=new TextureCopyQueue();copy.Enqueue("source","HDR_ClientLcd_0",16,16);
        for(int i=0;i<240;i++)copy.Drain(request=>false,message=>failures++,request=>false);
        check(copy.Count==1&&failures==0,"LCD deferral preserves pending warming target");
        copy.Drain(request=>true,message=>failures++,request=>true);check(copy.Count==0,"LCD copy progresses after shared grant");
        Density(check);
        AtomicRegistration(check);
        RasterPublication(check);
    }
    sealed class AsyncRaster : RasterStore.IBackend,RasterStore.IReadiness
    {
        internal readonly System.Collections.Generic.Dictionary<string,long> Published=new System.Collections.Generic.Dictionary<string,long>();
        internal readonly System.Collections.Generic.Dictionary<string,long> Pending=new System.Collections.Generic.Dictionary<string,long>();
        internal int Destroys;
        public void Create(string target,int width,int height,byte[] data,long serial){Pending[target]=serial;}
        public void Reset(string target,int width,int height,byte[] data,long serial){Create(target,width,height,data,serial);}
        public bool Ready(string target,long serial){long current;return Published.TryGetValue(target,out current)&&current==serial;}
        public void Destroy(string target){Pending.Remove(target);Published.Remove(target);Destroys++;}
        internal void Commit(string target){Published[target]=Pending[target];Pending.Remove(target);}
    }
    static void RasterPublication(Action<bool,string> check)
    {
        var backend=new AsyncRaster();var store=new RasterStore(backend);var first=store.Upload("ui",1,2,2,new byte[16]);
        check(first==null&&store.PendingBytes==16,"first UI upload remains warming until its exact serial completes");
        backend.Commit("HDR_ClientRaster_0");store.Tick();first=store.Upload("ui",1,2,2,new byte[16]);
        check(first!=null&&store.Valid(first.Lease)&&store.PendingBytes==0,"completed UI serial publishes its own lease");
        store.Tick();var deferred=store.Upload("ui",2,2,2,new byte[16]);
        check(ReferenceEquals(deferred,first)&&store.Valid(first.Lease)&&backend.Destroys==0,"shared-budget deferral retains the previous completed UI image");
        store.Tick();var resize=store.Upload("ui",3,4,2,new byte[32]);
        check(ReferenceEquals(resize,first)&&store.Valid(first.Lease)&&backend.Destroys==0&&store.PendingBytes==32,"pending size change coalesces without destroying the published front");
        backend.Commit("HDR_ClientRaster_0");store.Tick();var published=store.Upload("ui",3,4,2,new byte[32]);
        check(published!=null&&published.Serial==3&&published.Width==4&&!store.Valid(first.Lease)&&store.Valid(published.Lease),"only the committed latest serial replaces the prior lease");
        check(store.PendingBytes==0&&store.ResidentBytes==32,"publication releases pending storage and updates reserved resident bytes");
        store.Tick();store.Upload("ui",4,4,2,new byte[32]);store.NewEpoch();
        check(!store.Valid(published.Lease)&&store.PendingBytes==0&&store.ResidentBytes==0&&backend.Pending.Count==0,"world reset cancels pending UI publication and old serial identity");
    }
    static void AtomicRegistration(Action<bool,string> check)
    {
        var budget=new ClientPixelBudget(()=>new ClientPixelBudget.Frame(0,256,256));
        var queue=new PanoramaQueue();const string target="HDR_ClientPanorama_0",key="atomic-panorama";
        var face=PanoramaTests.Forward();var settings=new PanoramaStore.Settings();
        queue.Enqueue(target,16,16,new[]{face},settings,()=>budget.Request(key,ClientPixelBudget.Kind.Panorama,256));
        using(var registering=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var consumerStarted=new ManualResetEventSlim())using(var consumerDone=new ManualResetEventSlim())
        {
            var producer=Task.Run(()=>queue.Enqueue(target,16,16,new[]{face},settings,()=>{registering.Set();release.Wait();return budget.Request(key,ClientPixelBudget.Kind.Panorama,256);}));
            check(registering.Wait(5000),"producer reached reservation commit barrier");
            var consumer=Task.Run(()=>{consumerStarted.Set();queue.Drain(request=>{budget.Complete(key);return true;},message=>throw new Exception(message));consumerDone.Set();});
            try
            {
                check(consumerStarted.Wait(5000),"consumer started during reservation commit");
                check(!consumerDone.Wait(25),"drain cannot run between queue mutation and ledger registration");
            }
            finally{release.Set();}
            check(producer.GetAwaiter().GetResult(),"coalesced producer accepted after atomic registration");consumer.GetAwaiter().GetResult();
            check(queue.Count==0&&budget.Pending==0,"concurrent queue drain leaves no phantom ledger job");
        }
        var copies=new TextureCopyQueue();bool committed=false;
        check(copies.Enqueue("source","HDR_ClientLcd_0",16,16,()=>{committed=budget.Request("atomic-lcd",ClientPixelBudget.Kind.Lcd,256);return committed;}),"LCD registration commits under queue ownership");
        copies.Drain(request=>{budget.Complete("atomic-lcd");return true;},message=>throw new Exception(message));
        check(committed&&copies.Count==0&&budget.Pending==0,"LCD completion removes its owned reservation");
    }
    static void Density(Action<bool,string> check)
    {
        var packed=new double[PanoramaDensity.CellCount*PanoramaDensity.Fields];int index=(11*16+3)*7;
        Array.Copy(new[]{1d,64,4096,32,1024,.75,1},0,packed,index,7);
        var field=PanoramaDensity.FromPacked(packed,256,true);packed[index+1]=250;
        var cell=field.At(3,11);
        check(field.Known&&field.Fresh&&cell.Covered&&cell.HasSample&&cell.UpperMagnification==64&&cell.BlendWeight==.75,"source UV cell metrics survive as detached immutable evidence");
        check(!field.At(4,11).Covered,"hidden source UV cells stay hidden instead of becoming uniform size");
        var other=new double[packed.Length];Array.Copy(new[]{1d,128,16384,64,4096,.5,1},0,other,(1*16+2)*7,7);
        var union=PanoramaDensity.Union(field,PanoramaDensity.FromPacked(other,256,true),128);
        check(union.At(3,11).Covered&&union.At(2,1).Covered&&union.At(2,1).UpperMagnification==128,"all consumers conservatively union source-space tile demand");
        check(!union.At(15,15).Covered,"union does not invent unrelated source coverage");
        var unknown=PanoramaDensity.Union(union,PanoramaDensity.Full(256),256);
        check(!unknown.Known&&!unknown.Fresh&&unknown.At(15,15).Covered&&unknown.At(15,15).UpperMagnification==256,"unknown consumer forces conservative full-camera demand");
        var malformed=(double[])other.Clone();malformed[0]=double.NaN;
        check(!PanoramaDensity.FromPacked(malformed,256,true).Known,"malformed density cells fail to full conservative field");
        malformed=(double[])other.Clone();malformed[6]=2;check(!PanoramaDensity.FromPacked(malformed,256,true).Known,"nonboolean sample evidence rejected");
        check(union.Clamp(64).At(2,1).UpperArea<=4096,"physical uniform ceiling bounds tile density without discarding cells");
    }
}
