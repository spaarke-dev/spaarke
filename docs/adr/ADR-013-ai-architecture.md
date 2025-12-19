# ADR-013: AI Architecture for Azure OpenAI, AI Search, and Document Intelligence

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-12-05 |
| Updated | 2025-12-05 |
| Authors | Spaarke Engineering |
| Sprint | Sprint 7 - AI Foundation |

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

**We will extend Sprk.Bff.Api with AI endpoints following the established architectural patterns, using Azure OpenAI, AI Search, and Document Intelligence as the foundation.**

### Core Principles

1. **ADR Compliance** - AI implementation follows all existing ADRs
2. **Minimal API Pattern** - AI endpoints use the same patterns as existing endpoints (ADR-001)
3. **Endpoint Filter Authorization** - AI endpoints protected via endpoint filters (ADR-008)
4. **Redis-First Caching** - Embeddings and search results cached in Redis (ADR-009)
5. **Job Contract for Indexing** - Document indexing uses async job pattern (ADR-004)
6. **DI Minimalism** - Single-responsibility AI services (ADR-010)

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
│  └── RecordMatchEndpoints.cs           ← record matching/association          │
│                                                                              │
│  Services/Ai/                                                                │
│  ├── DocumentIntelligenceService.cs    ← summarization/entity extraction      │
│  ├── AnalysisOrchestrationService.cs   ← orchestration + SSE                  │
│  └── TextExtractorService.cs           ← text extraction (native/Doc Intel)   │
│                                                                              │
│  Services/Jobs/Handlers/                                                     │
│  └── DocumentAnalysisJobHandler.cs     ← JobType: "ai-analyze"                │
│                                                                              │
│  Api/Filters/                                                                │
│  ├── AiAuthorizationFilter.cs         ← document-level checks (ADR-008)      │
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

- Streaming is implemented via Server-Sent Events (SSE) on `/api/ai/document-intelligence/analyze` and `/api/ai/analysis/execute`.
- Background processing uses ADR-004 `JobContract` via `JobSubmissionService` and `ServiceBusJobProcessor`.
- `ai-analyze` is the current background job type for document intelligence analysis.

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
| Authorization | `DocumentIntelligenceEndpoints` does **not** enforce `.AddAiAuthorizationFilter()` (resource-level document checks) | Dataverse OBO-backed access checks are not yet configured for these endpoints | **REQUIRED:** add work item ID | Configure Dataverse OBO access for document checks; enable the filter on `/analyze`, `/enqueue`, `/enqueue-batch`; add tests proving forbidden access is blocked |
| Caching | Redis caching for AI outputs is not fully implemented/standardized (do not assume cache coverage) | Implementation is incremental and must avoid caching unsafe/PII-heavy payloads without policy | **REQUIRED:** add work item ID | Define cacheable artifacts + TTLs; implement via ADR-009 patterns; centralize key/TTL definitions; add cache hit/miss telemetry |
| Data persistence | Some “status update” flows are not fully wired (e.g., placeholder updates for pending/completed states) | Dataverse update schema/fields may still be evolving | **REQUIRED:** add work item ID | Finalize Dataverse fields; implement real updates in all enqueue/handler paths; ensure consistent status transitions and error states |

---

## Alternatives Considered

### Alternative 1: Separate AI Microservice

**Description:** Deploy AI functionality as a separate microservice.

**Pros:**
- Independent scaling
- Technology flexibility

**Cons:**
- Additional deployment complexity
- Network latency between services
- Duplicated authentication/authorization logic
- Violates our simplicity-first architecture

**Decision:** Rejected - adds unnecessary complexity for current scale.

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

### Alternative 3: Azure Functions for AI

**Description:** Use Azure Functions for AI processing.

**Pros:**
- Consumption-based scaling
- Independent deployment

**Cons:**
- Cold start latency
- Different deployment model than BFF
- Inconsistent patterns

**Decision:** Rejected - prefer unified BFF approach per ADR-001.

---

## Implementation Details

### File Structure

```
src/server/api/Sprk.Bff.Api/
├── Api/
│   └── Ai/
│       ├── DocumentIntelligenceEndpoints.cs
│       ├── AnalysisEndpoints.cs
│       └── RecordMatchEndpoints.cs
├── Services/
│   └── Ai/
│       ├── DocumentIntelligenceService.cs
│       ├── AnalysisOrchestrationService.cs
│       ├── TextExtractorService.cs
│       └── OpenAiClient.cs
├── Services/Jobs/
│   ├── JobContract.cs
│   ├── ServiceBusJobProcessor.cs
│   └── Handlers/
│       └── DocumentAnalysisJobHandler.cs
├── Api/Filters/
│   └── Analysis*AuthorizationFilter.cs
└── Configuration/
    └── DocumentIntelligenceOptions.cs

src/client/pcf/
├── UniversalQuickCreate/               # Upload + AI summary + record match (SSE client utilities)
├── AnalysisWorkspace/                  # Interactive analysis surface (SSE streaming)
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

- [SPAARKE-AI-STRATEGY.md](../architecture/SPAARKE-AI-STRATEGY.md) - Strategic AI direction and Microsoft Foundry alignment
- [SPAARKE-AI-ARCHITECTURE.md](../../ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md) - Detailed implementation guide
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

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-013 Concise](../../.claude/adr/ADR-013-ai-architecture.md) - ~100 lines
- [AI Constraints](../../.claude/constraints/ai.md) - MUST/MUST NOT rules

**When to load this full ADR**: Historical context, deployment models, resource requirements, security considerations.
