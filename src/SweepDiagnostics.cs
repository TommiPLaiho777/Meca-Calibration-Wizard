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
        // A direct assessment may also receive cached/imported traces that
        // did not pass the live InputSweepRun bounds check. Never clamp an
        // impossible WinMM reading into a seemingly valid press/release.
        if (trace.Any(point => point.WinmmValue < axisMin || point.WinmmValue > axisMax))
            return new SweepAssessment(false, false,
                trace.Select(point => point.WinmmValue).Distinct().Count(),
                0, 0, "Invalid WinMM range",
                "WinMM readings are outside the axis bounds used for this preview (reported or assumed). Discard this sweep and capture again.");

        // Imported/cached WinMM traces must have an unambiguous sample order.
        // Equal millisecond timestamps can otherwise describe an impossible
        // instantaneous press and release. Live WinMM capture samples much
        // farther apart than 1 ms; reject duplicates as well as rollbacks.
        for (int i = 0; i < trace.Count; i++)
        {
            if (trace[i].TimeMs < 0 ||
                (i > 0 && trace[i].TimeMs <= trace[i - 1].TimeMs))
                return new SweepAssessment(false, false,
                    trace.Select(point => point.WinmmValue).Distinct().Count(),
                    0, 0, "Invalid sweep timing",
                    "Sweep timestamps are negative, repeated, or move backwards. Discard this trace and capture again.");
        }

        // Even a short imported trace must disclose impossible axis values or
        // ambiguous timestamps before falling back to insufficient data.
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
        // A single final low WinMM sample may be a transient drop rather than
        // evidence of a completed release. Require a descending three-sample
        // return toward rest, including two near-rest samples spanning >=80 ms.
        // This checks Windows signal shape, not physical pedal force.
        bool releaseTailConfirmed = values.Length >= 3 &&
            values[^3] <= .20 && values[^2] <= .10 && endedNearRest &&
            values[^3] >= values[^2] - .025 &&
            values[^2] >= values[^1] - .025 &&
            trace[^1].TimeMs - trace[^3].TimeMs >= 80;
        bool strokeShapeComplete = startedNearRest && pressIndex > 0 &&
            endedNearRest && releaseTailConfirmed;
        // Imported/cached traces bypass the live eight-second capture gate.
        // A few milliseconds of plausible-looking values, or a large missing
        // interval, must not be reported as a complete physical pedal sweep.
        // These are Windows sample-timing checks, not force measurements.
        bool timingEvidenceSufficient = trace[^1].TimeMs - trace[0].TimeMs >= 2000 &&
            !Enumerable.Range(1, trace.Count - 1)
                .Any(i => trace[i].TimeMs - trace[i - 1].TimeMs > 500);
        // A two-level or similarly quantized WinMM signal can mimic a full stroke
        // without enough intermediate evidence to validate the capture.
        bool resolutionSufficient = distinct >= 5;
        // A brief spike or two high readings do not establish a held press.
        // Require three consecutive high-direction samples spanning at least
        // 100 ms. The existing 500 ms trace-gap check handles sparse captures;
        // do not reject valid slow sweeps (e.g. 320 ms between readings).
        // This is WinMM timing evidence, not physical pedal force.
        int pressedRunStart = -1, pressedRunSamples = 0;
        bool pressedHoldSufficient = false;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < .80)
            {
                pressedRunStart = -1;
                pressedRunSamples = 0;
                continue;
            }
            if (pressedRunStart < 0)
            {
                pressedRunStart = i;
                pressedRunSamples = 0;
            }
            pressedRunSamples++;
            if (pressedRunSamples >= 3 &&
                trace[i].TimeMs - trace[pressedRunStart].TimeMs >= 100)
            {
                pressedHoldSufficient = true;
                break;
            }
        }
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
        // One guided sweep means one substantial press and one release.
        // An otherwise plausible trace with a second press/release is not
        // evidence of a single completed stroke.
        bool complete = strokeShapeComplete && timingEvidenceSufficient &&
            resolutionSufficient && pressedHoldSufficient && reversals == 1;

        string status;
        string explanation;
        if (observedTravel < 20)
        {
            status = "Little movement";
            explanation = "The recorded axis did not move through a useful range. Repeat the sweep.";
        }
        else if (!resolutionSufficient)
        {
            status = "Insufficient signal steps";
            explanation = "Too few distinct Windows WinMM values were observed to validate a press and release. Try a slower sweep; sensor resolution and pedal force remain unverified.";
        }
        else if (strokeShapeComplete && !timingEvidenceSufficient)
        {
            status = "Insufficient sweep timing";
            explanation = "The WinMM trace is too short or contains a sampling gap over 0.5 seconds. Record a continuous eight-second sweep.";
        }
        else if (strokeShapeComplete && !pressedHoldSufficient)
        {
            status = "Pressed hold not confirmed";
            explanation = "A brief WinMM peak does not confirm a held pedal press. Repeat the sweep, hold briefly, then release; physical pedal force remains unverified.";
        }
        else if (strokeShapeComplete && timingEvidenceSufficient &&
                 resolutionSufficient && pressedHoldSufficient && reversals > 1)
        {
            status = "Multiple stroke reversals";
            explanation = "More than one substantial WinMM direction reversal was observed. Record one slow press, hold, and release; this is not a physical-force assessment.";
        }
        else if (startedNearRest && pressIndex > 0 && endedNearRest &&
                 timingEvidenceSufficient && resolutionSufficient &&
                 pressedHoldSufficient && !releaseTailConfirmed)
        {
            status = "Release not confirmed";
            explanation = "A single final low WinMM reading cannot confirm release. Return toward rest earlier and hold briefly before the timer ends; physical pedal force remains unverified.";
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
                : inverted
                    ? "Press and release recorded. The Windows axis high endpoint was not observed (released end of this inverted mapping); this does not establish physical pedal force."
                    : "Press and release recorded. The Windows axis maximum was not reached in this sweep.";
        }
        return new SweepAssessment(complete, full, distinct, Math.Round(observedTravel, 2),
            reversals, status, explanation);
    }
}
