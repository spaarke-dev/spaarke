# SDAP BFF API Architecture

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current
> **Purpose**: Architecture of the Sprk.Bff.Api — the unified .NET 8 Minimal API backend for the SDAP platform.

---

## Overview

Sprk.Bff.Api is a single .NET 8 Minimal API that serves as the Backend-for-Frontend (BFF) for the entire SDAP platform. It provides 120+ endpoints across 7 functional domains: SPE/Documents, AI Platform, Office Add-ins, Email/Communication, Finance Intelligence, Workspace/Portfolio, and Background Processing. The API uses a modular DI registration system where each domain is encapsulated in a startup module, and endpoints are registered through extension methods organized by domain.

The architecture is shaped by three key ADRs: ADR-001 (Minimal API + BackgroundService, no Azure Functions), ADR-008 (endpoint filters for authorization, no global middleware), and ADR-010 (DI minimalism with concrete types and feature modules).

## Component Structure

### DI Module System

All service registration is modular. `Program.cs` calls extension methods, each defined in a dedicated module file under `Infrastructure/DI/`.

| Module | Path | Responsibility |
|--------|------|---------------|
| `ConfigurationModule` | `Infrastructure/DI/ConfigurationModule.cs` | Options binding and validation |
| `SpaarkeCore` | `Infrastructure/DI/SpaarkeCore.cs` | AuthorizationService, AiAuthorizationService, RequestCache, IAccessDataSource |
| `AuthorizationModule` | `Infrastructure/DI/AuthorizationModule.cs` | Azure AD JWT validation, authorization policies |
| `ExternalAccessModule` | `Infrastructure/DI/ExternalAccessModule.cs` | Power Pages portal token validation, Contact participation |
| `DocumentsModule` | `Infrastructure/DI/DocumentsModule.cs` | SpeFileStore facade, GraphTokenCache, GraphMetadataCache, ContainerOperations, DriveItemOperations, UploadSessionManager |
| `GraphModule` | `Infrastructure/DI/GraphModule.cs` | GraphClientFactory, IDataverseService (composite + 9 narrow interfaces), DataverseWebApiService, resilience handler |
| `CacheModule` | `Infrastructure/DI/CacheModule.cs` | Redis or in-memory IDistributedCache (ADR-009) |
| `AnalysisServicesModule` | `Infrastructure/DI/AnalysisServicesModule.cs` | OpenAiClient, TextExtractor, DocumentIntelligence, playbook, builder, RAG, record matching |
| `JobProcessingModule` | `Infrastructure/DI/JobProcessingModule.cs` | JobSubmissionService, 6+ IJobHandler implementations, ServiceBusJobProcessor |
| `WorkersModule` | `Infrastructure/DI/WorkersModule.cs` | ServiceBusClient, Office workers, IdempotencyService, BatchJobStatusStore |
| `CommunicationModule` | `Infrastructure/DI/CommunicationModule.cs` | CommunicationService, EmlGenerationService, MailboxVerificationService |
| `EmailServicesModule` | `Infrastructure/DI/EmailServicesModule.cs` | Email-to-Document conversion services |
| `OfficeModule` | `Infrastructure/DI/OfficeModule.cs` | Office Add-in services |
| `FinanceModule` | `Infrastructure/DI/FinanceModule.cs` | Invoice classification, field extraction, financial aggregation |
| `WorkspaceModule` | `Infrastructure/DI/WorkspaceModule.cs` | Legal Operations Workspace services |
| `ReportingModule` | `Infrastructure/DI/ReportingModule.cs` | Power BI Embedded (ReportingEmbedService, ReportingProfileManager) |
| `RegistrationModule` | `Infrastructure/DI/RegistrationModule.cs` | Self-service demo access provisioning |
| `SpeAdminModule` | `Infrastructure/DI/SpeAdminModule.cs` | SPE environments, container configs, Graph service, audit logging, dashboard sync |
| `AgentModule` | `Infrastructure/DI/AgentModule.cs` | M365 Copilot Agent gateway, auth, cards, conversation, playbook invocation |
| `CorsModule` | `Infrastructure/DI/CorsModule.cs` | CORS configuration (fail-closed) |
| `RateLimitingModule` | `Infrastructure/DI/RateLimitingModule.cs` | Per-user/per-IP rate limiting |
| `TelemetryModule` | `Infrastructure/DI/TelemetryModule.cs` | OpenTelemetry, health checks, circuit breaker |
| `AiModule` | `Infrastructure/DI/AiModule.cs` | AI-specific registrations |

### Startup Pipeline

`Program.cs` orchestrates the full startup in three phases:

1. **Service registration** — calls each `Add*Module()` extension method on `IServiceCollection`
2. **Middleware pipeline** — `app.UseSpaarkeMiddleware()` (in `Infrastructure/DI/MiddlewarePipelineExtensions.cs`) sets up CORS, auth, rate limiting, security headers, static files
3. **Endpoint mapping** — `app.MapSpaarkeEndpoints()` (in `Infrastructure/DI/EndpointMappingExtensions.cs`) registers all endpoint groups

Startup diagnostics run via `app.RunStartupDiagnostics()` (in `Infrastructure/DI/StartupDiagnostics.cs`) between build and middleware.

### Endpoint Registration Map

`EndpointMappingExtensions.MapDomainEndpoints()` registers endpoints in this order:

| Group | Method | Domain | Conditional |
|-------|--------|--------|-------------|
| Users | `MapUserEndpoints()` | Identity | No |
| Permissions | `MapPermissionsEndpoints()` | Authorization | No |
| NavMap | `MapNavMapEndpoints()` | Metadata | No |
| Dataverse Documents | `MapDataverseDocumentsEndpoints()` | Documents | No |
| File Access | `MapFileAccessEndpoints()` | Documents | No |
| Documents | `MapDocumentsEndpoints()` | Documents | No |
| Upload | `MapUploadEndpoints()` | Documents | No |
| OBO | `MapOBOEndpoints()` | Auth | No |
| Document Operations | `MapDocumentOperationsEndpoints()` | Documents | No |
| Email | `MapEmailEndpoints()` | Communication | No |
| Office | `MapOfficeEndpoints()` | Office Add-ins | No |
| Field Mappings | `MapFieldMappingEndpoints()` | Configuration | No |
| Events | `MapEventEndpoints()` | Events | No |
| Work Assignments | `MapWorkAssignmentEndpoints()` | Events | No |
| Scorecard | `MapScorecardCalculatorEndpoints()` | Analytics | No |
| Analysis + Playbooks | `MapAnalysisEndpoints()`, `MapPlaybookEndpoints()`, etc. | AI Platform | Yes: `DocumentIntelligence:Enabled` AND `Analysis:Enabled` |
| RAG / Knowledge / Chat | `MapRagEndpoints()`, `MapKnowledgeBaseEndpoints()`, `MapChatEndpoints()` | AI Platform | No |
| Semantic + Record Search | `MapSemanticSearchEndpoints()`, `MapRecordSearchEndpoints()` | AI Platform | Yes: `DocumentIntelligence:Enabled` AND `Analysis:Enabled` |
| Visualization | `MapVisualizationEndpoints()` | AI Platform | No |
| Record Matching | `MapRecordMatchEndpoints()` | AI Platform | Yes: `DocumentIntelligence:RecordMatchingEnabled` |
| Admin Knowledge | `MapAdminKnowledgeEndpoints()`, `MapBuilderScopeAdminEndpoints()` | Admin | Yes: `DocumentIntelligence:Enabled` AND `Analysis:Enabled` |
| Workspace (6 groups) | `MapWorkspaceEndpoints()` through `MapWorkspaceFileEndpoints()` | Workspace | No |
| Daily Briefing | `MapDailyBriefingEndpoints()` | AI Platform | No |
| Finance (2 groups) | `MapFinanceEndpoints()`, `MapFinanceRollupEndpoints()` | Finance | No |
| Communication | `MapCommunicationEndpoints()` | Communication | No |
| SPE Admin | `MapSpeAdminEndpoints()` | SPE Admin | No |
| Container Items | `MapContainerItemEndpoints()` | SPE Admin | No |
| Agent | `MapAgentEndpoints()` | Copilot Agent | No |
| External Access | `MapExternalAccessEndpoints()` | External | No |
| Reporting | `MapReportingEndpoints()` | Reporting | No |
| Registration | `MapRegistrationEndpoints()` | Registration | No |

SPA fallback serves `playbook-builder/index.html` for deep-linked routes.

### Endpoint Filter Inventory

Authorization is enforced per-endpoint via `IEndpointFilter` implementations (ADR-008), not global middleware.

| Filter | Path | Purpose |
|--------|------|---------|
| `DocumentAuthorizationFilter` | `Api/Filters/` | SPE document Read access |
| `AnalysisAuthorizationFilter` | `Api/Filters/` | AI analysis document access (delegates to IAiAuthorizationService) |
| `ExternalCallerAuthorizationFilter` | `Api/Filters/` | Power Pages portal user validation |
| `FinanceAuthorizationFilter` | `Api/Filters/` | Finance endpoint access |
| `WorkspaceAuthorizationFilter` | `Api/Filters/` | Workspace access |
| `WorkspaceLayoutAuthorizationFilter` | `Api/Filters/` | Workspace layout CRUD |
| `OfficeAuthFilter` | `Api/Filters/` | Office Add-in token validation |
| `OfficeDocumentAccessFilter` | `Api/Filters/` | Office document-level access |
| `PlaybookAuthorizationFilter` | `Api/Filters/` | Playbook admin operations |
| `SemanticSearchAuthorizationFilter` | `Api/Filters/` | Semantic search access |
| `RecordSearchAuthorizationFilter` | `Api/Filters/` | Record search access |
| `SpeAdminAuthorizationFilter` | `Api/Filters/` | SPE admin operations |
| `TenantAuthorizationFilter` | `Api/Filters/` | Tenant isolation |
| `VisualizationAuthorizationFilter` | `Api/Filters/` | Visualization access |
| `CommunicationAuthorizationFilter` | `Api/Filters/` | Communication operations |
| `AiAuthorizationFilter` | `Api/Filters/` | General AI endpoint access |
| `IdempotencyFilter` | `Api/Filters/` | Request deduplication |
| `JobOwnershipFilter` | `Api/Filters/` | Job handler access |
| `OfficeRateLimitFilter` | `Api/Filters/` | Office-specific rate limiting |

---

## Data Flow

### Service Registration (DI Module Pattern)

1. `Program.cs` calls `services.Add{Module}(configuration)` for each domain
2. Each module binds `IOptions<T>`, registers services (concrete types preferred per ADR-010), and adds filters
3. `GraphModule` registers `IDataverseService` as composite, then 9 narrow interface forwarding registrations
4. Narrow interfaces (`IDocumentDataverseService`, `IAnalysisDataverseService`, etc.) resolve to the same singleton — consumers inject the narrowest applicable interface

### Request Flow

1. Request enters ASP.NET Core pipeline
2. `UseSpaarkeMiddleware()` runs CORS, authentication, rate limiting, security headers
3. Endpoint matched by routing
4. Endpoint filter chain executes (authorization, idempotency)
5. Endpoint handler runs, injecting services from DI
6. Response returned (or SSE stream for AI endpoints)

### Background Job Flow

1. Endpoint or webhook enqueues job via `JobSubmissionService` to Azure Service Bus
2. `ServiceBusJobProcessor` (BackgroundService) receives message
3. Resolves `IJobHandler` by `JobType` string
4. Handler checks idempotency key (Redis), acquires processing lock, processes, marks as processed with 7-day TTL
5. AI/RAG failures are non-fatal (logged as warnings, job marked success)

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| API style | .NET 8 Minimal API + BackgroundService | No Azure Functions overhead; single deployable unit | ADR-001 |
| Authorization | Endpoint filters per-endpoint | No global middleware; fine-grained per-resource checks | ADR-008 |
| DI pattern | Feature modules with concrete types | Minimize DI registrations; forwarding delegates don't count | ADR-010 |
| Caching | Redis-first via IDistributedCache | No hybrid L1 cache unless profiling proves need | ADR-009 |
| SPE operations | SpeFileStore facade | No Graph SDK types leak above facade | ADR-007 |
| Dataverse access | Interface segregation (9 narrow + composite) | New consumers inject narrowest interface; backward compatible | ADR-010 |
| SPE container model | One container per environment | Simplify access management; entity relationships in Dataverse lookups | — |
| Upload ordering | SPE first, Dataverse second | SPE returns IDs needed by Dataverse record; avoids orphans | — |
| Container ID format | Drive ID format (`b!xxx`) | Graph API rejects raw GUIDs | — |
| Dataverse auth | ServiceClient + ClientSecret | More reliable than Managed Identity for ServiceClient | — |

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Consumed by | PCF Controls | REST endpoints (OBO auth) | Field-bound controls call BFF with `user_impersonation` scope |
| Consumed by | React Code Pages | REST endpoints (OBO or MSAL ssoSilent) | Standalone dialogs call BFF for AI, documents, uploads |
| Consumed by | Office Add-ins | REST endpoints (OBO auth) | Outlook/Word save and search |
| Consumed by | M365 Copilot Agent | `/api/agent/*` endpoints | Bot framework gateway via AgentModule |
| Consumed by | Power Pages Portal | `/api/v1/external/*` endpoints | Portal JWT auth (not Azure AD) |
| Consumed by | Dataverse Webhooks | `/api/v1/emails/webhook-trigger` | HMAC-SHA256 validated, enqueues jobs |
| Depends on | Microsoft Graph | `GraphClientFactory` (OBO + app-only) | SPE file operations, email fetch |
| Depends on | Dataverse | `IDataverseService` / `DataverseWebApiService` | Entity CRUD, metadata queries |
| Depends on | Azure AI Search | `RagService` | RAG search with OData filter builder |
| Depends on | Azure OpenAI | `OpenAiClient` | Chat completions, embeddings |
| Depends on | Azure Document Intelligence | `TextExtractorService` | Document text extraction |
| Depends on | Azure Service Bus | `ServiceBusClient` | Job queuing (13+ job types) |
| Depends on | Redis | `IDistributedCache` | Caching, idempotency, processing locks |

---

## Key Patterns

### SPE Container Model

One container per environment. All documents stored in the same container regardless of entity type. Entity relationships tracked by `sprk_document` parent lookup fields, not container location.

**Container resolution by upload flow:**

| Flow | Auth | Container Source | Parent Entity |
|------|------|-----------------|---------------|
| Code Page wizard | OBO | `containerId` URL parameter | Exists (synchronous) |
| PCF upload (legacy) | OBO | Parent's `sprk_containerid` field | Exists (synchronous) |
| Email-to-Document | App-only | `EmailProcessingOptions.DefaultContainerId` | Optional (async job) |
| Office Add-in | OBO | Request ?? `DefaultContainerId` | Optional (Service Bus worker) |
| Create New Entity | OBO | `DefaultContainerId` config | Created after upload (two-phase) |

All flows follow **SPE First, Dataverse Second**: upload to SPE to get `driveId` + `itemId` + `webUrl`, then create `sprk_document` in Dataverse. No file moves needed — files stay permanently in the original container.

### Redis Caching TTLs

| Cache Key Pattern | TTL | Purpose |
|-------------------|-----|---------|
| `navmap:lookup:{entity}:{relationship}` | 15 min | Navigation property lookup metadata (~2.5s miss vs ~0.3s hit) |
| `navmap:collection:{entity}:{relationship}` | 15 min | Collection navigation property metadata |
| Auth roles / teams | 2 min | User access roles (ADR-003 compliance) |
| Resource access | 60s | Per-resource access check |
| Communication accounts | 5 min | `sprk_communicationaccount` list |
| Finance summary | 5 min | Pre-computed spend summary; invalidated on snapshot job completion |
| OBO Graph tokens | 55 min | Cached by SHA-256 hash of input token (97% latency reduction) |
| Job idempotency keys | 7 days | Prevent reprocessing completed jobs |

### IDataverseService Interface Segregation

9 focused interfaces with composite `IDataverseService` for backward compatibility:

| Interface | Domain |
|-----------|--------|
| `IDocumentDataverseService` | Document CRUD + profiles |
| `IAnalysisDataverseService` | Analysis records + scope resolution |
| `IGenericEntityService` | Generic entity CRUD + search |
| `IProcessingJobService` | Job lifecycle management |
| `IEventDataverseService` | Event + todo operations (uses `DataverseWebApiService`) |
| `IFieldMappingDataverseService` | Field mapping configuration |
| `IKpiDataverseService` | KPI metrics + scoring |
| `ICommunicationDataverseService` | Email + communication records |
| `IDataverseHealthService` | Health check operations |

Forwarding registrations in `GraphModule` resolve narrow interfaces to the composite singleton. `IEventDataverseService` is the exception — it resolves to `DataverseWebApiService` (REST/HttpClient) instead of `DataverseServiceClientImpl` (WCF-based).

### AI Authorization via Endpoint Filter

`AnalysisAuthorizationFilter` validates user Read access before AI analysis (ADR-008):
1. Extracts document IDs from request
2. Calls `IAiAuthorizationService.AuthorizeAsync(user, documentIds, httpContext, ct)`
3. `HttpContext` propagated through chain for OBO token extraction
4. Uses direct Dataverse query pattern (not `RetrievePrincipalAccess`) — see `sdap-auth-patterns.md` Pattern 5
5. Fail-closed: returns `AccessRights.None` on errors

### Background Job Handler Pattern

Job handlers implement `IJobHandler`, registered by `JobType` string in `JobProcessingModule`:

- `DocumentProcessingJobHandler`, `AppOnlyDocumentAnalysisJobHandler`, `EmailAnalysisJobHandler`
- `RagIndexingJobHandler`, `ProfileSummaryJobHandler`, `BulkRagIndexingJobHandler`

All handlers follow the same pattern: idempotency check, acquire Redis processing lock, process, release lock in finally block, mark as processed with 7-day TTL. AI/RAG failures are non-fatal.

---

## Critical Field Mapping Gotchas

| Property | Dataverse Field | Type | Gotcha |
|----------|-----------------|------|--------|
| MimeType | `sprk_mimetype` | Text | NOT `sprk_filetype` |
| FileSize | `sprk_filesize` | Whole Number (int32) | Cast `(int)` — passing `long` causes type mismatch |
| FilePath | `sprk_filepath` | Text | Must be `fileHandle.WebUrl` — enables "Open in SharePoint" links |
| GraphItemId | `sprk_graphitemid` | Text | |
| GraphDriveId | `sprk_graphdriveid` | Text | Must be Drive ID format (`b!xxx`), not raw GUID |

**WCF DateTime**: Dataverse webhooks send dates as `/Date(1234567890000)/` (WCF format). Use `NullableWcfDateTimeConverter` — standard `DateTime.Parse` fails.

---

## Known Pitfalls

| Pitfall | Symptom | Fix |
|---------|---------|-----|
| Missing DI registration for new service | `InvalidOperationException` at startup or first request | Add registration in the appropriate `*Module.cs` file; follow existing pattern |
| Endpoint filter ordering matters | Authorization filter runs after a filter that modifies the request body | Add authorization filters first via `.AddEndpointFilter<AuthFilter>()` before other filters |
| Injecting `IDataverseService` for narrow use case | Violates ADR-010 intent | Inject the narrowest interface (e.g., `IDocumentDataverseService`) |
| Forgetting to gate AI endpoints on feature flags | `NullReferenceException` when AI services are disabled | Wrap endpoint mapping in `DocumentIntelligence:Enabled` AND `Analysis:Enabled` check (see `EndpointMappingExtensions`) |
| Using `DataverseServiceClientImpl` for events | Missing OData query support | Events use `DataverseWebApiService` — check `GraphModule` forwarding for `IEventDataverseService` |
| Adding global middleware for auth | Violates ADR-008 | Use endpoint filters applied per-endpoint |
| Registering an interface when concrete suffices | Inflates DI count toward ADR-010 limit | Use concrete types; interfaces only when a seam is required |
| `DefaultContainerId` as raw GUID | Graph API 400 error | Must use Drive ID format (`b!xxx`) |
| Dataverse record before SPE upload | Orphan record if SPE fails | Always SPE first, Dataverse second |

---

## Constraints

- **MUST** use endpoint filters for resource authorization (ADR-008) — no global auth middleware
- **MUST** register services in domain-specific modules under `Infrastructure/DI/` — not directly in `Program.cs`
- **MUST** use concrete types for DI unless a seam is required (ADR-010)
- **MUST** inject the narrowest applicable Dataverse interface for new consumers
- **MUST** follow SPE First, Dataverse Second ordering for all upload flows
- **MUST** use `IDistributedCache` (Redis) for all caching — no in-process L1 cache (ADR-009)
- **MUST** use `SpeFileStore` facade for SPE operations — no `GraphServiceClient` injection into endpoints (ADR-007)
- **MUST NOT** add Azure Functions — all background work via BackgroundService + Service Bus (ADR-001)
- **MUST NOT** make HTTP/Graph calls from Dataverse plugins

---

## Related

- [Pattern: BFF API endpoint definition](../../.claude/patterns/api/) — pointer files for endpoint patterns
- [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) — Minimal API + BackgroundService
- [ADR-007](../../.claude/adr/ADR-007-spefilestore.md) — SpeFileStore facade
- [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md) — Endpoint filters for authorization
- [ADR-009](../../.claude/adr/ADR-009-redis-caching.md) — Redis-first caching
- [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) — DI minimalism
- [sdap-auth-patterns.md](sdap-auth-patterns.md) — OBO and app-only auth patterns
- [sdap-component-interactions.md](sdap-component-interactions.md) — Cross-component impact table
- [communication-service-architecture.md](communication-service-architecture.md) — Communication module architecture
- [BFF API deployment guide](../guides/) — Operational deployment procedures

---

*Last Updated: April 5, 2026*
