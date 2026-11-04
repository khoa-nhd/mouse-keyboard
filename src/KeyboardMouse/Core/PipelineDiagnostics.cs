using System;
using System.Collections.Generic;

namespace KeyboardMouse.Core;

/// <summary>
/// Per-stage diagnostics for a single target through the hint pipeline.
/// Tracks why each target was detected, classified, merged, rejected, displayed,
/// selected, or executed.
/// </summary>
public class TargetDiagnostics
{
    /// <summary>
    /// Target identifier.
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Detection source (UIA, CV, OCR, etc.).
    /// </summary>
    public string DetectionSource { get; set; } = string.Empty;

    /// <summary>
    /// Why this target was detected.
    /// </summary>
    public string DetectionReason { get; set; } = string.Empty;

    /// <summary>
    /// Classification result.
    /// </summary>
    public string Classification { get; set; } = string.Empty;

    /// <summary>
    /// Why this target was classified this way.
    /// </summary>
    public string ClassificationReason { get; set; } = string.Empty;

    /// <summary>
    /// Whether this target was merged with another.
    /// </summary>
    public bool WasMerged { get; set; }

    /// <summary>
    /// Merge reason if applicable.
    /// </summary>
    public string MergeReason { get; set; } = string.Empty;

    /// <summary>
    /// Whether this target was rejected.
    /// </summary>
    public bool WasRejected { get; set; }

    /// <summary>
    /// Rejection reason if applicable.
    /// </summary>
    public string RejectionReason { get; set; } = string.Empty;

    /// <summary>
    /// Whether this target was displayed to the user.
    /// </summary>
    public bool WasDisplayed { get; set; }

    /// <summary>
    /// Display reason if applicable.
    /// </summary>
    public string DisplayReason { get; set; } = string.Empty;

    /// <summary>
    /// Whether this target was selected by the user.
    /// </summary>
    public bool WasSelected { get; set; }

    /// <summary>
    /// Selection reason if applicable.
    /// </summary>
    public string SelectionReason { get; set; } = string.Empty;

    /// <summary>
    /// Whether this target was executed (clicked).
    /// </summary>
    public bool WasExecuted { get; set; }

    /// <summary>
    /// Execution reason if applicable.
    /// </summary>
    public string ExecutionReason { get; set; } = string.Empty;

    /// <summary>
    /// Detection confidence.
    /// </summary>
    public double DetectionConfidence { get; set; }

    /// <summary>
    /// Clickability confidence.
    /// </summary>
    public double ClickabilityConfidence { get; set; }

    /// <summary>
    /// Geometry confidence.
    /// </summary>
    public double GeometryConfidence { get; set; }

    /// <summary>
    /// Timestamp when this diagnostic was recorded.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional context factors.
    /// </summary>
    public Dictionary<string, object> ContextFactors { get; set; } = new();
}

/// <summary>
/// Per-stage diagnostics collector for the hint pipeline.
/// </summary>
public static class PipelineDiagnostics
{
    private static readonly List<TargetDiagnostics> _diagnostics = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Whether diagnostics collection is enabled.
    /// </summary>
    public static bool IsEnabled { get; set; }

    /// <summary>
    /// Record diagnostics for a target.
    /// </summary>
    public static void Record(TargetDiagnostics diagnostic)
    {
        if (!IsEnabled) return;

        lock (_lock)
        {
            _diagnostics.Add(diagnostic);
        }
    }

    /// <summary>
    /// Get all recorded diagnostics.
    /// </summary>
    public static IReadOnlyList<TargetDiagnostics> GetAll()
    {
        lock (_lock)
        {
            return _diagnostics.AsReadOnly();
        }
    }

    /// <summary>
    /// Get diagnostics for a specific target.
    /// </summary>
    public static TargetDiagnostics? GetForTarget(string targetId)
    {
        lock (_lock)
        {
            return _diagnostics.FirstOrDefault(d => d.TargetId == targetId);
        }
    }

    /// <summary>
    /// Clear all diagnostics.
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _diagnostics.Clear();
        }
    }

    /// <summary>
    /// Get summary statistics.
    /// </summary>
    public static Dictionary<string, int> GetSummary()
    {
        lock (_lock)
        {
            return new Dictionary<string, int>
            {
                ["total"] = _diagnostics.Count,
                ["detected"] = _diagnostics.Count,
                ["merged"] = _diagnostics.Count(d => d.WasMerged),
                ["rejected"] = _diagnostics.Count(d => d.WasRejected),
                ["displayed"] = _diagnostics.Count(d => d.WasDisplayed),
                ["selected"] = _diagnostics.Count(d => d.WasSelected),
                ["executed"] = _diagnostics.Count(d => d.WasExecuted)
            };
        }
    }
}