# Component Interactions Guide

> **Purpose**: Help AI coding agents understand how Spaarke components interact, so changes to one component can be evaluated for impact on others.
> **Last Updated**: January 2026

---

## Quick Impact Reference

When modifying a component, check this table for potential downstream effects:

| If You Change... | Check Impact On... |
|------------------|-------------------|
| BFF API endpoints | PCF controls, Office add-ins, tests, API documentation |
| BFF authentication | PCF auth config, Office add-in auth, Dataverse plugin auth |
| PCF control API calls | BFF endpoint contracts |
| Dataverse entity schema | BFF Dataverse queries, PCF form bindings, Office workers |
| Bicep modules | Environment configs, deployment pipelines |
| Shared libraries | All consumers (search for ProjectReference) |
| Email processing options | Webhook handler, polling service, job handler |
| Email filter rules schema | EmailFilterService, EmailRuleSeedService |
| Webhook endpoint | Dataverse Service Endpoint registration |
| Office add-in entity models | UploadFinalizationWorker, IDataverseService, Dataverse schema |
| ProcessingJob schema | UploadFinalizationWorker, Office add-in tracking |
| EmailArtifact/AttachmentArtifact schema | UploadFinalizationWorker, Office add-in metadata |

---

## Component Interaction Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        USER INTERACTION LAYER                                │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │  Dataverse Model-Driven App (Browser)                                  │ │
│  │  └─ PCF Controls render in forms                                       │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                    ┌───────────────┼───────────────┐
                    │               │               │
                    ▼               ▼               ▼
┌──────────────────────┐ ┌──────────────────┐ ┌──────────────────────────────┐
│  UniversalQuickCreate│ │  SpeFileViewer   │ │  UniversalDatasetGrid       │
│  PCF Control         │ │  PCF Control     │ │  PCF Control                │
│  ──────────────────  │ │  ──────────────  │ │  ────────────────────────── │
│  • Upload documents  │ │  • Preview files │ │  • Display entity records   │
│  • Extract metadata  │ │  • Office Online │ │  • Custom grid rendering    │
│  • Create records    │ │  • Download      │ │                             │
└──────────────────────┘ └──────────────────┘ └──────────────────────────────┘
           │                      │                        │
           │   MSAL.js Token      │                        │
           │   (OBO flow)         │                        │
           ▼                      ▼                        ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          SPRK.BFF.API                                        │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Endpoints (Api/)                                                       ││
│  │  • POST /upload/file, /upload/session    ← Document uploads             ││
│  │  • GET  /api/containers/{id}/children    ← File listing                 ││
│  │  • GET  /api/documents/{id}/preview-url  ← Preview URLs                 ││
│  │  • GET  /api/navmap/{entity}/lookup      ← Metadata discovery           ││
│  │  • POST /api/v1/emails/{id}/save-as-document  ← Email-to-document       ││
│  │  • GET  /api/v1/emails/{id}/document-status   ← Email status check      ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Infrastructure Layer                                                   ││
│  │  • Auth/ — JWT validation, OBO token exchange                          ││
│  │  • Graph/ — ContainerOperations, DriveItemOperations, UploadManager    ││
│  │  • Dataverse/ — Web API client for metadata queries                    ││
│  │  • Resilience/ — Polly retry policies                                  ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Services Layer                                                         ││
│  │  • Email/EmailToEmlConverter — RFC 5322 .eml generation with MimeKit   ││
│  │  • Email/IEmailToEmlConverter — Email conversion interface             ││
│  │  • Email/IEmailFilterService — Rule-based email filtering              ││
│  │  • Jobs/EmailToDocumentJobHandler — Async email processing             ││
│  │  • Jobs/EmailPollingBackupService — Backup polling for missed webhooks ││
│  │  • Ai/AnalysisOrchestrationService — Document analysis orchestration   ││
│  │  • Ai/AnalysisContextBuilder — Prompt construction for AI analysis     ││
│  │  • Ai/RagService — Hybrid vector search for knowledge retrieval        ││
│  │  • Ai/VisualizationService — Document relationship visualization (NEW) ││
│  │  • Ai/Export/ — DOCX, PDF, Email, Teams export services (R3)           ││
│  │  • Ai/OpenAiClient — Azure OpenAI with Polly resilience (R3)           ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Infrastructure/Resilience (R3 Phase 4)                                 ││
│  │  • ResilientSearchClient — Polly circuit breaker for AI Search         ││
│  │  • CircuitBreakerRegistry — Named circuit breaker management           ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Telemetry (R3 Phase 4)                                                 ││
│  │  • AiTelemetry.cs — Application Insights custom metrics for AI ops     ││
│  └─────────────────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────────────┘
           │                                              │
           │  OBO Token                                   │  App Token
           │  (delegated)                                 │  (application)
           ▼                                              ▼
┌──────────────────────────────┐           ┌──────────────────────────────────┐
│  Microsoft Graph API         │           │  Dataverse Web API               │
│  ────────────────────────────│           │  ────────────────────────────────│
│  • FileStorageContainers     │           │  • sprk_document CRUD            │
│  • DriveItems (files)        │           │  • sprk_matter lookup            │
│  • Permissions               │           │  • Entity metadata               │
└──────────────────────────────┘           └──────────────────────────────────┘
           │                                              │
           ▼                                              ▼
┌──────────────────────────────┐           ┌──────────────────────────────────┐
│  SharePoint Embedded         │           │  Dataverse Tables                │
│  Container (SPE)             │           │  ──────────────────────────────  │
│  ────────────────────────────│           │  • sprk_document (metadata)      │
│  • File binary storage       │           │  • sprk_matter (parent record)   │
│  • Up to 250GB per container │           │  • sprk_documenttype (lookup)    │
│  • Versioning, permissions   │           │  • Custom entities               │
└──────────────────────────────┘           └──────────────────────────────────┘
```

---

## Interaction Patterns

### Pattern 1: Document Upload Flow

```
User → PCF (UniversalQuickCreate) → BFF API → Graph API → SPE Container
                                       │
                                       └──→ Dataverse API → sprk_document record
```

**Components involved:**
1. `src/client/pcf/UniversalQuickCreate/` — UI, file selection, metadata form
2. `src/server/api/Sprk.Bff.Api/Api/DocumentsEndpoints.cs` — Upload endpoints
3. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs` — Chunked uploads
4. `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/` — Metadata record creation

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify upload endpoint signature | Update PCF API client |
| Add upload validation | Add corresponding PCF-side validation |
| Change file size limits | Update both BFF config and PCF UI messaging |

### Pattern 2: Authentication Flow (OBO)

```
Dataverse Session → PCF (MSAL.js) → BFF API (validate) → OBO Exchange → Graph/Dataverse
```

**Components involved:**
1. `src/client/pcf/*/services/auth/msalConfig.ts` — MSAL configuration
2. `src/client/pcf/*/services/auth/MsalAuthProvider.ts` — Token acquisition
3. `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/` — JWT validation
4. `src/server/api/Sprk.Bff.Api/Program.cs` — Auth middleware registration

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify API scopes | Update PCF msalConfig AND Entra app registration |
| Change token validation | All PCF controls affected |
| Add new authorization policy | Update endpoint decorators |

### Pattern 3: File Preview Flow

```
User → PCF (SpeFileViewer) → BFF API → Graph API → Preview URL
                                │
                                └──→ Redis Cache (URL caching)
```

**Components involved:**
1. `src/client/pcf/SpeFileViewer/` — Preview UI component
2. `src/server/api/Sprk.Bff.Api/Api/DocumentsEndpoints.cs` — Preview endpoint
3. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs` — Graph calls

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify preview URL structure | Update SpeFileViewer iframe handling |
| Change caching TTL | Consider Graph API rate limits |
| Add new preview types | Update both BFF and PCF |

### Pattern 4: Analysis Flow (AI Document Intelligence)

```
User → PCF (AnalysisWorkspace v1.2.7) → BFF API (AnalysisEndpoints) → Azure OpenAI
                │                                │
                │  Resume Dialog                 ├──→ SpeFileStore → SPE Container (document text)
                │  (ADR-023)                     ├──→ ScopeResolver → Dataverse (playbooks, skills)
                │                                ├──→ AnalysisContextBuilder → Prompt construction
                ▼                                └──→ Dataverse → sprk_analysis (chat history)
        Chat History
        Persistence
```

**Components involved:**
1. `src/client/pcf/AnalysisWorkspace/` — Analysis UI (v1.2.7), SSE streaming, chat interface
   - `AnalysisWorkspaceApp.tsx` — Main app with resume dialog (ADR-023), chat history ref
   - `SourceDocumentViewer.tsx` — Document preview with URL normalization
   - `useSseStream.ts` — SSE streaming hook for chat
2. `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` — Execute, Continue, Export endpoints
3. `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` — Orchestration
4. `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs` — Prompt construction
5. `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` — Playbook/skill resolution

**R3 Additions (v1.2.7):**
- Resume/Fresh session choice dialog (ADR-023 pattern)
- Chat history persistence to `sprk_analysis.sprk_chathistory` (JSON)
- Working document auto-save to `sprk_analysis.sprk_workingdocument`
- `chatMessagesRef` pattern to avoid stale closures in auto-save

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify analysis endpoint signature | Update PCF AnalysisWorkspace API client |
| Change prompt structure | Update AnalysisContextBuilder methods |
| Add new export format | Update ExportServiceRegistry and PCF export UI |
| Modify playbook resolution | Update ScopeResolverService and Dataverse entities |
| Change chat history schema | Update PCF `IChatMessage` interface |

### Pattern 5: Email-to-Document Conversion Flow

**Full Documentation**: See [email-to-document-automation.md](email-to-document-automation.md) for complete architecture.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  Email Activity Created (Server-Side Sync / Outlook)                         │
└─────────────────────────────────────────────────────────────────────────────┘
          │                                              │
          │ Webhook (Real-time)                          │ Polling (Backup)
          ↓                                              ↓
┌─────────────────────────┐              ┌─────────────────────────────────────┐
│ POST /api/v1/emails/    │              │ EmailPollingBackupService           │
│   webhook-trigger       │              │ (BackgroundService, 5 min interval) │
└─────────────────────────┘              └─────────────────────────────────────┘
          │                                              │
          └──────────────────────┬───────────────────────┘
                                 ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│               EmailToDocumentJobHandler (Core Processing)                    │
│  1. Idempotency check → 2. EML conversion (MimeKit) → 3. SPE upload         │
│  4. Create sprk_document → 5. Update metadata → 6. Process attachments      │
│  7. Enqueue AI analysis (Document Profile playbook)                         │
└─────────────────────────────────────────────────────────────────────────────┘
          │                              │                              │
          ↓                              ↓                              ↓
┌─────────────────┐      ┌────────────────────────┐      ┌───────────────────┐
│ SPE Container   │      │ Dataverse              │      │ AI Job Queue      │
│ /emails/*.eml   │      │ sprk_document (parent) │      │ Document Profile  │
│ /emails/attach/ │      │ sprk_document (child)  │      │ playbook          │
└─────────────────┘      └────────────────────────┘      └───────────────────┘
```

**Components involved:**

| Component | File Location | Purpose |
|-----------|---------------|---------|
| Webhook Endpoint | `Api/EmailEndpoints.cs` | Receive Dataverse webhook, validate signature |
| Polling Backup | `Services/Jobs/EmailPollingBackupService.cs` | Query missed emails, create jobs |
| Job Handler | `Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` | Core email-to-document processing |
| EML Converter | `Services/Email/EmailToEmlConverter.cs` | RFC 5322 .eml generation with MimeKit |
| Attachment Filter | `Services/Email/AttachmentFilterService.cs` | Filter noise (signatures, tracking pixels) |
| Configuration | `Configuration/EmailProcessingOptions.cs` | All email processing settings |

**Document Entity Relationships:**
```
sprk_document (Parent - Email .eml)
├─ sprk_email → {email-activity-id}        ← Lookup to email entity
├─ sprk_documenttype = 100000006 (Email)
└─ [email metadata fields]

  └─ sprk_document (Child - Attachment)
     ├─ sprk_parentdocument → {parent-doc-id}  ← Lookup to parent
     ├─ sprk_documenttype = 100000007 (Email Attachment)
     └─ sprk_email = NULL                      ← NOT set (alternate key constraint)
```

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify webhook endpoint URL | Update Dataverse Service Endpoint registration |
| Change job payload schema | Update both webhook handler and job handler |
| Modify filter rule schema | Update EmailFilterService, seed service, Dataverse entity |
| Change default container | Update EmailProcessingOptions configuration |
| Modify EmailEndpoints signature | Update any UI callers (ribbon button, PCF) |
| Add email fields to sprk_document | Update DataverseWebApiService mappings |
| Change attachment filtering rules | Update EmailProcessingOptions config |
| Modify attachment parent-child link | Update EmailToDocumentJobHandler |
| Change AI playbook name | Update `EnqueueAiAnalysisJobAsync` constant |

**Key Files:**
- `EmailToEmlConverter.cs` — Uses MimeKit for RFC 5322 compliant .eml generation
- `EmailEndpoints.cs` — POST /api/v1/emails/{emailId}/save-as-document, webhook receiver
- `EmailProcessingOptions.cs` — Attachment size limits, blocked extensions, signature patterns
- `AttachmentFilterService.cs` — Regex-based filtering for signature images, tracking pixels

### Pattern 5B: Office Add-in Document Processing Flow

**Full Documentation**: See [office-outlook-teams-integration-architecture.md](office-outlook-teams-integration-architecture.md) for complete architecture.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  Office Add-in (Outlook/Word) - User uploads file or saves email            │
└─────────────────────────────────────────────────────────────────────────────┘
          │
          │ POST /api/office/upload (with metadata)
          ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│               OfficeEndpoints (Initial Upload)                               │
│  1. Validate user token (OBO) → 2. Create ProcessingJob (Pending)          │
│  3. Upload file to SPE → 4. Create sprk_document record                    │
│  5. Publish message to Service Bus (office-jobs queue)                      │
└─────────────────────────────────────────────────────────────────────────────┘
          │
          ↓ Service Bus Queue: office-jobs
┌─────────────────────────────────────────────────────────────────────────────┐
│            UploadFinalizationWorker (Background Processing)                  │
│  1. Dequeue message → 2. Load ProcessingJob (by IdempotencyKey)            │
│  3. Process stages (metadata, entities, AI analysis)                        │
│  4. Update ProcessingJob status and progress                                │
│  5. Create EmailArtifact (if email metadata present)                        │
│  6. Create AttachmentArtifact records (if attachments present)              │
│  7. Update sprk_document with final metadata                                │
└─────────────────────────────────────────────────────────────────────────────┘
          │                              │                              │
          ↓                              ↓                              ↓
┌─────────────────┐      ┌────────────────────────┐      ┌───────────────────┐
│ SPE Container   │      │ Dataverse              │      │ Dataverse Entities│
│ /office/*.docx  │      │ sprk_document          │      │ sprk_processingjob│
│ /office/*.xlsx  │      │                        │      │ sprk_emailartifact│
│ /office/*.msg   │      │                        │      │ sprk_attachment   │
│                 │      │                        │      │   artifact        │
└─────────────────┘      └────────────────────────┘      └───────────────────┘
```

**Components involved:**

| Component | File Location | Purpose |
|-----------|---------------|---------|
| Office Upload Endpoint | `Api/Office/OfficeEndpoints.cs` | Receive file upload from Office add-in |
| Upload Finalization Worker | `Workers/Office/UploadFinalizationWorker.cs` | Async processing, Dataverse integration |
| Processing Job Models | `Models/ProcessingJob.cs` | Track job status, stages, progress |
| Email Artifact Models | `Models/EmailArtifact.cs` | Email metadata entity |
| Attachment Artifact Models | `Models/AttachmentArtifact.cs` | Attachment metadata entity |
| Dataverse Service | `Spaarke.Dataverse/IDataverseService.cs` | CRUD operations for Office entities |
| Dataverse Service Impl | `Spaarke.Dataverse/DataverseServiceClientImpl.cs` | ServiceClient SDK implementation |

**Dataverse Entity Relationships:**
```
sprk_processingjob
├─ sprk_jobtype = 0 (DocumentSave)
├─ sprk_status = 0→1→2 (Pending→InProgress→Completed)
├─ sprk_progress = 0→100
├─ sprk_idempotencykey (SHA256 hash, indexed)
└─ sprk_document → {document-id} (Lookup)

sprk_emailartifact
├─ sprk_subject, sprk_sender, sprk_recipients (searchable)
├─ sprk_messageid (indexed for duplicate detection)
├─ sprk_internetheadershash (SHA256, indexed)
├─ sprk_bodypreview (first 2000 chars)
└─ sprk_document → {document-id} (Lookup)

sprk_attachmentartifact
├─ sprk_originalfilename (up to 260 chars)
├─ sprk_contenttype (MIME type)
├─ sprk_size (bytes)
├─ sprk_isinline (for embedded images)
├─ sprk_emailartifact → {email-artifact-id} (Lookup)
└─ sprk_document → {document-id} (Lookup)
```

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify OfficeEndpoints signature | Update Office add-in API client (TypeScript) |
| Change ProcessingJob schema | Update Models/ProcessingJob.cs, Dataverse table, worker logic |
| Modify EmailArtifact schema | Update Models/EmailArtifact.cs, Dataverse table, worker logic |
| Change IDataverseService interface | Update DataverseServiceClientImpl, DataverseWebApiService |
| Modify UploadFinalizationWorker stages | Update worker implementation, test scenarios |
| Add Office entity fields | Update entity models, Dataverse schema, worker mappings |
| Change Service Bus queue name | Update appsettings.json, UploadFinalizationWorker config |

**Key Files:**
- `OfficeEndpoints.cs` — POST /api/office/upload, GET /api/office/status/{jobId}
- `UploadFinalizationWorker.cs` — Background worker, Dataverse integration
- `ProcessingJob.cs` — Entity model with 19 fields, JobType/Status enums
- `EmailArtifact.cs` — Entity model with email metadata fields
- `AttachmentArtifact.cs` — Entity model with attachment metadata
- `IDataverseService.cs` — Extended with 8 Office-specific methods
- `DataverseServiceClientImpl.cs` — Reflection-based entity creation

**Security Model:**
- **M365 Add-in Deployment**: Centralized deployment via M365 Admin Center
- **BFF API Authorization**: OBO token exchange for user context
- **Dataverse Security**: Custom "Spaarke Office Add In User" role with table permissions
- **Three-Layer Defense**: Add-in registration, API auth, data-level access control

### Pattern 6: Analysis Export Flow (R3 Phase 3)

```
User → PCF (AnalysisWorkspace) → BFF API (POST /api/ai/analysis/{id}/export)
                                              │
                                              ├──→ ExportServiceRegistry → Select export service
                                              │
                                              ├──→ DocxExportService → Open XML SDK → .docx file
                                              ├──→ PdfExportService → QuestPDF → .pdf file
                                              ├──→ EmailExportService → Microsoft Graph → Email sent
                                              └──→ TeamsExportService → Graph API → Adaptive Card
```

**Components involved:**
1. `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` — Export endpoint
2. `src/server/api/Sprk.Bff.Api/Services/Ai/Export/IExportService.cs` — Export interface
3. `src/server/api/Sprk.Bff.Api/Services/Ai/Export/DocxExportService.cs` — Word export
4. `src/server/api/Sprk.Bff.Api/Services/Ai/Export/PdfExportService.cs` — PDF export
5. `src/server/api/Sprk.Bff.Api/Services/Ai/Export/EmailExportService.cs` — Email via Graph
6. `src/server/api/Sprk.Bff.Api/Services/Ai/Export/TeamsExportService.cs` — Teams adaptive cards

**Export Formats:**

| Format | Library | Output | Config Option |
|--------|---------|--------|---------------|
| DOCX | Open XML SDK | File download | `AnalysisOptions.EnableDocxExport` |
| PDF | QuestPDF | File download | `AnalysisOptions.EnablePdfExport` |
| Email | Microsoft Graph | Send from user mailbox | `AnalysisOptions.EnableEmailExport` |
| Teams | Microsoft Graph | Post adaptive card | `AnalysisOptions.EnableTeamsExport` |

**Change Impact:**
| Change | Impact |
|--------|--------|
| Add new export format | Create new `IExportService`, register in DI |
| Modify export branding | Update `AnalysisOptions.ExportBranding` |
| Change email template | Update `EmailExportService.BuildHtmlEmailBody()` |
| Change PDF layout | Update `PdfExportService` QuestPDF document definition |

### Pattern 7: AI Authorization Flow (OBO for Dataverse)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ 1. User clicks "AI Summary" in PCF AnalysisWorkspace                        │
│    PCF acquires user's bearer token via MSAL.js                             │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼ Authorization: Bearer {userToken}
┌─────────────────────────────────────────────────────────────────────────────┐
│ 2. BFF API Endpoint Filter (AnalysisAuthorizationFilter)                    │
│    src/server/api/Sprk.Bff.Api/Api/Filters/AnalysisAuthorizationFilter.cs  │
│    • Extracts documentIds from request body (AnalysisExecuteRequest)        │
│    • Extracts user claims (oid, objectidentifier)                           │
│    • Calls IAiAuthorizationService.AuthorizeAsync()                         │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ 3. AiAuthorizationService                                                   │
│    src/server/api/Sprk.Bff.Api/Services/Ai/AiAuthorizationService.cs       │
│    • Extracts bearer token from HttpContext via TokenHelper                 │
│    • Iterates through documentIds to authorize                              │
│    • Calls IAccessDataSource.GetUserAccessAsync(userAccessToken)            │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼ userAccessToken (from PCF)
┌─────────────────────────────────────────────────────────────────────────────┐
│ 4. DataverseAccessDataSource                                                │
│    src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs        │
│                                                                              │
│    STEP 4A: MSAL OBO Token Exchange                                         │
│    • ConfidentialClientApplicationBuilder (MSAL)                            │
│    • AcquireTokenOnBehalfOf(userToken)                                      │
│    • Scope: "https://org.crm.dynamics.com/.default"                         │
│    • Result: Dataverse-scoped token WITH user permissions                   │
│                                                                              │
│    STEP 4B: Set Authorization Header (CRITICAL FIX #1)                      │
│    • _httpClient.DefaultRequestHeaders.Authorization =                      │
│        new AuthenticationHeaderValue("Bearer", dataverseToken)              │
│    • This was MISSING in original implementation → caused 401 errors        │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼ Authorization: Bearer {dataverseToken}
┌─────────────────────────────────────────────────────────────────────────────┐
│ 5. Lookup Dataverse User                                                    │
│    DataverseAccessDataSource.LookupDataverseUserIdAsync()                   │
│    • GET /api/data/v9.2/systemusers?                                        │
│      $filter=azureactivedirectoryobjectid eq {userOid}                      │
│    • Uses OBO token (set in Step 4B)                                        │
│    • Maps Azure AD user → Dataverse systemuserid                            │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ 6. Direct Query Authorization (CRITICAL FIX #2)                             │
│    DataverseAccessDataSource.QueryUserPermissionsAsync()                    │
│                                                                              │
│    ORIGINAL APPROACH (FAILED):                                              │
│    • POST /api/data/v9.2/RetrievePrincipalAccess                            │
│    • Returned 404 "Resource not found" with OBO tokens                      │
│    • RetrievePrincipalAccess doesn't support delegated auth                 │
│                                                                              │
│    NEW APPROACH (WORKING):                                                  │
│    • GET /api/data/v9.2/sprk_documents({documentId})?$select=sprk_documentid│
│    • Uses OBO token (user context)                                          │
│    • If 200 OK → User has Read access (Dataverse enforces this)            │
│    • If 403/404 → User doesn't have access                                  │
│    • Return PermissionRecord with AccessRights.Read                         │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ 7. Authorization Decision                                                   │
│    AiAuthorizationService consolidates results                              │
│    • ALL documents authorized → AuthorizationResult.Allowed()               │
│    • ANY document denied → AuthorizationResult.Denied(reason)               │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ 8. Filter Response                                                          │
│    AnalysisAuthorizationFilter.InvokeAsync()                                │
│    • If authorized → await next(context) (continue to endpoint)             │
│    • If denied → Results.Problem(403, "Forbidden", reason)                  │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ 9. AI Analysis Proceeds                                                     │
│    AnalysisEndpoints.ExecuteAnalysisAsync()                                 │
│    • AnalysisOrchestrationService retrieves document content                │
│    • Builds prompt with AnalysisContextBuilder                              │
│    • Calls Azure OpenAI for analysis                                        │
│    • Streams response back to PCF via SSE                                   │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Components involved:**

| Component | Responsibility |
|-----------|---------------|
| `AnalysisAuthorizationFilter` | Endpoint filter - extracts documentIds, triggers authorization |
| `AiAuthorizationService` | Orchestration - extracts bearer token, calls access data source |
| `TokenHelper` | Utility - extracts bearer token from HttpContext |
| `DataverseAccessDataSource` | OBO implementation - MSAL token exchange, Dataverse queries |
| `IAccessDataSource` | Abstraction - pluggable authorization backends (Dataverse, SPE, Azure AD) |

**Key OBO Token Characteristics:**

| Aspect | User's PCF Token (Input) | Dataverse OBO Token (Output) |
|--------|--------------------------|------------------------------|
| **Audience (aud)** | `api://{bff-app-id}` | `https://{org}.crm.dynamics.com` |
| **Scopes** | `user_impersonation` | `.default` (all granted permissions) |
| **Permissions** | Grants access to BFF API | User's Dataverse permissions |
| **Identity (oid)** | User's Azure AD object ID | Same user (preserved) |
| **Usage** | Sent from PCF to BFF | Sent from BFF to Dataverse |

**MSAL OBO Configuration:**

```csharp
// src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs
var cca = ConfidentialClientApplicationBuilder
    .Create(clientId)                              // BFF App Registration ID
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithClientSecret(clientSecret)                // API_CLIENT_SECRET
    .Build();

var result = await cca.AcquireTokenOnBehalfOf(
    scopes: new[] { $"{dataverseUri}/.default" },  // Dataverse audience
    userAssertion: new UserAssertion(userToken)    // PCF user token
).ExecuteAsync(ct);

var dataverseToken = result.AccessToken;           // OBO token for Dataverse
```

**Critical Fixes Implemented:**

| Issue | Symptom | Root Cause | Fix |
|-------|---------|------------|-----|
| **Bug #1** | 401 Unauthorized from Dataverse API | OBO token obtained but never set on HttpClient headers | Set `_httpClient.DefaultRequestHeaders.Authorization` immediately after MSAL exchange |
| **Bug #2** | 404 "Resource not found for RetrievePrincipalAccess" | RetrievePrincipalAccess API doesn't support delegated (OBO) tokens | Changed to direct GET query: if document query succeeds, user has Read access |

**Azure AD Configuration Requirements:**

```
BFF App Registration (1e40baad-e065-4aea-a8d4-4b7ab273458c):
  API Permissions:
    • Dynamics CRM.user_impersonation (Delegated)  ✅ Required for OBO
    • Dynamics CRM.user (Delegated)                ✅ Required for OBO

  Certificates & Secrets:
    • Client Secret: API_CLIENT_SECRET
      Created: 2025-12-18
      Expires: 2027-12-18
      First chars: l8b8Q~J
      Used by: GraphClientFactory, DataverseAccessDataSource, PlaybookService
```

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify IAiAuthorizationService signature | Update all authorization filters (Analysis, Ai) |
| Change OBO token acquisition | Update MSAL configuration, test all AI operations |
| Modify direct query authorization logic | Update DataverseAccessDataSource, verify access checks |
| Change API_CLIENT_SECRET | Update Key Vault, App Settings, test OBO flow |
| Add new authorization data source | Implement IAccessDataSource, register in DI |

**Debugging with Application Insights:**

```kql
// Trace complete OBO authorization flow
traces
| where timestamp > ago(1h)
| where message contains "UAC-DIAG" or message contains "AI-AUTH" or message contains "OBO-DIAG"
| project timestamp, message, severityLevel
| order by timestamp desc

// Find OBO token exchange errors
exceptions
| where timestamp > ago(1h)
| where outerMessage contains "AcquireTokenOnBehalfOf" or outerMessage contains "RetrievePrincipalAccess"
| project timestamp, outerMessage, problemId, severityLevel
```

**Security Notes:**
- **Fail-closed**: Authorization errors return `AccessRights.None` (deny access)
- **No token caching**: Each request performs fresh OBO exchange (future: add Redis caching with 55-min TTL)
- **No token leakage**: User tokens never logged, only hashed identifiers in diagnostics
- **Audit trail**: All authorization decisions logged with correlation IDs

### Pattern 8: Document Relationship Visualization Flow (2026-01-12)

```
User → PCF (DocumentRelationshipViewer) → BFF API (VisualizationEndpoints) → Azure AI Search
                │                                      │
                │  React Flow                          ├──→ VisualizationService → Vector similarity search
                │  d3-force layout                     │      (documentVector3072 - 3072 dim)
                │                                      │
                ▼                                      └──→ VisualizationAuthorizationFilter → Dataverse
        Interactive Graph
        (nodes = documents,
         edges = similarity)
```

**Components involved:**
1. `src/client/pcf/DocumentRelationshipViewer/` — React Flow canvas with d3-force layout
   - `DocumentRelationshipViewer.tsx` — Main control with Fluent UI v9
   - `components/DocumentGraph.tsx` — React Flow graph with force-directed layout
   - `components/DocumentNode.tsx` — Custom node with file type icons, similarity badges
   - `components/DocumentEdge.tsx` — Similarity-based edge styling
   - `components/ControlPanel.tsx` — Threshold, depth, node limit filters
   - `components/NodeActionBar.tsx` — Open Document Record, View in SPE actions
   - `services/VisualizationApiService.ts` — API client with type mapping
   - `hooks/useVisualizationApi.ts` — Data fetching hook
2. `src/server/api/Sprk.Bff.Api/Api/Ai/VisualizationEndpoints.cs` — GET /api/ai/visualization/related/{documentId}
3. `src/server/api/Sprk.Bff.Api/Services/Ai/VisualizationService.cs` — Document similarity via vector search
4. `src/server/api/Sprk.Bff.Api/Api/Filters/VisualizationAuthorizationFilter.cs` — Resource-based auth

**Key Features (AI Search & Visualization Module):**
- **3072-dim document vectors**: `documentVector3072` field for whole-document similarity
- **Orphan file support**: Files without Dataverse records (`documentId` nullable, `speFileId` required)
- **Force-directed layout**: Edge distance = `200 * (1 - similarity)` for natural clustering
- **Real-time filtering**: Similarity threshold, depth limit, max nodes per level
- **Dark mode**: Full Fluent UI v9 token support (ADR-021)

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify VisualizationService vector search | Update test mocks, verify similarity calculations |
| Change graph data model | Update PCF TypeScript types, VisualizationApiService |
| Add new filter options | Update ControlPanel UI, endpoint query parameters |
| Modify node/edge styling | Update DocumentNode/DocumentEdge components |
| Change authorization rules | Update VisualizationAuthorizationFilter |

**Key Files:**
- `VisualizationEndpoints.cs` — GET /api/ai/visualization/related/{documentId}?tenantId={tenantId}
- `VisualizationService.cs` — Uses `IRagService` for vector search with `documentVector3072`
- `IVisualizationService.cs` — Interface with `DocumentNodeData`, `DocumentEdgeData` models
- PCF bundle: 6.65 MB (React 16, Fluent UI v9 externalized via platform-library)

### Pattern 9: RAG File Indexing Flow (2026-01-16)

RAG file indexing uses a **job queue pattern** with API key validation for background/automated operations:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      RAG FILE INDEXING ARCHITECTURE                          │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │                        ENTRY POINTS                                     ││
│  │  ─────────────────────────────────────────────────────────────────────  ││
│  │                                                                          ││
│  │  POST /api/ai/rag/index-file        → OBO token (user-initiated)        ││
│  │  POST /api/ai/rag/enqueue-indexing  → X-Api-Key header (jobs/scripts)   ││
│  │                                                                          ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                      │                               │                       │
│                      │ Immediate                     │ Async                 │
│                      ▼                               ▼                       │
│  ┌─────────────────────────────┐    ┌─────────────────────────────────────┐ │
│  │    FileIndexingService      │    │     JobSubmissionService            │ │
│  │  ───────────────────────────│    │  ───────────────────────────────────│ │
│  │  IndexFileAsync() [OBO]     │    │  Submit to Azure Service Bus        │ │
│  │  (reads & indexes inline)   │    │  JobTypeName = "RagIndexing"        │ │
│  └─────────────────────────────┘    └─────────────────────────────────────┘ │
│                      │                               │                       │
│                      │                               ▼                       │
│                      │              ┌─────────────────────────────────────┐ │
│                      │              │   RagIndexingJobHandler             │ │
│                      │              │  ───────────────────────────────────│ │
│                      │              │  IndexFileAppOnlyAsync() [app-only] │ │
│                      │              │  (processes from Service Bus queue) │ │
│                      │              └─────────────────────────────────────┘ │
│                      │                               │                       │
│                      └───────────────┬───────────────┘                      │
│                                      ▼                                      │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │                      GraphClientFactory                                 ││
│  │  ─────────────────────────────────────────────────────────────────────  ││
│  │  ForApp()           → ClientSecretCredential (app-only for jobs)        ││
│  │  ForUserAsync(ctx)  → OBO token exchange (delegated for user-initiated) ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                      │                                       │
│                                      ▼                                       │
│                     ┌─────────────────────────────┐                         │
│                     │       SpeFileStore          │                         │
│                     │   (ISpeFileOperations)      │                         │
│                     └─────────────────────────────┘                         │
│                                      │                                      │
│                                      ▼                                      │
│                     ┌─────────────────────────────┐                         │
│                     │  DriveItemOperations        │                         │
│                     │  DownloadFileAsync()        │                         │
│                     └─────────────────────────────┘                         │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Entry Points for RAG Indexing:**

| Endpoint | Auth Pattern | Use Case | Processing |
|----------|-------------|----------|------------|
| `POST /api/ai/rag/index-file` | OBO (Pattern 7) | User-initiated via PCF | Synchronous, inline indexing |
| `POST /api/ai/rag/enqueue-indexing` | X-Api-Key header | Background jobs, scheduled indexing, bulk ops, scripts | Async via Service Bus job queue |

**Components involved:**
1. `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs` — Both endpoints defined here
2. `src/server/api/Sprk.Bff.Api/Services/Ai/IFileIndexingService.cs` — Interface with entry points
3. `src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs` — Unified pipeline implementation
4. `src/server/api/Sprk.Bff.Api/Services/Jobs/JobSubmissionService.cs` — Service Bus job submission
5. `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RagIndexingJobHandler.cs` — Async job processor
6. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` — Auth factory (shared)
7. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs` — File download operations
8. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` — SPE facade (ISpeFileOperations)

**API Key Authentication (enqueue-indexing endpoint):**

The `/api/ai/rag/enqueue-indexing` endpoint uses API key validation similar to the email webhook pattern:

```
Request Header: X-Api-Key: {api-key}
Server Config:  Rag:ApiKey (App Service setting: Rag__ApiKey)

Validation Flow:
1. Extract X-Api-Key from request headers
2. Compare against Rag:ApiKey from configuration
3. If missing or mismatch → 401 Unauthorized
4. If match → Submit job to Service Bus queue
```

**Configuration (App Service Settings):**

| Setting | Description | Example |
|---------|-------------|---------|
| `Rag__ApiKey` | API key for enqueue-indexing endpoint | `rag-key-{guid}` |

**Security Notes:**
- API key is stored in App Service configuration (should migrate to Key Vault)
- API key should be rotated periodically
- Scripts/jobs should retrieve from environment variable `RAG_API_KEY`

**Job Queue Processing:**
```
1. POST /api/ai/rag/enqueue-indexing with X-Api-Key header
2. Validate API key against Rag:ApiKey config
3. Create JobContract with JobTypeName="RagIndexing" and FileIndexRequest payload
4. Submit to Azure Service Bus via JobSubmissionService
5. Return 202 Accepted with JobId, CorrelationId, IdempotencyKey
6. RagIndexingJobHandler picks up job from queue
7. Calls FileIndexingService.IndexFileAppOnlyAsync() with app-only auth
8. Job completes with indexing result
```

**Unified Pipeline (all paths converge):**
```
1. Download file (OBO or app-only based on entry point)
2. Extract text via ITextExtractor
3. Chunk text via ITextChunkingService
4. Build KnowledgeDocument objects for each chunk
5. Batch index via IRagService.IndexDocumentsBatchAsync
6. Return FileIndexingResult with statistics
```

**Shared Infrastructure with Email-to-Document:**

| Component | RAG Indexing | Email-to-Document |
|-----------|-------------|-------------------|
| `GraphClientFactory.ForApp()` | ✅ Downloads (job handler) | ✅ Uploads |
| `SpeFileStore` | ✅ Via ISpeFileOperations | ✅ Direct |
| `DriveItemOperations` | ✅ DownloadFileAsync | — |
| `JobSubmissionService` | ✅ Enqueue indexing jobs | ✅ Enqueue email jobs |
| `UploadSessionManager` | — | ✅ UploadSmallAsync |

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify FileIndexingService pipeline | Both OBO and app-only paths affected |
| Change GraphClientFactory auth | All SPE operations (email, RAG, visualization) affected |
| Update ISpeFileOperations interface | SpeFileStore + all consumers affected |
| Modify chunking/embedding strategy | IRagService, ITextChunkingService affected |
| Change Rag:ApiKey | Update App Service config + all calling scripts |
| Modify job queue payload | Update RagEndpoints + RagIndexingJobHandler |

**Key Files:**
- `RagEndpoints.cs` — POST /api/ai/rag/index-file (OBO), POST /api/ai/rag/enqueue-indexing (API key)
- `RagIndexingJobHandler.cs` — Service Bus job handler, calls `IndexFileAppOnlyAsync()`
- `JobSubmissionService.cs` — Submits `JobContract` to Service Bus
- `FileIndexingService.cs` — Unified pipeline with entry points for OBO and app-only
- `IFileIndexingService.cs` — Interface: `FileIndexRequest`, `ContentIndexRequest`, `FileIndexingResult`
- `GraphClientFactory.cs` — `ForApp()` and `ForUserAsync()` for auth
- `index-docs.ps1` — Example script using enqueue-indexing endpoint with X-Api-Key header

---

## Shared Dependencies

### Shared Configuration

| Config Key | Used By | Location |
|------------|---------|----------|
| `BffApiBaseUrl` | All PCF controls | PCF environment config |
| `ContainerTypeId` | Upload, listing endpoints | appsettings.json |
| `Redis:InstanceName` | BFF caching | appsettings.json |
| `AzureAd:*` | BFF auth, PCF MSAL | appsettings.json, msalConfig.ts |
| `EmailProcessing:*` | Webhook, polling, job handler | appsettings.json |

### Shared Types/Contracts

| Contract | Producer | Consumers |
|----------|----------|-----------|
| `ContainerDto` | BFF API | PCF controls (TypeScript interface) |
| `FileHandleDto` | BFF API | SpeFileViewer, UniversalQuickCreate |
| `UploadSessionDto` | BFF API | UniversalQuickCreate |
| Policy names (`graph-write`, etc.) | BFF Program.cs | BFF endpoints |

**Rule:** When modifying a DTO in the BFF, update the corresponding TypeScript interface in PCF controls.

---

## Cross-Cutting Concerns

### Error Handling Chain

```
Graph ODataError → BFF ProblemDetailsHelper → HTTP Response → PCF Error Handler
```

| Layer | Error Handling |
|-------|----------------|
| BFF Infrastructure | Catch `ODataError`, map to `ProblemDetails` |
| BFF Endpoints | Return `IResult` with proper status codes |
| PCF Controls | Parse `ProblemDetails`, show user-friendly message |

**Change Impact:** Modifying `ProblemDetailsHelper` affects all PCF error displays.

### Telemetry/Logging

```
PCF (console) → BFF (Serilog + App Insights) → Azure Monitor
```

| Layer | Telemetry |
|-------|-----------|
| PCF Controls | Browser console, optional App Insights |
| BFF API | Serilog structured logging, OpenTelemetry metrics |
| Infrastructure | Azure Monitor, Log Analytics |

**Change Impact:** Adding new telemetry dimensions requires BFF and Azure configuration.

---

## Dependency Direction Rules

### Allowed Dependencies

```
PCF Controls ──────────→ BFF API (HTTP)
BFF API ───────────────→ Graph API (SDK)
BFF API ───────────────→ Dataverse API (HTTP)
BFF API ───────────────→ Azure Services (SDK)
Tests ─────────────────→ Any src/ code
```

### Prohibited Dependencies

```
BFF API ──────────✗────→ PCF Controls (no reverse dependency)
Graph Infrastructure ─✗─→ Dataverse Infrastructure (isolated)
src/ ─────────────✗────→ tests/ (no test code in production)
```

---

## Change Checklist by Component

### BFF API Endpoint Changes

- [ ] Update endpoint in `Api/*.cs`
- [ ] Update tests in `tests/unit/Sprk.Bff.Api.Tests/`
- [ ] Update PCF API client if contract changed
- [ ] Update API documentation/comments
- [ ] Verify rate limiting policies still apply

### PCF Control Changes

- [ ] Update control source in `src/client/pcf/{Control}/`
- [ ] Run `npm run build` in pcf directory
- [ ] Test in Dataverse environment
- [ ] Update control manifest if properties changed
- [ ] Increment version in `ControlManifest.Input.xml`

### Infrastructure (Bicep) Changes

- [ ] Update module in `infrastructure/bicep/modules/`
- [ ] Update stack references in `infrastructure/bicep/stacks/`
- [ ] Update parameter files if new parameters added
- [ ] Test with `az deployment group what-if`
- [ ] Update `AZURE-RESOURCE-NAMING-CONVENTION.md` if naming affected

### Dataverse Schema Changes

- [ ] Update solution in `src/dataverse/solutions/`
- [ ] Update BFF Dataverse queries if fields changed
- [ ] Update PCF form bindings if bound fields changed
- [ ] Update any related documentation

---

## Quick Lookup: Component Locations

| Component | Primary Location | Test Location |
|-----------|------------------|---------------|
| BFF Endpoints | `src/server/api/Sprk.Bff.Api/Api/` | `tests/unit/Sprk.Bff.Api.Tests/` |
| BFF Graph Operations | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/` | Same test project |
| BFF Auth | `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/` | Same test project |
| **BFF Email Services** | `src/server/api/Sprk.Bff.Api/Services/Email/` | `tests/unit/Sprk.Bff.Api.Tests/Services/Email/` |
| **Email Models** | `src/server/api/Sprk.Bff.Api/Models/Email/` | — |
| **BFF Office Workers** | `src/server/api/Sprk.Bff.Api/Workers/Office/` | `tests/unit/Sprk.Bff.Api.Tests/Workers/Office/` |
| **Office Endpoints** | `src/server/api/Sprk.Bff.Api/Api/Office/` | Same test project |
| **Office Entity Models** | `src/server/api/Sprk.Bff.Api/Models/` | — |
| **Dataverse Service** | `src/server/shared/Spaarke.Dataverse/` | `tests/unit/Spaarke.Dataverse.Tests/` |
| **BFF AI Services** | `src/server/api/Sprk.Bff.Api/Services/Ai/` | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/` |
| **AI Export Services** | `src/server/api/Sprk.Bff.Api/Services/Ai/Export/` | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/` |
| **AI Endpoints** | `src/server/api/Sprk.Bff.Api/Api/Ai/` | Same test project |
| **AI Models** | `src/server/api/Sprk.Bff.Api/Models/Ai/` | — |
| **AI Resilience** | `src/server/api/Sprk.Bff.Api/Infrastructure/Resilience/` | Same test project |
| **AI Telemetry** | `src/server/api/Sprk.Bff.Api/Telemetry/` | — |
| **AI Filters** | `src/server/api/Sprk.Bff.Api/Api/Filters/` | Same test project |
| PCF UniversalQuickCreate | `src/client/pcf/UniversalQuickCreate/` | Manual testing |
| PCF SpeFileViewer | `src/client/pcf/SpeFileViewer/` | Manual testing |
| PCF AnalysisWorkspace | `src/client/pcf/AnalysisWorkspace/` (v1.2.7) | Manual testing |
| **PCF DocumentRelationshipViewer** | `src/client/pcf/DocumentRelationshipViewer/` (v1.0.18) | `__tests__/` (40 component tests) |
| **Visualization Endpoints** | `src/server/api/Sprk.Bff.Api/Api/Ai/VisualizationEndpoints.cs` | Same test project |
| **Visualization Service** | `src/server/api/Sprk.Bff.Api/Services/Ai/VisualizationService.cs` | Same test project (27 tests) |
| **RAG Endpoints** | `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs` | Same test project |
| **RAG Indexing Job Handler** | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RagIndexingJobHandler.cs` | Same test project |
| **File Indexing Service** | `src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs` | Same test project |
| PCF Shared Auth | `src/client/pcf/*/services/auth/` | — |
| Bicep Modules | `infrastructure/bicep/modules/` | `what-if` validation |
| Bicep AI Modules | `infrastructure/bicep/modules/dashboard.bicep`, `alerts.bicep` | `what-if` validation |
| Dataverse Plugins | `src/dataverse/plugins/` | `tests/unit/` |
| Load Test Scripts | `scripts/load-tests/` | — |

---

## See Also

- `/docs/ai-knowledge/architecture/sdap-overview.md` — System overview
- `/docs/ai-knowledge/architecture/sdap-bff-api-patterns.md` — BFF patterns
- `/docs/ai-knowledge/architecture/sdap-pcf-patterns.md` — PCF patterns
- `/docs/reference/architecture/SPAARKE-REPOSITORY-ARCHITECTURE.md` — Full repo structure
