# Email-to-Document Automation Architecture

> **Version**: 1.0
> **Date**: December 30, 2025
> **Project**: Email-to-Document Automation (Phase 2)
> **Status**: Implementation Complete

---

## Overview

The Email-to-Document Automation feature automatically converts Dataverse email activities (synced via Server-Side Sync) into archived .eml documents stored in SharePoint Embedded (SPE). This enables legal teams to preserve email correspondence as searchable, AI-analyzable documents linked to Matters and Projects.

### Key Capabilities

| Feature | Description |
|---------|-------------|
| **Automatic Archival** | Emails trigger conversion via Dataverse webhooks |
| **Hybrid Triggers** | Webhook (primary) + polling backup (5-minute intervals) |
| **Filter Rules** | Configurable rules to include/exclude emails by pattern |
| **Idempotency** | Guaranteed exactly-once processing via Redis locks |
| **AI Integration** | Optional auto-enqueue for AI summarization |
| **Telemetry** | OpenTelemetry metrics for monitoring and alerting |

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│  DATAVERSE                                                                       │
│  ┌─────────────┐    ┌──────────────────┐    ┌─────────────────────────────────┐ │
│  │  Server-    │    │  email entity    │    │  sprk_emailprocessingrule       │ │
│  │  Side Sync  │───→│  (activities)    │    │  (filter rules)                 │ │
│  └─────────────┘    └──────────────────┘    └─────────────────────────────────┘ │
│                              │                                                   │
│                              │ Webhook (on Create)                              │
│                              ▼                                                   │
└──────────────────────────────┼───────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│  BFF API (Sprk.Bff.Api)                                                         │
│  ┌──────────────────────────────────────────────────────────────────────────┐   │
│  │  EmailEndpoints.cs                                                        │   │
│  │  ├─ POST /api/v1/emails/webhook-trigger     ← Webhook receiver            │   │
│  │  ├─ POST /api/v1/emails/{id}/save-as-document  ← Manual trigger           │   │
│  │  └─ Admin endpoints (seed rules, list rules, refresh cache)              │   │
│  └──────────────────────────────────────────────────────────────────────────┘   │
│                     │                                                            │
│                     ▼                                                            │
│  ┌──────────────────────────────────────────────────────────────────────────┐   │
│  │  Services/Email/                                                          │   │
│  │  ├─ IEmailToEmlConverter   → Convert email to RFC 2822 .eml format       │   │
│  │  ├─ IEmailFilterService    → Evaluate filter rules (Redis cached)         │   │
│  │  └─ EmailRuleSeedService   → Seed default exclusion rules                 │   │
│  └──────────────────────────────────────────────────────────────────────────┘   │
│                     │                                                            │
│                     ▼                                                            │
│  ┌─────────────────────────────┐    ┌────────────────────────────────────────┐  │
│  │  JobSubmissionService       │    │  EmailPollingBackupService             │  │
│  │  → Enqueue to Service Bus   │    │  → BackgroundService (5-min interval)  │  │
│  │  → Idempotency key check    │    │  → Catches missed webhooks             │  │
│  └─────────────────────────────┘    └────────────────────────────────────────┘  │
│                     │                              │                             │
│                     └──────────────┬───────────────┘                             │
│                                    ▼                                             │
│  ┌──────────────────────────────────────────────────────────────────────────┐   │
│  │  ServiceBusJobProcessor                                                   │   │
│  │  └─ EmailToDocumentJobHandler.cs                                          │   │
│  │     ├─ Idempotency check (Redis)                                          │   │
│  │     ├─ Convert to .eml via IEmailToEmlConverter                           │   │
│  │     ├─ Upload to SPE via SpeFileStore                                     │   │
│  │     ├─ Create/update sprk_document in Dataverse                           │   │
│  │     └─ Optionally enqueue AI analysis job                                 │   │
│  └──────────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────────┘
                               │
              ┌────────────────┼────────────────┐
              ▼                ▼                ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────────────────────────┐
│  Azure Service   │ │  SharePoint      │ │  Dataverse                           │
│  Bus             │ │  Embedded (SPE)  │ │  ┌─────────────┐  ┌───────────────┐  │
│  ─────────────── │ │  ──────────────  │ │  │sprk_document│  │email activity │  │
│  sdap-jobs queue │ │  .eml files      │ │  │(metadata)   │──│(source)       │  │
└──────────────────┘ └──────────────────┘ │  └─────────────┘  └───────────────┘  │
                                          └──────────────────────────────────────┘
```

---

## Component Details

### 1. Trigger Layer

#### Webhook Trigger (Primary)
- **Dataverse Service Endpoint**: WebHook type with HMAC-SHA256 authentication
- **SDK Message Step**: Async post-operation on `email.Create`
- **Endpoint**: `POST /api/v1/emails/webhook-trigger`
- **Registration Script**: `scripts/Register-EmailWebhook.ps1`

#### Polling Backup Service
- **Type**: `BackgroundService` with `PeriodicTimer`
- **Interval**: Configurable (default 5 minutes)
- **Purpose**: Catches emails missed by webhook (e.g., during deployments)
- **Query**: Emails created in last N hours without corresponding document

### 2. Filter Layer

#### EmailFilterService
- **Purpose**: Evaluate emails against configurable rules
- **Caching**: Redis with 5-minute TTL (NFR-06)
- **Default Action**: Configurable (Process, Ignore, Review)

#### Filter Rule Types

| Rule Type | Value | Action |
|-----------|-------|--------|
| Exclude | 0 | Skip processing (ignore email) |
| Include | 1 | Force processing (auto-save) |
| Route | 2 | Flag for manual review |

#### Target Fields
- `subject` - Email subject line
- `from` - Sender address
- `to` - Recipients
- `cc` - CC recipients
- `body` - Email body text
- `attachmentname` - Attachment file names
- `regardingtype` - Regarding entity type

### 3. Processing Layer

#### EmailToDocumentJobHandler
- **Job Type**: `ProcessEmailToDocument`
- **Idempotency Key Format**: `Email:{emailId}:Archive`
- **Lock TTL**: 5 minutes (processing lock)
- **Retention**: 7 days (processed event marker)

#### Processing Steps
1. Parse job payload (emailId, triggerSource)
2. Check idempotency (already processed?)
3. Acquire processing lock (prevent duplicate processing)
4. Convert email to .eml via `IEmailToEmlConverter`
5. Upload .eml to SPE via `SpeFileStore`
6. Create `sprk_document` record in Dataverse
7. Update document with email metadata
8. Optionally enqueue AI analysis job
9. Mark as processed, release lock

### 4. Storage Layer

#### SharePoint Embedded (SPE)
- **File Format**: RFC 2822 .eml
- **Path Pattern**: `/emails/{filename}.eml`
- **Container**: Configured via `EmailProcessing:DefaultContainerId`

#### Dataverse Document Record
Extended `sprk_document` with email-specific fields:

| Field | Type | Description |
|-------|------|-------------|
| `sprk_isemailarchive` | Boolean | True for email documents |
| `sprk_documenttype` | Choice | 100000006 = Email |
| `sprk_email` | Lookup | Link to source email activity |
| `sprk_emailsubject` | Text | Subject line |
| `sprk_emailfrom` | Text | Sender |
| `sprk_emailto` | Text | Recipients |
| `sprk_emailcc` | Text | CC recipients |
| `sprk_emaildate` | DateTime | Sent/received date |
| `sprk_emailmessageid` | Text | RFC 2822 Message-ID |
| `sprk_emaildirection` | Choice | Received/Sent |

---

## Configuration

### EmailProcessingOptions

```json
{
  "EmailProcessing": {
    "Enabled": true,
    "EnableWebhook": true,
    "WebhookSecret": "your-secure-secret",
    "DefaultContainerId": "container-guid-here",
    "DefaultAction": "Process",
    "AutoEnqueueAi": true,
    "PollingIntervalMinutes": 5,
    "PollingLookbackHours": 24,
    "PollingBatchSize": 100,
    "FilterRulesCacheTtlMinutes": 5
  }
}
```

### Environment Variables

| Variable | Purpose |
|----------|---------|
| `EmailProcessing__Enabled` | Feature toggle |
| `EmailProcessing__WebhookSecret` | HMAC signature secret |
| `EmailProcessing__DefaultContainerId` | SPE container for emails |

---

## Telemetry

### OpenTelemetry Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `email.conversion.requests` | Counter | Total conversion requests |
| `email.conversion.duration` | Histogram | Processing time (ms) |
| `email.webhook.received` | Counter | Webhook requests received |
| `email.webhook.enqueued` | Counter | Jobs successfully enqueued |
| `email.webhook.rejected` | Counter | Rejected webhooks (with reason) |
| `email.polling.runs` | Counter | Polling service executions |
| `email.filter.evaluations` | Counter | Filter rule evaluations |
| `email.filter.matched` | Counter | Emails matching rules |
| `email.job.processed` | Counter | Jobs processed |
| `email.job.succeeded` | Counter | Successful jobs |
| `email.job.failed` | Counter | Failed jobs |
| `email.job.skipped_duplicate` | Counter | Idempotency skips |
| `email.eml.file_size` | Histogram | Generated .eml sizes |

### Application Insights Queries

```kusto
// Success rate by trigger type
customMetrics
| where name == "email.job.succeeded" or name == "email.job.failed"
| summarize count() by name, bin(timestamp, 1h)
| render timechart

// Webhook latency p95
customMetrics
| where name == "email.webhook.duration"
| summarize percentile(value, 95) by bin(timestamp, 1h)
| render timechart
```

---

## Security

### Webhook Authentication
- **Method**: HMAC-SHA256 signature validation
- **Header**: `X-Dataverse-Signature` or `Authorization: WebKey`
- **Secret Storage**: Azure Key Vault (production)

### Blocked Attachment Types (NFR-05)
The following extensions are blocked by default seeded rules:
- Executables: `.exe`, `.dll`
- Scripts: `.bat`, `.cmd`, `.ps1`, `.vbs`, `.js`
- Installers: `.msi`, `.msp`
- Shortcuts: `.lnk`, `.url`

### Idempotency Protection
- Redis-based processing locks prevent duplicate processing
- 5-minute lock TTL with automatic release
- 7-day retention for processed event markers

---

## Error Handling

### Retryable Errors
- HTTP 429 (rate limiting)
- HTTP 503 (service unavailable)
- Timeout exceptions

### Permanent Failures
- Invalid email ID (not found)
- Email conversion failure
- Configuration errors

### Poison Messages
After max retry attempts (default: 3), jobs are moved to dead-letter queue with full error context.

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| `projects/email-to-document-automation/spec.md` | Original specification |
| `projects/email-to-document-automation/notes/WEBHOOK-REGISTRATION.md` | Webhook setup guide |
| `docs/architecture/sdap-overview.md` | SDAP system overview |
| `docs/architecture/sdap-component-interactions.md` | Component interaction patterns |

---

## Key Files

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs` | Endpoint definitions |
| `src/server/api/Sprk.Bff.Api/Services/Email/IEmailToEmlConverter.cs` | Email conversion interface |
| `src/server/api/Sprk.Bff.Api/Services/Email/EmailToEmlConverter.cs` | RFC 2822 conversion |
| `src/server/api/Sprk.Bff.Api/Services/Email/IEmailFilterService.cs` | Filter service interface |
| `src/server/api/Sprk.Bff.Api/Services/Email/EmailFilterService.cs` | Rule evaluation |
| `src/server/api/Sprk.Bff.Api/Services/Email/EmailRuleSeedService.cs` | Default rules seeding |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` | Job processing |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/EmailPollingBackupService.cs` | Backup polling |
| `src/server/api/Sprk.Bff.Api/Telemetry/EmailTelemetry.cs` | OpenTelemetry metrics |
| `src/server/api/Sprk.Bff.Api/Configuration/EmailProcessingOptions.cs` | Configuration |
| `scripts/Register-EmailWebhook.ps1` | Webhook registration |

---

*Last Updated: December 30, 2025*
