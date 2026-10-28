using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Pure C# regression: no Windows joystick, HID handle, or pedal required.
internal static class WinmmMalformedNoiseChecks
{
    [ModuleInitializer]
    internal static void RejectMalformedIdleSamples()
    {
        var start = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        foreach (uint[]? malformed in new uint[]?[]
        {
            null, Array.Empty<uint>(), new uint[5], new uint[7]
        })
        {
            var check = new NoiseRun(start);
            for (int i = 0; i < 15; i++)
                check.Observe(new uint[6], start.AddMilliseconds(i * 40));
            if (check.Complete)
                throw new Exception("FAILED: valid idle samples ended the check early");

            check.Observe(malformed, start.AddMilliseconds(600));
            var result = check.Analyze(0, 0, 65535);
            if (!check.Complete || result.SampleCount != 15 ||
                result.Status != "Invalid WinMM sample" ||
                !result.Guidance.Contains("malformed", StringComparison.OrdinalIgnoreCase))
                throw new Exception("FAILED: malformed WinMM idle sample was not rejected");

            // A later normal sample cannot silently repair the interrupted check.
            check.Observe(new uint[6], start.AddSeconds(3));
            if (check.Analyze(0, 0, 65535).SampleCount != 15 ||
                check.Analyze(0, 0, 65535).Status != "Invalid WinMM sample")
                throw new Exception("FAILED: rejected idle check resumed after malformed sample");
        }

        var valid = new NoiseRun(start);
        for (int i = 0; i <= 75; i++)
            valid.Observe(new uint[6], start.AddMilliseconds(i * 40));
        if (!valid.Complete || valid.Analyze(0, 0, 65535).Status != "No variation resolved")
            throw new Exception("FAILED: ordinary six-axis idle check regressed");

        Console.WriteLine("PASS: malformed WinMM idle samples fail closed (synthetic C#)");
    }
}
