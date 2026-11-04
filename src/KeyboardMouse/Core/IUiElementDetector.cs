using KeyboardMouse.Core;

/// <summary>
/// Abstraction for UI element detection.
/// Separates heuristic/CV-based detection from optional ML-enhanced detection.
/// Implementations can be swapped without affecting the rest of the application.
/// </summary>
public interface IUiElementDetector
{
    /// <summary>
    /// Detect UI elements in the given capture region, augmenting existing candidates.
    /// </summary>
    /// <param name="capture">The capture result with bitmap and DPI information.</param>
    /// <param name="candidates">Existing candidate results from the CV heuristic detector.</param>
    /// <returns>Augmented list of candidate results, potentially with ML-enhanced
    /// type assignments, increased confidence, or additional diagnostic information.</returns>
    List<CandidateResult> Detect(CaptureResult capture, List<CandidateResult> candidates);
}