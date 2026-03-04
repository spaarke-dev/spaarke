# AI Trigger Configuration Method

> **Project**: ai-trigger-configuration-method
> **Status**: Design
> **Date**: 2026-03-02
> **Branch**: TBD (will be created from `master` when project starts)

---

## 1. Executive Summary

Today, every background AI analysis pathway is **hardcoded** — the `AppOnlyDocumentAnalysisJobHandler` always runs the `"Document Profile"` playbook, the `EmailAnalysisJobHandler` always runs `"Email Analysis"`, and so on. Adding a new trigger (e.g., "run playbook X when a Matter is created") requires writing a new `IJobHandler`, a new payload class, new DI registration, and a new Service Bus enqueue endpoint.

This project replaces the hardcoded handler-to-playbook coupling with a **configuration-driven trigger table** (`sprk_playbooktrigger`) in Dataverse. Administrators will be able to:

- Assign any playbook to any supported trigger event
- Configure per-trigger parameters (filter expressions, execution mode, priority)
- Enable/disable triggers without code changes or deployments

The BFF API will gain a `PlaybookTriggerService` that resolves matching triggers at runtime and delegates to `PlaybookOrchestrationService.ExecuteAppOnlyAsync()` (the node-based engine wired in the current sprint).

---

## 2. Problem Statement

### Current Architecture: Hardcoded Job Handlers

```
┌──────────────────────────┐     ┌──────────────────────────────────┐
│ Trigger Source            │     │ Service Bus Job Queue             │
│ (Upload, Email, Ribbon)   │────▶│ JobType → Handler mapping         │
└──────────────────────────┘     └──────────┬───────────────────────┘
                                            │
                        ┌───────────────────┼───────────────────────┐
                        ▼                   ▼                       ▼
            ┌───────────────────┐ ┌────────────────────┐  ┌─────────────────┐
            │ AppOnlyDocAnalysis│ │ EmailAnalysis       │  │ ProfileSummary  │
            │ JobHandler        │ │ JobHandler           │  │ JobHandler      │
            │                   │ │                      │  │                 │
            │ Hardcoded:        │ │ Hardcoded:           │  │ Hardcoded:      │
            │ "Document Profile"│ │ "Email Analysis"     │  │ content-type    │
            │                   │ │                      │  │ mapping         │
            └───────────────────┘ └────────────────────┘  └─────────────────┘
```

### Problems

| Problem | Impact |
|---------|--------|
| **Adding a trigger = code change** | Every new playbook-to-event binding requires a new handler class, payload, DI registration, and deployment |
| **No admin configurability** | Business users cannot assign playbooks to events without developer involvement |
| **Playbook name coupling** | Handlers reference playbook names as string constants — renaming a playbook breaks processing |
| **No multi-playbook triggers** | A single event (e.g., document upload) can only trigger one hardcoded playbook |
| **No conditional execution** | Cannot filter triggers by entity type, document category, or other metadata |

---

## 3. Current State Inventory

### 3.1 Job Handler Architecture

**Infrastructure files** (unchanged by this project):

| File | Purpose |
|------|---------|
| `Services/Jobs/IJobHandler.cs` | Handler interface: `JobType` + `ProcessAsync(JobContract, CancellationToken)` |
| `Services/Jobs/JobContract.cs` | Job payload envelope: JobId, JobType, SubjectId, Payload (JSON), IdempotencyKey |
| `Services/Jobs/JobSubmissionService.cs` | Enqueues jobs to Azure Service Bus |
| `Services/Jobs/ServiceBusJobProcessor.cs` | BackgroundService — dequeues, dispatches to `IJobHandler` by `job.JobType` match |
| `Services/Jobs/IIdempotencyService.cs` | Redis-backed deduplication (7-day retention) |

**Existing playbook-triggering handlers** (to be migrated):

| Handler | File | JobType Constant | Hardcoded Playbook | Trigger Source |
|---------|------|------------------|--------------------|----------------|
| `AppOnlyDocumentAnalysisJobHandler` | `Services/Jobs/Handlers/AppOnlyDocumentAnalysisJobHandler.cs` | `"AppOnlyDocumentAnalysis"` | `"Document Profile"` (default) | POST `/api/documents/{id}/analyze`, `UploadFinalizationWorker` |
| `EmailAnalysisJobHandler` | `Services/Jobs/Handlers/EmailAnalysisJobHandler.cs` | `"EmailAnalysis"` | `"Email Analysis"` | POST `/api/v1/emails/webhook-trigger` |
| `ProfileSummaryJobHandler` | `Services/Jobs/Handlers/ProfileSummaryJobHandler.cs` | `"ProfileSummary"` | Content-type mapped: Email→"Email Analysis", Doc→"Document Profile" | Profile summary generation |

**Non-playbook handlers** (unaffected):

| Handler | JobType | Purpose |
|---------|---------|---------|
| `RagIndexingJobHandler` | `"RagIndexing"` | Knowledge base vector indexing |
| `BulkRagIndexingJobHandler` | `"BulkRagIndexing"` | Batch vector indexing |
| `EmailToDocumentJobHandler` | `"EmailToDocument"` | Email→SPE document conversion |
| `IncomingCommunicationJobHandler` | `"IncomingCommunication"` | Email routing |
| `DocumentProcessingJobHandler` | `"DocumentProcessing"` | Document lifecycle |
| `InvoiceExtractionJobHandler` | `"InvoiceExtraction"` | Financial document extraction |
| `InvoiceIndexingJobHandler` | `"InvoiceIndexing"` | Financial index updates |
| `SpendSnapshotGenerationJobHandler` | `"SpendSnapshotGeneration"` | Finance snapshots |
| `AttachmentClassificationJobHandler` | `"AttachmentClassification"` | Attachment routing |
| `BatchProcessEmailsJobHandler` | `"BatchProcessEmails"` | Bulk email processing |

### 3.2 Execution Engine (Just Wired)

The `PlaybookOrchestrationService` now supports two execution paths:

| Method | Auth Mode | Use Case |
|--------|-----------|----------|
| `ExecuteAsync(request, httpContext, ct)` | OBO (user token) | Interactive — Analysis Workspace, Run button |
| `ExecuteAppOnlyAsync(request, tenantId, ct)` | App-only (client credentials) | Background — Service Bus jobs, webhooks |

The `ExecuteAppOnlyAsync` method was added in the current sprint specifically to enable this trigger infrastructure. It:
- Creates `PlaybookRunContext` without `HttpContext`
- Only supports node-based playbooks (emits `RunFailed` if no nodes)
- Reuses the same `ExecuteNodeBasedModeAsync` engine as interactive execution
- Streams `PlaybookStreamEvent` for progress tracking

### 3.3 Current Trigger Points (Enqueue Sites)

| Location | What It Does | Current Playbook |
|----------|-------------|------------------|
| `DocumentOperationsEndpoints.cs` POST `/api/documents/{id}/analyze` | Manual analysis trigger | `"Document Profile"` |
| `UploadFinalizationWorker.cs` (line ~923) | Auto-analyze after upload | `"Document Profile"` |
| `UploadFinalizationWorker.cs` (line ~1237) | Auto-analyze after upload | `"Document Profile"` |
| `EmailWebhookEndpoints.cs` POST `/api/v1/emails/webhook-trigger` | Email receipt webhook | `"Email Analysis"` |
| `ProfileSummaryWorker.cs` (line ~165) | Profile regeneration | Content-type mapped |

---

## 4. Future State Design

### 4.1 Architecture Overview

```
┌──────────────────────────┐     ┌──────────────────────────────────────────────┐
│ Trigger Source            │     │ Service Bus Job Queue                        │
│ (Upload, Email, Ribbon,   │────▶│ JobType = "PlaybookTrigger"                  │
│  Matter Create, SprkChat) │     │ Payload = { TriggerEvent, EntityId, Params } │
└──────────────────────────┘     └──────────────────┬───────────────────────────┘
                                                    │
                                                    ▼
                                      ┌─────────────────────────────┐
                                      │ PlaybookTriggerJobHandler    │
                                      │ (SINGLE generic handler)     │
                                      │                             │
                                      │ 1. Resolve matching triggers│
                                      │ 2. For each trigger:        │
                                      │    → ExecuteAppOnlyAsync()  │
                                      └──────────────┬──────────────┘
                                                     │
                                      ┌──────────────┼──────────────┐
                                      ▼              ▼              ▼
                             ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
                             │ Playbook A   │ │ Playbook B   │ │ Playbook C   │
                             │ (by config)  │ │ (by config)  │ │ (by config)  │
                             └──────────────┘ └──────────────┘ └──────────────┘
```

### 4.2 Dataverse Entity: `sprk_playbooktrigger`

**Display Name**: Playbook Trigger
**Schema Name**: `sprk_playbooktrigger`
**Ownership**: Organization-owned

#### Fields

| Display Name | Schema Name | Type | Required | Description |
|-------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Single Line (200) | Yes | Human-readable trigger name (e.g., "Profile on Upload", "Email Analysis on Receipt") |
| Playbook | `sprk_playbookid` | Lookup (`sprk_aiplaybook`) | Yes | The playbook to execute when this trigger fires |
| Trigger Event | `sprk_triggerevent` | Choice | Yes | The event that fires this trigger (see option set below) |
| Entity Logical Name | `sprk_entitylogicalname` | Single Line (100) | No | Dataverse entity this trigger applies to (e.g., `sprk_document`, `email`) |
| Filter Expression | `sprk_filterexpression` | Multi Line (4000) | No | JSON filter for conditional execution (see Filter Expressions below) |
| Is Active | `sprk_isactive` | Boolean | Yes | Enable/disable without deleting. Default: `true` |
| Execution Mode | `sprk_executionmode` | Choice | Yes | `Async` (Service Bus job) or `Inline` (synchronous in request) |
| Priority | `sprk_priority` | Integer | No | Execution order when multiple triggers match. Lower = first. Default: `100` |
| Parameters | `sprk_parameters` | Multi Line (4000) | No | JSON key-value parameters passed to playbook (template substitution) |
| Description | `sprk_description` | Multi Line (2000) | No | Admin notes about this trigger |

#### Trigger Event Option Set (`sprk_triggerevent_options`)

| Value | Label | Description |
|-------|-------|-------------|
| `100` | Document Upload Complete | Fires after document upload finalization (SPE file stored + Dataverse record created) |
| `200` | Email Received | Fires when incoming email is processed by email-to-document automation |
| `300` | Matter Created | Fires on new `sprk_matter` record creation |
| `400` | Document Status Changed | Fires when `sprk_document.statuscode` changes |
| `500` | Manual Trigger | Fires from explicit API call (ribbon button, SprkChat command) |
| `600` | Schedule | Fires on a schedule (future — requires timer infrastructure) |
| `700` | Chat Command | Fires from SprkChat pane command (e.g., "analyze this document") |

#### Relationships

| Type | Related Entity | Schema Name | Purpose |
|------|---------------|-------------|---------|
| N:1 | `sprk_aiplaybook` | `sprk_playbooktrigger_playbookid` | Which playbook to execute |

### 4.3 Filter Expressions

Filter expressions allow conditional trigger execution. Stored as JSON in `sprk_filterexpression`.

#### Schema

```json
{
  "conditions": [
    {
      "field": "sprk_documentcategory",
      "operator": "eq",
      "value": "Contract"
    },
    {
      "field": "sprk_fileextension",
      "operator": "in",
      "values": [".pdf", ".docx", ".doc"]
    }
  ],
  "logic": "and"
}
```

#### Supported Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `eq` | Equals | `{ "field": "statuscode", "operator": "eq", "value": 1 }` |
| `ne` | Not equals | `{ "field": "sprk_category", "operator": "ne", "value": "Draft" }` |
| `in` | In list | `{ "field": "sprk_type", "operator": "in", "values": ["Contract", "Invoice"] }` |
| `contains` | String contains | `{ "field": "sprk_name", "operator": "contains", "value": "NDA" }` |
| `startswith` | String starts with | `{ "field": "sprk_filename", "operator": "startswith", "value": "INV-" }` |
| `exists` | Field is not null | `{ "field": "sprk_matterid", "operator": "exists" }` |

#### Logic Operators

- `"and"` (default) — All conditions must match
- `"or"` — Any condition must match

### 4.4 BFF API Services

#### `PlaybookTriggerService`

**File**: `Services/Ai/PlaybookTriggerService.cs`
**DI Lifetime**: Scoped

```csharp
public interface IPlaybookTriggerService
{
    /// <summary>
    /// Find all active triggers matching an event + optional entity filter.
    /// Returns triggers ordered by Priority (ascending).
    /// </summary>
    Task<IReadOnlyList<PlaybookTriggerConfig>> GetMatchingTriggersAsync(
        TriggerEvent triggerEvent,
        string? entityLogicalName = null,
        IDictionary<string, object>? entityAttributes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a trigger — submits a Service Bus job for async execution,
    /// or calls PlaybookOrchestrationService directly for inline mode.
    /// </summary>
    Task<TriggerFireResult> FireTriggerAsync(
        PlaybookTriggerConfig trigger,
        Guid subjectEntityId,
        Guid[] documentIds,
        IDictionary<string, string>? additionalParameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience: find + fire all matching triggers for an event.
    /// Returns results for each trigger fired.
    /// </summary>
    Task<IReadOnlyList<TriggerFireResult>> FireMatchingTriggersAsync(
        TriggerEvent triggerEvent,
        string entityLogicalName,
        Guid subjectEntityId,
        Guid[] documentIds,
        IDictionary<string, object>? entityAttributes = null,
        IDictionary<string, string>? additionalParameters = null,
        CancellationToken cancellationToken = default);
}
```

**Implementation details**:

1. **`GetMatchingTriggersAsync`**: Queries Dataverse for `sprk_playbooktrigger` records where:
   - `sprk_triggerevent` matches the event
   - `sprk_entitylogicalname` matches (or is null = any entity)
   - `sprk_isactive` = true
   - Filter expression evaluates to true against provided entity attributes
   - Orders by `sprk_priority` ASC

2. **`FireTriggerAsync`**: Based on execution mode:
   - **Async**: Creates `JobContract` with `JobType = "PlaybookTrigger"`, enqueues via `JobSubmissionService`
   - **Inline**: Calls `PlaybookOrchestrationService.ExecuteAppOnlyAsync()` directly, consumes stream

3. **Caching**: Trigger configurations cached in Redis for 5 minutes (triggers rarely change). Cache key: `triggers:{tenantId}:{event}:{entity}`.

#### `PlaybookTriggerJobHandler`

**File**: `Services/Jobs/Handlers/PlaybookTriggerJobHandler.cs`
**DI Registration**: `services.AddScoped<IJobHandler, PlaybookTriggerJobHandler>()`

```csharp
public class PlaybookTriggerJobHandler : IJobHandler
{
    public const string JobTypeName = "PlaybookTrigger";
    public string JobType => JobTypeName;

    // Dependencies: IPlaybookOrchestrationService, INodeService,
    //               IIdempotencyService, ILogger

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        // 1. Deserialize PlaybookTriggerPayload from job.Payload
        // 2. Check idempotency (key: trigger-{triggerId}-{subjectId})
        // 3. Build PlaybookRunRequest with DocumentIds + Parameters
        // 4. Call _playbookOrchestrator.ExecuteAppOnlyAsync()
        // 5. Consume stream events, log progress
        // 6. Return Success/Failure based on RunCompleted/RunFailed event
    }
}
```

#### `PlaybookTriggerPayload`

```csharp
public record PlaybookTriggerPayload
{
    public required Guid TriggerId { get; init; }
    public required Guid PlaybookId { get; init; }
    public required Guid SubjectEntityId { get; init; }
    public required Guid[] DocumentIds { get; init; }
    public string? TenantId { get; init; }
    public IDictionary<string, string>? Parameters { get; init; }
    public string? Source { get; init; }  // "Upload", "Email", "Manual", etc.
}
```

### 4.5 Migration Path

The migration is **backward-compatible** — existing handlers continue to work while triggers are adopted incrementally.

#### Phase 1: Infrastructure (This Project)

1. Create `sprk_playbooktrigger` entity in Dataverse
2. Implement `PlaybookTriggerService` and `PlaybookTriggerJobHandler`
3. Add admin endpoint: `GET /api/ai/triggers` (list), `POST /api/ai/triggers/fire` (manual fire)
4. Register DI services

#### Phase 2: Seed Default Triggers

Create Dataverse records matching current hardcoded behavior:

| Trigger Name | Event | Entity | Playbook | Priority |
|-------------|-------|--------|----------|----------|
| Document Profile on Upload | `DocumentUploadComplete` | `sprk_document` | "Document Profile" (by ID) | 100 |
| Email Analysis on Receipt | `EmailReceived` | `email` | "Email Analysis" (by ID) | 100 |

#### Phase 3: Wire Trigger Sources

Update each trigger source to call `PlaybookTriggerService.FireMatchingTriggersAsync()`:

| Source | Current Code | New Code |
|--------|-------------|----------|
| `UploadFinalizationWorker` | Enqueues `AppOnlyDocumentAnalysis` job with hardcoded playbook | Calls `_triggerService.FireMatchingTriggersAsync(TriggerEvent.DocumentUploadComplete, "sprk_document", docId, [docId])` |
| `EmailWebhookEndpoints` | Enqueues `EmailAnalysis` job | Calls `_triggerService.FireMatchingTriggersAsync(TriggerEvent.EmailReceived, "email", emailId, documentIds)` |
| `DocumentOperationsEndpoints` | Enqueues `AppOnlyDocumentAnalysis` | Calls `_triggerService.FireMatchingTriggersAsync(TriggerEvent.ManualTrigger, "sprk_document", docId, [docId])` |

#### Phase 4: Deprecate Legacy Handlers

Once all trigger sources use `PlaybookTriggerService`:

1. Mark `AppOnlyDocumentAnalysisJobHandler` and `EmailAnalysisJobHandler` as `[Obsolete]`
2. Keep them registered for in-flight Service Bus messages (drain period: 1 week)
3. Remove after drain period

#### Phase 5: New Trigger Events

Add new capabilities without code changes:

| New Trigger | Enabled By |
|------------|-----------|
| "Run Classification playbook on Matter create" | Admin creates trigger record with `TriggerEvent = MatterCreated` |
| "Run NDA Review playbook for contracts" | Admin creates trigger with filter: `sprk_documentcategory eq 'Contract'` |
| "Run Summary playbook from SprkChat" | Admin creates trigger with `TriggerEvent = ChatCommand` |

---

## 5. Data Flow: End-to-End Example

### Document Upload → Trigger → Node-Based Playbook

```
1. User uploads document via SPE
   │
2. UploadFinalizationWorker completes SPE storage
   │
3. Worker calls: triggerService.FireMatchingTriggersAsync(
   │    TriggerEvent.DocumentUploadComplete,
   │    "sprk_document",
   │    documentId,
   │    [documentId],
   │    entityAttributes: { category: "Contract", extension: ".pdf" })
   │
4. PlaybookTriggerService queries Dataverse:
   │  SELECT * FROM sprk_playbooktrigger
   │  WHERE sprk_triggerevent = 100 (DocumentUploadComplete)
   │    AND sprk_entitylogicalname = 'sprk_document'
   │    AND sprk_isactive = true
   │  ORDER BY sprk_priority
   │
5. Returns 2 matching triggers:
   │  ├─ Trigger A: "Document Profile" playbook (priority 100)
   │  └─ Trigger B: "Contract Review" playbook (priority 200, filter: category=Contract)
   │
6. For each trigger:
   │  ├─ Evaluate filter expression against entity attributes
   │  │  ├─ Trigger A: No filter → MATCH
   │  │  └─ Trigger B: category eq "Contract" → MATCH (it's a contract)
   │  │
   │  └─ Fire trigger (async mode):
   │     └─ Enqueue JobContract { JobType: "PlaybookTrigger", Payload: { TriggerId, PlaybookId, ... } }
   │
7. ServiceBusJobProcessor picks up each job
   │  └─ PlaybookTriggerJobHandler.ProcessAsync()
   │     └─ PlaybookOrchestrationService.ExecuteAppOnlyAsync()
   │        └─ ExecuteNodeBasedModeAsync (topological sort → run nodes)
   │           ├─ Node 1: Extract text (Document Intelligence)
   │           ├─ Node 2: Analyze with AI (Azure OpenAI)
   │           └─ Node 3: Deliver Output (write to Dataverse / Working Document)
   │
8. Results stored per playbook's Deliver Output configuration
```

---

## 6. API Endpoints

### Trigger Management (Admin)

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/ai/triggers` | List all triggers (with optional event/entity filters) |
| `GET` | `/api/ai/triggers/{id}` | Get trigger details |
| `POST` | `/api/ai/triggers` | Create trigger (admin only) |
| `PUT` | `/api/ai/triggers/{id}` | Update trigger |
| `DELETE` | `/api/ai/triggers/{id}` | Delete trigger |
| `POST` | `/api/ai/triggers/{id}/toggle` | Enable/disable trigger |

### Trigger Execution (System)

| Method | Path | Purpose |
|--------|------|---------|
| `POST` | `/api/ai/triggers/fire` | Manually fire triggers for an event (testing/manual invocation) |
| `GET` | `/api/ai/triggers/match?event={event}&entity={entity}` | Preview which triggers would match (dry run) |

---

## 7. Configuration & DI

### New DI Registrations

```csharp
// In Program.cs or AI DI module
services.AddScoped<IPlaybookTriggerService, PlaybookTriggerService>();
services.AddScoped<IJobHandler, PlaybookTriggerJobHandler>();
```

### Trigger Event Enum

```csharp
public enum TriggerEvent
{
    DocumentUploadComplete = 100,
    EmailReceived = 200,
    MatterCreated = 300,
    DocumentStatusChanged = 400,
    ManualTrigger = 500,
    Schedule = 600,
    ChatCommand = 700
}
```

---

## 8. Observability

### Telemetry Events

| Event | When | Properties |
|-------|------|------------|
| `PlaybookTriggerMatched` | Trigger found for event | TriggerId, PlaybookId, Event, Entity |
| `PlaybookTriggerFired` | Job enqueued or inline execution started | TriggerId, PlaybookId, SubjectId, ExecutionMode |
| `PlaybookTriggerCompleted` | Playbook execution finished | TriggerId, PlaybookId, Duration, Success |
| `PlaybookTriggerFilterSkipped` | Trigger matched event but filter excluded it | TriggerId, FilterExpression, EntityAttributes |
| `PlaybookTriggerNoMatch` | No triggers found for event | Event, Entity |

### Structured Logging

```
[INF] Trigger resolution: Event=DocumentUploadComplete, Entity=sprk_document → 2 triggers matched
[INF] Firing trigger 'Document Profile on Upload' (Id={TriggerId}) → Playbook={PlaybookId}, Mode=Async
[INF] Trigger 'Contract Review' skipped: filter expression not matched (category=Report, expected=Contract)
```

---

## 9. Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Multiple triggers fire for same document** | Redundant AI processing, increased Azure OpenAI cost | Idempotency per trigger+subject pair; admin configures non-overlapping filters |
| **Trigger misconfiguration** | Wrong playbook runs on wrong event | Dry-run endpoint (`/match`), trigger audit log, IsActive toggle for safe testing |
| **Dataverse query latency** | Trigger resolution adds latency to upload flow | Redis cache (5-min TTL) for trigger configs; async execution mode |
| **Breaking existing flows during migration** | Document Profile or Email Analysis stops working | Phased migration: legacy handlers stay active until triggers proven; feature flag |
| **Filter expression complexity** | Complex JSON filters hard to author/debug | Start with simple operators; admin UI in future project; `/match` dry-run for testing |
| **Cascade failures** | One playbook failure should not block others | Each trigger fires independently; isolated error handling per trigger |

---

## 10. Dependencies

| Dependency | Type | Status |
|-----------|------|--------|
| `PlaybookOrchestrationService.ExecuteAppOnlyAsync()` | Code (BFF API) | Done (wired this sprint) |
| `PlaybookRunContext` app-only constructor | Code (BFF API) | Done (wired this sprint) |
| `INodeService.GetNodesAsync()` | Code (BFF API) | Existing |
| `JobSubmissionService` | Code (BFF API) | Existing |
| `ServiceBusJobProcessor` | Code (BFF API) | Existing |
| `IIdempotencyService` | Code (BFF API) | Existing |
| `sprk_playbooktrigger` entity | Dataverse schema | New — must be created |
| `sprk_aiplaybook` entity | Dataverse schema | Existing |

---

## 11. Scope Boundaries

### In Scope

- `sprk_playbooktrigger` Dataverse entity creation
- `PlaybookTriggerService` implementation
- `PlaybookTriggerJobHandler` implementation
- Admin API endpoints for trigger CRUD
- Migration of `AppOnlyDocumentAnalysisJobHandler` and `EmailAnalysisJobHandler` to trigger-based routing
- Seeding default trigger records
- Telemetry and structured logging

### Out of Scope (Future Projects)

- Admin UI for trigger management (use Dataverse model-driven form for now)
- Schedule-based triggers (requires timer/cron infrastructure)
- Complex filter expressions (nested logic, regex)
- Trigger versioning or audit history
- Multi-tenant trigger isolation (current: org-owned, shared)
- SprkChat integration (trigger source wiring — separate project)

---

## 12. Success Criteria

| Criteria | Verification |
|----------|-------------|
| Document upload triggers "Document Profile" playbook via trigger config (not hardcoded handler) | Upload document → verify playbook runs via `sprk_playbooktrigger` routing |
| Email receipt triggers "Email Analysis" playbook via trigger config | Receive email → verify playbook runs via trigger routing |
| Admin can add new trigger without code deployment | Create `sprk_playbooktrigger` record → verify it fires on matching event |
| Admin can disable trigger without code change | Set `sprk_isactive = false` → verify trigger stops firing |
| Filter expressions correctly include/exclude | Create trigger with filter → upload matching + non-matching docs → verify selective execution |
| Legacy handlers still work during transition | Both old and new paths active simultaneously until migration complete |
| No regression in Document Profile or Email Analysis quality | Compare outputs before/after migration |

---

## 13. Estimated Complexity

| Component | Effort | Notes |
|-----------|--------|-------|
| Dataverse entity + solution | Small | Standard entity creation, ~10 fields |
| `PlaybookTriggerService` | Medium | Query + filter evaluation + caching |
| `PlaybookTriggerJobHandler` | Small | Follows existing handler pattern exactly |
| Admin API endpoints | Small | Standard CRUD, ~6 endpoints |
| Trigger source wiring | Medium | Touch 3-4 existing files (upload worker, email webhook, document ops) |
| Legacy handler deprecation | Small | Mark obsolete, remove after drain |
| Testing | Medium | Integration tests for trigger resolution + filter evaluation |

---

## 14. Reference

- **ADR-001**: Minimal API + BackgroundService (no Azure Functions)
- **ADR-004**: Job contract patterns and idempotency
- **ADR-013**: AI Architecture (extend BFF, not separate service)
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md`: AI Tool Framework reference
- `Services/Jobs/IJobHandler.cs`: Job handler interface
- `Services/Ai/IPlaybookOrchestrationService.cs`: Orchestration interface (including `ExecuteAppOnlyAsync`)
- `Services/Ai/PlaybookRunContext.cs`: Run context with app-only constructor
