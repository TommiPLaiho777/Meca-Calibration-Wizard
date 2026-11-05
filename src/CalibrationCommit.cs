using System.IO;

namespace MecaCalibrationWizard;

// #CALIBRATION_TRANSACTION: persistent changes require exact identity, a current
// before-image, a completed backup and readback. Failure attempts restoration
// under the same configuration lock; incomplete restoration is a distinct error.
internal static class CalibrationCommit
{
    internal static void Validate(WindowsCalibrationSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Version != 1 || snapshot.Scope != "WINDOWS_DIRECTINPUT_CALIBRATION" ||
            snapshot.Instance == Guid.Empty || string.IsNullOrWhiteSpace(snapshot.InterfacePath) ||
            snapshot.Name != "MECA CUP1" || snapshot.Axes is not { Length: >= 2 and <= 6 } ||
            snapshot.Axes.Any(a => a is null || a.Axis is < 0 or > 5 || a.ObjectId < 0 || a.Min >= a.Center || a.Center >= a.Max) ||
            snapshot.Axes.Select(a => a.Axis).Distinct().Count() != snapshot.Axes.Length ||
            snapshot.Axes.Select(a => a.ObjectId).Distinct().Count() != snapshot.Axes.Length)
            throw new InvalidDataException("Invalid Windows calibration snapshot.");
    }
    private static bool SameContext(WindowsCalibrationSnapshot a, WindowsCalibrationSnapshot b) =>
        a.Version == b.Version && a.Scope == b.Scope && a.Instance == b.Instance &&
        a.InterfacePath == b.InterfacePath && a.Name == b.Name &&
        a.Axes.Select(x => (x.Axis, x.ObjectId)).OrderBy(x => x.Axis)
            .SequenceEqual(b.Axes.Select(x => (x.Axis, x.ObjectId)).OrderBy(x => x.Axis));
    internal static bool SameValues(WindowsCalibrationSnapshot a, WindowsCalibrationSnapshot b) =>
        SameContext(a, b) && a.Axes.OrderBy(x => x.Axis).SequenceEqual(b.Axes.OrderBy(x => x.Axis));
    internal static void Run(WindowsCalibrationSnapshot before, WindowsCalibrationSnapshot after,
        Func<WindowsCalibrationSnapshot> read, Action<AxisCalibration> write, Action notify, Action backup)
    {
        Validate(before); Validate(after);
        if (!SameContext(before, after)) throw new IOException("Controller or axis identity differs; no changes applied.");
        var current = read(); Validate(current);
        if (!SameValues(before, current)) throw new IOException("Windows calibration changed since review; no changes applied.");
        backup(); // Must succeed before the first persistent setter, including restore.
        var changes = after.Axes.Where(a => a != before.Axes.Single(b => b.Axis == a.Axis)).ToArray();
        try
        {
            foreach (var change in changes) write(change);
            notify();
            if (!SameValues(after, read())) throw new IOException("Windows did not retain the requested calibration.");
        }
        catch (Exception failure)
        {
            try
            {
                var observed = read();
                if (!SameContext(before, observed)) throw new IOException("Controller identity changed; automatic restore refused.");
                foreach (var change in changes) write(before.Axes.Single(a => a.Axis == change.Axis));
                notify();
                if (!SameValues(before, read())) throw new IOException("Restoration readback differs.");
            }
            catch (Exception recovery)
            { throw new AggregateException("Windows calibration failed and restoration is incomplete. Preserve the backup and stop using this setup.", failure, recovery); }
            throw new IOException("Windows calibration failed; earlier calibration was restored and verified.", failure);
        }
    }
}
