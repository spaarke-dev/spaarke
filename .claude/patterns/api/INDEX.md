# API/BFF Patterns Index

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current (added missing provisioning-pipeline entry)

> Pointer-based pattern files for BFF API / .NET Minimal API development.
> Each file points to canonical source code — read the code, not descriptions.

| Pattern | When to Load | Last Reviewed | Status |
|---------|-------------|---------------|--------|
| [endpoint-definition.md](endpoint-definition.md) | Creating/modifying API endpoints | 2026-04-05 | Verified |
| [endpoint-filters.md](endpoint-filters.md) | Adding resource-level authorization | 2026-04-05 | Verified |
| [error-handling.md](error-handling.md) | Returning errors, exception handling | 2026-04-05 | Verified |
| [background-workers.md](background-workers.md) | Adding async job processing | 2026-04-05 | Verified |
| [resilience.md](resilience.md) | Adding HTTP retry/circuit-breaker/timeout | 2026-04-05 | Verified |
| [service-registration.md](service-registration.md) | Registering services in DI | 2026-04-05 | Verified |
| [send-email-integration.md](send-email-integration.md) | Adding email sending to any module | 2026-04-05 | Verified |
| [provisioning-pipeline.md](provisioning-pipeline.md) | Customer provisioning flow | 2026-04-05 | Current |

## Entry Point
`src/server/api/Sprk.Bff.Api/Program.cs` — composition root for all endpoints, DI, middleware.

## Related
- [API Constraints](../../constraints/api.md) — MUST/MUST NOT rules
- [Jobs Constraints](../../constraints/jobs.md) — Background processing rules
