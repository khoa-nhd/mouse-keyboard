using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>Abstraction so GridController can be tested without real windows.</summary>
public interface IGridOverlay : IDisposable
{
    void Show(MonitorInfo monitor, Rectangle activeRect, IReadOnlyList<string> labels, int depth, int maxDepth);

    void Hide();
}

internal sealed class NullGridOverlay : IGridOverlay
{
    public static readonly NullGridOverlay Instance = new();

    public void Show(MonitorInfo monitor, Rectangle activeRect, IReadOnlyList<string> labels, int depth, int maxDepth)
    {
    }

    public void Hide()
    {
    }

    public void Dispose()
    {
    }
}
