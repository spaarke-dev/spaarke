# Task 056 — WizardShell re-base onto SprkModal: deviations and decisions

> Date: 2026-10-07 · Branch `feat/wizardshell-sprkmodal-056` (worktree `C:\wts-b56`) · Base `origin/master` @ `36ff14147` (task 110 / #1380 merged)

Recorded per POML step 8. Each item says what the POML asked for, what was done, and why.

## D1 — `'skipped'` applies to every consumer's Skip button (a fourth UAT-visible change)

- **POML**: add a `'skipped'` `WizardStepStatus` that does not tick the step. It also says "nothing else changes for existing consumers" beyond three listed UAT changes.
- **Done**: the shell's Skip button now dispatches a new `SKIP_STEP` reducer action. It marks the step it leaves `'skipped'`, and the stepper draws a dashed ring with no tick. So existing wizards with skippable steps (the `CreateRecordWizard` follow-on steps) now show a skipped follow-on without a tick. Before, Skip showed a tick (`completed`).
- **Why**: the two POML constraints cannot both hold without an opt-in flag, and an opt-in flag would break "additive props **exactly**". HANDOFF §4.1 states the rule generally: "A skipped step must not show a tick." A tick on a skipped step is wrong information in any wizard.
- **For UAT**: add to the three intended changes. *A skipped step shows an empty dashed ring instead of a tick.*
- **Reversal cost**: about one line. Make Skip dispatch `NEXT_STEP` unless an opt-in prop is set.

## D2 — `SprkModal` gained a transitional `legacySize` prop (`@deprecated`)

- **POML**: `maxWidth`/`height` strings are deprecated and must keep working; task 111 maps them to named sizes.
- **Done**: `SprkModal` has no sizing escape hatch, so the re-based shell could not honour those strings without one. I added `legacySize?: { width?, height? }`, marked `@deprecated`. WizardShell is its only caller. It replaces the named size's width (still clamped by the 96vw outer `maxWidth`), sets `height` and `minHeight`, and is ignored while maximized. Task 111 should delete it once the callers use named sizes (`CreateAnalysisWizardWidget` 60vw/70vh, `DocumentEmailWizard` 1280px/85vh through SemanticSearchControl and DocumentRelationshipViewer).
- **ADR-050 note**: this is not a new per-surface size. It carries existing behaviour through a migration that task 111 completes.

## D3 — No `onBeforeClose` prop

- **HANDOFF §4.1 (v4)** lists one `onBeforeClose` / `onBeforeNavigate` seam.
- **Done**: `onBeforeNavigate` only, on `nav`, lifted into `SprkModalNav` and therefore available to both `BrowseModal` and `WizardShell`. No `onBeforeClose`.
- **Why**: the POML's prop list is exact and does not include it. Modal note §7 already records that ×, Cancel and Close all route through the consumer-owned `onClose`, and the wizard stays open until the consumer sets `open={false}`. So `onClose` is the close seam. 058 opens its nested `ConfirmModal` from `onClose`.

## D4 — Footer override shape

- **POML**: "footer override". The shape was not specified.
- **Done**: `footer?: (ctx) => { start?, end? } | null`. A supplied slot replaces that side of the footer; a missing slot keeps the standard one. `ctx` provides `goBack`, `goNext`, `close`, `isFirstStep`, `isLastStep`, `isFinishing`, `canAdvance` and `currentStepId`, enough to build "Close · Back · Next open item" without new imperative-handle methods. Changing `IWizardShellHandle` would break consumers' typed mock handles. The override is not applied on the success screen, whose footer is the success config's actions.

## D5 — Embedded-only / modal-only props

- `hideTitle` and `ariaLabel` now apply to **embedded** mode only. In modal mode the header is SprkModal's and the dialog is named by `aria-labelledby` (intended change #3). The non-embedded consumers pass `ariaLabel === title`, so their accessible names are unchanged.
- `size`, `dismiss`, `uiScale` and `nav` are modal-only. `statusBar`, `footer`, `stayOpenOnFinish` and `'skipped'` work in both modes.

## D6 — Collateral edits outside the POML's file list

- `SprkModal/__tests__/a11y.test.tsx`: its explicit-dismiss case rendered `WizardModal`. It now renders `WizardShell`. It is SprkModal's own family test, not a consumer test, and deleting the preset required the change.
- `SprkModal/presets/BrowseModal.tsx`: now forwards its guard to the shell's `nav.onBeforeNavigate` (the lift, POML step 2). Its own tests pass unmodified.
- `AccessGrantModal.tsx` (a doc comment) and `docs/INDEX.md` (preset list): both named `WizardModal`. The acceptance criterion requires zero `WizardModal` hits in `src/`.
- `MODAL-DESIGN-SYSTEM.md` §7: not touched, because task 110 already amended it.

## D7 — Minor visual difference in modal mode (not in the UAT list)

SprkModal's body reserves a stable thin-scrollbar gutter (`scrollbarGutter: stable`). In a wizard the body itself never scrolls, because the content area scrolls, so a ~10px strip is reserved at the right edge. It's cosmetic. Check it in the task 111 regression pass. The fix would be a SprkModal option, which is out of scope here.

## Process note

- I accidentally wrote the ontology worktree's `docs/INDEX.md` through a .NET relative-path call and restored it immediately (`git restore`). The worktree was clean before, so nothing else was affected.
- `current-task.md` and `TASK-INDEX.md` were not edited, per the orchestrator's instruction, so the context-handoff checkpoints were not written.
