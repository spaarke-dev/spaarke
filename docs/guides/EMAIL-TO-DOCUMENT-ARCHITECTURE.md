# Email-to-Document Automation Architecture

> **Version**: 1.1
> **Date**: January 13, 2026
> **Project**: Email-to-Document Automation (Phase 2)
> **Status**: Core Implementation Complete, AI Analysis Planned

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

### Additional SPE File Fields

| Field | Type | Description |
|-------|------|-------------|
| `sprk_graphitemid` | Text | Graph API item ID |
| `sprk_graphdriveid` | Text | Graph API drive ID |
| `sprk_filepath` | Text | SharePoint WebUrl for "Open in SharePoint" links |
| `sprk_filename` | Text | Original file name |
| `sprk_filesize` | Whole Number | File size in bytes (cast to int, NOT Int64) |
| `sprk_mimetype` | Text | MIME type (NOT `sprk_filetype`) |
| `sprk_hasfile` | Boolean | Whether file is uploaded |

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

| Variable | Purpose | Notes |
|----------|---------|-------|
| `EmailProcessing__Enabled` | Feature toggle | |
| `EmailProcessing__WebhookSecret` | HMAC signature secret | |
| `EmailProcessing__DefaultContainerId` | SPE container for emails | **Must be Drive ID format (`b!xxx`)** |

**Critical**: The `DefaultContainerId` must be in Drive ID format, not a raw GUID.

```
❌ WRONG: "58dd5db4-8043-4676-965e-c92e45f07221"
✅ CORRECT: "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
```

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

## Authentication Pattern

Email processing uses **app-only authentication** (not OBO) because webhooks and background jobs have no user context. See [sdap-auth-patterns.md](../architecture/sdap-auth-patterns.md) Pattern 6.

### OBO vs App-Only for Email

| Component | Auth Type | Why |
|-----------|-----------|-----|
| `EmailToDocumentJobHandler` | App-Only | No user context in job handlers |
| `SpeFileStore.UploadSmallAsync` | App-Only | No HttpContext available |
| `AnalysisOrchestrationService` | OBO | AI analysis needs user's SPE permissions |

**Important**: AI analysis of email documents requires a separate **AppOnlyAnalysisService** (planned) because `AnalysisOrchestrationService` requires OBO authentication via `HttpContext`.

---

## Bug Fixes and Lessons Learned (January 2026)

### Issue 1: DateTime Parse Error on OperationCreatedOn

**Problem**: Dataverse webhooks send dates in WCF format (`/Date(1234567890000)/`), not ISO 8601.

**Fix**: Added `NullableWcfDateTimeConverter` to `DataverseWebhookPayload.cs`:
```csharp
[JsonConverter(typeof(NullableWcfDateTimeConverter))]
public DateTime? OperationCreatedOn { get; set; }
```

### Issue 2: Field Name Mismatch (sprk_filetype)

**Problem**: Code used `sprk_filetype` but Dataverse field is `sprk_mimetype`.

**Fix**: Updated `DataverseServiceClientImpl.cs` and `DataverseWebApiService.cs` to use correct field name.

### Issue 3: FileSize Type Mismatch

**Problem**: `sprk_filesize` is Dataverse "Whole Number" (int32), but code passed long.

**Fix**: Cast to `(int)` when setting the field value.

### Issue 4: DefaultContainerId Format

**Problem**: Graph API requires Drive ID format (`b!xxx`), not raw GUID.

**Fix**: Updated Azure config to use Drive ID. Created `scripts/Set-ContainerId.ps1` to avoid bash `!` escaping issues.

### Issue 5: FilePath URL Not Saved

**Problem**: PCF sets `sprk_filepath: file.webUrl` but job handler wasn't setting this field.

**Fix**: Added `FilePath` property to `UpdateDocumentRequest` and set `FilePath = fileHandle.WebUrl` in `EmailToDocumentJobHandler.cs`.

---

## Future: AI Analysis Architecture

### Three Distinct Email Analysis Requirements

| # | Entity | What's Needed | Current State |
|---|--------|---------------|---------------|
| 1 | **Document** (.eml) | SPE file + AI Document Profile | ✅ File uploaded, ❌ AI analysis |
| 2 | **Document** (per attachment) | SPE file + AI Document Profile (child of .eml Document) | ❌ Not created |
| 3 | **Email** (activity record) | AI analysis combining email metadata + all attachment content | ❌ Not implemented |

### Why Three Separate Analyses?

**Azure Document Intelligence does NOT process .eml files natively.** It supports: PDF, JPEG, PNG, BMP, TIFF, HEIF, DOCX, XLSX, PPTX, HTML.

For .eml files, `TextExtractorService` falls back to raw text extraction which sees MIME structure but does NOT decode base64-encoded attachments meaningfully.

### Planned Architecture: AppOnlyAnalysisService

```
┌─────────────────────────────────────────────────────────────┐
│  User-Initiated (OBO)              App-Only (Background)    │
│  ─────────────────────             ────────────────────     │
│  AnalysisOrchestrationService      AppOnlyAnalysisService   │
│  - PCF triggers                    - Email processing       │
│  - User context (OBO)              - Bulk uploads           │
│  - HttpContext required            - No user context        │
│                                                             │
│              ↘                    ↙                        │
│                 Shared Components                           │
│                 - OpenAiClient                              │
│                 - TextExtractorService                      │
│                 - SpeFileStore (app-only mode)              │
│                 - IDataverseService                         │
└─────────────────────────────────────────────────────────────┘
```

### Planned Implementation Phases

**Phase A: Attachment Processing**
1. Enhance `EmailToEmlConverter` to expose attachment extraction
2. Modify `EmailToDocumentJobHandler` to process attachments as separate uploads
3. Create child Document records with `sprk_ParentDocumentLookup` relationship

**Phase B: App-Only Analysis Service**
1. Create `IAppOnlyAnalysisService` interface
2. Implement `AppOnlyAnalysisService` using shared components
3. Add endpoint or job handler for triggering app-only analysis

**Phase C: Email Analysis Playbook**
1. Create "Email Analysis" playbook in Dataverse
2. Design prompt that combines email metadata + attachment contents
3. Implement playbook execution in `AppOnlyAnalysisService`

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| `projects/email-to-document-automation/spec.md` | Original specification |
| `projects/email-to-document-automation/notes/WEBHOOK-REGISTRATION.md` | Webhook setup guide |
| `docs/architecture/sdap-overview.md` | SDAP system overview |
| `docs/architecture/sdap-component-interactions.md` | Component interaction patterns |
| `docs/architecture/sdap-auth-patterns.md` | Auth patterns including app-only (Pattern 6) |
| `docs/architecture/sdap-bff-api-patterns.md` | BFF API patterns including job handlers |
| `docs/architecture/auth-azure-resources.md` | Azure config including DefaultContainerId |

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

*Last Updated: January 13, 2026*
