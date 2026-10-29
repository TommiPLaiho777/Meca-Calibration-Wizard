using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Synthetic WinMM samples only: no hardware, HID handles or Windows settings.
internal static class WinmmSignalRangeChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        // A constant trace outside joyGetDevCaps' advertised range must not
        // masquerade as a clean, zero-variation WinMM idle signal.
        foreach (uint invalid in new uint[] { 99, 1001 })
        {
            var stats = SignalHealthDiagnostics.Evaluate(
                Enumerable.Repeat(invalid, 20).ToArray(),
                axisMin: 100, axisMax: 1000,
                windowSeconds: 1.0, largestSamplingGapSeconds: 0.05);
            if (stats.Status != "Invalid WinMM range" ||
                !stats.Guidance.Contains("refresh the controller") ||
                !stats.Guidance.Contains("reported or assumed"))
                throw new Exception("FAILED: impossible WinMM idle samples appeared valid");
        }

        // Exact reported endpoints are valid WinMM readings, even if flat.
        foreach (uint valid in new uint[] { 100, 1000 })
        {
            var stats = SignalHealthDiagnostics.Evaluate(
                Enumerable.Repeat(valid, 20).ToArray(),
                axisMin: 100, axisMax: 1000,
                windowSeconds: 1.0, largestSamplingGapSeconds: 0.05);
            if (stats.Status != "No variation resolved")
                throw new Exception("FAILED: legal WinMM endpoint rejected");
        }

        // Both faults can occur in one read-only idle capture. The guidance
        // must mention invalid axis bounds AND the timestamp rollback, while
        // preserving the normal timestamp-only failure explanation.
        var invalidWithRollback = SignalHealthDiagnostics.Evaluate(
            Enumerable.Repeat(1001u, 20).ToArray(),
            axisMin: 100, axisMax: 1000, windowSeconds: 1.0,
            largestSamplingGapSeconds: 0.05, timestampRegressionDetected: true);
        if (invalidWithRollback.Status != "Invalid WinMM range" ||
            !invalidWithRollback.Guidance.Contains("outside the available axis bounds") ||
            !invalidWithRollback.Guidance.Contains("timestamps moved backwards") ||
            !invalidWithRollback.Guidance.Contains("refresh the controller"))
            throw new Exception("FAILED: simultaneous WinMM range/timing faults not both explained");

        var rollbackOnly = SignalHealthDiagnostics.Evaluate(
            Enumerable.Repeat(1000u, 20).ToArray(),
            axisMin: 100, axisMax: 1000, windowSeconds: 1.0,
            largestSamplingGapSeconds: 0.05, timestampRegressionDetected: true);
        if (rollbackOnly.Status != "Insufficient samples" ||
            !rollbackOnly.Guidance.Contains("timestamps moved backwards") ||
            rollbackOnly.Guidance.Contains("outside the available axis bounds"))
            throw new Exception("FAILED: timestamp-only WinMM fault guidance changed");

        // The central read-only WinMM boundary must fail closed even when
        // a future caller passes a missing sample or malformed capabilities.
        // No WinMM API or hardware is invoked by these synthetic checks.
        var min = Enumerable.Repeat(100u, 6).ToArray();
        var max = Enumerable.Repeat(1000u, 6).ToArray();
        var normalDevice = new WinmmJoystick.Device(0, "synthetic", min, max);
        var normalSample = Enumerable.Repeat(500u, 6).ToArray();
        if (!WinmmJoystick.IsSampleWithinAxisBounds(normalDevice, normalSample) ||
            !WinmmJoystick.IsSampleWithinAxisBounds(normalDevice, Enumerable.Repeat(100u, 6).ToArray()) ||
            !WinmmJoystick.IsSampleWithinAxisBounds(normalDevice, Enumerable.Repeat(1000u, 6).ToArray()))
            throw new Exception("FAILED: legal WinMM samples rejected");

        if (WinmmJoystick.IsSampleWithinAxisBounds(normalDevice, null) ||
            WinmmJoystick.IsSampleWithinAxisBounds(normalDevice, new uint[5]) ||
            WinmmJoystick.IsSampleWithinAxisBounds(normalDevice, new uint[7]))
            throw new Exception("FAILED: missing or malformed WinMM sample accepted");

        var flatMin = (uint[])min.Clone();
        var flatMax = (uint[])max.Clone();
        flatMin[3] = flatMax[3] = 500;
        var flatDevice = new WinmmJoystick.Device(0, "synthetic-flat-axis", flatMin, flatMax);
        if (WinmmJoystick.IsSampleWithinAxisBounds(flatDevice, normalSample))
            throw new Exception("FAILED: zero-width WinMM capability range accepted");

        // The live Advanced view must distinguish driver-reported ranges from
        // assumed fallbacks on EVERY valid sample, not only on an error.
        // Pure C# synthetic inputs; no WinMM API, WPF window or pedal access.
        var provenance = new[] { false, true, false, false, false, false };
        var previewDevice = new WinmmJoystick.Device(
            0, "synthetic-preview", min, max, AssumedAxisRanges: provenance);
        string reportedPreview = WinmmJoystick.FormatAxisPreview(
            previewDevice, normalSample, 0);
        string assumedPreview = WinmmJoystick.FormatAxisPreview(
            previewDevice, normalSample, 1);
        string unknownPreview = WinmmJoystick.FormatAxisPreview(
            normalDevice, normalSample, 0);
        if (!reportedPreview.Contains("driver-reported WinMM range 100–1000", StringComparison.Ordinal) ||
            !assumedPreview.Contains("assumed fallback WinMM range 100–1000", StringComparison.Ordinal) ||
            !unknownPreview.Contains("unverified WinMM range 100–1000", StringComparison.Ordinal) ||
            !assumedPreview.Contains("not sensor ADC", StringComparison.Ordinal))
            throw new Exception("FAILED: WinMM preview provenance must disclose reported/assumed bounds");
        var windowSource = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        if (!windowSource.Contains(
                "WinmmJoystick.FormatAxisPreview(_device, sample, i)", StringComparison.Ordinal))
            throw new Exception("FAILED: Advanced WinMM UI bypasses axis provenance formatter");
        Console.WriteLine("PASS: WinMM axis provenance visible in Advanced preview (synthetic C# + WPF source contract)");

        Console.WriteLine("PASS: WinMM sample/capability boundary rejects malformed inputs (synthetic C#)");
        Console.WriteLine("PASS: combined WinMM range/timing faults both explained (synthetic C#)");
        Console.WriteLine("PASS: out-of-range WinMM idle traces flagged invalid (synthetic C#)");
    }
}
