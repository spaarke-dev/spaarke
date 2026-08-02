# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-01 (🎉 PHASE 0 COMPLETE — tasks 001–013)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none active — **Phase 0 (Build) complete**; conversions (P0.5/P1–P7) not started |
| **Step** | — |
| **Status** | phase-boundary |
| **Next Action** | Start conversions. Recommended next: **P1 window-controls rollout (030, 031)** or **P0.5 app-shell scale (020, SpaarkeAi hot-path — `/conflict-check` first)**. Or say "continue" / "work on 030, 031". |

### Critical Context
**PHASE 0 (Build) is COMPLETE (001–013, 2026-08-01).** The canonical modal system ships in `@spaarke/ui-components`: `SprkModal/` = `sizes.ts`, `scaledTheme.ts`, `SprkModal.tsx` (+`.types.ts`), `ModalScrollArea.tsx`, `presets/{Confirm,Choice,Form,Preview,Browse,Wizard}Modal.tsx`, `index.ts` (barrel), + tests. Consolidated `tsc` build **green**, **86/86** tests (11 suites), eslint clean, zero hex/`'1px'`/inline-color. **React scoping**: the modal family is **Code-Page-only by construction** — exported from the main barrel (`src/index.ts`) and ABSENT from the curated `src/pcf-safe.ts` allow-list, so NFR-04's PCF arm is satisfied by exclusion (no PCF compile needed); the family is also structurally React-16/17-safe, so a specific preset can be added to `pcf-safe.ts` later at low cost if a PCF ever needs one. One open discoverability check: grep that PCF controls import from `pcf-safe` (not the main barrel). Importable: `import { SprkModal, ConfirmModal, ChoiceModal, FormModal, PreviewModal, BrowseModal, WizardModal } from '@spaarke/ui-components'`. Docs: `docs/standards/MODAL-DESIGN-SYSTEM.md` + `.claude/adr/ADR-050` + `.claude/patterns/ui/modal-shell.md` + crosslinks + root CLAUDE.md §17 row + CHANGELOG. **6 commits** (dc27660→…→docs). Notes: [`wave-a-completion.md`](./notes/wave-a-completion.md), [`wave-b-completion.md`](./notes/wave-b-completion.md) (⚠️ 007 BrowseModal single-header decision for owner review).

**Conversions NOT started** (each modifies EXISTING dialogs across `src/` + solutions — read target files carefully; higher blast radius). Order: P0.5 (020) · P1 (030/031) · P2 (040/041/042) · P3 (050/051) · P4 (060/061) · P5 (070) · P6 (080) · P7 (090→091/092) · P8 wrap-up (100). Preset→conversion map + escalation triggers are in each POML. **Env**: fresh worktree — `npm install` done in `Spaarke.UI.Components` + siblings `Spaarke.SdapClient`/`Spaarke.Auth` built.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none (phase boundary — P0 done) |
| **Task File** | — (next: tasks/030-* / 031-* for P1, or 020-* for P0.5) |
| **Title** | — |
| **Phase** | 0 Build ✅ → conversions pending |
| **Status** | none |
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
