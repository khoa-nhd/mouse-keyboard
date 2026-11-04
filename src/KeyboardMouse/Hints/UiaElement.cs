using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using KeyboardMouse.Core;

namespace KeyboardMouse.Hints;

/// <summary>
/// Read-only view over one UI Automation element. The seam keeps the scanner
/// testable and ensures no FlaUI type leaks into the shared target model.
/// Property getters may throw when a provider does not support them; callers
/// read each value through an error boundary.
/// </summary>
internal interface IUiaElement
{
    string? Name { get; }

    string? AutomationId { get; }

    string? ClassName { get; }

    string? ControlType { get; }

    string? RuntimeId { get; }

    Rectangle Bounds { get; }

    bool IsEnabled { get; }

    bool IsOffscreen { get; }

    bool IsKeyboardFocusable { get; }

    UiaPatternFlags SupportedPatterns { get; }

    IReadOnlyList<IUiaElement> Children { get; }
}

/// <summary>
/// Owns one UI Automation session. The provider and all elements it creates
/// must be used on the same MTA thread and disposed before that thread ends.
/// </summary>
internal interface IUiaElementProvider : IDisposable
{
    IUiaElement? FromWindow(IntPtr window);
}

/// <summary>
/// FlaUI adapter. The scanner only sees <see cref="IUiaElement"/>, so tests can
/// exercise filtering/traversal without a real desktop or UIA session.
/// </summary>
internal sealed class FlaUiElementProvider : IUiaElementProvider
{
    private readonly UIA3Automation _automation = new();
    private bool _disposed;

    public IUiaElement? FromWindow(IntPtr window)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return window == IntPtr.Zero
            ? null
            : new FlaUiElement(_automation.FromHandle(window));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _automation.Dispose();
    }
}

internal sealed class FlaUiElement(AutomationElement element) : IUiaElement
{
    private UiaPatternFlags _supportedPatterns;
    private bool _patternsLoaded;

    public string? Name => element.Name;

    public string? AutomationId => element.AutomationId;

    public string? ClassName => element.ClassName;

    public string? ControlType => element.ControlType.ToString();

    public string? RuntimeId
    {
        get
        {
            int[]? ids = element.Properties.RuntimeId.Value;
            return ids is { Length: > 0 }
                ? string.Join(".", ids)
                : null;
        }
    }

    public Rectangle Bounds => element.BoundingRectangle;

    public bool IsEnabled => element.IsEnabled;

    public bool IsOffscreen => element.IsOffscreen;

    public bool IsKeyboardFocusable => element.Properties.IsKeyboardFocusable.Value;

    public UiaPatternFlags SupportedPatterns
    {
        get
        {
            if (!_patternsLoaded)
            {
                _patternsLoaded = true;
                _supportedPatterns = ReadPatterns();
            }

            return _supportedPatterns;
        }
    }

    public IReadOnlyList<IUiaElement> Children
    {
        get
        {
            AutomationElement[] children = element.FindAllChildren();
            var result = new IUiaElement[children.Length];

            for (int i = 0; i < children.Length; i++)
            {
                result[i] = new FlaUiElement(children[i]);
            }

            return result;
        }
    }

    private UiaPatternFlags ReadPatterns()
    {
        var patterns = UiaPatternFlags.None;

        AddPattern(ref patterns, UiaPatternFlags.Invoke, () => element.Patterns.Invoke.IsSupported);
        AddPattern(ref patterns, UiaPatternFlags.SelectionItem, () => element.Patterns.SelectionItem.IsSupported);
        AddPattern(ref patterns, UiaPatternFlags.Toggle, () => element.Patterns.Toggle.IsSupported);
        AddPattern(ref patterns, UiaPatternFlags.ExpandCollapse, () => element.Patterns.ExpandCollapse.IsSupported);
        AddPattern(ref patterns, UiaPatternFlags.Value, () => element.Patterns.Value.IsSupported);
        AddPattern(ref patterns, UiaPatternFlags.ScrollItem, () => element.Patterns.ScrollItem.IsSupported);
        AddPattern(ref patterns, UiaPatternFlags.Selection, () => element.Patterns.Selection.IsSupported);
        AddPattern(ref patterns, UiaPatternFlags.GridItem, () => element.Patterns.GridItem.IsSupported);
        AddPattern(ref patterns, UiaPatternFlags.RangeValue, () => element.Patterns.RangeValue.IsSupported);

        return patterns;
    }

    private static void AddPattern(
        ref UiaPatternFlags patterns,
        UiaPatternFlags pattern,
        Func<bool> isSupported)
    {
        try
        {
            if (isSupported())
            {
                patterns |= pattern;
            }
        }
        catch
        {
            // A provider may throw only for patterns it does not implement.
            // Keep checking the remaining patterns.
        }
    }
}
