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
        // Source-level GUI contract: absent optional clutch has no visible mapping row.
        // This does not substitute for a real WPF layout test on Windows.
        string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        // Hardware-free accessibility source contract for the controller selector.
        // Actual screen-reader announcement still needs a Windows WPF test.
        int selectorStart = xaml.IndexOf("<ComboBox x:Name=\"DeviceCombo\"", StringComparison.Ordinal);
        int selectorEnd = selectorStart < 0 ? -1 : xaml.IndexOf("/>", selectorStart, StringComparison.Ordinal);
        if (selectorStart < 0 || selectorEnd < selectorStart)
            throw new Exception("FAILED: WinMM controller selector XAML missing");
        string selectorMarkup = xaml[selectorStart..(selectorEnd + 2)];
        if (!selectorMarkup.Contains("AutomationProperties.Name=\"WinMM pedal controller\"", StringComparison.Ordinal) ||
            !selectorMarkup.Contains("AutomationProperties.HelpText=\"Select the WinMM controller used by the pedals. WinMM ID and name do not verify physical USB identity.\"", StringComparison.Ordinal))
            throw new Exception("FAILED: WinMM controller selector accessible name or physical identity warning missing");
        Console.WriteLine("PASS: WinMM controller selector accessible label and identity warning (WPF source contract)");
        // Refresh is a destructive session boundary, not a harmless list reload.
        // Explain the recapture requirement to pointer and assistive-technology users.
        if (!xaml.Contains("Click=\"Refresh_Click\"", StringComparison.Ordinal) ||
            !xaml.Contains("ToolTip=\"Refresh clears the previous WinMM pedal, signal and sweep measurements and HID scan results. Capture or scan again before exporting.\"", StringComparison.Ordinal) ||
            !xaml.Contains("AutomationProperties.HelpText=\"Refresh clears previous measurements. Select the controller and repeat capture. A WinMM ID does not verify physical device identity.\"", StringComparison.Ordinal))
            throw new Exception("FAILED: Refresh must explain discarded WinMM/HID data and physical identity limits");
        Console.WriteLine("PASS: Refresh warns about lost measurements and WinMM identity limits (WPF source contract)");
        // Switching away from a scrolled diagnostics page must show the next
        // wizard heading, rather than preserving an off-screen scroll offset.
        // This is a source contract; actual scrolling needs a Windows WPF UI test.
        if (!xaml.Contains("<ScrollViewer x:Name=\"WizardScroll\" VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Disabled\">", StringComparison.Ordinal) ||
            !navigation.Contains("WizardScroll.ScrollToTop();", StringComparison.Ordinal))
            throw new Exception("FAILED: wizard view transitions must reset the scroll viewport");
        Console.WriteLine("PASS: wizard view changes reset the scroll viewport (WPF source contract)");
        if (!xaml.Contains(@"x:Name=""ClutchMappingRow"" Margin=""0,19,0,0"" Visibility=""Collapsed""", StringComparison.Ordinal))
            throw new Exception("FAILED: optional clutch mapping row must start hidden");
        string mapping = Section(source, "private void UpdateMappingLabels()", "private string MapLabel(");
        if (!mapping.Contains("ClutchMappingRow.Visibility = ClutchCheck?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;", StringComparison.Ordinal))
            throw new Exception("FAILED: optional clutch mapping visibility must follow explicit clutch selection");
        Console.WriteLine("PASS: optional clutch mapping is hidden unless selected (WPF source contract)");

        // The WinMM idle-check status needs a full-width, wrapped row below
        // the two buttons. This is a source-level layout contract, not a
        // Windows rendering or physical-pedal validation.
        int signalStart = xaml.IndexOf("WINMM SIGNAL STABILITY", StringComparison.Ordinal);
        int signalEnd = xaml.IndexOf("AXIS SWEEP · 8 SECONDS", signalStart, StringComparison.Ordinal);
        if (signalStart < 0 || signalEnd <= signalStart)
            throw new Exception("FAILED: WinMM signal layout boundaries missing");
        string signalLayout = xaml[signalStart..signalEnd];
        // English, assistive-technology guidance must distinguish a WinMM
        // at-rest check from direct sensor/EMI measurements. Markup contract
        // only: screen-reader behavior still needs Windows WPF validation.
        foreach (string expected in new[]
        {
            "ToolTip=\\"Read-only 3-second WinMM at-rest check. Keep all pedals released. This cannot prove absence of sensor noise or EMI.\\"",
            "AutomationProperties.HelpText=\\"Keep all pedals released for a 3-second WinMM signal check. No Windows calibration changes are made.\\"",
            "ToolTip=\\"Read-only 10-second WinMM at-rest check. Keep all pedals released. Windows deadzones may hide sensor noise.\\"",
            "AutomationProperties.HelpText=\\"Keep all pedals released for a 10-second WinMM signal check. This does not establish sensor noise or EMI.\\""
        })
            if (!signalLayout.Contains(expected, StringComparison.Ordinal))
                throw new Exception("FAILED: WinMM idle-check button guidance missing: " + expected);
        Console.WriteLine("PASS: 3s and 10s WinMM idle-check buttons explain limits (WPF source contract)");
        if (!signalLayout.Contains("x:Name=\"SignalText\" Grid.Row=\"1\" Text=\"Not measured\" TextWrapping=\"Wrap\"", StringComparison.Ordinal) ||
            signalLayout.Contains("<DockPanel LastChildFill=\"True\">", StringComparison.Ordinal))
            throw new Exception("FAILED: WinMM signal status must wrap below the action buttons");
        // Read-only WinMM action buttons must wrap rather than clip when the
        // diagnostics pane is narrow or the optional clutch button is visible.
        // Markup-only contract: real WPF layout remains to be checked on Windows.
        if (!signalLayout.Contains("<WrapPanel Orientation=\"Horizontal\">", StringComparison.Ordinal) ||
            signalLayout.Contains("<StackPanel Orientation=\"Horizontal\">", StringComparison.Ordinal))
            throw new Exception("FAILED: WinMM signal actions must wrap on narrow windows");
        int sweepStart = xaml.IndexOf("AXIS SWEEP · 8 SECONDS", StringComparison.Ordinal);
        int sweepEnd = sweepStart < 0 ? -1 : xaml.IndexOf("Export preview report (JSON)", sweepStart, StringComparison.Ordinal);
        if (sweepStart < 0 || sweepEnd <= sweepStart)
            throw new Exception("FAILED: WinMM sweep action layout boundaries missing");
        string sweepLayout = xaml[sweepStart..sweepEnd];
        if (!sweepLayout.Contains("<WrapPanel Orientation=\"Horizontal\">", StringComparison.Ordinal) ||
            sweepLayout.Contains("<StackPanel Orientation=\"Horizontal\">", StringComparison.Ordinal) ||
            !sweepLayout.Contains("x:Name=\"ClutchSweepButton\"", StringComparison.Ordinal))
            throw new Exception("FAILED: optional clutch and sweep actions must wrap on narrow windows");
        Console.WriteLine("PASS: WinMM signal and sweep action rows wrap (hardware-free WPF markup contract)");

        Console.WriteLine("PASS: WinMM signal status has full-width wrapped row (WPF source contract)");

        // The trace plots time horizontally and the Windows-reported axis value
        // vertically. Pedal movement may decrease the WinMM value on inverted
        // axes; the chart must not suggest that "up" always means "pressed".
        // Markup/source contract only: no WPF rendering or pedal test here.
        string trace = Section(source, "private void DrawTrace(", "private void ScanHid_Click(");
        if (!xaml.Contains("Graph axes: left to right = 0–8 seconds; vertical = WinMM axis value within its reported or assumed range. Pressing a pedal may move the line down.", StringComparison.Ordinal) ||
            !trace.Contains("p.TimeMs / (InputSweepRun.DurationSeconds * 1000)", StringComparison.Ordinal) ||
            !trace.Contains("(p.WinmmValue - min) / range", StringComparison.Ordinal))
            throw new Exception("FAILED: sweep graph explanation must match WinMM time/value axes");
        Console.WriteLine("PASS: sweep graph explains WinMM axes and inverted pedal direction (WPF source contract)");

        Console.WriteLine("PASS: stale WinMM sessions cannot re-enable measurement starts through WPF state resets (source contract)");
    }
}
