# SDAP Workspace Integration Patterns

> **Last Updated:** February 20, 2026
> **Applies To:** Corporate Workspace SPA, BFF API endpoint patterns, entity creation flows
> **Related:** [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md), [sdap-component-interactions.md](sdap-component-interactions.md)

---

## TL;DR

The Corporate Workspace introduced several new architectural patterns to SDAP: (1) **Entity-Agnostic Creation Service** — a TypeScript service that handles SPE upload, Dataverse record creation, and AI analysis triggering for any entity type; (2) **Document Operations Endpoints** — BFF endpoints for document checkout/checkin/delete/analyze; (3) **Workspace Matter Endpoints** — BFF AI pre-fill for entity creation wizards; (4) **App-Only Document Analysis via Service Bus** — background AI profiling triggered from entity creation flows. These patterns extend the existing Job Contract (ADR-004) and "SPE First, Dataverse Second" patterns to workspace-driven entity creation.

---

## 1. Entity-Agnostic Creation Pattern

### Problem

Each entity type (Matter, Project, Invoice) needs the same creation workflow: upload files to SPE, create the parent entity in Dataverse, link documents to the parent, trigger AI analysis. Without abstraction, each entity type would duplicate this logic.

### Solution

`EntityCreationService.ts` provides entity-agnostic methods parameterized by entity name and navigation property. Entity-specific services (e.g., `MatterService`) handle payload construction and entity-specific follow-on actions, then delegate shared infrastructure to `EntityCreationService`.

```
┌─────────────────────────────────────────────────────────────────┐
│  MatterService                    (entity-specific)              │
│  ├─ buildMatterEntity()           sprk_matter payload            │
│  ├─ _discoverNavProps()           OData nav-prop discovery        │
│  ├─ generateMatterNumber()        {typeCode}-{random6}           │
│  └─ executeFollowOnActions()      assign counsel, email, etc.    │
│                                                                  │
│  Delegates to EntityCreationService:                             │
│  ├─ uploadFilesToSpe()            PUT /api/obo/containers/…/files│
│  ├─ createDocumentRecords()       Xrm.WebApi.createRecord        │
│  └─ _triggerDocumentAnalysis()    POST /api/documents/{id}/analyze│
└─────────────────────────────────────────────────────────────────┘
```

### EntityCreationService Methods

| Method | Purpose | Entity-Specific? |
|--------|---------|-----------------|
| `uploadFilesToSpe(files, containerId)` | Upload browser files to SPE via BFF OBO endpoint | No |
| `createDocumentRecords(entityName, entityId, navProp, files, options)` | Create `sprk_document` records linked to parent entity | No (parameterized) |
| `_triggerDocumentAnalysis(documentIds)` | Queue AI Document Profile for each document | No |
| `requestAiPreFill(files)` | Call BFF pre-fill endpoint | Currently matter-specific (r2: entity-agnostic) |

### Document Record Fields (Entity-Agnostic)

Every `sprk_document` created by `EntityCreationService` includes:

```typescript
{
  sprk_documentname: file.name,           // Primary name
  sprk_filename: file.name,               // Original file name
  sprk_driveitemid: file.id,              // SPE item reference
  sprk_graphitemid: file.id,              // BFF view-url resolution
  sprk_graphdriveid: containerId,         // BFF view-url resolution
  sprk_containerid: containerId,          // SPE container reference
  sprk_filepath: file.webUrl,             // Direct URL to file
  sprk_filesize: file.size,              // File size in bytes
  sprk_sourcetype: 659490000,            // User Upload
  sprk_filesummarystatus: 100000001,     // Pending (for Document Profile)
  sprk_regardingrecordid: parentEntityId, // Association resolver
  [`${navProp}@odata.bind`]: `/${entityName}(${parentEntityId})`, // Parent lookup
}
```

### Adding a New Entity Type

Create a new `{Entity}Service.ts` that:

1. Builds the entity payload with entity-specific fields
2. Calls `_discoverNavProps('{entityLogicalName}')` for lookup bindings
3. Delegates to `EntityCreationService.uploadFilesToSpe()`
4. Delegates to `EntityCreationService.createDocumentRecords()` with entity-specific `parentEntityName` and `navigationProperty`
5. Implements entity-specific follow-on actions

No changes needed to `EntityCreationService` itself.

---

## 2. Document Operations Endpoints

### New Endpoint Group

`DocumentOperationsEndpoints.cs` adds document lifecycle management endpoints to the BFF:

```
POST   /api/documents/{documentId}/checkout          # Lock document for editing
POST   /api/documents/{documentId}/checkin            # Unlock + save new version
POST   /api/documents/{documentId}/discard            # Discard checkout
DELETE /api/documents/{documentId}                    # Delete from Dataverse + SPE
GET    /api/documents/{documentId}/checkout-status    # Query lock state
POST   /api/documents/{documentId}/analyze            # Trigger AI Document Profile
```

### Analyze Endpoint (New)

The `POST /api/documents/{documentId}/analyze` endpoint is critical for workspace integration. It enables the creation wizard to trigger AI Document Profile analysis after document records are created.

**Pattern**: Fire-and-forget via Service Bus (returns `202 Accepted`)

```csharp
// DocumentOperationsEndpoints.cs
var analysisJob = new JobContract
{
    JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,  // "AppOnlyDocumentAnalysis"
    SubjectId = documentId.ToString(),
    IdempotencyKey = $"analysis-{documentId}-documentprofile",
    Payload = JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        DocumentId = documentId,
        PlaybookName = "Document Profile",
        Source = "MatterCreationWizard",
        EnqueuedAt = DateTimeOffset.UtcNow
    }))
};
await jobSubmissionService.SubmitJobAsync(analysisJob, ct);
return TypedResults.Accepted(..., new { jobId, documentId, status = "queued" });
```

**Response**: `202 Accepted` with `{ jobId, documentId, status: "queued" }`

**Key design decisions**:
- Returns immediately (non-blocking for wizard completion)
- Failures are non-fatal — document exists regardless of analysis outcome
- Idempotency key prevents duplicate processing
- `Source` field enables telemetry segmentation by trigger origin

### Re-indexing on Check-in

Document check-in optionally triggers RAG re-indexing:

```
POST /api/documents/{id}/checkin
    │
    ├─ 1. Release checkout lock in SPE
    ├─ 2. Update sprk_document version metadata
    └─ 3. (Optional) Queue RagIndexingJobHandler
         • Controlled by ReindexingOptions configuration
         • Non-blocking (failure doesn't fail check-in)
         • Idempotency key: checkin-reindex-{documentId}-{ticks}
```

---

## 3. Workspace Matter Endpoints

### Pre-Fill Endpoint

`POST /api/workspace/matters/pre-fill` enables AI-powered form field extraction during entity creation. See [WORKSPACE-AI-PREFILL-GUIDE.md](../guides/WORKSPACE-AI-PREFILL-GUIDE.md) for full documentation.

**Architecture integration points**:

```
┌──────────────────────────────┐
│ WorkspaceMatterEndpoints.cs   │
│ POST /workspace/matters/      │
│ pre-fill                      │
│ • WorkspaceAuthorizationFilter│
│ • ai-stream rate limit policy │
└──────────┬───────────────────┘
           │
    ┌──────▼──────────────────────┐
    │ MatterPreFillService.cs      │
    │ • SpeFileStore (staging)     │  ← ADR-007: No direct SPE access
    │ • ITextExtractor             │
    │ • IPlaybookOrchestrationService │ ← ADR-013: Playbook for AI
    └─────────────────────────────┘
```

**ADR compliance**:
- ADR-007: File staging uses `SpeFileStore` facade (no Graph SDK types leak)
- ADR-008: Endpoint filter (`WorkspaceAuthorizationFilter`) not global middleware
- ADR-009: Not applicable (no caching needed for pre-fill)
- ADR-013: AI via `IPlaybookOrchestrationService` (no direct OpenAI calls)

### Workspace Authorization Filter

New endpoint filter following ADR-008:

```csharp
// WorkspaceAuthorizationFilter.cs
// 1. Extract user ID from claims (prefer "oid", fallback NameIdentifier)
// 2. Store in HttpContext.Items["UserId"]
// 3. Return 401 if identity not found
// 4. Does NOT do matter-level access control (PortfolioService responsibility)
```

---

## 4. App-Only Document Analysis

### Background

Document Profile analysis must run after entity creation when the user's OBO session may have ended. The `AppOnlyDocumentAnalysisJobHandler` handles this using application-only authentication.

### Flow

```
Entity Creation Wizard (client)
    │
    ├─ POST /api/documents/{id}/analyze  (OBO auth → BFF)
    │
    └─ BFF: JobSubmissionService.SubmitJobAsync()
         │
         └─ Azure Service Bus (sdap-jobs queue)
              │
              └─ ServiceBusJobProcessor routes to AppOnlyDocumentAnalysisJobHandler
                   │
                   ├─ 1. Read sprk_document from Dataverse (app-only)
                   ├─ 2. Download file from SPE (app-only auth)
                   ├─ 3. Run "Document Profile" playbook
                   ├─ 4. Update sprk_document fields:
                   │     • sprk_filesummary
                   │     • sprk_filetldr
                   │     • sprk_keywords
                   │     • sprk_entities
                   │     • sprk_classification
                   │     • sprk_filesummarystatus → Completed
                   └─ 5. Return JobOutcome (Completed / Failed / Poisoned)
```

### Job Contract

```csharp
{
  JobType: "AppOnlyDocumentAnalysis",
  SubjectId: "{documentId}",
  IdempotencyKey: "analysis-{documentId}-documentprofile",
  Payload: {
    DocumentId: Guid,
    PlaybookName: "Document Profile",
    Source: "MatterCreationWizard" | "EmailAttachment" | "BulkImport" | "Manual",
    EnqueuedAt: DateTimeOffset
  }
}
```

### Failure Handling

| Outcome | Behavior |
|---------|----------|
| `Completed` | Document fields updated, status → Completed |
| `Failed` (transient) | Retried up to MaxAttempts (3), then dead-lettered |
| `Poisoned` (permanent) | Document not found, unsupported playbook → dead-letter immediately |
| HTTP 429 (throttled) | Exponential backoff per ADR-016 |

### Source Tracking

The `Source` field enables telemetry segmentation:

| Source | Trigger | Auth Mode |
|--------|---------|-----------|
| `MatterCreationWizard` | Create New Matter dialog | App-only (Service Bus) |
| `EmailAttachment` | Email-to-document pipeline | App-only (Service Bus) |
| `BulkImport` | Batch document import | App-only (Service Bus) |
| `Manual` | User-initiated from document view | App-only (Service Bus) |

---

## 5. OData Navigation Property Discovery

### Problem

Dataverse requires PascalCase navigation property names for `@odata.bind` lookups, but column logical names are lowercase. Hardcoding PascalCase names is fragile — they can vary by entity and environment.

### Solution

Query entity metadata at runtime:

```
GET /api/data/v9.0/EntityDefinitions(LogicalName='{entity}')
    /ManyToOneRelationships
    ?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName
```

Returns: `{ sprk_mattertype: "sprk_MatterType", sprk_practicearea: "sprk_PracticeArea", ... }`

### Implementation

```typescript
// matterService.ts
async _discoverNavProps(entityLogicalName: string): Promise<Record<string, string>> {
  // Cache per entity for the session
  if (this._navPropCache[entityLogicalName]) return this._navPropCache[entityLogicalName];

  const response = await fetch(
    `${orgUrl}/api/data/v9.0/EntityDefinitions(LogicalName='${entityLogicalName}')/ManyToOneRelationships?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName`
  );
  const data = await response.json();

  const map: Record<string, string> = {};
  for (const rel of data.value) {
    map[rel.ReferencingAttribute] = rel.ReferencingEntityNavigationPropertyName;
  }

  this._navPropCache[entityLogicalName] = map;
  return map;
}
```

**Fallback**: If metadata discovery fails, falls back to lowercase column name (works for some fields, not all).

---

## 6. Container Resolution for Workspace

### Problem

The Create New Matter wizard runs in a Vite SPA web resource (iframe) without direct access to environment configuration. It needs the SPE container ID to upload files and store on entity records.

### Solution

Container ID is resolved from the user's Business Unit via Xrm.WebApi:

```
User ID (Xrm global context)
  → systemuser record (Xrm.WebApi.retrieveRecord)
    → businessunitid ($expand=businessunitid)
      → businessunit.sprk_containerid
```

```typescript
// xrmProvider.ts
async getSpeContainerIdFromBusinessUnit(webApi: IWebApi): Promise<string> {
  const userId = getXrm().Utility.getGlobalContext().userSettings.userId;
  const user = await webApi.retrieveRecord('systemuser', userId,
    '?$select=businessunitid&$expand=businessunitid($select=sprk_containerid)');
  return user.businessunitid.sprk_containerid;
}
```

**Key points**:
- Container ID format: `b!...` (Graph Drive ID, base64-encoded)
- Stored on the matter record (`sprk_containerid`) for PCF document grid to use
- Stored on each document record (`sprk_graphdriveid`, `sprk_containerid`) for BFF preview/download
- All entities in the same environment currently point to the same container

---

## Impact on Existing Architecture

### New Components Added

| Component | Type | Location |
|-----------|------|----------|
| `EntityCreationService.ts` | TypeScript service | `src/solutions/LegalWorkspace/src/services/` |
| `DocumentOperationsEndpoints.cs` | BFF endpoints | `src/server/api/Sprk.Bff.Api/Api/` |
| `WorkspaceMatterEndpoints.cs` | BFF endpoints | `src/server/api/Sprk.Bff.Api/Api/Workspace/` |
| `WorkspaceAuthorizationFilter.cs` | Endpoint filter | `src/server/api/Sprk.Bff.Api/Api/Workspace/` |
| `MatterPreFillService.cs` | BFF service | `src/server/api/Sprk.Bff.Api/Services/Workspace/` |
| `AppOnlyDocumentAnalysisJobHandler.cs` | Job handler | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/` |

### Existing Patterns Extended

| Pattern | Extension | Location |
|---------|-----------|----------|
| Job Contract (ADR-004) | New `AppOnlyDocumentAnalysis` job type | `JobContract.cs`, `ServiceBusJobProcessor.cs` |
| SPE First, Dataverse Second | Flow Variant C: entity doesn't exist yet at upload time | `sdap-bff-api-patterns.md` |
| Endpoint Filters (ADR-008) | New `WorkspaceAuthorizationFilter` | `WorkspaceMatterEndpoints.cs` |
| Playbook Orchestration | Pre-fill uses playbook for structured extraction | `MatterPreFillService.cs` |

### Job Handler Registry (Updated)

The Service Bus processor now routes to these handlers:

| JobType | Handler | Purpose |
|---------|---------|---------|
| `AppOnlyDocumentAnalysis` | `AppOnlyDocumentAnalysisJobHandler` | AI Document Profile (background) |
| `EmailToDocument` | `EmailToDocumentJobHandler` | Email-to-document conversion |
| `RagIndexing` | `RagIndexingJobHandler` | RAG document indexing |
| `ProfileSummary` | `ProfileSummaryJobHandler` | AI profile generation |
| `BulkRagIndexing` | `BulkRagIndexingJobHandler` | Batch indexing operations |
| `EmailAnalysis` | `EmailAnalysisJobHandler` | Email-specific AI analysis |
| `AttachmentClassification` | `AttachmentClassificationJobHandler` | Attachment classification |
| `DocumentProcessing` | `DocumentProcessingJobHandler` | General document processing |
| `BatchProcessEmails` | `BatchProcessEmailsJobHandler` | Batch email processing |
| `InvoiceExtraction` | `InvoiceExtractionJobHandler` | Invoice data extraction |
| `InvoiceIndexing` | `InvoiceIndexingJobHandler` | Invoice indexing |
| `SpendSnapshotGeneration` | `SpendSnapshotGenerationJobHandler` | Spend analytics |

---

## Cross-Reference

| Topic | Document |
|-------|----------|
| Entity creation wizard (full guide) | [WORKSPACE-ENTITY-CREATION-GUIDE.md](../guides/WORKSPACE-ENTITY-CREATION-GUIDE.md) |
| AI pre-fill (detailed) | [WORKSPACE-AI-PREFILL-GUIDE.md](../guides/WORKSPACE-AI-PREFILL-GUIDE.md) |
| BFF API patterns (uploads, auth) | [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) |
| Component interactions | [sdap-component-interactions.md](sdap-component-interactions.md) |
| AI architecture overview | [SPAARKE-AI-STRATEGY.md](SPAARKE-AI-STRATEGY.md) |
| AI playbook architecture | [AI-PLAYBOOK-ARCHITECTURE.md](AI-PLAYBOOK-ARCHITECTURE.md) |
