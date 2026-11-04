using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// Turns a hint scan scope into physical-pixel desktop regions, then splits
/// large regions into overlapping tiles.
///
/// All values are in the canonical virtual-desktop space: physical pixels,
/// possibly negative, never DIPs and never monitor-local. The same rectangle
/// can therefore be passed to <see cref="IScreenCaptureService.CaptureRegion"/>
/// and to hint overlays without another conversion.
/// </summary>
public static class ScreenScanPlanner
{
    /// <summary>
    /// Largest tile edge. Keeping tiles near 1280 px means the CV detector's
    /// 640 px analysis grid samples at most every other pixel instead of every
    /// fourth or sixth pixel on a 4K display.
    /// </summary>
    public const int MaxTileDimension = 1280;

    /// <summary>
    /// Tiles overlap so a control that straddles a boundary is fully visible in
    /// at least one tile and is not cut into two unrelated hints.
    /// </summary>
    public const int TileOverlap = 96;

    /// <summary>Safety cap; real desktops produce far fewer tiles.</summary>
    public const int MaxTiles = 64;

    /// <summary>
    /// Resolves the capture regions for a scope.
    /// </summary>
    /// <param name="scope">Requested scan scope.</param>
    /// <param name="windowBounds">
    /// Foreground window rectangle in desktop pixels. May be empty when the
    /// handle disappeared; callers then fall back to a foreground capture.
    /// </param>
    /// <param name="monitors">Physical monitor geometry.</param>
    /// <param name="virtualScreen">Fallback virtual-screen rectangle.</param>
    public static IReadOnlyList<TargetBounds> RegionsForScope(
        HintScanScope scope,
        Rectangle windowBounds,
        IReadOnlyList<MonitorInfo> monitors,
        Rectangle virtualScreen)
    {
        MonitorInfo[] valid = (monitors ?? [])
            .Where(monitor => monitor.Bounds.Width > 0 && monitor.Bounds.Height > 0)
            .ToArray();

        Rectangle desktop = valid.Length > 0
            ? Union(valid)
            : virtualScreen;

        if (desktop.Width <= 0 || desktop.Height <= 0)
        {
            return [];
        }

        switch (scope)
        {
            case HintScanScope.ForegroundWindow:
            {
                if (windowBounds.Width <= 0 || windowBounds.Height <= 0)
                {
                    return [];
                }

                Rectangle clipped = Rectangle.Intersect(windowBounds, desktop);
                return clipped.Width > 0 && clipped.Height > 0
                    ? [TargetBounds.FromRectangle(clipped)]
                    : [];
            }

            case HintScanScope.ActiveMonitor:
            {
                Point anchor = windowBounds.Width > 0 && windowBounds.Height > 0
                    ? Center(windowBounds)
                    : Center(desktop);

                MonitorInfo chosen = valid.Length > 0
                    ? Nearest(valid, anchor)
                    : new MonitorInfo(
                        IntPtr.Zero,
                        desktop,
                        desktop,
                        1.0);

                Rectangle clipped = Rectangle.Intersect(chosen.Bounds, desktop);
                return clipped.Width > 0 && clipped.Height > 0
                    ? [TargetBounds.FromRectangle(clipped)]
                    : [];
            }

            case HintScanScope.VirtualDesktop:
            case HintScanScope.AllMonitors:
            {
                // Capturing each monitor instead of one bounding rectangle keeps
                // the gaps between differently sized monitors out of the scan and
                // preserves every monitor's own origin.
                if (valid.Length > 0)
                {
                    return valid
                        .OrderBy(monitor => monitor.Bounds.Left)
                        .ThenBy(monitor => monitor.Bounds.Top)
                        .Select(monitor => TargetBounds.FromRectangle(monitor.Bounds))
                        .ToArray();
                }

                return [TargetBounds.FromRectangle(desktop)];
            }

            default:
                return [];
        }
    }

    /// <summary>
    /// Splits a region into overlapping tiles of at most
    /// <paramref name="maxDimension"/> pixels per edge. Tiles always stay inside
    /// <paramref name="region"/> and cover it without gaps.
    /// </summary>
    public static IReadOnlyList<TargetBounds> Tile(
        TargetBounds region,
        int maxDimension = MaxTileDimension,
        int overlap = TileOverlap)
    {
        if (!region.IsValid)
        {
            return [];
        }

        int max = Math.Max(32, maxDimension);
        int shared = Math.Clamp(overlap, 0, max / 2);

        int[] xs = Starts(region.Left, region.Width, max, shared);
        int[] ys = Starts(region.Top, region.Height, max, shared);

        var tiles = new List<TargetBounds>(xs.Length * ys.Length);

        foreach (int y in ys)
        {
            foreach (int x in xs)
            {
                int width = Math.Min(max, region.Right - x);
                int height = Math.Min(max, region.Bottom - y);

                if (width <= 0 || height <= 0)
                {
                    continue;
                }

                tiles.Add(new TargetBounds(x, y, width, height));

                if (tiles.Count >= MaxTiles)
                {
                    return tiles;
                }
            }
        }

        return tiles;
    }

    private static int[] Starts(int start, int length, int max, int overlap)
    {
        if (length <= max)
        {
            return [start];
        }

        int step = max - overlap;
        var starts = new List<int>();

        for (int offset = 0; ; offset += step)
        {
            if (offset + max >= length)
            {
                starts.Add(start + length - max);
                break;
            }

            starts.Add(start + offset);

            if (starts.Count >= MaxTiles)
            {
                break;
            }
        }

        return starts
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
    }

    private static Rectangle Union(IReadOnlyList<MonitorInfo> monitors)
    {
        int left = monitors.Min(monitor => monitor.Bounds.Left);
        int top = monitors.Min(monitor => monitor.Bounds.Top);
        int right = monitors.Max(monitor => monitor.Bounds.Right);
        int bottom = monitors.Max(monitor => monitor.Bounds.Bottom);

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static MonitorInfo Nearest(IReadOnlyList<MonitorInfo> monitors, Point point)
    {
        MonitorInfo best = monitors[0];
        long bestDistance = long.MaxValue;

        foreach (MonitorInfo monitor in monitors)
        {
            long dx = point.X - monitor.Bounds.Left - (monitor.Bounds.Width / 2);
            long dy = point.Y - monitor.Bounds.Top - (monitor.Bounds.Height / 2);
            long distance = (dx * dx) + (dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = monitor;
            }
        }

        return best;
    }

    private static Point Center(Rectangle rectangle) => new(
        rectangle.Left + (rectangle.Width / 2),
        rectangle.Top + (rectangle.Height / 2));
}
