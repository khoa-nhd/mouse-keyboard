using KeyboardMouse.Core;

/// <summary>
/// Metrics calculator for hint scan evaluation.
/// Computes precision, recall, F1, IoU, and click-point validity.
/// Reuses existing TargetSimilarity infrastructure.
/// </summary>
public static class ScanMetrics
{
    /// <summary>
    /// Intersection over Union between two TargetBounds, using the existing
    /// TargetSimilarity.Iu infrastructure.
    /// </summary>
    public static double IoU(TargetBounds a, TargetBounds b) =>
        TargetSimilarity.IoU(a, b);

    /// <summary>
    /// Click-point validity: percentage of targets whose click point falls
    /// inside their own bounds.
    /// </summary>
    public static double ClickPointValidity(IReadOnlyList<VisualTarget> targets)
    {
        if (targets.Count == 0) return 0.0;
        int valid = targets.Count(t => t.Bounds.Contains(t.ClickPoint));
        return (double)valid / targets.Count;
    }

    /// <summary>
    /// Precision at k: of the top k detected targets, how many match a
    /// ground-truth target with IoU >= threshold.
    /// </summary>
    /// <param name="detected">Detected visual targets (ordered by confidence)</param>
    /// <param name="groundTruth">Ground truth TargetBounds (ordered by importance)</param>
    /// <param name="iouThreshold">IoU threshold for a match (default 0.5)</param>
    /// <param name="k">Number of top detected targets to consider (int.Max = all)</param>
    public static (double Precision, double Recall, double F1)
        PrecisionRecallF1(
            IReadOnlyList<VisualTarget> detected,
            IReadOnlyList<TargetBounds> groundTruth,
            double iouThreshold = 0.5,
            int k = int.MaxValue)
    {
        k = Math.Min(k, detected.Count);
        var matched = new bool[groundTruth.Count];
        int truePositives = 0;

        for (int i = 0; i < k; i++)
        {
            var det = detected[i];
            double bestIou = 0;
            int bestIdx = -1;

            for (int j = 0; j < groundTruth.Count; j++)
            {
                if (matched[j]) continue;
                double iou = IoU(det.Bounds, groundTruth[j]);
                if (iou > bestIou)
                {
                    bestIou = iou;
                    bestIdx = j;
                }
            }

            if (bestIdx >= 0 && bestIou >= iouThreshold)
            {
                matched[bestIdx] = true;
                truePositives++;
            }
        }

        int falsePositives = k - truePositives;
        int falseNegatives = groundTruth.Count - matched.Count(d => d);

        double precision = k > 0 ? (double)truePositives / k : 0.0;
        double recall = groundTruth.Count > 0 ? (double)truePositives / groundTruth.Count : 1.0;
        double f1 = (precision + recall > 0) ? 2 * precision * recall / (precision + recall) : 0.0;

        return (precision, recall, f1);
    }

    /// <summary>
    /// Classification: whether a visual target is interactive (has a meaningful
    /// click point and a type suggesting interaction).
    /// </summary>
    public static bool IsInteractive(this VisualTarget target) =>
        target.Type != TargetType.Unknown &&
        target.Type != TargetType.Other;
}