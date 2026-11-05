using System.Diagnostics;
using System.IO;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MecaCalibrationWizard;

// #MECA_GUI: the window reads one controller and renders the existing setup core.
// All role/axis transitions live in PedalSetup; the GUI never guesses an axis.
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DateTime _epoch = DateTime.UtcNow;
    private WinmmJoystick.Device? _device;
    private WindowsCalibration? _windows;
    private WindowsCalibration? _verification;
    private WindowsCalibrationSnapshot? _verificationExpected;
    private Dictionary<int, (string Label, bool Inverted)> _verificationLabels = new();
    private bool WindowsTarget => _windows is not null && _device?.BackendIdentity == _windows.Descriptor.BackendIdentity;
    private PedalSetup? _setup;
    private bool _invalid;
    private bool _offerSaved = true;
    private TimeSpan _lastPoll;
    private DateTime Now => _epoch + _clock.Elapsed;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += Poll;
        Loaded += (_, _) => { RefreshDevices(); OfferSavedSettings(); _timer.Start(); };
        Closed += (_, _) => { _timer.Stop(); EndVerification(); _windows?.Dispose(); };
    }
    private void Refresh_Click(object sender, RoutedEventArgs e)
    { _offerSaved = false; RefreshDevices(); }
    private void RefreshDevices()
    {
        bool explicitSelection = _invalid;
        _invalid = false;
        _setup = null;
        _windows?.Dispose(); _windows = null;
        var devices = WinmmJoystick.FindDevices().ToList();
        for (int i = 0; i < devices.Count; i++)
        {
            try
            {
                _windows = WindowsCalibration.Open(devices[i].Id, new WindowInteropHelper(this).Handle);
                devices[i] = _windows.Descriptor; break;
            }
            catch (IOException) { /* Unsupported controllers retain preview-only WinMM reads. */ }
        }
        DeviceCombo.ItemsSource = devices;
        DeviceCombo.SelectedItem = explicitSelection ? null : WinmmJoystick.SelectSuggestedDevice(devices, null);
        _device = DeviceCombo.SelectedItem as WinmmJoystick.Device;
        _lastPoll = _clock.Elapsed;
        ResetDisplay();
    }
    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _device = DeviceCombo.SelectedItem as WinmmJoystick.Device;
        _setup = null;
        _lastPoll = _clock.Elapsed;
        ResetDisplay(); // selection cannot clear a latched interrupted session
        if (IsLoaded && !_invalid) OfferSavedSettings();
    }
    private void ClutchCheck_Changed(object sender, RoutedEventArgs e)
    {
        _setup = null;
        ResetDisplay(); // changed pedal set needs a fresh complete setup
    }
    private void ResetDisplay()
    {
        if (StatusText is null) return; // WPF may raise selection during initialization
        EndVerification();
        SaveButton.IsEnabled = false;
        BackupButton.IsEnabled = WindowsTarget && !_invalid;
        RestoreButton.IsEnabled = WindowsTarget && !_invalid;
        Gauges.ItemsSource = null;
        BrakeAdjustmentPanel.Visibility = Visibility.Collapsed;
        StageText.Text = "1 / 3 - IDENTIFY ALL PEDALS";
        PhaseTitle.Text = "Set up your pedals";
        InstructionText.Text = _invalid ? "Input interrupted. Click Refresh and select the controller again."
            : "Start with all pedals released. Follow the named pedal instructions.";
        StatusText.Text = _device?.Name ?? "Select your controller";
        StartButton.IsEnabled = _device is not null && !_invalid;
        StartButton.Content = "Start setup";
    }
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_device is null || _invalid) return;
        EndVerification();
        if (!Read(out _)) return;
        if (_setup?.ReadyToAdjust == true) _setup.BeginAdjustments(Now);
        else _setup = new PedalSetup(_device, ClutchCheck.IsChecked == true, Now);
        BrakeAdjustmentPanel.Visibility = Visibility.Collapsed;
        Gauges.ItemsSource = null;
        StartButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        PhaseTitle.Text = _setup.Heading;
        InstructionText.Text = _setup.Instruction(Now);
        UpdateStage();
    }
    // #SESSION_VALIDITY: disconnect, invalid sample or polling interruption clears
    // every instrument/result. Only Refresh starts a new session. No stale apply.
    private bool Read(out uint[] sample)
    {
        sample = Array.Empty<uint>();
        if (_device is null || _invalid) return false;
        bool read = WindowsTarget ? _windows!.TryRead(out sample) : WinmmJoystick.TryRead(_device.Id, out sample);
        if (read && WinmmJoystick.IsSampleWithinAxisBounds(_device, sample)) return true;
        Invalidate("Input unavailable or outside its reported range. Refresh and identify the pedals again.");
        return false;
    }
    private void Invalidate(string reason)
    {
        _invalid = true;
        _setup?.Invalidate(reason);
        ResetDisplay();
        InstructionText.Text = reason;
    }
    private void Poll(object? sender, EventArgs e)
    {
        var elapsed = _clock.Elapsed;
        bool interrupted = elapsed - _lastPoll > TimeSpan.FromSeconds(.5);
        _lastPoll = elapsed;
        if (_device is null || _invalid) return;
        if ((_setup is not null || _verificationExpected is not null) && interrupted)
        { Invalidate("Input timing interrupted. Refresh and identify the pedals again."); return; }
        if (!Read(out var sample)) return;
        if (_verificationExpected is not null)
        {
            try
            {
                if (_verification is null || !_verification.TryRead(out var cooked))
                    throw new IOException("Calibrated input unavailable. Refresh before retrying.");
                RenderRestoreVerification(_verification.Snapshot(), cooked);
            }
            catch (Exception ex) { FailVerification(ex.Message); }
            return;
        }
        if (_setup is null) return;
        _setup.Observe(sample, Now);
        if (_setup.Failure.Length != 0)
        { Invalidate(_setup.Failure + " Click Refresh to retry."); return; }
        PhaseTitle.Text = _setup.Heading;
        InstructionText.Text = _setup.Instruction(Now);
        UpdateStage();
        if (_setup.Complete)
        {
            Gauges.ItemsSource = _setup.Results.Keys.Select(role =>
            {
                var map = _setup.MappingFor(role);
                return Display(role, map.Percent(sample[map.Axis]), sample[map.Axis]);
            }).ToArray();
            SaveButton.IsEnabled = WindowsTarget;
            BrakeAdjustmentPanel.Visibility = Visibility.Visible;
            BrakeScaleText.Text = $"Brake maximum: {_setup.BrakeScalePercent:0}% of your selected setting";
            LighterButton.IsEnabled = _setup.CanAdjustBrake(-1);
            HeavierButton.IsEnabled = _setup.CanAdjustBrake(1);
            StatusText.Text = "Setup ready · test brake, throttle" + (ClutchCheck.IsChecked == true ? " and clutch" : "");
            StartButton.Content = "Start again";
            StartButton.IsEnabled = true;
        }
        else
        {
            // Identification asks only which pedal moved. Instruments begin in
            // the separate maximum-selection round, after every role is known.
            Gauges.ItemsSource = _setup.Calibrating && _setup.CurrentAxis >= 0
                ? new[] { Display(_setup.CurrentPedal, _setup.PreviewPercent(sample), sample[_setup.CurrentAxis]) } : null;
            StatusText.Text = _setup.ReadyToAdjust ? "Identification complete. No maximums set yet."
                : _setup.Calibrating ? "Press to your preferred maximum · Windows input, not measured force"
                : "Identify the named pedal · one press and release";
            StartButton.IsEnabled = _setup.ReadyToAdjust;
            if (_setup.ReadyToAdjust) StartButton.Content = "Set pedal maximums";
        }
    }
    // #APPLY_WINDOWS: review exact raw-axis endpoints, save the native before-image,
    // acquire configuration mode, write/notify/read back, then save the app profile.
    // The backend restores earlier values on failure; incomplete rollback is explicit.
    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!WindowsTarget || _device is null || _setup?.Complete != true || _invalid) return;
        if (_clock.Elapsed - _lastPoll > TimeSpan.FromSeconds(.5))
        { Invalidate("Input timing interrupted. Refresh and identify the pedals again."); return; }
        if (!Read(out _)) return;
        bool applied = false;
        _timer.Stop();
        try
        {
            RequireGamesClosed();
            var before = _windows!.Snapshot();
            var after = _windows.Plan(_setup);
            string changes = string.Join("\n", _setup.Results.Keys.Select(role =>
            {
                var a = after.Axes.Single(x => x.Axis == _setup.MappingFor(role).Axis);
                return $"{role}: raw minimum {a.Min}, midpoint {a.Center}, maximum {a.Max}";
            }));
            if (MessageBox.Show(this, "Apply Windows calibration to MECA CUP1?\nClose all games first.\n\n" + changes +
                "\n\nA Windows calibration backup is saved before any change. This affects applications using Windows DirectInput calibration; games using raw input may ignore it.",
                "Apply Windows calibration", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            RequireGamesClosed();
            if (!Read(out _)) return;
            string backup = AutomaticBackupPath();
            _windows.Apply(new WindowInteropHelper(this).Handle, before, after, backup);
            applied = true;
            PedalSettingsFile.Save(PedalSettingsFile.DefaultPath, PedalSettingsFile.Capture(_device, _setup));
            MessageBox.Show(this, "Windows calibration applied and read back successfully. App settings saved.\nPrevious Windows calibration: " + backup +
                "\nRestart the game and check its pedal input before driving.", "MECA Windows calibration");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, (applied ? "Windows calibration was applied, but app JSON saving failed.\n" : "") + ex.Message,
                "MECA calibration result", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _lastPoll = _clock.Elapsed; _timer.Start(); }
    }
    private static string AutomaticBackupPath() => Path.Combine(Path.GetDirectoryName(PedalSettingsFile.DefaultPath)!,
        "Windows backups", $"MECA-Windows-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
    private static void RequireGamesClosed()
    {
        foreach (string name in new[] { "AC2-Win64-Shipping", "acs", "acs_x86", "AssettoCorsaEVO", "iRacingSim64DX11", "rFactor2", "AMS2", "LMU" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process) throw new IOException("Close the running game before applying Windows calibration: " + name);
    }
    private void BackupSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!WindowsTarget || _invalid || !Read(out _)) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        { Title = "Back up current Windows pedal calibration", Filter = "JSON backup (*.json)|*.json",
          FileName = $"MECA-Windows-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        try
        {
            if (dialog.ShowDialog(this) == true)
            {
                if (!Read(out _)) return;
                if (string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(PedalSettingsFile.DefaultPath), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Choose a separate Windows backup file, not the app settings file.");
                WindowsCalibration.SaveBackup(dialog.FileName, _windows!.Snapshot());
                MessageBox.Show(this, "Current Windows calibration backed up. This is separate from the app's preview JSON.", "MECA Windows backup");
            }
        }
        catch (Exception ex) { SettingsError(ex); }
        finally { _lastPoll = _clock.Elapsed; }
    }
    private void RestoreSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!WindowsTarget || _invalid || !Read(out _)) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Restore Windows calibration - choose a MECA-Windows backup", Filter = "Windows calibration backup (*.json)|*.json" };
        _timer.Stop();
        try
        {
            if (dialog.ShowDialog(this) != true) return;
            RequireGamesClosed();
            var saved = WindowsCalibration.LoadBackup(dialog.FileName);
            var before = _windows!.Snapshot();
            if (MessageBox.Show(this, "Restore this Windows calibration backup to the same MECA CUP1 controller?\nCurrent calibration will be backed up first.",
                "Restore Windows calibration", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            RequireGamesClosed(); if (!Read(out _)) return;
            var labels = _setup?.Complete == true ? _setup.Results.Keys.ToDictionary(
                role => _setup.MappingFor(role).Axis,
                role => (role.ToString().ToUpperInvariant(), _setup.MappingFor(role).Full < _setup.MappingFor(role).Rest))
                : new Dictionary<int, (string, bool)>();
            EndVerification();
            _windows.Apply(new WindowInteropHelper(this).Handle, before, saved, AutomaticBackupPath());
            ClearRestoredPreview();
            string archive = Path.Combine(Path.GetDirectoryName(PedalSettingsFile.DefaultPath)!, "Windows backups",
                $"MECA-app-before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            string profileMessage;
            try
            {
                PedalSettingsFile.ArchiveAfterWindowsRestore(PedalSettingsFile.DefaultPath, archive);
                profileMessage = "Previous app settings, if present, were archived so they will not reload at startup.";
            }
            catch (Exception ex)
            {
                profileMessage = "Previous app settings could not be archived: " + ex.Message +
                    "\nDo not load those older app settings at startup; they describe the previous preview.";
            }
            try
            {
                _verification = WindowsCalibration.Open(_device!.Id, new WindowInteropHelper(this).Handle, cooked: true);
                _verificationExpected = saved;
                _verificationLabels = labels;
                if (!_verification.TryRead(out var cooked)) throw new IOException("Calibrated input unavailable.");
                RenderRestoreVerification(_verification.Snapshot(), cooked);
            }
            catch (Exception ex) { FailVerification(ex.Message); }
            MessageBox.Show(this, "Windows calibration restored. See the verification result and live Windows input in the main window. No further Apply is needed.\n\n" +
                profileMessage, "MECA Windows restore");
        }
        catch (Exception ex) { SettingsError(ex); }
        finally { _lastPoll = _clock.Elapsed; _timer.Start(); }
    }
    // #RESTORE_VIEW: RAW gauges use app mappings, not Windows calibration.
    // A native backup has no pedal roles: discard stale preview/Apply state
    // only after verified restore, and do not offer the old profile this session.
    private void ClearRestoredPreview()
    {
        _setup = null;
        _offerSaved = false;
        ResetDisplay();
        PhaseTitle.Text = "Windows calibration restored";
        InstructionText.Text = "Previous preview cleared. Start setup only to choose new pedal maximums.";
        StatusText.Text = "Windows restore complete. No further Apply needed. Restart the game before testing.";
    }
    // #RESTORE_TEST: use the fresh COOKED reader, never old preview endpoints.
    // Role/axis/direction metadata may survive an identified same-device session;
    // without it show honest HID-axis names, not guessed physical pedal roles.
    private void EndVerification()
    {
        _verification?.Dispose(); _verification = null;
        _verificationExpected = null;
        _verificationLabels.Clear();
    }
    private void FailVerification(string reason)
    {
        EndVerification();
        Gauges.ItemsSource = null;
        SaveButton.IsEnabled = false;
        PhaseTitle.Text = "Restore verification unavailable / failed";
        InstructionText.Text = reason;
        StatusText.Text = "Do not treat this as a verified restore. No further Apply needed. Refresh to reconnect.";
    }
    private void RenderRestoreVerification(WindowsCalibrationSnapshot observed, uint[] cooked)
    {
        if (_verificationExpected is null || !CalibrationCommit.SameValues(_verificationExpected, observed))
            throw new IOException("Windows calibration differs from the selected backup or controller identity changed.");
        if (cooked.Length != 6 || observed.Axes.Any(a => cooked[a.Axis] > 65535))
            throw new IOException("Calibrated input is outside its verified range.");
        Gauges.ItemsSource = observed.Axes.OrderBy(a => a.Axis).Select(a =>
        {
            bool known = _verificationLabels.TryGetValue(a.Axis, out var role);
            double percent = 100.0 * cooked[a.Axis] / 65535;
            if (known && role.Inverted) percent = 100 - percent;
            return new { Label = known ? role.Label : new[] { "X", "Y", "Z", "X ROTATION", "Y ROTATION", "Z ROTATION" }[a.Axis],
                Value = $"{percent:0}%", Reading = $"Windows calibrated input: {cooked[a.Axis]}",
                Percent = percent, Angle = -135 + percent * 2.7 };
        }).ToArray();
        StageText.Text = "RESTORE / TEST WINDOWS INPUT";
        PhaseTitle.Text = "Windows readback matches backup";
        InstructionText.Text = "Press and release each pedal to check the restored response. Axis names are shown where pedal roles are unknown.";
        StatusText.Text = "Exact calibration match · live Windows calibrated input · No further Apply needed. Games using raw input may differ.";
        SaveButton.IsEnabled = false;
        BrakeAdjustmentPanel.Visibility = Visibility.Collapsed;
        StartButton.Content = "Set up new maximums";
    }
    private void OfferSavedSettings()
    {
        if (!_offerSaved || _device is null || !System.IO.File.Exists(PedalSettingsFile.DefaultPath)) return;
        try
        {
            var saved = PedalSettingsFile.Load(PedalSettingsFile.DefaultPath);
            if (!PedalSettingsFile.Matches(saved, _device)) return;
            _offerSaved = false;
            if (MessageBox.Show(this, "Load your saved MECA app settings?\nConfirm this is the same physical pedal controller.\nWindows identifiers alone cannot verify that.",
                "Saved pedal settings", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes || !Read(out _)) return;
            ClutchCheck.IsChecked = saved.Pedals.Length == 3;
            _setup = PedalSetup.FromSaved(_device, saved, Now);
        }
        catch (Exception ex) { SettingsError(ex); }
        finally { _lastPoll = _clock.Elapsed; }
    }
    private void SettingsError(Exception ex) => MessageBox.Show(this, ex.Message,
        "MECA settings could not be saved or loaded", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void AdjustBrake_Click(object sender, RoutedEventArgs e)
    {
        if (_invalid || _setup?.Complete != true || !Read(out _)) return;
        _setup.AdjustBrake((string)((Button)sender).Tag == "1" ? 1 : -1);
    }
    private void UpdateStage() => StageText.Text = _setup?.Stage switch
    {
        SetupStage.Adjustment => "2 / 3 - SET YOUR PREFERRED MAXIMUMS",
        SetupStage.Complete => "3 / 3 - TEST EACH PEDAL",
        _ => "1 / 3 - IDENTIFY ALL PEDALS"
    };
    private object Display(PedalKind kind, double percent, uint raw) => new
    {
        Label = kind.ToString().ToUpperInvariant(),
        Value = $"{percent:0}%", Reading = WindowsTarget ? $"Raw input: {raw}" : $"Windows input: {raw}", Percent = percent, Angle = -135 + percent * 2.7
    };
}
