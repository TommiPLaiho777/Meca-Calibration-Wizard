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

// #CALIBRATION_PREVIEW: shared movement procedure for brake, throttle and clutch.
// Results define an in-app preview; they do not change Windows or hardware.
internal sealed class GuidedCalibration
{
    private readonly int _requiredPresses;
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
    // Second-largest high-band sample distinguishes a lone WinMM peak spike
    // from a sustained pedal endpoint; it does not establish sensor accuracy.
    private double _secondHighestPressDisplacement;
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

    // #PRESS_VALIDITY: both release paths must agree on a sustained press.
    // Count and elapsed hold time are required together; isolated spikes do not count.
    private bool HasSustainedPress => _pressAboveThresholdSamples >= 3 &&
        (_lastPressAboveUtc - _firstPressAboveUtc).TotalMilliseconds >= 100;

    public GuidedCalibration(PedalKind kind, WinmmJoystick.Device device,
        IEnumerable<int> reservedAxes, DateTime? startUtc = null,
        int requiredPresses = 3, int? identifiedAxis = null)
    {
        Kind = kind;
        if (requiredPresses is not (1 or 3)) throw new ArgumentOutOfRangeException(nameof(requiredPresses));
        _requiredPresses = requiredPresses;
        if (identifiedAxis is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(identifiedAxis));
        _startUtc = startUtc ?? DateTime.UtcNow;
        _lastSampleUtc = _startUtc;
        _movementStartUtc = _startUtc;
        _reservedAxes = reservedAxes.ToHashSet();
        // #PEDAL_IDENTITY: calibration may only use the axis identified for this
        // role. Moving another pedal must never silently re-identify the role.
        if (identifiedAxis.HasValue)
            _reservedAxes.UnionWith(Enumerable.Range(0, 6).Where(a => a != identifiedAxis.Value));
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
            GuidePhase.AwaitingPress => $"PRESS {Kind.ToString().ToUpperInvariant()} · {PressesCompleted + 1} of {_requiredPresses}",
            GuidePhase.AwaitingRelease => $"RELEASE {Kind.ToString().ToUpperInvariant()} · {PressesCompleted + 1} of {_requiredPresses}",
            GuidePhase.Complete => "Capture complete",
            GuidePhase.Failed => FailureReason,
            _ => ""
        };
    }

    // During maximum selection, use the fixed Windows range rather than an
    // ever-growing captured peak. Otherwise every new peak would display100%.
    // After adjustment, PedalMapping.Percent uses the user's chosen maximum.
    internal double WindowsRangePercent(uint[] reading) => _axis < 0 || _direction == 0 ? 0 :
        Math.Clamp(_direction * (reading[_axis] - _rest[_axis]) / _range[_axis] * 100, 0, 100);

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
            _secondHighestPressDisplacement = 0;
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
                _secondHighestPressDisplacement = 0;
            }
            if (displacement >= pressThreshold)
                _secondHighestPressDisplacement = Math.Max(
                    _secondHighestPressDisplacement, Math.Min(_currentPeak, displacement));
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
                if (!HasSustainedPress)
                {
                    _pressAboveThresholdSamples = 0;
                    _firstPressAboveUtc = default;
                    _lastPressAboveUtc = default;
                    _currentPeak = 0;
                    _secondHighestPressDisplacement = 0;
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
                if (HasSustainedPress && _currentPeak >= pressThreshold)
                {
                    // A single extreme WinMM sample can otherwise become a
                    // false pedal endpoint even after an ordinary sustained hold.
                    // Reject this ambiguous capture rather than silently changing
                    // its peak or claiming that a sensor was verified.
                    if (_currentPeak > _secondHighestPressDisplacement * 2.0 &&
                        _currentPeak - _secondHighestPressDisplacement > _range[_axis] * 0.25)
                    {
                        Fail("Isolated WinMM peak detected. Release the pedal and retry capture smoothly.");
                        return;
                    }
                    double absolutePeak = _rest[_axis] + _direction * _currentPeak;
                    _peaks.Add((uint)Math.Clamp(Math.Round(absolutePeak), 0d, (double)uint.MaxValue));
                }
                _currentPeak = 0;
                _secondHighestPressDisplacement = 0;
                _pressAboveThresholdSamples = 0;
                _releaseBelowThresholdSamples = 0;
                Phase = GuidePhase.AwaitingPress;
                if (_peaks.Count == _requiredPresses) Finish();
            }
        }
    }

    private void Finish()
    {
        if (_axis < 0 || _peaks.Count != _requiredPresses) { Fail("Incomplete capture. Please retry."); return; }
        var rest = (uint)Math.Clamp(Math.Round(_rest[_axis]), 0, (double)uint.MaxValue);
        var distances = _peaks.Select(p => Math.Abs((double)p - rest)).OrderBy(d => d).ToArray();
        double medianDistance = distances[distances.Length / 2];
        if (medianDistance < _range[_axis] * 0.04)
        {
            Fail("Pedal movement too small. Please retry.");
            return;
        }
        var full = (uint)Math.Clamp(Math.Round((double)rest + _direction * medianDistance), 0, (double)uint.MaxValue);
        var spreadPercent = (distances[^1] - distances[0]) / medianDistance * 100;
        // A Windows-calibrated axis may clip at its reported min/max.
        // Three identical clipped readings do NOT prove repeatable physical force.
        double highThreshold = _deviceMax[_axis] - _range[_axis] * 0.002;
        double lowThreshold = _deviceMin[_axis] + _range[_axis] * 0.002;
        int clipped = _peaks.Count(p => _direction > 0
            ? p >= highThreshold : p <= lowThreshold);
        Result = new PedalCalibrationResult(
            new PedalMapping(_axis, rest, full, medianDistance / _range[_axis]),
            _restMax[_axis] - _restMin[_axis], _peaks.ToArray(),
            Math.Round(spreadPercent, 2), _restSamples, clipped >= (_requiredPresses == 1 ? 1 : 2));
        Phase = GuidePhase.Complete;
    }

    private void Fail(string message)
    {
        FailureReason = message;
        Phase = GuidePhase.Failed;
    }
}
