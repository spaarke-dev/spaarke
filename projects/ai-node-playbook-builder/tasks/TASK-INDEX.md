# Task Index - AI Node-Based Playbook Builder

> **Project**: ai-node-playbook-builder
> **Created**: 2026-01-08
> **Total Tasks**: 47

---

## Status Legend

| Symbol | Status |
|--------|--------|
| :white_large_square: | Not Started |
| :hourglass_flowing_sand: | In Progress |
| :white_check_mark: | Completed |
| :no_entry: | Blocked |

---

## Phase 1: Foundation (001-009)

| Task | Title | Status | Depends | Tags |
|------|-------|--------|---------|------|
| 001 | Design Dataverse Schema | :white_check_mark: | none | dataverse, schema |
| 002 | Implement Dataverse Schema | :white_check_mark: | 001 | dataverse, schema |
| 003 | Create NodeService | :white_check_mark: | 002 | bff-api, service |
| 004 | Extend ScopeResolverService | :white_check_mark: | 003 | bff-api, service |
| 005 | Create ExecutionGraph | :white_check_mark: | 003 | bff-api, orchestration |
| 006 | Create AiAnalysisNodeExecutor | :white_check_mark: | 004, 005 | bff-api, executor |
| 007 | Create PlaybookOrchestrationService | :white_check_mark: | 006 | bff-api, orchestration |
| 008 | Create Node API Endpoints | :white_check_mark: | 007 | bff-api, api |
| 009 | Phase 1 Tests and Deployment | :white_check_mark: | 008 | testing, deploy |

---

## Phase 2: Visual Builder (010-019)

| Task | Title | Status | Depends | Tags |
|------|-------|--------|---------|------|
| 010 | Setup Builder React App | :white_check_mark: | 009 | frontend, react |
| 011 | Implement React Flow Canvas | :white_check_mark: | 010 | frontend, react-flow |
| 012 | Create Custom Node Components | :white_check_mark: | 011 | frontend, react |
| 013 | Create Properties Panel | :white_check_mark: | 012 | frontend, react |
| 014 | Implement Scope Selector | :white_check_mark: | 013 | frontend, react |
| 015 | Create PCF Host Control | :white_check_mark: | 010 | pcf, fluent-ui |
| 016 | Implement Host-Builder Communication | :white_check_mark: | 015 | pcf, frontend |
| 017 | Add Canvas Persistence API | :white_check_mark: | 008 | bff-api, api |
| 018 | Deploy Builder to App Service | :white_check_mark: | 014, 016, 017 | deploy, azure |
| 019 | Phase 2 Tests and PCF Deployment | :white_check_mark: | 018 | testing, deploy, pcf |

---

## Phase 2.5: Architecture Refactor (019a)

| Task | Title | Status | Depends | Tags |
|------|-------|--------|---------|------|
| 019a | Refactor to React Flow v10 Direct PCF Integration | :white_check_mark: | 019 | pcf, react-flow, refactor, architecture |

**Rationale**: Migrate from iframe + React 18 to direct PCF + React 16 before Phase 3.
This eliminates postMessage complexity, dual deployment, and simplifies all future work.
See: [Architecture Refactor Documentation](../notes/architecture/ARCHITECTURE-REFACTOR-REACT-FLOW-V10.md)

---

## Phase 3: Parallel Execution + Delivery (020-029)

| Task | Title | Status | Depends | Tags |
|------|-------|--------|---------|------|
| 020 | Implement Parallel Execution | :white_check_mark: | 019a | bff-api, orchestration |
| 021 | Create TemplateEngine | :white_check_mark: | 020 | bff-api, service |
| 022 | Create CreateTaskNodeExecutor | :white_check_mark: | 021 | bff-api, executor |
| 023 | Create SendEmailNodeExecutor | :white_check_mark: | 021 | bff-api, executor |
| 024 | Create UpdateRecordNodeExecutor | :white_check_mark: | 021 | bff-api, executor |
| 025 | Create DeliverOutputNodeExecutor | :white_check_mark: | 021 | bff-api, executor |
| 026 | Implement Power Apps Integration | :white_check_mark: | 025 | bff-api, integration |
| 027 | Add Execution Visualization | :white_check_mark: | 020 | frontend, react |
| 028 | Phase 3 Integration Tests | :white_check_mark: | 027 | testing |
| 029 | Phase 3 Deployment | :white_check_mark: | 028 | deploy |

---

## Phase 4: Advanced Features (030-039)

| Task | Title | Status | Depends | Tags |
|------|-------|--------|---------|------|
| 030 | Create ConditionNodeExecutor | :white_check_mark: | 029 | bff-api, executor |
| 031 | Add Condition UI in Builder | :white_check_mark: | 030 | frontend, react |
| 032 | Implement Model Selection API | :white_check_mark: | 029 | bff-api, api |
| 033 | Add Model Selection UI | :white_check_mark: | 032 | frontend, react |
| 034 | Implement Confidence Scores | :white_check_mark: | 029 | bff-api, ai |
| 035 | Add Confidence UI Display | :white_check_mark: | 034 | frontend, react |
| 036 | Create Playbook Templates Feature | :white_check_mark: | 029 | bff-api, feature |
| 037 | Add Template Library UI | :white_check_mark: | 036 | frontend, react |
| 038 | Implement Execution History | :white_check_mark: | 029 | bff-api, feature |
| 039 | Phase 4 Tests and Deployment | :white_check_mark: | 038 | testing, deploy |

---

## Phase 5: Production Hardening (040-049)

| Task | Title | Status | Depends | Tags |
|------|-------|--------|---------|------|
| 040 | Comprehensive Error Handling | :white_large_square: | 039 | bff-api, quality |
| 041 | Implement Retry Logic | :white_large_square: | 040 | bff-api, resilience |
| 042 | Add Timeout Management | :white_large_square: | 041 | bff-api, resilience |
| 043 | Implement Cancellation Support | :white_large_square: | 042 | bff-api, feature |
| 044 | Add Cancel UI | :white_large_square: | 043 | frontend, react |
| 045 | Implement Audit Logging | :white_large_square: | 040 | bff-api, logging |
| 046 | Performance Optimization | :white_large_square: | 045 | bff-api, performance |
| 047 | Load Testing | :white_large_square: | 046 | testing, performance |
| 048 | Security Review | :white_large_square: | 047 | security, review |
| 049 | Phase 5 Final Deployment | :white_large_square: | 048 | deploy |

---

## Phase 6: Wrap-up (090)

| Task | Title | Status | Depends | Tags |
|------|-------|--------|---------|------|
| 090 | Project Wrap-up | :white_large_square: | 049 | documentation, cleanup |

---

## Critical Path

```
001 → 002 → 003 → 004 → 005 → 006 → 007 → 008 → 009
                                              ↓
010 → 011 → 012 → 013 → 014 ─────────────────→ 018 → 019
  └── 015 → 016 ──────────────────────────────↗
                008 → 017 ────────────────────↗
                                              ↓
020 → 021 → 022, 023, 024, 025 → 026 → 027 → 028 → 029
                                              ↓
030, 032, 034, 036, 038 (parallel) → 039
                                              ↓
040 → 041 → 042 → 043 → 044, 045 → 046 → 047 → 048 → 049
                                              ↓
                                             090
```

---

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 011 | React Flow + iframe complexity | Follow validated pattern from design |
| 015 | PCF React 16 constraint | Use ReactDOM.render, not createRoot |
| 020 | Parallel execution complexity | Start with throttled approach |
| 030 | Condition evaluation | Use simple JSON expression syntax |

---

*Updated by task-execute skill*
