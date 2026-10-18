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

        // Do not attach a comparison captured for another scan to this export.
        // Count-only metadata cannot prove physical identity, but it must at
        // least describe the current grouped inventory consistently.
        if (comparison is not null)
        {
            var currentCounts = inventory.Groups.ToDictionary(
                g => (g.VendorId, g.ProductId), g => g.InterfaceCount);
            if (comparison.CurrentInterfaceCount != inventory.InterfaceCount ||
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
