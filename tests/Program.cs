using System.Text.Json;
using MecaCalibrationWizard;

static class Checks
{
    private static readonly WinmmJoystick.Device Device = new(
        0, "Simulated joystick", new uint[6], Enumerable.Repeat(65535u, 6).ToArray());

    private static uint[] Axes(int axis = 0, uint value = 0)
    {
        var a = new uint[6];
        a[axis] = value;
        return a;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("FAILED: " + message);
    }

    private static GuidedCalibration Session(PedalKind kind, int axis, bool inverted,
        uint[] peaks, IEnumerable<int>? reserved = null)
    {
        var now = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
        var guide = new GuidedCalibration(kind, Device, reserved ?? Array.Empty<int>(), now);
        var released = inverted ? 65535u : 0u;
        for (int i = 0; i < 32; i++)
        {
            now = now.AddMilliseconds(40);
            guide.Observe(Axes(axis, released), now);
        }
        Require(guide.Phase == GuidePhase.AwaitingPress, "rest baseline step");
        foreach (var peak in peaks)
        {
            foreach (var fraction in new [] { 0.12, 0.60, 1.0, 0.40, 0.0 })
            {
                now = now.AddMilliseconds(40);
                var input = inverted
                    ? (uint)(released - peak * fraction)
                    : (uint)(peak * fraction);
                guide.Observe(Axes(axis, input), now);
            }
        }
        return guide;
    }

    public static int Main()
    {
        var brake = Session(PedalKind.Brake, 0, false, new uint[] { 40000, 41000, 40500 });
        Require(brake.Phase == GuidePhase.Complete, "three brake strokes complete");
        Require(brake.Result is not null && brake.Result.Mapping.Axis == 0, "correct brake axis");
        Require(brake.Result!.Mapping.Full == 40500, "median brake peak");
        Require(brake.Result.RepeatabilitySpreadPercent > 2 &&
                brake.Result.RepeatabilitySpreadPercent < 3, "repeatability percentage");
        Require(brake.Result.Mapping.Percent(20250) is > 49 and < 51, "preview scaling");
        Console.WriteLine("PASS: three-press brake calibration");

        var throttle = Session(PedalKind.Throttle, 3, false,
            new uint[] { 65535, 65500, 65490 }, new [] { 0 });
        Require(throttle.Phase == GuidePhase.Complete && throttle.Result!.Mapping.Axis == 3,
            "R axis recognition with X reserved");
        Require(throttle.Result.Mapping.Full == 65500, "throttle median");
        Require(throttle.Result.AtAxisLimit, "clipped throttle limit is flagged");
        Console.WriteLine("PASS: throttle axis and reserved axes");

        var inverted = Session(PedalKind.Clutch, 2, true, new uint[] { 42000, 42500, 42000 });
        Require(inverted.Phase == GuidePhase.Complete && inverted.Result is not null,
            "inverted axis completes");
        Require(inverted.Result!.Mapping.Full < inverted.Result.Mapping.Rest,
            "inverted full below rest");
        Require(inverted.Result.Mapping.Percent(inverted.Result.Mapping.Full) > 99.9,
            "inverted range scaling");
        Console.WriteLine("PASS: inverted pedal range");

        var time = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
        var idle = new GuidedCalibration(PedalKind.Brake, Device, Array.Empty<int>(), time);
        for (int i = 0; i < 32; i++)
        {
            time = time.AddMilliseconds(40);
            var a = Axes(0, 154);
            idle.Observe(a, time);
        }
        Require(idle.Phase == GuidePhase.AwaitingPress && idle.Axis == -1,
            "idle value must not falsely identify unused clutch");
        Console.WriteLine("PASS: static 154 count idle does not identify a pedal");

        time = time.AddSeconds(46);
        idle.Observe(Axes(0, 154), time);
        Require(idle.Phase == GuidePhase.Failed, "timeout retry");
        Console.WriteLine("PASS: capture timeout");

        // A progressive sweep should include intermediate readings and an endpoint hold.
        var sweepClock = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
        var sweep = new InputSweepRun(0, 0, 65535, sweepClock);
        for (int i = 0; i <= 200; i++)
        {
            var t = sweepClock.AddMilliseconds(i * 40);
            // 0..100 samples press; 20 samples hold at max; remainder release.
            uint val = i <= 100 ? (uint)Math.Round(65535.0 * i / 100)
                : i <= 120 ? 65535u
                : (uint)Math.Round(65535.0 * (200 - i) / 80);
            sweep.Observe(Axes(0, val), t);
        }
        Require(sweep.Complete && sweep.Result is not null, "sweep completes");
        Require(sweep.Result!.IntermediateSamples > 100, "intermediate values recorded");
        Require(sweep.Result.AtHighLimitSamples >= 20, "endpoint samples counted");
        Require(sweep.Result.DistinctValues >= 50, "axis distinguishable steps");
        Require(sweep.Result.Trace.Count == 201, "time trace recorded");
        Console.WriteLine("PASS: 8s progressive sweep and clipping statistics");

        var stationary = new InputSweepRun(0, 0, 65535, sweepClock);
        stationary.Observe(Axes(), sweepClock);
        stationary.Observe(Axes(), sweepClock.AddSeconds(8));
        Require(stationary.Result is not null && stationary.Result.DistinctValues == 1 &&
                stationary.Result.IntermediateSamples == 0, "idle signal is not full sweep");
        Console.WriteLine("PASS: no movement is not a valid full sweep");

        var clock = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var signal = new NoiseRun(clock);
        Require(signal.Statistics(0) is null, "empty signal has no statistics");
        for (int i = 0; i < 4; i++)
            signal.Observe(Axes(0, (uint)(i * 10)), clock.AddMilliseconds(i * 40));
        var statistics = signal.Statistics(0)!;
        Require(statistics.SampleCount == 4 && statistics.MinimumWinmm == 0 &&
                statistics.MaximumWinmm == 30 && statistics.MedianWinmm == 15,
            "WinMM count, range and median");
        Require(Math.Abs(statistics.StandardDeviationWinmm - 11.18) < .01 &&
                statistics.LargestAdjacentStepWinmm == 10 &&
                statistics.ObservedSamplesPerSecond == 25,
            "WinMM variation and observed sample rate");
        var mutable = Axes(0, 100);
        signal.Observe(mutable, clock.AddMilliseconds(160));
        mutable[0] = 60000;
        Require(signal.Statistics(0)!.MaximumWinmm == 100, "sample values are copied");
        Console.WriteLine("PASS: WinMM signal statistics");

        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "real-winmm-sweeps-20261014.json");
        using (var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath)))
        {
            foreach (var pedal in new[] { "Brake", "Throttle" })
            {
                var trace = fixture.RootElement.GetProperty(pedal).EnumerateArray().Select(element =>
                    new SweepPoint(element.GetProperty("TimeMs").GetInt64(), element.GetProperty("WinmmValue").GetUInt32()))
                    .ToArray();
                var result = SweepDiagnostics.Evaluate(trace, 0, 65535);
                Require(trace.Length == 172, $"{pedal}: reference trace contains 172 samples");
                Require(result.CompletePressAndRelease, $"{pedal}: reference press+release recognized");
                Require(result.ReachesReportedMaximum, $"{pedal}: Windows ceiling recognized");
                Require(result.Status == "Sweep captured", $"{pedal}: valid capture classification");
                Require(result.DistinctValues == (pedal == "Brake" ? 30 : 22),
                    $"{pedal}: actual distinct count preserved");
                Console.WriteLine($"PASS: recorded October 14 {pedal.ToLowerInvariant()} sweep regression");
            }
        }
        var emptyTrace = new[] { new SweepPoint(0, 0u), new SweepPoint(8000, 0u) };
        Require(SweepDiagnostics.Evaluate(emptyTrace, 0, 65535).Status == "Insufficient data",
            "short trace rejects false validation");
        var idleTrace = Enumerable.Range(0, 30).Select(i => new SweepPoint(i*100, 0u)).ToArray();
        Require(SweepDiagnostics.Evaluate(idleTrace, 0, 65535).Status == "Little movement",
            "idle trace cannot be accepted as complete sweep");
        var missingRelease = Enumerable.Range(0, 30).Select(i => new SweepPoint(i*100, (uint)(65535 * i / 29))).ToArray();
        Require(SweepDiagnostics.Evaluate(missingRelease, 0, 65535).Status == "Incomplete sweep",
            "missing release fails the quality check");
        Console.WriteLine("PASS: invalid sweep cases correctly rejected");

        Console.WriteLine("ALL CHECKS PASSED");
        return 0;
    }
}
