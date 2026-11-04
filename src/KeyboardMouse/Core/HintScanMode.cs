namespace KeyboardMouse.Core;

/// <summary>
/// Which detection source hint mode should use.
///
/// <para><see cref="Auto"/> is the safe default: UI Automation runs first and
/// computer vision is only used when UIA finds nothing inside the window's
/// client area (custom-drawn UIs, or a window that only exposes caption
/// buttons).</para>
/// </summary>
public enum HintScanMode
{
    Auto,
    Uia,
    Cv,
    Hybrid
}

/// <summary>
/// Parsing and defaults for <see cref="HintScanMode"/>. Settings store the
/// mode as a short lower-case id so a hand-edited settings file stays readable.
/// </summary>
public static class HintScanModes
{
    public const string AutoId = "auto";
    public const string UiaId = "uia";
    public const string CvId = "cv";
    public const string HybridId = "hybrid";

    /// <summary>Per-source hard budget. Mirrors the legacy UIA scanner cap.</summary>
    public static readonly TimeSpan SourceBudget = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Overall budget including the CV fallback. Auto may have to run UIA and
    /// then CV; Hybrid runs both sources concurrently.
    /// </summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromMilliseconds(700);

    public static string Normalize(string? value) => Parse(value) switch
    {
        HintScanMode.Uia => UiaId,
        HintScanMode.Cv => CvId,
        HintScanMode.Hybrid => HybridId,
        _ => AutoId
    };

    public static HintScanMode Parse(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            UiaId => HintScanMode.Uia,
            CvId => HintScanMode.Cv,
            HybridId => HintScanMode.Hybrid,
            _ => HintScanMode.Auto
        };

    public static string ToId(HintScanMode mode) => mode switch
    {
        HintScanMode.Uia => UiaId,
        HintScanMode.Cv => CvId,
        HintScanMode.Hybrid => HybridId,
        _ => AutoId
    };

    public static string DisplayName(HintScanMode mode) => mode switch
    {
        HintScanMode.Uia => "UI Automation only",
        HintScanMode.Cv => "Computer vision only",
        HintScanMode.Hybrid => "Hybrid (UIA + CV)",
        _ => "Auto (UIA, then CV)"
    };

    /// <summary>Sources a mode can query. Auto starts with both and falls back.</summary>
    public static TargetSource SourcesFor(HintScanMode mode) => mode switch
    {
        HintScanMode.Uia => TargetSource.Uia,
        HintScanMode.Cv => TargetSource.Cv,
        _ => TargetSource.All
    };

    public static TimeSpan BudgetFor(HintScanMode mode) => DefaultBudget;

    /// <summary>Budget that grows with the selected screen scope.</summary>
    public static TimeSpan BudgetFor(HintScanMode mode, HintScanScope scope) =>
        HintScanScopes.BudgetFor(mode, scope);
}
