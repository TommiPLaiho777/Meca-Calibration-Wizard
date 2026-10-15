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

    internal sealed record Device(uint Id, string Name, uint[] Min, uint[] Max)
    {
        public override string ToString() => $"{Name}  (Joystick {Id})";
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
                ? caps.ProductName : $"USB joystick {id}";
            uint[] min = ok ? new[] { caps.XMin, caps.YMin, caps.ZMin, caps.RMin, caps.UMin, caps.VMin }
                            : new uint[6];
            uint[] max = ok ? new[] { caps.XMax, caps.YMax, caps.ZMax, caps.RMax, caps.UMax, caps.VMax }
                            : Enumerable.Repeat(65535u, 6).ToArray();
            for (int axis = 0; axis < 6; axis++)
            {
                if (max[axis] <= min[axis]) { min[axis] = 0; max[axis] = 65535; }
            }
            found.Add(new Device(id, name, min, max));
        }
        return found;
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
