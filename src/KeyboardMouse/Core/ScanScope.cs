using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// How much of the desktop a hint scan covers. Foreground window is the
/// historical behaviour and stays the default, so an upgraded install scans
/// exactly what it scanned before until the user opts into a wider scope.
/// </summary>
public enum HintScanScope
{
    /// <summary>The foreground window plus its owned popups (menus, dialogs).</summary>
    ForegroundWindow,

    /// <summary>The monitor that holds the pointer when H is pressed.</summary>
    ActiveMonitor,

    /// <summary>Every connected monitor; targets must land on a real monitor.</summary>
    AllMonitors,

    /// <summary>The whole virtual desktop, including the empty gaps between monitors.</summary>
    VirtualDesktop
}

/// <summary>
/// String ids used by settings.json, so the file stays readable and an unknown
/// value can never make deserialization fail.
/// </summary>
public static class ScanScopeNames
{
    public const string Foreground = "foreground";
    public const string ActiveMonitor = "activeMonitor";
    public const string AllMonitors = "allMonitors";
    public const string VirtualDesktop = "virtualDesktop";

    public static string ToId(HintScanScope scope) => scope switch
    {
        HintScanScope.ActiveMonitor => ActiveMonitor,
        HintScanScope.AllMonitors => AllMonitors,
        HintScanScope.VirtualDesktop => VirtualDesktop,
        _ => Foreground
    };

    public static HintScanScope Parse(string? id) => id?.Trim() switch
    {
        ActiveMonitor => HintScanScope.ActiveMonitor,
        AllMonitors => HintScanScope.AllMonitors,
        VirtualDesktop => HintScanScope.VirtualDesktop,
        // Anything unknown (including null and hand-edited typos) keeps the
        // historical, cheapest, most predictable scope.
        _ => HintScanScope.ForegroundWindow
    };
}

/// <summary>
/// Pure geometry for scopes. All values are physical pixels, the same space the
/// pointer, the overlays, UI Automation bounds and SendInput already use, so no
/// DPI conversion happens here and none is needed.
/// </summary>
public static class ScanScopeMath
{
    /// <summary>
    /// The rectangle the scan should cover. Foreground scope uses the window's
    /// own bounds (the scanner widens it to include owned popups); monitors use
    /// their real bounds, never an assumed (0,0) origin.
    /// </summary>
    public static Rectangle ResolveRegion(
        HintScanScope scope,
        IReadOnlyList<MonitorInfo> monitors,
        Point anchor,
        Rectangle? foregroundBounds)
    {
        if (monitors.Count == 0)
        {
            return foregroundBounds ?? Rectangle.Empty;
        }

        return scope switch
        {
            HintScanScope.ForegroundWindow => foregroundBounds ?? MonitorAt(monitors, anchor).Bounds,
            HintScanScope.ActiveMonitor => MonitorAt(monitors, anchor).Bounds,
            _ => Union(monitors)
        };
    }

    /// <summary>Monitors a scope may place targets on. Foreground/active use one.</summary>
    public static IReadOnlyList<MonitorInfo> MonitorsInScope(
        HintScanScope scope,
        IReadOnlyList<MonitorInfo> monitors,
        Point anchor)
    {
        if (monitors.Count == 0)
        {
            return [];
        }

        return scope switch
        {
            HintScanScope.ForegroundWindow or HintScanScope.ActiveMonitor => [MonitorAt(monitors, anchor)],
            _ => monitors
        };
    }

    /// <summary>Monitor that contains the point, else the nearest one.</summary>
    public static MonitorInfo MonitorAt(IReadOnlyList<MonitorInfo> monitors, Point point)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.Bounds.Contains(point))
            {
                return monitor;
            }
        }

        return Nearest(monitors, point);
    }

    /// <summary>
    /// Bounding box of every monitor. Handles negative origins and mixed sizes;
    /// gaps between uneven monitors are included, which is exactly what the
    /// virtual-desktop scope means.
    /// </summary>
    public static Rectangle Union(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            return Rectangle.Empty;
        }

        int left = monitors.Min(m => m.Bounds.Left);
        int top = monitors.Min(m => m.Bounds.Top);
        int right = monitors.Max(m => m.Bounds.Right);
        int bottom = monitors.Max(m => m.Bounds.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    public static Rectangle Union(Rectangle first, Rectangle second)
    {
        if (first.Width <= 0 || first.Height <= 0)
        {
            return second;
        }

        if (second.Width <= 0 || second.Height <= 0)
        {
            return first;
        }

        int left = Math.Min(first.Left, second.Left);
        int top = Math.Min(first.Top, second.Top);
        int right = Math.Max(first.Right, second.Right);
        int bottom = Math.Max(first.Bottom, second.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static MonitorInfo Nearest(IReadOnlyList<MonitorInfo> monitors, Point point)
    {
        int best = 0;
        long bestDistance = long.MaxValue;

        for (int i = 0; i < monitors.Count; i++)
        {
            var r = monitors[i].Bounds;
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

        return monitors[best];
    }
}
