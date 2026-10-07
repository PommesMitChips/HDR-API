using System;
using System.Linq;
using HoloMap;
using VRageMath;

internal static class HologramEffectTests
{
    static int checks;
    static void Check(bool condition, string reason)
    { if (!condition) throw new Exception("Hologram effects: " + reason); checks++; }
    static void Near(double actual, double expected, string reason, double tolerance = 1e-9)
    { Check(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, reason); }
    static void Reject(Action action, string reason)
    { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("Hologram effects accepted invalid input: " + reason); }
    static HologramEffectSettings Settings(Action<double[]> values = null, Action<int[]> flags = null)
    {
        var v = HologramEffectSettings.DefaultValues(); var f = HologramEffectSettings.DefaultFlags();
        values?.Invoke(v); flags?.Invoke(f); return HologramEffectSettings.Create(v, f);
    }

    public static int Run()
    {
        checks = 0; Configuration(); Timing(); Sampler(); ExtraTransitions(); Planning(); Geometry(); BoundsClosure(); Samples(); return checks;
    }

    static void Configuration()
    {
        var v = HologramEffectSettings.DefaultValues(); var f = HologramEffectSettings.DefaultFlags();
        Check(v.Length == 24 && f.Length == 8 && f[0] == 1, "versioned fixed wire shape");
        var settings = HologramEffectSettings.Create(v, f);
        var expectedValues = settings.Values(); var expectedFlags = settings.Flags();
        v[0] = 1; f[3] = 100;
        Check(settings.Values().SequenceEqual(expectedValues) && settings.Flags().SequenceEqual(expectedFlags), "input arrays cannot mutate detached configuration");
        var exportedValues = settings.Values(); var exportedFlags = settings.Flags();
        exportedValues[1] = 120; exportedFlags[1] = 999;
        Check(settings.Values().SequenceEqual(expectedValues) && settings.Flags().SequenceEqual(expectedFlags), "export arrays cannot mutate configuration");
        Check(settings.Equivalent(HologramEffectSettings.Create(expectedValues, expectedFlags)) && !settings.Equivalent(null), "equivalence compares detached declarations");
        var freshValues = HologramEffectSettings.DefaultValues(); var freshFlags = HologramEffectSettings.DefaultFlags();
        Check(freshValues[0] == 0 && freshFlags[3] == 0, "default arrays are detached between callers");
        foreach (var bad in new[] { (double[])null, new double[23], new double[25] })
            Reject(() => HologramEffectSettings.Create(bad, freshFlags), "values length/null");
        foreach (var bad in new[] { (int[])null, new int[7], new int[9] })
            Reject(() => HologramEffectSettings.Create(freshValues, bad), "flags length/null");
        for (int i = 0; i < 24; i++) foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var invalid = (double[])freshValues.Clone(); invalid[i] = bad;
            Reject(() => HologramEffectSettings.Create(invalid, freshFlags), "nonfinite field " + i);
        }
        int[] bounded = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };
        double[] minimum = { 0, 0, 0, -20, 1e-6, 0, 0, 0, 0, 0, .05, .001, 0, .001, 0, 0, 0, 0 };
        double[] maximum = { 1, 120, 1, 20, 1, 4, 1000, 1, 1000, 1000, 120, 100, 1, 100, 1, 100, 120, 1 };
        for (int i = 0; i < bounded.Length; i++)
        {
            foreach (double valid in new[] { minimum[i], maximum[i] })
            { var boundary = (double[])freshValues.Clone(); boundary[bounded[i]] = valid; HologramEffectSettings.Create(boundary, freshFlags); checks++; }
            foreach (double bad in new[] { minimum[i] - Math.Max(.001, Math.Abs(minimum[i]) * .01), maximum[i] + Math.Max(.001, Math.Abs(maximum[i]) * .01) })
            { var boundary = (double[])freshValues.Clone(); boundary[bounded[i]] = bad; Reject(() => HologramEffectSettings.Create(boundary, freshFlags), "out-of-range field " + bounded[i]); }
        }
        foreach (int i in new[] { 0, 2, 3, 4, 5, 6, 7 })
        {
            var invalid = (int[])freshFlags.Clone(); invalid[i] = -1;
            Reject(() => HologramEffectSettings.Create(freshValues, invalid), "negative flag " + i);
        }
        int[] flagIndices = { 0, 2, 3, 4, 5, 6, 7 }; int[] tooLarge = { 2, 33, 4097, 2049, 4, 3, 65537 };
        for (int i = 0; i < flagIndices.Length; i++)
        { var invalid = (int[])freshFlags.Clone(); invalid[flagIndices[i]] = tooLarge[i]; Reject(() => HologramEffectSettings.Create(freshValues, invalid), "large flag " + flagIndices[i]); }
        foreach (int seed in new[] { int.MinValue, 0, int.MaxValue })
        { var seeded = (int[])freshFlags.Clone(); seeded[1] = seed; Check(HologramEffectSettings.Create(freshValues, seeded).Flags()[1] == seed, "full signed seed range"); }
        var zeroAxis = (double[])freshValues.Clone(); zeroAxis[18] = zeroAxis[19] = zeroAxis[20] = 0;
        Reject(() => HologramEffectSettings.Create(zeroAxis, freshFlags), "zero reveal axis");
        var distantEmitter = (double[])freshValues.Clone(); distantEmitter[21] = distantEmitter[22] = 800000;
        Reject(() => HologramEffectSettings.Create(distantEmitter, freshFlags), "emitter norm rather than component-only range");
        var largeAxis = (double[])freshValues.Clone(); largeAxis[18] = largeAxis[19] = 800000;
        Reject(() => HologramEffectSettings.Create(largeAxis, freshFlags), "unsafe reveal axis norm");
        var tinyAxis = (double[])freshValues.Clone(); tinyAxis[18] = 1e-12; tinyAxis[19] = tinyAxis[20] = 0;
        Reject(() => HologramEffectSettings.Create(tinyAxis, freshFlags), "numerically degenerate reveal axis");
    }

    static void Timing()
    {
        var disabled = Settings(); var plain = HologramEffectKernel.Evaluate(disabled, 10, 0, true);
        Near(plain.Time, 10, "shared frame time preserved"); Near(plain.Opacity, 1, "disabled transition leaves base opacity");
        Near(HologramEffectKernel.Alpha(disabled, plain, .5, 0), 1, "disabled effects leave opacity unchanged");
        Vector4 color = new Vector4(.2f, .3f, .4f, .7f);
        Check(HologramEffectKernel.Color(disabled, plain, color, .5, 0) == color, "disabled effects preserve RGBA");
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, -1e9 - 1, 1e9 + 1 })
        { Reject(() => HologramEffectKernel.Evaluate(disabled, bad, 0, true), "invalid clock"); Reject(() => HologramEffectKernel.Evaluate(disabled, 0, bad, true), "invalid transition origin"); }
        foreach (int easing in new[] { 0, 1, 2 })
        {
            var fade = Settings(x => x[16] = 2, x => { x[5] = 1; x[6] = easing; });
            Near(HologramEffectKernel.Evaluate(fade, 5, 5, true).Opacity, 0, "enter starts invisible");
            Near(HologramEffectKernel.Evaluate(fade, 7, 5, true).Opacity, 1, "enter ends visible");
            Near(HologramEffectKernel.Evaluate(fade, 5, 5, false).Opacity, 1, "exit starts visible");
            Near(HologramEffectKernel.Evaluate(fade, 7, 5, false).Opacity, 0, "exit ends invisible");
            double prior = -1;
            for (int i = 0; i <= 32; i++)
            { var frame = HologramEffectKernel.Evaluate(fade, 5 + i / 16d, 5, true); Check(frame.Progress >= prior && frame.Progress >= 0 && frame.Progress <= 1 && frame.Opacity == frame.Progress, "fade is bounded monotone"); prior = frame.Progress; }
        }
        var linear = Settings(x => x[16] = 2, x => { x[5] = 1; x[6] = 0; });
        Near(HologramEffectKernel.Evaluate(linear, 5.5, 5, true).Progress, .25, "linear timing witness");
        var smooth = Settings(x => x[16] = 2, x => { x[5] = 1; x[6] = 1; });
        Near(HologramEffectKernel.Evaluate(smooth, 5.5, 5, true).Progress, .15625, "smoothstep timing witness");
        var instant = Settings(x => x[16] = 0, x => x[5] = 1);
        Near(HologramEffectKernel.Evaluate(instant, 5, 5, true).Opacity, 1, "zero duration enter completes without division");
        Near(HologramEffectKernel.Evaluate(instant, 5, 5, false).Opacity, 0, "zero duration exit completes without division");
        foreach (int mode in new[] { 2, 3 })
        {
            var reveal = Settings(x => { x[16] = 2; x[17] = .3; }, x => x[5] = mode);
            foreach (double p in new[] { 0d, .25, .5, .75, 1 }) foreach (int primitive in new[] { 0, 1, 23 })
            {
                Near(HologramEffectKernel.Alpha(reveal, HologramEffectKernel.Evaluate(reveal, 5, 5, true), p, primitive), 0, "reveal start hides every position");
                Near(HologramEffectKernel.Alpha(reveal, HologramEffectKernel.Evaluate(reveal, 7, 5, true), p, primitive), 1, "reveal end shows every position");
                Near(HologramEffectKernel.Alpha(reveal, HologramEffectKernel.Evaluate(reveal, 5, 5, false), p, primitive), 1, "exit reveal starts fully visible");
                Near(HologramEffectKernel.Alpha(reveal, HologramEffectKernel.Evaluate(reveal, 7, 5, false), p, primitive), 0, "exit reveal ends fully hidden");
            }
        }
        foreach (double hz in new[] { -2d, 2d })
        {
            var scan = Settings(x => { x[2] = .8; x[3] = hz; x[4] = .2; });
            var a = HologramEffectKernel.Evaluate(scan, 12.125, 0, true); var b = HologramEffectKernel.Evaluate(scan, 12.625, 0, true);
            Near(a.ScanPhase, b.ScanPhase, "scan wraps in both directions"); Check(a.ScanPhase >= 0 && a.ScanPhase < 1, "wrapped phase range");
            Near(HologramEffectKernel.Alpha(scan, a, a.ScanPhase, 0), 1, "scan centre is unattenuated");
            double outside = a.ScanPhase + .5; if (outside >= 1) outside -= 1;
            Near(HologramEffectKernel.Alpha(scan, a, outside, 0), .2, "pixels away from moving bar use exact configured attenuation");
        }
        var flicker = Settings(x => { x[0] = .75; x[1] = 7; }, x => x[1] = 41);
        for (int i = 0; i < 100; i++)
        {
            double t = i * .031; var a = HologramEffectKernel.Evaluate(flicker, t, 0, true); var b = HologramEffectKernel.Evaluate(flicker, t, 0, true);
            double alpha = HologramEffectKernel.Alpha(flicker, a, .4, 2);
            Near(alpha, HologramEffectKernel.Alpha(flicker, b, .4, 2), "shared-time flicker repeatability");
            Check(alpha >= .25 && alpha <= 1, "flicker never exceeds configured attenuation");
        }
        var boundaryLeft = HologramEffectKernel.Evaluate(flicker, 1d / 7 - 1e-8, 0, true);
        var boundaryRight = HologramEffectKernel.Evaluate(flicker, 1d / 7 + 1e-8, 0, true);
        Check(Math.Abs(HologramEffectKernel.Alpha(flicker, boundaryLeft, .4, 2) - HologramEffectKernel.Alpha(flicker, boundaryRight, .4, 2)) < 1e-6,
            "flicker is continuous across random-key boundaries");
        var boosted = Settings(v => { v[2] = .8; v[4] = .2; v[5] = 2; });
        var boostFrame = HologramEffectKernel.Evaluate(boosted, 0, 0, true);
        var glowing = HologramEffectKernel.Color(boosted, boostFrame, new Vector4(10, 2, 1, .6f), boostFrame.ScanPhase, 0);
        Check(glowing.X == 16 && glowing.Y == 6 && glowing.Z == 3 && glowing.W == .6f, "scan color boosts RGB with safe ceiling and independent alpha");
        foreach (double bad in new[] { -.01, 1.01, double.NaN, double.PositiveInfinity })
            Reject(() => HologramEffectKernel.Alpha(disabled, plain, bad, 0), "invalid normalized sample coordinate");
        Reject(() => HologramEffectKernel.Alpha(disabled, plain, .5, -1), "negative primitive index");
        foreach (var bad in new[] { new Vector4(float.NaN, 0, 0, 1), new Vector4(0, float.PositiveInfinity, 0, 1), new Vector4(-.1f, 0, 0, 1), new Vector4(17, 0, 0, 1), new Vector4(0, 0, 0, 1.1f) })
            Reject(() => HologramEffectKernel.Color(disabled, plain, bad, .5, 0), "invalid color");
        Reject(() => HologramEffectKernel.Evaluate(null, 0, 0, true), "missing settings");
        foreach (var bad in new[] { new HologramEffectFrame(double.NaN, 1, 1, 0, 1, true), new HologramEffectFrame(0, double.NaN, 1, 0, 1, true), new HologramEffectFrame(0, 1, -.1, 0, 1, true), new HologramEffectFrame(0, 1, 1, 2, 1, true), new HologramEffectFrame(0, 1, 1, 0, double.NaN, true) })
            Reject(() => HologramEffectKernel.Alpha(disabled, bad, .5, 0), "poisoned frame rejected before sampling");
    }

    static void Sampler()
    {
        var color = new Vector4(.2f, .3f, .4f, .7f);
        foreach (int mode in new[] { 0, 1, 2, 3 })
        {
            var settings = Settings(v => { v[0] = .6; v[1] = 7; v[2] = .8; v[4] = .2; v[5] = 1.5; v[16] = 2; }, f => f[5] = mode);
            foreach (bool entering in new[] { false, true })
            {
                var frame = HologramEffectKernel.Evaluate(settings, 10.7, 10, entering);
                var sampler = HologramEffectKernel.Prepare(settings, frame);
                for (int i = 0; i <= 32; i++)
                {
                    double coordinate = i / 32d;
                    Check(sampler.Alpha(coordinate, i) == HologramEffectKernel.Alpha(settings, frame, coordinate, i), "prepared alpha exactly matches fully validated path");
                    Check(sampler.Color(color, coordinate, i) == HologramEffectKernel.Color(settings, frame, color, coordinate, i), "prepared color exactly matches fully validated path");
                }
            }
        }
        var plain = Settings(); var validFrame = HologramEffectKernel.Evaluate(plain, 10, 0, true); var prepared = HologramEffectKernel.Prepare(plain, validFrame);
        Reject(() => HologramEffectKernel.Prepare(null, validFrame), "missing prepared settings");
        Reject(() => default(HologramEffectSampler).Alpha(.5, 0), "default sampler cannot be used");
        Reject(() => default(HologramEffectSampler).Color(color, .5, 0), "default color sampler cannot be used");
        foreach (double bad in new[] { -.1, 1.1, double.NaN, double.PositiveInfinity })
        { Reject(() => prepared.Alpha(bad, 0), "prepared invalid coordinate"); Reject(() => prepared.Color(color, bad, 0), "prepared color invalid coordinate"); }
        Reject(() => prepared.Alpha(.5, -1), "prepared invalid primitive index");
        Reject(() => prepared.Color(new Vector4(float.NaN, 0, 0, 1), .5, 0), "prepared invalid color");
        var animated = Settings(v => { v[0] = .6; v[1] = 7; v[2] = .8; v[4] = .2; v[5] = 1.5; v[16] = 2; }, f => f[5] = 2);
        var animatedFrame = HologramEffectKernel.Evaluate(animated, 10.7, 10, true); var hot = HologramEffectKernel.Prepare(animated, animatedFrame);
        double checksum = 0;
        for (int i = 0; i < 1000; i++) checksum += hot.Alpha((i % 101) / 100d, i) + hot.Color(color, (i % 101) / 100d, i).W;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) checksum += hot.Alpha((i % 101) / 100d, i) + hot.Color(color, (i % 101) / 100d, i).W;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0 && checksum > 0 && double.IsFinite(checksum), "warm prepared per-primitive sampling performs zero managed allocation");
    }

    static void ExtraTransitions()
    {
        var min = new Vector3D(-1); var max = new Vector3D(1); var target = new Vector3D(.4, .3, .2);
        var tint = new Vector4(.2f, .3f, .4f, .6f);
        foreach (int mode in new[] { 1, 2, 3 })
        {
            HologramEffectSettings Config(double flicker) => Settings(v =>
            { v[0] = flicker; v[1] = 7; v[8] = .1; v[9] = .02; v[10] = 2; v[12] = .8; v[14] = .7; v[16] = 2; v[17] = 1; },
                f => { f[1] = 765; f[3] = 16; f[4] = 16; f[5] = mode; f[6] = 0; });
            var settings = Config(.8); var steady = Config(0);
            foreach (bool entering in new[] { true, false })
            {
                double hiddenTime = entering ? 40.125 : 42.125;
                var hidden = HologramEffectKernel.Evaluate(settings, hiddenTime, 40.125, entering);
                for (int slot = 0; slot < 16; slot++)
                {
                    Check(!HologramEffectKernel.Particle(settings, hidden, slot, min, max, out var particle) && particle.Opacity == 0 && particle.Position == Vector3D.Zero && particle.Size == 0 && particle.Age == 0 && particle.Lifetime == 0,
                        "fully hidden transition returns a default absent particle for every slot");
                    Check(!HologramEffectKernel.Beam(settings, hidden, slot, target, out var beam) && beam.Opacity == 0 && beam.Start == Vector3D.Zero && beam.End == Vector3D.Zero && beam.Width == 0,
                        "fully hidden transition returns a default absent beam for every slot");
                }
                var mid = HologramEffectKernel.Evaluate(settings, 41.125, 40.125, entering);
                var steadyMid = HologramEffectKernel.Evaluate(steady, 41.125, 40.125, entering);
                for (int slot = 0; slot < 16; slot++)
                {
                    bool present = HologramEffectKernel.Particle(settings, mid, slot, min, max, out var particle);
                    bool steadyPresent = HologramEffectKernel.Particle(steady, steadyMid, slot, min, max, out var steadyParticle);
                    Check(present == steadyPresent && particle.Position == steadyParticle.Position && particle.Age == steadyParticle.Age && particle.Size == steadyParticle.Size && particle.Opacity == steadyParticle.Opacity,
                        "particle path and intrinsic opacity are independent of temporal flicker");
                    if (present)
                    {
                        double fraction = particle.Age / particle.Lifetime;
                        double birth = Math.Clamp(fraction / .1, 0, 1), death = Math.Clamp((1 - fraction) / .5, 0, 1);
                        double intrinsic = .8 * birth * birth * (3 - 2 * birth) * death * death * (3 - 2 * death);
                        Near(particle.Opacity, intrinsic, "particle opacity contains only its configured lifetime envelope");
                        double coordinate = HologramEffectKernel.Coordinate(settings, particle.Position, min, max);
                        double factor = HologramEffectKernel.Alpha(settings, mid, coordinate, slot);
                        double composed = HologramEffectKernel.Color(settings, mid, tint, coordinate, slot).W * particle.Opacity;
                        Near(composed, tint.W * factor * intrinsic, "particle midpoint composition applies the complete temporal/spatial factor exactly once", 1e-7);
                        if (factor > .001 && factor < .999)
                            Check(Math.Abs(composed - tint.W * factor * factor * intrinsic) > 1e-8, "particle midpoint witness rejects squared animation attenuation");
                        if (mode == 1)
                            Near(composed, tint.W * .5 * mid.Flicker * intrinsic, "independent fade midpoint particle witness", 1e-7);
                    }
                    bool beamPresent = HologramEffectKernel.Beam(settings, mid, slot, target, out var beam);
                    bool steadyBeamPresent = HologramEffectKernel.Beam(steady, steadyMid, slot, target, out var steadyBeam);
                    Check(beamPresent && steadyBeamPresent, "midpoint beams remain available for renderer reveal sampling");
                    Near(beam.Opacity, .7, "beam sample opacity contains only its intrinsic setting");
                    Check(beam.Opacity == steadyBeam.Opacity && beam.Start == steadyBeam.Start && beam.End == steadyBeam.End,
                        "beam intrinsic opacity and geometry are independent of temporal flicker");
                    double beamCoordinate = HologramEffectKernel.Coordinate(settings, target, min, max);
                    double beamFactor = HologramEffectKernel.Alpha(settings, mid, beamCoordinate, slot);
                    double beamComposed = HologramEffectKernel.Color(settings, mid, tint, beamCoordinate, slot).W * beam.Opacity;
                    Near(beamComposed, tint.W * beamFactor * .7, "beam midpoint composition applies one temporal/spatial factor", 1e-7);
                    if (beamFactor > .001 && beamFactor < .999)
                        Check(Math.Abs(beamComposed - tint.W * beamFactor * beamFactor * .7) > 1e-8, "beam midpoint witness rejects squared animation attenuation");
                    if (mode == 1)
                        Near(beamComposed, tint.W * .5 * mid.Flicker * .7, "independent fade midpoint beam witness", 1e-7);
                }
            }
        }
    }

    static void Planning()
    {
        var settings = Settings(v => { v[6] = .4; v[7] = .5; }, f => { f[2] = 3; f[3] = 4; f[4] = 3; f[7] = 100; });
        var full = HologramEffectKernel.Plan(settings, 10, 100);
        Check(full.BaseAccepted && full.DepthLayers == 3 && full.Particles == 4 && full.Beams == 3 && full.Primitives == 54 && !full.Clipped, "base plus ghost copies and independent extras are billed exactly");
        var copies = HologramEffectKernel.Plan(settings, 10, 30);
        Check(copies.BaseAccepted && copies.DepthLayers == 2 && copies.Particles == 0 && copies.Beams == 0 && copies.Primitives == 30 && copies.Clipped, "complete depth copies take priority over extras");
        var partial = HologramEffectKernel.Plan(settings, 10, 43);
        Check(partial.BaseAccepted && partial.DepthLayers == 3 && partial.Particles == 1 && partial.Beams == 0 && partial.Primitives == 42 && partial.Clipped, "partial extra prefix leaves unusable remainder unspent");
        var denied = HologramEffectKernel.Plan(settings, 10, 9);
        Check(!denied.BaseAccepted && denied.DepthLayers == 0 && denied.Particles == 0 && denied.Beams == 0 && denied.Primitives == 0 && denied.Clipped, "base is atomic when remaining budget cannot fit it");
        var ceiling = Settings(v => { v[6] = .4; v[7] = .5; }, f => { f[2] = 3; f[3] = 4; f[4] = 3; f[7] = 30; });
        Check(HologramEffectKernel.Plan(ceiling, 10, int.MaxValue).Primitives == 30, "declared effect ceiling remains authoritative with huge viewer budget");
        Check(!HologramEffectKernel.Plan(settings, int.MaxValue, int.MaxValue).BaseAccepted, "oversized base count fails without overflow");
        var expensive = HologramEffectKernel.Plan(settings, 10, 100, int.MaxValue, int.MaxValue);
        Check(expensive.Primitives == 40 && expensive.Particles == 0 && expensive.Beams == 0, "huge extra unit costs are never multiplied before fitting");
        var emptyBase = HologramEffectKernel.Plan(settings, 0, 100);
        Check(emptyBase.BaseAccepted && emptyBase.DepthLayers == 0 && emptyBase.Primitives == 14, "empty base has no ghost duplicates but may emit independent extras");
        var maximum = Settings(v => v[6] = 1, f => { f[2] = 32; f[3] = 4096; f[4] = 2048; f[7] = 65536; });
        var maxPlan = HologramEffectKernel.Plan(maximum, 10, int.MaxValue);
        Check(maxPlan.BaseAccepted && maxPlan.DepthLayers == 32 && maxPlan.Particles == 4096 && maxPlan.Beams == 2048 && maxPlan.Primitives == 12618,
            "maximum admitted count plan remains bounded without allocating samples");
        for (int budget = 0; budget <= 100; budget++)
        {
            var plan = HologramEffectKernel.Plan(settings, 10, budget, 3, 4);
            int limit = Math.Min(budget, 100), expectedDepth = limit < 10 ? 0 : Math.Min(3, (limit - 10) / 10);
            int spent = limit < 10 ? 0 : 10 + expectedDepth * 10;
            int expectedParticles = limit < 10 ? 0 : Math.Min(4, (limit - spent) / 3); spent += expectedParticles * 3;
            int expectedBeams = limit < 10 ? 0 : Math.Min(3, (limit - spent) / 4); spent += expectedBeams * 4;
            Check(plan.BaseAccepted == (limit >= 10) && plan.DepthLayers == expectedDepth && plan.Particles == expectedParticles && plan.Beams == expectedBeams && plan.Primitives == spent,
                "independent budget oracle preserves deterministic whole-primitive prefixes");
            Check(plan.Primitives >= 0 && plan.Primitives <= limit, "planning never exceeds either limit");
        }
        foreach (int bad in new[] { -1, int.MinValue })
        { Reject(() => HologramEffectKernel.Plan(settings, bad, 100), "negative base count"); Reject(() => HologramEffectKernel.Plan(settings, 10, bad), "negative remaining count"); }
        foreach (int bad in new[] { -1, 0 })
        { Reject(() => HologramEffectKernel.Plan(settings, 10, 100, bad, 2), "invalid particle unit cost"); Reject(() => HologramEffectKernel.Plan(settings, 10, 100, 2, bad), "invalid beam unit cost"); }
        Near(HologramEffectKernel.LayerOffset(settings, 0), 0, "base geometry never moves");
        Near(HologramEffectKernel.LayerOpacity(settings, 0), 1, "base geometry never attenuates");
        double first = HologramEffectKernel.LayerOffset(settings, 1), second = HologramEffectKernel.LayerOffset(settings, 2);
        Check(first != 0 && second != 0 && Math.Sign(first) == -Math.Sign(second), "extra depth layers alternate across the source plane");
        for (int i = 1; i <= 3; i++)
        { Check(Math.Abs(HologramEffectKernel.LayerOffset(settings, i)) <= .4, "ghost depth cannot exceed requested depth"); Near(HologramEffectKernel.LayerOpacity(settings, i), Math.Pow(.5, i), "geometric ghost opacity witness"); }
        foreach (int bad in new[] { -1, 4 })
        { Reject(() => HologramEffectKernel.LayerOffset(settings, bad), "invalid depth layer"); Reject(() => HologramEffectKernel.LayerOpacity(settings, bad), "invalid ghost opacity layer"); }
    }

    static void Geometry()
    {
        var settings = Settings(); var min = new Vector3D(-1); var max = new Vector3D(1);
        Near(HologramEffectKernel.Coordinate(settings, Vector3D.Zero, min, max), .5, "centre coordinate");
        Near(HologramEffectKernel.Coordinate(settings, new Vector3D(0, -1, 0), min, max), 0, "default Y axis lower bound");
        Near(HologramEffectKernel.Coordinate(settings, new Vector3D(0, 1, 0), min, max), 1, "default Y axis upper bound");
        Near(HologramEffectKernel.Coordinate(settings, new Vector3D(0, 3, 0), min, max), 1, "out-of-bounds reveal coordinates clamp");
        Near(HologramEffectKernel.Coordinate(settings, Vector3D.Zero, Vector3D.Zero, Vector3D.Zero), .5, "point bounds have neutral coordinate without division");
        var diagonal = Settings(v => { v[18] = v[19] = 1; v[20] = 0; });
        Near(HologramEffectKernel.Coordinate(diagonal, new Vector3D(.5, 0, 0), min, max), .625, "independent diagonal axis projection witness");
        var inverted = Settings(v => v[19] = -1);
        Near(HologramEffectKernel.Coordinate(inverted, new Vector3D(0, .5, 0), min, max), .25, "axis inversion reverses reveal direction");
        foreach (var bad in new[] { new Vector3D(double.NaN, 0, 0), new Vector3D(0, double.PositiveInfinity, 0), new Vector3D(1000001, 0, 0) })
        { Reject(() => HologramEffectKernel.Coordinate(settings, bad, min, max), "invalid point"); Reject(() => HologramEffectKernel.Coordinate(settings, Vector3D.Zero, bad, max), "invalid lower bounds"); Reject(() => HologramEffectKernel.Coordinate(settings, Vector3D.Zero, min, bad), "invalid upper bounds"); }
        Reject(() => HologramEffectKernel.Coordinate(settings, Vector3D.Zero, max, min), "reversed bounds");
        Check(HologramEffectKernel.MatrixValid(MatrixD.Identity), "identity pose valid");
        var rigid = MatrixD.CreateRotationY(.7); rigid.Translation = new Vector3D(100, -20, 40);
        Check(HologramEffectKernel.MatrixValid(rigid), "finite rotated translated pose valid");
        Check(HologramEffectKernel.MatrixValid(MatrixD.CreateScale(2, 3, 4)), "moderate invertible affine scaling valid");
        Check(HologramEffectKernel.MatrixValid(MatrixD.CreateScale(-1, 1, 1)), "invertible reflection valid");
        var singular = MatrixD.Identity; singular.M11 = singular.M12 = singular.M13 = 0;
        Check(!HologramEffectKernel.MatrixValid(singular), "degenerate basis rejected");
        var perspective = MatrixD.Identity; perspective.M14 = .1;
        Check(!HologramEffectKernel.MatrixValid(perspective), "perspective transform rejected");
        var projective = MatrixD.Identity; projective.M44 = 2;
        Check(!HologramEffectKernel.MatrixValid(projective), "non-affine homogeneous scale rejected");
        var far = MatrixD.Identity; far.Translation = new Vector3D(800000, 800000, 0);
        Check(!HologramEffectKernel.MatrixValid(far), "translation norm is bounded");
        var huge = MatrixD.Identity; huge.M11 = 1000001;
        Check(!HologramEffectKernel.MatrixValid(huge), "unsafe coefficient rejected");
        foreach (var field in typeof(MatrixD).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public).Where(x => x.FieldType == typeof(double)))
        {
            object boxed = MatrixD.Identity; field.SetValue(boxed, double.NaN);
            Check(!HologramEffectKernel.MatrixValid((MatrixD)boxed), "every matrix coefficient rejects NaN: " + field.Name);
        }
    }

    static void BoundsClosure()
    {
        var submitted = new[] { new Vector3D(700000, 700000, 0), new Vector3D(-700000, 0, 700000), new Vector3D(0, -700000, -700000) };
        var geometry = HoloMap.Geometry.Wires(submitted, new[] { new Vector2I(0, 1), new Vector2I(1, 2), new Vector2I(2, 0) });
        var min = new Vector3D(geometry.Points.Min(p => p.X), geometry.Points.Min(p => p.Y), geometry.Points.Min(p => p.Z));
        var max = new Vector3D(geometry.Points.Max(p => p.X), geometry.Points.Max(p => p.Y), geometry.Points.Max(p => p.Z));
        Check(geometry.Points.All(HologramEffectKernel.PointValid) && min.Length() > 1000000 && max.Length() > 1000000,
            "admitted radial vertices can produce derived AABB corners outside the vertex radius");
        var settings = Settings(v => { v[0] = .4; v[8] = 1000; v[9] = 0; v[18] = v[19] = v[20] = 1; }, f => { f[1] = 765; f[3] = 512; });
        var frame = HologramEffectKernel.Evaluate(settings, 40.125, 0, true); var tint = new Vector4(.2f, .3f, .4f, .7f);
        foreach (var point in geometry.Points.Concat(new[] { Vector3D.Zero }))
        {
            double coordinate = HologramEffectKernel.Coordinate(settings, point, min, max);
            Check(double.IsFinite(coordinate) && coordinate >= 0 && coordinate <= 1, "admitted geometry's derived bounds remain valid for effect coordinates");
            Vector4 color = HologramEffectKernel.Color(settings, frame, tint, coordinate, 0);
            Check(float.IsFinite(color.X) && float.IsFinite(color.Y) && float.IsFinite(color.Z) && float.IsFinite(color.W) && color.W >= 0 && color.W <= tint.W,
                "effect coloring remains finite for admitted geometry and non-radial AABB corners");
        }
        for (int slot = 0; slot < 512; slot++)
        {
            bool present = HologramEffectKernel.Particle(settings, frame, slot, min, max, out var particle);
            Check(present ? HologramEffectKernel.PointValid(particle.Position) && particle.Opacity > 0 : particle.Position == Vector3D.Zero && particle.Opacity == 0 && particle.Size == 0,
                "wide derived AABB sampling either retains a safe radial point or returns an absent default sample");
        }
        foreach (var invalid in new[] { new Vector3D(double.NaN, 0, 0), new Vector3D(0, double.PositiveInfinity, 0), new Vector3D(0, 0, double.NegativeInfinity), new Vector3D(1000001, 0, 0), new Vector3D(-1000001, 0, 0) })
        {
            Reject(() => HologramEffectKernel.Coordinate(settings, Vector3D.Zero, invalid, max), "derived lower bound nonfinite/component limit");
            Reject(() => HologramEffectKernel.Coordinate(settings, Vector3D.Zero, min, invalid), "derived upper bound nonfinite/component limit");
            Reject(() => HologramEffectKernel.Particle(settings, frame, 0, invalid, max, out _), "particle lower bound nonfinite/component limit");
            Reject(() => HologramEffectKernel.Particle(settings, frame, 0, min, invalid, out _), "particle upper bound nonfinite/component limit");
        }
        Reject(() => HologramEffectKernel.Coordinate(settings, Vector3D.Zero, max, min), "inverted derived bounds");
        Reject(() => HologramEffectKernel.Particle(settings, frame, 0, max, min, out _), "inverted particle bounds");
        Reject(() => HologramEffectKernel.Coordinate(settings, min, min, max), "derived AABB acceptance does not admit unsafe actual geometry points");
    }

    static void Samples()
    {
        var settings = Settings(v => { v[8] = 2; v[9] = .3; v[10] = 2; v[11] = .05; v[12] = .7; v[13] = .04; v[14] = .8; v[15] = .2; v[21] = .1; v[22] = .2; v[23] = .3; }, f => { f[1] = 765; f[3] = 16; f[4] = 8; });
        var min = new Vector3D(-1); var max = new Vector3D(1); var frame = HologramEffectKernel.Evaluate(settings, 40.125, 0, true);
        Vector3D first = default; bool diverse = false;
        for (int slot = 0; slot < 16; slot++)
        {
            Check(HologramEffectKernel.Particle(settings, frame, slot, min, max, out var a), "enabled particle present");
            Check(HologramEffectKernel.Particle(settings, frame, slot, min, max, out var b), "deterministic particle replay present");
            Check(a.Position == b.Position && a.Size == b.Size && a.Opacity == b.Opacity && a.Age == b.Age && a.Lifetime == b.Lifetime, "particle seed/time replay is exact");
            Check(double.IsFinite(a.Position.X) && double.IsFinite(a.Position.Y) && double.IsFinite(a.Position.Z) && a.Position.Length() <= Math.Sqrt(3) + 2 + .3 * 2 + 1e-6, "particle geometry stays inside declared radius and drift envelope");
            Check(a.Size >= .05 * .65 && a.Size <= .05 * 1.35, "particle size variation stays within configured envelope"); Near(a.Lifetime, 2, "particle lifetime preserved");
            Check(a.Age >= 0 && a.Age < 2 && a.Opacity >= 0 && a.Opacity <= .7 + 1e-7, "particle lifetime and fade bounded");
            var later = HologramEffectKernel.Evaluate(settings, 42.125, 0, true);
            Check(HologramEffectKernel.Particle(settings, later, slot, min, max, out var next), "next cycle particle present"); Near(next.Age, a.Age, "lifetime cycle preserves phased age");
            if (slot == 0) first = a.Position; else diverse |= a.Position != first;
        }
        Check(diverse, "particle slots do not collapse to the same sample");
        var changedSeed = Settings(v => { v[8] = 2; v[9] = .3; v[10] = 2; v[12] = .7; }, f => { f[1] = 766; f[3] = 16; });
        var differentFrame = HologramEffectKernel.Evaluate(changedSeed, 40.125, 0, true);
        Check(HologramEffectKernel.Particle(changedSeed, differentFrame, 0, min, max, out var different) && different.Position != first, "seed changes particle sequence");
        var target = new Vector3D(2, 3, -4); var emitter = new Vector3D(.1, .2, .3); var ray = Vector3D.Normalize(target - emitter);
        for (int slot = 0; slot < 8; slot++)
        {
            Check(HologramEffectKernel.Beam(settings, frame, slot, target, out var a), "enabled beam present");
            Check(HologramEffectKernel.Beam(settings, frame, slot, target, out var b) && a.Start == b.Start && a.End == b.End && a.Width == b.Width && a.Opacity == b.Opacity, "beam replay is deterministic");
            Check(a.Start == emitter, "beam uses declared emitter");
            Vector3D jitter = a.End - target;
            Check(jitter.Length() <= .2 + 1e-9 && Math.Abs(Vector3D.Dot(jitter, ray)) <= 1e-9, "beam jitter is bounded and perpendicular");
            Near(a.Width, .04, "beam width preserved", 1e-7); Check(a.Opacity >= 0 && a.Opacity <= .8 + 1e-7, "beam opacity bounded");
        }
        Check(!HologramEffectKernel.Beam(settings, frame, 0, emitter, out _), "zero-length beams fail closed");
        var quiet = Settings(v => { v[12] = v[14] = 0; }, f => { f[3] = f[4] = 1; });
        var quietFrame = HologramEffectKernel.Evaluate(quiet, 2, 0, true);
        Check(!HologramEffectKernel.Particle(quiet, quietFrame, 0, min, max, out _) && !HologramEffectKernel.Beam(quiet, quietFrame, 0, target, out _), "zero-opacity configured extras fail closed");
        var exact = Settings(flags: f => f[4] = 1);
        Check(HologramEffectKernel.Beam(exact, HologramEffectKernel.Evaluate(exact, 2, 0, true), 0, target, out var fixedRay) && fixedRay.Start == Vector3D.Zero && fixedRay.End == target,
            "zero-jitter beam joins emitter and intended element exactly");
        foreach (double clock in new[] { -1e9, -10d, 0d, 1e9 })
        {
            var extreme = HologramEffectKernel.Evaluate(settings, clock, 0, true);
            for (int slot = 0; slot < 16; slot++)
                if (HologramEffectKernel.Particle(settings, extreme, slot, min, max, out var sample))
                    Check(sample.Age >= 0 && sample.Age < 2 && double.IsFinite(sample.Position.X) && double.IsFinite(sample.Opacity), "extreme admitted clocks retain finite bounded particle samples");
        }
        var edge = Settings(v => { v[14] = 1; v[15] = 100; v[21] = 999999; }, f => f[4] = 1);
        Check(!HologramEffectKernel.Beam(edge, HologramEffectKernel.Evaluate(edge, 3.25, 0, true), 0, new Vector3D(1000000, 0, 0), out _),
            "computed beam jitter outside safe geometry range fails closed");
        foreach (var bad in new[] { new Vector3D(double.NaN, 0, 0), new Vector3D(0, 0, double.NegativeInfinity), new Vector3D(1000001, 0, 0) })
        { Reject(() => HologramEffectKernel.Particle(settings, frame, 0, bad, max, out _), "invalid particle bounds"); Reject(() => HologramEffectKernel.Beam(settings, frame, 0, bad, out _), "invalid beam target"); }
        Reject(() => HologramEffectKernel.Particle(settings, frame, -1, min, max, out _), "negative particle slot");
        Reject(() => HologramEffectKernel.Particle(settings, frame, 16, min, max, out _), "particle slot outside configured prefix");
        Reject(() => HologramEffectKernel.Beam(settings, frame, -1, target, out _), "negative beam slot");
        Reject(() => HologramEffectKernel.Beam(settings, frame, 8, target, out _), "beam slot outside configured prefix");
    }
}

#if HOLOGRAM_EFFECT_TESTS_ONLY
internal static class HologramEffectDiagnostic
{
    static void Main()
    {
        string game = Environment.GetEnvironmentVariable("SE_BIN") ?? @"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
        { string path = System.IO.Path.Combine(game, name.Name + ".dll"); return System.IO.File.Exists(path) ? context.LoadFromAssemblyPath(path) : null; };
        Console.WriteLine("Hologram effect kernel: " + HologramEffectTests.Run() + " assertions passed.");
    }
}
#endif
