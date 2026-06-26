# Managed Redis migration — Decision Record

> **Decision**: NO. Spaarke stays on Azure Cache for Redis (`Microsoft.Cache/Redis`, OSS Redis 6.0) for the foreseeable future.
> **Decided**: 2026-06-26
> **Status**: Final (informal — no formal ADR amendment required; the existing ADR-009 SKU table already lists Azure Cache for Redis tiers)
> **Closes**: [#466 DEF-005](https://github.com/spaarke-dev/spaarke/issues/466)
> **Decided by**: Project owner (Ralph Schroeder)

---

## Decision

Don't migrate Spaarke's BFF cache from **Azure Cache for Redis** to **Azure Managed Redis** at this time.

## Rationale

Azure Managed Redis is a **high-throughput enterprise solution**. Its differentiating features are real but address scale and architecture profiles Spaarke doesn't have today:

- **RediSearch vector similarity** — would only pay off if `EmbeddingCache` were refactored to semantic dedup. Today the cache is exact-key SHA256 (per code audit in [`managed-redis-ai-research.md`](managed-redis-ai-research.md) §EmbeddingCache). At current scale (~5-10K embedding calls/day) the economic value of semantic dedup is ~$3.65/year. Worth revisiting only after ~100× scale-up.
- **RedisJSON / RedisBloom** — marginal fits for Spaarke's chat session storage / rate limiting / dedup patterns. Current implementations work fine; refactor cost outweighs benefit.
- **All-tier Entra ID auth** — was the strongest operational draw, but R2 Theme B (key rotation automation) solves the same root problem (90-day rotation slipping) without the migration cost.
- **Active-active geo-replication** — irrelevant; Spaarke BFF is single-region.
- **99.999% SLA** — that's Redis Inc marketing, not a Microsoft contractual commitment. Microsoft Learn's Azure SLA link goes to standard cache SLA.

The migration cost is real (architectural changes, module-decision-at-create-time constraint, TLS-or-not exclusivity surfacing latent issues, one-logical-DB limitation requiring code audit). It's not justified by any current Spaarke pain point.

## Conditions that would trigger a revisit

- Spaarke's prompt distribution shifts such that semantic-dedup of embeddings would save real money (decision rule was ≥30% of embedding cache misses being cosine ≥ 0.95 to a recent hit, at projected prod scale)
- Spaarke commits to multi-region deployment (active-active is dramatically simpler on Managed Redis)
- An AI workload emerges that genuinely needs RediSearch / RedisJSON / RedisBloom for an architectural reason — not "would be cool to have"
- Microsoft adds a Managed Redis feature that materially affects Spaarke's threat model or cost basis (e.g., a true 99.999% Microsoft SLA, or a feature that addresses a current pain point)

## What we keep instead

- **Azure Cache for Redis Basic C0** for dev (~$15/mo, ADR-009 SKU table)
- **Future prod** sizing per ADR-009: Standard C2+ or Premium P1+ depending on traffic
- **Manual 90-day key rotation** replaced by R2 Theme B automation
- The existing R7-S7 observability pipeline (no Managed Redis dependency)

## Background

See [`managed-redis-ai-research.md`](managed-redis-ai-research.md) for the full technical research that informed this decision. That document is retained for future reference if the conditions above ever trigger a revisit — saves repeating the research effort.

## GitHub Issue lifecycle

[#466 DEF-005](https://github.com/spaarke-dev/spaarke/issues/466) will be closed Won't Fix with a link to this decision record. Per `/project-defer-issue-tracking` protocol, the issue's title remains in the GitHub Issues list as a historical record of the decision.
