# Spaarke AI Architecture

> **Version**: 1.7
> **Date**: January 12, 2026
> **Status**: Production (R3 Phases 1-5 Complete + Document Visualization)
> **Author**: Spaarke Engineering
> **Related**: [SPAARKE-AI-STRATEGY.md](../../reference/architecture/SPAARKE-AI-STRATEGY.md)
> **R3 Updates**: RAG Foundation, Analysis Orchestration, Export Services, Monitoring/Resilience, Security
> **2026-01-12**: Document Relationship Visualization module added (3072-dim vectors, orphan file support)

---

## Overview

This document provides **implementation-focused architecture guidance** for extending the Spaarke platform with AI capabilities. AI features are implemented as **extensions to the existing `Sprk.Bff.Api`**, leveraging the BFF as an orchestration layer for Dataverse, Azure, SPE, and AI services.

For strategic context, use cases, and Microsoft Foundry platform details, see the [AI Strategy Document](../../reference/architecture/SPAARKE-AI-STRATEGY.md).

---

## Quick Reference

| Aspect | Implementation |
|--------|----------------|
| **API Layer** | `Sprk.Bff.Api/Api/Ai/` - AI endpoints in single BFF |
| **Orchestration** | BFF orchestrates SPE + Dataverse + Azure AI |
| **Auth** | Unified Access Control (UAC) - Entra ID + Dataverse permissions |
| **File Access** | `SpeFileStore` facade (existing) |
| **AI Client** | `OpenAiClient` (new, shared across tools) |
| **Text Extraction** | `TextExtractorService` (native + Document Intelligence) |
| **RAG Deployment** | `IKnowledgeDeploymentService` - Multi-tenant SearchClient routing (R3) |
| **Visualization** | `IVisualizationService` - Document relationship graph via vector similarity (2026-01-12) |
| **Background Jobs** | Service Bus + `IJobHandler` pattern (existing) |
| **Caching** | Redis with TTL-based invalidation (existing) |

---

## Core Principle: BFF as Orchestration Layer

The `Sprk.Bff.Api` serves as the **single orchestration point** for all backend services:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              Sprk.Bff.Api                                   │
│                        "Orchestration Layer"                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   Unified Access Control (UAC)                                              │
│   ─────────────────────────────────────────────────────────────────────     │
│   Entra ID authentication + Dataverse permission checks                     │
│                                                                             │
│   ┌──────────────┐    ┌──────────────┐    ┌──────────────┐                 │
│   │     SPE      │    │  Dataverse   │    │   Azure AI   │                 │
│   │ SpeFileStore │    │   Client     │    │   OpenAI     │                 │
│   │   (Graph)    │    │   (CRUD)     │    │  Doc Intel   │                 │
│   └──────────────┘    └──────────────┘    └──────────────┘                 │
│          │                   │                   │                          │
│          └───────────────────┴───────────────────┘                          │
│                              │                                              │
│                    Orchestrated by Tool Services                            │
│                              │                                              │
│   ┌─────────────────────────────────────────────────────────────────────┐  │
│   │  SummarizeService.cs                                                │  │
│   │  1. Get file from SPE (SpeFileStore)                                │  │
│   │  2. Extract text (TextExtractorService)                             │  │
│   │  3. Generate summary (OpenAiClient)                                 │  │
│   │  4. Update Dataverse (DataverseClient)                              │  │
│   └─────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Tool-Focused Implementation

AI features are built as **focused, self-contained tools** rather than a generic framework:

| Principle | Description |
|-----------|-------------|
| **YAGNI** | Don't build abstractions for hypothetical future tools |
| **Learn first** | First tool teaches what's actually reusable |
| **Extract later** | Shared utilities emerge organically after 2+ tools |
| **Fast value** | Each tool ships independently |

### First Tool: Document Summary

See [AI Document Summary Project](../../../projects/ai-document-summary/) for implementation details.

---

## 1. ADR Compliance

All AI implementations **must** follow existing Architecture Decision Records:

### 1.1 ADR Compliance Matrix

| ADR | Requirement | AI Implementation | Violation Example |
|-----|-------------|-------------------|-------------------|
| **ADR-001** | Minimal API + BackgroundService | AI endpoints via Minimal API; `AiIndexingJobHandler` via Service Bus | ❌ Azure Functions for AI |
| **ADR-003** | Lean authorization seams | Use existing `AuthorizationService`; add `AiAuthorizationFilter` | ❌ New `IAiAuthorizationService` |
| **ADR-004** | Standard job contract | `JobType: "ai-indexing"` with standard schema | ❌ Custom message format |
| **ADR-007** | SpeFileStore facade | AI services use `SpeFileStore.DownloadFileAsUserAsync(httpContext)` with OBO auth | ❌ Use `DownloadFileAsync` (app-only auth fails with 403) |
| **ADR-008** | Endpoint filters | `AiAuthorizationFilter` for per-resource auth | ❌ Global AI middleware |
| **ADR-009** | Redis-first caching | Embeddings/results in Redis; per-request in `HttpContext.Items` | ❌ `IMemoryCache` for embeddings |
| **ADR-010** | DI minimalism (≤15) | 3 new services: `AiSearchService`, `AiChatService`, `EmbeddingService` | ❌ 10+ AI service interfaces |

### 1.2 Code Review Checklist

```markdown
## AI Feature Code Review Checklist

- [ ] No Azure Functions or Durable Functions
- [ ] AI endpoints use Minimal API pattern
- [ ] `AiAuthorizationFilter` applied to all AI endpoints
- [ ] Background work uses `JobContract` with `JobType: "ai-indexing"`
- [ ] Document access via `SpeFileStore.DownloadFileAsUserAsync(httpContext)` for OBO auth
- [ ] HttpContext propagated through orchestration methods for SPE file access
- [ ] No `IMemoryCache` for AI data (use Redis)
- [ ] ≤3 new DI registrations for AI services
- [ ] Per-customer isolation via filter queries
- [ ] Rate limiting applied per customer
```

---

## 2. Solution Architecture

### 2.1 Component Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          Sprk.Bff.Api                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  API Layer                                                                  │
│  ─────────────────────────────────────────────────────────────────────────  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │ AiEndpoints.cs  │  │ DocumentEndpts  │  │ OBOEndpoints    │             │
│  │ ─────────────── │  │ (existing)      │  │ (existing)      │             │
│  │ POST /ai/search │  │                 │  │                 │             │
│  │ POST /ai/chat   │  │                 │  │                 │             │
│  │ POST /ai/summ.  │  │                 │  │                 │             │
│  │ POST /ai/extract│  │                 │  │                 │             │
│  └────────┬────────┘  └─────────────────┘  └─────────────────┘             │
│           │                                                                 │
│           ▼                                                                 │
│  Filters                                                                    │
│  ─────────────────────────────────────────────────────────────────────────  │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ AiAuthorizationFilter                                               │   │
│  │ • Validates AI feature license                                      │   │
│  │ • Extracts customerId for isolation                                 │   │
│  │ • Injects search filters for document access                        │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│           │                                                                 │
│           ▼                                                                 │
│  Services Layer                                                             │
│  ─────────────────────────────────────────────────────────────────────────  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │ AiSearchService │  │ AiChatService   │  │ EmbeddingService│             │
│  │ ─────────────── │  │ ─────────────── │  │ ─────────────── │             │
│  │ • Vector search │  │ • RAG pipeline  │  │ • Generate emb. │             │
│  │ • Hybrid search │  │ • Streaming     │  │ • Cache lookup  │             │
│  │ • Reranking     │  │ • Citations     │  │ • Batch process │             │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘             │
│           │                    │                    │                       │
│           └────────────────────┼────────────────────┘                       │
│                                ▼                                            │
│  Infrastructure                                                             │
│  ─────────────────────────────────────────────────────────────────────────  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │ SpeFileStore    │  │ Redis Cache     │  │ Service Bus     │             │
│  │ (existing)      │  │ (existing)      │  │ (existing)      │             │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Azure AI Services                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │ Azure OpenAI    │  │ Azure AI Search │  │ Doc Intelligence│             │
│  │ • gpt-4o        │  │ • Vector index  │  │ • Text extract  │             │
│  │ • gpt-4o-mini   │  │ • Semantic rank │  │ • Layout        │             │
│  │ • text-embed-3  │  │ • Hybrid search │  │ • OCR           │             │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 File Structure

```
src/server/api/Sprk.Bff.Api/
├── Api/
│   ├── AiToolEndpoints.cs                # AI Tool Framework endpoints
│   └── Filters/
│       ├── DocumentAuthorizationFilter.cs  # Existing
│       └── AiAuthorizationFilter.cs        # AI-specific auth
│
├── Configuration/
│   ├── GraphOptions.cs                   # Existing
│   ├── RedisOptions.cs                   # Existing
│   └── DocumentIntelligenceOptions.cs    # Document Intelligence configuration
│
├── Services/
│   ├── Ai/                               # AI services folder
│   │   ├── AiToolService.cs              # Tool orchestrator
│   │   ├── FileIntakeService.cs          # File download & caching
│   │   ├── TextExtractorService.cs       # Text extraction
│   │   ├── AiStreamingService.cs         # OpenAI streaming
│   │   ├── AiSearchService.cs            # Vector/hybrid search
│   │   ├── AiChatService.cs              # RAG chat completion
│   │   ├── EmbeddingService.cs           # Embedding generation
│   │   │
│   │   ├── IKnowledgeDeploymentService.cs  # (R3) RAG deployment interface
│   │   ├── KnowledgeDeploymentService.cs   # (R3) SearchClient routing
│   │   │
│   │   └── Tools/                        # Tool handlers
│   │       ├── IAiToolHandler.cs         # Interface
│   │       ├── SummarizeToolHandler.cs   # Summarize implementation
│   │       ├── TranslateToolHandler.cs   # Translate implementation
│   │       └── ...
│   │
│   └── Jobs/
│       └── Handlers/
│           ├── DocumentProcessingJobHandler.cs  # Existing
│           └── AiToolJobHandler.cs              # AI tool background jobs
│
├── Models/
│   └── AiTools/                          # AI Tool models
│       ├── AiToolRequest.cs
│       ├── AiToolResponse.cs
│       ├── AiToolContext.cs
│       ├── AiToolChunk.cs
│       └── AiToolJobContract.cs
│
└── Infrastructure/
    └── Ai/                               # AI infrastructure
        ├── AiSearchClientFactory.cs
        ├── OpenAiClientFactory.cs
        └── DocIntelligenceClientFactory.cs

src/client/pcf/
├── UniversalQuickCreate/                 # Existing - embeds AiToolAgent
└── AiToolAgent/                          # Reusable AI agent PCF
    ├── AiToolAgent/
    │   ├── index.ts
    │   ├── components/
    │   │   ├── AiToolAgentContainer.tsx
    │   │   ├── StreamingResponse.tsx
    │   │   └── StatusIndicator.tsx
    │   └── services/
    │       └── SseClient.ts
    └── ControlManifest.Input.xml
```
    └── Ai/                               # NEW: AI infrastructure
        ├── AiSearchClientFactory.cs      # Azure AI Search client
        ├── OpenAiClientFactory.cs        # Azure OpenAI client
        └── DocIntelligenceClientFactory.cs
```

---

## 3. API Endpoints

### 3.1 Endpoint Definitions

```csharp
// Api/AiEndpoints.cs
namespace Sprk.Bff.Api.Api;

public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai")
            .RequireAuthorization()
            .AddEndpointFilter<AiAuthorizationFilter>()
            .RequireRateLimiting("ai-per-customer")
            .WithTags("AI");

        // Semantic search across documents
        group.MapPost("/search", SearchDocumentsAsync)
            .WithName("AiSearch")
            .WithDescription("Semantic search across user's documents")
            .Produces<SearchResponse>(200)
            .Produces<ProblemDetails>(400)
            .Produces<ProblemDetails>(429);

        // RAG chat with documents
        group.MapPost("/chat", ChatWithDocumentsAsync)
            .WithName("AiChat")
            .WithDescription("Ask questions about documents using RAG")
            .Produces<ChatResponse>(200)
            .Produces<ProblemDetails>(400);

        // Summarize a document
        group.MapPost("/summarize/{documentId:guid}", SummarizeDocumentAsync)
            .WithName("AiSummarize")
            .WithDescription("Generate AI summary of a document")
            .Produces<SummaryResponse>(200)
            .Produces<ProblemDetails>(404);

        // Extract entities from document
        group.MapPost("/extract/{documentId:guid}", ExtractEntitiesAsync)
            .WithName("AiExtract")
            .WithDescription("Extract structured data from a document")
            .Produces<ExtractionResponse>(200)
            .Produces<ProblemDetails>(404);

        return app;
    }

    private static async Task<IResult> SearchDocumentsAsync(
        SearchRequest request,
        AiSearchService searchService,
        HttpContext context,
        CancellationToken ct)
    {
        var customerId = context.Items["CustomerId"] as string
            ?? throw new UnauthorizedAccessException("Customer context not found");

        var results = await searchService.SearchAsync(
            request.Query,
            customerId,
            request.Filters,
            request.TopK ?? 10,
            ct);

        return Results.Ok(new SearchResponse { Results = results });
    }

    private static async Task<IResult> ChatWithDocumentsAsync(
        ChatRequest request,
        AiChatService chatService,
        HttpContext context,
        CancellationToken ct)
    {
        var customerId = context.Items["CustomerId"] as string
            ?? throw new UnauthorizedAccessException("Customer context not found");

        var response = await chatService.ChatAsync(
            request.Messages,
            customerId,
            request.DocumentScope,
            ct);

        return Results.Ok(response);
    }

    private static async Task<IResult> SummarizeDocumentAsync(
        Guid documentId,
        SummaryRequest request,
        AiChatService chatService,
        SpeFileStore fileStore,
        HttpContext context,
        CancellationToken ct)
    {
        var customerId = context.Items["CustomerId"] as string
            ?? throw new UnauthorizedAccessException("Customer context not found");

        // Verify document access via existing authorization
        var document = await fileStore.GetDocumentMetadataAsync(documentId, ct);
        if (document == null)
            return Results.Problem("Document not found", statusCode: 404);

        var summary = await chatService.SummarizeAsync(
            documentId,
            request.Style ?? SummaryStyle.Standard,
            ct);

        return Results.Ok(summary);
    }

    private static async Task<IResult> ExtractEntitiesAsync(
        Guid documentId,
        ExtractionRequest request,
        AiChatService chatService,
        SpeFileStore fileStore,
        HttpContext context,
        CancellationToken ct)
    {
        var customerId = context.Items["CustomerId"] as string
            ?? throw new UnauthorizedAccessException("Customer context not found");

        var document = await fileStore.GetDocumentMetadataAsync(documentId, ct);
        if (document == null)
            return Results.Problem("Document not found", statusCode: 404);

        var extraction = await chatService.ExtractAsync(
            documentId,
            request.EntityTypes ?? EntityTypes.All,
            ct);

        return Results.Ok(extraction);
    }
}
```

### 3.2 Endpoint Registration

```csharp
// Program.cs addition
var app = builder.Build();

// ... existing middleware ...

// Map AI endpoints
app.MapAiEndpoints();

// ... existing endpoints ...
```

---

## 4. Services Implementation

### 4.1 AiSearchService

```csharp
// Services/Ai/AiSearchService.cs
namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Vector and hybrid search service using Azure AI Search.
/// ADR-007 compliant: No Azure SDK types leak to consumers.
/// </summary>
public class AiSearchService
{
    private readonly SearchClient _searchClient;
    private readonly EmbeddingService _embeddingService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<AiSearchService> _logger;
    private readonly AiOptions _options;

    public AiSearchService(
        SearchClient searchClient,
        EmbeddingService embeddingService,
        IDistributedCache cache,
        IOptions<AiOptions> options,
        ILogger<AiSearchService> logger)
    {
        _searchClient = searchClient;
        _embeddingService = embeddingService;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SearchResult[]> SearchAsync(
        string query,
        string customerId,
        SearchFilters? filters,
        int topK,
        CancellationToken ct)
    {
        // Check cache first (ADR-009)
        var cacheKey = $"{customerId}:ai:search:{ComputeHash(query, filters)}";
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached != null)
        {
            _logger.LogDebug("Search cache hit for {CustomerId}", customerId);
            return JsonSerializer.Deserialize<SearchResult[]>(cached)!;
        }

        // Generate query embedding
        var queryVector = await _embeddingService.GenerateAsync(query, ct);

        // Build search options with customer isolation filter
        var searchOptions = new SearchOptions
        {
            Size = topK,
            Select = { "documentId", "fileName", "content", "chunkIndex" },
            VectorSearch = new VectorSearchOptions
            {
                Queries = { new VectorizedQuery(queryVector) { KNearestNeighborsCount = topK, Fields = { "contentVector" } } }
            },
            // CRITICAL: Always filter by customerId for isolation
            Filter = BuildFilter(customerId, filters),
            QueryType = SearchQueryType.Semantic,
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = "default",
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive)
            }
        };

        var response = await _searchClient.SearchAsync<SearchDocument>(query, searchOptions, ct);

        var results = response.Value.GetResults()
            .Select(r => new SearchResult
            {
                DocumentId = r.Document.GetString("documentId"),
                FileName = r.Document.GetString("fileName"),
                Content = r.Document.GetString("content"),
                Score = r.Score ?? 0,
                Caption = r.SemanticSearch?.Captions?.FirstOrDefault()?.Text
            })
            .ToArray();

        // Cache results (ADR-009: 5 minute TTL for search results)
        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(results),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.Caching.SearchResultTtlSeconds) },
            ct);

        return results;
    }

    private static string BuildFilter(string customerId, SearchFilters? filters)
    {
        var conditions = new List<string>
        {
            $"customerId eq '{customerId}'"  // Always required for isolation
        };

        if (filters?.MatterId != null)
            conditions.Add($"matterId eq '{filters.MatterId}'");

        if (filters?.FileTypes?.Any() == true)
            conditions.Add($"fileType eq '{string.Join("' or fileType eq '", filters.FileTypes)}'");

        if (filters?.DateFrom != null)
            conditions.Add($"createdDate ge {filters.DateFrom:O}");

        return string.Join(" and ", conditions);
    }

    private static string ComputeHash(string query, SearchFilters? filters)
    {
        var input = $"{query}|{JsonSerializer.Serialize(filters)}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes)[..16];
    }
}
```

### 4.2 AiChatService

```csharp
// Services/Ai/AiChatService.cs
namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// RAG-based chat service using Azure OpenAI.
/// </summary>
public class AiChatService
{
    private readonly OpenAIClient _openAiClient;
    private readonly AiSearchService _searchService;
    private readonly SpeFileStore _fileStore;
    private readonly IDistributedCache _cache;
    private readonly ILogger<AiChatService> _logger;
    private readonly AiOptions _options;

    public AiChatService(
        OpenAIClient openAiClient,
        AiSearchService searchService,
        SpeFileStore fileStore,
        IDistributedCache cache,
        IOptions<AiOptions> options,
        ILogger<AiChatService> logger)
    {
        _openAiClient = openAiClient;
        _searchService = searchService;
        _fileStore = fileStore;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ChatResponse> ChatAsync(
        ChatMessage[] messages,
        string customerId,
        DocumentScope? scope,
        CancellationToken ct)
    {
        var userMessage = messages.LastOrDefault(m => m.Role == "user")?.Content
            ?? throw new ArgumentException("No user message found");

        // 1. Retrieve relevant documents
        var searchResults = await _searchService.SearchAsync(
            userMessage,
            customerId,
            scope?.ToFilters(),
            topK: 5,
            ct);

        // 2. Build RAG prompt
        var systemPrompt = BuildSystemPrompt(searchResults);

        // 3. Call Azure OpenAI
        var chatMessages = new List<ChatRequestMessage>
        {
            new ChatRequestSystemMessage(systemPrompt)
        };
        chatMessages.AddRange(messages.Select(m => m.Role switch
        {
            "user" => new ChatRequestUserMessage(m.Content) as ChatRequestMessage,
            "assistant" => new ChatRequestAssistantMessage(m.Content),
            _ => throw new ArgumentException($"Unknown role: {m.Role}")
        }));

        var chatOptions = new ChatCompletionsOptions(_options.OpenAi.ChatModel, chatMessages)
        {
            MaxTokens = _options.OpenAi.MaxTokensPerRequest,
            Temperature = 0.7f
        };

        var response = await _openAiClient.GetChatCompletionsAsync(chatOptions, ct);
        var choice = response.Value.Choices.First();

        return new ChatResponse
        {
            Content = choice.Message.Content,
            Citations = searchResults.Select(r => new Citation
            {
                DocumentId = r.DocumentId,
                FileName = r.FileName,
                Excerpt = r.Caption ?? r.Content[..Math.Min(200, r.Content.Length)]
            }).ToList(),
            TokenUsage = new TokenUsage
            {
                PromptTokens = response.Value.Usage.PromptTokens,
                CompletionTokens = response.Value.Usage.CompletionTokens
            }
        };
    }

    public async Task<SummaryResponse> SummarizeAsync(
        Guid documentId,
        SummaryStyle style,
        CancellationToken ct)
    {
        // Check cache (ADR-009: 1 hour TTL for summaries)
        var cacheKey = $"ai:summary:{documentId}:{style}";
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached != null)
            return JsonSerializer.Deserialize<SummaryResponse>(cached)!;

        // Get document content
        var content = await _fileStore.GetDocumentTextAsync(documentId, ct);

        var prompt = style switch
        {
            SummaryStyle.Brief => $"Summarize this document in 2-3 sentences:\n\n{content}",
            SummaryStyle.Standard => $"Provide a paragraph summary of this document:\n\n{content}",
            SummaryStyle.Detailed => $"Provide a detailed summary with key sections:\n\n{content}",
            SummaryStyle.KeyPoints => $"Extract 5-10 key points from this document:\n\n{content}",
            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };

        var chatOptions = new ChatCompletionsOptions(_options.OpenAi.ChatModel, new[]
        {
            new ChatRequestSystemMessage("You are a document summarization assistant."),
            new ChatRequestUserMessage(prompt)
        })
        {
            MaxTokens = 1000,
            Temperature = 0.3f
        };

        var response = await _openAiClient.GetChatCompletionsAsync(chatOptions, ct);

        var summary = new SummaryResponse
        {
            DocumentId = documentId,
            Style = style,
            Summary = response.Value.Choices.First().Message.Content,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        // Cache the summary
        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(summary),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.Caching.SummaryTtlMinutes) },
            ct);

        return summary;
    }

    private static string BuildSystemPrompt(SearchResult[] context)
    {
        var contextText = string.Join("\n\n---\n\n", context.Select((r, i) =>
            $"[Document {i + 1}: {r.FileName}]\n{r.Content}"));

        return $"""
            You are an AI assistant for a document management system.
            Answer the user's question based ONLY on the provided context.
            If the answer is not in the context, say "I couldn't find that information in the available documents."
            Always cite your sources using [Document Name] format.

            ## Context
            {contextText}
            """;
    }
}
```

### 4.3 EmbeddingService

```csharp
// Services/Ai/EmbeddingService.cs
namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Embedding generation service with caching.
/// ADR-009: Embeddings cached for 24 hours (deterministic output).
/// </summary>
public class EmbeddingService
{
    private readonly OpenAIClient _openAiClient;
    private readonly IDistributedCache _cache;
    private readonly AiOptions _options;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        OpenAIClient openAiClient,
        IDistributedCache cache,
        IOptions<AiOptions> options,
        ILogger<EmbeddingService> logger)
    {
        _openAiClient = openAiClient;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<float[]> GenerateAsync(string text, CancellationToken ct)
    {
        // Check cache first
        var cacheKey = $"ai:embed:{ComputeHash(text)}";
        var cached = await _cache.GetAsync(cacheKey, ct);
        if (cached != null)
        {
            _logger.LogDebug("Embedding cache hit");
            return DeserializeVector(cached);
        }

        // Generate embedding via Azure OpenAI
        var response = await _openAiClient.GetEmbeddingsAsync(
            new EmbeddingsOptions(_options.OpenAi.EmbeddingModel, new[] { text }),
            ct);

        var vector = response.Value.Data.First().Embedding.ToArray();

        // Cache for 24 hours (embeddings are deterministic)
        await _cache.SetAsync(
            cacheKey,
            SerializeVector(vector),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.Caching.EmbeddingTtlMinutes)
            },
            ct);

        return vector;
    }

    public async Task<float[][]> GenerateBatchAsync(string[] texts, CancellationToken ct)
    {
        var results = new float[texts.Length][];
        var uncachedIndexes = new List<int>();
        var uncachedTexts = new List<string>();

        // Check cache for each text
        for (int i = 0; i < texts.Length; i++)
        {
            var cacheKey = $"ai:embed:{ComputeHash(texts[i])}";
            var cached = await _cache.GetAsync(cacheKey, ct);
            if (cached != null)
            {
                results[i] = DeserializeVector(cached);
            }
            else
            {
                uncachedIndexes.Add(i);
                uncachedTexts.Add(texts[i]);
            }
        }

        // Generate embeddings for uncached texts
        if (uncachedTexts.Count > 0)
        {
            var response = await _openAiClient.GetEmbeddingsAsync(
                new EmbeddingsOptions(_options.OpenAi.EmbeddingModel, uncachedTexts),
                ct);

            for (int i = 0; i < uncachedTexts.Count; i++)
            {
                var vector = response.Value.Data[i].Embedding.ToArray();
                results[uncachedIndexes[i]] = vector;

                // Cache each embedding
                var cacheKey = $"ai:embed:{ComputeHash(uncachedTexts[i])}";
                await _cache.SetAsync(
                    cacheKey,
                    SerializeVector(vector),
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.Caching.EmbeddingTtlMinutes)
                    },
                    ct);
            }
        }

        return results;
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(bytes);
    }

    private static byte[] SerializeVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] DeserializeVector(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }
}
```

---

## 5. Entity Extraction & Dataverse Integration

### 5.1 Document Type Classification

The AI extracts a document type classification that maps to Dataverse choice values.

**AI Prompt Constraint:** The AI is instructed to return ONLY these document type values:

| AI Output Value | Dataverse Choice Label | Dataverse Choice Value |
|-----------------|------------------------|------------------------|
| `contract` | Contract | 100000000 |
| `invoice` | Invoice | 100000001 |
| `proposal` | Proposal | 100000002 |
| `report` | Report | 100000003 |
| `letter` | Letter | 100000004 |
| `memo` | Memo | 100000005 |
| `email` | Email | 100000006 |
| `agreement` | Agreement | 100000007 |
| `statement` | Statement | 100000008 |
| `other` | Other | 100000009 |

**Mapping Code:**

```csharp
// Services/Ai/DocumentTypeMapper.cs
public static class DocumentTypeMapper
{
    private static readonly Dictionary<string, int> AiToDataverseMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["contract"] = 100000000,
        ["invoice"] = 100000001,
        ["proposal"] = 100000002,
        ["report"] = 100000003,
        ["letter"] = 100000004,
        ["memo"] = 100000005,
        ["email"] = 100000006,
        ["agreement"] = 100000007,
        ["statement"] = 100000008,
        ["other"] = 100000009
    };

    public static int? ToDataverseValue(string? aiDocumentType)
    {
        if (string.IsNullOrWhiteSpace(aiDocumentType))
            return null;

        return AiToDataverseMap.TryGetValue(aiDocumentType, out var value)
            ? value
            : 100000009; // Default to "Other"
    }
}
```

### 5.2 Extracted Entities Dataverse Fields

AI-extracted entities are stored in dedicated Dataverse text fields on the `sprk_document` entity:

| AI Model Property | Dataverse Field | Type | Storage Format |
|-------------------|-----------------|------|----------------|
| `Entities.Organizations` | `sprk_ExtractOrganization` | Multiline Text | Newline-separated |
| `Entities.People` | `sprk_ExtractPeople` | Multiline Text | Newline-separated |
| `Entities.Amounts` | `sprk_ExtractFees` | Multiline Text | Newline-separated |
| `Entities.Dates` | `sprk_ExtractDates` | Multiline Text | Newline-separated |
| `Entities.References` | `sprk_ExtractReference` | Multiline Text | Newline-separated |
| `Entities.DocumentType` | `sprk_ExtractDocumentType` | Text | Single value |

**Note:** These are **raw extracted text values**, not validated lookups. Phase 2 (Record Matching) will use these values to suggest matching Dataverse records (Accounts, Contacts, etc.).

### 5.3 Extensibility Model

The `ExtractedEntities` model supports document type-specific extensions via nested objects:

```csharp
// Models/Ai/ExtractedEntities.cs
public class ExtractedEntities
{
    // ═══════════════════════════════════════════════════════════════════════
    // COMMON FIELDS (all document types)
    // ═══════════════════════════════════════════════════════════════════════
    public List<string> Organizations { get; set; } = [];
    public List<string> People { get; set; } = [];
    public List<string> Amounts { get; set; } = [];
    public List<string> Dates { get; set; } = [];
    public string DocumentType { get; set; } = "other";
    public List<string> References { get; set; } = [];

    // ═══════════════════════════════════════════════════════════════════════
    // TYPE-SPECIFIC EXTENSIONS (null when not applicable)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Email-specific metadata. Populated when DocumentType is "email".</summary>
    public EmailMetadata? Email { get; set; }

    /// <summary>Invoice-specific metadata. Populated when DocumentType is "invoice".</summary>
    public InvoiceMetadata? Invoice { get; set; }

    /// <summary>Contract-specific metadata. Populated when DocumentType is "contract" or "agreement".</summary>
    public ContractMetadata? Contract { get; set; }
}

/// <summary>Metadata extracted from email files (.eml, .msg).</summary>
public class EmailMetadata
{
    public string? Sender { get; set; }
    public List<string> ToRecipients { get; set; } = [];
    public List<string> CcRecipients { get; set; } = [];
    public string? Subject { get; set; }
    public DateTime? SentDate { get; set; }
    public bool HasAttachments { get; set; }
}

/// <summary>Metadata extracted from invoice documents.</summary>
public class InvoiceMetadata
{
    public string? VendorName { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? DueDate { get; set; }
    public string? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public List<string> LineItems { get; set; } = [];
}

/// <summary>Metadata extracted from contracts and agreements.</summary>
public class ContractMetadata
{
    public List<string> Parties { get; set; } = [];
    public string? EffectiveDate { get; set; }
    public string? ExpirationDate { get; set; }
    public string? ContractValue { get; set; }
    public string? GoverningLaw { get; set; }
}
```

### 5.4 Adding New Document Type Extensions

To add extraction support for a new document type:

1. **Add metadata class** in `ExtractedEntities.cs`:
   ```csharp
   public class NewTypeMetadata
   {
       public string? FieldA { get; set; }
       public List<string> FieldB { get; set; } = [];
   }
   ```

2. **Add nullable property** to `ExtractedEntities`:
   ```csharp
   public NewTypeMetadata? NewType { get; set; }
   ```

3. **Update AI prompt** in `DocumentIntelligenceOptions.StructuredAnalysisPromptTemplate` to include the new fields when the document type matches.

4. **Add Dataverse fields** (optional) if the type-specific data should be stored separately:
   - Create fields: `sprk_ExtractNewTypeFieldA`, etc.
   - Update `UpdateDocumentRequest` with new properties
   - Update `DataverseService.UpdateDocumentAsync` to map them

5. **Update JSON parsing** in `DocumentIntelligenceService.ParseStructuredResponse` if special handling is needed.

### 5.5 Storage Strategy

| Data Type | Storage Approach | Rationale |
|-----------|------------------|-----------|
| **Common fields** | Dedicated Dataverse columns | Queryable, indexable, reportable |
| **Type-specific metadata** | JSON in `sprk_ExtractedMetadata` | Flexible, no schema changes needed |
| **Document type** | Both text + choice field | Text for AI raw output, choice for validated value |

---

## 6. Background Processing

### 6.1 AI Indexing Job Handler

```csharp
// Services/Jobs/Handlers/AiIndexingJobHandler.cs
namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Processes AI indexing jobs following ADR-004 job contract.
/// </summary>
public class AiIndexingJobHandler : IJobHandler
{
    public string JobType => "ai-indexing";

    private readonly SpeFileStore _fileStore;
    private readonly EmbeddingService _embeddingService;
    private readonly SearchIndexClient _indexClient;
    private readonly IDocumentIntelligenceClient _docIntelClient;
    private readonly ILogger<AiIndexingJobHandler> _logger;

    public AiIndexingJobHandler(
        SpeFileStore fileStore,
        EmbeddingService embeddingService,
        SearchIndexClient indexClient,
        IDocumentIntelligenceClient docIntelClient,
        ILogger<AiIndexingJobHandler> logger)
    {
        _fileStore = fileStore;
        _embeddingService = embeddingService;
        _indexClient = indexClient;
        _docIntelClient = docIntelClient;
        _logger = logger;
    }

    public async Task HandleAsync(JobContract job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AiIndexingPayload>(job.Payload)
            ?? throw new ArgumentException("Invalid job payload");

        _logger.LogInformation(
            "Processing AI indexing job {JobId} for document {DocumentId}, action={Action}",
            job.JobId, job.SubjectId, payload.Action);

        switch (payload.Action)
        {
            case "index":
            case "reindex":
                await IndexDocumentAsync(job.SubjectId, payload.CustomerId, ct);
                break;

            case "delete":
                await DeleteDocumentAsync(job.SubjectId, payload.CustomerId, ct);
                break;

            default:
                throw new ArgumentException($"Unknown action: {payload.Action}");
        }
    }

    private async Task IndexDocumentAsync(Guid documentId, string customerId, CancellationToken ct)
    {
        // 1. Get document from SPE via facade (ADR-007)
        var stream = await _fileStore.GetDocumentContentAsync(documentId, ct);
        var metadata = await _fileStore.GetDocumentMetadataAsync(documentId, ct);

        // 2. Extract text via Document Intelligence
        var analysisResult = await _docIntelClient.AnalyzeDocumentAsync(
            "prebuilt-read", stream, ct);
        var fullText = string.Join("\n", analysisResult.Value.Pages.SelectMany(p => p.Lines).Select(l => l.Content));

        // 3. Chunk text (semantic chunking, ~500 tokens with overlap)
        var chunks = ChunkText(fullText, maxTokens: 500, overlap: 50);

        // 4. Generate embeddings in batch
        var embeddings = await _embeddingService.GenerateBatchAsync(chunks, ct);

        // 5. Build index documents
        var indexDocs = chunks.Select((chunk, i) => new SearchDocument
        {
            ["id"] = $"{documentId}_{i}",
            ["documentId"] = documentId.ToString(),
            ["customerId"] = customerId,
            ["matterId"] = metadata.MatterId?.ToString(),
            ["fileName"] = metadata.FileName,
            ["fileType"] = Path.GetExtension(metadata.FileName),
            ["content"] = chunk,
            ["contentVector"] = embeddings[i],
            ["chunkIndex"] = i,
            ["createdDate"] = metadata.CreatedAt
        });

        // 6. Upload to Azure AI Search
        var searchClient = _indexClient.GetSearchClient($"{customerId}-documents");
        await searchClient.MergeOrUploadDocumentsAsync(indexDocs, cancellationToken: ct);

        _logger.LogInformation(
            "Indexed document {DocumentId} with {ChunkCount} chunks",
            documentId, chunks.Length);
    }

    private async Task DeleteDocumentAsync(Guid documentId, string customerId, CancellationToken ct)
    {
        var searchClient = _indexClient.GetSearchClient($"{customerId}-documents");

        // Delete all chunks for this document
        var searchResults = await searchClient.SearchAsync<SearchDocument>(
            "*",
            new SearchOptions { Filter = $"documentId eq '{documentId}'" },
            ct);

        var keysToDelete = searchResults.Value.GetResults()
            .Select(r => r.Document["id"].ToString())
            .ToList();

        if (keysToDelete.Any())
        {
            await searchClient.DeleteDocumentsAsync("id", keysToDelete, cancellationToken: ct);
            _logger.LogInformation("Deleted {Count} chunks for document {DocumentId}", keysToDelete.Count, documentId);
        }
    }

    private static string[] ChunkText(string text, int maxTokens, int overlap)
    {
        // Simple paragraph-based chunking with token estimation
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        var currentChunk = new StringBuilder();
        var estimatedTokens = 0;

        foreach (var para in paragraphs)
        {
            var paraTokens = para.Length / 4; // Rough token estimate

            if (estimatedTokens + paraTokens > maxTokens && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
                // Keep last ~overlap tokens for context
                var overlapText = currentChunk.ToString();
                currentChunk.Clear();
                if (overlapText.Length > overlap * 4)
                {
                    currentChunk.Append(overlapText[(overlapText.Length - overlap * 4)..]);
                }
                estimatedTokens = currentChunk.Length / 4;
            }

            currentChunk.AppendLine(para);
            estimatedTokens += paraTokens;
        }

        if (currentChunk.Length > 0)
            chunks.Add(currentChunk.ToString().Trim());

        return chunks.ToArray();
    }
}

public record AiIndexingPayload(string CustomerId, string ContainerId, string Action);
```

---

## 6. Configuration

### 6.1 AiOptions

```csharp
// Configuration/DocumentIntelligenceOptions.cs
namespace Sprk.Bff.Api.Configuration;

public class DocumentIntelligenceOptions
{
    public const string SectionName = "AiServices";

    [Required]
    public OpenAiSettings OpenAi { get; set; } = new();

    [Required]
    public AiSearchSettings AiSearch { get; set; } = new();

    [Required]
    public DocIntelligenceSettings DocIntelligence { get; set; } = new();

    public CachingSettings Caching { get; set; } = new();

    public RateLimitingSettings RateLimiting { get; set; } = new();
}

public class OpenAiSettings
{
    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    public string ChatModel { get; set; } = "gpt-4o";

    public string EmbeddingModel { get; set; } = "text-embedding-3-large";

    public int MaxTokensPerRequest { get; set; } = 4000;
}

public class AiSearchSettings
{
    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    public string IndexNamePrefix { get; set; } = string.Empty;

    public string SemanticConfigName { get; set; } = "default";
}

public class DocIntelligenceSettings
{
    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;
}

public class CachingSettings
{
    public int EmbeddingTtlMinutes { get; set; } = 1440; // 24 hours

    public int SearchResultTtlSeconds { get; set; } = 300; // 5 minutes

    public int SummaryTtlMinutes { get; set; } = 60; // 1 hour
}

public class RateLimitingSettings
{
    public int RequestsPerMinute { get; set; } = 100;

    public int TokensPerMinute { get; set; } = 50000;
}
```

### 6.2 appsettings.json

```json
{
  "AiServices": {
    "OpenAi": {
      "Endpoint": "${OPENAI_ENDPOINT}",
      "ApiKey": "@Microsoft.KeyVault(VaultName=${KEY_VAULT_NAME};SecretName=openai-api-key)",
      "ChatModel": "gpt-4o",
      "EmbeddingModel": "text-embedding-3-large",
      "MaxTokensPerRequest": 4000
    },
    "AiSearch": {
      "Endpoint": "${AI_SEARCH_ENDPOINT}",
      "ApiKey": "@Microsoft.KeyVault(VaultName=${KEY_VAULT_NAME};SecretName=aisearch-admin-key)",
      "IndexNamePrefix": "",
      "SemanticConfigName": "default"
    },
    "DocIntelligence": {
      "Endpoint": "${DOC_INTELLIGENCE_ENDPOINT}",
      "ApiKey": "@Microsoft.KeyVault(VaultName=${KEY_VAULT_NAME};SecretName=docintel-key)"
    },
    "Caching": {
      "EmbeddingTtlMinutes": 1440,
      "SearchResultTtlSeconds": 300,
      "SummaryTtlMinutes": 60
    },
    "RateLimiting": {
      "RequestsPerMinute": 100,
      "TokensPerMinute": 50000
    }
  }
}
```

### 6.3 Program.cs Registration

```csharp
// Program.cs additions for AI services

// ============================================================================
// AI SERVICES CONFIGURATION
// ============================================================================

builder.Services
    .AddOptions<DocumentIntelligenceOptions>()
    .Bind(builder.Configuration.GetSection(DocumentIntelligenceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// AI Service Registrations (ADR-010: ≤3 registrations)
builder.Services.AddSingleton<AiSearchService>();
builder.Services.AddSingleton<AiChatService>();
builder.Services.AddSingleton<EmbeddingService>();

// AI Job Handler
builder.Services.AddScoped<IJobHandler, AiIndexingJobHandler>();

// Azure OpenAI Client
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
    return new OpenAIClient(new Uri(options.OpenAi.Endpoint), new AzureKeyCredential(options.OpenAi.ApiKey));
});

// Azure AI Search Client
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
    return new SearchIndexClient(new Uri(options.AiSearch.Endpoint), new AzureKeyCredential(options.AiSearch.ApiKey));
});

// Document Intelligence Client
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
    return new DocumentIntelligenceClient(new Uri(options.DocIntelligence.Endpoint), new AzureKeyCredential(options.DocIntelligence.ApiKey));
});

// ============================================================================
// AI RATE LIMITING
// ============================================================================

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("ai-per-customer", context =>
        RateLimitPartition.GetTokenBucketLimiter(
            context.User.FindFirst("tenant_id")?.Value ?? "anonymous",
            _ =>
            {
                var aiOptions = context.RequestServices.GetRequiredService<IOptions<AiOptions>>().Value;
                return new TokenBucketRateLimiterOptions
                {
                    TokenLimit = aiOptions.RateLimiting.RequestsPerMinute,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    TokensPerPeriod = aiOptions.RateLimiting.RequestsPerMinute / 2
                };
            }));
});
```

---

## 7. Authorization Filters

AI endpoints use multiple authorization filters following ADR-008 (endpoint filters for resource-level auth).

### 7.1 AiAuthorizationFilter (Document-Level)

Validates user has read access to documents being analyzed. Uses `DataverseAccessDataSource` to check Dataverse permissions via `RetrievePrincipalAccess`.

```csharp
// Api/Filters/AiAuthorizationFilter.cs
namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Authorization filter for AI endpoints.
/// Validates user has read access to the document being analyzed.
/// ADR-008 compliant: Uses endpoint filter pattern.
/// </summary>
public class AiAuthorizationFilter : IEndpointFilter
{
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<AiAuthorizationFilter>? _logger;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // IMPORTANT: Extract Azure AD Object ID (oid) from claims.
        // DataverseAccessDataSource requires the 'oid' claim to lookup user
        // in Dataverse via 'azureactivedirectoryobjectid' field.
        // Fallback chain ensures compatibility with different token types.
        var userId = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(statusCode: 401, title: "Unauthorized",
                detail: "User identity not found");
        }

        // Extract document IDs from request arguments
        var documentIds = ExtractDocumentIds(context);
        if (documentIds.Count == 0)
        {
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "No document identifier found in request");
        }

        // Authorize access to all requested documents
        foreach (var documentId in documentIds)
        {
            var authContext = new AuthorizationContext
            {
                UserId = userId,
                ResourceId = documentId.ToString(),
                Operation = "read",
                CorrelationId = httpContext.TraceIdentifier
            };

            var result = await _authorizationService.AuthorizeAsync(authContext);
            if (!result.IsAllowed)
            {
                return Results.Problem(statusCode: 403, title: "Forbidden",
                    detail: $"Access denied to document {documentId}");
            }
        }

        return await next(context);
    }

    private static List<Guid> ExtractDocumentIds(EndpointFilterInvocationContext context)
    {
        // Supports DocumentAnalysisRequest, batch IEnumerable<DocumentAnalysisRequest>, or raw Guid
        // See implementation for full pattern matching
    }
}
```

**Applied to**: All `/api/ai/document-intelligence/*` endpoints (`/analyze`, `/enqueue`, `/enqueue-batch`)

### 7.2 TenantAuthorizationFilter (Tenant Isolation)

Validates user's Azure AD tenant matches the requested tenant in RAG operations. Prevents cross-tenant data access.

```csharp
// Api/Filters/TenantAuthorizationFilter.cs
/// <summary>
/// Validates user's Azure AD tenant matches the requested tenant.
/// Prevents cross-tenant RAG index access.
/// </summary>
public class TenantAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Extract tenant from Azure AD 'tid' claim
        var userTenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        // Extract tenant from request body (tenantId field)
        var requestTenantId = ExtractTenantIdFromRequest(context);

        if (!string.IsNullOrEmpty(requestTenantId) &&
            !string.Equals(userTenantId, requestTenantId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "Tenant mismatch: cannot access resources in another tenant");
        }

        return await next(context);
    }
}
```

**Applied to**: All `/api/ai/rag/*` endpoints (`/search`, `/index`, `/index/batch`, `/delete`, `/delete/source`)

### 7.3 Authorization Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        AI Authorization Flow                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Request → .RequireAuthorization()  → Filter Chain → Service                │
│              (Azure AD JWT)                                                  │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Document Intelligence Endpoints:                                     │   │
│  │   └─ AiAuthorizationFilter                                          │   │
│  │       └─ Extracts 'oid' claim (Azure AD Object ID)                  │   │
│  │       └─ Calls DataverseAccessDataSource.GetUserAccessAsync()       │   │
│  │       └─ DataverseAccessDataSource uses ClientSecret auth           │   │
│  │       └─ Queries: RetrievePrincipalAccess(systemuser, sprk_document)│   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ RAG Endpoints:                                                       │   │
│  │   └─ TenantAuthorizationFilter                                      │   │
│  │       └─ Extracts 'tid' claim (Azure AD Tenant ID)                  │   │
│  │       └─ Compares against tenantId in request body                  │   │
│  │       └─ Prevents cross-tenant RAG index access                     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 7.4 Claim Extraction Pattern

**IMPORTANT**: Always use the `oid` claim (Azure AD Object ID) when integrating with Dataverse:

```csharp
// CORRECT: Use 'oid' claim with fallback chain
var userId = httpContext.User.FindFirst("oid")?.Value
    ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
    ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

// INCORRECT: ClaimTypes.NameIdentifier alone may not match Dataverse lookup
var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value; // DON'T DO THIS
```

The `DataverseAccessDataSource.LookupDataverseUserIdAsync()` method queries:
```
systemusers?$filter=azureactivedirectoryobjectid eq '{oid}'
```

If you use a different claim value, the user lookup will fail and authorization will be denied.

---

## 8. RAG Deployment Models (R3)

The RAG (Retrieval-Augmented Generation) system supports 3 deployment models for multi-tenant knowledge retrieval. This enables flexible data isolation strategies based on customer requirements.

### 8.1 Deployment Model Overview

| Model | Description | Index Location | Use Case |
|-------|-------------|----------------|----------|
| **Shared** | Multi-tenant index with tenant filtering | `spaarke-knowledge-index` | Default, cost-effective |
| **Dedicated** | Per-customer index in Spaarke subscription | `{tenantId}-knowledge` | Per-customer isolation |
| **CustomerOwned** | Customer's own Azure AI Search | Customer-provided | Full data sovereignty (BYOK) |

### 8.2 IKnowledgeDeploymentService

The `IKnowledgeDeploymentService` routes SearchClient requests to the appropriate deployment model:

```csharp
public interface IKnowledgeDeploymentService
{
    /// <summary>Get deployment config for a tenant (creates default if not exists).</summary>
    Task<KnowledgeDeploymentConfig> GetDeploymentConfigAsync(string tenantId, CancellationToken ct);

    /// <summary>Get SearchClient configured for tenant's deployment model.</summary>
    Task<SearchClient> GetSearchClientAsync(string tenantId, CancellationToken ct);

    /// <summary>Get SearchClient for explicit deployment ID.</summary>
    Task<SearchClient> GetSearchClientByDeploymentAsync(Guid deploymentId, CancellationToken ct);

    /// <summary>Save/update deployment configuration.</summary>
    Task<KnowledgeDeploymentConfig> SaveDeploymentConfigAsync(KnowledgeDeploymentConfig config, CancellationToken ct);

    /// <summary>Validate CustomerOwned deployment (test connectivity).</summary>
    Task<DeploymentValidationResult> ValidateCustomerOwnedDeploymentAsync(KnowledgeDeploymentConfig config, CancellationToken ct);
}
```

### 8.3 Deployment Configuration

```csharp
public record KnowledgeDeploymentConfig
{
    public Guid? Id { get; init; }                    // sprk_aiknowledgedeploymentid
    public string TenantId { get; init; }             // Dataverse org ID
    public RagDeploymentModel Model { get; init; }    // Shared, Dedicated, CustomerOwned
    public string? SearchEndpoint { get; init; }      // For CustomerOwned
    public string IndexName { get; init; }            // Index name
    public string? ApiKeySecretName { get; init; }    // Key Vault secret (CustomerOwned)
    public bool IsActive { get; init; }               // Active/inactive
}
```

### 8.4 Index Naming Convention

| Model | Index Name Pattern | Example |
|-------|-------------------|---------|
| Shared | `spaarke-knowledge-index` | `spaarke-knowledge-index` |
| Dedicated | `{sanitized-tenantId}-knowledge` | `contoso123-knowledge` |
| CustomerOwned | Customer-specified | `customer-knowledge-prod` |

**Sanitization**: Tenant IDs are sanitized to lowercase alphanumeric + hyphens for Azure AI Search compatibility.

### 8.5 CustomerOwned Model - Key Vault Integration

For CustomerOwned deployments, API keys are stored in Azure Key Vault:

```csharp
// Secret name format
config.ApiKeySecretName = "kv://spaarke-spekvcert/customer-contoso-searchkey";

// Retrieved via SecretClient injection
var apiKey = await _secretClient.GetSecretAsync(secretName, cancellationToken);
```

### 8.6 DI Registration

```csharp
// Program.cs - Analysis services section
if (!string.IsNullOrEmpty(docIntelOptions?.AiSearchEndpoint))
{
    // SearchIndexClient for index management
    builder.Services.AddSingleton(sp => new SearchIndexClient(
        new Uri(docIntelOptions.AiSearchEndpoint),
        new AzureKeyCredential(docIntelOptions.AiSearchKey)));

    // KnowledgeDeploymentService - Singleton for caching
    builder.Services.AddSingleton<IKnowledgeDeploymentService, KnowledgeDeploymentService>();

    // EmbeddingCache - Redis-based embedding caching (ADR-009)
    builder.Services.AddSingleton<IEmbeddingCache, EmbeddingCache>();

    // RagService - Hybrid search with embedding cache integration
    builder.Services.AddSingleton<IRagService, RagService>();
}
```

### 8.7 Embedding Cache (R3)

The embedding cache reduces Azure OpenAI API costs and latency by caching embeddings by content hash.

**Interface: `IEmbeddingCache`**
```csharp
public interface IEmbeddingCache
{
    Task<ReadOnlyMemory<float>?> GetEmbeddingAsync(string contentHash, CancellationToken ct);
    Task SetEmbeddingAsync(string contentHash, ReadOnlyMemory<float> embedding, CancellationToken ct);
    string ComputeContentHash(string content);

    // Convenience methods (compute hash internally)
    Task<ReadOnlyMemory<float>?> GetEmbeddingForContentAsync(string content, CancellationToken ct);
    Task SetEmbeddingForContentAsync(string content, ReadOnlyMemory<float> embedding, CancellationToken ct);
}
```

**Implementation: `EmbeddingCache`**

| Feature | Implementation |
|---------|----------------|
| **Cache Key** | `sdap:embedding:{SHA256-base64-hash}` |
| **TTL** | 7 days (embeddings are deterministic for same model) |
| **Serialization** | Binary via `Buffer.BlockCopy` (float[] → byte[]) |
| **Error Handling** | Graceful - cache failures don't break embedding generation |
| **Metrics** | `CacheMetrics` with `cacheType="embedding"` for OpenTelemetry |

**Integration with RagService:**
```csharp
// RagService.SearchAsync - Query embedding with cache
var cachedEmbedding = await _embeddingCache.GetEmbeddingForContentAsync(query, ct);
if (cachedEmbedding.HasValue)
{
    queryEmbedding = cachedEmbedding.Value;
    embeddingCacheHit = true;
}
else
{
    queryEmbedding = await _openAiClient.GenerateEmbeddingAsync(query, ct);
    await _embeddingCache.SetEmbeddingForContentAsync(query, queryEmbedding, ct);
}
```

**Performance Targets:**
- Cache hit: ~5-10ms (Redis lookup)
- Cache miss: ~150-200ms (Azure OpenAI API call)
- Expected hit rate: >80% for document-heavy workloads

---

## 9. Index Schema

### 9.1 Azure AI Search Index Definition

```json
{
  "name": "{customerId}-documents",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "documentId", "type": "Edm.String", "filterable": true, "facetable": false },
    { "name": "customerId", "type": "Edm.String", "filterable": true, "facetable": false },
    { "name": "matterId", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "parentEntityId", "type": "Edm.String", "filterable": true },
    { "name": "parentEntityType", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "fileName", "type": "Edm.String", "searchable": true, "filterable": false },
    { "name": "fileType", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "content", "type": "Edm.String", "searchable": true },
    { "name": "contentVector", "type": "Collection(Edm.Single)", "dimensions": 3072, "vectorSearchProfile": "default" },
    { "name": "chunkIndex", "type": "Edm.Int32", "sortable": true },
    { "name": "createdDate", "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true },
    { "name": "createdBy", "type": "Edm.String", "filterable": true }
  ],
  "vectorSearch": {
    "algorithms": [
      {
        "name": "hnsw",
        "kind": "hnsw",
        "hnswParameters": {
          "m": 4,
          "efConstruction": 400,
          "efSearch": 500,
          "metric": "cosine"
        }
      }
    ],
    "profiles": [
      { "name": "default", "algorithmConfigurationName": "hnsw", "vectorizer": null }
    ]
  },
  "semantic": {
    "configurations": [
      {
        "name": "default",
        "prioritizedFields": {
          "titleField": { "fieldName": "fileName" },
          "contentFields": [{ "fieldName": "content" }]
        }
      }
    ]
  }
}
```

### 9.2 RAG Knowledge Index Definition (R3)

The `spaarke-knowledge-index` is used for hybrid RAG search with vector, keyword, and semantic ranking:

```json
{
  "name": "spaarke-knowledge-index",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "tenantId", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "deploymentId", "type": "Edm.String", "filterable": true },
    { "name": "deploymentModel", "type": "Edm.String", "filterable": true },
    { "name": "knowledgeSourceId", "type": "Edm.String", "filterable": true },
    { "name": "knowledgeSourceName", "type": "Edm.String", "searchable": true },
    { "name": "documentId", "type": "Edm.String", "filterable": true },
    { "name": "documentName", "type": "Edm.String", "searchable": true },
    { "name": "documentType", "type": "Edm.String", "filterable": true },
    { "name": "chunkIndex", "type": "Edm.Int32", "sortable": true },
    { "name": "chunkCount", "type": "Edm.Int32" },
    { "name": "content", "type": "Edm.String", "searchable": true },
    { "name": "contentVector", "type": "Collection(Edm.Single)", "dimensions": 1536, "vectorSearchProfile": "knowledge-vector-profile" },
    { "name": "metadata", "type": "Edm.String" },
    { "name": "tags", "type": "Collection(Edm.String)", "searchable": true, "filterable": true },
    { "name": "createdAt", "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true },
    { "name": "updatedAt", "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true }
  ],
  "vectorSearch": {
    "algorithms": [
      {
        "name": "hnsw-knowledge",
        "kind": "hnsw",
        "hnswParameters": { "m": 4, "efConstruction": 400, "efSearch": 500, "metric": "cosine" }
      }
    ],
    "profiles": [
      { "name": "knowledge-vector-profile", "algorithm": "hnsw-knowledge" }
    ]
  },
  "semantic": {
    "configurations": [
      {
        "name": "knowledge-semantic-config",
        "prioritizedFields": {
          "titleField": { "fieldName": "documentName" },
          "prioritizedContentFields": [{ "fieldName": "content" }],
          "prioritizedKeywordsFields": [{ "fieldName": "knowledgeSourceName" }, { "fieldName": "tags" }]
        }
      }
    ]
  }
}
```

**Key differences from per-customer index:**
- 1536 dimensions (text-embedding-3-small vs 3072 for text-embedding-3-large)
- Multi-tenant fields: `tenantId`, `deploymentId`, `deploymentModel`
- Knowledge source metadata: `knowledgeSourceId`, `knowledgeSourceName`
- Tags field for categorization

**C# Model**: See `src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs`

---

## 10. Tool Framework (R3 Phase 2)

The Tool Framework provides a unified architecture for document analysis tools. Each tool implements a common interface enabling consistent execution, prompt management, and result handling.

### 10.1 Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Tool Framework Architecture                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ToolEndpoints.cs                                                           │
│  ─────────────────                                                          │
│  POST /api/ai/tools/{toolType}/execute                                      │
│  POST /api/ai/tools/{toolType}/stream                                       │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ ToolHandlerRegistry                                                 │   │
│  │ • Resolves IAnalysisToolHandler by toolType                         │   │
│  │ • Returns null for unknown tools (404)                              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ IAnalysisToolHandler                                                │   │
│  │ • ToolType: string                                                  │   │
│  │ • DisplayName: string                                               │   │
│  │ • ExecuteAsync(context, ct) → ToolResult                            │   │
│  │ • ExecuteStreamingAsync(context, ct) → IAsyncEnumerable<chunk>      │   │
│  │ • ValidateRequest(request) → ValidationResult                       │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐        │
│  │ Entity      │  │ Clause      │  │ Document    │  │ Summarize   │        │
│  │ Extractor   │  │ Analyzer    │  │ Classifier  │  │ Handler     │        │
│  └─────────────┘  └─────────────┘  └─────────────┘  └─────────────┘        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 10.2 IAnalysisToolHandler Interface

```csharp
// Services/Ai/Tools/IAnalysisToolHandler.cs
namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Common interface for all document analysis tools.
/// Each tool handles a specific analysis type (extraction, classification, etc.)
/// </summary>
public interface IAnalysisToolHandler
{
    /// <summary>Tool identifier used in API routes (e.g., "entity-extractor").</summary>
    string ToolType { get; }

    /// <summary>Human-readable name for UI display.</summary>
    string DisplayName { get; }

    /// <summary>Tool description for help text and tooltips.</summary>
    string Description { get; }

    /// <summary>Execute the tool and return complete result.</summary>
    Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken ct);

    /// <summary>Execute the tool with streaming response.</summary>
    IAsyncEnumerable<ToolStreamChunk> ExecuteStreamingAsync(
        ToolExecutionContext context,
        CancellationToken ct);

    /// <summary>Validate request before execution.</summary>
    ToolValidationResult ValidateRequest(ToolExecutionRequest request);
}
```

### 10.3 Tool Execution Context

```csharp
// Models/Ai/ToolExecutionContext.cs
public record ToolExecutionContext
{
    public required Guid DocumentId { get; init; }
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public string? DriveId { get; init; }
    public string? ItemId { get; init; }
    public Guid? PlaybookId { get; init; }
    public Dictionary<string, object>? Parameters { get; init; }
    public string? DocumentContent { get; init; }  // Pre-loaded for efficiency
    public string? DocumentFileName { get; init; }
}
```

### 10.4 Built-in Tool Handlers

| Handler | ToolType | Purpose |
|---------|----------|---------|
| `EntityExtractorHandler` | `entity-extractor` | Extract organizations, people, dates, amounts, references |
| `ClauseAnalyzerHandler` | `clause-analyzer` | Identify and categorize contract clauses |
| `DocumentClassifierHandler` | `document-classifier` | Classify document type (contract, invoice, memo, etc.) |
| `SummarizeToolHandler` | `summarize` | Generate document summaries (existing) |

### 10.5 EntityExtractorHandler Example

```csharp
// Services/Ai/Tools/EntityExtractorHandler.cs
public class EntityExtractorHandler : IAnalysisToolHandler
{
    public string ToolType => "entity-extractor";
    public string DisplayName => "Entity Extractor";
    public string Description => "Extract organizations, people, dates, and monetary amounts from documents";

    private readonly IOpenAiClient _openAiClient;
    private readonly ITextExtractorService _textExtractor;
    private readonly ILogger<EntityExtractorHandler> _logger;

    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken ct)
    {
        // 1. Get document content (pre-loaded or fetch)
        var content = context.DocumentContent
            ?? await _textExtractor.ExtractTextAsync(context.DocumentId, ct);

        // 2. Build extraction prompt
        var prompt = BuildExtractionPrompt(content, context.Parameters);

        // 3. Call OpenAI with structured output
        var response = await _openAiClient.GetChatCompletionAsync(
            new ChatRequest
            {
                Model = "gpt-4o",
                Messages = [new("system", SystemPrompt), new("user", prompt)],
                ResponseFormat = new JsonSchemaFormat(ExtractedEntitiesSchema)
            }, ct);

        // 4. Parse and return
        var entities = JsonSerializer.Deserialize<ExtractedEntities>(response.Content);
        return new ToolExecutionResult
        {
            Success = true,
            ToolType = ToolType,
            Data = entities,
            TokensUsed = response.TokensUsed
        };
    }

    private const string SystemPrompt = """
        You are an entity extraction assistant. Extract the following from documents:
        - Organizations: Company names, government entities, institutions
        - People: Full names of individuals mentioned
        - Dates: All dates in ISO 8601 format (YYYY-MM-DD)
        - Amounts: Monetary values with currency
        - References: Document numbers, case numbers, invoice numbers
        Return as JSON matching the provided schema.
        """;
}
```

### 10.6 ToolHandlerRegistry

```csharp
// Services/Ai/Tools/ToolHandlerRegistry.cs
public class ToolHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IAnalysisToolHandler> _handlers;

    public ToolHandlerRegistry(IEnumerable<IAnalysisToolHandler> handlers)
    {
        _handlers = handlers.ToDictionary(
            h => h.ToolType,
            StringComparer.OrdinalIgnoreCase);
    }

    public IAnalysisToolHandler? GetHandler(string toolType)
        => _handlers.TryGetValue(toolType, out var handler) ? handler : null;

    public IEnumerable<ToolInfo> GetAvailableTools()
        => _handlers.Values.Select(h => new ToolInfo(h.ToolType, h.DisplayName, h.Description));
}
```

### 10.7 Configuration

```csharp
// Configuration/ToolFrameworkOptions.cs
public class ToolFrameworkOptions
{
    public const string SectionName = "Ai:ToolFramework";

    public bool Enabled { get; set; } = true;
    public int MaxConcurrentExecutions { get; set; } = 10;
    public int DefaultTimeoutSeconds { get; set; } = 120;
    public Dictionary<string, ToolSettings> Tools { get; set; } = new();
}

public class ToolSettings
{
    public bool Enabled { get; set; } = true;
    public string? Model { get; set; }
    public int? MaxTokens { get; set; }
    public float? Temperature { get; set; }
}
```

### 10.8 DI Registration

```csharp
// Program.cs - Tool Framework registration
builder.Services.AddSingleton<IAnalysisToolHandler, EntityExtractorHandler>();
builder.Services.AddSingleton<IAnalysisToolHandler, ClauseAnalyzerHandler>();
builder.Services.AddSingleton<IAnalysisToolHandler, DocumentClassifierHandler>();
builder.Services.AddSingleton<IAnalysisToolHandler, SummarizeToolHandler>();
builder.Services.AddSingleton<ToolHandlerRegistry>();
```

---

## 11. Playbook System (R3 Phase 3)

The Playbook System enables users to save, share, and reuse analysis configurations. Playbooks define which tools to run, in what order, and with what parameters.

### 11.1 Playbook Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Playbook System Architecture                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  PlaybookEndpoints.cs                                                       │
│  ────────────────────                                                       │
│  GET  /api/ai/playbooks           → List user's playbooks                   │
│  GET  /api/ai/playbooks/public    → List public playbooks                   │
│  POST /api/ai/playbooks           → Create playbook                         │
│  GET  /api/ai/playbooks/{id}      → Get playbook details                    │
│  PUT  /api/ai/playbooks/{id}      → Update playbook                         │
│  POST /api/ai/playbooks/{id}/share   → Share with teams                     │
│  POST /api/ai/playbooks/{id}/unshare → Revoke sharing                       │
│  GET  /api/ai/playbooks/{id}/sharing → Get sharing info                     │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ PlaybookAuthorizationFilter                                         │   │
│  │ • OwnerOnly mode: Only owner can modify                             │   │
│  │ • OwnerOrSharedOrPublic mode: Owner, shared, or public can read     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────┐          ┌─────────────────┐                          │
│  │ IPlaybookService│          │ IPlaybookSharing│                          │
│  │ • CRUD ops      │          │   Service       │                          │
│  │ • Query/list    │          │ • Share/Revoke  │                          │
│  │ • Validation    │          │ • Access checks │                          │
│  └────────┬────────┘          └────────┬────────┘                          │
│           │                            │                                    │
│           └────────────┬───────────────┘                                    │
│                        ▼                                                    │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Dataverse: sprk_aiplaybook, sprk_aiplaybookshare                    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 11.2 Playbook Entity Model

```csharp
// Models/Ai/PlaybookDto.cs
public class PlaybookDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required Guid OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public SharingLevel SharingLevel { get; set; }
    public List<PlaybookStep> Steps { get; set; } = [];
    public Dictionary<string, object>? DefaultParameters { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public bool IsPublic { get; set; }
}

public class PlaybookStep
{
    public int Order { get; set; }
    public required string ToolType { get; set; }
    public string? DisplayName { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
    public bool ContinueOnError { get; set; }
}
```

### 11.3 Sharing Levels

| Level | Description | Access |
|-------|-------------|--------|
| `Private` | Only owner can access | Owner only |
| `Team` | Shared with specific teams | Owner + team members |
| `Organization` | Available to all org users | All users in tenant |
| `Public` | Available to everyone | All authenticated users |

```csharp
// Models/Ai/SharingLevel.cs
public enum SharingLevel
{
    Private = 0,
    Team = 1,
    Organization = 2,
    Public = 3
}
```

### 11.4 PlaybookAuthorizationFilter

The filter supports two authorization modes:

```csharp
// Filters/PlaybookAuthorizationFilter.cs
public enum PlaybookAuthorizationMode
{
    /// <summary>Only the playbook owner can access (for update/delete).</summary>
    OwnerOnly,

    /// <summary>Owner, users with shared access, or public playbook access.</summary>
    OwnerOrSharedOrPublic
}

public class PlaybookAuthorizationFilter : IEndpointFilter
{
    private readonly IPlaybookService _playbookService;
    private readonly IPlaybookSharingService? _sharingService;
    private readonly PlaybookAuthorizationMode _mode;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        // 1. Extract user ID from OID claim (Azure AD)
        var userId = context.HttpContext.User.FindFirst("oid")?.Value
            ?? context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
            return Results.Problem("User not authenticated", statusCode: 401);

        // 2. Extract playbook ID from route
        var playbookId = context.GetArgument<Guid>(0);
        if (playbookId == Guid.Empty)
            return Results.Problem("Invalid playbook ID", statusCode: 400);

        // 3. Get playbook
        var playbook = await _playbookService.GetByIdAsync(playbookId, ct);
        if (playbook == null)
            return Results.Problem("Playbook not found", statusCode: 404);

        // 4. Check access based on mode
        var hasAccess = _mode switch
        {
            PlaybookAuthorizationMode.OwnerOnly =>
                playbook.OwnerId.ToString() == userId,

            PlaybookAuthorizationMode.OwnerOrSharedOrPublic =>
                playbook.OwnerId.ToString() == userId ||
                playbook.IsPublic ||
                await HasSharedAccessAsync(playbookId, userId),

            _ => false
        };

        if (!hasAccess)
            return Results.Problem("Access denied", statusCode: 403);

        // 5. Store playbook in context for endpoint use
        context.HttpContext.Items["Playbook"] = playbook;

        return await next(context);
    }
}
```

### 11.5 IPlaybookSharingService

```csharp
// Services/Ai/IPlaybookSharingService.cs
public interface IPlaybookSharingService
{
    /// <summary>Share playbook with teams.</summary>
    Task<ShareOperationResult> ShareWithTeamsAsync(
        Guid playbookId,
        SharePlaybookRequest request,
        CancellationToken ct);

    /// <summary>Revoke sharing from teams.</summary>
    Task<ShareOperationResult> RevokeShareAsync(
        Guid playbookId,
        RevokeShareRequest request,
        CancellationToken ct);

    /// <summary>Get current sharing info for a playbook.</summary>
    Task<PlaybookSharingInfo> GetSharingInfoAsync(
        Guid playbookId,
        CancellationToken ct);

    /// <summary>Check if user has shared access to playbook.</summary>
    Task<bool> HasAccessAsync(
        Guid playbookId,
        string userId,
        CancellationToken ct);
}
```

### 11.6 Dataverse Integration

Playbook sharing uses Dataverse's native sharing model via the Web API:

```csharp
// GrantAccess for team sharing
POST /api/data/v9.2/GrantAccess
{
    "Target": {
        "@odata.type": "Microsoft.Dynamics.CRM.sprk_aiplaybook",
        "sprk_aiplaybookid": "{playbookId}"
    },
    "PrincipalAccess": {
        "Principal": {
            "@odata.type": "Microsoft.Dynamics.CRM.team",
            "teamid": "{teamId}"
        },
        "AccessMask": "ReadAccess"
    }
}

// RevokeAccess to remove sharing
POST /api/data/v9.2/RevokeAccess
{
    "Target": {
        "@odata.type": "Microsoft.Dynamics.CRM.sprk_aiplaybook",
        "sprk_aiplaybookid": "{playbookId}"
    },
    "Revokee": {
        "@odata.type": "Microsoft.Dynamics.CRM.team",
        "teamid": "{teamId}"
    }
}
```

### 11.7 API Endpoints

| Method | Path | Authorization | Description |
|--------|------|---------------|-------------|
| GET | `/api/ai/playbooks` | Authenticated | List user's own playbooks |
| GET | `/api/ai/playbooks/public` | Authenticated | List public playbooks |
| POST | `/api/ai/playbooks` | Authenticated | Create new playbook |
| GET | `/api/ai/playbooks/{id}` | OwnerOrSharedOrPublic | Get playbook details |
| PUT | `/api/ai/playbooks/{id}` | OwnerOnly | Update playbook |
| DELETE | `/api/ai/playbooks/{id}` | OwnerOnly | Delete playbook |
| POST | `/api/ai/playbooks/{id}/share` | OwnerOnly | Share with teams |
| POST | `/api/ai/playbooks/{id}/unshare` | OwnerOnly | Revoke team sharing |
| GET | `/api/ai/playbooks/{id}/sharing` | OwnerOnly | Get sharing info |

### 11.8 Access Rights

```csharp
// Models/Ai/PlaybookAccessRights.cs
[Flags]
public enum PlaybookAccessRights
{
    None = 0,
    Read = 1,
    Write = 2,
    Share = 4,
    Full = Read | Write | Share
}
```

---

## 12. Export Services (R3 Phase 4)

The Export Services enable users to share and download analysis results in multiple formats. Each export format is implemented as a separate service following a common interface pattern.

### 12.1 Export Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Export Services Architecture                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  AnalysisEndpoints.cs                                                       │
│  ────────────────────                                                       │
│  POST /api/ai/analysis/{analysisId}/export                                  │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ ExportServiceRegistry                                               │   │
│  │ • Resolves IExportService by format                                 │   │
│  │ • Returns null for unsupported formats                              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ IExportService                                                      │   │
│  │ • Format: ExportFormat                                              │   │
│  │ • Validate(context) → ExportValidationResult                        │   │
│  │ • ExportAsync(context, ct) → ExportFileResult                       │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │ DocxExport      │  │ PdfExport       │  │ EmailExport     │             │
│  │ Service         │  │ Service         │  │ Service         │             │
│  │ ───────────     │  │ ───────────     │  │ ───────────     │             │
│  │ DocumentFormat  │  │ QuestPDF        │  │ Microsoft Graph │             │
│  │ .OpenXml        │  │                 │  │ OBO Flow        │             │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 12.2 IExportService Interface

```csharp
// Services/Ai/Export/IExportService.cs
namespace Sprk.Bff.Api.Services.Ai.Export;

/// <summary>
/// Common interface for all analysis export services.
/// Each service handles a specific export format (DOCX, PDF, Email).
/// </summary>
public interface IExportService
{
    /// <summary>Export format handled by this service.</summary>
    ExportFormat Format { get; }

    /// <summary>Validate export request before execution.</summary>
    ExportValidationResult Validate(ExportContext context);

    /// <summary>Execute the export and return result.</summary>
    Task<ExportFileResult> ExportAsync(
        ExportContext context,
        CancellationToken cancellationToken);
}
```

### 12.3 Export Formats

| Format | Service | Library | Output |
|--------|---------|---------|--------|
| `Docx` | `DocxExportService` | DocumentFormat.OpenXml | Binary file |
| `Pdf` | `PdfExportService` | QuestPDF | Binary file |
| `Email` | `EmailExportService` | Microsoft Graph SDK | Action result |

### 12.4 ExportFileResult

The `ExportFileResult` supports both file downloads and action-based exports:

```csharp
// Models/Ai/ExportFileResult.cs
public class ExportFileResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public byte[]? FileBytes { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }

    // Factory methods
    public static ExportFileResult Ok(byte[] bytes, string fileName, string contentType);
    public static ExportFileResult OkAction(Dictionary<string, object?> metadata);
    public static ExportFileResult Fail(string error);
}
```

### 12.5 ExportContext

```csharp
// Models/Ai/ExportContext.cs
public class ExportContext
{
    public required Guid AnalysisId { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public string? Summary { get; init; }
    public string? SourceDocumentName { get; init; }
    public Guid? SourceDocumentId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public AnalysisEntities? Entities { get; init; }
    public AnalysisClauses? Clauses { get; init; }
    public ExportOptions? Options { get; init; }
}
```

### 12.6 DocxExportService

Generates Word documents using DocumentFormat.OpenXml:

```csharp
// Services/Ai/Export/DocxExportService.cs
public class DocxExportService : IExportService
{
    public ExportFormat Format => ExportFormat.Docx;

    public async Task<ExportFileResult> ExportAsync(
        ExportContext context, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        using var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);

        // Build document structure
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = BuildDocument(context);

        doc.Save();
        return ExportFileResult.Ok(
            stream.ToArray(),
            GenerateFileName(context, ".docx"),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }
}
```

### 12.7 PdfExportService

Generates PDF documents using QuestPDF:

```csharp
// Services/Ai/Export/PdfExportService.cs
public class PdfExportService : IExportService
{
    public ExportFormat Format => ExportFormat.Pdf;

    public async Task<ExportFileResult> ExportAsync(
        ExportContext context, CancellationToken ct)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.Header().Element(ComposeHeader);
                page.Content().Element(c => ComposeContent(c, context));
                page.Footer().Element(c => ComposeFooter(c, context));
            });
        });

        var bytes = document.GeneratePdf();
        return ExportFileResult.Ok(
            bytes,
            GenerateFileName(context, ".pdf"),
            "application/pdf");
    }
}
```

### 12.8 EmailExportService

Sends analysis via Microsoft Graph using On-Behalf-Of (OBO) flow:

```csharp
// Services/Ai/Export/EmailExportService.cs
public class EmailExportService : IExportService
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Lazy<IExportService?> _pdfService;

    public ExportFormat Format => ExportFormat.Email;

    public async Task<ExportFileResult> ExportAsync(
        ExportContext context, CancellationToken ct)
    {
        var graphClient = await _graphClientFactory.ForUserAsync(
            _httpContextAccessor.HttpContext!, ct);

        var message = await BuildEmailMessageAsync(context, ct);

        await graphClient.Me.SendMail.PostAsync(new SendMailPostRequestBody
        {
            Message = message,
            SaveToSentItems = true
        }, cancellationToken: ct);

        return ExportFileResult.OkAction(new Dictionary<string, object?>
        {
            ["Recipients"] = context.Options?.EmailTo,
            ["Subject"] = message.Subject,
            ["SentAt"] = DateTimeOffset.UtcNow
        });
    }
}
```

### 12.9 ExportServiceRegistry

Resolves export services by format:

```csharp
// Services/Ai/Export/ExportServiceRegistry.cs
public class ExportServiceRegistry
{
    private readonly IReadOnlyDictionary<ExportFormat, IExportService> _services;

    public ExportServiceRegistry(IEnumerable<IExportService> services)
    {
        _services = services.ToDictionary(s => s.Format);
    }

    public IExportService? GetService(ExportFormat format)
        => _services.TryGetValue(format, out var service) ? service : null;

    public IEnumerable<ExportFormat> SupportedFormats => _services.Keys;
}
```

### 12.10 Configuration

```csharp
// Configuration/AnalysisOptions.cs (Export section)
public class AnalysisOptions
{
    public bool EnableDocxExport { get; set; } = true;
    public bool EnablePdfExport { get; set; } = true;
    public bool EnableEmailExport { get; set; } = true;
    public ExportBrandingOptions Branding { get; set; } = new();
}

public class ExportBrandingOptions
{
    public string CompanyName { get; set; } = "Spaarke AI";
    public string? LogoUrl { get; set; }
}
```

### 12.11 DI Registration

```csharp
// Program.cs - Export services registration
builder.Services.AddScoped<IExportService, DocxExportService>();
builder.Services.AddScoped<IExportService, PdfExportService>();
builder.Services.AddScoped<IExportService, EmailExportService>();
builder.Services.AddSingleton<ExportServiceRegistry>();
```

### 12.12 Test Coverage

| Test Class | Tests | Coverage |
|------------|-------|----------|
| `DocxExportServiceTests` | 18 | Format, validation, content, entities, clauses |
| `PdfExportServiceTests` | 15 | Format, validation, PDF generation, filename |
| `EmailExportServiceTests` | 19 | Format, validation, recipients, Graph API |
| `ExportServiceRegistryTests` | 20 | Resolution, registration, unknown formats |

---

## 13. Testing

### 13.1 Unit Test Example

```csharp
// Tests/Unit/Sprk.Bff.Api.Tests/Services/Ai/AiSearchServiceTests.cs
public class AiSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_AlwaysIncludesCustomerIdFilter()
    {
        // Arrange
        var mockSearchClient = new Mock<SearchClient>();
        var mockEmbeddingService = new Mock<EmbeddingService>();
        var mockCache = new Mock<IDistributedCache>();

        mockEmbeddingService
            .Setup(e => e.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3072]);

        mockSearchClient
            .Setup(c => c.SearchAsync<SearchDocument>(
                It.IsAny<string>(),
                It.Is<SearchOptions>(o => o.Filter.Contains("customerId eq 'test-customer'")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<SearchResults<SearchDocument>>>());

        var service = new AiSearchService(
            mockSearchClient.Object,
            mockEmbeddingService.Object,
            mockCache.Object,
            Options.Create(new AiOptions()),
            Mock.Of<ILogger<AiSearchService>>());

        // Act
        await service.SearchAsync("test query", "test-customer", null, 10, CancellationToken.None);

        // Assert - Verify customer filter was applied
        mockSearchClient.Verify(c => c.SearchAsync<SearchDocument>(
            It.IsAny<string>(),
            It.Is<SearchOptions>(o => o.Filter.Contains("customerId eq 'test-customer'")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_ReturnsCachedResults_WhenAvailable()
    {
        // Arrange
        var mockCache = new Mock<IDistributedCache>();
        var cachedResults = JsonSerializer.Serialize(new[] { new SearchResult { DocumentId = "doc1" } });

        mockCache
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResults);

        var service = new AiSearchService(
            Mock.Of<SearchClient>(),
            Mock.Of<EmbeddingService>(),
            mockCache.Object,
            Options.Create(new AiOptions()),
            Mock.Of<ILogger<AiSearchService>>());

        // Act
        var results = await service.SearchAsync("test", "customer", null, 10, CancellationToken.None);

        // Assert
        Assert.Single(results);
        Assert.Equal("doc1", results[0].DocumentId);
    }
}
```

---

## 14. Monitoring & Resilience (R3 Phase 4-5)

### 14.1 Application Insights Telemetry

The `AiTelemetry` class provides custom metrics for AI operations.

**Location**: `src/server/api/Sprk.Bff.Api/Telemetry/AiTelemetry.cs`

**Metrics tracked:**

| Metric | Type | Description |
|--------|------|-------------|
| `ai.operation.duration` | Histogram | Latency of AI operations |
| `ai.embedding.cache_hit` | Counter | Embedding cache hits |
| `ai.embedding.cache_miss` | Counter | Embedding cache misses |
| `ai.circuit_breaker.state_change` | Event | Circuit state transitions |
| `ai.search.latency` | Histogram | AI Search query latency |
| `ai.token.usage` | Counter | Token consumption by model |

**Usage:**
```csharp
// Injected via DI
public class RagService
{
    private readonly AiTelemetry _telemetry;

    public async Task<RagSearchResponse> SearchAsync(...)
    {
        using var _ = _telemetry.TrackOperation("rag_search");
        // ...operation code...
    }
}
```

### 14.2 Circuit Breaker Pattern

AI Search operations are protected by Polly circuit breakers via `ResilientSearchClient`.

**Location**: `src/server/api/Sprk.Bff.Api/Infrastructure/Resilience/`

**Configuration** (`AiSearchResilienceOptions`):

| Parameter | Default | Description |
|-----------|---------|-------------|
| `FailureRatioThreshold` | 0.5 | Opens at 50% failure rate |
| `MinimumThroughput` | 5 | Min calls before evaluation |
| `BreakDuration` | 30s | Wait before half-open |
| `Timeout` | 30s | Per-request timeout |

**Circuit States:**
- **Closed**: Normal operation
- **Open**: All requests fail fast with `ai_circuit_open`
- **Half-Open**: Testing if service recovered

**Monitoring endpoint:**
```
GET /api/ai/resilience/status
→ { "aiSearch": { "state": "Closed", "lastStateChange": "..." } }
```

### 14.3 Security - Tenant Authorization Filter

**Location**: `src/server/api/Sprk.Bff.Api/Api/Filters/TenantAuthorizationFilter.cs`

The `TenantAuthorizationFilter` validates tenant isolation on RAG endpoints:

```csharp
// Applied to RAG endpoints
group.MapPost("/search", RagSearch)
    .AddEndpointFilter<TenantAuthorizationFilter>();
```

**Validation flow:**
1. Extract `tid` claim from JWT token
2. Extract `tenantId` from request body
3. Compare values (must match)
4. Return 403 if mismatch

### 14.4 Authorization Filter Fixes (R3 Phase 5)

The `AiAuthorizationFilter` was updated to correctly extract user identity:

| Before | After | Why |
|--------|-------|-----|
| `ClaimTypes.NameIdentifier` | `oid` claim | Dataverse queries `azureactivedirectoryobjectid` |

**Claim extraction order:**
1. `oid` (Azure AD Object ID) - **primary**
2. `http://schemas.microsoft.com/identity/claims/objectidentifier` - fallback
3. `ClaimTypes.NameIdentifier` - last resort

### 14.5 Azure Monitor Dashboards

Bicep modules for Azure Monitor dashboards are available:

**Location**: `infrastructure/bicep/modules/dashboard.bicep`, `alerts.bicep`

**Key dashboard tiles:**
- AI operation success rate (4-hour trend)
- Embedding cache hit rate
- Circuit breaker state timeline
- Token consumption by model
- P95 latency by operation type

**Alerts configured:**
- Circuit breaker opened (Severity 1)
- Error rate > 5% (Severity 2)
- P95 latency > 2s (Severity 3)

---

## 15. Deployment Checklist

### 15.1 Pre-Deployment

- [ ] Azure OpenAI resource provisioned with required model deployments
- [ ] Azure AI Search resource provisioned with index created
- [ ] Document Intelligence resource provisioned
- [ ] Key Vault secrets configured for all AI service keys
- [ ] Service Bus queue `ai-indexing` created
- [ ] Rate limiting configuration reviewed per environment
- [ ] Application Insights configured with AI telemetry
- [ ] Circuit breaker thresholds reviewed

### 15.2 Configuration

- [ ] `appsettings.{Environment}.json` updated with AI configuration
- [ ] Key Vault references validated
- [ ] Redis instance sized appropriately for embedding cache
- [ ] Resilience options configured in `AiSearchResilienceOptions`

### 15.3 Testing

- [ ] Unit tests passing
- [ ] Integration tests with AI services validated
- [ ] Customer isolation verified (no cross-tenant data leakage)
- [ ] Rate limiting tested under load
- [ ] Circuit breaker behavior validated

### 15.4 Post-Deployment

- [ ] Azure Monitor dashboard deployed
- [ ] Alerts configured and tested
- [ ] Load test baseline established

---

## 16. Document Relationship Visualization (2026-01-12)

### 16.1 Overview

The Document Relationship Visualization module enables users to discover semantically similar documents through an interactive graph interface. Documents are represented as nodes, with edges indicating similarity based on 3072-dimension document vectors.

### 16.2 Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     DocumentRelationshipViewer PCF                           │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │  DocumentGraph  │  │  ControlPanel   │  │  NodeActionBar  │             │
│  │  (React Flow +  │  │  (threshold,    │  │  (Open Record,  │             │
│  │   d3-force)     │  │   depth, limit) │  │   View in SPE)  │             │
│  └────────┬────────┘  └─────────────────┘  └─────────────────┘             │
└───────────┼─────────────────────────────────────────────────────────────────┘
            │ GET /api/ai/visualization/related/{documentId}
            ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        VisualizationEndpoints                                │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ VisualizationAuthorizationFilter                                    │   │
│  │ • Validates user has access to source document                      │   │
│  │ • Resource-based auth via Dataverse OBO                             │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ IVisualizationService                                               │   │
│  │ • GetRelatedDocumentsAsync(documentId, options)                     │   │
│  │ • Uses IRagService for vector similarity search                     │   │
│  │ • Returns DocumentGraphResponse with nodes, edges, metadata         │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                     Azure AI Search (spaarke-knowledge-index-v2)            │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Vector Search on documentVector3072 (3072 dimensions)               │   │
│  │ • Cosine similarity via HNSW algorithm                              │   │
│  │ • Filtered by tenantId for multi-tenant isolation                   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 16.3 Key Components

| Component | Location | Purpose |
|-----------|----------|---------|
| `IVisualizationService` | `Services/Ai/IVisualizationService.cs` | Interface for document visualization |
| `VisualizationService` | `Services/Ai/VisualizationService.cs` | Vector similarity search implementation |
| `VisualizationEndpoints` | `Api/Ai/VisualizationEndpoints.cs` | GET endpoint for related documents |
| `VisualizationAuthorizationFilter` | `Api/Filters/VisualizationAuthorizationFilter.cs` | Resource-based authorization |
| `DocumentRelationshipViewer` | `src/client/pcf/DocumentRelationshipViewer/` | PCF control (React Flow + d3-force) |

### 16.4 Data Models

```csharp
// Response model for document graph
public class DocumentGraphResponse
{
    public IReadOnlyList<DocumentNodeData> Nodes { get; set; }
    public IReadOnlyList<DocumentEdgeData> Edges { get; set; }
    public GraphMetadata Metadata { get; set; }
}

public class DocumentNodeData
{
    public string Id { get; set; }               // Node identifier
    public string? DocumentId { get; set; }      // Dataverse document ID (null for orphans)
    public string SpeFileId { get; set; }        // SPE file ID (always populated)
    public string FileName { get; set; }         // Display name
    public string FileType { get; set; }         // Extension (pdf, docx, etc.)
    public string NodeType { get; set; }         // "source", "related", or "orphan"
    public double Similarity { get; set; }       // 0.0 to 1.0
    public bool IsOrphanFile { get; set; }       // True if no Dataverse record
}
```

### 16.5 Key Features

| Feature | Implementation |
|---------|----------------|
| **3072-dim vectors** | Uses `documentVector3072` for whole-document similarity |
| **Orphan file support** | Files without Dataverse records (`documentId` nullable) |
| **Force-directed layout** | Edge distance = `200 * (1 - similarity)` for natural clustering |
| **Real-time filtering** | Similarity threshold, depth limit, max nodes per level |
| **Dark mode** | Full Fluent UI v9 token support (ADR-021) |
| **Multi-tenant isolation** | `tenantId` filter on all searches |

### 16.6 API Endpoint

```
GET /api/ai/visualization/related/{documentId}
    ?tenantId={tenantId}
    &similarityThreshold={0.65}   // Default: 0.65
    &depthLimit={1}               // Default: 1
    &maxNodesPerLevel={25}        // Default: 25

Response: DocumentGraphResponse
```

### 16.7 PCF Control Details

| Aspect | Implementation |
|--------|----------------|
| **Framework** | React Flow v10 (React 16 compatible) + d3-force |
| **Bundle Size** | 6.65 MB (React, Fluent UI externalized via platform-library) |
| **Test Coverage** | 40 component tests (Jest + React Testing Library) |
| **Version** | 1.0.18 |
| **Form Integration** | Embedded in "Search" tab of `sprk_document` form |

### 16.8 Test Coverage

| Component | Tests | Type |
|-----------|-------|------|
| VisualizationService | 27 | Unit (.NET) |
| DocumentNode | 15 | Component (Jest) |
| ControlPanel | 18 | Component (Jest) |
| NodeActionBar | 20 | Component (Jest) |
| E2E Tests | 18 | Integration |
| **Total** | **143** | |

---

## 17. Playbook Builder AI Assistant Scope Patterns (2026-01-19)

### 17.1 Overview

The AI Playbook Builder Assistant uses a scope-based architecture for AI operations. Scopes define reusable AI capabilities that can be composed into playbook workflows. This section documents the builder-specific scope patterns used by `AiPlaybookBuilderService` and `ScopeResolverService`.

### 17.2 Scope Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Playbook Builder Scope System                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  User Message                                                               │
│  ────────────────                                                           │
│  "Add an AI node for lease clause extraction"                               │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ AiPlaybookBuilderService                                            │   │
│  │ • Intent classification via Azure OpenAI (GPT-4o / GPT-4o-mini)     │   │
│  │ • Clarification flow for ambiguous requests                         │   │
│  │ • Tool call generation for canvas operations                        │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ ScopeResolverService                                                │   │
│  │ • Resolve scope by ID or search query                               │   │
│  │ • CRUD operations for customer scopes                               │   │
│  │ • Ownership validation (SYS- vs CUST-)                              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │ Actions (ACT-)  │  │ Skills (SKL-)   │  │ Tools (TL-)     │             │
│  │ • AI operations │  │ • Workflow      │  │ • Canvas ops    │             │
│  │ • Intent class. │  │   patterns      │  │ • Node manip.   │             │
│  │ • Entity extract│  │ • Domain guides │  │ • Edge creation │             │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘             │
│         │                    │                    │                        │
│         └────────────────────┼────────────────────┘                        │
│                              ▼                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Knowledge (KNW-)                                                    │   │
│  │ • Scope catalog                                                     │   │
│  │ • Reference playbooks                                               │   │
│  │ • Node schema definitions                                           │   │
│  │ • Best practices                                                    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 17.3 Scope Types and Prefixes

| Type | Prefix | Purpose | Examples |
|------|--------|---------|----------|
| **Actions** | `ACT-` | AI operations and transformations | `ACT-LEASE-CLASSIFY`, `ACT-BUILDER-001` |
| **Skills** | `SKL-` | Reusable workflow patterns and domain expertise | `SKL-CONTRACT-REVIEW`, `SKL-BUILDER-003` |
| **Tools** | `TL-` | Canvas manipulation and node operations | `TL-BUILDER-001` (addNode), `TL-BUILDER-003` (createEdge) |
| **Knowledge** | `KNW-` | Reference data, catalogs, and best practices | `KNW-SCOPE-CATALOG`, `KNW-BUILDER-004` |

### 17.4 Ownership Model

Scopes follow a two-tier ownership model enforced by `ScopeResolverService`:

| Prefix | Owner Type | Dataverse Field | Immutable | Operations Allowed |
|--------|------------|-----------------|-----------|-------------------|
| `SYS-` | System | `sprk_ownertype = 1` | Yes | Read, Save As |
| `CUST-` | Customer | `sprk_ownertype = 2` | No | Full CRUD |

**Ownership enforcement pattern:**

```csharp
// ScopeResolverService.UpdateScopeAsync
public async Task<AnalysisScope> UpdateScopeAsync(
    Guid id,
    UpdateScopeRequest request,
    CancellationToken ct)
{
    var existing = await GetScopeAsync(id, ct);

    // Ownership validation
    if (existing.IsImmutable || existing.OwnerType == OwnerType.System)
    {
        throw new InvalidOperationException(
            "Cannot update system scope. Use 'Save As' to create a customer copy.");
    }

    // Proceed with update...
}
```

### 17.5 Builder-Specific Scopes (23 Total)

The Playbook Builder uses 23 specialized scopes for AI-assisted playbook construction:

**Actions (ACT-BUILDER-*)**

| ID | Name | Purpose | Model |
|----|------|---------|-------|
| ACT-BUILDER-001 | Intent Classification | Parse user message into operation + parameters | GPT-4o-mini |
| ACT-BUILDER-002 | Node Configuration | Generate node config from requirements | GPT-4o |
| ACT-BUILDER-003 | Scope Selection | Select appropriate existing scope | GPT-4o-mini |
| ACT-BUILDER-004 | Scope Creation | Generate new scope definition | GPT-4o |
| ACT-BUILDER-005 | Build Plan Generation | Create structured plan from description | GPT-4o |

**Skills (SKL-BUILDER-*)**

| ID | Name | Purpose |
|----|------|---------|
| SKL-BUILDER-001 | Lease Analysis Pattern | How to build lease analysis playbooks |
| SKL-BUILDER-002 | Contract Review Pattern | Contract review playbook patterns |
| SKL-BUILDER-003 | Risk Assessment Pattern | Risk workflow patterns |
| SKL-BUILDER-004 | Node Type Guide | When to use each node type |
| SKL-BUILDER-005 | Scope Matching | Find/create appropriate scopes |

**Tools (TL-BUILDER-*)**

| ID | Name | Canvas Operation |
|----|------|------------------|
| TL-BUILDER-001 | addNode | Add node to canvas |
| TL-BUILDER-002 | removeNode | Remove node from canvas |
| TL-BUILDER-003 | createEdge | Connect two nodes |
| TL-BUILDER-004 | updateNodeConfig | Configure node properties |
| TL-BUILDER-005 | linkScope | Wire scope to node |
| TL-BUILDER-006 | createScope | Create new scope in Dataverse |
| TL-BUILDER-007 | searchScopes | Find existing scopes |
| TL-BUILDER-008 | autoLayout | Arrange canvas nodes |
| TL-BUILDER-009 | validateCanvas | Validate playbook structure |

**Knowledge (KNW-BUILDER-*)**

| ID | Name | Content |
|----|------|---------|
| KNW-BUILDER-001 | Scope Catalog | Available system scopes |
| KNW-BUILDER-002 | Reference Playbooks | Example playbook patterns |
| KNW-BUILDER-003 | Node Schema | Valid node configurations |
| KNW-BUILDER-004 | Best Practices | Design guidelines |

### 17.6 Intent Classification Pattern

The `AiPlaybookBuilderService` uses structured output for intent classification:

```csharp
// Intent classification with confidence scoring
public async Task<IntentResult> ClassifyIntentWithAiAsync(
    string message,
    string model,
    CancellationToken ct)
{
    var schema = new
    {
        operation = "string",    // add_node, remove_node, search_scopes, etc.
        parameters = "object",   // Operation-specific parameters
        confidence = "number",   // 0.0 to 1.0
        clarificationNeeded = "boolean",
        suggestedQuestions = "array"
    };

    var result = await _openAiClient.GetStructuredOutputAsync<IntentResult>(
        message,
        schema,
        model,  // GPT-4o or GPT-4o-mini
        ct);

    // Trigger clarification flow if confidence below threshold
    if (result.Confidence < 0.8 || result.ClarificationNeeded)
    {
        return new IntentResult
        {
            RequiresClarification = true,
            Questions = result.SuggestedQuestions ?? GenerateQuestions(result)
        };
    }

    return result;
}
```

### 17.7 Scope Search Pattern

Semantic scope search uses the same RAG infrastructure:

```csharp
// ScopeResolverService.SearchScopesAsync
public async Task<IReadOnlyList<ScopeSearchResult>> SearchScopesAsync(
    string query,
    ScopeSearchOptions options,
    CancellationToken ct)
{
    // Build filter for scope type and owner
    var filter = BuildScopeFilter(options.Types, options.OwnerTypes);

    // Vector search against scope embeddings
    var results = await _ragService.SearchAsync(
        query,
        new RagSearchOptions
        {
            TenantId = options.TenantId,
            Filter = filter,
            TopK = options.Limit,
            MinScore = options.MinSimilarity
        },
        ct);

    return results.Select(r => new ScopeSearchResult
    {
        ScopeId = r.DocumentId,
        Name = r.Metadata["name"],
        Type = r.Metadata["scopeType"],
        Similarity = r.Score,
        Description = r.Content
    }).ToList();
}
```

### 17.8 Save As / Extend Pattern

Lineage tracking for scope derivatives:

```csharp
// Save As - creates copy with basedon reference
public async Task<AnalysisScope> SaveScopeAsAsync(
    Guid sourceId,
    string newName,
    CancellationToken ct)
{
    var source = await GetScopeAsync(sourceId, ct);

    var newScope = new CreateScopeRequest
    {
        Name = $"CUST-{newName}",
        Type = source.Type,
        Configuration = source.Configuration,
        BasedOn = sourceId,           // Lineage tracking
        OwnerType = OwnerType.Customer
    };

    return await CreateScopeAsync(newScope, ct);
}

// Extend - creates child with parentscope reference (inherits updates)
public async Task<AnalysisScope> ExtendScopeAsync(
    Guid parentId,
    ExtendScopeRequest request,
    CancellationToken ct)
{
    var parent = await GetScopeAsync(parentId, ct);

    var childScope = new CreateScopeRequest
    {
        Name = $"CUST-{request.Name}",
        Type = parent.Type,
        Configuration = MergeConfigurations(parent.Configuration, request.Extensions),
        ParentScope = parentId,       // Inheritance tracking
        OwnerType = OwnerType.Customer
    };

    return await CreateScopeAsync(childScope, ct);
}
```

### 17.9 Dataverse Schema

Scope entities include ownership and lineage fields:

| Field | Type | Description |
|-------|------|-------------|
| `sprk_analysisactionid` | Unique Identifier | Primary key |
| `sprk_name` | Text | Scope name (with prefix) |
| `sprk_scopetype` | OptionSet | Action/Skill/Tool/Knowledge |
| `sprk_ownertype` | OptionSet | System (1) / Customer (2) |
| `sprk_isimmutable` | Boolean | True for SYS- scopes |
| `sprk_parentscope` | Lookup (self) | For Extend relationships |
| `sprk_basedon` | Lookup (self) | For Save As relationships |
| `sprk_configuration` | Multiline Text | JSON configuration |

### 17.10 Caching Strategy

Builder scope prompts use `IMemoryCache` per ADR-014:

```csharp
// AiPlaybookBuilderService - Prompt caching
private readonly IMemoryCache _promptCache;

public async Task<string> GetBuilderPromptAsync(string scopeId, CancellationToken ct)
{
    var cacheKey = $"builder:prompt:{scopeId}";

    if (!_promptCache.TryGetValue(cacheKey, out string prompt))
    {
        var scope = await _scopeResolver.GetScopeAsync(Guid.Parse(scopeId), ct);
        prompt = scope.Configuration.Prompt;

        _promptCache.Set(cacheKey, prompt, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        });
    }

    return prompt;
}
```

### 17.11 Performance Targets

| Operation | Target | Implementation |
|-----------|--------|----------------|
| Intent classification | < 2s | GPT-4o-mini with 500-1500ms typical |
| Scope search | < 1s | In-memory for stub, vector search production |
| Scope CRUD | < 500ms | Direct Dataverse operations |
| Prompt cache hit | < 10ms | IMemoryCache with 30-min TTL |

---

## Related Documents

- [SPAARKE-AI-STRATEGY.md](../../reference/architecture/SPAARKE-AI-STRATEGY.md) - Strategic context, Microsoft Foundry, use cases
- [ADR-001: Minimal API + BackgroundService](../../reference/adr/ADR-001-minimal-api-and-workers.md)
- [ADR-004: Async Job Contract](../../reference/adr/ADR-004-async-job-contract.md)
- [ADR-009: Redis-first Caching](../../reference/adr/ADR-009-caching-redis-first.md)
- [BFF API Patterns](../architecture/sdap-bff-api-patterns.md)
- [AI Search & Visualization Module](../../projects/ai-azure-search-module/README.md) - Project documentation
- [Playbook Builder Full-Screen Setup](PLAYBOOK-BUILDER-FULLSCREEN-SETUP.md) - PCF control deployment and AI Assistant usage

---

*Document Owner: Spaarke Engineering*
*Last Updated: January 19, 2026*
*R3 Updates: RAG Deployment Models (Section 8), Knowledge Index Schema (Section 9.2), Tool Framework (Section 10), Playbook System (Section 11), Export Services (Section 12)*
*2026-01-12: Document Relationship Visualization (Section 16)*
*2026-01-19: Playbook Builder AI Assistant Scope Patterns (Section 17)*
