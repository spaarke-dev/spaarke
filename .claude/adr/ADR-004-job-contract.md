# ADR-004: Async Job Contract (Concise)

> **Status**: Accepted
> **Domain**: Background Processing
> **Last Updated**: 2025-12-18

---

## Decision

Use **one standard Job Contract** for all async work. Process via BackgroundService workers with idempotent handlers.

**Rationale**: Consistent retries, idempotency, and observability across all background processing.

---

## Constraints

### ✅ MUST

- **MUST** use Job Contract schema for all async work
- **MUST** implement handlers as idempotent (safe under at-least-once)
- **MUST** use deterministic IdempotencyKey patterns
- **MUST** propagate CorrelationId from original request
- **MUST** emit JobOutcome events (Completed, Failed, Poisoned)

### ❌ MUST NOT

- **MUST NOT** place document bytes or large blobs in payload
- **MUST NOT** assume exactly-once delivery
- **MUST NOT** use Durable Functions for orchestration

---

## Job Contract Schema

```json
{
  "jobId": "guid",
  "jobType": "ai-indexing",
  "subjectId": "guid",
  "correlationId": "guid",
  "idempotencyKey": "doc-{docId}-v{rowVersion}",
  "attempt": 1,
  "maxAttempts": 3,
  "payload": { "action": "index" },
  "createdAt": "2025-12-12T00:00:00Z"
}
```

### Idempotency Key Patterns

| Job Type | Key Pattern |
|----------|-------------|
| Document indexing | `doc-{docId}-v{rowVersion}` |
| AI analysis | `analysis-{docId}-{analysisType}` |
| Email processing | `email-{messageId}` |

**See**: [Background Worker Pattern](../patterns/api/background-workers.md)

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-001](ADR-001-minimal-api.md) | BackgroundService workers |
| [ADR-017](ADR-017-job-status.md) | Job status persistence |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-004-async-job-contract.md](../../docs/adr/ADR-004-async-job-contract.md)

---

**Lines**: ~75
