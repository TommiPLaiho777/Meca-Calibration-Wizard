using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Source-level WPF safety contract only. Actual Windows UI interaction and
// physical MECA pedal behavior remain unverified by these synthetic checks.
internal static class WinmmSessionButtonGateChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        string source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));

        static string Section(string text, string start, string end)
        {
            int from = text.IndexOf(start, StringComparison.Ordinal);
            int to = from < 0 ? -1 : text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
            if (from < 0 || to <= from)
                throw new Exception("FAILED: WinMM WPF session-control source boundary missing: " + start);
            return text[from..to];
        }

        string[] controls =
        {
            "CaptureButton", "BrakeSweepButton", "ThrottleSweepButton",
            "ClutchSweepButton", "CheckSignalButton", "LongSignalButton"
        };
        string navigation = Section(source, "private void ChangeStep(int step)",
            "private PedalKind StepKind()");
        if (!navigation.Contains(
                "bool canStartWinmmMeasurement = _device is not null && !_winmmSessionInvalid;",
                StringComparison.Ordinal))
            throw new Exception("FAILED: navigation must gate WinMM starts on a valid selected session");
        foreach (string control in controls)
            if (!navigation.Contains(control + ".IsEnabled = canStartWinmmMeasurement;",
                    StringComparison.Ordinal))
                throw new Exception("FAILED: navigation enabled unsafe WinMM start: " + control);

        string clutch = Section(source, "private void ClutchCheck_Changed(",
            "private void UpdateMappingLabels()");
        foreach (string control in controls)
        {
            if (clutch.Contains(control + ".IsEnabled = true;", StringComparison.Ordinal))
                throw new Exception("FAILED: optional clutch change bypasses stale session: " + control);
            if (clutch.Contains(control + ".IsEnabled =", StringComparison.Ordinal) &&
                !clutch.Contains(control +
                    ".IsEnabled = _device is not null && !_winmmSessionInvalid;",
                    StringComparison.Ordinal))
                throw new Exception("FAILED: optional clutch cancellation does not preserve WinMM gate: " + control);
        }

        string directRead = Section(source, "private void Capture_Click(", "private void Poll(");
        string poll = Section(source, "private void Poll(", "private double GetPercent(");
        string failedPoll = poll[..poll.IndexOf(
            "StatusText.Text = \"Input streaming · read-only\";", StringComparison.Ordinal)];
        foreach (string control in controls)
        {
            if (!directRead.Contains(control + ".IsEnabled = false;", StringComparison.Ordinal) ||
                directRead.Contains(control + ".IsEnabled = true;", StringComparison.Ordinal))
                throw new Exception("FAILED: failed direct WinMM capture re-enabled " + control);
            if (!failedPoll.Contains(control + ".IsEnabled = false;", StringComparison.Ordinal) ||
                failedPoll.Contains(control + ".IsEnabled = true;", StringComparison.Ordinal))
                throw new Exception("FAILED: interrupted WinMM polling re-enabled " + control);
        }
        Console.WriteLine("PASS: stale WinMM sessions cannot re-enable measurement starts through WPF state resets (source contract)");
    }
}
