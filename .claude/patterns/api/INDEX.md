# API/BFF Patterns Index

> **Domain**: BFF API / .NET Minimal API
> **Last Updated**: 2025-12-19

---

## Available Patterns

| Pattern | Purpose | Lines |
|---------|---------|-------|
| [endpoint-definition.md](endpoint-definition.md) | Minimal API endpoint structure | ~115 |
| [endpoint-filters.md](endpoint-filters.md) | Authorization filters (ADR-008) | ~125 |
| [service-registration.md](service-registration.md) | DI configuration (ADR-010) | ~115 |
| [error-handling.md](error-handling.md) | ProblemDetails (ADR-019) | ~145 |
| [background-workers.md](background-workers.md) | Job processing (ADR-004) | ~175 |

---

## When to Load

| Task | Load These Patterns |
|------|---------------------|
| Create new endpoint | `endpoint-definition.md`, `error-handling.md` |
| Add authorization | `endpoint-filters.md` |
| Register new service | `service-registration.md` |
| Add background job | `background-workers.md` |
| Handle errors | `error-handling.md` |

---

## Canonical Source Files

All patterns reference actual implementations in:
```
src/server/api/Sprk.Bff.Api/
├── Api/                    # Endpoint definitions
│   ├── Ai/                 # AI endpoints (AnalysisEndpoints.cs)
│   ├── Documents/          # Document endpoints
│   └── Filters/            # Authorization filters
├── Infrastructure/
│   ├── DI/                 # Service registration modules
│   ├── Errors/             # ProblemDetailsHelper
│   └── Authorization/      # Policy handlers
├── Services/
│   └── Jobs/               # Background job processing
└── Program.cs              # Main composition root
```

---

## Related Resources

- [API Constraints](../../constraints/api.md) - MUST/MUST NOT rules
- [Jobs Constraints](../../constraints/jobs.md) - Background processing rules

