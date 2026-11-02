using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Source-only WPF regression. This never scans HID devices or changes Windows settings.
internal static class HidScanRecoveryMessageChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        string source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        if (!source.Contains(
                "previous is { Available: true } && current.Available",
                StringComparison.Ordinal) ||
            !source.Contains(
                "Comparison reset after an unsuccessful scan. Two successful scans are needed to compare counts.",
                StringComparison.Ordinal) ||
            !source.Contains(
                "current.Available ? \"Rescan & compare\" : \"Scan HID identifiers\"",
                StringComparison.Ordinal))
            throw new Exception(
                "FAILED: unsuccessful HID scan must reset comparison and explain two-scan recovery");

        Console.WriteLine(
            "PASS: HID comparison reset guidance matches scan recovery (WPF source contract)");
    }
}
