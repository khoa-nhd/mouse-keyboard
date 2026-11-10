namespace KeyboardMouse.Vision;

/// <summary>
/// Collapses the output of every detector and every scale into one hint list.
///
/// The rules are intentionally narrow: only near-identical boxes are treated as
/// duplicates, and only genuinely nested boxes are collapsed. Two buttons that
/// merely sit next to each other keep two candidates — proximity is not
/// evidence of sameness.
/// </summary>
public static class VisualMerger
{
    public const double DuplicateIou = 0.7;

    /// <summary>Above this share of the container the inner box is the same control.</summary>
    public const double NestedAreaRatio = 0.8;

    public static List<VisualCandidate> Merge(IEnumerable<VisualCandidate> candidates, int maxCount = 200)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var ordered = candidates
            .OrderByDescending(c => c.Area)
            .ThenByDescending(c => c.Score)
            .ToList();

        var kept = new List<VisualCandidate>(ordered.Count);

        foreach (var candidate in ordered)
        {
            bool skip = false;

            for (int i = kept.Count - 1; i >= 0 && !skip; i--)
            {
                var existing = kept[i];
                double iou = VisualCandidate.Iou(candidate.Bounds, existing.Bounds);

                if (iou >= DuplicateIou)
                {
                    // Same control seen by two detectors or two scales: take the
                    // more confident description, keep its geometry. A glyph and
                    // the ring traced inside it are the same box, and the glyph is
                    // the more specific description even when the ring scores
                    // higher: a circular glyph was being replaced by its own hole.
                    bool glyphBeatsRing = candidate.Shape == VisualShape.Icon &&
                                          existing.Shape != VisualShape.Icon;

                    if (glyphBeatsRing || candidate.Score > existing.Score)
                    {
                        kept[i] = candidate;
                    }

                    skip = true;
                }
                else if (existing.Bounds.Contains(candidate.Bounds) &&
                         candidate.Area <= NestedAreaRatio * existing.Area)
                {
                    // Anything inside a detected glyph is part of that glyph: a
                    // magnifier ring, the ball of a gear, a border of one stroke.
                    // Keeping the inner box turned every toolbar icon into the
                    // fragments of its own drawing.
                    bool insideGlyph = (candidate.Shape == VisualShape.Icon || existing.Shape == VisualShape.Icon) &&
                                       existing.Bounds.Width <= 96 &&
                                       existing.Bounds.Height <= 96;

                    // A link is text plus its rule: a fragment of its own label
                    // must not replace the link.
                    bool insideComposite = existing.Shape == VisualShape.Underline;

                    if (insideGlyph || insideComposite)
                    {
                        // An icon inside a small shape sits on that shape: the
                        // shape is the target (icon-only toolbar tiles land here).
                        // Inside a title bar or a panel the icon is the control, so
                        // it is kept and the bar stays a separate candidate.
                        skip = true;
                    }
                    else
                    {
                        // Segment inside a segmented control, checkbox inside a
                        // row, link inside a card: keep the inner, precise target.
                        kept.RemoveAt(i);
                    }
                }
                else if (candidate.Bounds.Contains(existing.Bounds) &&
                         existing.Area <= NestedAreaRatio * candidate.Area)
                {
                    skip = true;
                }
            }

            if (!skip)
            {
                kept.Add(candidate);
            }
        }

        return kept
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Bounds.Top)
            .ThenBy(c => c.Bounds.Left)
            .Take(maxCount)
            .ToList();
    }
}
