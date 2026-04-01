# SDAP Workspace Integration Patterns

> **Last Updated:** February 20, 2026
> **Applies To:** Corporate Workspace SPA, BFF API endpoint patterns, entity creation flows

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Entity-agnostic creation service | Each entity type (Matter, Project) needs the same SPE upload → Dataverse record → AI analysis flow; parameterizing by entity name and nav-prop eliminates duplication |
| `POST /api/documents/{id}/analyze` returns 202 | Fire-and-forget AI Document Profile — analysis is non-blocking; document exists regardless of analysis outcome; idempotency key prevents duplicate processing |
| `AppOnlyDocumentAnalysisJobHandler` (new job type) | Analysis triggered after entity creation when user's OBO session may have ended — requires app-only auth, not OBO |
| Nav-prop discovery via Dataverse metadata API | PascalCase nav property names can't be reliably hardcoded — query `EntityDefinitions/ManyToOneRelationships` at runtime; cache per session |
| Container from Business Unit (not config) | SPA web resource has no access to `appsettings.json`; Xrm.WebApi walk: `systemuser → businessunitid → sprk_containerid` |

---

## Entity-Agnostic Creation Service Pattern

`EntityCreationService.ts` handles shared infrastructure (SPE upload, `sprk_document` creation, AI analysis trigger) parameterized by entity name and navigation property. Entity-specific services (e.g., `MatterService`) build the entity payload and delegate to `EntityCreationService` for infrastructure steps.

**To add a new entity type**: Create `{Entity}Service.ts` that builds the entity payload, calls `_discoverNavProps('{entityLogicalName}')` for lookup bindings, and delegates upload/document creation to `EntityCreationService`. No changes to `EntityCreationService` itself.

---

## Document Operations Endpoints

`DocumentOperationsEndpoints.cs` provides document lifecycle management:

| Endpoint | Purpose |
|---------|---------|
| `POST /api/documents/{id}/checkout` | Lock document for editing |
| `POST /api/documents/{id}/checkin` | Unlock + optionally re-index |
| `DELETE /api/documents/{id}` | Delete from Dataverse + SPE |
| `POST /api/documents/{id}/analyze` | Fire-and-forget AI Document Profile (202 Accepted) |

The analyze endpoint submits `AppOnlyDocumentAnalysis` to Service Bus and returns `202 Accepted` immediately. The `Source` field in the payload (`MatterCreationWizard`, `EmailAttachment`, `BulkImport`, `Manual`) enables telemetry segmentation.

---

## AppOnlyDocumentAnalysis Job

Background AI profiling using app-only auth. Routes to `AppOnlyDocumentAnalysisJobHandler` via `ServiceBusJobProcessor`.

**Failure handling**:

| Outcome | Behavior |
|---------|----------|
| `Completed` | Document fields updated, status → Completed |
| `Failed` (transient) | Retried up to 3 times, then dead-lettered |
| `Poisoned` (permanent) | Document not found, unsupported playbook → dead-letter immediately |

---

## Job Handler Registry

All Service Bus job types registered in `ServiceBusJobProcessor`:

| JobType | Purpose |
|---------|---------|
| `AppOnlyDocumentAnalysis` | AI Document Profile (background) |
| `EmailToDocument` | Email-to-document conversion |
| `RagIndexing` | RAG document indexing |
| `AttachmentClassification` | Attachment invoice classification |
| `InvoiceExtraction` | Invoice billing fact extraction |
| `InvoiceIndexing` | Invoice search indexing |
| `SpendSnapshotGeneration` | Spend analytics aggregation |

---

## ADR Compliance

| ADR | Pattern |
|-----|---------|
| ADR-007 | File staging uses `SpeFileStore` facade — no Graph SDK types leak |
| ADR-008 | `WorkspaceAuthorizationFilter` applied per-endpoint, not global middleware |
| ADR-010 | `EntityCreationService` is a concrete singleton; no unnecessary interfaces |
| ADR-013 | Pre-fill AI uses `IPlaybookOrchestrationService` — no direct OpenAI calls |

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) | "SPE First, Dataverse Second" pattern, upload flows |
| [sdap-component-interactions.md](sdap-component-interactions.md) | Cross-component impact table |
| [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) | Playbook orchestration pattern |

---

*Last Updated: February 20, 2026*
