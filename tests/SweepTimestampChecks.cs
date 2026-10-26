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

        // A binary WinMM signal can show a full-range rest/press/release shape
        // without enough intermediate samples to validate a sweep.
        // Synthetic values only; no joystick or physical pedal access.
        var twoLevelTrace = Enumerable.Range(0, 41)
            .Select(i => new SweepPoint(i * 200L, i is >= 10 and <= 30 ? 65535u : 0u))
            .ToArray();
        var twoLevel = SweepDiagnostics.Evaluate(twoLevelTrace, 0, 65535);
        if (twoLevel.CompletePressAndRelease ||
            twoLevel.Status != "Insufficient signal steps" ||
            !twoLevel.ReachesReportedMaximum ||
            twoLevel.DistinctValues != 2)
            throw new Exception("FAILED: binary WinMM sweep falsely marked complete");

        var invertedTwoLevelTrace = twoLevelTrace
            .Select(point => point with { WinmmValue = 65535u - point.WinmmValue })
            .ToArray();
        var invertedTwoLevel = SweepDiagnostics.Evaluate(invertedTwoLevelTrace, 0, 65535, inverted: true);
        if (invertedTwoLevel.CompletePressAndRelease ||
            invertedTwoLevel.Status != "Insufficient signal steps" ||
            invertedTwoLevel.DistinctValues != 2)
            throw new Exception("FAILED: binary inverted WinMM sweep falsely marked complete");
        Console.WriteLine("PASS: binary WinMM sweep cannot claim completed capture (synthetic C#)");

        // A two-sample cached trace is insufficient for a physical sweep, but
        // invalid values or ambiguous timestamps still take precedence.
        // Synthetic-only: no WinMM, HID, or pedal device access.
        var shortOutOfRange = SweepDiagnostics.Evaluate(
            new[] { new SweepPoint(0, 0u), new SweepPoint(100, 1001u) }, 0, 1000);
        if (shortOutOfRange.Status != "Invalid WinMM range" ||
            shortOutOfRange.CompletePressAndRelease || shortOutOfRange.ReachesReportedMaximum)
            throw new Exception("FAILED: short out-of-range WinMM trace masked as insufficient data");

        var shortDuplicate = SweepDiagnostics.Evaluate(
            new[] { new SweepPoint(0, 0u), new SweepPoint(0, 100u) }, 0, 1000);
        if (shortDuplicate.Status != "Invalid sweep timing" ||
            shortDuplicate.CompletePressAndRelease || shortDuplicate.ReachesReportedMaximum)
            throw new Exception("FAILED: short duplicate-timestamp trace masked as insufficient data");

        var shortValid = SweepDiagnostics.Evaluate(
            new[] { new SweepPoint(0, 0u), new SweepPoint(100, 100u) }, 0, 1000);
        if (shortValid.Status != "Insufficient data" || shortValid.CompletePressAndRelease)
            throw new Exception("FAILED: valid short WinMM trace must remain insufficient data");
        Console.WriteLine("PASS: short imported WinMM traces prioritize range/timing faults (synthetic C#)");

        // One isolated WinMM high sample can be a transient spike rather
        // than a held pedal press. The trace still has enough distinct values,
        // normal timestamps, and a released start/end. No hardware involved.
        var isolatedPeak = Enumerable.Range(0, 41)
            .Select(i => new SweepPoint(i * 200L, i switch
            {
                17 or 23 => 10000u,
                18 or 22 => 30000u,
                19 or 21 => 45000u,
                20 => 65535u,
                _ => 0u
            }))
            .ToArray();
        var isolatedAssessment = SweepDiagnostics.Evaluate(isolatedPeak, 0, 65535);
        if (isolatedAssessment.CompletePressAndRelease ||
            isolatedAssessment.Status != "Pressed hold not confirmed" ||
            !isolatedAssessment.Explanation.Contains("hold briefly"))
            throw new Exception("FAILED: isolated WinMM peak falsely accepted as a held pedal press");

        var invertedIsolatedPeak = isolatedPeak
            .Select(p => p with { WinmmValue = 65535u - p.WinmmValue })
            .ToArray();
        var invertedIsolatedAssessment = SweepDiagnostics.Evaluate(
            invertedIsolatedPeak, 0, 65535, inverted: true);
        if (invertedIsolatedAssessment.CompletePressAndRelease ||
            invertedIsolatedAssessment.Status != "Pressed hold not confirmed")
            throw new Exception("FAILED: isolated inverted WinMM peak falsely accepted");

        // Two high samples 200 ms apart can still be two transients;
        // three high samples 300 ms apart are too sparse to prove a hold.
        // All fixtures below are synthetic and never contact a pedal.
        var twoHighSamples = isolatedPeak.Select((p, i) =>
            i is 20 or 21 ? p with { WinmmValue = 65535u } : p).ToArray();
        var twoHighAssessment = SweepDiagnostics.Evaluate(twoHighSamples, 0, 65535);
        if (twoHighAssessment.CompletePressAndRelease ||
            twoHighAssessment.Status != "Pressed hold not confirmed")
            throw new Exception("FAILED: two high WinMM samples falsely proved a held press");

        var invertedTwoHigh = twoHighSamples
            .Select(p => p with { WinmmValue = 65535u - p.WinmmValue }).ToArray();
        var invertedTwoHighAssessment = SweepDiagnostics.Evaluate(
            invertedTwoHigh, 0, 65535, inverted: true);
        if (invertedTwoHighAssessment.CompletePressAndRelease ||
            invertedTwoHighAssessment.Status != "Pressed hold not confirmed")
            throw new Exception("FAILED: two inverted high-direction samples falsely proved a hold");

        var sparseHighSamples = isolatedPeak.Select((p, i) => new SweepPoint(
            p.TimeMs + (i >= 21 ? 100L : 0L) + (i >= 22 ? 100L : 0L),
            i is >= 20 and <= 22 ? 65535u : p.WinmmValue)).ToArray();
        var sparseHighAssessment = SweepDiagnostics.Evaluate(sparseHighSamples, 0, 65535);
        if (sparseHighAssessment.CompletePressAndRelease ||
            sparseHighAssessment.Status != "Pressed hold not confirmed")
            throw new Exception("FAILED: sparse high WinMM samples falsely proved a held press");
        Console.WriteLine("PASS: two-sample and sparse WinMM peaks cannot prove a hold (synthetic C#)");

        var heldPeak = isolatedPeak.Select((p, i) =>
            i is >= 20 and <= 22 ? p with { WinmmValue = 65535u } : p).ToArray();
        var heldAssessment = SweepDiagnostics.Evaluate(heldPeak, 0, 65535);
        if (!heldAssessment.CompletePressAndRelease ||
            heldAssessment.Status != "Limited intermediate steps")
            throw new Exception("FAILED: sustained WinMM press/release was rejected");
        Console.WriteLine("PASS: sustained WinMM press required; isolated spikes rejected (synthetic C#)");

        Console.WriteLine("PASS: imported WinMM sweep timing rejects rollback, duplicate, and negative timestamps (synthetic C#)");
    }
}
