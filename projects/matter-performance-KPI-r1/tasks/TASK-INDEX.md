# Task Index: Matter Performance Assessment - R1 MVP

> **Project**: matter-performance-KPI-r1
> **Total Tasks**: 27
> **Status**: Complete (27/27)
> **Last Updated**: 2026-02-12

---

## Task List

| ID | Title | Phase | Status | Dependencies | Parallel | Rigor |
|----|-------|-------|--------|--------------|----------|-------|
| 001 | Create KPI Assessment Entity | 1 | ✅ | none | — | STANDARD |
| 002 | Extend Matter Entity with Grade Fields | 1 | ✅ | none | — | STANDARD |
| 003 | Create Performance Area Choice Field | 1 | ✅ | 001 | — | MINIMAL |
| 004 | Create Grade Choice Field | 1 | ✅ | 001 | — | MINIMAL |
| 005 | Configure Quick Create Form | 1 | ✅ | 001,002,003,004 | — | STANDARD |
| 006 | Deploy Phase 1 to Dataverse | 1 | ✅ | 005 | — | STANDARD |
| 010 | Create Calculator Endpoint Structure | 2 | ✅ | 006 | — | FULL |
| 011 | Implement Current Grade Calculation | 2 | ✅ | 010 | — | FULL |
| 012 | Implement Historical Average Calculation | 2 | ✅ | 010 | — | FULL |
| 013 | Implement Trend Data Query | 2 | ✅ | 010 | — | FULL |
| 014 | Create Web Resource Trigger | 2 | ✅ | 010 | — | FULL |
| 015 | Add Error Handling to Web Resource | 2 | ✅ | 014 | — | FULL |
| 016 | Unit Tests for Calculator Logic | 2 | ✅ | 011,012,013 | — | STANDARD |
| 020 | Research VisualHost Card Types | 5 | ✅ | 006 | Group A | MINIMAL |
| 021 | Design Report Card Metric Card | 5 | ✅ | 020 | — | FULL |
| 022 | Implement Report Card Metric Card | 5 | ✅ | 021 | — | FULL |
| 023 | Document Card Type Configuration | 5 | ✅ | 022 | — | MINIMAL |
| 030 | Configure Guidelines Card | 3 | ✅ | 022,016 | Group B | STANDARD |
| 031 | Configure Budget Card | 3 | ✅ | 022,016 | Group B | STANDARD |
| 032 | Configure Outcomes Card | 3 | ✅ | 022,016 | Group B | STANDARD |
| 033 | Implement Color Coding Logic | 3 | ✅ | 030,031,032 | — | FULL |
| 034 | Implement Contextual Text Templates | 3 | ✅ | 030,031,032 | — | FULL |
| 040 | Create Trend Card Component | 4 | ✅ | 016 | — | FULL |
| 041 | Implement Sparkline Rendering | 4 | ✅ | 040 | — | FULL |
| 042 | Implement Linear Regression Logic | 4 | ✅ | 040 | — | FULL |
| 043 | Configure Trend Cards for 3 Areas | 4 | ✅ | 041,042 | — | STANDARD |
| 044 | Configure KPI Assessments Subgrid | 4 | ✅ | 006 | — | STANDARD |
| 045 | Add "Add KPI" Ribbon Button | 4 | ✅ | 044 | — | STANDARD |
| 050 | Integration Test: End-to-End Flow | 6 | ✅ | 034,045 | — | STANDARD |
| 051 | Performance Test: API Response Time | 6 | ✅ | 016 | Group C | STANDARD |
| 052 | Performance Test: Subgrid Load Time | 6 | ✅ | 045 | Group C | STANDARD |
| 053 | Test Error Scenarios | 6 | ✅ | 015 | Group C | STANDARD |
| 054 | Validate Accessibility (WCAG 2.1 AA) | 6 | ✅ | 034 | — | STANDARD |
| 055 | Test Dark Mode Compatibility | 6 | ✅ | 034 | — | STANDARD |
| 090 | Project Wrap-up | Wrap | ✅ | 055 | — | FULL |

**Legend:**
- 🔲 Pending
- ⏳ In Progress
- ✅ Completed
- ❌ Blocked

---

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met.

| Group | Tasks | Prerequisite | Description | Safe to Parallelize |
|-------|-------|--------------|-------------|---------------------|
| A | 020 | 006 ✅ | VisualHost research (Phase 5 starts early) | ✅ Yes |
| B | 030, 031, 032 | 022, 016 ✅ | Configure 3 metric cards independently | ✅ Yes (separate configs) |
| C | 051, 052, 053 | Various ✅ | Performance and error tests | ✅ Yes (independent test scenarios) |

**How to Execute Parallel Groups:**
1. Check all prerequisites are complete (✅ in Status)
2. Invoke Task tool with multiple subagents in ONE message
3. Each subagent runs task-execute for one task
4. Wait for all to complete before next group

---

## Critical Path

Longest dependency chain (tasks that block the most downstream work):

```
001 → 002 → 005 → 006 → 010 → 011,012,013 → 016 → 022 → 030,031,032 → 033,034 → 050 → 090
```

**Critical Path Duration**: ~22-24 tasks (sequential core)

**Opportunities for Parallelization**:
- Phase 5 (020-023) can run PARALLEL to Phase 4 (040-047) after task 016 completes
- Tasks 030, 031, 032 (Group B) can run in parallel
- Tasks 051, 052, 053 (Group C) can run in parallel

---

## Phase Summary

### Phase 1: Data Model (001-006)
**Objective**: Create Dataverse entities and forms
**Deliverables**: KPI Assessment entity, 6 grade fields on Matter, Quick Create form
**Status**: 🔲 Not Started

### Phase 2: Calculator API (010-016)
**Objective**: Build API endpoint and web resource trigger
**Deliverables**: Calculator endpoint, current/average/trend calculations, web resource
**Status**: 🔲 Not Started (Blocked by Phase 1)

### Phase 5: VisualHost Enhancement (020-023)
**Objective**: Research and implement Report Card metric card type
**Deliverables**: New/modified VisualHost card component
**Status**: 🔲 Not Started (Can start after 006)
**Note**: Runs in parallel with Phase 4

### Phase 3: Main Tab Cards (030-034)
**Objective**: Configure 3 VisualHost metric cards on main form
**Deliverables**: Guidelines, Budget, Outcomes cards with color coding
**Status**: 🔲 Not Started (Blocked by Phase 5 + Phase 2)

### Phase 4: Report Card Tab (040-045)
**Objective**: Build trend cards, sparkline graphs, and subgrid
**Deliverables**: 3 trend cards, linear regression, subgrid, ribbon button
**Status**: 🔲 Not Started (Blocked by Phase 2)

### Phase 6: Testing & Polish (050-055)
**Objective**: End-to-end testing, performance validation, accessibility
**Deliverables**: Integration tests, performance tests, accessibility validation
**Status**: 🔲 Not Started (Blocked by Phase 3 + Phase 4)

### Wrap-up (090)
**Objective**: Project closure and documentation
**Deliverables**: Updated README, lessons learned, repository cleanup
**Status**: 🔲 Not Started (Blocked by Phase 6)

---

## High-Risk Tasks

| Task ID | Risk | Mitigation |
|---------|------|------------|
| 020-023 | VisualHost card type may need significant customization | Research phase (020) determines scope early |
| 014-015 | Web resource API call may fail silently | Robust error handling with user dialog (015) |
| 041 | Sparkline library may add bundle size | Evaluate lightweight libraries (Victory, Recharts) |
| 042 | Linear regression logic may be incorrect | Unit tests validate trend calculation (016) |

---

## Next Steps

1. **Start with task 001**: `work on task 001` or `/task-execute projects/matter-performance-KPI-r1/tasks/001-create-kpi-assessment-entity.poml`
2. **After 006 completes**: Start task 020 in parallel with 010 (Phase 5 research while Phase 2 builds API)
3. **Monitor progress**: Update task status in this file after each completion

---

*Generated: 2026-02-12 by task-create skill*
*For updates: Task-execute skill auto-updates status as tasks complete*
