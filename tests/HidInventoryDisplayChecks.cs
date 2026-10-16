using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MecaCalibrationWizard;

// Hardware-free regression: Advanced shows only 12 count changes, but
// a larger change must not disappear merely because its VID sorts last.
internal static class HidInventoryDisplayChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var before = HidInterfaceInventory.FromPaths(Array.Empty<string>());
        var paths = Enumerable.Range(0, 14)
            .Select(i => $"hid#VID_{0x1000 + i:X4}&PID_0001#PRIVATE_{i}")
            .Concat(Enumerable.Repeat("hid#VID_FFFE&PID_0001#PRIVATE_HIGH", 5));
        var after = HidInterfaceInventory.FromPaths(paths);
        var comparison = HidInterfaceInventory.Compare(before, after);
        var summary = comparison.Summary;

        // The initial inventory uses the same 12-row preview limit as the
        // comparison; the most numerous group must not be hidden by VID order.
        var inventorySummary = after.Summary;
        if (after.Groups.Count != 15 ||
            !inventorySummary.Contains("VID FFFE / PID 0001: 5 interface(s)") ||
            !inventorySummary.Contains("+ 3 more groups (in JSON)") ||
            inventorySummary.IndexOf("VID FFFE", StringComparison.Ordinal) >
                inventorySummary.IndexOf("VID 1000", StringComparison.Ordinal) ||
            inventorySummary.Contains("VID 100B / PID 0001"))
            throw new Exception("FAILED: largest HID inventory group must remain visible in the 12-row preview");
        using var inventoryJson = JsonDocument.Parse(JsonSerializer.Serialize(after));
        if (inventoryJson.RootElement.GetProperty("Groups").GetArrayLength() != 15 ||
            inventoryJson.RootElement.GetProperty("WinmmAssociationVerified").GetBoolean() ||
            inventoryJson.RootElement.GetRawText().Contains("PRIVATE_"))
            throw new Exception("FAILED: HID inventory preview altered full JSON or leaked identity");

        if (comparison.Changes.Count != 15 ||
            !summary.Contains("VID FFFE / PID 0001: 0 -> 5") ||
            !summary.Contains("+ 3 more changes (in JSON)") ||
            summary.IndexOf("VID FFFE", StringComparison.Ordinal) >
                summary.IndexOf("VID 1000", StringComparison.Ordinal) ||
            summary.Contains("VID 100B / PID 0001"))
            throw new Exception("FAILED: largest HID count change must remain visible in the 12-row preview");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(comparison));
        var rawJson = json.RootElement.GetRawText();
        if (json.RootElement.GetProperty("Changes").GetArrayLength() != 15 ||
            json.RootElement.GetProperty("WinmmAssociationVerified").GetBoolean() ||
            rawJson.Contains("PRIVATE_") ||
            rawJson.Contains("DevicePath"))
            throw new Exception("FAILED: HID comparison JSON lost changes or leaked identity");

        Console.WriteLine("PASS: largest HID count changes shown first; full JSON and privacy preserved");
    }
}
