# iss050 — the line-decision lock is renewed during a long call (ISS-050, #3059)

Task: `tasks/iss050-line-decision-lock-renewal.poml`. Rigor FULL. Branch `task/ebilling-iss050` (base `b4746e24df`).

## Before → after

| | Before | After |
|---|---|---|
| A line-decision call that runs longer than the 2-minute lock | The lock expires mid-call; a second reviewer can take it and change a line this call has written but not yet recorded; this call then records "accept" on a line that shows Reduced | The call renews the lock (owner-checked) at checkpoints through the whole call (after each pre-write read or check, before each owner question, and before every write), so the lock does not lapse between checkpoints |
| A renewal answers false or throws (the lock is lost) | Not detected | The call writes nothing more. After its line write: 503 `ebilling.decision.incomplete` with `failedStep` (`totals`, `corrections`, `record`, or `signals` when only the last line's close is left) and the committed line ids. Before its line write: 503 `ebilling.decision.unavailable` ("nothing was changed"). Never 409, never continued unlocked. The same decision repeated completes it under a new lock |
| Approve's Accept all Clean (and its completion of an earlier bulk accept) under the lock the approval holds | Never renewed | Renews the approval's line lock with the approval's owner id at `InvoiceApprovalService.LockDuration` (5 minutes). The first checkpoint always renews, because the approval's time under the lock is unknown to the line service. A lost lock answers the approval's existing 503s: `Incomplete` at `accept-clean` (code `ebilling.decision.incomplete`), or `Unavailable` before the bulk accept's line write. The claimant's repeat approves |
| A short call | Completes | Completes exactly as before. It makes no renewal call |
| ISS-029 `ebilling.decision.id_reused`, and every other answer | — | Unchanged. Release is still owner-checked (`ReleaseProcessingLockAsync(key, owner)`) |

## What changed

- `Services/Finance/InvoiceLineDecisionService.cs`
  - A private `LockLease` (key, owner, duration, last renewed) and a `KeepLockAsync` checkpoint. The checkpoint calls `IIdempotencyService.RenewProcessingLockAsync(key, owner, duration)` only once `LockRenewInterval` has passed. A false answer or a throw means the lock is lost and is logged as `lock-lost`.
  - Checkpoints in `ApplyAsync` run before:
    - the conditional line write;
    - the totals recalculation;
    - each correction chunk;
    - each line's Decision Record;
    - each line's Signal close (only when the line has Signals).
  - A lost lock stops the record loop at once. This differs from a failed record, after which the other lines are still attempted.
  - `RunAsync` reads the clock before it acquires the lock and passes the lease to `ApplyAsync`.
  - `AcceptCleanHeldAsync` and `CompleteBulkAcceptHeldAsync` take `lockOwner` and `lockDuration` from the caller. Their lease starts "never renewed", so the first checkpoint renews it.
  - The class remarks have a new "Lock renewal (ISS-050)" paragraph.
  - Fix round 1 (F1): checkpoints before any write, each answering 503 `unavailable` ("nothing was changed") on a loss:
    - after the paged line read;
    - after `RefuseUnappliedAsync`, `ResolveSignalSetsAsync`, `AuthorizeCloseAsync` and the firm gate;
    - before each owner question in `StampOwnerAsync`, which now takes the lease.
  - Fix round 1: `WriteUnreviewedAsync` takes `lockOwner` and `lockDuration`, passes its lease to `StampOwnerAsync`, and checks the lock before each chunk. A loss answers Unavailable before its first chunk, or Incomplete at `unreviewed` after it.
- `Services/Finance/InvoiceApprovalService.cs`
  - The approval's owner id is passed down: `ApproveHeldAsync` → `CompleteEarlierBulkAcceptAsync` / `AcceptCleanHeldAsync` / `WriteUnreviewedAsync` with `LockDuration`. The class remarks and the step comment are updated.
  - Fix round 1 (F1-low): an Unavailable line answer is no longer passed through as "nothing was changed" once this approve has written. `CompleteEarlierBulkAcceptAsync` now also returns whether it wrote. `FromLineResult` takes `approveWrote`, and when it is true, any answer other than Incomplete or InvoiceNotFound gets `ApproveNotFinishedText` ("The approval did not finish; part of it was saved. Approve again to complete it."). The outcome and code are unchanged.
- `notes/100-review-read-model.md` §3 points here.
- `LockDuration` (2 minutes) and `MaxLinesPerRequest` (1,000) are unchanged.

No new endpoint, service, DI registration, package, column or lock API. The only lock call added is the existing `RenewProcessingLockAsync`, used as `DocumentContainerRelocator` uses it (`ConfirmAsync`: renew, and treat false or a throw as lost, for good).

## Renewal interval and why

The interval is `LockRenewInterval` = 30 seconds, a quarter of `LockDuration`. It is checked at each checkpoint and is not a background heartbeat.

- **Bound (corrected in fix round 1).** A checkpoint that does not renew is less than 30 seconds past the last renewal. The lock therefore lapses only when two consecutive checkpoints are more than the lock's duration minus 30 seconds apart: 90 seconds on the line route, 4.5 minutes on the approval's held paths.
  - Since fix round 1, the checkpoints cover the whole call, not only the writes: after the line read, the unapplied corrections, the Signal sets, the close rights and the firm gate; before each owner question; and before every write.
  - So "one step" is one paged line read (one page holds 5,000 rows), one of those checks, one owner question, one conditional line transaction, one totals recalculation (plus the best-effort index enqueue after it), one 400-row correction chunk, or one line's record or Signal close.
  - Before fix round 1, the whole read-and-validate phase (about 120 ms per owner question, so about 120 seconds for 1,000 lines) ran before the first checkpoint and could exceed the lock on its own.
  - On a held path the first checkpoint always renews, so the caller's own time under the lock before the call must fit in the caller's lock.
- **Cost.** A call shorter than 30 seconds makes no renewal call. A long call makes about one renewal call per 30 seconds, instead of one per line.
- **No heartbeat.** A background timer, like the relocator's, would keep the lock alive during one long step. The relocator still needs a check at each step, though. The POML asks for checkpoints ("before each record chunk or every N lines"). A timer adds a task that runs alongside the call and has to be cancelled for a per-request call. The step-length limit below is the residual risk.
- **Time-based, not count-based.** A count ("every N lines") does not bound time when Dataverse is slow. The interval uses the service's `TimeProvider`, so tests drive it with `FakeTimeProvider`.
- **Approval path.** The approval's renewal uses the same interval at the approval's 5-minute duration, so the approval's lock is never shortened to 2 minutes.

## Tests

The new tests are in the existing classes, as partial files.

- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceLineDecisionServiceTests.LockRenewal.cs`
  - It adds `LeaseLocks`: owners and expiry on a fake clock, as `IdempotencyService` behaves. A test can force the answer of the n-th renewal or make it throw, and capture the world at that moment.
  - (1) `LongCall_RenewsTheLock_AndCompletes`: 10 Clean lines take 200 seconds, past the 2-minute lock. The call makes 4 or more renewals, all with this call's owner and key at 2 minutes. No gap between taking, renewing and releasing the lock reaches 2 minutes. The call completes.
  - (2) and (3) `LostLock_StopsBeforeTheNextWrite_AndTheRepeatCompletes` has 10 cases: answers false or throws, crossed with the slow step (ownership before the line write, lines, totals, record, close). Each case asserts:
    - the write counts at the failed renewal equal the final ones (no write after it);
    - Unavailable or Incomplete with the expected `failedStep`;
    - an owner-checked release and a `lock-lost` error log;
    - that the same decision repeated is Applied, with one record per line and every Signal closed.
  - (4) `ShortCall_MakesNoRenewal_AndCompletesAsBefore`.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceApprovalServiceTests.LockRenewal.cs` (the class was made `partial`)
  - (5) `Approve_AcceptClean_RenewsTheHeldLineLock_AndApproves`: the renewals use the line key, the approval's owner and `InvoiceApprovalService.LockDuration`, and the lock never lapses.
  - (5) `Approve_LostLineLock_StopsTheBulkAccept_AndTheRepeatApproves` has 4 cases: the first or second renewal answers false or throws. Each asserts no write after the failed renewal, the existing Unavailable or Incomplete at `accept-clean`, In Review with no approve record, and that the claimant's repeat approves with 3 line records.
- Harness changes:
  - `InvoiceLineDecisionServiceTests.World`: optional `LockService`, `Clock` and `OnWrite` hooks.
  - `ApprovalWorld`: optional `LockService` and `LineClock`. `InMemoryLocks.RenewProcessingLockAsync` answers true for a held key, because the held path now always renews once.
  - The defaults are unchanged, so the existing tests run as before.

**Fail-before.** The two service files were restored to `b4746e24df` with the new tests in place, using the filter on the five new test names. The result was 16 failed and 1 passed. Every failure was "Expected locks.Renewals ... but found 0 / the collection is empty". The one pass is (4), the short-call guard, which passes before and after by design. With the change the result is 17 of 17 passed.

**Runs after**, all with `-- xUnit.MaxParallelThreads=4`:

- The POML filter `FullyQualifiedName~InvoiceLineDecision|FullyQualifiedName~InvoiceApproval`: 427 of 427 passed.
- The wider affected set: 1,822 of 1,822 passed. It adds `InvoiceLineReviewEndpoints`, `InvoiceRejectReturn`, `InvoiceDecisionIncompleteHint`, `InvoiceBlockedHold`, `InvoiceOpenHolds`, `InvoiceFirmHold`, `InvoiceCorrection`, `InvoiceDecisionNote`, `InvoiceAssignFirm`, `InvoiceLineAdd`, `EligibleApproverResolver` and `InvoiceSubmissionPersisterTests`, which covers the ISS-029 `InvoiceLineDecisionIdReusedTests`.

**Build.** `dotnet build src/server/api/Sprk.Bff.Api/ --no-incremental` gave 0 warnings and 0 errors.

### Fix round 1 (adversarial review)

- Line service (`InvoiceLineDecisionServiceTests.LockRenewal.cs`):
  - `SlowPreWritePhase_Renews_AndCompletes`: 4 lines at 40 seconds per owner question, so the pre-write phase alone passes the 2-minute lock. The call renews, no gap reaches 2 minutes, and it completes.
  - `LostInASlowPreWritePhase_TheEquallySlowRepeatCompletes`: the lock is lost in that phase, so the answer is Unavailable with nothing written. A repeat that is just as slow completes.
  - `LostBeforeTheLastClose_IsIncompleteAtSignals`: covers the `SignalsStep` branch of `LockLost`.
- Approval (`InvoiceApprovalServiceTests.LockRenewal.cs`):
  - `Approve_SlowPreWritePhase_RenewsTheHeldLineLock_AndApproves`: 2 minutes per owner question across 3 lines, past the approval's 5-minute lock. It approves, and the line lock never lapses.
  - `Approve_LostAfterThisApproveWrote_NeverSaysNothingWasChanged`: this approve completes an earlier bulk accept (one record), then loses the lock before the Clean lines' write. The answer is Unavailable with `ApproveNotFinishedText`, and the repeat approves.
  - `Approve_LostBeforeAnyWrite_SaysNothingWasChanged`: the guard for the unchanged text.

**Fail-before.** The two service files were restored to `e5aaaf229b`, with the `ApproveNotFinishedText` literal inlined in the test. The result was 4 failed and 2 passed:
- The 4 failures were the two slow pre-write tests, the equally slow repeat and the approve-already-wrote text.
- The 2 passes were the unchanged-text guard and the `SignalsStep` test, which covers a branch already present in `e5aaaf229b`.

With the change, all 23 lock-renewal tests passed.

**Runs after**, with `-- xUnit.MaxParallelThreads=4`:
- the POML filter: 433 of 433 passed;
- the wider affected set: 1,828 of 1,828 passed.

`dotnet build src/server/api/Sprk.Bff.Api/ --no-incremental` gave 0 warnings and 0 errors.

## Known limits

- **K1: the duration of a 1,000-line call on dev is still not measured.** The renewal makes the measurement less urgent but does not replace it. Nobody knows yet whether one step can approach 90 seconds on dev. The steps in question are the 1,000-line conditional transaction and a 400-row correction chunk.
- **K2: no lease covers one long step.** If a single step outlasts the rest of the lock, the next checkpoint finds the lock lost and stops. The decision is not corrupted. The line write is still guarded by row versions, and the stop is the existing incomplete answer, which a repeat completes. Only that one step ran unlocked. A background heartbeat would close this gap and can be added if K1's measurement shows long steps.
- **K2: renewal is read-then-write, not atomic.** `IdempotencyService.RenewProcessingLockAsync` reads the lock, checks the owner and writes the new expiry. Another owner that takes an expired lock between the read and the write can be overwritten. This is pre-existing, in the shared API (task 166), and ISS-050 only calls it.
- **K3: the invoice-decision lock is not renewed, but an Override cannot overlap an approve on a real path.**
  - The invoice-decision lock is `SubmissionOverrideService.LockKey`, 5 minutes. Submission Override takes only that lock, not the line lock.
  - Override acts only on a held submission.
  - Approve refuses a held invoice before any write (its hold checks in `CheckApprovableAsync`).
  - Every path that makes an invoice held in the middle of an approve takes the line lock (the re-run and Assign Firm do), and approve keeps that lock.
  - The line lock is renewed through the held bulk accepts and the Unreviewed rows. The totals, claim, transition and record after them are bounded and run inside the remaining 4.5 minutes or more.
- **K4: during a lock-store outage, acquiring fails open but renewal fails closed.** `IdempotencyService` answers a lock-store fault on acquire as "acquired" (the pre-existing fail-open), while a fault on renewal is a lost lock. The effects:
  - An approve with Clean lines or decided AI fields renews at the first checkpoint of its held calls, so it answers 503.
  - A line decision that runs past 30 seconds answers 503.
  - Short line decisions still run.
  - This is by design: never continue on a lock that cannot be confirmed.
- **K4: before the first write a lost lock answers 503 `ebilling.decision.unavailable`, not `incomplete`.** Nothing was committed, so "the lines were updated" would be false. The POML's "incomplete" outcome applies once something is written, which is the "part way" case. Both are 503 and never 409, and a same-content repeat completes either.
- **K4: the invoice index enqueue (task 152) has no checkpoint before it.** It runs after the totals, is best effort, and is keyed by the decision id, so a repeat reuses it. A lost lock is caught at the next checkpoint, before the correction chunks.
- **K4: the line read is checkpointed after the whole paged read, not per page.** A page is 5,000 rows, more than `MaxLinesPerRequest`, so a line decision reads one page. The shared reader (`InvoiceReviewReadService.ReadPagedAsync`) was left unchanged.
- **K5: `InvoiceAdjustmentService`, `InvoiceLineAddService` and the other holders of the line lock are unchanged.** Each holds it for a short, bounded operation. `notes/102-invoice-adjustments.md` K3, "the lock lease is not renewed", still holds for adjustments.
- **Publish-size delta (root §10 bullet 4) was not measured in this sub-agent run.** The change is code only: no package, asset or DI change.
