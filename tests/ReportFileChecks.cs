using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MecaCalibrationWizard;

// Real disposable filesystem checks; no WPF, controller access or settings.
internal static class ReportFileChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        string root = Environment.GetEnvironmentVariable("MECA_EXPORT_CHECK_ROOT") ?? Path.GetTempPath();
        string folder = Path.Combine(root, "meca-report-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "preview.json");
            const string first = "{\"Note\":\"Read-only ää\"}";
            ReadOnlyHidReport.SaveJson(path, first);
            if (File.ReadAllText(path) != first) throw new Exception("FAILED: new report bytes");
            using (var parsed = JsonDocument.Parse(File.ReadAllBytes(path)))
                if (parsed.RootElement.GetProperty("Note").GetString() != "Read-only ää")
                    throw new Exception("FAILED: report JSON round trip");
            const string next = "{\"Status\":\"READ_ONLY_PREVIEW_NOT_APPLIED_TO_WINDOWS\"}";
            ReadOnlyHidReport.SaveJson(path, next);
            byte[] accepted = File.ReadAllBytes(path);
            if (File.ReadAllText(path) != next) throw new Exception("FAILED: report replacement");

            // Strict UTF-8 encoding fails during staging, after a write begins.
            // Both old and new destination cases must remain unpublished.
            void Refuse(Action save)
            { try { save(); } catch (Exception e) when (e is IOException or System.Text.EncoderFallbackException) { return; }
              throw new Exception("FAILED: invalid or locked report unexpectedly saved"); }
            Refuse(() => ReadOnlyHidReport.SaveJson(path, new string('x', 8192) + "\uD800"));
            if (!File.ReadAllBytes(path).SequenceEqual(accepted)) throw new Exception("FAILED: failed write changed accepted report");
            string absent = Path.Combine(folder, "absent.json");
            Refuse(() => ReadOnlyHidReport.SaveJson(absent, "\uD800"));
            if (File.Exists(absent)) throw new Exception("FAILED: failed new write published output");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Refuse(() => ReadOnlyHidReport.SaveJson(path, first));
            if (!File.ReadAllBytes(path).SequenceEqual(accepted)) throw new Exception("FAILED: locked replacement changed report");
            Refuse(() => ReadOnlyHidReport.SaveJson(Path.Combine(folder, "missing", "out.json"), first));
            if (Directory.Exists(Path.Combine(folder, "missing"))) throw new Exception("FAILED: unexpected directory creation");
            ReadOnlyHidReport.SaveJson(path, first);
            if (File.ReadAllText(path) != first || Directory.GetFiles(folder, ".meca-report-*.tmp").Length != 0)
                throw new Exception("FAILED: retry or staging cleanup");
            Console.WriteLine("PASS: local JSON creation/replacement, interrupted staging preservation, lock refusal, retry and cleanup (actual filesystem, no device)");
        }
        finally { Directory.Delete(folder, true); }
    }
}
