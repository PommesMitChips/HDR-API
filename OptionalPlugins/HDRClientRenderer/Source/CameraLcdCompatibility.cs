using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using VRageRender;

namespace HDRClientRenderer
{
    // Direct capture isolation and optional locally installed CameraLCD interop. Harmony is supplied
    // by its client loader, not by the Workshop mod or any server/network payload.
    internal sealed class CameraLcdCompatibility : IDisposable
    {
        private const string PatchId = "HDRClientRenderer.CameraCaptureIsolation";
        private static Func<bool> isCapturing;
        private static Action<string> report;
        private static int reported;
        private Harmony harmony;
        private MethodInfo hook;
        private MethodBase original;
        private bool attempted;
        private MethodInfo foreignPrefix,foreignGuard,foreignWork,foreignWorkGuard;
        private Harmony foreignHarmony;
        private bool foreignAttempted;
        internal bool Ready { get { return harmony != null&&(LoadedType("CameraLCD.Patches.Patch_MyRender11")==null||foreignHarmony!=null); } }

        private static Type LoadedType(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            return null;
        }

        internal void TryInstall(Action<string> log)
        {
            TryInstallForeignGuard(log);
            if (attempted) return;
            var camera = LoadedType("CameraLCD.CameraViewRenderer");
            var renderer = LoadedType("VRageRender.MyBillboardRenderer");
            if (renderer == null) return;
            attempted = true;
            try
            {
                var property = camera == null ? null : camera.GetProperty("IsDrawing", BindingFlags.Public | BindingFlags.Static);
                if (camera != null && (property == null || property.PropertyType != typeof(bool)))
                    throw new InvalidOperationException("Optional CameraLCD capture marker unavailable.");
                original = renderer.GetMethod("PrepareList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (original == null || ((MethodInfo)original).ReturnType != typeof(int))
                    throw new InvalidOperationException("Renderer billboard preparation ABI changed.");
                hook = typeof(CameraLcdCompatibility).GetMethod("CaptureTranspiler",
                    BindingFlags.NonPublic | BindingFlags.Static);
                harmony = new Harmony(PatchId);
                isCapturing = property == null ? null : (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), property.GetGetMethod());
                report = log; reported = 0;
                // Patch the caller, not its trivial getter: an already inlined getter
                // would otherwise escape a getter-only patch in the game's JIT code.
                harmony.Patch(original, transpiler: new HarmonyMethod(hook));
                log("Camera capture isolation installed; HDR displays excluded from direct and optional CameraLCD camera billboards.");
            }
            catch (Exception ex)
            {
                Dispose();
                log("Camera capture isolation unavailable: " + ex.GetBaseException().Message);
            }
        }
        internal void TryInstallForeignGuard(Action<string> log)
        {
            if(foreignAttempted)return;
            var type=LoadedType("CameraLCD.Patches.Patch_MyRender11");if(type==null)return;
            foreignAttempted=true;
            try
            {
                const BindingFlags flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
                foreignPrefix=type.GetMethod("MyRender11_DrawGameScene_Prefix",flags,null,Type.EmptyTypes,null);
                if(foreignPrefix==null||foreignPrefix.GetMethodBody()==null)throw new InvalidOperationException("CameraLCD scene prefix ABI changed.");
                var manager=LoadedType("CameraLCD.CameraLcdManager");
                foreignWork=manager==null?null:manager.GetMethod("Draw",flags,null,Type.EmptyTypes,null);
                if(foreignWork==null||foreignWork.ReturnType!=typeof(bool)||foreignWork.GetMethodBody()==null)
                    throw new InvalidOperationException("CameraLCD capture work ABI changed.");
                foreignGuard=typeof(CameraLcdCompatibility).GetMethod("AllowForeignCameraPrefix",flags);
                foreignWorkGuard=typeof(CameraLcdCompatibility).GetMethod("AllowForeignCameraWork",flags);
                foreignHarmony=new Harmony(PatchId+".ForeignSceneGuard");
                // The small foreign prefix can already be inlined in a Release
                // scene wrapper. Guard its large, try/finally capture work boundary
                // too, before it mutates the shared native renderer scheduler.
                foreignHarmony.Patch(foreignWork,prefix:new HarmonyMethod(foreignWorkGuard){priority=Priority.First});
                foreignHarmony.Patch(foreignPrefix,prefix:new HarmonyMethod(foreignGuard){priority=Priority.First});
                log("CameraLCD capture-work and scene-prefix guards installed; nested CameraLCD captures excluded only during HDR native capture.");
            }
            catch(Exception error)
            {
                if(foreignHarmony!=null&&foreignPrefix!=null&&foreignGuard!=null)try{foreignHarmony.Unpatch(foreignPrefix,foreignGuard);}catch{}
                if(foreignHarmony!=null&&foreignWork!=null&&foreignWorkGuard!=null)try{foreignHarmony.Unpatch(foreignWork,foreignWorkGuard);}catch{}
                foreignHarmony=null;
                log("CameraLCD scene-prefix guard unavailable: "+error.GetBaseException().Message);
            }
        }
        private static bool AllowForeignCameraPrefix(){return !DirectCameraCapture.IsCapturing;}
        private static bool AllowForeignCameraWork(ref bool __result)
        {
            if(!DirectCameraCapture.IsCapturing)return true;
            __result=false;
            return false;
        }

        private static IEnumerable<CodeInstruction> CaptureTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            var getter = typeof(MyRenderProxy).GetProperty("BillboardsRead").GetGetMethod();
            var replacement = typeof(CameraLcdCompatibility).GetMethod("CaptureBillboards",
                BindingFlags.NonPublic | BindingFlags.Static);
            var snapshot=generator.DeclareLocal(typeof(List<MyBillboard>));
            int matches = 0;var rewritten=new List<CodeInstruction>();
            foreach(var instruction in code)
            {
                if(!instruction.Calls(getter)){rewritten.Add(instruction);continue;}
                if(matches++==0)
                {
                    instruction.opcode=OpCodes.Call;instruction.operand=replacement;rewritten.Add(instruction);
                    rewritten.Add(new CodeInstruction(OpCodes.Dup));
                    rewritten.Add(new CodeInstruction(OpCodes.Stloc,snapshot));
                }
                else
                {
                    // PrepareList counts buckets on its first read, then fills them
                    // on the second. Reuse one list even if capture/disposal changes.
                    instruction.opcode=OpCodes.Ldloc;instruction.operand=snapshot;rewritten.Add(instruction);
                }
            }
            if (matches != 2) throw new InvalidOperationException("Expected two renderer billboard-list reads; isolation patch not installed.");
            return rewritten;
        }

        private static List<MyBillboard> CaptureBillboards()
        {
            var source = MyRenderProxy.BillboardsRead;
            var capture = isCapturing;
            if (!DirectCameraCapture.IsCapturing && (capture == null || !capture())) return source;
            var filtered = CaptureIsolation.Filter(source, true, billboard => billboard.Material.String);
            if (!ReferenceEquals(filtered, source) && Interlocked.Exchange(ref reported, 1) == 0)
            {
                var log = report;
                if (log != null) log("Camera capture isolation active: HDR billboard displays removed from secondary view only.");
            }
            return filtered;
        }

        public void Dispose()
        {
            if(foreignHarmony!=null&&foreignPrefix!=null&&foreignGuard!=null)try{foreignHarmony.Unpatch(foreignPrefix,foreignGuard);}catch{}
            if(foreignHarmony!=null&&foreignWork!=null&&foreignWorkGuard!=null)try{foreignHarmony.Unpatch(foreignWork,foreignWorkGuard);}catch{}
            foreignHarmony=null;
            isCapturing = null; report = null;
            if (harmony != null && original != null && hook != null)
                try { harmony.Unpatch(original, hook); } catch { }
            harmony = null;
        }
    }
}
