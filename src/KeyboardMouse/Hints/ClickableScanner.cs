using System.Diagnostics;
using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using KeyboardMouse.Core;
using KeyboardMouse.Native;
using KeyboardMouse.Platform;

namespace KeyboardMouse.Hints;

/// <summary>
/// Scans the foreground window (and optionally the taskbar) with UI Automation.
/// The scan starts on its own MTA thread and has a hard 400 ms budget, because
/// a single unresponsive app must never freeze the keyboard hook or the UI.
/// </summary>
public sealed class ClickableScanner : IClickableScanner
{
    public const int ScanTimeoutMs = 400;

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

    public void BeginScan(
        IntPtr window,
        HintScanOptions options,
        Action<IReadOnlyList<ClickableTarget>> completed,
        Action timedOut)
    {
        if (window == IntPtr.Zero)
        {
            completed([]);
            return;
        }

        int finished = 0;
        System.Threading.Timer? timer = null;

        timer = new System.Threading.Timer(
            _ =>
            {
                if (Interlocked.Exchange(ref finished, 1) == 0)
                {
                    timer?.Dispose();
                    Log.Info($"Hint scan timed out after {ScanTimeoutMs} ms.");
                    timedOut();
                }
            },
            null,
            ScanTimeoutMs,
            Timeout.Infinite);

        var thread = new Thread(() =>
        {
            IReadOnlyList<ClickableTarget> targets;

            try
            {
                targets = ScanWindow(window, options);
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

    private IReadOnlyList<ClickableTarget> ScanWindow(IntPtr window, HintScanOptions options)
    {
        var stopwatch = Stopwatch.StartNew();

        using var automation = new UIA3Automation();
        var roots = new List<AutomationElement>();

        AddRoot(automation, window, roots);

        if (options.IncludeTaskbar)
        {
            IntPtr taskbar = User32.FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero && taskbar != window)
            {
                AddRoot(automation, taskbar, roots);
            }
        }

        var raw = new List<RawClickableTarget>(256);
        foreach (var root in roots)
        {
            Collect(root, raw);
        }

        var monitors = _monitors?.All ?? [];
        var targets = HintTargetFilter.Apply(raw, monitors, options.MaxElements);

        // Chromium and Electron apps build their accessibility tree lazily.
        // One quick retry gives the first scan a chance to become useful.
        if (targets.Count == 0 &&
            stopwatch.ElapsedMilliseconds < 200)
        {
            Thread.Sleep(150);
            raw.Clear();

            foreach (var root in roots)
            {
                Collect(root, raw);
            }

            targets = HintTargetFilter.Apply(raw, monitors, options.MaxElements);
        }

        Log.Info($"Hint scan found {targets.Count} element(s) in {stopwatch.ElapsedMilliseconds} ms.");
        return targets;
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
            Log.Error("Could not open the foreground window for UIA", ex);
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
}
