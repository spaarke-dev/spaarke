# ADR-004: Async job contract and uniform processing

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2025-12-04 |
| Authors | Spaarke Engineering |

## Context

Ad-hoc background processing patterns cause inconsistent retries, missing idempotency, and hard-to-diagnose failures. We need a single, disciplined approach across all async work.

## Decision

| Rule | Description |
|------|-------------|
| **One Job Contract** | Standard message format for all async work |
| **BackgroundService workers** | `ServiceBusProcessor` per JobType |
| **Idempotent handlers** | Handlers must be safe under at-least-once delivery (dedupe + safe replays) |
| **Central retry policy** | Standard Polly retry/backoff/jitter policy applied consistently across handlers |
| **Poison queue** | On retry exhaustion |
| **Observable outcomes** | Persist and emit `JobOutcome` events |

## Job Contract Schema

| Field | Type | Description |
|-------|------|-------------|
| `JobId` | GUID | Unique job identifier |
| `JobType` | string | Determines handler routing |
| `SubjectId` | string | Entity being processed (commonly a GUID string) |
| `CorrelationId` | string | Request correlation (commonly a GUID string) |
| `IdempotencyKey` | string | Deduplication key |
| `Attempt` | int | Current attempt number |
| `MaxAttempts` | int | Retry limit |
| `Payload` | JSON | Job-specific payload (must not include large blobs/PII) |
| `CreatedAt` | datetime | When the job was created |

### JSON Example (as sent on Service Bus)

The implementation serializes using `camelCase` JSON.

```json
{
	"jobId": "00000000-0000-0000-0000-000000000001",
	"jobType": "ai-indexing",
	"subjectId": "00000000-0000-0000-0000-000000000002",
	"correlationId": "00000000-0000-0000-0000-000000000003",
	"idempotencyKey": "doc-00000000-0000-0000-0000-000000000002-v5",
	"attempt": 1,
	"maxAttempts": 3,
	"payload": {
		"action": "index"
	},
	"createdAt": "2025-12-12T00:00:00+00:00"
}
```

**Payload rules (Required):**
- Do not place document bytes, attachment bytes, or email bodies in `payload`.
- Keep payloads small and fetch required data from Dataverse/SPE at processing time.
- Prefer stable identifiers (GUIDs) and configuration flags only.

## Consequences

**Positive:**
- Predictable error handling and recovery
- Simpler reasoning about back-pressure and scaling

**Negative:**
- Slightly more explicit plumbing compared to Functions bindings, but consistent and testable

## Alternatives Considered

Durable Functions orchestration. **Rejected** due to host fragmentation and additional complexity.

## Operationalization

| Aspect | Implementation |
|--------|----------------|
| Topic/subscription | Named by JobType |
| Idempotency | Per-handler keys |
| Correlation | Centralized propagation |
| Logging | Structured metrics for queue depth and age |
| Health | Worker liveness checks |

## AI-Directed Coding Guidance

When adding new background work:

- Define a new `JobType` string and a handler implementing `IJobHandler`.
- Set `SubjectId` to the primary entity being processed.
- Use deterministic `IdempotencyKey` patterns (e.g., `doc-{docId}-v{rowVersion}`) so replays are safe.
- Treat job handling as at-least-once: correctness must not depend on exactly-once delivery.
- Emit/persist a `JobOutcome` for `Completed`, `Failed` (retryable vs permanent), and `Poisoned`.

## Exceptions

If third-party triggers are required, introduce a dedicated adapter that still pushes to the same Job Contract and processing pipeline.

## Success Metrics

| Metric | Target |
|--------|--------|
| Duplicate processing | Low rate |
| Retries | Bounded |
| Poison queue handling | Clear process |
| Queue health | Observable |
| Processing latency | Monitored |

## Compliance

**Code review checklist:**
- [ ] All async work uses Job Contract schema
- [ ] Handler implements idempotency check
- [ ] CorrelationId propagated from original request
- [ ] MaxAttempts configured appropriately
- [ ] Poison queue destination defined
