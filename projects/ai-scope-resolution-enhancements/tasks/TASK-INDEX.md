# Task Index - AI Scope Resolution Enhancements

> **Project**: ai-scope-resolution-enhancements
> **Created**: 2026-01-29
> **Total Tasks**: 29

---

## Status Legend

| Symbol | Status |
|--------|--------|
| 🔲 | Pending |
| 🔄 | In Progress |
| ✅ | Completed |
| ⏸️ | Blocked |
| ❌ | Cancelled |

---

## Phase Overview

| Phase | Description | Tasks | Status |
|-------|-------------|-------|--------|
| **0** | Fix Job Handler Registration (CRITICAL) | 001-003 | 🔄 In Progress |
| **1** | Complete Tool Resolution | 010-011 | 🔲 Pending |
| **2** | Implement Skill Resolution | 020-022 | 🔲 Pending |
| **3** | Implement Knowledge Resolution | 030-032 | 🔲 Pending |
| **4** | Implement Action Resolution | 040-042 | 🔲 Pending |
| **5a** | CRUD Migration to Dataverse | 051-055 | 🔲 Pending |
| **5b** | Remove Stub Dictionaries | 050 | ⏸️ Blocked |
| **6** | Handler Discovery API | 060-064 | ✅ Completed |
| **7** | Testing & Validation | 070-074 | 🔲 Pending |
| **8** | Deployment & Monitoring | 080-081 | 🔲 Pending |
| **Wrap-up** | Project Completion | 090 | 🔲 Pending |

---

## Task List

### Phase 0: Fix Job Handler Registration (CRITICAL)

| Task | Title | Status | Dependencies | Parallel |
|------|-------|--------|--------------|----------|
| ✅ 001 | Investigate Job Handler Registration Issue | Completed | - | - |
| ✅ 002 | Fix Job Handler Registration | Completed | 001 | - |
| 🔲 003 | Deploy and Test Job Handler Fix | Pending | 002 | - |

### Phase 1: Complete Tool Resolution

| Task | Title | Status | Dependencies | Parallel |
|------|-------|--------|--------------|----------|
| ✅ 010 | Verify Tool Resolution from Dataverse | Completed | 003 | - |
| ✅ 011 | Test GenericAnalysisHandler Fallback | Completed | 010 | - |

### Phase 2: Implement Skill Resolution

| Task | Title | Status | Dependencies | Parallel |
|------|-------|--------|--------------|----------|
| ✅ 020 | Implement Skill Resolution DTOs | Completed | 011 | **Group A** |
| ✅ 021 | Implement GetSkillAsync Dataverse Query | Completed | 020 | Group A |
| ✅ 022 | Create Unit Tests for Skill Resolution | Completed | 021 | Group A |

### Phase 3: Implement Knowledge Resolution

| Task | Title | Status | Dependencies | Parallel |
|------|-------|--------|--------------|----------|
| ✅ 030 | Implement Knowledge Resolution DTOs | Completed | 011 | **Group A** |
| ✅ 031 | Implement GetKnowledgeAsync Dataverse Query | Completed | 030 | Group A |
| ✅ 032 | Create Unit Tests for Knowledge Resolution | Completed | 031 | Group A |

### Phase 4: Implement Action Resolution

| Task | Title | Status | Dependencies | Parallel |
|------|-------|--------|--------------|----------|
| ✅ 040 | Implement Action Resolution DTOs | Completed | 011 | **Group A** |
| ✅ 041 | Implement GetActionAsync Dataverse Query | Completed | 040 | Group A |
| ✅ 042 | Create Unit Tests for Action Resolution | Completed | 041 | Group A |

### Phase 5a: CRUD Migration to Dataverse

| Task | Title | Status | Dependencies | Parallel |
|------|-------|--------|--------------|----------|
| ✅ 051 | Implement List*Async Dataverse Queries | Completed | 042 | - |
| ✅ 052 | Implement Create*Async Dataverse Operations | Completed | 051 | - |
| ✅ 053 | Implement Update*Async Dataverse Operations | Completed | 052 | - |
| ✅ 054 | Implement Delete*Async Dataverse Operations | Completed | 053 | - |
| ✅ 055 | Update SearchScopesAsync to Use Dataverse | Completed | 051 | - |

> **Note**: These tasks migrate all CRUD operations from stub dictionaries to Dataverse Web API, which is required before Task 050 can be executed.

### Phase 5b: Remove Stub Dictionaries

| Task | Title | Status | Dependencies | Parallel |
|------|-------|--------|--------------|----------|
| ✅ 050 | Remove All Stub Dictionaries | Completed | 051, 052, 053, 054, 055 | - |

> **Note**: Task 050 blocked until all CRUD operations (051-055) are migrated to Dataverse.

### Phase 6: Handler Discovery API

| Task | Title | Status | Dependencies | Parallel |
|------|-------|--------|--------------|----------|
| ✅ 060 | Create Handler Discovery API Endpoint | Completed | 050 | - |
| ✅ 061 | Add ConfigurationSchema to ToolHandlerMetadata | Completed | 060 | - |
| ✅ 062 | Add ConfigurationSchema to GenericAnalysisHandler | Completed | 061 | **Group B** |
| ✅ 063 | Add ConfigurationSchema to All Remaining Handlers | Completed | 061 | **Group B** |
| ✅ 064 | Create Unit Tests for Handler Discovery API | Completed | 063 | - |

### Phase 7: Testing & Validation

| Task | Title | Status | Dependencies | Parallel |
|------|-------|--------|--------------|----------|
| ✅ 070 | Integration Test: End-to-End Playbook Execution | Completed | 064 | - |
| 🔲 071 | User Testing: File Upload via UniversalDocumentUpload | Pending | 070 | **Group C** |
| 🔲 072 | User Testing: Email-to-Document Automation | Pending | 070 | **Group C** |
| 🔲 073 | User Testing: Outlook Add-in Document Save | Pending | 070 | **Group C** |
| 🔲 074 | User Testing: Word Add-in Document Save | Pending | 070 | **Group C** |

### Phase 8: Deployment & Monitoring

| Task | Title | Status | Dependencies | Parallel |
|------|-------|--------|--------------|----------|
| 🔲 080 | Final Deployment to Dev Environment | Pending | 074 | - |
| 🔲 081 | Monitoring and Success Metrics Verification | Pending | 080 | - |

### Wrap-up

| Task | Title | Status | Dependencies | Parallel |
|------|-------|--------|--------------|----------|
| 🔲 090 | Project Wrap-Up and Documentation | Pending | 081 | - |

---

## Parallel Execution Groups

Tasks in the same group can be executed simultaneously after their dependencies are satisfied.

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| **A** | 020-022, 030-032, 040-042 | Task 011 complete | Skill, Knowledge, Action resolution - all independent |
| **B** | 062, 063 | Task 061 complete | Handler schema updates - independent handlers |
| **C** | 071, 072, 073, 074 | Task 070 complete | User testing - all document creation flows |

### How to Execute Parallel Groups

When Group A prerequisite (task 011) is satisfied, you can execute tasks in parallel:

```
"Execute tasks 020, 030, and 040 in parallel"
```

This will send ONE message with THREE Task tool calls, each running task-execute.

---

## Critical Path

The longest dependency chain determines minimum project duration:

```
001 → 002 → 003 → 010 → 011 → [020-022 OR 030-032 OR 040-042] → 051 → 052 → 053 → 054 → 050 → 060 → 061 → [062 OR 063] → 064 → 070 → [071-074] → 080 → 081 → 090
                                                                ↘ 055 ↗
```

**Critical Path Length**: ~29 tasks (Phase 5a adds 5 sequential CRUD migration tasks)

---

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 002 | Fix may require new handler implementation | Have GenericAnalysisHandler as fallback |
| 051-054 | CRUD migration to Dataverse may have query/payload differences | Follow existing Get*Async pattern; test with real Dataverse data |
| 050 | Removing stubs may break tests | Run full test suite before removal; ensure all CRUD operations migrated first |
| 073 | Outlook add-in testing requires user environment | Document setup prerequisites |

---

## Progress Summary

| Metric | Value |
|--------|-------|
| Total Tasks | 29 |
| Completed | 26 |
| In Progress | 0 |
| Blocked | 0 |
| Pending | 3 |
| Completion % | 90% |

---

*Updated: 2026-01-29 by task-execute (completed Task 070 Playbook Integration Tests)*
