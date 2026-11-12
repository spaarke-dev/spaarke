# Task 179 — Assign Work follow-on lookups on the side pane

PR [#2753](https://github.com/spaarke-dev/spaarke/pull/2753) squash-merged to master as `2ef4a17915` (owner: merge on green, 2026-11-12).

- `AssignWorkFollowOnStep`: Matter Type, Practice Area, Assigned Attorney, Assigned Paralegal and Outside Counsel use `DataverseLookupField`; the two contact fields pass `ACTIVE_CONTACT_LOOKUP_FILTERS` (D-219 Q1, Q6).
- `ICreateRecordWizardConfig.navigationService` (optional) reaches the step. Set by the Matter, Project, Event and To Do wizards; Report Card passes it to its step directly. Without it the step keeps the inline fields.
- `LookupField/lookupFilters.ts` byte-identical to task 181's copy (compared before commit; the two PRs merged without conflict).
- Deviation (POML amended): `CreateInvoiceWizard` has no Assign Work step, so four wizards, not five.
- Found in review and fixed in scope: the Assign Work card is a default on every `CreateRecordWizard`, so the To Do wizard showed it with inline fields; it now passes its existing navigationService (test fails without the fix). `WorkAssignmentWizardDialog` (own steps, task 181) and `SummarizeFilesDialog` (own card set) do not render this step.
- Tests: Spaarke.UI.Components jest 50 suites / 502 pass before the To Do fix; CreateTodoWizard 5 suites / 35 pass after; tsc clean; client-lint `ui-components ok`.
- Dev check runs with task 185's deploy verification.

rework: none
