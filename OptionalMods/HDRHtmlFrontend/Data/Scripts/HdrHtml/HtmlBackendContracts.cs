using System;
using System.Collections.Generic;
using Hdr.Mods;
using VRage;
using VRageMath;

namespace Hdr.Html
{
    public interface IHtmlPainter : IDisposable
    {
        bool TryPaint(HtmlPaintFrame frame, out HtmlPaintReport report);
        void Reset();
    }

    public sealed class HtmlPainterLimits
    {
        public int MaxItems = 64, MaxPoints = 8192, MaxPrimitives = 8192;
        public int MaxSvgCharacters = 65536, MaxOperations = 512;
        public int SvgOperationsPerChunk = 8, CurveSegments = 12;
        internal void Validate()
        {
            if (MaxItems < 1 || MaxItems > 64 || MaxPoints < 1 || MaxPoints > 8192 || MaxPrimitives < 1 || MaxPrimitives > 8192 || MaxSvgCharacters < 1 || MaxSvgCharacters > 65536 || MaxOperations < 1 || MaxOperations > 512 || SvgOperationsPerChunk < 1 || SvgOperationsPerChunk > 32 || CurveSegments < 2 || CurveSegments > 32) throw new ArgumentException("HTML painter limits exceed the current HDR grants.");
        }
        internal HtmlPainterLimits Snapshot(){return new HtmlPainterLimits {MaxItems=MaxItems,MaxPoints=MaxPoints,MaxPrimitives=MaxPrimitives,MaxSvgCharacters=MaxSvgCharacters,MaxOperations=MaxOperations,SvgOperationsPerChunk=SvgOperationsPerChunk,CurveSegments=CurveSegments};}
    }

    public sealed class HtmlPaintReport
    {
        public string Backend, Error;
        public bool Success, Changed, RestoredPrevious;
        public int MutatingCalls, PreparedOperations, EstimatedPoints, EstimatedTriangles;
        public long Revision, EndpointGeneration;
    }

    /// <summary>Small injectable renderer boundary. Consumers normally use HtmlHdrDrawApi.</summary>
    public interface IHtmlDrawApi
    {
        bool Ready { get; }
        long Generation { get; }
        object Call(string command, params object[] arguments);
    }

    public sealed class HtmlHdrDrawApi : IHtmlDrawApi
    {
        readonly HdrModApi api;
        public HtmlHdrDrawApi(HdrModApi api) { if (api == null) throw new ArgumentNullException("api"); this.api = api; }
        public bool Ready { get { return api.Ready; } }
        public long Generation { get { return api.ConnectionGeneration; } }
        public object Call(string command, params object[] arguments) { return api.Call(command, arguments); }
    }

    public sealed class HtmlPreparedItem
    {
        public string Id, Kind, Text, Svg, Material;
        public int Order, Points, Primitives;
        public Vector3D[] Vertices;
        public int[] Triangles;
        public Vector2[] UV;
        public Vector4 Color;
        public MatrixD Pose = MatrixD.Identity;
        public Vector3D Position;
        public double Height;
        public int Segments;
        internal bool SameContent(HtmlPreparedItem other)
        {
            if (other == null || Kind != other.Kind || Text != other.Text || Svg != other.Svg || Material != other.Material || Color != other.Color || Pose != other.Pose || Position != other.Position || Height != other.Height || Segments != other.Segments) return false;
            if (!Equal(Vertices, other.Vertices) || !Equal(Triangles, other.Triangles) || !Equal(UV, other.UV)) return false;
            return true;
        }
        static bool Equal<T>(T[] a, T[] b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Length != b.Length) return false;
            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < a.Length; i++) if (!comparer.Equals(a[i], b[i])) return false;
            return true;
        }
    }
}
