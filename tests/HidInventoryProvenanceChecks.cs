using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MecaCalibrationWizard;

// Synthetic identifiers only; no HID handles or reports are accessed.
internal static class HidInventoryProvenanceChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var before = HidInterfaceInventory.FromPaths(new[]
        {
            "hid#VID_1234&PID_ABCD#instance_a"
        });
        var after = HidInterfaceInventory.FromPaths(new[]
        {
            "hid#VID_1234&PID_ABCD#instance_b",
            "hid#VID_1234&PID_ABCD#instance_c"
        });
        using var firstJson = JsonDocument.Parse(JsonSerializer.Serialize(before));
        var first = firstJson.RootElement;
        if (first.GetProperty("WinmmAssociationVerified").GetBoolean() ||
            first.GetProperty("UncalibratedSensorInputVerified").GetBoolean() ||
            !first.GetProperty("InputSource").GetString()!.Contains("NOT_SENSOR_ADC"))
            throw new Exception("FAILED: HID inventory overstates input provenance");

        using var diffJson = JsonDocument.Parse(
            JsonSerializer.Serialize(HidInterfaceInventory.Compare(before, after)));
        var diff = diffJson.RootElement;
        if (diff.GetProperty("WinmmAssociationVerified").GetBoolean() ||
            diff.GetProperty("UncalibratedSensorInputVerified").GetBoolean() ||
            !diff.GetProperty("InputSource").GetString()!.Contains("NOT_DEVICE_MATCH") ||
            !diff.GetProperty("InputSource").GetString()!.Contains("NOT_SENSOR_ADC"))
            throw new Exception("FAILED: HID comparison overstates device association");

        foreach (var json in new[] { firstJson.RootElement.GetRawText(),
                                     diffJson.RootElement.GetRawText() })
            if (json.Contains("instance_a") || json.Contains("instance_b") ||
                json.Contains("instance_c") || json.Contains("DevicePath"))
                throw new Exception("FAILED: HID metadata exported a private interface path");

        // Source-level contract for the WinMM+optional-HID export branch.
        // HID metadata cannot verify a WinMM/USB match or native sensor ADC.
        // This is a hardware-free C# check, not a WPF or pedal execution.
        string wpfSource = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        int reportStart = wpfSource.IndexOf("report = new", StringComparison.Ordinal);
        int sweepsStart = wpfSource.IndexOf("Sweeps = _sweepResults",
            reportStart, StringComparison.Ordinal);
        if (reportStart < 0 || sweepsStart <= reportStart)
            throw new Exception("FAILED: WinMM JSON report branch not found");
        string winmmReport = wpfSource[reportStart..sweepsStart];
        foreach (string required in new[]
        {
            "InputSource = \"WINMM_JOYGETPOSEX_NOT_SENSOR_ADC\"",
            "WinmmIdsSource = \"WINMM_JOYGETDEVCAPSW_NOT_VERIFIED_USB_VID_PID\"",
            "WinmmAssociationVerified = false",
            "UncalibratedSensorInputVerified = false"
        })
            if (!winmmReport.Contains(required, StringComparison.Ordinal))
                throw new Exception("FAILED: WinMM report missing explicit unverified provenance: " + required);
        Console.WriteLine("PASS: WinMM+HID JSON preserves unverified device and sensor provenance (WPF source contract)");

        Console.WriteLine("PASS: explicit HID provenance and path privacy");
    }
}
