using System;
using System.Collections.Generic;
using HDRClientRenderer;

internal static class DirectCameraCapturePolicyTests
{
    internal static void Run(Action<bool, string> check)
    {
        var policy = new DirectCameraCapturePolicy();
        var pose = new object();
        for (long i = 1; i <= 6; i++)
            check(policy.Request(i, 105, 1024, 1000, 0, pose) != null, "direct capture admits six physical cameras");
        check(policy.Request(7, 105, 1024, 30, 0, pose) == null && policy.Count == 6,
            "direct capture rejects a seventh source");
        check(policy.Select(0, 1, false, false).Length == 0 && policy.Select(0, 1, true, true).Length == 0,
            "auxiliary and recursive scene calls consume no capture work");
        var first = policy.Select(0, 1, true, false);
        var second = policy.Select(1d / 60, 2, true, false);
        check(first.Length == 3 && second.Length == 3, "at most three sources are selected per main frame");
        var identities = new HashSet<long>();
        foreach (var source in first) identities.Add(source.Camera);
        foreach (var source in second) identities.Add(source.Camera);
        check(identities.Count == 6, "six demanding cameras each receive a turn in two main frames");
        check(policy.Select(1d / 60, 2, true, false).Length == 0,
            "duplicate main-frame callbacks cannot spend a second budget");
        check(policy.Select(.025, 3, true, false).Length == 0,
            "requested rates above thirty cannot bypass the thirty Hz ceiling");
        check(policy.Select(1d / 30, 4, true, false).Length == 3,
            "the first three sources become due after one thirtieth second");
        var sourceOne = policy.Find(1); long generation = sourceOne.Generation;
        check(policy.Complete(sourceOne, generation, new object(), .04) && policy.Valid(1, generation, .04),
            "only completed capture evidence is valid");
        policy.Request(1, 105, 1024, 30, .05, new object());
        check(policy.Find(1).Generation == generation && policy.Valid(1, generation, .05),
            "moving pose demand reuses resources and keeps a stable generation");
        policy.Request(1, 110, 1024, 30, .06, pose);
        check(!policy.Valid(1, generation, .06) && policy.Find(1).Completed == null,
            "a lens reconfiguration immediately invalidates previous capture evidence");
        check(!policy.Complete(sourceOne, generation, new object(), .06),
            "an old in-flight capture cannot publish into a new configuration");
        long replacementGeneration = sourceOne.Generation;
        policy.Invalidate(sourceOne);
        check(!policy.Complete(sourceOne, replacementGeneration, new object(), .07),
            "resource and identity invalidation rejects stale render completion");
        sourceOne.DemandUntil = double.NegativeInfinity;
        check(!policy.Live(sourceOne, sourceOne.Generation, .08), "revocation immediately removes authorization demand");
        check(policy.Prune(.08).Length == 1 && policy.Find(1) == null,
            "revoked sources can be retired before their former timeout");
        check(policy.Request(7, 105, 1024, 30, .08, pose) != null, "retiring a source releases its quota");
        check(policy.Prune(1).Length == 6 && policy.Count == 0 && policy.Select(1, 5, true, false).Length == 0,
            "expired visibility demand stops all capture work");
        check(policy.Request(0, 105, 1024, 30, 1, pose) == null &&
            policy.Request(1, double.NaN, 1024, 30, 1, pose) == null &&
            policy.Request(1, 105, 1000, 30, 1, pose) == null &&
            policy.Request(1, 105, 1024, double.PositiveInfinity, 1, pose) == null &&
            policy.Request(1, 105, 1024, 0, 1, pose) == null,
            "invalid identity, lens, resolution and rate requests fail closed");
        check(policy.Request(1, 60, 256, 10, 1, pose) != null && policy.Request(2, 120, 512, 30, 1, pose) != null,
            "supported lens and capture resolution bounds are inclusive");
        check(DirectCameraCapturePolicy.BoundResolution(1024, 1920, 720, 1920, 1080) == 512 &&
            DirectCameraCapturePolicy.BoundResolution(1024, 1920, 1080, 640, 480) == 256 &&
            DirectCameraCapturePolicy.BoundResolution(1024, 0, 1080, 1920, 1080) == 0,
            "square capture is bounded by actual resources and uses a valid complete-mip power-of-two descriptor");
        long oldEpochGeneration = policy.Find(1).Generation;
        policy.Clear(); policy.Request(1, 60, 256, 10, 2, pose);
        check(policy.Find(1).Generation != oldEpochGeneration && !policy.Valid(1, oldEpochGeneration, 2),
            "world epochs never revive retired capture evidence");
        var warmSource = policy.Find(1); long warmGeneration = warmSource.Generation;
        policy.Complete(warmSource, warmGeneration, new object(), 2);
        policy.Warm(1, 2.1);
        check(policy.Valid(1, warmGeneration, 2.2) && policy.Select(2.2, 10, true, false).Length == 0,
            "warm offscreen sources retain evidence without spending capture work");
        policy.Warm(1, 2.4);
        check(policy.Prune(2.61).Length == 1, "repeated warm notifications do not retain resources beyond the off delay");
    }
}
