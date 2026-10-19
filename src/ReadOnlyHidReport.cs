using System;

namespace MecaCalibrationWizard;

// HID-only exports are metadata snapshots. Never claim WinMM device identity,
// raw ADC access, or a pedal calibration when no WinMM controller was selected.
internal static class ReadOnlyHidReport
{
    internal static bool CanExport(bool hasWinmmController, HidInventorySnapshot? inventory) =>
        hasWinmmController || inventory is { Available: true };

    internal static object Create(
        HidInventorySnapshot inventory, HidInventoryComparison? comparison, DateTime exportedAtUtc)
    {
        if (!inventory.Available)
            throw new ArgumentException("Only a complete HID inventory may be exported.", nameof(inventory));

        // Successful metadata scans must have internally consistent totals.
        // Do not export impossible counts or duplicate VID/PID groups.
        if (inventory.InterfaceCount < 0 || inventory.WithoutVidPidCount < 0 ||
            inventory.Groups.Any(g => g.InterfaceCount <= 0) ||
            inventory.Groups.Select(g => (g.VendorId, g.ProductId)).Distinct().Count() !=
                inventory.Groups.Count ||
            inventory.Groups.Sum(g => (long)g.InterfaceCount) +
                inventory.WithoutVidPidCount != inventory.InterfaceCount)
            throw new ArgumentException("HID inventory counts are inconsistent.", nameof(inventory));

        // Do not attach a comparison captured for another scan to this export.
        // Count-only metadata cannot prove physical identity, but it must at
        // least describe the current grouped inventory consistently.
        if (comparison is not null)
        {
            var currentCounts = inventory.Groups.ToDictionary(
                g => (g.VendorId, g.ProductId), g => g.InterfaceCount);
            // Verify the previous scan's totals using count-only differences.
            long previousGroupedCount =
                (long)comparison.CurrentInterfaceCount - comparison.CurrentUnidentifiedCount +
                comparison.Changes.Sum(change => (long)change.PreviousCount - change.CurrentCount);
            if (comparison.PreviousInterfaceCount < 0 ||
                comparison.PreviousUnidentifiedCount < 0 ||
                comparison.PreviousUnidentifiedCount > comparison.PreviousInterfaceCount ||
                comparison.Changes.Any(change =>
                    change.PreviousCount < 0 || change.CurrentCount < 0 ||
                    change.PreviousCount == change.CurrentCount) ||
                comparison.Changes.Select(change => (change.VendorId, change.ProductId))
                    .Distinct().Count() != comparison.Changes.Count ||
                previousGroupedCount + comparison.PreviousUnidentifiedCount !=
                    comparison.PreviousInterfaceCount)
                throw new ArgumentException(
                    "HID comparison counts are inconsistent.", nameof(comparison));

            if (comparison.CurrentGroupCounts is null ||
                !comparison.CurrentGroupCounts
                    .OrderBy(g => g.VendorId).ThenBy(g => g.ProductId)
                    .SequenceEqual(inventory.Groups
                        .OrderBy(g => g.VendorId).ThenBy(g => g.ProductId)) ||
                comparison.CurrentInterfaceCount != inventory.InterfaceCount ||
                comparison.CurrentUnidentifiedCount != inventory.WithoutVidPidCount ||
                comparison.Changes.Any(change =>
                    currentCounts.GetValueOrDefault((change.VendorId, change.ProductId)) !=
                    change.CurrentCount))
                throw new ArgumentException(
                    "HID comparison does not match the current inventory.", nameof(comparison));
        }

        return new
        {
            App = "MECA Calibration Wizard v0.2.3",
            ExportedAtUtc = exportedAtUtc,
            Status = "READ_ONLY_PREVIEW_NOT_APPLIED_TO_WINDOWS",
            Device = (object?)null,
            Pedals = new Dictionary<string, object>(),
            NoiseCheckRawSpan = (object?)null,
            SignalHealth = (object?)null,
            InputSource = "WINDOWS_SETUPAPI_HID_INTERFACE_METADATA_NOT_SENSOR_ADC",
            HidInventory = inventory,
            HidInventoryComparison = comparison,
            Sweeps = new Dictionary<string, object>(),
            WinmmAssociationVerified = false,
            UncalibratedSensorInputVerified = false,
            Note = "HID metadata covers all present HID interfaces, not a verified MECA or WinMM match. No raw sensor readings, calibration or driver changes."
        };
    }
}
