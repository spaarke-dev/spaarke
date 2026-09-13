# Background Jobs Constraints

> **Domain**: Async Processing, Background Workers, Scheduled Jobs
> **Source ADRs**: ADR-052 (where work runs), ADR-004 (queue), ADR-036 (schedule), ADR-017 (status)
> **Last Updated**: 2026-09-12
> **Last Reviewed**: 2026-09-12
> **Reviewed By**: unified-access-control-r2 task 102 (ADR-052 + ADR-004/036 Amendment A1)
> **Status**: Current

---

## When to Load This File

Load when:
- Deciding where background, scheduled or event-driven work runs ([ADR-052](../adr/ADR-052-workload-placement.md))
- Creating new background job types or scheduled jobs
- Implementing job handlers
- Adding async enqueue endpoints
- Working with job status and persistence

---

## MUST Rules

### Placement (ADR-052)

- ✅ **MUST** decide the host — the BFF, Azure Functions or Container Apps Jobs — per workload under [ADR-052](../adr/ADR-052-workload-placement.md), and state it in the Placement Justification
- ✅ **MUST** make every background handler idempotent per unit of work, whatever its host

### Job Contract — queue-driven work (ADR-004)

- ✅ **MUST** use Job Contract schema for all **queue-driven** async work (schedule-driven → ADR-036)
- ✅ **MUST** implement handlers as the non-generic `IJobHandler` (`JobType` + `ProcessAsync(JobContract, ct)` → `JobOutcome`)
- ✅ **MUST** take an **atomic** per-message claim before the side effect and write a completion marker after it (ADR-004 A1 §4). `IIdempotencyService` as built is check-then-set and fails open — not sufficient on its own until fixed
- ✅ **MUST** use deterministic IdempotencyKey patterns
- ✅ **MUST** set Service Bus `MessageId` = `IdempotencyKey` — but it de-duplicates only where the queue has duplicate detection enabled, a create-time property that is **off** on the BFF queues (ADR-004 A1 §5). Receive-side idempotency is what protects you
- ✅ **MUST** SHA-256 hash IdempotencyKey values exceeding 128 characters (Service Bus MessageId limit)
- ✅ **MUST** propagate CorrelationId from original request
- ✅ **MUST** emit JobOutcome events (Completed, Failed, Poisoned)
- ✅ **MUST** follow ADR-052 §7 for multi-step orchestration (Durable Task in its own host, or a hand-rolled state machine with a written reason)

### Scheduled Jobs — schedule-driven work in the BFF (ADR-036 A1)

- ✅ **MUST** implement `IScheduledJob` (see [`scheduled-jobs.md`](../patterns/api/scheduled-jobs.md))
- ✅ **MUST** get one dispatch per schedule across instances (distributed lease — task 103) for jobs that must not run concurrently
- ✅ **MUST** take an atomic claim per unit of work before its side effect, and write a completion marker after it
- ✅ **MUST** throw from `ExecuteAsync` when a retry could complete work this tick would otherwise lose; otherwise count failures and complete
- ✅ **MUST** emit one structured heartbeat log per attempt, including an attempt with nothing to do
- ✅ **MUST** register via `AddScheduledJob<TJob>(cron, enabled)` (task 103) and stay host-neutral (no `ScheduledJobHost` / `IBackgroundJobStore` / `ScheduledJobRegistry` dependency)

### Job Status (ADR-017)

- ✅ **MUST** persist status transitions and final outcome
- ✅ **MUST** return `202 Accepted` with `jobId` and status URL
- ✅ **MUST** make status updates idempotent
- ✅ **MUST** enforce authorization on status endpoints (ADR-008)
- ✅ **MUST** use stable error codes in outcomes (ADR-019)

---

## MUST NOT Rules

### Job Contract (ADR-004)

- ❌ **MUST NOT** place document bytes or large blobs in payload (orchestration inputs included)
- ❌ **MUST NOT** assume exactly-once delivery

### Placement (ADR-052)

- ❌ **MUST NOT** add a new hand-rolled timer `BackgroundService` (a `PeriodicTimer` / `Task.Delay` loop) — scheduled work in the BFF is an `IScheduledJob`
- ❌ **MUST NOT** add new Azure WebJobs
- ❌ **MUST NOT** put Azure Functions or Durable Task packages inside `Sprk.Bff.Api` (ADR-001)

### Job Status (ADR-017)

- ❌ **MUST NOT** store large payloads in status records
- ❌ **MUST NOT** expose status without authorization
- ❌ **MUST NOT** leave jobs without status records (orphaned)

---

## Job Contract Schema

```json
{
  "jobId": "guid",
  "jobType": "ai-analyze",
  "subjectId": "guid",
  "correlationId": "guid",
  "idempotencyKey": "doc-{docId}-v{rowVersion}",
  "attempt": 1,
  "maxAttempts": 3,
  "payload": { "action": "index" },
  "createdAt": "2025-12-12T00:00:00Z"
}
```

## Status States

```
Queued → Running → (Completed | Failed | Poisoned | Cancelled)
```

---

## Quick Reference Patterns

### Enqueue Endpoint

```csharp
app.MapPost("/api/analysis/enqueue", async (
    AnalysisRequest request,
    JobSubmissionService jobs) =>
{
    var job = await jobs.SubmitAsync(new JobContract {
        JobType = "ai-analyze",
        SubjectId = request.DocumentId,
        IdempotencyKey = $"analysis-{request.DocumentId}-{request.Type}",
        Payload = JsonSerializer.Serialize(request)
    });

    return Results.Accepted(new {
        jobId = job.JobId,
        statusUrl = $"/api/jobs/{job.JobId}/status"
    });
});
```

### Idempotent Handler

```csharp
public class AnalysisJobHandler : IJobHandler
{
    public string JobType => "ai-analyze";

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;

        // Completion marker: already done → nothing to do
        if (await _idempotency.IsEventProcessedAsync(job.IdempotencyKey, ct))
            return JobOutcome.Success(job.JobId, JobType, DateTimeOffset.UtcNow - started);

        // Claim BEFORE the side effect. ⚠️ As built, TryAcquireProcessingLockAsync is check-then-set and fails
        // open (ADR-004 A1 §4) — the rule is an ATOMIC claim; tracked in #984.
        if (!await _idempotency.TryAcquireProcessingLockAsync(job.IdempotencyKey, TimeSpan.FromMinutes(10), ct))
            return JobOutcome.Failure(job.JobId, JobType, "claimed by another consumer", job.Attempt,
                DateTimeOffset.UtcNow - started);

        try
        {
            // Process...
            await _idempotency.MarkEventAsProcessedAsync(job.IdempotencyKey, TimeSpan.FromDays(7), ct);
            return JobOutcome.Success(job.JobId, JobType, DateTimeOffset.UtcNow - started);
        }
        finally
        {
            await _idempotency.ReleaseProcessingLockAsync(job.IdempotencyKey, ct);
        }
    }
}
```

### Idempotency Key Patterns

| Job Type | Key Pattern |
|----------|-------------|
| Document indexing | `doc-{docId}-v{rowVersion}` |
| AI analysis | `analysis-{docId}-{analysisType}` |
| Email processing | `Communication:{messageId}:Process` |

### Service Bus Deduplication

Service Bus duplicate detection de-duplicates on `MessageId` **only when the queue was created with duplicate
detection enabled** (Standard/Premium; it cannot be switched on later without recreating the queue). It is off on
the BFF queues (`service-bus.bicep`). The BFF still sets `MessageId = IdempotencyKey` on every enqueued message, so
the de-duplication applies wherever it is enabled; receive-side idempotency (above) is the guarantee.

**SHA-256 hashing rule**: Service Bus limits `MessageId` to 128 characters. If the IdempotencyKey exceeds this, hash it:

```csharp
var messageId = idempotencyKey.Length > 128
    ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)))
    : idempotencyKey;
```

This keeps the key deterministic even for long keys (e.g., email message IDs with long domains).

---

## Pattern Files (Complete Examples)

- [Background Workers Pattern](../patterns/api/background-workers.md) — queue-driven
- [Scheduled Jobs Pattern](../patterns/api/scheduled-jobs.md) — schedule-driven
- [Jobs Architecture](../../docs/architecture/jobs-architecture.md) — Service Bus processors, job handlers, idempotency, dead-letter
- [Background Workers Architecture](../../docs/architecture/background-workers-architecture.md) — IHostedService inventory

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-052](../adr/ADR-052-workload-placement.md) | Where background work runs | Any new background, scheduled or event-driven work |
| [ADR-004](../adr/ADR-004-job-contract.md) | Job contract schema (queue-driven) | New job types |
| [ADR-036](../adr/ADR-036-background-job-infrastructure.md) | Scheduled jobs in the BFF | New scheduled work |
| [ADR-017](../adr/ADR-017-job-status.md) | Status persistence | Status implementation |

---

**Purpose**: Single-file reference for all background job constraints
