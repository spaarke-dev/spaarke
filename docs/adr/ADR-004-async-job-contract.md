# ADR-004: Async job contract and uniform processing

| Field | Value |
|-------|-------|
| Status | **Accepted, as amended** |
| Date | 2025-09-27 |
| Updated | 2026-09-12 (Amendment A1) |
| Authors | Spaarke Engineering |

> ⚠️ **READ [Amendment A1](#amendment-a1-2026-09-12-queue-driven-scope-orchestration-hosting-atomic-idempotency) FIRST.**
> It scopes this ADR to **queue-driven** work (schedule-driven → ADR-036; where either runs → ADR-052), moves
> orchestration hosting to ADR-052 §7, and makes receive-side idempotency atomic.

## Context

Ad-hoc background processing patterns cause inconsistent retries, missing idempotency, and hard-to-diagnose failures. We need a single, disciplined approach across all async work.

## Decision

| Rule | Description |
|------|-------------|
| **One Job Contract** | Standard message format for all **queue-driven** async work *(scope narrowed by A1)* |
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
*(Superseded by A1: orchestration hosting is ADR-052 §7 — Durable Task in its own host, never inside the BFF.)*

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

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-004 Concise](../../.claude/adr/ADR-004-job-contract.md) - ~75 lines
- [Jobs Constraints](../../.claude/constraints/jobs.md) - MUST/MUST NOT rules

**When to load this full ADR**: Historical context, schema details, compliance checklists.

---

## Amendment A1 (2026-09-12): queue-driven scope, orchestration hosting, atomic idempotency

> **Status**: Accepted (path **B**, root CLAUDE.md §6.5; owner decision 2026-09-12). **Driver**:
> `unified-access-control-r2` task 102. **Evidence**:
> [`workload-placement-policy-evaluation.md`](../../projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md).

### 1. Scope

This ADR governs **queue- and topic-driven** async work consumed from Service Bus. Schedule-driven work is
[ADR-036](ADR-036-background-job-infrastructure.md). Where either runs — the BFF, Azure Functions or Container Apps
Jobs — is [ADR-052](ADR-052-workload-placement.md). The Decision's "one Job Contract for all async work" reads
"…for all **queue-driven** async work". A queue or topic consumer is ADR-004 work, never a "long-lived listener".

### 2. Orchestration

<!-- adr052-drift:allow reason="quotes the withdrawn rule" -->
The concise ADR's former rule "MUST NOT use Durable Functions for orchestration", and the Durable rejection under
Alternatives Considered above, are **withdrawn**.
<!-- /adr052-drift:allow -->
Multi-step orchestration follows ADR-052 §7: Durable Task on Durable Task Scheduler in its own host, never inside
the BFF — or a hand-rolled state machine with a written reason. Job and orchestration payloads still carry no
document content.

### 3. Handler contract

The handler interface is the non-generic `IJobHandler`
([`Services/Jobs/IJobHandler.cs`](../../src/server/api/Sprk.Bff.Api/Services/Jobs/IJobHandler.cs)), dispatched by
`ServiceBusJobProcessor` on `JobType`. Documents showing a generic form are stale.

### 4. Receive-side idempotency (MUST)

Take an **atomic** per-message marker before the side effect — Redis `SET NX`, or a conditional upsert on a
natural key — and write a completion marker after it. `IIdempotencyService` as built is check-then-set and fails
open when the cache is unavailable; it is not sufficient on its own until fixed (#984).

### 5. Duplicate detection

`MessageId = IdempotencyKey` de-duplicates only when the queue has duplicate detection enabled. Service Bus fixes
that property **at queue creation** (Standard / Premium tiers), and it is off on the BFF queues
(`service-bus.bicep`); enabling it means recreating the queues (#980). Until then,
receive-side idempotency (§4) is the only de-duplication. Related: `MembershipEventPublisher` sets no `MessageId`
(#981), and the BFF and L2 provisioning derive `MessageId`s differently (#982).

### 6. Known non-conforming consumers

The three Office workers and the membership topic consumer use bespoke message shapes. They are the only
permitted exceptions to "a consumer is ADR-004 work", until migrated (#977). Two defects found in the same
inventory: `IndexingWorkerHostedService` completes messages on failure (#978), and the `office-profile` queue has
no sender (#979).
