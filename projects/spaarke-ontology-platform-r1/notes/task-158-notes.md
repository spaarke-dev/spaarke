# Task 158 notes - decision wizard side-pane lookups and subject link (D-182)

Branch `task/158-wizard-lookups`. Steps 1-3 done (tests first, implementation, both hosts wired). **Step 4 (deploy + dev check) is NOT done**: the main session does it after merge.

## What changed
- Shared lib (`Spaarke.UI.Components`, no Xrm, ADR-012): two optional host-injected callbacks, `openLookup` (`DecisionOpenLookup`) and `openRecord` (`DecisionOpenRecord`), carried the same way `lookup` is: `WorklistWidget` -> launch request -> `DecisionWizardHost` -> `DecisionWizard` -> `DecisionItemWizard` -> `DecisionItemContext`. `DecisionWizard` fills the item's core record into the request, as it does for `lookup`.
- `ParameterFields`: with `openLookup` and a table (Person = `contact`; Lookup = the catalog's `lookupEntity`), the field is a read-only input + search button that opens the pane (single flight, cancel/failure = no change). Otherwise the inline `LookupField` is unchanged. D-18 pre-select still runs from the host's `lookup` search and never opens the pane.
- `FoundStep`: the subject name is a Fluent `Link` calling `openRecord(recordType, recordId)` when the host gave `openRecord` and the subject has a name, type and id; plain text otherwise.
- Hosts (`Spaarke.AI.Widgets` `worklistLookup.ts`): `createWorklistOpenLookup(nav)` builds the same scope as the inline search as a FetchXML `filters` entry (Person: active contacts; budget: active + the item's matter/project; organization: none; any other entity: null). `createWorklistOpenRecord(nav)` returns `openRecordModal` or undefined (never `openForm`). Wired in `WorklistWorkspaceWidget` and `LegalWorkspace/.../worklist.registration.ts` over `createXrmNavigationService()`, only when Xrm exists.

## Decisions (directional step mode)
- The filter is built by the host, not the wizard: the wizard passes `{kind, entity, parameterCode, core}` and never names a filter (keeps scoping beside the inline search it must match).
- Budget with no core: active-only filter. Today's inline search also allows a typed-name search across budgets with no core, so this is no wider.
- Unknown entity or non-GUID core id: the host opens nothing (the inline search returns nothing either).
- Extra test files: `DecisionWizardSidePane.test.tsx` (wizard, 16 cases) and `worklistOpenLookup.test.ts` (host scope, 10 cases) are the cases the POML lists plus negative cases (cancel keeps a prior value, failure = cancel, single-flight, Not filed, no id, openRecord failure). `WorklistWorkspaceWidget.test.tsx` and DailyBriefing `worklistSectionRegistration.test.tsx` (+ its ai-widgets mock) got host-wiring cases.

## Known limits / risks
- **Stacking (POML escalation trigger) is untested here.** The wizard's `SprkModal` is a blocking Fluent modal. Earlier work found a blocking modal's backdrop covers or swallows clicks on the native `lookupObjects` pane (CommunicationActions UAT r6/r7; AccessGrantModal "Manage Access" 2026-10-07). `SprkModal` already has `nonBlocking` + `yieldToSidePane` for this, but `WizardShell` does not pass them through, and turning them on changes the wizard's modality, which is an owner decision. Nothing was changed. If the dev check shows the pane or the record dialog behind the wizard, STOP (POML) and decide: wire `nonBlocking`/`yieldToSidePane` through `WizardShell`, or another approach.
- D-18 pre-select needs the host's `lookup` search; both hosts provide it. A host with `openLookup` but no `lookup` would not pre-select.
- openRecord/openLookup failures are swallowed (treated as cancel); nothing is shown to the user.

## Pre-existing, found in passing
- `DecisionWizard.test.tsx` "Escape and the backdrop do nothing ... Keep deciding on the left" is flaky on unchanged origin (7/8 runs failed): issue #2293.
- Environment only (no node_modules / dist in the worktree): UI.Components has 43 suites failing on `Cannot find module '@spaarke/sdap-client'`; DailyBriefing has 3 suites failing on `@spaarke/ui-components/types/LookupTypes`. Unrelated to this change.

## Verification
See the PR description for commands and counts.
