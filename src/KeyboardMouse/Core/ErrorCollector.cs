using System;
using System.Collections.Generic;
using System.IO;

namespace KeyboardMouse.Core;

/// <summary>
/// Developer-only diagnostic workflow for collecting detection errors.
/// All data is kept local. No screenshots are uploaded.
/// </summary>
public class ErrorCollector
{
    private readonly string _diagnosticDir;
    private readonly List<ErrorSample> _samples = new();
    private bool _isEnabled;

    /// <summary>
    /// Creates a new error collector.
    /// </summary>
    /// <param name="diagnosticDir">Directory to store diagnostic samples.</param>
    public ErrorCollector(string diagnosticDir = "diagnostics")
    {
        _diagnosticDir = diagnosticDir;
    }

    /// <summary>
    /// Whether diagnostic collection is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (value)
            {
                Directory.CreateDirectory(_diagnosticDir);
            }
        }
    }

    /// <summary>
    /// Mark a detection error for collection.
    /// </summary>
    public ErrorSample MarkError(
        DetectionErrorCategory category,
        List<CandidateResult> detectorOutput,
        CandidateResult? candidate = null,
        ApplicationCategory appCategory = ApplicationCategory.Unknown,
        List<VisualCondition>? visualConditions = null,
        string notes = "")
    {
        if (!_isEnabled)
        {
            throw new InvalidOperationException("Diagnostic collection is not enabled.");
        }

        var sample = new ErrorSample
        {
            ErrorCategory = category,
            DetectorOutput = detectorOutput,
            ApplicationCategory = appCategory,
            VisualConditions = visualConditions ?? new List<VisualCondition>(),
            Notes = notes,
            AlgorithmVersion = "1.2.0", // TODO: Get from assembly version
            Confidence = candidate?.Confidence ?? 0.0
        };

        // Determine if this is an active learning candidate
        sample.IsActiveLearningCandidate = IsActiveLearningCandidate(sample);
        sample.ReviewPriority = ComputeReviewPriority(sample);

        _samples.Add(sample);

        return sample;
    }

    /// <summary>
    /// Determine if a sample should be prioritized for active learning.
    /// </summary>
    private bool IsActiveLearningCandidate(ErrorSample sample)
    {
        // Low confidence samples
        if (sample.Confidence < 0.6)
            return true;

        // Disagreement between detectors (would need detector outputs to compare)
        // For now, mark ambiguous and incorrect targets as candidates
        if (sample.ErrorCategory == DetectionErrorCategory.AmbiguousCandidate ||
            sample.ErrorCategory == DetectionErrorCategory.IncorrectTarget)
            return true;

        // Near decision thresholds
        if (sample.Confidence >= 0.35 && sample.Confidence <= 0.45)
            return true;

        return false;
    }

    /// <summary>
    /// Compute review priority score.
    /// </summary>
    private double ComputeReviewPriority(ErrorSample sample)
    {
        double priority = 0.0;

        // Error category priority
        priority += sample.ErrorCategory switch
        {
            DetectionErrorCategory.MissedTarget => 0.4,
            DetectionErrorCategory.IncorrectTarget => 0.3,
            DetectionErrorCategory.DuplicateTarget => 0.2,
            DetectionErrorCategory.IncorrectBounds => 0.25,
            DetectionErrorCategory.IncorrectClickPoint => 0.35,
            DetectionErrorCategory.AmbiguousCandidate => 0.15,
            _ => 0.0
        };

        // Low confidence increases priority
        if (sample.Confidence < 0.5)
            priority += 0.2;

        // Active learning candidates get bonus
        if (sample.IsActiveLearningCandidate)
            priority += 0.1;

        return Math.Clamp(priority, 0.0, 1.0);
    }

    /// <summary>
    /// Get all collected samples.
    /// </summary>
    public IReadOnlyList<ErrorSample> GetSamples() => _samples.AsReadOnly();

    /// <summary>
    /// Get samples that need human review, sorted by priority.
    /// </summary>
    public List<ErrorSample> GetReviewQueue()
    {
        return _samples
            .Where(s => !s.IsReviewed)
            .OrderByDescending(s => s.ReviewPriority)
            .ToList();
    }

    /// <summary>
    /// Get samples that are active learning candidates.
    /// </summary>
    public List<ErrorSample> GetActiveLearningCandidates()
    {
        return _samples
            .Where(s => s.IsActiveLearningCandidate && !s.IsReviewed)
            .OrderByDescending(s => s.ReviewPriority)
            .ToList();
    }

    /// <summary>
    /// Mark a sample as reviewed.
    /// </summary>
    public void MarkReviewed(string sampleId, string notes = "")
    {
        var sample = _samples.FirstOrDefault(s => s.Id == sampleId);
        if (sample != null)
        {
            sample.IsReviewed = true;
            sample.Notes = notes;
        }
    }

    /// <summary>
    /// Delete all diagnostic samples and files.
    /// </summary>
    public void ClearAll()
    {
        _samples.Clear();
        if (Directory.Exists(_diagnosticDir))
        {
            Directory.Delete(_diagnosticDir, recursive: true);
        }
    }

    /// <summary>
    /// Save a sample to disk (local only, no upload).
    /// </summary>
    public void SaveSample(ErrorSample sample, byte[]? screenshotData = null)
    {
        if (!_isEnabled) return;

        var sampleDir = Path.Combine(_diagnosticDir, sample.Id);
        Directory.CreateDirectory(sampleDir);

        // Save metadata as JSON
        var metadataPath = Path.Combine(sampleDir, "metadata.json");
        var json = System.Text.Json.JsonSerializer.Serialize(sample, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(metadataPath, json);

        // Save screenshot if provided (local only)
        if (screenshotData != null && screenshotData.Length > 0)
        {
            var screenshotPath = Path.Combine(sampleDir, "screenshot.png");
            File.WriteAllBytes(screenshotPath, screenshotData);
        }
    }

    /// <summary>
    /// Load samples from disk.
    /// </summary>
    public void LoadSamples()
    {
        if (!Directory.Exists(_diagnosticDir)) return;

        foreach (var dir in Directory.GetDirectories(_diagnosticDir))
        {
            var metadataPath = Path.Combine(dir, "metadata.json");
            if (File.Exists(metadataPath))
            {
                try
                {
                    var json = File.ReadAllText(metadataPath);
                    var sample = System.Text.Json.JsonSerializer.Deserialize<ErrorSample>(json);
                    if (sample != null)
                    {
                        _samples.Add(sample);
                    }
                }
                catch
                {
                    // Skip invalid samples
                }
            }
        }
    }
}