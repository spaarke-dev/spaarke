# spaarke-redis-cache-remediation-r2 — Design

> **Last Updated**: 2026-06-26
> **Status**: Draft (design)
> **Owner**: spaarke-dev
> **Predecessor**: [`spaarke-redis-cache-remediation-r1`](../spaarke-redis-cache-remediation-r1/) (R7-S7 closure shipped; 6 items filed as GitHub Issues #462–#467)
> **Supplement**: [`notes/managed-redis-ai-research.md`](notes/managed-redis-ai-research.md) — Managed Redis research + Spaarke AI service audit (drives the Phase 2 decision)

---

## TL;DR

R2 ships two independent things:

1. **Cache observability hardening** (Phase 1, ~3 days) — fixes real gaps the R1 senior review surfaced. Ships without waiting on any decision.
2. **Managed Redis decision gate** (Phase 2, ~3 days) — quantitative audit of whether Spaarke's prompt distribution makes `EmbeddingCache` semantic dedup economic. **If yes**, Phase 3 migrates dev to Azure Managed Redis with RediSearch + folds in Entra ID auth (DEF-001). **If no**, Phase 3 ships Entra ID auth on Azure Cache for Redis Premium only (DEF-001 standalone) or defers DEF-001.

Items deliberately CUT from R2 scope: multi-region (DEF-003), Pub/Sub separation (DEF-002), non-Redis cleanup (DEF-004, DEF-006), `customer.bicep` per-customer Redis call cleanup. See §6 for rationale.

---

## 1. Why R2

The R1 senior review uncovered four cache/Redis items that have **concrete failure modes** in production and need fixing before Spaarke leans on the new cache layer for incident response:

| # | Issue | Concrete failure mode |
|---|---|---|
| C-1 | `MetricsDistributedCache` doesn't track failures | Redis timeout / connection drop / auth failure produces **zero telemetry**. Operators see "no traffic" not "high failures" during outage. Incident MTTR longer. |
| I-1 | `resource` dimension silently disappeared | Any pre-existing dashboard or alert filtering `cache.hits` by `resource` is broken since R7-S7 moved emission to the decorator layer. |
| I-2 | Two `Meter("Sprk.Bff.Api.Cache")` instances claim the same name | Discovered during this audit: `TenantCache` static fields + `CacheMetrics` class both create `cache.hits` / `cache.misses` instruments on the same Meter. App Insights may merge or duplicate counts. Currently producing ambiguous data. |
| I-3 | Three alerts documented as markdown only | Hit-rate <80% / P95 >100ms / memory >80% — designed in R1 but NOT deployed via Bicep. The new metrics are dashboards-only, not actionable. |

Separately, the research findings on Azure Managed Redis raise a strategic question: **before any prod-tier Redis provisioning happens**, evaluate whether Spaarke should migrate from `Microsoft.Cache/Redis` (legacy product) to `Microsoft.Cache/redisEnterprise` (Managed Redis, GA mid-2025). The honest answer depends on whether `EmbeddingCache` semantic dedup is economic at Spaarke's scale — quantifiable via a half-day audit before any migration cost is incurred.

R2 ships the four observability fixes unconditionally and gates the Managed Redis decision on the audit.

---

## 2. Phase 1 — Cache observability hardening (in scope, no decision dependency)

All five items below ship in **one PR** to keep the cache observability surface coherent. Estimated effort: 2-3 days end-to-end including test + deploy + KQL re-verification.

### 1.1 `cache.failures` counter + try/finally on every op

**Source**: senior review C-1 (would be DEF-007). [`MetricsDistributedCache.cs:49-56`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Cache/MetricsDistributedCache.cs#L49-L56) and 7 other op methods. Today an exception from the inner cache skips `sw.Stop()` and the metric record entirely.

**Change**: wrap every op in `try/finally`. Add a `cache.failures` Counter on the same Meter with `outcome` dimension (`timeout` / `canceled` / `connection` / `other`). Classify exceptions in a small `ClassifyException` helper. Verify via KQL after deploy.

### 1.2 Meter ownership consolidation

**Source**: senior review I-2 (expanded). Two independent `Meter("Sprk.Bff.Api.Cache")` instances exist in the codebase:
- [`TenantCache.cs:28-32`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Cache/TenantCache.cs#L28-L32) — static fields, used by `MetricsDistributedCache`
- [`Telemetry/CacheMetrics.cs:25-40`](../../src/server/api/Sprk.Bff.Api/Telemetry/CacheMetrics.cs#L25-L40) — instance class, injected into `EmbeddingCache`, `GraphTokenCache`

Both create `cache.hits` / `cache.misses` on the same Meter name. App Insights may merge or double-count.

**Change**: collapse to a single `Sprk.Bff.Api.Telemetry.CacheMetrics` static class that owns the Meter + instruments. `MetricsDistributedCache` and any AI-service that wants additional dimensions emit via it. Remove the static fields from `TenantCache`. Verify only one Meter instance exists at runtime.

### 1.3 `resource` dimension restoration (wrapper-layer)

**Source**: senior review I-1. The R7-S7 decorator emission lost the `resource` tag because keys are opaque at the decorator layer. Wrapper-layer code knows the resource.

**Change**: have `TenantCache` re-emit a *secondary* `cache.hits.by_resource` / `cache.misses.by_resource` counter set with `resource` dimension (bounded to ~10-20 known resources). Keep the primary `cache.hits` / `cache.misses` from the decorator as the canonical raw count. This avoids the duplicate-emission problem because the new instruments have different names.

Alternative considered: parse `resource` from the key prefix in the decorator (e.g., `spaarke:tenant:{tid}:{resource}:...`). Rejected — fails on system-cache exceptions (`comm:accounts:*`, MSAL `appcache-{appId}`) without an allow-list, and the allow-list is the same dev cost as wrapper-layer emission with cleaner cardinality.

### 1.4 Bicep-deployed alerts (3 minimum)

**Source**: senior review I-3 (would be DEF-008). R1 task 043 marked ✅ with "ready-to-paste Bicep skeletons" — but the alerts are markdown only. Without Bicep deploy, hit-rate <80% / P95 >100ms / memory >80% all fail silently.

**Change**: new `infrastructure/bicep/alerts.bicep` with the 3 alert rules. Bicep deploy via `Deploy-RedisCache.ps1 -DeployAlerts`. Smoke test by manually triggering each condition (e.g., scale Redis to evict all keys → confirm hit-rate alert fires). KQL verify alert state in `azureActivity`.

### 1.5 Decorator regression integration test

**Source**: senior review N-1. The `CacheModule.DecorateDistributedCacheWithMetrics` does `services.Remove(...) + AddSingleton(...)`. If Microsoft changes the `IDistributedCache` registration shape in a future package version (e.g., keyed singleton, different lifetime), the decorator wiring silently fails OR double-registers.

**Change**: new `tests/integration/Sprk.Bff.Api.Tests.Integration/Cache/MetricsDistributedCacheRegistrationTests.cs` — spins up full DI graph, resolves `IDistributedCache`, asserts it's a `MetricsDistributedCache` wrapping the expected inner type. Runs in CI.

### Phase 1 acceptance criteria

- [ ] `dotnet build` clean; `dotnet test` matches baseline (~7885 pass)
- [ ] `Deploy-BffApi.ps1` clean — 4/4 critical DLLs hash-verified
- [ ] `customMetrics | where name == 'cache.failures'` returns ≥1 row within 10 min of a forced Redis disconnect
- [ ] `customMetrics | where name == 'cache.hits.by_resource'` returns ≥1 row with `resource` tag populated
- [ ] `gh api repos/spaarke-dev/spaarke/branches/master/protection` — all required checks passing
- [ ] At least one alert rule visible via `az monitor metrics alert list -g rg-spaarke-dev` referencing the new metrics
- [ ] Decorator integration test passes; runs in CI

---

## 3. Phase 2 — Managed Redis decision gate (3 days)

Five pre-decision audits MUST complete before any Managed Redis migration cost is incurred. Each is short and concrete.

### 2.1 EmbeddingCache semantic dedup economic audit (the pivot)

Per [`notes/managed-redis-ai-research.md`](notes/managed-redis-ai-research.md) §EmbeddingCache opportunity — answer the question "does Spaarke's prompt distribution benefit from semantic dedup?"

**Method**:
1. Export 7 days of prompts (anonymized via SHA256) that produced embedding cache misses from `App Insights`
2. Re-embed via `text-embedding-3-small` (cost: ~$0.01 for 7 days of misses at current scale)
3. Pairwise cosine similarity in a Jupyter notebook (`projects/spaarke-redis-cache-remediation-r2/notes/embedding-audit/`)
4. Compute %-of-misses with cosine ≥ 0.95 to a recent hit

**Decision rule**:
- If ≥ 30% near-matches → **GO** on Managed Redis (Phase 3 fires)
- If 10-30% near-matches → defer; revisit at next scale milestone
- If < 10% near-matches → **NO-GO** on Managed Redis; defer DEF-001 to ACR Premium upgrade or wait

### 2.2 StackExchange.Redis compatibility audit

**Method**: grep for `ConfigurationOptions`, `ConnectionMultiplexer`, `IDatabase.SelectDatabase`, `,defaultDatabase=`. Confirm:
- `Microsoft.Extensions.Caching.StackExchangeRedis 10.0.1` is compatible with `Microsoft.Azure.StackExchangeRedis` (the Entra ID extension)
- No code uses logical DB indexes 1-15 (Managed Redis has 1 DB only)
- All Redis connections use TLS (Managed Redis is TLS-or-not exclusive)

### 2.3 Non-TLS callers audit

**Method**: grep for `:6379` (non-TLS Redis port) across `src/`, `tests/`, `scripts/`, `infrastructure/`. Each hit must be either upgraded to `:6380` (TLS) or explicitly documented as out-of-band.

### 2.4 Region availability + pricing confirmation

**Method**: Portal check (not Microsoft Learn — it's not authoritative for region availability or current pricing):
- Managed Redis available in West US 2 / East US 2?
- B0 (0.5 GB) actual monthly price for West US 2 (researcher cited ~$13/mo — verify)
- B3 (HA prod baseline) actual monthly price for the target prod region

Output: spreadsheet `notes/managed-redis-cost-vs-acr.md`.

### 2.5 Modules required-at-create-time decision

If Phase 3 fires, decide BEFORE provisioning which modules to enable:
- **RediSearch** (required for EmbeddingCache semantic dedup)
- **RedisJSON** (optional — chat session storage opportunity)
- **RedisBloom** (optional — speculative use cases)

Modules cannot be added post-create. The conservative call is "RediSearch only" — minimize attack surface, avoid Pyrrhic unused-module cost. RedisJSON / RedisBloom can be added in R3 by creating a parallel instance.

### Phase 2 acceptance criteria

- [ ] Audit notebook produces a definitive number for "% of embedding misses with semantic near-match"
- [ ] Decision memo at `notes/managed-redis-go-no-go.md` cites the audit + decision rule + outcome
- [ ] StackExchange.Redis / non-TLS / logical-DB audits all green OR have explicit remediation items
- [ ] Region + pricing confirmed in writing

---

## 4. Phase 3 — Conditional: Managed Redis migration + EmbeddingCache semantic refactor (GO path only, 2 weeks)

**Fires only if Phase 2.1 decision rule says GO.**

### 3.1 Provision Managed Redis with RediSearch enabled
- New `infrastructure/bicep/managed-redis.bicep` (parallel to `redis.bicep`)
- Modules enabled at create: `RediSearch` (+ `RedisJSON` if 2.5 decided to include)
- Cluster policy: Enterprise (required for RediSearch)
- Tier: Balanced B0 for dev, B3+ for prod

### 3.2 EmbeddingCache refactor to vector similarity
- New `IEmbeddingCache` API: `Task<ReadOnlyMemory<float>?> GetSemanticAsync(string prompt, double minSimilarity = 0.95)`
- HNSW index on the `embedding` field; cosine distance metric
- Tunable threshold via `appsettings.json` (`EmbeddingCache:SemanticThreshold`)
- Backwards-compat: `GetEmbeddingAsync(contentHash)` still works (exact-key path)
- Add `cache.semantic_hits` Meter instrument with `threshold` tag

### 3.3 Entra ID auth via Managed Identity (folds in DEF-001)
- Add `Microsoft.Azure.StackExchangeRedis` package
- `CacheModule.cs` — replace connection-string password parsing with `await opts.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential())`
- Drop `Redis-ConnectionString` KV secret (no longer needed for auth)
- Role assignment: BFF MI gets "Redis Data Owner" role on Managed Redis instance
- **Closes [#462 DEF-001](https://github.com/spaarke-dev/spaarke/issues/462)**

### 3.4 Dev cutover
- Provision `spaarke-bff-managed-redis-dev`
- Optional: RDB export from `spaarke-bff-redis-dev` → import into Managed Redis (only if dev data has value; cleaner to start fresh)
- Update App Settings: `Redis__ConnectionString` → Managed Redis hostname (TLS port 6380)
- Restart BFF; verify `/healthz` 200 + startup log
- Re-run Phase 1's KQL verification queries

### 3.5 Telemetry pipeline verification (R7-S7 invariants still hold)
- `dependencies | where type contains 'Redis'` shows new Managed Redis hostname
- `customMetrics | where name startswith 'cache.'` shows steady-state hits/misses/failures
- New: `customMetrics | where name == 'cache.semantic_hits'` shows hits when semantic threshold is met

### 3.6 Decommission ACR dev (after 7-day reversibility window)
- Tag `spaarke-bff-redis-dev` with `decommission=YYYY-MM-DD`
- Delete after window closes

### 3.7 Prod migration runbook
- Author runbook in `docs/guides/managed-redis-cutover.md` (parallel to existing `redis-cache-azure-setup.md`)
- Prod migration NOT in R2 — deferred to a Phase 4 prod project after dev runs cleanly for 30+ days

### Phase 3 acceptance criteria (GO path only)

- [ ] Managed Redis instance provisioned with RediSearch
- [ ] `EmbeddingCache.GetSemanticAsync` returns non-null for prompts within cosine threshold of cached embeddings
- [ ] `cache.semantic_hits` metric non-empty
- [ ] BFF MI authenticates via Entra ID; `Redis-ConnectionString` KV secret rotated to a sentinel (NOT deleted yet — kept as fallback for 30 days)
- [ ] All Phase 1 KQL verification queries still pass
- [ ] Sister project handoff signal if AI-Search relies on the embedding cache

---

## 5. Phase 3-Alt — Conditional: Entra ID auth on Azure Cache for Redis Premium (NO-GO path, 2-3 days)

**Fires only if Phase 2.1 decision rule says NO-GO but operator still wants DEF-001.**

- Upgrade `spaarke-bff-redis-dev` SKU from Basic C0 to Premium P1 (Entra ID is Premium-tier-only on ACR)
- Wire `Microsoft.Azure.StackExchangeRedis` extension
- Same DEF-001 closure semantics as 3.3, but at +$485/mo cost
- Closes [#462 DEF-001](https://github.com/spaarke-dev/spaarke/issues/462)

**Default**: if NO-GO on Managed Redis, also DEFER DEF-001 to next round rather than pay +$485/mo for key-rotation elimination alone. Acknowledge the 90-day key rotation as accepted operational cost.

---

## 6. Explicitly out of scope (with rationale)

| Item | Why deferred / cut |
|---|---|
| **DEF-002** Pub/Sub separation in prod | No measured contention. Phase 1 cache observability gives us the data to know if/when this becomes real. Re-evaluate after Phase 1 ships + 30 days. |
| **DEF-003** Multi-region Redis (geo-replication) | Spaarke BFF is single-region today. Active-active is dramatically simpler on Managed Redis, so this naturally folds into a future round IF Phase 3 GO landed AND Spaarke goes multi-region. |
| **DEF-004** Plain-text secret remediation (non-Redis) | Cross-cutting App Settings hygiene; not Redis. Belongs in a separate hardening project or sister project. |
| **DEF-006** Rename App Insights `spe-insights-dev-67e2xz` | Not Redis. Belongs in `spaarke-ai-azure-setup-dev-r1` continuation. |
| **`customer.bicep:181` per-customer Redis call cleanup** | R1 implementation gap — `Provision-Customer.ps1` properly deprecated per-customer Redis but the underlying Bicep template still has the `modules/redis.bicep` call. Real bug, but tiny scope (1-line Bicep delete + customer-template.bicepparam update). File as a separate small-scope issue ; NOT R2. |
| **N-3** ConnectionMultiplexerFactory hot-rotation lifecycle | Theoretical concern; no observed failure. Watch for it in Phase 3 if migration happens. |
| **M-1 / M-3 / M-4** non-cache hygiene items | Bundle into a "BFF telemetry hygiene" project; not Redis-specific. |

---

## 7. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Phase 2.1 audit shows < 10% near-matches → Managed Redis investment NOT economic | Medium | Low (we save money) | Path C — defer DEF-001 to next round; ship only Phase 1 |
| Phase 3 cutover surfaces an undetected StackExchange.Redis incompatibility | Low | Medium | Phase 2.2 audit + Phase 3.4 dev cutover BEFORE prod |
| `cache.failures` counter reveals high transient failure rate previously masked | High (this is the POINT) | Operational — paged for actual problems | Tune alert thresholds in Phase 1.4 to balance signal-to-noise; expect 1-2 weeks of threshold-tuning |
| `MetricsDistributedCache` decorator + Meter consolidation breaks an undetected dashboard | Medium | Low (rebuild dashboards in App Insights) | Phase 1.3's `resource` tag restoration is the primary mitigation |
| RDB import from ACR → Managed Redis fails or loses data | Low | Low (dev only; re-warming acceptable) | Phase 3 doesn't migrate data unless explicitly required; start fresh is cleaner |
| Modules locked at create time → wrong module choice forces re-provision | Medium | Medium | Phase 2.5 makes the modules decision before provisioning; default RediSearch-only is conservative |

---

## 8. Success criteria

- [ ] Phase 1 ships: all 5 sub-items merged; KQL verifies `cache.failures` + `cache.hits.by_resource` flowing; alerts deployed via Bicep
- [ ] Phase 2 produces a written decision memo at `notes/managed-redis-go-no-go.md`
- [ ] If Phase 2 = GO: Phase 3 ships; Managed Redis dev cutover clean; semantic embedding cache measurably reduces OpenAI API calls
- [ ] If Phase 2 = NO-GO: decision memo published with audit data; Path C or Path A documented as the chosen direction
- [ ] DEF-007 (cache failures), DEF-008 (alerts) created and closed by Phase 1 PR
- [ ] [#462 DEF-001 Entra ID auth](https://github.com/spaarke-dev/spaarke/issues/462) — either closed (Phase 3 GO) or explicitly deferred with rationale (Phase 3 NO-GO)

---

## 9. Estimated effort

| Phase | Best case | Worst case |
|---|---|---|
| Phase 1 (always ships) | 2 days | 4 days (alert threshold tuning) |
| Phase 2 (audit) | 1 day | 3 days (if EmbeddingCache audit requires more data) |
| Phase 3 GO | 8 days (1.5 wk) | 14 days (2.5 wk) |
| Phase 3-Alt NO-GO | 2 days | 3 days |

**Total**: 3-7 days if NO-GO; 11-21 days if GO. Phase 1 ships first regardless and de-risks everything that follows.

---

## 10. Open questions for project owner before spec lock

1. Is Spaarke's projected prod scale (500K+ embedding calls/day) close enough that the GO threshold should be lower (e.g., ≥ 15% near-matches instead of 30%)?
2. If Phase 2 = GO, do we also enable RedisJSON (chat session storage opportunity) at create time, or stay RediSearch-only? Locking modules at create is a real constraint.
3. Is the +$485/mo cost of ACR Premium acceptable for DEF-001 standalone (Path C), or is the 90-day key rotation acceptable as ongoing operational cost?
4. Phase 3 prod migration timing — fold into R2, or carve out as a separate prod project after dev runs cleanly for N days?

---

## See also

- [`notes/managed-redis-ai-research.md`](notes/managed-redis-ai-research.md) — full research + sources
- R1 retrospective: [`projects/spaarke-redis-cache-remediation-r1/`](../spaarke-redis-cache-remediation-r1/)
- [Microsoft Learn — Azure Managed Redis](https://learn.microsoft.com/en-us/azure/redis/overview)
- ADR-009 (concise): [`.claude/adr/ADR-009-redis-caching.md`](../../.claude/adr/ADR-009-redis-caching.md) — R1 amended; R2 may add to "Operational MUSTs" section
