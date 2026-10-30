namespace MecaCalibrationWizard;

// In-app calibration assistant ONLY. No HID feature reports, registry modifications,
// driver installation or writes to device firmware occur in this class.
internal enum GuidePhase { MeasuringRest, AwaitingPress, AwaitingRelease, Complete, Failed }

internal sealed record PedalCalibrationResult(
    PedalMapping Mapping,
    uint RestInputSpread,
    uint[] PeakInputs,
    double RepeatabilitySpreadPercent,
    int RestSamples,
    bool AtAxisLimit)
{
    // WinMM axis endpoints do not establish physical pedal-force repeatability.
    public string PeakSpreadSummary => AtAxisLimit
        ? "WinMM near-limit band observed - physical-force repeatability unknown"
        : $"WinMM peak spread {RepeatabilitySpreadPercent:0.0}% - not a physical-force test";

    // Labels are exported to JSON. WinMM peak similarity must never be
    // presented as verified repeatability of physical pedal force.
    public string RepeatabilityLabel => AtAxisLimit ? "WinMM limit (force unverified)" :
        RepeatabilitySpreadPercent switch
        {
            <= 5.0 => "Similar WinMM peaks (force unverified)",
            <= 12.0 => "WinMM peak variation (force unverified)",
            _ => "Repeat WinMM capture (force unverified)"
        };
}

internal sealed class GuidedCalibration
{
    private const int RequiredPresses = 3;
    // Ignore a single low WinMM sample as a release; require a stable return to rest.
    private const int RequiredReleaseSamples = 3;
    private const double RequiredReleaseMilliseconds = 80;
    private readonly uint[] _range;
    private readonly uint[] _deviceMin;
    private readonly uint[] _deviceMax;
    private readonly bool[] _assumedAxisRanges;
    private readonly HashSet<int> _reservedAxes;
    private readonly double[] _sum = new double[6];
    private readonly uint[] _restMin = Enumerable.Repeat(uint.MaxValue, 6).ToArray();
    private readonly uint[] _restMax = new uint[6];
    private readonly double[] _rest = new double[6];
    private readonly List<uint> _peaks = new();
    private readonly DateTime _startUtc;
    private DateTime _lastSampleUtc;
    private DateTime _movementStartUtc;
    private double _currentPeak;
    // Reject brief WinMM spikes as pedal presses; require sustained movement.
    private int _pressAboveThresholdSamples;
    private DateTime _firstPressAboveUtc;
    private DateTime _lastPressAboveUtc;
    private int _releaseBelowThresholdSamples;
    private DateTime _firstReleaseBelowUtc;
    private int _direction;
    private int _restSamples;
    private int _axis = -1;

    public PedalKind Kind { get; }
    public GuidePhase Phase { get; private set; } = GuidePhase.MeasuringRest;
    public string FailureReason { get; private set; } = "";
    public PedalCalibrationResult? Result { get; private set; }
    public int PressesCompleted => _peaks.Count;
    public int Axis => _axis;
    public int RestSamples => _restSamples;

    public GuidedCalibration(PedalKind kind, WinmmJoystick.Device device,
        IEnumerable<int> reservedAxes, DateTime? startUtc = null)
    {
        Kind = kind;
        _startUtc = startUtc ?? DateTime.UtcNow;
        _lastSampleUtc = _startUtc;
        _movementStartUtc = _startUtc;
        _reservedAxes = reservedAxes.ToHashSet();
        _deviceMin = (uint[])device.Min.Clone();
        _deviceMax = (uint[])device.Max.Clone();
        _assumedAxisRanges = device.AssumedAxisRanges is { Length: 6 } assumed
            ? (bool[])assumed.Clone() : new bool[6];
        _range = Enumerable.Range(0, 6)
            .Select(i => (uint)Math.Max(1.0, (double)device.Max[i] - device.Min[i]))
            .ToArray();
    }

    public string Instruction(DateTime utcNow)
    {
        return Phase switch
        {
            GuidePhase.MeasuringRest =>
                $"RELEASE PEDAL · Measuring rest ({Math.Max(0, 1.2 - (utcNow - _startUtc).TotalSeconds):0.0}s)",
            GuidePhase.AwaitingPress => $"PRESS PEDAL · {PressesCompleted + 1} of {RequiredPresses}",
            GuidePhase.AwaitingRelease => $"RELEASE PEDAL · {PressesCompleted + 1} of {RequiredPresses}",
            GuidePhase.Complete => "THREE PRESSES CAPTURED · Preview ready",
            GuidePhase.Failed => FailureReason,
            _ => ""
        };
    }

    public double PreviewPercent(uint[] reading)
    {
        if (_axis < 0 || reading.Length <= _axis || _direction == 0) return 0;
        var span = Result is not null
            ? Math.Abs((double)Result.Mapping.Full - Result.Mapping.Rest)
            : Math.Max(_range[_axis] * 0.05, Math.Max(_currentPeak, _peaks.Count == 0 ? 0 : _peaks.Max(p => Math.Abs(p - _rest[_axis]))));
        return Math.Clamp(_direction * (reading[_axis] - _rest[_axis]) / Math.Max(1, span) * 100, 0, 100);
    }

    public void Observe(uint[] reading, DateTime utcNow)
    {
        if (Phase is GuidePhase.Complete or GuidePhase.Failed) return;
        // A malformed WinMM sample must invalidate guided capture, not be
        // silently skipped and followed by an apparently complete pedal result.
        if (reading is null || reading.Length != WinmmJoystick.AxisNames.Length)
        {
            Fail("WinMM guided sample has an invalid axis count. Release the pedal and retry capture.");
            return;
        }
        // Wall-clock adjustments and long WinMM polling stalls must not make a
        // short rest baseline or a pedal press appear to have elapsed normally.
        // Fail conservatively; no calibration is written to Windows or the device.
        double sampleGapSeconds = (utcNow - _lastSampleUtc).TotalSeconds;
        // The first sample may be stamped at capture start, but subsequent
        // equal timestamps cannot count as independent rest/press/release samples.
        // RestSamples is nonzero after the first accepted sample in every phase.
        if (sampleGapSeconds < 0 || sampleGapSeconds > 0.5 ||
            (sampleGapSeconds == 0 && _restSamples > 0))
        {
            Fail("WinMM sample timing interrupted. Release the pedal and retry capture.");
            return;
        }
        _lastSampleUtc = utcNow;
        // A Windows joystick sample outside joyGetDevCaps' reported bounds
        // cannot be used to identify a pedal axis or calculate its travel.
        // Fail closed; this preview never changes Windows calibration.
        for (int axis = 0; axis < 6; axis++)
        {
            if (reading[axis] < _deviceMin[axis] || reading[axis] > _deviceMax[axis])
            {
                Fail(_assumedAxisRanges[axis]
                    ? "WinMM reading outside the assumed axis range (0–65535 fallback). Release the pedal and retry capture."
                    : "WinMM reading outside the reported axis range. Release the pedal and retry capture.");
                return;
            }
        }
        if (Phase == GuidePhase.MeasuringRest)
        {
            for (int i = 0; i < 6; i++)
            {
                _sum[i] += reading[i];
                _restMin[i] = Math.Min(_restMin[i], reading[i]);
                _restMax[i] = Math.Max(_restMax[i], reading[i]);
            }
            _restSamples++;
            if ((utcNow - _startUtc).TotalSeconds < 1.2) return;
            if (_restSamples < 8) { Fail("Insufficient samples. Please retry."); return; }
            for (int i = 0; i < 6; i++) _rest[i] = _sum[i] / _restSamples;
            Phase = GuidePhase.AwaitingPress;
            _movementStartUtc = utcNow;
            return;
        }

        if ((utcNow - _movementStartUtc).TotalSeconds > 45)
        {
            Fail("Capture timed out. Release pedal and retry.");
            return;
        }

        // Detect the first significant physical movement, excluding axes already
        // assigned to other pedals. A nonzero idle value is NOT pedal detection.
        if (_axis < 0)
        {
            double top = 0, runnerUp = 0;
            int candidate = -1;
            for (int i = 0; i < 6; i++)
            {
                if (_reservedAxes.Contains(i)) continue;
                double fraction = Math.Abs(reading[i] - _rest[i]) / _range[i];
                if (fraction > top)
                {
                    runnerUp = top;
                    top = fraction;
                    candidate = i;
                }
                else runnerUp = Math.Max(runnerUp, fraction);
            }
            if (top < 0.05) return;
            if (runnerUp > 0.05 && runnerUp > top * 0.65)
            {
                Fail("Several axes moved. Press only the selected pedal, then retry.");
                return;
            }
            _axis = candidate;
            _direction = reading[candidate] >= _rest[candidate] ? 1 : -1;
            if (_restMax[_axis] - _restMin[_axis] > _range[_axis] * 0.02)
            {
                Fail("Pedal moved during rest sampling. Release it and retry.");
                return;
            }
        }

        double displacement = Math.Max(0, _direction * (reading[_axis] - _rest[_axis]));
        var pressThreshold = _range[_axis] * 0.05;
        var releaseThreshold = _range[_axis] * 0.015;
        if (Phase == GuidePhase.AwaitingPress && displacement >= pressThreshold)
        {
            _currentPeak = displacement;
            _pressAboveThresholdSamples = 1;
            _firstPressAboveUtc = utcNow;
            _lastPressAboveUtc = utcNow;
            Phase = GuidePhase.AwaitingRelease;
        }
        else if (Phase == GuidePhase.AwaitingRelease)
        {
            // An isolated low sample can be a WinMM dropout, not a completed
            // pedal release. If high readings resume, start a fresh hold streak
            // instead of borrowing timing or a peak from before the dropout.
            if (_releaseBelowThresholdSamples > 0 && displacement > releaseThreshold)
            {
                _releaseBelowThresholdSamples = 0;
                _pressAboveThresholdSamples = 0;
                _firstPressAboveUtc = default;
                _lastPressAboveUtc = default;
                _currentPeak = 0;
            }
            _currentPeak = Math.Max(_currentPeak, displacement);
            if (displacement >= pressThreshold)
            {
                if (_pressAboveThresholdSamples == 0)
                    _firstPressAboveUtc = utcNow;
                _pressAboveThresholdSamples++;
                _lastPressAboveUtc = utcNow;
            }
            else if (displacement > releaseThreshold)
            {
                // A gradual, legitimate release crosses the band between the
                // press and release thresholds. Keep an already verified hold
                // through this band, but never let interrupted short high
                // samples accumulate into a false press.
                bool sustainedPress = _pressAboveThresholdSamples >= 3 &&
                    (_lastPressAboveUtc - _firstPressAboveUtc).TotalMilliseconds >= 100;
                if (!sustainedPress)
                {
                    _pressAboveThresholdSamples = 0;
                    _firstPressAboveUtc = default;
                    _lastPressAboveUtc = default;
                    _currentPeak = 0;
                }
            }
            else
            {
                if (_releaseBelowThresholdSamples == 0)
                    _firstReleaseBelowUtc = utcNow;
                _releaseBelowThresholdSamples++;
                if (_releaseBelowThresholdSamples < RequiredReleaseSamples ||
                    (utcNow - _firstReleaseBelowUtc).TotalMilliseconds < RequiredReleaseMilliseconds)
                    return;

                // Only a continuous high streak followed by a stable low
                // streak counts. No sensor force or Windows settings inferred.
                if (_pressAboveThresholdSamples >= 3 &&
                    (_lastPressAboveUtc - _firstPressAboveUtc).TotalMilliseconds >= 100 &&
                    _currentPeak >= pressThreshold)
                {
                    double absolutePeak = _rest[_axis] + _direction * _currentPeak;
                    _peaks.Add((uint)Math.Clamp(Math.Round(absolutePeak), 0d, (double)uint.MaxValue));
                }
                _currentPeak = 0;
                _pressAboveThresholdSamples = 0;
                _releaseBelowThresholdSamples = 0;
                Phase = GuidePhase.AwaitingPress;
                if (_peaks.Count == RequiredPresses) Finish();
            }
        }
    }

    private void Finish()
    {
        if (_axis < 0 || _peaks.Count != RequiredPresses) { Fail("Incomplete capture. Please retry."); return; }
        var rest = (uint)Math.Clamp(Math.Round(_rest[_axis]), 0, (double)uint.MaxValue);
        var distances = _peaks.Select(p => Math.Abs((double)p - rest)).OrderBy(d => d).ToArray();
        double medianDistance = distances[1];
        if (medianDistance < _range[_axis] * 0.04)
        {
            Fail("Pedal movement too small. Please retry.");
            return;
        }
        var full = (uint)Math.Clamp(Math.Round((double)rest + _direction * medianDistance), 0, (double)uint.MaxValue);
        var spreadPercent = (distances[2] - distances[0]) / medianDistance * 100;
        // A Windows-calibrated axis may clip at its reported min/max.
        // Three identical clipped readings do NOT prove repeatable physical force.
        double highThreshold = _deviceMax[_axis] - _range[_axis] * 0.002;
        double lowThreshold = _deviceMin[_axis] + _range[_axis] * 0.002;
        int clipped = _peaks.Count(p => _direction > 0
            ? p >= highThreshold : p <= lowThreshold);
        Result = new PedalCalibrationResult(
            new PedalMapping(_axis, rest, full, medianDistance / _range[_axis]),
            _restMax[_axis] - _restMin[_axis], _peaks.ToArray(),
            Math.Round(spreadPercent, 2), _restSamples, clipped >= 2);
        Phase = GuidePhase.Complete;
    }

    private void Fail(string message)
    {
        FailureReason = message;
        Phase = GuidePhase.Failed;
    }
}
