using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Synthetic-only: no joystick, HID handle, Windows calibration or hardware access.
internal static class WinmmOutOfRangeSweepChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var start = new DateTime(2026, 10, 25, 4, 0, 0, DateTimeKind.Utc);

        // A WinMM sample outside joyGetDevCaps' declared range must never
        // become a valid sweep or an apparent axis-limit observation.
        foreach (uint invalid in new[] { 99u, 1001u })
        {
            var run = new InputSweepRun(0, 100, 1000, start);
            run.Observe(new uint[] { 100, 0, 0, 0, 0, 0 }, start);
            run.Observe(new uint[] { invalid, 0, 0, 0, 0, 0 },
                start.AddMilliseconds(40));
            if (!run.Failed || run.Complete || run.Result is not null ||
                !run.FailureReason.Contains("outside the reported axis range"))
                throw new Exception("FAILED: out-of-range WinMM sweep was accepted");
        }

        // Exact endpoints are valid. Sparse synthetic timing is permitted here
        // only to keep this hardware-free test small; live capture is strict.
        var valid = new InputSweepRun(0, 100, 1000, start,
            requireContinuousSamples: false);
        valid.Observe(new uint[] { 100, 0, 0, 0, 0, 0 }, start);
        valid.Observe(new uint[] { 1000, 0, 0, 0, 0, 0 },
            start.AddSeconds(8));
        if (valid.Failed || !valid.Complete ||
            valid.Result is not { AtLowLimitSamples: 1, AtHighLimitSamples: 1 })
            throw new Exception("FAILED: valid WinMM sweep endpoints were rejected");

        Console.WriteLine("PASS: WinMM out-of-range sweep samples fail closed (synthetic C#)");
    }
}
