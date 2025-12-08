# AI Document Summary - Implementation Plan

> **Version**: 1.2  
> **Date**: December 8, 2025  
> **Target Sprint**: 8-10  
> **Estimated Effort**: ~16 dev days (128 hours)

---

## Overview

This plan breaks down the AI Document Summary feature into discrete, testable tasks. Each task is designed to be implementable and verifiable independently.

**Key Features:**
- **Multi-file support** - Carousel UI for 1-10 files with concurrent processing
- **User opt-out option** - "Run AI Summary" checkbox defaulted to checked (status: Skipped when unchecked)
- **Configurable file types** - Enable/disable extensions via configuration
- **Configurable models** - Switch between gpt-4o-mini, gpt-4o, gpt-4-vision, etc.
- **Image file support** - Multimodal summarization via GPT-4 Vision
- **Streaming + background** - Real-time streaming with background fallback

**Reference Documents:**
- [spec.md](spec.md) - Full design specification
- [ADR-013](../../docs/reference/adr/ADR-013-ai-architecture.md) - AI Architecture decision

---

## Phase 1: Infrastructure & Configuration

**Goal:** Set up Azure AI services, comprehensive configuration, and shared clients.

### Task 1.1: Azure OpenAI Client Setup

**Files to create:**
```
src/server/api/Sprk.Bff.Api/
├── Configuration/
│   └── AiOptions.cs
├── Services/
│   └── Ai/
│       └── OpenAiClient.cs
```

**AiOptions.cs (Comprehensive):**
```csharp
public class AiOptions
{
    // Azure OpenAI
    public string OpenAiEndpoint { get; set; }
    public string OpenAiKey { get; set; }
    
    // Model Configuration
    public string SummarizeModel { get; set; } = "gpt-4o-mini";
    public int MaxOutputTokens { get; set; } = 1000;
    public float Temperature { get; set; } = 0.3f;
    
    // Document Intelligence
    public string? DocIntelEndpoint { get; set; }
    public string? DocIntelKey { get; set; }
    
    // File Type Configuration (extensible)
    public Dictionary<string, FileTypeConfig> SupportedFileTypes { get; set; }
    
    // Processing Limits
    public int MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxInputTokens { get; set; } = 100_000;
    public int MaxConcurrentStreams { get; set; } = 3;
    
    // Feature Flags
    public bool Enabled { get; set; } = true;
    public bool StreamingEnabled { get; set; } = true;
}
```

**OpenAiClient.cs:**
- Constructor with `Azure.AI.OpenAI.OpenAIClient` injection
- `StreamCompletionAsync(string prompt, CancellationToken)` - Returns `IAsyncEnumerable<string>`
- `GetCompletionAsync(string prompt, CancellationToken)` - Returns `Task<string>`

**Acceptance Criteria:**
- [ ] Can connect to Azure OpenAI with configured endpoint
- [ ] Streaming completion works end-to-end
- [ ] Configuration supports model switching
- [ ] Unit tests with mocked OpenAIClient

---

### Task 1.2: Configuration & KeyVault

**Files to modify:**
```
src/server/api/Sprk.Bff.Api/
├── appsettings.json
├── appsettings.Development.json
└── Program.cs (DI registration)
```

**appsettings.json additions:**
```json
{
  "Ai": {
    "Enabled": true,
    "StreamingEnabled": true,
    "OpenAiEndpoint": "",
    "OpenAiKey": "",
    "SummarizeModel": "gpt-4o-mini",
    "MaxOutputTokens": 1000,
    "Temperature": 0.3,
    "MaxFileSizeBytes": 10485760,
    "MaxInputTokens": 100000,
    "MaxConcurrentStreams": 3,
    "SupportedFileTypes": {
      ".txt": { "Enabled": true, "Method": "Native" },
      ".md": { "Enabled": true, "Method": "Native" },
      ".pdf": { "Enabled": true, "Method": "DocumentIntelligence" },
      ".docx": { "Enabled": true, "Method": "DocumentIntelligence" },
      ".png": { "Enabled": false, "Method": "VisionOCR" }
    }
  }
}
```

**KeyVault secrets (infrastructure):**
- `ai-openai-endpoint`
- `ai-openai-key`
- `ai-docintel-endpoint`
- `ai-docintel-key`

**Acceptance Criteria:**
- [ ] Configuration loads correctly in all environments
- [ ] KeyVault references resolve in deployed environments
- [ ] File type config is extensible
- [ ] Missing configuration fails fast with clear error

---

## Phase 2: Text Extraction Service

**Goal:** Extract text from files stored in SPE.

### Task 2.1: Native Text Extraction

**Files to create:**
```
src/server/api/Sprk.Bff.Api/Services/Ai/
└── TextExtractorService.cs
```

**Implementation:**
- Support extensions: `.txt`, `.md`, `.json`, `.csv`, `.xml`, `.html`
- Direct stream-to-string read with encoding detection
- Handle BOM (byte order mark) correctly

**Interface:**
```csharp
public class TextExtractorService
{
    Task<TextExtractionResult> ExtractAsync(
        Stream fileStream, 
        string fileName, 
        CancellationToken ct);
}

public record TextExtractionResult(
    string Text,
    bool Success,
    string? ErrorMessage,
    TextExtractionMethod Method);

public enum TextExtractionMethod
{
    Native,
    DocumentIntelligence,
    NotSupported
}
```

**Acceptance Criteria:**
- [ ] Extracts text from TXT, MD, JSON, CSV files
- [ ] Returns empty result with `NotSupported` for unknown types
- [ ] Handles encoding correctly (UTF-8, UTF-16, etc.)
- [ ] Unit tests for each supported file type

---

### Task 2.2: Document Intelligence Integration (Phase 4)

*Deferred to Phase 4 - MVP works with native text files first*

**Files to modify:**
```
src/server/api/Sprk.Bff.Api/Services/Ai/
└── TextExtractorService.cs

src/server/api/Sprk.Bff.Api/Configuration/
└── AiOptions.cs (add DocIntel settings)
```

**Additional AiOptions:**
- `DocIntelEndpoint`
- `DocIntelKey`

**Extensions to support:**
- `.pdf` - PDF documents
- `.docx` - Word documents
- `.doc` - Legacy Word documents

**Acceptance Criteria:**
- [ ] PDF text extraction works
- [ ] DOCX text extraction works
- [ ] Falls back gracefully if Document Intelligence unavailable
- [ ] Integration tests with real documents

---

## Phase 3: Summarization Service

**Goal:** Orchestrate the summarization flow.

### Task 3.1: SummarizeService Core

**Files to create:**
```
src/server/api/Sprk.Bff.Api/
├── Services/Ai/
│   └── SummarizeService.cs
├── Models/Ai/
│   ├── SummarizeRequest.cs
│   ├── SummarizeResponse.cs
│   └── SummarizeChunk.cs
```

**SummarizeRequest:**
```csharp
public record SummarizeRequest(
    Guid DocumentId,
    string DriveId,
    string ItemId);
```

**SummarizeChunk:**
```csharp
public record SummarizeChunk(
    string Content,
    bool Done,
    string? Summary = null,
    string? Error = null);
```

**SummarizeService:**
```csharp
public class SummarizeService
{
    // Dependencies: SpeFileStore, TextExtractorService, OpenAiClient, IDataverseClient
    
    IAsyncEnumerable<SummarizeChunk> SummarizeStreamingAsync(
        SummarizeRequest request, 
        CancellationToken ct);
    
    Task<string> EnqueueAsync(
        SummarizeRequest request, 
        CancellationToken ct);
}
```

**Flow:**
1. Download file from SPE via `SpeFileStore`
2. Extract text via `TextExtractorService`
3. Build prompt with extracted text
4. Stream completion via `OpenAiClient`
5. Save summary to Dataverse on completion

**Prompt Template:**
```
You are a document summarization assistant. Generate a clear, concise 
summary of the following document. The summary should:

- Be 2-4 paragraphs (approximately 200-400 words)
- Capture the main points and key information
- Be written in professional business language
- Not start with "This document" or "The document"

Document:
{documentText}

Summary:
```

**Acceptance Criteria:**
- [ ] Downloads file from SPE correctly
- [ ] Extracts text from supported file types
- [ ] Streams OpenAI response token by token
- [ ] Saves completed summary to Dataverse
- [ ] Handles cancellation gracefully
- [ ] Unit tests with mocked dependencies

---

### Task 3.2: SummarizeJobHandler

**Files to create:**
```
src/server/api/Sprk.Bff.Api/
├── Services/Jobs/Handlers/
│   └── SummarizeJobHandler.cs
├── Models/Ai/
│   └── SummarizeJobPayload.cs
```

**SummarizeJobPayload:**
```csharp
public record SummarizeJobPayload(
    Guid DocumentId,
    string DriveId,
    string ItemId);
```

**SummarizeJobHandler:**
- Implements `IJobHandler`
- `JobType = "ai-summarize"`
- Calls `SummarizeService.SummarizeStreamingAsync()` and consumes stream
- Follows existing `DocumentProcessingJobHandler` pattern

**DI Registration:**
- Register as `IJobHandler` implementation

**Acceptance Criteria:**
- [ ] Processes jobs from Service Bus
- [ ] Uses existing idempotency infrastructure
- [ ] Logs job start/completion/failure
- [ ] Handles retries correctly

---

## Phase 4: API Endpoints

**Goal:** Expose summarization via REST endpoints.

### Task 4.1: Streaming Endpoint

**Files to create:**
```
src/server/api/Sprk.Bff.Api/Api/Ai/
└── SummarizeEndpoints.cs
```

**Endpoint:**
```
POST /api/ai/summarize/stream
Content-Type: application/json
Accept: text/event-stream

Request:
{
  "documentId": "guid",
  "driveId": "string",
  "itemId": "string"
}

Response: Server-Sent Events
data: {"content": "This quarterly", "done": false}
data: {"content": " report covers", "done": false}
data: {"content": "", "done": true, "summary": "Full summary text"}
```

**Implementation notes:**
- Set `Content-Type: text/event-stream`
- Set `Cache-Control: no-cache`
- Flush after each chunk
- On client disconnect, enqueue background job

**Acceptance Criteria:**
- [ ] SSE response format correct
- [ ] Streams tokens in real-time
- [ ] Handles client disconnection
- [ ] Integration test with actual HTTP client

---

### Task 4.2: Enqueue Endpoint (Single + Batch)

**Single Document Endpoint:**
```
POST /api/ai/summarize/enqueue

Request:
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

**Batch Endpoint (Multiple Documents):**
```
POST /api/ai/summarize/enqueue-batch

Request:
{
  "documents": [
    { "documentId": "guid1", "driveId": "string", "itemId": "string" },
    { "documentId": "guid2", "driveId": "string", "itemId": "string" }
  ]
}

Response: 202 Accepted
{
  "jobIds": ["guid1", "guid2"]
}
```

**Implementation:**
- Validate request(s)
- Submit to Service Bus via `JobSubmissionService`
- Update document status(es) to `Pending`
- Return job ID(s)

**Acceptance Criteria:**
- [ ] Single enqueue returns 202 Accepted
- [ ] Batch enqueue handles up to 10 documents
- [ ] Jobs appear in Service Bus
- [ ] Document statuses updated to Pending
- [ ] Integration tests for both endpoints

---

### Task 4.3: Authorization Filter

**Files to create/modify:**
```
src/server/api/Sprk.Bff.Api/Api/Filters/
└── AiAuthorizationFilter.cs (or reuse existing)
```

**Authorization logic:**
- User must have read access to the `sprk_document` record
- Use existing Dataverse permission check pattern

**Acceptance Criteria:**
- [ ] Unauthorized users get 403
- [ ] Authorized users can access
- [ ] Uses existing UAC infrastructure

---

## Phase 5: Dataverse Schema

**Goal:** Add summary fields to sprk_document entity.

### Task 5.1: Add Fields

**Fields to add to `sprk_document`:**

| Display Name | Schema Name | Type | Details |
|--------------|-------------|------|---------|
| File Summary | `sprk_filesummary` | Multi-line Text | Max: 4000 chars |
| Summary Status | `sprk_filesummarystatus` | Choice | See below |
| Summary Date | `sprk_filesummarydate` | DateTime | UTC |

**Summary Status Choice (`sprk_summarystatus`):**

| Value | Label |
|-------|-------|
| 0 | None |
| 1 | Pending |
| 2 | Completed |
| 3 | Failed |
| 4 | Not Supported |
| 5 | Skipped |

**Acceptance Criteria:**
- [ ] Fields exist in Dataverse
- [ ] Choice values correct (6 values: None through Skipped)
- [ ] Can update fields via API
- [ ] Skipped status set when user opts out of summarization

---

### Task 5.2: Update Solution

**Files to modify:**
```
src/dataverse/solutions/sprk/
└── (relevant solution files)
```

**Tasks:**
- Add fields to managed solution
- Update form to display summary (optional - can show in PCF)
- Export updated solution

**Acceptance Criteria:**
- [ ] Solution exports cleanly
- [ ] Solution imports into test environment
- [ ] Fields visible in Document form

---

## Phase 6: Frontend Integration

**Goal:** Display AI summaries in Universal Quick Create with multi-file carousel support.

### Task 6.1: AiSummaryCarousel Component (Multi-File)

**Files to create:**
```
src/client/pcf/UniversalQuickCreate/control/components/
├── AiSummaryCarousel.tsx      # Multi-file carousel
├── AiSummaryPanel.tsx         # Single document display
└── AiSummaryCarousel.styles.ts

src/client/pcf/UniversalQuickCreate/control/services/
└── useSseStream.ts            # SSE streaming hook
```

**AiSummaryCarousel Props:**
```typescript
interface AiSummaryCarouselProps {
  documents: Array<{
    documentId: string;
    driveId: string;
    itemId: string;
    fileName: string;
  }>;
  maxConcurrent?: number;  // Default: 3
  onAllComplete?: () => void;
}
```

**Features:**
- Navigation arrows: `[◀] 1 of 3 [▶]`
- Status badges per document (pending, streaming, complete, error)
- Aggregate status: "2 complete, 1 generating"
- Concurrent stream limit (default: 3 at a time)
- Auto-advance to next pending document

**States per document:**
- `pending` - Waiting for processing slot
- `streaming` - Actively receiving chunks
- `complete` - Summary finished
- `error` - Processing failed
- `not-supported` - File type disabled

**Acceptance Criteria:**
- [ ] Carousel navigation works
- [ ] Up to 3 concurrent streams
- [ ] Shows aggregate status
- [ ] Handles errors per-document
- [ ] Accessible (keyboard nav, screen reader)

---

### Task 6.2: SSE Client Hook

**Files to create:**
```
src/client/pcf/UniversalQuickCreate/control/services/
└── useSseStream.ts
```

**Hook signature:**
```typescript
function useSseStream(url: string, body: object): {
  data: string;
  status: 'idle' | 'connecting' | 'streaming' | 'complete' | 'error';
  error: Error | null;
  start: () => void;
  abort: () => void;
}
```

**Implementation:**
- Use `fetch` with streaming body reader
- Parse SSE format (`data: {...}\n\n`)
- Handle connection errors
- Support abort/cleanup

**Acceptance Criteria:**
- [ ] Parses SSE format correctly
- [ ] Handles network errors
- [ ] Cleans up on unmount
- [ ] Unit tests

---

### Task 6.3: Integration with DocumentUploadForm

**Files to modify:**
```
src/client/pcf/UniversalQuickCreate/control/components/
└── DocumentUploadForm.tsx
```

**Changes:**
1. Add "Run AI Summary" checkbox (defaulted to checked/true)
2. Track uploaded documents with their SPE IDs (`driveId`, `itemId`)
3. Only render `AiSummaryCarousel` if checkbox is checked
4. Pass all document info to carousel when enabled
5. On dialog close, call batch enqueue for incomplete summaries (if opted-in)

**Opt-Out Checkbox:**
```typescript
const [runSummary, setRunSummary] = useState(true);

// In form UI
<Checkbox 
  checked={runSummary}
  onChange={(_, data) => setRunSummary(data.checked ?? true)}
  label="Run AI Summary"
/>

// Conditional carousel rendering
{runSummary && showSummary && uploadedDocs.length > 0 && (
  <AiSummaryCarousel documents={uploadedDocs} />
)}
```

**Multi-File Flow:**
```
User selects 3 files → Optionally unchecks "Run AI Summary" → Upload all → 
  ├── If opt-in: Show carousel, stream doc 1, 2, 3 concurrently (max 3)
  ├── User navigates between summaries
  └── User closes dialog → Batch enqueue remaining (if opt-in)
  
  ├── If opt-out: No summarization triggered
```

**Acceptance Criteria:**
- [ ] "Run AI Summary" checkbox visible, defaulted to checked
- [ ] Unchecking prevents summarization
- [ ] Carousel appears for multi-file uploads when opted-in
- [ ] Single panel for single-file uploads when opted-in
- [ ] Background enqueue on close works (only if opted-in)
- [ ] E2E test with multiple files (with and without opt-out)

---

## Phase 7: Document Intelligence (Deferred)

**Goal:** Support PDF and Office documents.

### Task 7.1: Add Document Intelligence Client

**Files to modify:**
```
src/server/api/Sprk.Bff.Api/
├── Configuration/AiOptions.cs
├── Services/Ai/TextExtractorService.cs
└── Program.cs (DI)
```

**Changes:**
- Add `DocIntelEndpoint` and `DocIntelKey` to options
- Add `DocumentAnalysisClient` from `Azure.AI.FormRecognizer`
- Implement `ExtractViaDocIntelAsync()` method
- Use `prebuilt-read` model for text extraction

**Acceptance Criteria:**
- [ ] PDF extraction works
- [ ] DOCX extraction works
- [ ] Falls back if not configured
- [ ] Integration tests

---

## Phase 8: Production Hardening

**Goal:** Make feature production-ready.

### Task 8.1: Error Handling

- File not found in SPE
- Text extraction failure
- OpenAI rate limits / errors
- Dataverse update failure
- Network timeouts

### Task 8.2: Monitoring & Alerting

- Log summarization requests (duration, file size, success/failure)
- Track token usage for cost monitoring
- Alert on high failure rate
- Custom metrics in Application Insights

### Task 8.3: Rate Limiting

- Per-user rate limit on streaming endpoint
- Queue depth monitoring for background jobs
- Circuit breaker for OpenAI failures

### Task 8.4: Documentation

- Update README with feature description
- API documentation
- Troubleshooting guide

---

## Task Summary

| Phase | Task | Effort | Dependencies |
|-------|------|--------|--------------|
| 1 | 1.1 OpenAI Client + AiOptions | 1 day (8h) | None |
| 1 | 1.2 Configuration (with file types) | 0.5 day (4h) | 1.1 |
| 2 | 2.1 Text Extraction | 1 day (8h) | None |
| 3 | 3.1 SummarizeService | 1.5 days (12h) | 1.1, 2.1 |
| 3 | 3.2 JobHandler | 0.5 day (4h) | 3.1 |
| 4 | 4.1 Stream Endpoint | 1 day (8h) | 3.1, 4.3 |
| 4 | 4.2 Enqueue + Batch Endpoints | 1 day (8h) | 3.2, 4.3 |
| 4 | 4.3 Auth Filter | 0.5 day (4h) | None |
| 5 | 5.1 Dataverse Fields | 0.5 day (4h) | None |
| 5 | 5.2 Solution Update | 0.5 day (4h) | 5.1 |
| 6 | 6.0 AiSummaryPanel (single-file) | 0.5 day (4h) | None |
| 6 | 6.1 AiSummaryCarousel (multi-file) | 0.75 day (6h) | 6.0 |
| 6 | 6.2 SSE Hook | 0.5 day (4h) | None |
| 6 | 6.3 Form Integration (multi-file) | 1 day (8h) | 6.0, 6.1, 6.2 |
| 7 | 7.1 Doc Intelligence | 1 day (8h) | 2.1 |
| 7 | 7.2 Image File Support | 1 day (8h) | 7.1 |
| 8 | 8.1 Error Handling | 1 day (8h) | 3.1, 4.1 |
| 8 | 8.2 Monitoring | 1 day (8h) | 3.1 |
| 8 | 8.3 Rate Limiting | 1 day (8h) | 4.1 |
| 8 | 8.4 Documentation | 0.75 day (6h) | All above |
| 9 | 9.1 Project Wrap-up | 0.5 day (4h) | 8.4 |
| **Total** | | **~16 days (128h)** | |

---

## Implementation Order (Recommended)

**Sprint 8 (Backend Foundation) - ~44 hours:**
1. Task 1.1 - OpenAI Client + AiOptions (comprehensive config)
2. Task 2.1 - Text Extraction (native only)
3. Task 4.3 - Auth Filter
4. Task 1.2 - Configuration (with file type support)  
5. Task 3.1 - SummarizeService
6. Task 4.1 - Stream Endpoint

**Sprint 9 (Frontend + Integration) - ~38 hours:**
7. Task 5.1 - Dataverse Fields (with Skipped status)
8. Task 5.2 - Solution Update
9. Task 6.0 - AiSummaryPanel (single-file) ← NEW
10. Task 6.1 - AiSummaryCarousel (multi-file)
11. Task 6.2 - SSE Hook
12. Task 3.2 - JobHandler
13. Task 4.2 - Enqueue + Batch Endpoints
14. Task 6.3 - Form Integration (multi-file)

**Sprint 10 (Polish + PDF/Image Support) - ~46 hours:**
15. Task 7.1 - Document Intelligence
16. Task 7.2 - Image File Support (Multimodal) ← NEW
17. Task 8.1 - Error Handling
18. Task 8.2 - Monitoring
19. Task 8.3 - Rate Limiting
20. Task 8.4 - Documentation
21. Task 9.1 - Project Wrap-up

---

## Notes for Implementation

### Multi-File Technical Considerations

**Concurrency Strategy:**
- Client opens up to 3 concurrent SSE streams
- Remaining documents wait in `pending` state
- When one completes, next pending document starts
- If user closes dialog, batch enqueue all incomplete

**Rate Limiting:**
- BFF tracks concurrent streams per user
- Returns 429 if limit exceeded
- Client handles by queuing requests

### Existing Patterns to Follow

1. **Endpoints**: See `DocumentsEndpoints.cs` for Minimal API pattern
2. **Job Handlers**: See `DocumentProcessingJobHandler.cs` for job pattern
3. **File Access**: Use existing `SpeFileStore` for SPE operations
4. **Auth**: Use existing endpoint filter pattern from `Filters/`

### NuGet Packages Required

```xml
<PackageReference Include="Azure.AI.OpenAI" Version="1.0.0-beta.17" />
<PackageReference Include="Azure.AI.FormRecognizer" Version="4.1.0" />
```

### Test Strategy

- **Unit tests**: Mock external services (OpenAI, SPE, Dataverse)
- **Integration tests**: Use real Azure services in CI
- **E2E tests**: Full flow with multiple files in test environment

### Feature Flags (Optional)

Consider adding feature flag for gradual rollout:
```json
{
  "Ai": {
    "Enabled": true,
    "StreamingEnabled": true
  }
}
```

---

## Definition of Done

- [ ] All tasks completed
- [ ] Multi-file carousel working (1-10 files)
- [ ] "Run AI Summary" opt-out checkbox works correctly
- [ ] Configuration supports model/file type changes
- [ ] Unit tests passing (>80% coverage for new code)
- [ ] Integration tests passing
- [ ] E2E test with multiple files in staging
- [ ] E2E test for opt-out flow
- [ ] Documentation updated
- [ ] Code reviewed
- [ ] No critical/high security issues
- [ ] Performance acceptable (<30s for typical document)
