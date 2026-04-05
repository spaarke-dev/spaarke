# Caching Patterns Index

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

> Pointer-based pattern files for Redis caching, request memoization, and token management.
> Each file points to canonical source code — read the code, not descriptions.

| Pattern | When to Load |
|---------|-------------|
| [distributed-cache.md](distributed-cache.md) | Adding Redis-backed cross-request caching |
| [request-cache.md](request-cache.md) | Deduplicating data loads within a single request |
| [token-cache.md](token-cache.md) | Caching OBO Graph tokens |

## Architecture
- **Production**: Redis (`ADR-009`) — `AbortOnConnectFail = false` for graceful degradation
- **Development**: In-memory fallback (`AddDistributedMemoryCache`) — same interface
- **Per-Request**: `RequestCache` (Scoped) — collapses duplicate loads within one HTTP request

## Related
- [Data Constraints](../../constraints/data.md) — Caching MUST/MUST NOT rules
