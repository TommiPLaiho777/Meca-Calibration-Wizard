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
                !stats.Guidance.Contains("refresh the controller"))
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

        Console.WriteLine("PASS: out-of-range WinMM idle traces flagged invalid (synthetic C#)");
    }
}
