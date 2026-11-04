using System.Diagnostics;
using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Native;
using KeyboardMouse.Platform;

namespace KeyboardMouse.Hints;

/// <summary>
/// Independent UI Automation scanner for the Hybrid Hint architecture.
///
/// It opens only the requested foreground window and, optionally, the shell
/// taskbar. Traversal is bounded in depth and node count, every element read is
/// isolated from the rest of the scan, and UIA3 runs on its own MTA thread. A
/// hard deadline prevents an unresponsive application from blocking the input
/// or UI threads; the deadline and the caller's cancellation token both produce
/// exactly one terminal callback.
///
/// This type does not generate labels and does not keep UIA elements after the
/// scan ends. All targets are normalized to the shared physical-pixel model.
/// </summary>
public sealed class UIAutomationScanner : IHintScanner
{
    public const int DefaultTimeoutMs = 400;

    internal const int DefaultMaxDepth = 16;
    internal const int DefaultMaxVisitedElements = 5000;

    private const int DuplicateCenterPixels = 4;
    private const int MaxOutputElements = 2000;

    private const UiaPatternFlags ActionablePatterns =
        UiaPatternFlags.Invoke |
        UiaPatternFlags.SelectionItem |
        UiaPatternFlags.Toggle |
        UiaPatternFlags.ExpandCollapse |
        UiaPatternFlags.Value |
        UiaPatternFlags.ScrollItem |
        UiaPatternFlags.GridItem |
        UiaPatternFlags.RangeValue;

    private static readonly HashSet<string> WantedControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Button",
        "Edit",
        "CheckBox",
        "RadioButton",
        "ComboBox",
        "TabItem",
        "MenuItem",
        "Hyperlink",
        "ListItem",
        "TreeItem",
        "SplitButton",
        "DataItem",
        "HeaderItem",
        "GridItem",
        "TableItem",
        "Slider",
        "Spinner"
    };

    // Containers are traversed but never emitted as hint targets. A structural
    // control may technically expose Invoke/Selection; clicking the container
    // is still not the hint the user expects.
    private static readonly HashSet<string> StructuralControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Window",
        "Pane",
        "Group",
        "ToolBar",
        "MenuBar",
        "Tree",
        "List",
        "Table",
        "DataGrid",
        "Document",
        "TitleBar",
        "Header",
        "StatusBar",
        "Separator",
        "ProgressBar",
        "ScrollBar",
        "Thumb"
    };

    private readonly IMonitorService? _monitors;
    private readonly Func<IUiaElementProvider> _providerFactory;
    private readonly Func<IntPtr> _taskbarWindow;
    private readonly TimeSpan _hardTimeout;
    private readonly int _maxDepth;
    private readonly int _maxVisitedElements;

    public UIAutomationScanner(IMonitorService? monitors = null)
        : this(
            () => new FlaUiElementProvider(),
            monitors,
            TimeSpan.FromMilliseconds(DefaultTimeoutMs))
    {
    }

    internal UIAutomationScanner(
        Func<IUiaElementProvider> providerFactory,
        IMonitorService? monitors = null,
        TimeSpan? timeout = null,
        int maxDepth = DefaultMaxDepth,
        int maxVisitedElements = DefaultMaxVisitedElements,
        Func<IntPtr>? taskbarWindow = null)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _monitors = monitors;
        _hardTimeout = timeout is { } value && value > TimeSpan.Zero
            ? value
            : TimeSpan.FromMilliseconds(DefaultTimeoutMs);
        _maxDepth = Math.Max(0, maxDepth);
        _maxVisitedElements = Math.Max(0, maxVisitedElements);
        _taskbarWindow = taskbarWindow ?? (() => User32.FindWindow("Shell_TrayWnd", null));
    }

    public void BeginScan(HintScanRequest request, Action<HintScanResult> completed)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(completed);

        if (request.Window == IntPtr.Zero ||
            (request.Options.Sources & TargetSource.Uia) == 0)
        {
            Complete(
                completed,
                HintScanResult.FromTargets(
                    [],
                    new HintScanDiagnostics(UiaUsed: false)));
            return;
        }

        if (request.CancellationToken.IsCancellationRequested)
        {
            Complete(
                completed,
                HintScanResult.Failure(
                    HintScanStatus.Canceled,
                    "The UI Automation scan was canceled."));
            return;
        }

        TimeSpan timeout = ResolveTimeout(request.Options);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken);
        int finished = 0;
        int timedOut = 0;
        System.Threading.Timer? timer = null;
        CancellationTokenRegistration cancellationRegistration = default;

        cancellationRegistration = request.CancellationToken.Register(
            () =>
            {
                if (Interlocked.Exchange(ref finished, 1) != 0)
                {
                    return;
                }

                timer?.Dispose();
                Complete(
                    completed,
                    HintScanResult.Failure(
                        HintScanStatus.Canceled,
                        "The UI Automation scan was canceled."));
            });

        if (Volatile.Read(ref finished) != 0)
        {
            cancellationRegistration.Dispose();
            linked.Dispose();
            return;
        }

        timer = new System.Threading.Timer(
            _ =>
            {
                if (Interlocked.Exchange(ref finished, 1) != 0)
                {
                    return;
                }

                Volatile.Write(ref timedOut, 1);

                try
                {
                    linked.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The worker won the completion race.
                }

                timer?.Dispose();
                Log.Info($"UI Automation hint scan timed out after {timeout.TotalMilliseconds:0} ms.");
                Complete(
                    completed,
                    HintScanResult.Failure(
                        HintScanStatus.TimedOut,
                        "The UI Automation scan timed out."));
            },
            null,
            timeout,
            Timeout.InfiniteTimeSpan);

        var thread = new Thread(() =>
        {
            HintScanResult result;

            try
            {
                result = Scan(request, linked.Token);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                bool wasTimeout = Volatile.Read(ref timedOut) != 0;
                result = HintScanResult.Failure(
                    wasTimeout ? HintScanStatus.TimedOut : HintScanStatus.Canceled,
                    wasTimeout
                        ? "The UI Automation scan timed out."
                        : "The UI Automation scan was canceled.");
            }
            catch (Exception ex)
            {
                Log.Error("UI Automation hint scan failed", ex);
                result = HintScanResult.Failure(
                    HintScanStatus.Failed,
                    "The UI Automation scan failed.");
            }
            finally
            {
                timer?.Dispose();
                cancellationRegistration.Dispose();
            }

            if (Interlocked.Exchange(ref finished, 1) == 0)
            {
                Complete(completed, result);
            }

            linked.Dispose();
        })
        {
            IsBackground = true,
            Name = "KeyboardMouse.UIAutomationScan"
        };

        // UIA3 is COM-based; all UIA calls stay on one MTA thread.
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    private HintScanResult Scan(HintScanRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();

        using IUiaElementProvider provider = _providerFactory();
        var roots = new List<IUiaElement>(2);

        IUiaElement? foreground = OpenRoot(provider, request.Window);
        if (foreground is not null)
        {
            roots.Add(foreground);
        }

        if (request.Options.IncludeTaskbar)
        {
            IntPtr taskbar = IntPtr.Zero;

            try
            {
                taskbar = _taskbarWindow();
            }
            catch (Exception ex)
            {
                Log.Error("Could not find the taskbar for a UIA hint scan", ex);
            }

            if (taskbar != IntPtr.Zero && taskbar != request.Window)
            {
                IUiaElement? taskbarRoot = OpenRoot(provider, taskbar);
                if (taskbarRoot is not null)
                {
                    roots.Add(taskbarRoot);
                }
            }
        }

        var monitors = GetMonitors();
        var candidates = new List<Candidate>();
        int visited = 0;
        int candidateCount = 0;

        foreach (IUiaElement root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Collect(
                root,
                monitors,
                cancellationToken,
                candidates,
                ref visited,
                ref candidateCount);

            if (visited >= _maxVisitedElements)
            {
                break;
            }
        }

        IReadOnlyList<VisualTarget> targets = BuildTargets(
            candidates,
            request.Options.MaxElements,
            GetScreenBounds(monitors),
            GetTargetDpiScale(monitors));
        int lowGeometryConfidence = targets.Count(
            target => target.Geometry.Confidence < 0.35);
        var diagnostics = new HintScanDiagnostics(
            CandidateCount: candidateCount,
            AcceptedCount: targets.Count,
            ElapsedMilliseconds: stopwatch.ElapsedMilliseconds,
            UiaUsed: true,
            LowGeometryConfidenceCount: lowGeometryConfidence);

        Log.Info(
            $"UI Automation hint scan found {targets.Count} target(s) " +
            $"from {candidateCount} candidate(s) in {diagnostics.ElapsedMilliseconds} ms.");

        return HintScanResult.FromTargets(targets, diagnostics);
    }

    private void Collect(
        IUiaElement root,
        IReadOnlyList<MonitorInfo> monitors,
        CancellationToken cancellationToken,
        List<Candidate> candidates,
        ref int visited,
        ref int candidateCount)
    {
        var queue = new Queue<Node>();
        EnqueueChildren(root, depth: 1, queue, visited, cancellationToken);

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (visited >= _maxVisitedElements)
            {
                return;
            }

            Node node = queue.Dequeue();
            visited++;

            AddCandidate(
                node.Element,
                monitors,
                cancellationToken,
                candidates,
                ref candidateCount);

            if (node.Depth < _maxDepth)
            {
                EnqueueChildren(
                    node.Element,
                    node.Depth + 1,
                    queue,
                    visited,
                    cancellationToken);
            }
        }
    }

    private void EnqueueChildren(
        IUiaElement element,
        int depth,
        Queue<Node> queue,
        int visited,
        CancellationToken cancellationToken)
    {
        if (depth > _maxDepth || _maxVisitedElements == 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<IUiaElement> children;

        try
        {
            children = element.Children;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A broken subtree must not hide its siblings.
            return;
        }

        for (int i = 0; i < children.Count; i++)
        {
            if (visited + queue.Count >= _maxVisitedElements)
            {
                break;
            }

            if (children[i] is { } child)
            {
                queue.Enqueue(new Node(child, depth));
            }
        }
    }

    private static void AddCandidate(
        IUiaElement element,
        IReadOnlyList<MonitorInfo> monitors,
        CancellationToken cancellationToken,
        List<Candidate> candidates,
        ref int candidateCount)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string? controlType = Read(() => element.ControlType, null);

            if (IsStructuralControl(controlType))
            {
                return;
            }

            bool wantedType = IsWantedControlType(controlType);
            bool isOffscreen;
            bool isEnabled;

            try
            {
                isOffscreen = element.IsOffscreen;
                isEnabled = element.IsEnabled;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return;
            }

            if (isOffscreen || !isEnabled || !TryReadBounds(element, out Rectangle sourceBounds))
            {
                return;
            }

            Rectangle? visibleBounds = ClipToMonitors(sourceBounds, monitors);
            if (visibleBounds is null)
            {
                return;
            }

            UiaPatternFlags patterns = ReadPatterns(element);
            if (!wantedType && (patterns & ActionablePatterns) == 0)
            {
                return;
            }

            string? automationId = Read(() => element.AutomationId, null);
            string? runtimeId = Read(() => element.RuntimeId, null);
            string? className = Read(() => element.ClassName, null);
            bool isKeyboardFocusable = Read(() => element.IsKeyboardFocusable, false);
            string text = Read(() => element.Name, string.Empty) ?? string.Empty;

            var metadata = new UiaTargetMetadata(
                AutomationId: automationId,
                RuntimeId: runtimeId,
                ControlType: controlType,
                ClassName: className,
                SupportsInvoke: (patterns & UiaPatternFlags.Invoke) != 0,
                SourceBounds: sourceBounds,
                IsKeyboardFocusable: isKeyboardFocusable,
                IsOffscreen: false,
                SupportedPatterns: patterns);

            candidates.Add(new Candidate(
                visibleBounds.Value,
                wantedType ? MapType(controlType) : TargetType.Other,
                text,
                metadata));
            candidateCount++;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A single malformed element must not abort the rest of the scan.
        }
    }

    private static bool TryReadBounds(IUiaElement element, out Rectangle bounds)
    {
        try
        {
            bounds = element.Bounds;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            bounds = default;
            return false;
        }

        long right = (long)bounds.X + bounds.Width;
        long bottom = (long)bounds.Y + bounds.Height;

        return bounds.Width > 0 &&
               bounds.Height > 0 &&
               right > bounds.X &&
               bottom > bounds.Y;
    }

    private static Rectangle? ClipToMonitors(
        Rectangle bounds,
        IReadOnlyList<MonitorInfo> monitors)
    {
        foreach (MonitorInfo monitor in monitors)
        {
            Rectangle visible = Rectangle.Intersect(bounds, monitor.Bounds);
            if (visible.Width > 0 && visible.Height > 0)
            {
                return visible;
            }
        }

        return null;
    }

    private IReadOnlyList<MonitorInfo> GetMonitors()
    {
        if (_monitors is not null)
        {
            return _monitors.All;
        }

        Rectangle virtualScreen = SystemInformation.VirtualScreen;

        return virtualScreen.Width > 0 && virtualScreen.Height > 0
            ? [new MonitorInfo(IntPtr.Zero, virtualScreen, virtualScreen, 1.0)]
            : [];
    }

    private static IReadOnlyList<VisualTarget> BuildTargets(
        IReadOnlyList<Candidate> candidates,
        int maxElements,
        Rectangle screen,
        double dpiScale)
    {
        int cap = Math.Clamp(maxElements, 1, MaxOutputElements);
        var selected = new List<Candidate>(Math.Min(candidates.Count, cap));
        var centers = new List<Point>(Math.Min(candidates.Count, cap));

        foreach (Candidate candidate in candidates)
        {
            Point center = Center(candidate.Bounds);
            bool duplicate = false;

            foreach (Point existing in centers)
            {
                if (Math.Abs(existing.X - center.X) <= DuplicateCenterPixels &&
                    Math.Abs(existing.Y - center.Y) <= DuplicateCenterPixels)
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
            {
                continue;
            }

            centers.Add(center);
            selected.Add(candidate);
        }

        Candidate[] ordered = selected
            .OrderBy(candidate => candidate.Bounds.Top)
            .ThenBy(candidate => candidate.Bounds.Left)
            .Take(cap)
            .ToArray();

        var targets = new VisualTarget[ordered.Length];

        for (int i = 0; i < ordered.Length; i++)
        {
            Candidate candidate = ordered[i];
            TargetBounds bounds = TargetBounds.FromRectangle(candidate.Bounds);
            bool clipped = candidate.Metadata.SourceBounds is { } sourceBounds &&
                           sourceBounds != candidate.Bounds;
            var geometry = new TargetGeometry
            {
                Confidence = clipped ? 0.82 : 0.98,
                IsClipped = clipped,
                Issues = clipped
                    ? GeometryIssue.PartiallyOffScreen
                    : GeometryIssue.None
            };
            Point clickPoint = HintCoordinates.ClampToBounds(bounds.Center, bounds);
            GeometryValidation validation = TargetGeometryValidator.Validate(
                bounds,
                clickPoint,
                candidate.Type,
                screen,
                dpiScale,
                strict: false,
                geometry);

            targets[i] = new VisualTarget(
                $"uia-{i + 1}",
                TargetSource.Uia,
                bounds,
                clickPoint,
                candidate.Type,
                candidate.Text,
                metadata: new TargetMetadata { Uia = candidate.Metadata },
                geometry: validation.Geometry,
                classificationConfidence: ClassificationConfidenceFor(candidate.Type));
        }

        return targets;
    }

    private static Rectangle GetScreenBounds(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            return virtualScreen.Width > 0 && virtualScreen.Height > 0
                ? virtualScreen
                : new Rectangle(0, 0, 1, 1);
        }

        Rectangle screen = monitors[0].Bounds;
        for (int i = 1; i < monitors.Count; i++)
        {
            screen = Rectangle.Union(screen, monitors[i].Bounds);
        }

        return screen;
    }

    private static double GetTargetDpiScale(IReadOnlyList<MonitorInfo> monitors)
    {
        foreach (MonitorInfo monitor in monitors)
        {
            if (double.IsFinite(monitor.Scale) && monitor.Scale > 0)
            {
                return monitor.Scale;
            }
        }

        return 1.0;
    }

    private static double ClassificationConfidenceFor(TargetType type) =>
        type switch
        {
            TargetType.Button or TargetType.CheckBox or TargetType.RadioButton or
            TargetType.ComboBox or TargetType.TabItem or TargetType.MenuItem or
            TargetType.Hyperlink or TargetType.ListItem or TargetType.TreeItem or
            TargetType.Edit => 0.98,
            TargetType.Other => 0.55,
            _ => 0.40
        };

    private static Point Center(Rectangle bounds) =>
        new(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));

    private static IUiaElement? OpenRoot(IUiaElementProvider provider, IntPtr window)
    {
        try
        {
            return provider.FromWindow(window);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("Could not open a window for the UIA hint scan", ex);
            return null;
        }
    }

    private static UiaPatternFlags ReadPatterns(IUiaElement element) =>
        Read(
            () => element.SupportedPatterns & UiaPatternFlags.All,
            UiaPatternFlags.None);

    private static T Read<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool IsWantedControlType(string? controlType) =>
        controlType is not null && WantedControlTypes.Contains(controlType);

    private static bool IsStructuralControl(string? controlType) =>
        controlType is not null && StructuralControlTypes.Contains(controlType);

    private static TargetType MapType(string? controlType) =>
        controlType?.ToLowerInvariant() switch
        {
            "button" or "splitbutton" => TargetType.Button,
            "edit" or "spinner" => TargetType.Edit,
            "checkbox" => TargetType.CheckBox,
            "radiobutton" => TargetType.RadioButton,
            "combobox" => TargetType.ComboBox,
            "tabitem" => TargetType.TabItem,
            "menuitem" => TargetType.MenuItem,
            "hyperlink" => TargetType.Hyperlink,
            "listitem" or "dataitem" or "griditem" or "tableitem" => TargetType.ListItem,
            "treeitem" => TargetType.TreeItem,
            _ => TargetType.Other
        };

    private TimeSpan ResolveTimeout(HintScanOptions options)
    {
        TimeSpan budget = options.Budget;

        return budget > TimeSpan.Zero && budget < _hardTimeout
            ? budget
            : _hardTimeout;
    }

    private static void Complete(Action<HintScanResult> completed, HintScanResult result)
    {
        try
        {
            completed(result);
        }
        catch
        {
            // A consumer callback must not crash a scanner timer or worker.
        }
    }

    private sealed record Candidate(
        Rectangle Bounds,
        TargetType Type,
        string Text,
        UiaTargetMetadata Metadata);

    private readonly record struct Node(IUiaElement Element, int Depth);
}
