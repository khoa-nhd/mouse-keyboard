/// <summary>
/// Result of an action execution against a <see cref="VisualTarget"/>.
/// </summary>
public sealed record ActionResult
{
    /// <summary>
    /// Action was executed successfully.</summary>
    public static readonly ActionResult Success = new() { IsSuccessful = true };

    /// <summary>
    /// Action failed (target became invalid during execution).</summary>
    public static readonly ActionResult Failure = new() { IsSuccessful = false, Reason = "Action failed" };

    /// <summary>
    /// Action was canceled (foreground window changed, mode deactivated, etc.).</summary>
    public static readonly ActionResult Canceled = new() { IsSuccessful = false, IsStale = true, Reason = "Action canceled" };

    /// <summary>
    /// Target is stale or no longer valid.</summary>
    public static readonly ActionResult Stale = new() { IsSuccessful = false, IsStale = true, Reason = "Target is stale" };

    /// <summary>
    /// Target is not actionable (disabled, offscreen, etc.).</summary>
    public static readonly ActionResult NotActionable = new() { IsSuccessful = false, IsNotActionable = true, Reason = "Target is not actionable" };

    /// <summary>Whether the action was successful.</summary>
    public bool IsSuccessful { get; init; }

    /// <summary>Whether the target is stale.</summary>
    public bool IsStale { get; init; }

    /// <summary>Whether the target is not actionable.</summary>
    public bool IsNotActionable { get; init; }

    /// <summary>Human-readable reason.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Creates a new ActionResult.</summary>
    public ActionResult(bool isSuccessful = false, bool isStale = false, bool isNotActionable = false, string reason = "")
    {
        IsSuccessful = isSuccessful;
        IsStale = isStale;
        IsNotActionable = isNotActionable;
        Reason = reason;
    }
}