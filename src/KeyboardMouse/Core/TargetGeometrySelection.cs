using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// Chooses between nested targets. A large UIA pane or custom container should
/// not hide a smaller, clearly interactive child. Conversely, a small text or
/// icon box inside a real button must not replace the button.
/// </summary>
public static class TargetGeometrySelection
{
    /// <summary>
    /// Removes containers that merely wrap a smaller control, removes duplicate
    /// nested boxes, and preserves distinct adjacent targets.
    /// </summary>
    public static IReadOnlyList<VisualTarget> RemoveContainedContainers(
        IReadOnlyList<VisualTarget> targets)
    {
        var kept = new List<VisualTarget>(targets.Count);

        foreach (VisualTarget target in targets
                     .Where(target => target.IsActionable)
                     .OrderByDescending(target => (long)target.Bounds.Width * target.Bounds.Height)
                     .ThenByDescending(target => target.RankingConfidence)
                     .ThenBy(target => target.Id))
        {
            bool add = true;

            for (int i = 0; i < kept.Count; i++)
            {
                VisualTarget existing = kept[i];

                if (existing.Bounds.Contains(target.Bounds))
                {
                    if (ShouldPreferChild(existing, target))
                    {
                        kept.RemoveAt(i);
                        i--;
                        continue;
                    }

                    if (IsDistinctNestedControl(existing, target))
                    {
                        continue;
                    }

                    add = false;
                    break;
                }

                if (target.Bounds.Contains(existing.Bounds))
                {
                    if (ShouldPreferChild(target, existing))
                    {
                        add = false;
                        break;
                    }

                    if (IsDistinctNestedControl(target, existing))
                    {
                        continue;
                    }

                    // The incoming target is a duplicate/container around an
                    // already accepted child.
                    add = false;
                    break;
                }

                if (!existing.Bounds.IntersectsWith(target.Bounds))
                {
                    continue;
                }

                long overlap = OverlapArea(existing.Bounds, target.Bounds);
                long existingArea =
                    (long)existing.Bounds.Width * existing.Bounds.Height;
                long targetArea =
                    (long)target.Bounds.Width * target.Bounds.Height;
                long smallerArea = Math.Min(existingArea, targetArea);
                long largerArea = Math.Max(existingArea, targetArea);

                if (smallerArea > 0 &&
                    overlap * 100 >= smallerArea * 75 &&
                    largerArea <= smallerArea * 2.5)
                {
                    // Near-duplicate overlap: keep the earlier (larger/more
                    // confident) target. Distinct neighbours overlapping by a
                    // few pixels do not reach this threshold.
                    add = false;
                    break;
                }
            }

            if (add)
            {
                kept.Add(target);
            }
        }

        return kept
            .OrderBy(target => target.Bounds.Top)
            .ThenBy(target => target.Bounds.Left)
            .ToArray();
    }

    public static bool IsContainerLike(TargetType type) =>
        type is TargetType.Unknown or TargetType.Other or TargetType.Text;

    public static bool IsInteractiveLike(TargetType type) =>
        type is TargetType.Button or
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

    /// <summary>
    /// Nested controls with different interaction semantics are distinct
    /// targets, not duplicates. A checkbox inside a list row, for example, can
    /// be clicked without activating the row. Text/icon children are handled by
    /// <see cref="ShouldPreferChild"/> and are still treated as decoration.
    /// </summary>
    public static bool IsDistinctNestedControl(
        VisualTarget parent,
        VisualTarget child)
    {
        if (ReferenceEquals(parent, child) ||
            !parent.Bounds.Contains(child.Bounds) ||
            !IsInteractiveLike(parent.Type) ||
            !IsInteractiveLike(child.Type) ||
            IsContainerLike(child.Type))
        {
            return false;
        }

        if (parent.Type == child.Type)
        {
            return false;
        }

        return !TargetSimilarity.IsDecorative(child.Type);
    }

    private static bool ShouldPreferChild(VisualTarget container, VisualTarget child)
    {
        if (!IsContainerLike(container.Type) ||
            !IsInteractiveLike(child.Type) ||
            !container.Bounds.Contains(child.Bounds))
        {
            return false;
        }

        long containerArea =
            (long)container.Bounds.Width * container.Bounds.Height;
        long childArea = (long)child.Bounds.Width * child.Bounds.Height;

        if (childArea <= 0 || containerArea < childArea * 2)
        {
            return false;
        }

        // A tiny dot inside a pane is usually an icon belonging to a larger
        // control; keep it only when it is large enough to be its own target.
        return child.Bounds.Width >= 8 && child.Bounds.Height >= 6;
    }

    private static long OverlapArea(TargetBounds a, TargetBounds b)
    {
        TargetBounds overlap = a.Intersect(b);
        return overlap.IsValid
            ? (long)overlap.Width * overlap.Height
            : 0;
    }
}
