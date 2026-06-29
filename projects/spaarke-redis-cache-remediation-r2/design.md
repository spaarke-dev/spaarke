# spaarke-redis-cache-remediation-r2 — Design

> **Last Updated**: 2026-06-26 (revised + decisions locked)
> **Status**: Spec-locked — ready to execute
> **Owner**: spaarke-dev
> **Predecessor**: [`spaarke-redis-cache-remediation-r1`](../spaarke-redis-cache-remediation-r1/) (R7-S7 closure shipped; 6 items filed as GitHub Issues #462–#467; #466 DEF-005 Managed Redis closed Won't Fix per [`notes/managed-redis-decision.md`](notes/managed-redis-decision.md))
> **Background research** (informational): [`notes/managed-redis-ai-research.md`](notes/managed-redis-ai-research.md)

<hot-path-declaration>
  <!-- Required per root CLAUDE.md §10 (binding 2026-06-26 per ci-cd-unit-test-remediation-r1 FR-C04). -->
  <bff>Y</bff>                <!-- Theme A modifies src/server/api/Sprk.Bff.Api/Infrastructure/Cache/* + Telemetry/CacheMetrics.cs + Program.cs -->
  <spaarke-ai>N</spaarke-ai>  <!-- No src/solutions/SpaarkeAi/** changes -->
  <ci-workflows>Y</ci-workflows>  <!-- Theme B adds .github/workflows/redis-key-rotation.yml -->
  <skill-directives>N</skill-directives>  <!-- No .claude/skills/** or .claude/constraints/** changes -->
  <root-claudemd>N</root-claudemd>  <!-- No root CLAUDE.md changes -->
</hot-path-declaration>

## Decisions locked (2026-06-26)

| # | Decision | Source |
|---|---|---|
| 1 | **Cron cadence**: quarterly per environment | Q1 answer |
| 2 | **Theme B environment coverage**: all environments (dev + staging + prod) from day 1, staggered cron times to catch breakage in dev before prod | Q2 answer + safety refinement |
| 3 | **Resource dimension cardinality**: no soft cap. Code-driven bounding (~10-20 expected values). Re-evaluate only if observed cardinality > 50. | Q3 — operator discretion |
| 4 | **PR sequencing**: one combined PR for all 3 themes — ship quickly | Q4 answer |

---

## TL;DR

R2 is a **closure project** — finishes the R1 work properly without re-architecting. Three coherent themes:

| Theme | Scope | Effort |
|---|---|---|
| **A. Cache observability hardening** | Six concrete fixes from the R1 senior review. Closes DEF-007 + DEF-008. | 2-3 days |
| **B. Redis key rotation automation** | Replaces the historically-slipping manual 90-day procedure with a scheduled GitHub Actions workflow. Closes DEF-001 without paying +$485/mo for ACR Premium. | 1-2 days |
| **C. R1 implementation gap closure** | Removes `customer.bicep:181` per-customer Redis (R1 deprecated this in `Provision-Customer.ps1` but the Bicep template was left untouched). | 0.5 day |

**Total**: 3-5 days end-to-end. One PR per theme (preferred — atomic review) or one combined PR (acceptable — they don't conflict).

**Explicit non-goals**: Managed Redis migration (decision: NO — see `notes/managed-redis-decision.md`). Multi-region. Pub/Sub separation. Semantic embedding cache. These are all valid future considerations but none have a concrete failure mode today.

---

## 1. Theme A — Cache observability hardening (closes DEF-007 + DEF-008)

All six items below ship in **one PR** to keep the cache observability surface coherent.

### A.1 `cache.failures` counter + try/finally on every op

**Concrete failure mode**: today, a Redis timeout / connection drop / auth failure produces **zero telemetry** because the `MetricsDistributedCache` decorator's `sw.Stop()` and metric record are AFTER the `await`. Exception throws skip them. Operators see "no traffic" not "high failures" during outage.

**Fix**: [`MetricsDistributedCache.cs:40-108`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Cache/MetricsDistributedCache.cs#L40-L108) — wrap every op in `try/finally`. Add a new `cache.failures` Counter on the same Meter with `outcome` dimension. Small `ClassifyException` helper maps to bounded values: `timeout` / `canceled` / `connection` / `serialization` / `other`.

```csharp
public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
{
    var sw = Stopwatch.StartNew();
    string outcome = "ok";
    byte[]? bytes = null;
    try
    {
        bytes = await _inner.GetAsync(key, token).ConfigureAwait(false);
        return bytes;
    }
    catch (Exception ex)
    {
        outcome = ClassifyException(ex);
        throw;
    }
    finally
    {
        sw.Stop();
        RecordGet(bytes, sw.Elapsed.TotalMilliseconds, outcome);
    }
}
```

**Verify**: KQL `customMetrics | where name == 'cache.failures' | summarize sum(value) by tostring(customDimensions.outcome)` returns ≥1 row after a forced Redis disconnect.

### A.2 Meter consolidation — collapse two `Meter("Sprk.Bff.Api.Cache")` instances

**Concrete failure mode**: today, two independent `Meter("Sprk.Bff.Api.Cache")` instances exist:
- [`TenantCache.cs:28-32`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Cache/TenantCache.cs#L28-L32) — static fields owned by `TenantCache`, used by `MetricsDistributedCache`
- [`Telemetry/CacheMetrics.cs:25-40`](../../src/server/api/Sprk.Bff.Api/Telemetry/CacheMetrics.cs#L25-L40) — instance class injected into `EmbeddingCache`, `GraphTokenCache`, and others

Both create `cache.hits` / `cache.misses` instruments on the same Meter NAME. App Insights may merge or duplicate counts. The earlier R7-S7 verification numbers (`cache.hits=20`) may have been mixing both sources without us knowing.

**Fix**: collapse to one canonical static `Sprk.Bff.Api.Telemetry.CacheMetrics` static class that owns the Meter + instruments. `MetricsDistributedCache` decorator and any AI-service that records additional dimensions emit via the static class. Delete the static fields from `TenantCache` (keep the static initializer for `JsonSerializerOptions` only). Existing consumers of injected `CacheMetrics?` either move to the static class OR are deleted if redundant.

**Migration**: ~7 files touched (TenantCache + CacheMetrics + the decorator + 4-5 AI consumers). Carefully ordered to keep tests green.

**Verify**: integration test asserts `Meter`-named-Sprk.Bff.Api.Cache count is exactly 1 at runtime via `MeterListener` enumeration.

### A.3 `resource` dimension restoration (wrapper layer)

**Concrete failure mode**: R7-S7 moved emission to the `IDistributedCache` decorator layer, which can't see the resource name (cache keys are opaque strings at that layer). Any pre-existing dashboard or alert that filters `cache.hits` by `resource` is silently broken.

**Fix**: have `TenantCache` emit a SECONDARY metric set with names `cache.hits.by_resource` / `cache.misses.by_resource` carrying the `resource` dimension. Keep the primary `cache.hits` / `cache.misses` from the decorator as the canonical raw count. Different names = no double-counting. Bounded cardinality (~10-20 resources).

**Why not parse `resource` from the key prefix in the decorator?** Considered. Rejected because system-cache exception keys (`comm:accounts:*`, MSAL `appcache-{appId}`) don't follow the `spaarke:tenant:{tid}:{resource}:...` pattern, and an allow-list is the same dev cost as wrapper-layer emission with cleaner cardinality.

**Verify**: KQL `customMetrics | where name == 'cache.hits.by_resource' | summarize sum(value) by tostring(customDimensions.resource)` returns rows with bounded values.

### A.4 Bicep-deployed alerts (3 minimum)

**Concrete failure mode**: R1 task 043 marked ✅ with "ready-to-paste Bicep skeletons" — but the alerts are markdown-only in `redis-cache-azure-setup.md` §8. Hit-rate <80% / P95 >100ms / memory >80% all fail silently with no page. The new metrics are dashboards-only, not actionable.

**Fix**: new `infrastructure/bicep/alerts.bicep` with:
1. **Hit-rate <80% / 15min** → action group: on-call email
2. **P95 latency >100ms / 5min** → action group: on-call email
3. **Memory >80% of SKU / 15min** → action group: on-call email

Action groups parameterized via `.bicepparam` (different recipients per environment). `Deploy-RedisCache.ps1 -DeployAlerts` flag triggers Bicep deploy of alerts only.

**Verify**: `az monitor metrics alert list -g rg-spaarke-dev` shows 3 rules. Manually trigger each (e.g., scale Redis to evict all keys → hit-rate alert fires within 15 min).

### A.5 Decorator regression integration test

**Concrete failure mode**: `CacheModule.DecorateDistributedCacheWithMetrics` does `services.Remove(...) + AddSingleton(...)`. If Microsoft changes the `IDistributedCache` registration shape in a future package version (e.g., keyed singleton, different lifetime), the decorator wiring silently fails or double-registers. Today there is no regression net.

**Fix**: new `tests/integration/Sprk.Bff.Api.Tests.Integration/Cache/MetricsDistributedCacheRegistrationTests.cs` — spins up the full DI graph via `WebApplicationFactory`, resolves `IDistributedCache`, asserts it's a `MetricsDistributedCache` wrapping the expected inner type. Runs in CI alongside unit tests.

### A.6 `UseAzureMonitor()` fails-open guard tightening

**Concrete failure mode**: [`Program.cs:21-30`](../../src/server/api/Sprk.Bff.Api/Program.cs#L21-L30) silently skips OTel registration if `APPLICATIONINSIGHTS_CONNECTION_STRING` is missing or empty. This was the right call for tests but in production a missing/empty conn string means we silently ship a BFF with no telemetry.

**Fix**: throw in non-Development environments, skip in Development. Mirrors the existing `CacheModule` 4-branch pattern.

```csharp
var aiConnString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
if (!string.IsNullOrWhiteSpace(aiConnString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}
else if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "APPLICATIONINSIGHTS_CONNECTION_STRING is required in non-Development environments. " +
        "Set the App Setting or environment variable.");
}
```

### Theme A acceptance criteria

- [ ] All 6 changes in one PR
- [ ] `dotnet build` clean; `dotnet test` ≥7885 pass (matches baseline)
- [ ] `Deploy-BffApi.ps1` clean — 4/4 critical DLLs hash-verified; `/healthz` 200
- [ ] `customMetrics | where name == 'cache.failures'` returns ≥1 row within 10 min of a forced Redis disconnect (use `az redis force-reboot` against dev)
- [ ] `customMetrics | where name == 'cache.hits.by_resource'` returns ≥1 row with `resource` dimension
- [ ] `az monitor metrics alert list -g rg-spaarke-dev` shows 3 alerts referencing the cache metrics
- [ ] Integration test passes in CI

---

## 2. Theme B — Redis key rotation automation (closes [#462 DEF-001](https://github.com/spaarke-dev/spaarke/issues/462) without Premium SKU)

### Why this approach instead of Entra ID auth

Entra ID auth via Managed Identity (DEF-001) eliminates the rotation procedure entirely, but on Azure Cache for Redis it's a **Premium-tier feature only** (+$485/mo over Basic C0). Project owner decision (2026-06-26): not worth +$485/mo for rotation elimination alone.

The alternative: **automate the rotation procedure** so the historical slippage problem (operators forgetting / deferring rotation past the 90-day target) is solved by a scheduled job, not by human reliability. Stays on Basic/Standard SKU.

### B.1 `scripts/Rotate-RedisKey.ps1`

Idempotent script parameterized by environment:

```powershell
Rotate-RedisKey.ps1 -Environment dev [-WhatIf]
```

**Algorithm** (designed to be safe under partial failure):

1. Verify Redis instance + KV exist and operator has permission
2. Read current connection string from KV (call it CONN_OLD)
3. Call `az redis regenerate-key --key-type Secondary` (rotates the SECONDARY key only; primary still works — this is the "safe window" property)
4. Construct new connection string using the SECONDARY key (call it CONN_NEW)
5. Update KV secret `Redis-ConnectionString` with CONN_NEW (new secret version; old version retained per KV's default retention policy)
6. Restart BFF App Service (`az webapp restart`)
7. Poll `/healthz` for HTTP 200 with timeout (default 120s — same as `Deploy-BffApi.ps1`)
8. **If healthz succeeds**: call `az redis regenerate-key --key-type Primary` (so the new key is the ONLY key — eliminates the now-unused primary)
9. **If healthz fails**: rollback — update KV secret back to CONN_OLD (use KV secret version history), restart BFF, exit non-zero with detailed error

Log every step to App Insights as a custom event (`RedisKeyRotation`) with `outcome`, `environment`, `duration_ms` fields.

### B.2 Scheduled trigger — GitHub Actions workflow (all environments, staggered)

New `.github/workflows/redis-key-rotation.yml` with three scheduled jobs — staggered by 7 days so dev rotates first; if anything breaks, dev catches it before staging/prod:

```yaml
on:
  schedule:
    - cron: '0 6 1 */3 *'   # Dev:     06:00 UTC on the  1st of Jan/Apr/Jul/Oct
    - cron: '0 6 8 */3 *'   # Staging: 06:00 UTC on the  8th of Jan/Apr/Jul/Oct
    - cron: '0 6 15 */3 *'  # Prod:    06:00 UTC on the 15th of Jan/Apr/Jul/Oct
  workflow_dispatch:        # Manual trigger for testing — env input parameter
    inputs:
      environment:
        type: choice
        options: [dev, staging, prod]
```

Job logic determines which environment to rotate based on `github.event.schedule` (dev/staging/prod literal match on the cron expression) or `inputs.environment` for manual runs.

Uses existing OIDC auth pattern (already in use for other Spaarke workflows). Distinct service-principal-to-environment mapping in GitHub Environments + secrets:
- `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` shared across envs
- `AZURE_CLIENT_ID_DEV`, `AZURE_CLIENT_ID_STAGING`, `AZURE_CLIENT_ID_PROD` per environment (so a compromised prod SP can't rotate dev)
- KV name + Redis name + App Service name parameterized per environment

**Initial rollout**: enable `workflow_dispatch` for all three envs; run dev manually first to validate the script + auth chain end-to-end. Only after one successful manual dev run, enable the cron schedules. Then watch staging at +7 days, then prod at +14 days.

### B.3 Operational runbook update

Update `docs/guides/redis-cache-azure-setup.md` §6 (Secret Rotation Procedure) — replace the manual 5-step procedure with:
- The automated workflow runs quarterly without operator action
- Manual fallback procedure (existing 5 steps) retained for emergency rotation (e.g., suspected key compromise)
- Verification queries:

```kql
// Verify last rotation succeeded
customEvents
| where name == 'RedisKeyRotation'
| where customDimensions.environment == 'dev'
| top 1 by timestamp desc
| project timestamp, outcome=customDimensions.outcome, duration_ms=customDimensions.duration_ms
```

### B.4 Alert — rotation hasn't run in 100 days

New alert in `infrastructure/bicep/alerts.bicep` (added in Theme A.4):
- Query App Insights for `customEvents | where name == 'RedisKeyRotation' AND outcome == 'success'` over the last 100 days
- If count = 0, alert on-call (automation has stopped, manual rotation needed)

### Theme B acceptance criteria

- [ ] `Rotate-RedisKey.ps1 -Environment dev -WhatIf` plans correctly without making changes
- [ ] `Rotate-RedisKey.ps1 -Environment staging -WhatIf` and `... prod -WhatIf` both plan correctly with the right per-env resource names
- [ ] Dry-run actual rotation on dev: KV secret has TWO versions (old + new); BFF restart succeeds; `/healthz` 200; App Insights logs `RedisKeyRotation` event with `outcome=success`, `environment=dev`
- [ ] GitHub Actions workflow runs successfully via `workflow_dispatch` for dev (validates auth chain end-to-end)
- [ ] Cron schedules enabled only after manual dispatch succeeds for at least dev
- [ ] Manual fallback procedure still works (operator unfamiliar with automation can still rotate)
- [ ] Alert fires if `RedisKeyRotation` event missing for >100 days for any environment (test by faking timestamp in App Insights)
- [ ] Per-env service principals scoped to their environment's KV + Redis only (prod SP cannot rotate dev, dev SP cannot rotate prod)

---

## 3. Theme C — R1 implementation gap closure

### C.1 Remove `customer.bicep:181` per-customer Redis call

**Concrete failure mode**: R1 FR-12 / Q-E Architecture 1 deprecated per-customer Redis. `Provision-Customer.ps1` was correctly updated (line 421 has the deprecation comment block). But `infrastructure/bicep/customer.bicep:181` still has the `module redis 'modules/redis.bicep' = { ... }` call. If anyone runs `customer.bicep` directly (not through `Provision-Customer.ps1`), they'd still provision a per-customer Redis — silently violating the architectural rule.

**Fix**:
- Delete the `redis` module call in [`customer.bicep`](../../infrastructure/bicep/customer.bicep) (lines ~181-195) plus the `redisSku` / `redisCapacity` parameters (lines ~62-67) plus the `redisName` variable (line ~99) plus any outputs that reference `redis.outputs.*`
- Update [`infrastructure/bicep/parameters/customer-template.bicepparam`](../../infrastructure/bicep/parameters/customer-template.bicepparam) — drop `redisSku` / `redisCapacity` params
- Update any test fixture that uses `customer.bicep` directly (grep `infrastructure/bicep/customer.bicep` in `tests/`, `scripts/`)
- Re-verify [`docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md`](../../docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md) §4.6 — the strikethrough row + footnote can stay as-is or be cleaned up (the gap will be closed)

### Theme C acceptance criteria

- [ ] `customer.bicep` lints + builds cleanly
- [ ] `az deployment group what-if` on `customer.bicep` for a fresh customer ID shows NO Redis resource in the plan
- [ ] All existing customer environments unaffected (no resource deletion — Bicep template only governs new deployments)

---

## 4. Explicitly out of scope (with rationale)

| Item | Why deferred / cut |
|---|---|
| **DEF-002** Pub/Sub separation in prod | No measured contention. Theme A's `cache.failures` + `resource`-tagged hits give us the data to know if/when this becomes real. Re-evaluate after Theme A ships + 30 days of prod data. |
| **DEF-003** Multi-region Redis | Spaarke BFF is single-region today. No DR commitment that requires geo-rep. Re-evaluate when (if) Spaarke commits to multi-region. |
| **DEF-004** Plain-text secret remediation (non-Redis) | Cross-cutting App Settings hygiene; not Redis. Belongs in a separate hardening project. |
| **DEF-005** Managed Redis migration | Decision: NO. See [`notes/managed-redis-decision.md`](notes/managed-redis-decision.md). Managed Redis is a high-throughput enterprise solution; Spaarke is below the scale where its bundled modules (RediSearch vector / RedisJSON / Bloom) pay off. Revisit if Spaarke crosses ~500K embedding calls/day OR commits to multi-region active-active. |
| **DEF-006** Rename App Insights `spe-insights-dev-67e2xz` | Not Redis. Fold into `spaarke-ai-azure-setup-dev-r1` continuation (sister project is already doing canonicalization for AI Search + KV). |
| **N-2** Hot-path performance baseline | Theoretical concern; no observed regression. Hardening in Theme A doesn't change per-call cost materially. Bundle into a future "BFF perf baseline" project if any cache regression is suspected. |
| **N-3** ConnectionMultiplexerFactory hot-rotation lifecycle | Theoretical concern; no observed failure. StackExchange.Redis auto-reconnects. Re-examine if rotation automation in Theme B surfaces issues. |
| **M-1** dead `Microsoft.ApplicationInsights.AspNetCore` package | Pure cleanup; not cache-specific. Bundle into a future "BFF dep hygiene" project. |
| **M-4** `CacheModule` bootstrap logger leak | Cosmetic. Pre-existing. Defer. |

---

## 5. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Theme A.2 Meter consolidation breaks a pre-existing dashboard | Medium | Low (rebuild dashboards in App Insights) | Theme A.3 wrapper-layer `resource` tag restoration is the primary mitigation. Run KQL diff before/after to spot dashboards depending on removed instruments. |
| Theme A.1 `cache.failures` reveals high transient failure rate previously masked | High (this IS the point) | Operational — paged for actual problems | Tune alert thresholds in Theme A.4 to balance signal-to-noise; expect 1-2 weeks of threshold-tuning post-deploy. |
| Theme B rotation script fails mid-rotation, leaving BFF unable to connect | Low | High | Algorithm rotates SECONDARY first (primary still works during transition), updates KV, restarts BFF, verifies `/healthz`, ONLY THEN rotates primary. Rollback path retains old KV secret version. |
| Theme B GitHub Actions OIDC auth misconfigured for KV write | Medium | Low (caught at first scheduled run) | `workflow_dispatch` test before reliance on cron; Theme B.4 alert catches silent failure (>100 days no rotation event). |
| Theme C `customer.bicep` change breaks existing customer-onboarding scripts | Low | Medium | `what-if` deployment validates the plan; smoke-test against demo-customer before merge. Existing customers unaffected (Bicep governs new deployments only). |

---

## 6. Success criteria

- [ ] Theme A: all 6 hardening items shipped; KQL verifies `cache.failures` + `cache.hits.by_resource` flowing; 3 Bicep alerts live in dev; CI test prevents decorator regression
- [ ] Theme B: rotation script committed; GitHub Actions workflow ran successfully at least once (via `workflow_dispatch`); operational runbook updated; key rotation alert deployed
- [ ] Theme C: `customer.bicep` Redis call deleted; verified via what-if against a fresh customer ID; deployment guide §4.6 cleanup
- [ ] [#462 DEF-001 Entra ID auth](https://github.com/spaarke-dev/spaarke/issues/462) closed with link to Theme B automation
- [ ] [#466 DEF-005 Managed Redis](https://github.com/spaarke-dev/spaarke/issues/466) closed Won't Fix with link to `notes/managed-redis-decision.md`
- [ ] DEF-007 (cache.failures) + DEF-008 (Bicep alerts) created and closed by Theme A PR
- [ ] DEF-009 (customer.bicep cleanup) created and closed by Theme C PR

---

## 7. Estimated effort

| Theme | Best case | Worst case |
|---|---|---|
| A. Observability hardening (6 items, 1 PR) | 2 days | 3 days (alert threshold tuning) |
| B. Key rotation automation (script + workflow + runbook) | 1 day | 2 days (OIDC auth troubleshooting) |
| C. R1 gap closure (Bicep edit) | 0.5 day | 1 day (test fixture surprises) |

**Total**: 3-5 days. Themes can ship independently (each is its own PR) — A first (largest, most foundational), then B + C in parallel.

---

## 8. Open questions — resolved 2026-06-26

All four resolved in "Decisions locked" header at the top of this document. Preserved here for the historical record:

1. ~~Cron cadence~~ → quarterly per environment, staggered (dev 1st / staging 8th / prod 15th)
2. ~~Initial environment coverage~~ → all environments from day 1; manual `workflow_dispatch` validates first before enabling cron
3. ~~`resource` cardinality cap~~ → no soft cap; code-driven natural bounding; re-evaluate only if observed > 50 distinct values
4. ~~PR sequencing~~ → one combined PR for all 3 themes; speed prioritized over revert-granularity

---

## See also

- [`notes/managed-redis-decision.md`](notes/managed-redis-decision.md) — informal decision record (no formal ADR amendment)
- [`notes/managed-redis-ai-research.md`](notes/managed-redis-ai-research.md) — research that informed the Managed Redis decision (retained for future reference)
- R1 retrospective: [`projects/spaarke-redis-cache-remediation-r1/`](../spaarke-redis-cache-remediation-r1/)
- ADR-009 (concise): [`.claude/adr/ADR-009-redis-caching.md`](../../.claude/adr/ADR-009-redis-caching.md) — no R2 amendment required (R2 doesn't change architecture; only operationalizes what R1 amended)
