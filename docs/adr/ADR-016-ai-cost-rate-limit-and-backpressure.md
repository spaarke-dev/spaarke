# ADR-016: AI Cost, Rate Limits, and Backpressure Strategy

| Field | Value |
|-------|-------|
| Status | **Proposed** |
| Date | 2025-12-12 |
| Updated | 2025-12-12 |
| Authors | Spaarke Engineering |
| Sprint | TBD |

---

## Context

AI workloads are expensive, bursty, and subject to upstream throttling. Spaarke already applies rate limiting to AI endpoints (e.g., `ai-stream`, `ai-batch` policies) and uses async jobs via Service Bus. As volume increases, we need a uniform approach to:

- Bound concurrency (avoid stampedes)
- Provide predictable behavior under load
- Protect upstream dependencies (Graph/SPE, Document Intelligence, OpenAI)
- Make cost visible and controllable

## Decision

### Decision Rules

| Rule | Requirement |
|------|-------------|
| **Layered throttling** | Use per-endpoint rate limiting and bounded concurrency; do not rely on upstream throttling as a control mechanism. |
| **Prefer async for heavy work** | Large/batch AI work must be enqueue-only (ADR-004) unless streaming is required for UX. |
| **Explicit budgets** | AI operations must have explicit token/size/time budgets via options/config.
| **Backpressure signals** | Under load, return clear `429`/`503` ProblemDetails responses and avoid long queue buildup without visibility. |
| **Retry discipline** | Retries must be centralized, bounded, and telemetry-visible; avoid client retry storms. |
| **Cost telemetry** | Track per-operation counts and latencies, and (where feasible) estimated tokens/bytes — without logging content. |

## Scope

Applies to:
- AI endpoints (`src/server/api/Sprk.Bff.Api/Api/Ai/*`)
- Background job handlers invoking AI (`src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/*`)
- Any client functionality that triggers AI repeatedly (PCF)

## Non-goals

- Selecting exact quotas for every tenant right now
- Implementing billing attribution in this ADR

## Operationalization

### Current building blocks

- Rate limiting policies are applied on AI endpoints (e.g., `.RequireRateLimiting("ai-stream")`, `.RequireRateLimiting("ai-batch")`).
- Feature flags exist on options (e.g., `AnalysisOptions.Enabled`, `DocumentIntelligenceOptions.Enabled`).

### Required guardrails

- **API layer**
  - Keep `ai-stream` and `ai-batch` policies current and enforced on all AI endpoints.
  - Use consistent ProblemDetails for load shedding and AI upstream failure (see ADR-019 and `ProblemDetailsHelper`).

- **Service layer**
  - Bound concurrency when calling upstream AI services (OpenAI/DocIntel/Graph). Do not allow unbounded `Task.WhenAll` on externally throttled operations.
  - Establish timeouts for upstream calls and map timeouts to stable error codes.

- **Job layer**
  - Use ADR-004 job processing with idempotency for heavy work.
  - Limit concurrent job handling per node; scale out workers rather than increasing concurrency blindly.

## Failure modes

- **Thundering herd** (many parallel extractions/completions) → upstream throttling and cascading failures.
- **Unbounded queue growth** → delayed results and poor user trust.
- **Silent cost growth** → surprise spend.

## AI-Directed Coding Guidance

When adding an AI endpoint or job:
- Decide streaming vs async job early.
- Apply rate limiting for all exposed endpoints.
- Add explicit request budgets (max docs, max tokens, max file size, max duration).
- Ensure concurrency is bounded in code paths that call upstream services.

## Compliance checklist

- [ ] Endpoint has an explicit rate limiter policy.
- [ ] Concurrency is bounded for upstream calls.
- [ ] Timeouts are configured and mapped to stable error responses.
- [ ] Heavy operations have an enqueue-only path.
- [ ] Telemetry records counts/latency/estimated size without content.

## Related ADRs

- [ADR-004: Async job contract](./ADR-004-async-job-contract.md)
- [ADR-013: AI architecture](./ADR-013-ai-architecture.md)
- [ADR-014: AI caching and reuse policy](./ADR-014-ai-caching-and-reuse-policy.md)
- [ADR-019: API error & ProblemDetails standard (incl SSE)](./ADR-019-api-errors-and-problemdetails.md)

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-12-12 | 0.1 | Initial proposed ADR | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-016 Concise](../../.claude/adr/ADR-016-ai-rate-limits.md) - ~95 lines
- [AI Constraints](../../.claude/constraints/ai.md) - MUST/MUST NOT rules

**When to load this full ADR**: Historical context, operationalization details, compliance checklists.
