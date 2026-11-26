# Task 180 notes (untracked; not under projects/ by instruction)

Rigor FULL, sonnet @ high, step mode directional. Branch task/ontology-180-enter-info-side-pane from origin/master 3ae2a63362.

## Deviations from the POML

1. **Invoice Vendor Organization: nothing to convert (escalation-trigger case, recorded not converted).** `CreateInvoiceStep.tsx` no longer exists:
   ebilling-r1 task 052 (d763121e5f, 2026-11-03) deleted it and rebuilt `CreateInvoiceWizard` on the shared `InvoiceSubmissionWizard`
   (record picker, firm step, PDF, firm invoice number). There is no Vendor Organization field. The wizard's only firm choice is the
   Firm step, a closed list the server returns for the caller's own firms (`loadFirms`), shown only when the caller has more than one
   firm. That is a BFF-scoped list, the same shape as D-221's SPE Admin pickers, so it is not converted. `CreateInvoiceWizard.tsx`
   and `CreateInvoiceStep.tsx` are therefore untouched; the POML's Invoice acceptance clause is void. Owner may want the review's
   section 2.1 Invoice row and the POML amended.
2. **`CreateRecordWizard.tsx` and `CreateRecordWizard/types.ts` (shared) edited minimally**: one prop line (`navigationService={config.navigationService}` on
   `CreateEventFollowOnStep`) and a doc-comment extension on `ICreateRecordWizardConfig.navigationService`. No new config field, so no
   conflict surface with tasks 182/188 beyond two adjacent-free hunks.
3. `CreateInvoiceWizard.tsx` is in the POML modify list but needs no change (deviation 1).

## What changed

`CreateEventStep`, `CreateTodoStep`, `CreateReportCardStep` take an optional `navigationService` and render their lookups with
`DataverseLookupField` (existing search callback kept as `onSearch`). Contact fields pass the shared `ACTIVE_CONTACT_LOOKUP_FILTERS`
(task 179's `lookupFilters.ts`, not copied). Event Type -> `sprk_eventtype_ref` (pane entity = the table `searchEventTypes` queries).
`CreateEventWizard`, `TodoWizardDialog`, `CreateReportCardWizard` pass their `navigationService` to the step; the Report Card wizard also
passes it to its Add To Do follow-on. `CreateEventFollowOnStep` and `AddTodoFollowOnStep` take and forward it; `CreateRecordWizard`
passes `config.navigationService` to the Create Event follow-on.

## Verification

- New tests (8 files). Fail before the source change: 25 tests in 8 suites failed with master sources; all pass after.
- Touched-folder suites (CreateEventWizard, CreateTodoWizard, CreateReportCardWizard, WizardFollowOns, CreateRecordWizard, LookupField): 37 suites / 216 tests pass.
  CreateMatterWizard, CreateProjectWizard, CreateWorkAssignmentWizard, CreateInvoiceWizard, AssociateToStep: 36 suites / 431 pass.
- `src/utils/__tests__/xrmCapabilityUsage.guard.test.ts`: 85 pass. `npx tsc --noEmit -p .`: clean. ESLint on touched files: 0 errors (1 existing
  `react-hooks/exhaustive-deps` warning for `tenantId` at CreateEventWizard.tsx:545, not touched). `client-lint check --package ui-components`: ok. Prettier: ok.
- Not run: the dev check (Console in-app + a code-page dialog, light and dark). It needs a deploy to dev; owner/main session.

## Step 9.5

code-review (self, scope: diff + 8 new tests): no F-class finding in the diff. adr-check: ADR-012 (no Xrm, injected service), ADR-021 (no styles added), ADR-028 (no fetch),
ADR-038 (behavioural tests, module-boundary mock only), ADR-050 (existing pane pattern): compliant.
Class search (rule 6): `grep -rn "<LookupField"` over these four steps leaves none; remaining inline `LookupField` users outside this task's four steps are
listed in review section 2.1 (Rule Review, ontology rule wizard, etc.), other tasks.

## Out-of-scope defects (for the operator)

1. Create Event follow-on drops Assigned To. `CreateRecordWizard.tsx` ~486-493 builds the form with `assignedToId: ''`, the `onFormValues` handler (~504-511) never reads `vals.assignedToId`,
   `IFinishContext.followOn` has no `createEventAssignedTo*` fields, and Matter (CreateMatterWizard.tsx:663-671) and Project (CreateProjectWizard.tsx:735-742) hard-code
   `assignedToId: ''`. Scenario: user picks an assignee in the follow-on, clicks Finish: the event is created unassigned; going Back/Next also clears the chip. Pre-existing (inline field behaved the same); now
   more visible. Fix touches shared type + 3 wizards.
2. `Spaarke.AI.Widgets/src/widgets/workspace/CreateAnalysisWizardWidget.tsx:584` mounts `AddTodoFollowOnStep` without `navigationService` although the widget has one (line 485): its To Do Assigned To stays inline.

## Fix round 1 (coordinator: F1, F2)

- F1: `CreateAnalysisWizardWidget` passes `navigationService` to `AddTodoFollowOnStep` (+ useMemo dep). New widget test fails before, passes after.
- F2: `ICreateEventFollowOnState` gains `createEventAssignedToId/Name`; `CreateRecordWizard` keeps them in state/refs, seeds the step, stores them from `onFormValues`, adds them to `followOn`; Matter and Project `onFinish` pass them to `EventService.createEvent` (same `sprk_assignedto` contact bind path the Create Event wizard uses). Only Matter and Project use the create-event follow-on (grep `eventDataService`, `followOn.createEvent*`): no Event/To Do host does.
- Tests: 3 new test files / cases; 5 fail before, 9 pass after (CreateRecordWizard.createEventSidePane 3 new, Matter/Project followOnEventAssignee 2 each; the two "no assignee" Matter/Project cases pass before and after by design).
- Pre-existing, unrelated: `Spaarke.AI.Widgets` `register-workspace-widgets.test.ts` 3 failures (communications-list x2, worklist x1) fail identically with the fix reverted; `tsc -p .` in Widgets cannot run standalone here (`@spaarke/ui-components` resolves to an unbuilt dist, Communication.Components has no node_modules); ts-jest type-checks the widget instead.

rework: fix-round-2 task 180


## Fix round 2
- CreateRecordWizard open-reset effect now clears createEventAssignedToId/Name (refs follow state). Test: reopen -> Assigned To empty and followOn carries ''. Fails before (chip persisted), passes after.
- Observed, not fixed: the shell keeps the 'Create Event' stepper label across a reopen (selectedActions resets, stepper label does not). Hosts normally remount on close, so low impact.
- rework: fix-round-2 task 180
