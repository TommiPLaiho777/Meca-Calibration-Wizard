using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MecaCalibrationWizard;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Dictionary<PedalKind, PedalMapping> _mappings = new();
    private WinmmJoystick.Device? _device;
    // A read failure may indicate device replacement even if WinMM reuses the ID.
    // Require an explicit refresh and fresh captures before exporting again.
    private bool _winmmSessionInvalid;
    private uint[] _raw = Array.Empty<uint>();
    private GuidedCalibration? _capture;
    private readonly Dictionary<PedalKind, PedalCalibrationResult> _calibrationResults = new();
    private NoiseRun? _noise;
    private Dictionary<string, uint>? _lastNoise;
    private Dictionary<string, SignalHealthStats>? _lastSignalHealth;
    private HidInventorySnapshot? _hidInventory;
    private HidInventoryComparison? _hidInventoryComparison;
    private InputSweepRun? _sweep;
    private PedalKind? _sweepKind;
    private DateTime _sweepStartUtc;
    private readonly Dictionary<PedalKind, InputSweepResult> _sweepResults = new();
    private int _step; // 0=connect, 1=brake, 2=throttle, 3=clutch, 4=test, 5=raw monitor
    private int _returnStep;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += Poll;
        Loaded += (_, _) =>
        {
            RefreshDevices();
            ChangeStep(0);
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private void RefreshDevices()
    {
        _hidInventory = null; // Refresh starts a new explicit comparison sequence.
        _hidInventoryComparison = null;
        if (HidScanButton is not null) HidScanButton.Content = "Scan HID identifiers";
        if (HidInventoryText is not null)
            HidInventoryText.Text = "Not scanned. No HID device paths or serials are exported.";
        var previousId = _device?.Id;
        var devices = WinmmJoystick.FindDevices().ToList();
        DeviceCombo.ItemsSource = devices;
        WinmmJoystick.Device? next = devices.FirstOrDefault(x => previousId.HasValue && x.Id == previousId.Value);
        next ??= devices.FirstOrDefault(x => x.Name.Contains("MECA", StringComparison.OrdinalIgnoreCase))
             ?? (devices.Count == 1 ? devices[0] : null);
        DeviceCombo.SelectedItem = next;
        // WinMM IDs can be reused. SelectionChanged compares IDs, so an
        // unchanged ID must still receive this scan's fresh capabilities.
        // This does not establish physical USB identity.
        _device = next;
        if (next is null)
        {
            _device = null;
            _raw = Array.Empty<uint>();
            _capture = null;
            _noise = null;
            _sweep = null;
            _sweepResults.Clear();
            _mappings.Clear();
            _calibrationResults.Clear();
            _lastSignalHealth = null;
        }
        UpdateMappingLabels();
        UpdateDeviceInfo();
        UpdateWinmmIds();
    }

    private void UpdateWinmmIds()
    {
        if (WinmmIdsText is not null)
            WinmmIdsText.Text = _device?.WinmmIdsSummary ?? "No WinMM controller selected";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        // A reused WinMM ID/name is not proof of the same physical controller.
        // Explicit refresh starts a fresh read-only capture session.
        _winmmSessionInvalid = false;
        // Refresh is a session boundary even when WinMM reuses the same ID.
        // Cancel all in-flight captures before any new sample can complete them.
        _capture = null;
        _noise = null;
        _sweep = null;
        _sweepKind = null;
        _mappings.Clear();
        _calibrationResults.Clear();
        _sweepResults.Clear();
        _lastNoise = null;
        _lastSignalHealth = null;
        _raw = Array.Empty<uint>();
        CaptureButton.IsEnabled = true;
        CheckSignalButton.IsEnabled = true;
        LongSignalButton.IsEnabled = true;
        BrakeSweepButton.IsEnabled = true;
        ThrottleSweepButton.IsEnabled = true;
        ClutchSweepButton.IsEnabled = true;
        SignalText.Text = "Controller refreshed · run signal check again.";
        SweepStatusText.Text = "Controller refreshed · record a new sweep.";
        RefreshDevices();
        ChangeStep(0);
        StepHint.Text = "Controller refreshed · capture pedals again before exporting measurements";
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var next = DeviceCombo.SelectedItem as WinmmJoystick.Device;
        if (next?.Id != _device?.Id)
        {
            _winmmSessionInvalid = false; // deliberate selection begins a fresh session
            _device = next;
            _raw = Array.Empty<uint>();
            _capture = null;
            _noise = null;
            _sweep = null;
            _sweepResults.Clear();
            _lastNoise = null;
            _lastSignalHealth = null;
            _mappings.Clear();
            _calibrationResults.Clear();
            UpdateMappingLabels();
            UpdateDeviceInfo();
            UpdateWinmmIds();
        }
    }

    private void ClutchCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (ClutchCheck is null) return;
        // An unused WinMM axis is not evidence of a physical clutch.
        if (ClutchSweepButton is not null)
            ClutchSweepButton.Visibility = ClutchCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (ClutchSweepLine is not null)
            ClutchSweepLine.Visibility = ClutchCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (ClutchCheck.IsChecked != true)
        {
            // Disabling an optional clutch while its guided capture is running
            // must not let a later WinMM sample resurrect a disabled pedal.
            if (_capture?.Kind == PedalKind.Clutch)
            {
                _capture = null;
                CaptureButton.IsEnabled = true;
                CaptureCounter.Text = "0 / 3 presses";
                CaptureQualityText.Text = "";
                CaptureFooter.Text = "Clutch disabled · capture cancelled. No clutch data saved.";
                if (_step == 3)
                {
                    NextButton.IsEnabled = true;
                    NextButton.Content = "Skip  →";
                }
            }
            if (_sweepKind == PedalKind.Clutch)
            {
                _sweep = null;
                _sweepKind = null;
                BrakeSweepButton.IsEnabled = true;
                ThrottleSweepButton.IsEnabled = true;
                ClutchSweepButton.IsEnabled = true;
                SweepProgress.Value = 0;
                SweepStatusText.Text = "Clutch disabled · sweep cancelled. No clutch sweep saved.";
            }
            _mappings.Remove(PedalKind.Clutch);
            _calibrationResults.Remove(PedalKind.Clutch);
            // A removed pedal must not leave an orphan sweep: JSON export
            // resolves each saved sweep through the active pedal mapping.
            _sweepResults.Remove(PedalKind.Clutch);
            // An earlier signal check may contain a now-disabled clutch.
            // Discard the cached WinMM snapshots instead of exporting stale
            // clutch data under the current pedal selection.
            if (_lastNoise is not null || _lastSignalHealth is not null)
            {
                _lastNoise = null;
                _lastSignalHealth = null;
                if (SignalText is not null)
                    SignalText.Text = "Pedal selection changed · run signal check again.";
            }
        }
        UpdateMappingLabels();
        if (_step == 4)
        {
            ClutchTestArea.Visibility = ClutchCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            UpdateSweepDetails();
        }
    }

    private void UpdateMappingLabels()
    {
        BrakeMapText.Text = MapLabel(PedalKind.Brake);
        ThrottleMapText.Text = MapLabel(PedalKind.Throttle);
        ClutchMapText.Text = ClutchCheck?.IsChecked == true ? MapLabel(PedalKind.Clutch) : "Not installed";
    }

    private string MapLabel(PedalKind kind) => _mappings.TryGetValue(kind, out var mapping)
        ? $"WinMM {WinmmJoystick.AxisNames[mapping.Axis]} · {(_winmmSessionInvalid ? "STALE — refresh required" : "Preview")}"
        : "Not captured";

    // Never leave the last successful pedal position looking like a live value
    // after a WinMM read fails. Preserve the stored measurements for review,
    // but label their mappings stale and keep JSON export blocked.
    private void MarkLiveReadingsUnavailable()
    {
        LiveValue.Text = "—";
        LiveRaw.Text = "WinMM input interrupted · no current reading.";
        CaptureProgress.Value = 0;
        BrakeProgress.Value = 0;
        ThrottleProgress.Value = 0;
        ClutchProgress.Value = 0;
        BrakeValue.Text = "No live input";
        ThrottleValue.Text = "No live input";
        ClutchValue.Text = "No live input";
        SweepProgress.Value = 0;
        UpdateMappingLabels();
    }

    private void UpdateDeviceInfo()
    {
        if (DeviceText is null || StatusText is null) return;
        DeviceText.Text = _device?.Name ?? "No controller selected";
        StatusText.Text = _device is null ? "Select a WinMM controller to preview inputs"
            : "WinMM controller selected · physical USB identity not verified";
    }

    private void ChangeStep(int step)
    {
        _capture = null;
        _noise = null;
        _sweep = null;
        _sweepKind = null;
        CaptureButton.IsEnabled = true;
        BrakeSweepButton.IsEnabled = true;
        ThrottleSweepButton.IsEnabled = true;
        ClutchSweepButton.IsEnabled = true;
        CheckSignalButton.IsEnabled = true;
        LongSignalButton.IsEnabled = true;
        _step = step;
        WelcomeView.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        CaptureView.Visibility = step is >= 1 and <= 3 ? Visibility.Visible : Visibility.Collapsed;
        TestView.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        AdvancedView.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.IsEnabled = step != 0;
        if (step == 0)
        {
            StepLabel.Text = "STEP 1 / CONNECT";
            StepHint.Text = "Select a WinMM controller · physical identity not verified";
            NextButton.Content = "Begin  →";
            NextButton.IsEnabled = _device is not null;
        }
        else if (step is >= 1 and <= 3)
        {
            var kind = StepKind();
            var info = kind switch
            {
                PedalKind.Brake => ("02 / BRAKE", "Brake input preview", "Release, then press to your comfortable maximum three times."),
                PedalKind.Throttle => ("03 / THROTTLE", "Throttle input preview", "Release, press fully three times."),
                _ => ("04 / CLUTCH", "Clutch input preview", "Release, press fully three times.")
            };
            CaptureStepText.Text = info.Item1;
            CaptureTitle.Text = info.Item2;
            CaptureHelp.Text = info.Item3;
            BrakeIcon.Visibility = kind == PedalKind.Brake ? Visibility.Visible : Visibility.Collapsed;
            ThrottleIcon.Visibility = kind == PedalKind.Throttle ? Visibility.Visible : Visibility.Collapsed;
            ClutchIcon.Visibility = kind == PedalKind.Clutch ? Visibility.Visible : Visibility.Collapsed;
            StepLabel.Text = info.Item1;
            StepHint.Text = "Read-only WinMM preview · rest, three presses and repeatability";
            CaptureCounter.Text = _calibrationResults.TryGetValue(kind, out var current)
                ? "3 / 3 presses" : "0 / 3 presses";
            CaptureQualityText.Text = current is null ? "" :
                current.PeakSpreadSummary;
            CaptureFooter.Text = _mappings.ContainsKey(kind) ? "Preview captured. Press Start to measure again."
                : "Release the pedal, then click Start. Wait for the PRESS prompt.";
            NextButton.Content = kind == PedalKind.Clutch && !_mappings.ContainsKey(kind) ? "Skip  →" : "Continue  →";
            NextButton.IsEnabled = kind == PedalKind.Clutch || _mappings.ContainsKey(kind);
        }
        else if (step == 4)
        {
            StepLabel.Text = "LIVE TEST";
            StepHint.Text = "Read-only preview · no changes to Windows";
            NextButton.Content = "Start over  ↻";
            NextButton.IsEnabled = true;
            ClutchTestArea.Visibility = ClutchCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            SignalText.Text = _lastNoise is null ? "Not measured" : FormatNoise();
            SweepStatusText.Text = _sweepResults.Count == 0
                ? "Not measured" : string.Join(" | ", _sweepResults.Select(p => $"{p.Key}: {p.Value.Summary}"));
            SweepProgress.Value = 0;
            UpdateSweepDetails();
            SummaryText.Text = string.Join("   |   ", _calibrationResults.Select(p =>
                $"{p.Key}: {p.Value.PeakSpreadSummary}"));
        }
        else
        {
            StepLabel.Text = "WINMM / HID DIAGNOSTICS";
            StepHint.Text = "WinMM-reported values (not sensor ADC; may be calibrated by Windows)";
            NextButton.Content = "Return  →";
            NextButton.IsEnabled = true;
        }
    }

    private PedalKind StepKind() => _step switch
    {
        1 => PedalKind.Brake,
        2 => PedalKind.Throttle,
        _ => PedalKind.Clutch
    };

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 5) { ChangeStep(_returnStep); return; }
        if (_step == 0)
        {
            if (_winmmSessionInvalid)
            {
                MessageBox.Show(this, "WinMM input was interrupted. Click Refresh to start a new session and capture pedals again.",
                    "Refresh required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_device is null) { MessageBox.Show("Select a WinMM controller first."); return; }
            ChangeStep(1);
        }
        else if (_step == 1 && _mappings.ContainsKey(PedalKind.Brake)) ChangeStep(2);
        else if (_step == 2 && _mappings.ContainsKey(PedalKind.Throttle)) ChangeStep(ClutchCheck.IsChecked == true ? 3 : 4);
        else if (_step == 3) ChangeStep(4);
        else if (_step == 4)
        {
            _mappings.Clear();
            _calibrationResults.Clear();
            _lastNoise = null;
            _lastSignalHealth = null;
            _sweepResults.Clear();
            UpdateMappingLabels();
            ChangeStep(0);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 5) ChangeStep(_returnStep);
        else if (_step == 4) ChangeStep(ClutchCheck.IsChecked == true ? 3 : 2);
        else if (_step > 0) ChangeStep(_step - 1);
    }

    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        _returnStep = _step;
        ChangeStep(5);
    }

    private void ReturnToSetup_Click(object sender, RoutedEventArgs e) => ChangeStep(_returnStep);

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (_winmmSessionInvalid)
        {
            CaptureFooter.Text = "WinMM input interrupted · return to Connect and click Refresh.";
            return;
        }
        if (_device is null)
        {
            CaptureFooter.Text = "Select a WinMM controller before capturing.";
            return;
        }
        // Keep guided capture separate from any active WinMM signal check or sweep.
        // Do not interrupt an in-flight measurement or read a competing sample.
        if (_capture is not null || _noise is not null || _sweep is not null)
        {
            CaptureFooter.Text = "Another WinMM measurement is in progress. Finish or cancel it before capturing a pedal.";
            return;
        }
        if (!WinmmJoystick.TryRead(_device.Id, out var sample))
        {
            // A failed direct capture read must invalidate the session immediately,
            // even before the next timer Poll. Never export earlier measurements
            // under a potentially replaced controller with the same WinMM ID.
            _winmmSessionInvalid = true;
            _raw = Array.Empty<uint>();
            MarkLiveReadingsUnavailable();
            _capture = null;
            _noise = null;
            _sweep = null;
            _sweepKind = null;
            CheckSignalButton.IsEnabled = true;
            LongSignalButton.IsEnabled = true;
            BrakeSweepButton.IsEnabled = true;
            ThrottleSweepButton.IsEnabled = true;
            ClutchSweepButton.IsEnabled = true;
            CaptureButton.IsEnabled = true;
            AdvancedAxesText.Text = "WinMM stream interrupted · readings are not current.";
            StatusText.Text = "Controller unavailable · return to Connect and click Refresh";
            CaptureFooter.Text = "WinMM read failed. Return to Connect and click Refresh; capture pedals again.";
            return;
        }
        _raw = sample;
        var kind = StepKind();
        var reserved = _mappings.Where(p => p.Key != kind).Select(p => p.Value.Axis);
        _capture = new GuidedCalibration(kind, _device, reserved);
        CaptureButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        CaptureCounter.Text = "0 / 3 presses";
        CaptureQualityText.Text = "";
        CaptureFooter.Text = "RELEASE PEDAL · Measuring resting signal...";
    }

    private void Poll(object? sender, EventArgs e)
    {
        if (_winmmSessionInvalid)
        {
            StatusText.Text = "WinMM session interrupted · return to Connect and click Refresh, then re-capture.";
            return;
        }
        if (_device is null || !WinmmJoystick.TryRead(_device.Id, out var sample))
        {
            if (_device is not null)
            {
                _winmmSessionInvalid = true;
                _raw = Array.Empty<uint>();
                MarkLiveReadingsUnavailable();
                AdvancedAxesText.Text = "WinMM stream interrupted · readings are not current.";
                StatusText.Text = "Controller unavailable · return to Connect and click Refresh";
                _capture = null;
                if (_noise is not null)
                    SignalText.Text = "Signal check cancelled: controller disconnected.";
                _noise = null;
                CheckSignalButton.IsEnabled = true;
                LongSignalButton.IsEnabled = true;
                _sweep = null;
                _sweepKind = null;
                BrakeSweepButton.IsEnabled = true;
                ThrottleSweepButton.IsEnabled = true;
                ClutchSweepButton.IsEnabled = true;
                SweepStatusText.Text = "Sweep cancelled: controller disconnected.";
                CaptureButton.IsEnabled = true;
            }
            return;
        }
        _raw = sample;
        // Show WinMM capability inconsistencies in the read-only live monitor.
        // This is not proof of sensor clipping or a HID-to-WinMM identity match.
        bool outsideWinmmRange = Enumerable.Range(0, 6)
            .Any(i => sample[i] < _device.Min[i] || sample[i] > _device.Max[i]);
        StatusText.Text = outsideWinmmRange
            ? "WinMM reading outside reported axis range · measurement may be invalid"
            : "Input streaming · read-only";
        AdvancedAxesText.Text = string.Join("\n", Enumerable.Range(0, 6)
            .Select(i => $"{WinmmJoystick.AxisNames[i],-2}  {sample[i],7:N0}   (0x{sample[i]:X8})" +
                (sample[i] < _device.Min[i] || sample[i] > _device.Max[i]
                    ? "  OUTSIDE REPORTED RANGE" : "")));

        if (_capture is not null)
        {
            var guide = _capture;
            guide.Observe(sample, DateTime.UtcNow);
            CaptureCounter.Text = $"{guide.PressesCompleted} / 3 presses";
            CaptureFooter.Text = guide.Instruction(DateTime.UtcNow);
            if (guide.Phase is GuidePhase.Complete or GuidePhase.Failed)
            {
                _capture = null;
                CaptureButton.IsEnabled = true;
                if (guide.Result is { } result)
                {
                    _mappings[guide.Kind] = result.Mapping;
                    _calibrationResults[guide.Kind] = result;
                    _sweepResults.Remove(guide.Kind); // mapping changed; old sweep no longer applies
                    // A new pedal capture may select a different WinMM axis.
                    // Existing noise and signal-health results were sampled using
                    // the previous mappings, so they must not be exported as current.
                    _lastNoise = null;
                    _lastSignalHealth = null;
                    UpdateMappingLabels();
                    CaptureQualityText.Text = result.PeakSpreadSummary;
                    CaptureFooter.Text = $"WinMM {WinmmJoystick.AxisNames[result.Mapping.Axis]} · Preview measured. No changes saved.";
                    NextButton.IsEnabled = true;
                    NextButton.Content = "Continue  →";
                }
                else
                {
                    CaptureQualityText.Text = "Measurement not saved · Retry";
                    NextButton.IsEnabled = _mappings.ContainsKey(guide.Kind) || guide.Kind == PedalKind.Clutch;
                    CaptureFooter.Text = guide.FailureReason;
                }
            }
        }

        if (_noise is not null)
        {
            var now = DateTime.UtcNow;
            _noise.Observe(sample, now);
            if (_noise.Complete)
            {
                _lastNoise = _mappings.ToDictionary(
                    p => p.Key.ToString(), p => _noise.Spread(p.Value.Axis));
                _lastSignalHealth = _mappings.ToDictionary(
                    p => p.Key.ToString(),
                    p => _noise.Analyze(p.Value.Axis, _device.Min[p.Value.Axis], _device.Max[p.Value.Axis]));
                _noise = null;
                CheckSignalButton.IsEnabled = true;
                LongSignalButton.IsEnabled = true;
                SignalText.Text = FormatNoise();
            }
            else
            {
                var remaining = Math.Max(0, _noise.RequestedDurationSeconds -
                    (now - _noise.StartedUtc).TotalSeconds);
                SignalText.Text = $"Keep pedals released · {remaining:0.0}s remaining";
            }
        }

        if (_sweep is not null && _sweepKind is { } sweepPedal)
        {
            var now = DateTime.UtcNow;
            _sweep.Observe(sample, now);
            if (_sweep.Failed)
            {
                SweepProgress.Value = 0;
                SweepStatusText.Text = $"{sweepPedal}: {_sweep.FailureReason}";
            }
            else
            {
                SweepProgress.Value = _sweep.Progress(now) * 100;
                SweepStatusText.Text = $"{sweepPedal}: recording… {Math.Max(0, InputSweepRun.DurationSeconds - (now - _sweepStartUtc).TotalSeconds):0.0}s";
                if (_sweep.Complete && _sweep.Result is { } result)
                {
                    _sweepResults[sweepPedal] = result;
                    SweepStatusText.Text = $"{sweepPedal}: {result.Summary}. Export JSON to inspect trace.";
                    UpdateSweepDetails();
                }
            }
            if (_sweep.Failed || _sweep.Complete)
            {
                _sweep = null;
                _sweepKind = null;
                BrakeSweepButton.IsEnabled = true;
                ThrottleSweepButton.IsEnabled = true;
                ClutchSweepButton.IsEnabled = true;
            }
        }

        if (_step is >= 1 and <= 3)
        {
            var kind = StepKind();
            var percent = _capture is not null
                ? _capture.PreviewPercent(sample) : GetPercent(kind);
            LiveValue.Text = _capture is not null || _mappings.ContainsKey(kind)
                ? $"{percent:0}%" : "—";
            CaptureProgress.Value = percent;
            int shownAxis = _capture?.Axis ?? (_mappings.TryGetValue(kind, out var map) ? map.Axis : -1);
            LiveRaw.Text = shownAxis >= 0
                ? $"WinMM reading: {sample[shownAxis]} · axis {WinmmJoystick.AxisNames[shownAxis]}"
                : "Awaiting pedal movement to identify WinMM axis";
            NeedleRotation.Angle = -5 + (percent * 1.1);
            ClutchDiscShift.X = percent * 0.32;
            ClutchHubShift.X = percent * 0.32;
            BrakeCaliper.Opacity = 1.0 - percent * 0.003;
        }
        else if (_step == 4)
        {
            UpdateTestBar(PedalKind.Brake, BrakeProgress, BrakeValue);
            UpdateTestBar(PedalKind.Throttle, ThrottleProgress, ThrottleValue);
            if (ClutchCheck.IsChecked == true) UpdateTestBar(PedalKind.Clutch, ClutchProgress, ClutchValue);
        }
    }

    private double GetPercent(PedalKind kind)
    {
        if (!_mappings.TryGetValue(kind, out var map) || _raw.Length <= map.Axis) return 0;
        return map.Percent(_raw[map.Axis]);
    }

    private void UpdateTestBar(PedalKind kind, ProgressBar bar, TextBlock value)
    {
        if (!_mappings.ContainsKey(kind))
        {
            bar.Value = 0;
            value.Text = "Not captured";
            return;
        }
        var percent = GetPercent(kind);
        bar.Value = percent;
        value.Text = $"{percent:0}%";
    }

    private void BrakeSweep_Click(object sender, RoutedEventArgs e) => BeginSweep(PedalKind.Brake);
    private void ThrottleSweep_Click(object sender, RoutedEventArgs e) => BeginSweep(PedalKind.Throttle);
    private void ClutchSweep_Click(object sender, RoutedEventArgs e) => BeginSweep(PedalKind.Clutch);

    private void BeginSweep(PedalKind kind)
    {
        if (kind == PedalKind.Clutch && ClutchCheck.IsChecked != true)
        {
            SweepStatusText.Text = "Enable the installed clutch and capture its WinMM axis first.";
            return;
        }
        if (_winmmSessionInvalid)
        {
            SweepStatusText.Text = "WinMM session interrupted · return to Connect and click Refresh.";
            return;
        }
        if (_device is null || !_mappings.TryGetValue(kind, out var mapping))
        {
            SweepStatusText.Text = $"{kind} is not captured. Complete guided setup first.";
            return;
        }
        // Never overlap a sweep with guided capture or an at-rest signal check.
        // Mixed measurements could misrepresent intentional pedal motion as noise.
        if (_capture is not null || _noise is not null || _sweep is not null)
        {
            SweepStatusText.Text = "Another WinMM measurement is in progress. Finish or cancel it before starting a sweep.";
            return;
        }
        _sweepResults.Remove(kind);
        UpdateSweepDetails();
        _sweepStartUtc = DateTime.UtcNow;
        _sweep = new InputSweepRun(mapping.Axis, _device.Min[mapping.Axis], _device.Max[mapping.Axis], _sweepStartUtc);
        _sweepKind = kind;
        BrakeSweepButton.IsEnabled = false;
        ThrottleSweepButton.IsEnabled = false;
        ClutchSweepButton.IsEnabled = false;
        SweepProgress.Value = 0;
        SweepStatusText.Text = $"{kind}: slowly press, hold, release within 8 seconds.";
    }

    private void UpdateSweepDetails()
    {
        if (SweepAnalysisText is null || BrakeSweepLine is null || ThrottleSweepLine is null || ClutchSweepLine is null) return;
        var descriptions = new List<string>();
        foreach (var pair in _sweepResults.OrderBy(p => p.Key))
        {
            if (!_mappings.TryGetValue(pair.Key, out var mapping) || _device is null) continue;
            var a = SweepDiagnostics.Evaluate(pair.Value.Trace, _device.Min[mapping.Axis], _device.Max[mapping.Axis],
                mapping.Full < mapping.Rest);
            descriptions.Add($"{pair.Key}: {a.Status} — {a.Explanation}");
        }
        SweepAnalysisText.Text = descriptions.Count == 0
            ? "Complete a sweep to review its signal shape."
            : string.Join("\n", descriptions);
        DrawTrace(PedalKind.Brake, BrakeSweepLine);
        DrawTrace(PedalKind.Throttle, ThrottleSweepLine);
        DrawTrace(PedalKind.Clutch, ClutchSweepLine);
    }

    private void DrawTrace(PedalKind kind, Polyline line)
    {
        var points = new PointCollection();
        if (_device is not null && _mappings.TryGetValue(kind, out var mapping) &&
            _sweepResults.TryGetValue(kind, out var sweep))
        {
            double min = _device.Min[mapping.Axis];
            double range = Math.Max(1.0, _device.Max[mapping.Axis] - min);
            foreach (var p in sweep.Trace)
            {
                double x = Math.Clamp(p.TimeMs / (InputSweepRun.DurationSeconds * 1000), 0, 1) * 460;
                double y = 99 - Math.Clamp((p.WinmmValue - min) / range, 0, 1) * 95;
                points.Add(new Point(x, y));
            }
        }
        line.Points = points;
    }

    private void ScanHid_Click(object sender, RoutedEventArgs e)
    {
        HidScanButton.IsEnabled = false;
        try
        {
            var previous = _hidInventory;
            var current = HidInterfaceInventory.Scan();
            _hidInventoryComparison = previous is { Available: true } && current.Available
                ? HidInterfaceInventory.Compare(previous, current) : null;
            _hidInventory = current;
            HidInventoryText.Text = current.Summary +
                (current.Available
                    ? _hidInventoryComparison is null
                        ? "\nScan again to compare grouped counts."
                        : "\n\nCHANGES SINCE PREVIOUS SCAN\n" + _hidInventoryComparison.Summary
                    : "\nComparison unavailable; scan again when enumeration works.");
            HidScanButton.Content = current.Available ? "Rescan & compare" : "Scan HID identifiers";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Unexpected scan failures must not crash the diagnostics UI or
            // leave a previous HID inventory looking like a fresh result.
            // Do not display native device paths or exception details.
            _hidInventory = null;
            _hidInventoryComparison = null;
            HidInventoryText.Text = "HID scan failed unexpectedly. No current HID inventory is available. Retry the scan; no controller settings were changed.";
            HidScanButton.Content = "Retry HID scan";
        }
        finally
        {
            HidScanButton.IsEnabled = true;
        }
    }

    private void CheckSignal_Click(object sender, RoutedEventArgs e) =>
        BeginSignalCheck(NoiseRun.DurationSeconds);

    private void CheckLongSignal_Click(object sender, RoutedEventArgs e) =>
        BeginSignalCheck(NoiseRun.ExtendedDurationSeconds);

    private void BeginSignalCheck(double seconds)
    {
        if (_winmmSessionInvalid)
        {
            SignalText.Text = "WinMM session interrupted · return to Connect and click Refresh.";
            return;
        }
        if (_raw.Length != 6) { SignalText.Text = "No input received"; return; }
        // An idle check cannot be interpreted while a pedal sweep or guided
        // press is active. Keep existing measurements and request a clean run.
        if (_capture is not null || _noise is not null || _sweep is not null)
        {
            SignalText.Text = "Another WinMM measurement is in progress. Finish or cancel it before checking signal at rest.";
            return;
        }
        _noise = new NoiseRun(durationSeconds: seconds);
        _lastNoise = null;
        _lastSignalHealth = null;
        CheckSignalButton.IsEnabled = false;
        LongSignalButton.IsEnabled = false;
        SignalText.Text = $"Measuring WinMM signal for {seconds:0}s… keep all pedals released.";
    }

    private string FormatNoise()
    {
        if (_lastSignalHealth is null) return "Not measured";
        return string.Join("\n", _lastSignalHealth.Select(x =>
            $"{x.Key}: Δ{x.Value.SpanWinmm} · σ{x.Value.StandardDeviationWinmm:0.0} · " +
            $"{x.Value.SamplingRateHz:0.0} Hz" +
            (x.Value.LargestSamplingGapSeconds is double gap ? $" · max gap {gap:0.00}s" : "") +
            $" · {x.Value.Status} — {x.Value.Guidance}"));
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        // Do not export a mixture of completed results and a measurement still in progress.
        bool measurementInProgress = _capture is not null || _noise is not null || _sweep is not null;
        if (!ReadOnlyHidReport.CanExport(_device is not null, _hidInventory, _winmmSessionInvalid,
                measurementInProgress))
        {
            MessageBox.Show(this,
                measurementInProgress
                    ? "A WinMM measurement is in progress. Wait for it to finish before exporting."
                    : _winmmSessionInvalid
                        ? "WinMM input was interrupted. Return to Connect, click Refresh, and capture pedals again before exporting."
                        : "Select a WinMM controller, or scan HID identifiers in Advanced diagnostics, before exporting.",
                "Export unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (ReadOnlyHidReport.IsControllerOnlyExport(
                _device is not null, _mappings.Count, _lastNoise is not null,
                _lastSignalHealth is not null, _sweepResults.Count, _hidInventory) &&
            MessageBox.Show(this,
                "No pedal measurements or HID scan are included. This report will contain only the selected WinMM controller details. Continue exporting?",
                "Controller-only report", MessageBoxButton.YesNo,
                MessageBoxImage.Information, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var save = new SaveFileDialog
        {
            Filter = "JSON report (*.json)|*.json",
            FileName = $"meca-preview-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            Title = "Export read-only diagnostics report"
        };
        if (save.ShowDialog(this) != true) return;
        try
        {
            // Validate optional HID metadata on both export paths, including
            // when a WinMM controller is selected.
            ReadOnlyHidReport.ValidateOptionalMetadata(_hidInventory, _hidInventoryComparison);
            object report;
            if (_device is null)
                report = ReadOnlyHidReport.Create(_hidInventory!, _hidInventoryComparison, DateTime.UtcNow);
            else
                report = new
            {
                App = "MECA Calibration Wizard v0.2.3",
                ExportedAtUtc = DateTime.UtcNow,
                Status = "READ_ONLY_PREVIEW_NOT_APPLIED_TO_WINDOWS",
                Device = new
                {
                    _device.Id, _device.Name,
                    WinmmManufacturerId = _device.ManufacturerId,
                    WinmmProductId = _device.ProductId,
                    WinmmIdsSource = "WINMM_JOYGETDEVCAPSW_NOT_VERIFIED_USB_VID_PID",
                    // Report per-axis WinMM capability provenance. A 0..65535
                    // fallback is not a driver-reported range or raw sensor ADC.
                    WinmmAxisRanges = Enumerable.Range(0, 6).Select(i => new
                    {
                        Axis = WinmmJoystick.AxisNames[i],
                        MinimumWinmm = _device.Min[i],
                        MaximumWinmm = _device.Max[i],
                        RangeSource = _device.AssumedAxisRanges is { Length: 6 } assumed
                            ? assumed[i] ? "ASSUMED_FALLBACK_NOT_REPORTED" : "WINMM_JOYGETDEVCAPSW"
                            : "UNKNOWN_NOT_VERIFIED"
                    }).ToArray()
                },
                Pedals = _mappings.ToDictionary(p => p.Key.ToString(), p => new
                {
                    Axis = WinmmJoystick.AxisNames[p.Value.Axis],
                    RestRaw = p.Value.Rest,
                    FullRaw = p.Value.Full,
                    MovementFraction = Math.Round(p.Value.Confidence, 4),
                    Calibration = _calibrationResults.TryGetValue(p.Key, out var c) ? new
                    {
                        Cycles = c.PeakInputs.Length,
                        PeaksWinmm = c.PeakInputs,
                        RestSpreadWinmm = c.RestInputSpread,
                        RestSampleCount = c.RestSamples,
                        RepeatabilityPeakSpreadPercent = c.RepeatabilitySpreadPercent,
                        Repeatability = c.RepeatabilityLabel,
                        AtAxisLimit = c.AtAxisLimit,
                        PeakSpreadInterpretation = c.PeakSpreadSummary,
                        PeakSpreadAffectedByWinmmLimit = c.AtAxisLimit
                    } : null
                }),
                NoiseCheckRawSpan = _lastNoise, // retained for report compatibility
                SignalHealth = _lastSignalHealth,
                InputSource = "WINMM_JOYGETPOSEX_NOT_SENSOR_ADC",
                HidInventory = _hidInventory, // optional explicit scan; aggregates only, no device paths
                HidInventoryComparison = _hidInventoryComparison, // count-only difference between scans
                WinmmAssociationVerified = false, // same numeric VID/PID values do not prove a device match
                UncalibratedSensorInputVerified = false, // WinMM is not raw sensor ADC
                Sweeps = _sweepResults.ToDictionary(p => p.Key.ToString(), p => new
                {
                    Axis = WinmmJoystick.AxisNames[_mappings[p.Key].Axis],
                    p.Value.InputSource,
                    p.Value.MinimumWinmm,
                    p.Value.MaximumWinmm,
                    p.Value.SampleCount,
                    p.Value.DistinctValues,
                    p.Value.IntermediateSamples,
                    p.Value.AtLowLimitSamples,
                    p.Value.AtHighLimitSamples,
                    p.Value.FirstHighLimitMs,
                    p.Value.LongestHighLimitRunSamples,
                    p.Value.HighLimitInterpretation,
                    Assessment = SweepDiagnostics.Evaluate(p.Value.Trace, _device.Min[_mappings[p.Key].Axis], _device.Max[_mappings[p.Key].Axis],
                        _mappings[p.Key].Full < _mappings[p.Key].Rest),
                    p.Value.Trace
                }),
                Note = "WinMM values may already be calibrated by Windows: not raw sensor ADC or physical force. Zero reported idle variation cannot prove correct earthing or absence of EMI. Axis names differ from DIView. No calibration is applied."
            };
            File.WriteAllText(save.FileName,
                JsonSerializer.Serialize(report, report.GetType(), new JsonSerializerOptions { WriteIndented = true }));
            MessageBox.Show(this, "Preview report saved. Device calibration was not changed.", "Export complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not export report: {ex.Message}", "Export error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
