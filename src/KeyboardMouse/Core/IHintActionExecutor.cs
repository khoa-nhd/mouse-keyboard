namespace KeyboardMouse.Core;

/// <summary>
/// Executes an action against a resolved target. Implementations move the
/// pointer to <see cref="VisualTarget.ClickPoint"/> (clamped to the desktop)
/// and perform the requested click or hover. They do not scan or access UI.
/// Returns an <see cref="ActionResult"/> describing success, failure,
/// cancellation, or a stale target.
/// </summary>
public interface IHintActionExecutor
{
    /// <summary>
    /// Executes an action against a visual target.  Returns false without
    /// moving or clicking when the target is not actionable. The controller
    /// owns label-prefix and modifier decisions.
    /// </summary>
    ActionResult Execute(VisualTarget target, TargetActionType action);
}