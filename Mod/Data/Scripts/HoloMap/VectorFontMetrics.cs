using System;
using System.Collections.Generic;
using System.Globalization;
using VRage;

namespace HoloMap
{
    public static partial class VectorFont
    {
        // Metrics read the same packaged, quantized outlines as Text. They never construct Geometry,
        // charge GeometryWork, or populate/evict the renderer's glyph Geometry cache.
        public const string MetricProfile = "HDR.TextMetrics/1:Inter:cap-height";
        public const int MetricGeneration = 1;
        sealed class MetricGlyph
        {
            public double Advance, MinX, MinY, MaxX, MaxY;
            public bool Ink;
        }
        static readonly Dictionary<int, MetricGlyph> MetricCache = new Dictionary<int, MetricGlyph>();
        static MetricGlyph ReadMetric(int code)
        {
            if (!Outlines.ContainsKey(code)) code = '?';
            MetricGlyph glyph;
            if (MetricCache.TryGetValue(code, out glyph)) return glyph;
            string[] parts = Outlines[code].Split(';');
            glyph = new MetricGlyph { Advance = int.Parse(parts[0], CultureInfo.InvariantCulture) / 1000.0 };
            if (parts[1].Length > 0 && parts[2].Length > 0)
            {
                string[] points = parts[1].Split(','), indices = parts[2].Split(',');
                // Bounds include only vertices actually referenced by rendered triangles.
                foreach (string encoded in indices)
                {
                    int index = int.Parse(encoded, CultureInfo.InvariantCulture) * 2;
                    double x = int.Parse(points[index], CultureInfo.InvariantCulture) / 1000000.0;
                    double y = int.Parse(points[index + 1], CultureInfo.InvariantCulture) / 1000000.0;
                    if (!glyph.Ink) { glyph.MinX = glyph.MaxX = x; glyph.MinY = glyph.MaxY = y; glyph.Ink = true; }
                    else { glyph.MinX = Math.Min(glyph.MinX, x); glyph.MinY = Math.Min(glyph.MinY, y); glyph.MaxX = Math.Max(glyph.MaxX, x); glyph.MaxY = Math.Max(glyph.MaxY, y); }
                }
            }
            // A bounded independent cache. All cached entries derive solely from immutable packaged data.
            if (MetricCache.Count >= 192) MetricCache.Clear();
            MetricCache[code] = glyph;
            return glyph;
        }

        /// <summary>
        /// Start-anchored bounds in source Y-up coordinates, including Text's cap-centre offset -height/2.
        /// Layout tuple: maximum line advance, cap height, line advance. Ink tuple: minX,minY,maxX,maxY.
        /// Empty/space-only text has zero ink bounds and false; whitespace still contributes advance.
        /// Height is the packaged cap-height scale, NOT CSS em. No exact em ratio was retained by the bake.
        /// Ink is source-outline ink before mesh degeneracy filtering, clipping, rasterization or surface projection.
        /// </summary>
        public static MyTuple<string, MyTuple<double, double, double>, MyTuple<double, double, double, double>, int, bool>
            MeasureText(string text, double height = 1, double lineHeight = 1.3)
        {
            if (text == null || text.Length > 64 || !Geometry.Finite(height) || height <= 0 || height > 1000000)
                throw new ArgumentException("Text requires at most 64 characters and positive finite height.");
            if (!Geometry.Finite(lineHeight) || lineHeight < 1 || lineHeight > 4)
                throw new ArgumentException("Text line height must be 1–4.");
            double x = 0, maximum = 0, minX = 0, minY = 0, maxX = 0, maxY = 0;
            int row = 0; bool ink = false;
            for (int i = 0; i < text.Length; i++)
            {
                int code = text[i];
                if (code == '\r' || code == '\n')
                {
                    if (code == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    maximum = Math.Max(maximum, x); x = 0; row++; continue;
                }
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    code = 0x10000 + (text[i] - 0xd800) * 1024 + (text[++i] - 0xdc00);
                MetricGlyph glyph = ReadMetric(code == '\t' ? ' ' : code);
                if (glyph.Ink)
                {
                    double left = (x + glyph.MinX) * height, right = (x + glyph.MaxX) * height;
                    double offset = -0.5 - row * lineHeight;
                    double bottom = (glyph.MinY + offset) * height, top = (glyph.MaxY + offset) * height;
                    if (!ink) { minX = left; minY = bottom; maxX = right; maxY = top; ink = true; }
                    else { minX = Math.Min(minX, left); minY = Math.Min(minY, bottom); maxX = Math.Max(maxX, right); maxY = Math.Max(maxY, top); }
                }
                x += glyph.Advance * (code == '\t' ? 4 : 1);
            }
            maximum = Math.Max(maximum, x);
            return new MyTuple<string, MyTuple<double, double, double>, MyTuple<double, double, double, double>, int, bool>(
                MetricProfile, new MyTuple<double, double, double>(maximum * height, height, lineHeight * height),
                new MyTuple<double, double, double, double>(minX, minY, maxX, maxY), MetricGeneration, ink);
        }
    }

    public sealed partial class HoloMapSession
    {
        static object MeasureTextCommand(DrawArgs arguments)
        {
            string text = arguments.Text(); double height = arguments.Number(1), lineHeight = arguments.Number(1.3);
            arguments.End(); return VectorFont.MeasureText(text, height, lineHeight);
        }
    }
}
