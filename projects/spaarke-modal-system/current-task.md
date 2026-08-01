# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-01 (project initialization)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none — project initialized, no task started |
| **Step** | — |
| **Status** | not-started |
| **Next Action** | Execute task 001 (`tasks/001-size-scale-tokens.poml`) — P0 foundation. Or say "work on task 001". |

### Critical Context
Project initialized 2026-08-01 via `/project-pipeline`. 29 tasks across 8 phases (P0–P7 + P0.5). The prototype at `c:/code_files/spaarke-prototype/projects/2026-07-sprk-modal-system/` is the visual contract to port. Start with P0 foundation: tasks 001/002/003 are parallel-safe (size tokens, scaled theme, window-controls glyph), then 004 (SprkModal base).

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | — |
| **Phase** | — |
| **Status** | not-started |
| **Started** | — |

---

## Progress

### Completed Steps
*No steps completed yet — project just initialized.*

### Files Modified (All Task)
*No implementation files modified yet.*

### Decisions Made
- 2026-08-01: Project initialized. Scaled-theme mechanism (not CSS zoom), 7-size scale, Cancel-left footer, FullScreen glyph — all locked by prototype 2026-07-31 + owner 2026-08-01. See spec.md "Owner Clarifications" + "Decisions Confirmed".

---

## Next Action

**Next Step**: Execute task 001 — Size scale + layout tokens (`sizes.ts`).

**Pre-conditions**: none (P0 foundation; 001/002/003 have no dependencies).

**Key Context**:
- Port `sizes.ts` from the prototype (`SIZE_SPEC`, `getSurfaceStyle`, `SprkModalSize`/`SprkModalLayout` types).
- ADR-021 applies: semantic tokens only; sizes via `min(calc(px * var(--sprk-ui-scale,1)), vw)`.

**Expected Output**: `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/sizes.ts` + tests.

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-08-01
- Focus: Project initialization complete; ready to execute Phase 0.

### Key Learnings
- Prototype has all presets in ONE `presets.tsx`; `ChoiceModal` must be built fresh; `BrowseModal` = `PreviewModal` + `nav`.
- Shipped `ModalWindowControls` uses `ArrowMaximize` — reconcile in task 003.

---

## Quick Reference

### Project Context
- **Project**: spaarke-modal-system
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-012: shared components in `@spaarke/ui-components`
- ADR-021: Fluent v9 semantic tokens only (strengthened)
- ADR-023: ChoiceDialog preserved via ChoiceModal
- ADR-028: pass `authenticatedFetch` as a function

---

*This file is the primary source of truth for active work state. Keep it updated.*
