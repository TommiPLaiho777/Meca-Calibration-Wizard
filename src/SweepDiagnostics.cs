namespace MecaCalibrationWizard;

// Deterministic interpretation of WinMM-reported joystick traces.
// No inference about raw ADC counts, real pedal force or electrical grounding.
internal sealed record SweepAssessment(
    bool CompletePressAndRelease,
    bool ReachesReportedMaximum,
    int DistinctValues,
    double ObservedTravelPercent,
    int DirectionChanges,
    string Status,
    string Explanation);

internal static class SweepDiagnostics
{
    public static SweepAssessment Evaluate(IReadOnlyList<SweepPoint> trace, uint axisMin, uint axisMax, bool inverted = false)
    {
        if (axisMax <= axisMin) throw new ArgumentException("Invalid Windows-reported axis range.");
        if (trace.Count < 3)
            return new SweepAssessment(false, false, trace.Select(x => x.WinmmValue).Distinct().Count(),
                0, 0, "Insufficient data", "Record an eight-second press, hold and release sweep.");

        double scale = (double)axisMax - axisMin;
        // Classify press/release in pedal direction, not raw WinMM axis direction.
        // Inverted mappings rest near the high endpoint and press toward the low endpoint.
        // Keep ReachesReportedMaximum tied to the raw Windows axis maximum below.
        double[] values = trace.Select(s =>
        {
            double rawFraction = Math.Clamp((s.WinmmValue - (double)axisMin) / scale, 0, 1);
            return inverted ? 1 - rawFraction : rawFraction;
        }).ToArray();
        var distinct = trace.Select(s => s.WinmmValue).Distinct().Count();
        var min = values.Min();
        var max = values.Max();
        // Only an observed exact WinMM endpoint establishes that the reported axis maximum was reached.
        // A 98–99.9% reading is near the limit, not proof of an endpoint sample.
        bool full = trace.Any(point => point.WinmmValue == axisMax);
        double observedTravel = (max - min) * 100;

        // A complete guided sweep must start released, reach the pressed end,
        // and finish released. Searching for any early/late rest sample can
        // misclassify a trace that started pressed or ended with a second press.
        int pressIndex = Array.FindIndex(values, x => x >= .80);
        bool startedNearRest = values[0] <= .03;
        bool endedNearRest = values[^1] <= .03;
        bool complete = startedNearRest && pressIndex > 0 && endedNearRest;

        // Count substantial directional reversals; suppress tiny quantization noise.
        const double changeThreshold = .025;
        int previousDirection = 0, reversals = 0;
        double anchor = values[0];
        foreach (double current in values.Skip(1))
        {
            var diff = current - anchor;
            if (Math.Abs(diff) < changeThreshold) continue;
            int direction = Math.Sign(diff);
            if (previousDirection != 0 && direction != previousDirection) reversals++;
            previousDirection = direction;
            anchor = current;
        }

        string status;
        string explanation;
        if (observedTravel < 20 || distinct < 5)
        {
            status = "Little movement";
            explanation = "The recorded axis did not move through a useful range. Repeat the sweep.";
        }
        else if (!complete)
        {
            status = "Incomplete sweep";
            explanation = "Start released, press to your normal maximum, then release completely before the timer ends.";
        }
        else if (distinct < 12)
        {
            status = "Limited intermediate steps";
            explanation = "Few distinct Windows-reported values were observed; try a slower press and release.";
        }
        else
        {
            status = "Sweep captured";
            // On inverted pedals the raw WinMM high endpoint is the released end,
            // not the pressed end. Do not imply the pedal saturated under load.
            explanation = full
                ? inverted
                    ? "Press and release recorded. Windows axis high endpoint was observed at the released end of this inverted mapping; this does not establish pedal-force clipping."
                    : "Press and release recorded. Windows axis reached its limit; actual sensor-force clipping is unknown."
                : "Press and release recorded. The Windows axis maximum was not reached in this sweep.";
        }
        return new SweepAssessment(complete, full, distinct, Math.Round(observedTravel, 2),
            reversals, status, explanation);
    }
}
