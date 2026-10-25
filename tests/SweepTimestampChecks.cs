using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Synthetic-only regression: imported WinMM traces, no joystick or pedal access.
internal static class SweepTimestampChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var valid = new[]
        {
            new SweepPoint(0, 0),
            new SweepPoint(100, 300),
            new SweepPoint(200, 900),
            new SweepPoint(300, 0)
        };
        var normal = SweepDiagnostics.Evaluate(valid, 0, 1000);
        if (normal.Status == "Invalid sweep timing")
            throw new Exception("FAILED: monotonic WinMM sweep timestamps were rejected");

        var rollback = new[]
        {
            new SweepPoint(0, 0),
            new SweepPoint(200, 300),
            new SweepPoint(100, 900),
            new SweepPoint(300, 0)
        };
        var reversed = SweepDiagnostics.Evaluate(rollback, 0, 1000);
        if (reversed.Status != "Invalid sweep timing" ||
            reversed.CompletePressAndRelease || reversed.ReachesReportedMaximum ||
            reversed.ObservedTravelPercent != 0)
            throw new Exception("FAILED: backwards WinMM timestamps were accepted");

        var negative = new[]
        {
            new SweepPoint(-1, 0),
            new SweepPoint(100, 300),
            new SweepPoint(200, 900),
            new SweepPoint(300, 0)
        };
        var invalid = SweepDiagnostics.Evaluate(negative, 0, 1000);
        if (invalid.Status != "Invalid sweep timing" || invalid.CompletePressAndRelease)
            throw new Exception("FAILED: negative WinMM timestamps were accepted");

        // Equal timestamps can hide an impossible instantaneous press/release
        // in an imported trace. These are not physical pedal measurements.
        var duplicate = new[]
        {
            new SweepPoint(0, 0),
            new SweepPoint(100, 300),
            new SweepPoint(100, 900),
            new SweepPoint(200, 0)
        };
        var repeated = SweepDiagnostics.Evaluate(duplicate, 0, 1000);
        if (repeated.Status != "Invalid sweep timing" ||
            repeated.CompletePressAndRelease || repeated.ReachesReportedMaximum ||
            repeated.ObservedTravelPercent != 0 ||
            !repeated.Explanation.Contains("repeated"))
            throw new Exception("FAILED: repeated WinMM sweep timestamps were accepted");

        // The recorded field-test sweeps use distinct timestamps; normal
        // strictly increasing imported traces must remain acceptable.
        if (normal.Status == "Invalid sweep timing")
            throw new Exception("FAILED: valid increasing WinMM timestamps were rejected");

        Console.WriteLine("PASS: imported WinMM sweep timing rejects rollback, duplicate, and negative timestamps (synthetic C#)");
    }
}
