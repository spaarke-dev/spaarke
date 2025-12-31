# Project Plan: Email-to-Document Automation

> **Last Updated**: 2025-12-14
> **Status**: Ready for Tasks
> **Spec**: [SPEC.md](SPEC.md)

---

## 1. Executive Summary

**Purpose**: Convert Power Platform Email activities (received via Server-Side Sync) into SDAP Document records with RFC 5322 compliant `.eml` files stored in SharePoint Embedded (SPE), enabling full document management and AI processing for email content.

**Scope**: Key deliverables
- Background service for automatic email-to-document conversion
- RFC 5322 compliant .eml file generation using MimeKit
- Smart email-to-Matter association (tracking tokens, threading, sender matching)
- Attachment extraction and separate document creation
- Filter/exclusion rules engine for spam, signatures, logos
- Manual "Save to Document" ribbon button on Email form
- Admin batch processing and monitoring dashboard
- AI Document Intelligence integration for email summarization

**Timeline**: 10 weeks | **Estimated Effort**: ~400 hours

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: Use BackgroundService + Service Bus; **no Azure Functions/Durable Functions**
- **ADR-002**: No HTTP/Graph calls from Dataverse plugins; keep plugins thin (<50ms)
- **ADR-004**: Use standard Job Contract schema for all async work; idempotent handlers required
- **ADR-007**: All SPE operations via `SpeFileStore` facade; no Graph SDK types above facade
- **ADR-008**: Resource authorization via endpoint filters, not global middleware
- **ADR-009**: Redis-first caching for rules and associations
- **ADR-013**: AI processing via existing Document Intelligence pipeline

**From Spec**:
- Idempotency by design: repeated triggers must produce at most one primary email Document
- Durable progress tracking: no reliance on "last poll window" timestamps alone
- Bounded concurrency + backpressure: respect SPE/Dataverse throttling
- Production observability: correlation IDs, metrics, no PII in logs

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Use MimeKit for .eml generation | Production-proven RFC 5322 library | Reliable MIME multipart handling |
| Polling + durable checkpoint | More reliable than pure event-driven for SSS emails | Persist high-watermark in Dataverse |
| Alternate keys for idempotency | Storage-level uniqueness prevents duplicates | Add unique constraint on `(sprk_emailactivityid, sprk_isemailarchive)` |
| Association confidence scoring | Transparent linking decisions | User can override low-confidence associations |

### Discovered Resources

**Applicable Skills** (auto-discovered):
- `.claude/skills/adr-aware/` - Proactive ADR loading when creating services
- `.claude/skills/dataverse-deploy/` - Deploy solution packages to Dataverse
- `.claude/skills/ribbon-edit/` - Email form ribbon button customization
- `.claude/skills/task-execute/` - Execute POML tasks with knowledge loading

**Knowledge Articles**:
- `docs/ai-knowledge/guides/RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md` - Ribbon XML patterns
- `docs/ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md` - AI processing integration
- `docs/reference/adr/ADR-001-minimal-api-and-workers.md` - BackgroundService patterns
- `docs/reference/adr/ADR-004-async-job-contract.md` - Job Contract schema

**Reusable Code**:
- `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` - Base job processor pattern
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/DocumentProcessingJobHandler.cs` - Document processing handler
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` - SPE file operations
- `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` - Dataverse operations interface

---

## CRITICAL: Reuse Existing Components

**DO NOT recreate functionality that already exists.** This project extends the SDAP platform using proven components.

### Components to REUSE (not recreate)

| Component | Location | Use For | DO NOT |
|-----------|----------|---------|--------|
| **SpeFileStore** | `Infrastructure/Graph/SpeFileStore.cs` | Upload .eml files to SPE | Create new file upload service |
| **IDocumentIntelligenceService** | `Services/Ai/DocumentIntelligenceService.cs` | AI summarization + metadata | Create new AI service |
| **TextExtractorService** | `Services/Ai/TextExtractorService.cs` | Extract text from .eml | Create new text extractor |
| **IDataverseService** | `Spaarke.Dataverse/` | Query emails, create documents | Create new Dataverse client |
| **IJobHandler pattern** | `Services/Jobs/Handlers/` | Job processing (follow pattern) | Create different job interface |
| **ServiceBusJobProcessor** | `Services/Jobs/` | Job dispatch | Create new message processor |
| **JobContract** | ADR-004 | Message schema | Create new message format |
| **Redis caching** | ADR-009 | Cache filter rules | Use IMemoryCache |

### Integration Pattern

```csharp
// ✅ CORRECT: Reuse existing services
public class EmailToDocumentJobHandler : IJobHandler
{
    private readonly SpeFileStore _speFileStore;           // REUSE
    private readonly IDocumentIntelligenceService _aiService; // REUSE
    private readonly IDataverseService _dataverse;         // REUSE
    private readonly IEmailToEmlConverter _emlConverter;   // NEW (this project)

    // After creating document, enqueue existing AI job type
    await _serviceBus.PublishAsync(new JobContract {
        JobType = "ai-document-processing",  // EXISTING job type
        SubjectId = documentId.ToString()
    });
}

// ❌ WRONG: Don't create parallel services
public class EmailDocumentIntelligenceService { } // NO - use existing
public class EmailFileStore { } // NO - use SpeFileStore
```

### What's NEW in This Project

Only these are new implementations:
- `IEmailToEmlConverter` - RFC 5322 .eml file generation (MimeKit)
- `IEmailAssociationService` - Smart Matter/Account/Contact linking
- `IEmailFilterService` - Exclusion rules engine
- `IEmailAttachmentProcessor` - Attachment handling
- `EmailToDocumentJobHandler` - Job handler (follows existing pattern)
- `EmailPollingBackupService` - Backup polling (follows BackgroundService pattern)
- Webhook endpoint - `/api/emails/webhook-trigger`
- Entity extensions - `sprk_document` email fields, `sprk_emailprocessingrule`

### What's EXTENDED (minor additions)

- `TextExtractorService` - Add `.eml` support method
- `SupportedFileTypes` config - Add `.eml` entry

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Core Conversion Infrastructure (Week 1-2)
├─ Data model extensions to sprk_document
├─ IEmailToEmlConverter service (MimeKit)
├─ POST /api/emails/convert-to-document endpoint
└─ Basic unit and integration tests

Phase 2: Hybrid Trigger & Filtering (Week 3-4)
├─ POST /api/emails/webhook-trigger (Dataverse webhook receiver)
├─ Dataverse Service Endpoint + Webhook Step registration
├─ EmailPollingBackupService (every 5 min backup)
├─ EmailToDocumentJobHandler (shared processing)
├─ IEmailFilterService + sprk_emailprocessingrule entity
└─ Monitoring and metrics

Phase 3: Association & Attachments (Week 5-6)
├─ IEmailAssociationService (tracking tokens, threading, sender matching)
├─ IEmailAttachmentProcessor (separate document creation)
├─ GET /api/emails/association-preview endpoint
└─ Parent-child document relationships

Phase 4: UI Integration & AI Processing (Week 7-8)
├─ Email form ribbon button ("Save to Document")
├─ TextExtractorService .eml support
├─ AI processing enqueue integration
└─ Admin monitoring custom page (PCF)

Phase 5: Batch Processing & Production (Week 9-10)
├─ POST /api/emails/batch-process endpoint
├─ Job status tracking and DLQ handling
├─ Performance optimization and load testing
└─ Production deployment and documentation
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 BLOCKED BY Phase 1 (needs .eml converter + document creation)
- Phase 3 BLOCKED BY Phase 2 (needs job processing infrastructure)
- Phase 4 BLOCKED BY Phase 3 (needs association service for ribbon button)
- Phase 5 BLOCKED BY Phase 4 (needs complete feature for batch processing)

**High-Risk Items:**
- RFC 5322 compliance - Mitigation: Use MimeKit, extensive test coverage
- Association accuracy - Mitigation: Multiple methods, confidence scoring, manual override
- Performance at scale - Mitigation: Bounded concurrency, Redis caching, load testing
- Email content variations - Mitigation: Test diverse email clients, robust error handling

---

## 4. Phase Breakdown

### Phase 1: Core Conversion Infrastructure (Week 1-2)

**Objectives:**
1. Extend sprk_document entity with email-specific fields
2. Create sprk_emailprocessingrule configuration entity
3. Implement RFC 5322 compliant .eml file generation
4. Create manual conversion API endpoint

**Deliverables:**
- [ ] sprk_document entity extended with email fields (sprk_emailcc, sprk_emaildirection, sprk_emailactivityid, sprk_emailtrackingtoken, sprk_emailconversationindex, sprk_isemailarchive, sprk_parentemaildocumentid)
- [ ] sprk_emailprocessingrule entity created
- [ ] Alternate key for idempotency: `(sprk_emailactivityid, sprk_isemailarchive)`
- [ ] IEmailToEmlConverter interface and implementation
- [ ] POST /api/emails/convert-to-document endpoint
- [ ] Unit tests for EML generation (80% coverage)
- [ ] Integration tests for API endpoint

**Critical Tasks:**
- Data model changes MUST BE FIRST - blocks all other work
- MimeKit integration - core dependency for all email processing

**Inputs**:
- SPEC.md sections 2 (Data Model), 3.1 (IEmailToEmlConverter), 4.1 (API)
- Existing SpeFileStore patterns
- ADR-001, ADR-004, ADR-007, ADR-008

**Outputs**:
- Dataverse solution with entity changes
- `src/server/api/Sprk.Bff.Api/Services/Email/EmailToEmlConverter.cs`
- `src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs`
- Unit and integration tests

### Phase 2: Hybrid Trigger & Filtering (Week 3-4)

**Objectives:**
1. Implement hybrid trigger model (webhook + polling backup)
2. Create ADR-004 compliant job processing
3. Build rules engine for email filtering
4. Add monitoring and observability

**Deliverables:**
- [ ] POST /api/emails/webhook-trigger endpoint (Dataverse webhook receiver)
- [ ] Dataverse Service Endpoint + Webhook Step registration on Email entity
- [ ] Webhook signature/token validation
- [ ] EmailPollingBackupService (BackgroundService, every 5 minutes)
- [ ] EmailToDocumentJobHandler (IJobHandler) with idempotency
- [ ] IEmailFilterService with rule evaluation + QuickFilterCheckAsync
- [ ] Default exclusion rules seeded in sprk_emailprocessingrule
- [ ] Redis caching for filter rules
- [ ] Application Insights custom events (JobStarted, JobSucceeded, JobSkipped, JobFailed)
- [ ] Unit tests for webhook, polling, filtering logic

**Critical Tasks:**
- Webhook endpoint MUST validate Dataverse signature before processing
- Job handler MUST implement idempotency via alternate key check
- Both webhook and polling MUST use same IdempotencyKey format: `Email:{emailId}:Archive`

**Inputs**:
- SPEC.md sections 3.2 (Hybrid Trigger Architecture), 3.3 (Job Contract)
- ServiceBusJobProcessor.cs patterns
- ADR-004 Job Contract schema

**Outputs**:
- `src/server/api/Sprk.Bff.Api/Api/EmailWebhookEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Services/Jobs/EmailPollingBackupService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Email/EmailFilterService.cs`
- Dataverse webhook registration script/instructions

### Phase 3: Association & Attachments (Week 5-6)

**Objectives:**
1. Implement smart Matter/Account/Contact association
2. Build attachment extraction and separate document creation
3. Create association preview API for UI

**Deliverables:**
- [ ] IEmailAssociationService with 6 association methods
- [ ] Association confidence scoring (0.0 - 1.0)
- [ ] IEmailAttachmentProcessor for separate documents
- [ ] Attachment filtering (exclude signatures, logos)
- [ ] GET /api/emails/association-preview endpoint
- [ ] Parent-child document relationships (sprk_parentemaildocumentid)
- [ ] ProcessEmailAttachmentsJob handler
- [ ] Unit tests for all association methods

**Critical Tasks:**
- Tracking token matching MUST be highest priority method
- Attachment filtering rules MUST exclude small images (<5KB)

**Inputs**:
- SPEC.md sections 3.1 (IEmailAssociationService, IEmailAttachmentProcessor), 4.3 (Preview API)
- Appendix B: Association Algorithm Pseudocode

**Outputs**:
- `src/server/api/Sprk.Bff.Api/Services/Email/EmailAssociationService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Email/EmailAttachmentProcessor.cs`
- API endpoint for association preview

### Phase 4: UI Integration & AI Processing (Week 7-8)

**Objectives:**
1. Add "Save to Document" ribbon button to Email form
2. Extend TextExtractorService for .eml files
3. Integrate with AI Document Intelligence pipeline
4. Create admin monitoring dashboard

**Deliverables:**
- [ ] Email form ribbon button with JavaScript handler
- [ ] RibbonDiffXml for sprk.Email.SaveToDocument
- [ ] sprk_emailactions.js web resource
- [ ] TextExtractorService .eml support (MimeKit text extraction)
- [ ] .eml added to SupportedFileTypes configuration
- [ ] AI processing enqueue on document creation
- [ ] Admin custom page (PCF) for monitoring
- [ ] User documentation

**Critical Tasks:**
- Ribbon button MUST validate user has read access to email
- AI processing enqueue MUST use existing Document Intelligence pipeline

**Inputs**:
- SPEC.md sections 5 (UI Components)
- docs/ai-knowledge/guides/RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md
- ribbon-edit skill
- ADR-006 (PCF over webresources - but webresource needed for ribbon handler)

**Outputs**:
- Ribbon customization in solution
- `src/client/webresources/sprk_emailactions.ts`
- Admin PCF control
- User guide documentation

### Phase 5: Batch Processing & Production (Week 9-10)

**Objectives:**
1. Implement admin batch processing endpoint
2. Add comprehensive job status tracking
3. Optimize performance and conduct load testing
4. Deploy to production with monitoring

**Deliverables:**
- [ ] POST /api/emails/batch-process endpoint (returns 202 Accepted)
- [ ] GET /api/emails/batch-process/{jobId}/status endpoint
- [ ] BatchProcessEmailsJob handler
- [ ] Job outcome persistence (JobOutcome store)
- [ ] DLQ handling and admin re-drive tooling
- [ ] Performance tuning (MaxConcurrentEmails, PrefetchCount)
- [ ] Load test results (1,000 emails, 10,000 batch)
- [ ] Production deployment runbook
- [ ] Training materials

**Critical Tasks:**
- Batch endpoint MUST NOT process synchronously
- Job status MUST be persisted, not derived from Dataverse queries

**Inputs**:
- SPEC.md sections 4.2 (Batch API), 7 (NFRs)
- ADR-017 (async job status persistence)

**Outputs**:
- Batch processing endpoints
- Job status tracking infrastructure
- Production deployment
- Documentation and training

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| MimeKit NuGet package | Stable (v4.x) | Low | Well-maintained, RFC compliant |
| Server-Side Sync | GA | Low | Standard Power Platform feature |
| Azure Service Bus | GA | Low | Existing infrastructure |
| SharePoint Embedded | GA | Low | Existing SpeFileStore |
| Application Insights | GA | Low | Existing integration |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| SpeFileStore | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/` | Production |
| IDataverseService | `src/server/shared/Spaarke.Dataverse/` | Production |
| ServiceBusJobProcessor | `src/server/api/Sprk.Bff.Api/Services/Jobs/` | Production |
| TextExtractorService | `src/server/api/Sprk.Bff.Api/Services/` | Production |
| AI Document Intelligence | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Production |
| Job Contract schema | ADR-004 | Current |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- EmailToEmlConverter - RFC 5322 compliance, attachment embedding, filename sanitization
- EmailAssociationService - All 6 association methods, confidence scoring
- EmailFilterService - Rule evaluation, priority ordering, regex patterns
- EmailAttachmentProcessor - Filtering logic, size checks, MIME type detection

**Integration Tests**:
- `/convert-to-document` - End-to-end with test email activity
- `/batch-process` - Job queueing and status tracking
- `/association-preview` - All association scenarios
- Background service poll and process workflow
- Idempotency verification (duplicate trigger handling)

**E2E Tests**:
- Receive email via Server-Side Sync → automatic Document creation
- Manual conversion via ribbon button
- Email with attachments → separate documents created
- Association preview → user confirms → document linked to Matter
- Batch processing of 100 historical emails

**Performance Tests**:
- 1,000 emails processed via background service (target: <30s each)
- 100 concurrent manual conversions
- Batch processing of 10,000 emails (target: 100 emails/minute)
- Large email handling (25MB with attachments)

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] Email activity can be converted to RFC 5322 .eml file
- [ ] .eml file stored in SPE successfully via SpeFileStore
- [ ] sprk_document created with all email metadata fields
- [ ] Embedded attachments preserved in .eml
- [ ] API returns proper error codes (400, 404, 429, 500)

**Phase 2:**
- [ ] Background service polls and processes new emails automatically
- [ ] Exclusion rules filter spam, signatures, logos
- [ ] Failed emails logged with correlation ID and retryable
- [ ] Application Insights events: JobStarted, JobSucceeded, JobSkipped, JobFailed
- [ ] Idempotency: re-processing same email creates no duplicate documents

**Phase 3:**
- [ ] Association accuracy > 80% against manual review sample
- [ ] Tracking token matching works with confidence 0.95
- [ ] Attachments stored as separate documents with parent link
- [ ] Small signature images (<5KB) excluded
- [ ] Preview API shows all signals and alternatives

**Phase 4:**
- [ ] Ribbon button visible on Email form (completed emails only)
- [ ] Manual conversion completes in <10 seconds
- [ ] AI summary generated for email documents
- [ ] Admin dashboard shows processing statistics

**Phase 5:**
- [ ] Batch processing handles 10,000 emails successfully
- [ ] Job status endpoint returns accurate progress
- [ ] DLQ depth alerts configured
- [ ] Production monitoring operational

### Business Acceptance

- [ ] 95% of incoming emails processed within 2 minutes
- [ ] 100% of legal emails archived as Documents
- [ ] User satisfaction score > 4/5 for manual conversion
- [ ] Storage costs within budget (<$100/month per 10,000 emails)
- [ ] Compliance audit passes for email retention

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|-------------|--------|------------|
| R1 | RFC 5322 compliance issues | Low | High | Use proven MimeKit library, extensive testing |
| R2 | Association accuracy below target | Medium | High | Multiple fallback methods, manual override, confidence scoring |
| R3 | Performance degradation at scale | Medium | Medium | Bounded concurrency, Redis caching, load testing |
| R4 | Email content variations | Medium | Medium | Test diverse email clients, robust error handling |
| R5 | False positive associations | Medium | High | Confidence thresholds, preview API, user confirmation |
| R6 | Server-Side Sync delays | Low | Medium | Durable checkpoint polling, no timestamp reliance |
| R7 | SPE throttling | Low | Medium | Respect Retry-After, adaptive backoff |
| R8 | Large email handling | Low | Low | Size limits (25MB), admin tools for exceptions |

---

## 9. Next Steps

1. **Review this PLAN.md** with team
2. **Run** `/task-create email-to-document-automation` to generate task files
3. **Begin** Phase 1 implementation with data model changes

---

**Status**: Ready for Tasks
**Next Action**: Generate POML task files

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
