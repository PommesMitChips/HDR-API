using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VRageMath;

static class CaptureCullingFixture
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static object Manager, Scheduler;
    static int done, resets, jobsExecuted;
    static object cpuScheduler;
    static bool SchedulerService(ref object __result) { __result = cpuScheduler; return false; }
    static Type Engine(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null);
    static object New(Type type, params object[] args) => Activator.CreateInstance(type, Instance, null, args, null);
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static bool StopDone() { done++; return false; }
    static bool QuietFixtureLog() => false;
    static void CountReset() { resets++; }
    static void Invoke(object owner, string method) => owner.GetType().GetMethod(method, Instance).Invoke(owner, null);

    internal static void Run(Assembly plugin)
    {
        var serviceType = plugin.GetType("HDRClientRenderer.DirectCameraCapture", true);
        object service = New(serviceType);
        var terminal = new Harmony("HDR.Tests.CaptureCullingRetirement");
        try
        {
            serviceType.GetMethod("TryInstall", Instance).Invoke(service, new object[] { (Action<string>)Console.WriteLine });
            Assert((bool)serviceType.GetProperty("Ready", Instance).GetValue(service), "Actual capture hooks failed to install.");
            object native = serviceType.GetField("native", Instance).GetValue(service);
            var nativeType = native.GetType();
            cpuScheduler = DispatchProxy.Create(Engine("ParallelTasks.IWorkScheduler"), typeof(CaptureFixtureScheduler));
            terminal.Patch(Engine("ParallelTasks.Parallel").GetMethod("get_Scheduler", Static),
                prefix: new HarmonyMethod(typeof(CaptureCullingFixture).GetMethod(nameof(SchedulerService), Static)));
            Manager = New(Engine("VRage.Render11.Culling.MyCullManager"), (object)null);
            Scheduler = RuntimeHelpers.GetUninitializedObject(Engine("VRage.Render11.Render.MyRenderScheduler"));
            var batchType = Engine("ParallelTasks.DependencyBatch");
            object batch = New(batchType, Enum.Parse(Engine("ParallelTasks.WorkPriority"), "Normal"));
            Scheduler.GetType().GetField("m_batch", Instance).SetValue(Scheduler, batch);
            nativeType.GetField("cullManager", Instance).SetValue(native, typeof(CaptureCullingFixture).GetField(nameof(Manager), Static));
            nativeType.GetField("renderScheduler", Instance).SetValue(native, typeof(CaptureCullingFixture).GetField(nameof(Scheduler), Static));
            var doneMethod = (MethodInfo)nativeType.GetField("CullDone", Instance).GetValue(native);
            var resetMethod = (MethodInfo)nativeType.GetField("CullReset", Instance).GetValue(native);
            terminal.Patch(doneMethod, prefix: new HarmonyMethod(typeof(CaptureCullingFixture).GetMethod(nameof(StopDone), Static)) { priority = Priority.Last });
            terminal.Patch(resetMethod, prefix: new HarmonyMethod(typeof(CaptureCullingFixture).GetMethod(nameof(CountReset), Static)));
            object queries = Manager.GetType().GetMethod("GetCullQueries", Instance).Invoke(Manager, null);
            var size = queries.GetType().GetProperty("Size", Instance);
            var queryArray = queries.GetType().GetField("CullQueries", Instance);
            var addView = queries.GetType().GetMethod("AddView", Instance);
            Action appendMain = () => addView.Invoke(queries, new object[] {
                Enum.ToObject(addView.GetParameters()[0].ParameterType, 1), 0,
                new BoundingFrustumD(MatrixD.Identity), new BoundingFrustumD(MatrixD.Identity), Matrix.Identity, Vector3D.Zero, null, null });
            Func<bool> idle = () => (bool)nativeType.GetProperty("FrameIdle", Instance).GetValue(native);
            Assert(idle(), "Fresh actual cull query/batch owners are not idle.");
            appendMain(); appendMain();
            var two = (Array)queryArray.GetValue(queries);
            Assert((int)size.GetValue(queries) == 2 &&
                Equals(two.GetValue(0).GetType().GetField("ViewId", Instance).GetValue(two.GetValue(0)),
                    two.GetValue(1).GetType().GetField("ViewId", Instance).GetValue(two.GetValue(1))),
                "Native AddView did not reproduce duplicate view-zero query accumulation.");
            Assert(!idle() && (int)size.GetValue(queries) == 2, "Idle gate discarded preexisting foreign cull work.");
            resetMethod.Invoke(Manager, null);
            Console.WriteLine("REPRODUCED: actual engine AddView appends two main ViewId0 queries; capture's idle gate preserves and refuses that foreign baseline.");

            serviceType.GetField("capturing", Static).SetValue(null, 1);
            foreach (int failureStage in new[] { 0, 1, 2 })
            {
                foreach (string marker in new[] { "CullingFinished", "CullingReset", "CullingDoneAttempted" }) nativeType.GetField(marker, Instance).SetValue(native, false);
                done = resets = 0; appendMain();
                // Stage0 mirrors a failed Execute before scheduler Done. Stage1
                // fails after Done, before the scene tail; stage2 is normal tail.
                if (failureStage >= 1) doneMethod.Invoke(Manager, null);
                if (failureStage >= 2) resetMethod.Invoke(Manager, null);
                Invoke(native, "FinishCaptureCulling"); Invoke(native, "FinishCaptureCulling");
                Assert((int)size.GetValue(queries) == 0 && done == 1 && resets == 1,
                    "Capture cleanup left queries or returned pooled work twice at stage " + failureStage + ".");
                serviceType.GetField("capturing", Static).SetValue(null, 0);
                appendMain();
                Assert((int)size.GetValue(queries) == 1, "Main view inherited a stale capture query after stage " + failureStage + ".");
                resetMethod.Invoke(Manager, null);
                serviceType.GetField("capturing", Static).SetValue(null, 1);
                Console.WriteLine("PASS: actual query reset after capture stage " + failureStage + " leaves exactly one next-main query; native Done/Reset hooks prevent duplicate finalization.");
            }
            var add = batchType.GetMethod("Add", Instance);
            jobsExecuted = 0;
            add.Invoke(batch, new object[] { (Action)(() => jobsExecuted++) });
            Assert(!idle(), "Idle gate accepted a pending scheduler batch.");
            nativeType.GetField("SchedulerEntered", Instance).SetValue(native, true);
            try { Invoke(native, "CancelUnstartedCaptureJobs"); throw new Exception("An entered nonempty scheduler was cancelled."); }
            catch (TargetInvocationException ex) { Assert(ex.GetBaseException().Message.Contains("unfinished jobs"), "Unexpected scheduler cancellation failure."); }
            Assert((int)batchType.GetField("m_jobCount", Instance).GetValue(batch) == 1, "Entered scheduler jobs were discarded.");
            nativeType.GetField("SchedulerEntered", Instance).SetValue(native, false);
            Invoke(native, "CancelUnstartedCaptureJobs");
            Assert(idle() && jobsExecuted == 0, "Unstarted capture work was executed or retained during cancellation.");
            Console.WriteLine("PASS: real DependencyBatch unstarted work is cancelled without execution; entered work is never cleared by capture cleanup; no GPU frame.");
            // Exercise the actual native batch's synchronous worker/exception
            // path too. Its CPU scheduler has zero external threads; no game
            // task or render job is ever submitted by this fixture.
            foreach (var log in Engine("VRage.Library.Utils.MyDefaultLogInject").GetMethods(Static)
                .Where(m => m.Name == "WriteLine" && m.ReturnType == typeof(void) && m.GetMethodBody() != null))
                terminal.Patch(log, prefix: new HarmonyMethod(typeof(CaptureCullingFixture).GetMethod(nameof(QuietFixtureLog), Static)));
            foreach (string marker in new[] { "CullingFinished", "CullingReset", "CullingDoneAttempted" }) nativeType.GetField(marker, Instance).SetValue(native, false);
            done = resets = jobsExecuted = 0; appendMain();
            add.Invoke(batch, new object[] { (Action)(() => { jobsExecuted++; throw new InvalidOperationException("Injected capture worker failure."); }) });
            nativeType.GetField("SchedulerEntered", Instance).SetValue(native, true);
            try { batchType.GetMethod("Execute", Instance).Invoke(batch, null); throw new Exception("Real dependency batch did not propagate its task failure."); }
            catch (TargetInvocationException ex) { Assert(ex.GetBaseException().GetType().FullName == "ParallelTasks.TaskException", "Expected the actual engine TaskException."); }
            Assert(jobsExecuted == 1 && (int)batchType.GetField("m_jobCount", Instance).GetValue(batch) == 0, "Real Execute did not finish/clear the failed worker before returning.");
            Invoke(native, "CancelUnstartedCaptureJobs"); Invoke(native, "FinishCaptureCulling");
            Assert(done == 1 && resets == 1 && (int)size.GetValue(queries) == 0, "Task failure did not retire its capture query exactly once.");
            serviceType.GetField("capturing", Static).SetValue(null, 0); appendMain();
            Assert((int)size.GetValue(queries) == 1, "Task failure leaked a duplicate query into the next main view.");
            resetMethod.Invoke(Manager, null);
            Console.WriteLine("PASS: actual DependencyBatch.Execute propagates an injected worker TaskException after joined cleanup; production cull retirement leaves one next-main view-zero query.");
        }
        finally
        {
            serviceType.GetField("capturing", Static).SetValue(null, 0);
            serviceType.GetField("native", Instance).SetValue(service, null);
            ((IDisposable)service).Dispose(); terminal.UnpatchAll(terminal.Id); Manager = Scheduler = cpuScheduler = null;
        }
    }
}

public class CaptureFixtureScheduler : DispatchProxy
{
    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (method.Name == "get_ThreadCount") return 0;
        throw new Exception("Capture lifecycle fixture unexpectedly launched scheduler work: " + method.Name);
    }
}
