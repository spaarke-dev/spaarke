# API/BFF Patterns Index

> Pointer-based pattern files for BFF API / .NET Minimal API development.
> Each file points to canonical source code — read the code, not descriptions.

| Pattern | When to Load |
|---------|-------------|
| [endpoint-definition.md](endpoint-definition.md) | Creating/modifying API endpoints |
| [endpoint-filters.md](endpoint-filters.md) | Adding resource-level authorization |
| [error-handling.md](error-handling.md) | Returning errors, exception handling |
| [background-workers.md](background-workers.md) | Adding async job processing |
| [resilience.md](resilience.md) | Adding HTTP retry/circuit-breaker/timeout |
| [service-registration.md](service-registration.md) | Registering services in DI |
| [send-email-integration.md](send-email-integration.md) | Adding email sending to any module |

## Entry Point
`src/server/api/Sprk.Bff.Api/Program.cs` — composition root for all endpoints, DI, middleware.

## Related
- [API Constraints](../../constraints/api.md) — MUST/MUST NOT rules
- [Jobs Constraints](../../constraints/jobs.md) — Background processing rules
