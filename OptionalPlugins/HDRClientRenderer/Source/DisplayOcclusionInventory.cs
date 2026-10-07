using System;
using System.Collections.Generic;

namespace HDRClientRenderer
{
    internal sealed class DisplayOcclusionInventory
    {
        internal sealed class Consumer
        {
            internal readonly string Key;
            internal readonly object Geometry;
            internal readonly double[] WorldTriangles;
            internal Consumer(string key, object geometry, double[] triangles)
            { Key = key; Geometry = geometry; WorldTriangles = triangles; }
        }
        internal readonly object Identity;
        internal readonly Consumer[] Consumers;
        DisplayOcclusionInventory(object identity, Consumer[] consumers) { Identity = identity; Consumers = consumers; }
        internal static DisplayOcclusionInventory Read(bool complete, object identity, Consumer[] consumers)
        {
            if (!complete || identity == null || consumers == null || consumers.Length < 1 || consumers.Length > DisplayOcclusion.MaxEntries) return null;
            var keys = new HashSet<string>(StringComparer.Ordinal); var copy = new Consumer[consumers.Length];
            for (int i = 0; i < consumers.Length; i++)
            {
                var c = consumers[i];
                if (c == null || string.IsNullOrEmpty(c.Key) || c.Key.Length > 128 || c.Geometry == null || !keys.Add(c.Key) ||
                    c.WorldTriangles == null || c.WorldTriangles.Length < 9 || c.WorldTriangles.Length > DisplayOcclusionProjection.MaxVertices * 3 || c.WorldTriangles.Length % 9 != 0) return null;
                var values = (double[])c.WorldTriangles.Clone();
                foreach (double v in values) if (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 1e9) return null;
                copy[i] = new Consumer(c.Key, c.Geometry, values);
            }
            return new DisplayOcclusionInventory(identity, copy);
        }
        internal bool Same(DisplayOcclusionInventory current)
        {
            if (current == null || !ReferenceEquals(Identity, current.Identity) || Consumers.Length != current.Consumers.Length) return false;
            for (int i = 0; i < Consumers.Length; i++)
            {
                var a = Consumers[i]; var b = current.Consumers[i];
                if (a.Key != b.Key || !ReferenceEquals(a.Geometry, b.Geometry) || a.WorldTriangles.Length != b.WorldTriangles.Length) return false;
                for (int j = 0; j < a.WorldTriangles.Length; j++) if (a.WorldTriangles[j] != b.WorldTriangles[j]) return false;
            }
            return true;
        }
        internal bool AllHidden(DisplayOcclusionInventory current, Func<Consumer, DisplayOcclusion.Decision> probe)
        {
            if (!Same(current) || probe == null) return false;
            foreach (var consumer in Consumers) if (probe(consumer).KeepDemand) return false;
            return true;
        }
    }
}
