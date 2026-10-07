using System;
using System.Collections.Generic;

namespace HDRClientRenderer
{
    // Pure scheduling policy. The client adapter supplies authorized, visible demand;
    // render work is selected only once for each actual main-view frame.
    internal sealed class DirectCameraCapturePolicy
    {
        internal const int MaxSources = 6, MaxCapturesPerFrame = 3, MaxResolution = 2048;
        internal const double MaxRate = 30, DemandLifetime = 0.5, SnapshotLifetime = 1;

        internal sealed class Source
        {
            internal readonly long Camera;
            internal long Generation;
            internal double Rate, FovDegrees, DemandUntil, LastAttempt = double.NegativeInfinity;
            internal int Resolution;
            internal int NativeTile, NativePixels;
            internal object Payload, Completed;
            internal bool CaptureDemand = true;
            internal double CompletedAt;
            internal Source(long camera) { Camera = camera; }
        }

        readonly Dictionary<long, Source> sources = new Dictionary<long, Source>();
        readonly List<long> order = new List<long>();
        long generation, lastFrame = long.MinValue;
        int cursor;
        internal double RateCeiling = MaxRate;
        internal bool RetainCompletedWhileLive;

        internal int Count { get { return sources.Count; } }
        internal Source Find(long camera)
        { Source source; return sources.TryGetValue(camera, out source) ? source : null; }
        internal Source[] All()
        { var result = new Source[order.Count]; for (int i = 0; i < result.Length; i++) result[i] = sources[order[i]]; return result; }

        internal Source Request(long camera, double fovDegrees, int resolution, double rate,
            double now, object payload)
        {
            if (camera <= 0 || !Finite(now) || !Finite(fovDegrees) || fovDegrees < 60 ||
                fovDegrees > 120 || !SupportedResolution(resolution) ||
                !Finite(rate) || rate <= 0 || payload == null) return null;
            Source source;
            if (!sources.TryGetValue(camera, out source))
            {
                if (sources.Count >= MaxSources) return null;
                source = new Source(camera) { Generation = ++generation };
                sources.Add(camera, source); order.Add(camera);
            }
            else if (source.Resolution != resolution || source.FovDegrees != fovDegrees)
            {
                source.Generation = ++generation; source.Completed = null;
                // Configuration changes do not bypass the source's 30 Hz limit.
            }
            source.FovDegrees = fovDegrees; source.Resolution = resolution;
            source.Rate = RateCeiling > 0 ? Math.Min(rate,RateCeiling) : rate; source.DemandUntil = now + DemandLifetime;
            source.Payload = payload;
            source.CaptureDemand = true;
            return source;
        }

        internal Source[] Select(double now, long mainFrame, bool mainView, bool capturing)
        {
            if (!mainView || capturing || !Finite(now) || mainFrame == lastFrame)
                return new Source[0];
            lastFrame = mainFrame;
            var result = new List<Source>(MaxCapturesPerFrame);
            int count = order.Count, visited = 0;
            while (visited++ < count && result.Count < MaxCapturesPerFrame)
            {
                if (cursor >= order.Count) cursor = 0;
                if (order.Count == 0) break;
                var source = sources[order[cursor++]];
                if (!source.CaptureDemand || source.DemandUntil < now || now - source.LastAttempt + 1e-9 < 1 / source.Rate)
                    continue;
                source.LastAttempt = now;
                result.Add(source);
            }
            return result.ToArray();
        }
        // Native selection must inspect every bounded eligible source because the
        // shared ledger can grant a source outside an arbitrary first-three group.
        // Denied work consumes neither its rate timestamp nor a scene-pass slot.
        internal Source[] Candidates(double now, long mainFrame, bool mainView, bool capturing)
        {
            if (!mainView || capturing || !Finite(now) || mainFrame == lastFrame) return new Source[0];
            lastFrame = mainFrame;
            var result = new List<Source>(MaxSources);
            for(int i=0;i<order.Count;i++) { var source=sources[order[(cursor+i)%order.Count]]; if (source.CaptureDemand&&source.DemandUntil>=now&&(source.NativeTile>0||Eligible(source, now))) result.Add(source); }
            if(order.Count>0)cursor=(cursor+1)%order.Count;
            return result.ToArray();
        }
        internal bool Eligible(Source source, double now)
        { return source != null && source.CaptureDemand && source.DemandUntil >= now && source.Rate > 0 &&
            now - source.LastAttempt + 1e-9 >= 1 / source.Rate; }
        internal bool RecordAttempt(Source source, long expectedGeneration, double now)
        { if (!Live(source, expectedGeneration, now) || !Eligible(source, now)) return false; source.LastAttempt = now; return true; }

        internal bool Live(Source source, long expectedGeneration, double now)
        {
            return source != null && ReferenceEquals(Find(source.Camera), source) &&
                source.Generation == expectedGeneration && source.DemandUntil >= now;
        }

        internal bool Complete(Source source, long expectedGeneration, object completed, double now)
        {
            if (!Live(source, expectedGeneration, now) || completed == null) return false;
            source.Completed = completed; source.CompletedAt = now; return true;
        }

        internal bool Valid(long camera, long expectedGeneration, double now)
        {
            var source = Find(camera);
            return Live(source, expectedGeneration, now) && source.Completed != null &&
                (RetainCompletedWhileLive || now - source.CompletedAt <= SnapshotLifetime);
        }

        internal void Invalidate(Source source)
        {
            if (source == null || !ReferenceEquals(Find(source.Camera), source)) return;
            source.Generation = ++generation; source.Completed = null;
        }
        internal void Warm(long camera, double now)
        {
            var source = Find(camera);
            if (source == null || !source.CaptureDemand) return;
            source.CaptureDemand = false; source.DemandUntil = now + DemandLifetime;
        }

        internal long[] Prune(double now)
        {
            var removed = new List<long>();
            for (int i = order.Count - 1; i >= 0; i--)
            {
                long camera = order[i];
                if (sources[camera].DemandUntil >= now) continue;
                removed.Add(camera); sources.Remove(camera); order.RemoveAt(i);
                if (i < cursor) cursor--;
            }
            if (cursor >= order.Count) cursor = 0;
            return removed.ToArray();
        }

        internal void Clear()
        { sources.Clear(); order.Clear(); cursor = 0; lastFrame = long.MinValue; }

        internal static int BoundResolution(int requested, int depthWidth, int depthHeight,
            int colorWidth, int colorHeight)
        {
            if (!SupportedResolution(requested) || depthWidth < 64 || depthHeight < 64 ||
                colorWidth < 64 || colorHeight < 64) return 0;
            int available = Math.Min(requested, Math.Min(Math.Min(depthWidth, depthHeight),
                Math.Min(colorWidth, colorHeight)));
            // The engine's generated-texture helper rounds NPOT mip counts up,
            // while D3D11 permits floor(log2(size))+1. Use a bounded power-of-two
            // target rather than creating an invalid capped mip descriptor.
            int result = 64;
            while (result <= available / 2 && result < MaxResolution) result *= 2;
            return result;
        }
        internal static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
        internal static bool SupportedResolution(int value)
        { return value == 256 || value == 512 || value == 1024 || value == 2048; }
    }
}
