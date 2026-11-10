using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// One top-level window as the scanner sees it. Kept as plain data so window
/// selection is a pure, testable function instead of living inside P/Invoke.
/// Bounds are physical pixels and may have negative coordinates.
/// </summary>
public sealed record ScreenWindow(
    IntPtr Handle,
    Rectangle Bounds,
    uint ProcessId,
    bool Visible,
    bool Cloaked,
    bool Minimized,
    IntPtr Owner,
    string ClassName = "")
{
    /// <summary>
    /// Window class used by every Mouse Keyboard feedback overlay (hint labels,
    /// grid, cursor dot, screen border). Those windows must never become hint
    /// targets themselves, while our normal windows (the guide) still can.
    /// </summary>
    public const string OverlayClassName = "KeyboardMouseOverlay";

    public bool IsOwnOverlay =>
        ClassName.Equals(OverlayClassName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Shell taskbars are handled by the taskbar switch, not by the normal
    /// window sweep, so they never need special-casing inside the sweep.
    /// </summary>
    public bool IsTaskbar =>
        ClassName.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
        ClassName.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase);

    /// <summary>Desktop furniture that exposes no useful controls to hints.</summary>
    public bool IsDesktopShell =>
        ClassName.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
        ClassName.Equals("WorkerW", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Chooses which windows a scope should scan. Pure and deterministic: the same
/// input always yields the same roots, in top-level z-order.
/// </summary>
public static class WindowSelection
{
    /// <summary>
    /// Cap on scanned roots. A desktop full of windows must not multiply the
    /// per-window UI Automation cost without bound; the front-most windows win.
    /// </summary>
    public const int MaxWindows = 32;

    public static IReadOnlyList<ScreenWindow> Choose(
        IReadOnlyList<ScreenWindow> windows,
        HintScanScope scope,
        Rectangle region,
        IntPtr foreground,
        bool includeTaskbar,
        int maxWindows = MaxWindows)
    {
        var eligible = new List<ScreenWindow>(windows.Count);
        var byHandle = new Dictionary<IntPtr, ScreenWindow>();

        foreach (var window in windows)
        {
            if (window.Handle == IntPtr.Zero)
            {
                continue;
            }

            if (window.IsOwnOverlay)
            {
                // Never scan our own feedback windows: the hint labels and the
                // grid overlay would otherwise turn into "clickable" targets.
                continue;
            }

            if (!window.Visible || window.Cloaked || window.Minimized)
            {
                continue;
            }

            if (window.Bounds.Width < 1 || window.Bounds.Height < 1)
            {
                continue;
            }

            if (window.IsTaskbar)
            {
                if (includeTaskbar && Intersects(window.Bounds, region))
                {
                    eligible.Add(window);
                }

                continue;
            }

            if (window.IsDesktopShell)
            {
                continue;
            }

            eligible.Add(window);
            byHandle[window.Handle] = window;
        }

        if (scope == HintScanScope.ForegroundWindow)
        {
            return ChooseForeground(eligible, byHandle, foreground, maxWindows);
        }

        var chosen = new List<ScreenWindow>(eligible.Count);
        foreach (var window in eligible)
        {
            if (Intersects(window.Bounds, region))
            {
                chosen.Add(window);
            }

            if (chosen.Count >= maxWindows)
            {
                break;
            }
        }

        return chosen;
    }

    /// <summary>
    /// The foreground window and everything it owns, transitively: context
    /// menus, dropdowns, tooltips and modal dialogs are separate HWNDs owned
    /// by the app window, so scanning only the foreground handle missed them.
    /// </summary>
    private static IReadOnlyList<ScreenWindow> ChooseForeground(
        List<ScreenWindow> eligible,
        Dictionary<IntPtr, ScreenWindow> byHandle,
        IntPtr foreground,
        int maxWindows)
    {
        var keep = new HashSet<IntPtr>();

        if (foreground != IntPtr.Zero)
        {
            keep.Add(foreground);
        }

        if (keep.Count == 0)
        {
            return [];
        }

        // Owner chains are short (window -> menu/dialog); a few passes are
        // enough and avoid recursion over hostile input.
        for (int pass = 0; pass < 4; pass++)
        {
            bool added = false;

            foreach (var window in eligible)
            {
                if (keep.Contains(window.Handle) || !byHandle.ContainsKey(window.Handle))
                {
                    continue;
                }

                if (keep.Contains(window.Owner))
                {
                    keep.Add(window.Handle);
                    added = true;
                }
            }

            if (!added)
            {
                break;
            }
        }

        var result = new List<ScreenWindow>(keep.Count);
        foreach (var window in eligible)
        {
            if (keep.Contains(window.Handle))
            {
                result.Add(window);
                if (result.Count >= maxWindows)
                {
                    break;
                }
            }
        }

        return result;
    }

    private static bool Intersects(Rectangle a, Rectangle b) =>
        b.Width > 0 && b.Height > 0 && a.IntersectsWith(b);
}
