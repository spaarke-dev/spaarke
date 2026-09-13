# ADR-004: Async Job Contract (Concise)

> **Status**: Accepted, as amended (A1, 2026-09-12)
> **Domain**: Background Processing
> **Last Updated**: 2026-09-12 (Amendment A1 — queue-only scope, orchestration hosting → ADR-052, atomic idempotency)

---

## Decision

Use **one standard Job Contract** for all **queue-driven** async work (Service Bus). Inside the BFF, process it via
`ServiceBusJobProcessor` → `IJobHandler` with idempotent handlers.

**Scope (A1)**: queue- and topic-driven work consumed from Service Bus. Schedule-driven work → [ADR-036](ADR-036-background-job-infrastructure.md).
Where either runs → [ADR-052](ADR-052-workload-placement.md). A queue or topic consumer is ADR-004 work, never a
"long-lived listener".

**Rationale**: Consistent retries, idempotency, and observability across all background processing.

---

## Constraints

### ✅ MUST

- **MUST** use Job Contract schema for all queue-driven async work
- **MUST** implement handlers as `IJobHandler` (non-generic — `Services/Jobs/IJobHandler.cs`), idempotent under at-least-once
- **MUST** mark receive-side idempotency **atomically** per message — SET NX, or a conditional upsert on a natural key. `IIdempotencyService` as built is check-then-set and fails open; not sufficient on its own until fixed (#984)
- **MUST** use deterministic IdempotencyKey patterns
- **MUST** propagate CorrelationId from original request
- **MUST** emit JobOutcome events (Completed, Failed, Poisoned)
- **MUST** follow ADR-052 §7 for multi-step orchestration (Durable Task in its own host, or a hand-rolled state machine with a written reason). The former Durable prohibition is withdrawn (A1)

### ❌ MUST NOT

- **MUST NOT** place document bytes or large blobs in payload (this applies to orchestration inputs too)
- **MUST NOT** assume exactly-once delivery
- **MUST NOT** rely on `MessageId = IdempotencyKey` for de-duplication unless the queue has duplicate detection enabled — Service Bus fixes that **at queue creation**, and it is off on the BFF queues (`service-bus.bicep`)

### Known non-conforming consumers (A1)

The three Office workers and the membership topic use bespoke message shapes. They are the only permitted
exceptions to "a consumer is ADR-004 work", until migrated (#977).

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
| [ADR-052](ADR-052-workload-placement.md) | Where queue-driven work runs; orchestration hosting (§7) |
| [ADR-036](ADR-036-background-job-infrastructure.md) | Schedule-driven work (not this ADR) |
| [ADR-001](ADR-001-minimal-api.md) | The BFF runtime that hosts `ServiceBusJobProcessor` |
| [ADR-017](ADR-017-job-status.md) | Job status persistence |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-004-async-job-contract.md](../../docs/adr/ADR-004-async-job-contract.md) (Amendment A1 in full)
