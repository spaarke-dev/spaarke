# Task 038 notes: Create Work Assignment wizard, optional Assigned To contact picker (FR-37)

## Rigor and scope

FULL, from the POML. Executed on Sonnet 5.5 at high effort, branch `task/fm-038` cut from `51d3e82cfc` (project work branch
with task 037 merged as `eaa500c656`). Gate met: 037 is merged, so the server fills `sprk_assignedto` from
`sprk_assignedtointernal` only when the create leaves it UNSET.

rework: fix-round-1

## Escalation check (POML trigger)

Trigger: "the child-records route does not run the Assigned-To policy on a supplied `sprk_assignedto`". Read, not live-tested:
`OwnedChildWrite.CreateAsync` runs `CheckCallerMayCreateAsync` over every lookup in the item (a supplied `sprk_AssignedTo`
bind costs AppendTo on that contact, as the caller), and `ChildRecordEndpoints` runs `AssignedAccessMaterializer.RunAfterWriteAsync`
after the create (`writtenColumns: null`, so it reads the created row). Task 037's notes record the supplied-Y cases
(kept, checked, materialised). The trigger does not fire; no client-side check was added.

## What changed (all under `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/`)

| File | Change |
|---|---|
| `formTypes.ts` | `assignedToId` / `assignedToName` on `ICreateWorkAssignmentFormState` (OPTIONAL, see Deviations) and `''` in `EMPTY_WORK_ASSIGNMENT_FORM`. |
| `EnterInfoStep.tsx` | "Assigned To" `LookupField` (contact search via `searchContactsAsLookup`, copied from `CreateFollowOnEventStep`) after Priority / Response Due Date, with the hint "Leave blank to use the Assigned To Internal value" (`Text`, `colorNeutralForeground3`). Reports id/name through the extended `onFormValues` Pick. `isValid` unchanged. |
| `WorkAssignmentWizardDialog.tsx` | `onFormValues` Pick extended; the Send Email pre-fill summary gets `Assigned To: {name}` only when a contact was chosen. |
| `workAssignmentService.ts` | In `createWorkAssignment`, only when `form.assignedToId` is set: `toNavPropMap(navProps)['sprk_assignedto']` (exact column, as the follow-on event step) then `{navProp}@odata.bind = /contacts({guid})`; nav-prop absent: `console.warn`, no bind, create proceeds. Blank: nothing is written (no bind, no null). |

## Tests (KEEP paths, `__tests__/`)

| File | Cases |
|---|---|
| `workAssignmentService.assignedTo.test.ts` (new) | Chosen contact: exactly one bind to `sprk_AssignedTo`, with systemuser and look-alike contact lookups (`sprk_assignedattorney1`, `sprk_assignedparalegal1`, `sprk_assignedtointernal`, `sprk_assignedto1`) listed first; braces/upper-case GUID cleaned. Blank (empty id; id never set): no key matching `assignedto`, no `/contacts(` value, no null, with the nav-prop discoverable. Nav-prop absent: no bind, warning, create succeeds. |
| `EnterInfoStep.assignedTo.test.tsx` (new) | Picker + hint render; blank keeps Enter Info valid and reports `''`; a chosen contact is reported with id/name and validity is unchanged; a missing required field still invalidates (rule unchanged). |
| `WorkAssignmentWizardDialog.assignedTo.test.tsx` (new) | The summary contains `Assigned To: {name}` when chosen and no `Assigned To` text when blank (real dialog state; shell, Enter Info and the Next-Steps grid are recorders). |

Test-scope justification beyond the three service cases: the EnterInfoStep and dialog tests are AC4 ("validity unchanged;
summary only when set"), which is `testable="true"` and not reachable from the service.

## Red / green evidence

- Source files (`EnterInfoStep.tsx`, `WorkAssignmentWizardDialog.tsx`, `workAssignmentService.ts`, `formTypes.ts`) reset to
  `51d3e82cfc`, the three new suites run: **5 red / 4 green** of 9. Red: chosen-contact bind, nav-prop-absent warning,
  picker + hint, chosen contact reported, summary line. Green by design (negative controls: blank sends nothing, summary
  absent when blank, validity rule): they pin that the change does not over-reach.
- Restored: the 3 new suites **9/9 green**; the whole wizard folder **7 suites, 29 tests passed** (4 existing suites unchanged).

## Checks

- Wizard jest (`npx jest src/components/CreateWorkAssignmentWizard`): 7 suites / 29 tests passed.
- `tsc --noEmit -p tsconfig.json` (Spaarke.UI.Components): no errors in changed files. Two `TS2307 '@spaarke/auth'` errors in
  `AccessGrantModal/types.ts` and `utils/useWizardPageBootstrap.ts` are environmental (auth `dist` not built in this checkout) and
  not in the diff.
- CI ratchet: `client-lint.mjs install --package ui-components` then `check --package ui-components`: ok, 0 new findings.
- Code page: `src/solutions/CreateWorkAssignmentWizard` `npm install --legacy-peer-deps --no-audit --no-fund` then `npm run build`
  (vite): 2324 modules, `dist/index.html` 1,070.82 kB; the hint string is present in the bundle. Not deployed (task 076).

## Deviations

1. `assignedToId` / `assignedToName` are optional on the interface (`?: string`) rather than required. Four other wizards
   (`CreateInvoiceWizard`, `CreateMatterWizard`, `CreateProjectWizard`, `CreateReportCardWizard`) build an
   `ICreateWorkAssignmentFormState` literal for their Assign Work follow-on and have no picker; required fields broke their
   typecheck (TS2739). Optional keeps those files untouched and means the same thing (absent = blank = no key). The empty state
   still sets `''`.
2. (Superseded by fix round 1) The hint is now `LookupField`'s new optional `hint` prop, passed to Fluent `Field`.

## Review (Step 9.5, own diff, not the adversarial agent) and classification

| # | Finding | Class | Resolution |
|---|---|---|---|
| 1 | The 4 other wizards fail typecheck with required new fields | F | Fixed in-scope: made optional (Deviation 1); tsc clean. |
| 2 | Blank must send no key even when a parent record is set: checked the field-mapping path (`FieldMappingService` writes `${navProp}@odata.bind` with the same discovered nav-prop), so a picked contact overwrites a mapped one on the SAME key (one bind, not two) | none | Pinned by reading; no code needed. |
| 3 | A field-mapping profile that maps a parent's field onto `sprk_assignedto` still carries its bind when the picker is blank | K4 | Correct supplied-value behaviour (FR-36: a supplied value wins), but the hint "Leave blank to use the Assigned To Internal value" is not true in that configuration. A scalar mapping rule writing null onto it would be a configuration error. |
| 4 | Typed-but-unpicked text in the picker sends nothing | K4 | By design (only a picked contact is bound); pinned by `typed-but-unpicked text reports no contact`. |

## Fix round 1 (independent review of `8b13873034`)

rework: fix-round-1

Core contract confirmed by the reviewer (blank never sends a key, bind or null). Fixed:

| # | Finding | Class | Resolution |
|---|---|---|---|
| 1 | Back to Enter Info wiped every edit (Enter Info remounts; seeded from the record pre-fill only), so the picker showed blank while the Send Email summary still named the old contact; with no record, everything typed was lost | F1 | `WorkAssignmentWizardDialog.tsx`: `enterInfoSeenRef` (set when Enter Info reports values; cleared when the selected record changes and on reopen). A remount gets `initialValues = formStateRef.current` and `hasInitialValues = true` (AI pre-fill does not re-run). Test `WorkAssignmentWizardDialog.backNav.test.tsx` (real `EnterInfoStep` + `LookupField`: type a name, pick a contact, unmount, re-render; name and contact still shown; a cleared contact stays cleared). The former K #5 is removed (fixed). |
| 1b | The Send Email pre-fill was one-shot, so a changed Assigned To (or name) did not reach it | F1 | `autoEmailRef` holds the subject/body last pre-filled; a later Enter Info report refreshes the body (and the subject if unedited) while the body still equals it. An edited body is left alone. Built by the new `buildEmailPrefill`. Tests in `WorkAssignmentWizardDialog.assignedTo.test.tsx` (`refreshes the unedited summary...`, `leaves an email the person edited alone`). The former K #6 is removed (fixed). |
| 2 | Nav-prop not found: the chosen contact was dropped with only a console warning | F1 (low) | `warnings.push('Assigned To could not be set; the record uses its Assigned To Internal value.')` (the dialog shows `result.warnings`; status becomes `partial`). Asserted in the nav-prop-absent test; the blank test asserts no warning. |
| 3 | Hint not linked to the input; the `Text` `marginTop` did nothing (inline) | F4 | `LookupField` gets an optional `hint?: string` -> `<Field hint={hint}>` (Fluent wires `aria-describedby`); `EnterInfoStep` uses it and the free-standing `Text` and its style are gone. Tests: `LookupField.test.tsx` (hint linked; no `aria-describedby` without one) and `EnterInfoStep.assignedTo.test.tsx`. |
| 4 | The EnterInfoStep test's search double was not production-like (browse returned a contact with no query) | F2 | The double returns `[]` below 2 characters (as `searchContactsAsLookup` does); the test types `ch`; the picker sets `minSearchLength={2}`; asserts the search is never called with fewer than 2 characters. |
| 5 | Stale comment in `services/userLookup.ts` said `sprk_workassignment.sprk_assignedto` targets systemuser | F2 | Corrected in the file header and both JSDoc blocks (a contact lookup, live schema 2026-10-27; points to `resolveCurrentUserAsContactAssignee`). Comment only. |

Red/green for the round: with the round-1 production files reset to `8b13873034` and the new/changed tests kept, **5 red**
(nav-prop-absent warning, LookupField hint, EnterInfoStep hint link, Back-to-Enter-Info keeps the edits, unedited summary
refreshes); the negative controls (edited email left alone, cleared contact stays cleared, blank sends no warning) pass both
ways. After: all green.

Test-harness note: a test that renders inside `waitFor` mutates the DOM and re-triggers `waitFor` endlessly when the condition
never holds (it ran out of memory against the old code). The dialog test re-renders one tree instead.

Checks after the round: wizard folder + `LookupField` + the four sibling wizards (`CreateInvoiceWizard`, `CreateMatterWizard`,
`CreateProjectWizard`, `CreateReportCardWizard`) + `userLookup` service tests: **33 suites, 331 tests passed**. `tsc --noEmit`:
no errors in changed files (the two environmental `@spaarke/auth` errors remain). client-lint ratchet `ui-components`: ok.
Code page `src/solutions/CreateWorkAssignmentWizard` `npm run build`: ok (`dist/index.html` 1,071.11 kB).

No open F-class finding.

## Placement / component justification (CLAUDE.md §11)

Existing: the follow-on event step's contact `LookupField` + nav-prop resolution (task 097). Extension: this extends the existing
step, form state and payload builder; no new component, service, endpoint or DI. Cost of doing nothing: a person creating a work
assignment cannot name its Assigned To; it always gets the Assigned To Internal default.
