# ADR-017: Async Job Status and Persistence (Concise)

> **Status**: Proposed
> **Domain**: Background Processing
> **Last Updated**: 2025-12-18

---

## Decision

Standardize **job status persistence** and **client contract** for all async work. Every job must persist status transitions and expose uniform polling API.

**Rationale**: Clients need predictable status polling. Operators need consistent outcomes for troubleshooting.

---

## Constraints

### ✅ MUST

- **MUST** persist status transitions and final outcome for every job
- **MUST** return `202 Accepted` with `jobId` and status URL from enqueue endpoints
- **MUST** make status updates idempotent (safe under retries)
- **MUST** enforce authorization on job status endpoints (ADR-008)
- **MUST** use stable error codes in outcomes (ADR-019)

### ❌ MUST NOT

- **MUST NOT** store large payloads in status records (ADR-015)
- **MUST NOT** expose status without authorization checks
- **MUST NOT** leave jobs without status records (orphaned jobs)

---

## Status States

```
Queued → Running → (Completed | Failed | Poisoned | Cancelled)
```

## Status Shape

```json
{
  "jobId": "guid",
  "jobType": "ai-analyze",
  "subjectId": "guid",
  "correlationId": "guid",
  "status": "Running",
  "attempt": 1,
  "maxAttempts": 3,
  "createdAt": "2025-12-12T00:00:00Z",
  "startedAt": "2025-12-12T00:00:01Z",
  "completedAt": null,
  "errorCode": null,
  "errorMessage": null
}
```

---

## Implementation Patterns

### Enqueue Response

```csharp
// Return 202 with status URL
return Results.Accepted(new {
    jobId = job.JobId,
    subjectId = job.SubjectId,
    statusUrl = $"/api/jobs/{job.JobId}/status"
});
```

### Handler Status Updates

```csharp
// Record Running when starting
await _statusStore.UpdateStatusAsync(jobId, JobStatus.Running);

// Record terminal outcome
await _statusStore.CompleteAsync(jobId, new JobOutcome {
    Success = true,
    Message = "Analysis completed"
});
```

**See**: [Job Status Pattern](../patterns/jobs/status-persistence.md)

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-004](ADR-004-job-contract.md) | Job envelope and processing |
| [ADR-008](ADR-008-endpoint-filters.md) | Status endpoint authorization |
| [ADR-015](ADR-015-ai-data-governance.md) | No large payloads in status |
| [ADR-019](ADR-019-problemdetails.md) | Stable error codes |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-017-async-job-status-and-persistence.md](../../docs/adr/ADR-017-async-job-status-and-persistence.md)

---

**Lines**: ~100

