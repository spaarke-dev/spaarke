# AI Monitoring Dashboard Guide

> **Last Updated**: December 2025
> **Applies To**: SDAP AI Document Intelligence features

---

## Overview

The SDAP AI Monitoring Dashboard provides real-time visibility into AI feature health, performance, and reliability. It is deployed as an Azure Dashboard and integrates with Application Insights metrics collected from the Sprk.Bff.Api.

## Dashboard Panels

### Row 1: Overview Tiles

| Panel | Metrics | Purpose |
|-------|---------|---------|
| Total AI Requests | `ai.summarize.requests` | Overall volume of AI operations |
| Success vs Failure | `ai.summarize.successes`, `ai.summarize.failures` | Error rate monitoring |
| Open Circuit Breakers | `circuit_breaker.open_count` | Service health status |
| Total Tokens Used | `ai.summarize.tokens` | Cost tracking |

### Row 2: RAG Performance

| Panel | Metrics | Purpose |
|-------|---------|---------|
| RAG Search Latency | `ai.rag.duration` (avg) | Overall search performance |
| RAG Throughput | `ai.rag.requests` | Search volume |
| Latency Breakdown | `ai.rag.embedding_duration`, `ai.rag.search_duration` | Performance bottleneck identification |

### Row 3: Tool Execution

| Panel | Metrics | Purpose |
|-------|---------|---------|
| Tool Executions | `ai.tool.requests` | Tool usage volume |
| Tool Latency | `ai.tool.duration` (avg) | Tool performance |
| Tool Token Usage | `ai.tool.tokens` | Per-tool cost tracking |

### Row 4: Export Operations

| Panel | Metrics | Purpose |
|-------|---------|---------|
| Export Requests | `ai.export.requests` | Export volume by format |
| Export Latency | `ai.export.duration` (avg) | Export performance |
| Export File Size | `ai.export.file_size` (avg) | Output size monitoring |

### Row 5: Resilience & Cache

| Panel | Metrics | Purpose |
|-------|---------|---------|
| Circuit Breaker Transitions | `circuit_breaker.state_transitions` | Service stability |
| Cache Hit/Miss | `cache.hits`, `cache.misses` | Cache effectiveness |
| Cache Latency | `cache.latency` (avg) | Cache performance |

---

## Alert Rules

The following alerts are configured for critical thresholds:

| Alert | Condition | Severity | Description |
|-------|-----------|----------|-------------|
| AI Failure Rate | Dynamic threshold | Critical | AI request failures spike |
| Circuit Breaker Open | `open_count > 0` | Critical | External service degraded |
| High RAG Latency | `duration > 3000ms` | Warning | Search performance degraded |
| Tool Failures | Dynamic threshold | Warning | Tool execution failures |
| Cache Miss Spike | Dynamic threshold | Warning | Cache effectiveness drop |
| High Token Usage | Dynamic threshold | Warning | Cost impact alert |
| Export Failures | Dynamic threshold | Warning | Export operation failures |

---

## Accessing the Dashboard

### Azure Portal

1. Navigate to **Azure Portal** > **Dashboard**
2. Search for `{environment}-ai-dashboard` (e.g., `sprkcontosoprod-ai-dashboard`)
3. Pin to favorites for quick access

### Direct URL

```
https://portal.azure.com/#@{tenant}/dashboard/arm/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Portal/dashboards/{dashboardName}
```

### Sharing with Ops Team

1. Open dashboard in Azure Portal
2. Click **Share** in the toolbar
3. Choose **Publish to dashboard gallery** for organization-wide access
4. Or use **Access control (IAM)** to grant specific users/groups Reader role

---

## Metric Dimensions

Metrics include the following dimensions for filtering:

| Dimension | Values | Use Case |
|-----------|--------|----------|
| `ai.status` | `success`, `failed` | Filter by outcome |
| `ai.method` | `streaming`, `batch` | Processing method |
| `ai.extraction` | `native`, `document_intelligence`, `vision` | Text extraction method |
| `ai.file_type` | `.pdf`, `.docx`, `.txt`, etc. | File type analysis |
| `ai.tool_id` | `EntityExtractor`, `ClauseAnalyzer`, `DocumentClassifier` | Tool-specific metrics |
| `ai.format` | `docx`, `pdf`, `email` | Export format |
| `ai.error_code` | Various error codes | Error analysis |
| `ai.cache_hit` | `true`, `false` | Cache effectiveness |
| `service` | `AzureOpenAI`, `AzureAISearch`, `MicrosoftGraph` | Circuit breaker service |

---

## Custom Queries

### Application Insights KQL Queries

**RAG Latency P95:**
```kusto
customMetrics
| where name == "ai.rag.duration"
| summarize percentile(value, 95) by bin(timestamp, 5m)
| render timechart
```

**Tool Success Rate by Type:**
```kusto
customMetrics
| where name == "ai.tool.requests"
| extend status = tostring(customDimensions["ai.status"])
| extend tool = tostring(customDimensions["ai.tool_id"])
| summarize count() by tool, status
| render piechart
```

**Export Volume by Format:**
```kusto
customMetrics
| where name == "ai.export.requests"
| extend format = tostring(customDimensions["ai.format"])
| summarize count() by format, bin(timestamp, 1h)
| render columnchart
```

**Circuit Breaker Events:**
```kusto
customMetrics
| where name == "circuit_breaker.state_transitions"
| extend service = tostring(customDimensions["service"])
| extend state = tostring(customDimensions["state"])
| summarize count() by service, state, bin(timestamp, 5m)
| render timechart
```

**Cache Hit Rate:**
```kusto
customMetrics
| where name in ("cache.hits", "cache.misses")
| summarize hits = sumif(value, name == "cache.hits"),
            misses = sumif(value, name == "cache.misses")
  by bin(timestamp, 5m)
| extend hitRate = hits * 100.0 / (hits + misses)
| render timechart
```

---

## API Endpoints

Real-time circuit breaker status is also available via API:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/resilience/circuits` | GET | All circuit breaker states |
| `/api/resilience/circuits/{serviceName}` | GET | Specific circuit state |
| `/api/resilience/health` | GET | Overall resilience health (503 if any open) |

**Example Response:**
```json
{
  "circuits": [
    {
      "serviceName": "AzureOpenAI",
      "state": "Closed",
      "lastStateChange": "2025-12-30T10:00:00Z",
      "consecutiveFailures": 0,
      "isAvailable": true
    }
  ],
  "timestamp": "2025-12-30T10:30:00Z",
  "openCount": 0,
  "halfOpenCount": 0,
  "closedCount": 3
}
```

---

## Deployment

### Bicep Deployment

The dashboard is deployed via the `model2-full.bicep` stack:

```bash
az deployment sub create \
  --location eastus \
  --template-file infrastructure/bicep/stacks/model2-full.bicep \
  --parameters customerId=contoso \
               environment=prod \
               enableMonitoringDashboard=true \
               alertNotificationEmail=ops@contoso.com
```

### Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `enableMonitoringDashboard` | `true` | Deploy AI monitoring dashboard |
| `alertNotificationEmail` | `''` | Email for alert notifications |

---

## Troubleshooting

### No Data in Dashboard

1. **Verify Application Insights connection**: Check `APPLICATIONINSIGHTS_CONNECTION_STRING` in App Service
2. **Check metric collection**: Ensure `AiTelemetry` is registered in DI
3. **Wait for data**: Metrics may take 2-5 minutes to appear

### Alerts Not Firing

1. **Check action group configuration**: Verify email addresses in action group
2. **Verify thresholds**: Review alert rule conditions in Azure Portal
3. **Check suppression rules**: Ensure no maintenance windows are active

### High Latency Alerts

1. **Check circuit breaker states**: Use `/api/resilience/health`
2. **Review Azure OpenAI throttling**: Check for 429 responses in logs
3. **Analyze cache hit rate**: Low hit rate may indicate cache issues

---

## Related Documentation

- [SPAARKE-AI-ARCHITECTURE.md](SPAARKE-AI-ARCHITECTURE.md) - AI feature architecture
- [RAG-TROUBLESHOOTING.md](RAG-TROUBLESHOOTING.md) - RAG-specific troubleshooting
- [AI-DEPLOYMENT-GUIDE.md](AI-DEPLOYMENT-GUIDE.md) - Deployment procedures

---

*Generated for AI Document Intelligence R3 - Phase 5: Production Readiness*
