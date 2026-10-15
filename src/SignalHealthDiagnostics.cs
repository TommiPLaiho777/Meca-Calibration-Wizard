namespace MecaCalibrationWizard;

// Reports *Windows WinMM* axis data; never a raw sensor/force or earthing diagnosis.
internal sealed record SignalHealthStats(
    int SampleCount, double WindowSeconds, double SamplingRateHz,
    uint MinimumWinmm, uint MaximumWinmm, uint SpanWinmm,
    double MedianWinmm, double StandardDeviationWinmm,
    uint LargestStepWinmm, int LargeStepCount, string Status)
{
    // A reported WinMM change is not proof of an electrical fault.
    // This guidance is exported alongside Status and does not change device state.
    public string Guidance => Status switch
    {
        "Pedal motion likely" =>
            "Not a valid idle-noise check. Release all pedals and repeat; movement is not proof of EMI.",
        "No variation resolved" =>
            "No WinMM variation resolved. Windows deadzones may hide sensor noise.",
        "No samples" or "Insufficient samples" =>
            "Too little data. Release all pedals and repeat the check.",
        _ => "WinMM variation alone cannot identify electrical interference."
    };
}

internal static class SignalHealthDiagnostics
{
    public static SignalHealthStats Evaluate(
        IReadOnlyList<uint> readings, uint axisMin, uint axisMax, double windowSeconds)
    {
        if (axisMax <= axisMin) throw new ArgumentException("Invalid WinMM axis range");
        if (!double.IsFinite(windowSeconds) || windowSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        if (readings.Count == 0)
            return new(0, windowSeconds, 0, 0, 0, 0, 0, 0, 0, 0, "No samples");
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
        string status = readings.Count < 8 || windowSeconds < .5 ? "Insufficient samples"
            : span > .02 * scale ? "Pedal motion likely"
            : span > .003 * scale ? "Variation observed"
            : span == 0 ? "No variation resolved"
            : "Small variation resolved";
        return new(readings.Count, Math.Round(windowSeconds, 3),
            Math.Round(windowSeconds > 0 ? (readings.Count-1)/windowSeconds : 0, 2),
            minimum, maximum, span, Math.Round(median, 2), Math.Round(sd, 2),
            largest, jumps, status);
    }
}
