using System;
using VRageMath;

namespace HoloMap
{
    public struct HologramEffectFrame
    {
        public readonly double Time, Opacity, Progress, ScanPhase, Flicker;
        public readonly bool Entering;
        internal HologramEffectFrame(double time, double opacity, double progress, double scanPhase, double flicker, bool entering)
        { Time = time; Opacity = opacity; Progress = progress; ScanPhase = scanPhase; Flicker = flicker; Entering = entering; }
    }
    public struct HologramEffectPlan
    {
        public readonly bool BaseAccepted, Clipped;
        public readonly int DepthLayers, Particles, Beams, Primitives;
        internal HologramEffectPlan(bool accepted, bool clipped, int layers, int particles, int beams, int primitives)
        { BaseAccepted = accepted; Clipped = clipped; DepthLayers = layers; Particles = particles; Beams = beams; Primitives = primitives; }
    }
    public struct HologramEffectParticle
    {
        public readonly Vector3D Position;
        public readonly double Size, Opacity, Age, Lifetime;
        internal HologramEffectParticle(Vector3D position, double size, double opacity, double age, double lifetime)
        { Position = position; Size = size; Opacity = opacity; Age = age; Lifetime = lifetime; }
    }
    public struct HologramEffectBeam
    {
        public readonly Vector3D Start, End;
        public readonly double Width, Opacity;
        internal HologramEffectBeam(Vector3D start, Vector3D end, double width, double opacity)
        { Start = start; End = end; Width = width; Opacity = opacity; }
    }
    // A validated immutable value-type sampler for raster loops. The time/config proof is
    // checked once, while varying colors/coordinates remain checked at each sample.
    public struct HologramEffectSampler
    {
        readonly HologramEffectSettings _settings;
        readonly HologramEffectFrame _frame;
        internal HologramEffectSampler(HologramEffectSettings settings, HologramEffectFrame frame)
        { _settings = settings; _frame = frame; }
        public double Alpha(double coordinate, int primitiveIndex)
        { return HologramEffectKernel.PreparedAlpha(_settings, _frame, coordinate, primitiveIndex); }
        public Vector4 Color(Vector4 baseColor, double coordinate, int primitiveIndex)
        { return HologramEffectKernel.PreparedColor(_settings, _frame, baseColor, coordinate, primitiveIndex); }
    }

    // Pure shared-time sampling. No physics, native renderer, mutable RNG or per-frame collections.
    // Depth layers and beam strips approximate holographic volume; they are not participating-media lighting.
    public static class HologramEffectKernel
    {
        public static HologramEffectFrame Evaluate(HologramEffectSettings settings, double time, double startTime, bool entering)
        {
            Required(settings); Time(time); Time(startTime);
            double progress = settings.TransitionSeconds == 0 ? 1 : Clamp((time - startTime) / settings.TransitionSeconds);
            progress = Ease(progress, settings.Easing);
            double opacity = settings.Transition == 1 ? (entering ? progress : 1 - progress) : 1;
            double phase = Fraction(time * settings.ScanHz + Random(settings.Seed, 0, 0, 811));
            return new HologramEffectFrame(time, opacity, progress, phase, Flicker(settings, time), entering);
        }

        public static double Coordinate(HologramEffectSettings settings, Vector3D point, Vector3D min, Vector3D max)
        {
            Required(settings); Bounds(min, max); Point(point);
            var axis = settings.RevealAxis;
            double low = (axis.X >= 0 ? min.X : max.X) * axis.X + (axis.Y >= 0 ? min.Y : max.Y) * axis.Y + (axis.Z >= 0 ? min.Z : max.Z) * axis.Z;
            double high = (axis.X >= 0 ? max.X : min.X) * axis.X + (axis.Y >= 0 ? max.Y : min.Y) * axis.Y + (axis.Z >= 0 ? max.Z : min.Z) * axis.Z;
            double extent = high - low;
            return extent < 1e-12 ? .5 : Clamp((Vector3D.Dot(point, axis) - low) / extent);
        }

        public static double Alpha(HologramEffectSettings settings, HologramEffectFrame frame, double coordinate, int primitiveIndex)
        {
            Required(settings); Frame(frame); Unit(coordinate); Index(primitiveIndex);
            return AlphaCore(settings, frame, coordinate, primitiveIndex);
        }
        static double AlphaCore(HologramEffectSettings settings, HologramEffectFrame frame, double coordinate, int primitiveIndex)
        {
            double opacity = frame.Opacity;
            if (settings.Transition == 2 || settings.Transition == 3)
            {
                double threshold = settings.Transition == 2 ? coordinate : Random(settings.Seed, 0, primitiveIndex, 1223);
                double visible = Reveal(frame.Progress, threshold, settings.RevealFeather);
                opacity *= frame.Entering ? visible : 1 - visible;
            }
            opacity *= frame.Flicker;
            opacity *= 1 - settings.ScanStrength * (1 - Scan(settings, frame.ScanPhase, coordinate));
            return Clamp(opacity);
        }

        public static Vector4 Color(HologramEffectSettings settings, HologramEffectFrame frame, Vector4 baseColor, double coordinate, int primitiveIndex)
        {
            Required(settings); Frame(frame); return PreparedColor(settings, frame, baseColor, coordinate, primitiveIndex);
        }
        public static HologramEffectSampler Prepare(HologramEffectSettings settings, HologramEffectFrame frame)
        { Required(settings); Frame(frame); return new HologramEffectSampler(settings, frame); }
        internal static double PreparedAlpha(HologramEffectSettings settings, HologramEffectFrame frame, double coordinate, int primitiveIndex)
        { Required(settings); Unit(coordinate); Index(primitiveIndex); return AlphaCore(settings, frame, coordinate, primitiveIndex); }
        internal static Vector4 PreparedColor(HologramEffectSettings settings, HologramEffectFrame frame, Vector4 baseColor, double coordinate, int primitiveIndex)
        {
            Required(settings); Unit(coordinate); Index(primitiveIndex);
            if (!Geometry.Finite(baseColor.X) || !Geometry.Finite(baseColor.Y) || !Geometry.Finite(baseColor.Z) || !Geometry.Finite(baseColor.W) ||
                baseColor.X < 0 || baseColor.Y < 0 || baseColor.Z < 0 || baseColor.X > 16 || baseColor.Y > 16 || baseColor.Z > 16 || baseColor.W < 0 || baseColor.W > 1)
                throw new ArgumentException("Hologram color must be finite, with RGB 0–16 and alpha 0–1.");
            double alpha = AlphaCore(settings, frame, coordinate, primitiveIndex);
            double boost = settings.ScanBoost == 0 ? 1 : 1 + settings.ScanBoost * Scan(settings, frame.ScanPhase, coordinate);
            return new Vector4((float)Math.Min(16, baseColor.X * boost), (float)Math.Min(16, baseColor.Y * boost),
                (float)Math.Min(16, baseColor.Z * boost), (float)(baseColor.W * alpha));
        }

        // The canonical base stays at Z=0. Ghost copies alternate toward both sides.
        public static double LayerOffset(HologramEffectSettings settings, int layer)
        {
            Required(settings); Layer(settings, layer);
            if (layer == 0 || settings.Depth == 0) return 0;
            int rings = (settings.DepthLayers + 1) / 2;
            double distance = ((layer + 1) / 2) * settings.Depth / (2 * rings);
            return (layer % 2 == 1 ? 1 : -1) * distance;
        }
        public static double LayerOpacity(HologramEffectSettings settings, int layer)
        { Required(settings); Layer(settings, layer); return layer == 0 ? 1 : Math.Pow(settings.DepthFalloff, layer); }

        // Source content wins over embellishments. A deterministic prefix is retained under reduced budgets.
        public static HologramEffectPlan Plan(HologramEffectSettings settings, int basePrimitives, int remainingBudget, int particleCost = 2, int beamCost = 2)
        {
            Required(settings);
            if (basePrimitives < 0 || remainingBudget < 0 || particleCost < 1 || beamCost < 1)
                throw new ArgumentException("Hologram effect work costs must be nonnegative, with positive sample costs.");
            int limit = Math.Min(remainingBudget, settings.PrimitiveLimit);
            if (basePrimitives > limit)
                return new HologramEffectPlan(false, true, 0, 0, 0, 0);
            int used = basePrimitives;
            int layers = basePrimitives == 0 || settings.Depth == 0 || settings.DepthFalloff == 0 ? 0 : Math.Min(settings.DepthLayers, (limit - used) / basePrimitives);
            used += layers * basePrimitives;
            int particles = settings.ParticleOpacity == 0 ? 0 : Math.Min(settings.Particles, (limit - used) / particleCost);
            used += particles * particleCost;
            int beams = settings.BeamOpacity == 0 ? 0 : Math.Min(settings.Beams, (limit - used) / beamCost);
            used += beams * beamCost;
            return new HologramEffectPlan(true, layers < settings.DepthLayers || particles < settings.Particles || beams < settings.Beams, layers, particles, beams, used);
        }

        public static bool Particle(HologramEffectSettings settings, HologramEffectFrame frame, int slot, Vector3D min, Vector3D max, out HologramEffectParticle sample)
        {
            Required(settings); Frame(frame); Bounds(min, max);
            if (slot < 0 || slot >= settings.Particles) throw new ArgumentException("Hologram particle slot is outside the configured count.");
            sample = default(HologramEffectParticle);
            if (settings.ParticleOpacity == 0 || FullyHidden(settings, frame)) return false;
            double cyclePosition = frame.Time / settings.ParticleLifetime + Random(settings.Seed, 0, slot, 2017);
            long cycle = (long)Math.Floor(cyclePosition);
            double age = Fraction(cyclePosition) * settings.ParticleLifetime;
            var center = new Vector3D(min.X + (max.X - min.X) * Random(settings.Seed, cycle, slot, 2027),
                min.Y + (max.Y - min.Y) * Random(settings.Seed, cycle, slot, 2029),
                min.Z + (max.Z - min.Z) * Random(settings.Seed, cycle, slot, 2039));
            var direction = Direction(settings.Seed, cycle, slot);
            var point = center + direction * (settings.ParticleRadius * Random(settings.Seed, cycle, slot, 2053) + age * settings.ParticleSpeed);
            if (!PointValid(point)) return false;
            double fraction = age / settings.ParticleLifetime;
            double envelope = Smooth(Clamp(fraction / .1)) * Smooth(Clamp((1 - fraction) / .5));
            // Intrinsic opacity only. The renderer samples Color/Alpha at Position exactly
            // once, so spatial wipes/dissolves and temporal flicker are not applied twice.
            double opacity = Clamp(settings.ParticleOpacity * envelope);
            if (opacity <= 0) return false;
            sample = new HologramEffectParticle(point, settings.ParticleSize * (.65 + .7 * Random(settings.Seed, cycle, slot, 2063)), opacity, age, settings.ParticleLifetime);
            return true;
        }

        public static bool Beam(HologramEffectSettings settings, HologramEffectFrame frame, int slot, Vector3D target, out HologramEffectBeam sample)
        {
            Required(settings); Frame(frame); Point(target);
            if (slot < 0 || slot >= settings.Beams) throw new ArgumentException("Hologram beam slot is outside the configured count.");
            sample = default(HologramEffectBeam);
            var ray = target - settings.Emitter;
            double distance = ray.Length();
            if (distance < 1e-9 || settings.BeamOpacity == 0 || FullyHidden(settings, frame)) return false;
            var direction = ray / distance;
            var basis = Math.Abs(direction.Y) < .9 ? Vector3D.UnitY : Vector3D.UnitX;
            var side = Vector3D.Normalize(Vector3D.Cross(direction, basis));
            var up = Vector3D.Cross(direction, side);
            double phase = Random(settings.Seed, 0, slot, 3011) * Math.PI * 2;
            var end = target + (side * Math.Sin(frame.Time * 3 + phase) + up * Math.Cos(frame.Time * 2 + phase)) * (settings.BeamJitter / Math.Sqrt(2));
            if (!PointValid(end)) return false;
            // Intrinsic opacity only; renderer applies Color/Alpha at the target once.
            double opacity = settings.BeamOpacity;
            sample = new HologramEffectBeam(settings.Emitter, end, settings.BeamWidth, opacity);
            return opacity > 0;
        }

        public static bool MatrixValid(MatrixD matrix)
        {
            if (!MatrixValue(matrix.M11) || !MatrixValue(matrix.M12) || !MatrixValue(matrix.M13) || !MatrixValue(matrix.M14) ||
                !MatrixValue(matrix.M21) || !MatrixValue(matrix.M22) || !MatrixValue(matrix.M23) || !MatrixValue(matrix.M24) ||
                !MatrixValue(matrix.M31) || !MatrixValue(matrix.M32) || !MatrixValue(matrix.M33) || !MatrixValue(matrix.M34) ||
                !MatrixValue(matrix.M41) || !MatrixValue(matrix.M42) || !MatrixValue(matrix.M43) || !MatrixValue(matrix.M44)) return false;
            if (Math.Abs(matrix.M14) > 1e-12 || Math.Abs(matrix.M24) > 1e-12 || Math.Abs(matrix.M34) > 1e-12 || Math.Abs(matrix.M44 - 1) > 1e-12 || !PointValid(matrix.Translation)) return false;
            double determinant = matrix.M11 * (matrix.M22 * matrix.M33 - matrix.M23 * matrix.M32) - matrix.M12 * (matrix.M21 * matrix.M33 - matrix.M23 * matrix.M31) + matrix.M13 * (matrix.M21 * matrix.M32 - matrix.M22 * matrix.M31);
            return Geometry.Finite(determinant) && Math.Abs(determinant) > 1e-18;
        }
        static bool MatrixValue(double value) { return Geometry.Finite(value) && Math.Abs(value) <= 1000000; }
        public static bool PointValid(Vector3D point)
        { return Geometry.Finite(point.X) && Geometry.Finite(point.Y) && Geometry.Finite(point.Z) && point.LengthSquared() <= 1e12; }
        static void Point(Vector3D point)
        { if (!PointValid(point)) throw new ArgumentException("Hologram effect point must be finite and within 1,000,000 model units."); }
        static void Bounds(Vector3D min, Vector3D max)
        {
            // AABB corners are derived extents, not submitted geometry vertices. Every
            // admitted vertex can lie inside the model radius while a derived corner
            // combines components from several vertices and lies outside that sphere.
            if (!BoundPoint(min) || !BoundPoint(max))
                throw new ArgumentException("Hologram effect bounds must have finite components within 1,000,000 model units.");
            if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
                throw new ArgumentException("Hologram effect bounds are inverted.");
        }
        static bool BoundPoint(Vector3D point)
        { return Geometry.Finite(point.X) && Geometry.Finite(point.Y) && Geometry.Finite(point.Z) && Math.Abs(point.X) <= 1000000 && Math.Abs(point.Y) <= 1000000 && Math.Abs(point.Z) <= 1000000; }
        static void Required(HologramEffectSettings settings)
        { if (settings == null) throw new ArgumentException("Missing hologram effect settings."); }
        static void Time(double time)
        { if (!Geometry.Finite(time) || Math.Abs(time) > 1000000000) throw new ArgumentException("Hologram effect time must be finite and within the shared clock range."); }
        static void Frame(HologramEffectFrame frame)
        { Time(frame.Time); Unit(frame.Opacity); Unit(frame.Progress); Unit(frame.ScanPhase); Unit(frame.Flicker); }
        static void Unit(double value)
        { if (!Geometry.Finite(value) || value < 0 || value > 1) throw new ArgumentException("Hologram effect sample requires a normalized finite value."); }
        static void Index(int index)
        { if (index < 0) throw new ArgumentException("Hologram primitive index must be nonnegative."); }
        static void Layer(HologramEffectSettings settings, int layer)
        { if (layer < 0 || layer > settings.DepthLayers) throw new ArgumentException("Hologram depth layer is outside the configured count."); }
        static double Clamp(double value) { return Math.Max(0, Math.Min(1, value)); }
        static double Fraction(double value) { return value - Math.Floor(value); }
        static double Smooth(double value) { return value * value * (3 - 2 * value); }
        static double Ease(double value, int easing)
        { return easing == 0 ? value : easing == 1 ? Smooth(value) : value < .5 ? 4 * value * value * value : 1 - 4 * (1 - value) * (1 - value) * (1 - value); }
        static double Reveal(double progress, double threshold, double feather)
        { return progress <= 0 ? 0 : progress >= 1 ? 1 : feather <= 0 ? (progress >= threshold ? 1 : 0) : Smooth(Clamp((progress - threshold) / feather + .5)); }
        static bool FullyHidden(HologramEffectSettings settings, HologramEffectFrame frame)
        { return settings.Transition == 1 ? frame.Opacity == 0 : settings.Transition == 2 || settings.Transition == 3 ? (frame.Entering ? frame.Progress == 0 : frame.Progress == 1) : false; }
        static double Scan(HologramEffectSettings settings, double phase, double coordinate)
        {
            if (settings.ScanWidth >= 1) return 1;
            double distance = Math.Abs(coordinate - phase); distance = Math.Min(distance, 1 - distance);
            double half = settings.ScanWidth / 2;
            return Smooth(Clamp((half - distance) / (half * .3)));
        }
        static double Flicker(HologramEffectSettings settings, double time)
        {
            if (settings.FlickerStrength == 0 || settings.FlickerHz == 0) return 1;
            double position = time * settings.FlickerHz;
            long tick = (long)Math.Floor(position);
            double a = Random(settings.Seed, tick, 0, 4091), b = Random(settings.Seed, tick + 1, 0, 4091);
            double noise = a + (b - a) * Smooth(Fraction(position));
            // Short deep glitches, smoothly joined, rather than unbounded frame-to-frame RNG.
            double power = noise * noise; power *= power; power *= power;
            return 1 - settings.FlickerStrength * power;
        }
        static Vector3D Direction(int seed, long cycle, int slot)
        {
            double z = Random(seed, cycle, slot, 2081) * 2 - 1;
            double angle = Random(seed, cycle, slot, 2083) * Math.PI * 2;
            double radial = Math.Sqrt(Math.Max(0, 1 - z * z));
            return new Vector3D(radial * Math.Cos(angle), z, radial * Math.Sin(angle));
        }
        static double Random(int seed, long cycle, int slot, int salt)
        {
            unchecked
            {
                uint x = (uint)seed ^ (uint)cycle * 0x9E3779B9u ^ (uint)(cycle >> 32) * 0x85EBCA6Bu ^ (uint)slot * 0xC2B2AE35u ^ (uint)salt;
                x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
                return (double)x / 4294967296.0;
            }
        }
    }
}
