using System.Threading;
using KeyboardMouse.Core;

namespace KeyboardMouse.Capture;

/// <summary>
/// Abstraction for a local OCR engine.  Implementations run entirely on the
/// local machine and never send screenshots or recognized text to cloud APIs.
/// </summary>
public interface IOcrEngine
{
    /// <summary>
    /// Recognizes text in the given capture, returning word-level results with
    /// bounding boxes in the capture's local coordinate space.
    /// </summary>
    /// <param name="capture">The capture whose content should be analyzed.</param>
    /// <param name="cancellationToken">Optional token to abort the operation.</param>
    /// <returns>A list of recognized words with their local bounds and confidence.</returns>
    IReadOnlyList<OcrWord> Recognize(CaptureResult capture, CancellationToken cancellationToken = default);
}

/// <summary>
/// A recognized word from an OCR engine, with its local bounding box and confidence.
/// </summary>
public sealed record OcrWord
{
    /// <summary>
    /// The recognized text.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Confidence of this recognition in [0, 1].
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// The word's bounding box in the capture's local coordinate space.
    /// </summary>
    public TargetBounds Bounds { get; init; } = TargetBounds.Empty;
}