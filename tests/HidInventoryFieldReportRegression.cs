using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MecaCalibrationWizard;

// Hardware-free regression based on the 2026-10-17 owner-provided Windows
// HID inventory (16 interfaces in five groups). All interface paths below are
// synthetic; no serial numbers, USB handles, or pedal signals are used.
internal static class HidInventoryFieldReportRegression
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var expected = new (ushort Vid, ushort Pid, int Count)[]
        {
            (0x03F0, 0x6241, 2),
            (0x048D, 0x5702, 2),
            (0x1532, 0x0537, 3),
            (0x1A2C, 0x8F03, 8),
            (0x1DD2, 0x22D2, 1)
        };
        string[] firstPaths = expected.SelectMany(group =>
            Enumerable.Range(0, group.Count).Select(i =>
                $"hid#VID_{group.Vid:X4}&PID_{group.Pid:X4}#SYNTHETIC_FIRST_{i}")).ToArray();

        // Different synthetic instance identifiers deliberately preserve the
        // group counts: a count-only rescan must not claim device identity.
        string[] secondPaths = expected.Reverse().SelectMany(group =>
            Enumerable.Range(0, group.Count).Select(i =>
                $"hid#VID_{group.Vid:X4}&PID_{group.Pid:X4}#SYNTHETIC_SECOND_{i}")).ToArray();

        var first = HidInterfaceInventory.FromPaths(firstPaths);
        var second = HidInterfaceInventory.FromPaths(secondPaths);
        var diff = HidInterfaceInventory.Compare(first, second);

        if (!first.Available || first.InterfaceCount != 16 ||
            first.WithoutVidPidCount != 0 || first.Groups.Count != expected.Length ||
            expected.Any(group => !first.Groups.Any(actual =>
                actual.VendorId == group.Vid &&
                actual.ProductId == group.Pid &&
                actual.InterfaceCount == group.Count)))
            throw new Exception("FAILED: 2026-10-17 synthetic HID group-count regression");

        if (diff.PreviousInterfaceCount != 16 || diff.CurrentInterfaceCount != 16 ||
            diff.PreviousUnidentifiedCount != 0 || diff.CurrentUnidentifiedCount != 0 ||
            diff.Changes.Count != 0 ||
            !diff.Summary.Contains("individual interfaces may still have changed",
                StringComparison.Ordinal))
            throw new Exception("FAILED: stable HID group counts imply physical identity");

        // Numerically equal IDs do not establish that WinMM capability IDs
        // identify the same USB HID device (or its uncalibrated sensor).
        if (!first.Groups.Any(group =>
                group.VendorId == 7634 && group.ProductId == 8914) ||
            first.WinmmAssociationVerified || diff.WinmmAssociationVerified ||
            first.UncalibratedSensorInputVerified || diff.UncalibratedSensorInputVerified)
            throw new Exception("FAILED: unverified WinMM/HID association asserted");

        foreach (var report in new[] { first, second }.Select(item => JsonSerializer.Serialize(item))
                     .Concat(new[] { JsonSerializer.Serialize(diff) }))
        {
            using var json = JsonDocument.Parse(report);
            if (report.Contains("SYNTHETIC_FIRST_", StringComparison.Ordinal) ||
                report.Contains("SYNTHETIC_SECOND_", StringComparison.Ordinal) ||
                report.Contains("DevicePath", StringComparison.Ordinal) ||
                json.RootElement.GetProperty("WinmmAssociationVerified").GetBoolean() ||
                json.RootElement.GetProperty("UncalibratedSensorInputVerified").GetBoolean())
                throw new Exception("FAILED: exported HID data leaked paths or claimed raw sensor access");
        }

        Console.WriteLine("PASS: field-report HID group counts, rescan limitations and privacy");
    }
}
