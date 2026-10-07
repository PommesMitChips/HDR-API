using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace HDRClientRenderer
{
    // Owns private native queries, never the engine's player-occlusion pool. This
    // is usable only with a renderer that proves and draws a COMPLETE enclosure;
    // a phase observer or API signature check does not implement that renderer.
    internal sealed class DisplayOcclusionQueryNative : DisplayOcclusion.IBackend, IDisposable
    {
        internal interface IEnclosureRenderer
        {
            bool CurrentFrameOpaqueProof { get; }
            // Validate exact immutable primary view, opaque image, viewport,
            // geometry and phase. Reused G-buffer object identity is inadequate.
            bool Matches(DisplayOcclusion.Stamp stamp);
            // Draw the entire padded rectangle at closest reverse depth, >=,
            // all MSAA samples, no stencil/scissor rejection, no color/depth
            // writes. Restore affected state before returning, including errors.
            void Draw(object immediateContext, DisplayOcclusion.Stamp stamp, DisplayOcclusion.Footprint footprint);
        }
        const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags AllStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        readonly IEnclosureRenderer renderer;
        readonly ConstructorInfo create;
        readonly MethodInfo begin, end, result, release;
        readonly FieldInfo resource;
        readonly PropertyInfo context;
        readonly int ownerThread;
        readonly object gate = new object();
        readonly HashSet<Ticket> owned = new HashSet<Ticket>();
        readonly Queue<Ticket> retired = new Queue<Ticket>();
        bool disposed;

        internal DisplayOcclusionQueryNative(Assembly assembly, IEnclosureRenderer renderer)
        {
            if (assembly == null) throw new ArgumentNullException("assembly");
            this.renderer = renderer ?? throw new ArgumentNullException("renderer");
            var query = assembly.GetType("VRage.Render11.Culling.Occlusion.MyOcclusionQuery", true);
            var rc = assembly.GetType("VRage.Render11.RenderContext.MyRenderContext", true);
            create = query.GetConstructor(AllInstance, null, new[] { typeof(string) }, null);
            begin = query.GetMethod("Begin", AllInstance, null, new[] { rc }, null);
            end = query.GetMethod("End", AllInstance, null, new[] { rc }, null);
            result = query.GetMethod("GetResult", AllInstance, null, new[] { typeof(bool) }, null);
            resource = query.GetField("m_query", AllInstance);
            release = resource?.FieldType.GetMethod("Dispose", AllInstance, null, Type.EmptyTypes, null);
            context = assembly.GetType("VRageRender.MyRender11", true).GetProperty("RC", AllStatic);
            if (create == null || begin == null || begin.ReturnType != typeof(void) || end == null || end.ReturnType != typeof(void) ||
                result == null || result.ReturnType != typeof(long) || resource == null || release == null || context == null || context.PropertyType != rc)
                throw new InvalidOperationException("Display occlusion private-query ABI changed.");
            // Construct this adapter on the render owner thread. Its constructor
            // only inspects metadata; no native query or context is created/read.
            ownerThread = Thread.CurrentThread.ManagedThreadId;
        }
        public bool CurrentFrameOpaqueProof
        { get { lock (gate) { return !disposed && renderer.CurrentFrameOpaqueProof; } } }
        public DisplayOcclusion.IQuery Begin(string displayKey, DisplayOcclusion.Stamp stamp, DisplayOcclusion.Footprint footprint)
        {
            CheckOwner(); DrainRetired();
            lock (gate)
            {
                if (disposed || owned.Count >= DisplayOcclusion.MaxEntries || !stamp.Valid || !footprint.Valid(stamp) ||
                    !renderer.CurrentFrameOpaqueProof || !renderer.Matches(stamp)) return null;
                var rc = context.GetValue(null);
                if (rc == null) return null;
                object native = create.Invoke(new object[] { "HDR display enclosure" });
                var ticket = new Ticket(this, native, stamp); owned.Add(ticket);
                bool open = false;
                try
                {
                    begin.Invoke(native, new[] { rc }); open = true;
                    renderer.Draw(rc, stamp, footprint);
                    end.Invoke(native, new[] { rc }); open = false;
                    // The draw may have retired the primary frame. Reject it even
                    // if the private query later returns a completed zero count.
                    if (!renderer.CurrentFrameOpaqueProof || !renderer.Matches(stamp)) { Retire(ticket); return null; }
                    return ticket;
                }
                catch
                {
                    if (open) try { end.Invoke(native, new[] { rc }); } catch { }
                    Retire(ticket); throw;
                }
            }
        }
        sealed class Ticket : DisplayOcclusion.IQuery
        {
            internal readonly DisplayOcclusionQueryNative Owner;
            internal readonly DisplayOcclusion.Stamp Stamp;
            internal object Native;
            internal bool Retired;
            internal Ticket(DisplayOcclusionQueryNative owner, object native, DisplayOcclusion.Stamp stamp)
            { Owner = owner; Native = native; Stamp = stamp; }
            public bool TryGetSamples(out long samples)
            {
                Owner.CheckOwner(); samples = -1;
                lock (Owner.gate)
                {
                    if (Retired || Native == null || Owner.disposed || !Owner.renderer.CurrentFrameOpaqueProof || !Owner.renderer.Matches(Stamp)) return false;
                    samples = (long)Owner.result.Invoke(Native, new object[] { false });
                    return samples >= 0;
                }
            }
            public void Dispose() { Owner.Retire(this); }
        }
        void CheckOwner()
        { if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("Display occlusion GPU access requires its render owner thread."); }
        void Retire(Ticket ticket)
        {
            lock (gate) { if (ticket.Retired) return; ticket.Retired = true; retired.Enqueue(ticket); }
            if (Thread.CurrentThread.ManagedThreadId == ownerThread) DrainRetired();
        }
        // Game-thread invalidations only queue COM resource retirement. The next
        // render boundary drains it without querying results, flushing or waiting.
        internal void DrainRetired()
        {
            CheckOwner();
            lock (gate)
            {
                while (retired.Count > 0)
                {
                    var ticket = retired.Dequeue(); var native = ticket.Native; ticket.Native = null;
                    try { var gpu = resource.GetValue(native); if (gpu != null) release.Invoke(gpu, null); }
                    finally { owned.Remove(ticket); }
                }
            }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (!disposed)
                {
                    disposed = true;
                    foreach (var ticket in owned) if (!ticket.Retired) { ticket.Retired = true; retired.Enqueue(ticket); }
                }
            }
            if (Thread.CurrentThread.ManagedThreadId == ownerThread) DrainRetired();
        }
    }
}
