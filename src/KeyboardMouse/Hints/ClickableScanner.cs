using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using KeyboardMouse.Core;
using KeyboardMouse.Native;
using KeyboardMouse.Platform;
using KeyboardMouse.Vision;

// Both UIA and the visual pipeline have a ControlType; this file talks UIA.
using ControlType = FlaUI.Core.Definitions.ControlType;

namespace KeyboardMouse.Hints;

/// <summary>
/// Finds clickable elements for hint mode in two passes.
///
/// The first pass is UI Automation over the windows the configured scope
/// covers: the foreground window together with everything it owns (context
/// menus, dropdowns, modal dialogs are separate windows and used to be missed),
/// or every non-minimized window on a monitor, on all monitors, or on the whole
/// virtual desktop.
///
/// The second pass captures the same region as pixels and runs the visual
/// pipeline over it. It only contributes targets UI Automation did not already
/// report, which is what makes custom drawn UI (canvas toolbars, games,
/// Electron surfaces without accessibility, remote desktops, screenshots of a
/// UI) reachable at all.
///
/// The whole scan runs on its own MTA thread with a hard time budget, because a
/// single unresponsive app must never freeze the keyboard hook or the UI.
/// </summary>
public sealed class ClickableScanner : IClickableScanner
{
    /// <summary>Budget for the historical foreground-window scan.</summary>
    public const int ForegroundScanTimeoutMs = 400;

    /// <summary>Budget for monitor-wide scans: more windows, plus pixel work.</summary>
    public const int WideScanTimeoutMs = 1400;

    /// <summary>Reserve kept free so the pipeline can finish inside the budget.</summary>
    private const int VisualReserveMs = 120;

    private static readonly HashSet<ControlType> WantedTypes =
    [
        ControlType.Button,
        ControlType.Hyperlink,
        ControlType.MenuItem,
        ControlType.TabItem,
        ControlType.ListItem,
        ControlType.TreeItem,
        ControlType.CheckBox,
        ControlType.RadioButton,
        ControlType.ComboBox,
        ControlType.Edit,
        ControlType.SplitButton,
        ControlType.DataItem
    ];

    private readonly IMonitorService? _monitors;

    public ClickableScanner(IMonitorService? monitors = null)
    {
        _monitors = monitors;
    }

    /// <summary>Budget for one scan of this scope.</summary>
    public static int TimeoutFor(HintScanScope scope) =>
        scope == HintScanScope.ForegroundWindow ? ForegroundScanTimeoutMs : WideScanTimeoutMs;

    public void BeginScan(
        IntPtr window,
        HintScanOptions options,
        Action<IReadOnlyList<ClickableTarget>> completed,
        Action timedOut)
    {
        // A foreground handle is only required by the foreground scope; monitor
        // scopes still work when the desktop has no active window.
        if (window == IntPtr.Zero && options.Scope == HintScanScope.ForegroundWindow)
        {
            completed([]);
            return;
        }

        int timeout = TimeoutFor(options.Scope);
        int finished = 0;
        System.Threading.Timer? timer = null;

        timer = new System.Threading.Timer(
            _ =>
            {
                if (Interlocked.Exchange(ref finished, 1) == 0)
                {
                    timer?.Dispose();
                    Log.Info($"Hint scan timed out after {timeout} ms ({ScanScopeNames.ToId(options.Scope)}).");
                    timedOut();
                }
            },
            null,
            timeout,
            Timeout.Infinite);

        var thread = new Thread(() =>
        {
            IReadOnlyList<ClickableTarget> targets;

            try
            {
                targets = Scan(window, options, timeout);
            }
            catch (Exception ex)
            {
                Log.Error("Hint scan failed", ex);
                targets = [];
            }

            if (Interlocked.Exchange(ref finished, 1) == 0)
            {
                timer?.Dispose();
                completed(targets);
            }
        })
        {
            IsBackground = true,
            Name = "KeyboardMouse.HintScan"
        };

        // UIA3 is COM-based; keep all calls on one MTA thread.
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    private IReadOnlyList<ClickableTarget> Scan(IntPtr foreground, HintScanOptions options, int timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        var monitors = _monitors?.All ?? [];
        Rectangle? windowBounds = foreground == IntPtr.Zero ? null : BoundsOf(foreground);
        var region = ScanScopeMath.ResolveRegion(options.Scope, monitors, options.Anchor, windowBounds);

        using var automation = new UIA3Automation();
        var handles = ChooseWindows(foreground, options, region);
        var roots = new List<AutomationElement>(handles.Count);

        foreach (var handle in handles)
        {
            AddRoot(automation, handle, roots);
        }

        var limit = Math.Clamp(options.MaxElements, 1, 2000);
        var raw = new List<RawClickableTarget>(256);
        foreach (var root in roots)
        {
            Collect(root, raw);
        }

        var targets = HintTargetFilter.Apply(raw, monitors, limit);

        // Chromium and Electron apps build their accessibility tree lazily.
        // One quick retry gives the first scan a chance to become useful.
        if (targets.Count == 0 && stopwatch.ElapsedMilliseconds < 200)
        {
            Thread.Sleep(150);
            raw.Clear();

            foreach (var root in roots)
            {
                Collect(root, raw);
            }

            targets = HintTargetFilter.Apply(raw, monitors, limit);
        }

        Log.Info(
            $"Hint scan ({ScanScopeNames.ToId(options.Scope)}) read {handles.Count} window(s), " +
            $"{targets.Count} accessible element(s) in {stopwatch.ElapsedMilliseconds} ms.");

        if (options.VisualDetection)
        {
            targets = AddVisualTargets(targets, options, monitors, region, windowBounds, stopwatch, timeout);
        }

        return targets;
    }

    /// <summary>
    /// Windows this scan should read. Wide scopes sweep the desktop; the
    /// foreground scope keeps the historical "this app only" answer but now
    /// includes the owned popups and the taskbar.
    /// </summary>
    private static List<IntPtr> ChooseWindows(
        IntPtr foreground,
        HintScanOptions options,
        Rectangle region)
    {
        var handles = new List<IntPtr>(8);
        var seen = new HashSet<IntPtr>();

        IReadOnlyList<ScreenWindow> chosen = [];
        var desktop = ScreenWindowEnumerator.Enumerate();

        if (desktop.Count > 0)
        {
            try
            {
                chosen = WindowSelection.Choose(
                    desktop,
                    options.Scope,
                    region,
                    foreground,
                    options.IncludeTaskbar);
            }
            catch (Exception ex)
            {
                Log.Error("Hint scan could not choose windows", ex);
            }
        }

        foreach (var window in chosen)
        {
            if (seen.Add(window.Handle))
            {
                handles.Add(window.Handle);
            }
        }

        // The enumeration can be empty (no DWM, a locked session) and the
        // foreground scope always needs its own window even when it was
        // filtered out above.
        if (foreground != IntPtr.Zero && seen.Add(foreground))
        {
            handles.Add(foreground);
        }

        // WindowSelection keeps taskbars for the wide scopes only, so a
        // foreground scan still adds the taskbar the user asked for.
        if (options.IncludeTaskbar)
        {
            AddTaskbar(seen, handles, User32.FindWindow("Shell_TrayWnd", null), region);
            AddTaskbar(seen, handles, User32.FindWindow("Shell_SecondaryTrayWnd", null), region);
        }

        return handles;
    }

    private static void AddTaskbar(HashSet<IntPtr> seen, List<IntPtr> handles, IntPtr taskbar, Rectangle region)
    {
        if (taskbar == IntPtr.Zero || !seen.Add(taskbar))
        {
            return;
        }

        var bounds = BoundsOf(taskbar);
        if (region.Width > 0 && region.Height > 0 && !bounds.IntersectsWith(region))
        {
            return;
        }

        handles.Add(taskbar);
    }

    /// <summary>
    /// Pixel detection for the same region, one capture per monitor so a wide
    /// desktop never asks for a single oversized bitmap. UIA wins on overlap.
    /// </summary>
    private IReadOnlyList<ClickableTarget> AddVisualTargets(
        IReadOnlyList<ClickableTarget> targets,
        HintScanOptions options,
        IReadOnlyList<MonitorInfo> monitors,
        Rectangle region,
        Rectangle? windowBounds,
        Stopwatch stopwatch,
        int timeout)
    {
        var regions = new List<Rectangle>(2);

        if (options.Scope == HintScanScope.ForegroundWindow)
        {
            if (windowBounds is { Width: > 0, Height: > 0 } bounds)
            {
                regions.Add(bounds);
            }
        }
        else
        {
            foreach (var monitor in ScanScopeMath.MonitorsInScope(options.Scope, monitors, options.Anchor))
            {
                regions.Add(monitor.Bounds);
            }
        }

        if (regions.Count == 0 && region.Width > 0 && region.Height > 0)
        {
            regions.Add(region);
        }

        var result = targets;

        foreach (var capture in regions)
        {
            int remaining = timeout - (int)stopwatch.ElapsedMilliseconds - VisualReserveMs;
            if (remaining < 60 || result.Count >= options.MaxElements)
            {
                break;
            }

            int slice = Math.Max(60, remaining / regions.Count);
            result = VisualHintSupplement.Add(capture, result, options.MaxElements, slice);
        }

        Log.Info(
            $"Hint scan ({ScanScopeNames.ToId(options.Scope)}) returned {result.Count} target(s) " +
            $"in {stopwatch.ElapsedMilliseconds} ms, {result.Count - targets.Count} from pixels.");

        return result;
    }

    private static void AddRoot(
        UIA3Automation automation,
        IntPtr handle,
        List<AutomationElement> roots)
    {
        try
        {
            roots.Add(automation.FromHandle(handle));
        }
        catch (Exception ex)
        {
            Log.Error("Could not open a window for UIA", ex);
        }
    }

    private static void Collect(AutomationElement root, List<RawClickableTarget> raw)
    {
        AutomationElement[] elements;

        try
        {
            elements = root.FindAllDescendants();
        }
        catch (Exception ex)
        {
            Log.Error("Hint scan could not enumerate descendants", ex);
            return;
        }

        foreach (var element in elements)
        {
            try
            {
                if (element.IsOffscreen || !element.IsEnabled)
                {
                    continue;
                }

                bool wanted = WantedTypes.Contains(element.ControlType);
                if (!wanted)
                {
                    try
                    {
                        wanted = element.Patterns.Invoke.IsSupported;
                    }
                    catch
                    {
                        // Some providers throw only for unsupported patterns.
                    }
                }

                if (!wanted)
                {
                    continue;
                }

                var bounds = element.BoundingRectangle;
                if (bounds.Width < 1 || bounds.Height < 1)
                {
                    continue;
                }

                raw.Add(new RawClickableTarget(bounds, false, true, SafeName(element)));
            }
            catch
            {
                // A broken element must not abort the rest of the scan.
            }
        }
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Extended frame bounds, not GetWindowRect: the invisible resize border of
    /// an aero window would inflate the captured region and shift every visual
    /// box away from the control it belongs to.
    /// </summary>
    private static Rectangle BoundsOf(IntPtr handle)
    {
        RECT rect = default;
        int hr = Dwm.DwmGetWindowAttribute(
            handle,
            Dwm.DWMWA_EXTENDED_FRAME_BOUNDS,
            out rect,
            Marshal.SizeOf<RECT>());

        if (hr != 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            if (!User32.GetWindowRect(handle, out rect))
            {
                return Rectangle.Empty;
            }
        }

        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }
}
