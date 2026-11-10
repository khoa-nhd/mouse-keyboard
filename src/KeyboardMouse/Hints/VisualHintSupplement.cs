using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Platform;
using KeyboardMouse.Vision;

namespace KeyboardMouse.Hints;

/// <summary>
/// Adds pixel-based candidates to a UIA hint scan. UI Automation wins on any
/// overlap; the visual layer only contributes targets the accessibility tree
/// does not expose (canvas UI, custom-drawn controls, icon-only toolbars).
/// </summary>
public static class VisualHintSupplement
{
    public const int MaxVisualCandidates = 60;

    /// <summary>Overlap above which a visual box is considered already covered by UIA.</summary>
    public const double UiaOverlapIou = 0.35;

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

        // Keep most of the capture's detail: 1.6 MP is native 1080p, and a 4K
        // monitor still lands around half scale, where a 10 px control is five
        // pixels instead of the three a 700 k ceiling allowed.
        var result = VisualPipeline.Detect(image, new VisualDetectionOptions
        {
            MaxCandidates = 80,
            MaxProcessingPixels = 1_600_000
        });

        // Only typed, interactive controls are offered. Decorative icons, cards,
        // images and bare text reach the classifier with high detection
        // confidence but below the interaction threshold for their type.
        var interactive = result.Interactive;

        if (interactive.Count == 0)
        {
            return existing;
        }

        var added = new List<ClickableTarget>(existing);
        int budget = Math.Min(MaxVisualCandidates, Math.Max(0, maxElements - existing.Count));
        int rejected = 0;

        foreach (var candidate in interactive)
        {
            if (budget <= 0)
            {
                break;
            }

            // A box the geometry pass could not place a click point in is not a
            // target, however confident the classifier was.
            if (candidate.Geometry is not { IsClickable: true } geometry)
            {
                rejected++;
                continue;
            }

            var box = geometry.Bounds;

            // A container is not a target when a smaller control inside it is:
            // the precise box wins over the region that merely surrounds it.
            if (interactive.Any(other =>
                    !ReferenceEquals(other, candidate) &&
                    other.Geometry is { IsClickable: true } inner &&
                    box.Contains(inner.Bounds) &&
                    inner.Bounds != box &&
                    (long)inner.Bounds.Width * inner.Bounds.Height < 0.6 * (long)box.Width * box.Height))
            {
                rejected++;
                continue;
            }

            var bounds = new Rectangle(region.Left + box.Left, region.Top + box.Top, box.Width, box.Height);

            if (existing.Any(target => VisualCandidate.Iou(target.Bounds, bounds) >= UiaOverlapIou))
            {
                continue;
            }

            bool duplicate = false;

            for (int i = existing.Count; i < added.Count; i++)
            {
                if (VisualCandidate.Iou(added[i].Bounds, bounds) >= 0.5)
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
            {
                continue;
            }

            added.Add(new ClickableTarget(bounds)
            {
                ClickPoint = new Point(
                    region.Left + geometry.ClickPoint.X,
                    region.Top + geometry.ClickPoint.Y)
            });
            budget--;
        }

        int extra = added.Count - existing.Count;

        if (extra > 0 || rejected > 0)
        {
            Log.Info(
                $"Visual detection added {extra} target(s) in {result.ElapsedMilliseconds} ms " +
                $"at scale(s) {string.Join(", ", result.Scales)}: " +
                string.Join(", ", interactive.Take(extra).Select(c => c.Type.ToString())) +
                (rejected > 0 ? $" ({rejected} rejected by geometry)." : "."));
        }

        return added;
    }
}
