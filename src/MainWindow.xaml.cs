using Microsoft.Win32;
using System.Diagnostics;
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
    // All WinMM capture durations use elapsed monotonic time, not the adjustable
    // Windows wall clock. UTC remains appropriate for JSON export timestamps.
    private readonly DateTime _measurementEpochUtc = DateTime.UtcNow;
    private readonly Stopwatch _measurementStopwatch = Stopwatch.StartNew();
    private DateTime MeasurementNow => _measurementEpochUtc + _measurementStopwatch.Elapsed;
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

    private void RefreshDevices(bool requireExplicitSelection = false)
    {
        _hidInventory = null; // Refresh starts a new explicit comparison sequence.
        _hidInventoryComparison = null;
        if (HidScanButton is not null) HidScanButton.Content = "Scan HID identifiers";
        if (HidInventoryText is not null)
            HidInventoryText.Text = "Not scanned. No HID device paths or serials are exported.";
        var previousId = _device?.Id;
        var devices = WinmmJoystick.FindDevices().ToList();
        DeviceCombo.ItemsSource = devices;
        // Never auto-select an arbitrary first match when multiple controllers
        // advertise MECA in their WinMM names. Selection remains user-controlled.
        WinmmJoystick.Device? next = requireExplicitSelection
            ? null : WinmmJoystick.SelectSuggestedDevice(devices, previousId);
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
        // A failed stream cannot prove that a reused WinMM ID belongs to
        // the same physical controller. Require deliberate reselection after
        // an interrupted session, not just an automatic name/ID hint.
        bool requireExplicitSelection = _winmmSessionInvalid;
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
        // Refresh is a new session even if WinMM reuses the same ID. Do not
        // depend on WPF SelectionChanged or the next timer tick to clear old
        // pedal readings; the gauges must stop looking live immediately.
        LiveValue.Text = "—";
        LiveRaw.Text = "Controller refreshed · awaiting a new WinMM reading.";
        CaptureProgress.Value = 0;
        BrakeProgress.Value = 0;
        ThrottleProgress.Value = 0;
        ClutchProgress.Value = 0;
        NeedleRotation.Angle = -5;
        ClutchDiscShift.X = 0;
        ClutchHubShift.X = 0;
        BrakeCaliper.Opacity = 1.0;
        BrakeValue.Text = "No live input";
        ThrottleValue.Text = "No live input";
        ClutchValue.Text = "No live input";
        SweepProgress.Value = 0;
        AdvancedAxesText.Text = "Controller refreshed · no current WinMM reading.";
        CaptureButton.IsEnabled = true;
        CheckSignalButton.IsEnabled = true;
        LongSignalButton.IsEnabled = true;
        BrakeSweepButton.IsEnabled = true;
        ThrottleSweepButton.IsEnabled = true;
        ClutchSweepButton.IsEnabled = true;
        SignalText.Text = "Controller refreshed · run signal check again.";
        SweepStatusText.Text = "Controller refreshed · record a new sweep.";
        RefreshDevices(requireExplicitSelection);
        ChangeStep(0);
        StepHint.Text = requireExplicitSelection
            ? "WinMM session interrupted · select controller explicitly, then capture pedals again"
            : "Controller refreshed · capture pedals again before exporting measurements";
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var next = DeviceCombo.SelectedItem as WinmmJoystick.Device;
        if (next?.Id != _device?.Id)
        {
            // Selecting an item from the pre-refresh list is not a fresh device scan.
            // Only Refresh_Click may clear an interrupted WinMM session.
            _device = next;
            _raw = Array.Empty<uint>();
            // A deliberate controller change is not a live WinMM reading. Poll
            // returns without updating gauges when no device is selected, so
            // clear every visual instrument immediately instead of freezing
            // the previous controller's last displayed pedal position.
            LiveValue.Text = "—";
            LiveRaw.Text = "Controller selection changed · no current WinMM reading.";
            CaptureProgress.Value = 0;
            BrakeProgress.Value = 0;
            ThrottleProgress.Value = 0;
            ClutchProgress.Value = 0;
            NeedleRotation.Angle = -5;
            ClutchDiscShift.X = 0;
            ClutchHubShift.X = 0;
            BrakeCaliper.Opacity = 1.0;
            BrakeValue.Text = "No live input";
            ThrottleValue.Text = "No live input";
            ClutchValue.Text = "No live input";
            SweepProgress.Value = 0;
            AdvancedAxesText.Text = "No current WinMM reading · select a controller.";
            _capture = null;
            _noise = null;
            _sweep = null;
            _sweepKind = null;
            // Switching WinMM controllers cancels active guided, signal and sweep
            // runs. Restore the start buttons for the newly selected controller,
            // but never permit a stale session to resume without Refresh.
            bool canReadSelectedController = next is not null && !_winmmSessionInvalid;
            CaptureButton.IsEnabled = canReadSelectedController;
            CheckSignalButton.IsEnabled = canReadSelectedController;
            LongSignalButton.IsEnabled = canReadSelectedController;
            BrakeSweepButton.IsEnabled = canReadSelectedController;
            ThrottleSweepButton.IsEnabled = canReadSelectedController;
            ClutchSweepButton.IsEnabled = canReadSelectedController;
            SignalText.Text = "Controller selection changed · run a new signal check.";
            SweepStatusText.Text = "Controller selection changed · record a new sweep.";
            _sweepResults.Clear();
            _lastNoise = null;
            _lastSignalHealth = null;
            _mappings.Clear();
            _calibrationResults.Clear();
            UpdateMappingLabels();
            UpdateDeviceInfo();
            UpdateWinmmIds();
            // Manual controller selection on Connect must update Begin immediately.
            // Poll updates the live values, not the wizard navigation button.
            // An interrupted WinMM session remains blocked until explicit Refresh.
            if (_step == 0)
            {
                NextButton.IsEnabled = _device is not null && !_winmmSessionInvalid;
                StepHint.Text = _winmmSessionInvalid
                    ? "WinMM session interrupted · click Refresh and select controller before continuing"
                    : _device is null
                        ? "Select a WinMM controller · physical identity not verified"
                        : "Controller selected · capture pedals to establish WinMM axis mapping";
            }
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
        // An optional pedal selection change during a WinMM rest check
        // invalidates that in-flight measurement. Do not let a timer tick
        // finish it under a different pedal configuration.
        if (_noise is not null)
        {
            _noise = null;
            CheckSignalButton.IsEnabled = _device is not null && !_winmmSessionInvalid;
            LongSignalButton.IsEnabled = _device is not null && !_winmmSessionInvalid;
            _lastNoise = null;
            _lastSignalHealth = null;
            SignalText.Text = "Pedal selection changed · signal check cancelled. Repeat with pedals released.";
        }
        // The optional pedal configuration has changed in either direction.
        // A completed WinMM signal check belongs to the previous channel set;
        // invalidate it even when the clutch is being ENABLED, not just removed.
        if (_lastNoise is not null || _lastSignalHealth is not null)
        {
            _lastNoise = null;
            _lastSignalHealth = null;
            if (SignalText is not null)
                SignalText.Text = "Pedal selection changed · run signal check again.";
        }
        if (ClutchCheck.IsChecked != true)
        {
            // Disabling an optional clutch while its guided capture is running
            // must not let a later WinMM sample resurrect a disabled pedal.
            if (_capture?.Kind == PedalKind.Clutch)
            {
                _capture = null;
                CaptureButton.IsEnabled = _device is not null && !_winmmSessionInvalid;
                CaptureCounter.Text = "0 / 3 presses";
                CaptureQualityText.Text = "";
                CaptureFooter.Text = "Clutch disabled · capture cancelled. No clutch data saved.";
                if (_step == 3)
                {
                    // A checkbox change must not re-enable Skip after a failed
                    // WinMM read. Only Refresh can start a new capture session.
                    NextButton.IsEnabled = !_winmmSessionInvalid;
                    NextButton.Content = "Skip  →";
                }
            }
            if (_sweepKind == PedalKind.Clutch)
            {
                _sweep = null;
                _sweepKind = null;
                BrakeSweepButton.IsEnabled = _device is not null && !_winmmSessionInvalid;
                ThrottleSweepButton.IsEnabled = _device is not null && !_winmmSessionInvalid;
                ClutchSweepButton.IsEnabled = _device is not null && !_winmmSessionInvalid;
                SweepProgress.Value = 0;
                SweepStatusText.Text = "Clutch disabled · sweep cancelled. No clutch sweep saved.";
            }
            _mappings.Remove(PedalKind.Clutch);
            _calibrationResults.Remove(PedalKind.Clutch);
            // A removed pedal must not leave an orphan sweep: JSON export
            // resolves each saved sweep through the active pedal mapping.
            _sweepResults.Remove(PedalKind.Clutch);
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
        ClutchMapText.Text = ClutchCheck?.IsChecked == true ? MapLabel(PedalKind.Clutch) : "Optional · not selected";
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
        LiveRaw.Text = "WinMM input interrupted · no current reading. Return to Connect and click Refresh.";
        CaptureProgress.Value = 0;
        BrakeProgress.Value = 0;
        ThrottleProgress.Value = 0;
        ClutchProgress.Value = 0;
        // Reset the visual instruments as well as the numeric readouts:
        // a frozen needle or displaced clutch graphic is not a live sample.
        NeedleRotation.Angle = -5;
        ClutchDiscShift.X = 0;
        ClutchHubShift.X = 0;
        BrakeCaliper.Opacity = 1.0;
        BrakeValue.Text = "No live input";
        ThrottleValue.Text = "No live input";
        ClutchValue.Text = "No live input";
        SweepProgress.Value = 0;
        if (_step is >= 1 and <= 3)
            NextButton.IsEnabled = false; // Stale guided captures must not offer Continue or Skip.
        if (_step == 0)
        {
            // The Connect view must not offer Begin for an invalid WinMM session.
            // The user must refresh and explicitly reselect after an interruption.
            NextButton.IsEnabled = false;
            StepHint.Text = "WinMM session interrupted · click Refresh and select controller before continuing";
        }
        if (_step is >= 1 and <= 3)
        {
            // An interrupted capture view must not keep a previous success
            // message or unmarked quality summary after a failed WinMM read.
            CaptureFooter.Text = "STALE — WinMM session interrupted. Click Refresh and capture pedals again.";
            CaptureQualityText.Text = MarkStaleForReview(CaptureQualityText.Text);
        }
        if (_step == 4)
        {
            // A completed idle check must not appear current after a failed WinMM read.
            // Preserve the earlier metrics for review, visibly marked stale.
            SignalText.Text = MarkStaleForReview(SignalText.Text);
            SummaryText.Text = MarkStaleForReview(SummaryText.Text);
            SweepAnalysisText.Text = MarkStaleForReview(SweepAnalysisText.Text);
            SweepStatusText.Text = MarkStaleForReview(SweepStatusText.Text);
        }
        UpdateMappingLabels();
    }

    private void UpdateDeviceInfo()
    {
        if (DeviceText is null || StatusText is null) return;
        DeviceText.Text = _device?.Name ?? "No controller selected";
        StatusText.Text = _device is null ? "Select a WinMM controller to preview inputs"
            : "WinMM controller selected · physical USB identity not verified";
    }

    // A disconnected WinMM stream leaves prior captures available for review,
    // not as current measurements. Every re-entered results view must say so.
    // An empty or never-recorded result is not a stale measurement: do not
    // prepend STALE to "Not measured" or an empty summary.
    private string MarkStaleForReview(string text) =>
        _winmmSessionInvalid &&
        !string.IsNullOrWhiteSpace(text) &&
        !string.Equals(text, "Not measured", StringComparison.Ordinal) &&
        // An empty sweep view contains guidance, not a previously captured result.
        !string.Equals(text, "Complete a sweep to review its signal shape.", StringComparison.Ordinal) &&
        !text.StartsWith("STALE —", StringComparison.Ordinal)
            ? "STALE — WinMM session interrupted. Review only; click Refresh and capture pedals again.\n" + text
            : text;

    private void ChangeStep(int step)
    {
        _capture = null;
        _noise = null;
        _sweep = null;
        _sweepKind = null;
        bool canStartWinmmMeasurement = _device is not null && !_winmmSessionInvalid;
        CaptureButton.IsEnabled = canStartWinmmMeasurement;
        BrakeSweepButton.IsEnabled = canStartWinmmMeasurement;
        ThrottleSweepButton.IsEnabled = canStartWinmmMeasurement;
        ClutchSweepButton.IsEnabled = canStartWinmmMeasurement;
        CheckSignalButton.IsEnabled = canStartWinmmMeasurement;
        LongSignalButton.IsEnabled = canStartWinmmMeasurement;
        _step = step;
        WelcomeView.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        CaptureView.Visibility = step is >= 1 and <= 3 ? Visibility.Visible : Visibility.Collapsed;
        TestView.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        AdvancedView.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.IsEnabled = step != 0;
        if (step == 0)
        {
            StepLabel.Text = "STEP 1 / CONNECT";
            StepHint.Text = _winmmSessionInvalid
                ? "WinMM session interrupted · click Refresh and select controller before continuing"
                : "Select a WinMM controller · physical identity not verified";
            NextButton.Content = "Begin  →";
            NextButton.IsEnabled = _device is not null && !_winmmSessionInvalid;
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
            StepHint.Text = "Read-only WinMM preview · rest, three presses and WinMM peak variation";
            CaptureCounter.Text = _calibrationResults.TryGetValue(kind, out var current)
                ? "3 / 3 presses" : "0 / 3 presses";
            CaptureQualityText.Text = current is null ? "" :
                MarkStaleForReview(current.PeakSpreadSummary);
            CaptureFooter.Text = _winmmSessionInvalid
                ? "STALE — WinMM session interrupted. Click Refresh and capture pedals again."
                : _mappings.ContainsKey(kind) ? "Preview captured. Press Start to measure again."
                : "Release the pedal, then click Start. Wait for the PRESS prompt.";
            NextButton.Content = kind == PedalKind.Clutch && !_mappings.ContainsKey(kind) ? "Skip  →" : "Continue  →";
            NextButton.IsEnabled = !_winmmSessionInvalid && (kind == PedalKind.Clutch || _mappings.ContainsKey(kind));
        }
        else if (step == 4)
        {
            StepLabel.Text = "LIVE TEST";
            StepHint.Text = "Read-only preview · no changes to Windows";
            NextButton.Content = "Start over  ↻";
            NextButton.IsEnabled = true;
            ClutchTestArea.Visibility = ClutchCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            SignalText.Text = MarkStaleForReview(_lastNoise is null ? "Not measured" : FormatNoise());
            SweepStatusText.Text = MarkStaleForReview(_sweepResults.Count == 0
                ? "Not measured" : string.Join(" | ", _sweepResults.Select(p => $"{p.Key}: {p.Value.Summary}")));
            SweepProgress.Value = 0;
            UpdateSweepDetails();
            SummaryText.Text = MarkStaleForReview(string.Join("   |   ", _calibrationResults.Select(p =>
                $"{p.Key}: {p.Value.PeakSpreadSummary}")));
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
        // Keep stored measurements visible for review after a failed WinMM read,
        // but never let the guided wizard advance using stale pedal mappings.
        // Advanced diagnostics can still be viewed; export remains blocked.
        if (_winmmSessionInvalid && _step is >= 1 and <= 3)
        {
            MessageBox.Show(this,
                "WinMM input was interrupted. Return to Connect and click Refresh before continuing; previous captures are stale.",
                "Refresh required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
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
        if (!WinmmJoystick.TryRead(_device.Id, out var sample) ||
            !WinmmJoystick.IsSampleWithinAxisBounds(_device, sample))
        {
            // A failed or out-of-range direct capture read must invalidate the session immediately,
            // even before the next timer Poll. Never export earlier measurements
            // under a potentially replaced controller with the same WinMM ID.
            _winmmSessionInvalid = true;
            _raw = Array.Empty<uint>();
            MarkLiveReadingsUnavailable();
            _capture = null;
            _noise = null;
            _sweep = null;
            _sweepKind = null;
            CheckSignalButton.IsEnabled = false;
            LongSignalButton.IsEnabled = false;
            BrakeSweepButton.IsEnabled = false;
            ThrottleSweepButton.IsEnabled = false;
            ClutchSweepButton.IsEnabled = false;
            CaptureButton.IsEnabled = false;
            AdvancedAxesText.Text = "WinMM stream interrupted · readings are not current.";
            StatusText.Text = "Controller unavailable · return to Connect and click Refresh";
            CaptureFooter.Text = "STALE — WinMM read failed or an axis was out of range. Return to Connect and click Refresh; capture pedals again.";
            return;
        }
        _raw = sample;
        var kind = StepKind();
        var reserved = _mappings.Where(p => p.Key != kind).Select(p => p.Value.Axis);
        _capture = new GuidedCalibration(kind, _device, reserved, MeasurementNow);
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
                // Completed checks remain visible as STALE; only active checks are cancelled.
                bool signalWasActive = _noise is not null;
                bool sweepWasActive = _sweep is not null;
                _capture = null;
                if (signalWasActive)
                    SignalText.Text = "Signal check cancelled: controller disconnected.";
                _noise = null;
                CheckSignalButton.IsEnabled = false;
                LongSignalButton.IsEnabled = false;
                _sweep = null;
                _sweepKind = null;
                BrakeSweepButton.IsEnabled = false;
                ThrottleSweepButton.IsEnabled = false;
                ClutchSweepButton.IsEnabled = false;
                if (sweepWasActive)
                    SweepStatusText.Text = "Sweep cancelled: controller disconnected.";
                CaptureButton.IsEnabled = false;
            }
            return;
        }
        _raw = sample;
        // Fail closed for an impossible reading on ANY WinMM axis, including
        // an unmapped one. Continuing could attach a valid-looking pedal sweep
        // to a stream whose capability data is no longer trustworthy.
        // Preserve previous captures for review, but block export until the
        // user explicitly refreshes and captures again.
        if (!WinmmJoystick.IsSampleWithinAxisBounds(_device, sample))
        {
            _winmmSessionInvalid = true;
            _raw = Array.Empty<uint>();
            // Preserve finished measurements as STALE; only interrupt active runs.
            bool captureWasActive = _capture is not null;
            bool signalWasActive = _noise is not null;
            bool sweepWasActive = _sweep is not null;
            MarkLiveReadingsUnavailable();
            _capture = null;
            _noise = null;
            _sweep = null;
            _sweepKind = null;
            CaptureButton.IsEnabled = false;
            CheckSignalButton.IsEnabled = false;
            LongSignalButton.IsEnabled = false;
            BrakeSweepButton.IsEnabled = false;
            ThrottleSweepButton.IsEnabled = false;
            ClutchSweepButton.IsEnabled = false;
            StatusText.Text = "WinMM reading outside the reported or assumed axis range. Session invalid · return to Connect and click Refresh, then re-capture.";
            AdvancedAxesText.Text = "WinMM sample invalid · live readings stopped. No sensor clipping or device identity inferred.";
            // Do not overwrite a completed capture's STALE footer with a generic
            // cancellation message. Only an active guided capture was cancelled.
            if (captureWasActive && _step is >= 1 and <= 3)
                CaptureFooter.Text = "STALE — WinMM session interrupted. Active capture cancelled; refresh and capture again.";
            if (signalWasActive)
                SignalText.Text = "Signal check cancelled · refresh the controller and repeat.";
            if (sweepWasActive)
                SweepStatusText.Text = "Sweep cancelled · refresh the controller and repeat.";
            return;
        }
        StatusText.Text = "Input streaming · read-only";
        AdvancedAxesText.Text = string.Join("\n", Enumerable.Range(0, 6)
            .Select(i => WinmmJoystick.FormatAxisPreview(_device, sample, i)));

        if (_capture is not null)
        {
            var guide = _capture;
            var measurementNow = MeasurementNow;
            guide.Observe(sample, measurementNow);
            CaptureCounter.Text = $"{guide.PressesCompleted} / 3 presses";
            CaptureFooter.Text = guide.Instruction(measurementNow);
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
                    CaptureFooter.Text = $"WinMM {WinmmJoystick.AxisNames[result.Mapping.Axis]} · Pedal measurement captured for this session. No Windows settings changed.";
                    NextButton.IsEnabled = true;
                    NextButton.Content = "Continue  →";
                }
                else
                {
                    CaptureQualityText.Text = "Measurement not saved · Retry";
                    NextButton.IsEnabled = _mappings.ContainsKey(guide.Kind) || guide.Kind == PedalKind.Clutch;
                    CaptureFooter.Text = _mappings.ContainsKey(guide.Kind)
                        ? $"{guide.FailureReason} Previous WinMM preview retained (not remeasured); retry to replace it."
                        : guide.FailureReason;
                }
            }
        }

        if (_noise is not null)
        {
            var now = MeasurementNow;
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
            var now = MeasurementNow;
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
            int shownAxis = _capture?.Axis ?? (_mappings.TryGetValue(kind, out var map) ? map.Axis : -1);
            // Guided preview uses the same WinMM range guard as the final test bars.
            // Never show a plausible percentage when the selected axis is invalid.
            bool invalidWinmmReading = shownAxis >= 0 &&
                (sample[shownAxis] < _device.Min[shownAxis] || sample[shownAxis] > _device.Max[shownAxis]);
            var percent = invalidWinmmReading ? 0 : _capture is not null
                ? _capture.PreviewPercent(sample) : GetPercent(kind);
            LiveValue.Text = invalidWinmmReading ? "Invalid WinMM reading"
                : _capture is not null || _mappings.ContainsKey(kind)
                    ? $"{percent:0}%" : "—";
            CaptureProgress.Value = percent;
            LiveRaw.Text = shownAxis >= 0
                ? $"WinMM reading: {sample[shownAxis]} · axis {WinmmJoystick.AxisNames[shownAxis]}" +
                    (invalidWinmmReading ? " · OUTSIDE WINMM AXIS RANGE" : "")
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
        // A successful WinMM poll can still return a value outside the
        // controller's advertised (or assumed) axis range. Do not display
        // a plausible pedal percentage for that invalid live reading.
        if (_device is null || _raw.Length <= _mappings[kind].Axis)
        {
            bar.Value = 0;
            value.Text = "No live input";
            return;
        }
        int axis = _mappings[kind].Axis;
        if (_raw[axis] < _device.Min[axis] || _raw[axis] > _device.Max[axis])
        {
            bar.Value = 0;
            value.Text = "Invalid WinMM reading";
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
        _sweepStartUtc = MeasurementNow;
        _sweep = new InputSweepRun(mapping.Axis, _device.Min[mapping.Axis], _device.Max[mapping.Axis],
            _sweepStartUtc, axisRangeAssumed: _device.AssumedAxisRanges is { Length: 6 } assumed
                ? assumed[mapping.Axis] : null);
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
        SweepAnalysisText.Text = MarkStaleForReview(descriptions.Count == 0
            ? "Complete a sweep to review its signal shape."
            : string.Join("\n", descriptions));
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
                    : "\nComparison reset after an unsuccessful scan. Two successful scans are needed to compare counts.");
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
        // Without a captured pedal, a signal check would produce empty diagnostics.
        if (_mappings.Count == 0)
        {
            SignalText.Text = "Capture at least one pedal before checking WinMM signal at rest.";
            return;
        }
        // An idle check cannot be interpreted while a pedal sweep or guided
        // press is active. Keep existing measurements and request a clean run.
        if (_capture is not null || _noise is not null || _sweep is not null)
        {
            SignalText.Text = "Another WinMM measurement is in progress. Finish or cancel it before checking signal at rest.";
            return;
        }
        _noise = new NoiseRun(startedUtc: MeasurementNow, durationSeconds: seconds);
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
            $"{x.Key}: n={x.Value.SampleCount} · median {x.Value.MedianWinmm:0.#} · " +
            $"Δ{x.Value.SpanWinmm} · max step {x.Value.LargestStepWinmm} · " +
            $"σ{x.Value.StandardDeviationWinmm:0.0} · {x.Value.SamplingRateHz:0.0} Hz" +
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
        // Avoid asking for a save path when the report is already known to
        // contain only controller details. Re-check after the modal dialog too:
        // captured data can be cleared while it pumps WPF dispatcher events.
        bool controllerOnlyConsentGiven = false;
        if (ReadOnlyHidReport.IsControllerOnlyExport(
                _device is not null, _mappings.Count, (_lastNoise?.Count ?? 0) > 0,
                (_lastSignalHealth?.Count ?? 0) > 0, _sweepResults.Count, _hidInventory))
        {
            if (MessageBox.Show(this,
                    "No pedal measurements or successful HID inventory are included. This report contains WinMM controller details and may include a failed HID scan status. Continue exporting?",
                    "Controller-only report", MessageBoxButton.YesNo,
                    MessageBoxImage.Information, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            controllerOnlyConsentGiven = true;
        }

        var save = new SaveFileDialog
        {
            Filter = "JSON report (*.json)|*.json",
            FileName = $"meca-preview-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            Title = "Export read-only diagnostics report"
        };
        if (save.ShowDialog(this) != true) return;
        // A modal save dialog can pump WPF dispatcher messages. A WinMM poll
        // may invalidate the capture session while the user chooses a path.
        // Recheck immediately before creating/writing any report; never export
        // measurements that became stale after the initial export gate.
        if (!ReadOnlyHidReport.CanExport(_device is not null, _hidInventory,
                _winmmSessionInvalid,
                _capture is not null || _noise is not null || _sweep is not null))
        {
            MessageBox.Show(this,
                "Input changed while the save dialog was open. No report saved. If WinMM was interrupted, click Refresh and capture pedals again.",
                "Export cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // Re-evaluate controller-only consent AFTER the modal save dialog:
        // Refresh or a device change can clear captured pedal data while the
        // dialog pumps WPF events. Never silently export an unconfirmed
        // controller-only report just because the initial state had measurements.
        if (!controllerOnlyConsentGiven && ReadOnlyHidReport.IsControllerOnlyExport(
                _device is not null, _mappings.Count, (_lastNoise?.Count ?? 0) > 0,
                (_lastSignalHealth?.Count ?? 0) > 0, _sweepResults.Count, _hidInventory))
        {
            if (MessageBox.Show(this,
                    "No pedal measurements or successful HID inventory are included. This report contains WinMM controller details and may include a failed HID scan status. Continue exporting?",
                    "Controller-only report", MessageBoxButton.YesNo,
                    MessageBoxImage.Information, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            // The confirmation dialog can also pump dispatcher messages.
            // Check session validity again before the final direct WinMM read.
            if (!ReadOnlyHidReport.CanExport(_device is not null, _hidInventory,
                    _winmmSessionInvalid,
                    _capture is not null || _noise is not null || _sweep is not null))
            {
                MessageBox.Show(this,
                    "Input changed during confirmation. No report saved.",
                    "Export cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        // The dispatcher timer may not have polled since the save dialog
        // closed. Re-read the selected WinMM controller before serializing:
        // a cached valid session flag alone is not fresh input evidence.
        // This is a read-only check; it cannot establish physical USB identity.
        if (_device is { } exportDevice &&
            (!WinmmJoystick.TryRead(exportDevice.Id, out var exportSample) ||
             !WinmmJoystick.IsSampleWithinAxisBounds(exportDevice, exportSample)))
        {
            _winmmSessionInvalid = true;
            _raw = Array.Empty<uint>();
            MarkLiveReadingsUnavailable();
            AdvancedAxesText.Text = "WinMM stream interrupted · readings are not current.";
            StatusText.Text = "WinMM export check failed · return to Connect and click Refresh";
            MessageBox.Show(this,
                "WinMM input failed its final read-only check. No report saved. Click Refresh and capture pedals again.",
                "Export cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
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
                    RestRaw = p.Value.Rest, // legacy key: Windows WinMM, not sensor ADC
                    FullRaw = p.Value.Full, // legacy key: Windows WinMM, not sensor ADC
                    RestWinmm = p.Value.Rest,
                    FullWinmm = p.Value.Full,
                    MovementFraction = Math.Round(p.Value.Confidence, 4),
                    Calibration = _calibrationResults.TryGetValue(p.Key, out var c) ? new
                    {
                        Cycles = c.PeakInputs.Length,
                        PeaksWinmm = c.PeakInputs,
                        RestSpreadWinmm = c.RestInputSpread,
                        RestSampleCount = c.RestSamples,
                        RepeatabilityPeakSpreadPercent = c.RepeatabilitySpreadPercent,
                        Repeatability = c.RepeatabilityLabel,
                        AtAxisLimit = c.AtAxisLimit, // legacy field: near-limit band, not necessarily an exact endpoint
                        NearWinmmLimitBandObserved = c.AtAxisLimit,
                        WinmmLimitBandInterpretation = "Two or more guided peaks within 0.2% of a reported or assumed WinMM axis endpoint; exact endpoint, sensor ADC clipping and physical force remain unverified.",
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
                    p.Value.MinimumWinmm, // legacy fields: observed sweep extrema, not capability bounds
                    p.Value.MaximumWinmm,
                    ObservedMinimumWinmm = p.Value.MinimumWinmm,
                    ObservedMaximumWinmm = p.Value.MaximumWinmm,
                    SweepExtremaSource = "OBSERVED_WINMM_SAMPLES_NOT_DEVICE_AXIS_LIMITS",
                    p.Value.SampleCount,
                    p.Value.DistinctValues,
                    p.Value.IntermediateSamples,
                    p.Value.AtLowLimitSamples,
                    p.Value.AtHighLimitSamples,
                    p.Value.FirstHighLimitMs,
                    p.Value.LongestHighLimitRunSamples,
                    HighLimitInterpretation = p.Value.HighLimitInterpretationFor(
                        _mappings[p.Key].Full < _mappings[p.Key].Rest),
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
