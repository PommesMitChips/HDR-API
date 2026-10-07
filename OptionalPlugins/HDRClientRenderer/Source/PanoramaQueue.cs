using System;
using System.Collections.Generic;

namespace HDRClientRenderer
{
    internal sealed class PanoramaQueue
    {
        internal sealed class Request
        {
            internal string Target;
            internal int Width, Height, Attempts;
            internal long Order;
            internal PanoramaStore.Face[] Faces;
            internal float[] Constants;
            internal PanoramaStore.Settings Settings;
        }
        readonly object gate = new object();
        readonly Dictionary<string, Request> requests = new Dictionary<string, Request>();
        long order;
        internal int Count { get { lock (gate) return requests.Count; } }
        internal bool Enqueue(string target, int width, int height, PanoramaStore.Face[] faces, PanoramaStore.Settings settings,Func<bool> register=null)
        {
            if (target != "HDR_ClientPanorama_0" && target != "HDR_ClientPanorama_1" || width < 16 || height < 16 ||
                width > PanoramaStore.MaxWidth || height > PanoramaStore.MaxHeight || (long)width * height > PanoramaStore.MaxPixels) return false;
            float[] constants = PanoramaShader.Constants(faces, settings); if (constants == null) return false;
            var detached = new PanoramaStore.Face[faces.Length];
            for (int i = 0; i < faces.Length; i++) { if (faces[i].Texture == target) return false; detached[i] = faces[i].Copy(); }
            lock (gate)
            {
                Request old; requests.TryGetValue(target, out old);
                if (old == null && requests.Count >= PanoramaStore.MaxSlots) return false;
                if(register!=null&&!register())return false;
                requests[target] = new Request { Target = target, Width = width, Height = height, Faces = detached, Constants = constants,
                    Settings = settings.Copy(), Order = old == null ? ++order : old.Order, Attempts = old == null ? 0 : old.Attempts };
                return true;
            }
        }
        internal void Cancel(string target) { lock (gate) requests.Remove(target); }
        internal void Cancel(string target,Action cleanup){lock(gate){requests.Remove(target);cleanup();}}
        internal void Clear() { lock (gate) requests.Clear(); }
        internal void Clear(Action cleanup) { lock (gate) { requests.Clear(); cleanup(); } }
        internal void Drain(Func<Request, bool> draw, Action<string> report, Action<Request> failed = null, Func<Request,bool> eligible = null)
        {
            lock (gate)
            {
                var ready = new List<Request>(requests.Values); ready.Sort((a, b) => a.Order.CompareTo(b.Order));
                int draws = 0, pixels = 0;
                foreach (var request in ready)
                {
                    int cost = request.Width * request.Height;
                    if (draws >= PanoramaStore.MaxDraws || cost > PanoramaStore.MaxDrawPixels - pixels) break;
                    if (eligible != null && !eligible(request)) continue;
                    draws++; pixels += cost;
                    try
                    {
                        if (draw(request)) { requests.Remove(request.Target); continue; }
                        if (++request.Attempts < 120) continue;
                        report("Panorama GPU resources unavailable: " + request.Target);
                    }
                    catch (Exception error) { report("Panorama GPU composition failed: " + error.GetBaseException().Message); }
                    if (failed != null) failed(request);
                    requests.Remove(request.Target);
                }
            }
        }
    }
}
