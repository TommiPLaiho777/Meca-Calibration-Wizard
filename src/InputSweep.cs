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
        $"{AtHighLimitSamples} at high axis limit";

    // WinMM reports Windows joystick-axis values, not the native sensor ADC.
    // Neither reaching nor avoiding the axis ceiling establishes physical force.
    public string HighLimitInterpretation => AtHighLimitSamples > 0
        ? "Windows WinMM high axis limit observed. This does not verify sensor ADC clipping or physical pedal force."
        : "No Windows WinMM high-limit samples observed. This does not establish sensor headroom or physical pedal force.";
}

internal sealed class InputSweepRun
{
    public const double DurationSeconds = 8;
    private readonly int _axis;
    private readonly uint _lower;
    private readonly uint _upper;
    private readonly double _range;
    private readonly DateTime _startUtc;
    private readonly List<SweepPoint> _trace = new();
    public bool Complete { get; private set; }
    public InputSweepResult? Result { get; private set; }
    public double Progress(DateTime time) => Math.Clamp((time - _startUtc).TotalSeconds / DurationSeconds, 0, 1);

    public InputSweepRun(int axis, uint min, uint max, DateTime? startUtc = null)
    {
        if (axis is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(axis));
        if (min >= max) throw new ArgumentException("Axis minimum must be lower than maximum.");
        _axis = axis;
        _lower = min;
        _upper = max;
        _range = (double)max - min;
        _startUtc = startUtc ?? DateTime.UtcNow;
    }

    public void Observe(uint[] reading, DateTime utcNow)
    {
        if (Complete || reading.Length <= _axis) return;
        // A Windows clock adjustment can move UTC backwards mid-capture.
        // Drop retrograde samples rather than exporting a nonmonotonic trace.
        long timeMs = (long)(utcNow - _startUtc).TotalMilliseconds;
        if (timeMs < 0 || (_trace.Count > 0 && timeMs < _trace[^1].TimeMs)) return;
        _trace.Add(new SweepPoint(timeMs, reading[_axis]));
        if (Progress(utcNow) < 1) return;

        // Within 0.2% of the device's WinMM-reported min/max counts as at-limit.
        var margin = Math.Max(1, _range * 0.002);
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
