using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyboardMouse.Core;

/// <summary>
/// Threshold calibration using validation data.
/// Avoids tuning thresholds on the held-out test set.
/// </summary>
public class ThresholdCalibrator
{
    /// <summary>
    /// Calibrated thresholds for different control types and conditions.
    /// </summary>
    public class CalibratedThresholds
    {
        /// <summary>
        /// Default clickability threshold.
        /// </summary>
        public double DefaultThreshold { get; set; } = 0.5;

        /// <summary>
        /// Thresholds by control type.
        /// </summary>
        public Dictionary<TargetType, double> ByControlType { get; set; } = new();

        /// <summary>
        /// Thresholds by detection method.
        /// </summary>
        public Dictionary<string, double> ByDetectionMethod { get; set; } = new();

        /// <summary>
        /// Thresholds by visual condition.
        /// </summary>
        public Dictionary<VisualCondition, double> ByVisualCondition { get; set; } = new();
    }

    private CalibratedThresholds _thresholds = new();

    /// <summary>
    /// Get the current calibrated thresholds.
    /// </summary>
    public CalibratedThresholds Thresholds => _thresholds;

    /// <summary>
    /// Calibrate thresholds using validation data.
    /// </summary>
    /// <param name="validationSamples">Reviewed validation samples.</param>
    public void Calibrate(List<ErrorSample> validationSamples)
    {
        if (validationSamples == null || validationSamples.Count == 0)
            return;

        // Group by control type (from detector output)
        var byType = validationSamples
            .Where(s => s.IsReviewed)
            .GroupBy(s => s.DetectorOutput.Count > 0 ? s.DetectorOutput[0].Type : TargetType.Other)
            .ToList();

        foreach (var group in byType)
        {
            var type = group.Key;
            var samples = group.ToList();

            // Compute optimal threshold using F1 maximization
            var optimalThreshold = FindOptimalThreshold(samples);
            _thresholds.ByControlType[type] = optimalThreshold;
        }

        // Group by detection method
        var byMethod = validationSamples
            .Where(s => s.IsReviewed)
            .GroupBy(s => s.DetectorOutput.Count > 0 ? s.DetectorOutput[0].Detector : "unknown")
            .ToList();

        foreach (var group in byMethod)
        {
            var method = group.Key;
            var samples = group.ToList();

            var optimalThreshold = FindOptimalThreshold(samples);
            _thresholds.ByDetectionMethod[method] = optimalThreshold;
        }

        // Group by visual condition
        foreach (var condition in Enum.GetValues<VisualCondition>())
        {
            var conditionSamples = validationSamples
                .Where(s => s.IsReviewed && s.VisualConditions.Contains(condition))
                .ToList();

            if (conditionSamples.Count > 0)
            {
                var optimalThreshold = FindOptimalThreshold(conditionSamples);
                _thresholds.ByVisualCondition[condition] = optimalThreshold;
            }
        }

        // Overall default threshold
        var allReviewed = validationSamples.Where(s => s.IsReviewed).ToList();
        if (allReviewed.Count > 0)
        {
            _thresholds.DefaultThreshold = FindOptimalThreshold(allReviewed);
        }
    }

    /// <summary>
    /// Find optimal threshold using F1 score maximization.
    /// </summary>
    private double FindOptimalThreshold(List<ErrorSample> samples)
    {
        double bestF1 = 0.0;
        double bestThreshold = 0.5;

        // Try thresholds from 0.1 to 0.9
        for (double t = 0.1; t <= 0.9; t += 0.05)
        {
            int tp = 0, fp = 0, fn = 0;

            foreach (var sample in samples)
            {
                bool predicted = sample.Confidence >= t;
                bool actual = sample.ErrorCategory == DetectionErrorCategory.CorrectDetection;

                if (predicted && actual) tp++;
                else if (predicted && !actual) fp++;
                else if (!predicted && actual) fn++;
            }

            double precision = tp + fp > 0 ? (double)tp / (tp + fp) : 0.0;
            double recall = tp + fn > 0 ? (double)tp / (tp + fn) : 0.0;
            double f1 = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0.0;

            if (f1 > bestF1)
            {
                bestF1 = f1;
                bestThreshold = t;
            }
        }

        return bestThreshold;
    }

    /// <summary>
    /// Get the calibrated threshold for a specific context.
    /// </summary>
    public double GetThreshold(TargetType? controlType = null, string? detectionMethod = null, VisualCondition? visualCondition = null)
    {
        // Priority: visual condition > detection method > control type > default
        if (visualCondition.HasValue && _thresholds.ByVisualCondition.TryGetValue(visualCondition.Value, out var vcThreshold))
            return vcThreshold;

        if (detectionMethod != null && _thresholds.ByDetectionMethod.TryGetValue(detectionMethod, out var dmThreshold))
            return dmThreshold;

        if (controlType.HasValue && _thresholds.ByControlType.TryGetValue(controlType.Value, out var ctThreshold))
            return ctThreshold;

        return _thresholds.DefaultThreshold;
    }
}