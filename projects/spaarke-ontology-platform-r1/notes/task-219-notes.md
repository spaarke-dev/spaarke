# Task 219 notes: wizard reopen drops follow-on steps (#2814, DI-155)

## Findings (step 1)

- The reset-on-open effect in `WizardShell.tsx` ("Reset on open (false -> true)") dispatched `GO_TO_STEP { stepIndex: 0, clearSkipped: true }`.
  That reducer case only re-marks statuses; `state.steps` (static + dynamic) is untouched, so follow-on steps from the last
  session stayed in the stepper. The config map was rebuilt from the static steps, so the leftover steps also had no config.
- `GO_TO_STEP` is dispatched only by that effect (grep of every `GO_TO_STEP` / `clearSkipped` in `src`); the only other uses
  are reducer tests. No other caller depends on it.
- The reducer already has a `RESET` action (task 165, `resetKey`) that rebuilds state from the static step configs exactly
  as a fresh mount would. The open effect now dispatches `RESET { steps: stepConfigs }` (no `initialStepId`, so a reopen
  still lands on step 0 as before). No new action, prop, or component (root CLAUDE.md section 11: extension of an existing mechanism).

## Dynamic-step consumers checked (escalation trigger did not fire)

`DecisionItemWizard`, `InvoiceDecisionWizard`, `RecordProcedures/WizardFrame`, `CreateRecordWizard`,
`WorkAssignmentWizardDialog`, `SummarizeFilesDialog`, `DocumentUploadWizard/NextStepsStep` add steps through the handle after
the shell is open. `DecisionItemWizard` re-syncs on `steps` changes, so after the reset it re-adds what it wants (before, a stale
dynamic step stayed in `state.steps` with no config after the config-map rebuild, so it could not self-heal). No props contract change.

## Tests

- `__tests__/wizardShellReducer.test.ts` (added describe): `RESET` after `ADD_DYNAMIC_STEP` gives the static steps; `RESET`
  forgets skipped marks; must-not-fire `GO_TO_STEP` keeps dynamic steps.
- `__tests__/WizardShell.reopen.test.tsx` (new): reopen drops the follow-on step and re-adding adds it once (fails on origin/master);
  re-render while open keeps it; reopen still starts at step 1 with skipped marks cleared.

Deviation from the POML acceptance wording: the reducer test cannot fail on origin/master, because `RESET` already
behaved correctly there; the defect was the shell dispatching the wrong action. The failing-on-master proof is the WizardShell test.
