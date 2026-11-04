# Mouse Keyboard CV Detection Dataset

## Purpose

Structured evaluation dataset for improving real-world generalization of the Mouse Keyboard Computer Vision hint detector.

## NOTICE

Do not use this dataset for training models. It is for evaluation and failure analysis only.

## Dataset Structure

	datasets/
	  annotation-schema.json
	  README.md
	  train/annotations.json  (8 annotations)
	  val/annotations.json     (4 annotations)
	  test/annotations.json   (5 annotations)

## Split Strategy

- **Training set**: Development set for understanding failure modes (not for model training)
- **Validation set**: Parameter tuning and threshold validation
- **Test set**: Held-out evaluation (never seen during development)

## Grouping Rule

Screenshots from the same application are grouped together within a single split to reduce data leakage.

## Annotation Schema

See `annotation-schema.json` for the full JSON schema.

### Key Fields

- screenshotId: Unique identifier for the screenshot
- targetId: Unique identifier for the target within the screenshot
- targetType: The type of UI control (see categories below)
- bbox: Bounding box [x, y, width, height] in screen coordinates
- clickableRegion: Valid clickable region [x, y, width, height]
- interactive: Whether the target is interactive
- text: Visible text on the control, if any
- annotationConfidence: Human annotator confidence (0-1)
- difficultCaseCategory: Category of difficult case (if applicable)

## Categories

### Standard Controls

- rectangular-button, rounded-button, filled-button, outlined-button
- text-button, text-field, checkbox, radio-button
- tab, link, dropdown

### Difficult Controls

- icon-only-button, borderless-button, transparent-button
- small-button, low-contrast-button, custom-drawn-control
- control-without-ocr-text, control-with-unusual-shape
- control-with-shadow, control-with-complex-background

### Complex Layouts

- dense-toolbar, navigation-bar, sidebar, nested-panels
- card, table, menu, context-menu, popup, dialog
- overlapping-windows

### Visual Conditions

- light-theme, dark-theme, high-resolution, different-dpi
- different-scaling, different-application-styles
- different-font-sizes, different-zoom-levels

## Hard Negatives (elements that should NOT be detected)

- decorative-icon, non-clickable-text, separator, border
- background-shape, non-interactive-card, static-image

## Data Quality Rules

1. Do not generate fake ground truth from the current detector
2. Ground truth must be independently verified
3. Avoid using the same screenshots for both threshold tuning and final evaluation
4. Split data into development/validation/test sets
5. Group similar screenshots from the same application together when splitting
6. Use only safe test screenshots (no passwords, personal messages, or private information)
7. Do not upload screenshots to external services

## Adding New Annotations

1. Capture a new screenshot (safe content only)
2. Annotate targets using the annotation format
3. Place the screenshot in the appropriate split directory
4. Update the annotations file
5. Regenerate dataset statistics

## Generated Screenshots

The current dataset contains screenshots of the following applications:

- Calculator (light theme)
- Chrome (light theme)
- Settings (dark theme)
- Terminal (dark theme)

Sensitive content has been removed or obscured. No passwords, personal messages, or private account information is included.

## Citation

If you use this dataset in your research, please cite:

```
@dataset{mousekeyboard-cv-2026,
  title={Mouse Keyboard CV Detection Dataset},
  author={OpenCode},
  year={2026},
  note={For evaluation of computer vision hint detection},
  url={https://github.com/khoa-nhd/mouse-keyboard}
}
```