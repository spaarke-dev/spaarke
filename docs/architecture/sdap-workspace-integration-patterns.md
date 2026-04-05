# SDAP Workspace Integration Patterns

> **Last Updated**: April 5, 2026
> **Purpose**: Architecture of entity creation workflows, document operations, and background analysis in the Corporate Workspace

---

## Overview

The Corporate Workspace (LegalWorkspace SPA at `src/solutions/LegalWorkspace/`) introduced several architectural patterns for entity creation and document lifecycle management. The central pattern is an **entity-agnostic creation service** that handles SPE file upload, Dataverse record creation, and AI analysis triggering for any entity type (Matter, Project, Event, etc.) without duplication. These patterns extend the existing "SPE First, Dataverse Second" flow and the Job Contract (Service Bus) infrastructure to workspace-driven entity creation.

The key design decisions were: (1) parameterize the creation service by entity name and navigation property rather than duplicating per entity; (2) make AI Document Profile analysis fire-and-forget (returns 202 Accepted) so document creation is not blocked by analysis; (3) use app-only auth for background analysis because the user's OBO session may have ended; (4) discover OData navigation properties at runtime via Dataverse metadata API because PascalCase names cannot be reliably hardcoded.

---

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| EntityCreationService | `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts` | Generic upload → record creation → analysis trigger (entity-agnostic) |
| EntityCreationService re-export | `src/solutions/LegalWorkspace/src/services/EntityCreationService.ts` | Backward-compatible re-export from shared library |
| DocumentOperationsEndpoints | `src/server/api/Sprk.Bff.Api/Api/DocumentOperationsEndpoints.cs` | Checkout, checkin, delete, analyze endpoints |
| WorkspaceMatterEndpoints | `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceMatterEndpoints.cs` | AI pre-fill for entity creation wizards |
| WorkspaceAuthorizationFilter | `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceAuthorizationFilter.cs` | Per-endpoint user identity extraction (ADR-008) |
| MatterPreFillService | `src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs` | File staging + playbook-based field extraction |
| AppOnlyDocumentAnalysisJobHandler | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/AppOnlyDocumentAnalysisJobHandler.cs` | Background AI profiling with app-only auth |
| xrmProvider | `src/solutions/LegalWorkspace/src/services/xrmProvider.ts` | Container ID resolution from Business Unit via Xrm.WebApi |

---

## Data Flow: Entity Creation

1. User opens creation wizard in Corporate Workspace SPA
2. Entity-specific service (e.g., `MatterService`) builds entity payload
3. `EntityCreationService.uploadFilesToSpe(containerId, files)` uploads files to SPE via BFF OBO endpoint
4. Entity-specific service creates the parent entity record via `Xrm.WebApi.createRecord`
5. `EntityCreationService.createDocumentRecords(entitySetName, entityId, navProp, files)` creates `sprk_document` records with OData binding to parent
6. `EntityCreationService._triggerDocumentAnalysis(documentIds)` calls `POST /api/documents/{id}/analyze` for each document
7. BFF submits `AppOnlyDocumentAnalysis` job to Service Bus — returns 202 Accepted
8. `ServiceBusJobProcessor` routes job to `AppOnlyDocumentAnalysisJobHandler`
9. Handler downloads file from SPE (app-only auth), runs "Document Profile" playbook, updates `sprk_document` fields

---

## Entity-Agnostic Creation Service Pattern

`EntityCreationService` handles shared infrastructure parameterized by entity name and navigation property. Entity-specific services build the entity payload and delegate infrastructure steps.

**Key methods**:

| Method | Purpose | Entity-Specific? |
|--------|---------|-----------------|
| `uploadFilesToSpe(containerId, files)` | Upload files to SPE via BFF OBO endpoint | No |
| `createEntityRecord(entityName, payload)` | Create parent entity in Dataverse | No (parameterized) |
| `createDocumentRecords(entitySetName, entityId, navProp, files)` | Create `sprk_document` records linked to parent | No (parameterized) |
| `_triggerDocumentAnalysis(documentIds)` | Queue AI Document Profile per document | No |

**To add a new entity type**: Create `{Entity}Service.ts` that builds the entity payload, calls `_discoverNavProps('{entityLogicalName}')` for lookup bindings, and delegates upload/document creation to `EntityCreationService`. No changes to `EntityCreationService` itself.

**Dependencies are injected** via constructor: `webApi` (Dataverse WebAPI interface), `authenticatedFetch` (BFF-authenticated fetch function), `bffBaseUrl` (BFF API base URL). No solution-specific imports inside the service.

---

## Document Operations Endpoints

`DocumentOperationsEndpoints.cs` provides document lifecycle management:

| Endpoint | Purpose | Response |
|---------|---------|----------|
| `POST /api/documents/{id}/checkout` | Lock document for editing | 200 |
| `POST /api/documents/{id}/checkin` | Unlock + optionally re-index via Service Bus | 200 |
| `POST /api/documents/{id}/discard` | Discard checkout | 200 |
| `DELETE /api/documents/{id}` | Delete from Dataverse + SPE | 204 |
| `GET /api/documents/{id}/checkout-status` | Query lock state | 200 |
| `POST /api/documents/{id}/analyze` | Fire-and-forget AI Document Profile | 202 Accepted |

The analyze endpoint submits `AppOnlyDocumentAnalysis` to Service Bus and returns `202 Accepted` immediately. The `Source` field in the payload (`MatterCreationWizard`, `EmailAttachment`, `BulkImport`, `Manual`) enables telemetry segmentation. An idempotency key (`analysis-{documentId}-documentprofile`) prevents duplicate processing.

---

## AppOnlyDocumentAnalysis Job

Background AI profiling using app-only auth. Routes to `AppOnlyDocumentAnalysisJobHandler` via `ServiceBusJobProcessor`.

**Why app-only auth**: Analysis is triggered after entity creation when the user's OBO session may have ended. The handler uses application credentials to access SPE files and Dataverse records.

**Fields updated on `sprk_document`**: `sprk_filesummary`, `sprk_filetldr`, `sprk_keywords`, `sprk_entities`, `sprk_classification`, `sprk_filesummarystatus` (set to Completed).

**Failure handling**:

| Outcome | Behavior |
|---------|----------|
| `Completed` | Document fields updated, status set to Completed |
| `Failed` (transient) | Retried up to 3 times, then dead-lettered |
| `Poisoned` (permanent) | Document not found or unsupported playbook — dead-letter immediately |

---

## OData Navigation Property Discovery

PascalCase navigation property names cannot be reliably hardcoded — they vary by entity and environment. Entity-specific services discover them at runtime:

```
GET /api/data/v9.0/EntityDefinitions(LogicalName='{entity}')/ManyToOneRelationships
    ?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName
```

Result is cached per entity for the session. If metadata discovery fails, falls back to lowercase column name (works for some fields, not all).

---

## Container Resolution for Workspace

The Corporate Workspace SPA runs as a Vite web resource (iframe) without direct access to `appsettings.json`. Container ID is resolved from the user's Business Unit via Xrm.WebApi:

```
User ID (Xrm global context) → systemuser record → businessunitid ($expand) → businessunit.sprk_containerid
```

The container ID is stored on the created entity record (`sprk_containerid`) and on each document record (`sprk_graphdriveid`, `sprk_containerid`) for BFF preview/download operations.

---

## Job Handler Registry

All Service Bus job types registered in `ServiceBusJobProcessor`:

| JobType | Handler | Purpose |
|---------|---------|---------|
| `AppOnlyDocumentAnalysis` | `AppOnlyDocumentAnalysisJobHandler` | AI Document Profile (background) |
| `EmailToDocument` | `EmailToDocumentJobHandler` | Email-to-document conversion |
| `RagIndexing` | `RagIndexingJobHandler` | RAG document indexing |
| `AttachmentClassification` | `AttachmentClassificationJobHandler` | Attachment invoice classification |
| `InvoiceExtraction` | `InvoiceExtractionJobHandler` | Invoice billing fact extraction |
| `InvoiceIndexing` | `InvoiceIndexingJobHandler` | Invoice search indexing |
| `SpendSnapshotGeneration` | `SpendSnapshotGenerationJobHandler` | Spend analytics aggregation |

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Entity-agnostic creation service | Parameterize by entity name + nav-prop | Eliminates duplication across Matter, Project, Event creation | — |
| Analyze endpoint returns 202 | Fire-and-forget via Service Bus | Non-blocking; document exists regardless of analysis outcome | — |
| App-only auth for analysis | Application credentials (not OBO) | User session may have ended after entity creation | — |
| Nav-prop discovery at runtime | Query `EntityDefinitions/ManyToOneRelationships` | PascalCase names can't be reliably hardcoded | — |
| Container from Business Unit | `systemuser → businessunitid → sprk_containerid` | SPA web resource has no access to `appsettings.json` | — |
| Per-endpoint auth filter | `WorkspaceAuthorizationFilter` | Follows ADR-008 endpoint-filter-over-global-middleware | ADR-008 |
| File staging via SpeFileStore | Facade for all SPE operations | No Graph SDK types leak above facade per ADR-007 | ADR-007 |
| AI via playbook orchestration | `IPlaybookOrchestrationService` | No direct OpenAI calls per ADR-013 | ADR-013 |

---

## Constraints

- **MUST** use `EntityCreationService` for shared upload/record-creation infrastructure — do not duplicate per entity
- **MUST** use `SpeFileStore` facade for file operations — no Graph SDK types leak above it (ADR-007)
- **MUST** apply `WorkspaceAuthorizationFilter` per-endpoint, not global middleware (ADR-008)
- **MUST** use `IPlaybookOrchestrationService` for AI operations — no direct OpenAI calls (ADR-013)
- **MUST** discover navigation property names at runtime via metadata API — do not hardcode PascalCase names
- **MUST NOT** assume user's OBO session is active for background analysis — use app-only auth

---

## Known Pitfalls

| Pitfall | Symptom | Resolution |
|---------|---------|------------|
| Hardcoded navigation property name | 400 error on `@odata.bind` record creation | Always discover nav-props at runtime via `_discoverNavProps()` — PascalCase varies by entity |
| Missing `sprk_containerid` on parent entity | Upload succeeds but document cannot be previewed | Ensure every entity supporting documents has `sprk_containerid` populated from Business Unit |
| Duplicate analysis jobs | Document Profile runs twice for same document | Idempotency key `analysis-{documentId}-documentprofile` prevents duplicates — do not change key format |
| OBO token expired during wizard flow | BFF returns 401 mid-creation | EntityCreationService should handle token refresh; long wizard flows may need re-auth |
| Missing Source field in analyze payload | Cannot segment telemetry by trigger origin | Always include `Source` field (`MatterCreationWizard`, `EmailAttachment`, `BulkImport`, `Manual`) |

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | BFF API | OBO upload, analyze endpoint, pre-fill | File staging and analysis triggering |
| Depends on | Dataverse WebAPI | `Xrm.WebApi.createRecord` | Entity and document record creation |
| Depends on | Service Bus | `AppOnlyDocumentAnalysis` job type | Background processing |
| Depends on | `@spaarke/ui-components` | `EntityCreationService` shared implementation | Extracted from LegalWorkspace |
| Consumed by | LegalWorkspace SPA | Creation wizards (Matter, Project, Event) | Entity-specific services delegate to EntityCreationService |
| Consumed by | DocumentUploadWizard Code Page | Document upload with analysis | Same shared upload services |

---

## Related

- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) — "SPE First, Dataverse Second" pattern, upload flows
- [sdap-component-interactions.md](sdap-component-interactions.md) — Cross-component impact table
- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) — Playbook orchestration pattern
- [`docs/guides/WORKSPACE-ENTITY-CREATION-GUIDE.md`](../guides/WORKSPACE-ENTITY-CREATION-GUIDE.md) — Entity creation procedures

---

*Last Updated: April 5, 2026*
