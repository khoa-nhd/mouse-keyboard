using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>
/// Which stage of the grid the overlay should draw: the full primary lattice
/// with every cell's complete code, the zoomed ten by three subgrid of the
/// chosen cell, or the zoomed three by three micro grid of the chosen sub-cell.
/// </summary>
public enum GridOverlayPhase
{
    Primary,
    Subgrid,
    Micro
}

/// <summary>
/// Everything the grid overlay needs for one frame.
///
/// <see cref="Typed"/> is the key sequence typed so far, and every label the
/// overlay draws is the complete sequence that reaches that cell (`AG`, `AGW`,
/// `AGWD`), so a cell can be identified before its parent is chosen.
/// <see cref="Target"/> is the virtual cursor position (the point a click would
/// land on).
/// </summary>
public sealed record GridOverlayState(
    MonitorInfo Monitor,
    Rectangle Root,
    GridOverlayPhase Phase,
    Rectangle? Column,
    Rectangle? Cell,
    Rectangle? Subcell,
    IReadOnlyList<string> Level1Labels,
    IReadOnlyList<string> Level2Labels,
    IReadOnlyList<string> SubgridLabels,
    IReadOnlyList<string> MicroLabels,
    string Typed,
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
