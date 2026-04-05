# Background Jobs Constraints

> **Domain**: Async Processing, Background Workers
> **Source ADRs**: ADR-004, ADR-017
> **Last Updated**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current (broken jobs pattern links fixed)

---

## When to Load This File

Load when:
- Creating new background job types
- Implementing job handlers
- Adding async enqueue endpoints
- Working with job status and persistence

---

## MUST Rules

### Job Contract (ADR-004)

- ✅ **MUST** use Job Contract schema for all async work
- ✅ **MUST** implement handlers as idempotent (safe under at-least-once)
- ✅ **MUST** use deterministic IdempotencyKey patterns
- ✅ **MUST** set Service Bus `MessageId` = `IdempotencyKey` for cross-instance deduplication
- ✅ **MUST** SHA-256 hash IdempotencyKey values exceeding 128 characters (Service Bus MessageId limit)
- ✅ **MUST** propagate CorrelationId from original request
- ✅ **MUST** emit JobOutcome events (Completed, Failed, Poisoned)

### Job Status (ADR-017)

- ✅ **MUST** persist status transitions and final outcome
- ✅ **MUST** return `202 Accepted` with `jobId` and status URL
- ✅ **MUST** make status updates idempotent
- ✅ **MUST** enforce authorization on status endpoints (ADR-008)
- ✅ **MUST** use stable error codes in outcomes (ADR-019)

---

## MUST NOT Rules

### Job Contract (ADR-004)

- ❌ **MUST NOT** place document bytes or large blobs in payload
- ❌ **MUST NOT** assume exactly-once delivery
- ❌ **MUST NOT** use Durable Functions for orchestration

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
public class AnalysisJobHandler : IJobHandler<AnalysisJob>
{
    public async Task<JobResult> HandleAsync(AnalysisJob job, CancellationToken ct)
    {
        // Check if already processed (idempotency)
        if (await _cache.ExistsAsync(job.IdempotencyKey))
            return JobResult.Success("Already processed");

        // Process...
        await _cache.SetAsync(job.IdempotencyKey, true, TimeSpan.FromDays(7));
        return JobResult.Success("Completed");
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

Service Bus provides cross-instance duplicate detection when `MessageId` is set. The BFF sets `MessageId = IdempotencyKey` on every enqueued message.

**SHA-256 hashing rule**: Service Bus limits `MessageId` to 128 characters. If the IdempotencyKey exceeds this, hash it:

```csharp
var messageId = idempotencyKey.Length > 128
    ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)))
    : idempotencyKey;
```

This ensures deterministic deduplication even for long keys (e.g., email message IDs with long domains).

---

## Pattern Files (Complete Examples)

- [Background Workers Pattern](../patterns/api/background-workers.md)
- [Jobs Architecture](../../docs/architecture/jobs-architecture.md) — Service Bus processors, 13 job handlers, idempotency, dead-letter
- [Background Workers Architecture](../../docs/architecture/background-workers-architecture.md) — 17 IHostedService implementations

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-004](../adr/ADR-004-job-contract.md) | Job contract schema | New job types |
| [ADR-017](../adr/ADR-017-job-status.md) | Status persistence | Status implementation |

---

**Lines**: ~125
**Purpose**: Single-file reference for all background job constraints

