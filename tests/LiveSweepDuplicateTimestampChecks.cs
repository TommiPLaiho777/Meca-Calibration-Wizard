using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Synthetic WinMM timing regression only: no joystick, driver or pedal access.
internal static class LiveSweepDuplicateTimestampChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var start = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        static uint[] Sample(uint axisValue) => new[] { axisValue, 0u, 0u, 0u, 0u, 0u };

        // The first live sample is allowed at t=0. A second observation
        // with the same millisecond timestamp must invalidate the capture.
        var repeatedAtStart = new InputSweepRun(0, 0, 1000, start);
        repeatedAtStart.Observe(Sample(0), start);
        if (repeatedAtStart.Failed)
            throw new Exception("FAILED: live WinMM sweep rejected its first t=0 sample");
        repeatedAtStart.Observe(Sample(900), start);
        AssertFailed(repeatedAtStart, "duplicate live WinMM timestamp at t=0");

        // A duplicate timestamp after normal polling must also fail closed.
        var repeatedLater = new InputSweepRun(0, 0, 1000, start);
        repeatedLater.Observe(Sample(0), start);
        repeatedLater.Observe(Sample(500), start.AddMilliseconds(40));
        repeatedLater.Observe(Sample(900), start.AddMilliseconds(40));
        AssertFailed(repeatedLater, "duplicate live WinMM timestamp after initial sample");
        repeatedLater.Observe(Sample(0), start.AddSeconds(8));
        AssertFailed(repeatedLater, "invalid live sweep must not recover");

        // Ordinary 40ms polling remains valid, including a first sample at t=0.
        var continuous = new InputSweepRun(0, 0, 1000, start);
        for (int i = 0; i <= 200; i++)
        {
            uint value = i <= 100 ? (uint)(i * 10) : (uint)((200 - i) * 10);
            continuous.Observe(Sample(value), start.AddMilliseconds(i * 40));
        }
        if (!continuous.Complete || continuous.Failed || continuous.Result is null)
            throw new Exception("FAILED: strictly increasing live WinMM timestamps were rejected");

        // Historical sparse fixtures opt out of continuous timing validation;
        // keep their behavior separate from the real WPF capture path.
        var synthetic = new InputSweepRun(0, 0, 1000, start, requireContinuousSamples: false);
        synthetic.Observe(Sample(0), start);
        synthetic.Observe(Sample(900), start);
        synthetic.Observe(Sample(0), start.AddSeconds(8));
        if (!synthetic.Complete || synthetic.Failed || synthetic.Result is null)
            throw new Exception("FAILED: legacy sparse synthetic WinMM fixture changed");

        Console.WriteLine("PASS: live WinMM sweeps reject duplicate timestamps; sparse synthetic fixtures remain separate");
    }

    private static void AssertFailed(InputSweepRun run, string scenario)
    {
        if (!run.Failed || run.Complete || run.Result is not null ||
            !run.FailureReason.Contains("timing interrupted"))
            throw new Exception("FAILED: " + scenario);
    }
}
