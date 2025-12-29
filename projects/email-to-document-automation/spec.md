# Email-to-Document Automation - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2025-12-29
> **Source**: DESIGN-SPEC-Email-to-Document.md
> **Feature Code**: SDAP-EMAIL-DOC

---

## Executive Summary

This project implements automatic and manual conversion of Power Platform email activities into SDAP Document records with RFC 5322 compliant `.eml` files stored in SharePoint Embedded (SPE). The feature bridges Server-Side Sync email activities (which lack physical file representation) with the SDAP document management and AI processing pipeline.

**Key Capabilities:**
- Manual "Save to Document" ribbon button for user-initiated conversion
- Automatic rule-based processing via hybrid webhook + polling architecture
- RFC 5322 compliant .eml generation with embedded attachments
- Separate document creation for email attachments (parent-child relationship)
- Integration with existing AI Document Intelligence pipeline

---

## Scope

### In Scope

1. **Inbound email processing** - Emails received via Server-Side Sync
2. **Outbound email processing** - Sent emails
3. **Email attachments** - Both embedded in .eml and as separate searchable documents
4. **Manual "Save to Document" action** - Ribbon button on email form
5. **Automatic rule-based processing** - Filter engine with configurable rules
6. **AI Document Intelligence integration** - .eml files processed for summarization/entity extraction

### Out of Scope

1. Calendar items / meetings
2. Draft emails (only Sent/Received status)
3. Email threading / conversation grouping (future enhancement)
4. Bulk migration of historical emails (batch processing not in initial scope)
5. Exchange connector modifications
6. Changes to Server-Side Sync configuration

### Affected Areas

| Area | Path/Component | Changes |
|------|----------------|---------|
| BFF API | `src/server/api/Sprk.Bff.Api/` | New endpoints, services, job handler |
| Dataverse | `src/solutions/` | New entity (sprk_emailsaverule), ribbon customization |
| PCF/JS | `src/client/` | Ribbon button handler (minimal JS or PCF) |
| Existing Services | `SpeFileStore`, `IDataverseService`, `AIDocumentIntelligence` | Reused, no modifications |

---

## Requirements

### Functional Requirements

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| **FR-01** | Manual email-to-document conversion | User clicks "Save to Document" on email form, .eml document created synchronously with immediate feedback |
| **FR-02** | RFC 5322 compliant .eml generation | Generated .eml files pass RFC 5322 validation, openable in email clients (Outlook, Thunderbird) |
| **FR-03** | Attachment embedding in .eml | All email attachments embedded as MIME parts in the .eml file |
| **FR-04** | Separate attachment documents | Each attachment also created as separate sprk_document with parent-child relationship |
| **FR-05** | Automatic email processing | Emails matching filter rules automatically converted to documents |
| **FR-06** | Filter rule engine | Configurable rules based on sender, subject, direction, attachments, regarding object |
| **FR-07** | Hybrid trigger architecture | Webhook (Service Endpoint) for real-time + polling backup every 5 minutes |
| **FR-08** | Idempotency | Same email never creates duplicate documents; check before processing |
| **FR-09** | AI processing integration | Created documents queued for AI Document Intelligence (summary, entity extraction) |
| **FR-10** | Regarding object inheritance | Document inherits matter/account/contact linkage from email's regardingobjectid |
| **FR-11** | Manual save bypasses filters | User-initiated saves always succeed regardless of filter rules |

### Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| **NFR-01** | Manual save response time | < 10 seconds for typical email with 3 attachments |
| **NFR-02** | Automatic processing latency | < 30 seconds from email creation to document creation (webhook path) |
| **NFR-03** | Max attachment size | 25 MB per attachment (configurable) |
| **NFR-04** | Max total email size | 100 MB including all attachments (configurable) |
| **NFR-05** | Blocked attachment types | .exe, .dll, .bat, .ps1, .vbs, .js blocked by default |
| **NFR-06** | Filter rule cache | 5-minute TTL in Redis |
| **NFR-07** | Job retry policy | 3 attempts with exponential backoff, then poison queue |

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance | Key Constraint |
|-----|-----------|----------------|
| **ADR-001** | API + Background processing | Use Minimal API + BackgroundService; NO Azure Functions |
| **ADR-002** | No heavy plugins | Webhook uses Service Endpoint (not plugin); orchestration in BFF |
| **ADR-004** | Async job contract | Standard JobContract schema for email processing jobs |
| **ADR-005** | Flat SPE storage | Single container; metadata-based associations in Dataverse |
| **ADR-007** | SpeFileStore facade | All file uploads via existing SpeFileStore |
| **ADR-008** | Endpoint authorization | Use endpoint filters for API authorization |
| **ADR-009** | Redis caching | Filter rules cached in Redis |
| **ADR-010** | DI minimalism | Minimal new service registrations; reuse existing services |
| **ADR-013** | AI Architecture | Queue AI processing via existing pipeline |
| **ADR-017** | Job status persistence | Persist job outcomes for monitoring |
| **ADR-019** | ProblemDetails | API errors return RFC 7807 ProblemDetails |

### MUST Rules

- MUST use Minimal API pattern for new endpoints (ADR-001)
- MUST use BackgroundService + Service Bus for async processing (ADR-001)
- MUST NOT use Azure Functions or Durable Functions (ADR-001)
- MUST NOT add heavy logic in Dataverse plugins (ADR-002)
- MUST use standard JobContract schema for all background jobs (ADR-004)
- MUST use SpeFileStore for all SPE file operations (ADR-007)
- MUST implement idempotent job handlers (ADR-004)
- MUST cache filter rules in Redis (ADR-009)
- MUST return ProblemDetails for API errors (ADR-019)

### MUST NOT Rules

- MUST NOT create Azure Functions for email processing
- MUST NOT make HTTP/Graph calls from Dataverse plugins
- MUST NOT leak Graph SDK types above SpeFileStore facade
- MUST NOT process emails with blocked attachment extensions
- MUST NOT create duplicate documents for same email (idempotency)

### Existing Patterns to Follow

| Pattern | Reference |
|---------|-----------|
| Minimal API endpoints | `src/server/api/Sprk.Bff.Api/Endpoints/` |
| BackgroundService jobs | Existing job handlers in BFF API |
| SpeFileStore usage | Existing document upload patterns |
| Job contract | ADR-004 schema and existing implementations |
| Redis caching | Existing cache patterns in BFF API |

---

## Architecture Decisions (Clarified)

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Trigger mechanism** | Hybrid (Webhook + Polling) | Webhook for real-time; 5-min polling backup for missed events |
| **Manual save mode** | Synchronous | Consistent with existing document create; immediate user feedback |
| **Filter bypass** | Manual saves bypass filters | User explicitly wants to save; rules for automatic processing only |

---

## Data Model

### New Entity: sprk_emailsaverule

| Field | Type | Description |
|-------|------|-------------|
| sprk_emailsaveruleid | GUID | Primary key |
| sprk_name | Text(200) | Rule display name |
| sprk_isactive | Boolean | Enable/disable |
| sprk_priority | Int | Evaluation order (lower = first) |
| sprk_action | OptionSet | AutoSave, Ignore, ReviewRequired |
| sprk_direction | OptionSet | Inbound, Outbound, Both |
| sprk_senderdomain | Text | Match sender domain |
| sprk_subjectcontains | Text | Partial subject match |
| sprk_hasattachments | Boolean | Attachment filter |
| sprk_regardingentitytype | Text | e.g., "sprk_matter" |
| sprk_createattachmentdocuments | Boolean | Create separate docs |

### Extended: sprk_document (verify existing fields)

| Field | Purpose | Status |
|-------|---------|--------|
| sprk_email | Lookup to email activity | **Verify exists** |
| sprk_emailmessageid | RFC 5322 Message-ID | **Verify exists** |
| sprk_emailsubject | Email subject | **Verify exists** |
| sprk_emailsender | Sender address | **Verify exists** |
| sprk_emaildirection | Inbound/Outbound | **Verify exists** |
| sprk_parentdocument | Parent document lookup | **Verify exists** |
| sprk_relationshiptype | Relationship type (Email Attachment) | **Verify exists** |

### Document Relationship Pattern

```
Email Activity (activityid: AAA)
├── sprk_document (Parent - .eml)
│   ├── sprk_email → AAA
│   ├── sprk_documenttype = "Email"
│   ├── sprk_parentdocument = NULL
│   └── AI processing queued
│
├── sprk_document (Child - Attachment 1)
│   ├── sprk_email → AAA
│   ├── sprk_parentdocument → Parent doc
│   ├── sprk_relationshiptype = "Email Attachment"
│   └── AI processing queued
│
└── sprk_document (Child - Attachment 2)
    └── (same pattern)
```

---

## Component Inventory

### New Components

| Component | Type | Responsibility |
|-----------|------|----------------|
| EmlGenerationService | BFF Service | Convert email activity → RFC 5322 .eml stream |
| EmailFilterEngine | BFF Service | Evaluate rules, determine action |
| EmailDocumentOrchestrator | BFF Service | Coordinate EML gen → document pipeline |
| EmailEndpoints | Minimal API | Manual save, status queries |
| EmailWebhookEndpoint | Minimal API | Receive Dataverse Service Endpoint notifications |
| EmailProcessingJobHandler | BackgroundService | Handle ProcessEmailToDocument jobs |
| EmailPollingService | BackgroundService | 5-minute backup poll for missed emails |
| sprk_emailsaverule | Dataverse Entity | Filter rule configuration |
| Email Ribbon Command | JS/PCF | "Save to Document" button |

### Reused Components (No Modifications)

| Component | Usage |
|-----------|-------|
| SpeFileStore | Upload .eml and attachment files |
| IDataverseService | Query emails, create documents |
| AIDocumentIntelligence | Process .eml for summarization |
| Service Bus | Queue background jobs |
| Redis Cache | Cache filter rules |

---

## API Endpoints

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/emails/{emailId}/save-as-document` | POST | User JWT | Manual save (synchronous) |
| `/api/emails/{emailId}/document-status` | GET | User JWT | Check if email already saved |
| `/api/emails/{emailId}/can-save` | GET | User JWT | Check user permissions |
| `/api/webhooks/email` | POST | Webhook secret | Dataverse Service Endpoint receiver |

---

## Success Criteria

| # | Criterion | Verification Method |
|---|-----------|---------------------|
| 1 | Manual save creates .eml document in < 10s | Integration test with timing |
| 2 | .eml files are RFC 5322 compliant | Validate with MimeKit; open in email clients |
| 3 | Attachments created as child documents | Query sprk_document relationships |
| 4 | Filter rules correctly route emails | Unit tests for filter engine |
| 5 | Webhook triggers job within 5 seconds | End-to-end test with Service Endpoint |
| 6 | Polling catches missed webhooks | Simulate webhook failure, verify poll recovery |
| 7 | Idempotency prevents duplicates | Trigger same email twice, verify single document |
| 8 | AI processing queued for all documents | Verify AI jobs created |
| 9 | 409 returned for already-saved emails | API integration test |
| 10 | Blocked extensions rejected | Test .exe attachment blocked |

---

## Dependencies

### Prerequisites

1. **sprk_document schema verification** - Confirm email-related fields exist
2. **SpeFileStore** - Existing and working
3. **AIDocumentIntelligence pipeline** - Existing and working
4. **Service Bus** - Existing queue infrastructure
5. **Redis** - Existing cache infrastructure

### External Dependencies

1. **MimeKit NuGet package** - RFC 5322 .eml generation
2. **Dataverse Service Endpoint** - Webhook registration (admin setup)

### Implementation Order

1. **Phase 1**: Core services (EmlGeneration, Orchestrator) + Manual save endpoint
2. **Phase 2**: Filter engine + sprk_emailsaverule entity
3. **Phase 3**: Webhook endpoint + polling backup service
4. **Phase 4**: Ribbon button + UI integration
5. **Phase 5**: Testing, monitoring, production deployment

---

## Questions/Clarifications Resolved

| Question | Resolution |
|----------|------------|
| Webhook vs Polling | Hybrid: Webhook primary + 5-min polling backup |
| Sync vs Async for manual | Synchronous (consistent with existing document create) |
| Filter bypass for manual | Manual saves bypass filters |

### Schema Verification Required (Implementation Phase)

- [ ] Q5: Confirm sprk_document.sprk_email lookup exists
- [ ] Q6: Confirm sprk_document.sprk_parentdocument lookup exists
- [ ] Q7: Confirm sprk_relationshiptype has "Email Attachment" value
- [ ] Q8: Confirm sprk_documenttype has "Email" value

---

## Configuration

```json
{
  "Email": {
    "DefaultContainerId": "{SPE container ID}",
    "ProcessInbound": true,
    "ProcessOutbound": true,
    "MaxAttachmentSizeMB": 25,
    "MaxTotalSizeMB": 100,
    "BlockedAttachmentExtensions": [".exe", ".dll", ".bat", ".ps1", ".vbs", ".js"],
    "FilterRuleCacheTtlMinutes": 5,
    "DefaultAction": "Ignore",
    "EnableWebhook": true,
    "EnablePolling": true,
    "PollingIntervalMinutes": 5
  },
  "Webhooks": {
    "EmailSecret": "{shared-secret}"
  }
}
```

---

## Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|------------|
| Large emails cause timeouts | Medium | Size limits, async fallback option |
| Webhook missed events | Medium | Polling backup every 5 minutes |
| Duplicate document creation | High | Idempotency check before processing |
| MimeKit RFC compliance issues | Low | Well-tested library, extensive testing |
| Schema fields missing | Medium | Verify in Phase 1, add if needed |

---

*AI-optimized specification. Original design: DESIGN-SPEC-Email-to-Document.md*
