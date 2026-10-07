using System;

namespace HDRClientRenderer
{
    // Immutable source-UV demand. It survives service packet lifetime and is
    // passed to the native off-axis tile planner, not used as a cosmetic warp.
    internal sealed class PanoramaDensity
    {
        internal int Ceiling
        {
            get { double bound=0;foreach(var cell in cells)if(cell.Covered)bound=Math.Max(bound,Math.Max(cell.UpperMagnification,Math.Sqrt(cell.UpperArea)));
                int side=64;while(side<bound&&side<2048)side*=2;return side; }
        }
        internal const int Grid = 16, CellCount = Grid * Grid, Fields = 7;
        internal readonly struct Cell
        {
            internal readonly bool Covered, HasSample;
            internal readonly double UpperMagnification, UpperArea, Magnification, Area, BlendWeight;
            internal Cell(bool covered, double upperMagnification, double upperArea, double magnification, double area, double blendWeight, bool hasSample)
            { Covered = covered; UpperMagnification = upperMagnification; UpperArea = upperArea; Magnification = magnification; Area = area; BlendWeight = blendWeight; HasSample = hasSample; }
        }
        readonly Cell[] cells;
        internal readonly bool Known, Fresh;
        PanoramaDensity(Cell[] cells, bool known)
        {
            this.cells = cells; Known = known;
            foreach (var cell in cells) if (cell.Covered && cell.HasSample) { Fresh = true; break; }
        }
        internal Cell At(int x, int y)
        {
            if (x < 0 || x >= Grid || y < 0 || y >= Grid) throw new ArgumentOutOfRangeException();
            return cells[y * Grid + x];
        }
        internal static PanoramaDensity Full(int maximum)
        {
            maximum = Math.Max(1, Math.Min(2048, maximum));
            var cells = new Cell[CellCount];
            for (int i = 0; i < cells.Length; i++) cells[i] = new Cell(true, maximum, (double)maximum * maximum, 0, 0, 1, false);
            return new PanoramaDensity(cells, false);
        }
        internal static PanoramaDensity FromPacked(double[] values, int maximum, bool known)
        {
            if (!known || values == null || values.Length != CellCount * Fields || maximum < 1 || maximum > 2048) return Full(maximum);
            var cells = new Cell[CellCount]; double maxArea = (double)maximum * maximum;
            for (int i = 0; i < cells.Length; i++)
            {
                int start = i * Fields;
                for (int f = 0; f < Fields; f++) if (!PanoramaStore.Finite(values[start + f])) return Full(maximum);
                if (values[start] != 0 && values[start] != 1 || values[start + 1] < 0 || values[start + 1] > maximum ||
                    values[start + 2] < 0 || values[start + 2] > maxArea || values[start + 3] < 0 || values[start + 3] > maximum ||
                    values[start + 4] < 0 || values[start + 4] > maxArea || values[start + 5] < 0 || values[start + 5] > 1 ||
                    values[start + 6] != 0 && values[start + 6] != 1) return Full(maximum);
                cells[i] = new Cell(values[start] == 1, values[start + 1], values[start + 2], values[start + 3], values[start + 4], values[start + 5], values[start + 6] == 1);
            }
            return new PanoramaDensity(cells, true);
        }
        internal PanoramaDensity Clamp(int maximum)
        {
            maximum = Math.Max(1, Math.Min(2048, maximum)); double area = (double)maximum * maximum;
            var result = new Cell[CellCount];
            for (int i = 0; i < result.Length; i++)
            {
                var cell = cells[i];
                result[i] = new Cell(cell.Covered, Math.Min(maximum, cell.UpperMagnification), Math.Min(area, cell.UpperArea),
                    Math.Min(maximum, cell.Magnification), Math.Min(area, cell.Area), cell.BlendWeight, cell.HasSample);
            }
            return new PanoramaDensity(result, Known);
        }
        internal static PanoramaDensity Union(PanoramaDensity left, PanoramaDensity right, int maximum)
        {
            if (left == null) return right == null ? Full(maximum) : right.Clamp(maximum);
            if (right == null) return left.Clamp(maximum);
            // A stale/unknown consumer can need any part of the full source.
            if (!left.Known || !right.Known) return Full(maximum);
            var result = new Cell[CellCount];
            for (int i = 0; i < result.Length; i++)
            {
                var a = left.cells[i]; var b = right.cells[i]; bool covered = a.Covered || b.Covered;
                bool sample = covered && (!a.Covered || a.HasSample) && (!b.Covered || b.HasSample);
                result[i] = new Cell(covered, Math.Max(a.UpperMagnification, b.UpperMagnification), Math.Max(a.UpperArea, b.UpperArea),
                    Math.Max(a.Magnification, b.Magnification), Math.Max(a.Area, b.Area), Math.Max(a.BlendWeight, b.BlendWeight), sample);
            }
            return new PanoramaDensity(result, true).Clamp(maximum);
        }
    }
}
