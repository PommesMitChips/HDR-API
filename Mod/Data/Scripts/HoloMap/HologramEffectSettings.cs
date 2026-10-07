using System;
using VRageMath;

namespace HoloMap
{
    // Internal immutable interpretation of the standard CLR-array effect descriptor.
    // Keeping the wire form numeric avoids passing mod-defined CLR types to PBs.
    public sealed class HologramEffectSettings
    {
        public const int DescriptorVersion = 1, ValueCount = 24, FlagCount = 8;
        public const int MaximumDepthLayers = 32, MaximumParticles = 4096,
            MaximumBeams = 2048, MaximumPrimitives = 65536;
        readonly double[] _values;
        readonly int[] _flags;
        public readonly double FlickerStrength, FlickerHz, ScanStrength, ScanHz, ScanWidth, ScanBoost;
        public readonly double Depth, DepthFalloff, ParticleRadius, ParticleSpeed, ParticleLifetime, ParticleSize, ParticleOpacity;
        public readonly double BeamWidth, BeamOpacity, BeamJitter, TransitionSeconds, RevealFeather;
        public readonly Vector3D RevealAxis, Emitter;
        public readonly int Seed, DepthLayers, Particles, Beams, Transition, Easing, PrimitiveLimit;

        HologramEffectSettings(double[] values, int[] flags)
        {
            _values = values; _flags = flags;
            FlickerStrength = values[0]; FlickerHz = values[1]; ScanStrength = values[2]; ScanHz = values[3];
            ScanWidth = values[4]; ScanBoost = values[5]; Depth = values[6]; DepthFalloff = values[7];
            ParticleRadius = values[8]; ParticleSpeed = values[9]; ParticleLifetime = values[10];
            ParticleSize = values[11]; ParticleOpacity = values[12]; BeamWidth = values[13];
            BeamOpacity = values[14]; BeamJitter = values[15]; TransitionSeconds = values[16]; RevealFeather = values[17];
            RevealAxis = Vector3D.Normalize(new Vector3D(values[18], values[19], values[20]));
            Emitter = new Vector3D(values[21], values[22], values[23]);
            Seed = flags[1]; DepthLayers = flags[2]; Particles = flags[3]; Beams = flags[4];
            Transition = flags[5]; Easing = flags[6]; PrimitiveLimit = flags[7];
        }

        public static HologramEffectSettings Create(double[] values, int[] flags)
        {
            if (values == null || values.Length != ValueCount || flags == null || flags.Length != FlagCount)
                throw new ArgumentException("Hologram effects require 24 values and 8 flags.");
            // Capture before validation: concurrent callers cannot change admitted settings afterward.
            values = (double[])values.Clone(); flags = (int[])flags.Clone();
            for (int i = 0; i < values.Length; i++)
                if (!Geometry.Finite(values[i])) throw new ArgumentException("Hologram effect values must be finite.");
            Range(values[0], 0, 1); Range(values[1], 0, 120); Range(values[2], 0, 1);
            Range(values[3], -20, 20); Range(values[4], .000001, 1); Range(values[5], 0, 4);
            Range(values[6], 0, 1000); Range(values[7], 0, 1); Range(values[8], 0, 1000);
            Range(values[9], 0, 1000); Range(values[10], .05, 120); Range(values[11], .001, 100);
            Range(values[12], 0, 1); Range(values[13], .001, 100); Range(values[14], 0, 1);
            Range(values[15], 0, 100); Range(values[16], 0, 120); Range(values[17], 0, 1);
            var axis = new Vector3D(values[18], values[19], values[20]);
            if (!HologramEffectKernel.PointValid(axis) || axis.LengthSquared() < 1e-20)
                throw new ArgumentException("Hologram reveal axis must be finite and nonzero.");
            if (!HologramEffectKernel.PointValid(new Vector3D(values[21], values[22], values[23])))
                throw new ArgumentException("Hologram emitter must be within 1,000,000 model units.");
            if (flags[0] != DescriptorVersion) throw new ArgumentException("Unsupported hologram effect descriptor version.");
            Integer(flags[2], 0, MaximumDepthLayers); Integer(flags[3], 0, MaximumParticles);
            Integer(flags[4], 0, MaximumBeams); Integer(flags[5], 0, 3); Integer(flags[6], 0, 2);
            Integer(flags[7], 0, MaximumPrimitives);
            return new HologramEffectSettings(values, flags);
        }
        static void Range(double value, double min, double max)
        { if (value < min || value > max) throw new ArgumentException("Hologram effect value is outside its supported range."); }
        static void Integer(int value, int min, int max)
        { if (value < min || value > max) throw new ArgumentException("Hologram effect count or mode is outside its supported range."); }

        // Defaults do not alter source content. Named presets/commands can selectively enable fields.
        public static double[] DefaultValues()
        { return new double[] { 0, 12, 0, .2, .05, 0, 0, .5, .1, .05, 2, .02, .5, .02, .15, 0, .5, .05, 0, 1, 0, 0, 0, 0 }; }
        public static int[] DefaultFlags()
        { return new int[] { DescriptorVersion, 1, 0, 0, 0, 0, 1, 4096 }; }
        public double[] Values() { return (double[])_values.Clone(); }
        public int[] Flags() { return (int[])_flags.Clone(); }
        public bool Equivalent(HologramEffectSettings other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            for (int i = 0; i < ValueCount; i++) if (_values[i] != other._values[i]) return false;
            for (int i = 0; i < FlagCount; i++) if (_flags[i] != other._flags[i]) return false;
            return true;
        }
    }
}
