# AI Document Summary - Task Index

> **Project**: ai-document-summary
> **Created**: December 7, 2025
> **Total Tasks**: 17

---

## Task Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| 1: Infrastructure & Configuration | 2 | 🔲 Not Started |
| 2: Text Extraction Service | 1 | 🔲 Not Started |
| 3: Summarization Service | 2 | 🔲 Not Started |
| 4: API Endpoints | 3 | 🔲 Not Started |
| 5: Dataverse Schema | 2 | 🔲 Not Started |
| 6: Frontend Integration | 3 | 🔲 Not Started |
| 7: Document Intelligence | 1 | 🔲 Not Started |
| 8: Production Hardening | 4 | 🔲 Not Started |
| 9: Wrap-up | 1 | 🔲 Not Started |

---

## All Tasks

| ID | Title | Phase | Status | Dependencies | Est. Hours |
|----|-------|-------|--------|--------------|------------|
| 001 | [Azure OpenAI Client Setup](001-azure-openai-client-setup.poml) | 1 | 🔲 not-started | none | 4 |
| 002 | [Configuration and KeyVault Integration](002-configuration-keyvault.poml) | 1 | 🔲 not-started | 001 | 2 |
| 010 | [Native Text Extraction Service](010-text-extraction-service.poml) | 2 | 🔲 not-started | none | 4 |
| 020 | [SummarizeService Core Implementation](020-summarize-service-core.poml) | 3 | 🔲 not-started | 001, 010 | 6 |
| 021 | [SummarizeJobHandler for Background Processing](021-summarize-job-handler.poml) | 3 | 🔲 not-started | 020 | 4 |
| 030 | [Streaming SSE Endpoint](030-streaming-endpoint.poml) | 4 | 🔲 not-started | 020 | 4 |
| 031 | [Enqueue Endpoints (Single and Batch)](031-enqueue-endpoints.poml) | 4 | 🔲 not-started | 021 | 4 |
| 032 | [AI Authorization Filter](032-authorization-filter.poml) | 4 | 🔲 not-started | none | 2 |
| 040 | [Add Dataverse Summary Fields](040-dataverse-fields.poml) | 5 | 🔲 not-started | none | 2 |
| 041 | [Update Dataverse Solution](041-solution-update.poml) | 5 | 🔲 not-started | 040 | 2 |
| 050 | [AiSummaryCarousel Component (Multi-File)](050-ai-summary-carousel.poml) | 6 | 🔲 not-started | none | 8 |
| 051 | [SSE Client Hook (useSseStream)](051-sse-client-hook.poml) | 6 | 🔲 not-started | none | 4 |
| 052 | [DocumentUploadForm Integration (Multi-File)](052-form-integration.poml) | 6 | 🔲 not-started | 030, 031, 050, 051 | 6 |
| 060 | [Document Intelligence Integration (PDF/DOCX)](060-document-intelligence.poml) | 7 | 🔲 not-started | 010 | 6 |
| 070 | [Error Handling](070-error-handling.poml) | 8 | 🔲 not-started | 020, 030 | 4 |
| 071 | [Monitoring and Alerting](071-monitoring-alerting.poml) | 8 | 🔲 not-started | 020 | 4 |
| 072 | [Rate Limiting and Circuit Breaker](072-rate-limiting.poml) | 8 | 🔲 not-started | 030 | 4 |
| 073 | [Documentation](073-documentation.poml) | 8 | 🔲 not-started | all | 3 |
| 090 | [Project Wrap-up](090-project-wrap-up.poml) | 9 | 🔲 not-started | 073 | 2 |

---

## Execution Order (Recommended)

### Sprint 8 - Backend Foundation
1. **001** - Azure OpenAI Client Setup (no deps)
2. **010** - Native Text Extraction Service (no deps)
3. **032** - AI Authorization Filter (no deps)
4. **002** - Configuration and KeyVault Integration (needs 001)
5. **020** - SummarizeService Core Implementation (needs 001, 010)
6. **030** - Streaming SSE Endpoint (needs 020)

### Sprint 9 - Frontend + Integration
7. **040** - Add Dataverse Summary Fields (no deps)
8. **041** - Update Dataverse Solution (needs 040)
9. **050** - AiSummaryCarousel Component (no deps)
10. **051** - SSE Client Hook (no deps)
11. **021** - SummarizeJobHandler (needs 020)
12. **031** - Enqueue Endpoints (needs 021)
13. **052** - DocumentUploadForm Integration (needs 030, 031, 050, 051)

### Sprint 10 - Polish + PDF Support
14. **060** - Document Intelligence Integration (needs 010)
15. **070** - Error Handling (needs 020, 030)
16. **071** - Monitoring and Alerting (needs 020)
17. **072** - Rate Limiting and Circuit Breaker (needs 030)
18. **073** - Documentation (needs all)
19. **090** - Project Wrap-up (needs 073)

---

## Dependency Graph

```
                    ┌─────┐
                    │ 001 │ Azure OpenAI Client
                    └──┬──┘
                       │
        ┌──────────────┼──────────────┐
        │              │              │
        ▼              ▼              ▼
     ┌─────┐       ┌─────┐       ┌─────┐
     │ 002 │       │ 020 │◄──────│ 010 │ Text Extraction
     └─────┘       └──┬──┘       └──┬──┘
   Config              │              │
                       │              │
        ┌──────────────┼──────┐      │
        │              │      │      │
        ▼              ▼      ▼      ▼
     ┌─────┐       ┌─────┐  ┌─────┐ ┌─────┐
     │ 030 │       │ 021 │  │ 070 │ │ 060 │ Doc Intelligence
     └──┬──┘       └──┬──┘  └─────┘ └─────┘
   Stream              │   Error
        │              │
        │              ▼
        │          ┌─────┐
        │          │ 031 │ Enqueue
        │          └──┬──┘
        │              │
        └──────┬───────┘
               │
               ▼
           ┌─────┐       ┌─────┐       ┌─────┐
           │ 052 │◄──────│ 050 │       │ 051 │
           └─────┘       └─────┘       └─────┘
        Form Integration   Carousel    SSE Hook

     Parallel: 040 → 041 (Dataverse)
     Parallel: 071, 072 (Hardening)
     Final: 073 → 090 (Docs, Wrap-up)
```

---

## Status Legend

- 🔲 `not-started` - Task not yet begun
- 🔄 `in-progress` - Currently being worked
- ⏸️ `blocked` - Waiting on dependency or external input
- ✅ `completed` - All deliverables and criteria met
- ⏭️ `deferred` - Postponed (with reason)

---

## Quick Commands

```bash
# Execute first available task
/task-execute 001

# Check task status
cat projects/ai-document-summary/tasks/TASK-INDEX.md

# View specific task
cat projects/ai-document-summary/tasks/001-azure-openai-client-setup.poml
```

---

*Last updated: December 7, 2025*
