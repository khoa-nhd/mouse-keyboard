using System.Diagnostics;
using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;

namespace KeyboardMouse.Tests;

public class UIAutomationScannerTests
{
    private static readonly IntPtr MainWindow = new(1234);
    private static readonly IntPtr TaskbarWindow = new(5678);

    [Fact]
    public async Task MapsButtonToVisualTargetAndPreservesMetadata()
    {
        var button = new FakeUiaElement
        {
            ControlType = "Button",
            Name = "Save",
            AutomationId = "save-button",
            ClassName = "Button",
            RuntimeId = "42.1",
            Bounds = new Rectangle(100, 200, 80, 30),
            IsEnabled = true,
            IsOffscreen = false,
            IsKeyboardFocusable = true,
            SupportedPatterns = UiaPatternFlags.Invoke | UiaPatternFlags.SelectionItem
        };
        var provider = ProviderWithRoot(Root(button));
        var scanner = CreateScanner(provider);
        var recorder = new ScanRecorder();

        HintScanResult result = await ScanAsync(scanner, recorder: recorder);

        Assert.Equal(HintScanStatus.Success, result.Status);
        VisualTarget target = Assert.Single(result.Targets);
        Assert.Equal("uia-1", target.Id);
        Assert.Equal(TargetSource.Uia, target.Source);
        Assert.Equal(TargetType.Button, target.Type);
        Assert.Equal("Save", target.Text);
        Assert.Equal(new TargetBounds(100, 200, 80, 30), target.Bounds);
        Assert.Equal(new Point(140, 215), target.ClickPoint);
        Assert.True(target.IsEnabled);
        Assert.True(target.IsVisible);
        Assert.True(target.IsActionable);

        UiaTargetMetadata metadata = Assert.IsType<UiaTargetMetadata>(target.Metadata.Uia);
        Assert.Equal("save-button", metadata.AutomationId);
        Assert.Equal("42.1", metadata.RuntimeId);
        Assert.Equal("Button", metadata.ControlType);
        Assert.Equal("Button", metadata.ClassName);
        Assert.True(metadata.SupportsInvoke);
        Assert.Equal(new Rectangle(100, 200, 80, 30), metadata.SourceBounds);
        Assert.True(metadata.IsKeyboardFocusable);
        Assert.False(metadata.IsOffscreen);
        Assert.Equal(
            UiaPatternFlags.Invoke | UiaPatternFlags.SelectionItem,
            metadata.SupportedPatterns);

        HintScanDiagnostics diagnostics = Assert.IsType<HintScanDiagnostics>(result.Diagnostics);
        Assert.Equal(1, diagnostics.CandidateCount);
        Assert.Equal(1, diagnostics.AcceptedCount);
        Assert.True(diagnostics.UiaUsed);
        Assert.False(diagnostics.CvUsed);
        Assert.True(provider.Disposed);
        Assert.Equal(1, recorder.Count);
    }

    [Theory]
    [InlineData("Button", TargetType.Button)]
    [InlineData("Edit", TargetType.Edit)]
    [InlineData("CheckBox", TargetType.CheckBox)]
    [InlineData("RadioButton", TargetType.RadioButton)]
    [InlineData("ComboBox", TargetType.ComboBox)]
    [InlineData("TabItem", TargetType.TabItem)]
    [InlineData("MenuItem", TargetType.MenuItem)]
    [InlineData("Hyperlink", TargetType.Hyperlink)]
    [InlineData("ListItem", TargetType.ListItem)]
    [InlineData("TreeItem", TargetType.TreeItem)]
    [InlineData("SplitButton", TargetType.Button)]
    [InlineData("DataItem", TargetType.ListItem)]
    [InlineData("HeaderItem", TargetType.Other)]
    [InlineData("GridItem", TargetType.ListItem)]
    [InlineData("TableItem", TargetType.ListItem)]
    [InlineData("Slider", TargetType.Other)]
    [InlineData("Spinner", TargetType.Edit)]
    public async Task MapsSupportedControlTypesToTargetTypes(
        string controlType,
        TargetType expected)
    {
        var element = new FakeUiaElement
        {
            ControlType = controlType,
            Name = controlType,
            Bounds = new Rectangle(10, 10, 80, 24)
        };
        var scanner = CreateScanner(ProviderWithRoot(Root(element)));

        HintScanResult result = await ScanAsync(scanner);

        VisualTarget target = Assert.Single(result.Targets);
        Assert.Equal(expected, target.Type);
    }

    [Fact]
    public async Task ClipsBoundsToMonitorAndKeepsSourceBounds()
    {
        var monitor = new MonitorInfo(
            IntPtr.Zero,
            new Rectangle(-100, -100, 500, 500),
            new Rectangle(-100, -100, 500, 500),
            1.0);
        var button = new FakeUiaElement
        {
            ControlType = "Button",
            Name = "Edge",
            Bounds = new Rectangle(-150, -80, 100, 60)
        };
        var scanner = CreateScanner(
            ProviderWithRoot(Root(button)),
            new FakeMonitorService(monitor));

        HintScanResult result = await ScanAsync(scanner);

        VisualTarget target = Assert.Single(result.Targets);
        Assert.Equal(new TargetBounds(-100, -80, 50, 60), target.Bounds);
        Assert.Equal(new Point(-75, -50), target.ClickPoint);
        Assert.Equal(
            new Rectangle(-150, -80, 100, 60),
            target.Metadata.Uia?.SourceBounds);
    }

    [Fact]
    public async Task FiltersDisabledOffscreenInvalidAndOutsideElements()
    {
        var root = Root(
            new FakeUiaElement
            {
                ControlType = "Button",
                Bounds = new Rectangle(10, 10, 20, 20),
                IsEnabled = false
            },
            new FakeUiaElement
            {
                ControlType = "Button",
                Bounds = new Rectangle(10, 50, 20, 20),
                IsOffscreen = true
            },
            new FakeUiaElement
            {
                ControlType = "Button",
                Bounds = new Rectangle(10, 90, 0, 20)
            },
            new FakeUiaElement
            {
                ControlType = "Button",
                Bounds = new Rectangle(3000, 3000, 20, 20)
            },
            new FakeUiaElement
            {
                ControlType = "Button",
                Name = "Valid",
                Bounds = new Rectangle(20, 20, 30, 30)
            });
        var scanner = CreateScanner(ProviderWithRoot(root));

        HintScanResult result = await ScanAsync(scanner);

        VisualTarget target = Assert.Single(result.Targets);
        Assert.Equal("Valid", target.Text);
        Assert.Equal(new TargetBounds(20, 20, 30, 30), target.Bounds);
        Assert.Equal(1, result.Diagnostics?.CandidateCount);
        Assert.Equal(1, result.Diagnostics?.AcceptedCount);
    }

    [Fact]
    public async Task TraversesStructuralContainersWithoutEmittingThem()
    {
        var pane = new FakeUiaElement
        {
            ControlType = "Pane",
            Bounds = new Rectangle(0, 0, 400, 300),
            SupportedPatterns = UiaPatternFlags.Invoke
        };
        pane.Add(new FakeUiaElement
        {
            ControlType = "Button",
            Name = "Inside",
            Bounds = new Rectangle(20, 20, 80, 24)
        });
        var scanner = CreateScanner(ProviderWithRoot(Root(pane)));

        HintScanResult result = await ScanAsync(scanner);

        VisualTarget target = Assert.Single(result.Targets);
        Assert.Equal("Inside", target.Text);
        Assert.Equal(TargetType.Button, target.Type);
    }

    [Fact]
    public async Task UsesActionablePatternsForUnknownInteractiveControls()
    {
        var root = Root(
            new FakeUiaElement
            {
                ControlType = "Custom",
                Name = "Selectable",
                Bounds = new Rectangle(10, 10, 40, 20),
                SupportedPatterns = UiaPatternFlags.SelectionItem
            },
            new FakeUiaElement
            {
                ControlType = "Custom",
                Name = "Selection container",
                Bounds = new Rectangle(10, 40, 40, 20),
                SupportedPatterns = UiaPatternFlags.Selection
            },
            new FakeUiaElement
            {
                ControlType = "Custom",
                Name = "Inert",
                Bounds = new Rectangle(10, 70, 40, 20)
            },
            new FakeUiaElement
            {
                ControlType = "Image",
                Name = "Invokable image",
                Bounds = new Rectangle(10, 100, 40, 20),
                SupportedPatterns = UiaPatternFlags.Invoke
            });
        var scanner = CreateScanner(ProviderWithRoot(root));

        HintScanResult result = await ScanAsync(scanner);

        Assert.Equal(2, result.Targets.Count);
        Assert.Equal(TargetType.Other, result.Targets[0].Type);
        Assert.Equal("Selectable", result.Targets[0].Text);
        Assert.Equal("Invokable image", result.Targets[1].Text);
        Assert.All(result.Targets, target => Assert.Equal(TargetSource.Uia, target.Source));
    }

    [Fact]
    public async Task KeepsTargetWhenOptionalPropertiesThrow()
    {
        var button = new FakeUiaElement
        {
            ControlType = "Button",
            Bounds = new Rectangle(10, 10, 80, 24),
            ThrowOnName = true,
            ThrowOnAutomationId = true,
            ThrowOnClassName = true,
            ThrowOnRuntimeId = true,
            ThrowOnKeyboardFocusable = true,
            ThrowOnPatterns = true
        };
        var scanner = CreateScanner(ProviderWithRoot(Root(button)));

        HintScanResult result = await ScanAsync(scanner);

        VisualTarget target = Assert.Single(result.Targets);
        Assert.Equal(string.Empty, target.Text);
        UiaTargetMetadata metadata = Assert.IsType<UiaTargetMetadata>(target.Metadata.Uia);
        Assert.Null(metadata.AutomationId);
        Assert.Null(metadata.RuntimeId);
        Assert.Null(metadata.ClassName);
        Assert.False(metadata.IsKeyboardFocusable);
        Assert.False(metadata.SupportsInvoke);
        Assert.Equal(UiaPatternFlags.None, metadata.SupportedPatterns);
    }

    [Fact]
    public async Task SkipsOnlyElementsWithInvalidBounds()
    {
        var root = Root(
            new FakeUiaElement
            {
                ControlType = "Button",
                Bounds = new Rectangle(10, 10, 80, 24),
                ThrowOnBounds = true
            },
            new FakeUiaElement
            {
                ControlType = "Button",
                Name = "Still here",
                Bounds = new Rectangle(20, 50, 80, 24)
            });
        var scanner = CreateScanner(ProviderWithRoot(root));

        HintScanResult result = await ScanAsync(scanner);

        VisualTarget target = Assert.Single(result.Targets);
        Assert.Equal("Still here", target.Text);
    }

    [Fact]
    public async Task MissingPatternsAreRepresentedAsNone()
    {
        var button = new FakeUiaElement
        {
            ControlType = "Button",
            Name = "Plain button",
            Bounds = new Rectangle(10, 10, 80, 24)
        };
        var scanner = CreateScanner(ProviderWithRoot(Root(button)));

        HintScanResult result = await ScanAsync(scanner);

        VisualTarget target = Assert.Single(result.Targets);
        Assert.Equal(UiaPatternFlags.None, target.Metadata.Uia?.SupportedPatterns);
        Assert.False(target.Metadata.Uia?.SupportsInvoke);
    }

    [Fact]
    public async Task HonorsMaximumTraversalDepth()
    {
        var deepButton = new FakeUiaElement
        {
            ControlType = "Button",
            Name = "Deep",
            Bounds = new Rectangle(10, 10, 80, 24)
        };
        var levelTwo = new FakeUiaElement
        {
            ControlType = "Pane",
            Bounds = new Rectangle(0, 0, 200, 200)
        };
        levelTwo.Add(deepButton);
        var levelOne = new FakeUiaElement
        {
            ControlType = "Pane",
            Bounds = new Rectangle(0, 0, 300, 300)
        };
        levelOne.Add(levelTwo);

        HintScanResult shallow = await ScanAsync(
            CreateScanner(ProviderWithRoot(Root(levelOne)), maxDepth: 2));
        Assert.Equal(HintScanStatus.NoTargets, shallow.Status);

        HintScanResult deep = await ScanAsync(
            CreateScanner(ProviderWithRoot(Root(levelOne)), maxDepth: 3));
        VisualTarget target = Assert.Single(deep.Targets);
        Assert.Equal("Deep", target.Text);
    }

    [Fact]
    public async Task HonorsMaximumVisitedElementCount()
    {
        var root = Root(
            Button("One", 10, 10),
            Button("Two", 10, 40),
            Button("Three", 10, 70),
            Button("Four", 10, 100),
            Button("Five", 10, 130));
        var scanner = CreateScanner(
            ProviderWithRoot(root),
            maxVisitedElements: 3);

        HintScanResult result = await ScanAsync(scanner);

        Assert.Equal(3, result.Targets.Count);
        Assert.Equal(3, result.Diagnostics?.CandidateCount);
    }

    [Fact]
    public async Task SortsTargetsAndAppliesOutputCap()
    {
        var root = Root(
            Button("Bottom", 50, 10),
            Button("Top", 10, 50),
            Button("First", 10, 10));
        var scanner = CreateScanner(ProviderWithRoot(root));

        HintScanResult result = await ScanAsync(
            scanner,
            new HintScanOptions(2, false) { Budget = TimeSpan.FromSeconds(5) });

        Assert.Equal(2, result.Targets.Count);
        Assert.Equal("First", result.Targets[0].Text);
        Assert.Equal("Bottom", result.Targets[1].Text);
        Assert.Equal("uia-1", result.Targets[0].Id);
        Assert.Equal("uia-2", result.Targets[1].Id);
        Assert.Equal(3, result.Diagnostics?.CandidateCount);
        Assert.Equal(2, result.Diagnostics?.AcceptedCount);
    }

    [Fact]
    public async Task DeduplicatesTargetsByVisibleCenter()
    {
        var root = Root(
            new FakeUiaElement
            {
                ControlType = "Button",
                Name = "Outer",
                Bounds = new Rectangle(10, 10, 80, 24)
            },
            new FakeUiaElement
            {
                ControlType = "Text",
                Name = "Inner",
                Bounds = new Rectangle(20, 14, 60, 16),
                SupportedPatterns = UiaPatternFlags.Invoke
            });
        var scanner = CreateScanner(ProviderWithRoot(root));

        HintScanResult result = await ScanAsync(scanner);

        VisualTarget target = Assert.Single(result.Targets);
        Assert.Equal("Outer", target.Text);
        Assert.Equal(2, result.Diagnostics?.CandidateCount);
        Assert.Equal(1, result.Diagnostics?.AcceptedCount);
    }

    [Fact]
    public async Task ReturnsNoTargetsWhenTreeHasNoInteractiveElements()
    {
        var scanner = CreateScanner(ProviderWithRoot(Root(
            new FakeUiaElement
            {
                ControlType = "Text",
                Name = "Label",
                Bounds = new Rectangle(10, 10, 100, 20)
            },
            new FakeUiaElement
            {
                ControlType = "Pane",
                Bounds = new Rectangle(0, 0, 400, 300)
            })));

        HintScanResult result = await ScanAsync(scanner);

        Assert.Equal(HintScanStatus.NoTargets, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Targets);
        Assert.Equal(0, result.Diagnostics?.AcceptedCount);
        Assert.True(result.Diagnostics?.UiaUsed);
    }

    [Fact]
    public async Task DoesNotScanWhenUiaSourceIsNotRequested()
    {
        var provider = ProviderWithRoot(Root(Button("Ignored", 10, 10)));
        var scanner = CreateScanner(provider);

        HintScanResult result = await ScanAsync(
            scanner,
            new HintScanOptions(100, false)
            {
                Sources = TargetSource.Cv,
                Budget = TimeSpan.FromSeconds(5)
            });

        Assert.Equal(HintScanStatus.NoTargets, result.Status);
        Assert.Empty(result.Targets);
        Assert.Equal(0, provider.FromWindowCount);
        Assert.False(result.Diagnostics?.UiaUsed);
    }

    [Fact]
    public async Task CancellationBeforeStartCompletesOnceWithoutScanning()
    {
        var provider = ProviderWithRoot(Root(Button("Ignored", 10, 10)));
        var scanner = CreateScanner(provider);
        var recorder = new ScanRecorder();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        scanner.BeginScan(
            new HintScanRequest(
                MainWindow,
                new HintScanOptions(100, false) { Budget = TimeSpan.FromSeconds(5) },
                cancellation.Token),
            recorder.Callback);

        HintScanResult result = await recorder.WaitAsync();

        Assert.Equal(HintScanStatus.Canceled, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Empty(result.Targets);
        Assert.Equal(0, provider.FromWindowCount);
        Assert.Equal(1, recorder.Count);
    }

    [Fact]
    public async Task CancellationDuringScanCompletesOnce()
    {
        var recorder = new ScanRecorder();
        using var cancellation = new CancellationTokenSource();
        var provider = ProviderWithRoot(Root(Button("Ignored", 10, 10)));
        provider.Handler = _ =>
        {
            cancellation.Cancel();
            return provider.DefaultRoot;
        };
        var scanner = CreateScanner(provider);

        scanner.BeginScan(
            new HintScanRequest(
                MainWindow,
                new HintScanOptions(100, false) { Budget = TimeSpan.FromSeconds(5) },
                cancellation.Token),
            recorder.Callback);

        HintScanResult result = await recorder.WaitAsync();

        Assert.Equal(HintScanStatus.Canceled, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(1, recorder.Count);
    }

    [Fact]
    public async Task TimesOutWithoutBlockingTheCallerAndCompletesOnce()
    {
        using var release = new ManualResetEventSlim();
        var provider = ProviderWithRoot(Root(Button("Late", 10, 10)));
        provider.Handler = _ =>
        {
            release.Wait(TimeSpan.FromSeconds(5));
            return provider.DefaultRoot;
        };
        var scanner = CreateScanner(
            provider,
            timeout: TimeSpan.FromMilliseconds(30));
        var recorder = new ScanRecorder();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            scanner.BeginScan(
                new HintScanRequest(
                    MainWindow,
                    new HintScanOptions(100, false) { Budget = TimeSpan.FromSeconds(5) },
                    CancellationToken.None),
                recorder.Callback);

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(1),
                "BeginScan must return without waiting for UI Automation.");

            HintScanResult result = await recorder.WaitAsync();
            Assert.Equal(HintScanStatus.TimedOut, result.Status);
            Assert.False(result.IsSuccess);
            Assert.Empty(result.Targets);
        }
        finally
        {
            release.Set();
        }

        await Task.Delay(100);
        Assert.Equal(1, recorder.Count);
    }

    [Fact]
    public async Task ScansTaskbarOnlyWhenRequested()
    {
        var mainRoot = Root(Button("Main", 10, 10));
        var taskbarRoot = Root(Button("Taskbar", 10, 40));

        var noTaskbarProvider = new FakeUiaElementProvider
        {
            Roots =
            {
                [MainWindow] = mainRoot,
                [TaskbarWindow] = taskbarRoot
            }
        };
        HintScanResult withoutTaskbar = await ScanAsync(
            CreateScanner(noTaskbarProvider, taskbarWindow: () => TaskbarWindow),
            new HintScanOptions(100, false) { Budget = TimeSpan.FromSeconds(5) });

        Assert.Equal("Main", Assert.Single(withoutTaskbar.Targets).Text);
        Assert.DoesNotContain(TaskbarWindow, noTaskbarProvider.RequestedWindows);

        var taskbarProvider = new FakeUiaElementProvider
        {
            Roots =
            {
                [MainWindow] = mainRoot,
                [TaskbarWindow] = taskbarRoot
            }
        };
        HintScanResult withTaskbar = await ScanAsync(
            CreateScanner(taskbarProvider, taskbarWindow: () => TaskbarWindow),
            new HintScanOptions(100, true) { Budget = TimeSpan.FromSeconds(5) });

        Assert.Equal(2, withTaskbar.Targets.Count);
        Assert.Contains(TaskbarWindow, taskbarProvider.RequestedWindows);
        Assert.Equal(
            new[] { MainWindow, TaskbarWindow },
            taskbarProvider.RequestedWindows);
    }

    [Fact]
    public async Task KeepsScanningTaskbarWhenForegroundRootFails()
    {
        var provider = new FakeUiaElementProvider
        {
            Roots =
            {
                [TaskbarWindow] = Root(Button("Taskbar", 10, 10))
            }
        };
        provider.Handler = window => window == MainWindow
            ? throw new InvalidOperationException("Foreground window unavailable.")
            : provider.Roots[window];

        HintScanResult result = await ScanAsync(
            CreateScanner(provider, taskbarWindow: () => TaskbarWindow),
            new HintScanOptions(100, true) { Budget = TimeSpan.FromSeconds(5) });

        VisualTarget target = Assert.Single(result.Targets);
        Assert.Equal("Taskbar", target.Text);
    }

    private static UIAutomationScanner CreateScanner(
        FakeUiaElementProvider provider,
        FakeMonitorService? monitors = null,
        TimeSpan? timeout = null,
        int maxDepth = UIAutomationScanner.DefaultMaxDepth,
        int maxVisitedElements = UIAutomationScanner.DefaultMaxVisitedElements,
        Func<IntPtr>? taskbarWindow = null) =>
        new(
            () => provider,
            monitors ?? FakeMonitorService.Single(),
            timeout ?? TimeSpan.FromSeconds(5),
            maxDepth,
            maxVisitedElements,
            taskbarWindow ?? (() => IntPtr.Zero));

    private static FakeUiaElementProvider ProviderWithRoot(FakeUiaElement root) =>
        new() { DefaultRoot = root };

    private static FakeUiaElement Root(params FakeUiaElement[] children)
    {
        var root = new FakeUiaElement
        {
            ControlType = "Window",
            Bounds = new Rectangle(0, 0, 1920, 1080)
        };
        root.Add(children);
        return root;
    }

    private static FakeUiaElement Button(string name, int x, int y) =>
        new()
        {
            ControlType = "Button",
            Name = name,
            Bounds = new Rectangle(x, y, 80, 24)
        };

    private static async Task<HintScanResult> ScanAsync(
        UIAutomationScanner scanner,
        HintScanOptions? options = null,
        CancellationToken cancellationToken = default,
        ScanRecorder? recorder = null)
    {
        recorder ??= new ScanRecorder();
        scanner.BeginScan(
            new HintScanRequest(
                MainWindow,
                options ?? new HintScanOptions(100, false) { Budget = TimeSpan.FromSeconds(5) },
                cancellationToken),
            recorder.Callback);

        return await recorder.WaitAsync();
    }

    private sealed class ScanRecorder
    {
        private readonly TaskCompletionSource<HintScanResult> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Action<HintScanResult> Callback => result =>
        {
            Interlocked.Increment(ref _count);
            _result.TrySetResult(result);
        };

        public async Task<HintScanResult> WaitAsync()
        {
            Task completed = await Task.WhenAny(
                _result.Task,
                Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.Same(_result.Task, completed);
            return await _result.Task;
        }
    }
}

internal sealed class FakeUiaElement : IUiaElement
{
    private readonly List<IUiaElement> _children = [];

    public string? Name { get; set; }

    public string? AutomationId { get; set; }

    public string? ClassName { get; set; }

    public string? ControlType { get; set; }

    public string? RuntimeId { get; set; }

    public Rectangle Bounds { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsOffscreen { get; set; }

    public bool IsKeyboardFocusable { get; set; }

    public UiaPatternFlags SupportedPatterns { get; set; }

    public bool ThrowOnName { get; set; }

    public bool ThrowOnAutomationId { get; set; }

    public bool ThrowOnClassName { get; set; }

    public bool ThrowOnRuntimeId { get; set; }

    public bool ThrowOnBounds { get; set; }

    public bool ThrowOnPatterns { get; set; }

    public bool ThrowOnKeyboardFocusable { get; set; }

    public bool ThrowOnChildren { get; set; }

    public FakeUiaElement Add(params FakeUiaElement[] children)
    {
        _children.AddRange(children);
        return this;
    }

    string? IUiaElement.Name => ThrowOnName
        ? throw new InvalidOperationException("Name is unsupported.")
        : Name;

    string? IUiaElement.AutomationId => ThrowOnAutomationId
        ? throw new InvalidOperationException("AutomationId is unsupported.")
        : AutomationId;

    string? IUiaElement.ClassName => ThrowOnClassName
        ? throw new InvalidOperationException("ClassName is unsupported.")
        : ClassName;

    string? IUiaElement.ControlType => ControlType;

    string? IUiaElement.RuntimeId => ThrowOnRuntimeId
        ? throw new InvalidOperationException("RuntimeId is unsupported.")
        : RuntimeId;

    Rectangle IUiaElement.Bounds => ThrowOnBounds
        ? throw new InvalidOperationException("Bounds are unsupported.")
        : Bounds;

    bool IUiaElement.IsEnabled => IsEnabled;

    bool IUiaElement.IsOffscreen => IsOffscreen;

    bool IUiaElement.IsKeyboardFocusable => ThrowOnKeyboardFocusable
        ? throw new InvalidOperationException("Keyboard focus is unsupported.")
        : IsKeyboardFocusable;

    UiaPatternFlags IUiaElement.SupportedPatterns => ThrowOnPatterns
        ? throw new InvalidOperationException("Patterns are unsupported.")
        : SupportedPatterns;

    IReadOnlyList<IUiaElement> IUiaElement.Children => ThrowOnChildren
        ? throw new InvalidOperationException("Children are unsupported.")
        : _children;
}

internal sealed class FakeUiaElementProvider : IUiaElementProvider
{
    public IUiaElement? DefaultRoot { get; set; }

    public Dictionary<IntPtr, IUiaElement?> Roots { get; } = [];

    public Func<IntPtr, IUiaElement?>? Handler { get; set; }

    public List<IntPtr> RequestedWindows { get; } = [];

    public int FromWindowCount { get; private set; }

    public bool Disposed { get; private set; }

    public IUiaElement? FromWindow(IntPtr window)
    {
        FromWindowCount++;
        RequestedWindows.Add(window);

        return Handler is not null
            ? Handler(window)
            : Roots.TryGetValue(window, out IUiaElement? root)
                ? root
                : DefaultRoot;
    }

    public void Dispose() => Disposed = true;
}
