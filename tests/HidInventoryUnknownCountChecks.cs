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
        // Equal overall totals can conceal replacement or reconfiguration.
        // This is not evidence of a particular controller being swapped.
        var swappedBefore = HidInterfaceInventory.FromPaths(new[]
        {
            "hid#VID_1111&PID_0001#PRIVATE_BEFORE"
        });
        var swappedAfter = HidInterfaceInventory.FromPaths(new[]
        {
            "hid#VID_2222&PID_0002#PRIVATE_AFTER"
        });
        var swapped = HidInterfaceInventory.Compare(swappedBefore, swappedAfter);
        if (swapped.PreviousInterfaceCount != swapped.CurrentInterfaceCount ||
            swapped.Changes.Count != 2 ||
            !swapped.Summary.Contains("Total HID interface count is unchanged") ||
            !swapped.Summary.Contains("does not identify which physical devices changed") ||
            unchanged.Summary.Contains("VID/PID group counts changed"))
            throw new Exception("FAILED: unchanged HID total obscured grouped changes");
        var swappedJson = JsonSerializer.Serialize(swapped);
        if (swappedJson.Contains("PRIVATE_") || swappedJson.Contains("DevicePath") ||
            swappedJson.Contains("hid#"))
            throw new Exception("FAILED: equal-total comparison leaked interface identity");
        // Identified VID/PID count changes cannot explain simultaneous changes
        // among HID interfaces without usable VID/PID metadata.
        var mixedAfter = HidInterfaceInventory.FromPaths(new[]
        {
            "hid#VID_1234&PID_ABCD#B", "hid#VID_4321&PID_DCBA#PRIVATE_INSTANCE",
            "hid#no_vid#B", "hid#no_vid#C"
        });
        var mixed = HidInterfaceInventory.Compare(before, mixedAfter);
        if (mixed.Changes.Count != 1 || mixed.PreviousUnidentifiedCount != 1 ||
            mixed.CurrentUnidentifiedCount != 2 ||
            !mixed.Summary.Contains("Unidentified HID interface counts also changed") ||
            !mixed.Summary.Contains("cannot be attributed to a specific device or WinMM controller"))
            throw new Exception("FAILED: mixed HID changes overstate device attribution");
        var mixedJson = JsonSerializer.Serialize(mixed);
        if (mixedJson.Contains("PRIVATE_INSTANCE") || mixedJson.Contains("DevicePath") ||
            mixedJson.Contains("hid#"))
            throw new Exception("FAILED: mixed HID comparison leaked private interface data");
        Console.WriteLine("PASS: unidentified-only and mixed HID count guidance and path privacy");
    }
}
