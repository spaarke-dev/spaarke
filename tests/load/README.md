# Load Testing - Email-to-Document Automation

This directory contains load testing scripts for the Email-to-Document Automation feature using [k6](https://k6.io/).

## Prerequisites

1. **Install k6**:
   ```bash
   # Windows (with Chocolatey)
   choco install k6

   # macOS
   brew install k6

   # Or download from https://k6.io/docs/getting-started/installation/
   ```

2. **API deployed and accessible** - The BFF API must be running

3. **Authentication token** - For batch processing tests, you need a valid bearer token

4. **Environment data** - Dataverse environment with email activities to process

## Test Scripts

### 1. batch-processing.k6.js

Tests the batch email processing endpoint (`POST /api/v1/emails/admin/batch-process`).

**NFR Targets:**
- Batch processing: 100 emails/minute
- 10,000 email batch completes successfully

**Usage:**
```bash
# Small batch test (100 emails)
k6 run batch-processing.k6.js \
  --env BASE_URL=https://spe-api-dev-67e2xz.azurewebsites.net \
  --env TOKEN=your-bearer-token \
  --env CONTAINER_ID=your-container-id

# Customize batch size
k6 run batch-processing.k6.js \
  --env BASE_URL=https://... \
  --env TOKEN=... \
  --env BATCH_SIZE=1000
```

### 2. webhook-processing.k6.js

Tests the webhook endpoint (`POST /api/v1/emails/webhook-trigger`) under sustained and burst load.

**NFR Targets:**
- 95% of emails processed within 2 minutes
- API response times less than 2s (P95)
- Webhook acceptance response under 500ms (P95)

**Usage:**
```bash
k6 run webhook-processing.k6.js \
  --env BASE_URL=https://spe-api-dev-67e2xz.azurewebsites.net \
  --env WEBHOOK_SECRET=your-webhook-secret
```

## Recommended Configuration Settings

Based on load testing, these are the recommended settings for production:

### appsettings.json - Email Processing

```json
{
  "Email": {
    "BatchMaxConcurrency": 5,
    "BatchProcessingBatchSize": 50,
    "PollingBatchSize": 100,
    "PollingIntervalMinutes": 5
  }
}
```

| Setting | Default | Description | Tuning Notes |
|---------|---------|-------------|--------------|
| `BatchMaxConcurrency` | 5 | Max concurrent emails in batch job | Increase for faster processing, decrease if hitting throttling |
| `BatchProcessingBatchSize` | 50 | Emails fetched per query | Higher = fewer queries, but more memory |
| `PollingBatchSize` | 100 | Max emails per polling cycle | Balance between catching up and overwhelming the system |

### appsettings.json - Service Bus

```json
{
  "Jobs": {
    "ServiceBus": {
      "QueueName": "sdap-jobs",
      "MaxConcurrentCalls": 5,
      "PrefetchCount": 10
    }
  }
}
```

| Setting | Default | Description | Tuning Notes |
|---------|---------|-------------|--------------|
| `MaxConcurrentCalls` | 5 | Concurrent message processing | Match to `BatchMaxConcurrency` |
| `PrefetchCount` | 10 | Messages prefetched from queue | 2x MaxConcurrentCalls for efficiency |

### Azure Service Bus Queue Settings

Configure in Azure Portal:
- **Max Delivery Count**: 5 (messages DLQ after 5 failures)
- **Lock Duration**: 5 minutes (allow time for email processing)
- **Enable Dead Lettering on Message Expiration**: Yes

## Running Tests

### Quick Baseline Test

```bash
# Run webhook test for 1 minute at 10 req/sec
k6 run webhook-processing.k6.js \
  --env BASE_URL=https://your-api.azurewebsites.net

# Run small batch test (100 emails)
k6 run batch-processing.k6.js \
  --env BASE_URL=https://your-api.azurewebsites.net \
  --env TOKEN=your-token \
  --env CONTAINER_ID=your-container
```

### Full Load Test (Production Validation)

**Step 1: Webhook sustained load (1,000 emails)**
```bash
# Uncomment burst_load and ramping_load scenarios in webhook-processing.k6.js
k6 run webhook-processing.k6.js --env BASE_URL=... --env WEBHOOK_SECRET=...
```

**Step 2: Batch processing (10,000 emails)**
```bash
# Uncomment medium_batch and large_batch scenarios in batch-processing.k6.js
k6 run batch-processing.k6.js --env BASE_URL=... --env TOKEN=... --env BATCH_SIZE=10000
```

## Monitoring During Tests

### Application Insights Queries

**Email processing latency:**
```kusto
customMetrics
| where name startswith "email.job"
| where timestamp > ago(1h)
| summarize avg(value), percentile(value, 95) by name, bin(timestamp, 1m)
| render timechart
```

**Webhook acceptance rate:**
```kusto
customMetrics
| where name == "email.webhook.enqueued" or name == "email.webhook.rejected"
| where timestamp > ago(1h)
| summarize count() by name, bin(timestamp, 1m)
| render columnchart
```

**Service Bus queue depth:**
```kusto
AzureMetrics
| where ResourceProvider == "MICROSOFT.SERVICEBUS"
| where MetricName == "ActiveMessages"
| where timestamp > ago(1h)
| summarize avg(Average) by bin(timestamp, 1m)
| render timechart
```

### Key Metrics to Monitor

| Metric | Target | Action if Exceeded |
|--------|--------|-------------------|
| `email.job.duration` P95 | < 120,000ms | Reduce BatchMaxConcurrency |
| `email.webhook.duration` P95 | < 500ms | Scale out App Service |
| Service Bus Active Messages | < 1,000 | Increase MaxConcurrentCalls |
| DLQ Depth | < 100 | Investigate failures |

## Troubleshooting

### High DLQ Depth
1. Check DLQ messages: `GET /api/v1/emails/admin/dlq`
2. Analyze dead-letter reasons
3. Fix root cause
4. Redrive: `POST /api/v1/emails/admin/dlq/redrive`

### Slow Batch Processing
1. Check Application Insights for bottlenecks
2. Reduce `BatchMaxConcurrency` if seeing throttling
3. Increase App Service plan if CPU > 80%
4. Check Dataverse API response times

### Webhook Timeouts
1. Increase Service Bus `Lock Duration`
2. Check for long-running email conversions
3. Consider async processing for large attachments

## Test Results

Test results are saved to JSON files:
- `batch-processing-results.json`
- `webhook-processing-results.json`

Example output:
```json
{
  "testRun": "2026-01-09T12:00:00.000Z",
  "metrics": {
    "webhooksSubmitted": 600,
    "webhooksAccepted": 595,
    "acceptRate": 0.9917,
    "avgResponseTimeMs": 45.2,
    "p95ResponseTimeMs": 120.5
  },
  "thresholds": {
    "webhook_accept_rate": true,
    "webhook_response_time_ms": true
  }
}
```

## Performance Baseline (December 2025)

| Metric | Target | Measured |
|--------|--------|----------|
| Webhook response time (P95) | < 500ms | TBD |
| Batch emails/minute | 100 | TBD |
| Single email processing (P95) | < 2 min | TBD |
| 1,000 email background test | Pass | TBD |
| 10,000 email batch test | Pass | TBD |

*Update this table after running load tests against the production environment.*
