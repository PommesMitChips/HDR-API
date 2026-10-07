using System;
using VRageMath;

namespace HDR.Interactions
{
    /// <summary>Finite canonical scalar domain. Step zero is continuous.</summary>
    public struct NumericRange
    {
        public readonly double Min;
        public readonly double Max;
        public readonly double Step;
        public readonly double Span;
        private readonly bool _valid;

        private NumericRange(double min, double max, double step, double span)
        {
            Min = min;
            Max = max;
            Step = step;
            Span = span;
            _valid = true;
        }

        public bool IsValid { get { return _valid; } }

        public static bool TryCreate(double min, double max, double step, out NumericRange range)
        {
            range = default(NumericRange);
            if (!NumericGeometry.IsScalar(min) || !NumericGeometry.IsScalar(max)
                || !NumericGeometry.IsScalar(step) || min >= max || step < 0)
                return false;
            double span = max - min;
            if (!NumericGeometry.IsScalar(span) || span <= 0 || step > span)
                return false;
            if (step > 0 && (span / step > 1000000000.0 || min + step <= min || max - step >= max))
                return false;
            range = new NumericRange(min, max, step, span);
            return true;
        }

        /// <summary>Clamp and snap to a min-anchored grid; max remains a reachable endpoint.</summary>
        public bool TryNormalize(double requested, out double value)
        {
            value = Min;
            if (!_valid || !NumericGeometry.IsScalar(requested))
                return false;
            if (requested <= Min)
                return true;
            if (requested >= Max)
            {
                value = Max;
                return true;
            }
            if (Step == 0)
            {
                value = requested;
                return true;
            }
            double lastIndex = Math.Floor(Span / Step);
            double lowIndex = NumericGeometry.Clamp(Math.Floor((requested - Min) / Step), 0, lastIndex);
            double highIndex = Math.Min(lowIndex + 1, lastIndex);
            double low = NumericGeometry.Clamp(Min + lowIndex * Step, Min, Max);
            double high = NumericGeometry.Clamp(Min + highIndex * Step, Min, Max);
            // Compare represented candidates rather than adding 0.5 to a quotient: that
            // rounds just-below-midpoint inputs upward and ignores large-offset ULP ties.
            double grid = Math.Abs(high - requested) <= Math.Abs(low - requested) ? high : low;
            value = Max - requested <= Math.Abs(grid - requested) ? Max : grid;
            return true;
        }
    }

    /// <summary>A finite object-local forward ray with a unit direction.</summary>
    public struct LocalRay
    {
        public readonly Vector3D Origin;
        public readonly Vector3D Direction;
        private readonly bool _valid;

        private LocalRay(Vector3D origin, Vector3D direction)
        {
            Origin = origin;
            Direction = direction;
            _valid = true;
        }

        public bool IsValid { get { return _valid; } }

        public static bool TryCreate(Vector3D origin, Vector3D unitDirection, out LocalRay ray)
        {
            ray = default(LocalRay);
            if (!NumericGeometry.IsPoint(origin) || !NumericGeometry.IsPoint(unitDirection))
                return false;
            double length = Math.Sqrt(NumericGeometry.Dot(unitDirection, unitDirection));
            if (Math.Abs(length - 1) > 0.000001)
                return false;
            ray = new LocalRay(origin, unitDirection / length);
            return true;
        }

        public static bool TryFromDirection(Vector3D origin, Vector3D direction, out LocalRay ray)
        {
            ray = default(LocalRay);
            Vector3D unit;
            if (!NumericGeometry.IsPoint(origin) || !NumericGeometry.TryFiniteUnit(direction, out unit))
                return false;
            ray = new LocalRay(origin, unit);
            return true;
        }
    }

    /// <summary>Immutable line or polyline parameterized by physical arc length.</summary>
    public sealed class PathConstraint
    {
        public readonly NumericRange Range;
        public readonly double Length;
        private readonly Vector3D[] _points;
        private readonly Vector3D[] _directions;
        private readonly double[] _arcs;
        private readonly double[] _lengths;

        private PathConstraint(NumericRange range, Vector3D[] points, Vector3D[] directions,
            double[] arcs, double[] lengths, double length)
        {
            Range = range;
            Length = length;
            _points = points;
            _directions = directions;
            _arcs = arcs;
            _lengths = lengths;
        }

        public int PointCount { get { return _points.Length; } }
        public int SegmentCount { get { return _lengths.Length; } }

        public static bool TryLine(NumericRange range, Vector3D start, Vector3D end, out PathConstraint path)
        {
            return TryPolyline(range, new Vector3D[] { start, end }, out path);
        }

        public static bool TryPolyline(NumericRange range, Vector3D[] points, out PathConstraint path)
        {
            path = null;
            if (!range.IsValid || points == null || points.Length < 2 || points.Length > 256)
                return false;
            Vector3D[] copy = new Vector3D[points.Length];
            Vector3D[] directions = new Vector3D[points.Length - 1];
            double[] arcs = new double[points.Length];
            double[] lengths = new double[points.Length - 1];
            double total = 0;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3D point = points[i];
                if (!NumericGeometry.IsPoint(point))
                    return false;
                copy[i] = point;
                arcs[i] = total;
                if (i == 0)
                    continue;
                Vector3D difference = copy[i] - copy[i - 1];
                double length = Math.Sqrt(NumericGeometry.Dot(difference, difference));
                if (!NumericGeometry.IsFinite(length) || length <= 0.000000001)
                    return false;
                lengths[i - 1] = length;
                directions[i - 1] = difference / length;
                double nextTotal = total + length;
                if (!NumericGeometry.IsFinite(nextTotal) || nextTotal <= total)
                    return false;
                total = nextTotal;
                arcs[i] = total;
            }
            path = new PathConstraint(range, copy, directions, arcs, lengths, total);
            return true;
        }

        public bool TryGetPoint(int index, out Vector3D point)
        {
            point = default(Vector3D);
            if (index < 0 || index >= _points.Length)
                return false;
            point = _points[index];
            return true;
        }

        public bool TryEvaluate(double value, out Vector3D point)
        {
            point = default(Vector3D);
            double arc;
            if (!TryArcFromValue(value, out arc))
                return false;
            int segment = SegmentAt(arc);
            point = _points[segment] + _directions[segment] * (arc - _arcs[segment]);
            // Preserve authored endpoints exactly, including folded paths and tiny segments.
            if (arc == _arcs[segment])
                point = _points[segment];
            else if (arc == _arcs[segment + 1])
                point = _points[segment + 1];
            return NumericGeometry.IsPoint(point);
        }

        public bool TryGetTranslationDelta(double fromValue, double toValue, out MatrixD delta)
        {
            delta = MatrixD.Identity;
            Vector3D from;
            Vector3D to;
            if (!TryEvaluate(fromValue, out from) || !TryEvaluate(toValue, out to))
                return false;
            Vector3D translation = to - from;
            delta.M41 = translation.X;
            delta.M42 = translation.Y;
            delta.M43 = translation.Z;
            return true;
        }

        internal bool TryArcFromValue(double requested, out double arc)
        {
            arc = 0;
            double value;
            if (!Range.TryNormalize(requested, out value))
                return false;
            arc = ((value - Range.Min) / Range.Span) * Length;
            if (value == Range.Max)
                arc = Length;
            return true;
        }

        internal bool TryValueFromArc(double arc, out double value)
        {
            value = Range.Min;
            if (!NumericGeometry.IsFinite(arc))
                return false;
            arc = NumericGeometry.Clamp(arc, 0, Length);
            return Range.TryNormalize(arc == Length ? Range.Max : Range.Min + (arc / Length) * Range.Span, out value);
        }

        internal int SegmentAt(double arc)
        {
            for (int i = 0; i < _lengths.Length - 1; i++)
                if (arc < _arcs[i + 1])
                    return i;
            return _lengths.Length - 1;
        }

        internal double StartArc(int segment) { return _arcs[segment]; }
        internal double EndArc(int segment) { return _arcs[segment + 1]; }

        internal bool TryClosest(int segment, LocalRay ray, out PathCandidate candidate)
        {
            candidate = default(PathCandidate);
            Vector3D start = _points[segment];
            Vector3D direction = _directions[segment];
            Vector3D relative = start - ray.Origin;
            double rayDot = NumericGeometry.Dot(ray.Direction, relative);
            double coupling = NumericGeometry.Dot(direction, ray.Direction);
            double segmentLength = _lengths[segment];
            if (Math.Max(rayDot, rayDot + coupling * segmentLength) < 0)
                return false;
            // Cross products avoid subtracting nearly equal O(model-size) dot terms
            // and avoid the precision loss of 1 - cosine^2 near parallel views.
            Vector3D normal = NumericGeometry.Cross(direction, ray.Direction);
            double denominator = NumericGeometry.Dot(normal, normal);
            if (denominator <= 0.0000000001)
                return false;
            double raw = NumericGeometry.Dot(NumericGeometry.Cross(ray.Direction, relative), normal) / denominator;
            double rawRayDistance = NumericGeometry.Dot(NumericGeometry.Cross(direction, relative), normal) / denominator;
            // The outer tangent follows the forward ray, rather than an infinite line
            // behind its origin. Preserve separate finite-segment metrics for selection.
            double pointerAlong = rawRayDistance < 0 ? -NumericGeometry.Dot(direction, relative) : raw;
            double along = NumericGeometry.Clamp(raw, 0, segmentLength);
            double rayDistance = rayDot + coupling * along;
            if (rayDistance < 0)
            {
                rayDistance = 0;
                along = NumericGeometry.Clamp(-NumericGeometry.Dot(direction, relative), 0, segmentLength);
            }
            Vector3D separation = relative + direction * along - ray.Direction * rayDistance;
            double distanceSquared = NumericGeometry.Dot(separation, separation);
            double pointerArc = _arcs[segment] + along;
            if ((segment == 0 && pointerAlong < 0)
                || (segment == _lengths.Length - 1 && pointerAlong > segmentLength))
                pointerArc = _arcs[segment] + pointerAlong;
            if (!NumericGeometry.IsFinite(raw) || !NumericGeometry.IsFinite(rawRayDistance)
                || !NumericGeometry.IsFinite(pointerArc) || !NumericGeometry.IsFinite(distanceSquared))
                return false;
            candidate = new PathCandidate(segment, _arcs[segment] + along, pointerArc, distanceSquared);
            return true;
        }
    }

    /// <summary>Immutable angular constraint, with radians measured from Reference toward Tangent.</summary>
    public sealed class RotationConstraint
    {
        public readonly NumericRange Range;
        public readonly Vector3D Pivot;
        public readonly Vector3D Axis;
        public readonly Vector3D Reference;
        public readonly Vector3D Tangent;

        private RotationConstraint(NumericRange range, Vector3D pivot, Vector3D axis,
            Vector3D reference, Vector3D tangent)
        {
            Range = range;
            Pivot = pivot;
            Axis = axis;
            Reference = reference;
            Tangent = tangent;
        }

        /// <summary>Derives a stable reference from the coordinate axis least aligned with Axis.</summary>
        public static bool TryCreate(NumericRange range, Vector3D pivot, Vector3D axis,
            out RotationConstraint rotation)
        {
            rotation = null;
            Vector3D unit;
            if (!NumericGeometry.TryUnit(axis, out unit))
                return false;
            double x = Math.Abs(unit.X);
            double y = Math.Abs(unit.Y);
            double z = Math.Abs(unit.Z);
            Vector3D reference = x <= y && x <= z ? new Vector3D(1, 0, 0)
                : y <= z ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1);
            return TryCreate(range, pivot, unit, reference, out rotation);
        }

        public static bool TryCreate(NumericRange range, Vector3D pivot, Vector3D axis,
            Vector3D reference, out RotationConstraint rotation)
        {
            rotation = null;
            Vector3D unitAxis;
            if (!range.IsValid || Math.Abs(range.Min) > 1000000 || Math.Abs(range.Max) > 1000000
                || range.Span > 64 * Math.PI || !NumericGeometry.IsPoint(pivot)
                || !NumericGeometry.TryUnit(axis, out unitAxis) || !NumericGeometry.IsPoint(reference))
                return false;
            double referenceLength = Math.Sqrt(NumericGeometry.Dot(reference, reference));
            Vector3D projected = reference - unitAxis * NumericGeometry.Dot(reference, unitAxis);
            double projectedLength = Math.Sqrt(NumericGeometry.Dot(projected, projected));
            if (projectedLength <= 0.000000000001 || projectedLength <= referenceLength * 0.00000001)
                return false;
            Vector3D unitReference = projected / projectedLength;
            Vector3D tangent = NumericGeometry.Cross(unitAxis, unitReference);
            rotation = new RotationConstraint(range, pivot, unitAxis, unitReference, tangent);
            return true;
        }

        public bool TryGetRotationDelta(double fromValue, double toValue, out MatrixD delta)
        {
            delta = MatrixD.Identity;
            double from;
            double to;
            if (!Range.TryNormalize(fromValue, out from) || !Range.TryNormalize(toValue, out to))
                return false;
            double angle = (to - from) % (2 * Math.PI);
            double cosine = Math.Cos(angle);
            double sine = Math.Sin(angle);
            double t = 1 - cosine;
            double x = Axis.X;
            double y = Axis.Y;
            double z = Axis.Z;
            // VRageMath transforms row vectors. Translation keeps the authored pivot fixed.
            delta.M11 = cosine + x * x * t;
            delta.M12 = x * y * t + z * sine;
            delta.M13 = x * z * t - y * sine;
            delta.M21 = x * y * t - z * sine;
            delta.M22 = cosine + y * y * t;
            delta.M23 = y * z * t + x * sine;
            delta.M31 = x * z * t + y * sine;
            delta.M32 = y * z * t - x * sine;
            delta.M33 = cosine + z * z * t;
            delta.M41 = Pivot.X - (Pivot.X * delta.M11 + Pivot.Y * delta.M21 + Pivot.Z * delta.M31);
            delta.M42 = Pivot.Y - (Pivot.X * delta.M12 + Pivot.Y * delta.M22 + Pivot.Z * delta.M32);
            delta.M43 = Pivot.Z - (Pivot.X * delta.M13 + Pivot.Y * delta.M23 + Pivot.Z * delta.M33);
            return true;
        }

        internal bool TryAngle(LocalRay ray, out double angle)
        {
            angle = 0;
            double denominator = NumericGeometry.Dot(ray.Direction, Axis);
            if (Math.Abs(denominator) <= 0.00000001)
                return false;
            double distance = NumericGeometry.Dot(Pivot - ray.Origin, Axis) / denominator;
            if (!NumericGeometry.IsFinite(distance) || distance < 0)
                return false;
            Vector3D intersection = ray.Origin + ray.Direction * distance;
            if (!NumericGeometry.IsPoint(intersection))
                return false;
            Vector3D relative = intersection - Pivot;
            double x = NumericGeometry.Dot(relative, Reference);
            double y = NumericGeometry.Dot(relative, Tangent);
            if (x * x + y * y <= 0.0000000000000001)
                return false;
            angle = Math.Atan2(y, x);
            return NumericGeometry.IsFinite(angle);
        }
    }

    public struct PathDragState
    {
        public readonly double Value;
        public readonly double PointerArc;
        public readonly int Segment;
        internal readonly double GrabOffsetArc;
        internal readonly PathConstraint Constraint;

        internal PathDragState(PathConstraint constraint, double value, double pointerArc, int segment, double offset)
        {
            Constraint = constraint;
            Value = value;
            PointerArc = pointerArc;
            Segment = segment;
            GrabOffsetArc = offset;
        }

        public bool IsValid { get { return Constraint != null; } }
    }

    public struct RotationDragState
    {
        public readonly double Value;
        public readonly double PointerAngle;
        internal readonly double GrabOffsetAngle;
        internal readonly RotationConstraint Constraint;

        internal RotationDragState(RotationConstraint constraint, double value, double pointerAngle, double offset)
        {
            Constraint = constraint;
            Value = value;
            PointerAngle = pointerAngle;
            GrabOffsetAngle = offset;
        }

        public bool IsValid { get { return Constraint != null; } }
    }

    /// <summary>Pure allocation-free drag updates. Rejection preserves the previous state.</summary>
    public static class InteractionMath
    {
        public static bool TryBeginPathDrag(PathConstraint path, LocalRay ray, double value, out PathDragState state)
        {
            state = default(PathDragState);
            double canonical;
            double valueArc;
            if (path == null || !ray.IsValid || !path.Range.TryNormalize(value, out canonical)
                || !path.TryArcFromValue(canonical, out valueArc))
                return false;
            bool found = false;
            PathCandidate best = default(PathCandidate);
            for (int i = 0; i < path.SegmentCount; i++)
            {
                PathCandidate candidate;
                if (!path.TryClosest(i, ray, out candidate))
                    continue;
                if (!found || IsBetter(candidate, best, valueArc))
                {
                    best = candidate;
                    found = true;
                }
            }
            if (!found)
                return false;
            state = new PathDragState(path, canonical, best.PointerArc, best.Segment, valueArc - best.PointerArc);
            return true;
        }

        public static bool TryUpdatePathDrag(PathConstraint path, LocalRay ray, PathDragState previous, out PathDragState state)
        {
            state = previous;
            if (path == null || !ray.IsValid || !Object.ReferenceEquals(path, previous.Constraint))
                return false;
            PathCandidate best;
            if (!path.TryClosest(previous.Segment, ray, out best))
                return false;
            // Clamped candidates publish the exact stored junction arc. A tolerance scaled
            // by whole-path length would swallow short segments on a long polyline.
            const double arcTolerance = 0;
            int direction = 0;
            if (best.Arc >= path.EndArc(best.Segment) - arcTolerance
                || previous.PointerArc >= path.EndArc(best.Segment) - arcTolerance)
                direction = 1;
            else if (best.Arc <= path.StartArc(best.Segment) + arcTolerance
                || previous.PointerArc <= path.StartArc(best.Segment) + arcTolerance)
                direction = -1;
            // Only connected junctions may change the latched segment; crossings never teleport.
            for (int examined = 0; direction != 0 && examined < path.SegmentCount - 1; examined++)
            {
                int next = best.Segment + direction;
                if (next < 0 || next >= path.SegmentCount)
                    break;
                PathCandidate candidate;
                if (!path.TryClosest(next, ray, out candidate))
                    break;
                bool advances = direction > 0 ? candidate.Arc > best.Arc + arcTolerance
                    : candidate.Arc < best.Arc - arcTolerance;
                double tolerance = DistanceTolerance(candidate.DistanceSquared, best.DistanceSquared);
                if (!advances || candidate.DistanceSquared > best.DistanceSquared + tolerance)
                    break;
                best = candidate;
                bool junction = direction > 0 ? best.Arc >= path.EndArc(best.Segment) - arcTolerance
                    : best.Arc <= path.StartArc(best.Segment) + arcTolerance;
                if (!junction)
                    break;
            }
            double canonical;
            if (!path.TryValueFromArc(best.PointerArc + previous.GrabOffsetArc, out canonical))
                return false;
            state = new PathDragState(path, canonical, best.PointerArc, best.Segment, previous.GrabOffsetArc);
            return true;
        }

        public static bool TryBeginRotationDrag(RotationConstraint rotation, LocalRay ray, double value,
            out RotationDragState state)
        {
            state = default(RotationDragState);
            double canonical;
            double wrapped;
            double unwrapped;
            if (rotation == null || !ray.IsValid || !rotation.Range.TryNormalize(value, out canonical)
                || !rotation.TryAngle(ray, out wrapped) || !TryUnwrapAngle(wrapped, canonical, out unwrapped))
                return false;
            state = new RotationDragState(rotation, canonical, unwrapped, canonical - unwrapped);
            return true;
        }

        public static bool TryUpdateRotationDrag(RotationConstraint rotation, LocalRay ray, RotationDragState previous,
            out RotationDragState state)
        {
            state = previous;
            double wrapped;
            double unwrapped;
            double canonical;
            if (rotation == null || !ray.IsValid || !Object.ReferenceEquals(rotation, previous.Constraint)
                || !rotation.TryAngle(ray, out wrapped) || !TryUnwrapAngle(wrapped, previous.PointerAngle, out unwrapped)
                || !rotation.Range.TryNormalize(unwrapped + previous.GrabOffsetAngle, out canonical))
                return false;
            state = new RotationDragState(rotation, canonical, unwrapped, previous.GrabOffsetAngle);
            return true;
        }

        /// <summary>Nearest previous turn. Exactly half a turn chooses the positive direction.</summary>
        public static bool TryUnwrapAngle(double wrapped, double previous, out double unwrapped)
        {
            unwrapped = previous;
            if (!NumericGeometry.IsFinite(wrapped) || !NumericGeometry.IsFinite(previous)
                || Math.Abs(wrapped) > 1000000 || Math.Abs(previous) > 1000000)
                return false;
            double turn = 2 * Math.PI;
            double difference = (wrapped % turn) - (previous % turn);
            difference %= turn;
            if (difference > Math.PI)
                difference -= turn;
            else if (difference <= -Math.PI)
                difference += turn;
            double candidate = previous + difference;
            if (!NumericGeometry.IsFinite(candidate) || Math.Abs(candidate) > 1000000)
                return false;
            unwrapped = candidate;
            return true;
        }

        private static bool IsBetter(PathCandidate candidate, PathCandidate best, double referenceArc)
        {
            double tolerance = DistanceTolerance(candidate.DistanceSquared, best.DistanceSquared);
            if (candidate.DistanceSquared < best.DistanceSquared - tolerance)
                return true;
            if (Math.Abs(candidate.DistanceSquared - best.DistanceSquared) > tolerance)
                return false;
            double candidateTravel = Math.Abs(candidate.Arc - referenceArc);
            double bestTravel = Math.Abs(best.Arc - referenceArc);
            return candidateTravel < bestTravel || (candidateTravel == bestTravel && candidate.Segment < best.Segment);
        }

        private static double DistanceTolerance(double first, double second)
        {
            return 0.000000000001 * Math.Max(1, Math.Max(first, second));
        }
    }

    internal struct PathCandidate
    {
        internal readonly int Segment;
        internal readonly double Arc;
        internal readonly double PointerArc;
        internal readonly double DistanceSquared;

        internal PathCandidate(int segment, double arc, double pointerArc, double distanceSquared)
        {
            Segment = segment;
            Arc = arc;
            PointerArc = pointerArc;
            DistanceSquared = distanceSquared;
        }
    }

    internal static class NumericGeometry
    {
        internal static bool IsFinite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        internal static bool IsScalar(double value) { return IsFinite(value) && Math.Abs(value) <= 1000000000000.0; }
        internal static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(value, max)); }
        internal static double Dot(Vector3D a, Vector3D b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        internal static Vector3D Cross(Vector3D a, Vector3D b)
        {
            return new Vector3D(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }

        internal static bool IsPoint(Vector3D value)
        {
            return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z)
                && Math.Abs(value.X) <= 1000000 && Math.Abs(value.Y) <= 1000000 && Math.Abs(value.Z) <= 1000000
                && Dot(value, value) <= 1000000000000.0;
        }

        internal static bool TryUnit(Vector3D value, out Vector3D unit)
        {
            unit = default(Vector3D);
            if (!IsPoint(value))
                return false;
            double length = Math.Sqrt(Dot(value, value));
            if (length <= 0.000000000001)
                return false;
            unit = value / length;
            return true;
        }

        internal static bool TryFiniteUnit(Vector3D value, out Vector3D unit)
        {
            unit = default(Vector3D);
            if (!IsFinite(value.X) || !IsFinite(value.Y) || !IsFinite(value.Z))
                return false;
            double scale = Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
            if (scale == 0)
                return false;
            // Divide components directly so a subnormal scale does not create an
            // overflowing reciprocal inside a vector division operator.
            Vector3D scaled = new Vector3D(value.X / scale, value.Y / scale, value.Z / scale);
            double length = Math.Sqrt(Dot(scaled, scaled));
            unit = scaled / length;
            return true;
        }
    }
}
