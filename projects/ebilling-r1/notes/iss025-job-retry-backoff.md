# iss025 — a failed job is retried with back-off (ISS-025 #2876, DEF-055 #2600)

Task: `tasks/iss025-job-retry-backoff.poml`. Rigor FULL. Branch `task/ebilling-iss025` (base `72f2e54503`).

## Before → after

| | Before | After |
|---|---|---|
| Handler returns a retryable `Failure`, not the final delivery | `AbandonMessageAsync`: the broker redelivers at once, so all 5 deliveries are spent within seconds and a one-minute outage dead-letters the job (`MaxRetriesExceeded`) | The processor sends a **copy** of the message scheduled `JobContract.RetryDelay(delivery)` later, carrying `PriorDeliveryCount` = this logical delivery, then completes the original |
| Processing throws (the `catch` path), before the 3rd delivery | Abandon, immediate redelivery | The same scheduled copy, then complete |
| Final delivery (`IsFinalDelivery`: attempts exhausted or logical delivery 5) | Dead-letter `MaxRetriesExceeded` | Unchanged, counted on the logical delivery |
| `Poisoned` outcome | Dead-letter `Poisoned` | Unchanged |
| Throw on the 3rd delivery | Dead-letter `ProcessingError` | Unchanged, counted on the logical delivery |
| `InvalidFormat`, `NoHandler`, `HandlerResolutionFailed` | Dead-letter | Unchanged |
| `JobContract.DeliveryCount` seen by a handler | The broker's count for this message | The logical count: `PriorDeliveryCount` + the broker's count. On an original message (no property) it is the broker's count, exactly as before |
| The send of the copy fails | — | Logged (Warning); the original is abandoned as before (immediate redelivery; the delivery is counted by the broker) |
| The complete fails after the send | — | Logged (Warning), not thrown; see "Duplicate run" below |

## Back-off schedule and window

`JobContract.RetryDelay(failedDelivery)` = `RetryBaseDelay` (1 min) × 2^(delivery − 1), capped at `RetryMaxDelay` (15 min):

| After logical delivery | 1 | 2 | 3 | 4 | (5 = final) |
|---|---|---|---|---|---|
| Wait before the next | 1 min | 2 min | 4 min | 8 min | dead-letter |

`JobContract.RetryWindow` = 15 minutes of waiting in all before the 5th (final) delivery, plus the time the deliveries take. A job that throws (the `catch` path, dead-lettered at the 3rd delivery) waits 1 + 2 = 3 minutes. The cap only matters if `MaxDeliveryCount` grows. No jitter (known limit 4).

Why this length: the transient causes the handlers name (Dataverse service-protection throttling, whose windows are 5 minutes; Redis failover; a token or network blip) clear within minutes, so a 15-minute window outlasts them. Much longer would delay the final-delivery handling that handlers rely on (the inbound email HOLD alert, the invoice submission's Failed state, the eBilling notification's final outcome), which only runs on the 5th delivery.

## How the logical attempt is carried

- Application property `PriorDeliveryCount` (`JobContract.PriorDeliveryCountProperty`), an `int`: the deliveries earlier copies of the job spent. A message without it (a first submission, a dead-letter re-drive, which builds a fresh message without the old properties) has spent none.
- On receipt: `deliveryCount = ReadPriorDeliveryCount(message) + message.DeliveryCount`, set on `job.DeliveryCount` before the handler runs, and used by the processor's dead-letter rules. It is computed before the `try`, so the `catch` path uses it too.
- `ReadPriorDeliveryCount` accepts `int`, `long`, `short` or a whole-number string; anything else, absent or negative reads as 0; it is clamped to 10 000 so adding the broker's count cannot overflow.
- Including the broker's own count keeps the count right when a copy is itself redelivered by the broker (lock lost, a fallback abandon, a host crash).
- The body is not changed, so `Attempt` does not advance (as before) and the idempotency key, job id, correlation id and payload are byte-identical.

## Mechanism: why a scheduled copy

Service Bus has no "abandon with delay". The options were:

1. **Send a copy with `ScheduledEnqueueTime`, then complete the original** (chosen). Uses the processor's existing `ServiceBusClient` (one sender to its own queue, created on the first retry, disposed after the processor stops). The copy is made with the SDK's `new ServiceBusMessage(ServiceBusReceivedMessage)`, which keeps the body, `MessageId`, `CorrelationId`, `SessionId`, `PartitionKey`, `Subject`, `ContentType`, `To`, `ReplyTo`, time-to-live and every application property, and drops only broker-owned annotations.
2. Defer the message and schedule a "wake-up" message that receives it by sequence number: two messages, a deferred-message store, and a new message shape — more moving parts and a new contract.
3. Hold the lock and `Task.Delay` in the callback: occupies a concurrent-call slot for minutes and is capped by `MaxAutoLockRenewalDuration` (10 min).

## Ordering, failure handling and the duplicate run

Order: send the copy, then complete the original (`RetryLaterAsync`).

- **Send fails** → abandon (today's behaviour). The job loses its back-off for that delivery, never a delivery or the job itself.
- **Complete fails after the send** → logged, not thrown. The copy is queued; the original is redelivered when its lock expires; the job may run twice. That is safe because ADR-004 requires every `IJobHandler` to be idempotent under at-least-once delivery, and the copy carries the same `IdempotencyKey` and `JobId` in its unchanged body. The mechanism the handlers use:
  - a per-job **processing lock** in Redis through `IIdempotencyService.TryAcquireProcessingLockAsync(key, owner = JobId, duration)` — e.g. `RecordProcedureDecisionJob` (key `RecordProcedureDecisionKey.Of(table, record, decision)`), `GateDecisionRecordJob` (`GateDecisionKey.Of(communication, authorize)`), `InvoiceSubmissionProcessingJobHandler` and `InvoiceSubmissionRerunJobHandler` (`LockKeyPrefix + submissionId`), `AssignTaskEmailJob`, `BillingRuleDraftJobHandler`;
  - a **completion marker** (`MarkEventAsProcessedAsync` / `IsEventProcessedAsync`) or a natural-key conditional write, so a run after a completed one does nothing new.
  This is the same protection a broker redelivery after a lost message lock has always relied on; the complete-after-send failure adds no new class of duplicate.
- The handler that ran the failed delivery normally released its lock. If it did not (a crash), the copy arrives at least 1 minute later, which is `IdempotencyService.OwnerTakeoverAge`, so the same job can take its own stale lock over (before, an immediate redelivery found the lock under a minute old, was refused, and spent a delivery).

## Retry horizon (a direct consequence)

`StoredFileRetention.SubmissionRetryHorizon` (the lower bound `FinanceOptionsValidator` enforces on `Finance:FileStore:OrphanGraceMinutes`) assumed the 5 deliveries ran back to back: 5 × 20 min = 100 min. A staged blob must now outlive the back-off too, so the horizon is 100 + `RetryWindow` = **115 minutes**. The default grace is 1440, and no config, Bicep or script in the repo sets a lower value; an environment that set the grace between 100 and 114 would now fail start-up validation with the existing message. `appsettings.template.json`'s comment and the `StoredFileReconcileJobTests.Identity` assertion are updated.

## Infra check

`infrastructure/bicep/modules/service-bus.bicep` (used by `customer.bicep` for `sdap-jobs`): `maxDeliveryCount: 10`, `requiresDuplicateDetection: false`, `requiresSession: false`, `lockDuration: PT5M`, `defaultMessageTimeToLive: P14D`. The broker's limit (10) stays above `JobContract.MaxDeliveryCount` (5), so the processor's rule decides, as before. Each copy starts a fresh broker count and a fresh 14-day TTL; the logical count bounds the chain.

## Files

- `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` — logical delivery count; `RetryLaterAsync` (send copy → complete; send fails → abandon; complete fails → log); `ReadPriorDeliveryCount`; a lazily created retry sender to the processor's own queue, disposed after the processor stops; optional `TimeProvider` constructor parameter (default `TimeProvider.System`; no DI registration added — the container uses the default because `TimeProvider` is not registered); `ProcessMessageAsync` is `internal` for the tests; `MaxUnexpectedErrorDeliveries = 3` names the existing literal.
- `src/server/api/Sprk.Bff.Api/Services/Jobs/JobContract.cs` — `PriorDeliveryCountProperty`, `RetryBaseDelay`, `RetryMaxDelay`, `RetryDelay(int)`, `RetryWindow`; `DeliveryCount` / `IsFinalDelivery` docs say "logical".
- `src/server/api/Sprk.Bff.Api/Services/Finance/Files/StoredFileReconcileJob.cs` — horizon includes `RetryWindow`.
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` — the horizon in the comment (100 → 115).
- `tests/integration/regression/Issue2876_ServiceBusJobProcessorRetryBackoffTests.cs` — new.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Files/StoredFileReconcileJobTests.cs` — horizon 100 → 115.
- `docs/architecture/jobs-architecture.md`, `docs/standards/INTEGRATION-CONTRACTS.md` — retry timing.

No new queue, topic, package, job type or DI registration.

## Tests

`Issue2876_ServiceBusJobProcessorRetryBackoffTests` (regression, ADR-038 category 1; 24 cases). It drives the real processor callback and a real handler resolved from DI. Only the Service Bus boundary is doubled (`ServiceBusClient`, `ServiceBusSender`, and the settlement calls of `ProcessMessageEventArgs`), and time is a `FakeTimeProvider`. It covers:

- a retryable failure schedules a copy at now + 1 min with `PriorDeliveryCount` 1, then completes the original (order: send, then complete);
- the delay grows 1/2/4/8 min with the logical delivery, including an original the broker redelivered;
- the schedule, the cap and `RetryWindow`;
- the final delivery dead-letters `MaxRetriesExceeded` for the original at broker count 5 and for a copy at broker count 1;
- `Poisoned` dead-letters as before;
- the copy keeps the body, MessageId, CorrelationId, SessionId, Subject, ContentType and all application properties;
- a failed send abandons and does not complete;
- a failed complete after the send is logged and not thrown (no abandon, no dead-letter);
- a handler sees the logical `DeliveryCount` and `IsFinalDelivery` on a copy;
- a throw before the 3rd delivery schedules a copy, and a throw on the 3rd logical delivery dead-letters `ProcessingError`;
- the property is read defensively.

`StoredFileReconcileJobTests.Identity` asserts the 115-minute horizon.

- **Before** (old processor, with only `ProcessMessageAsync` made internal and the constructor given an ignored `TimeProvider` so the tests compile; old horizon): 17 failed, 7 passed, 24 total. The 7 that pass are the unchanged-behaviour controls: dead-letter at broker 5, poison, a first delivery, the schedule constants, and three defensive reads that expect 0.
- **After**: 24 of 24 pass.
- **Affected classes**, `--filter` on `Issue2876_ServiceBusJobProcessor`, `StoredFileReconcileJobTests`, `ServiceBusCredentialSeamTests`, `BillingRuleDraft`, `InvoiceSubmissionRerunTests`, `GateDecisionRecordJobTests`, `AssignTaskEmailJobHandlerTests`, `InvoiceSubmissionProcessingJobHandlerTests`, `EbillingNotificationJobHandler`, `RecordProcedureDecisionJobTests`, `RecordProcedureRepairSeamTests` and `JobAwareCompletionState`, with `-- xUnit.MaxParallelThreads=4`: 706 passed, 0 failed.
- **Build**: `dotnet build src/server/api/Sprk.Bff.Api/` gives 0 warnings and 0 errors.

## Docs under `.claude/` for the main session

- `.claude/constraints/jobs.md` — add under Job Contract: "A retryable `Failure` (or a throw) is retried by a copy scheduled `JobContract.RetryDelay` later (1, 2, 4, 8 min); `JobContract.DeliveryCount` is the LOGICAL delivery (`PriorDeliveryCount` application property + the broker's count). A handler's last-chance logic keys on `IsFinalDelivery`, never on `args.Message.DeliveryCount`." Optional; nothing there is now wrong.
- `.claude/patterns/api/background-workers.md` line 20 ("Retry with exponential backoff; dead-letter after max attempts") is now accurate for `ServiceBusJobProcessor`; no edit needed. If the pattern describes the processor's retry anywhere else, point it at `JobContract.RetryDelay`.
- `.claude/adr/ADR-004-job-contract.md` — no change needed; the schema's `attempt` still does not advance on a retry.

## Out of scope (recorded, nothing filed)

- **Operator resubmit tool** by job type (ISS-025's second suggestion): not built, not filed, per the POML. The dead-letter re-drive in `DeadLetterQueueService` still exists and gives a re-driven job a full budget (it builds a fresh message without `PriorDeliveryCount`).
- **#2848** (dead-letter alert): unchanged.

## Known limits

1. **`CommunicationJobProcessor` (`sdap-communication`) still abandons at once** and is unchanged (the POML names only `ServiceBusJobProcessor`). Found while reading it, pre-existing and separate: it never sets `job.DeliveryCount`, so `IncomingCommunicationJobHandler`'s `IsFinalDelivery` on that queue is true only via `IsAtMaxAttempts`, and its last-chance HOLD never sees the 5th delivery there. Reported to the caller; not fixed here.
2. **Duplicate detection.** The copy keeps `MessageId` = idempotency key (the jobs constraint). On a queue with duplicate detection ON, the broker would silently drop the copy within the detection window and the completed original would be lost. The BFF queues have it OFF (`service-bus.bicep`), and it can only be set at queue creation. If a queue processed by `ServiceBusJobProcessor` ever enables it, the retry must use a distinct `MessageId`.
3. **Overlapping duplicate after a failed complete.** When the copy and the redelivered original overlap and the first run's lock is over a minute old, the same-owner takeover (`OwnerTakeoverAge`) lets both run; handler idempotency (completion marker / conditional writes) is then the guard, as for any lock-lost redelivery today. `IIdempotencyService`'s lock is still check-then-set (#984).
4. **No jitter.** Jobs that failed at the same instant retry at the same instant. They fail at different times in practice; jitter would make the delay untestable without another seam.
5. **Shutdown.** A retry whose send is cancelled by shutdown falls back to abandon (as before), so that delivery has no back-off.
