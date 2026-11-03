using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Output;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Tests;

internal sealed class FakeMouseOutput : IMouseOutput
{
    public List<string> Calls { get; } = new();

    /// <summary>Lets the harness keep its fake pointer in sync with moves.</summary>
    public Action<int, int>? OnMove { get; set; }

    public int Count(string call) => Calls.Count(c => c == call);

    public void MoveTo(int x, int y)
    {
        Calls.Add($"move:{x},{y}");
        OnMove?.Invoke(x, y);
    }

    public void LeftDown() => Calls.Add("left-down");
    public void LeftUp() => Calls.Add("left-up");
    public void RightDown() => Calls.Add("right-down");
    public void RightUp() => Calls.Add("right-up");
    public void MiddleDown() => Calls.Add("middle-down");
    public void MiddleUp() => Calls.Add("middle-up");

    public void LeftClick() => Calls.Add("left-click");
    public void RightClick() => Calls.Add("right-click");
    public void MiddleClick() => Calls.Add("middle-click");
    public void DoubleClick() => Calls.Add("double-click");

    public void XButtonDown(MouseXButton button) => Calls.Add($"x{(int)button}-down");
    public void XButtonUp(MouseXButton button) => Calls.Add($"x{(int)button}-up");
    public void XButtonClick(MouseXButton button) => Calls.Add($"x{(int)button}-click");

    public void Wheel(int delta) => Calls.Add($"wheel:{delta}");

    public void HorizontalWheel(int delta) => Calls.Add($"hwheel:{delta}");

    public void ReleaseAll() => Calls.Add("release-all");
}

internal sealed class FakeMovementControl : IMovementControl
{
    public HashSet<Direction> Active { get; } = new();

    public List<(Direction Direction, bool Down)> Calls { get; } = new();

    public MovementMode Mode { get; private set; } = MovementMode.Move;

    public SpeedProfile MoveProfile { get; private set; } = SpeedProfile.Normal;

    public SpeedProfile ScrollProfile { get; private set; } = SpeedProfiles.Default.Scroll;

    public bool SmoothScroll { get; private set; } = true;

    public bool ScrollPrecision { get; private set; }

    public void SetDirection(Direction direction, bool down)
    {
        Calls.Add((direction, down));
        if (down)
        {
            Active.Add(direction);
        }
        else
        {
            Active.Remove(direction);
        }
    }

    public void ClearDirections() => Active.Clear();

    public void SetMode(MovementMode mode) => Mode = mode;

    public void SetMoveProfile(SpeedProfile profile) => MoveProfile = profile;

    public void SetScrollProfile(SpeedProfile profile, bool smooth)
    {
        ScrollProfile = profile;
        SmoothScroll = smooth;
    }

    public void SetScrollPrecision(bool precision) => ScrollPrecision = precision;
}

internal sealed class FakeKeyOutput : IKeyOutput
{
    public List<PhysicalKey> Keys { get; } = new();

    public List<(bool Ctrl, PhysicalKey Key)> Chords { get; } = new();

    public void SendKey(PhysicalKey key) => Keys.Add(key);

    public void SendChord(bool ctrl, PhysicalKey key) => Chords.Add((ctrl, key));
}

internal sealed class FakeHintScanner : IClickableScanner
{
    public int ScanCount { get; private set; }

    public IntPtr Window { get; private set; }

    public HintScanOptions? Options { get; private set; }

    public Action<IReadOnlyList<ClickableTarget>>? Completed { get; private set; }

    public Action? TimedOut { get; private set; }

    public void BeginScan(
        IntPtr window,
        HintScanOptions options,
        Action<IReadOnlyList<ClickableTarget>> completed,
        Action timedOut)
    {
        ScanCount++;
        Window = window;
        Options = options;
        Completed = completed;
        TimedOut = timedOut;
    }

    public void Complete(params ClickableTarget[] targets) => Completed?.Invoke(targets);

    public void Timeout() => TimedOut?.Invoke();
}

internal sealed class FakeHintOverlay : IHintOverlay
{
    public bool Visible { get; private set; }

    public bool Scanning { get; private set; }

    public int ShowCount { get; private set; }

    public int HideCount { get; private set; }

    public MonitorInfo? ScanMonitor { get; private set; }

    public string ScanText { get; private set; } = string.Empty;

    public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = [];

    public IReadOnlyList<ClickableTarget> Targets { get; private set; } = [];

    public IReadOnlyList<string> Labels { get; private set; } = [];

    public string Typed { get; private set; } = string.Empty;

    public List<string> TypedHistory { get; } = new();

    public void ShowScanning(MonitorInfo monitor, string text)
    {
        Visible = true;
        Scanning = true;
        ScanMonitor = monitor;
        ScanText = text;
    }

    public void Show(
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<ClickableTarget> targets,
        IReadOnlyList<string> labels,
        string typed)
    {
        Visible = true;
        Scanning = false;
        ShowCount++;
        Monitors = monitors;
        Targets = targets;
        Labels = labels;
        Typed = typed;
        TypedHistory.Add(typed);
    }

    public void Hide()
    {
        if (Visible)
        {
            HideCount++;
        }

        Visible = false;
        Scanning = false;
    }

    public void Dispose() => Hide();
}

internal sealed class FakeGridOverlay : IGridOverlay
{
    public bool Visible { get; private set; }

    public int ShowCount { get; private set; }

    public int HideCount { get; private set; }

    public MonitorInfo? Monitor { get; private set; }

    public Rectangle ActiveRect { get; private set; }

    public IReadOnlyList<string> Labels { get; private set; } = [];

    public void Show(
        MonitorInfo monitor,
        Rectangle activeRect,
        IReadOnlyList<string> labels,
        int depth,
        int maxDepth)
    {
        Visible = true;
        ShowCount++;
        Monitor = monitor;
        ActiveRect = activeRect;
        Labels = labels;
    }

    public void Hide()
    {
        if (Visible)
        {
            HideCount++;
        }

        Visible = false;
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeMonitorService : IMonitorService
{
    private readonly List<MonitorInfo> _monitors;

    public FakeMonitorService(params MonitorInfo[] monitors)
    {
        _monitors = monitors.Length > 0 ? monitors.ToList() : [DefaultMonitor()];
    }

    public Rectangle? ForegroundBounds { get; set; }

    public IReadOnlyList<MonitorInfo> All => _monitors;

    public static FakeMonitorService Single() => new();

    public static FakeMonitorService Dual() => new(
        new MonitorInfo(
            new IntPtr(1),
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(0, 0, 1920, 1040),
            1.0),
        new MonitorInfo(
            new IntPtr(2),
            new Rectangle(1920, 0, 1280, 1024),
            new Rectangle(1920, 0, 1280, 984),
            2.0));

    public MonitorInfo GetAtPoint(Point point)
    {
        foreach (var monitor in _monitors)
        {
            if (monitor.Bounds.Contains(point))
            {
                return monitor;
            }
        }

        return _monitors[0];
    }

    public MonitorInfo Next(Point pointer)
    {
        int index = _monitors.FindIndex(m => m.Bounds.Contains(pointer));
        if (index < 0)
        {
            index = 0;
        }

        return _monitors[(index + 1) % _monitors.Count];
    }

    public Rectangle? GetForegroundWindowBounds() => ForegroundBounds;

    public Point? GetForegroundWindowCenter() => ForegroundBounds is { } bounds
        ? new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2))
        : null;

    public double GetScaleAtPoint(Point point) => GetAtPoint(point).Scale;

    public (double X, double Y) ClampToDesktop(double x, double y)
    {
        int left = _monitors.Min(m => m.Bounds.Left);
        int top = _monitors.Min(m => m.Bounds.Top);
        int right = _monitors.Max(m => m.Bounds.Right);
        int bottom = _monitors.Max(m => m.Bounds.Bottom);

        return (
            Math.Clamp(x, left, right - 1),
            Math.Clamp(y, top, bottom - 1));
    }

    private static MonitorInfo DefaultMonitor() => new(
        IntPtr.Zero,
        new Rectangle(0, 0, 1920, 1080),
        new Rectangle(0, 0, 1920, 1040),
        1.0);
}

internal sealed class RouterHarness
{
    public AppState State { get; } = new();

    public FakeMouseOutput Mouse { get; } = new();

    public FakeMovementControl Movement { get; } = new();

    public FakeKeyOutput Keys { get; } = new();

    public FakeGridOverlay Grid { get; } = new();

    public FakeHintScanner HintScanner { get; } = new();

    public FakeHintOverlay HintOverlay { get; } = new();

    public FakeMonitorService Monitors { get; }

    public InMemoryMarkStore Marks { get; } = new();

    public KeysSettings KeySettings { get; } = new();

    public HintSettings HintSettings { get; } = new();

    public IntPtr Foreground { get; set; } = new(4242);

    public SpeedProfiles Profiles { get; } = SpeedProfiles.Default;

    public InputRouter Router { get; }

    public Point Pointer { get; set; } = new(100, 100);

    public long Now { get; set; }

    public RouterHarness(int tapTimeoutMs = 200, FakeMonitorService? monitors = null)
    {
        Monitors = monitors ?? FakeMonitorService.Single();
        Mouse.OnMove = (x, y) => Pointer = new Point(x, y);

        Router = new InputRouter(
            State,
            Movement,
            Mouse,
            tapTimeoutMs,
            clock: () => Now,
            keyOutput: Keys,
            profiles: Profiles,
            monitors: Monitors,
            gridOverlay: Grid,
            marks: Marks,
            keys: KeySettings,
            clickableScanner: HintScanner,
            hintOverlay: HintOverlay,
            hintSettings: HintSettings,
            foreground: () => Foreground,
            dispatch: action => action(),
            cursor: () => Pointer,
            autoClick: AutoClickSettings,
            autoClickScheduler: AutoClickTimer.Start);
    }

    public AutoClickSettings AutoClickSettings { get; } = new();

    public FakePeriodicTimer AutoClickTimer { get; } = new();

    public bool Down(PhysicalKey key, bool repeat = false) =>
        Router.HandleKey(new KeyEvent(key, IsDown: true, repeat, Now));

    public bool Up(PhysicalKey key) =>
        Router.HandleKey(new KeyEvent(key, IsDown: false, IsRepeat: false, Now));

    public void Tick() => Router.Tick();

    /// <summary>A quick Caps press/release with no other key in between.</summary>
    public void TapCaps()
    {
        Down(PhysicalKey.CapsLock);
        Now += 50;
        Up(PhysicalKey.CapsLock);
    }
}

/// <summary>Stands in for the auto clicker's periodic timer; tests fire ticks by hand.</summary>
internal sealed class FakePeriodicTimer
{
    private Action? _tick;

    public int IntervalMs { get; private set; }

    public int Starts { get; private set; }

    public bool Running => _tick is not null;

    public IDisposable Start(int intervalMs, Action tick)
    {
        IntervalMs = intervalMs;
        Starts++;
        _tick = tick;
        return new Handle(this, tick);
    }

    public void Fire(int times = 1)
    {
        for (int i = 0; i < times; i++)
        {
            _tick?.Invoke();
        }
    }

    private sealed class Handle(FakePeriodicTimer owner, Action tick) : IDisposable
    {
        public void Dispose()
        {
            if (owner._tick == tick)
            {
                owner._tick = null;
            }
        }
    }
}
