using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MecaCalibrationWizard;

// Synthetic paths only: no HID device is opened.
internal static class HidInventoryUnknownCountChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var before = HidInterfaceInventory.FromPaths(new[]
        {
            "hid#VID_1234&PID_ABCD#A", "hid#no_vid#A"
        });
        var after = HidInterfaceInventory.FromPaths(new[]
        {
            "hid#VID_1234&PID_ABCD#B", "hid#no_vid#B", "hid#no_vid#C"
        });
        var diff = HidInterfaceInventory.Compare(before, after);
        if (diff.Changes.Count != 0 || diff.PreviousUnidentifiedCount != 1 ||
            diff.CurrentUnidentifiedCount != 2 ||
            !diff.Summary.Contains("Only unidentified HID interface counts changed") ||
            !diff.Summary.Contains("cannot be identified or matched to WinMM"))
            throw new Exception("FAILED: unidentified-only changes must not imply a match");
        var json = JsonSerializer.Serialize(diff);
        if (json.Contains("hid#") || json.Contains("DevicePath"))
            throw new Exception("FAILED: inventory comparison leaked a device path");
        var unchanged = HidInterfaceInventory.Compare(before, before);
        if (!unchanged.Summary.Contains("individual interfaces may still have changed"))
            throw new Exception("FAILED: equal counts do not establish device identity");
        Console.WriteLine("PASS: unidentified-only HID guidance and path privacy");
    }
}
