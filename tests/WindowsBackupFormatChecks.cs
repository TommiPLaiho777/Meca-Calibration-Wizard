using System.IO;
using System.Text.Json;
using MecaCalibrationWizard;

// Exercises the actual file reader only: no DirectInput connection or settings
// mutation. Wrong-format refusal must preserve the user's original report.
static class WindowsBackupFormatChecks
{
    internal static void Run()
    {
        string folder = Path.Combine(Environment.GetEnvironmentVariable("MECA_TEST_SCRATCH")
            ?? throw new Exception("Set MECA_TEST_SCRATCH to a disposable workspace directory."), "format-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "selected.json");
        void Refuse(string json, string explanation)
        {
            File.WriteAllText(path, json);
            var original = File.ReadAllBytes(path);
            try { WindowsCalibration.LoadBackup(path); throw new Exception("Wrong backup accepted"); }
            catch (InvalidDataException ex)
            {
                if (!ex.Message.Contains(explanation, StringComparison.Ordinal) || ex.Message.Contains(".NET"))
                    throw new Exception("Restore refusal did not explain the selected format", ex);
            }
            if (!File.ReadAllBytes(path).SequenceEqual(original)) throw new Exception("Refused file changed");
        }
        try
        {
            Refuse("""{"App":"MECA Calibration Wizard v0.2.3","ExportedAtUtc":"2026-11-03T22:38:09Z","Status":"READ_ONLY_PREVIEW_NOT_APPLIED_TO_WINDOWS","Device":{"Id":0},"Pedals":{}}""", "older MECA preview report");
            Refuse("""{"Scope":"MECA_APP_PREVIEW_ONLY","Version":1}""", "MECA app settings");
            Refuse("{broken}", "not a supported Windows calibration backup");
            Refuse("[]", "Choose a Windows calibration backup");
            var native = new WindowsCalibrationSnapshot(1, "WINDOWS_DIRECTINPUT_CALIBRATION", Guid.NewGuid(),
                "synthetic-device", "MECA CUP1", new[] { new AxisCalibration(4, 1026, 800, 6500, 13000), new AxisCalibration(5, 1282, 5500, 12150, 18800) });
            WindowsCalibration.SaveBackup(path, native);
            var loaded = WindowsCalibration.LoadBackup(path);
            if (loaded.Instance != native.Instance || loaded.InterfacePath != native.InterfacePath || !loaded.Axes.SequenceEqual(native.Axes))
                throw new Exception("Real backup schema did not retain identity and exact values");
            string valid = JsonSerializer.Serialize(native);
            Refuse(valid.Insert(1, "\"Unexpected\":true,"), "not a supported Windows calibration backup");
            Refuse(valid.Replace("\"Scope\":\"WINDOWS_DIRECTINPUT_CALIBRATION\"", "\"Scope\":5"), "not a supported Windows calibration backup");
            Refuse(JsonSerializer.Serialize(native with { Version = 99 }), "Invalid Windows calibration snapshot");
            Refuse(JsonSerializer.Serialize(native with { Axes = new[] { native.Axes[0], native.Axes[0] } }), "Invalid Windows calibration snapshot");
        }
        finally { Directory.Delete(folder, true); }
        Console.WriteLine("PASS: preview/app JSON explained and refused unchanged; corrupt/unknown/type/schema/duplicate-axis refusal; native backup identity/value round trip");
    }
}
