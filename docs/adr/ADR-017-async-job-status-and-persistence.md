# ADR-017: Async Job Status, Persistence, and Client Contract

| Field | Value |
|-------|-------|
| Status | **Proposed** |
| Date | 2025-12-12 |
| Updated | 2025-12-12 |
| Authors | Spaarke Engineering |
| Sprint | TBD |

---

## Context

ADR-004 defines the job envelope and uniform processing model. As async usage grows (especially for AI and document processing), we need a consistent, user-facing and operationally useful **job status contract**:

- Clients need a standard way to poll/subscribe for status
- Operators need consistent outcomes for troubleshooting
- The platform needs consistent idempotency and state transitions

The repo already contains:
- `JobContract` (`src/server/api/Sprk.Bff.Api/Services/Jobs/JobContract.cs`)
- `JobOutcome` (`src/server/api/Sprk.Bff.Api/Services/Jobs/JobOutcome.cs`)

This ADR standardizes status persistence and API shape (without prescribing the exact storage choice today).

## Decision

### Decision Rules

| Rule | Requirement |
|------|-------------|
| **Status is persisted** | Every job must persist status transitions and final outcome (success/failure/poisoned). |
| **Client contract is uniform** | All async endpoints return `202 Accepted` with a `jobId` and a status URL pattern. |
| **Idempotent status updates** | Status updates must be safe under retries (at-least-once delivery). |
| **No large payloads** | Status records store summaries/metadata, not large blobs or full model outputs (ADR-015). |
| **Authorization applies** | Job status endpoints enforce access to the underlying subject (document/record) via ADR-008 patterns. |

## Scope

Applies to:
- Any endpoint that enqueues work (AI batch, document processing, indexing)
- Any worker/handler that processes Service Bus jobs

## Non-goals

- Choosing whether status storage lives in Dataverse vs another store (allowed as an implementation detail, but must satisfy the contract)
- Implementing live push/subscription in this ADR (polling is sufficient as baseline)

## Status contract (Recommended)

### Status states

`Queued` → `Running` → (`Completed` | `Failed` | `Poisoned` | `Cancelled`)

### Minimal status shape

- `jobId` (GUID)
- `jobType` (string)
- `subjectId` (string)
- `correlationId` (string)
- `status` (string)
- `attempt` / `maxAttempts`
- `createdAt` / `startedAt` / `completedAt`
- `errorCode` (optional)
- `errorMessage` (optional, safe)

## Operationalization

### Endpoint pattern

- Enqueue endpoints should:
  - Submit `JobContract` (ADR-004)
  - Return `202 Accepted` with `{ jobId, subjectId, statusUrl }`

### Worker/handler pattern

- Handlers should:
  - Record `Running` when starting meaningful work
  - Record terminal status with a `JobOutcome`
  - Include stable error codes (see ADR-019)

## Failure modes

- **Orphaned jobs** (no status record) → no debuggability.
- **Inconsistent client behavior** → each UI implements a different polling strategy.
- **Status leakage** (no auth) → cross-tenant/user information leak.

## AI-Directed Coding Guidance

When adding a new `JobType`:
- Define the subject and status persistence behavior first.
- Ensure job submission returns the same response contract.
- Use stable error codes and keep error messages safe.

## Compliance checklist

- [ ] Enqueue endpoints return `202` with `jobId` and status URL.
- [ ] Job processing records transitions and terminal outcomes.
- [ ] Status endpoints enforce authorization.
- [ ] Status records do not store large/unsafe payloads.

## Related ADRs

- [ADR-004: Async job contract](./ADR-004-async-job-contract.md)
- [ADR-008: Authorization via endpoint filters](./ADR-008-authorization-endpoint-filters.md)
- [ADR-015: AI data governance](./ADR-015-ai-data-governance.md)
- [ADR-019: API error & ProblemDetails standard](./ADR-019-api-errors-and-problemdetails.md)

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-12-12 | 0.1 | Initial proposed ADR | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-017 Concise](../../.claude/adr/ADR-017-job-status.md) - ~100 lines
- [Jobs Constraints](../../.claude/constraints/jobs.md) - MUST/MUST NOT rules

**When to load this full ADR**: Historical context, status contract details, compliance checklists.
