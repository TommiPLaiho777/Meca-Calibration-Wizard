using System.IO;
using MecaCalibrationWizard;

static class SettingsFileChecks
{
    internal static void Run(WinmmJoystick.Device device, PedalSetup setup)
    {
        string root = Path.Combine(Environment.GetEnvironmentVariable("MECA_TEST_SCRATCH")
            ?? throw new Exception("Set MECA_TEST_SCRATCH to a disposable workspace directory."), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json"), backup = Path.Combine(root, "backup.json");
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Refuses(Action action, string message)
        { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException) { return; } throw new Exception(message); }
        try
        {
            var captured = PedalSettingsFile.Capture(device, setup);
            PedalSettingsFile.Save(path, captured);
            var loaded = PedalSettingsFile.Load(path);
            var restored = PedalSetup.FromSaved(device, loaded, DateTime.UtcNow);
            foreach (var role in setup.Results.Keys)
            {
                Check(restored.MappingFor(role) == setup.MappingFor(role), "Applied mapping changed on JSON round trip");
                Check(restored.Results[role].Mapping == setup.Results[role].Mapping, "Original capture changed");
            }
            Check(restored.Complete && Math.Abs(restored.BrakeScalePercent - setup.BrakeScalePercent) < 1e-10,
                "Restored endpoint scale changed");
            var original = File.ReadAllBytes(path);
            setup.AdjustBrake(1);
            PedalSettingsFile.Backup(path, backup);
            Check(original.SequenceEqual(File.ReadAllBytes(backup)), "Backup included unsaved adjustments or changed bytes");
            Refuses(() => PedalSettingsFile.Backup(path, path), "Backup overwrote active settings");
            Refuses(() => PedalSetup.FromSaved(device with { Id = device.Id + 1 }, loaded, DateTime.UtcNow), "Controller slot mismatch accepted");
            Refuses(() => PedalSetup.FromSaved(device with { Max = Enumerable.Repeat(60000u, 6).ToArray() }, loaded, DateTime.UtcNow), "Changed ranges accepted");
            Refuses(() => PedalSettingsFile.Save(path, captured with { Version = 99 }), "Unknown schema accepted");
            var duplicate = captured.Pedals.Select(_ => captured.Pedals[0]).ToArray();
            Refuses(() => PedalSettingsFile.Save(path, captured with { Pedals = duplicate }), "Duplicated roles/axes accepted");
            var invalid = captured.Pedals.ToArray();
            invalid[0] = invalid[0] with { AppliedFull = uint.MaxValue };
            Refuses(() => PedalSettingsFile.Save(path, captured with { Pedals = invalid }), "Out-of-range endpoint accepted");
            Check(original.SequenceEqual(File.ReadAllBytes(path)), "Rejected settings changed earlier file");
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Refuses(() => PedalSettingsFile.Save(path, captured), "Locked replacement unexpectedly succeeded");
            Check(original.SequenceEqual(File.ReadAllBytes(path)), "Failed replacement lost previous settings");
            PedalSettingsFile.Save(path, captured with { SavedUtc = captured.SavedUtc.AddSeconds(1) });
            Check(PedalSettingsFile.Load(path).SavedUtc != captured.SavedUtc, "Replacement failed");
            Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "Temporary file leaked");
            File.WriteAllText(path, "{broken JSON");
            Refuses(() => PedalSettingsFile.Backup(path, backup), "Corrupt source labelled a backup");
            Check(original.SequenceEqual(File.ReadAllBytes(backup)), "Failed backup destroyed earlier backup");
            Console.WriteLine("PASS: app JSON round trip, preserved capture/adjustment, exact saved backup, identity/range/schema refusal and atomic replacement failure/retry");
        }
        finally { Directory.Delete(root, true); }
    }
}
