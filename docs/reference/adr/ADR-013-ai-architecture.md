# ADR-013: AI Architecture for Azure OpenAI, AI Search, and Document Intelligence

**Status:** Accepted
**Date:** 2025-12-05
**Authors:** Spaarke Engineering
**Sprint:** Sprint 7 - AI Foundation

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
│  Endpoints/                                                                  │
│  ├── AiEndpoints.cs          ← AI API routes                                │
│  │   ├── POST /api/ai/chat   ← RAG-powered chat                             │
│  │   ├── POST /api/ai/search ← Semantic/hybrid search                       │
│  │   └── POST /api/ai/index  ← Trigger document indexing                    │
│  │                                                                          │
│  Services/                                                                   │
│  ├── AiChatService.cs        ← Orchestrates RAG pipeline                    │
│  ├── AiSearchService.cs      ← Vector + keyword search                      │
│  ├── EmbeddingService.cs     ← Generates embeddings (cached)                │
│  └── DocumentProcessorService.cs ← Document Intelligence integration        │
│                                                                              │
│  Jobs/                                                                       │
│  └── AiIndexingJobHandler.cs ← Async document indexing                      │
│                                                                              │
│  Filters/                                                                    │
│  └── AiAuthorizationFilter.cs ← Validates AI access permissions             │
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
| ADR-001 | Minimal API pattern | `AiToolEndpoints.MapGroup("/api/ai/tools")` with route handlers |
| ADR-003 | Lean authorization seams | `AiAuthorizationFilter` validates tenant AI entitlements |
| ADR-004 | Async job contract | `AiToolJobHandler` implements `IJobHandler<AiToolJobContract>` |
| ADR-007 | SpeFileStore seam | AI reads documents via `FileIntakeService` → `SpeFileStore` |
| ADR-008 | Endpoint filters | All AI endpoints use `AddEndpointFilter<AiAuthorizationFilter>()` |
| ADR-009 | Redis-first caching | Extracted text cached with key `ai:text:{driveId}:{itemId}`, TTL 1h |
| ADR-010 | DI minimalism | Tool framework: `AiToolService`, `IAiToolHandler` implementations |

---

## AI Tool Framework

AI functionality is implemented through a **reusable Tool Framework** that provides shared infrastructure for all AI features.

### Dual Pipeline Architecture

When users upload files, two pipelines execute in parallel:

1. **SPE Pipeline**: Upload → Store → Create Dataverse record
2. **AI Pipeline**: Extract text → Process with tool-specific AI → Save results

### Tool Handler Pattern

Each AI tool (Summarize, Translate, etc.) implements `IAiToolHandler`:

```csharp
public interface IAiToolHandler
{
    string ToolName { get; }
    IAsyncEnumerable<AiToolChunk> ProcessStreamingAsync(AiToolContext context, CancellationToken ct);
    Task<AiToolResult> ProcessAsync(AiToolContext context, CancellationToken ct);
    Task SaveResultAsync(AiToolResult result, AiToolContext context, CancellationToken ct);
}
```

### Streaming UI Integration

The `AiToolAgent` PCF component embeds in forms/dialogs and connects to streaming endpoints via Server-Sent Events (SSE), providing real-time AI response rendering.

See [AI Tool Framework Spec](../../projects/ai-tool-framework/spec.md) for complete implementation details.

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
│   └── AiToolEndpoints.cs              # Streaming + enqueue endpoints
├── Services/
│   └── Ai/
│       ├── AiToolService.cs            # Tool orchestrator
│       ├── FileIntakeService.cs        # File download from SPE
│       ├── TextExtractorService.cs     # Text extraction (native + Doc Intel)
│       ├── AiStreamingService.cs       # OpenAI streaming wrapper
│       ├── AiSearchService.cs          # Vector/hybrid search
│       ├── EmbeddingService.cs         # Embedding generation
│       └── Tools/
│           ├── IAiToolHandler.cs       # Tool interface
│           ├── SummarizeToolHandler.cs # Summarize implementation
│           └── TranslateToolHandler.cs # Translate implementation
├── Jobs/
│   └── AiToolJobHandler.cs             # Background job processor
├── Filters/
│   └── AiAuthorizationFilter.cs
├── Models/
│   └── AiTools/
│       ├── AiToolRequest.cs
│       ├── AiToolContext.cs
│       ├── AiToolChunk.cs
│       ├── AiToolResult.cs
│       └── AiToolJobContract.cs
└── Configuration/
    └── AiOptions.cs

src/client/pcf/
├── UniversalQuickCreate/               # Embeds AiToolAgent
└── AiToolAgent/                        # Reusable streaming AI PCF
    └── AiToolAgent/
        ├── components/
        │   ├── AiToolAgentContainer.tsx
        │   └── StreamingResponse.tsx
        └── services/
            └── SseClient.ts
```

### Endpoint Registration

```csharp
// AiToolEndpoints.cs
public static class AiToolEndpoints
{
    public static RouteGroupBuilder MapAiToolEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/ai/tools")
            .WithTags("AI Tools")
            .AddEndpointFilter<AiAuthorizationFilter>();

        // Streaming endpoint - returns SSE
        group.MapPost("/{toolName}/stream", StreamToolAsync)
            .WithName("AiToolStream")
            .Produces(200, contentType: "text/event-stream");

        // Fire-and-forget endpoint
        group.MapPost("/{toolName}/enqueue", EnqueueToolAsync)
            .WithName("AiToolEnqueue")
            .Produces<EnqueueResponse>(202);
            .Produces(401);

        group.MapPost("/index", HandleIndexAsync)
            .WithName("AiIndex")
            .Produces<JobAcceptedResponse>(202)
            .Produces(401);

        return group;
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
