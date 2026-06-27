# Task 028e — KQL Dashboard Queries (Phase 1R Deprecation Telemetry)

> **Purpose**: Application Insights / Log Analytics queries to observe Phase 1R routing deprecation signals and steady-state cache health. Suitable for an ops dashboard.
> **Authored**: 2026-06-24
> **Telemetry sources**: `WorkspaceOptionsValidator` (Configuration), `RoutingConsumerTypeHealthCheck` (Services/Ai/PublicContracts), `ConsumerRoutingService` (Services/Ai/PublicContracts — instrumented in 028a).

---

## 1. Startup deprecation WARN — `WorkspaceOptionsValidator`

**Signal**: any of the 6 deprecated `Workspace__*PlaybookId` env vars is set on a deployed environment. Fires once per app start.

**Query**:
```kusto
traces
| where timestamp > ago(7d)
| where message has "Workspace__*PlaybookId env vars are deprecated"
| extend instance = tostring(customDimensions["AspNetCoreEnvironment"]),
         role     = cloud_RoleName,
         host     = cloud_RoleInstance
| project timestamp, message, instance, role, host, severityLevel
| order by timestamp desc
```

**Interpretation**: if any row appears in the last 7 days for `role = spaarke-bff-prod`, an operator should remove the corresponding `Workspace__*PlaybookId` value from App Service config (those values are now graceful-degrade fallback only; the canonical source is the `sprk_playbookconsumer` Dataverse table).

---

## 2. Startup routing health check — `RoutingConsumerTypeHealthCheck`

**Signal**: BFF constants don't match Dataverse routing-table contents at app start. Catches admin typos on the Dataverse side OR missing routing records for a new environment.

**Query (mismatches only)**:
```kusto
traces
| where timestamp > ago(7d)
| where message has "RoutingConsumerTypeHealthCheck"
| where message has "NOT in" // matches both "Dataverse has consumer types NOT in ConsumerTypes.All" and "ConsumerTypes.All has types NOT in Dataverse"
| extend role = cloud_RoleName
| project timestamp, message, role, cloud_RoleInstance, severityLevel
| order by timestamp desc
```

**Query (healthy steady-state)**:
```kusto
traces
| where timestamp > ago(24h)
| where message startswith "RoutingConsumerTypeHealthCheck"
| where severityLevel == 1 // Information
| where message has "Routing surface healthy"
| summarize count() by bin(timestamp, 1h), cloud_RoleName
| render timechart
```

**Interpretation**: in a healthy environment, every app start logs one healthy line and zero mismatch lines. A spike in mismatch entries after a Dataverse change indicates an admin made a typo or removed a row in error.

---

## 3. Runtime fallback telemetry — Activity tag (placeholder)

**Signal**: a consumer call to `IConsumerRoutingService.ResolveAsync` returned `null` and the consumer fell back to its env-var path. Tag was wired by the consumer migrations (028c + 028d) per FR-1R-06.

**Note (2026-06-24)**: the current 028c/028d implementations log the fallback at the consumer site rather than emitting a discrete Activity tag — both signals are captured here. If a future task adds a strict `routing.envvar_fallback_used` Activity tag, this query already covers the future shape.

**Query**:
```kusto
union
    (dependencies
     | where timestamp > ago(24h)
     | where customDimensions["routing.envvar_fallback_used"] == "true"
     | extend signal = "activity-tag"),
    (traces
     | where timestamp > ago(24h)
     | where message has "env var fallback used"
     | extend signal = "log-message")
| project timestamp, signal, cloud_RoleName, message = coalesce(message, name)
| summarize count() by bin(timestamp, 1h), signal
| render columnchart
```

**Interpretation**: a healthy environment with the routing table fully populated should show **zero** fallback rows over 24 hours. Any spike means either the routing table is missing a record (see Query 2) or `ConsumerRoutingService` failed (Dataverse outage) and consumers gracefully degraded.

---

## 4. Cache hit ratio — `ConsumerRoutingService` (steady-state metric)

**Signal**: how warm is the 5-min TTL routing cache? Target: **>70%** post-warmup (FR-1R-08 stabilization metric).

**Query**:
```kusto
traces
| where timestamp > ago(15m)
| where message startswith "ConsumerRoutingService"
| extend cacheHit = case(
    message has "cache hit", "hit",
    message has "cacheHit=false", "miss",
    "other")
| where cacheHit in ("hit", "miss")
| summarize
    hits  = countif(cacheHit == "hit"),
    misses = countif(cacheHit == "miss")
| extend total = hits + misses,
         hit_ratio = round(todouble(hits) / todouble(hits + misses), 3)
```

**Interpretation**: with 6 consumer types and a 5-min TTL, steady-state hit ratio should converge to **~95-99%** under normal traffic. A sustained ratio below 70% indicates either:
- traffic is too sparse for the TTL window (each consumer-type hit is a cache miss because the prior cached entry expired) — expected in dev/test
- the cache is being cleared aggressively (no current mechanism, but worth investigating if seen)
- a new consumer type was added and is still warming

**Per-consumer-type breakdown** (diagnostic):
```kusto
traces
| where timestamp > ago(1h)
| where message startswith "ConsumerRoutingService"
| extend consumerType = extract(@"consumerType=([\w-]+)", 1, message),
         cacheHit = case(
             message has "cache hit", true,
             message has "cacheHit=false", false,
             bool(null))
| where isnotempty(consumerType) and isnotnull(cacheHit)
| summarize
    hits  = countif(cacheHit == true),
    misses = countif(cacheHit == false)
    by consumerType
| extend hit_ratio = round(todouble(hits) / todouble(hits + misses), 3)
| order by hit_ratio asc
```

---

## 5. Workbook layout suggestion

For a single-page Phase 1R health dashboard, lay out the queries top-to-bottom:

1. **Status banner** — Query 2 (healthy) as a single green-tile gauge: "Routing surface healthy: ✅ / ⚠️"
2. **Deprecation board** — Query 1 as a table sorted by timestamp desc; if non-empty, op should clean up env vars
3. **Mismatch alerts** — Query 2 (mismatches) as a table
4. **Fallback signal** — Query 3 as a column chart, expected to be empty
5. **Cache hit ratio** — Query 4 as a scalar tile + Query 4 (per-type breakdown) as a sortable table

A future task can codify this layout via Application Insights Workbook JSON; documented here as the canonical query set so the workbook author has a single source of truth.

---

*Authored 2026-06-24 as part of Phase 1R task 028e (FR-1R-06 deprecation telemetry).*
