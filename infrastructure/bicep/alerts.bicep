// infrastructure/bicep/alerts.bicep
// Spaarke BFF Redis cache — Azure Monitor alert rules (FR-04 of
// spaarke-redis-cache-remediation-r2).
//
// Replaces the markdown-only skeletons in
// `docs/guides/redis-cache-azure-setup.md` §8 with Bicep-deployed alerts so
// they are queryable via `az monitor metrics alert list` and
// `az monitor scheduled-query list`.
//
// Four alerts (mirroring docs §8 Alert Definitions FR-17 source of truth + FR-11 rotation):
//   1. Hit-rate < 80% over 15 min     (scheduledQueryRule, App Insights)
//   2. P95 latency > 100 ms over 5 min (scheduledQueryRule, App Insights)
//   3. Memory > 80% of SKU over 15 min (metricAlert, Redis platform metric)
//   4. RedisKeyRotation success absent >100 days (scheduledQueryRule, App Insights) — FR-11
//
// Module shape mirrors `infrastructure/bicep/modules/redis.bicep`:
//   @description params, defaults via `resourceGroup().location`,
//   resource declarations with explicit api-version, outputs at bottom.
//
// Deploy via `scripts/Deploy-RedisCache.ps1 -DeployAlerts -Environment <env>`.

// =====================================================
// PARAMETERS
// =====================================================

@description('Redis cache resource name (target for memory alert). Convention: `spaarke-bff-redis-{env}`.')
param redisCacheName string

@description('Application Insights resource name (target for hit-rate + P95 KQL alerts). Convention: `spe-insights-{env}-67e2xz` (dev) / TBD other envs.')
param appInsightsName string

@description('Action group resource ID for on-call email routing (e.g., `/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Insights/actionGroups/{name}`).')
param actionGroupResourceId string

@description('Location for the metric alerts. Metric alerts are global by convention; scheduled-query rules respect RG location.')
param location string = resourceGroup().location

@description('Environment tag (dev | staging | prod) — flows into alert tags + display names for KQL filtering.')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'dev'

@description('Severity for the 3 cache alerts (0-4). Default 2 = Warning (Sev 2) per docs §8 convention.')
@allowed([0, 1, 2, 3, 4])
param alertSeverity int = 2

@description('Hit-rate threshold (0.0–1.0). Default 0.80 = 80% per FR-04 / docs §8 Alert 1. Prod tuning increases to 0.90 per docs §8 tuning table.')
param hitRateThreshold string = '0.80'

@description('P95 latency threshold in milliseconds. Default 100 per FR-04 / docs §8 Alert 2. Prod tuning tightens to 50 per docs §8 tuning table.')
param p95LatencyMsThreshold int = 100

@description('Memory percent threshold (0-100). Default 80 per FR-04 / docs §8 Alert 3. Prod tuning tightens to 70 per docs §8 tuning table.')
@minValue(1)
@maxValue(100)
param memoryPercentThreshold int = 80

@description('Missed-rotation alert threshold in days. Fires if no RedisKeyRotation success custom event for any env in this window. Default 100 per FR-11 (90-day rotation cadence + 10-day grace).')
@minValue(1)
param missedRotationDays int = 100

@description('Tags propagated to all alert resources.')
param tags object = {
  environment: environment
  project: 'spaarke-redis-cache-remediation-r2'
  feature: 'cache-observability'
}

// =====================================================
// VARIABLES
// =====================================================

var alertNamePrefix = 'redis-cache'

// Resource IDs — derived from name params via `resourceId()` to keep the
// module callable from any RG context (mirrors the redis.bicep output pattern).
var redisCacheResourceId = resourceId('Microsoft.Cache/Redis', redisCacheName)
var appInsightsResourceId = resourceId('Microsoft.Insights/components', appInsightsName)

// KQL — hit rate below threshold (mirrors docs §8 Alert 1 KQL, threshold parameterized).
var hitRateKql = '''
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
| where total >= 100
| extend hit_rate = hits / total
| summarize avg_hit_rate = avg(hit_rate) by bin(timestamp, 15m), resource
| where avg_hit_rate < ${HIT_RATE_THRESHOLD}
'''

// KQL — P95 latency above threshold (mirrors docs §8 Alert 2 KQL).
var p95LatencyKql = '''
customMetrics
| where name == "cache.redis_p95_ms"
| extend resource = tostring(customDimensions.resource)
| summarize avg_p95_ms = avg(valueSum / valueCount) by bin(timestamp, 1m), resource
| summarize windowed_p95 = avg(avg_p95_ms) by bin(timestamp, 5m), resource
| where windowed_p95 > ${P95_THRESHOLD_MS}
'''

// KQL — RedisKeyRotation success absent >N days per env (FR-11).
// Fires when any env's last_success is older than the threshold, OR when an env has never recorded success (isnull).
// NOTE: detection of envs that have NEVER recorded success requires the env tuple to appear in the row set;
// since `customEvents` only yields rows for recorded events, "never recorded" is only detectable when at least one
// stale row exists for that env. This matches FR-11 intent: detect rotation regression, not bootstrap-state absence.
var missedRotationKql = '''
customEvents
| where name == 'RedisKeyRotation' and customDimensions.outcome == 'success'
| summarize last_success = max(timestamp) by tostring(customDimensions.environment)
| where last_success < ago(${MISSED_ROTATION_DAYS}d) or isnull(last_success)
'''

// =====================================================
// ALERT 1 — Hit-rate < threshold over 15 min (scheduled-query rule on App Insights)
// =====================================================

resource hitRateAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${alertNamePrefix}-hit-rate-low-${environment}'
  location: location
  tags: tags
  properties: {
    description: 'Cache hit rate below ${hitRateThreshold} over 15 min — investigate key/version drift or TTL regression. Source: cache.hits / cache.misses custom metrics (FR-16 of R1).'
    severity: alertSeverity
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    scopes: [
      appInsightsResourceId
    ]
    criteria: {
      allOf: [
        {
          query: replace(hitRateKql, '\${HIT_RATE_THRESHOLD}', hitRateThreshold)
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroupResourceId
      ]
    }
    autoMitigate: true
  }
}

// =====================================================
// ALERT 2 — P95 latency > threshold over 5 min (scheduled-query rule on App Insights)
// =====================================================

resource p95LatencyAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${alertNamePrefix}-p95-latency-high-${environment}'
  location: location
  tags: tags
  properties: {
    description: 'Cache P95 latency above ${p95LatencyMsThreshold}ms over 5 min — likely network issue or SKU undersize. Source: cache.redis_p95_ms custom metric (FR-16 of R1).'
    severity: alertSeverity
    enabled: true
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    scopes: [
      appInsightsResourceId
    ]
    criteria: {
      allOf: [
        {
          query: replace(p95LatencyKql, '\${P95_THRESHOLD_MS}', string(p95LatencyMsThreshold))
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroupResourceId
      ]
    }
    autoMitigate: true
  }
}

// =====================================================
// ALERT 3 — Memory > threshold % of SKU over 15 min (platform metric alert on Redis)
// =====================================================

resource memoryAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-memory-high-${environment}'
  location: 'global'
  tags: tags
  properties: {
    description: 'Redis used memory above ${memoryPercentThreshold}% of SKU over 15 min — scale to next SKU. Source: Azure Monitor platform metric Microsoft.Cache/Redis/usedmemorypercentage.'
    severity: alertSeverity
    enabled: true
    scopes: [
      redisCacheResourceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    targetResourceType: 'Microsoft.Cache/Redis'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'MemoryUsageCriteria'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'usedmemorypercentage'
          metricNamespace: 'Microsoft.Cache/Redis'
          operator: 'GreaterThan'
          threshold: memoryPercentThreshold
          timeAggregation: 'Average'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupResourceId
      }
    ]
    autoMitigate: true
  }
}

// =====================================================
// ALERT 4 — RedisKeyRotation success absent >100 days (scheduled-query rule on App Insights) — FR-11
// =====================================================

resource missedRotationAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${alertNamePrefix}-rotation-missed-${environment}'
  location: location
  tags: tags
  properties: {
    description: 'No RedisKeyRotation success custom event recorded in App Insights for >${missedRotationDays} days for one or more envs — automation likely silently failing (workflow disabled, SP expired, script broken). Investigate the Theme B rotation workflow. FR-11 of spaarke-redis-cache-remediation-r2.'
    severity: alertSeverity
    enabled: true
    evaluationFrequency: 'P1D'
    windowSize: 'P1D'
    scopes: [
      appInsightsResourceId
    ]
    criteria: {
      allOf: [
        {
          query: replace(missedRotationKql, '\${MISSED_ROTATION_DAYS}', string(missedRotationDays))
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroupResourceId
      ]
    }
    autoMitigate: true
  }
}

// =====================================================
// OUTPUTS
// =====================================================

output hitRateAlertId string = hitRateAlert.id
output p95LatencyAlertId string = p95LatencyAlert.id
output memoryAlertId string = memoryAlert.id
output missedRotationAlertId string = missedRotationAlert.id
