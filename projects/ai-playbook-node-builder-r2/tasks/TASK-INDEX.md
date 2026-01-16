# AI Chat Playbook Builder - Task Index

> **Project**: ai-chat-playbook-builder
> **Generated**: 2026-01-16
> **Total Tasks**: 48

## Status Legend

| Icon | Status |
|------|--------|
| 🔲 | Pending |
| 🔄 | In Progress |
| ✅ | Completed |
| ⏭️ | Skipped |

---

## Phase 1: Infrastructure (9 tasks)

| Task | Title | Status | Dependencies | Estimate |
|------|-------|--------|--------------|----------|
| 001 | Create AiPlaybookBuilderService class | 🔲 | none | 1d |
| 002 | Add /api/ai/build-playbook-canvas SSE endpoint | 🔲 | 001 | 1d |
| 003 | Define canvas patch schema | 🔲 | none | 0.5d |
| 004 | Implement Dataverse scope CRUD operations | 🔲 | none | 1.5d |
| 005 | Implement N:N link table operations | 🔲 | 004 | 1d |
| 006 | Extend PlaybookExecutionEngine with conversational mode | 🔲 | none | 1.5d |
| 007 | Add ConversationContext and CanvasState models | 🔲 | 006 | 0.5d |
| 008 | Add endpoint authorization filter | 🔲 | 002 | 0.5d |
| 009 | Add rate limiting middleware | 🔲 | 002 | 0.5d |

**Phase 1 Deliverables**:
- `AiPlaybookBuilderEndpoints.cs`
- `AiPlaybookBuilderService.cs`
- `CanvasPatch.cs` models
- Conversational execution mode in engine

---

## Phase 2: PCF Components (9 tasks)

| Task | Title | Status | Dependencies | Estimate |
|------|-------|--------|--------------|----------|
| 010 | Create aiAssistantStore (Zustand) | 🔲 | none | 0.5d |
| 011 | Build AiAssistantModal container component | 🔲 | 010 | 1d |
| 012 | Build ChatHistory component | 🔲 | 010 | 1d |
| 013 | Build ChatInput component | 🔲 | 010 | 0.5d |
| 014 | Build OperationFeedback component | 🔲 | 010 | 0.5d |
| 015 | Create AiPlaybookService API client | 🔲 | Phase 1 | 0.5d |
| 016 | Wire SSE streaming to store | 🔲 | 010, 015 | 1d |
| 017 | Add toolbar button to toggle modal | 🔲 | 011 | 0.5d |
| 018 | Apply canvas patches from stream | 🔲 | 016 | 0.5d |

**Phase 2 Deliverables**:
- `aiAssistantStore.ts`
- `AiAssistant/` component folder
- `AiPlaybookService.ts`
- Toolbar integration

---

## Phase 3: AI Integration + Builder Scopes (10 tasks)

| Task | Title | Status | Dependencies | Estimate |
|------|-------|--------|--------------|----------|
| 020 | Design system prompt for canvas building | 🔲 | none | 1d |
| 021 | Implement intent classification (11 categories) | 🔲 | 020 | 1d |
| 022 | Implement entity resolution with confidence | 🔲 | 021 | 1d |
| 023 | Implement clarification loop for ambiguous input | 🔲 | 021 | 0.5d |
| 024 | Implement build plan generation | 🔲 | 021 | 1d |
| 025 | Create ACT-BUILDER-001 through ACT-BUILDER-005 | 🔲 | none | 0.5d |
| 026 | Create SKL-BUILDER-001 through SKL-BUILDER-005 | 🔲 | none | 0.5d |
| 027 | Create TL-BUILDER-001 through TL-BUILDER-009 definitions | 🔲 | none | 0.5d |
| 028 | Create KNW-BUILDER-001 through KNW-BUILDER-004 content | 🔲 | none | 0.5d |
| 029 | Implement ModelSelector for tiered selection | 🔲 | none | 0.5d |

**Phase 3 Deliverables**:
- Intent classification system
- Builder scope records in Dataverse
- PB-BUILDER playbook definition
- ModelSelector service

---

## Phase 4: Test Execution (9 tasks)

| Task | Title | Status | Dependencies | Estimate |
|------|-------|--------|--------------|----------|
| 030 | Add /api/ai/test-playbook-execution endpoint | 🔲 | Phase 1 | 0.5d |
| 031 | Implement mock test with sample data generation | 🔲 | 030 | 1d |
| 032 | Implement temp blob storage service (24hr TTL) | 🔲 | none | 1d |
| 033 | Implement quick test with temp blob | 🔲 | 030, 032 | 1d |
| 034 | Integrate Document Intelligence for quick test | 🔲 | 033 | 0.5d |
| 035 | Implement production test (full flow) | 🔲 | 030 | 0.5d |
| 036 | Build test options dialog in PCF | 🔲 | Phase 2 | 0.5d |
| 037 | Build test execution progress view | 🔲 | 036 | 0.5d |
| 038 | Add test result preview/download | 🔲 | 037 | 0.5d |

**Phase 4 Deliverables**:
- Test execution endpoint
- Three test modes operational
- Temp blob storage with TTL
- PCF test UI components

---

## Phase 5: Scope Management (10 tasks)

| Task | Title | Status | Dependencies | Estimate |
|------|-------|--------|--------------|----------|
| 040 | Add ownership fields to Dataverse schema | 🔲 | none | 0.5d |
| 041 | Implement ownership validation (SYS- immutable) | 🔲 | 040 | 0.5d |
| 042 | Implement "Save As" for playbooks | 🔲 | 041 | 0.5d |
| 043 | Implement "Save As" for scopes | 🔲 | 041 | 0.5d |
| 044 | Implement "Extend" with inheritance | 🔲 | 041 | 1d |
| 045 | Implement duplicate name handling (suffix) | 🔲 | 043 | 0.5d |
| 046 | Build Scope Browser component | 🔲 | Phase 2 | 1d |
| 047 | Add scope creation dialogs | 🔲 | 046 | 0.5d |
| 048 | Add GenericAnalysisHandler for configurable tools | 🔲 | none | 1d |
| 049 | Implement proactive scope gap detection | 🔲 | 046 | 0.5d |

**Phase 5 Deliverables**:
- Ownership fields in Dataverse
- Save As / Extend functionality
- Scope Browser UI
- GenericAnalysisHandler

---

## Phase 6: Polish (9 tasks)

| Task | Title | Status | Dependencies | Estimate |
|------|-------|--------|--------------|----------|
| 050 | Implement comprehensive error handling | 🔲 | All phases | 0.5d |
| 051 | Add retry logic with backoff | 🔲 | 050 | 0.5d |
| 052 | Add loading states and animations | 🔲 | Phase 2 | 0.5d |
| 053 | Implement keyboard shortcuts (Cmd/Ctrl+K) | 🔲 | Phase 2 | 0.5d |
| 054 | Responsive modal sizing | 🔲 | Phase 2 | 0.5d |
| 055 | Dark mode verification | 🔲 | Phase 2 | 0.5d |
| 056 | Write user documentation | 🔲 | All phases | 0.5d |
| 057 | Code review and cleanup | 🔲 | All phases | 0.5d |
| 058 | Final integration testing | 🔲 | All phases | 0.5d |

**Phase 6 Deliverables**:
- Polished UX
- Documentation
- Clean codebase

---

## Wrap-up (1 task)

| Task | Title | Status | Dependencies | Estimate |
|------|-------|--------|--------------|----------|
| 090 | Project wrap-up and cleanup | 🔲 | All tasks | 0.5d |

---

## Critical Path

The following tasks are on the critical path:

1. **001** → **002** → **008/009** (API foundation)
2. **010** → **011** → **016** → **018** (PCF foundation)
3. **020** → **021** → **022/023/024** (AI classification)
4. **030** → **031/033/035** → **036** → **037** → **038** (Test execution)
5. **040** → **041** → **042/043/044** (Scope management)

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 021 | Intent classification accuracy | Clarification loops, confidence thresholds |
| 033 | Quick test integration complexity | Document Intelligence may need extra config |
| 044 | Inheritance complexity | Single-level only, thorough testing |

---

## Dependencies Graph

```
Phase 1 (Infrastructure)
├── 001 → 002 → 008, 009
├── 003 (parallel)
├── 004 → 005
└── 006 → 007

Phase 2 (PCF Components)
├── 010 → 011, 012, 013, 014
├── 015 (needs Phase 1)
├── 010 + 015 → 016 → 018
└── 011 → 017

Phase 3 (AI Integration)
├── 020 → 021 → 022, 023, 024
├── 025, 026, 027, 028, 029 (parallel)
└── All → PB-BUILDER meta-playbook

Phase 4 (Test Execution)
├── 030 → 031, 033, 035
├── 032 → 033 → 034
└── Phase 2 → 036 → 037 → 038

Phase 5 (Scope Management)
├── 040 → 041 → 042, 043, 044, 045
├── Phase 2 → 046 → 047, 049
└── 048 (parallel)

Phase 6 (Polish)
└── All phases → 050-058 (mostly parallel)
```

---

*Generated by project-pipeline on 2026-01-16*
