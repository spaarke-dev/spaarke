# ADR-009: Redis-First Caching (Concise)

> **Status**: Accepted
> **Domain**: Data/Caching
> **Last Updated**: 2025-12-18

---

## Decision

Use **Redis as distributed cache**. Per-request cache for within-request de-dupe. No hybrid L1+L2 without profiling proof.

**Rationale**: Hybrid caching adds complexity and coherence issues without demonstrated benefit.

---

## Constraints

### ✅ MUST

- **MUST** use `IDistributedCache` for cross-request caching
- **MUST** use `RequestCache` for within-request de-dupe
- **MUST** version cache keys (rowversion/etag)
- **MUST** use short TTLs for security data
- **MUST** document ADR-009 exception for any `IMemoryCache` use

### ❌ MUST NOT

- **MUST NOT** cache authorization decisions (cache data only)
- **MUST NOT** add L1 cache without profiling proof
- **MUST NOT** use `IMemoryCache` for non-metadata without justification

---

## Implementation Pattern

### Distributed Cache (Default)

```csharp
// ✅ DO: Use distributed cache
var metadata = await _cache.GetOrCreateAsync(
    $"doc-metadata:{docId}:v{rowVersion}",
    async () => await _dataverse.GetDocumentMetadataAsync(docId),
    TimeSpan.FromMinutes(5));
```

### Per-Request Cache

```csharp
// ✅ DO: Use RequestCache for request-scoped de-dupe
var snapshot = await _requestCache.GetOrCreateAsync(
    "uac-snapshot",
    async () => await _accessDataSource.GetSnapshotAsync());
```

### Allowed L1 Exceptions

| Scenario | TTL | Requirement |
|----------|-----|-------------|
| Per-request (`HttpContext.Items`) | Request | Always OK |
| Metadata (entity definitions) | ≤15 min | Document in code |
| Non-metadata hotspots | 1-5s | Profiling evidence required |

**See**: [Caching Pattern](../patterns/data/redis-caching.md)

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-003](ADR-003-authorization-seams.md) | Cache snapshots, not decisions |
| [ADR-010](ADR-010-di-minimalism.md) | No hybrid cache services |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-009-caching-redis-first.md](../../docs/adr/ADR-009-caching-redis-first.md)

---

**Lines**: ~85
