# VisualHost NFR-05 Code-Only Backward-Compat Analysis (task 025)

> **Purpose**: Code-only NFR-05 verification for Phase 2 (tasks 020–024). The LIVE visual regression smoke (deploy + render every chart def + compare screenshots) is **DEFERRED to Phase 7 task 074** per task-025 scope reduction (autonomous mode without dev env access).
> **Analyzed**: 2026-05-27
> **Analyst**: Claude Code (STANDARD rigor — verification task with binding NFR-05)
> **Source of truth**: post-Wave-2BC renderer code on `work/spaarke-matter-ui-enhancement-r1` (commit `a111a633`)
> **Baseline reference**: `projects/spaarke-matter-ui-enhancement-r1/notes/spikes/visualhost-chart-def-inventory.md` (15 in-prod chart defs)

---

## TL;DR

**SIGN-OFF**: All 15 in-prod chart defs render via code paths that are byte-identical to pre-Phase-2 state. **Proceed to Phase 3.**

Every new Custom Options key introduced by FR-VH-01..05 is gated such that **absence in `sprk_optionsjson` preserves prior behavior**. The shared `getTokenSetColors` util extraction (task 020) is a strict superset of the previous MetricCardMatrix-local function — return shape unchanged for the matrix consumer. HSBar and GaugeVisual retain their own (narrower-shape) local copies untouched.

---

## Per-chart-def analysis

| # | Name | Visual Type | Owning Entity | Renderer File | New Code Path Triggered? | Gating Logic | Verdict |
|---|---|---|---|---|---|---|---|
| 1 | Documents by Document Type | BarChart | sprk_document | `BarChart.tsx` (NOT modified in Phase 2) | No | N/A — Bar Chart renderer untouched by FR-VH-01..05 | ✅ Pass |
| 2 | Due Date by Event Type Matrix | MetricCard | sprk_event | `MetricCardMatrix.tsx` (multi-DP) | No | `badge` undefined → matrix never reads it; `descriptionColor` undefined → matrix ignores (consumed by `MetricCard.tsx` single-card path only); `cardConfigResolver` returns undefined for all new keys | ✅ Pass |
| 3 | Due Date Card List | DueDateCardList | sprk_event | `DueDateCardList.tsx` (NOT modified) | No | FR-DV-04 explicitly carves out DueDateCard from Phase 2 changes; only CardChrome wraps it, and chrome is opt-in via `showTitle` PCF prop (defaults to false) | ✅ Pass |
| 4 | Due Date Count Card | MetricCard | sprk_event | `MetricCard.tsx` (single-DP, `dataPoints.length<=1`) | No | `badge`/`descriptionColor` absent in optionsjson → resolver returns `undefined` → both props short-circuit in MetricCard (lines 392, 402) | ✅ Pass |
| 5 | Events by Event Type | BarChart | sprk_event | `BarChart.tsx` (NOT modified) | No | N/A | ✅ Pass |
| 6 | **Matter Financial Metrics Scorecard** | MetricCard (matrix) | sprk_matter | `MetricCardMatrix.tsx` (multi-DP, fieldPivot) | No | Uses `fieldPivot`/`columns`/`colorSource: signBased`/`sortBy` — none of these are touched by Phase 2. New keys `badge`/`descriptionColor` absent → ICardConfig fields undefined; matrix never reads them. `getTokenSetColors` for `signBased` path returns same shape as old local function (line 137 = `getSharedTokenSetColors`) | ✅ Pass |
| 7 | **Matter Financial Metrics Stacked Bar** | HorizontalStackedBar | sprk_matter | `HorizontalStackedBar.tsx` | No | Uses `fieldPivot`/`valueFormat`/`colorSource`/`colorThresholds`/`aiSummaryField`. `layoutMode` absent → `isHeadlineLayout === false` (line 254) → original render branch (lines 312–356). Local `getTokenSetColors` untouched (HSBar still uses its own narrow-shape local copy, lines 56–70) | ✅ Pass |
| 8 | Matter KPI Score Card Gauges | Gauge | sprk_matter | `GaugeVisual.tsx` (NOT modified in Phase 2) | No | Gauge has its own local `getTokenSetColors` (narrow `IGaugeColorTokens` shape) untouched by task 020. CardChrome wrap is opt-in (showTitle=false default) → transparent pass-through | ✅ Pass |
| 9 | **Matter KPI Scorecard** | MetricCard (matrix) | sprk_matter | `MetricCardMatrix.tsx` (multi-DP, fieldPivot, iconMap) | No | Uses `fieldPivot`/`columns`/`iconMap`/`colorThresholds` + `valueFormat: Letter Grade` + `colorSource: Value Threshold`. New keys `badge`/`descriptionColor` absent. iconMap path (lines 108–121) unchanged by Phase 2. `colorThresholds` valueThreshold path goes through shared `getTokenSetColors`, which returns same `cardBackground`/`borderAccent`/`valueText`/`iconColor` shape | ✅ Pass |
| 10 | Number of Documents Card | MetricCard | sprk_document | `MetricCard.tsx` (single-DP) | No | Same gating as #4 | ✅ Pass |
| 11 | Open Tasks | BarChart | sprk_document | `BarChart.tsx` (NOT modified) | No | N/A | ✅ Pass |
| 12 | **Project Financial Metrics Scorecard** | MetricCard (matrix) | sprk_project | `MetricCardMatrix.tsx` | No | Mirror of #6 | ✅ Pass |
| 13 | Project Financial Metrics Stacked Bar | HorizontalStackedBar | sprk_project | `HorizontalStackedBar.tsx` | No | Mirror of #7 | ✅ Pass |
| 14 | **Project KPI Scorecard** | MetricCard (matrix) | sprk_project | `MetricCardMatrix.tsx` | No | Mirror of #9 | ✅ Pass |
| 15 | Project KPI Scorecard Gauges | Gauge | sprk_project | `GaugeVisual.tsx` (NOT modified) | No | Mirror of #8 | ✅ Pass |

**Tally**: 15 Pass · 0 Risk · 0 Critical.

---

## FR-VH-* gating verification (line-number citations)

Every new key is gated so that absence in `sprk_optionsjson` preserves pre-Phase-2 behavior. Cited lines are from the current `work/spaarke-matter-ui-enhancement-r1` branch.

### FR-VH-01 — Donut layout, center mode, breakdown rows

- **`donutLayout` absent or `"standard"` → original Donut render path**: `DonutChart.tsx:237` (`donutLayout = cardConfig?.donutLayout ?? 'standard'`); `DonutChart.tsx:285` gates the `matrixRight` branch behind `if (donutLayout === 'matrixRight')` — when false, control falls to the standard branch at lines 336–354 which renders the same `FluentDonutChart` shape as before.
- **`donutCenterMode` absent → `"total"` default behavior**: `DonutChart.tsx:238` (`donutCenterMode = cardConfig?.donutCenterMode ?? 'total'`); `DonutChart.tsx:266–269` — the `else` branch evaluates `total.toLocaleString()` exactly as before.
- **`colorThresholds` absent → undefined thresholdColor**: `DonutChart.tsx:248` (`thresholdColor = resolveSegmentColor(...)` returns undefined when thresholds unset, lines 150–159); line 252 `point.color || thresholdColor || palette[index]` falls through to palette — same as before.
- **Resolver gating**: `cardConfigResolver.ts:232–250` whitelists `donutLayout`/`donutCenterMode`/`breakdownValueFormat` against the typed union — any malformed value returns `undefined` (no behavioral fallback to a broken state).
- **In-prod Donut chart defs affected**: **ZERO** (inventory line 25). FR-VH-01 carries zero NFR-05 risk against the 15 live records — it ships cleanly via FR-DV-01 (Matter Health Composite).

### FR-VH-02 — MetricCard badge slot

- **`badge` absent → no Badge rendered (short-circuit)**: `MetricCard.tsx:392` (`{badge && (<Badge ... />)}`). React short-circuit — when `badge === undefined`, the `<Badge>` element is never created, no DOM mutation.
- **Resolver gating**: `cardConfigResolver.ts:256` calls `parseBadge(json.badge)`; lines 304–316 require a valid `text`/`tone`/`position` triple — any malformed object returns `undefined`.
- **In-prod chart defs affected**: 0 of 7 MetricCards (none set `badge` in optionsjson).

### FR-VH-03 — MetricCard description color

- **`descriptionColor` absent or `"neutral"` → existing `colorNeutralForeground3`**: `MetricCard.tsx:402` (`style={descriptionColor ? { color: descriptionColorToToken(descriptionColor) } : undefined}`) — when undefined, no inline style is applied, so the existing `styles.description` CSS class (line 151 `color: tokens.colorNeutralForeground3`) is the actual color. An explicit `"neutral"` value at lines 232–234 also returns `colorNeutralForeground3` — same token, byte-identical.
- **Resolver gating**: `cardConfigResolver.ts:263` calls `parseDescriptionColor(json.descriptionColor)`; lines 324–328 reject anything outside the 5-value whitelist (returns `undefined`).
- **In-prod chart defs affected**: 0 of 7 MetricCards.

### FR-VH-04 — HSBar headline layout

- **`layoutMode` absent or `"default"` → existing HSBar render path**: `HorizontalStackedBar.tsx:254` (`isHeadlineLayout = cardConfig?.layoutMode === 'headlineAboveBar'`) — boolean comparison; undefined/`"default"` both yield `false`. Lines 312, 343 (`{!isHeadlineLayout && ...}`) gate the original top-right total + bottom-row spent/remaining blocks; lines 298–309 gate the new headline stack behind `{isHeadlineLayout && ...}`.
- **Resolver gating**: `cardConfigResolver.ts:223–225` — `layoutMode` is set only when the raw value is exactly `"headlineAboveBar"` or `"default"`; otherwise undefined → render path identical to today.
- **In-prod chart defs affected**: 0 of 2 HSBars (neither #7 nor #13 sets `layoutMode`).

### FR-VH-05 — CardChrome wrap

- **CardChrome wrap with `showTitle !== true` → header NOT rendered**: `VisualHostRoot.tsx:522` (`chromeOptIn = showTitlePcf === true`); lines 523–525 — `chromeTitle` becomes `undefined` when `chromeOptIn === false`. `CardChrome.tsx:128–135` derives `renderHeader = hasTitle || hasAnyIcon`; when title is undefined and `onExpand` is also undefined (line 529: gated on `chromeOptIn`), `renderHeader === false` and the header `<div>` is never rendered (line 139). The body is rendered as a single `<div className={styles.body}>` — a transparent wrapper around `children`.
- **All-15-defs behavior**: every existing chart-def placement leaves the `showTitle` PCF property at its default (`false`). `chromeOptIn` evaluates `false` → CardChrome renders as `<div class="root"><div class="body">{children}</div></div>` — pure pass-through, no header, no icon slots.
- **Wrapper dimensional impact**: `CardChrome.tsx` root style (lines 71–77) is `display: flex; flex-direction: column; width: 100%; min-width: 0; box-sizing: border-box;` — no padding, no margin, no fixed height. Body (lines 107–113) is the same with `flex-grow: 1`. The wrap adds two `<div>` elements with zero box-model contribution — visually transparent.
- **Existing toolbar continues to handle expand**: `VisualHostRoot.tsx:580–604` — the existing toolbar (with AI Summary + View Details icons) renders independently of CardChrome. CardChrome is additive ONLY when callers opt in via `showTitle=true`; for the 15 in-prod placements, the legacy toolbar is the sole carrier of the expand-icon UX.

---

## Shared util refactor verification (`getTokenSetColors` — task 020)

Task 020 extracted the previously-local `getTokenSetColors` from `MetricCardMatrix.tsx:138` into a new shared util at `src/client/pcf/VisualHost/control/utils/tokenSetColors.ts`. This was required because `DonutChart.tsx` (new code path) needed to reuse the same `tokenSet` vocabulary. Verification:

| Check | Evidence |
|---|---|
| MetricCardMatrix imports from shared util | `MetricCardMatrix.tsx:35` — `import { getTokenSetColors as getSharedTokenSetColors, type ITokenSetColors } from '../utils/tokenSetColors';` |
| Local alias preserves API for the rest of the file | `MetricCardMatrix.tsx:137` — `const getTokenSetColors = getSharedTokenSetColors;` — every existing call site (lines 165, 169, 177, 179, 181) is unchanged |
| Return shape unchanged for matrix consumer | Pre-extraction local definition returned `cardBackground` + `borderAccent` + `valueText` + `iconColor` (4-field bag). Shared `ITokenSetColors` (`tokenSetColors.ts:29–34`) has the same 4 fields with the same semantics. `ICardColorTokens` (`MetricCardMatrix.tsx:130`) is now a type alias to `ITokenSetColors` — identical shape |
| Token values unchanged | Spot-checked all 5 branches: `brand`, `warning`, `danger`, `success`, `neutral` return the exact same `tokens.*` references as the pre-extraction switch (cross-verified against git history `c7089e40` Wave 2A) |
| HSBar's local copy untouched | `HorizontalStackedBar.tsx:56–70` — HSBar still has its own local `getTokenSetColors` returning `{ borderAccent?: string }` (narrower shape — HSBar only needs the bar fill color). Task 020 deliberately did NOT touch HSBar's local copy (it was added in a later wave for HSBar's specific narrow shape; no cross-contamination) |
| GaugeVisual's local copy untouched | `GaugeVisual.tsx:70` — GaugeVisual has its own local `getTokenSetColors` returning `IGaugeColorTokens` (`arcColor` + `valueTextColor` — narrower than matrix shape because the SVG gauge needs only arc + text color). Untouched by task 020. Gauge chart defs #8 and #15 are byte-identical to today |
| DonutChart consumes shared util | `DonutChart.tsx:24` — `import { getTokenSetColors } from '../utils/tokenSetColors';` (consumed at line 155 in `resolveSegmentColor`) — uses only `borderAccent`, ignoring the other 3 fields. No behavioral coupling to matrix consumers |

**Conclusion**: the `getTokenSetColors` extraction is a strict drop-in for `MetricCardMatrix` and an additive consumer for `DonutChart`. HSBar and GaugeVisual were intentionally left with their narrow-shape local copies — DRY remediation for those is a future task (not in Phase 2 scope) and does not affect NFR-05.

---

## What Phase 2 actually changed (cross-check)

For completeness, the only code paths touched by tasks 020-024 are:

1. **`DonutChart.tsx`** (task 020): added `cardConfig` prop + `matrixRight` layout branch + center-mode logic. All gated behind props that default to `undefined`/`"standard"`/`"total"`.
2. **`MetricCard.tsx`** (tasks 021 + 022): added `badge` and `descriptionColor` props. Both short-circuit when undefined.
3. **`HorizontalStackedBar.tsx`** (task 023): added `layoutMode === 'headlineAboveBar'` branch. Original render path remains the default.
4. **`CardChrome.tsx`** (task 024 — NEW file): per-card wrapper. `renderHeader` is gated on `hasTitle || hasAnyIcon` — both default to false for in-prod chart defs.
5. **`VisualHostRoot.tsx`** (task 024): wraps `<ChartRenderer />` in `<CardChrome>`. `chromeOptIn = showTitlePcf === true` — defaults to false for legacy placements.
6. **`ChartRenderer.tsx`** (tasks 020 + 023): added `cardConfig` resolution for Donut + HSBar code paths. Pre-existing MetricCard + Gauge resolution unchanged in structure.
7. **`MetricCardMatrix.tsx`** (task 020): imports `getTokenSetColors` from shared util instead of defining it locally. Return shape + token values identical.
8. **`cardConfigResolver.ts`** (tasks 020–022): parses new keys (`donutLayout`, `donutCenterMode`, `donutCenterLabel`, `showBreakdownRows`, `breakdownValueFormat`, `badge`, `descriptionColor`, `layoutMode`, `headlineFromField`, `subLineTemplate`). Each key is whitelist-validated; malformed/absent input → `undefined` field on `ICardConfig`.
9. **`types/index.ts`** (tasks 020–023): added types for the above keys (`BadgeTone`, `IBadgeConfig`, `DescriptionColorValue`, `donutLayout`/`donutCenterMode`/`breakdownValueFormat` unions, `layoutMode` union). All additive — no existing field renamed/removed.
10. **`utils/tokenSetColors.ts`** (task 020 — NEW file): shared util, see "Shared util refactor verification" above.

No existing field was removed. No existing prop default was changed. No existing code path was modified except to add a new branch behind an undefined-by-default guard.

---

## Sign-off

> **All 15 in-prod chart defs render via code paths byte-identical to pre-Phase-2 state. Proceed to Phase 3.**

Confidence is high because:

- Every new Custom Options key has a whitelist-validated resolver that returns `undefined` on absence/malformed input.
- Every renderer short-circuits new behavior when the corresponding `ICardConfig` field is `undefined` (verified by line-number citations above).
- CardChrome (FR-VH-05) is opt-IN via the existing `showTitle` PCF prop, which defaults to `false`. All 15 in-prod chart defs use the default → CardChrome renders as a transparent two-`<div>` pass-through wrapper with zero box-model impact.
- The shared `getTokenSetColors` extraction preserves the exact same return shape and token values for `MetricCardMatrix` (the only Phase-2-touched consumer of that function). HSBar and GaugeVisual retain their narrow-shape local copies untouched.

---

## Deferred work

**LIVE visual regression smoke** (deploy + render each chart def in dev + capture light/dark/axe screenshots + diff against baseline) is **DEFERRED to Phase 7 task 074**. Reasons:

1. Autonomous code-only mode in this session — no Power Platform CLI authentication, no dev environment access.
2. Phase 7 task 074 (`cross-cutting NFR validation`) is the natural cross-cutting NFR gate — it re-validates every record AFTER all Phase 4/5/6 work has shipped, catching interaction regressions that emerge only when the full project's surface is active.
3. Per-task evidence already exists for tasks 020–024 (donut-baseline, metriccard-baseline-021, metriccard-baseline-022, hsbar-baseline, cardchrome-baseline) — this code-only analysis cross-references those.

Task 074 MUST execute the full live smoke from steps 2–7 of the original task 025 prompt before closing the project.

---

## Out-of-scope DRY observations (not blocking — for future tasks)

- HSBar (`HorizontalStackedBar.tsx:56–70`) and GaugeVisual (`GaugeVisual.tsx:70`) still have their own LOCAL `getTokenSetColors` implementations with narrower return shapes. These are NOT broken — each is internally consistent. A future cleanup could consolidate to the shared `tokenSetColors.ts` util with consumers picking the fields they need (the shared shape is already a superset). Tracked in spec.md §FR-VH-01 follow-ups; not in Phase 2 scope.
- `MetricCardMatrix.tsx:137` keeps a thin alias (`const getTokenSetColors = getSharedTokenSetColors`) instead of an inline rename — intentional minimal-diff to preserve the local symbol name across all call sites. Future cleanup could inline the rename.

---

*Code-only analysis. Live regression smoke is task 074's responsibility. Sign-off above applies to backward-compat CODE PATHS — not to deployed runtime behavior, which task 074 will validate.*
