using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Capture;

/// <summary>
/// Pixel features measured for one CV candidate. Densities are fractions in
/// [0, 1] measured on the detector's analysis grid; variation is the standard
/// deviation of greyscale values inside the candidate.
/// </summary>
internal readonly record struct CvFeatures(
    Rectangle Bounds,
    double PerimeterDensity,
    double InteriorDensity,
    double InteriorVariation);

/// <summary>Conservative classification of one CV candidate.</summary>
internal readonly record struct CvClassification(
    TargetType Type,
    double DetectionConfidence,
    double InteractionConfidence,
    double ClassificationConfidence,
    bool IsTextLike);

/// <summary>
/// Classifies pixel candidates as UI controls instead of treating every
/// rectangle as clickable.
///
/// The rules are deliberately conservative because there is no OCR model in
/// the box: a borderless single-line text region is rejected rather than
/// labelled as a button. Bordered controls, filled controls and compact
/// icon-like regions survive. Callers keep the detection confidence separate
/// from the interaction confidence so icon targets can still be ranked lower
/// without being silently discarded.
/// </summary>
internal static class CvTargetClassifier
{
    public static CvClassification? Classify(CvFeatures features)
    {
        Rectangle bounds = features.Bounds;

        if (bounds.Width < 8 || bounds.Height < 6)
        {
            return null;
        }

        double aspect = bounds.Width / (double)Math.Max(bounds.Height, 1);
        int minSide = Math.Min(bounds.Width, bounds.Height);
        double perimeter = Math.Clamp(features.PerimeterDensity, 0, 1);
        double interior = Math.Clamp(features.InteriorDensity, 0, 1);
        double variation = Math.Max(0, features.InteriorVariation);

        // Separators, scrollbar rails and other one-dimensional decorations.
        if (aspect > 20 && bounds.Height <= 20)
        {
            return null;
        }

        // Very large low-aspect regions are containers (cards, panels, images),
        // not controls. Wide text fields and toolbars have a high aspect ratio.
        if (bounds.Width > 400 && bounds.Height > 100 && aspect < 3)
        {
            return null;
        }

        if ((long)bounds.Width * bounds.Height > 60000 && aspect < 2.5)
        {
            return null;
        }

        // Framed but almost blank medium/large regions are cards, panels or
        // group boxes. A real button big enough to match this size has label or
        // icon ink inside, which raises the interior density or variation.
        if (bounds.Width >= 220 &&
            bounds.Height >= 60 &&
            aspect < 6 &&
            perimeter >= 0.10 &&
            interior < 0.06 &&
            variation < 10)
        {
            return null;
        }

        // Text has dense interior edges, high variation and no border. Wide or
        // short single-line regions are labels and paragraphs, not buttons.
        bool textLike = perimeter < 0.18 &&
                        interior >= 0.18 &&
                        variation >= 8;

        if (textLike)
        {
            if (minSide < 16)
            {
                return null;
            }

            if (bounds.Width >= 100 ||
                (bounds.Height <= 34 && aspect >= 1.5))
            {
                return null;
            }
        }

        // Neither an outline/fill nor any interior structure: nothing to click.
        if (perimeter < 0.06 && interior < 0.10)
        {
            return null;
        }

        TargetType type;
        double confidence;

        if (minSide is >= 10 and <= 48 &&
            aspect is >= 0.65 and <= 1.55 &&
            perimeter >= 0.10 &&
            interior <= 0.20)
        {
            // Small square outline with little interior ink: checkbox or radio.
            type = TargetType.CheckBox;
            confidence = 0.58 + Math.Min(0.12, perimeter * 0.25);
        }
        else if (minSide is >= 16 and <= 56 &&
                 aspect is >= 0.60 and <= 1.75 &&
                 interior >= 0.22 &&
                 perimeter < 0.45)
        {
            // Compact, ink-heavy region: icon button.
            type = TargetType.Icon;
            confidence = 0.46 + Math.Min(0.12, interior * 0.20);
        }
        else if (bounds.Height <= 32 &&
                 aspect >= 3.0 &&
                 perimeter >= 0.08 &&
                 interior <= 0.22)
        {
            // Wide, flat, mostly empty box: text field / combo box.
            type = TargetType.Edit;
            confidence = 0.60 + Math.Min(0.10, perimeter * 0.20);
        }
        else if (perimeter >= 0.10)
        {
            type = TargetType.Button;
            confidence = 0.55;
        }
        else if (interior >= 0.10)
        {
            // Borderless button (flat fill, hover background, segmented tab).
            type = TargetType.Button;
            confidence = 0.42;
        }
        else
        {
            return null;
        }

        double detection = Math.Clamp(
            0.38 +
            Math.Min(0.22, perimeter * 0.40) +
            Math.Min(0.12, interior * 0.25) +
            (minSide <= 64 ? 0.04 : 0),
            0.35,
            0.92);

        if (type == TargetType.Button && perimeter >= 0.22)
        {
            detection = Math.Min(0.92, detection + 0.05);
        }

        double interaction = type == TargetType.Icon
            ? Math.Max(0.30, detection - 0.10)
            : detection;

        return new CvClassification(
            type,
            detection,
            interaction,
            Math.Clamp(confidence, 0, 1),
            textLike);
    }
}
