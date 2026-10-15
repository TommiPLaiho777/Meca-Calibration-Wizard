namespace MecaCalibrationWizard;

internal enum PedalKind { Brake, Throttle, Clutch }

internal sealed record PedalMapping(int Axis, uint Rest, uint Full, double Confidence)
{
    public double Percent(uint value)
    {
        var span = (double)Full - Rest;
        if (Math.Abs(span) < 1) return 0;
        return Math.Clamp((value - (double)Rest) / span * 100.0, 0, 100);
    }
}

// WinMM-reported values, not uncalibrated load-cell ADC values.
internal sealed record NoiseAxisStatistics(int SampleCount, uint MinimumWinmm, uint MaximumWinmm,
    double MedianWinmm, double StandardDeviationWinmm, uint LargestAdjacentStepWinmm,
    double ObservedSamplesPerSecond);

internal sealed class NoiseRun
{
    public DateTime StartedUtc { get; }
    private readonly List<(DateTime Time, uint[] Axes)> _samples = new();
    public bool HasSamples => _samples.Count > 0;
    public bool Complete => (DateTime.UtcNow - StartedUtc).TotalSeconds >= 3.0;
    public NoiseRun(DateTime? startedUtc = null) => StartedUtc = startedUtc ?? DateTime.UtcNow;

    public void Observe(uint[] values, DateTime? utcNow = null)
    {
        if (values.Length != 6) throw new ArgumentException("Expected six axes.", nameof(values));
        _samples.Add((utcNow ?? DateTime.UtcNow, (uint[])values.Clone()));
    }

    public uint Spread(int axis)
    {
        if (axis is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(axis));
        return HasSamples ? _samples.Max(x => x.Axes[axis]) - _samples.Min(x => x.Axes[axis]) : 0;
    }

    public NoiseAxisStatistics? Statistics(int axis)
    {
        if (axis is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(axis));
        if (!HasSamples) return null;
        uint[] values = _samples.Select(x => x.Axes[axis]).ToArray();
        uint[] sorted = values.OrderBy(x => x).ToArray();
        int n = values.Length;
        double median = n % 2 == 1 ? sorted[n / 2] : sorted[n / 2 - 1] / 2.0 + sorted[n / 2] / 2.0;
        double mean = values.Average(x => (double)x);
        double deviation = Math.Sqrt(values.Average(x => Math.Pow(x - mean, 2)));
        uint largestStep = 0;
        for (int i = 1; i < n; i++)
            largestStep = Math.Max(largestStep, (uint)Math.Abs((long)values[i] - values[i - 1]));
        double seconds = (_samples[^1].Time - _samples[0].Time).TotalSeconds;
        double rate = n > 1 && seconds > 0 ? (n - 1) / seconds : 0;
        return new NoiseAxisStatistics(n, sorted[0], sorted[^1], Math.Round(median, 2),
            Math.Round(deviation, 2), largestStep, Math.Round(rate, 2));
    }
}
