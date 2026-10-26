using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Hardware-free WPF source contract: a disconnected or invalid WinMM stream
// must not leave a plausible-looking pedal gauge frozen at its last value.
// The Windows CI build separately compiles the actual WPF view.
internal static class WinmmStaleGaugeChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        int start = source.IndexOf("private void MarkLiveReadingsUnavailable()", StringComparison.Ordinal);
        int end = source.IndexOf("private void UpdateDeviceInfo(", start, StringComparison.Ordinal);
        if (start < 0 || end <= start)
            throw new Exception("FAILED: WinMM stale-reading UI handler not found");

        string body = source[start..end];
        foreach (string expected in new[]
        {
            "LiveValue.Text = \"—\";",
            "LiveRaw.Text = \"WinMM input interrupted",
            "CaptureProgress.Value = 0;",
            "BrakeProgress.Value = 0;",
            "ThrottleProgress.Value = 0;",
            "ClutchProgress.Value = 0;",
            "NeedleRotation.Angle = -5;",
            "ClutchDiscShift.X = 0;",
            "ClutchHubShift.X = 0;",
            "BrakeCaliper.Opacity = 1.0;"
        })
            if (!body.Contains(expected, StringComparison.Ordinal))
                throw new Exception($"FAILED: stale WinMM gauge not reset: {expected}");

        Console.WriteLine("PASS: stale WinMM gauges reset after input interruption (WPF source contract)");
    }
}
