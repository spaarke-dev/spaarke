# Task Index — SprkChat Interactive Collaboration (R2)

> **Last Updated**: 2026-02-25
> **Total Tasks**: 89
> **Status**: Ready for Execution

---

## Status Legend

| Icon | Status |
|------|--------|
| 🔲 | Not started |
| 🔄 | In progress |
| ✅ | Completed |
| 🚫 | Blocked |

---

## Phase 0: Legacy Cleanup (Prerequisite)

| # | Task | Tags | Dependencies | Status |
|---|------|------|-------------|--------|
| 001 | Remove Legacy Chat Frontend Code | `frontend, cleanup, pcf` | — | 🔲 |
| 002 | Remove Deprecated Backend Endpoints | `bff-api, cleanup` | — | 🔲 |
| 003 | Verify Legacy Cleanup Completeness | `testing, cleanup` | 001, 002 | 🔲 |

**Parallel**: 001 and 002 can run simultaneously (no shared files).

---

## Phase 1: Foundation (Sprint 1 — 3 Parallel Tracks)

### Track 1: Package A — SprkChat Side Pane (`sprint1-track1`)

| # | Task | Tags | Dependencies | Placeholders | Status |
|---|------|------|-------------|-------------|--------|
| 010 | Scaffold SprkChatPane Code Page | `code-page, frontend, scaffold` | — | PH-010-A, PH-010-B → 012 | 🔲 |
| 011 | Implement SprkChatBridge Cross-Pane | `frontend, ui-components, shared` | — | — | 🔲 |
| 012 | Wire SprkChat into SprkChatPane | `code-page, frontend` | 010, 011 | — | 🔲 |
| 013 | Implement Side Pane Authentication | `code-page, frontend, auth` | 012 | — | 🔲 |
| 014 | Context Auto-Detection & Session Persistence | `code-page, frontend` | 012, 013 | — | 🔲 |
| 015 | Create Side Pane Launcher Script | `frontend, dataverse` | 010 | PH-015-A (icon) | 🔲 |
| 016 | Build & Deploy SprkChatPane Code Page | `deploy, code-page` | 012-015 | — | 🔲 |
| 017 | SprkChatBridge Integration Tests | `testing, frontend` | 011 | — | 🔲 |
| 018 | SprkChatPane Responsive Layout & Dark Mode | `frontend, fluent-ui, accessibility` | 012 | — | 🔲 |

### Track 2: Package B — Streaming Write Engine (`sprint1-track2`)

| # | Task | Tags | Dependencies | Placeholders | Status |
|---|------|------|-------------|-------------|--------|
| 025 | Define SSE Document Stream Events | `bff-api, frontend, sse` | — | — | 🔲 |
| 026 | Implement WorkingDocumentTools | `bff-api, ai, tools` | 025 | PH-026 → 028 | 🔲 |
| 027 | Register WorkingDocumentTools in Factory | `bff-api, ai` | 026 | — | 🔲 |
| 028 | Implement Streaming Write Flow (Inner LLM) | `bff-api, ai, streaming` | 026, 027 | — | 🔲 |
| 029 | WorkingDocumentTools Unit Tests | `testing, bff-api` | 028 | — | 🔲 |
| 030 | Create StreamingInsertPlugin (Lexical) | `frontend, lexical, ui-components` | — | — | 🔲 |
| 031 | Extend RichTextEditor Streaming API | `frontend, lexical, ui-components` | 030 | — | 🔲 |
| 032 | Extend RichTextEditor Selection API | `frontend, lexical, ui-components` | — | — | 🔲 |
| 033 | Implement useDocumentHistory Hook | `frontend, ui-components` | — | — | 🔲 |
| 034 | Wire Streaming Write E2E (SSE→Bridge→Editor) | `frontend, integration, streaming` | 025, 030, 031, 011 | PH-034 → 071 | 🔲 |
| 035 | Streaming Write Cancellation Handling | `frontend, bff-api, streaming` | 028, 031, 033 | — | 🔲 |
| 036 | Streaming Write Integration Tests | `testing, integration` | 028, 029 | — | 🔲 |
| 037 | StreamingInsertPlugin Unit Tests | `testing, frontend` | 030 | — | 🔲 |

### Track 3: Package D — Action Menu / Command Palette (`sprint1-track3`)

| # | Task | Tags | Dependencies | Placeholders | Status |
|---|------|------|-------------|-------------|--------|
| 045 | Implement GET /api/ai/chat/actions Endpoint | `bff-api, api` | — | PH-045 → 047 | 🔲 |
| 046 | Add Playbook Capability Field (Dataverse) | `dataverse, schema` | — | PH-046 → 047 | 🔲 |
| 047 | Capability Filtering in SprkChatAgentFactory | `bff-api, ai` | 046 | — | 🔲 |
| 048 | Create SprkChatActionMenu Component | `frontend, fluent-ui` | — | — | 🔲 |
| 049 | Wire Action Menu Trigger into SprkChatInput | `frontend` | 048 | — | 🔲 |
| 050 | Fetch Actions from API & Populate Menu | `frontend, api` | 045, 048, 049 | — | 🔲 |
| 051 | Implement Action Handlers | `frontend` | 050 | PH-051 → 078 | 🔲 |
| 052 | Actions Endpoint Unit Tests | `testing, bff-api` | 045 | — | 🔲 |
| 053 | Action Menu Component Tests | `testing, frontend` | 048, 049 | — | 🔲 |
| 054 | Capability Filtering Tests | `testing, bff-api` | 047 | — | 🔲 |

---

## Phase 2: Integration (Sprint 2 — 3 Parallel Tracks)

**Prerequisite**: Phase 1 complete (all tracks)

### Track 1: Package C — AW Code Page Migration (`sprint2-track1`)

| # | Task | Tags | Dependencies | Placeholders | Status |
|---|------|------|-------------|-------------|--------|
| 060 | Scaffold AnalysisWorkspace Code Page | `code-page, frontend, scaffold` | Phase 1 | PH-060 → 061 | 🔲 |
| 061 | Implement 2-Panel Layout (Editor+Viewer) | `code-page, frontend, layout` | 060 | PH-061 → 065 | 🔲 |
| 062 | Port Toolbar (Save, Export, Undo/Redo) | `code-page, frontend` | 061, 033 | — | 🔲 |
| 063 | Wire SprkChatBridge Document Streaming | `code-page, frontend, integration` | 011, 030, 031, 060 | — | 🔲 |
| 064 | Wire SprkChatBridge Selection Events | `code-page, frontend` | 032, 063 | — | 🔲 |
| 065 | Port Analysis Loading & API Integration | `code-page, frontend, api` | 061 | — | 🔲 |
| 066 | Implement Code Page Authentication | `code-page, frontend, auth` | 060 | — | 🔲 |
| 067 | Update Dataverse Form to Open Code Page | `dataverse, frontend` | 060, 065, 066 | — | 🔲 |
| 068 | Deprecate Legacy AnalysisWorkspace PCF | `cleanup, pcf, dataverse` | 067 | — | 🔲 |
| 069 | AW Code Page Dark Mode & Theme Testing | `testing, frontend, accessibility` | 061 | — | 🔲 |
| 070 | AW Code Page Integration Tests | `testing, integration, frontend` | 063, 065 | — | 🔲 |
| 071 | Wire Streaming E2E with Real AW Code Page | `integration, streaming` | 034, 063 | Resolves PH-034 | 🔲 |
| 072 | Build & Deploy AW Code Page | `deploy, code-page` | 067, 070 | — | 🔲 |

### Track 2: Package E — Re-Analysis Pipeline (`sprint2-track2`)

| # | Task | Tags | Dependencies | Placeholders | Status |
|---|------|------|-------------|-------------|--------|
| 078 | Implement AnalysisExecutionTools | `bff-api, ai, tools` | Phase 1 | PH-078 → 080 | 🔲 |
| 079 | Register AnalysisExecutionTools in Factory | `bff-api, ai` | 078 | — | 🔲 |
| 080 | Wire Re-Analysis Streaming Flow | `bff-api, ai, streaming` | 078, 079, 025 | Resolves PH-078 | 🔲 |
| 081 | Re-Analysis Progress Indicator (Frontend) | `frontend, streaming` | 080, 063 | — | 🔲 |
| 082 | AnalysisExecutionTools Unit Tests | `testing, bff-api` | 080 | — | 🔲 |
| 083 | Re-Analysis Integration Tests | `testing, integration` | 080 | — | 🔲 |

### Track 3: Package I — Web Search + Multi-Document (`sprint2-track3`)

| # | Task | Tags | Dependencies | Placeholders | Status |
|---|------|------|-------------|-------------|--------|
| 088 | Implement WebSearchTools | `bff-api, ai, tools` | Phase 1 | PH-088 (Bing API) | 🔲 |
| 089 | Register WebSearchTools in Factory | `bff-api, ai` | 088 | — | 🔲 |
| 090 | Extend ChatKnowledgeScope (AdditionalDocumentIds) | `bff-api, ai` | Phase 1 | — | 🔲 |
| 091 | Multi-Document Support in ContextSelector | `frontend, bff-api` | 090 | — | 🔲 |
| 092 | WebSearchTools Unit Tests | `testing, bff-api` | 088 | — | 🔲 |
| 093 | Multi-Document Context Tests | `testing, bff-api` | 090 | — | 🔲 |

---

## Phase 3: Polish (Sprint 3 — 3 Parallel Tracks)

**Prerequisite**: Phase 2 complete (all tracks)

### Track 1: Package F — Diff Compare View (`sprint3-track1`)

| # | Task | Tags | Dependencies | Status |
|---|------|------|-------------|--------|
| 100 | Implement DiffCompareView Component | `frontend, ui-components, fluent-ui` | Phase 2 | 🔲 |
| 101 | Implement Diff Algorithm for HTML | `frontend, ui-components` | 100 | 🔲 |
| 102 | Automatic Write Mode Selection (Stream/Diff) | `frontend` | 100, 034 | 🔲 |
| 103 | Wire DiffCompareView into AW | `frontend, code-page, integration` | 100, 063 | 🔲 |
| 104 | DiffCompareView Unit Tests | `testing, frontend` | 100, 101 | 🔲 |
| 105 | Write Mode Integration Tests | `testing, frontend` | 102 | 🔲 |

### Track 2: Package G — Selection-Based Revision (`sprint3-track2`)

| # | Task | Tags | Dependencies | Status |
|---|------|------|-------------|--------|
| 110 | Implement Cross-Pane Selection Flow | `frontend, integration` | 032, 064 | 🔲 |
| 111 | Enhance SprkChatHighlightRefine | `frontend` | 110 | 🔲 |
| 112 | Implement Selection-Based Revision Flow | `frontend, bff-api, integration` | 110, 111, 100 | 🔲 |
| 113 | Selection Revision E2E Tests | `testing, integration` | 112 | 🔲 |

### Track 3: Package H — Suggestions + Citations (`sprint3-track3`)

| # | Task | Tags | Dependencies | Status |
|---|------|------|-------------|--------|
| 122 | Implement SprkChatSuggestions Component | `frontend, fluent-ui` | Phase 2 | 🔲 |
| 123 | Implement Suggestions SSE Handling | `bff-api, frontend, sse` | 122 | 🔲 |
| 124 | Implement SprkChatCitationPopover | `frontend, fluent-ui` | Phase 2 | 🔲 |
| 125 | Implement Citations SSE Handling | `bff-api, frontend, sse` | 124 | 🔲 |
| 126 | Modify Search Tools Source Metadata | `bff-api, ai` | Phase 2 | 🔲 |
| 127 | Suggestions & Citations Unit Tests | `testing, frontend` | 122, 124 | 🔲 |
| 128 | Suggestions & Citations Integration Tests | `testing, integration` | 123, 125, 126 | 🔲 |

---

## Phase 4: Deployment & Validation (Sequential)

**Prerequisite**: All Phase 1-3 complete

| # | Task | Tags | Dependencies | Status |
|---|------|------|-------------|--------|
| 140 | Cross-Pane Integration Test Suite | `testing, integration, e2e` | Phase 3 | 🔲 |
| 141 | Dark Mode & Accessibility Validation | `testing, accessibility, fluent-ui` | Phase 3 | 🔲 |
| 142 | Performance Benchmarking | `testing, performance` | Phase 3 | 🔲 |
| 143 | Deploy SprkChatPane Code Page | `deploy, code-page, dataverse` | 140-142 | 🔲 |
| 144 | Deploy AnalysisWorkspace Code Page | `deploy, code-page, dataverse` | 072, 143 | 🔲 |
| 145 | Deploy BFF API (New Endpoints + Tools) | `deploy, bff-api` | 140-142 | 🔲 |
| 146 | Deploy Dataverse Schema Changes | `deploy, dataverse` | 046 | 🔲 |
| 147 | End-to-End Smoke Test | `testing, e2e, smoke` | 143-146 | 🔲 |
| 148 | Placeholder Audit (Zero Unresolved) | `validation, cleanup` | All prev | 🔲 |
| 149 | Remove Legacy AW PCF from Solution | `cleanup, dataverse, pcf` | 144 | 🔲 |
| 150 | Final Code Review & ADR Check | `quality, code-review` | 147, 148 | 🔲 |

---

## Project Wrap-Up

| # | Task | Tags | Dependencies | Status |
|---|------|------|-------------|--------|
| 190 | Project Wrap-Up | `wrap-up, documentation` | All | 🔲 |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|-------------|-------|
| `cleanup` | 001, 002 | — | Frontend + backend cleanup run simultaneously |
| `sprint1-track1` | 010-018 | 003 | Package A: SprkChat Side Pane |
| `sprint1-track2` | 025-037 | 003 | Package B: Streaming Write Engine |
| `sprint1-track3` | 045-054 | 003 | Package D: Action Menu |
| `sprint2-track1` | 060-072 | Phase 1 | Package C: AW Migration |
| `sprint2-track2` | 078-083 | Phase 1 | Package E: Re-Analysis |
| `sprint2-track3` | 088-093 | Phase 1 | Package I: Web Search |
| `sprint3-track1` | 100-105 | Phase 2 | Package F: Diff View |
| `sprint3-track2` | 110-113 | Phase 2 | Package G: Selection Revision |
| `sprint3-track3` | 122-128 | Phase 2 | Package H: Suggestions + Citations |

---

## Placeholder Tracking

| Placeholder | Location | Stub Type | Completed By | Status |
|-------------|----------|-----------|-------------|--------|
| PH-010-A | `SprkChatPane/App.tsx` | hardcoded-return | 012 | 🔲 |
| PH-010-B | `SprkChatPane/App.tsx` | todo-comment | 012 | 🔲 |
| PH-015-A | `openSprkChatPane.ts` | hardcoded-return | Designer | 🔲 |
| PH-026 | `WorkingDocumentTools.cs` | hardcoded-return | 028 | 🔲 |
| PH-034 | Test harness page | mock-data | 071 | 🔲 |
| PH-045 | `ChatEndpoints.cs` | hardcoded-return | 047 | 🔲 |
| PH-046 | `SprkChatAgentFactory.cs` | hardcoded-return | 047 | 🔲 |
| PH-051 | `useActionHandlers.ts` | hardcoded-return | 078 | 🔲 |
| PH-060 | `AW/App.tsx` | hardcoded-return | 061 | 🔲 |
| PH-061 | `AW 2-panel` | todo-comment | 065 | 🔲 |
| PH-078 | `AnalysisExecutionTools.cs` | hardcoded-return | 080 | 🔲 |
| PH-088 | `WebSearchTools.cs` | hardcoded-return | Bing API provision | 🔲 |

---

## Critical Path

```
001+002 → 003 → [010,025,045 parallel] → Phase 1 gate
  → [060,078,088 parallel] → Phase 2 gate
  → [100,110,122 parallel] → Phase 3 gate
  → 140-150 → 190
```

**Longest chain**: 001 → 003 → 010 → 012 → 013 → 014 → 016 → 060 → 061 → 063 → 071 → 100 → 103 → 140 → 143 → 147 → 150 → 190

---

## Summary

| Phase | Package | Tasks | Est. Effort |
|-------|---------|-------|-------------|
| 0 | Legacy Cleanup | 3 | 9h |
| 1 | A: Side Pane | 9 | ~36h |
| 1 | B: Streaming Engine | 13 | ~74h |
| 1 | D: Action Menu | 10 | ~49h |
| 2 | C: AW Migration | 13 | ~45h |
| 2 | E: Re-Analysis | 6 | ~28h |
| 2 | I: Web Search | 6 | ~22h |
| 3 | F: Diff View | 6 | ~28h |
| 3 | G: Selection Revision | 4 | ~22h |
| 3 | H: Suggestions/Citations | 7 | ~28h |
| 4 | Deployment & Validation | 11 | ~54h |
| — | Wrap-Up | 1 | ~4h |
| **Total** | **9 packages** | **89** | **~399h** |

---

*For Claude Code: Use `task-execute` skill for all task execution. Never bypass the skill protocol.*
