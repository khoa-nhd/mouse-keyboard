using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>
/// Three-pixel frame around each monitor while mouse mode is on. Built from
/// four thin layered strips per monitor, so a 4K screen does not allocate a
/// full-screen bitmap. Optional via settings.feedback.screenBorder.
/// </summary>
public sealed class ScreenBorder : IDisposable
{
    private const int Thickness = 3;

    private readonly List<BorderStrip> _strips = new();
    private bool _visible;

    public void Refresh(Monitors monitors)
    {
        foreach (var strip in _strips)
        {
            strip.Dispose();
        }

        _strips.Clear();

        var color = Color.FromArgb(255, 46, 204, 113);

        foreach (var monitor in monitors.All)
        {
            var b = monitor.Bounds;
            if (b.Width < Thickness * 2 || b.Height < Thickness * 2)
            {
                continue;
            }

            Add(new Rectangle(b.Left, b.Top, b.Width, Thickness), color);
            Add(new Rectangle(b.Left, b.Bottom - Thickness, b.Width, Thickness), color);
            Add(new Rectangle(b.Left, b.Top + Thickness, Thickness, b.Height - (Thickness * 2)), color);
            Add(new Rectangle(b.Right - Thickness, b.Top + Thickness, Thickness, b.Height - (Thickness * 2)), color);
        }

        if (_visible)
        {
            foreach (var strip in _strips)
            {
                strip.ShowOverlay();
            }
        }
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        _visible = visible;
        foreach (var strip in _strips)
        {
            if (visible)
            {
                strip.ShowOverlay();
            }
            else
            {
                strip.HideOverlay();
            }
        }
    }

    private void Add(Rectangle rect, Color color)
    {
        var strip = new BorderStrip();
        strip.RenderSolid(rect.Width, rect.Height, rect.Left, rect.Top, color);
        _strips.Add(strip);
    }

    public void Dispose()
    {
        foreach (var strip in _strips)
        {
            strip.Dispose();
        }

        _strips.Clear();
    }

    private sealed class BorderStrip : OverlayForm
    {
    }
}
