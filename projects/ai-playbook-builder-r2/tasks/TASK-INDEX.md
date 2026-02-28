# Task Index — AI Playbook Builder R2

> **Total Tasks**: 17
> **Estimated Hours**: 55-65
> **Parallel Groups**: 2 (A: scope resolution, B: client overlaps server)
> **Critical Path**: 001→002→010→020→021→022→030→031→040→041→050→051→060→070→080→090

## Task Registry

| ID | Title | Phase | Status | Est. | Deps | Parallel | Tags |
|----|-------|-------|--------|------|------|----------|------|
| 001 | Register missing DI services | 0: Job Handler Fix | 🔲 | 1h | none | — | bff-api, di |
| 002 | Verify and complete GetToolAsync | 1: Tool Resolution | 🔲 | 3h | 001 | — | bff-api, ai, dataverse |
| 010 | Implement GetSkillAsync from Dataverse | 2a: Skill Resolution | 🔲 | 3h | 002 | **A** | bff-api, ai, dataverse |
| 011 | Implement GetKnowledgeAsync from Dataverse | 2b: Knowledge Resolution | 🔲 | 3h | 002 | **A** | bff-api, ai, dataverse |
| 012 | Implement GetActionAsync from Dataverse | 2c: Action Resolution | 🔲 | 3h | 002 | **A** | bff-api, ai, dataverse |
| 020 | Remove all stub dictionaries and fake GUIDs | 3: Stub Removal | 🔲 | 4h | 010, 011, 012 | — | bff-api, refactoring |
| 021 | Add ConfigurationSchema to all 9 handlers | 3: Handler Schemas | 🔲 | 4h | 020 | C | bff-api, ai |
| 022 | Implement GET /api/ai/handlers endpoint | 3: Handler Discovery | 🔲 | 3h | 021 | — | bff-api, api |
| 030 | Implement SyncCanvasToNodesAsync | 4: Canvas Sync | 🔲 | 8h | 022 | — | bff-api, ai, dataverse |
| 031 | Implement ResolveNodeScopesAsync | 4: Node Scopes | 🔲 | 4h | 030 | — | bff-api, ai, dataverse |
| 040 | Wire ExecutePlaybookAsync delegation | 5: Execution Wiring | 🔲 | 6h | 031 | — | bff-api, ai |
| 041 | Load document into PlaybookRunContext | 5: Document Loading | 🔲 | 2h | 040 | — | bff-api, ai |
| 050 | Enable per-token streaming in node executor | 6: Streaming | 🔲 | 4h | 041 | — | bff-api, ai |
| 051 | Persist Deliver Output to working document | 6: Output Persistence | 🔲 | 3h | 050 | — | bff-api, ai |
| 060 | Statuscode-based auto-execute + triggerExecute | 7: Workspace UX | 🔲 | 3h | 051 | **B** | frontend, fluent-ui |
| 061 | Add completion toast notification | 7: Workspace UX | 🔲 | 1h | 060 | — | frontend, fluent-ui |
| 062 | Add Run Analysis button + source toggle | 7: Workspace UX | 🔲 | 2h | 060 | — | frontend, fluent-ui |
| 063 | Auto-load SprkChat side pane | 7: SprkChat | 🔲 | 2h | 060 | — | frontend, code-page |
| 070 | End-to-end verification | 8: Testing | 🔲 | 4h | 060, 061, 062, 063 | — | testing, verification |
| 080 | Deploy to dev environment | 9: Deployment | 🔲 | 2h | 070 | — | deploy, azure |
| 090 | Project wrap-up | 9: Wrap-up | 🔲 | 1h | 080 | — | wrap-up |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| **A** | 010, 011, 012 | 002 complete | Independent scope types — Skill, Knowledge, Action resolution. Each modifies different sections of ScopeResolverService.cs (different methods + different DTO classes). Can run as parallel Task agents. |
| **B** | 060 (client) overlaps 051 (server) | 050 complete | Client-side TypeScript work can start while server-side output persistence is being finalized. Different codebases. |
| **C** | 021 sub-tasks (9 handlers) | 020 complete | Each handler file is independent — can split into parallel sub-agents if needed. |

## Critical Path

```
001 → 002 → [010 + 011 + 012] → 020 → 021 → 022 → 030 → 031 → 040 → 041 → 050 → 051 → 060 → 070 → 080 → 090
                (parallel A)
```

**Bottleneck tasks** (block the most downstream work):
- **001** (blocks everything)
- **002** (gates all scope resolution)
- **020** (gates handler discovery + canvas sync)
- **030** (gates execution wiring)
- **040** (gates streaming + persistence)

## Phase Summary

| Phase | Tasks | Parallel? | Description |
|-------|-------|-----------|-------------|
| 0 | 001 | No | Job handler + node executor DI registration |
| 1 | 002 | No | Verify tool resolution against Dataverse |
| 2 | 010, 011, 012 | **Yes (Group A)** | Skill, Knowledge, Action resolution |
| 3 | 020, 021, 022 | Sequential | Stub removal → handler schemas → discovery API |
| 4 | 030, 031 | Sequential | Canvas sync → node scope resolution |
| 5 | 040, 041 | Sequential | Execution wiring → document loading |
| 6 | 050, 051 | Sequential | Streaming → output persistence |
| 7 | 060, 061, 062, 063 | Partial | Auto-execute first, then toast/button/chat |
| 8 | 070 | No | End-to-end verification |
| 9 | 080, 090 | Sequential | Deploy → wrap-up |

## High-Risk Tasks

| Task | Risk | Mitigation |
|------|------|------------|
| 030 (Canvas Sync) | Complex diffing, N:N relationship mapping | Design.md section 6.9 has detailed spec; verify Dataverse schema first |
| 040 (Execution Wiring) | Architecture-critical delegation | Follow existing PlaybookOrchestrationService patterns |
| 020 (Stub Removal) | Must be certain all queries work before deleting | Run after 010+011+012 all verified |

---

*Generated by project-pipeline. 17 tasks across 10 phases.*
