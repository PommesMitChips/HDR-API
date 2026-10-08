using System;
using VRage;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        // Unlike measure-text, this deliberately runs the bounded CPU geometry compiler.
        // Nothing is retained. Calling the corresponding artwork upsert compiles again.
        static object GeometryCostCommand(DrawArgs arguments)
        {
            string kind = arguments.Text(), source = arguments.Text();
            double height = arguments.Number(1); int segments = arguments.Integer(12); arguments.End();
            if (!Geometry.Finite(height) || height <= 0 || height > 1000000)
                throw new ArgumentException("Geometry cost requires positive finite height up to 1,000,000.");
            Geometry geometry;
            if (kind == "text") geometry = VectorFont.Text(source, height, "start");
            else if (kind == "svg") geometry = Svg.Parse(source, segments).Geometry;
            else throw new ArgumentException("Geometry cost kind requires text or svg.");
            return new MyTuple<int, int>(geometry.Points.Length, geometry.Triangles.Length / 3 + geometry.Edges.Length / 2);
        }
        static MyTuple<int, int> ModClientGeometryUsage(ModClientOwner owner)
        {
            int points = 0, primitives = 0;
            foreach (var context in owner.Contexts.Values) foreach (var item in context.Items.Values)
            {
                points += item.Geometry.Points.Length;
                primitives += item.Geometry.Triangles.Length / 3 + item.Geometry.Edges.Length / 2;
            }
            return new MyTuple<int, int>(points, primitives);
        }
    }
}
