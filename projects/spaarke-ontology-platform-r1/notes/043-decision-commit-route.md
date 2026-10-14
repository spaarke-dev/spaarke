# Task 043: the decision commit route

`POST /api/v1/signals/{signalId:guid}/decision` records ONE decision on a Signal, in the D-17 order. This note was written by the
task 043 lane on 2026-10-11 and is not a status record (see the POML for status).

## Design

| Piece | File | Role |
|---|---|---|
| Endpoint | `Api/Signals/DecisionCommitEndpoints.cs` | `RequireAuthorization`, rate limit `dataverse-query`, maps a `DecisionCommitResult` to one HTTP status |
| Service | `Services/Signals/DecisionCommitService.cs` | Validates, sequences the executors, writes the record, closes the Signals |
| After-record effects | `Services/Signals/DecisionCommitFollowUps.cs` | 032's BudgetRevision trigger and 034's suppression. Neither can fail the commit |
| Gate tier | `Services/Ai/PublicContracts/DecisionGateTier.cs` | A pure function over `RiskTierResolver` (D-52). The catalog declares a `DecisionGateFactors` for each action |
| Catalog | `DecisionActionCatalog.cs` | Adds `Gate`, `ResolvesSignal` (false only for the inquiry, D-119) and `SendsMessage`. The inquiry gains the optional `outsideFirm` |
| Executors | `DecisionActionExecutors.cs` | Adds `AssignWorkExecutor` (046's create core, matter/project only) and `BudgetInquiryActionExecutor` (070's executor, adapted rather than re-shaped). Every catalog code now has an executor |
| Record writer | `DecisionRecordWriter.cs` | Adds `Preflight` (no I/O). It runs Validate and Build on the projected record before the first write |

**Order (D-17).** The steps run in this order:

1. Validate everything before any write: every Signal as the caller, the review against the plan and the catalog, excludes, required values, gates, dismissal, the server-derived core and owner, every executor's preflight as the caller, and the record writer's own preflight.
2. Run the taken non-message actions in plan order.
3. Create the non-message Next steps.
4. Send the messages: message actions, then message Next steps, then the inquiry last.
5. Write the one Decision Record.
6. Close or link the Signals.

A failure before step 5 writes no record and changes no Signal. The result lists `landed` and `possiblyLanded`, and nothing is compensated.

**Authorization.**
- Every Signal (the primary and each Also-resolve) goes through `SignalCoreRecordAccess.AuthorizeAsync` as the caller. This happens before the review-id ledger is read, so a caller who cannot read an item learns nothing, not even a stored outcome.
- If an access check returns CallerUnresolved, the answer is the single 403. Any other result that is not Allowed is the uniform 404.
- **FR-51 (review fix F-5).** After the plan is validated and before any executor preflight, the route asks, as the caller through `CallerRecordAccessProbe`, for AppendTo on the core record (`entity.associate_document`) and Write on each covered Signal's subject (`write`, on the table `IGenericEntityService.GetEntitySetNameAsync` names). A missing right is a 403 `ontology.decision.not_authorized` before any write. A table whose entity set cannot be looked up (a metadata fault) is a 503 before any write; set names are memoised per commit. This applies to a dismissal or a record-only approval too, which no executor preflight would otherwise check. **This is stricter than the POML** (which named only the Signal read and each executor's preflight); it follows the spec's FR-51. See owner question 7.
- When an executor preflight refuses with NotAuthorized, the result is a 403 that names the action.
- This placement also keeps the route-census chain at two hops: handler → `DecisionCommitService.CommitAsync` → `SignalCoreRecordAccess.AuthorizeAsync` → `IDataverseUserClient`.

**Server-derived facts (uac-r2 binding condition).**
- The core record comes from the Signal's own `sprk_corerecordtype` and `sprk_corerecordid`, read app-only. Its table comes from `sprk_recordtype_ref`, and it must be a `CoreAncestorResolver.CoreRecordEntities` type.
- A no-core item's owner is read app-only from the item row and must be a systemuser.
- `DecisionReview` has no core or owner member, so the JSON binder drops anything the body names (tested).
- **K2 (coordinator round).** `SignalCoreRecordAccess.AuthorizeAsync` now also returns the core pair the caller's own read saw (`SignalAccessDecision.CoreTypeId/CoreId`, optional members). Validation compares it with the app-only re-read for every Signal; a mismatch is refused `ontology.decision.core_changed` before any write.
- **The record's owner is resolved in step 1** (coordinator round, item 5): `DecisionRecordWriter.ResolveOwnerAsync` runs before the first write and its answer is handed to `WriteAsync(request, owner, ct)`. An owner refusal refuses the commit (`record_refused`, nothing written); a resolver fault is a 503.

**Idempotency.**
- **One commit per review id at a time (coordinator round, item 3).** Before the ledger is read, the route takes an `IScheduledJobLease` (Redis `SET NX PX` when Redis is on, the same keyed-mutex use as `NoAccessShareEnforcer`) on `decision-review:{tid}:{oid}:{reviewId}` for 10 minutes, released in a `finally`. A second concurrent request gets 409 `review_in_progress`; a lease store that cannot be reached is a 503.
- The ledger is ADR-009 `ITenantCache`: resource `decision-review`, id `{oid}:{reviewId}`, version 1.
  - It holds an in-progress marker for 10 minutes; a repeat that meets it gets 409.
  - It keeps the first outcome (Recorded or Failed) for 7 days.
  - A resume keeps the failure it resumes inside its in-progress marker. If the resume ends without a write outcome (refused, forbidden, unavailable, cancelled), that failure is put back, so the next retry still skips what landed (review fix F-1).
  - A refusal is not cached, so the same id can be corrected and sent again.
- When the cache has nothing, the record itself answers the repeat: `sprk_confirmedby = caller` and `sprk_steps LIKE %"reviewId":"<id>"%`.
- **A repeat must be the same review (item 10).** A stored outcome is returned only when the stored content hash matches; the hash covers the Signal id and everything the review asks for. A different decision, or the same review id on another Signal, is refused `review_changed`.
- **A Recorded repeat finishes the closes (item 7).** It returns the first outcome and runs step 6 for any covered Signal the first run did not close or link (a close that failed, a crash after the record); the "already done" check makes this idempotent, and the stored outcome is updated. The durable-lookup path does the same with the record it finds.
- A cache that cannot be read fails closed (503).

**Retry (coordinator point 3).**
- `retry: true` with the same content hash resumes a Failed outcome marked `Resumable`. Steps that landed (Done) are skipped, and a record a timed-out create left behind is found and reused.
- Without the flag, the first outcome is replayed (AC4).
- **Only a step that was Refused is resumable (item 2).** By the executor contract a Refused step wrote nothing. A Failed step may have landed (a create that timed out after committing, a message that went out, a revision whose amount was then refused), and a thrown step is Failed, so none of those is resumable.
- A record-write failure after a send is not resumable (escalation trigger 1).

**A fault before the first write (re-check finding).** An unexpected exception before step 2 (for example the plan read timing out) is a 503: nothing was written, so the review id is freed, or a resumed failure is put back. Only a fault after the first write is stored as a non-resumable failure.

**Cancellation (review fix F-3).** The request's token applies only up to the first write. From then on the executors, the record write, the closes and the follow-ups run on `CancellationToken.None`, so a client that disconnects after a send still gets its record or a stored failure. A cancellation before the first write frees the review id (or puts back a resumed failure) and rethrows. A record create that throws something other than the writer's refusal (for example a client-side timeout) becomes a `create_failed` failure that may have written the record.

**Step 6 against a system closure (coordinator point 2).**
- Each Signal is re-read and updated with `UpdateIfRowVersionMatchesAsync`. On a version mismatch it is re-read once and the decision is made again.
- A Signal Resolved with a system resolution (ConditionCleared, Superseded or PolicyRetired) and `sprk_resolvedon` on or after the review start is re-closed. It gets the commit's resolution (Acted or Dismissed), resolved-on, resolved-by and `sprk_decisionrecord` = the new record. The Signal result carries the warning `reclosed_after_system_resolution`.
- A Signal a person resolved is left alone, with the warning `resolved_by_another_decision`.
- **D-120 (025, owner 2026-10-11).** Every close (including the re-close of a system-resolved Signal) writes `sprk_lastevaluated` in the same update with the same timestamp as `sprk_resolvedon`, as `SignalWriter.CloseAsync` does. `CloseAsync` itself takes system resolutions only, so the commit route cannot call it. Test: `AClosedSignal_HasLastEvaluatedEqualToResolvedOn_D120` (open close and re-close).
- The review start is:
  - on a resume or a Recorded replay, the first attempt's review start, stored in the ledger entry (review fix F-2; it used to be the first attempt's "now"), but never earlier than now minus the plan-read window of 4 hours (second review K3: a resume days later cannot reach back to re-close a system resolution);
  - otherwise the server's own plan-read stamp (K4, below);
  - otherwise now.
- **K4: the review start is never client-sent.** I chose the server-issued stamp over a clamp. `DecisionPlanReadStamp` records, in the same tenant cache, when the decision-plan route last served this caller this Signal's plan (key `{oid}:{signalId}`, 4-hour TTL; the latest read wins, which can only move the start later, the fail-closed direction). The commit reads it. `DecisionReview.planReadOn` is removed. The plan route takes the stamp as an optional `[FromServices]` parameter and stamps best-effort after the plan resolves; a stamp that cannot be written or read means "the review started now".
- Validation (step 1) applies the same rule. A Signal closed by the system before the review started is a 409.

**BudgetRevision trigger (coordinator point 1).**
- `DecisionCommitFollowUps.AfterRecordedAsync` runs after step 6. `AfterFailedCommitAsync` runs on the failure path for each `sprk_budgetrevision` a `revise-budget` step wrote.
- Each calls `SignalEvaluationTrigger.EnqueueAsync(BudgetRevision, "sprk_matter", matter, revision)` with no delay.
- A false return is a Warning log plus the result warning `budget_revision_trigger_not_queued`. The nightly pass is the backstop.

**Suppression (034 hand-off).**
- After a Dismissed commit, each dismissed Signal's grain is passed to `SignalSuppressionService.ApplyAsync(policyId, policyCode, grain)`. The grain is (policy, core) or (policy, item from the dedupe key), as 034 keys it.
- A failure is non-fatal: a Warning log, the metric `decision_commit_suppression_failed` and the result warning `suppression_not_applied`.
- **Nothing from `ApplyAsync` reaches the caller (uac-r2 on 034).** Its status and counts are computed app-only over every Signal of the grain, Secure-team rows the caller cannot read included, so the route discards them: the response carries only the fixed warning above, never a count or a suppressed state. Test: `SuppressionOverSignalsTheCallerCannotRead_ReachesTheCallerAsNothing_NoStatusNoCount`.

**Second review (deb4bbbc6) and uac-r2's review of #1665 (CHANGES REQUESTED).**
- **F1 (both reviewers): the stored-record repeat is now a full check.** The record `sprk_steps` stores the review's content hash (`reviewHash`, right after `reviewId`; `DecisionRecordRequest.ReviewHash`). A record found by (caller, review id) answers a repeat only when its stored hash equals this request's and its stored `resolvedSignalIds` are exactly this request's Signals; otherwise `review_changed` and nothing is closed. A record that cannot prove either (no hash, unreadable envelope) answers nothing. Before any close the Signals are re-read and `PrepareAsync` and FR-51 (`CallerMayDecideAsync`) run as for a first commit; a Signal already closed with that record is not a conflict. The resolution and dismissal text come from the RECORD (its taken steps, follow-ons and `sprk_reason`), never the body. Tests: `F1_AStoredRecordFoundByItsReviewId_NeverClosesOtherSignals_UnderIt`, `F1_AStoredRecordForTheSameSignals_ButADifferentReview_OrWithoutAStoredHash_ClosesNothing`, `F1_AStoredRecordRepeat_AsksFr51Again_AndARevokedRightClosesNothing`, `WhenTheLedgerHasForgotten_TheRecordItselfAnswersTheRepeat` (now a real round trip).
- **F4: a landed revision is queued on every failure path.** Both record-write failures (writer refusal, create fault) and the unexpected fault after the first write call `AfterFailedCommitAsync` (once; a flag stops a second call after the follow-ups ran). Tests: `F4_ARevisionThatLanded_BeforeTheRecordCreateFaulted_StillQueuesItsTrigger`, `F4_AFaultAfterTheFirstWrite_StillQueuesTheTriggerForALandedRevision`.
- **K1: one review per Signal at a time.** Besides the review-id lock, a lock `decision-signal:{tid}:{signalId}` is taken for every covered Signal (sorted by id), released in the same finally. A Signal held by another review is 409 `review_in_progress` and nothing runs. Test: `K1_ASecondReviewIdOnTheSameSignal_...`.
- **K3:** see the review start above. Test: `K3_AResumeAfterTheReviewWindow_...`.
- **K5: a Recorded replay asks FR-51 again** before it repairs a close. Stated behaviour: the stored outcome is returned (200, `replayed`) with each still-open Signal listed `Unchanged` and the warning `signal_close_not_authorized` (or `signal_close_not_checked` when a read or the metadata lookup failed); nothing is closed and the stored outcome is not overwritten, so a later repeat with the rights restored finishes the close. Test: `K5_ARecordedReplay_AsksFr51Again_...`.
- **K6: replay and stored-record closes run suppression** for the Signals they dismissed (`AfterRecordedAsync` with no steps, so no revision trigger); the result is discarded as on the first run. Tests: `K6_AStoredRecordRepeatThatDismisses_...`, `K6_ARecordedReplayThatDismisses_...`.
- **K7: a lock is never stranded by a cancellation.** Locks are taken with `CancellationToken.None` inside the try/finally; the request token is checked once each is held, so a cancellation releases it. Test: `K7_ACancellationWhileTheLockIsTaken_...`.
- **uac-r2 F4-a: no existence oracle.** The no-core item's owner is read app-only only AFTER FR-51, so a caller without Write on the subject gets the same 403 whether or not the row exists or its owner resolves. Test: `UacF4a_ACallerWhoCannotWriteANoCoreSubject_...` (both cases; the owner row is never read).
- **uac-r2 F4-b: faults are 503, not 422.** The core-type and item-owner reads return "not found or unsupported" (a missing row, `0x80040217`, a team owner, a non-core type) as 422 and a thrown fault as 503 `Unavailable`, per the agreed mapping. Tests: `UacF4b_ACoreTypeReadFault_Is503_AMissingCoreType_Is422`, `UacF4b_AnItemOwnerReadFault_Is503_ATeamOwner_Is422`.
- **uac-r2 K4: no raw Dataverse text to the caller.** The inquiry adapter (`BudgetInquiryActionExecutor.ToOutcome`) returns a fixed message per outcome ending in the refusal code; 070's error text is logged server-side only. Test: `InquiryAdapter_NeverPassesTheRawErrorText_..._UacK4`.
- **uac-r2 NIT:** the census HandlerDecision now says the item owner comes from the Signal's subject row, read after FR-51.
- **uac-r2 K2** (`CallerRecordAccessProbe` answers 403 on a transient WhoAmI/OBO fault) is uac-r2's to fix; their probe is unchanged here.

**EventIds.** 50360-50364 are used, from the 50360-50369 range the coordinator assigned.

## 032 and 034 status

**Both have merged and are merged into this branch.** `DecisionCommitFollowUps` calls 032's `SignalEvaluationTrigger.EnqueueAsync(SignalTriggerKind, string, Guid, Guid, string?, CancellationToken)` and 034's `SignalSuppressionService.ApplyAsync(Guid, string, SuppressionGrain, CancellationToken)` (with `SuppressionGrain.ForCore` / `ForItem`) unchanged.

034 (#1654) landed on the project branch as `02ba5f29b` and was merged here on 2026-10-12. The three additive conflicts (both usings in `DecisionPlanEndpoints.cs`; the 036, 043 and 034 HandlerDecisions in the census Ledger with line references refreshed; `defer-issues.md` merged cleanly) are resolved in that merge. After it: the Signals, decision, inquiry, suppression and secure-child filter passed 1651/1651, and the full ArchTests built from `C:\wts-043` passed 958/958.

## uac-r2 coordination (re-read 2026-10-11, origin/master 89480bff8)

| File | Last master commit | Changed vs this branch? |
|---|---|---|
| `Services/Access/SecureChildLineage.cs` | 0615f9781 (task 039, #1355) | Only the ontology project's own 044 addition (`sprk_budgetrevision`), which is not on master |
| `config/secure-record-owner-role.json` | 0615f9781 | Same: the 044 addition only |
| `RecordOwnershipResolver.cs` | d254d7166 (#1312) | No |
| `RecordRouteAccessAuthorizationFilter.cs` | c5f71487f (#1411) | No |
| `CallerRecordAccessProbe.cs` | c5f71487f (#1411) | No |
| `RouteAuthorizationGuardTests.Ledger.cs` | d5e1dffe7 (#1527) | No uac-r2 commit since the branch base; this task adds three entries |
| `.claude/adr/ADR-034-user-record-membership.md` | 97d6e0be3 | No |
| `ExternalCallerContext.cs` | d254d7166 | No |
| `CoreAncestorResolver.cs` | d7fdcafc3 (#1458) | No |

- The open uac-r2 PR #1657 (form-script auth) does not touch these files.
- **Call site for uac-r2:** `DecisionCommitService.RecordRequest` builds the `DecisionRecordRequest` (core from `Prepared.RecordCore`, owner from `Prepared.ItemOwnerUserId`, both derived in `PrepareAsync`). `CommitCoreAsync` calls `DecisionRecordWriter.WriteAsync` at step 5.
- **Pair rule.**
  - A decision's To Do (a non-WA create) went through the real `OwnedChildWrite.PairTargetAsync` with the typed pair and got 201, not 400.
  - Assign Work binds `sprk_RegardingMatter` (or `sprk_RegardingProject`) as well as the pair, and also got 201.

## Deviations

- **The step-7 Also-resolve candidate query is not part of this route.** The wizard proposes the candidates (task 058). This route only enforces the rule.
- **Explicit `retry` resume.** AC4 (a repeat returns the first outcome) conflicted with the coordinator's retry case. Both hold: a repeat without `retry` replays, and `retry: true` resumes a resumable failure.
- **The authorization check moved from the handler into `CommitAsync`.** This keeps the census at two hops, and it means no ledger replay happens for a Signal the caller cannot read.
- **A dismissal's stored text.** It is `"code - detail"`, or `"code - Label"` when the reason needs no detail (for example `not-material - Not material`).

## Owner questions

1. **Inquiry-only (D-119).**
   - It is represented as `sprk_signalstatus = Acknowledged` with `sprk_decisionrecord` linked, and the item stays open. Is Acknowledged the right state, or should status stay Open with only the link?
   - Note that a later commit that closes the item overwrites `sprk_decisionrecord` with the closing record. The inquiry's record is still found through its own `sprk_steps` and resolved ids.
2. **A review with only Next steps and no taken action is non-resolving** (the same as inquiry-only: Acknowledged plus the link). Confirm, or should a Next step resolve?
3. **A send that went out and then the record failed** (escalation trigger 1).
   - The route stops and logs Critical (`SentButNotRecorded`, 50361). The response says `sentButNotRecorded: true` and the outcome is not resumable.
   - What should the owner-facing recovery be?
4. **`DeciderRole` is null.** No role source is wired. Is it needed in R1?
5. **A no-core item owned by a team is refused** (`item_owner_unsupported`), because the record writer owns by systemuser. Is that acceptable for R1?
6. **`send-reminder` resolves the Signal** (only the inquiry is non-resolving under D-119). Confirm.
7. **FR-51 strictening.** The route now requires the caller's Write on each Signal's subject and AppendTo on the core record, even for a dismissal. A user who can read an item but cannot write its subject can no longer dismiss it. Confirm this is the intended rule (spec FR-51), or narrow it to the POML's rule (Signal read + executor preflights).

## Known limits (K-class)

- **With Redis disabled the review and Signal locks are process-local** (`ProcessLocalScheduledJobLease`): two instances could each take them. Production runs with Redis.
- **A Signal lock outlives a commit longer than 10 minutes** (the lock TTL); a commit that slow could overlap a second review of the same Signal. The executors' own timeouts are far shorter.
- **A crash between a write and the outcome store.** The 10-minute marker expires, and the record lookup finds a written record. A crash before step 5 leaves landed writes that a repeat would run again.
- **The outcome is kept 7 days.** After that, only a written record answers a repeat.
- **A `send-email` Next step whose communication row was not saved after the send** has no id. It is recorded in `sprk_followons` as a follow-on without an id (`{"actionCode":"send-email","entity":null,"id":null}`), and "nothing taken" is decided from the Next steps that actually ran, so the record is Judgement, not a refused dismissal (review fix F-4, coordinator item 4).
- **K3: the durable lookup has not run against Dataverse.** `sprk_steps LIKE %"reviewId":"<id>"%` on a memo column is pinned only against the writer's serialized text in unit tests.
- **A crash during a resume.** The resumed failure lives in the 10-minute in-progress marker; if the process dies mid-resume and the marker expires, the failure is forgotten and a later retry runs from the start (the durable record lookup still prevents a second record).
- **Also resolve is capped at 25 items**; a longer list is `review_invalid`.
- **A decision-created work assignment has the business-unit search-index cascade issue DI-60 (#1607).** This is the same as the wizard and already filed.

## Tests and why each exists

**`tests/integration/data-mutation/Signals/DecisionCommitServiceTests.cs`** runs the real service, access check, plan service, record writer (over the SDK seam) and tenant-cache ledger, with the executors scripted at their interface:

| Test | What it proves |
|---|---|
| `PathB_...` | AC1 |
| `TheExecutors_GetTheServerDerivedCore_...` | The executors receive the server-derived typed pair (#1634) |
| `TakingNothingWithNotMaterial_...` | AC2 |
| Dismissal Theory, `OtherWithDetail_...` | AC7 |
| `AFailureAtEachStepBeforeTheRecord_...` (steps 2, 3 and 4), `AFailureWritingTheRecord_...` (step 5), `AnExecutorThatThrows_...` | AC3 |
| `ARevisionThatLanded_BeforeAFailure_...` | The coordinator's failure-path trigger |
| `TheSameReviewIdTwice_...`, `...AfterAFailure_...`, `WhenTheLedgerHasForgotten_...`, `TheRecordLookup_...` | AC4, including the durable lookup and its pattern matching what the writer stores |
| `F1_...` (x3), `F4_...` (x2), `K1_...`, `K3_...`, `K5_...`, `K6_...` (x2), `K7_...`, `UacF4a_...` (Theory), `UacF4b_...` (x2) | Second review and uac-r2 review of #1665 (above); each fails with its fix reverted |
| `AReviewInProgress_...` | The 409 |
| `ARefusedReview_CanBeCorrected...` | A refusal is not an outcome |
| `AlsoResolve_...` (x2), `ADoItem_NeverResolvesAnother` | AC5 |
| `ACallerWhoCannotReadTheMatter_...`, `AnAlsoResolveSignal...Cannot Read`, `ACallerWhoCannotWrite...`, `AnUnresolvedCaller_...` | AC6 |
| `AnActionTheCatalogDoesNotKnow_...`, `AReviewThatDoesNotListThePlanExactly_...`, `TwoActionsThatExcludeEachOther_...`, `AMissingRequiredValue_...`, `AnUnconfirmedReview_...` | AC7 and the C-3 refusals |
| `ASignalAPersonAlreadyResolved_...` | The conflict |
| `D119_...` (x3) | Run through `DecisionCommitEndpoints.CommitDecisionAsync` |
| `AClientSuppliedCoreOrOwner_IsIgnored_...` | uac-r2 binding |
| `ANoCoreDoItem_...` | The D-39 owner read app-only |
| Step-6 re-close / person-conflict / before-review-start | Coordinator point 2 |
| Retry-after-system-closure and `ARetryThatChangesTheReview_IsRefused` | Coordinator point 3 |
| HTTP mapping Theory, failure extensions, uniform 404 | The endpoint contract |
| `AStepThatFailedAfterWritingPartOfItsWork_IsNotResumable_...` | Review: a partial step is never written twice |
| `ARetryThatIsRefused_KeepsTheStoredFailure_...` | Review F-1 |
| `ARetry_JudgesASystemClosure_AgainstTheReviewsOwnStart_...` | Review F-2 |
| `ACancelledRequest_AfterTheFirstWrite_...`, `ACancelledRequest_BeforeAnyWrite_ReleasesTheReviewId` | Review F-3 |
| `ANotificationThatNamedNoRow_IsStillRecorded_...` | Review F-4 |
| `ACallerWhoCannotAppendToTheMatter_...`, `ACallerWhoCannotWriteTheSubject_...`, `ASubjectWhoseTableCannotBeLookedUp_IsUnavailable_...` | Review F-5 (FR-51) |
| `AReadFaultBeforeAnyWrite_IsUnavailable_...`, `AReadFaultDuringAResume_PutsTheResumedFailureBack` | Re-check: a pre-write fault is not a stored failure |
| `AlsoResolve_IsCapped` | Review K2 |

**`DecisionCommitFollowUpsTests.cs`** covers the trigger arguments, the failure path, no-revision, a trigger that returns false, suppression running, suppression failing, a policy without a code, the grain keying and the revision filter. These are the 032 and 034 hand-off contracts.

**`SecureChildOwnershipAiToolTests.DecisionCommit.cs`** runs the real executors through the real `OwnedChildWrite.PairTargetAsync`: a To Do with the typed pair, Assign Work with the typed lookup plus the pair, and a refusal under a secure matter the caller cannot append to. This covers the #1634 rule and the uac-r2 pair rule.

**`tests/unit/domain/Signals/DecisionGateTierTests.cs`** covers AC9: every action declares a gate, the tier for each action, under-declared factors are raised, the strictest tier, no AI-internal constructor dependency, and only the inquiry is non-resolving.

**`DecisionActionExecutorsTests.cs` additions:**
- Assign Work: the binds, the matter/project-only refusal and the privilege name.
- The inquiry adapter: its mapping and refusals.
- Every catalog code has an executor.

**AC1 note.** "The revision and the To Do exist" is proven at the executor seam: their real writes are pinned in `DecisionActionExecutorsTests` and in the harness partial. The commit tests prove order, record content and Signal state. This split keeps the commit tests independent of every executor's Dataverse script.

**Mutation check (local).**
- Moving the inquiry first fails AC1 and AC3.
- Disabling the system-closure rule fails the re-close and retry tests.
- Review fixes: disabling each fix (F-1 put-back, F-2 stored start, F-3 detached token and the cancellation release, F-4 unnamed Next steps, F-5 rights check, K2 cap) fails exactly its new test(s); all nine new tests failed under the mutations and pass on the fix.
- Second review and uac-r2 round: reverting each of F1 (record covers the review), F4 (both failure paths), K1, K3, K5, K6, K7, uac F4-b (core type and item owner) and uac K4 one at a time fails its own new test(s); all pass on the fix.

**`DecisionRecordWriterTests.cs` additions:** an unnamed Next step that is not a Next-step creator is refused; an unnamed `send-email` makes a Judgement record listed under `unnamedNextSteps`, and the `"reviewId":"<id>"` lookup text is still present.

**Coordinator-round tests (review at e48399548):** `OnlyARefusedStep_IsResumable_...` and the `AnExecutorThatThrows_...` resumable assertion (item 2); `ASecondConcurrentCommitOfTheSameReview_IsInProgress_...` and `ALockStoreThatCannotBeReached_FailsClosed` (item 3); `ANotificationThatNamedNoRow_...` (item 4, a follow-on without an id); `TheRecordOwner_IsResolvedBeforeTheFirstWrite_...` (item 5); the cancellation tests (item 6); `ARecordedReplay_ClosesASignalTheFirstRunCouldNotClose` and `WhenTheLedgerHasForgotten_...` (item 7, both replay paths); `AlsoResolve_IsCapped` (item 9, `review_invalid`); `TheSameReviewIdForADifferentDecision_OrADifferentSignal_IsReviewChanged` (item 10); `ACorePairThatChanged...` (K2); `WithoutAServerPlanReadStamp_...`, `AServerPlanReadStamp_...`, `APlanReadStamp_IsTheServersOwn_AndExpires` (K4). A combined mutation of items 2, 3, 5, 7, 10, K2 and K4 failed all ten of their tests.

**Also fixed in scope:** `DecisionActionCatalogTests.SendBudgetInquiry_HasNoReplyDueParameter_D56_D20` asserted the inquiry's parameters were exactly to/subject/body; this task's optional `outsideFirm` broke it. The assertion now lists `outsideFirm` (optional) and still has no reply-due parameter.
