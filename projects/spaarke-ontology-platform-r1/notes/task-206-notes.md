# Task 206 notes: plan refusal trace, authoring messages, policy-order comment (#3034)

Rigor FULL, sonnet @ high. Closes #3034 (uac-r2 review of tasks 203-205).

## What changed

1. **Admin Rules page records a refused stored plan** (`OntologyAdminRulesService.ResolvePlan`).
   - `DecisionPlanService.GetAsync` had the only log + count. That block moved, unchanged, into `internal static DecisionPlanService.RecordRefusal(ILogger, Guid policyVersionId, DecisionPlanResolution)`; `GetAsync` calls it, and so does `ResolvePlan` (now an instance method so it has the service logger). The static `Resolve` stays pure.
   - Same event (`OntologyWriterEvents.DecisionPlanRefused`), same message, same counter (`ontology.decisionplan.refused`) with its existing single tag `reason`. No `surface` tag: the metric has no tag pattern for a source, so the existing tags are kept.
   - One page load is one `GetAsync` is one `ResolvePlan` call is one log line and one count (the list view never resolves plans). A rule with no version in force has no plan to refuse, so it records nothing (the decision path is never asked about a version that does not exist).
   - The comment now says what the code does.
2. **Authoring names the real refusal** (`OntologyAdminAuthoringService.ValidateDraft`). Resolver refusal reasons and API reason codes are unchanged; no new ReasonCode (none of the sibling branches for PlanMissing set one, and adding a code is the POML escalation trigger).
   - PlanMalformed: "The decision plan can't be read. Choose its actions again in the wizard."
   - UnknownAction: "This plan lists an action that doesn't exist. Choose from the actions the wizard offers for the {lane} lane."
   - Non-next-step code in `nextSteps`: "This plan lists a Next step that can't be used as one. Choose Next steps from the ones the wizard offers." The resolver reports it as `ActionNotAllowed`, same as an action outside the lane, so a small private `NextStepsAreTheProblem` asks the resolver again with `nextSteps` dropped: if that resolves, the Next steps were the problem. Validation stays in one place.
   - Lane case (action outside the lane, or a substitution-only action): sentence unchanged. A plan with both a bad action and a bad Next step gets the lane sentence (actions are checked first).
3. **`SignalReevaluationJob.ReadEnabledPoliciesAsync` comment** now says the result is sorted in memory by code, `OrdinalIgnoreCase`, not in Dataverse collation. Comment only.

## Component justification (CLAUDE.md section 11)

No new service, interface, endpoint, DI registration, package, column or metric. `RecordRefusal` and `NextStepsAreTheProblem` are internal/private helpers. Existing: the log and counter in `GetAsync`. Extension: moved into a shared helper. Cost of doing nothing: the refusal leaves no trace on the admin path (the issue).

## Tests (fail before, pass after)

- `OntologyAdminRulesServiceTests`: `Get_ARefusedStoredPlan_IsLoggedWithItsReason_AndCountedOnce` (3 reasons), plus two controls (a resolved plan, and a rule with no version in force, record nothing).
- `OntologyAdminAuthoringEndpointTests.Validate_EachPlanRefusal_GetsItsOwnPlainSentence_AndNoInternalCode_3034` (8 cases, validate and save agree, no reasonCode).
- On origin/master source: 9 of those 13 cases fail (the 3 logging cases and 6 of the 8 sentence cases); the 4 that pass are the controls and the two lane cases, which must not change.

## Known limits / observations

- None filed here. Task 204's ordering note in #3034 item 3 is confirmed cosmetic: nothing depends on the order.
- Not changed (out of scope): `GetAsync` / `RecordRefusal` log with the caller's category (`OntologyAdminRulesService` for the admin path), so a log query on category alone separates the two sources; the event id and message are the same.

rework: none task 206
