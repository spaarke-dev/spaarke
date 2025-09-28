# ADR-009: Caching policy — Redis-first with per-request cache; no hybrid L1 without proof
Status: Accepted
Date: 2025-09-27
Authors: Spaarke Engineering

## Context
A hybrid cache (`IMemoryCache` + Redis + custom HybridCacheService) adds complexity, coherence issues, and extra code paths without demonstrated benefit. SDAP’s hot paths need cross-instance reuse.

## Decision
- Use **distributed cache (Redis)** as the only cross-request cache.
- Add a tiny **per-request cache** to collapse duplicate reads within one request.
- Do **not** implement a hybrid L1+L2 cache. Consider an L1 only if profiling proves Redis round-trips dominate p99 latency.
- Version cache keys (e.g., rowversion/etag) and keep short TTLs for security-sensitive data.

## Consequences
Positive:
- Simpler code and fewer invalidation bugs.
- Cross-instance effectiveness; consistent behavior under scale-out.
Negative:
- Might leave a small amount of latency on the table without L1; add later if data shows need.

## Alternatives considered
- Custom HybridCacheService wrapping L1+L2. Rejected as premature complexity.

## Operationalization
- Use `IDistributedCache` directly with small helper extensions (`GetOrCreateAsync`).
- Cache **snapshots** for UAC and document metadata; **do not** cache authorization decisions.
- Add per-request single-flight cache via `HttpContext.Items` to avoid repeated loads by multiple rules.
- Instrument hit/miss and payload sizes.

## Exceptions
Consider an opt-in L1 for specific hotspots after profiling, with very short TTLs (1–5s).

## Success metrics
- Reduced Dataverse/Graph read counts; stable authorization latency.
- No staleness defects traced to cache coherence.
