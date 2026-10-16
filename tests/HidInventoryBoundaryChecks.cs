using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Runs only in the tests assembly (SDK includes tests/*.cs). Hardware-free
// regression for SetupAPI's NUL-terminated device-interface strings.
internal static class HidInventoryBoundaryChecks
{
    [ModuleInitializer]
    internal static void CheckTerminatedPaths()
    {
        string trailing = ((char)0).ToString() + "#VID_9999&PID_8888";
        if (HidInterfaceInventory.TryParseVidPid(trailing, out _, out _))
            throw new Exception("FAILED: HID identifier after terminator was accepted");

        if (!HidInterfaceInventory.TryParseVidPid(
                "hid#VID_1234&PID_ABCD" + trailing, out var vid, out var pid) ||
            vid != 0x1234 || pid != 0xABCD)
            throw new Exception("FAILED: HID identifier before terminator was lost");

        var inventory = HidInterfaceInventory.FromPaths(new[]
        {
            "hid#VID_1234&PID_ABCD" + trailing,
            trailing
        });
        if (inventory.InterfaceCount != 2 || inventory.WithoutVidPidCount != 1 ||
            inventory.Groups.Count != 1 || inventory.Groups[0].InterfaceCount != 1)
            throw new Exception("FAILED: terminated HID inventory aggregation");

        Console.WriteLine("PASS: terminated HID paths and aggregate counts");
    }
}
