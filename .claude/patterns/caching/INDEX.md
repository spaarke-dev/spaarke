# Caching Patterns Index

> **Domain**: Data Access & Caching
> **Last Updated**: 2025-12-19

---

## When to Load

Load these patterns when:
- Implementing distributed caching
- Adding request-scoped memoization
- Caching tokens or auth data
- Implementing idempotency for jobs
- Configuring Redis for production

---

## Cache Architecture

```
┌─────────────────────────────────────────────┐
│ Redis (Production)                          │ ← ADR-009: Redis-First
│ - GraphTokenCache (OBO tokens)              │
│ - IdempotencyService (job deduplication)    │
│ - General data caching                      │
└─────────────────────────────────────────────┘
                    ↓ (fallback)
┌─────────────────────────────────────────────┐
│ In-Memory Distributed Cache (Dev)           │
│ - Same interface as Redis                   │
│ - No distribution across instances          │
└─────────────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────────────┐
│ Request-Scoped Cache (All Environments)     │
│ - RequestCache for per-request dedup        │
│ - Collapses duplicate loads in same request │
└─────────────────────────────────────────────┘
```

---

## Available Patterns

| Pattern | Purpose | Lines |
|---------|---------|-------|
| [distributed-cache.md](distributed-cache.md) | Redis/IDistributedCache patterns | ~120 |
| [request-cache.md](request-cache.md) | Per-request memoization | ~80 |
| [token-cache.md](token-cache.md) | OBO token caching with hashing | ~100 |

---

## Canonical Source Files

| File | Purpose |
|------|---------|
| `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs` | GetOrCreate helpers |
| `src/server/shared/Spaarke.Core/Cache/RequestCache.cs` | Request-scoped cache |
| `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` | Token caching |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/IdempotencyService.cs` | Job idempotency |
| `src/server/api/Sprk.Bff.Api/Configuration/RedisOptions.cs` | Redis configuration |

---

## Key Constants

| Constant | Value | Usage |
|----------|-------|-------|
| Security data TTL | 5 minutes | UAC snapshots |
| Metadata TTL | 15 minutes | Entity schemas |
| Token TTL | 55 minutes | OBO tokens (5-min buffer) |
| Idempotency TTL | 24 hours | Job deduplication |
| Lock duration | 5 minutes | Processing locks |

---

## Cache Key Conventions

```
sdap:{category}:{identifier}[:v:{version}]

Examples:
- sdap:graph:token:{sha256hash}
- sdap:uac:user:{userId}:v:{hash}
- idempotency:processed:{eventId}
- idempotency:lock:{eventId}
```

---

**Lines**: ~80
