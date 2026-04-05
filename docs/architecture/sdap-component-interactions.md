# SDAP Component Interactions Architecture

> **Last Updated**: April 2026
> **Purpose**: Cross-module impact map for Spaarke — when you change component X, what else is affected.

---

## Overview

The Spaarke Data & AI Platform (SDAP) is a multi-layer system where changes to one component frequently cascade to others. This document maps those interactions so that modifications can be evaluated for downstream impact before implementation. The BFF API (`Sprk.Bff.Api`) is the central integration hub, connecting PCF controls, Code Pages, Office add-ins, background workers, and external services (Graph, Dataverse, Azure AI). Most cross-cutting impacts flow through the BFF.

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| BFF API entry point | `src/server/api/Sprk.Bff.Api/Program.cs` | DI registration, middleware, endpoint mapping |
| Endpoint groups | `src/server/api/Sprk.Bff.Api/Api/` | 120+ HTTP endpoints across 10+ domain groups |
| Endpoint filters | `src/server/api/Sprk.Bff.Api/Api/Filters/` | 20 resource-authorization filters (ADR-008) |
| Service layer | `src/server/api/Sprk.Bff.Api/Services/` | Business logic: AI, Email, Finance, Jobs, Workspace |
| Graph infrastructure | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/` | SpeFileStore facade, GraphClientFactory, OBO exchange |
| Background workers | `src/server/api/Sprk.Bff.Api/Workers/Office/` | UploadFinalizationWorker, ProfileSummaryWorker, IndexingWorker |
| Job handlers | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/` | 11 async job handlers via Service Bus |
| DI modules | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` | 26 modular registration files |
| Shared libraries | `src/server/shared/Spaarke.Core/`, `src/server/shared/Spaarke.Dataverse/` | Cross-cutting utilities, Dataverse client |
| PCF controls | `src/client/pcf/` | 15 field-bound controls (React 16/17) |
| Code Pages | `src/solutions/`, `src/client/code-pages/` | 20 standalone React 18 SPAs |
| Shared UI components | `src/client/shared/Spaarke.UI.Components/` | Reusable React component library |

---

## Cross-Module Impact Map

When modifying a component, check this table for potential downstream effects:

| If You Change... | Check Impact On... |
|------------------|-------------------|
| BFF API endpoint signature | PCF controls, Code Pages, Office add-ins, M365 Copilot Agent, tests |
| BFF authentication / OBO | PCF auth config, Office add-in auth, Code Page auth bootstrap, Copilot Agent token service |
| PCF control API calls | BFF endpoint contracts, shared UI component interfaces |
| Dataverse entity schema | BFF Dataverse queries, PCF form bindings, Office workers, email processing |
| Shared .NET libraries (`Spaarke.Core`, `Spaarke.Dataverse`) | All ProjectReference consumers — BFF, plugins, workers |
| Shared UI library (`@spaarke/ui-components`) | All PCF controls and Code Pages that import from it |
| `SpeFileStore` facade | All document endpoints, upload flows, AI analysis, RAG indexing |
| `GraphClientFactory` | OBO flow (user-initiated), app-only flow (jobs), all Graph-dependent services |
| Endpoint filters | All endpoints using that filter — search for `.AddEndpointFilter<{FilterName}>()` |
| DI module registration | All services resolved from that module — check the `Add{Module}()` method |
| Email processing options | Webhook handler, polling service, EmailToDocumentJobHandler |
| Email filter rules schema | EmailFilterService, EmailRuleSeedService, Dataverse entity |
| Webhook endpoint URL | Dataverse Service Endpoint registration |
| Office add-in entity models | UploadFinalizationWorker, IDataverseService, Dataverse schema |
| ProcessingJob schema | UploadFinalizationWorker, Office add-in tracking, ProfileSummaryWorker |
| `IDataverseService` interface | Both implementations (DataverseServiceClientImpl, DataverseWebApiService) |
| Service Bus queue names | appsettings.json, corresponding worker/handler configuration |
| AI playbook schema / names | PlaybookService, ScopeResolverService, AnalysisOrchestrationService, Chat system |
| RAG index schema | FileIndexingService, RagService, SemanticSearch endpoints, AI Search index definition |
| `IFileIndexingService` interface | Sync path (OBO), async path (RagIndexingJobHandler), Office IndexingWorker |
| Export service interface | ExportServiceRegistry, DocxExportService, PdfExportService, EmailExportService |

---

## Data Flow: Document Upload

**Primary (Code Page wizard):**
- User → ribbon button → DocumentUploadWizard Code Page → BFF → Graph → SPE → Xrm.WebApi → Dataverse → Document Profile playbook → RAG indexing

**Legacy (PCF form-embedded):**
- User → UniversalQuickCreate PCF → BFF → Graph → SPE + Dataverse (same SPE-first ordering)

**Components involved:**

| Component | Path | Role |
|-----------|------|------|
| DocumentUploadWizard | `src/solutions/DocumentUploadWizard/` | Code Page wizard (preferred path) |
| UniversalQuickCreate | `src/client/pcf/UniversalQuickCreate/` | PCF upload (form-embedded, legacy) |
| Shared upload services | `src/client/shared/` | MultiFileUploadService, DocumentRecordService, useAiSummary |
| Upload endpoints | `src/server/api/Sprk.Bff.Api/Api/UploadEndpoints.cs` | BFF upload API |
| Upload session manager | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs` | Chunked uploads to SPE |

**Change impact:**

| Change | Impact |
|--------|--------|
| Modify upload endpoint signature | Update shared upload services (used by both Code Page and PCF) |
| Change file size limits | Update both BFF config and shared UI messaging |
| Modify `sprk_document` field names | Update BFF field mapping, PCF form bindings, Office workers |

---

## Data Flow: Authentication (OBO)

Sequence: Dataverse Session → PCF/Code Page (MSAL.js) → BFF API (validate JWT) → OBO Exchange → Graph/Dataverse.

**Components involved:**

| Component | Path | Role |
|-----------|------|------|
| PCF MSAL config | `src/client/pcf/*/services/auth/msalConfig.ts` | Token acquisition config |
| Code Page auth | `src/client/shared/` (`@spaarke/auth`) | Bootstrap: resolveRuntimeConfig → ensureAuthInitialized |
| JWT validation | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs` | Auth middleware registration |
| Graph client factory | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` | OBO exchange + app-only |

**Change impact:**

| Change | Impact |
|--------|--------|
| Modify API scopes | Update PCF msalConfig AND Entra app registration |
| Change token validation | All PCF controls and Code Pages affected |
| Add new authorization policy | Update endpoint filter decorators |

---

## Data Flow: AI Analysis

Sequence: User → PCF (AnalysisWorkspace) or Chat → BFF (AnalysisEndpoints/ChatEndpoints) → AnalysisOrchestrationService → ScopeResolverService → AnalysisContextBuilder → Azure OpenAI (streaming SSE).

**Components involved:**

| Component | Path | Role |
|-----------|------|------|
| AnalysisWorkspace PCF | `src/client/code-pages/AnalysisWorkspace/` | Analysis UI with SSE streaming |
| Chat endpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | Session + message + playbook discovery |
| Analysis orchestration | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | Core orchestration |
| Context builder | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs` | Prompt construction |
| Scope resolver | `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` | Playbook/skill resolution |
| Export services | `src/server/api/Sprk.Bff.Api/Services/Ai/Export/` | DOCX, PDF, Email export |

**Change impact:**

| Change | Impact |
|--------|--------|
| Modify analysis endpoint signature | Update AnalysisWorkspace API client and Chat system |
| Change prompt structure | Update AnalysisContextBuilder methods |
| Add new export format | Create new IExportService, register in ExportServiceRegistry |
| Modify playbook resolution | Update ScopeResolverService and Dataverse playbook entities |
| Change chat session schema | Update ChatSessionManager, ChatEndpoints, PCF chat component |

---

## Data Flow: Email-to-Document Conversion

See [email-to-document-automation.md](email-to-document-automation.md) for full design.

Dual entry: Webhook (real-time) + EmailPollingBackupService (5-min backup) → EmailToDocumentJobHandler → SPE upload + Dataverse record + AI analysis enqueue.

**Components involved:**

| Component | Path | Role |
|-----------|------|------|
| Email endpoints | `src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs` | Webhook receiver |
| EML converter | `src/server/api/Sprk.Bff.Api/Services/Email/EmailToEmlConverter.cs` | RFC 5322 .eml via MimeKit |
| Attachment filter | `src/server/api/Sprk.Bff.Api/Services/Email/AttachmentFilterService.cs` | Filter noise attachments |
| Job handler | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/` | EmailAnalysisJobHandler |

**Change impact:**

| Change | Impact |
|--------|--------|
| Modify webhook endpoint URL | Update Dataverse Service Endpoint registration |
| Change job payload schema | Update webhook handler AND job handler |
| Modify filter rule schema | Update EmailFilterService, seed service, Dataverse entity |
| Change default container | Update EmailProcessingOptions configuration |
| Modify attachment parent-child link | `sprk_email` must NOT be set on child documents (alternate key constraint) |
| Change AI playbook name | Update `EnqueueAiAnalysisJobAsync` constant |

---

## Data Flow: Office Add-in Processing

Multi-stage Service Bus pipeline: OfficeEndpoints → UploadFinalizationWorker → ProfileSummaryWorker + IndexingWorkerHostedService (parallel).

**Service Bus queues:**

| Queue | Consumer | Next Stage |
|-------|----------|------------|
| `office-upload-finalization` | UploadFinalizationWorker | office-profile + office-indexing |
| `office-profile` | ProfileSummaryWorker | Terminal |
| `office-indexing` | IndexingWorkerHostedService | Terminal |

**Components involved:**

| Component | Path | Role |
|-----------|------|------|
| Office endpoints | `src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs` | Upload API for add-ins |
| Upload finalization | `src/server/api/Sprk.Bff.Api/Workers/Office/UploadFinalizationWorker.cs` | File move, record creation |
| Profile worker | `src/server/api/Sprk.Bff.Api/Workers/Office/ProfileSummaryWorker.cs` | AI profile via IAppOnlyAnalysisService |
| Indexing worker | `src/server/api/Sprk.Bff.Api/Workers/Office/IndexingWorkerHostedService.cs` | RAG indexing via IFileIndexingService |
| Dataverse service | `src/server/shared/Spaarke.Dataverse/` | IDataverseService CRUD for Office entities |

**Change impact:**

| Change | Impact |
|--------|--------|
| Modify OfficeEndpoints signature | Update Office add-in TypeScript API client |
| Change ProcessingJob schema | Update Models, Dataverse table, worker logic |
| Change IDataverseService interface | Update DataverseServiceClientImpl, DataverseWebApiService |
| Add Service Bus queue | Update appsettings.json, add consumer worker |

---

## Data Flow: AI Authorization (OBO for Dataverse)

Sequence: PCF acquires token → BFF `AnalysisAuthorizationFilter` → `AiAuthorizationService` → `DataverseAccessDataSource` OBO exchange → Dataverse direct query → authorization decision.

**Two critical bugs fixed in this flow:**
1. OBO token obtained but never set on HttpClient → 401. Fix: `_httpClient.DefaultRequestHeaders.Authorization = Bearer {oboToken}` immediately after MSAL exchange.
2. `RetrievePrincipalAccess` returns 404 with OBO (delegated) tokens. Fix: Direct query `GET /sprk_documents({id})?$select=sprk_documentid` — success = Read access granted.

**Components involved:**

| Component | Path | Role |
|-----------|------|------|
| AnalysisAuthorizationFilter | `src/server/api/Sprk.Bff.Api/Api/Filters/AnalysisAuthorizationFilter.cs` | Extract documentIds, trigger auth |
| AiAuthorizationService | `src/server/api/Sprk.Bff.Api/Services/Ai/AiAuthorizationService.cs` | Orchestrate OBO auth check |
| DataverseAccessDataSource | `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` | MSAL OBO exchange, direct query |

**Change impact:**

| Change | Impact |
|--------|--------|
| Modify `IAiAuthorizationService` signature | Update all authorization filters that depend on it |
| Change OBO token acquisition | Update MSAL configuration, test all AI operations |
| Add new `IAccessDataSource` | Implement interface, register in DI |

---

## Data Flow: RAG File Indexing (Dual Entry Points)

Two indexing paths with different auth patterns:

| Entry Point | Auth | Use Case |
|-------------|------|---------|
| `POST /api/ai/rag/index-file` | OBO (user token) | User-initiated via PCF |
| `POST /api/ai/rag/enqueue-indexing` | X-Api-Key header | Background jobs, bulk ops, scripts |

The async path submits to Service Bus; `RagIndexingJobHandler` processes using app-only auth (`ForApp()`). The synchronous path uses OBO (`ForUserAsync(ctx)`). Both ultimately call `FileIndexingService`.

**Change impact:**

| Change | Impact |
|--------|--------|
| Modify indexing entry point auth | Update both OBO path (filter) and API key path (validation) |
| Change `IFileIndexingService` interface | Update both sync handler and async RagIndexingJobHandler, plus Office IndexingWorker |
| Change RAG index schema | Update indexing services, search queries, Azure AI Search index definition |

---

## Data Flow: Document Relationship Visualization

Sequence: PCF (DocumentRelationshipViewer) → BFF (VisualizationEndpoints) → VisualizationService → IRagService vector search (3072-dim `documentVector3072`) → interactive React Flow graph.

**Components involved:**

| Component | Path | Role |
|-----------|------|------|
| DocumentRelationshipViewer PCF | `src/client/pcf/DocumentRelationshipViewer/` | React Flow canvas with d3-force layout |
| Visualization endpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/VisualizationEndpoints.cs` | GET /api/ai/visualization/related/{documentId} |
| Visualization service | `src/server/api/Sprk.Bff.Api/Services/Ai/Visualization/VisualizationService.cs` | Vector similarity via IRagService |

**Change impact:**

| Change | Impact |
|--------|--------|
| Modify VisualizationService vector search | Update test mocks, verify similarity calculations |
| Change graph data model | Update PCF TypeScript types, VisualizationApiService |
| Modify node/edge styling | Update DocumentNode/DocumentEdge components in PCF |

---

## Data Flow: M365 Copilot Agent

Sequence: Teams/M365 Copilot → AgentEndpoints → SpaarkeAgentHandler → PlaybookInvocationService → AnalysisOrchestrationService → Azure OpenAI → AdaptiveCardFormatterService → response.

**Components involved:**

| Component | Path | Role |
|-----------|------|------|
| Agent endpoints | `src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs` | Gateway for M365 Copilot |
| Agent handler | `src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs` | Conversation orchestration |
| Playbook invocation | `src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookInvocationService.cs` | Bridge to AI analysis |
| Adaptive cards | `src/server/api/Sprk.Bff.Api/Api/Agent/AdaptiveCardFormatterService.cs` | Format AI output for Teams |

**Change impact:**

| Change | Impact |
|--------|--------|
| Modify playbook invocation interface | Update AgentHandler AND ChatEndpoints (both consumers) |
| Change adaptive card schema | Update AdaptiveCardFormatterService, test in Teams |
| Modify agent auth | Update AgentTokenService and AgentAuthorizationFilter |

---

## Integration Points

| Direction | Subsystem A | Subsystem B | Interface | Notes |
|-----------|-------------|-------------|-----------|-------|
| BFF → Graph | BFF API | Microsoft Graph | `SpeFileStore`, `GraphClientFactory` | OBO for user ops, app-only for jobs |
| BFF → Dataverse | BFF API | Dataverse Web API | `DataverseAccessDataSource`, `IDataverseService` | OBO for auth checks, app-only for workers |
| BFF → AI Search | BFF API | Azure AI Search | `RagService`, `FileIndexingService` | Vector + keyword hybrid search |
| BFF → OpenAI | BFF API | Azure OpenAI | `OpenAiClient`, `AnalysisOrchestrationService` | Streaming SSE, Polly resilience |
| BFF → Service Bus | BFF API | Azure Service Bus | `JobSubmissionService`, `ServiceBusJobProcessor` | 13+ job types, dead letter handling |
| PCF → BFF | PCF Controls | BFF API | HTTP + MSAL OBO tokens | 15 controls with auth token acquisition |
| Code Pages → BFF | Code Page SPAs | BFF API | HTTP + `@spaarke/auth` bootstrap | 20 standalone React 18 apps |
| Shared UI → PCF/Pages | UI Components | PCF + Code Pages | `@spaarke/ui-components` npm package | DataGrid, WizardDialog, SidePanel, etc. |
| Shared .NET → BFF | Spaarke.Core, Spaarke.Dataverse | BFF API | ProjectReference | Cross-cutting utilities, Dataverse client |
| Office → BFF | Office Add-ins | BFF API | HTTP + OBO | Multi-stage Service Bus pipeline |
| Copilot → BFF | M365 Copilot Agent | BFF API | AgentEndpoints | Token service, adaptive cards |
| Jobs → BFF Services | Job Handlers | AI/Email/Finance Services | Direct service injection | App-only auth context |

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Central BFF hub | Single .NET 8 API serves all frontends | Avoid service proliferation; single auth boundary | ADR-001 |
| Endpoint filters for auth | Per-endpoint resource authorization | No global middleware; fine-grained control | ADR-008 |
| SpeFileStore facade | Wrap Graph SDK behind concrete facade | Prevent Graph type leakage above facade layer | ADR-007 |
| Dual auth strategies | OBO for user-initiated, app-only for jobs | Background workers have no user context | ADR-008 |
| Service Bus for async | All async processing via Azure Service Bus | Reliable delivery, dead-letter handling, idempotency | ADR-001 |

---

## Constraints

- **MUST** use `SpeFileStore` for all SPE operations — never inject `GraphServiceClient` directly (ADR-007)
- **MUST** use endpoint filters for resource authorization — no global auth middleware (ADR-008)
- **MUST** use `GraphClientFactory.ForApp()` for background workers and `ForUserAsync(ctx)` for user-initiated operations
- **MUST NOT** add HTTP/Graph calls to Dataverse plugins (ADR-002)
- **MUST** update both sync and async paths when changing `IFileIndexingService`
- **MUST** update all consumers when modifying shared library interfaces (`IDataverseService`, `@spaarke/ui-components`)

---

## Known Pitfalls

### 1. OBO Token Not Set on HttpClient

When performing OBO token exchange via MSAL, the resulting token must be explicitly set on the HttpClient `Authorization` header. Forgetting this step causes silent 401 errors against Dataverse. This was bug #1 in the AI authorization flow — see `DataverseAccessDataSource.cs`.

### 2. RetrievePrincipalAccess Does Not Support Delegated Tokens

The Dataverse `RetrievePrincipalAccess` API returns 404 when called with OBO (delegated) tokens. The workaround is a direct GET query on the target record — if the query succeeds, the user has Read access. This was bug #2 in the AI authorization flow.

### 3. `sprk_email` Must Not Be Set on Attachment Child Documents

The `sprk_email` field on `sprk_document` participates in an alternate key. Setting it on child attachment documents (which share the same email reference) causes duplicate key violations. Child documents must have `sprk_email = NULL`.

### 4. IFileIndexingService Has Three Consumers

Changes to `IFileIndexingService` affect three separate code paths: (a) synchronous OBO indexing via RagEndpoints, (b) async `RagIndexingJobHandler` via Service Bus, and (c) Office `IndexingWorkerHostedService`. Missing any consumer causes silent indexing failures.

### 5. Shared UI Library Version Sync

The `@spaarke/ui-components` library is consumed by both PCF controls (React 16/17) and Code Pages (React 18). Breaking changes must account for both React version contexts. PCF controls cannot use React 18 hooks or APIs.

### 6. Kiota Package Version Alignment

All Microsoft.Kiota packages in the BFF must be the same version. Microsoft.Graph pulls Kiota as transitive dependencies — updating only direct references causes `FileNotFoundException` at runtime. See `Sprk.Bff.Api/CLAUDE.md` Package Management section.

### 7. Service Bus Queue Name Changes Require Config + Code

Changing a Service Bus queue name requires updates in both `appsettings.json` configuration AND the corresponding worker/handler registration in the DI module. The queue name is resolved at startup — mismatches cause silent message loss.

### 8. Endpoint Filter Ordering Matters

Endpoint filters execute in registration order. Authorization filters must run before business logic filters (e.g., `IdempotencyFilter`). Adding a filter in the wrong position can cause authorization bypasses or duplicate processing.

### 9. Code Page Auth Bootstrap Ordering

Code Pages using `@spaarke/auth` must follow strict initialization order: `resolveRuntimeConfig()` → `setRuntimeConfig()` → `ensureAuthInitialized()` → render. Calling runtime config getters at module level (before bootstrap) throws because the config is not yet loaded.

### 10. DI Module Registration Dependencies

The 26 DI module files in `Infrastructure/DI/` have implicit ordering dependencies. For example, `AddAnalysisServicesModule` depends on services registered by `AddGraphModule`. Reordering `Add{Module}()` calls in `Program.cs` can cause DI resolution failures at startup.

---

## Related

- [sdap-auth-patterns.md](sdap-auth-patterns.md) — Auth pattern details (OBO bugs, dual strategies)
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) — BFF field mappings, container model, caching TTLs
- [email-to-document-automation.md](email-to-document-automation.md) — Email pipeline design decisions
- [uac-access-control.md](uac-access-control.md) — Three-plane access control model
- [office-outlook-teams-integration-architecture.md](office-outlook-teams-integration-architecture.md) — Office add-in architecture
- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) — AI tool framework and playbook architecture

---

*Last Updated: April 2026*
