using System.Collections.Generic;
using System.Linq;
using KeyboardMouse.Core;

/// <summary>
/// Fuses UIA and CV visual target collections into a single harmonized
/// collection.  The strategy is one-to-one matching with deterministic
/// resolution of conflicts.
/// </summary>
public sealed class TargetFusionEngine
{
    /// <summary>Weight for Intersection-over-Union (IoU) of bounds.</summary>
    public double IoUWeight { get; set; } = 0.30;

    /// <summary>Weight for normalized center-to-center distance.</summary>
    public double CenterDistWeight { get; set; } = 0.20;

    /// <summary>Weight for text equality/signals.</summary>
    public double TextWeight { get; set; } = 0.15;

    /// <summary>Weight for target-type compatibility.</summary>
    public double TypeWeight { get; set; } = 0.15;

    /// <summary>Weight for containment relationship.</summary>
    public double ContainmentWeight { get; set; } = 0.10;

    /// <summary>Weight for spatial relationship bonus.</summary>
    public double SpatialWeight { get; set; } = 0.10;

    /// <summary>Fusion confidence threshold: a match is accepted only when the
    /// total score meets or exceeds this value.</summary>
    public double MatchThreshold { get; set; } = 0.55;

    /// <summary>Minimum CV detection confidence for a CV-only target to be kept.</summary>
    public double MinCvConfidence { get; set; } = 0.40;

    /// <summary>Minimum UIA detection confidence for a UIA-only target to be kept.</summary>
    public double MinUiaConfidence { get; set; } = 0.50;

    /// <summary>Construct a <see cref="TargetFusionEngine"/> with default weights.</summary>
    public TargetFusionEngine() { }

    /// <summary>
    /// Fusion weights that prioritize geometric overlap.</summary>
    public static TargetFusionEngine GeometricPrior =>
        new TargetFusionEngine
        {
            IoUWeight = 0.40,
            CenterDistWeight = 0.20,
            TextWeight = 0.10,
            TypeWeight = 0.10,
            ContainmentWeight = 0.10,
            SpatialWeight = 0.10,
            MatchThreshold = 0.50,
        };

    /// <summary>
    /// Fusion weights that prioritize text/OCR association.</summary>
    public static TargetFusionEngine TextPrior =>
        new TargetFusionEngine
        {
            IoUWeight = 0.20,
            CenterDistWeight = 0.15,
            TextWeight = 0.30,
            TypeWeight = 0.15,
            ContainmentWeight = 0.10,
            SpatialWeight = 0.10,
            MatchThreshold = 0.60,
        };

    /// <summary>
    /// Fusion weights that prioritize UIA compatibility.</summary>
    public static TargetFusionEngine UiaPrior =>
        new TargetFusionEngine
        {
            IoUWeight = 0.25,
            CenterDistWeight = 0.20,
            TextWeight = 0.10,
            TypeWeight = 0.25,
            ContainmentWeight = 0.10,
            SpatialWeight = 0.10,
            MatchThreshold = 0.50,
        };

    /// <summary>
    /// Fuses UIA and CV visual target collections into a single harmonized
    /// <see cref="HintScanResult"/>.  The strategy is one-to-one matching with
    /// deterministic resolution of conflicts.
    /// </summary>
    /// <param name="uiaTargets">Visual targets from the UI Automation scanner.</param>
    /// <param name="cvTargets">Visual targets from the computer-vision scanner.</param>
    /// <returns>A <see cref="HintScanResult"/> containing the fused targets.</returns>
    public HintScanResult Fuse(
        IReadOnlyList<VisualTarget> uiaTargets,
        IReadOnlyList<VisualTarget> cvTargets)
    {
        // Quick return if both are empty.
        if (uiaTargets.Count == 0 && cvTargets.Count == 0)
        {
            return HintScanResult.FromTargets(Enumerable.Empty<VisualTarget>().ToList());
        }

        // Phase 1: compute all pairwise match scores.
        var matches = ComputeMatches(uiaTargets, cvTargets);

        // Phase 2: resolve one-to-one matches greedily by highest score,
        //        breaking ties by lower ID, then by source priority (Uia > CV).
        var matchedUia = new HashSet<string>();
        var matchedCv = new HashSet<string>();
        var fusedTargets = new List<VisualTarget>();

        // Sort matches by descending score, then by source priority.
        var sorted = matches
            .OrderByDescending(m => m.TotalScore)
            .ThenBy(m => m.UiaId)
            .ThenBy(m => m.CvId);

        foreach (var m in sorted)
        {
            // Skip if either side already matched.
            if (matchedUia.Contains(m.UiaId) || matchedCv.Contains(m.CvId))
                continue;

            // Accept if score meets threshold.
            if (m.TotalScore < MatchThreshold)
                continue;

            // Accept the match and mark both sides as used.
            matchedUia.Add(m.UiaId);
            matchedCv.Add(m.CvId);

            var fused = MergeTargets(m.UiaTarget, m.CvTarget, m);
            fusedTargets.Add(fused);
        }

        // Phase 3: add unmatched UIA targets that are valid.
        foreach (var t in uiaTargets)
        {
            if (matchedUia.Contains(t.Id)) continue;
            if (t.IsActionable)
                fusedTargets.Add(t);
        }

        // Phase 4: add unmatched CV targets that meet minimum confidence.
        foreach (var t in cvTargets)
        {
            if (matchedCv.Contains(t.Id)) continue;
            if (t.DetectionConfidence >= MinCvConfidence)
                fusedTargets.Add(t);
        }

        // Phase 5: deduplicate fused targets by visible center.
        var deduped = DeduplicateByCenter(fusedTargets);

        return HintScanResult.FromTargets(deduped.ToList());
    }

    /// <summary>
    /// Computes match scores for every UIA–CV pair.
    /// </summary>
    private List<MatchInfo> ComputeMatches(
        IReadOnlyList<VisualTarget> uiaTargets,
        IReadOnlyList<VisualTarget> cvTargets)
    {
        var result = new List<MatchInfo>();

        foreach (var uia in uiaTargets)
        {
            foreach (var cv in cvTargets)
            {
                var iou = ComputeIoU(uia.Bounds, cv.Bounds);
                var centerDist = ComputeCenterDistance(uia.Bounds, cv.Bounds);
                var textSim = ComputeTextSimilarity(uia.Text, cv.Text);
                var typeCompat = ComputeTypeCompatibility(uia.Type, cv.Type);
                var containment = ComputeContainment(uia.Bounds, cv.Bounds);
                var spatial = ComputeSpatialRelationship(uia.Bounds, cv.Bounds);

                var totalScore =
                    IoUWeight * iou +
                    CenterDistWeight * centerDist +
                    TextWeight * textSim +
                    TypeWeight * typeCompat +
                    ContainmentWeight * containment +
                    SpatialWeight * spatial;

                result.Add(new MatchInfo
                {
                    UiaTarget = uia,
                    CvTarget = cv,
                    IoU = iou,
                    CenterDist = centerDist,
                    TextSimilarity = textSim,
                    TypeCompatibility = typeCompat,
                    Containment = containment,
                    SpatialRelationship = spatial,
                    TotalScore = totalScore,
                    UiaId = uia.Id,
                    CvId = cv.Id,
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Computes Intersection-over-Union between two <see cref="TargetBounds"/>.
    /// </summary>
    private static double ComputeIoU(TargetBounds a, TargetBounds b)
    {
        int left = System.Math.Max(a.Left, b.Left);
        int top = System.Math.Max(a.Top, b.Top);
        int right = System.Math.Min(a.Right, b.Right);
        int bottom = System.Math.Min(a.Bottom, b.Bottom);

        int intersectionWidth = System.Math.Max(0, right - left);
        int intersectionHeight = System.Math.Max(0, bottom - top);
        int intersectionArea = intersectionWidth * intersectionHeight;

        if (intersectionArea == 0) return 0.0;

        int areaA = a.Width * a.Height;
        int areaB = b.Width * b.Height;
        int unionArea = areaA + areaB - intersectionArea;

        return (double)intersectionArea / unionArea;
    }

    /// <summary>
    /// Computes a normalized center-to-center distance (0 = same center, 1 =
    /// far apart roughly at target-diagonal distance).
    /// </summary>
    private static double ComputeCenterDistance(TargetBounds a, TargetBounds b)
    {
        int ac = a.Center.X, bc = b.Center.X;
        int acY = a.Center.Y, bcY = b.Center.Y;
        double dx = ac - bc;
        double dy = acY - bcY;
        double euclid = System.Math.Sqrt(dx * dx + dy * dy);

        // Normalize by the larger target's diagonal.
        double diagA = System.Math.Sqrt(a.Width * a.Width + a.Height * a.Height);
        double diagB = System.Math.Sqrt(b.Width * b.Width + b.Height * b.Height);
        double normDiag = System.Math.Max(diagA, diagB);

        if (normDiag == 0) return 1.0;
        return System.Math.Min(1.0, euclid / normDiag);
    }

    /// <summary>
    /// Computes text similarity in [0, 1].  Exact match = 1, no text = 1 (neutral),
    /// completely different = 0.
    /// </summary>
    private static double ComputeTextSimilarity(string? a, string? b)
    {
        if (a == null && b == null) return 1.0;
        if (a == null || b == null) return 0.5;
        if (a.Equals(b, System.StringComparison.OrdinalIgnoreCase)) return 1.0;
        // Simple substring presence bonus.
        if (a.Contains(b) || b.Contains(a)) return 0.7;
        return 0.0;
    }

    /// <summary>
    /// Computes target-type compatibility in [0, 1].  Same type = 1, compatible
    /// families (both button-like) = 0.7, unrelated = 0.
    /// </summary>
    private static double ComputeTypeCompatibility(TargetType a, TargetType b)
    {
        if (a == b) return 1.0;
        // Button-like family.
        if ((a == TargetType.Button || a == TargetType.ListItem) &&
            (b == TargetType.Button || b == TargetType.ListItem))
            return 0.7;
        // Checkbox/radio family.
        if ((a == TargetType.CheckBox || a == TargetType.RadioButton) &&
            (b == TargetType.CheckBox || b == TargetType.RadioButton))
            return 0.7;
        // Edit/text field family.
        if ((a == TargetType.Edit || a == TargetType.Text) &&
            (b == TargetType.Edit || b == TargetType.Text))
            return 0.7;
        // Unknown vs anything is moderately compatible.
        if (a == TargetType.Unknown || b == TargetType.Unknown) return 0.5;
        return 0.0;
    }

    /// <summary>
    /// Computes containment: 1 if A fully contains B, 0.5 if B fully contains A,
    /// 0.2 if partial overlap, 0 if no overlap.
    /// </summary>
    private static double ComputeContainment(TargetBounds a, TargetBounds b)
    {
        if (!a.IsValid || !b.IsValid) return 0.0;

        if (a.Contains(b)) return 1.0;
        if (b.Contains(a)) return 0.5;

        // Partial overlap.
        if (a.IntersectsWith(b)) return 0.2;
        return 0.0;
    }

    /// <summary>
    /// Computes a spatial relationship bonus in [0, 1].  Targets that are
    /// aligned (same left edge, same top edge, etc.) get a small boost; targets
    /// that are far apart in both axes get a small penalty.  The base contribution
    /// is 0.5 and the weight modulates it.
    /// </summary>
    private static double ComputeSpatialRelationship(TargetBounds a, TargetBounds b)
    {
        double score = 0.5;

        // Same left edge.
        if (a.Left == b.Left) score += 0.15;
        // Same top edge.
        if (a.Top == b.Top) score += 0.15;
        // One is directly right of the other (no vertical overlap).
        if (a.Right <= b.Left || b.Right <= a.Left) score -= 0.10;
        // One is directly below the other (no horizontal overlap).
        if (a.Bottom <= b.Top || b.Bottom <= a.Top) score -= 0.10;

        return System.Math.Max(0.0, System.Math.Min(1.0, score));
    }

    /// <summary>
    /// Merges a UIA and CV target that have been deemed a match.  The resulting
    /// target preserves UIA interaction metadata, CV visual metadata, and sets
    /// the source to <see cref="TargetSource.Hybrid"/>.
    /// </summary>
    private VisualTarget MergeTargets(
        VisualTarget uia,
        VisualTarget cv,
        MatchInfo match)
    {
        // Decide which bounds/click-point to preserve.
        // If the IoU is high enough, use the larger/preferred bounds; otherwise
        // use the UIA bounds as the primary (UIA is authoritative for geometry).
        TargetBounds finalBounds;
        Point finalClickPoint;
        TargetType finalType;
        string finalText;
        TargetSource finalSource = TargetSource.Uia | TargetSource.Cv;
        double fusionConfidence = match.TotalScore;

        if (match.IoU >= 0.60)
        {
            // High overlap: prefer UIA bounds (authoritative) but keep CV text.
            finalBounds = uia.Bounds;
            finalClickPoint = uia.ClickPoint;
            finalType = uia.Type;
            finalText = uia.Text;
        }
        else
        {
            // Moderate overlap: UIA is primary, CV contributes confidence.
            finalBounds = uia.Bounds;
            finalClickPoint = uia.ClickPoint;
            finalType = uia.Type;
            finalText = uia.Text;
        }

        // Build merged metadata.
        var mergedMetadata = new TargetMetadata
        {
            Uia = new UiaTargetMetadata
            {
                AutomationId = uia.Metadata.Uia?.AutomationId,
                RuntimeId = uia.Metadata.Uia?.RuntimeId,
                ControlType = uia.Metadata.Uia?.ControlType,
                ClassName = uia.Metadata.Uia?.ClassName,
                SupportsInvoke = uia.Metadata.Uia?.SupportsInvoke ?? false,
                SourceBounds = uia.Metadata.Uia?.SourceBounds,
                IsKeyboardFocusable = uia.Metadata.Uia?.IsKeyboardFocusable ?? false,
                IsOffscreen = uia.Metadata.Uia?.IsOffscreen ?? false,
                SupportedPatterns = uia.Metadata.Uia?.SupportedPatterns ?? UiaPatternFlags.None,
            },
            Cv = new CvTargetMetadata
            {
                OcrText = cv.Text,
                OcrConfidence = cv.DetectionConfidence,
                FrameId = cv.Metadata.Cv?.FrameId ?? 0,
                Detector = "cv",
                SourceBounds = cv.Metadata.Cv?.SourceBounds,
            },
        };

        // Determine the "best" detection confidence: use the higher of the two,
        // adjusted by fusion confidence.
        double bestDetection = System.Math.Max(uia.DetectionConfidence, cv.DetectionConfidence);
        double bestInteraction = System.Math.Max(uia.InteractionConfidence, cv.InteractionConfidence);

        return new VisualTarget(
            id: $"{uia.Id}-fused-{cv.Id}",
            source: finalSource,
            bounds: finalBounds,
            clickPoint: finalClickPoint,
            type: finalType,
            text: finalText,
            detectionConfidence: bestDetection,
            interactionConfidence: bestInteraction,
            isEnabled: uia.IsEnabled && cv.IsEnabled,
            isVisible: uia.IsVisible && cv.IsVisible,
            metadata: mergedMetadata);
    }

    /// <summary>
    /// Deduplicates a list of <see cref="VisualTarget"/> by visible center,
    /// keeping the target with the higher detection confidence when centers
    /// coincide.
    /// </summary>
    private static List<VisualTarget> DeduplicateByCenter(List<VisualTarget> targets)
    {
        var centers = new HashSet<(int x, int y)>();
        var result = new List<VisualTarget>();

        foreach (var t in targets.OrderByDescending(t => t.DetectionConfidence)
                                 .ThenBy(t => t.Id))
        {
            int cx = t.Bounds.Center.X;
            int cy = t.Bounds.Center.Y;
            if (centers.Contains((cx, cy)))
                continue;
            centers.Add((cx, cy));
            result.Add(t);
        }

        return result;
    }

    /// <summary>
    /// Holds the computed match information for a single UIA–CV pair.
    /// </summary>
    private sealed class MatchInfo
    {
        public VisualTarget UiaTarget { get; set; } = default!;
        public VisualTarget CvTarget { get; set; } = default!;
        public double IoU { get; set; }
        public double CenterDist { get; set; }
        public double TextSimilarity { get; set; }
        public double TypeCompatibility { get; set; }
        public double Containment { get; set; }
        public double SpatialRelationship { get; set; }
        public double TotalScore { get; set; }
        public string UiaId { get; set; } = string.Empty;
        public string CvId { get; set; } = string.Empty;
    }
}