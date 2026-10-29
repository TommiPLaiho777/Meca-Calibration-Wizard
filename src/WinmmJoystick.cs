using System.Runtime.InteropServices;

namespace MecaCalibrationWizard;

// Read-only legacy Windows joystick interface. It requires no replacement driver.
// NOTE: WinMM axes X,Y,Z,R,U,V are NOT guaranteed to correspond to DIView's
// Rx,Ry,Rz labels. Capture each physical pedal to establish the mapping.
internal static class WinmmJoystick
{
    internal static readonly string[] AxisNames = { "X", "Y", "Z", "R", "U", "V" };
    private const uint JoyReturnAll = 0xFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct JoyInfoEx
    {
        public uint Size, Flags, X, Y, Z, R, U, V;
        public uint Buttons, ButtonNumber, Pov, Reserved1, Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JoyCaps
    {
        public ushort Mid, Pid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ProductName;
        public uint XMin, XMax, YMin, YMax, ZMin, ZMax;
        public uint NumButtons, PeriodMin, PeriodMax;
        public uint RMin, RMax, UMin, UMax, VMin, VMax;
        public uint Caps, MaxAxes, NumAxes, MaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegistryKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string OemVxd;
    }

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint joyGetNumDevs();

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint joyGetPosEx(uint joyId, ref JoyInfoEx info);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "joyGetDevCapsW")]
    private static extern uint joyGetDevCaps(uint joyId, ref JoyCaps caps, uint cbjc);

    internal sealed record Device(uint Id, string Name, uint[] Min, uint[] Max,
        ushort? ManufacturerId = null, ushort? ProductId = null, bool[]? AssumedAxisRanges = null)
    {
        public override string ToString() => $"{Name}  (Joystick {Id})";
        // WinMM capability IDs are NOT proof of USB VID/PID or a specific HID interface.
        // A 0..65535 fallback range is not evidence that joyGetDevCaps reported it.
        public string WinmmIdsSummary
        {
            get
            {
                var ids = ManufacturerId is { } mid && ProductId is { } pid
                    ? $"WinMM manufacturer 0x{mid:X4}, product 0x{pid:X4} (not verified USB VID/PID)"
                    : "WinMM manufacturer/product IDs unavailable";
                var assumed = AssumedAxisRanges;
                if (assumed is not { Length: 6 } || !assumed.Any(x => x)) return ids;
                var axes = string.Join(", ", Enumerable.Range(0, 6)
                    .Where(i => assumed[i]).Select(i => AxisNames[i]));
                return $"{ids} · assumed WinMM axis range 0–65535 (capabilities unavailable/invalid): {axes}";
            }
        }
    }

    // A product name or WinMM ID is not proof of physical USB identity.
    // Avoid silently choosing the first of multiple MECA-labelled controllers:
    // the user must explicitly choose one before any pedal capture.
    // An explicit prior selection may be retained by WinMM ID only as a UI hint;
    // Refresh still invalidates all measurements and requires fresh capture.
    internal static Device? SelectSuggestedDevice(IReadOnlyList<Device> devices, uint? previousId)
    {
        if (previousId.HasValue)
        {
            var previous = devices.FirstOrDefault(d => d.Id == previousId.Value);
            if (previous is not null) return previous;
        }
        var matches = devices.Where(d =>
            d.Name.Contains("MECA", StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (matches.Length == 1) return matches[0];
        return devices.Count == 1 ? devices[0] : null;
    }

    internal static IReadOnlyList<Device> FindDevices()
    {
        var found = new List<Device>();
        var count = Math.Min(joyGetNumDevs(), 16u);
        for (uint id = 0; id < count; id++)
        {
            if (!TryRead(id, out _)) continue;
            var caps = new JoyCaps();
            var ok = joyGetDevCaps(id, ref caps, (uint)Marshal.SizeOf<JoyCaps>()) == 0;
            var name = ok && !string.IsNullOrWhiteSpace(caps.ProductName)
                ? caps.ProductName : $"WinMM controller {id} (name unavailable)";
            uint[] min = ok ? new[] { caps.XMin, caps.YMin, caps.ZMin, caps.RMin, caps.UMin, caps.VMin }
                            : new uint[6];
            uint[] max = ok ? new[] { caps.XMax, caps.YMax, caps.ZMax, caps.RMax, caps.UMax, caps.VMax }
                            : Enumerable.Repeat(65535u, 6).ToArray();
            var assumedRanges = new bool[6];
            for (int axis = 0; axis < 6; axis++)
            {
                if (!ok || max[axis] <= min[axis])
                {
                    // Record provenance; do not silently present fallback limits
                    // as values actually reported by the WinMM driver.
                    assumedRanges[axis] = true;
                    min[axis] = 0;
                    max[axis] = 65535;
                }
            }
            found.Add(new Device(id, name, min, max,
                ok ? caps.Mid : null, ok ? caps.Pid : null, assumedRanges));
        }
        return found;
    }

    // Validate every WinMM axis before the WPF preview consumes a sample.
    // An impossible value on even an unmapped axis invalidates this session's
    // provenance; it must not silently accompany a valid-looking pedal sweep.
    internal static bool IsSampleWithinAxisBounds(Device device, IReadOnlyList<uint>? reading)
    {
        // Reject missing samples and malformed capabilities before accepting
        // a WinMM session. A zero-width axis is not a usable reported range;
        // FindDevices normally substitutes an explicitly labelled fallback.
        if (reading is null || reading.Count != AxisNames.Length ||
            device.Min.Length != AxisNames.Length || device.Max.Length != AxisNames.Length)
            return false;
        for (int axis = 0; axis < AxisNames.Length; axis++)
            if (device.Min[axis] >= device.Max[axis] ||
                reading[axis] < device.Min[axis] || reading[axis] > device.Max[axis])
                return false;
        return true;
    }

    // Read-only diagnostic text: expose the provenance of each WinMM axis
    // range even when the current sample is inside its bounds. A fallback
    // range is not driver-reported and none of these values is sensor ADC.
    internal static string FormatAxisPreview(Device device, IReadOnlyList<uint> sample, int axis)
    {
        if (axis < 0 || axis >= AxisNames.Length ||
            sample.Count != AxisNames.Length ||
            device.Min.Length != AxisNames.Length || device.Max.Length != AxisNames.Length)
            throw new ArgumentException("Invalid WinMM axis preview inputs.");

        string source = device.AssumedAxisRanges is { Length: 6 } assumed
            ? assumed[axis] ? "assumed fallback" : "driver-reported"
            : "unverified";
        return $"{AxisNames[axis],-2}  {sample[axis],7:N0} (0x{sample[axis]:X8})" +
            $" · {source} WinMM range {device.Min[axis]}–{device.Max[axis]} (not sensor ADC)";
    }

    internal static bool TryRead(uint id, out uint[] raw)
    {
        var state = new JoyInfoEx { Size = (uint)Marshal.SizeOf<JoyInfoEx>(), Flags = JoyReturnAll };
        if (joyGetPosEx(id, ref state) == 0)
        {
            raw = new[] { state.X, state.Y, state.Z, state.R, state.U, state.V };
            return true;
        }
        raw = Array.Empty<uint>();
        return false;
    }
}
