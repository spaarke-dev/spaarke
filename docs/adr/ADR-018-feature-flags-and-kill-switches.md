# ADR-018: Feature Flags and Kill Switches (Server + Client)

| Field | Value |
|-------|-------|
| Status | **Proposed** |
| Date | 2025-12-12 |
| Updated | 2025-12-12 |
| Authors | Spaarke Engineering |
| Sprint | TBD |

---

## Context

Spaarke needs reliable ways to:
- Disable expensive or unstable features quickly (kill switch)
- Roll out features safely (feature flags)
- Keep behavior consistent across BFF, workers, and PCF clients

The codebase already uses options-based flags (e.g., `AnalysisOptions.Enabled`, `DocumentIntelligenceOptions.Enabled`). This ADR standardizes how flags are defined and enforced.

## Decision

### Decision Rules

| Rule | Requirement |
|------|-------------|
| **Options-based flags** | Feature flags must be represented in typed options classes and validated at startup. |
| **Fail safe** | When a feature is disabled, endpoints must return a clear `503` ProblemDetails response (not partial behavior). |
| **No security bypass** | Feature flags must not disable authorization checks or weaken security controls; they only gate availability. |
| **Consistent server behavior** | All entry points (sync endpoints + async handlers) check the same flag before executing the feature. |
| **Client respect** | Clients must treat a disabled feature as expected behavior and present a safe UX (no repeated retries). |
| **Documented defaults** | Each flag must state its default and intended environment behavior (dev/staging/prod). |

## Scope

Applies to:
- BFF endpoints (`src/server/api/Sprk.Bff.Api/Api/*`)
- Workers/job handlers (`src/server/api/Sprk.Bff.Api/Services/Jobs/*`)
- PCF clients initiating AI operations (`src/client/pcf/*`)

## Non-goals

- Implementing a new third-party feature management system
- Defining tenant-by-tenant rollout mechanics in this ADR

## Operationalization

### Current examples

- `src/server/api/Sprk.Bff.Api/Configuration/AnalysisOptions.cs` (feature flags)
- `src/server/api/Sprk.Bff.Api/Configuration/DocumentIntelligenceOptions.cs` (feature flags)

### Required pattern

- Server endpoints:
  - Check `options.Value.Enabled` (or equivalent) before starting work.
  - Return a consistent 503 response when disabled.

- Async handlers:
  - Check the same flag and produce a terminal `JobOutcome` indicating feature disabled.

## Failure modes

- **Hidden partial disablement** → confusing UX and operational ambiguity.
- **Kill switch does nothing** → outages become harder to mitigate.
- **Flag disables security** → critical incident.

## AI-Directed Coding Guidance

When introducing a new feature:
- Add a typed flag to an options class.
- Enforce it in both endpoints and handlers.
- Provide a clear ProblemDetails error code for “feature disabled”.

## Compliance checklist

- [ ] Feature has a typed flag with startup validation.
- [ ] Endpoint returns `503` ProblemDetails when disabled.
- [ ] Async handler checks the same flag.
- [ ] Authorization remains enforced regardless of flag state.

## Related ADRs

- [ADR-010: DI minimalism](./ADR-010-di-minimalism.md)
- [ADR-013: AI architecture](./ADR-013-ai-architecture.md)
- [ADR-019: API error & ProblemDetails standard](./ADR-019-api-errors-and-problemdetails.md)

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-12-12 | 0.1 | Initial proposed ADR | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-018 Concise](../../.claude/adr/ADR-018-feature-flags.md) - ~95 lines
- [Config Constraints](../../.claude/constraints/config.md) - MUST/MUST NOT rules

**When to load this full ADR**: Historical context, operationalization details, compliance checklists.
