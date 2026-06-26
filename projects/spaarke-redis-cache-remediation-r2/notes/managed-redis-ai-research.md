# Azure Managed Redis — Research + Spaarke AI Service Mapping

> **Purpose**: Supplement to `design.md`. Captures the technical research on Azure Managed Redis (`Microsoft.Cache/redisEnterprise`, GA mid-2025) + an honest audit of which Spaarke AI services would actually benefit. Used to gate the R2 Phase 2 decision.
>
> **Sources**: Microsoft Learn `azure/redis/*` (cited inline). Researcher subagent findings 2026-06-26 saved at `.claude/agent-memory/researcher/azure-managed-redis-2026-06-26.md`.
>
> **Caveat upfront**: the 99.999% SLA number repeated in some marketing is **Redis Inc's claim, not Microsoft's**. Microsoft Learn's Azure SLA link goes to standard cache SLA. Be honest about what's product-marketing vs. contractual.

---

## TL;DR

Three things Managed Redis offers that Azure Cache for Redis Basic/Standard/Premium cannot (at any price):

1. **RediSearch with HNSW vector similarity** — for semantic dedup of embeddings, semantic Q&A cache, LLM conversation memory
2. **RedisJSON with JSONPath** — native JSON document storage + subfield queries
3. **RedisBloom probabilistic data structures** — for high-throughput dedup / rate-limiting at sub-microsecond cost

Plus operationally:
- **Entra ID auth across all tiers** (DEF-001 becomes trivial on Managed Redis; on Azure Cache for Redis it's Premium-tier-only)
- **Multi-thread per-node** throughput (~10× higher per-GB than OSS Redis 6.0)

What it does NOT offer that some assume:
- ❌ 99.999% Microsoft SLA — that's a Redis Inc claim
- ❌ Modules can be added later — must be enabled at create time
- ❌ 16 logical DBs — only 1 per instance (vs Basic/Standard's 16)
- ❌ Free StackExchange.Redis swap — connection string changes; TLS-or-not exclusivity breaks any non-TLS callers

---

## The pivot question for Spaarke

**Does any current Spaarke AI service need semantic similarity in cache lookups, or is everything exact-key?**

Answer based on code audit (`EmbeddingCache.cs:67`):

> Today: **everything is exact-key SHA256 match**. `EmbeddingCache.ComputeContentHash` produces `Convert.ToBase64String(SHA256(content))`. "What's the deadline?" and "When is the deadline?" produce different hashes; both pay for separate `text-embedding-3-small` calls (~$0.00002/1K tokens — small per-call, additive at scale).

This means:
- If we migrate to Managed Redis **without changing `EmbeddingCache`'s lookup model**, the RediSearch vector engine sits unused. We've paid the migration cost for arm candy.
- If we migrate to Managed Redis **AND refactor `EmbeddingCache` to semantic similarity** (HNSW + cosine threshold), we unlock real economic value — but it's a real architectural change + tuning exercise (what cosine threshold is "similar enough"?).

**The R2 decision isn't "migrate to Managed Redis". It's "introduce semantic embedding dedup, and if yes, Managed Redis is the only Azure-native fit."**

---

## Spaarke AI services — module-by-module audit

### Service-level audit

| Service | Today's cache pattern | RediSearch fit? | RedisJSON fit? | RedisBloom fit? | Verdict |
|---|---|---|---|---|---|
| `EmbeddingCache` | SHA256 exact-key | **YES** — convert to HNSW cosine similarity for semantic dedup of paraphrased prompts. Tunable threshold (e.g., cosine ≥ 0.95). | No | No | **Pivot point** — single best use case |
| `GraphTokenCache` | OBO token by user OID | No | No (token is opaque) | No | No fit |
| `MembershipResolverService` | Authorization snapshot by user | No (exact-key) | No | **Marginal** — Bloom for "have we already invalidated this user" sub-millisecond check | No fit |
| Chat session storage | JSON in Cosmos + Redis cache | No | **Marginal** — could store sessions as RedisJSON and query subfields (e.g., last 5 messages) without full deserialize | No | Nice-to-have, not load-bearing |
| Playbook config | JSON read on agent construction | No | **Marginal** — RedisJSON could expose JSONPath queries | No | Nice-to-have |
| AI tool result caching | Currently does NOT exist | No | No | **Marginal** — Bloom for "have we seen this tool call signature in last hour" rate-limit / dedup | Speculative |
| `InboundPollingBackupService` (email polling) | Watermark in Redis | No | No | **Marginal** — Bloom for "have we processed this MessageId" idempotency in case of poll overlap | Marginal — but our current pattern works |

### Strict reading of the audit

- **1 strong fit**: `EmbeddingCache` semantic dedup via RediSearch
- **0 medium fits**: nothing else clears the "concrete economic failure mode" bar per CLAUDE.md §11
- **4 marginal fits**: nice-to-have refactors that don't justify the migration on their own

If R2 ships Managed Redis purely for the marginal fits, that's bloat. If R2 ships Managed Redis for the EmbeddingCache opportunity, it's load-bearing.

---

## EmbeddingCache semantic dedup — quantifying the opportunity

To decide GO/NO-GO on Managed Redis, we need to know whether Spaarke's actual prompt distribution has enough linguistic variance to make semantic dedup pay off. Pre-decision audit (Phase 2 of R2):

### Audit plan

```kql
// In App Insights, look at the embedding cache miss rate over a 30-day window
customMetrics
| where timestamp > ago(30d)
| where name == 'cache.misses'
| extend resource = tostring(customDimensions.resource)
| where resource == 'embedding'  // when I-1 resource tag is restored
| summarize misses=sum(value) by bin(timestamp, 1d)
```

If we see N misses/day, the question becomes: of those misses, how many are **semantically similar to a hit within the last 7d**?

To answer that without deploying RediSearch first:
1. Export 7 days of prompts (anonymized) that produced embedding cache misses
2. Re-embed them via `text-embedding-3-small`
3. Run pairwise cosine similarity (offline, in a notebook)
4. Count pairs with cosine ≥ 0.95 (typical "semantic equivalence" threshold for sentence-level embeddings)
5. If ≥ 30% of misses have a semantic-near match, semantic dedup is a real economic win — proceed to Managed Redis
6. If < 10%, defer — prompt distribution is too unique, semantic dedup won't move the needle

The threshold (30%) is a judgment call; happy to discuss. The audit itself is ~half a day of work — doable in Phase 2 of R2 before any migration cost is incurred.

### Cost framing

At Spaarke's current scale (estimate ~50-100 embedding ops/sec peak, ~5K-10K/day):
- ACR Basic C0 cost: ~$15/mo. Embedding API cost dominant.
- 7-day exact-hash cache hit rate is presumably already 60-80% (would need to measure).
- Adding semantic dedup with cosine ≥ 0.95 might lift hit rate by 5-20 percentage points (real-world papers show similar deltas for chatbot Q&A workloads).
- 10 percentage-point hit rate improvement on 5K calls/day = 500 extra hits/day = 500 × $0.00002 saved = $0.01/day = $3.65/year.

At this scale, semantic dedup is **not economic**. The migration cost isn't worth $3.65/year.

At hypothetical prod scale (500K-1M embedding ops/day) — sister projects' ambitions:
- 10 pct point improvement = 50K-100K extra hits/day = $1-$2/day = **$365-$730/year savings**
- Plus latency improvement (in-Redis vector lookup ≈ 1-5ms vs OpenAI API ≈ 50-200ms)

**Honest read**: semantic dedup is economic only after Spaarke scales 100×. Today: defer. After Spaarke hits 500K+ embedding calls/day: real win.

---

## Module use cases (deeper detail)

### RediSearch vector similarity
- **Index spec**: HNSW or FLAT, cosine/L2/inner-product distance
- **Use case for Spaarke (theoretical)**: store embeddings in RediSearch index; on cache lookup, find top-K nearest neighbors; if nearest has cosine ≥ threshold, return its cached completion/embedding
- **Operational constraint**: requires Enterprise cluster policy + NoEviction policy (i.e., explicit memory sizing; no LRU)
- **Module availability**: Memory Optimized / Balanced / Compute Optimized tiers (NOT Flash Optimized in current GA)
- **Reference**: [Microsoft Learn — Vector similarity tutorial](https://learn.microsoft.com/en-us/azure/redis/tutorial-vector-similarity)

### RedisJSON
- **Use case for Spaarke**: store chat sessions as JSON with JSONPath query for subfield ops (e.g., "give me the last 5 messages without deserializing the whole session")
- **Operational**: works on ALL tiers including Flash. Compatible with active-active geo-replication.
- **Concrete value**: marginal. Current chat session pattern (read whole JSON from Redis + deserialize) works fine and the dev cost to refactor isn't justified by the marginal benefit.

### RedisBloom
- **Use case for Spaarke**: high-throughput dedup of "have I seen this email MessageId" / "have I emailed this user today" / "is this tool call signature recent"
- **Operational**: works on Memory Optimized / Balanced / Compute Optimized
- **Concrete value**: marginal. Current dedup patterns (Cosmos lookup or in-memory dict) work at Spaarke's scale.

---

## Operational concerns to verify before migrating (per researcher findings)

1. **StackExchange.Redis version**: confirm Spaarke's pinned version (currently `Microsoft.Extensions.Caching.StackExchangeRedis 10.0.1`) is compatible with `Microsoft.Azure.StackExchangeRedis` (the official Entra ID extension)
2. **Non-TLS internal callers**: Managed Redis is TLS-or-not exclusive (single port). Audit any caller (test fixture, validation script, monitoring task) that connects to Redis without TLS — all break at cutover
3. **Logical DB usage**: Managed Redis has 1 logical DB per instance (vs Basic/Standard's 16). Grep codebase for `ConfigurationOptions.DefaultDatabase`, `IDatabase.SelectDatabase()`, or connection-string `,defaultDatabase=N`
4. **Region availability**: Managed Redis is in fewer regions than Azure Cache for Redis. Verify West US 2 / East US 2 availability via [products-by-region table](https://azure.microsoft.com/explore/global-infrastructure/products-by-region/table)
5. **Pricing**: confirm B0 price for West US 2 / East US 2 via portal — researcher cited ~$13/mo but the public pricing page enumerates by region

---

## Reference implementations

These are the highest-signal references for a real .NET implementation:

- **[Vector similarity tutorial (Python + .NET)](https://learn.microsoft.com/en-us/azure/redis/tutorial-vector-similarity)** — Azure Managed Redis + Azure OpenAI + LangChain; most relevant for an `EmbeddingCache` refactor
- **[Azure-Samples/azure-redis-dalle-semantic-caching](https://github.com/Azure-Samples/azure-redis-dalle-semantic-caching)** — .NET semantic cache with Redis OM, DALL-E gallery; closest in stack to Spaarke
- **[Microsoft.Azure.StackExchangeRedis](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis)** — official .NET extension for Entra ID auth; required for DEF-001 on Managed Redis

---

## Net recommendation for R2

| Decision path | When it's right |
|---|---|
| **Path A — Ship cache observability hardening; defer Managed Redis** | Today. Spaarke is single-region and below the scale where semantic embedding dedup pays. Hardening ships in 2-3 days; observability is value the team uses every day. |
| **Path B — Ship hardening + Managed Redis with semantic embedding** | When the audit shows ≥30% semantic-similar misses on real prompt distribution, OR when scale hits 500K+ embedding calls/day, OR when Phase 3 sister-project AI workloads need RediSearch/JSON/Bloom for distinct architectural reasons (NOT speculative). |
| **Path C — Defer Managed Redis indefinitely; ship Entra ID auth on ACR Premium** | If Path A audit clears Managed Redis as not-economic AND we still want DEF-001 (Entra ID), upgrade ACR dev/prod to Premium tier and ship Entra ID there. ~$485/mo cost delta; not justified by Entra ID alone. |

The R2 design.md adopts **Path A as default** with **Path B as a conditional Phase 3 gated on the audit outcome**. If the audit comes back negative, Phase 3 doesn't fire and we save the migration cost.
