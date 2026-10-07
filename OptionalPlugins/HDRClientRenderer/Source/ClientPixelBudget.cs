using System;
using System.Collections.Generic;

namespace HDRClientRenderer
{
    // Primary occupancy counts physical base pixels written by capture rasters,
    // panorama outputs, LCD destination copies and UI updates. Read bandwidth,
    // clears and mip generation are separate diagnostics, never a multiplier.
    internal sealed class ClientPixelBudget
    {
        internal const int MaxViewportPixels = 8294400, MaxJobPixels=8388608, MaxJobs = 64;
        internal enum Kind { Capture, Panorama, Lcd, Ui }
        internal struct Frame
        {
            internal readonly long Number;
            internal readonly int Width, Height;
            internal Frame(long number, int width, int height) { Number = number; Width = width; Height = height; }
            internal bool Valid { get { return Number >= 0 && Width > 0 && Height > 0 && Width <= 16384 && Height <= 16384; } }
            internal int Pixels { get { return Valid ? (int)Math.Min(MaxViewportPixels, (long)Width * Height) : 0; } }
        }
        sealed class Job
        {
            internal string Key;
            internal Kind Category;
            internal int Pixels;
            internal long Order, LastSpent = long.MinValue, Grant = long.MinValue;
        }
        readonly object gate = new object();
        readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();
        readonly HashSet<string> spentKeys = new HashSet<string>();
        readonly Func<Frame> readFrame;
        long frame = long.MinValue, order, bandwidthPixels;
        int limit, allocated, spent, nextKind, frameStart;
        int configuredPixels;
        double viewportMultiplier;
        bool configured;
        internal ClientPixelBudget(Func<Frame> readFrame) { this.readFrame = readFrame; }
        Frame Read() { try { return readFrame == null ? default(Frame) : readFrame(); } catch { return default(Frame); } }
        internal int PixelLimit { get { var current=Read();lock(gate)return Limit(current); } }
        internal void Configure(ClientRenderSettings settings)
        { if(settings==null||!settings.Valid)throw new ArgumentException("Invalid client render settings.");
            lock(gate){configured=true;configuredPixels=settings.PixelBudget;viewportMultiplier=settings.ViewportMultiplier;} }
        int Limit(Frame current)
        {
            if(!current.Valid)return 0;
            if(!configured)return current.Pixels;
            if(configuredPixels>0)return configuredPixels;
            if(viewportMultiplier>0)return (int)Math.Min(int.MaxValue,Math.Max(1,(double)current.Width*current.Height*viewportMultiplier));
            return int.MaxValue;
        }
        internal int Spent { get { lock (gate) return spent; } }
        internal int AvailablePixels
        {
            get
            {
                var current=Read();lock(gate)
                {
                    int allowed=Limit(current);if(!current.Valid||current.Number<frame)return 0;
                    return current.Number==frame?Math.Max(0,allowed-spent):allowed;
                }
            }
        }
        internal int Pending { get { lock (gate) return jobs.Count; } }
        internal long BandwidthPixels { get { lock (gate) return bandwidthPixels; } }
        internal bool Request(string key, Kind category, int pixels)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 128 || (int)category < 0 || (int)category > 3 || pixels <= 0 || pixels > MaxJobPixels) return false;
            lock (gate)
            {
                Job job;
                if (!jobs.TryGetValue(key, out job))
                {
                    if (jobs.Count >= MaxJobs) return false;
                    jobs.Add(key, new Job { Key = key, Category = category, Pixels = pixels, Order = ++order });
                }
                else
                {
                    if (job.Category != category) return false;
                    if (job.Pixels != pixels)
                    {
                        if (job.Grant == frame && !spentKeys.Contains(key)) allocated -= job.Pixels;
                        job.Grant = long.MinValue; job.Pixels = pixels;
                    }
                }
                return true;
            }
        }
        internal bool TrySpend(string key, Kind category, int pixels)
        {
            if (!Request(key, category, pixels)) return false;
            var current = Read();
            if (!current.Valid) return false;
            lock (gate)
            {
                if (current.Number < frame) return false;
                if (current.Number != frame)
                {
                    if(frame==long.MinValue)
                    {
                        Job oldest=null;
                        foreach(var candidate in jobs.Values)if(oldest==null||candidate.Order<oldest.Order)oldest=candidate;
                        if(oldest!=null)frameStart=(int)oldest.Category;
                    }
                    else frameStart=(frameStart+1)%4;
                    nextKind=frameStart;
                    frame = current.Number; limit = Limit(current);
                    allocated = spent = 0; bandwidthPixels = 0; spentKeys.Clear();
                }
                Plan();
                Job job;
                if (!jobs.TryGetValue(key, out job) || job.Grant != frame || spentKeys.Contains(key) || pixels > limit - spent) return false;
                spent += pixels; spentKeys.Add(key); job.LastSpent = frame;
                return true;
            }
        }
        void Plan()
        {
            // Reserve pending jobs before the first producer executes. Rotating
            // families prevents the Priority.First capture hook taking everything;
            // oldest service within each family prevents small-job monopolies.
            while (allocated < limit)
            {
                Job selected = null; int selectedKind = nextKind;
                for (int offset = 0; offset < 4; offset++)
                {
                    int category = (nextKind + offset) % 4;
                    foreach (var candidate in jobs.Values)
                    {
                        if ((int)candidate.Category != category || candidate.Grant == frame || spentKeys.Contains(candidate.Key) || candidate.Pixels > limit - allocated) continue;
                        if (selected == null || candidate.LastSpent < selected.LastSpent || candidate.LastSpent == selected.LastSpent && candidate.Order < selected.Order) selected = candidate;
                    }
                    if (selected != null) { selectedKind = category; break; }
                }
                if (selected == null) break;
                selected.Grant = frame; allocated += selected.Pixels; nextKind = (selectedKind + 1) % 4;
            }
        }
        internal void Complete(string key) { Cancel(key); }
        internal void Cancel(string key)
        {
            if (key == null) return;
            lock (gate)
            {
                Job job;
                if (!jobs.TryGetValue(key, out job)) return;
                if (job.Grant == frame && !spentKeys.Contains(key)) allocated -= job.Pixels;
                jobs.Remove(key);
            }
        }
        internal void RecordBandwidth(long pixels)
        { if (pixels > 0) lock (gate) bandwidthPixels = pixels > long.MaxValue - bandwidthPixels ? long.MaxValue : bandwidthPixels + pixels; }
        internal void Clear()
        { lock (gate) { jobs.Clear(); spentKeys.Clear(); frame = long.MinValue; allocated = spent = limit = nextKind = frameStart = 0; bandwidthPixels = 0; } }
        internal static bool BoundSize(int width, int height, int minimum, int pixels, out int boundedWidth, out int boundedHeight)
        {
            boundedWidth = boundedHeight = 0;
            if (width < minimum || height < minimum || width > 8192 || height > 8192 || minimum < 1 || pixels < (long)minimum * minimum) return false;
            double scale = Math.Min(1, Math.Sqrt((double)pixels / ((long)width * height)));
            boundedWidth = Math.Max(minimum, (int)Math.Floor(width * scale));
            boundedHeight = Math.Max(minimum, (int)Math.Floor(height * scale));
            return (long)boundedWidth * boundedHeight <= pixels;
        }
        internal static long MipPixels(int width,int height)
        {
            long pixels=0;
            for(int side=Math.Max(width,height);side>1;side>>=1)
            {width=Math.Max(1,width>>1);height=Math.Max(1,height>>1);pixels+=(long)width*height;}
            return pixels;
        }
    }
}
