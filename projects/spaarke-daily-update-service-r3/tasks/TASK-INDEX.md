# Task Index — Daily Briefing R3

> **Project**: spaarke-daily-update-service-r3
> **Generated**: 2026-06-24 by `/project-pipeline`
> **Total Tasks**: 7
> **Critical Path** (with parallel): max(001, 010, 020) → 030 → 031 → 040 → 090 (~5 wall-clock waves)

---

## Status Legend

- 🔲 Not started
- 🔄 In progress
- ✅ Complete
- ⛔ Blocked

---

## Task Registry

| # | ID | Title | Phase | Status | Hours | Tags | Rigor |
|---|----|----|----|---|---|---|---|
| 1 | [001](001-add-sprk-briefingstate-choice-column.poml) | Add `sprk_briefingstate` Choice column + deploy | Phase 1: Schema | ✅ | 0.5 | dataverse, schema, operator | STANDARD |
| 2 | [010](010-bff-fix-ttlinseconds-field-name.poml) | BFF fix: `ttlindays` → `ttlinseconds` + test + §10 verification | Phase 2: BFF Producer Fix | ✅ | 0.5 | bff-api, csharp, defect-fix | FULL |
| 3 | [020](020-widget-service-layer-read-state-swap.poml) | Widget service: read-state swap + 3 new functions + filter + tests | Phase 3: Widget Service | ✅ | 2 | frontend, typescript, refactoring | FULL |
| 4 | [030](030-use-briefing-actions-hook-extension.poml) | `useBriefingActions` hook: 3 new handlers + tests | Phase 4a: Widget Hook | ✅ | 1 | frontend, react, hook | FULL |
| 5 | [031](031-widget-ui-three-action-buttons.poml) | Widget UI: 3 action buttons + props wiring + handler composition | Phase 4b: Widget UI | ✅ | 2 | frontend, react, fluent-ui | FULL |
| 6 | [040](040-manual-uat-spaarkedev1.poml) | Manual UAT in spaarkedev1: verify 7 ACs | Phase 5: UAT | ✅ | 0.5 | uat, manual-test | STANDARD |
| 7 | [090](090-project-wrap-up.poml) | Wrap-up: lessons-learned + status + archive | Phase 5: Wrap-up | ✅ | 1 | wrap-up, documentation | MINIMAL |

**Total estimated effort**: 7.5 hours (engineering + operator + UAT + wrap-up)

---

## Dependency Graph

```
        ┌─ 001 (schema)  ─┐
        ├─ 010 (BFF fix) ─┤
Wave 1: │  (parallel)     │
        └─ 020 (widget    ─┘
            service)       ↓
                       030 (hook)
                            ↓
                       031 (UI wiring)
                            ↓
                       040 (UAT — needs all)
                            ↓
                       090 (wrap-up)
```

### Blocking Edges

| Task | Blocked By | Blocks |
|---|---|---|
| 001 | — | 040 (UAT runtime) |
| 010 | — | 040 |
| 020 | — | 030, 040 |
| 030 | 020 | 031, 040 |
| 031 | 030 | 040 |
| 040 | 001, 010, 020, 030, 031 | 090 |
| 090 | 040 | — |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| **Wave 1** | 001, 010, 020 | none (Wave 1 is the entry) | 3 concurrent agents. 001 = operator (schema); 010 = BFF (1-line fix); 020 = widget service layer. All touch independent surfaces; no file conflicts. **Max parallelism opportunity.** |
| **Wave 2** | 030 | Wave 1 (specifically 020) | Single agent — hook extension. BLOCKED BY 020 (needs new service functions). |
| **Wave 3** | 031 | Wave 2 (030) | Single agent — UI wiring. BLOCKED BY 030 (needs new hook handlers). |
| **Wave 4** | 040 | Waves 1–3 + schema deployed in spaarkedev1 | Single agent (manual UAT). Cannot start until all code merged AND schema deployed. |
| **Wave 5** | 090 | Wave 4 (040) | Single agent — wrap-up. |

### How to Execute Wave 1 in Parallel

When the user says "start" or "begin", send ONE message with THREE `Skill` tool invocations:

```
Skill(skill="task-execute", args="tasks/001-add-sprk-briefingstate-choice-column.poml")
Skill(skill="task-execute", args="tasks/010-bff-fix-ttlinseconds-field-name.poml")
Skill(skill="task-execute", args="tasks/020-widget-service-layer-read-state-swap.poml")
```

This runs 3 task-execute instances concurrently. They modify completely different surfaces (Dataverse / BFF C# / Widget TS), so no file conflicts.

**Build verification between waves**: After Wave 1 completes, main session MUST verify:
- `dotnet build src/server/api/Sprk.Bff.Api/` succeeds (task 010 touched .cs)
- `npm run build` in `src/client/shared/Spaarke.DailyBriefing.Components/` succeeds (task 020 touched .ts)
- Schema deployment record exists in `notes/schema-deployment.md` (task 001)

If any wave member fails, mark 🔄 (needs retry), report at wave end, then decide whether to retry sequentially or escalate.

---

## Critical Path Analysis

**Longest dependency chain** (without parallelization): 001 → 030 → 031 → 040 → 090 (5 hops)

**Wall-clock with parallel Wave 1**: max(001 ~30min, 010 ~30min, 020 ~2h) ≈ 2h → 030 (~1h) → 031 (~2h) → 040 (~30min) → 090 (~1h) = **~6.5 hours wall-clock**

**Wall-clock fully sequential**: 0.5 + 0.5 + 2 + 1 + 2 + 0.5 + 1 = **~7.5 hours**

→ Wave 1 parallel saves ~1 hour wall-clock (~13% reduction).

---

## High-Risk Items

| Task | Risk | Mitigation |
|---|---|---|
| 001 | Maker portal may not accept per-column default for Choice on Microsoft-owned table | Widget null-coalesce treats null as Unread (FR-3 AC-3c handles this regardless); document fallback in `notes/schema-deployment.md` |
| 010 | BFF publish-size delta > +0.1 MB | Very high confidence delta is ≤+0.01 MB (1-line change); verify per §10 NFR-01 rule and report in `notes/bff-size-check.md` |
| 020 | Stale `toasttype: 200000000` literals in existing widget tests | Task includes explicit sweep step (Step 9) |
| 031 | Dark-mode regression from Fluent v9 raw color usage | Task includes semantic-token verification step (Step 8) and dark-mode visual check (Step 9) |
| 040 | UAT surfaces an AC failure | File a fix task (e.g., `041-fix-{slug}.poml`) before proceeding to 090 |

---

## Progress Notes

*Updated as tasks complete*

- 2026-06-24: Project initialized; 7 tasks generated; PR #451 (draft) opened.
- 2026-06-24: Task 001 ✅ — `sprk_briefingstate` Choice (Unread=0/Checked=1/Removed=2) deployed to spaarkedev1 in `SpaarkeCore` solution via Web API. All 3 ACs verified including AC-6 default propagation (Dataverse DID honor default=0 for new rows — better than Risk R5 worst case). See `notes/schema-deployment.md`.
- 2026-06-24: Task 030 ✅ — `useBriefingActions` extended with `markChecked` / `markRemoved` / `extendTtl` handlers (FR-4/5/6). Each orchestrates `onOptimistic` → service call → `onSuccess` OR (`onRevert` → `onError`) via a `BriefingActionOptions<TSuccess>` callback bag. Hook stays JSX-agnostic (toast JSX construction stays at UI layer per existing `handleAddToTodo` pattern). Transitional aliases `markNotificationRead` / `markAllNotificationsRead` removed from `notificationService.ts` + `services/index.ts`. 79/79 tests pass (10 new). Existing 5 pre-existing peer-dep build errors (@spaarke/ui-components, @spaarke/auth) confirmed baseline — none introduced. ADR-check + code-review = 0 violations / 0 warnings.
- 2026-06-25: Tasks 010 ✅ + 020 ✅ + 031 ✅ executed. BFF: `NotificationService.cs` ttlinseconds fix + xUnit test + §10 verification (47.86 MB compressed, ~0 MB delta, no new HIGH CVE). Widget service: read-state swap + EXCLUDE_REMOVED_FILTER + 4 new exports (`markBriefingChecked` / `markAllBriefingsChecked` / `markBriefingRemoved` / `extendBriefingTtl`); 24 new jest tests; 7764/7764 BFF tests pass. UI: 3 new R3 buttons in NarrativeBullet using owner-specified icons; jsdom v30 `Response` shim fix; 80/80 widget tests pass. FR-6 follow-up: `ttlinseconds` propagation through types → NOTIFICATION_SELECT → toNotificationItem → NarrativeBullet for additive Keep +7d math; +3 jest tests; 83/83 widget pass.
- 2026-06-25: 4 parallel deploys to spaarkedev1 — BFF (`https://spaarke-bff-dev.azurewebsites.net`), DailyBriefing standalone (`sprk_dailyupdate`), SpaarkeAi (`sprk_spaarkeai`); LegalWorkspace correctly refused (retired per OC-R4-05). DailyBriefing solution config fixed during deploy (3 pre-existing config gaps: tsconfig path aliases, deps, vite alias — mirroring SpaarkeAi/LegalWorkspace patterns).
- 2026-06-25: UAT (task 040) — **AC-3a confirmed**: widget renders content with 28 unread notifications (was 0 pre-R3); 3 new R3 buttons render in correct order. Broader UAT findings (hallucinations, dead preferences, Activity Notes disappearing on refresh, UX icon collision) surfaced — R2-inherited bugs; R3 explicitly out-of-scoped them. **Task 040 treated complete per owner 2026-06-25**: AC-3a (the headline bug fix) confirmed; AC-4/5/6/7a/7b covered by unit tests; manual click-through deferred to R4 UAT with redesigned UX.
- 2026-06-25: R4 design + spec authored (`projects/spaarke-daily-update-service-r4/`) capturing UAT findings + JPS deployment gap discovery (`mcp__dataverse__read_query` confirmed 0 deployed `sprk_analysisaction` rows for ActionType 52 despite C# code shipped from platform-foundations-R3).
- 2026-06-25: Task 090 ✅ — Wrap-up complete. `notes/lessons-learned.md` authored (R3 journey + R4 trigger findings + cross-project observations). README + current-task + TASK-INDEX updated to project-complete state. Final code-review at branch level: PASS WITH NOTES (0 critical, 3 warnings as architectural follow-ups). Master merged into R3 branch cleanly (no conflicts). PR #451 ready for merge.

---

## Quick Links

- [Project README](../README.md)
- [Project Plan](../plan.md)
- [Spec](../spec.md)
- [Design](../design.md)
- [CLAUDE.md (AI context)](../CLAUDE.md)
- [Current Task State](../current-task.md)
