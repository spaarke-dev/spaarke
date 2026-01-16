# Email-to-Document Automation - Administrator Guide

## Introduction

The Email-to-Document Automation feature automatically converts Dataverse email activities into searchable documents stored in SharePoint Embedded. This guide covers administrative tasks for managing the feature.

---

## 1. Accessing Admin Features

### 1.1 EmailProcessingMonitor PCF Control

The EmailProcessingMonitor PCF control provides a visual dashboard for monitoring email processing. To access:

1. Navigate to the Dataverse model-driven app
2. Open the Email Processing Monitor form/page
3. The dashboard displays:
   - Total conversions
   - Success/failure rates
   - Recent activity
   - DLQ status

### 1.2 API Endpoints

Admin endpoints are available at:
- **Base URL**: `https://spe-api-dev-67e2xz.azurewebsites.net`
- **Admin Prefix**: `/api/v1/emails/admin/`

All admin endpoints require authentication with appropriate permissions.

---

## 2. Manual Email Conversion

### When to Use
- User wants to archive a specific email immediately
- Testing conversion for a specific email
- Retry after fixing a conversion issue

### From Dataverse UI
1. Open the email activity in Dataverse
2. Click the "Save as Document" ribbon button
3. Confirm the action
4. Document will appear in the related documents grid

### From API
```bash
POST /api/v1/emails/{emailId}/save-as-document
Content-Type: application/json
{
  "includeAttachments": true,
  "createAttachmentDocuments": true
}
```

---

## 3. Batch Processing

### When to Use
- Initial migration of historical emails
- Recovering from outage/backlog
- Processing emails that missed automatic triggers

### Submitting a Batch Job

1. Determine the date range for emails to process
2. Submit the batch job:

```bash
POST /api/v1/emails/admin/batch-process
Content-Type: application/json
{
  "startDate": "2025-01-01T00:00:00Z",
  "endDate": "2025-01-31T23:59:59Z",
  "maxEmails": 1000,
  "includeAttachments": true,
  "createAttachmentDocuments": true,
  "skipAlreadyConverted": true,
  "statusFilter": "Completed",
  "directionFilter": "Incoming"
}
```

### Request Parameters

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `startDate` | Yes | - | Start of date range (UTC) |
| `endDate` | Yes | - | End of date range (UTC) |
| `maxEmails` | No | 1000 | Maximum emails to process |
| `includeAttachments` | No | true | Include attachments in .eml |
| `createAttachmentDocuments` | No | true | Create separate attachment documents |
| `skipAlreadyConverted` | No | true | Skip already-converted emails |
| `statusFilter` | No | Completed | Email status filter |
| `directionFilter` | No | null | Incoming, Outgoing, or null for all |
| `senderDomainFilter` | No | null | Filter by sender domain |
| `subjectContainsFilter` | No | null | Filter by subject content |

### Monitoring Progress

Poll the status endpoint to track progress:

```bash
GET /api/v1/emails/admin/batch-process/{jobId}/status
```

Response includes:
- `status`: Pending, InProgress, Completed, PartiallyCompleted, Failed
- `progressPercent`: 0-100
- `processedCount`: Successfully processed
- `errorCount`: Failed to process
- `skippedCount`: Skipped (already converted)
- `estimatedTimeRemainingSeconds`: Estimated time to completion

### Best Practices

1. **Start small**: Test with 100 emails first
2. **Use filters**: Narrow down to specific criteria
3. **Run during off-hours**: Large batches impact system resources
4. **Monitor progress**: Check status every few minutes
5. **Review errors**: Check DLQ after completion

---

## 4. Dead Letter Queue Management

The Dead Letter Queue (DLQ) contains messages that failed processing after maximum retry attempts.

### Viewing DLQ Contents

```bash
GET /api/v1/emails/admin/dlq?maxMessages=50
```

Response includes:
- `summary`: Queue statistics and breakdown by failure reason
- `messages`: List of dead-lettered messages
- `totalCount`: Total messages in DLQ
- `hasMore`: Whether more messages exist

### Understanding Failure Reasons

| Reason | Meaning | Action |
|--------|---------|--------|
| `InvalidFormat` | Job data corrupted | Do not re-drive; investigate source |
| `NoHandler` | Missing job handler | Check deployment; restart service |
| `MaxRetriesExceeded` | Transient failures | Fix root cause; then re-drive |
| `ProcessingError` | Exception occurred | Check logs; fix issue; then re-drive |
| `Poisoned` | Unrecoverable error | Investigate; may need manual fix |

### Re-driving Messages

After fixing the root cause, re-drive messages back to the main queue:

```bash
POST /api/v1/emails/admin/dlq/redrive
Content-Type: application/json
{
  "maxMessages": 100,
  "reasonFilter": "MaxRetriesExceeded"
}
```

Or re-drive specific messages:

```bash
POST /api/v1/emails/admin/dlq/redrive
Content-Type: application/json
{
  "sequenceNumbers": [123, 456, 789],
  "maxMessages": 100
}
```

**Warning**: Only re-drive after confirming the root cause is fixed.

---

## 5. Email Processing Rules

### Viewing Active Rules

```bash
GET /api/v1/emails/admin/rules
```

Rules determine which emails are automatically processed. Each rule has:
- **Priority**: Lower numbers evaluated first
- **Criteria**: Matching conditions (sender, subject, etc.)
- **Action**: AutoSave, Ignore, or ReviewRequired

### Refreshing Rules Cache

After updating rules in Dataverse:

```bash
POST /api/v1/emails/admin/refresh-rules-cache
```

### Seeding Default Rules

To initialize default rules (idempotent):

```bash
POST /api/v1/emails/admin/seed-rules
```

---

## 6. Monitoring & Alerts

### Key Metrics to Watch

1. **Email Processing Statistics**
   ```bash
   GET /api/admin/email-processing/stats
   ```

2. **DLQ Depth**: Should be 0 under normal operations

3. **Processing Latency**: P95 should be under 2 minutes

### Recommended Alerts

Configure these in Azure Monitor:

| Alert | Threshold | Action |
|-------|-----------|--------|
| DLQ Depth > 10 | 5 min sustained | Investigate failures |
| Error Rate > 5% | 15 min sustained | Check Application Insights |
| Processing Time P95 > 120s | 15 min sustained | Check resources |
| Webhook Rejections > 10/hour | 1 hour | Check webhook config |

---

## 7. Troubleshooting Quick Reference

| Issue | Check | Resolution |
|-------|-------|------------|
| Emails not converting | Webhook enabled? | Set `Email:EnableWebhook=true` |
| High failure rate | DLQ messages | Analyze and fix root cause |
| Slow processing | Concurrency settings | Adjust `BatchMaxConcurrency` |
| Duplicates | Skip already converted | Set `skipAlreadyConverted=true` |
| Missing attachments | Attachment settings | Verify `includeAttachments=true` |

---

## 8. Configuration Quick Reference

### App Service Settings

| Setting | Purpose | Default |
|---------|---------|---------|
| `Email:Enabled` | Master enable/disable | true |
| `Email:EnableWebhook` | Webhook processing | true |
| `Email:EnablePolling` | Backup polling | true |
| `Email:BatchMaxConcurrency` | Parallel processing | 5 |
| `Email:DefaultAction` | No-rule-match action | Ignore |

---

## Support

For issues not covered in this guide:
1. Check the [Production Runbook](RUNBOOK.md)
2. Review Application Insights logs
3. Contact SPAARKE DevOps team

---

*Last Updated: January 2026*
