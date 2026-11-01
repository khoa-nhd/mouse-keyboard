using System.Drawing;
using System.Runtime.InteropServices;
using KeyboardMouse.Native;

namespace KeyboardMouse.Core;

public sealed record MonitorInfo(IntPtr Handle, Rectangle Bounds, Rectangle WorkArea, double Scale)
{
    public Point WorkCenter => new(
        WorkArea.Left + (WorkArea.Width / 2),
        WorkArea.Top + (WorkArea.Height / 2));
}

/// <summary>
/// Cached monitor geometry in physical pixels. Refreshed on display changes,
/// not on every tick. The pointer never uses keyboard layout or DPI-virtual
/// coordinates, so mixed-DPI setups stay consistent.
/// </summary>
public sealed class Monitors : IMonitorService
{
    private readonly object _gate = new();
    private MonitorInfo[] _monitors = [];

    public Monitors()
    {
        Refresh();
    }

    public IReadOnlyList<MonitorInfo> All => Snapshot();

    /// <summary>Re-enumerates monitors. Call on display settings changes.</summary>
    public void Refresh()
    {
        var found = new List<MonitorInfo>(2);

        try
        {
            User32.MonitorEnumProc callback = (IntPtr handle, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (User32.GetMonitorInfoW(handle, ref info))
                {
                    found.Add(new MonitorInfo(
                        handle,
                        ToRectangle(info.rcMonitor),
                        ToRectangle(info.rcWork),
                        GetScale(handle)));
                }

                return true;
            };

            User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Fall through to the virtual-screen fallback below.
        }

        if (found.Count == 0)
        {
            found.Add(FallbackMonitor());
        }

        var sorted = found
            .OrderBy(m => m.Bounds.Left)
            .ThenBy(m => m.Bounds.Top)
            .ToArray();

        lock (_gate)
        {
            _monitors = sorted;
        }
    }

    public Rectangle VirtualBounds
    {
        get
        {
            var all = Snapshot();
            if (all.Length == 0)
            {
                return Rectangle.Empty;
            }

            int left = all.Min(m => m.Bounds.Left);
            int top = all.Min(m => m.Bounds.Top);
            int right = all.Max(m => m.Bounds.Right);
            int bottom = all.Max(m => m.Bounds.Bottom);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
    }

    public MonitorInfo GetAtPoint(Point point)
    {
        var all = Snapshot();
        foreach (var monitor in all)
        {
            if (monitor.Bounds.Contains(point))
            {
                return monitor;
            }
        }

        return Nearest(all, point);
    }

    public double GetScaleAtPoint(Point point) => GetAtPoint(point).Scale;

    /// <summary>Monitor that comes after the pointer's monitor, wrapping around.</summary>
    public MonitorInfo Next(Point pointer)
    {
        var all = Snapshot();
        if (all.Length == 0)
        {
            return FallbackMonitor();
        }

        int index = -1;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].Bounds.Contains(pointer))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            index = IndexOfNearest(all, pointer);
        }

        return all[(index + 1) % all.Length];
    }

    /// <summary>
    /// Extended frame bounds (no invisible resize border). Null when there is
    /// no usable foreground window.
    /// </summary>
    public Rectangle? GetForegroundWindowBounds()
    {
        IntPtr hwnd = User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        RECT rect = default;
        int hr = Dwm.DwmGetWindowAttribute(
            hwnd,
            Dwm.DWMWA_EXTENDED_FRAME_BOUNDS,
            out rect,
            Marshal.SizeOf<RECT>());

        if (hr != 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            if (!User32.GetWindowRect(hwnd, out rect))
            {
                return null;
            }
        }

        var bounds = ToRectangle(rect);
        if (bounds.Width <= 0 || bounds.Height <= 0 ||
            !VirtualBounds.IntersectsWith(bounds))
        {
            return null;
        }

        return bounds;
    }

    public Point? GetForegroundWindowCenter()
    {
        var bounds = GetForegroundWindowBounds();
        if (bounds is null)
        {
            return null;
        }

        return new Point(
            bounds.Value.Left + (bounds.Value.Width / 2),
            bounds.Value.Top + (bounds.Value.Height / 2));
    }

    /// <summary>
    /// Clamps a floating-point position into the virtual desktop and then into
    /// the nearest real monitor, so the pointer cannot disappear into the gaps
    /// between monitors of different sizes or scaling.
    /// </summary>
    public (double X, double Y) ClampToDesktop(double x, double y)
    {
        var all = Snapshot();
        if (all.Length == 0)
        {
            return (x, y);
        }

        var virtualBounds = VirtualBoundsOf(all);
        x = Math.Clamp(x, virtualBounds.Left, virtualBounds.Right - 1);
        y = Math.Clamp(y, virtualBounds.Top, virtualBounds.Bottom - 1);

        var point = new Point((int)Math.Floor(x), (int)Math.Floor(y));
        var target = Nearest(all, point);
        var bounds = target.Bounds;

        x = Math.Clamp(x, bounds.Left, bounds.Right - 1);
        y = Math.Clamp(y, bounds.Top, bounds.Bottom - 1);
        return (x, y);
    }

    private MonitorInfo[] Snapshot()
    {
        lock (_gate)
        {
            return _monitors;
        }
    }

    private static Rectangle VirtualBoundsOf(MonitorInfo[] all)
    {
        int left = all.Min(m => m.Bounds.Left);
        int top = all.Min(m => m.Bounds.Top);
        int right = all.Max(m => m.Bounds.Right);
        int bottom = all.Max(m => m.Bounds.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static MonitorInfo Nearest(MonitorInfo[] all, Point point)
    {
        if (all.Length == 0)
        {
            return FallbackMonitor();
        }

        return all[IndexOfNearest(all, point)];
    }

    private static int IndexOfNearest(MonitorInfo[] all, Point point)
    {
        int best = 0;
        long bestDistance = long.MaxValue;

        for (int i = 0; i < all.Length; i++)
        {
            var r = all[i].Bounds;
            long dx = point.X < r.Left ? r.Left - point.X
                : point.X >= r.Right ? point.X - (r.Right - 1)
                : 0;
            long dy = point.Y < r.Top ? r.Top - point.Y
                : point.Y >= r.Bottom ? point.Y - (r.Bottom - 1)
                : 0;

            long distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private static MonitorInfo FallbackMonitor()
    {
        int left = User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN);
        int top = User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN);
        int width = Math.Max(1, User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN));
        int height = Math.Max(1, User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN));
        var bounds = new Rectangle(left, top, width, height);
        return new MonitorInfo(IntPtr.Zero, bounds, bounds, 1.0);
    }

    private static double GetScale(IntPtr monitor)
    {
        try
        {
            if (Shcore.GetDpiForMonitor(monitor, Shcore.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 &&
                dpiX > 0)
            {
                return Math.Clamp(dpiX / 96.0, 0.5, 4.0);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Windows without Shcore (pre-8.1): physical pixels are 96 DPI.
        }

        return 1.0;
    }

    private static Rectangle ToRectangle(RECT rect) =>
        Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
