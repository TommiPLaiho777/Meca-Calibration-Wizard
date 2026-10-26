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

        Console.WriteLine("PASS: combined WinMM range/timing faults both explained (synthetic C#)");
        Console.WriteLine("PASS: out-of-range WinMM idle traces flagged invalid (synthetic C#)");
    }
}
