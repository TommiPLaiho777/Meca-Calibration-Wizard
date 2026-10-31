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
        string source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        if (!source.Contains(
                "ClutchMapText.Text = ClutchCheck?.IsChecked == true ? MapLabel(PedalKind.Clutch) : \\"" +
                optionalClutchLabel + "\\";", StringComparison.Ordinal))
            throw new Exception("FAILED: unchecked clutch label must preserve unverified hardware state");
        Console.WriteLine("PASS: optional clutch UI does not infer physical absence (hardware-free source contract)");
        Console.WriteLine("PASS: WinMM idle-check scope is explicit in WPF markup (hardware-free C#)");
    }
}
