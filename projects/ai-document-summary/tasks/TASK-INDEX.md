# AI Document Summary - Task Index

> **Project**: ai-document-summary
> **Created**: December 7, 2025
> **Total Tasks**: 20
> **Total Estimated Hours**: 128 hours (~16 dev days)

---

## Task Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| 1: Infrastructure & Configuration | 2 | 🔲 Not Started |
| 2: Text Extraction Service | 1 | 🔲 Not Started |
| 3: Summarization Service | 2 | 🔲 Not Started |
| 4: API Endpoints | 3 | 🔲 Not Started |
| 5: Dataverse Schema | 2 | 🔲 Not Started |
| 6: Frontend Integration | 4 | 🔲 Not Started |
| 7: Document Intelligence | 2 | 🔲 Not Started |
| 8: Production Hardening | 4 | 🔲 Not Started |
| 9: Wrap-up | 1 | 🔲 Not Started |

---

## All Tasks

| ID | Title | Phase | Status | Dependencies | Est. Hours |
|----|-------|-------|--------|--------------|------------|
| 001 | [Azure OpenAI Client Setup](001-azure-openai-client-setup.poml) | 1 | 🔲 not-started | none | 8 |
| 002 | [Configuration and KeyVault Integration](002-configuration-keyvault.poml) | 1 | 🔲 not-started | 001 | 4 |
| 010 | [Native Text Extraction Service](010-text-extraction-service.poml) | 2 | 🔲 not-started | none | 8 |
| 020 | [SummarizeService Core Implementation](020-summarize-service-core.poml) | 3 | 🔲 not-started | 001, 010 | 12 |
| 021 | [SummarizeJobHandler for Background Processing](021-summarize-job-handler.poml) | 3 | 🔲 not-started | 020 | 4 |
| 030 | [Streaming SSE Endpoint](030-streaming-endpoint.poml) | 4 | 🔲 not-started | 020, 032 | 8 |
| 031 | [Enqueue Endpoints (Single and Batch)](031-enqueue-endpoints.poml) | 4 | 🔲 not-started | 021, 032 | 8 |
| 032 | [AI Authorization Filter](032-authorization-filter.poml) | 4 | 🔲 not-started | none | 4 |
| 040 | [Add Dataverse Summary Fields](040-dataverse-fields.poml) | 5 | 🔲 not-started | none | 4 |
| 041 | [Update Dataverse Solution](041-solution-update.poml) | 5 | 🔲 not-started | 040 | 4 |
| 049 | [AiSummaryPanel Component (Single File)](049-ai-summary-panel.poml) | 6 | 🔲 not-started | none | 4 |
| 050 | [AiSummaryCarousel Component (Multi-File)](050-ai-summary-carousel.poml) | 6 | 🔲 not-started | 049 | 6 |
| 051 | [SSE Client Hook (useSseStream)](051-sse-client-hook.poml) | 6 | 🔲 not-started | none | 4 |
| 052 | [DocumentUploadForm Integration (Multi-File)](052-form-integration.poml) | 6 | 🔲 not-started | 030, 031, 049, 050, 051 | 8 |
| 060 | [Document Intelligence Integration (PDF/DOCX)](060-document-intelligence.poml) | 7 | 🔲 not-started | 010 | 8 |
| 061 | [Image File Support (Multimodal)](061-image-file-support.poml) | 7 | 🔲 not-started | 060 | 8 |
| 070 | [Error Handling](070-error-handling.poml) | 8 | 🔲 not-started | 020, 030 | 8 |
| 071 | [Monitoring and Alerting](071-monitoring-alerting.poml) | 8 | 🔲 not-started | 020 | 8 |
| 072 | [Rate Limiting and Circuit Breaker](072-rate-limiting.poml) | 8 | 🔲 not-started | 030 | 8 |
| 073 | [Documentation](073-documentation.poml) | 8 | 🔲 not-started | all | 6 |
| 090 | [Project Wrap-up](090-project-wrap-up.poml) | 9 | 🔲 not-started | 073 | 4 |

---

## Execution Order (Recommended)

### Sprint 8 - Backend Foundation (~44 hours)
1. **001** - Azure OpenAI Client Setup (no deps)
2. **010** - Native Text Extraction Service (no deps)
3. **032** - AI Authorization Filter (no deps)
4. **002** - Configuration and KeyVault Integration (needs 001)
5. **020** - SummarizeService Core Implementation (needs 001, 010)
6. **030** - Streaming SSE Endpoint (needs 020, 032)

### Sprint 9 - Frontend + Integration (~38 hours)
7. **040** - Add Dataverse Summary Fields (no deps)
8. **041** - Update Dataverse Solution (needs 040)
9. **049** - AiSummaryPanel Component (no deps) ← NEW
10. **050** - AiSummaryCarousel Component (needs 049)
11. **051** - SSE Client Hook (no deps)
12. **021** - SummarizeJobHandler (needs 020)
13. **031** - Enqueue Endpoints (needs 021, 032)
14. **052** - DocumentUploadForm Integration (needs 030, 031, 049, 050, 051)

### Sprint 10 - Polish + PDF/Image Support (~46 hours)
15. **060** - Document Intelligence Integration (needs 010)
16. **061** - Image File Support (needs 060) ← NEW
17. **070** - Error Handling (needs 020, 030)
18. **071** - Monitoring and Alerting (needs 020)
19. **072** - Rate Limiting and Circuit Breaker (needs 030)
20. **073** - Documentation (needs all)
21. **090** - Project Wrap-up (needs 073)

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
                       │              ▼
        ┌──────────────┼──────┐   ┌─────┐
        │              │      │   │ 060 │ Doc Intelligence
        ▼              ▼      ▼   └──┬──┘
     ┌─────┐       ┌─────┐  ┌─────┐  │
     │ 030 │       │ 021 │  │ 070 │  ▼
     └──┬──┘       └──┬──┘  └─────┘ ┌─────┐
   Stream              │   Error   │ 061 │ Image Support
        │              │           └─────┘
        │              ▼
        │          ┌─────┐
        │          │ 031 │ Enqueue
        │          └──┬──┘
        │              │
        └──────┬───────┘
               │
               ▼
           ┌─────┐       ┌─────┐       ┌─────┐
           │ 052 │◄──────│ 050 │◄──────│ 049 │ Panel
           └─────┘       └─────┘       └─────┘
        Form Integration   Carousel      │
               ▲                         │
               └─────────────────────────┘
               ▲
           ┌───┴───┐
           │  051  │ SSE Hook
           └───────┘

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

*Last updated: December 8, 2025*

*Last updated: December 7, 2025*
