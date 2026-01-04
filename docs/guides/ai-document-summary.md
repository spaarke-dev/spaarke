# AI Document Intelligence - Developer Guide

> Complete API documentation for the AI Document Intelligence feature (document analysis, summarization, entity extraction).

## Table of Contents

1. [Overview](#overview)
2. [Configuration](#configuration)
3. [Authorization](#authorization)
4. [API Reference](#api-reference)
5. [SSE Response Format](#sse-response-format)
6. [Error Handling](#error-handling)
7. [Rate Limiting](#rate-limiting)
8. [Monitoring](#monitoring)
9. [Code Examples](#code-examples)

---

## Overview

The AI Document Summary feature provides automatic document summarization using Azure OpenAI. It supports:

- **Real-time streaming** via Server-Sent Events (SSE)
- **Background processing** via Service Bus jobs
- **Multiple file types**: Text, PDF, DOCX, images
- **Production resilience**: Rate limiting, circuit breaker, telemetry
- **Session persistence**: Chat history and working document saved to Dataverse (R3)
- **Export capabilities**: DOCX, PDF, Email, Teams adaptive cards (R3)
- **RAG integration**: Hybrid vector search for knowledge retrieval (R3)

### Architecture

```
Client (PCF)          BFF API                    Azure Services
+-----------+    +----------------+         +------------------+
| SSE       |--->| /stream        |-------->| Azure OpenAI     |
| EventSource|   | endpoint       |         | (gpt-4o-mini)    |
+-----------+    +----------------+         +------------------+
                        |
                        ↓ HttpContext (OBO token)
                 +----------------+         +------------------+
                 | Orchestration  |         | Doc Intelligence |
                 | Service        |-------->| (PDF/DOCX)       |
                 +----------------+         +------------------+
                        |
                        ↓ DownloadFileAsUserAsync (OBO)
                 +----------------+         +------------------+
                 | SpeFileStore   |-------->| SharePoint       |
                 +----------------+         | Embedded         |
                                            +------------------+
```

### SPE File Access Authentication (Critical)

**OBO Authentication Required**: Downloading files from SharePoint Embedded for analysis requires On-Behalf-Of (OBO) authentication. App-only authentication returns HTTP 403 (Access Denied).

```csharp
// ✅ CORRECT: Use OBO via HttpContext
var fileStream = await _speFileStore.DownloadFileAsUserAsync(
    httpContext,           // Passed from endpoint
    document.GraphDriveId!,
    document.GraphItemId!,
    cancellationToken);

// ❌ WRONG: App-only auth fails with 403
// var fileStream = await _speFileStore.DownloadFileAsync(driveId, itemId, ct);
```

**Why**: SPE containers use user-level permissions, not app-level permissions. The user's token must be exchanged via OBO to access files they have permission to view.

**HttpContext propagation**: All analysis endpoint methods accept and propagate `HttpContext` through the orchestration layer to enable OBO authentication for file downloads. See [SDAP Auth Patterns](../architecture/sdap-auth-patterns.md#pattern-4-obo-for-ai-analysis-spe-file-access) for details.

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

### AnalysisOptions (Analysis & Export)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxChatHistoryMessages` | int | `20` | Max chat messages to include in continuation context |
| `MaxDocumentContextLength` | int | `100000` | Max characters of document text in continuation prompts (1000-200000) |
| `EnableDocxExport` | bool | `true` | Enable DOCX export format |
| `EnablePdfExport` | bool | `true` | Enable PDF export format |
| `EnableEmailExport` | bool | `true` | Enable email export via Graph |
| `ExportBranding.CompanyName` | string | `"Spaarke AI"` | Branding in export footers |
| `ExportBranding.LogoUrl` | string? | null | Logo URL for PDF exports |

### Chat Continuation Context

When users continue an analysis via chat, the system includes full document context to provide accurate, document-specific responses:

| Context Section | Description | Truncation |
|-----------------|-------------|------------|
| System Instructions | Original action + skill prompts | None |
| Original Document | Full document text from SPE | Truncated to `MaxDocumentContextLength` |
| Current Analysis | Working document from previous response | None |
| Conversation History | Recent chat messages | Limited to `MaxChatHistoryMessages` |
| User Request | New user message | None |

This context is built by `IAnalysisContextBuilder.BuildContinuationPromptWithContext()` and ensures the AI can reference the original document content when answering follow-up questions.

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
  "DocumentIntelligence": {
    "SupportedFileTypes": {
      ".txt": { "Enabled": true, "Method": "Native" },
      ".pdf": { "Enabled": true, "Method": "DocumentIntelligence" },
      ".png": { "Enabled": true, "Method": "VisionOcr" }
    }
  }
}
```

---

## Authorization

All Document Intelligence endpoints require proper authentication and authorization.

### Authentication

All endpoints require Azure AD authentication via Bearer token. The user must be authenticated with a valid Azure AD JWT token containing at minimum:
- `oid` claim (Azure AD Object ID) - used for Dataverse user lookup
- `tid` claim (Tenant ID) - used for tenant isolation

### Document-Level Authorization

The `AiAuthorizationFilter` validates that the user has read access to the document being analyzed:

```
Request Flow:
POST /api/ai/document-intelligence/analyze
  → .RequireAuthorization()           (Azure AD JWT validation)
  → .AddAiAuthorizationFilter()       (Document-level access check)
  → .RequireRateLimiting("ai-stream") (Rate limiting)
  → StreamAnalyze()                   (Handler)
```

**How it works:**
1. **Claim Extraction**: Extracts the Azure AD `oid` (Object ID) from the JWT token
2. **Dataverse Lookup**: Queries Dataverse to find the user by `azureactivedirectoryobjectid`
3. **Permission Check**: Calls `RetrievePrincipalAccess` to verify user has read access to the `sprk_document` record
4. **Fail-Closed**: If any step fails, access is denied (security-first design)

### Claim Extraction Pattern

The filter uses the `oid` claim with a fallback chain:

```csharp
var userId = httpContext.User.FindFirst("oid")?.Value
    ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
    ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
```

**IMPORTANT**: The `oid` claim is required because `DataverseAccessDataSource` queries:
```
systemusers?$filter=azureactivedirectoryobjectid eq '{oid}'
```

### Authorization Error Responses

| Code | Cause | Resolution |
|------|-------|------------|
| `401` | Missing or invalid token | Provide valid Azure AD Bearer token |
| `401` | User identity not found (no `oid` claim) | Ensure token contains Azure AD Object ID |
| `403` | No read access to document | User must have Dataverse read access to the `sprk_document` record |
| `400` | No document ID in request | Include `documentId` in request body |

---

## API Reference

### POST /api/ai/document-intelligence/analyze

Stream document summarization via Server-Sent Events.

**Authentication:** Bearer token (Azure AD)

**Authorization:** User must have read access to the document (via `AiAuthorizationFilter`)

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

### POST /api/ai/document-intelligence/enqueue

Enqueue a single document for background summarization.

**Authentication:** Bearer token (Azure AD)

**Authorization:** User must have read access to the document (via `AiAuthorizationFilter`)

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

### POST /api/ai/document-intelligence/enqueue-batch

Enqueue multiple documents (max 10) for background summarization.

**Authentication:** Bearer token (Azure AD)

**Authorization:** User must have read access to ALL documents in the batch (via `AiAuthorizationFilter`)

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

### POST /api/ai/analysis/{analysisId}/export

Export analysis results to various formats.

**Authentication:** Bearer token (Azure AD)

**Rate Limit:** 10 requests/minute per user

**Request Headers:**
```
Content-Type: application/json
Authorization: Bearer {token}
```

**Request Body:**
```json
{
  "format": "docx",
  "emailTo": ["user@example.com"],
  "includeEntities": true,
  "includeClauses": true,
  "includeSummary": true
}
```

**Supported Formats:**

| Format | Description | Response |
|--------|-------------|----------|
| `docx` | Microsoft Word document | File download (binary) |
| `pdf` | PDF document | File download (binary) |
| `email` | Send via Microsoft Graph | Action confirmation (JSON) |
| `teams` | Post adaptive card to Teams channel | Action confirmation (JSON) |

**DOCX/PDF Response:** `200 OK`
```
Content-Type: application/vnd.openxmlformats-officedocument.wordprocessingml.document
Content-Disposition: attachment; filename="Analysis_Report_2025-12-30.docx"
[Binary file content]
```

**Email Response:** `200 OK`
```json
{
  "success": true,
  "format": "email",
  "metadata": {
    "Recipients": ["user@example.com"],
    "Subject": "Analysis: Contract Review",
    "SentAt": "2025-12-30T10:15:00Z"
  }
}
```

**Error Responses:**

| Code | Cause | Resolution |
|------|-------|------------|
| `400` | Invalid format or missing required fields | Check request body |
| `403` | Export format disabled | Enable in configuration |
| `404` | Analysis not found | Verify analysisId |
| `422` | Validation failed (e.g., invalid email) | Fix validation errors |

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
  const response = await fetch('/api/ai/document-intelligence/analyze', {
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
        "/api/ai/document-intelligence/enqueue",
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

## PCF Analysis Workspace (v1.2.7)

The `AnalysisWorkspace` PCF control provides the user interface for document analysis.

### Resume Session Dialog (ADR-023)

When an analysis has existing chat history, users are presented with a choice dialog:

| Option | Behavior |
|--------|----------|
| **Resume Session** | Load previous chat messages, continue conversation |
| **Start Fresh** | Clear chat history in Dataverse, start new conversation |
| **Cancel** | Close dialog, preserve history in Dataverse for next time |

**Key Implementation Details:**
- Dialog follows ADR-023 Choice Dialog Pattern (Fluent UI v9)
- Chat history stored in `sprk_analysis.sprk_chathistory` as JSON
- Working document stored in `sprk_analysis.sprk_workingdocument`
- Uses `chatMessagesRef` to avoid stale closures in auto-save

### URL Normalization

The control normalizes the BFF API base URL to prevent double path segments:

```typescript
// Handles case where apiBaseUrl already ends with /api
const normalizedBaseUrl = apiBaseUrl.replace(/\/api\/?$/, "");
const url = `${normalizedBaseUrl}/api/documents/${documentId}/preview-url`;
```

---

## Related Documentation

- [AI Troubleshooting Guide](ai-troubleshooting.md) - Common issues and solutions
- [RAG Architecture Guide](RAG-ARCHITECTURE.md) - Hybrid search and knowledge retrieval
- [AI Architecture Guide](SPAARKE-AI-ARCHITECTURE.md) - Overall AI architecture
- [ADR-023: Choice Dialog Pattern](../adr/ADR-023-choice-dialog-pattern.md) - Resume dialog design

---

*Last updated: January 2026 (R3 Phases 1-5)*
