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
        Console.WriteLine("PASS: WinMM idle-check scope is explicit in WPF markup (hardware-free C#)");
    }
}
