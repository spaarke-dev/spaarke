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
After a reset the step content remounts, so the control that held keyboard focus (Try again, Next open item) is removed. The
original note here said focus "stays in the dialog"; that was wrong (jsdom and browsers drop it to `<body>`). Corrected in fix
round 1 (F1-b): `WizardShell` now moves focus to its content area. Recording still moves focus to the status bar (effect also
keyed on `resetKey`).

## Fix round 1 (adversarial review of PR #2388)
- **F1-a late side-pane pick.** `DecisionItemWizard.update` is session-stable and called `onDraftRef.current`, the CURRENT item's
  updater, so a Person/Lookup pick for item A released after the person moved on landed in item B's draft (same action) or was
  dropped (B loading). Now a `Map<itemId, onDraftChange>` ref is filled while an item is ready and `update` is
  `useMemo(..., [itemId])`, bound to the item it was rendered for. Test: `DecisionWizard.latePick.test.tsx` (B ready, B loading,
  must-not-fire on the same item); failed at the review head (B got the value / A stayed empty), passes now.
- **F1-b focus to `<body>`.** `WizardShell`: an effect keyed on the applied reset key (skips first mount) focuses the content
  area when focus is not inside the dialog (embedded: when it was lost to `<body>`). The content area gets `tabIndex={-1}` (and
  an outline-none class) ONLY when `resetKey` is set; without it the markup is unchanged. Tests: `oneDialog` (e) Try again ->
  ready and (f) record -> Next open item; `resetKey` tests for focus moved, focus already inside left alone, no `tabindex`
  without `resetKey`, `tabindex=-1` with it. (e), (f) and the focus-moved test failed before.
- **F3.** The "forgets skipped marks" test now clicks Skip on step a, asserts the stepper shows it skipped, resets, and asserts no
  step is skipped. It passed at the old head too (RESET builds a fresh state, so the behaviour was already right); the fix is that
  it now exercises what its title says.
- **K4.** `WizardShell` compares `!Object.is(resetKey, appliedResetKey)`; a NaN key no longer loops. One-line test added; it failed
  (render loop) before.
- Render-phase reset mechanism and behaviour without `resetKey` untouched.

## Re-check of fix round 1 (scoped Sonnet, 2026-10-28) and fix round 2 (main session)
- F1-a, F3 and K4: confirmed fixed by trace.
- **F2, fixed:** the post-reset focus effect also moved focus that was held OUTSIDE the wizard's dialog. Scenario: the discard confirm is open, and an item that was loading finishes. The effect now moves focus only when it was lost (`<body>` or none). New test: focus in a separate dialog survives a reset. It fails on the fix-round-1 code (mutant run: 1 failed) and passes now.
- **K4, accepted:** under StrictMode in development, the skip-first-mount guard may focus the content area once at open. This is harmless; the comment now says so.
- Wizard and DecisionWizard: 17 suites, 295 tests pass. `tsc --noEmit` is clean.
