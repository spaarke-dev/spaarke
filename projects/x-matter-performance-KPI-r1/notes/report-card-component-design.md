# Report Card Metric Card - Component Design Specification

> **Task**: 021 - Design Report Card Metric Card
> **Project**: matter-performance-KPI-r1
> **Date**: 2026-02-12
> **Status**: Complete
> **Implements**: FR-08 (Report Card Metric Cards), FR-11 (Report Card Metric Card Type)
> **Consumed by**: Task 022 (Implement Report Card Metric Card)

---

## 1. Component Architecture

### Where GradeMetricCard Fits in VisualHost

Based on task 020 research, the GradeMetricCard is a **new component** within the VisualHost PCF control (Option B). It lives alongside existing chart components and is dispatched by ChartRenderer via a new VisualType enum value.

```
src/client/pcf/VisualHost/control/
├── components/
│   ├── MetricCard.tsx              # Existing: numeric metric + trend
│   ├── MetricCardMatrix.tsx        # Existing: grid of MetricCards
│   ├── GradeMetricCard.tsx         # NEW: letter grade + color-coded card
│   ├── ChartRenderer.tsx           # MODIFIED: add ReportCardMetric case
│   └── ...
├── types/
│   └── index.ts                    # MODIFIED: add ReportCardMetric enum
└── utils/
    ├── chartColors.ts              # Existing: color palette utilities
    └── gradeUtils.ts               # NEW: grade conversion + color resolution
```

### Component Hierarchy

```
VisualHostRoot
  └── ChartRenderer
        └── case VT.ReportCardMetric:
              └── GradeMetricCard
                    ├── Icon (Fluent UI react-icons)
                    ├── Area Name (Text)
                    ├── Letter Grade (Text, hero size)
                    └── Contextual Text (Text, small)
```

### Registration in VisualType Enum

```typescript
// types/index.ts
export enum VisualType {
  MetricCard        = 100000000,
  BarChart          = 100000001,
  LineChart         = 100000002,
  AreaChart         = 100000003,
  DonutChart        = 100000004,
  StatusBar         = 100000005,
  Calendar          = 100000006,
  MiniTable         = 100000007,
  DueDateCard       = 100000008,
  DueDateCardList   = 100000009,
  ReportCardMetric  = 100000010,  // NEW
}
```

**Dataverse option set**: Add `ReportCardMetric = 100000010` to `sprk_visualtype` global option set.

---

## 2. Props Interface

### IGradeMetricCardProps

```typescript
/**
 * GradeMetricCard displays a letter grade with color-coded styling,
 * an area-specific icon, and contextual compliance text.
 *
 * Used for matter performance Report Card metric cards (FR-08).
 */
export interface IGradeMetricCardProps {
  /** Area name displayed as card label: "Guidelines", "Budget", "Outcomes" */
  areaName: string;

  /**
   * Area icon identifier. Maps to a Fluent UI icon component.
   * If not recognized, falls back to a default icon.
   */
  areaIcon: string;

  /**
   * Numeric grade value as a decimal between 0.00 and 1.00.
   * Null indicates no grade data (new matter, no assessments yet).
   * - 1.00 = A+, 0.95 = A, 0.90 = B+, 0.85 = B
   * - 0.80 = C+, 0.75 = C, 0.70 = D+, 0.65 = D
   * - 0.60 = F, 0.00 = No Grade
   */
  gradeValue: number | null;

  /**
   * Contextual text template with placeholder substitution.
   * Supported placeholders:
   *   {grade} -> numeric percentage (e.g., "95")
   *   {area}  -> area name (e.g., "Guideline")
   * Example: "You have an {grade}% in {area} compliance"
   */
  contextTemplate?: string;

  /**
   * Color rules mapping grade ranges to color schemes.
   * If not provided, uses default rules:
   *   [0.85, 1.00] -> "blue"
   *   [0.70, 0.84] -> "yellow"
   *   [0.00, 0.69] -> "red"
   */
  colorRules?: IColorRule[];

  /** Callback when card is clicked for drill-through */
  onDrillInteraction?: (interaction: DrillInteraction) => void;

  /** Field name for drill interaction */
  drillField?: string;

  /** Value to filter by when drilling */
  drillValue?: unknown;

  /** Whether the card should be interactive (clickable). Default: true */
  interactive?: boolean;

  /** Fill parent container width. Default: false */
  fillContainer?: boolean;

  /** Explicit width in pixels */
  explicitWidth?: number;

  /** Explicit height in pixels */
  explicitHeight?: number;
}
```

### IColorRule

```typescript
/**
 * Maps a numeric grade range to a semantic color scheme.
 */
export interface IColorRule {
  /**
   * Inclusive range [min, max] for grade values.
   * Values are decimals between 0.00 and 1.00.
   */
  range: [number, number];

  /**
   * Semantic color name. Maps to Fluent UI v9 design tokens.
   * - "blue"   -> Brand/positive (A-B grades)
   * - "yellow" -> Warning (C grades)
   * - "red"    -> Danger (D-F grades)
   */
  color: "blue" | "yellow" | "red";
}
```

### Default Color Rules

```typescript
const DEFAULT_COLOR_RULES: IColorRule[] = [
  { range: [0.85, 1.00], color: "blue" },
  { range: [0.70, 0.84], color: "yellow" },
  { range: [0.00, 0.69], color: "red" },
];
```

---

## 3. Grade Value Mapping

### Decimal to Letter Grade

The `gradeValueToLetter` utility converts a decimal grade value (0.00-1.00) to a display letter grade. This mapping matches the Dataverse choice values defined in FR-01.

```typescript
// utils/gradeUtils.ts

/**
 * Convert a decimal grade value (0.00-1.00) to a display letter grade.
 * Returns "N/A" for null values (no assessment data).
 *
 * Mapping (matches Dataverse sprk_grade choice field):
 *   1.00       -> "A+"
 *   0.95-0.99  -> "A"
 *   0.90-0.94  -> "B+"
 *   0.85-0.89  -> "B"
 *   0.80-0.84  -> "C+"
 *   0.75-0.79  -> "C"
 *   0.70-0.74  -> "D+"
 *   0.65-0.69  -> "D"
 *   0.60-0.64  -> "F"
 *   0.00-0.59  -> "F"
 *   null       -> "N/A"
 */
export function gradeValueToLetter(value: number | null): string {
  if (value === null || value === undefined) return "N/A";

  // Clamp to valid range
  const clamped = Math.max(0, Math.min(1, value));

  if (clamped >= 1.00) return "A+";
  if (clamped >= 0.95) return "A";
  if (clamped >= 0.90) return "B+";
  if (clamped >= 0.85) return "B";
  if (clamped >= 0.80) return "C+";
  if (clamped >= 0.75) return "C";
  if (clamped >= 0.70) return "D+";
  if (clamped >= 0.65) return "D";
  return "F";
}
```

### Grade to Percentage

For contextual text substitution, the grade decimal is converted to a display percentage:

```typescript
/**
 * Convert decimal grade (0.00-1.00) to display percentage string.
 * Returns "N/A" for null values.
 *
 * Examples:
 *   0.95 -> "95"
 *   1.00 -> "100"
 *   0.675 -> "68" (rounded to nearest integer)
 *   null -> "N/A"
 */
export function gradeValueToPercent(value: number | null): string {
  if (value === null || value === undefined) return "N/A";
  return Math.round(value * 100).toString();
}
```

### Complete Grade Display Table

| Decimal | Letter | Percent | Color |
|---------|--------|---------|-------|
| 1.00 | A+ | 100% | Blue |
| 0.95 | A | 95% | Blue |
| 0.90 | B+ | 90% | Blue |
| 0.85 | B | 85% | Blue |
| 0.80 | C+ | 80% | Yellow |
| 0.75 | C | 75% | Yellow |
| 0.70 | D+ | 70% | Yellow |
| 0.65 | D | 65% | Red |
| 0.60 | F | 60% | Red |
| 0.00 | F | 0% | Red |
| null | N/A | N/A | Neutral (grey) |

---

## 4. Color Token Mapping

### Semantic Color Tokens (Fluent UI v9)

Per ADR-021, all colors MUST use Fluent UI v9 design tokens. No hard-coded hex values.

The GradeMetricCard uses a **left border accent + subtle background tint** pattern (consistent with how `CalendarVisual.tsx` uses `colorBrandBackground2` for selected state backgrounds).

```typescript
// utils/gradeUtils.ts

import { tokens } from "@fluentui/react-components";

/**
 * Grade color scheme type.
 * "neutral" is used for null/N/A state.
 */
export type GradeColorScheme = "blue" | "yellow" | "red" | "neutral";

/**
 * Token set for a single grade color scheme.
 * Each scheme defines tokens for card background, border accent,
 * grade text, icon tint, and contextual text.
 */
export interface IGradeColorTokens {
  /** Card background (subtle tint) */
  cardBackground: string;
  /** Left border accent color (4px solid) */
  borderAccent: string;
  /** Grade letter text color (large hero text) */
  gradeText: string;
  /** Icon tint color */
  iconColor: string;
  /** Contextual description text color */
  contextText: string;
  /** Area name label color */
  labelColor: string;
}

/**
 * Resolve color tokens for a grade color scheme.
 *
 * Token selection rationale:
 * - Blue: Uses brand tokens for positive/good state (existing pattern in VisualHost)
 * - Yellow: Uses status warning tokens (matches chartColors.ts status palette)
 * - Red: Uses palette red tokens (matches chartColors.ts error palette)
 * - Neutral: Uses neutral tokens for N/A state
 *
 * All tokens auto-adapt to light/dark/high-contrast via FluentProvider.
 */
export function getGradeColorTokens(scheme: GradeColorScheme): IGradeColorTokens {
  switch (scheme) {
    case "blue":
      return {
        cardBackground: tokens.colorBrandBackground2,
        borderAccent: tokens.colorBrandBackground,
        gradeText: tokens.colorBrandForeground1,
        iconColor: tokens.colorBrandForeground2,
        contextText: tokens.colorNeutralForeground2,
        labelColor: tokens.colorNeutralForeground2,
      };
    case "yellow":
      return {
        cardBackground: tokens.colorPaletteYellowBackground1,
        borderAccent: tokens.colorPaletteYellowBorderActive,
        gradeText: tokens.colorPaletteYellowForeground2,
        iconColor: tokens.colorPaletteYellowForeground2,
        contextText: tokens.colorNeutralForeground2,
        labelColor: tokens.colorNeutralForeground2,
      };
    case "red":
      return {
        cardBackground: tokens.colorPaletteRedBackground1,
        borderAccent: tokens.colorPaletteRedBorderActive,
        gradeText: tokens.colorPaletteRedForeground1,
        iconColor: tokens.colorPaletteRedForeground1,
        contextText: tokens.colorNeutralForeground2,
        labelColor: tokens.colorNeutralForeground2,
      };
    case "neutral":
    default:
      return {
        cardBackground: tokens.colorNeutralBackground3,
        borderAccent: tokens.colorNeutralStroke1,
        gradeText: tokens.colorNeutralForeground3,
        iconColor: tokens.colorNeutralForeground3,
        contextText: tokens.colorNeutralForeground3,
        labelColor: tokens.colorNeutralForeground3,
      };
  }
}
```

### Color Resolution from Grade Value

```typescript
/**
 * Resolve the color scheme for a grade value using color rules.
 *
 * @param gradeValue - Decimal grade (0.00-1.00) or null
 * @param colorRules - Array of color rules (optional, uses defaults)
 * @returns GradeColorScheme
 */
export function resolveGradeColorScheme(
  gradeValue: number | null,
  colorRules?: IColorRule[]
): GradeColorScheme {
  if (gradeValue === null || gradeValue === undefined) {
    return "neutral";
  }

  const rules = colorRules || DEFAULT_COLOR_RULES;

  for (const rule of rules) {
    const [min, max] = rule.range;
    if (gradeValue >= min && gradeValue <= max) {
      return rule.color;
    }
  }

  // Fallback: if no rule matches (shouldn't happen with default rules)
  return "red";
}
```

### Token Behavior Across Themes

| Token | Light Mode | Dark Mode | High Contrast |
|-------|-----------|-----------|---------------|
| `colorBrandBackground2` | Light blue tint (#EBF3FC) | Dark blue tint | Adapts |
| `colorBrandBackground` | Brand blue (#0F6CBD) | Lighter brand blue | System accent |
| `colorBrandForeground1` | Brand blue text | Lighter blue text | System text |
| `colorPaletteYellowBackground1` | Pale yellow | Dark yellow tint | Adapts |
| `colorPaletteYellowBorderActive` | Yellow accent | Lighter yellow | System accent |
| `colorPaletteYellowForeground2` | Dark yellow text | Light yellow text | System text |
| `colorPaletteRedBackground1` | Pale red | Dark red tint | Adapts |
| `colorPaletteRedBorderActive` | Red accent | Lighter red | System accent |
| `colorPaletteRedForeground1` | Red text | Light red text | System text |
| `colorNeutralBackground3` | Light grey | Dark grey | System background |
| `colorNeutralForeground3` | Medium grey text | Light grey text | System text |

All tokens are resolved at runtime by FluentProvider and automatically adapt. No manual dark mode branching is needed.

---

## 5. Layout Structure

### Visual Layout

```
+--+--------------------------------------------+
|  |  [Icon]  Area Name                          |  <- Top row: icon + label
|  |                                              |
|  |              A+                              |  <- Center: large letter grade
|  |                                              |
|  |  You have an 95% in Guideline compliance     |  <- Bottom: contextual text
+--+--------------------------------------------+
 ^
 |__ 4px color-coded left border accent
```

### CSS Layout (Griffel makeStyles)

```typescript
const useStyles = makeStyles({
  // Card outer container
  card: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalL,
    gap: tokens.spacingVerticalS,
    minWidth: "180px",
    minHeight: "140px",
    cursor: "default",
    transition: "box-shadow 0.2s ease-in-out, transform 0.2s ease-in-out",
    borderLeftWidth: "4px",
    borderLeftStyle: "solid",
    borderRadius: tokens.borderRadiusMedium,
    position: "relative",
  },

  // Interactive hover state (when drillField is configured)
  cardInteractive: {
    cursor: "pointer",
    "&:hover": {
      boxShadow: tokens.shadow8,
      transform: "translateY(-2px)",
    },
    "&:active": {
      transform: "translateY(0)",
    },
  },

  // Fill parent container width (matches MetricCard pattern)
  cardFillContainer: {
    width: "100%",
    minWidth: "unset",
    minHeight: "unset",
  },

  // Header row: icon + area name
  header: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    width: "100%",
    justifyContent: "center",
  },

  // Area icon
  icon: {
    fontSize: "24px",
    display: "flex",
    alignItems: "center",
  },

  // Area name label
  label: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase300,
  },

  // Large letter grade display
  grade: {
    fontSize: tokens.fontSizeHero900,
    fontWeight: tokens.fontWeightBold,
    lineHeight: tokens.lineHeightHero900,
    textAlign: "center",
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },

  // Contextual text below grade
  contextText: {
    fontSize: tokens.fontSizeBase200,
    textAlign: "center",
    lineHeight: tokens.lineHeightBase200,
  },
});
```

### Responsive Sizing

The card supports three sizing modes (consistent with existing MetricCard):

| Mode | Behavior |
|------|----------|
| **Default** | `minWidth: 180px`, `minHeight: 140px` (intrinsic sizing) |
| **fillContainer** | `width: 100%`, fills parent (used when placed in CSS grid layout) |
| **explicitWidth + explicitHeight** | Exact pixel dimensions (used by ChartRenderer with PCF width/height properties) |

When placed on a Dataverse form, 3 cards typically sit in a horizontal row. The form section width determines card width. VisualHost passes `width` and `height` from the PCF container allocation.

---

## 6. Template Substitution

### Placeholder Syntax

The `contextTemplate` string supports simple placeholder substitution with `{name}` syntax:

| Placeholder | Replaced With | Example |
|-------------|--------------|---------|
| `{grade}` | Grade as percentage integer | `"95"` |
| `{area}` | Area name from `areaName` prop | `"Guideline"` |

### Implementation

```typescript
/**
 * Substitute placeholders in contextual text template.
 *
 * @param template - Template string with {grade} and {area} placeholders
 * @param gradeValue - Decimal grade value (0.00-1.00) or null
 * @param areaName - Area name string
 * @returns Resolved string with placeholders replaced
 *
 * @example
 * resolveContextTemplate(
 *   "You have an {grade}% in {area} compliance",
 *   0.95,
 *   "Guideline"
 * )
 * // Returns: "You have an 95% in Guideline compliance"
 *
 * resolveContextTemplate(
 *   "You have an {grade}% in {area} compliance",
 *   null,
 *   "Guideline"
 * )
 * // Returns: "No grade data available for Guideline"
 */
export function resolveContextTemplate(
  template: string,
  gradeValue: number | null,
  areaName: string
): string {
  if (gradeValue === null || gradeValue === undefined) {
    return `No grade data available for ${areaName}`;
  }

  return template
    .replace(/\{grade\}/g, gradeValueToPercent(gradeValue))
    .replace(/\{area\}/g, areaName);
}
```

### Configuration Examples

```json
// Guidelines card
{
  "contextTemplate": "You have an {grade}% in {area} compliance"
}
// Result: "You have an 95% in Guideline compliance"

// Budget card (alternative wording)
{
  "contextTemplate": "{area} compliance is at {grade}%"
}
// Result: "Budget compliance is at 82%"
```

If `contextTemplate` is not provided in the chart definition config, the card displays no contextual text (the template is optional).

---

## 7. Icon Resolution

### Icon Mapping

The `areaIcon` prop is a string identifier that maps to Fluent UI v9 icon components. The VisualHost does NOT use the `@spaarke/ui-components` SprkIcons library (it's a separate PCF control with its own imports). Icons are imported directly from `@fluentui/react-icons`.

```typescript
// utils/gradeUtils.ts (or inline in GradeMetricCard.tsx)

import * as React from "react";
import {
  GavelRegular,
  MoneyRegular,
  TargetRegular,
  QuestionCircleRegular,
} from "@fluentui/react-icons";

/**
 * Icon registry for grade metric card areas.
 * Maps string identifiers from chart definition config to Fluent icon components.
 *
 * New icons can be added here without modifying component code.
 */
const AREA_ICON_MAP: Record<string, React.ComponentType<{ className?: string }>> = {
  guidelines: GavelRegular,
  budget: MoneyRegular,
  outcomes: TargetRegular,
};

/** Fallback icon when area identifier is not recognized */
const DEFAULT_ICON = QuestionCircleRegular;

/**
 * Resolve an area icon identifier to a React icon component.
 *
 * @param areaIcon - Icon identifier string (case-insensitive)
 * @returns React icon component
 */
export function resolveAreaIcon(
  areaIcon: string
): React.ComponentType<{ className?: string }> {
  const normalized = areaIcon.toLowerCase().trim();
  return AREA_ICON_MAP[normalized] || DEFAULT_ICON;
}
```

### Icon Selection Rationale

| Area | Icon | Package Name | Why |
|------|------|-------------|-----|
| Guidelines | Gavel (24px Regular) | `GavelRegular` | Gavel = rules/legal compliance |
| Budget | Money (24px Regular) | `MoneyRegular` | Money = financial/budget |
| Outcomes | Target (24px Regular) | `TargetRegular` | Target = goals/outcomes |
| Unknown | Question Circle | `QuestionCircleRegular` | Fallback for unrecognized icons |

Icons inherit color from the `IGradeColorTokens.iconColor` token, so they automatically match the card's color scheme and theme mode.

---

## 8. Dark Mode Considerations

### Theme Resolution Chain

The GradeMetricCard inherits theme context from the VisualHost's existing `ThemeProvider.ts` and `FluentProvider` wrapper. No additional dark mode logic is needed in the component itself.

**Theme resolution (from VisualHostRoot)**:
1. `resolveTheme(context)` detects light/dark/high-contrast
2. `FluentProvider theme={resolvedTheme}` wraps entire VisualHost
3. All `tokens.*` references within GradeMetricCard resolve to correct values automatically

### Contrast Requirements (WCAG 2.1 AA)

| Element | Token | Light Contrast | Dark Contrast | Requirement |
|---------|-------|---------------|---------------|-------------|
| Grade text on blue bg | `colorBrandForeground1` on `colorBrandBackground2` | > 4.5:1 | > 4.5:1 | AA Normal |
| Grade text on yellow bg | `colorPaletteYellowForeground2` on `colorPaletteYellowBackground1` | > 4.5:1 | > 4.5:1 | AA Normal |
| Grade text on red bg | `colorPaletteRedForeground1` on `colorPaletteRedBackground1` | > 4.5:1 | > 4.5:1 | AA Normal |
| Context text | `colorNeutralForeground2` on card bg | > 4.5:1 | > 4.5:1 | AA Small |
| Area label | `colorNeutralForeground2` on card bg | > 4.5:1 | > 4.5:1 | AA Small |

Fluent UI v9 tokens are designed to meet AA contrast requirements in all theme modes. By using semantic tokens (not hard-coded colors), the component automatically inherits correct contrast ratios.

### High Contrast Mode

In high contrast mode (`teamsHighContrastTheme`), all palette tokens resolve to system colors. The `borderLeftColor` accent becomes the system highlight color. Background tints resolve to system background. This is handled entirely by FluentProvider.

### What NOT to Do

- Do NOT use `window.matchMedia("prefers-color-scheme")` in the component (already handled by ThemeProvider)
- Do NOT conditionally render different tokens for dark vs light mode
- Do NOT use CSS custom properties for color values (use only `tokens.*`)
- Do NOT use opacity-based overlays for tinting (use semantic Background tokens)

---

## 9. VisualHost Integration

### ChartRenderer Switch Case

```typescript
// In ChartRenderer.tsx, add after the DueDateCardList case:

case VT.ReportCardMetric: {
  // Parse grade-specific config from sprk_configurationjson
  const gradeField = config.gradeField as string | undefined;
  const areaIcon = (config.icon as string) || "outcomes";
  const contextTemplate = config.contextTemplate as string | undefined;
  const colorRulesConfig = config.colorRules as IColorRule[] | undefined;

  // Grade value source:
  // Option A: From aggregated data points (if sprk_aggregationfield is set)
  // Option B: From config JSON (if gradeField maps to a form field value)
  // For R1 MVP: data comes from sprk_{area}compliancegrade_current via
  // DataAggregationService (single data point aggregation on the field)
  const gradeValue = dataPoints.length > 0
    ? dataPoints[0].value / 100  // Convert percentage to decimal if needed
    : null;

  return (
    <GradeMetricCard
      areaName={sprk_name}
      areaIcon={areaIcon}
      gradeValue={gradeValue}
      contextTemplate={contextTemplate}
      colorRules={colorRulesConfig}
      onDrillInteraction={onDrillInteraction}
      drillField={drillField}
      drillValue={dataPoints.length > 0 ? dataPoints[0].fieldValue : null}
      interactive={!!onDrillInteraction}
      fillContainer
      explicitWidth={width}
      explicitHeight={height}
    />
  );
}
```

### No-Data Handling in ChartRenderer

The `ReportCardMetric` type should be added to the "no data needed" bypass list (similar to DueDateCard), since the component handles null grade values gracefully:

```typescript
// In ChartRenderer.tsx no-data check, add ReportCardMetric:
if (
  sprk_visualtype !== VT.MetricCard
  && sprk_visualtype !== VT.DueDateCard
  && sprk_visualtype !== VT.DueDateCardList
  && sprk_visualtype !== VT.ReportCardMetric  // NEW: handles null state internally
) {
  return <div className={styles.placeholder}>...</div>;
}
```

### getVisualTypeName Update

```typescript
// Add to the switch in getVisualTypeName():
case VT.ReportCardMetric:
  return "Report Card Metric";
```

### Dataverse Configuration Records

Three `sprk_chartdefinition` records need to be created in Dataverse (in task 030/031/032):

**Guidelines Card**:
```json
{
  "sprk_name": "Guidelines",
  "sprk_visualtype": 100000010,
  "sprk_entitylogicalname": "sprk_matter",
  "sprk_aggregationfield": "sprk_guidelinecompliancegrade_current",
  "sprk_aggregationtype": 100000002,
  "sprk_configurationjson": "{\"icon\": \"guidelines\", \"contextTemplate\": \"You have an {grade}% in {area} compliance\", \"colorRules\": [{\"range\": [0.85, 1.00], \"color\": \"blue\"}, {\"range\": [0.70, 0.84], \"color\": \"yellow\"}, {\"range\": [0.00, 0.69], \"color\": \"red\"}]}"
}
```

**Budget Card**:
```json
{
  "sprk_name": "Budget",
  "sprk_visualtype": 100000010,
  "sprk_entitylogicalname": "sprk_matter",
  "sprk_aggregationfield": "sprk_budgetcompliancegrade_current",
  "sprk_aggregationtype": 100000002,
  "sprk_configurationjson": "{\"icon\": \"budget\", \"contextTemplate\": \"You have an {grade}% in {area} compliance\", \"colorRules\": [{\"range\": [0.85, 1.00], \"color\": \"blue\"}, {\"range\": [0.70, 0.84], \"color\": \"yellow\"}, {\"range\": [0.00, 0.69], \"color\": \"red\"}]}"
}
```

**Outcomes Card**:
```json
{
  "sprk_name": "Outcomes",
  "sprk_visualtype": 100000010,
  "sprk_entitylogicalname": "sprk_matter",
  "sprk_aggregationfield": "sprk_outcomecompliancegrade_current",
  "sprk_aggregationtype": 100000002,
  "sprk_configurationjson": "{\"icon\": \"outcomes\", \"contextTemplate\": \"You have an {grade}% in {area} compliance\", \"colorRules\": [{\"range\": [0.85, 1.00], \"color\": \"blue\"}, {\"range\": [0.70, 0.84], \"color\": \"yellow\"}, {\"range\": [0.00, 0.69], \"color\": \"red\"}]}"
}
```

---

## 10. Null/No-Data State

### When gradeValue is null

This occurs when:
- Matter has no KPI assessments yet (new matter)
- The specific performance area has no assessments
- Data fetch returned no results

### Null State Rendering

```
+--+--------------------------------------------+
|  |  [?]  Guidelines                             |  <- Icon: QuestionCircle (neutral)
|  |                                              |
|  |             N/A                              |  <- Grade: "N/A" in grey
|  |                                              |
|  |  No grade data available for Guidelines      |  <- Null-specific text
+--+--------------------------------------------+
 ^
 |__ 4px neutral grey border (colorNeutralStroke1)
```

| Element | Null Behavior |
|---------|--------------|
| **Icon** | Same area icon, but colored with `colorNeutralForeground3` (grey) |
| **Area Name** | Normal text, colored `colorNeutralForeground3` |
| **Grade** | Displays "N/A" string |
| **Color** | `neutral` scheme: grey background, grey border, grey text |
| **Contextual Text** | "No grade data available for {areaName}" (ignores template) |
| **Interactivity** | Non-interactive (no click handler fires) |

### Implementation in Component

```typescript
// Inside GradeMetricCard render:
const letterGrade = gradeValueToLetter(gradeValue);
const colorScheme = resolveGradeColorScheme(gradeValue, colorRules);
const colorTokens = getGradeColorTokens(colorScheme);

const contextualText = gradeValue !== null && contextTemplate
  ? resolveContextTemplate(contextTemplate, gradeValue, areaName)
  : gradeValue === null
    ? `No grade data available for ${areaName}`
    : undefined;

// Card is non-interactive when there is no grade data
const isInteractive = interactive
  && gradeValue !== null
  && onDrillInteraction
  && drillField;
```

---

## 11. Accessibility

### ARIA Attributes

```typescript
<Card
  role={isInteractive ? "button" : undefined}
  tabIndex={isInteractive ? 0 : undefined}
  aria-label={
    isInteractive
      ? `${areaName}: Grade ${letterGrade}. ${contextualText || ""}. Click to view details.`
      : `${areaName}: Grade ${letterGrade}. ${contextualText || ""}`
  }
  onClick={isInteractive ? handleClick : undefined}
  onKeyDown={isInteractive ? handleKeyDown : undefined}
>
```

### Keyboard Navigation

- **Tab**: Focuses the card (when interactive)
- **Enter / Space**: Triggers drill interaction (when interactive)
- **Focus visible**: Fluent Card component provides focus ring via `tokens.colorStrokeFocus2`

### Screen Reader Announcements

The `aria-label` provides a complete description:
- Area name (e.g., "Guidelines")
- Letter grade (e.g., "Grade A+")
- Contextual text (e.g., "You have an 95% in Guideline compliance")
- Action hint (e.g., "Click to view details") when interactive

### Color Independence

Grade information is conveyed through:
1. **Letter grade** (text, not color-dependent)
2. **Percentage** (in contextual text)
3. **Color coding** (supplementary, not sole indicator)

This ensures colorblind users can understand the grade from text alone.

---

## 12. Component Render Logic (Pseudo-code)

```typescript
export const GradeMetricCard: React.FC<IGradeMetricCardProps> = ({
  areaName,
  areaIcon,
  gradeValue,
  contextTemplate,
  colorRules,
  onDrillInteraction,
  drillField,
  drillValue,
  interactive = true,
  fillContainer = false,
  explicitWidth,
  explicitHeight,
}) => {
  const styles = useStyles();

  // 1. Resolve display values
  const letterGrade = gradeValueToLetter(gradeValue);
  const colorScheme = resolveGradeColorScheme(gradeValue, colorRules);
  const colorTokens = getGradeColorTokens(colorScheme);
  const IconComponent = resolveAreaIcon(areaIcon);

  // 2. Resolve contextual text
  const contextualText = gradeValue !== null && contextTemplate
    ? resolveContextTemplate(contextTemplate, gradeValue, areaName)
    : gradeValue === null
      ? `No grade data available for ${areaName}`
      : undefined;

  // 3. Determine interactivity
  const isInteractive = interactive
    && gradeValue !== null
    && !!onDrillInteraction
    && !!drillField;

  const hasExplicitDimensions = explicitWidth != null && explicitHeight != null;

  // 4. Drill interaction handlers (same pattern as MetricCard)
  const handleClick = () => {
    if (isInteractive && onDrillInteraction && drillField) {
      onDrillInteraction({
        field: drillField,
        operator: "eq",
        value: drillValue ?? gradeValue,
        label: areaName,
      });
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (isInteractive && (e.key === "Enter" || e.key === " ")) {
      e.preventDefault();
      handleClick();
    }
  };

  // 5. Render
  return (
    <Card
      className={mergeClasses(
        styles.card,
        isInteractive && styles.cardInteractive,
        fillContainer && !hasExplicitDimensions && styles.cardFillContainer,
      )}
      style={{
        backgroundColor: colorTokens.cardBackground,
        borderLeftColor: colorTokens.borderAccent,
        ...(hasExplicitDimensions ? {
          width: `${explicitWidth}px`,
          height: `${explicitHeight}px`,
          minWidth: "unset",
          minHeight: "unset",
        } : {}),
      }}
      onClick={isInteractive ? handleClick : undefined}
      onKeyDown={isInteractive ? handleKeyDown : undefined}
      tabIndex={isInteractive ? 0 : undefined}
      role={isInteractive ? "button" : undefined}
      aria-label={`${areaName}: Grade ${letterGrade}. ${contextualText || ""}${isInteractive ? " Click to view details." : ""}`}
    >
      {/* Header: icon + area name */}
      <div className={styles.header}>
        <IconComponent
          className={styles.icon}
          style={{ color: colorTokens.iconColor }}
        />
        <Text
          className={styles.label}
          style={{ color: colorTokens.labelColor }}
        >
          {areaName}
        </Text>
      </div>

      {/* Letter grade (large) */}
      <Text
        className={styles.grade}
        style={{ color: colorTokens.gradeText }}
      >
        {letterGrade}
      </Text>

      {/* Contextual text */}
      {contextualText && (
        <Text
          className={styles.contextText}
          style={{ color: colorTokens.contextText }}
        >
          {contextualText}
        </Text>
      )}
    </Card>
  );
};
```

---

## 13. File Inventory (Task 022 Deliverables)

| File | Action | Description |
|------|--------|-------------|
| `control/components/GradeMetricCard.tsx` | **CREATE** | New component (main deliverable) |
| `control/utils/gradeUtils.ts` | **CREATE** | Grade conversion, color resolution, template substitution |
| `control/types/index.ts` | **MODIFY** | Add `ReportCardMetric = 100000010` to VisualType enum |
| `control/components/ChartRenderer.tsx` | **MODIFY** | Add `case VT.ReportCardMetric:` dispatch block |
| `control/components/__tests__/GradeMetricCard.test.tsx` | **CREATE** | Unit tests |
| `stories/GradeMetricCard.stories.tsx` | **CREATE** | Storybook stories for all variants |

### Testing Requirements

| Test Category | Test Cases |
|--------------|------------|
| **Rendering** | Renders with all grade values (A+ through F), renders "N/A" for null, renders area name and icon, renders contextual text |
| **Color coding** | Blue for 0.85-1.00, Yellow for 0.70-0.84, Red for 0.00-0.69, Neutral for null |
| **Template substitution** | {grade} replaced with percentage, {area} replaced with area name, null grade shows fallback text |
| **Grade conversion** | 1.00=A+, 0.95=A, 0.90=B+, 0.85=B, 0.80=C+, 0.75=C, 0.70=D+, 0.65=D, 0.60=F, null=N/A |
| **Interaction** | Click fires DrillInteraction, keyboard Enter/Space fires drill, non-interactive when no drillField, non-interactive when gradeValue is null |
| **Theme** | Renders in light theme, renders in dark theme |
| **Custom color rules** | Respects custom IColorRule array, falls back to defaults when not provided |

### Storybook Stories

| Story | Description |
|-------|-------------|
| `Default` | Guidelines card with A grade (0.95) |
| `BudgetYellow` | Budget card with C+ grade (0.80) |
| `OutcomesRed` | Outcomes card with D grade (0.65) |
| `NullGrade` | Card with null grade value (N/A state) |
| `APlusGrade` | Perfect score (1.00) |
| `FailingGrade` | F grade (0.60) |
| `ThreeCardRow` | All 3 area cards side by side (as they appear on the form) |
| `Interactive` | Card with drill-through enabled |
| `NonInteractive` | Card without drill-through |
| `CustomTemplate` | Card with custom contextual text template |

---

## 14. Design Decisions Summary

| Decision | Choice | Rationale |
|----------|--------|-----------|
| New component vs extend MetricCard | New `GradeMetricCard` | Different layout, zero regression risk (Task 020 recommendation) |
| Color application method | Left border accent + subtle background tint | Matches existing VisualHost patterns (CalendarVisual); not overwhelming |
| Token selection | `*Background1` for card bg, `*BorderActive` for accent | Background1 is subtler than Background3; BorderActive provides strong accent |
| Icon source | Direct `@fluentui/react-icons` import | VisualHost is a separate PCF, does not consume `@spaarke/ui-components` for icons |
| Null state display | "N/A" with neutral grey scheme | Clear indication of missing data; non-interactive prevents confusing drill-throughs |
| Template substitution | Simple `{name}` replacement | Matches spec requirement; extensible for future placeholders |
| Grade boundary handling | `>= min && <= max` inclusive | Matches Dataverse choice field decimal values exactly |
| Grade below 0.60 | Maps to "F" | Spec defines 0.60 as F; anything below is still failing |
| Utility file location | `control/utils/gradeUtils.ts` | Co-located with VisualHost; if reused later, promote to shared library per ADR-012 |

---

*End of design specification. This document feeds directly into Task 022 (Implement Report Card Metric Card).*
