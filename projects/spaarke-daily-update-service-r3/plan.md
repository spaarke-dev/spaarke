# Project Plan: Daily Briefing — Read-State Decoupling + Producer TTL Hardening (R3)

> **Last Updated**: 2026-06-24
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md) | **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Decouple the Daily Briefing widget's read-state from the native bell panel's `toasttype` field (fixing the UAT-reported empty-state defect), and fix a parallel producer-side TTL field-name defect in `NotificationService.cs`. Add per-item user actions (Check / Remove / Keep +7d) so users can manage briefing items without affecting their bell-panel lifecycle.

**Scope**:
- New `sprk_briefingstate` Choice column on `appnotification` (Unread/Checked/Removed) — operator deploy
- 1-line fix in `NotificationService.cs` (`ttlindays` → `ttlinseconds`) + unit test
- Widget service layer: read-state field swap + 3 new action functions + Removed-state filter + jest tests
- Widget hook layer: `useBriefingActions` extended with 3 new handlers + jest tests
- Widget UI layer: 3 new per-item buttons (Fluent v9 icons) + props wiring + handler composition + optimistic UI + toast
- Manual UAT in spaarkedev1 covering 7 ACs

**Timeline**: ~1 day (sequential) or ~½ day (with parallel Wave 1) | **Estimated Effort**: ~6 hours engineering + 30 min operator

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):

- **ADR-001** — BFF Minimal API: `NotificationService.cs` change is a 1-line bug fix in an existing service. No new endpoints. No new Azure Functions.
- **ADR-012** — Shared component library: All widget changes live in `@spaarke/daily-briefing-components` (R2 deliverable). Package boundary unchanged. New action functions added to existing exports.
- **ADR-021** — Fluent v9 design system: All 3 new UI buttons MUST use Fluent v9 tokens and icons (`CheckmarkRegular`, `DismissRegular`, `CalendarAddRegular`). Dark mode required.
- **ADR-024** — `sprk_todo` regarding catalog: Existing `useInlineTodoCreate` MUST remain functional. The 4th existing per-item button ("Add to To Do") is unchanged. Manual UAT verifies regression-free.
- **ADR-027** — Subscription isolation: `appnotification` is a CORE Dataverse entity. Adding `sprk_briefingstate` is a CORE schema additive change. Permitted; flag in deployment notes so solution-import order is correct.

**From Spec**:
- Producer layer is verified healthy (`CreateNotificationNodeExecutor.cs` already correct); explicitly preserved
- Native bell-panel lifecycle MUST be preserved unchanged (FR-7 invariant)
- BFF publish-size delta ≤ +0.1 MB (NFR-02)
- No new HIGH-severity CVE (NFR-01)
- Backward compatible with existing rows (null `sprk_briefingstate` → Unread; no backfill)
- Per CLAUDE.md §10 BFF Hygiene: Placement Justification stated (1-line bug fix in existing service); publish-size + CVE verification mandatory on BFF-touching task

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| New custom Choice column `sprk_briefingstate`, NOT repurposing OOB `statecode`/`statuscode` | `appnotification` lacks those columns (owner-verified) | 1 new column on a CORE entity; operator-driven deploy via solution |
| Briefing read-state decoupled from bell-panel `isread`/`toasttype` | Owner: bell = real-time tray; briefing = daily report; different lifecycles by design | Widget never reads/writes `isread` or `toasttype` for state; bell unaffected by widget actions |
| Fixed +7 days for "Keep on briefing" (no date picker, no weekend logic) | Simpler UX; future due-date engine will own complex date math | `extendBriefingTtl` adds literal 604800 seconds; no `DayOfWeek` checks |
| 3 widget actions go direct to Dataverse via `Xrm.WebApi`, not via BFF | Minimal scope; no new endpoint per §10 BFF Hygiene | No BFF changes for the widget side |
| `sprk_briefingstate = null` on read = Unread (null-coalesce in widget) | Backward compatibility for pre-rollout existing rows | No backfill needed; widget handles existing data |
| Server-side filter `(sprk_briefingstate ne 2 or sprk_briefingstate eq null)` excludes Removed | Reduces network payload; cleaner client logic | One filter clause appended to `fetchNotifications` |

### Discovered Resources

**Applicable Skills** (auto-discovered for task-execute Step 0):
- [`.claude/skills/task-execute/`](../../.claude/skills/task-execute/SKILL.md) — load knowledge files + quality gates at Step 9.5
- [`.claude/skills/code-review/`](../../.claude/skills/code-review/SKILL.md) — required quality gate (FULL rigor)
- [`.claude/skills/adr-check/`](../../.claude/skills/adr-check/SKILL.md) — required quality gate (FULL rigor)
- [`.claude/skills/fluent-v9-component/`](../../.claude/skills/fluent-v9-component/SKILL.md) — invoke for the 3 new UI buttons
- [`.claude/skills/dataverse-create-schema/`](../../.claude/skills/dataverse-create-schema/SKILL.md) — for `sprk_briefingstate` Choice column creation
- [`.claude/skills/bff-deploy/`](../../.claude/skills/bff-deploy/SKILL.md) — deploy BFF NotificationService.cs fix
- [`.claude/skills/code-page-deploy/`](../../.claude/skills/code-page-deploy/SKILL.md) — redeploy standalone Daily Briefing code page after widget changes
- [`.claude/skills/push-to-github/`](../../.claude/skills/push-to-github/SKILL.md) — commit per Spaarke git conventions
- [`.claude/skills/merge-to-master/`](../../.claude/skills/merge-to-master/SKILL.md) — final merge with safety checks

**Binding Constraints**:
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — Sections A (MUST checklist), F (test update obligation) — applies to NotificationService.cs change
- [`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md) — Griffel, semantic tokens, `mergeClasses` for 3 new buttons
- [`.claude/patterns/ui/fluent-v9-theming.md`](../../.claude/patterns/ui/fluent-v9-theming.md) — FluentProvider, dark mode
- [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](../../docs/standards/DATA-ACCESS-DECISION-CRITERIA.md) — `Xrm.WebApi` vs BFF — widget uses host-context `Xrm.WebApi.updateRecord` for direct Dataverse writes

**Reusable Code**:
- [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs:488-490`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs#L488) — canonical `ttlinseconds` write pattern (`NotificationService.cs` aligns to this)
- [`src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts:270-276`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts#L270) — existing `markNotificationRead` is the single-record update pattern; new actions follow the same `tryCatch + webApi.updateRecord` shape
- [`src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useBriefingActions.ts`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useBriefingActions.ts) — existing actions hook; new functions added alongside; exported via same barrel
- [`src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx:238-287`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx#L238) — existing `handleAddToTodo` optimistic-update + toast wiring pattern; new handlers mirror this shape

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Dataverse Schema (operator)
└─ Add sprk_briefingstate Choice column to appnotification
└─ Deploy to spaarkedev1

Phase 2: BFF Producer Fix
└─ NotificationService.cs ttlindays → ttlinseconds + unit test

Phase 3: Widget Service Layer
└─ types/notifications.ts + notificationService.ts: read-state swap + 3 new actions + filter + jest tests

Phase 4a: Widget Hook Layer
└─ useBriefingActions.ts: 3 new handlers + jest tests

Phase 4b: Widget UI Wiring
└─ NarrativeBullet.tsx + ActivityNotesSection.tsx + DailyBriefingApp.tsx: 3 buttons + props wiring + optimistic UI + toast

Phase 5: UAT + Wrap-up
└─ Manual UAT verifying 7 ACs in spaarkedev1
└─ Project wrap-up (lessons-learned + status update)
```

### Critical Path

**Blocking Dependencies:**
- Task 020 (widget service) BLOCKED BY 001 deployment for runtime UAT (jest tests can run before deploy)
- Task 030 (hook) BLOCKED BY 020 (needs new service-layer functions to wire)
- Task 031 (UI wiring) BLOCKED BY 030 (needs new hook handlers to bind to buttons)
- Task 040 (UAT) BLOCKED BY 001 + 010 + 020 + 030 + 031 (everything must be merged + deployed)
- Task 090 (wrap-up) BLOCKED BY 040

**Parallel Opportunities** (Wave 1 — 3 concurrent agents):
- 001 (schema deploy, operator) ∥ 010 (BFF fix) ∥ 020 (widget service layer code — jest tests don't need schema deployed)

**Critical Path Length** (sequential): 001 → 020 → 030 → 031 → 040 → 090 (6 tasks)
**Critical Path Length** (with parallel): max(001, 010, 020) → 030 → 031 → 040 → 090 (~5 wall-clock waves)

**High-Risk Items:**
- Dataverse rejects post-create `ttlinseconds` mutation → Mitigation: verified writable per Microsoft Learn; fallback design documented in spec Risk #1
- Stale `toasttype: 200000000` literals in widget tests → Mitigation: explicit sweep in task 020 test-update step

---

## 4. Phase Breakdown

### Phase 1: Dataverse Schema (~30 min, operator-driven)

**Objectives:**
1. Create custom `sprk_briefingstate` Choice column on `appnotification` (CORE entity)
2. Deploy to spaarkedev1 via solution

**Deliverables:**
- [ ] Choice column `sprk_briefingstate` with values: `Unread = 0` (default), `Checked = 1`, `Removed = 2`
- [ ] Default value `0` propagates on row create at Dataverse-schema level
- [ ] Solution exported + imported to spaarkedev1
- [ ] OData `$select=sprk_briefingstate` returns the column without 400

**Critical Tasks:**
- 001: Add + deploy `sprk_briefingstate` Choice column (BLOCKS UAT in task 040; non-blocking for code work in tasks 010/020)

**Inputs**: `spec.md` FR-1, `appnotification` OOB entity, spaarkedev1 environment access
**Outputs**: Custom column deployed; solution artifact retained for env promotion

---

### Phase 2: BFF Producer Fix (~30 min)

**Objectives:**
1. Replace `entity["ttlindays"] = 7` with `entity["ttlinseconds"] = 604800` in `NotificationService.CreateNotificationAsync`
2. Update or add the unit test that asserts the payload key
3. Per CLAUDE.md §10 BFF Hygiene: verify publish-size delta ≤ +0.1 MB; verify no new HIGH CVE

**Deliverables:**
- [ ] [`NotificationService.cs:106`](../../src/server/api/Sprk.Bff.Api/Services/NotificationService.cs#L106) writes `ttlinseconds = 604800`
- [ ] Corresponding xUnit test asserts the payload key + value
- [ ] BFF publish-size measured and reported in task notes (baseline ~45.65 MB; expect ≤ +0.01 MB delta)
- [ ] `dotnet list package --vulnerable --include-transitive` reports no new HIGH

**Critical Tasks:**
- 010: BFF NotificationService.cs ttlinseconds fix + test + size/CVE verification

**Inputs**: `spec.md` FR-2 AC-2; existing `CreateNotificationNodeExecutor.cs:488-490` as canonical reference; `.claude/constraints/bff-extensions.md`
**Outputs**: 1-line code change + 1 test update + BFF size/CVE record in task notes

---

### Phase 3: Widget Service Layer (~2 hours)

**Objectives:**
1. Add `sprk_briefingstate` to widget entity type + `NOTIFICATION_SELECT`
2. Switch `toNotificationItem` read-state derivation from `toasttype` to `sprk_briefingstate` (with null-coalesce → Unread)
3. Add server-side filter `(sprk_briefingstate ne 2 or sprk_briefingstate eq null)` to `fetchNotifications`
4. Replace `markNotificationRead` body to write `{ sprk_briefingstate: 1 }`
5. Replace `markAllNotificationsRead` body similarly
6. Add `markBriefingRemoved(id)` writing `{ sprk_briefingstate: 2 }`
7. Add `extendBriefingTtl(id, currentTtlSeconds)` writing `{ ttlinseconds: currentTtl + 604800 }`
8. Sweep + update existing jest tests for stale `toasttype: 200000000` literals
9. Add jest tests for the 3 new action functions

**Deliverables:**
- [ ] `notifications.ts` type extended with `sprk_briefingstate?: number`
- [ ] `notificationService.ts` read-state derivation switched (FR-3 AC-3a/b/c)
- [ ] 3 new exported action functions
- [ ] All existing jest tests updated; 3 new action-function tests added
- [ ] `npm test` in `@spaarke/daily-briefing-components` passes

**Critical Tasks:**
- 020: Widget service layer (read-state swap + 3 new action functions + filter + jest test updates)

**Inputs**: `spec.md` FR-3, FR-4, FR-5, FR-6; existing `notificationService.ts` patterns; `useToastController` pattern
**Outputs**: Updated service file + types + tests

---

### Phase 4a: Widget Hook Layer (~1 hour)

**Objectives:**
1. Extend `useBriefingActions` with 3 new handler functions: `markChecked(id)`, `markRemoved(id)`, `extendTtl(id, currentTtl)`
2. Each handler implements optimistic UI update → service call → success/error toast
3. Add jest tests for each new handler

**Deliverables:**
- [ ] `useBriefingActions.ts` exports 3 new handlers alongside existing ones (no public-contract break)
- [ ] Each handler follows the same shape as existing `handleAddToTodo` (optimistic + service call + toast)
- [ ] `useBriefingActions.test.ts` covers the 3 new handlers (success path + error path)
- [ ] Jest tests pass

**Critical Tasks:**
- 030: useBriefingActions hook extension + tests (BLOCKED BY 020)

**Inputs**: `spec.md` FR-4, FR-5, FR-6; existing `useBriefingActions` shape; service functions from task 020
**Outputs**: Updated hook + tests

---

### Phase 4b: Widget UI Wiring (~2 hours)

**Objectives:**
1. Add 3 new per-item action buttons to `NarrativeBullet.tsx` using Fluent v9 icons:
   - `CheckmarkRegular` (tooltip: "Mark as read") → calls `markChecked(id)`
   - `DismissRegular` (tooltip: "Remove from briefing") → calls `markRemoved(id)`
   - `CalendarAddRegular` (tooltip: "Keep on briefing for 7 more days") → calls `extendTtl(id, currentTtl)`
2. Wire new button props through `ActivityNotesSection.tsx`
3. Compose handlers in `DailyBriefingApp.tsx` (mirror existing `handleAddToTodo` pattern)
4. Verify dark-mode rendering (ADR-021)
5. Verify existing "Add to To Do" button regression-free (ADR-024)
6. Update smoke test fixtures for `sprk_briefingstate` field

**Deliverables:**
- [ ] 3 new action buttons rendered on each item, in order: Check, Remove, Keep, (existing) Add to To Do
- [ ] Optimistic UI update on click; toast confirms success / surfaces failure
- [ ] Dark-mode + light-mode both render correctly (Fluent v9 tokens)
- [ ] `DailyBriefingApp.smoke.test.tsx` fixtures updated for `sprk_briefingstate`

**Critical Tasks:**
- 031: NarrativeBullet + ActivityNotesSection + DailyBriefingApp wiring (BLOCKED BY 030)

**Inputs**: `spec.md` FR-4, FR-5, FR-6, FR-7; `.claude/patterns/ui/fluent-v9-component-authoring.md`; hook handlers from task 030
**Outputs**: Updated UI components + smoke test

---

### Phase 5: UAT + Wrap-up (~30 min UAT + 1 hour wrap-up)

**Objectives:**
1. Verify in spaarkedev1: widget renders unread notifications for a current user (AC-3a)
2. Verify each of 3 new actions works end-to-end (AC-4, AC-5, AC-6)
3. Verify bell-panel decoupling: dismiss in bell ≠ remove from widget; check in widget ≠ remove from bell (AC-7a/b)
4. Verify backward compatibility: existing notifications without `sprk_briefingstate` render as Unread (AC-3c)
5. Author `notes/lessons-learned.md`
6. Update README status to Complete

**Deliverables:**
- [ ] UAT findings recorded in `notes/uat-2026-06-2X.md`
- [ ] All 7 ACs marked pass/fail with evidence
- [ ] `notes/lessons-learned.md` captured
- [ ] README status: Complete

**Critical Tasks:**
- 040: Manual UAT in spaarkedev1 (BLOCKED BY 001, 010, 020, 030, 031)
- 090: Project wrap-up (BLOCKED BY 040)

**Inputs**: spaarkedev1 environment; merged code; deployed schema
**Outputs**: UAT record; lessons-learned; status update

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Microsoft `appnotification` OOB entity | GA | Low | Standard custom-column extension; supported in all envs |
| Microsoft `Xrm.WebApi.updateRecord` for host-context Dataverse writes | GA | Low | Existing widget already uses this pattern |
| spaarkedev1 environment | Ready | Low | Operator access confirmed |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| R2 Pattern D widget package `@spaarke/daily-briefing-components` | `src/client/shared/Spaarke.DailyBriefing.Components/` | Production (R2 shipped) |
| R3 platform-foundations recipient resolution (producer side) | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs` | Production |
| `useToastController` pattern in widget | `@spaarke/daily-briefing-components` | Production |
| `useInlineTodoCreate` ADR-024 multi-entity resolver | `@spaarke/daily-briefing-components` | Production (preserve unchanged) |

---

## 6. Testing Strategy

**Unit Tests** (90% line coverage target on changed files per NFR-03):
- BFF: xUnit test asserts `NotificationService.CreateNotificationAsync` writes `ttlinseconds = 604800` (FR-2)
- Widget: jest test asserts `toNotificationItem` derives `isRead` from `sprk_briefingstate` not `toasttype` (FR-3)
- Widget: jest test asserts `fetchNotifications` filter excludes Removed items (FR-3 AC-3b)
- Widget: jest tests assert null `sprk_briefingstate` → Unread (FR-3 AC-3c)
- Widget: 3 new service function tests (markBriefingRead → field write; markBriefingRemoved → field write; extendBriefingTtl → field write)
- Widget: 3 new hook handler tests (success path + error path each)
- Smoke test: `DailyBriefingApp.smoke.test.tsx` fixtures cover `sprk_briefingstate` field

**Integration Tests**:
- None new — direct Dataverse writes are covered by manual UAT

**E2E / Manual UAT** (spaarkedev1):
- AC-3a: Widget populates with unread notifications for current user
- AC-3b: Notifications with `sprk_briefingstate = 2` don't appear
- AC-3c: Pre-rollout existing rows (null field) render as Unread
- AC-4: Check action → DV row `sprk_briefingstate = 1`; widget renders as read
- AC-5: Remove action → DV row `sprk_briefingstate = 2`; widget doesn't re-render on refresh
- AC-6: Keep action → DV row `ttlinseconds` increased by 604800; toast shows new expiry
- AC-7a: Dismiss in bell ≠ remove from widget
- AC-7b: Check in widget ≠ remove from bell

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] `sprk_briefingstate` Choice column exists on `appnotification` in spaarkedev1
- [ ] OData `$select=sprk_briefingstate` returns 200
- [ ] New rows show value `0` without explicit producer write (Dataverse-default propagation verified)

**Phase 2:**
- [ ] BFF unit test asserts payload key is `ttlinseconds` (not `ttlindays`)
- [ ] BFF publish-size delta ≤ +0.1 MB (compressed)
- [ ] No new HIGH-severity CVE

**Phase 3:**
- [ ] All jest tests pass (existing updated + 3 new)
- [ ] Read-state derivation switched to `sprk_briefingstate`
- [ ] Removed-state filter applied in `fetchNotifications`
- [ ] 3 new action functions exported from `notificationService.ts`

**Phase 4a:**
- [ ] `useBriefingActions` exports 3 new handlers (public-contract back-compat preserved)
- [ ] Jest tests pass for 3 new handlers (success + error paths)

**Phase 4b:**
- [ ] 3 new action buttons rendered on each item; existing "Add to To Do" preserved
- [ ] Optimistic UI update on click; toast confirms outcome
- [ ] Dark mode + light mode both render correctly
- [ ] Smoke test fixtures updated for `sprk_briefingstate`

**Phase 5:**
- [ ] All 7 ACs pass in manual UAT
- [ ] Lessons-learned authored
- [ ] PR merged to master; deployed to dev

### Business Acceptance

- [ ] Daily Briefing widget no longer renders EmptyState when unread notifications exist for the user
- [ ] Users can Check / Remove / Keep items in the widget without affecting their bell-panel lifecycle
- [ ] No regression in existing "Add to To Do" functionality

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Dataverse rejects post-create `ttlinseconds` mutation | Low | Med | Verified writable per Microsoft Learn. Fallback: separate `sprk_briefingttlextended` DateTime field with `effectiveExpiry = max(createdon + ttlinseconds, sprk_briefingttlextended)` (documented in spec). |
| R2 | Existing notifications without `sprk_briefingstate` value | Low | Low | Widget null-coalesce treats null as Unread (FR-3 AC-3c); no backfill required. |
| R3 | Stale `toasttype: 200000000` assertions in existing widget tests | Med | Low | Task 020 includes explicit sweep step; replace with `sprk_briefingstate: 1` literals. |
| R4 | Native bell-panel state confusion ("dismissed in bell, still in briefing") | Med | Low | Brief UX caption ("Your daily summary — independent of system notifications") — defer if scope creep; flag for UAT. |
| R5 | Dataverse maker portal doesn't support per-column default value for Choice columns on Microsoft-owned tables | Low | Low | Fallback: leave default unset; rely entirely on widget null-coalesce (FR-3 AC-3c covers this anyway). |
| R6 | BFF publish-size delta unexpectedly exceeds +0.1 MB | Low | Low | 1-line code change; very high confidence delta is ≤+0.01 MB. Verify per §10 NFR-01 rule. |

---

## 9. Next Steps

1. **Review this plan.md** for completeness + accuracy
2. **Run task generation** (Step 3 of `/project-pipeline`) to create 7 POML task files
3. **Begin Phase 1** — task 001 (schema deploy via operator) can run in parallel with task 010 (BFF fix) and task 020 (widget service code)
4. **Coordinate Wave 1 (parallel)**: 001, 010, 020 — 3 concurrent agents possible

---

**Status**: Ready for Tasks
**Next Action**: Tasks 001–090 generated; review + execute via `task-execute`

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. Wave 1 parallel execution is the recommended path.*
