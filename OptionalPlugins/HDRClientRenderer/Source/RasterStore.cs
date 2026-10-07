using System;

namespace HDRClientRenderer
{
    // Game-independent lease and budget policy. All calls are made on IPlugin.Update's game thread.
    internal sealed class RasterStore
    {
        internal const int MaxWidth = 2048, MaxHeight = 2048, MaxPixels = 1048576;
        internal const int MaxSlots = 16, MaxResidentBytes = 16 * 1024 * 1024;
        internal const int MaxUploadBytesPerTick = 4 * 1024 * 1024;
        internal sealed class Lease
        {
            internal readonly int Epoch, Slot, Version;
            internal Lease(int epoch, int slot, int version) { Epoch = epoch; Slot = slot; Version = version; }
        }
        internal sealed class Entry
        {
            internal string Key, Texture, Material;
            internal long Serial;
            internal int Width, Height, Version;
            internal byte[] Data;
            internal Lease Lease;
            internal bool Queued;
        }
        internal interface IBackend
        {
            void Create(string texture, int width, int height, byte[] rgba,long serial);
            void Reset(string texture, int width, int height, byte[] rgba,long serial);
            void Destroy(string texture);
        }
        internal interface IReadiness { bool Ready(string texture,long serial); }
        private readonly Entry[] entries = new Entry[MaxSlots];
        private readonly Entry[] pending = new Entry[MaxSlots];
        private readonly int[] versions = new int[MaxSlots];
        private static readonly byte[] SrgbPremultiply = BuildPremultiplyTable();
        private readonly IBackend backend;
        private int epoch, resident, uploadedThisTick,pendingBytes;
        internal int Epoch { get { return epoch; } }
        internal int ResidentBytes { get { return resident; } }
        internal int PendingBytes {get{return pendingBytes;}}
        internal RasterStore(IBackend backend) { this.backend = backend; }
        internal void Tick() { uploadedThisTick = 0;for(int i=0;i<MaxSlots;i++)Publish(i); }
        internal void NewEpoch()
        {
            for (int i = 0; i < entries.Length; i++) ReleaseSlot(i);
            epoch = checked(epoch + 1);
            uploadedThisTick = 0;
        }
        internal Entry Upload(string key, long serial, int width, int height, byte[] rgba)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 128 || serial < 0 || width < 1 || width > MaxWidth ||
                height < 1 || height > MaxHeight || (long)width * height > MaxPixels || rgba == null ||
                rgba.Length != (long)width * height * 4) return null;
            int slot = -1, free = -1;
            for (int i = 0; i < entries.Length; i++)
            {
                Publish(i);
                if (entries[i] == null&&pending[i]==null) { if (free < 0) free = i; }
                else if (entries[i]!=null&&entries[i].Key==key||pending[i]!=null&&pending[i].Key==key) { slot = i; break; }
            }
            if (slot < 0) slot = free;
            if (slot < 0) return null;
            var old = entries[slot];
            var waiting=pending[slot];
            if (old != null && serial == old.Serial && old.Width == width && old.Height == height) return old;
            if(waiting!=null&&serial==waiting.Serial&&waiting.Width==width&&waiting.Height==height)return old;
            if (old != null && serial <= old.Serial||waiting!=null&&serial<=waiting.Serial) return null;
            int bytes = rgba.Length;
            var readiness=backend as IReadiness;
            if (bytes > MaxUploadBytesPerTick - uploadedThisTick || bytes > MaxResidentBytes - resident + SlotBytes(slot)||
                readiness!=null&&bytes>MaxUploadBytesPerTick-pendingBytes+(waiting==null?0:waiting.Data.Length))
            {
                // A newer rejected serial must not leave the prior image live.
                if (old != null||waiting!=null) ReleaseSlot(slot);
                return null;
            }
            var copy = (byte[])rgba.Clone();
            // Billboards use a premultiplied-alpha blend. Keep sRGB encoded bytes
            // while multiplying in linear light; leave caller's straight buffer untouched.
            for (int i = 0; i < copy.Length; i += 4)
            {
                int alphaOffset = copy[i + 3] << 8;
                copy[i] = SrgbPremultiply[alphaOffset | copy[i]];
                copy[i + 1] = SrgbPremultiply[alphaOffset | copy[i + 1]];
                copy[i + 2] = SrgbPremultiply[alphaOffset | copy[i + 2]];
            }
            var texture = "HDR_ClientRaster_" + slot;
            var material = "HDR_ClientRaster_" + slot;
            var next = new Entry { Key = key, Serial = serial, Width = width, Height = height, Data = copy,
                Texture = texture, Material = material, Version = checked(++versions[slot]) };
            next.Lease = new Lease(epoch, slot, next.Version);
            try
            {
                // Async backends retain the published front while a newer serial
                // waits for its shared grant. Size replacement happens at commit.
                if(readiness!=null||old==null)backend.Create(texture,width,height,copy,serial);
                else if (old.Width == width && old.Height == height) backend.Reset(texture, width, height, copy,serial);
                else { backend.Destroy(texture); backend.Create(texture, width, height, copy,serial); }
                next.Queued = true;
            }
            catch
            {
                // A partially queued replacement must never make its prior lease valid.
                ReleaseSlot(slot);
                if(old==null&&waiting==null)try{backend.Destroy(texture);}catch{}
                return null;
            }
            uploadedThisTick += bytes;
            if(readiness!=null)
            {
                // An older in-flight update may have completed while enqueue
                // waited on the renderer lock. Publish it before coalescing again.
                Publish(slot);int previousCost=SlotBytes(slot);waiting=pending[slot];
                pendingBytes+=bytes-(waiting==null?0:waiting.Data.Length);pending[slot]=next;
                resident+=SlotBytes(slot)-previousCost;
                Publish(slot);return entries[slot];
            }
            int cost=SlotBytes(slot);entries[slot]=next;resident+=bytes-cost;
            return next;
        }
        internal bool Valid(object value)
        {
            var lease = value as Lease;
            if (lease == null || lease.Epoch != epoch || lease.Slot < 0 || lease.Slot >= entries.Length) return false;
            Publish(lease.Slot);
            var entry = entries[lease.Slot];
            var readiness = backend as IReadiness;
            return entry != null && entry.Queued && ReferenceEquals(entry.Lease, lease) && entry.Version == lease.Version &&
                (readiness == null || readiness.Ready(entry.Texture,entry.Serial));
        }
        internal bool Release(object value)
        {
            if (!Valid(value)) return false;
            ReleaseSlot(((Lease)value).Slot);
            return true;
        }
        private void ReleaseSlot(int slot)
        {
            var entry = entries[slot]??pending[slot];
            if (entry == null) return;
            resident-=SlotBytes(slot);if(pending[slot]!=null)pendingBytes-=pending[slot].Data.Length;
            entries[slot] = pending[slot] = null;
            try { backend.Destroy(entry.Texture); } catch { }
        }
        int SlotBytes(int slot){return Math.Max(entries[slot]==null?0:entries[slot].Data.Length,pending[slot]==null?0:pending[slot].Data.Length);}
        void Publish(int slot)
        {
            var next=pending[slot];var readiness=backend as IReadiness;
            if(next==null||readiness==null||!readiness.Ready(next.Texture,next.Serial))return;
            int previous=SlotBytes(slot);pendingBytes-=next.Data.Length;pending[slot]=null;entries[slot]=next;resident+=next.Data.Length-previous;
        }
        private static byte[] BuildPremultiplyTable()
        {
            var table = new byte[65536];
            for (int alpha = 0; alpha < 256; alpha++)
                for (int channel = 0; channel < 256; channel++)
                {
                    double encoded = channel / 255d;
                    double linear = encoded <= 0.04045 ? encoded / 12.92 :
                        Math.Pow((encoded + 0.055) / 1.055, 2.4);
                    linear *= alpha / 255d;
                    double premultiplied = linear <= 0.0031308 ? 12.92 * linear :
                        1.055 * Math.Pow(linear, 1d / 2.4) - 0.055;
                    table[(alpha << 8) | channel] = (byte)Math.Max(0, Math.Min(255,
                        (int)Math.Round(premultiplied * 255d)));
                }
            return table;
        }
    }
}
