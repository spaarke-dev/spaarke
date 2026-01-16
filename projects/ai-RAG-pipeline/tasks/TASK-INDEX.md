# RAG Document Ingestion Pipeline - Task Index

> **Auto-updated by task-execute skill**
> **Project**: ai-RAG-pipeline
> **Last Updated**: 2026-01-16

---

## Summary

| Metric | Value |
|--------|-------|
| Total Tasks | 27 |
| Completed | 25 |
| In Progress | 0 |
| Pending | 2 |

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not started |
| 🔄 | In progress |
| ✅ | Completed |
| ⏸️ | Blocked |
| ⏭️ | Deferred |

---

## Task List

### Phase 0: Analysis Workflow Alignment (Critical Prerequisite)

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 001 | Add IDataverseService dependency to AppOnlyAnalysisService | ✅ | none | FULL |
| 002 | Create Analysis record before running playbook tools | ✅ | 001 | FULL |
| 003 | Create AnalysisOutput records for tool outputs | ✅ | 002 | FULL |
| 004 | Update AppOnlyDocumentAnalysisJobHandler telemetry | ✅ | 003 | STANDARD |
| 005 | Unit tests for Phase 0 changes | ✅ | 003 | STANDARD |

### Phase 1: Core Pipeline

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 010 | Create ITextChunkingService interface | ✅ | none | FULL |
| 011 | Implement TextChunkingService | ✅ | 010 | FULL |
| 012 | Create IFileIndexingService interface | ✅ | none | FULL |
| 013 | Implement FileIndexingService | ✅ | 011, 012 | FULL |
| 014 | Create RagIndexingJobHandler | ✅ | 013 | FULL |
| 015 | Create RagTelemetry class | ✅ | none | STANDARD |
| 016 | Add POST /api/ai/rag/index-file endpoint | ✅ | 013 | FULL |
| 017 | Register services in AiModule.cs | ✅ | 014, 016 | STANDARD |
| 018 | Unit tests for TextChunkingService | ✅ | 011 | STANDARD |
| 019 | Unit tests for FileIndexingService and RagIndexingJobHandler | ✅ | 014 | STANDARD |

### Phase 2: Email Integration

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 020 | Add AutoIndexToRag to EmailProcessingOptions | ✅ | 017 | STANDARD |
| 021 | Add EnqueueRagIndexingJobAsync to EmailToDocumentJobHandler | ✅ | 020 | FULL |
| 022 | Add RAG telemetry to EmailTelemetry | ✅ | 015 | STANDARD |
| 023 | Integration tests for email-to-RAG flow | ✅ | 021, 022 | STANDARD |

### Phase 3: Cleanup

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 030 | Refactor SummaryHandler to use ITextChunkingService | ✅ | 017 | FULL |
| 031 | Refactor remaining tool handlers (6 handlers) | ✅ | 030 | FULL |
| 032 | Verify no duplicate ChunkText methods remain | ✅ | 031 | MINIMAL |

### Phase 4: Event-Driven

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 040 | Implement DocumentEventHandler.HandleDocumentCreatedAsync | ✅ | 017 | FULL |
| 041 | E2E tests for document event to RAG flow | ✅ | 040 | STANDARD |

### Phase 5: PCF Integration

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 050 | Implement PCF RAG indexing call after upload | ✅ | 016 | FULL |
| 051 | Manual UI testing for PCF to RAG flow | 🔲 | 050 | MINIMAL |

### Phase 6: Project Wrap-up

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 090 | Project wrap-up | 🔲 | all | FULL |

---

## Critical Path

```
001 → 002 → 003 → Phase 0 Complete
         ↓
010 → 011 ─┬→ 013 → 014 → 017 → Phase 1 Core Ready
012 ───────┘      │
                  ↓
            016 (API endpoint)
                  │
         ┌────────┼────────┐
         ↓        ↓        ↓
       020-023  030-032  040-041
       (Email)  (Clean)  (Events)
                  │
                  ↓
              050-051 (PCF)
                  │
                  ↓
                090 (Wrap-up)
```

---

## Rigor Level Distribution

| Level | Count | Tasks |
|-------|-------|-------|
| FULL | 15 | 001-003, 010-014, 016, 021, 030-031, 040, 050, 090 |
| STANDARD | 10 | 004-005, 015, 017-020, 022-023, 041 |
| MINIMAL | 2 | 032, 051 |

---

## Notes

- **Phase 0 is prerequisite**: Must complete before visible integration testing
- **Phases 2-4 can partially parallel**: After Phase 1 core is complete
- **Phase 5 depends on Phase 1 API endpoint**: Task 016 must be deployed
- **Task 090 is mandatory final task**: Runs quality gates and cleanup

---

*Updated by task-execute skill as tasks progress*
