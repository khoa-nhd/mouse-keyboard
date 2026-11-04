using System;

namespace KeyboardMouse.Core;

/// <summary>
/// Settings for UI element detector behavior.
/// Controls whether ML inference runs, confidence thresholds, and fallback behavior.
/// </summary>
public class UiElementDetectorSettings
{
    /// <summary>
    /// When true, the detector runs ML inference on each candidate region.
    /// When false, only heuristic analysis is performed (no performance overhead).
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Minimum confidence threshold for ML-assisted type assignment.
    /// Candidates with existing confidence below this may be upgraded by the detector.
    /// </summary>
    public double MlConfidenceThreshold { get; set; } = 0.7;

    /// <summary>
    /// When true, the detector may upgrade a candidate's type based on ML classification.
    /// When false, the detector only adds diagnostic features without type changes.
    /// </summary>
    public bool AllowTypeUpgrade { get; set; } = true;

    /// <summary>
    /// When true, the detector runs asynchronously with a timeout.
    /// When false, synchronous inference (may block briefly).
    /// </summary>
    public bool UseAsyncInference { get; set; } = true;

    /// <summary>
    /// Maximum inference timeout in milliseconds. Inference exceeding this is cancelled
    /// and the candidate falls back to heuristic-only results.
    /// </summary>
    public int MaxInferenceTimeoutMs { get; set; } = 200;
}