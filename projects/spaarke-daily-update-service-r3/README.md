# Daily Briefing — Read-State Decoupling + Producer TTL Hardening (R3)

> **Last Updated**: 2026-06-24
>
> **Status**: Ready for Tasks

## Overview

UAT against the post-`spaarke-platform-foundations-r3` master deploy surfaced a Daily Briefing widget defect: notifications exist in the user's native bell panel but the widget renders the empty "You're all caught up!" state. This project decouples the widget's read-state from `toasttype` via a new Daily-Briefing-scoped option-set field, adds three per-item user actions (Check / Remove / Keep +7d), and fixes a parallel producer-side defect where `NotificationService.cs` writes a non-existent TTL field name. R3 is **consumer-layer + minor producer-fix** work only — the R2 producer migration is healthy.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Phases, WBS, parallel groups, dependencies |
| [Design Spec](./design.md) | Human-readable design with rationale and owner clarifications |
| [Spec](./spec.md) | AI-optimized specification (7 FRs, 5 NFRs, MUST rules) |
| [Tasks Index](./tasks/TASK-INDEX.md) | Numbered POML task registry |
| [Current Task](./current-task.md) | Active task state (context recovery) |
| [CLAUDE.md](./CLAUDE.md) | AI context file |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Ready for Tasks |
| **Progress** | 0% (planning complete; 0/7 tasks done) |
| **Target Date** | TBD (estimated ~6 hours engineering + 30 min operator) |
| **Completed Date** | — |
| **Owner** | Spaarke platform team |
| **Branch** | `work/spaarke-daily-update-service-r3` |
| **PR** | [#451](https://github.com/spaarke-dev/spaarke/pull/451) (draft) |

## Problem Statement

Daily Briefing widget renders `EmptyState` ("You're all caught up!") even when the user has multiple unread `appnotification` records. Root cause is a semantic mismatch on `appnotification.toasttype`: the producer writes `200000000` (Microsoft's canonical "Timed" toast-display value) while the widget reads `=== 200000000` as "Dismissed/Read." Every notification arrives pre-marked-read in the widget's eyes; `totalUnreadCount === 0`; EmptyState renders. A parallel producer-side bug writes `ttlindays = 7` (non-existent field) instead of `ttlinseconds = 604800`, causing those notifications to fall back silently to the tenant-default 14-day TTL.

## Solution Summary

Add one custom option-set field (`sprk_briefingstate` on `appnotification`: Unread/Checked/Removed) scoped to the Daily Briefing surface only. Switch the widget's read-state derivation off `toasttype` to the new field. Add three per-item action buttons (Check / Remove / Keep +7d) using Fluent v9 icons. Fix the BFF producer line that writes the non-existent TTL field name. The native bell panel keeps using its own `isread`/`toasttype` lifecycle — fully independent of the briefing per the owner's user-model.

## Graduation Criteria

The project is considered **complete** when:

- [ ] `sprk_briefingstate` Choice column deployed to spaarkedev1 (Unread=0 default / Checked=1 / Removed=2)
- [ ] `NotificationService.CreateNotificationAsync` writes `ttlinseconds = 604800` (verified in unit test)
- [ ] Widget renders unread notifications correctly for a test user (manual UAT confirms)
- [ ] All 3 new actions work end-to-end in spaarkedev1 (Check / Remove / Keep +7d) with correct Dataverse writes and toast feedback
- [ ] Bell-panel decoupling verified: dismiss in bell ≠ remove from widget; check off in widget ≠ remove from bell
- [ ] All 7 FRs (FR-1 — FR-7) and 7 corresponding ACs (AC-1 — AC-7b) pass
- [ ] All 5 NFRs pass (no new HIGH CVE; BFF publish-size delta ≤ +0.1 MB; ≥90% line coverage on changed files; optimistic UI ≤16ms; backward compatible with null `sprk_briefingstate`)
- [ ] BFF unit test + widget jest tests pass
- [ ] PR merged to master; deployed to dev; spot-check confirms widget populates

## Scope

### In Scope

- New Choice column `sprk_briefingstate` on `appnotification` (operator-driven)
- 1-line fix in `NotificationService.cs` (`ttlindays` → `ttlinseconds`) + matching unit test
- Widget service layer: switch read-state derivation; add 3 new action functions; add server-side filter excluding Removed items
- Widget hook layer: extend `useBriefingActions` with 3 new handlers + jest tests
- Widget UI layer: add 3 action buttons to `NarrativeBullet`; wire props through `ActivityNotesSection`; handler wiring in `DailyBriefingApp.tsx` with optimistic UI + toast
- Manual UAT verifying 7 ACs

### Out of Scope

- Weekend-aware TTL calculation (future due-date engine will own date math)
- Widget-side matter-scope filtering (trust producer-side recipient resolution from R3 platform-foundations)
- Backfill of existing notifications (widget null-coalesces null → Unread)
- Changes to the native bell panel (different lifecycle by design)
- TTL admin overrides per category or per-user preferences
- Producer-side category-specific TTL tuning

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Use custom `sprk_briefingstate` option-set, not native `statecode`/`statuscode` | `appnotification` lacks those columns (owner-verified via maker portal) | — |
| Briefing read-state independent of bell-panel `isread` | Owner clarification: bell = real-time tray; briefing = daily report; different lifecycles by design | — |
| Fixed +7 days (no date picker) for "Keep on briefing" action | Simpler UX; matches briefing's rhythmic (not calendar-pinned) model | — |
| Producer keeps writing `toasttype = 200000000` | Canonically correct per Microsoft Learn ("Timed" display behavior); widget no longer reads it for state | — |
| All 3 widget actions go direct to Dataverse via `Xrm.WebApi`, no new BFF endpoints | Minimal scope; aligns with §10 BFF Hygiene | [ADR-001](../../docs/adr/ADR-001-minimal-api.md) |
| All UI uses Fluent v9 icons + tokens + dark-mode support | Spaarke convention | [ADR-021](../../docs/adr/ADR-021-fluent-design-system.md) |
| `sprk_briefingstate = null` on read treated as Unread | Backward compatibility for pre-rollout existing rows; no backfill required | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Dataverse rejects post-create `ttlinseconds` update | Med | Low | Verified writable per Microsoft Learn. Fallback: add `sprk_briefingttlextended` DateTime field and have widget compute `max(createdon + ttlinseconds, sprk_briefingttlextended)`. |
| Existing notifications without `sprk_briefingstate` value | Low | Low | Widget null-coalescing read treats null as Unread (FR-3 AC-3c). No backfill needed. |
| Native bell-panel display state confusion (user dismisses in bell, item still in briefing) | Low | Med | Brief UX caption near widget header: "Your daily summary — independent of system notifications." Flag for UAT. |
| Test fixture coverage for `sprk_briefingstate` requires new jest mock entity shape | Low | Low | Add to existing notification fixture builders (~30 min). |
| Stale `toasttype: 200000000` literal assertions in widget tests | Low | Med | Sweep `notificationService.test.ts` + `useBriefingActions.test.ts`; replace with `sprk_briefingstate: 1`. |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| spaarkedev1 environment access (for schema deploy) | Internal | Ready | Operator-driven |
| R2 Pattern D widget migration | Internal | Shipped | R3 builds on its consumer layer |
| `@spaarke/daily-briefing-components` package | Internal | Shipped (R2) | Package boundary unchanged |
| R3 platform-foundations (producer-side recipient resolution) | Internal | Shipped | Independent code path; widget trusts it |
| Microsoft `appnotification` OOB entity | External | Stable | Standard custom-column extension supported on all envs |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Spaarke platform | Overall accountability + UAT-driven design session 2026-06-24 |
| Developer | AI-assisted (Claude Code) | Implementation per POML tasks |
| Reviewer | Spaarke platform | Code review per [`code-review`](../../.claude/skills/code-review/SKILL.md) + [`adr-check`](../../.claude/skills/adr-check/SKILL.md) |
| Operator | Spaarke platform | Schema deploy to spaarkedev1 |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-06-24 | 1.0 | Initial design + spec + project artifacts | UAT-driven design session |

---

*Predecessor: [spaarke-daily-update-service-r2](../spaarke-daily-update-service-r2/) — Pattern D migration.*
*Independent sibling: [spaarke-platform-foundations-r3](../spaarke-platform-foundations-r3/) — producer-side recipient resolution.*
