namespace MecaCalibrationWizard;

internal enum PedalKind { Brake, Throttle, Clutch }
internal enum SetupStage { Identification, ReadyToAdjust, Adjustment, Complete }

// #PEDAL_WORKFLOW: one role owns its heading, identified axis and result.
// Identify ALL enabled pedals first. Only then set each desired maximum on its
// locked axis. No manual axis selection, role swapping or device writes.
internal sealed class PedalSetup
{
    private readonly WinmmJoystick.Device _device;
    private readonly bool _clutch;
    private GuidedCalibration? _guide;
    private readonly Dictionary<PedalKind, int> _identifiedAxes = new();
    private readonly Dictionary<PedalKind, PedalCalibrationResult> _results = new();
    private uint? _brakeMaximum;
    public IReadOnlyDictionary<PedalKind, PedalCalibrationResult> Results => _results;
    public PedalKind CurrentPedal { get; private set; }
    public SetupStage Stage { get; private set; }
    public bool Calibrating => Stage == SetupStage.Adjustment;
    public bool Complete => Stage == SetupStage.Complete;
    public bool ReadyToAdjust => Stage == SetupStage.ReadyToAdjust;
    public int CurrentAxis => _identifiedAxes.TryGetValue(CurrentPedal, out var axis) ? axis : _guide?.Axis ?? -1;
    public string Failure { get; private set; } = "";
    public string Heading => Complete ? "Pedal test" : ReadyToAdjust ? "Pedals identified" :
        $"{(Calibrating ? "Set maximum for" : "Identify")} {CurrentPedal.ToString().ToLowerInvariant()}";
    public string Instruction(DateTime now)
    {
        if (ReadyToAdjust) return "All pedals identified. Continue to set your preferred pedal maximums.";
        if (Complete) return "Setup complete. Test each pedal.";
        string pedal = CurrentPedal.ToString().ToUpperInvariant();
        if (Calibrating && _guide?.Phase == GuidePhase.AwaitingPress)
            return $"How much do you want to press {pedal}? Press to your desired maximum, then release.";
        if (Calibrating && _guide?.Phase == GuidePhase.AwaitingRelease)
            return $"Release {pedal} to save this maximum.";
        return _guide?.Instruction(now) ?? "";
    }
    public double PreviewPercent(uint[] sample) => _guide?.WindowsRangePercent(sample) ?? 0;

    // #BRAKE_FINE_ADJUST: keep the measured endpoint intact; only the final
    // preview scale changes. A fixed5% step of the chosen span makes +/-
    // reversible away from limits. Greater span means more input for100%.
    public PedalMapping MappingFor(PedalKind role)
    {
        var mapping = _results[role].Mapping;
        return role == PedalKind.Brake && _brakeMaximum.HasValue
            ? mapping with { Full = _brakeMaximum.Value } : mapping;
    }
    public double BrakeScalePercent => !_results.ContainsKey(PedalKind.Brake) ? 100 :
        100 * Math.Abs((double)MappingFor(PedalKind.Brake).Full - _results[PedalKind.Brake].Mapping.Rest) /
        Math.Abs((double)_results[PedalKind.Brake].Mapping.Full - _results[PedalKind.Brake].Mapping.Rest);
    private uint? NextBrakeMaximum(int direction)
    {
        if (!Complete || Failure.Length != 0 || direction is not (-1 or 1)) return null;
        var original = _results[PedalKind.Brake].Mapping;
        var current = MappingFor(PedalKind.Brake);
        double sign = original.Full > original.Rest ? 1 : -1;
        double chosenSpan = Math.Abs((double)original.Full - original.Rest);
        double step = Math.Max(1, Math.Round(chosenSpan * .05));
        double maximum = sign > 0 ? (double)_device.Max[original.Axis] - original.Rest
            : (double)original.Rest - _device.Min[original.Axis];
        double minimum = Math.Max(1, Math.Ceiling(((double)_device.Max[original.Axis] - _device.Min[original.Axis]) * .05));
        double span = Math.Clamp(Math.Abs((double)current.Full - current.Rest) + direction * step, minimum, maximum);
        uint next = (uint)Math.Round(original.Rest + sign * span);
        return next != current.Full ? next : null;
    }
    public bool CanAdjustBrake(int direction) => NextBrakeMaximum(direction).HasValue;
    public bool AdjustBrake(int direction)
    {
        var next = NextBrakeMaximum(direction);
        if (!next.HasValue) return false;
        _brakeMaximum = next;
        return true;
    }

    public PedalSetup(WinmmJoystick.Device device, bool clutch, DateTime now)
    {
        _device = device;
        _clutch = clutch;
        StartPhase(now);
    }
    private void StartPhase(DateTime now) => _guide = new GuidedCalibration(
        CurrentPedal, _device, _identifiedAxes.Where(p => p.Key != CurrentPedal).Select(p => p.Value), now,
        requiredPresses: 1, identifiedAxis: Calibrating ? _identifiedAxes[CurrentPedal] : null);

    // #SAVED_SETUP: restore the original capture AND the brake's adjusted endpoint.
    // Call only after file validation, controller compatibility and user confirmation.
    internal static PedalSetup FromSaved(WinmmJoystick.Device device, PedalSettings saved, DateTime now)
    {
        PedalSettingsFile.Validate(saved);
        if (!PedalSettingsFile.Matches(saved, device))
            throw new System.IO.InvalidDataException("Saved settings do not match this controller.");
        var setup = new PedalSetup(device, saved.Pedals.Length == 3, now);
        foreach (var pedal in saved.Pedals)
        {
            setup._results.Add(pedal.Role, pedal.Capture);
            setup._identifiedAxes.Add(pedal.Role, pedal.Capture.Mapping.Axis);
            if (pedal.Role == PedalKind.Brake) setup._brakeMaximum = pedal.AppliedFull;
        }
        setup.Stage = SetupStage.Complete;
        setup._guide = null;
        return setup;
    }

    public bool BeginAdjustments(DateTime now)
    {
        if (!ReadyToAdjust || Failure.Length != 0) return false;
        Stage = SetupStage.Adjustment;
        CurrentPedal = PedalKind.Brake;
        StartPhase(now);
        return true;
    }

    public void Observe(uint[] sample, DateTime now)
    {
        if (_guide is null || Failure.Length != 0) return;
        _guide!.Observe(sample, now);
        if (_guide.Phase == GuidePhase.Failed) { Invalidate(_guide.FailureReason); return; }
        if (_guide.Result is not { } result) return;
        if (!Calibrating)
            _identifiedAxes[CurrentPedal] = result.Mapping.Axis; // identity is not calibration
        else
        {
            _results[CurrentPedal] = result;
        }
        if (CurrentPedal == PedalKind.Clutch || (CurrentPedal == PedalKind.Throttle && !_clutch))
        { Stage = Calibrating ? SetupStage.Complete : SetupStage.ReadyToAdjust; _guide = null; return; }
        CurrentPedal = CurrentPedal == PedalKind.Brake ? PedalKind.Throttle : PedalKind.Clutch;
        StartPhase(now); // fresh released baseline before the next named prompt
    }
    public void Invalidate(string reason)
    {
        Failure = reason;
        _results.Clear();
        _brakeMaximum = null;
        _identifiedAxes.Clear();
        _guide = null;
        Stage = SetupStage.Identification;
    }
}

internal sealed record PedalMapping(int Axis, uint Rest, uint Full, double Confidence)
{
    public double Percent(uint value)
    {
        var span = (double)Full - Rest;
        if (Math.Abs(span) < 1) return 0;
        return Math.Clamp((value - (double)Rest) / span * 100.0, 0, 100);
    }
}
