using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Synthetic WinMM samples only. Nonzero axis bounds and inverted mappings
// must preserve the raw high-endpoint meaning without implying pedal force.
internal static class NonzeroWinmmAxisSweepChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        const uint axisMin = 1000;
        const uint axisMax = 2000;

        // End with two WinMM readings inside the 3% released band.
        // The earlier linear fixture ended at 10%, 5%, 0%, which left
        // only one near-rest sample and could conceal a final dropout.
        var invertedTrace = Enumerable.Range(0, 41)
            .Select(i => new SweepPoint(i * 200L,
                axisMin + (i == 39 ? 980u : (uint)(50 * Math.Abs(i - 20)))))
            .ToArray();
        var inverted = SweepDiagnostics.Evaluate(
            invertedTrace, axisMin, axisMax, inverted: true);
        var wrongPolarity = SweepDiagnostics.Evaluate(
            invertedTrace, axisMin, axisMax);

        if (!inverted.CompletePressAndRelease ||
            inverted.Status != "Sweep captured" ||
            inverted.ObservedTravelPercent != 100 ||
            !inverted.ReachesReportedMaximum ||
            !inverted.Explanation.Contains("released end") ||
            !inverted.Explanation.Contains("does not establish pedal-force clipping") ||
            wrongPolarity.CompletePressAndRelease)
            throw new Exception("FAILED: nonzero WinMM axis bounds or inverted sweep provenance");

        var normalTrace = invertedTrace
            .Select(p => new SweepPoint(p.TimeMs, axisMin + axisMax - p.WinmmValue))
            .ToArray();
        var normal = SweepDiagnostics.Evaluate(normalTrace, axisMin, axisMax);
        if (!normal.CompletePressAndRelease ||
            normal.Status != "Sweep captured" ||
            normal.ObservedTravelPercent != 100 ||
            !normal.ReachesReportedMaximum ||
            SweepDiagnostics.Evaluate(normalTrace, axisMin, axisMax, inverted: true)
                .CompletePressAndRelease)
            throw new Exception("FAILED: nonzero WinMM axis normal sweep classification");

        Console.WriteLine("PASS: nonzero WinMM axis range and inverted endpoint semantics (synthetic C#)");
    }
}
