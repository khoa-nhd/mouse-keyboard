namespace KeyboardMouse.Core;

/// <summary>
/// Explicit final-target rules. Thresholds are ordering/filtering heuristics,
/// not calibrated probabilities. UIA remains authoritative for an actionable
/// control; the stricter confidence and type rules apply to CV-only targets so
/// a perfectly recognised text region cannot become a hint.
/// </summary>
public sealed class TargetFilterOptions
{
    public double MinCvDetectionConfidence { get; set; } = 0.40;

    public double MinCvInteractionConfidence { get; set; } = 0.25;

    public double MinCvGeometryConfidence { get; set; } = 0.25;

    public double MaxCvAmbiguity { get; set; } = 0.85;

    public double MinUiaDetectionConfidence { get; set; } = 0.30;

    /// <summary>
    /// DPI scale used for type-specific size limits. Scanners have already
    /// validated against the real monitor DPI; this second pass is a final
    /// consistency check for callers that only have canonical bounds.
    /// </summary>
    public double DpiScale { get; set; } = 1.0;
}

/// <summary>
/// The last filtering stage. It removes invalid, low-confidence, unsupported
/// and oversized CV targets while preserving actionable UIA targets, then keeps
/// canonical output order. Duplicate and nesting decisions happen before this
/// stage in <see cref="TargetPipeline"/>.
/// </summary>
public static class TargetFinalFilter
{
    public static IReadOnlyList<VisualTarget> Apply(
        IReadOnlyList<VisualTarget> targets,
        TargetFilterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(targets);

        TargetFilterOptions actual = options ?? new TargetFilterOptions();
        var kept = new List<VisualTarget>(targets.Count);

        foreach (VisualTarget target in targets)
        {
            if (!target.IsActionable)
            {
                continue;
            }

            bool uiaBacked = (target.Source & TargetSource.Uia) != 0;

            if (!HasSupportedType(target, uiaBacked))
            {
                continue;
            }

            if (uiaBacked)
            {
                if (target.DetectionConfidence < actual.MinUiaDetectionConfidence)
                {
                    continue;
                }
            }
            else
            {
                if (target.DetectionConfidence < actual.MinCvDetectionConfidence ||
                    target.InteractionConfidence < actual.MinCvInteractionConfidence ||
                    target.Geometry.Confidence < actual.MinCvGeometryConfidence ||
                    TargetRanker.ComputeAmbiguity(target) > actual.MaxCvAmbiguity)
                {
                    continue;
                }
            }

            if (!HasSupportedSize(target, uiaBacked, actual.DpiScale))
            {
                continue;
            }

            kept.Add(target);
        }

        return kept;
    }

    private static bool HasSupportedType(VisualTarget target, bool uiaBacked)
    {
        if (uiaBacked)
        {
            // UIA can expose an actionable control whose semantic mapping is
            // "Other"; that is still a real interaction contract.
            return true;
        }

        return target.Type is TargetType.Button or
            TargetType.Hyperlink or
            TargetType.MenuItem or
            TargetType.TabItem or
            TargetType.ListItem or
            TargetType.TreeItem or
            TargetType.CheckBox or
            TargetType.RadioButton or
            TargetType.ComboBox or
            TargetType.Edit or
            TargetType.Icon;
    }

    private static bool HasSupportedSize(
        VisualTarget target,
        bool uiaBacked,
        double dpiScale)
    {
        TargetSizeLimits limits = TargetGeometryValidator.LimitsFor(
            target.Type,
            dpiScale);

        if (target.Bounds.Width < limits.MinWidth ||
            target.Bounds.Height < limits.MinHeight)
        {
            return false;
        }

        // UIA is authoritative for a real control, so its maximum is only a
        // guard against a whole-window rectangle. CV has already passed strict
        // validation; this catches a malformed transformed result.
        double maximumFactor = uiaBacked ? 4.0 : 1.0;

        return target.Bounds.Width <= limits.MaxWidth * maximumFactor &&
               target.Bounds.Height <= limits.MaxHeight * maximumFactor &&
               target.Bounds.Width / (double)Math.Max(1, target.Bounds.Height) <=
               limits.MaxAspect * maximumFactor;
    }
}
