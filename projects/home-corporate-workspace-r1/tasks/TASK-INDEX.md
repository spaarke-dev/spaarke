# Task Index — Legal Operations Workspace (Home Corporate) R1

> **Last Updated**: 2026-02-18
> **Total Tasks**: 42
> **Status**: In Progress (Deployment Phase)

---

## Task Registry

| ID | Title | Phase | Status | Dependencies | Rigor | Parallel Group |
|----|-------|-------|--------|--------------|-------|----------------|
| 001 | Custom Page Shell and Theme System | 1 | ✅ | none | FULL | pg-foundation |
| 002 | Shared TypeScript Interfaces and Types | 1 | ✅ | 001 | FULL | pg-foundation |
| 003 | Xrm.WebApi Data Service Layer | 1 | ✅ | 002 | FULL | pg-foundation |
| 004 | Portfolio Health Summary (Block 2) | 1 | ✅ | 002 | FULL | pg-phase1-ui |
| 005 | My Portfolio Widget - Matters Tab (Block 5) | 1 | ✅ | 002, 003 | FULL | pg-phase1-ui |
| 006 | My Portfolio Widget - Projects + Documents Tabs | 1 | ✅ | 005 | STANDARD | pg-phase1-ui |
| 007 | Notification Panel (Block 7) | 1 | ✅ | 002 | FULL | pg-phase1-ui |
| 008 | BFF - Portfolio Aggregation Endpoint | 1 | ✅ | none | FULL | pg-phase1-bff |
| 009 | BFF - Health Metrics Endpoint | 1 | ✅ | 008 | FULL | pg-phase1-bff |
| 010 | Updates Feed Base (Block 3) | 2 | ✅ | 001, 002, 003 | FULL | pg-phase2-feed |
| 011 | Feed Item Cards | 2 | ✅ | 010 | FULL | pg-phase2-feed |
| 012 | Flag-as-ToDo Toggle (Block 3D) | 2 | ✅ | 011, 003 | FULL | pg-phase2-feed |
| 013 | AI Summary Dialog (Block 3E) | 2 | ✅ | 011 | FULL | pg-phase2-feed |
| 014 | Smart To Do List Base (Block 4) | 2 | ✅ | 002, 003, 012 | FULL | pg-phase2-todo |
| 015 | Manual Add, Checkbox, Dismiss | 2 | ✅ | 014 | FULL | pg-phase2-todo |
| 016 | To Do AI Summary with Scoring Grid (Block 4D) | 2 | ✅ | 014 | FULL | pg-phase2-todo |
| 017 | BFF - Priority Scoring Engine | 2 | ✅ | none | FULL | pg-scoring |
| 018 | BFF - Effort Scoring Engine | 2 | ✅ | none | FULL | pg-scoring |
| 019 | BFF - Scoring Calculation Endpoint | 2 | ✅ | 017, 018, 008 | FULL | pg-scoring |
| 020 | Get Started Card Row + Quick Summary (Block 1) | 3 | ✅ | 001, 002 | FULL | pg-phase3-actions |
| 021 | Quick Summary Briefing Dialog (Block 1B) | 3 | ✅ | 020 | FULL | pg-phase3-actions |
| 022 | Create Matter Step 1 - File Upload (Block 6) | 3 | ✅ | 001, 002 | FULL | pg-phase3-dialog |
| 023 | Create Matter Step 2 - Form + AI Pre-fill | 3 | ✅ | 022 | FULL | pg-phase3-dialog |
| 024 | Create Matter Step 3 - Next Steps + Follow-ons | 3 | ✅ | 023 | FULL | pg-phase3-dialog |
| 025 | Action Card Integration (6 cards → Analysis Builder) | 3 | ✅ | 020 | FULL | pg-phase3-actions |
| 026 | BFF - AI Summary Endpoint | 3 | ✅ | 008 | FULL | pg-ai-integration |
| 027 | BFF - Quick Summary Briefing Endpoint | 3 | ✅ | 008, 009 | FULL | pg-ai-integration |
| 028 | BFF - Create Matter AI Pre-fill Endpoint | 3 | ✅ | 008 | FULL | pg-ai-integration |
| 029 | System-Generated To-Do Items (BFF Job) | 3 | ✅ | 017, 018 | FULL | pg-scoring |
| 030 | Cross-Block State Synchronization | 4 | ✅ | 012, 014, 020 | FULL | none |
| 031 | Dark Mode Audit | 4 | ✅ | 030 | STANDARD | pg-polish |
| 032 | Accessibility Audit (WCAG 2.1 AA) | 4 | ✅ | 030 | STANDARD | pg-polish |
| 033 | Bundle Size Optimization | 4 | ✅ | 030 | STANDARD | pg-polish |
| 034 | Performance Optimization | 4 | ✅ | 030 | STANDARD | pg-polish |
| 035 | Unit Tests - Scoring Engine | 4 | ✅ | 017, 018, 019 | STANDARD | pg-testing |
| 036 | Integration Tests - BFF Endpoints | 4 | ✅ | 008, 009, 019, 026, 027, 028 | STANDARD | pg-testing |
| 037 | E2E Test Scenarios | 4 | ✅ | 030, 031, 032 | STANDARD | none |
| 040 | Solution Packaging for Dataverse | 5 | ✅ | 033, 034, 037 | FULL | none |
| 041 | Custom Page Deployment to MDA | 5 | ✅ | 040 | FULL | none |
| 042 | BFF Endpoint Deployment | 5 | ✅ | 036 | FULL | none |
| 043 | Post-Deployment Verification | 5 | ✅ | 041, 042 | STANDARD | none |
| 090 | Project Wrap-up | 5 | ✅ | 043 | FULL | none |

---

## Phase Summary

| Phase | Name | Tasks | Status |
|-------|------|-------|--------|
| 1 | Foundation & Independent Blocks | 001-009 (9 tasks) | ✅ Complete |
| 2 | Core Feature Blocks | 010-019 (10 tasks) | ✅ Complete |
| 3 | Action Cards & Dialogs | 020-029 (10 tasks) | ✅ Complete |
| 4 | Integration & Polish | 030-037 (8 tasks) | ✅ Complete |
| 5 | Deployment & Wrap-up | 040-043, 090 (5 tasks) | ✅ Complete |

---

## Rigor Level Distribution

| Level | Count | Tasks |
|-------|-------|-------|
| **FULL** | 32 | 001-005, 007-019, 020-030, 040-042, 090 |
| **STANDARD** | 10 | 006, 031-037, 043 |
| **MINIMAL** | 0 | — |

---

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| **pg-foundation** | 001 → 002 → 003 | none | `LegalWorkspace/` shell, types, services | Sequential (dependent chain) |
| **pg-phase1-ui** | 004, 005, 007 | 002 (types) | Separate component directories | ✅ Yes |
| **pg-phase1-bff** | 008, 009 | none | `Api/Workspace/` separate endpoints | ✅ Yes (with pg-phase1-ui) |
| **pg-phase2-feed** | 010 → 011 → 012, 013 | 003 (data service) | `components/ActivityFeed/` | Sequential internally, 013 parallels 012 |
| **pg-phase2-todo** | 014 → 015, 016 | 012 (flag interface) | `components/SmartToDo/` | Sequential internally, 016 parallels 015 |
| **pg-scoring** | 017, 018 → 019, 029 | none (017/018 parallel) | `Services/Workspace/` separate files | ✅ 017+018 parallel, then 019, then 029 |
| **pg-phase3-actions** | 020 → 021, 025 | 002 (types) | `components/GetStarted/` | 021 + 025 parallel after 020 |
| **pg-phase3-dialog** | 022 → 023 → 024 | 002 (types) | `components/CreateMatter/` | Sequential internally |
| **pg-ai-integration** | 026, 027, 028 | 008 (BFF base) | `Services/Workspace/` separate files | ✅ Yes |
| **pg-polish** | 031, 032, 033, 034 | 030 | All component files (read-heavy) | ✅ Yes (audits, not heavy edits) |
| **pg-testing** | 035, 036 | Respective endpoints | `tests/` separate directories | ✅ Yes |

### Cross-Phase Parallelism

These groups from different phases can run simultaneously:

| Concurrent Groups | Why It Works |
|------------------|--------------|
| pg-phase1-ui + pg-phase1-bff | UI components and BFF endpoints touch different directories |
| pg-phase2-feed + pg-scoring | Feed UI and scoring engine are independent |
| pg-phase3-actions + pg-phase3-dialog + pg-ai-integration | Action cards, wizard, and AI endpoints are independent |
| pg-polish + pg-testing | Audits (read-heavy) and tests (separate directory) don't conflict |

### How to Execute Parallel Groups

1. Check all prerequisites are complete (✅ in Status column)
2. Invoke task-execute with multiple Skill tool invocations in ONE message
3. Each invocation calls task-execute for one task
4. Wait for all to complete before starting next dependent group

**Recommended Execution Order:**

```
Step 1: 001 → 002 → 003 (pg-foundation, sequential)
Step 2: [004 + 005 + 007] + [008] (pg-phase1-ui + pg-phase1-bff, parallel)
Step 3: 006, 009 (complete Phase 1 remainders)
Step 4: [010→011→012→013] + [017 + 018] (pg-phase2-feed + pg-scoring, parallel)
Step 5: [014→015→016] + [019, 029] (pg-phase2-todo + scoring endpoint, parallel)
Step 6: [020→021+025] + [022→023→024] + [026+027+028] (3-way Phase 3 parallel)
Step 7: 030 (cross-block sync, sequential)
Step 8: [031+032+033+034] + [035+036] (pg-polish + pg-testing, parallel)
Step 9: 037 (E2E tests after polish)
Step 10: [040→041] + [042] (deployment, partial parallel)
Step 11: 043 (post-deployment verification)
Step 12: 090 (project wrap-up)
```

---

## Critical Path

The longest dependency chain (critical path):

```
001 → 002 → 003 → 010 → 011 → 012 → 014 → 030 → 031/032/037 → 040 → 041 → 043 → 090
```

**Critical path length**: 13 tasks

**Bottlenecks**:
- Task 012 (Flag-as-ToDo) blocks both Block 4 and cross-block sync
- Task 030 (State Sync) blocks all Phase 4 polish and testing
- Task 040 (Solution Packaging) blocks all deployment

---

## Dependencies Graph (Blocking Tasks)

Tasks that block multiple downstream tasks (high-impact):

| Task | Blocks | Impact |
|------|--------|--------|
| 001 (Shell) | 002, 003, 010, 020, 022 | Foundation for all UI |
| 002 (Types) | 003, 004, 005, 007, 010, 014, 020, 022 | Interfaces for all blocks |
| 003 (Data Service) | 005, 010, 012 | Xrm.WebApi layer |
| 008 (BFF Portfolio) | 009, 019, 026, 027, 028 | BFF foundation |
| 012 (Flag-as-ToDo) | 014, 030 | Cross-block state |
| 030 (State Sync) | 031, 032, 033, 034, 037 | Phase 4 gate |

---

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 001 | Custom Page auth to BFF unclear | Investigate MSAL token flow early |
| 033 | Bundle may exceed 5MB | Code splitting, lazy loading |
| 017/018 | Scoring edge cases | Comprehensive unit tests (Task 035) |
| 025 | Analysis Builder integration pattern unknown | Study AiToolAgent PCF first |

---

*This file is the source of truth for task status. Updated by task-execute skill after each task completion.*
