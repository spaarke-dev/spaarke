# Task 056 — WizardShell re-base onto SprkModal: deviations and decisions

> Date: 2026-10-07 · Branch `feat/wizardshell-sprkmodal-056` (worktree `C:\wts-b56`) · Base `origin/master` @ `36ff14147` (task 110 / #1380 merged)

Recorded per POML step 8. Each item says what the POML asked for, what was done, and why.

## D1 — `'skipped'` marker: first shipped for every consumer, now OPT-IN (owner decision D-69)

- **POML**: add a `'skipped'` `WizardStepStatus` that does not tick the step; "nothing else changes for existing consumers".
- **First version**: the shell's Skip button always dispatched `SKIP_STEP`. The independent review of #1386 found that this hit main steps of existing wizards in both modes (Add file(s) in every Create* wizard, Associate To, Work to Assign / Add Files, the Summarize Files and Document Upload follow-ons).
- **Fixed (`54e8f637f`, D-69)**: a new opt-in prop, `showSkippedSteps` (default `false`). With it off, Skip dispatches `NEXT_STEP` exactly as on master, so the step shows the tick. With it on, the step shows the dashed ring. The 058 decision wizard opts in.
- **Tests**:
  - With the prop off, Skip marks the step completed; the test asserts the stepper state.
  - With the prop on, the step shows the skipped marker.
  - Mutation-checked both ways: 2 and 4 failures respectively.
## D2 — `SprkModal` gained a transitional `legacySize` prop (`@deprecated`)

- **POML**: `maxWidth`/`height` strings are deprecated and must keep working; task 111 maps them to named sizes.
- **Done**: `SprkModal` has no sizing escape hatch, so the re-based shell could not honour those strings without one. I added `legacySize?: { width?, height? }`, marked `@deprecated`. WizardShell is its only caller. It replaces the named size's width (still clamped by the 96vw outer `maxWidth`), sets `height` and `minHeight`, and is ignored while maximized. Task 111 should delete it once the callers use named sizes (`CreateAnalysisWizardWidget` 60vw/70vh, `DocumentEmailWizard` 1280px/85vh through SemanticSearchControl and DocumentRelationshipViewer).
- **ADR-050**: owner-approved as an **ADR-050 Path A exception (spec §6, D-70)**; removal in task 111. The JSDoc on `legacySize`, `maxWidth` and `height` cites it.

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
