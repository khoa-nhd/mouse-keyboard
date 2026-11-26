using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// Drops elements their own window draws something else over.
///
/// A window can hide its own controls: Chrome keeps the buttons of a collapsed
/// vertical tab strip and of a hidden tab-group bar in the tree, reported as on
/// screen, under the page that is actually drawn there. No window-level test
/// can see that. The accessibility hit test can: asked what is at the control's
/// centre, it answers with the control (or a part of it) when the control is
/// really there, and with whatever covers it when it is not.
///
/// Providers differ in how carefully they hit-test. One that answers with its
/// whole pane for every point would otherwise lose every hint, so when most of
/// a window's targets fail the test the window is trusted as read.
/// </summary>
public static class HitConfirmation
{
    /// <summary>Pixels a hit element may stick out of the target and still be part of it.</summary>
    public const int Slack = 3;

    /// <summary>Share of failures above which the provider's hit test is not believed.</summary>
    public const double UnreliableShare = 0.6;

    /// <summary>Fewest checked targets before the failure share is judged.</summary>
    public const int MinimumForJudgement = 5;

    /// <param name="items">Targets read from one top-level window.</param>
    /// <param name="ownsPoint">Whether the window itself is the top-level window at a point.</param>
    /// <param name="probe">Bounds of the element the hit test finds at a point; null when it cannot tell.</param>
    /// <param name="isAncestor">
    /// Whether the hit element (by its bounds) is an ancestor of the target.
    /// Asked only when the hit encloses the target: some providers answer with
    /// the container of a visible control, which a covering page also does.
    /// Null treats every enclosing hit as covering.
    /// </param>
    public static List<RawClickableTarget> Filter(
        IReadOnlyList<RawClickableTarget> items,
        Func<Point, bool> ownsPoint,
        Func<Point, Rectangle?> probe,
        Func<RawClickableTarget, Rectangle, bool>? isAncestor = null)
    {
        var hidden = new HashSet<int>();
        int checkedCount = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var bounds = items[i].Bounds;
            var centre = new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));

            // A point another window covers is the window-level filter's job;
            // it can still move the click onto an uncovered part.
            if (!SafeOwns(ownsPoint, centre))
            {
                continue;
            }

            Rectangle? hit;

            try
            {
                hit = probe(centre);
            }
            catch
            {
                hit = null;
            }

            if (hit is not { Width: > 0, Height: > 0 } found)
            {
                continue;
            }

            checkedCount++;

            if (IsPartOf(found, bounds))
            {
                continue;
            }

            if (found.Contains(bounds) && SafeAncestor(isAncestor, items[i], found))
            {
                continue;
            }

            hidden.Add(i);
        }

        if (hidden.Count == 0 ||
            (checkedCount >= MinimumForJudgement && hidden.Count > UnreliableShare * checkedCount))
        {
            return [.. items];
        }

        var kept = new List<RawClickableTarget>(items.Count - hidden.Count);

        for (int i = 0; i < items.Count; i++)
        {
            if (!hidden.Contains(i))
            {
                kept.Add(items[i]);
            }
        }

        return kept;
    }

    /// <summary>
    /// Whether the element the hit test found belongs to the target. Usually it
    /// is the target or something inside it. A truncated label is the exception:
    /// it reports its full text width, wider than the row that clips it. What
    /// covers a hidden control is different in kind — a page, a panel, a
    /// toolbar — many times the control's size.
    /// </summary>
    public static bool IsPartOf(Rectangle hit, Rectangle target)
    {
        if (Rectangle.Inflate(target, Slack, Slack).Contains(hit))
        {
            return true;
        }

        long hitArea = (long)hit.Width * hit.Height;
        long targetArea = (long)target.Width * target.Height;
        var overlap = Rectangle.Intersect(hit, target);
        long shared = (long)Math.Max(0, overlap.Width) * Math.Max(0, overlap.Height);

        return hitArea <= MaxPartScale * targetArea && shared >= MinSharedShare * hitArea;
    }

    /// <summary>Largest size, relative to the target, an overflowing part may have.</summary>
    public const double MaxPartScale = 4;

    /// <summary>Share of an overflowing part that must lie on the target.</summary>
    public const double MinSharedShare = 0.3;

    private static bool SafeAncestor(Func<RawClickableTarget, Rectangle, bool>? isAncestor, RawClickableTarget item, Rectangle hit)
    {
        if (isAncestor is null)
        {
            return false;
        }

        try
        {
            return isAncestor(item, hit);
        }
        catch
        {
            // Cannot tell: keep the target rather than lose a real control.
            return true;
        }
    }

    private static bool SafeOwns(Func<Point, bool> ownsPoint, Point point)
    {
        try
        {
            return ownsPoint(point);
        }
        catch
        {
            return false;
        }
    }
}
