using System;
using ProtoBuf;

namespace HoloMap
{
    public enum UiDragKind { Begin = 1, Move = 2, End = 3, Cancel = 4, Focus = 5, KeepAlive = 6, Blur = 7 }
    public enum UiDragMode { WorldLook = 0, FocusedPointer = 1 }
    public enum UiDragStatus { Accepted = 0, Invalid = 1, Busy = 2, Stale = 3, ContextLost = 4, Cancelled = 5, TimedOut = 6, Preempted = 7, Bounds = 8 }

    // Wire data is deliberately restricted to CLR/protobuf primitives. The server
    // resolves artwork, constraints, PB wake arguments, transforms and hit rays.
    [ProtoContract]
    public sealed class UiDragRequest
    {
        [ProtoMember(1)] public int Protocol = 1;
        [ProtoMember(2)] public int Kind;
        [ProtoMember(3)] public long CallerId;
        [ProtoMember(4)] public long TargetId;
        [ProtoMember(5)] public string ControlId;
        [ProtoMember(6)] public long DefinitionRevision;
        [ProtoMember(7)] public long ValueRevision;
        [ProtoMember(8)] public long RequestId;
        [ProtoMember(9)] public long Sequence;
        [ProtoMember(10)] public long LeaseId;
        [ProtoMember(11)] public int Mode;
        [ProtoMember(12)] public double Value;
        [ProtoMember(13)] public long ViewerId;
    }
    [ProtoContract]
    public sealed class UiDragAck
    {
        [ProtoMember(1)] public int Protocol = 1;
        [ProtoMember(2)] public int Kind;
        [ProtoMember(3)] public long CallerId;
        [ProtoMember(4)] public long TargetId;
        [ProtoMember(5)] public string ControlId;
        [ProtoMember(6)] public long DefinitionRevision;
        [ProtoMember(7)] public long ValueRevision;
        [ProtoMember(8)] public long RequestId;
        [ProtoMember(9)] public long Sequence;
        [ProtoMember(10)] public long LeaseId;
        [ProtoMember(11)] public int Status;
        [ProtoMember(12)] public double Value;
        [ProtoMember(13)] public long TileId;
        [ProtoMember(14)] public long CharacterId;
        [ProtoMember(15)] public bool Terminal;
        [ProtoMember(16)] public long MinimumSequence;
        [ProtoMember(17)] public long ViewerId;
    }
    public static class UiDragWire
    {
        public const int PacketLimit = 512;
        static readonly byte[] Prefix = { 72, 68, 82, 68, 49 }; // HDRD1, existing UI channel.
        public static bool IsDrag(byte[] data)
        {
            if (data == null || data.Length < Prefix.Length) return false;
            for (int i = 0; i < Prefix.Length; i++) if (data[i] != Prefix[i]) return false;
            return true;
        }
        public static byte[] Wrap(byte[] body)
        {
            if (body == null || body.Length == 0 || body.Length + Prefix.Length > PacketLimit) return null;
            var bytes = new byte[body.Length + Prefix.Length];
            Array.Copy(Prefix, bytes, Prefix.Length); Array.Copy(body, 0, bytes, Prefix.Length, body.Length); return bytes;
        }
        public static byte[] Body(byte[] data)
        {
            if (!IsDrag(data) || data.Length > PacketLimit || data.Length == Prefix.Length) return null;
            var body = new byte[data.Length - Prefix.Length]; Array.Copy(data, Prefix.Length, body, 0, body.Length); return body;
        }
        public static bool Id(string value)
        {
            if (value == null || value.Length < 1 || value.Length > 24) return false;
            foreach (char c in value) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' && c != '_') return false;
            return true;
        }
        public static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x) && Math.Abs(x) <= 1e12; }
        public static bool Valid(UiDragRequest p)
        {
            return p != null && p.Protocol == 1 && p.Kind >= 1 && p.Kind <= 7 && p.CallerId != 0 && p.TargetId != 0
                && Id(p.ControlId) && p.DefinitionRevision > 0 && p.ValueRevision > 0 && p.RequestId > 0 && p.Sequence > 0
                && (p.Mode == 0 || p.Mode == 1) && Finite(p.Value)&&p.ViewerId>=0
                && (p.Kind == (int)UiDragKind.Begin||p.Kind==(int)UiDragKind.Focus ? p.LeaseId == 0 : p.Kind == (int)UiDragKind.Cancel||p.Kind==(int)UiDragKind.Blur ? p.LeaseId >= 0 : p.LeaseId > 0)
                && (p.Kind!=(int)UiDragKind.KeepAlive||p.ViewerId>0&&p.LeaseId==p.ViewerId)
                && (p.Kind!=(int)UiDragKind.Blur||p.ViewerId==0&&p.LeaseId==0||p.ViewerId>0&&p.LeaseId==p.ViewerId);
        }
        public static bool Valid(UiDragAck p)
        {
            return p != null && p.Protocol == 1 && p.Kind >= 1 && p.Kind <= 7 && p.CallerId != 0 && p.TargetId != 0
                && Id(p.ControlId) && p.DefinitionRevision > 0 && p.ValueRevision > 0 && p.RequestId > 0 && p.Sequence > 0
                && p.Status >= 0 && p.Status <= 8 && Finite(p.Value) && p.MinimumSequence>=0&&p.ViewerId>=0
                && (p.Status != 0 || p.LeaseId > 0 && p.TileId != 0 && p.CharacterId != 0);
        }
        internal static UiDragRequest Copy(UiDragRequest p)
        { return new UiDragRequest { Protocol=p.Protocol,Kind=p.Kind,CallerId=p.CallerId,TargetId=p.TargetId,ControlId=p.ControlId,
            DefinitionRevision=p.DefinitionRevision,ValueRevision=p.ValueRevision,RequestId=p.RequestId,Sequence=p.Sequence,
            LeaseId=p.LeaseId,Mode=p.Mode,Value=p.Value,ViewerId=p.ViewerId }; }
    }
}
