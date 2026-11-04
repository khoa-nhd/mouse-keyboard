namespace KeyboardMouse.Core;

/// <summary>
/// Collapses repeated observations of one control while preserving separate
/// adjacent controls. Duplicate edges are evaluated pairwise; the representative
/// of each connected group is chosen with semantics (button before its label or
/// icon) and then the composite ranking, never by list order alone.
/// </summary>
public static class TargetDeduplicator
{
    public static IReadOnlyList<VisualTarget> SuppressDuplicates(
        IReadOnlyList<VisualTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Count < 2)
        {
            return targets;
        }

        var parent = new int[targets.Count];
        for (int i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            for (int j = i + 1; j < targets.Count; j++)
            {
                if (TargetSimilarity.TryGetDuplicate(targets[i], targets[j], out _))
                {
                    Union(parent, i, j);
                }
            }
        }

        var groups = new Dictionary<int, List<VisualTarget>>();
        for (int i = 0; i < targets.Count; i++)
        {
            int root = Find(parent, i);
            if (!groups.TryGetValue(root, out List<VisualTarget>? group))
            {
                group = [];
                groups[root] = group;
            }

            group.Add(targets[i]);
        }

        var rankings = new Dictionary<string, TargetRanking>(
            targets.Count,
            StringComparer.Ordinal);

        foreach (VisualTarget target in targets)
        {
            rankings[target.Id] = TargetRanker.Evaluate(target, targets);
        }

        var kept = new List<VisualTarget>(groups.Count);

        foreach (List<VisualTarget> group in groups.Values)
        {
            VisualTarget representative = SelectRepresentative(group, rankings);
            kept.Add(representative);
        }

        return kept
            .OrderBy(target => target.Bounds.Top)
            .ThenBy(target => target.Bounds.Left)
            .ThenBy(target => target.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static VisualTarget SelectRepresentative(
        IReadOnlyList<VisualTarget> group,
        IReadOnlyDictionary<string, TargetRanking> rankings)
    {
        VisualTarget representative = group[0];

        for (int i = 1; i < group.Count; i++)
        {
            representative = Prefer(representative, group[i], rankings);
        }

        return representative;
    }

    private static VisualTarget Prefer(
        VisualTarget left,
        VisualTarget right,
        IReadOnlyDictionary<string, TargetRanking> rankings)
    {
        if (left.Bounds.Contains(right.Bounds))
        {
            if (TargetSimilarity.IsInteractiveLike(left.Type) &&
                TargetSimilarity.IsDecorative(right.Type))
            {
                return left;
            }

            if (TargetSimilarity.IsContainerLike(left.Type) &&
                TargetSimilarity.IsInteractiveLike(right.Type))
            {
                return right;
            }
        }

        if (right.Bounds.Contains(left.Bounds))
        {
            if (TargetSimilarity.IsInteractiveLike(right.Type) &&
                TargetSimilarity.IsDecorative(left.Type))
            {
                return right;
            }

            if (TargetSimilarity.IsContainerLike(right.Type) &&
                TargetSimilarity.IsInteractiveLike(left.Type))
            {
                return left;
            }
        }

        bool leftUia = (left.Source & TargetSource.Uia) != 0;
        bool rightUia = (right.Source & TargetSource.Uia) != 0;

        if (leftUia != rightUia)
        {
            // UIA carries the interaction contract (patterns, enabled state,
            // focusability). Keep it unless it is only a wrapper around a real
            // interactive CV control, which the containment checks above cover.
            return leftUia ? left : right;
        }

        TargetRanking leftRanking = rankings[left.Id];
        TargetRanking rightRanking = rankings[right.Id];

        int byScore = rightRanking.Score.CompareTo(leftRanking.Score);
        if (byScore != 0)
        {
            return byScore > 0 ? right : left;
        }

        long leftArea = (long)left.Bounds.Width * left.Bounds.Height;
        long rightArea = (long)right.Bounds.Width * right.Bounds.Height;
        int byArea = rightArea.CompareTo(leftArea);
        if (byArea != 0)
        {
            return byArea > 0 ? right : left;
        }

        return string.CompareOrdinal(left.Id, right.Id) <= 0 ? left : right;
    }

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return index;
    }

    private static void Union(int[] parent, int left, int right)
    {
        int leftRoot = Find(parent, left);
        int rightRoot = Find(parent, right);

        if (leftRoot == rightRoot)
        {
            return;
        }

        // Smaller root wins so grouping is deterministic.
        if (leftRoot < rightRoot)
        {
            parent[rightRoot] = leftRoot;
        }
        else
        {
            parent[leftRoot] = rightRoot;
        }
    }
}
