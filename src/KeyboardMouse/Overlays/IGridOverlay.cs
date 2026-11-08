using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>Which stage of the grid the overlay should draw.</summary>
public enum GridOverlayPhase
{
    Level1,
    Level2,
    Subgrid
}

/// <summary>
/// Everything the grid overlay needs for one frame. The selected column and
/// cell are null until the matching level has been chosen, and
/// <see cref="Target"/> is the virtual cursor position (the point a click would
/// land on).
/// </summary>
public sealed record GridOverlayState(
    MonitorInfo Monitor,
    Rectangle Root,
    Rectangle? Column,
    Rectangle? Cell,
    IReadOnlyList<string> Level1Labels,
    IReadOnlyList<string> Level2Labels,
    IReadOnlyList<string> SubgridLabels,
    GridOverlayPhase Phase,
    Point Target);

/// <summary>Abstraction so GridController can be tested without real windows.</summary>
public interface IGridOverlay : IDisposable
{
    void Show(GridOverlayState state);

    void Hide();
}

internal sealed class NullGridOverlay : IGridOverlay
{
    public static readonly NullGridOverlay Instance = new();

    public void Show(GridOverlayState state)
    {
    }

    public void Hide()
    {
    }

    public void Dispose()
    {
    }
}
