using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Hardware-free provenance regression. This does not run WPF or read MECA pedals.
internal static class SweepAssumedRangeChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        uint[] values =
        {
            0, 5000, 15000, 25000, 35000, 45000, 55000,
            65535, 65535, 65535, 58000, 42000, 24000,
            10000, 1000, 0, 0
        };
        var trace = values.Select((value, index) =>
            new SweepPoint(index * 400L, value)).ToArray();

        var reported = SweepDiagnostics.Evaluate(trace, 0, 65535);
        if (!reported.CompletePressAndRelease || !reported.ReachesReportedMaximum ||
            reported.Status != "Sweep captured")
            throw new Exception("FAILED: reported WinMM upper endpoint classification");

        var assumed = SweepDiagnostics.Evaluate(trace, 0, 65535,
            axisRangeAssumed: true);
        if (!assumed.CompletePressAndRelease || assumed.ReachesReportedMaximum ||
            assumed.Status != "Sweep captured" ||
            !assumed.Explanation.Contains("assumed", StringComparison.OrdinalIgnoreCase) ||
            !assumed.Explanation.Contains("not a verified driver-reported maximum",
                StringComparison.OrdinalIgnoreCase))
            throw new Exception("FAILED: assumed WinMM fallback was reported as verified maximum");

        var invertedTrace = values.Select((value, index) =>
            new SweepPoint(index * 400L, 65535u - value)).ToArray();
        var inverted = SweepDiagnostics.Evaluate(invertedTrace, 0, 65535,
            inverted: true, axisRangeAssumed: true);
        if (!inverted.CompletePressAndRelease || inverted.ReachesReportedMaximum ||
            !inverted.Explanation.Contains("released end", StringComparison.OrdinalIgnoreCase))
            throw new Exception("FAILED: inverted assumed WinMM range provenance");

        // Both visible sweep analysis and JSON export must pass the live
        // capability provenance; the default argument is for legacy fixtures only.
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        if (System.Text.RegularExpressions.Regex.Matches(source,
                @"axisRangeAssumed:\s*_device\.AssumedAxisRanges").Count != 2)
            throw new Exception("FAILED: WPF sweep display/export omitted range provenance");

        Console.WriteLine("PASS: assumed WinMM sweep bound is not a reported maximum (synthetic)");
    }
}
