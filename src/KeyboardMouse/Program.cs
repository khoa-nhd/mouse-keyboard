using KeyboardMouse.Platform;

namespace KeyboardMouse;

internal static class Program
{
    private const string MutexName = @"Local\KeyboardMouse-SingleInstance";

    [STAThread]
    private static void Main()
    {
        // Single instance: the hook must exist exactly once per session.
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) =>
        {
            Log.Error("Unhandled UI thread exception", e.Exception);
            TrayApp.Current?.EmergencyStop();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            // The process is going down; make sure no button stays pressed.
            TrayApp.EmergencyRelease();
        };

        try
        {
            using var app = new TrayApp();
            Log.Info("KeyboardMouse started.");
            Application.Run(app);
        }
        catch (Exception ex)
        {
            Log.Error("Fatal error", ex);
            TrayApp.EmergencyRelease();
        }
        finally
        {
            Log.Info("KeyboardMouse stopped.");
        }
    }
}
