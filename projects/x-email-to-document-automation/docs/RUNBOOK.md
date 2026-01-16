# Email-to-Document Automation - Production Runbook

## Overview

This runbook documents operational procedures for the Email-to-Document Automation feature in the SPAARKE platform. It covers monitoring, troubleshooting, and administrative tasks.

## Quick Reference

| Endpoint | Purpose |
|----------|---------|
| `POST /api/v1/emails/{emailId}/save-as-document` | Manual email conversion |
| `POST /api/v1/emails/webhook-trigger` | Dataverse webhook receiver |
| `GET /api/v1/emails/{emailId}/document-status` | Check conversion status |
| `POST /api/v1/emails/admin/batch-process` | Batch process historical emails |
| `GET /api/v1/emails/admin/batch-process/{jobId}/status` | Batch job status |
| `GET /api/v1/emails/admin/dlq` | List dead-lettered messages |
| `POST /api/v1/emails/admin/dlq/redrive` | Re-drive failed messages |
| `GET /api/admin/email-processing/stats` | Processing statistics |

---

## 1. Health Monitoring

### 1.1 Key Health Indicators

| Metric | Healthy | Warning | Critical |
|--------|---------|---------|----------|
| API Response Time (P95) | < 1s | 1-2s | > 2s |
| Email Processing Time (P95) | < 60s | 60-120s | > 120s |
| Service Bus Queue Depth | < 100 | 100-500 | > 500 |
| DLQ Depth | 0 | 1-10 | > 10 |
| Error Rate | < 1% | 1-5% | > 5% |

### 1.2 Application Insights Queries

**Processing Success Rate:**
```kusto
customMetrics
| where name in ("email.job.succeeded", "email.job.failed")
| where timestamp > ago(1h)
| summarize
    succeeded = countif(name == "email.job.succeeded"),
    failed = countif(name == "email.job.failed")
| extend successRate = 100.0 * succeeded / (succeeded + failed)
```

**Webhook Latency:**
```kusto
customMetrics
| where name == "email.webhook.duration"
| where timestamp > ago(1h)
| summarize
    avg(value),
    percentile(value, 95),
    percentile(value, 99)
    by bin(timestamp, 5m)
| render timechart
```

**Job Processing Time:**
```kusto
customMetrics
| where name == "email.job.duration"
| where timestamp > ago(1h)
| summarize
    avg(value),
    percentile(value, 95)
    by bin(timestamp, 5m)
| render timechart
```

---

## 2. Common Operations

### 2.1 Manual Email Conversion

**When**: User wants to convert a single email manually.

**Steps**:
1. Get the email activity ID from Dataverse
2. Call the conversion endpoint:
```bash
curl -X POST "https://{api-url}/api/v1/emails/{emailId}/save-as-document" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"includeAttachments": true, "createAttachmentDocuments": true}'
```

**Expected Response**: 201 Created with document details.

---

### 2.2 Batch Processing Historical Emails

**When**: Need to process a backlog of historical emails.

**Steps**:
1. Submit batch job:
```bash
curl -X POST "https://{api-url}/api/v1/emails/admin/batch-process" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "startDate": "2025-01-01T00:00:00Z",
    "endDate": "2025-01-31T23:59:59Z",
    "maxEmails": 1000,
    "includeAttachments": true,
    "skipAlreadyConverted": true,
    "statusFilter": "Completed"
  }'
```

2. Monitor job status:
```bash
curl "https://{api-url}/api/v1/emails/admin/batch-process/{jobId}/status" \
  -H "Authorization: Bearer {token}"
```

**Expected Throughput**: ~100 emails/minute

**Notes**:
- Maximum date range: 365 days
- Maximum emails per batch: 10,000
- SkipAlreadyConverted prevents duplicates

---

### 2.3 Viewing Processing Statistics

**When**: Admin monitoring dashboard or troubleshooting.

```bash
curl "https://{api-url}/api/admin/email-processing/stats" \
  -H "Authorization: Bearer {token}"
```

**Response includes**:
- Total conversions (success/failure)
- Webhook received/enqueued/rejected
- Jobs processed/succeeded/failed/skipped
- Average conversion duration
- Last error information

---

## 3. Troubleshooting

### 3.1 High DLQ Depth

**Symptoms**: DLQ depth alert triggered, failed job count increasing.

**Investigation**:
1. List DLQ messages:
```bash
curl "https://{api-url}/api/v1/emails/admin/dlq?maxMessages=50" \
  -H "Authorization: Bearer {token}"
```

2. Analyze dead-letter reasons from response:
   - `InvalidFormat`: Job payload corruption
   - `NoHandler`: Missing job handler (deployment issue)
   - `MaxRetriesExceeded`: Transient failure exhausted retries
   - `ProcessingError`: Exception during processing

3. Check specific message details:
```bash
curl "https://{api-url}/api/v1/emails/admin/dlq/{sequenceNumber}" \
  -H "Authorization: Bearer {token}"
```

**Resolution by Reason**:

| Reason | Resolution |
|--------|------------|
| `InvalidFormat` | Check job submission logic; do not re-drive |
| `NoHandler` | Verify deployment; restart App Service; then re-drive |
| `MaxRetriesExceeded` | Check Dataverse/Graph availability; fix root cause; then re-drive |
| `ProcessingError` | Check Application Insights logs; fix code if needed; then re-drive |

**Re-drive Messages**:
```bash
# Re-drive specific messages
curl -X POST "https://{api-url}/api/v1/emails/admin/dlq/redrive" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"sequenceNumbers": [123, 456], "maxMessages": 100}'

# Re-drive by reason
curl -X POST "https://{api-url}/api/v1/emails/admin/dlq/redrive" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"maxMessages": 100, "reasonFilter": "MaxRetriesExceeded"}'
```

---

### 3.2 Slow Processing

**Symptoms**: Email processing time exceeding 2 minutes.

**Investigation**:
1. Check Application Insights for slow operations:
```kusto
requests
| where timestamp > ago(1h)
| where name contains "email"
| where duration > 60000
| project timestamp, name, duration, success, resultCode
| order by duration desc
```

2. Check for resource constraints:
   - App Service CPU/Memory utilization
   - Service Bus message backlog
   - Dataverse API throttling

**Resolution**:
- If CPU > 80%: Scale up App Service plan
- If message backlog high: Increase `BatchMaxConcurrency`
- If Dataverse throttling: Decrease `BatchMaxConcurrency`

---

### 3.3 Webhook Not Triggering

**Symptoms**: Emails created in Dataverse but not being processed.

**Investigation**:
1. Check webhook endpoint accessibility:
```bash
curl -X POST "https://{api-url}/api/v1/emails/webhook-trigger" \
  -H "Content-Type: application/json" \
  -d '{"PrimaryEntityId": "00000000-0000-0000-0000-000000000000", "PrimaryEntityName": "email"}'
```

2. Verify Dataverse Service Endpoint configuration
3. Check webhook secret matches between Dataverse and App Service

**Resolution**:
- Verify Service Endpoint is active in Dataverse
- Check plugin step is registered
- Verify firewall rules allow Dataverse to reach API
- Temporarily disable webhook signature validation for debugging

---

### 3.4 Duplicate Documents

**Symptoms**: Same email converted multiple times.

**Investigation**:
1. Check idempotency by querying documents with same email lookup
2. Review idempotency service logs

**Resolution**:
- Idempotency check uses Redis with email ID as key
- Check Redis connectivity
- Verify `SkipAlreadyConverted` is true for batch operations

---

## 4. Configuration Reference

### 4.1 App Service Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `Email:Enabled` | true | Enable/disable email processing |
| `Email:EnableWebhook` | true | Enable webhook processing |
| `Email:EnablePolling` | true | Enable backup polling |
| `Email:BatchMaxConcurrency` | 5 | Max concurrent emails in batch |
| `Email:BatchProcessingBatchSize` | 50 | Emails per batch query |
| `Email:PollingIntervalMinutes` | 5 | Polling service interval |
| `Email:PollingBatchSize` | 100 | Max emails per poll |
| `Email:DefaultAction` | Ignore | Default when no filter matches |
| `Email:WebhookSecret` | (secret) | Webhook validation secret |

### 4.2 Service Bus Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `Jobs:ServiceBus:QueueName` | sdap-jobs | Service Bus queue name |
| `Jobs:ServiceBus:MaxConcurrentCalls` | 5 | Concurrent message processing |
| `Jobs:ServiceBus:PrefetchCount` | 10 | Messages to prefetch |

---

## 5. Emergency Procedures

### 5.1 Stop All Processing

**When**: Critical bug discovered, need to halt processing.

**Steps**:
1. Disable webhook in App Service:
   - Set `Email:EnableWebhook` = false
   - Set `Email:EnablePolling` = false
2. Stop the App Service (if needed)
3. Messages will queue in Service Bus (retained for 14 days)

### 5.2 Purge Service Bus Queue

**When**: Corrupt messages or need to clear backlog.

**Steps**:
1. Stop App Service to prevent processing
2. Use Azure Portal or Service Bus Explorer to purge queue
3. Restart App Service

**Warning**: This will lose all queued messages permanently.

### 5.3 Database Recovery

**When**: Need to reprocess emails after data corruption.

**Steps**:
1. Identify affected email date range
2. Delete corrupt document records from Dataverse
3. Clear Redis idempotency keys for affected emails
4. Submit batch job with `skipAlreadyConverted: false`

---

## 6. Maintenance Windows

### 6.1 Planned Maintenance

**Before**:
1. Announce maintenance window
2. Complete or pause active batch jobs
3. Wait for queue to drain

**During**:
1. Deploy updates
2. Verify health endpoints
3. Check Application Insights for errors

**After**:
1. Monitor for 30 minutes
2. Resume batch jobs if needed
3. Announce completion

---

## 7. Contacts

| Role | Team |
|------|------|
| Application Support | SPAARKE DevOps |
| Azure Support | Microsoft Azure |
| Dataverse Support | Power Platform Admin |

---

*Last Updated: January 2026*
