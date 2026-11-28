# Task 017 notes: intake approval on the Decide lane (FR-11)

> 2026-11-28. Rigor FULL, opus @ high. Owner decisions and the agreed ontology-r1 / uac-r2 conditions: `decisions.md`.

## What runs where

| Step | Where | What it writes |
|---|---|---|
| Run reaches Approve | `IntakeRunJobHandler.AwaitApprovalAsync` | request stage Awaiting approval (100000003), then one `IntakeAwaitingApproval` evaluation trigger (key `signal-eval-intake-awaiting-approval-{request}-{run}`), then the claim release. A redelivery writes no second marker; its trigger has the same key. |
| Work Item | the existing evaluate-plan-write path (`SignalEvaluationTrigger` → re-evaluation), nightly pass as backstop | ONE Decide-lane Signal per request (policy `POL-INTAKE-APPROVAL`), the request its own core record, owned by the resolver's team. No direct `SignalWriter.WriteAsync` from intake. |
| Claim | `POST /api/v1/intake/requests/{requestId}/claim` → `IntakeApprovalService.ClaimAsync` | only `sprk_assignedtointernal` = the caller's OWN linked contact, conditional on the row version read. |
| Approve | plan action `approve-intake` on the decision commit route → `ApproveIntakeExecutor` → `IntakeApprovalService.ApproveAsync` | run state: approved time + the current SPE version of each output document, in ONE transaction with a conditional no-op write of the request (its stage unchanged), each row at the version the checks read, so a stage or claimer change in between refuses; then the Return job (key `intake-return:{runId:N}`), then its marker. The Decision Record (review kind, Judgement) is the commit route's. |
| Reject | plan action `reject-intake` → `RejectIntakeExecutor` → `IntakeApprovalService.RejectAsync` | run Cancelled (never Failed) with the rejected time AND the request stage In review, in ONE transaction conditional on both rows. The reason is stored on the Decision Record (step value) and never logged. |
| Any fault | the run read, the claim write, the decision transaction | a coded answer, never a 500 on the claim route: 503 `INTAKE_UNAVAILABLE`. On the commit route a fault at preflight is 503 `StepFailed` (nothing written); a fault at write is a failed step naming the request as possibly written (a timeout can land after the commit; a retry converges). |

The caller's contact must be ONE contact that represents only them: a contact also linked from another user is refused (403), so two users never share a claim.

## Deviations from the POML (each agreed or judged; listed for review)

1. **Claim write path.** The POML says "through the existing Assigned-To write path (ADR-034 A4)". uac-r2 directed otherwise (2026-11-28): the app writes the column directly, checks as the caller first, writes the caller's own contact only, `If-Match` always, and A4 is NOT extended to service requests (a service request is never grantable to a contact; the claimer is granted nothing). Built that way; a test pins that a claim creates no access row and no grant, and that A4's root parser still refuses `sprk_servicerequest`.
2. **No ForPerson registration.** `sprk_assignedtointernal` is not registered as the request's ForPerson column (the create paths would stamp the creator and a new request would arrive claimed). One named constant (`IntakeRunStore.RequestClaimedBy`) is used by the claim write, the checks and the executors. A test pins that the ForPerson set does not contain the request.
3. **WorkType null.** No approval work type yet: the Console shows the item under "Other". Follow-up for task 019: a `sprk_policy.sprk_worktype` option, the Console labels and the parity corpus.
4. **Seed script.** `scripts/intake/Apply-IntakeApprovalPolicy.ps1` is a wrapper over `scripts/Apply-DoLanePolicies.ps1` with a new definition kind (`intake-approval-policies`: Decide lane, codes approve-intake/reject-intake only, no Next steps, a null work type allowed) instead of a copy of the script. The guard test that ties the script's lists to the catalog covers the new kind. Written, not run (owner's go needed).
5. **Test locations.** The POML names a seam test `tests/integration/seam/Intake/IntakeApprovalDecideLaneSeamTests.cs`. The Decide-lane behaviour is covered without live Dataverse instead: the evaluation (`tests/unit/domain/Signals/SignalReevaluationJobTests.IntakeApproval.cs`), the commit route (`tests/integration/data-mutation/Signals/DecisionCommitServiceTests.IntakeApproval.cs`), the reader gating (`tests/integration/auth/Intake/IntakeApprovalReaderGatingTests.cs`) and the policy definition (`tests/unit/domain/Signals/IntakeApprovalPolicyDefinitionTests.cs`). A live seam test belongs after the policy is seeded.
6. **Readers unchanged.** The POML lists `SignalReadService` and `DecisionCapability` to modify. No change was needed: both reach the core record through the caller's own read per core type (`SignalCoreRecordAccess`), and `sprk_servicerequest` is already a core type; an unknown core table is already NotFound. Pinned by the reader-gating tests. No external surface reads intake Signals or their Decision Records.
7. **Approve and reject need a claim.** The POML says "after a claim, only the claimer may approve or reject". Built stricter: approve and reject require an existing claim by the caller (an unclaimed request is WrongState, 409-class), so every decision has a named claimer.
8. **Kill switch codes.** The claim route answers 503 `INTAKE_DISABLED`; the commit route answers 503 with `decision.action.disabled` (its own code family), mapped from the executor's preflight.

## Live check (POML step 2)

Read-only, no new privilege or secret, by the PredicateCompiler remarks method: the check passed, so `sprk_servicerequest` was added to `EvaluatorGlobalReadableEntities`.

## Known limits (K-class)

- **Team cache.** Owning-team membership comes from the identity service's cache (about 2 minutes), so a just-removed member can still claim or decide inside that window. Rights on the request are asked live each time.
- **The form edits the claimer.** `sprk_assignedtointernal` is editable on the request form, so a user with Write can change the claimer outside the route (bypassing the 409 exclusivity). The checks read the column each time and every decider must pass Read+Write, team and own-contact checks, so the result is a different claimer, never a bypass of who may decide. Follow-up: field security (or a read-only form control) on the column.

## Escalation: a refusal at execute time is a 500 on the commit route

POML criterion 7 says a claimer who loses the claim between preflight and execute is refused 403 again. The executor does refuse (NotAuthorized, nothing written; `IntakeApprovalAuthorizationTests`), but the shared commit route (`DecisionCommitService`, ontology-owned) records any execute-time refusal as a Failed, resumable step and answers 500; the same holds for the harness switched off between preflight and execute (500, not 503). Options: (a) accept the D-17 contract (a Failed step, resumable, nothing written) and amend the criterion; (b) have the commit route answer the preflight status for a first step that was cleanly refused with nothing landed, which changes every executor's behaviour and needs ontology-r1's sign-off. Not changed here.
- **Approve during a redelivery.** If a duplicate delivery bumps the run's row version (or a form edit the request's) between the checks and the write, approve answers WrongState and writes nothing; a retry succeeds.
- **A reject's Decision Record outcome.** The review-kind record's outcome is Authorized for both actions; "rejected" is the taken step `reject-intake` on the record. No new outcome value was added (no new DecisionRecordWriter kind, owner constraint).
- **The intake policy is seed-only.** `sprk_intakestage` is not an admin-authorable column and `sprk_servicerequest` is not an authoring subject, so the admin authoring routes cannot dry-run or re-version this policy; the seed script is its only author. Authoring for other rules is not widened.

## Deferred defect: a second approval cycle on the same request (blocks the re-run doors)

Not reachable today: no production code creates a second run for a request (`CreateRunAsync` has no production caller until the doors, tasks 021/022/043/044). When a door lets an attorney start a new run after a reject (spec: "the attorney starts a new run"), three things break:

1. **No new Work Item.** The cycle-1 Signal is Acted. An Acted Decide Signal is re-raised only on newer dated evidence (D-119), and this Existence rule has none, so the cycle-2 run waits for ever. Needs ontology-r1's answer: a dated witness in the rule, or closing the Signal ConditionCleared on reject.
2. **The old claimer stays.** Reject does not clear `sprk_assignedtointernal`, so the cycle-1 claimer holds cycle 2 and everyone else gets 409 (no unclaim). Clearing it on reject needs the decider recorded on the run (the retry check reads the claimer today).
3. **The decision is not bound to its run.** The executors act on the request's current run, not the run the Signal was raised for: a cycle-1 review resumed after cycle 2 started would decide the cycle-2 run.

Fix together before any door can start a second run; reported to the coordinator for an issue.

## Open question: who owns the Work Item vs who may claim it

The Signal's owner is the ownership resolver's answer (D-129/D-130: the request's business-unit default team); the claim and decision check membership of the request's own owning team (uac-r2 (a)(2)). They are the same team only when the request is owned by its business unit's default team. When they differ, the item is listed to people who cannot claim it (they get 403) and may not be listed to the people who can. Fails closed either way; criterion 1's "owned by the request's owning team" is met only in the same-team case. Pinned by `SignalReevaluationJobTests.IntakeApproval`; needs ontology-r1 / uac-r2 to say which team is meant.
- **D-17 order.** The executor's run write lands before the Decision Record (the commit route writes the record last). A record write failure after a landed approval leaves an approved run with no record; the commit route's resume finishes the record and the executor's retry writes nothing more.
- **Unclaim.** Not built (not in the POML). Follow-up: it clears the column under the same checks, and only the claimer or someone with the approve right may clear another person's claim.
- **Stage filter in the fake.** The re-evaluation harness does not apply option-set conditions, so the stage filter is pinned on the compiled FetchXML, not by the evaluation test.
