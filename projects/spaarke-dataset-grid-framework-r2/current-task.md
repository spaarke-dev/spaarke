# Current Task State — spaarke-dataset-grid-framework-r2

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-02 (by project-pipeline Step 2)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | none — project just initialized |
| **Step** | Pipeline Step 2 complete (artifacts generated); Step 3 (task decomposition) next |
| **Status** | not-started |
| **Next Action** | Run `/task-create projects/spaarke-dataset-grid-framework-r2` to decompose `plan.md` into POML task files |

### Files that exist in this project

- ✅ [`design.md`](design.md) — original 574-line human design (11 issues + evidence)
- ✅ [`spec.md`](spec.md) — AI-optimized spec (10 FRs, 7 NFRs, ADR tensions, owner clarifications)
- ✅ [`README.md`](README.md) — project overview + graduation criteria
- ✅ [`plan.md`](plan.md) — 4-phase WBS + risk register
- ✅ [`CLAUDE.md`](CLAUDE.md) — project AI context (load at every task start)
- ✅ [`current-task.md`](current-task.md) — this file
- ⚠️ `tasks/` — folder created with `.gitkeep`, awaiting `/task-create`
- ⚠️ `notes/` — folder created with `.gitkeep` and subdirs (`debug/`, `spikes/`, `drafts/`, `handoffs/`)

### Critical Context

Project scaffolded 2026-07-02 via `/design-to-spec` → `/project-pipeline` for the DataGrid framework R2. The R1 framework shipped 2026-06. Production use in `ai-spaarke-ai-workspace-UI-r2` surfaced 11 gaps; a tactical CSS `maxHeight` hack was deployed against the 6 entity-list section registrations during that project's follow-up (PRs #530 + #531 + #533). R2 unwinds that hack via proper framework `contentSizing` metadata, adds per-instance overrides, and — per owner clarification 2026-07-02 — extracts the LegalWorkspace section registry into a new shared package so SpaarkeAi no longer aliases into a sibling workspace's source tree.

**Ships as 3 phased PRs across 4 project phases. Estimated ~3.5 days.**

---

## Active Task (Full Details)

| Field | Value |
|---|---|
| **Task ID** | none |
| **Task File** | — (tasks not yet generated) |
| **Title** | — |
| **Phase** | Pre-Phase 1 (pipeline scaffolding) |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps

- [x] `/design-to-spec` — produced `spec.md` from `design.md` (2026-07-02)
- [x] `/project-pipeline` Step 0.3 — pre-flight checks (2026-07-02)
- [x] `/project-pipeline` Step 0.5 — master staleness check (informational; 0 behind) (2026-07-02)
- [x] `/project-pipeline` Step 1 — spec.md validated (3,677 words, 7 sections + ADR tensions) (2026-07-02)
- [x] `/project-pipeline` Step 1.5 — PR overlap detection (no blocking overlaps) (2026-07-02)
- [x] `/project-pipeline` Step 1.7 — ADR tensions validated (both Path C compliant) (2026-07-02)
- [x] `/project-pipeline` Step 2 — resource discovery + artifact generation (2026-07-02)
- [ ] `/project-pipeline` Step 3 — task decomposition via `/task-create`
- [ ] `/project-pipeline` Step 4 — commit + push project artifacts
- [ ] Phase 1 kickoff — first task execution

### Current Step

**Step**: pipeline handoff to `/task-create`

**What this step involves**:
- Invoke `/task-create projects/spaarke-dataset-grid-framework-r2`
- Decompose plan.md Phases 1-4 into POML task files (estimated 17-25 tasks + 3 deployment + 1 wrap-up)
- Generate `tasks/TASK-INDEX.md` with dependencies + parallel groups

### Files Modified (All Task)

- `projects/spaarke-dataset-grid-framework-r2/spec.md` — created 2026-07-02 by `/design-to-spec`; minor path correction 2026-07-02 by `/project-pipeline` Step 2 (sectionRegistry.ts location)
- `projects/spaarke-dataset-grid-framework-r2/README.md` — rewritten 2026-07-02 by `/project-pipeline` Step 2
- `projects/spaarke-dataset-grid-framework-r2/plan.md` — created 2026-07-02 by `/project-pipeline` Step 2
- `projects/spaarke-dataset-grid-framework-r2/CLAUDE.md` — created 2026-07-02 by `/project-pipeline` Step 2
- `projects/spaarke-dataset-grid-framework-r2/current-task.md` — refreshed 2026-07-02 by `/project-pipeline` Step 2

### Decisions Made

- **2026-07-02** — `pageSize` default: **100 → 25** (owner clarification during `/design-to-spec`)
- **2026-07-02** — Adopted Issue 12 Option B (shared-package extraction) in R2 scope (owner clarification)
- **2026-07-02** — All 6 entity-list widgets default to `widthPreference: 'full'` (owner clarification)
- **2026-07-02** — Ships in 3 PRs (framework, wizard, extraction) — design decision preserved

---

## Next Action

**Next Step**: Run `/task-create projects/spaarke-dataset-grid-framework-r2`

**Pre-conditions**:
- ✅ `plan.md` exists with 4 phases + phase deliverables
- ✅ `spec.md` exists with 10 FRs + acceptance criteria
- ✅ `CLAUDE.md` exists with applicable ADRs + tag hints

**Key Context**:
- ADRs to include in every task: ADR-012, ADR-021, ADR-022, ADR-028, ADR-038
- Knowledge files to include on FR-01/FR-08 tasks: `.claude/patterns/ui/embedded-widget-sizing.md`, `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`
- Knowledge files to include on FR-02/FR-03/FR-04 tasks: `.claude/patterns/ui/fluent-v9-component-authoring.md`, `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`
- Knowledge files to include on FR-10 tasks: `src/client/shared/Spaarke.DailyBriefing.Components/` (structural template)
- FR-09 must be marked `parallel-safe: false` (touches `.claude/` — main-session-only per CLAUDE.md §3)
- FR-08 6-file unwind is a great candidate for parallel subagent execution (identical edit pattern)

**Expected Output**:
- `tasks/TASK-INDEX.md` with task list + dependencies + parallel groups
- ~17-25 POML task files + 3 deployment tasks + 1 wrap-up task (`090-project-wrap-up.poml`)

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-07-02
- Focus: Pipeline scaffolding — `/design-to-spec` → `/project-pipeline` end-to-end initialization

### Key Learnings

- **Owner-clarified scope expansion**: Issue 12 Option B moved from "future project" to R2 scope. Adds FR-10 (~1 day), PR 3.
- **`sectionRegistry.ts` file location correction**: lives at `src/solutions/LegalWorkspace/src/sectionRegistry.ts`, NOT under `WorkspaceShell/`. Corrected in spec.md.
- **Config templates directory does not exist yet**: `scripts/config-templates/` is created by FR-06.

### Handoff Notes

*No handoff notes yet — this is fresh scaffolding, not a mid-task handoff.*

---

## Quick Reference

### Project Context
- **Project**: `spaarke-dataset-grid-framework-r2`
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) *(pending — will be generated by `/task-create`)*
- **Worktree**: `C:/code_files/spaarke-wt-spaarke-dataset-grid-framework-r2`
- **Branch**: `work/spaarke-dataset-grid-framework-r2`

### Applicable ADRs

- ADR-012 (shared component library) — Framework changes stay in `@spaarke/ui-components`
- ADR-021 (Fluent Design System) — No Fluent v8; native scrollbar retained
- ADR-022 (PCF Platform Libraries) — Shared-lib code React-16-safe
- ADR-028 (Spaarke Auth v2) — No auth surface touched
- ADR-038 (Testing Strategy) — MAINTAIN-class tests only, `/test-diet` gate at wrap-up

### Knowledge Files Loaded

*(none loaded yet — task-execute will load per task's `<knowledge>` section)*

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read Active Task and Progress sections
3. **Load task file**: `tasks/{task-id}-*.poml` (once tasks are generated)
4. **Load knowledge files**: from task's `<knowledge>` section
5. **Resume**: from the "Next Action" section

**Commands**:
- `/project-continue` — full project context reload + master sync
- `/context-handoff` — save current state before compaction
- "where was I?" — quick context recovery

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
