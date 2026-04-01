# Daily Update Service — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-30
> **Source**: design.md (playbook-driven notification system + Daily Digest)

## Executive Summary

Extend the existing Playbook execution engine with a `CreateNotification` node executor (ActionType 50) and a `PlaybookSchedulerService` to generate `appnotification` records on schedule. Build a Daily Digest Code Page (`sprk_dailyupdate`) that queries those notifications, groups by category, and presents a narrative TL;DR with action links and optional AI-generated briefing summary. Remove the mock NotificationPanel from LegalWorkspace — the MDA native bell icon handles real-time notifications.

## Scope

### In Scope
- `CreateNotificationNodeExecutor` — new `INodeExecutor` (ActionType 50) following existing `NodeExecutorRegistry` pattern
- `createNotification` canvas type added to PlaybookBuilder node palette
- `PlaybookSchedulerService` — new `BackgroundService` that runs notification-mode playbooks on schedule
- `PlaybookRunContext` extension — add `UserId` and `UserPreferences` fields
- `ActionType` enum extension — add `CreateNotification = 50`
- `NodeExecutorRegistry` registration for ActionType 50
- `NotificationService` — shared BFF singleton for inline `appnotification` creation
- Inline notification calls in existing BFF endpoints (document upload, analysis complete, email received, work assignment created)
- Notification deduplication — idempotency check in `CreateNotificationNodeExecutor`
- 7 notification playbooks deployed (`sprk_playbooktype = Notification (2)`):
  1. Tasks overdue
  2. Tasks due soon (configurable window)
  3. New documents on user's matters
  4. New emails related to user's matters
  5. New events on user's matters/projects
  6. Matter/project activity
  7. New work assignments for user
- `sprk_dailyupdate` Code Page (React 19, Vite single-file build)
- AI briefing summary — `POST /api/ai/daily-briefing/summarize` BFF endpoint
- User-customizable channel preferences (opt-out model via `sprk_userpreference`)
- Template-based narrative format (Level 1) + AI narrative (Level 2)
- Clear/dismiss items (mark read, mark all read via `appnotification` built-in)
- Auto-popup on workspace launch (once per session via `sessionStorage`)
- Remove mock `NotificationPanel` from LegalWorkspace
- Dark mode support via unified theme utility

### Out of Scope
- Event-driven triggers (data change detection) — requires BFF middleware, future workflow mode
- Date arithmetic in templates (`{{date + P12M}}`) — workflow mode feature
- Custom user-defined notification rules — R3
- Shared/team digests
- Custom notification panel in workspace (use MDA native bell)

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/` — new `CreateNotificationNodeExecutor`
- `src/server/api/Sprk.Bff.Api/Services/Ai/INodeExecutor.cs` — add ActionType 50
- `src/server/api/Sprk.Bff.Api/Services/Ai/NodeExecutorRegistry.cs` — register new executor
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookRunContext.cs` — add UserId, UserPreferences
- `src/server/api/Sprk.Bff.Api/Services/` — new `NotificationService`, `PlaybookSchedulerService`
- `src/server/api/Sprk.Bff.Api/Api/` — new AI briefing endpoint
- `src/server/api/Sprk.Bff.Api/Api/SdapEndpoints.cs` — inline notification after upload
- `src/server/api/Sprk.Bff.Api/Api/Ai/AiToolEndpoints.cs` — inline notification after analysis
- `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingCommunicationProcessor.cs` — inline notification after email
- `src/client/code-pages/PlaybookBuilder/` — add `createNotification` node type to palette
- `src/solutions/` — new `DailyBriefing` Code Page solution
- `src/solutions/LegalWorkspace/src/components/NotificationPanel/` — remove entirely
- `src/solutions/LegalWorkspace/src/hooks/useNotifications.ts` — remove

## Requirements

### Functional Requirements

1. **FR-01**: `CreateNotificationNodeExecutor` creates `appnotification` records via Dataverse Web API when executed as a playbook node — Acceptance: Executor registered at ActionType 50, creates valid `appnotification` with title, body, category, priority, action URL, regarding record, and AI metadata fields
2. **FR-02**: `CreateNotificationNodeExecutor` performs idempotency check before creating — Acceptance: If unread `appnotification` exists for same user + regarding record + category, creation is skipped
3. **FR-03**: `PlaybookSchedulerService` runs notification-mode playbooks on configurable schedule — Acceptance: Reads `sprk_analysisplaybook` where `sprk_playbooktype = Notification (2)`, executes via `PlaybookOrchestrationService.ExecutePlaybookAsync()`, processes users in parallel (throttled), survives App Service restarts via persisted last-run timestamps
4. **FR-04**: Scheduler uses opt-out subscription model — Acceptance: All notification playbooks run for all users by default; `sprk_userpreference` stores only overrides (disabled channels, parameter customizations)
5. **FR-05**: User preference parameters inject as template parameters into playbook execution — Acceptance: `{{dueWithinDays}}`, `{{timeWindow}}`, `{{minConfidence}}` resolve from user preferences during `PlaybookRunContext` initialization
6. **FR-06**: Inline `NotificationService` creates `appnotification` records after BFF operations — Acceptance: Notifications created for document uploads (SdapEndpoints), analysis completions (AiToolEndpoints), email receipt (IncomingCommunicationProcessor), work assignment creation
7. **FR-07**: 7 notification playbooks deployed to Dataverse — Acceptance: Each playbook uses `sprk_playbooktype = Notification (2)`, schedule config in `sprk_configjson`, and produces `appnotification` records via `CreateNotification` node
8. **FR-08**: `createNotification` canvas type available in PlaybookBuilder palette — Acceptance: Node can be dragged onto canvas, maps to `NodeType.Workflow` / `ActionType.CreateNotification (50)`, config panel shows title, body, category, priority, action URL fields
9. **FR-09**: Daily Digest Code Page renders narrative TL;DR — Acceptance: Queries `appnotification` via `Xrm.WebApi.retrieveMultipleRecords`, groups by category (from `customData.category` in notification data JSON), renders template-based narrative sentences per category with item list and action links
10. **FR-10**: Daily Digest auto-popup on workspace launch — Acceptance: LegalWorkspace checks `sprk_userpreference` for `autoPopup` flag, opens `sprk_dailyupdate` via `Xrm.Navigation.navigateTo` (60% × 80% dialog), tracks shown state in `sessionStorage` (once per session)
11. **FR-11**: Users can mark items read/dismissed — Acceptance: Per-item mark-read button, "Mark All Read" batch action, both update `appnotification.isread` via `Xrm.WebApi.updateRecord`
12. **FR-12**: Preferences panel allows toggling channels and configuring parameters — Acceptance: Inline panel with Switch per channel, Dropdown per parameter (due window: 1/2/3/5/7 days; time window: 12h/24h/48h/7d; AI confidence: 60/75/85/95%), saves to `sprk_userpreference` (type 100000002)
13. **FR-13**: AI briefing summary generates contextual narrative — Acceptance: `POST /api/ai/daily-briefing/summarize` accepts structured notification data, returns 3-4 sentence prioritized briefing via Azure OpenAI, renders at top of digest with priority action items
14. **FR-14**: Mock NotificationPanel removed from LegalWorkspace — Acceptance: `NotificationPanel/` component directory and `useNotifications.ts` hook deleted, no references remain, MDA native bell icon serves as notification UX
15. **FR-15**: Empty state renders when no unread notifications — Acceptance: "You're all caught up!" message with last-checked timestamp

### Non-Functional Requirements

- **NFR-01**: Scheduler processes all users within 15 minutes (parallel processing, `MaxDegreeOfParallelism = 5`)
- **NFR-02**: Daily Digest page load < 2 seconds (parallel `Promise.allSettled` queries, no waterfall)
- **NFR-03**: Individual channel failure does not crash digest (graceful degradation with inline error per channel)
- **NFR-04**: All configuration via environment variables — no hardcoded endpoints, keys, or schedule times (BYOK/multi-tenant compatible)
- **NFR-05**: Dark mode fully supported via unified theme utility (`themeStorage.ts`)

## Technical Constraints

### Applicable ADRs
- **ADR-001**: `PlaybookSchedulerService` as BackgroundService — no Azure Functions
- **ADR-006**: Daily Digest as Code Page dialog — not PCF, not custom page
- **ADR-010**: DI minimalism — `NotificationService` registered as singleton
- **ADR-012**: Shared components from `@spaarke/ui-components` for digest UI
- **ADR-013**: AI briefing endpoint extends BFF — not separate service
- **ADR-021**: Fluent UI v9 exclusively; semantic tokens; dark mode required

### MUST Rules
- ✅ MUST use native `appnotification` entity — no custom notification table
- ✅ MUST use existing `PlaybookOrchestrationService` for notification playbook execution
- ✅ MUST register `CreateNotificationNodeExecutor` via `NodeExecutorRegistry[ActionType]`
- ✅ MUST use existing `sprk_playbooktype` field (Notification = 2) to distinguish playbook types
- ✅ MUST store schedule config in `sprk_configjson` (no new schema fields)
- ✅ MUST implement opt-out subscription model (all playbooks active by default)
- ✅ MUST implement idempotency check before creating notifications
- ✅ MUST generate inline notifications via `NotificationService` for user-action triggers
- ✅ MUST use `Xrm.Navigation.navigateTo` to open Daily Digest as dialog
- ✅ MUST query `appnotification` via `Xrm.WebApi.retrieveMultipleRecords`
- ✅ MUST fetch channels via `Promise.allSettled` — individual failures show inline error
- ✅ MUST label AI-identified items clearly (confidence badge, "AI Insight" indicator)
- ✅ MUST set `WEBSITE_ALWAYS_ON = true` on App Service (BackgroundService requirement)
- ❌ MUST NOT build a separate notification engine — use existing playbook engine
- ❌ MUST NOT build a custom notification panel in workspace — use MDA native bell

### Existing Patterns to Follow
- See `Services/Ai/Nodes/CreateTaskNodeExecutor.cs` for `INodeExecutor` implementation pattern
- See `Services/Ai/NodeExecutorRegistry.cs` for executor registration
- See `Services/Ai/PlaybookOrchestrationService.cs` for batch execution flow
- See `Services/Ai/PlaybookRunContext.cs` for run context structure
- See `src/client/code-pages/PlaybookBuilder/src/types/playbook.ts` for canvas type definitions

### Architecture Reference Documents
- `docs/architecture/playbook-architecture.md` — Playbook engine, node executors, execution flow
- `docs/guides/JPS-AUTHORING-GUIDE.md` — JPS authoring, prompt schema, playbook design
- `docs/guides/SCOPE-CONFIGURATION-GUIDE.md` — Scope configuration, pre-fill, builder
- `docs/architecture/ai-document-summary-architecture.md` — Document creation flows (notification trigger points)

## Success Criteria

1. [ ] `CreateNotificationNodeExecutor` registered in `NodeExecutorRegistry` (ActionType 50) — Verify: unit test creating notification via executor
2. [ ] `createNotification` node available in PlaybookBuilder canvas — Verify: drag node, configure, save
3. [ ] `PlaybookSchedulerService` runs notification playbooks on schedule — Verify: scheduler creates notifications for test user with tasks due
4. [ ] BFF creates `appnotification` inline for document uploads, analysis completions, emails, assignments — Verify: upload document, check MDA bell
5. [ ] 7 notification playbooks deployed and generating notifications — Verify: run each playbook, confirm `appnotification` records created
6. [ ] Notifications appear in MDA native bell icon — Verify: visual check in Power App
7. [ ] Daily Digest dialog opens on workspace launch (auto-popup enabled) — Verify: enable preference, reload workspace
8. [ ] Digest shows narrative TL;DR grouped by channels — Verify: create test notifications, open digest
9. [ ] Each item links to correct source record — Verify: click item, confirm navigation
10. [ ] Channel toggles and parameters work in preferences — Verify: disable channel, change window, save, reopen
11. [ ] Mark read / mark all read works — Verify: mark items, confirm blue dot disappears in MDA bell
12. [ ] Preferences persist to Dataverse (cross-device) — Verify: set preference on one browser, confirm on another
13. [ ] AI briefing summary generates contextual narrative — Verify: enable AI briefing, confirm summary renders with priority items
14. [ ] Duplicate notifications prevented — Verify: run scheduler twice, confirm no duplicate `appnotification` records
15. [ ] Opt-out model works — new playbooks auto-apply — Verify: deploy new playbook, confirm notifications generated for users without explicit opt-in
16. [ ] Mock NotificationPanel removed from LegalWorkspace — Verify: grep for `NotificationPanel` returns no results
17. [ ] Empty state displays when no notifications — Verify: mark all read, reopen digest
18. [ ] Dark mode renders correctly — Verify: toggle theme, confirm digest uses semantic tokens
19. [ ] New notification playbooks deployable without code changes — Verify: deploy new playbook definition, confirm scheduler picks it up
20. [ ] App Service `WEBSITE_ALWAYS_ON = true` configured — Verify: check App Service configuration

## Dependencies

### Prerequisites
- `sprk_userpreference` entity (exists)
- `appnotification` entity (system, exists)
- `sprk_analysisplaybook` entity with `sprk_playbooktype` field (exists)
- `PlaybookOrchestrationService` (exists)
- `NodeExecutorRegistry` (exists)
- `INodeExecutor` interface and existing executors (exists)
- Azure OpenAI endpoint (exists — for AI briefing)
- AI Search semantic index (exists — for R2 similarity playbooks)
- `DataverseWebApiService` (exists)

### External Dependencies
- None — all Azure resources already provisioned

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Playbook type field | New field or existing? | Existing `sprk_playbooktype` with Notification (2) | No schema changes needed |
| Schedule storage | Where to store schedule config? | In `sprk_configjson` on playbook record | No schema changes needed |
| Subscription model | Opt-in or opt-out? | Opt-out — all playbooks run for all users by default | Simpler, higher engagement, preferences store only overrides |
| Deduplication | How to prevent duplicates? | Idempotency check — query before create | Small query overhead but guaranteed no duplicates |
| R1 playbooks | Which playbooks in R1? | 7 deterministic (no budget burn rate) | Budget burn rate deferred to R2 with financial intelligence |
| Mock panel | Keep or remove? | Remove entirely | Clean UX — MDA bell + Daily Digest only |
| AI briefing | R1 or R2? | R1 — low effort, high value | One endpoint + one prompt, uses existing Azure OpenAI |

## Assumptions

- **Scheduler frequency**: Assuming hourly check with daily execution per playbook (configurable via `sprk_configjson`). If more granular scheduling needed, can be adjusted without code changes.
- **User count**: Assuming < 500 active users for R1 scheduler performance. If >500, may need batching optimization.
- **appnotification TTL**: Assuming 14-day default expiry. Old notifications auto-clean.

## Unresolved Questions

None — all design questions resolved during design phase.

---

*AI-optimized specification. Original: design.md*
