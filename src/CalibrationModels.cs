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

internal sealed class NoiseRun
{
    public const double DurationSeconds = 3.0;
    public const double ExtendedDurationSeconds = 10.0;
    public double RequestedDurationSeconds { get; }
    public DateTime StartedUtc { get; }
    private DateTime _lastUtc;
    private readonly List<uint>[] _samples =
        Enumerable.Range(0, 6).Select(_ => new List<uint>()).ToArray();
    public bool HasSamples => _samples[0].Count > 0;
    public bool Complete => (_lastUtc - StartedUtc).TotalSeconds >= RequestedDurationSeconds;

    public NoiseRun(DateTime? startedUtc = null, double durationSeconds = DurationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.5 || durationSeconds > 60)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        RequestedDurationSeconds = durationSeconds;
        StartedUtc = startedUtc ?? DateTime.UtcNow;
        _lastUtc = StartedUtc;
    }
    public void Observe(uint[] values, DateTime? atUtc = null)
    {
        if (values.Length != 6) return;
        for (int i = 0; i < 6; i++) _samples[i].Add(values[i]);
        _lastUtc = atUtc ?? DateTime.UtcNow;
    }
    public uint Spread(int axis) => HasSamples ? _samples[axis].Max()-_samples[axis].Min() : 0;
    public SignalHealthStats Analyze(int axis, uint min, uint max) =>
        SignalHealthDiagnostics.Evaluate(_samples[axis], min, max,
            Math.Max(0, (_lastUtc - StartedUtc).TotalSeconds));
}
