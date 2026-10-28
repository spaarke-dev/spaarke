# Task 038 notes: Create Work Assignment wizard, optional Assigned To contact picker (FR-37)

## Rigor and scope

FULL, from the POML. Executed on Sonnet 5.5 at high effort, branch `task/fm-038` cut from `51d3e82cfc` (project work branch
with task 037 merged as `eaa500c656`). Gate met: 037 is merged, so the server fills `sprk_assignedto` from
`sprk_assignedtointernal` only when the create leaves it UNSET.

rework: none

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
2. Hint rendered as a `Text` under the picker: `LookupField` has no hint prop.

## Review (Step 9.5, own diff, not the adversarial agent) and classification

| # | Finding | Class | Resolution |
|---|---|---|---|
| 1 | The 4 other wizards fail typecheck with required new fields | F | Fixed in-scope: made optional (Deviation 1); tsc clean. |
| 2 | Blank must send no key even when a parent record is set: checked the field-mapping path (`FieldMappingService` writes `${navProp}@odata.bind` with the same discovered nav-prop), so a picked contact overwrites a mapped one on the SAME key (one bind, not two) | none | Pinned by reading; no code needed. |
| 3 | A field-mapping profile that maps a parent's field onto `sprk_assignedto` still carries its bind when the picker is blank | K | Not the wizard's default and not new; the mapping is explicit configuration. Noted for the field-mapping owners. |
| 4 | Hint is visual text, not programmatically tied to the lookup input (no `aria-describedby`) | K | `LookupField` exposes no hint/describedby prop; extending the shared component is out of scope. |
| 5 | Enter Info remounts when leaving and returning (WizardShell renders only the current step) and seeds its state from the record pre-fill only, so a chosen contact (like every other field) resets on Back; the payload follows what the form then shows | K | Pre-existing for every field; display and payload agree, so no wrong contact is ever sent. |
| 6 | The Send Email body is pre-filled once (`emailSubject === ''` guard), so changing Assigned To after opening Send Email does not refresh it | K | Same behaviour as every other summary line. |

No open F-class finding.

## Placement / component justification (CLAUDE.md §11)

Existing: the follow-on event step's contact `LookupField` + nav-prop resolution (task 097). Extension: this extends the existing
step, form state and payload builder; no new component, service, endpoint or DI. Cost of doing nothing: a person creating a work
assignment cannot name its Assigned To; it always gets the Assigned To Internal default.
