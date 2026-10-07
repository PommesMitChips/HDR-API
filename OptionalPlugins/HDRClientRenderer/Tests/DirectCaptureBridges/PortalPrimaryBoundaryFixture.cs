using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VRageMath;

// Executes the installed DrawGameScene Harmony wrapper with an exact typed call.
// Its original GPU body alone is replaced by a CPU observation/throw terminal.
// Cull queries, DependencyBatch, camera owners and snapshot/restore are real
// initialized engine objects. The trusted provider callback injects secondary
// work; it is not a test invocation of BeforeScene/AfterScene/FailedScene.
static class PortalPrimaryBoundaryFixture
{
    const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static object Manager, Scheduler;
    static object mainTarget, otherTarget, currentDevice, service, native, matrices, cpuScheduler, visibilityIdentity, fakeContextToken;
    static Type serviceType, nativeType;
    static Action<object> draw;
    static readonly List<string> events = new();
    static long frame;
    static int checks, beforeCalls, afterCalls, starts, completed, closed, primaryBodies, secondaryBodies, frontGeneration, cameraWork;
    static bool throwBody;
    static bool failContextFactory, nullContextFactory, tracePreparation;
    static int portalEpoch, deviceRetirements, contextFactories, cameraTargetRetirements;
    static Vector2I presentation;
    static Exception firstAssertion;
    static MatrixD expectedPose;
    static Matrix expectedProjection;
    static void Check(bool ok, string why)
    {
        if (!ok) { var error = new Exception("Primary boundary: " + why); firstAssertion ??= error; throw error; }
        checks++;
    }
    static Type Engine(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null);
    static object New(Type type, params object[] args) => Activator.CreateInstance(type, I, null, args, null);
    static bool Backbuffer(ref object __result) { __result = mainTarget; return false; }
    static bool Device(ref object __result) { __result = currentDevice; return false; }
    static bool Frame(ref long __result) { __result = frame; return false; }
    static bool PortalEpoch(ref int __result) { __result = portalEpoch; return false; }
    static bool PhysicalPresentation(ref Vector2I __result) { __result = presentation; return false; }
    static bool CpuScheduler(ref object __result) { __result = cpuScheduler; return false; }
    static bool NoGpu() => false;
    static bool ReleasePrivateDevice()
    {
        // A CPU-only terminal for retirement of owned device resources. Count
        // every call so a duplicate post-primary reset cannot go unnoticed.
        deviceRetirements++; portalEpoch++;
        nativeType.GetField("resourceDevice", I).SetValue(native, null);
        nativeType.GetField("contextToken", I).SetValue(native, null);
        if (tracePreparation) events.Add("retire-device");
        return false;
    }
    static object ContextFactory()
    {
        contextFactories++;
        if (tracePreparation) events.Add("prepare-device");
        if (failContextFactory) throw new InjectedFailure("private context creation");
        return nullContextFactory ? null : fakeContextToken;
    }
    static bool RetireCameraTarget(string __0)
    {
        Check(__0 == "HDR_PrimaryFixture_OldCamera" || __0 == "HDR_PrimaryFixture_OldCamera_Stage", "unexpected camera texture deletion endpoint");
        Check(primaryBodies == 1 && closed == 1, "old main-camera atlas retired before primary scene/presentation completion");
        cameraTargetRetirements++; events.Add("retire-camera"); return false;
    }
    static void ObserveCameraWork()
    {
        cameraWork++; events.Add("camera");
        Check(primaryBodies == 1, "ordinary RenderMain ran before original primary scene");
        Check(closed == 1 || beforeCalls == 0, "ordinary RenderMain ran before portal presentation closure");
    }
    static bool IsCapturing => (int)serviceType.GetField("capturing", S).GetValue(null) != 0;
    static void Terminal(object target)
    {
        if (IsCapturing) { secondaryBodies++; events.Add("secondary"); return; }
        primaryBodies++; events.Add("primary");
        Check((MatrixD)matrices.GetType().GetField("InvViewD", I).GetValue(matrices) == expectedPose &&
            (Matrix)matrices.GetType().GetField("Projection", I).GetValue(matrices) == expectedProjection,
            "original primary stage inherited a secondary pose/projection");
        if (throwBody) throw new InjectedFailure("original primary stage");
    }
    static IEnumerable<CodeInstruction> CpuBody(IEnumerable<CodeInstruction> original)
    {
        // Keep the actual DrawGameScene signature and all installed production
        // prefixes/postfixes/finalizers; only its native GPU implementation ends
        // at this controlled terminal. No prefix skips the production wrapper.
        yield return new CodeInstruction(OpCodes.Ldarg_0);
        yield return new CodeInstruction(OpCodes.Call, typeof(PortalPrimaryBoundaryFixture).GetMethod(nameof(Terminal), S));
        yield return new CodeInstruction(OpCodes.Ret);
    }
    static Action<object> TypedScene(MethodInfo method)
    {
        var call = new DynamicMethod("HDRPrimaryBoundaryExactSceneCall", typeof(void), new[] { typeof(object) }, typeof(PortalPrimaryBoundaryFixture).Module, true);
        var il = call.GetILGenerator(); var aux = il.DeclareLocal(method.GetParameters()[1].ParameterType.GetElementType());
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, method.GetParameters()[0].ParameterType);
        il.Emit(OpCodes.Ldloca, aux); il.Emit(OpCodes.Call, method); il.Emit(OpCodes.Ret);
        return (Action<object>)call.CreateDelegate(typeof(Action<object>));
    }
    static void SetCallback(string name, object value) => serviceType.GetProperty(name, I).SetValue(service, value);
    static void Healthy(bool value) => nativeType.GetField("<Healthy>k__BackingField", I).SetValue(native, value);
    static void ResetCase()
    {
        events.Clear(); beforeCalls = afterCalls = starts = completed = closed = primaryBodies = secondaryBodies = cameraWork = 0;
        throwBody = failContextFactory = nullContextFactory = tracePreparation = false; deviceRetirements = contextFactories = cameraTargetRetirements = 0; frame++; Healthy(true);
        serviceType.GetField("capturing", S).SetValue(null, 0);
        serviceType.GetField("portalCapturing", S).SetValue(null, 0);
        serviceType.GetField("renderedEpoch", I).SetValue(service, serviceType.GetField("epoch", I).GetValue(service));
        nativeType.GetField("resourceDevice", I).SetValue(native, currentDevice);
        nativeType.GetField("contextToken", I).SetValue(native, fakeContextToken);
        SetCallback("BeforePrimaryWork", (Action)(() => { beforeCalls++; events.Add("before"); }));
    }
    static Exception Failure(Action action)
    {
        try { action(); return null; } catch (Exception error) { return error.GetBaseException(); }
    }
    static void Sequence(params string[] expected) => Check(events.SequenceEqual(expected),
        "wrapper ordering: expected " + string.Join("/", expected) + "; observed " + string.Join("/", events));

    internal static void Run(Assembly plugin)
    {
        checks = 0; firstAssertion = null; frontGeneration = 7; frame = 120; portalEpoch = 40; presentation = new Vector2I(1920, 1200);
        var terminal = new Harmony("HDR.Tests.PortalPrimaryBoundary");
        serviceType = plugin.GetType("HDRClientRenderer.DirectCameraCapture", true);
        service = New(serviceType); var renderer = Engine("VRageRender.MyRender11");
        var environmentField = renderer.GetField("Environment", S);
        var matricesField = environmentField.FieldType.GetField("Matrices", I);
        matrices = matricesField.GetValue(environmentField.GetValue(null));
        object savedCamera = null;
        var viewport = renderer.GetProperty("ViewportResolution", S); var oldViewport = viewport.GetValue(null);
        var oldEpoch = serviceType.GetField("epoch", I).GetValue(service);
        object queries = null, batch = null;
        try
        {
            // Real installation also checks the whole camera-native ABI and
            // installs its scheduler/cull/particle/draw preservation hooks.
            serviceType.GetMethod("TryInstall", I).Invoke(service, new object[] { (Action<string>)Console.WriteLine });
            Check((bool)serviceType.GetProperty("Ready", I).GetValue(service), "actual TryInstall did not install capture hooks");
            native = serviceType.GetField("native", I).GetValue(service); nativeType = native.GetType();
            var scene = (MethodInfo)nativeType.GetField("DrawScene", I).GetValue(native);
            var bb = renderer.GetProperty("Backbuffer", S);
            mainTarget = RuntimeHelpers.GetUninitializedObject(bb.PropertyType);
            otherTarget = RuntimeHelpers.GetUninitializedObject(bb.PropertyType);
            currentDevice = RuntimeHelpers.GetUninitializedObject(renderer.GetProperty("DeviceInstance", S).PropertyType);
            foreach (var pair in new[] { (bb.GetGetMethod(true), nameof(Backbuffer)),
                (renderer.GetProperty("DeviceInstance", S).GetGetMethod(true), nameof(Device)),
                (Engine("VRageRender.MyCommon").GetProperty("FrameCounter", S).GetGetMethod(true), nameof(Frame)) })
                terminal.Patch(pair.Item1, prefix: new HarmonyMethod(typeof(PortalPrimaryBoundaryFixture).GetMethod(pair.Item2, S)));
            // Execute production PreparePrimaryDevice/EnsureDevice; intercept
            // only their private GPU factory/retirement terminals. The epoch
            // terminal models the integration's resource-reset generation.
            var factory = (MethodInfo)nativeType.GetField("createContextState", I).GetValue(native);
            currentDevice = RuntimeHelpers.GetUninitializedObject(factory.DeclaringType);
            fakeContextToken = RuntimeHelpers.GetUninitializedObject(factory.ReturnType);
            // A closed generic SharpDX reflection invoker can cache a bypass
            // around a detour on repeated calls. Substitute the owned adapter's
            // validated factory endpoint with an exact-signature CPU terminal;
            // actual PreparePrimaryDevice/EnsureDevice control flow stays intact.
            var cpuFactory = new DynamicMethod("HDRPrivateContextFactoryCpuTerminal", factory.ReturnType,
                factory.GetParameters().Select(p => p.ParameterType).ToArray(), typeof(PortalPrimaryBoundaryFixture).Module, true);
            var factoryIl = cpuFactory.GetILGenerator();
            factoryIl.Emit(OpCodes.Call, typeof(PortalPrimaryBoundaryFixture).GetMethod(nameof(ContextFactory), S));
            factoryIl.Emit(OpCodes.Castclass, factory.ReturnType); factoryIl.Emit(OpCodes.Ret);
            nativeType.GetField("createContextState", I).SetValue(native, cpuFactory);
            terminal.Patch(nativeType.GetProperty("PortalEpoch", I).GetGetMethod(true),
                prefix: new HarmonyMethod(typeof(PortalPrimaryBoundaryFixture).GetMethod(nameof(PortalEpoch), S)));
            terminal.Patch(nativeType.GetMethod("ReleaseDeviceResources", I),
                prefix: new HarmonyMethod(typeof(PortalPrimaryBoundaryFixture).GetMethod(nameof(ReleasePrivateDevice), S)));
            terminal.Patch(nativeType.GetMethod("DisposeNative", I), prefix: new HarmonyMethod(typeof(PortalPrimaryBoundaryFixture).GetMethod(nameof(NoGpu), S)));
            terminal.Patch(nativeType.GetMethod("DestroyTarget", I),
                prefix: new HarmonyMethod(typeof(PortalPrimaryBoundaryFixture).GetMethod(nameof(RetireCameraTarget), S)));
            terminal.Patch(scene, transpiler: new HarmonyMethod(typeof(PortalPrimaryBoundaryFixture).GetMethod(nameof(CpuBody), S)));
            terminal.Patch(serviceType.GetMethod("RenderMain", I),
                prefix: new HarmonyMethod(typeof(PortalPrimaryBoundaryFixture).GetMethod(nameof(ObserveCameraWork), S)));
            draw = TypedScene(scene);

            Manager = New(Engine("VRage.Render11.Culling.MyCullManager"), (object)null);
            Scheduler = RuntimeHelpers.GetUninitializedObject(Engine("VRage.Render11.Render.MyRenderScheduler"));
            cpuScheduler = DispatchProxy.Create(Engine("ParallelTasks.IWorkScheduler"), typeof(CaptureFixtureScheduler));
            terminal.Patch(Engine("ParallelTasks.Parallel").GetMethod("get_Scheduler", S),
                prefix: new HarmonyMethod(typeof(PortalPrimaryBoundaryFixture).GetMethod(nameof(CpuScheduler), S)));
            var batchType = Engine("ParallelTasks.DependencyBatch");
            batch = New(batchType, Enum.Parse(Engine("ParallelTasks.WorkPriority"), "Normal"));
            Scheduler.GetType().GetField("m_batch", I).SetValue(Scheduler, batch);
            nativeType.GetField("cullManager", I).SetValue(native, typeof(PortalPrimaryBoundaryFixture).GetField(nameof(Manager), S));
            nativeType.GetField("renderScheduler", I).SetValue(native, typeof(PortalPrimaryBoundaryFixture).GetField(nameof(Scheduler), S));
            queries = Manager.GetType().GetMethod("GetCullQueries", I).Invoke(Manager, null);
            var querySize = queries.GetType().GetProperty("Size", I); var addView = queries.GetType().GetMethod("AddView", I);
            // Native manager retirement clears its actual pooled query array.
            Action clearQueries = () => Manager.GetType().GetMethod("OnFrameEnd", I).Invoke(Manager, null);
            Action appendQuery = () => addView.Invoke(queries, new object[] { Enum.ToObject(addView.GetParameters()[0].ParameterType, 1), 0,
                new BoundingFrustumD(MatrixD.Identity), new BoundingFrustumD(MatrixD.Identity), Matrix.Identity, Vector3D.Zero, null, null });
            var jobCount = batchType.GetField("m_jobCount", I); var addJob = batchType.GetMethod("Add", I);
            Action clearJobs = () => batchType.GetMethod("Clear", I).Invoke(batch, new object[] { (int)jobCount.GetValue(batch) });
            Func<bool> idle = () => (bool)nativeType.GetProperty("FrameIdle", I).GetValue(native);
            Check(idle(), "real fresh query/batch owners are not idle");

            var cameraSnapshotType = nativeType.GetNestedType("CameraMatricesSnapshot", BindingFlags.NonPublic);
            savedCamera = New(cameraSnapshotType, environmentField, matricesField);
            expectedPose = MatrixD.CreateRotationY(.23) * MatrixD.CreateTranslation(20, -7, 31);
            var authored = MatrixD.CreatePerspectiveFieldOfView(.83, 1.6, .2, 2500); authored.M12 = .02; authored.M31 = -.07;
            object[] reverseArgs = { authored, MatrixD.Identity };
            Check((bool)plugin.GetType("HDRClientRenderer.HDRClientRendererPlugin", true).GetMethod("PortalProjectionFromCamera", S).Invoke(null, reverseArgs), "native projection normalization failed");
            expectedProjection = (Matrix)(MatrixD)reverseArgs[1];
            var message = nativeType.GetMethod("CameraMessage", I).Invoke(native, new object[] { expectedPose, 48d, matrices, null, (MatrixD?)(MatrixD)reverseArgs[1] });
            var setup = (MethodInfo)nativeType.GetField("setup", I).GetValue(native);
            setup.Invoke(null, new[] { message, matrices, Enum.ToObject(setup.GetParameters()[2].ParameterType, 0) });
            var nativePose = (MatrixD)matrices.GetType().GetField("InvViewD", I).GetValue(matrices);
            Check(Vector3D.Distance(nativePose.Translation, expectedPose.Translation) < 1e-10 &&
                Vector3D.Distance(nativePose.Forward, expectedPose.Forward) < 1e-10 &&
                (Matrix)matrices.GetType().GetField("Projection", I).GetValue(matrices) == expectedProjection,
                "actual native setup did not preserve the requested primary pose/projection");
            // Engine inverse(view) round trips may differ by a double ULP;
            // the boundary certificate compares exact current native bits.
            expectedPose = nativePose;
            viewport.SetValue(null, new Vector2I(960, 600));
            var readerType = plugin.GetType("HDRClientRenderer.PortalPrimaryViewNative", true); var reader = New(readerType, renderer.Assembly);
            // The real physical backbuffer descriptor would query a GPU
            // resource. Intercept only that terminal; all primary camera,
            // projection, native frame and scaled viewport reads stay real.
            terminal.Patch(readerType.GetMethod("ReadPresentation", I),
                prefix: new HarmonyMethod(typeof(PortalPrimaryBoundaryFixture).GetMethod(nameof(PhysicalPresentation), S)));
            var snapshotMethod = readerType.GetMethod("TrySnapshot", I);
            object snapshot = null;

            // A genuine shared-budget instance, with the installed frame reader
            // and the primary viewport, proves recursion does not reset/spend
            // another main frame. Only the trusted callback asks for a grant.
            var budgetType = plugin.GetType("HDRClientRenderer.ClientPixelBudget", true);
            var budgetFrame = budgetType.GetNestedType("Frame", BindingFlags.NonPublic);
            var frameCtor = budgetFrame.GetConstructor(I, null, new[] { typeof(long), typeof(int), typeof(int) }, null);
            var frameExpr = Expression.New(frameCtor, Expression.Field(null, typeof(PortalPrimaryBoundaryFixture).GetField(nameof(frame), S)), Expression.Constant(960), Expression.Constant(600));
            var budgetReader = Expression.Lambda(typeof(Func<>).MakeGenericType(budgetFrame), frameExpr).Compile();
            var budget = New(budgetType, budgetReader); var captureKind = Enum.ToObject(budgetType.GetNestedType("Kind", BindingFlags.NonPublic), 0);
            serviceType.GetMethod("SetBudget", I).Invoke(service, new[] { budget });
            Func<int> spent = () => (int)budgetType.GetProperty("Spent", I).GetValue(budget);
            SetCallback("MainSceneStarting", (Func<object, object>)(target => { starts++; events.Add("start"); Check(ReferenceEquals(target, mainTarget), "visibility received a non-main identity"); visibilityIdentity = target; return target; }));
            SetCallback("MainSceneCompleted", (Action<object, bool>)((ticket, success) => { completed++; events.Add(success ? "complete" : "failed"); Check(ReferenceEquals(ticket, visibilityIdentity), "visibility ticket identity changed"); }));
            SetCallback("AfterPrimaryWork", (Action)(() => { afterCalls++; events.Add("after"); Check(primaryBodies == 1, "ordinary camera/provider callback ran before original primary stage"); }));
            SetCallback("PrimaryPresentationCompleted", (Action)(() => { closed++; events.Add("close"); }));

            ResetCase();
            SetCallback("BeforePrimaryWork", (Action)(() =>
            {
                beforeCalls++; events.Add("before"); Check(primaryBodies == 0, "portal callback ran after original primary stage"); Check(idle(), "portal callback entered nonempty queries/jobs");
                object[] snapArgs = { (long)serviceType.GetProperty("NativeEpoch", I).GetValue(service), null };
                Check((bool)snapshotMethod.Invoke(reader, snapArgs), "current native primary snapshot unavailable"); snapshot = snapArgs[1];
                object Field(string name) => snapshot.GetType().GetField(name, I).GetValue(snapshot);
                Check((MatrixD)Field("Viewer") == expectedPose && (MatrixD)Field("Projection") == (MatrixD)expectedProjection,
                    "capture did not select current native InvViewD/full projection");
                Check((long)Field("Frame") == frame && (int)Field("Width") == 960 && (int)Field("Height") == 600, "primary native snapshot frame/viewport mismatched");
                var gameCameraSentinel = MatrixD.CreateTranslation(-200, 90, 8);
                Check((MatrixD)Field("Viewer") != gameCameraSentinel, "native snapshot inherited gameplay camera fixture sentinel");
                Check((bool)budgetType.GetMethod("TrySpend", I).Invoke(budget, new[] { "HDR.Portal.CpuPrimary", captureKind, (object)4096 }), "primary grant was unavailable");
                object restore = New(cameraSnapshotType, environmentField, matricesField);
                try
                {
                    serviceType.GetField("capturing", S).SetValue(null, 1); serviceType.GetField("portalCapturing", S).SetValue(null, 1);
                    matrices.GetType().GetField("InvViewD", I).SetValue(matrices, gameCameraSentinel);
                    matrices.GetType().GetField("CameraPosition", I).SetValue(matrices, gameCameraSentinel.Translation);
                    matrices.GetType().GetField("Projection", I).SetValue(matrices, Matrix.Identity);
                    draw(mainTarget); draw(otherTarget);
                    Check(beforeCalls == 1 && starts == 1 && afterCalls == 0 && cameraWork == 0 && closed == 0 && completed == 0, "secondary wrapper obtained a main ticket/reentered callbacks");
                    Check(spent() == 4096, "recursive scene changed/spent the primary pixel grant");
                }
                finally
                { cameraSnapshotType.GetMethod("Restore", I).Invoke(restore, null); serviceType.GetField("portalCapturing", S).SetValue(null, 0); serviceType.GetField("capturing", S).SetValue(null, 0); }
                frontGeneration++;
                Check((MatrixD)Field("Viewer") == expectedPose && (MatrixD)Field("Projection") == (MatrixD)expectedProjection, "immutable native snapshot changed during secondary setup");
            }));
            draw(mainTarget);
            Check(primaryBodies == 1 && secondaryBodies == 2 && beforeCalls == 1 && afterCalls == 1 && cameraWork == 1 && completed == 1 && closed == 1, "eligible primary did not execute exactly once through wrapper");
            Check(frontGeneration == 8 && spent() == 4096, "eligible primary front/budget did not publish once");
            Sequence("start", "before", "secondary", "secondary", "primary", "complete", "close", "camera", "after");

            ResetCase(); draw(otherTarget);
            Check(primaryBodies == 1 && beforeCalls == 0 && afterCalls == 0 && cameraWork == 0 && starts == 0 && completed == 0 && closed == 0 && frontGeneration == 8,
                "equal-type non-main target acquired callbacks or changed retained front"); Sequence("primary");
            ResetCase(); draw(null);
            Check(beforeCalls == 0 && starts == 0 && frontGeneration == 8, "null target admitted a primary ticket");

            ResetCase(); appendQuery(); var priorQueries = (int)querySize.GetValue(queries); draw(mainTarget);
            Check(beforeCalls == 0 && afterCalls == 0 && cameraWork == 0 && primaryBodies == 1 && frontGeneration == 8 && (int)querySize.GetValue(queries) == priorQueries,
                "preexisting native query was consumed or refreshed portal pixels"); clearQueries();
            ResetCase(); int jobsRun = 0; addJob.Invoke(batch, new object[] { (Action)(() => jobsRun++) }); var priorJobs = (int)jobCount.GetValue(batch); draw(mainTarget);
            Check(beforeCalls == 0 && afterCalls == 0 && cameraWork == 0 && primaryBodies == 1 && frontGeneration == 8 && jobsRun == 0 && (int)jobCount.GetValue(batch) == priorJobs,
                "preexisting native batch was executed/cleared or refreshed portal pixels"); clearJobs();

            ResetCase(); var validProjection = expectedProjection;
            expectedProjection = Matrix.Identity; matrices.GetType().GetField("Projection", I).SetValue(matrices, expectedProjection);
            object[] invalidSnapshot = { 0L, null };
            Check(!(bool)snapshotMethod.Invoke(reader, invalidSnapshot) && invalidSnapshot[1] == null, "invalid native primary projection unexpectedly yielded a capture snapshot");
            draw(mainTarget);
            Check(beforeCalls == 0 && primaryBodies == 1 && closed == 0 && frontGeneration == 8,
                "missing native primary snapshot refreshed pixels or prevented the original primary scene");
            Sequence("start", "primary", "complete", "camera", "after");
            expectedProjection = validProjection; matrices.GetType().GetField("Projection", I).SetValue(matrices, expectedProjection);

            ResetCase(); SetCallback("BeforePrimaryWork", (Action)(() =>
            {
                beforeCalls++; events.Add("before");
                object restore = New(cameraSnapshotType, environmentField, matricesField);
                try
                {
                    matrices.GetType().GetField("InvViewD", I).SetValue(matrices, MatrixD.CreateTranslation(-50, 2, 3));
                    matrices.GetType().GetField("Projection", I).SetValue(matrices, Matrix.Identity);
                    throw new InjectedFailure("restored healthy idle");
                }
                finally { cameraSnapshotType.GetMethod("Restore", I).Invoke(restore, null); }
            }));
            Check(Failure(() => draw(mainTarget)) == null, "restored healthy/idle optional failure aborted original primary");
            Check(primaryBodies == 1 && completed == 1 && afterCalls == 1 && closed == 1 && frontGeneration == 8, "restored exception lost primary or duplicated presentation closure");
            Sequence("start", "before", "close", "primary", "complete", "camera", "after");

            foreach (string failure in new[] { "unhealthy throw", "dirty queries throw", "dirty jobs throw", "frame change", "epoch change", "backbuffer change", "device change", "pose change", "projection change", "viewport change", "presentation change" })
            {
                ResetCase(); long baselineEpoch = (long)serviceType.GetField("epoch", I).GetValue(service); var baselineTarget = mainTarget; var baselineDevice = currentDevice;
                SetCallback("BeforePrimaryWork", (Action)(() =>
                {
                    beforeCalls++; events.Add("before");
                    switch (failure)
                    {
                        case "unhealthy throw": Healthy(false); throw new InjectedFailure(failure);
                        case "dirty queries throw": appendQuery(); throw new InjectedFailure(failure);
                        case "dirty jobs throw": addJob.Invoke(batch, new object[] { (Action)(() => jobsRun++) }); throw new InjectedFailure(failure);
                        case "frame change": frame++; break;
                        case "epoch change": serviceType.GetField("epoch", I).SetValue(service, baselineEpoch + 1); break;
                        case "backbuffer change": mainTarget = otherTarget; break;
                        case "device change": currentDevice = RuntimeHelpers.GetUninitializedObject(baselineDevice.GetType()); break;
                        case "pose change":
                            var moved = expectedPose; moved.Translation += new Vector3D(3, 1, 2);
                            matrices.GetType().GetField("InvViewD", I).SetValue(matrices, moved);
                            matrices.GetType().GetField("CameraPosition", I).SetValue(matrices, moved.Translation); break;
                        case "projection change": var changed = expectedProjection; changed.M31 += .01f; matrices.GetType().GetField("Projection", I).SetValue(matrices, changed); break;
                        case "viewport change": viewport.SetValue(null, new Vector2I(961, 600)); break;
                        case "presentation change": presentation = new Vector2I(1921, 1200); break;
                    }
                }));
                var error = Failure(() => draw(baselineTarget));
                mainTarget = baselineTarget; currentDevice = baselineDevice; serviceType.GetField("epoch", I).SetValue(service, baselineEpoch);
                Check(error != null, failure + " was accepted by primary boundary");
                bool dirty = failure.StartsWith("dirty ", StringComparison.Ordinal);
                Check(primaryBodies == 0 && afterCalls == 0 && cameraWork == 0 && completed == 1 && closed == (dirty ? 0 : 1) && frontGeneration == 8,
                    failure + " entered original primary, published, or closed despite unretired native ownership");
                if (dirty) Sequence("start", "before", "failed"); else Sequence("start", "before", "close", "failed");
                clearQueries(); clearJobs(); Healthy(true);
                matrices.GetType().GetField("InvViewD", I).SetValue(matrices, expectedPose);
                matrices.GetType().GetField("CameraPosition", I).SetValue(matrices, expectedPose.Translation);
                matrices.GetType().GetField("Projection", I).SetValue(matrices, expectedProjection);
                viewport.SetValue(null, new Vector2I(960, 600));
                presentation = new Vector2I(1920, 1200);
            }

            ResetCase(); throwBody = true;
            var originalError = Failure(() => draw(mainTarget));
            Check(originalError is InjectedFailure && originalError.Message == "original primary stage", "finalizer replaced original primary exception");
            Check(primaryBodies == 1 && completed == 1 && afterCalls == 0 && cameraWork == 0 && closed == 1, "original exception reached post-primary provider or lost/doubled closure");
            Sequence("start", "before", "primary", "failed", "close");

            int capturedDeviceEpoch = -1;
            var targetType = serviceType.GetNestedType("Target", BindingFlags.NonPublic);
            var targets = serviceType.GetField("targets", I).GetValue(service);
            Action settledCapture = () =>
            {
                beforeCalls++; events.Add("before");
                Check(primaryBodies == 0 && contextFactories == 1 && deviceRetirements == 1,
                    "first-use/replacement callback preceded completed device settlement");
                Check(!(bool)nativeType.GetProperty("DeviceChanged", I).GetValue(native) &&
                    ReferenceEquals(nativeType.GetField("resourceDevice", I).GetValue(native), currentDevice) &&
                    ReferenceEquals(nativeType.GetField("contextToken", I).GetValue(native), fakeContextToken),
                    "callback observed an uncommitted native device/context token");
                var latched = nativeType.GetMethod("PrimaryView", I).Invoke(native, null);
                Check(latched != null, "prepared callback lost native primary snapshot");
                capturedDeviceEpoch = (int)nativeType.GetProperty("PortalEpoch", I).GetValue(native);
                Check((long)latched.GetType().GetField("Epoch", I).GetValue(latched) == capturedDeviceEpoch &&
                    (long)latched.GetType().GetField("Frame", I).GetValue(latched) == frame &&
                    (MatrixD)latched.GetType().GetField("Viewer", I).GetValue(latched) == expectedPose,
                    "pre-primary capture latched an epoch/view from before device settlement");
                Check(ReferenceEquals(serviceType.GetField("primaryPreparedDevice", I).GetValue(service), currentDevice) &&
                    (long)serviceType.GetField("primaryPreparedEpoch", I).GetValue(service) ==
                    (long)serviceType.GetProperty("NativeEpoch", I).GetValue(service),
                    "prepared device certificate omitted the owner's current world epoch");
            };
            foreach (string transition in new[] { "cold first use", "device replacement" })
            {
                ResetCase(); tracePreparation = true;
                // Seed only the real service's managed old-atlas ownership. Its
                // two native deletion endpoints are intercepted, never created.
                var oldTarget = New(targetType); targetType.GetField("Camera", I).SetValue(oldTarget, 9001L);
                targetType.GetField("Name", I).SetValue(oldTarget, "HDR_PrimaryFixture_OldCamera");
                targetType.GetField("StagingName", I).SetValue(oldTarget, "HDR_PrimaryFixture_OldCamera_Stage");
                targets.GetType().GetMethod("Add", I).Invoke(targets, new[] { (object)9001L, oldTarget });
                if (transition == "cold first use")
                { nativeType.GetField("resourceDevice", I).SetValue(native, null); nativeType.GetField("contextToken", I).SetValue(native, null); }
                else currentDevice = RuntimeHelpers.GetUninitializedObject(currentDevice.GetType());
                int beforeEpoch = portalEpoch; SetCallback("BeforePrimaryWork", settledCapture); draw(mainTarget);
                Check(beforeCalls == 1 && primaryBodies == 1 && completed == 1 && closed == 1 && cameraWork == 1 && afterCalls == 1,
                    transition + " did not finish one complete primary presentation; " + string.Join("/", events) + "; factories=" + contextFactories + "; retirements=" + deviceRetirements);
                Check(contextFactories == 1 && deviceRetirements == 1 && portalEpoch == beforeEpoch + 1 && portalEpoch == capturedDeviceEpoch,
                    transition + " post-primary RenderMain reset the newly prepared portal epoch again");
                Check(!(bool)serviceType.GetField("primaryDeviceRetirementPending", I).GetValue(service) &&
                    (long)serviceType.GetField("renderedEpoch", I).GetValue(service) ==
                    (long)serviceType.GetProperty("NativeEpoch", I).GetValue(service),
                    transition + " did not finish post-primary camera retirement bookkeeping");
                Check(idle() && (int)querySize.GetValue(queries) == 0 && (int)jobCount.GetValue(batch) == 0,
                    transition + " device preparation changed native main-view query/batch ownership");
                Check(cameraTargetRetirements == 2 && (int)targets.GetType().GetProperty("Count", I).GetValue(targets) == 0,
                    transition + " did not retire both old camera targets in actual post-primary ClearTargets");
                Sequence("start", "retire-device", "prepare-device", "before", "primary", "complete", "close", "camera", "retire-camera", "retire-camera", "after");
            }

            foreach (bool returnNull in new[] { false, true })
            {
                ResetCase(); tracePreparation = true; failContextFactory = !returnNull; nullContextFactory = returnNull;
                nativeType.GetField("resourceDevice", I).SetValue(native, null); nativeType.GetField("contextToken", I).SetValue(native, null);
                Check(Failure(() => draw(mainTarget)) == null, "unsettled private context creation prevented healthy idle primary rendering");
                Check(beforeCalls == 0 && primaryBodies == 1 && closed == 0 && frontGeneration == 8,
                    "failed/null context creation entered a portal capture or changed the retained front");
                Check((bool)nativeType.GetProperty("DeviceChanged", I).GetValue(native) &&
                    nativeType.GetField("resourceDevice", I).GetValue(native) == null && nativeType.GetField("contextToken", I).GetValue(native) == null,
                    "failed/null context creation poisoned the next preparation fast path");
                Check(idle() && (MatrixD)matrices.GetType().GetField("InvViewD", I).GetValue(matrices) == expectedPose &&
                    (Matrix)matrices.GetType().GetField("Projection", I).GetValue(matrices) == expectedProjection &&
                    (Vector2I)viewport.GetValue(null) == new Vector2I(960, 600),
                    "failed/null device preparation changed primary camera/viewport/query/batch state");
                Sequence("start", "retire-device", "prepare-device", "primary", "complete", "camera", "retire-device", "after");

                // Retry through another exact scene call without repairing the
                // resourceDevice/contextToken fields in the fixture.
                events.Clear(); beforeCalls = afterCalls = starts = completed = closed = primaryBodies = secondaryBodies = cameraWork = 0;
                deviceRetirements = contextFactories = 0; failContextFactory = nullContextFactory = false; frame++;
                int beforeRetryEpoch = portalEpoch; SetCallback("BeforePrimaryWork", settledCapture);
                draw(mainTarget);
                Check(beforeCalls == 1 && contextFactories == 1 && deviceRetirements == 1 && portalEpoch == capturedDeviceEpoch && portalEpoch == beforeRetryEpoch + 1,
                    "failed/null preparation could not retry transactionally without an external state repair");
                Sequence("start", "retire-device", "prepare-device", "before", "primary", "complete", "close", "camera", "after");
            }

            Check(firstAssertion == null, "production callback exception handling swallowed an earlier fixture assertion: " + firstAssertion?.Message);

            Console.WriteLine("PASS: " + checks + " installed pre-primary boundary assertions: exact scene call, actual idle query/batch gates, native immutable pose, nested capture budget, success/failure ordering, identity/epoch/frame/device rejection and actual cold/replacement device preparation with transactional retry; no GPU frame.");
            Console.WriteLine("EVIDENCE: model/instance restoration, joined cull retirement, particle VS binding and native frame b0 upload retain their separate models/culling/particle/portal-projection executed fixtures.");
        }
        finally
        {
            serviceType.GetField("capturing", S).SetValue(null, 0); serviceType.GetField("portalCapturing", S).SetValue(null, 0);
            if (savedCamera != null) savedCamera.GetType().GetMethod("Restore", I).Invoke(savedCamera, null);
            viewport.SetValue(null, oldViewport); serviceType.GetField("epoch", I).SetValue(service, oldEpoch);
            serviceType.GetField("native", I).SetValue(service, null); ((IDisposable)service).Dispose(); terminal.UnpatchAll(terminal.Id);
            Manager = Scheduler = mainTarget = otherTarget = currentDevice = native = service = matrices = cpuScheduler = visibilityIdentity = fakeContextToken = null; draw = null;
        }
    }
    sealed class InjectedFailure : Exception { internal InjectedFailure(string message) : base(message) { } }
}
