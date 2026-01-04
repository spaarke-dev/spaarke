# Streaming Endpoints Pattern

> **Domain**: AI / Server-Sent Events
> **Last Validated**: 2025-01-03
> **Source ADRs**: ADR-013

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` | SSE endpoints |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | Stream orchestration |

---

## Endpoint Structure

```csharp
app.MapPost("/api/ai/analysis/execute", async (
    AnalysisRequest request,
    HttpContext context,
    IAnalysisOrchestrationService orchestration,
    CancellationToken ct) =>
{
    // 1. Set SSE headers
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    // 2. Stream chunks - HttpContext required for OBO authentication
    // The orchestration service uses HttpContext to download files from SPE
    // using the user's delegated permissions (not app-only auth)
    await foreach (var chunk in orchestration.ExecuteAnalysisAsync(request, context, ct))
    {
        var json = JsonSerializer.Serialize(chunk);
        await context.Response.WriteAsync($"data: {json}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }
}).RequireAuthorization();
```

**HttpContext for OBO Authentication:**
The `HttpContext` must be passed to orchestration methods because SPE file access requires On-Behalf-Of (OBO) authentication. See [Text Extraction Pattern](text-extraction.md#file-download-obo-authentication-required) for details.

---

## Stream Chunk Types

```csharp
public record AnalysisStreamChunk(
    string Type,           // "metadata", "chunk", "done", "error"
    string? Content,       // Text content for streaming
    bool Done,             // Completion flag
    string? AnalysisId,    // For metadata type
    string? DocumentName,  // For metadata type
    TokenUsage? Usage,     // For done type
    string? Error          // For error type
);

// Usage examples:
new AnalysisStreamChunk("metadata", null, false, analysisId, fileName, null, null);
new AnalysisStreamChunk("chunk", tokenText, false, null, null, null, null);
new AnalysisStreamChunk("done", null, true, null, null, usage, null);
new AnalysisStreamChunk("error", null, true, null, null, null, errorMessage);
```

---

## Client-Side Consumption (TypeScript)

```typescript
const response = await fetch('/api/ai/analysis/execute', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${token}` },
    body: JSON.stringify(request)
});

const reader = response.body!.getReader();
const decoder = new TextDecoder();
let buffer = '';

while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split('\n\n');
    buffer = lines.pop() || '';

    for (const line of lines) {
        if (line.startsWith('data: ')) {
            const chunk = JSON.parse(line.slice(6)) as AnalysisStreamChunk;

            switch (chunk.type) {
                case 'metadata':
                    setAnalysisId(chunk.analysisId);
                    break;
                case 'chunk':
                    appendContent(chunk.content!);
                    break;
                case 'done':
                    setComplete(true);
                    break;
                case 'error':
                    setError(chunk.error);
                    break;
            }
        }
    }
}
```

---

## Error Handling

```csharp
try
{
    await foreach (var chunk in orchestration.ExecuteStreamingAsync(request, ct))
    {
        await WriteChunkAsync(ctx, chunk, ct);
    }
}
catch (OperationCanceledException)
{
    // Client disconnected - don't log as error
}
catch (Exception ex)
{
    _logger.LogError(ex, "Streaming error");
    await WriteChunkAsync(ctx, new AnalysisStreamChunk(
        "error", null, true, null, null, null, "Analysis failed"
    ), ct);
}
```

---

## Circuit Breaker (OpenAiClient)

```csharp
// OpenAiClient.cs configuration
private readonly CircuitBreakerPolicy _circuitBreaker = Policy
    .Handle<HttpRequestException>()
    .Or<TaskCanceledException>()
    .CircuitBreakerAsync(
        exceptionsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromSeconds(30),
        onBreak: (ex, duration) => _logger.LogWarning("Circuit OPENED"),
        onReset: () => _logger.LogInformation("Circuit CLOSED")
    );
```

---

## Key Points

1. **SSE format** - `data: {json}\n\n` with flush after each chunk
2. **Chunk types** - metadata, chunk, done, error
3. **Circuit breaker** - 5 failures opens for 30 seconds
4. **Client buffering** - Handle partial chunks across reads
5. **Cancellation** - Graceful handling of client disconnect

---

## Related Patterns

- [Text Extraction](text-extraction.md) - Document processing
- [Analysis Scopes](analysis-scopes.md) - Prompt construction

---

**Lines**: ~110
