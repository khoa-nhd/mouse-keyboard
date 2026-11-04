using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace KeyboardMouse.Core;

/// <summary>
/// Evidence source for clickability reasoning.
/// </summary>
public class ClickabilityEvidence
{
    /// <summary>
    /// Visual shape evidence.
    /// </summary>
    public double ShapeScore { get; set; }

    /// <summary>
    /// Text content evidence (OCR).
    /// </summary>
    public double TextScore { get; set; }

    /// <summary>
    /// Icon appearance evidence.
    /// </summary>
    public double IconScore { get; set; }

    /// <summary>
    /// Local contrast evidence.
    /// </summary>
    public double ContrastScore { get; set; }

    /// <summary>
    /// Hover or selected state evidence.
    /// </summary>
    public double StateScore { get; set; }

    /// <summary>
    /// Spatial grouping evidence.
    /// </summary>
    public double GroupingScore { get; set; }

    /// <summary>
    /// Repeated UI pattern evidence.
    /// </summary>
    public double PatternScore { get; set; }

    /// <summary>
    /// Parent container evidence.
    /// </summary>
    public double ContainerScore { get; set; }

    /// <summary>
    /// Nearby labels evidence.
    /// </summary>
    public double LabelScore { get; set; }

    /// <summary>
    /// UI Automation metadata evidence.
    /// </summary>
    public double UiaScore { get; set; }

    /// <summary>
    /// Previous screen observation evidence.
    /// </summary>
    public double HistoryScore { get; set; }

    /// <summary>
    /// Computes the overall clickability score from individual evidence sources.
    /// Uses weighted combination with calibrated thresholds.
    /// </summary>
    public double ComputeOverallScore()
    {
        // Weighted combination of evidence scores
        double weightedSum =
            ShapeScore * 0.15 +
            TextScore * 0.12 +
            IconScore * 0.10 +
            ContrastScore * 0.08 +
            StateScore * 0.10 +
            GroupingScore * 0.12 +
            PatternScore * 0.10 +
            ContainerScore * 0.08 +
            LabelScore * 0.07 +
            UiaScore * 0.05 +
            HistoryScore * 0.03;

        return Math.Clamp(weightedSum, 0.0, 1.0);
    }

    /// <summary>
    /// Determines the clickability classification based on the overall score.
    /// Uses calibrated thresholds.
    /// </summary>
    public ClickabilityClassification Classify(double overallScore)
    {
        return overallScore switch
        {
            >= 0.8 => ClickabilityClassification.LikelyInteractive,
            >= 0.6 => ClickabilityClassification.ProbablyInteractive,
            >= 0.4 => ClickabilityClassification.Ambiguous,
            >= 0.2 => ClickabilityClassification.ProbablyNonInteractive,
            _ => ClickabilityClassification.NonInteractive
        };
    }
}

/// <summary>
/// Context reasoning engine that analyzes spatial context to determine clickability.
/// Uses local spatial context rather than classifying every candidate independently.
/// </summary>
public static class ContextReasoningEngine
{
    /// <summary>
    /// Analyze spatial context for a single candidate.
    /// </summary>
    public static ClickabilityEvidence AnalyzeContext(
        CandidateResult candidate,
        List<CandidateResult> allCandidates,
        List<Rectangle>? uiaTargets = null)
    {
        var evidence = new ClickabilityEvidence();

        // 1. Visual shape evidence
        evidence.ShapeScore = AnalyzeShape(candidate);

        // 2. Text content evidence
        evidence.TextScore = AnalyzeText(candidate);

        // 3. Icon appearance evidence
        evidence.IconScore = AnalyzeIcon(candidate);

        // 4. Local contrast evidence
        evidence.ContrastScore = AnalyzeContrast(candidate);

        // 5. Spatial grouping evidence
        evidence.GroupingScore = AnalyzeSpatialGrouping(candidate, allCandidates);

        // 6. Repeated UI pattern evidence
        evidence.PatternScore = AnalyzeRepeatedPatterns(candidate, allCandidates);

        // 7. Parent container evidence
        evidence.ContainerScore = AnalyzeContainer(candidate, allCandidates);

        // 8. Nearby labels evidence
        evidence.LabelScore = AnalyzeNearbyLabels(candidate, allCandidates);

        // 9. UI Automation metadata evidence
        evidence.UiaScore = AnalyzeUiaMetadata(candidate, uiaTargets);

        // 10. Hover/selected state evidence (placeholder for future)
        evidence.StateScore = AnalyzeState(candidate);

        // 11. History evidence (placeholder for future)
        evidence.HistoryScore = AnalyzeHistory(candidate);

        return evidence;
    }

    /// <summary>
    /// Analyze visual shape for clickability indicators.
    /// </summary>
    private static double AnalyzeShape(CandidateResult candidate)
    {
        double score = 0.0;

        // Rectangle-like shapes with reasonable aspect ratios are more likely interactive
        double aspect = (double)candidate.Bounds.Width / Math.Max(candidate.Bounds.Height, 1);

        // Button-like aspect ratios (1.5-5.0) suggest interactivity
        if (aspect >= 1.5 && aspect <= 5.0)
            score += 0.3;

        // Square-ish shapes (0.8-1.2) could be checkboxes or icon buttons
        if (aspect >= 0.8 && aspect <= 1.2)
            score += 0.2;

        // Reasonable size (not too small, not too large)
        int area = candidate.Bounds.Width * candidate.Bounds.Height;
        if (area >= 100 && area <= 10000)
            score += 0.2;

        // Candidate type from detector
        score += candidate.Type switch
        {
            TargetType.Button => 0.3,
            TargetType.CheckBox => 0.25,
            TargetType.Icon => 0.2,
            TargetType.Hyperlink => 0.3,
            TargetType.ListItem => 0.15,
            TargetType.MenuItem => 0.2,
            TargetType.TabItem => 0.25,
            TargetType.ComboBox => 0.2,
            _ => 0.0
        };

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Analyze text content for clickability indicators.
    /// </summary>
    private static double AnalyzeText(CandidateResult candidate)
    {
        double score = 0.0;

        // Check if features contain OCR text
        if (candidate.Features.TryGetValue("ocrText", out var textObj) &&
            textObj is string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                // Short text (button labels) more likely interactive
                if (text.Length <= 20)
                    score += 0.3;

                // Common button words
                var lowerText = text.ToLower();
                if (lowerText.Contains("ok") || lowerText.Contains("cancel") ||
                    lowerText.Contains("submit") || lowerText.Contains("apply") ||
                    lowerText.Contains("save") || lowerText.Contains("open") ||
                    lowerText.Contains("close") || lowerText.Contains("yes") ||
                    lowerText.Contains("no"))
                    score += 0.3;

                // Text inside a contour (custom-drawn button)
                if (candidate.Features.ContainsKey("matchedContour"))
                    score += 0.2;
            }
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Analyze icon appearance for clickability indicators.
    /// </summary>
    private static double AnalyzeIcon(CandidateResult candidate)
    {
        double score = 0.0;

        // Check if detector identified this as an icon
        if (candidate.Type == TargetType.Icon)
            score += 0.3;

        // Check if icon-region detector was used
        if (candidate.Detector.Contains("icon"))
            score += 0.2;

        // Small, compact, square-ish regions more likely icon buttons
        double aspect = (double)candidate.Bounds.Width / Math.Max(candidate.Bounds.Height, 1);
        if (aspect >= 0.8 && aspect <= 1.2)
            score += 0.2;

        if (candidate.Bounds.Width >= 16 && candidate.Bounds.Width <= 48)
            score += 0.2;

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Analyze local contrast for clickability indicators.
    /// </summary>
    private static double AnalyzeContrast(CandidateResult candidate)
    {
        // Placeholder - would need access to the actual bitmap
        return 0.5;
    }

    /// <summary>
    /// Helper to get center point of a rectangle.
    /// </summary>
    private static Point GetCenter(Rectangle r)
    {
        return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
    }

    /// <summary>
    /// Helper to check if two candidates are the same (by bounds and detector).
    /// </summary>
    private static bool IsSameCandidate(CandidateResult a, CandidateResult b)
    {
        return a.Bounds == b.Bounds && a.Detector == b.Detector;
    }

    /// <summary>
    /// Analyze spatial grouping for clickability indicators.
    /// Candidates that are part of a group (toolbar, button group) are more likely interactive.
    /// </summary>
    private static double AnalyzeSpatialGrouping(
        CandidateResult candidate,
        List<CandidateResult> allCandidates)
    {
        double score = 0.0;

        // Count nearby candidates
        int nearbyCount = 0;
        foreach (var other in allCandidates)
        {
            if (IsSameCandidate(other, candidate)) continue;

            // Check if candidates are close together
            var candidateCenter = GetCenter(candidate.Bounds);
            var otherCenter = GetCenter(other.Bounds);
            double dx = Math.Abs(candidateCenter.X - otherCenter.X);
            double dy = Math.Abs(candidateCenter.Y - otherCenter.Y);

            // If within 50px horizontally or vertically, they're grouped
            if (dx < 50 || dy < 50)
            {
                nearbyCount++;
            }
        }

        // More nearby candidates suggests a toolbar or button group
        if (nearbyCount >= 3)
            score += 0.4;
        else if (nearbyCount >= 2)
            score += 0.3;
        else if (nearbyCount >= 1)
            score += 0.1;

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Analyze repeated UI patterns for clickability indicators.
    /// Repeated aligned items suggest a toolbar or menu.
    /// </summary>
    private static double AnalyzeRepeatedPatterns(
        CandidateResult candidate,
        List<CandidateResult> allCandidates)
    {
        double score = 0.0;

        // Check for repeated size and spacing
        var similarCandidates = allCandidates
            .Where(c => !IsSameCandidate(c, candidate))
            .Where(c => Math.Abs(c.Bounds.Width - candidate.Bounds.Width) < 10 &&
                        Math.Abs(c.Bounds.Height - candidate.Bounds.Height) < 10)
            .ToList();

        // If multiple candidates have similar dimensions, likely a toolbar
        if (similarCandidates.Count >= 3)
            score += 0.4;
        else if (similarCandidates.Count >= 2)
            score += 0.2;

        // Check for consistent horizontal or vertical alignment
        var horizontallyAligned = allCandidates
            .Where(c => !IsSameCandidate(c, candidate))
            .Where(c => Math.Abs(c.Bounds.Top - candidate.Bounds.Top) < 10)
            .Count();

        if (horizontallyAligned >= 2)
            score += 0.2;

        var verticallyAligned = allCandidates
            .Where(c => !IsSameCandidate(c, candidate))
            .Where(c => Math.Abs(c.Bounds.Left - candidate.Bounds.Left) < 10)
            .Count();

        if (verticallyAligned >= 2)
            score += 0.2;

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Analyze parent container for clickability indicators.
    /// </summary>
    private static double AnalyzeContainer(
        CandidateResult candidate,
        List<CandidateResult> allCandidates)
    {
        double score = 0.0;

        // Check if this candidate is contained within another candidate
        // (suggests it's a child element of a larger control)
        foreach (var other in allCandidates)
        {
            if (IsSameCandidate(other, candidate)) continue;

            if (other.Bounds.Contains(candidate.Bounds))
            {
                // This candidate is inside another - likely a label or icon within a button
                score += 0.3;
                break;
            }
        }

        // Check if this candidate contains other candidates
        // (suggests it's a container, not directly clickable)
        var containedCount = allCandidates
            .Where(c => !IsSameCandidate(c, candidate) && candidate.Bounds.Contains(c.Bounds))
            .Count();

        if (containedCount > 0)
        {
            // Container with children - the container itself may not be directly clickable
            score -= 0.2;
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Analyze nearby labels for clickability indicators.
    /// </summary>
    private static double AnalyzeNearbyLabels(
        CandidateResult candidate,
        List<CandidateResult> allCandidates)
    {
        double score = 0.0;

        // Check for nearby text that might be a label
        foreach (var other in allCandidates)
        {
            if (IsSameCandidate(other, candidate)) continue;

            // Check if there's a text candidate nearby
            if (other.Features.ContainsKey("ocrText"))
            {
                var candidateCenter = GetCenter(candidate.Bounds);
                var otherCenter = GetCenter(other.Bounds);
                double dx = Math.Abs(candidateCenter.X - otherCenter.X);
                double dy = Math.Abs(candidateCenter.Y - otherCenter.Y);

                // Text within 30px suggests a label associated with this control
                if (dx < 30 && dy < 30)
                {
                    score += 0.2;
                    break;
                }
            }
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Analyze UI Automation metadata for clickability indicators.
    /// </summary>
    private static double AnalyzeUiaMetadata(
        CandidateResult candidate,
        List<Rectangle>? uiaTargets)
    {
        if (uiaTargets == null || uiaTargets.Count == 0)
            return 0.0;

        double score = 0.0;

        // Check if this candidate's bounds overlap with any UIA target
        foreach (var uiaTarget in uiaTargets)
        {
            if (uiaTarget.IntersectsWith(candidate.Bounds))
            {
                // UIA confirms this is interactive
                score += 0.5;

                // Check IoU
                var intersection = Rectangle.Intersect(uiaTarget, candidate.Bounds);
                if (intersection.Width > 0 && intersection.Height > 0)
                {
                    double intersectionArea = intersection.Width * intersection.Height;
                    double candidateArea = candidate.Bounds.Width * candidate.Bounds.Height;
                    double unionArea = candidateArea + (uiaTarget.Width * uiaTarget.Height) - intersectionArea;

                    if (unionArea > 0)
                    {
                        double iou = intersectionArea / unionArea;
                        if (iou > 0.5)
                            score += 0.3;
                        else if (iou > 0.25)
                            score += 0.15;
                    }
                }
            }
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Analyze hover/selected state for clickability indicators.
    /// </summary>
    private static double AnalyzeState(CandidateResult candidate)
    {
        // Placeholder - would need access to hover state detection
        return 0.5;
    }

    /// <summary>
    /// Analyze history for clickability indicators.
    /// </summary>
    private static double AnalyzeHistory(CandidateResult candidate)
    {
        // Placeholder - would need access to previous screen observations
        return 0.5;
    }

    /// <summary>
    /// Classify all candidates with clickability reasoning.
    /// </summary>
    public static List<ClickabilityResult> ClassifyAll(
        List<CandidateResult> candidates,
        List<Rectangle>? uiaTargets = null)
    {
        var results = new List<ClickabilityResult>();

        foreach (var candidate in candidates)
        {
            var evidence = AnalyzeContext(candidate, candidates, uiaTargets);
            var overallScore = evidence.ComputeOverallScore();
            var classification = evidence.Classify(overallScore);

            // Determine if this candidate should be shown to the user
            bool shouldShow = classification != ClickabilityClassification.NonInteractive &&
                              classification != ClickabilityClassification.ProbablyNonInteractive;

            // For ambiguous candidates, still show but with lower priority
            if (classification == ClickabilityClassification.Ambiguous)
                shouldShow = true; // Show but mark as ambiguous

            var result = new ClickabilityResult
            {
                Candidate = candidate,
                Classification = classification,
                DetectionConfidence = candidate.Confidence,
                ClickabilityConfidence = overallScore,
                GeometryConfidence = 0.8, // Placeholder - would need actual geometry confidence
                SupportingEvidence = GetSupportingEvidence(evidence),
                RejectionReasons = GetRejectionReasons(evidence, classification),
                ContextFactors = new Dictionary<string, object>
                {
                    ["shapeScore"] = evidence.ShapeScore,
                    ["textScore"] = evidence.TextScore,
                    ["iconScore"] = evidence.IconScore,
                    ["groupingScore"] = evidence.GroupingScore,
                    ["patternScore"] = evidence.PatternScore,
                    ["containerScore"] = evidence.ContainerScore,
                    ["uiaScore"] = evidence.UiaScore
                },
                ShouldShowToUser = shouldShow
            };

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Get supporting evidence strings from the evidence.
    /// </summary>
    private static List<string> GetSupportingEvidence(ClickabilityEvidence evidence)
    {
        var evidenceList = new List<string>();

        if (evidence.ShapeScore > 0.5) evidenceList.Add("Shape suggests interactivity");
        if (evidence.TextScore > 0.5) evidenceList.Add("Text content suggests button label");
        if (evidence.IconScore > 0.5) evidenceList.Add("Icon-like appearance");
        if (evidence.GroupingScore > 0.5) evidenceList.Add("Part of UI group");
        if (evidence.PatternScore > 0.5) evidenceList.Add("Part of repeated pattern");
        if (evidence.ContainerScore > 0.5) evidenceList.Add("Inside interactive container");
        if (evidence.UiaScore > 0.5) evidenceList.Add("Confirmed by UI Automation");

        return evidenceList;
    }

    /// <summary>
    /// Get rejection reasons from the evidence.
    /// </summary>
    private static List<string> GetRejectionReasons(
        ClickabilityEvidence evidence,
        ClickabilityClassification classification)
    {
        var reasons = new List<string>();

        if (classification == ClickabilityClassification.ProbablyNonInteractive ||
            classification == ClickabilityClassification.NonInteractive)
        {
            if (evidence.ShapeScore < 0.3) reasons.Add("Shape does not suggest interactivity");
            if (evidence.TextScore < 0.3) reasons.Add("No button-like text");
            if (evidence.GroupingScore < 0.2) reasons.Add("Not part of UI group");
            if (evidence.UiaScore < 0.1) reasons.Add("Not confirmed by UI Automation");
        }

        if (classification == ClickabilityClassification.Ambiguous)
        {
            reasons.Add("Insufficient evidence for confident classification");
        }

        return reasons;
    }
}