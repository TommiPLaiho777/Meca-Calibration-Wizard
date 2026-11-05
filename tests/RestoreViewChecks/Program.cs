using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MecaCalibrationWizard;

// Actual WPF controls, no window Show/Loaded, controller connection or setter.
// Reflection is confined to this test harness, not the product's native path.
static class RestoreViewChecks
{
    [STAThread]
    static void Main()
    {
        string scratch = Environment.GetEnvironmentVariable("MECA_TEST_SCRATCH")
            ?? throw new Exception("Set MECA_TEST_SCRATCH to a disposable workspace directory.");
        Directory.CreateDirectory(scratch);
        var app = new App();
        app.InitializeComponent(); // Load production brushes; never call Run/OnStartup.
        var window = new MainWindow();
        var type = typeof(MainWindow);
        const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
        T Control<T>(string name) => (T)window.FindName(name);
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        // Simulate a stale setup session without acquiring hardware.
        var setupField = type.GetField("_setup", fields)!;
        var stale = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(setupField.FieldType);
        setupField.SetValue(window, stale);
        Control<ItemsControl>("Gauges").ItemsSource = new[] { "stale brake", "stale throttle" };
        Control<Button>("SaveButton").IsEnabled = true;
        Control<StackPanel>("BrakeAdjustmentPanel").Visibility = Visibility.Visible;
        type.GetMethod("ClearRestoredPreview", fields)!.Invoke(window, null);
        Check(setupField.GetValue(window) is null, "Stale setup survives restore");
        Check(!(bool)type.GetField("_offerSaved", fields)!.GetValue(window)!, "Old profile offered again");
        Check(Control<ItemsControl>("Gauges").ItemsSource is null, "Stale gauges survive restore");
        Check(!Control<Button>("SaveButton").IsEnabled, "Stale Apply remains enabled");
        Check(Control<StackPanel>("BrakeAdjustmentPanel").Visibility == Visibility.Collapsed, "Old brake adjustment visible");
        Check(Control<TextBlock>("PhaseTitle").Text.Contains("restored"), "Restored state missing");
        Check(Control<TextBlock>("StatusText").Text.Contains("No further Apply needed"), "Restore requires misleading Apply");
        // Subsequent polling and a queued Apply must not revive the stale session.
        type.GetMethod("Poll", fields)!.Invoke(window, new object?[] { null, EventArgs.Empty });
        type.GetMethod("SaveSettings_Click", fields)!.Invoke(window, new object?[] { null, new RoutedEventArgs() });
        Check(!Control<Button>("SaveButton").IsEnabled && setupField.GetValue(window) is null, "Stale state revived");

        // Feed actual renderer synthetic Windows COOKED samples and exact readback.
        // No old raw endpoints enter the percentage calculation.
        var snapshotType = type.Assembly.GetType("MecaCalibrationWizard.WindowsCalibrationSnapshot")!;
        string json = """{"Version":1,"Scope":"WINDOWS_DIRECTINPUT_CALIBRATION","Instance":"11111111-1111-1111-1111-111111111111","InterfacePath":"synthetic","Name":"MECA CUP1","Axes":[{"Axis":4,"ObjectId":1026,"Min":44,"Center":6619,"Max":13194},{"Axis":5,"ObjectId":1282,"Min":2765,"Center":11359,"Max":19953}]}""";
        object expected = System.Text.Json.JsonSerializer.Deserialize(json, snapshotType)!;
        type.GetField("_verificationExpected", fields)!.SetValue(window, expected);
        var render = type.GetMethod("RenderRestoreVerification", fields)!;
        uint[] cooked = new uint[6]; cooked[4] = 65535; cooked[5] = 0;
        render.Invoke(window, new object[] { expected, cooked });
        var rows = Control<ItemsControl>("Gauges").ItemsSource.Cast<object>().ToArray();
        object Value(object row, string property) => row.GetType().GetProperty(property)!.GetValue(row)!;
        Check((double)Value(rows[0], "Percent") == 100 && (double)Value(rows[1], "Percent") == 0, "Cooked endpoints not used");
        Check((string)Value(rows[0], "Label") == "Y ROTATION", "Unknown physical role guessed");
        Check(((string)Value(rows[0], "Reading")).Contains("Windows calibrated"), "Input source not explicit");
        var labels = (Dictionary<int, (string Label, bool Inverted)>)type.GetField("_verificationLabels", fields)!.GetValue(window)!;
        labels[4] = ("BRAKE", true);
        render.Invoke(window, new object[] { expected, cooked });
        rows = Control<ItemsControl>("Gauges").ItemsSource.Cast<object>().ToArray();
        Check((string)Value(rows[0], "Label") == "BRAKE" && (double)Value(rows[0], "Percent") == 0, "Identified inverted role not preserved");
        Check(!Control<Button>("SaveButton").IsEnabled && Control<StackPanel>("BrakeAdjustmentPanel").Visibility == Visibility.Collapsed, "Verification permits writes");
        object changed = System.Text.Json.JsonSerializer.Deserialize(json.Replace("13194", "13195"), snapshotType)!;
        try { render.Invoke(window, new object[] { changed, cooked }); throw new Exception("Changed Windows calibration accepted"); }
        catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
        type.GetMethod("FailVerification", fields)!.Invoke(window, new object[] { "readback mismatch" });
        Check(Control<ItemsControl>("Gauges").ItemsSource is null && Control<TextBlock>("PhaseTitle").Text.Contains("failed"), "Mismatch keeps success gauges");
        Check(type.GetField("_verificationExpected", fields)!.GetValue(window) is null, "Failed verification retained");
        Console.WriteLine("PASS restore verification renderer: fresh exact snapshot required; cooked 0/100%; identified direction; unknown-role axis labels; mismatch clears gauges and status; Apply stays disabled.");

        var nativeType = type.Assembly.GetType("MecaCalibrationWizard.WindowsCalibration")!;
        var readerOnly = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(nativeType);
        nativeType.GetField("_cooked", fields)!.SetValue(readerOnly, true);
        try
        {
            nativeType.GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(readerOnly,
                new object[] { (nint)0, expected, expected, "unused" });
            throw new Exception("Verification reader permits calibration Apply");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is IOException io && io.Message.Contains("cannot apply")) { }
        Console.WriteLine("PASS cooked test reader refuses Apply before any native call.");

        var settings = type.Assembly.GetType("MecaCalibrationWizard.PedalSettingsFile")!;
        var archive = settings.GetMethod("ArchiveAfterWindowsRestore", BindingFlags.NonPublic | BindingFlags.Static)!;
        string folder = Path.Combine(scratch, "restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string original = Path.Combine(folder, "settings.json");
            string destination = Path.Combine(folder, "Windows backups", "old-app.json");
            byte[] bytes = "old app profile bytes preserved exactly"u8.ToArray();
            File.WriteAllBytes(original, bytes);
            archive.Invoke(null, new object[] { original, destination });
            Check(!File.Exists(original) && File.ReadAllBytes(destination).SequenceEqual(bytes), "Archive not exact/startup file remains");
            archive.Invoke(null, new object[] { original, destination }); // No saved profile is valid.
            File.WriteAllBytes(original, "newer profile"u8.ToArray());
            try { archive.Invoke(null, new object[] { original, destination }); throw new Exception("Archive overwritten"); }
            catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
            Check(File.ReadAllBytes(destination).SequenceEqual(bytes) && File.ReadAllText(original) == "newer profile", "Archive failure destroyed a profile");
        }
        finally { Directory.Delete(folder, true); }
        window.Close();
        Console.WriteLine("PASS actual WPF restore reset: stale setup/gauges/Apply/brake controls cleared, polling/queued Apply cannot revive; exact app-profile archive, absent-file and collision preservation. No native connection or calibration write.");
    }
}
