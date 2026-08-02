# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-01 (P1 wave complete — 030 + 031 ✅)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none active — **P0 ✅ + P0.5 ✅ + P1 ✅**; next wave = P2 (040/041/042 parallel) |
| **Step** | — |
| **Status** | phase-boundary |
| **Next Action** | P2 wave (040/041/042, parallel, needs 005 ✅) → P3 (050/051) → P4 (060/061) → P5 070 → P6 080 → P7 090→091/092 → P8 100 |

### Critical Context
**P0 (Build, 001–013) ✅** — `SprkModal` + 6 presets ship in `@spaarke/ui-components` (main barrel only; ABSENT from `pcf-safe.ts` → Code-Page-scoped by construction). 86 P0 tests green. Docs: `MODAL-DESIGN-SYSTEM.md` + ADR-050 + pattern pointer + §17 row.

**P1 (Window-controls, 030–031) ✅ 2026-08-01** — `ModalWindowControls` cluster (FullScreen glyph + ×) rolled out via interim `DialogTitle action` slot adapter: 13 dialogs wired/inherited/verified across UI.Components, LegalWorkspace copies, Compose.Components, AI.Widgets, SpaarkeAi. **1 escalation**: legacy `SendEmailDialog` skipped (in-file "v1.1.59 no-X" UAT decision; resolves at P3/051 retirement). See `notes/wave-p1-completion.md` + per-task notes. **Known env issue**: LegalWorkspace `npm run build` broken on master in fresh worktrees (missing `@spaarke/ai-outputs` dep) — **Issue #712 / DEF-001**; use scoped `tsc --noEmit` for LegalWorkspace verification in later waves.

**P0.5 (020) ✅ 2026-08-01** — `uiScale = max(setting, ≥2560→1.15)` bounded {1.0,1.15,1.25,1.5}; **`useUiScale()` from `@spaarke/ui-components` is the seam conversion tasks thread into `SprkModal uiScale`**; `DisplaySizeMenu` in SpaarkeAi strip + LW PageHeader; themeStorage extended in place. See `notes/task-020-completion.md`.

**Conversions remaining**: P2 040/041/042 · P3 050/051 · P4 060/061 · P5 070 (sonnet/**xhigh**) · P6 080 · P7 090→091/092 · P8 100 (main-session wrap-up). Each POML carries preset→conversion map + escalation triggers. Conversion waves modify EXISTING dialogs — read target files before rewriting.

**Env**: fresh worktree; node_modules + dists built for UI.Components, SdapClient, Auth, Compose.Components, AI.Widgets, AI.Outputs, AI.Context, DocumentOperations, SpaarkeAi, LegalWorkspace (build broken per #712), SemanticSearchControl.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none (wave boundary — P1 done) |
| **Task File** | next: tasks/020-app-shell-ui-scale-control.poml |
| **Phase** | 0 ✅ · 0.5 pending · 1 ✅ · 2–8 pending |
| **Status** | none |

---

## Progress

### Decisions Made (this session)
- 2026-08-01: P1 executed as 2 parallel Sonnet sub-agents (task-execute Step 0.3); main session did conflict-check (soft-pass), wave build verification, ADR-021 diff gate, tracking + commit.
- 2026-08-01: Interim adapter = `DialogTitle action` slot + local `isMaximized` → 100%/100% (SendEmailDialog precedent). P2+ re-bases supersede.
- 2026-08-01: Legacy `SendEmailDialog` escalation recorded, not overridden (owner decision; moot at P3/051).
- 2026-08-01: LegalWorkspace build defect = master issue, filed #712, NOT fixed in this project (out of scope).

---

## Next Action

**Next Step**: Task 020 — P0.5 app-shell `--sprk-ui-scale` control (FULL rigor, solo). Needs 002 ✅ (satisfied). Touches SpaarkeAi app shell → run conflict-check first, cite in notes.

Then P2 wave (040/041/042 parallel — confirms/choices re-base onto ConfirmModal/ChoiceModal + retire ActionConfirmationDialog overlay).

---

## Blockers

**Status**: None (2 open flags for owner, non-blocking: SendEmailDialog no-X escalation; interim maximize-on-confirms tension — both in `notes/wave-p1-completion.md`)

---

## Session Notes

### Key Learnings
- `FindSimilar` wizard copies own no header — inherit window controls via `WizardShell` (zero-edit).
- `RichFilePreview.tsx` (not the Dialog file) owns the title bar in the dominant non-nav path.
- Fluent `DialogTitle` `action` slot renders top-right and is excluded from `aria-labelledby` — safe for existing `getByRole(name:)` assertions.
- Existing suites: 11 UI.Components + 15 Compose + 1 AI.Widgets failures are all PRE-EXISTING (A/B-proven) — don't chase them in later waves.

---

## Quick Reference

### Project Context
- **Project**: spaarke-modal-system
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-012 (shared components) · ADR-021 (tokens only — strengthened) · ADR-023 (ChoiceDialog preserved) · ADR-028 (authenticatedFetch as function) · ADR-050 (canonical modal shell)

---

*This file is the primary source of truth for active work state. Keep it updated.*
