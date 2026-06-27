# R5 Sparkle-Wiring Baseline — Task 002 Deliverable

> **Status**: ✅ RESOLVED
> **Task**: `002-r5-sparkle-wiring-baseline.poml` (Phase 0, Wave 0, STANDARD rigor)
> **Authored**: 2026-06-10
> **Resolves**: spec.md Assumptions 2 + 11 (OPEN INVESTIGATION marker)

---

## Verdict (decision)

**Phase 4 is NET-NEW Matter form customization.**

There is **no R5 Matter form OnLoad handler that wires a sparkle icon to `sprk_performancesummary`** to replace. The R5 project shipped a different vertical slice entirely (chat-driven Summarize-document + Insights tool consumption — see §2 evidence). The single sparkle/AI affordance referencing `sprk_performancesummary` in the codebase lives inside the **VisualHost PCF chart toolbar** as a per-card `AiSummaryPopover`, NOT on the Matter form itself.

Spec Assumptions 2 + 11 — which assumed an existing R5 wiring to be REPLACED — are incorrect about the existence of an R5 sparkle wiring on the Matter form. The R5 placeholder data described in spec §Resolution table (where R5 "added sparkle placeholder linked to `sprk_financialsummary`, `sprk_performancesummary`, `sprk_tasksummary`") is realized via the **VisualHost PCF cards on the Matter form's Report Card tab**, not as an OnLoad handler that injects a sparkle.

Phase 4 therefore implements:
- NEW Matter main form OnLoad handler that reads `sprk_performancesummary`, parses the JSON envelope, and fires the background pre-warm if stale (per FR-17 / FR-18).
- NEW `InsightSummaryCard` host on the Matter form (location TBD in Phase 4 design — likely Report Card tab next to existing VisualHost trend cards, or a new dedicated section).
- NO removal / decommission step needed for an R5 popup or R5 sparkle wiring on the Matter form — none exists at that level.

The existing VisualHost-toolbar `AiSummaryPopover` instances on Report Card VisualHost cards are **out of scope for r1 Phase 4**: they belong to the VisualHost PCF, not the Matter form customization layer. They continue to function as-is (reading static text from the summary fields) until the playbook overwrites those fields with JSON envelopes — at which point per FR-17 their text rendering may need a follow-up `.body` extraction adjustment, captured here as a downstream concern (NOT an r1 Phase 4 task).

---

## Evidence

### 1. src tree grep — Matter form OnLoad handlers

Searched: `addOnLoad`, `Xrm.Page`, `formContext.data.entity`, `sprk_matter`, `OnLoad` across `src/**/*.{ts,js,tsx,jsx,xml}`.

The only Matter main form OnLoad handler in src is:
- **File**: `src/solutions/webresources/sprk_matter_kpi_refresh.js`
- **Header comment** (lines 17–22): `Event: OnLoad / Library: sprk_/scripts/matter_kpi_refresh.js / Function: Spaarke.MatterKpi.onLoad`
- **Function** (line 146): `Spaarke.MatterKpi.onLoad = function (executionContext) { … }`
- **Behavior**: Resolves BFF base URL from `sprk_BffApiBaseUrl` environment variable, waits for `subgrid_kpiassessments` to render, attaches a subgrid OnLoad listener that calls the BFF calculator API when KPI assessments change, then refreshes form data so grade fields update.
- **No sparkle reference.** No `sprk_performancesummary` reference. No AI/widget invocation. Pure KPI grade recalculation infrastructure (predecessor: `matter-performance-KPI-r1` per the form-XML header).

No other Matter form OnLoad handler exists in src. No `Spaarke.AI.Matter.*` namespace. No `MatterFormSparkle.*` namespace. The codebase does not contain a Matter form sparkle/AI handler that Phase 4 would replace.

### 2. src grep — `sprk_performancesummary` references

```
src/client/pcf/VisualHost/control/types/index.ts:484
src/client/pcf/VisualHost/control/types/index.ts:485   (sprk_financialsummary)
src/client/pcf/VisualHost/control/types/index.ts:486   (sprk_tasksummary)
```

Only documentation comment in the VisualHost PCF type definitions, referring to example mappings for the `aiSummaryField` chart-config property. No Matter form binding. No JavaScript handler. No FormXml reference.

### 3. R5 project artifacts — what R5 actually shipped

Read `projects/spaarke-ai-platform-unification-r5/README.md`, `CLAUDE.md`, and grepped for sparkle / matter form / performance summary across the full R5 project folder.

R5 shipped (per README §Outcome):
- Phase 1 platform extensions: session-files Azure Search index, RagIndexingPipeline session writes, ChatSession.UploadedFiles, FieldDelta SSE variant, Structured Outputs wiring, cleanup IHostedService, telemetry.
- Phase 2 vertical slice: `SessionSummarizeOrchestrator`, `/summarize` endpoint, `InvokeSummarizePlaybookTool`, `StructuredOutputStreamWidget`, PaneEventBus events, intent matcher, `executeSummarizeIntent`, `sseToPaneEventBridge`, FR-07 chat attachments.
- Phase 2 closeout: `WorkspaceTabManager.prependTab` + Summary tab installer + auto-focus + JSONPath strip.

R5 deferred to R6: schema-aware renderer for array/object fields, duplicate-fire defect, Insights renderer + clickable citations + confidence floor, DocumentViewerWidget upgrade, Context-pane execution-trace widget, Phase 3 polish.

**R5 had no Matter form deliverable.** Grep hits in R5 for "matter" all refer to subject scheme `matter:<guid>` for the Insights tool consumption contract (Phase 2 task 024/025 subject resolution). No matter form OnLoad work. No sparkle icon wiring on the Matter form. The R5 sparkle work is `SparkleRegular` Fluent icons inside chat / widget contexts (e.g., task 017 StructuredOutputStreamWidget header icon; task 021 per-file affordance), not on the Matter form.

### 4. What "sparkle" + `sprk_performancesummary` actually is in the codebase

Located in the **VisualHost PCF** (a chart card control):

| Location | Purpose |
|---|---|
| `src/client/pcf/VisualHost/control/components/CardChrome.tsx:146` | Renders `<AiSummaryPopover>` in card header chrome when card has `showAiSparkle` |
| `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx:718,744` | Renders `<AiSummaryPopover>` in card toolbar when `aiSummaryField` is configured |
| `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx:221` | Fetches the AI summary string via `context.webAPI.retrieveRecord(entityName, recordId, '?$select=' + aiSummaryField)` |
| `src/client/pcf/VisualHost/control/utils/cardConfigResolver.ts:276-280, 319` | Resolves `aiSummaryField` from card-config JSON |
| `src/client/pcf/VisualHost/control/types/index.ts:473-490` | Type definition + canonical comment with mappings: `Matter Health Composite → sprk_performancesummary`, `Financial → sprk_financialsummary`, `Task → sprk_tasksummary` |
| `src/solutions/SpaarkeCore/entities/sprk_matter/FormXml/reportcard/matter-reportcard-tab.xml:39` | Matter Report Card tab FormXml contains "Placeholder cells for 3 trend VisualHost cards" |

The sparkle icon on a VisualHost card is a **PCF-internal toolbar affordance** that fetches whatever string is in the configured Dataverse longtext field and renders it in a Fluent v9 Popover. There is no Matter form OnLoad handler involved; the PCF component runs inside its own lifecycle inside its bound cell on the Report Card tab.

This means: the "R5 added sparkle placeholder linked to `sprk_performancesummary`" statement in the spec table refers to the VisualHost PCF + Matter Report Card tab placement, NOT a Matter form OnLoad handler. (The actual PCF + tab work appears to have been a `matter-performance-KPI-r1` deliverable per the FormXml header, predating R5.)

### 5. Dataverse MCP live form inspection — NOT executed

The task POML Step 3 (`mcp__dataverse__describe` on `sprk_matter` form metadata) could not run: no Dataverse MCP `.env` is present in this worktree (`c:/code_files/spaarke-wt-ai-spaarke-insights-engine-widgets-r1/.env` does not exist). Per the dv-connect skill, MCP requires explicit setup before invocation.

**Impact assessment**: low. The src tree IS the authoring source of record for solution-controlled Matter form customizations in this repo (CLAUDE.md §2 "Source of Truth: Code, then `.claude/`, then `docs/`"). If an unmanaged customization existed in the dev environment that was NOT checked into src, it would not be a maintainable baseline anyway — Phase 4 would still implement net-new managed customization regardless of any rogue unmanaged delta. The decision (NET-NEW) is robust to that uncertainty.

**Mitigation if live-environment confirmation is desired** before Phase 4 begins: an operator with MCP access can run:
```
mcp__dataverse__describe { entityName: "sprk_matter" }
```
…and (separately, via `Xrm.WebApi` or PAC CLI in the dev environment) retrieve the published systemform XML for Matter main form and grep for `Spaarke.AI.Matter` / `sprk_performancesummary` / non-`MatterKpi` OnLoad handlers. If found, file a Phase 4 amendment. If not found (expected), the decision is confirmed.

---

## Implications for spec.md

| Assumption | Old text (in part) | Resolution |
|---|---|---|
| 2 | "Old R5 popup is decommissioned. **OPEN INVESTIGATION**: …" | RESOLVED: No R5 popup on the Matter form to decommission. Phase 4 is net-new Matter form customization. |
| 11 | "R5 sparkle icon currently wired — assumed exists on Matter form pointing to `sprk_performancesummary` … **OPEN INVESTIGATION**" | RESOLVED: No Matter form OnLoad sparkle wiring exists; the only `sprk_performancesummary` consumer is the VisualHost PCF toolbar `AiSummaryPopover` (PCF-internal, not Matter form). Phase 4 is NET-NEW. |

---

## Implications for Phase 4 task framing

Phase 4 (Matter form integration, tasks 040–044) frames as **net-new customization**:

1. **NEW** Matter main form OnLoad handler (web resource) — name candidate: `sprk_matter_insightprewarm.js` namespace `Spaarke.MatterInsight`. Coexists with `Spaarke.MatterKpi.onLoad`; both register on Matter main form OnLoad. Per FR-17 / FR-18: read `sprk_performancesummary`, attempt JSON.parse, check `.generatedAt`, fire `/api/insights/ask` if stale.
2. **NEW** Matter form section / cell hosting `InsightSummaryCard` (location to be decided in Phase 4 design — owner question). The card uses Fluent v9 + `@spaarke/ai-widgets` (per FR-03 resolution).
3. **NO** removal of an old sparkle handler. The existing `Spaarke.MatterKpi.onLoad` is untouched.
4. **DOWNSTREAM** (NOT r1 Phase 4 — track as follow-up): once playbook starts writing JSON envelopes to `sprk_performancesummary`, the VisualHost `AiSummaryPopover` for the Matter Health Composite card will render JSON text instead of human prose. If the popover needs to render `.body` extracted from the envelope, that's a VisualHost PCF enhancement, separate from r1 Phase 4. Captured as a known consequence here.

---

*Authored by task-execute task 002, STANDARD rigor. No code modified.*
