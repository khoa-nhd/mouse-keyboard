using System.Collections.Generic;
using System.Linq;
using KeyboardMouse.Core;

/// <summary>
/// Fuses UIA and CV target collections into one harmonized collection.
///
/// Matching uses IoU, normalized centre distance, containment, text, type,
/// shape and visual grouping. Candidate pairs are resolved with a deterministic
/// maximum-weight bipartite assignment, not a one-pass greedy loop, so competing
/// candidates cannot steal each other's best match. Unmatched targets from
/// either source survive independently.
/// </summary>
public sealed class TargetFusionEngine
{
    /// <summary>Weight for Intersection-over-Union (IoU) of bounds.</summary>
    public double IoUWeight { get; set; } = 0.28;

    /// <summary>Weight for normalized center-to-center distance.</summary>
    public double CenterDistWeight { get; set; } = 0.14;

    /// <summary>Weight for text equality/similarity.</summary>
    public double TextWeight { get; set; } = 0.12;

    /// <summary>Weight for target-type compatibility.</summary>
    public double TypeWeight { get; set; } = 0.10;

    /// <summary>Weight for containment (intersection over the smaller box).</summary>
    public double ContainmentWeight { get; set; } = 0.18;

    /// <summary>Weight for shape similarity (aspect and size).</summary>
    public double ShapeWeight { get; set; } = 0.08;

    /// <summary>Weight for visual grouping (alignment and proximity).</summary>
    public double SpatialWeight { get; set; } = 0.10;

    /// <summary>Fusion confidence threshold: a match is accepted only when the
    /// normalized total score meets or exceeds this value.</summary>
    public double MatchThreshold { get; set; } = 0.55;

    /// <summary>Minimum CV detection confidence for an unmatched CV-only target.</summary>
    public double MinCvConfidence { get; set; } = 0.40;

    /// <summary>Minimum UIA detection confidence for an unmatched UIA-only target.</summary>
    public double MinUiaConfidence { get; set; } = 0.30;

    /// <summary>Above this many candidate cells a component uses deterministic
    /// greedy assignment plus swap improvement instead of the exact solver.</summary>
    public int MaxExactAssignmentCells { get; set; } = 2_000_000;

    /// <summary>Construct a <see cref="TargetFusionEngine"/> with default weights.</summary>
    public TargetFusionEngine() { }

    /// <summary>Fusion weights that prioritize geometric overlap.</summary>
    public static TargetFusionEngine GeometricPrior =>
        new()
        {
            IoUWeight = 0.36,
            CenterDistWeight = 0.16,
            TextWeight = 0.08,
            TypeWeight = 0.08,
            ContainmentWeight = 0.16,
            ShapeWeight = 0.08,
            SpatialWeight = 0.08,
            MatchThreshold = 0.50,
        };

    /// <summary>Fusion weights that prioritize text/OCR association.</summary>
    public static TargetFusionEngine TextPrior =>
        new()
        {
            IoUWeight = 0.18,
            CenterDistWeight = 0.10,
            TextWeight = 0.28,
            TypeWeight = 0.12,
            ContainmentWeight = 0.14,
            ShapeWeight = 0.08,
            SpatialWeight = 0.10,
            MatchThreshold = 0.60,
        };

    /// <summary>Fusion weights that prioritize UIA compatibility.</summary>
    public static TargetFusionEngine UiaPrior =>
        new()
        {
            IoUWeight = 0.22,
            CenterDistWeight = 0.14,
            TextWeight = 0.10,
            TypeWeight = 0.22,
            ContainmentWeight = 0.14,
            ShapeWeight = 0.08,
            SpatialWeight = 0.10,
            MatchThreshold = 0.50,
        };

    /// <summary>
    /// Fuses UIA and CV targets into a final, deduplicated target list.
    /// </summary>
    public HintScanResult Fuse(
        IReadOnlyList<VisualTarget> uiaTargets,
        IReadOnlyList<VisualTarget> cvTargets)
    {
        ArgumentNullException.ThrowIfNull(uiaTargets);
        ArgumentNullException.ThrowIfNull(cvTargets);

        if (uiaTargets.Count == 0 && cvTargets.Count == 0)
        {
            return HintScanResult.FromTargets([]);
        }

        List<MatchInfo> matches = ResolveMatches(
            ComputeMatches(uiaTargets, cvTargets),
            uiaTargets.Count,
            cvTargets.Count);

        var matchedUia = new HashSet<string>(StringComparer.Ordinal);
        var matchedCv = new HashSet<string>(StringComparer.Ordinal);
        var fusedTargets = new List<VisualTarget>(
            Math.Max(uiaTargets.Count, cvTargets.Count));

        foreach (MatchInfo match in matches)
        {
            if (!matchedUia.Add(match.UiaTarget.Id))
            {
                continue;
            }

            if (!matchedCv.Add(match.CvTarget.Id))
            {
                continue;
            }

            fusedTargets.Add(MergeTargets(match.UiaTarget, match.CvTarget));
        }

        foreach (VisualTarget target in uiaTargets)
        {
            if (!matchedUia.Contains(target.Id) &&
                target.IsActionable &&
                target.DetectionConfidence >= MinUiaConfidence)
            {
                fusedTargets.Add(target);
            }
        }

        foreach (VisualTarget target in cvTargets)
        {
            if (!matchedCv.Contains(target.Id) &&
                target.DetectionConfidence >= MinCvConfidence)
            {
                fusedTargets.Add(target);
            }
        }

        return HintScanResult.FromTargets(
            TargetPipeline.Finalize(fusedTargets).ToList());
    }

    private List<MatchInfo> ComputeMatches(
        IReadOnlyList<VisualTarget> uiaTargets,
        IReadOnlyList<VisualTarget> cvTargets)
    {
        var result = new List<MatchInfo>();

        foreach (VisualTarget uia in uiaTargets)
        {
            foreach (VisualTarget cv in cvTargets)
            {
                double iou = TargetSimilarity.IoU(uia.Bounds, cv.Bounds);
                double centerDistance = TargetSimilarity.NormalizedCenterDistance(
                    uia.Bounds,
                    cv.Bounds);
                double containment = TargetSimilarity.IntersectionOverSmaller(
                    uia.Bounds,
                    cv.Bounds);
                double text = TargetSimilarity.TextSimilarity(uia.Text, cv.Text);
                double type = TargetSimilarity.TypeCompatibility(uia.Type, cv.Type);
                double shape = TargetSimilarity.ShapeSimilarity(uia.Bounds, cv.Bounds);
                double grouping = TargetSimilarity.VisualGrouping(uia.Bounds, cv.Bounds);

                // A nearby but disjoint control is not a match. This guard is
                // what keeps two adjacent buttons from fusing just because they
                // have the same size, type and alignment.
                if (iou <= 0 && containment < 0.50 && centerDistance > 0.60)
                {
                    continue;
                }

                double weightSum =
                    NonNegative(IoUWeight) +
                    NonNegative(CenterDistWeight) +
                    NonNegative(TextWeight) +
                    NonNegative(TypeWeight) +
                    NonNegative(ContainmentWeight) +
                    NonNegative(ShapeWeight) +
                    NonNegative(SpatialWeight);

                if (weightSum <= 0)
                {
                    continue;
                }

                double totalScore =
                    (NonNegative(IoUWeight) * iou) +
                    (NonNegative(CenterDistWeight) * (1.0 - centerDistance)) +
                    (NonNegative(TextWeight) * text) +
                    (NonNegative(TypeWeight) * type) +
                    (NonNegative(ContainmentWeight) * containment) +
                    (NonNegative(ShapeWeight) * shape) +
                    (NonNegative(SpatialWeight) * grouping);

                result.Add(new MatchInfo
                {
                    UiaTarget = uia,
                    CvTarget = cv,
                    IoU = iou,
                    CenterDistance = centerDistance,
                    TextSimilarity = text,
                    TypeCompatibility = type,
                    Containment = containment,
                    ShapeSimilarity = shape,
                    VisualGrouping = grouping,
                    TotalScore = Math.Clamp(totalScore / weightSum, 0, 1),
                });
            }
        }

        return result;
    }

    private List<MatchInfo> ResolveMatches(
        List<MatchInfo> matches,
        int uiaCount,
        int cvCount)
    {
        var candidates = matches
            .Where(match => match.TotalScore >= MatchThreshold)
            .OrderByDescending(match => match.TotalScore)
            .ThenBy(match => match.UiaTarget.Id, StringComparer.Ordinal)
            .ThenBy(match => match.CvTarget.Id, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            return [];
        }

        var uiaIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var cvIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (MatchInfo match in candidates)
        {
            uiaIndex.TryAdd(match.UiaTarget.Id, uiaIndex.Count);
            cvIndex.TryAdd(match.CvTarget.Id, cvIndex.Count);
        }

        var parent = Enumerable.Range(0, uiaIndex.Count + cvIndex.Count).ToArray();

        foreach (MatchInfo match in candidates)
        {
            Union(
                parent,
                uiaIndex[match.UiaTarget.Id],
                uiaIndex.Count + cvIndex[match.CvTarget.Id]);
        }

        var components = new Dictionary<int, List<MatchInfo>>();
        foreach (MatchInfo match in candidates)
        {
            int root = Find(parent, uiaIndex[match.UiaTarget.Id]);
            if (!components.TryGetValue(root, out List<MatchInfo>? component))
            {
                component = [];
                components[root] = component;
            }

            component.Add(match);
        }

        var accepted = new List<MatchInfo>(candidates.Count);

        foreach (List<MatchInfo> component in components.Values
                     .OrderBy(group => group.Min(match => match.UiaTarget.Id), StringComparer.Ordinal))
        {
            accepted.AddRange(ResolveComponent(component));
        }

        return accepted;
    }

    private List<MatchInfo> ResolveComponent(List<MatchInfo> component)
    {
        string[] uiaIds = component
            .Select(match => match.UiaTarget.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        string[] cvIds = component
            .Select(match => match.CvTarget.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var uiaPosition = uiaIds
            .Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        var cvPosition = cvIds
            .Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        var byPair = component.ToDictionary(
            match => (UiaId: match.UiaTarget.Id, CvId: match.CvTarget.Id));

        long cells = (long)uiaIds.Length * (cvIds.Length + uiaIds.Length);
        if (cells > MaxExactAssignmentCells)
        {
            return ResolveLargeComponent(component, uiaIds, cvIds);
        }

        var scores = new double[uiaIds.Length, cvIds.Length];
        foreach (MatchInfo match in component)
        {
            scores[
                uiaPosition[match.UiaTarget.Id],
                cvPosition[match.CvTarget.Id]] = match.TotalScore;
        }

        int[] assignment = TargetAssignment.Assign(scores, allowUnmatched: true);
        var accepted = new List<MatchInfo>(Math.Min(uiaIds.Length, cvIds.Length));

        for (int row = 0; row < assignment.Length; row++)
        {
            int column = assignment[row];
            if (column < 0)
            {
                continue;
            }

            if (byPair.TryGetValue((uiaIds[row], cvIds[column]), out MatchInfo? match) &&
                match.TotalScore >= MatchThreshold)
            {
                accepted.Add(match);
            }
        }

        return accepted;
    }

    /// <summary>
    /// Scalability fallback for a pathologically dense component. It starts
    /// from the best available edge and then performs pairwise swap improvement,
    /// so it is still a global refinement rather than a single greedy pass.
    /// </summary>
    private List<MatchInfo> ResolveLargeComponent(
        List<MatchInfo> component,
        IReadOnlyList<string> uiaIds,
        IReadOnlyList<string> cvIds)
    {
        var byPair = component.ToDictionary(
            match => (UiaId: match.UiaTarget.Id, CvId: match.CvTarget.Id));
        var usedUia = new HashSet<string>(StringComparer.Ordinal);
        var usedCv = new HashSet<string>(StringComparer.Ordinal);
        var accepted = new List<MatchInfo>(Math.Min(uiaIds.Count, cvIds.Count));

        foreach (MatchInfo match in component
                     .OrderByDescending(match => match.TotalScore)
                     .ThenBy(match => match.UiaTarget.Id, StringComparer.Ordinal)
                     .ThenBy(match => match.CvTarget.Id, StringComparer.Ordinal))
        {
            if (usedUia.Contains(match.UiaTarget.Id) ||
                usedCv.Contains(match.CvTarget.Id))
            {
                continue;
            }

            usedUia.Add(match.UiaTarget.Id);
            usedCv.Add(match.CvTarget.Id);
            accepted.Add(match);
        }

        bool improved;
        do
        {
            improved = false;

            for (int i = 0; i < accepted.Count && !improved; i++)
            {
                for (int j = i + 1; j < accepted.Count && !improved; j++)
                {
                    MatchInfo first = accepted[i];
                    MatchInfo second = accepted[j];

                    if (!byPair.TryGetValue(
                            (first.UiaTarget.Id, second.CvTarget.Id),
                            out MatchInfo? crossFirst) ||
                        !byPair.TryGetValue(
                            (second.UiaTarget.Id, first.CvTarget.Id),
                            out MatchInfo? crossSecond))
                    {
                        continue;
                    }

                    double current = first.TotalScore + second.TotalScore;
                    double swapped = crossFirst.TotalScore + crossSecond.TotalScore;

                    if (swapped > current + 1e-9)
                    {
                        accepted[i] = crossFirst;
                        accepted[j] = crossSecond;
                        improved = true;
                    }
                }
            }
        }
        while (improved);

        return accepted
            .Where(match => match.TotalScore >= MatchThreshold)
            .ToList();
    }

    /// <summary>
    /// Merges a matched UIA/CV pair. UIA remains authoritative for interaction
    /// semantics and metadata; CV contributes refined pixels when it found the
    /// real control inside a UIA container, plus visual metadata in all cases.
    /// </summary>
    private static VisualTarget MergeTargets(
        VisualTarget uia,
        VisualTarget cv)
    {
        TargetBounds finalBounds = uia.Bounds;
        Point finalClickPoint = uia.ClickPoint;
        TargetType finalType = uia.Type;
        string finalText = uia.Text;
        TargetGeometry finalGeometry = uia.Geometry;
        double finalClassification = uia.ClassificationConfidence;

        long uiaArea = (long)uia.Bounds.Width * uia.Bounds.Height;
        long cvArea = (long)cv.Bounds.Width * cv.Bounds.Height;
        bool uiaContainer = TargetGeometrySelection.IsContainerLike(uia.Type);
        bool cvControl = TargetGeometrySelection.IsInteractiveLike(cv.Type);
        bool cvInsideUia = uia.Bounds.Contains(cv.Bounds);
        bool preferCvBounds =
            cvInsideUia &&
            uiaContainer &&
            uiaArea >= Math.Max(1, cvArea) * 2;

        if (preferCvBounds)
        {
            finalBounds = cv.Bounds;
            finalClickPoint = cv.ClickPoint;
            finalType = cv.Type;
            finalText = string.IsNullOrWhiteSpace(uia.Text) ? cv.Text : uia.Text;
            finalGeometry = cv.Geometry;
            finalClassification = cv.ClassificationConfidence;
        }
        else
        {
            if (uia.Bounds.Contains(cv.ClickPoint))
            {
                if (uiaContainer ||
                    cv.Geometry.Confidence > uia.Geometry.Confidence + 0.10)
                {
                    finalClickPoint = cv.ClickPoint;
                }
            }

            if (cv.Geometry.HasRoundedCorners && !finalGeometry.HasRoundedCorners)
            {
                finalGeometry = finalGeometry with
                {
                    HasRoundedCorners = true,
                    Issues = finalGeometry.Issues | GeometryIssue.RoundedCorners
                };
            }

            if (finalType == cv.Type)
            {
                finalClassification = Math.Max(
                    uia.ClassificationConfidence,
                    cv.ClassificationConfidence);
            }
        }

        if (!finalBounds.Contains(finalClickPoint))
        {
            finalClickPoint = HintCoordinates.ClampToBounds(
                finalClickPoint,
                finalBounds);
        }

        var mergedMetadata = new TargetMetadata
        {
            Uia = uia.Metadata.Uia,
            Cv = new CvTargetMetadata(
                OcrText: cv.Text,
                OcrConfidence: cv.Metadata.Cv?.OcrConfidence ??
                               (string.IsNullOrWhiteSpace(cv.Text)
                                   ? null
                                   : cv.DetectionConfidence),
                FrameId: cv.Metadata.Cv?.FrameId ?? 0,
                Detector: cv.Metadata.Cv?.Detector ?? "cv",
                SourceBounds: cv.Metadata.Cv?.SourceBounds),
        };

        double bestDetection = Math.Max(
            uia.DetectionConfidence,
            cv.DetectionConfidence);
        double bestInteraction = Math.Max(
            uia.InteractionConfidence,
            cv.InteractionConfidence);

        return new VisualTarget(
            id: $"{uia.Id}+{cv.Id}",
            // Fusion always reports both source families. CV metadata retains
            // whether this particular detection came from OCR, contours or both.
            source: TargetSource.Uia | TargetSource.Cv,
            bounds: finalBounds,
            clickPoint: finalClickPoint,
            type: finalType,
            text: finalText,
            detectionConfidence: bestDetection,
            interactionConfidence: bestInteraction,
            isEnabled: uia.IsEnabled && cv.IsEnabled,
            isVisible: uia.IsVisible && cv.IsVisible,
            metadata: mergedMetadata,
            geometry: finalGeometry,
            classificationConfidence: finalClassification);
    }

    private static void Union(int[] parent, int left, int right)
    {
        int leftRoot = Find(parent, left);
        int rightRoot = Find(parent, right);

        if (leftRoot == rightRoot)
        {
            return;
        }

        if (leftRoot < rightRoot)
        {
            parent[rightRoot] = leftRoot;
        }
        else
        {
            parent[leftRoot] = rightRoot;
        }
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

    private static double NonNegative(double value) =>
        double.IsFinite(value) && value > 0 ? value : 0;

    private sealed class MatchInfo
    {
        public VisualTarget UiaTarget { get; set; } = default!;
        public VisualTarget CvTarget { get; set; } = default!;
        public double IoU { get; set; }
        public double CenterDistance { get; set; }
        public double TextSimilarity { get; set; }
        public double TypeCompatibility { get; set; }
        public double Containment { get; set; }
        public double ShapeSimilarity { get; set; }
        public double VisualGrouping { get; set; }
        public double TotalScore { get; set; }
    }
}
