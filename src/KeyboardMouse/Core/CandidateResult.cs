using System.Collections.Generic;

namespace KeyboardMouse.Core;

/// <summary>
/// Normalized candidate result from any detector.
/// </summary>
public struct CandidateResult
{
    /// <summary>
    /// Constructor.
    /// </summary>
    public CandidateResult(Rectangle bounds, TargetType type, double confidence, string detector)
    {
        Bounds = bounds;
        Type = type;
        Confidence = confidence;
        Detector = detector;
        Features = new Dictionary<string, object>();
        Diagnostics = new Dictionary<string, object>();
    }

    /// <summary>Bounding box in desktop coordinates.</summary>
    public Rectangle Bounds { get; set; }

    /// <summary>Candidate type (e.g., Button, CheckBox, Icon).</summary>
    public TargetType Type { get; set; }

    /// <summary>Detector confidence score.</summary>
    public double Confidence { get; set; }

    /// <summary>Source detector name.</summary>
    public string Detector { get; set; } = string.Empty;

    /// <summary>Visual features extracted by the detector.</summary>
    public Dictionary<string, object> Features { get; set; } = new();

    /// <summary>Diagnostic information from the detector.</summary>
    public Dictionary<string, object> Diagnostics { get; set; } = new();
}