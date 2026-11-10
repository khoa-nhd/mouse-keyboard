using System.Drawing;

namespace KeyboardMouse.Vision;

/// <summary>
/// Turns "there is a shape here" into "this looks like a control of type X, with
/// this much interaction confidence".
///
/// The rules are ordered from strongest evidence to weakest: a full-height
/// divider is a separator no matter what else is true, and an isolated icon that
/// no layout groups with anything else is decoration. Each branch records the
/// evidence it used, so a misclassification can be explained rather than just
/// counted.
/// </summary>
public static class ControlClassifier
{
    /// <summary>Largest box still treated as a control rather than a panel.</summary>
    public const int PanelWidth = 190;

    public const int PanelHeight = 70;

    public static List<ControlCandidate> Classify(
        Image32 image,
        IReadOnlyList<VisualCandidate> candidates,
        IReadOnlyList<Rectangle> textLines)
    {
        ArgumentNullException.ThrowIfNull(image);

        var features = ControlFeatureExtractor.Extract(image, candidates, textLines ?? Array.Empty<Rectangle>());
        var results = new List<ControlCandidate>(candidates.Count);

        for (int i = 0; i < candidates.Count; i++)
        {
            results.Add(Classify(candidates[i], features[i]));
        }

        return results;
    }

    public static ControlCandidate Classify(VisualCandidate candidate, ControlFeatures f)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var evidence = Evidence(candidate, f);
        var box = candidate.Bounds;

        // --- separators and scrollbars -------------------------------------
        if (f.ThinHorizontal || f.ThinVertical)
        {
            return Make(candidate, ControlType.Separator, 0.05, evidence);
        }

        bool barShaped = (box.Width <= 14 && box.Height >= 60) || (box.Height <= 14 && box.Width >= 60);

        if (barShaped && !f.HasText && f.HasFlatFill)
        {
            return Make(candidate, ControlType.Scrollbar, 0.74, evidence | ControlEvidence.ThinBar);
        }

        // --- panels, images ------------------------------------------------
        bool panelSized = box.Width >= PanelWidth || box.Height >= PanelHeight;

        if (panelSized && f.HasFlatFill && f.ContainsOthers)
        {
            return Make(candidate, ControlType.Card, 0.42, evidence | ControlEvidence.LowInteriorDetail);
        }

        if (!f.HasText &&
            box.Width >= 44 &&
            box.Height >= 32 &&
            f.EdgeDensity >= 0.16 &&
            f.FillUniformity < 0.45)
        {
            return Make(candidate, ControlType.Image, 0.15, evidence);
        }

        // --- segmented tabs -------------------------------------------------
        if (candidate.Detector == VisualDetector.Segmented)
        {
            return Make(candidate, ControlType.Tab, 0.72, evidence | ControlEvidence.Repetition);
        }

        // --- toggle switch: a pill with a knob ------------------------------
        // A switch is a pill with a knob on it. The knob may be light on a dark
        // track, so interior contrast counts as evidence too — the dark-polarity
        // circle pass cannot see a white knob.
        if (f.Aspect is >= 1.4 and <= 3.0 && box.Height is >= 14 and <= 36 && f.HasFlatFill &&
            (f.HasKnob || f.Contrast >= 90))
        {
            return Make(candidate, ControlType.Switch, 0.72, evidence | ControlEvidence.Knob);
        }

        // --- slider: a thin track, usually with a knob ----------------------
        if (box.Width >= 60 && box.Height <= 16 && (f.HasKnob || (f.HasFlatFill && f.Contrast >= 25)))
        {
            return Make(candidate, ControlType.Slider, f.HasKnob ? 0.72 : 0.5, evidence);
        }

        // --- links ----------------------------------------------------------
        // A rule under a heading is also an underline, but a box that is taller
        // than a line of text and carries its own border and fill is a card or a
        // banner with a rule in it, not something to click.
        bool rule = candidate.Shape == VisualShape.Underline || candidate.Detector == VisualDetector.Underline;

        if (rule && box.Height <= 28 && !(f.HasBorder && f.HasFlatFill))
        {
            return Make(candidate, ControlType.Link, 0.8, evidence | ControlEvidence.Underline);
        }

        // --- checkboxes and radios -----------------------------------------
        bool smallSquare = box.Width is >= 12 and <= 26 && box.Height is >= 12 and <= 26 && f.Square;
        bool isGlyph = candidate.Shape == VisualShape.Icon || candidate.Detector == VisualDetector.Icon;

        if (smallSquare && !isGlyph && (f.HasBorder || candidate.Shape == VisualShape.Circle || candidate.Detector == VisualDetector.Circle))
        {
            bool ring = candidate.Shape == VisualShape.Circle || candidate.Detector == VisualDetector.Circle;

            // A small ring with no label and no column of similar rings is more
            // likely a glyph inside an icon than a real checkbox.
            bool controlContext = f.LabelBeside || f.ColumnPeers >= 1;

            if (ring && !f.HasBorder)
            {
                return Make(candidate, ControlType.RadioButton, controlContext ? 0.72 : 0.5, evidence);
            }

            return Make(candidate, ControlType.Checkbox, controlContext ? 0.72 : 0.48, evidence);
        }

        // A small outline ring reaches us as an icon cluster. Beside a label, or in
        // a column of like-sized shapes, it is a radio/checkbox, not a glyph.
        // The label is the evidence: a ring in a row of unlabelled glyphs is a
        // toolbar icon, a ring beside an option's text is a radio button.
        if (isGlyph && smallSquare && !f.HasFlatFill && f.TextCoverage <= 0.2 && f.LabelBeside)
        {
            return Make(candidate, ControlType.RadioButton, 0.68, evidence);
        }

        // --- icon-only controls --------------------------------------------
        if (isGlyph)
        {
            // Size-agnostic row grouping: a toolbar mixes 22 px and 12 px glyphs.
            bool inToolbarRow = f.RowNeighbours >= 2;
            bool hasOwnSurface = f.HasFlatFill || f.HasBorder;

            // A glyph inside a photo or a dense region reads as "text inside" and
            // is decoration. A real toolbar icon does too (three bars look like a
            // word), so the guard only applies outside a grouped row.
            if (f.TextCoverage >= 0.35 && f.RowNeighbours < 2)
            {
                return Make(candidate, ControlType.DecorativeIcon, 0.25, evidence);
            }

            if (hasOwnSurface && (inToolbarRow || f.InsideContainer))
            {
                return Make(candidate, ControlType.IconButton, 0.68 + (f.RowPeers >= 2 ? 0.06 : 0), evidence);
            }

            // Two neighbours is what a grouped row looks like, but three tiles in a
            // row already share a surface and a baseline: the middle one sees two
            // peers, the outer ones only one.
            if (hasOwnSurface && f.RowPeers >= 1)
            {
                return Make(candidate, ControlType.IconButton, 0.68, evidence);
            }

            if (hasOwnSurface)
            {
                return Make(candidate, ControlType.IconButton, 0.6, evidence);
            }

            if (inToolbarRow)
            {
                return Make(candidate, ControlType.ToolbarItem, 0.68, evidence);
            }

            if (f.LabelBelow)
            {
                return Make(candidate, ControlType.IconButton, 0.64, evidence);
            }

            // A lone glyph with no group, no surface and no label is almost
            // always decoration: bullets, status marks, illustration details.
            return Make(candidate, ControlType.DecorativeIcon, 0.25, evidence);
        }

        // --- small flat tile with a glyph ----------------------------------
        // An icon on its own background arrives as the tile, because merging
        // replaced the inner glyph with its container. A small filled box with
        // interior detail is that tile: too small for a text button, too detailed
        // for an empty chip.
        if (f.HasFlatFill &&
            box.Width is >= 16 and <= 60 &&
            box.Height is >= 16 and <= 60 &&
            f.EdgeDensity is >= 0.05 and <= 0.3 &&
            f.Contrast >= 50 &&
            !f.ContainsOthers)
        {
            return Make(candidate, ControlType.IconButton, 0.68, evidence | ControlEvidence.IconCluster);
        }

        // --- buttons --------------------------------------------------------
        bool buttonSized = box.Width >= 34 && box.Height is >= 18 and <= 64 && f.Aspect is >= 0.6 and <= 8;
        bool labelCentred = f.TextCentered || (f.HasBorder && box.Width <= 160);

        // A rule inside the box means a heading over a divider — a banner, not a
        // button whose label is centred on it.
        bool hasRuleInside = evidence.HasFlag(ControlEvidence.Underline);

        if (buttonSized && f.HasText && labelCentred && (f.HasBorder || f.HasFlatFill) && !hasRuleInside)
        {
            double score = 0.72
                + (f.HasBorder ? 0.05 : 0)
                + (f.HasFlatFill ? 0.04 : 0)
                + (f.RowPeers >= 1 ? 0.03 : 0);

            return Make(candidate, ControlType.Button, score, evidence);
        }

        // --- text fields and dropdowns -------------------------------------
        // A field is wide, has a surface, and either no text or text pinned to
        // its left padding. Centred text on a surface is a button and was
        // already classified above.
        if (box.Width >= 70 && box.Height is >= 16 and <= 48 && f.Aspect >= 2.0 &&
            (f.HasBorder || f.HasFlatFill) &&
            (!f.HasText || !f.TextCentered || f.TextCoverage <= 0.35))
        {
            if (f.HasChevron)
            {
                return Make(candidate, ControlType.Dropdown, 0.72, evidence | ControlEvidence.Chevron);
            }

            return Make(candidate, ControlType.TextField, 0.66, evidence | ControlEvidence.LowInteriorDetail);
        }

        // --- menu rows: a stacked column of text inside a container ---------
        if (f.HasText && f.ColumnPeers >= 1 && f.InsideContainer && box.Height <= 44)
        {
            return Make(candidate, ControlType.MenuItem, 0.7, evidence);
        }

        // --- toggle button: a pill inside a segmented-looking row -----------
        if (buttonSized && f.RoundedCorners && f.HasFlatFill && f.Aspect is >= 1.3 and <= 3.4 && f.RowPeers >= 1)
        {
            return Make(candidate, ControlType.ToggleButton, 0.62, evidence);
        }

        // --- bare text ------------------------------------------------------
        // A text-only button has no surface to find. The only usable evidence is
        // layout: a short line that shares its row with other short lines is a
        // toolbar or dialog footer; a line alone in the flow is prose.
        if (candidate.Detector == VisualDetector.TextAssociation)
        {
            // A short line with a couple of spaced peers in an otherwise empty
            // band is a footer or toolbar button. Text inside a paragraph has
            // tightly stacked neighbours and word-sized peers, and stays a label.
            bool rowGroup = f.RowPeers is >= 1 and <= 4 &&
                            f.RowGap >= 10 &&
                            !f.InTextFlow &&
                            box.Height <= 44 &&
                            box.Width <= 260;

            if (rowGroup)
            {
                return Make(candidate, ControlType.Button, 0.64, evidence | ControlEvidence.RowAlignment);
            }

            // Labels stacked in a column inside a panel are menu rows even though
            // no per-row surface was drawn.
            bool stacked = f.ColumnPeers >= 1 && f.InsideContainer && box.Height <= 44;

            return stacked
                ? Make(candidate, ControlType.MenuItem, 0.7, evidence | ControlEvidence.ColumnAlignment)
                : Make(candidate, ControlType.StaticLabel, 0.18, evidence);
        }

        // --- text without any control surface ------------------------------
        if (f.HasText && !f.HasBorder && !f.HasFlatFill)
        {
            return Make(candidate, ControlType.StaticLabel, 0.15, evidence);
        }

        // --- empty flat box: could be a flat button or a panel strip -------
        if (f.HasFlatFill && !f.ContainsOthers && box.Height <= 48)
        {
            return Make(candidate, ControlType.Button, 0.52, evidence);
        }

        return Make(candidate, ControlType.Unknown, 0.3, evidence);
    }

    /// <summary>
    /// A text-only button has no surface, so no shape detector produces a box for
    /// it. Short lines that no control already covers are added here and judged
    /// by layout alone: a row of similar short lines is a dialog footer or a
    /// toolbar, an isolated line is prose. Detection confidence is deliberately
    /// low (there is no shape), the interaction confidence is not.
    /// </summary>
    public static void AddTextOnlyControls(
        Image32 image,
        IReadOnlyList<Rectangle> textLines,
        List<ControlCandidate> into)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(into);

        if (textLines is null || textLines.Count == 0)
        {
            return;
        }

        var textBoxes = new List<VisualCandidate>();

        foreach (var line in textLines)
        {
            if (line.Width < 14 || line.Height < 8 || line.Width > 320)
            {
                continue;
            }

            var padded = Rectangle.FromLTRB(line.Left - 6, line.Top - 3, line.Right + 6, line.Bottom + 3);

            bool covered = into.Any(control =>
                VisualCandidate.Iou(control.Bounds, padded) >= 0.3 ||
                control.Bounds.Contains(line));

            // Text-line detection can report the same run twice; without this the
            // same label becomes two identical hint targets.
            covered = covered || textBoxes.Any(block =>
                VisualCandidate.Iou(block.Bounds, padded) >= 0.5 || block.Bounds == padded);

            if (covered)
            {
                continue;
            }

            textBoxes.Add(new VisualCandidate(padded, VisualShape.Rectangle, VisualDetector.TextAssociation, 0.45));
        }

        if (textBoxes.Count == 0)
        {
            return;
        }

        // Existing controls are passed as context so grouping evidence can count
        // a text block's neighbours, not just other text blocks.
        var context = new List<VisualCandidate>(into.Count + textBoxes.Count);
        context.AddRange(into.Select(control => new VisualCandidate(
            control.Bounds,
            control.Shape,
            control.Detector,
            control.DetectionConfidence)));
        int offset = context.Count;
        context.AddRange(textBoxes);

        var features = ControlFeatureExtractor.Extract(image, context, textLines);

        for (int i = 0; i < textBoxes.Count; i++)
        {
            var result = Classify(textBoxes[i], features[offset + i]);

            // Only interactive verdicts are added: a paragraph line that resolves
            // to StaticLabel adds nothing but noise to the hint list.
            if (result.IsInteractive)
            {
                into.Add(result);
            }
        }
    }

    private static ControlEvidence Evidence(VisualCandidate candidate, ControlFeatures f)
    {
        var evidence = ControlEvidence.None;

        if (f.HasText) evidence |= ControlEvidence.TextInside;
        if (f.LabelBelow) evidence |= ControlEvidence.LabelBelow;
        if (f.HasBorder) evidence |= ControlEvidence.Border;
        if (f.HasFlatFill) evidence |= ControlEvidence.FlatFill;
        if (candidate.Shape == VisualShape.Icon || candidate.Detector == VisualDetector.Icon) evidence |= ControlEvidence.IconCluster;
        if (candidate.Shape == VisualShape.Underline || candidate.Detector == VisualDetector.Underline) evidence |= ControlEvidence.Underline;
        if (f.RowPeers >= 2 || f.ColumnPeers >= 2) evidence |= ControlEvidence.Repetition;
        if (f.RowPeers >= 1) evidence |= ControlEvidence.RowAlignment;
        if (f.ColumnPeers >= 1) evidence |= ControlEvidence.ColumnAlignment;
        if (f.InsideContainer) evidence |= ControlEvidence.InsideContainer;
        if (f.RoundedCorners) evidence |= ControlEvidence.RoundedCorner;
        if (f.Contrast >= 60) evidence |= ControlEvidence.HighContrast;
        if (f.LowDetail && f.HasFlatFill) evidence |= ControlEvidence.LowInteriorDetail;
        if (f.ThinHorizontal || f.ThinVertical) evidence |= ControlEvidence.ThinBar;

        return evidence;
    }

    private static ControlCandidate Make(
        VisualCandidate candidate,
        ControlType type,
        double interaction,
        ControlEvidence evidence)
    {
        double bonus = 0;

        if (evidence.HasFlag(ControlEvidence.Repetition)) bonus += 0.04;
        if (evidence.HasFlag(ControlEvidence.InsideContainer)) bonus += 0.03;
        if (evidence.HasFlag(ControlEvidence.HighContrast)) bonus += 0.02;
        if (evidence.HasFlag(ControlEvidence.RowAlignment)) bonus += 0.01;

        return new ControlCandidate(
            candidate.Bounds,
            type,
            candidate.Shape,
            candidate.Detector,
            Math.Clamp(candidate.Score, 0, 1),
            Math.Clamp(interaction + bonus, 0, 0.98),
            evidence);
    }
}
