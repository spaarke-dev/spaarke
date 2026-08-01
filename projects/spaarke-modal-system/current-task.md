# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-01 (Group A + task 004 complete)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Group B — presets 005/006/007/008 (next; not started) |
| **Step** | — |
| **Status** | not-started |
| **Next Action** | Execute Group B (005 Confirm/Choice · 006 Form · 007 Preview/Browse · 008 Wizard) — parallel-safe presets under `SprkModal/presets/`. Or say "continue". |

### Critical Context
Group A (001/002/003) ✅ + **004 SprkModal base shell** ✅ complete 2026-08-01. `SprkModal/` now has `sizes.ts`, `scaledTheme.ts`, `SprkModal.tsx` (+ `.types.ts`), `ModalScrollArea.tsx`. Shared-lib build **green**, **30/30** tests pass, eslint clean, **transform-robust portal verified**. Group B presets are thin configs of `SprkModal` — each imports `{ SprkModal, type SprkModalProps }` from `../SprkModal` and lives in `SprkModal/presets/`. **ChoiceModal is built FRESH** (not in prototype; re-base of ChoiceDialog, ADR-023); **BrowseModal = PreviewModal + nav** prop. Prototype presets live in ONE `presets.tsx` at `c:/code_files/spaarke-prototype/projects/2026-07-sprk-modal-system/src/components/presets.tsx`. Barrel wiring is task 009 (NOT the presets) — presets import from `../SprkModal` directly to avoid a barrel race. **Env note**: fresh worktree — `npm install` done in `Spaarke.UI.Components` + siblings `Spaarke.SdapClient`/`Spaarke.Auth` built.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 005/006/007/008 (Group B wave) |
| **Task File** | tasks/00[5-8]-*.poml |
| **Title** | Presets: Confirm/Choice · Form · Preview/Browse · Wizard |
| **Phase** | 0 Build |
| **Status** | not-started |
| **Started** | — |

---

## Progress

### Completed Steps
*Task 004 not started. (Group A — 001/002/003 — complete; see TASK-INDEX.md + notes/wave-a-completion.md.)*

### Files Modified (All Task)
*None for task 004 yet.*

### Decisions Made
- 2026-08-01: Executed Group A consolidated in the main session (≤5 small files, shared build target) rather than 3 sub-agents.
- 2026-08-01: Task 001 width uses spec FR-02's pre-multiplied `min(cap·uiScale px, N·vw)` form (numeric `uiScale` arg the host threads to both `getSurfaceStyle` and `scaleTheme`), NOT `calc(var(--sprk-ui-scale))`. No escalation — md/lg caps match spec §6.2.

---

## Next Action

**Next Step**: Execute task 004 — SprkModal base shell.

**Pre-conditions**: 001, 002, 003 ✅ (all satisfied).

**Key Context**:
- Compose the Fluent `Dialog` envelope (transform-robust portal) + `RecordNavigationModalShell` chrome + `ModalWindowControls`; do NOT hand-roll overlays.
- Consume `getSurfaceStyle(size, uiScale)` (sizes.ts) for the surface and `scaleTheme(base, scale)` (scaledTheme.ts) for `--sprk-ui-scale`.
- Cancel-always-left footer; native thin scrollbar; semantic tokens only.
- Prototype reference: `c:/code_files/spaarke-prototype/projects/2026-07-sprk-modal-system/src/components/SprkModal.tsx` (+ `ModalScrollArea.tsx`).

**Expected Output**: `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/SprkModal.tsx` (+ tests). Unblocks Group B presets (005–008).

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-08-01
- Focus: Group A (001/002/003) executed + verified. Ready for task 004.

### Key Learnings
- Prototype has all presets in ONE `presets.tsx`; `ChoiceModal` must be built fresh; `BrowseModal` = `PreviewModal` + `nav`.
- Shipped `ModalWindowControls` now uses `FullScreenMaximize/Minimize` (was `ArrowMaximize`) — reconciled in task 003.
- Jest + ts-jest is the shared-lib test runner (`**/__tests__/**/*.test.ts(x)`); tsconfig excludes tests from the `tsc` build.
- `SprkModal/wizard` size (62vw × min(74vh,760px)) ≠ the OOB `wizard` 60%×70% (FR-11) — different mechanisms; keep distinct when task 010 documents them.

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
