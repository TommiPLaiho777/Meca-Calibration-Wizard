using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace MecaCalibrationWizard;

// Hardware-free WPF markup contract. An idle WinMM trace must never be
// presented as a sensor-noise or electrical-interference diagnosis.
internal static class WinmmIdleScopeUiChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml");
        var markup = XDocument.Load(path);
        var notes = markup.Descendants()
            .Where(e => e.Name.LocalName == "TextBlock")
            .Select(e => (string?)e.Attribute("Text"))
            .Where(s => s is not null)
            .ToArray();
        if (!notes.Any(s =>
                s!.Contains("Windows WinMM joystick variation, not sensor noise or EMI",
                    StringComparison.Ordinal) &&
                s.Contains("Windows deadzones may hide changes",
                    StringComparison.Ordinal)))
            throw new Exception("FAILED: WinMM idle-check UI must disclaim sensor/EMI diagnosis");
        // An unchecked optional clutch is a UI choice, not proof of hardware absence.
        // Source/markup contract only: this does not execute the WPF dispatcher.
        const string optionalClutchLabel = "Optional · not selected";
        XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        var clutchMap = markup.Descendants().FirstOrDefault(e =>
            (string?)e.Attribute(xamlNamespace + "Name") == "ClutchMapText");
        if ((string?)clutchMap?.Attribute("Text") != optionalClutchLabel)
            throw new Exception("FAILED: initial clutch label must not assert physical absence");
        // Long WinMM range/provenance lines can exceed the diagnostics card
        // width on the minimum-size window. Keep the values visible by wrapping
        // within the scrollable panel; this is a markup check, not WPF rendering.
        var advancedAxes = markup.Descendants().FirstOrDefault(e =>
            (string?)e.Attribute(xamlNamespace + "Name") == "AdvancedAxesText");
        if ((string?)advancedAxes?.Attribute("TextWrapping") != "Wrap")
            throw new Exception("FAILED: advanced WinMM axis provenance lines must wrap");
        // Live test progress bars need distinct pedal names in the Windows UIA
        // tree. These are WinMM percentages, not raw ADC, physical travel or force.
        // This checks markup only; a real screen reader still needs WPF testing.
        foreach (var (control, accessibleName) in new[]
        {
            ("BrakeProgress", "Brake WinMM input"),
            ("ThrottleProgress", "Throttle WinMM input"),
            ("ClutchProgress", "Clutch WinMM input")
        })
        {
            var bar = markup.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "ProgressBar" &&
                (string?)e.Attribute(xamlNamespace + "Name") == control);
            string? name = (string?)bar?.Attribute("AutomationProperties.Name");
            string? help = (string?)bar?.Attribute("AutomationProperties.HelpText");
            if (name != accessibleName || help is null ||
                !help.Contains("Read-only", StringComparison.OrdinalIgnoreCase) ||
                !help.Contains("raw sensor ADC", StringComparison.Ordinal))
                throw new Exception("FAILED: live WinMM progress bar accessible label or scope: " + control);
        }
        Console.WriteLine("PASS: three live WinMM bars have distinct accessible names and scope (XAML contract)");

        Console.WriteLine("PASS: advanced WinMM axis provenance wraps in diagnostics (hardware-free XAML contract)");

        // Live WinMM readings also display long interrupted-session guidance.
        // Keep it visible on the minimum-width WPF window; markup-only test.
        var liveRaw = markup.Descendants().FirstOrDefault(e =>
            (string?)e.Attribute(xamlNamespace + "Name") == "LiveRaw");
        if ((string?)liveRaw?.Attribute("TextWrapping") != "Wrap")
            throw new Exception("FAILED: WinMM live reading and interrupted-session guidance must wrap");
        Console.WriteLine("PASS: WinMM live reading and interrupted-session guidance wraps (hardware-free XAML contract)");


        string source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        if (!source.Contains(
                "ClutchMapText.Text = ClutchCheck?.IsChecked == true ? MapLabel(PedalKind.Clutch) : \"" +
                optionalClutchLabel + "\";", StringComparison.Ordinal))
            throw new Exception("FAILED: unchecked clutch label must preserve unverified hardware state");
        // The eight-second read-only sweep starts with a settled released pedal.
        // Preserve the guidance after UI copy edits: one slow stroke, a held
        // press, and a settled release. This is markup only, not a pedal test.
        var sweepGuide = notes.FirstOrDefault(s =>
            s!.Contains("Start fully released and wait briefly after clicking Sweep.",
                StringComparison.Ordinal));
        if (sweepGuide is null ||
            !sweepGuide.Contains("press once slowly, hold briefly, release fully, and stay released",
                StringComparison.Ordinal) ||
            !sweepGuide.Contains("Windows joystick values only; not sensor ADC",
                StringComparison.Ordinal))
            throw new Exception("FAILED: WinMM sweep instructions must require a released start, one held stroke, a settled release, and explicit Windows-only scope");
        Console.WriteLine("PASS: WinMM sweep instructions preserve released start, settled release and WinMM-only scope (hardware-free markup contract)");

        // Grayscale/color-vision-friendly sweep legend: distinguish the three
        // WinMM traces by line pattern as well as color. Markup-only contract;
        // Windows WPF rendering and physical pedal behavior remain untested.
        foreach (var (name, expectedPattern) in new[]
        {
            ("BrakeSweepLine", (string?)null),
            ("ThrottleSweepLine", "5,3"),
            ("ClutchSweepLine", "1,3")
        })
        {
            var trace = markup.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "Polyline" &&
                (string?)e.Attribute(xamlNamespace + "Name") == name);
            if (trace is null ||
                (string?)trace.Attribute("StrokeDashArray") != expectedPattern)
                throw new Exception("FAILED: distinct WinMM sweep line pattern: " + name);
        }
        if (!notes.Any(s => s is not null &&
                s.Contains("Brake (solid red)", StringComparison.Ordinal) &&
                s.Contains("Throttle (dashed green)", StringComparison.Ordinal) &&
                s.Contains("Clutch (dotted blue if installed)", StringComparison.Ordinal)))
            throw new Exception("FAILED: WinMM sweep legend must name line patterns");
        Console.WriteLine("PASS: WinMM sweep traces distinguish pedals by pattern and legend (XAML contract)");

        Console.WriteLine("PASS: optional clutch UI does not infer physical absence (hardware-free source contract)");
        Console.WriteLine("PASS: WinMM idle-check scope is explicit in WPF markup (hardware-free C#)");
    }
}
