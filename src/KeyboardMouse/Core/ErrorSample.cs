using System;
using System.Collections.Generic;

namespace KeyboardMouse.Core;

/// <summary>
/// Error categories for detection failures.
/// </summary>
public enum DetectionErrorCategory
{
    /// <summary>
    /// A target that should have been detected but was missed.
    /// </summary>
    MissedTarget,

    /// <summary>
    /// A target that was detected but should not have been.
    /// </summary>
    IncorrectTarget,

    /// <summary>
    /// Multiple targets detected for the same UI element.
    /// </summary>
    DuplicateTarget,

    /// <summary>
    /// Target detected but bounding box is wrong.
    /// </summary>
    IncorrectBounds,

    /// <summary>
    /// Target detected but click point is outside the interactive area.
    /// </summary>
    IncorrectClickPoint,

    /// <summary>
    /// Detection was correct and useful.
    /// </summary>
    CorrectDetection,

    /// <summary>
    /// Candidate is ambiguous - unclear if interactive.
    /// </summary>
    AmbiguousCandidate
}

/// <summary>
/// Application categories for error samples.
/// </summary>
public enum ApplicationCategory
{
    WebBrowser,
    DesktopApp,
    SystemDialog,
    Game,
    Custom,
    Unknown
}

/// <summary>
/// Visual conditions for error samples.
/// </summary>
public enum VisualCondition
{
    Standard,
    DarkMode,
    HighContrast,
    Scaled,
    SmallControls,
    DenseLayout,
    LowResolution,
    HighResolution,
    Animating,
    Unknown
}

/// <summary>
/// A single error sample collected for active learning.
/// </summary>
public class ErrorSample
{
    /// <summary>
    /// Unique identifier for this sample.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Timestamp when the sample was captured.
    /// </summary>
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The error category.
    /// </summary>
    public DetectionErrorCategory ErrorCategory { get; set; }

    /// <summary>
    /// Application category.
    /// </summary>
    public ApplicationCategory ApplicationCategory { get; set; }

    /// <summary>
    /// Visual conditions present.
    /// </summary>
    public List<VisualCondition> VisualConditions { get; set; } = new();

    /// <summary>
    /// Algorithm version that produced the detection.
    /// </summary>
    public string AlgorithmVersion { get; set; } = string.Empty;

    /// <summary>
    /// Detector output at the time of capture.
    /// </summary>
    public List<CandidateResult> DetectorOutput { get; set; } = new();

    /// <summary>
    /// Ground-truth annotation (if reviewed).
    /// </summary>
    public List<CandidateResult> GroundTruth { get; set; } = new();

    /// <summary>
    /// Whether this sample has been human-reviewed.
    /// </summary>
    public bool IsReviewed { get; set; }

    /// <summary>
    /// Reviewer notes.
    /// </summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score of the candidate in question.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Whether this sample is a candidate for active learning.
    /// </summary>
    public bool IsActiveLearningCandidate { get; set; }

    /// <summary>
    /// Priority score for human review (higher = more important).
    /// </summary>
    public double ReviewPriority { get; set; }
}