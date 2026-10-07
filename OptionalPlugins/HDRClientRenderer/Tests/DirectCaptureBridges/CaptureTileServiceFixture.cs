using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VRageMath;

static class CaptureTileServiceFixture
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static long Number;
    public static int Width = 640, Height = 480;
    static readonly List<MatrixD> captured = new();
    static readonly List<int> logicalSizes = new();
    static int allocations, publications, copies;
    static bool failPublish, failCopy;
    static Action<bool, string> check;
    static object New(Type t, params object[] args) => Activator.CreateInstance(t, Instance, null, args, null);
    static bool False(ref bool __result) { __result = false; return false; }
    static bool True(ref bool __result) { __result = true; return false; }
    static bool Frame(ref long __result) { __result = Number; return false; }
    static bool Bound(ref int __result) { __result = 1024; return false; }
    static bool Allocate(ref object __result) { allocations++; __result = new object(); return false; }
    static bool NoGpu() => false;
    static bool Copy() { copies++;if(failCopy)throw new InvalidOperationException("Injected staging-only crop failure.");return false; }
    static bool Publish() { publications++; if (failPublish) throw new InvalidOperationException("Injected atomic atlas copy failure."); return false; }
    static bool Draw(int __1, object __2, int __4, object __5, bool __6, ref bool __result)
    { check(__5 != null && !__6 && (__4 == 0 || __4 == 1), "Actual adaptive job carries cropped geometry, validated profile and defers scratch mips."); captured.Add((MatrixD)__2); logicalSizes.Add(__1); __result = true; return false; }
    static void Patch(Harmony h, Type type, string method, string prefix, bool property = false)
    { var original = property ? type.GetProperty(method, Instance).GetGetMethod(true) : type.GetMethod(method, Instance);
        h.Patch(original, prefix: new HarmonyMethod(typeof(CaptureTileServiceFixture).GetMethod(prefix, Static))); }
    static object Budget(Assembly plugin)
    {
        var type = plugin.GetType("HDRClientRenderer.ClientPixelBudget", true);
        var frame = type.GetNestedType("Frame", BindingFlags.NonPublic);
        var make = new DynamicMethod("HDRFixtureTileBudgetFrame", frame, Type.EmptyTypes, typeof(CaptureTileServiceFixture).Module, true);
        var il = make.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, typeof(CaptureTileServiceFixture).GetField(nameof(Number), Static));
        il.Emit(OpCodes.Ldsfld, typeof(CaptureTileServiceFixture).GetField(nameof(Width), Static));
        il.Emit(OpCodes.Ldsfld, typeof(CaptureTileServiceFixture).GetField(nameof(Height), Static));
        il.Emit(OpCodes.Newobj, frame.GetConstructor(Instance, null, new[] { typeof(long), typeof(int), typeof(int) }, null)); il.Emit(OpCodes.Ret);
        return New(type, make.CreateDelegate(typeof(Func<>).MakeGenericType(frame)));
    }
    internal static void Run(Assembly plugin, object atlas, Action<bool, string> assert)
    {
        check = assert;
        var serviceType = plugin.GetType("HDRClientRenderer.DirectCameraCapture", true);
        var nativeType = plugin.GetType("HDRClientRenderer.DirectCameraCaptureNative", true);
        var renderer = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRageRender.MyRender11", false)).First(t => t != null);
        object service = New(serviceType), native = New(nativeType, renderer.Assembly), budget = Budget(plugin);
        var options=New(plugin.GetType("HDRClientRenderer.ClientRenderSettings",true));
        options.GetType().GetField("CapturePasses").SetValue(options,1);
        serviceType.GetMethod("Configure",Instance).Invoke(service,new[]{options});
        var h = new Harmony("HDR.Tests.AtomicCaptureTileService");
        try
        {
            foreach (var entry in new[] { ("Frame", nameof(Frame), true), ("FrameIdle", nameof(True), true), ("DeviceChanged", nameof(False), true),
                ("BoundResolution", nameof(Bound), false), ("CreateTarget", nameof(Allocate), false), ("CreateAtlasTarget", nameof(Allocate), false),
                ("ClearAtlas", nameof(NoGpu), false), ("CopyTile", nameof(Copy), false), ("PublishAtlas", nameof(Publish), false), ("Capture", nameof(Draw), false) })
                Patch(h, nativeType, entry.Item1, entry.Item2, entry.Item3);
            serviceType.GetField("native", Instance).SetValue(service, native);
            serviceType.GetField("renderedEpoch", Instance).SetValue(service, serviceType.GetField("epoch", Instance).GetValue(service));
            serviceType.GetMethod("SetBudget", Instance).Invoke(service, new[] { budget });
            var densityType = plugin.GetType("HDRClientRenderer.PanoramaDensity", true);
            var packed = new double[1792];
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            { int at = (y * 16 + x) * 7; double value = new[] { 1024d, 200d, 100d, 64d }[(y / 8) * 2 + x / 8];
                packed[at] = packed[at + 6] = 1; packed[at + 1] = packed[at + 3] = value; packed[at + 2] = packed[at + 4] = value * value; packed[at + 5] = 1; }
            object density = densityType.GetMethod("FromPacked", Static).Invoke(null, new object[] { packed, 1024, true });
            object identity = new object();
            MatrixD initial = MatrixD.CreateTranslation(1, 2, 3);
            var request = serviceType.GetMethod("Request", Instance);
            Action<MatrixD, int> demand = (pose, profile) => request.Invoke(service, new object[] { 17L, pose, 105d, 1024, 30d, identity, profile, density });
            Action tick = () => serviceType.GetMethod("RenderMain", Instance).Invoke(service, null);
            Func<object> latest = () => serviceType.GetMethod("Latest", Instance).Invoke(service, new object[] { 17L });
            // Advance the pure policy's rate clock without sleeping or running a
            // game timer. Each iteration still uses a distinct actual MAIN number.
            Action advance = () => {
                Number++;
                object policy = serviceType.GetField("policy", Instance).GetValue(service);
                object source = policy.GetType().GetMethod("Find", Instance).Invoke(policy, new object[] { 17L });
                source.GetType().GetField("LastAttempt", Instance).SetValue(source, double.NegativeInfinity);
            };
            Number = 1; allocations = publications = copies = 0; captured.Clear(); logicalSizes.Clear(); failPublish = false;
            demand(initial, 0);
            for (int tile = 0; tile < 4; tile++)
            {
                if (tile != 0) { demand(MatrixD.CreateTranslation(tile * 100, 0, 0), 0); advance(); }
                tick(); check((int)budget.GetType().GetProperty("Spent", Instance).GetValue(budget) <= Width * Height, "All real adaptive raster jobs obey the strict shared viewport occupancy ceiling.");
                check(tile == 3 ? latest() != null : latest() == null, "Incomplete native tiles never publish a partial camera atlas.");
            }
            check(captured.Count == 4 && captured.All(pose => pose == initial), "Every off-axis tile retains one frozen physical optical pose despite new game-thread requests.");
            check(copies == 4 && publications == 1 && allocations == 3, "One complete atlas uses four native crop copies, one atomic commit and reusable two atlases/shared scratch.");
            object first = latest(); long generation = (long)first.GetType().GetField("Generation", Instance).GetValue(first);
            string texture = (string)first.GetType().GetField("Texture", Instance).GetValue(first);
            // Repeat at a new pose. Resource identity remains stable while one
            // content revision advances only after the full staged batch.
            captured.Clear(); var secondPose = MatrixD.CreateTranslation(4, 5, 6); demand(secondPose, 0);
            for (int i = 0; i < 4; i++) { advance(); tick(); if (i < 3) check(ReferenceEquals(latest(), first), "Front atlas evidence stays at the last complete batch while staging changes."); }
            object second = latest();
            check(logicalSizes.Take(8).SequenceEqual(new[] { 512, 128, 64, 64, 512, 128, 64, 64 }), "Shared scratch safely alternates512→64→512 logical viewports without reallocation.");
            check(allocations == 3 && (long)second.GetType().GetField("Generation", Instance).GetValue(second) == generation && (string)second.GetType().GetField("Texture", Instance).GetValue(second) == texture,
                "Complete same-layout refreshes reuse resources and the authorized configuration generation.");
            check((long)second.GetType().GetField("ContentRevision", Instance).GetValue(second) == 2 && captured.All(pose => pose == secondPose), "Content revision and pose advance together after all cropped pixels complete.");
            object originalDensity=density;
            failCopy=true;demand(MatrixD.CreateTranslation(9,9,9),0);advance();tick();
            check(ReferenceEquals(latest(),second),"Staging-only crop failure retains the completed front and its matching pose/layout evidence.");
            failCopy=false;
            density=densityType.GetMethod("Full",Static).Invoke(null,new object[]{256});
            demand(secondPose,0);advance();tick();var coarse=latest();
            check(coarse!=null&&allocations==3&&(long)coarse.GetType().GetField("Generation",Instance).GetValue(coarse)==generation,
                "Stale/small density switches layout without destroying the front canvas or invalidating its generation.");
            density=originalDensity;demand(secondPose,0);advance();tick();
            check(ReferenceEquals(latest(),coarse)&&allocations==3,
                "Refining density retains the published coarse image while the new cropped staging batch is incomplete.");
            for(int i=0;i<3;i++){advance();tick();}
            check(latest()!=null&&allocations==3,"Alternating whole/cropped layouts reuse the same canvas and remain published.");
            demand(secondPose, 1); advance(); tick(); check(latest() == null, "Profile changes invalidate the old generation before a new multi-tile batch completes.");
            failPublish = true;
            for (int i = 0; i < 3; i++) { advance(); tick(); }
            check(latest() == null, "Failed atlas commit never authorizes old or partially overwritten pixels."); failPublish = false;
            options.GetType().GetField("CapturePasses").SetValue(options,0);
            serviceType.GetMethod("Configure",Instance).Invoke(service,new[]{options});
            int beforeUnlimited=copies, beforeCommits=publications;
            demand(secondPose,0);advance();tick();
            check(copies==beforeUnlimited+4&&publications==beforeCommits+1&&latest()!=null,
                "Uncapped pass preference admits all four crop tiles and one atomic image in a single main frame.");
            // A budget reservation can exist before any target. Pruning that
            // source must retire it even if no renderer frame ever allocates it.
            request.Invoke(service, new object[] { 18L, initial, 105d, 256, 30d, new object(), 0, null });
            object allPolicy = serviceType.GetField("policy", Instance).GetValue(service);
            object expired = allPolicy.GetType().GetMethod("Find", Instance).Invoke(allPolicy, new object[] { 18L });
            expired.GetType().GetField("DemandUntil", Instance).SetValue(expired, double.NegativeInfinity);
            int before = (int)budget.GetType().GetProperty("Pending", Instance).GetValue(budget);
            serviceType.GetMethod("Prune", Instance).Invoke(service, null);
            check((int)budget.GetType().GetProperty("Pending", Instance).GetValue(budget) < before, "Expired demand without GPU allocation releases its shared budget reservation.");
            request.Invoke(service, new object[] { 19L, initial, 105d, 256, 30d, new object(), 0, null });
            request.Invoke(service, new object[] { 20L, initial, 105d, 256, 30d, new object(), 0, null });
            object unrelated = allPolicy.GetType().GetMethod("Find", Instance).Invoke(allPolicy, new object[] { 19L });
            unrelated.GetType().GetField("DemandUntil", Instance).SetValue(unrelated, double.NegativeInfinity);
            before = (int)budget.GetType().GetProperty("Pending", Instance).GetValue(budget);
            serviceType.GetMethod("Revoke", Instance).Invoke(service, new object[] { 20L });
            check((int)budget.GetType().GetProperty("Pending", Instance).GetValue(budget) == before - 2, "Revoking one demand also cancels unrelated expired reservations pruned in that call.");
        }
        finally { serviceType.GetField("native", Instance).SetValue(service, null); ((IDisposable)service).Dispose(); h.UnpatchAll(h.Id); }
        MixedScheduler(plugin);
        RefinedProgress(plugin);
    }
    static void RefinedProgress(Assembly plugin)
    {
        var serviceType=plugin.GetType("HDRClientRenderer.DirectCameraCapture",true);var nativeType=plugin.GetType("HDRClientRenderer.DirectCameraCaptureNative",true);
        var service=New(serviceType);var native=New(nativeType,AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("VRageRender.MyRender11",false)).First(t=>t!=null).Assembly);
        var options=New(plugin.GetType("HDRClientRenderer.ClientRenderSettings",true));
        options.GetType().GetField("CapturePasses").SetValue(options,1);options.GetType().GetField("TileLimit").SetValue(options,64);options.GetType().GetField("TilePassCost").SetValue(options,0d);
        options.GetType().GetField("PixelBudget").SetValue(options,1024*1024);
        var budget=Budget(plugin);budget.GetType().GetMethod("Configure",Instance).Invoke(budget,new[]{options});
        serviceType.GetMethod("Configure",Instance).Invoke(service,new[]{options});serviceType.GetField("Clock",Instance).SetValue(service,(Func<double>)(()=>Number/60d));
        serviceType.GetField("native",Instance).SetValue(service,native);serviceType.GetMethod("SetBudget",Instance).Invoke(service,new[]{budget});
        serviceType.GetField("renderedEpoch",Instance).SetValue(service,serviceType.GetField("epoch",Instance).GetValue(service));
        var h=new Harmony("HDR.Tests.RefinedCaptureProgress");
        try
        {
            foreach(var e in new[]{("Frame",nameof(Frame),true),("FrameIdle",nameof(True),true),("DeviceChanged",nameof(False),true),
                ("BoundResolution",nameof(Bound),false),("CreateTarget",nameof(Allocate),false),("CreateAtlasTarget",nameof(Allocate),false),
                ("ClearAtlas",nameof(NoGpu),false),("CopyTile",nameof(Copy),false),("PublishAtlas",nameof(Publish),false),("Capture",nameof(Draw),false)})Patch(h,nativeType,e.Item1,e.Item2,e.Item3);
            var densityType=plugin.GetType("HDRClientRenderer.PanoramaDensity",true);var density=densityType.GetMethod("Full",Static).Invoke(null,new object[]{256});
            var packed=new double[1792];for(int i=0;i<256;i++){packed[i*7]=packed[i*7+6]=1;packed[i*7+1]=packed[i*7+3]=64;packed[i*7+2]=packed[i*7+4]=4096;packed[i*7+5]=1;}
            foreach(var p in new[]{(1,1),(14,1),(1,14),(14,14)}){int at=(p.Item2*16+p.Item1)*7;packed[at+1]=packed[at+3]=1024;packed[at+2]=packed[at+4]=1024*1024;}
            var refined=densityType.GetMethod("FromPacked",Static).Invoke(null,new object[]{packed,1024,true});
            var plan=plugin.GetType("HDRClientRenderer.CaptureTileAtlas",true).GetMethod("PlanAdaptive",Static).Invoke(null,new object[]{refined,1024,64,0d});
            int count=(int)plan.GetType().GetProperty("Count",Instance).GetValue(plan);check(count>4,"expanded service regression uses more than four actual crops");
            object identity=new object();var request=serviceType.GetMethod("Request",Instance);var render=serviceType.GetMethod("RenderMain",Instance);
            Action<MatrixD> demand=pose=>request.Invoke(service,new object[]{17L,pose,105d,1024,30d,identity,0,density});
            Func<object> latest=()=>serviceType.GetMethod("Latest",Instance).Invoke(service,new object[]{17L});
            Number=1;failPublish=failCopy=false;captured.Clear();logicalSizes.Clear();demand(MatrixD.Identity);render.Invoke(service,null);var front=latest();check(front!=null,"coarse completed front exists before refinement");
            density=refined;Number=2;demand(MatrixD.Identity);render.Invoke(service,null);check(ReferenceEquals(latest(),front),"normal30Hz batch pacing retains completed front on ineligible next frame");
            var pose=MatrixD.CreateTranslation(4,5,6);Number=3;demand(pose);render.Invoke(service,null);
            var policy=serviceType.GetField("policy",Instance).GetValue(service);var source=policy.GetType().GetMethod("Find",Instance).Invoke(policy,new object[]{17L});
            double attempt=(double)source.GetType().GetField("LastAttempt",Instance).GetValue(source);
            serviceType.GetMethod("Warm",Instance).Invoke(service,new object[]{17L});int paused=copies;
            for(int i=0;i<3;i++){Number++;render.Invoke(service,null);}
            check(copies==paused&&ReferenceEquals(latest(),front),"hidden/warm many-tile demand stops auxiliary passes and retains its completed front");
            for(int tile=1;tile<count;tile++)
            {
                Number++;demand(MatrixD.CreateTranslation(Number*100,0,0));render.Invoke(service,null);
                check((double)source.GetType().GetField("LastAttempt",Instance).GetValue(source)==attempt,"all continuation tiles keep the original batch rate clock without timestamp resets");
                if(tile<count-1)check(ReferenceEquals(latest(),front),"last completed front survives every refined pending tile");
            }
            var complete=latest();check(complete!=null&&!ReferenceEquals(complete,front)&&captured.Skip(1).All(p=>p==pose),"many-tile batch completes atomically at one frozen pose under actual60Hz clock");
            Number++;demand(pose);render.Invoke(service,null);check(ReferenceEquals(latest(),complete),"next many-tile batch starts staging without replacing completed front");
            options.GetType().GetField("PixelBudget").SetValue(options,4096);budget.GetType().GetMethod("Configure",Instance).Invoke(budget,new[]{options});
            int before=copies;for(int i=0;i<130&&ReferenceEquals(latest(),complete);i++){Number++;demand(pose);render.Invoke(service,null);}
            check(copies>before&&latest()!=null&&!ReferenceEquals(latest(),complete),"tightened budget cancels oversized staging, replans and eventually publishes rather than stranding old tiles");
        }
        finally{serviceType.GetField("native",Instance).SetValue(service,null);((IDisposable)service).Dispose();h.UnpatchAll(h.Id);}
    }
    static void MixedScheduler(Assembly plugin)
    {
        var policyType = plugin.GetType("HDRClientRenderer.DirectCameraCapturePolicy", true);
        object policy = New(policyType), budget = Budget(plugin);
        var budgetType = budget.GetType(); var kind = budgetType.GetNestedType("Kind", BindingFlags.NonPublic);
        object capture = Enum.ToObject(kind, 0);
        var ask = policyType.GetMethod("Request", Instance); var find = policyType.GetMethod("Find", Instance);
        var eligible = policyType.GetMethod("Eligible", Instance); var candidates = policyType.GetMethod("Candidates", Instance);
        var record = policyType.GetMethod("RecordAttempt", Instance);
        var register = budgetType.GetMethod("Request", Instance); var spend = budgetType.GetMethod("TrySpend", Instance);
        int[] captured = new int[6], families = new int[3];
        var last = Enumerable.Repeat(double.NegativeInfinity, 6).ToArray();
        var lastProgress = new int[6]; object payload = new object();
        Width = Height = 256;
        for (int frame = 1; frame <= 384; frame++)
        {
            Number = frame; double now = frame / 60d;
            for (int i = 0; i < 3; i++) register.Invoke(budget, new object[] { "family" + i, Enum.ToObject(kind, i + 1), 65536 });
            for (int camera = 0; camera < 6; camera++)
            {
                object source = ask.Invoke(policy, new object[] { (long)camera + 1, 105d, 256, 30d, now, payload });
                string key = "camera" + camera;
                if ((bool)eligible.Invoke(policy, new object[] { source, now })) register.Invoke(budget, new object[] { key, capture, 65536 });
                else budgetType.GetMethod("Cancel", Instance).Invoke(budget, new object[] { key });
            }
            int passes = 0;
            foreach (object source in (Array)candidates.Invoke(policy, new object[] { now, (long)frame, true, false }))
            {
                if (passes == 3) break;
                int camera = (int)((long)source.GetType().GetField("Camera", Instance).GetValue(source) - 1);
                string key = "camera" + camera;
                if (!(bool)spend.Invoke(budget, new object[] { key, capture, 65536 })) continue;
                long generation = (long)source.GetType().GetField("Generation", Instance).GetValue(source);
                check((bool)record.Invoke(policy, new object[] { source, generation, now }), "Only admitted eligible capture work advances the source rate clock.");
                check(now - last[camera] + 1e-9 >= 1 / 30d, "Mixed-family scheduling retains each camera's30Hz scene-pass ceiling.");
                captured[camera]++; passes++; last[camera] = now; lastProgress[camera] = frame;
                budgetType.GetMethod("Complete", Instance).Invoke(budget, new object[] { key });
            }
            for (int i = 0; i < 3; i++) if ((bool)spend.Invoke(budget, new object[] { "family" + i, Enum.ToObject(kind, i + 1), 65536 })) families[i]++;
            check(passes <= 3 && (int)budgetType.GetProperty("Spent", Instance).GetValue(budget) <= 65536, "Hostile combined scheduler never exceeds scene or viewport occupancy limits.");
            if (frame >= 48) for (int camera = 0; camera < 6; camera++) check(frame - lastProgress[camera] <= 48, "No source phase-locks behind mixed-family grants at60Hz/30Hz.");
        }
        check(captured.All(count => count >= 12) && families.All(count => count >= 48), "All six capture sources and all three competing full-viewport families make sustained progress.");
        Console.WriteLine("PASS: exact60Hz six30Hz sources +fullviewport panorama/LCD/UI phase-lock regression; captures=" + string.Join(",", captured) + ", families=" + string.Join(",", families) + "; actual policy/ledger clocks, no timestamp resets.");
    }
}
