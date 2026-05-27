# Visual Host Chart Definition Regression Baseline

> **Purpose**: NFR-05 baseline. Every chart definition listed here MUST render unchanged after Phase 2 (FR-VH-01..05) extensions land.
> **Source**: `mcp__dataverse__read_query` against `sprk_chartdefinition` where `statecode = 0` (Active)
> **Captured**: 2026-05-27
> **Total active chart defs**: 15
> **Consumer of this list**: task 025 (Visual Host backward-compat regression smoke)

---

## Schema notes

The actual `sprk_chartdefinition` table has these relevant fields (caller in spec used assumed names that drifted — corrected here):

| Field referenced in spec | Actual field in Dataverse |
|---|---|
| `sprk_entityname` | `sprk_entitylogicalname` |
| `sprk_drillthroughentity` | `sprk_drillthroughtarget` |

Visual type option-set values (from `describe_table`):
- Metric Card = `100000000`
- Bar Chart = `100000001`
- Line Chart = `100000002`
- Area Chart = `100000003`
- Donut Chart = `100000004` ← **NO active in-production chart defs today**
- Status Bar = `100000005`
- Calendar = `100000006`
- Mini Table = `100000007`
- Due Date Card = `100000008`
- Due Date Card List = `100000009`
- Report Card Metric = `100000010`
- Gauge = `100000011`
- Horizontal Stacked Bar = `100000012`

---

## In-production chart definitions (15 active records)

| # | Name | Visual Type | Owning Entity | Options Keys Used | FR-VH-* Risk |
|---|---|---|---|---|---|
| 1 | Documents by Document Type | Bar Chart | sprk_document | aiSummaryField | None (Bar Chart untouched by FR-VH-*) |
| 2 | Due Date by Event Type Matrix | Metric Card | sprk_event | (no optionsjson) | FR-VH-02, 03, 05 |
| 3 | Due Date Card List | Due Date Card List | sprk_event | aiSummaryField | FR-VH-05 only (DueDateCard untouched per FR-DV-04) |
| 4 | Due Date Count Card | Metric Card | sprk_event | (no optionsjson) | FR-VH-02, 03, 05 |
| 5 | Events by Event Type | Bar Chart | sprk_event | (no optionsjson) | None |
| 6 | **Matter Financial Metrics Scorecard** | **Metric Card** | sprk_matter | **fieldPivot, columns, colorSource: signBased, sortBy** | **HIGH — uses fieldPivot which FR-VH-02/03 extend** |
| 7 | **Matter Financial Metrics Stacked Bar** | **Horizontal Stacked Bar** | sprk_matter | **fieldPivot, valueFormat, colorSource, colorThresholds, aiSummaryField** | **HIGH — uses colorThresholds + fieldPivot; FR-VH-04 adds layoutMode** |
| 8 | Matter KPI Score Card Gauges | Gauge | sprk_matter | fieldPivot, gaugeMode, valueFormat: letterGrade, colorSource: valueThreshold, colorThresholds, aiSummaryField | FR-VH-05 only (Gauge renderer not touched by 020–023) |
| 9 | **Matter KPI Scorecard** | **Metric Card** | sprk_matter | **fieldPivot, columns, iconMap, colorThresholds** + sprk_valueformat=Letter Grade, sprk_colorsource=Value Threshold | **HIGH — spec-cited regression baseline; uses fieldPivot + colorThresholds heavily** |
| 10 | Number of Documents Card | Metric Card | sprk_document | aiSummaryField | FR-VH-02, 03, 05 |
| 11 | Open Tasks | Bar Chart | sprk_document | aiSummaryField | None |
| 12 | **Project Financial Metrics Scorecard** | **Metric Card** | sprk_project | **fieldPivot, columns, colorSource: signBased, sortBy** | MEDIUM — mirror of #6 |
| 13 | Project Financial Metrics Stacked Bar | Horizontal Stacked Bar | sprk_project | fieldPivot, valueFormat, colorSource, colorThresholds, aiSummaryField | MEDIUM — mirror of #7 |
| 14 | **Project KPI Scorecard** | **Metric Card** | sprk_project | **fieldPivot, columns, iconMap, colorThresholds** + sprk_valueformat=Letter Grade | MEDIUM — mirror of #9 |
| 15 | Project KPI Scorecard Gauges | Gauge | sprk_project | fieldPivot, gaugeMode, valueFormat, colorSource, colorThresholds, aiSummaryField | FR-VH-05 only (Gauge untouched by 020–023) |

---

## Risk-banded regression matrix

### HIGH risk — must smoke-test BEFORE any Phase 2 PR merges

These chart defs use the exact keys/visual types that FR-VH-01..04 extend. Any regression bug would visibly break them.

1. **Matter KPI Scorecard** (#9) — Metric Card + fieldPivot + colorThresholds + valueFormat: Letter Grade — spec.md §Existing Patterns to Follow explicitly cites this as canonical
2. **Matter Financial Metrics Scorecard** (#6) — Metric Card + fieldPivot + colorSource: signBased — spec.md §NFR-05 explicitly cites this as canonical
3. **Matter Financial Metrics Stacked Bar** (#7) — HSBar + fieldPivot + colorThresholds — FR-VH-04 adds `layoutMode` to the same renderer

### MEDIUM risk — smoke-test before Phase 7 deploy

Project-* equivalents of the HIGH-risk Matter ones — same patterns, less prominent in this project's surface area but same renderer code path.

4. **Project KPI Scorecard** (#14)
5. **Project Financial Metrics Scorecard** (#12)
6. **Project Financial Metrics Stacked Bar** (#13)

### LOW risk — smoke-check during NFR-05 final pass (task 025 + task 074)

These either use simple shapes (Bar Chart, aiSummaryField only) OR untouched visual types (Gauge, Due Date Card List, Due Date Card).

7. Due Date by Event Type Matrix (#2)
8. Due Date Count Card (#4)
9. Number of Documents Card (#10)
10. Matter KPI Score Card Gauges (#8) — Gauge, FR-VH-05 wrap only
11. Project KPI Scorecard Gauges (#15) — Gauge, FR-VH-05 wrap only
12. Due Date Card List (#3) — DueDateCard, FR-VH-05 wrap only
13. Documents by Document Type (#1) — Bar Chart
14. Events by Event Type (#5) — Bar Chart
15. Open Tasks (#11) — Bar Chart

---

## FR-VH-* impact map

| FR | Renderer file | Production chart defs affected | Regression smoke required |
|---|---|---|---|
| FR-VH-01 | `DonutChart.tsx` + `ChartRenderer.tsx` | **ZERO** — no Donut chart defs in production today | New code path; smoke-test by authoring FR-DV-01 (Matter Health) and rendering it. No existing regression risk. |
| FR-VH-02 | `MetricCard.tsx` (badge slot) | All 7 Metric Card defs (#2, #4, #6, #9, #10, #12, #14) | Verify badge prop absence is benign |
| FR-VH-03 | `MetricCard.tsx` (descriptionColor) | Same 7 Metric Card defs | Verify descriptionColor prop absence falls back to colorNeutralForeground3 |
| FR-VH-04 | `HorizontalStackedBar.tsx` (layoutMode) | 2 HSBar defs (#7, #13) | Verify layoutMode absence falls back to current (default) layout |
| FR-VH-05 | `CardChrome.tsx` (new) + `VisualHostRoot.tsx` | **All 15 chart defs** — CardChrome wraps every card | Verify CardChrome render does NOT change card dimensions, padding, or theming for any existing def |

---

## Notable findings (additional context for Phase 2 + 3)

### Donut Chart has zero production usage today

- FR-VH-01 is the highest-effort renderer extension but carries **zero regression risk** to in-production chart defs.
- The new `DonutChart` features (matrixRight layout, meanOfFields center, breakdown rows) ship cleanly via FR-DV-01 (Matter Health Composite).
- Decision: implementation can be more aggressive on Donut since there's no backward-compat fence.

### `aiSummaryField` is a Custom Options key already in widespread use

- 7 chart defs use `aiSummaryField` (whether populated or empty string).
- FR-VH-05 `CardChrome.onAiSummary` slot is consistent with this — implementation should respect `aiSummaryField` value if present (read-only in v1; `showAiSparkle: false` default per spec).

### `colorThresholds` + `tokenSet` pattern is widely used

- 6 of 15 chart defs use `colorThresholds[].tokenSet` (`brand`, `warning`, `danger`, `success`).
- FR-VH-01 reuses this pattern via `getTokenSetColors()` from `MetricCardMatrix.tsx:138` — no parallel pattern.
- FR-VH-01 `colorThresholds` segment coloring for Donut MUST consume the same `tokenSet` vocabulary (brand/warning/danger/success) to remain consistent.

### `fieldPivot` is widely used

- 9 of 15 chart defs use `fieldPivot.fields[]` — the multi-field consumption pattern FR-VH-01 plugs into is already production-validated.
- `FieldPivotService` in VisualHost source IS the canonical entry point; FR-VH-01 should not add a parallel pivot service.

### `iconMap` key exists in 2 defs (Matter KPI Scorecard, Project KPI Scorecard)

- Not touched by FR-VH-* but visible in regression smoke; verify CardChrome wrap does not interfere with the iconMap-driven per-field icons.

### `aggregationtype` + `aggregationfield` is used by ~5 defs

- Pattern for FR-DV-03 (Matter Tasks COUNT overdue + upcoming) and FR-DV-05 (Matter Activity COUNT recent events) — chart-def aggregation already in production. FetchXML aggregation path is well-trodden.

---

## Consumer cross-reference

**Task 025** (Visual Host backward-compat regression smoke) — depends on this file:
- Renders each chart def above against a representative entity record (Matter, Project, Event, Document) in dev environment
- Captures screenshot in light + dark
- Compares against pre-Phase-2 baseline screenshots
- Pass criteria: ZERO visual diff for HIGH + MEDIUM risk records; LOW risk records pass a lighter visual + axe check

**Task 047** (VisualHost docs update — FR-DOC-09) — references this file:
- New Custom Options keys (`donutLayout`, `donutCenterMode`, `donutCenterLabel`, `showBreakdownRows`, `breakdownValueFormat`, `badge`, `descriptionColor`, `layoutMode`, `headlineFromField`, `subLineTemplate`) documented in [docs/architecture/VISUALHOST-ARCHITECTURE.md](../../../../docs/architecture/VISUALHOST-ARCHITECTURE.md)
- Chart-def authoring examples in [docs/guides/VISUALHOST-SETUP-GUIDE.md](../../../../docs/guides/VISUALHOST-SETUP-GUIDE.md) — example patterns can reference the 15 records here

**Task 074** (cross-cutting NFR validation) — final NFR-05 gate:
- Re-validate every record in this file after all deploys (Phase 7) — no regression introduced during Phase 4+5+6 work

---

## Static config cross-reference (matterMainCards.ts, matterReportCardTrends.ts)

Per spec, the deprecated `matterMainCards.ts` static config was migrated to Dataverse records. Cross-checking source comments:

- `src/client/pcf/VisualHost/control/configurations/matterMainCards.ts` — flagged "Deprecated v1.2.33 static config (now in Dataverse via sprk_chartdefinition records)" per the project-pipeline resource discovery agent
- The 4 Matter-entity chart defs (#6, #7, #8, #9) appear to be the migration targets
- `matterReportCardTrends.ts` is still a separate configuration not yet migrated — out of scope for this project unless Phase 4 touches Report Card

NO action needed in this task; recorded for context.

---

*This inventory is the binding NFR-05 baseline. Re-capture if any chart def is added/modified between this snapshot and task 025 execution.*
