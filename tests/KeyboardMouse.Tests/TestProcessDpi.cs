using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KeyboardMouse.Tests;

/// <summary>
/// The application runs as PerMonitorV2 (manifest + Program.SetHighDpiMode).
/// The xunit host is DPI-unaware by default, which makes WinForms, UI Automation
/// and screen-capture coordinates disagree on a scaled display (for example
/// 150%), so CV tests would capture a shifted region. Match the app here.
/// </summary>
internal static class TestProcessDpi
{
    private const int PerMonitorAwareV2 = -4;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [ModuleInitializer]
    internal static void Initialize()
    {
        // Harmless when the test host already fixed the process awareness.
        _ = SetProcessDpiAwarenessContext(new IntPtr(PerMonitorAwareV2));
    }
}
