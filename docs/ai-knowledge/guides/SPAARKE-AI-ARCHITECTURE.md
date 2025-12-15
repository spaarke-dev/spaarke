# Spaarke AI Architecture

> **Version**: 1.4
> **Date**: December 15, 2025
> **Status**: Draft  
> **Author**: Spaarke Engineering  
> **Related**: [SPAARKE-AI-STRATEGY.md](../../reference/architecture/SPAARKE-AI-STRATEGY.md)

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
| **Scope Resolution** | `ScopeResolverService` (Actions, Skills, Knowledge from Dataverse) |
| **Prompt Construction** | `AnalysisContextBuilder` (builds system/user prompts from scopes) |
| **Tool Framework** | `IAnalysisToolHandler` (extensible tool execution) |
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
| **ADR-007** | SpeFileStore facade | AI services use `SpeFileStore` for document access | ❌ Inject `GraphServiceClient` into AI services |
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
- [ ] Document access via `SpeFileStore`, not direct Graph calls
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

## 6. Scope System Architecture

The Scope System enables configurable AI analysis through Dataverse-managed entities that control prompt construction.

### 6.1 Scope Entities

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          Scope System                                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │ sprk_analysis   │  │ sprk_analysis   │  │ sprk_analysis   │             │
│  │ action          │  │ skill           │  │ knowledge       │             │
│  │ ─────────────── │  │ ─────────────── │  │ ─────────────── │             │
│  │ SystemPrompt    │  │ PromptFragment  │  │ Content         │             │
│  │ (AI persona)    │  │ (instructions)  │  │ (reference)     │             │
│  │ SortOrder       │  │ Category        │  │ Type (enum)     │             │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘             │
│           │                    │                    │                       │
│           └────────────────────┼────────────────────┘                       │
│                                │                                            │
│                                ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │              AnalysisContextBuilder                                  │   │
│  │  ─────────────────────────────────────────────────────────────────   │   │
│  │  BuildSystemPrompt(action, skills[])                                 │   │
│  │  BuildUserPromptAsync(documentText, knowledge[])                     │   │
│  │  BuildContinuationPrompt(history[], userMessage, workingDoc)         │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                │                                            │
│                                ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │              OpenAI API                                              │   │
│  │  System: {Action.SystemPrompt} + ## Instructions + {Skills[]}        │   │
│  │  User: # Document to Analyze + # Reference Materials + {Knowledge[]} │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 6.2 KnowledgeType Enum

Knowledge sources are typed to control inclusion in prompts. Values match Dataverse `sprk_type` option set:

```csharp
// Services/Ai/IScopeResolverService.cs
public enum KnowledgeType
{
    /// <summary>Reference document (inline if has content).</summary>
    Document = 100000000,

    /// <summary>Business rules/guidelines (always inline).</summary>
    Rule = 100000001,

    /// <summary>Template documents (always inline).</summary>
    Template = 100000002,

    /// <summary>RAG index reference (async retrieval, not inline).</summary>
    RagIndex = 100000003
}
```

| Type | Dataverse Value | Prompt Behavior |
|------|-----------------|-----------------|
| Document | 100000000 | Included inline if `Content` is non-empty |
| Rule | 100000001 | Always included inline with `(Rule)` label |
| Template | 100000002 | Always included inline with `(Template)` label |
| RagIndex | 100000003 | Excluded from inline; requires async RAG retrieval |

### 6.3 AnalysisContextBuilder

The `AnalysisContextBuilder` constructs prompts from scope components:

```csharp
// Services/Ai/AnalysisContextBuilder.cs
public class AnalysisContextBuilder : IAnalysisContextBuilder
{
    /// <summary>
    /// Build system prompt: Action.SystemPrompt + Skills as ## Instructions
    /// </summary>
    public string BuildSystemPrompt(AnalysisAction action, AnalysisSkill[] skills);

    /// <summary>
    /// Build user prompt: Document + Knowledge (Rule, Template, Document with content)
    /// RAG knowledge excluded - requires async retrieval
    /// </summary>
    public Task<string> BuildUserPromptAsync(
        string documentText,
        AnalysisKnowledge[] knowledge,
        CancellationToken cancellationToken);

    /// <summary>
    /// Build continuation prompt for chat: Current analysis + History + New request
    /// </summary>
    public string BuildContinuationPrompt(
        ChatMessageModel[] history,
        string userMessage,
        string currentWorkingDocument);
}
```

**Prompt Output Format**:

```
SYSTEM PROMPT:
─────────────
{Action.SystemPrompt}

## Instructions
- {Skill[0].PromptFragment}
- {Skill[1].PromptFragment}
...

## Output Format
Provide your analysis in Markdown format with appropriate headings and structure.

USER PROMPT:
────────────
# Document to Analyze
{documentText}

# Reference Materials
## {Knowledge[0].Name} (Rule)
{Knowledge[0].Content}

## {Knowledge[1].Name} (Template)
{Knowledge[1].Content}

---
Please analyze the document above according to the instructions.
```

### 6.4 Tool Handler Framework

Extensible tool system for specialized AI operations:

```csharp
// Services/Ai/Tools/IAnalysisToolHandler.cs
public interface IAnalysisToolHandler
{
    string ToolName { get; }
    ToolType Type { get; }
    Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct);
}

public enum ToolType
{
    EntityExtractor = 0,
    ClauseAnalyzer = 1,
    Custom = 2
}
```

**Built-in Tools**:
| Tool | Type | Purpose |
|------|------|---------|
| EntityExtractor | 0 | Extract structured entities from text |
| ClauseAnalyzer | 1 | Analyze contract clauses |
| DocumentClassifier | Custom | Classify document types |

### 6.5 Playbook System (Phase 4)

Playbooks are pre-configured scope combinations stored in `sprk_analysisplaybook`:

```
┌─────────────────────────────────────────────────────────────────┐
│                 sprk_analysisplaybook                            │
├─────────────────────────────────────────────────────────────────┤
│  Name: "Contract Review Playbook"                                │
│  Description: "Full contract analysis with risk assessment"      │
│  IsPublic: true                                                  │
│                                                                  │
│  N:N Relationships:                                              │
│  ├── Actions → [Review Agreement]                                │
│  ├── Skills → [Identify Terms, Extract Dates, Identify Risks]   │
│  ├── Knowledge → [Company Policies, Legal Reference]            │
│  └── Tools → [EntityExtractor, ClauseAnalyzer]                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 7. Background Processing

### 7.1 AI Indexing Job Handler

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

## 8. Configuration

### 8.1 AiOptions

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

### 8.2 appsettings.json

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

### 8.3 Program.cs Registration

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

## 9. Authorization Filter

### 9.1 AiAuthorizationFilter

```csharp
// Api/Filters/AiAuthorizationFilter.cs
namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Endpoint filter for AI-specific authorization.
/// ADR-008 compliant: Uses endpoint filter pattern.
/// </summary>
public class AiAuthorizationFilter : IEndpointFilter
{
    private readonly ILogger<AiAuthorizationFilter> _logger;

    public AiAuthorizationFilter(ILogger<AiAuthorizationFilter> logger)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        // 1. Extract customer ID from token
        var customerId = user.FindFirst("tenant_id")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        if (string.IsNullOrEmpty(customerId))
        {
            _logger.LogWarning("AI request rejected: No customer ID in token");
            return Results.Problem(
                detail: "Customer context not found in token",
                statusCode: 401);
        }

        // 2. Check AI feature license (could be a claim or Dataverse lookup)
        var hasAiLicense = user.HasClaim("feature", "ai")
            || await CheckAiLicenseAsync(httpContext, customerId);

        if (!hasAiLicense)
        {
            _logger.LogWarning("AI request rejected: No AI license for customer {CustomerId}", customerId);
            return Results.Problem(
                detail: "AI features not enabled for this customer",
                statusCode: 403);
        }

        // 3. Store customer ID for downstream use
        httpContext.Items["CustomerId"] = customerId;

        // 4. Log AI usage for billing
        _logger.LogInformation(
            "AI request from customer {CustomerId}, user {UserId}, endpoint {Endpoint}",
            customerId,
            user.FindFirst("sub")?.Value,
            httpContext.Request.Path);

        return await next(context);
    }

    private Task<bool> CheckAiLicenseAsync(HttpContext context, string customerId)
    {
        // TODO: Implement Dataverse lookup for AI feature license
        // For now, allow all customers
        return Task.FromResult(true);
    }
}
```

---

## 10. Index Schema

### 10.1 Azure AI Search Index Definition

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

---

## 11. Testing

### 11.1 Unit Test Example

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

## 12. Deployment Checklist

### 12.1 Pre-Deployment

- [ ] Azure OpenAI resource provisioned with required model deployments
- [ ] Azure AI Search resource provisioned with index created
- [ ] Document Intelligence resource provisioned
- [ ] Key Vault secrets configured for all AI service keys
- [ ] Service Bus queue `ai-indexing` created
- [ ] Rate limiting configuration reviewed per environment

### 12.2 Configuration

- [ ] `appsettings.{Environment}.json` updated with AI configuration
- [ ] Key Vault references validated
- [ ] Redis instance sized appropriately for embedding cache

### 12.3 Testing

- [ ] Unit tests passing
- [ ] Integration tests with AI services validated
- [ ] Customer isolation verified (no cross-tenant data leakage)
- [ ] Rate limiting tested under load

---

## Related Documents

- [SPAARKE-AI-STRATEGY.md](../../reference/architecture/SPAARKE-AI-STRATEGY.md) - Strategic context, Microsoft Foundry, use cases
- [ADR-001: Minimal API + BackgroundService](../../reference/adr/ADR-001-minimal-api-and-workers.md)
- [ADR-004: Async Job Contract](../../reference/adr/ADR-004-async-job-contract.md)
- [ADR-009: Redis-first Caching](../../reference/adr/ADR-009-caching-redis-first.md)
- [BFF API Patterns](../architecture/sdap-bff-api-patterns.md)

---

*Document Owner: Spaarke Engineering*  
*Last Updated: December 15, 2025*
