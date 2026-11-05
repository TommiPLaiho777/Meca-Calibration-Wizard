using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MecaCalibrationWizard;

// Hardware-free WPF source contract. The Windows CI job compiles the actual
// application; this check does not run the dispatcher or touch any HID device.
internal static class WinmmMonotonicClockSourceChecks
{
    [ModuleInitializer]
    internal static void CheckMeasurementClock()
    {
        string source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));

        static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception("FAILED: " + message);
        }

        Require(source.Contains("using System.Diagnostics;") &&
                source.Contains("Stopwatch _measurementStopwatch = Stopwatch.StartNew();") &&
                source.Contains("DateTime MeasurementNow => _measurementEpochUtc + _measurementStopwatch.Elapsed;"),
            "WinMM measurement time must be derived from a monotonic Stopwatch");

        Require(source.Contains("new GuidedCalibration(kind, _device, reserved, MeasurementNow)") &&
                source.Contains("guide.Observe(sample, measurementNow)") &&
                source.Contains("guide.Instruction(measurementNow)"),
            "guided rest, press and release must use the same monotonic clock");

        Require(source.Contains("new NoiseRun(startedUtc: MeasurementNow, durationSeconds: seconds)") &&
                source.Contains("_sweepStartUtc = MeasurementNow;") &&
                Regex.Matches(source, @"var now = MeasurementNow;").Count == 2 &&
                !source.Contains("var now = DateTime.UtcNow;"),
            "idle-noise and sweep start/observation timestamps must use monotonic elapsed time");

        Require(source.Contains("ExportedAtUtc = DateTime.UtcNow,") &&
                source.Contains("ReadOnlyHidReport.Create(_hidInventory!, _hidInventoryComparison, DateTime.UtcNow)"),
            "JSON export must retain real UTC wall-clock timestamps");

        Console.WriteLine("PASS: read-only WinMM captures use monotonic elapsed time (WPF source contract)");
    }
}
