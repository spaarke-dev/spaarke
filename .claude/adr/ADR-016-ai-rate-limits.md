# ADR-016: AI Cost, Rate Limits, and Backpressure (Concise)

> **Status**: Proposed
> **Domain**: AI/ML Operations
> **Last Updated**: 2025-12-18

---

## Decision

Apply **layered throttling** to AI operations: per-endpoint rate limiting, bounded concurrency, and explicit budgets. Use async jobs for heavy work.

**Rationale**: AI workloads are expensive and subject to upstream throttling. Proactive limits prevent cascading failures and surprise costs.

---

## Constraints

### ✅ MUST

- **MUST** apply rate limiting to all AI endpoints (`ai-stream`, `ai-batch`)
- **MUST** bound concurrency for upstream AI service calls
- **MUST** use async jobs for large/batch AI work (ADR-004)
- **MUST** configure explicit timeouts for upstream calls
- **MUST** return clear `429`/`503` ProblemDetails under load
- **MUST** track per-operation counts/latencies (not content)

### ❌ MUST NOT

- **MUST NOT** rely on upstream throttling as primary control
- **MUST NOT** allow unbounded `Task.WhenAll` on throttled services
- **MUST NOT** retry without bounds (centralize retry logic)
- **MUST NOT** grow queues without visibility

---

## Rate Limiting Patterns

### Endpoint Layer

```csharp
group.MapPost("/analyze", StreamAnalyze)
    .RequireRateLimiting("ai-stream");

group.MapPost("/enqueue", EnqueueAnalysis)
    .RequireRateLimiting("ai-batch");
```

### Service Layer

- Bound concurrent calls to OpenAI/DocIntel/Graph
- Map timeouts to stable error codes
- Use semaphores or channel-based throttling

### Job Layer

- Limit concurrent job handling per node
- Scale out workers (not concurrency)
- Idempotent processing (ADR-004)

**See**: [AI Rate Limiting Pattern](../patterns/ai/rate-limiting.md)

---

## Failure Modes

| Risk | Prevention |
|------|------------|
| Thundering herd | Bounded concurrency |
| Unbounded queue growth | Backpressure signals |
| Silent cost growth | Telemetry + budgets |
| Retry storms | Centralized bounded retries |

---

## Budget Guidelines

Each AI operation should define:
- Max documents per request
- Max tokens per request
- Max file size
- Max duration/timeout

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-004](ADR-004-job-contract.md) | Async job pattern |
| [ADR-013](ADR-013-ai-architecture.md) | AI architecture |
| [ADR-014](ADR-014-ai-caching.md) | Caching reduces load |
| [ADR-019](ADR-019-problemdetails.md) | Error responses |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-016-ai-cost-rate-limit-and-backpressure.md](../../docs/adr/ADR-016-ai-cost-rate-limit-and-backpressure.md)

---

**Lines**: ~95

