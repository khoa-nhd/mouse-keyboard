using System;
using System.Collections.Generic;

namespace KeyboardMouse.Core;

/// <summary>
/// Clickability classification outcomes.
/// Separates visual detection confidence from interaction confidence.
/// </summary>
public enum ClickabilityClassification
{
    /// <summary>
    /// Likely interactive - strong evidence of clickability.
    /// </summary>
    LikelyInteractive,

    /// <summary>
    /// Probably interactive - reasonable evidence, some uncertainty.
    /// </summary>
    ProbablyInteractive,

    /// <summary>
    /// Ambiguous - insufficient evidence to determine clickability.
    /// Should not be shown as ordinary high-confidence Hint targets.
    /// </summary>
    Ambiguous,

    /// <summary>
    /// Probably non-interactive - evidence suggests decoration.
    /// </summary>
    ProbablyNonInteractive,

    /// <summary>
    /// Non-interactive - strong evidence of decoration.
    /// </summary>
    NonInteractive
}

/// <summary>
/// Result of clickability classification for a candidate.
/// </summary>
public struct ClickabilityResult
{
    /// <summary>
    /// Constructor.
    /// </summary>
    public ClickabilityResult()
    {
        SupportingEvidence = new List<string>();
        RejectionReasons = new List<string>();
        ContextFactors = new Dictionary<string, object>();
    }

    /// <summary>
    /// The original candidate result.
    /// </summary>
    public CandidateResult Candidate { get; set; }

    /// <summary>
    /// Clickability classification outcome.
    /// </summary>
    public ClickabilityClassification Classification { get; set; }

    /// <summary>
    /// Confidence that this is the correct element type (detection confidence).
    /// </summary>
    public double DetectionConfidence { get; set; }

    /// <summary>
    /// Confidence that this element is interactive (clickability confidence).
    /// </summary>
    public double ClickabilityConfidence { get; set; }

    /// <summary>
    /// Confidence in the bounding box geometry.
    /// </summary>
    public double GeometryConfidence { get; set; }

    /// <summary>
    /// Supporting evidence for the classification.
    /// </summary>
    public List<string> SupportingEvidence { get; set; } = new();

    /// <summary>
    /// Reasons why this candidate might not be interactive.
    /// </summary>
    public List<string> RejectionReasons { get; set; } = new();

    /// <summary>
    /// Context factors that influenced the classification.
    /// </summary>
    public Dictionary<string, object> ContextFactors { get; set; } = new();

    /// <summary>
    /// Whether this candidate should be shown to the user.
    /// </summary>
    public bool ShouldShowToUser { get; set; } = true;
}