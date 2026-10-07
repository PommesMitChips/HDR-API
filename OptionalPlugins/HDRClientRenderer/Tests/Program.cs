using System;
using System.Collections.Generic;
using HDRClientRenderer;

sealed class FakeBackend : RasterStore.IBackend
{
    internal readonly List<string> Calls = new List<string>();
    public void Create(string name, int w, int h, byte[] data,long serial) { Calls.Add("create:" + name + ":" + w + "x" + h); }
    public void Reset(string name,int width,int height,byte[] data,long serial) { Calls.Add("reset:" + name); }
    public void Destroy(string name) { Calls.Add("destroy:" + name); }
}
sealed class FakeLcdWorld : LcdStore.IWorld
{
    internal bool ActiveSource = true, Access = true, HasTexture = true;
    internal string Texture = "LCD_70_0";
    internal int Width = 512, Height = 256;
    internal int Targets, Copies, Destroys;
    internal bool Ready=true;
    public bool Active(long anchor, long caller, string sourceId)
    { return ActiveSource && anchor == 10 && caller == 20 && sourceId == "70:0"; }
    public bool Authorized(long anchor, long caller, long source)
    { return Access && anchor == 10 && caller == 20 && source == 70; }
    public bool TryTexture(long source, int index, out string texture, out int width, out int height)
    {
        texture = Texture; width = Width; height = Height;
        return HasTexture && source == 70 && index == 0;
    }
    public void CreateTarget(string target, int width, int height) { Targets++; }
    public bool TargetReady(string target){return Ready;}
    public void CopyTexture(string source, string target, int width, int height) { Copies++; }
    public void DestroyTarget(string target) { Destroys++; }
}
sealed class CyclingLcdWorld : LcdStore.IWorld
{
    internal readonly HashSet<string> ActiveSources = new HashSet<string>();
    public bool Active(long anchor, long caller, string sourceId)
    { return ActiveSources.Contains(sourceId); }
    public bool Authorized(long anchor, long caller, long source) { return true; }
    public bool TryTexture(long source, int index, out string texture, out int width, out int height)
    { texture = "LCD_" + source; width = 128; height = 128; return true; }
    public void CreateTarget(string target, int width, int height) { }
    public bool TargetReady(string target){return true;}
    public void CopyTexture(string source, string target, int width, int height) { }
    public void DestroyTarget(string target) { }
}
static class Program
{
    static int assertions;
    static void Check(bool condition, string description)
    { if (!condition) throw new Exception(description); assertions++; }
    static void Main()
    {
        var backend = new FakeBackend(); var store = new RasterStore(backend);
        store.NewEpoch(); store.Tick();
        byte[] pixels = new byte[2048 * 256 * 4];
        Check(store.Upload("bad", 1, 2049, 1, pixels) == null, "width ceiling");
        Check(store.Upload("bad", 1, 2048, 513, pixels) == null, "pixel ceiling");
        Check(store.Upload("bad", 1, 2048, 256, new byte[3]) == null, "exact RGBA size");
        Check(store.Upload(new string('x', 129), 1, 1, 1, new byte[4]) == null, "key ceiling");
        var first = store.Upload("a", 1, 2048, 256, pixels);
        Check(first != null && store.Valid(first.Lease), "queued first lease");
        Check(first.Material == "HDR_ClientRaster_0" && first.Texture == "HDR_ClientRaster_0", "fixed material target");
        pixels[0] = 255;
        Check(first.Data[0] == 0, "caller cannot mutate queued bytes");
        Check(ReferenceEquals(first, store.Upload("a", 1, 2048, 256, pixels)), "same serial idempotent");
        Check(store.Upload("a", 0, 2048, 256, pixels) == null, "stale serial");
        var second = store.Upload("a", 2, 2048, 256, pixels);
        Check(second != null && store.Valid(second.Lease) && !store.Valid(first.Lease), "replacement invalidates prior lease");
        Check(backend.Calls.Exists(c => c == "reset:HDR_ClientRaster_0"), "same-size reset queued");
        Check(store.Upload("b", 1, 1, 1, new byte[4]) == null, "per tick byte budget");
        store.Tick();
        Check(store.Upload("b", 1, 1, 1, new byte[4]) != null, "new tick admits work");
        var resized = store.Upload("a", 3, 4, 4, new byte[64]);
        Check(resized != null && !store.Valid(second.Lease), "resize invalidates prior lease");
        Check(backend.Calls.Exists(c => c == "destroy:HDR_ClientRaster_0"), "resize destroys old texture first");
        Check(store.Release(resized.Lease) && !store.Valid(resized.Lease), "release closes lease");
        Check(!store.Release(resized.Lease), "release idempotent false");
        store.Tick();
        for (int i = 0; i < 15; i++) Check(store.Upload("slot" + i, 1, 1, 1, new byte[4]) != null, "free slot " + i);
        Check(store.Upload("overflow", 1, 1, 1, new byte[4]) == null, "16 slot cap");
        var survivor = store.Upload("slot0", 2, 1, 1, new byte[4]);
        Check(survivor != null, "replacement at capacity");
        store.NewEpoch();
        Check(store.ResidentBytes == 0 && !store.Valid(survivor.Lease), "world epoch releases all resources");
        Check(store.Upload("new", 1, 1, 1, new byte[4]) != null, "new epoch admits work");
        var straight = new byte[] { 255, 100, 0, 128 };
        var blended = store.Upload("blend", 1, 1, 1, straight);
        Check(blended != null && blended.Data[0] == 188 && blended.Data[3] == 128,
            "sRGB RGBA is premultiplied in linear light");
        Check(straight[0] == 255 && straight[1] == 100, "premultiplication preserves caller buffer");
        var busyStore = new RasterStore(new FakeBackend());
        busyStore.NewEpoch(); busyStore.Tick();
        var busyLease = busyStore.Upload("same", 1, 2048, 256, new byte[2048 * 256 * 4]);
        Check(busyLease != null, "initial large upload admitted");
        Check(busyStore.Upload("other", 1, 2048, 256, new byte[2048 * 256 * 4]) != null,
            "second large upload fills tick budget");
        Check(busyStore.Upload("same", 2, 2048, 256, new byte[2048 * 256 * 4]) == null &&
            !busyStore.Valid(busyLease.Lease), "newer busy serial revokes stale image");
        var lcdWorld = new FakeLcdWorld(); var lcd = new LcdStore(lcdWorld);
        var warmingWorld=new FakeLcdWorld{Ready=false};var warming=new LcdStore(warmingWorld);
        Check(warming.Acquire(10,20,"70:0",70,0,256,144)==null&&warmingWorld.Targets==1,"LCD warming target never publishes undefined pixels");
        warming.Prune();warming.Tick();
        Check(warming.Acquire(10,20,"70:0",70,0,256,144)==null&&warmingWorld.Targets==1&&warmingWorld.Destroys==0,"LCD warming target survives retry without repeated allocations");
        warmingWorld.Ready=true;warming.Tick();
        Check(warming.Acquire(10,20,"70:0",70,0,256,144)!=null&&warmingWorld.Targets==1,"LCD publishes only after successful render completion");
        lcd.NewEpoch();
        var lcdLease = lcd.Acquire(10, 20, "70:0", 70, 0, 256, 144);
        Check(lcdLease != null && lcd.Valid(lcdLease, 10, 20, "70:0"), "live LCD source");
        Check(lcdWorld.Targets == 1 && lcdWorld.Copies == 1 && lcdLease.Material == "HDR_ClientLcd_0", "fixed LCD target created and copied");
        Check(ReferenceEquals(lcdLease, lcd.Acquire(10, 20, "70:0", 70, 0, 256, 144)), "LCD frame reuses lease");
        Check(lcdWorld.Copies == 2, "live LCD refresh copies GPU source");
        Check(!lcd.Valid(lcdLease, 11, 20, "70:0"), "anchor evidence bound");
        Check(!lcd.Valid(lcdLease, 10, 20, "71:0"), "source ID evidence bound");
        lcdWorld.Access = false;
        Check(!lcd.Valid(lcdLease, 10, 20, "70:0"), "permission loss revokes lease");
        lcdWorld.Access = true;
        Check(!lcd.Valid(lcdLease, 10, 20, "70:0"), "old lease cannot revive");
        Check(lcdWorld.Destroys == 1, "source revocation releases owned target");
        var restored = lcd.Acquire(10, 20, "70:0", 70, 0, 256, 144);
        Check(restored != null && !ReferenceEquals(restored, lcdLease), "restored source gets new lease");
        lcdWorld.ActiveSource = false;
        lcd.Prune();
        Check(!lcd.Valid(restored, 10, 20, "70:0"), "source switch retires slot");
        Check(lcd.Acquire(10, 20, "70:0", 70, 0, 256, 144) == null, "inactive source cannot allocate");
        lcdWorld.ActiveSource = true;
        restored = lcd.Acquire(10, 20, "70:0", 70, 0, 256, 144);
        Check(restored != null, "source reattach allocates slot");
        lcdWorld.Texture = "LCD_70_0_new";
        Check(!lcd.Valid(restored, 10, 20, "70:0"), "texture replacement revokes lease");
        lcd.Tick();
        var newTexture = lcd.Acquire(10, 20, "70:0", 70, 0, 256, 144);
        lcdWorld.Width = 1024;
        Check(!lcd.Valid(newTexture, 10, 20, "70:0"), "source texture resize revokes lease");
        var newSize = lcd.Acquire(10, 20, "70:0", 70, 0, 256, 144);
        lcdWorld.HasTexture = false;
        Check(!lcd.Valid(newSize, 10, 20, "70:0"), "removed texture revokes lease");
        lcdWorld.HasTexture = true;
        var worldLease = lcd.Acquire(10, 20, "70:0", 70, 0, 256, 144);
        Check(lcd.Release(worldLease) && !lcd.Valid(worldLease, 10, 20, "70:0"), "explicit LCD release");
        worldLease = lcd.Acquire(10, 20, "70:0", 70, 0, 256, 144);
        lcd.NewEpoch();
        Check(!lcd.Valid(worldLease, 10, 20, "70:0"), "world transition revokes LCD lease");
        var cyclingWorld = new CyclingLcdWorld(); var cycling = new LcdStore(cyclingWorld);
        cycling.NewEpoch();
        for (int i = 0; i < 16; i++)
        {
            var id = (100 + i) + ":0"; cyclingWorld.ActiveSources.Add(id);
            cycling.Tick();
            Check(cycling.Acquire(10, 20, id, 100 + i, 0, 256, 144) != null, "fill LCD slot " + i);
        }
        cyclingWorld.ActiveSources.Add("116:0");
        cycling.Tick();
        Check(cycling.Acquire(10, 20, "116:0", 116, 0, 256, 144) == null, "all 16 active LCDs occupy slots");
        cyclingWorld.ActiveSources.Remove("100:0");
        Check(cycling.Acquire(10, 20, "116:0", 116, 0, 256, 144) != null,
            "source switch retires stale slot before same-tick allocation");
        var budgetWorld = new CyclingLcdWorld(); var budget = new LcdStore(budgetWorld);
        budget.NewEpoch(); budget.Tick();
        for (int i = 0; i < 5; i++) budgetWorld.ActiveSources.Add((200 + i) + ":0");
        for (int i = 0; i < 4; i++) Check(budget.Acquire(10, 20, (200 + i) + ":0",
            200 + i, 0, 256, 144) != null, "bounded LCD copy " + i);
        Check(budget.Acquire(10, 20, "204:0", 204, 0, 256, 144) == null,
            "fifth LCD copy rejected in one update");
        budget.Tick();
        Check(budget.Acquire(10, 20, "204:0", 204, 0, 256, 144) != null,
            "new update admits LCD copy");
        Check(budget.Acquire(10, 20, "204:0", 204, 0, 1024, 1024) == null,
            "LCD target pixel ceiling");
        var dualWorld = new FakeLcdWorld(); var dual = new LcdStore(dualWorld);
        dual.NewEpoch(); dual.Tick();
        var small = dual.Acquire(10, 20, "70:0", 70, 0, 256, 144);
        var large = dual.Acquire(10, 20, "70:0", 70, 0, 512, 288);
        Check(small != null && large != null && small.Material != large.Material,
            "two output sizes use distinct fixed targets");
        Check(dual.Valid(small, 10, 20, "70:0") && dual.Valid(large, 10, 20, "70:0"),
            "resized peer leaves both leases live");
        Check(dual.Release(small) && dual.Valid(large, 10, 20, "70:0"),
            "releasing one resolution preserves peer target");
        var screensWorld = new FakeLcdWorld(); var screens = new LcdStore(screensWorld);
        screens.NewEpoch(); screens.Tick();
        var leftScreen = screens.Acquire(10, 20, "70:0", "left", 70, 0, 256, 144);
        var rightScreen = screens.Acquire(10, 20, "70:0", "right", 70, 0, 256, 144);
        Check(leftScreen != null && rightScreen != null && leftScreen.Material != rightScreen.Material,
            "same source and size on two screens has separate ownership");
        Check(screens.Release(leftScreen) && screens.Valid(rightScreen, 10, 20, "70:0"),
            "detaching one screen preserves peer evidence and target");
        var residentStore = new RasterStore(new FakeBackend());
        byte[] fullImage = new byte[1024 * 1024 * 4];
        for(int i=0;i<4;i++)
        {
            residentStore.Tick();
            Check(residentStore.Upload("full"+i,1,1024,1024,fullImage)!=null,"one-megapixel upload "+i);
        }
        residentStore.Tick();
        Check(residentStore.ResidentBytes==16*1024*1024&&
            residentStore.Upload("excess",1,1,1,new byte[4])==null,
            "larger textures retain the original sixteen-MiB resident budget");
        int boundedWidth,boundedHeight;
        Check(LcdStore.BoundSize(2048,1024,out boundedWidth,out boundedHeight)&&
            boundedWidth==724&&boundedHeight==362,"camera copies keep their previous area ceiling");
        Check(LcdStore.BoundSize(256,256,out boundedWidth,out boundedHeight)&&
            boundedWidth==256&&boundedHeight==256,"small camera requests unchanged");
        Check(!LcdStore.BoundSize(4096,16,out boundedWidth,out boundedHeight),"camera aspect abuse rejected");
        Check(CaptureIsolation.IsHdrMaterial("HDR_ClientPortal_0")&&CaptureIsolation.IsHdrMaterial("HDR_ClientPortal_1"),"direct and shell captures exclude their own portal display materials");
        var original=new List<string>{"Smoke", "HDR_ClientLcd_0", null, "HoloMap_Line", "HDR_ClientRaster_2", "ShipFlare"};
        var mainView=CaptureIsolation.Filter(original,false,name=>name);
        Check(ReferenceEquals(mainView,original),"player view receives the complete original list");
        var captureView=CaptureIsolation.Filter(original,true,name=>name);
        Check(captureView.Count==3&&captureView[0]=="Smoke"&&captureView[1]==null&&captureView[2]=="ShipFlare",
            "capture excludes HDR screens while preserving unrelated billboards and order");
        Check(original.Count==6&&original[1]=="HDR_ClientLcd_0","capture never mutates main-view input");
        var unrelated=new List<string>{"Particles", "OtherMod_Display"};
        Check(ReferenceEquals(CaptureIsolation.Filter(unrelated,true,name=>name),unrelated),
            "capture without HDR incurs no list copy");
        Check(CaptureIsolation.Filter(new List<string>{"HoloMap_Fill"},true,name=>name).Count==0,
            "a capture consisting only of HDR has an empty list");
        var copies=new TextureCopyQueue();var copied=new List<string>();var failures=new List<string>();
        Check(!copies.Enqueue("source","OtherTexture",256,256),"copy queue only accepts owned fixed targets");
        Check(!copies.Enqueue("source","HDR_ClientLcd_0",1024,1024),"GPU copy queue enforces area budget");
        for(int i=0;i<6;i++)Check(copies.Enqueue("source"+i,"HDR_ClientLcd_"+i,256,256),"bounded pending copy "+i);
        copies.Drain(request=>{copied.Add(request.Source);return true;},failures.Add);
        Check(copied.Count==4&&copies.Count==2,"at most four GPU copies per render frame");
        copies.Drain(request=>{copied.Add(request.Source);return true;},failures.Add);
        Check(copied.Count==6&&copies.Count==0,"remaining copies make progress next frame");
        copies.Enqueue("old","HDR_ClientLcd_0",256,256);copies.Cancel("HDR_ClientLcd_0");
        copies.Enqueue("new","HDR_ClientLcd_0",256,256);
        copies.Drain(request=>{copied.Add(request.Source);return true;},failures.Add);
        Check(copied[copied.Count-1]=="new"&&!copied.Contains("old"),"retired source cannot write into a reused target");
        copies.Enqueue("large0","HDR_ClientLcd_0",1024,256);
        copies.Enqueue("large1","HDR_ClientLcd_1",1024,256);
        copies.Enqueue("large2","HDR_ClientLcd_2",1024,256);
        int before=copied.Count;
        copies.Drain(request=>{copied.Add(request.Source);return true;},failures.Add);
        Check(copied.Count-before==2&&copies.Count==1,"GPU copy output stays within two MiB per frame");
        copies.Clear();
        for(int i=0;i<120;i++)
        {copies.Enqueue("not-ready","HDR_ClientLcd_0",256,256);copies.Drain(request=>false,failures.Add);}
        Check(copies.Count==0&&failures.Count==1,"permanent resource unavailability expires even when requests refresh");
        copies.Enqueue("fault","HDR_ClientLcd_0",256,256);
        copies.Drain(request=>{throw new InvalidOperationException("test fault");},failures.Add);
        Check(copies.Count==0&&failures.Count==2,"GPU copy faults retire work without escaping render callback");
        var cube=new CubeCaptureGuard();string issue;
        Check(cube.CanRelay(70,0,"Normal Camera",out issue)&&issue==null,"ordinary camera LCDs do not require the 360 addon");
        Check(!cube.CanRelay(70,0,"0:360 Camera\nCamera360.Face=4",out issue)&&issue.Contains("not registered"),"cube selector cannot masquerade as a captured direction without an addon");
        long queriedLcd=0;int queriedFace=-1;bool readyFace=true;
        Func<long,int,bool> firstVerifier=(id,face)=>{queriedLcd=id;queriedFace=face;return readyFace;};cube.Register(firstVerifier);
        Check(cube.CanRelay(70,0,"Camera360.Face=up",out issue)&&queriedLcd==70&&queriedFace==4,"relay verifies the LCD identity and physical cube face");
        readyFace=false;
        Check(!cube.CanRelay(70,0,"Camera360.Face=4",out issue)&&issue.Contains("completed"),"stopped or incomplete capture cannot validate existing sky pixels");
        Check(!cube.CanRelay(70,1,"Camera360.Face=4",out issue),"cube producer only claims its supported LCD surface");
        Check(!cube.CanRelay(70,0,"Camera360.Face=6",out issue)&&!cube.CanRelay(70,0,"Camera360.Face=nonsense",out issue),"invalid selectors fail closed");
        Func<long,int,bool> faulty=(id,face)=>{throw new Exception("offline fake");};cube.Register(faulty);
        Check(!cube.CanRelay(70,0,"Camera360.Face=0",out issue)&&issue.Contains("could not validate"),"addon query failures do not escape the display frame");
        Func<long,int,bool> secondVerifier=(id,face)=>true;cube.Register(secondVerifier);cube.Unregister(firstVerifier);
        Check(cube.CanRelay(70,0,"Camera360.Face=5",out issue),"retired addon endpoint cannot unregister a replacement");
        cube.Unregister(secondVerifier);
        Check(!cube.CanRelay(70,0,"Camera360.Face=0",out issue),"addon disposal revokes a camera relay");
        cube.Register(secondVerifier);cube.Clear();
        Check(!cube.CanRelay(70,0,"Camera360.Face=0",out issue),"world change drops the previous world's capture verifier");
        int panoramaAssertions = PanoramaTests.Run();
        Console.WriteLine(ClientRenderSettingsTests.Run()+" client render settings/retained-image assertions passed.");
        DirectCameraCapturePolicyTests.Run(Check);
        ClientPixelBudgetTests.Run(Check);
        Console.WriteLine(panoramaAssertions + " direct camera panorama policy assertions passed.");
        Console.WriteLine(assertions + " raster/LCD/capture policy assertions passed.");
    }
}
