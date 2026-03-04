# Project CLAUDE.md — SDAP BFF API & Performance Enhancement

## Project Context

- **Branch**: `work/sdap-bff-api-and-performance-enhancement-r1`
- **Scope**: Internal improvements to Sprk.Bff.Api — no new features, no API contract changes
- **Primary Target**: `src/server/api/Sprk.Bff.Api/`
- **Secondary Target**: `infrastructure/bicep/`, `tests/`

## Applicable ADRs

| ADR | Key Constraint | Applies To |
|-----|---------------|------------|
| ADR-001 | Minimal API, no Azure Functions | All API work |
| ADR-003 | Cache data not decisions, per-request UAC only | B2, auth caching |
| ADR-004 | Idempotent jobs, deterministic keys | D1, job queues |
| ADR-007 | All Graph ops through SpeFileStore facade | B1, B3 |
| ADR-008 | Endpoint filters, no global auth middleware | A2, D3 |
| ADR-009 | Redis-first, no IMemoryCache without proof | B1, B2, B4, D2 |
| ADR-010 | Concrete-first DI, feature modules, ≤15 non-framework | A1 |
| ADR-013 | AI stays in BFF, no separate service | E1, E2 |
| ADR-015 | Log only identifiers/sizes/timings | D3, E1 |
| ADR-019 | ProblemDetails for all HTTP failures | A2, D3 |

## Key Decisions

| Decision | Rationale | Date |
|----------|-----------|------|
| Redis graceful degradation | Bypass cache on Redis failure, don't fail fast | 2026-03-04 |
| Organic cache warming | No proactive warm-up, accept first-request latency | 2026-03-04 |
| appsettings.json feature flags | No Azure App Configuration dependency | 2026-03-04 |
| Unit + integration tests | Both required for new caching/resilience code | 2026-03-04 |

## File Ownership (Parallel Safety)

When running parallel agents, respect these boundaries:

| Scope | Owned Files | Conflict Risk |
|-------|------------|---------------|
| A1 (decomposition) | Program.cs, Infrastructure/Startup/*, Infrastructure/DI/* | HIGH — blocks others |
| B1 (graph cache) | New cache service files, SpeFileStore cache integration | MEDIUM |
| B2 (auth cache) | AuthorizationService cache layer | LOW |
| C1/C3 (dataverse) | DataverseService.cs, DataverseWebApiService.cs | MEDIUM — C1 and C3 touch same file |
| D1 (queues) | ServiceBusJobProcessor.cs, Jobs/* | LOW |
| E1/E2 (AI perf) | Ai/Chat/Tools/*, Ai/Services/* | LOW |
| F* (infra) | infrastructure/bicep/* | LOW — isolated from src/ |

## Task Execution Protocol

All tasks in this project MUST be executed via the `task-execute` skill. See root CLAUDE.md for full protocol.

**Trigger phrases**: "work on task X", "continue", "next task", "resume task X"
