# Task 228 notes: an open inquiry's To Do closes only through its disposition; the disposition closes the Signal within seconds (#1984, #1991 / DI-101)

Line numbers are approximate, at origin/master d43fc9e16d plus this change.

## #1984: the refusal (D-111, ADR-002)

### Where the check lives
`ChildRecordEndpoints.UpdateAsync`, the shared core behind `PATCH /api/v1/child-records/{table}/{id}` and the decision executors that call it
(`MarkCompleteExecutor`). It is the one server-side owner of the invariant "an open inquiry's To Do is not closed except through the
disposition" (ADR-002: no plugin; D-111). The filing routes never reach it, because their table is not `sprk_todo`.

### What it does
1. `InquiryDispositionService.ClosesTodo(body)` decides whether the write closes the To Do: `statecode` 1, or `statuscode` 2 (Completed) or
   659490002 (Dismissed). Only numeric values count. Any other To Do update (a rename, a date, `statecode` 0) is not checked and costs nothing.
2. When the write closes a To Do, the existing row read (the caller's own read, which the route already makes) also selects `sprk_notes` and
   `_sprk_regardingmatter_value`.
3. After the row read succeeds and before the search-index drop or any write, `InquiryTodoCloseRefusalAsync` calls the Worklist's own
   identification, `InquiryDispositionService.FindOpenInquiryOfTodoAsync` (notes hint `serviceRequestId=<guid>` first, then the matter's
   open outbound service requests), as the caller:
   - an open inquiry: 409 `inquiry.disposition_required` (the commit route's reason code, `DecisionCommitReasons.InquiryDispositionRequired`),
     with plain-words detail; logged at Information; nothing written;
   - the lookup failed (any non-success): 503 `child_record.inquiry_check_unavailable`, logged at Warning with the To Do id; nothing written.
     A fault never reads as "no inquiry" (fail closed);
   - no open inquiry: the update goes ahead exactly as before.
4. Dismissal is refused as well as completion: the issue names `statecode=1`, and a dismissed To Do drops out of the Worklist the same way,
   leaving the inquiry with no prompt to record its outcome.

### Client
`TodoDetail` shows `INQUIRY_TODO_CLOSE_MESSAGE` ("This To Do is completed by recording how the budget inquiry turned out. Open it from the
Worklist and record the outcome; that completes the To Do. Nothing was saved.") when Complete or Dismiss is refused with that reason code,
whether the host returns `{ success: false, reasonCode }` (new optional field on `ITodoSaveResult`) or throws an error carrying `reasonCode`
(the shape `ChildRecordWriteError` already has). Any other failure still shows the host's own text.

## #1991 / DI-101: the disposition closes the Signal within seconds (D-12)

- `InquiryDispositionService.RecordDispositionAsync`: when the To Do was completed, it queues
  `SignalEvaluationTrigger.EnqueueAsync(SignalTriggerKind.InquiryDisposition, "sprk_todo", TodoIdFor(inquiry), inquiry)`. The trigger never
  throws (5-second timeout); a failed enqueue is logged by the trigger and the disposition still returns Resolved. Nothing is queued when the
  To Do write failed or the disposition was refused.
- `SignalTriggerKind.InquiryDisposition` (kind name `inquiry-disposition`) is the only kind for which
  `SignalEvaluationTrigger.IncludesDoLane` is true. The handler passes it to `SignalReevaluationJob.EvaluateSubjectAsync(..., includeDoLane)`,
  which then evaluates Do-lane policies for that subject as well as Decide-lane ones. Every other trigger is unchanged (Decide lane only).
- This is the nightly job's own subject path, so the close is the nightly close: ConditionCleared, no `sprk_decisionrecord` on the Signal,
  D-61 (a second run writes nothing).
- The Worklist notice no longer says the row waits for tonight's check: "Outcome recorded and the To Do completed. This item leaves the list
  shortly; refresh to see it go." The widget keeps its per-instance `recordedTodos` map, because the reload right after the record can beat
  the job, and opening the row again must not show a picker for finished work.

### Interpretation to confirm in review (D-145b vs BR-1)
Because the trigger reuses the nightly Do-lane path, it writes D-145b's one **Routine** closure Decision Record for the ConditionCleared
close (record class Routine, naming who changed the To Do). That is the record the nightly run would have written anyway; it is not a BR-1
human-resolution record, and the disposition's own Decision Record (written by the disposition service) is unchanged. No other record is
added. If the owner wants the immediate close to write no Routine record, that would need a flag on the evaluator path; not done.

## Escalation trigger
Not fired: no plugin, no new route, no schema change, and To Dos with no open inquiry complete exactly as before (tested: no inquiry, no hint
and no matter, a closed inquiry, an inbound service request, a non-closing update, a non-To-Do table).

## Known limits
- K: if the enqueue fails (Service Bus down), the Worklist says the item leaves shortly, but the Signal closes only at the next nightly run.
  The failure is logged by `SignalEvaluationTrigger`; the user-visible effect is the pre-#1991 behaviour.
- K: the check adds one or two caller reads (the inquiry, or the matter's open inquiries) to a To Do write that closes the To Do. Writes that
  do not close a To Do are unaffected.

## Found, not fixed (other close paths that bypass the BFF owner)
These close a To Do without going through `ChildRecordEndpoints.UpdateAsync`, so an open inquiry's To Do can still be completed or dismissed
there. Out of this task's scope (the POML names the child-records route and TodoDetail); to be filed:
1. SmartTodo Kanban / LegalWorkspace: `Spaarke.SmartTodo.Components/src/services/TodoDataverseService.ts` `updateTodoStatus` and
   `dismissTodo` (~98, ~109) write `statecode`/`statuscode` directly through `Xrm.WebApi`; used by `src/solutions/SmartTodo/src/hooks/useSmartTodoBridge.ts`
   and `src/solutions/LegalWorkspace/src/hooks/useSmartToDoBridge.ts`.
2. The chat update tool, `Services/Ai/Handlers/DataverseUpdateRecordHandler.cs`, can set a To Do's state.
3. TodoDetail has no in-repo host that passes `reasonCode`; a host that maps the BFF 409 to `{ success:false, error }` without the reason code
   shows its own error text, not the new words.

## §11 answers
- **Existing**: `FindOpenInquiryOfTodoAsync` (the Worklist's identification), `SignalEvaluationTrigger` + `SignalEvaluationTriggerHandler`
  (ADR-004 event-trigger path), `SignalReevaluationJob.EvaluateSubjectAsync` (the nightly subject path), `DecisionCommitReasons.InquiryDispositionRequired`.
- **Extension**: all extended, nothing new: one enum value, one optional parameter, one predicate, constants. No new service, route, DI
  registration or package. `InquiryDispositionService` takes the already-registered singleton `SignalEvaluationTrigger`.
- **Cost of doing nothing**: an open inquiry's To Do can be completed from TodoDetail, leaving the inquiry with no recorded outcome and no
  prompt (#1984); a recorded outcome leaves its Worklist row up until the next night (#1991).

## Tests
- Integration (`tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.InquiryTodoClose.cs`): 409 by hint and by
  matter fallback; dismiss / statuscode-only / statecode-only refused; must-not-fire cases; 503 on lookup and matter-list faults, with log.
- Unit (`InquiryDispositionTests`): enqueue once with the right arguments; none on To Do 404/403 or refused disposition; failed enqueue still Resolved.
- Domain (`SignalReevaluationJobInquiryDispositionTriggerTests.cs`): the trigger closes the Do-lane Signal as ConditionCleared with one Routine
  record; again writes nothing; a still-holding To Do closes nothing; other kinds leave the Do lane alone; only the new kind includes the Do lane.
- Jest: TodoDetail 409 words (resolved, thrown, dismiss, other refusal); Worklist notice wording without "tonight".

rework: none task 228
