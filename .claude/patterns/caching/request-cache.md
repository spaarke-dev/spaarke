# Request Cache Pattern

## When
Collapsing duplicate data loads within a single HTTP request (same data accessed multiple times).

## Read These Files
1. `src/server/shared/Spaarke.Core/Cache/RequestCache.cs` — Dictionary-backed per-request memoization
2. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs` — Scoped DI registration

## Constraints
- **ADR-009**: Use for same-request dedup only — cross-request caching uses distributed cache

## Key Rules
- Registered as Scoped — new instance per HTTP request, auto-disposed
- No TTL needed — lives only for request duration
- Use for: auth snapshots accessed multiple times, data shared across middleware and endpoint
- MUST NOT use for data that changes during request processing
