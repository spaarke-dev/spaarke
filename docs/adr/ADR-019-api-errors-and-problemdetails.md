# ADR-019: API Errors and ProblemDetails Standard (including SSE)

| Field | Value |
|-------|-------|
| Status | **Proposed** |
| Date | 2025-12-12 |
| Updated | 2025-12-12 |
| Authors | Spaarke Engineering |
| Sprint | TBD |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-019 Concise](../../.claude/adr/ADR-019-problemdetails.md) - ~100 lines, decision + constraints + patterns
- [API Constraints](../../.claude/constraints/api.md) - MUST/MUST NOT rules for API development
- [Error Handling Pattern](../../.claude/patterns/api/error-handling.md) - ProblemDetails code examples

**When to load this full ADR**: Error handling strategy, SSE error event structure

---

## Context

Minimal APIs make it easy for each endpoint to invent its own error shape. For AI workloads (including SSE streaming), inconsistent error handling causes:

- Client-side complexity (many special cases)
- Poor debuggability (missing correlation/error codes)
- Security risk (leaking details/content)

The repo already contains a shared helper for consistent ProblemDetails responses:
- `src/server/api/Sprk.Bff.Api/Infrastructure/Errors/ProblemDetailsHelper.cs`

This ADR standardizes how we represent errors across HTTP and SSE.

## Decision

### Decision Rules

| Rule | Requirement |
|------|-------------|
| **ProblemDetails for HTTP** | All non-streaming HTTP failures return RFC 7807 ProblemDetails (use helpers where available). |
| **Stable error codes** | ProblemDetails must include a stable `errorCode` extension where meaningful (especially for AI). |
| **Correlation always** | Include a correlation identifier (e.g., `HttpContext.TraceIdentifier`) in error responses and logs. |
| **No content leaks** | Error details must not include document content, prompts, or model output. |
| **SSE terminal error event** | SSE endpoints emit a final “error” event with a stable code and correlation ID; do not silently drop connections without a terminal signal when possible. |
| **Map upstream failures** | Graph/OpenAI/DocIntel failures should map to consistent status codes (`429`, `503`, `500`) and stable error codes. |

## Scope

Applies to:
- BFF endpoints (all API areas)
- AI streaming endpoints (SSE)
- Job status endpoints (ADR-017)

## Non-goals

- Standardizing every single error title string

## Operationalization

### HTTP APIs

- Use `.ProducesProblem(...)` in endpoint metadata.
- Prefer `ProblemDetailsHelper` helpers for common cases (e.g., `AiUnavailable`, `AiRateLimited`, `Forbidden`).

### SSE APIs

SSE endpoints should:
- Stream content events as usual.
- On error, emit a final event that includes:
  - `type: "error"`
  - `done: true`
  - `errorCode` (stable)
  - `message` (safe)
  - `correlationId`

## Failure modes

- **Client can’t distinguish** rate limit vs outage → retry storms.
- **Missing correlation** → slow incident response.
- **Leaking details** → security/privacy incident.

## AI-Directed Coding Guidance

When adding a new endpoint:
- Decide the ProblemDetails shape first.
- Define a stable `errorCode` set for the feature.
- Ensure logs and ProblemDetails share correlation.

## Compliance checklist

- [ ] Non-streaming endpoints return ProblemDetails on failure.
- [ ] Errors include correlation ID.
- [ ] AI-related errors include stable `errorCode`.
- [ ] SSE endpoints emit a terminal error event (when possible).
- [ ] No prompts/document contents are included in errors/logs.

## Related ADRs

- [ADR-013: AI architecture](./ADR-013-ai-architecture.md)
- [ADR-015: AI data governance](./ADR-015-ai-data-governance.md)
- [ADR-017: Async job status](./ADR-017-async-job-status-and-persistence.md)

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-12-12 | 0.1 | Initial proposed ADR | Spaarke Engineering |
