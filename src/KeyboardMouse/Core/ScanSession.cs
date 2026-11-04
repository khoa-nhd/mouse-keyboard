using System;
using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Native;

namespace KeyboardMouse.Core;

/// <summary>
/// Captures the context of a hint scan session. Used for stale-target detection
/// and refresh decisions. Instances are lightweight (struct) and serializable so
/// they can be compared across scan boundaries.
/// </summary>
[Serializable]
public struct ScanSession
{
    /// <summary>The foreground window handle at scan start.</summary>
    public IntPtr Window { get; init; }

    /// <summary>The hint scan scope at scan start.</summary>
    public HintScanScope Scope { get; init; }

    /// <summary>The capture rectangle at scan start (union of relevant monitor bounds).</summary>
    public Rectangle Bounds { get; init; }

    /// <summary>The DPI scale at scan start.</summary>
    public double DpiScale { get; init; }

    /// <summary>The timestamp when the scan started (milliseconds since epoch).</summary>
    public long StartTimestampMs { get; init; }

    /// <summary>The user32 monitor handle at scan start.</summary>
    public IntPtr MonitorHandle { get; init; }

    /// <summary>
    /// Creates a new ScanSession from the current scan state.
    /// </summary>
    public static ScanSession FromCurrent(
        IntPtr window,
        HintScanScope scope,
        IMonitorService? monitors,
        double dpiScale)
    {
        Rectangle bounds;
        IntPtr monitorHandle = IntPtr.Zero;

        // Map HintScanScope to capture strategy
        // Note: Only valid enum values: ForegroundWindow, ActiveMonitor, VirtualDesktop, AllMonitors
        switch (scope)
        {
            case HintScanScope.ForegroundWindow:
                {
                    if (User32.GetWindowRect(window, out RECT windowRect))
                    {
                        bounds = new Rectangle(windowRect.Left, windowRect.Top,
                            windowRect.Right - windowRect.Left,
                            windowRect.Bottom - windowRect.Top);
                    }
                    else
                    {
                        bounds = Rectangle.Empty;
                    }
                    monitorHandle = window;
                    break;
                }
            case HintScanScope.ActiveMonitor:
            case HintScanScope.VirtualDesktop:
                {
                    IntPtr hMonitor = User32.MonitorFromWindow(window, 2); // MONITOR_DEFAULTTONEAREST
                    if (User32.GetMonitorRect(hMonitor, out RECT monitorRect))
                    {
                        bounds = new Rectangle(monitorRect.Left, monitorRect.Top,
                            monitorRect.Right - monitorRect.Left,
                            monitorRect.Bottom - monitorRect.Top);
                    }
                    else
                    {
                        bounds = Rectangle.Empty;
                    }
                    monitorHandle = hMonitor;
                    break;
                }
            case HintScanScope.AllMonitors:
                {
                    // Get virtual desktop bounds (union of all monitors)
                    int virtualScreenX = User32.GetSystemMetrics(76); // SM_XVIRTUALSCREEN
                    int virtualScreenY = User32.GetSystemMetrics(77); // SM_YVIRTUALSCREEN
                    int virtualScreenWidth = User32.GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
                    int virtualScreenHeight = User32.GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
                    bounds = new Rectangle(virtualScreenX, virtualScreenY,
                        virtualScreenWidth, virtualScreenHeight);
                    monitorHandle = User32.MonitorFromWindow(window, 2);
                    break;
                }
            default:
                bounds = Rectangle.Empty;
                monitorHandle = IntPtr.Zero;
                break;
        }

        return new ScanSession
        {
            Window = window,
            Scope = scope,
            Bounds = bounds,
            DpiScale = dpiScale,
            StartTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MonitorHandle = monitorHandle != IntPtr.Zero ? monitorHandle : IntPtr.Zero
        };
    }

    /// <summary>True when the window and scope are still valid for comparison.</summary>
    public bool IsContextValid(IntPtr currentWindow, HintScanScope currentScope)
    {
        if (Window != currentWindow) return false;
        if (Scope != currentScope) return false;
        return true;
    }

    /// <summary>Compares this session to a new one. Returns true if the session
    /// is considered stale (window/scope changed or too much time elapsed).</summary>
    /// <param name="newSession">The new session to compare against.</param>
    /// <param name="staleTimeoutMs">Maximum age in ms before a session is considered stale.</param>
    /// <returns>True when the session should be considered stale.</returns>
    public bool IsStale(ScanSession newSession, int staleTimeoutMs = 5000)
    {
        // Window or scope changed = definitely stale
        if (Window != newSession.Window || Scope != newSession.Scope)
            return true;

        // Same window/scope but too much time elapsed
        long elapsed = Math.Abs(StartTimestampMs - newSession.StartTimestampMs);
        if (elapsed > staleTimeoutMs)
            return true;

        // Bounds changed materially (window moved/resized)
        if (Bounds != newSession.Bounds)
            return true;

        return false;
    }
}

/// <summary>
/// Result of stale target detection. Indicates whether targets should be
/// accepted, re-scanned, or rejected entirely.
/// </summary>
public enum StaleTargetResult
{
    /// <summary>Targets are valid; proceed with selection/execution.</summary>
    Accept,

    /// <summary>Targets may be stale; re-scan before proceeding.</summary>
    ReScan,

    /// <summary>Targets are definitely stale; reject and close hint mode.</summary>
    Reject
}

/// <summary>
/// Provides stale target detection for hint targets. Checks whether the
/// foreground window is still active, whether bounds are still valid, and
/// whether the screen content has changed materially.
/// </summary>
public static class StaleTargetDetector
{
    /// <summary>
    /// Determines if the given targets are still valid for the current UI state.
    /// </summary>
    /// <param name="previousSession">The scan session when targets were generated.</param>
    /// <param name="targets">The targets to validate.</param>
    /// <param name="currentWindow">The current foreground window handle.</param>
    /// <param name="currentScope">The current hint scan scope.</param>
    /// <param name="monitors">Available monitors for bounds validation.</param>
    /// <param name="dpiScale">Current DPI scale.</param>
    /// <param name="staleTimeoutMs">Maximum age in ms before a session is considered stale.</param>
    /// <returns>A <see cref="StaleTargetResult"/> indicating whether to accept,
    /// re-scan, or reject the targets.</returns>
    public static StaleTargetResult Check(
        ScanSession previousSession,
        IReadOnlyList<VisualTarget> targets,
        IntPtr currentWindow,
        HintScanScope currentScope,
        IMonitorService? monitors,
        double dpiScale,
        int staleTimeoutMs = 5000)
    {
        // 1. Check if the window/scope changed
        if (!previousSession.IsContextValid(currentWindow, currentScope))
        {
            return StaleTargetResult.ReScan;
        }

        // 2. Check if the session is too old
        ScanSession current = ScanSession.FromCurrent(
            currentWindow, currentScope, monitors, dpiScale);
        if (previousSession.IsStale(current, staleTimeoutMs))
        {
            return StaleTargetResult.ReScan;
        }

        // 3. Check if targets are still within valid bounds
        if (targets.Count > 0)
        {
            foreach (var target in targets)
            {
                // Target bounds must not be zero-sized
                if (target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
                {
                    return StaleTargetResult.Reject;
                }

                // Target bounds must intersect at least one monitor (if monitors available)
                if (monitors != null)
                {
                    bool inAnyMonitor = false;
                    foreach (var mon in monitors.All)
                    {
                        var monitorBounds = TargetBounds.FromRectangle(mon.Bounds);
                        if (target.Bounds.IntersectsWith(monitorBounds))
                        {
                            inAnyMonitor = true;
                            break;
                        }
                    }
                    if (!inAnyMonitor)
                    {
                        return StaleTargetResult.Reject;
                    }
                }

                // Target click point must be within bounds
                // ClickPoint is Point (not nullable), always has a value
                if (!target.Bounds.Contains(target.ClickPoint))
                {
                    // Click point outside bounds - flag for re-scan
                    return StaleTargetResult.ReScan;
                }
            }
        }

        // 4. All checks passed
        return StaleTargetResult.Accept;
    }
}