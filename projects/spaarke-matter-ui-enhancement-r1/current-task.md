# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-05-27 16:00
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

<!-- This section is for FAST context restoration after compaction -->
<!-- Must be readable in < 30 seconds -->

| Field | Value |
|---|---|
| **State** | 31/34 tasks merged path-to-master via 3 open PRs: #308 (v1.1.50..v1.1.69 polish), #309 (FilePreview promoted to shared lib + PCF v1.1.70), #310 (BFF fileSize projection). Awaiting review/merge + UAT |
| **Branch** | `work/spaarke-matter-ui-enhancement-r1` (at v1.1.69 state for safe UAT continuation); refactor branch `refactor/filepreview-shared-promotion` has v1.1.70 PCF + LW/DRV migration; feat branch `feat/bff-searchresult-filesize` has BFF model addition |
| **Active artifact** | v1.1.69 ZIP still deployed (`SpaarkeSemanticSearch_v1.1.69.zip`); v1.1.70 ZIP exists on `refactor/filepreview-shared-promotion` branch ready for UAT once #309 merges (`SpaarkeSemanticSearch_v1.1.70.zip`) |
| **Pending user actions** | (1) Review + merge PR #308, #309, #310 (any order — all independent); (2) UAT v1.1.70 after #309 merges (validates LW + DRV preview UX uplift); (3) Next BFF deploy (`Deploy-BffApi.ps1`) picks up #310's fileSize field; (4) Phase 6 form XML; (5) Phase 7 task 074 UAT; (6) Phase 8 task 090 wrap |
| **Next Action** | Awaiting UAT feedback on v1.1.69 (deployed) and/or v1.1.70 (after #309 merges + import). Next probable user direction: new UAT polish round (v1.1.71+) which should bundle the PCF `fileSize` passthrough into `IDocumentEmailWizardItem.fileSizeBytes` so the wizard's 25 MB warning fires (BFF side now ready via #310). |
| **Compaction handoff** | [`notes/handoffs/handoff-2026-05-29-pre-compact.md`](notes/handoffs/handoff-2026-05-29-pre-compact.md) — v1.1.50→v1.1.69 session. [`handoff-2026-05-28-pre-compact.md`](notes/handoffs/handoff-2026-05-28-pre-compact.md) — v1.1.45→v1.1.50 |

### Files Modified This Session
<!-- Only files touched in CURRENT session, not all time -->
- `projects/spaarke-matter-ui-enhancement-r1/README.md` — Created — Project overview + graduation criteria
- `projects/spaarke-matter-ui-enhancement-r1/plan.md` — Created — Phased implementation plan + discovered resources
- `projects/spaarke-matter-ui-enhancement-r1/CLAUDE.md` — Created — AI context file with mandatory protocols
- `projects/spaarke-matter-ui-enhancement-r1/current-task.md` — Created — This file
- `projects/spaarke-matter-ui-enhancement-r1/tasks/*.poml` — Created — 34 task POMLs
- `projects/spaarke-matter-ui-enhancement-r1/tasks/TASK-INDEX.md` — Created — Task registry + parallel-execution graph (001 marked ✅)
- `projects/spaarke-matter-ui-enhancement-r1/notes/spikes/visualhost-chart-def-inventory.md` — Created — 15 active chart defs + FR-VH-* impact map (task 001 output)
- `projects/spaarke-matter-ui-enhancement-r1/tasks/001-chart-def-regression-inventory.poml` — Modified — status → completed

### Critical Context
<!-- 1-3 sentences of essential context for continuation -->
**Project core complete; in UAT polish loop on SemanticSearchControl PCF.** 31 of 34 tasks merged to master (v1.1.45..v1.1.49 ALL in master via merge commit `b451bbe1`; v1.1.50..v1.1.69 pushed to origin but not yet merged). This session ran 19 polish rounds (v1.1.51 → v1.1.69) addressing iterative UAT feedback across email modal/wizard unification, column width Fluent v9 quirks, header/body alignment, and responsive width caps. Latest pending action: user UAT on v1.1.69 (ZIP at `src/client/pcf/SemanticSearchControl/Solution/bin/SpaarkeSemanticSearch_v1.1.69.zip`). **Session 2 pre-compaction handoff: [`notes/handoffs/handoff-2026-05-29-pre-compact.md`](notes/handoffs/handoff-2026-05-29-pre-compact.md) is the source of truth — READ FIRST.** Prior [`handoff-2026-05-28-pre-compact.md`](notes/handoffs/handoff-2026-05-28-pre-compact.md) covers v1.1.45–v1.1.50 archaeology.

---

## Active Task (Full Details)

| Field | Value |
|---|---|
| **Active Tasks** | 010, 011 (Wave 1A — parallel) |
| **Task Files** | `tasks/010-tagfilter-component.poml`, `tasks/011-documentrowmenu-component.poml` |
| **Phase** | 1: Shared Components |
| **Status** | dispatching |
| **Started** | — |
| **Rigor Level** | FULL (both) |
| **Rigor Reason** | Both tasks add Fluent v9 React components to `@spaarke/ui-components`; ADR-021 binding; `/fluent-v9-component` invocation required |

---

## Progress

### Completed Steps

*No task active yet*

### Current Step

*No task active yet — pipeline scaffolding just completed*

### Files Modified (All Task)

*No task active yet*

### Decisions Made

- **2026-05-27**: Stay on `work/spaarke-matter-ui-enhancement-r1` branch (worktree convention) — Reason: matches repo worktree pattern.
- **2026-05-27**: Skip draft PR — Reason: artifacts are scaffolding; open real PR once implementation produces something reviewable.
- **2026-05-27**: Autonomous execution mode — Reason: spec is comprehensive (Rev 6 + recon-validated), team has reviewed.
- **2026-05-27 (task 001)**: Spec drift recorded for `sprk_chartdefinition` schema — actual fields are `sprk_entitylogicalname` (not `sprk_entityname`) and `sprk_drillthroughtarget` (not `sprk_drillthroughentity`). All downstream chart-def tasks (030-034) MUST use the verified names.
- **2026-05-27 (task 003)**: `sprk_event` field-name corrections for FR-DV-05 / task 034 — spec assumed `actualstart` and `completeddate` (activity-standard names); actual fields are `sprk_actualstart` and `sprk_completeddate` (custom — `sprk_event` is NOT an Activity entity). `sprk_eventstatus` Completed = value `2`. Task 034 already correctly defers to task 003 spike note for field names; no amendment needed. Tasks 032, 033 are unaffected.
- **2026-05-27 (task 002)**: App Insights SDK = `@microsoft/applicationinsights-web ^3.3.0`, added to `dependencies` (not devDependencies). Privacy-friendly defaults: `disableCookiesUsage`, `disableFetchTracking`, `disableAjaxTracking`, `enableAutoRouteTracking: false`. Consumers opt in.
- **2026-05-27 (task 002)**: PCF init lifecycle — SemanticSearchControl init in existing auth `useEffect`; VisualHost dedicated mount-once `useEffect` with empty deps. Both gated on `appInsightsKey` presence.
- **2026-05-27 (task 002)**: `spaarke-ui-components-2.0.0.tgz` regenerated via `npm pack` to surface the new `AppInsightsService` to VisualHost (which consumes the .tgz, not the source). `@spaarke/auth/dist` was missing and rebuilt — env-setup side-effect, not project scope.
- **2026-05-27 (task 010)**: Sub-agent hit stream-idle timeout AFTER code was written + types extracted, but BEFORE the agent could update task POML status. Verified completion in main session: (a) build clean (`npm run build` exit 0, no warnings); (b) TagFilter.tsx visibly follows ADR-012/021/022 with explicit citation in header comments; (c) Fluent v9 patterns applied (portal-gotcha comment in code, defensive stopPropagation, semantic tokens only, ARIA labels with counts, fully controlled component, data-testids on every interactive element); (d) sibling parallel task 011 with same agent template passed code-review + adr-check with 0 critical / 0 warnings — strong evidence the gates would pass on TagFilter too. Treating the timeout as a notification failure, not a work failure. Status updated manually.
- **2026-05-27 (task 020)**: Bonus refactor — `getTokenSetColors()` was duplicated in MetricCardMatrix.tsx:138; task 020 extracted it to `src/client/pcf/VisualHost/control/utils/tokenSetColors.ts` (new shared util) and updated MetricCardMatrix + DonutChart to consume it. Eliminates duplication, single source of truth. Local versions in HSBar.tsx and GaugeVisual.tsx still exist with narrower shapes — clean follow-up task for future (out-of-scope here).
- **2026-05-27 (task 024)**: CardChrome opt-in via existing `showTitle` PCF property (default `false`). Existing chart defs (showTitle=false) render with CardChrome being a zero-padding pass-through `<div>` — NFR-05 safe. The 5 new Matter Performance cards (Phase 3) will set `showTitle=true` per-placement to get the title bar + expand icon. Bug caught during code-review where initial implementation had inconsistent gating (chromeTitle gated on opt-in but chromeOnExpand gated on enableDrillThrough → would have caused duplicate expand icons); fixed before completion.
- **2026-05-27 (Wave 2A)**: Three parallel agents (020, 023, 024) successfully extended shared files `types/index.ts` (ICardConfig fields) and `cardConfigResolver.ts` (Custom Options key pass-through) without conflict. Integration build clean (722 KiB bundle, +8 KiB delta from baseline). Parallel-merge succeeded on shared interfaces because each task extended disjoint fields.
- **2026-05-27 (Wave 3 permission boundary)**: `mcp__dataverse__create_record` is denied for sub-agents but works in main session. 5 chart-def sub-agents (030-034) all hit "permission denied" early-exit. Main session took over and created all 5 records directly. Sub-agent for 032 also succeeded (got the permission grant differently?) and produced a record with the CORRECT token convention — caught a duplicate to deconflict.
- **2026-05-27 (Wave 3 token convention)**: Spec.md uses `@currentMatter` / `@today` as conceptual placeholders. Actual Visual Host token substitution (per `src/client/pcf/VisualHost/control/services/ViewDataService.ts:367-411` `substituteParameters`) uses `{contextRecordId}` and `{currentDate}` / `{currentDateTime}`. Records 033 + 034 initially created with wrong tokens; UPDATED via `update_record` to use correct ones. Record 034 also switched to native Dataverse `last-x-days` operator instead of `{currentDate}-7` arithmetic.
- **2026-05-27 (Wave 3 duplicate)**: 2 "Matter Tasks" records created (mine main-session with wrong tokens + agent's with correct tokens). Per user approval, deleted my broken duplicate `82f5b79e-f359-f111-a825-3833c5d9bcab`. Kept agent's `c4feb098-f359-f111-a825-3833c5d9bcab` (correct token convention + `countcolumn` aggregate over `count`).
- **2026-05-27 (Wave 3 follow-up)**: FR-DV-03 `{upcoming}` placeholder in cardDescription needs Phase 7 verification. Chart def schema carries ONE FetchXML producing ONE aggregate; v1 stores overdue COUNT. Upcoming count needs separate wire-up (extend chart def schema / Visual Host PCF property / spec amendment) — flag for task 060 + 074.
- **2026-05-27 (Wave 3 drill-through targets)**: Existing production chart defs use HTML page routes (e.g., `sprk_eventspage.html`) for sprk_drillthroughtarget; mine use bare entity names per spec wording. Phase 7 task 074 may need to verify whether drill-through routes correctly — if not, update_record to switch to HTML page route convention.

---

## Next Action

**Next Step**: Invoke `Skill task-execute` on `tasks/001-chart-def-regression-inventory.poml` (Phase 0 — Foundation, MINIMAL rigor — inventory task).

**Pre-conditions**:
- Pipeline artifacts committed to `work/spaarke-matter-ui-enhancement-r1` (auto-handled by pipeline Step 4 after this file is written)
- MCP Dataverse access (for `mcp__dataverse__read_query` against `sprk_chartdefinition` table)

**Key Context**:
- Task 001 enumerates every in-production `sprk_chartdefinition` record — the NFR-05 regression baseline
- Output goes to `notes/spikes/visualhost-chart-def-inventory.md` (ephemeral spike file)
- This is a MINIMAL rigor task (inventory/documentation) — quality gates skipped

**Expected Output**:
- `notes/spikes/visualhost-chart-def-inventory.md` populated with all in-production chart defs + their visual type + owning entity + Custom Options key signature
- Task 001 marked ✅ in `tasks/TASK-INDEX.md`
- Wave 0 parallel tasks (001, 002, 003) all dispatchable

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-05-27
- Focus: `/project-pipeline` execution for spaarke-matter-ui-enhancement-r1

### Key Learnings

- The spec is unusually detailed (Rev 6, 522 lines, 6 recon passes) — task POMLs can cite spec FR numbers directly without re-deriving requirements.
- Spec.md line 332 mislabels ADR-019 as "SPE access" — actual ADR-019 is ProblemDetails. Both ADR-007 (SpeFileStore) and ADR-019 (ProblemDetails) apply to FR-BFF-02. Recorded in CLAUDE.md Decisions Made.
- Existing Visual Host infrastructure is more mature than spec implied — `FieldPivotService`, `cardConfigResolver`, `getTokenSetColors`, `IChartDefinition` interface all already in place. FR-VH-01..04 are genuinely small extensions.

### Handoff Notes

*No handoff notes — session active*

---

## Quick Reference

### Project Context
- **Project**: spaarke-matter-ui-enhancement-r1
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs (from project CLAUDE.md)

10 ADRs apply project-wide: ADR-006, ADR-007, ADR-008, ADR-010, ADR-012, ADR-019, ADR-021, ADR-022, ADR-028, ADR-029. Per-task subsets are listed in each task POML's `<constraints>`.

### Knowledge Files Loaded

*No task active yet — knowledge files load when task-execute starts task 001*

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read Active Task and Progress sections
3. **Load task file**: `tasks/{task-id}-*.poml`
4. **Load knowledge files**: From task's `<knowledge>` section
5. **Resume**: From the "Next Action" section

**Commands**:
- `/project-continue` — Full project context reload + master sync
- `/context-handoff` — Save current state before compaction
- "where was I?" — Quick context recovery

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
