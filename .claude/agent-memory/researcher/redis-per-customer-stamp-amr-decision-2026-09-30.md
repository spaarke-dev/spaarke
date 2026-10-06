---
name: redis-per-customer-stamp-amr-decision-2026-09-30
description: Per-customer-stamp Redis decision (2026-09-30) — Azure Cache for Redis retirement/creation-block dates, AMR SKUs/prices westus2, Entra/UAMI Bicep + .NET client, recommendation AMR Balanced_B1 HA + Entra-only.
metadata:
  type: project
---

# Per-customer Redis: Azure Cache for Redis vs Azure Managed Redis (2026-09-30)

**Question**: For new per-customer stamps (own sub, westus2, prod, UAMI everywhere) is Azure Cache for Redis Standard C1 + access keys right, or AMR + Entra?

**Findings (confirmed by source)**:
- ACR Basic/Standard/Premium retire **2028-09-30**, disabled from 2028-10-01; Enterprise/Enterprise Flash retire 2027-03-31. Creation blocked for NEW customers since **2026-04-01**; the planned 2026-10-01 block for existing customers was **removed (July 2026)**. "Existing customer" = **tenant** where any sub had an ACR cache before 2026-04-01 (Spaarke's tenant qualifies via dev caches — inferred). Source: learn cache-whats-new (updated 2026-08-18), retirement-faq.
- AMR (Microsoft.Cache/redisEnterprise) GA since May 2025; Entra ID is default on new caches; HA on by default, can be disabled only at create (cannot disable later); zone-redundant by default in AZ regions when HA; **no scale-down**; ~20% memory reserved; B0/B1 no active geo-rep; 15K connections at B0–B6.
- westus2 retail (prices API, 2026-09-30): ACR Standard C1 $0.138/h (~$101/mo). AMR per-node meters: B0 $0.016, B1 $0.032, B3 $0.065, B5 $0.156, M10 $0.216. Pricing page columns = "One Node (non-HA)" vs "Two Node (HA)" → HA ≈ 2× meter (inferred): B0 HA ~$23/mo, B1 HA ~$47/mo, B3 HA ~$95/mo.
- Bicep AMR: cluster `highAvailability`, `minimumTlsVersion`, `publicNetworkAccess` (required); database `accessKeysAuthentication: 'Disabled'`, `clusteringPolicy` default OSSCluster (immutable once OSS/Enterprise), `evictionPolicy` default VolatileLRU, port 10000; UAMI via `databases/accessPolicyAssignments` (`accessPolicyName: 'default'`, `user.objectId` = principalId) in GA 2025-07-01; `accessString` custom ACL only in 2026-x previews. ARM provider also lists API 2026-09-01 (not yet documented on Learn).
- ACR Entra: `redisConfiguration.'aad-enabled': 'true'`, `disableAccessKeyAuthentication: true`, `redis/accessPolicyAssignments` with 'Data Owner'/'Data Contributor'/'Data Reader' + objectId + objectIdAlias.
- .NET: Microsoft.Azure.StackExchangeRedis 3.3.1 (2025-12-09; deps SE.Redis ≥2.10.1, Microsoft.Extensions.Azure 1.13.1). `ConfigureForAzureWithUserAssignedManagedIdentityAsync(clientId)` — CLIENT id. Refreshes token + re-AUTH proactively. **RESP2 pub/sub connection can't be re-authed → use `Protocol = RedisProtocol.Resp3`** (BFF has pub/sub: JobStatusService, MembershipCacheInvalidator). Official SessionState sample wires IDistributedCache via `ConnectionMultiplexerFactory` (same as BFF CacheModule).
- AMR capacity pitfall: Q&A 2026-06-27 West Europe HA creates failing (backend capacity) — preflight in westus2.

**Recommendation given**: AMR Balanced_B1, HA on, OSSCluster, AllKeysLRU (parity), accessKeysAuthentication Disabled, UAMI access-policy assignment; dev = Balanced_B0 non-HA. C1 per customer is wrong (retiring product, ~2× cost, key-based).

**Code touchpoints**: CacheModule.cs (sync Connect → need async ConfigureForAzure), raw SCAN loops over GetEndPoints in ChatContextMappingService + MembershipCacheInvalidationSubscriber (cluster-safe shape, verify), RedisScheduledJobLease single-key (safe), redis.bicep listKeys outputs.

**Open questions**: API default of accessKeysAuthentication (set explicitly); exact HA billing (verify calculator); B0 vs B1 sizing needs real memory metric from dev.

Related: [[azure-managed-redis-2026-06-26]], [[reference_azure_fresh_sub_regional_gotchas]]
