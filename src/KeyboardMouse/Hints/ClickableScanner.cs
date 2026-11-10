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
        Action<HintScanResult> reported,
        Action timedOut)
    {
        // A foreground handle is only required by the foreground scope; monitor
        // scopes still work when the desktop has no active window.
        if (window == IntPtr.Zero && options.Scope == HintScanScope.ForegroundWindow)
        {
            reported(new HintScanResult([], true));
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
                // The full budget stays with the scan: the user already has the
                // targets from the fast windows, so a late window costs nothing
                // but its own time and is still included when it answers.
                targets = Scan(
                    window,
                    options,
                    soft,
                    result =>
                    {
                        // An early batch that arrives after the scan already
                        // gave up must not reopen hints.
                        if (Volatile.Read(ref finished) == 0)
                        {
                            reported(result);
                        }
                    });
            }
            catch (Exception ex)
            {
                Log.Error("Hint scan failed", ex);
                targets = [];
            }

            if (Interlocked.Exchange(ref finished, 1) == 0)
            {
                timer?.Dispose();
                reported(new HintScanResult(targets, true));
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

    private IReadOnlyList<ClickableTarget> Scan(
        IntPtr foreground,
        HintScanOptions options,
        int budgetMs,
        Action<HintScanResult> reported)
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
        // cost, so the windows that answered early are handed over first and
        // the slow ones fill in when they are done.
        int shown = 0;

        var collected = CollectWindows(
            handles,
            rawCap,
            stopwatch,
            budgetMs,
            early =>
            {
                var partial = HintTargetFilter.Apply(early, monitors, limit);

                if (visual is { IsCompleted: true })
                {
                    partial = AddVisual(partial, visual.Result, captures, limit);
                }

                if (partial.Count <= shown)
                {
                    return;
                }

                Log.Info(
                    $"Hint scan showed {partial.Count} target(s) after " +
                    $"{stopwatch.ElapsedMilliseconds} ms while the slow windows finish.");

                shown = partial.Count;
                reported(new HintScanResult(partial, false));
            });
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

        // The accessibility answer is complete but the pixel pass is still
        // looking: show what the elements give now instead of making the user
        // wait for the extras.
        if (visual is { IsCompleted: false } && targets.Count > shown)
        {
            Log.Info(
                $"Hint scan showed {targets.Count} target(s) after " +
                $"{stopwatch.ElapsedMilliseconds} ms while the pixel pass finishes.");

            shown = targets.Count;
            reported(new HintScanResult(targets, false));
        }

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

        targets = AddVisual(targets, visual.Result, captures, limit);

        Log.Info(
            $"Hint scan ({ScanScopeNames.ToId(options.Scope)}) returned {targets.Count} target(s) " +
            $"in {stopwatch.ElapsedMilliseconds} ms from {captures.Count} capture(s).");

        return targets;
    }

    private static IReadOnlyList<ClickableTarget> AddVisual(
        IReadOnlyList<ClickableTarget> targets,
        VisualDetectionResult[] results,
        IReadOnlyList<VisualCapture> captures,
        int limit)
    {
        for (int i = 0; i < captures.Count && i < results.Length; i++)
        {
            targets = VisualHintSupplement.Add(results[i], captures[i].Region, targets, limit);
        }

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
        int budgetMs,
        Action<List<RawClickableTarget>>? onEarly = null)
    {
        var raw = new List<RawClickableTarget>(256);

        if (handles.Count == 0)
        {
            return new CollectedWindows(raw, 0, 0);
        }

        var items = new List<RawClickableTarget>?[handles.Count];
        var elapsed = new int[handles.Count];

        // One reader per window: reading them one after another made the
        // slowest window the whole scan, and splitting a window over several
        // threads only queued them all at the same automation broker.
        var tasks = new Task[handles.Count];

        for (int i = 0; i < handles.Count; i++)
        {
            int index = i;

            tasks[index] = Task.Run(() =>
            {
                using var automation = new UIA3Automation();
                var read = ReadWindow(automation, handles[index], rawCap);
                items[index] = read.Items;
                elapsed[index] = read.Elapsed;
            });
        }

        int wait = Math.Max(150, budgetMs - (int)stopwatch.ElapsedMilliseconds);

        try
        {
            if (onEarly is not null)
            {
                // Hand each window's targets over as soon as it answers: the
                // hints grow while the slower windows are still reading, instead
                // of every hint waiting for the slowest one. The reports are
                // cumulative, and kept a little apart so the overlay is not
                // redrawn for every single window.
                var waiting = Stopwatch.StartNew();
                var sinceReport = Stopwatch.StartNew();
                int reported = 0;

                while (items.Any(item => item is null) && waiting.ElapsedMilliseconds < wait)
                {
                    if (sinceReport.ElapsedMilliseconds >= ReportGapMs)
                    {
                        if (ReportReady(items, ref reported, onEarly))
                        {
                            sinceReport.Restart();
                        }
                    }

                    Thread.Sleep(ReadPollMs);
                }
            }

            Task.WaitAll(tasks, Math.Max(0, budgetMs - (int)stopwatch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            Log.Error("Hint scan could not wait for window scans", ex);
        }

        return Snapshot(items, elapsed);
    }

    /// <summary>How often the reader looks for windows that have just answered.</summary>
    private const int ReadPollMs = 40;

    /// <summary>Smallest gap between two reports to the overlay while reading.</summary>
    private const int ReportGapMs = 150;

    /// <summary>
    /// Hands over the targets of every window that has answered so far, unless
    /// nothing new has arrived since the last report or nothing has been found
    /// yet. The list is cumulative, so the caller can merge it as it grows.
    /// </summary>
    internal static bool ReportReady(
        List<RawClickableTarget>?[] items,
        ref int reported,
        Action<List<RawClickableTarget>> report)
    {
        int ready = 0;
        var raw = new List<RawClickableTarget>(256);

        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            ready++;
            raw.AddRange(item);
        }

        if (ready <= reported || raw.Count == 0)
        {
            return false;
        }

        reported = ready;
        report(raw);
        return true;
    }

    /// <summary>
    /// Targets from the windows that have answered, in window order, so a list
    /// built from a snapshot is ordered like the final one and the early batch
    /// stays a prefix of it.
    /// </summary>
    private static CollectedWindows Snapshot(List<RawClickableTarget>?[] items, int[] elapsed)
    {
        var raw = CollectReady(items);
        int read = 0;
        int slowest = 0;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] is null)
            {
                continue;
            }

            read++;
            slowest = Math.Max(slowest, elapsed[i]);
        }

        return new CollectedWindows(raw, read, slowest);
    }

    /// <summary>Targets of the windows that have answered, in window order.</summary>
    private static List<RawClickableTarget> CollectReady(List<RawClickableTarget>?[] items)
    {
        var raw = new List<RawClickableTarget>(256);

        foreach (var item in items)
        {
            if (item is not null)
            {
                raw.AddRange(item);
            }
        }

        return raw;
    }

    private static (List<RawClickableTarget> Items, int Elapsed) ReadWindow(
        UIA3Automation automation,
        IntPtr handle,
        int cap)
    {
        var items = new List<RawClickableTarget>(64);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // UIA3 is COM-based; the automation object and every call it makes
            // stay on this reader's thread.
            Collect(automation.FromHandle(handle), items, cap);
        }
        catch (Exception ex)
        {
            Log.Error("Could not read a window for UIA", ex);
        }

        stopwatch.Stop();
        int elapsed = (int)stopwatch.ElapsedMilliseconds;

        if (elapsed >= SlowWindowMs)
        {
            Log.Info(
                $"Hint scan read {items.Count} element(s) from {ProcessNameOf(handle)} " +
                $"in {elapsed} ms.");
        }

        return (items, elapsed);
    }

    /// <summary>A window this slow is worth naming in the log.</summary>
    private const int SlowWindowMs = 400;

    /// <summary>
    /// Reads one window: walk its descendants, then ask each element for the
    /// properties the filter needs. This is the plain UI Automation path, and
    /// the walk stays on one thread because a shared automation object is not
    /// safe across threads.
    /// </summary>
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
                // The control type rules out most of a big tree before anything
                // else is asked for, and an element with no box cannot be
                // clicked.
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

                if (element.IsOffscreen || !element.IsEnabled)
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

    private static string ProcessNameOf(IntPtr handle)
    {
        try
        {
            User32.GetWindowThreadProcessId(handle, out uint processId);
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return "a window";
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
