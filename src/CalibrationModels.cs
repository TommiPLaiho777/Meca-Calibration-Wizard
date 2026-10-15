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
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    private readonly uint[] _min = Enumerable.Repeat(uint.MaxValue, 6).ToArray();
    private readonly uint[] _max = new uint[6];
    public bool HasSamples { get; private set; }
    public bool Complete => (DateTime.UtcNow - StartedUtc).TotalSeconds >= 3.0;

    public void Observe(uint[] value)
    {
        HasSamples = true;
        for (int i = 0; i < 6; i++)
        {
            _min[i] = Math.Min(_min[i], value[i]);
            _max[i] = Math.Max(_max[i], value[i]);
        }
    }

    public uint Spread(int axis) => HasSamples ? _max[axis] - _min[axis] : 0;
}
