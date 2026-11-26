# Task 191: failed-send recovery (D-222, D-223, #2779)

> **Merged**: PR [#2792](https://github.com/spaarke-dev/spaarke/pull/2792) squash-merged to master as `e5f5c9a6df` (owner: merge on green). Opus implementer, one full Opus review, three fix rounds with scoped re-checks. Not yet on dev (needs a BFF + code page deploy).

Branch `task/ontology-191-failed-send-recovery` from master `5f86c3bbd5`. Rigor: FULL. Model: Opus, effort high.

Rework: **fix-round-1** (coordinator review): rebased onto master `b77967a750` (task 190, clean); F4/F1 reachability and duplicate
guard, F4 Signal (unbounded resume), F1 not-sent status, F2 locked text. Tests after the round: scoped BFF 4,728 passed / 6 skipped;
ArchTests 2,543 passed; ui-components DecisionWizard + Worklist jest 465 passed; client-lint ok; tsc: no errors in changed files
(10 pre-existing unresolved-module errors elsewhere in the package).

Rework: **fix-round-2** (adversarial review). (1) Record without sending is accepted for ANY resumable inquiry failure whose
service request landed, NotSent included; the client offers it beside Retry recording; a resume refused for a reason it will meet
again (Conflict/NotFound/Forbidden/Refused, not-mine) removes the pointer. D-141: when the review's own reassign landed the pointer
is NOT written (released): the author's plan read is the uniform 404 so the item could never offer it; the author's open wizard can
still finish it. (2) `ReadUnfinishedAsync` returns the review for every covered Signal with the primary `SignalId`; the client posts
there. (3) Only a ConditionCleared closure is matched against the unbounded start (`clearedSince`); Superseded/PolicyRetired keep the
4 h window and its `signal_not_open` refusal. (4) signal_not_open on a resume removes the pointer; a released resume re-writes the
pointer with the ledger (same TTL). (5) Transport fault / timeout through the sender: one request, Unknown. Notes moved off the
code branch (this file is an untracked copy).

Rework: **fix-round-3** (scoped re-check of round 2). Rule: the pointer is never removed while landed writes are unrecorded and the
Signal is open, unless the item is gone for good. (F2) On resume, a not-mine answer releases the pointer only when it is NotFound;
Unavailable / CallerUnresolved keep it. (F3, main-session decision) The resume else branch removes the pointer only for
Conflict + signal_not_open (item closed) and NotFound; Refused, Forbidden and every other outcome keep it (renewed with the ledger) and
Refused/Forbidden log an Error naming the review id and the landed writes. The 409 review_unfinished another user sees names who
started it (the token's `name` claim, stored in the pointer, no read; "Another user" without it) and when (UTC). Round-2 Conflict
removal narrowed to signal_not_open (the other Conflict, inquiry_disposition_required, does not mean the item is closed).
(F4) Tests: resume Unavailable keeps; Forbidden keeps; Refused keeps; ConditionCleared closure BEFORE the review start is still
signal_not_open; the refusal text names the author and time. The round-2 sendMail transport-fault/timeout test covers existing
behaviour (NoTransientRetryOption was already on), so it has no seeding proof.

## Design

**Classification (#2779).** `EmailChannelSender` marks both `sendMail` calls (OBO and app-only) with `NoTransientRetryOption`, copying
`ContainerOperations.CreateContainerAsync` (#1755). `GraphHttpMessageHandler` then retries only a 429. `GRAPH_SEND_FAILED` carries
the extension `sendOutcome`:

- `NotSent`: the request never left (token, circuit, refused connect) or got a settled refusal (4xx other than 408, or 429).
- `Unknown`: the request left and got no answer, a 5xx or a 408.

`BudgetInquiryExecutor.DefinitelyNotSent` reads that. It returns true only for:

- a failure before the communication core is called (`InquiryEmailNotSentException`, thrown by `InquiryEmailSender.SendAsync`);
- `GRAPH_SEND_FAILED` with `sendOutcome=NotSent`;
- any other `SdapProblemException` with a 4xx, which are the core's own refusals before the send.

Everything else is `SendOutcomeUnknown`, including the core's catch-all `GRAPH_SEND_FAILED` with no extension, which also wraps
faults after the send. The steps after the send (Dataverse record, archive, participant index, arrived event) are best-effort and
documented never to throw.

**Stored state.** `DecisionCommitService.Failure` makes ONE Failed case resumable. The inquiry step must be Failed with a
`SendFailure`, exactly one written ref (`sprk_servicerequest`) and nothing possibly written. `SendChoiceRequired` is true when the
`SendFailure` is `Unknown`. Both flags live in the existing ledger entry (ITenantCache, 7-day outcome TTL). No schema change.

**Resume.** `ResumedInquiry` finds that step in the stored failure. Its retry passes `DecisionActionRequest.Landed`, and
`BudgetInquiryActionExecutor` then calls `SendOnlyAsync`: no create, the email associated with the landed row. Done steps are
carried forward as before.

**The choice.** `DecisionReview.SendChoice` is `send-again` or `record-without-sending`. It travels on the existing POST, and the
content hash does not include it.

| Request | Answer |
|---|---|
| Retry of a choice-required failure, no choice | The stored failure (`Replayed`, reason `send_choice_required`), returned as 409; nothing runs |
| Choice anywhere else (first attempt, a failure that needs none, unknown value) | 422 `review_invalid` |
| `record-without-sending` | Skips that step's preflight and execute; the step is recorded as `NotSentByChoice` with the landed service request |

**409 for a replay.** `DecisionCommitEndpoints.ToHttp` answers `Failed` + `Replayed` with 409 and the same body (plus
`sendChoiceRequired`). A first-attempt failure stays 500. On the client, `isFailedOutcome` treats a 409 replay as the Failed
outcome, so its flags decide the next try.

## Signal mechanism and the reachability rule (fix round 1)

**Signal choice: an unfinished review is completable regardless of the plan-read window.** `CommitLeasedAsync` keeps a resumed
review's stored start unbounded when the review is unfinished (`IsUnfinished`: Failed, Resumable, at least one landed write). Every
other resume keeps the K3 bound (`K3_AResumeAfterTheReviewWindow...` now uses a resume with nothing landed). Step 6 then re-closes the
budget-revision trigger's ConditionCleared closure with the decision's resolution and record, however long the person took (within
the 7-day outcome TTL).

- **Order 1** (retry before the trigger): the Signal is open; closed Acted with the record linked.
- **Order 2** (the trigger closed it first): re-closed Acted, record linked, warning `reclosed_after_system_resolution`; also days
  later (`AnUnfinishedReview_FinishedAfterThePlanReadWindow_StillReclosesTheSignalTheTriggerClosed_Acted`).

Why not hold the trigger: the close is `SignalReevaluationJob.ApplyAsync` -> `SignalWriter.CloseAsync`, shared by the trigger and the
nightly pass. Holding it means the job reads a per-Signal tenant-cache pointer it has no tenant id for, and skipping a planned close
changes what the job does next (a re-file's new episode is created only when its predecessor closed; the Do-lane routine record of
task 153 follows the close). That is an evaluator change beyond this one Signal, the POML's escalation trigger. Not enqueueing the
trigger on a resumable failure alone would not help: the nightly pass closes the Signal anyway. The window change is one condition
in the route and reuses step 6.

**Reachability rule (criterion 134).** An unfinished review is reachable from its item, by the person who made it, until it is
recorded, stops being resumable, or its outcome expires (7 days):

- `TrackUnfinishedAsync` writes a per-Signal pointer (`decision-unfinished`, same ITenantCache as the ledger, key = Signal id, one
  per covered Signal) when a review's outcome is unfinished, and removes it when the review is recorded (also on a recorded replay)
  or fails in a way that cannot be resumed. The pointer holds the review as posted (without `retry` / `sendChoice`) and the stored
  failure.
- `GET /decision-plan` (existing route, no new endpoint) returns it as `unfinished` to its owner only, after the caller's own
  authorization; another person, an also-resolve Signal or a cache fault gets none (the read never fails on it).
- The wizard reads the plan of every item (it read only open and decided ones), so a Signal the trigger closed is reached from Closed
  today / Closed earlier. With `unfinished` it opens the item editable-but-pinned: the draft rebuilt for display (`draftFromReview`),
  the stored failure shown, the review posted back exactly as stored with `retry` (and the send choice when it needs one).
- **Guard:** `RefuseWhileUnfinishedAsync` refuses a NEW review (no stored outcome under its id) on any covered Signal whose pointer
  names another review or another caller: 409 `ontology.decision.review_unfinished`, nothing runs. A pointer that cannot be read
  refuses with 503 (fail closed, as the ledger read does). An old client therefore cannot write the landed steps twice.

**F1.** `resolutionFromRecord` notes "Decision recorded, inquiry not sent - this item is still open" when a step is `NotSentByChoice`
and the item stays open. **F2.** `LockedNote` reads `lockedNoteText(lockedBy)`: the send choice, "Retry recording finishes it without
repeating what was already carried out", or (not resumable) "Try recording it again". "Finish it later from this item" is said only
when the plan read will offer it (resumable with landed writes).

## Seeding proofs

1. **No duplicate service request.** Replaced the guard in `BudgetInquiryActionExecutor.ExecuteAsync` with
   `if (false && LandedServiceRequest(request) is { } landed)`. Both cases of
   `BudgetInquiryExecutorTests.A_retry_through_the_action_executor_reuses_the_landed_service_request_and_creates_exactly_one`
   failed ("Expected rig.Writes to contain a single item ... but found" two). Restored; they pass.
2. **NoTransientRetryOption.** Replaced `rc => rc.Options.Add(sendOption),` with `null,` in both sends. Both cases of
   `EmailChannelSenderSendOutcomeTests.A5xxOnSendMail_IsSentOnce_AndIsAnUnknownOutcome` failed (the handler re-sent the 503).
   Restored; they pass.

3. **Fix round 1 guard.** Changed `if (resume is null && await RefuseWhileUnfinishedAsync(` to `if (false && resume is null && ...`.
   Three tests failed: `ANewReview_OnASignalWithAnUnfinishedOne_IsRefused409_AndRunsNothing` (a second revision ran),
   `AnotherPerson_DoesNotGetTheUnfinishedReview_AndCannotStartANewOne`, `APointerThatCannotBeRead_RefusesANewReview_503_FailClosed`.
   Restored; 249/249 pass.
4. **Fix round 1 window.** Changed the unfinished-resume condition to `false && ...`.
   `AnUnfinishedReview_FinishedAfterThePlanReadWindow_StillReclosesTheSignalTheTriggerClosed_Acted` failed. Restored; it passes.
5. **Fix round 1 client.** Ran the new `task 191 fix round 1` describe against the pre-round `DecisionWizard.tsx`: the two resume
   tests failed (a new review id was posted), the must-not-fire test passed. Restored; 3/3 pass.

6. **Fix round 2, item 1.** Reverted the choice check to `!first.SendChoiceRequired && review.SendChoice is not null`.
   `ANotSentSendThatKeepsFailing_IsFinishedByRecordWithoutSending_AndTheItemIsReleased` failed (422 review_invalid). Restored; passes.
7. **Fix round 2, item 3.** Widened the `clearedSince` match to any resolution (`Resolution is not null`).
   `ALateResume_DoesNotOverwriteASupersededClosure_ItIsTheWindowsRefusal_AndReleasesTheItem` failed (the Superseded Signal was
   re-closed Acted). Restored; 253/253 pass.

8. **Fix round 3.** Restored the round-2 `DecisionCommitService.cs` (removal on every not-mine answer and on Refused/Forbidden).
   `FixRound3_AResumeWhoseMineCheckIsUnavailable_KeepsThePointer`, both cases of
   `FixRound3_AResumeRefusedOrForbidden_KeepsThePointer_TheItemStaysHeld`, and the two refusal-text tests failed (pointer null;
   old text). The ConditionCleared-before-start test passed on both (it pins existing behaviour). Restored; 269/269 pass.

## Publish size (§10)

Fresh master `5f86c3bbd5` (git archive of `src/server`, `config` and root props into `C:\t191m`) against this branch, both
`dotnet publish -c Release`, zipped with the same Python `zipfile` deflate:

| | Files | Zip |
|---|---|---|
| master | 191 | 41,046,789 B |
| branch | 191 | 41,054,476 B |

Delta: **+7,687 B**. No package, endpoint or DI registration was added.

Fix round 1, against fresh master `b77967a750` (same method, extract in the session scratchpad): master 191 files, 41,057,958 B;
branch 191 files, 41,068,705 B. Delta: **+10,747 B**. Still no package, endpoint or DI registration.

## Component justification (§11)

- **`DecisionReview.SendChoice`** (a field on the existing POST)
  - Existing: `Retry` on the same record.
  - Extension: yes, extended in place.
  - Cost of doing nothing: D-223's choice has no carrier without a new endpoint.
- **`DecisionCommitResult.SendChoiceRequired`**
  - Existing: `Resumable`.
  - Extension: `Resumable` alone cannot say that a retry needs a choice.
  - Cost of doing nothing: an Unknown retry would resend automatically.
- **`BudgetInquiryExecutor.SendOnlyAsync`**
  - Existing: `ExecuteAsync`.
  - Extension: `ExecuteAsync` always creates.
  - Cost of doing nothing: a resume creates a second service request (the D-222 defect).
- **`InquiryEmailNotSentException`**
  - Existing: none (grep for `NotSentException` finds no matches).
  - Cost of doing nothing: a failure before the core cannot be told apart from one after it, so it would be Unknown and ask the
    person needlessly.
- **`DecisionSendFailure`** enum and `DecisionActionOutcome.SendFailure`
  - Existing: `DecisionActionStatus`.
  - Extension: Failed alone does not say whether the email may have left.
  - Cost of doing nothing: the route cannot store NotSent vs Unknown.

- **`decision-unfinished` pointer** (`UnfinishedEntry`, `TrackUnfinishedAsync`, `RefuseWhileUnfinishedAsync`; fix round 1)
  - Existing: the review ledger (`decision-review`, keyed caller + review id) and `DecisionPlanReadStamp` on the same cache.
  - Extension: one more key family on that cache; the ledger key cannot be found from a Signal id.
  - Cost of doing nothing: reopening the item starts a new review that writes a second revision and service request (criterion 134).
- **`DecisionPlanResponse.Unfinished` / `UnfinishedDecision` / `ReadUnfinishedAsync`**
  - Existing: the decision-plan read the wizard already makes on open.
  - Extension: yes, an optional field on that response; no new endpoint.
  - Cost of doing nothing: the client has no way to learn the review id it must resume.
- **`DecisionCommitReasons.ReviewUnfinished`**
  - Existing: `SignalNotOpen`, `ReviewInProgress`.
  - Extension: neither says that another review must be finished first.
  - Cost of doing nothing: the guard's refusal could not be told apart or worded on the client.
- **Client `draftFromReview`, `lockedNoteText`, context `lockedBy`**
  - Existing: `buildReview` (draft to review), the fixed `LockedNote` text, `locked`.
  - Extension: `draftFromReview` is its inverse for display only; `LockedNote` now takes its text from the pinning failure.
  - Cost of doing nothing: a resumed review shows empty steps, and the locked text tells the person to do something the item does not offer (F2).

## Line-reference drift

The POML cites `ContainerOperations.cs:103-106` and `:180`. They match on `5f86c3bbd5`.

## Known limits (fix round 1)

- The pointer holds the review as posted (recipients, subject, body) in the tenant cache for up to 7 days; it is removed when the
  review is recorded. Same store and TTL as the ledger outcome (ADR-009).
- After the 7-day outcome TTL both the pointer and the stored failure are gone: a later review is a new review (unchanged from
  before this task).
- A reopened unfinished review shows picked Lookup / Person values by id (their names were not stored).
- Another person cannot decide the item while someone's review on it is unfinished (409 naming that); the owner finishes it.
- Fix round 2, K (failed pointer write fails open): if the pointer write throws (logged), a new review on the Signal is not refused.
  Failing closed would need the guard to find the ledger entry, but the ledger is keyed by caller and review id and cannot be found
  from a Signal id; a Signal-keyed index is the pointer itself. The window is a cache fault at the moment a write failed.
- Fix round 3 supersedes the round-2 K on 403: a Refused or Forbidden resume now KEEPS the pointer, so the item stays held (409
  naming the author and start time) until the author finishes it or the 7-day TTL ends. Accepted on purpose: a blocked item with a
  clear explanation beats a silent second budget revision.
- K (D-141, stays): the new owner of a reassigned Do review can repeat earlier landed steps. Scenario: Bob's Do review reassigns the
  To Do to Alice (lands), adds a To Do (lands), then a later step fails. The review's own reassign landed, so no pointer is written
  (Bob's plan read is the uniform 404, the item could never offer it). Alice opens the item, starts a new review and adds a To Do
  again: two To Dos. Bob's open wizard can still finish his review; nothing else stops Alice's repeat.
- K: a pointer stored before fix round 3 has no author or start; its refusal says "Another user started a decision on this item
  that is unfinished" without a time (expires within 7 days).

## Out of scope (reported, not fixed)

Other `sendMail` callers without `NoTransientRetryOption`, the same #2779 class: Graph re-sends them after a 5xx, 408 or
transport fault.

- `Services/Ai/Nodes/SendEmailNodeExecutor.cs:237`: a playbook email node can send twice.
- `Services/Communication/MailboxVerificationService.cs:185`: a verification email can arrive twice.

The first is in `Services/Ai` (D-66) and the second is outside the ontology critical path (D-106). A source-scan guard
("every `SendMail.PostAsync` carries the option", with a ratchet of those two) would enforce the class.

## Test scope beyond the criteria

- `DefinitelyNotSent` theory: one row per classification branch.
- A NotSent retry that fails NotSent again stays resumable.
- The re-close warning and the `signal_not_open` text on the client.
