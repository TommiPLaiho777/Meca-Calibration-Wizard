using System.Windows;
using System.Threading;

namespace MecaCalibrationWizard;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // #SINGLE_INSTANCE: keep one setup session on this Windows desktop.
        // Retain the mutex for the application's lifetime to close the startup
        // race. Older builds lack this guard and must be closed before testing.
        // Never close another app or discard its in-progress pedal settings.
        _instance = new Mutex(true, @"Local\MecaCalibrationWizard.PedalSetup", out _ownsInstance);
        if (!_ownsInstance)
        {
            MessageBox.Show("MECA is already open. Use that window, or close it before starting this version.",
                "MECA already running", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
