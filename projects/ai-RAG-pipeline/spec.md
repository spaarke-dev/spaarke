# RAG Document Ingestion Pipeline - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-15
> **Source**: design.md (2026-01-14)
> **Supersedes**: rag-ingestion-assessment.md, rag-ingestion-design.md

## Executive Summary

Implement a unified RAG document ingestion pipeline that supports three entry points (user upload via OBO, background job with file download, background job with pre-extracted content) converging to a single shared chunking/embedding/indexing pipeline. This ensures consistent searchability regardless of how documents enter the system.

**Root Cause Addressed**: No trigger mechanism currently exists to invoke the RAG indexing pipeline when documents are uploaded or created.

## Scope

### In Scope

**Phase 0: Analysis Workflow Alignment** (Prerequisite)
- Align `AppOnlyAnalysisService` to create `sprk_analysis` and `sprk_analysisoutput` records
- Ensure consistent behavior between OBO and app-only analysis flows

**Phase 1: Core Pipeline**
- Create `ITextChunkingService` - extract and centralize duplicated chunking logic
- Create `IFileIndexingService` - unified pipeline with 3 entry points
- Create `RagIndexingJobHandler` - background job processing
- Add `POST /api/ai/rag/index-file` endpoint for user-triggered indexing
- Register services in DI
- Unit tests for core services

**Phase 2: Email Integration**
- Add `AutoIndexToRag` configuration option to `EmailProcessingOptions`
- Add `EnqueueRagIndexingJobAsync` to `EmailToDocumentJobHandler`
- Add RAG telemetry methods to `EmailTelemetry`
- Integration tests for email-to-RAG flow

**Phase 3: Cleanup**
- Refactor 7 tool handlers to use `ITextChunkingService`
- Remove duplicate `ChunkText()` methods

**Phase 4: Event-Driven**
- Implement `DocumentEventHandler.HandleDocumentCreatedAsync()`
- E2E tests for document event → RAG flow

**Phase 5: PCF Integration**
- Implement client-side RAG indexing call in PCF after successful upload
- Non-blocking - failures logged as warnings

### Out of Scope

- Changes to existing RAG search functionality (`/api/ai/rag/search`)
- Modifications to Azure AI Search index schema
- Multi-tenant deployment routing changes (already implemented)
- Batch import from external sources (future enhancement)
- Real-time sync with SharePoint webhooks (future enhancement)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Ai/` - New services (TextChunkingService, FileIndexingService)
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/` - New job handler (RagIndexingJobHandler)
- `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs` - New endpoint
- `src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs` - Phase 0 alignment
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` - Email integration
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/DocumentEventHandler.cs` - Phase 4
- `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/*.cs` - Refactor chunking (7 handlers)
- `src/client/pcf/` - PCF integration for upload flow

## Requirements

### Functional Requirements

1. **FR-01**: Create `ITextChunkingService` that chunks text with configurable chunk size (default 4000), overlap (default 200), and sentence boundary preservation
   - Acceptance: Single implementation used by all consumers; unit tests pass

2. **FR-02**: Create `IFileIndexingService` with three entry points that all converge to identical shared pipeline
   - `IndexFileAsync()` - OBO auth, downloads file
   - `IndexFileAppOnlyAsync()` - App-only auth, downloads file
   - `IndexContentAsync()` - Pre-extracted content, skips download
   - Acceptance: All three methods produce identical index entries for same content

3. **FR-03**: Create `RagIndexingJobHandler` implementing `IJobHandler` for background RAG indexing
   - Acceptance: Handler processes jobs, implements idempotency, emits telemetry

4. **FR-04**: Add `POST /api/ai/rag/index-file` endpoint using OBO authentication
   - Acceptance: Endpoint indexes files, returns `FileIndexingResult`, uses endpoint filter for auth

5. **FR-05**: Add `EnqueueRagIndexingJobAsync()` to `EmailToDocumentJobHandler`
   - Acceptance: RAG job enqueued after document creation when `AutoIndexToRag=true`

6. **FR-06**: Implement `DocumentEventHandler.HandleDocumentCreatedAsync()` for document event-driven indexing
   - Acceptance: Documents created outside email flow get indexed to RAG

7. **FR-07**: PCF calls indexing endpoint after successful upload
   - Acceptance: PCF integration works, failures don't block upload flow

8. **FR-08**: Align `AppOnlyAnalysisService` to create `sprk_analysis` and `sprk_analysisoutput` records
   - Acceptance: Background analyses visible in Dataverse Analysis Workspace

### Non-Functional Requirements

- **NFR-01**: Performance - Small file (<1MB): <5s, Medium (1-10MB): <15s, Large (10-50MB): <60s
- **NFR-02**: All indexed content must be searchable via existing `/api/ai/rag/search` endpoint
- **NFR-03**: Idempotency - Duplicate indexing requests must be detected and skipped
- **NFR-04**: Resilience - RAG indexing failures in email flow must not fail the email job (silent warning)
- **NFR-05**: Observability - All operations emit telemetry with correlation IDs

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| **ADR-001** | Minimal API patterns for `/index-file` endpoint |
| **ADR-004** | Job Contract schema for `RagIndexingJobHandler` |
| **ADR-007** | File access through `SpeFileStore` only |
| **ADR-008** | Endpoint filters for authorization |
| **ADR-009** | Redis caching for expensive AI results |
| **ADR-010** | DI minimalism, feature module registration |
| **ADR-013** | AI architecture - extend BFF, no separate service |
| **ADR-014** | AI caching - scope by tenant, include version in keys |
| **ADR-015** | Data governance - don't log document content |
| **ADR-016** | Rate limiting on AI endpoints |
| **ADR-017** | Job status persistence |
| **ADR-019** | ProblemDetails for errors |

### MUST Rules

- ✅ MUST use Minimal API for `/index-file` endpoint (ADR-001)
- ✅ MUST use endpoint filter for authorization, not global middleware (ADR-008)
- ✅ MUST access files through `SpeFileStore.DownloadFileAsync()` / `DownloadFileAsUserAsync()` only (ADR-007)
- ✅ MUST use Job Contract schema for RAG indexing jobs (ADR-004)
- ✅ MUST implement handlers as idempotent (ADR-004)
- ✅ MUST propagate CorrelationId from original request (ADR-004)
- ✅ MUST apply rate limiting to indexing endpoint (ADR-016)
- ✅ MUST scope cache keys by tenant (ADR-014)
- ✅ MUST log only identifiers, sizes, timings - not document content (ADR-015)

### MUST NOT Rules

- ❌ MUST NOT create separate AI microservice (ADR-013)
- ❌ MUST NOT use Azure Functions for processing (ADR-001)
- ❌ MUST NOT place document bytes in job payloads (ADR-004)
- ❌ MUST NOT log document contents or extracted text (ADR-015)
- ❌ MUST NOT assume exactly-once delivery for jobs (ADR-004)

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/AppOnlyDocumentAnalysisJobHandler.cs` for job handler pattern
- See `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs` for existing RAG endpoint patterns
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/SummaryHandler.cs:479-517` for chunking logic to extract
- See `.claude/patterns/api/endpoint-definition.md` for endpoint patterns
- See `.claude/patterns/api/background-workers.md` for job handler patterns

## Success Criteria

1. [ ] `AppOnlyAnalysisService` creates `sprk_analysis` records - Verify: Query Dataverse after background analysis
2. [ ] `AppOnlyAnalysisService` creates `sprk_analysisoutput` records - Verify: Query Dataverse
3. [ ] `POST /api/ai/rag/index-file` indexes documents via OBO auth - Verify: API test
4. [ ] `RagIndexingJobHandler` indexes documents via app-only auth - Verify: Job processing test
5. [ ] Both OBO and app-only paths produce **identical** indexed results - Verify: Compare index entries
6. [ ] Email archives automatically indexed when `AutoIndexToRag=true` - Verify: Integration test
7. [ ] Document events trigger RAG indexing - Verify: E2E test
8. [ ] PCF calls indexing endpoint after upload - Verify: Manual UI test
9. [ ] Works for Documents (with documentId) and orphan files (without) - Verify: Both scenarios tested
10. [ ] Indexed documents searchable via `/api/ai/rag/search` - Verify: Search test
11. [ ] Performance within targets - Verify: Load test
12. [ ] All existing tests continue to pass - Verify: CI pipeline
13. [ ] Duplicate `ChunkText()` methods removed from tool handlers - Verify: Code review

## Dependencies

### Prerequisites

- ✅ **email-to-document-automation-r2** project complete (confirmed 2026-01-15)
- ✅ Existing RAG infrastructure (RagService, TextExtractor, EmbeddingCache) is complete and tested
- ✅ Azure AI Search index exists and is configured

### External Dependencies

- Azure OpenAI service for embedding generation
- Azure AI Search for index storage
- Service Bus for job queue (existing)

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| PCF Scope | Is PCF client integration in scope? | **Yes - include PCF** | Add Phase 5 with PCF integration tasks to call `/api/ai/rag/index-file` after uploads |
| Phase 4 Priority | Include Document Event Handler (Phase 4)? | **Yes - include Phase 4** | DocumentEventHandler implementation is in scope (+2 tasks) |
| Failure Behavior | What if RAG indexing fails during email automation? | **Silent warning** | Log warning, continue email processing - RAG is enhancement, not critical path |

## Assumptions

*Proceeding with these assumptions:*

- **Chunking defaults**: 4000 char chunks, 200 char overlap, preserve sentence boundaries (from design)
- **Idempotency TTL**: 7 days for RAG indexing idempotency keys (consistent with AI analysis)
- **Retry policy**: 3 max attempts for job handler (consistent with existing handlers)
- **Pre-extraction optimization**: Initial implementation without pre-extraction optimization; can add later

## Unresolved Questions

*None blocking - all clarifications received*

---

## Architecture Reference

### Core Principle: Single Service, Multiple Entry Points

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     UNIFIED RAG INDEXING ARCHITECTURE                       │
├─────────────────────────────────────────────────────────────────────────────┤
│   User Upload (PCF) ─────┐                                                  │
│   Email Automation ──────┼──► FileIndexingService ──► SHARED PIPELINE      │
│   Document Events ───────┘         │                        │               │
│                                    ▼                        ▼               │
│                          3 Entry Points           Chunk → Embed → Index    │
│                          (same output)            (identical for all)       │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Entry Points Summary

| Entry Point | Method | Auth | Text Source |
|-------------|--------|------|-------------|
| User Upload | `IndexFileAsync()` | OBO | Download via user token |
| Background (Download) | `IndexFileAppOnlyAsync()` | App-only | Download via app |
| Background (Pre-extracted) | `IndexContentAsync()` | App-only | Content provided |

---

## Files Summary

### New Files (10)

```
src/server/api/Sprk.Bff.Api/
├── Services/Ai/
│   ├── ITextChunkingService.cs
│   ├── TextChunkingService.cs
│   ├── IFileIndexingService.cs
│   └── FileIndexingService.cs
├── Services/Jobs/Handlers/
│   └── RagIndexingJobHandler.cs
└── Telemetry/
    └── RagTelemetry.cs

tests/
├── Services/Ai/TextChunkingServiceTests.cs
├── Services/Ai/FileIndexingServiceTests.cs
└── Services/Jobs/RagIndexingJobHandlerTests.cs
```

### Modified Files (~15)

```
src/server/api/Sprk.Bff.Api/
├── Api/Ai/RagEndpoints.cs                           # Add /index-file endpoint
├── Configuration/EmailProcessingOptions.cs          # Add AutoIndexToRag
├── Infrastructure/DI/AiModule.cs                    # Register services
├── Services/Ai/AppOnlyAnalysisService.cs           # Phase 0: Add Analysis records
├── Services/Jobs/Handlers/
│   ├── AppOnlyDocumentAnalysisJobHandler.cs        # Phase 0: Pass AnalysisId
│   ├── EmailToDocumentJobHandler.cs                # Phase 2: Add RAG enqueueing
│   └── DocumentEventHandler.cs                      # Phase 4: Implement handler
├── Telemetry/
│   ├── EmailTelemetry.cs                           # Add RAG telemetry
│   └── DocumentTelemetry.cs                        # Track AnalysisId
└── Services/Ai/Tools/
    ├── SummaryHandler.cs                           # Refactor chunking
    └── (6 other handlers)                          # Refactor chunking

src/client/pcf/                                      # Phase 5: PCF integration
```

---

*AI-optimized specification. Original design: design.md (2026-01-14)*
