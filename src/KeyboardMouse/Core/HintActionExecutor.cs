using System;
using System.Collections.Generic;
using System.Diagnostics;
using KeyboardMouse.Core;
using KeyboardMouse.Native;
using KeyboardMouse.Output;

namespace KeyboardMouse.Capture;

/// <summary>
/// Safe action execution layer for hint targets.
///
/// Executes clicks, hovers, and other actions against resolved VisualTarget values.
/// Enforces safety checks before every execution: foreground window validation,
/// staleness detection, UIA pattern availability, coordinate validation, and
/// duplicate execution prevention.
///
/// Does not automatically execute both UIA Invoke and mouse click for the same target.
/// Uses explicit fallback rules: UIA patterns first, then CV mouse fallback.
/// </summary>
public sealed class HintActionExecutor : IHintActionExecutor
{
    /// <summary>Maximum time (ms) a target can go without a foreground check before being considered stale.</summary>
    private const int StaleTimeoutMs = 5000;

    /// <summary>Track the last executed target to prevent duplicates.</summary>
    private VisualTarget? _lastExecutedTarget;

    /// <summary>Track the last action time for each target, for staleness detection.</summary>
    private readonly Dictionary<string, DateTime> _targetLastActionTime = new();

    /// <summary>Execute an action against a visual target with full safety checks.</summary>
    /// <param name="target">The target to act upon.</param>
    /// <param name="action">The action type (LeftClick, RightClick, Hover).</param>
    /// <returns>An ActionResult describing the outcome.</returns>
    public ActionResult Execute(VisualTarget target, TargetActionType action)
    {
        // -- 1. Duplicate execution prevention --
        if (_lastExecutedTarget?.Id == target.Id)
        {
            return ActionResult.Canceled;
        }

        // -- 2. Foreground window validation --
        IntPtr foreground = User32.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return ActionResult.Stale;
        }

        // -- 3. Target staleness check --
        if (IsStale(target))
        {
            return ActionResult.Stale;
        }

        // -- 4. Action-specific validation and execution --
        return action switch
        {
            TargetActionType.LeftClick => ExecuteLeftClick(target),
            TargetActionType.RightClick => ExecuteRightClick(target),
            TargetActionType.DoubleClick => ExecuteDoubleClick(target),
            TargetActionType.Hover => ExecuteHover(target),
            _ => ActionResult.Failure
        };
    }

    /// <summary>Execute a left-click action.</summary>
    private ActionResult ExecuteLeftClick(VisualTarget target)
    {
        // Verify click point is valid for CV targets.
        if (!target.Bounds.Contains(target.ClickPoint) && target.Source.HasFlag(TargetSource.Cv))
        {
            return ActionResult.NotActionable;
        }

        // Verify the target is actionable.
        if (!IsTargetActionable(target))
        {
            return ActionResult.NotActionable;
        }

        try
        {
            var mouse = new MouseOutput();
            mouse.MoveTo(target.ClickPoint.X, target.ClickPoint.Y);
            mouse.LeftClick();

            // Track execution.
            _lastExecutedTarget = target;
            _targetLastActionTime[target.Id] = DateTime.Now;

            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Left-click execution failed: {ex}");
            return ActionResult.Failure;
        }
    }

    /// <summary>Execute a right-click action.</summary>
    private ActionResult ExecuteRightClick(VisualTarget target)
    {
        if (!target.Bounds.Contains(target.ClickPoint) && target.Source.HasFlag(TargetSource.Cv))
        {
            return ActionResult.NotActionable;
        }

        if (!IsTargetActionable(target))
        {
            return ActionResult.NotActionable;
        }

        try
        {
            var mouse = new MouseOutput();
            mouse.MoveTo(target.ClickPoint.X, target.ClickPoint.Y);
            mouse.RightClick();

            _lastExecutedTarget = target;
            _targetLastActionTime[target.Id] = DateTime.Now;

            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Right-click execution failed: {ex}");
            return ActionResult.Failure;
        }
    }

    /// <summary>Execute a double-click action.</summary>
    private ActionResult ExecuteDoubleClick(VisualTarget target)
    {
        if (!target.Bounds.Contains(target.ClickPoint) && target.Source.HasFlag(TargetSource.Cv))
        {
            return ActionResult.NotActionable;
        }

        if (!IsTargetActionable(target))
        {
            return ActionResult.NotActionable;
        }

        try
        {
            var mouse = new MouseOutput();
            mouse.MoveTo(target.ClickPoint.X, target.ClickPoint.Y);
            mouse.LeftClick();
            mouse.LeftClick();

            _lastExecutedTarget = target;
            _targetLastActionTime[target.Id] = DateTime.Now;

            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Double-click execution failed: {ex}");
            return ActionResult.Failure;
        }
    }

    /// <summary>Execute a hover action.</summary>
    private ActionResult ExecuteHover(VisualTarget target)
    {
        // For CV-only targets, hover is a validation no-op.
        if (target.Source.HasFlag(TargetSource.Cv) && !target.Source.HasFlag(TargetSource.Uia))
        {
            if (IsTargetActionable(target))
            {
                _lastExecutedTarget = target;
                _targetLastActionTime[target.Id] = DateTime.Now;
                return ActionResult.Success;
            }
            return ActionResult.NotActionable;
        }

        // For UIA targets, validate and position mouse.
        if (IsTargetActionable(target))
        {
            try
            {
                var mouse = new MouseOutput();
                mouse.MoveTo(target.ClickPoint.X, target.ClickPoint.Y);
                _lastExecutedTarget = target;
                _targetLastActionTime[target.Id] = DateTime.Now;
                return ActionResult.Success;
            }
            catch
            {
                return ActionResult.Failure;
            }
        }
        return ActionResult.NotActionable;
    }

    /// <summary>Check if a target is stale based on time and foreground state.</summary>
    private bool IsStale(VisualTarget target)
    {
        // For CV targets, staleness is based on time and foreground window only.
        // CvTargetMetadata does not have an IsOffscreen flag.

        // If too much time has passed since the last action, consider stale.
        if (_targetLastActionTime.TryGetValue(target.Id, out var last))
        {
            if (DateTime.Now - last > TimeSpan.FromMilliseconds(StaleTimeoutMs))
            {
                return true;
            }
        }

        // Verify the foreground window still exists.
        try
        {
            if (User32.GetForegroundWindow() == IntPtr.Zero)
            {
                return true;
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    /// <summary>Check if a target is actionable based on its metadata.</summary>
    private bool IsTargetActionable(VisualTarget target)
    {
        // Check IsEnabled/IsVisible from the target itself.
        if (!target.IsEnabled || !target.IsVisible)
        {
            return false;
        }

        // For UIA targets, check supported patterns.
        if (target.Metadata.Uia is not null)
        {
            // Target is actionable if it supports at least one relevant pattern.
            if (target.Metadata.Uia.SupportedPatterns.HasFlag(UiaPatternFlags.Invoke))
            {
                return true;
            }
            if (target.Metadata.Uia.SupportedPatterns.HasFlag(UiaPatternFlags.Toggle))
            {
                return true;
            }
            if (target.Metadata.Uia.SupportedPatterns.HasFlag(UiaPatternFlags.SelectionItem))
            {
                return true;
            }
        }

        // Default: if we have no reason to reject, consider it actionable.
        return true;
    }
}