# Lessons Learned: Matter Performance Assessment R1 MVP

> **Project**: matter-performance-KPI-r1
> **Completed**: 2026-02-12 (code), 2026-02-16 (deployment verified end-to-end)
> **Tasks**: 27 total (all completed in single day)

---

## Execution Strategy

### Wave-Based Parallel Execution
The project used dependency-based wave grouping with parallel subagents:
- **13 waves** executed across the session
- Tasks grouped by dependency satisfaction, launched as parallel subagents
- Typical wave: 2-4 tasks running concurrently
- **Result**: 27 tasks completed efficiently with minimal sequential bottlenecks

### Early Integration of Cross-Cutting Concerns
Tasks 033 (Color Coding) and 034 (Contextual Text Templates) were already implemented during Task 022 (Implement Report Card Metric Card). The task decomposition over-separated concerns that naturally belong together. Future projects should combine "implement component" and "implement component feature X" when the feature is integral to the component.

---

## Technical Insights

### VisualHost Card Pattern
Adding a new card type to VisualHost follows a well-defined pattern:
1. Add enum value to `VisualType` in `types/index.ts`
2. Create component in `components/`
3. Add switch case in `ChartRenderer.tsx`
4. Configuration via `sprk_configurationjson` JSON blob

### SVG Sparkline vs. Charting Library
Chose inline SVG over external charting library (Victory, Recharts) for the sparkline:
- **Pros**: Zero additional dependencies, tiny bundle impact, `currentColor` for automatic theme support
- **Cons**: Limited to simple line charts
- **Decision**: Correct for this use case (simple trend visualization)

### Fluent UI v9 Semantic Tokens
All colors use `tokens.*` references — no hex values anywhere. This provides:
- Automatic dark mode support via FluentProvider
- High contrast theme support for free
- Consistent visual language across components

### Linear Regression for Trend
Least squares method with 0.02 slope threshold works well for 5-point trend data:
- `slope > 0.02` = Improving (up arrow)
- `slope < -0.02` = Declining (down arrow)
- Otherwise = Stable (dash)

---

## Testing Approach

### Unit Test Coverage
44 tests covering the ScorecardCalculatorService:
- 20 core calculation tests (Task 016)
- 9 integration/E2E flow tests (Task 050)
- 4 performance benchmark tests (Task 051)
- 15 error scenario tests (Task 053)

### Audit-Based Validation
For accessibility (WCAG 2.1 AA) and dark mode compatibility, code audits proved effective:
- Systematic review of ARIA attributes, keyboard handlers, and semantic tokens
- Documented with specific line references for future maintenance

---

## Files Created

### Source Code (15 files)
| File | Purpose |
|------|---------|
| `GradeMetricCard.tsx` | Grade metric card component |
| `gradeUtils.ts` | Grade conversion, color resolution, templates, icons |
| `TrendCard.tsx` | Trend card with sparkline |
| `trendAnalysis.ts` | Linear regression and trend direction |
| `matterMainCards.ts` | Configuration for 3 main tab cards |
| `matterReportCardTrends.ts` | Configuration for 3 trend cards |
| `matter-reportcard-tab.xml` | Dataverse form tab XML |
| `add-kpi-ribbon.xml` | Ribbon button XML |
| `sprk_kpi_ribbon_actions.js` | Ribbon JavaScript handler |
| `sprk_matter_kpi_refresh.js` | Matter main form subgrid listener (deployed & working) |
| `sprk_kpi_subgrid_refresh.js` | Entity auto-detect version (Matter + Project) |
| `sprk_subgrid_parent_rollup.js` | Generic config-driven rollup (pattern reference) |
| `sprk_kpiassessment_quickcreate.js` | Quick Create trigger (superseded by subgrid listener) |
| `ScorecardCalculatorService.cs` | Calculator API service |
| `ScorecardCalculatorEndpoints.cs` | API endpoint registration (AllowAnonymous + RateLimited) |

### Test Code (3 files)
| File | Tests |
|------|-------|
| `ScorecardCalculatorIntegrationTests.cs` | 9 E2E flow tests |
| `ScorecardCalculatorPerformanceTests.cs` | 4 NFR-01 benchmarks |
| `ScorecardCalculatorErrorTests.cs` | 15 error scenarios |

---

## Deployment Insights (Added 2026-02-16)

### UCI Quick Create Cannot Refresh Parent Form

**Discovery**: The original design had the Quick Create form's `OnSave` event call the calculator API and then refresh the parent form via `window.parent.Xrm.Page.data.refresh(false)`. This does NOT work in UCI (Unified Client Interface).

**Root Cause**: In UCI, Quick Create forms open as flyout panels in the same window context. `window.parent.Xrm.Page` refers to the Quick Create form itself, not the parent entity form. All three refresh strategies fail:
1. `window.parent.Xrm.Page.data.refresh()` — references Quick Create, not parent
2. `window.top.Xrm.Page.data.refresh()` — same issue
3. `Xrm.Navigation.openForm()` — heavy approach, poor UX

**Solution**: Move the API call + refresh logic to the **parent form** using the subgrid `addOnLoad` event listener:
- Register `sprk_matter_kpi_refresh.js` on the Matter main form OnLoad
- It attaches a listener to the KPI Assessments subgrid
- When a Quick Create save adds a row, `addOnLoad` fires automatically
- The listener calls the calculator API from the parent form context
- After 1.5s delay (for Dataverse commit), `formContext.data.refresh(false)` re-reads all fields

**Pattern documented**: `.claude/patterns/webresource/subgrid-parent-rollup.md`

### Row Count Guard Prevents Infinite Refresh Loops

`formContext.data.refresh(false)` triggers the subgrid's `addOnLoad` event. Without a row count check, this creates an infinite loop:
```
subgrid change → API call → form refresh → subgrid OnLoad → API call → ...
```
Comparing `currentCount !== lastRowCount` breaks the cycle because `formContext.data.refresh()` doesn't change the row count.

### Web Resources Cannot Acquire Azure AD Tokens

Dataverse web resources (JavaScript on forms) cannot acquire Azure AD tokens for external APIs. The original endpoints required `RequireAuthorization()`, which returned 401 from web resource `fetch()` calls.

**Fix**: Added `.AllowAnonymous()` to the recalculate-grades endpoints with `RequireRateLimiting("dataverse-query")` as compensating control.

**Production TODO**: Replace with API key or service-to-service auth.

### Dataverse Event Handler Parameters Split JSON on Commas

The "Comma separated list of parameters" field in Dataverse form event handlers splits values on commas. This breaks JSON configuration strings like:
```json
{"subgridName":"subgrid_kpiassessments","apiPathTemplate":"/api/{entityPath}/{entityId}/recalculate-grades"}
```
Each key-value pair becomes a separate parameter. The generic config-driven web resource (`sprk_subgrid_parent_rollup.js`) cannot be used with JSON parameters.

**Workaround**: Use entity-specific web resources with hardcoded configuration (e.g., `sprk_matter_kpi_refresh.js`).

### Three Web Resource Variants

| File | Approach | Status |
|------|----------|--------|
| `sprk_matter_kpi_refresh.js` | Matter-specific, no parameters | **Deployed & working** |
| `sprk_kpi_subgrid_refresh.js` | Auto-detects Matter vs Project entity | Ready for Project form |
| `sprk_subgrid_parent_rollup.js` | Generic config-driven (JSON params) | Pattern reference only (Dataverse parameter limitation) |

---

*Generated: 2026-02-12, Updated: 2026-02-16*
