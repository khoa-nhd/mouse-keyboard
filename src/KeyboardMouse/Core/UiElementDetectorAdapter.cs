using KeyboardMouse.Core;
using KeyboardMouse.Config;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Base adapter for UI element detectors.
/// Provides default no-op implementation (CV-only mode) and common infrastructure.
/// Concrete adapters (e.g., ONNX-based) override the DetectCore method.
/// </summary>
public abstract class UiElementDetectorAdapter : IUiElementDetector
{
    private readonly UiElementDetectorSettings _settings;

    protected UiElementDetectorAdapter(UiElementDetectorSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Detect UI elements, augmenting existing CV candidates.
    /// </summary>
    public List<CandidateResult> Detect(CaptureResult capture, List<CandidateResult> candidates)
    {
        if (!IsInitialized)
        {
            // Model not available - return candidates unchanged (CV-only mode)
            return candidates;
        }

        if (candidates == null || candidates.Count == 0)
        {
            return candidates;
        }

        // Quick check: if detector is disabled, skip entirely
        if (!_settings.IsEnabled)
        {
            return candidates;
        }

        return DetectCore(capture, candidates);
    }

    /// <summary>
    /// Initialization status - derived classes report whether model is loaded.
    /// </summary>
    protected abstract bool IsInitialized { get; }

    /// <summary>
    /// Core detection logic. Override this method in derived classes.
    /// The default implementation returns candidates unchanged (fallback mode).
    /// </summary>
    protected virtual List<CandidateResult> DetectCore(CaptureResult capture, List<CandidateResult> candidates)
    {
        // Default: return candidates unchanged (fallback to CV-only)
        // Derived classes should override this with actual ML inference
        return candidates;
    }

    /// <summary>
    /// Run ML inference on a single candidate region.
    /// Derived classes should implement region extraction and model inference.
    /// </summary>
    /// <param name="capture">The capture result for coordinate info and bitmap access.</param>
    /// <param "candidate">The candidate region to classify.</param>
    /// <param name="cancellationToken">For cancellation during inference.</param>
    /// <returns>Classification result with confidence and predicted type, or null if inference fails.</returns>
    protected abstract Task<ClassificationResult?> ClassifyCandidateAsync(
        CaptureResult capture,
        CandidateResult candidate,
        CancellationToken cancellationToken);

    /// <summary>
    /// Synchronous wrapper for classify candidate (uses async internally if configured).
    /// </summary>
    protected virtual ClassificationResult? ClassifyCandidate(
        CaptureResult capture,
        CandidateResult candidate)
    {
        if (!_settings.UseAsyncInference)
        {
            // Synchronous path - block briefly
            var cts = new CancellationTokenSource(_settings.MaxInferenceTimeoutMs);
            try
            {
                return ClassifyCandidateAsync(capture, candidate, cts.Token).Result;
            }
            catch (OperationCanceledException)
            {
                return null; // Timeout - fall back
            }
            catch
            {
                return null; // Error - fall back
            }
        }
        else
        {
            // Async path - not callable from sync context directly
            return null; // Caller should use async API
        }
    }

    /// <summary>
    /// Check if candidate bounds are valid for classification.
    /// </summary>
    protected static bool IsValidCandidateBounds(CandidateResult candidate)
    {
        // CandidateResult is a struct, always non-null
        return candidate.Bounds.Width >= 12 &&
               candidate.Bounds.Height >= 12 &&
               candidate.Bounds.Width <= 500 &&
               candidate.Bounds.Height <= 500;
    }

    /// <summary>
    /// Result of classifying a single candidate.
    /// </summary>
    public record ClassificationResult
    {
        /// <summary>
        /// Predicted UI element type.
        /// </summary>
        public TargetType PredictedType { get; set; } = TargetType.Other;

        /// <summary>
        /// Confidence of the prediction (0.0 to 1.0).
        /// </summary>
        public double Confidence { get; set; } = 0.0;

        /// <summary>
        /// Additional visual features extracted for diagnostics.
        /// </summary>
        public Dictionary<string, object> Features { get; set; } = new();

        /// <summary>
        /// Source model name or identifier.
        /// </summary>
        public string ModelSource { get; set; } = "unknown";
    }
}