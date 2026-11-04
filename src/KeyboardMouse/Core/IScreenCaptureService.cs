using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using KeyboardMouse.Core;

namespace KeyboardMouse.Core;

/// <summary>
/// Source-neutral contract for screen capture.  Implementors take a snapshot
/// of the desktop or a window and return it in a format scanners can consume,
/// without retaining the bitmap after the callback completes.
/// </summary>
public interface IScreenCaptureService : IDisposable
{
    /// <summary>
    /// Captures the foreground window and returns a bitmap plus capture metadata.
    /// The caller receives ownership of the bitmap and must dispose it.
    /// </summary>
    /// <param name="cancellationToken">Optional token to abort the capture.</param>
    /// <returns>A capture result containing the bitmap and its geometry.</returns>
    CaptureResult CaptureForegroundWindow(CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures a specific monitor and returns a bitmap plus capture metadata.
    /// The caller receives ownership of the bitmap and must dispose it.
    /// </summary>
    /// <param name="monitor">The monitor to capture.</param>
    /// <param name="cancellationToken">Optional token to abort the capture.</param>
    /// <returns>A capture result containing the bitmap and its geometry.</returns>
    CaptureResult CaptureMonitor(MonitorInfo monitor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures a rectangular region of the desktop (screen coordinates) and
    /// returns a bitmap plus capture metadata.
    /// The caller receives ownership of the bitmap and must dispose it.
    /// </summary>
    /// <param name="region">Rectangle in the canonical physical-pixel coordinate space.</param>
    /// <param name="cancellationToken">Optional token to abort the capture.</param>
    /// <returns>A capture result containing the bitmap and its geometry.</returns>
    CaptureResult CaptureRegion(TargetBounds region, CancellationToken cancellationToken = default);
}

/// <summary>
/// Capture result returned by <see cref="IScreenCaptureService"/>.
/// The caller owns the <see cref="Bitmap"/> and must dispose it.
/// </summary>
public sealed record CaptureResult
{
    /// <summary>
    /// The captured bitmap in the canonical physical-pixel coordinate space.
    /// Caller must dispose.
    /// </summary>
    public Bitmap Bitmap { get; init; }

    /// <summary>
    /// The desktop-origin rectangle that was captured.  In the canonical
    /// physical-pixel space; may be negative.
    /// </summary>
    public TargetBounds Region { get; init; }

    /// <summary>
    /// The scale factor applied during capture.  1.0 means a native-resolution
    /// capture; other values indicate scaling was used.
    /// </summary>
    public double DpiScale { get; init; } = 1.0;

    /// <summary>
    /// The window handle captured, if any.  Null for pure monitor captures.
    /// </summary>
    public IntPtr? WindowHandle { get; init; }

    /// <summary>
    /// The monitor handle captured, if any.  Null for window/region captures.
    /// </summary>
    public IntPtr? MonitorHandle { get; init; }
}

/// <summary>
/// Capture metadata that accompanies a <see cref="CaptureResult"/>.
/// These values describe how the capture was produced and are passed through
/// to <see cref="VisualTarget"/> metadata during fusion.  No raw pixel data
/// or recognized text is stored here.
/// </summary>
public sealed record CaptureMetadata
{
    /// <summary>
    /// The desktop-origin rectangle that was captured.
    /// </summary>
    public TargetBounds Region { get; init; }

    /// <summary>
    /// The scale factor applied during capture.  1.0 means a native-resolution
    /// capture; other values indicate scaling was used.
    /// </summary>
    public double DpiScale { get; init; } = 1.0;

    /// <summary>
    /// The window handle captured, if any.  Null for pure monitor captures.
    /// </summary>
    public IntPtr? WindowHandle { get; init; }

    /// <summary>
    /// The monitor handle that was captured, if applicable.
    /// </summary>
    public IntPtr? MonitorHandle { get; init; }

    /// <summary>
    /// Indicates whether the capture was a full-screen grab.
    /// </summary>
    public bool IsFullScreen { get; init; }

    /// <summary>
    /// Optional error information.  Non-null when the capture failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// A capture that covers the entire virtual desktop across all monitors.
    /// </summary>
    public static CaptureMetadata FullScreen()
    {
        Rectangle virtualScreen = SystemInformation.VirtualScreen;

        return new CaptureMetadata
        {
            Region = new TargetBounds(
                virtualScreen.X,
                virtualScreen.Y,
                virtualScreen.Width,
                virtualScreen.Height),
            DpiScale = 1.0,
            WindowHandle = null,
            MonitorHandle = IntPtr.Zero,
            IsFullScreen = true,
            Error = null
        };
    }

    /// <summary>
    /// Creates metadata for a window capture.
    /// </summary>
    public static CaptureMetadata FromWindow(IntPtr window, Rectangle desktopRegion)
    {
        return new CaptureMetadata
        {
            Region = TargetBounds.FromRectangle(desktopRegion),
            DpiScale = 1.0,
            WindowHandle = window,
            MonitorHandle = IntPtr.Zero,
            IsFullScreen = false,
            Error = null
        };
    }

    /// <summary>
    /// Creates metadata for a monitor capture.
    /// </summary>
    public static CaptureMetadata FromMonitor(MonitorInfo monitor)
    {
        return new CaptureMetadata
        {
            Region = TargetBounds.FromRectangle(monitor.Bounds),
            DpiScale = monitor.Scale,
            WindowHandle = null,
            MonitorHandle = monitor.Handle,
            IsFullScreen = false,
            Error = null
        };
    }

    /// <summary>
    /// Creates metadata for a region capture.
    /// </summary>
    public static CaptureMetadata FromRegion(TargetBounds region)
    {
        return new CaptureMetadata
        {
            Region = region,
            DpiScale = 1.0,
            WindowHandle = null,
            MonitorHandle = IntPtr.Zero,
            IsFullScreen = false,
            Error = null
        };
    }
}