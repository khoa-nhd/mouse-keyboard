using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;

namespace KeyboardMouse.Tests;

public class HintScanScopeTests
{
    // ---------- scope ids / settings ----------

    [Theory]
    [InlineData("foreground", HintScanScope.ForegroundWindow)]
    [InlineData("activeMonitor", HintScanScope.ActiveMonitor)]
    [InlineData("allMonitors", HintScanScope.AllMonitors)]
    [InlineData("virtualDesktop", HintScanScope.VirtualDesktop)]
    [InlineData("  allMonitors  ", HintScanScope.AllMonitors)]
    public void Parse_ReadsEverySupportedId(string id, HintScanScope expected)
    {
        Assert.Equal(expected, ScanScopeNames.Parse(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("everything")]
    public void Parse_FallsBackToForeground(string? id)
    {
        Assert.Equal(HintScanScope.ForegroundWindow, ScanScopeNames.Parse(id));
    }

    [Theory]
    [InlineData(HintScanScope.ForegroundWindow, "foreground")]
    [InlineData(HintScanScope.ActiveMonitor, "activeMonitor")]
    [InlineData(HintScanScope.AllMonitors, "allMonitors")]
    [InlineData(HintScanScope.VirtualDesktop, "virtualDesktop")]
    public void ToId_RoundTrips(HintScanScope scope, string id)
    {
        Assert.Equal(id, ScanScopeNames.ToId(scope));
        Assert.Equal(scope, ScanScopeNames.Parse(id));
    }

    [Fact]
    public void Settings_NormalizeRepairsTheScopeId()
    {
        var settings = new Settings();
        settings.Hint.ScanScope = "bogus";
        settings.Normalize();
        Assert.Equal(ScanScopeNames.Foreground, settings.Hint.ScanScope);

        settings.Hint.ScanScope = "  allMonitors ";
        settings.Normalize();
        Assert.Equal(ScanScopeNames.AllMonitors, settings.Hint.ScanScope);
    }

    // ---------- region geometry ----------

    [Fact]
    public void ActiveMonitor_UsesTheMonitorUnderThePointer_NegativeOrigin()
    {
        var left = Monitor(-1920, 0, 1920, 1080);
        var right = Monitor(0, 0, 2560, 1440);
        var monitors = new[] { left, right };

        var region = ScanScopeMath.ResolveRegion(
            HintScanScope.ActiveMonitor,
            monitors,
            new Point(-900, 400),
            foregroundBounds: null);

        Assert.Equal(new Rectangle(-1920, 0, 1920, 1080), region);

        var inScope = ScanScopeMath.MonitorsInScope(
            HintScanScope.ActiveMonitor,
            monitors,
            new Point(-900, 400));

        Assert.Single(inScope);
        Assert.Equal(new IntPtr(-1920 * 100 + 1), inScope[0].Handle);
    }

    [Fact]
    public void VirtualDesktop_CoversNegativeAndDifferentlySizedMonitors()
    {
        var monitors = new[]
        {
            Monitor(-1280, -200, 1280, 1024, 1.25),
            Monitor(0, 0, 3840, 2160, 1.5)
        };

        var region = ScanScopeMath.ResolveRegion(
            HintScanScope.VirtualDesktop,
            monitors,
            new Point(10, 10),
            foregroundBounds: null);

        Assert.Equal(new Rectangle(-1280, -200, 5120, 2360), region);
    }

    [Fact]
    public void AllMonitors_UsesEveryMonitor()
    {
        var monitors = new[] { Monitor(0, 0, 1920, 1080), Monitor(1920, 0, 1280, 1024) };

        var inScope = ScanScopeMath.MonitorsInScope(
            HintScanScope.AllMonitors,
            monitors,
            new Point(10, 10));

        Assert.Equal(2, inScope.Count);
    }

    [Fact]
    public void ForegroundScope_PrefersTheWindowBounds()
    {
        var monitors = new[] { Monitor(0, 0, 1920, 1080) };
        var bounds = new Rectangle(300, 200, 800, 600);

        var region = ScanScopeMath.ResolveRegion(
            HintScanScope.ForegroundWindow,
            monitors,
            new Point(400, 300),
            bounds);

        Assert.Equal(bounds, region);
    }

    [Fact]
    public void PointerInGap_UsesTheNearestMonitor()
    {
        var left = Monitor(0, 0, 1000, 1000);
        var right = Monitor(1200, 0, 800, 600);
        var monitors = new[] { left, right };

        // 1050 is 51 px from the left monitor and 150 px from the right one.
        var nearLeft = ScanScopeMath.ResolveRegion(
            HintScanScope.ActiveMonitor,
            monitors,
            new Point(1050, 100),
            foregroundBounds: null);

        // 1100 is 101 px from the left monitor and 100 px from the right one.
        var nearRight = ScanScopeMath.ResolveRegion(
            HintScanScope.ActiveMonitor,
            monitors,
            new Point(1100, 100),
            foregroundBounds: null);

        Assert.Equal(left.Bounds, nearLeft);
        Assert.Equal(right.Bounds, nearRight);
    }

    [Fact]
    public void Union_GrowsToIncludeBothRectangles()
    {
        var union = ScanScopeMath.Union(
            new Rectangle(-500, -300, 500, 300),
            new Rectangle(100, 200, 50, 50));

        Assert.Equal(new Rectangle(-500, -300, 650, 550), union);
    }

    // ---------- window selection ----------

    [Fact]
    public void ForegroundScope_KeepsTheWindowAndItsOwnedPopups()
    {
        var foreground = Window(10, new Rectangle(100, 100, 800, 600));
        var menu = Window(11, new Rectangle(300, 300, 200, 150), owner: 10);
        var nested = Window(12, new Rectangle(320, 320, 150, 100), owner: 11);
        var unrelated = Window(13, new Rectangle(50, 50, 400, 300));

        var chosen = WindowSelection.Choose(
            new[] { foreground, menu, nested, unrelated },
            HintScanScope.ForegroundWindow,
            region: new Rectangle(100, 100, 800, 600),
            foreground: new IntPtr(10),
            includeTaskbar: false);

        Assert.Equal(new[] { new IntPtr(10), new IntPtr(11), new IntPtr(12) },
            chosen.Select(w => w.Handle));
    }

    [Fact]
    public void OverlayWindows_AreNeverScanned_ButOurOwnNormalWindowsAre()
    {
        // Hint/grid/cursor overlays carry a dedicated class name, so they can
        // never become hint targets. Ordinary windows of the same process (the
        // guide) stay scannable.
        var app = Window(10, new Rectangle(0, 0, 500, 500), processId: 42);
        var hintLabels = Window(11, new Rectangle(0, 0, 500, 500), processId: 1234, owner: 10,
            className: ScreenWindow.OverlayClassName);
        var guide = Window(12, new Rectangle(520, 0, 400, 400), processId: 1234);

        var chosen = WindowSelection.Choose(
            new[] { app, hintLabels, guide },
            HintScanScope.AllMonitors,
            region: new Rectangle(0, 0, 1200, 900),
            foreground: new IntPtr(10),
            includeTaskbar: false);

        Assert.Equal(new[] { new IntPtr(10), new IntPtr(12) }, chosen.Select(w => w.Handle));
    }

    [Fact]
    public void HiddenCloakedMinimizedAndEmptyWindows_AreSkipped()
    {
        var visible = Window(10, new Rectangle(0, 0, 500, 500));
        var hidden = Window(11, new Rectangle(0, 0, 500, 500), visible: false);
        var cloaked = Window(12, new Rectangle(0, 0, 500, 500), cloaked: true);
        var minimized = Window(13, new Rectangle(0, 0, 500, 500), minimized: true);
        var empty = Window(14, Rectangle.Empty);

        var chosen = WindowSelection.Choose(
            new[] { visible, hidden, cloaked, minimized, empty },
            HintScanScope.ForegroundWindow,
            region: new Rectangle(0, 0, 500, 500),
            foreground: new IntPtr(10),
            includeTaskbar: false);

        Assert.Equal(new[] { new IntPtr(10) }, chosen.Select(w => w.Handle));
    }

    [Fact]
    public void MonitorScope_KeepsWindowsInsideTheRegionOnly()
    {
        var inside = Window(10, new Rectangle(-1500, 100, 600, 400));
        var straddling = Window(11, new Rectangle(-100, 100, 300, 200));
        var outside = Window(12, new Rectangle(3000, 100, 600, 400));

        var chosen = WindowSelection.Choose(
            new[] { inside, straddling, outside },
            HintScanScope.ActiveMonitor,
            region: new Rectangle(-1920, 0, 1920, 1080),
            foreground: new IntPtr(10),
            includeTaskbar: false);

        Assert.Equal(new[] { new IntPtr(10), new IntPtr(11) }, chosen.Select(w => w.Handle));
    }

    [Fact]
    public void Taskbar_IsIncludedOnlyWhenRequested_AndNeverDuplicated()
    {
        var app = Window(10, new Rectangle(0, 0, 1920, 1000));
        var taskbar = Window(20, new Rectangle(0, 1000, 1920, 80), className: "Shell_TrayWnd");
        var secondary = Window(21, new Rectangle(1920, 0, 60, 1024), className: "Shell_SecondaryTrayWnd");

        var withoutTaskbar = WindowSelection.Choose(
            new[] { app, taskbar, secondary },
            HintScanScope.AllMonitors,
            region: new Rectangle(0, 0, 3000, 1100),
            foreground: new IntPtr(10),
            includeTaskbar: false);

        Assert.Equal(new[] { new IntPtr(10) }, withoutTaskbar.Select(w => w.Handle));

        var withTaskbar = WindowSelection.Choose(
            new[] { app, taskbar, secondary },
            HintScanScope.AllMonitors,
            region: new Rectangle(0, 0, 3000, 1100),
            foreground: new IntPtr(10),
            includeTaskbar: true);

        Assert.Equal(
            new[] { new IntPtr(10), new IntPtr(20), new IntPtr(21) },
            withTaskbar.Select(w => w.Handle));
    }

    [Fact]
    public void DesktopShellWindows_AreIgnored()
    {
        var app = Window(10, new Rectangle(0, 0, 800, 600));
        var progman = Window(30, new Rectangle(0, 0, 1920, 1080), className: "Progman");
        var worker = Window(31, new Rectangle(0, 0, 1920, 1080), className: "WorkerW");

        var chosen = WindowSelection.Choose(
            new[] { app, progman, worker },
            HintScanScope.VirtualDesktop,
            region: new Rectangle(0, 0, 1920, 1080),
            foreground: new IntPtr(10),
            includeTaskbar: false);

        Assert.Equal(new[] { new IntPtr(10) }, chosen.Select(w => w.Handle));
    }

    [Fact]
    public void WindowCount_IsCapped_FrontMostFirst()
    {
        var windows = Enumerable.Range(0, 10)
            .Select(i => (ScreenWindow)Window(100 + i, new Rectangle(i, 0, 200, 200)))
            .ToArray();

        var chosen = WindowSelection.Choose(
            windows,
            HintScanScope.VirtualDesktop,
            region: new Rectangle(0, 0, 2000, 2000),
            foreground: IntPtr.Zero,
            includeTaskbar: false,
            maxWindows: 3);

        Assert.Equal(new[] { new IntPtr(100), new IntPtr(101), new IntPtr(102) },
            chosen.Select(w => w.Handle));
    }

    // ---------- region-aware target filtering ----------

    [Fact]
    public void Filter_KeepsTargetsOnNegativeOriginMonitor()
    {
        var monitors = new[] { Monitor(-1920, 0, 1920, 1080), Monitor(0, 0, 2560, 1440) };
        var raw = new[] { RawTarget(-1500, 300, 200, 80) };

        var result = HintTargetFilter.Apply(
            raw,
            monitors,
            maxElements: 100,
            region: new Rectangle(-1920, 0, 1920, 1080));

        var target = Assert.Single(result);
        Assert.Equal(new Rectangle(-1500, 300, 200, 80), target.Bounds);
        Assert.Equal(new Point(-1400, 340), target.Center);
    }

    [Fact]
    public void Filter_DropsTargetsOutsideTheRegion()
    {
        var monitors = new[] { Monitor(0, 0, 1920, 1080), Monitor(1920, 0, 1280, 1024) };
        var raw = new[]
        {
            RawTarget(100, 100, 200, 80),
            RawTarget(2200, 100, 200, 80)
        };

        var result = HintTargetFilter.Apply(
            raw,
            monitors,
            maxElements: 100,
            region: new Rectangle(0, 0, 1920, 1080));

        var target = Assert.Single(result);
        Assert.Equal(new Rectangle(100, 100, 200, 80), target.Bounds);
    }

    [Fact]
    public void Filter_ClipsTargetsAtMonitorAndRegionEdges()
    {
        var monitors = new[] { Monitor(0, 0, 1920, 1080) };
        var raw = new[] { RawTarget(1800, 1000, 400, 200) };

        var result = HintTargetFilter.Apply(
            raw,
            monitors,
            maxElements: 100,
            region: new Rectangle(0, 0, 1920, 1080));

        var target = Assert.Single(result);
        Assert.Equal(new Rectangle(1800, 1000, 120, 80), target.Bounds);
        Assert.True(target.Bounds.Left >= 0 && target.Bounds.Right <= 1920);
        Assert.True(target.Bounds.Top >= 0 && target.Bounds.Bottom <= 1080);
        Assert.True(target.Center.X < 1920 && target.Center.Y < 1080);
    }

    [Fact]
    public void Filter_ClipsToRegion_WhenRegionIsSmallerThanMonitor()
    {
        var monitors = new[] { Monitor(0, 0, 1920, 1080) };
        var raw = new[] { RawTarget(-100, 100, 400, 100) };

        var result = HintTargetFilter.Apply(
            raw,
            monitors,
            maxElements: 100,
            region: new Rectangle(0, 0, 1000, 1080));

        var target = Assert.Single(result);
        Assert.Equal(new Rectangle(0, 100, 300, 100), target.Bounds);
    }

    [Fact]
    public void Filter_WithoutRegion_BehavesAsBefore()
    {
        var monitors = new[] { Monitor(0, 0, 1920, 1080) };
        var result = HintTargetFilter.Apply(
            new[] { RawTarget(100, 100, 200, 80) },
            monitors,
            maxElements: 100);

        Assert.Single(result);
    }

    // ---------- controller wiring ----------

    [Fact]
    public void HintController_PassesConfiguredScopeAndPointerAnchor()
    {
        var harness = new RouterHarness(monitors: FakeMonitorService.Dual());
        harness.HintSettings.ScanScope = ScanScopeNames.AllMonitors;
        harness.Pointer = new Point(2000, 100);

        harness.TapCaps();
        Assert.True(harness.Down(PhysicalKey.H));
        Assert.True(harness.Up(PhysicalKey.H));

        Assert.Equal(HintScanScope.AllMonitors, harness.HintScanner.Options!.Scope);
        Assert.Equal(new Point(2000, 100), harness.HintScanner.Options.Anchor);
    }

    [Fact]
    public void HintController_DefaultsToActiveMonitor()
    {
        var harness = new RouterHarness();

        harness.TapCaps();
        Assert.True(harness.Down(PhysicalKey.H));
        Assert.True(harness.Up(PhysicalKey.H));

        Assert.Equal(HintScanScope.ActiveMonitor, harness.HintScanner.Options!.Scope);
    }

    // ---------- helpers ----------

    private static MonitorInfo Monitor(int x, int y, int width, int height, double scale = 1.0) =>
        new(
            new IntPtr((x * 100) + y + 1),
            new Rectangle(x, y, width, height),
            new Rectangle(x, y, width, height),
            scale);

    private static ScreenWindow Window(
        int handle,
        Rectangle bounds,
        uint processId = 42,
        bool visible = true,
        bool cloaked = false,
        bool minimized = false,
        int owner = 0,
        string className = "TestWindow") =>
        new(
            new IntPtr(handle),
            bounds,
            processId,
            visible,
            cloaked,
            minimized,
            new IntPtr(owner),
            className);

    private static RawClickableTarget RawTarget(int x, int y, int width, int height) =>
        new(new Rectangle(x, y, width, height), false, true);
}
