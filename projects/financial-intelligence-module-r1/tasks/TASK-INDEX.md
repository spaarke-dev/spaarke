# Finance Intelligence Module R1 — Task Index

> **Last Updated**: 2026-02-12
> **Total Tasks**: 35 (3 removed: 041, 043, 044)
> **Status**: ✅ **ALL COMPLETE** (35/35)

## Task Registry

### Phase 1: Foundation (Dataverse Schema + AI Platform Capability)

| # | Task | Status | Est. | Deps | Blocks | Rigor |
|---|------|--------|------|------|--------|-------|
| 001 | Create Finance Dataverse Entities (6 new entities) | ✅ | 6h | none | 010, 011, 014, 016, 017, 019 | STANDARD |
| 002 | Add Document Classification Fields (16 fields on sprk_document) | ✅ | 3h | none | 011, 013 | STANDARD |
| 003 | Create Dataverse Views (Invoice Review Queue, Active Invoices) | ✅ | 2h | 001 | 047 | MINIMAL |
| 004 | Add GetStructuredCompletionAsync&lt;T&gt; to OpenAI Client | ✅ | 4h | none | 005, 010 | FULL |
| 005 | Unit Tests for Structured Output Method | ✅ | 3h | 004 | none | STANDARD |
| 006 | Define Finance Record Types (C# records + JSON schemas) | ✅ | 3h | none | 010, 011, 016 | STANDARD |
| 007 | Write Classification Prompt Template (Playbook A) | ✅ | 3h | none | 011 | STANDARD |
| 008 | Write Extraction Prompt Template (Playbook B) | ✅ | 3h | none | 016 | STANDARD |
| 009 | Create FinanceOptions and AddFinanceModule DI Registration | ✅ | 3h | none | 010, 011, 016, 019 | FULL |

### Phase 2: AI Services + Job Handlers

| # | Task | Status | Est. | Deps | Blocks | Rigor |
|---|------|--------|------|------|--------|-------|
| 010 | Implement IInvoiceAnalysisService (classification + extraction) | ✅ | 6h | 004, 006, 009 | 011, 016 | FULL |
| 011 | Implement AttachmentClassificationJobHandler | ✅ | 6h | 001, 002, 006, 007, 009, 010 | 013 | FULL |
| 012 | Implement Entity Matching Signals (invoice-specific) | ✅ | 4h | 011 | none | FULL |
| 013 | Enqueue Classification from EmailToDocumentJobHandler | ✅ | 3h | 002, 011 | none | FULL |
| 014 | Implement Invoice Review Confirm Endpoint | ✅ | 4h | 001 | 015, 016 | FULL |
| 015 | Implement Invoice Review Reject Endpoint | ✅ | 3h | 014 | none | FULL |
| 016 | Implement InvoiceExtractionJobHandler | ✅ | 6h | 001, 010, 014 | 019, 032, 034 | FULL |
| 017 | Implement SpendSnapshotService | ✅ | 5h | 001 | 019, 020 | FULL |
| 018 | Implement SignalEvaluationService | ✅ | 4h | 001 | 019, 021 | FULL |
| 019 | Implement SpendSnapshotGenerationJobHandler | ✅ | 5h | 001, 009, 016, 017, 018 | 040 | FULL |
| 020 | Unit Tests: SpendSnapshot Aggregation | ✅ | 4h | 017 | none | STANDARD |
| 021 | Unit Tests: Signal Evaluation Rules | ✅ | 3h | 018 | none | STANDARD |
| 022 | Finance Endpoint Authorization Filter | ✅ | 3h | none | 014 | FULL |

### Phase 3: Invoice RAG + Search

| # | Task | Status | Est. | Deps | Blocks | Rigor |
|---|------|--------|------|------|--------|-------|
| 030 | Define Invoice Search Index Schema (JSON + Bicep) | ✅ | 4h | none | 031 | STANDARD |
| 031 | Deploy Invoice Search Index to Azure AI Search | ✅ | 2h | 030 | 032 | STANDARD |
| 032 | Implement InvoiceIndexingJobHandler | ✅ | 6h | 016, 031 | 033 | FULL |
| 033 | Implement InvoiceSearchService + Search Endpoint | ✅ | 5h | 032 | none | FULL |
| 034 | Wire Invoice Indexing into Extraction Job Chain | ✅ | 2h | 016 | none | FULL |

### Phase 4: VisualHost Configuration + Integration + Polish

| # | Task | Status | Est. | Deps | Blocks | Rigor |
|---|------|--------|------|------|--------|-------|
| 040 | Implement Finance Summary Endpoint (Redis-cached) | ✅ | 4h | 019 | none | FULL |
| 041 | ~~Finance Intelligence PCF Control~~ | ❌ REMOVED | 0h | - | - | - |
| 042 | Configure VisualHost Chart Definitions for Finance Metrics | ✅ | 2h | 002, 019 | none | MINIMAL |
| 043 | ~~PCF Panel: Active Signals + Invoice History~~ | ❌ REMOVED | 0h | - | - | - |
| 044 | ~~PCF Panel: Theming and Dark Mode~~ | ❌ REMOVED | 0h | - | - | - |
| 045 | Tune Classification Confidence Thresholds | ✅ | 4h | 011 | none | STANDARD |
| 046 | Tune Extraction Prompts with Real Invoice Samples | ✅ | 4h | 016 | none | STANDARD |
| 047 | Configure Invoice Review Queue Dataverse View | ✅ | 2h | 003 | none | MINIMAL |
| 048 | Integration Tests: Full Pipeline End-to-End | ✅ | 8h | 019, 032, 040 | none | STANDARD |
| 049 | Extend IDataverseService for Finance Entities | ✅ | 4h | none | 016, 019, 032 | FULL |

### Wrap-up

| # | Task | Status | Est. | Deps | Blocks | Rigor |
|---|------|--------|------|------|--------|-------|
| 090 | Project Wrap-up | ✅ | 3h | all | none | MINIMAL |

## Summary

| Metric | Value |
|--------|-------|
| Total Tasks | 35 (3 removed: 041, 043, 044; 1 added: 049) |
| Phase 1 (Foundation) | 9 tasks |
| Phase 2 (AI + Handlers) | 13 tasks |
| Phase 3 (RAG + Search) | 5 tasks |
| Phase 4 (VisualHost + Integration) | 7 tasks |
| Wrap-up | 1 task |
| FULL rigor tasks | 22 |
| STANDARD rigor tasks | 11 |
| MINIMAL rigor tasks | 4 |
| Estimated total effort | ~155 hours |

## Dependency Graph

```
Phase 1 (Foundation):
  001 ──┬── 003 ─── 047
        ├── 010* ──┬── 011* ──┬── 012
        ├── 014 ──┬── 015    ├── 013
        ├── 016*  │          └── 045
        ├── 017 ──┤
        └── 019   │
  002 ──┬── 011*  │
        └── 013   │
  004 ──┬── 005   │
        └── 010*  │
  006 ──┬── 010*  │
        ├── 011*  │
        └── 016*  │
  007 ──── 011*   │
  008 ──── 016*   │
  009 ──┬── 010*  │
        ├── 011*  │
        ├── 016*  │
        └── 019   │

Phase 2 (AI + Handlers):
  010 ──┬── 011 ──── 013
        └── 016 ──┬── 019 ──── 040 ──── 048
                  ├── 032 ──── 033
                  ├── 034
                  └── 046
  017 ──┬── 019
        └── 020
  018 ──┬── 019
        └── 021
  022 ──── 014

Phase 3 (RAG + Search):
  030 ──── 031 ──── 032

Phase 4 (VisualHost + Integration):
  002, 019 ──── 042 (VisualHost chart definitions)
  003 ──── 047 (Review queue view)
  011 ──── 045 (Tune classification thresholds)
  016 ──── 046 (Tune extraction prompts)
  019, 032, 040 ──── 048 (Integration tests)
  049 (extends IDataverseService, blocks 016, 019, 032)
```

`*` = Task has multiple inbound dependencies (convergence point)

## Critical Path

The longest dependency chain determines the minimum project duration:

```
004 → 010 → 016 → 019 → 040 → 048 → 090
  4h    6h    6h    5h    4h    8h    3h  = 36h
```

**Alternate critical path (through RAG):**
```
004 → 010 → 016 → 032 → 033
  4h    6h    6h    6h    5h = 27h
```

**Note:** With Tasks 041, 043, 044 removed, the critical path is significantly shorter (~36h vs ~52h).

## Parallel Execution Groups

Tasks within a group can execute simultaneously when their prerequisites are met.

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 002, 004, 006, 007, 008, 009 | none | Phase 1 foundation — 7 tasks with no dependencies |
| B | 003, 005 | A (partial: 001, 004) | Views + structured output tests |
| C | 020, 021 | 017, 018 | Snapshot + signal unit tests (independent) |
| D | 012, 013, 045 | 011 | Post-classification tasks (independent) |
| E | 030, 034, 046 | 016 | Post-extraction: index schema, chain wiring, prompt tuning |
| F | 042, 047, 049 | 002, 003, 019 | VisualHost config + review queue view + IDataverseService |
| G | 033, 048 | 032, 019, 040 | Search service + integration tests |

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 004 | Extending IOpenAiClient with structured output — interface change affects consumers | Unit tests in 005; use extension method if interface change blocked |
| 011 | Classification accuracy depends on prompt quality | Feature flag (AutoClassifyAttachments: false); tune in 045 |
| 016 | Extraction handler is most complex (AI + Dataverse + job chaining) | Full rigor protocol; 6 inbound/outbound dependencies |
| 019 | Snapshot handler convergence point (4 dependencies) | Test aggregation math independently in 020 |
| 049 | IDataverseService extensions block multiple handlers | Implement early; 4h estimate may be conservative |

## Progress Tracking

| Phase | Total | Completed | Remaining |
|-------|-------|-----------|-----------|
| Phase 1 | 9 | 9 | 0 |
| Phase 2 | 13 | 13 | 0 |
| Phase 3 | 5 | 5 | 0 |
| Phase 4 | 7 | 7 (040, 042, 045, 046, 047, 048, 049) | 0 |
| Wrap-up | 1 | 1 (090) | 0 |
| **Total** | **35** | **35** | **0** |

**Note:** Tasks 041, 043, 044 removed (using VisualHost instead of custom PCF). Task 049 added (extend IDataverseService).

## Completion Summary

**Project Status**: ✅ **COMPLETE**
**Completion Date**: 2026-02-12
**Total Effort**: ~155 hours (estimated)
**Success Rate**: 100% (35/35 tasks completed)
