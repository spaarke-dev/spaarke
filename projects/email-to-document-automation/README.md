# Email-to-Document Automation

> **Status**: Phase 2 Complete
> **Started**: 2025-12-14
> **Last Updated**: 2025-01-09
> **Target Completion**: Week 10

## Overview

Converts Power Platform Email activities (received via Server-Side Sync) into SDAP Document records with RFC 5322 compliant `.eml` files stored in SharePoint Embedded. Enables full document management, intelligent association, and AI processing for email content.

## Quick Start

```bash
# View project plan
cat projects/email-to-document-automation/PLAN.md

# Generate tasks
/task-create email-to-document-automation

# Execute first task
/task-execute projects/email-to-document-automation/tasks/001-*.poml
```

## Key Features

- **Automatic Email Ingestion**: Background service monitors incoming emails and creates Documents
- **RFC 5322 Compliance**: Emails stored as standards-compliant .eml files for legal discovery
- **Intelligent Association**: Automatic linking to Matters, Accounts, or Contacts via email metadata
- **Attachment Handling**: Both embedded (complete archive) and separated (searchable documents) storage
- **AI Processing**: Emails enter existing AI Document Intelligence pipeline
- **Manual Fallback**: "Save to Document" ribbon button for user-initiated conversion
- **Smart Filtering**: Rules engine prevents storage of unnecessary emails

## Architecture

**Hybrid Trigger Model**: Near real-time webhook + polling backup for reliability.

```
Email Activity (Dataverse)
    │
    ├──────────────────────┬──────────────────────┐
    ▼                      ▼                      │
Dataverse Webhook    Polling Backup         Manual Ribbon
(near real-time)     (every 5 min)          Button
    │                      │                      │
    └──────────┬───────────┴──────────────────────┘
               ▼
    ProcessEmailToDocumentJob (Service Bus)
               │
               ├─ Idempotency check
               ├─ IEmailFilterService (rules)
               ├─ IEmailToEmlConverter (RFC 5322)
               ├─ IEmailAssociationService (linking)
               └─ IEmailAttachmentProcessor
                              │
                              ▼
                   sprk_document + .eml in SPE
                              │
                              ▼
                   AI Document Intelligence
```

## Project Structure

```
projects/email-to-document-automation/
├── SPEC.md              # Detailed design specification
├── PLAN.md              # Implementation plan (this project)
├── README.md            # This file
├── CLAUDE.md            # AI context (if needed)
├── tasks/               # POML task files
│   ├── TASK-INDEX.md    # Task registry
│   ├── 001-*.poml       # Phase 1 tasks
│   ├── 010-*.poml       # Phase 2 tasks
│   └── ...
└── notes/               # Working notes (ephemeral)
```

## Implementation Phases

| Phase | Description | Status | Duration |
|-------|-------------|--------|----------|
| 1 | Core Conversion Infrastructure | ✅ Complete | Week 1-2 |
| 2 | Background Service & Filtering | ✅ Complete | Week 3-4 |
| 3 | Association & Attachments | ⬜ Not Started | Week 5-6 |
| 4 | UI Integration & AI Processing | ⬜ Not Started | Week 7-8 |
| 5 | Batch Processing & Production | ⬜ Not Started | Week 9-10 |

## New Services

| Service | Purpose |
|---------|---------|
| `IEmailToEmlConverter` | RFC 5322 .eml file generation |
| `IEmailAssociationService` | Smart Matter/Account/Contact linking |
| `IEmailFilterService` | Exclusion rules engine |
| `IEmailAttachmentProcessor` | Attachment extraction and storage |

## New API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/emails/webhook-trigger` | POST | Dataverse webhook receiver (near real-time) |
| `/api/emails/convert-to-document` | POST | Manual email conversion |
| `/api/emails/batch-process` | POST | Admin bulk processing |
| `/api/emails/batch-process/{jobId}/status` | GET | Batch job status |
| `/api/emails/association-preview` | GET | Preview automatic associations |

## Data Model Changes

**sprk_document extensions:**
- `sprk_emailcc` - CC recipients
- `sprk_emaildirection` - Incoming/Outgoing
- `sprk_emailactivityid` - Source email activity lookup
- `sprk_emailtrackingtoken` - Email tracking token
- `sprk_emailconversationindex` - Threading identifier
- `sprk_isemailarchive` - Indicates complete .eml archive
- `sprk_parentemaildocumentid` - Parent email document (for attachments)

**New entity:**
- `sprk_emailprocessingrule` - Filter and routing rules

## ADR Compliance

| ADR | Requirement | Implementation |
|-----|-------------|----------------|
| ADR-001 | BackgroundService, no Functions | EmailToDocumentBackgroundService |
| ADR-002 | No heavy plugins | All orchestration in BFF API |
| ADR-004 | Job Contract | ProcessEmailToDocumentJob |
| ADR-007 | SpeFileStore facade | All SPE operations via facade |
| ADR-008 | Endpoint authorization | EmailAuthorizationFilter |
| ADR-009 | Redis caching | Rules cached in Redis |

## Success Criteria

- [ ] 95% of emails processed within 2 minutes
- [ ] Association accuracy > 80%
- [ ] RFC 5322 validation passes for all .eml files
- [ ] Zero data loss or corruption
- [ ] API response times < 2s (P95)

## Deployment Notes

### Phase 1 & 2 Deployment Checklist

**Configuration Requirements:**

1. **appsettings.json** - Add `Email` section (see template):
   ```json
   "Email": {
     "Enabled": true,
     "DefaultContainerId": "...",
     "ProcessInbound": true,
     "ProcessOutbound": true,
     "MaxAttachmentSizeMB": 25,
     "MaxTotalSizeMB": 100,
     "FilterRuleCacheTtlMinutes": 5,
     "DefaultAction": "Ignore",
     "EnableWebhook": true,
     "EnablePolling": true,
     "PollingIntervalMinutes": 5,
     "WebhookSecret": "..."
   }
   ```

2. **Key Vault Secrets** (required):
   - `Email-WebhookSecret` - HMAC-SHA256 webhook signature validation

3. **Dataverse** (required):
   - Deploy `sprk_emailprocessingrule` entity (solution)
   - Deploy `sprk_document` extensions (email fields)
   - Configure Dataverse Service Endpoint webhook with same secret
   - Register webhook on `email` entity Create message

4. **Azure Service Bus**:
   - Queue `sdap-jobs` already exists (shared with document processing)

**Post-Deployment:**

1. Call `POST /api/v1/emails/admin/seed-rules` to create default filter rules
2. Verify polling service starts (check logs for "Email polling backup service configured")
3. Test webhook with curl/Postman (use valid signature)

### Service Dependencies

| Service | Required | Purpose |
|---------|----------|---------|
| Redis | Yes | Filter rules cache (5-min TTL) |
| Service Bus | Yes | Job queue for background processing |
| Dataverse | Yes | Email activities, filter rules |
| SPE Container | Yes | .eml file storage |

## Related Documents

- [SPEC.md](SPEC.md) - Full design specification
- [PLAN.md](PLAN.md) - Implementation plan
- [ADR-001](../../docs/reference/adr/ADR-001-minimal-api-and-workers.md) - BackgroundService pattern
- [ADR-004](../../docs/reference/adr/ADR-004-async-job-contract.md) - Job Contract

---

*Last updated: 2025-01-09*
