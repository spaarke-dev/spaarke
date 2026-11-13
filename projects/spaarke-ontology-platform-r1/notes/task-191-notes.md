# Task 191: failed-send recovery (D-222, D-223, #2779)

Branch `task/ontology-191-failed-send-recovery` from master `5f86c3bbd5`. Rigor: FULL. Model: Opus, effort high.

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

## Signal mechanism and the reachability rule

Chosen: **keep the item reachable, and re-close in step 6**. No hold on the trigger.

- **Order 1** (retry before the trigger): the Signal is open, closed Acted, record linked.
- **Order 2** (the budget-revision trigger closes it ConditionCleared between failure and retry): within the plan-read window
  (4 h, `Bounded`) the existing step-6 rule re-closes a system closure since review start. The Signal ends Acted with the record
  linked, and the warning `reclosed_after_system_resolution` is now worded on the client. Outside the window the retry gets
  Conflict 409 `signal_not_open`, nothing runs, and the client says the item was closed and what landed stands.

Holding the trigger was rejected for three reasons:

- it needs a per-Signal "unfinished review" marker, either a new column (an escalation trigger) or a per-Signal ledger index;
- the evaluator would have to read that marker (evaluator changes, also an escalation trigger);
- the nightly pass would close the Signal anyway.

**Reachability rule.** An unfinished decision is reachable while the wizard that made it stays open. Its loaded item and pinned
review id persist after the Signal closes, so the retry posts the same review id and the server re-closes (in the window) or
refuses with 409.

Known limit: closing the wizard drops the in-memory review id (`DecisionWizard.tsx` `attempts` ref). After that:

- reopening before the trigger fires starts a new review id, which would re-run the landed writes;
- after the trigger fires, the item is closed and cannot be reopened.

The close confirm now says the attempt is unfinished and that what landed stands (it used to say nothing is written). A durable
fix needs a server-side per-Signal pointer to the unfinished review. That is out of scope here, reported.

## Seeding proofs

1. **No duplicate service request.** Replaced the guard in `BudgetInquiryActionExecutor.ExecuteAsync` with
   `if (false && LandedServiceRequest(request) is { } landed)`. Both cases of
   `BudgetInquiryExecutorTests.A_retry_through_the_action_executor_reuses_the_landed_service_request_and_creates_exactly_one`
   failed ("Expected rig.Writes to contain a single item ... but found" two). Restored; they pass.
2. **NoTransientRetryOption.** Replaced `rc => rc.Options.Add(sendOption),` with `null,` in both sends. Both cases of
   `EmailChannelSenderSendOutcomeTests.A5xxOnSendMail_IsSentOnce_AndIsAnUnknownOutcome` failed (the handler re-sent the 503).
   Restored; they pass.

## Publish size (§10)

Fresh master `5f86c3bbd5` (git archive of `src/server`, `config` and root props into `C:\t191m`) against this branch, both
`dotnet publish -c Release`, zipped with the same Python `zipfile` deflate:

| | Files | Zip |
|---|---|---|
| master | 191 | 41,046,789 B |
| branch | 191 | 41,054,476 B |

Delta: **+7,687 B**. No package, endpoint or DI registration was added.

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

## Line-reference drift

The POML cites `ContainerOperations.cs:103-106` and `:180`. They match on `5f86c3bbd5`.

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
