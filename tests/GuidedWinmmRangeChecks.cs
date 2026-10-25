using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Synthetic-only: no joystick, HID handles, driver writes or physical pedal access.
internal static class GuidedWinmmRangeChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var start = new DateTime(2026, 10, 25, 5, 0, 0, DateTimeKind.Utc);
        var device = new WinmmJoystick.Device(
            0, "Synthetic WinMM controller",
            new uint[] { 100, 0, 0, 0, 0, 0 },
            new uint[] { 1000, 65535, 65535, 65535, 65535, 65535 });

        GuidedCalibration NewGuide() =>
            new(PedalKind.Brake, device, Array.Empty<int>(), start);

        // Invalid rest readings must never become a baseline or a pedal mapping.
        foreach (uint invalid in new uint[] { 99, 1001 })
        {
            var guide = NewGuide();
            guide.Observe(new uint[] { invalid, 0, 0, 0, 0, 0 }, start);
            if (guide.Phase != GuidePhase.Failed || guide.Result is not null ||
                !guide.FailureReason.Contains("outside the reported axis range"))
                throw new Exception("FAILED: invalid WinMM rest sample was accepted");
        }

        // A later invalid sample must also invalidate an otherwise valid rest phase.
        var afterRest = NewGuide();
        for (int i = 0; i <= 30; i++)
            afterRest.Observe(new uint[] { 100, 0, 0, 0, 0, 0 },
                start.AddMilliseconds(i * 40));
        if (afterRest.Phase != GuidePhase.AwaitingPress)
            throw new Exception("FAILED: valid WinMM rest samples were rejected");

        afterRest.Observe(new uint[] { 1001, 0, 0, 0, 0, 0 },
            start.AddMilliseconds(1240));
        if (afterRest.Phase != GuidePhase.Failed || afterRest.Result is not null)
            throw new Exception("FAILED: invalid WinMM press sample was accepted");

        // Exact advertised endpoints remain legal input for axis discovery.
        var valid = NewGuide();
        for (int i = 0; i <= 30; i++)
            valid.Observe(new uint[] { 100, 0, 0, 0, 0, 0 },
                start.AddMilliseconds(i * 40));
        valid.Observe(new uint[] { 1000, 0, 0, 0, 0, 0 },
            start.AddMilliseconds(1240));
        if (valid.Phase == GuidePhase.Failed || valid.Axis != 0)
            throw new Exception("FAILED: valid WinMM endpoint was rejected");

        Console.WriteLine("PASS: guided WinMM out-of-range samples fail closed (synthetic C#)");
    }
}
