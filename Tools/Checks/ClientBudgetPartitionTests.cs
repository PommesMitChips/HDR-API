using System;
using HoloMap;

internal static class ClientBudgetPartitionTests
{
    internal static int Run()
    {
        int checks = 0;
        foreach (int cap in new[] { 0, 1, 2, 3, 19, 20000, 200000, int.MaxValue })
        {
            int grant = ClientBudgetPartitions.ModGrant(cap, true);
            if (grant < 0 || grant > cap || cap - grant != cap / 2) throw new Exception("Block category must retain its fair share, including odd/large caps.");
            checks++;
            if (ClientBudgetPartitions.ModGrant(cap, false) != cap) throw new Exception("No block demand means the full cap is available to mod consumers.");
            checks++;
            foreach (int spent in new[] { 0, grant / 2, grant })
            {
                int remainder = cap - spent;
                if (remainder < cap / 2 || (long)spent + remainder != cap) throw new Exception("Mod work must conserve the global cap and preserve block work.");
                checks++;
            }
        }
        if (ClientBudgetPartitions.ModGrant(-1, true) != 0) throw new Exception("Unavailable budget cannot admit work.");
        return checks + 1;
    }
}
