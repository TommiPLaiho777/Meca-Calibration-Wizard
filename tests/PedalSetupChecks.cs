using MecaCalibrationWizard;

// Real setup core, generated samples only. No controller or WPF calls.
static class SetupChecks
{
    static readonly WinmmJoystick.Device Device = new(0, "Synthetic", new uint[6], Enumerable.Repeat(65535u, 6).ToArray());
    static DateTime now;
    static uint[] baseline = new uint[6];
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    static uint[] Frame(int axis = -1, uint value = 0) { var sample = (uint[])baseline.Clone(); if (axis >= 0) sample[axis] = value; return sample; }
    static void Send(PedalSetup setup, uint[] sample, int count)
    { for (int i = 0; i < count; i++) { now = now.AddMilliseconds(40); setup.Observe(sample, now); } }
    static void Rest(PedalSetup setup) => Send(setup, Frame(), 34);
    static void Press(PedalSetup setup, int axis, uint value = 50000)
    { Send(setup, Frame(axis, value), 6); Send(setup, Frame(), 4); }
    static void Run(bool clutch, int[] axes)
    {
        baseline = new uint[6];
        now = new DateTime(2026, 11, 4, 20, 0, 0, DateTimeKind.Utc);
        var setup = new PedalSetup(Device, clutch, now);
        var roles = clutch ? new[] { PedalKind.Brake, PedalKind.Throttle, PedalKind.Clutch } : new[] { PedalKind.Brake, PedalKind.Throttle };
        foreach (var role in roles)
        {
            Check(setup.CurrentPedal == role && !setup.Calibrating && setup.Heading == $"Identify {role.ToString().ToLowerInvariant()}", "Identification heading/role mismatch");
            Rest(setup);
            Check(setup.Instruction(now).Contains($"PRESS {role.ToString().ToUpperInvariant()}"), "Prompt must name the physical role");
            Press(setup, axes[(int)role]);
            Check(!setup.Calibrating && setup.Results.Count == 0, "Identification must not set any maximum");
        }
        Check(setup.ReadyToAdjust && setup.Results.Count == 0, "All identification must finish before adjustment");
        Check(setup.BeginAdjustments(now), "Explicit start of adjustment round refused");
        foreach (var role in roles)
        {
            Check(setup.Calibrating && setup.CurrentPedal == role, "Adjustment role order mismatch");
            Rest(setup);
            Check(setup.Instruction(now).Contains("desired maximum"), "Desired-maximum prompt missing");
            Send(setup, Frame(axes[(int)role], 10000), 6);
            Check(Math.Abs(setup.PreviewPercent(Frame(axes[(int)role], 10000)) - 10000d / 65535 * 100) < 1e-9,
                "Selection gauge must use fixed Windows range, not auto-normalize each new peak to100%");
            Send(setup, Frame(axes[(int)role], 25000), 6);
            Send(setup, Frame(), 4);
            Check(setup.Results[role].Mapping.Axis == axes[(int)role] && setup.Results[role].Mapping.Full == 25000,
                "Maximum selection changed identified axis or ignored desired endpoint");
        }
        Check(setup.Complete && setup.Results.Count == roles.Length && setup.Failure.Length == 0, "Workflow not complete");
        // Independent role-specific inputs, deliberately varying physical-axis order.
        foreach (var role in roles)
        {
            var sample = Frame(axes[(int)role], 12500);
            foreach (var other in roles)
                Check(setup.Results[other].Mapping.Percent(sample[setup.Results[other].Mapping.Axis]) == (role == other ? 50 : 0), "Pedal display would use another role's axis");
        }
        var chosenBrake = setup.Results[PedalKind.Brake].Mapping;
        var throttle = setup.MappingFor(PedalKind.Throttle);
        Check(setup.AdjustBrake(1) && setup.MappingFor(PedalKind.Brake).Full > chosenBrake.Full,
            "Heavier must require more input, not less");
        Check(setup.MappingFor(PedalKind.Brake).Percent(12500) < 50 && setup.Results[PedalKind.Brake].Mapping == chosenBrake,
            "Fine adjustment must change only preview sensitivity, preserving the measured endpoint");
        Check(setup.AdjustBrake(-1) && setup.MappingFor(PedalKind.Brake) == chosenBrake, "Plus then minus should restore chosen maximum");
        for (int i=0;i<100;i++) setup.AdjustBrake(1);
        Check(setup.MappingFor(PedalKind.Brake).Full == 65535 && !setup.CanAdjustBrake(1), "Heavier exceeds Windows range");
        for (int i=0;i<100;i++) setup.AdjustBrake(-1);
        Check(setup.MappingFor(PedalKind.Brake).Full >= 3277 && !setup.CanAdjustBrake(-1), "Lighter creates a tiny noise-sensitive input span");
        Check(setup.MappingFor(PedalKind.Throttle) == throttle && (!clutch || setup.MappingFor(PedalKind.Clutch) == setup.Results[PedalKind.Clutch].Mapping),
            "Brake adjustment changed throttle or clutch");
        SettingsFileChecks.Run(Device, setup);
        setup.Invalidate("disconnected");
        Check(!setup.Complete && setup.Results.Count == 0 && !setup.AdjustBrake(1), "Invalid session retains accepted results or permits adjustment");
    }
    public static void Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--backup-format" })) { WindowsBackupFormatChecks.Run(); return; }
        WindowsBackupFormatChecks.Run();
        WindowsCalibrationChecks.Run();
        Run(false, new[] { 0, 3, 2 });
        Run(false, new[] { 3, 0, 2 });
        Run(true, new[] { 4, 1, 5 });
        now = DateTime.UtcNow;
        var locked = new PedalSetup(Device, false, now);
        Check(!locked.BeginAdjustments(now), "Adjustment started before identification");
        Rest(locked); Press(locked, 3); Rest(locked); Press(locked, 0);
        Check(locked.BeginAdjustments(now), "Completed identification cannot start adjustment");
        Rest(locked);
        for (int p = 0; p < 3; p++) Press(locked, 0);
        Check(locked.Calibrating && locked.Results.Count == 0, "Wrong pedal re-identifies calibration");
        locked.Observe(new uint[] { 65536, 0, 0, 0, 0, 0 }, now.AddMilliseconds(40));
        Check(locked.Failure.Length != 0 && locked.Results.Count == 0, "Invalid sample accepted");
        now = DateTime.UtcNow;
        var stalled = new PedalSetup(Device, false, now);
        stalled.Observe(Frame(), now.AddSeconds(1));
        Check(stalled.Failure.Length != 0, "Polling interruption accepted");
        baseline = new uint[] { 65535, 0, 0, 0, 0, 0 };
        now = DateTime.UtcNow;
        var inverted = new PedalSetup(Device, false, now);
        Check(!inverted.AdjustBrake(1), "Adjustment allowed before setup");
        Rest(inverted); Press(inverted, 0, 10000); Rest(inverted); Press(inverted, 3);
        Check(inverted.BeginAdjustments(now), "Inverted setup cannot begin adjustment");
        Rest(inverted); Press(inverted, 0, 30000); Rest(inverted); Press(inverted, 3, 25000);
        Check(inverted.Complete && inverted.AdjustBrake(1) && inverted.MappingFor(PedalKind.Brake).Full < 30000,
            "Heavier direction incorrect for inverted brake");
        Check(inverted.AdjustBrake(-1) && inverted.MappingFor(PedalKind.Brake).Full == 30000,
            "Inverted plus/minus is not reversible");
        SettingsFileChecks.Run(Device, inverted);
        Console.WriteLine("PASS: two/three pedal workflow, all-identification-first barrier, explicit adjustment round, selected maximum, fixed-range live gauge, locked axes, role-specific input, wrong-pedal refusal, session invalidation, timing and brake +/- for normal/inverted axes with bounds and unchanged other pedals");
    }
}
