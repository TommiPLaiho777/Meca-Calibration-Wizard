using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Synthetic, hardware-free regression: reject contradictory count comparisons.
internal static class HidComparisonInvariantChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var current = new HidInventorySnapshot(true, "synthetic", 2, 0,
            new[] { new HidInterfaceGroup(0x1111, 0x2222, 2) });
        var stable = HidInterfaceInventory.Compare(current, current);

        void Reject(HidInventoryComparison candidate)
        {
            try
            {
                _ = ReadOnlyHidReport.Create(current, candidate, DateTime.UnixEpoch);
            }
            catch (ArgumentException)
            {
                return;
            }
            throw new Exception("FAILED: contradictory HID comparison was exported");
        }

        Reject(stable with { PreviousInterfaceCount = -1 });
        Reject(stable with { PreviousInterfaceCount = 3 });
        Reject(stable with { PreviousUnidentifiedCount = -1 });
        Reject(stable with { PreviousUnidentifiedCount = 3 });
        Reject(stable with { Changes = new[]
        {
            new HidInventoryCountChange(0x1111, 0x2222, 2, 2)
        } });
        Reject(stable with { Changes = new[]
        {
            new HidInventoryCountChange(0x1111, 0x2222, 1, 2),
            new HidInventoryCountChange(0x1111, 0x2222, 1, 2)
        } });
        Reject(stable with { Changes = new[]
        {
            new HidInventoryCountChange(0x1111, 0x2222, -1, 2)
        } });

        // A previous scan with an unidentified interface is legitimate.
        var before = new HidInventorySnapshot(true, "synthetic", 2, 1,
            new[] { new HidInterfaceGroup(0x1111, 0x2222, 1) });
        _ = ReadOnlyHidReport.Create(current,
            HidInterfaceInventory.Compare(before, current), DateTime.UnixEpoch);
        // The WinMM+HID JSON path must enforce the same metadata invariants.
        // An absent or failed optional scan is valid if no comparison exists.
        var failed = new HidInventorySnapshot(false, "unavailable", 0, 0,
            Array.Empty<HidInterfaceGroup>());
        ReadOnlyHidReport.ValidateOptionalMetadata(null, null);
        ReadOnlyHidReport.ValidateOptionalMetadata(failed, null);
        ReadOnlyHidReport.ValidateOptionalMetadata(current, stable);

        void RejectWinmmMetadata(HidInventorySnapshot? inventory, HidInventoryComparison? candidate)
        {
            try
            {
                ReadOnlyHidReport.ValidateOptionalMetadata(inventory, candidate);
            }
            catch (ArgumentException)
            {
                return;
            }
            throw new Exception("FAILED: WinMM+HID export accepted contradictory metadata");
        }
        RejectWinmmMetadata(null, stable);
        RejectWinmmMetadata(failed, stable);
        RejectWinmmMetadata(current, stable with { PreviousInterfaceCount = 3 });
        RejectWinmmMetadata(current with { InterfaceCount = 3 }, null);
        RejectWinmmMetadata(current with { Groups = new[]
        {
            new HidInterfaceGroup(0x9999, 0x2222, 2)
        } }, stable);

        Console.WriteLine("PASS: HID comparison count invariants and optional WinMM metadata validation");
    }
}
