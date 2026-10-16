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

        // The instance segment must not be mistaken for a hardware ID.
        foreach (string path in new[]
        {
            @"\\?\hid#col01#VID_9999&PID_8888#PRIVATE_SERIAL",
            @"\\?\hid#MI_00&VID_9999&PID_8888#PRIVATE_SERIAL",
            @"\\?\other#VID_9999&PID_8888#PRIVATE_SERIAL",
            @"\\?\hid#COL01&VID_9999&PID_8888#PRIVATE_SERIAL",
            @"\\?\hid#VID_1234&PID_ABCDmore#PRIVATE_SERIAL"
        })
        {
            if (HidInterfaceInventory.TryParseVidPid(path, out _, out _))
                throw new Exception("FAILED: non-hardware HID path segment was accepted");
        }
        if (!HidInterfaceInventory.TryParseVidPid(
                @"\\?\HiD#vId_1234&pId_abcd&mi_00#PRIVATE_SERIAL", out var mixedVid, out var mixedPid) ||
            mixedVid != 0x1234 || mixedPid != 0xABCD)
            throw new Exception("FAILED: valid mixed-case HID hardware ID was rejected");
        Console.WriteLine("PASS: terminated HID paths, hardware-ID boundaries and aggregate counts");
    }
}
