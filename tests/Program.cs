using System.Text.Json;
using System.Xml.Linq;
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
            foreach (var fraction in new [] { 0.12, 0.60, 1.0, 0.40, 0.0, 0.0, 0.0 })
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
        // Hardware-free C# regression: even an unmapped WinMM axis with an
        // impossible value must invalidate the entire live preview sample.
        // No Windows calibration, HID device, or pedal is accessed here.
        var boundsDevice = new WinmmJoystick.Device(7, "Synthetic nonzero bounds",
            new uint[] { 1000, 10, 0, 0, 0, 0 },
            new uint[] { 2000, 20, 65535, 65535, 65535, 65535 });
        Require(WinmmJoystick.IsSampleWithinAxisBounds(boundsDevice,
                    new uint[] { 1500, 15, 0, 0, 0, 0 }) &&
                !WinmmJoystick.IsSampleWithinAxisBounds(boundsDevice,
                    new uint[] { 999, 15, 0, 0, 0, 0 }) &&
                !WinmmJoystick.IsSampleWithinAxisBounds(boundsDevice,
                    new uint[] { 2001, 15, 0, 0, 0, 0 }) &&
                !WinmmJoystick.IsSampleWithinAxisBounds(boundsDevice,
                    new uint[] { 1500, 21, 0, 0, 0, 0 }) &&
                !WinmmJoystick.IsSampleWithinAxisBounds(boundsDevice,
                    new uint[] { 1500, 15, 0 }),
            "out-of-range WinMM readings on any axis, or malformed samples, must fail closed");
        Console.WriteLine("PASS: all WinMM axes checked against capability bounds (synthetic C#)");

        // Synthetic controller selection: a shared product name cannot identify
        // which physical device is present. Do not pick the first MECA label
        // when two are available; retain a deliberate previous WinMM ID only
        // as a selection hint, never as verified USB identity.
        var mecaOne = Device with { Id = 3, Name = "MECA CUP1" };
        var mecaTwo = Device with { Id = 4, Name = "meca pedals" };
        var generic = Device with { Id = 5, Name = "Other joystick" };
        Require(WinmmJoystick.SelectSuggestedDevice(new[] { mecaOne, generic }, null) == mecaOne &&
                WinmmJoystick.SelectSuggestedDevice(new[] { mecaOne, mecaTwo, generic }, null) is null &&
                WinmmJoystick.SelectSuggestedDevice(new[] { mecaOne, mecaTwo, generic }, 4) == mecaTwo &&
                WinmmJoystick.SelectSuggestedDevice(new[] { mecaOne, mecaTwo, generic }, 5) == generic &&
                WinmmJoystick.SelectSuggestedDevice(new[] { generic }, null) == generic &&
                WinmmJoystick.SelectSuggestedDevice(new[] { generic, Device with { Id = 6, Name = "Other gamepad" } }, null) is null,
            "ambiguous WinMM product names must require explicit controller selection");
        Console.WriteLine("PASS: ambiguous MECA WinMM controller names never auto-select (synthetic C#)");

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
        // The exported Repeatability label describes WinMM peaks only; no
        // physical-force repeatability is established by this preview.
        Require(brake.Result.RepeatabilityLabel == "Similar WinMM peaks (force unverified)" &&
                (brake.Result with { RepeatabilitySpreadPercent = 8.0 }).RepeatabilityLabel ==
                    "WinMM peak variation (force unverified)" &&
                (brake.Result with { RepeatabilitySpreadPercent = 13.0 }).RepeatabilityLabel ==
                    "Repeat WinMM capture (force unverified)",
            "WinMM peak similarity labels must not claim physical-force repeatability");
        Console.WriteLine("PASS: three-press brake calibration");

        // Synthetic-only regression: isolated one-sample WinMM spikes followed
        // by release must not count as pedal presses or produce a mapping.
        var spikeClock = new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
        var spikeGuide = new GuidedCalibration(PedalKind.Brake, Device,
            Array.Empty<int>(), spikeClock);
        for (int i = 1; i <= 32; i++)
            spikeGuide.Observe(Axes(0, 0), spikeClock.AddMilliseconds(i * 40));
        Require(spikeGuide.Phase == GuidePhase.AwaitingPress,
            "synthetic spike fixture must finish rest sampling");
        spikeClock = spikeClock.AddMilliseconds(32 * 40);
        for (int i = 0; i < 3; i++)
        {
            spikeClock = spikeClock.AddMilliseconds(40);
            spikeGuide.Observe(Axes(0, 40000), spikeClock);
            spikeClock = spikeClock.AddMilliseconds(40);
            spikeGuide.Observe(Axes(0, 0), spikeClock);
            for (int releaseSample = 0; releaseSample < 2; releaseSample++)
            {
                spikeClock = spikeClock.AddMilliseconds(40);
                spikeGuide.Observe(Axes(0, 0), spikeClock);
            }
        }
        Require(spikeGuide.Phase == GuidePhase.AwaitingPress &&
                spikeGuide.PressesCompleted == 0 && spikeGuide.Result is null,
            "three one-sample WinMM spikes must not become a valid calibration");
        Console.WriteLine("PASS: transient WinMM spikes do not count as guided presses (synthetic C#)");

        // A one-sample full-scale WinMM glitch followed by a sustained
        // lower-amplitude press must not become the guided pedal endpoint.
        // Check both axis polarities without accessing a physical controller.
        foreach (bool invertedSpike in new[] { false, true })
        {
            var peakClock = new DateTime(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc);
            var peakGuide = new GuidedCalibration(PedalKind.Brake, Device,
                Array.Empty<int>(), peakClock);
            uint rest = invertedSpike ? 65535u : 0u;
            uint sustained = invertedSpike ? 50535u : 15000u;
            uint isolatedExtreme = invertedSpike ? 0u : 65535u;
            for (int i = 1; i <= 32; i++)
                peakGuide.Observe(Axes(0, rest), peakClock.AddMilliseconds(i * 40));
            Require(peakGuide.Phase == GuidePhase.AwaitingPress,
                "peak outlier fixture must finish rest sampling");
            peakClock = peakClock.AddMilliseconds(32 * 40);
            foreach (uint value in new[] { isolatedExtreme, sustained, sustained,
                                          sustained, sustained, rest, rest, rest })
            {
                peakClock = peakClock.AddMilliseconds(40);
                peakGuide.Observe(Axes(0, value), peakClock);
            }
            Require(peakGuide.Phase == GuidePhase.Failed &&
                    peakGuide.Result is null && peakGuide.PressesCompleted == 0 &&
                    peakGuide.FailureReason.Contains("Isolated WinMM peak"),
                "single extreme WinMM peak must invalidate guided capture");
        }
        Console.WriteLine("PASS: isolated WinMM guided peak outliers rejected in both polarities (synthetic C#)");

        // Three nonconsecutive high samples must not be accumulated into a
        // sustained press when each is separated by a below-threshold reading
        // that is still above the release threshold. Synthetic WinMM only.
        var interruptedClock = new DateTime(2026, 10, 26, 11, 0, 0, DateTimeKind.Utc);
        var interruptedGuide = new GuidedCalibration(PedalKind.Brake, Device,
            Array.Empty<int>(), interruptedClock);
        for (int i = 1; i <= 32; i++)
            interruptedGuide.Observe(Axes(0, 0), interruptedClock.AddMilliseconds(i * 40));
        Require(interruptedGuide.Phase == GuidePhase.AwaitingPress,
            "interrupted-press fixture must finish rest sampling");
        interruptedClock = interruptedClock.AddMilliseconds(32 * 40);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            foreach (uint value in new uint[] { 40000, 1500, 40000, 1500, 40000, 0, 0, 0 })
            {
                interruptedClock = interruptedClock.AddMilliseconds(40);
                interruptedGuide.Observe(Axes(0, value), interruptedClock);
            }
        }
        Require(interruptedGuide.Phase == GuidePhase.AwaitingPress &&
                interruptedGuide.PressesCompleted == 0 && interruptedGuide.Result is null,
            "separated WinMM high samples must not count as sustained presses");
        Console.WriteLine("PASS: interrupted WinMM press streaks rejected (synthetic C#)");

        var throttle = Session(PedalKind.Throttle, 3, false,
            new uint[] { 65535, 65500, 65490 }, new [] { 0 });
        Require(throttle.Phase == GuidePhase.Complete && throttle.Result!.Mapping.Axis == 3,
            "R axis recognition with X reserved");
        Require(throttle.Result.Mapping.Full == 65500, "throttle median");
        Require(throttle.Result.AtAxisLimit, "clipped throttle limit is flagged");
        Require(throttle.Result.RepeatabilityLabel == "WinMM limit (force unverified)",
            "WinMM endpoint must not imply physical-force repeatability");
        Require(throttle.Result.PeakSpreadSummary.Contains("near-limit band") &&
                throttle.Result.PeakSpreadSummary.Contains("repeatability unknown") &&
                !throttle.Result.PeakSpreadSummary.Contains("axis limit reached") &&
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

        // Hardware-free regression: a wall-clock jump must not make a short
        // rest baseline or a press capture appear valid. No WinMM device is used.
        var guidedClockStart = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        var guidedClockJump = new GuidedCalibration(PedalKind.Brake, Device,
            Array.Empty<int>(), guidedClockStart);
        for (int i = 1; i <= 16; i++)
            guidedClockJump.Observe(Axes(0, 0), guidedClockStart.AddMilliseconds(i * 40));
        guidedClockJump.Observe(Axes(0, 0), guidedClockStart.AddMilliseconds(1300));
        Require(guidedClockJump.Phase == GuidePhase.Failed &&
                guidedClockJump.Result is null &&
                guidedClockJump.FailureReason.Contains("timing interrupted"),
            "forward clock jump must invalidate guided rest baseline");

        var guidedClockRollback = new GuidedCalibration(PedalKind.Brake, Device,
            Array.Empty<int>(), guidedClockStart);
        for (int i = 1; i <= 20; i++)
            guidedClockRollback.Observe(Axes(0, 0), guidedClockStart.AddMilliseconds(i * 40));
        guidedClockRollback.Observe(Axes(0, 0), guidedClockStart.AddMilliseconds(760));
        Require(guidedClockRollback.Phase == GuidePhase.Failed &&
                guidedClockRollback.Result is null,
            "backward clock jump must invalidate guided rest baseline");

        var guidedPressJump = new GuidedCalibration(PedalKind.Brake, Device,
            Array.Empty<int>(), guidedClockStart);
        for (int i = 1; i <= 32; i++)
            guidedPressJump.Observe(Axes(0, 0), guidedClockStart.AddMilliseconds(i * 40));
        Require(guidedPressJump.Phase == GuidePhase.AwaitingPress,
            "normal guided rest samples must still pass");
        guidedPressJump.Observe(Axes(0, 20000), guidedClockStart.AddMilliseconds(1900));
        Require(guidedPressJump.Phase == GuidePhase.Failed &&
                guidedPressJump.Result is null,
            "forward clock jump during press must not produce a captured pedal");
        Console.WriteLine("PASS: guided WinMM timestamp interruptions require recapture (synthetic C#)");


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
        Require(sweep.Result.HighLimitInterpretation.Contains("WinMM high-end axis band observed") &&
                sweep.Result.HighLimitInterpretation.Contains("does not verify sensor ADC clipping"),
            "WinMM axis saturation must not be presented as sensor clipping");
        // A near-high WinMM sample is counted in the band without reaching
        // the exact reported axis maximum. Keep GUI wording precise.
        var nearHighSweep = new InputSweepRun(0, 0, 65535, sweepClock, requireContinuousSamples: false);
        nearHighSweep.Observe(Axes(0, 0), sweepClock);
        nearHighSweep.Observe(Axes(0, 65480), sweepClock.AddSeconds(8));
        Require(nearHighSweep.Result is { AtHighLimitSamples: 1, MaximumWinmm: 65480 } &&
                nearHighSweep.Result.Summary.Contains("in high-end WinMM band") &&
                !nearHighSweep.Result.Summary.Contains("at high axis limit"),
            "near-high WinMM band counts must not claim exact axis maximum");
        // Inverted WinMM mappings reach the raw high band at release, not press.
        // The exported interpretation must not imply high pedal force or ADC clipping.
        Require(sweep.Result.HighLimitInterpretationFor(false) == sweep.Result.HighLimitInterpretation &&
                sweep.Result.HighLimitInterpretationFor(true).Contains("released end of this inverted mapping") &&
                sweep.Result.HighLimitInterpretationFor(true).Contains("does not verify sensor ADC clipping") &&
                stationaryHighBandExplanation().Contains("released end of this inverted mapping"),
            "inverted WinMM high band must be described as the released endpoint");
        static string stationaryHighBandExplanation() => new InputSweepResult(
            "WINMM_JOYGETPOSEX_NOT_SENSOR_ADC", 0, 0, 1, 1, 0, 0, 0, null, 0,
            Array.Empty<SweepPoint>()).HighLimitInterpretationFor(true);
        var sweepJson = JsonSerializer.Serialize(sweep.Result);
        Require(sweepJson.Contains("\"HighLimitInterpretation\"") &&
                sweepJson.Contains("does not verify sensor ADC clipping") &&
                !sweepJson.Contains("sensor clipped"),
            "serialized sweep must explain the high-limit provenance");
        Console.WriteLine("PASS: 8s progressive sweep and clipping statistics");

        // Narrow WinMM axes must never count the same sample at both limits.
        // Use only synthetic values: no physical pedal or Windows device access.
        foreach (var (width, expectedLow, expectedHigh) in new[]
        {
            (1u, 1, 1), (2u, 1, 1), (3u, 2, 2)
        })
        {
            var narrow = new InputSweepRun(0, 0, width, sweepClock, requireContinuousSamples: false);
            for (uint value = 0; value <= width; value++)
                narrow.Observe(Axes(0, value), sweepClock.AddMilliseconds(value * 100));
            narrow.Observe(Axes(0, 0), sweepClock.AddSeconds(8));
            Require(narrow.Result is { } narrowResult &&
                    narrowResult.AtLowLimitSamples == expectedLow + 1 &&
                    narrowResult.AtHighLimitSamples == expectedHigh &&
                    narrowResult.AtLowLimitSamples + narrowResult.AtHighLimitSamples <= narrowResult.SampleCount,
                $"axis width {width}: WinMM low/high limit bands must be disjoint");
        }
        Console.WriteLine("PASS: narrow WinMM axis endpoint bands are disjoint");

        var stationary = new InputSweepRun(0, 0, 65535, sweepClock, requireContinuousSamples: false);
        stationary.Observe(Axes(), sweepClock);
        stationary.Observe(Axes(), sweepClock.AddSeconds(8));
        Require(stationary.Result is not null && stationary.Result.DistinctValues == 1 &&
                stationary.Result.IntermediateSamples == 0, "idle signal is not full sweep");
        Require(stationary.Result.HighLimitInterpretation.Contains("No Windows WinMM high-end axis band samples observed") &&
                !stationary.Result.HighLimitInterpretation.Contains("high-limit samples") &&
                stationary.Result.HighLimitInterpretationFor(true).Contains("high-end axis band samples observed at the released end") &&
                stationary.Result.HighLimitInterpretation.Contains("does not establish sensor headroom"),
            "absence of WinMM saturation must not imply native sensor headroom");
        Console.WriteLine("PASS: no movement is not a valid full sweep");
        Console.WriteLine("PASS: additive WinMM high-limit interpretation in read-only sweep JSON");

        // Clock rollback must not create out-of-order samples or artificial spikes.
        // Equal-millisecond observations remain valid for coarse Windows clocks.
        var rollbackSweep = new InputSweepRun(0, 0, 65535, sweepClock, requireContinuousSamples: false);
        rollbackSweep.Observe(Axes(0, 99), sweepClock.AddMilliseconds(-40));
        rollbackSweep.Observe(Axes(0, 100), sweepClock);
        rollbackSweep.Observe(Axes(0, 200), sweepClock.AddMilliseconds(120));
        rollbackSweep.Observe(Axes(0, 9999), sweepClock.AddMilliseconds(80));
        rollbackSweep.Observe(Axes(0, 300), sweepClock.AddMilliseconds(120));
        rollbackSweep.Observe(Axes(0, 400), sweepClock.AddSeconds(8));
        Require(rollbackSweep.Result is { SampleCount: 4 } &&
                rollbackSweep.Result.Trace.Select(p => p.TimeMs)
                    .SequenceEqual(new long[] { 0, 120, 120, 8000 }) &&
                !rollbackSweep.Result.Trace.Any(p => p.WinmmValue == 9999),
            "clock rollback must not corrupt exported WinMM sweep timestamps or readings");
        Console.WriteLine("PASS: WinMM sweep clock-rollback timestamp integrity");

        // Live WinMM captures require continuous timing. Sparse endpoint-only
        // fixtures above explicitly opt out; they do not represent a valid sweep.
        var forwardGapSweep = new InputSweepRun(0, 0, 65535, sweepClock);
        forwardGapSweep.Observe(Axes(0, 0), sweepClock);
        forwardGapSweep.Observe(Axes(0, 40000), sweepClock.AddMilliseconds(650));
        Require(forwardGapSweep.Failed && !forwardGapSweep.Complete &&
                forwardGapSweep.Result is null &&
                forwardGapSweep.FailureReason.Contains("timing interrupted"),
            "live sweep must reject a forward clock jump or WinMM polling gap");
        forwardGapSweep.Observe(Axes(0, 0), sweepClock.AddSeconds(8));
        Require(forwardGapSweep.Result is null && !forwardGapSweep.Complete,
            "interrupted sweep must never resume into an exportable trace");

        var reverseTimeSweep = new InputSweepRun(0, 0, 65535, sweepClock);
        reverseTimeSweep.Observe(Axes(0, 0), sweepClock);
        reverseTimeSweep.Observe(Axes(0, 1000), sweepClock.AddMilliseconds(40));
        reverseTimeSweep.Observe(Axes(0, 50000), sweepClock.AddMilliseconds(20));
        Require(reverseTimeSweep.Failed && reverseTimeSweep.Result is null,
            "live sweep must reject retrograde Windows timestamps");

        var delayedFirstSweep = new InputSweepRun(0, 0, 65535, sweepClock);
        delayedFirstSweep.Observe(Axes(0, 0), sweepClock.AddMilliseconds(600));
        Require(delayedFirstSweep.Failed && delayedFirstSweep.Result is null,
            "live sweep must reject an initial sample delayed beyond 0.5 seconds");
        Console.WriteLine("PASS: interrupted live WinMM sweeps require recapture (synthetic C#)");

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
        // Cached/imported traces bypass the live sweep guard; direct assessment
        // must not turn impossible WinMM samples into a valid captured sweep.
        foreach (var invalid in new[]
        {
            new[] { new SweepPoint(0, 100u), new SweepPoint(100, 150u), new SweepPoint(200, 201u), new SweepPoint(300, 100u) },
            new[] { new SweepPoint(0, 100u), new SweepPoint(100, 99u), new SweepPoint(200, 180u), new SweepPoint(300, 100u) }
        })
        {
            var rejected = SweepDiagnostics.Evaluate(invalid, 100, 200);
            Require(rejected.Status == "Invalid WinMM range" &&
                    !rejected.CompletePressAndRelease && !rejected.ReachesReportedMaximum &&
                    rejected.Explanation.Contains("reported or assumed"),
                "direct WinMM sweep assessment must reject out-of-bounds cached traces");
        }
        Console.WriteLine("PASS: out-of-range cached WinMM sweeps rejected (synthetic C#)");

        var emptyTrace = new[] { new SweepPoint(0, 0u), new SweepPoint(8000, 0u) };
        Require(SweepDiagnostics.Evaluate(emptyTrace, 0, 65535).Status == "Insufficient data",
            "short trace rejects false validation");
        var idleTrace = Enumerable.Range(0, 30).Select(i => new SweepPoint(i*100, 0u)).ToArray();
        Require(SweepDiagnostics.Evaluate(idleTrace, 0, 65535).Status == "Little movement",
            "idle trace cannot be accepted as complete sweep");
        var missingRelease = Enumerable.Range(0, 30).Select(i => new SweepPoint(i*100, (uint)(65535 * i / 29))).ToArray();
        Require(SweepDiagnostics.Evaluate(missingRelease, 0, 65535).Status == "Incomplete sweep",
            "missing release fails the quality check");
        // Regressions: an early rest sample or a previous completed stroke
        // must not hide a pressed start or an unreleased final stroke.
        var startsPressed = new uint[] { 65535, 0, 7000, 16000, 32000, 53000, 65535 };
        var secondStrokeHeld = new uint[] { 0, 8000, 30000, 56000, 0, 9000, 35000, 65535 };
        var startsPartPressed = new uint[] { 32000, 0, 8000, 16000, 35000, 55000, 0 };
        foreach (var (name, values) in new[]
        {
            ("starts pressed, finishes pressed", startsPressed),
            ("second stroke not released", secondStrokeHeld),
            ("starts partially pressed", startsPartPressed)
        })
        {
            var trace = values.Select((value, index) => new SweepPoint(index * 100, value)).ToArray();
            var assessment = SweepDiagnostics.Evaluate(trace, 0, 65535);
            Require(!assessment.CompletePressAndRelease && assessment.Status == "Incomplete sweep",
                $"{name}: reject false complete WinMM press-and-release");
        }
        Console.WriteLine("PASS: ordered WinMM sweep start/end regression checks");

        // Two distinct press/release strokes in one imported WinMM trace must
        // not be reported as one completed guided sweep. Check both polarities
        // using synthetic C# data; no Windows controller or pedals are accessed.
        // The final two samples must be within the 3% released band;
        // otherwise a final zero could be a one-sample WinMM dropout.
        var doubleStrokeTrace = Enumerable.Range(0, 49).Select(i =>
        {
            double fraction = i <= 12 ? i / 12.0
                : i <= 24 ? (24 - i) / 12.0
                : i <= 36 ? (i - 24) / 12.0
                : i == 47 ? .02 : (48 - i) / 12.0;
            return new SweepPoint(i * 160L, (uint)Math.Round(65535.0 * fraction));
        }).ToArray();
        var doubleStrokeInverted = doubleStrokeTrace
            .Select(p => new SweepPoint(p.TimeMs, 65535u - p.WinmmValue)).ToArray();
        foreach (var assessment in new[]
        {
            SweepDiagnostics.Evaluate(doubleStrokeTrace, 0, 65535),
            SweepDiagnostics.Evaluate(doubleStrokeInverted, 0, 65535, inverted: true)
        })
        {
            Require(!assessment.CompletePressAndRelease &&
                    assessment.Status == "Multiple stroke reversals" &&
                    assessment.DirectionChanges >= 3 &&
                    assessment.Explanation.Contains("one slow press"),
                "two complete WinMM strokes must not be accepted as one guided sweep");
        }
        Console.WriteLine("PASS: repeated WinMM strokes rejected in both axis directions (synthetic C#)");


        // Imported/cached traces must not claim a completed physical sweep
        // based on millisecond-fast movement or a large missing sample window.
        // These are synthetic Windows WinMM samples, not actual pedal tests.
        var timingValues = Enumerable.Range(0, 25)
            .Select(i => i == 23 ? (uint)Math.Round(65535.0 * .02)
                : (uint)Math.Round(65535.0 * (i <= 12 ? i : 24 - i) / 12))
            .ToArray();
        var tooFastTrace = timingValues.Select((v, i) => new SweepPoint(i * 5L, v)).ToArray();
        var missingWindowTrace = timingValues.Select((v, i) =>
            new SweepPoint(i * 100L + (i > 12 ? 800L : 0L), v)).ToArray();
        var continuousTrace = timingValues.Select((v, i) => new SweepPoint(i * 320L, v)).ToArray();
        var tooFastAssessment = SweepDiagnostics.Evaluate(tooFastTrace, 0, 65535);
        var missingWindowAssessment = SweepDiagnostics.Evaluate(missingWindowTrace, 0, 65535);
        var continuousAssessment = SweepDiagnostics.Evaluate(continuousTrace, 0, 65535);
        Require(!tooFastAssessment.CompletePressAndRelease &&
                tooFastAssessment.Status == "Insufficient sweep timing",
            "millisecond-fast cached WinMM trace cannot be a completed sweep");
        Require(!missingWindowAssessment.CompletePressAndRelease &&
                missingWindowAssessment.Status == "Insufficient sweep timing",
            "cached WinMM trace with a >500 ms gap cannot be a completed sweep");
        Require(continuousAssessment.CompletePressAndRelease &&
                continuousAssessment.Status == "Sweep captured",
            "continuous eight-second WinMM synthetic trace remains accepted");
        Console.WriteLine("PASS: cached WinMM sweep timing quality checks (synthetic C#)");

        // Inverted WinMM axes rest at the high endpoint and press toward low.
        // Evaluate the same synthetic trace in both polarities to avoid silently
        // classifying a correct inverted pedal stroke as an incomplete sweep.
        var invertedTrace = Enumerable.Range(0, 41)
            .Select(i => new SweepPoint(i * 200L,
                (uint)Math.Round(65535.0 * (i == 39 ? .98 : Math.Abs(i - 20) / 20.0))))
            .ToArray();
        var invertedAssessment = SweepDiagnostics.Evaluate(invertedTrace, 0, 65535, inverted: true);
        var normalAssessment = SweepDiagnostics.Evaluate(invertedTrace, 0, 65535);
        Require(invertedAssessment.CompletePressAndRelease &&
                invertedAssessment.Status == "Sweep captured" &&
                invertedAssessment.ReachesReportedMaximum &&
                !normalAssessment.CompletePressAndRelease,
            "inverted WinMM sweep must use calibrated pedal direction while retaining raw endpoint semantics");
        Require(invertedAssessment.Explanation.Contains("released end of this inverted mapping") &&
                invertedAssessment.Explanation.Contains("does not establish pedal-force clipping"),
            "inverted WinMM raw high endpoint must be described as released, not pedal-force saturation");
        // Even a partial inverted press may report the exact raw maximum at rest.
        // Do not mistake that endpoint for proof of a full-force press.
        var partialInvertedTrace = Enumerable.Range(0, 41)
            .Select(i => new SweepPoint(i * 200L,
                (uint)Math.Round(10000.0 + 55535.0 * (i == 39 ? .98 : Math.Abs(i - 20) / 20.0))))
            .ToArray();
        var partialInvertedAssessment = SweepDiagnostics.Evaluate(
            partialInvertedTrace, 0, 65535, inverted: true);
        Require(partialInvertedAssessment.Status == "Sweep captured" &&
                partialInvertedAssessment.ReachesReportedMaximum &&
                partialInvertedAssessment.Explanation.Contains("released end of this inverted mapping"),
            "partial inverted press must not mislabel released WinMM high endpoint as pressed saturation");
        // An inverted pedal can rest just below the raw WinMM high endpoint.
        // A complete press/release still must not be described as a missing
        // physical pedal press or as a sensor headroom measurement.
        var belowHighInvertedTrace = Enumerable.Range(0, 41)
            .Select(i => new SweepPoint(i * 200L,
                (uint)Math.Round(65000.0 * (i == 39 ? .98 : Math.Abs(i - 20) / 20.0))))
            .ToArray();
        var belowHighInverted = SweepDiagnostics.Evaluate(
            belowHighInvertedTrace, 0, 65535, inverted: true);
        Require(belowHighInverted.CompletePressAndRelease &&
                belowHighInverted.Status == "Sweep captured" &&
                !belowHighInverted.ReachesReportedMaximum &&
                belowHighInverted.Explanation.Contains("high endpoint was not observed") &&
                belowHighInverted.Explanation.Contains("released end of this inverted mapping") &&
                belowHighInverted.Explanation.Contains("does not establish physical pedal force"),
            "inverted WinMM sweep below raw high endpoint needs correct released-end guidance");

        foreach (var (name, values) in new[]
        {
            ("starts pressed", new uint[] { 0, 65535, 52000, 44000, 32000, 18000, 0, 65535 }),
            ("ends pressed", new uint[] { 65535, 52000, 44000, 32000, 18000, 8000, 0 })
        })
        {
            var trace = values.Select((value, index) => new SweepPoint(index * 200L, value)).ToArray();
            var assessment = SweepDiagnostics.Evaluate(trace, 0, 65535, inverted: true);
            Require(!assessment.CompletePressAndRelease && assessment.Status == "Incomplete sweep",
                $"inverted WinMM sweep {name}: must reject missing released endpoint");
        }
        Console.WriteLine("PASS: inverted WinMM sweep orientation and incomplete-stroke regression");
        // Near-ceiling WinMM readings can enter the 0.2% high-end band without
        // ever observing the exact axis maximum. The JSON must distinguish them.
        var nearHighValues = Enumerable.Range(0, 16)
            .Select(index => (uint)Math.Round(65500.0 * index / 15))
            .Concat(Enumerable.Range(0, 16)
                .Select(index => index == 14 ? 1000u : (uint)Math.Round(65500.0 * (15 - index) / 15)))
            .ToArray();
        var nearHighTrace = nearHighValues
            .Select((value, index) => new SweepPoint(index * 260L, value)).ToArray();
        var nearHighAssessment = SweepDiagnostics.Evaluate(nearHighTrace, 0, 65535);
        Require(nearHighAssessment.CompletePressAndRelease &&
                nearHighAssessment.Status == "Sweep captured" &&
                !nearHighAssessment.ReachesReportedMaximum &&
                nearHighAssessment.Explanation.Contains("maximum was not reached"),
            "99.95% WinMM sweep must not claim exact Windows axis maximum");
        var nearHighRun = new InputSweepRun(0, 0, 65535, sweepClock);
        foreach (var point in nearHighTrace)
            nearHighRun.Observe(Axes(0, point.WinmmValue), sweepClock.AddMilliseconds(point.TimeMs));
        Require(nearHighRun.Complete && nearHighRun.Result is { AtHighLimitSamples: > 0 } nearHighResult &&
                nearHighResult.HighLimitInterpretation.Contains("high-end axis band") &&
                nearHighResult.HighLimitInterpretation.Contains("band counts alone"),
            "near-high WinMM band count must not claim an exact endpoint");
        var exactHighTrace = nearHighTrace
            .Select(point => point with { WinmmValue = point.WinmmValue == 65500u ? 65535u : point.WinmmValue })
            .ToArray();
        var exactHighAssessment = SweepDiagnostics.Evaluate(exactHighTrace, 0, 65535);
        Require(exactHighAssessment.CompletePressAndRelease &&
                exactHighAssessment.ReachesReportedMaximum &&
                exactHighAssessment.Explanation.Contains("Windows axis reached its limit"),
            "observed exact WinMM maximum remains recognized");
        Console.WriteLine("PASS: near-high band versus exact WinMM endpoint distinction");


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
        // Optional longer stationary recording remains a pure WinMM observation.
        var extended = new NoiseRun(fixedClock, NoiseRun.ExtendedDurationSeconds);
        for (int i = 0; i < 250; i++)
            extended.Observe(Axes(0, 42), fixedClock.AddMilliseconds(i * 40));
        Require(!extended.Complete && extended.RequestedDurationSeconds == 10,
            "long signal capture must not end before 10 seconds");
        extended.Observe(Axes(0, 42), fixedClock.AddSeconds(10));
        var extendedStats = extended.Analyze(0, 0, 65535);
        Require(extended.Complete && extendedStats.WindowSeconds == 10 &&
                extendedStats.SampleCount == 251 && extendedStats.SamplingRateHz == 25 &&
                extendedStats.Status == "No variation resolved",
            "10-second WinMM capture uses actual timestamps and unchanged diagnostics");
        foreach (var invalidDuration in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, 61.0 })
        {
            try
            {
                _ = new NoiseRun(fixedClock, invalidDuration);
                throw new Exception("FAILED: invalid signal-check duration accepted");
            }
            catch (ArgumentOutOfRangeException) { }
        }
        Console.WriteLine("PASS: optional 10s stationary WinMM capture and duration validation");
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
        // Eight identical values over 3 seconds cannot justify a quiet-signal
        // conclusion; 8 values over 1 second still exercise the existing
        // small-variation fixture above.
        var sparseIdle = SignalHealthDiagnostics.Evaluate(
            Enumerable.Repeat(0u, 8).ToArray(), 0, 65535, 3.0);
        Require(sparseIdle.SampleCount == 8 &&
                sparseIdle.SpanWinmm == 0 &&
                sparseIdle.SamplingRateHz == 2.33 &&
                sparseIdle.Status == "Insufficient samples" &&
                sparseIdle.Guidance.Contains("widely spaced"),
            "sparse idle WinMM samples must not be reported as resolved stability");
        var adequateIdle = SignalHealthDiagnostics.Evaluate(
            Enumerable.Repeat(0u, 16).ToArray(), 0, 65535, 3.0);
        Require(adequateIdle.SamplingRateHz == 5.0 &&
                adequateIdle.Status == "No variation resolved",
            "five-Hz minimum threshold is inclusive");
        var sparseMoving = SignalHealthDiagnostics.Evaluate(
            new uint[] { 0, 0, 0, 25000, 0, 0, 0, 0 }, 0, 65535, 3.0);
        Require(sparseMoving.Status == "Insufficient samples" &&
                sparseMoving.SpanWinmm == 25000 &&
                sparseMoving.LargeStepCount == 2,
            "sparse recordings must retain numerical motion evidence");
        // A burst of valid samples before and after a 1.4s polling outage
        // must not be labelled stable merely because the average rate is high.
        var gapClock = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
        var gappedRun = new NoiseRun(gapClock);
        for (int i = 0; i <= 20; i++)
            gappedRun.Observe(Axes(0, 0), gapClock.AddMilliseconds(i * 40));
        for (int i = 0; i <= 20; i++)
            gappedRun.Observe(Axes(0, 0), gapClock.AddMilliseconds(2200 + i * 40));
        var gappedHealth = gappedRun.Analyze(0, 0, 65535);
        Require(gappedRun.Complete && gappedHealth.SampleCount == 42 &&
                gappedHealth.SamplingRateHz > 5 &&
                gappedHealth.LargestSamplingGapSeconds == 1.4 &&
                gappedHealth.Status == "Insufficient samples" &&
                gappedHealth.SpanWinmm == 0,
            "long WinMM polling gap must override misleading average sample rate");
        var steadyRun = new NoiseRun(gapClock);
        for (int i = 0; i <= 75; i++)
            steadyRun.Observe(Axes(), gapClock.AddMilliseconds(i * 40));
        var steadyHealth = steadyRun.Analyze(0, 0, 65535);
        Require(steadyHealth.LargestSamplingGapSeconds == 0.04 &&
                steadyHealth.Status == "No variation resolved",
            "normal 40ms WinMM polling must remain acceptable");
        var firstSampleDelayed = new NoiseRun(gapClock);
        for (int i = 0; i <= 40; i++)
            firstSampleDelayed.Observe(Axes(), gapClock.AddMilliseconds(1400 + i * 40));
        Require(firstSampleDelayed.Analyze(0, 0, 65535).LargestSamplingGapSeconds == 1.4 &&
                firstSampleDelayed.Analyze(0, 0, 65535).Status == "Insufficient samples",
            "initial blind spot must also invalidate idle-signal quality");
        Require(SignalHealthDiagnostics.Evaluate(
                Enumerable.Repeat(0u, 40).ToArray(), 0, 65535, 3, 0.5).Status == "No variation resolved" &&
                SignalHealthDiagnostics.Evaluate(
                Enumerable.Repeat(0u, 40).ToArray(), 0, 65535, 3, 0.501).Status == "Insufficient samples",
            "half-second WinMM gap threshold must be inclusive");
        // A +0.6s wall-clock adjustment during a nominal 3s idle check can
        // prematurely finish a flat trace. Do not call it stable when the
        // timestamps show an implausible 0.64s interval between 40ms polls.
        var forwardJump = new NoiseRun(gapClock);
        for (int i = 0; i <= 60; i++)
            forwardJump.Observe(Axes(), gapClock.AddMilliseconds(i * 40));
        forwardJump.Observe(Axes(), gapClock.AddMilliseconds(3040));
        var forwardJumpStats = forwardJump.Analyze(0, 0, 65535);
        Require(forwardJump.Complete && forwardJumpStats.SampleCount == 62 &&
                forwardJumpStats.LargestSamplingGapSeconds == 0.64 &&
                forwardJumpStats.SpanWinmm == 0 &&
                forwardJumpStats.Status == "Insufficient samples" &&
                forwardJumpStats.Guidance.Contains("widely spaced"),
            "0.6s forward clock jump must not produce a valid-looking quiet WinMM report");
        Console.WriteLine("PASS: forward WinMM timestamp jump is inconclusive (synthetic)");
        // A wall-clock rollback during capture must never be treated as a
        // normal flat trace or as a positive polling gap.
        var rollbackRun = new NoiseRun(gapClock);
        for (int i = 0; i < 15; i++)
            rollbackRun.Observe(Axes(0, 0), gapClock.AddMilliseconds(i * 40));
        rollbackRun.Observe(Axes(0, 0), gapClock.AddMilliseconds(400));
        var rollbackHealth = rollbackRun.Analyze(0, 0, 65535);
        Require(rollbackRun.Complete && rollbackHealth.TimestampRegressionDetected &&
                rollbackHealth.Status == "Insufficient samples" &&
                rollbackHealth.SampleCount == 15 &&
                rollbackHealth.LargestSamplingGapSeconds == 0.04 &&
                rollbackHealth.Guidance.Contains("timestamps moved backwards"),
            "WinMM clock rollback must terminate capture and invalidate stability");
        rollbackRun.Observe(Axes(0, 5000), gapClock.AddSeconds(10));
        Require(rollbackRun.Analyze(0, 0, 65535).SampleCount == 15,
            "invalid WinMM capture must not accept further samples");
        var firstTimestampBeforeStart = new NoiseRun(gapClock);
        firstTimestampBeforeStart.Observe(Axes(), gapClock.AddMilliseconds(-10));
        Require(firstTimestampBeforeStart.Complete &&
                firstTimestampBeforeStart.Analyze(0, 0, 65535).Status == "Insufficient samples" &&
                firstTimestampBeforeStart.Analyze(0, 0, 65535).TimestampRegressionDetected,
            "clock rollback before first sample must not hang or look stable");
        Console.WriteLine("PASS: backward WinMM timestamps invalidate capture safely");
        Console.WriteLine("PASS: signal-health metrics, timing and conservative statuses");

        var caps = new WinmmJoystick.Device(3, "Example", new uint[6], new uint[6],
            0x045E, 0x1234);
        Require(caps.ManufacturerId == 0x045E && caps.ProductId == 0x1234 &&
                caps.WinmmIdsSummary.Contains("not verified USB VID/PID"),
            "WinMM capability IDs must not be mistaken for verified USB IDs");
        Require(Device.ManufacturerId is null && Device.ProductId is null &&
                Device.WinmmIdsSummary.Contains("unavailable"),
            "missing WinMM capabilities must not invent hardware identifiers");
        // Capability fallback limits must remain visibly distinguishable from
        // actual WinMM driver-reported ranges. Neither proves sensor ADC limits.
        var fallbackCaps = new WinmmJoystick.Device(4, "Synthetic fallback",
            new uint[6], Enumerable.Repeat(65535u, 6).ToArray(),
            0x1234, 0x5678, new[] { false, true, false, false, false, true });
        Require(fallbackCaps.WinmmIdsSummary.Contains("not verified USB VID/PID") &&
                fallbackCaps.WinmmIdsSummary.Contains("assumed WinMM axis range 0–65535") &&
                fallbackCaps.WinmmIdsSummary.EndsWith(": Y, V"),
            "only fallback WinMM axes must be named as assumed, not driver-reported");
        var reportedCaps = fallbackCaps with { AssumedAxisRanges = new bool[6] };
        Require(!reportedCaps.WinmmIdsSummary.Contains("assumed WinMM axis range"),
            "fully reported WinMM capability ranges must not be labelled fallback");
        Console.WriteLine("PASS: WinMM capability fallback provenance (synthetic C#)");
        Console.WriteLine("PASS: WinMM capability ID provenance");

        // Real Windows HID paths can include private instance/serial information.
        // The inventory must export only grouped VID/PID counts, never paths.
        var hid = HidInterfaceInventory.FromPaths(new[]
        {
            @"\\?\hid#vid_1234&pid_abcd&mi_00#PRIVATE_SERIAL_A#{GUID}",
            @"\\?\HID#VID_1234&PID_ABCD&COL01#PRIVATE_SERIAL_B#{GUID}",
            @"\\?\hid#VID_1A2B&PID_3C4D#PRIVATE_SERIAL_C#{GUID}",
            @"\\?\hid#col01#PRIVATE_SERIAL_D#{GUID}",
            @"\\?\hid#VID_123&PID_9999#PRIVATE_SERIAL_E#{GUID}"
        });
        Require(hid.Available && hid.InterfaceCount == 5 &&
                hid.WithoutVidPidCount == 2 && hid.Groups.Count == 2 &&
                hid.Groups.Single(g => g.VendorId == 0x1234).InterfaceCount == 2,
            "HID interface inventory aggregates identifiers without inventing a WinMM match");
        var hidJson = JsonSerializer.Serialize(hid);
        Require(hidJson.Contains("WINDOWS_SETUPAPI_HID_INTERFACE_METADATA_NOT_SENSOR_ADC") &&
                !hidJson.Contains("PRIVATE_SERIAL") && !hidJson.Contains("DevicePath"),
            "HID report must never contain private device instance paths");
        Require(!HidInterfaceInventory.TryParseVidPid(
                @"\\?\hid#notvid_1234&pid_abcd#PRIVATE_SERIAL", out _, out _) &&
                !HidInterfaceInventory.TryParseVidPid(
                @"\\?\hid#pid_abcd&vid_1234#PRIVATE_SERIAL", out _, out _) &&
                !HidInterfaceInventory.TryParseVidPid(
                @"\\?\hid#VID_1234&PID_ABCD", out _, out _),
            "invalid or truncated HID identifier patterns must not be accepted");
        var truncatedPath = HidInterfaceInventory.FromPaths(new[]
        {
            @"\\?\hid#VID_1234&PID_ABCD",
            @"\\?\hid#VID_1234&PID_ABCD#PRIVATE_SERIAL#{GUID}"
        });
        Require(truncatedPath.InterfaceCount == 2 &&
                truncatedPath.WithoutVidPidCount == 1 &&
                truncatedPath.Groups.Count == 1 &&
                truncatedPath.Groups[0].InterfaceCount == 1 &&
                !JsonSerializer.Serialize(truncatedPath).Contains("PRIVATE_SERIAL"),
            "truncated HID path must be unidentified; complete path must remain grouped");
        Console.WriteLine("PASS: read-only HID interface ID aggregation and path privacy");


        // Counts only: a changed group is not a proven physical controller identity.
        var nextHid = HidInterfaceInventory.FromPaths(new[]
        {
            @"\\?\hid#VID_1234&PID_ABCD#PRIVATE_SERIAL_F#{GUID}",
            @"\\?\hid#VID_1A2B&PID_3C4D#PRIVATE_SERIAL_G#{GUID}",
            @"\\?\hid#VID_1A2B&PID_3C4D#PRIVATE_SERIAL_H#{GUID}",
            @"\\?\hid#col01#PRIVATE_SERIAL_I#{GUID}"
        });
        var difference = HidInterfaceInventory.Compare(hid, nextHid);
        Require(difference.PreviousInterfaceCount == 5 &&
                difference.CurrentInterfaceCount == 4 &&
                difference.PreviousUnidentifiedCount == 2 &&
                difference.CurrentUnidentifiedCount == 1 &&
                difference.Changes.Count == 2 &&
                difference.Changes.Single(c => c.VendorId == 0x1234).Delta == -1 &&
                difference.Changes.Single(c => c.VendorId == 0x1A2B).Delta == 1 &&
                difference.Summary.Contains("not a verified MECA or WinMM match"),
            "count-only HID inventory comparison must not infer physical device identity");
        var differenceJson = JsonSerializer.Serialize(difference);
        Require(!differenceJson.Contains("PRIVATE_SERIAL") &&
                !differenceJson.Contains("DevicePath"),
            "comparison JSON must never export instance paths or serials");
        var equalCountsDifferentPaths = HidInterfaceInventory.FromPaths(new[]
        {
            @"\\?\hid#VID_1234&PID_ABCD#NEW_SERIAL_A#{GUID}",
            @"\\?\hid#VID_1234&PID_ABCD#NEW_SERIAL_B#{GUID}",
            @"\\?\hid#VID_1A2B&PID_3C4D#NEW_SERIAL_C#{GUID}",
            @"\\?\hid#col01#NEW_SERIAL_D#{GUID}",
            @"\\?\hid#VID_123&PID_9999#NEW_SERIAL_E#{GUID}"
        });
        var unchanged = HidInterfaceInventory.Compare(hid, equalCountsDifferentPaths);
        Require(unchanged.Changes.Count == 0 &&
                unchanged.Summary.Contains("individual interfaces may still have changed"),
            "equal VID/PID counts cannot prove the same physical device remains");
        try
        {
            HidInterfaceInventory.Compare(hid,
                new HidInventorySnapshot(false, "failed", 0, 0, Array.Empty<HidInterfaceGroup>()));
            throw new Exception("FAILED: incomplete scan accepted as valid comparison");
        }
        catch (ArgumentException) { }
        Console.WriteLine("PASS: HID count-difference diagnostics and privacy boundaries");

        // Inspect UI markup without requiring a Windows desktop or physical pedals.
        // The Windows CI job also compiles the real WPF application.
        var xaml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        // Read-only WinMM display is quantized/processed: the live bar cannot diagnose sensor faults.
        Require(xaml.Descendants().Any(e => e.Name.LocalName == "TextBlock" &&
                ((string?)e.Attribute("Text"))?.Contains(
                    "Visible steps or deadzones may reflect Windows input processing, not a sensor fault.") == true),
            "live WinMM test guidance must not equate displayed steps with physical sensor faults");
        Console.WriteLine("PASS: WinMM live-bar guidance avoids unsupported sensor fault claims (XAML source contract)");
        var advanced = xaml.Descendants().Single(e => e.Name.LocalName == "StackPanel" &&
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "AdvancedView"));
        var exportButton = advanced.Descendants().Single(e => e.Name.LocalName == "Button" &&
            (string?)e.Attribute("Content") == "Export read-only report (JSON)");
        Require((string?)exportButton.Attribute("Click") == "Export_Click" &&
                ((string?)exportButton.Attribute("ToolTip"))?.Contains("No controller settings are changed.") == true &&
                advanced.Descendants().Any(e => e.Name.LocalName == "Button" &&
                    (string?)e.Attribute("Click") == "ScanHid_Click"),
            "Advanced must offer read-only JSON export alongside the explicit HID scan");
        // Setup must visibly explain where the owner's HID scan and WinMM
        // diagnostics live, without suggesting direct sensor/ADC access.
        var advancedEntry = xaml.Descendants().Single(e => e.Name.LocalName == "Button" &&
            (string?)e.Attribute("Click") == "Advanced_Click");
        Require(((string?)advancedEntry.Attribute("Content"))?.Contains("Advanced diagnostics") == true &&
                xaml.Descendants().Any(e => e.Name.LocalName == "TextBlock" &&
                    (string?)e.Attribute("Text") == "WinMM axes · HID identifiers · read-only"),
            "setup must expose a clearly labelled, read-only HID/WinMM diagnostics entry");
        Console.WriteLine("PASS: Advanced diagnostics discoverability and read-only scope");
        Console.WriteLine("PASS: Advanced HID export control and read-only guidance");
        Require(xaml.Descendants().Any(e => e.Name.LocalName == "TextBlock" &&
                    (string?)e.Attribute("Text") == "WINMM CONTROLLER") &&
                xaml.Descendants().Any(e => e.Name.LocalName == "TextBlock" &&
                    ((string?)e.Attribute("Text"))?.Contains("Physical USB identity is not verified.") == true),
            "WinMM selection UI must not assert verified physical USB identity");
        Console.WriteLine("PASS: WinMM controller selection UI states USB identity is unverified");
        // The initial live capture readout must never imply native sensor ADC
        // access before the first WinMM sample updates its label.
        var liveReadout = xaml.Descendants().Single(e => e.Name.LocalName == "TextBlock" &&
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "LiveRaw"));
        Require(((string?)liveReadout.Attribute("Text"))?.Contains("WinMM reading:") == true &&
                ((string?)liveReadout.Attribute("Text"))?.Contains("not sensor ADC") == true,
            "initial live input label must identify WinMM and disclaim sensor ADC");
        Console.WriteLine("PASS: initial live pedal reading is labelled WinMM, not sensor ADC");

        // The visible analog dial must expose the current WinMM percentage
        // to assistive technology, not just a static pedal name. Binding to
        // LiveValue also propagates the interrupted/no-reading marker.
        // This is a markup/source contract, not a rendered WPF accessibility test.
        var livePercent = xaml.Descendants().Single(e => e.Name.LocalName == "TextBlock" &&
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "LiveValue"));
        Require((string?)livePercent.Attribute("Text") == "—",
            "live WinMM percentage must not initially imply a controller reading");
        foreach (string pedal in new[] { "Brake", "Throttle", "Clutch" })
        {
            var gauge = xaml.Descendants().Single(e => e.Name.LocalName == "Canvas" &&
                e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == pedal + "Icon"));
            string? accessibleName = (string?)gauge.Attribute("AutomationProperties.Name");
            Require(accessibleName is not null &&
                    accessibleName.Contains("ElementName=LiveValue", StringComparison.Ordinal) &&
                    accessibleName.Contains("StringFormat='" + pedal +
                        " WinMM input gauge: {0}'", StringComparison.Ordinal),
                pedal + " gauge accessibility name must track the live WinMM readout");
        }
        Console.WriteLine("PASS: analog gauge accessibility names track live WinMM values (XAML source contract)");

        // The sweep progress bar reports elapsed time, not pedal force.
        // Source contract only: actual screen-reader behavior requires a Windows GUI test.
        var sweepProgress = xaml.Descendants().Single(e => e.Name.LocalName == "ProgressBar" &&
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "SweepProgress"));
        Require((string?)sweepProgress.Attribute("AutomationProperties.Name") ==
                    "WinMM pedal sweep elapsed time" &&
                ((string?)sweepProgress.Attribute("AutomationProperties.HelpText"))?.Contains(
                    "elapsed time, not pedal force or raw sensor ADC.") == true &&
                ((string?)sweepProgress.Attribute("ToolTip"))?.Contains("elapsed time only") == true,
            "WinMM sweep progress must be accessible without implying a physical force reading");
        Console.WriteLine("PASS: WinMM sweep progress elapsed-time accessibility (XAML source contract)");

        // Setup copy must not imply that WinMM peak similarity measures
        // native load-cell force or physically verified repeatability.
        Require(xaml.Descendants().Any(e => e.Name.LocalName == "TextBlock" &&
                    ((string?)e.Attribute("Text"))?.Contains("compares Windows-reported pedal peaks") == true &&
                    ((string?)e.Attribute("Text"))?.Contains("Physical force is not measured") == true &&
                    ((string?)e.Attribute("Text"))?.Contains("No controller or Windows calibration settings are changed") == true &&
                    ((string?)e.Attribute("Text"))?.Contains("JSON export is optional") == true) &&
                !xaml.Descendants().Any(e => e.Name.LocalName == "TextBlock" &&
                    ((string?)e.Attribute("Text"))?.Contains("measures repeatability") == true),
            "setup must label WinMM peak comparisons without claiming physical force repeatability");
        Console.WriteLine("PASS: setup explains WinMM peak comparisons are not physical-force measurements");


        // A controller-only export is permitted but must ask for confirmation.
        // A failed HID scan is error metadata, not a successful inventory.
        // Completed HID scans and any WinMM measurements are meaningful exports.
        var failedHidScan = new HidInventorySnapshot(false, "HID scan unavailable", 0, 0,
            Array.Empty<HidInterfaceGroup>());
        Require(ReadOnlyHidReport.IsControllerOnlyExport(true, 0, false, false, 0, null) &&
                ReadOnlyHidReport.IsControllerOnlyExport(true, 0, false, false, 0, failedHidScan),
            "controller-only report must require confirmation after no HID scan or a failed scan");
        Require(!ReadOnlyHidReport.IsControllerOnlyExport(false, 0, false, false, 0, null) &&
                !ReadOnlyHidReport.IsControllerOnlyExport(true, 0, false, false, 0, hid) &&
                !ReadOnlyHidReport.IsControllerOnlyExport(true, 1, false, false, 0, null) &&
                !ReadOnlyHidReport.IsControllerOnlyExport(true, 0, true, false, 0, null) &&
                !ReadOnlyHidReport.IsControllerOnlyExport(true, 0, false, true, 0, null) &&
                !ReadOnlyHidReport.IsControllerOnlyExport(true, 0, false, false, 1, null),
            "HID or measured WinMM reports must not be mislabeled controller-only");

        // Hardware-free source-contract guard: after a successful pedal
        // recapture, old WinMM noise/health statistics must not be exported
        // under a potentially different axis mapping.
        var windowSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        // Read-only WinMM safety regression: a failed poll retains historical
        // measurements for review, but no results panel may display them as live.
        // This inspects WPF source; the Windows CI job compiles the real UI.
        int staleHelperStart = windowSource.IndexOf("private string MarkStaleForReview(", StringComparison.Ordinal);
        int staleHelperEnd = windowSource.IndexOf("private void ChangeStep(", staleHelperStart, StringComparison.Ordinal);
        int staleChangeStepEnd = windowSource.IndexOf("private PedalKind StepKind(", staleHelperEnd, StringComparison.Ordinal);
        int staleUnavailableStart = windowSource.IndexOf("private void MarkLiveReadingsUnavailable(", StringComparison.Ordinal);
        int staleUnavailableEnd = windowSource.IndexOf("private void UpdateDeviceInfo(", staleUnavailableStart, StringComparison.Ordinal);
        int staleSweepDetailsStart = windowSource.IndexOf("private void UpdateSweepDetails(", StringComparison.Ordinal);
        int staleSweepDetailsEnd = windowSource.IndexOf("private void DrawTrace(", staleSweepDetailsStart, StringComparison.Ordinal);
        Require(staleHelperStart >= 0 && staleHelperEnd > staleHelperStart &&
                staleChangeStepEnd > staleHelperEnd && staleUnavailableStart >= 0 &&
                staleUnavailableEnd > staleUnavailableStart &&
                staleSweepDetailsStart >= 0 && staleSweepDetailsEnd > staleSweepDetailsStart,
            "WPF must define the stale WinMM review marker and results handlers");
        string staleHelper = windowSource[staleHelperStart..staleHelperEnd];
        string staleStep = windowSource[staleHelperEnd..staleChangeStepEnd];
        string staleUnavailable = windowSource[staleUnavailableStart..staleUnavailableEnd];
        string staleSweepDetails = windowSource[staleSweepDetailsStart..staleSweepDetailsEnd];
        Require(staleHelper.Contains("_winmmSessionInvalid") &&
                staleHelper.Contains("STALE — WinMM session interrupted.") &&
                staleHelper.Contains("click Refresh and capture pedals again.") &&
                staleStep.Contains("SignalText.Text = MarkStaleForReview(") &&
                staleStep.Contains("SweepStatusText.Text = MarkStaleForReview(") &&
                staleStep.Contains("SummaryText.Text = MarkStaleForReview(") &&
                staleUnavailable.Contains("SummaryText.Text = MarkStaleForReview(") &&
                staleUnavailable.Contains("SweepAnalysisText.Text = MarkStaleForReview(") &&
                staleSweepDetails.Contains("SweepAnalysisText.Text = MarkStaleForReview("),
            "retained WinMM noise, sweep and guided results must be visibly stale after interruption");
        Console.WriteLine("PASS: stale WinMM review results labelled on interruption and view re-entry (WPF source contract)");

        Require(windowSource.Contains("WinmmJoystick.SelectSuggestedDevice(devices, previousId)") &&
                !windowSource.Contains("devices.FirstOrDefault(x => x.Name.Contains(\"MECA\""),
            "WPF refresh must use ambiguity-safe WinMM controller suggestion");
        Console.WriteLine("PASS: WPF refresh uses ambiguity-safe WinMM suggestion (source contract)");
        // WinMM idle diagnostics must expose sample count, median and largest
        // Windows-reported step in the GUI; these are not raw ADC or EMI proof.
        int formatNoiseStart = windowSource.IndexOf("private string FormatNoise(", StringComparison.Ordinal);
        int formatNoiseEnd = windowSource.IndexOf("private void Export_Click(", formatNoiseStart, StringComparison.Ordinal);
        Require(formatNoiseStart >= 0 && formatNoiseEnd > formatNoiseStart,
            "WinMM idle diagnostics formatter must exist");
        string formatNoiseSource = windowSource[formatNoiseStart..formatNoiseEnd];
        Require(formatNoiseSource.Contains("n={x.Value.SampleCount}") &&
                formatNoiseSource.Contains("median {x.Value.MedianWinmm:0.#}") &&
                formatNoiseSource.Contains("max step {x.Value.LargestStepWinmm}") &&
                formatNoiseSource.Contains("x.Value.SamplingRateHz:0.0"),
            "idle WinMM GUI must show count, median, largest step and polling rate");
        Console.WriteLine("PASS: WinMM idle signal statistics visible in GUI (WPF source contract)");
        // Guided step hints describe WinMM peak variation only; they do not
        // establish physical-force repeatability or load-cell health.
        Require(windowSource.Contains("StepHint.Text = ") &&
                windowSource.Contains("rest, three presses and WinMM peak variation") &&
                !windowSource.Contains("rest, three presses and repeatability"),
            "guided WinMM step must not label peak similarity as physical repeatability");
        Console.WriteLine("PASS: guided step hints describe WinMM peak variation (WPF source contract)");

        // JSON compatibility: the legacy AtAxisLimit flag is a near-endpoint
        // heuristic, not proof that an exact WinMM endpoint or sensor limit was hit.
        // WPF source contract is checked here; Windows CI compiles the exporter.
        Require(windowSource.Contains("NearWinmmLimitBandObserved = c.AtAxisLimit") &&
                windowSource.Contains("WinmmLimitBandInterpretation =") &&
                windowSource.Contains("within 0.2% of a reported or assumed WinMM axis endpoint") &&
                windowSource.Contains("exact endpoint, sensor ADC clipping and physical force remain unverified"),
            "WinMM JSON export must disclose near-limit heuristic without claiming sensor clipping");
        Console.WriteLine("PASS: WinMM JSON near-limit provenance (WPF source contract)");
        // A live out-of-range WinMM sample is invalid, not merely suspect.
        // The GUI must offer a safe recapture path without changing Windows settings.
        Require(windowSource.Contains("WinMM reading outside the reported or assumed axis range. Session invalid") &&
                windowSource.Contains("return to Connect and click Refresh, then re-capture."),
            "live WinMM out-of-range status must invalidate the session and explain safe retry");
        Console.WriteLine("PASS: invalid live WinMM range has actionable English guidance (WPF source contract)");
        // The diagnostics step displays WinMM joystick values, not uncalibrated
        // sensor ADC. Its heading and hint must not call the stream raw data.
        Require(windowSource.Contains("StepLabel.Text = \"WINMM / HID DIAGNOSTICS\";") &&
                windowSource.Contains("StepHint.Text = \"WinMM-reported values (not sensor ADC; may be calibrated by Windows)\";") &&
                !windowSource.Contains("StepLabel.Text = \"RAW MONITOR\";"),
            "advanced step must distinguish WinMM readings from native sensor ADC");
        Console.WriteLine("PASS: Advanced step clearly labels WinMM/HID data provenance");
        // Source-level WPF regression: disabling the optional clutch while
        // a capture is active must cancel it before a later Poll can save it.
        // The Windows build compiles the handler; this is not a hardware test.
        int clutchToggleStart = windowSource.IndexOf("private void ClutchCheck_Changed(", StringComparison.Ordinal);
        int clutchToggleEnd = windowSource.IndexOf("private void UpdateMappingLabels(", clutchToggleStart, StringComparison.Ordinal);
        Require(clutchToggleStart >= 0 && clutchToggleEnd > clutchToggleStart,
            "optional clutch selection handler must exist");
        string clutchHandlerSource = windowSource[clutchToggleStart..clutchToggleEnd];
        Require(clutchHandlerSource.Contains("_capture?.Kind == PedalKind.Clutch") &&
                clutchHandlerSource.Contains("_capture = null;") &&
                clutchHandlerSource.Contains("CaptureButton.IsEnabled = _device is not null && !_winmmSessionInvalid;") &&
                clutchHandlerSource.Contains("Clutch disabled · capture cancelled.") &&
                clutchHandlerSource.Contains("_mappings.Remove(PedalKind.Clutch);") &&
                clutchHandlerSource.Contains("_calibrationResults.Remove(PedalKind.Clutch);"),
            "disabling clutch must cancel active capture and remove old mapping/results");
        Console.WriteLine("PASS: disabling optional clutch cancels active WPF capture (source contract)");
        // Hardware-free source contract: a recoverable HID scan exception
        // must not crash the UI or leave a previous successful inventory
        // eligible for export as if it were freshly collected.
        int hidScanStart = windowSource.IndexOf("private void ScanHid_Click(", StringComparison.Ordinal);
        int hidScanEnd = windowSource.IndexOf("private void CheckSignal_Click(", hidScanStart, StringComparison.Ordinal);
        Require(hidScanStart >= 0 && hidScanEnd > hidScanStart,
            "read-only HID scan handler must exist");
        string hidScanSource = windowSource[hidScanStart..hidScanEnd];
        Require(hidScanSource.Contains("catch (Exception ex) when (ex is not OutOfMemoryException)") &&
                hidScanSource.Contains("_hidInventory = null;") &&
                hidScanSource.Contains("_hidInventoryComparison = null;") &&
                hidScanSource.Contains("No current HID inventory is available.") &&
                hidScanSource.Contains("Retry HID scan") &&
                hidScanSource.Contains("finally") &&
                hidScanSource.Contains("HidScanButton.IsEnabled = true;"),
            "unexpected HID scan failure must clear stale counts and offer safe retry");
        Console.WriteLine("PASS: unexpected HID scan failure invalidates stale metadata (WPF source contract)");

        Require(windowSource.Contains("WinMM controller selected · physical USB identity not verified") &&
                !windowSource.Contains("Device found · USB game controller"),
            "WinMM selection status must not claim physical USB identity");
        Require(windowSource.Contains("mapping.Full < mapping.Rest") &&
                windowSource.Contains("_mappings[p.Key].Full < _mappings[p.Key].Rest"),
            "WPF sweep display and JSON export must use the captured WinMM pedal polarity");
        int exportStart = windowSource.IndexOf("private void Export_Click(", StringComparison.Ordinal);
        int saveDialogStart = windowSource.IndexOf("var save = new SaveFileDialog", exportStart, StringComparison.Ordinal);
        Require(exportStart >= 0 && saveDialogStart > exportStart,
            "read-only JSON export handler and save dialog must exist");
        string exportPreamble = windowSource[exportStart..saveDialogStart];
        Require(exportPreamble.Contains("ReadOnlyHidReport.IsControllerOnlyExport(") &&
                exportPreamble.Contains("No pedal measurements or successful HID inventory are included.") &&
                exportPreamble.Contains("may include a failed HID scan status.") &&
                exportPreamble.Contains("MessageBoxButton.YesNo") &&
                exportPreamble.Contains("MessageBoxResult.No") &&
                exportPreamble.Contains("MessageBoxResult.Yes"),
            "controller-only exports must show an opt-in warning before choosing a file");
        Console.WriteLine("PASS: controller-only report warning contract");

        // WPF source-contract regression: two read-only WinMM captures must
        // never run together. In particular, pedal motion during a sweep must
        // not contaminate an at-rest noise measurement or erase saved results.
        int beginSweepStart = windowSource.IndexOf("private void BeginSweep(", StringComparison.Ordinal);
        int beginSweepEnd = windowSource.IndexOf("private void UpdateSweepDetails(", beginSweepStart, StringComparison.Ordinal);
        int beginSignalStart = windowSource.IndexOf("private void BeginSignalCheck(", StringComparison.Ordinal);
        int beginSignalEnd = windowSource.IndexOf("private string FormatNoise(", beginSignalStart, StringComparison.Ordinal);
        Require(beginSweepStart >= 0 && beginSweepEnd > beginSweepStart &&
                beginSignalStart >= 0 && beginSignalEnd > beginSignalStart,
            "WinMM sweep and signal-check handlers must exist");
        string beginSweepSource = windowSource[beginSweepStart..beginSweepEnd];
        string beginSignalSource = windowSource[beginSignalStart..beginSignalEnd];
        // Hardware-free WPF source contract: an unmapped WinMM controller
        // must not yield an empty idle check that bypasses the controller-only warning.
        int noMappingGuard = beginSignalSource.IndexOf("if (_mappings.Count == 0)", StringComparison.Ordinal);
        Require(noMappingGuard >= 0 &&
                noMappingGuard < beginSignalSource.IndexOf("_noise = new NoiseRun(", StringComparison.Ordinal) &&
                beginSignalSource.Contains("Capture at least one pedal before checking WinMM signal at rest.") &&
                exportPreamble.Contains("(_lastNoise?.Count ?? 0) > 0") &&
                exportPreamble.Contains("(_lastSignalHealth?.Count ?? 0) > 0"),
            "unmapped WinMM signal checks must not create empty measurements or suppress the controller-only export warning");
        Console.WriteLine("PASS: empty WinMM diagnostics cannot masquerade as measured pedal data (WPF source contract)");
        const string activeWinmmGuard = "if (_capture is not null || _noise is not null || _sweep is not null)";
        Require(beginSweepSource.Contains(activeWinmmGuard) &&
                beginSweepSource.IndexOf(activeWinmmGuard, StringComparison.Ordinal) <
                    beginSweepSource.IndexOf("_sweepResults.Remove(kind)", StringComparison.Ordinal) &&
                beginSweepSource.Contains("Finish or cancel it before starting a sweep.") &&
                beginSignalSource.Contains(activeWinmmGuard) &&
                beginSignalSource.IndexOf(activeWinmmGuard, StringComparison.Ordinal) <
                    beginSignalSource.IndexOf("_noise = new NoiseRun(", StringComparison.Ordinal) &&
                beginSignalSource.Contains("Finish or cancel it before checking signal at rest."),
            "overlapping WinMM captures must be rejected before prior measurements are cleared");
        Console.WriteLine("PASS: overlapping WinMM sweep/noise capture blocked (WPF source contract)");

        // An in-progress WinMM capture/noise/sweep cannot be mixed into an
        // apparently complete JSON export. Pure C# policy and WPF source contract.
        Require(!ReadOnlyHidReport.CanExport(true, null, false, true) &&
                !ReadOnlyHidReport.CanExport(false, hid, false, true) &&
                ReadOnlyHidReport.CanExport(true, null, false, false) &&
                ReadOnlyHidReport.CanExport(false, hid, false, false),
            "active measurement must block both export paths without blocking completed reports");
        Require(exportPreamble.Contains("bool measurementInProgress = _capture is not null || _noise is not null || _sweep is not null;") &&
                exportPreamble.Contains("ReadOnlyHidReport.CanExport(_device is not null, _hidInventory, _winmmSessionInvalid,") &&
                exportPreamble.Contains("measurementInProgress))") &&
                exportPreamble.Contains("Wait for it to finish before exporting."),
            "WPF export must enforce active WinMM measurement guard with clear English guidance");
        Console.WriteLine("PASS: active WinMM measurement blocks report export (C# policy + WPF source contract)");

        // Source-level contract for the WPF WinMM+HID JSON path. The HID-only
        // serializer already emits these fields; both export paths must state
        // that matching identifiers do not prove device identity or raw ADC.
        int winmmExportStart = windowSource.IndexOf("else\n                report = new", exportStart, StringComparison.Ordinal);
        int reportWriteStart = windowSource.IndexOf("File.WriteAllText(save.FileName", winmmExportStart, StringComparison.Ordinal);
        Require(winmmExportStart >= 0 && reportWriteStart > winmmExportStart,
            "WinMM JSON export object must exist");
        string winmmExportObject = windowSource[winmmExportStart..reportWriteStart];
        Require(winmmExportObject.Contains("WinmmAssociationVerified = false,") &&
                winmmExportObject.Contains("UncalibratedSensorInputVerified = false,"),
            "WinMM+HID JSON must explicitly disavow verified device identity and raw ADC access");
        Console.WriteLine("PASS: WinMM+HID JSON top-level provenance flags");

        int recaptureStart = windowSource.IndexOf("if (guide.Result is { } result)", StringComparison.Ordinal);
        Require(recaptureStart >= 0, "successful guided-capture branch must exist");
        int recaptureEnd = windowSource.IndexOf("\n                else", recaptureStart, StringComparison.Ordinal);
        Require(recaptureEnd > recaptureStart, "successful guided-capture branch must be bounded");
        string recaptureBranch = windowSource[recaptureStart..recaptureEnd];
        Require(recaptureBranch.Contains("_sweepResults.Remove(guide.Kind)") &&
                recaptureBranch.Contains("_lastNoise = null;") &&
                recaptureBranch.Contains("_lastSignalHealth = null;"),
            "successful pedal recapture must invalidate old sweep, noise and signal-health results");
        Console.WriteLine("PASS: guided recapture invalidates stale WinMM diagnostics cache");

        // Hardware-free WPF source contract: disabling an optional clutch
        // must not leave its previously sampled WinMM noise/health results
        // eligible for export alongside the remaining brake/throttle pedals.
        int clutchHandlerStart = windowSource.IndexOf("private void ClutchCheck_Changed(", StringComparison.Ordinal);
        int clutchHandlerEnd = windowSource.IndexOf("private void UpdateMappingLabels(", clutchHandlerStart, StringComparison.Ordinal);
        Require(clutchHandlerStart >= 0 && clutchHandlerEnd > clutchHandlerStart,
            "optional clutch selection handler must exist");
        string clutchHandler = windowSource[clutchHandlerStart..clutchHandlerEnd];
        Require(clutchHandler.Contains("_mappings.Remove(PedalKind.Clutch)") &&
                clutchHandler.Contains("_calibrationResults.Remove(PedalKind.Clutch)") &&
                clutchHandler.Contains("_lastNoise = null;") &&
                clutchHandler.Contains("_lastSignalHealth = null;") &&
                clutchHandler.Contains("run signal check again."),
            "disabling clutch must invalidate prior signal snapshots and explain the next step");
        // Enabling the optional clutch also changes the expected channel set.
        // The completed WinMM noise/health cache must be cleared before the
        // disabled-only cleanup branch, so both checkbox directions are safe.
        int completedSignalReset = clutchHandler.IndexOf(
            "if (_lastNoise is not null || _lastSignalHealth is not null)", StringComparison.Ordinal);
        int disabledClutchBranch = clutchHandler.IndexOf(
            "if (ClutchCheck.IsChecked != true)", StringComparison.Ordinal);
        Require(completedSignalReset >= 0 && disabledClutchBranch > completedSignalReset &&
                clutchHandler[completedSignalReset..disabledClutchBranch].Contains("_lastNoise = null;") &&
                clutchHandler[completedSignalReset..disabledClutchBranch].Contains("_lastSignalHealth = null;") &&
                clutchHandler[completedSignalReset..disabledClutchBranch].Contains("run signal check again."),
            "enabling or disabling optional clutch must invalidate completed WinMM signal snapshots");
        Console.WriteLine("PASS: both optional clutch selection directions invalidate completed WinMM signal snapshots (WPF source contract)");
        // WPF is source-checked here; the runtime event path still needs physical
        // GUI validation. An orphan clutch sweep would cause JSON export to
        // index a missing pedal mapping and fail instead of saving the report.
        Require(clutchHandler.Contains("_sweepResults.Remove(PedalKind.Clutch);"),
            "disabling clutch must remove its cached sweep before JSON export");
        Console.WriteLine("PASS: optional clutch removal clears orphan sweep (WPF source contract)");
        // Third-pedal sweep wiring: XAML/source checks are not a physical GUI test.
        var clutchSweepButton = xaml.Descendants().Single(e => e.Name.LocalName == "Button" &&
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "ClutchSweepButton"));
        var clutchSweepLine = xaml.Descendants().Single(e => e.Name.LocalName == "Polyline" &&
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "ClutchSweepLine"));
        Require((string?)clutchSweepButton.Attribute("Click") == "ClutchSweep_Click" &&
                (string?)clutchSweepButton.Attribute("Visibility") == "Collapsed" &&
                (string?)clutchSweepLine.Attribute("Visibility") == "Collapsed" &&
                windowSource.Contains("BeginSweep(PedalKind.Clutch)") &&
                windowSource.Contains("DrawTrace(PedalKind.Clutch, ClutchSweepLine)") &&
                clutchHandler.Contains("_sweepKind == PedalKind.Clutch") &&
                clutchHandler.Contains("_sweep = null;") &&
                clutchHandler.Contains("_sweepResults.Remove(PedalKind.Clutch);") &&
                clutchHandler.Contains("No clutch sweep saved."),
            "optional clutch sweep must be hidden until enabled, charted and cancelled safely");
        Console.WriteLine("PASS: optional clutch sweep WPF wiring (source contract, not physical test)");
        var clutchSweepStart = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        var clutchSweep = new InputSweepRun(2, 0, 65535, clutchSweepStart);
        for (int i = 0; i <= 40; i++)
        {
            uint reading = (uint)Math.Round(65535.0 * (i == 39 ? .98 : Math.Abs(i - 20) / 20.0));
            clutchSweep.Observe(Axes(2, reading), clutchSweepStart.AddMilliseconds(i * 200));
        }
        Require(clutchSweep.Complete && clutchSweep.Result is { } clutchResult &&
                SweepDiagnostics.Evaluate(clutchResult.Trace, 0, 65535, inverted: true)
                    .Status == "Sweep captured",
            "synthetic inverted clutch sweep uses the same read-only WinMM evaluation");
        Console.WriteLine("PASS: synthetic inverted clutch sweep (not a physical pedal test)");
        Console.WriteLine("PASS: disabling clutch invalidates cached WinMM signal diagnostics");

        // Hardware-free C# regression: a failed WinMM read must prevent stale
        // WinMM exports even if an unrelated HID inventory is available.
        Require(ReadOnlyHidReport.CanExport(true, null) &&
                !ReadOnlyHidReport.CanExport(true, null, true) &&
                !ReadOnlyHidReport.CanExport(true, hid, true) &&
                ReadOnlyHidReport.CanExport(false, hid, true) &&
                !ReadOnlyHidReport.CanExport(false, null, true),
            "invalid WinMM session must block exports; HID-only remains available");

        // WPF is not executed in this test project. Guard the source path and
        // rely on Windows CI for WPF compilation; physical device tests remain separate.
        int pollStart = windowSource.IndexOf("private void Poll(", StringComparison.Ordinal);
        int pollEnd = windowSource.IndexOf("private double GetPercent(", pollStart, StringComparison.Ordinal);
        Require(pollStart >= 0 && pollEnd > pollStart, "WinMM poll source must exist");
        string pollSource = windowSource[pollStart..pollEnd];
        Require(pollSource.IndexOf("if (_winmmSessionInvalid)", StringComparison.Ordinal) <
                pollSource.IndexOf("WinmmJoystick.TryRead(", StringComparison.Ordinal) &&
                pollSource.Contains("_winmmSessionInvalid = true;") &&
                pollSource.Contains("_raw = Array.Empty<uint>();"),
            "WinMM poll must latch read failure and refuse silent same-ID recovery");
        Require(windowSource.Contains("ReadOnlyHidReport.CanExport(_device is not null, _hidInventory, _winmmSessionInvalid,") &&
                windowSource.Contains("private void Refresh_Click(") &&
                windowSource.Contains("StepHint.Text = requireExplicitSelection") &&
                windowSource.Contains("Controller refreshed · capture pedals again") &&
                windowSource.Contains("select controller explicitly, then capture pedals again") &&
                windowSource.Contains("RefreshDevices(requireExplicitSelection);") &&
                windowSource.Contains("_mappings.Clear();") &&
                windowSource.Contains("_calibrationResults.Clear();") &&
                windowSource.Contains("_sweepResults.Clear();"),
            "WPF export must respect session invalidation and explicit refresh must clear captures");
        Console.WriteLine("PASS: interrupted WinMM session blocks stale exports (C# logic + WPF source contract)");

        // A failed final WinMM read in Export_Click occurs outside the normal
        // Poll path. It must disable every WinMM measurement button immediately,
        // rather than leaving enabled controls that only reject clicks later.
        // This is a WPF source contract; it does not execute the GUI or pedals.
        int finalExportReadStart = windowSource.IndexOf(
            "if (_device is { } exportDevice &&", exportStart, StringComparison.Ordinal);
        int finalExportReadEnd = windowSource.IndexOf(
            "        try\n        {\n            // Validate optional HID metadata",
            finalExportReadStart, StringComparison.Ordinal);
        Require(finalExportReadStart > exportStart && finalExportReadEnd > finalExportReadStart,
            "final read-only WinMM export check must exist");
        string finalExportRead = windowSource[finalExportReadStart..finalExportReadEnd];
        Require(finalExportRead.Contains("_winmmSessionInvalid = true;") &&
                finalExportRead.Contains("MarkLiveReadingsUnavailable();") &&
                new[] { "CaptureButton", "CheckSignalButton", "LongSignalButton",
                        "BrakeSweepButton", "ThrottleSweepButton", "ClutchSweepButton" }
                    .All(button => finalExportRead.Contains(button + ".IsEnabled = false;")),
            "failed final WinMM export read must visibly disable all measurement controls");
        Console.WriteLine("PASS: final export read failure disables WinMM controls (WPF source contract)");
        // WPF source-path contract: a valid-looking mapped axis must not
        // conceal an out-of-range reading on a different WinMM axis.
        // The UI must stop all in-flight measurements and block JSON export.
        int boundsGuardStart = pollSource.IndexOf(
            "if (!WinmmJoystick.IsSampleWithinAxisBounds(_device, sample))",
            StringComparison.Ordinal);
        int boundsGuardEnd = pollSource.IndexOf(
            "StatusText.Text = \"Input streaming · read-only\";", boundsGuardStart,
            StringComparison.Ordinal);
        Require(boundsGuardStart >= 0 && boundsGuardEnd > boundsGuardStart,
            "live WinMM polling must guard the entire sample before consuming it");
        string boundsGuard = pollSource[boundsGuardStart..boundsGuardEnd];
        Require(boundsGuard.Contains("_winmmSessionInvalid = true;") &&
                boundsGuard.Contains("_raw = Array.Empty<uint>();") &&
                boundsGuard.Contains("_capture = null;") &&
                boundsGuard.Contains("_noise = null;") &&
                boundsGuard.Contains("_sweep = null;") &&
                boundsGuard.Contains("MarkLiveReadingsUnavailable();") &&
                boundsGuard.Contains("return;") &&
                boundsGuardEnd < pollSource.IndexOf("guide.Observe(sample,", StringComparison.Ordinal),
            "out-of-range WinMM sample must latch invalid session before guided capture");
        Console.WriteLine("PASS: out-of-range WinMM sample blocks stale WPF exports (source contract)");

        // A failed synchronous read from the Capture button occurs outside Poll.
        // It must latch the same session-invalid flag before a user can export
        // earlier WinMM measurements. This is a source contract, not a WPF test.
        int captureStart = windowSource.IndexOf("private void Capture_Click(", StringComparison.Ordinal);
        int captureEnd = windowSource.IndexOf("private void Poll(", captureStart, StringComparison.Ordinal);
        Require(captureStart >= 0 && captureEnd > captureStart,
            "guided WinMM capture handler must exist");
        string captureSource = windowSource[captureStart..captureEnd];
        int captureRead = captureSource.IndexOf("if (!WinmmJoystick.TryRead(", StringComparison.Ordinal);
        // Defense-in-depth: the capture button must not start a guided pedal
        // read while another WinMM measurement is active, even if invoked
        // outside the normal WPF navigation flow. Source contract only.
        int captureOverlapGuard = captureSource.IndexOf(activeWinmmGuard, StringComparison.Ordinal);
        Require(captureOverlapGuard >= 0 && captureRead > captureOverlapGuard &&
                captureSource.Contains("Finish or cancel it before capturing a pedal.") &&
                captureSource.IndexOf("_capture = new GuidedCalibration(", StringComparison.Ordinal) > captureOverlapGuard,
            "guided capture must reject overlapping WinMM measurements before reading or changing state");
        Console.WriteLine("PASS: guided WinMM capture overlap rejected (WPF source contract)");
        int invalidateRead = captureSource.IndexOf("_winmmSessionInvalid = true;", StringComparison.Ordinal);
        Require(captureRead >= 0 && invalidateRead > captureRead &&
                captureSource.Contains("_raw = Array.Empty<uint>();") &&
                captureSource.Contains("_capture = null;") &&
                captureSource.Contains("click Refresh; capture pedals again."),
            "failed guided-capture WinMM read must immediately invalidate the session");
        Console.WriteLine("PASS: guided-capture read failure latches WinMM session invalid (WPF source contract)");
        Require(pollSource.Contains("if (_sweep.Failed)") &&
                pollSource.Contains("if (_sweep.Failed || _sweep.Complete)") &&
                pollSource.Contains("SweepStatusText.Text = $\"{sweepPedal}: {_sweep.FailureReason}\";") &&
                pollSource.Contains("SweepProgress.Value = 0;"),
            "WPF must cancel an interrupted WinMM sweep and display a retry message");
        Console.WriteLine("PASS: interrupted sweep WPF cancellation contract (source check)");

        // Refresh may be pressed while a capture, idle-noise check or sweep is
        // still active. Even if the same WinMM ID is selected afterward, none
        // of those old-session measurements may finish into the new report.
        int refreshStart = windowSource.IndexOf("private void Refresh_Click(", StringComparison.Ordinal);
        int refreshEnd = windowSource.IndexOf("private void DeviceCombo_SelectionChanged(", refreshStart, StringComparison.Ordinal);
        Require(refreshStart >= 0 && refreshEnd > refreshStart,
            "explicit WinMM refresh handler must exist");
        string refreshSource = windowSource[refreshStart..refreshEnd];
        foreach (string cancelled in new[] { "_capture = null;", "_noise = null;", "_sweep = null;", "_sweepKind = null;" })
            Require(refreshSource.Contains(cancelled),
                $"WinMM refresh must cancel old-session activity: {cancelled}");
        Require(refreshSource.IndexOf("_sweep = null;", StringComparison.Ordinal) <
                refreshSource.IndexOf("RefreshDevices(requireExplicitSelection);", StringComparison.Ordinal) &&
                refreshSource.Contains("CheckSignalButton.IsEnabled = true;") &&
                refreshSource.Contains("LongSignalButton.IsEnabled = true;") &&
                refreshSource.Contains("BrakeSweepButton.IsEnabled = true;") &&
                refreshSource.Contains("ThrottleSweepButton.IsEnabled = true;") &&
                refreshSource.Contains("Controller refreshed · run signal check again.") &&
                refreshSource.Contains("Controller refreshed · record a new sweep."),
            "WinMM refresh must restore controls and explicitly require fresh diagnostics");
        Console.WriteLine("PASS: explicit WinMM refresh cancels in-flight captures (WPF source contract)");

        // Hardware-free WPF source contract: a failed WinMM read must not leave
        // a previous pedal percentage or gauge visible as if it were live.
        // Windows CI compiles the actual WPF code; physical pedals are not tested.
        int unavailableStart = windowSource.IndexOf(
            "private void MarkLiveReadingsUnavailable(", StringComparison.Ordinal);
        int unavailableEnd = windowSource.IndexOf(
            "private void UpdateDeviceInfo(", unavailableStart, StringComparison.Ordinal);
        Require(unavailableStart >= 0 && unavailableEnd > unavailableStart,
            "WinMM live-display invalidation helper must exist");
        string unavailableSource = windowSource[unavailableStart..unavailableEnd];
        Require(unavailableSource.Contains("LiveValue.Text = \"—\";") &&
                unavailableSource.Contains("LiveRaw.Text = \"WinMM input interrupted") &&
                unavailableSource.Contains("CaptureProgress.Value = 0;") &&
                unavailableSource.Contains("BrakeProgress.Value = 0;") &&
                unavailableSource.Contains("ThrottleProgress.Value = 0;") &&
                unavailableSource.Contains("ClutchProgress.Value = 0;") &&
                unavailableSource.Contains("BrakeValue.Text = \"No live input\";") &&
                unavailableSource.Contains("ThrottleValue.Text = \"No live input\";") &&
                unavailableSource.Contains("ClutchValue.Text = \"No live input\";") &&
                unavailableSource.Contains("UpdateMappingLabels();") &&
                windowSource.Contains("STALE — refresh required") &&
                pollSource.Contains("MarkLiveReadingsUnavailable();") &&
                captureSource.Contains("MarkLiveReadingsUnavailable();"),
            "failed WinMM reads must clear stale live gauges and label saved mappings stale");
        Console.WriteLine("PASS: interrupted WinMM stream clears stale live UI (WPF source contract)");

        // Source contract for the WPF-only WinMM JSON export path.
        // The Windows CI job compiles WPF; this synthetic test does not use pedals.
        int axisExportStart = windowSource.IndexOf("private void Export_Click(", StringComparison.Ordinal);
        Require(axisExportStart >= 0, "WinMM JSON export handler must exist");
        string axisExportSource = windowSource[axisExportStart..];
        Require(axisExportSource.Contains("WinmmAxisRanges = Enumerable.Range(0, 6)", StringComparison.Ordinal) &&
                axisExportSource.Contains("MinimumWinmm = _device.Min[i]", StringComparison.Ordinal) &&
                axisExportSource.Contains("MaximumWinmm = _device.Max[i]", StringComparison.Ordinal) &&
                axisExportSource.Contains("ASSUMED_FALLBACK_NOT_REPORTED", StringComparison.Ordinal) &&
                axisExportSource.Contains("WINMM_JOYGETDEVCAPSW", StringComparison.Ordinal) &&
                axisExportSource.Contains("UNKNOWN_NOT_VERIFIED", StringComparison.Ordinal),
            "WinMM JSON must preserve six axis bounds and distinguish assumed from reported ranges");
        Console.WriteLine("PASS: WinMM JSON axis-range provenance (WPF source contract)");

        // Hardware-free WPF source contract: an out-of-range WinMM axis must
        // not be rendered as a normal-looking live pedal percentage.
        // Windows CI compiles the WPF application; this does not test hardware.
        int testBarStart = windowSource.IndexOf("private void UpdateTestBar(", StringComparison.Ordinal);
        int testBarEnd = windowSource.IndexOf("private void BrakeSweep_Click(", testBarStart, StringComparison.Ordinal);
        Require(testBarStart >= 0 && testBarEnd > testBarStart,
            "WinMM live test-bar handler must exist");
        string testBarSource = windowSource[testBarStart..testBarEnd];
        int invalidBarGuard = testBarSource.IndexOf(
            "if (_raw[axis] < _device.Min[axis] || _raw[axis] > _device.Max[axis])",
            StringComparison.Ordinal);
        int percentRead = testBarSource.IndexOf("var percent = GetPercent(kind);", StringComparison.Ordinal);
        Require(invalidBarGuard >= 0 && percentRead > invalidBarGuard &&
                testBarSource.Contains("value.Text = \"Invalid WinMM reading\";") &&
                testBarSource.Contains("bar.Value = 0;") &&
                testBarSource.Contains("value.Text = \"No live input\";"),
            "out-of-range WinMM live values must not appear as valid pedal percentages");
        Console.WriteLine("PASS: out-of-range WinMM live test bars suppressed (WPF source contract)");

        // The guided capture pages (steps 1-3) also render a live gauge.
        // Out-of-range values must not appear as normal pedal percentages there.
        // This is a hardware-free source contract; Windows CI compiles WPF.
        // Scope this source contract to Poll, not an earlier capture-view stale handler.
        int pollUiStart = windowSource.IndexOf("private void Poll(", StringComparison.Ordinal);
        int guidedUiStart = windowSource.IndexOf("if (_step is >= 1 and <= 3)",
            pollUiStart, StringComparison.Ordinal);
        int guidedUiEnd = windowSource.IndexOf("else if (_step == 4)", guidedUiStart, StringComparison.Ordinal);
        Require(pollUiStart >= 0 && guidedUiStart > pollUiStart && guidedUiEnd > guidedUiStart,
            "guided WinMM preview UI branch must exist");
        string guidedUiSource = windowSource[guidedUiStart..guidedUiEnd];
        int guidedInvalidGuard = guidedUiSource.IndexOf(
            "bool invalidWinmmReading = shownAxis >= 0 &&", StringComparison.Ordinal);
        int guidedPercent = guidedUiSource.IndexOf(
            "var percent = invalidWinmmReading ? 0", StringComparison.Ordinal);
        Require(guidedInvalidGuard >= 0 && guidedPercent > guidedInvalidGuard &&
                guidedUiSource.Contains("sample[shownAxis] < _device.Min[shownAxis]") &&
                guidedUiSource.Contains("sample[shownAxis] > _device.Max[shownAxis]") &&
                guidedUiSource.Contains("LiveValue.Text = invalidWinmmReading ? \"Invalid WinMM reading\"") &&
                guidedUiSource.Contains("CaptureProgress.Value = percent;") &&
                guidedUiSource.Contains("OUTSIDE WINMM AXIS RANGE"),
            "guided preview must suppress out-of-range WinMM percentages");
        Console.WriteLine("PASS: guided WinMM preview suppresses invalid live values (WPF source contract)");

        // Hardware-free WPF source contract for the brake's white-faced
        // analog WinMM gauge. This does not render the window or test pedals.
        var brakeDial = xaml.Descendants().Single(e => e.Name.LocalName == "Canvas" &&
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "BrakeIcon"));
        Require(brakeDial.Elements().Any(e => e.Name.LocalName == "Ellipse" &&
                    (string?)e.Attribute("Fill") == "#F6F8F8") &&
                new[] { "0", "50", "100" }.All(label =>
                    brakeDial.Elements().Any(e => e.Name.LocalName == "TextBlock" &&
                        (string?)e.Attribute("Text") == label &&
                        (string?)e.Attribute("Foreground") == "#152027")) &&
                brakeDial.Descendants().Any(e => e.Name.LocalName == "RotateTransform" &&
                    e.Attributes().Any(a => a.Name.LocalName == "Name" &&
                        a.Value == "BrakeNeedleRotation")) &&
                guidedUiSource.Contains("BrakeNeedleRotation.Angle = -5 + (percent * 1.1);") &&
                windowSource.Split("BrakeNeedleRotation.Angle = -5;").Length - 1 == 3 &&
                !windowSource.Contains("BrakeCaliper.Opacity = 1.0 - percent"),
            "brake WinMM analog dial, needle update and stale-read reset must stay wired");
        Console.WriteLine("PASS: white-faced brake WinMM analog gauge (WPF source contract, no hardware)");

        Console.WriteLine("ALL CHECKS PASSED");
        return 0;
    }
}
