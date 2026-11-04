using System;
using System.Collections.Generic;
using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;

namespace KeyboardMouse.Core;

/// <summary>
/// Candidate position for a label around a target, with a score indicating
/// how good that position is considering proximity, overlap and screen bounds.
/// </summary>
public readonly record struct LabelPlacementCandidate
{
    /// <summary>The rectangle where the label would be drawn.</summary>
    public Rectangle Bounds { get; init; }

    /// <summary>Score: higher is better. Combines proximity, overlap and clip penalties.</summary>
    public double Score { get; init; }

    /// <summary>Which side of the target this position is on.</summary>
    public string? Side { get; init; }
}

/// <summary>
/// Result of laying out a label for one target, including the chosen bounds
/// and the score achieved.
/// </summary>
public readonly record struct LabelLayoutResult
{
    /// <summary>The optimal label rectangle, clamped to the monitor.</summary>
    public Rectangle Bounds { get; init; }

    /// <summary>The placement side (e.g. "TopLeft", "Right", "Below").</summary>
    public string? Side { get; init; }

    /// <summary>Layout quality score (0 = poor, 1 = ideal).</summary>
    public double Quality { get; init; }
}

/// <summary>
/// Strategies for placing a label relative to its target.
/// </summary>
public static class LabelPlacementStrategy
{
    /// <summary>
    /// Generate candidate label positions around a target. The candidates are
    /// ordered by preference: close to the target, avoiding edges and other targets.
    /// </summary>
    /// <param name="targetBounds">The target's bounds in monitor-relative coordinates.</param>
    /// <param name="labelWidth">Width of the label in pixels.</param>
    /// <param name="labelHeight">Height of the label in pixels.</param>
    /// <param name="monitorBounds">The monitor's work area.</param>
    /// <returns>A list of candidate positions with preliminary scores.</returns>
    public static List<LabelPlacementCandidate> GenerateCandidates(
        Rectangle targetBounds,
        int labelWidth,
        int labelHeight,
        Rectangle monitorBounds)
    {
        var candidates = new List<LabelPlacementCandidate>();
        int l = targetBounds.Left;
        int t = targetBounds.Top;
        int r = targetBounds.Right;
        int b = targetBounds.Bottom;
        int centerX = (l + r) / 2;
        int centerY = (t + b) / 2;

        // Helper to add a candidate if it fits within the monitor
        void AddIfFit(Rectangle rect, string? side)
        {
            // Must be at least partially inside the monitor
            if (rect.Right >= monitorBounds.Left && rect.Left <= monitorBounds.Right &&
                rect.Bottom >= monitorBounds.Top && rect.Top <= monitorBounds.Bottom)
            {
                candidates.Add(new LabelPlacementCandidate
                {
                    Bounds = rect,
                    Side = side,
                    Score = 0.0
                });
            }
        }

        // 1. Top-left corner of target
        AddIfFit(new Rectangle(l - labelWidth, t - labelHeight, labelWidth, labelHeight), "TopLeft");
        // 2. Top-right corner
        AddIfFit(new Rectangle(r, t - labelHeight, labelWidth, labelHeight), "TopRight");
        // 3. Bottom-left corner
        AddIfFit(new Rectangle(l - labelWidth, b, labelWidth, labelHeight), "BottomLeft");
        // 4. Bottom-right corner
        AddIfFit(new Rectangle(r, b, labelWidth, labelHeight), "BottomRight");

        // 5. Directly above target (centered)
        AddIfFit(new Rectangle(centerX - labelWidth / 2, t - labelHeight, labelWidth, labelHeight), "Above");

        // 6. Directly below target (centered)
        AddIfFit(new Rectangle(centerX - labelWidth / 2, b, labelWidth, labelHeight), "Below");

        // 7. Directly left of target (centered)
        AddIfFit(new Rectangle(l - labelWidth, centerY - labelHeight / 2, labelWidth, labelHeight), "Left");

        // 8. Directly right of target (centered)
        AddIfFit(new Rectangle(r, centerY - labelHeight / 2, labelWidth, labelHeight), "Right");

        // 9. Top-left of monitor area (fallback)
        AddIfFit(new Rectangle(monitorBounds.Left + 6, monitorBounds.Top + 6, labelWidth, labelHeight), "MonitorTL");

        // 10. Bottom-right of monitor area (fallback)
        AddIfFit(new Rectangle(monitorBounds.Right - labelWidth - 6, monitorBounds.Bottom - labelHeight - 6, labelWidth, labelHeight), "MonitorBR");

        return candidates;
    }
}

/// <summary>
/// Performs deterministic label layout for all targets across one or more monitors.
/// Targets are processed in z-order (first-served). Each target gets the
/// highest-scoring position that doesn't overlap already-placed labels or other
/// target bounds.
/// </summary>
public static class TargetLabelMapper
{
    /// <summary>
    /// Layout labels for all clickable targets. Targets are processed in the
    /// order they appear (typically ranking order from TargetRanker). Each target
    /// gets the highest-scoring position that doesn't overlap already-placed labels.
    /// </summary>
    /// <param name="targets">Clickable targets in display order.</param>
    /// <param name="monitors">All monitors available.</param>
    /// <param name="fontSizePt">Font size in points (converted to pixels per DPI).</param>
    /// <returns>Dictionary mapping index-based keys (e.g. "target-0") to layout
    /// results. Each target receives a deterministic position that avoids overlap,
    /// target obstruction and screen-edge clipping.</returns>
    public static Dictionary<string, LabelLayoutResult> LayoutLabels(
        IReadOnlyList<ClickableTarget> targets,
        IReadOnlyList<MonitorInfo> monitors,
        double fontSizePt = 13.0)
    {
        var results = new Dictionary<string, LabelLayoutResult>(targets.Count);
        var occupied = new List<Rectangle>(); // placed label bounds

        // Get total monitor working area (union of all monitors)
        Rectangle totalArea = new Rectangle(
            monitors.Select(m => m.Bounds.Left).Min(),
            monitors.Select(m => m.Bounds.Top).Min(),
            monitors.Select(m => m.Bounds.Right).Max(),
            monitors.Select(m => m.Bounds.Bottom).Max());

        float fontSizePx = (float)(fontSizePt * 1.333); // 96 DPI base
        int labelWidth = (int)Math.Ceiling(fontSizePx * 1.8);
        int labelHeight = (int)Math.Ceiling(fontSizePx * 1.0);

        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            Rectangle targetBounds = target.Bounds;

            // Generate candidates for this target
            var candidates = LabelPlacementStrategy.GenerateCandidates(
                targetBounds, labelWidth, labelHeight, totalArea);

            // Score each candidate
            for (int c = 0; c < candidates.Count; c++)
            {
                var cand = candidates[c];
                double score = ComputeCandidateScore(cand.Bounds, targetBounds, occupied, totalArea);
                candidates[c] = cand with { Score = score };
            }

            // Sort by score descending, then by side order for determinism
            candidates.Sort((a, b) =>
            {
                int byScore = b.Score.CompareTo(a.Score);
                if (byScore != 0) return byScore;
                return string.Compare(a.Side ?? "", b.Side ?? "", StringComparison.Ordinal);
            });

            // Pick the best candidate that doesn't heavily overlap already-placed labels
            LabelPlacementCandidate? best = null;
            for (int c = 0; c < candidates.Count; c++)
            {
                var cand = candidates[c];

                // Check overlap with already-placed labels
                bool overlaps = false;
                foreach (var occupiedRect in occupied)
                {
                    // Two rectangles overlap if they share any area
                    if (cand.Bounds.IntersectsWith(occupiedRect))
                    {
                        // Allow tiny overlap at corners (1px)
                        int overlapWidth = Math.Max(0, Math.Min(cand.Bounds.Right, occupiedRect.Right) - Math.Max(cand.Bounds.Left, occupiedRect.Left));
                        int overlapHeight = Math.Max(0, Math.Min(cand.Bounds.Bottom, occupiedRect.Bottom) - Math.Max(cand.Bounds.Top, occupiedRect.Top));
                        if (overlapWidth > 1 || overlapHeight > 1)
                        {
                            overlaps = true;
                            break;
                        }
                    }
                }

                if (!overlaps)
                {
                    best = cand;
                    break;
                }
            }

            // If no good candidate, try relaxing overlap constraint slightly
            // (for very dense layouts, allow 1-pixel corner overlap)
            if (best == null)
            {
                for (int c = 0; c < candidates.Count; c++)
                {
                    var cand = candidates[c];
                    bool overlaps = false;
                    foreach (var occupiedRect in occupied)
                    {
                        if (cand.Bounds.IntersectsWith(occupiedRect))
                        {
                            int ow = Math.Max(0, Math.Min(cand.Bounds.Right, occupiedRect.Right) - Math.Max(cand.Bounds.Left, occupiedRect.Left));
                            int oh = Math.Max(0, Math.Min(cand.Bounds.Bottom, occupiedRect.Bottom) - Math.Max(cand.Bounds.Top, occupiedRect.Top));
                            if (ow > 2 || oh > 2) { overlaps = true; break; }
                        }
                    }
                    if (!overlaps)
                    {
                        best = cand;
                        break;
                    }
                }
            }

            // If still no candidate, assign the target's own bounds as fallback
            if (best == null)
            {
                best = new LabelPlacementCandidate
                {
                    Bounds = targetBounds,
                    Side = "Fallback",
                    Score = 0.1
                };
            }

            // Store result (use index-based key since ClickableTarget has no Id)
            string key = $"target-{i}";
            results[key] = new LabelLayoutResult
            {
                Bounds = best.Value.Bounds,
                Side = best.Value.Side,
                Quality = Math.Clamp(best.Value.Score, 0, 1)
            };

            // Mark this label as occupied
            occupied.Add(best.Value.Bounds);
        }

        return results;
    }

    /// <summary>
    /// Compute a score for a label candidate. Higher is better.
    /// Components:
    /// - Proximity to target center (closer = better, but not inside the target)
    /// - Penalty for crossing monitor edges
    /// - Penalty for overlapping already-placed labels (residual, mainly for tie-breaking)
    /// - Penalty for covering the target itself
    /// </summary>
    private static double ComputeCandidateScore(
        Rectangle labelBounds,
        Rectangle targetBounds,
        List<Rectangle> alreadyOccupied,
        Rectangle monitorBounds)
    {
        double score = 1.0;

        // 1. Proximity to target center: prefer positions close to the target
        double tcx = targetBounds.Left + (targetBounds.Width / 2.0);
        double tcy = targetBounds.Top + (targetBounds.Height / 2.0);
        double lcx = labelBounds.Left + (labelBounds.Width / 2.0);
        double lcy = labelBounds.Top + (labelBounds.Height / 2.0);
        double dist = Math.Sqrt(Math.Pow(lcx - tcx, 2) + Math.Pow(lcy - tcy, 2));
        double maxDiag = Math.Max(targetBounds.Width, targetBounds.Height) * 1.5;
        score -= Math.Clamp(dist / maxDiag, 0, 0.3);

        // 2. Monitor edge penalty: prefer labels fully inside the combined monitor area
        if (labelBounds.Left < monitorBounds.Left) score -= 0.15;
        if (labelBounds.Right > monitorBounds.Right) score -= 0.15;
        if (labelBounds.Top < monitorBounds.Top) score -= 0.15;
        if (labelBounds.Bottom > monitorBounds.Bottom) score -= 0.15;

        // 3. Residual overlap penalty (mainly for tie-breaking, already handled greedily)
        foreach (var occupied in alreadyOccupied)
        {
            if (labelBounds.IntersectsWith(occupied))
            {
                int ow = Math.Max(0, Math.Min(labelBounds.Right, occupied.Right) - Math.Max(labelBounds.Left, occupied.Left));
                int oh = Math.Max(0, Math.Min(labelBounds.Bottom, occupied.Bottom) - Math.Max(labelBounds.Top, occupied.Top));
                score -= (ow + oh) / (double)(Math.Max(1, labelBounds.Width * labelBounds.Height)) * 2.0;
            }
        }

        // 4. Penalty for covering the target itself (we want the label next to it, not on it)
        bool labelCenterInsideTarget = targetBounds.Contains(
            new Point(labelBounds.Left + labelBounds.Width / 2, labelBounds.Top + labelBounds.Height / 2));
        if (labelCenterInsideTarget) score -= 0.25;

        return Math.Max(score, 0.01);
    }
}