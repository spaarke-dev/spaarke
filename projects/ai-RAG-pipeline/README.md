# RAG Document Ingestion Pipeline

> **Status**: In Progress
> **Phase**: Planning
> **Created**: 2026-01-15

---

## Quick Links

- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Design Specification](spec.md)
- [Original Design](design.md)

---

## Overview

This project implements a unified RAG (Retrieval-Augmented Generation) document ingestion pipeline that enables automatic indexing of documents to Azure AI Search. The pipeline supports three entry points (user upload, email automation, document events) that converge to a single shared chunking/embedding/indexing pipeline, ensuring consistent searchability regardless of how documents enter the system.

---

## Problem Statement

**Root Cause**: No trigger mechanism currently exists to invoke the RAG indexing pipeline when documents are uploaded or created.

**Impact**:
- Documents uploaded via PCF controls are not automatically indexed for RAG search
- Email archives processed by email-to-document automation are not searchable
- Document events do not trigger indexing
- Duplicate chunking logic exists across 7+ tool handlers

**Underlying Issue**: While RAG infrastructure (RagService, TextExtractor, EmbeddingCache) exists and is complete, there's no unified service to orchestrate the text → chunk → embed → index pipeline from various entry points.

---

## Proposed Solution

Create `IFileIndexingService` as the **single source of truth** for RAG indexing with:

1. **Three Entry Points**:
   - `IndexFileAsync()` - OBO auth for user-triggered indexing via API
   - `IndexFileAppOnlyAsync()` - App-only auth for background jobs with file download
   - `IndexContentAsync()` - Pre-extracted content for optimized background processing

2. **Single Shared Pipeline**:
   - All entry points converge to identical chunking/embedding/indexing logic
   - Uses existing `IRagService.IndexDocumentsBatchAsync()` for actual indexing
   - Shared `ITextChunkingService` eliminates duplicate chunking code

3. **Integration Points**:
   - New API endpoint: `POST /api/ai/rag/index-file`
   - New job handler: `RagIndexingJobHandler`
   - Email integration: `EnqueueRagIndexingJobAsync` in `EmailToDocumentJobHandler`
   - PCF client integration for post-upload indexing

---

## Scope

### In Scope

- **Phase 0**: Analysis workflow alignment (AppOnlyAnalysisService creates sprk_analysis records)
- **Phase 1**: Core pipeline (ITextChunkingService, IFileIndexingService, RagIndexingJobHandler, API endpoint)
- **Phase 2**: Email integration (AutoIndexToRag config, RAG job enqueueing)
- **Phase 3**: Cleanup (refactor 7 tool handlers to use shared chunking)
- **Phase 4**: Event-driven (DocumentEventHandler implementation)
- **Phase 5**: PCF integration (client-side RAG indexing call)

### Out of Scope

- Changes to existing RAG search functionality (`/api/ai/rag/search`)
- Modifications to Azure AI Search index schema
- Multi-tenant deployment routing changes (already implemented)
- Batch import from external sources (future enhancement)
- Real-time sync with SharePoint webhooks (future enhancement)

---

## Graduation Criteria

### Functional Requirements

- [ ] `AppOnlyAnalysisService` creates `sprk_analysis` records for background analyses
- [ ] `POST /api/ai/rag/index-file` indexes documents via OBO authentication
- [ ] `RagIndexingJobHandler` indexes documents via app-only authentication
- [ ] Both OBO and app-only paths produce **identical** indexed results
- [ ] Email archives are automatically indexed when `AutoIndexToRag=true`
- [ ] Document events trigger RAG indexing via `DocumentEventHandler`
- [ ] PCF calls indexing endpoint after successful upload
- [ ] Works for Documents (with documentId) and orphan files (without)
- [ ] Indexed documents are searchable via `/api/ai/rag/search`

### Quality Requirements

- [ ] All existing tests continue to pass
- [ ] New unit tests for TextChunkingService, FileIndexingService, RagIndexingJobHandler
- [ ] Integration tests for email-to-RAG flow
- [ ] E2E tests for document event → RAG flow
- [ ] Duplicate `ChunkText()` methods removed from tool handlers

### Performance Requirements

- [ ] Small file (<1MB): < 5 seconds
- [ ] Medium file (1-10MB): < 15 seconds
- [ ] Large file (10-50MB): < 60 seconds

---

## Key Decisions

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-01-15 | Include PCF integration (Phase 5) | Owner confirmed client-side RAG call is in scope |
| 2026-01-15 | Include Phase 4 (Document Events) | Owner confirmed DocumentEventHandler implementation is in scope |
| 2026-01-15 | RAG failures use silent warning | RAG is enhancement, not critical - should not fail email processing |

---

## Related Documentation

- [AI Architecture Guide](../../docs/guides/SPAARKE-AI-ARCHITECTURE.md)
- [ADR-013: AI Architecture](../../.claude/adr/ADR-013-ai-architecture.md)
- [ADR-004: Job Contract](../../.claude/adr/ADR-004-job-contract.md)
- [Background Workers Pattern](../../.claude/patterns/api/background-workers.md)

---

*Project initialized: 2026-01-15*
