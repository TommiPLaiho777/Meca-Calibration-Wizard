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

        // The Advanced view must distinguish driver-reported axis limits from
        // a protective 0..65535 fallback. No Windows joystick is accessed.
        var fallbackDevice = new WinmmJoystick.Device(
            0, "Synthetic WinMM controller", new uint[6],
            Enumerable.Repeat(65535u, 6).ToArray(),
            AssumedAxisRanges: new[] { true, false, false, true, false, false });
        var fallbackSummary = fallbackDevice.WinmmIdsSummary;
        if (!fallbackSummary.Contains("assumed WinMM axis range 0–65535") ||
            !fallbackSummary.Contains(": X, R") ||
            fallbackSummary.Contains(": X, Y"))
            throw new Exception("FAILED: fallback WinMM capability ranges not disclosed");

        // A fallback limit is protective, not a driver-reported capability.
        // The guided UI must name its provenance when rejecting a bad sample.
        var fallbackGuide = new GuidedCalibration(PedalKind.Brake, fallbackDevice,
            Array.Empty<int>(), start);
        fallbackGuide.Observe(new uint[] { 65536, 0, 0, 0, 0, 0 },
            start.AddMilliseconds(40));
        if (fallbackGuide.Phase != GuidePhase.Failed || fallbackGuide.Result is not null ||
            !fallbackGuide.FailureReason.Contains("assumed axis range") ||
            fallbackGuide.FailureReason.Contains("reported axis range"))
            throw new Exception("FAILED: assumed WinMM bounds were presented as reported");
        Console.WriteLine("PASS: guided fallback bounds disclosed (synthetic C#)");

        var reportedDevice = fallbackDevice with { AssumedAxisRanges = new bool[6] };
        if (reportedDevice.WinmmIdsSummary.Contains("assumed WinMM axis range"))
            throw new Exception("FAILED: reported WinMM ranges mislabeled as fallback");
        Console.WriteLine("PASS: WinMM capability fallback provenance shown (synthetic C#)");

        // A brief full-scale spike followed by a middle-band interruption
        // must not contaminate the peak of a later sustained press.
        // Synthetic-only: no physical pedal or WinMM device is accessed.
        var interruptedPeakGuide = NewGuide();
        void FeedInterruptedPeak(int ms, uint x) => interruptedPeakGuide.Observe(
            new uint[] { x, 0, 0, 0, 0, 0 }, start.AddMilliseconds(ms));
        for (int i = 1; i <= 30; i++) FeedInterruptedPeak(i * 40, 100);
        if (interruptedPeakGuide.Phase != GuidePhase.AwaitingPress)
            throw new Exception("FAILED: interrupted peak fixture rest phase");
        FeedInterruptedPeak(1240, 1000); // transient Windows axis high endpoint
        FeedInterruptedPeak(1280, 130);  // below press, above release threshold
        // Three readings spanning only 80 ms must not borrow the old
        // spike's timestamp to satisfy the 100 ms sustained-press rule.
        FeedInterruptedPeak(1320, 200);
        FeedInterruptedPeak(1360, 200);
        FeedInterruptedPeak(1400, 200);
        FeedInterruptedPeak(1440, 100);
        if (interruptedPeakGuide.PressesCompleted != 0)
            throw new Exception("FAILED: interrupted WinMM hold inherited an old timestamp");
        // Now capture three legitimate sustained strokes, all at 200.
        FeedInterruptedPeak(1480, 200);
        FeedInterruptedPeak(1540, 200);
        FeedInterruptedPeak(1600, 200);
        FeedInterruptedPeak(1640, 100);
        FeedInterruptedPeak(1680, 200);
        FeedInterruptedPeak(1740, 200);
        FeedInterruptedPeak(1800, 200);
        FeedInterruptedPeak(1840, 100);
        FeedInterruptedPeak(1880, 200);
        FeedInterruptedPeak(1940, 200);
        FeedInterruptedPeak(2000, 200);
        FeedInterruptedPeak(2040, 100);
        if (interruptedPeakGuide.Phase != GuidePhase.Complete ||
            interruptedPeakGuide.Result is not { PeakInputs.Length: 3 } peaks ||
            peaks.PeakInputs[0] != 200 || peaks.PeakInputs[1] != 200 ||
            peaks.PeakInputs[2] != 200 || peaks.AtAxisLimit)
            throw new Exception("FAILED: interrupted WinMM spike polluted a valid press peak");
        Console.WriteLine("PASS: interrupted WinMM peak cannot pollute a valid press (synthetic C#)");

        Console.WriteLine("PASS: guided WinMM out-of-range samples fail closed (synthetic C#)");
    }
}
