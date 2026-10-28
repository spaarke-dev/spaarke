# Task 165 notes: decision wizard keeps ONE dialog from launch to close

Rigor FULL (shared `WizardShell`, `.tsx`, modifies tests). Branch `task/165-decision-wizard-one-dialog`. Sonnet/high.
conflict-check: no open PR touches `components/Wizard/` or `components/DecisionWizard/` (gh pr list, 2026-10-28).

## Cause (confirmed)
`DecisionWizard` rendered the loading/error state as its own `WizardShell` and the ready item as `DecisionItemWizard`
(keyed by item id and `:recorded`), each a modal `SprkModal`. Every state change was an unmount plus a mount of the dialog.

## Change
- `WizardShell`: ONE additive, optional prop `resetKey?: string | number`. When it changes after mount the shell rebuilds its step
  state in place (new reducer action `RESET` -> `buildInitialShellState(steps, initialStepId)`), clears finishing state, the Finish
  error and the success screen, re-registers the base step configs, and remounts the step CONTENT (a fragment keyed by
  `resetKey` inside the content area). The `SprkModal` is not touched. Absent: no behaviour change (the fragment has no key).
  The reset is applied during render (React's derived-state pattern), not in an effect: an effect would let one render commit the
  new steps against the old step state, and `ShellSync` (which turns step statuses into draft decisions) would write the previous
  item's taken/skipped marks into the next item's draft.
- `DecisionItemWizard` is now always mounted and never keyed. Props: `itemId`, `view` (`loading` | `error` + `onRetry` | `ready` +
  the former item props), `nav`, `onClose`, `uiScale`, `today`. It owns the loading/Unavailable step (same testids, text and Try
  again button as before). `resetKey` = `{itemId}:loading|error`, `{itemId}`, or `{itemId}:recorded`; `baseIds` recompute on it
  (was `[]` + remount); the context Provider stays in the tree (value null while loading) so the shell never changes position.
- `DecisionWizard` builds the `view` and renders one `DecisionItemWizard`; its own `WizardShell`, `key`s and styles are gone.
- Loading shell title stays "Decision"; the loading footer is the stock one (Cancel, disabled Finish), as before. Finish label while
  not ready is the shell default ("Finish"), not "Record decision".

## Tests edited
None. No existing test asserted the remount. New: `DecisionWizard.oneDialog.test.tsx` (5), `WizardShell.resetKey.test.tsx` (7).
Scope beyond the stated contract: the resetKey tests for a finish-error clear, skipped marks / typed-value forgotten, and the
unchanged-key case each pin one line of the new prop's contract (one line each).

## Known limit
Keyboard focus inside the dialog after a reset is whatever Fluent leaves it on (the content remounts); focus stays in the dialog,
never on the page behind. Recording still moves focus to the status bar (effect now also keyed on `resetKey`).
