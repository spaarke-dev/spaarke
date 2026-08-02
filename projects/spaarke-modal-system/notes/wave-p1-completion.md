# Wave P1 Completion — Tasks 030 + 031 (2026-08-01)

**Result: ✅ both tasks complete.** 2 parallel Sonnet agents (task-execute STANDARD rigor each); main-session wave verification.

## Outcomes
- **030 (UI.Components dialogs)**: 9 files wired (`ChoiceDialog`, legacy `FindSimilarDialog`, `NewThreadModal`, both `CloseProjectDialog` copies, `RichFilePreviewDialog`+`RichFilePreview` title-bar owner, deprecated `FilePreviewDialog`, `WizardShell` — maximize gated off in embedded mode). Both `FindSimilar` wizard copies inherit via `WizardShell` (own no header — zero-edit). `EmailComposer/wrappers/SendEmailDialog` verified already-consuming. **1 escalation**: legacy `SendEmailDialog` skipped (in-file "v1.1.59 no-X" UAT decision — see defer-issues.md Decision-pending; resolves at P3/051 retirement).
- **031 (other libs)**: 4/4 wired (`ComposeConflictDialog`, `PinnedMemoryEditDialog`, `PinnedMemoryDeleteConfirmation`, `QuickStartModal`) — all via Fluent `DialogTitle` `action` slot; alert/non-dismissible semantics preserved (× → existing cancel-equivalent only). `ConversationModal` (PCF) verified already-consuming via `@spaarke/ui-components` barrel + React-16 boundary cast — untouched (P5 replaces it).

## Adapter shape (consistent across both tasks)
`<DialogTitle action={<ModalWindowControls isMaximized onToggleMaximize onClose/>}>` where DialogTitle exists (excluded from aria-labelledby — no a11y-name regressions); local `isMaximized` + reset-on-close → `{width:'100%', height:'100%', maxWidth:'100%'}` (the shipped SendEmailDialog.surfaceMaximized precedent).

## Verification (main session)
- ADR-021 negative gate: **CLEAN** — 400 added lines, zero new hex / `'1px'` / inline-color.
- Builds: UI.Components ✅ · PCF `SemanticSearchControl` `build:prod` (React 16/17) ✅ · Code Page `SpaarkeAi` (React 19, 4019 modules + ribbon) ✅ · Compose.Components ✅ · AI.Widgets ✅ → NFR-04 dual-React leg proven. LegalWorkspace ❌ **pre-existing master defect** (missing `@spaarke/ai-outputs` dep; our 2 files transform clean) → **Issue #712** + DEF-001.
- Tests: P0 gate 86/86 ✅; UI.Components 2444/2466 (11 failing suites all pre-existing/unrelated, one assertion updated for the now-two "Close" buttons); AI.Widgets 677/678 (1 pre-existing); Compose 843/858 (15 pre-existing, git-stash A/B-proven unrelated); SpaarkeAi 838/838 ✅.
- SpaarkeAi hot-path conflict-check: soft-pass (zero open-PR overlap on target files; many worktrees share the surface — cite at PR time).
- `SemanticSearchControl/package-lock.json` install churn restored (not committed).

## Tension noted for owner
Confirm-class dialogs (`PinnedMemoryDeleteConfirmation`, `CloseProjectDialog`, `ChoiceDialog`) now carry maximize per the literal FR-12 mandate, but the P0 `ConfirmModal` preset (their P2 re-base target) is deliberately non-maximizable. P2 will supersede the interim wiring with the preset's contract — flagging so the interim maximize-on-confirms isn't mistaken for the end-state.

Per-task detail: `task-030-completion.md`, `task-031-completion.md`.
