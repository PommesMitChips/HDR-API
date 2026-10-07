using System;

namespace HDRClientRenderer
{
    // Keeps an LCD material lease bound to the precise live source identity.
    internal sealed class LcdStore
    {
        internal interface IWorld
        {
            bool Active(long anchor, long caller, string sourceId);
            bool Authorized(long anchor, long caller, long source);
            bool TryTexture(long source, int index, out string texture, out int width, out int height);
            void CreateTarget(string target, int width, int height);
            bool TargetReady(string target);
            void CopyTexture(string source, string target, int width, int height);
            void DestroyTarget(string target);
        }
        internal sealed class Lease
        {
            internal int Epoch, Version, Index, Slot, Width, Height, SourceWidth, SourceHeight;
            internal long Anchor, Caller, Source;
            internal string SourceId, ScreenId, Texture, Material;
        }
        private readonly IWorld world;
        private readonly Lease[] slots = new Lease[16];
        private int epoch, version, copyBytes, copiedFrames;
        internal LcdStore(IWorld world) { this.world = world; }
        // UI textures have a higher ceiling. Keep camera copies at the existing quota.
        internal static bool BoundSize(int width,int height,out int boundedWidth,out int boundedHeight)
        {
            boundedWidth=boundedHeight=0;
            if(width<16||height<16||width>4096||height>4096||(double)width/height>64||(double)height/width>64)return false;
            double scale=Math.Min(1d,Math.Sqrt(262144d/((double)width*height)));
            scale=Math.Min(scale,Math.Min(1024d/width,1024d/height));
            boundedWidth=Math.Max(16,(int)Math.Floor(width*scale));
            boundedHeight=Math.Max(16,(int)Math.Floor(height*scale));
            return true;
        }
        internal void Tick() { copyBytes = 0; copiedFrames = 0; }
        internal void NewEpoch()
        {
            for (int i = 0; i < slots.Length; i++) ClearSlot(i);
            epoch = checked(epoch + 1);
        }
        internal Lease Acquire(long anchor, long caller, string sourceId, long source, int index,
            int targetWidth, int targetHeight)
        { return Acquire(anchor, caller, sourceId, "", source, index, targetWidth, targetHeight); }
        internal Lease Acquire(long anchor, long caller, string sourceId, string screenId, long source, int index,
            int targetWidth, int targetHeight)
        {
            if (screenId == null || screenId.Length > 64) return null;
            if (!world.Active(anchor, caller, sourceId) || !world.Authorized(anchor, caller, source)) return null;
            string texture; int sourceWidth, sourceHeight;
            if (!world.TryTexture(source, index, out texture, out sourceWidth, out sourceHeight) ||
                string.IsNullOrEmpty(texture) || sourceWidth < 1 || sourceHeight < 1 ||
                sourceWidth > 8192 || sourceHeight > 8192 || targetWidth < 16 || targetHeight < 16 ||
                targetWidth > 1024 || targetHeight > 1024 ||
                (long)targetWidth * targetHeight > 262144) return null;
            int bytes = targetWidth * targetHeight * 4;
            if (bytes > 2 * 1024 * 1024 - copyBytes || copiedFrames >= 4)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    var prior = slots[i];
                    if (prior != null && prior.Anchor == anchor && prior.Caller == caller && prior.ScreenId == screenId &&
                        prior.SourceId == sourceId && prior.Width == targetWidth && prior.Height == targetHeight)
                    { ClearSlot(i); break; }
                }
                return null;
            }
            int selected = -1, free = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                var old = slots[i];
                if (old == null) { if (free < 0) free = i; }
                else if (old.Anchor == anchor && old.Caller == caller && old.ScreenId == screenId &&
                    old.SourceId == sourceId &&
                    old.Width == targetWidth && old.Height == targetHeight)
                { selected = i; break; }
            }
            int slot = selected >= 0 ? selected : free;
            if (slot < 0)
            {
                Prune();
                for (int i = 0; i < slots.Length; i++) if (slots[i] == null) { slot = i; break; }
            }
            if (slot < 0) return null;
            var current = slots[slot];
            if (current != null && current.Source == source && current.Index == index &&
                current.Texture == texture && current.SourceWidth == sourceWidth &&
                current.SourceHeight == sourceHeight && current.Width == targetWidth && current.Height == targetHeight &&
                current.Epoch == epoch)
            {
                try { world.CopyTexture(texture, current.Material, targetWidth, targetHeight);
                    copyBytes += bytes; copiedFrames++; return world.TargetReady(current.Material)?current:null; }
                catch { ClearSlot(slot); return null; }
            }
            var next = new Lease { Epoch = epoch, Version = checked(++version), Slot = slot,
                Anchor = anchor, Caller = caller, SourceId = sourceId, ScreenId = screenId,
                Source = source, Index = index,
                Texture = texture, SourceWidth = sourceWidth, SourceHeight = sourceHeight,
                Width = targetWidth, Height = targetHeight, Material = "HDR_ClientLcd_" + slot };
            ClearSlot(slot);
            try
            {
                world.CreateTarget(next.Material, targetWidth, targetHeight);
                world.CopyTexture(texture, next.Material, targetWidth, targetHeight);
            }
            catch { try { world.DestroyTarget(next.Material); } catch { } return null; }
            slots[slot] = next;
            copyBytes += bytes; copiedFrames++;
            return world.TargetReady(next.Material)?next:null;
        }
        internal bool Valid(object value, long anchor, long caller, string sourceId)
        {
            var lease = value as Lease;
            if (lease == null || lease.Epoch != epoch || lease.Slot < 0 || lease.Slot >= slots.Length ||
                !ReferenceEquals(slots[lease.Slot], lease) || lease.Anchor != anchor ||
                lease.Caller != caller || lease.SourceId != sourceId) return false;
            try
            {
                if (!world.Active(anchor, caller, sourceId) || !world.Authorized(anchor, caller, lease.Source))
                { ClearSlot(lease.Slot); return false; }
                string texture; int width, height;
                bool live = world.TryTexture(lease.Source, lease.Index, out texture, out width, out height) &&
                    texture == lease.Texture && width == lease.SourceWidth && height == lease.SourceHeight;
                if (!live) ClearSlot(lease.Slot);
                return live&&world.TargetReady(lease.Material);
            }
            catch { ClearSlot(lease.Slot); return false; }
        }
        internal void Prune()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var lease = slots[i];
                if (lease != null) Valid(lease, lease.Anchor, lease.Caller, lease.SourceId);
            }
        }
        internal bool Release(object value)
        {
            var lease = value as Lease;
            if (lease == null || lease.Slot < 0 || lease.Slot >= slots.Length ||
                !ReferenceEquals(slots[lease.Slot], lease)) return false;
            ClearSlot(lease.Slot);
            return true;
        }
        private void ClearSlot(int slot)
        {
            var lease = slots[slot];
            if (lease == null) return;
            slots[slot] = null;
            try { world.DestroyTarget(lease.Material); } catch { }
        }
    }
}
