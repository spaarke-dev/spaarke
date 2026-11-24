# iss050 — the line-decision lock is renewed during a long call (ISS-050, #3059)

Task: `tasks/iss050-line-decision-lock-renewal.poml`. Rigor FULL. Branch `task/ebilling-iss050` (base `b4746e24df`).

## Before → after

| | Before | After |
|---|---|---|
| A line-decision call that runs longer than the 2-minute lock | The lock expires mid-call; a second reviewer can take it and change a line this call has written but not yet recorded; this call then records "accept" on a line that shows Reduced | The call renews the lock (owner-checked) at a checkpoint before every write, so the lock does not lapse between checkpoints |
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
- `Services/Finance/InvoiceApprovalService.cs`: the approval's owner id is passed down: `ApproveHeldAsync` → `CompleteEarlierBulkAcceptAsync` / `AcceptCleanHeldAsync` with `LockDuration`. The class remarks and the step comment are updated. No new answer.
- `notes/100-review-read-model.md` §3 points here.
- `LockDuration` (2 minutes) and `MaxLinesPerRequest` (1,000) are unchanged.

No new endpoint, service, DI registration, package, column or lock API. The only lock call added is the existing `RenewProcessingLockAsync`, used as `DocumentContainerRelocator` uses it (`ConfirmAsync`: renew, and treat false or a throw as lost, for good).

## Renewal interval and why

The interval is `LockRenewInterval` = 30 seconds, a quarter of `LockDuration`. It is checked at each write checkpoint and is not a background heartbeat.

- **Bound.** The lock was renewed at most 30 seconds before a checkpoint passes it. It can lapse only if one step between two checkpoints takes longer than about 90 seconds. Such a step is one conditional line transaction, one totals recalculation, one 400-row correction chunk, or one line's record or Signal close.
- **Cost.** A call shorter than 30 seconds makes no renewal call. A long call makes about one renewal call per 30 seconds of writing, instead of one per line.
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

## Known limits

- **K1: the duration of a 1,000-line call on dev is still not measured.** The renewal makes the measurement less urgent but does not replace it. Nobody knows yet whether one step can approach 90 seconds on dev. The steps in question are the 1,000-line conditional transaction and a 400-row correction chunk.
- **K2: no lease covers one long step.** If a single step outlasts the rest of the lock, the next checkpoint finds the lock lost and stops. The decision is not corrupted. The line write is still guarded by row versions, and the stop is the existing incomplete answer, which a repeat completes. Only that one step ran unlocked. A background heartbeat would close this gap and can be added if K1's measurement shows long steps.
- **K3: the approval's other lock is not renewed.** The invoice-decision lock (`SubmissionOverrideService.LockKey`, 5 minutes) is not renewed, and nothing after the bulk accept renews the line lock. That covers the Unreviewed rows, totals, claim, transition and record. The line lock was renewed at most 30 seconds before the bulk accept returned, so about 4.5 minutes remain for those steps. Reject and Return, Assign Firm and the re-run also take the line lock, so they get Busy while the approval holds it. Submission Override (`SubmissionOverrideService`) takes only the invoice-decision lock, so an approve that runs past 5 minutes could overlap an Override. This is pre-existing and outside ISS-050's scope (the line lock under the bulk accept). It is not filed; the main session decides.
- **K4: before the first write a lost lock answers 503 `ebilling.decision.unavailable`, not `incomplete`.** Nothing was committed, so "the lines were updated" would be false. The POML's "incomplete" outcome applies once something is written, which is the "part way" case. Both are 503 and never 409, and a same-content repeat completes either.
- **K5: `InvoiceAdjustmentService`, `InvoiceLineAddService` and the other holders of the line lock are unchanged.** Each holds it for a short, bounded operation. `notes/102-invoice-adjustments.md` K3, "the lock lease is not renewed", still holds for adjustments.
- **Publish-size delta (root §10 bullet 4) was not measured in this sub-agent run.** The change is code only: no package, asset or DI change.
