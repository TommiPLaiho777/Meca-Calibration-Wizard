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

        // WPF SaveFileDialog is modal but may pump dispatcher messages. A failed
        // WinMM poll while it is open must block the report, even though the
        // initial export gate was satisfied. No WPF dialog or pedal is opened here.
        int exportStart = source.IndexOf("private void Export_Click(", StringComparison.Ordinal);
        int dialogIndex = source.IndexOf("if (save.ShowDialog(this) != true) return;",
            exportStart, StringComparison.Ordinal);
        int secondGate = source.IndexOf("if (!ReadOnlyHidReport.CanExport(",
            dialogIndex, StringComparison.Ordinal);
        int fileWrite = source.IndexOf("File.WriteAllText(save.FileName",
            dialogIndex, StringComparison.Ordinal);
        if (exportStart < 0 || dialogIndex < 0 || secondGate <= dialogIndex ||
            fileWrite <= secondGate)
            throw new Exception("FAILED: missing post-dialog WinMM export gate");
        string postDialog = source[secondGate..fileWrite];
        foreach (string expectedGate in new[]
        {
            "_winmmSessionInvalid",
            "_capture is not null || _noise is not null || _sweep is not null",
            "No report saved."
        })
            if (!postDialog.Contains(expectedGate, StringComparison.Ordinal))
                throw new Exception($"FAILED: post-dialog WinMM export gate missing: {expectedGate}");
        Console.WriteLine("PASS: report export rechecks WinMM session after save dialog (WPF source contract)");
        
        // Capture_Click makes a direct WinMM read outside Poll. A single
        // impossible axis value must invalidate the session before any new
        // GuidedCalibration is constructed, not wait for a later timer tick.
        int captureStart = source.IndexOf("private void Capture_Click(", StringComparison.Ordinal);
        int captureEnd = source.IndexOf("private void Poll(", captureStart, StringComparison.Ordinal);
        if (captureStart < 0 || captureEnd <= captureStart)
            throw new Exception("FAILED: direct WinMM capture handler not found");
        string captureBody = source[captureStart..captureEnd];
        int readIndex = captureBody.IndexOf("WinmmJoystick.TryRead(", StringComparison.Ordinal);
        int boundsIndex = captureBody.IndexOf("WinmmJoystick.IsSampleWithinAxisBounds(", StringComparison.Ordinal);
        int invalidIndex = captureBody.IndexOf("_winmmSessionInvalid = true;", StringComparison.Ordinal);
        int beginIndex = captureBody.IndexOf("new GuidedCalibration(", StringComparison.Ordinal);
        if (readIndex < 0 || boundsIndex <= readIndex ||
            invalidIndex <= boundsIndex || beginIndex <= invalidIndex)
            throw new Exception("FAILED: direct capture must reject out-of-range WinMM samples before starting calibration");
        Console.WriteLine("PASS: direct capture invalidates out-of-range WinMM session (WPF source contract)");
    }
}
