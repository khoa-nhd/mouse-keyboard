using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyboardMouse.Core;

/// <summary>
/// Active learning prioritization for human review.
/// Identifies candidates that are low confidence, disagree between detectors,
/// near decision thresholds, frequently misclassified, or common false positives.
/// </summary>
public static class ActiveLearningPrioritizer
{
    /// <summary>
    /// Identify candidates that should be prioritized for human review.
    /// </summary>
    public static List<ActiveLearningCandidate> Prioritize(
        List<CandidateResult> candidates,
        List<CandidateResult>? cvCandidates = null,
        List<CandidateResult>? mlCandidates = null,
        Dictionary<string, int>? misclassificationCounts = null)
    {
        var prioritized = new List<ActiveLearningCandidate>();

        foreach (var candidate in candidates)
        {
            var reasons = new List<string>();
            double priority = 0.0;

            // Low confidence
            if (candidate.Confidence < 0.6)
            {
                reasons.Add("Low confidence");
                priority += 0.3;
            }

            // Disagreement between detectors
            if (cvCandidates != null && mlCandidates != null)
            {
                var cvMatch = cvCandidates.Any(c =>
                    Math.Abs(c.Bounds.X - candidate.Bounds.X) < 10 &&
                    Math.Abs(c.Bounds.Y - candidate.Bounds.Y) < 10);

                var mlMatch = mlCandidates.Any(c =>
                    Math.Abs(c.Bounds.X - candidate.Bounds.X) < 10 &&
                    Math.Abs(c.Bounds.Y - candidate.Bounds.Y) < 10);

                if (cvMatch != mlMatch)
                {
                    reasons.Add("Detector disagreement");
                    priority += 0.4;
                }
            }

            // Near decision thresholds (0.35-0.45 or 0.55-0.65)
            if ((candidate.Confidence >= 0.35 && candidate.Confidence <= 0.45) ||
                (candidate.Confidence >= 0.55 && candidate.Confidence <= 0.65))
            {
                reasons.Add("Near decision threshold");
                priority += 0.2;
            }

            // Frequently misclassified
            if (misclassificationCounts != null &&
                misclassificationCounts.TryGetValue(candidate.Detector, out var count) &&
                count > 3)
            {
                reasons.Add("Frequently misclassified");
                priority += 0.25;
            }

            // Common sources of false positives
            if (candidate.Type == TargetType.Other && candidate.Confidence > 0.7)
            {
                reasons.Add("High-confidence Other type (potential false positive)");
                priority += 0.15;
            }

            if (reasons.Count > 0)
            {
                prioritized.Add(new ActiveLearningCandidate
                {
                    Candidate = candidate,
                    Reasons = reasons,
                    Priority = Math.Clamp(priority, 0.0, 1.0)
                });
            }
        }

        return prioritized.OrderByDescending(p => p.Priority).ToList();
    }
}

/// <summary>
/// An active learning candidate for human review.
/// </summary>
public class ActiveLearningCandidate
{
    /// <summary>
    /// The candidate to review.
    /// </summary>
    public CandidateResult Candidate { get; set; }

    /// <summary>
    /// Reasons why this candidate was prioritized.
    /// </summary>
    public List<string> Reasons { get; set; } = new();

    /// <summary>
    /// Priority score (higher = more important).
    /// </summary>
    public double Priority { get; set; }
}