# Task 203 - user-facing wording tells the truth (DI-157, DI-159, DI-161)

Branch `fix/ontology-203-wording-truth`, from master `9d08576e63`. Only user-visible text changes: the formal rule lines, rule
evaluation, plan validation codes and API reason codes are untouched.

## Cause and fix per item

### DI-157 (#2945) Rule wizard
- Tables by logical name. Cause: `ConditionStep.tsx` printed `s.entity` / `j.related` as the option label. The catalog carries no display
  names. Fix: reuse the existing table display-name list that `RecordFamilyModal` kept privately (`TABLE_LABELS`, which covers every table
  the rule grammar reads). It moved, unchanged, to `src/utils/tableLabels.ts` (exports `TABLE_LABELS`, `tableLabel`); `RecordFamilyModal`
  imports it; `ConditionStep` shows `tableLabel(...)` for the subject and the related-record lists and keeps the logical name as the value.
  No endpoint, no catalog field, no new list.
- `(plan_missing)`. Cause: `OntologyAdminAuthoringService.ValidateDraft` wrote `The decision plan is refused ({RefusalReason}): ...` for every
  refusal, and an empty plan refuses as `plan_missing`. Fix: that one refusal reads "Choose at least one action." The refusal reason
  (telemetry, the plan route, the stored-plan refusal) is unchanged. Validate and save still return the same text.

### DI-159 (#2950) Decision wizard
- "where state is 0". Cause: `RuleBodyDescriber.Phrases` printed the `statecode` value raw under the label "state". Fix: a `statecode` test
  of 0 / 1 (scalar or list) reads "the budget is active" / "is inactive"; any other value keeps the old wording. The formal line is unchanged
  (`statecode = 0`). `statuscode` is left as it was: its options differ per table and the describer has no per-table label source (see
  Noticed).
- "the person above". Cause: `DecisionActionCatalog` Assign Task effect line. Fix: "assigned to the person you choose". The BFF catalog test
  and the wizard's fixture copy in `DecisionWizard.assignTaskNotify.test.tsx` changed with it; no other catalog test changed.

### DI-161 (#2952) Worklist summary
- Cause: `buildWorklistNarrativeRequest` sent past-due Do items as a third category, and the Daily Briefing narrate prompt (Dataverse)
  writes "N notifications across K categories" from `totalNotificationCount` / `categories`.
- Fix without any Dataverse or playbook write (option b of the issue): the request carries the two lane categories only (never more
  categories than lanes, past due never a category), and the summary line shown is built from the counts in the client
  (`worklistSummaryLine`): "241 Work Items: 192 to decide and 49 to do. 49 of the 49 ... past due." (Past due is said of the Do items only.)
  The Assistant's own summary sentence is not shown; its bullets are. `useWorklistNarrative` swaps the summary in.
- Residual: the Assistant's bullets are still written by the Dataverse prompt and could say "notifications". Fixing that needs the prompt
  edited in Dataverse (owner approval); not done here.

## Tests (fail on origin/master, pass on the branch)
BFF (Sprk.Bff.Api.Tests; 7 failed against master src, 383 / 383 pass after, filtered to RuleBodyDescriberTests, DecisionActionCatalogTests,
OntologyAdminAuthoringEndpointTests, AuthoringCatalog):
- `RuleBodyDescriberTests.AStateCodeTest_IsDescribedByItsLabel_NotItsNumber_AndTheFormalLineKeepsTheNumber` (x2)
- `RuleBodyDescriberTests.AStateCodeInASubjectWhen_IsDescribedByItsLabel_ForAThresholdBodyToo`
- `RuleBodyDescriberTests.EachDoFixture_...(do-workassignment-past-due)` (expected text changed)
- `DecisionActionCatalogTests.AssignTask_IsADecideLaneActionNotANextStep_WithTheD178Shape`
- `OntologyAdminAuthoringEndpointTests.Validate_AnEmptyPlan_SaysChooseAtLeastOneAction_AndNoInternalCode_DI157` (x2)

Client (jest, UI.Components; 5 failed against master source, pass after):
- `RuleWizard.test.tsx` "shows each table by its display name and keeps the logical name as the value (DI-157)"
- `worklistNarrative.test.tsx` "carries the lanes' counts, one category per lane, and nothing else";
  `worklistSummaryLine` "names the two lanes and the Work Items ..." and "says all of them when every Do item is past due ...";
  `WorklistWidget narrative` "shows the Assistant's narrative above the lanes ..." (now asserts the deterministic line)

## Results
- `dotnet build src/server/api/Sprk.Bff.Api/` 0 errors; `dotnet build tests/Spaarke.ArchTests` 0 errors; full ArchTests 2775 / 2775.
- jest (Worklist, OntologyAdmin, DecisionWizard, RecordFamilyModal): 35 suites, 832 tests, all pass; one unrelated `Rules.test.tsx`
  retire-dialog test failed once under load and passed alone (38/38).
- `tsc --noEmit` in Spaarke.UI.Components clean. Console (`src/solutions/SpaarkeAi`) `npm run build` OK (surface-owned tsc errors: 0).
- client-lint: ui-components and spaarkeai ok. Other surfaces (ai-widgets, compose, communication, daily-briefing, legal-workspace,
  reporting, semantic-search) report one violation each in files this task does not touch (no node_modules for those packages in this
  worktree); not caused by this change.

## Publish size (.claude/rules/bff-hygiene.md item 4)
Fresh short-path worktrees, `dotnet publish -c Release`, `Compress-Archive` of both outputs:
- origin/master (`9d08576e63`): 191 files, 42,615,397 bytes zipped (141,225,153 raw)
- branch: 191 files, 42,616,229 bytes zipped (141,225,849 raw)
- delta: +832 bytes zipped, +696 bytes raw, 0 files. No package change.

## Component justification (CLAUDE.md section 11)
No new service, endpoint, DI registration, package, column or catalog field. The one new file (`utils/tableLabels.ts`) is the existing
`RecordFamilyModal` table list moved so two callers share it (Existing: that list; Extension: it is the extension; Cost of doing nothing: a
second hand-kept copy of the same list).

## Noticed, not changed
- The Reassign effect line (`DecisionActionCatalog`, "Assigns the item to the person above") has the same "above" pattern; the issue names only
  Assign Task.
- Other plan refusals (`action_not_allowed`, `duplicate_action`) still show the internal code in the validate message, and existing
  endpoint tests pin them. Only `plan_missing` (the case in #2945) changed.
- `statuscode` is still shown as a number (e.g. "status is 659490001").
