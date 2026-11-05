using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MecaCalibrationWizard;

internal sealed record SavedPedal(PedalKind Role, PedalCalibrationResult Capture, uint AppliedFull);
internal sealed record PedalSettings(int Version, string Scope, DateTime SavedUtc,
    WinmmJoystick.Device Controller, SavedPedal[] Pedals);

// #JSON_SETTINGS: only this application's input mappings are persisted. Windows
// capability identity is a compatibility check, not proof of physical USB identity.
// Loading requires explicit user confirmation and a fresh bounded input sample.
internal static class PedalSettingsFile
{
    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MECA Pedal Setup", "settings.json");
    private const string Scope = "MECA_APP_PREVIEW_ONLY";
    // #RESTORE_VIEW: preserve the old profile, but remove it from the startup
    // location after native restore. Its named mappings are not in that backup.
    internal static void ArchiveAfterWindowsRestore(string settingsPath, string archivePath)
    {
        if (!File.Exists(settingsPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(archivePath))!);
        File.Move(settingsPath, archivePath); // No overwrite; failure preserves the original.
    }
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    internal static PedalSettings Capture(WinmmJoystick.Device device, PedalSetup setup)
    {
        if (!setup.Complete || setup.Failure.Length != 0)
            throw new InvalidDataException("Complete a valid pedal setup before saving.");
        var saved = new PedalSettings(1, Scope, DateTime.UtcNow, device,
            setup.Results.Select(p => new SavedPedal(p.Key, p.Value, setup.MappingFor(p.Key).Full)).ToArray());
        Validate(saved);
        return saved;
    }
    internal static void Validate(PedalSettings saved)
    {
        if (saved is null || saved.Version != 1 || saved.Scope != Scope || saved.Controller is not { } device ||
            string.IsNullOrWhiteSpace(device.Name) || device.Min is not { Length: 6 } || device.Max is not { Length: 6 } ||
            saved.Pedals is not { Length: 2 or 3 } || saved.Pedals.Any(p => p is null || p.Capture?.Mapping is null))
            throw new InvalidDataException("Unsupported or incomplete MECA settings file.");
        if (Enumerable.Range(0, 6).Any(a => device.Min[a] >= device.Max[a]) ||
            (device.AssumedAxisRanges is not null && device.AssumedAxisRanges.Length != 6))
            throw new InvalidDataException("Invalid controller ranges.");
        var roles = saved.Pedals.Select(p => p.Role).OrderBy(p => p).ToArray();
        if (!roles.SequenceEqual(saved.Pedals.Length == 3
            ? new[] { PedalKind.Brake, PedalKind.Throttle, PedalKind.Clutch }
            : new[] { PedalKind.Brake, PedalKind.Throttle }) ||
            saved.Pedals.Select(p => p.Capture.Mapping.Axis).Distinct().Count() != saved.Pedals.Length)
            throw new InvalidDataException("Pedal roles or axes are duplicated or missing.");
        foreach (var pedal in saved.Pedals)
        {
            var map = pedal.Capture.Mapping;
            if (map.Axis is < 0 or > 5) throw new InvalidDataException("Invalid pedal axis.");
            double span = Math.Abs((double)map.Full - map.Rest);
            double adjusted = Math.Abs((double)pedal.AppliedFull - map.Rest);
            double range = (double)device.Max[map.Axis] - device.Min[map.Axis];
            if (map.Rest < device.Min[map.Axis] || map.Rest > device.Max[map.Axis] ||
                map.Full < device.Min[map.Axis] || map.Full > device.Max[map.Axis] ||
                pedal.AppliedFull < device.Min[map.Axis] || pedal.AppliedFull > device.Max[map.Axis] ||
                span < range * .04 || adjusted < range * .04 ||
                Math.Sign((double)map.Full - map.Rest) != Math.Sign((double)pedal.AppliedFull - map.Rest) ||
                !double.IsFinite(map.Confidence) || map.Confidence < 0 || map.Confidence > 1 ||
                (pedal.Role != PedalKind.Brake && pedal.AppliedFull != map.Full) ||
                (pedal.AppliedFull != map.Full && adjusted < Math.Ceiling(range * .05)))
                throw new InvalidDataException("Invalid pedal endpoints or adjustment.");
        }
    }
    internal static bool Matches(PedalSettings saved, WinmmJoystick.Device current)
    {
        var d = saved.Controller;
        return d.Id == current.Id && d.Name == current.Name && d.BackendIdentity == current.BackendIdentity &&
            d.ManufacturerId == current.ManufacturerId && d.ProductId == current.ProductId &&
            d.Min.SequenceEqual(current.Min) && d.Max.SequenceEqual(current.Max) &&
            (d.AssumedAxisRanges ?? new bool[6]).SequenceEqual(current.AssumedAxisRanges ?? new bool[6]);
    }
    private static PedalSettings Parse(byte[] bytes)
    {
        var saved = JsonSerializer.Deserialize<PedalSettings>(bytes, Json)
            ?? throw new InvalidDataException("Empty settings file.");
        Validate(saved);
        return saved;
    }
    private static byte[] ReadBytes(string path)
    {
        // Bound allocation before parsing, including files that grow during reading.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("Settings file is too large.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
    internal static PedalSettings Load(string path) => Parse(ReadBytes(path));
    internal static void Save(string path, PedalSettings saved)
    {
        Validate(saved);
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(saved, Json));
    }
    internal static void Backup(string source, string destination)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a separate backup file.");
        var bytes = ReadBytes(source);
        Parse(bytes); // A corrupt file must not be labelled a settings backup.
        WriteAtomic(destination, bytes); // Exact saved bytes, never unsaved preview values.
    }
    internal static void WriteAtomic(string path, byte[] bytes)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            // Publish only the completed file; never truncate an existing setting.
            if (File.Exists(full)) File.Replace(temporary, full, null);
            else File.Move(temporary, full);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
