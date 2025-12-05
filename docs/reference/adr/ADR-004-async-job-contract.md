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
| **Idempotent handlers** | Centralized Polly retry policies |
| **Poison queue** | On retry exhaustion |
| **Observable outcomes** | Persist and emit `JobOutcome` events |

## Job Contract Schema

| Field | Type | Description |
|-------|------|-------------|
| `JobId` | GUID | Unique job identifier |
| `JobType` | string | Determines handler routing |
| `SubjectId` | GUID | Entity being processed |
| `CorrelationId` | GUID | Request correlation |
| `IdempotencyKey` | string | Deduplication key |
| `Attempt` | int | Current attempt number |
| `MaxAttempts` | int | Retry limit |

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
