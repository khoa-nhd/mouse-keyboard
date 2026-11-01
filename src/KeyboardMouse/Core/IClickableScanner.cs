namespace KeyboardMouse.Core;

public sealed record HintScanOptions(int MaxElements, bool IncludeTaskbar);

/// <summary>
/// Starts a hint scan without blocking the caller. Exactly one of the two
/// callbacks is invoked, from a background thread.
/// </summary>
public interface IClickableScanner
{
    void BeginScan(
        IntPtr window,
        HintScanOptions options,
        Action<IReadOnlyList<ClickableTarget>> completed,
        Action timedOut);
}

internal sealed class NullClickableScanner : IClickableScanner
{
    public static readonly NullClickableScanner Instance = new();

    public void BeginScan(
        IntPtr window,
        HintScanOptions options,
        Action<IReadOnlyList<ClickableTarget>> completed,
        Action timedOut) => completed([]);
}
