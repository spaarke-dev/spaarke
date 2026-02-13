# Report Card Metric Card - Configuration Guide

> **Component**: GradeMetricCard
> **Location**: `src/client/pcf/VisualHost/control/components/GradeMetricCard.tsx`
> **Utilities**: `src/client/pcf/VisualHost/control/utils/gradeUtils.ts`
> **VisualType**: `ReportCardMetric` (100000010)
> **Project**: matter-performance-KPI-r1
> **Date**: 2026-02-12

---

## Overview

The GradeMetricCard is a VisualHost chart type that displays a letter grade with color-coded styling for matter performance areas. It is configured via `sprk_chartdefinition` records in Dataverse and rendered by the ChartRenderer when `sprk_visualtype` is set to `ReportCardMetric` (100000010).

The component shows:
- A color-coded left border accent (4px)
- An area icon and area name header
- A large hero-sized letter grade (A+ through F, or N/A)
- Contextual text describing the compliance level

---

## Props API Reference

| Prop | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `areaName` | `string` | Yes | -- | Area name displayed as card label: "Guidelines", "Budget", "Outcomes" |
| `areaIcon` | `string` | Yes | -- | Icon key for the area: "guidelines", "budget", "outcomes" |
| `gradeValue` | `number \| null` | Yes | -- | Decimal grade (0.00-1.00) or null for N/A |
| `contextTemplate` | `string` | No | `"You have a {grade}% in {area} compliance"` | Template string with `{grade}` and `{area}` placeholders |
| `colorRules` | `IColorRule[]` | No | Default rules (see below) | Custom color range mappings |
| `onDrillInteraction` | `(interaction: DrillInteraction) => void` | No | -- | Drill-through callback |
| `drillField` | `string` | No | -- | Field name for drill filtering |
| `drillValue` | `unknown` | No | -- | Value for drill filtering (defaults to `areaName` if not set) |
| `interactive` | `boolean` | No | `false` | Enable click interaction (also requires `onDrillInteraction` and `drillField`) |
| `fillContainer` | `boolean` | No | `false` | Fill parent container width |
| `explicitWidth` | `number` | No | -- | Explicit width in pixels |
| `explicitHeight` | `number` | No | -- | Explicit height in pixels |

### IColorRule Interface

```typescript
interface IColorRule {
  range: [number, number]; // Inclusive [min, max] for grade values (0.00-1.00)
  color: "blue" | "yellow" | "red"; // Semantic color name
}
```

---

## Grade Value Mapping

The `gradeValueToLetter` function converts a decimal grade value (0.00-1.00) to a display letter grade. Values are clamped to the 0-1 range. This mapping matches the Dataverse `sprk_grade` choice field.

| Decimal Range | Letter | Display Percentage | Color |
|---------------|--------|-------------------|-------|
| 1.00 | A+ | 100% | Blue |
| 0.95-0.99 | A | 95-99% | Blue |
| 0.90-0.94 | B+ | 90-94% | Blue |
| 0.85-0.89 | B | 85-89% | Blue |
| 0.80-0.84 | C+ | 80-84% | Yellow |
| 0.75-0.79 | C | 75-79% | Yellow |
| 0.70-0.74 | D+ | 70-74% | Yellow |
| 0.65-0.69 | D | 65-69% | Red |
| 0.60-0.64 | F | 60-64% | Red |
| 0.00-0.59 | F | 0-59% | Red |
| null | N/A | N/A | Neutral (grey) |

---

## Default Color Rules

| Range | Semantic Color | Card Background Token | Border Accent Token | Grade Text Token |
|-------|---------------|----------------------|--------------------|--------------------|
| 0.85-1.00 | Blue (brand) | `colorBrandBackground2` | `colorBrandBackground` | `colorBrandForeground1` |
| 0.70-0.84 | Yellow (warning) | `colorPaletteYellowBackground1` | `colorPaletteYellowBorderActive` | `colorPaletteYellowForeground2` |
| 0.00-0.69 | Red (danger) | `colorPaletteRedBackground1` | `colorPaletteRedBorderActive` | `colorPaletteRedForeground1` |
| null | Neutral (grey) | `colorNeutralBackground3` | `colorNeutralStroke1` | `colorNeutralForeground3` |

### Full Token Set Per Color Scheme

Each color scheme resolves to an `IGradeColorTokens` object with six token properties:

| Property | Blue | Yellow | Red | Neutral |
|----------|------|--------|-----|---------|
| `cardBackground` | `colorBrandBackground2` | `colorPaletteYellowBackground1` | `colorPaletteRedBackground1` | `colorNeutralBackground3` |
| `borderAccent` | `colorBrandBackground` | `colorPaletteYellowBorderActive` | `colorPaletteRedBorderActive` | `colorNeutralStroke1` |
| `gradeText` | `colorBrandForeground1` | `colorPaletteYellowForeground2` | `colorPaletteRedForeground1` | `colorNeutralForeground3` |
| `iconColor` | `colorBrandForeground2` | `colorPaletteYellowForeground2` | `colorPaletteRedForeground1` | `colorNeutralForeground3` |
| `contextText` | `colorNeutralForeground2` | `colorNeutralForeground2` | `colorNeutralForeground2` | `colorNeutralForeground3` |
| `labelColor` | `colorNeutralForeground2` | `colorNeutralForeground2` | `colorNeutralForeground2` | `colorNeutralForeground3` |

---

## Dataverse Configuration

### Chart Definition Records (sprk_chartdefinition)

Each card requires a `sprk_chartdefinition` record in Dataverse. These are configured in Phase 3 tasks (030-032).

| Field | Type | Description |
|-------|------|-------------|
| `sprk_name` | String | Area name: "Guidelines", "Budget", or "Outcomes" |
| `sprk_visualtype` | Choice | `100000010` (ReportCardMetric) |
| `sprk_entitylogicalname` | String | `sprk_matter` |
| `sprk_aggregationfield` | String | The grade field to aggregate (see examples below) |
| `sprk_aggregationtype` | Choice | `100000002` (Average) |
| `sprk_configurationjson` | String | JSON configuration (see below) |

### Configuration JSON Examples

**Guidelines Card:**
```json
{
  "icon": "guidelines",
  "contextTemplate": "You have a {grade}% in {area} compliance",
  "colorRules": [
    { "range": [0.85, 1.00], "color": "blue" },
    { "range": [0.70, 0.84], "color": "yellow" },
    { "range": [0.00, 0.69], "color": "red" }
  ]
}
```
- `sprk_aggregationfield`: `sprk_guidelinecompliancegrade_current`

**Budget Card:**
```json
{
  "icon": "budget",
  "contextTemplate": "You have a {grade}% in {area} compliance"
}
```
- `sprk_aggregationfield`: `sprk_budgetcompliancegrade_current`

**Outcomes Card:**
```json
{
  "icon": "outcomes",
  "contextTemplate": "You have a {grade}% in {area} compliance"
}
```
- `sprk_aggregationfield`: `sprk_outcomecompliancegrade_current`

Note: If `colorRules` is omitted from the configuration JSON, the default rules apply (blue for 0.85-1.00, yellow for 0.70-0.84, red for 0.00-0.69).

---

## Template Placeholders

The `contextTemplate` string supports simple `{name}` placeholder substitution via the `resolveContextTemplate` utility.

| Placeholder | Replaced With | Example |
|-------------|--------------|---------|
| `{grade}` | Grade as percentage integer (rounded) | `"95"` |
| `{area}` | Area name from `areaName` prop | `"Guidelines"` |

### Template Examples

| Template | gradeValue | areaName | Result |
|----------|-----------|----------|--------|
| `"You have a {grade}% in {area} compliance"` | 0.95 | Guidelines | "You have a 95% in Guidelines compliance" |
| `"{area} compliance is at {grade}%"` | 0.82 | Budget | "Budget compliance is at 82%" |
| `"You have a {grade}% in {area} compliance"` | null | Outcomes | "No grade data available for Outcomes" |

When `gradeValue` is null, the template is ignored and the component displays: `"No grade data available for {areaName}"`.

---

## Icon Registry

Icons are imported directly from `@fluentui/react-icons` and resolved via the `resolveAreaIcon` utility. The icon key is case-insensitive and trimmed.

| Key | Fluent Icon Component | Description |
|-----|-----------------------|-------------|
| `guidelines` | `GavelRegular` | Rules/legal compliance |
| `budget` | `MoneyRegular` | Financial/budget |
| `outcomes` | `TargetRegular` | Goals/outcomes |
| (unknown key) | `QuestionCircleRegular` | Fallback for unrecognized icon keys |

Icons inherit their color from `IGradeColorTokens.iconColor`, so they automatically match the card's color scheme and adapt to light/dark/high-contrast themes.

### Adding New Icons

To add a new area icon, update the `AREA_ICON_MAP` in `gradeUtils.ts`:

```typescript
import { NewIconRegular } from "@fluentui/react-icons";

const AREA_ICON_MAP: Record<string, React.ComponentType<{ className?: string }>> = {
  guidelines: GavelRegular,
  budget: MoneyRegular,
  outcomes: TargetRegular,
  newarea: NewIconRegular,  // Add new mapping here
};
```

---

## Sizing Modes

The card supports three sizing modes, consistent with the existing MetricCard pattern:

| Mode | Behavior | When Used |
|------|----------|-----------|
| **Default** | `minWidth: 200px`, `minHeight: 120px` (intrinsic sizing) | Standalone usage |
| **fillContainer** | `width: 100%`, fills parent with `aspect-ratio: 5/3` | When placed in CSS grid layout |
| **explicitWidth + explicitHeight** | Exact pixel dimensions, overrides min sizes | ChartRenderer passes PCF container dimensions |

When `explicitWidth` and `explicitHeight` are both provided, they take precedence over `fillContainer`.

---

## Interactivity

A card is interactive (clickable with drill-through) only when ALL of the following are true:
- `interactive` is `true`
- `onDrillInteraction` callback is provided
- `drillField` is set

When interactive, the card:
- Shows a pointer cursor with hover elevation effect (shadow + translateY)
- Has `role="button"` and `tabIndex=0`
- Fires `DrillInteraction` on click, Enter, or Space

When not interactive, the card:
- Has `role="region"` (default)
- Shows default cursor
- Is not focusable via tab

---

## Dark Mode

No manual dark mode handling is needed. All colors use Fluent UI v9 semantic tokens that auto-adapt via `FluentProvider`. The VisualHost's existing `ThemeProvider.ts` detects the current theme (light/dark/high-contrast) and the `FluentProvider` wrapper resolves all `tokens.*` references automatically.

**Do NOT:**
- Use `window.matchMedia("prefers-color-scheme")` in the component
- Conditionally render different tokens for dark vs light mode
- Use CSS custom properties for color values
- Use hard-coded hex color values

---

## Accessibility

- Cards provide `aria-label` with area name, letter grade, and action hint (when interactive)
  - Non-interactive: `"{areaName}: Grade {letterGrade}"`
  - Interactive: `"{areaName}: Grade {letterGrade}. Click to view details."`
- Interactive cards have `role="button"` and `tabIndex=0`
- The grade text element has `aria-live="polite"` for screen reader updates
- Keyboard: Tab to focus, Enter/Space to activate drill-through
- Focus ring provided by Fluent Card component via `tokens.colorStrokeFocus2`
- Color is supplementary -- the grade is always conveyed via letter text and percentage in contextual text

---

## Null/No-Data State

When `gradeValue` is `null` (no assessments exist for the area):

| Element | Behavior |
|---------|----------|
| Icon | Same area icon, colored with neutral grey (`colorNeutralForeground3`) |
| Area Name | Displayed normally, colored neutral grey |
| Grade | Displays `"N/A"` |
| Color Scheme | Neutral: grey background, grey border, grey text |
| Contextual Text | `"No grade data available for {areaName}"` (template is ignored) |
| Interactivity | Non-interactive (card is not clickable) |

---

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| Card shows "N/A" | `gradeValue` is null | Ensure the matter has KPI assessments for the area; check that the calculator endpoint has run |
| Wrong color displayed | `gradeValue` outside expected range or custom `colorRules` misconfigured | Verify `sprk_aggregationfield` returns a decimal 0.00-1.00; check `colorRules` ranges cover full 0-1 spectrum |
| Card not rendering | VisualType not recognized by ChartRenderer | Verify `sprk_visualtype = 100000010` in the chart definition record |
| Template not showing | `contextTemplate` missing from config JSON | Add `"contextTemplate"` to `sprk_configurationjson` |
| Wrong icon displayed | `areaIcon` key not matching registry | Use lowercase values: `"guidelines"`, `"budget"`, `"outcomes"` |
| Card not clickable | Missing `onDrillInteraction` or `drillField` | Ensure ChartRenderer passes both `onDrillInteraction` and `drillField` props |
| Contextual text shows "N/A" instead of percentage | `gradeValue` is null | This is expected behavior; see Null/No-Data State section |
| Colors don't change in dark mode | Hard-coded hex values used instead of tokens | Verify all colors reference `tokens.*` from Fluent UI v9 (already correct in implementation) |

---

## Source Files

| File | Purpose |
|------|---------|
| `src/client/pcf/VisualHost/control/components/GradeMetricCard.tsx` | Main component |
| `src/client/pcf/VisualHost/control/utils/gradeUtils.ts` | Grade conversion, color resolution, template substitution, icon resolution |
| `src/client/pcf/VisualHost/control/types/index.ts` | `VisualType.ReportCardMetric = 100000010` enum value |
| `src/client/pcf/VisualHost/control/components/ChartRenderer.tsx` | Dispatches to `GradeMetricCard` for `ReportCardMetric` visual type |

---

*This guide supports Phase 3 tasks (030-032) for configuring Guidelines, Budget, and Outcomes cards.*
