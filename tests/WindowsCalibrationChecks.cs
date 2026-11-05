using System.IO;
using MecaCalibrationWizard;

static class WindowsCalibrationChecks
{
    internal static void Run()
    {
        var before = new WindowsCalibrationSnapshot(1, "WINDOWS_DIRECTINPUT_CALIBRATION", Guid.NewGuid(),
            "synthetic-device", "MECA CUP1", new[] { new AxisCalibration(4, 1026, 800, 6500, 13000), new AxisCalibration(5, 1282, 5500, 12150, 18800) });
        var after = before with { Axes = new[] { before.Axes[0] with { Max = 14000 }, before.Axes[1] } };
        WindowsCalibrationSnapshot state = before;
        int writes = 0, notifications = 0, backups = 0;
        void Write(AxisCalibration a)
        { writes++; state = state with { Axes = state.Axes.Select(x => x.Axis == a.Axis ? a : x).ToArray() }; }
        void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
        void Refuse(Action a)
        { try { a(); } catch (Exception e) when (e is IOException or InvalidDataException) { return; } throw new Exception("Expected refusal"); }
        CalibrationCommit.Run(before, after, () => state, Write, () => notifications++, () => backups++);
        Check(writes == 1 && notifications == 1 && backups == 1 && state.Axes.SequenceEqual(after.Axes), "Normal commit or unaffected axis changed");
        state = before; writes = 0;
        Refuse(() => CalibrationCommit.Run(before, after, () => state, Write, () => {}, () => throw new IOException("Backup unavailable")));
        Check(writes == 0, "Write happened before backup");
        state = after;
        Refuse(() => CalibrationCommit.Run(before, after, () => state, Write, () => {}, () => {}));
        Check(writes == 0, "Stale before image accepted");
        state = before;
        Refuse(() => CalibrationCommit.Run(before, after with { Instance = Guid.NewGuid() }, () => state, Write, () => {}, () => {}));
        Check(writes == 0, "Different controller accepted");
        bool failed = false;
        Refuse(() => CalibrationCommit.Run(before, after, () => state, a => { Write(a); if (!failed) { failed = true; throw new IOException("Partial setter failure"); } }, () => {}, () => {}));
        Check(state.Axes.SequenceEqual(before.Axes), "Partial setter failure did not restore before image");
        notifications = 0;
        Refuse(() => CalibrationCommit.Run(before, after, () => state, Write, () => { if (++notifications == 1) throw new IOException("Notify failed"); }, () => {}));
        Check(state.Axes.SequenceEqual(before.Axes) && notifications == 2, "Notify failure did not restore and re-notify");
        Refuse(() => CalibrationCommit.Run(before, after, () => state, _ => {}, () => {}, () => {}));
        Check(state.Axes.SequenceEqual(before.Axes), "Ignored setter reported success");
        try
        {
            CalibrationCommit.Run(before, after, () => state, _ => throw new IOException("Setter unavailable"), () => {}, () => {});
            throw new Exception("Incomplete rollback misreported");
        }
        catch (AggregateException) { }
        string folder = Path.Combine(Environment.GetEnvironmentVariable("MECA_TEST_SCRATCH")!, "Windows-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "before.json");
            WindowsCalibration.SaveBackup(path, before);
            var loaded = WindowsCalibration.LoadBackup(path);
            Check(loaded.Instance == before.Instance && loaded.InterfacePath == before.InterfacePath && loaded.Axes.SequenceEqual(before.Axes), "Native calibration backup changed values or identity");
            File.WriteAllText(path, "{broken}");
            try { WindowsCalibration.LoadBackup(path); throw new Exception("Corrupt Windows backup accepted"); }
            catch (InvalidDataException) { }
        }
        finally { Directory.Delete(folder, true); }
        Console.WriteLine("PASS: Windows calibration transaction, exact before-image/identity, backup-before-write, untouched axes, readback, partial setter/notify rollback and explicit incomplete recovery");
    }
}
