# iss025 — a failed job is retried with back-off (ISS-025 #2876, DEF-055 #2600)

Task: `tasks/iss025-job-retry-backoff.poml`. Rigor FULL. Branch `task/ebilling-iss025` (base `72f2e54503`). Round 0 `57ad9ef7dc`; fix round 1 (Opus review F1-a, F1-b, F2-a, F2-b, K3 tests) on top.

## Before → after

Both ADR-004 queue consumers, `ServiceBusJobProcessor` (`sdap-jobs`) and, since fix round 1, `CommunicationJobProcessor` (`sdap-communication`), follow these rules through the shared `Services/Jobs/JobMessageRetry.cs`.

| | Before | After |
|---|---|---|
| Handler returns a retryable `Failure`, not the final delivery | `AbandonMessageAsync`: the broker redelivers at once, so all 5 deliveries are spent within seconds and a one-minute outage dead-letters the job (`MaxRetriesExceeded`) | The processor **schedules a copy** of the message on its own queue (`ScheduleMessageAsync`) `JobContract.RetryDelay(delivery)` later, carrying `PriorDeliveryCount` = this logical delivery, keeps its sequence number, then completes the original |
| Processing throws (the `catch` path), before the 3rd delivery | Abandon, immediate redelivery | The same scheduled copy, then complete |
| A `MessageLockLost` reaches the `catch` | Abandon (which fails too) or dead-letter | Logged; nothing else: no copy, no settlement. The broker redelivers |
| Final delivery (`IsFinalDelivery`: attempts exhausted or logical delivery 5) | Dead-letter `MaxRetriesExceeded` | Unchanged, counted on the logical delivery |
| `Poisoned` outcome | Dead-letter `Poisoned` | Unchanged |
| Throw on the 3rd delivery | Dead-letter `ProcessingError` | Unchanged, counted on the logical delivery |
| `InvalidFormat`, `NoHandler`, `HandlerResolutionFailed`, `WrongQueue` | Dead-letter | Unchanged |
| The complete of a `Completed` outcome, or any dead-letter in the `try`, throws | Fell into the `catch`: abandon or `ProcessingError`, so a completed job could be retried | Logged where it happens (`TrySettleAsync`) and returned: never a retry copy. The broker redelivers the message when its lock lapses, and the same rule settles it then |
| `JobContract.DeliveryCount` seen by a handler | `sdap-jobs`: the broker's count. `sdap-communication`: **never set (0)** | Both queues: the logical count, `PriorDeliveryCount` + the broker's count. On an original message (no property) it is the broker's count |
| Inbound email HOLD (`IncomingCommunicationJobHandler.HoldAsync`) on the last delivery | Never saw it on `sdap-communication`: the email was dead-lettered by broker count with no Critical log and no administrator alert | Sees `IsFinalDelivery` on the logical 5th delivery: Critical log, administrators alerted, dead-lettered `Poisoned` |
| The schedule of the copy fails | — | Logged; the original is abandoned as before (immediate redelivery). If the abandon also fails, it is logged and not thrown, so the `catch` never schedules a second copy |
| The complete fails after the schedule | — | Fix round 2: the processor's own queue is **peeked** at the original's sequence number. Original still there → the copy is **cancelled** (`CancelScheduledMessageAsync`) and the redelivered original carries the job on; a failed cancel is logged at Error (residual risk 1). Original gone (nothing, or a later sequence number) → the complete was applied, the copy is **kept** (Warning). The peek fails → the copy is **kept** (Warning): a duplicate over a lost job |

## Back-off schedule and window

`JobContract.RetryDelay(failedDelivery)` = `RetryBaseDelay` (1 min) × 2^(delivery − 1), capped at `RetryMaxDelay` (15 min):

| After logical delivery | 1 | 2 | 3 | 4 | (5 = final) |
|---|---|---|---|---|---|
| Wait before the next | 1 min | 2 min | 4 min | 8 min | dead-letter |

`JobContract.RetryWindow` = 15 minutes of waiting in all before the 5th (final) delivery, plus the time the deliveries take. A job that throws (the `catch` path, dead-lettered at the 3rd delivery) waits 1 + 2 = 3 minutes. The cap only matters if `MaxDeliveryCount` grows. No jitter (known limit 3).

Why this length: the transient causes the handlers name (Dataverse service-protection throttling, whose windows are 5 minutes; Redis failover; a token or network blip) clear within minutes, so a 15-minute window outlasts them. Much longer would delay the final-delivery handling that handlers rely on (the inbound email HOLD alert, the invoice submission's Failed state, the eBilling notification's final outcome), which only runs on the 5th delivery.

## How the logical attempt is carried

- Application property `PriorDeliveryCount` (`JobContract.PriorDeliveryCountProperty`), an `int`: the deliveries earlier copies of the job spent. A message without it (a first submission, a dead-letter re-drive, which builds a fresh message without the old properties) has spent none.
- On receipt: `deliveryCount = JobMessageRetry.LogicalDeliveryCount(message)` = `ReadPriorDeliveryCount(message) + message.DeliveryCount`, set on `job.DeliveryCount` before the handler runs, and used by the processor's dead-letter rules. It is computed before the `try`, so the `catch` path uses it too.
- `ReadPriorDeliveryCount` accepts `int`, `long`, `short` or a whole-number string; anything else, absent or negative reads as 0; it is clamped to 10 000 so adding the broker's count cannot overflow.
- Including the broker's own count keeps the count right when a copy is itself redelivered by the broker (lock lost, a fallback abandon, a host crash).
- The body is not changed, so `Attempt` does not advance (as before) and the idempotency key, job id, correlation id and payload are byte-identical.

## Mechanism: why a scheduled copy

Service Bus has no "abandon with delay". The options were:

1. **Schedule a copy (`ScheduleMessageAsync`), then complete the original** (chosen). Uses the processor's existing `ServiceBusClient` (one sender per processor, to that processor's OWN queue, created on the first retry, disposed after the processor stops: `JobMessageRetry.RetrySender`). The copy is made with the SDK's `new ServiceBusMessage(ServiceBusReceivedMessage)`, which keeps the body, `MessageId`, `CorrelationId`, `SessionId`, `PartitionKey`, `Subject`, `ContentType`, `To`, `ReplyTo`, time-to-live and every application property, and drops only broker-owned annotations. `ScheduleMessageAsync` (rather than a send with `ScheduledEnqueueTime`) returns the sequence number, which is what lets a failed complete cancel the copy when the original is still on the queue.
2. Defer the message and schedule a "wake-up" message that receives it by sequence number: two messages, a deferred-message store, and a new message shape — more moving parts and a new contract.
3. Hold the lock and `Task.Delay` in the callback: occupies a concurrent-call slot for minutes and is capped by `MaxAutoLockRenewalDuration` (10 min).

**`JobMessageRetry` (new internal static class, fix round 1).** Existing: the retry logic lived in `ServiceBusJobProcessor`; `CommunicationJobProcessor` had its own abandon copy. Extension: the two processors are separate hosted services on separate queues (by design, `jobs-architecture.md` "Queue Isolation"), so the shared rules are a static helper both call, with no DI registration. Cost of doing nothing: the communication queue keeps immediate redelivery and never sets the delivery count, so a held inbound email is dead-lettered with no alert (F1-a).

## Ordering, failure handling and the duplicate run

Order: schedule the copy, then complete the original (`JobMessageRetry.RetryLaterAsync`, which never throws).

- **Schedule fails** → abandon (the behaviour before ISS-025). The job loses its back-off for that delivery, never a delivery or the job itself. An abandon that also fails is logged; the broker redelivers when the lock lapses.
- **Complete fails after the schedule** (fix round 2, `JobMessageRetry.ResolveFailedCompleteAsync`). A failed complete is ambiguous: the broker may have applied it and lost the answer. So the processor peeks its OWN queue (`RetrySender.PeekAsync`: a short-lived `client.CreateReceiver(queueName)` from the same `ServiceBusClient`, `PeekMessageAsync(fromSequenceNumber: original.SequenceNumber)`, then disposed; no DI registration). The receiver targets `RetrySender.QueueName`, the same queue the copies go to: `sdap-jobs` for `ServiceBusJobProcessor`, `sdap-communication` for `CommunicationJobProcessor` (both tested). A peek takes no lock and also returns a message another receiver holds, so a redelivered original that another run has locked still counts as present.
  - **Present** (the peeked message has the original's sequence number) → the original was not settled. Cancel the copy by its sequence number (with `CancellationToken.None`, so a shutdown does not leave a copy beside the redelivered original); the broker redelivers the original and it carries the job on. Logged at Warning.
  - **Absent** (nothing, or a message with a later sequence number) → the complete was applied. Keep the copy, which carries the job on alone. Logged at Warning with the message id and the copy's sequence number.
  - **The peek fails** → keep the copy, logged at Warning. A duplicate run is recoverable through idempotency (risk 1); a lost job would be silent.
- **Residual risk 1: a duplicate run.** It happens when `ScheduleMessageAsync` throws after the broker accepted the schedule (a timeout or lost response: the original is then abandoned while the copy exists; re-check K2), when the original is present but the cancel fails (logged at Error with the sequence number), when the peek fails while the original is in fact still there, or when the original is redelivered between a failed complete and the peek. The copy and the redelivered original can then both run. That is covered by ADR-004 handler idempotency: the copy carries the same `IdempotencyKey` and `JobId` in its unchanged body, and the handlers guard with
  - a per-job **processing lock** in Redis through `IIdempotencyService.TryAcquireProcessingLockAsync(key, owner = JobId, duration)` — e.g. `RecordProcedureDecisionJob` (key `RecordProcedureDecisionKey.Of(table, record, decision)`), `GateDecisionRecordJob` (`GateDecisionKey.Of(communication, authorize)`), `InvoiceSubmissionProcessingJobHandler` and `InvoiceSubmissionRerunJobHandler` (`LockKeyPrefix + submissionId`), `AssignTaskEmailJob`, `BillingRuleDraftJobHandler`;
  - a **completion marker** (`MarkEventAsProcessedAsync` / `IsEventProcessedAsync`) or a natural-key conditional write (the inbound email: the `sprk_graphmessageid` check), so a run after a completed one does nothing new.
  This is the same protection a broker redelivery after a lost message lock has always relied on. When the two runs overlap and the first run's lock is over a minute old, the same-owner takeover (`IdempotencyService.OwnerTakeoverAge`) lets both run, and the completion marker / conditional write is then the guard; the lock is still check-then-set (#984).
- **Residual risk 2: a complete that failed but was in fact applied — now keeps the job.** A complete can report failure after the broker applied it (a lost response, after which the SDK's own retry sees the lock token gone). In fix round 1 the processor cancelled the copy blindly, and the job was lost. Now the peek finds the original gone, so the copy is kept and carries the job on (Warning with the message id and the copy's sequence number). What remains: if an applied complete were still visible to a peek for a moment, the original would read as present and the copy would be cancelled (the round-1 loss). Whether a peek can see a just-completed message was not checked against a live namespace from this task; it is the one path left that can lose the job, and it is logged at Warning ("still on the queue; cancelling retry copy").
- The handler that ran the failed delivery normally released its lock. If it did not (a crash), the copy arrives at least 1 minute later, which is `OwnerTakeoverAge`, so the same job can take its own stale lock over (before, an immediate redelivery found the lock under a minute old, was refused, and spent a delivery).

## Retry horizon (a direct consequence)

`StoredFileRetention.SubmissionRetryHorizon` (the lower bound `FinanceOptionsValidator` enforces on `Finance:FileStore:OrphanGraceMinutes`) assumed the 5 deliveries ran back to back: 5 × 20 min = 100 min. A staged blob must now outlive the back-off too, so the horizon is 100 + `RetryWindow` = **115 minutes**. The default grace is 1440, and no config, Bicep or script in the repo sets a lower value; an environment that set the grace between 100 and 114 would now fail start-up validation with the existing message. `appsettings.template.json`'s comment and the `StoredFileReconcileJobTests.Identity` assertion are updated. The invoice scan window over all deliveries is now about 34 minutes (19 of waits + 15 of back-off; `InvoiceSubmissionProcessingJobHandler.ScanWaits` doc).

## Infra check

`infrastructure/bicep/modules/service-bus.bicep` (used by `customer.bicep` for `sdap-jobs` and `sdap-communication`): `maxDeliveryCount: 10`, `requiresDuplicateDetection: false`, `requiresSession: false`, `lockDuration: PT5M`, `defaultMessageTimeToLive: P14D`. The broker's limit (10) stays above `JobContract.MaxDeliveryCount` (5), so the processor's rule decides, as before. Each copy starts a fresh broker count and a fresh 14-day TTL; the logical count bounds the chain. Duplicate detection being OFF is what the copy depends on (known limit 1).

## Files

- `src/server/api/Sprk.Bff.Api/Services/Jobs/JobMessageRetry.cs` — new (fix round 1): `ReadPriorDeliveryCount`, `LogicalDeliveryCount`, `IsLockLost`, `TrySettleAsync`, `RetryLaterAsync` (schedule → complete; schedule fails → abandon, abandon failure logged; complete fails → `ResolveFailedCompleteAsync`: peek, cancel the copy only if the original is still there, cancel failure logged at Error; keep the copy if it is gone or the peek fails), `RetrySender` (`Get`, `PeekAsync` on its own queue, `DisposeAsync`).
- `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` — logical delivery count; settlements through `TrySettleAsync`; the retry through `JobMessageRetry`; lock lost not retried; optional `TimeProvider` constructor parameter (default `TimeProvider.System`; no DI registration added — `TimeProvider` is not registered, so the container uses the default); `ProcessMessageAsync` is `internal` for the tests; `MaxUnexpectedErrorDeliveries = 3` names the existing literal.
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationJobProcessor.cs` — fix round 1 (F1-a): sets the logical `job.DeliveryCount`; the final branch keys on `job.IsFinalDelivery` (was `IsAtMaxAttempts || DeliveryCount >= 5` on the broker count); the same back-off, settlement and lock-lost rules; its `RetrySender` targets `CommunicationQueueName` (`sdap-communication`), tested; optional `TimeProvider`; `ProcessMessageAsync` internal.
- `src/server/api/Sprk.Bff.Api/Services/Jobs/JobContract.cs` — `PriorDeliveryCountProperty`, `RetryBaseDelay`, `RetryMaxDelay`, `RetryDelay(int)`, `RetryWindow`; `DeliveryCount` / `IsFinalDelivery` docs say "logical".
- `src/server/api/Sprk.Bff.Api/Services/Jobs/JobSubmissionService.cs` — comment (F2-a): duplicate detection is off, receive-side idempotency is the guarantee, and the retry copy depends on it staying off.
- Comments only (F2-b): `Services/Jobs/Insights/InsightsIngestJobHandler.cs`, `Services/Ai/PublicContracts/JobAwareCompletionState.cs` (4 places), `Services/Signals/RecordProcedureDecisionJob.cs`, `Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs` (scan window about 34 min).
- `src/server/api/Sprk.Bff.Api/Services/Finance/Files/StoredFileReconcileJob.cs` — horizon includes `RetryWindow`.
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` — the horizon in the comment (100 → 115).
- `tests/integration/regression/Issue2876_ServiceBusJobProcessorRetryBackoffTests.cs` — new.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Files/StoredFileReconcileJobTests.cs` — horizon 100 → 115.
- `docs/architecture/jobs-architecture.md`, `docs/standards/INTEGRATION-CONTRACTS.md` — retry timing, schedule/cancel, both processors; the duplicate-detection row corrected (F2-a).

No new queue, topic, package, job type or DI registration.

## Tests

`Issue2876_ServiceBusJobProcessorRetryBackoffTests` (regression, ADR-038 category 1; 35 cases). It drives the REAL processor callbacks with handlers resolved from DI. Only the Service Bus boundary is doubled (`ServiceBusClient`, `ServiceBusSender`, `ServiceBusReceiver` for the peek, and the settlement calls of `ProcessMessageEventArgs`), and time is a `FakeTimeProvider`. On the communication queue the handler is the real `IncomingCommunicationJobHandler.HoldAsync` with its real `NotificationService` over a capturing entity service; only its Graph-backed processing step is replaced by "owner unresolved" (the HOLD trigger), because `IncomingCommunicationProcessor` is sealed and fetches from Graph.

`sdap-jobs`:
- a retryable failure schedules a copy at now + 1 min with `PriorDeliveryCount` 1 on `sdap-jobs`, then completes the original (order: schedule, then complete);
- the delay grows 1/2/4/8 min with the logical delivery, including an original the broker redelivered;
- the schedule, the cap and `RetryWindow`;
- the final delivery dead-letters `MaxRetriesExceeded` for the original at broker count 5 and for a copy at broker count 1;
- `Poisoned` dead-letters as before;
- the copy keeps the body, MessageId, CorrelationId, SessionId, PartitionKey, Subject, ContentType, To, ReplyTo, TTL and all application properties (K3);
- a failed schedule abandons and does not complete; a failed schedule then a failed abandon is not thrown and schedules no second copy;
- a failed complete after the schedule peeks `sdap-jobs` at the original's sequence number, and: original present → cancels the copy by its sequence number; absent (peek returns nothing) → keeps the copy; a later sequence number → keeps the copy; the peek throws → keeps the copy, Warning; a failed cancel is logged at Error and not thrown;
- a `Completed` outcome whose complete fails makes no copy; a failed dead-letter makes no copy and no `ProcessingError`;
- a lost lock reaching the `catch` makes no copy and no settlement;
- a handler sees the logical `DeliveryCount` and `IsFinalDelivery` on a copy;
- a throw before the 3rd delivery schedules a copy, and a throw on the 3rd logical delivery dead-letters `ProcessingError`;
- the property is read defensively.

`sdap-communication`:
- a HELD email on the logical final delivery (a copy at broker count 1) logs Critical, alerts every administrator and dead-letters `Poisoned`;
- a HELD email before the final delivery is retried by a copy on `sdap-communication` (2 min, `PriorDeliveryCount` 2), with no alert;
- a failed complete after the schedule peeks `sdap-communication` (not `sdap-jobs`) and cancels the copy when the original is present, keeps it when absent.

`StoredFileReconcileJobTests.Identity` asserts the 115-minute horizon.

- **Round 0** — before (pre-ISS-025 processor with compile shims; old horizon): 17 failed, 7 passed, 24 total; after: 24/24.
- **Fix round 1** — before (the round-0 processors `57ad9ef7dc`, with only `CommunicationJobProcessor.ProcessMessageAsync` made internal and given an ignored `TimeProvider` so the tests compile): 18 failed, 13 passed, 31 total. The 13 that pass are the behaviours round 0 already had or that did not change. After: 31/31.
- **Fix round 2** — before (fix round 1's `JobMessageRetry.cs` from `c3b4c94b9f`, no shims needed): 7 failed, 28 passed, 35 total. The 7 are the three existing cancel tests (they now expect the peek and the receiver queue) and the four new branch tests. After: 35/35.
- **Affected classes**, `--filter "FullyQualifiedName~Issue2876|FullyQualifiedName~ServiceBusJobProcessor|FullyQualifiedName~StoredFileReconcileJob|FullyQualifiedName~CommunicationJobProcessor|FullyQualifiedName~IncomingCommunicationJobHandler|FullyQualifiedName~InboundHold|FullyQualifiedName~JobAwareCompletionState|FullyQualifiedName~ServiceBusCredentialSeamTests" -- xUnit.MaxParallelThreads=4`: 120 passed, 0 failed (116 after fix round 1). (`InboundHold` selects the existing `HoldAsync` tests in `SecureChildOwnershipTests`; no class is named `CommunicationJobProcessor*` or `IncomingCommunicationJobHandler*`.)
- **Build**: `dotnet build src/server/api/Sprk.Bff.Api/` gives 0 warnings and 0 errors.

## Docs under `.claude/` for the main session

- `.claude/constraints/jobs.md` — add under Job Contract: "A retryable `Failure` (or a throw) is retried by a copy scheduled `JobContract.RetryDelay` later (1, 2, 4, 8 min) on the processor's own queue (`JobMessageRetry`, both processors); `JobContract.DeliveryCount` is the LOGICAL delivery (`PriorDeliveryCount` application property + the broker's count). A handler's last-chance logic keys on `IsFinalDelivery`, never on `args.Message.DeliveryCount`. The copy reuses `MessageId`, so it depends on duplicate detection staying off." Optional; nothing there is now wrong (its "Service Bus Deduplication" section already says detection is off).
- `.claude/patterns/api/background-workers.md` line 20 ("Retry with exponential backoff; dead-letter after max attempts") is now accurate; no edit needed.
- `.claude/adr/ADR-004-job-contract.md` — no change needed; the schema's `attempt` still does not advance on a retry.

## Out of scope (recorded, nothing filed)

- **Operator resubmit tool** by job type (ISS-025's second suggestion): not built, not filed, per the POML. The dead-letter re-drive in `DeadLetterQueueService` still exists and gives a re-driven job a full budget (it builds a fresh message without `PriorDeliveryCount`).
- **#2848** (dead-letter alert): unchanged.

## Known limits

1. **Duplicate detection (K2).** The copy keeps `MessageId` = idempotency key (the jobs constraint). On a queue with duplicate detection ON, the broker would silently drop the copy within the detection window, and the completed original would leave the job lost. `service-bus.bicep` creates both queues with it OFF, and it can only be set at queue creation; the LIVE namespaces were not inspected from this task (no Azure access here), so a queue created by hand with detection on would not be caught. Stays a known limit: confirm `requiresDuplicateDetection = false` on the live `sdap-jobs` and `sdap-communication` queues during deployment, or give the copy a distinct `MessageId` if that ever changes.
2. **A duplicate run** (a failed cancel, a failed peek, or a redelivery racing the peek), covered by idempotency, and **the narrow remainder of an ambiguous complete** — residual risks 1 and 2 above. The ambiguous complete now keeps the job.
3. **No jitter.** Jobs that failed at the same instant retry at the same instant. They fail at different times in practice; jitter would make the delay untestable without another seam.
4. **Shutdown.** A retry whose schedule is cancelled by shutdown falls back to abandon (as before), so that delivery has no back-off.

## Main-session re-check (2026-11-24)

The scoped re-check of fix rounds 1 and 2 (`57ad9ef7dc..a52fb2ceb9`, Sonnet) passed with no F finding; 999 filtered tests passed. K2 (a schedule that the broker accepted but reported as failed leads to a duplicate run) was added to residual risk 1 above. K1 (a peek that still shows an already-applied complete) stays as stated: a completed message is gone from a non-partitioned queue. Test nit: the peek receiver's disposal is not asserted (the code disposes it in `finally`). Merged into work at 6e9054434c.
