using KeyboardMouse.Platform;

namespace KeyboardMouse;

internal static class Program
{
    private const string MutexName = @"Local\KeyboardMouse-SingleInstance";
    private const string ShowEventName = @"Local\KeyboardMouse-ShowGuide";

    [STAThread]
    private static void Main(string[] args)
    {
        // Started by the autostart entry: stay silent in the tray. Started by
        // the user (double-click, Start menu): open the guide window.
        bool background = args.Any(a => string.Equals(a, Autostart.BackgroundArgument, StringComparison.OrdinalIgnoreCase));

        // Single instance: the hook must exist exactly once per session. A
        // second launch just asks the running instance to show its window.
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!createdNew)
        {
            if (!background)
            {
                // This launch came from the user, so it may hand the
                // foreground to the running instance's window.
                Native.User32.AllowSetForegroundWindow(Native.User32.ASFW_ANY);
                showEvent.Set();
            }

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

        RegisteredWaitHandle? listener = null;
        try
        {
            using var app = new TrayApp(showGuide: !background);
            listener = ListenForShowRequests(showEvent, app);
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
            listener?.Unregister(null);
            Log.Info("KeyboardMouse stopped.");
        }
    }

    /// <summary>Opens the guide whenever another launch signals the event.</summary>
    private static RegisteredWaitHandle ListenForShowRequests(EventWaitHandle showEvent, TrayApp app) =>
        ThreadPool.RegisterWaitForSingleObject(
            showEvent,
            (_, _) => app.RequestShowGuide(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
}
