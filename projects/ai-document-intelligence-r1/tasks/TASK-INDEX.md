# Task Index - AI Document Intelligence R1

> **Project**: AI Document Intelligence R1 - Core Infrastructure
> **Created**: 2025-12-25
> **Total Tasks**: 22 tasks (5 verification + 12 conditional + 5 deployment)

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Phase 1A: Verification | 001-005 | 🔲 Not Started |
| Phase 1B: Entity Creation (Conditional) | 010-021 | ⏸️ Pending Verification |
| Phase 1C: Deployment Testing | 030-034 | 🔲 Not Started |
| Project Completion | 090 | 🔲 Not Started |

---

## Phase 1A: Verification

These tasks verify existing infrastructure. All can run in parallel.

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 001 | Verify Dataverse Entities Exist | 🔲 Not Started | none | Determines Phase 1B scope |
| 002 | Verify Environment Variables in Solution | 🔲 Not Started | none | |
| 003 | Verify AI Foundry Hub Connections | 🔲 Not Started | none | |
| 004 | Run API Health Check and SSE Test | 🔲 Not Started | none | |
| 005 | Document Verification Results | 🔲 Not Started | 001, 002, 003, 004 | Determines Phase 1B tasks |

---

## Phase 1B: Entity Creation (Conditional)

**IMPORTANT**: These tasks only execute if Phase 1A verification finds missing entities.

| ID | Title | Status | Dependencies | Condition |
|----|-------|--------|--------------|-----------|
| 010 | Create sprk_analysis Entity | ⏸️ Conditional | 005 | If missing |
| 011 | Create sprk_analysisaction Entity | ⏸️ Conditional | 005 | If missing |
| 012 | Create sprk_analysisskill Entity | ⏸️ Conditional | 005 | If missing |
| 013 | Create sprk_analysisknowledge Entity | ⏸️ Conditional | 005 | If missing |
| 014 | Create sprk_knowledgedeployment Entity | ⏸️ Conditional | 005 | If missing |
| 015 | Create sprk_analysistool Entity | ⏸️ Conditional | 005 | If missing |
| 016 | Create sprk_analysisplaybook Entity | ⏸️ Conditional | 005 | If missing |
| 017 | Create sprk_analysisworkingversion Entity | ⏸️ Conditional | 005 | If missing |
| 018 | Create sprk_analysisemailmetadata Entity | ⏸️ Conditional | 005 | If missing |
| 019 | Create sprk_analysischatmessage Entity | ⏸️ Conditional | 005 | If missing |
| 020 | Create Security Roles | ⏸️ Conditional | 010-019 | If any entities created |
| 021 | Export Solution Package | ⏸️ Conditional | 020 | If any entities/roles created |

---

## Phase 1C: Deployment Testing

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 030 | Test Bicep Deployment to External Subscription | 🔲 Not Started | 005 | |
| 031 | Test Dataverse Solution Import to Clean Environment | 🔲 Not Started | 021 | Only if solution exported |
| 032 | Verify Environment Variables Resolve in Deployed API | 🔲 Not Started | 030 | |
| 033 | Run Integration Tests Against Dev Environment | 🔲 Not Started | 004, 030, 031, 032 | |
| 034 | Create Phase 1 Deployment Guide | 🔲 Not Started | 030, 031, 032, 033 | |

---

## Project Completion

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 090 | Project Wrap-up | 🔲 Not Started | 034 | MANDATORY final task |

---

## Critical Path

```
001, 002, 003, 004 (parallel)
        ↓
       005 (consolidate verification)
        ↓
    ┌───┴───┐
    │       │
 Phase 1B  030 (Bicep)
(if needed) ↓
    │      032 (Env Vars)
    ↓       ↓
   021 ──→ 031 (Solution)
            ↓
           033 (Integration Tests)
            ↓
           034 (Deployment Guide)
            ↓
           090 (Wrap-up)
```

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not Started |
| 🔄 | In Progress |
| ✅ | Completed |
| ⏸️ | Conditional/Pending |
| ❌ | Blocked |
| ⏭️ | Skipped |

---

## Execution Notes

1. **Start with Phase 1A** (Tasks 001-004 can run in parallel)
2. **Task 005** synthesizes verification results and determines Phase 1B scope
3. **Phase 1B tasks** are CONDITIONAL - only execute those marked as needed
4. **Phase 1C** validates deployment regardless of Phase 1B outcome
5. **Task 090** is MANDATORY - must complete to close project

---

*Last Updated: 2025-12-25*
