using System;

namespace MecaCalibrationWizard;

// HID-only exports are metadata snapshots. Never claim WinMM device identity,
// raw ADC access, or a pedal calibration when no WinMM controller was selected.
internal static class ReadOnlyHidReport
{
    // #JSON_EXPORT #REPORT_FILE — publish a complete local report in one step.
    // The GUI serializes and validates its current session before calling here.
    // Write a unique sibling file first: a failed write must not truncate the
    // user's previous report. Replace/Move is the only publication step; never
    // fall back to opening the destination after a failed replacement. A file
    // lock or concurrent new destination therefore remains an ordinary refusal.
    // This protects local report files only; it does not back up calibration,
    // guarantee power-loss durability, or write any Windows/device settings.
    internal static void SaveJson(string path, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(json);
        string destination = System.IO.Path.GetFullPath(path);
        string directory = System.IO.Path.GetDirectoryName(destination)!;
        string temporary = System.IO.Path.Combine(directory, $".meca-report-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new System.IO.FileStream(temporary, System.IO.FileMode.CreateNew,
                       System.IO.FileAccess.Write, System.IO.FileShare.None))
            using (var writer = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(false, true)))
                writer.Write(json);

            if (System.IO.File.Exists(destination))
                System.IO.File.Replace(temporary, destination, null);
            else
                System.IO.File.Move(temporary, destination);
        }
        finally
        {
            // Keep the original write/publication exception. A cleanup failure
            // can leave the uniquely named staging file, never a partial report
            // published at the selected destination. No broad directory cleanup.
            try { System.IO.File.Delete(temporary); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // A failed WinMM read invalidates the selected capture session. HID metadata
    // cannot make stale WinMM measurements eligible for export.
    internal static bool CanExport(
        bool hasWinmmController, HidInventorySnapshot? inventory,
        bool winmmSessionInvalid = false, bool measurementInProgress = false) =>
        !measurementInProgress &&
        ((hasWinmmController && !winmmSessionInvalid) ||
         (!hasWinmmController && inventory is { Available: true }));

    // Controller-only exports remain permitted, but the user should explicitly
    // acknowledge when no pedal measurement or successful HID scan is present.
    // A failed scan can be reported as error metadata; it is not a completed inventory.
    internal static bool IsControllerOnlyExport(
        bool hasWinmmController, int pedalCount, bool hasNoiseCheck,
        bool hasSignalHealth, int sweepCount, HidInventorySnapshot? inventory) =>
        hasWinmmController && pedalCount == 0 && !hasNoiseCheck &&
        !hasSignalHealth && sweepCount == 0 && inventory is not { Available: true };

    // Use this for both WinMM+HID and HID-only exports. A failed optional
    // scan can be reported with WinMM data, but never with a comparison.
    internal static void ValidateOptionalMetadata(
        HidInventorySnapshot? inventory, HidInventoryComparison? comparison)
    {
        if (inventory is null || !inventory.Available)
        {
            if (comparison is not null)
                throw new ArgumentException(
                    "HID comparison requires a successful current inventory.", nameof(comparison));
            return;
        }

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

    }

    internal static object Create(
        HidInventorySnapshot inventory, HidInventoryComparison? comparison, DateTime exportedAtUtc)
    {
        if (!inventory.Available)
            throw new ArgumentException("Only a complete HID inventory may be exported.", nameof(inventory));
        ValidateOptionalMetadata(inventory, comparison);

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
