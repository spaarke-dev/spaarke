# ADR-009: Caching policy — Redis-first with per-request cache; no hybrid L1 without proof

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2025-12-04 |
| Authors | Spaarke Engineering |

## Context

A hybrid cache (`IMemoryCache` + Redis + custom HybridCacheService) adds complexity, coherence issues, and extra code paths without demonstrated benefit. SDAP's hot paths need cross-instance reuse.

## Decision

| Rule | Description |
|------|-------------|
| **Redis as L2** | Use distributed cache (Redis) as the only cross-request cache |
| **Per-request L1** | `RequestCache` (scoped) to collapse duplicate reads within a request |
| **No hybrid L1+L2** | Do not implement hybrid without profiling proof |
| **Key versioning** | Version cache keys (rowversion/etag); short TTLs for security data |

## Consequences

**Positive:**
- Simpler code and fewer invalidation bugs
- Cross-instance effectiveness; consistent behavior under scale-out

**Negative:**
- Might leave small latency on the table without L1; add later if data shows need

## Alternatives Considered

Custom HybridCacheService wrapping L1+L2. **Rejected** as premature complexity.

## Operationalization

| Pattern | Implementation |
|---------|----------------|
| Distributed cache | `IDistributedCache` + `DistributedCacheExtensions.GetOrCreateAsync(...)` |
| Per-request cache | `RequestCache` (scoped) |
| Cache targets | UAC snapshots, document metadata |
| Never cache | Authorization decisions |
| Instrumentation | Hit/miss rates, payload sizes |

## Exceptions

### Allowed L1 Caching Scenarios

| Scenario | TTL | Justification |
|----------|-----|---------------|
| Per-request (`HttpContext.Items`) | Request lifetime | Always allowed - no coherence issues |
| **Metadata caching** (entity definitions, navigation properties) | Up to 15 minutes | Metadata rarely changes; documented in code |
| Non-metadata hotspots | 1-5 seconds | Only after profiling proves Redis latency dominates p99 |

### Current L1 Implementations

| Location | Cache Type | TTL | ADR Reference |
|----------|------------|-----|---------------|
| `NavMapEndpoints.cs` | `IMemoryCache` | 15 min | Justified metadata hotspot (see code comments) |

### Requirements for New L1 Caching

1. **Must document** ADR-009 compliance in code comments
2. **Metadata only** for TTLs > 5 seconds
3. **Non-metadata** requires profiling evidence

## Success Metrics

| Metric | Target |
|--------|--------|
| Dataverse/Graph read counts | Reduced |
| Authorization latency | Stable |
| Cache staleness defects | Zero |

## Compliance

**Architecture tests:** `ADR009_CachingTests.cs` validates caching patterns.

**Code review checklist:**
- [ ] `IMemoryCache` use documents ADR-009 exception
- [ ] Metadata caching has appropriate TTL (≤15 min)
- [ ] Non-metadata L1 has profiling justification
- [ ] Authorization decisions not cached

## AI-Directed Coding Guidance

- Prefer `IDistributedCache` + `DistributedCacheExtensions.GetOrCreateAsync(...)` for cross-request caching.
- Use `RequestCache` for within-request de-dupe; do not add new ad-hoc `HttpContext.Items` caching.
- `IMemoryCache` is allowed only for explicitly documented metadata hotspots (see `NavMapEndpoints.cs`).

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-009 Concise](../../.claude/adr/ADR-009-redis-caching.md) - ~85 lines
- [Data Constraints](../../.claude/constraints/data.md) - MUST/MUST NOT rules
- [AI Caching Constraints](../../.claude/constraints/ai.md) - AI-specific caching rules

**When to load this full ADR**: Historical context, exception details, compliance checklists.
