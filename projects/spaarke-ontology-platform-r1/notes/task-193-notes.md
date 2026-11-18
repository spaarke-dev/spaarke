# Task 193 notes: rule wizard no-test guard; Console Send Email side-pane lookups

PR [#2855](https://github.com/spaarke-dev/spaarke/pull/2855), head `0350264943` (implementer agent, Sonnet). Main-session review PASS 2026-11-16; merged to master `c0d8a5b74f`.

- **A7 root cause:** `buildRuleBody` returned an Existence body with no `when` and no clause, so `canLeave('condition')` let Next through; the BFF schema refused it at the root, so its text started ": ".
- **Fix:** `buildRuleBody` adds issue `all` = "Add at least one test on the record or one related-record test." when there is no row problem, no `when` and no clause. `ConditionStep` strips a leading ": " from the ruleBody refusal. Schema and BFF unchanged; the BFF still refuses such a body. Escalation triggers did not fire.
- **D1:** `ConversationPane` passes `onLookupRecipients`, `recordLookupCatalog`, `onLookupRecord` and `onAddRelationship` from the existing `emailComposeHandlers`; no second factory call.
- **Tests:** OntologyAdmin + EmailComposer + `wizardLookupPatternGuard` 601 pass; SpaarkeAi conversation 746 pass; the new tests fail on the old source. Existing wizard-flow tests now add one record test; one "Next enabled with bare table" assertion became disabled (it encoded the A7 defect).
- **tsc:** Spaarke.UI.Components 0 errors; SpaarkeAi 448 pre-existing, none in touched files. client-lint: ui-components and spaarkeai pass.
- **Review:** code-review + adr-check clean after one lint F-class (`?? []`, no-unnecessary-condition) fixed; `/conflict-check` clean.
- **K4:** the save/publish refusal list in `RuleWizard.tsx` (~725) still prints BFF text verbatim (the no-test case cannot reach it now).
- **K4:** client-lint fails on other packages in files this PR does not touch (fresh worktree with only shared libs built).
- **Deviation:** prettier (lint-staged) reformatted about 10 existing lines in `ConditionStep.tsx`.
- **Browser check pending:** rides the Console auto-deploy after merge (185 checklist A7, D1).

rework: none task 193
