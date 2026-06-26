# Redis Cache Alert Definitions — Draft (FR-17)

> **Status**: Draft (Task 043). Source of truth for Task 051 integration into `docs/guides/redis-cache-azure-setup.md` (Troubleshooting section). Optional Bicep deployment via extension to `infrastructure/bicep/modules/alerts.bicep`.
>
> **Scope**: Three alerts on the shared Redis cache (`spaarke-bff-redis-{env}`) and on cache-wrapper telemetry (`cache.hits`, `cache.misses`, `cache.redis_p95_ms`) emitted per FR-16.
>
> **Resource identifiers (placeholder)**:
> - Redis: `spaarke-bff-redis-{env}` (Azure Cache for Redis)
> - App Insights: `spaarke-{env}-appi` (or environment-specific; see `auth-azure-resources.md`)
> - Action Group: `sdap-alerts-{env}` (existing `alerts.bicep` default: `${alertNamePrefix}-action-group`)

---

## Severity convention

Matches existing `alerts.bicep`:
- **Sev 1 (Critical)** — service-degrading; page on-call
- **Sev 2 (Warning)** — investigate next business day; email
- **Sev 3 (Informational)** — trend signal; review weekly

All three Redis alerts below are **Sev 2 (Warning)**. None are paging incidents on their own; sustained or co-occurring firings (e.g., (b) + (c) together) escalate via runbook.

---

## Alert 1: Cache Hit Rate Below 80%

**Name**: `redis-cache-hit-rate-low`
**Resource scope**: App Insights (`spaarke-{env}-appi`)
**Severity**: Warning (Sev 2)
**Evaluation frequency**: 5 minutes
**Window**: 15 minutes
**Threshold**: `avg_hit_rate < 0.80` for the window
**Metric source**: App Insights custom metrics `cache.hits` and `cache.misses` (FR-16)

**Metric expression (App Insights KQL — scheduledQueryRules)**:

```kusto
let hits = customMetrics
  | where name == "cache.hits"
  | summarize hits = sum(valueSum) by bin(timestamp, 5m), resource = tostring(customDimensions.resource);
let misses = customMetrics
  | where name == "cache.misses"
  | summarize misses = sum(valueSum) by bin(timestamp, 5m), resource = tostring(customDimensions.resource);
hits
| join kind=fullouter misses on timestamp, resource
| extend hits = coalesce(hits, 0.0), misses = coalesce(misses, 0.0)
| extend total = hits + misses
| where total >= 100  // noise floor: ignore intervals with <100 total ops
| extend hit_rate = hits / total
| summarize avg_hit_rate = avg(hit_rate) by bin(timestamp, 15m), resource
| where avg_hit_rate < 0.80
```

**Suggested action**: "Cache key/version drift; investigate."

Likely causes (runbook):
1. **Key version drift** — a recent deploy wrote keys without the `:v{n}` suffix, causing reads to miss against the prior version's keys. Check `git log` for changes to `CacheKeys.cs` or any `IDistributedCache.Set*` callsites in the last 24 h.
2. **Tenant prefix miscomputation** — a code path bypassed `tenant:{tenantId}:` prefix derivation (NFR-08). Grep for `IDistributedCache` usage without `TenantPrefixedKey(...)` helper.
3. **TTL too short** — a recently-tuned TTL is evicting entries before normal re-access. Check `RedisCacheOptions` / `IDistributedCache.Set*` TTL values vs prior versions.
4. **Cold start after restart/scale event** — transient; resolves naturally within ~30 min as cache warms. Verify against App Service restart events in the same window.
5. **Cache stampede on a high-traffic key** — rare; if hit rate is recovering on its own and `cache.redis_p95_ms` is also elevated, correlate with the eviction policy and `maxmemory-policy`.

---

## Alert 2: Redis P95 Latency Above 100 ms

**Name**: `redis-cache-p95-latency-high`
**Resource scope**: App Insights (`spaarke-{env}-appi`) — preferred because the metric is wrapper-emitted P95
**Severity**: Warning (Sev 2)
**Evaluation frequency**: 1 minute
**Window**: 5 minutes
**Threshold**: `avg(cache.redis_p95_ms) > 100` for the window
**Metric source**: App Insights custom metric `cache.redis_p95_ms` (FR-16; emitted from cache wrapper)

**Metric expression (App Insights KQL — scheduledQueryRules)**:

```kusto
customMetrics
| where name == "cache.redis_p95_ms"
| extend resource = tostring(customDimensions.resource)
| summarize avg_p95_ms = avg(valueSum / valueCount) by bin(timestamp, 1m), resource
| summarize windowed_p95 = avg(avg_p95_ms) by bin(timestamp, 5m), resource
| where windowed_p95 > 100
```

**Alternative — Azure Monitor platform metric** (if wrapper metric is unavailable; less precise — measures Redis-side, not wrapper-observed):

- **Metric namespace**: `Microsoft.Cache/Redis`
- **Metric**: `cacheLatency` (Premium SKU only) — *or* `serverLoad` as a proxy on Basic/Standard
- **Aggregation**: Average
- **Threshold**: > 100,000 (microseconds) for Premium; serverLoad > 80% as proxy

**Suggested action**: "Network issue or SKU undersize."

Likely causes (runbook):
1. **Network path degradation** — check Azure Service Health for `Cache` in the deployment region. If a regional incident is active, ride it out (auto-mitigates).
2. **Redis SKU undersize** — check Redis `serverLoad` and `usedmemorypercentage` in the same window. If `serverLoad > 80%` or `usedmemorypercentage > 70%`, the SKU is undersized — escalate to scale-up (link to Alert 3 runbook).
3. **Client-side connection pool exhaustion** — check `StackExchange.Redis` `ConnectionMultiplexer` configuration; should have `IOCP`/`Worker` thread counts adequate; check App Insights for `RedisTimeoutException`. Symptom: P95 high but Redis-side `cacheLatency` normal → client problem.
4. **VNet / private endpoint config** — if Redis is behind a private endpoint, verify DNS resolution + NSG rules. Symptom: P95 spike correlates with VNet config change.
5. **Noisy neighbor on Basic/Standard SKU** — Premium SKU has dedicated cores; Basic/Standard share. If on Basic/Standard and persistent, scale to Premium or accept variance.

---

## Alert 3: Redis Memory Usage Above 80% of SKU Limit

**Name**: `redis-cache-memory-high`
**Resource scope**: Redis (`spaarke-bff-redis-{env}`) — Azure Monitor platform metric
**Severity**: Warning (Sev 2)
**Evaluation frequency**: 5 minutes
**Window**: 15 minutes (sustained)
**Threshold**: `usedmemorypercentage > 80` for the window
**Metric source**: Azure Monitor platform metric `Microsoft.Cache/Redis/usedmemorypercentage`

**Metric expression (Azure Monitor metric alert — no KQL required)**:

- **Namespace**: `Microsoft.Cache/Redis`
- **Metric name**: `usedmemorypercentage`
- **Aggregation**: Average (or Maximum for tighter)
- **Operator**: GreaterThan
- **Threshold**: 80
- **Window**: PT15M
- **Evaluation frequency**: PT5M

**Equivalent KQL (if expressed as scheduledQueryRules instead — useful for multi-resource queries)**:

```kusto
AzureMetrics
| where ResourceProvider == "MICROSOFT.CACHE"
| where ResourceId endswith "/redis/spaarke-bff-redis-dev"  // adjust per env
| where MetricName == "usedmemorypercentage"
| summarize avg_used_pct = avg(Average) by bin(TimeGenerated, 15m), Resource
| where avg_used_pct > 80
```

**Suggested action**: "Scale to next SKU."

Likely causes (runbook):
1. **Organic growth** — natural cache fill; this is the expected trigger. Confirm by checking `usedmemorypercentage` trend over prior 7 days; if monotonically increasing, scale up (see step 3).
2. **Eviction policy misconfigured** — if Redis is configured with `noeviction`, full memory causes write failures. Verify `maxmemory-policy` is `allkeys-lru` (or `allkeys-lfu`). For Spaarke's distributed-cache pattern, `allkeys-lru` is correct.
3. **Scale to next SKU** — decision matrix:
   - Basic C0 (250 MB) → Basic C1 (1 GB) — dev/test only
   - Standard C1 (1 GB) → Standard C2 (2.5 GB) — typical dev/test upgrade
   - Standard C2 → Standard C3 (6 GB) or Premium P1 (6 GB w/ persistence) — prod sizing
   - Premium P1 → Premium P2 (13 GB) — high-volume prod
   - Update `infrastructure/bicep/parameters/redis-{env}.bicepparam` `redisSkuName` / `redisSkuFamily` / `redisSkuCapacity` and redeploy via `redis.bicep`.
4. **Stale entries with long TTL** — if many keys have multi-day TTL and the working set is much smaller, consider reducing TTL or relying on LRU eviction. Check `cache.hits` distribution by key prefix for skew.
5. **Tenant explosion** — if a new tenant onboarded recently with high cache volume, the per-tenant prefix may have unbounded growth. Verify with `INFO keyspace` on the Redis side; consider per-tenant TTL tuning or moving high-volume tenants to dedicated Redis (consult capacity-planning notes).

---

## Cross-alert correlation runbook

- **(b) + (c) together** = SKU is undersized for current load. Default action: scale up one tier and observe for 24 h.
- **(a) + (b) without (c)** = likely code regression (key-version drift causing both increased Redis traffic for misses AND slower per-op latency). Default action: check recent deploys, consider rollback while investigating.
- **(a) alone without (b) or (c)** = likely TTL / key-naming bug from a recent commit. Code review focus.
- **(c) alone without (a) or (b)** = healthy growth signal; scale before it impacts latency.

---

## Threshold tuning — dev vs prod

Defaults above are dev/staging-appropriate. Prod tuning per spec.md "Unresolved Questions":

| Alert | Dev/Staging | Prod (proposed) |
|---|---|---|
| Hit rate | < 80% | < 90% |
| P95 latency | > 100 ms | > 50 ms |
| Memory | > 80% | > 70% |

Finalize prod thresholds during prod provisioning project (out of scope for r1).

---

## Bicep deployment (optional — best-effort)

`infrastructure/bicep/modules/alerts.bicep` exists and already contains 7 AI-feature alerts using `Microsoft.Insights/metricAlerts@2018-03-01`. Adding the three Redis alerts is structurally consistent — same pattern, same action-group wiring.

**Recommended approach (if Task 051 chooses to deploy via Bicep)**:

1. **Alerts (a) and (b)** — App Insights custom-metric alerts. Use `Microsoft.Insights/scheduledQueryRules@2023-03-15-preview` (KQL-based; matches `customMetrics` table queries above). The existing 7 alerts in `alerts.bicep` use `metricAlerts` for namespace-style metrics, but `scheduledQueryRules` is the correct resource for KQL queries against `customMetrics`. Either the new alerts use `scheduledQueryRules` (new pattern), or the cache wrapper emits via standard metric namespaces and they use `metricAlerts` (existing pattern). The cache-miss-spike alert in `alerts.bicep` (lines 195–233) shows the `metricAlerts` pattern with `metricName: 'customMetrics/cache.misses'` — that pattern works for App Insights custom metrics and is the simplest extension.

2. **Alert (c)** — Azure Monitor platform metric on the Redis resource. Use `Microsoft.Insights/metricAlerts@2018-03-01` with `scopes: [redisId]` and `metricName: 'usedmemorypercentage'`, `metricNamespace: 'Microsoft.Cache/Redis'`. Requires the Redis resource ID as an additional parameter to `alerts.bicep`.

**New module parameters needed**:

```bicep
@description('Redis resource ID (for memory alert scope)')
param redisResourceId string = ''
```

**Skeleton for the three new resources** (informational — Task 051 will finalize):

```bicep
// Alert 8: Redis Cache Hit Rate Low
resource cacheHitRateLowAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-cache-hit-rate-low'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alert when cache hit rate drops below 80% for 15 min — cache key/version drift; investigate'
    severity: warningSeverity
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'HitRateLowCriteria'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'customMetrics/cache.hit_rate'
          metricNamespace: 'microsoft.insights/components'
          operator: 'LessThan'
          threshold: 0.80
          timeAggregation: 'Average'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
    autoMitigate: true
  }
}

// Alert 9: Redis P95 Latency High
resource redisP95LatencyAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-redis-p95-high'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alert when Redis P95 > 100ms for 5 min — network issue or SKU undersize'
    severity: warningSeverity
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'RedisP95Criteria'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'customMetrics/cache.redis_p95_ms'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          threshold: 100
          timeAggregation: 'Average'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
    autoMitigate: true
  }
}

// Alert 10: Redis Memory > 80%
resource redisMemoryHighAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = if (!empty(redisResourceId)) {
  name: '${alertNamePrefix}-redis-memory-high'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alert when Redis memory > 80% of SKU limit for 15 min — scale to next SKU'
    severity: warningSeverity
    enabled: true
    scopes: [ redisResourceId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'RedisMemoryCriteria'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'usedmemorypercentage'
          metricNamespace: 'Microsoft.Cache/Redis'
          operator: 'GreaterThan'
          threshold: 80
          timeAggregation: 'Average'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
    autoMitigate: true
  }
}
```

**Deferred decision (Task 051)**: whether to (i) extend `alerts.bicep` with these three resources, OR (ii) document-only in `redis-cache-azure-setup.md` and create alerts via Portal / App Insights workbook. Both satisfy FR-17 acceptance.

---

## Test plan (Task 051 will integrate)

- **(a)** simulate: write known keys with a wrong `:v{n}` suffix from a test harness; expect hit rate < 80% within ~5 min; alert fires within 15-min window.
- **(b)** simulate: introduce 200 ms artificial delay in cache wrapper (debug-only feature flag) for 6 min; alert fires within window.
- **(c)** simulate: pre-load Redis with > 80% of SKU capacity (dev only; use C0 or C1 small SKU); alert fires within window.

Each simulation should generate one alert email to the action-group recipient and a trace entry in App Insights `traces` for cross-correlation.

---

*Draft per FR-17. Task 051 will integrate into `docs/guides/redis-cache-azure-setup.md` Troubleshooting section and optionally extend `infrastructure/bicep/modules/alerts.bicep`.*
