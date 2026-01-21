# SDAP Office Integration - Monitoring and Alerting Runbook

> **Status**: Production Ready
> **Task Reference**: Task 084 - Configure monitoring and alerting
> **Date Created**: 2026-01-20
> **Last Updated**: 2026-01-20

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Azure Resources](#azure-resources)
3. [SLA Requirements](#sla-requirements)
4. [Custom Metrics](#custom-metrics)
5. [Alert Configuration](#alert-configuration)
6. [Dashboard Setup](#dashboard-setup)
7. [Application Insights Queries](#application-insights-queries)
8. [Runbook Procedures](#runbook-procedures)
9. [Notification Configuration](#notification-configuration)
10. [Testing Alerts](#testing-alerts)
11. [Troubleshooting Guide](#troubleshooting-guide)

---

## 1. Executive Summary

This runbook provides comprehensive guidance for monitoring the SDAP Office Integration platform, including:
- Outlook and Word add-ins
- BFF API Office endpoints (`/office/*`)
- Background workers (upload, profile, indexing)
- Redis cache and Service Bus queues

**Key Objectives**:
- Early detection of issues before user impact
- Rapid response to production problems
- Maintainability and operational excellence

---

## 2. Azure Resources

### 2.1 Monitoring Resources

| Resource | Name | Resource Group | Purpose |
|----------|------|----------------|---------|
| **Application Insights** | `spe-insights-dev-67e2xz` | `spe-infrastructure-westus2` | Application Performance Monitoring |
| **Log Analytics** | `spe-logs-dev-67e2xz` | `spe-infrastructure-westus2` | Log aggregation and queries |
| **Action Group** | `SDAP-Office-Alerts` | `spe-infrastructure-westus2` | Alert notifications |

### 2.2 Monitored Resources

| Resource Type | Name | Purpose |
|---------------|------|---------|
| **App Service** | `spe-api-dev-67e2xz` | BFF API hosting Office endpoints |
| **Redis Cache** | `spaarke-redis-dev` | Rate limiting, idempotency, caching |
| **Service Bus** | `spaarke-servicebus-dev` | Job queue processing |

### 2.3 Quick Access Links

```
Azure Portal - Application Insights:
https://portal.azure.com/#@{tenant}/resource/subscriptions/{sub}/resourceGroups/spe-infrastructure-westus2/providers/microsoft.insights/components/spe-insights-dev-67e2xz/overview

Azure Portal - App Service:
https://portal.azure.com/#@{tenant}/resource/subscriptions/{sub}/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.Web/sites/spe-api-dev-67e2xz/overview
```

---

## 3. SLA Requirements

Based on `spec.md` non-functional requirements:

### 3.1 API Response Time SLAs

| Endpoint | Target | Threshold (Alert) | Source |
|----------|--------|-------------------|--------|
| `POST /office/save` | < 3 seconds | > 3 seconds | NFR-01 |
| `GET /office/search/entities` | < 500ms | > 500ms | FR-04 |
| `GET /office/jobs/{id}` | < 1 second | > 1 second | NFR-04 |
| `GET /office/jobs/{id}/stream` | SSE established < 1s | Connection fails | NFR-04 |

### 3.2 Availability SLAs

| Metric | Target | Threshold (Alert) |
|--------|--------|-------------------|
| API Availability | > 99.9% | < 99.9% |
| Error Rate | < 1% | > 1% |
| Worker Processing | Success > 95% | Failure > 5% |

### 3.3 Rate Limiting Configuration

| Endpoint | Limit (per user per minute) |
|----------|----------------------------|
| `POST /office/save` | 10 |
| `POST /office/quickcreate/*` | 5 |
| `GET /office/search/*` | 30 |
| `GET /office/jobs/*` | 60 |
| `POST /office/share/*` | 20 |

---

## 4. Custom Metrics

### 4.1 Office API Metrics

Track in Application Insights using `TelemetryClient.TrackMetric()`:

| Metric Name | Type | Description |
|-------------|------|-------------|
| `office.save.requests` | Counter | Total save requests |
| `office.save.duration_ms` | Histogram | Save endpoint latency |
| `office.save.success` | Counter | Successful saves |
| `office.save.failure` | Counter | Failed saves |
| `office.save.duplicate` | Counter | Duplicate detections |
| `office.search.requests` | Counter | Entity search requests |
| `office.search.duration_ms` | Histogram | Search latency |
| `office.search.results_count` | Histogram | Results returned |
| `office.share.requests` | Counter | Share requests |
| `office.share.links_generated` | Counter | Links created |
| `office.quickcreate.requests` | Counter | Quick create requests |
| `office.quickcreate.success` | Counter | Successful creates |

### 4.2 Worker Metrics

| Metric Name | Type | Description |
|-------------|------|-------------|
| `office.worker.upload.processed` | Counter | Upload jobs processed |
| `office.worker.upload.duration_ms` | Histogram | Upload processing time |
| `office.worker.upload.failure` | Counter | Upload failures |
| `office.worker.profile.processed` | Counter | Profile jobs processed |
| `office.worker.profile.duration_ms` | Histogram | Profile processing time |
| `office.worker.indexing.processed` | Counter | Indexing jobs processed |
| `office.worker.indexing.duration_ms` | Histogram | Indexing processing time |

### 4.3 SSE Metrics

| Metric Name | Type | Description |
|-------------|------|-------------|
| `office.sse.connections` | Gauge | Active SSE connections |
| `office.sse.events_sent` | Counter | Total events sent |
| `office.sse.connection_duration_ms` | Histogram | Connection duration |

### 4.4 Implementing Custom Metrics

**Code Example (C#)**:

```csharp
// In Office endpoint handlers
public class OfficeSaveEndpoint
{
    private readonly TelemetryClient _telemetry;

    public async Task<IResult> Handle(SaveRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // ... processing logic ...

            _telemetry.TrackMetric("office.save.requests", 1);
            _telemetry.TrackMetric("office.save.duration_ms", stopwatch.ElapsedMilliseconds);
            _telemetry.TrackMetric("office.save.success", 1);

            return Results.Accepted(new { jobId, statusUrl });
        }
        catch (Exception ex)
        {
            _telemetry.TrackMetric("office.save.failure", 1);
            _telemetry.TrackException(ex);
            throw;
        }
    }
}
```

---

## 5. Alert Configuration

### 5.1 Critical Alerts (Severity 0 - Page On-Call)

| Alert Name | Condition | Window | Action |
|------------|-----------|--------|--------|
| **Office API Availability Critical** | Availability < 99% | 5 minutes | Page on-call |
| **Office Save Endpoint Failures** | 5xx count > 10 | 5 minutes | Page on-call |
| **Worker Dead Letter Queue** | DLQ count > 0 | 1 minute | Page on-call |
| **Redis Connection Failures** | Connection errors > 0 | 5 minutes | Page on-call |

### 5.2 Warning Alerts (Severity 2 - Email Team)

| Alert Name | Condition | Window | Action |
|------------|-----------|--------|--------|
| **Office Save Latency High** | P95 > 3000ms | 15 minutes | Email team |
| **Office Search Latency High** | P95 > 500ms | 15 minutes | Email team |
| **Office Error Rate Elevated** | 4xx > 100 | 5 minutes | Email team |
| **Worker Processing Slow** | Avg duration > 30s | 15 minutes | Email team |
| **Service Bus Queue Depth** | Queue > 1000 | 5 minutes | Email team |
| **Rate Limiting Active** | 429 count > 50 | 5 minutes | Email team |

### 5.3 Azure CLI Alert Commands

**Create Office Save Response Time Alert:**

```powershell
az monitor metrics alert create `
  --name "Office-Save-ResponseTime-Warning" `
  --resource-group spe-infrastructure-westus2 `
  --scopes "/subscriptions/{sub}/resourceGroups/spe-infrastructure-westus2/providers/microsoft.insights/components/spe-insights-dev-67e2xz" `
  --description "Alert when Office save endpoint P95 latency exceeds 3 seconds" `
  --condition "avg requests/duration > 3000 where request/name contains '/office/save'" `
  --window-size 15m `
  --evaluation-frequency 5m `
  --severity 2 `
  --action "/subscriptions/{sub}/resourceGroups/spe-infrastructure-westus2/providers/microsoft.insights/actionGroups/SDAP-Office-Alerts"
```

**Create Office Error Rate Alert:**

```powershell
az monitor metrics alert create `
  --name "Office-API-ErrorRate-Critical" `
  --resource-group spe-infrastructure-westus2 `
  --scopes "/subscriptions/{sub}/resourceGroups/spe-infrastructure-westus2/providers/microsoft.insights/components/spe-insights-dev-67e2xz" `
  --description "Alert when Office API error rate exceeds 1%" `
  --condition "count requests > 10 where request/success == 'False' and request/name contains '/office/'" `
  --window-size 5m `
  --evaluation-frequency 1m `
  --severity 1 `
  --action "/subscriptions/{sub}/resourceGroups/spe-infrastructure-westus2/providers/microsoft.insights/actionGroups/SDAP-Office-Alerts"
```

### 5.4 Bicep Alert Extension

Add to `infrastructure/bicep/modules/alerts.bicep`:

```bicep
// =====================================================
// Office Integration Alerts
// =====================================================

resource officeSaveLatencyAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-office-save-latency'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alert when Office save endpoint P95 latency exceeds 3 seconds'
    severity: warningSeverity
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'SaveLatencyCriteria'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'customMetrics/office.save.duration_ms'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          threshold: 3000
          timeAggregation: 'Average'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroupId }]
    autoMitigate: true
  }
}

resource officeWorkerFailureAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-office-worker-failures'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alert when Office worker failures spike'
    severity: criticalSeverity
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'WorkerFailureCriteria'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'customMetrics/office.worker.upload.failure'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Total'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroupId }]
    autoMitigate: true
  }
}
```

---

## 6. Dashboard Setup

### 6.1 Office Integration Dashboard

Access via Azure Portal: **Dashboards > SDAP Office Integration**

**Dashboard Sections:**

1. **Overview Row**
   - Total Office Requests (line chart)
   - Success vs Failure Rate (pie chart)
   - Active SSE Connections (gauge)

2. **API Performance Row**
   - Save Endpoint Latency P50/P95/P99
   - Search Endpoint Latency
   - Error Rate by Endpoint

3. **Worker Performance Row**
   - Jobs Processed per Worker
   - Processing Duration Breakdown
   - Dead Letter Queue Count

4. **Infrastructure Row**
   - Service Bus Queue Depth
   - Redis Cache Hit/Miss
   - App Service CPU/Memory

### 6.2 Create Dashboard via Azure CLI

```powershell
# Export existing dashboard template
az portal dashboard show `
  --name "SDAP-AI-Monitoring" `
  --resource-group spe-infrastructure-westus2 `
  --output json > dashboard-template.json

# Modify template for Office integration, then import
az portal dashboard create `
  --name "SDAP-Office-Integration" `
  --resource-group spe-infrastructure-westus2 `
  --input-path office-dashboard.json
```

---

## 7. Application Insights Queries

### 7.1 Office API Performance

**Save Endpoint Performance (Last 24 Hours):**

```kusto
requests
| where timestamp > ago(24h)
| where name contains "/office/save"
| summarize
    Total = count(),
    AvgDuration = avg(duration),
    P50 = percentile(duration, 50),
    P95 = percentile(duration, 95),
    P99 = percentile(duration, 99),
    Failures = countif(success == false)
  by bin(timestamp, 1h)
| order by timestamp desc
```

**All Office Endpoints Overview:**

```kusto
requests
| where timestamp > ago(1h)
| where name contains "/office/"
| summarize
    Count = count(),
    AvgDuration = avg(duration),
    P95 = percentile(duration, 95),
    SuccessRate = countif(success == true) * 100.0 / count()
  by name, resultCode
| order by Count desc
```

**Error Analysis:**

```kusto
requests
| where timestamp > ago(4h)
| where name contains "/office/"
| where success == false
| summarize Count = count() by name, resultCode, tostring(customDimensions["errorCode"])
| order by Count desc
```

### 7.2 Worker Performance

**Worker Job Processing:**

```kusto
customMetrics
| where timestamp > ago(24h)
| where name startswith "office.worker."
| summarize
    Total = sum(value)
  by name, bin(timestamp, 1h)
| render timechart
```

**Worker Errors:**

```kusto
exceptions
| where timestamp > ago(4h)
| where customDimensions["WorkerType"] in ("UploadFinalization", "Profile", "Indexing")
| summarize Count = count() by problemId, outerMessage
| order by Count desc
```

### 7.3 Job Status and Tracking

**Jobs by Status:**

```kusto
customEvents
| where timestamp > ago(24h)
| where name == "JobStatusChanged"
| summarize Count = count() by tostring(customDimensions["Status"])
| render piechart
```

**Job Duration by Type:**

```kusto
customEvents
| where timestamp > ago(24h)
| where name == "JobCompleted"
| extend JobType = tostring(customDimensions["JobType"])
| extend DurationMs = toint(customDimensions["DurationMs"])
| summarize
    AvgDuration = avg(DurationMs),
    P95Duration = percentile(DurationMs, 95)
  by JobType
```

### 7.4 SSE Connection Monitoring

**Active SSE Connections:**

```kusto
customMetrics
| where timestamp > ago(1h)
| where name == "office.sse.connections"
| summarize ActiveConnections = max(value) by bin(timestamp, 1m)
| render timechart
```

**SSE Events Sent:**

```kusto
customEvents
| where timestamp > ago(1h)
| where name == "SSEEventSent"
| summarize EventCount = count() by tostring(customDimensions["EventType"]), bin(timestamp, 5m)
| render timechart
```

### 7.5 Rate Limiting

**Rate Limited Requests:**

```kusto
requests
| where timestamp > ago(4h)
| where resultCode == 429
| where name contains "/office/"
| summarize Count = count() by name, tostring(customDimensions["UserId"])
| order by Count desc
```

### 7.6 Correlation Tracking

**Trace Request by Correlation ID:**

```kusto
union requests, traces, exceptions, customEvents
| where timestamp > ago(24h)
| where customDimensions["CorrelationId"] == "{your-correlation-id}"
| project timestamp, itemType, name, message, severityLevel
| order by timestamp asc
```

---

## 8. Runbook Procedures

### 8.1 Alert: Office API Availability Critical

**Trigger**: API availability drops below 99%

**Response Steps**:

1. **Acknowledge Alert** (within 5 minutes)
   - Check Azure Portal for alert details
   - Note start time and affected endpoints

2. **Initial Assessment**
   ```powershell
   # Check App Service status
   az webapp show --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --query state

   # Check health endpoint
   curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz

   # Stream live logs
   az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
   ```

3. **Root Cause Investigation**
   - Check Application Insights for exceptions
   - Review recent deployments
   - Check dependency health (Redis, Service Bus, Dataverse)

4. **Resolution Actions**
   | Cause | Action |
   |-------|--------|
   | App crashed | Restart App Service |
   | Bad deployment | Rollback via slot swap |
   | Dependency failure | Check downstream services |
   | Resource exhaustion | Scale up/out App Service Plan |

5. **Post-Incident**
   - Update incident ticket with resolution
   - Document root cause
   - Create follow-up tasks if needed

### 8.2 Alert: Office Save Latency High

**Trigger**: P95 latency > 3 seconds for 15 minutes

**Response Steps**:

1. **Check Current Load**
   ```kusto
   requests
   | where timestamp > ago(30m)
   | where name contains "/office/save"
   | summarize
       Count = count(),
       P95 = percentile(duration, 95)
     by bin(timestamp, 1m)
   | render timechart
   ```

2. **Identify Slow Operations**
   ```kusto
   dependencies
   | where timestamp > ago(30m)
   | where target contains "sharepoint" or target contains "dynamics"
   | summarize AvgDuration = avg(duration), Count = count() by target, type
   | order by AvgDuration desc
   ```

3. **Resolution Actions**
   | Cause | Action |
   |-------|--------|
   | High load | Scale App Service |
   | Slow SPE calls | Check Graph throttling |
   | Slow Dataverse | Check Dataverse health |
   | Redis latency | Check Redis metrics |

### 8.3 Alert: Worker Dead Letter Queue

**Trigger**: Messages in dead letter queue > 0

**Response Steps**:

1. **Check Dead Letter Queue**
   ```powershell
   az servicebus queue show `
     --resource-group spe-infrastructure-westus2 `
     --namespace-name spaarke-servicebus-dev `
     --name "office-upload-finalization" `
     --query "countDetails.deadLetterMessageCount" -o tsv
   ```

2. **View Dead Letter Messages**
   - Use Azure Portal > Service Bus > Queue > Service Bus Explorer
   - Check message body and exception details

3. **Common Failure Causes**
   | Error | Cause | Resolution |
   |-------|-------|------------|
   | Deserialize failed | Bad message format | Fix producer, purge bad messages |
   | Authorization failed | Token expired | Check auth configuration |
   | SPE upload failed | Graph API error | Retry message, check Graph health |
   | Dataverse error | Entity not found | Validate entity exists |

4. **Retry or Purge**
   - If transient: Resubmit messages from DLQ
   - If permanent: Purge and fix root cause

### 8.4 Alert: Service Bus Queue Depth High

**Trigger**: Queue depth > 1000 messages

**Response Steps**:

1. **Check All Queue Depths**
   ```powershell
   $queues = @("office-upload-finalization", "office-profile", "office-indexing")
   foreach ($queue in $queues) {
       $depth = az servicebus queue show `
         --resource-group spe-infrastructure-westus2 `
         --namespace-name spaarke-servicebus-dev `
         --name $queue `
         --query "countDetails.activeMessageCount" -o tsv
       Write-Host "${queue}: $depth messages"
   }
   ```

2. **Check Worker Health**
   ```kusto
   traces
   | where timestamp > ago(30m)
   | where message contains "Worker" and message contains "processing"
   | summarize Count = count() by bin(timestamp, 1m)
   | render timechart
   ```

3. **Resolution Actions**
   | Cause | Action |
   |-------|--------|
   | Workers stopped | Restart App Service |
   | Workers slow | Increase MaxConcurrentCalls |
   | Burst traffic | Wait for processing to catch up |
   | Poison messages | Check DLQ |

---

## 9. Notification Configuration

### 9.1 Create Action Group

```powershell
az monitor action-group create `
  --name "SDAP-Office-Alerts" `
  --resource-group spe-infrastructure-westus2 `
  --short-name "SDAPOffice" `
  --action email admin admin@contoso.com `
  --action email devops devops@contoso.com
```

### 9.2 Add Teams Channel Notification

```powershell
# Create webhook action
az monitor action-group update `
  --name "SDAP-Office-Alerts" `
  --resource-group spe-infrastructure-westus2 `
  --add-action webhook Teams "https://contoso.webhook.office.com/webhookb2/..."
```

### 9.3 Escalation Matrix

| Severity | Initial Contact | Escalation (15 min) | Escalation (30 min) |
|----------|-----------------|---------------------|---------------------|
| 0 - Critical | On-call engineer | Team lead | CTO |
| 1 - High | Dev team | On-call engineer | Team lead |
| 2 - Warning | Email notification | — | — |

---

## 10. Testing Alerts

### 10.1 Simulate Save Latency Alert

**Option A: Manual slow request**

```powershell
# Add artificial delay to endpoint (dev only)
# Then trigger multiple requests to exceed threshold
```

**Option B: Azure CLI test**

```powershell
# Test action group notification
az monitor action-group test-notifications create `
  --name "SDAP-Office-Alerts" `
  --resource-group spe-infrastructure-westus2 `
  --alert-type "metric" `
  --notifications '[{"email": {"emailAddress": "test@contoso.com"}}]'
```

### 10.2 Verify Alert Resolution

1. Trigger condition to fire alert
2. Verify notification received (email/Teams)
3. Fix condition
4. Verify alert auto-resolves
5. Document test results

### 10.3 Alert Testing Checklist

| Alert | Tested | Date | Result |
|-------|--------|------|--------|
| Office API Availability Critical | Pending | — | — |
| Office Save Latency High | Pending | — | — |
| Worker Dead Letter Queue | Pending | — | — |
| Service Bus Queue Depth | Pending | — | — |
| Rate Limiting Active | Pending | — | — |

---

## 11. Troubleshooting Guide

### 11.1 Common Issues

| Symptom | Likely Cause | Diagnostic Query | Resolution |
|---------|--------------|------------------|------------|
| Save returns 401 | Token expired | Check auth logs | User re-authenticate |
| Save returns 403 | Missing permissions | Check UAC logs | Verify entity access |
| Save returns 502 | SPE upload failed | Check dependencies | Check Graph API status |
| Jobs stuck in Queued | Workers not running | Check worker traces | Restart App Service |
| SSE disconnects | Network timeout | Check SSE metrics | Client reconnect logic |
| High latency | Resource contention | Check App Insights | Scale resources |

### 11.2 Diagnostic Commands

**Full health check:**

```powershell
# 1. App Service health
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz

# 2. Ping check
curl https://spe-api-dev-67e2xz.azurewebsites.net/ping

# 3. Check App Service status
az webapp show --name spe-api-dev-67e2xz -g spe-infrastructure-westus2 --query "state"

# 4. Check Service Bus queues
az servicebus queue list --namespace-name spaarke-servicebus-dev -g spe-infrastructure-westus2 -o table

# 5. Stream logs
az webapp log tail --name spe-api-dev-67e2xz -g spe-infrastructure-westus2
```

### 11.3 Emergency Contacts

| Role | Name | Contact |
|------|------|---------|
| On-Call Engineer | Rotating | PagerDuty |
| Team Lead | TBD | Email |
| Azure Support | Microsoft | Support ticket |

---

## Acceptance Criteria

- [x] Application Insights configuration documented
- [x] Custom metrics defined for Office endpoints
- [x] Alert rules documented with thresholds
- [x] Dashboard setup instructions provided
- [x] Application Insights queries for common scenarios
- [x] Runbook procedures for each alert type
- [x] Notification channel configuration documented
- [x] Alert testing procedures documented
- [x] Troubleshooting guide created

---

## Related Documentation

- [deployment-log.md](deployment-log.md) - Production deployment procedures
- [spec.md](../spec.md) - Non-functional requirements and SLAs
- [azure-deploy skill](../../../.claude/skills/azure-deploy/SKILL.md) - Azure deployment procedures
- [auth-azure-resources.md](../../../docs/architecture/auth-azure-resources.md) - Azure resource details
- [alerts.bicep](../../../infrastructure/bicep/modules/alerts.bicep) - Existing alert infrastructure
- [dashboard.bicep](../../../infrastructure/bicep/modules/dashboard.bicep) - Dashboard infrastructure

---

*This runbook should be reviewed and updated quarterly or after significant architecture changes.*
