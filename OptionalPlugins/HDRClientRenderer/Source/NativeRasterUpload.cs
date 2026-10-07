using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using VRageMath;

namespace HDRClientRenderer
{
    // Single-mip UI uploads execute on the same main-render boundary as other
    // producers, so their updated pixels share the actual render-frame budget.
    internal sealed class NativeRasterUpload : IDisposable
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        sealed class Request { internal string Target; internal int Width, Height; internal byte[] Data; internal long Order,Serial; }
        static NativeRasterUpload active;
        readonly object gate = new object();
        readonly Dictionary<string, Request> pending = new Dictionary<string, Request>();
        readonly int[] ready = new int[RasterStore.MaxSlots];
        readonly long[] serials = new long[RasterStore.MaxSlots];
        ClientPixelBudget budget;
        GpuPass pass;
        Harmony harmony;
        MethodInfo original, prefix;
        Action<string> report;
        bool attempted;
        long order, lastFrame = long.MinValue;
        int errors;
        internal bool Ready { get { return pass != null; } }
        internal void SetBudget(ClientPixelBudget value) { budget = value; }
        static int Slot(string target)
        { const string name = "HDR_ClientRaster_"; int slot; return target != null && target.StartsWith(name, StringComparison.Ordinal) && int.TryParse(target.Substring(name.Length), out slot) && slot >= 0 && slot < RasterStore.MaxSlots ? slot : -1; }
        static string Key(string target) { return "ui:" + target; }
        internal bool Enqueue(string target, int width, int height, byte[] data,long serial)
        {
            int slot = Slot(target);
            if (!Ready || slot < 0 || serial<0||width < 1 || height < 1 || width > RasterStore.MaxWidth || height > RasterStore.MaxHeight ||
                (long)width * height > RasterStore.MaxPixels || data == null || data.Length != (long)width * height * 4) return false;
            if(budget!=null&&(long)width*height>budget.PixelLimit)return false;
            lock (gate)
            {
                Request old; pending.TryGetValue(target, out old);
                int bytes=0;foreach(var item in pending.Values)bytes+=item.Data.Length;
                if(data.Length>RasterStore.MaxUploadBytesPerTick-bytes+(old==null?0:old.Data.Length))return false;
                if (budget != null && !budget.Request(Key(target), ClientPixelBudget.Kind.Ui, width * height)) return false;
                pending[target] = new Request { Target = target, Width = width, Height = height, Data = (byte[])data.Clone(),Serial=serial, Order = old == null ? ++order : old.Order };
                return true;
            }
        }
        internal bool TargetReady(string target,long serial) { int slot = Slot(target); return slot >= 0 && Interlocked.CompareExchange(ref ready[slot], 0, 0) == 1&&Interlocked.Read(ref serials[slot])==serial; }
        internal void Cancel(string target)
        {
            lock (gate) { pending.Remove(target); if (budget != null) budget.Cancel(Key(target)); int slot = Slot(target); if (slot >= 0) Interlocked.Exchange(ref ready[slot], 0); }
        }
        internal void Clear()
        {
            lock (gate)
            {
                foreach (var request in pending.Values) if (budget != null) budget.Cancel(Key(request.Target));
                pending.Clear(); for (int i = 0; i < ready.Length; i++) Interlocked.Exchange(ref ready[i], 0);
            }
        }
        internal void TryInstall(Action<string> log)
        {
            if (attempted) return;
            var renderer = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRageRender.MyRender11", false)).FirstOrDefault(t => t != null);
            if (renderer == null) return;
            attempted = true; report = log;
            try
            {
                pass = new GpuPass(renderer.Assembly); original = renderer.GetMethods(Static).Single(m => m.Name == "DrawGameScene" && m.GetParameters().Length == 2);
                prefix = typeof(NativeRasterUpload).GetMethod("BeforeScene", Static);
                harmony = new Harmony("HDRClientRenderer.NativeRasterUpload"); active = this;
                harmony.Patch(original, prefix: new HarmonyMethod(prefix) { priority = Priority.Last });
                log("UI upload queue installed; single-mip uploads share the main-render pixel budget.");
            }
            catch (Exception error) { Dispose(); log("UI upload queue unavailable: " + error.GetBaseException().Message); }
        }
        static void BeforeScene(object __0)
        {
            var uploader = active;
            if (uploader == null || uploader.pass == null || DirectCameraCapture.IsCapturing || !uploader.pass.IsMain(__0)) return;
            long frame = uploader.pass.Frame; if (frame == uploader.lastFrame) return; uploader.lastFrame = frame;
            lock (uploader.gate)
            {
                var requests = new List<Request>(uploader.pending.Values); requests.Sort((a, b) => a.Order.CompareTo(b.Order));
                int pixels = 0;
                foreach (var request in requests)
                {
                    int cost = request.Width * request.Height;
                    if (cost > RasterStore.MaxUploadBytesPerTick / 4 - pixels) continue;
                    if (uploader.budget != null && !uploader.budget.TrySpend(Key(request.Target), ClientPixelBudget.Kind.Ui, cost)) continue;
                    pixels += cost;
                    try
                    {
                        uploader.pass.Upload(request.Target, request.Width, request.Height, request.Data);
                        Interlocked.Exchange(ref uploader.serials[Slot(request.Target)],request.Serial);
                        Interlocked.Exchange(ref uploader.ready[Slot(request.Target)], 1);
                    }
                    catch (Exception error) {Interlocked.Exchange(ref uploader.ready[Slot(request.Target)],0); if (uploader.errors++ < 8) uploader.report("UI upload failed: " + error.GetBaseException().Message); }
                    uploader.pending.Remove(request.Target); if (uploader.budget != null) uploader.budget.Complete(Key(request.Target));
                }
            }
        }
        public void Dispose()
        {
            Clear(); if (ReferenceEquals(active, this)) active = null;
            if (harmony != null && original != null && prefix != null) try { harmony.Unpatch(original, prefix); } catch { }
            harmony = null; pass = null;
        }
        internal sealed class GpuPass
        {
            readonly PropertyInfo backbuffer, frame, size;
            readonly FieldInfo fileTextures;
            readonly FieldInfo description;
            readonly MethodInfo tryTexture, create, reset, destroy;
            internal GpuPass(Assembly assembly)
            {
                var renderer = assembly.GetType("VRageRender.MyRender11", true);
                var manager = assembly.GetType("VRage.Render11.Resources.MyFileTextureManager", true);
                var texture = assembly.GetType("VRage.Render11.Resources.IUserGeneratedTexture", true);
                backbuffer = renderer.GetProperty("Backbuffer", Static); frame = assembly.GetType("VRageRender.MyCommon", true).GetProperty("FrameCounter", Static);
                fileTextures = assembly.GetType("VRage.Render11.Common.MyManagers", true).GetField("FileTextures", Static);
                tryTexture = manager.GetMethod("TryGetTexture", Instance, null, new[] { typeof(string), texture.MakeByRefType() }, null);
                create = manager.GetMethods(Instance).Single(m => m.Name == "CreateGeneratedTexture" && m.GetParameters().Length == 7);
                reset = manager.GetMethod("ResetGeneratedTexture", Instance, null, new[] { typeof(string), typeof(byte[]) }, null);
                destroy = manager.GetMethod("DestroyGeneratedTexture", Instance, null, new[] { typeof(string) }, null);
                size = assembly.GetType("VRage.Render11.Resources.IResource", true).GetProperty("Size", Instance);
                description=assembly.GetType("VRage.Render11.Resources.Internal.MyGeneratedTexture",true).GetField("m_desc",Instance);
                if (new object[] { backbuffer, frame, fileTextures, tryTexture, create, reset, destroy, size,description }.Any(item => item == null))
                    throw new InvalidOperationException("UI upload renderer ABI changed: "+string.Join(", ",GetType().GetFields(Instance).Where(member=>typeof(MemberInfo).IsAssignableFrom(member.FieldType)&&member.GetValue(this)==null).Select(member=>member.Name)));
            }
            internal long Frame { get { return (long)frame.GetValue(null); } }
            internal bool IsMain(object target) { return target != null && ReferenceEquals(target, backbuffer.GetValue(null)); }
            internal void Upload(string target, int width, int height, byte[] data)
            {
                if(Slot(target)<0||width<1||height<1||(long)width*height>RasterStore.MaxPixels||data==null||data.Length!=(long)width*height*4)
                    throw new ArgumentException("Invalid UI upload.");
                var manager = fileTextures.GetValue(null); object[] found = { target, null };
                bool exists = (bool)tryTexture.Invoke(manager, found);
                if (exists && found[1] != null && ((Vector2I)size.GetValue(found[1])).Equals(new Vector2I(width, height)))
                {
                    if(!SingleMip(found[1],width,height))throw new InvalidOperationException("UI target is not a safe single-mip RGBA allocation.");
                    reset.Invoke(manager, new object[] { target, data });
                }
                else
                {
                    if (exists) destroy.Invoke(manager, new object[] { target });
                    var type = create.GetParameters()[3].ParameterType;
                    object created=create.Invoke(manager, new object[] { target, width, height, Enum.ToObject(type, 0), false, data, true });
                    if(created==null||!SingleMip(created,width,height))throw new InvalidOperationException("UI upload target was not created with a safe single-mip descriptor.");
                }
            }
            internal bool SingleMip(object target,int width,int height)
            {
                if(target==null)return false;
                object value=description.GetValue(target);var type=value.GetType();
                Func<string,int> field=name=>Convert.ToInt32(type.GetField(name).GetValue(value));
                object sample=type.GetField("SampleDescription").GetValue(value);var sampleType=sample.GetType();
                return field("Width")==width&&field("Height")==height&&field("MipLevels")==1&&field("ArraySize")==1&&field("Format")==29&&field("Usage")==0&&field("BindFlags")==8&&field("OptionFlags")==0&&
                    (int)sampleType.GetField("Count").GetValue(sample)==1&&(int)sampleType.GetField("Quality").GetValue(sample)==0;
            }
        }
    }
}
