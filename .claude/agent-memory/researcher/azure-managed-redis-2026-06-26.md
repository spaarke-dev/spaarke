---
name: azure-managed-redis-2026-06-26
description: Azure Managed Redis (Microsoft.Cache/redisEnterprise) deep research for Spaarke BFF AI cache migration decision — tiers, modules, throughput, Entra auth, migration, anti-recommendations.
metadata:
  type: project
---

# Azure Managed Redis research for spaarke-redis-cache-remediation-r2

**Date**: 2026-06-26
**For**: Spaarke BFF (.NET 8) deciding whether to migrate dev + prod from Azure Cache for Redis Basic C0 (OSS 6.0) to Azure Managed Redis (Redis Enterprise 7.4.x).

**Why**: R1 just landed on Basic C0; the team wants to know if R2 should jump to Managed Redis to get vector search, JSON, Bloom, and Entra ID auth as a single packaged service. Concerns: bloat / arm candy vs load-bearing.

## Authoritative findings (Microsoft Learn only)

### Architecture
- Runs **Redis Enterprise** (not OSS). Each node runs multiple shards (Redis processes) in parallel — multi-threaded. OSS Redis is single-threaded per process. This is the source of the throughput-per-GB delta.
- High-perf proxy per node manages shards, connections, self-heal.
- Three cluster policies: **OSS** (highest throughput, requires Redis Cluster API client), **Enterprise** (single endpoint, simpler, **REQUIRED for RediSearch**), **Non-clustered** (≤25 GB, like Basic/Standard).
- ~20% memory reserved per instance for buffers/failover/geo-rep sync.
- Source: https://learn.microsoft.com/en-us/azure/redis/architecture

### Tiers (all support Entra ID, replication, scaling, persistence)
- **Memory Optimized** — 8:1 mem:vCPU, low throughput, cheapest in-memory tier; **the doc explicitly names this as "excellent choice for development and testing"**.
- **Balanced** — 4:1 ratio (B0–B1000). B0 = 0.5 GB, B1 = 1 GB. B0/B1 do NOT support active geo-replication.
- **Compute Optimized** — 2:1 ratio, max throughput.
- **Flash Optimized** — NVMe + RAM hybrid, 250+ GB only, NO RediSearch, NO active geo-rep, NO non-clustered. Hot keys on RAM, cold on NVMe.
- All tiers GA up to 350 GB; >350 GB is preview.
- SLA: standard cache SLA at https://azure.microsoft.com/support/legal/sla/cache/v1_0/ (not 99.999% — that's a Redis Inc marketing number; Microsoft Learn does not claim five nines).

### Modules (Spaarke-relevant fit)

| Module | Tier coverage | Relevance to Spaarke |
| --- | --- | --- |
| RediSearch | MO, B, CO (NOT Flash). **Requires Enterprise cluster policy + NoEviction policy** | EmbeddingCache could become a real vector index (HNSW/FLAT, cosine/L2/IP, hybrid filter). |
| RedisJSON | All tiers including Flash | Chat session state, playbook config — JSONPath set/get on subfields without GET/parse/SET roundtrip. |
| RedisBloom | MO, B, CO | Bloom/Cuckoo for "have I emailed this user", Top-K for "most-used tool", count-min for rate limiting. |
| RedisTimeSeries | MO, B, CO | OTel metric history alternative (not core Spaarke need). |
| Active geo-rep modules: **only RediSearch + RedisJSON** work concurrently with active-active. |

Modules must be enabled **at create time** — cannot be added later. Cannot manually load other modules. No `FT.CONFIG` runtime — pass args via ARM/CLI at create time.

Source: https://learn.microsoft.com/en-us/azure/redis/redis-modules

### Performance (memtier_benchmark, GET, 1KB, OSS cluster policy)

| Size GB | Balanced GET/sec | Compute Opt GET/sec |
| --- | --- | --- |
| 0.5 (B0) | 120,000 | n/a |
| 1 (B1) | 120,000 | n/a |
| 3 (B3 / X3) | 230,000 | 480,000 |
| 12 | 480,000 | 810,000 |

For comparison: **Azure Cache for Redis Basic C0 is documented at ~16K GET/sec headroom**. So even Balanced B0 is ~7-8x the throughput of C0 at the same 0.5 GB size point.

Source: https://learn.microsoft.com/en-us/azure/redis/best-practices-performance

### Entra ID auth (all tiers)
- Default on new caches. Token scope: `https://redis.azure.com/.default` or `acca5fbb-b7e4-4009-81f1-37e38fd66d78/.default`.
- .NET client: `Microsoft.Azure.StackExchangeRedis` NuGet extension on top of `StackExchange.Redis`. Handles token refresh.
- 3-line setup:
  ```csharp
  var opts = ConfigurationOptions.Parse($"{host}:6380");
  await opts.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential());
  var mux = await ConnectionMultiplexer.ConnectAsync(opts);
  ```
- Microsoft Entra groups NOT supported. SSL only. Custom data-access permissions (RBAC scoped to keys/commands) is preview.
- Sources: https://learn.microsoft.com/en-us/azure/redis/entra-for-authentication ; https://github.com/Azure/Microsoft.Azure.StackExchangeRedis

### Active geo-rep (CRDT-based, active-active)
- Up to 5 instances, all primary, bi-directional.
- CRDT conflict resolution. Eventual consistency, **no SLA on sync time**.
- B0/B1 and Flash Optimized EXCLUDED.
- Only RediSearch + RedisJSON modules work with it.
- All caches in group must have identical SKU/capacity/eviction/clustering/modules/TLS.
- FLUSHALL/FLUSHDB blocked — use Flush Cache(s) button.
- Microsoft currently absorbs cross-region bandwidth charges ("can change in the future").
- Source: https://learn.microsoft.com/en-us/azure/redis/how-to-active-geo-replication

### Migration from Azure Cache for Redis
- StackExchange.Redis IS compatible — only connection string changes for most apps.
- TLS gotcha: Managed Redis supports ONE mode at a time (TLS XOR non-TLS), unlike Basic/Standard which support both ports.
- One database per instance (Basic/Standard allowed 16).
- Migration agent skill (preview): https://github.com/AzureManagedRedis/amr-migration-skill
- Sources: https://learn.microsoft.com/en-us/azure/redis/migrate/migrate-basic-standard-premium-overview ; https://learn.microsoft.com/en-us/azure/redis/migrate/migrate-basic-standard-premium-understand

### Vector embeddings official tutorial
- https://learn.microsoft.com/en-us/azure/redis/tutorial-vector-similarity (LangChain + Azure OpenAI embeddings)
- https://github.com/Azure-Samples/azure-redis-dalle-semantic-caching (.NET semantic cache w/ Redis OM)
- Vector search on Azure Managed Redis listed as canonical option in Azure Architecture Center vector-search guide.

### Pricing (signal, not gospel — verify on the pricing page)
- Balanced B0 (0.5 GB): ~$13/month (reported in Microsoft Q&A discussion; needs portal verification for West US 2 / East US 2).
- B3 with HA experienced ~3x billing correction Jan 2026 after replica metering fix — note: B3 with HA is two-node, ~$0.20-0.40/hr range.
- Azure Cache for Redis Basic C0 (250 MB): ~$16/month; Standard C0 (replicated): ~$40/month.
- Net signal: **Managed Redis B0 is in the same order of magnitude as Standard C0**, NOT cheaper than Basic. The savings story is "modules are bundled" — RediSearch, RedisJSON, RedisBloom otherwise are not separately purchasable on Basic/Standard/Premium tier at all (only on now-deprecated Enterprise tier).
- Source: https://azure.microsoft.com/en-us/pricing/details/managed-redis/

## Anti-recommendations (when NOT to use)

1. **Dev C0 currently costs ~$16/mo and works** — paying ~$13-30/mo for B0 only buys value if Spaarke actually exercises modules in dev. If `EmbeddingCache` stays as plain k/v IDistributedCache, modules don't help and Basic C0 is fine.
2. **Single-DB limitation** — code that uses Redis logical DB index separation needs refactor (Spaarke doesn't seem to, but check `IDistributedCache` usage).
3. **TLS-or-not, not both** — if any internal client connects via non-TLS today, that path breaks at cutover.
4. **B0/B1 are crippled for prod** — no active geo-rep, lowest connection cap (15K). For multi-region BFF, must jump to B3+ which is materially more expensive.
5. **Enterprise cluster policy required for RediSearch** = single-proxy bottleneck at very high QPS (acceptable for Spaarke scale).
6. **Modules locked at create time** — must declare RediSearch/JSON/Bloom up front; can't enable later without rebuild + import.
7. **Modules manually configurable only via ARM/CLI at create** (no `FT.CONFIG` runtime).

## Spaarke-specific verdicts (my opinion as researcher, caller decides)

| Spaarke component | Managed Redis pays off? | Why |
| --- | --- | --- |
| GraphTokenCache | No | Plain k/v with TTL. C0 is fine. |
| EmbeddingCache | **YES (if real semantic dedup)** | RediSearch HNSW = "have I embedded similar text" lookup in O(log n). Otherwise no. |
| MembershipResolverService | No | Plain k/v. |
| Chat session storage | **Maybe** | RedisJSON makes per-turn subfield update + JSONPath retrieval cleaner; not load-bearing. |
| Playbook configs | Maybe | RedisJSON nice-to-have for $.tools[?(@.name=="x")] patterns. |
| AI tool definitions | No | Static, infrequent. |
| Dashboard sync results | No | Plain k/v list. |
| Rate limiting (cross-cutting) | Yes (Bloom/Top-K) | RedisBloom replaces home-grown sliding window for "per-user N requests in 60s" with O(1) probabilistic. |

## Open questions for caller
- Does EmbeddingCache actually do (or want to do) semantic dedup, or just exact-hash key match? Answer = pivot point for the entire migration decision.
- Is there a real need for active-active geo-rep, or is BFF single-region today? If single-region: pay for B3+ feature buys nothing.
- Confirm `Microsoft.Azure.StackExchangeRedis` extension is compatible with current `StackExchange.Redis` version in Spaarke (likely yes; verify before R2 spec lock).

## Sources consulted
- https://learn.microsoft.com/en-us/azure/redis/overview
- https://learn.microsoft.com/en-us/azure/redis/architecture
- https://learn.microsoft.com/en-us/azure/redis/redis-modules
- https://learn.microsoft.com/en-us/azure/redis/best-practices-performance
- https://learn.microsoft.com/en-us/azure/redis/how-to-active-geo-replication
- https://learn.microsoft.com/en-us/azure/redis/entra-for-authentication
- https://learn.microsoft.com/en-us/azure/redis/overview-vector-similarity
- https://learn.microsoft.com/en-us/azure/redis/development-faq
- https://learn.microsoft.com/en-us/azure/redis/migrate/migrate-basic-standard-premium-overview
- https://learn.microsoft.com/en-us/azure/redis/migrate/migrate-basic-standard-premium-understand
- https://github.com/Azure/Microsoft.Azure.StackExchangeRedis
- https://github.com/Azure-Samples/azure-redis-dalle-semantic-caching
- https://azure.microsoft.com/en-us/pricing/details/managed-redis/
