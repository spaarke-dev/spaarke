# R4 Coordination Risks

> **Logged**: 2026-06-25 during `/project-pipeline` Step 1.5 overlap detection + canonical-path verification
> **Owner**: ralph.schroeder@hotmail.com
> **Status**: Active — referenced by W2 tasks before merge

---

## R1: R3 PR #451 File Overlap (11 files in `Spaarke.DailyBriefing.Components/`)

### Summary

The R3 sibling project's PR (`#451`, branch `work/spaarke-daily-update-service-r3`) is OPEN and unmerged as of 2026-06-25. It modifies 11 files inside `src/client/shared/Spaarke.DailyBriefing.Components/` that R4 will also modify. R4 develops in parallel per spec line 305 ("R3 PR #451 stays in draft and merges separately (R4 is independent; can be developed in parallel and merged in any order)"), accepting a conflict-resolution pass when whichever PR lands second is rebased.

### Overlapping Files (R3 PR #451 ∩ R4 W2 surface)

**Components**:
- `src/client/shared/Spaarke.DailyBriefing.Components/src/components/ActivityNotesSection.tsx`
- `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx`
- `src/client/shared/Spaarke.DailyBriefing.Components/src/components/NarrativeBullet.tsx`

**Hooks**:
- `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useBriefingActions.ts`
- `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/index.ts`

**Services**:
- `src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts`
- `src/client/shared/Spaarke.DailyBriefing.Components/src/services/index.ts`

**Types**:
- `src/client/shared/Spaarke.DailyBriefing.Components/src/types/notifications.ts`

**Tests**:
- `src/client/shared/Spaarke.DailyBriefing.Components/test/DailyBriefingApp.smoke.test.tsx`
- `src/client/shared/Spaarke.DailyBriefing.Components/test/notificationService.test.ts`
- `src/client/shared/Spaarke.DailyBriefing.Components/test/useBriefingActions.test.ts`

### R3 PR #451 Scope (what those files contain on the R3 side)

- `sprk_briefingstate` Choice column read/write logic
- Read-state derivation (replacing the prior cache-based approach)
- 3 R3 per-user actions (Check, Remove, Keep) — inline in NarrativeBullet
- BFF NotificationService TTL fix observability (but TTL fix code itself lives at `CreateNotificationNodeExecutor.cs:490`)

### R4 W2 Scope (what R4 changes in the same files)

- `NarrativeBullet.tsx`: replace inline 5-icon row with three-dot overflow menu (FR-18); the 3 R3 actions MOVE INTO the overflow menu (behavior preserved, presentation changes)
- `ActivityNotesSection.tsx`: empty-narrative fallback rendering (FR-16); wire overflow callbacks
- `DailyBriefingApp.tsx`: wire `autoPopup` workspace launcher hook (FR-17d); pass new overflow callbacks
- `useBriefingActions.ts`: may need to expose action surfaces consumed by overflow menu (TBD per task 046 design)
- `notificationService.ts`: wire preferences server-side (`timeWindow` `createdon`, `dueWithinDays`, `disabledChannels` `sprk_category not in (…)`) per FR-17a/b/c
- `types/notifications.ts`: remove `minConfidence` / `AiConfidenceThreshold` types per FR-17e
- Tests: update + add for all the above

### Mitigation

1. **Per-PR conflict-check**: Each W2 task wrap (tasks 036, 049) MUST run `/conflict-check` against R3 PR #451 before opening the R4 PR.
2. **Rebase whichever lands second**: If R3 PR #451 merges first, rebase R4's W2 PRs on the merged state and resolve. If R4 merges first, R3 PR #451 author rebases their PR.
3. **R3 deliverables preserved**: R4 spec explicitly preserves R3 schema + TTL + read-state + 3 actions (now via overflow menu) — there's no behavioral regression intended; conflicts will be presentation-vs-behavior at git-line level.
4. **W2 task awareness**: Tasks 033, 034, 040–048 each load this `notes/risks.md` and check whether the R3 PR has merged before starting.

### Verification when this risk closes

- [ ] EITHER R3 PR #451 has merged to master AND R4 W2 PRs rebased on the merged state
- [ ] OR R4's PR 4 and PR 5 have merged to master AND R3 PR #451 rebased and re-opened

---

## R2: Spec Path Correction — `NotificationService.cs` does not exist

### Summary

R4 spec.md cites the R3 BFF TTL fix as living at `src/server/api/Sprk.Bff.Api/Services/Ai/NotificationService.cs` (spec line 64 "R3 BFF fix (`ttlinseconds = 604800` in `NotificationService.cs`) — preserved" and §Affected Areas implicitly). That file does not exist at the cited path.

Pre-flight verification confirmed the TTL fix actually lives in `BuildNotificationEntity` at:
- **Actual path**: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs:490`

### Impact

- Task descriptions must reference the correct path
- No code change in R4 is needed for this — TTL is already correct in tree at line 490
- Documentation note only

### Mitigation

- CLAUDE.md "Decisions Made" section captures the correction
- All R4 tasks reference `CreateNotificationNodeExecutor.cs:490` when discussing TTL preservation
- No update to spec.md required (it's frozen as the design artifact); risks.md serves as the addendum

---

## R3: `sprk_playbookconsumer` Dispatch Path Unknown Until Investigation

### Summary

R4 FR-12 (PR 4, task 030) explicitly defers the `/narrate` dispatch path decision to implementation time. The `sprk_playbookconsumer` entity + service was shipped in `work/spaarke-ai-platform-chat-routing-redesign-r1` Phase 1R and is used in `WorkspaceFileEndpoints` routing. R4 task 030 evaluates extending it for widget-as-consumer pattern.

### Possible Outcomes

- **Path A (preferred)**: `sprk_playbookconsumer` supports daily-briefing widget payload shape → use it
- **Path B (fallback)**: `sprk_playbookconsumer` doesn't fit → direct `AnalysisOrchestrationService` invocation with a degenerate playbook (no Dataverse query node; just LLM + Tool)

### Mitigation

- Task 030 investigates first; documents decision + rationale per AC-12c
- Either path satisfies FR-12 (a thin wrapper that dispatches to `DAILY-BRIEFING-NARRATE` is the binding requirement; the specific routing mechanism is implementation detail)

---

## Status Summary

| Risk | Status | Resolved When |
|------|--------|---------------|
| R1: R3 PR #451 overlap | Active | Either PR lands first + the other rebases |
| R2: NotificationService.cs path | Resolved (documentation) | Captured in CLAUDE.md + this file |
| R3: `sprk_playbookconsumer` dispatch | Active | Task 030 decision recorded |
