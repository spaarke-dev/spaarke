# Task Index: Spaarke Matter UI Enhancement R1

> **Generated**: 2026-05-27 by `/project-pipeline`
> **Total Tasks**: 34 across 9 phases
> **Project**: [`projects/spaarke-matter-ui-enhancement-r1/`](..)
> **Spec**: [`../spec.md`](../spec.md) (Rev 6 — recon-validated)
> **Plan**: [`../plan.md`](../plan.md)
> **Project CLAUDE.md**: [`../CLAUDE.md`](../CLAUDE.md)
> **Current Active Task State**: [`../current-task.md`](../current-task.md)

## Status Legend

| Symbol | Meaning |
|---|---|
| 🔲 | Not started |
| 🔄 | In progress / needs retry |
| ✅ | Completed |
| ⛔ | Blocked |

---

## Task Registry

| ID | Title | Phase | FR | Status | Dependencies | Parallel Group | Rigor |
|---|---|---|---|---|---|---|---|
| 001 | Enumerate in-production sprk_chartdefinition records (NFR-05 baseline) | 0 | NFR-05 | ✅ | none | A | MINIMAL |
| 002 | App Insights shared wiring (FR-TEL-01 infrastructure) | 0 | FR-TEL-01 | ✅ | none | A | FULL |
| 003 | Verify sprk_event schema field names via MCP describe_table | 0 | Assumption #1 | ✅ | none | A | MINIMAL |
| 010 | Add TagFilter shared component | 1 | FR-SC-01 | ✅ | 002 | B | FULL |
| 011 | Add DocumentRowMenu shared component | 1 | FR-SC-02 | ✅ | 002 | B | FULL |
| 012 | Update @spaarke/ui-components barrel (TagFilter + DocumentRowMenu exports) | 1 | FR-SC-01,02 | ✅ | 010, 011 | — (serial) | STANDARD |
| 020 | DonutChart fieldPivot + matrixRight layout + colorThresholds + meanOfFields | 2 | FR-VH-01 | ✅ | 002 | C | FULL |
| 021 | MetricCard badge slot | 2 | FR-VH-02 | 🔲 | 002 | — (serial w/022) | FULL |
| 022 | MetricCard descriptionColor prop | 2 | FR-VH-03 | 🔲 | 021 | — (serial after 021) | FULL |
| 023 | HorizontalStackedBar headlineAboveBar layout | 2 | FR-VH-04 | ✅ | 002 | C | FULL |
| 024 | CardChrome internal wrapper component | 2 | FR-VH-05 | ✅ | 002 | C | FULL |
| 025 | Visual Host backward-compat regression smoke (NFR-05) | 2 | NFR-05 | 🔲 | 001, 020, 021, 022, 023, 024 | — (serial gate) | STANDARD |
| 030 | Matter Health Composite chart def | 3 | FR-DV-01 | 🔲 | 020, 025 | D | STANDARD |
| 031 | Matter Budget chart def | 3 | FR-DV-02 | 🔲 | 023, 025 | D | STANDARD |
| 032 | Matter Tasks chart def | 3 | FR-DV-03 | 🔲 | 003, 021, 025 | D | STANDARD |
| 033 | Matter Next Date chart def | 3 | FR-DV-04 | 🔲 | 003 | D | STANDARD |
| 034 | Matter Activity chart def | 3 | FR-DV-05 | 🔲 | 003, 022, 025 | D | STANDARD |
| 040 | Documents three-dot row menu | 4 | FR-DOC-01 | 🔲 | 012 | — (serial w/041..046) | FULL |
| 041 | Documents list/card toggle + sortable columns + multi-select + pin | 4 | FR-DOC-04 | 🔲 | 040, 050 | — (serial) | FULL |
| 042 | Documents Tags filter (consumes TagFilter) | 4 | FR-DOC-05 | 🔲 | 012, 041 | — (serial) | FULL |
| 043 | Documents filter relocation (sidebar → command bar) | 4 | FR-DOC-06 | 🔲 | 042 | — (serial) | FULL |
| 044 | FilePreviewDialog restructure (960 px, 2-col body) | 4 | FR-DOC-03 | 🔲 | 040 | — (serial) | FULL |
| 045 | Bulk-action bar (6 actions) | 4 | FR-DOC-02 | 🔲 | 041, 051 | — (serial) | FULL |
| 046 | Documents telemetry (App Insights events) | 4 | FR-DOC-07 | 🔲 | 002, 040, 041, 042, 043, 044, 045 | — (serial) | FULL |
| 047 | VisualHost docs update (FR-DOC-09 — architecture + setup guide) | 4 | FR-DOC-09 | 🔲 | 020, 021, 022, 023, 030, 031, 032, 033, 034 | F | STANDARD |
| 050 | BFF /api/ai/search projection adds modifiedAt + modifiedBy | 5 | FR-BFF-01 | 🔲 | none | E | FULL |
| 051 | BFF POST /api/documents/bulk-download endpoint | 5 | FR-BFF-02 | 🔲 | none | E | FULL |
| 060 | Matter main-form Overview tab 2-column 66/34 layout | 6 | FR-FORM-01 | 🔲 | 024, 030, 031, 032, 033, 034, 040 | — (serial gate) | FULL |
| 070 | Deploy SemanticSearchControl PCF | 7 | — | 🔲 | 046 | G | FULL |
| 071 | Deploy VisualHost PCF | 7 | — | 🔲 | 025, 047 | G | FULL |
| 072 | Deploy BFF | 7 | — | 🔲 | 050, 051 | G | FULL |
| 073 | Dataverse solution import (chart defs + form XML) | 7 | — | 🔲 | 030..034, 060, 070, 071 | — (serial after G) | FULL |
| 074 | Cross-cutting NFR validation (NFR-01, 02, 04, 05; FR-TEL-01) | 7 | NFR-01..08 | 🔲 | 070, 071, 072, 073 | — (final gate) | FULL |
| 090 | Project wrap-up | 8 | — | 🔲 | 074 | — (serial) | FULL |

---

## Parallel Execution Plan

Tasks in the same group can run simultaneously once their prerequisite group(s) complete. **Max concurrency: 6 agents per wave.** Build verification (`dotnet build` / `npm run build`) runs between waves.

### Wave 0 — Phase 0 Foundation (3 agents, parallel)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize | Notes |
|---|---|---|---|---|---|
| Wave 0 | 001, 002, 003 | none | Disjoint: 001 = MCP reads, 002 = new service + manifest props in PCFs, 003 = MCP reads | ✅ Yes | Pure foundation. 002 touches PCF manifests but does not modify renderer logic. |

### Wave 1 — Phase 1 Shared components (Group B, 2 agents parallel; then 1 serial)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize | Notes |
|---|---|---|---|---|---|
| Wave 1A | 010, 011 | 002 ✅ | 010 = TagFilter.tsx (new), 011 = DocumentRowMenu.tsx (new) | ✅ Yes | Both new files in @spaarke/ui-components/src/components/ |
| Wave 1B | 012 | 010 ✅ + 011 ✅ | src/client/shared/Spaarke.UI.Components/src/index.ts (barrel) | ❌ No | Barrel — serializes after both Wave 1A tasks |

### Wave 2 — Phase 2 Visual Host renderer extensions

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize | Notes |
|---|---|---|---|---|---|
| Wave 2A | 020, 023, 024 | 002 ✅ | Disjoint: 020 = DonutChart.tsx + ChartRenderer.tsx, 023 = HorizontalStackedBar.tsx, 024 = CardChrome.tsx (new) + VisualHostRoot.tsx | ✅ Yes | Group C — different renderer files |
| Wave 2B | 021 | Wave 2A complete (and 002) | MetricCard.tsx | ❌ No | First of two serial MetricCard tasks |
| Wave 2C | 022 | 021 ✅ | MetricCard.tsx | ❌ No | Second MetricCard task — serializes after 021 |
| Wave 2D | 025 | 001 ✅ + 020, 021, 022, 023, 024 ✅ | NO code; regression evidence into notes/spikes/visualhost-regression-evidence/ | ❌ No | NFR-05 gate — must run after all renderer extensions land |

### Wave 3 — Phase 3 Chart definitions (Group D, 5 agents parallel)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize | Notes |
|---|---|---|---|---|---|
| Wave 3 | 030, 031, 032, 033, 034 | 025 ✅ (NFR-05 baseline confirmed safe), respective renderer tasks, and 003 ✅ (for 032/033/034) | Disjoint: each is a new sprk_chartdefinition record in Dataverse | ✅ Yes | 5 agents — Dataverse records are independent |

### Wave 4 — Phase 5 BFF (Group E, 2 agents parallel)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize | Notes |
|---|---|---|---|---|---|
| Wave 4 | 050, 051 | none (could run alongside Wave 0–3) | 050 = SearchResult.cs + SemanticSearchEndpoints.cs; 051 = DocumentsBulkEndpoints.cs (new) | ✅ Yes | Could run in parallel with Phase 1 if context allows — see "Schedule" section below |

### Wave 5 — Phase 4 Documents PCF (mostly serial)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize | Notes |
|---|---|---|---|---|---|
| Wave 5A | 047 | 020, 021, 022, 023, 030..034 ✅ | docs/architecture/VISUALHOST-ARCHITECTURE.md, docs/guides/VISUALHOST-SETUP-GUIDE.md | ✅ Yes (Group F) | Docs work — fully independent of Documents PCF code |
| Wave 5B | 040 | 012 ✅ | ResultCard.tsx, FilePreviewDialog.tsx | ❌ No | Most Documents PCF tasks touch SemanticSearchControl.tsx — must serialize |
| Wave 5C | 041 | 040 ✅ + 050 ✅ | SemanticSearchControl.tsx + new ListView component | ❌ No | Default sort needs modifiedAt projection from 050 |
| Wave 5D | 042 | 012 ✅ + 041 ✅ | SemanticSearchControl.tsx + TagFilter consumption | ❌ No | |
| Wave 5E | 043 | 042 ✅ | FilterPanel removal + command bar | ❌ No | AssociatedOnly verbatim preservation binding |
| Wave 5F | 044 | 040 ✅ | FilePreviewDialog.tsx | ❌ No | Dialog restructure |
| Wave 5G | 045 | 041 ✅ + 051 ✅ | BulkActionBar (new) + SemanticSearchControl.tsx | ❌ No | Bulk-download depends on 051 |
| Wave 5H | 046 | 002 ✅ + 040..045 ✅ | SemanticSearchControl.tsx telemetry | ❌ No | Replaces console.log with AppInsights events |

### Wave 6 — Form configuration (serial)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize | Notes |
|---|---|---|---|---|---|
| Wave 6 | 060 | 024, 030..034, 040 ✅ | Matter main-form solution XML | ❌ No | All Visual Host wiring + Documents PCF baseline must exist |

### Wave 7 — Deploy + validation

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize | Notes |
|---|---|---|---|---|---|
| Wave 7A | 070, 071, 072 | 046 ✅, 071 prereq: 025 ✅ + 047 ✅, 072 prereq: 050 ✅ + 051 ✅ | 070 = SemanticSearchControl PCF deploy; 071 = VisualHost PCF deploy; 072 = BFF deploy | ✅ Yes (Group G — different deploy targets) | Each uses `npm run build:prod` (NOT `npm run build` — AP-1 binding) |
| Wave 7B | 073 | Wave 7A complete + 060 ✅ | Dataverse solution import | ❌ No | Imports the unified solution post-deploy |
| Wave 7C | 074 | 073 ✅ | Cross-cutting validation; evidence into notes/spikes/nfr-validation-evidence/ | ❌ No | Final NFR-01, 02, 04, 05; FR-TEL-01 gate |

### Wave 8 — Wrap-up

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize | Notes |
|---|---|---|---|---|---|
| Wave 8 | 090 | 074 ✅ | Project README, plan, notes/lessons-learned.md; repo-cleanup | ❌ No | Mandatory final task per task-create Step 3.7 |

---

## Schedule overview

Maximum parallelism per wave (assuming Wave 4 BFF runs alongside Wave 1 + Wave 2):

```
Wave 0 (3 agents): 001 + 002 + 003                                          [Phase 0]
Wave 1A (2 agents): 010 + 011                                               [Phase 1]
Wave 1B (1 agent):  012                                                     [Phase 1]
Wave 2A (3 agents): 020 + 023 + 024                                         [Phase 2]
Wave 2B (1 agent):  021                                                     [Phase 2]
Wave 2C (1 agent):  022                                                     [Phase 2]
Wave 2D (1 agent):  025  (NFR-05 regression gate)                           [Phase 2]
Wave 3 (5 agents):  030 + 031 + 032 + 033 + 034                             [Phase 3]
Wave 4 (2 agents):  050 + 051  (can run as early as Wave 0)                 [Phase 5]
Wave 5A (1 agent):  047 (docs — can run alongside Wave 5B-H)                [Phase 4]
Wave 5B (1 agent):  040                                                     [Phase 4]
Wave 5C (1 agent):  041  (depends on 050)                                   [Phase 4]
Wave 5D (1 agent):  042                                                     [Phase 4]
Wave 5E (1 agent):  043                                                     [Phase 4]
Wave 5F (1 agent):  044                                                     [Phase 4]
Wave 5G (1 agent):  045  (depends on 051)                                   [Phase 4]
Wave 5H (1 agent):  046                                                     [Phase 4]
Wave 6  (1 agent):  060                                                     [Phase 6]
Wave 7A (3 agents): 070 + 071 + 072                                         [Phase 7]
Wave 7B (1 agent):  073                                                     [Phase 7]
Wave 7C (1 agent):  074  (NFR validation final gate)                        [Phase 7]
Wave 8  (1 agent):  090  (project wrap-up — mandatory)                      [Phase 8]
```

**Critical path** (longest dependency chain): `001 → 020 → 025 → 030 → 060 → 073 → 074 → 090` (~8 task sequence)

**High-risk waves**:
- Wave 2D (025 NFR-05 regression smoke): blocks all of Wave 3. If any existing chart def regresses, halt and remediate.
- Wave 5C / 5G: depend on Wave 4 BFF tasks completing. If Wave 4 is delayed, Phase 4 cannot finish.
- Wave 7C (074): final cross-cutting validation. axe / dark mode / NFR-05 / FR-TEL-01 all must pass before 090.

---

## How to execute parallel groups

1. Check all prerequisites are complete (✅ in Status column above)
2. Invoke `Skill` with `skill="task-execute"` in ONE message with MULTIPLE invocations — one per task in the wave
3. Each invocation calls `task-execute` with a different task file (e.g., `tasks/020-donut-fieldpivot-matrixright.poml`)
4. Wait for ALL agents in the wave to complete (or report failure)
5. Run build verification between waves: `dotnet build src/server/api/Sprk.Bff.Api/` if any `.cs` modified; `npm run build` in relevant package if any `.ts`/`.tsx` modified
6. Mark all completed tasks ✅ in this table
7. Advance to next wave whose prerequisites are now satisfied

**Failure isolation**: one agent failing does NOT abort the wave. Collect all outcomes. Failed tasks are marked 🔄 (needs retry); main session decides retry vs. report.

**Permission boundary**: tasks touching `.claude/` paths must be sequential (main-session-only). No task in this project currently touches `.claude/` (the docs updates in 047 go to `docs/` only).

---

## Quick command reference

```
# Auto-start the next pending task
"continue"
# or
Skill task-execute tasks/{next-id}-*.poml

# Resume a specific task
"work on task 020"

# Recover from compaction
"where was I?"
# Then read current-task.md
```

---

*Generated by `/project-pipeline projects/spaarke-matter-ui-enhancement-r1`. Update this file as tasks complete — `task-execute` does this automatically per its protocol.*
