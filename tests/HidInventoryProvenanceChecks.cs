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

        Console.WriteLine("PASS: explicit HID provenance and path privacy");
    }
}
