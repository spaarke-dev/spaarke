# RAG Document Ingestion Pipeline - Implementation Plan

> **Status**: Approved
> **Created**: 2026-01-15
> **Source**: [spec.md](spec.md)

---

## Executive Summary

### Purpose

Implement a unified RAG document ingestion pipeline that enables automatic indexing of documents to Azure AI Search from multiple entry points (user upload, email automation, document events) using a single shared pipeline.

### Scope

- 6 implementation phases (Phase 0-5)
- Core services: ITextChunkingService, IFileIndexingService
- Job handler: RagIndexingJobHandler
- API endpoint: POST /api/ai/rag/index-file
- Email and event integrations
- PCF client integration

### Estimated Effort

~25-35 tasks across 6 phases

---

## Architecture Context

### Applicable ADRs

| ADR | Relevance | Key Constraint |
|-----|-----------|----------------|
| [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) | API endpoint | Minimal API pattern, no Azure Functions |
| [ADR-004](../../.claude/adr/ADR-004-job-contract.md) | Job handler | Job Contract schema, idempotency |
| [ADR-007](../../.claude/adr/ADR-007-spefilestore.md) | File download | Use SpeFileStore facade only |
| [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md) | Authorization | Endpoint filters, not global middleware |
| [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) | AI architecture | Extend BFF, no separate AI microservice |
| [ADR-014](../../.claude/adr/ADR-014-ai-caching.md) | Caching | Scope by tenant, include version in keys |
| [ADR-015](../../.claude/adr/ADR-015-ai-data-governance.md) | Data governance | Don't log document content |
| [ADR-016](../../.claude/adr/ADR-016-ai-rate-limits.md) | Rate limiting | Apply to AI endpoints |
| [ADR-017](../../.claude/adr/ADR-017-job-status.md) | Job status | Status persistence |
| [ADR-019](../../.claude/adr/ADR-019-problemdetails.md) | Errors | ProblemDetails for all errors |

### Discovered Resources

**Patterns**:
- `.claude/patterns/api/endpoint-definition.md` - Endpoint registration pattern
- `.claude/patterns/api/background-workers.md` - Job handler pattern
- `.claude/patterns/ai/text-extraction.md` - Text extraction pattern

**Canonical Implementations**:
- `Services/Jobs/Handlers/AppOnlyDocumentAnalysisJobHandler.cs` - Job handler pattern to follow
- `Services/Ai/IRagService.cs` - RAG service interface (IndexDocumentsBatchAsync)
- `Services/Ai/TextExtractorService.cs` - Text extraction service
- `Services/Ai/Tools/SummaryHandler.cs:479-517` - Chunking logic to extract

**Skills**:
- `adr-aware` - Auto-load ADRs during implementation
- `dataverse-deploy` - Deployment procedures
- `code-review` - Quality gate

**Scripts**:
- `scripts/Test-SdapBffApi.ps1` - API testing after deployment

### Integration Points

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     UNIFIED RAG INDEXING ARCHITECTURE                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   PCF Upload ────────────┐                                                  │
│   POST /index-file       │                                                  │
│                          ▼                                                  │
│                    ┌───────────────┐                                        │
│                    │ IndexFileAsync│  ← OBO Auth                            │
│                    │    (API)      │                                        │
│                    └───────┬───────┘                                        │
│                            │                                                │
│   Email Automation ──┐     │                                                │
│   RagIndexingJob     │     │                                                │
│                      ▼     │                                                │
│                 ┌──────────┴─────────┐                                      │
│                 │ IndexFileAppOnly   │  ← App-Only Auth                     │
│                 │  or IndexContent   │                                      │
│                 └──────────┬─────────┘                                      │
│                            │                                                │
│   Document Events ─────────┘                                                │
│                            │                                                │
│                            ▼                                                │
│   ┌─────────────────────────────────────────────────────────────────────┐  │
│   │                    SHARED PIPELINE                                   │  │
│   │  ┌────────┐   ┌────────┐   ┌────────┐   ┌────────────────────────┐ │  │
│   │  │ Chunk  │ → │ Build  │ → │ Embed  │ → │ Index (AI Search)      │ │  │
│   │  │ Text   │   │ Docs   │   │ OpenAI │   │ via RagService         │ │  │
│   │  └────────┘   └────────┘   └────────┘   └────────────────────────┘ │  │
│   └─────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## WBS (Work Breakdown Structure)

### Phase 0: Analysis Workflow Alignment

> **Priority**: CRITICAL (Prerequisite)
> **Objective**: Ensure AppOnlyAnalysisService creates Analysis records for consistency

**Deliverables**:
1. Add IDataverseService dependency to AppOnlyAnalysisService
2. Call CreateAnalysisAsync() before running playbook tools
3. Call CreateAnalysisOutputAsync() for each tool output
4. Dual-write: outputs to sprk_analysisoutput AND Document fields
5. Update AppOnlyDocumentAnalysisJobHandler telemetry
6. Unit tests for Analysis record creation

**Inputs**: Existing AppOnlyAnalysisService, AnalysisOrchestrationService (for reference)
**Outputs**: Aligned Analysis record creation in app-only flow
**Dependencies**: None

---

### Phase 1: Core Pipeline

> **Priority**: HIGH
> **Objective**: Create the foundational indexing services and API endpoint

**Deliverables**:
1. **ITextChunkingService + TextChunkingService**
   - Interface and implementation
   - Extract chunking logic from SummaryHandler.cs
   - Configurable chunk size, overlap, sentence boundaries
   - Unit tests

2. **IFileIndexingService + FileIndexingService**
   - Three entry point methods
   - Shared pipeline implementation
   - Integration with RagService
   - Unit tests

3. **RagIndexingJobHandler**
   - IJobHandler implementation
   - Idempotency handling
   - Telemetry integration
   - Unit tests

4. **RagTelemetry**
   - RAG-specific telemetry class
   - Job success/failure metrics

5. **POST /api/ai/rag/index-file endpoint**
   - Add to RagEndpoints.cs
   - Endpoint filter for authorization
   - Rate limiting

6. **DI Registration**
   - Register services in AiModule.cs
   - Register job handler

**Inputs**: spec.md, existing RagService, SpeFileStore, TextExtractor
**Outputs**: Functional indexing pipeline with API endpoint
**Dependencies**: None

---

### Phase 2: Email Integration

> **Priority**: HIGH
> **Objective**: Enable automatic RAG indexing for email-processed documents

**Deliverables**:
1. **AutoIndexToRag configuration**
   - Add to EmailProcessingOptions.cs
   - Default: false (enable after testing)

2. **EnqueueRagIndexingJobAsync method**
   - Add to EmailToDocumentJobHandler
   - Non-blocking (failures logged as warnings)
   - Uses try/catch pattern per spec

3. **RAG telemetry in EmailTelemetry**
   - RecordRagJobEnqueued
   - RecordRagJobEnqueueFailure

4. **Integration tests**
   - Email-to-RAG flow verification

**Inputs**: Phase 1 complete, existing EmailToDocumentJobHandler
**Outputs**: Email documents indexed to RAG when enabled
**Dependencies**: Phase 1

---

### Phase 3: Cleanup

> **Priority**: MEDIUM
> **Objective**: Consolidate duplicate chunking code across tool handlers

**Deliverables**:
1. **Refactor tool handlers to use ITextChunkingService**:
   - SummaryHandler.cs
   - Other 6 handlers with duplicate ChunkText()

2. **Verification**
   - No duplicate ChunkText() methods remain
   - All handlers use shared service

**Inputs**: Phase 1 complete (ITextChunkingService)
**Outputs**: Consolidated chunking logic, reduced code duplication
**Dependencies**: Phase 1

---

### Phase 4: Event-Driven

> **Priority**: MEDIUM
> **Objective**: Enable RAG indexing triggered by document events

**Deliverables**:
1. **Implement DocumentEventHandler.HandleDocumentCreatedAsync()**
   - Use FileIndexingService.IndexFileAppOnlyAsync()
   - Idempotency handling

2. **E2E tests**
   - Document event → RAG flow

**Inputs**: Phase 1 complete, existing DocumentEventHandler stub
**Outputs**: Document events trigger RAG indexing
**Dependencies**: Phase 1

---

### Phase 5: PCF Integration

> **Priority**: MEDIUM
> **Objective**: Enable client-side RAG indexing after file upload

**Deliverables**:
1. **PCF integration code**
   - indexDocumentToRag() function
   - Non-blocking (failures logged as warnings)
   - Call after successful SPE upload

2. **Manual UI testing**
   - Verify PCF → RAG flow

**Inputs**: Phase 1 complete (API endpoint)
**Outputs**: PCF uploads trigger RAG indexing
**Dependencies**: Phase 1

---

### Phase 6: Project Wrap-up

> **Priority**: LOW
> **Objective**: Complete project and archive

**Deliverables**:
1. Update README.md status to Complete
2. Create lessons-learned.md
3. Run /repo-cleanup
4. Merge to master

**Inputs**: All phases complete
**Outputs**: Archived project
**Dependencies**: Phases 0-5

---

## Dependencies

### External Dependencies

| Dependency | Status | Notes |
|------------|--------|-------|
| Azure OpenAI | ✅ Available | Embedding generation |
| Azure AI Search | ✅ Available | Index storage |
| Service Bus | ✅ Available | Job queue |
| email-to-document-automation-r2 | ✅ Complete | Prerequisite project |

### Internal Dependencies

| Component | Status | Notes |
|-----------|--------|-------|
| RagService | ✅ Complete | IndexDocumentsBatchAsync |
| TextExtractorService | ✅ Complete | Text extraction |
| EmbeddingCache | ✅ Complete | Redis caching |
| SpeFileStore | ✅ Complete | File download |
| JobSubmissionService | ✅ Complete | Job enqueueing |

---

## Testing Strategy

### Unit Testing

- TextChunkingService: Chunk size, overlap, sentence boundaries
- FileIndexingService: Each entry point, shared pipeline
- RagIndexingJobHandler: Idempotency, error handling, telemetry

### Integration Testing

- Email-to-RAG flow: Email → Document → RAG index
- API endpoint: Authorization, rate limiting, error handling

### E2E Testing

- Document event → RAG flow
- PCF upload → RAG index
- Search verification: Indexed documents searchable

---

## Acceptance Criteria

1. [ ] `AppOnlyAnalysisService` creates `sprk_analysis` records
2. [ ] `POST /api/ai/rag/index-file` indexes documents via OBO
3. [ ] `RagIndexingJobHandler` indexes documents via app-only
4. [ ] Both paths produce identical indexed results
5. [ ] Email archives indexed when `AutoIndexToRag=true`
6. [ ] Document events trigger RAG indexing
7. [ ] PCF calls indexing endpoint after upload
8. [ ] Works for Documents and orphan files
9. [ ] Indexed documents searchable via `/api/ai/rag/search`
10. [ ] Performance within targets
11. [ ] All existing tests pass
12. [ ] Duplicate `ChunkText()` methods removed

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Embedding generation latency | Medium | Medium | Use existing EmbeddingCache with 7-day TTL |
| Circuit breaker trips | Low | High | Follow existing Polly patterns in RagService |
| Large file processing timeout | Medium | Medium | Chunked processing, configurable timeouts |
| Job queue backlog | Low | Medium | Rate limiting, async processing |

---

## Next Steps

1. Run `/task-create` to decompose plan into executable task files
2. Review generated tasks
3. Begin with Phase 0 task 001
4. Execute tasks using `task-execute` skill

---

*Plan created: 2026-01-15*
*Source: spec.md, design.md*
