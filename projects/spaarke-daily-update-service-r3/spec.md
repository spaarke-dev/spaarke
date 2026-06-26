# Spaarke Daily Update Service (R3) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-24
> **Source**: `design.md` (UAT-driven design session, 2026-06-24)
> **Predecessor**: [`projects/spaarke-daily-update-service-r2/`](../spaarke-daily-update-service-r2/) (R2 — Pattern D widget migration + producer-fix Phases A/B)

---

## Executive Summary

Daily Briefing widget renders the "all caught up" empty state even when the user has unread `appnotification` records, because the widget's read-state check (`toasttype === 200000000` ≡ Dismissed) mis-interprets the producer's display-behavior default (`toasttype = 200000000` ≡ Microsoft's "Timed" toast type). Every notification arrives pre-marked-read in the widget. R3 introduces a Daily-Briefing-scoped option-set field (`sprk_briefingstate`) to decouple briefing read-state from the native bell panel, adds per-item user actions (Check, Remove, Keep +7d), and fixes a parallel producer-side defect where `NotificationService.cs` writes a non-existent TTL field name (`ttlindays`) that silently falls back to the tenant default.

R3 is **consumer-layer + minor producer-fix** work only. ~6 hours engineering + ~30 min operator schema add.

---

## Scope

### In Scope

- Add `sprk_briefingstate` custom Choice column on `appnotification` (operator-driven via solution import to spaarkedev1, then higher envs)
- Fix [`NotificationService.cs:106`](../../src/server/api/Sprk.Bff.Api/Services/NotificationService.cs#L106) `ttlindays` → `ttlinseconds`
- Switch widget read-state field from `toasttype` to `sprk_briefingstate`
- Add 3 new per-item action functions to widget service: `markBriefingChecked`, `markBriefingRemoved`, `extendBriefingTtl`
- Add 3 new per-item UI action buttons (Check, Remove, Keep +7d) using Fluent v9 icons
- Unit-test coverage for all new behavior + update existing tests for read-marker field swap
- Manual UAT in spaarkedev1 verifying the 3 actions + bell-panel decoupling

### Out of Scope

- **Weekend-aware TTL calculation** — deferred. Future due-date engine will own date math (handles weekends, holidays, time zones)
- **Widget-side matter-scope filtering** — unnecessary. Producer-side membership resolution (R3 platform-foundations) already determines recipients; a second filter would be redundant or introduce divergence bugs
- **Backfill of existing notifications** — not needed. Null `sprk_briefingstate` treated as `Unread` per FR-3 AC-3c
- **Changes to the native bell panel** — different lifecycle by design
- **TTL admin overrides per category / per-user preferences** — defer to a future enhancement
- **Producer-side category-specific TTL tuning** — current 7-day default works post-c55c7a5fe

### Explicitly NOT Changing

- `appnotification.ownerid` resolution (R3 platform-foundations membership service)
- `CreateNotificationNodeExecutor` ActionType 50 / playbook engine
- BFF `/narrate` endpoint contract
- Pattern D dual-use architecture (R2 deliverable)
- `@spaarke/daily-briefing-components` package boundary
- Standalone Daily Briefing code page (`sprk_dailyupdate`)
- ADR-024 `sprk_todo` regarding catalog / `useInlineTodoCreate`

### Affected Areas

| Path | Purpose |
|---|---|
| Dataverse `appnotification` table | NEW: `sprk_briefingstate` Choice column (Unread=0, Checked=1, Removed=2) |
| [`src/server/api/Sprk.Bff.Api/Services/NotificationService.cs`](../../src/server/api/Sprk.Bff.Api/Services/NotificationService.cs) | Fix `ttlindays` → `ttlinseconds` (line 106) |
| [`src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts) | Switch read-state field; add 3 action functions |
| [`src/client/shared/Spaarke.DailyBriefing.Components/src/types/notifications.ts`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/types/notifications.ts) | Add `sprk_briefingstate` to entity type + actions |
| [`src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useBriefingActions.ts`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useBriefingActions.ts) | Extend with 3 new handler functions |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/components/NarrativeBullet.tsx` | Add 3 new action buttons + handlers |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/components/ActivityNotesSection.tsx` | Wire button props through |
| [`src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx) | Hook composition for new handlers |
| `tests/unit/Sprk.Bff.Api.Tests/Services/NotificationServiceTests.cs` (or equivalent) | Verify `ttlinseconds` field write |
| `src/client/shared/Spaarke.DailyBriefing.Components/test/notificationService.test.ts` | Update field-name assertions; add 3 new function tests |
| `src/client/shared/Spaarke.DailyBriefing.Components/test/useBriefingActions.test.ts` | Add 3 new handler tests |
| `src/client/shared/Spaarke.DailyBriefing.Components/test/DailyBriefingApp.smoke.test.tsx` | Update fixtures for `sprk_briefingstate` field |

---

## Requirements

### Functional Requirements

**FR-1 — Add `sprk_briefingstate` schema field**
- Custom Choice column on `appnotification`
- Values: `Unread = 0` (default), `Checked = 1`, `Removed = 2`
- Default value `0` applies on row create at Dataverse level (producer does not write field explicitly)
- Deployed to spaarkedev1 via solution; higher envs via solution promotion
- **AC-1**: Querying `appnotification?$select=sprk_briefingstate` returns the column without error; newly created rows surface `sprk_briefingstate = 0` without explicit producer write

**FR-2 — Fix producer-side TTL field name (D2)**
- `NotificationService.CreateNotificationAsync` writes `entity["ttlinseconds"] = 604800` instead of `entity["ttlindays"] = 7`
- No other producer changes
- **AC-2**: Creating a notification via `NotificationService` produces an `appnotification` row with `ttlinseconds = 604800`; existing rows are unaffected; unit test asserts the write payload key is `ttlinseconds`

**FR-3 — Widget read-state switches off `toasttype`**
- `fetchNotifications` selects `sprk_briefingstate` (added to `NOTIFICATION_SELECT`)
- `toNotificationItem` derives `isRead` from `(entity['sprk_briefingstate'] as number ?? 0) === 1`
- `fetchNotifications` includes server-side filter excluding Removed: `(sprk_briefingstate ne 2 or sprk_briefingstate eq null)` (when no other filter applies, becomes the sole condition; when combined with `unreadOnly`, joins with AND)
- **AC-3a**: Widget renders unread items as unread regardless of `toasttype` value (verify with manual UAT in spaarkedev1 + jest fixture)
- **AC-3b**: Notifications with `sprk_briefingstate = 2` (Removed) do not appear in the widget
- **AC-3c**: Notifications with null `sprk_briefingstate` (pre-rollout existing rows) render as Unread

**FR-4 — "Check off" action**
- New per-item button using `CheckmarkRegular` icon from `@fluentui/react-icons`
- Tooltip: "Mark as read"
- Behavior: optimistic UI update → `webApi.updateRecord('appnotification', id, { sprk_briefingstate: 1 })` → success toast on resolve or error toast on reject
- Implemented in `useBriefingActions` as `markChecked(id)`
- **AC-4**: After click, the notification's `sprk_briefingstate = 1` in Dataverse, widget renders it as read, native bell-panel state is unaffected

**FR-5 — "Remove from briefing" action**
- New per-item button using `DismissRegular` icon
- Tooltip: "Remove from briefing"
- Behavior: optimistic UI update → `webApi.updateRecord('appnotification', id, { sprk_briefingstate: 2 })` → success/error toast
- Implemented in `useBriefingActions` as `markRemoved(id)`
- **AC-5**: After click, the notification's `sprk_briefingstate = 2` in Dataverse, item does not re-appear in widget on subsequent fetch, underlying record and native bell-panel state are unaffected

**FR-6 — "Keep 7 more days" action**
- New per-item button using `CalendarAddRegular` icon
- Tooltip: "Keep on briefing for 7 more days"
- Behavior: optimistic UI update → read current `ttlinseconds` from item → compute `newTtl = currentTtl + 604800` → `webApi.updateRecord('appnotification', id, { ttlinseconds: newTtl })` → success toast displaying new effective expiry date / error toast on failure
- Implemented in `useBriefingActions` as `extendTtl(id, currentTtlSeconds)` with hardcoded `addSeconds = 604800`
- **AC-6**: After click, the notification's `ttlinseconds` is increased by 604800, the new effective expiry date is correctly reflected in any displayed metadata, and the row persists 7 more calendar days before Dataverse-platform TTL purge

**FR-7 — Bell-panel decoupling verified**
- Widget code does not read `toasttype` or `isread` for state derivation
- Widget code does not write `toasttype` or `isread` for state mutation
- **AC-7a**: User dismisses notification in native bell panel → item continues to appear in Daily Briefing widget on next fetch
- **AC-7b**: User checks off notification in Daily Briefing widget → item continues to appear in native bell panel until separately dismissed there

### Non-Functional Requirements

- **NFR-01**: No new HIGH-severity CVE introduced (verify via `dotnet list package --vulnerable --include-transitive` + `npm audit --production`)
- **NFR-02**: BFF publish-size delta ≤ +0.1 MB (no NuGet adds; 1-line code change) — verify per CLAUDE.md §10 ceiling rule
- **NFR-03**: Unit + integration tests cover all 7 FRs; widget jest tests use `jest-environment-jsdom`; minimum 90% line coverage on changed files
- **NFR-04**: Widget action latency — optimistic UI update ≤16ms (single React render); backend `Xrm.WebApi.updateRecord` write ≤300ms p95
- **NFR-05**: Backward compatible with existing `appnotification` rows (null `sprk_briefingstate` treated as `Unread` per FR-3 AC-3c; no data backfill required)

---

## Technical Constraints

### Applicable ADRs

- **ADR-001** — BFF Minimal API: `NotificationService.cs` change is a 1-line bug fix in an existing service. No new endpoints. Constraint trivially satisfied.
- **ADR-012** — Shared component library: All widget changes live in `@spaarke/daily-briefing-components` (R2 deliverable). No package boundary changes. New action functions added to existing barrel exports.
- **ADR-021** — Fluent v9 design system: All 3 new UI buttons MUST use Fluent v9 tokens (`tokens.colorBrandBackground` etc.) and Fluent v9 icons (`@fluentui/react-icons/CheckmarkRegular`, `/DismissRegular`, `/CalendarAddRegular`). Dark mode required.
- **ADR-024** — `sprk_todo` regarding catalog: Existing `useInlineTodoCreate` MUST remain functional. The 4th existing per-item button ("Add to To Do") is unchanged. Manual UAT verifies regression-free.
- **ADR-027** — Subscription isolation: `appnotification` is a CORE Dataverse entity. Adding `sprk_briefingstate` is a CORE schema additive change. Permitted; flag in deployment notes so solution-import order is correct.

### MUST Rules

- ✅ MUST add `sprk_briefingstate` as Choice (option-set) NOT Boolean — three discrete states (Unread / Checked / Removed) are not representable as a single Boolean
- ✅ MUST default `sprk_briefingstate` to `0` (Unread) at the Dataverse-schema level, NOT in producer code — keeps producers oblivious to briefing-specific state
- ✅ MUST treat null `sprk_briefingstate` on read as `Unread` (null-coalesce in the widget) — supports backward compatibility with existing rows
- ✅ MUST use `CalendarAddRegular` from `@fluentui/react-icons` for the Keep button (owner-specified)
- ✅ MUST use literal +7 calendar days for the Keep action; computation: `newTtl = currentTtl + 604800` — owner-specified, no weekend logic
- ✅ MUST issue server-side filter `(sprk_briefingstate ne 2 or sprk_briefingstate eq null)` in widget queries to exclude Removed items without filtering nulls
- ✅ MUST perform optimistic UI update on all 3 new actions (per UX consistency with existing "Add to To Do" path)
- ✅ MUST show success or error toast via existing `useToastController` pattern on each action's resolution
- ❌ MUST NOT change the producer's `toasttype = 200000000` write — that value is canonically correct ("Timed") per Microsoft Learn
- ❌ MUST NOT add weekend-aware TTL calculation (owner: future due-date engine will own this)
- ❌ MUST NOT add a widget-side BFF call to filter by user's matter associations (owner: trust producer-side recipient resolution)
- ❌ MUST NOT backfill `sprk_briefingstate` on existing rows (FR-3 AC-3c handles via null-coalesce)
- ❌ MUST NOT write `isread` or `toasttype` from the widget for state mutation (FR-7 invariant)
- ❌ MUST NOT introduce new BFF endpoints — all widget actions go directly to Dataverse via `Xrm.WebApi.updateRecord`

### Existing Patterns to Follow

- [`src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts:270-276`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts#L270) — `markNotificationRead` is the existing single-record update pattern; new actions follow the same `tryCatch + webApi.updateRecord` shape
- [`src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useBriefingActions.ts`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useBriefingActions.ts) — existing actions hook; new functions added alongside existing ones, exported via same barrel
- [`src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx:238-287`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx#L238) — existing `handleAddToTodo` with optimistic update + toast wiring; new handlers mirror this pattern
- [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs:488-490`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs#L488) — canonical `ttlinseconds` write pattern (already correct); `NotificationService.cs` aligns to this
- `.claude/patterns/ui/fluent-v9-component-authoring.md` — Fluent v9 button + icon conventions
- `.claude/patterns/ui/fluent-v9-theming.md` — Token + dark-mode requirements

### Cross-cutting Constraints (CLAUDE.md)

- **§10 BFF Hygiene — Placement Justification**: `NotificationService.cs` change is a 1-line bug fix in an existing service inside `Sprk.Bff.Api`. No new components, interfaces, or DI registrations. Constraint satisfied; PR description need only note this minimal scope.
- **§10 BFF Hygiene — Publish-size verification**: Trivial change; expected delta ≤ +0.01 MB. Verify per the per-task rule.
- **§11 Component Justification**: No new services / abstractions / interfaces / endpoints / DI registrations / packages / Dataverse columns (other than `sprk_briefingstate` justified in design.md FR-1 rationale) introduced. The 1 new Dataverse column has explicit cost-of-doing-nothing: without it, every notification appears pre-read in the widget (the bug). The 3 new action functions extend an existing service file. Constraint satisfied.

---

## Success Criteria

1. [ ] **Schema deployed** — `sprk_briefingstate` Choice column exists on `appnotification` in spaarkedev1 — *Verify by:* Power Apps maker portal inspection + OData `$select=sprk_briefingstate` returns 200
2. [ ] **Producer TTL fix verified** — `NotificationService` creates notifications with `ttlinseconds = 604800` — *Verify by:* unit test asserting payload key + manual Dataverse query post-create
3. [ ] **Widget renders unread notifications** — Daily Briefing widget surfaces unread items for a test user in spaarkedev1 — *Verify by:* manual UAT against a user with ≥1 unread `appnotification` row
4. [ ] **Check action works end-to-end** — Click "Mark as read" → widget moves item to read → Dataverse `sprk_briefingstate = 1` — *Verify by:* manual UAT + jest hook test
5. [ ] **Remove action works end-to-end** — Click "Remove from briefing" → item disappears from widget → Dataverse `sprk_briefingstate = 2` — *Verify by:* manual UAT + jest hook test
6. [ ] **Keep action extends TTL** — Click "Keep 7 more days" → toast shows new expiry → Dataverse `ttlinseconds` increased by 604800 — *Verify by:* manual UAT + jest hook test + Dataverse query showing new value
7. [ ] **Bell-panel decoupling verified** — Dismiss in bell panel does NOT remove from widget; check off in widget does NOT remove from bell panel — *Verify by:* manual UAT scenario AC-7a + AC-7b
8. [ ] **All unit tests pass** — BFF unit test updated; widget jest tests updated; new tests added per FR-4/5/6 — *Verify by:* `dotnet test` + `npm test` in the widget package
9. [ ] **No new HIGH-severity CVE** — *Verify by:* `dotnet list package --vulnerable --include-transitive` + `npm audit --production`
10. [ ] **BFF publish-size within budget** — Delta ≤ +0.1 MB — *Verify by:* `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` measurement vs baseline

---

## Dependencies

### Prerequisites

- spaarkedev1 environment access (operator for schema deployment)
- `sprk_briefingstate` Choice column must be deployed to spaarkedev1 BEFORE widget code changes can be UAT-tested (deployment can happen in parallel with code work; UAT blocks on it)
- R2 Pattern D widget migration (already shipped — R3 builds on its consumer layer)
- `@spaarke/daily-briefing-components` package (already exists — R2 deliverable)

### External Dependencies

- None — this work is internal to Spaarke; no third-party APIs, services, or approvals needed
- Microsoft `appnotification` entity is OOB Dataverse; adding `sprk_briefingstate` is a standard custom-column extension supported by all environments

---

## Owner Clarifications

*Captured from UAT-driven design session, 2026-06-24. All 7 questions resolved.*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Read-marker field | Use native `statecode`/`statuscode`? | No — `appnotification` lacks those columns. Use a custom field. | Custom `sprk_briefingstate` introduced; ADR-027 CORE schema change accepted |
| Surface independence | Should briefing read-state mirror bell-panel read-state? | No — different lifecycles by design | Widget never reads/writes `isread` (FR-7 invariant); 1 new custom field instead of repurposing OOB |
| Extension UX | Date picker or fixed +N days for "Keep on briefing"? | Fixed +7 days, one-click, `CalendarAddRegular` icon | FR-6 implementation simplicity; no calendar widget |
| Weekend logic | Should "Keep" be weekend-aware (push expiry to Monday if it lands Sat/Sun)? | No — future due-date engine will own date math | Implementation: literal `+604800` seconds; no `DayOfWeek` checks |
| Matter scoping | Should widget filter to user's matter associations via R3 membership service? | No — producer side already does this; trust it | No new BFF call; FR-3 filter is only `sprk_briefingstate ne 2 or null` |
| Action coupling | Does "Add to To Do" also mark briefing as Checked? | Deferred — implement as independent action; revisit if UAT requests | 4 buttons in row stay independent; no auto-Check coupling |
| TTL update mechanism | Does Dataverse honor post-create `ttlinseconds` update? | Yes per Microsoft Learn — field is writable; extends total lifespan from `createdon` | FR-6 implementation uses `updateRecord` directly; fallback design (separate `sprk_briefingttlextended` field) not needed |

---

## Assumptions

*None requiring carry-forward.*

The design session resolved all gaps. Two assumptions worth noting for the implementer:

- **Dataverse default value behavior**: Assuming the `sprk_briefingstate` Choice column accepts a `Default Value = Unread (0)` setting via Power Apps maker portal that propagates to row create without producer-side writes. If maker portal doesn't support per-column default for Choice columns on Microsoft-owned tables, fallback is to leave default unset and rely entirely on the widget's null-coalesce read (FR-3 AC-3c covers this anyway). Confirm during FR-1 deployment.
- **`useBriefingActions` extension pattern**: Assuming the hook can be extended with 3 new functions without breaking its existing public contract (the hook is consumed by `DailyBriefingApp.tsx`). The hook's existing pattern returns an object with `markAsRead`, `refresh`; adding `markChecked`, `markRemoved`, `extendTtl` follows the same shape.

---

## Unresolved Questions

*None blocking implementation.*

The following are operational nice-to-haves that can be answered during build or deferred to UAT feedback without blocking task decomposition:

- [ ] **Solution layering** — should `sprk_briefingstate` go in the existing Spaarke core solution or a new "Daily Briefing Extensions" solution? Default: existing Spaarke core (matches pattern for prior custom columns on OOB tables). *Blocks*: nothing (operator can decide at FR-1 deployment time).
- [ ] **Toast copy** — exact wording for the 3 success/error toasts. Default: "Marked as read." / "Removed from briefing." / "Extended for 7 more days (new expiry: {date})." *Blocks*: nothing (final copy can be tuned during widget UI task).

---

*AI-optimized specification. Original design: [design.md](design.md). Generated by `/design-to-spec` 2026-06-24.*
