using System;
using System.Globalization;
using System.Collections.Generic;

namespace HDRClientRenderer
{
    // Fixed client composition policy. Sources are camera identities, never file
    // names, pointers, shaders, or video bytes supplied by a caller.
    internal sealed class PanoramaStore
    {
        internal const int MaxFaces = 6, MaxSlots = 2, MaxWidth = 4096, MaxHeight = 2048;
        internal const int MaxPixels = 8388608, MaxResidentBytes = 128 * 1024 * 1024;
        internal const int MaxInitialUploadBytes = 32 * 1024 * 1024, MaxDraws = 2;
        internal const int MaxDrawPixels = 2 * MaxPixels;
        internal sealed class Settings
        {
            internal double FovDegrees = 105, FeatherDegrees = 8, Saturation = 1.15;
            internal int CaptureResolution = 1024;
            internal int Profile;
            internal double Rate = 30;
            internal bool Valid
            {
                get { return Finite(FovDegrees) && FovDegrees >= 60 && FovDegrees <= 120 &&
                    Finite(FeatherDegrees) && FeatherDegrees >= 0 && FeatherDegrees <= 25 &&
                    Finite(Saturation) && Saturation >= 0 && Saturation <= 2 &&
                    (CaptureResolution == 256 || CaptureResolution == 512 || CaptureResolution == 1024 || CaptureResolution == 2048) && Profile >= 0 && Profile <= 1 && Finite(Rate) && Rate > 0; }
            }
            internal Settings Copy() { return new Settings { FovDegrees = FovDegrees, FeatherDegrees = FeatherDegrees,
                Saturation = Saturation, CaptureResolution = CaptureResolution, Rate = Rate, Profile = Profile }; }
            internal bool Same(Settings other) { return other != null && FovDegrees == other.FovDegrees &&
                FeatherDegrees == other.FeatherDegrees && Saturation == other.Saturation && CaptureResolution == other.CaptureResolution && Profile == other.Profile; }
        }
        internal sealed class Face
        {
            internal long Camera, Generation;
            internal string Texture;
            internal int Width, Height;
            internal double RightX, RightY, RightZ, UpX, UpY, UpZ, ForwardX, ForwardY, ForwardZ;
            internal double TanHorizontal, TanVertical;
            internal object Evidence;
            internal double[] AnchorInverse;
            internal PanoramaTile[] Tiles;
            internal int Profile;
            internal long ContentRevision;
            internal Face Copy() { var next = (Face)MemberwiseClone(); next.AnchorInverse = AnchorInverse == null ? null : (double[])AnchorInverse.Clone();next.Tiles=Tiles==null?null:(PanoramaTile[])Tiles.Clone(); return next; }
            internal bool Valid
            {
                get
                {
                    return Camera > 0 && Generation >= 0 && !string.IsNullOrEmpty(Texture) && Texture.Length <= 256 &&
                        Width >= 16 && Height >= 16 && Width <= 4096 && Height <= 4096 && Profile>=0&&Profile<=1&&ContentRevision>=0&&PanoramaTile.Valid(Tiles,Width,Height)&&
                        Finite(TanHorizontal) && Finite(TanVertical) && TanHorizontal > 0 && TanHorizontal < 16 && TanVertical > 0 && TanVertical < 16 &&
                        Unit(RightX, RightY, RightZ) && Unit(UpX, UpY, UpZ) && Unit(ForwardX, ForwardY, ForwardZ) &&
                        Math.Abs(RightX * UpX + RightY * UpY + RightZ * UpZ) < .0001 &&
                        Math.Abs(RightX * ForwardX + RightY * ForwardY + RightZ * ForwardZ) < .0001 &&
                        Math.Abs(UpX * ForwardX + UpY * ForwardY + UpZ * ForwardZ) < .0001;
                }
            }
            internal bool Same(Face other)
            {
                return other != null && Camera == other.Camera && Generation == other.Generation && Texture == other.Texture &&
                    Width == other.Width && Height == other.Height && Near(RightX, other.RightX) && Near(RightY, other.RightY) && Near(RightZ, other.RightZ) &&
                    Near(UpX, other.UpX) && Near(UpY, other.UpY) && Near(UpZ, other.UpZ) && Near(ForwardX, other.ForwardX) &&
                    Near(ForwardY, other.ForwardY) && Near(ForwardZ, other.ForwardZ) && Near(TanHorizontal, other.TanHorizontal) && Near(TanVertical, other.TanVertical)&&Profile==other.Profile&&SameTiles(Tiles,other.Tiles);
            }
            static bool SameTiles(PanoramaTile[] a,PanoramaTile[] b)
            {if(a==null||b==null)return a==null&&b==null;if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(!a[i].Same(b[i]))return false;return true;}
        }
        internal interface IWorld
        {
            bool Active(long anchor, long caller, string sourceId);
            bool TrySettings(long anchor, long caller, string sourceId, out Settings settings);
            bool DisplayDemand(long anchor,long caller,string sourceId,out int width,out int height,out double rate);
            double Now { get; }
            bool Authorized(long anchor, long caller, long camera);
            PanoramaDemand Demand(long anchor, long caller, string sourceId, long[] cameras, Settings settings);
            void Warm(long camera);
            // The native owner schedules visible requests and returns only verified
            // textures. This boundary must reauthorize the physical camera too.
            bool TryFace(long anchor, long caller, long camera, Settings settings, PanoramaDensity density, out Face face);
            bool ValidFace(long anchor, long caller, Face face, Settings settings);
            void CreateTarget(string target, int width, int height);
            bool Compose(string target, int width, int height, Face[] faces, Settings settings);
            bool TargetHealthy(string target);
            bool TargetReady(string target);
            void DestroyTarget(string target);
            void RetireFaces(Face[] faces);
        }
        internal sealed class Lease
        {
            internal int Epoch, Slot, Version, Width, Height;
            internal long Anchor, Caller;
            internal string SourceId, ScreenId, Material;
            internal Face[] Faces;
            internal Settings Settings;
            internal double RefreshRate;
        }
        sealed class DemandContext
        {
            internal long Anchor, Caller;
            internal string SourceId, ScreenId;
            internal long[] Cameras;
            internal Settings Settings;
            internal PanoramaDemand Demand;
            internal PanoramaResolution[] Reservations;
            internal int OutputWidth,OutputHeight,OutputLimit;
            internal double Rate;
        }
        readonly IWorld world;
        readonly Lease[] slots = new Lease[MaxSlots];
        readonly DemandContext[] contexts = new DemandContext[MaxSlots];
        int epoch, version, residentBytes, uploadBytes, drawPixels, draws;
        internal int ResidentBytes { get { return residentBytes; } }
        internal string Describe()
        {
            var text=new System.Text.StringBuilder("Panorama detail:");
            foreach(var lease in slots)if(lease!=null)text.Append(" ").Append(lease.ScreenId).Append(' ').Append(lease.Width).Append('x').Append(lease.Height).Append(" / ").Append(lease.Faces.Length).Append(" cameras;");
            return text.ToString();
        }
        internal int ResidentTargets { get { int count = 0; foreach (var slot in slots) if (slot != null) count++; return count; } }
        internal bool Demands(long camera)
        { foreach (var context in contexts) if (context != null) foreach (long id in context.Cameras) if (id == camera) return true; return false; }
        internal PanoramaStore(IWorld world) { this.world = world; }
        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        static bool Near(double left, double right) { return Math.Abs(left - right) <= .00001; }
        static bool Unit(double x, double y, double z) { return Finite(x) && Finite(y) && Finite(z) && Math.Abs(x * x + y * y + z * z - 1) < .0001; }
        internal static bool ParseSources(string id, out long[] cameras)
        {
            cameras = null;
            if (string.IsNullOrEmpty(id) || id.Length > 256) return false;
            string[] fields = id.Split(',');
            if (fields.Length < 1 || fields.Length > MaxFaces) return false;
            var result = new long[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                if (!long.TryParse(fields[i], NumberStyles.None, CultureInfo.InvariantCulture, out result[i]) || result[i] <= 0) return false;
                for (int j = 0; j < i; j++) if (result[i] == result[j]) return false;
            }
            cameras = result; return true;
        }
        internal static bool BoundSize(int width, int height, out int boundedWidth, out int boundedHeight)
        {
            boundedWidth = boundedHeight = 0;
            if (width < 16 || height < 16 || width > 4096 || height > 4096 || (double)width / height > 64 || (double)height / width > 64) return false;
            double scale = Math.Min(1, Math.Min((double)MaxWidth / width, (double)MaxHeight / height));
            scale = Math.Min(scale, Math.Sqrt((double)MaxPixels / ((double)width * height)));
            boundedWidth = Math.Max(16, (int)Math.Floor(width * scale));
            boundedHeight = Math.Max(16, (int)Math.Floor(height * scale));
            return (long)boundedWidth * boundedHeight <= MaxPixels;
        }
        internal void Tick() { uploadBytes = drawPixels = draws = 0; }
        internal void NewEpoch() { for (int i = 0; i < slots.Length; i++) Clear(i); epoch = checked(epoch + 1); Tick(); }
        internal Lease Acquire(long anchor, long caller, string id, string screenId, int width, int height)
        {
            long[] cameras;
            if (anchor <= 0 || caller <= 0 || screenId == null || screenId.Length > 64 || !ParseSources(id, out cameras) ||
                width < 16 || height < 16 || width > MaxWidth || height > MaxHeight || (long)width * height > MaxPixels) return null;
            int selected = -1, free = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                var context = contexts[i];
                if (context == null) { if (free < 0) free = i; }
                else if (context.Anchor == anchor && context.Caller == caller && context.SourceId == id && context.ScreenId == screenId) selected = i;
            }
            int slot = selected >= 0 ? selected : free;
            if (slot < 0) { Prune(); for (int i = 0; i < contexts.Length; i++) if (contexts[i] == null) { slot = i; break; } }
            if (slot < 0) return null;
            Settings settings; Face[] faces;
            try
            {
                if (!world.Active(anchor, caller, id) || !world.TrySettings(anchor, caller, id, out settings) || settings == null || !settings.Valid)
                { if (selected >= 0) Clear(selected); return null; }
                int demandWidth,demandHeight;double demandRate;
                if(!world.DisplayDemand(anchor,caller,id,out demandWidth,out demandHeight,out demandRate)||demandWidth<16||demandHeight<16||!Finite(demandRate)||demandRate<=0)
                {Clear(slot);return null;}
                foreach (long camera in cameras) if (!world.Authorized(anchor, caller, camera)) { Clear(slot); return null; }
                // One reused native camera cannot hold two simultaneous pinhole
                // configurations. Preserve the established peer instead of
                // alternately invalidating both consumers on every request.
                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    var peer = contexts[slotIndex]; if (peer == null || slotIndex == slot) continue;
                    if (peer.Settings.FovDegrees == settings.FovDegrees) continue;
                    foreach (long peerCamera in peer.Cameras) foreach (long camera in cameras) if (peerCamera == camera)
                    { if (selected >= 0) Clear(selected); return null; }
                }
                var context = new DemandContext { Anchor = anchor, Caller = caller, SourceId = id, ScreenId = screenId, Cameras = cameras,
                    Settings = settings.Copy(), Demand = SafeDemand(anchor, caller, id, cameras, settings), OutputWidth=width,OutputHeight=height,
                    Rate=demandRate, OutputLimit=PanoramaResolution.OutputLimit(Math.Min(width,demandWidth),Math.Min(height,demandHeight),settings.CaptureResolution) };
                var previousContext=contexts[slot];
                context.Reservations=previousContext!=null&&previousContext.Settings.Same(settings)?previousContext.Reservations:new PanoramaResolution[cameras.Length];
                for(int i=0;i<cameras.Length;i++)if(context.Reservations[i]==null)context.Reservations[i]=new PanoramaResolution();
                contexts[slot] = context;
                ObserveReservations(context);
                // Request every constituent even while some captures are pending.
                bool complete = true; var visibleFaces = new List<Face>();
                for (int i = 0; i < cameras.Length; i++)
                {
                    if ((context.Demand.Mask & (1 << i)) == 0) continue;
                    Face face;
                    var captureSettings = settings.Copy(); captureSettings.CaptureResolution = UnionResolution(cameras[i], settings.FovDegrees);
                    captureSettings.Rate=UnionRate(cameras[i],settings.FovDegrees);
                    captureSettings.Profile=UnionProfile(cameras[i],settings.FovDegrees);
                    if (!world.TryFace(anchor, caller, cameras[i], captureSettings, UnionDensity(cameras[i],settings.FovDegrees,captureSettings.CaptureResolution), out face) || face == null || !face.Valid || face.Camera != cameras[i]) complete = false;
                    else visibleFaces.Add(face.Copy());
                }
                if (!complete)
                {
                    var prior = slots[slot];
                    if (prior != null && prior.Width == width && prior.Height == height && prior.Settings.Same(settings) && Valid(prior, anchor, caller, id)) return prior;
                    Clear(slot, false, false); return null;
                }
                faces = visibleFaces.ToArray();
            }
            catch { Clear(slot); return null; }
            int pixels = checked(width * height), bytes = checked(pixels * 4);
            var old = slots[slot];
            bool sameSize = old != null && old.Width == width && old.Height == height;
            bool same = sameSize && old.Settings.Same(settings) && SameFaces(old.Faces, faces);
            int residentCost = checked(bytes * 2); // Covers base plus full mipchain conservatively.
            if (draws >= MaxDraws || pixels > MaxDrawPixels - drawPixels || !sameSize &&
                (bytes > MaxInitialUploadBytes - uploadBytes || residentCost > MaxResidentBytes - residentBytes + (old == null ? 0 : old.Width * old.Height * 8)))
            { if (old != null && !same) Clear(slot); return null; }
            var next = same ? old : new Lease { Epoch = epoch, Slot = slot, Version = checked(++version), Anchor = anchor, Caller = caller,
                SourceId = id, ScreenId = screenId, Width = width, Height = height, Material = "HDR_ClientPanorama_" + slot,
                Faces = faces, Settings = settings.Copy() };
            if (!sameSize) Clear(slot, false, false);
            try
            {
                if (!sameSize) { world.CreateTarget(next.Material, width, height); uploadBytes += bytes; }
                if (!world.Compose(next.Material, width, height, faces, settings)) throw new InvalidOperationException("Panorama GPU request unavailable.");
            }
            catch
            {
                if (sameSize) Clear(slot); else
                { contexts[slot] = null; try { world.DestroyTarget(next.Material); } catch { } try { world.RetireFaces(faces); } catch { } }
                return null;
            }
            if (!sameSize) residentBytes += residentCost;
            next.Faces = faces; next.RefreshRate=contexts[slot].Rate;
            slots[slot] = next; draws++; drawPixels += pixels;
            return world.TargetReady(next.Material)?next:null;
        }
        static bool SameFaces(Face[] left, Face[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (!left[i].Same(right[i])) return false;
            return true;
        }
        internal bool Valid(object value, long anchor, long caller, string id)
        {
            var lease = value as Lease;
            if (lease == null || lease.Epoch != epoch || lease.Slot < 0 || lease.Slot >= slots.Length || !ReferenceEquals(slots[lease.Slot], lease)
                || lease.Anchor != anchor || lease.Caller != caller || lease.SourceId != id) return false;
            try
            {
                Settings current; var context = contexts[lease.Slot];
                if (!world.TargetHealthy(lease.Material) || !world.Active(anchor, caller, id) || !world.TrySettings(anchor, caller, id, out current) || !lease.Settings.Same(current))
                { Clear(lease.Slot); return false; }
                if (context == null) return false;
                foreach (long camera in context.Cameras) if (!world.Authorized(anchor, caller, camera)) { Clear(lease.Slot); return false; }
                context.Demand = SafeDemand(anchor, caller, id, context.Cameras, current);
                UpdateDisplayDemand(context);
                foreach (var face in lease.Faces)
                {
                    int index = Array.IndexOf(context.Cameras, face.Camera);
                    if (index >= 0 && (context.Demand.Mask & (1 << index)) != 0 && !world.ValidFace(anchor, caller, face, lease.Settings)) { Clear(lease.Slot); return false; }
                }
                return world.TargetReady(lease.Material);
            }
            catch { Clear(lease.Slot); return false; }
        }
        internal bool Release(object value)
        {
            var lease = value as Lease;
            if (lease == null || lease.Epoch != epoch || lease.Slot < 0 || lease.Slot >= slots.Length || !ReferenceEquals(slots[lease.Slot], lease)) return false;
            Clear(lease.Slot); return true;
        }
        PanoramaDemand SafeDemand(long anchor, long caller, string id, long[] cameras, Settings settings)
        {
            try
            {
                var demand = world.Demand(anchor, caller, id, cameras, settings);
                if (demand == null || demand.Sizes == null || demand.Sizes.Length != cameras.Length || demand.Mask < 0 ||
                    (demand.Mask & ~((1 << cameras.Length) - 1)) != 0 || !demand.Known) return PanoramaDemand.Full(cameras.Length, settings.CaptureResolution);
                for (int i = 0; i < cameras.Length; i++)
                {
                    bool wanted = (demand.Mask & (1 << i)) != 0; int size = demand.Sizes[i];
                    if (wanted && (size != 256 && size != 512 && size != 1024 || size > settings.CaptureResolution) || !wanted && size != 0)
                        return PanoramaDemand.Full(cameras.Length, settings.CaptureResolution);
                }
                return demand;
            }
            catch { return PanoramaDemand.Full(cameras.Length, settings.CaptureResolution); }
        }
        int UnionResolution(long camera, double fov)
        {
            int resolution = 0;
            foreach (var context in contexts) if (context != null && context.Settings.FovDegrees == fov)
                for (int i = 0; i < context.Cameras.Length; i++) if (context.Cameras[i] == camera && (context.Demand.Mask & (1 << i)) != 0)
                    resolution = Math.Max(resolution, context.Settings.CaptureResolution);
            return resolution;
        }
        double UnionRate(long camera,double fov)
        {
            double rate=0;
            foreach(var context in contexts)if(context!=null&&context.Settings.FovDegrees==fov)
                for(int i=0;i<context.Cameras.Length;i++)if(context.Cameras[i]==camera&&(context.Demand.Mask&(1<<i))!=0)rate=Math.Max(rate,context.Rate);
            return rate;
        }
        int UnionProfile(long camera,double fov)
        {
            int profile=1;
            foreach(var context in contexts)if(context!=null&&context.Settings.FovDegrees==fov)
                for(int i=0;i<context.Cameras.Length;i++)if(context.Cameras[i]==camera&&(context.Demand.Mask&(1<<i))!=0)
                    profile=Math.Min(profile,context.Settings.Profile);
            return profile;
        }
        PanoramaDensity UnionDensity(long camera,double fov,int maximum)
        {
            PanoramaDensity result=null;
            foreach(var context in contexts)if(context!=null&&context.Settings.FovDegrees==fov)
                for(int i=0;i<context.Cameras.Length;i++)if(context.Cameras[i]==camera&&(context.Demand.Mask&(1<<i))!=0)
                {
                    var fields=context.Demand.Density;
                    var field=fields!=null&&fields.Length==context.Cameras.Length?fields[i]:null;
                    result=PanoramaDensity.Union(result,(field??PanoramaDensity.Full(maximum)).Clamp(context.OutputLimit),maximum);
                }
            return result??PanoramaDensity.Full(maximum);
        }
        void ObserveReservations(DemandContext context)
        {
            for(int i=0;i<context.Cameras.Length;i++)
            {
                if((context.Demand.Mask&(1<<i))==0)continue;
                bool fresh=context.Demand.Fresh!=null&&context.Demand.Fresh.Length==context.Cameras.Length&&context.Demand.Fresh[i];
                context.Reservations[i].Observe(context.Demand.Sizes[i],fresh,context.OutputLimit,world.Now);
            }
        }
        void UpdateDisplayDemand(DemandContext context)
        {
            int width,height;double rate;
            if(!world.DisplayDemand(context.Anchor,context.Caller,context.SourceId,out width,out height,out rate)||width<16||height<16||!Finite(rate)||rate<=0)
                throw new InvalidOperationException("Source display demand is inactive.");
            context.Rate=rate;
            context.OutputLimit=PanoramaResolution.OutputLimit(Math.Min(context.OutputWidth,width),Math.Min(context.OutputHeight,height),context.Settings.CaptureResolution);
            ObserveReservations(context);
        }
        internal void RefreshDemands()
        {
            var requested = new HashSet<long>(); var referenced = new HashSet<long>();
            foreach (var context in contexts) if (context != null)
                for (int i = 0; i < context.Cameras.Length; i++)
                {
                    long camera = context.Cameras[i]; referenced.Add(camera);
                    if ((context.Demand.Mask & (1 << i)) == 0 || !requested.Add(camera)) continue;
                    var settings = context.Settings.Copy(); settings.CaptureResolution = UnionResolution(camera, settings.FovDegrees);
                    settings.Rate=UnionRate(camera,settings.FovDegrees);
                    settings.Profile=UnionProfile(camera,settings.FovDegrees);
                    Face ignored; world.TryFace(context.Anchor, context.Caller, camera, settings, UnionDensity(camera,settings.FovDegrees,settings.CaptureResolution), out ignored);
                }
            foreach (long camera in referenced) if (!requested.Contains(camera)) world.Warm(camera);
        }
        internal void Prune()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var context = contexts[i]; if (context == null) continue;
                if (slots[i] != null) { Valid(slots[i], slots[i].Anchor, slots[i].Caller, slots[i].SourceId); continue; }
                try
                {
                    Settings settings = null;
                    bool live = world.Active(context.Anchor, context.Caller, context.SourceId) && world.TrySettings(context.Anchor, context.Caller, context.SourceId, out settings);
                    if (!live || settings == null || !settings.Valid || !settings.Same(context.Settings)) { Clear(i); continue; }
                    foreach (long camera in context.Cameras) if (!world.Authorized(context.Anchor, context.Caller, camera)) { live = false; break; }
                    if (!live) { Clear(i); continue; }
                    context.Demand = SafeDemand(context.Anchor, context.Caller, context.SourceId, context.Cameras, context.Settings);
                    UpdateDisplayDemand(context);
                }
                catch { Clear(i); }
            }
        }
        void Clear(int slot, bool retire = true, bool dropContext = true)
        {
            var lease = slots[slot]; var context = contexts[slot];
            if (dropContext) contexts[slot] = null;
            if (lease != null)
            {
                slots[slot] = null; residentBytes -= lease.Width * lease.Height * 8;
                try { world.DestroyTarget(lease.Material); } catch { }
            }
            if (retire && context != null) try
            { var retired = new Face[context.Cameras.Length]; for (int i = 0; i < retired.Length; i++) retired[i] = new Face { Camera = context.Cameras[i] }; world.RetireFaces(retired); }
            catch { }
        }
    }
}
