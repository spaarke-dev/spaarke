# AI Document Summary

## Overview

AI-powered document summarization for Spaarke. When users upload documents via the Universal Quick Create dialog, an AI summary is automatically generated and streamed in real-time, then stored in the `sprk_document` record.

## Status

**Implementation Complete** - Deployment Required

| Phase | Status |
|-------|--------|
| 1: Infrastructure & Configuration | ✅ Complete |
| 2: Text Extraction Service | ✅ Complete |
| 3: Summarization Service | ✅ Complete |
| 4: API Endpoints | ✅ Complete |
| 5: Dataverse Schema | ✅ Complete |
| 6: Frontend Integration | ✅ Complete |
| 7: Document Intelligence | ✅ Complete |
| 8: Production Hardening | ✅ Complete |
| 10: Deployment | 🔲 Not Started |
| 11: Functional Testing | 🔲 Not Started |
| 12: Wrap-up | 🔲 Not Started |

**Next Steps:** Deploy to Azure and Dataverse, then execute functional testing.

## Quick Start

### Prerequisites

- Azure OpenAI resource with `gpt-4o-mini` deployment
- Azure Document Intelligence (optional, for PDF/DOCX)
- Key Vault secrets configured

### Configuration

Add to `appsettings.json` or Key Vault:

```json
{
  "Ai": {
    "Enabled": true,
    "OpenAiEndpoint": "https://{resource}.openai.azure.com/",
    "OpenAiKey": "@Microsoft.KeyVault(SecretUri=...)",
    "SummarizeModel": "gpt-4o-mini",
    "DocIntelEndpoint": "https://{resource}.cognitiveservices.azure.com/",
    "DocIntelKey": "@Microsoft.KeyVault(SecretUri=...)"
  }
}
```

### Key Vault Secrets

| Secret Name | Description |
|-------------|-------------|
| `ai-openai-endpoint` | Azure OpenAI endpoint URL |
| `ai-openai-key` | Azure OpenAI API key |
| `ai-docint-endpoint` | Document Intelligence endpoint (optional) |
| `ai-docint-key` | Document Intelligence key (optional) |

## User Experience

```
+---------------------------------------------------------+
|  + New Document                                    [X]  |
+---------------------------------------------------------+
|  Name: [Quarterly Report Q4.pdf                    ]    |
|  File: Quarterly Report Q4.pdf  Uploaded               |
|                                                         |
|  AI Summary                                             |
|  +---------------------------------------------------+  |
|  | This quarterly report covers Q4 2025 performance  |  |
|  | including revenue growth of 12%, expansion into   |  |
|  | three new markets, and the acquisition of...      |  |
|  +---------------------------------------------------+  |
|                                                         |
|  You can close - summary will complete in background    |
+---------------------------------------------------------+
|                        [Cancel]  [Upload & Create]      |
+---------------------------------------------------------+
```

## API Endpoints

### POST /api/ai/summarize/stream

Stream document summarization via Server-Sent Events (SSE).

**Request:**
```json
{
  "documentId": "00000000-0000-0000-0000-000000000001",
  "driveId": "drive-id-from-spe",
  "itemId": "item-id-from-spe"
}
```

**Response:** `text/event-stream`
```
data: {"type":"chunk","content":"This quarterly"}
data: {"type":"chunk","content":" report covers"}
data: {"type":"done","summaryLength":450}
```

**Rate Limit:** 10 requests/minute per user

### POST /api/ai/summarize/enqueue

Enqueue a single document for background summarization.

**Request:**
```json
{
  "documentId": "00000000-0000-0000-0000-000000000001",
  "driveId": "drive-id-from-spe",
  "itemId": "item-id-from-spe"
}
```

**Response:** `202 Accepted`
```json
{
  "jobId": "job-guid",
  "documentId": "document-guid"
}
```

**Rate Limit:** 20 requests/minute per user

### POST /api/ai/summarize/enqueue-batch

Enqueue up to 10 documents for background summarization.

**Request:**
```json
{
  "documents": [
    { "documentId": "...", "driveId": "...", "itemId": "..." },
    { "documentId": "...", "driveId": "...", "itemId": "..." }
  ]
}
```

**Response:** `202 Accepted`
```json
{
  "jobs": [
    { "jobId": "...", "documentId": "..." }
  ],
  "count": 2
}
```

## Architecture

```
+---------------+     +------------------+     +----------------+
|   PCF Control |---->|  BFF API         |---->| Azure OpenAI   |
| (AiSummaryPanel)    | (SummarizeService)|    | (gpt-4o-mini)  |
+---------------+     +------------------+     +----------------+
       |                      |
       |                      v
       |              +------------------+     +----------------+
       |              | Text Extraction  |---->| Doc Intelligence|
       |              +------------------+     | (PDF/DOCX)     |
       |                      |               +----------------+
       |                      v
       |              +------------------+
       +------------->|  SharePoint      |
                      |  Embedded (SPE)  |
                      +------------------+
```

**Flow:**
1. User uploads document to SPE via PCF control
2. PCF calls `/api/ai/summarize/stream` with document IDs
3. BFF downloads file from SPE, extracts text
4. BFF streams OpenAI response back via SSE
5. Summary saved to Dataverse `sprk_document` record

## Components

| Component | Type | Description |
|-----------|------|-------------|
| `SummarizeEndpoints.cs` | BFF API | Streaming + enqueue endpoints |
| `SummarizeService.cs` | BFF Service | Orchestrates summarization flow |
| `TextExtractor.cs` | BFF Service | Text extraction (native + Doc Intel + Vision) |
| `OpenAiClient.cs` | BFF Service | Azure OpenAI wrapper with circuit breaker |
| `SummarizeJobHandler.cs` | BFF Job | Background processing via Service Bus |
| `AiSummaryPanel.tsx` | PCF Component | Embedded in UniversalQuickCreate |
| `AiTelemetry.cs` | BFF Telemetry | OpenTelemetry metrics |

## Supported File Types

| Extension | Method | Requirements |
|-----------|--------|--------------|
| `.txt`, `.md`, `.json`, `.csv`, `.xml`, `.html` | Native | None |
| `.pdf`, `.docx`, `.doc` | Document Intelligence | DocIntelEndpoint configured |
| `.png`, `.jpg`, `.jpeg`, `.gif`, `.tiff`, `.bmp`, `.webp` | Vision OCR | ImageSummarizeModel configured |

## Dataverse Fields

| Field | Type | Description |
|-------|------|-------------|
| `sprk_filesummary` | Multi-line (4000) | AI-generated summary |
| `sprk_filesummarystatus` | Choice | Pending/Completed/Failed/NotSupported |
| `sprk_filesummarydate` | DateTime | When summary was generated |

## Error Codes

| Code | HTTP | Description |
|------|------|-------------|
| `ai_disabled` | 503 | AI features disabled via config |
| `file_not_found` | 404 | Document not found in SPE |
| `file_download_failed` | 502 | Failed to download from SPE |
| `extraction_failed` | 422 | Text extraction failed |
| `extraction_not_configured` | 503 | Doc Intel not configured |
| `vision_not_configured` | 503 | Vision model not configured |
| `openai_rate_limit` | 429 | OpenAI rate limit exceeded |
| `openai_timeout` | 504 | OpenAI request timed out |
| `openai_content_filter` | 422 | Content blocked by safety filter |
| `ai_circuit_open` | 503 | Circuit breaker open (service recovering) |
| `file_too_large` | 413 | File exceeds 10MB limit |
| `unsupported_file_type` | 415 | File type not supported |

## Production Features

### Rate Limiting
- Streaming: 10 requests/minute per user
- Batch: 20 requests/minute per user
- Returns 429 with `Retry-After` header

### Circuit Breaker
- Opens after 50% failure rate (min 5 calls)
- Half-open after 30 seconds
- Returns 503 when open

### Telemetry (OpenTelemetry)
- `ai.summarize.requests` - Total requests
- `ai.summarize.successes` - Successful completions
- `ai.summarize.failures` - Failed requests
- `ai.summarize.duration` - Processing time (ms)
- `ai.summarize.tokens` - Token usage (prompt/completion)

## Cost Estimate

| Model | Input | Output | Per Document |
|-------|-------|--------|--------------|
| gpt-4o-mini | $0.00015/1K | $0.0006/1K | ~$0.005 |
| gpt-4o | $0.005/1K | $0.015/1K | ~$0.15 |

Estimated monthly cost: **~$50 for 10,000 documents** (gpt-4o-mini)

## ADR Compliance

| ADR | Requirement | Implementation |
|-----|-------------|----------------|
| ADR-001 | Minimal API endpoints | `SummarizeEndpoints.cs` |
| ADR-004 | Job contract for background | `SummarizeJobHandler.cs` |
| ADR-007 | SpeFileStore for file access | `ISpeFileOperations` |
| ADR-008 | Endpoint filter auth | `AiAuthorizationFilter` |
| ADR-009 | Redis caching | Rate limit counters |
| ADR-010 | DI minimalism (3 new services) | 3 AI services registered |
| ADR-013 | AI architecture patterns | BFF orchestration |

## Documents

- [spec.md](spec.md) - Full design specification
- [plan.md](plan.md) - Implementation plan
- [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) - Task tracking

## Related Guides

- [AI Document Summary Guide](../../docs/guides/ai-document-summary.md) - Detailed API documentation
- [AI Troubleshooting](../../docs/guides/ai-troubleshooting.md) - Common issues and solutions
