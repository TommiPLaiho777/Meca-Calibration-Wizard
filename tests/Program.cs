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
        Require(sweep.Result.HighLimitInterpretation.Contains("WinMM high axis limit observed") &&
                sweep.Result.HighLimitInterpretation.Contains("does not verify sensor ADC clipping"),
            "WinMM axis saturation must not be presented as sensor clipping");
        var sweepJson = JsonSerializer.Serialize(sweep.Result);
        Require(sweepJson.Contains("\"HighLimitInterpretation\"") &&
                sweepJson.Contains("does not verify sensor ADC clipping") &&
                !sweepJson.Contains("sensor clipped"),
            "serialized sweep must explain the high-limit provenance");
        Console.WriteLine("PASS: 8s progressive sweep and clipping statistics");

        var stationary = new InputSweepRun(0, 0, 65535, sweepClock);
        stationary.Observe(Axes(), sweepClock);
        stationary.Observe(Axes(), sweepClock.AddSeconds(8));
        Require(stationary.Result is not null && stationary.Result.DistinctValues == 1 &&
                stationary.Result.IntermediateSamples == 0, "idle signal is not full sweep");
        Require(stationary.Result.HighLimitInterpretation.Contains("No Windows WinMM high-limit samples observed") &&
                stationary.Result.HighLimitInterpretation.Contains("does not establish sensor headroom"),
            "absence of WinMM saturation must not imply native sensor headroom");
        Console.WriteLine("PASS: no movement is not a valid full sweep");
        Console.WriteLine("PASS: additive WinMM high-limit interpretation in read-only sweep JSON");

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
                Enumerable.Repeat(0u, 40).ToArray(), 0, 65535, 3, 1.0).Status == "No variation resolved" &&
                SignalHealthDiagnostics.Evaluate(
                Enumerable.Repeat(0u, 40).ToArray(), 0, 65535, 3, 1.001).Status == "Insufficient samples",
            "one-second gap threshold must be inclusive");
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
                @"\\?\hid#pid_abcd&vid_1234#PRIVATE_SERIAL", out _, out _),
            "invalid HID identifier patterns must not be accepted");
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

        Console.WriteLine("ALL CHECKS PASSED");
        return 0;
    }
}
