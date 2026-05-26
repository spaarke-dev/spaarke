# ADR-013: AI Architecture for Azure OpenAI, AI Search, and Document Intelligence

| Field | Value |
|-------|-------|
| Status | **Accepted (refined 2026-05-20)** |
| Date | 2025-12-05 |
| Updated | 2026-05-20 |
| Authors | Spaarke Engineering |
| Sprint | Sprint 7 - AI Foundation (R3 Phases 1-5 Complete) |

---

## Context

Spaarke is extending its platform with AI capabilities to provide intelligent document processing, semantic search, and conversational interfaces. The AI features must support two deployment models:

1. **Model 1 (Spaarke-Hosted SaaS)** - Multi-tenant, shared Azure AI resources
2. **Model 2 (Customer-Hosted)** - Dedicated Azure AI resources per customer

Without a clear architecture decision, we risk:
- **Inconsistent patterns** - AI endpoints diverging from established BFF patterns
- **Security gaps** - Missing authorization on AI endpoints
- **Performance issues** - No caching strategy for expensive AI operations
- **Maintenance burden** - Duplicated AI logic across the codebase
- **ADR violations** - Not following existing architectural decisions

### Current State
- **BFF API** (`src/server/api/Sprk.Bff.Api/`) - .NET 8 Minimal API, production-ready
- **Azure Infrastructure** - Bicep modules exist for OpenAI, AI Search, Document Intelligence
- **Existing ADRs** - 10+ ADRs covering API patterns, caching, authorization, job processing
- **Microsoft Foundry** - New AI platform direction (formerly Azure AI Foundry)

---

## Decision

**Default: extend `Sprk.Bff.Api` with AI endpoints in-process.** The bulk of AI synthesis, chat, RAG, safety, capability routing, session persistence, and orchestration MUST live in BFF because these workloads have tight latency budgets and transactional coupling that a service boundary would break — specifically:

- Capability routing: <50ms (Layer 1), <500ms (Layer 2) targets
- RAG / knowledge retrieval: <100ms target with embedding cache co-location
- Streaming chat: <500ms TTFB, retroactive safety annotation in the same request lifecycle
- Session persistence: write-through Cosmos transaction tied to chat response
- Safety perimeter: streaming-response pipeline annotation; cannot be HTTP-hopped without breaking it

**Exceptions** (separate deployable IS permitted) when ALL of the following hold:

1. The workload has **no latency coupling** with BFF synthesis (no <500ms TTFB requirement against BFF state)
2. The workload has **no transactional coupling** with BFF session/safety/audit state
3. The workload has a **bounded, well-defined integration surface** (HTTP contract, MCP tools, etc.)
4. Separation does **not require duplicating** latency-sensitive components in both processes

Workloads currently meeting all four criteria:
- **Azure Functions for sync/extraction/scheduled work** — already permitted by ADR-001. The Insights Engine sync pipelines (Dataverse → AI Search; closure-extraction triggers; scheduled re-indexing) are the canonical example.
- **MCP server (e.g., `Sprk.Insights.Mcp`)** — a thin facade over the Insights Engine designed for external consumers (M365 Copilot, declarative agents). This is a DESIGN-TIME consideration when Insights Engine Phase 1 lands; it is NOT pre-decided. A successor ADR or amendment is required before standing one up.

**This decision supersedes the prior categorical rejection of "separate AI microservice."** The 2026-05-20 BFF AI extraction assessment ([`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../assessments/bff-ai-extraction-assessment-2026-05-20.md)) examined extraction with evidence (composition, coupling, operational profile, release cadence) and concluded that the categorical rejection had the right outcome but the wrong rationale. The current decision reflects the right rationale: separation is permitted but rare, governed by technical criteria, not by an absolute rule.

### Core Principles

1. **ADR Compliance** - AI implementation follows all existing ADRs
2. **Minimal API Pattern** - AI endpoints use the same patterns as existing endpoints (ADR-001)
3. **Endpoint Filter Authorization** - AI endpoints protected via endpoint filters (ADR-008)
4. **Redis-First Caching** - Embeddings and search results cached in Redis (ADR-009)
5. **Job Contract for Indexing** - Document indexing uses async job pattern (ADR-004)
6. **DI Minimalism** - Single-responsibility AI services (ADR-010)
7. **External CRUD Consumers Use Facades** — CRUD code (Finance, Workspace, Jobs handlers in non-AI folders, etc.) MUST consume AI capabilities through `Services/Ai/PublicContracts/` facade types. Direct injection of `IOpenAiClient`, `IPlaybookService`, or other AI-internal types into CRUD code is prohibited going forward. The remediation project `sdap-bff-api-remediation-fix` migrates existing direct dependencies to the facade pattern.

### AI Services Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              Sprk.Bff.Api                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│  Api/Ai/                                                                     │
│  ├── DocumentIntelligenceEndpoints.cs  ← /api/ai/document-intelligence/*     │
│  │   ├── POST /analyze        ← SSE analysis stream                          │
│  │   ├── POST /enqueue        ← enqueue background analysis (ADR-004)        │
│  │   └── POST /enqueue-batch  ← enqueue batch background analysis            │
│  ├── AnalysisEndpoints.cs              ← /api/ai/analysis/*                   │
│  │   ├── POST /execute        ← start new analysis (SSE)                     │
│  │   ├── POST /{id}/continue  ← continue analysis chat (SSE)                 │
│  │   └── POST /{id}/export    ← export to DOCX/PDF/Email (R3)                │
│  ├── RagEndpoints.cs                   ← /api/ai/rag/* (R3 Phase 1)          │
│  │   ├── POST /search         ← hybrid vector search                         │
│  │   ├── POST /index          ← index document                               │
│  │   └── POST /embedding      ← generate embedding                           │
│  ├── ResilienceEndpoints.cs            ← /api/ai/resilience/* (R3 Phase 4)   │
│  └── RecordMatchEndpoints.cs           ← record matching/association          │
│                                                                              │
│  Services/Ai/                                                                │
│  ├── DocumentIntelligenceService.cs    ← summarization/entity extraction      │
│  ├── AnalysisOrchestrationService.cs   ← orchestration + SSE                  │
│  ├── RagService.cs                     ← hybrid search + embeddings (R3)      │
│  ├── TextExtractorService.cs           ← text extraction (native/Doc Intel)   │
│  ├── OpenAiClient.cs                   ← Azure OpenAI with resilience         │
│  └── Export/                           ← Export services (R3 Phase 3)         │
│      ├── DocxExportService.cs          ← Word document export                 │
│      ├── PdfExportService.cs           ← PDF export with QuestPDF             │
│      ├── EmailExportService.cs         ← Email via Microsoft Graph            │
│      └── TeamsExportService.cs         ← Teams adaptive cards                 │
│                                                                              │
│  Infrastructure/Resilience/            ← Circuit breaker (R3 Phase 4)         │
│  ├── ResilientSearchClient.cs          ← Polly-wrapped search client          │
│  └── CircuitBreakerRegistry.cs         ← Named circuit breaker management     │
│                                                                              │
│  Telemetry/                            ← Monitoring (R3 Phase 4)              │
│  └── AiTelemetry.cs                    ← Application Insights AI metrics      │
│                                                                              │
│  Api/Filters/                                                                │
│  ├── AiAuthorizationFilter.cs         ← document-level checks (ADR-008)      │
│  ├── TenantAuthorizationFilter.cs     ← tenant isolation (R3 Phase 5)        │
│  └── Analysis*AuthorizationFilter.cs  ← analysis resource checks (ADR-008)   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                    ┌─────────────────┼─────────────────┐
                    ▼                 ▼                 ▼
            ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
            │ Azure OpenAI │  │  AI Search   │  │Doc Intelligence│
            │              │  │              │  │              │
            │ • gpt-4o     │  │ • Vectors    │  │ • PDF/DOCX   │
            │ • gpt-4o-mini│  │ • Semantic   │  │ • OCR        │
            │ • embeddings │  │ • Hybrid     │  │ • Tables     │
            └──────────────┘  └──────────────┘  └──────────────┘
```

---

## ADR Compliance Matrix

| ADR | Requirement | AI Implementation |
|-----|-------------|-------------------|
| ADR-001 | Minimal API pattern | `DocumentIntelligenceEndpoints`, `AnalysisEndpoints`, `RecordMatchEndpoints` |
| ADR-003 | Lean authorization seams | Authorization checks run as endpoint filters + Dataverse-backed access |
| ADR-004 | Async job contract | Background analysis uses `JobContract` with `JobType: "ai-analyze"` |
| ADR-007 | SpeFileStore seam | AI file reads/writes use `SpeFileStore` (no Graph SDK leakage) |
| ADR-008 | Endpoint filters | Analysis endpoints use `Add*AuthorizationFilter()` helpers |
| ADR-009 | Redis-first caching | Redis-first caching applies to expensive AI results where safe/valuable; cache keys and TTLs are defined in code (single place) and must not be hard-coded in this ADR. |
| ADR-010 | DI minimalism | Small, focused AI services (`DocumentIntelligenceService`, orchestration) |

---

## Implementation Notes (Current)

- Streaming is implemented via Server-Sent Events (SSE) on `/api/ai/document-intelligence/analyze`, `/api/ai/analysis/execute`, and `/api/ai/analysis/{id}/continue`.
- Background processing uses ADR-004 `JobContract` via `JobSubmissionService` and `ServiceBusJobProcessor`.
- `ai-analyze` is the current background job type for document intelligence analysis.
- **R3 Phase 1**: RAG hybrid search via `RagService` with Redis-cached embeddings (text-embedding-3-small).
- **R3 Phase 2**: Analysis orchestration with playbook-based prompts and SSE streaming.
- **R3 Phase 3**: Export services (DOCX via Open XML SDK, PDF via QuestPDF, Email via Graph, Teams adaptive cards).
- **R3 Phase 4**: Application Insights telemetry (`AiTelemetry.cs`) and Polly circuit breakers for resilience.
- **R3 Phase 5**: Security hardening with `TenantAuthorizationFilter` for RAG endpoint tenant isolation.

### Temporary Exceptions, Hidden/Orphaned Elements, and Exit Criteria

AI work frequently introduces **temporary shortcuts** (e.g., temporarily disabled resource-level authorization) and **latent/orphaned elements** (unused flags, unused endpoints, unused infra resources, dead configuration). These are sometimes necessary, but they must never be “silent” or permanent.

**Policy (Required)**
- No hidden shortcuts: commented-out filters, bypassed auth, disabled checks, or hard-coded “MVP” behavior must be documented here.
- Every exception/orphan must have a **tracking work item** (issue/story/task ID) and a concrete **exit criteria**.
- Every exception/orphan must include an **owner** and an **expiry/target milestone**; it must be reviewed on each release.
- If an element is not required, it must be removed (delete unused code/config/infra rather than leaving it “just in case”).

**Exception / Orphan Register (Current)**

| Area | Exception / Orphan | Why it exists | Tracking | Exit criteria |
|------|---------------------|--------------|----------|--------------|
| Authorization | ✅ **RESOLVED** (R3 Phase 5): `AiAuthorizationFilter` now extracts `oid` claim correctly for Dataverse user lookup | Fixed in Task 044 | R3-044 | Verified working with Dataverse `azureactivedirectoryobjectid` field |
| RAG Tenant Isolation | ✅ **RESOLVED** (R3 Phase 5): `TenantAuthorizationFilter` validates `tid` claim matches request tenant | Added to RAG endpoints | R3-044 | All RAG endpoints protected with tenant isolation |
| Caching | Redis caching implemented for embeddings (7-day TTL) and search results (5-min TTL) | `EmbeddingCache` with SHA256 hash keys | R3-001-010 | ✅ Implemented with cache hit/miss telemetry via `AiTelemetry.cs` |
| Data persistence | Dataverse fields finalized (`sprk_workingdocument`, `sprk_chathistory`) | Analysis record updates via Web API | R3 Phase 2 | ✅ Status transitions implemented in `AnalysisOrchestrationService` |

**Remaining Items (Non-Blocking)**

| Area | Item | Status | Notes |
|------|------|--------|-------|
| Load Testing | Production load baseline not established | Pending | Load test scripts created (`scripts/load-tests/`), baseline pending production deployment |
| Dashboard Deployment | Azure Monitor dashboards defined but not deployed | Pending | Bicep modules ready (`infrastructure/bicep/modules/dashboard.bicep`) |

---

## Alternatives Considered

### Alternative 1: Separate AI Microservice

**Description:** Deploy AI functionality as a separate microservice (e.g., `Sprk.Ai.Bff.Api` or `Sprk.Insights.Api`).

**Pros:**
- Independent scaling
- Independent deployment cadence
- Technology flexibility
- Bounds BFF blast radius for pre-release AI package churn

**Cons (verified against evidence 2026-05-20):**
- **Network latency breaks documented budgets**: routing <50ms, RAG <100ms, streaming TTFB <500ms cannot accommodate a service hop without degrading user experience or duplicating components in both services
- **Transactional coupling**: chat streaming + retroactive safety annotation + Cosmos session writes share one request lifecycle; splitting them breaks atomicity or duplicates safety code
- **Duplicated cross-cutting concerns**: auth, correlation, ProblemDetails, telemetry would exist in both services
- **20 inbound CRUD→AI dependencies** (per 2026-05-20 assessment) would require ~3–4 weeks of refactoring before extraction is safe
- **No team specialization benefit**: 100% author overlap between AI and CRUD work (per 2026-05-20 assessment) means extraction adds context-switching cost without unlocking parallel teams

**Decision (refined 2026-05-20):** Rejected as a **default** policy. Specific narrow-scope deployables (Functions for sync, MCP server for external integration) ARE permitted when the four exception criteria in the Decision section are met. A successor ADR is required to authorize standing up any new AI service; the criteria provide the evidence framework that ADR must address.

### Alternative 2: Direct Azure AI SDK Calls from PCF

**Description:** Call Azure AI services directly from PCF controls.

**Pros:**
- No BFF changes needed

**Cons:**
- Exposes API keys to client
- No server-side caching
- No authorization control
- No audit logging

**Decision:** Rejected - significant security concerns.

### Alternative 3: Azure Functions for AI BFF endpoints

**Description:** Host AI BFF endpoints (chat, analysis, RAG search) in Azure Functions instead of `Sprk.Bff.Api`.

**Pros:**
- Consumption-based scaling
- Independent deployment

**Cons:**
- Cold start latency on user-facing AI requests
- Duplicates BFF cross-cutting concerns (auth, correlation, ProblemDetails) in a parallel runtime
- Inconsistent patterns vs. the rest of the BFF

**Decision:** Rejected for AI BFF endpoints — unified BFF approach per ADR-001.

**Note (2026-05-19):** This rejection applies only to BFF endpoints. Azure Functions ARE permitted for **out-of-band AI integration work** that meets the criteria in ADR-001 — examples relevant to the AI subsystem include:
- Dataverse → AI Search sync (event-driven + scheduled reconciliation)
- Closure-extraction pipelines triggered by matter-lifecycle events
- Scheduled re-indexers and embedding refresh jobs
- Webhook receivers from external AI services

These workloads are genuinely independent of the BFF request pipeline. See ADR-001 for the full criteria.

---

## Implementation Details

### File Structure

```
src/server/api/Sprk.Bff.Api/
├── Api/
│   ├── Ai/
│   │   ├── ChatEndpoints.cs                  # SprkChat: sessions, messages, playbook discovery
│   │   ├── DocumentIntelligenceEndpoints.cs
│   │   ├── AnalysisEndpoints.cs
│   │   ├── RagEndpoints.cs              # R3 Phase 1: RAG search/indexing
│   │   ├── ResilienceEndpoints.cs       # R3 Phase 4: Circuit breaker status
│   │   └── RecordMatchEndpoints.cs
│   └── Filters/
│       ├── AiAuthorizationFilter.cs     # Document-level auth (oid claim)
│       ├── TenantAuthorizationFilter.cs # R3: Tenant isolation (tid claim)
│       └── Analysis*AuthorizationFilter.cs
├── Models/Ai/Chat/
│   ├── ChatSession.cs                   # Session record (includes HostContext)
│   ├── ChatContext.cs                   # ChatContext + ChatKnowledgeScope
│   └── ChatHostContext.cs               # Entity-aware host context record
├── Services/
│   └── Ai/
│       ├── DocumentIntelligenceService.cs
│       ├── AnalysisOrchestrationService.cs
│       ├── AnalysisContextBuilder.cs    # Prompt construction
│       ├── IRagService.cs / RagService.cs # Hybrid search + boolean OData filters
│       ├── ScopeResolverService.cs      # Resolves knowledge source IDs from playbook
│       ├── TextExtractorService.cs
│       ├── OpenAiClient.cs              # Azure OpenAI with resilience
│       ├── Chat/                        # SprkChat pipeline
│       │   ├── ChatSessionManager.cs    # Session lifecycle + HostContext storage
│       │   ├── IChatContextProvider.cs  # Context resolution interface
│       │   ├── PlaybookChatContextProvider.cs # Playbook-driven context + entity scope
│       │   ├── SprkChatAgentFactory.cs  # Agent construction with context
│       │   └── Tools/
│       │       ├── DocumentSearchTools.cs     # Entity-scoped search discovery
│       │       └── KnowledgeRetrievalTools.cs # Knowledge source-scoped retrieval
│       └── Export/                      # R3 Phase 3: Export services
│           ├── IExportService.cs
│           ├── DocxExportService.cs     # Open XML SDK
│           ├── PdfExportService.cs      # QuestPDF
│           ├── EmailExportService.cs    # Microsoft Graph
│           └── TeamsExportService.cs    # Adaptive Cards
├── Infrastructure/
│   └── Resilience/                      # R3 Phase 4
│       ├── ResilientSearchClient.cs     # Polly circuit breaker wrapper
│       └── CircuitBreakerRegistry.cs
├── Telemetry/                           # R3 Phase 4
│   └── AiTelemetry.cs                   # Application Insights AI metrics
├── Services/Jobs/
│   ├── JobContract.cs
│   ├── ServiceBusJobProcessor.cs
│   └── Handlers/
│       └── DocumentAnalysisJobHandler.cs
└── Configuration/
    ├── DocumentIntelligenceOptions.cs
    ├── AnalysisOptions.cs               # R3: Export and context settings
    └── AiSearchResilienceOptions.cs     # R3: Circuit breaker config

src/client/shared/Spaarke.UI.Components/src/components/SprkChat/
├── SprkChat.tsx                         # Main component + playbook chips
├── types.ts                             # IHostContext, ISprkChatProps, etc.
├── index.ts                             # Barrel exports
└── hooks/
    ├── useChatSession.ts                # Session create/switch/send with HostContext
    └── useChatPlaybooks.ts              # Playbook discovery hook

src/client/pcf/
├── UniversalQuickCreate/               # Upload + AI summary + record match (SSE client)
├── AnalysisWorkspace/                  # v1.2.7: Analysis workspace with:
│   └── components/                     #   - Resume/Fresh session dialog (ADR-023)
│       ├── AnalysisWorkspaceApp.tsx    #   - Chat history persistence
│       └── SourceDocumentViewer.tsx    #   - SSE streaming chat
├── AnalysisBuilder/                    # Analysis configuration/building blocks
├── AIMetadataExtractor/                # Metadata extraction tooling/control
└── shared/                             # Shared PCF React/TS code
```

### Endpoint Registration

```csharp
// DocumentIntelligenceEndpoints.cs
public static class DocumentIntelligenceEndpoints
{
    public static IEndpointRouteBuilder MapDocumentIntelligenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/document-intelligence")
            .RequireAuthorization()
            .WithTags("AI");

        group.MapPost("/analyze", StreamAnalyze)
            .RequireRateLimiting("ai-stream")
            .Produces(200, contentType: "text/event-stream");

        group.MapPost("/enqueue", EnqueueAnalysis)
            .RequireRateLimiting("ai-batch")
            .Produces(202);

        group.MapPost("/enqueue-batch", EnqueueBatchAnalysis)
            .RequireRateLimiting("ai-batch")
            .Produces(202);

        return app;
    }
}
```

### Caching Strategy (ADR-009 Compliance)

| Data Type | Cache Key Pattern | TTL | Rationale |
|-----------|-------------------|-----|-----------|
| Embeddings | `ai:embedding:{sha256(text)}` | 24 hours | Embeddings are deterministic |
| Search Results | `ai:search:{sha256(query+filters)}` | 5 minutes | Fresh results needed |
| Chat Context | `ai:chat:{sessionId}` | 1 hour | Conversation continuity |
| Model Config | `ai:config:{tenantId}` | 10 minutes | Admin changes infrequent |

### Authorization Filter

```csharp
public class AiAuthorizationFilter : IEndpointFilter
{
    private readonly IAiEntitlementService _entitlementService;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var tenantId = httpContext.GetTenantId();
        
        // Check AI feature entitlement
        var hasAiAccess = await _entitlementService
            .HasAiAccessAsync(tenantId);
        
        if (!hasAiAccess)
        {
            return Results.Forbid();
        }

        // Check rate limits
        var rateLimitResult = await _entitlementService
            .CheckRateLimitAsync(tenantId);
        
        if (rateLimitResult.IsExceeded)
        {
            return Results.StatusCode(429);
        }

        return await next(context);
    }
}
```

### Job Handler (ADR-004 Compliance)

```csharp
public record AiIndexingJob(
    Guid TenantId,
    Guid ContainerId,
    string[] DriveItemIds,
    string IndexName
) : IJob;

public class AiIndexingJobHandler : IJobHandler<AiIndexingJob>
{
    private readonly ISpeFileStore _fileStore;
    private readonly IDocumentProcessorService _processor;
    private readonly IEmbeddingService _embeddings;
    private readonly IAiSearchService _search;

    public async Task<JobResult> HandleAsync(
        AiIndexingJob job,
        CancellationToken ct)
    {
        var indexed = 0;
        var errors = new List<string>();

        foreach (var itemId in job.DriveItemIds)
        {
            try
            {
                // 1. Get file content via SpeFileStore (ADR-007)
                var content = await _fileStore.GetContentAsync(
                    job.ContainerId, itemId, ct);

                // 2. Extract text via Document Intelligence
                var text = await _processor.ExtractTextAsync(content, ct);

                // 3. Generate embeddings (cached per ADR-009)
                var embedding = await _embeddings.GenerateAsync(text, ct);

                // 4. Index to AI Search
                await _search.IndexDocumentAsync(
                    job.IndexName,
                    new SearchDocument(itemId, text, embedding),
                    ct);

                indexed++;
            }
            catch (Exception ex)
            {
                errors.Add($"{itemId}: {ex.Message}");
            }
        }

        return new JobResult(
            Success: errors.Count == 0,
            Message: $"Indexed {indexed}/{job.DriveItemIds.Length} documents",
            Errors: errors
        );
    }
}
```

---

## Model 1 vs Model 2 Resource Configuration

### Model 1: Spaarke-Hosted SaaS (Multi-Tenant)

```json
{
  "AiOptions": {
    "Provider": "AzureOpenAI",
    "Endpoint": "https://spaarke-ai-prod.openai.azure.com/",
    "DeploymentName": "gpt-4o",
    "EmbeddingDeployment": "text-embedding-3-large",
    "SearchEndpoint": "https://spaarke-search-prod.search.windows.net",
    "SearchIndex": "spaarke-documents-{tenantId}",
    "TenantIsolation": "IndexPerTenant",
    "RateLimits": {
      "RequestsPerMinute": 60,
      "TokensPerMinute": 40000
    }
  }
}
```

### Model 2: Customer-Hosted (Dedicated)

```json
{
  "AiOptions": {
    "Provider": "AzureOpenAI",
    "Endpoint": "{customer-openai-endpoint}",
    "DeploymentName": "gpt-4o",
    "EmbeddingDeployment": "text-embedding-3-large",
    "SearchEndpoint": "{customer-search-endpoint}",
    "SearchIndex": "documents",
    "TenantIsolation": "DedicatedResources",
    "RateLimits": {
      "RequestsPerMinute": 120,
      "TokensPerMinute": 80000
    }
  }
}
```

---

## Azure Resource Requirements

### Per-Environment Resources

| Resource | Model 1 (Shared) | Model 2 (Dedicated) | SKU |
|----------|------------------|---------------------|-----|
| Azure OpenAI | 1 per region | 1 per customer | Standard S0 |
| AI Search | 1 per region | 1 per customer | Standard |
| Document Intelligence | 1 per region | 1 per customer | S0 |
| Redis Cache | Shared | Shared | Standard C1+ |

### OpenAI Model Deployments

| Model | Use Case | TPM (Model 1) | TPM (Model 2) |
|-------|----------|---------------|---------------|
| gpt-4o | Chat, complex reasoning | 40K | 80K |
| gpt-4o-mini | Simple queries, summarization | 60K | 120K |
| text-embedding-3-large | Vector embeddings | 100K | 200K |

---

## Security Considerations

1. **API Key Management** - Keys stored in Azure Key Vault, accessed via Managed Identity
2. **Network Isolation** - Private endpoints for all AI services in production
3. **Data Residency** - AI resources deployed in same region as customer data
4. **Audit Logging** - All AI requests logged with user/tenant context
5. **Content Filtering** - Azure OpenAI content filters enabled
6. **PII Detection** - Document Intelligence PII detection for sensitive content

---

## Testing Strategy

### Unit Tests
- Service logic with mocked Azure SDK clients
- Caching behavior verification
- Authorization filter logic

### Integration Tests
- End-to-end chat flow with Azure OpenAI
- Search indexing and retrieval
- Document processing pipeline

### Performance Tests
- Embedding generation latency
- Search response times
- Concurrent request handling

---

## Consequences

### Positive
- Unified architecture with existing BFF patterns
- ADR compliance ensures consistency
- Caching reduces AI costs and latency
- Job-based indexing handles large document sets
- Clear separation of concerns

### Negative
- BFF becomes larger (mitigated by clean module separation)
- Azure AI costs scale with usage
- Initial learning curve for AI services

### Risks
- Azure OpenAI rate limits may impact peak usage
- Embedding model changes require re-indexing
- AI Search costs scale with index size

---

## References

- [AI-ARCHITECTURE.md](../architecture/AI-ARCHITECTURE.md) - Architecture overview and ADR compliance
- [ADR-001: Minimal API and Workers](ADR-001-minimal-api-and-workers.md)
- [ADR-004: Async Job Contract](ADR-004-async-job-contract.md)
- [ADR-008: Authorization Endpoint Filters](ADR-008-authorization-endpoint-filters.md)
- [ADR-009: Caching Redis-First](ADR-009-caching-redis-first.md)
- [Azure OpenAI Documentation](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
- [Azure AI Search Documentation](https://learn.microsoft.com/en-us/azure/search/)

---

## Changelog

| Date | Author | Change |
|------|--------|--------|
| 2025-12-05 | Spaarke Engineering | Initial ADR created |
| 2026-01-02 | Spaarke Engineering | R3 Phases 1-5 complete: RAG, Export, Monitoring, Security |
| 2026-02-24 | Spaarke Engineering | SprkChat system: ChatHostContext, entity-scoped RAG, playbook discovery, boolean filter logic |
| 2026-05-20 | Spaarke Engineering | Decision rationale refined per BFF AI extraction assessment ([docs/assessments/bff-ai-extraction-assessment-2026-05-20.md](../assessments/bff-ai-extraction-assessment-2026-05-20.md)). Categorical "no separate AI microservice" rule replaced with four technical exception criteria. External CRUD consumers must use `Services/Ai/PublicContracts/` facades — direct injection of AI-internal types into CRUD code prohibited. |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-013 Concise](../../.claude/adr/ADR-013-ai-architecture.md) - ~100 lines
- [AI Constraints](../../.claude/constraints/ai.md) - MUST/MUST NOT rules

**When to load this full ADR**: Historical context, deployment models, resource requirements, security considerations.
