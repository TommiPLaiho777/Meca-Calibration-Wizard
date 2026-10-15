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
        Require(brake.Result.PeakSpreadSummary.Contains("WinMM peak spread") &&
                brake.Result.PeakSpreadSummary.Contains("not a physical-force test"),
            "non-saturated WinMM peaks are not physical-force measurements");
        Console.WriteLine("PASS: three-press brake calibration");

        var throttle = Session(PedalKind.Throttle, 3, false,
            new uint[] { 65535, 65500, 65490 }, new [] { 0 });
        Require(throttle.Phase == GuidePhase.Complete && throttle.Result!.Mapping.Axis == 3,
            "R axis recognition with X reserved");
        Require(throttle.Result.Mapping.Full == 65500, "throttle median");
        Require(throttle.Result.AtAxisLimit, "clipped throttle limit is flagged");
        Require(throttle.Result.PeakSpreadSummary.Contains("repeatability unknown") &&
                !throttle.Result.PeakSpreadSummary.Contains("%"),
            "saturated WinMM peaks must not display a misleading percentage");
        var observedBrake = Session(PedalKind.Brake, 0, false, new uint[] { 45842, 65535, 65535 });
        Require(observedBrake.Result is { AtAxisLimit: true } &&
                observedBrake.Result.PeakSpreadSummary.Contains("repeatability unknown") &&
                !observedBrake.Result.PeakSpreadSummary.Contains("%"),
            "2026-10-15 hardware peak pattern must not imply force repeatability");
        Console.WriteLine("PASS: axis limits correctly qualify peak-spread interpretation");

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


        var flat = SignalHealthDiagnostics.Evaluate(Enumerable.Repeat(0u, 76).ToArray(), 0, 65535, 3);
        Require(flat.SampleCount == 76 && flat.SpanWinmm == 0 &&
                flat.StandardDeviationWinmm == 0 && flat.SamplingRateHz == 25 &&
                flat.Status == "No variation resolved", "stable does not mean proven EMI-free");
        var readings = Enumerable.Repeat(1000u, 76).ToArray();
        readings[25] = 4000;
        var motion = SignalHealthDiagnostics.Evaluate(readings, 0, 65535, 3);
        Require(motion.SpanWinmm == 3000 && motion.LargeStepCount == 2 &&
                motion.Status == "Pedal motion likely", "large signal not labelled EMI");
        var small = SignalHealthDiagnostics.Evaluate(
            new uint[] { 1000, 1010, 1020, 990, 1005, 1015, 995, 1005 }, 0, 65535, 1);
        Require(small.StandardDeviationWinmm > 0 && small.Status == "Small variation resolved",
                "smaller variation is retained");
        Require(SignalHealthDiagnostics.Evaluate(Array.Empty<uint>(), 0, 65535, 3).Status == "No samples",
                "empty recording is not a stable result");
        var fixedClock = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var noiseRun = new NoiseRun(fixedClock);
        for (int i = 0; i <= 75; i++)
            noiseRun.Observe(Axes(0, 42), fixedClock.AddMilliseconds(i*40));
        Require(noiseRun.Complete && noiseRun.Analyze(0, 0, 65535).SpanWinmm == 0,
                "fixed-time signal capture");
        // Observed 2026-10-15 WinMM brake idle-check span reached 25769;
        // this cannot be interpreted as idle electrical noise.
        var observedMotion = Enumerable.Repeat(0u, 64).ToArray();
        observedMotion[18] = 25769u;
        var observedHealth = SignalHealthDiagnostics.Evaluate(observedMotion, 0, 65535, 3.045);
        Require(observedHealth.Status == "Pedal motion likely" &&
                observedHealth.Guidance.Contains("repeat", StringComparison.OrdinalIgnoreCase) &&
                observedHealth.Guidance.Contains("not proof of EMI"),
            "large WinMM movement must prompt repeat, not a false EMI diagnosis");
        Require(flat.Guidance.Contains("deadzones"),
            "zero variation must not imply a proven noise-free sensor");
        Console.WriteLine("PASS: signal-health metrics, timing and conservative statuses");

        Console.WriteLine("ALL CHECKS PASSED");
        return 0;
    }
}
