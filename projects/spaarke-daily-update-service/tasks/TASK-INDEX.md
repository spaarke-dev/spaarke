# Task Index — Spaarke Daily Update Service

> **Total Tasks**: 27
> **Status**: 15/27 complete
> **Last Updated**: 2026-03-31

---

## Task Registry

| ID | Title | Phase | Status | Dependencies | Rigor | Parallel |
|----|-------|-------|--------|--------------|-------|----------|
| 001 | Extend ActionType enum and PlaybookRunContext | 1 | ✅ | none | FULL | — |
| 002 | Implement CreateNotificationNodeExecutor | 1 | ✅ | 001 | FULL | Group A |
| 003 | Implement NotificationService singleton | 1 | ✅ | 001 | FULL | Group A |
| 004 | Register executor and service in DI | 1 | ✅ | 002, 003 | STANDARD | — |
| 005 | Unit tests for executor and notification service | 1 | ✅ | 004 | STANDARD | — |
| 010 | Implement PlaybookSchedulerService BackgroundService | 2 | ✅ | 004 | FULL | — |
| 011 | Add inline notification to SdapEndpoints (document upload) | 2 | ✅ | 010 | FULL | Group B |
| 012 | Add inline notification to AiToolEndpoints (analysis complete) | 2 | ✅ | 010 | FULL | Group B |
| 013 | Add inline notification to IncomingCommunicationProcessor (email) | 2 | ✅ | 010 | FULL | Group B |
| 014 | Add inline notification for work assignment creation | 2 | ✅ | 010 | FULL | Group B |
| 015 | Unit tests for scheduler and inline notifications | 2 | ✅ | 011-014 | STANDARD | — |
| 020 | Create playbooks 1-3 (tasks overdue, due soon, new docs) | 3 | ✅ | 004 | STANDARD | Group C |
| 021 | Create playbooks 4-5 (new emails, new events) | 3 | ✅ | 004 | STANDARD | Group C |
| 022 | Create playbooks 6-7 (matter activity, work assignments) | 3 | ✅ | 004 | STANDARD | Group C |
| 030 | Scaffold DailyBriefing Code Page | 4 | ✅ | none | FULL | — |
| 031 | Implement notification data service | 4 | ✅ | 030 | FULL | — |
| 032 | Build channel category components | 4 | ✅ | 031 | FULL | Group D |
| 033 | Build narrative TL;DR renderer | 4 | ✅ | 031 | FULL | Group D |
| 034 | Build empty state and mark-read actions | 4 | ✅ | 031 | FULL | Group D |
| 035 | Build preferences panel | 4 | ✅ | 031 | FULL | — |
| 036 | Implement AI briefing summary endpoint | 4 | ✅ | 004 | FULL | Group E |
| 037 | Integrate AI briefing into Daily Digest UI | 4 | ✅ | 036, 031 | FULL | — |
| 040 | Add createNotification node to PlaybookBuilder | 5 | ✅ | 001 | FULL | Group F |
| 041 | Add notification node config panel | 5 | ✅ | 040 | FULL | — |
| 050 | Remove mock NotificationPanel from LegalWorkspace | 6 | ✅ | none | FULL | — |
| 051 | Add Daily Digest auto-popup to LegalWorkspace | 6 | ✅ | 050, 030 | FULL | Group G |
| 052 | Configure App Service WEBSITE_ALWAYS_ON | 6 | ✅ | none | MINIMAL | Group G |
| 053 | Dark mode testing and token audit | 6 | 🔲 | 032-035, 037 | STANDARD | — |
| 054 | Deploy notification playbooks to Dataverse | 6 | 🔲 | 020-022 | STANDARD | Group H |
| 055 | Deploy Daily Digest Code Page to Dataverse | 6 | 🔲 | 030, 037 | STANDARD | Group H |
| 060 | Deploy BFF API | 7 | 🔲 | 010, 036 | STANDARD | — |
| 061 | Integration tests — scheduler flow | 7 | 🔲 | 060, 054, 055 | STANDARD | Group I |
| 062 | Integration tests — inline notifications | 7 | 🔲 | 060 | STANDARD | Group I |
| 063 | End-to-end verification | 7 | 🔲 | 061, 062 | STANDARD | — |
| 090 | Project wrap-up | — | 🔲 | 063 | FULL | — |

---

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| A | 002, 003 | 001 ✅ | Separate .cs files (executor vs service) | ✅ Yes |
| B | 011, 012, 013, 014 | 010 ✅ | Separate endpoint files | ✅ Yes |
| C | 020, 021, 022 | 004 ✅ | Separate playbook JSON files | ✅ Yes |
| D | 032, 033, 034 | 031 ✅ | Separate .tsx components | ✅ Yes |
| E | 036 | 004 ✅ | BFF endpoint (separate from frontend) | ✅ Yes |
| F | 040 | 001 ✅ | PlaybookBuilder types (separate from BFF) | ✅ Yes |
| G | 051, 052 | 050 ✅ (051), none (052) | Separate files (LegalWorkspace vs Azure config) | ✅ Yes |
| H | 054, 055 | 020-022 ✅ (054), 030+037 ✅ (055) | Separate deployments | ✅ Yes |
| I | 061, 062 | 060 ✅ | Separate test files | ✅ Yes |

**How to Execute Parallel Groups:**
1. Check all prerequisites are complete (✅ in Status)
2. Invoke task-execute with multiple tasks in ONE message
3. Each task agent runs task-execute independently
4. Wait for all to complete before next group

---

## Execution Order (Optimized for Parallelism)

### Wave 1 (No Dependencies — Start Immediately)
- **001** — Extend ActionType enum and PlaybookRunContext (serial, foundation)
- **030** — Scaffold DailyBriefing Code Page (serial, can start in parallel with 001)
- **050** — Remove mock NotificationPanel (serial, can start in parallel with 001)
- **052** — Configure WEBSITE_ALWAYS_ON (minimal, can start in parallel)

### Wave 2 (After 001)
- **Group A**: 002, 003 (parallel — executor + service)
- **Group F**: 040 (parallel with Group A — PlaybookBuilder types)

### Wave 3 (After 002+003, 030)
- **004** — Register in DI (serial)
- **031** — Notification data service (serial, depends on 030)

### Wave 4 (After 004, 031)
- **005** — Unit tests (serial)
- **010** — PlaybookSchedulerService (serial, critical path)
- **Group C**: 020, 021, 022 (parallel — playbooks)
- **Group D**: 032, 033, 034 (parallel — UI components)
- **Group E**: 036 (parallel — AI endpoint)
- **035** — Preferences panel (serial)
- **041** — Notification node config (depends on 040)

### Wave 5 (After 010, 031, 036)
- **Group B**: 011, 012, 013, 014 (parallel — inline notifications)
- **037** — AI briefing UI integration (depends on 036 + 031)
- **051** — Auto-popup (depends on 050 + 030)

### Wave 6 (After Groups B, C, D, E)
- **015** — Unit tests scheduler/inline
- **053** — Dark mode audit
- **Group H**: 054, 055 (parallel — deployments)

### Wave 7 (After deployments)
- **060** — Deploy BFF API
- **Group I**: 061, 062 (parallel — integration tests, after 060)

### Wave 8 (Final)
- **063** — E2E verification
- **090** — Project wrap-up

---

## Rigor Level Distribution

| Level | Count | Tasks |
|-------|-------|-------|
| FULL | 18 | 001-003, 010-014, 030-037, 040-041, 050-051, 090 |
| STANDARD | 8 | 004-005, 015, 020-022, 053-055, 060-063 |
| MINIMAL | 1 | 052 |

---

## Critical Path

```
001 → 002/003 → 004 → 010 → 011-014 → 015 → 060 → 061/062 → 063 → 090
                           ↘ 020-022 → 054 ↗
001 → 030 → 031 → 032-034 → 053 → 055 → 061
                  → 036 → 037 ↗
```

---

*Generated by project-pipeline on 2026-03-30*
