namespace KeyboardMouse.Core;

/// <summary>
/// Screen area covered by a hint scan. The default stays the foreground window
/// so existing behaviour does not change unless the user opts in.
/// </summary>
public enum HintScanScope
{
    /// <summary>The window that was foreground when hint mode opened.</summary>
    ForegroundWindow,

    /// <summary>The monitor that contains the foreground window.</summary>
    ActiveMonitor,

    /// <summary>Every monitor, captured separately so gaps and mixed DPI are safe.</summary>
    VirtualDesktop,

    /// <summary>Every connected monitor. Same capture strategy as the virtual desktop.</summary>
    AllMonitors
}

/// <summary>
/// Parsing, defaults and budgets for <see cref="HintScanScope"/>. Settings store
/// a short lower-case id; display names are used by the tray menu.
/// </summary>
public static class HintScanScopes
{
    public const string WindowId = "window";
    public const string MonitorId = "monitor";
    public const string DesktopId = "desktop";
    public const string AllMonitorsId = "all-monitors";

    /// <summary>Window scans stay close to the legacy 400 ms cap.</summary>
    public static readonly TimeSpan WindowSourceBudget = TimeSpan.FromMilliseconds(500);

    /// <summary>A full monitor is several times more pixels than a window.</summary>
    public static readonly TimeSpan MonitorSourceBudget = TimeSpan.FromMilliseconds(900);

    /// <summary>Tiled virtual-desktop scans need room for several monitor captures.</summary>
    public static readonly TimeSpan DesktopSourceBudget = TimeSpan.FromMilliseconds(1800);

    public static string Normalize(string? value) => Parse(value) switch
    {
        HintScanScope.ActiveMonitor => MonitorId,
        HintScanScope.VirtualDesktop => DesktopId,
        HintScanScope.AllMonitors => AllMonitorsId,
        _ => WindowId
    };

    public static HintScanScope Parse(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            MonitorId or "active-monitor" or "active monitor" => HintScanScope.ActiveMonitor,
            DesktopId or "virtual-desktop" or "virtual desktop" => HintScanScope.VirtualDesktop,
            AllMonitorsId or "all monitors" or "allmonitors" => HintScanScope.AllMonitors,
            _ => HintScanScope.ForegroundWindow
        };

    public static string ToId(HintScanScope scope) => scope switch
    {
        HintScanScope.ActiveMonitor => MonitorId,
        HintScanScope.VirtualDesktop => DesktopId,
        HintScanScope.AllMonitors => AllMonitorsId,
        _ => WindowId
    };

    public static string DisplayName(HintScanScope scope) => scope switch
    {
        HintScanScope.ActiveMonitor => "Active monitor",
        HintScanScope.VirtualDesktop => "Entire virtual desktop",
        HintScanScope.AllMonitors => "All monitors",
        _ => "Foreground window"
    };

    /// <summary>True when the scope can include content outside the window.</summary>
    public static bool CoversDesktop(HintScanScope scope) =>
        scope is HintScanScope.ActiveMonitor or
                 HintScanScope.VirtualDesktop or
                 HintScanScope.AllMonitors;

    /// <summary>Per-source budget for a scope.</summary>
    public static TimeSpan SourceBudgetFor(HintScanScope scope) => scope switch
    {
        HintScanScope.ActiveMonitor => MonitorSourceBudget,
        HintScanScope.VirtualDesktop or HintScanScope.AllMonitors => DesktopSourceBudget,
        _ => WindowSourceBudget
    };

    /// <summary>Overall budget including a UIA + CV hybrid round trip.</summary>
    public static TimeSpan BudgetFor(HintScanMode mode, HintScanScope scope)
    {
        TimeSpan source = SourceBudgetFor(scope);

        return mode switch
        {
            HintScanMode.Uia => TimeSpan.FromMilliseconds(Math.Max(400, source.TotalMilliseconds / 2)),
            HintScanMode.Cv => source,
            _ => source + TimeSpan.FromMilliseconds(300)
        };
    }
}
