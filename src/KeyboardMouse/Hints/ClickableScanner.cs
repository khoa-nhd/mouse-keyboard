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
/// Finds clickable elements for hint mode in two passes that run at the same
/// time.
///
/// UI Automation walks the windows the configured scope covers: the foreground
/// window together with everything it owns (context menus, dropdowns, modal
/// dialogs are separate windows), every window on a monitor, or the whole
/// desktop.
///
/// The pixel pass captures the same region first — before any window can move —
/// and analyses it on a worker thread while automation runs. It contributes the
/// targets UI Automation cannot see: custom drawn UI, canvas toolbars, games,
/// remote desktops, icon-only chrome. Running it in parallel is what makes it
/// useful: previously it shared one budget with automation, so whenever
/// automation was slow the pixel pass got no time at all and added nothing.
///
/// The whole scan runs on its own MTA thread. Its budget is a soft deadline: a
/// scan that is a little late is still shown instead of being thrown away
/// (which used to turn a 500 ms scan into no hints at all).
/// </summary>
public sealed class ClickableScanner : IClickableScanner
{
    /// <summary>UI Automation budget for the foreground-window scan.</summary>
    public const int ForegroundScanTimeoutMs = 700;

    /// <summary>UI Automation budget for monitor-wide scans: more windows.</summary>
    public const int WideScanTimeoutMs = 1200;

    /// <summary>Time the pixel pass is allowed, running beside automation.</summary>
    public const int VisualBudgetMs = 1000;

    /// <summary>
    /// Extra time a scan may still take before it is abandoned. A scan that
    /// finishes inside this window is delivered, not discarded: dropping a
    /// result that was 100 ms late left the screen with no hints at all.
    /// </summary>
    public const int TimeoutGraceMs = 1500;

    /// <summary>Most monitors one scan captures pixels from.</summary>
    public const int MaxVisualCaptures = 4;

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

    /// <summary>UI Automation budget for one scan of this scope.</summary>
    public static int TimeoutFor(HintScanScope scope) =>
        scope == HintScanScope.ForegroundWindow ? ForegroundScanTimeoutMs : WideScanTimeoutMs;

    /// <summary>Soft budget: automation plus the pixel pass running beside it.</summary>
    public static int TotalTimeoutFor(HintScanScope scope, bool visualDetection) =>
        TimeoutFor(scope) + (visualDetection ? VisualBudgetMs : 0);

    /// <summary>Hard deadline, after the grace period.</summary>
    public static int HardTimeoutFor(HintScanScope scope, bool visualDetection) =>
        TotalTimeoutFor(scope, visualDetection) + TimeoutGraceMs;

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

        int soft = TotalTimeoutFor(options.Scope, options.VisualDetection);
        int hard = HardTimeoutFor(options.Scope, options.VisualDetection);
        int finished = 0;
        int ticks = 0;
        System.Threading.Timer? timer = null;

        // Two-stage deadline. The first tick only says the scan is late; the
        // second one, after the grace period, gives up. A late answer is still
        // an answer, and showing nothing is worse than showing it 300 ms later.
        timer = new System.Threading.Timer(
            _ =>
            {
                if (Interlocked.Increment(ref ticks) == 1)
                {
                    Log.Info(
                        $"Hint scan is past its {soft} ms budget " +
                        $"({ScanScopeNames.ToId(options.Scope)}), waiting up to {hard} ms.");
                    return;
                }

                if (Interlocked.Exchange(ref finished, 1) == 0)
                {
                    timer?.Dispose();
                    Log.Info(
                        $"Hint scan gave up after {hard} ms ({ScanScopeNames.ToId(options.Scope)}).");
                    timedOut();
                }
            },
            null,
            soft,
            TimeoutGraceMs);

        var thread = new Thread(() =>
        {
            IReadOnlyList<ClickableTarget> targets;

            try
            {
                targets = Scan(window, options, soft);
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

    private IReadOnlyList<ClickableTarget> Scan(IntPtr foreground, HintScanOptions options, int budgetMs)
    {
        var stopwatch = Stopwatch.StartNew();
        var monitors = _monitors?.All ?? [];
        Rectangle? windowBounds = foreground == IntPtr.Zero ? null : BoundsOf(foreground);
        var region = ScanScopeMath.ResolveRegion(options.Scope, monitors, options.Anchor, windowBounds);
        int limit = Math.Clamp(options.MaxElements, 1, 2000);

        // Capture first, analyse in parallel: the screen still looks like it did
        // when the user pressed the key, and the pixel pass gets its own time.
        var captures = options.VisualDetection
            ? CaptureVisual(options, monitors, region, windowBounds)
            : [];

        Task<VisualDetectionResult[]>? visual = captures.Count == 0
            ? null
            : Task.Run(() => Detect(captures));

        var handles = ChooseWindows(foreground, options, region);

        // Reading a property of an accessibility element is a cross-process
        // call, so a huge tree (a browser, an IDE) can spend seconds on elements
        // the cap would discard anyway. Stop reading once the list is clearly
        // larger than the number of hints that can be shown.
        int rawCap = Math.Max(600, limit * 3);

        // One worker per window: a single Electron window can block its own
        // accessibility call for 1.6 s, and reading windows one after another
        // made that the whole scan. In parallel the slowest window sets the
        // cost, and a window that never answers is simply left out.
        var collected = CollectWindows(handles, rawCap, stopwatch, budgetMs);
        var raw = collected.Raw;

        // No region clip here: the foreground scope owns its popups, and a
        // context menu can be drawn outside the window's own bounds.
        var targets = HintTargetFilter.Apply(raw, monitors, limit);

        // Chromium and Electron apps build their accessibility tree lazily.
        // One quick retry gives the first scan a chance to become useful.
        if (targets.Count == 0 && stopwatch.ElapsedMilliseconds < 250)
        {
            Thread.Sleep(150);
            var retry = CollectWindows(handles, rawCap, stopwatch, budgetMs);
            targets = HintTargetFilter.Apply(retry.Raw, monitors, limit);
        }

        Log.Info(
            $"Hint scan ({ScanScopeNames.ToId(options.Scope)}) read {collected.Read} of " +
            $"{handles.Count} window(s), {targets.Count} accessible element(s) in " +
            $"{stopwatch.ElapsedMilliseconds} ms ({collected.SlowestMs} ms was the slowest window).");

        if (visual is null)
        {
            return targets;
        }

        int wait = Math.Max(
            100,
            HardTimeoutFor(options.Scope, true) - (int)stopwatch.ElapsedMilliseconds - 50);

        if (!visual.Wait(wait))
        {
            Log.Info(
                $"Visual pass did not finish within {wait} ms and was left out " +
                $"({ScanScopeNames.ToId(options.Scope)}).");
            return targets;
        }

        var results = visual.Result;

        for (int i = 0; i < captures.Count && i < results.Length; i++)
        {
            targets = VisualHintSupplement.Add(results[i], captures[i].Region, targets, limit);
        }

        Log.Info(
            $"Hint scan ({ScanScopeNames.ToId(options.Scope)}) returned {targets.Count} target(s) " +
            $"in {stopwatch.ElapsedMilliseconds} ms from {captures.Count} capture(s).");

        return targets;
    }

    private static VisualDetectionResult[] Detect(IReadOnlyList<VisualCapture> captures)
    {
        // Native resolution for a single screen: downscaling loses the 1-2 px
        // strokes an icon is made of, and MultiScale doubled the cost for a
        // second look at the same boxes. Large desktops are scaled to fit the
        // pixel ceiling instead.
        var options = new VisualDetectionOptions
        {
            MaxCandidates = 120,
            MaxProcessingPixels = 2_200_000,
            MultiScale = false,
            MinScore = 0.45
        };

        var results = new VisualDetectionResult[captures.Count];

        for (int i = 0; i < captures.Count; i++)
        {
            results[i] = VisualPipeline.Detect(captures[i].Image, options);
        }

        return results;
    }

    private static List<VisualCapture> CaptureVisual(
        HintScanOptions options,
        IReadOnlyList<MonitorInfo> monitors,
        Rectangle region,
        Rectangle? windowBounds)
    {
        var regions = new List<Rectangle>(4);

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

        var captures = new List<VisualCapture>(Math.Min(regions.Count, MaxVisualCaptures));

        foreach (var capture in regions)
        {
            if (captures.Count >= MaxVisualCaptures)
            {
                break;
            }

            if (capture.Width < 8 || capture.Height < 8)
            {
                continue;
            }

            var image = ScreenCapture.Capture(capture);

            if (image is null)
            {
                Log.Info($"Visual pass could not capture {capture.Width}x{capture.Height} at {capture.X},{capture.Y}.");
                continue;
            }

            captures.Add(new VisualCapture(capture, image));
        }

        return captures;
    }

    private sealed record VisualCapture(Rectangle Region, Image32 Image);

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

    private sealed record CollectedWindows(List<RawClickableTarget> Raw, int Read, int SlowestMs);

    /// <summary>
    /// Reads the chosen windows at the same time, each on its own thread with its
    /// own automation object, and waits only until the budget runs out. A window
    /// that is still busy after that is dropped: its hints are missing, but the
    /// rest of the screen is not held hostage by one unresponsive app.
    /// </summary>
    private static CollectedWindows CollectWindows(
        IReadOnlyList<IntPtr> handles,
        int rawCap,
        Stopwatch stopwatch,
        int budgetMs)
    {
        var raw = new List<RawClickableTarget>(256);

        if (handles.Count == 0)
        {
            return new CollectedWindows(raw, 0, 0);
        }

        var tasks = new Task<(List<RawClickableTarget> Items, int Elapsed)>[handles.Count];

        for (int i = 0; i < handles.Count; i++)
        {
            var handle = handles[i];
            tasks[i] = Task.Run(() => ReadWindow(handle, rawCap));
        }

        int wait = Math.Max(150, budgetMs - (int)stopwatch.ElapsedMilliseconds);

        try
        {
            Task.WaitAll(tasks, wait);
        }
        catch (Exception ex)
        {
            Log.Error("Hint scan could not wait for window scans", ex);
        }

        int read = 0;
        int slowest = 0;

        foreach (var task in tasks)
        {
            if (!task.IsCompletedSuccessfully)
            {
                continue;
            }

            read++;
            slowest = Math.Max(slowest, task.Result.Elapsed);
            raw.AddRange(task.Result.Items);
        }

        return new CollectedWindows(raw, read, slowest);
    }

    private static (List<RawClickableTarget> Items, int Elapsed) ReadWindow(IntPtr handle, int cap)
    {
        var items = new List<RawClickableTarget>(64);
        var stopwatch = Stopwatch.StartNew();
        UIA3Automation? automation = null;

        try
        {
            // UIA3 is COM-based; this runs on a thread-pool (MTA) thread.
            automation = new UIA3Automation();
            Collect(automation.FromHandle(handle), items, cap);
        }
        catch (Exception ex)
        {
            Log.Error("Could not read a window for UIA", ex);
        }
        finally
        {
            automation?.Dispose();
        }

        stopwatch.Stop();
        return (items, (int)stopwatch.ElapsedMilliseconds);
    }

    private static void Collect(AutomationElement root, List<RawClickableTarget> raw, int cap)
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
            if (raw.Count >= cap)
            {
                Log.Info($"Hint scan stopped reading a window after {raw.Count} element(s).");
                return;
            }

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
