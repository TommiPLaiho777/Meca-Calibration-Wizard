using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MecaCalibrationWizard;

// Inventory only: never opens a HID device handle or reads/writes HID reports.
// Device interface paths are transient; no serial numbers or paths leave this class.
internal sealed record HidInterfaceGroup(ushort VendorId, ushort ProductId, int InterfaceCount);

internal sealed record HidInventorySnapshot(
    bool Available, string Status, int InterfaceCount, int WithoutVidPidCount,
    IReadOnlyList<HidInterfaceGroup> Groups)
{
    public string InputSource => "WINDOWS_SETUPAPI_HID_INTERFACE_METADATA_NOT_SENSOR_ADC";
    public bool WinmmAssociationVerified => false;
    public bool UncalibratedSensorInputVerified => false;

    public string Summary
    {
        get
        {
            if (!Available) return Status;
            var lines = new List<string>
            {
                $"Present HID interfaces: {InterfaceCount} (all devices, NOT matched to WinMM)",
                $"VID/PID unavailable: {WithoutVidPidCount} interface(s)"
            };
            // Prioritize common interface groups in the 12-row display.
            // This does not identify a device or alter the complete JSON.
            lines.AddRange(Groups
                .OrderByDescending(g => g.InterfaceCount)
                .ThenBy(g => g.VendorId).ThenBy(g => g.ProductId)
                .Take(12).Select(g =>
                    $"VID {g.VendorId:X4} / PID {g.ProductId:X4}: {g.InterfaceCount} interface(s)"));
            if (Groups.Count > 12)
                lines.Add($"+ {Groups.Count - 12} more groups (in JSON)");
            lines.Add("Read-only identifiers; no sensor data or pedal association verified.");
            return string.Join("\n", lines);
        }
    }
}

 // Count-only comparison. Never stores device paths or asserts a WinMM/HID match.
internal sealed record HidInventoryCountChange(
    ushort VendorId, ushort ProductId, int PreviousCount, int CurrentCount)
{
    public int Delta => CurrentCount - PreviousCount;
}

internal sealed record HidInventoryComparison(
    int PreviousInterfaceCount, int CurrentInterfaceCount,
    int PreviousUnidentifiedCount, int CurrentUnidentifiedCount,
    IReadOnlyList<HidInventoryCountChange> Changes)
{
    public string InputSource => "WINDOWS_SETUPAPI_HID_INTERFACE_COUNT_DIFFERENCE_NOT_DEVICE_MATCH_NOT_SENSOR_ADC";
    public bool WinmmAssociationVerified => false;
    public bool UncalibratedSensorInputVerified => false;

    public string Summary
    {
        get
        {
            var lines = new List<string>
            {
                $"All HID interfaces: {PreviousInterfaceCount} -> {CurrentInterfaceCount}",
                $"Without VID/PID: {PreviousUnidentifiedCount} -> {CurrentUnidentifiedCount}"
            };
            // Surface the largest count changes first when the UI shows only 12.
            // This is a display priority, not evidence of a particular device.
            lines.AddRange(Changes
                .OrderByDescending(c => Math.Abs((long)c.Delta))
                .ThenBy(c => c.VendorId).ThenBy(c => c.ProductId)
                .Take(12).Select(c =>
                    $"VID {c.VendorId:X4} / PID {c.ProductId:X4}: {c.PreviousCount} -> {c.CurrentCount}"));
            if (Changes.Count > 12)
                lines.Add($"+ {Changes.Count - 12} more changes (in JSON)");
            if (Changes.Count == 0)
            {
                if (PreviousUnidentifiedCount != CurrentUnidentifiedCount)
                    lines.Add("Only unidentified HID interface counts changed; the device cannot be identified or matched to WinMM.");
                else
                    lines.Add("No grouped count changes; individual interfaces may still have changed.");
            }
            else if (PreviousUnidentifiedCount != CurrentUnidentifiedCount)
            {
                lines.Add("Unidentified HID interface counts also changed; these cannot be attributed to a specific device or WinMM controller.");
            }
            // An unchanged aggregate total can conceal different VID/PID groups.
            // Count differences never identify which physical controller changed.
            if (PreviousInterfaceCount == CurrentInterfaceCount && Changes.Count > 0)
                lines.Add("Total HID interface count is unchanged, but VID/PID group counts changed; this does not identify which physical devices changed.");
            lines.Add("Changes cover all HID devices; not a verified MECA or WinMM match.");
            return string.Join("\n", lines);
        }
    }
}

internal static class HidInterfaceInventory
{
    private const uint DigcfPresent = 0x2;
    private const uint DigcfDeviceInterface = 0x10;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;
    private const int MaxInterfaces = 512;
    private static readonly Regex VidPidPattern = new(
        @"^VID_([0-9A-F]{4})&PID_([0-9A-F]{4})(?=$|&)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [DllImport("hid.dll", ExactSpelling = true)]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid classGuid,
        uint memberIndex, ref SpDeviceInterfaceData interfaceData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref SpDeviceInterfaceData interfaceData,
        IntPtr detailData, uint detailDataSize, out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    internal static bool TryParseVidPid(string? devicePath, out ushort vid, out ushort pid)
    {
        vid = pid = 0;
        if (string.IsNullOrEmpty(devicePath)) return false;
        // Ignore trailing native-buffer contents after a path terminator.
        int end = devicePath.IndexOf((char)0);
        if (end >= 0) devicePath = devicePath[..end];
        // A Windows HID interface path has a hardware-ID segment between
        // its first and second '#'. Never parse instance/serial segments.
        int firstSeparator = devicePath.IndexOf('#');
        if (firstSeparator < 0) return false;
        string prefix = devicePath[..firstSeparator];
        if (!prefix.Equals("hid", StringComparison.OrdinalIgnoreCase) &&
            !prefix.Equals(@"\\?\hid", StringComparison.OrdinalIgnoreCase))
            return false;
        int nextSeparator = devicePath.IndexOf('#', firstSeparator + 1);
        string hardwareId = devicePath[(firstSeparator + 1)..
            (nextSeparator < 0 ? devicePath.Length : nextSeparator)];
        var match = VidPidPattern.Match(hardwareId);
        return match.Success &&
               ushort.TryParse(match.Groups[1].Value, NumberStyles.HexNumber,
                   CultureInfo.InvariantCulture, out vid) &&
               ushort.TryParse(match.Groups[2].Value, NumberStyles.HexNumber,
                   CultureInfo.InvariantCulture, out pid);
    }

    // Count identifiers as they arrive; never retain transient interface paths.
    private sealed class InventoryCounter
    {
        private readonly Dictionary<(ushort Vid, ushort Pid), int> _counts = new();
        private int _total;
        private int _unidentified;

        public void AddPath(string? path)
        {
            _total++;
            if (!TryParseVidPid(path, out var vid, out var pid))
            {
                _unidentified++;
                return;
            }
            var key = (vid, pid);
            _counts[key] = _counts.GetValueOrDefault(key) + 1;
        }

        public HidInventorySnapshot Snapshot()
        {
            var groups = _counts.OrderBy(p => p.Key.Vid).ThenBy(p => p.Key.Pid)
                .Select(p => new HidInterfaceGroup(p.Key.Vid, p.Key.Pid, p.Value))
                .ToArray();
            return new(true, "HID interface metadata enumerated; WinMM association unverified.",
                _total, _unidentified, groups);
        }
    }

    // Pure aggregation for hardware-free tests. Never retain or export input paths.
    internal static HidInventorySnapshot FromPaths(IEnumerable<string> paths)
    {
        var counter = new InventoryCounter();
        foreach (string path in paths) counter.AddPath(path);
        return counter.Snapshot();
    }

    // Compare successive complete scans. Equal VID/PID counts cannot prove identity.
    internal static HidInventoryComparison Compare(HidInventorySnapshot previous, HidInventorySnapshot current)
    {
        if (!previous.Available || !current.Available)
            throw new ArgumentException("Only complete HID inventories can be compared.");
        var before = previous.Groups.ToDictionary(g => (g.VendorId, g.ProductId), g => g.InterfaceCount);
        var after = current.Groups.ToDictionary(g => (g.VendorId, g.ProductId), g => g.InterfaceCount);
        var changes = before.Keys.Concat(after.Keys).Distinct()
            .OrderBy(k => k.VendorId).ThenBy(k => k.ProductId)
            .Select(k => new HidInventoryCountChange(
                k.VendorId, k.ProductId, before.GetValueOrDefault(k), after.GetValueOrDefault(k)))
            .Where(c => c.Delta != 0).ToArray();
        return new HidInventoryComparison(
            previous.InterfaceCount, current.InterfaceCount,
            previous.WithoutVidPidCount, current.WithoutVidPidCount, changes);
    }

    private static HidInventorySnapshot Unavailable(string reason) =>
        new(false, reason, 0, 0, Array.Empty<HidInterfaceGroup>());

    // Explicit user action only. SetupAPI reads present interface metadata, not reports.
    internal static HidInventorySnapshot Scan()
    {
        if (!OperatingSystem.IsWindows())
            return Unavailable("HID interface scan is available only on Windows.");
        try
        {
            HidD_GetHidGuid(out var hidGuid);
            IntPtr deviceSet = SetupDiGetClassDevs(
                ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (deviceSet == new IntPtr(-1))
                return Unavailable($"HID enumeration unavailable (Windows error {Marshal.GetLastPInvokeError()}).");
            try
            {
                var counter = new InventoryCounter();
                for (uint index = 0; index < MaxInterfaces; index++)
                {
                    var data = new SpDeviceInterfaceData
                    {
                        Size = (uint)Marshal.SizeOf<SpDeviceInterfaceData>()
                    };
                    if (!SetupDiEnumDeviceInterfaces(
                        deviceSet, IntPtr.Zero, ref hidGuid, index, ref data))
                    {
                        int error = Marshal.GetLastPInvokeError();
                        return error == ErrorNoMoreItems
                            ? counter.Snapshot()
                            : Unavailable($"HID enumeration incomplete (Windows error {error}).");
                    }

                    _ = SetupDiGetDeviceInterfaceDetail(
                        deviceSet, ref data, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);
                    int sizeError = Marshal.GetLastPInvokeError();
                    uint minimumSize = IntPtr.Size == 8 ? 8u : 6u;
                    if (sizeError != ErrorInsufficientBuffer ||
                        requiredSize < minimumSize || requiredSize > 65536)
                        return Unavailable("HID interface detail size unavailable or invalid.");

                    IntPtr detail = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(
                            deviceSet, ref data, detail, requiredSize, out _, IntPtr.Zero))
                            return Unavailable($"HID interface detail unavailable (Windows error {Marshal.GetLastPInvokeError()}).");
                        // WCHAR DevicePath begins at byte offset 4; bound the read
                        // to the allocated native buffer and discard the path.
                        int pathChars = checked((int)((requiredSize - 4u) / 2u));
                        counter.AddPath(Marshal.PtrToStringUni(IntPtr.Add(detail, 4), pathChars));
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
                return Unavailable("More than 512 HID interfaces; inventory not reported.");
            }
            finally
            {
                _ = SetupDiDestroyDeviceInfoList(deviceSet);
            }
        }
        catch (DllNotFoundException)
        {
            return Unavailable("Windows HID enumeration API unavailable.");
        }
        catch (EntryPointNotFoundException)
        {
            return Unavailable("Windows HID enumeration entry point unavailable.");
        }
        catch (BadImageFormatException)
        {
            return Unavailable("Windows HID enumeration architecture mismatch.");
        }
    }
}
