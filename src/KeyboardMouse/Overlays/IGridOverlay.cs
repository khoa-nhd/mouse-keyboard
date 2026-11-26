using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>
/// Everything the grid overlay needs for one frame.
///
/// <see cref="Labels"/> holds one four-character code per cell, row-major;
/// <see cref="Typed"/> is what the user has typed so far, so cells that no
/// longer match can fade and the current match can be highlighted.
///
/// For the recursive grid <see cref="Recursive"/> is true: <see cref="Root"/>
/// is the current level's rectangle, the lattice is 3 x 3, <see cref="Labels"/>
/// holds the nine cell keys, and <see cref="Depth"/> of <see cref="MaxDepth"/>
/// says how far it has narrowed.
/// </summary>
public sealed record GridOverlayState(
    MonitorInfo Monitor,
    Rectangle Root,
    int Columns,
    int Rows,
    IReadOnlyList<string> Labels,
    string Typed,
    int Highlight,
    Point Target,
    bool Recursive = false,
    int Depth = 0,
    int MaxDepth = 0);

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
