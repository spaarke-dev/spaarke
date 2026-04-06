# Monitoring and Alerting Setup Guide

> **Last Updated**: 2026-04-05
>
> **Scope**: Spaarke production environment monitoring via Azure Application Insights, Log Analytics, and Azure Monitor alerts.
>
> **Prerequisite**: Platform deployed via `Deploy-Platform.ps1` (task 020), which provisions the monitoring stack.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Application Insights Configuration](#application-insights-configuration)
3. [Key Metrics by Service](#key-metrics-by-service)
4. [Alert Rules](#alert-rules)
5. [Dashboard Setup](#dashboard-setup)
6. [Customer-Scoped Log Queries (KQL)](#customer-scoped-log-queries-kql)
7. [Cost Monitoring](#cost-monitoring)
8. [On-Call Procedures](#on-call-procedures)
9. [Maintenance and Tuning](#maintenance-and-tuning)

---

## Architecture Overview

The Spaarke monitoring stack consists of three Azure resources deployed by `infrastructure/bicep/modules/monitoring.bicep`:

```
┌──────────────────────────────────────────────────┐
│              Azure Monitor                        │
│  ┌─────────────────────┐  ┌────────────────────┐ │
│  │  Application Insights│  │  Log Analytics     │ │
│  │  (spaarke-appins-    │  │  (spaarke-laws-    │ │
│  │   prod)              │──│   prod)            │ │
│  │                      │  │  Retention: 90 days│ │
│  │  - Request telemetry │  │  - Raw log storage │ │
│  │  - Custom metrics    │  │  - KQL queries     │ │
│  │  - Dependency calls  │  │  - Cross-resource  │ │
│  └─────────────────────┘  └────────────────────┘ │
│                                                    │
│  ┌─────────────────────┐  ┌────────────────────┐ │
│  │  Alert Rules         │  │  Azure Dashboard   │ │
│  │  (alerts.bicep)      │  │  (dashboard.bicep) │ │
│  │  7 rules configured  │  │  5 dashboard rows  │ │
│  └─────────────────────┘  └────────────────────┘ │
└──────────────────────────────────────────────────┘
         ▲
         │ Telemetry
         │
┌────────┴──────────┐
│  BFF API           │
│  (spaarke-bff-prod)│
│                    │
│  Connection string │
│  from Key Vault:   │
│  ApplicationInsights│
│  --ConnectionString │
└────────────────────┘
```

**Data flow**: BFF API sends telemetry to Application Insights via the connection string stored in Key Vault. Application Insights ingests data into the connected Log Analytics workspace. Alert rules evaluate metrics from Application Insights. The Azure Dashboard visualizes key metrics.

---

## Application Insights Configuration

### Resource Details

| Property | Value |
|----------|-------|
| Resource Name | `spaarke-appins-prod` |
| Resource Group | `spe-infrastructure-westus2` |
| Type | `web` |
| Ingestion Mode | `LogAnalytics` |
| Workspace | `spaarke-laws-prod` |
| SKU | `PerGB2018` (pay-per-GB) |
| Retention | 90 days |

### Connection String

The BFF API connects to Application Insights via a connection string stored in Key Vault:

```
Key Vault: sprk-platform-prod-kv
Secret Name: ApplicationInsights--ConnectionString
```

The BFF API loads this automatically via `appsettings.Production.json`:

```json
{
  "ApplicationInsights": {
    "ConnectionString": "@Microsoft.KeyVault(SecretUri=https://sprk-platform-prod-kv.vault.azure.net/secrets/ApplicationInsights--ConnectionString)"
  }
}
```

### Verifying Telemetry Ingestion

After deployment, confirm telemetry is flowing:

```bash
# Check App Insights for recent requests (last 5 minutes)
az monitor app-insights query \
  --app spaarke-appins-prod \
  --resource-group spe-infrastructure-westus2 \
  --analytics-query "requests | where timestamp > ago(5m) | count"
```

If the count is zero after the API has received traffic, check:
1. Connection string is set in App Service configuration
2. App Service can reach the App Insights ingestion endpoint
3. The `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable is present

---

## Key Metrics by Service

### BFF API (App Service)

| Metric | Source | Description | Healthy Range |
|--------|--------|-------------|---------------|
| `requests/count` | Built-in | Total HTTP requests | Depends on load |
| `requests/duration` | Built-in | Request latency (ms) | P95 < 500ms |
| `requests/failed` | Built-in | Failed requests (5xx) | < 1% of total |
| `dependencies/duration` | Built-in | External call latency | P95 < 2000ms |
| `availabilityResults/availabilityPercentage` | Availability test | Uptime percentage | > 99.5% |
| CPU Percentage | App Service | CPU utilization | < 70% sustained |
| Memory Percentage | App Service | Memory utilization | < 80% sustained |

### AI Features (Custom Metrics)

| Metric | Metric Name | Description | Healthy Range |
|--------|-------------|-------------|---------------|
| AI Requests | `customMetrics/ai.summarize.requests` | Total AI summarize calls | Baseline-dependent |
| AI Successes | `customMetrics/ai.summarize.successes` | Successful AI operations | > 90% of requests |
| AI Failures | `customMetrics/ai.summarize.failures` | Failed AI operations | < 10% of requests |
| AI Tokens | `customMetrics/ai.summarize.tokens` | Token consumption | Dynamic threshold |

### RAG (Retrieval-Augmented Generation)

| Metric | Metric Name | Description | Healthy Range |
|--------|-------------|-------------|---------------|
| RAG Requests | `customMetrics/ai.rag.requests` | RAG search operations | Baseline-dependent |
| RAG Duration | `customMetrics/ai.rag.duration` | End-to-end RAG latency (ms) | P95 < 3000ms |
| Embedding Duration | `customMetrics/ai.rag.embedding_duration` | Embedding generation time (ms) | P95 < 1000ms |
| Search Duration | `customMetrics/ai.rag.search_duration` | AI Search query time (ms) | P95 < 2000ms |

### Tool Execution

| Metric | Metric Name | Description | Healthy Range |
|--------|-------------|-------------|---------------|
| Tool Requests | `customMetrics/ai.tool.requests` | Tool invocations | Baseline-dependent |
| Tool Duration | `customMetrics/ai.tool.duration` | Tool execution latency (ms) | P95 < 5000ms |
| Tool Tokens | `customMetrics/ai.tool.tokens` | Tokens used by tools | Dynamic threshold |

### Export Operations

| Metric | Metric Name | Description | Healthy Range |
|--------|-------------|-------------|---------------|
| Export Requests | `customMetrics/ai.export.requests` | Export operations (DOCX, PDF, Email) | Baseline-dependent |
| Export Duration | `customMetrics/ai.export.duration` | Export generation time (ms) | P95 < 10000ms |
| Export File Size | `customMetrics/ai.export.file_size` | Average output file size (bytes) | < 10MB typical |

### Resilience and Caching

| Metric | Metric Name | Description | Healthy Range |
|--------|-------------|-------------|---------------|
| Circuit Breaker Opens | `customMetrics/circuit_breaker.open_count` | Open circuit breakers | 0 (any > 0 is critical) |
| Circuit State Transitions | `customMetrics/circuit_breaker.state_transitions` | State changes | Low frequency |
| Cache Hits | `customMetrics/cache.hits` | Successful cache lookups | > 80% hit rate |
| Cache Misses | `customMetrics/cache.misses` | Cache misses | < 20% miss rate |
| Cache Latency | `customMetrics/cache.latency` | Cache operation time (ms) | P95 < 10ms |

---

## Alert Rules

All alerts are deployed via `infrastructure/bicep/modules/alerts.bicep`. The action group sends email notifications to the configured admin address.

### Alert Summary

| # | Alert Name | Severity | Condition | Evaluation | Window |
|---|------------|----------|-----------|------------|--------|
| 1 | AI Failure Rate | **Critical (Sev 1)** | AI failure rate exceeds dynamic threshold | Every 5 min | 15 min |
| 2 | Circuit Breaker Open | **Critical (Sev 1)** | Any circuit breaker opens (count > 0) | Every 1 min | 5 min |
| 3 | RAG High Latency | Warning (Sev 2) | RAG duration average > 3000ms | Every 5 min | 15 min |
| 4 | Tool Execution Failures | Warning (Sev 2) | Tool request count drops (dynamic) | Every 5 min | 15 min |
| 5 | Cache Miss Spike | Warning (Sev 2) | Cache miss rate exceeds dynamic threshold | Every 5 min | 15 min |
| 6 | High Token Usage | Warning (Sev 2) | Token usage exceeds dynamic threshold | Every 1 hr | 6 hr |
| 7 | Export Failures | Warning (Sev 2) | Export request count drops (dynamic) | Every 5 min | 15 min |

### Alert Details

#### 1. AI Failure Rate (Critical)

- **Metric**: `customMetrics/ai.summarize.failures`
- **Threshold type**: Dynamic (Medium sensitivity)
- **Failing periods**: 3 of 4 evaluation periods
- **Auto-mitigate**: Yes
- **Response**: Check Azure OpenAI service health, verify API keys in Key Vault, check BFF API logs for error details.

#### 2. Circuit Breaker Open (Critical)

- **Metric**: `customMetrics/circuit_breaker.open_count`
- **Threshold type**: Static (> 0)
- **Failing periods**: 1 evaluation
- **Auto-mitigate**: Yes
- **Response**: A circuit breaker opening means a downstream service (Azure OpenAI, AI Search, Dataverse) is failing. Check the specific service status and the BFF API logs for the circuit name.

#### 3. RAG High Latency (Warning)

- **Metric**: `customMetrics/ai.rag.duration`
- **Threshold type**: Static (average > 3000ms)
- **Auto-mitigate**: Yes
- **Response**: Check AI Search service health, embedding model availability, and network latency. Review RAG latency breakdown (embedding vs. search duration) on the dashboard.

#### 4. Tool Execution Failures (Warning)

- **Metric**: `customMetrics/ai.tool.requests`
- **Threshold type**: Dynamic (High sensitivity, detects drops)
- **Failing periods**: 3 of 4 evaluation periods
- **Auto-mitigate**: Yes
- **Response**: Check individual tool handler logs. A sudden drop in tool requests may indicate the tool orchestrator is failing.

#### 5. Cache Miss Spike (Warning)

- **Metric**: `customMetrics/cache.misses`
- **Threshold type**: Dynamic (Medium sensitivity)
- **Failing periods**: 3 of 4 evaluation periods
- **Auto-mitigate**: Yes
- **Response**: Check Redis connectivity, memory usage, and eviction policy. A spike in cache misses may indicate Redis is down or keys are expiring unexpectedly.

#### 6. High Token Usage (Warning)

- **Metric**: `customMetrics/ai.summarize.tokens`
- **Threshold type**: Dynamic (Low sensitivity, avoids noise)
- **Evaluation**: Hourly, 6-hour window
- **Auto-mitigate**: Yes
- **Response**: Review token usage by customer and operation type. May indicate a runaway process or unexpected usage spike. Check Azure OpenAI cost dashboard for financial impact.

#### 7. Export Failures (Warning)

- **Metric**: `customMetrics/ai.export.requests`
- **Threshold type**: Dynamic (High sensitivity, detects drops)
- **Failing periods**: 3 of 4 evaluation periods
- **Auto-mitigate**: Yes
- **Response**: Check export handler logs for errors. Common causes: SharePoint Embedded storage issues, file format conversion failures, or Azure OpenAI unavailability.

### Configuring the Action Group

The default action group sends email notifications. To customize:

```bash
# Update action group email
az monitor action-group update \
  --name "spaarke-prod-action-group" \
  --resource-group spe-infrastructure-westus2 \
  --add-action email "Ops Team" ops@spaarke.com

# Add SMS notification
az monitor action-group update \
  --name "spaarke-prod-action-group" \
  --resource-group spe-infrastructure-westus2 \
  --add-action sms "On-Call" 1 5551234567

# Add webhook (e.g., for Slack/Teams)
az monitor action-group update \
  --name "spaarke-prod-action-group" \
  --resource-group spe-infrastructure-westus2 \
  --add-action webhook "Slack" "https://hooks.slack.com/services/..."
```

### Adding Custom Alerts

To add a new alert rule, add it to `infrastructure/bicep/modules/alerts.bicep` and redeploy the platform:

```bicep
resource customAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-your-alert-name'
  location: 'global'
  tags: tags
  properties: {
    description: 'Description of what this alert detects'
    severity: 2  // 0=Critical, 1=Error, 2=Warning, 3=Informational, 4=Verbose
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'YourCriteria'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'customMetrics/your.metric.name'
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
```

---

## Dashboard Setup

The production dashboard is deployed via `infrastructure/bicep/modules/dashboard.bicep` and is named **SDAP AI Monitoring Dashboard**.

### Dashboard Layout

The dashboard has 5 rows:

| Row | Section | Charts |
|-----|---------|--------|
| 1 | **Overview** | Total AI Requests, Success vs Failure, Open Circuit Breakers, Total Tokens Used |
| 2 | **RAG Performance** | RAG Search Latency, RAG Throughput, RAG Latency Breakdown (Embedding vs Search) |
| 3 | **Tool Execution** | Tool Executions, Tool Execution Latency, Tool Token Usage |
| 4 | **Export Operations** | Export Requests, Export Latency, Export File Size |
| 5 | **Resilience & Cache** | Circuit Breaker State Transitions, Cache Hit/Miss, Cache Latency |

### Accessing the Dashboard

1. Open the Azure Portal: https://portal.azure.com
2. Navigate to **Dashboard** (top-left hamburger menu)
3. Select **SDAP AI Monitoring Dashboard** from the dropdown
4. Default time range: 24 hours

### Customizing the Dashboard

**Change time range**: Use the time picker at the top-right of the dashboard.

**Add a new chart**:
1. Click **Edit** on the dashboard
2. Click **+ Add** and select **Metrics chart**
3. Select the `spaarke-appins-prod` resource
4. Choose the metric namespace and metric name
5. Configure aggregation and time range
6. Save the dashboard

**Pin a log query**:
1. Go to Application Insights > Logs
2. Run a KQL query
3. Click **Pin to dashboard**
4. Select the SDAP AI Monitoring Dashboard

### Creating Custom Dashboard Views

For customer-specific dashboards, clone the existing dashboard and add customer filters:

```bash
# Export current dashboard
az portal dashboard show \
  --name "spaarke-prod-dashboard" \
  --resource-group spe-infrastructure-westus2 \
  --output json > dashboard-template.json

# Modify and re-import with customer filters
az portal dashboard create \
  --name "spaarke-prod-dashboard-{customerId}" \
  --resource-group spe-infrastructure-westus2 \
  --input-path dashboard-template.json
```

---

## Customer-Scoped Log Queries (KQL)

Per **FR-12**, all BFF API logs include a `customerId` dimension. This enables filtering all telemetry by customer.

### Basic Customer Filter

```kql
// All requests for a specific customer in the last 24 hours
requests
| where timestamp > ago(24h)
| where customDimensions.customerId == "demo"
| summarize count(), avg(duration), percentile(duration, 95) by bin(timestamp, 1h)
| order by timestamp desc
```

### Customer Error Analysis

```kql
// Errors for a specific customer
exceptions
| where timestamp > ago(24h)
| where customDimensions.customerId == "demo"
| summarize count() by type, outerMessage
| order by count_ desc
```

### Customer AI Usage

```kql
// AI operations by customer
customMetrics
| where timestamp > ago(7d)
| where name startswith "ai."
| extend customerId = tostring(customDimensions.customerId)
| where customerId == "demo"
| summarize
    totalRequests = sumif(value, name == "ai.summarize.requests"),
    totalTokens = sumif(value, name == "ai.summarize.tokens"),
    avgDuration = avgif(value, name == "ai.summarize.duration")
  by bin(timestamp, 1d)
| order by timestamp desc
```

### Customer RAG Performance

```kql
// RAG latency breakdown by customer
customMetrics
| where timestamp > ago(24h)
| where name in ("ai.rag.duration", "ai.rag.embedding_duration", "ai.rag.search_duration")
| extend customerId = tostring(customDimensions.customerId)
| where customerId == "demo"
| summarize avg(value), percentile(value, 95) by name, bin(timestamp, 1h)
| order by timestamp desc
```

### Cross-Customer Comparison

```kql
// Compare key metrics across all customers (last 7 days)
customMetrics
| where timestamp > ago(7d)
| where name == "ai.summarize.requests"
| extend customerId = tostring(customDimensions.customerId)
| summarize totalRequests = sum(value) by customerId, bin(timestamp, 1d)
| order by timestamp desc, totalRequests desc
```

### Customer Health Summary

```kql
// Health summary for a specific customer
let customerId = "demo";
let timeRange = 24h;
requests
| where timestamp > ago(timeRange)
| where customDimensions.customerId == customerId
| summarize
    totalRequests = count(),
    failedRequests = countif(resultCode startswith "5"),
    avgLatency = avg(duration),
    p95Latency = percentile(duration, 95)
| extend
    errorRate = round(100.0 * failedRequests / totalRequests, 2),
    avgLatencyMs = round(avgLatency, 0),
    p95LatencyMs = round(p95Latency, 0)
| project totalRequests, failedRequests, errorRate, avgLatencyMs, p95LatencyMs
```

### Customer Document Operations

```kql
// Document operations by customer
customMetrics
| where timestamp > ago(24h)
| where name in ("ai.export.requests", "ai.export.duration", "ai.export.file_size")
| extend customerId = tostring(customDimensions.customerId)
| where customerId == "demo"
| summarize
    exports = sumif(value, name == "ai.export.requests"),
    avgDuration = avgif(value, name == "ai.export.duration"),
    avgFileSize = avgif(value, name == "ai.export.file_size")
  by bin(timestamp, 1h)
| order by timestamp desc
```

### Top Customers by Usage

```kql
// Top 10 customers by request volume (last 7 days)
requests
| where timestamp > ago(7d)
| extend customerId = tostring(customDimensions.customerId)
| where isnotempty(customerId)
| summarize requestCount = count(), avgDuration = avg(duration) by customerId
| top 10 by requestCount desc
```

---

## Cost Monitoring

### Application Insights Data Ingestion

Monitor ingestion volume to control costs:

```kql
// Daily data ingestion volume (GB)
union withsource=tt *
| where timestamp > ago(30d)
| summarize DataGB = sum(_BilledSize) / 1e9 by bin(timestamp, 1d)
| order by timestamp desc
```

```kql
// Ingestion by data type (identify largest contributors)
union withsource=tt *
| where timestamp > ago(7d)
| summarize DataGB = sum(_BilledSize) / 1e9 by tt
| order by DataGB desc
```

### Azure OpenAI Token Costs

```kql
// Daily token usage for cost estimation
customMetrics
| where timestamp > ago(30d)
| where name in ("ai.summarize.tokens", "ai.tool.tokens")
| summarize totalTokens = sum(value) by bin(timestamp, 1d), name
| order by timestamp desc
```

### Setting Up Azure Cost Alerts

```bash
# Create a budget alert for the monitoring resource group
az consumption budget create \
  --budget-name "monitoring-monthly" \
  --resource-group spe-infrastructure-westus2 \
  --amount 100 \
  --category cost \
  --time-grain monthly \
  --start-date 2026-04-01 \
  --end-date 2027-03-31

# Add notification at 80% threshold
az consumption budget create \
  --budget-name "monitoring-monthly" \
  --resource-group spe-infrastructure-westus2 \
  --amount 100 \
  --category cost \
  --time-grain monthly \
  --start-date 2026-04-01 \
  --end-date 2027-03-31 \
  --notifications "{\"Actual_GreaterThan_80_Percent\":{\"enabled\":true,\"operator\":\"GreaterThan\",\"threshold\":80,\"contactEmails\":[\"ops@spaarke.com\"]}}"
```

### Cost Optimization Tips

1. **Log Analytics retention**: Currently set to 90 days. Reduce to 30 days if historical data is not needed (saves storage costs).
2. **Sampling**: Enable adaptive sampling in Application Insights to reduce high-volume telemetry ingestion.
3. **Daily cap**: Set a daily ingestion cap in Application Insights to prevent cost overruns:
   ```bash
   az monitor app-insights component update \
     --app spaarke-appins-prod \
     --resource-group spe-infrastructure-westus2 \
     --ingestion-access Enabled \
     --query-access Enabled
   ```
4. **Archive old data**: Export data older than 30 days to a Storage Account for long-term retention at lower cost.

---

## On-Call Procedures

### Alert Response Workflow

```
Alert Fired
  │
  ├─ Severity 1 (Critical): Respond within 15 minutes
  │   ├─ Circuit Breaker Open → Check downstream service status
  │   └─ AI Failure Rate High → Check Azure OpenAI, Key Vault secrets
  │
  └─ Severity 2 (Warning): Respond within 1 hour
      ├─ RAG High Latency → Check AI Search, embedding model
      ├─ Tool Failures → Check tool handler logs
      ├─ Cache Miss Spike → Check Redis health
      ├─ High Token Usage → Review customer usage patterns
      └─ Export Failures → Check SPE storage, file conversion
```

### Initial Triage Steps

For any alert:

1. **Check the BFF API health endpoint**:
   ```bash
   curl -s https://api.spaarke.com/healthz | jq .
   curl -s https://api.spaarke.com/healthz/dataverse | jq .
   ```

2. **Check App Service status**:
   ```bash
   az webapp show --name spaarke-bff-prod --resource-group spe-infrastructure-westus2 --query state
   ```

3. **Check recent errors in Application Insights**:
   ```kql
   exceptions
   | where timestamp > ago(1h)
   | summarize count() by type, outerMessage
   | order by count_ desc
   | take 10
   ```

4. **Check dependency health**:
   ```kql
   dependencies
   | where timestamp > ago(1h)
   | where success == false
   | summarize count() by target, type, resultCode
   | order by count_ desc
   ```

### Escalation Matrix

| Severity | Initial Response | Escalation (30 min) | Escalation (1 hr) |
|----------|-----------------|---------------------|-------------------|
| Critical (Sev 1) | On-call engineer | Engineering lead | VP Engineering |
| Warning (Sev 2) | On-call engineer | Engineering lead | — |
| Informational | Review next business day | — | — |

### Common Scenarios and Resolutions

#### Circuit Breaker Open

1. Identify which circuit breaker opened:
   ```kql
   customEvents
   | where timestamp > ago(1h)
   | where name == "CircuitBreakerStateChanged"
   | project timestamp, customDimensions.circuitName, customDimensions.newState
   | order by timestamp desc
   ```
2. Check the target service status (Azure OpenAI, AI Search, Dataverse)
3. If the service is down, wait for recovery (circuit will auto-close after half-open test succeeds)
4. If the service is up, check for network issues or throttling

#### High AI Failure Rate

1. Check Azure OpenAI service health: https://status.azure.com
2. Verify API key in Key Vault is valid:
   ```bash
   az keyvault secret show --vault-name sprk-platform-prod-kv --name AzureOpenAI--ApiKey --query value -o tsv | head -c 5
   ```
3. Check for rate limiting (HTTP 429):
   ```kql
   dependencies
   | where timestamp > ago(1h)
   | where target contains "openai"
   | where resultCode == "429"
   | summarize count() by bin(timestamp, 5m)
   ```
4. If rate-limited, reduce concurrent requests or increase Azure OpenAI quota

#### Cache Miss Spike

1. Check Redis connectivity:
   ```bash
   az redis show --name sprk-platform-prod-redis --resource-group spe-infrastructure-westus2 --query provisioningState
   ```
2. Check Redis memory usage (should be < 80%)
3. Review eviction policy -- if keys are being evicted, consider increasing Redis size
4. Check if a deployment recently cleared the cache (expected temporary spike)

---

## Maintenance and Tuning

### Weekly Review Checklist

- [ ] Review dashboard for anomalies in the past 7 days
- [ ] Check data ingestion volume (cost control)
- [ ] Review alert firing history -- tune thresholds if too noisy
- [ ] Verify customer-scoped queries return expected data
- [ ] Check Log Analytics retention and archive status

### Monthly Review Checklist

- [ ] Review and update alert thresholds based on baseline data
- [ ] Audit action group recipients (are the right people notified?)
- [ ] Review cost trends and optimize if needed
- [ ] Check for new custom metrics that should be added to the dashboard
- [ ] Export monthly customer usage reports

### Tuning Alert Thresholds

Alerts use two threshold types:

**Static thresholds** (fixed values):
- Circuit Breaker Open: > 0 (should remain at 0)
- RAG High Latency: > 3000ms (adjust based on observed P95)

**Dynamic thresholds** (ML-based baselines):
- AI Failure Rate: Medium sensitivity
- Tool Failures: High sensitivity
- Cache Misses: Medium sensitivity
- Token Usage: Low sensitivity (avoids noise from natural variation)

To change sensitivity, update `alertSensitivity` in `alerts.bicep`:
- `High`: Tight bounds, more alerts (use for critical metrics)
- `Medium`: Balanced (default for most metrics)
- `Low`: Wide bounds, fewer alerts (use for noisy metrics)

### Adding New Custom Metrics

When the BFF API emits a new custom metric:

1. Add the metric to the relevant dashboard section in `dashboard.bicep`
2. If the metric needs alerting, add a rule in `alerts.bicep`
3. Redeploy the platform: `./scripts/Deploy-Platform.ps1 -Environment prod`
4. Update this guide with the new metric details

---

*This guide covers the monitoring stack deployed by task 020 (Deploy shared platform). For incident response procedures, see [INCIDENT-RESPONSE-PROCEDURES.md](INCIDENT-RESPONSE-PROCEDURES.md). For secret rotation, see [SECRET-ROTATION-PROCEDURES.md](SECRET-ROTATION-PROCEDURES.md).*
