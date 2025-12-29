# ADR-014: AI Caching and Reuse Policy (Concise)

> **Status**: Proposed
> **Domain**: AI/ML Caching
> **Last Updated**: 2025-12-18

---

## Decision

Apply **AI-specific caching rules** on top of ADR-009 Redis-first policy. Cache derived artifacts (text, embeddings) with versioned keys; never cache raw content without governance approval.

**Rationale**: AI operations are expensive. Caching reduces cost and latency while preventing stale/unsafe reuse.

---

## Constraints

### ✅ MUST

- **MUST** use `IDistributedCache` (Redis) for cross-request caching (ADR-009)
- **MUST** use `RequestCache` for per-request collapse
- **MUST** centralize cache keys/TTLs in code (single place)
- **MUST** include version input in keys (rowVersion, ETag, model version)
- **MUST** scope keys by tenant (and user when OBO-derived)

### ❌ MUST NOT

- **MUST NOT** cache raw document bytes without ADR-015 approval
- **MUST NOT** cache streaming tokens (cache final outcome only)
- **MUST NOT** assume cache coverage (verify implementation)
- **MUST NOT** inline string keys (use centralized key builder)

---

## Cacheable Artifacts

| Artifact | Cacheable | Notes |
|----------|-----------|-------|
| File metadata | ✅ | Short TTL, tenant-scoped |
| Extracted text | ✅* | Requires ADR-015 compliance |
| Embeddings | ✅ | Long TTL, version by model + content |
| AI Search results | ✅ | Short TTL, include query hash |
| Model completions | ✅* | Version by prompt + model + content |
| Streaming tokens | ❌ | Not stable artifacts |

`✅*` requires ADR-015 data governance compliance

---

## Key Design Requirements

Keys must include:
- **Tenant identifier** (isolation)
- **Artifact category** (`ai-text`, `ai-embedding`, `ai-search`)
- **Stable identifiers** (`documentId`, `driveId:itemId`)
- **Version suffix** (`:v:{version}`)

```csharp
var key = DistributedCacheExtensions.CreateKey(
    "ai-embedding", tenantId, documentId, $"v:{rowVersion}");
```

**See**: [AI Caching Pattern](../patterns/ai/caching.md)

---

## Failure Modes

| Risk | Prevention |
|------|------------|
| Stale reuse | Versioned keys |
| Cross-tenant leakage | Tenant-scoped keys |
| Cross-user leakage | User-scoped keys for OBO content |
| Cache stampede | `GetOrCreateAsync` single-flight pattern |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-009](ADR-009-redis-caching.md) | Base caching policy |
| [ADR-013](ADR-013-ai-architecture.md) | AI architecture |
| [ADR-015](ADR-015-ai-data-governance.md) | Data governance rules |
| [ADR-016](ADR-016-ai-rate-limits.md) | Rate limiting |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-014-ai-caching-and-reuse-policy.md](../../docs/adr/ADR-014-ai-caching-and-reuse-policy.md)

---

**Lines**: ~95

