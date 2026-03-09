# VisualHost Card Types Research Report

> **Task**: 020 - Research VisualHost Card Types
> **Project**: matter-performance-KPI-r1
> **Date**: 2026-02-12
> **Purpose**: Determine if Report Card metric card can extend existing VisualHost component or requires a new card type

---

## 1. Directory Structure

### VisualHost PCF Control Location

The VisualHost PCF control lives at:

```
src/client/pcf/VisualHost/
├── control/
│   ├── index.ts                          # PCF entry point (React 16 APIs)
│   ├── ControlManifest.Input.xml         # v1.2.29, platform React 16.14.0 + Fluent 9.46.2
│   ├── components/
│   │   ├── VisualHostRoot.tsx            # Main React root: loads chart definition, renders ChartRenderer
│   │   ├── ChartRenderer.tsx             # Central switch: maps VisualType enum → component
│   │   ├── MetricCard.tsx                # Single metric card (value + label + trend)
│   │   ├── MetricCardMatrix.tsx          # Grid of MetricCards from grouped data
│   │   ├── BarChart.tsx                  # Vertical/horizontal bar chart
│   │   ├── LineChart.tsx                 # Line/area chart
│   │   ├── DonutChart.tsx                # Donut/pie chart
│   │   ├── StatusDistributionBar.tsx     # Horizontal status bar
│   │   ├── CalendarVisual.tsx            # Calendar heat map
│   │   ├── MiniTable.tsx                 # Compact ranked table
│   │   ├── DueDateCard.tsx               # Single event due date card
│   │   ├── DueDateCardList.tsx           # List of due date cards
│   │   └── ErrorBoundary.tsx             # Error boundary wrapper
│   ├── providers/
│   │   └── ThemeProvider.ts              # Light/dark/high-contrast detection
│   ├── services/
│   │   ├── ConfigurationLoader.ts        # Loads sprk_chartdefinition from Dataverse
│   │   ├── DataAggregationService.ts     # FetchXML + aggregation
│   │   ├── ClickActionHandler.ts         # Click action dispatch
│   │   └── ViewDataService.ts            # View data fetching
│   ├── types/
│   │   └── index.ts                      # VisualType enum, IChartDefinition, interfaces
│   └── utils/
│       ├── chartColors.ts                # Fluent token-based color palettes
│       └── logger.ts                     # Logging utility
├── stories/                              # Storybook stories for all visual types
├── Solution/                             # Dataverse solution packaging
├── package.json
├── tsconfig.json
└── jest.config.js
```

**Note**: There is no `src/client/shared/visual-host/` directory. The VisualHost is a PCF control, not a shared library component.

### Shared Component Library

Relevant shared components at `src/client/shared/Spaarke.UI.Components/`:
- `EventDueDateCard` - Used by VisualHost's DueDateCard visual (imported from shared library)
- `DatasetGrid/CardView.tsx` - Generic card view for dataset grids (not metric cards)
- Theme utilities: `themeDetection.ts`, `themeStorage.ts`
- Icons: `SprkIcons.tsx`

---

## 2. Existing Card Types

### VisualType Enum (Dataverse Option Set)

```typescript
enum VisualType {
  MetricCard       = 100000000,  // Single aggregate value + trend
  BarChart         = 100000001,
  LineChart        = 100000002,
  AreaChart        = 100000003,
  DonutChart       = 100000004,
  StatusBar        = 100000005,
  Calendar         = 100000006,
  MiniTable        = 100000007,
  DueDateCard      = 100000008,  // Single event card
  DueDateCardList  = 100000009,  // List of event cards
}
```

### MetricCard Component (Current)

**File**: `src/client/pcf/VisualHost/control/components/MetricCard.tsx`

**Props Interface** (`IMetricCardProps`):

| Prop | Type | Description |
|------|------|-------------|
| `value` | `string \| number` | Main metric value |
| `label` | `string` | Label text |
| `description` | `string?` | Subtitle text |
| `trend` | `"up" \| "down" \| "neutral"?` | Trend indicator |
| `trendValue` | `number?` | Percentage change |
| `onDrillInteraction` | `(DrillInteraction) => void?` | Drill callback |
| `drillField` | `string?` | Field for drill filter |
| `drillValue` | `unknown?` | Filter value |
| `interactive` | `boolean` | Clickable (default: true) |
| `compact` | `boolean` | Compact mode |
| `fillContainer` | `boolean` | Fill parent width |
| `justification` | `"left" \| "center" \| "right"` | Alignment |
| `explicitWidth` | `number?` | Explicit px width |
| `explicitHeight` | `number?` | Explicit px height |

**Rendering**: Label at top, large numeric value (Hero 800 font), optional trend arrow + percentage, optional description text below.

**Styling**: Uses Fluent v9 tokens throughout (`tokens.colorNeutralForeground1`, etc.). All colors are token-based. Dark mode supported via FluentProvider theme resolution.

### MetricCardMatrix Component

Renders multiple MetricCards in a CSS Grid layout. Uses `IAggregatedDataPoint[]` data. Same styling approach as MetricCard.

### DueDateCard / EventDueDateCard

These are completely different card types: they display event date, title, urgency badge. Irrelevant to Report Card needs.

### DrillThroughWorkspace MetricCard

**File**: `src/client/pcf/DrillThroughWorkspace/control/components/charts/MetricCard.tsx`

This is an older copy of the VisualHost MetricCard without `fillContainer`, `justification`, or `explicitWidth/Height` props. Functionally identical otherwise.

---

## 3. Capabilities Assessment

### Required Features vs. Existing MetricCard

| Required Feature | Existing MetricCard Support | Gap |
|-----------------|---------------------------|-----|
| **Area icon** (Guidelines, Budget, Outcomes) | NO - No icon prop or icon rendering | MISSING |
| **Large letter grade** (A+, B, C) center-aligned | PARTIAL - Supports `value: string` and `justification: "center"` | Grade is just a string value, but formatting is for numeric values |
| **Color coding by grade range** (blue/yellow/red) | NO - Card background/border is neutral theme color only | MISSING - No `colorScheme` or conditional color prop |
| **Contextual text below grade** | PARTIAL - Has `description` prop | WORKS - Can pass formatted text |
| **Dark mode support** | YES - All tokens from Fluent v9 | WORKS |
| **Responsive layout** | YES - `fillContainer` + `aspectRatio` | WORKS |
| **Click interaction** | YES - `onDrillInteraction` with drill field/value | WORKS |
| **Compact mode** | YES - `compact` prop | WORKS |

### Detailed Gap Analysis

#### Gap 1: No Icon Support

The existing MetricCard renders: `Label -> Value -> Trend -> Description`. There is no `icon` prop, no icon rendering slot, and no area for an icon in the layout. The Report Card needs an area icon (e.g., a Fluent icon for Guidelines, Budget, Outcomes) displayed prominently.

**Impact**: Layout needs a new visual element (icon above or beside the grade).

#### Gap 2: No Color Coding / Conditional Styling

The existing MetricCard uses a neutral `Card` component with no background color variation. Colors are:
- Background: Inherits from Fluent `Card` (neutral)
- Value text: `tokens.colorNeutralForeground1` (always)
- Label text: `tokens.colorNeutralForeground2` (always)
- Trend: Green (up) or Red (down) via semantic palette tokens

The Report Card requires the card itself to be color-coded:
- **Blue** background/accent for A-B grades (0.85-1.00)
- **Yellow** background/accent for C grades (0.70-0.84)
- **Red** background/accent for D-F grades (0.00-0.69)

This requires conditional styling based on the grade value, using semantic color tokens (not hard-coded hex values per ADR-021).

**Impact**: Requires new props for `gradeValue` (numeric), color resolution logic, and conditional card styling.

#### Gap 3: Grade Display Formatting

The existing `formatValue()` function is designed for numbers (1000 -> 1K). For letter grades like "A+", "B", "C", the value would need to be passed as a string, bypassing `formatValue`. The font size (`fontSizeHero800`) and center alignment work well for letter grades.

**Impact**: Minor - pass value as string, works correctly with existing rendering path.

---

## 4. Configuration System Analysis

### How VisualHost Cards Are Configured

The VisualHost uses a **Dataverse-driven configuration** model:

1. A `sprk_chartdefinition` record is created in Dataverse
2. The record specifies `sprk_visualtype` (the VisualType enum value)
3. Additional configuration goes in `sprk_optionsjson` or `sprk_configurationjson` (JSON strings)
4. `ConfigurationLoader.ts` fetches the record and maps to `IChartDefinition`
5. `VisualHostRoot.tsx` passes config to `ChartRenderer.tsx`
6. `ChartRenderer.tsx` switches on `sprk_visualtype` and renders the appropriate component

### Adding a New Visual Type

To add a new visual type, the following changes are needed:

1. **Dataverse**: Add new option to `sprk_visualtype` option set (e.g., `ReportCardMetric = 100000010`)
2. **Types**: Add `ReportCardMetric` to `VisualType` enum in `types/index.ts`
3. **Component**: Create new component (e.g., `GradeMetricCard.tsx`)
4. **ChartRenderer**: Add new `case VT.ReportCardMetric:` block
5. **ConfigurationLoader**: No changes needed (already loads generic JSON config)
6. **Storybook**: Add stories for the new component
7. **Tests**: Add unit tests

### Extending MetricCard Instead

If extending MetricCard, these changes would be needed:

1. **Add props**: `icon`, `colorScheme` (or `gradeValue` + color logic), possibly `variant: "standard" | "grade"`
2. **Modify render**: Add icon slot, conditional card background/border styling
3. **Update ChartRenderer**: Add logic in `case VT.MetricCard:` to detect grade mode from config JSON
4. **Risk**: Increases complexity of a stable, well-tested component; risks regressions for all existing MetricCard deployments

---

## 5. Theme and Color Token Analysis

### Available Fluent v9 Semantic Color Tokens for Grade Coding

Per ADR-021 (MUST use design tokens, MUST NOT hard-code colors), the grade color coding should use:

**Blue (A-B grades: 0.85-1.00)**:
- Background: `tokens.colorPaletteBlueBorderActive` or `tokens.colorBrandBackground`
- Foreground: `tokens.colorNeutralForegroundOnBrand` (white text on brand)

**Yellow (C grades: 0.70-0.84)**:
- Background: `tokens.colorPaletteYellowBackground3` or `tokens.colorStatusWarningBackground2`
- Foreground: `tokens.colorStatusWarningForeground1`

**Red (D-F grades: 0.00-0.69)**:
- Background: `tokens.colorPaletteRedBackground3` or `tokens.colorStatusDangerBackground2`
- Foreground: `tokens.colorStatusDangerForeground1`

### Dark Mode Behavior

All tokens above automatically adapt in dark mode via FluentProvider. The existing `ThemeProvider.ts` handles:
- localStorage user preference
- Power Apps context detection
- DOM navbar fallback
- System `prefers-color-scheme` media query

The `chartColors.ts` utility already provides a `getStatusColorPalette()` function with semantic status mappings. The grade color coding pattern follows the same approach used by `EventDueDateCard` (urgency-based background colors using CSS custom properties).

---

## 6. Recommendation

### Option B: Create New Card Type (GradeMetricCard)

**Recommended approach**: Create a **new `GradeMetricCard` component** as a separate visual type rather than extending the existing MetricCard.

### Rationale

1. **Single Responsibility**: The existing MetricCard is a general-purpose numeric metric display. Report Card grades are a fundamentally different visual pattern (icon + letter grade + color-coded card + contextual text). Merging these concerns into one component would violate single responsibility and create a fragile multi-purpose component.

2. **Risk Mitigation**: MetricCard is deployed across multiple Dataverse forms (v1.2.29 with 29 versions of iteration). Modifying it risks regressions in all existing deployments. A new component has zero regression risk.

3. **Layout Difference**: The Report Card layout is structurally different:
   - **MetricCard**: `[Label] → [Large Number] → [Trend Arrow] → [Description]`
   - **GradeMetricCard**: `[Icon] → [Large Letter Grade] → [Contextual Text]` with color-coded card

4. **Color Coding**: The existing MetricCard has no concept of conditional card styling. Adding `colorScheme` logic to MetricCard would add complexity that only benefits the grade use case. A new component keeps this logic isolated.

5. **Configuration Clean Separation**: A new `VisualType.ReportCardMetric` enum value makes configuration intent explicit. Operators creating chart definitions in Dataverse will see "Report Card Metric" as a distinct option rather than needing to configure a MetricCard with special JSON options to enable grade mode.

6. **ADR-012 Compliance**: If the GradeMetricCard proves useful in other projects, it can later be promoted to the shared library (`@spaarke/ui-components`). Starting as a VisualHost-local component follows the "keep in module when experimental" guidance from ADR-012.

### Why NOT Option A (Extend Existing)

- Would require adding `icon`, `gradeValue`, `colorScheme`, and `variant` props to MetricCard
- Would create conditional rendering paths (`if variant === "grade"`) that complicate the component
- Regression risk for 10+ existing VisualHost MetricCard deployments
- ChartRenderer would need complex config-sniffing logic to detect "grade mode"
- Testing burden increases on existing MetricCard test suite

---

## 7. Implementation Estimate

### New Component: GradeMetricCard

| Task | Estimate | Notes |
|------|----------|-------|
| 1. Add `ReportCardMetric` to VisualType enum + Dataverse option set | 0.5 hr | `types/index.ts` + Dataverse config |
| 2. Create `GradeMetricCard.tsx` component | 2 hr | New component with icon, grade, color, contextual text |
| 3. Add grade-to-color mapping utility | 0.5 hr | In `chartColors.ts` or new `gradeColors.ts` |
| 4. Add grade-to-letter conversion utility | 0.5 hr | Numeric value -> "A+", "A", "B+", etc. |
| 5. Wire into `ChartRenderer.tsx` switch | 0.5 hr | New case block |
| 6. Unit tests | 1 hr | Theme tests, color coding tests, click interaction |
| 7. Storybook stories | 0.5 hr | All grade variants, dark mode |
| 8. Create 3 `sprk_chartdefinition` records in Dataverse | 0.5 hr | One per area |
| **Total** | **~6 hr** | |

### If Extending MetricCard Instead (Not Recommended)

| Task | Estimate | Notes |
|------|----------|-------|
| 1. Add props to MetricCard | 1 hr | `icon`, `gradeValue`, `colorScheme`, `variant` |
| 2. Modify MetricCard render logic | 2 hr | Conditional icon, color, layout |
| 3. Update existing MetricCard tests | 1.5 hr | Ensure no regressions |
| 4. Add grade-specific tests | 1 hr | New variant tests |
| 5. Update ChartRenderer config logic | 1 hr | Config JSON sniffing for grade mode |
| 6. Regression test existing deployments | 1 hr | Verify all existing cards still work |
| **Total** | **~7.5 hr** | Higher risk, more testing needed |

---

## 7. Code Examples

### New GradeMetricCard Component Structure (Sketch)

```typescript
// GradeMetricCard.tsx

import * as React from "react";
import {
  Card, Text, makeStyles, tokens, mergeClasses,
} from "@fluentui/react-components";
// Icons from @fluentui/react-icons
import { GavelRegular, MoneyRegular, TargetRegular } from "@fluentui/react-icons";

export type GradeColorScheme = "blue" | "yellow" | "red";

export interface IGradeMetricCardProps {
  /** Area name: "Guidelines", "Budget", "Outcomes" */
  areaName: string;
  /** Area icon identifier */
  areaIcon: "guidelines" | "budget" | "outcomes";
  /** Numeric grade value (0.00 - 1.00) */
  gradeValue: number;
  /** Contextual text template (e.g., "You have an {grade}% in {area} compliance") */
  contextTemplate?: string;
  /** Override color scheme (auto-calculated from gradeValue if not provided) */
  colorScheme?: GradeColorScheme;
  /** Callback for drill interaction */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Whether the card is interactive */
  interactive?: boolean;
}

// Color scheme resolution from grade value
function resolveColorScheme(gradeValue: number): GradeColorScheme {
  if (gradeValue >= 0.85) return "blue";
  if (gradeValue >= 0.70) return "yellow";
  return "red";
}

// Grade value to letter grade conversion
function gradeValueToLetter(value: number): string {
  if (value >= 1.00) return "A+";
  if (value >= 0.95) return "A";
  if (value >= 0.90) return "B+";
  if (value >= 0.85) return "B";
  if (value >= 0.80) return "C+";
  if (value >= 0.75) return "C";
  if (value >= 0.70) return "D+";
  if (value >= 0.65) return "D";
  if (value >= 0.60) return "F";
  return "No Grade";
}

// Icon resolver
function getAreaIcon(area: string): React.ReactElement {
  switch (area) {
    case "guidelines": return <GavelRegular />;
    case "budget":     return <MoneyRegular />;
    case "outcomes":   return <TargetRegular />;
    default:           return <TargetRegular />;
  }
}

// Styles using makeStyles (Griffel) with Fluent v9 tokens
const useStyles = makeStyles({
  card: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalL,
    gap: tokens.spacingVerticalS,
    minWidth: "180px",
    minHeight: "160px",
    transition: "box-shadow 0.2s ease-in-out",
  },
  // Color-coded card backgrounds using semantic tokens
  cardBlue: {
    backgroundColor: "var(--colorBrandBackground2, #ebf3fc)",
    borderLeftWidth: "4px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorBrandBackground,
  },
  cardYellow: {
    backgroundColor: "var(--colorStatusWarningBackground2, #fff4ce)",
    borderLeftWidth: "4px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorPaletteYellowBorderActive,
  },
  cardRed: {
    backgroundColor: "var(--colorStatusDangerBackground2, #fde7e9)",
    borderLeftWidth: "4px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorPaletteRedBorderActive,
  },
  icon: {
    fontSize: "28px",
    color: tokens.colorNeutralForeground2,
  },
  grade: {
    fontSize: tokens.fontSizeHero900,    // Largest hero size
    fontWeight: tokens.fontWeightBold,
    lineHeight: tokens.lineHeightHero900,
    textAlign: "center",
  },
  // Grade text color variants
  gradeBlue:   { color: tokens.colorBrandForeground1 },
  gradeYellow: { color: tokens.colorPaletteYellowForeground2 },
  gradeRed:    { color: tokens.colorPaletteRedForeground1 },
  contextText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
  },
});
```

### ChartRenderer Integration Point

In `ChartRenderer.tsx`, add a new case:

```typescript
case VT.ReportCardMetric: {
  // Parse config for Report Card metric card
  const gradeField = config.gradeField as string;
  const areaIcon = config.icon as string || "outcomes";
  const contextTemplate = config.contextTemplate as string;

  // Grade value comes from chart data (single data point)
  const gradeValue = dataPoints.length > 0 ? dataPoints[0].value : 0;

  return (
    <GradeMetricCard
      areaName={sprk_name}
      areaIcon={areaIcon as "guidelines" | "budget" | "outcomes"}
      gradeValue={gradeValue / 100}  // Convert from percentage to decimal
      contextTemplate={contextTemplate}
      onDrillInteraction={onDrillInteraction}
      interactive={!!onDrillInteraction}
    />
  );
}
```

### VisualType Enum Addition

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
  ReportCardMetric  = 100000010,  // NEW: Grade-based metric card
}
```

---

## 8. Summary

| Question | Answer |
|----------|--------|
| Does VisualHost exist? | Yes, at `src/client/pcf/VisualHost/` (v1.2.29) |
| Does a `visual-host` shared library exist? | No, VisualHost is PCF-only |
| Does MetricCard support icons? | No |
| Does MetricCard support color coding? | No (neutral theme only) |
| Does MetricCard support letter grades? | Partially (string values work, but formatting is numeric-oriented) |
| Does MetricCard support contextual text? | Yes (description prop) |
| Can MetricCard be extended? | Technically yes, but NOT recommended |
| **Recommendation** | **Create new `GradeMetricCard` component** (Option B) |
| Estimated effort (new component) | ~6 hours |
| Estimated effort (extend existing) | ~7.5 hours (higher risk) |
| Files to modify | `types/index.ts`, `ChartRenderer.tsx`, + new component file |
| ADR compliance | Fluent v9 tokens (ADR-021), shared library eligible later (ADR-012) |

---

*End of research report. This output feeds directly into Task 021 (Design Report Card Metric Card).*
