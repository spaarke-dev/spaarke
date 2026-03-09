# MetricCard Enhancement Design — Configurable Matrix Cards

> **Date**: February 12, 2026
> **Project**: matter-performance-KPI-r1
> **Status**: Design (pending approval)
> **Scope**: Extend MetricCard visual type; deprecate ReportCardMetric

---

## 1. Problem Statement

The current MetricCard matrix mode produces identical-looking cards (label + raw number). For KPI scorecards, we need per-card color coding, icons, custom value formatting (letter grades), and descriptions. A separate `ReportCardMetric` (100000010) visual type was created for this, but it only renders one card per VisualHost instance — requiring 3 PCF controls for 3 performance areas.

The existing MetricCard matrix mode already handles multi-card layout via Group By Field. Rather than maintaining two visual types, we should extend MetricCard to be fully configurable and deprecate ReportCardMetric.

---

## 2. Design Principles

### 2.1 Responsive — No Hardcoded Sizes

- All card dimensions use **CSS Grid `auto-fill` with `minmax()`** — cards wrap naturally based on container width
- Font sizes use **Fluent UI typography tokens** (not px values) so they scale with user settings
- Card aspect ratio maintained via `aspect-ratio` CSS property (not pixel calculations)
- No fixed breakpoints — the Grid handles layout automatically
- Cards adapt from 1-column (narrow) to N-column (wide) without media queries

**Implementation:**
```css
/* Current (rigid): */
grid-template-columns: repeat(3, 1fr);

/* Enhanced (responsive): */
grid-template-columns: repeat(auto-fill, minmax(var(--card-min-width), 1fr));
```

Where `--card-min-width` derives from `cardSize` config:

| Card Size | Min Width | Rationale |
|-----------|-----------|-----------|
| `small` | 140px | Dense dashboards — 5+ cards per row on wide screens |
| `medium` | 200px | Default — 3-4 cards per row |
| `large` | 280px | Hero metrics — 2-3 cards per row |

### 2.2 Container Overflow — Scrolling Strategy

**Vertical overflow (too many card rows):**
- Cards wrap into multiple rows naturally via CSS Grid `auto-fill`
- The VisualHost container itself respects its parent section's height
- If the form section has a fixed height, CSS `overflow-y: auto` enables vertical scroll
- The component does NOT set its own scroll — it defers to the Dataverse form section container
- Cards render fully; the parent decides whether to scroll or grow

**Horizontal overflow (card too wide for column):**
- Never happens — `minmax()` ensures cards shrink to fit
- At minimum width, single column stacks vertically

**Edge cases:**
- 0 data points → "No data available" message (current behavior, preserved)
- 1 data point → Single card, centered (existing MetricCard single mode)
- 20+ data points → Grid wraps; parent section scrolls if needed
- `maxDisplayItems` from config JSON caps visible cards: `"maxCards": 12`

### 2.3 Dark Mode — Fluent Token-Only Colors

**Rule: Zero hardcoded hex values in any component.**

All colors must come from one of these sources:

| Source | How It Works | Dark Mode Behavior |
|--------|-------------|-------------------|
| **Fluent UI v9 tokens** | `tokens.colorBrandBackground2` | Auto-adapts via FluentProvider theme |
| **Semantic token aliases** | `"brandBackground2"` in config → resolved at runtime | Mapped to current theme's token |
| **Option set colors from Dataverse** | Hex values from metadata API | Applied as **accent only** (border, icon tint) — never as background or text fill |

**Why option set colors are accent-only:**
Dataverse option set colors are single hex values defined by admins without dark mode awareness. Using them as card backgrounds would break contrast in dark themes. Instead:
- **Light mode**: Option set color applies to left border accent (4px) and icon tint
- **Dark mode**: Same — the accent is small enough that a bright color doesn't break readability
- Card background and text always use Fluent semantic tokens that auto-adapt

**Value threshold colors** use semantic token aliases (not hex):
```json
{
  "colorThresholds": [
    {
      "range": [0.85, 1.00],
      "tokenSet": "brand"
    },
    {
      "range": [0.70, 0.84],
      "tokenSet": "warning"
    },
    {
      "range": [0.00, 0.69],
      "tokenSet": "danger"
    }
  ]
}
```

Each `tokenSet` name maps to a predefined set of Fluent tokens:

| Token Set | Card Background | Border Accent | Value Text | Icon/Label |
|-----------|----------------|---------------|------------|------------|
| `brand` | `colorBrandBackground2` | `colorBrandBackground` | `colorBrandForeground1` | `colorBrandForeground2` |
| `warning` | `colorPaletteYellowBackground1` | `colorPaletteYellowBorderActive` | `colorPaletteYellowForeground2` | `colorPaletteYellowForeground2` |
| `danger` | `colorPaletteRedBackground1` | `colorPaletteRedBorderActive` | `colorPaletteRedForeground1` | `colorPaletteRedForeground1` |
| `success` | `colorPaletteGreenBackground1` | `colorPaletteGreenBorderActive` | `colorPaletteGreenForeground1` | `colorPaletteGreenForeground1` |
| `neutral` | `colorNeutralBackground3` | `colorNeutralStroke1` | `colorNeutralForeground3` | `colorNeutralForeground3` |

All tokens are resolved via `useFluentTheme()` at render time — no theme detection, no conditional logic, no `prefers-color-scheme`.

---

## 3. Configuration Tiers

### Tier 1: Chart Definition Fields (Dataverse Columns)

New fields to add to `sprk_chartdefinition` entity:

| Field | Schema Name | Type | Options | Default | Purpose |
|-------|------------|------|---------|---------|---------|
| Value Format | `sprk_valueformat` | Choice (local) | ShortNumber (100000000), LetterGrade (100000001), Percentage (100000002), WholeNumber (100000003), Decimal (100000004), Currency (100000005) | ShortNumber | How to display aggregated values on cards |
| Color Source | `sprk_colorsource` | Choice (local) | None (100000000), OptionSetColor (100000001), ValueThreshold (100000002) | None | How per-card colors are determined |

**Why these are fields (not JSON):**
- Every MetricCard chart definition needs to choose these
- Finite, well-defined options → perfect for choice dropdowns
- Discoverable by admins without JSON knowledge
- Queryable (find all chart definitions using letter grades)

### Tier 2: Configuration JSON (`sprk_configurationjson`)

Enhanced schema — all fields optional with sensible defaults:

```json
{
  "cardDescription": "{formatted} in {label} compliance",
  "nullDisplay": "N/A",
  "nullDescription": "No data available for {label}",
  "cardSize": "medium",
  "sortBy": "optionSetOrder",
  "columns": null,
  "compact": false,
  "showTitle": true,
  "maxCards": null,
  "accentFromOptionSet": true,
  "iconMap": {
    "Guidelines": "Gavel",
    "Budget": "Money",
    "Outcomes": "Target"
  },
  "colorThresholds": [
    { "range": [0.85, 1.00], "tokenSet": "brand" },
    { "range": [0.70, 0.84], "tokenSet": "warning" },
    { "range": [0.00, 0.69], "tokenSet": "danger" }
  ]
}
```

**Field Reference:**

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `cardDescription` | string | `null` | Template for subtitle text. Placeholders: `{value}` (raw), `{formatted}` (after valueFormat), `{label}` (group label), `{count}` (record count in group) |
| `nullDisplay` | string | `"—"` | What to show as the value when aggregation returns null |
| `nullDescription` | string | `null` | Description text when value is null. If null, uses `cardDescription` template with "N/A" |
| `cardSize` | `"small"` \| `"medium"` \| `"large"` | `"medium"` | Controls card min-width and typography scale |
| `sortBy` | `"label"` \| `"value"` \| `"valueAsc"` \| `"optionSetOrder"` | `"label"` | How cards are ordered in the grid |
| `columns` | number \| null | `null` | Fixed columns per row. `null` = auto-fill responsive grid |
| `compact` | boolean | `false` | Compact mode: reduced padding and font sizes |
| `showTitle` | boolean | `true` | Show chart name above card grid |
| `maxCards` | number \| null | `null` | Max visible cards. `null` = show all |
| `accentFromOptionSet` | boolean | `false` | Use Dataverse option set color as border accent and icon tint |
| `iconMap` | object \| null | `null` | Map group labels (or raw option values) to Fluent UI icon names |
| `colorThresholds` | array \| null | `null` | Value-based color rules. Only used when Color Source = ValueThreshold |

**iconMap key resolution order:**
1. Match against formatted label (e.g., `"Guidelines"`)
2. Match against raw option set value (e.g., `"100000000"`)
3. No match → no icon rendered

### Tier 3: PCF Properties (Per-Placement)

One new property:

| Property | Schema Name | Type | Purpose |
|----------|------------|------|---------|
| Value Format Override | `valueFormatOverride` | SingleLine.Text | Override chart def's value format for this specific placement. Values: `"shortNumber"`, `"letterGrade"`, `"percentage"`, `"wholeNumber"`, `"decimal"`, `"currency"` |

**Why per-placement:** Same chart definition on a summary dashboard might show percentages, while on a detail form it shows letter grades. The PCF property override avoids duplicating chart definitions.

Existing PCF properties (unchanged): `chartDefinitionId`, `contextFieldName`, `fetchXmlOverride`, `height`, `width`, `justification`, `showToolbar`, `enableDrillThrough`

---

## 4. Component Changes

### 4.1 Files to Modify

| File | Change | Scope |
|------|--------|-------|
| `control/types/index.ts` | Add `ValueFormat` enum, `ColorSource` enum, extend `IChartDefinition` with new fields, extend `IAggregatedDataPoint` with `color` and `iconKey` fields, add `ICardConfig` interface | Types |
| `control/services/DataAggregationService.ts` | Capture option set color metadata (`@OData.Community.Display.V1.Color` annotation) during aggregation; pass through to data points | Data |
| `control/components/ChartRenderer.tsx` | Update MetricCard case to pass new config; map ReportCardMetric → MetricCard with preset defaults | Routing |
| `control/components/MetricCardMatrix.tsx` | Major enhancement: add icon rendering, per-card colors (accent border, background tokens), description text, value formatting, responsive grid, card size variants, sort order | UI |
| `control/components/MetricCard.tsx` | Add value formatting support, icon slot, description slot, color prop | UI |
| `control/utils/gradeUtils.ts` | Extract `gradeValueToLetter` and `resolveColorTokens` as reusable utilities (already exist, just ensure they're importable from MetricCard path) | Utils |
| `control/utils/valueFormatters.ts` | **New file** — Value formatting functions: `formatShortNumber`, `formatLetterGrade`, `formatPercentage`, `formatWholeNumber`, `formatDecimal`, `formatCurrency` | Utils |
| `control/utils/cardConfigResolver.ts` | **New file** — Resolves merged config from chart definition fields + JSON + PCF overrides; applies preset defaults for ReportCardMetric | Utils |
| `control/ControlManifest.Input.xml` | Add `valueFormatOverride` property | Manifest |
| `control/components/VisualHostRoot.tsx` | Pass new PCF property to ChartRenderer | Wiring |

### 4.2 Files to Deprecate (Not Delete Yet)

| File | Action | Reason |
|------|--------|--------|
| `control/components/GradeMetricCard.tsx` | Mark deprecated, add comment pointing to MetricCardMatrix | Replaced by enhanced MetricCard |
| `control/configurations/matterMainCards.ts` | Mark deprecated | Static config replaced by Dataverse chart definitions |
| `control/configurations/matterReportCardTrends.ts` | Mark deprecated | Same |

### 4.3 New File: `control/utils/valueFormatters.ts`

```typescript
export type ValueFormatType =
  | "shortNumber"
  | "letterGrade"
  | "percentage"
  | "wholeNumber"
  | "decimal"
  | "currency";

export function formatValue(
  value: number | null,
  format: ValueFormatType,
  nullDisplay: string = "—"
): string {
  if (value === null || value === undefined) return nullDisplay;

  switch (format) {
    case "letterGrade":
      return gradeValueToLetter(value);     // From gradeUtils.ts
    case "percentage":
      return `${Math.round(value * 100)}%`;
    case "wholeNumber":
      return Math.round(value).toLocaleString();
    case "decimal":
      return value.toFixed(2);
    case "currency":
      return `$${value.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
    case "shortNumber":
    default:
      return formatShortNumber(value);       // Existing K/M formatter
  }
}
```

### 4.4 New File: `control/utils/cardConfigResolver.ts`

Merges configuration from three tiers into a single resolved config object:

```
Priority: PCF Property Override > Chart Definition Field > Config JSON > Defaults

Example resolution for valueFormat:
  1. PCF valueFormatOverride = "percentage" → use "percentage"
  2. Chart def sprk_valueformat = 100000001 (LetterGrade) → use "letterGrade"
  3. Config JSON "valueFormat" field → use that
  4. Default → "shortNumber"
```

Also handles the **ReportCardMetric preset**: when `sprk_visualtype === 100000010`, auto-applies:
- `valueFormat: "letterGrade"`
- `colorSource: "valueThreshold"`
- `colorThresholds`: grade defaults (brand/warning/danger)
- `iconMap`: from existing GradeMetricCard icon registry
- `nullDisplay: "N/A"`

---

## 5. Enhanced MetricCardMatrix Component

### 5.1 Card Layout (Responsive Grid)

```css
.metricCardMatrix {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(var(--card-min-width), 1fr));
  gap: 8px;
  width: 100%;
}

/* Card sizes set the CSS variable */
.cardSize-small  { --card-min-width: 140px; }
.cardSize-medium { --card-min-width: 200px; }
.cardSize-large  { --card-min-width: 280px; }
```

When `columns` is explicitly set in config, switches to fixed grid:
```css
grid-template-columns: repeat(var(--columns), 1fr);
```

### 5.2 Card Anatomy (Enhanced)

```
┌─────────────────────────────────┐
│ ▌ [Icon]  Label                 │  ← Border accent (4px left)
│ ▌                               │     + Icon (from iconMap)
│ ▌      Formatted Value          │     + Label (from group by)
│ ▌                               │
│ ▌ Description text              │  ← Card description template
└─────────────────────────────────┘
```

**Color application by source:**

| Color Source | Border Accent | Card Background | Value Text | Icon Tint |
|-------------|---------------|-----------------|------------|-----------|
| None | `colorNeutralStroke1` | `colorNeutralBackground1` | `colorNeutralForeground1` | `colorNeutralForeground2` |
| OptionSetColor | Option set hex color | `colorNeutralBackground1` | `colorNeutralForeground1` | Option set hex color |
| ValueThreshold | Token from matching threshold | Token from threshold | Token from threshold | Token from threshold |

### 5.3 Overflow Handling

The component itself never scrolls. It renders all cards (up to `maxCards`) in a grid that wraps naturally. The parent Dataverse form section controls whether scrolling occurs.

If `maxCards` is set and there are more data points than `maxCards`, the grid shows `maxCards` cards. No "show more" button (keep it simple for V1).

### 5.4 Accessibility

- Each card: `role="button"` when interactive, `role="region"` otherwise
- `aria-label`: `"{label}: {formatted value}. {description}"`
- Tab navigation across cards
- Enter/Space activates drill-through
- Color is supplementary — value and label always convey meaning via text
- Icon has `aria-hidden="true"` (decorative, label provides context)

---

## 6. Data Flow Changes

### 6.1 DataAggregationService Enhancement

Currently captures per group:
```typescript
{ label: string, value: number, fieldValue: unknown }
```

Enhanced to also capture:
```typescript
{
  label: string,
  value: number,
  fieldValue: unknown,
  color?: string,       // From option set color metadata (hex)
  sortOrder?: number    // From option set sequence (for optionSetOrder sort)
}
```

**How option set color is captured:**

When `colorSource === "optionSetColor"`, the DataAggregationService makes an additional metadata call to get option set definitions:

```typescript
// Fetch option set metadata once during aggregation
const optionSetMeta = await webApi.retrieveOptionSetMetadata(entityName, groupByField);
// Returns: [{ value: 100000000, label: "Guidelines", color: "#0078D4" }, ...]
```

This is cached per entity+field combination for the session.

### 6.2 Sort Order

Applied after aggregation, before rendering:

| Sort | Implementation |
|------|---------------|
| `label` | `dataPoints.sort((a, b) => a.label.localeCompare(b.label))` |
| `value` | `dataPoints.sort((a, b) => b.value - a.value)` |
| `valueAsc` | `dataPoints.sort((a, b) => a.value - b.value)` |
| `optionSetOrder` | `dataPoints.sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))` |

---

## 7. ReportCardMetric Deprecation Path

### What happens in ChartRenderer.tsx

```typescript
case VT.ReportCardMetric: {
  // ReportCardMetric is now a preset for MetricCard.
  // Apply grade-specific defaults, then fall through to MetricCard rendering.
  const presetConfig = applyReportCardMetricPreset(chartDef, config);
  // ... render MetricCardMatrix with presetConfig
}
```

### Preset Defaults Applied

| Setting | Auto-Applied Value |
|---------|-------------------|
| valueFormat | `"letterGrade"` |
| colorSource | `"valueThreshold"` |
| colorThresholds | Brand (0.85-1.00), Warning (0.70-0.84), Danger (0.00-0.69) |
| iconMap | From existing GradeMetricCard icon registry (guidelines→Gavel, budget→Money, outcomes→Target) |
| cardDescription | From `contextTemplate` in config JSON (backward compatible) |
| nullDisplay | `"N/A"` |
| nullDescription | `"No data available for {label}"` |
| sortBy | `"optionSetOrder"` |

### Files Deprecated

| File | Status | Migration |
|------|--------|-----------|
| `GradeMetricCard.tsx` | Deprecated → remove after verification | Functionality absorbed into MetricCardMatrix |
| `matterMainCards.ts` | Deprecated → remove | Config moves to Dataverse chart definition records |
| `matterReportCardTrends.ts` | Deprecated → remove | Config moves to Dataverse chart definition records |

### Option Set Value

`ReportCardMetric (100000010)` remains valid in `sprk_visualtype`. It maps to MetricCard internally. No schema migration needed.

---

## 8. Task Breakdown

### Phase 1: Foundation (Types, Utils, Data)

| # | Task | Files | Est. |
|---|------|-------|------|
| 1.1 | Add `ValueFormat` and `ColorSource` enums to types | `types/index.ts` | Small |
| 1.2 | Extend `IAggregatedDataPoint` with `color` and `sortOrder` | `types/index.ts` | Small |
| 1.3 | Add `ICardConfig` interface (resolved config) | `types/index.ts` | Small |
| 1.4 | Create `valueFormatters.ts` with all format functions | `utils/valueFormatters.ts` (new) | Medium |
| 1.5 | Create `cardConfigResolver.ts` with tier merge logic + ReportCardMetric preset | `utils/cardConfigResolver.ts` (new) | Medium |
| 1.6 | Extend `DataAggregationService` to capture option set color + sort order | `services/DataAggregationService.ts` | Medium |

### Phase 2: Component Enhancement

| # | Task | Files | Est. |
|---|------|-------|------|
| 2.1 | Enhance `MetricCardMatrix` — responsive grid, per-card colors, icons, descriptions, value formatting | `components/MetricCardMatrix.tsx` | Large |
| 2.2 | Enhance `MetricCard` (single mode) — add icon slot, description, value formatting, color | `components/MetricCard.tsx` | Medium |
| 2.3 | Update `ChartRenderer` MetricCard case — pass resolved config, add ReportCardMetric preset routing | `components/ChartRenderer.tsx` | Medium |
| 2.4 | Add `valueFormatOverride` PCF property | `ControlManifest.Input.xml`, `VisualHostRoot.tsx` | Small |

### Phase 3: Deprecation & Cleanup

| # | Task | Files | Est. |
|---|------|-------|------|
| 3.1 | Mark `GradeMetricCard.tsx` deprecated with comment | `components/GradeMetricCard.tsx` | Small |
| 3.2 | Mark config files deprecated | `configurations/matterMainCards.ts`, `matterReportCardTrends.ts` | Small |
| 3.3 | Verify ReportCardMetric preset produces identical output to old GradeMetricCard | Manual testing | Medium |

### Phase 4: Build, Documentation, Deploy

| # | Task | Files | Est. |
|---|------|-------|------|
| 4.1 | Version bump PCF (1.2.32 → 1.2.33) in all 5 locations | Manifests, solution.xml, pack.ps1, VisualHostRoot.tsx footer | Small |
| 4.2 | Build and lint | `npm run build` | Small |
| 4.3 | Update `VISUALHOST-SETUP-GUIDE.md` — MetricCard enhancements, new fields, updated examples, deprecate ReportCardMetric sections | `docs/guides/VISUALHOST-SETUP-GUIDE.md` | Medium |
| 4.4 | Update `VISUALHOST-ARCHITECTURE.md` — component changes, data flow | `docs/guides/VISUALHOST-ARCHITECTURE.md` | Medium |
| 4.5 | Pack solution .zip | `pack.ps1` | Small |

### Phase 5: Dataverse Schema

| # | Task | Method | Est. |
|---|------|--------|------|
| 5.1 | Add `sprk_valueformat` choice field to `sprk_chartdefinition` | Manual in Dataverse or solution XML | Small |
| 5.2 | Add `sprk_colorsource` choice field to `sprk_chartdefinition` | Manual in Dataverse or solution XML | Small |
| 5.3 | Update `customizations.xml` in deployment folder | `deployment/customizations.xml` | Small |

---

## 9. Example Configurations (Post-Enhancement)

### KPI Grade Cards — Single Chart Definition (Replaces 3 ReportCardMetric Instances)

**Chart Definition Record:**

| Field | Value |
|-------|-------|
| Name | KPI Performance Grades |
| Visual Type | MetricCard (100000000) |
| Entity Logical Name | `sprk_matter` |
| Group By Field | `sprk_performancearea` |
| Aggregation Field | `sprk_grade_current` |
| Aggregation Type | Average (100000002) |
| Value Format | Letter Grade (100000001) |
| Color Source | Value Threshold (100000002) |
| Configuration JSON | *(see below)* |

```json
{
  "cardDescription": "{formatted} — {value}% compliance",
  "nullDisplay": "N/A",
  "nullDescription": "No assessments submitted",
  "sortBy": "optionSetOrder",
  "accentFromOptionSet": true,
  "iconMap": {
    "Guidelines": "Gavel",
    "Budget": "Money",
    "Outcomes": "Target"
  },
  "colorThresholds": [
    { "range": [0.85, 1.00], "tokenSet": "brand" },
    { "range": [0.70, 0.84], "tokenSet": "warning" },
    { "range": [0.00, 0.69], "tokenSet": "danger" }
  ]
}
```

**Result:** One VisualHost instance → 3 cards (Guidelines, Budget, Outcomes), each with appropriate icon, letter grade, color coding, and description.

### Event Count by Type — Enhanced with Icons

**Chart Definition Record:**

| Field | Value |
|-------|-------|
| Name | Events by Type |
| Visual Type | MetricCard (100000000) |
| Entity Logical Name | `sprk_event` |
| Group By Field | `_sprk_eventtype_ref_value` |
| Aggregation Type | Count (100000000) |
| Value Format | Whole Number (100000003) |
| Color Source | Option Set Color (100000001) |

```json
{
  "accentFromOptionSet": true,
  "iconMap": {
    "Task": "TaskListSquare",
    "Deadline": "CalendarClock",
    "Reminder": "Alert"
  },
  "sortBy": "value"
}
```

### Financial Summary — Currency Format

| Field | Value |
|-------|-------|
| Name | Budget by Category |
| Visual Type | MetricCard (100000000) |
| Group By Field | `sprk_budgetcategory` |
| Aggregation Field | `sprk_amount` |
| Aggregation Type | Sum (100000001) |
| Value Format | Currency (100000005) |
| Color Source | None (100000000) |

```json
{
  "cardSize": "large",
  "cardDescription": "{count} line items",
  "sortBy": "value"
}
```

---

## 10. Risk Assessment

| Risk | Mitigation |
|------|-----------|
| Option set metadata API call adds latency | Cache per entity+field; only call when colorSource = OptionSetColor |
| Large number of groups overwhelms the grid | `maxCards` config caps visible cards; responsive grid wraps naturally |
| Dataverse option set hex colors break dark mode contrast | Applied as accent only (border + icon tint), not background |
| Breaking change for existing MetricCard chart definitions | All new fields default to current behavior (None, ShortNumber) — zero impact on existing configs |
| ReportCardMetric preset doesn't match old GradeMetricCard exactly | Phase 3.3 manual verification before removing old component |

---

## 11. Documentation Updates Required

| Document | Changes |
|----------|---------|
| `docs/guides/VISUALHOST-SETUP-GUIDE.md` | Update MetricCard section with new fields (Value Format, Color Source); add configuration JSON reference; update Use Case 5/6 to use single chart definition; deprecation notice for ReportCardMetric; update Quick Reference entries |
| `docs/guides/VISUALHOST-ARCHITECTURE.md` | Update component diagram; document enhanced data flow; update MetricCardMatrix component description |
| `projects/matter-performance-KPI-r1/notes/report-card-component-usage.md` | Add deprecation notice pointing to enhanced MetricCard |
| `projects/matter-performance-KPI-r1/notes/report-card-component-design.md` | Add deprecation notice pointing to this design doc |

---

*This design replaces the separate ReportCardMetric visual type with a fully configurable MetricCard, reducing chart definitions from 3 to 1 for KPI scorecards while making the component reusable across any grouped metric display.*
