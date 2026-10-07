namespace HoloMap
{
    // Separate client consumers cannot exhaust the block-display category.
    // The viewer's global cap remains the upper bound for both categories.
    public static class ClientBudgetPartitions
    {
        public static int ModGrant(int available, bool blockDemand)
        {
            if (available <= 0) return 0;
            return blockDemand ? available / 2 + (available & 1) : available;
        }
    }
}
