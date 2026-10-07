using System;
using System.Collections.Generic;

namespace HDRClientRenderer
{
    // Only a complete, current-frame, opaque-depth enclosure can remove demand.
    // The shipping renderer observer has no such backend: its fallback is explicit.
    internal sealed class DisplayOcclusion : IDisposable
    {
        internal const int MaxEntries = 64, MaxQueriesPerFrame = 16;
        internal enum Phase { Unknown, BeforeMainScene, PrimaryOpaqueCompleted, MainSceneCompleted, OpaqueProofReady }
        internal enum Status { Disabled, Unavailable, BeforeOpaqueDepth, InvalidEvidence, Pending, Visible, Occluded, Stale, BudgetExceeded, Faulted, Disposed }
        internal struct Stamp
        {
            internal readonly long Epoch, Frame;
            internal readonly int Width, Height;
            // These tokens identify immutable exact view, mesh/pose/crop, and
            // opaque-depth snapshots, not lossy hashes or reused GPU resources.
            internal readonly object View, Geometry, OpaqueDepth;
            internal Stamp(long epoch, long frame, int width, int height, object view, object geometry, object opaqueDepth)
            { Epoch = epoch; Frame = frame; Width = width; Height = height; View = view; Geometry = geometry; OpaqueDepth = opaqueDepth; }
            internal bool Valid { get { return Epoch > 0 && Frame >= 0 && Width > 0 && Height > 0 && Width <= 16384 && Height <= 16384 && View != null && Geometry != null && OpaqueDepth != null; } }
            internal bool SameFrame(Stamp other)
            { return Epoch == other.Epoch && Frame == other.Frame && Width == other.Width && Height == other.Height && ReferenceEquals(View, other.View) && ReferenceEquals(OpaqueDepth, other.OpaqueDepth); }
            internal bool Same(Stamp other) { return SameFrame(other) && ReferenceEquals(Geometry, other.Geometry); }
        }
        internal struct Footprint
        {
            internal readonly int Left, Top, Right, Bottom;
            internal readonly double ClosestReverseDepth;
            internal readonly bool Complete;
            internal Footprint(int left, int top, int right, int bottom, double closestReverseDepth, bool complete)
            { Left = left; Top = top; Right = right; Bottom = bottom; ClosestReverseDepth = closestReverseDepth; Complete = complete; }
            internal bool Valid(Stamp stamp)
            { return Complete && Left >= 0 && Top >= 0 && Right > Left && Bottom > Top && Right <= stamp.Width && Bottom <= stamp.Height && !double.IsNaN(ClosestReverseDepth) && !double.IsInfinity(ClosestReverseDepth) && ClosestReverseDepth >= 0 && ClosestReverseDepth <= 1; }
            internal long Pixels { get { return (long)(Right - Left) * (Bottom - Top); } }
            internal bool Same(Footprint other)
            { return Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom && ClosestReverseDepth == other.ClosestReverseDepth && Complete == other.Complete; }
        }
        internal struct Decision
        {
            internal readonly Status State;
            internal bool KeepDemand { get { return State != Status.Occluded; } }
            internal Decision(Status state) { State = state; }
        }
        internal interface IQuery : IDisposable
        {
            // A native implementation must call GetResult(false), never stall or
            // flush. Negative counts and an incomplete result do not prove zero.
            bool TryGetSamples(out long samples);
            // Disposal must be nonblocking and queue owner-thread retirement;
            // the policy may invalidate evidence from the game thread.
        }
        internal interface IBackend
        {
            // Certifies a complete conservative proxy against primary opaque
            // depth, with depth reads only and color/depth writes disabled. The
            // immutable image/view/geometry tokens must match the submitted frame.
            bool CurrentFrameOpaqueProof { get; }
            IQuery Begin(string displayKey, Stamp stamp, Footprint completeEnclosure);
        }
        sealed class Entry
        {
            internal Stamp Stamp;
            internal Footprint Footprint;
            internal IQuery Query;
            internal bool Polled;
            internal Status State = Status.Pending;
        }
        readonly object gate = new object();
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly IBackend backend;
        long epoch = 1, frame = -1, submittedPixels;
        int width, height, submitted;
        object mainIdentity;
        Stamp opaqueStamp;
        Phase phase;
        bool enabled, disposed;
        internal DisplayOcclusion(IBackend backend = null) { this.backend = backend; }
        internal long Epoch { get { lock (gate) return epoch; } }
        internal Phase CurrentPhase { get { lock (gate) return phase; } }
        internal int PendingCount { get { lock (gate) return entries.Count; } }
        internal bool Supported { get { lock (gate) return !disposed && BackendSupported(); } }
        internal string StatusText
        {
            get
            {
                lock (gate)
                {
                    if (disposed) return "Display occlusion unavailable: observer disposed; capture demand retained.";
                    if (!enabled) return "Display occlusion disabled; capture demand retained.";
                    if (!BackendSupported()) return "Display occlusion unavailable: capture precedes current main opaque-depth proof; capture demand retained.";
                    if (phase != Phase.OpaqueProofReady) return "Display occlusion unavailable for this phase: current opaque-depth proof is not ready; capture demand retained.";
                    return "Display occlusion proof backend available for this frame; pending or mismatched evidence retains capture demand.";
                }
            }
        }
        bool BackendSupported() { try { return backend != null && backend.CurrentFrameOpaqueProof; } catch { return false; } }
        internal void SetEnabled(bool value)
        { lock (gate) { if (enabled != value) { ClearEntries(); phase = Phase.Unknown; } enabled = value; } }
        internal void ObserveMainStarting(long number, int viewportWidth, int viewportHeight, object gbufferIdentity)
        {
            lock (gate)
            {
                if (disposed) return;
                if (number < 0 || viewportWidth <= 0 || viewportHeight <= 0 || viewportWidth > 16384 || viewportHeight > 16384 || gbufferIdentity == null || number < frame)
                { ClearEntries(); phase = Phase.Unknown; return; }
                bool newFrame = number != frame;
                // Repeated primary draws invalidate prior depth without renewing
                // the per-frame query/pixel allowance.
                ClearEntries();
                frame = number; width = viewportWidth; height = viewportHeight; mainIdentity = gbufferIdentity;
                if (newFrame) { submitted = 0; submittedPixels = 0; }
                opaqueStamp = default(Stamp); phase = Phase.BeforeMainScene;
            }
        }
        internal void ObserveMainCompleted(long number, object gbufferIdentity, bool succeeded)
        {
            lock (gate)
            {
                if (disposed) return;
                // Scene completion is a phase observation, not opaque-depth proof.
                if (!succeeded || number != frame || !ReferenceEquals(mainIdentity, gbufferIdentity))
                { ClearEntries(); phase = Phase.Unknown; return; }
                if (phase != Phase.OpaqueProofReady) phase = Phase.MainSceneCompleted;
            }
        }
        internal void ObservePrimaryOpaqueCompleted(long number, object gbufferIdentity)
        {
            lock (gate)
            {
                if (disposed) return;
                if (phase != Phase.BeforeMainScene || number != frame || !ReferenceEquals(mainIdentity, gbufferIdentity))
                { ClearEntries(); phase = Phase.Unknown; return; }
                phase = Phase.PrimaryOpaqueCompleted;
            }
        }
        internal bool MarkOpaqueProofReady(Stamp stamp)
        {
            lock (gate)
            {
                if (disposed || !enabled || !BackendSupported() || (phase != Phase.MainSceneCompleted && phase != Phase.PrimaryOpaqueCompleted) || !stamp.Valid || stamp.Epoch != epoch || stamp.Frame != frame || stamp.Width != width || stamp.Height != height) return false;
                opaqueStamp = stamp; phase = Phase.OpaqueProofReady; return true;
            }
        }
        // Submit proxies after current opaque depth, then consume once at a later
        // render seam in the SAME frame. Submission does not immediately exhaust
        // the single nonblocking poll while the GPU is still drawing the proxy.
        internal Decision Submit(string displayKey, Stamp stamp, Footprint footprint)
        { return Evaluate(displayKey, stamp, footprint, false); }
        internal Decision Probe(string displayKey, Stamp stamp, Footprint footprint)
        { return Evaluate(displayKey, stamp, footprint, true); }
        Decision Evaluate(string displayKey, Stamp stamp, Footprint footprint, bool consume)
        {
            lock (gate)
            {
                if (disposed) return new Decision(Status.Disposed);
                if (!enabled) return new Decision(Status.Disabled);
                if (!BackendSupported()) { ClearEntries(); return new Decision(Status.Unavailable); }
                if (phase != Phase.OpaqueProofReady) return new Decision(Status.BeforeOpaqueDepth);
                if (string.IsNullOrEmpty(displayKey) || displayKey.Length > 128 || !stamp.Valid || !footprint.Valid(stamp)) return new Decision(Status.InvalidEvidence);
                if (stamp.Epoch != epoch || !opaqueStamp.SameFrame(stamp)) return new Decision(Status.Stale);
                Entry entry;
                if (entries.TryGetValue(displayKey, out entry) && (!entry.Stamp.Same(stamp) || !entry.Footprint.Same(footprint)))
                { Release(entry); entries.Remove(displayKey); entry = null; }
                if (entry == null)
                {
                    if (entries.Count >= MaxEntries || submitted >= MaxQueriesPerFrame || footprint.Pixels > (long)width * height - submittedPixels) return new Decision(Status.BudgetExceeded);
                    submitted++; submittedPixels += footprint.Pixels;
                    try
                    {
                        var query = backend.Begin(displayKey, stamp, footprint);
                        if (query == null) return new Decision(Status.Faulted);
                        entry = new Entry { Stamp = stamp, Footprint = footprint, Query = query };
                        entries.Add(displayKey, entry);
                    }
                    catch { return new Decision(Status.Faulted); }
                }
                if (!consume || entry.Polled) return new Decision(entry.State);
                entry.Polled = true;
                try
                {
                    long samples;
                    if (!entry.Query.TryGetSamples(out samples) || samples < 0) entry.State = Status.Pending;
                    else entry.State = samples == 0 ? Status.Occluded : Status.Visible;
                }
                catch { entry.State = Status.Faulted; }
                return new Decision(entry.State);
            }
        }
        internal void Forget(string displayKey)
        { if (displayKey == null) return; lock (gate) { Entry entry; if (entries.TryGetValue(displayKey, out entry)) { Release(entry); entries.Remove(displayKey); } } }
        internal void NewEpoch()
        { lock (gate) { ClearEntries(); epoch = checked(epoch + 1); frame = -1; width = height = submitted = 0; submittedPixels = 0; mainIdentity = null; opaqueStamp = default(Stamp); phase = Phase.Unknown; } }
        static void Release(Entry entry) { try { entry.Query.Dispose(); } catch { } }
        void ClearEntries() { foreach (var entry in entries.Values) Release(entry); entries.Clear(); }
        public void Dispose() { lock (gate) { if (disposed) return; disposed = true; ClearEntries(); phase = Phase.Unknown; } }
    }
}
