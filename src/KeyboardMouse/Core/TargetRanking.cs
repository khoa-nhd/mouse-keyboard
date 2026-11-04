namespace KeyboardMouse.Core;

/// <summary>
/// Relative importance of the ranking signals. Weights are heuristic ordering
/// signals, not probabilities. They are normalized by their sum, so callers can
/// tune one weight without having to keep the set summing to exactly one.
/// </summary>
public sealed class TargetRankingWeights
{
    /// <summary>
    /// Fresh default weights. Returning a new instance prevents one caller from
    /// mutating the defaults observed by every other scan.
    /// </summary>
    public static TargetRankingWeights Default => new();

    public double Detection { get; set; } = 0.24;

    public double Geometry { get; set; } = 0.22;

    public double Interaction { get; set; } = 0.18;

    public double Classification { get; set; } = 0.12;

    public double SourceReliability { get; set; } = 0.10;

    public double Uniqueness { get; set; } = 0.09;

    public double NonDuplicate { get; set; } = 0.05;

    internal double NormalizedSum
    {
        get
        {
            double sum =
                NonNegative(Detection) +
                NonNegative(Geometry) +
                NonNegative(Interaction) +
                NonNegative(Classification) +
                NonNegative(SourceReliability) +
                NonNegative(Uniqueness) +
                NonNegative(NonDuplicate);

            return sum > 0 ? sum : 1.0;
        }
    }

    private static double NonNegative(double value) =>
        double.IsFinite(value) && value > 0 ? value : 0;
}

/// <summary>One target's ranking inputs and final ordering score.</summary>
public readonly record struct TargetRanking(
    double Score,
    double Ambiguity,
    double DuplicateLikelihood,
    double SourceReliability);

/// <summary>
/// Deterministic target ranking. Detection confidence is only one input:
/// interaction, classification, geometry, source reliability, ambiguity and
/// duplicate likelihood all participate, so a perfectly recognised but
/// unclickable text region does not outrank a real button.
/// </summary>
public static class TargetRanker
{
    public static TargetRanking Evaluate(
        VisualTarget target,
        IReadOnlyList<VisualTarget>? peers = null,
        TargetRankingWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        TargetRankingWeights actualWeights = weights ?? TargetRankingWeights.Default;
        double ambiguity = ComputeAmbiguity(target);
        double duplicateLikelihood = peers is null
            ? 0
            : ComputeDuplicateLikelihood(target, peers);
        double sourceReliability = SourceReliability(target.Source);

        double normalizedSum = actualWeights.NormalizedSum;
        double score =
            (NonNegative(actualWeights.Detection) * Clamp(target.DetectionConfidence)) +
            (NonNegative(actualWeights.Geometry) * Clamp(target.Geometry.Confidence)) +
            (NonNegative(actualWeights.Interaction) * Clamp(target.InteractionConfidence)) +
            (NonNegative(actualWeights.Classification) * Clamp(target.ClassificationConfidence)) +
            (NonNegative(actualWeights.SourceReliability) * sourceReliability) +
            (NonNegative(actualWeights.Uniqueness) * (1.0 - ambiguity)) +
            (NonNegative(actualWeights.NonDuplicate) * (1.0 - duplicateLikelihood));

        return new TargetRanking(
            Math.Clamp(score / normalizedSum, 0, 1),
            ambiguity,
            duplicateLikelihood,
            sourceReliability);
    }

    /// <summary>
    /// Orders targets by composite score, then by area and id. The id tie-break
    /// makes ordering stable across runs when two candidates are otherwise equal.
    /// </summary>
    public static IReadOnlyList<VisualTarget> Rank(
        IReadOnlyList<VisualTarget> targets,
        TargetRankingWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Count < 2)
        {
            return targets;
        }

        var duplicateLikelihood = ComputeDuplicateLikelihoods(targets);
        var rankings = new Dictionary<string, TargetRanking>(
            targets.Count,
            StringComparer.Ordinal);

        for (int i = 0; i < targets.Count; i++)
        {
            VisualTarget target = targets[i];
            rankings[target.Id] = EvaluateCore(
                target,
                duplicateLikelihood[i],
                weights ?? TargetRankingWeights.Default);
        }

        return targets
            .OrderByDescending(target => rankings[target.Id].Score)
            .ThenByDescending(target => (long)target.Bounds.Width * target.Bounds.Height)
            .ThenBy(target => target.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static double SourceReliability(TargetSource source)
    {
        bool uia = (source & TargetSource.Uia) != 0;
        bool ocr = (source & TargetSource.CvOcr) != 0;
        bool contour = (source & TargetSource.CvContour) != 0;

        if (uia && (ocr || contour))
        {
            return 1.0;
        }

        if (uia)
        {
            return 0.98;
        }

        if (ocr && contour)
        {
            return 0.78;
        }

        return ocr ? 0.72 : 0.62;
    }

    /// <summary>
    /// Ambiguity in [0, 1]. Geometry flags, interaction/classification
    /// uncertainty and inherently non-interactive types all contribute. It is a
    /// ranking/filtering signal and never contains text or pixels.
    /// </summary>
    public static double ComputeAmbiguity(VisualTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        double ambiguity =
            ((1.0 - Clamp(target.InteractionConfidence)) * 0.34) +
            ((1.0 - Clamp(target.ClassificationConfidence)) * 0.16);

        ambiguity += target.Type switch
        {
            TargetType.Text => 0.30,
            TargetType.Unknown => 0.20,
            TargetType.Other => 0.16,
            TargetType.Icon => 0.08,
            _ => 0
        };

        GeometryIssue issues = target.Geometry.Issues;

        if ((issues & GeometryIssue.InvalidTransform) != 0) ambiguity += 0.45;
        if ((issues & GeometryIssue.OffScreen) != 0) ambiguity += 0.45;
        if ((issues & GeometryIssue.ContainerLike) != 0) ambiguity += 0.30;
        if ((issues & GeometryIssue.AmbiguousShape) != 0) ambiguity += 0.22;
        if ((issues & GeometryIssue.ImplausiblyLarge) != 0) ambiguity += 0.22;
        if ((issues & GeometryIssue.WeakEdges) != 0) ambiguity += 0.18;
        if ((issues & GeometryIssue.OverlapsNeighbor) != 0) ambiguity += 0.14;
        if ((issues & GeometryIssue.TooSmall) != 0) ambiguity += 0.15;
        if ((issues & GeometryIssue.UsedFallback) != 0) ambiguity += 0.12;
        if ((issues & GeometryIssue.PartiallyOffScreen) != 0) ambiguity += 0.10;
        if ((issues & GeometryIssue.SharedBoundary) != 0) ambiguity += 0.06;

        return Math.Clamp(ambiguity, 0, 1);
    }

    public static double ComputeDuplicateLikelihood(
        VisualTarget target,
        IReadOnlyList<VisualTarget> peers)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(peers);

        double maximum = 0;

        foreach (VisualTarget peer in peers)
        {
            if (ReferenceEquals(peer, target) ||
                string.Equals(peer.Id, target.Id, StringComparison.Ordinal))
            {
                continue;
            }

            if (TargetSimilarity.TryGetDuplicate(target, peer, out double likelihood))
            {
                maximum = Math.Max(maximum, likelihood);
            }
        }

        return maximum;
    }

    /// <summary>Orders two targets using the same deterministic criteria.</summary>
    public static int Compare(
        VisualTarget left,
        VisualTarget right,
        IReadOnlyDictionary<string, TargetRanking>? rankings = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        TargetRanking leftRanking = rankings is not null &&
                                    rankings.TryGetValue(left.Id, out TargetRanking existingLeft)
            ? existingLeft
            : Evaluate(left);
        TargetRanking rightRanking = rankings is not null &&
                                     rankings.TryGetValue(right.Id, out TargetRanking existingRight)
            ? existingRight
            : Evaluate(right);

        int byScore = rightRanking.Score.CompareTo(leftRanking.Score);
        if (byScore != 0)
        {
            return byScore;
        }

        long leftArea = (long)left.Bounds.Width * left.Bounds.Height;
        long rightArea = (long)right.Bounds.Width * right.Bounds.Height;
        int byArea = rightArea.CompareTo(leftArea);
        return byArea != 0
            ? byArea
            : string.CompareOrdinal(left.Id, right.Id);
    }

    private static double[] ComputeDuplicateLikelihoods(
        IReadOnlyList<VisualTarget> targets)
    {
        var likelihoods = new double[targets.Count];

        for (int i = 0; i < targets.Count; i++)
        {
            for (int j = i + 1; j < targets.Count; j++)
            {
                if (!TargetSimilarity.TryGetDuplicate(
                        targets[i],
                        targets[j],
                        out double likelihood))
                {
                    continue;
                }

                likelihoods[i] = Math.Max(likelihoods[i], likelihood);
                likelihoods[j] = Math.Max(likelihoods[j], likelihood);
            }
        }

        return likelihoods;
    }

    private static TargetRanking EvaluateCore(
        VisualTarget target,
        double duplicateLikelihood,
        TargetRankingWeights weights)
    {
        double ambiguity = ComputeAmbiguity(target);
        double sourceReliability = SourceReliability(target.Source);
        double normalizedSum = weights.NormalizedSum;
        double score =
            (NonNegative(weights.Detection) * Clamp(target.DetectionConfidence)) +
            (NonNegative(weights.Geometry) * Clamp(target.Geometry.Confidence)) +
            (NonNegative(weights.Interaction) * Clamp(target.InteractionConfidence)) +
            (NonNegative(weights.Classification) * Clamp(target.ClassificationConfidence)) +
            (NonNegative(weights.SourceReliability) * sourceReliability) +
            (NonNegative(weights.Uniqueness) * (1.0 - ambiguity)) +
            (NonNegative(weights.NonDuplicate) * (1.0 - duplicateLikelihood));

        return new TargetRanking(
            Math.Clamp(score / normalizedSum, 0, 1),
            ambiguity,
            duplicateLikelihood,
            sourceReliability);
    }

    private static double NonNegative(double value) =>
        double.IsFinite(value) && value > 0 ? value : 0;

    private static double Clamp(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
