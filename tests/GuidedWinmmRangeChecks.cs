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
        // Three sustained strokes with three consecutive low samples each.
        // One low sample at 1440 ms must not complete the earlier short streak.
        int nextStrokeMs = 1480;
        for (int stroke = 0; stroke < 3; stroke++)
        {
            FeedInterruptedPeak(nextStrokeMs, 200);
            FeedInterruptedPeak(nextStrokeMs + 60, 200);
            FeedInterruptedPeak(nextStrokeMs + 120, 200);
            FeedInterruptedPeak(nextStrokeMs + 160, 100);
            FeedInterruptedPeak(nextStrokeMs + 200, 100);
            FeedInterruptedPeak(nextStrokeMs + 240, 100);
            nextStrokeMs += 280;
        }
        if (interruptedPeakGuide.Phase != GuidePhase.Complete ||
            interruptedPeakGuide.Result is not { PeakInputs.Length: 3 } peaks ||
            peaks.PeakInputs[0] != 200 || peaks.PeakInputs[1] != 200 ||
            peaks.PeakInputs[2] != 200 || peaks.AtAxisLimit)
            throw new Exception("FAILED: interrupted WinMM spike polluted a valid press peak");
        Console.WriteLine("PASS: interrupted WinMM peak cannot pollute a valid press (synthetic C#)");

        // A one-sample low dropout after a valid high hold is not a physical
        // release. It must not create two strokes. Check both axis directions.
        foreach (bool inverted in new[] { false, true })
        {
            var releaseStart = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
            var releaseDevice = new WinmmJoystick.Device(
                0, "Synthetic WinMM controller", new uint[6],
                Enumerable.Repeat(65535u, 6).ToArray());
            var releaseGuide = new GuidedCalibration(PedalKind.Brake, releaseDevice,
                Array.Empty<int>(), releaseStart);
            uint rest = inverted ? 65535u : 0u;
            uint pressed = inverted ? 15000u : 50000u;
            int elapsedMs = 0;
            void FeedRelease(uint x)
            {
                elapsedMs += 40;
                releaseGuide.Observe(new uint[] { x, 0, 0, 0, 0, 0 },
                    releaseStart.AddMilliseconds(elapsedMs));
            }
            for (int i = 0; i < 32; i++) FeedRelease(rest);
            if (releaseGuide.Phase != GuidePhase.AwaitingPress)
                throw new Exception("FAILED: debounce fixture rest phase");
            for (int stroke = 0; stroke < 3; stroke++)
            {
                for (int i = 0; i < 4; i++) FeedRelease(pressed);
                FeedRelease(rest); // single 40ms low dropout
                if (releaseGuide.PressesCompleted != stroke ||
                    releaseGuide.Phase != GuidePhase.AwaitingRelease)
                    throw new Exception("FAILED: one low WinMM sample counted as release");
                for (int i = 0; i < 4; i++) FeedRelease(pressed);
                for (int i = 0; i < 3; i++) FeedRelease(rest);
                if (releaseGuide.PressesCompleted != stroke + 1)
                    throw new Exception("FAILED: stable WinMM release not accepted");
            }
            if (releaseGuide.Phase != GuidePhase.Complete ||
                releaseGuide.Result is not { PeakInputs.Length: 3 } result ||
                result.PeakInputs.Any(p => p != pressed) ||
                (result.Mapping.Full < result.Mapping.Rest) != inverted)
                throw new Exception("FAILED: stable release/inverted mapping regression");
        }
        Console.WriteLine("PASS: one-sample release dropouts ignored; stable releases accepted in both directions (synthetic C#)");

        Console.WriteLine("PASS: guided WinMM out-of-range samples fail closed (synthetic C#)");
    }
}
