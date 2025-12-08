# AI Document Summary Specification

> **Version**: 1.0  
> **Date**: December 5, 2025  
> **Status**: Draft  
> **Author**: Spaarke Engineering  
> **Related ADRs**: ADR-001, ADR-004, ADR-007, ADR-008, ADR-009, ADR-010, ADR-013

---

## 1. Overview

### 1.1 Feature Summary

Add **AI-powered document summarization** to Spaarke. When users upload documents via the Universal Quick Create dialog, an AI summary is automatically generated and stored in the `sprk_document` record.

### 1.2 User Experience

**Multiple File Upload with Summary Carousel:**

The current SDAP flow supports **up to 10 files** simultaneously. The AI Summary panel displays summaries in a carousel, allowing users to navigate between files:

```
┌─────────────────────────────────────────────────────────────┐
│  + Add Documents                                       [X]  │
├─────────────────────────────────────────────────────────────┤
│  Files: 📄 Report.pdf ✓  📄 Summary.docx ✓  📄 Data.csv ✓   │
│  Document Type: [Financial Report ▼ ]                       │
│                                                             │
│  [☑] Generate AI Summary                                    │
│                                                             │
│  ─────────────────────────────────────────────────────────  │
│                                                             │
│  📝 AI Summaries                           [◀] 1 of 3 [▶]   │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ 📄 Report.pdf                                       │    │
│  │                                                     │    │
│  │ This quarterly report covers Q4 2025 performance    │    │
│  │ including revenue growth of 12%, expansion into     │    │
│  │ three new markets, and the acquisition of...█       │    │
│  │                                                     │    │
│  │ ⏳ Generating...                                    │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                             │
│  Status: 1 complete, 2 generating                           │
│  ℹ️ You can close - summaries will complete in background   │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│                      [Upload & Create Documents]  [Cancel]  │
└─────────────────────────────────────────────────────────────┘
```

**Single File View:**
```
┌─────────────────────────────────────────────────────────────┐
│  + Add Document                                        [X]  │
├─────────────────────────────────────────────────────────────┤
│  File: 📄 Quarterly Report Q4.pdf  ✓ Uploaded               │
│  Document Type: [Financial Report ▼ ]                       │
│                                                             │
│  [☑] Generate AI Summary                                    │
│                                                             │
│  ─────────────────────────────────────────────────────────  │
│                                                             │
│  📝 AI Summary                                              │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ This quarterly report covers Q4 2025 performance    │    │
│  │ including revenue growth of 12%, expansion into     │    │
│  │ three new markets, and the acquisition of...█       │    │
│  │                                                     │    │
│  │ ⏳ Generating...                                    │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                             │
│  ℹ️ You can close - summary will complete in background     │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│                       [Upload & Create Document]  [Cancel]  │
└─────────────────────────────────────────────────────────────┘
```

**Key UX Points:**
- **Opt-out checkbox**: "Generate AI Summary" - checked by default, user can uncheck to skip
- Supports **1-10 files** per upload (matching existing SDAP limits)
- **Carousel navigation** when multiple files uploaded
- Shows aggregate status: "2 complete, 1 generating, 1 pending"
- User sees AI summary generating in real-time (streaming)
- User can close dialog at any time - processing continues in background
- Each summary stored on its respective `sprk_document` record
- Status field shows Pending/Completed/Failed per document
- Summary panel hidden when checkbox is unchecked

### 1.3 Scope

**In Scope (MVP):**
- Automatic summarization during SDAP upload flow
- **User opt-out**: "Generate AI Summary" checkbox (default: checked)
- **Multi-file support** with carousel UI (1-10 files)
- Streaming display in Universal Quick Create dialog
- Background job fallback when user closes dialog
- Support for PDF, DOCX, TXT, MD files
- Summary stored in `sprk_filesummary` field (per document)
- **Configurable file type support** (enable/disable per extension)
- **Configurable model selection** (e.g., gpt-4.1-mini, gpt-4.1, gpt-5)
- **Microsoft Foundry integration** - Customers can manage AI resources via [ai.azure.com](https://ai.azure.com)

**Out of Scope (MVP):**
- **Image summarization** (PNG, JPG, TIFF) - Phase 2, requires multimodal model (GPT-4.1 or GPT-5 with vision)
- On-demand re-summarization (future)
- Configurable summary length/style (future)
- Multi-language summaries (future)
- Vectorization/RAG indexing (future feature)

---

## 2. Architecture

### 2.1 BFF Orchestration Pattern

This feature follows the **BFF orchestration pattern** - AI endpoints live in `Sprk.Bff.Api` alongside existing SDAP endpoints, using shared infrastructure:

> **Deployment Models:** This feature supports both Spaarke-hosted (Model 1) and Customer-hosted BYOK (Model 2) deployments. Customers using BYOK can manage their Azure OpenAI resources directly through [Microsoft Foundry portal](https://ai.azure.com). See [SPAARKE-AI-STRATEGY.md](../../docs/reference/architecture/SPAARKE-AI-STRATEGY.md) for details.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              Sprk.Bff.Api                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   POST /api/ai/summarize/stream    (new)                                    │
│   POST /api/ai/summarize/enqueue   (new)                                    │
│         │                                                                   │
│         ▼                                                                   │
│   ┌─────────────────────────────────────────────────────────────────────┐  │
│   │  SummarizeService (new)                                             │  │
│   │  ──────────────────────────────────────────────────────────────     │  │
│   │  1. Get file from SPE via SpeFileStore (existing)                   │  │
│   │  2. Extract text via TextExtractorService (new)                     │  │
│   │  3. Generate summary via OpenAiClient (new, shared)                 │  │
│   │  4. Update Dataverse via DataverseClient (existing pattern)         │  │
│   └─────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│   Shared Infrastructure:                                                    │
│   • UAC (auth) • SpeFileStore • Redis • Service Bus                        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 File Structure

```
src/server/api/Sprk.Bff.Api/
├── Api/
│   └── Ai/
│       └── SummarizeEndpoints.cs        # NEW: Summarization endpoints
│
├── Services/
│   └── Ai/
│       ├── SummarizeService.cs          # NEW: Orchestrates summarization
│       ├── TextExtractorService.cs      # NEW: Text extraction (native + Doc Intel)
│       └── OpenAiClient.cs              # NEW: Azure OpenAI wrapper (shared)
│
├── Jobs/
│   └── Handlers/
│       └── SummarizeJobHandler.cs       # NEW: Background job handler
│
├── Models/
│   └── Ai/
│       ├── SummarizeRequest.cs          # NEW
│       ├── SummarizeResponse.cs         # NEW
│       └── SummarizeJobContract.cs      # NEW
│
└── Configuration/
    └── AiOptions.cs                     # NEW: AI configuration

src/client/pcf/
└── UniversalQuickCreate/
    └── control/
        └── components/
            ├── AiSummaryCarousel.tsx    # NEW: Multi-file carousel UI
            └── AiSummaryPanel.tsx       # NEW: Single document summary UI
```

---

## 3. API Design

### 3.1 Streaming Endpoint (Single Document)

```
POST /api/ai/summarize/stream
Content-Type: application/json
Accept: text/event-stream

{
  "documentId": "guid",
  "driveId": "string",
  "itemId": "string"
}

Response: Server-Sent Events (SSE)
data: {"content": "This quarterly", "done": false}
data: {"content": " report covers", "done": false}
data: {"content": "...", "done": false}
data: {"content": "", "done": true, "summary": "Full summary text"}
```

### 3.2 Background Endpoint (Single Document)

```
POST /api/ai/summarize/enqueue
Content-Type: application/json

{
  "documentId": "guid",
  "driveId": "string",
  "itemId": "string"
}

Response: 202 Accepted
{
  "jobId": "guid"
}
```

### 3.3 Batch Enqueue Endpoint (Multiple Documents)

```
POST /api/ai/summarize/enqueue-batch
Content-Type: application/json

{
  "documents": [
    { "documentId": "guid1", "driveId": "string", "itemId": "string" },
    { "documentId": "guid2", "driveId": "string", "itemId": "string" },
    { "documentId": "guid3", "driveId": "string", "itemId": "string" }
  ]
}

Response: 202 Accepted
{
  "jobIds": ["guid1", "guid2", "guid3"]
}
```

**Multi-File Client Strategy:**

When multiple files are uploaded, the client:
1. Opens **parallel SSE streams** for each document (up to 3 concurrent)
2. Displays results in carousel as they complete
3. If user closes dialog, calls batch enqueue for remaining documents
4. Rate limiting: Max 3 concurrent streams per user

### 3.4 Endpoint Implementation

```csharp
// Api/Ai/SummarizeEndpoints.cs
namespace Sprk.Bff.Api.Api.Ai;

public static class SummarizeEndpoints
{
    public static IEndpointRouteBuilder MapSummarizeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/summarize")
            .RequireAuthorization()
            .AddEndpointFilter<AiAuthorizationFilter>()
            .WithTags("AI");

        group.MapPost("/stream", StreamSummarizeAsync)
            .WithName("AiSummarizeStream")
            .Produces(200, contentType: "text/event-stream");

        group.MapPost("/enqueue", EnqueueSummarizeAsync)
            .WithName("AiSummarizeEnqueue")
            .Produces<EnqueueResponse>(202);

        return app;
    }

    private static async Task StreamSummarizeAsync(
        SummarizeRequest request,
        SummarizeService summarizeService,
        HttpContext context,
        CancellationToken ct)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        try
        {
            await foreach (var chunk in summarizeService.SummarizeStreamingAsync(request, ct))
            {
                var json = JsonSerializer.Serialize(chunk);
                await context.Response.WriteAsync($"data: {json}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected - enqueue for background completion
            await summarizeService.EnqueueAsync(request, ct);
        }
    }

    private static async Task<IResult> EnqueueSummarizeAsync(
        SummarizeRequest request,
        SummarizeService summarizeService,
        CancellationToken ct)
    {
        var jobId = await summarizeService.EnqueueAsync(request, ct);
        return Results.Accepted(value: new EnqueueResponse(jobId));
    }
}
```

---

## 4. Service Implementation

### 4.1 SummarizeService

```csharp
// Services/Ai/SummarizeService.cs
namespace Sprk.Bff.Api.Services.Ai;

public class SummarizeService
{
    private readonly SpeFileStore _fileStore;
    private readonly TextExtractorService _textExtractor;
    private readonly OpenAiClient _openAi;
    private readonly IDataverseClient _dataverse;
    private readonly IServiceBusClient _serviceBus;
    private readonly IDistributedCache _cache;
    private readonly ILogger<SummarizeService> _logger;

    public SummarizeService(
        SpeFileStore fileStore,
        TextExtractorService textExtractor,
        OpenAiClient openAi,
        IDataverseClient dataverse,
        IServiceBusClient serviceBus,
        IDistributedCache cache,
        ILogger<SummarizeService> logger)
    {
        _fileStore = fileStore;
        _textExtractor = textExtractor;
        _openAi = openAi;
        _dataverse = dataverse;
        _serviceBus = serviceBus;
        _cache = cache;
        _logger = logger;
    }

    public async IAsyncEnumerable<SummarizeChunk> SummarizeStreamingAsync(
        SummarizeRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 1. Download file from SPE
        var file = await _fileStore.DownloadAsync(request.DriveId, request.ItemId, ct);
        
        // 2. Extract text
        var text = await _textExtractor.ExtractAsync(file.Stream, file.FileName, ct);
        
        if (string.IsNullOrWhiteSpace(text))
        {
            yield return new SummarizeChunk("Unable to extract text from this file.", Done: true);
            await UpdateStatusAsync(request.DocumentId, SummaryStatus.NotSupported, ct);
            yield break;
        }

        // 3. Stream summary from OpenAI
        var fullSummary = new StringBuilder();
        var prompt = BuildPrompt(text);
        
        await foreach (var chunk in _openAi.StreamCompletionAsync(prompt, ct))
        {
            fullSummary.Append(chunk);
            yield return new SummarizeChunk(chunk, Done: false);
        }

        // 4. Save to Dataverse
        var summary = fullSummary.ToString();
        await SaveSummaryAsync(request.DocumentId, summary, ct);
        
        yield return new SummarizeChunk(string.Empty, Done: true, Summary: summary);
    }

    public async Task<string> EnqueueAsync(SummarizeRequest request, CancellationToken ct)
    {
        var job = new SummarizeJobContract
        {
            JobId = Guid.NewGuid().ToString(),
            JobType = "ai-summarize",
            DocumentId = request.DocumentId,
            DriveId = request.DriveId,
            ItemId = request.ItemId
        };

        await _serviceBus.SendAsync(job, ct);
        await UpdateStatusAsync(request.DocumentId, SummaryStatus.Pending, ct);
        
        return job.JobId;
    }

    private async Task SaveSummaryAsync(Guid documentId, string summary, CancellationToken ct)
    {
        await _dataverse.UpdateAsync("sprk_document", documentId, new
        {
            sprk_filesummary = summary,
            sprk_filesummarystatus = (int)SummaryStatus.Completed,
            sprk_filesummarydate = DateTime.UtcNow
        }, ct);
    }

    private async Task UpdateStatusAsync(Guid documentId, SummaryStatus status, CancellationToken ct)
    {
        await _dataverse.UpdateAsync("sprk_document", documentId, new
        {
            sprk_filesummarystatus = (int)status
        }, ct);
    }

    private static string BuildPrompt(string documentText)
    {
        // Truncate if too long (gpt-4.1-mini context: 128K tokens)
        var maxChars = 100_000;
        var text = documentText.Length > maxChars 
            ? documentText[..maxChars] + "\n\n[Document truncated...]"
            : documentText;

        return $"""
            You are a document summarization assistant. Generate a clear, concise 
            summary of the following document. The summary should:
            
            - Be 2-4 paragraphs (approximately 200-400 words)
            - Capture the main points and key information
            - Be written in professional business language
            - Not start with "This document" or "The document"
            
            Document:
            {text}
            
            Summary:
            """;
    }
}
```

### 4.2 TextExtractorService

```csharp
// Services/Ai/TextExtractorService.cs
namespace Sprk.Bff.Api.Services.Ai;

public class TextExtractorService
{
    private readonly DocumentAnalysisClient _docIntelClient;
    private readonly IDistributedCache _cache;
    private readonly ILogger<TextExtractorService> _logger;

    private static readonly HashSet<string> NativeTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".csv", ".xml", ".html"
    };

    private static readonly HashSet<string> DocIntelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".doc", ".xlsx", ".pptx"
    };

    public async Task<string> ExtractAsync(Stream fileStream, string fileName, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName);

        if (NativeTextExtensions.Contains(extension))
        {
            return await ExtractNativeTextAsync(fileStream, ct);
        }

        if (DocIntelExtensions.Contains(extension))
        {
            return await ExtractViaDocIntelAsync(fileStream, ct);
        }

        _logger.LogWarning("Unsupported file type for text extraction: {Extension}", extension);
        return string.Empty;
    }

    private static async Task<string> ExtractNativeTextAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    private async Task<string> ExtractViaDocIntelAsync(Stream stream, CancellationToken ct)
    {
        var operation = await _docIntelClient.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-read",
            stream,
            cancellationToken: ct);

        var result = operation.Value;
        return result.Content;
    }
}
```

### 4.3 OpenAiClient

```csharp
// Services/Ai/OpenAiClient.cs
namespace Sprk.Bff.Api.Services.Ai;

public class OpenAiClient
{
    private readonly OpenAIClient _client;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiClient> _logger;

    public OpenAiClient(OpenAIClient client, IOptions<AiOptions> options, ILogger<OpenAiClient> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamCompletionAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var chatOptions = new ChatCompletionsOptions(_options.SummarizeModel, new[]
        {
            new ChatRequestUserMessage(prompt)
        })
        {
            MaxTokens = 1000,
            Temperature = 0.3f
        };

        var response = await _client.GetChatCompletionsStreamingAsync(chatOptions, ct);

        await foreach (var update in response.WithCancellation(ct))
        {
            if (!string.IsNullOrEmpty(update.ContentUpdate))
            {
                yield return update.ContentUpdate;
            }
        }
    }

    public async Task<string> GetCompletionAsync(string prompt, CancellationToken ct)
    {
        var chatOptions = new ChatCompletionsOptions(_options.SummarizeModel, new[]
        {
            new ChatRequestUserMessage(prompt)
        })
        {
            MaxTokens = 1000,
            Temperature = 0.3f
        };

        var response = await _client.GetChatCompletionsAsync(chatOptions, ct);
        return response.Value.Choices[0].Message.Content;
    }
}
```

---

## 5. Background Job Handler

```csharp
// Jobs/Handlers/SummarizeJobHandler.cs
namespace Sprk.Bff.Api.Jobs.Handlers;

public class SummarizeJobHandler : IJobHandler<SummarizeJobContract>
{
    private readonly SummarizeService _summarizeService;
    private readonly ILogger<SummarizeJobHandler> _logger;

    public async Task HandleAsync(SummarizeJobContract job, CancellationToken ct)
    {
        _logger.LogInformation("Processing summarize job {JobId} for document {DocumentId}",
            job.JobId, job.DocumentId);

        var request = new SummarizeRequest(job.DocumentId, job.DriveId, job.ItemId);
        
        // Use non-streaming version for background processing
        await foreach (var _ in _summarizeService.SummarizeStreamingAsync(request, ct))
        {
            // Consume stream - final chunk saves to Dataverse
        }

        _logger.LogInformation("Completed summarize job {JobId}", job.JobId);
    }
}
```

---

## 6. Client Integration

### 6.1 Multi-File AiSummaryCarousel Component

The panel supports **multiple documents** with carousel navigation:

```tsx
// src/client/pcf/UniversalQuickCreate/control/components/AiSummaryCarousel.tsx

interface DocumentSummary {
  documentId: string;
  driveId: string;
  itemId: string;
  fileName: string;
  status: 'pending' | 'streaming' | 'complete' | 'error' | 'not-supported';
  summary: string;
  error?: string;
}

interface AiSummaryCarouselProps {
  documents: Array<{
    documentId: string;
    driveId: string;
    itemId: string;
    fileName: string;
  }>;
  maxConcurrent?: number; // Default: 3
  onAllComplete?: () => void;
}

export const AiSummaryCarousel: React.FC<AiSummaryCarouselProps> = ({
  documents,
  maxConcurrent = 3,
  onAllComplete
}) => {
  const [summaries, setSummaries] = useState<Map<string, DocumentSummary>>(new Map());
  const [currentIndex, setCurrentIndex] = useState(0);
  const activeStreams = useRef(0);

  // Initialize all documents as pending
  useEffect(() => {
    const initial = new Map<string, DocumentSummary>();
    documents.forEach(doc => {
      initial.set(doc.documentId, {
        ...doc,
        status: 'pending',
        summary: ''
      });
    });
    setSummaries(initial);
  }, [documents]);

  // Process documents with concurrency limit
  useEffect(() => {
    const processNext = async () => {
      const pending = Array.from(summaries.values())
        .filter(s => s.status === 'pending');
      
      if (pending.length === 0 || activeStreams.current >= maxConcurrent) {
        return;
      }

      const doc = pending[0];
      activeStreams.current++;
      
      try {
        await streamSummary(doc, (chunk, done, fullSummary) => {
          setSummaries(prev => {
            const updated = new Map(prev);
            const current = updated.get(doc.documentId)!;
            updated.set(doc.documentId, {
              ...current,
              status: done ? 'complete' : 'streaming',
              summary: done ? fullSummary! : current.summary + chunk
            });
            return updated;
          });
        });
      } catch (error) {
        setSummaries(prev => {
          const updated = new Map(prev);
          const current = updated.get(doc.documentId)!;
          updated.set(doc.documentId, {
            ...current,
            status: 'error',
            error: error instanceof Error ? error.message : 'Unknown error'
          });
          return updated;
        });
      } finally {
        activeStreams.current--;
        processNext(); // Start next document
      }
    };

    processNext();
  }, [summaries, maxConcurrent]);

  // Check if all complete
  useEffect(() => {
    const all = Array.from(summaries.values());
    const allDone = all.length > 0 && all.every(s => 
      s.status === 'complete' || s.status === 'error' || s.status === 'not-supported'
    );
    if (allDone) {
      onAllComplete?.();
    }
  }, [summaries, onAllComplete]);

  const currentDoc = Array.from(summaries.values())[currentIndex];
  const statusCounts = getStatusCounts(summaries);

  return (
    <div className="ai-summary-carousel">
      {/* Header with navigation */}
      <div className="carousel-header">
        <DocumentTextRegular />
        <span>AI Summaries</span>
        
        {documents.length > 1 && (
          <div className="carousel-nav">
            <Button 
              icon={<ChevronLeftRegular />} 
              onClick={() => setCurrentIndex(i => Math.max(0, i - 1))}
              disabled={currentIndex === 0}
            />
            <span>{currentIndex + 1} of {documents.length}</span>
            <Button 
              icon={<ChevronRightRegular />} 
              onClick={() => setCurrentIndex(i => Math.min(documents.length - 1, i + 1))}
              disabled={currentIndex === documents.length - 1}
            />
          </div>
        )}
      </div>

      {/* Current document summary */}
      {currentDoc && (
        <div className="carousel-content">
          <div className="document-name">
            <DocumentRegular />
            <span>{currentDoc.fileName}</span>
            <StatusBadge status={currentDoc.status} />
          </div>
          
          <div className="summary-text">
            {currentDoc.status === 'pending' && <Spinner size="tiny" label="Waiting..." />}
            {currentDoc.status === 'streaming' && (
              <>
                {currentDoc.summary}
                <span className="cursor">█</span>
              </>
            )}
            {currentDoc.status === 'complete' && currentDoc.summary}
            {currentDoc.status === 'error' && (
              <MessageBar intent="error">{currentDoc.error}</MessageBar>
            )}
            {currentDoc.status === 'not-supported' && (
              <MessageBar intent="warning">
                This file type is not supported for summarization.
              </MessageBar>
            )}
          </div>
        </div>
      )}

      {/* Aggregate status */}
      <div className="carousel-footer">
        <span className="status-summary">
          {statusCounts.complete} complete
          {statusCounts.streaming > 0 && `, ${statusCounts.streaming} generating`}
          {statusCounts.pending > 0 && `, ${statusCounts.pending} pending`}
          {statusCounts.error > 0 && `, ${statusCounts.error} failed`}
        </span>
        <InfoRegular />
        <span>You can close - summaries will complete in background</span>
      </div>
    </div>
  );
};
```

### 6.2 Single Document AiSummaryPanel (Simplified)

For single-file uploads, a simpler component:

```tsx
// src/client/pcf/UniversalQuickCreate/control/components/AiSummaryPanel.tsx

interface AiSummaryPanelProps {
  documentId: string;
  driveId: string;
  itemId: string;
  fileName: string;
  onComplete?: (summary: string) => void;
}

export const AiSummaryPanel: React.FC<AiSummaryPanelProps> = ({
  documentId,
  driveId,
  itemId,
  fileName,
  onComplete
}) => {
  const [status, setStatus] = useState<'idle' | 'loading' | 'streaming' | 'complete' | 'error'>('idle');
  const [summary, setSummary] = useState('');
  const [error, setError] = useState<string | null>(null);

  const startSummarization = useCallback(async () => {
    setStatus('loading');
    setSummary('');
    
    try {
      const response = await fetch('/api/ai/summarize/stream', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ documentId, driveId, itemId })
      });

      if (!response.ok) throw new Error('Failed to start summarization');
      
      setStatus('streaming');
      const reader = response.body?.getReader();
      const decoder = new TextDecoder();

      while (reader) {
        const { done, value } = await reader.read();
        if (done) break;

        const text = decoder.decode(value);
        const lines = text.split('\n').filter(line => line.startsWith('data: '));
        
        for (const line of lines) {
          const json = JSON.parse(line.slice(6));
          if (json.done) {
            setStatus('complete');
            onComplete?.(json.summary);
          } else {
            setSummary(prev => prev + json.content);
          }
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
      setStatus('error');
    }
  }, [documentId, driveId, itemId, onComplete]);

  // Auto-start when props are available
  useEffect(() => {
    if (documentId && driveId && itemId && status === 'idle') {
      startSummarization();
    }
  }, [documentId, driveId, itemId, status, startSummarization]);

  return (
    <div className="ai-summary-panel">
      <div className="ai-summary-header">
        <DocumentTextRegular />
        <span>AI Summary</span>
      </div>
      
      <div className="ai-summary-content">
        {status === 'loading' && <Spinner label="Preparing..." />}
        
        {(status === 'streaming' || status === 'complete') && (
          <div className="summary-text">
            {summary}
            {status === 'streaming' && <span className="cursor">█</span>}
          </div>
        )}
        
        {status === 'error' && (
          <MessageBar intent="error">{error}</MessageBar>
        )}
      </div>
      
      {status === 'streaming' && (
        <div className="ai-summary-footer">
          <InfoRegular />
          <span>You can close this dialog. Summary will complete in background.</span>
        </div>
      )}
    </div>
  );
};
```

### 6.3 Integration in DocumentUploadForm

```tsx
// Modified: UniversalQuickCreate/control/components/DocumentUploadForm.tsx

export const DocumentUploadForm: React.FC<Props> = ({ onClose, onSuccess }) => {
  const [uploadedDocs, setUploadedDocs] = useState<UploadedDocument[]>([]);
  const [generateSummary, setGenerateSummary] = useState(true); // Default: checked

  const handleUploadComplete = (docs: UploadedDocument[]) => {
    setUploadedDocs(docs);
  };

  return (
    <div className="document-upload-form">
      {/* Existing upload UI */}
      <FileUploadSection onUploadComplete={handleUploadComplete} />
      <DocumentTypeField />
      <DescriptionField />
      
      {/* AI Summary Opt-in Checkbox */}
      <Checkbox
        label="Generate AI Summary"
        checked={generateSummary}
        onChange={(_, data) => setGenerateSummary(data.checked ?? false)}
      />
      
      {/* AI Summary Panel - only shown if checkbox is checked */}
      {generateSummary && uploadedDocs.length > 0 && (
        uploadedDocs.length === 1 ? (
          <AiSummaryPanel
            documentId={uploadedDocs[0].documentId}
            driveId={uploadedDocs[0].driveId}
            itemId={uploadedDocs[0].itemId}
            fileName={uploadedDocs[0].fileName}
          />
        ) : (
          <AiSummaryCarousel
            documents={uploadedDocs}
            maxConcurrent={3}
          />
        )
      )}
      
      <div className="form-actions">
        <DefaultButton onClick={onClose}>Cancel</DefaultButton>
        <PrimaryButton onClick={handleSubmit}>
          Upload & Create Document{uploadedDocs.length > 1 ? 's' : ''}
        </PrimaryButton>
      </div>
    </div>
  );
};
```

---

## 7. Dataverse Schema

### 7.1 New Fields on sprk_document

| Field | Schema Name | Type | Description |
|-------|-------------|------|-------------|
| File Summary | `sprk_filesummary` | Multi-line Text (4000) | AI-generated summary |
| Summary Status | `sprk_filesummarystatus` | Choice | Processing status |
| Summary Date | `sprk_filesummarydate` | DateTime | When generated |

### 7.2 Summary Status Choice

| Value | Label | Description |
|-------|-------|-------------|
| 0 | None | No summary requested |
| 1 | Pending | Job queued |
| 2 | Completed | Summary generated |
| 3 | Failed | Processing failed |
| 4 | Not Supported | File type not supported |
| 5 | Skipped | User opted out via checkbox |

---

## 8. Configuration

### 8.1 AiOptions (Comprehensive)

```csharp
// Configuration/AiOptions.cs
public class AiOptions
{
    // === Azure OpenAI Settings ===
    public string OpenAiEndpoint { get; set; } = string.Empty;
    public string OpenAiKey { get; set; } = string.Empty;
    
    // === Model Configuration ===
    /// <summary>
    /// Model deployment name for summarization.
    /// Options: "gpt-4.1-mini" (fast, cheap), "gpt-4.1" (better quality), "gpt-5" (highest quality)
    /// Note: Model names should match Azure OpenAI deployment names in Microsoft Foundry portal.
    /// </summary>
    public string SummarizeModel { get; set; } = "gpt-4.1-mini";
    
    /// <summary>
    /// Max tokens for summary output. Higher = longer summaries.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 1000;
    
    /// <summary>
    /// Temperature for generation. Lower = more deterministic.
    /// </summary>
    public float Temperature { get; set; } = 0.3f;
    
    // === Document Intelligence Settings ===
    public string? DocIntelEndpoint { get; set; }
    public string? DocIntelKey { get; set; }
    
    // === File Type Configuration ===
    /// <summary>
    /// Enabled file extensions for summarization.
    /// Each extension maps to its extraction method.
    /// </summary>
    public Dictionary<string, FileTypeConfig> SupportedFileTypes { get; set; } = new()
    {
        // Native text extraction
        [".txt"] = new() { Enabled = true, Method = ExtractionMethod.Native },
        [".md"] = new() { Enabled = true, Method = ExtractionMethod.Native },
        [".json"] = new() { Enabled = true, Method = ExtractionMethod.Native },
        [".csv"] = new() { Enabled = true, Method = ExtractionMethod.Native },
        [".xml"] = new() { Enabled = true, Method = ExtractionMethod.Native },
        [".html"] = new() { Enabled = true, Method = ExtractionMethod.Native },
        
        // Document Intelligence extraction
        [".pdf"] = new() { Enabled = true, Method = ExtractionMethod.DocumentIntelligence },
        [".docx"] = new() { Enabled = true, Method = ExtractionMethod.DocumentIntelligence },
        [".doc"] = new() { Enabled = true, Method = ExtractionMethod.DocumentIntelligence },
        
        // Image OCR (Phase 2 - disabled by default)
        [".png"] = new() { Enabled = false, Method = ExtractionMethod.VisionOCR },
        [".jpg"] = new() { Enabled = false, Method = ExtractionMethod.VisionOCR },
        [".jpeg"] = new() { Enabled = false, Method = ExtractionMethod.VisionOCR },
        [".tiff"] = new() { Enabled = false, Method = ExtractionMethod.VisionOCR },
        [".bmp"] = new() { Enabled = false, Method = ExtractionMethod.VisionOCR },
    };
    
    // === Processing Limits ===
    public int MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10MB
    public int MaxInputTokens { get; set; } = 100_000; // ~75K words
    public int MaxConcurrentStreams { get; set; } = 3; // Per user
    
    // === Feature Flags ===
    /// <summary>
    /// Master switch to enable/disable AI summarization.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Enable streaming (SSE) responses. If false, only background jobs available.
    /// </summary>
    public bool StreamingEnabled { get; set; } = true;
}

public class FileTypeConfig
{
    public bool Enabled { get; set; }
    public ExtractionMethod Method { get; set; }
}

public enum ExtractionMethod
{
    Native,              // Direct text read
    DocumentIntelligence, // Azure Document Intelligence
    VisionOCR            // Azure Vision / GPT-4 Vision (Phase 2)
}
```

### 8.2 appsettings.json (Full Example)

```json
{
  "Ai": {
    "Enabled": true,
    "StreamingEnabled": true,
    
    "OpenAiEndpoint": "https://{resource}.openai.azure.com/",
    "OpenAiKey": "@Microsoft.KeyVault(SecretUri=https://kv-spaarke.vault.azure.net/secrets/ai-openai-key)",
    
    "SummarizeModel": "gpt-4.1-mini",
    "MaxOutputTokens": 1000,
    "Temperature": 0.3,
    
    "DocIntelEndpoint": "https://{resource}.cognitiveservices.azure.com/",
    "DocIntelKey": "@Microsoft.KeyVault(SecretUri=https://kv-spaarke.vault.azure.net/secrets/ai-docintel-key)",
    
    "MaxFileSizeBytes": 10485760,
    "MaxInputTokens": 100000,
    "MaxConcurrentStreams": 3,
    
    "SupportedFileTypes": {
      ".txt": { "Enabled": true, "Method": "Native" },
      ".md": { "Enabled": true, "Method": "Native" },
      ".json": { "Enabled": true, "Method": "Native" },
      ".csv": { "Enabled": true, "Method": "Native" },
      ".pdf": { "Enabled": true, "Method": "DocumentIntelligence" },
      ".docx": { "Enabled": true, "Method": "DocumentIntelligence" },
      ".png": { "Enabled": false, "Method": "VisionOCR" },
      ".jpg": { "Enabled": false, "Method": "VisionOCR" }
    }
  }
}
```

### 8.3 Environment-Specific Configuration

| Setting | Development | Staging | Production |
|---------|-------------|---------|------------|
| `SummarizeModel` | gpt-4.1-mini | gpt-4.1-mini | gpt-4.1-mini |
| `Enabled` | true | true | true |
| `MaxConcurrentStreams` | 5 | 3 | 3 |
| Image OCR | Enabled | Disabled | Disabled |

### 8.4 Runtime Configuration Checks

```csharp
// TextExtractorService.cs
public async Task<TextExtractionResult> ExtractAsync(Stream stream, string fileName, CancellationToken ct)
{
    var extension = Path.GetExtension(fileName).ToLowerInvariant();
    
    // Check if file type is configured
    if (!_options.SupportedFileTypes.TryGetValue(extension, out var config))
    {
        return TextExtractionResult.NotSupported($"File type {extension} not configured");
    }
    
    // Check if file type is enabled
    if (!config.Enabled)
    {
        return TextExtractionResult.NotSupported($"File type {extension} is disabled");
    }
    
    // Route to appropriate extractor
    return config.Method switch
    {
        ExtractionMethod.Native => await ExtractNativeAsync(stream, ct),
        ExtractionMethod.DocumentIntelligence => await ExtractViaDocIntelAsync(stream, ct),
        ExtractionMethod.VisionOCR => await ExtractViaVisionAsync(stream, ct),
        _ => TextExtractionResult.NotSupported($"Unknown extraction method")
    };
}
```

---

## 9. Supported File Types

File type support is **fully configurable** via `AiOptions.SupportedFileTypes`. This allows enabling/disabling specific extensions without code changes.

### Default Configuration (MVP)

| Extension | Method | Default | Notes |
|-----------|--------|---------|-------|
| `.txt` | Native | ✅ Enabled | Direct text read |
| `.md` | Native | ✅ Enabled | Markdown files |
| `.json` | Native | ✅ Enabled | JSON data |
| `.csv` | Native | ✅ Enabled | Comma-separated values |
| `.xml` | Native | ✅ Enabled | XML markup |
| `.html` | Native | ✅ Enabled | HTML (strips tags) |
| `.pdf` | Document Intelligence | ✅ Enabled | Full text extraction |
| `.docx` | Document Intelligence | ✅ Enabled | Word documents |
| `.doc` | Document Intelligence | ✅ Enabled | Legacy Word format |
| `.png` | Vision OCR | ❌ Disabled | Phase 2 |
| `.jpg/.jpeg` | Vision OCR | ❌ Disabled | Phase 2 |
| `.tiff` | Vision OCR | ❌ Disabled | Phase 2 |

### Phase 2: Image Support

Image files (PNG, JPG, TIFF, BMP) require **multimodal AI models** that can process visual content directly. This is more complex than text extraction.

**Why Images Are Different:**
- Text files → Extract text → Send to any LLM
- PDF/DOCX → Document Intelligence extracts text → Send to any LLM  
- **Images → Require multimodal model (GPT-4.1 or GPT-5 with vision) to "see" the image**

**Implementation Options for Images:**

| Approach | Model | Pros | Cons |
|----------|-------|------|------|
| **OCR First** | Doc Intelligence + gpt-4.1-mini | Cheaper, works with current model | Only extracts visible text, misses context |
| **Direct Vision** | GPT-4.1 (multimodal) | Understands diagrams, charts, context | Higher cost (~$0.01/image), requires model switch |
| **Hybrid** | Doc Intelligence + GPT-4.1 | Best of both | Most complex, highest cost |

**Recommended Approach (Phase 2):**
1. Use **GPT-4.1** (multimodal) for image summarization - it can directly process images
2. Configure separately: `ImageSummarizeModel` vs `SummarizeModel`
3. Add config option: `"ImageMethod": "Vision"` or `"ImageMethod": "OCR"`

**Configuration for Image Support:**
```json
{
  "Ai": {
    "SummarizeModel": "gpt-4.1-mini",
    "ImageSummarizeModel": "gpt-4.1",
    "SupportedFileTypes": {
      ".png": { "Enabled": true, "Method": "VisionOCR" },
      ".jpg": { "Enabled": true, "Method": "VisionOCR" }
    }
  }
}
```

**Cost Impact:**
- GPT-4.1 vision: ~$0.01-0.03 per image (depending on size/detail)
- Significantly higher than text summarization (~$0.005/doc)

### PDF Handling Strategy

PDFs can contain:
- **Text-based content** → Document Intelligence extracts text directly
- **Scanned images** → Document Intelligence OCR extracts text
- **Mixed content** → Document Intelligence handles both

Document Intelligence automatically handles all cases with the `prebuilt-read` model.

---

## 10. Cost Estimate

### Per-Document Cost

| Component | Rate | Estimate |
|-----------|------|----------|
| Document Intelligence | $1.50/1K pages | ~$0.0015/doc |
| gpt-4.1-mini input | $0.15/1M tokens | ~$0.003/doc (20K tokens) |
| gpt-4.1-mini output | $0.60/1M tokens | ~$0.0006/doc (1K tokens) |
| **Total (text)** | | **~$0.005/doc** |

### With Image OCR (Phase 2)

| Component | Rate | Estimate |
|-----------|------|----------|
| Document Intelligence OCR | $1.50/1K pages | ~$0.0015/page |
| GPT-4 Vision (if used) | $0.01/image | ~$0.01/image |

### Monthly Projection

| Volume | Text Only | With Images |
|--------|-----------|-------------|
| 1,000 docs | ~$5 | ~$15 |
| 10,000 docs | ~$50 | ~$150 |
| 100,000 docs | ~$500 | ~$1,500 |

---

## 11. Implementation Tasks

### Phase 1: Backend (Sprint 8)

- [ ] Create `AiOptions.cs` configuration
- [ ] Create `OpenAiClient.cs` wrapper
- [ ] Create `TextExtractorService.cs` (native text only)
- [ ] Create `SummarizeService.cs`
- [ ] Create `SummarizeEndpoints.cs`
- [ ] Create `SummarizeJobHandler.cs`
- [ ] Add DI registration
- [ ] Unit tests

### Phase 2: Frontend (Sprint 8-9)

- [ ] Create `AiSummaryPanel.tsx` component
- [ ] Integrate into `DocumentUploadForm.tsx`
- [ ] Handle SSE streaming
- [ ] Handle background fallback on close
- [ ] Styling

### Phase 3: Dataverse (Sprint 9)

- [ ] Add `sprk_filesummary` field
- [ ] Add `sprk_filesummarystatus` choice field
- [ ] Add `sprk_filesummarydate` field
- [ ] Update solution

### Phase 4: Document Intelligence (Sprint 9)

- [ ] Add Document Intelligence client
- [ ] PDF extraction
- [ ] DOCX extraction
- [ ] Integration tests

### Phase 5: Hardening (Sprint 10)

- [ ] Error handling edge cases
- [ ] Rate limiting
- [ ] Monitoring/alerts
- [ ] Documentation

---

## 12. ADR Compliance

| ADR | Requirement | Implementation |
|-----|-------------|----------------|
| ADR-001 | Minimal API | `SummarizeEndpoints.cs` using Minimal API |
| ADR-004 | Job contract | `SummarizeJobContract` for background jobs |
| ADR-007 | SpeFileStore | File download via existing `SpeFileStore` |
| ADR-008 | Endpoint filters | `AiAuthorizationFilter` on endpoints |
| ADR-009 | Redis caching | Cache extracted text (optional optimization) |
| ADR-010 | DI minimalism | 3 new services: `SummarizeService`, `TextExtractorService`, `OpenAiClient` |
| ADR-013 | AI architecture | Single BFF, orchestration pattern |

---

## 13. Future Enhancements

After MVP, consider:

1. **On-demand re-summarization** - Button on Document form
2. **Summary styles** - Brief, detailed, bullet points
3. **Multi-language** - Summarize in user's language
4. **Summary for existing documents** - Batch processing
5. **Summary quality feedback** - Thumbs up/down
