# Daily Update Service — Implementation Plan

> **Status**: Active
> **Created**: 2026-03-30
> **Estimated Effort**: 12-16 days

---

## 1. Executive Summary

### Purpose
Extend the Spaarke playbook engine with notification generation capabilities and build a Daily Digest UI that consolidates user notifications into a categorized, narrative view.

### Scope
- Backend: New node executor, scheduler service, notification service, AI briefing endpoint
- Frontend: Daily Digest Code Page, PlaybookBuilder palette extension
- Cleanup: Remove mock NotificationPanel from LegalWorkspace
- Data: 7 notification playbook definitions, user preference schema usage

### Critical Path
ActionType enum → CreateNotificationNodeExecutor → PlaybookSchedulerService → Notification playbooks → Daily Digest Code Page → Integration

---

## 2. Architecture Context

### Key Constraints
- **ADR-001**: PlaybookSchedulerService as BackgroundService — no Azure Functions
- **ADR-006**: Daily Digest as Code Page (React 19, Vite) — not PCF, not custom page
- **ADR-010**: DI minimalism — NotificationService as singleton, feature module registration
- **ADR-012**: Shared components from `@spaarke/ui-components` for digest UI
- **ADR-013**: AI briefing endpoint extends BFF — not separate service
- **ADR-021**: Fluent UI v9 exclusively; semantic tokens; dark mode required

### Technology Stack
- .NET 8 Minimal API (backend)
- React 19 + Vite + Fluent v9 (Daily Digest Code Page)
- PlaybookBuilder (existing React Code Page, canvas extension)
- Dataverse `appnotification` entity (system, existing)
- Azure OpenAI (AI briefing summary)

### Integration Points
- `NodeExecutorRegistry` — register ActionType 50
- `PlaybookOrchestrationService` — execute notification playbooks
- `PlaybookRunContext` — extend with UserId/UserPreferences
- BFF endpoints (SdapEndpoints, AiToolEndpoints, IncomingCommunicationProcessor)
- LegalWorkspace — remove NotificationPanel, add auto-popup trigger
- PlaybookBuilder — add createNotification canvas type

### Discovered Resources
- **ADRs**: ADR-001, ADR-006, ADR-010, ADR-012, ADR-013, ADR-021
- **Patterns**: `background-workers.md`, `endpoint-definition.md`, `endpoint-filters.md`, `streaming-endpoints.md`
- **Constraints**: `api.md`, `jobs.md`, `ai.md`, `webresource.md`
- **Architecture docs**: `playbook-architecture.md`, `PLAYBOOK-DESIGN-GUIDE.md`
- **Existing code**: `CreateTaskNodeExecutor.cs`, `NodeExecutorRegistry.cs`, `PlaybookOrchestrationService.cs`, `PlaybookRunContext.cs`
- **Code Page pattern**: `src/solutions/` (CreateMatterWizard, LegalWorkspace, etc.)

---

## 3. Implementation Approach

This plan is structured for **maximum parallelism** — phases are organized so independent tasks can run concurrently via Claude Code task agents.

### Phase Structure
| Phase | Name | Tasks | Parallel Groups |
|-------|------|-------|-----------------|
| 1 | Foundation: Node Executor & Notification Service | 001-005 | A (002, 003) |
| 2 | Scheduler & Inline Notifications | 010-015 | B (011, 012, 013, 014) |
| 3 | Notification Playbooks | 020-022 | C (020, 021, 022) |
| 4 | Daily Digest Code Page | 030-037 | D (032, 033, 034), E (036, 037) |
| 5 | PlaybookBuilder Extension | 040-041 | F (040, 041) |
| 6 | Integration, Cleanup & Polish | 050-055 | G (051, 052), H (054, 055) |
| 7 | Testing & Deployment | 060-063 | I (061, 062) |
| — | Wrap-up | 090 | — |

---

## 4. WBS (Work Breakdown Structure)

### Phase 1: Foundation — Node Executor & Notification Service
**Objective**: Create the CreateNotificationNodeExecutor and NotificationService as the foundation for all notification generation.

| Task | Title | Est. | Parallel |
|------|-------|------|----------|
| 001 | Extend ActionType enum and PlaybookRunContext | 2h | — (serial, foundation) |
| 002 | Implement CreateNotificationNodeExecutor | 3h | Group A |
| 003 | Implement NotificationService singleton | 3h | Group A |
| 004 | Register executor and service in DI | 2h | — (depends on 002, 003) |
| 005 | Unit tests for executor and notification service | 3h | — (depends on 004) |

**Inputs**: Existing `INodeExecutor`, `NodeExecutorRegistry`, `PlaybookRunContext`
**Outputs**: Working executor + service, registered in DI, unit tested

### Phase 2: Scheduler & Inline Notifications
**Objective**: Build the PlaybookSchedulerService and wire inline notifications into existing BFF endpoints.

| Task | Title | Est. | Parallel |
|------|-------|------|----------|
| 010 | Implement PlaybookSchedulerService BackgroundService | 4h | — (serial, depends on 004) |
| 011 | Add inline notification to SdapEndpoints (document upload) | 2h | Group B |
| 012 | Add inline notification to AiToolEndpoints (analysis complete) | 2h | Group B |
| 013 | Add inline notification to IncomingCommunicationProcessor (email) | 2h | Group B |
| 014 | Add inline notification for work assignment creation | 2h | Group B |
| 015 | Unit tests for scheduler and inline notifications | 3h | — (depends on 011-014) |

**Inputs**: NotificationService, PlaybookOrchestrationService, existing BFF endpoints
**Outputs**: Scheduler running on schedule, inline notifications firing from 4 BFF operations

### Phase 3: Notification Playbooks
**Objective**: Create and deploy the 7 notification playbook definitions.

| Task | Title | Est. | Parallel |
|------|-------|------|----------|
| 020 | Create playbooks 1-3 (tasks overdue, tasks due soon, new documents) | 3h | Group C |
| 021 | Create playbooks 4-5 (new emails, new events) | 2h | Group C |
| 022 | Create playbooks 6-7 (matter activity, work assignments) | 2h | Group C |

**Inputs**: Playbook template format, notification schema, existing playbook examples
**Outputs**: 7 playbook JSON definitions ready for deployment

### Phase 4: Daily Digest Code Page
**Objective**: Build the `sprk_dailyupdate` Code Page with categorized notification view, preferences, and AI briefing.

| Task | Title | Est. | Parallel |
|------|-------|------|----------|
| 030 | Scaffold DailyBriefing Code Page (Vite + React 19 + Fluent v9) | 2h | — (serial, setup) |
| 031 | Implement notification data service (Xrm.WebApi queries) | 3h | — (depends on 030) |
| 032 | Build channel category components (grouped notification cards) | 3h | Group D |
| 033 | Build narrative TL;DR template renderer | 2h | Group D |
| 034 | Build empty state and mark-read actions | 2h | Group D |
| 035 | Build preferences panel (channel toggles, parameter dropdowns) | 3h | — (depends on 031) |
| 036 | Implement AI briefing summary endpoint | 3h | Group E |
| 037 | Integrate AI briefing into Daily Digest UI | 2h | Group E (depends on 036 for endpoint, but UI work can start) |

**Inputs**: `appnotification` entity, `sprk_userpreference` entity, Azure OpenAI
**Outputs**: Complete Daily Digest Code Page with all features

### Phase 5: PlaybookBuilder Extension
**Objective**: Add the `createNotification` canvas type to the PlaybookBuilder node palette.

| Task | Title | Est. | Parallel |
|------|-------|------|----------|
| 040 | Add createNotification node type to PlaybookBuilder types and palette | 3h | Group F |
| 041 | Add configuration panel for createNotification node | 2h | Group F |

**Inputs**: PlaybookBuilder canvas types, existing node type definitions
**Outputs**: Draggable createNotification node with config panel

### Phase 6: Integration, Cleanup & Polish
**Objective**: Wire Daily Digest into LegalWorkspace, remove mock panel, add auto-popup.

| Task | Title | Est. | Parallel |
|------|-------|------|----------|
| 050 | Remove mock NotificationPanel from LegalWorkspace | 2h | — (serial) |
| 051 | Add Daily Digest auto-popup to LegalWorkspace | 2h | Group G |
| 052 | Configure App Service WEBSITE_ALWAYS_ON | 1h | Group G |
| 053 | Dark mode testing and token audit | 2h | — (depends on 030-037) |
| 054 | Deploy notification playbooks to Dataverse | 2h | Group H |
| 055 | Deploy Daily Digest Code Page to Dataverse | 2h | Group H |

**Inputs**: All prior phases complete
**Outputs**: Fully integrated, deployed, cleaned up

### Phase 7: Testing & Deployment
**Objective**: End-to-end testing and final deployment.

| Task | Title | Est. | Parallel |
|------|-------|------|----------|
| 060 | Deploy BFF API with scheduler and notification services | 2h | — (serial) |
| 061 | Integration tests for scheduler → notification → digest flow | 3h | Group I |
| 062 | Integration tests for inline notifications | 2h | Group I |
| 063 | End-to-end verification against success criteria | 2h | — (final) |

### Wrap-up

| Task | Title | Est. |
|------|-------|------|
| 090 | Project wrap-up (code-review, adr-check, repo-cleanup, README update) | 2h |

---

## 5. Dependencies

### External
- None — all Azure resources already provisioned

### Internal
- `PlaybookOrchestrationService` (exists)
- `NodeExecutorRegistry` (exists)
- `INodeExecutor` interface (exists)
- `appnotification` entity (system, exists)
- `sprk_userpreference` entity (exists)
- `sprk_analysisplaybook` with `sprk_playbooktype` field (exists)
- Azure OpenAI endpoint (exists)

---

## 6. Testing Strategy

### Unit Tests
- `CreateNotificationNodeExecutor` — creates notification, idempotency check skips duplicate
- `NotificationService` — creates `appnotification` with correct fields
- `PlaybookSchedulerService` — queries playbooks, processes users in parallel
- AI briefing endpoint — generates summary from structured notification data

### Integration Tests
- Scheduler → PlaybookOrchestrationService → CreateNotificationNodeExecutor → `appnotification` record
- Inline notification from document upload → notification appears
- Daily Digest queries → renders grouped notifications

### Acceptance Tests
- All 20 success criteria from spec.md verified

---

## 7. Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| `appnotification` entity API limitations | High | Validate schema fields early in Phase 1 |
| Scheduler performance with many users | Medium | MaxDegreeOfParallelism = 5, test with simulated load |
| PlaybookBuilder canvas type conflicts | Low | Check existing types before adding ActionType 50 |
| DI registration count limit (≤15) | Low | Use feature module extension for notification services |

---

## 8. Acceptance Criteria

See [spec.md](spec.md) — 20 success criteria covering all functional and non-functional requirements.

---

## 9. Next Steps

1. Run `/task-create` to generate POML task files from this plan
2. Start with Task 001 (ActionType enum extension)
3. Execute parallel groups as identified above

---

*Generated by project-pipeline on 2026-03-30*
