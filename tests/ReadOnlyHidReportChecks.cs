using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MecaCalibrationWizard;

// Hardware-free check for exporting a HID inventory without selecting a
// WinMM controller. Synthetic paths must never appear in the JSON.
internal static class ReadOnlyHidReportChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var inventory = HidInterfaceInventory.FromPaths(new[]
        {
            @"\\?\hid#VID_1DD2&PID_22D2#PRIVATE_SERIAL_001#{GUID}",
            @"\\?\hid#VID_1DD2&PID_22D2#PRIVATE_SERIAL_002#{GUID}"
        });
        var unavailable = new HidInventorySnapshot(false, "failed", 0, 0,
            Array.Empty<HidInterfaceGroup>());
        if (ReadOnlyHidReport.CanExport(false, null) ||
            ReadOnlyHidReport.CanExport(false, unavailable) ||
            !ReadOnlyHidReport.CanExport(false, inventory) ||
            !ReadOnlyHidReport.CanExport(true, null))
            throw new Exception("FAILED: HID-only export eligibility");

        // Safety gates: a failed WinMM session cannot export cached readings,
        // and neither WinMM nor HID-only reports can export mid-measurement.
        // A genuinely HID-only snapshot remains independent of WinMM state.
        if (ReadOnlyHidReport.CanExport(true, inventory, winmmSessionInvalid: true) ||
            ReadOnlyHidReport.CanExport(true, null, measurementInProgress: true) ||
            ReadOnlyHidReport.CanExport(false, inventory, measurementInProgress: true) ||
            !ReadOnlyHidReport.CanExport(false, inventory, winmmSessionInvalid: true))
            throw new Exception("FAILED: stale or in-progress WinMM/HID export gate");

        // Controller-only JSON is intentional but requires explicit UI consent.
        // Any actual pedal, signal, sweep, or HID metadata must bypass that prompt.
        if (!ReadOnlyHidReport.IsControllerOnlyExport(true, 0, false, false, 0, null) ||
            ReadOnlyHidReport.IsControllerOnlyExport(false, 0, false, false, 0, null) ||
            ReadOnlyHidReport.IsControllerOnlyExport(true, 1, false, false, 0, null) ||
            ReadOnlyHidReport.IsControllerOnlyExport(true, 0, true, false, 0, null) ||
            ReadOnlyHidReport.IsControllerOnlyExport(true, 0, false, true, 0, null) ||
            ReadOnlyHidReport.IsControllerOnlyExport(true, 0, false, false, 1, null) ||
            ReadOnlyHidReport.IsControllerOnlyExport(true, 0, false, false, 0, inventory))
            throw new Exception("FAILED: controller-only export consent classification");

        try
        {
            _ = ReadOnlyHidReport.Create(unavailable, null, DateTime.UnixEpoch);
            throw new Exception("FAILED: incomplete HID scan was exported");
        }
        catch (ArgumentException) { }

        // Synthetic invalid inventories must fail closed before JSON export.
        void RejectMalformed(HidInventorySnapshot malformed)
        {
            try
            {
                _ = ReadOnlyHidReport.Create(malformed, null, DateTime.UnixEpoch);
            }
            catch (ArgumentException)
            {
                return;
            }
            throw new Exception("FAILED: inconsistent HID inventory was exported");
        }

        RejectMalformed(inventory with { InterfaceCount = 3 });
        RejectMalformed(inventory with { WithoutVidPidCount = -1 });
        RejectMalformed(inventory with { Groups = new[]
        {
            new HidInterfaceGroup(0x1DD2, 0x22D2, 1),
            new HidInterfaceGroup(0x1DD2, 0x22D2, 1)
        } });
        RejectMalformed(inventory with { Groups = new[]
        {
            new HidInterfaceGroup(0x1DD2, 0x22D2, -1)
        } });
        // A valid, successful scan can legitimately contain zero interfaces.
        _ = ReadOnlyHidReport.Create(
            HidInterfaceInventory.FromPaths(Array.Empty<string>()), null, DateTime.UnixEpoch);

        // A previous comparison must not silently be paired with a newer
        // or different HID inventory in an exported JSON report.
        void RejectStale(HidInventoryComparison stale)
        {
            try
            {
                _ = ReadOnlyHidReport.Create(inventory, stale, DateTime.UnixEpoch);
            }
            catch (ArgumentException)
            {
                return;
            }
            throw new Exception("FAILED: stale HID comparison was exported");
        }

        var added = HidInterfaceInventory.FromPaths(new[]
        {
            "hid#VID_1DD2&PID_22D2#A",
            "hid#VID_1DD2&PID_22D2#B",
            "hid#VID_1DD2&PID_22D2#C"
        });
        RejectStale(HidInterfaceInventory.Compare(inventory, added));

        var unidentified = HidInterfaceInventory.FromPaths(new[]
        {
            "hid#VID_1DD2&PID_22D2#A",
            "hid#no-vid-pid#B"
        });
        RejectStale(HidInterfaceInventory.Compare(inventory, unidentified));

        var swapped = HidInterfaceInventory.FromPaths(new[]
        {
            "hid#VID_1DD2&PID_22D2#A",
            "hid#VID_AAAA&PID_BBBB#B"
        });
        RejectStale(HidInterfaceInventory.Compare(inventory, swapped));
        // Equal totals and an empty change list do not make a comparison
        // from a different VID/PID inventory current.
        RejectStale(HidInterfaceInventory.Compare(swapped, swapped));
        // A comparison whose second scan is this inventory remains valid.
        _ = ReadOnlyHidReport.Create(inventory,
            HidInterfaceInventory.Compare(swapped, inventory), DateTime.UnixEpoch);

        var diff = HidInterfaceInventory.Compare(inventory, inventory);
        var report = ReadOnlyHidReport.Create(inventory, diff, DateTime.UnixEpoch);
        string json = JsonSerializer.Serialize(report, report.GetType());
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        if (root.GetProperty("Device").ValueKind != JsonValueKind.Null ||
            root.GetProperty("Pedals").EnumerateObject().Any() ||
            root.GetProperty("Sweeps").EnumerateObject().Any() ||
            root.GetProperty("SignalHealth").ValueKind != JsonValueKind.Null ||
            root.GetProperty("NoiseCheckRawSpan").ValueKind != JsonValueKind.Null ||
            root.GetProperty("Status").GetString() != "READ_ONLY_PREVIEW_NOT_APPLIED_TO_WINDOWS" ||
            root.GetProperty("InputSource").GetString() !=
                "WINDOWS_SETUPAPI_HID_INTERFACE_METADATA_NOT_SENSOR_ADC" ||
            root.GetProperty("HidInventory").GetProperty("InterfaceCount").GetInt32() != 2 ||
            root.GetProperty("HidInventoryComparison").GetProperty("Changes").GetArrayLength() != 0 ||
            root.GetProperty("WinmmAssociationVerified").GetBoolean() ||
            root.GetProperty("UncalibratedSensorInputVerified").GetBoolean() ||
            json.Contains("PRIVATE_SERIAL_", StringComparison.Ordinal) ||
            json.Contains("DevicePath", StringComparison.Ordinal) ||
            json.Contains("CurrentGroupCounts", StringComparison.Ordinal))
            throw new Exception("FAILED: HID-only JSON must not claim WinMM identity or leak paths");

        // The WinMM+HID WPF exporter is source-checked here; Windows CI
        // separately compiles it. Legacy *Raw keys remain for compatibility,
        // but explicitly named WinMM endpoints prevent an ADC interpretation.
        var windowSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        int pedalStart = windowSource.IndexOf("Pedals = _mappings.ToDictionary(", StringComparison.Ordinal);
        int pedalEnd = windowSource.IndexOf("NoiseCheckRawSpan = _lastNoise", pedalStart, StringComparison.Ordinal);
        if (pedalStart < 0 || pedalEnd <= pedalStart)
            throw new Exception("FAILED: WinMM pedal JSON export block missing");
        string pedalExport = windowSource[pedalStart..pedalEnd];
        if (!pedalExport.Contains("RestRaw = p.Value.Rest") ||
            !pedalExport.Contains("FullRaw = p.Value.Full") ||
            !pedalExport.Contains("RestWinmm = p.Value.Rest") ||
            !pedalExport.Contains("FullWinmm = p.Value.Full") ||
            !pedalExport.Contains("legacy key: Windows WinMM, not sensor ADC"))
            throw new Exception("FAILED: WinMM endpoint provenance or legacy JSON compatibility");
        Console.WriteLine("PASS: explicit WinMM pedal endpoint keys retain legacy export compatibility (WPF source contract)");

        Console.WriteLine("PASS: HID-only export eligibility, stale comparison rejection, JSON provenance and privacy");
    }
}
