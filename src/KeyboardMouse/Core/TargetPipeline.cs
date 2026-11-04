namespace KeyboardMouse.Core;

/// <summary>
/// Turns a raw or fused target collection into the final hint list. The order is
/// deliberate:
/// 1. drop targets that are not actionable or fail explicit final rules,
/// 2. collapse repeated observations of the same control,
/// 3. remove containers that wrap a real child control,
/// 4. restore canonical top-to-bottom/left-to-right order and protect click
///    points that are close to a neighbouring target.
///
/// The pipeline is idempotent, so both fusion and the clickable-target adapter
/// can call it without penalising targets twice.
/// </summary>
public static class TargetPipeline
{
    public static IReadOnlyList<VisualTarget> Finalize(
        IReadOnlyList<VisualTarget> targets,
        TargetFilterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Count == 0)
        {
            return [];
        }

        IReadOnlyList<VisualTarget> filtered = TargetFinalFilter.Apply(
            targets,
            options);
        IReadOnlyList<VisualTarget> deduplicated =
            TargetDeduplicator.SuppressDuplicates(filtered);
        IReadOnlyList<VisualTarget> selected =
            TargetGeometrySelection.RemoveContainedContainers(deduplicated);

        IReadOnlyList<VisualTarget> ordered = selected
            .OrderBy(target => target.Bounds.Top)
            .ThenBy(target => target.Bounds.Left)
            .ThenBy(target => target.Id, StringComparer.Ordinal)
            .ToArray();

        return TargetGeometryNeighbors.Protect(ordered);
    }
}