# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-01 (Group A + 004 + Group B complete)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 009 — barrel exports + a11y snapshot + dual-React verify (next; not started) |
| **Step** | — |
| **Status** | not-started |
| **Next Action** | Execute task 009 (`tasks/009-barrel-exports-and-tests.poml`) — wires `SprkModal/index.ts` + `components/index.ts`, a11y snapshot, dual-React verify. Or say "continue". |

### Critical Context
P0 build is nearly done: Group A (001/002/003) ✅ + **004 shell** ✅ + Group B presets (005/006/007/008) ✅ complete 2026-08-01. `SprkModal/` now has `sizes.ts`, `scaledTheme.ts`, `SprkModal.tsx` (+`.types.ts`), `ModalScrollArea.tsx`, and `presets/{ConfirmModal,ChoiceModal,FormModal,PreviewModal,BrowseModal,WizardModal}.tsx` (+ tests). Consolidated build **green**, **81/81** tests pass, eslint clean. **Task 009 wires the barrels** (nothing is exported from `SprkModal/index.ts` or `components/index.ts` yet — presets import `../SprkModal` directly). Then docs (010→012; 011; 013 — **011/012/013 main-session-only**, they write `.claude/`) + conversion phases P1–P7 open. **020** (P0.5, needs 002) and **090** (P7 OOB) are independent and runnable anytime. **007 design note**: BrowseModal uses SprkModal's single-header `nav` + an `onBeforeNavigate` guard seam (does NOT nest RecordNavigationModalShell) — see [`notes/wave-b-completion.md`](./notes/wave-b-completion.md) ⚠️ for the owner-review flag. **Env**: fresh worktree — `npm install` done in `Spaarke.UI.Components` + siblings built.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 009 |
| **Task File** | tasks/009-barrel-exports-and-tests.poml |
| **Title** | Barrel exports + a11y snapshot + dual-React verify |
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
