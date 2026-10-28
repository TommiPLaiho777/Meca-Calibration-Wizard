namespace MecaCalibrationWizard;

// Samples Windows WinMM joystick output only. It cannot read the native load-cell ADC
// or establish at which physical force the Windows signal begins saturating.
internal sealed record SweepPoint(long TimeMs, uint WinmmValue);

internal sealed record InputSweepResult(
    string InputSource,
    uint MinimumWinmm,
    uint MaximumWinmm,
    int SampleCount,
    int DistinctValues,
    int IntermediateSamples,
    int AtLowLimitSamples,
    int AtHighLimitSamples,
    long? FirstHighLimitMs,
    int LongestHighLimitRunSamples,
    IReadOnlyList<SweepPoint> Trace)
{
    public string Summary =>
        $"{SampleCount} samples · {DistinctValues} distinct · {IntermediateSamples} intermediate · " +
        $"{AtHighLimitSamples} in high-end WinMM band (not necessarily exact axis maximum)";

    // WinMM reports Windows joystick-axis values, not the native sensor ADC.
    // Neither reaching nor avoiding the axis ceiling establishes physical force.
    public string HighLimitInterpretation => AtHighLimitSamples > 0
        ? "Windows WinMM high-end axis band observed. This does not verify sensor ADC clipping or physical pedal force; band counts alone do not establish the exact Windows axis maximum."
        : "No Windows WinMM high-limit samples observed. This does not establish sensor headroom or physical pedal force.";

    // The raw WinMM high band is the released endpoint for an inverted mapping.
    // Keep the existing property for compatibility; only mapped exports need this context.
    public string HighLimitInterpretationFor(bool inverted) => !inverted
        ? HighLimitInterpretation
        : AtHighLimitSamples > 0
            ? "Windows WinMM high-end axis band observed at the released end of this inverted mapping. This does not verify sensor ADC clipping or physical pedal force; band counts alone do not establish the exact Windows axis maximum."
            : "No Windows WinMM high-end axis band samples observed at the released end of this inverted mapping. This does not establish sensor headroom or physical pedal force.";
}

internal sealed class InputSweepRun
{
    public const double DurationSeconds = 8;
    private readonly int _axis;
    private readonly uint _lower;
    private readonly uint _upper;
    private readonly double _range;
    private readonly bool _requireContinuousSamples;
    private readonly bool? _axisRangeAssumed;
    private const long MaximumContinuousGapMs = 500;
    private readonly DateTime _startUtc;
    private readonly List<SweepPoint> _trace = new();
    public bool Complete { get; private set; }
    public bool Failed { get; private set; }
    public string FailureReason { get; private set; } = "";
    public InputSweepResult? Result { get; private set; }
    public double Progress(DateTime time) => Math.Clamp((time - _startUtc).TotalSeconds / DurationSeconds, 0, 1);

    public InputSweepRun(int axis, uint min, uint max, DateTime? startUtc = null,
        bool requireContinuousSamples = true, bool? axisRangeAssumed = null)
    {
        if (axis is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(axis));
        if (min >= max) throw new ArgumentException("Axis minimum must be lower than maximum.");
        _axis = axis;
        _lower = min;
        _upper = max;
        _range = (double)max - min;
        _requireContinuousSamples = requireContinuousSamples;
        _axisRangeAssumed = axisRangeAssumed;
        _startUtc = startUtc ?? DateTime.UtcNow;
    }

    public void Observe(uint[]? reading, DateTime utcNow)
    {
        if (Complete || Failed) return;
        // A WinMM joyGetPosEx sample must contain exactly six axes. Silently
        // ignoring a truncated sample would leave a live sweep in progress and
        // could let later readings complete a misleading partial trace.
        if (reading is null || reading.Length != WinmmJoystick.AxisNames.Length)
        {
            Failed = true;
            FailureReason = "WinMM sweep sample has an invalid axis count. Release the pedal and retry.";
            return;
        }
        long timeMs = (long)(utcNow - _startUtc).TotalMilliseconds;
        long previousMs = _trace.Count > 0 ? _trace[^1].TimeMs : 0;
        // Values outside the controller's advertised WinMM axis range cannot
        // support a valid endpoint assessment. Fail closed rather than counting
        // an out-of-range value as a high- or low-limit sample.
        if (reading[_axis] < _lower || reading[_axis] > _upper)
        {
            Failed = true;
            string provenance = _axisRangeAssumed switch
            {
                true => "assumed fallback",
                false => "reported",
                null => "unverified"
            };
            FailureReason = $"WinMM sweep reading outside the {provenance} axis range. Release the pedal and retry.";
            return;
        }
        // In a real sweep, a clock rollback, repeated millisecond timestamp,
        // or polling stall over 0.5s invalidates timing evidence. A repeated
        // timestamp is rejected only after the first sample: the initial
        // sample may legitimately be at t=0. Never export an ambiguous trace.
        // Sparse synthetic endpoint-statistic checks can opt out explicitly.
        if (_requireContinuousSamples &&
            (timeMs < previousMs ||
             (_trace.Count > 0 && timeMs == previousMs) ||
             timeMs - previousMs > MaximumContinuousGapMs))
        {
            Failed = true;
            FailureReason = "WinMM sweep timing interrupted. Release the pedal and retry.";
            return;
        }
        // Legacy sparse diagnostic fixtures ignore retrograde observations;
        // they are not used by the live WPF capture path.
        if (timeMs < 0 || timeMs < previousMs) return;
        _trace.Add(new SweepPoint(timeMs, reading[_axis]));
        if (Progress(utcNow) < 1) return;

        // For normal WinMM ranges, count samples within 0.2% of either axis limit.
        // Cap the margin on narrow axes so low/high bands never overlap.
        var margin = Math.Min(Math.Max(1.0, _range * 0.002), _range / 3.0);
        var lower = _lower + margin;
        var upper = _upper - margin;
        var midLow = _lower + _range * 0.02;
        var midHigh = _upper - _range * 0.02;
        int high = 0, low = 0, between = 0, longest = 0, run = 0;
        long? firstHigh = null;
        foreach (var point in _trace)
        {
            if (point.WinmmValue >= upper)
            {
                high++;
                run++;
                longest = Math.Max(longest, run);
                firstHigh ??= point.TimeMs;
            }
            else run = 0;
            if (point.WinmmValue <= lower) low++;
            if (point.WinmmValue > midLow && point.WinmmValue < midHigh) between++;
        }
        Result = new InputSweepResult(
            "WINMM_JOYGETPOSEX_NOT_SENSOR_ADC",
            _trace.Min(p => p.WinmmValue), _trace.Max(p => p.WinmmValue),
            _trace.Count, _trace.Select(p => p.WinmmValue).Distinct().Count(),
            between, low, high, firstHigh, longest, _trace.ToArray());
        Complete = true;
    }
}
