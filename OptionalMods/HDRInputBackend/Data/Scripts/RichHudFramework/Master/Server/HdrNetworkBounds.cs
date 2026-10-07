using System.Collections.Generic;
using VRage;

namespace RichHudFramework.Server
{
    internal static class HdrNetworkBounds
    {
        internal static bool PacketAllowed(ushort channel, ushort expected, int length, int queued,
            bool server, bool client, bool fromServer, ulong sender, ulong serverId)
        {
            if (channel != expected || length < 1 || length > RhServer.MaxPacketBytes
                || queued >= RhServer.MaxQueueMessages) return false;
            return fromServer ? client && (sender == 0 || sender == serverId) : server && sender != 0;
        }

        internal static bool BatchAllowed(int length, int queued)
        {
            return length >= 0 && length <= RhServer.MaxBatchMessages
                && queued >= 0 && queued <= RhServer.MaxQueueMessages - length;
        }
    }

    internal sealed class HdrRequestBudget
    {
        private readonly Dictionary<ulong, MyTuple<long, int>> peers = new Dictionary<ulong, MyTuple<long, int>>();
        internal bool TryConsume(ulong peer, long tick, int requests)
        {
            if (peer == 0 || requests < 1 || requests > 20) return false;
            MyTuple<long, int> state;
            if (!peers.TryGetValue(peer, out state))
            {
                if (peers.Count >= 256) return false;
                state = new MyTuple<long, int>(tick, 0);
            }
            if (tick - state.Item1 >= 60) state = new MyTuple<long, int>(tick, 0);
            if (requests > 20 - state.Item2) return false;
            state.Item2 += requests;
            peers[peer] = state;
            return true;
        }
        internal void Prune(long tick)
        {
            var stale = new List<ulong>();
            foreach (var peer in peers)
                if (tick - peer.Value.Item1 > 600) stale.Add(peer.Key);
            foreach (ulong peer in stale) peers.Remove(peer);
        }
    }
}
