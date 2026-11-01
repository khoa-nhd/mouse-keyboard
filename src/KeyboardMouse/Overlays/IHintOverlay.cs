using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>Feedback layer for hint mode; fakeable in controller tests.</summary>
public interface IHintOverlay : IDisposable
{
    void ShowScanning(MonitorInfo monitor, string text);

    void Show(
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<ClickableTarget> targets,
        IReadOnlyList<string> labels,
        string typed);

    void Hide();
}

internal sealed class NullHintOverlay : IHintOverlay
{
    public static readonly NullHintOverlay Instance = new();

    public void ShowScanning(MonitorInfo monitor, string text)
    {
    }

    public void Show(
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<ClickableTarget> targets,
        IReadOnlyList<string> labels,
        string typed)
    {
    }

    public void Hide()
    {
    }

    public void Dispose()
    {
    }
}
