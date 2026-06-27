# Daily Briefing — Read-State Decoupling + Producer TTL Hardening (R3)

> **Project**: spaarke-daily-update-service-r3
> **Status**: Design
> **Predecessor**: [`spaarke-daily-update-service-r2`](../spaarke-daily-update-service-r2/) (R2 — Pattern D widget migration + producer-fixes Phases A/B)
> **Created**: 2026-06-24
> **Author**: UAT-driven design session, 2026-06-24 (post-`spaarke-platform-foundations-r3` master deploy)

---

## Executive Summary

UAT against the post-`spaarke-platform-foundations-r3` master deploy surfaced a Daily Briefing widget defect: notifications exist in the user's native bell panel but the widget renders the empty "You're all caught up!" state. Root cause is a semantic mismatch on the Dataverse `appnotification.toasttype` field — the playbook engine writes `toasttype = 200000000` (Microsoft's "Timed" toast-display value) while the widget reads `toasttype === 200000000` as "Dismissed/Read." Every notification arrives pre-marked-read in the widget's eyes; `totalUnreadCount = 0`; EmptyState renders.

The fix has two pieces. First, decouple the widget's read-state from `toasttype` (which is a display-behavior field, not a read-state field) by introducing a custom `sprk_briefingstate` option-set scoped to the Daily Briefing surface only. This also gives users the briefing-specific actions they need (check off, remove, keep visible) without affecting the native bell-panel lifecycle. Second, fix a parallel producer-side defect where `NotificationService.cs` writes to a non-existent field name (`ttlindays`) instead of the canonical `ttlinseconds`, causing those notifications to fall back to the tenant-default 14-day TTL silently.

R3 is **consumer-layer + minor producer-fix** work only. The R2 producer migration is healthy. The R3 platform-foundations work (membership resolution, scheduling library) is independent and unaffected.

---

## Problem Statement

### What the user sees today (UAT, 2026-06-24)

1. Daily Briefing widget renders `EmptyState` ("You're all caught up! · No unread notifications. New activity across your matters and projects will appear here automatically.") even when the user has multiple unread `appnotification` records.
2. The native bell-icon panel (right rail) shows the same user's notifications correctly — "Due soon: Task," "Due soon: Event New Matter Created," etc.
3. Adding `assignedTo` values on the source records does not surface them in the widget — confirmed not a membership / association issue.
4. Behavior is consistent across notification categories (tasks, events, work assignments).

### What's broken (root causes, verified)

| # | Defect | Where | Verified by |
|---|---|---|---|
| **D1** | Daily Briefing widget interprets `toasttype === 200000000` as "Dismissed/Read." Per Microsoft Learn, `200000000` is the canonical "Timed" toast-display value (visible toast that auto-dismisses) — it has nothing to do with read state. Producer ([`CreateNotificationNodeExecutor.cs:62`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs#L62)) writes this value as the documented default, so every notification arrives pre-marked-read in the widget. `totalUnreadCount === 0` → [`DailyBriefingApp.tsx:327`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx#L327) renders `EmptyState`. | [`notificationService.ts:123`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts#L123) (widget read); [`CreateNotificationNodeExecutor.cs:62`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs#L62) + `:488` (producer write); [Microsoft Learn — Send in-app notifications](https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/send-in-app-notifications) ("Toast Type · Timed = 200000000") | Code inspection + Microsoft Learn doc citation provided by owner, 2026-06-24. |
| **D2** | `NotificationService.cs` writes TTL to a non-existent field name `ttlindays = 7`. The canonical OOB field is `ttlinseconds` (Microsoft Learn). The write silently fails for that field; the notification falls back to the tenant-default 14-day TTL. Asymmetric with `CreateNotificationNodeExecutor.cs:490` which correctly writes `ttlinseconds = 604800`. | [`NotificationService.cs:105-106`](../../src/server/api/Sprk.Bff.Api/Services/NotificationService.cs#L105) | Microsoft Learn — `appnotification` table reference column "Expiry (seconds) · `TTLInSeconds`"; no `ttlindays` column documented. |
| **D3** | The native bell-panel and the widget have different intended lifecycles, but both currently couple to the same field (`toasttype`). When users dismiss in the bell, they shouldn't lose the item from their daily review. The bell is real-time; the briefing is rhythmic. The two surfaces need independent read-state. | Architectural — `notificationService.ts:273` writes `toasttype: 200000000` on dismiss; same field that drives bell-panel display | Owner clarification 2026-06-24: "the Notifications are meant as the system generated messages — whereas Daily Briefing is more of a daily report — has same information but different viewport." |
| **D4** | Daily Briefing users have no way to extend visibility for an item they want to keep on their next briefing. Notifications either persist passively until TTL or get dismissed. The "I'll deal with this next week" intent has no UX affordance. | Widget UI — only existing item action is "Add to To Do" | Owner-stated user behavior, 2026-06-24: "they want to indicate if something has been read yet ('check it off') or remove it or keep it on their Briefing (in that regard we need to have a way to extend its TTL?)" |

### What was already verified working (out of scope)

| ✓ | Component | Verified |
|---|---|---|
| ✓ | Notification producer pipeline (playbook engine + 4 inline writers) | UAT screenshot shows live notifications in user's bell — production end-to-end is firing |
| ✓ | `appnotification.ownerid` resolution (right user receives right notifications) | UAT user sees their own notifications, scoped via producer-side recipient resolution |
| ✓ | R3 platform-foundations membership resolution | Independent code path; widget does not call membership service. Verified: widget queries `appnotification` directly via `Xrm.WebApi.retrieveMultipleRecords` ([notificationService.ts:159](../../src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts#L159)) |
| ✓ | R3 `BriefingService.cs` top-priority-matter fix | Different surface (workspace briefing card, not the notification widget) |
| ✓ | Pattern D widget migration from R2 | Widget UI structure correct; defect is in field-semantic layer, not architecture |

---

## Architecture Context

### Two surfaces, two lifecycles (the design goal)

```
┌─ NATIVE BELL PANEL (right rail) ───────────────────────────────────────┐
│                                                                        │
│  Source: appnotification rows where ownerid = currentUser              │
│  Read state: native `isread` boolean field (Power Apps managed)        │
│  Lifecycle: real-time — user dismisses → removed from panel display    │
│  Cleanup: TTL purge (ttlinseconds, Dataverse-platform-managed)         │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘

┌─ DAILY BRIEFING WIDGET (workspace pane) ───────────────────────────────┐
│                                                                        │
│  Source: same appnotification rows where ownerid = currentUser         │
│  Read state: NEW `sprk_briefingstate` option-set (Unread/Checked/      │
│              Removed) — Daily Briefing-specific, independent of bell   │
│  Lifecycle: rhythmic — user processes daily, items persist past bell-  │
│             dismissal up to TTL, can be extended +7 days at a time     │
│  Cleanup: same TTL purge (extended TTL → longer visibility)            │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

The two surfaces read the same underlying `appnotification` rows but apply different per-surface filters. Dismissing in the bell does NOT affect the briefing; checking off in the briefing does NOT affect the bell. This matches the owner's user-model: bell = real-time message tray; briefing = daily report.

### Field semantics (corrected per Microsoft Learn)

| Field | Type | Canonical purpose | What R2/R3 code thought it meant | Correct meaning |
|---|---|---|---|---|
| `toasttype` | Option-set | Toast display behavior | "Dismissed/Read marker" (wrong) | `100000000`=Hidden / `200000000`=Timed |
| `isread` | Boolean | Bell-panel read state | (not used by widget) | Native Power Apps read-receipt |
| `ttlinseconds` | Integer | Auto-deletion timer (seconds from `createdon`) | `NotificationService.cs` used `ttlindays` instead (silent fail) | Authoritative cleanup field; tenant default 14 days when unset |
| `ttlindays` | (doesn't exist) | — | Field name fabricated in producer code | N/A |
| `sprk_briefingstate` (NEW) | Option-set | Daily Briefing per-item state | — | `0`=Unread / `1`=Checked / `2`=Removed; widget-scoped only |

---

## Solution Approach

### One custom field + four small code changes + three UI buttons

**Dataverse schema (operator-driven, 1 field)**

Add `sprk_briefingstate` to `appnotification`:
- Type: Option-set (Choice)
- Values: `Unread = 0` (default), `Checked = 1`, `Removed = 2`
- Default value applies at row create — producer code does not need to write this field; new rows arrive with `sprk_briefingstate = 0`. Existing rows that lack the field treat null-on-read as `Unread` per widget logic.

**BFF producer fixes**

1. [`NotificationService.cs:106`](../../src/server/api/Sprk.Bff.Api/Services/NotificationService.cs#L106) — change `entity["ttlindays"] = 7` to `entity["ttlinseconds"] = 604800` (canonical 7 days in seconds). Aligns with the playbook executor that already does the right thing.
2. [`CreateNotificationNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs) — no change. Already correct.

Both writers continue to write `toasttype = 200000000` (Timed) as the canonical display default. The widget no longer relies on `toasttype` for read state, so this is now harmless.

**Widget consumer (notificationService.ts)**

- Add `sprk_briefingstate` to `NOTIFICATION_SELECT`.
- Change `isRead` derivation from `(entity['toasttype'] as number) === 200000000` to `(entity['sprk_briefingstate'] as number ?? 0) === 1`.
- Add server-side filter to exclude removed: `&$filter=sprk_briefingstate ne 2 or sprk_briefingstate eq null`.
- Replace `markNotificationRead(id)` body: write `{ sprk_briefingstate: 1 }` instead of `{ toasttype: 200000000 }`.
- Replace `markAllNotificationsRead` body: same swap.
- Add `markNotificationRemoved(id)`: writes `{ sprk_briefingstate: 2 }`.
- Add `extendNotificationTtl(id, currentTtlSeconds, addSeconds)`: writes `{ ttlinseconds: currentTtlSeconds + addSeconds }`. Default `addSeconds = 604800` (7 days).

**Widget UI (per-item action buttons)**

Three icon buttons appear on each `NarrativeBullet` (or equivalent item card), in order:

| Action | Icon | Tooltip | Backend write |
|---|---|---|---|
| Check off | `CheckmarkRegular` | "Mark as read" | `sprk_briefingstate = 1` |
| Remove | `DismissRegular` | "Remove from briefing" | `sprk_briefingstate = 2` |
| Keep 7 more days | `CalendarAddRegular` | "Keep on briefing for 7 more days" | `ttlinseconds = current + 604800` |

The existing "Add to To Do" action (4th button) remains. Optimistic UI update on click; toast confirms success or surfaces failure.

**Bell-panel decoupling**

The widget no longer reads or writes `isread` or `toasttype`. The native bell panel continues to use `isread` (Power Apps managed) for its read state, fully independent. Dismissing in the bell does not affect the widget; checking off in the widget does not affect the bell. This is the intended decoupling per the owner's user-model.

---

## Scope

### In Scope (R3)

- Add `sprk_briefingstate` custom option-set to `appnotification` in spaarkedev1; deploy to higher envs via solution import
- Fix `NotificationService.cs` `ttlindays` → `ttlinseconds` (D2)
- Widget service layer: switch read-state field; add 3 new action functions
- Widget UI: 3 new per-item action buttons + optimistic update + toast wiring
- Unit tests: widget service hook tests for the 3 new actions + jest fixture coverage for `sprk_briefingstate` field values
- BFF unit test update for `NotificationService` (verifies `ttlinseconds` written, not `ttlindays`)
- Manual UAT in spaarkedev1: verify briefing populates with current user's notifications; verify each of the 3 actions works; verify bell-panel state is unaffected by widget actions

### Out of Scope

- **Weekend-aware TTL calculation** — explicitly deferred. Owner is building a more robust due-date engine that handles weekends, holidays, time zones; that engine will own all date math when ready.
- **Widget-side matter-scope filtering** — unnecessary. The producer side already determines recipients via R3 membership resolution; trusting that resolution is correct. Adding a second filter would be redundant or introduce divergence bugs.
- **Backfill of existing notifications** — not needed. `sprk_briefingstate = null` on existing rows is treated as `Unread` by the widget's null-coalescing read; new notifications get the Dataverse-level default `0 = Unread`.
- **Changes to the native bell panel** — owner-confirmed: bell and briefing have different lifecycles by design.
- **TTL admin override per category / per-user preferences** — defer to a future enhancement; flat 7-day default + 7-day extensions is sufficient for R3.
- **Producer-side category-specific TTL tuning** — current 7-day default is empirically working post-c55c7a5fe fix; not revisiting.

### Explicitly NOT Changing

- `appnotification.ownerid` resolution (R3 platform-foundations membership service)
- `CreateNotificationNodeExecutor` ActionType 50 / playbook engine
- BFF `/narrate` endpoint contract
- Pattern D dual-use architecture (R2 deliverable)
- `@spaarke/daily-briefing-components` package boundary
- Standalone Daily Briefing code page (`sprk_dailyupdate`)
- ADR-024 sprk_todo regarding catalog (existing `useInlineTodoCreate` works as-is)

### Affected Areas

| Path | Purpose |
|---|---|
| Dataverse `appnotification` table | Add `sprk_briefingstate` Choice column |
| [`src/server/api/Sprk.Bff.Api/Services/NotificationService.cs`](../../src/server/api/Sprk.Bff.Api/Services/NotificationService.cs) | Fix `ttlindays` → `ttlinseconds` |
| [`src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts) | Switch read-state field; add 3 new action functions |
| [`src/client/shared/Spaarke.DailyBriefing.Components/src/types/notifications.ts`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/types/notifications.ts) | Add `sprk_briefingstate` to entity type + actions enum |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/components/ActivityNotesSection.tsx` + `NarrativeBullet.tsx` | Add 3 new action buttons + handlers |
| [`src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx) | Wire new handlers through hook composition |
| `tests/unit/Sprk.Bff.Api.Tests/Services/NotificationServiceTests.cs` (or equivalent) | Verify `ttlinseconds` field write |
| `src/client/shared/Spaarke.DailyBriefing.Components/test/` | Update existing tests + add coverage for 3 new actions + filter |

---

## Requirements

### Functional Requirements

**FR-1 — Add `sprk_briefingstate` schema field**
- Custom Choice column on `appnotification`: `Unread = 0` (default), `Checked = 1`, `Removed = 2`
- Deployed to spaarkedev1 via solution
- Default value `0` applies on row create at the Dataverse level
- **AC-1**: Querying `appnotification?$select=sprk_briefingstate` returns the column without error; new rows show value `0` without explicit producer write

**FR-2 — Fix producer-side TTL field name (D2)**
- `NotificationService.CreateNotificationAsync` writes `ttlinseconds = 604800` instead of `ttlindays = 7`
- **AC-2**: Creating a notification via `NotificationService` results in an `appnotification` row with `ttlinseconds = 604800`; existing rows are unaffected

**FR-3 — Widget read-state switches off `toasttype`**
- `fetchNotifications` selects `sprk_briefingstate`
- `toNotificationItem` derives `isRead` from `(entity['sprk_briefingstate'] as number ?? 0) === 1`
- `fetchNotifications` includes filter `(sprk_briefingstate ne 2 or sprk_briefingstate eq null)` to exclude removed
- **AC-3a**: Widget renders unread items as unread regardless of `toasttype` value
- **AC-3b**: Notifications with `sprk_briefingstate = 2` (Removed) do not appear in the widget
- **AC-3c**: Notifications without the field (null) render as Unread (handles pre-rollout existing rows)

**FR-4 — "Check off" action**
- New per-item button using `CheckmarkRegular` icon, tooltip "Mark as read"
- Click → optimistic UI update → `webApi.updateRecord('appnotification', id, { sprk_briefingstate: 1 })` → success toast or error toast on failure
- Item moves from Unread to Read in the widget; bell-panel state is unaffected
- **AC-4**: After clicking Check, the notification's `sprk_briefingstate = 1` in Dataverse and the widget renders it as read

**FR-5 — "Remove from briefing" action**
- New per-item button using `DismissRegular` icon, tooltip "Remove from briefing"
- Click → optimistic UI update → `webApi.updateRecord('appnotification', id, { sprk_briefingstate: 2 })` → success/error toast
- Item disappears from widget; bell-panel and the underlying record are unaffected
- **AC-5**: After clicking Remove, the notification's `sprk_briefingstate = 2` in Dataverse and the widget does not re-render it on refresh

**FR-6 — "Keep 7 more days" action**
- New per-item button using `CalendarAddRegular` icon, tooltip "Keep on briefing for 7 more days"
- Click → optimistic UI update → fetch current `ttlinseconds` → compute new value (current + 604800) → `webApi.updateRecord('appnotification', id, { ttlinseconds: newValue })` → success toast showing new effective expiry date / error toast on failure
- Item's auto-purge date is pushed out 7 calendar days; bell-panel TTL also extends (acceptable per design — TTL is shared cleanup, not display state)
- **AC-6**: After clicking Keep, the notification's `ttlinseconds` is increased by 604800 and the new effective expiry date is reflected in any displayed metadata

**FR-7 — Bell-panel decoupling verified**
- Widget never reads or writes `toasttype` or `isread`
- **AC-7a**: User dismisses a notification in the native bell panel → item still appears in widget
- **AC-7b**: User checks off a notification in the widget → item still appears in native bell panel until user separately dismisses there

### Non-Functional Requirements

- **NFR-01**: No new HIGH-severity CVE introduced
- **NFR-02**: BFF publish-size delta ≤ +0.1 MB (no NuGet adds; trivial code change)
- **NFR-03**: Unit + integration tests cover all 6 FRs; widget jest tests use `jest-environment-jsdom`
- **NFR-04**: Widget action latency: optimistic UI update ≤16ms (single React render), backend write ≤300ms p95 over Power Apps `Xrm.WebApi`
- **NFR-05**: Backward compatible with existing notifications (null `sprk_briefingstate` treated as Unread; no backfill required)

---

## Owner Clarifications (Resolved 2026-06-24)

| # | Question | Owner Decision | Rationale |
|---|---|---|---|
| Q1 | Use native `statecode`/`statuscode` as the read marker? | **No** — `appnotification` doesn't have those columns. Use a custom field. | Empirical: maker portal inspection confirmed the OOB schema lacks these. Owner offered to add custom fields. |
| Q2 | Should Daily Briefing read state mirror bell-panel read state? | **No** — they're intentionally different surfaces with different lifecycles. | Owner: "Notifications are meant as system generated messages — whereas Daily Briefing is more of a daily report — same information but different viewport." |
| Q3 | TTL extension — date picker or fixed +N days? | **Fixed +7 days, one-click, `CalendarAddRegular` icon.** | Simpler UX; matches briefing's rhythmic (not calendar-pinned) model. |
| Q4 | Should "Keep 7 more days" be weekend-aware (push to Monday if expiry lands on weekend)? | **No** — out of scope. | Owner is building a robust due-date engine that handles weekends, holidays, timezones. Daily Briefing uses literal 7-day extensions until that engine ships. |
| Q5 | Should the widget also scope notifications to user's matter associations (R3 membership integration)? | **No** — unnecessary and risky. | Producer side already determines recipients via membership resolution. Widget filtering would either be redundant (same answer) or introduce divergence bugs. Trust the producer's per-recipient row creation. |
| Q6 | Does "Add to To Do" also mark the briefing item as Checked? | **Deferred to UAT** — implement as separate actions for R3; revisit if users ask. | Avoid coupling actions until we see real usage. |
| Q7 | TTL value mutation on update — does Dataverse honor it? | **Per Microsoft Learn, yes** — `ttlinseconds` is a writable field; updating extends total lifespan from `createdon`. | Cited by owner from Microsoft Learn `appnotification` documentation. |

---

## Risks & Mitigations

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Dataverse rejects post-create `ttlinseconds` update | Low | Medium | Verified in Microsoft Learn that field is writable on the entity. If verification surfaces an issue, fallback design: add a `sprk_briefingttlextended` custom DateTime field and have the widget compute `effectiveExpiry = max(createdon + ttlinseconds, sprk_briefingttlextended)`. |
| Existing notifications (pre-rollout) without `sprk_briefingstate` value | Low | Low | Widget null-coalescing read treats null as Unread; verified in FR-3 AC. No backfill needed. |
| Producer-side notifications hand-written by non-Spaarke code paths set `sprk_briefingstate` explicitly | Low | Low | Field is in `sprk_*` custom namespace — only Spaarke code would know to write it. Native Microsoft notifications won't touch it. |
| Native bell-panel display state confusion (user dismisses in bell, item still in briefing → "why didn't it go away?") | Medium | Low | Brief UX caption near the briefing header: "Your daily summary — independent of system notifications." Defer if scope creep, but flag for UAT. |
| Test fixture coverage for `sprk_briefingstate` requires new jest mock entity shape | Low | Low | Add to existing `__mocks__/spaarke-auth.ts` and notification fixture builders. ~30 min. |
| Widget tests already mock `webApi.updateRecord`; risk of stale assertion on `toasttype` value | Medium | Low | Sweep all `notificationService.test.ts` + `useBriefingActions.test.ts` for `toasttype: 200000000` literals; replace with `sprk_briefingstate: 1`. |

---

## Acceptance Criteria Summary

R3 graduates when all of the following pass:

- [ ] All 7 FRs (FR-1 through FR-7) deliver per spec
- [ ] All 7 corresponding ACs (AC-1 through AC-7b) pass
- [ ] All 5 NFRs pass
- [ ] Schema deployed to spaarkedev1; verified in maker portal
- [ ] BFF unit test updated and passes
- [ ] Widget jest tests updated and pass
- [ ] Manual UAT in spaarkedev1 confirms each of the 3 new actions + bell-panel decoupling per AC-7a/b
- [ ] PR merged to master; deployed to dev; spot-check confirms widget populates with unread notifications

---

## Implementation Estimate

| Workstream | Effort |
|---|---|
| Dataverse: add `sprk_briefingstate` Choice column, deploy to spaarkedev1 (operator) | ~30 min |
| BFF: `NotificationService.cs` `ttlindays` → `ttlinseconds` fix + unit test | ~30 min |
| Widget service layer: read-state swap + 3 new action functions + jest tests | ~2 hours |
| Widget UI: 3 buttons + optimistic update + handler wiring + toast plumbing | ~2 hours |
| Widget hook updates: `useBriefingActions` extended for 3 actions + tests | ~1 hour |
| Manual UAT in spaarkedev1 | ~30 min |
| **Total** | **~6 hours engineering + 30 min operator** |

Single FULL-rigor day's work; no NuGet adds, no architectural changes, no multi-region coordination.

---

## References

- **Predecessor**: [`projects/spaarke-daily-update-service-r2/`](../spaarke-daily-update-service-r2/) — Pattern D widget migration (the architecture R3 builds on)
- **R3 platform-foundations**: [`projects/spaarke-platform-foundations-r3/`](../spaarke-platform-foundations-r3/) — independent; provides producer-side recipient resolution that this project trusts
- **Microsoft Learn — Send in-app notifications**: https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/send-in-app-notifications (cited by owner 2026-06-24 for `ttlinseconds` + `toasttype` semantics)
- **Microsoft Learn — `appnotification` table reference**: column `TTLInSeconds` — "The number of seconds from when the notification should be deleted if not already dismissed"
- **Prior TTL fix**: commit `c55c7a5fe` (Phase B — `markAllNotificationsRead` field mismatch + TTL 3d→7d) — same kind of field-semantic mismatch bug, different field. This project completes the cleanup.
- **CLAUDE.md §10**: BFF Hygiene binding governance — applies to `NotificationService.cs` change (BFF-touching). Placement Justification: change is a 1-line bug fix in existing service; no new component or interface; constraint satisfied by minimal scope.
- **CLAUDE.md §11**: Component Justification — no new components introduced; all changes extend existing files. Constraint satisfied; no `<justification>` blocks required.

---

*Drafted 2026-06-24 from UAT-driven design session. Next step: `/design-to-spec` to produce the AI-optimized spec.md, then `/project-pipeline` for plan + tasks.*
