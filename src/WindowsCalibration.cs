using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MecaCalibrationWizard;

internal sealed record AxisCalibration(int Axis, int ObjectId, int Min, int Center, int Max);
internal sealed record WindowsCalibrationSnapshot(int Version, string Scope, Guid Instance,
    string InterfacePath, string Name, AxisCalibration[] Axes);

// #WINDOWS_CALIBRATION: public DirectInput SDK only. Raw mode/data format are
// local to this connection. Persistent calibration writes happen ONLY in Apply.
// Each native object and format allocation is owned until Dispose; no driver,
// firmware, guessed registry branch or virtual device is installed or changed.
internal sealed class WindowsCalibration : IDisposable
{
    [DllImport("dinput8.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DirectInput8Create(nint module, uint version, ref Guid iid, out nint input, nint outer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Callback(nint item, nint context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumDevices(nint self, uint type, Callback callback, nint context, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumObjects(nint self, Callback callback, nint context, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateDevice(nint self, ref Guid instance, out nint device, nint outer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Query(nint self, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Property(nint self, nint property, nint header);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Simple(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Cooperative(nint self, nint window, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int BufferCall(nint self, nint buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int StateCall(nint self, uint size, nint buffer);
    [StructLayout(LayoutKind.Sequential)] private struct ObjectFormat { public nint Guid; public uint Offset, Type, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct DataFormat { public uint Size, ObjectSize, Flags, DataSize, Count; public nint Objects; }
    private nint _input, _device;
    private bool _cooked;
    private readonly List<nint> _allocations = new();
    private readonly List<AxisCalibration> _axes = new();
    private readonly List<Guid> _axisTypes = new();
    internal Guid Instance { get; private set; }
    internal string InterfacePath { get; private set; } = "";
    internal string Name { get; private set; } = "";
    internal WinmmJoystick.Device Descriptor { get; private set; } = null!;
    private static T Method<T>(nint obj, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));
    private static void Require(int hr, string operation)
    { if (hr < 0) throw new IOException($"{operation} failed (0x{unchecked((uint)hr):X8})."); }
    private nint Allocate(int bytes)
    { nint p = Marshal.AllocHGlobal(bytes); _allocations.Add(p); Marshal.Copy(new byte[bytes], 0, p, bytes); return p; }
    private int[] PropertyValues(int property, int values, int objectId = 0, int how = 0, int[]? write = null)
    {
        int size = 16 + values * 4; nint b = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, b, size);
            Marshal.WriteInt32(b, size); Marshal.WriteInt32(b, 4, 16);
            Marshal.WriteInt32(b, 8, objectId); Marshal.WriteInt32(b, 12, how);
            if (write is not null) for (int i = 0; i < values; i++) Marshal.WriteInt32(b, 16 + i * 4, write[i]);
            Require(Method<Property>(_device, write is null ? 5 : 6)(_device, (nint)property, b),
                write is null ? "Read DirectInput property" : "Set DirectInput property");
            return Enumerable.Range(0, values).Select(i => Marshal.ReadInt32(b, 16 + i * 4)).ToArray();
        }
        finally { Marshal.FreeHGlobal(b); }
    }
    private string ReadPath()
    {
        nint b = Marshal.AllocHGlobal(552);
        try
        {
            Marshal.Copy(new byte[552], 0, b, 552); Marshal.WriteInt32(b, 552); Marshal.WriteInt32(b, 4, 16);
            Require(Method<Property>(_device, 5)(_device, (nint)12, b), "Read HID interface path");
            return Marshal.PtrToStringUni(b + 32) ?? throw new IOException("Missing HID path.");
        }
        finally { Marshal.FreeHGlobal(b); }
    }
    internal static WindowsCalibration Open(uint joystickId, nint owner, bool cooked = false)
    {
        var result = new WindowsCalibration { _cooked = cooked };
        try { result.Initialize(joystickId, owner); return result; }
        catch { result.Dispose(); throw; }
    }
    private void Initialize(uint joystickId, nint owner)
    {
        var iid = new Guid("BF798031-483A-4DA2-AA99-5D64ED369700");
        Require(DirectInput8Create(GetModuleHandle(null), 0x800, ref iid, out _input, 0), "Create DirectInput");
        Exception? failure = null; int count = 0, matches = 0;
        Callback callback = (item, _) =>
        {
            nint device = 0;
            try
            {
                if (++count > 32 || Marshal.ReadInt32(item) < 1100) throw new IOException("Unsupported device inventory.");
                var instance = Marshal.PtrToStructure<Guid>(item + 4);
                Require(Method<CreateDevice>(_input, 3)(_input, ref instance, out device, 0), "Create controller connection");
                _device = device;
                uint id;
                try { id = unchecked((uint)PropertyValues(15, 1)[0]); }
                catch (IOException) { return 1; }
                if (id != joystickId) return 1;
                string path = ReadPath();
                // Initial supported target: the connected CUP1 HID family observed
                // in the owner's read-only probe. Never choose merely by list order.
                string name = Marshal.PtrToStringUni(item + 40)?.Trim() ?? "";
                if (!path.Contains("vid_1dd2&pid_22d2", StringComparison.OrdinalIgnoreCase) || name != "MECA CUP1") return 1;
                if (++matches != 1) throw new IOException("Ambiguous pedal controller.");
                Instance = instance; InterfacePath = path; Name = name;
                var axisFailure = (Exception?)null;
                Callback axis = (obj, __) =>
                {
                    try
                    {
                        if (_axes.Count >= 6 || Marshal.ReadInt32(obj) < 576) throw new IOException("Unsupported axis inventory.");
                        int usage = (ushort)Marshal.ReadInt16(obj, 566), page = (ushort)Marshal.ReadInt16(obj, 564);
                        if (page != 1 || usage is < 0x30 or > 0x35) throw new IOException("Unsupported pedal axis usage.");
                        int index = usage - 0x30, objectId = Marshal.ReadInt32(obj, 24);
                        if (_axes.Any(a => a.Axis == index)) throw new IOException("Ambiguous axis usage.");
                        var cal = PropertyValues(11, 3, objectId, 2);
                        _axes.Add(new(index, objectId, cal[0], cal[1], cal[2]));
                        _axisTypes.Add(Marshal.PtrToStructure<Guid>(obj + 4));
                        return 1;
                    }
                    catch (Exception ex) { axisFailure = ex; return 0; }
                };
                Require(Method<EnumObjects>(_device, 4)(_device, axis, 0, 3), "Enumerate pedal axes");
                GC.KeepAlive(axis); if (axisFailure is not null) throw axisFailure;
                device = 0; return 0; // retain exactly this matched connection
            }
            catch (Exception ex) { failure = ex; return 0; }
            finally { if (device != 0) { Marshal.Release(device); _device = 0; } }
        };
        Require(Method<EnumDevices>(_input, 4)(_input, 4, callback, 0, 1), "Enumerate controllers");
        GC.KeepAlive(callback); if (failure is not null) throw failure;
        if (_device == 0 || matches != 1 || _axes.Count < 2) throw new IOException("Supported MECA CUP1 controller not found.");
        var min = new uint[6]; var max = Enumerable.Repeat(65535u, 6).ToArray(); var absent = Enumerable.Repeat(true, 6).ToArray();
        foreach (var a in _axes)
        {
            var bounds = PropertyValues(19, 2, a.ObjectId, 2);
            if (bounds[0] < 0 || bounds[1] <= bounds[0]) throw new IOException("Unsupported raw axis range.");
            min[a.Axis] = (uint)bounds[0]; max[a.Axis] = (uint)bounds[1]; absent[a.Axis] = false;
        }
        // The custom format uses HID usage positions, not guessed WinMM X/R labels.
        int objectSize = Marshal.SizeOf<ObjectFormat>(); nint objects = Allocate(objectSize * _axes.Count);
        for (int i = 0; i < _axes.Count; i++)
        {
            nint guid = Allocate(16); Marshal.StructureToPtr(_axisTypes[i], guid, false);
            Marshal.StructureToPtr(new ObjectFormat { Guid = guid, Offset = (uint)_axes[i].Axis * 4,
                Type = (uint)_axes[i].ObjectId }, objects + i * objectSize, false);
        }
        nint format = Allocate(Marshal.SizeOf<DataFormat>());
        Marshal.StructureToPtr(new DataFormat { Size = (uint)Marshal.SizeOf<DataFormat>(), ObjectSize = (uint)objectSize,
            Flags = 1, DataSize = 24, Count = (uint)_axes.Count, Objects = objects }, format, false);
        Require(Method<BufferCall>(_device, 11)(_device, format), "Set local input format");
        // #RESTORE_TEST: a separate connection reads actual COOKED DirectInput.
        // Range/mode are local reader properties, never DIPROP_CALIBRATION writes.
        if (_cooked)
            foreach (var a in _axes)
            {
                PropertyValues(4, 2, a.ObjectId, 2, new[] { 0, 65535 });
                var range = PropertyValues(4, 2, a.ObjectId, 2);
                if (range[0] != 0 || range[1] != 65535) throw new IOException("Calibrated reader range unavailable.");
                min[a.Axis] = 0; max[a.Axis] = 65535;
            }
        PropertyValues(10, 1, write: new[] { _cooked ? 0 : 1 });
        foreach (var axis in _axes)
            if (PropertyValues(10, 1, axis.ObjectId, 2)[0] != (_cooked ? 0 : 1))
                throw new IOException("Uncalibrated input mode could not be verified.");
        Require(Method<Cooperative>(_device, 13)(_device, owner, 10), "Set nonexclusive input access");
        Require(Method<Simple>(_device, 7)(_device), "Acquire raw input");
        string identity = (_cooked ? "DIRECTINPUT_COOKED_V1:" : "DIRECTINPUT_RAW_V1:") + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Instance + "|" + InterfacePath.ToUpperInvariant())));
        Descriptor = new(joystickId, Name, min, max, 0x1dd2, 0x22d2, absent, identity);
    }
    internal bool TryRead(out uint[] sample)
    {
        sample = new uint[6]; nint b = Marshal.AllocHGlobal(24);
        try
        {
            if (ReadPath() != InterfacePath || PropertyValues(15, 1)[0] != Descriptor.Id) return false;
            Require(Method<Simple>(_device, 25)(_device), "Poll raw input");
            Require(Method<StateCall>(_device, 9)(_device, 24, b), "Read raw input");
            foreach (var a in _axes)
            {
                if (PropertyValues(10, 1, a.ObjectId, 2)[0] != (_cooked ? 0 : 1)) return false;
                if (_cooked && !PropertyValues(4, 2, a.ObjectId, 2).SequenceEqual(new[] { 0, 65535 })) return false;
                int value = Marshal.ReadInt32(b, a.Axis * 4);
                if (value < 0) return false;
                sample[a.Axis] = (uint)value;
            }
            return WinmmJoystick.IsSampleWithinAxisBounds(Descriptor, sample);
        }
        catch (IOException) { return false; }
        finally { Marshal.FreeHGlobal(b); }
    }
    internal WindowsCalibrationSnapshot Snapshot()
    {
        if (ReadPath() != InterfacePath) throw new IOException("Controller identity changed.");
        var axes = _axes.Select(a => { var v = PropertyValues(11, 3, a.ObjectId, 2); return a with { Min = v[0], Center = v[1], Max = v[2] }; }).ToArray();
        return new(1, "WINDOWS_DIRECTINPUT_CALIBRATION", Instance, InterfacePath, Name, axes);
    }
    internal WindowsCalibrationSnapshot Plan(PedalSetup setup)
    {
        if (!setup.Complete || setup.Failure.Length != 0 || !TryRead(out _))
            throw new IOException("Complete a valid raw-input setup first.");
        var before = Snapshot();
        var proposed = before.Axes.ToArray();
        foreach (var role in setup.Results.Keys)
        {
            var map = setup.MappingFor(role);
            int i = Array.FindIndex(proposed, a => a.Axis == map.Axis);
            if (i < 0) throw new IOException("Pedal axis is not present on this controller.");
            int min = checked((int)Math.Min(map.Rest, map.Full));
            int max = checked((int)Math.Max(map.Rest, map.Full));
            proposed[i] = proposed[i] with { Min = min, Center = min + (max - min) / 2, Max = max };
        }
        return before with { Axes = proposed };
    }
    internal void Apply(nint owner, WindowsCalibrationSnapshot before, WindowsCalibrationSnapshot after, string backupPath)
    {
        if (_cooked) throw new IOException("The calibrated test reader cannot apply calibration.");
        if (owner == 0) throw new IOException("A live application window is required for configuration.");
        nint config = 0; bool acquired = false;
        try
        {
            var iid = new Guid("EB0D7DFA-1990-4F27-B4D6-EDF2EEC4A44C");
            Require(Method<Query>(_input, 0)(_input, ref iid, out config), "Open Windows configuration interface");
            Require(Method<Cooperative>(config, 5)(config, owner, 9), "Associate configuration window");
            Require(Method<Simple>(config, 3)(config), "Acquire Windows configuration lock");
            acquired = true;
            Require(Method<Simple>(_device, 8)(_device), "Release raw input for configuration");
            CalibrationCommit.Run(before, after, Snapshot,
                a => PropertyValues(11, 3, a.ObjectId, 2, new[] { a.Min, a.Center, a.Max }),
                () => Require(Method<Simple>(config, 6)(config), "Notify calibration change"),
                () => SaveBackup(backupPath, before));
        }
        finally
        {
            if (config != 0)
            {
                try { if (acquired) Require(Method<Simple>(config, 4)(config), "Release configuration lock"); }
                finally { Marshal.Release(config); Require(Method<Simple>(_device, 7)(_device), "Resume raw input"); }
            }
        }
    }
    internal static void SaveBackup(string path, WindowsCalibrationSnapshot snapshot)
    {
        CalibrationCommit.Validate(snapshot);
        PedalSettingsFile.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static WindowsCalibrationSnapshot LoadBackup(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 1024 * 1024) throw new IOException("Backup is too large.");
        try
        {
            // #BACKUP_FORMAT: historical reports contain cooked Windows input,
            // not the original native calibration. Identify their format before
            // strict deserialization so refusal explains the required action.
            // Never convert their endpoints or relax native identity validation.
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Choose a Windows calibration backup created with Back up Windows settings.");
            if (root.TryGetProperty("App", out var app) && app.ValueKind == JsonValueKind.String &&
                app.GetString()?.StartsWith("MECA", StringComparison.Ordinal) == true)
                throw new InvalidDataException("This is an older MECA preview report, not a Windows calibration backup. It cannot restore Windows calibration. Use a MECA-Windows backup created with Back up Windows settings. No Windows settings were changed.");
            if (root.TryGetProperty("Scope", out var scope) && scope.ValueKind == JsonValueKind.String &&
                scope.GetString() == "MECA_APP_PREVIEW_ONLY")
                throw new InvalidDataException("This file contains MECA app settings, not a Windows calibration backup. Use a MECA-Windows backup created with Back up Windows settings. No Windows settings were changed.");
            var snapshot = JsonSerializer.Deserialize<WindowsCalibrationSnapshot>(bytes,
                new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
                ?? throw new InvalidDataException("Empty Windows calibration backup.");
            CalibrationCommit.Validate(snapshot); return snapshot;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("This file is not a supported Windows calibration backup or is damaged. Choose a MECA-Windows backup created with Back up Windows settings. No Windows settings were changed.", ex);
        }
    }

    public void Dispose()
    {
        if (_device != 0) { Method<Simple>(_device, 8)(_device); Marshal.Release(_device); _device = 0; }
        if (_input != 0) { Marshal.Release(_input); _input = 0; }
        foreach (var p in _allocations) Marshal.FreeHGlobal(p); _allocations.Clear();
    }
}
