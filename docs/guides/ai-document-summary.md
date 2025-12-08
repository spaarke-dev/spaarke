# AI Document Summary - Developer Guide

> Complete API documentation for the AI Document Summary feature.

## Table of Contents

1. [Overview](#overview)
2. [Configuration](#configuration)
3. [API Reference](#api-reference)
4. [SSE Response Format](#sse-response-format)
5. [Error Handling](#error-handling)
6. [Rate Limiting](#rate-limiting)
7. [Monitoring](#monitoring)
8. [Code Examples](#code-examples)

---

## Overview

The AI Document Summary feature provides automatic document summarization using Azure OpenAI. It supports:

- **Real-time streaming** via Server-Sent Events (SSE)
- **Background processing** via Service Bus jobs
- **Multiple file types**: Text, PDF, DOCX, images
- **Production resilience**: Rate limiting, circuit breaker, telemetry

### Architecture

```
Client (PCF)          BFF API                    Azure Services
+-----------+    +----------------+         +------------------+
| SSE       |--->| /stream        |-------->| Azure OpenAI     |
| EventSource|   | endpoint       |         | (gpt-4o-mini)    |
+-----------+    +----------------+         +------------------+
                        |
                        v
                 +----------------+         +------------------+
                 | TextExtractor  |-------->| Doc Intelligence |
                 +----------------+         | (PDF/DOCX)       |
                        |                   +------------------+
                        v
                 +----------------+         +------------------+
                 | SpeFileStore   |-------->| SharePoint       |
                 +----------------+         | Embedded         |
                                            +------------------+
```

---

## Configuration

### AiOptions Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | bool | `true` | Master switch for AI features |
| `StreamingEnabled` | bool | `true` | Enable SSE streaming |
| `OpenAiEndpoint` | string | Required | Azure OpenAI endpoint URL |
| `OpenAiKey` | string | Required | Azure OpenAI API key |
| `SummarizeModel` | string | `gpt-4o-mini` | Model deployment name |
| `ImageSummarizeModel` | string? | null | Vision model for images |
| `MaxOutputTokens` | int | `1000` | Max tokens in summary (100-4000) |
| `Temperature` | float | `0.3` | Generation temperature (0.0-1.0) |
| `DocIntelEndpoint` | string? | null | Document Intelligence endpoint |
| `DocIntelKey` | string? | null | Document Intelligence key |
| `MaxFileSizeBytes` | int | `10MB` | Max file size |
| `MaxInputTokens` | int | `100000` | Max input tokens |
| `MaxConcurrentStreams` | int | `3` | Max concurrent SSE per user |

### Example Configuration

```json
{
  "Ai": {
    "Enabled": true,
    "StreamingEnabled": true,
    "OpenAiEndpoint": "https://myresource.openai.azure.com/",
    "OpenAiKey": "@Microsoft.KeyVault(SecretUri=https://myvault.vault.azure.net/secrets/ai-openai-key)",
    "SummarizeModel": "gpt-4o-mini",
    "ImageSummarizeModel": "gpt-4o",
    "MaxOutputTokens": 1000,
    "Temperature": 0.3,
    "DocIntelEndpoint": "https://myresource.cognitiveservices.azure.com/",
    "DocIntelKey": "@Microsoft.KeyVault(SecretUri=https://myvault.vault.azure.net/secrets/ai-docint-key)",
    "MaxFileSizeBytes": 10485760,
    "MaxInputTokens": 100000,
    "MaxConcurrentStreams": 3
  }
}
```

### Supported File Types

Configure via `SupportedFileTypes` dictionary:

```json
{
  "Ai": {
    "SupportedFileTypes": {
      ".txt": { "Enabled": true, "Method": "Native" },
      ".pdf": { "Enabled": true, "Method": "DocumentIntelligence" },
      ".png": { "Enabled": true, "Method": "VisionOcr" }
    }
  }
}
```

---

## API Reference

### POST /api/ai/summarize/stream

Stream document summarization via Server-Sent Events.

**Authentication:** Bearer token (Azure AD)

**Rate Limit:** 10 requests/minute per user

**Request Headers:**
```
Content-Type: application/json
Authorization: Bearer {token}
Accept: text/event-stream
```

**Request Body:**
```json
{
  "documentId": "00000000-0000-0000-0000-000000000001",
  "driveId": "b!abc123...",
  "itemId": "01ABC123..."
}
```

**Response:** `200 OK` with `Content-Type: text/event-stream`

**Success Response:**
```
data: {"type":"metadata","fileName":"report.pdf","fileSize":1024,"extractionMethod":"document_intelligence"}

data: {"type":"chunk","content":"This quarterly report"}

data: {"type":"chunk","content":" covers Q4 2025 performance"}

data: {"type":"done","summaryLength":450}
```

**Error Response:**
```
data: {"type":"error","code":"openai_rate_limit","message":"Rate limit exceeded","retryAfterSeconds":30}
```

---

### POST /api/ai/summarize/enqueue

Enqueue a single document for background summarization.

**Authentication:** Bearer token (Azure AD)

**Rate Limit:** 20 requests/minute per user

**Request Body:**
```json
{
  "documentId": "00000000-0000-0000-0000-000000000001",
  "driveId": "b!abc123...",
  "itemId": "01ABC123..."
}
```

**Response:** `202 Accepted`
```json
{
  "jobId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "documentId": "00000000-0000-0000-0000-000000000001"
}
```

---

### POST /api/ai/summarize/enqueue-batch

Enqueue multiple documents (max 10) for background summarization.

**Authentication:** Bearer token (Azure AD)

**Rate Limit:** 20 requests/minute per user

**Request Body:**
```json
{
  "documents": [
    {
      "documentId": "00000000-0000-0000-0000-000000000001",
      "driveId": "b!abc123...",
      "itemId": "01ABC123..."
    },
    {
      "documentId": "00000000-0000-0000-0000-000000000002",
      "driveId": "b!abc123...",
      "itemId": "01DEF456..."
    }
  ]
}
```

**Response:** `202 Accepted`
```json
{
  "jobs": [
    {
      "jobId": "a1b2c3d4-...",
      "documentId": "00000000-0000-0000-0000-000000000001"
    },
    {
      "jobId": "b2c3d4e5-...",
      "documentId": "00000000-0000-0000-0000-000000000002"
    }
  ],
  "count": 2
}
```

---

## SSE Response Format

### Chunk Types

| Type | Description | Properties |
|------|-------------|------------|
| `metadata` | File information | `fileName`, `fileSize`, `extractionMethod` |
| `chunk` | Content token | `content` |
| `done` | Completion | `summaryLength` |
| `error` | Error occurred | `code`, `message`, `retryAfterSeconds?` |

### Metadata Chunk

Sent first with file information:
```json
{
  "type": "metadata",
  "fileName": "quarterly-report.pdf",
  "fileSize": 1048576,
  "extractionMethod": "document_intelligence"
}
```

### Content Chunk

Streamed tokens from OpenAI:
```json
{
  "type": "chunk",
  "content": "This quarterly"
}
```

### Done Chunk

Sent when summarization completes:
```json
{
  "type": "done",
  "summaryLength": 450
}
```

### Error Chunk

Sent on failure:
```json
{
  "type": "error",
  "code": "openai_rate_limit",
  "message": "The AI service is currently overloaded. Please try again later.",
  "retryAfterSeconds": 30
}
```

---

## Error Handling

### Error Codes

| Code | HTTP | Cause | Resolution |
|------|------|-------|------------|
| `ai_disabled` | 503 | AI features disabled | Enable `Ai:Enabled` in config |
| `file_not_found` | 404 | Document not in SPE | Verify driveId/itemId |
| `file_download_failed` | 502 | SPE download failed | Check SPE connectivity |
| `extraction_failed` | 422 | Text extraction failed | Check file format |
| `extraction_not_configured` | 503 | Doc Intel not configured | Configure DocIntelEndpoint |
| `vision_not_configured` | 503 | Vision model not configured | Configure ImageSummarizeModel |
| `openai_error` | 502 | OpenAI API error | Check OpenAI status |
| `openai_rate_limit` | 429 | OpenAI rate limit | Wait and retry |
| `openai_timeout` | 504 | Request timed out | Retry |
| `openai_content_filter` | 422 | Content blocked | Review document content |
| `ai_circuit_open` | 503 | Circuit breaker open | Wait 30s for recovery |
| `file_too_large` | 413 | File > 10MB | Use smaller file |
| `unsupported_file_type` | 415 | Type not supported | Check supported types |

### Problem Details Response

All errors return RFC 7807 Problem Details:

```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "Rate Limit Exceeded",
  "status": 429,
  "detail": "The AI service is currently overloaded. Please try again later.",
  "instance": "/api/ai/summarize/stream",
  "extensions": {
    "code": "openai_rate_limit",
    "retryAfterSeconds": 30,
    "correlationId": "abc123..."
  }
}
```

---

## Rate Limiting

### Policies

| Endpoint | Policy | Limit | Window |
|----------|--------|-------|--------|
| `/stream` | `ai-stream` | 10 requests | 1 minute |
| `/enqueue` | `ai-batch` | 20 requests | 1 minute |
| `/enqueue-batch` | `ai-batch` | 20 requests | 1 minute |

### Rate Limit Response

When rate limited, returns `429 Too Many Requests`:

```json
{
  "type": "https://tools.ietf.org/html/rfc6585#section-4",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Please retry after the specified duration.",
  "instance": "/api/ai/summarize/stream",
  "retryAfter": "60 seconds"
}
```

Response headers:
```
Retry-After: 60
```

### Circuit Breaker

The OpenAI client includes a circuit breaker for resilience:

| Parameter | Value |
|-----------|-------|
| Failure ratio | 50% |
| Minimum throughput | 5 calls |
| Break duration | 30 seconds |

When open, returns `503 Service Unavailable` with `ai_circuit_open` code.

---

## Monitoring

### OpenTelemetry Metrics

| Metric | Type | Tags |
|--------|------|------|
| `ai.summarize.requests` | Counter | `ai.method`, `ai.extraction` |
| `ai.summarize.successes` | Counter | `ai.method`, `ai.extraction`, `ai.file_type` |
| `ai.summarize.failures` | Counter | `ai.method`, `ai.error_code` |
| `ai.summarize.duration` | Histogram | `ai.method`, `ai.status` |
| `ai.summarize.tokens` | Counter | `ai.token_type`, `ai.model` |
| `ai.summarize.file_size` | Histogram | `ai.method` |

### Application Insights Queries

**Success Rate:**
```kusto
customMetrics
| where name == "ai.summarize.requests"
| summarize count() by tostring(customDimensions["ai.status"])
```

**Token Usage:**
```kusto
customMetrics
| where name == "ai.summarize.tokens"
| summarize sum(value) by tostring(customDimensions["ai.token_type"])
```

**Average Duration:**
```kusto
customMetrics
| where name == "ai.summarize.duration"
| summarize avg(value) by bin(timestamp, 1h)
```

---

## Code Examples

### TypeScript - SSE Client

```typescript
async function streamSummarize(
  documentId: string,
  driveId: string,
  itemId: string,
  onChunk: (content: string) => void,
  onComplete: () => void,
  onError: (error: string) => void
): Promise<void> {
  const response = await fetch('/api/ai/summarize/stream', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${getAccessToken()}`
    },
    body: JSON.stringify({ documentId, driveId, itemId })
  });

  if (!response.ok) {
    const error = await response.json();
    onError(error.detail || 'Summarization failed');
    return;
  }

  const reader = response.body?.getReader();
  const decoder = new TextDecoder();

  while (reader) {
    const { done, value } = await reader.read();
    if (done) break;

    const text = decoder.decode(value);
    const lines = text.split('\n');

    for (const line of lines) {
      if (line.startsWith('data: ')) {
        const data = JSON.parse(line.slice(6));

        if (data.type === 'chunk') {
          onChunk(data.content);
        } else if (data.type === 'done') {
          onComplete();
        } else if (data.type === 'error') {
          onError(data.message);
        }
      }
    }
  }
}
```

### C# - Background Job

```csharp
public async Task SummarizeInBackgroundAsync(
    Guid documentId,
    string driveId,
    string itemId,
    CancellationToken ct)
{
    var client = _httpClientFactory.CreateClient("BffApi");

    var response = await client.PostAsJsonAsync(
        "/api/ai/summarize/enqueue",
        new { documentId, driveId, itemId },
        ct);

    if (response.StatusCode == HttpStatusCode.TooManyRequests)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta
            ?? TimeSpan.FromSeconds(60);
        await Task.Delay(retryAfter, ct);
        // Retry...
    }

    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadFromJsonAsync<EnqueueResponse>(ct);

    _logger.LogInformation("Job {JobId} enqueued for document {DocumentId}",
        result.JobId, result.DocumentId);
}
```

---

## Related Documentation

- [AI Troubleshooting Guide](ai-troubleshooting.md) - Common issues and solutions
- [Project README](../../projects/ai-document-summary/README.md) - Project overview
- [Design Specification](../../projects/ai-document-summary/spec.md) - Original design

---

*Last updated: December 2025*
