using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Hardware-free C# regression: a repeated WinMM timestamp is not a second
// rest or pedal sample. The real WPF path supplies monotonic elapsed time.
internal static class GuidedDuplicateTimestampChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var start = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);
        var device = new WinmmJoystick.Device(
            0, "Synthetic WinMM", new uint[6],
            Enumerable.Repeat(65535u, 6).ToArray());
        var rest = new uint[6];

        // A first sample exactly at start is legitimate.
        var duplicateRest = new GuidedCalibration(
            PedalKind.Brake, device, Array.Empty<int>(), start);
        duplicateRest.Observe(rest, start);
        if (duplicateRest.Phase != GuidePhase.MeasuringRest ||
            duplicateRest.RestSamples != 1)
            throw new Exception("FAILED: first guided sample at start was rejected");
        duplicateRest.Observe(rest, start);
        if (duplicateRest.Phase != GuidePhase.Failed ||
            duplicateRest.RestSamples != 1 ||
            duplicateRest.Result is not null ||
            !duplicateRest.FailureReason.Contains("timing interrupted"))
            throw new Exception("FAILED: repeated guided rest timestamp was accepted");

        // A repeated timestamp after rest must not count as a new press.
        var duplicatePress = new GuidedCalibration(
            PedalKind.Brake, device, Array.Empty<int>(), start);
        for (int i = 1; i <= 32; i++)
            duplicatePress.Observe(rest, start.AddMilliseconds(i * 40));
        if (duplicatePress.Phase != GuidePhase.AwaitingPress)
            throw new Exception("FAILED: valid guided rest baseline did not complete");
        duplicatePress.Observe(
            new uint[] { 20000, 0, 0, 0, 0, 0 }, start.AddMilliseconds(1280));
        if (duplicatePress.Phase != GuidePhase.Failed ||
            duplicatePress.PressesCompleted != 0 ||
            duplicatePress.Result is not null)
            throw new Exception("FAILED: repeated guided press timestamp was accepted");

        // A genuine next poll 40 ms later remains valid.
        var validPress = new GuidedCalibration(
            PedalKind.Brake, device, Array.Empty<int>(), start);
        for (int i = 1; i <= 32; i++)
            validPress.Observe(rest, start.AddMilliseconds(i * 40));
        validPress.Observe(
            new uint[] { 20000, 0, 0, 0, 0, 0 }, start.AddMilliseconds(1320));
        if (validPress.Phase != GuidePhase.AwaitingRelease ||
            validPress.PressesCompleted != 0)
            throw new Exception("FAILED: valid guided press timing was rejected");

        Console.WriteLine("PASS: duplicate guided WinMM timestamps fail closed (synthetic C#)");
    }
}
