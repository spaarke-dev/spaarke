# iss029 — line decision repeated with another decision under the same decisionId (ISS-029, #2880)

Task: `tasks/iss029-line-decision-id-reused.poml`. Rigor FULL. Branch `task/ebilling-iss029` (base `f5ec8b6a20`).

## Before → after

| | Before | After |
|---|---|---|
| Repeat, same `decisionId`, another reason / amount / note / action / decider, line already recorded | Line fields rewritten, totals recalculated, the record refused as other content, Signal close skipped → 503 `ebilling.decision.incomplete` (`failedStep` `record`), which no repeat can ever complete | 409 `ebilling.decision.id_reused`, before any line, total, correction, Signal or record write |
| Multi-line call, one line recorded under the id with other content | Every line rewritten with the new content (the lines disagree), 503 | 409 `id_reused`, no line written |
| Repeat with the same content | Completes what the first call left unfinished | Unchanged |
| First call (no record under the id) | Decides and records | Unchanged |
| Fault reading the recorded decisions | 503 `ebilling.decision.unavailable`, nothing written | Unchanged (the check reuses that read) |

## Design

- **One comparison.** The writer's own "already recorded" decision (`DecisionRecordWriter.AlreadyRecordedOrRefuse` over `BillingDecisionHash`) is applied to the stored record by a new no-I/O entry, `DecisionRecordWriter.PreflightBillingRepeat`, with the same messages (the other-code message was hoisted unchanged to `BillingOtherCodeRefusal`). The recorder exposes it as `BillingDecisionRecorder.PreflightRepeat`. No second definition of equality.
- **Same input as the record step.** The service checks the same `BillingDecision` objects it later passes to `RecordAsync` (built once), so the pre-check refuses exactly the cases the record step would refuse with `ReviewInvalid` — other code, other content, stored content unreadable — and passes exactly the ones it would answer `AlreadyRecorded`. The hash covers the resolved Signal ids; a repeat reuses the stored set, so a true repeat hashes the same.
- **Position.** After the recorder's existing no-I/O preflight and before the correction-row build and the first write (the conditional line update). All three `ReviewInvalid` refusals map to 409 `id_reused` (the detail says which kind); `IsOtherContent` picks the sentence. Only the stored-set read can fault, and it was already a 503 `unavailable` before any write.
- **Another action under the id** (accept, then disallow with the same id) is 409 too: the POML names "another decision". The adjustment path never meets it (its decision ids are namespaced per action).
- **Content that cannot be read** (a record without a stored hash) is 409 too: the record step refuses it on every repeat, so only a new decision id can proceed. Before, it was the same never-completing 503.
- **DEF-056 not made worse.** The check runs after the status and corrections checks, exactly where the stored-set read already ran; a repeat after the invoice leaves In Review is refused as before.

## Root CLAUDE.md §11 — the extended read

No new read, query, endpoint, service, DI registration, package or column. The existing reader the service already calls before any write (`BillingDecisionRecorder.FindStoredResolvedSetsAsync` → `DecisionRecordWriter.FindBillingDecisionResolvedSetsAsync`, one query per call) now returns two more values on its existing `BillingStoredResolvedSet` record: `ActionCode` and `ContentHash`, from `sprk_actioncode` and `sprk_steps`, columns it already reads.

1. **Existing:** `FindBillingDecisionResolvedSetsAsync` reads the record under the decision id for every chosen line, inside the lock and before any write. `AlreadyRecordedOrRefuse` is the writer's comparison.
2. **Extension:** that is what was done: two additive record fields and one no-I/O writer entry that calls the existing comparison. A separate lookup would double the reads and could disagree with the stored set the close uses.
3. **Cost of doing nothing:** a repeat with other content rewrites the line and totals, leaves the line's Signal open, and answers a 503 that never completes. In a multi-line call the lines end up disagreeing with their records (ISS-029).

The writer is ontology-r1-owned (`Services/Signals/**`). The change is additive: `WriteBillingDecisionAsync`, its hash and its messages are byte-identical. ontology-r1 should see it in the PR.

## Fail-before evidence

The new class `tests/integration/data-mutation/Ebilling/InvoiceLineDecisionIdReusedTests.cs` was run with only the new status and code constants in place (no behaviour change), using `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter FullyQualifiedName~InvoiceLineDecisionIdReusedTests -- xUnit.MaxParallelThreads=4`:

```
Failed iss029 AC1: a repeat of a recorded decision id with other content ... is 409 id_reused ...   x8
    [another reason] [another reason (disallow)] [another amount] [another note] [a note added]
    [the note dropped] [another decision (action)] [another decider]
Failed iss029 AC1: a multi-line call where ONE line's recorded decision differs is 409 id_reused, and NO line ... is written
Failed iss029 AC1: a repeat with other content after a strand at 'signals' is 409, and the unedited repeat still completes it
Passed iss029 AC2: ... (5 tests)    Passed iss029 AC3: ... (2 tests)
Failed!  - Failed: 10, Passed: 7, Total: 17
Every failure: "Expected result.Status to be LineDecisionStatus.IdReused ... but found LineDecisionStatus.Incomplete"
  (ebilling.decision.incomplete: "The lines were updated but a later step did not finish. Repeat the same decision ...")
```

The AC2 and AC3 tests pass both before and after, as intended: they guard behaviour that must not change (the same-content repeat, the first call, and the read fault). AC3 cannot fail before because the check reuses the existing read, whose fault was already a 503 with nothing written.

After the change, the class has 18 tests, all passing (one route-rendering test was added after the fail-before run). The updated `InvoiceLineDecisionNoteTests.Repeat_WithAnotherNote_IsRefusedAsOtherContent` (4 cases) asserted the old 503. It failed after the change and was rewritten to the new 409 with no write.

## Test runs (after)

- `FullyQualifiedName~InvoiceLineDecisionIdReusedTests`: 18 passed.
- The affected suites (`InvoiceLineDecision|DecisionRecordWriterTests|BillingDecisionRecorder|InvoiceApproval|InvoiceLineReviewEndpoints|InvoiceAdjustmentService|InvoiceLineAdd|InvoiceRejectReturn|SubmissionOverride`): 1967 passed, 0 failed.
- `dotnet build src/server/api/Sprk.Bff.Api/ --no-incremental`: 0 warnings, 0 errors.

## Known limits / follow-ups (not done here)

- **Client (095f-2f scope):** `src/solutions/InvoiceReview` has no handling for `ebilling.decision.id_reused`. The shipped client mints a new `decisionId` per submit and its Repeat resends the exact request, so it cannot reach the 409. A generic 409 shows the server's detail.
- **No docs under `docs/`** list the line-decision route's answers (grep `ebilling.decision.incomplete` under `docs/` finds nothing). The route description and the service remarks were updated.

## Quality gates (task-execute Step 9.5)

- **code-review** (on the diff, standard depth): no F-class finding. One cleanup was made in a follow-up commit: the redundant local alias `otherCodeRefusal` in `WriteBillingDecisionAsync` was replaced by the hoisted `BillingOtherCodeRefusal`, so the message is byte-identical. Known limits:
  - K4: `ArgumentNullException.ThrowIfNull` on the non-nullable parameters of `PreflightBillingRepeat`. This follows the file's idiom.
  - K2: a colleague cannot finish another reviewer's stranded decision under its id. It is now refused with 409 instead of the old 503 that could never complete, the same as the adjustment path's "another reviewer" rule.
- **adr-check**: no violations.
  - Compliant: ADR-002 (the writer remains the only owner of the content comparison), ADR-008, ADR-010, ADR-015 (the log carries ids and refusal codes only), ADR-019, ADR-038 (new test in the data-mutation KEEP path; the old test was modified, not deleted).
  - N/A: ADR-013.
- **Re-run after the cleanup:** the affected suites (adding `RecordProcedure`) passed 2616 / 2616. BFF build: 0 warnings, 0 errors.
- **Publish-size delta (root §10 bullet 4): not measured in this sub-agent run.** The change is code-only: no package, asset or DI change. The main session measures it at the PR against a fresh master build.

## Fix round 1

Review of 534b87a152. Wording only in `src`; new tests only otherwise. No runtime behaviour change.

1. **F2 - untested "stored content cannot be read -> 409" path.** Added `StoredContentUnreadable_Is409_AndWritesNothing` (Reduce and Disallow cases) in `tests/integration/data-mutation/Ebilling/InvoiceLineDecisionIdReusedTests.cs`. It seeds a writer-created line record with a `reviewId` and no `reviewHash` (`w.H.Seeded`), repeats under that decisionId, and asserts 409 `ebilling.decision.id_reused` with the "cannot be read" wording, `Writes(w)` (write log, row versions, totals writes, closes, records, corrections) and the line status unchanged, and no record created. The "another decision (action)" theory case now also asserts the wording: other-action text and not the other-content text (and the reverse for the other-content cases).
   - **Mutation evidence:** `DecisionRecordWriter.PreflightBillingRepeat` temporarily changed to pass `stored.ContentHash ?? BillingDecisionHash(request)`: the new test failed (2 of 2 cases); the 18 other tests in the class passed. Reverted; the class passes 21 / 21 on the real code.
2. **F2 - route descriptions** (`InvoiceLineReviewEndpoints.cs`). Multi-line: the decision must match every *chosen* line already recorded under that decisionId; lines not in the call are not checked. Single-line: dropped "503 is only for a retryable fault"; it now says a 503 on the lookup of the recorded decision writes nothing and is safe to repeat, and that other 503 codes (for example `ebilling.decision.incomplete`) say which step to repeat or that the step needs a fix first.
3. **K - approver text** (`InvoiceApprovalService.FromLineResult`). `IdReused` now maps to Conflict with "This decision was already recorded with other content; nothing was changed. Reload the invoice." (same code `ebilling.decision.id_reused`). The line route's text ("Use a new decision id") is unchanged. New test `Approve_AcceptCleanIdReused_Is409_WithNeutralText` (fails with the old pass-through text, passes now).

Results: affected suites (`InvoiceLineDecision|DecisionRecordWriterTests|BillingDecisionRecorder|InvoiceApproval|InvoiceLineReviewEndpoints`, MaxParallelThreads=4) 1009 / 1009; BFF build 0 warnings, 0 errors.
