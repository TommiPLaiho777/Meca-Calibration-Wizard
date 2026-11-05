namespace MecaCalibrationWizard;

// Reports *Windows WinMM* axis data; never a raw sensor/force or earthing diagnosis.
internal sealed record SignalHealthStats(
    int SampleCount, double WindowSeconds, double SamplingRateHz,
    uint MinimumWinmm, uint MaximumWinmm, uint SpanWinmm,
    double MedianWinmm, double StandardDeviationWinmm,
    uint LargestStepWinmm, int LargeStepCount, string Status,
    double? LargestSamplingGapSeconds = null,
    bool TimestampRegressionDetected = false)
{
    // A reported WinMM change is not proof of an electrical fault.
    // This guidance is exported alongside Status and does not change device state.
    // When both the sample range and timestamps are invalid, explain both
    // faults instead of hiding the impossible WinMM values behind timing advice.
    public string Guidance => Status == "Invalid WinMM range" && TimestampRegressionDetected
        ? "WinMM readings fell outside the available axis bounds (reported or assumed), and sample timestamps moved backwards. This idle check is invalid; refresh the controller and repeat without changing device settings."
        : TimestampRegressionDetected
        ? "WinMM sample timestamps moved backwards. This check stopped early; repeat without changing device settings."
        : Status switch
    {
        "Pedal motion likely" =>
            "Not a valid idle-noise check. Release all pedals and repeat; movement is not proof of EMI.",
        "No variation resolved" =>
            "No WinMM variation resolved. Windows deadzones may hide sensor noise.",
        "No samples" =>
            "No WinMM samples received. Check the controller connection and repeat.",
        "Insufficient samples" =>
            "Too few or too widely spaced WinMM samples to assess rest stability. Keep pedals released and repeat.",
        "Invalid WinMM sample" =>
            "An incomplete or malformed six-axis WinMM sample interrupted this idle check. Discard the result and repeat; no device settings were changed.",
        "Repeated WinMM timestamps" =>
            "WinMM sample timestamps repeated. This idle check stopped early and cannot establish signal stability; repeat without changing device settings.",
        "Invalid WinMM range" =>
            "WinMM readings fell outside the available axis bounds (reported or assumed). This idle check is invalid; refresh the controller and repeat without changing device settings.",
        _ => "WinMM variation alone cannot identify electrical interference."
    };
}

internal static class SignalHealthDiagnostics
{
    // Conservative minimum *average* polling rate for a useful at-rest assessment.
    // Not a claim about the USB HID report rate or native load-cell sampling.
    private const double MinimumUsefulAverageSamplingRateHz = 5.0;
    // A gap above 0.5s is inconclusive for a 3s or 10s idle check.
    // This also prevents a +0.6s Windows wall-clock jump from producing a
    // valid-looking flat trace: timestamps alone cannot prove continuous sampling.
    private const double MaximumUsefulSamplingGapSeconds = 0.5;

    public static SignalHealthStats Evaluate(
        IReadOnlyList<uint> readings, uint axisMin, uint axisMax, double windowSeconds,
        double? largestSamplingGapSeconds = null,
        bool timestampRegressionDetected = false)
    {
        if (axisMax <= axisMin) throw new ArgumentException("Invalid WinMM axis range");
        if (largestSamplingGapSeconds is double gap && (!double.IsFinite(gap) || gap < 0))
            throw new ArgumentOutOfRangeException(nameof(largestSamplingGapSeconds));
        if (!double.IsFinite(windowSeconds) || windowSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        if (readings.Count == 0)
            return new(0, windowSeconds, 0, 0, 0, 0, 0, 0, 0, 0,
                timestampRegressionDetected ? "Insufficient samples" : "No samples",
                largestSamplingGapSeconds, timestampRegressionDetected);
        var sorted = readings.OrderBy(x => x).ToArray();
        double median = sorted.Length % 2 == 0
            ? ((double)sorted[sorted.Length/2-1] + sorted[sorted.Length/2])/2
            : sorted[sorted.Length/2];
        double mean = readings.Average(x => (double)x);
        double sd = Math.Sqrt(readings.Average(x => Math.Pow(x - mean, 2)));
        uint minimum = sorted[0], maximum = sorted[^1], largest = 0;
        uint span = maximum - minimum;
        double scale = (double)axisMax - axisMin;
        int jumps = 0;
        for (int i = 1; i < readings.Count; i++)
        {
            uint step = readings[i] >= readings[i-1]
                ? readings[i] - readings[i-1] : readings[i-1] - readings[i];
            largest = Math.Max(largest, step);
            if (step > .005 * scale) jumps++;
        }
        double averageRate = windowSeconds > 0 ? (readings.Count - 1) / windowSeconds : 0;
        // A flat trace sampled too sparsely must not be presented as a valid
        // zero-variation result. Preserve the numerical metrics for review.
        // An out-of-range WinMM sample invalidates an idle assessment even
        // when all readings are identical. A flat but impossible trace must
        // never be reported as "No variation resolved".
        bool outsideReportedRange = readings.Any(x => x < axisMin || x > axisMax);
        string status = outsideReportedRange ? "Invalid WinMM range"
            : timestampRegressionDetected ||
              readings.Count < 8 || windowSeconds < .5 ||
              averageRate < MinimumUsefulAverageSamplingRateHz ||
              largestSamplingGapSeconds is > MaximumUsefulSamplingGapSeconds
            ? "Insufficient samples"
            : span > .02 * scale ? "Pedal motion likely"
            : span > .003 * scale ? "Variation observed"
            : span == 0 ? "No variation resolved"
            : "Small variation resolved";
        return new(readings.Count, Math.Round(windowSeconds, 3),
            Math.Round(averageRate, 2),
            minimum, maximum, span, Math.Round(median, 2), Math.Round(sd, 2),
            largest, jumps, status,
            largestSamplingGapSeconds.HasValue
                ? Math.Round(largestSamplingGapSeconds.Value, 3) : null,
            timestampRegressionDetected);
    }
}

internal sealed class NoiseRun
{
    public const double DurationSeconds = 3.0;
    public const double ExtendedDurationSeconds = 10.0;
    public double RequestedDurationSeconds { get; }
    public DateTime StartedUtc { get; }
    private DateTime _lastUtc;
    private double _largestSamplingGapSeconds;
    private bool _timestampRegressionDetected;
    private bool _repeatedTimestampDetected;
    private bool _invalidSampleDetected;
    private readonly List<uint>[] _samples =
        Enumerable.Range(0, 6).Select(_ => new List<uint>()).ToArray();
    public bool HasSamples => _samples[0].Count > 0;
    // End an invalid capture immediately rather than waiting indefinitely for a
    // wall clock that may have been moved backwards during the measurement.
    public bool Complete => _timestampRegressionDetected || _repeatedTimestampDetected ||
        _invalidSampleDetected ||
        (_lastUtc - StartedUtc).TotalSeconds >= RequestedDurationSeconds;

    public NoiseRun(DateTime? startedUtc = null, double durationSeconds = DurationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.5 || durationSeconds > 60)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        RequestedDurationSeconds = durationSeconds;
        StartedUtc = startedUtc ?? DateTime.UtcNow;
        _lastUtc = StartedUtc;
    }
    public void Observe(uint[]? values, DateTime? atUtc = null)
    {
        if (Complete) return;
        // Never silently skip a malformed six-axis WinMM sample and then
        // report the remaining idle trace as a complete stability check.
        if (values is null || values.Length != WinmmJoystick.AxisNames.Length)
        {
            _invalidSampleDetected = true;
            return;
        }
        var now = atUtc ?? DateTime.UtcNow;
        if (now < _lastUtc)
        {
            // DateTime.UtcNow is a wall clock, not a monotonic timer.
            // Do not mistake a clock rollback for a sampling gap or silently
            // accept a misleading stationary signal. Keep earlier readings.
            _timestampRegressionDetected = true;
            return;
        }
        // The first sample may occur exactly at capture start, but two actual
        // samples at the same timestamp cannot establish a valid polling rate.
        // Stop rather than allowing duplicate timestamps to inflate sample count.
        if (now == _lastUtc && HasSamples)
        {
            _repeatedTimestampDetected = true;
            return;
        }
        // Include the delay before the first sample; a high average rate
        // can otherwise hide a long interval without any WinMM readings.
        _largestSamplingGapSeconds = Math.Max(_largestSamplingGapSeconds,
            (now - _lastUtc).TotalSeconds);
        for (int i = 0; i < 6; i++) _samples[i].Add(values[i]);
        _lastUtc = now;
    }
    public uint Spread(int axis) => HasSamples ? _samples[axis].Max()-_samples[axis].Min() : 0;
    public SignalHealthStats Analyze(int axis, uint min, uint max)
    {
        var stats = SignalHealthDiagnostics.Evaluate(_samples[axis], min, max,
            Math.Max(0, (_lastUtc - StartedUtc).TotalSeconds),
            _largestSamplingGapSeconds, _timestampRegressionDetected);
        // Preserve numeric evidence but explicitly reject the interrupted check.
        return _invalidSampleDetected ? stats with { Status = "Invalid WinMM sample" }
            : _repeatedTimestampDetected ? stats with { Status = "Repeated WinMM timestamps" }
            : stats;
    }
}
