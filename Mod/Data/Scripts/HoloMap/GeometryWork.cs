using System;

namespace HoloMap
{
    // Geometry compilation runs synchronously on the simulation thread. Nested
    // operations share one allowance, including attempts that produce no output.
    public static class GeometryWork
    {
        public const int DefaultLimit = 200000;
        static Budget current;
        sealed class Budget { public int Remaining; }
        sealed class Scope : IDisposable
        {
            readonly Budget previous;
            bool disposed;
            public Scope(Budget prior) { previous = prior; }
            public void Dispose() { if (!disposed) { current = previous; disposed = true; } }
        }
        public static IDisposable Begin(int limit = DefaultLimit)
        {
            if (limit < 1 || limit > DefaultLimit) throw new ArgumentException("Invalid geometry work allowance.");
            var previous = current;
            if (current == null) current = new Budget { Remaining = limit };
            return new Scope(previous);
        }
        public static void Charge(int amount = 1)
        {
            if (amount < 0) throw new ArgumentException("Invalid geometry work charge.");
            if (current == null) return;
            if (amount > current.Remaining) { current.Remaining = 0; throw new ArgumentException("Geometry compilation work budget exceeded; simplify the artwork."); }
            current.Remaining -= amount;
        }
    }
}
