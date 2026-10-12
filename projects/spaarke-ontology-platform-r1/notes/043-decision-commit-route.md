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
- **FR-51 (review fix F-5).** After the plan is validated and before any executor preflight, the route asks, as the caller through `CallerRecordAccessProbe`, for AppendTo on the core record (`entity.associate_document`) and Write on each covered Signal's subject (`write`, on the table `IGenericEntityService.GetEntitySetNameAsync` names). A missing right is a 403 `ontology.decision.not_authorized` before any write. A subject whose table cannot be resolved is denied. This applies to a dismissal or a record-only approval too, which no executor preflight would otherwise check. **This is stricter than the POML** (which named only the Signal read and each executor's preflight); it follows the spec's FR-51. See owner question 7.
- When an executor preflight refuses with NotAuthorized, the result is a 403 that names the action.
- This placement also keeps the route-census chain at two hops: handler → `DecisionCommitService.CommitAsync` → `SignalCoreRecordAccess.AuthorizeAsync` → `IDataverseUserClient`.

**Server-derived facts (uac-r2 binding condition).**
- The core record comes from the Signal's own `sprk_corerecordtype` and `sprk_corerecordid`, read app-only. Its table comes from `sprk_recordtype_ref`, and it must be a `CoreAncestorResolver.CoreRecordEntities` type.
- A no-core item's owner is read app-only from the item row and must be a systemuser.
- `DecisionReview` has no core or owner member, so the JSON binder drops anything the body names (tested).

**Idempotency.**
- The ledger is ADR-009 `ITenantCache`: resource `decision-review`, id `{oid}:{reviewId}`, version 1.
  - It holds an in-progress marker for 10 minutes; a concurrent repeat gets 409.
  - It keeps the first outcome (Recorded or Failed) for 7 days.
  - A resume keeps the failure it resumes inside its in-progress marker. If the resume ends without a write outcome (refused, forbidden, unavailable, cancelled), that failure is put back, so the next retry still skips what landed (review fix F-1).
  - A refusal is not cached, so the same id can be corrected and sent again.
- When the cache has nothing, the record itself answers the repeat: `sprk_confirmedby = caller` and `sprk_steps LIKE %"reviewId":"<id>"%`.
- A cache that cannot be read fails closed (503).

**Retry (coordinator point 3).**
- `retry: true` with the same content hash resumes a Failed outcome marked `Resumable`. Steps that landed (Done) are skipped, and a record a timed-out create left behind is found and reused.
- Without the flag, the first outcome is replayed (AC4).
- A Failed outcome is not resumable if a message step failed (it may have gone out) or if a step got no clear answer.
- A record-write failure after a send is not resumable (escalation trigger 1).
- A step that failed after writing part of its work (non-empty `written`) is not resumable, so a retry cannot write it twice.

**Cancellation (review fix F-3).** The request's token applies only up to the first write. From then on the executors, the record write, the closes and the follow-ups run on `CancellationToken.None`, so a client that disconnects after a send still gets its record or a stored failure. A cancellation before the first write frees the review id (or puts back a resumed failure) and rethrows. A record create that throws something other than the writer's refusal (for example a client-side timeout) becomes a `create_failed` failure that may have written the record.

**Step 6 against a system closure (coordinator point 2).**
- Each Signal is re-read and updated with `UpdateIfRowVersionMatchesAsync`. On a version mismatch it is re-read once and the decision is made again.
- A Signal Resolved with a system resolution (ConditionCleared, Superseded or PolicyRetired) and `sprk_resolvedon` on or after the review start is re-closed. It gets the commit's resolution (Acted or Dismissed), resolved-on, resolved-by and `sprk_decisionrecord` = the new record. The Signal result carries the warning `reclosed_after_system_resolution`.
- A Signal a person resolved is left alone, with the warning `resolved_by_another_decision`.
- The review start is:
  - on a resume, the first attempt's bounded review start, stored in the ledger entry (review fix F-2; it used to be the first attempt's "now");
  - otherwise the client's `planReadOn`, bounded to the last 24 hours and never later than now;
  - otherwise now.
- Validation (step 1) applies the same rule. A Signal closed by the system before the review started is a 409.

**BudgetRevision trigger (coordinator point 1).**
- `DecisionCommitFollowUps.AfterRecordedAsync` runs after step 6. `AfterFailedCommitAsync` runs on the failure path for each `sprk_budgetrevision` a `revise-budget` step wrote.
- Each calls `SignalEvaluationTrigger.EnqueueAsync(BudgetRevision, "sprk_matter", matter, revision)` with no delay.
- A false return is a Warning log plus the result warning `budget_revision_trigger_not_queued`. The nightly pass is the backstop.

**Suppression (034 hand-off).**
- After a Dismissed commit, each dismissed Signal's grain is passed to `SignalSuppressionService.ApplyAsync(policyId, policyCode, grain)`. The grain is (policy, core) or (policy, item from the dedupe key), as 034 keys it.
- A failure is non-fatal: a Warning log, the metric `decision_commit_suppression_failed` and the result warning `suppression_not_applied`.

**EventIds.** 50360-50364 are used, from the 50360-50369 range the coordinator assigned.

## 032 and 034 status

**Neither had merged into `docs/ontology-platform-design` when this was built.** `DecisionCommitFollowUps` is built against their branch APIs:
- `origin/task/ontology-032-event-triggers` (#1660): `SignalEvaluationTrigger(JobSubmissionService, ILogger)` and `virtual Task<bool> EnqueueAsync(SignalTriggerKind, string, Guid, Guid, string?, CancellationToken)`.
- `origin/task/ontology-034-suppression` (#1654): `SignalSuppressionService.ApplyAsync(Guid, string, SuppressionGrain, CancellationToken)`, `SuppressionGrain.ForCore` and `SuppressionGrain.ForItem`.

**Effect on this branch:**
- The BFF on this branch alone does not compile until both are merged. `SignalsModule` registers both as singletons on their branches.
- The tests and ArchTests were run in a local integration tree: this branch plus 032 plus 034's service file and its registration. That tree was never pushed.
- Once both merge, merge the project branch and re-run.

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

- **The in-progress marker is a get-then-set on the tenant cache, not atomic.** Two truly simultaneous requests can both pass; the durable record lookup still prevents a second record after the first one is written. ITenantCache has no set-if-absent.
- **A crash between a write and the outcome store.** The 10-minute marker expires, and the record lookup finds a written record. A crash before step 5 leaves landed writes that a repeat would run again.
- **The outcome is kept 7 days.** After that, only a written record answers a repeat.
- **A `send-email` Next step's communication id is not tracked by its core.** That follow-on is listed under `unnamedNextSteps` in the `sprk_steps` envelope (and counts as something done, so the record is Judgement, not a refused dismissal; review fix F-4), but not in `sprk_followons`.
- **Replay ignores the Signal id.** A repeat of a review id under another Signal id returns the stored outcome (the ledger key is caller + review id). The caller must already be able to read that other Signal, so nothing leaks; the content hash only gates a `retry`.
- **A crash during a resume.** The resumed failure lives in the 10-minute in-progress marker; if the process dies mid-resume and the marker expires, the failure is forgotten and a later retry runs from the start (the durable record lookup still prevents a second record).
- **Also resolve is capped at 25 items** (review K2).
- **A decision-created work assignment has the business-unit search-index cascade issue F-60 (#1607).** This is the same as the wizard and already filed.

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
| `ACallerWhoCannotAppendToTheMatter_...`, `ACallerWhoCannotWriteTheSubject_...`, `ASubjectWhoseTableCannotBeResolved_IsDenied` | Review F-5 (FR-51) |
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

**Also fixed in scope:** `DecisionActionCatalogTests.SendBudgetInquiry_HasNoReplyDueParameter_D56_D20` asserted the inquiry's parameters were exactly to/subject/body; this task's optional `outsideFirm` broke it. The assertion now lists `outsideFirm` (optional) and still has no reply-due parameter.
