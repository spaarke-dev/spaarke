# Task Index: Events and Workflow Automation R1

> **Last Updated**: 2026-02-01
> **Project**: events-and-workflow-automation-r1
> **Total Tasks**: 33
> **Status**: Ready for Execution

---

## Task Status Overview

| Status | Count | Meaning |
|--------|-------|---------|
| 🔲 | 32 | Not started |
| 🔄 | 0 | In progress |
| ✅ | 0 | Completed |
| ⏸️ | 0 | Blocked |

---

## Phase 1: Foundation & Data Model (Tasks 001-005)

| ID | Title | Status | Dependencies | Parallel Group | Est. Hours |
|----|-------|--------|--------------|----------------|------------|
| 001 | Create Field Mapping Profile table | 🔲 | none | — | 2 |
| 002 | Create Field Mapping Rule table | 🔲 | 001 | — | 2 |
| 003 | Seed Event Type records | 🔲 | none | A | 2 |
| 004 | Configure Event form with control placeholders | 🔲 | none | A | 3 |
| 005 | Scaffold PCF project structure | 🔲 | none | A | 4 |

---

## Phase 2: Field Mapping Framework (Tasks 010-016)

| ID | Title | Status | Dependencies | Parallel Group | Est. Hours |
|----|-------|--------|--------------|----------------|------------|
| 010 | Implement FieldMappingService shared component | 🔲 | 002 | — | 4 |
| 011 | Implement type compatibility validation | 🔲 | 010 | — | 3 |
| 012 | Build FieldMappingAdmin PCF control | 🔲 | 010 | B | 4 |
| 013 | Create Field Mapping API - GET profiles | 🔲 | 010 | B | 3 |
| 014 | Create Field Mapping API - GET profile by source/target | 🔲 | 013 | — | 2 |
| 015 | Create Field Mapping API - POST validate | 🔲 | 011 | — | 2 |
| 016 | Deploy Phase 2 - Field Mapping Framework | 🔲 | 012, 015 | — | 2 |

---

## Phase 3: Association Resolver (Tasks 020-025)

| ID | Title | Status | Dependencies | Parallel Group | Est. Hours |
|----|-------|--------|--------------|----------------|------------|
| 020 | Build AssociationResolver PCF - entity type dropdown | 🔲 | 005, 010 | — | 4 |
| 021 | Implement AssociationResolver - regarding field population | 🔲 | 020 | — | 3 |
| 022 | Integrate AssociationResolver with FieldMappingService | 🔲 | 021 | — | 3 |
| 023 | Add Refresh from Parent functionality | 🔲 | 022 | — | 2 |
| 024 | Add toast notifications for mapping results | 🔲 | 022 | — | 2 |
| 025 | Deploy Phase 3 - AssociationResolver PCF | 🔲 | 024 | — | 2 |

---

## Phase 4: Event Form Controls (Tasks 030-036)

| ID | Title | Status | Dependencies | Parallel Group | Est. Hours |
|----|-------|--------|--------------|----------------|------------|
| 030 | Build EventFormController PCF - Event Type fetching | 🔲 | 005 | — | 3 |
| 031 | Implement EventFormController - field show/hide logic | 🔲 | 030 | — | 3 |
| 032 | Implement EventFormController - save validation | 🔲 | 031 | — | 2 |
| 033 | Build RegardingLink PCF control | 🔲 | 005 | C | 3 |
| 034 | Build UpdateRelatedButton PCF control | 🔲 | 005, 054 | — | 4 |
| 035 | Configure Event form with all controls | 🔲 | 025, 032, 033 | — | 3 |
| 036 | Deploy Phase 4 - Event Form Controls | 🔲 | 035 | — | 2 |

---

## Phase 5: API & Event Log (Tasks 050-058)

| ID | Title | Status | Dependencies | Parallel Group | Est. Hours |
|----|-------|--------|--------------|----------------|------------|
| 050 | Create Event API - GET endpoints | 🔲 | 002 | D | 3 |
| 051 | Create Event API - POST/PUT endpoints | 🔲 | 050 | — | 3 |
| 052 | Create Event API - DELETE endpoint | 🔲 | 050 | D | 2 |
| 053 | Create Event API - complete/cancel actions | 🔲 | 051 | — | 2 |
| 054 | Create Field Mapping API - POST push | 🔲 | 015 | — | 4 |
| 055 | Implement Event Log creation on state changes | 🔲 | 053 | — | 3 |
| 056 | Write integration tests for Event API | 🔲 | 055 | E | 4 |
| 057 | Write integration tests for Field Mapping API | 🔲 | 054 | E | 3 |
| 058 | Deploy Phase 5 - BFF API | 🔲 | 057 | — | 2 |

---

## Phase 6: Integration & Testing (Tasks 060-065)

| ID | Title | Status | Dependencies | Parallel Group | Est. Hours |
|----|-------|--------|--------------|----------------|------------|
| 060 | E2E test - Event creation with regarding record | 🔲 | 036, 058 | F | 4 |
| 061 | E2E test - Field mapping auto-application | 🔲 | 036, 058 | F | 3 |
| 062 | E2E test - Refresh from Parent flow | 🔲 | 036, 058 | F | 2 |
| 063 | E2E test - Update Related push flow | 🔲 | 036, 058 | F | 3 |
| 064 | Dark mode verification - all PCF controls | 🔲 | 036 | — | 3 |
| 065 | Performance validation and bundle size check | 🔲 | 036 | — | 2 |

---

## Phase 7: Deployment & Wrap-up (Tasks 070-074, 090)

| ID | Title | Status | Dependencies | Parallel Group | Est. Hours |
|----|-------|--------|--------------|----------------|------------|
| 070 | Deploy solution to dev environment | 🔲 | 065 | — | 3 |
| 071 | User acceptance testing scenarios | 🔲 | 070 | — | 4 |
| 072 | Create user documentation | 🔲 | 071 | G | 4 |
| 073 | Create admin documentation | 🔲 | 071 | G | 3 |
| 074 | Update README status to Complete | 🔲 | 072, 073 | — | 1 |
| 090 | Project Wrap-up | 🔲 | 074 | — | 2 |

---

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| A | 003, 004, 005 | none | Separate: Dataverse, Forms, PCF scaffolds | ✅ Yes |
| B | 012, 013 | 010 ✅ | Separate: PCF control, API endpoint | ✅ Yes |
| C | 033 | 005 ✅ | RegardingLink PCF (independent) | ✅ Yes |
| D | 050, 052 | 002 ✅ | Separate: GET vs DELETE endpoints | ✅ Yes |
| E | 056, 057 | 054, 055 ✅ | Separate test files | ✅ Yes |
| F | 060, 061, 062, 063 | 036, 058 ✅ | Separate E2E test scenarios | ✅ Yes |
| G | 072, 073 | 071 ✅ | Separate documentation files | ✅ Yes |

**How to Execute Parallel Groups:**
1. Check all prerequisites are complete (✅ in Status)
2. Invoke Task tool with multiple subagents in ONE message
3. Each subagent runs task-execute for one task
4. Wait for all to complete before next group

---

## Critical Path

The longest dependency chain that determines minimum project duration:

```
001 → 002 → 010 → 011 → 015 → 054 → 034 → 035 → 036 → 060 → 070 → 071 → 074 → 090
```

**Critical Path Summary:**
- Field Mapping tables must be created first
- FieldMappingService is the core dependency
- Push API (054) blocks UpdateRelatedButton PCF (034)
- Integration testing blocks deployment
- UAT blocks documentation and wrap-up

---

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 010 | FieldMappingService complexity | Start simple, add cascading later |
| 012 | FieldMappingAdmin PCF is complex | Reference existing admin patterns |
| 020 | AssociationResolver must support 8 entity types | Use entity configuration pattern |
| 034 | UpdateRelatedButton needs push API | Ensure API complete before starting |
| 064 | Dark mode across 5 controls | Use Fluent UI v9 tokens only |

---

## Quick Start

**To begin implementation:**
```
work on task 001
```

**To check current status:**
```
/project-status events-and-workflow-automation-r1
```

**To continue after break:**
```
continue
```

---

*This index is updated by task-execute skill as tasks progress.*
