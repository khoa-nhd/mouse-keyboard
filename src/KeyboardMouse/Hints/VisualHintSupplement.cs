using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Platform;
using KeyboardMouse.Vision;

namespace KeyboardMouse.Hints;

/// <summary>
/// Adds pixel-based candidates to a UIA hint scan. UI Automation wins on any
/// overlap; the visual layer only contributes targets the accessibility tree
/// does not expose (canvas UI, custom-drawn controls, icon-only toolbars).
///
/// Acceptance is a two-step decision. A control the classifier is sure about is
/// offered. A control whose box is clean and whose type is unmistakably a
/// control is offered too, even when the classifier's confidence sat a little
/// below its threshold — that near-miss band is where most real buttons, tabs
/// and icons used to disappear.
/// </summary>
public static class VisualHintSupplement
{
    public const int MaxVisualCandidates = 120;

    /// <summary>Overlap above which a visual box is considered already covered by UIA.</summary>
    public const double UiaOverlapIou = 0.25;

    /// <summary>A box covering more than this share of the capture is a panel, not a control.</summary>
    public const double MaxRegionCoverage = 0.6;

    /// <summary>Geometry confidence a near-miss classification needs.</summary>
    public const double FallbackGeometryConfidence = 0.6;

    private static readonly HashSet<ControlType> ClickableTypes =
    [
        ControlType.Button,
        ControlType.IconButton,
        ControlType.ToggleButton,
        ControlType.Tab,
        ControlType.Link,
        ControlType.TextField,
        ControlType.Dropdown,
        ControlType.Checkbox,
        ControlType.RadioButton,
        ControlType.Switch,
        ControlType.MenuItem,
        ControlType.ToolbarItem
    ];

    /// <summary>
    /// Whether a geometrical control is worth a hint: either the classifier is
    /// confident, or the box is clean and the type can only be a control.
    /// </summary>
    public static bool IsAcceptable(ControlCandidate control)
    {
        ArgumentNullException.ThrowIfNull(control);

        // A box the geometry pass could not place a click point in is not a
        // target, however confident the classifier was.
        if (control.Geometry is not { IsClickable: true } geometry)
        {
            return false;
        }

        if (control.IsOfferable)
        {
            return true;
        }

        return geometry.Confidence >= FallbackGeometryConfidence &&
               ClickableTypes.Contains(control.Type) &&
               control.Bounds.Width >= 12 &&
               control.Bounds.Height >= 10;
    }

    /// <summary>
    /// Merges one capture's detection result into the hint list. Click points
    /// are mapped from capture pixels to screen pixels.
    /// </summary>
    public static IReadOnlyList<ClickableTarget> Add(
        VisualDetectionResult result,
        Rectangle region,
        IReadOnlyList<ClickableTarget> existing,
        int maxElements)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(existing);

        if (region.Width < 8 || region.Height < 8)
        {
            return existing;
        }

        var acceptable = result.Controls
            .Where(IsAcceptable)
            .OrderByDescending(control => control.Rank)
            .ThenByDescending(control => control.Area)
            .ToList();

        if (acceptable.Count == 0)
        {
            Log.Info(
                $"Visual detection found {result.Controls.Count} control(s) in " +
                $"{result.ElapsedMilliseconds} ms and none was offerable.");
            return existing;
        }

        var added = new List<ClickableTarget>(existing);
        int budget = Math.Min(MaxVisualCandidates, Math.Max(0, maxElements - existing.Count));
        long regionArea = (long)region.Width * region.Height;
        int covered = 0;
        int oversized = 0;
        int duplicateInResult = 0;

        foreach (var control in acceptable)
        {
            if (budget <= 0)
            {
                break;
            }

            var box = control.Geometry!.Bounds;

            if ((long)box.Width * box.Height > MaxRegionCoverage * regionArea)
            {
                oversized++;
                continue;
            }

            var bounds = new Rectangle(region.Left + box.Left, region.Top + box.Top, box.Width, box.Height);

            if (bounds.Width < 8 || bounds.Height < 6)
            {
                oversized++;
                continue;
            }

            // The accessible target is more precise than a pixel box that
            // surrounds or sits inside it, so it keeps the slot.
            if (existing.Any(target => Overlaps(target.Bounds, bounds)))
            {
                covered++;
                continue;
            }

            bool duplicate = false;

            for (int i = existing.Count; i < added.Count; i++)
            {
                // Two detectors describing the same control rarely agree on the
                // exact rectangle, so this uses the same offset-tolerant test as
                // the UIA check instead of plain IoU: 18 pairs on one screen were
                // arriving as two labels each.
                if (Overlaps(added[i].Bounds, bounds))
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
            {
                duplicateInResult++;
                continue;
            }

            added.Add(new ClickableTarget(bounds)
            {
                ClickPoint = new Point(
                    region.Left + control.Geometry.ClickPoint.X,
                    region.Top + control.Geometry.ClickPoint.Y)
            });

            budget--;
        }

        Log.Info(
            $"Visual detection: {result.Controls.Count} control(s), {acceptable.Count} acceptable, " +
            $"{added.Count - existing.Count} new, {covered} already covered by UIA, " +
            $"{duplicateInResult} duplicate(s), {oversized} unusable, " +
            $"{result.ElapsedMilliseconds} ms.");

        return added;
    }

    /// <summary>
    /// Whether a pixel box describes a control UI Automation already reported.
    /// Nested boxes count: a label inside a button is not a second target.
    /// </summary>
    public static bool Overlaps(Rectangle existing, Rectangle candidate)
    {
        if (VisualCandidate.Iou(existing, candidate) >= UiaOverlapIou)
        {
            return true;
        }

        // The same control seen by two passes is rarely the same rectangle: a
        // pixel box sits a few pixels off the accessible one, where IoU alone
        // reads as two different controls. Covering a third of the smaller box
        // is enough to call it the same target.
        var intersection = Rectangle.Intersect(existing, candidate);

        if (intersection.Width > 0 && intersection.Height > 0)
        {
            long inter = (long)intersection.Width * intersection.Height;
            long smaller = Math.Min(
                (long)existing.Width * existing.Height,
                (long)candidate.Width * candidate.Height);

            if (smaller > 0 && inter >= 0.35 * smaller)
            {
                return true;
            }
        }

        // The candidate surrounds an accessible target of comparable size: the
        // controllable area of that target is already labelled.
        if (candidate.Contains(existing) &&
            (long)existing.Width * existing.Height >= 0.15 * ((long)candidate.Width * candidate.Height))
        {
            return true;
        }

        // The candidate is a fragment of a small accessible control. A big
        // accessible box (a panel or a window) must not swallow real controls.
        return existing.Contains(candidate) &&
               existing.Width <= 600 &&
               existing.Height <= 200;
    }

    /// <summary>
    /// Captures and analyses one region. Kept for callers that do not run the
    /// pixel pass themselves; <see cref="ClickableScanner"/> captures up front
    /// and analyses in parallel instead.
    /// </summary>
    public static IReadOnlyList<ClickableTarget> Add(
        Rectangle region,
        IReadOnlyList<ClickableTarget> existing,
        int maxElements,
        int remainingMs)
    {
        if (remainingMs < 60 || region.Width < 8 || region.Height < 8)
        {
            return existing;
        }

        var image = ScreenCapture.Capture(region);

        if (image is null)
        {
            return existing;
        }

        var result = VisualPipeline.Detect(image, new VisualDetectionOptions
        {
            MaxCandidates = MaxVisualCandidates,
            MaxProcessingPixels = 2_200_000,
            MultiScale = false,
            MinScore = 0.45
        });

        return Add(result, region, existing, maxElements);
    }
}
